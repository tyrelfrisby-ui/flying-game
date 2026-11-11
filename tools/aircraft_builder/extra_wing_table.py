"""THE EXTRA 300L WING'S SECTION TABLES, ONE PER STRIP STATION (lane/claude-inertia run 4, 2026-11-02; candidates A/B run 5d).
Every number and its source: docs/aircraft/extra-300-airfoil-DERIVATION.md. Reads only files committed under
tools/aircraft_builder/data/xfoil/ (made by extra_xfoil.py), so it runs without XFOIL:
    python3 tools/aircraft_builder/extra_wing_table.py            prints the derivation tables
    python3 tools/aircraft_builder/extra_wing_table.py --write    also writes configs/aircraft/extra-300-like.json
        (and its StreamingAssets mirror IF that mirror exists on disk -- since 2026-11-10 it is generated and
        correctly untracked, so in a clean checkout there is nothing there to write and the mirror is skipped)
    python3 tools/aircraft_builder/extra_wing_table.py --cand A [--raw]   the other candidate / XFOIL's decline everywhere

Steps, per strip station y (the config's eight wing strip centres; the aileron strip at the same y flies the same table):
 1. 2-D section, attached: XFOIL 6.99 (NCRIT 9, free transition, the station's Re at the 1-g stall, M 0.083) on the
    candidate section (extra_sections.py; WIRED below), its cl multiplied by K_CL(Re): the measured/XFOIL ratio for the NACA 0012
    in the linear range, Ladson NASA TM 4074 Table I (M 0.15, free transition) against XFOIL at the same Re and Mach:
    0.929 at Re 2.0 M, 0.944 at 5.97 M (log-interpolated; ~7 % slope over-prediction, which Ladson reports for theory).
 2. Stall type: Gault, NACA TN 3963 (1957) Fig. 1, from the upper-surface ordinate at 1.25 % chord and Re (boundaries
    read off the figure below, +-0.05 % c).
      LE-type (leading-edge, or the band where leading-edge and combined stalls both occur): the section breaks while its
        trailing edge is still nearly all attached. WHEN: the alpha at which XFOIL's upper-surface trailing-edge
        separation has reached the extent it had at the measured 0012 breaks (Ladson 2.0 M: peak 1.408 at 15.25 deg,
        0.753 at 16.25 deg; 5.97 M: 1.660 at 17.35 deg, 1.005 at 18.65 deg). XFOIL cannot burst the leading-edge bubble
        itself (its own 0012 peaks 1.2 deg late and falls only 22 % in 3.5 deg against the tunnel's 47 % in 1 deg), so the
        tunnel says when, XFOIL says how far the trailing-edge separation had got by then. HOW FAR it falls: to the
        measured post-break fraction of the peak, 1 deg later (0.535 at 2.0 M, 0.605 at 5.97 M); cd/cl there 0.21 and
        cm -0.108 (Ladson's first post-break points at 3.94, 5.97, 11.9, 18.9 M: cd/cl 0.208, -, 0.210, 0.245; cm
        -0.080, -0.108, -0.134, -0.111, and -0.106 at 2.0 M).
      TE-type (trailing-edge only, or trailing-edge-and-combined): XFOIL's own rounded peak and decline (times K_CL).
 3. Finite wing: each station flies its section at the geometric angle less its own lifting-line induced angle,
    alpha_3D = alpha_2D + cl (1/a_y - 1/a0), a_y = CLalpha * eta(y) the station's lift per radian of wing alpha (eta from a
    Glauert lifting line of the published planform, 1.85 -> 0.88 m over 8.00 m, untwisted; CLalpha from DATCOM's
    2 pi A / (2 + sqrt(A^2/kappa^2 + 4)), the same formula AeroModel uses for the tails). Where the section stops
    lifting the induced angle goes to zero -- the same mapping as AeroModel's tail (TailInducedAngleMapping). A section
    drop steeper than 1/kInd folds the map: the finite wing's drop then happens at one alpha (a cliff).
 4. Past the drop: blended linearly over 10 deg into the run-1 flat plate (poststall_plate.py) -- the previous extra-sym
    table's own rows from there to +-180, which are that plate and the reversed-flow data.
"""
import json, math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import extra_sections as S  # noqa: E402
import extra_xfoil as X  # noqa: E402
import xfoil_0012_validation as V  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
XF = os.path.join(HERE, 'data', 'xfoil')
CFG = os.path.join(REPO, 'configs', 'aircraft', 'extra-300-like.json')
MIRROR = os.path.join(REPO, 'unity', 'Assets', 'StreamingAssets', 'aircraft', 'extra-300-like.json')
D2R = math.pi / 180

