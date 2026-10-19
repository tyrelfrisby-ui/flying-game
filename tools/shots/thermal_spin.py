"""A spin out of a thermal turn, for Talk Flying Ep4 (Ty via Glass, 2026-10-19).

Ty's shot: "a real spin while in a thermal ... no bubbles and an outside view from the south ... no vectors no
bubbles just a clean spinning out of a tight thermal climb by stalling and using too much inside rudder."

Everything is FLOWN. The loops below move the stick and the rudder; the sim decides what the aeroplane does, and
the spin is the wing letting go, not a keyframe. The camera is world-fixed due south of the aeroplane: it follows
the aeroplane's POSITION but never turns with it, so a spin reads as a spin and not as a camera move.

  AC=<id> BANK=40 KIAS=56 python3 thermal_spin.py

Notes measured while building this (2026-10-19):
  * The angle of attack must be read AIR-relative. In a 4 m/s core at 30 m/s the ground-relative angle is 8 deg
    wrong, which hides the stall completely.
  * Full aft stick does NOT slow a glider in a steep thermal turn: at 40-53 deg of bank the Swift settles at
    alpha 7-8 against a 14 deg stall, because the turn's own pitch rate eats the elevator. The speed has to be
    FLOWN down first, which is what a pilot slowing in a thermal does anyway.
"""
import json, math, os, subprocess, time
import aerofly
from aerofly import clamp
from pilot import Pilot

AC = os.environ.get("AC", "glider-swift-s1-like")
NAME = os.environ.get("NAME", "swift-thermal-spin")
OUT = os.path.expanduser(os.environ.get("OUT", "~/Movies/Talk Flying Ep4 (media)/aero"))
TMP = "/private/tmp/aero-ep4"
BANK = float(os.environ.get("BANK", "40"))        # the thermal circle, degrees (right-hand: core to the right)
KIAS = float(os.environ.get("KIAS", "56"))        # the circling speed; min sink x sqrt(1/cos bank)
SLOW_TO = float(os.environ.get("SLOW_TO", "0.62"))  # fraction of it to fly down to before the wing lets go
ZOOM = float(os.environ.get("ZOOM", "1.30"))
CLIMB_KT = float(os.environ.get("CLIMB_KT", "8"))   # thermal core
CORE_M = float(os.environ.get("CORE_M", "150"))     # tight
AGL_FT = float(os.environ.get("AGL_FT", "5000"))    # the spin's room: it loses ~750 ft a turn
SPIN_S = float(os.environ.get("SPIN_S", "14"))
SLOW_S = float(os.environ.get("SLOW_S", "8"))
ENTRY_S = float(os.environ.get("ENTRY_S", "8"))
HIST_HZ = 30.0
FRAME_MS = 1000.0 / HIST_HZ
INSETS = ("forceBalance", "forceTrend", "clAlpha", "liftDrag", "powerRequired", "ball", "aoa", "wb")


def bank_g(st, kias_cmd):
    """The load factor a level turn at this bank needs, plus the speed error.

    THE SIGN MATTERS AND IT IS NOT THE OBVIOUS ONE. A glider has no throttle: too fast is corrected by PULLING,
    not by easing off. Written the other way round (less g when fast) the Swift ran away to 161 KIAS — the stick
    kept unloading the wing the faster it went, which is a dive, not a speed hold."""
    return clamp(1.0 / max(0.25, math.cos(math.radians(clamp(st["roll"], -75, 75))))
                 + 0.025 * (st["kias"] - kias_cmd), 0.4, 3.5)


def look(w):
    """Nothing on screen but the aeroplane, and a camera that does not chase it."""
    w.cmd(cmd="view", frame="16:9")
    w.cmd(cmd="display", mode="classic")
    w.cmd(cmd="fourForces", on=False)
    w.cmd(cmd="show", earthGrid=False, readout=False, labels=False, vectors=False, smoke=False,
          lift=False, weight=False, thrust=False, drag=False, total=False, inertial=False, tail=False,
          moments=False, wind=False, wingWind=False, tailWind=False, horizon=False, axis=False,
          flightPath=False, groundTrack=False, propEffects=False)
    w.cmd(cmd="controlsDisplay", show=False)
    for ins in INSETS:
        w.cmd(cmd="inset", name=ins, show=False)
    w.cmd(cmd="view", name="orbit")
    # orbitDeg is measured from NORTH with orbitWorld on: 180 = the camera due SOUTH looking north. elev 0 = level.
    w.cmd(cmd="view", orbitWorld=True, orbitDeg=180, orbitElev=0)
    w.cmd(cmd="view", zoom=ZOOM)


