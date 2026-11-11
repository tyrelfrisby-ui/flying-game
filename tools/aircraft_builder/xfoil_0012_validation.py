"""XFOIL 6.99 AGAINST LADSON'S NACA 0012 (NASA TM 4074, 1988), lane/claude-inertia run 5e, 2026-11-02.
docs/aircraft/extra-300-airfoil-DERIVATION.md sec. 1e. Reads only committed polars (data/xfoil/v0012_*), so it runs without
XFOIL:   python3 tools/aircraft_builder/xfoil_0012_validation.py

LADSON below is copied BY HAND from TM 4074 (Langley LTPT, M 0.15, chord 0.601 m, model polished with No. 400 paper):
  Table I   free transition;
  Table IX  transition fixed at 0.05 c on both surfaces with No. 80 grit.
For each Reynolds number: the last point before the break (cl_max, alpha), and the first point after it, if tabulated.
Two of them are LOWER BOUNDS on cl_max, and are flagged as such:
  3.94 M free  -- the table jumps from 15.14 deg (1.4844) straight to 18.09 deg (1.0946), so the peak lies in between;
  8.86 M free  -- the run stops at 18.42 deg (1.693) still rising.
Ladson: "the turbulence level of the tunnel was unknown, but there were many indications that it had increased from the
original low level" (p. 1); "fixing transition at the 0.05c location has very little effect on the maximum lift
coefficient" (p. 4).
"""
import math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import extra_xfoil as X  # noqa: E402

XF = os.path.join(HERE, 'data', 'xfoil')

# Re: (alpha at cl_max, cl_max, alpha after, cl after, cl_max is a lower bound)
LADSON_FREE = {
    2.00e6: (15.25, 1.4080, 16.25, 0.7530, False),
    3.94e6: (15.14, 1.4844, 18.09, 1.0946, True),
    5.97e6: (17.35, 1.6600, 18.65, 1.0050, False),
    8.86e6: (18.42, 1.6930, None, None, True),
    11.90e6: (18.04, 1.6174, 18.25, 1.2188, False),
}
LADSON_TRIP = {
    2.00e6: (15.16, 1.3748, 16.02, 1.0732, False),
    3.99e6: (17.26, 1.5692, 18.03, 0.9899, False),
    5.95e6: (17.13, 1.6116, 18.02, 0.9967, False),
}
# Ladson's linear range (Table I, free transition): alpha, cl -- for the lift-slope ratio K
LADSON_LINEAR = {
    2.00e6: [(4.25, 0.4450), (6.05, 0.6250), (8.15, 0.8550), (10.15, 1.0450), (12.10, 1.2180)],
    3.94e6: [(3.96, 0.4203), (6.00, 0.6360), (8.17, 0.8657), (10.02, 1.0629), (12.08, 1.2489)],
    5.97e6: [(4.18, 0.4520), (6.20, 0.6630), (8.22, 0.8800), (10.18, 1.0880), (12.25, 1.2920)],
    8.86e6: [(4.13, 0.4500), (6.08, 0.6650), (8.02, 0.8750), (10.30, 1.1150), (12.22, 1.3050)],
    11.90e6: [(4.17, 0.4575), (6.10, 0.6693), (8.11, 0.8885), (10.40, 1.1289), (12.52, 1.3331)],
}
NCRIT_REF = 9   # the validation's reference XFOIL setting, the same the Extra stations use


def polar(name):
    rows = sorted(X.read_polar(os.path.join(XF, name + '.up.pol')))
    return [r for j, r in enumerate(rows) if j == 0 or r[0] > rows[j - 1][0] + 1e-9]


def interp(rows, a, col=1):
    for r0, r1 in zip(rows, rows[1:]):
        if r0[0] <= a <= r1[0]:
            return r0[col] + (a - r0[0]) / (r1[0] - r0[0]) * (r1[col] - r0[col])
    return None


def sep(name):
    """[(alpha, upper-surface trailing-edge separation x/c, converged)] from the case's .sep.txt."""
    out = []
    for l in open(os.path.join(XF, name + '.sep.txt')):
        if l.startswith('#'): continue
        p = l.split()
        out.append((float(p[0]), float(p[1]), p[-1] == '1'))
    return out


def sep_at(name, a):
    pts = sep(name)
    for (a0, s0, _), (a1, s1, _) in zip(pts, pts[1:]):
        if a0 <= a <= a1: return s0 + (a - a0) / (a1 - a0) * (s1 - s0)
    return None


def peak(rows):
    r = max(rows, key=lambda r: r[1])
    return r[1], r[0]


def case(re, ncrit=None, trip=False):
    return f'v0012_re{re / 1e6:.2f}_m15_' + ('x05' if trip else f'n{ncrit}')


