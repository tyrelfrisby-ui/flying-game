"""Model a 172 in Blender, as SEPARATE NAMED PARTS so the game can move them.

    blender --background --python tools/model_c172.py

WHY THIS EXISTS. The generated airframe is built from primitives in Unity: a body of revolution for the
fuselage and simple lofts for the wings. However good its shading gets (2026-10-21 added smooth normals, UVs,
a specular shader and panel-line normal maps), its SILHOUETTE is still a tube, because that is all a lathe can
make. Blender can bridge arbitrary cross-sections, so the fuselage can be flat-sided with a rounded turtledeck
and a tapered boom — the shape a 172 actually has.

DIMENSIONS COME FROM THE CONFIG, not from this file's imagination, exactly as the Jenny's did. The physics is
the authority: if the config says the wing spans 10.92 m then so does the model, or the aeroplane you see is
not the one you fly. Everything else is the published 172: 8.28 m long, 2.72 m tall, NACA 2412 wing.

NAMED PARTS. Unity already CARVES ailerons, elevator and rudder out of a model's mesh using the config's
surface regions (AirframeVisual.CarveModel), so those would animate even as one lump. Flaps and nose-wheel
steering are different: neither is animated anywhere in the game today — the flap geometry does not exist and
the steer angle the physics computes never reaches the visual. Shipping them as their own objects is what makes
binding them possible at all:

    Flap.L / Flap.R        inboard trailing edge, hinge at its leading edge
    Aileron.L / Aileron.R  outboard trailing edge
    Elevator / Rudder      hinged tail surfaces
    Gear.Nose.Steer        the steering leg; its origin IS the steering axis
    Prop / Spinner         spun by the existing PropHub

Origins matter as much as the geometry: each hinged part's origin is placed ON ITS HINGE LINE, so Unity can
rotate it by a local rotation and nothing has to be worked out at runtime.
"""
import bpy, bmesh, json, math, os, sys
from mathutils import Vector

REPO = "/Users/tyfrisby/Documents/flying-game"
CFG = json.load(open(os.path.join(REPO, "configs/aircraft/c172-like.json")))

# ---- what the config says, which the model must match -------------------------------------------------------
def wing_strips():
    out = []
    for sf in CFG["surfaces"]:
        if sf["id"].startswith("wing"):
            out += sf["strips"]
    return out


def bz(config_z):
    """Config z is DOWN-positive (aviation body axes); Blender's Z is UP. Every vertical number crossing
    between them goes through here. Getting this wrong put the high wing under the fuselage and the
    undercarriage on the roof (2026-10-21, first pass)."""
    return -config_z


STRIPS = wing_strips()
HALF_SPAN = max(abs(s["pos"][1]) for s in STRIPS)
WING_AREA = sum(s["area"] for s in STRIPS)
ROOT_CHORD = max(s["chord"] for s in STRIPS)
WING_X = sum(s["pos"][0] for s in STRIPS) / len(STRIPS)      # quarter-chord station
WING_Z = bz(sum(s["pos"][2] for s in STRIPS) / len(STRIPS))   # now UP-positive, i.e. above the CG

# published 172 figures the config is silent about
LENGTH, HEIGHT = 8.28, 2.72
PROP_D = 1.93


def naca2412(n=28):
    """NACA 2412, the 172's section: 2 % camber at 40 % chord, 12 % thick. Points run from the trailing edge
    over the top to the leading edge and back under, which is the order a closed loop wants."""
    m, p, t = 0.02, 0.4, 0.12
    def camber(x):
        if x < p: return m / p**2 * (2 * p * x - x * x), 2 * m / p**2 * (p - x)
        return m / (1 - p)**2 * ((1 - 2 * p) + 2 * p * x - x * x), 2 * m / (1 - p)**2 * (p - x)
    def half(x):
        return 5 * t * (0.2969 * math.sqrt(x) - 0.1260 * x - 0.3516 * x**2 + 0.2843 * x**3 - 0.1036 * x**4)
    top, bot = [], []
    for i in range(n + 1):
        # cosine spacing: tight at the leading edge where the curvature is
        x = 0.5 * (1 - math.cos(math.pi * i / n))
        yc, dy = camber(x)
        th = math.atan(dy)
        yt = half(x)
        top.append((x - yt * math.sin(th), yc + yt * math.cos(th)))
        bot.append((x + yt * math.sin(th), yc - yt * math.cos(th)))
    # trailing edge -> over the top -> leading edge -> under -> back, without repeating either end
    return list(reversed(top)) + bot[1:-1]


