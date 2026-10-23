/**
 * flyinggame-relay — WebSocket state relay for multiplayer free play.
 *
 * One Durable Object per room, keyed by room code. Free-for-all is SHARDED (2026-10-11, the aeroplayground.app
 * platform): `ffa`, `ffa2` … `ffa10`, CAP_FFA (100) pilots each — GET /rooms/ffa/assign picks the first with room;
 * private rooms are 6-char [A-Z0-9] codes (cap 16). No auth, no persistence.
 *
 * PROTOCOL 2 (?v=2 on the socket): the room keeps each pilot's newest state and, on a 10 Hz TICK, sends each
 * v2 pilot ONE packet { t:"S", b:[state…] } with the states that changed since the last tick — in a free-for-all
 * only those within INTEREST_M of that pilot. N pilots cost N sends a tick instead of N² (v1 clients still get
 * every state as it arrives). "ping" is answered "pong" by the runtime without waking the room.
 * Uses the WebSocket Hibernation API so an idle room costs nothing.
 *
 * COST (owner 2026-10-16). A Durable Object is billed for the time it is awake, so the two things that cost real
 * money are a ticker that never stops and a client that never stops sending:
 *  - SOLO: with fewer than two pilots connected there is nobody to send to, so the room stops its ticker and says
 *    so ("solo":true on hello/bye). The client then stops sending state until a "peer" arrives, which lets the
 *    object hibernate. A lone pilot in a private room went from a real per-hour cost to nothing at all.
 *  - IDLE: a pilot who has not moved a control for IDLE_CLOSE_MS is closed, with a warning at IDLE_WARN_MS. The
 *    client enforces it on the controls and the room enforces it on the STATE, so a modified client cannot park a
 *    room awake all night.
 * Neither caps anybody and neither stores anything: no counters, no history, nothing per pilot beyond the socket.
 *
 * PRIVACY: the published policy (glassoverlay.com/legal/aero-playground-privacy) promises the relay
 * stores and logs nothing. Keep wrangler observability off and never log names, IPs or voice
 * without updating that policy first.
 *
 * HTTP
 *   POST /rooms                      -> { code }            fresh 6-char private room code
 *   GET  /rooms/{code}/ws?name=&ac=  -> WebSocket upgrade   (code = ffa | [A-Z0-9]{6})
 *       &idle=<seconds>                                      ask to be dropped sooner than the default; a client
 *                                                            can shorten its own idle timeout but never lengthen it
 *   GET  /rooms/{code}               -> { code, pilots }    roster size
 *   GET  /rooms/ffa/assign           -> { code }            the free-for-all shard to join (503 when all are full)
 *
 * Client -> server
 *   { t:"join", name, ac }                                   optional (name/ac also come via the query)
 *   { t:"s", p:[x,y,z], q:[x,y,z,w], v:[x,y,z], d:[ail,ele,rud,spoil,flap], f:{ w:0|1, o:0|1 } }
 *   { t:"tune", fq:"123.45" }                                radio frequency this pilot listens/talks on
 *   { t:"v", a:"<base64 8 kHz mu-law>", e:0|1 }              radio voice (~100 ms per packet; e:1 = mic unkeyed)
 * Server -> clients
 *   { t:"hello", id, peers:[{ id, name, ac, s? }], solo }    s = that peer's last state, if any; solo = you are alone
 *   { t:"peer", id, name, ac }                               someone joined (stop being solo, send your state now)
 *   { t:"bye", id, solo? }                                   someone left (solo = you are the only one left)
 *   { t:"idle", w:1 } / { t:"idle", c:1 }                    idle warning, then the close that follows it
 *   { t:"s", id, p, q, v, d, f }                             fan-out of a peer's state (v1)
 *   { t:"S", b:[{ id, p, q, v, d, f }…] }                    the tick's states (v2)
 *   { t:"v", id, fq, a, e }                                  voice, only to pilots tuned to the same frequency
 */
