"""Matched-view renders of pitts-s2b-like-own*.glb for reference comparison (v9).
  blender -b --python tools/own_models/pitts_refviews.py -- model.glb outprefix view[,view...]
Cameras are given in MODEL coordinates (nose +x, port +y, up +z); the glTF import frame is x' = -x, y' = -y."""
import bpy, sys, math, mathutils
a = sys.argv[sys.argv.index('--') + 1:]; G, OUT = a[0], a[1]
ONLY = a[2].split(',') if len(a) > 2 else None
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=G)
sc = bpy.context.scene
for e in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try: sc.render.engine = e; break
    except Exception: pass
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (1, 1, 1, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 1.0
for rot, e in (((math.radians(40), 0, math.radians(-150)), 2.6), ((math.radians(150), 0, math.radians(60)), 1.2)):
    l = bpy.data.lights.new('s', 'SUN'); l.energy = e; o = bpy.data.objects.new('s', l); o.rotation_euler = rot; sc.collection.objects.link(o)
sc.view_settings.view_transform = 'Standard'
M = lambda p: (-p[0], -p[1], p[2])
def cam(name, loc, look, res, scale=None, lens=None, up='Y', pitch=0.0, roll=0.0, yaw=0.0):
    if ONLY and name not in ONLY: return
    c = bpy.data.cameras.new(name)
    if lens: c.type = 'PERSP'; c.lens = lens
    else: c.type = 'ORTHO'; c.ortho_scale = scale
    o = bpy.data.objects.new(name, c); sc.collection.objects.link(o); o.location = M(loc)
    d = mathutils.Vector(M(look)) - mathutils.Vector(M(loc))
    o.rotation_euler = d.to_track_quat('-Z', up).to_euler()
    if pitch or roll or yaw:   # aircraft attitude (deg, nose-up / right-wing-down / nose-left) emulated by moving the camera inversely
        bpy.context.view_layer.update()
        Rp = mathutils.Matrix.Rotation(math.radians(pitch), 4, 'Y')
        Rr = mathutils.Matrix.Rotation(math.radians(roll), 4, 'X')
        Ry = mathutils.Matrix.Rotation(math.radians(yaw), 4, 'Z')
        o.matrix_world = (Ry @ Rp @ Rr).inverted() @ o.matrix_world
    sc.camera = o; sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.filepath = OUT + '-' + name + '.png'; bpy.ops.render.render(write_still=True)
# ortho nose views: 1000 px per metre. side: centre model (x 0.9, z -0.25), 2.0 m wide x 1.6 m high, nose LEFT (port side)
cam('nside', (0.9, 20, -0.25), (0.9, 0, -0.25), (2000, 1600), scale=2.0)
# top: centre (x 1.0, y 0), nose UP in the image, port LEFT  (camera up vector = +x model -> -x import)
cam('ntop', (1.0, 0, 20), (1.0, 0, 0), (1600, 1400), scale=1.6, up='X')
cam('nbot', (1.0, 0, -20), (1.0, 0, 0), (1600, 1400), scale=1.6, up='X')
cam('pside', (0.30, 20, -1.12), (0.30, 0, -1.12), (1400, 700), scale=1.4)
import json, os
J = os.environ.get('PV_PERSP')
if J:
    for v in json.load(open(J)):
        cam(v['name'], v['loc'], v['look'], tuple(v['res']), lens=v['lens'], pitch=v.get('pitch', 0), roll=v.get('roll', 0), yaw=v.get('yaw', 0))
