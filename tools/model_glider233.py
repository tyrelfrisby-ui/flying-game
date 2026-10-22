"""Model a Schweizer 2-33 in Blender, from the config's own numbers.

    blender --background --python tools/model_glider233.py        -> build/models/glider-2-33-like.glb

WHY THIS EXISTS. The downloaded 2-33 (42manako's, on Sketchfab) is a 15.5 m span on a 13.3 m fuselage --
the real aeroplane is 15.5 on 7.9 -- so its wings look stubby and its boom looks like a sailplane's. The
owner's verdict on 2026-10-22 was "the wings are too short, it is a lost cause", and the download was
deleted. Nothing about it could be fixed by scaling: a uniform scale cannot change a ratio, and squashing
the body in x alone squashes the canopy with it.

DIMENSIONS COME FROM THE CONFIG, not from this file's imagination -- the same rule tools/model_c172.py
works under. The physics is the authority: if the config flies a 15.44 m wing at 1.40 m of chord, then
that is what this draws. The published 2-33A agrees with it to within 100 mm (51 ft span, 25 ft 9 in long).

    span        15.44 m   wing strips, tip at y 7.72
    length       7.85 m   nose +2.90 to tailpost -4.95
    chord        1.40 m   constant to y 4.5, tapering to 0.645 at the tip
    wing height  0.55 m   above the body axis (heightAboveBodyAxisM; the strips' own z is the lift line,
                          which is a different thing and sits lower)
    tail        stab 3.50 m span at x -4.03, fin to 1.03 above the axis, rudder down to 0.07 below
    gear        wheel contact 1.00 m below the axis at x 0.055, nose skid 1.05 at x 1.655,
                tail skid 0.60 at x -3.945, wingtip skids at y +-7.60

THE LIFT STRUTS are the shape of this aeroplane as much as the wing is -- a 2-33 without them reads as a
glass ship. One streamlined strut each side from the lower longeron out to 45 % of the half-span, with a
jury strut up to the spar, which is what the real one carries.
"""
import bpy, bmesh, json, math, os
from mathutils import Vector

REPO = "/Users/tyfrisby/Documents/flying-game"
OUT = os.path.join(REPO, "build/models")
CFG = json.load(open(os.path.join(REPO, "configs/aircraft/glider-2-33-like.json")))

WHITE = (0.880, 0.885, 0.895, 1.0)
TRIM = (0.247, 0.263, 0.298, 1.0)        # graphite, the same the liveries are trimmed in
GLASS = (0.42, 0.50, 0.56, 1.0)
RUBBER = (0.055, 0.058, 0.062, 1.0)


def surface(sid):
    for sf in CFG["surfaces"]:
        if sf["id"] == sid:
            return sf
    raise KeyError(sid)


WING = surface("wing")
AIL = surface("wing-aileron")
HALF = max(abs(s["pos"][1]) for s in WING["strips"])          # 7.72
WING_Z = 0.46        # the wing's flat underside, which sits ON the cabin roof (0.38 at the
                     # centreline). heightAboveBodyAxisM says 0.55 and the strips' own z says
                     # 0.30; the first is the aero model's lift line raised to the mid-chord of
                     # a cambered section and the second is the strip datum. Neither is where
                     # the skin is, and the skin is what you see.
WING_QC = WING["strips"][0]["pos"][0]                          # 0.10, the quarter-chord station


def chord_at(y):
    """The config's own chord distribution, read at any station by walking its strips."""
    pts = sorted((abs(s["pos"][1]), s["chord"]) for s in WING["strips"])
    # the aileron is a separate surface, so a strip that has one is short by the aileron's chord
    ail = sorted((abs(s["pos"][1]), s["chord"]) for s in AIL["strips"])
    for ya, ca in ail:
        for i, (yw, cw) in enumerate(pts):
            if abs(yw - ya) < 1e-6:
                pts[i] = (yw, cw + ca)
    y = min(abs(y), pts[-1][0])
    for (y0, c0), (y1, c1) in zip(pts, pts[1:]):
        if y0 <= y <= y1:
            t = (y - y0) / (y1 - y0) if y1 > y0 else 0.0
            return c0 + (c1 - c0) * t
    return pts[0][1]


