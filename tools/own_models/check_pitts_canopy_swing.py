"""THE PITTS CANOPY'S SWING AGAINST THE MODEL (logic lane, 2026-11-05).

Core's CanopyStopTab simulates only the stop tab: it assumes the canopy, swinging open about its right-sill hinge,
never touches the upper wing, and that its frame meets Canopy_Stop_Rubber after the normal 4 in of slide but not after
the jettison's 6 in. This checks both against the GLB's own meshes (every mesh under the Canopy node, every vertex
widened by the 3/8 in tube's radius, swung in 0.25 deg steps):

  WING    slid 0, 4 in and 6 in, no canopy vertex enters Wing_Upper (its top/bottom skins rasterised on a 5 mm grid)
          anywhere from shut to 180 deg;
  TAB     slid 4 in, the frame reaches the rubber (within tube + rubber radii of a rubber vertex) before 180 deg, and
          it does so at an angle within 2 deg of what Core says (CORE_REST_DEG, copied from PittsCanopyTabTests);
          slid 6 in it never does.

    /usr/bin/python3 tools/own_models/check_pitts_canopy_swing.py [glb ...]     (default: all four Pitts GLBs)
"""
import json, math, struct, sys
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
MODELS = ROOT / 'unity/Assets/_Project/Resources/Models/pitts-s2b-like'
HINGE = np.array((0.8, 0.16, -0.36))      # the Canopy node's origin, glTF
TUBE_R, RUBBER_R = 0.0047625, 0.006
SLIDE, TRACK = 0.1016, 0.1524
CORE_REST_DEG = 96.8                       # PittsCanopyTabTests: 96.6..97.0
CT = {5126: 'f4', 5123: 'u2', 5125: 'u4', 5121: 'u1'}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}


def load(p):
    f = p.read_bytes()
    n = struct.unpack('<I', f[12:16])[0]
    return json.loads(f[20:20 + n]), f[20 + n + 8:]


def acc(j, b, i):
    a = j['accessors'][i]; bv = j['bufferViews'][a['bufferView']]
    dt = np.dtype(CT[a['componentType']]); n = NC[a['type']]
    off = bv.get('byteOffset', 0) + a.get('byteOffset', 0); stride = bv.get('byteStride', dt.itemsize * n)
    out = np.zeros((a['count'], n))
    for k in range(n):
        out[:, k] = np.ndarray((a['count'],), dtype=dt, buffer=b, offset=off + k * dt.itemsize, strides=(stride,))
    return out


def quat(q):
    x, y, z, w = q
    return np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)], [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                     [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])


def worlds(j):
    par = {c: i for i, n in enumerate(j['nodes']) for c in n.get('children', [])}
    W = {}
    def w(i):
        if i not in W:
            n = j['nodes'][i]; M = np.eye(4)
            if 'matrix' in n: M = np.array(n['matrix']).reshape(4, 4).T
            else:
                M[:3, :3] = quat(n.get('rotation', [0, 0, 0, 1])) @ np.diag(n.get('scale', [1, 1, 1]))
                M[:3, 3] = n.get('translation', [0, 0, 0])
            W[i] = (w(par[i]) @ M) if i in par else M
        return W[i]
    for i in range(len(j['nodes'])): w(i)
    return W


def mesh(j, b, W, i, tris=False):
    n = j['nodes'][i]
    if 'mesh' not in n: return None
    out = []
    for p in j['meshes'][n['mesh']]['primitives']:
        v = acc(j, b, p['attributes']['POSITION']); v = (W[i] @ np.c_[v, np.ones(len(v))].T).T[:, :3]
        if tris:
            idx = acc(j, b, p['indices']).astype(int).reshape(-1, 3) if 'indices' in p else np.arange(len(v)).reshape(-1, 3)
            v = v[idx]
        out.append(v)
    return np.vstack(out) if out else None


