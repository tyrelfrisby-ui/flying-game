"""Close-ups of where the wing skin meets the aileron, at neutral, for check_aileron_junction.py's work list.

blender -b --factory-startup --python tools/own_models/render_aileron_junctions.py -- GLB AILERON_NODE OUT_DIR LABEL

Three shots of the named aileron (Blender frame after glTF import, +Z up; span and chord found from the model):
  <label>-above.png    perspective from above, aft and outboard of the aileron's inboard end
  <label>-below.png    the same from below
  <label>-section.png  orthographic chordwise section through wing + aileron at 30 % of the aileron span (2 mm slice),
                       framed on the aileron's nose: the contour step / kink / gap the checker reports
Cycles CPU, 24 samples, a sky and two soft lights: the light is the same for every model.
"""
import bpy, bmesh, sys
from pathlib import Path
from mathutils import Vector

glb, node, out, label = sys.argv[sys.argv.index('--') + 1:][:4]
out = Path(out); out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
sc = bpy.context.scene
sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 24
sc.view_settings.view_transform = 'Standard'
sc.world = bpy.data.worlds.new('sky'); sc.world.use_nodes = True
bg = sc.world.node_tree.nodes['Background']; bg.inputs[0].default_value = (.66, .72, .8, 1); bg.inputs[1].default_value = .8


def bbox(o):
    pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
    return Vector([min(p[i] for p in pts) for i in range(3)]), Vector([max(p[i] for p in pts) for i in range(3)])


def mesh_objs(prefix):
    return [o for o in sc.objects if o.type == 'MESH' and (o.name == prefix or o.name.startswith(prefix + '.') or o.name.startswith(prefix + '_') and o.name[len(prefix) + 1:].isdigit())]


ail = [o for o in sc.objects if o.type == 'MESH' and (o.name == node or o.name.startswith(node + '.'))]
if not ail:
    ail = [o for o in sc.objects if o.type == 'MESH' and o.parent is not None and o.parent.name == node]
lo, hi = bbox(ail[0])
for o in ail[1:]:
    a, b = bbox(o); lo = Vector(map(min, lo, a)); hi = Vector(map(max, hi, b))
c = (lo + hi) / 2
others = [o for o in sc.objects if o.type == 'MESH' and o.name.startswith('Aileron') and o not in ail]
# Span axis: the horizontal axis along which this aileron is longest.
size = hi - lo
span_ax = 0 if size.x > size.y else 1
chord_ax = 1 - span_ax
side = 1 if c[span_ax] > 0 else -1
s_in = lo[span_ax] if side > 0 else hi[span_ax]
# Forward: the side the rest of the aeroplane's mass is on, along the chord axis.
allc = [o.matrix_world.translation for o in sc.objects if o.type == 'MESH']
wing_c = sum(((bbox(o)[0] + bbox(o)[1]) / 2 for o in sc.objects if o.type == 'MESH' and o.name.startswith('Wing')), Vector())
nw = len([o for o in sc.objects if o.type == 'MESH' and o.name.startswith('Wing')])
fwd = 1 if nw and (wing_c / nw)[chord_ax] > c[chord_ax] else -1
chord = size[chord_ax]


def V(chordv, spanv, up):
    v = Vector((0, 0, up)); v[chord_ax] = chordv; v[span_ax] = spanv; return v


for loc, power, sz in [((-3, -4, 7), 1800, 6), ((3, 3, -5), 700, 5), ((3, 3, 5), 1100, 5)]:
    l = bpy.data.lights.new('soft', 'AREA'); l.energy = power; l.shape = 'DISK'; l.size = sz
    o = bpy.data.objects.new('soft', l); sc.collection.objects.link(o); o.location = loc
    o.rotation_euler = (c - o.location).to_track_quat('-Z', 'Y').to_euler()


def shoot(cam_loc, target, name, ortho=None, res=(1600, 1000), lens=50):
    d = bpy.data.cameras.new(name)
    if ortho: d.type = 'ORTHO'; d.ortho_scale = ortho
    else: d.lens = lens
    d.clip_start = 0.005
    o = bpy.data.objects.new(name, d); sc.collection.objects.link(o); o.location = cam_loc
    o.rotation_euler = (Vector(target) - o.location).to_track_quat('-Z', 'Y').to_euler(); sc.camera = o
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.filepath = str(out / f'{label}-{name}.png'); bpy.ops.render.render(write_still=True)


root_pt = V(c[chord_ax], s_in, c.z)
aft = -fwd
shoot(root_pt + V(aft * 1.1 * max(chord, .3), side * 0.9, 0.75), root_pt, 'above', lens=45)
shoot(root_pt + V(aft * 1.1 * max(chord, .3), side * 0.9, -0.75), root_pt, 'below', lens=45)

# The section: keep a 2 mm slice of every mesh at 30 % of the aileron span, look along the span at it.
s_cut = s_in + side * 0.30 * size[span_ax]
nose = hi[chord_ax] if fwd > 0 else lo[chord_ax]
for o in list(sc.objects):
    if o.type != 'MESH':
        continue
    a, b = bbox(o)
    if not (a[span_ax] - 1e-3 <= s_cut <= b[span_ax] + 1e-3) or (b.z - a.z) > 3:
        o.hide_render = True; continue
    bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world)
    for off, sgn in ((0.001, 1), (-0.001, -1)):
        n = Vector((0, 0, 0)); n[span_ax] = sgn
        p = Vector((0, 0, 0)); p[span_ax] = s_cut + off
        bmesh.ops.bisect_plane(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces), plane_co=p, plane_no=n, clear_outer=True, dist=1e-7)
    bm.transform(o.matrix_world.inverted()); bm.to_mesh(o.data); bm.free()
    m = bpy.data.materials.new('cut'); m.diffuse_color = (.9, .25, .1, 1) if o in ail else (.05, .07, .1, 1)
    m.use_nodes = True; m.node_tree.nodes['Principled BSDF'].inputs[0].default_value = m.diffuse_color
    o.data.materials.clear(); o.data.materials.append(m)
    for poly in o.data.polygons: poly.material_index = 0
tgt = V(nose, s_cut, c.z)
shoot(tgt + V(0, side * 2.0, 0), tgt, 'section', ortho=max(0.18, 0.55 * chord), res=(1600, 900))
print('[junction] rendered', label, 'aileron', node, 'span axis', 'xy'[span_ax], 'inboard end', round(s_in, 3), 'cut at', round(s_cut, 3))