def mesh_from_loops(name, loops, close_ends=True):
    """Bridge a list of cross-sections (each a list of (x,y,z)) into a skinned solid."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    rings = []
    for loop in loops:
        rings.append([bm.verts.new(Vector(p)) for p in loop])
    bm.verts.ensure_lookup_table()
    n = len(rings[0])
    for i in range(len(rings) - 1):
        a, b = rings[i], rings[i + 1]
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


def set_origin(ob, world_point):
    """Move an object's origin to a point without moving its geometry — so a hinge rotates about its hinge."""
    off = Vector(world_point) - ob.location
    me = ob.data
    for v in me.vertices:
        v.co -= off
    ob.location = Vector(world_point)


# =============================================================================================================
bpy.ops.wm.read_factory_settings(use_empty=True)

# ---- FUSELAGE: real cross-sections, not a body of revolution ------------------------------------------------
# (station aft of the nose, half-width, bottom z, top z) — a 172 is flat-sided with a rounded turtledeck, a
# squared-off cabin and a boom that tapers to the fin. x here is +forward from the CG, matching the config.
NOSE = WING_X + 2.05
# MEASURED, not guessed. tools/measure_model.py slices the reference model at 18 stations, scales by the
# known 10.92 m span and reports the body's half-width every 0.45 m. The first pass of this file had these
# numbers INVENTED from a general sense of what a 172 looks like, and it put the widest point of the cabin
# nearly two metres too far forward — which is why it read as a blimp. The protocol said so all along:
# NEW-AIRPLANE-PROTOCOL.md 1b, "work from a dimensioned three-view, scale it by the known span, then measure".
# Measuring a reference for DIMENSIONS is not copying it: the shape of a real 172 is a fact about an aeroplane
# Cessna built, and our mesh is built from the numbers with its own topology.
#
# Two slices were repaired by interpolation (0.91 and 7.24 m) where the cut fell in a gap and read near zero.
# The depths are a profile over the measured widths rather than measured themselves — a slice also catches the
# gear below and the fin above, and the vertical did not come out clean enough to trust.
#   (station aft of the spinner, half-width, bottom, top) — all metres, z about the thrust line
# THE THREE-VIEW, derived rather than drawn. tools/average_sections.py slices THREE independent references
# (Meee's untextured WIP, ollietaylor219's "True Scale and Accurate", Viva_1's) at 19 stations, scales each by
# the known 10.92 m span, normalises by its own length, and takes the MEDIAN half-width at each station.
#
# Why three, and why the median: averaging the MESHES would be a derivative of all three and would mush the
# sharp features; averaging the MEASUREMENTS converges on the real aeroplane, because a 172's shape is a fact
# about a machine Cessna built. The median rather than the mean because one bad slice ruins a mean — at 0.46 m
# two references said 0.404 and 0.335 and the third said 0.097 (its cut fell between spinner and cowl), and the
# mean returned 0.279, a number none of them measured.
#
# Where they AGREE (spread under 0.07 m) is 1.8 m to 5.9 m — the cabin and the whole aft taper — so those
# numbers are trustworthy. They disagree at the nose (spinner, nose gear) and over the tailplane root, where
# the body-isolating band catches other structure; the two small bumps that left at 6.84 and 7.75 m are the
# tailplane fairing and are smoothed out here.
#
# The result: a 1.11 m external cabin, widest 3.2 m from the nose. The first version of this file GUESSED the
# widest point at about 1.8 m, which is why it read as a blimp.
#   (station aft of the spinner, half-width, bottom, top) — metres, z about the thrust line
STATIONS = [
    (0.00, 0.073, -0.09, 0.09),
    (0.46, 0.335, -0.35, 0.29),
    (0.91, 0.443, -0.45, 0.40),
    (1.37, 0.496, -0.51, 0.58),
    (1.82, 0.563, -0.55, 0.74),
    (2.28, 0.538, -0.55, 0.78),
    (2.73, 0.556, -0.55, 0.78),
    (3.19, 0.543, -0.54, 0.74),
    (3.64, 0.532, -0.52, 0.64),
    (4.10, 0.468, -0.47, 0.52),
    (4.56, 0.414, -0.42, 0.45),
    (5.01, 0.366, -0.37, 0.40),
    (5.47, 0.302, -0.31, 0.35),
    (5.92, 0.253, -0.26, 0.31),
    (6.38, 0.228, -0.23, 0.28),
    (6.84, 0.195, -0.20, 0.26),
    (7.30, 0.160, -0.17, 0.24),
    (7.75, 0.110, -0.12, 0.22),
    (8.21, 0.020, -0.05, 0.19),
]
SECT = 24   # enough to read a radiused corner; the plan's F1 has a generous one


