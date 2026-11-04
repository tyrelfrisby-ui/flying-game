"""The Extra's aileron at its 30 % hinge, deflected, from above-aft — does the nose stand up out of the cove?

blender -b --factory-startup --python tools/own_models/render_aileron_deflect.py -- GLB RIG_JSON NODE OUT_DIR LABEL DEG [DEG ...]

Turns NODE about its rig axis (rig json axis_gltf, through the node's origin, which is the hinge) by each DEG of the rig's
own convention (+ = TE up for Aileron_L; the rig map gives TE down 30 at -30), and renders <label>-<deg>.png from above,
aft and outboard of the aileron's mid-span, and <label>-<deg>-below.png from below.
"""
import bpy, json, sys, math
from pathlib import Path
from mathutils import Vector, Matrix

glb, rigp, node, out, label, *degs = sys.argv[sys.argv.index('--') + 1:]
out = Path(out); out.mkdir(parents=True, exist_ok=True)
rig = next(r for r in json.load(open(rigp))['rig'] if r['node'] == node)
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=glb)
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 24
sc.view_settings.view_transform = 'AgX'; sc.view_settings.exposure = -0.6
sc.world = bpy.data.worlds.new('sky'); sc.world.use_nodes = True
bg = sc.world.node_tree.nodes['Background']; bg.inputs[0].default_value = (.66, .72, .8, 1); bg.inputs[1].default_value = .8
for loc, power in [((-3, -4, 7), 700), ((3, 3, -5), 300), ((3, 3, 5), 450)]:
    l = bpy.data.lights.new('soft', 'AREA'); l.energy = power; l.size = 5
    o = bpy.data.objects.new('soft', l); sc.collection.objects.link(o); o.location = loc
    o.rotation_euler = (Vector((0, 0, 0)) - o.location).to_track_quat('-Z', 'Y').to_euler()
ob = bpy.data.objects[node]
ax = Vector(rig['axis_gltf']); ax = Vector((ax.x, -ax.z, ax.y)).normalized()       # glTF -> Blender (x, -z, y)
rest = ob.matrix_world.copy(); piv = rest.translation.copy()
pts = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
mid = sum(pts, Vector()) / 8
# Camera: aft (+X in Blender: the Extra's nose is -X), outboard and above the aileron's mid-span.
side = 1 if mid.y > 0 else -1


def shoot(cam_loc, target, path, ortho=None):
    d = bpy.data.cameras.new('c'); d.clip_start = .005
    if ortho: d.type = 'ORTHO'; d.ortho_scale = ortho
    else: d.lens = 40
    o = bpy.data.objects.new('c', d); sc.collection.objects.link(o); o.location = cam_loc
    o.rotation_euler = (Vector(target) - o.location).to_track_quat('-Z', 'Y').to_euler(); sc.camera = o
    sc.render.resolution_x, sc.render.resolution_y = 1600, 1000; sc.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


for deg in map(float, degs):
    R = Matrix.Translation(piv) @ Matrix.Rotation(math.radians(deg), 4, ax) @ Matrix.Translation(-piv)
    ob.matrix_world = R @ rest
    shoot(mid + Vector((1.1, side * 0.9, 0.65)), mid, out / f'{label}-{deg:+.0f}.png')
    shoot(mid + Vector((1.1, side * 0.9, -0.65)), mid, out / f'{label}-{deg:+.0f}-below.png')
print('[deflect] rendered', label, degs, 'axis', tuple(round(a, 4) for a in ax), 'pivot', tuple(round(p, 4) for p in piv))
