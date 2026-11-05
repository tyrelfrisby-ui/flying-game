"""Measure the Grob G 103 Twin II three-view in its own flight manual (2026-11-01, lane/claude-grob103).

    python3 tools/aircraft_builder/grob103_threeview.py      (needs Pillow and poppler's pdftoppm)

Writes tools/aircraft_builder/data/grob103_threeview.json, which build_grob103.py reads.

SOURCE. "Flight Manual GROB G 103 TWIN II", FAA-English edition, September 1981 (LBA revisions to March 1982),
page 5 (PDF page 7): https://esgc.co.uk/wp-content/uploads/2014/01/Grob103_Flight_Manual.pdf
It is the only three-view of the type found free. It carries no dimension lines, so the published figures scale it:
  - the PLAN and FRONT views by the 17.5 m span (both measure 2523 px tip to tip at 400 dpi: one scale, 6.936 mm/px);
  - the SIDE view by the 8.18 m length, nose to rudder trailing edge (1172 px: 6.980 mm/px).
The plan view's fuselage measures 7.96 m at the span scale, 2.7 % short of 8.18, so stations ALONG the fuselage
(datum, tail, wheels) come from the side view and only the planform shapes from the plan view. The page is rotated:
the nose points right in the plan view and down in the side view; "up" is to the left in the side and front views.

Every edge is taken at the CENTRE of its drawn line (lines are 4-8 px thick, 30-55 mm at scale), so a chord is
centre-to-centre, not ink-to-ink.
"""
import json, os, subprocess, tempfile, urllib.request

from PIL import Image

HERE = os.path.dirname(os.path.realpath(__file__))
OUT = os.path.join(HERE, 'data', 'grob103_threeview.json')
URL = 'https://esgc.co.uk/wp-content/uploads/2014/01/Grob103_Flight_Manual.pdf'
SPAN, LENGTH = 17.5, 8.18
INK = 140


def page():
    work = tempfile.mkdtemp(prefix='g103tv', dir='/tmp')
    pdf = os.path.join(work, 'fm.pdf')
    with open(pdf, 'wb') as f: f.write(urllib.request.urlopen(URL, timeout=120).read())
    subprocess.run(['pdftoppm', '-f', '7', '-l', '7', '-r', '400', '-png', pdf, os.path.join(work, 'p')], check=True)
    return Image.open(os.path.join(work, 'p-07.png')).convert('L')


def runs_row(px, y, x0, x1):
    r, start = [], None
    for x in range(x0, x1):
        d = px[x, y] < INK
        if d and start is None: start = x
        if not d and start is not None: r.append((start, x - 1)); start = None
    return r


def centre(run): return 0.5 * (run[0] + run[1])


def main():
    im = page(); px = im.load()
    out = {'source': URL, 'page': 'flight manual p.5 (PDF page 7), 400 dpi'}

    # ---- PLAN VIEW, wing: x 1650-1900 px, tips at rows 404 and 2927 ------------------------------------------
    tip_a, tip_b = 404, 2927
    k = SPAN / (tip_b - tip_a); yc = 0.5 * (tip_a + tip_b)
    wing = []
    for y in range(tip_a + 6, tip_b - 6, 12):
        r = runs_row(px, y, 1650, 1900)
        ym = (yc - y) * k
        if abs(ym) < 0.45 or len(r) < 2: continue         # the fuselage crosses the wing root
        le, te = centre(r[-1]), centre(r[0])               # nose is +x (right): LE is the right-hand line
        inner = [centre(q) for q in r[1:-1]]
        wing.append({'y': round(ym, 3), 'leX': round((le - 1872) * k, 4), 'chord': round((le - te) * k, 4),
                     'inner': [round((le - c) * k, 4) for c in inner]})   # interior lines, m aft of the LE
    out['wing'] = {'scale_m_per_px': k, 'stations': wing}

    # ---- FRONT VIEW: the wing's upper-surface line, rows across the span ----------------------------------------
    front = []
    for y in range(391, 2913, 30):
        r = runs_row(px, y, 260, 480)
        ym = (0.5 * (391 + 2913) - y) * k
        if abs(ym) < 1.6 or not r: continue
        front.append((round(ym, 3), round(-centre(r[0]) * k, 4)))   # up = left in the drawing, so z_up = -x
    def fit(pts):
        n = len(pts); sx = sum(p[0] for p in pts); sy = sum(p[1] for p in pts)
        sxx = sum(p[0] ** 2 for p in pts); sxy = sum(p[0] * p[1] for p in pts)
        return (n * sxy - sx * sy) / (n * sxx - sx * sx)
    right = [(abs(a), b) for a, b in front if a > 0]; left = [(abs(a), b) for a, b in front if a < 0]
    sr, sl = fit(right), fit(left)
    import math
    out['front'] = {'slopeRight': sr, 'slopeLeft': sl,
                    'dihedralDeg': math.degrees(math.atan(0.5 * (sr + sl))),
                    'drawingTiltDeg': math.degrees(math.atan(0.5 * (sl - sr)))}

    # ---- PLAN VIEW, tailplane: x 1055-1200, rows 1416-1899 -----------------------------------------------------
    t0, t1 = 1416, 1899; tc = 0.5 * (t0 + t1)
    tail = []
    for y in range(t0 + 8, t1 - 6, 10):
        r = runs_row(px, y, 1055, 1200)
        if len(r) != 3: continue                            # TE, elevator hinge, LE
        te, hinge, le = (centre(q) for q in r)
        tail.append({'y': round((tc - y) * k, 3), 'leAftOfTipLE': None, 'leX': round(le * k, 4),
                     'chord': round((le - te) * k, 4), 'elevChord': round((hinge - te) * k, 4)})
    out['tailplane'] = {'spanM': round((t1 - t0) * k, 4), 'stations': tail}

    # ---- SIDE VIEW: rows 413 (rudder TE) to 1585 (nose) --------------------------------------------------------
    ytail, ynose = 413, 1585; m = LENGTH / (ynose - ytail)
    def lowest(y, x0, x1):
        xs = [x for x in range(x0, x1) if px[x, y] < INK]; return max(xs) if xs else None
    def bump(lo, hi, x0, x1):
        rows = [(y, lowest(y, x0, x1)) for y in range(int(ynose - hi / m), int(ynose - lo / m))]
        rows = [r for r in rows if r[1] is not None]
        deep = max(r[1] for r in rows)
        inside = [r[0] for r in rows if r[1] >= deep - 2]
        return round((ynose - 0.5 * (min(inside) + max(inside))) * m, 3), deep
    main_x, main_low = bump(2.6, 3.6, 860, 940)
    nose_x, nose_low = bump(0.6, 1.2, 860, 940)
    tailw_x, tailw_low = bump(7.1, 7.6, 840, 900)
    out['side'] = {'scale_m_per_px': m,
                   'mainWheelFromNose': main_x, 'noseWheelFromNose': nose_x, 'tailWheelFromNose': tailw_x,
                   'mainWheelBottomPx': main_low, 'noseWheelBottomPx': nose_low, 'tailWheelBottomPx': tailw_low}
    json.dump(out, open(OUT, 'w'), indent=1)
    print(json.dumps({k2: v for k2, v in out.items() if k2 not in ('wing', 'tailplane')}, indent=1))
    print('wing stations', len(wing), 'tail stations', len(tail))


if __name__ == '__main__':
    main()
