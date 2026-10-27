import bpy, sys, math, mathutils
a = sys.argv[sys.argv.index('--') + 1:]; G, OUT = a[0], a[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=G)
sc = bpy.context.scene
try: sc.render.engine = 'BLENDER_EEVEE_NEXT'
except Exception: sc.render.engine = 'BLENDER_EEVEE'
sc.render.film_transparent = False
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.82, 0.86, 0.9, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 0.9
for rot, e in (((math.radians(40), 0, math.radians(30)), 3.0), ((math.radians(150), 0, math.radians(-120)), 1.2)):
    l = bpy.data.lights.new('s', 'SUN'); l.energy = e; o = bpy.data.objects.new('s', l); o.rotation_euler = rot; sc.collection.objects.link(o)
sc.view_settings.view_transform = 'Standard'
def cam(name, loc, look, scale, res, roll=None, persp=False, target=None):
    c = bpy.data.cameras.new(name); c.type = 'ORTHO'; c.ortho_scale = scale
    o = bpy.data.objects.new(name, c); sc.collection.objects.link(o); o.location = loc
    d = mathutils.Vector(look) - mathutils.Vector(loc)
    o.rotation_euler = d.to_track_quat('-Z', roll or 'Y').to_euler()
    sc.camera = o; sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.filepath = OUT + '-' + name + '.png'; bpy.ops.render.render(write_still=True)
cam("top", (0.6, 0, 20), (0.6, 0, 0), 9.4, (1600, 1300), 'X')
cam("bottom", (0.6, 0, -20), (0.6, 0, 0), 9.4, (1600, 1300), 'X')
cam('side', (1.0, 20, 0.2), (1.0, 0, 0.2), 9.6, (1600, 900))
cam('iso', (-10.4, 11, 7), (0.6, 0, 0), 9.6, (1600, 1100))
cam('tail', (4.3, 12, 0.6), (4.3, 0, 0.6), 2.0, (1200, 900))
cam('tailr', (4.3, -12, 0.6), (4.3, 0, 0.6), 2.0, (1200, 900))
cam('prop', (-12, 6, 2), (-2.3, 0, 0.0), 2.6, (1200, 1000))
