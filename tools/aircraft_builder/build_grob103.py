"""Grob G 103 C Twin III Acro -> configs/aircraft/glider-grob103-acro-like.json (2026-11-01, lane/claude-grob103;
re-based from the Twin II Acro to the Twin III Acro 2026-11-04 on Ty's call).

    python3 tools/aircraft_builder/build_grob103.py            (writes the config and prints every derivation)
    GROB_OUT=/tmp/x.json python3 tools/aircraft_builder/build_grob103.py   (writes somewhere else)

Ty's brief: physics from published geometry and standard aerodynamics, NO fudge factors; the published performance
is for validation only and nothing here is tuned toward it. docs/aircraft/grob103-acro-DERIVATION.md walks through
every number this prints. Inputs:

  data/grob103_threeview.json  the TWIN II flight manual's three-view, measured (grob103_threeview.py) -- the Twin III's
                               own three-view ([POH] 1.6) is too small to measure, so the Twin II's SHAPES are kept and
                               stretched to the Twin III's published span and area (ASSUMED, see WING PLANFORM)
  data/grob103_xfoil.json      XFOIL 6.99 polars for the E583 wing and the FX 71-L-150/30 tail (grob103_xfoil.py)

and these published figures, quoted where they are used:

  [POH]       Pilot's Operating Handbook G 103 C TWIN III ACRO, issue January 1989, revision 6 / 16.10.2003 (the copy of
              s/n 34197, G-CLXH): https://pilots.scottishglidingcentre.co.uk/_media/aircraft/twinastir/grob103c_poh.pdf
              Limits are the ones it gives for OSB 315-66 INSTALLED (the airframe aerobatics are approved for).
  [TCDS]      EASA TCDS A.250 Issue 04 (21 Dec 2011), Section N "G 103 C TWIN III ACRO" (Ty's note said A.024; the
              Twin III Acro is Section N of A.250): https://www.easa.europa.eu/en/downloads/7216/en
  [UIUC]      UIUC "Incomplete Guide to Airfoil Usage": "Grob 103C Twin III Acro -- Eppler 583 / Eppler 583".
  [N41KP]     Statement of weight and balance, Grob 103C Twin III N41KP, 29 Sep 2010 (Norcal Soaring):
              https://www.norcalsoaring.org/uploads/1/0/0/6/100675970/kp_weight_and_balance.pdf -- ONE weighed airframe:
              empty CG, seat and baggage arms, main and tail wheel stations.
  [FM-II]     Flight Manual GROB G 103 TWIN II, Sept 1981 -- only where the Twin III has no figure of its own (flagged).
  [TCDS-FAA]  FAA TCDS G39EU (G 103 / G 103A, i.e. TWIN II) -- control travel and brake height, flagged likewise.

Frame: +x FORWARD, +y right, +z DOWN, origin at the [POH]/[TCDS] DATUM -- the wing leading edge at the root rib -- on
the fuselage reference line at the wing root (the wing's own chord line: the Twin III is a mid-wing, [POH] 1.4). A station s mm
aft of the datum is body x = -s/1000.
"""
import json, math, os, sys

