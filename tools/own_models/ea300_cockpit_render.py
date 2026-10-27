import bpy, sys, math
from mathutils import Vector
glb, out = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
sc = bpy.context.scene
ck = [o for o in sc.objects if o.name.startswith('Cockpit')][0]
bb = [ck.matrix_world @ Vector(v) for v in ck.bound_box]; c = sum(bb, Vector()) / 8
print('CK centre', c, 'min', min(v.x for v in bb), max(v.x for v in bb))
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (0.62, 0.72, 0.85, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 0.9
sd = bpy.data.lights.new('sun', 'SUN'); sd.energy = 3.5; so = bpy.data.objects.new('sun', sd); sc.collection.objects.link(so); so.rotation_euler = (math.radians(40), 0, math.radians(30))
cd = bpy.data.cameras.new('cam'); cd.lens = 35; cam = bpy.data.objects.new('cam', cd); sc.collection.objects.link(cam); sc.camera = cam
try: sc.render.engine = 'BLENDER_EEVEE_NEXT'
except Exception: sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x, sc.render.resolution_y = 1600, 1000; sc.view_settings.view_transform = 'Standard'
def shot(name, off, hide):
    for o in sc.objects:
        if o.type == 'MESH': o.hide_render = any(o.name.startswith(h) for h in hide)
    cam.location = c + Vector(off); d = c - cam.location; cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = out + '/' + name + '.png'; bpy.ops.render.render(write_still=True)
# nose is -x in imported frame (GLB yawed 180)
shot('cockpit-closeup', (-1.3, -2.0, 1.5), [])
shot('cockpit-closeup-rear', (1.6, -1.6, 1.4), [])
shot('cockpit-cutaway', (0.9, -1.5, 1.6), ['Canopy_Glass', 'Canopy_Frame'])
shot('cockpit-cutaway-front', (-1.2, -1.2, 1.4), ['Canopy_Glass', 'Canopy_Frame'])
