"""Smooth, skinned, normal-mapped pilot body for the Quest — original geometry from numbers only.

Replaces the earlier flat-shaded rigid tubes (49 separate meshes, one draw each) with ONE skinned mesh,
five materials, smooth normals, tangents and procedural tangent-space normal maps (suit twill, glove
leather, boot grain, harness webbing). Limbs are continuous lofts across the elbow / knee / wrist with
linear-blend weights, so a bent joint does not open a gap. ~7k triangles (Quest target < 15k).

Frame: the aircraft builder's (x nose, y left, z up); vr_cockpit.gl converts to glTF on write.
No third-party mesh, texture, scan, rig or training-derived raster: every texel is a formula below.
"""
import io, math
import numpy as np
from PIL import Image

def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)

def unit(v):
    v = np.asarray(v, float); n = np.linalg.norm(v)
    return v / n if n > 1e-12 else v

class Acc:
    """One material's vertices: position, uv, two-joint weights; indexed triangles."""
    def __init__(self): self.p=[]; self.uv=[]; self.j=[]; self.w=[]; self.t=[]
    def ring_grid(self, rings, us, vs, weights, close_start=None, close_end=None):
        """rings: list of (n,3) point arrays (same n). Adds a seam column so UVs wrap.
        weights: per ring ((j0,j1),(w0,w1)). close_*: pole point, or None for an open end."""
        n = len(rings[0]); base = len(self.p); cols = n + 1
        for r, v, (jj, ww) in zip(rings, vs, weights):
            for i in range(cols):
                self.p.append(np.asarray(r[i % n], float)); self.uv.append((us * i / n, v))
                self.j.append((jj[0], jj[1], 0, 0)); self.w.append((ww[0], ww[1], 0, 0))
        for k in range(len(rings) - 1):
            for i in range(n):
                a = base + k * cols + i; b = a + 1; c = a + cols; d = c + 1
                self.t += [(a, b, c), (b, d, c)]
        for pole, k, flip in ((close_start, 0, True), (close_end, len(rings) - 1, False)):
            if pole is None: continue
            pid = len(self.p); self.p.append(np.asarray(pole, float)); self.uv.append((us * .5, vs[k]))
            jj, ww = weights[k]; self.j.append((jj[0], jj[1], 0, 0)); self.w.append((ww[0], ww[1], 0, 0))
            for i in range(n):
                a = base + k * cols + i; b = a + 1
                self.t.append((b, a, pid) if flip else (a, b, pid))

    def arrays(self):
        p = np.array(self.p, float); t = np.array(self.t, int); uv = np.array(self.uv, float)
        e1 = p[t[:, 1]] - p[t[:, 0]]; e2 = p[t[:, 2]] - p[t[:, 0]]
        fn = np.cross(e1, e2)                       # area-weighted
        nrm = np.zeros_like(p)
        for k in range(3): np.add.at(nrm, t[:, k], fn)
        # weld normals across UV seams and poles: same position, same normal
        key = np.round(p / 1e-5).astype(np.int64)
        _, inv = np.unique(key, axis=0, return_inverse=True); inv = inv.reshape(-1)
        summed = np.zeros((inv.max() + 1, 3)); np.add.at(summed, inv, nrm); nrm = summed[inv]
        nrm /= np.maximum(np.linalg.norm(nrm, axis=1)[:, None], 1e-12)
        # tangents from UV derivatives
        d1 = uv[t[:, 1]] - uv[t[:, 0]]; d2 = uv[t[:, 2]] - uv[t[:, 0]]
        r = d1[:, 0] * d2[:, 1] - d2[:, 0] * d1[:, 1]; r = np.where(abs(r) < 1e-12, 1e-12, r)
        sdir = (e1 * d2[:, 1:2] - e2 * d1[:, 1:2]) / r[:, None]
        tdir = (e2 * d1[:, 0:1] - e1 * d2[:, 0:1]) / r[:, None]
        tan = np.zeros_like(p); bit = np.zeros_like(p)
        for k in range(3): np.add.at(tan, t[:, k], sdir); np.add.at(bit, t[:, k], tdir)
        tan = tan - nrm * np.sum(nrm * tan, 1)[:, None]
        bad = np.linalg.norm(tan, axis=1) < 1e-9
        if bad.any():
            alt = np.cross(nrm[bad], np.array((0, 0, 1.0))); alt[np.linalg.norm(alt, axis=1) < 1e-6] = (1, 0, 0); tan[bad] = alt
            tan[bad] -= nrm[bad] * np.sum(nrm[bad] * tan[bad], 1)[:, None]
        tan /= np.maximum(np.linalg.norm(tan, axis=1)[:, None], 1e-12)
        sign = np.where(np.sum(np.cross(nrm, tan) * bit, 1) < 0, -1.0, 1.0)
        w = np.array(self.w, float); w /= w.sum(1)[:, None]
        return p, nrm, tan, sign, uv, np.array(self.j, int), w, t