import { DurableObject } from "cloudflare:workers";

export interface Env {
  ROOM: DurableObjectNamespace<Room>;
}

const FFA = "ffa";
const CODE_RE = /^[A-Z0-9]{6}$/;
// Measured 2026-10-11 (test/load.mjs, live): one shard holds a 10 Hz tick with 100 pilots all in one 20 km area
// (p50 ≈ 150 ms). 10 shards × 100 = the 1,000-pilot target; pilots in different shards don't see each other.
const FFA_SHARDS = ["ffa", "ffa2", "ffa3", "ffa4", "ffa5", "ffa6", "ffa7", "ffa8", "ffa9", "ffa10"];
const CAP_FFA = 100;
const TICK_MS = 100;
const INTEREST_M = 40000;          // free-for-all: states of pilots within 40 km (positions are metres)
const CAP_PRIVATE = 16;
const RATE_LIMIT_PER_SEC = 20; // >= this many messages in a 1 s window -> drop
const MAX_MSG_BYTES = 1024;
const MAX_VOICE_BYTES = 2048;      // a 100 ms mu-law packet is ~1.1 kB of base64
const VOICE_RATE_PER_SEC = 25;     // voice has its own budget (10 packets/s nominal)
const MAX_FREQ = 8;
const DEFAULT_FREQ = "122.80";
const MAX_NAME = 16;
const MAX_AC = 40;
// Idle (owner 2026-10-16): a pilot parked in a room keeps the object awake and the bill running. The client
// watches the CONTROLS; the room watches the STATE, which is the half a modified client cannot talk its way out of.
const IDLE_WARN_MS = 13 * 60 * 1000;
const IDLE_CLOSE_MS = 15 * 60 * 1000;
// What counts as having moved: enough to be a pilot flying rather than a trimmed-out aeroplane drifting. 25 m of
// position, or any real control movement.
const IDLE_POS_M = 25;
const IDLE_CTRL = 0.02;
const IDLE_SWEEP_TICKS = 10;       // check for idle pilots once a second, not every tick: at 5 s the warning
                                   // and the close could land in the same sweep and the pilot would never see it

interface PeerMeta { id: string; name: string; ac: string; fq?: string; v?: number; r?: string; im?: number }
interface StateRec { json: string; p: number[]; seq: number }
interface RateWindow { start: number; count: number }

function randomCode(): string {
  const alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I ambiguity
  const bytes = crypto.getRandomValues(new Uint8Array(6));
  let s = "";
  for (const b of bytes) s += alphabet[b % alphabet.length];
  return s;
}

/** The idle timeout for a socket, in ms: the default, or a shorter one the client asked for. */
function idleMsFrom(v: string | null): number {
  const s = Number(v);
  if (!Number.isFinite(s) || s <= 0) return IDLE_CLOSE_MS;
  return Math.max(5000, Math.min(IDLE_CLOSE_MS, s * 1000));
}

function clean(v: unknown, max: number, fallback: string): string {
  if (typeof v !== "string") return fallback;
  const s = v.replace(/[^\x20-\x7e]/g, "").trim().slice(0, max);
  return s.length ? s : fallback;
}

