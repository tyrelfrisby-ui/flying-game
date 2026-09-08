// Node test client for the relay: two pilots join a private room, exchange states, then a third
// (late) joiner must receive the roster with both peers' last states. Usage:
//   node test/client.mjs [ws://127.0.0.1:8787]
// Exits 0 on pass, 1 on failure. Requires Node >= 22 (global WebSocket).

const base = (process.argv[2] ?? "ws://127.0.0.1:8787").replace(/\/$/, "");
const http = base.replace(/^ws/, "http");

function fail(msg) { console.error("FAIL:", msg); process.exit(1); }
function ok(msg) { console.log("ok  -", msg); }

function connect(code, name, ac) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(`${base}/rooms/${code}/ws?name=${encodeURIComponent(name)}&ac=${encodeURIComponent(ac)}`);
    const inbox = [];
    const waiters = [];
    ws.onmessage = (e) => {
      const m = JSON.parse(e.data);
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

  // Rate limit: 40 msgs in a burst -> B sees fewer than 20 of them.
  for (let i = 0; i < 40; i++) a.send(state(100 + i));
  await new Promise((r) => setTimeout(r, 500));
  const burst = b.inbox.filter((m) => m.t === "s" && m.id === helloA.id && m.p[0] >= 100).length;
  if (burst >= 20) fail(`rate limit not applied: ${burst} relayed`);
  ok(`rate limit: ${burst}/40 burst messages relayed`);

  // Leave: A closes, B and C get bye.
  a.ws.close();
  await b.expect((m) => m.t === "bye" && m.id === helloA.id, "bye(A)");
  await c.expect((m) => m.t === "bye" && m.id === helloA.id, "bye(A)");
  ok("A left; B and C got bye");

  b.ws.close(); c.ws.close();
  console.log("PASS");
  process.exit(0);
} catch (e) {
  fail(e.message ?? String(e));
}