def body_loop(hw, zb, zt, squareness):
    """One fuselage section, as a SUPERELLIPSE.

    Section A-A of the Marutaka 1:8 plan (assets-incoming/plans-c172/oz3838) is former F1 at the firewall, and
    it is a ROUNDED SQUARE -- width about equal to height, corners radiused at roughly a fifth of the width.
    Not an ellipse. That is the shape an RC plan gives you that slicing a mesh cannot: a slice reports a width
    and a height, never an outline.

    Parametrically a superellipse is  x = a*sgn(cos t)*|cos t|^e,  y = b*sgn(sin t)*|sin t|^e, where e = 1 is
    an ellipse and smaller e squares it off. e = 0.55 matches F1; the nose and the tail boom relax back toward
    a true ellipse, which is what the plan's other sections show.
    """
    pts = []
    zc, zr = (zt + zb) * 0.5, (zt - zb) * 0.5
    for i in range(SECT):
        a = 2 * math.pi * i / SECT
        cs, sn = math.cos(a), math.sin(a)
        y = hw * math.copysign(abs(cs) ** squareness, cs)
        z = zc + zr * math.copysign(abs(sn) ** squareness, sn)
        pts.append((0.0, y, z))
    return pts


def squareness_at(frac):
    """How square the section is along the body: a true ellipse at the spinner, squared off through the
    firewall and cabin (the plan's F1), easing back to an ellipse down the tail boom."""
    if frac < 0.08: return 1.00
    if frac < 0.22: return 1.00 - (frac - 0.08) / 0.14 * 0.45    # 1.00 -> 0.55 through the cowl
    if frac < 0.50: return 0.55                                   # firewall and cabin
    return 0.55 + (frac - 0.50) / 0.50 * 0.40                     # 0.55 -> 0.95 down the boom


loops = []
BODY_LEN = STATIONS[-1][0]
for st_s, hw, zb, zt in STATIONS:
    loop = [(NOSE - st_s, p[1], p[2]) for p in body_loop(hw, zb, zt, squareness_at(st_s / BODY_LEN))]
    loops.append(loop)
fus = mesh_from_loops("Fuselage", loops)

# ---- WINGS --------------------------------------------------------------------------------------------------
TIP_CHORD = ROOT_CHORD * 0.68                 # the 172's semi-tapered outer panel
FLAP_SPAN = 0.52                              # inboard 52 % of the half-span carries flap
AIL_START = 0.62


def wing_section(chord, y, z, le_x):
    return [(le_x - px * chord, y, z - py * chord) for px, py in naca2412()]


def build_panel(side, frac0, frac1, name, chord_frac=(0.0, 1.0)):
    """A spanwise piece of wing between two span fractions, and between two CHORD fractions — which is how a
    flap or an aileron is cut out of the same section as the wing it belongs to."""
    loops = []
    for f in (frac0, frac1):
        y = side * f * HALF_SPAN
        chord = ROOT_CHORD + (TIP_CHORD - ROOT_CHORD) * f
        le = WING_X + chord * 0.25
        prof = naca2412()
        c0, c1 = chord_frac
        pts = []
        for px, py in prof:
            if px < c0 or px > c1:
                continue
            pts.append((le - px * chord, y, WING_Z - py * chord))
        if len(pts) < 3:
            pts = [(le - px * chord, y, WING_Z - py * chord) for px, py in prof]
        loops.append(pts)
    return mesh_from_loops(name, loops)


