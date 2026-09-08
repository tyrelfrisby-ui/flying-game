/**
 * flyinggame-relay — WebSocket state relay for multiplayer free play.
 *
 * One Durable Object per room, keyed by room code. `ffa` is the giant free-for-all (cap 200);
 * private rooms are 6-char [A-Z0-9] codes (cap 16). No auth, no persistence: the DO fans each
 * pilot's 10 Hz state packet out to everyone else in the room and remembers the last packet per
 * peer for late joiners. Uses the WebSocket Hibernation API so an idle room costs nothing.
 *
 * HTTP
 *   POST /rooms                      -> { code }            fresh 6-char private room code
 *   GET  /rooms/{code}/ws?name=&ac=  -> WebSocket upgrade   (code = ffa | [A-Z0-9]{6})
 *   GET  /rooms/{code}               -> { code, pilots }    roster size
 *
 * Client -> server
 *   { t:"join", name, ac }                                   optional (name/ac also come via the query)
 *   { t:"s", p:[x,y,z], q:[x,y,z,w], v:[x,y,z], d:[ail,ele,rud,spoil,flap], f:{ w:0|1, o:0|1 } }
 * Server -> clients
 *   { t:"hello", id, peers:[{ id, name, ac, s? }] }          s = that peer's last state, if any
 *   { t:"peer", id, name, ac }                               someone joined
 *   { t:"bye", id }                                          someone left
 *   { t:"s", id, p, q, v, d, f }                             fan-out of a peer's state
 */
import { DurableObject } from "cloudflare:workers";

export interface Env {
  ROOM: DurableObjectNamespace<Room>;
}

const FFA = "ffa";
const CODE_RE = /^[A-Z0-9]{6}$/;
const CAP_FFA = 200;
const CAP_PRIVATE = 16;
const RATE_LIMIT_PER_SEC = 20; // >= this many messages in a 1 s window -> drop
const MAX_MSG_BYTES = 1024;
const MAX_NAME = 16;
const MAX_AC = 40;

interface PeerMeta { id: string; name: string; ac: string }
interface RateWindow { start: number; count: number }

function randomCode(): string {
  const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I ambiguity
  const bytes = crypto.getRandomValues(new Uint8Array(6));
  let s = "";
  for (const b of bytes) s += alphabet[b % alphabet.length];
  return s;
}

function clean(v: unknown, max: number, fallback: string): string {
  if (typeof v !== "string") return fallback;
  const s = v.replace(/[^\x20-\x7e]/g, "").trim().slice(0, max);
  return s.length ? s : fallback;
}

export class Room extends DurableObject<Env> {
  /** Last state packet per peer id (in-memory only; rebuilt from traffic after hibernation). */
  private lastState = new Map<string, string>();
  private rate = new WeakMap<WebSocket, RateWindow>();

  private meta(ws: WebSocket): PeerMeta | null {
    try { return (ws.deserializeAttachment() as PeerMeta) ?? null; } catch { return null; }
  }

  private roster(): { ws: WebSocket; meta: PeerMeta }[] {
    const out: { ws: WebSocket; meta: PeerMeta }[] = [];
    for (const ws of this.ctx.getWebSockets()) {
      const m = this.meta(ws);
      if (m) out.push({ ws, meta: m });
    }
    return out;
  }

  private send(ws: WebSocket, text: string): void {
    try { ws.send(text); } catch { /* closed / backpressured: drop */ }
  }

