"""EXTRA 300L WING SECTIONS: TWO CANDIDATES, BOTH FROM PUBLISHED EPPLER COORDINATES (lane/claude-inertia run 5d,
2026-11-02; docs/aircraft/extra-300-airfoil-DERIVATION.md section 1).

The Extra's own sections are published by NAME only (300L Information Manual sec. 1.3.3: root "MA 15 S", tip "MA 12 S";
symmetrical, zero incidence). No coordinates for them exist in any source found. Both candidates start from the
published Eppler aerobatic-aircraft sections, UIUC Airfoil Coordinates Database via airfoiltools.com, copied verbatim
into data/ (each with a .SOURCE.txt):
  data/e473-il.dat   EPPLER 473, 16.2 % thick at 21.2 % c   -> root
  data/e472-il.dat   EPPLER 472, 12.1 % thick at 17.5 % c   -> tip

CANDIDATE A ("modified Eppler stand-in for the Extra's MA 15 S / MA 12 S"; Ty, run 5d): E473 root and E472 tip, BOTH
  UNSCALED. (Run 5c's 15 % scaling of the root is undone.)
CANDIDATE B (Extra-style "ice cream cone"): a smooth geometric reshape of the same E473 / E472, NOT a published section.
  The algorithm is the lane owner's, ported from ~/Developer/agent-lanes/aero-playground/extra-airfoil-AB/ab_sections.py
  (reshape), on the half-thickness h(x) of the symmetric section:
   1. x-warp  x' = x + c x (1 - x),  c = (xm_new - xm_old) / (xm_old (1 - xm_old)): moves the maximum thickness from
      xm_old to xm_new exactly; LE and TE fixed; near the LE x' ~ (1 + c) x, so the LE radius grows by 1/(1 + c).
   2. aft sag  dy = -A sin^2(pi u),  u = (x' - xm_new)/(1 - xm_new), aft of xm_new only (zero value and slope at both
      ends), A solved by bisection on [0, 0.02] so the surface dips DEPTH below the straight line from the maximum-
      thickness point to the trailing edge (measured on the 161-point cosine grid, as ab_sections.concavity).
   3. rescale the ordinates to the base section's t/c.
  B_SPEC (lane owner): root xm_new 0.170, tip 0.155; DEPTH 0.0030 c both.
  Two implementation differences from ab_sections.py, both so that A and B differ ONLY by the reshape: the base h(x) is
  candidate A's own spline (ab_sections linearly interpolates the 61 published points, which makes a faceted nose), and
  the warped section is evaluated exactly, h(w^-1(x)), instead of by linear interpolation on the grid. Resulting
  numbers against ab_sections.py's: see derivation sec. 1.

STATIONS  The wing is ruled: chord and ABSOLUTE thickness linear in span between the root (1.85 m, E473's t/c) and the tip
  (0.88 m, E472's t/c) at y = 4.00 m: t/c(y) = (T_ROOT*1.85*(1-e) + T_TIP*0.88*e) / c(y), e = y/4. At each strip station
  candidate A is the coordinate blend  h(x) = (1-w) h_root(x) + w h_tip(x)  on a common cosine-spaced x grid, w solved so
  the BLEND's own maximum thickness equals the ruled t/c. Candidate B blends B-root and B-tip with the SAME w (so B's
  station t/c is close to, not exactly, the ruled one; printed).

Interpolation: each published surface is a natural cubic spline of y against u = sqrt(x). A round nose is y ~ a u near
the leading edge, so the spline is smooth there where y(x) is not; the natural end condition y''(u) = 0 is exactly the
circle's (y = sqrt(2 r) u + O(u^3)), and the leading-edge radius is r = (dh/du at 0)^2 / 2.

The stall-type criterion (Gault, NACA TN 3963) depends only on the ordinate at 1.25 % chord; DATCOM's sharpness
parameter is y(6 %) - y(0.15 %). Both are printed by geometry().

API:  half(w, cand)            half-thickness function of the station section with blend weight w (0 root .. 1 tip)
      coords_w(w, cand)        Selig-order coordinates, cosine grid, N_GRID panels a surface
      geometry_w(w, cand)      t/c, x(t max), LE radius, y(1.25 %), DATCOM dy, aft concavity, TE angle
      weight(t), station(y) -> (chord m, ruled t/c, w)
Usage: python3 extra_sections.py            prints root, tip and station tables for A and B
       python3 extra_sections.py --write    also writes data/extra300-sections/{A,B}-{root,st1..st8,tip}.dat
"""
import math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, 'data')
SECTIONS = os.path.join(DATA, 'extra300-sections')
E473_FILE = os.path.join(DATA, 'e473-il.dat')
E472_FILE = os.path.join(DATA, 'e472-il.dat')

