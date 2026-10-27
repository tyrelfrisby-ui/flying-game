"""Measure the approved FG-based Extra (codex/fg-models v9 5fdacad, extra-300-like-own.glb) as a DIMENSIONAL REFERENCE.

Ty's decision (2026-10-26): our own EA 300 must match that model's shape to ~1 in.  This script only MEASURES it:
silhouette/section numbers sampled at our own stations are written to ea300_ref_tables.json.  No vertex, face, UV or
texture of the reference is copied into our model; build_ea300.py lofts all-new surfaces through the measured numbers.
Reference: FlightGear EA300 by Patrizio Melis (GPL) - see docs/models/ea300-own-PROVENANCE.md.

  git show 5fdacad:unity/Assets/_Project/Resources/Models/extra-300-like/extra-300-like-own.glb > /tmp/fgref.glb
  blender -b --python tools/own_models/ea300_measure.py -- /tmp/fgref.glb
Output frame = build_ea300.py's frame: nose +X, port +Y, up +Z  (glTF-import frame yawed 180 deg).
"""
import bpy, sys, os, json, math, numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else '/tmp/fgref.glb'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
T = {}
for o in bpy.context.scene.objects:
    if o.type != 'MESH': continue
    me = o.data; me.calc_loop_triangles(); M = o.matrix_world
    v = np.array([(M @ x.co)[:] for x in me.vertices]); v[:, 0] *= -1; v[:, 1] *= -1      # -> nose +X, port +Y
    tri = np.array([t.vertices[:] for t in me.loop_triangles])
    T[o.name] = (v, tri)
def bvh(names):
    vs, ts, off = [], [], 0
    for n in names:
        v, t = T[n]; vs.append(v); ts.append(t + off); off += len(v)
    v = np.concatenate(vs); t = np.concatenate(ts)
    return BVHTree.FromPolygons([tuple(p) for p in v], [tuple(map(int, q)) for q in t]), v
def bounds(names):
    v = np.concatenate([T[n][0] for n in names]); return v.min(0), v.max(0)
def slice_segs(names, axis, c):
    """triangle/plane intersection segments (other two coords) for plane coord[axis] = c"""
    out = []
    oth = [k for k in range(3) if k != axis]
    for n in names:
        v, t = T[n]; d = v[:, axis] - c; P = v[t]; D = d[t]
        s = np.sign(D); m = ~((s[:, 0] == s[:, 1]) & (s[:, 1] == s[:, 2]))
        P, D = P[m], D[m]
        for p, dd in zip(P, D):
            pts = []
            for i, j in ((0, 1), (1, 2), (2, 0)):
                if (dd[i] > 0) != (dd[j] > 0):
                    f = dd[i] / (dd[i] - dd[j]); q = p[i] + f * (p[j] - p[i]); pts.append(q[oth])
            if len(pts) == 2: out.append(pts)
    return np.array(out).reshape(-1, 2, 2)
def cross(segs, k, c):
    """values of the other coordinate where segments cross coordinate k == c"""
    a, b = segs[:, 0], segs[:, 1]; m = (a[:, k] - c) * (b[:, k] - c) <= 0
    a, b = a[m], b[m]; dk = b[:, k] - a[:, k]; f = np.where(np.abs(dk) > 1e-12, (c - a[:, k]) / np.where(np.abs(dk) > 1e-12, dk, 1), 0.5)
    return a[:, 1 - k] + f * (b[:, 1 - k] - a[:, 1 - k])