def settle(w, bank, kias, agl_ft, limit=120):
    """Set the circle up with the widget's OWN autopilot, then hand back: CWS bank hold on the roll axis and FLC
    (speed on pitch) on the pitch axis. Six hand-flown entry loops rolled the Swift inverted and into the ground —
    the autopilot moves the same stick and the same rudder, the physics still flies the aeroplane, and it is used
    only to SET UP, never during a recorded sequence (the rule vg.py set on Ep1).

    The height comes from one application of the altitude lock: it pins the aeroplane at agl_ft, and then it is
    turned OFF again, so the recorded circle climbs for real and the recorded spin descends for real."""
    w.cmd(cmd="altitudeLock", on=True, aglFt=agl_ft)
    time.sleep(0.5)
    w.cmd(cmd="altitudeLock", on=False)
    w.cmd(cmd="autopilot", on=True, mode="flc", kias=kias, altitudeFt=agl_ft + 4000)
    ap = w.cmd(cmd="autopilot", bankDeg=bank)["autopilot"]
    print(f"  [AP {ap.get('fma')}]")
    if not ap["on"]:
        raise RuntimeError(f"autopilot refused to engage: {ap}")
    t0 = time.time()
    steady = 0
    while time.time() - t0 < limit:
        time.sleep(1.0)
        st = w.cmd(cmd="state")
        if abs(st["roll"] - bank) < 4 and abs(st["kias"] - kias) < 4:
            steady += 1
            if steady >= 6:
                break
        else:
            steady = 0
    else:
        raise RuntimeError(f"the circle never settled: {st['roll']:.0f} deg bank, {st['kias']:.0f} KIAS")
    w.cmd(cmd="autopilot", on=False)
    time.sleep(0.3)
    return w.cmd(cmd="state")


class Marks:
    CAP_MS = 15000 * FRAME_MS

    def __init__(self, w):
        self.w, self.h0, self.marks = w, None, []

    def start(self):
        self.h0 = self.w.cmd(cmd="state")["review"]["historyMs"]
        self.t0 = time.time()
        room = self.CAP_MS - self.h0
        print(f"  [history: {self.h0/1000:.0f} s used, {room/1000:.0f} s of room]")
        if room < 60000:
            raise RuntimeError(f"only {room/1000:.0f} s of review history left; the ring would wrap")

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
        h = self.w.cmd(cmd="state")["review"]["historyMs"]
        n = int(round((h - self.h0) / FRAME_MS))
        if h >= self.CAP_MS - FRAME_MS:
            raise RuntimeError(f"review history WRAPPED at {h/1000:.0f} s: re-fly with a shorter setup")
        print(f"  [sequence {n} frames = {n/HIST_HZ:.1f} s]")
        return n


