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
CX = 0.95
cam('top', (CX, 0, 20), (CX, 0, 0), 6.6, (1600, 1600), 'X')
cam('bottom', (CX, 0, -20), (CX, 0, 0), 6.6, (1600, 1600), 'X')
cam('side', (CX, -20, -0.25), (CX, 0, -0.25), 6.2, (1600, 1000))
cam('front', (-20, 0, -0.25), (0, 0, -0.25), 6.6, (1600, 900))
cam('iso', (-8.5, -9.0, 4.0), (CX, 0, -0.2), 7.6, (1600, 1100))
cam('rudder', (3.45, -12, 0.25), (3.45, 0, 0.25), 1.5, (1000, 1100))
cam('under', (-5.5, -7.5, -5.5), (CX, 0, -0.2), 7.4, (1600, 1100))
cam('cockpit', (3.4, -1.5, 2.2), (0.85, 0, -0.15), 2.4, (1400, 1000))
cam('nose', (-5.0, -1.6, 0.2), (-1.5, 0, 0.0), 1.6, (1200, 900))
# perspective approximately from the N31DG photo position (low, front-left, ~45 deg)
cam('lowfront', (-5.2, -4.4, -1.75), (0.15, 0, -0.75), 0, (1600, 1100), persp=45)
cam('gear', (-3.2, -1.2, -1.95), (-0.25, 0, -0.85), 0, (1400, 1000), persp=40)
cam('prop', (-3.1, -1.5, 0.55), (-1.62, 0, 0.0), 0, (1400, 1000), persp=45)
cam('photo', (-6.6, -6.2, -0.35), (0.7, 0, -0.15), 0, (1224, 1000), persp=50)
