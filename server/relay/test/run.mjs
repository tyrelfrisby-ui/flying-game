// `npm test`: start `wrangler dev` on a free port, run the test client against it, tear down.
import { spawn } from "node:child_process";

const port = 8799;
const dev = spawn("npx", ["wrangler", "dev", "--port", String(port), "--log-level", "warn"], {
  cwd: new URL("..", import.meta.url).pathname,
  stdio: ["ignore", "pipe", "pipe"],
});
dev.stdout.on("data", (d) => process.stdout.write(`[dev] ${d}`));
dev.stderr.on("data", (d) => process.stderr.write(`[dev] ${d}`));

async function waitReady() {
  for (let i = 0; i < 120; i++) {
    try {
      const r = await fetch(`http://127.0.0.1:${port}/`);
      if (r.ok) return;
    } catch { /* not up yet */ }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error("wrangler dev did not come up");
}

let code = 1;
try {
  await waitReady();
  code = await new Promise((resolve) => {
    const t = spawn(process.execPath, [new URL("client.mjs", import.meta.url).pathname, `ws://127.0.0.1:${port}`], { stdio: "inherit" });
    t.on("exit", (c) => resolve(c ?? 1));
  });
} catch (e) {
  console.error(e.message);
} finally {
  dev.kill("SIGTERM");
}
process.exit(code);