# --- Ladson, NASA TM 4074 (1988), NACA 0012, M 0.15, free transition, Table I (copied by hand) -----------------------
LADSON_BREAK = {2.0e6: (15.25, 1.408, 16.25, 0.753), 5.97e6: (17.35, 1.660, 18.65, 1.005)}
K_CL = {2.0e6: 0.929, 5.97e6: 0.944}            # run 4: measured / XFOIL cl, linear range (derivation sec. 3)
# Run 5e (derivation sec. 1e): the same ratio at all five of Ladson's free-transition Reynolds numbers, NCRIT 9, and the
# stall correction: XFOIL's peak comes DALPHA_STALL late against Ladson's measured 0012 (mean over the three Reynolds
# numbers whose break is tabulated). Both computed from the committed v0012 polars and Ladson's hand-copied tables.
K_CL_5E = {re: V.k_lift(re) for re in V.LADSON_FREE}
DALPHA_STALL = V.stall_lateness()[0]
POST_CD_OVER_CL, POST_CM = 0.21, -0.108

# --- method 'measured' (WIRED 2026-11-10): Ty's decision -------------------------------------------------------------
# Ty, 2026-11-10, on the run-5e NEEDS YOU (the corrected section carried 0.15-0.30 more cl_max than any MEASURED
# symmetric section of its thickness at its Reynolds number): "scale the cl_max to the measured data ... XFOIL's
# optimism on the ice-cream-cone shape is not evidence". So the section's MAXIMUM LIFT is no longer XFOIL's at all. It
# is Ladson's measured 0012 cl_max at the station's own Reynolds number, and the break is taken WHERE THE CORRECTED
# CURVE REACHES THAT VALUE -- which keeps the lift SLOPE measured too (that is what K(Re) already does) and lets the
# stall ANGLE follow from the two of them instead of being asserted.
#
# Only the three Reynolds numbers whose cl_max Ladson actually brackets are used. At 3.94 M and 8.86 M his run stopped
# while still rising, so those rows are LOWER BOUNDS (LADSON_FREE's fifth field) and an interpolation through them
# would be an interpolation through "at least". Below 2.00 M the 2.00 M value is held: st7 and st8 sit at 1.80 and
# 1.72 M, off the bottom of the table, and extrapolating cl_max down a Reynolds number is exactly the kind of guess
# this change exists to remove.
#
# WHAT THIS ASSUMES, stated so it can be argued with: that the 16.2 %-thick ice-cream-cone section cannot carry more
# maximum lift than a measured 12 % NACA 0012 at the same Reynolds number. That is a judgement, not a measurement --
# a thicker section at these Reynolds numbers usually carries somewhat MORE -- so this is the conservative end of the
# honest range, and it is the end Ty chose. The alternative, believing XFOIL, is the end run 5e sat at.
#
# WHY THE 0012 VALIDATION COULD NOT HAVE FOUND THIS. On the 0012 itself the slope correction alone already lands the
# peak: K x XFOIL's peak is 1.410 against Ladson's 1.408 at 2.00 M and 1.653 against 1.660 at 5.97 M. So on the only
# section with tunnel data the two methods agree to 0.01 and this one has nothing to say. The 0.15-0.30 excess is
# therefore a statement XFOIL makes about THIS SHAPE and nothing validates it. Printed by `--method r5e` for comparison.
LADSON_CLMAX = {re: v[1] for re, v in V.LADSON_FREE.items() if not v[4]}      # firm values only: 2.00, 5.97, 11.90 M
# Gault's band where trailing-edge AND combined stalls both occur: the COMBINED stall (trailing-edge separation creeps
# forward, then the rest of the upper surface lets go). How far it creeps first is not in any source found; 0.15 c is
# Ty's own figure ("about 85 % of the upper surface separates at once"), used as stated. XFOIL's peak, the fully
# progressive limit, sits at 0.31-0.36 c on these sections; the Ladson leading-edge breaks at 0.07-0.10 c.
SEP_COMBINED = 0.15