def render(w, marks, name, fps=30):
    """ONE pass over the review history: every stored frame read and grabbed, so the CSV row and the video frame
    are the same instant. The camera is world-fixed, so it needs no per-frame command."""
    os.makedirs(TMP, exist_ok=True)
    for fn in os.listdir(TMP):
        fp = os.path.join(TMP, fn)
        if os.path.isfile(fp):
            os.remove(fp)
    os.makedirs(OUT, exist_ok=True)
    n = marks.frames()
    w.cmd(cmd="pause")
    w.cmd(cmd="seek", offsetMs=-n * FRAME_MS)
    time.sleep(0.2)
    rows = []
    for i in range(n):
        if i:
            w.cmd(cmd="step", frames=1)
        time.sleep(0.034)            # the widget must DRAW once: snapshot reads the last rendered frame
        st = w.pipe({"cmd": "state"}, {"cmd": "snapshot", "path": f"{TMP}/f{i:05d}.png"})[0]
        rows.append((i / fps, st, marks.phase_at(i)))
        if i % 150 == 0:
            print(f"    frame {i}/{n}", flush=True)
    # The CSV first: it is the measurement, and it must not depend on a convenience step finishing.
    csv = os.path.join(OUT, f"{name}.csv")
    with open(csv, "w") as fh:
        fh.write("t_s,kias,ktas,load_factor_g,pitch_deg,bank_deg,heading_deg,altitude_ft,"
                 "alpha_deg,beta_deg,vertical_speed_fpm,phase\n")
        for t, st, ph in rows:
            fh.write(f"{t:.4f},{st['kias']:.2f},{st['ktas']:.2f},{st['loadFactor']:.3f},{st['pitch']:.2f},"
                     f"{st['roll']:.2f},{st['heading']:.2f},{st['heightFt']:.1f},{st['alpha']:.2f},"
                     f"{st['beta']:.2f},{-st['sinkFpm']:.0f},{ph}\n")
    mov = os.path.join(OUT, f"{name}.mov")
    subprocess.run(["ffmpeg", "-y", "-framerate", str(fps), "-i", f"{TMP}/f%05d.png",
                    "-c:v", "prores_ks", "-profile:v", "4444", "-pix_fmt", "yuva444p10le", mov],
                   check=True, capture_output=True)
    # A reference mp4: yuv420p has no alpha, so the transparency flattens onto black. (A "color" source with no
    # duration never ends — overlaying one made ffmpeg generate frames for ever.)
    mp4 = os.path.join(OUT, f"{name}.mp4")
    subprocess.run(["ffmpeg", "-y", "-framerate", str(fps), "-i", f"{TMP}/f%05d.png",
                    "-vf", "format=yuv420p", "-c:v", "libx264", "-crf", "17", mp4],
                   check=True, capture_output=True)
    return mov, mp4, csv, rows