LE_X = WING_QC + 0.25 * chord_at(0.0)      # the leading edge is straight; the trailing edge does the taper
DIHEDRAL = WING["strips"][0]["dihedralRad"]


# -------------------------------------------------------------------------------------------------------
# sections
# -------------------------------------------------------------------------------------------------------
def clark_y(n=26):
    """A Clark-Y-like section: 11.7 % thick, cambered, and FLAT UNDERNEATH aft of a third of the chord,
    which is the whole point of a Clark Y and what a 2-33's wing looks like end-on. Points run from the
    trailing edge over the top to the leading edge and back under -- the order a closed loop wants."""
    m, p, t = 0.035, 0.42, 0.117

    def camber(x):
        if x < p:
            return m / p ** 2 * (2 * p * x - x * x), 2 * m / p ** 2 * (p - x)
        return m / (1 - p) ** 2 * ((1 - 2 * p) + 2 * p * x - x * x), 2 * m / (1 - p) ** 2 * (p - x)

    def half(x):
        return 5 * t * (0.2969 * math.sqrt(x) - 0.1260 * x - 0.3516 * x ** 2 + 0.2843 * x ** 3
                        - 0.1036 * x ** 4)

    top, bot = [], []
    for i in range(n + 1):
        x = 0.5 * (1 - math.cos(math.pi * i / n))              # cosine spacing, tight at the nose
        yc, dy = camber(x)
        th = math.atan(dy)
        yt = half(x)
        top.append((x - yt * math.sin(th), yc + yt * math.cos(th)))
        lo = yc - yt * math.cos(th)
        if x > 0.30:
            lo = max(lo, 0.0)                                   # the flat bottom
        bot.append((x + yt * math.sin(th), lo))
    return list(reversed(top)) + bot[1:-1]


def naca00(t=0.10, n=18):
    """A symmetric section for the tail surfaces and the struts."""
    def half(x):
        return 5 * t * (0.2969 * math.sqrt(x) - 0.1260 * x - 0.3516 * x ** 2 + 0.2843 * x ** 3
                        - 0.1036 * x ** 4)
    top = [(0.5 * (1 - math.cos(math.pi * i / n)), half(0.5 * (1 - math.cos(math.pi * i / n))))
           for i in range(n + 1)]
    bot = [(x, -y) for x, y in top]
    return list(reversed(top)) + bot[1:-1]


CLARK = clark_y()
SYM = naca00()


# -------------------------------------------------------------------------------------------------------
# mesh plumbing
# -------------------------------------------------------------------------------------------------------
def mesh_from_loops(name, loops, close_ends=True):
    """Bridge a list of cross-sections (each a list of (x, y, z)) into a skinned solid."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    rings = [[bm.verts.new(Vector(p)) for p in loop] for loop in loops]
    bm.verts.ensure_lookup_table()
    n = len(rings[0])
    for a, b in zip(rings, rings[1:]):
        for j in range(n):
            k = (j + 1) % n
            try:
                bm.faces.new((a[j], a[k], b[k], b[j]))
            except ValueError:
                pass
    if close_ends:
        for ring in (rings[0], rings[-1]):
            try:
                bm.faces.new(ring)
            except ValueError:
                pass
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return ob


def grid_mesh(name, rows):
    """Bridge rows of points into an OPEN sheet -- a canopy is not a closed ring."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    rings = [[bm.verts.new(Vector(q)) for q in r] for r in rows]
    bm.verts.ensure_lookup_table()
    for a, b in zip(rings, rings[1:]):
        for j in range(len(a) - 1):
            try:
                bm.faces.new((a[j], a[j + 1], b[j + 1], b[j]))
            except ValueError:
                pass
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return ob


