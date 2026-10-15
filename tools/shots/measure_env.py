"""Measure each airplane's stall speeds IN THIS SIM and write the envelope JSON the V-g diagram is drawn from.

vs and vsi are flown here (a 1-g upright stall and a -1-g inverted stall, both at idle power); vne and the limit
loads come from the airplane's own config, so the drawn envelope is the one the sim actually enforces.
"""
import json, math, os, sys, time
import aerofly, vg
from aerofly import clamp
from pilot import Pilot

REPO = "/Users/tyfrisby/Documents/flying-game"
AIRCRAFT = {"c172": "c172-like", "extra300": "extra-300-like"}
CRUISE = {"c172": 110, "extra300": 150}


CRIT_DEG = 15.0        # AeroHost stallDeg


def wing_stalled(st):
    """True when either wing is past the critical angle - EITHER SIGN. `leftStalled`/`rightStalled` in `state`
    were one-sided (alpha > crit) and never fired inverted, so the wing angles are tested directly."""
    return (abs(st.get("leftWingAlpha", 0.0)) > CRIT_DEG or abs(st.get("rightWingAlpha", 0.0)) > CRIT_DEG)


def vs_from(st):
    """The 1-g stall speed implied by this sample: V / sqrt(|n|). Taken AT the break it needs no g band."""
    n = max(0.25, abs(st["loadFactor"]))
    return st["kias"] / (n ** 0.5)


def stall_speed(w, p, inverted, limit=200):
    """The standard stall-speed measurement, flown the way it is taught (Ty, 2026-10-15).

    Wings level, idle-ish, clean, holding ALTITUDE so the wing is carrying ~1 g (-1 g inverted) rather than
    being unloaded; decelerating about 1 kt/s; the speed taken at THE BREAK - the first moment a wing's |alpha|
    passes the stall angle - and corrected to 1 g as V/sqrt(|n|). Then the back pressure (or push) is released
    at once.

    Ty: "if the airplane is pitched down the stall speed will be low." A nose-down sinking break unloads the
    wing, and Vs scales with sqrt(n), so a 0.7 g break reads ~16 % low. Taking the lowest speed anywhere inside
    a loose g band has the same fault, which is what read 51.8 upright and 48.0 inverted.
    """
    sgn = -1.0 if inverted else 1.0
    st = w.cmd(cmd="state")
    p.reset()
    p.g.ilim = 0.9
    p.seed_elevator(st["controls"]["elevator"])
    v = st["kias"]
    t0 = time.time()
    last = t0
    bank = 0.0 if not inverted else (180.0 if st["roll"] >= 0 else -180.0)
    while time.time() - t0 < limit:
        now = time.time()
        v -= 1.0 * (now - last)                      # the speed target walks down at about 1 kt/s
        last = now
        vs_fpm = -st["sinkFpm"]
        # hold the PATH level: the wing then carries ~ +-1 g right up to the break
        g_cmd = clamp(sgn * 1.0 + sgn * 0.0010 * (0.0 - vs_fpm), *((0.6, 1.6) if not inverted else (-1.6, -0.6)))
        u = dict(elevator=p.hold_g(st, g_cmd, 1 / 60.0), aileron=p.hold_bank(st, bank, 1 / 60.0),
                 rudder=p.coordinate(st, 1 / 60.0),
                 throttle=p.hold_speed(st, v, 1 / 60.0, base=0.25))
        st = w.pipe({"cmd": "controls", **u}, {"cmd": "state"})[1]
        if wing_stalled(st):
            n = abs(st["loadFactor"])
            vs_1g = st["kias"] / max(0.3, n) ** 0.5
            print(f"    [break: {st['kias']:.1f} KIAS at n={st['loadFactor']:+.2f}, alpha {st['alpha']:+.1f}, "
                  f"vs {vs_fpm:+.0f} fpm -> Vs {vs_1g:.1f} KIAS at 1 g]")
            # release at once - holding it drives the wing far past the break and mushes it down
            for _ in range(60):
                rel = dict(elevator=p.hold_alpha(st, sgn * 4.0, 1 / 60.0),
                           aileron=p.hold_bank(st, bank, 1 / 60.0), rudder=0.0, throttle=0.6)
                st = w.pipe({"cmd": "controls", **rel}, {"cmd": "state"})[1]
            return vs_1g, st
        if st["heightFt"] < 1200:
            break
    return None, st