C_ROOT, C_TIP, SEMISPAN = 1.85, 0.88, 4.0   # 300L Information Manual sec. 1.3.3
STATION_Y = [0.390, 1.155, 1.875, 2.523, 3.075, 3.508, 3.806, 3.958]
N_GRID = 160                                # cosine-spaced panels per surface
CANDIDATES = ('A', 'B')
B_SPEC = {'root': (0.170, 0.0030), 'tip': (0.155, 0.0030)}   # lane owner, ab_sections.py: (xm_new, aft depth)


def read_selig(path):
    """(name, upper, lower) with each surface a list of (x, y) from the leading edge to the trailing edge."""
    with open(path) as f:
        lines = [ln.split() for ln in f if ln.strip()]
    name = ' '.join(lines[0])
    pts = [(float(a), float(b)) for a, b in lines[1:]]
    i = min(range(len(pts)), key=lambda k: pts[k][0])
    return name, pts[:i + 1][::-1], pts[i:]


class _Spline:
    """Natural cubic spline of y against u = sqrt(x)."""

    def __init__(self, pts):
        self.u = [math.sqrt(x) for x, _y in pts]
        self.y = [y for _x, y in pts]
        n = len(self.u)
        h = [self.u[i + 1] - self.u[i] for i in range(n - 1)]
        # tridiagonal system for the second derivatives M, M[0] = M[n-1] = 0
        a, b, c, d = [0.0] * n, [1.0] * n, [0.0] * n, [0.0] * n
        for i in range(1, n - 1):
            a[i], b[i], c[i] = h[i - 1], 2 * (h[i - 1] + h[i]), h[i]
            d[i] = 6 * ((self.y[i + 1] - self.y[i]) / h[i] - (self.y[i] - self.y[i - 1]) / h[i - 1])
        for i in range(1, n):
            m = a[i] / b[i - 1]
            b[i] -= m * c[i - 1]
            d[i] -= m * d[i - 1]
        M = [0.0] * n
        M[-1] = d[-1] / b[-1]
        for i in range(n - 2, -1, -1):
            M[i] = (d[i] - c[i] * M[i + 1]) / b[i]
        self.h, self.M = h, M

    def _seg(self, u):
        lo, hi = 0, len(self.u) - 2
        while lo < hi:
            mid = (lo + hi + 1) // 2
            if self.u[mid] <= u: lo = mid
            else: hi = mid - 1
        return lo

    def __call__(self, x):
        u = math.sqrt(min(max(x, 0.0), 1.0))
        i = self._seg(u)
        h, M, y = self.h[i], self.M, self.y
        A, B = (self.u[i + 1] - u) / h, (u - self.u[i]) / h
        return A * y[i] + B * y[i + 1] + ((A ** 3 - A) * M[i] + (B ** 3 - B) * M[i + 1]) * h * h / 6

    def slope_le(self):
        """dy/du at u = 0."""
        h, M, y = self.h[0], self.M, self.y
        return (y[1] - y[0]) / h - h * (2 * M[0] + M[1]) / 6


class Half:
    """Half-thickness h(x) of a symmetric section, plus its leading-edge slope a = dh/d(sqrt x) at 0."""

    def __init__(self, f, a):
        self.f, self.a = f, a

    def __call__(self, x): return self.f(x)


