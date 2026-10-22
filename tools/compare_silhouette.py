"""Put our model's outline on top of a reference's and say, in numbers, where they differ.

    blender --background --python tools/compare_silhouette.py -- <reference> <ours> <out.png> [true-span]

WHY. MODEL-PIPELINE.md step 8 says "render and compare against the references -- the images are the test",
and for two days that was done by eye, which is how a 0.9 m datum error, an upside-down aerofoil and a
tailplane half a strip short at each tip all survived at once. Eyes are good at "that doesn't look right"
and bad at "the cowl is 0.21 m too long". This turns the second question into a table.

HOW. Both models go through exactly the same pipeline, so nothing in the comparison depends on how either
was authored:

  1. scale by the known span, so a model in inches and a model in metres are the same aeroplane;
  2. drop every connected island wider than 1.4 m -- that is the wing, the tailplane and the propeller disc,
     none of which belong in a fuselage side view (MODEL-PIPELINE.md 3.4);
  3. put the nose at station 0, the length along +X and the thrust line at z = 0, taking the thrust line
     from the spinner, which both models have in the same place;
  4. read the top and bottom of the outline at every station and difference them.

The render is the two outlines superimposed as additive emission -- the reference in red, ours in blue, so
MAGENTA IS AGREEMENT. Anywhere the picture is pure red we are missing material and anywhere it is pure blue
we have invented some. That reads at a glance in a way two side-by-side greys never do.

A reference is MEASURED AND NEVER SHIPPED (MODEL-PIPELINE.md 1). Nothing of it enters the repo: this writes
one image of two outlines and a table of differences.
"""
import bpy, bmesh, sys, os, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
REF, OURS, OUT = argv[0], argv[1], argv[2]
TRUE_SPAN = float(argv[3]) if len(argv) > 3 else 10.92
WIDE = 1.4          # m: an island wider than this is a flying surface, not a fuselage
STATIONS = 40
BELLY = -0.55       # m: an island lying entirely below this is undercarriage, not fuselage


def load(path):
    """Import one model and return its new mesh objects."""
    before = set(bpy.data.objects)
    low = path.lower()
    if low.endswith((".glb", ".gltf")):
        bpy.ops.import_scene.gltf(filepath=path)
    elif low.endswith((".usdz", ".usd", ".usda", ".usdc")):
        bpy.ops.wm.usd_import(filepath=path)
    elif low.endswith(".fbx"):
        bpy.ops.import_scene.fbx(filepath=path)
    else:
        bpy.ops.wm.obj_import(filepath=path)
    return [o for o in set(bpy.data.objects) - before if o.type == 'MESH']


def world_points(objs):
    out = []
    for o in objs:
        m = o.matrix_world
        out += [m @ v.co for v in o.data.vertices]
    return out


def axes(pts):
    """Longest extent is the span, next the length, shortest the height -- true of every aeroplane."""
    mn = Vector((min(p[i] for p in pts) for i in range(3)))
    mx = Vector((max(p[i] for p in pts) for i in range(3)))
    size = mx - mn
    order = sorted(range(3), key=lambda i: -size[i])
    return order[0], order[1], order[2], mn, mx, size


def drop_islands(objs, test):
    """Delete every connected island for which `test(points)` is true.

    Asking this of an ISLAND rather than of a bounding box is the whole trick. It runs twice, and the order
    matters: a flying surface can be recognised on its own (it is wider than any fuselage), but an
    undercarriage can only be recognised once the thrust line is known, and the thrust line can only be
    found once the wing is out of the way. Trying to do both at once picked the thrust line off the tail.
    """
    for o in list(objs):
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bm.verts.ensure_lookup_table()
        seen, kill = set(), []
        for v0 in bm.verts:
            if v0.index in seen:
                continue
            stack, island = [v0], []
            seen.add(v0.index)
            while stack:
                v = stack.pop()
                island.append(v)
                for e in v.link_edges:
                    w = e.other_vert(v)
                    if w.index not in seen:
                        seen.add(w.index)
                        stack.append(w)
            if test([o.matrix_world @ v.co for v in island]):
                kill += island
        if kill:
            bmesh.ops.delete(bm, geom=kill, context='VERTS')
        bm.to_mesh(o.data)
        bm.free()
        o.data.update()


