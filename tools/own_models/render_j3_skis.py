"""CPU-only review of the exported ski GLB; no Unity or shared build paths.

  blender -b --python tools/own_models/render_j3_skis.py        (J3_SKI_RENDER_OUT overrides the directory)

THE DECK-ANGLE PAIR. Ty, 2026-11-10: the straight ski's "pedestal [is] the radius of a normal tire for that
airplane so the aoa on the ground is the same". That is the whole reason the pedestal height is derived rather
than typed (build_j3_skis.py) and asserted rather than eyeballed (check_j3_skis.py) -- but it is also something he
should be able to SEE, so this renders the aeroplane on skis and the same aeroplane on wheels from the identical
orthographic camera, as 'deck-wheels.png' and 'deck-skis.png'. Flick between the two: the fuselage must not move.
"""
import bpy, os, sys
from pathlib import Path
from mathutils import Vector

root = Path(__file__).resolve().parents[2]
out = Path(os.environ.get('J3_SKI_RENDER_OUT', str(Path.home()/'Developer/agent-lanes/renders/j3-skis')))
out.mkdir(parents=True, exist_ok=True)
SKIS = root/'unity/Assets/_Project/Resources/Models/j3-cub-skis-like/j3-cub-skis-like-own.glb'
WHEELS = root/'unity/Assets/_Project/Resources/Models/j3-cub-like/j3-cub-like-own.glb'


def scene(glb):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(glb))
    s = bpy.context.scene
    s.render.engine = 'CYCLES'; s.cycles.device = 'CPU'; s.cycles.samples = 20
    s.render.resolution_percentage = 100; s.render.image_settings.file_format = 'PNG'
    s.world = bpy.data.worlds.new('Review world'); s.world.use_nodes = True
    s.world.node_tree.nodes['Background'].inputs[0].default_value = (.70, .76, .84, 1)
    s.world.node_tree.nodes['Background'].inputs[1].default_value = .8
    for loc, power, size in [((-4, -5, 7), 1500, 5), ((2, 4, 5), 1100, 4)]:
        l = bpy.data.lights.new('Softbox', 'AREA'); l.energy = power; l.size = size
        o = bpy.data.objects.new('Softbox', l); s.collection.objects.link(o)
        o.location = loc; o.rotation_euler = (Vector((0, 0, 0)) - o.location).to_track_quat('-Z', 'Y').to_euler()
    s.view_settings.view_transform = 'AgX'
    return s


def shoot(s, name, loc, target, scale, res):
    c = bpy.data.cameras.new(name); c.type = 'ORTHO'; c.ortho_scale = scale
    o = bpy.data.objects.new(name, c); s.collection.objects.link(o)
    o.location = loc; o.rotation_euler = (Vector(target) - o.location).to_track_quat('-Z', 'Y').to_euler()
    s.camera = o
    s.render.resolution_x, s.render.resolution_y = res
    s.render.filepath = str(out/(name + '.png'))
    bpy.ops.render.render(write_still=True)


# The one camera both aeroplanes are shot from, so the two images overlay exactly.
DECK = ((1.6, -20, -.1), (1.6, 0, -.1), 7.6, (1600, 1000))

# THE SKI ON ITS OWN. Every camera in the old set shot the ski through the aeroplane, so the one view that shows a
# ski best -- straight down on it, where the keel and the nose handle live -- was looking at the wing instead. These
# three hide everything but the left main ski, and they are the ones to hold against Ty's reference photographs.
s = scene(SKIS)
keep = bpy.data.objects.get('SkiPivot_L')
alone = set()
def mark(o):
    alone.add(o.name)
    for c in o.children: mark(c)
if keep: mark(keep)
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o.name not in alone: o.hide_render = True
LX, LY, LZ = -.35, -.92, -1.05
for name, loc, target, scale, res in [
        ('skialone-side', (LX, LY - 6, LZ + .18), (LX, LY, LZ + .04), 2.2, (1800, 700)),
        ('skialone-plan', (LX, LY, LZ + 6), (LX, LY, LZ), 2.2, (1800, 700)),
        ('skialone-34', (LX - 1.9, LY - 2.4, LZ + 1.25), (LX, LY, LZ), 2.6, (1700, 1000))]:
    shoot(s, name, loc, target, scale, res)
for o in bpy.data.objects: o.hide_render = False

for name, loc, target, scale, res in [
        ('three-quarter', (-8, -11, 4), (1.3, 0, -.05), 11.7, (1500, 1000)),
        ('side', (1.6, -20, -.1), (1.6, 0, -.1), 7.6, (1500, 1000)),
        ('front', (-20, 0, -.1), (0, 0, -.1), 11.4, (1600, 850)),
        # Straight down on the left main ski: the keel and the nose handle are features of a ski seen from ABOVE,
        # which no camera in the old set looked from.
        ('plan-mainski', (-.35, -.92, 6.0), (-.35, -.92, -1.0), 2.3, (900, 1500)),
        ('pedestal', (-2.5, -3, -.3), (-.35, -.92, -1.02), 2.2, (1400, 1000)),
        ('mainski-side', (-.35, -6, -.90), (-.35, -.92, -1.05), 2.4, (1600, 900)),
        ('tailski', (5.5, -1.6, .7), (4.65, 0, .035), 1.25, (1200, 900))]:
    shoot(s, name, loc, target, scale, res)
shoot(s, 'deck-skis', *DECK)

s = scene(WHEELS)
shoot(s, 'deck-wheels', *DECK)
print('J3 SKIS REVIEW RENDERS DONE', out)