# --- Gault, NACA TN 3963 Fig. 1, read off the figure: Re -> (thin|LE, LE|LE+comb, LE+comb|TE+comb, TE+comb|TE) -------
GAULT = [(1.0e6, (1.62, 1.66, 1.95, 2.20)), (1.5e6, (1.42, 1.66, 1.93, 2.28)), (2.0e6, (1.33, 1.60, 1.92, 2.25)),
         (3.0e6, (1.20, 1.52, 1.86, 2.22)), (6.0e6, (1.00, 1.32, 1.68, 2.12)), (9.0e6, (0.92, 1.20, 1.57, 2.06))]
TYPES = ('thin-airfoil', 'leading-edge', 'leading-edge / combined', 'trailing-edge / combined', 'trailing-edge')

# --- planform (Extra 300L Information Manual sec. 1.3.3; EASA TCDS A.362) -------------------------------------------
SPAN, C_ROOT, C_TIP = 8.00, 1.85, 0.88
ESA_AREA = 10.84
PLATE_BLEND_DEG = 10.0
# The candidate wired into the config: B, the "ice cream cone" reshape at full thickness (run 5d: XFOIL gives it the
# sharper stall). Run 5e's thinned candidate C was an experiment, rejected (sec. 1d).
WIRED = 'B'
# The method wired into the config: 'measured' (Ty, 2026-11-10), cl_max from Ladson's tunnel 0012 at the station's
# Reynolds number. 'r5e' is run 5e's -- XFOIL's own cl_max, the break 1.287 deg before its peak -- kept reproducible
# with `--method r5e` because it is what the derivation document was written against.
WIRED_METHOD = 'measured'


def logi(table, re):
    ks = sorted(table)
    if re <= ks[0]: return table[ks[0]]
    if re >= ks[-1]: return table[ks[-1]]
    for a, b in zip(ks, ks[1:]):
        if a <= re <= b:
            t = math.log(re / a) / math.log(b / a)
            va, vb = table[a], table[b]
            return tuple(x + t * (y - x) for x, y in zip(va, vb)) if isinstance(va, tuple) else va + t * (vb - va)


def gault(y125, re):
    bounds = logi({r: b for r, b in GAULT}, re)
    k = sum(1 for b in bounds if y125 > b)
    return TYPES[k], bounds


def polar(name):
    """XFOIL's up-sweep polar, converged points only, ascending alpha."""
    rows = []
    for l in open(os.path.join(XF, name + '.up.pol')).readlines()[12:]:
        p = l.split()
        if len(p) >= 5: rows.append(tuple(map(float, p[:5])))   # alpha cl cd cdp cm
    rows.sort()
    return [r for j, r in enumerate(rows) if j == 0 or r[0] > rows[j - 1][0] + 1e-9]


def interp(rows, a, col):
    for r0, r1 in zip(rows, rows[1:]):
        if r0[0] <= a <= r1[0]:
            t = (a - r0[0]) / (r1[0] - r0[0])
            return r0[col] + t * (r1[col] - r0[col])
    return rows[-1][col] if a > rows[-1][0] else rows[0][col]