def frames(points, up_hint):
    """Parallel-transported (tangent, u, w) frames along a polyline."""
    pts = np.asarray(points, float); m = len(pts); ts = []
    for i in range(m):
        a = pts[max(i - 1, 0)]; b = pts[min(i + 1, m - 1)]; ts.append(unit(b - a))
    u = unit(np.asarray(up_hint, float) - np.dot(up_hint, ts[0]) * ts[0]); out = []
    for t in ts:
        u = unit(u - np.dot(u, t) * t); out.append((t, u, np.cross(t, u)))
    return out

def superellipse(n, a, b, e=2.0, phase=0.0):
    th = np.linspace(0, 2 * math.pi, n, endpoint=False) + phase
    c, s = np.cos(th), np.sin(th)
    return np.sign(c) * np.abs(c) ** (2 / e) * a, np.sign(s) * np.abs(s) ** (2 / e) * b

def loft(acc, centres, fr, radii, weights, n, cap_start=0.0, cap_end=0.0, e=2.0, uv_tile=0.08, aspect=None):
    """A tube through centres with per-station radius (and optional (a,b) aspect), hemispherical caps
    of the given depth fraction (0 = flat pole, None = open)."""
    rings = []; vs = []; ws = []; length = 0.0
    def ring(c, f, r, asp):
        t, u, w = f; a, b = (r * asp[0], r * asp[1]) if asp is not None else (r, r)
        x, y = superellipse(n, a, b, e); return c + np.outer(x, u) + np.outer(y, w)
    stations = list(range(len(centres)))
    # start cap rings
    def cap(idx, sign, depth):
        c = np.asarray(centres[idx], float); f = fr[idx]; r = radii[idx]; asp = aspect[idx] if aspect else None
        out = []
        for phi in (math.radians(60), math.radians(35)):
            out.append((c - sign * f[0] * r * depth * math.cos(phi) * 1.0, (f[0], f[1], f[2]), r * math.sin(phi) * 1.0 + 0 * r, asp))
        return out, c - sign * f[0] * r * depth
    pre, pole0 = cap(0, 1, cap_start) if cap_start else ([], centres[0])
    post, pole1 = cap(len(centres) - 1, -1, cap_end) if cap_end else ([], centres[-1])
    seq = [(c, f, r, asp, wt) for (c, f, r, asp, wt) in
           [(p[0], p[1], p[2], p[3], weights[0]) for p in reversed(pre)] +
           [(centres[i], fr[i], radii[i], aspect[i] if aspect else None, weights[i]) for i in stations] +
           [(p[0], p[1], p[2], p[3], weights[-1]) for p in post]]
    prev = None
    circ = 2 * math.pi * max(radii)
    for c, f, r, asp, wt in seq:
        if prev is not None: length += np.linalg.norm(np.asarray(c) - prev)
        prev = np.asarray(c, float)
        rings.append(ring(np.asarray(c, float), f, r, asp)); vs.append(length / uv_tile); ws.append(wt)
    acc.ring_grid(rings, max(1, round(circ / uv_tile)), vs, ws,
                  close_start=pole0 if cap_start is not None else None,
                  close_end=pole1 if cap_end is not None else None)

