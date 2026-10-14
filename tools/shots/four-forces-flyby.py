#!/usr/bin/env python3
"""Four-forces FLY-BY render (Ty 2026-10-14).

A C-172 in cruise passes a camera at its own height; the airplane flies right to left; the clip is 10 s at 30 fps
with the abeam point at 5 s, and it is true 25 % slow motion (the history records at 120 Hz, played at 30).
Lift starts above the airframe and weight below it (vectorStyle forceEnds), so no arrow crosses the airplane.
"""
import json, math, os, socket, subprocess, sys, time

PORT = int(os.environ.get("AERO_PORT", "47840"))
OUT = os.environ.get("OUT", "/Users/tyfrisby/Desktop/Talk Flying Animations/Widget Shots")
TMP = "/private/tmp/aero-flyby"
FPS = 30
CLIP_S = 10.0           # the clip the viewer sees
SLOW = 0.25             # 25 % speed
RECORD = int(round(1 / SLOW))          # history multiplier: 4 → 120 Hz
FRAMES = int(round(CLIP_S * FPS))      # 300
FLIGHT_S = CLIP_S * SLOW               # 2.5 s of flight
ABEAM_FRAC = 0.5                       # abeam at 5 s of the clip
STAND_OFF_M = float(os.environ.get("DIST", "65"))   # camera, abeam, this far off the track (the shot's character)
ZOOM = float(os.environ.get("ZOOM", "3.0"))   # crowd lens = 50°/zoom, capped at 3
AIRCRAFT = os.environ.get("AIRCRAFT", "c172-like")
SETTLE = 0.05          # let the widget render once after a step/seek before the snapshot reads the frame


class W:
    def __init__(self, port=PORT, timeout=240):
        self.s = socket.create_connection(("127.0.0.1", port), timeout=15)
        self.s.settimeout(timeout)
        self.f = self.s.makefile("rwb")

    def cmd(self, **kw):
        self.f.write((json.dumps(kw) + "\n").encode())
        self.f.flush()
        line = self.f.readline()
        if not line:
            raise RuntimeError("widget closed the connection")
        r = json.loads(line)
        if not r.get("ok", True):
            raise RuntimeError(f"{kw} -> {r}")
        return r


def setup(w, frame_mode):
    w.cmd(cmd="view", frame=frame_mode)
    w.cmd(cmd="recordRate", rate=RECORD)
    w.cmd(cmd="start", condition="cruise", aircraft=AIRCRAFT)
    time.sleep(5)                                   # the cruise trim settles
    w.cmd(cmd="display", mode="classic")
    # ⚠ vectorStyle BEFORE fourForces: a label's text is only re-made when its NUMBER moves, so a weight drawn once
    # as "WEIGHT 2,300 lb" keeps that text for ever (weight never changes). Set the style before the first draw.
    # dragScale 1 = thrust and drag drawn on the SAME scale as lift and weight (the Ep4 four-forces look);
    # forceValues off = words-only labels; lightFill 0.25 = the C-172 lighting Ty settled on.
    w.cmd(cmd="vectorStyle", forceEnds=True, lightFill=0.25, forceValues=False, dragScale=1.0, width=2.0)
    w.cmd(cmd="fourForces", on=True)
    w.cmd(cmd="show", earthGrid=False, readout=False, labels=True, vectors=True,
          lift=True, weight=True, thrust=True, drag=True,
          wingWind=False, tailWind=False, inertial=False, total=False, tail=False, moments=False, wind=False)
    w.cmd(cmd="controlsDisplay", show=False)
    # fourForces brings up the two balance plates; the Ep4 four-forces shot is the airplane and the arrows alone.
    for ins in ("forceBalance", "forceTrend", "clAlpha", "liftDrag", "powerRequired", "ball", "aoa", "wb"):
        w.cmd(cmd="inset", name=ins, show=False)
    time.sleep(FLIGHT_S + 4)                        # fill the history either side of the abeam frame
    return w.cmd(cmd="pause")


