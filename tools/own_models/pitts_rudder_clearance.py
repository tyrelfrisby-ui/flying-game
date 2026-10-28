"""Rudder / elevator clearance check for pitts-s2b-like-own*.glb (imported frame: nose -x, port -y, up +z).
Rotates the Rudder about its hinge (vertical line x = +3.30, y = 0) and the elevators about theirs (x = +3.30, z = 0.071)
and reports the minimum surface-to-surface distance (BVH, vertex->surface both ways) plus a top-view render with the
rudder shown at full left and full right (two copies) and the closest-point pairs projected to pixels (JSON).
  blender -b --python tools/own_models/pitts_rudder_clearance.py -- model.glb outprefix [rudder_deg=30.75] [elev_deg=27.75]"""
import bpy, sys, math, json, mathutils
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
from bpy_extras.object_utils import world_to_camera_view
a = sys.argv[sys.argv.index('--') + 1:]; G, OUT = a[0], a[1]
DR = float(a[2]) if len(a) > 2 else 30.75; DE = float(a[3]) if len(a) > 3 else 27.75
HX, EZ = 3.30, 0.071
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=G)
sc = bpy.context.scene; dg = bpy.context.evaluated_depsgraph_get()
obj = {o.name: o for o in sc.objects if o.type == 'MESH'}
def geo(n):
    o = obj[n]; m = o.evaluated_get(dg).to_mesh()
    return [o.matrix_world @ v.co for v in m.vertices], [tuple(p.vertices) for p in m.polygons]
RU = geo('Rudder'); EL = {s: geo('Elevator_' + s) for s in 'LR'}
FIX = {n: geo(n) for n in ('Fin', 'Stab', 'Fuselage_Painted', 'Wire_Bracing', 'Gear_Main') if n in obj}
def rot_z(d): return Matrix.Translation((HX, 0, 0)) @ Matrix.Rotation(math.radians(d), 4, 'Z') @ Matrix.Translation((-HX, 0, 0))
def rot_y(d): return Matrix.Translation((HX, 0, EZ)) @ Matrix.Rotation(math.radians(d), 4, 'Y') @ Matrix.Translation((-HX, 0, -EZ))
def xf(g, M): return [M @ v for v in g[0]], g[1]
def dist(ga, gb):
    ta, tb = BVHTree.FromPolygons(*ga), BVHTree.FromPolygons(*gb)
    if ta.overlap(tb): return 0.0, None, None
    best = (1e9, None, None)
    for v in ga[0]:
        p, _, _, d = tb.find_nearest(v)
        if p is not None and d < best[0]: best = (d, v, p)
    for v in gb[0]:
        p, _, _, d = ta.find_nearest(v)
        if p is not None and d < best[0]: best = (d, p, v)
    return best
# rudder nose = half-cylinder about the hinge (unchanged by deflection, sits inside the fin / tail post by design):
# against the fixed parts only the swinging skins count; vertices within the local nose radius of the axis are skipped
RHO = {}
for v in RU[0]:
    if v.x < HX + 0.0005:                                  # imported frame: forward = -x
        k = round(v.z, 3); RHO[k] = max(RHO.get(k, 0.0), math.hypot(v.x - HX, v.y))
def rho_at(z):
    if not RHO: return 0.0
    ks = sorted(RHO); return RHO[min(ks, key=lambda k: abs(k - z))]
def off_axis(v): return math.hypot(v.x - HX, v.y) > rho_at(v.z) + 0.0005
def fixed_check(R, g):
    """swinging rudder skins (faces with any vertex off the nose cylinder) vs a fixed part: BVH triangle overlap
    (any = intersection) and the minimum vertex->surface distance both ways (nose-cylinder vertices skipped)"""
    body = [f for f in R[1] if any(off_axis(R[0][i]) for i in f)] if RHO else R[1]
    tR, tg = BVHTree.FromPolygons(R[0], body), BVHTree.FromPolygons(*g); clr = 1e9
    for A, T in ((R[0], tg), (g[0], tR)):
        for v in A:
            if RHO and not off_axis(v): continue
            p, _, _, d = T.find_nearest(v)
            if p is not None: clr = min(clr, d)
    return dict(mm=round(1000 * clr, 1), intersecting_tris=len(tR.overlap(tg)))