def te_sep(name, a):
    """Upper-surface trailing-edge separation extent (fraction of chord with Cf < 0 from the TE) at alpha a, linearly
    interpolated between the XFOIL boundary-layer dumps (summarised in <case>.sep.txt by extra_xfoil.py)."""
    pts = [tuple(map(float, l.split()[:2])) for l in open(os.path.join(XF, name + '.sep.txt')) if not l.startswith('#')]
    if a <= pts[0][0]: return pts[0][1]
    for (a0, s0), (a1, s1) in zip(pts, pts[1:]):
        if a0 <= a <= a1: return s0 + (a - a0) / (a1 - a0) * (s1 - s0)
    return pts[-1][1]


def alpha_for_sep(name, target, a_lo=8.0, a_hi=24.0):
    if te_sep(name, a_hi) < target: return None
    for _ in range(50):
        m = 0.5 * (a_lo + a_hi)
        if te_sep(name, m) >= target: a_hi = m
        else: a_lo = m
    return a_hi


def ladson_sep_at_break():
    out = {}
    for re, (ab, *_r) in LADSON_BREAK.items():
        name = 'naca0012_re2.0_n9_m15' if re < 3e6 else 'naca0012_re5.97_n9_m15'
        out[re] = te_sep(name, ab)
    return out


def llt_eta(n=60):
    """Glauert lifting line, untwisted straight-tapered wing: eta(y) = cl(y)/CL and the LLT CLalpha."""
    s = SPAN / 2
    chord = lambda y: C_ROOT - (C_ROOT - C_TIP) * abs(y) / s
    a0 = 2 * math.pi
    th = [(i + 0.5) * math.pi / (2 * n) for i in range(n)]
    nn = [2 * k + 1 for k in range(n)]
    M = []
    for t in th:
        mu = chord(s * math.cos(t)) * a0 / (4 * SPAN)
        M.append([math.sin(k * t) * (1 + mu * k / math.sin(t)) for k in nn] + [mu])
    for k in range(n):
        p = max(range(k, n), key=lambda q: abs(M[q][k])); M[k], M[p] = M[p], M[k]
        for q in range(n):
            if q != k:
                f = M[q][k] / M[k][k]
                for z in range(k, n + 1): M[q][z] -= f * M[k][z]
    A = [M[k][n] / M[k][k] for k in range(n)]
    area = (C_ROOT + C_TIP) * s
    ar = SPAN ** 2 / area
    cla = math.pi * ar * A[0]
    def eta(y):
        t = math.acos(min(1.0, abs(y) / s))
        g = sum(a * math.sin(k * t) for k, a in zip(nn, A))
        return 4 * SPAN * g / chord(y) / cla
    return eta, ar, cla


def datcom_cla(ar, a0):
    kappa = a0 / (2 * math.pi)
    return 2 * math.pi * ar / (2 + math.sqrt(ar * ar / (kappa * kappa) + 4))


def case_name(i, y, cand):
    return f'{cand}_st{i + 1}_y{y:.3f}'