def roll_inverted(w, p, limit=35):
    st = w.cmd(cmd="state")
    t0 = time.time()
    while time.time() - t0 < limit:
        g_cmd = clamp(math.cos(math.radians(st["roll"])), -1.0, 1.0)
        u = dict(elevator=p.hold_g(st, g_cmd, 1 / 60.0), aileron=p.hold_bank(st, 180.0, 1 / 60.0),
                 rudder=0.0, throttle=0.8)
        st = w.pipe({"cmd": "controls", **u}, {"cmd": "state"})[1]
        if abs(((180.0 - st["roll"] + 180.0) % 360.0) - 180.0) < 12.0:
            break
    p.reset()
    p.seed_elevator(st["controls"]["elevator"])
    return st


def main():
    which = sys.argv[1]
    ac = AIRCRAFT[which]
    cfg = json.load(open(f"{REPO}/configs/aircraft/{ac}.json"))
    lim = cfg["limits"]
    w = aerofly.W()
    f = aerofly.Flight(w, ac, "cruise", hz=60)
    p = Pilot()
    vg.setup_look(w)
    WORK = 7000.0                      # two stalls at idle need real height under them
    vg.climb_to(f, p, WORK, CRUISE[which], limit=260)      # full power: much faster than the AP's climb
    st = vg.ap_goto(w, WORK, CRUISE[which], limit=90)
    base = st["heightFt"]
    print(f"at work altitude: {st['kias']:.0f} KIAS {base:.0f} ft")
    p.reset(); p.seed_elevator(st["controls"]["elevator"])

    vs, st = stall_speed(w, p, inverted=False)
    print(f"  vs  = {vs if vs is None else round(vs,1)} KIAS")

    st = vg.recover_from_stall(w, p, kias_target=min(130.0, CRUISE[which]))   # fly it out of the stall FIRST
    print(f"    [recovered {st['kias']:.0f} KIAS at {st['heightFt']:.0f} ft]")
    vg.climb_to(f, p, WORK, CRUISE[which], limit=260)
    st = vg.ap_goto(w, WORK, CRUISE[which], limit=90)
    print(f"    [back at {st['heightFt']:.0f} ft, {st['kias']:.0f} KIAS]")
    p.reset(); p.seed_elevator(st["controls"]["elevator"])

    roll_inverted(w, p)
    vsi, st = stall_speed(w, p, inverted=True)
    print(f"  vsi = {vsi if vsi is None else round(vsi,1)} KIAS")

    vne = lim["vneMs"] * 1.943844
    env = {
        "aircraft": ac,
        "displayName": cfg.get("displayName"),
        "vs": None if vs is None else round(vs, 1),
        "vsi": None if vsi is None else round(vsi, 1),
        "vne": round(vne, 1),
        "va": None if vs is None else round(vs * math.sqrt(lim["gMax"]), 1),
        "pos": lim["gMax"],
        "neg": lim["gMin"],
        "units": {"speeds": "KIAS", "loads": "g"},
        "notes": ("vs and vsi are MEASURED in this sim (idle power, 1 g upright and -1 g inverted, clean) so the "
                  "envelope matches the flight data; vne and the limit loads are the airplane's own config "
                  "values. va = vs * sqrt(pos)."),
    }
    path = os.path.join(vg.OUT, f"{which}.json")
    json.dump(env, open(path, "w"), indent=2)
    print("WROTE", path)
    print(json.dumps(env))


if __name__ == "__main__":
    main()