R = {}
# ---- fuselage + canopy envelope: radial rays in x-stations, tagged by which part is hit first ----------------
FUS = ['Fuselage', 'Canopy_Glass', 'Canopy_Frame']
tags = {}; trees = {}
for n in FUS: trees[n] = bvh([n])[0]
lo, hi = bounds(['Fuselage'])
xs = np.concatenate([np.linspace(hi[0] - 0.003, hi[0] - 0.10, 6), np.linspace(hi[0] - 0.12, lo[0] + 0.12, 110), np.linspace(lo[0] + 0.10, lo[0] + 0.004, 5)])
NA = 64; ang = np.linspace(0, 2 * math.pi, NA, endpoint=False)
rings = []
for x in xs:
    seg = slice_segs(['Fuselage'], 0, x)
    if len(seg) == 0: continue
    p = seg.reshape(-1, 2); zc = 0.5 * (p[:, 1].max() + p[:, 1].min())
    ring = []
    for a in ang:
        d = Vector((0, math.sin(a), math.cos(a))); o = Vector((x, 0, zc)) + 3 * d
        best = None
        for n, tr in trees.items():
            h = tr.ray_cast(o, -d, 3.0)
            if h[0] is not None and (best is None or h[3] < best[1]): best = (n, h[3], h[0])
        if best is None: ring.append([x, 0.0, zc, 0]); continue
        ring.append([x, best[2].y, best[2].z, FUS.index(best[0])])
    rings.append({'x': float(x), 'zc': float(zc), 'pts': ring})
R['fuselage'] = {'angles_from_up_toward_port': list(map(float, ang)), 'rings': rings}
# ---- lifting surfaces: sections at span stations, upper/lower z at chord fractions --------------------------
FR = list((1 - np.cos(np.linspace(0, math.pi, 41))) / 2)
def surf(names, mov, spans, axis_span=1, thick_axis=2, minc=0.05):
    out = []
    for y in spans:
        seg = slice_segs(names, axis_span, y)
        if len(seg) == 0: continue
        p = seg.reshape(-1, 2); chord_k = 0; th_k = 1      # (x, other)
        xle, xte = p[:, 0].max(), p[:, 0].min()          # nose +X: LE is max x
        up, dn = [], []
        for f in FR:
            xx = xle - f * (xle - xte); xx = min(max(xx, xte + 1e-4), xle - 1e-4)
            zz = cross(seg, 0, xx)
            if axis_span == 1 and len(zz): zz = zz[zz > zz.max() - 0.30]      # ignore aileron spades hanging below
            up.append(float(zz.max()) if len(zz) else float(p[:, 1].mean())); dn.append(float(zz.min()) if len(zz) else float(p[:, 1].mean()))
        hinge = None
        if mov:
            ms = slice_segs(mov, axis_span, y)
            if len(ms): hinge = float(ms.reshape(-1, 2)[:, 0].max())
        out.append({'s': float(y), 'xle': float(xle), 'xte': float(xte), 'up': up, 'dn': dn, 'hinge': hinge})
    return out
R['chord_fractions'] = list(map(float, FR))
wl, wh = bounds(['Wing', 'Aileron_L', 'Aileron_R'])
R['wing'] = surf(['Wing', 'Aileron_L'], ['Aileron_L'], list(np.linspace(0.0, wh[1] - 0.004, 41)))
R['wing_r'] = surf(['Wing', 'Aileron_R'], ['Aileron_R'], list(np.linspace(-0.0, wl[1] + 0.004, 41)))
sl, sh = bounds(['Stab'])
R['stab'] = surf(['Stab', 'Elevator_L'], ['Elevator_L'], list(np.linspace(0.0, sh[1] - 0.003, 21)))
R['aileron_span'] = [float(-bounds(['Aileron_R'])[1][1]), float(bounds(['Aileron_L'])[1][1])] if False else [float(bounds(['Aileron_L'])[0][1]), float(bounds(['Aileron_L'])[1][1])]
R['elevator_span'] = [float(bounds(['Elevator_L'])[0][1]), float(bounds(['Elevator_L'])[1][1])]
fl, fh = bounds(['Fin', 'Rudder'])
R['fin'] = surf(['Fin', 'Rudder'], ['Rudder'], list(np.linspace(fl[2] + 0.002, fh[2] - 0.004, 41)), axis_span=2)
R['fin_z'] = [float(fl[2]), float(fh[2])]; R['rudder_z'] = [float(x) for x in (bounds(['Rudder'])[0][2], bounds(['Rudder'])[1][2])]
R['fin_only_zmin'] = float(bounds(['Fin'])[0][2])
# ---- wheel pants (port): radial rays about their own axis --------------------------------------------------
pv = T['Wheel_Pants'][0]; port = pv[:, 1] > 0
ptr = BVHTree.FromPolygons([tuple(p) for p in pv], [tuple(map(int, q)) for q in T['Wheel_Pants'][1]])
pl, ph = pv[port].min(0), pv[port].max(0); yc = 0.5 * (pl[1] + ph[1])
pr = []
for x in np.concatenate([[ph[0] - 0.003], np.linspace(ph[0] - 0.02, pl[0] + 0.02, 40), [pl[0] + 0.003]]):
    seg = slice_segs(['Wheel_Pants'], 0, x); p = seg.reshape(-1, 2); p = p[p[:, 0] > 0]
    if len(p) == 0: continue
    zc = 0.5 * (p[:, 1].max() + p[:, 1].min()); ring = []
    for a in np.linspace(0, 2 * math.pi, 32, endpoint=False):
        d = Vector((0, math.sin(a), math.cos(a))); o = Vector((x, yc, zc)) + 1 * d
        h = ptr.ray_cast(o, -d, 1.0)
        ring.append([float(h[0].y), float(h[0].z)] if h[0] is not None else [yc, zc])
    pr.append({'x': float(x), 'pts': ring})