def eppler(path):
    _name, up, lo = read_selig(path)
    su, sl = _Spline(up), _Spline(lo)
    return Half(lambda x: 0.5 * (su(x) - sl(x)), 0.5 * (su.slope_le() - sl.slope_le()))


def _tmax(h):
    """(t max, x(t max)) of half-thickness h: a 1000-point scan, then golden-section refinement about the best sample."""
    _tb, xb = max((h(k / 1000), k / 1000) for k in range(1, 1000))
    a, b, g = xb - 1e-3, xb + 1e-3, (math.sqrt(5) - 1) / 2
    for _ in range(40):
        c, d = b - g * (b - a), a + g * (b - a)
        if h(c) > h(d): b = d
        else: a = c
    x = 0.5 * (a + b)
    return 2 * h(x), x


def grid(n=N_GRID):
    return [0.5 * (1.0 - math.cos(math.pi * i / n)) for i in range(n + 1)]


XC = grid(160)   # ab_sections.XC


def concavity(h):
    """ab_sections.concavity: the largest depth of the aft surface below the straight line from the maximum-thickness
    point to the trailing edge, on the 161-point cosine grid (+ = concave)."""
    t, xm = _tmax(h)
    h1 = h(1.0)
    return max(t / 2 * (1 - x) / (1 - xm) + h1 * (x - xm) / (1 - xm) - h(x) for x in XC if x > xm)


def reshape(base, xm_new, depth):
    """ab_sections.reshape, ported: x-warp to move t max to xm_new, aft sag of the given depth, rescale to base t/c.
    Returns (Half, c, A)."""
    t, xm = _tmax(base)
    c = (xm_new - xm) / (xm * (1 - xm))

    def unwarp(x):   # X with X + c X (1 - X) = x
        if abs(c) < 1e-12: return x
        return ((1 + c) - math.sqrt((1 + c) ** 2 - 4 * c * x)) / (2 * c)

    def bump(x):
        if x <= xm_new: return 0.0
        return math.sin(math.pi * min((x - xm_new) / (1 - xm_new), 1.0)) ** 2

    yn = lambda x: base(unwarp(x))
    lo, hi = 0.0, 0.02
    for _ in range(60):
        Am = 0.5 * (lo + hi)
        if concavity(lambda x: yn(x) - Am * bump(x)) < depth: lo = Am
        else: hi = Am
    sag = lambda x: yn(x) - Am * bump(x)
    k = t / _tmax(sag)[0]
    return Half(lambda x: k * sag(x), k * base.a / math.sqrt(1 + c)), c, Am


A_ROOT, A_TIP = eppler(E473_FILE), eppler(E472_FILE)
T_ROOT, XM_ROOT = _tmax(A_ROOT)
T_TIP, XM_TIP = _tmax(A_TIP)
B_ROOT, B_ROOT_C, B_ROOT_A = reshape(A_ROOT, *B_SPEC['root'])
B_TIP, B_TIP_C, B_TIP_A = reshape(A_TIP, *B_SPEC['tip'])
ENDS = {'A': (A_ROOT, A_TIP), 'B': (B_ROOT, B_TIP)}


def half(w, cand='A'):
    r, t = ENDS[cand]
    if w <= 0: return r
    if w >= 1: return t
    return Half(lambda x: (1 - w) * r(x) + w * t(x), (1 - w) * r.a + w * t.a)


_W = {}


def weight(t):
    """Candidate A blend weight (0 = root, 1 = tip) whose section has maximum thickness t; candidate B uses the same."""
    if t in _W: return _W[t]
    if not T_TIP - 1e-6 <= t <= T_ROOT + 1e-6:
        raise ValueError(f't/c {t} outside the wing: tip {T_TIP:.5f} .. root {T_ROOT:.5f}')
    lo, hi = 0.0, 1.0
    for _ in range(40):
        w = 0.5 * (lo + hi)
        if _tmax(half(w))[0] > t: lo = w
        else: hi = w
    _W[t] = 0.5 * (lo + hi)
    return _W[t]