def limb(acc, J, pts, bones, profile, n, blend, cap_start, cap_end, up_hint, end_trim=0.0, start_extend=0.0):
    """Continuous tube root -> joint -> end, weights blending bones[0] -> bones[1] across the joint."""
    p0, p1, p2 = (np.asarray(p, float) for p in pts)
    L1 = np.linalg.norm(p1 - p0); L2 = np.linalg.norm(p2 - p1)
    ss = list(np.linspace(-start_extend, L1 - .035, 6)) + [L1 - .015, L1, L1 + .015] + list(np.linspace(L1 + .035, L1 + L2 - end_trim, 6))
    cs = []; rs = []; wts = []
    for s in ss:
        c = p0 + unit(p1 - p0) * s if s <= L1 else p1 + unit(p2 - p1) * (s - L1)
        cs.append(c); rs.append(profile(s / (L1 + L2), s < L1))
        k = float(smoothstep(-blend, blend, s - L1)); wts.append(((J[bones[0]], J[bones[1]]), (1 - k, k)))
    fr = frames(cs, up_hint)
    loft(acc, cs, fr, rs, wts, n, cap_start=cap_start, cap_end=cap_end)

# ---------------------------------------------------------------- normal maps
def _noise(size, cells, rng):
    g = rng.random((cells + 1, cells + 1)); g[-1] = g[0]; g[:, -1] = g[:, 0]
    x = np.linspace(0, cells, size, endpoint=False); i = x.astype(int); f = x - i; f = f * f * (3 - 2 * f)
    a = g[i][:, i]; b = g[i][:, i + 1]; c = g[i + 1][:, i]; d = g[i + 1][:, i + 1]
    fx = f[None, :]; fy = f[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy

def normal_png(kind, size=256, seed=7):
    rng = np.random.default_rng(seed); y, x = np.mgrid[0:size, 0:size] / size
    if kind == 'twill':
        h = 0.5 * np.sin(2 * math.pi * 48 * (x + y)) * (0.6 + 0.4 * np.sin(2 * math.pi * 48 * (x - y)))
        h += 1.8 * _noise(size, 6, rng) + 0.8 * _noise(size, 12, rng); strength = 2.2
    elif kind == 'leather':
        h = 0.7 * _noise(size, 40, rng) + 0.5 * _noise(size, 20, rng) + 1.2 * _noise(size, 5, rng); strength = 3.0
    elif kind == 'grain':
        h = 0.4 * _noise(size, 64, rng) + 0.6 * _noise(size, 8, rng); strength = 1.6
    else:  # webbing: ribs along v
        h = 0.6 * np.sin(2 * math.pi * 40 * x) + 0.2 * _noise(size, 16, rng); strength = 2.0
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * size / 2 / 64 * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * size / 2 / 64 * strength
    n = np.dstack((-dx, -dy, np.ones_like(h))); n /= np.linalg.norm(n, axis=2)[..., None]
    img = Image.fromarray(np.uint8(np.clip((n * 0.5 + 0.5) * 255, 0, 255)), 'RGB')
    b = io.BytesIO(); img.save(b, 'PNG', optimize=True); return b.getvalue()

# ---------------------------------------------------------------- body
def body(bones):
    """bones: [(name, head, tail, parent)] in builder frame. Returns {material: Acc}."""
    J = {n: i for i, (n, a, b, p) in enumerate(bones)}; H = {n: np.asarray(a, float) for n, a, b, p in bones}
    T = {n: np.asarray(b, float) for n, a, b, p in bones}
    suit, glove, boot, strap, metal = Acc(), Acc(), Acc(), Acc(), Acc()
    hip = H['Hips']

    # Torso: superellipse sections, hips -> shoulders -> collar. (dz, centre x, half-width, half-depth)
    prof = [(-.05, -.030, .150, .110), (0.0, -.025, .172, .125), (.10, -.022, .165, .118), (.20, -.028, .156, .112),
            (.30, -.038, .172, .120), (.40, -.048, .192, .126), (.46, -.056, .198, .114), (.51, -.064, .150, .092),
            (.545, -.070, .085, .066), (.565, -.072, .066, .056)]
    def tw(dz):
        if dz < .14: k = float(smoothstep(.06, .14, dz)); return (J['Hips'], J['Spine']), (1 - k, k)
        k = float(smoothstep(.28, .36, dz)); return (J['Spine'], J['Chest']), (1 - k, k)
    rings = []; vs = []; ws = []; n = 28; length = 0
    for i, (dz, cx, a, b) in enumerate(prof):
        xs, ys = superellipse(n, b, a, 2.5)
        rings.append(np.stack([hip[0] + cx + xs, hip[1] + ys, np.full(n, hip[2] + dz)], 1))
        if i: length += abs(dz - prof[i - 1][0])
        vs.append(length / .08); ws.append(tw(dz))
    suit.ring_grid(rings, 14, vs, ws, close_start=hip + (-.03, 0, -.07), close_end=hip + (-.072, 0, .57))
    def front(dz, y, out=.006):
        for k in range(len(prof) - 1):
            if prof[k][0] <= dz <= prof[k + 1][0]:
                f = (dz - prof[k][0]) / (prof[k + 1][0] - prof[k][0]); cx, a, b = (np.array(prof[k][1:]) * (1 - f) + np.array(prof[k + 1][1:]) * f)
                break
        q = max(0.0, 1 - abs(y / a) ** 2.5) ** (1 / 2.5)
        return hip + (cx + b * q + out, y, dz)

    # Harness: two shoulder straps over the chest to a lap belt, a buckle at the middle.
    for sy in (1, -1):
        zs = np.linspace(.50, .09, 9); cs = [front(z, sy * (.085 - .035 * (.50 - z) / .41)) for z in zs]
        cs[0] = cs[0] + (-.03, 0, .015)
        fr = frames(cs, (1, 0, 0))
        wts = [tw(z) for z in zs]
        loft(strap, cs, fr, [.024] * len(cs), wts, 8, cap_start=None, cap_end=None, e=8, aspect=[(.22, 1)] * len(cs), uv_tile=.05)
    ys = np.linspace(-.168, .168, 11); cs = [front(.075, y, .007) for y in ys]
    loft(strap, cs, frames(cs, (0, 0, 1)), [.026] * len(cs), [tw(.075)] * len(cs), 8, cap_start=None, cap_end=None, e=8, aspect=[(1, .22)] * len(cs), uv_tile=.05)
    bc = front(.10, 0, .012); bfr = frames([bc - (0, .02, 0), bc + (0, .02, 0)], (0, 0, 1))
    loft(metal, [bc - (0, .028, 0), bc + (0, .028, 0)], bfr, [.035, .035], [tw(.1)] * 2, 8, cap_start=0, cap_end=0, e=6, aspect=[(1, .2)] * 2)

    for side, sy in (('L', 1), ('R', -1)):
        S = H[side + '_UpperArm']; E = H[side + '_LowerArm']; W = H[side + '_Hand']; G = T[side + '_Hand']
        def arm(f, upper):
            return (.058 - .010 * f / .54) if upper else (.047 - .011 * min(1, (f - .54) / .46))
        limb(suit, J, (S, E, W), (side + '_UpperArm', side + '_LowerArm'), arm, 16, .05, 0.9, 0.3,
             (0, 0, 1), end_trim=.012, start_extend=.0)
        # Glove gauntlet over the sleeve's cuff, blending forearm -> hand at the wrist.
        fwd = unit(W - E); hd = unit(G - W); cs = [W - fwd * .065, W - fwd * .035, W - fwd * .01, W + hd * .012]
        rs = [.047, .045, .040, .035]; wts = []
        for c in cs:
            k = float(smoothstep(-.03, .01, np.dot(c - W, fwd))); wts.append(((J[side + '_LowerArm'], J[side + '_Hand']), (1 - k, k)))
        fr = frames(cs, (0, 0, 1))
        loft(glove, [cs[0]] + cs, [fr[0]] + fr, [.034] + rs, [wts[0]] + wts, 14, cap_start=None, cap_end=None, uv_tile=.04)
        # Palm: a rounded block, wrist -> knuckles.
        side_v = unit(np.cross((0, 0, 1), hd)); up = unit(np.cross(hd, side_v))
        cs = [W + hd * s for s in (-.005, .02, .05, .075, .092)]
        asp = [(1, .78), (1.2, .58), (1.32, .48), (1.3, .44), (1.2, .40)]
        fr = [(hd, side_v, up)] * len(cs)
        loft(glove, cs, fr, [.031, .032, .033, .033, .033], [((J[side + '_Hand'], 0), (1, 0))] * len(cs), 14, cap_start=.5, cap_end=.6, e=3.0, aspect=asp, uv_tile=.04)
        # Fingers: tapered capsules per phalanx (rigid), overlapping so a curled joint stays closed.
        for finger, r0 in (('Thumb', .0118), ('Index', .0102), ('Middle', .0104), ('Ring', .0098), ('Little', .0086)):
            for j in (1, 2, 3):
                bn = f'{side}_{finger}{j}'; a, b = H[bn], T[bn]; r = r0 * (1 - .07 * (j - 1))
                d = unit(b - a); cs = [a, b]; fr = frames(cs, up)
                loft(glove, cs, fr, [r, r * .95], [((J[bn], 0), (1, 0))] * 2, 8, cap_start=.9, cap_end=.9, uv_tile=.03)
        # Legs: thigh -> knee -> shin, continuous, tucked into the boot shaft.
        P = H[side + '_UpperLeg']; K = H[side + '_LowerLeg']; A = H[side + '_Foot']; B = T[side + '_Foot']; X = T[side + '_Toes']
        def leg(f, upper):
            return (.080 - .016 * f / .49) if upper else (.060 - .012 * min(1, (f - .49) / .51))
        limb(suit, J, (P, K, A), (side + '_UpperLeg', side + '_LowerLeg'), leg, 16, .06, 0.6, None,
             (0, sy, 0), end_trim=.03, start_extend=.02)
        # Boot shaft up the shin, foot -> shin weights.
        sh = unit(K - A); cs = [A + sh * s for s in (0.0, .05, .10, .145, .145)]
        rs = [.054, .055, .057, .060, .047]
        wts = [((J[side + '_Foot'], J[side + '_LowerLeg']), (1 - k, k)) for k in (0, .5, 1, 1, 1)]
        loft(boot, cs, frames(cs, (1, 0, 0)), rs, wts, 16, cap_start=None, cap_end=None, uv_tile=.05)
        # Boot: lofted heel -> ball -> toe along the foot, sole flat, foot -> toes weights.
        f = unit(B - A); s_ = unit(np.cross((0, 0, 1), f)); u = unit(np.cross(f, s_)); Lf = np.linalg.norm(B - A)
        st = [-.06, -.04, 0, .04, .08, Lf, Lf + .035, Lf + .062, Lf + .08]
        top = [.055, .075, .08, .066, .052, .042, .034, .024, .010]
        hw = [.032, .043, .046, .048, .050, .052, .048, .038, .020]
        cs = []; fr = []; rs = []; asp = []; wts = []
        for s, tp, w_ in zip(st, top, hw):
            bot = -.080 + .042 * min(max(s / Lf, 0), 1.25)
            if s < 0: bot = -.080 + .01 * (s / .06) ** 2
            mid = (tp + bot) / 2; hh = (tp - bot) / 2
            cs.append(A + f * s + u * mid); fr.append((f, s_, u)); rs.append(hh); asp.append((w_ / hh, 1))
            k = float(smoothstep(Lf - .015, Lf + .02, s)); wts.append(((J[side + '_Foot'], J[side + '_Toes']), (1 - k, k)))
        loft(boot, cs, fr, rs, wts, 18, cap_start=.5, cap_end=.8, e=3.2, aspect=asp, uv_tile=.05)
    return dict(suit=suit, glove=glove, boot=boot, strap=strap, metal=metal)