def normalise(path, name):
    """Load, scale, strip the flying surfaces, and move into a common frame: nose at the origin, length
    along +X, up along +Z, thrust line at z = 0. Returns (objects, length, points)."""
    objs = load(path)
    pts = world_points(objs)
    SP, LN, HG, mn, mx, size = axes(pts)
    k = TRUE_SPAN / size[SP]
    # PASS ONE: the flying surfaces. A wing is one island spanning more than any fuselage does; so is a
    # tailplane, and so is a propeller disc.
    drop_islands(objs, lambda ps: max(abs(p[SP]) for p in ps) * k > WIDE)

    pts = world_points(objs)
    if not pts:
        raise SystemExit("%s: nothing left after dropping the flying surfaces" % name)
    lo, hi = min(p[LN] for p in pts), max(p[LN] for p in pts)

    # WHICH END IS THE NOSE. Both ends are thin -- a spinner at one, a tailpost at the other -- so compare
    # the ends and you learn nothing. The cabin is the tell: on a light single the widest body section sits
    # forward of the midpoint (tools/measure_model.py says the same thing the same way).
    band = (hi - lo) / 24.0
    widest_f, widest = 0.5, -1.0
    for i in range(25):
        f = i / 24.0
        c = lo + (hi - lo) * f
        sel = [abs(p[SP]) for p in pts if abs(p[LN] - c) <= band]
        if not sel:
            continue
        sel.sort()
        w = sel[int(len(sel) * 0.9)]
        if w > widest:
            widest, widest_f = w, f
    nose_lo = widest_f < 0.5

    # the thrust line, from the spinner: the mean height of the most forward 2 % of the body
    fwd = sorted(pts, key=lambda p: p[LN] if nose_lo else -p[LN])[:max(8, len(pts) // 50)]
    z0 = sum(p[HG] for p in fwd) / len(fwd)
    nose = lo if nose_lo else hi
    sgn = 1.0 if nose_lo else -1.0

    # PASS TWO, now that the thrust line is known: the undercarriage. Wheels and legs swamp the bottom line
    # and say nothing about the shape of the body, which is the thing being compared.
    drop_islands(objs, lambda ps: max((p[HG] - z0) for p in ps) * k < BELLY)
    pts = world_points(objs)
    lo, hi = min(p[LN] for p in pts), max(p[LN] for p in pts)
    nose = lo if nose_lo else hi

    for o in objs:
        me = o.data
        for v in me.vertices:
            w = o.matrix_world @ v.co
            v.co = Vector(((w[LN] - nose) * sgn * k, w[SP] * k, (w[HG] - z0) * k))
        o.matrix_world.identity()
        me.update()
    pts = world_points(objs)
    length = max(p.x for p in pts)
    print("CMP %-10s length %.3f m   span kept %.3f m" % (name, length, max(abs(p.y) for p in pts) * 2))
    return objs, length, pts


def contour(pts, length, n=STATIONS):
    """Top and bottom of the outline at n stations from the nose."""
    # A band one station wide. Thinner and a sparse mesh gives half-empty slices and a table that jumps
    # about, which is how "that looks wrong" sneaks back in wearing numbers.
    band = length / n
    rows = []
    for i in range(n + 1):
        s = length * i / n
        sel = [p.z for p in pts if abs(p.x - s) <= band]
        rows.append((s, min(sel), max(sel)) if len(sel) >= 8 else (s, None, None))
    return rows


# ---------------------------------------------------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
ref_objs, ref_len, ref_pts = normalise(REF, "reference")
our_objs, our_len, our_pts = normalise(OURS, "ours")


def emit(objs, colour, name):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs[0].default_value = colour
    em.inputs[1].default_value = 1.0
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(em.outputs[0], out.inputs[0])
    m.blend_method = 'BLEND'
    for o in objs:
        o.data.materials.clear()
        o.data.materials.append(m)


# red is the reference, blue is ours, and additive emission makes agreement MAGENTA
emit(ref_objs, (1.0, 0.10, 0.18, 1), "Ref")
emit(our_objs, (0.12, 0.45, 1.0, 1), "Ours")

span = max(ref_len, our_len)
allp = ref_pts + our_pts
zlo, zhi = min(p.z for p in allp), max(p.z for p in allp)
ctr = Vector((span * 0.5, 0.0, (zlo + zhi) * 0.5))
cam = bpy.data.objects.new("C", bpy.data.cameras.new("C"))
bpy.context.collection.objects.link(cam)
cam.data.type = 'ORTHO'
cam.data.ortho_scale = span * 1.06
cam.location = ctr + Vector((0, -span * 3, 0))
cam.rotation_euler = (math.radians(90), 0, 0)
bpy.context.scene.camera = cam
w = bpy.data.worlds.new("W")
bpy.context.scene.world = w
w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0, 0, 0, 1)
w.node_tree.nodes["Background"].inputs[1].default_value = 0.0
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x = 1800
sc.render.resolution_y = max(240, int(1800 * (zhi - zlo) * 1.25 / (span * 1.06)))
sc.render.filepath = OUT
bpy.ops.render.render(write_still=True)

# ---- the table ------------------------------------------------------------------------------------------
rc, oc = contour(ref_pts, ref_len), contour(our_pts, our_len)
print("CMP")
print("CMP length  reference %.3f   ours %.3f   (%+.3f m)" % (ref_len, our_len, our_len - ref_len))
print("CMP")
print("CMP  station   ref top  our top    dTOP    ref bot  our bot    dBOT")
worst_t = worst_b = (0.0, 0.0)
for (s, rb, rt), (_, ob, ot) in zip(rc, oc):
    if rb is None or ob is None:
        continue
    dt, db = ot - rt, ob - rb
    if abs(dt) > abs(worst_t[1]):
        worst_t = (s, dt)
    if abs(db) > abs(worst_b[1]):
        worst_b = (s, db)
    flag = "  <<<" if max(abs(dt), abs(db)) > 0.10 else ""
    print("CMP %8.3f %9.3f %8.3f %7.3f %10.3f %8.3f %7.3f%s" % (s, rt, ot, dt, rb, ob, db, flag))
print("CMP")
print("CMP worst top %+.3f m at station %.2f" % (worst_t[1], worst_t[0]))
print("CMP worst bot %+.3f m at station %.2f" % (worst_b[1], worst_b[0]))
print("CMP wrote %s" % OUT)
