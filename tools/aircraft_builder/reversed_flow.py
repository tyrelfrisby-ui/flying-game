"""REVERSED FLOW (|alpha| > 90 deg) for the generated airfoil tables (2026-10-12, the spin research + Ty's tailslides).

The generators kept the broadside drag (CD90) all the way to 180 deg and let CL drift to -/+0.5 there: a wing sliding
backwards (a tailslide, alpha ~180) had the drag of a barn door. A section in reversed flow is a thin plate with its sharp
trailing edge leading: at 180 deg it is streamlined again (CD ~0.025, CL 0), it lifts weakly near 180 and stalls early
(sharp leading edge), and between 90 and 180 deg the normal force falls back off as sin^2. Theta = 180 - |alpha|:
  CL = -sign(alpha) * 0.65 * sin(2 theta)        (thin plate; +-0.4 at 160, +-0.64 at 135 — as naca0012-like, hand data)
  CD = 0.025 + CD90 * sin^2(alpha)               (the table's own value at 90 deg, so it joins up)
  Cm = about the quarter chord from the normal force acting at x_cp, which moves from 0.5 c (90 deg) to 0.75 c (180),
       blended over the first 30 deg from the table's own Cm(90) so it joins up.
Run on configs: python3 reversed_flow.py configs/aircraft/*.json  (only tables whose |180| row has CD > 0.5 change).
"""
import json, math, sys


def fix_table(tb):
    deg = [math.degrees(a) for a in tb['alphaRad']]
    def at(target):
        i = min(range(len(deg)), key=lambda k: abs(deg[k] - target))
        return i
    i90p, i90n = at(90), at(-90)
    cd90 = max(tb['cd'][i90p], tb['cd'][i90n])
    for k, d in enumerate(deg):
        a = abs(d)
        if a <= 90.5: continue
        s = 1 if d > 0 else -1
        th = math.radians(180 - a)
        cl = -s * 0.65 * math.sin(2 * th)
        cd = 0.025 + cd90 * math.sin(math.radians(a)) ** 2
        ar = math.radians(d)
        cn = cl * math.cos(ar) + cd * math.sin(ar)          # normal force coefficient (signed)
        xcp = 0.5 + 0.25 * (a - 90) / 90
        cm_plate = -cn * (xcp - 0.25)
        cm90 = tb['cm'][i90p if s > 0 else i90n]
        w = min(1.0, (a - 90) / 30)
        tb['cl'][k] = round(cl, 4); tb['cd'][k] = round(cd, 4); tb['cm'][k] = round((1 - w) * cm90 + w * cm_plate, 4)
    return tb


def needs_fix(tb):
    deg = [math.degrees(a) for a in tb['alphaRad']]
    if max(deg) < 179: return False
    i = deg.index(max(deg))
    return tb['cd'][i] > 0.5


if __name__ == '__main__':
    for f in sys.argv[1:]:
        c = json.load(open(f))
        changed = [n for n, tb in c.get('airfoilTables', {}).items() if needs_fix(tb)]
        if not changed: continue
        for n in changed: fix_table(c['airfoilTables'][n])
        json.dump(c, open(f, 'w'), indent=2)
        print(f, changed)
