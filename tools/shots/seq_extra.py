"""FLIGHT B - Extra 300, the aerobatic training experience (Ty via Glass, 2026-10-15).

pushover, pull-level, half-loop, rolls, push-vertical, hammerhead, stall-touches, pullout.

Ty's sequence has one inconsistency: a half loop ends INVERTED, 1.5 rolls from inverted end UPRIGHT, and you can
only PUSH the nose up to vertical from inverted. Glass confirmed the reading used here - a half roll to upright on
the half-loop exit (an Immelmann finish), so the 1.5 rolls start upright, end inverted, and the push to vertical
is a genuine outside push at negative g. Every element and every g sign Ty named survives.

Everything is flown: the loops below move stick, rudder and throttle and the sim decides what the airplane does.
"""
import json, math, os, time
import aerofly, vg
from aerofly import clamp
from pilot import Pilot
from roll import SlowRoll

ZOOM = float(os.environ.get("ZOOM", "0.8"))
WORK_FT = float(os.environ.get("ALT", "9500"))
VS = float(os.environ.get("VS", "51.7"))        # measured in this sim (measure_env.py)
VNE_CFG = 113.2 * 1.943844                       # config limits.vneMs = 220.0 KIAS (published; owner 2026-10-15)
DIVE_TO = float(os.environ.get("DIVE_TO", "188"))  # the 2.5 g pull adds ~20 kt before the nose comes up,
# so diving to 188 peaks near 210 - "near Vne" on the 220 KIAS the config now carries
ROLL_ENTRY = float(os.environ.get("ROLL_ENTRY", "130"))   # the rolls need flying speed before they can start