def paint(ob, rgba, rough=0.45):
    mat = bpy.data.materials.new(ob.name + "Mat")
    mat.use_nodes = True
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = rgba
    if "Roughness" in b.inputs:
        b.inputs["Roughness"].default_value = rough
    if rgba is GLASS and "Alpha" in b.inputs:
        b.inputs["Alpha"].default_value = 0.55
    ob.data.materials.append(mat)
    return ob


def smooth(ob):
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


# -------------------------------------------------------------------------------------------------------
# the fuselage: slab-sided, deep at the cabin, tapering to a slim boom
# -------------------------------------------------------------------------------------------------------
# (station x, half-width, bottom z, top z). The cabin is 1.1 m deep and 0.69 m across, which is the real
# aeroplane's cockpit; the boom at the tailpost is 0.19 m deep, which is what the fin and stab sit on.
STATIONS = [
    (2.90, 0.045, -0.33, -0.12),
    (2.62, 0.170, -0.56, 0.06),
    (2.25, 0.270, -0.68, 0.22),
    (1.80, 0.330, -0.74, 0.33),
    (1.20, 0.345, -0.75, 0.36),
    (0.50, 0.345, -0.75, 0.38),
    (0.10, 0.340, -0.74, 0.38),
    (-0.50, 0.310, -0.70, 0.36),
    (-1.30, 0.255, -0.62, 0.31),
    (-2.20, 0.195, -0.52, 0.24),
    (-3.10, 0.145, -0.44, 0.14),
    (-4.00, 0.100, -0.35, 0.03),
    (-4.70, 0.070, -0.28, -0.02),
    (-4.95, 0.045, -0.24, -0.06),
]
NA = 16                                   # points round half a section


def station(x):
    """(half-width, bottom z, top z) of the fuselage at any station, between the ones listed."""
    xs = [st[0] for st in STATIONS]
    x = max(min(x, xs[0]), xs[-1])
    for a, b in zip(STATIONS, STATIONS[1:]):
        if b[0] <= x <= a[0]:
            t = (a[0] - x) / (a[0] - b[0]) if a[0] > b[0] else 0.0
            return tuple(a[i] + (b[i] - a[i]) * t for i in (1, 2, 3))
    return STATIONS[0][1:]


def body_loop(x, hw, zb, zt, square=0.72):
    """One fuselage section: a rounded rectangle, because a 2-33 is a fabric-covered steel tube box with
    its corners rounded, not a body of revolution. `square` pushes the corners out towards the box."""
    zc, zr = (zt + zb) * 0.5, (zt - zb) * 0.5
    pts = []
    for i in range(NA * 2):
        a = 2.0 * math.pi * i / (NA * 2)
        c, s = math.cos(a), math.sin(a)
        # a superellipse between a circle (square = 0) and a rectangle (square = 1)
        k = 2.0 / (1.0 + square * 2.0)
        cy = math.copysign(abs(c) ** k, c)
        cz = math.copysign(abs(s) ** k, s)
        pts.append((x, hw * cy, zc + zr * cz))
    return pts


def build_body():
    loops = [body_loop(x, hw, zb, zt) for x, hw, zb, zt in STATIONS]
    return smooth(paint(mesh_from_loops("Fuselage", loops), WHITE))