def check(path):
    j, b = load(path); W = worlds(j); names = {n.get('name'): i for i, n in enumerate(j['nodes'])}
    sub, st = [], [names['Canopy']]
    while st:
        i = st.pop(); sub.append(i); st += j['nodes'][i].get('children', [])
    cv = np.vstack([v for v in (mesh(j, b, W, i) for i in sub) if v is not None])
    rubber = mesh(j, b, W, names['Canopy_Stop_Rubber'])
    T = mesh(j, b, W, names['Wing_Upper'], tris=True)
    g, x0, x1, z0, z1 = 0.005, -0.8, 0.7, -1.7, 1.7
    nx, nz = int((x1 - x0) / g) + 1, int((z1 - z0) / g) + 1
    top = np.full((nx, nz), -9.0); bot = np.full((nx, nz), 9.0)
    for a, bb, c in T:
        lo, hi = np.minimum(np.minimum(a, bb), c), np.maximum(np.maximum(a, bb), c)
        if hi[0] < x0 or lo[0] > x1 or hi[2] < z0 or lo[2] > z1: continue
        i0, i1 = max(0, int((lo[0] - x0) / g)), min(nx - 1, int((hi[0] - x0) / g) + 1)
        k0, k1 = max(0, int((lo[2] - z0) / g)), min(nz - 1, int((hi[2] - z0) / g) + 1)
        X, Z = np.meshgrid(x0 + g * np.arange(i0, i1 + 1), z0 + g * np.arange(k0, k1 + 1), indexing='ij')
        d = (bb[2] - c[2]) * (a[0] - c[0]) + (c[0] - bb[0]) * (a[2] - c[2])
        if abs(d) < 1e-12: continue
        l1 = ((bb[2] - c[2]) * (X - c[0]) + (c[0] - bb[0]) * (Z - c[2])) / d
        l2 = ((c[2] - a[2]) * (X - c[0]) + (a[0] - c[0]) * (Z - c[2])) / d
        l3 = 1 - l1 - l2; m = (l1 >= -1e-6) & (l2 >= -1e-6) & (l3 >= -1e-6)
        Y = l1 * a[1] + l2 * bb[1] + l3 * c[1]
        t, s = top[i0:i1 + 1, k0:k1 + 1], bot[i0:i1 + 1, k0:k1 + 1]
        t[m] = np.maximum(t[m], Y[m]); s[m] = np.minimum(s[m], Y[m])

    def pose(slide, deg):
        t = math.radians(deg); p = cv - HINGE
        inb, up = p[:, 2], p[:, 1]
        right = -math.cos(t) * inb + math.sin(t) * up; upn = math.sin(t) * inb + math.cos(t) * up
        return np.c_[p[:, 0] + slide, upn, -right] + HINGE

    def in_wing(q):
        i = np.round((q[:, 0] - x0) / g).astype(int); k = np.round((q[:, 2] - z0) / g).astype(int)
        ok = (i >= 0) & (i < nx) & (k >= 0) & (k < nz); r = np.zeros(len(q), bool)
        r[ok] = (q[ok, 1] > bot[i[ok], k[ok]] - TUBE_R) & (q[ok, 1] < top[i[ok], k[ok]] + TUBE_R)
        return r

    def on_tab(q):
        near = q[(np.abs(q[:, 0] - rubber[:, 0].mean()) < 0.1) & (np.abs(q[:, 2] - rubber[:, 2].mean()) < 0.1)]
        if len(near) == 0: return False
        d = np.sqrt(((near[:, None, :] - rubber[None, :, :]) ** 2).sum(-1)).min()
        return d < TUBE_R + RUBBER_R

    fails = []
    for slide in (0.0, SLIDE, TRACK):
        hit = next((a for a in np.arange(0, 180.01, 0.25) if in_wing(pose(slide, a)).any()), None)
        print(f'  {path.name}: slid {slide * 39.37:.0f} in, wing: {"clear to 180" if hit is None else f"TOUCHES at {hit:.2f} deg"}')
        if hit is not None: fails.append(f'canopy enters Wing_Upper at {hit:.2f} deg slid {slide} m')
    for slide, want in ((SLIDE, True), (TRACK, False)):
        hit = next((a for a in np.arange(45, 180.01, 0.25) if on_tab(pose(slide, a))), None)
        print(f'  {path.name}: slid {slide * 39.37:.0f} in, tab: {"never" if hit is None else f"meets the rubber at {hit:.2f} deg"}')
        if want and (hit is None or abs(hit - CORE_REST_DEG) > 2.0): fails.append(f'slid 4 in the frame meets the rubber at {hit} deg, Core says {CORE_REST_DEG}')
        if not want and hit is not None: fails.append(f'slid 6 in the frame still meets the rubber at {hit:.2f} deg')
    return fails


def main():
    paths = [Path(p) for p in sys.argv[1:]] or sorted(MODELS.glob('*-own*.glb'))
    bad = 0
    for p in paths:
        f = check(p)
        for m in f: print(f'FAIL {p.name}: {m}')
        bad += len(f)
    print('PASS' if bad == 0 else f'FAIL ({bad})')
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