export class Room extends DurableObject<Env> {
  /** Last state packet per peer id (in-memory only; rebuilt from traffic after hibernation). */
  private lastState = new Map<string, StateRec>();
  private dirty = new Set<string>();
  private ticker: ReturnType<typeof setInterval> | null = null;
  private tickNo = 0;
  /** Socket metadata, cached (the attachment is the source of truth across hibernation). */
  private metas = new Map<WebSocket, PeerMeta>();
  /** v2: per recipient, the seq of each peer's state last sent to it. */
  private sentSeq = new Map<string, Map<string, number>>();
  /** When each pilot last did something, and what they were doing then. In memory only, like everything else
   *  here: it is a timer, not a record, and it dies with the room. */
  private idle = new Map<string, { at: number; p: number[]; d: number[]; warned: boolean }>();

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    this.ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair("ping", "pong"));
  }

  /** v2: one packet per pilot per tick with the peers' new states — and, in a free-for-all, at a rate by distance
   *  (LEVEL OF DETAIL, as MMOs do): within 3 km every tick (10 Hz), within 10 km every 3rd, within 40 km every 10th,
   *  beyond that none. A tick sends only states newer than the last one this pilot was sent. */
  private tick(): void {
    const socks = this.roster();
    if (socks.length < 2) { this.stopTicker(); return; }   // nobody to send to: let the room hibernate
    if (this.tickNo % IDLE_SWEEP_TICKS === 0) this.sweepIdle(socks);
    if (this.dirty.size === 0 && this.tickNo % 10 !== 0) { this.tickNo++; return; }
    this.dirty.clear();
    const n = this.tickNo++;
    const ffa = (socks[0].meta.r ?? "").startsWith("ffa");
    const all = [...this.lastState];
    for (const { ws, meta } of socks) {
      if (meta.v !== 2) continue;
      let sent = this.sentSeq.get(meta.id);
      if (!sent) { sent = new Map(); this.sentSeq.set(meta.id, sent); }
      const me = this.lastState.get(meta.id)?.p;
      const parts: string[] = [];
      let k = 0;
      for (const [id, r] of all) {
        k++;
        if (id === meta.id || sent.get(id) === r.seq) continue;
        if (ffa && me && me.length === 3 && r.p.length === 3) {
          const dx = r.p[0] - me[0], dy = r.p[1] - me[1], dz = r.p[2] - me[2], d2 = dx * dx + dy * dy + dz * dz;
          const period = d2 < 3000 * 3000 ? 1 : d2 < 10000 * 10000 ? 3 : d2 < INTEREST_M * INTEREST_M ? 10 : 0;
          if (period === 0 || (n + k) % period !== 0) continue;
        }
        sent.set(id, r.seq);
        parts.push(r.json);
      }
      if (parts.length) this.send(ws, `{"t":"S","b":[${parts.join(",")}]}`);
    }
  }
  /** Fewer than two pilots: there is nobody to send anything to. */
  private alone(except?: WebSocket): boolean {
    let n = 0;
    for (const ws of this.ctx.getWebSockets()) if (ws !== except) n++;
    return n < 2;
  }

  private stopTicker(): void { if (this.ticker) { clearInterval(this.ticker); this.ticker = null; } }

  /** The ticker is the room's heartbeat and the thing that keeps it awake, so it only runs when a second pilot
   *  is there to receive a tick. One pilot alone costs nothing. */
  private ensureTicker(): void {
    if (this.alone()) { this.stopTicker(); return; }
    if (!this.ticker) this.ticker = setInterval(() => this.tick(), TICK_MS);
  }

  /** Has this pilot done anything? Control movement, or enough position change to be going somewhere. A pilot
   *  flying hands-off is idle by this measure and by the client's, which is the point: both ends agree. */
  private static stirred(prev: { p: number[]; d: number[] }, p: number[], d: number[]): boolean {
    for (let i = 0; i < Math.max(prev.d.length, d.length); i++)
      if (Math.abs((d[i] ?? 0) - (prev.d[i] ?? 0)) > IDLE_CTRL) return true;
    if (prev.p.length === 3 && p.length === 3) {
      const dx = p[0] - prev.p[0], dy = p[1] - prev.p[1], dz = p[2] - prev.p[2];
      if (dx * dx + dy * dy + dz * dz > IDLE_POS_M * IDLE_POS_M) return true;
    }
    return false;
  }

  /** Close the pilots who have stopped flying. The client does this on the controls; this is the half that a
   *  modified client cannot avoid, because parking means the state stops changing. */
  private sweepIdle(socks: { ws: WebSocket; meta: PeerMeta }[]): void {
    const now = Date.now();
    for (const { ws, meta } of socks) {
      const rec = this.idle.get(meta.id);
      if (!rec) continue;
      const close = meta.im ?? IDLE_CLOSE_MS;
      const warn = close === IDLE_CLOSE_MS ? IDLE_WARN_MS : close * (IDLE_WARN_MS / IDLE_CLOSE_MS);
      const age = now - rec.at;
      if (age > close) {
        this.send(ws, '{"t":"idle","c":1}');
        this.leave(ws);
        try { ws.close(1000, "idle"); } catch { /* already gone */ }
      } else if (age > warn && !rec.warned) {
        rec.warned = true;
        this.send(ws, '{"t":"idle","w":1}');
      }
    }
  }
  private rate = new WeakMap<WebSocket, RateWindow>();
  private voiceRate = new WeakMap<WebSocket, RateWindow>();

  /** Per-socket TOKEN BUCKET: `limit` messages of burst, refilled at `limit` per second (a fixed window let a burst
   *  straddling its edge through at twice the rate). */
  private allow(map: WeakMap<WebSocket, RateWindow>, ws: WebSocket, limit: number): boolean {
    const now = Date.now();
    let b = map.get(ws);
    if (!b) { b = { start: now, count: limit }; map.set(ws, b); }   // (start = last refill, count = tokens)
    b.count = Math.min(limit, b.count + ((now - b.start) * limit) / 1000); b.start = now;
    if (b.count < 1) return false;
    b.count -= 1; return true;
  }

  private meta(ws: WebSocket): PeerMeta | null {
    const c = this.metas.get(ws);
    if (c) return c;
    try { const m = (ws.deserializeAttachment() as PeerMeta) ?? null; if (m) this.metas.set(ws, m); return m; } catch { return null; }
  }
  private setMeta(ws: WebSocket, m: PeerMeta): void { ws.serializeAttachment(m); this.metas.set(ws, m); }

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
    const cap = FFA_SHARDS.includes(code) ? CAP_FFA : CAP_PRIVATE;

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
      v: url.searchParams.get("v") === "2" ? 2 : 1,
      r: code,
      // A client may ask to be dropped SOONER than the default (the tests do). It cannot ask for longer, so
      // this is not a way to park a room awake.
      im: idleMsFrom(url.searchParams.get("idle")),
    };

    const pair = new WebSocketPair();
    const [client, server] = [pair[0], pair[1]];
    this.setMeta(server, meta);
    this.ctx.acceptWebSocket(server, [meta.id]);

    const peers = this.roster()
      .filter((p) => p.ws !== server)
      .map((p) => {
        const s = this.lastState.get(p.meta.id);
        const { v, r, ...pub } = p.meta;
        return s ? { ...pub, s: JSON.parse(s.json) } : pub;
      });
    const solo = peers.length === 0;
    this.send(server, JSON.stringify({ t: "hello", id: meta.id, room: code, peers, tick: TICK_MS, solo }));
    // Everyone else stops being solo the moment this arrives, and sends their state straight back.
    this.broadcast(JSON.stringify({ t: "peer", id: meta.id, name: meta.name, ac: meta.ac }), server);
    this.idle.set(meta.id, { at: Date.now(), p: [], d: [], warned: false });
    if (meta.v === 2) this.ensureTicker();

    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(ws: WebSocket, message: string | ArrayBuffer): Promise<void> {
    if (typeof message !== "string" || message.length > MAX_VOICE_BYTES) return;

    // Voice packets ({"t":"v",...}) have their own size and rate budget; everything else the 1 kB / 20 per s one.
    const voice = message.startsWith('{"t":"v"');
    if (voice ? !this.allow(this.voiceRate, ws, VOICE_RATE_PER_SEC)
              : message.length > MAX_MSG_BYTES || !this.allow(this.rate, ws, RATE_LIMIT_PER_SEC)) return;

    const meta = this.meta(ws);
    if (!meta) return;

    let msg: any;
    try { msg = JSON.parse(message); } catch { return; }
    if (!msg || typeof msg !== "object") return;

    switch (msg.t) {
      case "s": {
        const p = Array.isArray(msg.p) ? msg.p.slice(0, 3).map(Number) : [];
        const out = JSON.stringify({ t: "s", id: meta.id, p: msg.p, q: msg.q, v: msg.v, d: msg.d, f: msg.f });
        this.lastState.set(meta.id, { json: out, p, seq: (this.lastState.get(meta.id)?.seq ?? 0) + 1 });
        this.dirty.add(meta.id);
        const d = Array.isArray(msg.d) ? msg.d.slice(0, 5).map(Number) : [];
        const prev = this.idle.get(meta.id);
        if (!prev || !prev.p.length || Room.stirred(prev, p, d)) this.idle.set(meta.id, { at: Date.now(), p, d, warned: false });
        // v1 pilots get it now; v2 pilots on the tick.
        for (const o of this.roster()) if (o.ws !== ws && o.meta.v !== 2) this.send(o.ws, out);
        this.ensureTicker();
        break;
      }
      case "join": {
        // Optional explicit join: update name/aircraft after connect (also used on aircraft switch).
        const next: PeerMeta = {
          id: meta.id,
          name: clean(msg.name, MAX_NAME, meta.name),
          ac: clean(msg.ac, MAX_AC, meta.ac),
          fq: meta.fq,
          v: meta.v,
          r: meta.r,
          im: meta.im,
        };
        if (next.name !== meta.name || next.ac !== meta.ac) {
          this.setMeta(ws, next);
          this.broadcast(JSON.stringify({ t: "peer", id: next.id, name: next.name, ac: next.ac }), ws);
        }
        break;
      }
      case "tune": {
        const fq = clean(msg.fq, MAX_FREQ, DEFAULT_FREQ);
        if (fq !== meta.fq) this.setMeta(ws, { ...meta, fq });
        break;
      }
      case "v": {
        // Radio: only pilots on the sender's frequency hear it.
        if (typeof msg.a !== "string" || !/^[A-Za-z0-9+/=]*$/.test(msg.a)) return;
        const fq = meta.fq ?? DEFAULT_FREQ;
        const out = JSON.stringify({ t: "v", id: meta.id, fq, a: msg.a, e: msg.e ? 1 : 0 });
        for (const p of this.roster()) {
          if (p.ws !== ws && (p.meta.fq ?? DEFAULT_FREQ) === fq) this.send(p.ws, out);
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
    this.dirty.delete(meta.id);
    this.sentSeq.delete(meta.id);
    this.idle.delete(meta.id);
    for (const m of this.sentSeq.values()) m.delete(meta.id);
    this.metas.delete(ws);
    // The pilot left behind is told they are alone, so they stop sending and the room can go back to sleep.
    const solo = this.alone(ws);
    this.broadcast(JSON.stringify(solo ? { t: "bye", id: meta.id, solo: true } : { t: "bye", id: meta.id }), ws);
    if (solo) this.stopTicker();
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

    const lower = parts[1].toLowerCase();
    // GET /rooms/ffa/assign -> the first free-for-all shard with room.
    if (lower === FFA && parts.length === 3 && parts[2] === "assign") {
      for (const shard of FFA_SHARDS) {
        const u = new URL(request.url); u.pathname = `/rooms/${shard}`; u.searchParams.set("code", shard);
        const r = await env.ROOM.getByName(shard).fetch(new Request(u));
        const j = (await r.json()) as { pilots: number };
        if (j.pilots < CAP_FFA - 2) return json({ code: shard, pilots: j.pilots, cap: CAP_FFA });
      }
      return json({ error: "all free-for-all shards are full" }, 503);
    }
    const code = FFA_SHARDS.includes(lower) ? lower : parts[1].toUpperCase();
    if (!FFA_SHARDS.includes(code) && !CODE_RE.test(code)) return json({ error: "bad room code" }, 400);

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