def station(y):
    """(chord m, ruled t/c, blend weight) at span station y m."""
    e = y / SEMISPAN
    c = C_ROOT - (C_ROOT - C_TIP) * e
    t = (T_ROOT * C_ROOT * (1 - e) + T_TIP * C_TIP * e) / c
    return c, t, weight(t)


def coords_w(w, cand='A', n=N_GRID):
    """Selig order (TE -> upper -> LE -> lower -> TE), cosine grid."""
    h = half(w, cand)
    xs = grid(n)
    return [(x, h(x)) for x in reversed(xs)] + [(x, -h(x)) for x in xs[1:]]


def geometry_w(w, cand='A'):
    h = half(w, cand)
    tm, xmax = _tmax(h)
    dx = 1e-4
    te_slope = (h(1.0) - h(1.0 - dx)) / dx
    return {
        'w': w,
        't/c %': 100 * tm,
        'x(tmax) %': 100 * xmax,
        'LE radius %c': 100 * h.a * h.a / 2,
        'y(1.25%) %c': 100 * h(0.0125),
        'DATCOM dy = y(6%)-y(0.15%) %c': 100 * (h(0.06) - h(0.0015)),
        'aft concavity %c': 100 * concavity(h),
        'TE included angle deg': 2 * math.degrees(math.atan(-te_slope)),
    }


def coords(t, xm=None, cand='A'):
    if xm is not None:
        raise ValueError('x(t max) is fixed by the sections; pass xm=None')
    return coords_w(weight(t), cand)


def geometry(t, xm=None, cand='A'):
    if xm is not None:
        raise ValueError('x(t max) is fixed by the sections; pass xm=None')
    return geometry_w(weight(t), cand)


def naca00(t, x):
    return 5 * t * (0.2969 * math.sqrt(x) - 0.1260 * x - 0.3516 * x * x + 0.2843 * x ** 3 - 0.1015 * x ** 4)


def sections():
    """[(label, w)] root, st1..st8, tip."""
    return [('root', 0.0)] + [(f'st{i + 1}', station(y)[2]) for i, y in enumerate(STATION_Y)] + [('tip', 1.0)]


def write_dat(path, name, pts):
    with open(path, 'w') as f:
        f.write(name + '\n')
        for x, y in pts:
            f.write(f'{x:10.6f} {y:10.6f}\n')


if __name__ == '__main__':
    fmt = lambda g: '  '.join(f'{k} {v:.3f}' for k, v in g.items())
    print(f'A root E473 t/c {100 * T_ROOT:.3f} % at {100 * XM_ROOT:.2f} %; A tip E472 t/c {100 * T_TIP:.3f} % at {100 * XM_TIP:.2f} %')
    print(f'B root: warp c {B_ROOT_C:.5f}, sag A {B_ROOT_A:.6f};  B tip: warp c {B_TIP_C:.5f}, sag A {B_TIP_A:.6f}')
    for cand in CANDIDATES:
        for lab, w in sections():
            print(f'{cand} {lab:4s} ' + fmt(geometry_w(w, cand)))
    for t in (0.12, 0.15, 0.16):
        print(f'NACA 00{int(t*100):02d}  LE radius %c {100*1.1019*t*t:.2f}  y(1.25%) %c {100*naca00(t,0.0125):.2f}  '
              f'DATCOM dy %c {100*(naca00(t,0.06)-naca00(t,0.0015)):.2f}')
    if '--write' in sys.argv:
        os.makedirs(SECTIONS, exist_ok=True)
        for cand in CANDIDATES:
            for lab, w in sections():
                g = geometry_w(w, cand)
                write_dat(os.path.join(SECTIONS, f'{cand}-{lab}.dat'),
                          f'EXTRA300 CANDIDATE {cand} {lab} w{w:.4f} t{g["t/c %"]:.2f} xm{g["x(tmax) %"]:.1f}', coords_w(w, cand))
        print('written', SECTIONS)