  private broadcast(text: string, except?: WebSocket): void {
    for (const ws of this.ctx.getWebSockets()) {
      if (ws !== except) this.send(ws, text);
    }
  }

  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);
    const code = url.searchParams.get("code") ?? "";
    const cap = code === FFA ? CAP_FFA : CAP_PRIVATE;

    if (request.headers.get("Upgrade")?.toLowerCase() !== "websocket") {
      return Response.json({ code, pilots: this.ctx.getWebSockets().length, cap });
    }
    if (this.ctx.getWebSockets().length >= cap) {
      return new Response("room full", { status: 409 });
    }

    const meta: PeerMeta = {
      id: crypto.randomUUID().slice(0, 8),
      name: clean(url.searchParams.get("name"), MAX_NAME, "Pilot"),
      ac: clean(url.searchParams.get("ac"), MAX_AC, "glider-2-33-like"),
    };

    const pair = new WebSocketPair();
    const [client, server] = [pair[0], pair[1]];
    server.serializeAttachment(meta);
    this.ctx.acceptWebSocket(server, [meta.id]);

    const peers = this.roster()
      .filter((p) => p.ws !== server)
      .map((p) => {
        const s = this.lastState.get(p.meta.id);
        return s ? { ...p.meta, s: JSON.parse(s) } : p.meta;
      });
    this.send(server, JSON.stringify({ t: "hello", id: meta.id, room: code, peers }));
    this.broadcast(JSON.stringify({ t: "peer", ...meta }), server);

    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(ws: WebSocket, message: string | ArrayBuffer): Promise<void> {
    if (typeof message !== "string" || message.length > MAX_MSG_BYTES) return;

    // Per-socket rate limit: fixed 1 s window.
    const now = Date.now();
    let win = this.rate.get(ws);
    if (!win || now - win.start >= 1000) { win = { start: now, count: 0 }; this.rate.set(ws, win); }
    if (++win.count >= RATE_LIMIT_PER_SEC) return;

    const meta = this.meta(ws);
    if (!meta) return;

    let msg: any;
    try { msg = JSON.parse(message); } catch { return; }
    if (!msg || typeof msg !== "object") return;

    switch (msg.t) {
      case "s": {
        const out = JSON.stringify({ t: "s", id: meta.id, p: msg.p, q: msg.q, v: msg.v, d: msg.d, f: msg.f });
        this.lastState.set(meta.id, out);
        this.broadcast(out, ws);
        break;
      }
      case "join": {
        // Optional explicit join: update name/aircraft after connect (also used on aircraft switch).
        const next: PeerMeta = {
          id: meta.id,
          name: clean(msg.name, MAX_NAME, meta.name),
          ac: clean(msg.ac, MAX_AC, meta.ac),
        };
        if (next.name !== meta.name || next.ac !== meta.ac) {
          ws.serializeAttachment(next);
          this.broadcast(JSON.stringify({ t: "peer", ...next }), ws);
        }
        break;
      }
      default:
        break;
    }
  }

  async webSocketClose(ws: WebSocket, code: number, reason: string, wasClean: boolean): Promise<void> {
    this.leave(ws);
    try { ws.close(1000, "bye"); } catch { /* already closed */ }
  }

  async webSocketError(ws: WebSocket, error: unknown): Promise<void> {
    this.leave(ws);
  }

  private leave(ws: WebSocket): void {
    const meta = this.meta(ws);
    if (!meta) return;
    this.lastState.delete(meta.id);
    this.broadcast(JSON.stringify({ t: "bye", id: meta.id }), ws);
  }
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json", "access-control-allow-origin": "*" },
  });
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    const parts = url.pathname.split("/").filter(Boolean);

    if (parts.length === 0) return json({ service: "flyinggame-relay", ffa: FFA });

    if (parts[0] !== "rooms") return json({ error: "not found" }, 404);

    // POST /rooms -> new private code (client may also generate its own; the server only routes).
    if (parts.length === 1) {
      if (request.method !== "POST") return json({ error: "method" }, 405);
      return json({ code: randomCode() });
    }

    const code = parts[1].toUpperCase() === FFA.toUpperCase() ? FFA : parts[1].toUpperCase();
    if (code !== FFA && !CODE_RE.test(code)) return json({ error: "bad room code" }, 400);

    const stub = env.ROOM.getByName(code);
    const forward = new URL(request.url);
    forward.searchParams.set("code", code);

    if (parts.length === 2) {
      const r = await stub.fetch(new Request(forward, request));
      return r;
    }
    if (parts.length === 3 && parts[2] === "ws") {
      if (request.headers.get("Upgrade")?.toLowerCase() !== "websocket") {
        return json({ error: "expected websocket" }, 426);
      }
      return stub.fetch(new Request(forward, request));
    }
    return json({ error: "not found" }, 404);
  },
} satisfies ExportedHandler<Env>;
