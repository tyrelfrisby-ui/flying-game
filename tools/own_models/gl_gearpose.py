"""Pose the Great Lakes own GLB's rigged main gear and render side/front views (+ gear close-ups).
  blender -b --python tools/own_models/gl_gearpose.py -- model.glb outprefix state   (state: extended|static|full|<deg>)
Rig (written by build_great_lakes.py): Gear_Pivot_<s> at the axle hinge, hinge axis = Pivot -> Gear_HingeAxis_<s>;
children Gear_Axle/Wheel/Wheel_Pant/Oleo_Cylinder_<s>; Oleo_Shaft_<s> is fixed at Gear_OleoTop_<s>. The cylinder and
shaft are re-aimed at each other (what a LookAt constraint does in the game).
v12: the GLB is exported in the STATIC pose (pivot extra hinge_deg_as_exported, angles measured from extended), so the
pivot is rotated by (target - as_exported); 'extended' therefore drops the wheels from the exported stance."""
import bpy, sys, math, mathutils
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index('--') + 1:]; G, OUT, STATE = a[0], a[1], a[2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=G)
sc = bpy.context.scene; O = bpy.data.objects
for e in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try: sc.render.engine = e; break
    except Exception: pass
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.82, 0.86, 0.9, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 0.9
for rot, e in (((math.radians(40), 0, math.radians(-150)), 3.0), ((math.radians(150), 0, math.radians(60)), 1.2)):
    l = bpy.data.lights.new('s', 'SUN'); l.energy = e; o = bpy.data.objects.new('s', l); o.rotation_euler = rot; sc.collection.objects.link(o)
sc.view_settings.view_transform = 'Standard'
def W(n): return O[n].matrix_world.translation.copy()
for s in ('L', 'R'):
    piv = O['Gear_Pivot_' + s]
    th = {'extended': 0.0, 'full': piv['hinge_deg_full'], 'static': piv.get('hinge_deg_static', 0.0)}.get(STATE)
    th0 = float(piv.get('hinge_deg_as_exported', 0.0))
    th = math.radians((float(STATE) if th is None else th) - th0)
    bpy.context.view_layer.update()
    H, C, T = W('Gear_Pivot_' + s), W('Gear_HingeAxis_' + s), W('Gear_OleoTop_' + s)
    cyl, sh, wh = O['Oleo_Cylinder_' + s], O['Oleo_Shaft_' + s], O['Wheel_' + s]
    A0 = W(cyl.name); L0 = (T - A0).length
    ax = (C - H).normalized()
    def try_rot(sign):
        R = Matrix.Rotation(sign * th, 4, ax); return (H + (R.to_3x3() @ (A0 - H)))
    up = 1 if (H + (Matrix.Rotation(0.1, 4, ax).to_3x3() @ (A0 - H))).z > (H + (Matrix.Rotation(-0.1, 4, ax).to_3x3() @ (A0 - H))).z else -1
    sign = up  # positive th = compress (wheel up); negative th = extend from the exported static stance
    R = Matrix.Translation(H) @ Matrix.Rotation(sign * th, 4, ax) @ Matrix.Translation(-H)
    piv.matrix_world = R @ piv.matrix_world; bpy.context.view_layer.update()
    A1 = W(cyl.name)
    # re-aim the cylinder at T and the shaft at A (correct the small angle error of a rigid rotation)
    d_cur = (R.to_3x3() @ (T - A0)).normalized(); d_new = (T - A1).normalized()
    q = d_cur.rotation_difference(d_new)
    mw = cyl.matrix_world.copy(); cyl.matrix_world = Matrix.Translation(A1) @ q.to_matrix().to_4x4() @ Matrix.Translation(-A1) @ mw
    q2 = (A0 - T).normalized().rotation_difference((A1 - T).normalized())
    mw = sh.matrix_world.copy(); sh.matrix_world = Matrix.Translation(T) @ q2.to_matrix().to_4x4() @ Matrix.Translation(-T) @ mw
    bpy.context.view_layer.update()
    print('POSE %s %s hinge %.2f deg (sign %+d) strut %.3f -> %.3f m (%.2f in) wheel dz %+.3f  aim-correction %.2f deg'
          % (s, STATE, math.degrees(th) + th0, sign, L0, (T - A1).length, (L0 - (T - A1).length) / 0.0254,
             (A1 - A0).z * 0 + (wh.matrix_world.translation.z - wh.matrix_world.translation.z), math.degrees(d_cur.angle(d_new))))
def cam(name, loc, look, scale, res, roll=None):
    c = bpy.data.cameras.new(name); c.type = 'ORTHO'; c.ortho_scale = scale
    o = bpy.data.objects.new(name, c); sc.collection.objects.link(o); o.location = loc
    o.rotation_euler = (Vector(look) - Vector(loc)).to_track_quat('-Z', roll or 'Y').to_euler()
    sc.camera = o; sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.filepath = OUT + '-' + STATE + '-' + name + '.png'; bpy.ops.render.render(write_still=True)
cam('side', (2.0, -20, -0.30), (2.0, 0, -0.30), 7.0, (1600, 1000))
cam('front', (-20, 0, -0.30), (0, 0, -0.30), 8.8, (1600, 900))
cam('gear-side', (0.4, -20, -1.05), (0.4, 0, -1.05), 2.2, (1100, 1000))
cam('gear-front', (-20, 0, -1.05), (0, 0, -1.05), 2.6, (1300, 1000))
