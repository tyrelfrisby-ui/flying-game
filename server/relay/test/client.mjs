// Node test client for the relay: two pilots join a private room, exchange states, then a third
// (late) joiner must receive the roster with both peers' last states. Usage:
//   node test/client.mjs [ws://127.0.0.1:8787]
// Exits 0 on pass, 1 on failure. Requires Node >= 22 (global WebSocket).

const base = (process.argv[2] ?? "ws://127.0.0.1:8787").replace(/\/$/, "");
const http = base.replace(/^ws/, "http");

function fail(msg) { console.error("FAIL:", msg); process.exit(1); }
function ok(msg) { console.log("ok  -", msg); }

function connect(code, name, ac, extra = "") {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(`${base}/rooms/${code}/ws?name=${encodeURIComponent(name)}&ac=${encodeURIComponent(ac)}${extra}`);
    const inbox = [];
    const waiters = [];
    ws.onmessage = (e) => {
      let m; try { m = JSON.parse(e.data); } catch { m = { t: "raw", data: e.data }; }
      const i = waiters.findIndex((w) => w.pred(m));
      if (i >= 0) waiters.splice(i, 1)[0].resolve(m); else inbox.push(m);
    };
    ws.onerror = (e) => reject(new Error(`ws error for ${name}: ${e.message ?? e}`));
    ws.onopen = () => resolve({
      ws, name, inbox,
      send: (o) => ws.send(JSON.stringify(o)),
      /** Wait for a message matching pred (checks backlog first). */
      expect: (pred, what, ms = 3000) => new Promise((res, rej) => {
        const i = inbox.findIndex(pred);
        if (i >= 0) return res(inbox.splice(i, 1)[0]);
        const w = { pred, resolve: res };
        waiters.push(w);
        setTimeout(() => { const k = waiters.indexOf(w); if (k >= 0) { waiters.splice(k, 1); rej(new Error(`timeout waiting for ${what} (${name})`)); } }, ms);
      }),
      /** Assert NO message matching pred arrives within ms. */
      expectNone: (pred, what, ms = 400) => new Promise((res, rej) => {
        const w = { pred, resolve: () => rej(new Error(`unexpected ${what} (${name})`)) };
        waiters.push(w);
        setTimeout(() => { const k = waiters.indexOf(w); if (k >= 0) waiters.splice(k, 1); res(); }, ms);
      }),
    });
  });
}

const state = (x) => ({ t: "s", p: [x, 600, 0], q: [0, 0, 0, 1], v: [25, 0, 0], d: [0.1, -0.05, 0, 0, 0], f: { w: 0, o: 0 } });

