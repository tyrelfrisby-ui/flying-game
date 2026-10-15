"""V-g diagram flights for Talk Flying Ep1 (Ty via Glass, 2026-10-15).

Shared machinery: the marker look, a world-fixed camera, flying the airplane east, and then ONE pass over the
review history that writes the CSV and the frames together - so CSV row i IS video frame i, and video time 0 is
t_s = 0. Everything is flown; nothing sets an attitude directly.
"""
import json, math, os, subprocess, time
import aerofly
from aerofly import clamp
from pilot import Pilot

OUT = os.path.expanduser("~/Movies/Talk Flying Ep1 Re-edit (media)/aero")
TMP = "/private/tmp/aero-vg"
HIST_HZ = 30.0            # WidgetReview.Hz - the history always stores at 30 Hz
FRAME_MS = 1000.0 / HIST_HZ
INSETS = ("forceBalance", "forceTrend", "clAlpha", "liftDrag", "powerRequired", "ball", "aoa", "wb")


def setup_look(w, zoom=0.8):
    """The marker look: the airplane alone on transparent, lit the way Ty asked (the default rig washed the
    white 172 out), and a camera whose BEARING IS FIXED IN THE WORLD - due south of the airplane, level with it,
    following its position but never turning with it."""
    w.cmd(cmd="view", frame="square")
    w.cmd(cmd="display", mode="classic")
    # vectorStyle before anything is drawn: a label's text is only remade when its number moves.
    w.cmd(cmd="vectorStyle", lightFill=0.25, width=2.0)
    w.cmd(cmd="fourForces", on=False)
    w.cmd(cmd="show", earthGrid=False, readout=False, labels=False, vectors=False, smoke=False,
          lift=False, weight=False, thrust=False, drag=False, total=False, inertial=False, tail=False,
          moments=False, wind=False, wingWind=False, tailWind=False, horizon=False, axis=False,
          flightPath=False, groundTrack=False, propEffects=False)
    w.cmd(cmd="controlsDisplay", show=False)
    for ins in INSETS:
        w.cmd(cmd="inset", name=ins, show=False)
    w.cmd(cmd="view", name="orbit")
    # orbitDeg is measured from NORTH when orbitWorld is on: 180 = the camera due south, looking north, so an
    # airplane heading east crosses the frame LEFT TO RIGHT. elev 0 = level with it.
    w.cmd(cmd="view", orbitWorld=True, orbitDeg=180, orbitElev=0)
    w.cmd(cmd="view", zoom=zoom)          # orbit distance = (max(span,length)*2.6 + 6)/zoom: < 1 backs the camera off


def fly_east(f, p, kias, alt_ft=None, limit=120):
    """Turn onto east (090) and settle there - the start direction Ty asked for. Not recorded."""
    w = f.w
    st = f.state()
    alt_ft = alt_ft if alt_ft is not None else st["heightFt"]
    t0 = time.time()
    while time.time() - t0 < limit:
        err = (90.0 - st["heading"] + 540) % 360 - 180
        bank = clamp(err * 1.2, -25, 25)
        u = dict(elevator=p.hold_g(st, p.level(st, alt_ft, 1 / 60.0), 1 / 60.0),
                 aileron=p.hold_bank(st, bank, 1 / 60.0), rudder=p.coordinate(st, 1 / 60.0),
                 throttle=p.hold_speed(st, kias, 1 / 60.0, base=0.6))
        st = w.pipe({"cmd": "controls", **u}, {"cmd": "state"})[1]
        if abs(err) < 1.0 and abs(st["roll"]) < 1.5 and abs(st["kias"] - kias) < 4:
            break
    return st


def climb_to(f, p, alt_ft, kias, limit=240, vs_target=900.0):
    """Climb to the working altitude under power. Not recorded.

    vs_target matters: the setup shares the 500 s review ring with the sequence, and climbing an Extra at a
    trainer's 900 fpm burned 490 s of it before the first recorded frame."""
    w = f.w
    st = f.state()
    t0 = time.time()
    while time.time() - t0 < limit and st["heightFt"] < alt_ft - 60:
        err = (90.0 - st["heading"] + 540) % 360 - 180
        u = dict(elevator=p.hold_g(st, clamp(1.0 + 0.0006 * (vs_target - (-st["sinkFpm"])), 0.6, 1.8), 1 / 60.0),
                 aileron=p.hold_bank(st, clamp(err * 1.2, -20, 20), 1 / 60.0),
                 rudder=p.coordinate(st, 1 / 60.0), throttle=1.0)
        st = w.pipe({"cmd": "controls", **u}, {"cmd": "state"})[1]
    return st