def table():
    """Every validation case: XFOIL peak and its bias against Ladson, and XFOIL's state at Ladson's measured stall."""
    out = []
    for trip, lad in ((False, LADSON_FREE), (True, LADSON_TRIP)):
        for re, (a_l, cl_l, *_r, lower) in lad.items():
            for n in ((9,) if trip else (9, 6, 4, 2)):
                name = case(re, n, trip)
                if not os.path.exists(os.path.join(XF, name + '.up.pol')): continue
                rows = polar(name)
                cl_x, a_x = peak(rows)
                out.append(dict(name=name, re=re, trip=trip, ncrit=n, lad_cl=cl_l, lad_a=a_l, lower=lower, xf_cl=cl_x, xf_a=a_x,
                                d_cl=cl_x - cl_l, d_a=a_x - a_l, xf_cl_at_lad_a=interp(rows, a_l), sep_at_lad_a=sep_at(name, a_l)))
    return out


def k_lift(re):
    """Ladson / XFOIL cl, mean over Ladson's linear points (4-12.5 deg), free transition, NCRIT 9, at Ladson's Re."""
    rows = polar(case(re, NCRIT_REF))
    r = [cl / interp(rows, a) for a, cl in LADSON_LINEAR[re]]
    return sum(r) / len(r)


def complete_cases():
    """Ladson free-transition Reynolds numbers whose cl_max is bracketed (the break is tabulated), not a lower bound."""
    return [re for re, v in LADSON_FREE.items() if not v[4]]


def stall_lateness():
    """XFOIL's peak alpha minus Ladson's measured cl_max alpha, free transition, NCRIT 9, over the complete cases:
    (mean, sample standard deviation, the individual values)."""
    d = [peak(polar(case(re, NCRIT_REF)))[1] - LADSON_FREE[re][0] for re in complete_cases()]
    m = sum(d) / len(d)
    return m, math.sqrt(sum((x - m) ** 2 for x in d) / (len(d) - 1)), d


def corrected_break(rows, k, dalpha):
    """The correction: the section breaks DALPHA earlier than XFOIL's own peak, with K x XFOIL's cl there."""
    cl_x, a_x = peak(rows)
    a_b = a_x - dalpha
    return a_b, k * interp(rows, a_b)


if __name__ == '__main__':
    m, sd, d = stall_lateness()
    print('K (Ladson/XFOIL linear range, NCRIT 9): ' + ', '.join(f'{re / 1e6:.2f}M {k_lift(re):.4f}' for re in LADSON_FREE))
    print(f'XFOIL stall lateness over the complete cases {[f"{re / 1e6:.2f}M" for re in complete_cases()]}: '
          f'{", ".join(f"{x:+.2f}" for x in d)} -> mean {m:.3f} deg, s.d. {sd:.3f} deg')
    print(f"{'Re':>7} {'Ladson':>16} {'XFOIL raw':>16} {'corrected':>16} {'resid cl':>9} {'resid a':>8}")
    for re, (a_l, cl_l, *_r, lower) in LADSON_FREE.items():
        rows = polar(case(re, NCRIT_REF))
        cx, ax = peak(rows)
        ab, cb = corrected_break(rows, k_lift(re), m)
        print(f"{re / 1e6:6.2f}M {cl_l:7.4f}{'+' if lower else ' '}@{a_l:5.2f} {cx:7.3f} @{ax:5.2f}  {cb:7.3f} @{ab:5.2f}  {cb - cl_l:+9.3f} {ab - a_l:+8.2f}"
              + ('   (Ladson is a lower bound)' if lower else ''))
    print('Leave-one-out (DALPHA from the other two complete cases):')
    for re, x in zip(complete_cases(), d):
        dl = (sum(d) - x) / (len(d) - 1)
        rows = polar(case(re, NCRIT_REF))
        ab, cb = corrected_break(rows, k_lift(re), dl)
        print(f'  {re / 1e6:5.2f}M  DALPHA {dl:.3f}  corrected {cb:.3f} @ {ab:.2f}  resid cl {cb - LADSON_FREE[re][1]:+.3f}, a {ab - LADSON_FREE[re][0]:+.2f}')
    print()
    print(f"{'case':24} {'Ladson cl_max @ a':>20} {'XFOIL cl_max @ a':>18} {'d cl':>7} {'d a':>6} {'XFOIL cl @ Ladson a':>20} {'TE sep there':>12}")
    for r in table():
        print(f"{r['name']:24} {r['lad_cl']:8.4f}{'+' if r['lower'] else ' '} @ {r['lad_a']:5.2f}      {r['xf_cl']:6.3f} @ {r['xf_a']:5.2f}  "
              f"{r['d_cl']:+7.3f} {r['d_a']:+6.2f}   {r['xf_cl_at_lad_a'] or float('nan'):10.3f}        {r['sep_at_lad_a'] if r['sep_at_lad_a'] is not None else float('nan'):6.3f}")