def section_2d(i, y, cand=WIRED, raw=False, method=WIRED_METHOD):
    """The station's 2-D section: list of (alpha_deg, cl, cd, cm) for alpha >= 0, attached through the drop, and its
    derivation record.
    method 'measured' (WIRED, Ty 2026-11-10; derivation sec. 1f): cl_max is LADSON'S MEASURED 0012 cl_max at the
      station's Reynolds number, and the break is where the K-corrected curve reaches it, so slope and peak are both
      measured and the stall angle follows. The decline is then XFOIL's own, moved that much earlier.
    method 'r5e' (run 5e, derivation sec. 1e): every station breaks DALPHA_STALL before XFOIL's own peak, at K x
      XFOIL's cl there (the correction validated on Ladson's 0012). After the break a trailing-edge station follows
      XFOIL's own decline moved DALPHA_STALL earlier (continuous at the break); any other Gault type falls to Ladson's
      measured post-break fraction one degree later (run 4).
    method 'run4': run 4's rules (XFOIL's own peak for trailing-edge stations, the separation-extent breaks otherwise).
    raw=True: XFOIL's own peak and decline (x K) at every station -- no correction, no break model."""
    name = case_name(i, y, cand)
    sp = X.CASES[name]
    w, re = sp['w'], sp['re']
    rows = polar(name)
    k = logi(K_CL_5E if method in ('r5e', 'measured') else K_CL, re)
    g = S.geometry_w(w, cand)
    t = g['t/c %'] / 100
    typ, bounds = gault(g['y(1.25%) %c'], re)
    # section slope for the induced-angle map: the 0-10 deg secant (XFOIL's slope at 0-4 deg is depressed by a laminar
    # run that moves forward at 7-8 deg; Ladson's measured 0012 secant to 10 deg is 5.90/rad at Re 2.0 M)
    a0 = k * interp(rows, 10.0, 1) / (10 * D2R)
    rec = dict(name=name, y=y, t=t, re=re, k=k, y125=g['y(1.25%) %c'], le_r=g['LE radius %c'], type=typ, bounds=bounds, a0=a0)
    pts = []
    xf_peak = max(rows, key=lambda r: r[1])
    rec['xfoil_clmax'] = (xf_peak[1], xf_peak[0])
    if method in ('r5e', 'measured') and not raw:
        ap, cpx = xf_peak[0], xf_peak[1]
        if method == 'measured':
            # cl_max IS the measured value; the break is where the K-corrected curve reaches it (bisection on the
            # monotone rising branch below XFOIL's peak). DALPHA_STALL is then an OUTPUT, not an input: it is
            # whatever angle the two measured quantities imply, and it is printed so it can be compared with run 5e's
            # 1.287 deg. If the corrected curve never reaches the measured cl_max (it does not on the 0012 itself,
            # which is the point) the break stays at the peak and the station is left at K x XFOIL -- the method is
            # then saying nothing, which is better than scaling the whole polar to force it.
            target = logi(LADSON_CLMAX, re)
            rec['clmax_measured_target'] = target
            if k * cpx <= target:
                ab = ap
            else:
                lo, hi = 0.0, ap
                for _ in range(80):
                    mid = 0.5 * (lo + hi)
                    if k * interp(rows, mid, 1) < target: lo = mid
                    else: hi = mid
                ab = 0.5 * (lo + hi)
        else:
            ab = ap - DALPHA_STALL
        clb = k * interp(rows, ab, 1)
        off = cpx - interp(rows, ab, 1)          # XFOIL's own rise from the corrected break to its late peak
        rec.update(alpha_break=ab, clmax=clb, sep_at_break=te_sep(name, ab), dalpha=ap - ab)
        rec['attached_at_break'] = 1 - rec['sep_at_break']
        a = 0.0
        while a < ab - 1e-9:
            pts.append((a, k * interp(rows, a, 1), interp(rows, a, 2), interp(rows, a, 4)))
            a += 1.0
        pts.append((ab, clb, interp(rows, ab, 2), interp(rows, ab, 4)))
        if typ == 'trailing-edge':
            last = max(r[0] for r in rows)
            for j in range(1, 13):
                src = ap + 0.5 * j
                if src > last: break
                pts.append((ab + 0.5 * j, k * (interp(rows, src, 1) - off), interp(rows, src, 2), interp(rows, src, 4)))
            rec['post_frac'] = None
        else:
            frac = logi({r: v[3] / v[1] for r, v in LADSON_BREAK.items()}, re)
            clp = frac * clb
            pts.append((ab + 1.0, clp, POST_CD_OVER_CL * clp, POST_CM))
            rec['post_frac'] = frac
        rec['drop_end'] = pts[-1][0]
    elif typ != 'trailing-edge' and not raw:
        sep_b = logi(ladson_sep_at_break(), re) if typ != 'trailing-edge / combined' else SEP_COMBINED
        ab = alpha_for_sep(name, sep_b)
        clb = k * interp(rows, ab, 1)
        frac = logi({r: v[3] / v[1] for r, v in LADSON_BREAK.items()}, re)
        rec.update(sep_at_break=sep_b, alpha_break=ab, clmax=clb, post_frac=frac, attached_at_break=1 - sep_b)
        a = 0.0
        while a < ab - 1e-9:
            pts.append((a, k * interp(rows, a, 1), interp(rows, a, 2), interp(rows, a, 4)))
            a += 1.0
        pts.append((ab, clb, interp(rows, ab, 2), interp(rows, ab, 4)))
        clp = frac * clb
        pts.append((ab + 1.0, clp, POST_CD_OVER_CL * clp, POST_CM))
        rec['drop_end'] = ab + 1.0
    else:
        ap = xf_peak[0]
        rec.update(alpha_break=ap, clmax=k * xf_peak[1], sep_at_break=te_sep(name, ap), post_frac=None)
        rec['attached_at_break'] = 1 - rec['sep_at_break']
        a = 0.0
        last = max(r[0] for r in rows)
        grid = sorted(set([float(x) for x in range(0, int(min(last, ap + 6.0)) + 1)] + [ap]))
        for a in grid:
            pts.append((a, k * interp(rows, a, 1), interp(rows, a, 2), interp(rows, a, 4)))
        rec['drop_end'] = pts[-1][0]
    return pts, rec