R['pants'] = {'yc': float(yc), 'rings': pr}
# ---- main gear leg (port): centre line + size from horizontal slices -----------------------------------------
gl = []
gv = T['Gear_Main'][0]; gp = gv[gv[:, 1] > 0]
for z in np.linspace(gp[:, 2].max() - 0.01, gp[:, 2].min() + 0.01, 14):
    seg = slice_segs(['Gear_Main'], 2, z); p = seg.reshape(-1, 2); p = p[p[:, 1] > 0]
    if len(p): gl.append([float(p[:, 0].mean()), float(p[:, 1].mean()), float(z), float(np.ptp(p[:, 0])), float(np.ptp(p[:, 1]))])
R['gear_leg'] = gl
# ---- wheels: main (port) and tail -------------------------------------------------------------------------------
wv = T['Wheels'][0]
mw = wv[(wv[:, 0] > -2) & (wv[:, 1] > 0.3)]; tw = wv[wv[:, 0] < -3]
R['main_wheel'] = {'c': list(map(float, (mw.min(0) + mw.max(0)) / 2)), 'r': float(np.ptp(mw[:, 2]) / 2), 'w': float(np.ptp(mw[:, 1]))}
R['tail_wheel'] = {'c': list(map(float, (tw.min(0) + tw.max(0)) / 2)), 'r': float(np.ptp(tw[:, 2]) / 2), 'w': float(np.ptp(tw[:, 1]))}
tg = T['Gear_Tail'][0]; tl = []
for x in np.linspace(tg[:, 0].max() - 0.01, tg[:, 0].min() + 0.01, 10):
    seg = slice_segs(['Gear_Tail'], 0, x); p = seg.reshape(-1, 2)
    if len(p): tl.append([float(x), float(p[:, 1].mean()), float(np.ptp(p[:, 1]))])
R['tail_leg'] = tl
# ---- spinner profile and prop disc --------------------------------------------------------------------------------
sv = T['Spinner'][0]; sp = []
for x in np.linspace(sv[:, 0].max() - 0.002, sv[:, 0].min() + 0.002, 24):
    seg = slice_segs(['Spinner'], 0, x); p = seg.reshape(-1, 2)
    if len(p): sp.append([float(x), float(np.sqrt((p ** 2).sum(1)).max())])
R['spinner'] = sp
R['spinner_tip_x'] = float(sv[:, 0].max())
pv = T['Prop'][0]; R['prop'] = {'x': float(pv[:, 0].mean()), 'r': float(np.sqrt(pv[:, 1] ** 2 + pv[:, 2] ** 2).max())}
R['bounds'] = {n: [list(map(float, T[n][0].min(0))), list(map(float, T[n][0].max(0)))] for n in T}
# ---- radial hull rings of a slice (for gear legs etc.) --------------------------------------------------------------
from mathutils.geometry import convex_hull_2d
def hull_ring(p, n=16):
    h = p[convex_hull_2d([tuple(q) for q in p])]; c = h.mean(0)
    d = np.concatenate([h[i] + (h[(i + 1) % len(h)] - h[i]) * t[:, None] for i in range(len(h)) for t in [np.linspace(0, 1, 30, endpoint=False)]])
    a = np.arctan2(d[:, 1] - c[1], d[:, 0] - c[0]); r = np.hypot(d[:, 1] - c[1], d[:, 0] - c[0]); o = np.argsort(a)
    at = np.linspace(-math.pi, math.pi, n, endpoint=False)
    rr = np.interp(at, a[o], r[o], period=2 * math.pi)
    return [[float(c[0] + q * math.cos(t)), float(c[1] + q * math.sin(t))] for q, t in zip(rr, at)]
