"""Pose the v11 Great Lakes ailerons and render slave-strut close-ups (imported frame: nose -x, port -y, up +z).
  blender -b --python tools/own_models/gl_aileronpose.py -- model.glb roll outprefix [cams]
roll in [-1, 1]: +1 = full right roll (port lower TE down, starboard lower TE up, to the rigged stops in the lower
pivot extras travel_te_down_deg / travel_te_up_deg: v12 21.0 / 24, TCDS A18EA 22 / 24 +-1). The lower
ailerons are driven; each upper aileron is solved so the slave strut keeps its rest length (pin markers
Strut_Slave_<s>_PinUpper / _PinLower), and Strut_Slave_<s> is moved to the new upper pin and re-aimed at the lower pin.
Prints POSE lines (lower / upper TE-up angles, strut length error)."""
import bpy, sys, math
from mathutils import Matrix, Vector
a = sys.argv[sys.argv.index('--') + 1:]; G, ROLL, OUT = a[0], float(a[1]), a[2]
ONLY = a[3].split(',') if len(a) > 3 else None
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=G)
O = bpy.data.objects; sc = bpy.context.scene; vl = bpy.context.view_layer
def wpos(n): return O[n].matrix_world.translation.copy()
def hinge(k):
    o = wpos('Aileron_%s_Pivot' % k); u = (wpos('Aileron_%s_HingeAxis' % k) - o).normalized()
    return o, u, int(O['Aileron_%s_Pivot' % k].get('te_up_sign', 1))
def R(k, te_up_deg):
    o, u, sg = hinge(k)
    return Matrix.Translation(o) @ Matrix.Rotation(math.radians(sg * te_up_deg), 4, u) @ Matrix.Translation(-o)
pin0 = {s: (wpos('Strut_Slave_%s_PinUpper' % s), wpos('Strut_Slave_%s_PinLower' % s)) for s in 'LR'}
M0 = {s: O['Strut_Slave_' + s].matrix_world.copy() for s in 'LR'}
d = max(-1.0, min(1.0, ROLL))
TU = {s: float(O['Aileron_L%s_Pivot' % s].get('travel_te_up_deg', 24)) for s in 'LR'}    # rigged lower stops (v12)
TD = {s: float(O['Aileron_L%s_Pivot' % s].get('travel_te_down_deg', 22)) for s in 'LR'}
lower = {'L': -TD['L'] * d if d >= 0 else -TU['L'] * d, 'R': TU['R'] * d if d >= 0 else TD['R'] * d}
for s in 'LR':
    pu0, pl0 = pin0[s]; L0 = (pu0 - pl0).length
    Rl = R('L' + s, lower[s]); pl = Rl @ pl0
    f = lambda t: ((R('U' + s, t) @ pu0) - pl).length - L0
    lo, hi = lower[s] - 20, lower[s] + 20; flo = f(lo)
    for _ in range(60):
        m = 0.5 * (lo + hi); fm = f(m)
        if (fm > 0) == (flo > 0): lo, flo = m, fm
        else: hi = m
    tu = 0.5 * (lo + hi); Ru = R('U' + s, tu); pu = Ru @ pu0
    for k, M in (('L' + s, Rl), ('U' + s, Ru)):
        p = O['Aileron_%s_Pivot' % k]; p.matrix_world = M @ p.matrix_world
    q = (pl0 - pu0).rotation_difference(pl - pu)
    O['Strut_Slave_' + s].matrix_world = Matrix.Translation(pu) @ q.to_matrix().to_4x4() @ Matrix.Translation(-pu0) @ M0[s]
    vl.update()
    err = (wpos('Strut_Slave_%s_PinUpper' % s) - wpos('Strut_Slave_%s_PinLower' % s)).length - L0
    mv = (wpos('Strut_Slave_%s_PinLower' % s) - pl0).length
    print('POSE %s roll %+.2f lower TE-up %+.2f upper TE-up %+.2f strut length error %.5f m (lower pin moved %.3f m)' % (s, d, lower[s], tu, err, mv))
for e in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try: sc.render.engine = e; break
    except Exception: pass
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.82, 0.86, 0.9, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 0.9
for rot, e in (((math.radians(40), 0, math.radians(-150)), 3.0), ((math.radians(150), 0, math.radians(60)), 1.2)):
    l = bpy.data.lights.new('s', 'SUN'); l.energy = e; o = bpy.data.objects.new('s', l); o.rotation_euler = rot; sc.collection.objects.link(o)
sc.view_settings.view_transform = 'Standard'
def cam(name, loc, look, scale, res, persp=None, clip=None):
    if ONLY and name not in ONLY: return
    c = bpy.data.cameras.new(name)
    if persp: c.type = 'PERSP'; c.lens = persp
    else: c.type = 'ORTHO'; c.ortho_scale = scale
    if clip: c.clip_start, c.clip_end = clip
    o = bpy.data.objects.new(name, c); sc.collection.objects.link(o); o.location = loc
    o.rotation_euler = (Vector(look) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    sc.camera = o; sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.filepath = OUT + '-' + name + '.png'; bpy.ops.render.render(write_still=True)
cam('port_side', (1.6, -12, 0.0), (1.6, 0, 0.0), 2.3, (1100, 1100), clip=(7.6, 9.9))   # along the span, port wing |y| 2.1-4.4 only
cam('port_aft', (5.4, -6.6, 0.9), (1.55, -2.95, 0.0), 0, (1400, 1000), persp=50)       # 3/4 from aft, outboard
cam('stbd_aft', (5.4, 6.6, 0.9), (1.55, 2.95, 0.0), 0, (1400, 1000), persp=50)
