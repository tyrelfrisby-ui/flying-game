"""J-3 cockpit shots from the approved REAR-seat eye (Ty 2026-11-02 cockpit changes; docs/pending/j3-cockpit/).

Every shot but the cutaways is taken from the exact rear eye the game uses (configs/aircraft/j3-cub-like.json
loading.seats[rear].eye, carried into the GLB frame the way AirframeModels.Place places the model; see
check_eye_interiors.py). Only the view DIRECTION changes per shot, like turning your head; the wide shots use a
Quest 3S-like 96 deg horizontal field of view, the close-ups a narrow one from the same eye (a zoom, not a move).
The cutaway (04b) is a camera low in the front footwell (where the front pilot's shins are), looking forward and a
little up under the panel, because the rear eye cannot see the whole of the tube work under the tank.

Targets are given in the build script's drawing frame (xd m aft of the prop hub, y to port, z up from the thrust
line) and turned into the imported frame here: Blender = (xd - 1.817, -y, z + 0.135).

  blender -b --python tools/own_models/j3_cockpit_shots.py -- <model.glb> <out_dir> <suffix> [shot ...]
"""
import bpy, sys, math, os
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
glb, out, suffix, only = argv[0], argv[1], argv[2], set(argv[3:])

# Rear eye in the GLB's glTF frame (check_eye_interiors.py / sights.placement inverted, scale 0.9905 included):
# glTF (0.7774, 0.5067, 0.0) -> Blender (0.7774, 0.0, 0.5067).
EYE = Vector((0.7774, 0.0, 0.5067))
def D(xd, y, z): return Vector((xd - 1.817, -y, z + 0.135))

QUEST_HFOV = 96.0
SHOTS = [  # name, camera, target, horizontal fov (deg)
    ('01-top-tubes', EYE, D(2.05, 0.0, 0.66), QUEST_HFOV),
    ('02-mag-switch', EYE, D(1.66, 0.25, 0.55), QUEST_HFOV),
    ('03-carb-heat', EYE, D(1.80, -0.26, -0.22), QUEST_HFOV),
    ('04-under-tank-tubes', EYE, D(1.10, 0.0, -0.20), QUEST_HFOV),
    ('04b-under-tank-tubes-cutaway', D(1.50, 0.0, -0.30), D(0.95, 0.0, -0.17), QUEST_HFOV),
    ('05-lettering', EYE, D(1.40, 0.04, -0.02), QUEST_HFOV),
    ('06-trim-crank', EYE, D(1.72, 0.29, -0.20), QUEST_HFOV),
    ('07-fuel-valve', EYE, D(1.53, 0.29, -0.10), QUEST_HFOV),
    ('closeup-panel-lettering', EYE, D(1.233, 0.0, 0.098), 24.0),
    ('closeup-fuel-knob', EYE, D(1.53, 0.285, -0.10), 13.0),
    ('closeup-mag-switch', EYE, D(1.743, 0.29, 0.575), 14.0),
    ('closeup-trim-crank', EYE, D(1.865, 0.28, -0.22), 16.0),
    ('closeup-carb-heat', EYE, D(1.455, -0.28, -0.175), 14.0),
    ('closeup-throttle-front', EYE, D(1.335, 0.29, -0.04), 14.0),
]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
sc = bpy.context.scene
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes['Background']; bg.inputs[0].default_value = (0.62, 0.72, 0.85, 1); bg.inputs[1].default_value = 1.0
sd = bpy.data.lights.new('sun', 'SUN'); sd.energy = 3.0; sd.angle = math.radians(3)
so = bpy.data.objects.new('sun', sd); sc.collection.objects.link(so); so.rotation_euler = (math.radians(35), math.radians(-15), math.radians(-120))
cd = bpy.data.cameras.new('cam'); cd.sensor_fit = 'HORIZONTAL'; cd.clip_start = 0.02
cam = bpy.data.objects.new('cam', cd); sc.collection.objects.link(cam); sc.camera = cam
if os.environ.get('J3_SHOT_ENGINE', 'eevee') == 'cycles':
    sc.render.engine = 'CYCLES'; sc.cycles.samples = 48; sc.cycles.use_denoising = True; sc.cycles.device = 'CPU'
else:
    sc.render.engine = 'BLENDER_EEVEE'; sc.eevee.taa_render_samples = 32
    try: sc.eevee.use_raytracing = True
    except AttributeError: pass
sc.render.resolution_x, sc.render.resolution_y = 1200, 900; sc.render.image_settings.color_mode = 'RGB'; sc.render.image_settings.compression = 100; sc.view_settings.view_transform = 'Standard'
sc.view_settings.exposure = 0.6
for name, at, target, fov in SHOTS:
    if only and name not in only: continue
    cd.angle = math.radians(fov)
    cam.location = at; cam.rotation_euler = (target - at).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = '%s/%s-%s.png' % (out, name, suffix)
    bpy.ops.render.render(write_still=True)
    print('SHOT', name, suffix, 'eye', tuple(round(c, 4) for c in at), 'target', tuple(round(c, 4) for c in target), 'hfov', fov)
