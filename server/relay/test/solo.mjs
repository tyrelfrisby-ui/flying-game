// SOLO COST TEST (owner 2026-10-16): a pilot alone in a room must cost ~nothing. One pilot joins, obeys the
// server's `solo` flag the way the game's NetSession does, and we count what crosses the wire. A Durable
// Object is billed for the time it is AWAKE, and what keeps it awake is messages arriving and a ticker
// running — so "no messages, no ticker" is the whole of the fix, and this counts both.
//
//   node test/solo.mjs [ws://127.0.0.1:8787] [seconds=60] [room=<new private room>]
//
// Then a second pilot joins for the last few seconds to prove the quiet pilot wakes up and is seen.
const [base = "ws://127.0.0.1:8787", secStr = "60", roomArg] = process.argv.slice(2);
const SECONDS = +secStr;
const http = base.replace(/^ws/, "http").replace(/\/$/, "");

const code = roomArg ?? (await (await fetch(`${http}/rooms`, { method: "POST" })).json()).code;
console.log(`room ${code}, ${SECONDS} s`);

function pilot(name) {
  const counts = { sent: 0, ticks: 0, states: 0, peers: 0, solo: null };
  const ws = new WebSocket(`${base.replace(/\/$/, "")}/rooms/${code}/ws?name=${name}&ac=c172-like&v=2`);
  const p = { counts, ws, id: null, solo: false };
  ws.onmessage = (e) => {
    if (typeof e.data !== "string" || e.data[0] !== "{") return;   // "pong" is not JSON
    const m = JSON.parse(e.data);
    if (m.t === "hello") { p.id = m.id; p.solo = m.solo === true; counts.solo = p.solo; }
    else if (m.t === "peer") { counts.peers++; p.solo = false; }
    else if (m.t === "bye" && m.solo === true) p.solo = true;
    else if (m.t === "S") { counts.ticks++; counts.states += m.b.length; }
    else if (m.t === "s") counts.states++;
  };
  // The game's rule: 10 Hz of state, and nothing at all while the room says we are alone.
  p.timer = setInterval(() => {
    if (ws.readyState !== 1 || p.solo) return;
    counts.sent++;
    ws.send(JSON.stringify({ t: "s", p: [counts.sent, 600, 0], q: [0, 0, 0, 1], v: [50, 0, 0], d: [0, 0, 0, 0, 0], f: { w: 0, o: 0 } }));
  }, 100);
  // The keepalive, which the runtime answers without waking the room.
  p.ping = setInterval(() => { if (ws.readyState === 1) ws.send("ping"); }, 15000);
  return p;
}

const a = pilot("Alone");
await new Promise((r) => setTimeout(r, 400));
console.log(`A joined: solo=${a.counts.solo}`);

await new Promise((r) => setTimeout(r, Math.max(0, SECONDS - 5) * 1000));
const aloneSent = a.counts.sent, aloneTicks = a.counts.ticks;
console.log(`alone for ${SECONDS - 5} s: sent ${aloneSent} states, received ${aloneTicks} ticks`);

const b = pilot("Arrival");
await new Promise((r) => setTimeout(r, 2500));
console.log(`after a peer arrived: A sent ${a.counts.sent - aloneSent} states, B saw ${b.counts.states} of A's`);

clearInterval(a.timer); clearInterval(a.ping); clearInterval(b.timer); clearInterval(b.ping);
a.ws.close(); b.ws.close();

const perHour = 10 * 3600;    // what 10 Hz would have sent in an hour
const ok = aloneSent === 0 && aloneTicks === 0 && b.counts.states > 0;
console.log("");
console.log(`A lone pilot sent ${aloneSent} messages where the old client would have sent ${perHour} an hour,`);
console.log(`and the room ran ${aloneTicks} ticks where it would have run ${perHour}. The object has nothing to`);
console.log(`wake it, so it hibernates and its duration bill stops.`);
console.log(ok ? "PASS" : "FAIL");
process.exit(ok ? 0 : 1);
