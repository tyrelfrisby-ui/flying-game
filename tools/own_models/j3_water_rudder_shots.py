"""J-3 FLOATS water-rudder retract shots (Ty 2026-11-02, t278u): the red WATER RUDDER handle let go (standing on its
floor fairlead left of the rear stick, blade down) and hung on the hook on the back of the front seat (blade up).

Poses the GLB the way the game does, from the node extras build_j3_cub.py writes: WaterRudder_Handle moves along
rig_move_axis_gltf by rig_map_m (water_rudder_up 0 -> 1), WaterRudder_Blade_L turns retract_deg about
retract_axis_gltf, and WaterRudder_Cable (unit length up its local Z) is aimed at the handle's ferrule and stretched to
it (CockpitWaterRudder does the same in Unity). Eye and frame as j3_cockpit_shots.py (the rear eye is the same on the
floats model: check_eye_interiors.airframe_offset < 0.01 mm).

  blender -b --python tools/own_models/j3_water_rudder_shots.py -- <floats.glb> <out_dir> [shot ...]
"""
import bpy, sys, math
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index('--') + 1:]
glb, out, only = argv[0], argv[1], set(argv[2:])
EYE = Vector((0.7774, 0.0, 0.5067))
def D(xd, y, z): return Vector((xd - 1.817, -y, z + 0.135))
def G(v): return Vector((v[0], -v[2], v[1]))                           # glTF direction -> Blender

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
sc = bpy.context.scene
ob = bpy.data.objects
H, C, BL = ob['WaterRudder_Handle'], ob['WaterRudder_Cable'], ob['WaterRudder_Blade_L']
bpy.context.view_layer.update()
H0 = H.matrix_world.copy(); BL0 = BL.matrix_world.copy()
FAIR = C.matrix_world.translation.copy()
MOVE = G(H['rig_move_axis_gltf']).normalized() * float(H['rig_map_m'][-1])
RAX = G(BL['retract_axis_gltf']).normalized(); RDEG = float(BL['retract_deg'])

def pose(up):
    H.matrix_world = Matrix.Translation(MOVE * up) @ H0
    o = BL0.translation
    BL.matrix_world = Matrix.Translation(o) @ Matrix.Rotation(math.radians(RDEG * up), 4, RAX) @ Matrix.Translation(-o) @ BL0
    bpy.context.view_layer.update()
    tip = H.matrix_world.translation; d = tip - FAIR; ln = max(d.length, 1e-4)
    q = Vector((0, 0, 1)).rotation_difference(d.normalized() if d.length > 1e-6 else Vector((0, 0, 1)))
    C.matrix_world = Matrix.Translation(FAIR) @ q.to_matrix().to_4x4() @ Matrix.Diagonal((1, 1, ln, 1))
    bpy.context.view_layer.update()
    return ln

w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes['Background']; bg.inputs[0].default_value = (0.62, 0.72, 0.85, 1); bg.inputs[1].default_value = 1.0
sd = bpy.data.lights.new('sun', 'SUN'); sd.energy = 3.0; sd.angle = math.radians(3)
so = bpy.data.objects.new('sun', sd); sc.collection.objects.link(so); so.rotation_euler = (math.radians(35), math.radians(-15), math.radians(-120))
cd = bpy.data.cameras.new('cam'); cd.sensor_fit = 'HORIZONTAL'; cd.clip_start = 0.02
cam = bpy.data.objects.new('cam', cd); sc.collection.objects.link(cam); sc.camera = cam
sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 32
try: sc.eevee.use_raytracing = True
except AttributeError: pass
sc.render.resolution_x, sc.render.resolution_y = 1200, 900; sc.render.image_settings.color_mode = 'RGB'
sc.view_settings.view_transform = 'Standard'; sc.view_settings.exposure = 0.6

bl = BL0.translation
SHOTS = [  # name, water_rudder_up, camera, target, hfov
    ('01-rear-seat-let-go', 0, EYE, D(2.20, 0.10, -0.38), 96.0),
    ('02-rear-seat-hooked', 1, EYE, D(2.20, 0.10, -0.38), 96.0),
    ('03-closeup-floor-let-go', 0, EYE, D(2.27, 0.09, -0.47), 16.0),
    ('04-closeup-hook-hooked', 1, EYE, D(2.16, 0.11, -0.24), 18.0),
    ('05-detail-floor-let-go', 0, D(2.52, 0.30, -0.25), D(2.25, 0.09, -0.47), 34.0),
    ('06-detail-hook-hooked', 1, D(2.52, 0.30, -0.10), D(2.16, 0.11, -0.26), 34.0),
    ('07-blade-down', 0, bl + Vector((0.25, -1.15, 0.10)), bl + Vector((0.0, 0.0, -0.05)), 30.0),
    ('08-blade-up', 1, bl + Vector((0.25, -1.15, 0.10)), bl + Vector((0.0, 0.0, -0.05)), 30.0),
]
for name, up, at, target, fov in SHOTS:
    if only and name not in only: continue
    ln = pose(up)
    cd.angle = math.radians(fov)
    cam.location = at; cam.rotation_euler = (target - at).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = '%s/%s.png' % (out, name)
    bpy.ops.render.render(write_still=True)
    print('SHOT', name, 'water_rudder_up', up, 'cable %.3f m' % ln, 'handle at', tuple(round(c, 3) for c in H.matrix_world.translation))