def build_canopy():
    """The greenhouse. A 2-33 is mostly window from the nose back to the wing's leading edge, and the
    glazing is a shallow dome ON the deck -- drawn as its own shell floating above the fuselage, it read
    as a blister bolted to the roof."""
    rows = []
    for x in (2.45, 2.15, 1.80, 1.40, 1.00, 0.62):
        hw, zb, zt = station(x)
        row = []
        for i in range(15):
            a = math.pi * i / 14.0
            # a dome that meets the deck at the sides and stands 0.11 m proud on the centreline
            row.append((x, hw * 0.98 * math.cos(a), zt - 0.055 + 0.165 * math.sin(a) ** 0.85))
        rows.append(row)
    ob = grid_mesh("Canopy", rows)
    m = bpy.data.materials.new("CanopyMat")
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = GLASS
    b.inputs["Roughness"].default_value = 0.08
    if "Alpha" in b.inputs:
        b.inputs["Alpha"].default_value = 0.45
    for attr, val in (("blend_method", 'BLEND'), ("surface_render_method", 'BLENDED')):
        if hasattr(m, attr):
            try:
                setattr(m, attr, val)
            except (TypeError, AttributeError):
                pass
    ob.data.materials.append(m)
    return smooth(ob)


def build_wing_root():
    """The short fairing between the cabin roof and the wing's underside -- the cabane a 2-33's wing sits
    on. Without it the wing floats a hand's breadth above the fuselage in any side view."""
    loops = []
    for x in (LE_X + 0.02, LE_X - 0.45, LE_X - 1.00, LE_X - 1.42):
        hw, zb, zt = station(min(x, STATIONS[0][0]))
        hw = min(hw, 0.33) * (0.72 if x < LE_X - 1.0 else 0.96)
        loops.append([(x, hw, zt - 0.06), (x, hw, WING_Z + 0.01),
                      (x, -hw, WING_Z + 0.01), (x, -hw, zt - 0.06)])
    return smooth(paint(mesh_from_loops("WingRoot", loops), WHITE))


# -------------------------------------------------------------------------------------------------------
# flying surfaces
# -------------------------------------------------------------------------------------------------------
def wing_loop(side, y, chord_frac=(0.0, 1.0)):
    """One wing section at station y, as a closed loop in world space."""
    c = chord_at(y)
    z = WING_Z + abs(y) * math.tan(DIHEDRAL)
    a, b = chord_frac
    out = []
    for u, t in CLARK:
        u = a + u * (b - a)
        out.append((LE_X - u * c, side * y, z + t * c))
    return out


def build_wing(side):
    ys = [0.0, 1.0, 2.0, 3.0, 4.0, 4.6, 5.3, 6.0, 6.6, 7.1, 7.45, HALF]
    loops = [wing_loop(side, y, (0.0, 0.76 if y > 4.6 else 1.0)) for y in ys]
    # the tip is closed off by a short rounded cap rather than a flat plate
    tip = wing_loop(side, HALF, (0.0, 0.76))
    cz = sum(p[2] for p in tip) / len(tip)
    cx = sum(p[0] for p in tip) / len(tip)
    loops.append([(cx + (p[0] - cx) * 0.6, side * (HALF + 0.08), cz + (p[2] - cz) * 0.6) for p in tip])
    ob = smooth(paint(mesh_from_loops("Wing." + ("R" if side > 0 else "L"), loops), WHITE))
    return ob


def build_aileron(side):
    """The outboard quarter of the trailing edge, hinged. Its origin sits ON the hinge line so Unity can
    turn it with a local rotation -- the same rule model_c172.py works to."""
    ys = [y for y in (4.6, 5.3, 6.0, 6.6, 7.1, 7.45, HALF)]
    loops = [wing_loop(side, y, (0.76, 1.0)) for y in ys]
    ob = smooth(paint(mesh_from_loops("Aileron." + ("R" if side > 0 else "L"), loops), WHITE))
    hinge = Vector((LE_X - 0.76 * chord_at(6.0), side * 6.0, WING_Z + 6.0 * math.tan(DIHEDRAL)))
    off = hinge - ob.location
    for v in ob.data.vertices:
        v.co -= off
    ob.location = hinge
    return ob