HERE = os.path.dirname(os.path.realpath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
OUT = os.environ.get('GROB_OUT', os.path.join(REPO, 'configs', 'aircraft', 'glider-grob103-acro-like.json'))
TV = json.load(open(os.path.join(HERE, 'data', 'grob103_threeview.json')))
XF = json.load(open(os.path.join(HERE, 'data', 'grob103_xfoil.json')))
sys.path.insert(0, HERE)
from reversed_flow import fix_table as reversed_fix   # noqa: E402  (|alpha| > 90: a wing sliding backwards)

D2R = math.pi / 180.0
NU = 1.46e-5          # kinematic viscosity, ISA sea level (m^2/s)
RHO = 1.225
G = 9.80665

def say(*a): print(*a)

# =====================================================================================================================
# 1. PUBLISHED GEOMETRY
# =====================================================================================================================
SPAN = 18.0             # [POH] 1.4 "Wing span 18.0 m"; [TCDS] N.III.4 "Span 18,00 m"
AREA = 17.5             # [POH] 1.4 "Wing area 17.5 m2" (b2/S 18.51; [POH] quotes "aspect ratio 18.5")
LENGTH = 8.18           # [POH] 1.4 "Length 8.18 m" (the same as the Twin II's)
HALF = SPAN / 2
# The Twin II three-view this planform is measured from spans 17.5 m ([FM-II] I.5). The Twin III's wing is a new
# "triple tapered" wing ([POH] 1.4) whose stations are not published and whose three-view ([POH] 1.6) is too small to
# measure: every SPANWISE station read off the Twin II drawing (chords, aileron, airbrake box, strip edges) is stretched
# by 18.0/17.5 and the chords then scaled to the published 17.5 m2. ASSUMED -- the Twin III's own taper breaks are lost.
SPAN_II = 17.5
STRETCH = SPAN / SPAN_II

# Stations along the fuselage, from the side view (scaled by the 8.18 m length) and the plan view (span scale,
# stretched x 8.18/7.956 because the plan view's fuselage draws 2.7 % short of the published length).
PLAN_X = LENGTH / 7.956
NOSE_TO_DATUM = 2.254 * PLAN_X              # plan view: nose to wing-root LE 325 px = 2.254 m -> 2.317 m
def body_x_from_nose(s): return NOSE_TO_DATUM - s

# =====================================================================================================================
# 2. WING PLANFORM (three-view, scaled to the published area)
# =====================================================================================================================
def wing_chord_table():
    HALF = SPAN_II / 2           # the Twin II drawing's own half span: the table is built in ITS coordinates
    bins = {}
    for s in TV['wing']['stations']:
        b = round(abs(s['y']) * 4) / 4
        bins.setdefault(b, []).append(s['chord'])
    ys = sorted(bins)
    ch = [sum(bins[y]) / len(bins[y]) for y in ys]
    # the root strip under the fuselage: continue the inner panel's taper to the centreline
    slope = (ch[ys.index(2.0)] - ch[0]) / (2.0 - ys[0])
    pts = [(0.0, ch[0] - slope * ys[0])] + list(zip(ys, ch))
    # the tip: the drawing rounds the last 0.25 m; close it at the span with the outer panel's own taper
    pts = [p for p in pts if p[0] <= 8.5]
    outer = (pts[-1][1] - dict(pts)[6.0]) / (pts[-1][0] - 6.0)
    pts.append((HALF, pts[-1][1] + outer * (HALF - pts[-1][0])))
    return pts

RAW = wing_chord_table()
def _interp_ii(pts, y):
    y = abs(y)
    for (y0, c0), (y1, c1) in zip(pts, pts[1:]):
        if y0 <= y <= y1: return c0 + (c1 - c0) * (y - y0) / (y1 - y0)
    return pts[-1][1]
def _interp(pts, y): return _interp_ii(pts, abs(y) / STRETCH)     # Twin III station -> the Twin II drawing's
def _integrate(f, a, b, n=2000):
    h = (b - a) / n
    return sum(f(a + (i + 0.5) * h) for i in range(n)) * h
RAW_AREA = 2 * _integrate(lambda y: _interp(RAW, y), 0, HALF)
CHORD_SCALE = AREA / RAW_AREA
# The Twin II drawing's own chord correction (its planform measured 17.554 m2 against [FM-II]'s 17.8): the tail, which
# is kept from that drawing unstretched, takes this one, not the wing's stretched-and-rescaled factor.
CHORD_SCALE_II = 17.8 / (RAW_AREA / STRETCH)
def chord(y): return _interp(RAW, y) * CHORD_SCALE
MAC = 2 * _integrate(lambda y: chord(y) ** 2, 0, HALF) / AREA
# The Twin III's MAC is not published (the [POH] gives its CG range in mm only); the Twin II's [FM-II] II.7 MAC no longer
# applies, so %MAC below is of this planform's own integral MAC.
MAC_PUB = MAC
say(f"[wing] Twin II planform stretched x {STRETCH:.4f} spanwise: area {RAW_AREA:.3f} m2 -> chords x {CHORD_SCALE:.4f} "
    f"to the published {AREA} m2 (the Twin II drawing's own correction was x {CHORD_SCALE_II:.4f})")
say(f"[wing] root {chord(0):.3f} m, y 4.89 {chord(4.89):.3f}, y 8.23 {chord(8.23):.3f}, tip {chord(HALF):.3f}; "
    f"MAC {MAC:.3f} m (not published); AR {SPAN**2/AREA:.2f} ([POH] 18.5)")

# Leading edge: straight and unswept -- the measured LE stations scatter within +-0.03 m of the root's once the
# drawing's 0.64 deg tilt cancels between the two sides (grob103_threeview.json 'front'/'wing').
LE_X = 0.0
DIHEDRAL = TV['front']['dihedralDeg'] * D2R          # front view, both halves fitted, tilt removed: 2.92 deg
say(f"[wing] dihedral {TV['front']['dihedralDeg']:.2f} deg (front view; the drawing is tilted "
    f"{TV['front']['drawingTiltDeg']:.2f} deg, removed by averaging the two halves)")

# Ailerons: the hinge line drawn on the plan view (the interior line aft of 0.7 c) from y 5.0 m to the tip at
# y 8.6 m; its chord measures 0.24-0.26 of the local chord along its length. The airbrake box is the interior
# line at 0.40-0.45 c, y 2.45-3.70 m.
def _inner_lines(lo, hi):
    out = []
    for s in TV['wing']['stations']:
        for d in s['inner']:
            f = d / s['chord']
            if lo < f < hi: out.append((abs(s['y']) * STRETCH, f, s['chord']))
    return out
ail = _inner_lines(0.6, 0.85)
AIL_IN = round(min(a[0] for a in ail), 2); AIL_OUT = round(max(a[0] for a in ail), 2)
AIL_E = sum(1 - a[1] for a in ail) / len(ail)       # aileron chord / local chord
brk = _inner_lines(0.35, 0.5)
BRK_IN = round(min(b[0] for b in brk), 2); BRK_OUT = round(max(b[0] for b in brk), 2)
BRK_XC = sum(b[1] for b in brk) / len(brk)
say(f"[wing] aileron y {AIL_IN}-{AIL_OUT} m, chord ratio E = {AIL_E:.3f}; airbrake box y {BRK_IN}-{BRK_OUT} m "
    f"at x/c {BRK_XC:.3f}")

# Dihedral, aileron chord ratio and the airbrake box's x/c are the Twin II drawing's: none is published for the
# Twin III (ASSUMED the same). Washout and rigging incidence are NOT published (GAP). Washout: 1.5 deg, linear root to tip -- an assumption,
# the middle of the 1-2 deg that glass two-seaters of the period carry; the derivation shows its sensitivity.
WASHOUT = 1.5 * D2R

# =====================================================================================================================
# 3. SECTIONS (XFOIL) AND THE LIFTING LINE
# =====================================================================================================================
def polar(foil, re):
    return XF['foils'][foil]['polars'][f'{re:.0f}']
def section_props(foil, re):
    p = [q for q in polar(foil, re)]
    def cl_at(a):
        for q0, q1 in zip(p, p[1:]):
            if q0['alphaDeg'] <= a <= q1['alphaDeg']:
                t = (a - q0['alphaDeg']) / (q1['alphaDeg'] - q0['alphaDeg']); return q0['cl'] + t * (q1['cl'] - q0['cl'])
    a0 = (cl_at(4.0) - cl_at(-2.0)) / (6.0 * D2R)
    alpha0 = -cl_at(0.0) / a0 / D2R
    top = max(p, key=lambda q: q['cl'])
    cm0 = [q['cm'] for q in p if abs(q['alphaDeg'] - alpha0) < 0.6]
    return {'a0': a0, 'alpha0Deg': alpha0, 'clmax': top['cl'], 'alphaClmaxDeg': top['alphaDeg'],
            'cdmin': min(q['cd'] for q in p), 'cm0': sum(cm0) / len(cm0)}

# The Twin III Acro's wing is the Eppler E583 at root and tip ([UIUC]), not the Twin II's E603. XFOIL's 2.5 M sweep
# did not converge past 13.5 deg (its cl_max stops there, below the 2.0 M one), so the family ends at 2.0 M and is
# held there above it (data/grob103_xfoil.json keeps the 2.5 M points for the record).
RES = [0.6e6, 0.8e6, 1.0e6, 1.25e6, 1.5e6, 2.0e6]
for re in RES:
    s = section_props('e583', re)
    say(f"[E583 Re {re/1e6:.2f}M] a0 {s['a0']:.3f}/rad  alpha0 {s['alpha0Deg']:+.2f} deg  clmax {s['clmax']:.3f} at "
        f"{s['alphaClmaxDeg']:.1f} deg  cdmin {s['cdmin']:.5f}  cm0 {s['cm0']:+.4f}")

# Each strip NAMES the table for its chord at 90 km/h (25 m/s); AeroModel then flies it at its own instantaneous
# Reynolds number across the whole family (0.6-2.5 million), so the polar follows Re with speed and with mass.
V_REF = 25.0
def re_of(c): return V_REF * c / NU
def nearest_re(c): return min(RES, key=lambda r: abs(math.log(r / re_of(c))))

def lifting_line(alpha_deg, n=41):
    """Glauert's monoplane equation for this planform (symmetric loading, odd terms), E583 at Re 1.5M, with the
    washout. Returns CL, induced-drag factor e, a_3D (per rad), and the local cl distribution."""
    sec = section_props('e583', 1.5e6)
    a0, al0 = sec['a0'], sec['alpha0Deg'] * D2R
    th = [(i + 0.5) * math.pi / (2 * n) for i in range(n)]         # half span: theta in (0, pi/2)
    ks = [2 * j + 1 for j in range(n)]
    A = [[0.0] * n for _ in range(n)]; rhs = [0.0] * n
    for i, t in enumerate(th):
        y = HALF * math.cos(t)
        c = chord(y)
        mu = a0 * c / (4 * SPAN)
        twist = -WASHOUT * abs(y) / HALF
        for j, k in enumerate(ks):
            A[i][j] = math.sin(k * t) * (1 + mu * k / math.sin(t))
        rhs[i] = mu * (alpha_deg * D2R + twist - al0)
    # Gaussian elimination
    for col in range(n):
        piv = max(range(col, n), key=lambda r: abs(A[r][col]))
        A[col], A[piv] = A[piv], A[col]; rhs[col], rhs[piv] = rhs[piv], rhs[col]
        for r in range(col + 1, n):
            f = A[r][col] / A[col][col]
            for cc in range(col, n): A[r][cc] -= f * A[col][cc]
            rhs[r] -= f * rhs[col]
    x = [0.0] * n
    for r in range(n - 1, -1, -1):
        x[r] = (rhs[r] - sum(A[r][cc] * x[cc] for cc in range(r + 1, n))) / A[r][r]
    ar = SPAN ** 2 / AREA
    CL = math.pi * ar * x[0]
    delta = sum(k * (x[j] / x[0]) ** 2 for j, k in enumerate(ks) if j > 0)
    cls = [(HALF * math.cos(t), 4 * SPAN * sum(x[j] * math.sin(k * t) for j, k in enumerate(ks)) / chord(HALF * math.cos(t)))
           for t in th]
    return CL, 1 / (1 + delta), cls

cl_a, e_a, _ = lifting_line(2.0); cl_b, e_b, dist = lifting_line(6.0)
A3D = (cl_b - cl_a) / (4.0 * D2R)
SEC15 = section_props('e583', 1.5e6)
SCALE3D = A3D / SEC15['a0']
cl_des, E_SPAN, dist_des = lifting_line(4.0)
say(f"[lifting line] a_3D {A3D:.3f}/rad = {SCALE3D:.3f} x the 2-D {SEC15['a0']:.3f}; span efficiency e "
    f"{E_SPAN:.4f} at CL {cl_des:.2f} ({e_a:.4f} at CL {cl_a:.2f}, {e_b:.4f} at CL {cl_b:.2f})")
peak = max(dist_des, key=lambda p: p[1] / cl_des)
say(f"[lifting line] local cl/CL peaks at {peak[1]/cl_des:.3f} at y {peak[0]:.2f} m (where the stall starts)")

def wing_table(re):
    """The finite-span E583 table at Re: XFOIL's attached range with alpha stretched about the zero-lift angle by
    the lifting line's a_3D/a_2D (the downwash of the whole wing), XFOIL's profile drag and quarter-chord moment
    unchanged (the sim adds induced drag per strip), then the flat-plate post-stall and reversed flow the fleet's
    tables use (poststall_plate.py, reversed_flow.py; Hoerner CD90 1.20 at AR 17 is the finite-plate figure)."""
    p = polar('e583', re)
    al0 = section_props('e583', re)['alpha0Deg']
    pk = max(p, key=lambda q: q['cl'])
    # attached + XFOIL's own stall: keep everything XFOIL converged on, to 4 deg past the peak
    pts = [q for q in p if q['alphaDeg'] <= pk['alphaDeg'] + 4.0]
    rows = {}
    for q in pts:
        a3 = al0 + (q['alphaDeg'] - al0) / SCALE3D
        rows[round(a3, 3)] = (q['cl'], q['cd'], q['cm'])
    lo, hi = min(rows), max(rows)
    cl_hi, cd_hi, cm_hi = rows[hi]; cl_lo, cd_lo, cm_lo = rows[lo]
    cd0 = min(v[1] for v in rows.values())
    CD90 = 1.20 + cd0      # Hoerner, plate normal to the flow at AR 10-20: 1.20-1.23 (lowar_plate.py's table)
    def plate(a): s, c = math.sin(a * D2R), math.cos(a * D2R); return CD90 * s * c, cd0 + CD90 * s * s
    for sgn, edge, (clE, cdE, cmE) in ((1, hi, rows[hi]), (-1, lo, rows[lo])):
        start = abs(edge)
        grid = [d for d in range(int(start) + 2, 91, 2)] + [d for d in range(95, 181, 5)]
        for d in grid:
            a = sgn * d
            clp, cdp = plate(a)
            t = min(1.0, (d - start) / 12.0)            # blend over 12 deg into the plate
            cl = (1 - t) * clE + t * clp; cd = (1 - t) * cdE + t * cdp
            cn = cl * math.cos(a * D2R) + cd * math.sin(a * D2R)
            xcp = 0.25 + 0.25 * min(1.0, (d - start) / (90.0 - start)) if d <= 90 else 0.5
            cm = (1 - t) * cmE + t * (-cn * (xcp - 0.25))
            rows[round(a, 3)] = (cl, cd, cm)
    keys = sorted(rows)
    tb = {'alphaRad': [round(k * D2R, 5) for k in keys], 'cl': [round(rows[k][0], 4) for k in keys],
          'cd': [round(rows[k][1], 5) for k in keys], 'cm': [round(rows[k][2], 4) for k in keys]}
    reversed_fix(tb)
    return tb

def tail_table(re, scale):
    """FX 71-L-150/30 at Re, stretched about zero lift by `scale` (finite span), flat-plate beyond. Used for the
    FIN, whose table the sim does not rescale (AeroModel.TailSections only rescales the hStab system)."""
    p = polar('fx71l150', re)
    pk = max(p, key=lambda q: q['cl']); mn = min(p, key=lambda q: q['cl'])
    rows = {}
    for q in p:
        if mn['alphaDeg'] - 3 <= q['alphaDeg'] <= pk['alphaDeg'] + 3:
            rows[round(q['alphaDeg'] / scale, 3)] = (q['cl'], q['cd'], q['cm'])
    cd0 = min(v[1] for v in rows.values()); CD90 = 1.18 + cd0     # Hoerner plate at AR 1-2 (lowar_plate.py)
    lo, hi = min(rows), max(rows)
    for sgn, edge in ((1, hi), (-1, lo)):
        clE, cdE, cmE = rows[edge]; start = abs(edge)
        for d in list(range(int(start) + 2, 91, 2)) + list(range(95, 181, 5)):
            a = sgn * d; s, c = math.sin(a * D2R), math.cos(a * D2R)
            t = min(1.0, (d - start) / 12.0)
            cl = (1 - t) * clE + t * CD90 * s * c; cd = (1 - t) * cdE + t * (cd0 + CD90 * s * s)
            rows[round(a, 3)] = (cl, cd, (1 - t) * cmE)
    keys = sorted(rows)
    tb = {'alphaRad': [round(k * D2R, 5) for k in keys], 'cl': [round(rows[k][0], 4) for k in keys],
          'cd': [round(rows[k][1], 5) for k in keys], 'cm': [round(rows[k][2], 4) for k in keys]}
    reversed_fix(tb)
    return tb

# =====================================================================================================================
# 4. WING STRIPS
# =====================================================================================================================
# Strip edges per half-wing: every control end is an edge (airbrake box, aileron), finer outboard. The fixed edges are
# the Twin II strip layout, stretched with the rest of the span.
EDGES = [0.0] + [e * STRETCH for e in (0.85, 1.65)] + [BRK_IN, 0.5 * (BRK_IN + BRK_OUT), BRK_OUT, 4.35 * STRETCH, AIL_IN] + \
        [e * STRETCH for e in (5.6, 6.2, 6.8, 7.35, 7.85, 8.25)] + [AIL_OUT, HALF]
# The rigging incidence is not published either. A glider's fuselage is set to meet the air nose-on at the speed it
# spends its life at, so the root incidence is the wing's own 3-D angle of attack at the published best-glide point:
# [POH] 5.7, 579 kg at 95 km/h -> CL 2W/(rho V^2 S). The boom then trails the flow at best glide.
CL_BG = 2 * 579.0 * G / (RHO * (95 / 3.6) ** 2 * AREA)
SEC_ROOT = section_props('e583', nearest_re(chord(0.4)))
INCIDENCE = (SEC_ROOT['alpha0Deg'] * D2R + CL_BG / A3D)
say(f"[rigging] root incidence {INCIDENCE/D2R:.2f} deg (body alpha 0 at CL {CL_BG:.3f}), washout {WASHOUT/D2R:.1f} deg")

def tau_gain(E):
    """Fixed-strip gain from the hinged chord ratio: g = (0.85 tau(E) - E)/(1 - E), thin-airfoil tau (Glauert),
    x0.85 viscous (docs/CONTROL-SURFACES.md 'Control effectiveness')."""
    th = math.acos(2 * E - 1)
    tau = 1 - (th - math.sin(th)) / math.pi
    return (0.85 * tau - E) / (1 - E), tau

AIL_GAIN, AIL_TAU = tau_gain(AIL_E)
wing, wing_ail, airfoil_keys = [], [], set()
brake_strips = []
for side in (-1, 1):
    for y0, y1 in zip(EDGES, EDGES[1:]):
        yc = 0.5 * (y0 + y1); w = y1 - y0
        c = _integrate(chord, y0, y1, 200) / w
        has_ail = AIL_IN - 1e-6 <= y0 and y1 <= AIL_OUT + 1e-6
        E = AIL_E if has_ail else 0.0
        inc = INCIDENCE - WASHOUT * yc / HALF
        z = -yc * math.tan(DIHEDRAL)
        re = nearest_re(c); key = f'e583-re{re/1e6:.2f}M-3d'; airfoil_keys.add((key, re))
        cf = c * (1 - E)
        st = {'pos': [round(LE_X - 0.25 * cf, 4), round(side * yc, 4), round(z, 4)], 'chord': round(cf, 4),
              'area': round(cf * w, 5), 'incidenceRad': round(inc, 5), 'dihedralRad': round(DIHEDRAL, 5),
              'airfoil': key, 'control': None}
        if has_ail:
            st['control'] = {'surface': 'aileron', 'gain': round(-side * AIL_GAIN, 4)}
            ch = c * E
            wing_ail.append({'pos': [round(LE_X - cf - 0.25 * ch, 4), round(side * yc, 4), round(z, 4)],
                             'chord': round(ch, 4), 'area': round(ch * w, 5), 'incidenceRad': round(inc, 5),
                             'dihedralRad': round(DIHEDRAL, 5), 'airfoil': key,
                             'control': {'surface': 'aileron', 'gain': float(-side)}})
        wing.append(st)
wsum = sum(s['area'] for s in wing) + sum(s['area'] for s in wing_ail)
say(f"[wing] {len(wing)} strips + {len(wing_ail)} aileron strips, area {wsum:.3f} m2; aileron E {AIL_E:.3f} -> tau "
    f"{AIL_TAU:.3f}, fixed-strip gain {AIL_GAIN:.3f}")

# =====================================================================================================================
# 5. TAIL (three-view; tail sections ASSUMED Wortmann FX 71-L-150/30 -- the Twin II's tail section is not published)
# =====================================================================================================================
# Horizontal tail: plan view, line centres. This is the TWIN II drawing's tail (its original, pre s/n 33879 tailplane);
# no Twin III tail dimension is published, so the tailplane, fin and rudder are KEPT from it (ASSUMED, flagged).
tb = {}
for s in TV['tailplane']['stations']:
    b = round(abs(s['y']) * 8) / 8
    tb.setdefault(b, []).append((s['chord'], s['elevChord'], s['leX']))
T_HALF = TV['tailplane']['spanM'] / 2
tys = sorted(tb)
TROWS = [(y, sum(v[0] for v in tb[y]) / len(tb[y]), sum(v[1] for v in tb[y]) / len(tb[y]),
          sum(v[2] for v in tb[y]) / len(tb[y])) for y in tys]
k_plan = TV['wing']['scale_m_per_px']
WING_LE_PLAN = 1872 * k_plan                     # the plan view's root LE (px 1872) on the same absolute scale
def tail_le_body(le_plan): return -(WING_LE_PLAN - le_plan) * PLAN_X
def trow(y):
    y = abs(y)
    for r0, r1 in zip(TROWS, TROWS[1:]):
        if r0[0] <= y <= r1[0]:
            t = (y - r0[0]) / (r1[0] - r0[0]); return tuple(r0[i] + t * (r1[i] - r0[i]) for i in (1, 2, 3))
    r = TROWS[-1] if y > TROWS[-1][0] else TROWS[0]
    return r[1], r[2], r[3]
STAB_INC = -0.68 * D2R
STAB_Z = -1.20      # side view: the tailplane sits on the fin top, 172 px (1.20 m) above the reference line
T_EDGES = [0.0, 0.3, 0.6, 0.9, 1.15, 1.38, 1.56, T_HALF]
stab, elev = [], []
for side in (-1, 1):
    for y0, y1 in zip(T_EDGES, T_EDGES[1:]):
        yc = 0.5 * (y0 + y1); w = y1 - y0
        c, ce, le = trow(yc)
        c *= CHORD_SCALE_II; ce *= CHORD_SCALE_II
        le_b = tail_le_body(le)
        cf = c - ce
        stab.append({'pos': [round(le_b - 0.25 * cf, 4), round(side * yc, 4), STAB_Z], 'chord': round(cf, 4),
                     'area': round(cf * w, 5), 'incidenceRad': round(STAB_INC, 5), 'dihedralRad': 0.0, 'sweepRad': 0.0,
                     'airfoil': 'fx71l150-2d', 'control': {'surface': 'elevator', 'gain': None, '_E': ce / c}})
        elev.append({'pos': [round(le_b - cf - 0.25 * ce, 4), round(side * yc, 4), STAB_Z], 'chord': round(ce, 4),
                     'area': round(ce * w, 5), 'incidenceRad': 0.0, 'dihedralRad': 0.0, 'sweepRad': 0.0,
                     'airfoil': 'fx71l150-2d', 'control': {'surface': 'elevator', 'gain': 1.0}})
S_H = sum(s['area'] for s in stab) + sum(s['area'] for s in elev)
E_EL = sum(s['area'] for s in elev) / S_H
EL_GAIN, EL_TAU = tau_gain(E_EL)
for s in stab: s['control'] = {'surface': 'elevator', 'gain': round(EL_GAIN, 4)}
# Stabiliser incidence (unpublished). Rigged by the design rule a sailplane's tail is set to: the elevator streamlined
# (zero deflection) at the MID-ENVELOPE CG (375 mm = (270 + 480)/2, [POH] 2.7) at the best-glide condition ([POH] 5.7:
# 579 kg, 95 km/h, CL 0.76), so the trim's travel is shared out evenly either side. Solved in the sim (GROB_RIG=1
# Grob103Rigging), stepping stab += 0.53 x elevator (0.53 = 0.85 tau(E 0.259), the elevator's section effectiveness)
# from the Twin II's -1.53 deg: elevator +1.29 -> stab -0.85; +0.33 -> -0.68; residual +0.09 deg. The trim is a spring
# trim on the Twin II ([TCDS-EASA] A.250 Section L Note 8); the Twin III's [POH] speaks only of a "trim device" --
# trimType "spring" KEPT.

# Fin and rudder: side view (scale 6.98 mm/px), rows quoted in the derivation. LE root (boom top, z -0.06) at
# 6.80 m from the nose, LE top (z -1.17) at 7.30; hinge line 7.55 m at the rudder foot (z +0.21) to 7.82 m at the
# top; rudder trailing edge 7.95 to 8.15 m. Swept LE 22.6 deg.
def lerp(a, b, t): return a + (b - a) * t
FIN_Z0, FIN_Z1, RUD_Z0 = -0.06, -1.17, 0.21
def fin_le(z): return lerp(6.80, 7.30, (z - FIN_Z0) / (FIN_Z1 - FIN_Z0))
def hinge(z): return lerp(7.55, 7.82, (z - RUD_Z0) / (FIN_Z1 - RUD_Z0))
def rud_te(z): return lerp(7.95, 8.15, (z - RUD_Z0) / (FIN_Z1 - RUD_Z0))
fin, rud = [], []
F_EDGES = [FIN_Z0, -0.30, -0.55, -0.80, -1.00, FIN_Z1]
for z0, z1 in zip(F_EDGES, F_EDGES[1:]):
    zc = 0.5 * (z0 + z1); h = abs(z1 - z0)
    le, hg = fin_le(zc), hinge(zc)
    cf = hg - le
    fin.append({'pos': [round(body_x_from_nose(le) - 0.25 * cf, 4), 0.0, round(zc, 4)], 'chord': round(cf, 4),
                'area': round(cf * h, 5), 'incidenceRad': 0.0, 'dihedralRad': 0.0,
                'sweepRad': round(math.atan((7.30 - 6.80) / (FIN_Z0 - FIN_Z1)), 4), 'airfoil': 'fx71l150-fin',
                'control': {'surface': 'rudder', 'gain': None}})
R_EDGES = [RUD_Z0, FIN_Z0] + F_EDGES[1:]
for z0, z1 in zip(R_EDGES, R_EDGES[1:]):
    zc = 0.5 * (z0 + z1); h = abs(z1 - z0)
    hg, te = hinge(zc), rud_te(zc)
    cr = te - hg
    rud.append({'pos': [round(body_x_from_nose(hg) - 0.25 * cr, 4), 0.0, round(zc, 4)], 'chord': round(cr, 4),
                'area': round(cr * h, 5), 'incidenceRad': 0.0, 'dihedralRad': 0.0, 'sweepRad': 0.0,
                'airfoil': 'fx71l150-fin', 'control': {'surface': 'rudder', 'gain': 1.0}})
S_FIN = sum(s['area'] for s in fin); S_RUD = sum(s['area'] for s in rud)
E_RUD = S_RUD / (S_FIN + S_RUD)
RUD_GAIN, RUD_TAU = tau_gain(min(0.5, E_RUD))
for s in fin: s['control']['gain'] = round(RUD_GAIN, 4)
# fin finite-span slope: DATCOM/Helmbold with the end-plate factor AeroModel uses for fins (x1.55; a T-tail's
# stab is exactly such an end plate)
fin_h = RUD_Z0 - FIN_Z1
AR_FIN = fin_h ** 2 / (S_FIN + S_RUD) * 1.55
tail_sec = section_props('fx71l150', 0.8e6)
kap = tail_sec['a0'] / (2 * math.pi)
a_fin = 2 * math.pi * AR_FIN / (2 + math.sqrt(AR_FIN ** 2 / kap ** 2 + 4))
FIN_SCALE = a_fin / tail_sec['a0']
TAIL_MAC_X = sum(s['pos'][0] * s['area'] for s in stab + elev) / S_H
say(f"[tail] S_h {S_H:.3f} m2 (span {2*T_HALF:.2f} m, elevator {E_EL:.3f} of it -> gain {EL_GAIN:.3f}); fin {S_FIN:.3f} + "
    f"rudder {S_RUD:.3f} m2 (E {E_RUD:.3f} -> gain {RUD_GAIN:.3f}); fin effective AR {AR_FIN:.2f} -> slope "
    f"{a_fin:.2f}/rad = {FIN_SCALE:.3f} x 2-D")
say(f"[tail] FX 71-L-150/30 Re 0.8M: a0 {tail_sec['a0']:.3f}/rad, alpha0 {tail_sec['alpha0Deg']:+.2f}, clmax {tail_sec['clmax']:.3f}")

# =====================================================================================================================
# 6. FUSELAGE: profiles, wetted area, parasite-drag build-up
# =====================================================================================================================
# (from nose m, plan width m, side depth m) -- plan view widths less one line width (0.035 m), side view depths
# keel-to-canopy, both from the row scans quoted in the derivation.
PROFILE = [(0.0, 0.0, 0.0), (0.31, 0.40, 0.45), (0.52, 0.52, 0.63), (0.73, 0.60, 0.75), (0.94, 0.66, 0.85),
           (1.15, 0.70, 0.91), (1.57, 0.72, 1.00), (1.99, 0.73, 1.03), (2.41, 0.72, 1.03), (2.83, 0.69, 1.04),
           (3.25, 0.63, 0.87), (3.66, 0.55, 0.75), (4.08, 0.48, 0.62), (4.50, 0.43, 0.52), (4.92, 0.38, 0.45),
           (5.34, 0.34, 0.38), (5.76, 0.31, 0.34), (6.18, 0.28, 0.31), (6.60, 0.25, 0.29), (7.02, 0.23, 0.28),
           (7.45, 0.20, 0.27), (7.55, 0.0, 0.0)]
def ellipse_perim(a, b):
    a, b = a / 2, b / 2
    if a + b == 0: return 0.0
    h = ((a - b) / (a + b)) ** 2
    return math.pi * (a + b) * (1 + 3 * h / (10 + math.sqrt(4 - 3 * h)))       # Ramanujan
SWET_F = sum(0.5 * (ellipse_perim(w0, d0) + ellipse_perim(w1, d1)) * (x1 - x0)
             for (x0, w0, d0), (x1, w1, d1) in zip(PROFILE, PROFILE[1:]))
# less the wing root footprint (two sides, root chord x mean thickness ~ chord x 2) and the fin root footprint
SWET_F -= 2 * chord(0) * 0.40
SWET_F -= 0.80 * 0.25
AMAX = max(math.pi / 4 * w * d for _, w, d in PROFILE)
DEQ = math.sqrt(4 * AMAX / math.pi)
FINE = LENGTH / DEQ
FF = 1 + 60 / FINE ** 3 + FINE / 400                       # Raymer eq. 12.31 (fuselage form factor)
V_BG = 95 / 3.6                                            # [POH] 5.7 best glide, 579 kg
RE_L = V_BG * LENGTH / NU
CF_T = 0.455 / math.log10(RE_L) ** 2.58                    # Prandtl-Schlichting, fully turbulent
# Transition at the canopy's front frame (1.0 m from the nose): the glass nose ahead of it is smooth and in a
# favourable gradient. Schlichting's mixed-flow correction CF = CF_turb - A/Re_L, A = Re_x,tr(CF_turb(x) - CF_lam(x)).
X_TR = 1.0
RE_TR = V_BG * X_TR / NU
A_MIX = RE_TR * (0.455 / math.log10(RE_TR) ** 2.58 - 1.328 / math.sqrt(RE_TR))
CF_F = CF_T - A_MIX / RE_L
CDA_FUS = CF_F * FF * SWET_F
# Wing-fuselage junction (Hoerner, Fluid-Dynamic Drag 8-11, one junction per side): dCD.t^2 = 0.75 t/c - 0.0003/(t/c)^2,
# halved for the moulded root fairing the Twin III has (Hoerner: a fillet removes about half to all of it).
TC_ROOT = 0.165                                             # E583 thickness, 16.5 % at 0.41 c (UIUC coordinates)
t_root = TC_ROOT * chord(0)
CDA_JUNC = 2 * 0.5 * (0.75 * TC_ROOT - 0.0003 / TC_ROOT ** 2) * t_root ** 2
# T-junction fin/tailplane and fin/fuselage (same formula, full value: no fillets drawn)
TC_TAIL = 0.15
CDA_TJ = 2 * (0.75 * TC_TAIL - 0.0003 / TC_TAIL ** 2) * (TC_TAIL * 0.52) ** 2 + \
         (0.75 * TC_TAIL - 0.0003 / TC_TAIL ** 2) * (TC_TAIL * 0.80) ** 2
# Wheels, part-buried in the fuselage (Hoerner 13-3: a wheel half in a well CD ~0.25 on its exposed frontal area).
# [FM-II] tyres (the Twin III's sizes are not in the [POH]; ASSUMED the same): main 6.00-6 (0.40 m dia, 0.15 wide) showing 0.21 m below the keel; nose 260x85 showing ~0.10 m;
# tail 210x65 showing ~0.09 m (side view).
CDA_WHEELS = 0.25 * (0.15 * 0.21 + 0.085 * 0.10 + 0.065 * 0.09)
# Canopy frames, vents, tow hooks, the pitot in the fin, control gaps: Thomas ('Fundamentals of Sailplane Design'
# 1999, ch. 5) budgets ~5 % of the fuselage drag for these on a glass ship.
CDA_MISC = 0.05 * CDA_FUS
CD0_AREA = CDA_FUS + CDA_JUNC + CDA_TJ + CDA_WHEELS + CDA_MISC
say(f"[fuselage] wetted {SWET_F:.2f} m2, max section {AMAX:.3f} m2 (d_eq {DEQ:.3f}), fineness {FINE:.2f}, FF {FF:.3f}; "
    f"Re_L {RE_L:.3g}: CF turb {CF_T:.5f}, transition at {X_TR} m -> {CF_F:.5f}")
say(f"[drag areas m2] fuselage {CDA_FUS:.4f}  wing junction {CDA_JUNC:.4f}  tail junctions {CDA_TJ:.4f}  wheels "
    f"{CDA_WHEELS:.4f}  misc {CDA_MISC:.4f}  => cd0Area {CD0_AREA:.4f}")

# crossflow / body geometry for the big-angle terms (side and plan silhouettes)
def area_x(i):
    return sum(0.5 * (p0[i] + p1[i]) * (p1[0] - p0[0]) for p0, p1 in zip(PROFILE, PROFILE[1:]))
def centroid_x(i):
    m = sum(0.5 * (p0[i] + p1[i]) * (p1[0] - p0[0]) * 0.5 * (p0[0] + p1[0]) for p0, p1 in zip(PROFILE, PROFILE[1:]))
    return body_x_from_nose(m / area_x(i))
PLAN_A, SIDE_A = area_x(1), area_x(2)
SIDE_A_TOTAL = SIDE_A + S_FIN + S_RUD
say(f"[fuselage] plan area {PLAN_A:.2f} m2 at x {centroid_x(1):+.2f}; side area {SIDE_A:.2f} m2 at x {centroid_x(2):+.2f}")

# =====================================================================================================================
# 7. MASS, CG AND INERTIA (component build-up, protocol 1c)
# =====================================================================================================================
EMPTY_PUB = 399.0       # [POH] 5.7, the flight-polar airframe D-4279: "flight weight = empty weight + 180 kg = 579 kg".
                        # The [POH] gives no type empty mass (it is per airframe, Sec. 6); N41KP weighs 439.7 kg with
                        # its equipment ([N41KP] 969.3 lb).
MTOM = 600.0            # [POH] 2.6 "Max. permissible take-off mass 600 kg"; [TCDS] N.III.9
MAX_NLP = 420.0         # [POH] 2.6 "Max. permissible mass of all non-lifting parts 420 kg"; [TCDS] N.III.9
# WINGS: the max mass of non-lifting parts is the wing-root bending design point -- n x (W - W_wing) at MTOM -- so
# the wing pair is MTOM - NLP_max = 180 kg (90 kg a side). Distributed along the span in proportion to chord^2
# (skin and spar caps both scale with the section's chord x thickness), chordwise at 0.40 c (spar at ~0.3-0.35 c,
# skins and trailing edge aft of it).
M_WINGS = MTOM - MAX_NLP
items = []      # (name, kg, x_body, y, z, (ixx_own, iyy_own, izz_own))
wsq = [(0.5 * (y0 + y1), _integrate(lambda y: chord(y) ** 2, y0, y1, 100))
       for y0, y1 in zip([i * 0.25 for i in range(36)], [i * 0.25 for i in range(1, 37)])]
tot = sum(v for _, v in wsq)
for y, v in wsq:
    m = M_WINGS / 2 * v / tot
    for side in (-1, 1):
        items.append(('wing', m, LE_X - 0.40 * chord(y), side * y, -y * math.tan(DIHEDRAL), (0, 0, 0)))
# TAIL: GFRP sandwich ~5 kg/m2 of planform for a tailplane with its elevator, ~5.5 for a fin with its rudder and
# hinge fittings (Thomas & Milgram's component masses for glass ships of the period run 4.5-6).
M_HT = round(5.0 * S_H, 1)
items.append(('tailplane+elevator', M_HT, TAIL_MAC_X, 0, STAB_Z, (M_HT * (2 * T_HALF) ** 2 / 12, 0, M_HT * (2 * T_HALF) ** 2 / 12)))
M_VT = round(5.5 * (S_FIN + S_RUD), 1)
x_vt = sum(s['pos'][0] * s['area'] for s in fin + rud) / (S_FIN + S_RUD)
items.append(('fin+rudder', M_VT, x_vt, 0, -0.55, (M_VT * 1.38 ** 2 / 12, M_VT * 1.38 ** 2 / 12, 0)))
# FUSELAGE: what is left of the empty mass, split into the concentrated items and a shell spread over the wetted area.
# Wheel stations: main and tail from the [N41KP] weighing ("datum to main gear 21.25 in", "datum to tail wheel
# 192.00 in" = 540 / 4877 mm aft); the nose wheel's is not given there, so it stays where the Twin II drawing puts it.
MAIN_X = -21.25 * 0.0254
TAIL_X = -192.00 * 0.0254
NOSE_X = body_x_from_nose(0.858)
NOSE_STEER = 25.0 * D2R                     # ESTIMATED nose-wheel steering lock (no published figure; FLAGGED)
gear = [('main wheel, brake, axle, mount', 12.0, MAIN_X, 0, 0.49),
        ('nose wheel', 3.0, NOSE_X, 0, 0.56),
        ('tail wheel', 2.0, TAIL_X, 0, 0.23)]
fixed = gear + [
    ('front canopy (frame + acrylic)', 9.0, body_x_from_nose(1.45), 0, -0.30),
    ('rear canopy', 9.0, body_x_from_nose(2.75), 0, -0.42),
    ('front panel + instruments', 6.0, body_x_from_nose(0.75), 0, -0.10),
    ('rear panel + instruments', 5.0, body_x_from_nose(1.95), 0, -0.15),
    ('seats, harnesses, sticks, pedals (front)', 8.0, body_x_from_nose(1.10), 0, 0.30),
    ('seats, harnesses, sticks, pedals (rear)', 7.0, body_x_from_nose(2.35), 0, 0.30),
    ('wing-root fittings + main frame', 15.0, -0.35, 0, 0.0),
    ('control runs, airbrake + trim linkage', 6.0, body_x_from_nose(3.6), 0, 0.05),
]
m_shell = EMPTY_PUB - M_WINGS - M_HT - M_VT - sum(f[1] for f in fixed)
segs = []
for (x0, w0, d0), (x1, w1, d1) in zip(PROFILE, PROFILE[1:]):
    a = 0.5 * (ellipse_perim(w0, d0) + ellipse_perim(w1, d1)) * (x1 - x0)
    segs.append((0.5 * (x0 + x1), a, 0.25 * (w0 + w1 + d0 + d1) / 2))
# The shell's areal density is the one thing nobody publishes. Spread uniformly over the wetted area it puts the
# empty CG well FORWARD of where a weighed Twin III has it. A tail boom carries the whole tail's bending and torsion in
# a small perimeter, so its laminate is heavier per square metre than the cockpit's big shell. Model the density as
# rising linearly along the fuselage, w = 1 + k (x/L), and SOLVE k so the empty CG lands on the one Twin III empty CG
# found: [N41KP] "CG (Empty) = 28.69 in" = 729 mm aft of datum (that airframe weighs 440 kg, this one 399: the pairing
# is two sources, flagged). The [POH] publishes no empty-CG range (Maintenance Manual Sec. 7, not available).
EMPTY_CG_PUB = 28.69 * 0.0254
def shell_items(k):
    wts = [a * (1 + k * xm / LENGTH) for xm, a, _ in segs]; wt = sum(wts)
    return [('shell', m_shell * w / wt, body_x_from_nose(xm), 0, 0.0,
             (m_shell * w / wt * r * r, 0.5 * m_shell * w / wt * r * r, 0.5 * m_shell * w / wt * r * r))
            for (xm, a, r), w in zip(segs, wts)]
base_items = list(items) + [(f[0], f[1], f[2], f[3], f[4], (0, 0, 0)) for f in fixed]
def empty_cg(k):
    its = base_items + shell_items(k)
    return -sum(i[1] * i[2] for i in its) / sum(i[1] for i in its)
CG_UNIFORM = empty_cg(0.0)
lo, hi = 0.0, 50.0
for _ in range(80):
    mid = 0.5 * (lo + hi)
    if empty_cg(mid) < EMPTY_CG_PUB: lo = mid
    else: hi = mid
K_SHELL = 0.5 * (lo + hi)
items = base_items + shell_items(K_SHELL)
say(f"[mass] shell spread uniformly -> empty CG {CG_UNIFORM*1000:.0f} mm ([N41KP] 729); solved areal "
    f"density ramp k = {K_SHELL:.2f} (tail-end laminate {1+K_SHELL*7.5/LENGTH:.2f} x the nose's)")
say(f"[mass] wings {M_WINGS} kg, tailplane {M_HT}, fin {M_VT}, concentrated {sum(f[1] for f in fixed)}, shell "
    f"{m_shell:.1f} => empty {EMPTY_PUB}")

def mass_props(itms):
    m = sum(i[1] for i in itms)
    cx = sum(i[1] * i[2] for i in itms) / m; cy = sum(i[1] * i[3] for i in itms) / m; cz = sum(i[1] * i[4] for i in itms) / m
    ixx = iyy = izz = ixz = 0.0
    for _, mi, x, y, z, own in itms:
        dx, dy, dz = x - cx, y - cy, z - cz
        ixx += mi * (dy * dy + dz * dz) + own[0]; iyy += mi * (dx * dx + dz * dz) + own[1]
        izz += mi * (dx * dx + dy * dy) + own[2]; ixz += mi * dx * dz
    return m, (cx, cy, cz), {'ixx': ixx, 'iyy': iyy, 'izz': izz, 'ixz': ixz}

M_E, CG_E, I_E = mass_props(items)
say(f"[mass] EMPTY {M_E:.1f} kg, CG {-CG_E[0]*1000:.0f} mm aft of datum, z {CG_E[2]:+.3f}; "
    f"Ixx {I_E['ixx']:.0f} Iyy {I_E['iyy']:.0f} Izz {I_E['izz']:.0f} Ixz {I_E['ixz']:.1f}")
# Check against the placard: [POH] 2.10 "Min. load in the 1st seat 70 kg". The minimum exists to keep a solo
# aeroplane inside the aft limit, so a 70 kg front pilot should land inside 480 mm.
# Seat arms: [N41KP] "Front pilot -44.57 in", "Rear pilot 1.38 in" (1132 mm ahead of / 35 mm behind the datum). The
# [POH] does not give them; the Twin II's [FM-II] V.2 arms were 1150 / 40 mm.
SEAT1 = (44.57 * 0.0254, 0.0, 0.25)
SEAT2 = (-1.38 * 0.0254, 0.0, 0.24)
cg70 = (M_E * CG_E[0] + 70 * SEAT1[0]) / (M_E + 70)
say(f"[mass] check: 70 kg front seat solo -> CG {-cg70*1000:.0f} mm aft of datum (aft limit 480 mm)")

PILOT_SOLO = 90.0       # one pilot with parachute in the FRONT seat ("On solo flights the pilot has to be in the front
                        # seat", [POH] 2.10) -> 489 kg
pilot = lambda st, kg, nm: (nm, kg, st[0], st[1], st[2], (kg * (0.30 ** 2 + 0.15 ** 2) / 1.0 * 0.5,
                                                         kg * (0.6 ** 2 + 0.3 ** 2) / 12, kg * (0.6 ** 2 + 0.4 ** 2) / 12))
default_items = items + [pilot(SEAT1, PILOT_SOLO, 'front pilot')]
M_D, CG_D, I_D = mass_props(default_items)
mac_le = 0.0
def pct_mac(x): return 100 * (-x - mac_le) / MAC_PUB
say(f"[mass] DEFAULT (solo, {PILOT_SOLO:.0f} kg front): {M_D:.1f} kg, CG {-CG_D[0]*1000:.0f} mm aft = "
    f"{pct_mac(CG_D[0]):.1f} % MAC; Ixx {I_D['ixx']:.0f} Iyy {I_D['iyy']:.0f} Izz {I_D['izz']:.0f} Ixz {I_D['ixz']:.1f}")
Rx = 2 * math.sqrt(I_D['ixx'] / M_D) / SPAN; Ry = 2 * math.sqrt(I_D['iyy'] / M_D) / LENGTH
Rz = 2 * math.sqrt(I_D['izz'] / M_D) / ((SPAN + LENGTH) / 2)
say(f"[mass] radii of gyration Rx {Rx:.3f} Ry {Ry:.3f} Rz {Rz:.3f} (protocol bands are for powered light aircraft)")
for nm, ld in (('dual 2x100 kg', [pilot(SEAT1, 100, 'f'), pilot(SEAT2, 100, 'r')]),
               ('solo 70 kg', [pilot(SEAT1, 70, 'f')]), ('front 110 kg solo', [pilot(SEAT1, 110, 'f')]),
               ('dual 110+110 (over MTOM)', [pilot(SEAT1, 110, 'f'), pilot(SEAT2, 110, 'r')])):
    m, cg, ii = mass_props(items + ld)
    say(f"[mass]   {nm}: {m:.0f} kg, CG {-cg[0]*1000:.0f} mm = {pct_mac(cg[0]):.1f} % MAC")

# =====================================================================================================================
# 8. CONTROLS (FAA TCDS G39EU travels -- the TWIN II's: [TCDS] N.III.13 refers the Twin III's to its Maintenance
#    Manual, which is not available, so these are KEPT and flagged; the chord of the arc each is quoted at)
# =====================================================================================================================
def arc(d, r): return 2 * math.asin(d / (2 * r))
AIL_UP, AIL_DN = arc(90, 208), arc(50, 208)      # 90 up / 50 down mm at 208 mm (inboard end)
EL_UP, EL_DN = arc(3.82, 9.45), arc(2.99, 9.45)  # 3.82 in up / 2.99 in down at 9.45 in
RUD = arc(233, 450)                               # +-233 mm at 450 mm
say(f"[controls] aileron {AIL_UP/D2R:.1f} up / {AIL_DN/D2R:.1f} down, elevator {EL_UP/D2R:.1f} up / "
    f"{EL_DN/D2R:.1f} down, rudder +-{RUD/D2R:.1f} deg (chord-of-arc; the rudder's 30.0 and the aileron's 25.0 "
    f"are the round numbers that reading gives)")
# The sim's aileron axis is ONE symmetric travel per side of the stick; a differential aileron (up > down) is the
# mean on each wing: the rolling moment comes from the difference between the two wings, and with 25/13.8 the pair
# acts like +-19.4 deg. GAP: the adverse-yaw benefit of the differential is lost.
AIL_SYM = 0.5 * (AIL_UP + AIL_DN)

# =====================================================================================================================
# 9. AIRBRAKES -- "airbrakes (Type GROB) on the upper side" ([POH] 1.4, 7.4); "airbrakes on upper wing surface"
#    ([TCDS] N.III.2). No flaps. Upper surface only: the [POH] describes no lower-surface blades.
# =====================================================================================================================
BRAKE_H = 0.170         # [TCDS-FAA] G39EU, the TWIN II's 170 mm: no Twin III figure found (KEPT, flagged)
# Local velocity over the upper surface at the brake's x/c, from XFOIL's boundary-layer dump on the E583 (Re 1.5M,
# Ncrit 9, 200 panels, DUMP at a fixed CL), nearest panel x/c 0.434: Ue/V = 1.326 at CL 0.21, 1.398 at CL 0.57, 1.464
# at CL 0.92, 1.531 at CL 1.25. The brakes are used from the dive (CL ~0.2) to the approach (CL ~0.9); the middle of
# that, 1.40 at CL 0.57, so the plate sees 1.96 q. (Run 2026-11-04; the E603's was 1.42.)
UE_RATIO = 1.40

# =====================================================================================================================
# 10. ASSEMBLE
# =====================================================================================================================
tables = {}
# Every XFOIL Reynolds number goes in, as one family: each strip names the table nearest its chord's Re at 25 m/s
# (so anything that reads a strip's table finds a sensible one), and AeroModel flies it at its OWN Re, interpolated in
# log Re between members (AirfoilTableData.ReynoldsFamily).
for re in RES:
    tb = wing_table(re)
    tb['reynoldsFamily'] = 'e583-3d'; tb['reynoldsNumber'] = re
    tables[f'e583-re{re/1e6:.2f}M-3d'] = tb
# The tailplane gets the 2-D section: AeroModel.TailSections rescales any 2-D hStab table to the stab system's
# finite span itself (DATCOM/Helmbold). The fin does not (yet), so its table is pre-scaled here the same way.
tp = polar('fx71l150', 0.6e6)
tables['fx71l150-2d'] = tail_table(0.6e6, 1.0)
tables['fx71l150-fin'] = tail_table(0.8e6, FIN_SCALE)

def r4(v): return round(v, 4)
CG_DEFAULT = [r4(CG_D[0]), 0.0, r4(CG_D[2])]
# EYES: Ty APPROVED the Blanc model's eyes on 2026-11-03 (lane/codex-grob103 AGENT-LOG; docs/models/grob103-own-RIG.md:
# "the approved native glTF eye transforms remain exactly front (-1.15, .16, 0), rear (.04, .22, 0)", the GLB's
# Pilot_Eye_front / Pilot_Eye_rear nodes). Native glTF is (-x, z, y) of this config's frame with z UP, and the model's
# origin is this datum -- but AirframeModels.Place stands the model on THIS config's wheels, whose contacts are 0.69 m
# below the datum against the model's own tyre bottoms at 0.600 m. So the drawn cockpit, and the eyes in it, sit
# MODEL_DROP lower than the model's datum, and the camera has to go where the approved eye is drawn.
# (The 90 mm is a real model/config disagreement, recorded in the AGENT-LOG; neither side is moved to hide it.)
MODEL_TYRE_BOTTOM = 0.600
MODEL_DROP = 0.69 - MODEL_TYRE_BOTTOM
FRONT_EYE = [1.15, 0.0, r4(-(0.16 - MODEL_DROP))]
REAR_EYE = [-0.04, 0.0, r4(-(0.22 - MODEL_DROP))]
cfg = {
    'schemaVersion': 1,
    'id': 'glider-grob103-acro-like',
    'displayName': 'Twin III Acro-like Aerobatic Two-Seater',
    '_source': 'tools/aircraft_builder/build_grob103.py -- docs/aircraft/grob103-acro-DERIVATION.md. Do not hand-edit.',
    'spawnIasMs': round(96 / 3.6, 2),   # [POH] 4.5.4 "recommended normal approach speed is 96 km/h"
    'spinAero': {'rollDueToSideslip': 'monoplane'},
    'mass': {'massKg': round(M_D, 1), 'cg': CG_DEFAULT,
             'inertia': {k: round(v) for k, v in I_D.items()}},
    'surfaces': [
        {'id': 'wing', 'heightAboveBodyAxisM': 0.0, 'oswaldE': round(E_SPAN, 4), 'strips': wing},
        {'id': 'wing-aileron', 'heightAboveBodyAxisM': 0.0, 'oswaldE': round(E_SPAN, 4), 'strips': wing_ail},
        {'id': 'hStab', 'strips': stab},
        {'id': 'elevator', 'strips': elev},
        {'id': 'vStab', 'strips': fin},
        {'id': 'rudder-vstab', 'strips': rud},
    ],
    'airfoilTables': tables,
    'controls': {
        'aileron': {'maxDeflRad': r4(AIL_SYM), 'rateRadPerSec': 3.0, 'expo': 0.3, 'deadZone': 0.05, 'reversible': True,
                    'hingeChAlpha': -0.2, 'hingeChDelta': -0.45, 'centeringSpringKt': 0, 'trimType': 'tab', 'freeTauS': 0.12},
        'elevator': {'maxDeflRad': r4(EL_DN), 'maxDeflNegRad': r4(EL_UP), 'rateRadPerSec': 3.0, 'expo': 0.1,
                     'deadZone': 0.03, 'reversible': True, 'hingeChAlpha': -0.2, 'hingeChDelta': -0.45,
                     'centeringSpringKt': 25, 'trimType': 'spring', 'freeTauS': 0.12},
        'rudder': {'maxDeflRad': r4(RUD), 'rateRadPerSec': 2.5, 'expo': 0.3, 'deadZone': 0.05, 'reversible': True,
                   'hingeChAlpha': -0.2, 'hingeChDelta': -0.45, 'centeringSpringKt': 0, 'trimType': 'tab', 'freeTauS': 0.12},
        'spoiler': {'maxDeflRad': 0.9, 'dragOnly': False, 'axis': 'throttleLever', 'axisMap': 'aftOnly',
                    'wheelBrakeFromSpoiler': 0.9, 'wheelBrakeFullSpoiler': 1.0,
                    'brakeSpanInnerM': BRK_IN, 'brakeSpanOuterM': BRK_OUT, 'brakeChordFraction': round(BRK_XC, 3),
                    'brakeHeightM': BRAKE_H, 'brakeLocalVelocityRatio': UE_RATIO},
    },
    'fuselage': {'cd0Area': r4(CD0_AREA), 'sideForceArea': round(SIDE_A, 2), 'damping': {'p': 0, 'q': 0, 'r': 0},
                 'crossflow': {'bodyRadiusM': round(DEQ / 2, 3), 'planArea': round(PLAN_A, 2),
                               'planCenterX': round(centroid_x(1), 2), 'sideArea': round(SIDE_A, 2),
                               'sideCenterX': round(centroid_x(2), 2), 'cd': 1.2, 'lengthM': LENGTH}},
    'propulsion': None,
    'retractableGear': False,       # [POH] 1.4 "non-retractable tandem landing gear"
    # FIXED tandem gear -- main wheel with disc brake, nose wheel, tail wheel, no retraction ([POH] 1.4 "the
    # non-retractable tandem landing gear"; [TCDS] N.III.2 "fixed tandem landing gear with brake"). The nose wheel is
    # steerable from s/n 34171 ([TCDS]) and is modelled steered (Ty 2026-11-04 16:04 PT): linked to the rudder pedals,
    # so LandingGear.Compute points it at -rudder/maxRudder x NOSE_STEER (right pedal, wheel right). It only steers
    # while it carries load -- an unloaded wheel makes no tyre force at all -- and it scrubs through the same
    # load-scaled cornering and friction circle as every other tyre. NOSE_STEER is ESTIMATED: no steering angle is
    # published for the Twin III, so +-25 deg (inside the nose bay's width at the drawn 260x85 tyre) is a FLAGGED guess.
    'gear': [
        {'pos': [r4(MAIN_X), 0.0, 0.69], 'springN': 60000, 'dampNs': 3000, 'brake': True,
         'isTailwheel': False, 'isSteerable': False, 'maxSteerRad': 0.0, 'tireMu': 0.8, 'corneringStiffnessN': 5000,
         'rollResistN': 40, 'tireRadiusM': 0.20},
        {'pos': [r4(NOSE_X), 0.0, 0.69], 'springN': 40000, 'dampNs': 2500, 'brake': False,
         'isTailwheel': False, 'isSteerable': True, 'maxSteerRad': round(NOSE_STEER, 4), 'tireMu': 0.6, 'corneringStiffnessN': 1500,
         'rollResistN': 30, 'tireRadiusM': 0.13},
        {'pos': [r4(TAIL_X), 0.0, 0.33], 'springN': 15000, 'dampNs': 2000, 'brake': False,
         'isTailwheel': True, 'isSteerable': False, 'maxSteerRad': 0, 'tireMu': 0.5, 'corneringStiffnessN': 800,
         'rollResistN': 30, 'tireRadiusM': 0.105},
        {'pos': [0.0, -HALF + 0.1, round(-(HALF - 0.1) * math.tan(DIHEDRAL) + 0.04, 3)], 'springN': 4000, 'dampNs': 120,
         'brake': False, 'isTailwheel': True, 'isSteerable': False, 'maxSteerRad': 0, 'tireMu': 0.4,
         'corneringStiffnessN': 300, 'rollResistN': 60, 'gearType': 'tip-skid'},
        {'pos': [0.0, HALF - 0.1, round(-(HALF - 0.1) * math.tan(DIHEDRAL) + 0.04, 3)], 'springN': 4000, 'dampNs': 120,
         'brake': False, 'isTailwheel': True, 'isSteerable': False, 'maxSteerRad': 0, 'tireMu': 0.4,
         'corneringStiffnessN': 300, 'rollResistN': 60, 'gearType': 'tip-skid'},
    ],
    'limits': {
        # Acrobatic category ([TCDS] N.I.3), fuselage reinforcement OSB 315-66 installed ([POH] 2.2, 2.9):
        # +6.5/-4.0 at VA 185 km/h, +5.3/-3.0 at VNE 280 km/h, a straight line between.
        'vneMs': round(280 / 3.6, 6), 'gMax': 6.5, 'gMin': -4.0,
        'vaMs': round(185 / 3.6, 6), 'gMaxAtVne': 5.3, 'gMinAtVne': -3.0,
        # [POH] 2.2 / 4.5.7: VNE (IAS) with altitude, OSB 315-66 column -- 280 to 2000 m, 265 to 3000, 240 to 5000,
        # 215 to 7000, 190 to 9000, 165 to 11000 m.
        'vneByAltitude': [[0.0, round(280 / 3.6, 6)], [2000.0, round(280 / 3.6, 6)], [3000.0, round(265 / 3.6, 6)],
                          [5000.0, round(240 / 3.6, 6)], [7000.0, round(215 / 3.6, 6)], [9000.0, round(190 / 3.6, 6)],
                          [11000.0, round(165 / 3.6, 6)]],
        # wing-root bending is n x (weight of the non-lifting parts): [POH] 2.6 420 kg
        'maxNonLiftingMassKg': MAX_NLP, 'liftingMassKg': M_WINGS,
        # brakes out: [POH] 2.9 "Max. manoeuvring load factor with airbrakes extended at VNE n = +3.5" (and 4.5.9 "The
        # pull-out loads with extended airbrakes must not exceed +3.5 g"). The [POH] gives no negative figure: -1.5 is
        # CS 22.345's (the type was certificated to the LFSM, [TCDS] N.II.2) -- KEPT, flagged.
        'airbrakeGMax': 3.5, 'airbrakeGMin': -1.5,
        # No second category: at 18 m the Twin III Acro is certificated Acrobatic only ([TCDS] N.I.3; Utility is the
        # 20 m SB 315-59 wingtips, not modelled) and the [POH] gives one set of load factors. The Twin II's utility
        # envelope does not apply.
        'categories': [],
        '_comment': 'Not wired (the sim has no field for them): VRA 200, VT 185, VW 140 km/h ([POH] 2.2, [TCDS] '
                    'N.III.7). Without OSB 315-66: VNE 250, VA = VRA = VT 170 km/h, no aerobatics ([POH] 2.2A) -- not '
                    'modelled.',
    },
    'wakeBlanketMaxLoss': 0.65,
    'stallDynamics': {'stripSepTau': 0.15, 'stripReattachTau': 0.6, 'wakeGrowTau': 0.25, 'wakeDecayTau': 1.0, 'wakeSpreadDeg': 6.0},
    'verticalBlanketFactor': 0.8,
    # nose hook for aerotow (body x at the nose wheel bay front), weak link at [TCDS] N.III.6 "max. 845 daN" (winch and
    # aerotow alike; [POH] 4.5.1). NOTE: AircraftConfig has no towHook field, so the sim does not read this block (the
    # aerotow's weak link is AeroTow's own); it is kept as the record of the type's figure.
    'towHook': {'pos': [r4(body_x_from_nose(0.55)), 0, 0.45], 'weakLinkN': 8450, 'canRelease': True},
    'loading': {
        'emptyMassKg': round(M_E, 1), 'maxGrossMassKg': MTOM, 'cgForwardLimitM': -0.270, 'cgAftLimitM': -0.480,
        'seats': [
            {'id': 'front', 'label': 'Front seat', 'station': [SEAT1[0], 0.0, SEAT1[2]], 'maxKg': 110.0,
             'referenceKg': PILOT_SOLO, 'eye': FRONT_EYE},
            {'id': 'rear', 'label': 'Rear seat', 'station': [SEAT2[0], 0.0, SEAT2[2]], 'maxKg': 110.0,
             'referenceKg': 0.0, 'eye': REAR_EYE},
        ],
        'baggage': [
            {'id': 'baggage', 'label': 'Baggage', 'station': [r4(-31.89 * 0.0254), 0.0, 0.15], 'maxKg': 10.0,
             'referenceKg': 0.0},
            {'id': 'trim-ballast', 'label': 'Trim weights (front)', 'station': [1.543, 0.0, 0.40], 'maxKg': 11.2,
             'referenceKg': 0.0},
        ],
        '_comment': 'Arms: [N41KP] weighing, front seat 1132 mm in front of the datum, rear 35 mm behind, baggage 810 '
                    'mm behind; datum = wing root leading edge = body x 0 ([POH] 2.7). Max seat loads 110 kg, min '
                    'front 70 kg ([POH] 2.10); baggage 10 kg ([POH] 2.6). Trim weights: up to two of 5.6 kg at 1543 mm '
                    '(one) / 1560 mm (two) in front of the datum ([POH] 6.4). CG range 270-480 mm aft of datum '
                    '([POH] 2.7). No water ballast ([POH] 7.10: removable ballast is the trim weights only). Eye points '
                    'are derived, not published (derivation sec. 7).',
    },
}
os.makedirs(os.path.dirname(OUT), exist_ok=True)
json.dump(cfg, open(OUT, 'w'), indent=1)
say(f"wrote {OUT}: {M_D:.1f} kg, cd0Area {CD0_AREA:.4f}, e {E_SPAN:.4f}, tables {sorted(tables)}")
V_H = S_H * (TAIL_MAC_X - (-0.25 * MAC_PUB)) / (AREA * MAC_PUB)
say(f"[tail] V_h = {abs(V_H):.3f}  (tail MAC qc at x {TAIL_MAC_X:+.2f})")
