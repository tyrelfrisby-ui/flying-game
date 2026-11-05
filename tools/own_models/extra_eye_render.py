"""Extra 300 seat-eye and close-up renders in Blender (quick iteration; the Unity CockpitEyeShots are the evidence).

  /Applications/Blender.app/Contents/MacOS/Blender -b -P tools/own_models/extra_eye_render.py -- GLB OUTDIR [SHOT ...]

Each SHOT is name=ex,ey,ez:tx,ty,tz[:lens] in BUILDER metres (x forward, y LEFT, z up), the camera at e looking at t.
With no SHOT the default set is rendered: both seat eyes (config eyes as placed, check_eye_interiors.py) looking ahead
and to each side, plus the spec close-ups. Single-sided materials are culled the way glTFast/Unity cull them.
"""
import bpy, sys, math
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
glb, out, shots = args[0], args[1], args[2:]
EYE_R, EYE_F = (-1.622, 0.0, 0.526), (-0.703, 0.0, 0.537)
DEFAULT = [
    ('front-eye-ahead', EYE_F, (0.40, 0.0, 0.25), 18),
    ('front-eye-left', EYE_F, (-0.45, 0.60, 0.15), 18),
    ('front-eye-right', EYE_F, (-0.45, -0.60, 0.15), 18),
    ('rear-eye-ahead', EYE_R, (-0.40, 0.0, 0.30), 18),
    ('rear-eye-left', EYE_R, (-1.35, 0.60, 0.10), 18),
    ('rear-eye-right', EYE_R, (-1.35, -0.60, 0.10), 18),
]


def B(p): return Vector((-p[0], -p[1], p[2]))     # builder -> Blender (glTF import: x=-bx, y=-by, z=bz)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
sc = bpy.context.scene
for m in bpy.data.materials:
    if hasattr(m, 'use_backface_culling'): pass  # the importer sets it from doubleSided
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.68, 0.88, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 1.0
sd = bpy.data.lights.new('sun', 'SUN'); sd.energy = 3.0; so = bpy.data.objects.new('sun', sd); sc.collection.objects.link(so)
so.rotation_euler = (math.radians(35), 0, math.radians(200))
# ground plane so a see-through shows as ground, not sky
bpy.ops.mesh.primitive_plane_add(size=400, location=(0, 0, -1.4))
gm = bpy.data.materials.new('ground'); gm.diffuse_color = (0.35, 0.45, 0.30, 1); bpy.context.object.data.materials.append(gm)
cd = bpy.data.cameras.new('cam'); cd.clip_start = 0.02; cam = bpy.data.objects.new('cam', cd); sc.collection.objects.link(cam); sc.camera = cam
try: sc.render.engine = 'BLENDER_EEVEE_NEXT'
except Exception: sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x, sc.render.resolution_y = 1600, 1000; sc.view_settings.view_transform = 'Standard'
todo = DEFAULT
if shots:
    todo = []
    for s in shots:
        name, rest = s.split('=')
        parts = rest.split(':')
        e = tuple(float(v) for v in parts[0].split(',')); t = tuple(float(v) for v in parts[1].split(','))
        todo.append((name, e, t, float(parts[2]) if len(parts) > 2 else 18))
for name, e, t, lens in todo:
    cd.lens = lens
    cam.location = B(e); d = B(t) - B(e); cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = out + '/' + name + '.png'; bpy.ops.render.render(write_still=True)
