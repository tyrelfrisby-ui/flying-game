"""FLIGHT A - Cessna 172, the normal training experience. Phases: accel, turn-left, turn-right, cruise, stall."""
import json, math, os, time
import aerofly, vg
from aerofly import clamp
from pilot import Pilot

ZOOM = float(os.environ.get("ZOOM", "0.8"))
QUICK = os.environ.get("QUICK") == "1"        # stall logic only, no render - for tuning



def unwrap(prev, h):
    d = h - prev
    while d > 180: d -= 360
    while d < -180: d += 360
    return d


def main():
    w = aerofly.W()
    f = aerofly.Flight(w, "c172-like", "cruise", hz=60)
    w.cmd(cmd="recordRate", rate=1)          # history at 30 Hz = the render's frame rate, 1 frame : 1 row
    vg.setup_look(w, zoom=ZOOM)
    p = Pilot()

    # ---- setup (not recorded): onto east, then slow to just above the stall ----------------------
    # Setup is deliberately short: it all lands in the 500 s review ring alongside the sequence, and the
    # autopilot does heading, altitude and speed in one go - a manual turn east and climb first is 70 s wasted.
    st = f.state()
    print(f"start: hdg {st['heading']:.0f}, {st['kias']:.0f} KIAS, {st['heightFt']:.0f} ft")
    alt0 = st["heightFt"]
    t0 = time.time()
    while time.time() - t0 < 70 and st["kias"] > 70:      # idle bleed first: the AP alone takes ~120 s from cruise
        vs = -st["sinkFpm"]
        u = dict(elevator=p.hold_g(st, clamp(1.0 + 0.0010 * (0.0 - vs), 0.7, 1.4), 1/60),
                 aileron=p.hold_bank(st, 0, 1/60), rudder=p.coordinate(st, 1/60), throttle=0.0)
        st = w.pipe({"cmd": "controls", **u}, {"cmd": "state"})[1]
    st = vg.ap_slow_flight(w, 55.0, alt_ft=alt0, heading=90.0)
    if st["heightFt"] < 900:
        raise RuntimeError(f"slow flight ended at {st['heightFt']:.0f} ft - too low to fly the sequence")
    alt = st["heightFt"]
    thr_ap = st["controls"]["throttle"]
    p.g.ilim = 0.85
    p.reset()
    p.seed_elevator(st["controls"]["elevator"])   # take over on the stick the autopilot was holding
    t0 = time.time()                      # settle 4 s so the phase opens at a steady 1 g
    while time.time() - t0 < 4:
        # hold what the autopilot was holding: ~41 % power keeps 55 KIAS level, and the g command stays near 1
        # (at 1.5 g the stall speed is already 63 KIAS, which would drag the airplane straight back up to it)
        u = dict(elevator=p.hold_g(st, clamp(p.level(st, alt, 1/60, vs_lim=120), 0.92, 1.06), 1/60),
                 aileron=p.hold_bank(st, 0, 1/60), rudder=p.coordinate(st, 1/60), throttle=thr_ap)
        st = w.pipe({"cmd": "controls", **u}, {"cmd": "state"})[1]
    print(f"slow: {st['kias']:.1f} KIAS, {alt:.0f} ft, alpha {st['alpha']:.1f}")

    m = vg.Marks(w)
    m.start()
    m.mark("accel")

    # ---- 1. level acceleration at 1 g, just above the stall to 120 KIAS -------------------------
    if not QUICK:
      print("accel ...")
      f.fly(75, lambda st, t, dt: dict(
        elevator=p.hold_g(st, p.level(st, alt, dt, vs_lim=200), dt), aileron=p.hold_bank(st, 0, dt),
        rudder=p.coordinate(st, dt), throttle=1.0), "accel", stop=lambda s: s["kias"] >= 120.0)

    # ---- 2/3. steep turns, 45 deg, 180 deg each way ---------------------------------------------
    st = f.state()
    for sign, name in (() if QUICK else ((-1, "turn-left"), (+1, "turn-right"))):
        print(f"{name} ...")
        m.mark(name)
        p.reset()
        hold, turned, prev, rolled = st["heightFt"], [0.0], [st["heading"]], [False]

        def turn(st, t, dt, sign=sign, hold=hold, turned=turned, prev=prev, rolled=rolled):
            turned[0] += unwrap(prev[0], st["heading"]) * sign
            prev[0] = st["heading"]
            if turned[0] < 173 and not rolled[0]:
                bank = 45.0 * sign
            else:
                rolled[0] = True
                bank = 0.0
            return dict(elevator=p.hold_g(st, p.level(st, hold, dt, vs_lim=300), dt),
                        aileron=p.hold_bank(st, bank, dt), rudder=p.coordinate(st, dt),
                        throttle=p.hold_speed(st, 110, dt, base=0.75))
        f.fly(90, turn, name, stop=lambda s: rolled[0] and abs(s["roll"]) < 3.0)
        st = f.state()
        print(f"  turned {turned[0]:.0f} deg, {st['kias']:.1f} KIAS, {st['heightFt']:.0f} ft")

    # ---- 4. settled level cruise -----------------------------------------------------------------
    print("cruise ...")
    m.mark("cruise")
    _cruise_s = 4 if QUICK else 10
    p.reset()
    hold = st["heightFt"]
    f.fly(_cruise_s, lambda st, t, dt: dict(
        elevator=p.hold_g(st, p.level(st, hold, dt, vs_lim=200), dt), aileron=p.hold_bank(st, 0, dt),
        rudder=p.coordinate(st, dt), throttle=p.hold_speed(st, 110, dt, base=0.75)), "cruise")

    # ---- 5. power-off stall, break and recovery back to cruise ----------------------------------
    print("stall ...")
    m.mark("stall")
    p.reset()
    st = f.state()
    hold = st["heightFt"]
    broke = [False]
    low = [st["heightFt"]]

    held = [None]          # when the wing first went to the stall and stayed there

    def stall(st, t, dt, hold=hold):
        low[0] = min(low[0], st["heightFt"])
        stalled = (st["leftStalled"] or st["rightStalled"]) and st["alpha"] > 13.5
        if not broke[0]:
            # Power at idle, wings level, altitude held with back pressure. The airplane decays to its 1-g stall
            # speed (~52 KIAS) and mushes there at alpha ~15.5 - this model is docile and does not drop a wing,
            # so the stall is recognised by the wing being STALLED and staying stalled, not by a nose drop.
            # (Forcing a break with an elevator ramp instead zooms it to +50 deg pitch and departs - 90 deg of
            # bank and a 3.5 g recovery, which is not a power-off stall in a trainer.)
            if stalled:
                if held[0] is None:
                    held[0] = t
                elif t - held[0] > 3.0:
                    broke[0] = True
            else:
                held[0] = None
            if st["heightFt"] < 1200:                      # floor: never fly it into the ground
                broke[0] = True
            vs = -st["sinkFpm"]
            g_cmd = clamp(1.0 + 0.0010 * (0.0 - vs), 0.7, 1.5)
            if st["pitch"] > 13.0:
                g_cmd = min(g_cmd, 1.0)
            return dict(elevator=p.hold_g(st, g_cmd, dt), aileron=p.hold_bank(st, 0, dt),
                        rudder=p.coordinate(st, dt), throttle=0.0)
        return None     # the break ends this phase; the recovery is flown separately below
    p.g.ilim = 0.85
    f.fly(150, stall, "stall", stop=lambda s: broke[0])
    st = f.state()
    print(f"  broke at {st['kias']:.1f} KIAS, alpha {st['alpha']:.1f}, {st['heightFt']:.0f} ft")

    # ---- 6. recovery, flown on ANGLE OF ATTACK (Ty's spec) --------------------------------------
    # "the 172 should only pitch down to reduce aoa from stall (roughly 15-20 degrees aoa) to around 10 degrees
    # aoa then hold that aoa until level flight is regained and then reduce aoa to maintain level flight as the
    # airplane accelerates". No big pitch-down, no g excursion, no secondary stall.
    print("recovery ...")
    m.mark("recovery")
    p.reset()
    p.seed_elevator(st["controls"]["elevator"])
    level = [False]
    a_cmd = [10.0]

    def recovery(st, t, dt):
        vs = -st["sinkFpm"]
        if not level[0]:
            if vs > -120.0:                       # the descent has stopped: level flight regained
                level[0] = True
            a = 10.0                              # unload from the stall to 10 deg and HOLD it
        else:
            # ease the angle of attack off to hold level flight; it falls on its own as the speed builds
            a_cmd[0] = clamp(a_cmd[0] + 0.0012 * (0.0 - vs) * (dt * 60.0), 0.5, 10.0)
            a = a_cmd[0]
        thr = 1.0 if st["kias"] < 105 else p.hold_speed(st, 110, dt, base=0.75)
        return dict(elevator=p.hold_alpha(st, a, dt), aileron=p.hold_bank(st, 0, dt),
                    rudder=p.coordinate(st, dt), throttle=thr)
    f.fly(90, recovery, "recovery",
          stop=lambda s: s["kias"] >= 109 and abs(s["sinkFpm"]) < 150 and abs(s["roll"]) < 5)
    p.g.ilim = 0.35
    st = f.state()
    print(f"  recovered {st['kias']:.1f} KIAS, {st['heightFt']:.0f} ft, alpha {st['alpha']:.1f}")

    n = m.frames()
    if QUICK:
        print(f"QUICK: {n} frames = {n/30:.1f} s; no render")
        return
    print(f"\nflown: {n} history frames = {n/30:.1f} s; rendering ...")
    mov, csv, rows = vg.render(w, m, "c172", px=int(os.environ.get("PX", "512")))
    print(f"WROTE {mov}\nWROTE {csv}")
    gs = [r[1]["loadFactor"] for r in rows]; ks = [r[1]["kias"] for r in rows]
    print(f"  {len(rows)} rows/frames, {len(rows)/30:.1f} s, g {min(gs):.2f}..{max(gs):.2f}, "
          f"KIAS {min(ks):.1f}..{max(ks):.1f}")
    for _, ph in m.marks:
        rs = [r for r in rows if r[2] == ph]
        if rs:
            print(f"  {ph:<11} {len(rs):5d} fr {rs[0][0]:7.2f}-{rs[-1][0]:7.2f} s  "
                  f"g {min(r[1]['loadFactor'] for r in rs):5.2f}..{max(r[1]['loadFactor'] for r in rs):4.2f}  "
                  f"KIAS {min(r[1]['kias'] for r in rs):5.1f}..{max(r[1]['kias'] for r in rs):5.1f}")


if __name__ == "__main__":
    main()