def main():
    w = aerofly.W()
    f = aerofly.Flight(w, "extra-300-like", "cruise", hz=60)
    w.cmd(cmd="recordRate", rate=1)
    vg.setup_look(w, zoom=ZOOM)
    p = Pilot()

    # ---- setup (not recorded): up to the working height, pointing east -------------------------
    vg.climb_to(f, p, WORK_FT, 150, limit=200, vs_target=2600.0)   # an Extra climbs; 900 fpm wasted the ring
    st = vg.ap_goto(w, WORK_FT, 165.0, heading=90.0, limit=70)
    print(f"ready: {st['kias']:.0f} KIAS, {st['heightFt']:.0f} ft, hdg {st['heading']:.0f}")
    p.reset(); p.seed_elevator(st["controls"]["elevator"])
    alpha_level = st["alpha"]

    m = vg.Marks(w)
    m.start()

    # ---- 1. pushover to ~0 g, diving, gaining speed ---------------------------------------------
    print("pushover ...")
    m.mark("pushover")
    def pushover(st, t, dt):
        g_cmd = 0.0 if st["pitch"] > -45 else clamp(0.25 + 0.015 * (-45 - st["pitch"]), 0.0, 1.0)
        return dict(elevator=p.hold_g(st, g_cmd, dt), aileron=p.hold_bank(st, 0, dt),
                    rudder=p.coordinate(st, dt), throttle=1.0)
    f.fly(45, pushover, "pushover", stop=lambda s: s["kias"] >= DIVE_TO)
    st = f.state(); print(f"  {st['kias']:.0f} KIAS, pitch {st['pitch']:.0f}, {st['heightFt']:.0f} ft")

    # ---- 2. abrupt pull to level at ~2.5 g, near Vne --------------------------------------------
    print("pull-level ...")
    m.mark("pull-level")
    p.reset(); p.seed_elevator(st["controls"]["elevator"])
    f.fly(18, lambda st, t, dt: dict(
        elevator=p.hold_g(st, 2.5, dt), aileron=p.hold_bank(st, 0, dt),
        rudder=p.coordinate(st, dt), throttle=1.0), "pull-level", stop=lambda s: s["pitch"] >= -1)
    st = f.state(); print(f"  level at {st['kias']:.0f} KIAS, {st['heightFt']:.0f} ft")

    # ---- 3. half loop up, then half roll upright (Immelmann finish) ------------------------------
    print("half-loop ...")
    m.mark("half-loop")
    p.reset(); p.seed_elevator(st["controls"]["elevator"])
    stage = [0]        # 0 = looping, 1 = accelerating inverted, 2 = half roll to upright

    def halfloop(st, t, dt):
        # The top is slower than the 1-g stall speed and flown at ~0.5 g - that is what Ty asked for, and it is
        # fine as long as nothing demands more lift there. Rolling at 46 KIAS does: the wing departs at alpha 47
        # and the airplane tumbles. So the half roll WAITS until the airplane has accelerated, nose-down and
        # still inverted, back to flying speed.
        if stage[0] == 0:
            if abs(st["roll"]) > 150 and st["pitch"] < 12:
                stage[0] = 1
                p.reset(); p.seed_elevator(st["controls"]["elevator"])
            else:
                g_cmd = clamp((st["kias"] / VS) ** 2 * 0.75, 0.5, 5.0)
                return dict(elevator=p.hold_g(st, g_cmd, dt),
                            aileron=p.hold_bank(st, 0 if st["pitch"] < 55 else st["roll"], dt),
                            rudder=p.coordinate(st, dt), throttle=1.0)
        if stage[0] == 1:
            if st["kias"] >= ROLL_ENTRY:
                stage[0] = 2
                p.reset(); p.seed_elevator(st["controls"]["elevator"])
            else:
                # held inverted, small negative alpha, nose falling: this is the "gaining speed" part
                return dict(elevator=p.hold_alpha(st, -2.0, dt),
                            aileron=p.hold_bank(st, 180.0 if st["roll"] >= 0 else -180.0, dt),
                            rudder=p.coordinate(st, dt), throttle=1.0)
        return dict(elevator=p.hold_alpha(st, 1.0, dt), aileron=-0.6,
                    rudder=p.coordinate(st, dt), throttle=1.0)
    f.fly(60, halfloop, "half-loop", stop=lambda s: stage[0] == 2 and abs(s["roll"]) < 14)
    st = f.state(); print(f"  upright at {st['kias']:.0f} KIAS, {st['heightFt']:.0f} ft, roll {st['roll']:.0f}")
    if st["heightFt"] < 3500:
        raise RuntimeError(f"too low after the half loop ({st['heightFt']:.0f} ft)")

    # ---- 4. one and a half slow rolls ------------------------------------------------------------
    # Ty: top rudder at knife edge; a lot of rudder OPPOSITE the roll while inverted to kill the adverse yaw;
    # inverted targets the same angle of attack level flight used (symmetrical airfoil); and if the knife-edge
    # quarters sink, the negative alpha is made larger than the positive. The figure is a "slow roll" but the
    # rate may be anything - what matters is that altitude and heading hold. Measured: +15 ft, -1.1 deg.
    print("rolls ...")
    m.mark("rolls")
    p.reset(); p.seed_elevator(st["controls"]["elevator"])
    # The half loop exits slower than the 1-g stall speed - which is what Ty asked for - so the airplane has no
    # roll authority at all there. "Gaining speed" comes FIRST: wings level, full power, nose a touch low until
    # it is flying again, and only then the 540 deg. Rolling at 29 KIAS just falls out of the sky.
    ready = [False]
    sr = [None]

    def rolls(st, t, dt):
        if not ready[0]:
            # Start the figure from a settled, roughly level state. Beginning the roll while still descending
            # out of the half-loop exit is what made it snatch: the same stick is doing two jobs at once.
            # Roll as soon as it is flying. Waiting for a level path as well let it accelerate to 237 KIAS -
            # past the published Vne - before the figure even started.
            if st["kias"] >= ROLL_ENTRY:
                ready[0] = True
                p.reset(); p.seed_elevator(st["controls"]["elevator"])
                sr[0] = SlowRoll(p, max(1.0, st["alpha"]), st["heightFt"], st["heading"],
                                 aileron=0.42, direction=+1, k_adverse=0.45,
                                 k_top=0.50, k_hdg=0.030, neg_boost_gain=0.0060, kias_ref=130.0,
                                 vs_kias=VS, alpha_stall=14.0)
            else:
                # unstall first, then accelerate: hold a small positive alpha and let the nose fall
                return dict(elevator=p.hold_alpha(st, 4.0, dt), aileron=p.hold_bank(st, 0, dt),
                            rudder=p.coordinate(st, dt), throttle=1.0)

        sr[0].update_roll(st)
        return sr[0].controls(st, dt)
    f.fly(60, rolls, "rolls", stop=lambda s: ready[0] and sr[0] is not None and sr[0].rolled >= 540.0)
    st = f.state()
    rolled = sr[0].rolled if sr[0] else 0.0
    print(f"  rolled {rolled:.0f} deg, {st['kias']:.0f} KIAS, roll {st['roll']:.0f} (inverted), "
          f"{st['heightFt']:.0f} ft, hdg {st['heading']:.0f}")
    if st["heightFt"] < 2500:
        raise RuntimeError(f"too low after the rolls ({st['heightFt']:.0f} ft) to fly the rest")

    # ---- 5. push to vertical up, at negative g (outside, from inverted) --------------------------
    print("push-vertical ...")
    m.mark("push-vertical")
    p.reset(); p.seed_elevator(st["controls"]["elevator"])
    f.fly(22, lambda st, t, dt: dict(
        elevator=p.hold_g(st, -4.5, dt),
        aileron=p.hold_bank(st, 180.0 if st["roll"] >= 0 else -180.0, dt),
        rudder=p.coordinate(st, dt), throttle=1.0), "push-vertical", stop=lambda s: s["pitch"] >= 78)
    st = f.state(); print(f"  vertical: pitch {st['pitch']:.0f}, {st['kias']:.0f} KIAS, {st['heightFt']:.0f} ft")
    if st["pitch"] < 70:
        raise RuntimeError(f"the push never reached the vertical (pitch {st['pitch']:.0f})")

    # ---- 6. hammerhead: the speed runs out on the up-line, then the rudder pivots it --------------
    print("hammerhead ...")
    m.mark("hammerhead")
    p.reset(); p.seed_elevator(st["controls"]["elevator"])
    pivot = [False]
    t_piv = [None]
    done = [False]
    roll0 = [st["roll"]]

    def hammer(st, t, dt):
        # The UP-LINE is flown on attitude: commanding zero g lets the nose fall off the vertical and the
        # airplane tumbles down instead of standing on its line. 85 deg keeps clear of the Euler singularity
        # at 90. When the speed runs out, full rudder pivots it over the top.
        if not pivot[0] and st["kias"] < 45.0:
            pivot[0] = True
        if pivot[0]:
            if t_piv[0] is None:
                t_piv[0] = t
            if t - t_piv[0] > 7.0:          # the pivot has had its chance; the downline takes over
                done[0] = True
            return dict(elevator=0.05, aileron=0.0, rudder=-1.0, throttle=0.30)
        return dict(elevator=p.hold_pitch(st, 85.0, dt), aileron=p.hold_bank(st, roll0[0], dt),
                    rudder=p.coordinate(st, dt), throttle=1.0)
    f.fly(26, hammer, "hammerhead",
          stop=lambda s: done[0] or (pivot[0] and s["pitch"] < -45))
    st = f.state(); print(f"  pivoted: pitch {st['pitch']:.0f}, {st['kias']:.0f} KIAS, {st['heightFt']:.0f} ft")
    if st["heightFt"] < 2500:
        raise RuntimeError(f"too low after the hammerhead ({st['heightFt']:.0f} ft)")

    # ---- 7. push and pull to the stall on the downline, at rising speeds -------------------------
    print("stall-touches ...")
    m.mark("stall-touches")
    p.reset(); p.seed_elevator(st["controls"]["elevator"])
    sign = [1.0]
    tnext = [0.0]
    n = [0]

    going = [False]

    def touch(st, t, dt):
        # The pivot leaves it at ~38 KIAS pointing down. Commanding the stall boundary there just departs the
        # airplane (alpha ran to 100 deg). Fly the downline until it is properly flying, THEN start touching.
        if not going[0]:
            if st["kias"] >= 85.0:
                going[0] = True
                tnext[0] = t
                p.reset(); p.seed_elevator(st["controls"]["elevator"])
            else:
                return dict(elevator=p.hold_alpha(st, 0.0, dt), aileron=p.hold_bank(st, 0, dt),
                            rudder=p.coordinate(st, dt), throttle=0.25)
        if t >= tnext[0]:
            sign[0] = -sign[0]
            n[0] += 1
            tnext[0] = t + 1.5
        # n = (V/Vs)^2 is where the wing lets go at this speed: ride just past it, both signs, as speed builds
        g_edge = clamp((st["kias"] / VS) ** 2 * 1.04, 1.0, 8.0)
        return dict(elevator=p.hold_g(st, sign[0] * g_edge, dt), aileron=p.hold_bank(st, 0, dt),
                    rudder=p.coordinate(st, dt), throttle=0.25)
    f.fly(13, touch, "stall-touches")
    st = f.state(); print(f"  {n[0]} touches, {st['kias']:.0f} KIAS, {st['heightFt']:.0f} ft")
    if st["heightFt"] < 1200:
        raise RuntimeError(f"too low after the stall touches ({st['heightFt']:.0f} ft)")

    # ---- 8. pull out to level --------------------------------------------------------------------
    print("pullout ...")
    m.mark("pullout")
    p.reset(); p.seed_elevator(st["controls"]["elevator"])

    def pullout(st, t, dt):
        vs = -st["sinkFpm"]
        g_cmd = 4.0 if st["pitch"] < -10 else clamp(1.0 + 0.0008 * (0.0 - vs), 0.8, 2.5)
        return dict(elevator=p.hold_g(st, g_cmd, dt), aileron=p.hold_bank(st, 0, dt),
                    rudder=p.coordinate(st, dt), throttle=0.7)
    f.fly(30, pullout, "pullout",
          stop=lambda s: s["pitch"] > -3 and abs(s["roll"]) < 10 and abs(s["sinkFpm"]) < 500)
    st = f.state(); print(f"  level: {st['kias']:.0f} KIAS, {st['heightFt']:.0f} ft")

    n_fr = m.frames()
    print(f"\nflown: {n_fr} frames = {n_fr/30:.1f} s; rendering ...")
    mov, csv, rows = vg.render(w, m, "extra300", px=int(os.environ.get("PX", "512")))
    print(f"WROTE {mov}\nWROTE {csv}")
    gs = [r[1]["loadFactor"] for r in rows]; ks = [r[1]["kias"] for r in rows]
    print(f"  {len(rows)} rows/frames, {len(rows)/30:.1f} s, g {min(gs):.2f}..{max(gs):.2f}, "
          f"KIAS {min(ks):.1f}..{max(ks):.1f}")
    for _, ph in m.marks:
        rs = [r for r in rows if r[2] == ph]
        if rs:
            print(f"  {ph:<14} {len(rs):5d} fr {rs[0][0]:6.2f}-{rs[-1][0]:6.2f} s  "
                  f"g {min(r[1]['loadFactor'] for r in rs):6.2f}..{max(r[1]['loadFactor'] for r in rs):5.2f}  "
                  f"KIAS {min(r[1]['kias'] for r in rs):5.1f}..{max(r[1]['kias'] for r in rs):5.1f}")


if __name__ == "__main__":
    main()
