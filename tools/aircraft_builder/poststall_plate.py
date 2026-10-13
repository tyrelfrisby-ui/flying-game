"""POST-STALL WING SHAPE (2026-10-12, owner/CFI on the extra-sym chart: "the lift has a secondary peak at around 45 degrees
that is lower than the primary peak around 15-20 degrees but it definitely does not go up from there to 90 degrees aoa. at 90
degrees aoa it should essentially be zero lift").

The generated aerobatic wing tables went flat after the stall drop and then fell LINEARLY to zero at 90 deg — no secondary peak,
and no falling stretch for the normal force between the stall and ~25 deg, which is what drives a wing's autorotation.
Past the stall a wing behaves as a flat plate: the force is normal to the chord, CN = CD90 sin(a), so
  CL = CN cos(a) = CD90 sin(a) cos(a)   (a secondary maximum CD90/2 at 45 deg, below the primary; zero at 90 deg)
  CD = CD0 + CN sin(a) = CD0 + CD90 sin^2(a)
(Hoerner, Fluid-Dynamic Lift 1985 ch. 4 and Fluid-Dynamic Drag ch. 3; finite-AR post-stall data: Ostowari & Naik,
SERI/STR-217-2559, 1985 — AR 6 NACA 44xx levels off near 45 deg and falls to ~0 at 90 deg; NACA TN 3361 Critzos & Heyson for
the 0012's second maximum near 45 deg). CD90 is each table's own value at 90 deg (it already reflects the wing's AR).
Between the end of the stall drop (the first point past the primary peak, a_s) and a_s + 10 deg the table blends linearly
into the plate curve; the attached range, the primary peak and the stall drop itself are kept. Both signs (symmetric).
Run on configs: python3 poststall_plate.py configs/aircraft/*.json
"""
import json, math, sys

WINGS = ('extra-sym', 'swift-sym', 'pitts-wing', 'pitts-wing-upper', 'decathlon-wing')
BLEND_DEG = 10.0
STEP = 5.0   # plate points added every 5 deg so the secondary peak is resolved


def _plate(a, cd0, cd90):
    s, c = math.sin(a), math.cos(a)
    return cd90 * s * c, cd0 + cd90 * s * s


def fix_table(tb):
    deg = [math.degrees(a) for a in tb['alphaRad']]
    rows = {round(d, 3): (cl, cd, cm) for d, cl, cd, cm in zip(deg, tb['cl'], tb['cd'], tb['cm'])}
    def at(d): return min(rows, key=lambda k: abs(k - d))
    out = dict(rows)
    for sgn in (1, -1):
        side = sorted((d for d in rows if 0 < sgn * d <= 90.5), key=lambda d: sgn * d)
        # primary peak = the largest |CL| below 25 deg; a_s = the next point (the end of the stall drop)
        low = [d for d in side if sgn * d <= 25]
        if not low: continue
        pk = max(low, key=lambda d: abs(rows[d][0]))
        nxt = [d for d in side if sgn * d > sgn * pk]
        if not nxt: continue
        a_s = nxt[0]
        cd0 = rows[at(0)][1]
        cd90 = rows[at(sgn * 90)][1] - cd0   # (so CD at 90 deg stays the table's own)
        cl_s, cd_s, _ = rows[a_s]
        # drop the old points beyond a_s, put plate points every STEP deg (and the blend end)
        for d in [d for d in side if sgn * d > sgn * a_s]:
            out.pop(d, None)
        grid = sorted(set([sgn * x for x in [abs(a_s) + BLEND_DEG] + [STEP * k for k in range(1, 19) if STEP * k > abs(a_s)]] + [sgn * 90.0]))
        for d in grid:
            a = math.radians(d)
            cl_p, cd_p = _plate(a, cd0, cd90)
            t = min(1.0, (abs(d) - abs(a_s)) / BLEND_DEG)
            cl = (1 - t) * cl_s + t * cl_p
            cd = (1 - t) * cd_s + t * cd_p
            # Cm about the quarter chord from the normal force at the plate's centre of pressure (0.25 c → 0.5 c by 90 deg)
            cn = cl * math.cos(a) + cd * math.sin(a)
            xcp = 0.25 + 0.25 * min(1.0, (abs(d) - abs(a_s)) / (90 - abs(a_s)))
            out[round(d, 3)] = (round(cl, 4), round(cd, 4), round(-cn * (xcp - 0.25), 4))
    keys = sorted(out)
    tb['alphaRad'] = [round(math.radians(d), 5) for d in keys]
    tb['cl'] = [out[d][0] for d in keys]; tb['cd'] = [out[d][1] for d in keys]; tb['cm'] = [out[d][2] for d in keys]
    return tb


def needs_fix(tb):
    deg = [math.degrees(a) for a in tb['alphaRad']]
    i45 = min(range(len(deg)), key=lambda j: abs(deg[j] - 45)); i90 = min(range(len(deg)), key=lambda j: abs(deg[j] - 90))
    return abs(deg[i45] - 45) > 1 or abs(tb['cl'][i45] - 0.5 * tb['cd'][i90]) > 0.03


def fix_tables(tabs):
    for n in WINGS:
        if n in tabs and needs_fix(tabs[n]): fix_table(tabs[n])


if __name__ == '__main__':
    for f in sys.argv[1:]:
        c = json.load(open(f))
        tabs = c.get('airfoilTables', {})
        if not any(n in tabs and needs_fix(tabs[n]) for n in WINGS): continue
        fix_tables(tabs)
        json.dump(c, open(f, 'w'), indent=2)
        print(f)