res = {'rudder_deg': DR, 'elev_deg': DE, 'cases': []}
for dr in (-DR, DR):
    R = xf(RU, rot_z(dr))
    for de in (-DE, -DE / 2, 0.0, DE / 2, DE):
        for s in 'LR':
            d, pa, pb = dist(R, xf(EL[s], rot_y(de)))
            res['cases'].append(dict(rudder=dr, elev=de, side=s, mm=round(1000 * d, 1),
                                     pr=list(pa) if pa else None, pe=list(pb) if pb else None))
    for n, g in FIX.items():
        res['cases'].append(dict(rudder=dr, elev=None, side=n, **fixed_check(R, g)))
def contact(sign, de):
    lo, hi = 0.0, 60.0
    E = [xf(EL[s], rot_y(de)) for s in 'LR']
    for _ in range(18):
        m = 0.5 * (lo + hi); R = xf(RU, rot_z(sign * m))
        if min(dist(R, e)[0] for e in E) > 1e-4: lo = m
        else: hi = m
    return round(lo, 2)
res['contact_deg'] = {('left' if s < 0 else 'right') + '@elev%+.2f' % de: contact(s, de) for s in (-1, 1) for de in (-DE, 0.0, DE)}
el = [c for c in res['cases'] if c['elev'] is not None]
res['min_mm'] = min(c['mm'] for c in el)
# render: rudder at -DR (original object) and a copy at +DR, top ortho, nose up
ro = obj['Rudder']; cp = ro.copy(); cp.data = ro.data.copy(); sc.collection.objects.link(cp)
M0 = ro.matrix_world.copy(); ro.matrix_world = rot_z(-DR) @ M0; cp.matrix_world = rot_z(DR) @ M0
for e in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try: sc.render.engine = e; break
    except Exception: pass
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.82, 0.86, 0.9, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 0.9
for r_, e_ in (((math.radians(40), 0, math.radians(-150)), 3.0), ((math.radians(150), 0, math.radians(60)), 1.2)):
    l = bpy.data.lights.new('s', 'SUN'); l.energy = e_; o = bpy.data.objects.new('s', l); o.rotation_euler = r_; sc.collection.objects.link(o)
sc.view_settings.view_transform = 'Standard'
c = bpy.data.cameras.new('c'); c.type = 'ORTHO'; c.ortho_scale = 1.5
co = bpy.data.objects.new('c', c); sc.collection.objects.link(co); co.location = (3.50, 0, 20)
co.rotation_euler = (0, 0, math.radians(90)); bpy.context.view_layer.update()   # nose up, starboard right
sc.camera = co; RX = RY = 1600; sc.render.resolution_x, sc.render.resolution_y = RX, RY
sc.render.filepath = OUT + '-deflect.png'; bpy.ops.render.render(write_still=True)
def px(p):
    v = world_to_camera_view(sc, co, Vector(p)); return [round(v.x * RX, 1), round((1 - v.y) * RY, 1)]
for cse in res['cases']:
    if cse.get('pr'): cse['px'] = [px(cse['pr']), px(cse['pe'])]
res['px_hinge'] = px((HX, 0, EZ)); res['px_per_m'] = RX / 1.5
res['px_nose_dir'] = [b - a_ for a_, b in zip(px((3.5, 0, 0)), px((3.0, 0, 0)))]
res['px_port_dir'] = [b - a_ for a_, b in zip(px((3.5, 0, 0)), px((3.5, -0.5, 0)))]
json.dump(res, open(OUT + '-clearance.json', 'w'), indent=1)
print('CLEARANCE_MIN_MM', res['min_mm'], 'CONTACT', res['contact_deg'])
for cse in res['cases']: print('CASE', cse['rudder'], cse['elev'], cse['side'], cse['mm'], cse.get('intersecting_tris', ''))
for dr in (0.0,):
    for n, g in FIX.items(): print('NEUTRAL', n, fixed_check(xf(RU, rot_z(0.0)), g))
