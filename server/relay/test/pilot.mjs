// A SCRIPTED SECOND PILOT (2026-10-11) for testing a real game client against the platform: joins the free-for-all
// shard the server assigns (or a room code), finds the other pilots, and circles 300 m from the first one it hears.
// Prints what it sees. Usage: node test/pilot.mjs [https://mp.aeroplayground.app] [ffa|ROOMCODE] [seconds=60]
const [httpBase = "https://mp.aeroplayground.app", roomArg = "ffa", secStr = "60"] = process.argv.slice(2);
let room = roomArg;
if (room === "ffa") room = (await (await fetch(`${httpBase}/rooms/ffa/assign`)).json()).code;
const ws = new WebSocket(`${httpBase.replace(/^http/, "ws")}/rooms/${room}/ws?v=2&name=Robo&ac=pa18-cub-like`);
const seen = new Map(); let target = null, me = [0, 600, 0], ang = 0, myId = null, batches = 0;
ws.onmessage = (e) => {
  if (typeof e.data !== "string" || e.data === "pong") return;
  const m = JSON.parse(e.data);
  if (m.t === "hello") { myId = m.id; console.log(`joined ${room} as ${m.id}; peers: ${m.peers.map((p) => `${p.name}(${p.ac})`).join(", ") || "none"}`);
    for (const p of m.peers) if (p.s?.p) { seen.set(p.id, { name: p.name, p: p.s.p }); target ??= p.id; } }
  if (m.t === "peer") { console.log(`peer joined: ${m.name} (${m.ac})`); seen.set(m.id, { name: m.name, p: null }); }
  if (m.t === "bye") console.log(`peer left: ${seen.get(m.id)?.name ?? m.id}`);
  if (m.t === "S") { batches++; for (const s of m.b) { const r = seen.get(s.id) ?? { name: "?" }; r.p = s.p; seen.set(s.id, r); target ??= s.id; } }
};
await new Promise((r) => (ws.onopen = r));
const t0 = Date.now();
const timer = setInterval(() => {
  const tp = target && seen.get(target)?.p;
  if (tp) { ang += 0.02; me = [tp[0] + 300 * Math.cos(ang), tp[1] + 30, tp[2] + 300 * Math.sin(ang)]; }
  ws.send(JSON.stringify({ t: "s", p: me, q: [0, Math.sin(-ang / 2), 0, Math.cos(-ang / 2)], v: [-30 * Math.sin(ang), 0, 30 * Math.cos(ang)], d: [0, 0, 0, 0, 0], f: { w: 0, o: 0 } }));
}, 100);
const report = setInterval(() => {
  const tp = target && seen.get(target);
  console.log(`${((Date.now() - t0) / 1000).toFixed(0)} s: ${batches} batches; ${tp ? `following ${tp.name} at [${tp.p.map((x) => x.toFixed(0)).join(", ")}]` : "no one heard yet"}`);
}, 5000);
await new Promise((r) => setTimeout(r, +secStr * 1000));
clearInterval(timer); clearInterval(report); ws.close(); process.exit(0);
