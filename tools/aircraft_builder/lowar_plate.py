"""LOW-ASPECT-RATIO TAIL PLATES (2026-10-12, the spin autorotation research).

The tail tables kept near-two-dimensional broadside drag: `fin-lowAR` (rudders, elevators, some fins) CD90 = 1.658 and
`naca0012-like` (used ONLY on tail surfaces: stabs, fins, a few elevators) CD90 = 1.35. A flat plate broadside to the flow
has CD ~1.98 only when infinitely long; at the aspect ratios of tail surfaces (fins ~1-2, stabs ~3-4) it is 1.17-1.20
(Hoerner, Fluid-Dynamic Drag, 1965, ch. 3, "plates normal to the flow": AR 1: 1.18, AR 5: 1.20, AR 10: 1.23, AR 20: 1.48,
infinite: 1.98). The too-high value over-damps the spin's yaw at the fin. The post-stall drag follows CD90 sin^2(alpha), so
it is rescaled in proportion: CD = CD0 + (CD_old - CD0) * CD90_new / CD90_old for |alpha| >= 12 deg, CD0 being the table's
own base drag on that side (forward flow near 0 deg, reversed flow at +-180 deg): the attached range and both ends are kept.
Run on configs: python3 lowar_plate.py configs/aircraft/*.json
"""
import json, math, sys

TARGETS = {'fin-lowAR': 1.18, 'naca0012-like': 1.19}   # (fins AR ~1-2: 1.18; stabs AR ~3-4: 1.19)


def _i(deg, target): return min(range(len(deg)), key=lambda j: abs(deg[j] - target))


def fix_table(tb, cd90):
    deg = [math.degrees(a) for a in tb['alphaRad']]
    k = cd90 / tb['cd'][_i(deg, 90)]
    cd0f = tb['cd'][_i(deg, 0)]
    cd0r = tb['cd'][min(range(len(deg)), key=lambda j: abs(abs(deg[j]) - 180))]
    for j, d in enumerate(deg):
        if abs(d) < 12: continue
        base = cd0f if abs(d) <= 90 else cd0r
        tb['cd'][j] = round(base + max(0.0, tb['cd'][j] - base) * k, 4)
    return tb


def needs_fix(tb, cd90):
    deg = [math.degrees(a) for a in tb['alphaRad']]
    return tb['cd'][_i(deg, 90)] > cd90 + 0.05


def fix_tables(tabs):
    for name, cd90 in TARGETS.items():
        if name in tabs and needs_fix(tabs[name], cd90): fix_table(tabs[name], cd90)


if __name__ == '__main__':
    for f in sys.argv[1:]:
        c = json.load(open(f))
        tabs = c.get('airfoilTables', {})
        if not any(n in tabs and needs_fix(tabs[n], v) for n, v in TARGETS.items()): continue
        fix_tables(tabs)
        json.dump(c, open(f, 'w'), indent=2)
        print(f)
