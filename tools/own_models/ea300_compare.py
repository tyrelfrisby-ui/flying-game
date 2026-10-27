"""Compare our extra-300-like-own.glb against the FG reference GLB: 3-D surface deviation + silhouette masks.
  blender -b --python tools/own_models/ea300_compare.py -- ours.glb ref.glb outdir
Writes outdir/{ours,ref}_{side,top,front}.npy (orthographic silhouette masks, 4 mm/px) and dev.json."""
import bpy, sys, json, math, numpy as np
from mathutils.bvhtree import BVHTree
a = sys.argv[sys.argv.index('--') + 1:]; OURS, REF, OUTD = a[0], a[1], a[2]
SKIP_REF = {'Cockpit', 'Engine_Detail', 'Prop'}     # interior / engine parts and the prop blades (spinning disc) are not matched
SKIP_OURS = {'Cabin', 'Prop'}
def load(path, skip):
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=path); parts = {}
    for o in bpy.context.scene.objects:
        if o.type != 'MESH' or o.name.split('.')[0] in skip: continue
        me = o.data; me.calc_loop_triangles(); M = o.matrix_world
        v = np.array([(M @ x.co)[:] for x in me.vertices]); t = np.array([tr.vertices[:] for tr in me.loop_triangles])
        parts[o.name.split('.')[0]] = (v, t)
    return parts
def tree(parts):
    vs, ts, off = [], [], 0
    for v, t in parts.values(): vs.append(v); ts.append(t + off); off += len(v)
    v = np.concatenate(vs); t = np.concatenate(ts)
    return BVHTree.FromPolygons([tuple(p) for p in v], [tuple(map(int, q)) for q in t]), v, t
def samples(v, t, n_per_m2=40000):
    A = v[t]; ar = 0.5 * np.linalg.norm(np.cross(A[:, 1] - A[:, 0], A[:, 2] - A[:, 0]), axis=1)
    k = np.maximum(1, (ar * n_per_m2).astype(int)); idx = np.repeat(np.arange(len(t)), np.minimum(k, 200))
    r1, r2 = np.random.rand(len(idx)), np.random.rand(len(idx)); m = r1 + r2 > 1; r1[m], r2[m] = 1 - r1[m], 1 - r2[m]
    P = A[idx]; return P[:, 0] + r1[:, None] * (P[:, 1] - P[:, 0]) + r2[:, None] * (P[:, 2] - P[:, 0])
def inside(tr, pts):
    out = []
    for p in pts:
        o = list(p); n = 0
        for _ in range(20):
            h = tr.ray_cast(o, (0, 1, 0), 10)
            if h[0] is None: break
            n += 1; o = (h[0].x, h[0].y + 1e-4, h[0].z)
        out.append(n % 2 == 1)
    return np.array(out, bool)
DIRS = [np.array(d, float) / np.linalg.norm(d) for d in [(1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1),(1,1,1),(1,1,-1),(1,-1,1),(1,-1,-1),(-1,1,1),(-1,1,-1),(-1,-1,1),(-1,-1,-1),(1,1,0),(1,-1,0),(-1,1,0),(-1,-1,0),(1,0,1),(1,0,-1),(-1,0,1),(-1,0,-1),(0,1,1),(0,1,-1),(0,-1,1),(0,-1,-1)]]
def enclosed(tr, p):     # a surface point is hidden when every ray from it hits the same model within 4 m
    for d in DIRS:
        h = tr.ray_cast(tuple(p + 0.002 * d), tuple(d), 4.0)
        if h[0] is None: return False
    return True
def visible_dev(own_tr, p, d, th=0.02):
    m = d > th
    for i in np.where(m)[0]:
        if enclosed(own_tr, p[i]): d[i] = 0.0
    return d
def dist(tr, pts):
    return np.array([(tr.find_nearest(tuple(p))[3] or 0) for p in pts])
np.random.seed(1)
O = load(OURS, SKIP_OURS); Rf = load(REF, SKIP_REF)
to, vo, tto = tree(O); tr_, vr, ttr = tree(Rf)
fo = tree({k: v for k, v in O.items() if k.startswith('Fuselage')})[0]; fr = tree({'Fuselage': Rf['Fuselage']})[0]
def vis(p):   # hidden geometry (inside either fuselage) is not compared
    m = ~(inside(fo, p) | inside(fr, p)); return p[m]