def old_table():
    return json.load(open(os.path.join(HERE, 'data', 'extra-sym-f8dc538.json')))


def plate_from_old(a_deg):
    t = old_table()
    aa = t['alphaRad']; a = a_deg * D2R
    for j in range(len(aa) - 1):
        if aa[j] <= a <= aa[j + 1]:
            u = (a - aa[j]) / (aa[j + 1] - aa[j])
            return tuple(t[c][j] + u * (t[c][j + 1] - t[c][j]) for c in ('cl', 'cd', 'cm'))
    raise ValueError(a_deg)


def station_table(i, y, eta, cla, cand=WIRED, raw=False, method=WIRED_METHOD):
    pts, rec = section_2d(i, y, cand, raw, method)
    a_y = cla * eta(y)
    kind = 1.0 / a_y - 1.0 / rec['a0']
    rec.update(a_y=a_y, eta=eta(y))
    out = []
    for a2, cl, cd, cm in pts:
        a3 = a2 * D2R + cl * kind
        if out and a3 <= out[-1][0]: a3 = out[-1][0] + 1e-3    # a drop steeper than 1/kInd: a cliff at one alpha
        out.append((a3, cl, cd, cm))
    ib = max(range(len(pts)), key=lambda j: pts[j][1])
    rec['alpha3d_break'] = out[ib][0] / D2R
    rec['alpha3d_drop_end'] = out[-1][0] / D2R
    a_end = rec['alpha3d_drop_end'] + PLATE_BLEND_DEG
    a_s, cl_s, cd_s, cm_s = out[-1]
    grid = [d for d in range(int(math.floor(rec['alpha3d_drop_end'])) + 1, int(math.ceil(a_end)))]
    for d in grid:
        t = (d - rec['alpha3d_drop_end']) / PLATE_BLEND_DEG
        clp, cdp, cmp_ = plate_from_old(d)
        out.append((d * D2R, (1 - t) * cl_s + t * clp, (1 - t) * cd_s + t * cdp, (1 - t) * cm_s + t * cmp_))
    old = old_table()
    for a, cl, cd, cm in zip(old['alphaRad'], old['cl'], old['cd'], old['cm']):
        if a >= a_end * D2R - 1e-9: out.append((a, cl, cd, cm))
    out = [p for p in out if p[0] >= 0]
    # symmetric section: mirror (cl, cm odd; cd even). The +-180 deg ends come from the old table rows.
    neg = [(-a, -cl, cd, -cm) for a, cl, cd, cm in reversed(out) if a > 0]
    full = neg + out
    tab = {'alphaRad': [round(p[0], 5) for p in full], 'cl': [round(p[1], 4) for p in full],
           'cd': [round(p[2], 5) for p in full], 'cm': [round(p[3], 4) for p in full]}
    # enforce strictly ascending after rounding
    for j in range(1, len(tab['alphaRad'])):
        if tab['alphaRad'][j] <= tab['alphaRad'][j - 1]: tab['alphaRad'][j] = round(tab['alphaRad'][j - 1] + 1e-5, 5)
    return tab, rec


