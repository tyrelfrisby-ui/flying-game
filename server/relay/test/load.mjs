// LOAD TEST (2026-10-11): N protocol-2 pilots in one free-for-all shard, each sending 10 Hz states from a random
// spot within SPREAD metres, for SECONDS. Reports what each pilot received (states/s), the end-to-end latency
// (send → receive, same machine clock) and drops. Usage:
//   node test/load.mjs wss://mp.aeroplayground.app 100 20 [spread=20000] [room=ffa8]
const [base = "ws://127.0.0.1:8787", nStr = "20", secStr = "15", spreadStr = "20000", room = "ffa8"] = process.argv.slice(2);
const N = +nStr, SECONDS = +secStr, SPREAD = +spreadStr;
const lat = [], got = new Array(N).fill(0), batches = new Array(N).fill(0);
let opened = 0, closed = 0, errors = 0;
const socks = [];
function pilot(i) {
  return new Promise((resolve) => {
    const ws = new WebSocket(`${base.replace(/\/$/, "")}/rooms/${room}/ws?name=load${i}&ac=c172-like&v=2`);
    const p = [(Math.random() - 0.5) * SPREAD, 600 + Math.random() * 1500, (Math.random() - 0.5) * SPREAD];
    ws.onopen = () => { opened++; resolve(ws); };
    ws.onerror = () => { errors++; resolve(null); };
    ws.onclose = () => { closed++; };
    ws.onmessage = (e) => {
      if (typeof e.data !== "string" || e.data[2] !== "t") return;
      const m = JSON.parse(e.data);
      if (m.t !== "S") return;
      batches[i]++;
      const now = Date.now();
      for (const s of m.b) { got[i]++; const ts = s.d?.[5]; if (ts && lat.length < 200000) lat.push(now - ts); }
    };
    ws._p = p;
  });
}
for (let i = 0; i < N; i++) { socks.push(await pilot(i)); if (i % 20 === 19) await new Promise((r) => setTimeout(r, 200)); }
console.log(`connected ${opened}/${N} (errors ${errors})`);
const t0 = Date.now();
const timer = setInterval(() => {
  for (const ws of socks) {
    if (!ws || ws.readyState !== 1) continue;
    ws._p[0] += 5; // 50 m/s east
    ws.send(JSON.stringify({ t: "s", p: ws._p, q: [0, 0, 0, 1], v: [50, 0, 0], d: [0, 0, 0, 0, 0, Date.now()], f: { w: 0, o: 0 } }));
  }
}, 100);
await new Promise((r) => setTimeout(r, SECONDS * 1000));
clearInterval(timer);
const dt = (Date.now() - t0) / 1000;
lat.sort((a, b) => a - b);
const q = (f) => lat.length ? lat[Math.min(lat.length - 1, Math.floor(lat.length * f))] : NaN;
const perSec = got.map((g) => g / dt).sort((a, b) => a - b);
console.log(JSON.stringify({ pilots: N, seconds: +dt.toFixed(1), spreadM: SPREAD,
  statesPerPilotPerSec: { min: +perSec[0].toFixed(1), median: +perSec[Math.floor(N / 2)].toFixed(1), max: +perSec[N - 1].toFixed(1) },
  expectedIfAllInRange: (N - 1) * 10,
  batchesPerPilotPerSec: +(batches.reduce((a, b) => a + b, 0) / N / dt).toFixed(1),
  latencyMs: { p50: q(0.5), p90: q(0.9), p99: q(0.99), max: lat[lat.length - 1] }, closedEarly: closed }, null, 1));
for (const ws of socks) try { ws?.close(); } catch {}
process.exit(0);