res = {'ours_to_ref': {}, 'ref_to_ours': {}, 'clusters': []}
def clusters(tag, name, p, d, th=0.025):
    m = d > th
    if not m.any(): return
    q = p[m] * np.array([-1, -1, 1]); dd = d[m]            # report in the build frame (nose +x, port +y)
    cell = np.round(q / 0.1).astype(int); keys = {}
    for c, x, e in zip(map(tuple, cell), q, dd):
        k = keys.setdefault(c, [0, 0, None]); k[0] += 1
        if e > k[1]: k[1] = float(e); k[2] = [round(float(v), 3) for v in x]
    for c, (n, e, x) in sorted(keys.items(), key=lambda kv: -kv[1][1])[:6]:
        res['clusters'].append([tag, name, n, round(e, 3), x]); print('CL', tag, '%-14s n %4d max %.3f at' % (name, n, e), x)
for name, (v, t) in O.items():
    p = samples(v, t); p = p if name.startswith('Fuselage') else vis(p); d = visible_dev(to, p, dist(tr_, p)); i = int(d.argmax()); res['ours_to_ref'][name] = [float(d.max()), float(np.percentile(d, 99)), float(d.mean()), list(map(float, p[i]))]; clusters('o2r', name, p, d)
for name, (v, t) in Rf.items():
    p = samples(v, t); p = p if name == 'Fuselage' else vis(p); d = visible_dev(tr_, p, dist(to, p)); i = int(d.argmax())
    res['ref_to_ours'][name] = [float(d.max()), float(np.percentile(d, 99)), float(d.mean()), list(map(float, p[i]))]; clusters('r2o', name, p, d)
# silhouettes (orthographic, 4 mm px) - rasterise triangles
PX = 0.004; X0, X1, Y0, Y1, Z0, Z1 = -2.5, 5.5, -4.2, 4.2, -1.6, 1.5
def raster(v, t, ax_u, ax_v, u0, u1, v0, v1):
    W, H = int((u1 - u0) / PX), int((v1 - v0) / PX); img = np.zeros((H, W), bool)
    U = (v[:, ax_u] - u0) / PX; V = (v1 - v[:, ax_v]) / PX
    for a, b, c in t:
        xs = np.array([U[a], U[b], U[c]]); ys = np.array([V[a], V[b], V[c]])
        x0, x1 = max(int(xs.min()), 0), min(int(xs.max()) + 1, W - 1); y0, y1 = max(int(ys.min()), 0), min(int(ys.max()) + 1, H - 1)
        if x1 < x0 or y1 < y0: continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = (xs[1] - xs[0]) * (ys[2] - ys[0]) - (xs[2] - xs[0]) * (ys[1] - ys[0])
        if abs(d) < 1e-9:
            continue
        l1 = ((xs[1] - gx) * (ys[2] - gy) - (xs[2] - gx) * (ys[1] - gy)) / d
        l2 = ((xs[2] - gx) * (ys[0] - gy) - (xs[0] - gx) * (ys[2] - gy)) / d
        l3 = 1 - l1 - l2; m = (l1 >= -0.02) & (l2 >= -0.02) & (l3 >= -0.02)
        img[y0:y1 + 1, x0:x1 + 1] |= m
    return img
for tag, parts in (('ours', O), ('ref', Rf)):
    vs, ts, off = [], [], 0
    for v, t in parts.values(): vs.append(v); ts.append(t + off); off += len(v)
    v = np.concatenate(vs); t = np.concatenate(ts)
    np.save(f'{OUTD}/{tag}_side.npy', raster(v, t, 0, 2, X0, X1, Z0, Z1))
    np.save(f'{OUTD}/{tag}_top.npy', raster(v, t, 0, 1, X0, X1, Y0, Y1))
    np.save(f'{OUTD}/{tag}_front.npy', raster(v, t, 1, 2, Y0, Y1, Z0, Z1))
json.dump(res, open(f'{OUTD}/dev.json', 'w'), indent=1)
for k in ('ours_to_ref', 'ref_to_ours'):
    for n, r in sorted(res[k].items(), key=lambda kv: -kv[1][0]): print('DEV', k, '%-14s max %.3f p99 %.3f mean %.4f' % (n, r[0], r[1], r[2]), r[3:] and [round(c, 2) for c in r[3]])