def ap_slow_flight(w, kias, alt_ft=None, heading=90.0, limit=170):
    """Settle at a slow speed using the widget's OWN autopilot, then hand back.

    Six hand-built entry loops all parked the 172 at ~64 KIAS; the autopilot holds 55.0 KIAS level at 1 g on
    41 % power, so the speed was always flyable and the loop was the problem. The AP moves the same controls -
    the physics still flies the airplane - and it is only used to set up, never during a recorded sequence."""
    st = w.cmd(cmd="state")
    alt_ft = st["heightFt"] if alt_ft is None else alt_ft
    # Hand the airplane over cleanly: a stick input DISCONNECTS the autopilot (protocol 8 "AP DISC"), so let
    # the last manual command land, then engage and check it actually took.
    time.sleep(0.5)
    w.cmd(cmd="autopilot", on=True, mode="alt", altitudeFt=alt_ft)
    w.cmd(cmd="autopilot", mode="hdg", heading=heading)
    ap = w.cmd(cmd="autopilot", autothrottle=True, kias=kias)["autopilot"]
    print(f"  [AP {ap['fma']} | on={ap['on']} disc={ap['disc']}]")
    if not ap["on"]:
        raise RuntimeError(f"autopilot refused to engage: {ap}")
    t0 = time.time()
    steady = 0
    while time.time() - t0 < limit:
        time.sleep(1.0)
        st = w.cmd(cmd="state")
        # The condition has to HOLD. The speed sweeps down through the target about 25 s in and keeps falling;
        # releasing the autopilot there leaves the airplane to drift back to its unpowered trim (~63 KIAS).
        if abs(st["kias"] - kias) < 1.2 and abs(st["sinkFpm"]) < 90 and abs(st["roll"]) < 3:
            steady += 1
            if steady >= 8:
                break
        else:
            steady = 0
    else:
        raise RuntimeError(f"slow flight never settled: {st['kias']:.1f} KIAS, {-st['sinkFpm']:+.0f} fpm")
    w.cmd(cmd="autopilot", on=False)
    w.cmd(cmd="autopilot", autothrottle=False)
    time.sleep(0.3)
    return w.cmd(cmd="state")


def recover_from_stall(w, p, kias_target=120.0, limit=90):
    """Ty's recovery: unload from the stall to ~10 deg AoA, hold that until the descent stops, then ease the
    angle of attack off as the speed builds. Never hand a STALLED airplane to the autopilot and ask it to climb -
    it holds the nose up, stays stalled and mushes to the ground."""
    from aerofly import clamp as _c
    st = w.cmd(cmd="state")
    p.reset()
    p.seed_elevator(st["controls"]["elevator"])
    level = [False]
    a_cmd = [10.0]
    t0 = time.time()
    while time.time() - t0 < limit:
        vs = -st["sinkFpm"]
        if not level[0]:
            if vs > -120.0:
                level[0] = True
            a = 10.0
        else:
            a_cmd[0] = _c(a_cmd[0] + 0.0012 * (0.0 - vs), 0.5, 10.0)
            a = a_cmd[0]
        u = dict(elevator=p.hold_alpha(st, a, 1 / 60.0), aileron=p.hold_bank(st, 0.0, 1 / 60.0),
                 rudder=p.coordinate(st, 1 / 60.0), throttle=1.0)
        st = w.pipe({"cmd": "controls", **u}, {"cmd": "state"})[1]
        if st["kias"] >= kias_target and abs(st["sinkFpm"]) < 250 and abs(st["roll"]) < 6:
            break
    p.reset()
    return st


def ap_goto(w, alt_ft, kias, heading=None, limit=240):
    """Climb or descend to an altitude and speed under the widget's own autopilot, then hand back."""
    w.cmd(cmd="autopilot", on=True, mode="alt", altitudeFt=alt_ft)
    if heading is not None:
        w.cmd(cmd="autopilot", mode="hdg", heading=heading)
    w.cmd(cmd="autopilot", autothrottle=True, kias=kias)
    t0 = time.time()
    steady = 0
    while time.time() - t0 < limit:
        time.sleep(1.0)
        st = w.cmd(cmd="state")
        if abs(st["heightFt"] - alt_ft) < 250 and abs(st["kias"] - kias) < 8 and abs(st["roll"]) < 5:
            steady += 1
            if steady >= 4:
                break
        else:
            steady = 0
    w.cmd(cmd="autopilot", on=False)
    w.cmd(cmd="autopilot", autothrottle=False)
    time.sleep(0.3)
    return w.cmd(cmd="state")


