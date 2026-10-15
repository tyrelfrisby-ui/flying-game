"""Fly the Aero widget from a closed loop and record telemetry (Ty/Glass, Ep1 V-g diagram, 2026-10-15).

The airplane is FLOWN: every row below is the physics answering stick, rudder and throttle. Nothing here sets an
attitude or a rate directly — the controllers move the controls and the sim decides what the airplane does.
"""
import json, math, os, socket, time


class W:
    def __init__(self, port=47840, timeout=30):
        self.s = socket.create_connection(("127.0.0.1", port), timeout=15)
        self.s.settimeout(timeout)
        self.f = self.s.makefile("rwb")

    def cmd(self, **kw):
        return self.pipe(kw)[0]

    def pipe(self, *cmds):
        """Several commands in ONE round trip. The server answers once per Unity frame, so a controls+state pair
        costs one frame (~17 ms) instead of two — the control loop runs at frame rate, ~59 Hz."""
        for c in cmds:
            self.f.write((json.dumps(c) + "\n").encode())
        self.f.flush()
        out = []
        for c in cmds:
            line = self.f.readline()
            if not line:
                raise RuntimeError("widget closed the connection")
            r = json.loads(line)
            if not r.get("ok", True):
                raise RuntimeError(f"{c} -> {r}")
            out.append(r)
        return out


class PID:
    def __init__(self, kp, ki, kd, lim=1.0, ilim=0.5):
        self.kp, self.ki, self.kd, self.lim, self.ilim = kp, ki, kd, lim, ilim
        self.i = 0.0
        self.prev = None

    def __call__(self, err, dt):
        self.i = max(-self.ilim, min(self.ilim, self.i + err * dt * self.ki))
        d = 0.0 if self.prev is None or dt <= 0 else (err - self.prev) / dt
        self.prev = err
        return max(-self.lim, min(self.lim, self.kp * err + self.i + self.kd * d))

    def reset(self):
        self.i = 0.0
        self.prev = None


def clamp(v, lo, hi):
    return max(lo, min(hi, v))


class Flight:
    """A recorded flight. `hz` is the control-loop and logging rate (30 Hz was asked for; 50 gives margin)."""

    def __init__(self, w, aircraft, condition="cruise", hz=50.0):
        self.w, self.hz, self.dt = w, hz, 1.0 / hz
        self.aircraft = aircraft
        self.rows = []
        self.t0 = None
        w.cmd(cmd="start", condition=condition, aircraft=aircraft)
        w.cmd(cmd="controlsDisplay", show=False)
        time.sleep(6)          # the trim settles before anything is recorded

    def state(self):
        return self.w.cmd(cmd="state")

    def fly(self, seconds, fn, phase, stop=None):
        """Run the loop for `seconds` (or until `stop(st)` goes true), logging a row per tick under `phase`.

        `fn(st, t, dt) -> dict` returns control inputs; it is the pilot. The controls computed from one frame's
        state are sent with the next frame's state request (one frame of lag — a pilot has far more)."""
        if self.t0 is None:
            self.t0 = time.time()
        st0 = self.w.cmd(cmd="state")
        sim0 = st0["review"]["historyMs"] / 1000.0      # sim seconds, not wall seconds
        sim = [0.0]
        last = time.time()
        pending = None
        while sim[0] < seconds:
            if pending:
                st = self.w.pipe({"cmd": "controls", **pending}, {"cmd": "state"})[1]
            else:
                st = self.w.cmd(cmd="state")
            now = time.time()
            dt = min(0.2, now - last)
            last = now
            sim[0] = st["review"]["historyMs"] / 1000.0 - sim0
            pending = fn(st, sim[0], dt) or {}
            self.log(st, now, phase)
            if stop is not None and stop(st):
                break
        if pending:
            self.w.cmd(cmd="controls", **pending)
        return self.rows[-1] if self.rows else None

    def log(self, st, now, phase):
        self.rows.append(dict(
            t_s=round(now - self.t0, 4), kias=st["kias"], ktas=st["ktas"], g=st["loadFactor"],
            pitch=st["pitch"], bank=st["roll"], heading=st["heading"], alt_ft=st["heightFt"],
            alpha=st["alpha"], beta=st["beta"], vs_fpm=-st["sinkFpm"],
            ail=st["controls"]["aileron"], ele=st["controls"]["elevator"], rud=st["controls"]["rudder"],
            thr=st["controls"]["throttle"], phase=phase))

    def write_csv(self, path):
        cols = ["t_s", "kias", "ktas", "load_factor_g", "pitch_deg", "bank_deg", "heading_deg", "altitude_ft",
                "alpha_deg", "beta_deg", "vertical_speed_fpm", "aileron", "elevator", "rudder", "throttle", "phase"]
        key = dict(zip(cols, ["t_s", "kias", "ktas", "g", "pitch", "bank", "heading", "alt_ft",
                              "alpha", "beta", "vs_fpm", "ail", "ele", "rud", "thr", "phase"]))
        with open(path, "w") as fh:
            fh.write(",".join(cols) + "\n")
            for r in self.rows:
                out = []
                for c in cols:
                    v = r[key[c]]
                    out.append(v if isinstance(v, str) else (f"{v:.4f}" if c == "t_s" else f"{v:.3f}"))
                fh.write(",".join(out) + "\n")
        return path