def build(cand=WIRED, raw=False, method=WIRED_METHOD):
    eta, ar_llt, cla_llt = llt_eta()
    recs, tabs = [], {}
    # wing CLalpha: DATCOM with the mean section slope
    a0s = [section_2d(i, y, cand, raw, method)[1]['a0'] for i, y in enumerate(X.STATION_Y)]
    ar = SPAN ** 2 / ESA_AREA
    cla = datcom_cla(ar, sum(a0s) / len(a0s))
    for i, y in enumerate(X.STATION_Y):
        tab, rec = station_table(i, y, eta, cla, cand, raw, method)
        tabs[f'extra-ma-s{i + 1}'] = tab
        recs.append(rec)
    return tabs, recs, dict(ar=ar, cla=cla, ar_llt=ar_llt, cla_llt_2pi=cla_llt, a0_mean=sum(a0s) / len(a0s))


def report(tabs, recs, w):
    print(f"wing: A = {SPAN}^2/{ESA_AREA} = {w['ar']:.3f}; mean section slope a0 {w['a0_mean']:.3f}/rad -> DATCOM CLalpha "
          f"{w['cla']:.3f}/rad (LLT at a0 = 2pi: {w['cla_llt_2pi']:.3f})")
    print(f"Ladson 0012 breaks: XFOIL TE separation at the measured break alpha = "
          + ', '.join(f'{re / 1e6:.2f}M: {v:.3f} c' for re, v in ladson_sep_at_break().items()))
    print(f"{'stn':4} {'y':>5} {'t/c':>6} {'Re':>6} {'LEr%':>5} {'y125':>5} {'type':26} {'K':>5} {'a0':>5} {'eta':>5} {'a_y':>5} "
          f"{'xf clmax':>12} {'target':>6} {'clmax':>6} {'dalpha':>6} {'a2D':>5} {'a3D':>5} {'att.':>5} {'post':>5}")
    for i, r in enumerate(recs):
        print(f"s{i + 1:<3} {r['y']:5.3f} {r['t']:6.4f} {r['re'] / 1e6:5.2f}M {r['le_r']:5.2f} {r['y125']:5.2f} {r['type']:26} "
              f"{r['k']:5.3f} {r['a0']:5.2f} {r['eta']:5.3f} {r['a_y']:5.2f} {r['xfoil_clmax'][0]:6.3f}@{r['xfoil_clmax'][1]:4.1f} "
              f"{r.get('clmax_measured_target', float('nan')):6.3f} "
              f"{r['clmax']:6.3f} {r.get('dalpha', float('nan')):6.3f} {r['alpha_break']:5.1f} {r['alpha3d_break']:5.1f} "
              f"{r['attached_at_break']:5.2f} {(r['post_frac'] or 0):5.2f}")