def place_camera(w):
    """Abeam = the frame FLIGHT_S*(1-ABEAM_FRAC) before the live edge. The camera sits at that point's own
    altitude (level with the airplane), STAND_OFF_M abeam on the airplane's LEFT — measured: that is the side
    that puts the nose to the left, so the airplane tracks RIGHT TO LEFT across the picture."""
    # ⚠ review offsets are ALWAYS counted at 33.3 ms per stored frame, whatever the recordRate — so an offset in
    # those units IS clip time (300 stored frames = 10 s of clip = 2.5 s of flight at 120 Hz), not flight time.
    after_ms = CLIP_S * (1 - ABEAM_FRAC) * 1000.0
    w.cmd(cmd="seek", offsetMs=-after_ms)
    st = w.cmd(cmd="state")
    n, e, d = st["posNed"]
    psi = math.radians(st["heading"])
    left_n, left_e = math.sin(psi), -math.cos(psi)
    cam = [n + left_n * STAND_OFF_M, e + left_e * STAND_OFF_M, d]
    # ⚠ crowdNed must go ALONE: the server runs SetCrowd first, and a "name" in the same command then runs
    # SetView("crowd"), which nulls the fixed point and falls back to the default 450 m-left / 250 m-below spectator.
    w.cmd(cmd="view", zoom=ZOOM)
    w.cmd(cmd="view", crowdNed=cam)
    time.sleep(SETTLE * 4)
    return st, cam


def render(w, tag, frame_mode):
    os.makedirs(TMP, exist_ok=True)
    for f in os.listdir(TMP):
        os.remove(os.path.join(TMP, f))
    start_ms = CLIP_S * 1000.0   # (clip time: one stored frame per output frame)
    w.cmd(cmd="seek", offsetMs=-start_ms)
    rows = []
    time.sleep(SETTLE * 4)
    for i in range(FRAMES):
        if i:
            w.cmd(cmd="step", frames=1)
        # `snapshot` reads the LAST RENDERED frame, so the widget must draw once after the step (and after any
        # camera change) before the grab — otherwise every frame is the previous one's picture.
        time.sleep(SETTLE)
        st = w.cmd(cmd="state")
        fo = st["forces"]
        rows.append((i, fo["liftLb"], fo["weightLb"], fo["thrustLb"], fo["dragLb"],
                     st["kias"], st["alpha"], st["pitch"], -st.get("sinkFpm", 0)))
        w.cmd(cmd="snapshot", path=f"{TMP}/f{i:04d}.png")
        if i % 60 == 0:
            print(f"  {tag}: frame {i}/{FRAMES}", flush=True)
    name = f"Ep4 — Four forces fly-by, C-172 at 25% (level with the airplane){tag}"
    mov = os.path.join(OUT, name + ".mov")
    subprocess.run(["ffmpeg", "-y", "-framerate", str(FPS), "-i", f"{TMP}/f%04d.png",
                    "-c:v", "prores_ks", "-profile:v", "4444", "-pix_fmt", "yuva444p10le",
                    mov], check=True, capture_output=True)
    with open(os.path.join(OUT, name + " — telemetry.csv"), "w") as fh:
        fh.write("frame,liftLb,weightLb,thrustLb,dragLb,kias,alphaDeg,pitchDeg,verticalSpeedFpm\n")
        for r in rows:
            fh.write(f"{r[0]},{r[1]:.1f},{r[2]:.1f},{r[3]:.1f},{r[4]:.1f},{r[5]:.1f},{r[6]:.2f},{r[7]:.2f},{r[8]:.1f}\n")
    src = os.path.join(OUT, "Four forces — C-172 cruise (three-quarter side).credits.json")
    if os.path.exists(src):
        with open(src) as fh:
            creds = fh.read()
        with open(os.path.join(OUT, name + ".credits.json"), "w") as fh:
            fh.write(creds)
    print("WROTE", mov, flush=True)
    return mov


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "16:9"
    tag = "" if mode == "16:9" else " (square)"
    w = W()
    setup(w, mode)
    st, cam = place_camera(w)
    print(f"abeam at NED {st['posNed']}, heading {st['heading']:.0f}°, camera {cam}, {st['kias']:.0f} KIAS", flush=True)
    render(w, tag, mode)