parts = [fus]
for side, tag in ((-1, "L"), (1, "R")):
    parts.append(build_panel(side, 0.02, 1.0, "Wing." + tag))
    # flap and aileron as their own objects, cut from the aft 28 % of the section
    fl = build_panel(side, 0.10, FLAP_SPAN, "Flap." + tag, chord_frac=(0.72, 1.0))
    al = build_panel(side, AIL_START, 0.97, "Aileron." + tag, chord_frac=(0.72, 1.0))
    # origins ON the hinge lines
    for ob, f0, f1 in ((fl, 0.10, FLAP_SPAN), (al, AIL_START, 0.97)):
        fm = (f0 + f1) * 0.5
        chord = ROOT_CHORD + (TIP_CHORD - ROOT_CHORD) * fm
        set_origin(ob, (WING_X + chord * 0.25 - 0.72 * chord, side * fm * HALF_SPAN, WING_Z))
        parts.append(ob)

print("C172 parts: %s" % [o.name for o in parts])
print("C172 half-span %.2f  root chord %.2f  wing area %.2f  nose at x %.2f" % (HALF_SPAN, ROOT_CHORD, WING_AREA, NOSE))


# ---- TAIL: positions from the config, so the drawn tail is the tail that flies -------------------------------
def surf_stats(sid):
    sf = next((s for s in CFG["surfaces"] if s["id"] == sid), None)
    if sf is None or not sf["strips"]:
        return None
    st = sf["strips"]
    return dict(x=sum(s["pos"][0] for s in st) / len(st),
                z=bz(sum(s["pos"][2] for s in st) / len(st)),
                chord=sum(s["chord"] for s in st) / len(st),
                area=sum(s["area"] for s in st),
                span=max(abs(s["pos"][1]) for s in st) * 2,
                ztop=bz(min(s["pos"][2] for s in st)), zbot=bz(max(s["pos"][2] for s in st)))


def slab(name, x_le, chord, y0, y1, z, thick, vertical=False, taper=1.0):
    """A symmetric-section surface (the tail is NACA 0009-ish) between two stations."""
    loops = []
    for t, y in ((0.0, y0), (1.0, y1)):
        c = chord * (1 - t * (1 - taper))
        pts = []
        for i in range(24):
            a = 2 * math.pi * i / 24
            px = 0.5 * (1 - math.cos(a if a < math.pi else 2 * math.pi - a))
            py = thick * 0.5 * (0.2969 * math.sqrt(px) - 0.126 * px - 0.3516 * px**2 + 0.2843 * px**3 - 0.1036 * px**4) * 5
            py = py if a < math.pi else -py
            if vertical:
                pts.append((x_le - px * c, py * c, y))
            else:
                pts.append((x_le - px * c, y, z - py * c))
        loops.append(pts)
    return mesh_from_loops(name, loops)


hs, vs = surf_stats("hStab"), surf_stats("vStab")
if hs:
    hc = hs["chord"] * 2.0                                  # stab + elevator together
    x_le = hs["x"] + hc * 0.25
    parts.append(slab("HStab", x_le, hc * 0.62, -hs["span"] / 2, hs["span"] / 2, hs["z"], 0.09, taper=0.75))
    el = slab("Elevator", x_le - hc * 0.62, hc * 0.38, -hs["span"] / 2 * 0.98, hs["span"] / 2 * 0.98, hs["z"], 0.08, taper=0.8)
    set_origin(el, (x_le - hc * 0.62, 0.0, hs["z"]))
    parts.append(el)
if vs:
    vc = vs["chord"] * 2.0
    x_le = vs["x"] + vc * 0.25
    ztop, zbot = vs["ztop"], vs["zbot"]
    parts.append(slab("Fin", x_le, vc * 0.60, zbot, ztop, 0.0, 0.10, vertical=True, taper=0.55))
    rd = slab("Rudder", x_le - vc * 0.60, vc * 0.40, zbot, ztop * 0.98, 0.0, 0.09, vertical=True, taper=0.7)
    set_origin(rd, (x_le - vc * 0.60, 0.0, (ztop + zbot) * 0.5))
    parts.append(rd)

# ---- GEAR: the nose leg is its own object and its ORIGIN IS THE STEERING AXIS --------------------------------
def cyl(name, p0, p1, r, seg=12):
    a, b = Vector(p0), Vector(p1)
    axis = (b - a)
    L = axis.length
    if L < 1e-6:
        return None
    axis.normalize()
    up = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
    u = axis.cross(up).normalized()
    v = axis.cross(u).normalized()
    loops = []
    for end in (a, b):
        loops.append([tuple(end + u * (r * math.cos(2 * math.pi * i / seg)) + v * (r * math.sin(2 * math.pi * i / seg)))
                      for i in range(seg)])
    return mesh_from_loops(name, loops)