def build_tail_panel(name, x_le, root_c, tip_c, half_span, z0, z1, vertical, thick=0.10):
    """A straight-tapered tail surface, drawn as a loft between a root and a tip section."""
    loops = []
    n = 6
    for i in range(n + 1):
        f = i / n
        c = root_c + (tip_c - root_c) * f
        # a little sweep: the leading edge rakes back as the chord shrinks, which is what the real one does
        xl = x_le - (root_c - c) * 0.55
        loop = []
        for u, t in naca00(thick):
            if vertical:
                loop.append((xl - u * c, t * c, z0 + (z1 - z0) * f))
            else:
                loop.append((xl - u * c, half_span * f, z0 + (z1 - z0) * f + t * c))
        loops.append(loop)
    return smooth(paint(mesh_from_loops(name, loops), WHITE))


def mirror_y(ob, name):
    me = ob.data.copy()
    for v in me.vertices:
        v.co.y = -v.co.y
    nb = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(nb)
    nb.location = Vector((ob.location.x, -ob.location.y, ob.location.z))
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.reverse_faces(bm, faces=bm.faces)      # mirroring turns every face inside out
    bm.to_mesh(me)
    bm.free()
    me.update()
    return nb


# -------------------------------------------------------------------------------------------------------
# struts, skids and the wheel
# -------------------------------------------------------------------------------------------------------
def strut(name, p0, p1, chord=0.11, thick=0.30):
    """A streamlined strut between two points: an aerofoil section swept along the line, with the chord
    lying fore-and-aft. A 2-33's lift strut is a faired steel tube, not a rod."""
    p0, p1 = Vector(p0), Vector(p1)
    axis = (p1 - p0).normalized()
    # the section's "up" is across the strut and across the chord
    side = Vector((0, 0, 1)).cross(axis)
    if side.length < 1e-4:
        side = Vector((0, 1, 0))
    side.normalize()
    loops = []
    for p in (p0, p1):
        loop = []
        for u, t in naca00(thick, 10):
            loop.append(tuple(p + Vector((-(u - 0.25) * chord, 0, 0)) + side * (t * chord)))
        loops.append(loop)
    return smooth(paint(mesh_from_loops(name, loops), WHITE))


def cyl(name, p0, p1, r, seg=14):
    p0, p1 = Vector(p0), Vector(p1)
    axis = (p1 - p0).normalized()
    u = axis.cross(Vector((0, 0, 1)))
    if u.length < 1e-4:
        u = axis.cross(Vector((0, 1, 0)))
    u.normalize()
    w = axis.cross(u)
    loops = []
    for p in (p0, p1):
        loops.append([tuple(p + u * (r * math.cos(2 * math.pi * i / seg))
                            + w * (r * math.sin(2 * math.pi * i / seg))) for i in range(seg)])
    return smooth(paint(mesh_from_loops(name, loops), TRIM))


def build_wheel(name, cx, cz, r, width, seg=20):
    loops = []
    for y in (-width * 0.5, width * 0.5):
        loops.append([(cx + r * math.cos(2 * math.pi * i / seg), y, cz + r * math.sin(2 * math.pi * i / seg))
                      for i in range(seg)])
    return smooth(paint(mesh_from_loops(name, loops), RUBBER, rough=0.8))


def build_skid(name, x0, x1, z0, z1, hw=0.075):
    """A flat skid under the belly: the nose skid a 2-33 lands on, and the tail skid behind."""
    rows = []
    for x, z in ((x0, z0), ((x0 + x1) * 0.5, (z0 + z1) * 0.5 - 0.01), (x1, z1)):
        rows.append([(x, -hw, z), (x, hw, z)])
    ob = grid_mesh(name, rows)
    return paint(ob, TRIM)