def main():
    w = aerofly.W()
    print(f"widget {w.cmd(cmd='hello')['version']}")
    f = aerofly.Flight(w, AC, "cruise", hz=60)
    w.cmd(cmd="recordRate", rate=1)
    look(w)
    p = Pilot()

    # ---- setup (not recorded) ------------------------------------------------------------------
    st = f.state()
    print(f"start: {st['kias']:.0f} KIAS, {st['heightFt']:.0f} ft")
    st = settle(w, BANK, KIAS, AGL_FT)
    print(f"circling: {st['roll']:.0f} deg bank, {st['kias']:.0f} KIAS, {st['heightFt']:.0f} ft, "
          f"alpha {st['alpha']:.1f}, {-st['sinkFpm']:+.0f} fpm")
    # The thermal is centred on the circle the aeroplane is turning NOW, so it goes on once the turn is established.
    w.cmd(cmd="thermal", on=True, climbKt=CLIMB_KT, radiusM=CORE_M)
    p.reset(); p.seed_elevator(st["controls"]["elevator"])
    # Hand flying before the camera rolls, until the hand-flown circle is as settled as the autopilot left it.
    # The handover is the loose joint in this: left at a fixed 6 s it sometimes started the recording 15 kt fast,
    # and a fast entry is what turns the departure into a tumble.
    def steady(s):
        return abs(s["kias"] - KIAS) < 4 and abs(s["roll"] - BANK) < 4 and abs(s["sinkFpm"]) < 900
    for attempt in range(4):
        f.fly(8, lambda st, t, dt: dict(
            elevator=p.hold_g(st, bank_g(st, KIAS), dt), aileron=p.hold_bank(st, BANK, dt),
            rudder=p.coordinate(st, dt), throttle=-1.0), "settle")
        if steady(f.state()):
            break
        print(f"  [handover not settled: {f.state()['kias']:.0f} KIAS, bank {f.state()['roll']:.0f} — flying on]")
    f.rows.clear(); f.t0 = None
    st = f.state()
    if not steady(st):
        raise RuntimeError(f"the circle never settled after the handover: {st['kias']:.0f} KIAS, bank {st['roll']:.0f}")
    print(f"in the core: {st['heightFt']:.0f} ft, {-st['sinkFpm']:+.0f} fpm, alpha {st['alpha']:.1f}, "
          f"{st['kias']:.0f} KIAS, bank {st['roll']:.0f}")

    m = Marks(w)
    m.start()

    # ---- 1. the tight thermal climb (recorded) --------------------------------------------------
    print("thermal ...")
    m.mark("thermal")
    f.fly(4, lambda st, t, dt: dict(
        elevator=p.hold_g(st, bank_g(st, KIAS), dt), aileron=p.hold_bank(st, BANK, dt),
        rudder=p.coordinate(st, dt), throttle=-1.0), "thermal")

    # ---- 2. slowing in the circle, with too much inside rudder ----------------------------------
    # Inside = the bottom pedal = RIGHT in a right-hand circle. It goes in as the circle slows, and stays in:
    # that is the mistake being filmed.
    print("slowing ...")
    m.mark("slow")
    st = f.state()
    vcmd = [st["kias"]]
    floor = KIAS * SLOW_TO

    def slow(st, t, dt):
        vcmd[0] = max(floor, vcmd[0] - dt * (KIAS - floor) / 8.0)
        inside = clamp((st["alpha"] - 7.5) / 3.5, 0.0, 1.0)
        return dict(elevator=p.hold_g(st, bank_g(st, vcmd[0]), dt), aileron=p.hold_bank(st, BANK, dt),
                    rudder=clamp(p.coordinate(st, dt) + inside, -1, 1), throttle=-1.0)
    f.fly(SLOW_S, slow, "slow", stop=lambda s: s["alpha"] > 13.0)
    st = f.state()
    print(f"  slowed to {st['kias']:.0f} KIAS, alpha {st['alpha']:.1f}, bank {st['roll']:.0f}, {st['heightFt']:.0f} ft")

    # ---- 3. the stick comes back to the stop, and the wing lets go ------------------------------
    # NOT slammed. Jamming the stick and the rudder to the stops together at 47 KIAS does not spin this
    # aeroplane, it TUMBLES it: measured alpha 97 deg and the bank through +-175, which is the airframe going
    # sideways, not autorotating. Three seconds to the stop is both what a pilot does and what measures as a
    # spin (the headless entry, 2026-10-19).
    print("entry ...")
    m.mark("entry")
    ele0 = [f.state()["controls"]["elevator"]]

    def entry(st, t, dt):
        ramp = min(1.0, t / 3.0)
        return dict(elevator=ele0[0] + ramp * (-1.0 - ele0[0]),
                    aileron=p.hold_bank(st, BANK, dt) * (1.0 - ramp),
                    rudder=1.0, throttle=-1.0)
    f.fly(ENTRY_S, entry, "entry", stop=lambda s: s["alpha"] > 18.0)
    st = f.state()
    print(f"  let go at {st['kias']:.0f} KIAS, alpha {st['alpha']:.1f}, bank {st['roll']:.0f}, {st['heightFt']:.0f} ft")

    # ---- 4. the spin: pro-spin controls held, nothing else -------------------------------------
    print("spinning ...")
    m.mark("spin")
    f.fly(SPIN_S, lambda st, t, dt: dict(elevator=-1.0, aileron=0.0, rudder=1.0, throttle=-1.0), "spin")
    st = f.state()
    print(f"  after {SPIN_S:.0f} s: alpha {st['alpha']:.0f}, pitch {st['pitch']:.0f}, {st['kias']:.0f} KIAS, {st['heightFt']:.0f} ft")

    mov, mp4, csv, rows = render(w, m, NAME)
    turns = 0.0
    prev = None
    for _, s, _ in rows:
        if prev is not None:
            d = ((s["heading"] - prev + 540) % 360) - 180
            turns += d / 360.0
        prev = s["heading"]
    print(f"\n{mov}\n{mp4}\n{csv}")
    print(f"  {len(rows)} frames = {len(rows)/30:.1f} s, net heading change {turns:+.2f} turns")
    f.write_csv(os.path.join(OUT, f"{NAME}-flown.csv"))


if __name__ == "__main__":
    main()
