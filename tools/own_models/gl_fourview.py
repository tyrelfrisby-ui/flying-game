"""Four-view + close-ups of pitts-s2b-like-own.glb (imported frame: nose -x, port -y, up +z).
  blender -b --python tools/own_models/pitts_fourview.py -- model.glb outprefix"""
import bpy, sys, math, mathutils
a = sys.argv[sys.argv.index('--') + 1:]; G, OUT = a[0], a[1]
ONLY = a[2].split(',') if len(a) > 2 else None
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=G)
sc = bpy.context.scene
for e in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try: sc.render.engine = e; break
    except Exception: pass
sc.render.film_transparent = False
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.82, 0.86, 0.9, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 0.9
for rot, e in (((math.radians(40), 0, math.radians(-150)), 3.0), ((math.radians(150), 0, math.radians(60)), 1.2)):
    l = bpy.data.lights.new('s', 'SUN'); l.energy = e; o = bpy.data.objects.new('s', l); o.rotation_euler = rot; sc.collection.objects.link(o)
sc.view_settings.view_transform = 'Standard'
def cam(name, loc, look, scale, res, roll=None, persp=None):
    if ONLY and name not in ONLY: return
    c = bpy.data.cameras.new(name)
    if persp: c.type = 'PERSP'; c.lens = persp
    else: c.type = 'ORTHO'; c.ortho_scale = scale
    o = bpy.data.objects.new(name, c); sc.collection.objects.link(o); o.location = loc
    d = mathutils.Vector(look) - mathutils.Vector(loc)
    o.rotation_euler = d.to_track_quat('-Z', roll or 'Y').to_euler()
    sc.camera = o; sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.filepath = OUT + '-' + name + '.png'; bpy.ops.render.render(write_still=True)
CX = 2.0
cam('top', (CX, 0, 20), (CX, 0, 0), 8.8, (1600, 1600), 'X')
cam('bottom', (CX, 0, -20), (CX, 0, 0), 8.8, (1600, 1600), 'X')
cam('side', (CX, -20, -0.30), (CX, 0, -0.30), 7.0, (1600, 1000))
cam('front', (-20, 0, -0.30), (0, 0, -0.30), 8.8, (1600, 900))
cam('iso', (-9.0, -11.0, 5.0), (CX, 0, -0.3), 9.6, (1600, 1100))
cam('tail', (4.8, -12, 0.25), (4.8, 0, 0.25), 2.2, (1000, 1100))
cam('nose', (-5.0, -2.2, 0.3), (-0.6, 0, -0.2), 2.4, (1200, 900))
cam('cockpit', (4.6, -2.0, 2.6), (1.4, 0, -0.1), 3.6, (1400, 1000))
cam('photo', (-7.2, -7.4, -0.5), (1.6, 0, -0.25), 0, (1400, 1000), persp=50)