# -------------------------------------------------------------------------------------------------------
def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts = [build_body(), build_canopy(), build_wing_root()]

    for side in (1, -1):
        parts.append(build_wing(side))
        parts.append(build_aileron(side))

    # the tail. The stabiliser sits on top of the boom at the fin's root, the fin stands on it, and the
    # rudder reaches below the boom -- which is the 2-33's outline from the side.
    # ONE PANEL EACH, not a fin and a rudder drawn separately. Drawn as two, the swept leading edges
    # pulled apart at the top and the tail came out with a notch bitten out of it. Unity carves control
    # surfaces out of a model's mesh by the config's regions anyway (AirframeVisual.CarveModel), so
    # splitting them here buys nothing and costs the outline.
    #   stab     quarter chord x -4.03, chord 0.72 + elevator 0.48 -> leading edge -3.73, span 3.50
    #   fin      quarter chord x -4.05, chord 0.60; rudder -4.45, 0.45 -> -3.90 back to -4.79
    stab = build_tail_panel("Stab.R", -3.73, 1.20, 0.80, 1.75, 0.07, 0.09, vertical=False)
    parts += [stab, mirror_y(stab, "Stab.L")]
    parts.append(build_tail_panel("Fin", -3.90, 0.89, 0.52, 0.0, 0.02, 1.30, vertical=True))

    # THE LIFT STRUTS. Out to 45 % of the half-span, off the lower longeron, with a jury strut up to the
    # spar at two thirds of the way out -- which is the bracing a 2-33 actually carries.
    sy = 0.45 * HALF
    sz = WING_Z + sy * math.tan(DIHEDRAL)
    for side in (1, -1):
        foot = (0.30, side * 0.30, -0.58)
        head = (LE_X - 0.42 * chord_at(sy), side * sy, sz + 0.012)
        tag = "R" if side > 0 else "L"
        parts.append(strut("Strut." + tag, foot, head))
        jx = 0.30 + (head[0] - 0.30) * 0.62
        jy = side * (0.30 + (sy - 0.30) * 0.62)
        jz = -0.58 + (head[2] + 0.012 - (-0.58)) * 0.62
        parts.append(strut("Jury." + tag, (jx, jy, jz),
                           (LE_X - 0.18 * chord_at(abs(jy)), jy,
                            WING_Z + abs(jy) * math.tan(DIHEDRAL) + 0.01), chord=0.06, thick=0.34))
        # wingtip skid: the config lands this aeroplane on them (gearType tip-skid at y +-7.60)
        ty = side * 7.60
        parts.append(build_skid("TipSkid." + tag, LE_X - 0.65 * chord_at(7.60) + 0.12,
                                LE_X - 0.65 * chord_at(7.60) - 0.12,
                                WING_Z + 7.60 * math.tan(DIHEDRAL) - 0.05,
                                WING_Z + 7.60 * math.tan(DIHEDRAL) - 0.05, hw=0.03))
        parts[-1].data.vertices.foreach_set("co", [c for v in parts[-1].data.vertices
                                                   for c in (v.co.x, ty + v.co.y, v.co.z)])
        parts[-1].data.update()

    # the monowheel, semi-recessed in the belly, and the skids it shares the ground with
    parts.append(build_wheel("Wheel", 0.055, -0.78, 0.22, 0.10))
    parts.append(cyl("WheelLeg", (0.055, -0.10, -0.74), (0.055, 0.10, -0.74), 0.035))
    parts.append(build_skid("NoseSkid", 2.30, 1.10, -0.70, -1.02))
    parts.append(build_skid("TailSkid", -3.70, -4.15, -0.42, -0.60, hw=0.045))

    os.makedirs(OUT, exist_ok=True)
    glb = os.path.join(OUT, "glider-2-33-like.glb")
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.gltf(filepath=glb, export_format='GLB', use_selection=True, export_apply=True)

    pts = [o.matrix_world @ v.co for o in parts for v in o.data.vertices]
    print("MODEL233 parts %d  span %.2f  length %.2f  height %.2f"
          % (len(parts), max(p.y for p in pts) - min(p.y for p in pts),
             max(p.x for p in pts) - min(p.x for p in pts),
             max(p.z for p in pts) - min(p.z for p in pts)))
    print("MODEL233 wrote %s" % glb)


main()