class Marks:
    """Phase boundaries, recorded as review-history positions so the render can label every frame."""

    def __init__(self, w):
        self.w = w
        self.h0 = None
        self.marks = []       # (frame_index_from_start, phase)

    CAP_MS = 15000 * FRAME_MS      # WidgetReview.Capacity frames: the ring holds 500 s and then overwrites

    def start(self):
        self.h0 = self.w.cmd(cmd="state")["review"]["historyMs"]
        self.t0 = time.time()
        room = self.CAP_MS - self.h0
        print(f"  [history: {self.h0/1000:.0f} s used, {room/1000:.0f} s of room for the sequence]")
        if room < 120000:
            raise RuntimeError(f"only {room/1000:.0f} s of review history left - setup was too long; "
                               "the ring would wrap and every frame index would shift")

    def mark(self, phase):
        h = self.w.cmd(cmd="state")["review"]["historyMs"]
        self.marks.append((int(round((h - self.h0) / FRAME_MS)), phase))

    def phase_at(self, i):
        ph = self.marks[0][1] if self.marks else ""
        for idx, name in self.marks:
            if i >= idx:
                ph = name
        return ph

    def frames(self):
        """Stored-frame count for the sequence. The history stores one frame per 1/30 s of SIMULATION time
        (WidgetReview.Record accumulates simDt), so historyMs/FrameMs is exact and the clip is real time at
        30 fps. Note the sim is NOT wall-clock locked - it advances 1/30 s per rendered frame, so at ~33 fps
        it runs ~10 % faster than the wall clock. Wall time is therefore not a check on this; a WRAP is."""
        h = self.w.cmd(cmd="state")["review"]["historyMs"]
        wall = (time.time() - self.t0) * HIST_HZ
        n = int(round((h - self.h0) / FRAME_MS))
        if h >= self.CAP_MS - FRAME_MS:
            raise RuntimeError(f"review history WRAPPED ({h/1000:.0f} s at the {self.CAP_MS/1000:.0f} s cap): "
                               "the start of the sequence has been overwritten and every frame index would "
                               "shift. Shorten the setup and re-fly.")
        print(f"  [sequence {n} frames = {n/HIST_HZ:.1f} s sim ({wall/HIST_HZ:.1f} s wall); "
              f"history {h/1000:.0f} s of {self.CAP_MS/1000:.0f} s]")
        return n


def render(w, marks, name, fps=30, px=512):
    """ONE pass over the review history: each stored frame is read and grabbed, so the CSV row and the video
    frame are the same instant. The orbit camera is world-fixed, so it needs no per-frame command."""
    os.makedirs(TMP, exist_ok=True)
    for fn in os.listdir(TMP):
        fp = os.path.join(TMP, fn)
        if os.path.isfile(fp):
            os.remove(fp)
    n = marks.frames()
    w.cmd(cmd="pause")
    w.cmd(cmd="seek", offsetMs=-n * FRAME_MS)
    time.sleep(0.2)
    rows = []
    for i in range(n):
        if i:
            w.cmd(cmd="step", frames=1)
        time.sleep(0.034)                  # the widget must DRAW once: snapshot reads the last rendered frame
        # state and snapshot both refer to the frame now shown, so they go together in one round trip
        st = w.pipe({"cmd": "state"}, {"cmd": "snapshot", "path": f"{TMP}/f{i:05d}.png"})[0]
        rows.append((i / fps, st, marks.phase_at(i)))
        if i % 300 == 0:
            print(f"    frame {i}/{n}", flush=True)
    mov = os.path.join(OUT, f"{name}-marker.mov")
    subprocess.run(["ffmpeg", "-y", "-framerate", str(fps), "-i", f"{TMP}/f%05d.png",
                    "-vf", f"scale={px}:{px}", "-c:v", "prores_ks", "-profile:v", "4444",
                    "-pix_fmt", "yuva444p10le", mov], check=True, capture_output=True)
    csv = os.path.join(OUT, f"{name}.csv")
    with open(csv, "w") as fh:
        fh.write("t_s,kias,ktas,load_factor_g,pitch_deg,bank_deg,heading_deg,altitude_ft,"
                 "alpha_deg,beta_deg,vertical_speed_fpm,phase\n")
        for t, st, ph in rows:
            fh.write(f"{t:.4f},{st['kias']:.2f},{st['ktas']:.2f},{st['loadFactor']:.3f},{st['pitch']:.2f},"
                     f"{st['roll']:.2f},{st['heading']:.2f},{st['heightFt']:.1f},{st['alpha']:.2f},"
                     f"{st['beta']:.2f},{-st['sinkFpm']:.0f},{ph}\n")
    return mov, csv, rows