try {
  // Room code from the server.
  const r = await fetch(`${http}/rooms`, { method: "POST" });
  const { code } = await r.json();
  if (!/^[A-Z0-9]{6}$/.test(code)) fail(`bad code ${code}`);
  ok(`POST /rooms -> ${code}`);

  const bad = await fetch(`${http}/rooms/abc/ws`);
  if (bad.status !== 400) fail(`bad code should 400, got ${bad.status}`);
  ok("bad room code rejected (400)");

  // Pilot A joins, gets an empty hello.
  const a = await connect(code, "Alice", "c172-like");
  const helloA = await a.expect((m) => m.t === "hello", "hello");
  if (helloA.peers.length !== 0) fail("A should see empty roster");
  ok(`A hello id=${helloA.id}, empty roster`);

  // Pilot B joins: B's hello lists A; A gets a peer notice for B.
  const b = await connect(code, "Bob", "p51d-like");
  const helloB = await b.expect((m) => m.t === "hello", "hello");
  if (helloB.peers.length !== 1 || helloB.peers[0].id !== helloA.id || helloB.peers[0].name !== "Alice") fail("B roster should contain Alice");
  const peerB = await a.expect((m) => m.t === "peer" && m.id === helloB.id, "peer(B)");
  if (peerB.name !== "Bob" || peerB.ac !== "p51d-like") fail("peer notice for B wrong");
  ok("B hello lists A; A got peer(B)");

  // State fan-out: A sends, B receives with A's id; A must NOT get its own echo.
  a.send(state(1));
  const sB = await b.expect((m) => m.t === "s" && m.id === helloA.id, "state from A");
  if (sB.p[0] !== 1 || sB.d[0] !== 0.1 || sB.f.w !== 0) fail("state payload not relayed intact");
  await a.expectNone((m) => m.t === "s", "self-echo");
  ok("A state -> B (no self-echo)");

  b.send(state(2));
  const sA = await a.expect((m) => m.t === "s" && m.id === helloB.id, "state from B");
  if (sA.p[0] !== 2) fail("B state not relayed");
  ok("B state -> A");

  // Roster count via HTTP.
  const info = await (await fetch(`${http}/rooms/${code}`)).json();
  if (info.pilots !== 2) fail(`expected 2 pilots, got ${info.pilots}`);
  ok("GET /rooms/{code} pilots=2");

  // Late joiner C: hello must carry both peers WITH their last state.
  const c = await connect(code, "Carol", "glider-2-33-like");
  const helloC = await c.expect((m) => m.t === "hello", "hello");
  if (helloC.peers.length !== 2) fail(`C should see 2 peers, saw ${helloC.peers.length}`);
  const pa = helloC.peers.find((p) => p.id === helloA.id), pb = helloC.peers.find((p) => p.id === helloB.id);
  if (!pa?.s || pa.s.p[0] !== 1 || !pb?.s || pb.s.p[0] !== 2) fail("late-joiner roster missing last states");
  ok("late joiner C got roster with last states");

  // Radio: B and C tune 123.45, A stays on the default 122.80. Voice reaches only the same frequency.
  b.send({ t: "tune", fq: "123.45" });
  c.send({ t: "tune", fq: "123.45" });
  await new Promise((r) => setTimeout(r, 150));
  const pcm = Buffer.from(Array.from({ length: 800 }, (_, i) => (i * 37) & 255)).toString("base64");
  b.send({ t: "v", a: pcm, e: 0 });
  const vC = await c.expect((m) => m.t === "v" && m.id === helloB.id, "voice from B");
  if (vC.fq !== "123.45" || vC.a !== pcm || vC.e !== 0) fail("voice payload not relayed intact");
  await a.expectNone((m) => m.t === "v", "voice on another frequency");
  await b.expectNone((m) => m.t === "v", "voice self-echo");
  ok("voice B -> C on 123.45; A on 122.80 hears nothing; no self-echo");

  a.send({ t: "tune", fq: "123.45" });
  await new Promise((r) => setTimeout(r, 150));
  c.send({ t: "v", a: pcm, e: 1 });
  const vA = await a.expect((m) => m.t === "v" && m.id === helloC.id, "voice from C after retune");
  const vB = await b.expect((m) => m.t === "v" && m.id === helloC.id, "voice from C");
  if (vA.e !== 1 || vB.e !== 1) fail("end-of-transmission flag lost");
  ok("A retuned to 123.45 hears C; unkey flag relayed");

  // Voice has its own budget: 12 packets/s alongside 10 states/s all get through.
  for (let i = 0; i < 12; i++) { b.send({ t: "v", a: pcm, e: 0 }); b.send(state(50 + i)); }
  await new Promise((r) => setTimeout(r, 500));
  const vn = c.inbox.filter((m) => m.t === "v" && m.id === helloB.id).length;
  if (vn < 12) fail(`voice packets dropped: ${vn}/12`);
  ok(`voice budget: ${vn}/12 packets relayed alongside state`);
  c.inbox.length = 0; a.inbox.length = 0; b.inbox.length = 0;

  // Rate limit (token bucket, 20/s): 2 s of 40 msgs/s -> about 20/s get through (20 of burst + 20/s refill ≈ 60 of 80).
  const tb = Date.now();
  for (let i = 0; i < 80; i++) { a.send(state(100 + i)); await new Promise((r) => setTimeout(r, 25)); }
  const sendS = (Date.now() - tb) / 1000, expect = Math.min(80, 20 + 20 * sendS);
  await new Promise((r) => setTimeout(r, 500));
  const burst = b.inbox.filter((m) => m.t === "s" && m.id === helloA.id && m.p[0] >= 100).length;
  if (burst > expect + 8 || burst < 20) fail(`rate limit wrong: ${burst}/80 relayed over ${sendS.toFixed(2)} s (expect ~${expect.toFixed(0)})`);
  ok(`rate limit: ${burst}/80 relayed over ${sendS.toFixed(2)} s (bucket expects ~${expect.toFixed(0)})`);

  // Leave: A closes, B and C get bye.
  a.ws.close();
  await b.expect((m) => m.t === "bye" && m.id === helloA.id, "bye(A)");
  await c.expect((m) => m.t === "bye" && m.id === helloA.id, "bye(A)");
  ok("A left; B and C got bye");

  b.ws.close(); c.ws.close();

  // ---- PROTOCOL 2: free-for-all shard assignment, tick batches, interest radius, keepalive.
  const as = await (await fetch(`${http}/rooms/ffa/assign`)).json();
  if (!/^ffa\d*$/.test(as.code ?? "")) fail(`assign gave ${JSON.stringify(as)}`);
  ok(`GET /rooms/ffa/assign -> ${as.code} (${as.pilots}/${as.cap})`);
  const shard = as.code;
  const x = await connect(shard, "X2", "extra-300-like", "&v=2");
  const hx = await x.expect((m) => m.t === "hello", "hello(X)");
  if (hx.tick !== 100) fail("v2 hello lacks tick");
  const y = await connect(shard, "Y2", "c172-like", "&v=2");
  const hy = await y.expect((m) => m.t === "hello", "hello(Y)");
  const far = await connect(shard, "Far", "c172-like", "&v=2");
  const hf = await far.expect((m) => m.t === "hello", "hello(Far)");
  const st = (px) => ({ t: "s", p: [px, 600, 0], q: [0, 0, 0, 1], v: [30, 0, 0], d: [0, 0, 0, 0, 0], f: { w: 0, o: 0 } });
  far.send(st(100000)); x.send(st(0)); y.send(st(500));
  await new Promise((r) => setTimeout(r, 150));
  for (let i = 0; i < 5; i++) { x.send(st(10 * i)); y.send(st(500 + 10 * i)); far.send(st(100000 + i)); await new Promise((r) => setTimeout(r, 50)); }
  const batch = await y.expect((m) => m.t === "S" && m.b.some((s) => s.id === hx.id), "S batch with X (Y)");
  if (batch.b.some((s) => s.id === hy.id)) fail("batch echoed Y's own state");
  await new Promise((r) => setTimeout(r, 400));
  const ys = [batch, ...y.inbox.filter((m) => m.t === "S")];
  if (ys.some((m) => m.b.some((s) => s.id === hf.id))) fail("interest: Y (near 0) got Far (100 km) states");
  if (y.inbox.some((m) => m.t === "s")) fail("v2 pilot got v1 per-message states");
  ok(`v2: ${ys.length} tick batches to Y in ~0.6 s; X in, Far (100 km) out, no self-echo`);
  x.ws.send("ping");
  await x.expect((m) => m.t === "raw" && m.data === "pong", "pong");
  ok("ping -> pong (keepalive)");
  x.ws.close(); y.ws.close(); far.ws.close();
  console.log("PASS");
  process.exit(0);
} catch (e) {
  fail(e.message ?? String(e));
}