def write(tabs):
    # THE MIRROR IS WRITTEN ONLY IF IT IS ALREADY THERE. unity/Assets/StreamingAssets/aircraft/ is generated and, as
    # of 2026-11-10, correctly UNTRACKED and gitignored; a clean checkout has no mirror at all. Creating one here would
    # manufacture an ignored file that an unwary `git add -f` could then put back into the repository -- the exact trap
    # this repo has been bitten by before. configs/aircraft/ is the source of truth; the mirror is a build product.
    for path in (CFG, MIRROR):
        if path is MIRROR and not os.path.exists(path):
            print('skipped (generated, untracked, absent):', path)
            continue
        d = json.load(open(path))
        t = d['airfoilTables']
        t.pop('extra-sym', None)
        for k in [k for k in t if k.startswith('extra-ma-s')]: t.pop(k)
        t.update(tabs)
        for sf in d['surfaces']:
            if sf['id'] in ('wing', 'wing-aileron'):
                for st in sf['strips']:
                    y = abs(st['pos'][1])
                    i = min(range(len(X.STATION_Y)), key=lambda j: abs(X.STATION_Y[j] - y))
                    st['airfoil'] = f'extra-ma-s{i + 1}'
        d['_wingSectionNote'] = ("Wing sections extra-ma-s1..s8 (root to tip), one per strip station. Geometry and XFOIL runs "
                                 "derived 2026-11-02 on lane/claude-inertia run 5e; MAXIMUM LIFT SCALED TO MEASURED DATA "
                                 "2026-11-10 by Ty's decision (docs/aircraft/extra-300-airfoil-DERIVATION.md sec. 1f; "
                                 "tools/aircraft_builder/extra_wing_table.py, method 'measured'). CANDIDATE B: the lane owner's "
                                 "'ice cream cone' geometric reshape of the published Eppler E473 root / E472 tip (UIUC Airfoil "
                                 "Coordinates Database) at full thickness, 16.2 / 12.1 %, standing in for the unpublished "
                                 "MA 15 S / MA 12 S (Information Manual 1.3.3); NOT a published section. XFOIL 6.99 at each "
                                 "station's 1-g-stall Re, NCRIT 9, gives the shape of the curve and the post-break decline; the "
                                 "lift SLOPE is XFOIL x K(Re), K being Ladson's measured/XFOIL ratio for the NACA 0012 over his "
                                 "linear range (NASA TM 4074); and cl_max IS LADSON'S MEASURED 0012 cl_max at the station's own "
                                 "Reynolds number, with the break taken where the corrected curve reaches it, so the stall ANGLE "
                                 "follows from two measured quantities instead of from XFOIL's own peak. Ty 2026-11-10: 'scale "
                                 "the cl_max to the measured data ... XFOIL's optimism on the ice-cream-cone shape is not "
                                 "evidence.' Gault TN 3963 types every station trailing-edge, so XFOIL's own decline follows the "
                                 "break; each station carried to the finite wing by its own lifting-line induced angle. Run 5e's "
                                 "XFOIL-peak version is reproducible with --method r5e.")
        sd = d.setdefault('stallDynamics', {})
        sd['separationOnPastStallDeg'] = 0.0
        sd['reattachBelowStallDeg'] = 5.0
        sd['_separationBandNote'] = ("docs/aircraft/extra-300-airfoil-DERIVATION.md: the strip separation memory switches at "
                                     "each strip's OWN static stall (these tables put it near 17-21 deg) instead of the fleet's "
                                     "fixed 16/11 deg, whose numbers are a calibration against ~14 deg feel tables; "
                                     "reattachment keeps the legacy 5 deg band, which is inherited and not derived.")
        with open(path, 'w') as f:
            json.dump(d, f, indent=2)
            f.write('\n')


if __name__ == '__main__':
    cand = WIRED
    method = WIRED_METHOD
    if '--cand' in sys.argv: cand = sys.argv[sys.argv.index('--cand') + 1]
    if '--method' in sys.argv: method = sys.argv[sys.argv.index('--method') + 1]
    tabs, recs, w = build(cand, raw='--raw' in sys.argv, method=method)
    report(tabs, recs, w)
    if '--write' in sys.argv and (cand != WIRED or method != WIRED_METHOD or '--raw' in sys.argv):
        sys.exit('--write writes only the WIRED candidate and method through the Gault pipeline')
    if '--write' in sys.argv: write(tabs); print('written', CFG)