gr = []
for z in np.linspace(gp[:, 2].max() - 0.004, gp[:, 2].min() + 0.004, 24):
    seg = slice_segs(['Gear_Main'], 2, z); p = seg.reshape(-1, 2); p = p[p[:, 1] > 0]
    if len(p) >= 3: gr.append({'z': float(z), 'xy': hull_ring(p)})
R['gear_rings'] = gr
tr2 = []
for x in np.linspace(tg[:, 0].max() - 0.004, tg[:, 0].min() + 0.004, 24):
    seg = slice_segs(['Gear_Tail'], 0, x); p = seg.reshape(-1, 2)
    if len(p) >= 3: tr2.append({'x': float(x), 'yz': hull_ring(p)})
R['tail_rings'] = tr2
# aileron spades: aileron material hanging below the wing's lower surface
av = T['Aileron_L'][0]; W_ = R['wing']
def dn_at(y, x):
    w = min(W_, key=lambda t: abs(t['s'] - y))
    return np.interp(-x, [-(w['xle'] - f * (w['xle'] - w['xte'])) for f in FR], w['dn'])
low = np.array([q for q in av if q[2] < dn_at(q[1], q[0]) - 0.015])
if len(low):
    xz = low[:, [0, 2]]; hh = xz[convex_hull_2d([tuple(q) for q in xz])]
    R['spade'] = {'y': [float(low[:, 1].min()), float(low[:, 1].max())], 'xz': hh.tolist()}
    parts = []      # plate (well below the wing) and strut, measured separately
    for m in (low[:, 2] < -0.15, low[:, 2] >= -0.15):
        q = low[m]
        if len(q) >= 3:
            xz = q[:, [0, 2]]; parts.append({'y': [float(q[:, 1].min()), float(q[:, 1].max())], 'xz': xz[convex_hull_2d([tuple(a) for a in xz])].tolist()})
    R['spade_parts'] = parts
    pl = low[low[:, 2] < -0.15]; slabs = []          # plate is tilted: measure it in spanwise slabs
    ed = np.linspace(pl[:, 1].min(), pl[:, 1].max(), 7)
    for a_, b_ in zip(ed[:-1], ed[1:]):
        q = pl[(pl[:, 1] >= a_ - 0.01) & (pl[:, 1] <= b_ + 0.01)]
        if len(q) < 3: continue
        c = q[:, [0, 2]].mean(0); ring = []
        for ang in np.linspace(0, 2 * np.pi, 24, endpoint=False):
            dvec = np.array([np.cos(ang), np.sin(ang)]); pj = (q[:, [0, 2]] - c) @ dvec
            k = pj.argmax(); ring.append([float(q[k, 0]), float(q[k, 1]), float(q[k, 2])])
        slabs.append(ring)
    R['spade_slabs'] = slabs
# main wheel tyre profile: max radius vs y
mwv = mw; cy = R['main_wheel']['c']; prof = []
for y in np.linspace(mwv[:, 1].min(), mwv[:, 1].max(), 13):
    q = mwv[np.abs(mwv[:, 1] - y) < 0.006]
    if len(q): prof.append([float(y - cy[1]), float(np.hypot(q[:, 0] - cy[0], q[:, 2] - cy[2]).max())])
R['wheel_profile'] = prof
json.dump(R, open(os.path.join(HERE, 'ea300_ref_tables.json'), 'w'))
print('MEASURED', len(rings), 'fuselage rings', len(R['wing']), 'wing', len(R['stab']), 'stab', len(R['fin']), 'fin', len(pr), 'pant rings')