for g in CFG.get("gear", []):
    gx, gy, gz = g["pos"][0], g["pos"][1], bz(g["pos"][2])
    nose = g.get("isSteerable", False) and not g.get("isTailwheel", False)
    tag = "Nose" if nose else ("Main.L" if gy < 0 else "Main.R")
    # gz is the wheel's CONTACT patch; the axle sits a radius above it and the leg runs up from there
    axle = gz + 0.16
    top = (gx, gy, axle + 0.62)
    leg = cyl("Gear.%s.Leg" % tag, top, (gx, gy, axle), 0.045)
    whl = cyl("Gear.%s.Wheel" % tag, (gx, gy - 0.055, axle), (gx, gy + 0.055, axle), 0.16, seg=16)
    grp = [o for o in (leg, whl) if o]
    if nose:
        # one object, origin at the TOP of the leg: that point is the steering axis, so Unity steers it by a
        # local rotation about z and nothing has to be solved at runtime.
        for o in grp:
            o.select_set(True)
        bpy.context.view_layer.objects.active = grp[0]
        bpy.ops.object.join()
        joined = bpy.context.view_layer.objects.active
        joined.name = "Gear.Nose.Steer"
        set_origin(joined, top)
        parts.append(joined)
        bpy.ops.object.select_all(action='DESELECT')
    else:
        parts += grp

# ---- PROP, SPINNER, STRUTS -----------------------------------------------------------------------------------
px = NOSE + 0.10
spin = mesh_from_loops("Spinner", [[(px - 0.26, 0.11 * math.cos(2 * math.pi * i / 12), 0.11 * math.sin(2 * math.pi * i / 12)) for i in range(12)],
                                   [(px, 0.02 * math.cos(2 * math.pi * i / 12), 0.02 * math.sin(2 * math.pi * i / 12)) for i in range(12)]])
parts.append(spin)
blades = []
for k in (0, 1):
    ang = math.pi * k
    tipz, tipy = math.cos(ang) * PROP_D / 2, math.sin(ang) * PROP_D / 2
    b = mesh_from_loops("Blade%d" % k,
                        [[(px - 0.05, 0.02 * math.cos(a) + tipy * 0.08, 0.09 * math.sin(a) + tipz * 0.08) for a in
                          [2 * math.pi * i / 8 for i in range(8)]],
                         [(px - 0.01, 0.006 * math.cos(a) + tipy, 0.03 * math.sin(a) + tipz) for a in
                          [2 * math.pi * i / 8 for i in range(8)]]])
    blades.append(b)
for b in blades:
    b.select_set(True)
bpy.context.view_layer.objects.active = blades[0]
bpy.ops.object.join()
prop = bpy.context.view_layer.objects.active
prop.name = "Prop"
set_origin(prop, (px, 0, 0))
parts.append(prop)
bpy.ops.object.select_all(action='DESELECT')

# the 172's lift struts
for side in (-1, 1):
    s = cyl("Strut.%s" % ("L" if side < 0 else "R"),
            (WING_X - 0.10, side * HALF_SPAN * 0.46, WING_Z - 0.02), (WING_X - 0.35, side * 0.50, -0.42), 0.035)
    if s:
        parts.append(s)

# ---- smooth, thicken the single-sided surfaces, and export ---------------------------------------------------
# Shade smooth. Mesh.use_auto_smooth was removed in Blender 4.1 — the angle-limited version is now a
# "Smooth by Angle" modifier — so mark the polygons smooth and let the exporter write split normals.
for ob in parts:
    for poly in ob.data.polygons:
        poly.use_smooth = True

OUT = os.path.join(REPO, "unity/Assets/_Project/Resources/Models/c172-like/c172-like-own.glb")
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True,
                          export_apply=True, export_yup=True)
tris = sum(len(o.data.polygons) for o in parts)
print("C172 EXPORT %s" % OUT)
print("C172 parts=%d faces=%d" % (len(parts), tris))
print("C172 names=%s" % sorted(o.name for o in parts))
