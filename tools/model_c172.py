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
WING_AREA = sum(s["area"] for s in STRIPS)
HALF_SPAN = max(abs(s["pos"][1]) for s in STRIPS)

# THE DATUM. This used to average pos[0] over every surface whose id starts with "wing" -- which is the wing
# AND THE AILERON, two surfaces a metre apart in x. The mean of the two is a point on neither of them, and
# every single thing in this file was positioned from it, so the whole aeroplane sat about 0.9 m out of place
# relative to its own wing. The config's aileron is its own surface precisely so the physics can hinge it;
# it is not part of the wing's reference station.
MAIN = next(sf for sf in CFG["surfaces"] if sf["id"] == "wing")
AIL = next((sf for sf in CFG["surfaces"] if sf["id"] == "wing-aileron"), None)
WING_X = sum(s["pos"][0] for s in MAIN["strips"]) / len(MAIN["strips"])   # quarter-chord station
WING_Z = bz(sum(s["pos"][2] for s in MAIN["strips"]) / len(MAIN["strips"]))  # UP-positive, above the CG
ROOT_CHORD = max(s["chord"] for s in MAIN["strips"])


def total_chord(surface, extra, y):
    """A control surface is modelled separately from the panel it is cut out of, so the real chord at a
    station is the panel's plus the control's. Reading only the panel gave a tip chord a third short."""
    c = max((st["chord"] for st in surface["strips"] if abs(st["pos"][1] - y) < 1e-3), default=0.0)
    if extra:
        c += max((st["chord"] for st in extra["strips"] if abs(st["pos"][1] - y) < 1e-3), default=0.0)
    return c


_tip_y = max(s["pos"][1] for s in MAIN["strips"])
TIP_CHORD = total_chord(MAIN, AIL, _tip_y)

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


def grid_mesh(name, rows):
    """Bridge rows of points into an OPEN sheet -- mesh_from_loops closes each ring, which a window is not."""
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
    bm.normal_update(); bm.to_mesh(me); bm.free()
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
# WHERE THE NOSE IS, measured rather than assumed. Both references put the wing's root leading edge 2.06 m
# aft of the spinner (WIP 2.136, True-Scale 1.985), so the body's station 0 is that far ahead of the LE the
# config implies. The old value -- "WING_X + 2.05" -- was 2.05 m ahead of the bogus wing/aileron mean datum,
# which put the whole fuselage about 0.9 m out of place against its own wing.
LE_ROOT = WING_X + ROOT_CHORD * 0.25
NOSE = LE_ROOT + 2.06
# MEASURED, not guessed. tools/measure_model.py slices the reference model at 18 stations, scales by the
# known 10.92 m span and reports the body's half-width every 0.45 m. The first pass of this file had these
# numbers INVENTED from a general sense of what a 172 looks like, and it put the widest point of the cabin
# nearly two metres too far forward — which is why it read as a blimp. The protocol said so all along:
# NEW-AIRPLANE-PROTOCOL.md 1b, "work from a dimensioned three-view, scale it by the known span, then measure".
# Measuring a reference for DIMENSIONS is not copying it: the shape of a real 172 is a fact about an aeroplane
# Cessna built, and our mesh is built from the numbers with its own topology.
#
# Two slices were repaired by interpolation (0.91 and 7.24 m) where the cut fell in a gap and read near zero.
#
# THE HALF-WIDTHS are the MEDIAN of three independent references (tools/average_sections.py slices Meee's
# untextured WIP, ollietaylor219's "True Scale and Accurate" and Viva_1's at 19 stations, scales each by the
# known 10.92 m span and normalises by its own length). Median rather than mean because one bad slice ruins a
# mean: at 0.46 m two references said 0.404 and 0.335 and the third said 0.097 -- its cut fell between
# spinner and cowl -- and the mean returned 0.279, a number none of them measured. They agree to better than
# 0.07 m from 1.8 m to 5.9 m, which is the cabin and the whole aft taper.
#
# THE SIDE VIEW IS MEASURED NOW. The first version of this table carried a note admitting the worst thing in
# it: "the depths are a profile over the measured widths rather than measured themselves". The half-widths
# were measured and THE TOP AND BOTTOM LINES WERE INVENTED -- so the aeroplane had a correct plan view and a
# made-up side view, which is why it read as a teardrop with no windscreen in it. A 172 lives in its side
# view. Two measurements now produce this table and they were each wrong once before they were right:
#
#   THE BELLY comes from slicing the reference (tools/measure_model.py, edge()), which bins a slice by height
#   and takes the outermost bin still a real fraction of the body's width -- stepping over the fin, the legs
#   and the wheels, because those are THIN IN SPAN and the skin is not. It checks out against the Marutaka
#   plan's side view: -0.712 m under the thrust line at the cabin, against -0.71 measured. Independent
#   sources, 2 mm apart.
#
#   THE TOP LINE could not come from a slice at all, because at the cabin a slice catches THE WING, which is
#   as wide as anything and sits right on the roof. Cutting the wing away at |y| < 0.75 m does not help --
#   that leaves its ROOT in the picture. So the body is isolated BY CONNECTED COMPONENT: a wing is one piece
#   spanning more than any fuselage does, so every island wider than 1.4 m is thrown away and the body, fin
#   and undercarriage are what is left. Rendered as a white silhouette on black through an orthographic
#   camera and read back column by column, which gives the outline at 5 mm.
#
#   That is what finally produced a windscreen: the deck is at +0.407 at station 1.80 and the roof corner at
#   +0.670 at 1.98, a rake of about 52 degrees. Measuring through the wing had said +0.72 all the way from
#   1.98 to 2.73 -- a flat roof and no windscreen, which is the teardrop again by another route.
#
# The belly is three-point smoothed (the slice quantises to 30 mm); the top line is not, because its one big
# step is the windscreen and that is a real break in the surface, not noise. Stations are clustered at
# 1.80/1.98/2.05 to hold the windscreen's corners and at 3.84/4.00/4.10 to hold the drop behind the cabin.
# The two ends are closed by hand: a slice at the spinner catches the propeller and one at the tailpost
# catches the fin.
#   (station aft of the spinner, half-width, bottom, top) -- all metres, z about the thrust line
STATIONS = [
    (0.00, 0.073, -0.073, +0.073),
    (0.46, 0.335, -0.368, +0.223),
    (0.91, 0.443, -0.596, +0.287),
    (1.37, 0.496, -0.671, +0.330),
    (1.62, 0.533, -0.698, +0.360),
    (1.80, 0.560, -0.712, +0.407),
    (1.98, 0.554, -0.713, +0.670),
    (2.05, 0.550, -0.712, +0.681),
    (2.28, 0.538, -0.699, +0.634),
    (2.73, 0.556, -0.715, +0.622),
    (3.19, 0.543, -0.680, +0.600),
    (3.64, 0.532, -0.623, +0.520),
    (3.84, 0.504, -0.599, +0.330),
    (4.00, 0.482, -0.581, +0.275),
    (4.10, 0.468, -0.569, +0.262),
    (4.56, 0.414, -0.535, +0.250),
    (5.01, 0.366, -0.488, +0.222),
    (5.47, 0.302, -0.419, +0.180),
    (5.92, 0.253, -0.401, +0.150),
    (6.38, 0.228, -0.332, +0.093),
    (6.84, 0.195, -0.291, +0.064),
    (7.30, 0.160, -0.228, +0.056),
    (7.75, 0.110, -0.189, +0.043),
    (8.21, 0.020, -0.150, +0.030),
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


# ---- GLAZING: windscreen, door windows, rear windows ---------------------------------------------------------
# THE CABIN IS WHAT MAKES A 172 READ AS A 172. Until now the body was a blank shell, and no amount of correct
# section data rescues an aeroplane with no windows in it -- the owner looked at the last render and said so.
#
# Each pane is built from THE SAME SUPERELLIPSE AS THE SKIN, between two stations and two angles round the
# section, then lifted 8 mm clear. Because it is the same surface, it cannot float off the body or sink into
# it however the sections change; it follows the windscreen's rake and the cabin's taper for free. Sitting on
# the skin rather than cut out of it also means the fuselage stays one watertight loft, which is what Unity's
# CarveModel and the UV generator expect.


def station_at(s_m):
    """Interpolate the measured table: half-width, bottom, top at any station."""
    if s_m <= STATIONS[0][0]:
        return STATIONS[0][1:]
    if s_m >= STATIONS[-1][0]:
        return STATIONS[-1][1:]
    for a, b in zip(STATIONS, STATIONS[1:]):
        if a[0] <= s_m <= b[0]:
            f = (s_m - a[0]) / (b[0] - a[0])
            return tuple(a[i] + (b[i] - a[i]) * f for i in (1, 2, 3))


def skin_point(s_m, ang):
    """A point ON the fuselage skin at a station and an angle round the section (0 = right side, 90 = top)."""
    hw, zb, zt = station_at(s_m)
    e = squareness_at(s_m / BODY_LEN)
    zc, zr = (zt + zb) * 0.5, (zt - zb) * 0.5
    cs, sn = math.cos(ang), math.sin(ang)
    y = hw * math.copysign(abs(cs) ** e, cs)
    z = zc + zr * math.copysign(abs(sn) ** e, sn)
    return y, z, zc


def ang_at_z(s_m, z):
    """The angle round the section at a given HEIGHT -- which is how a side window is specified, because a
    side view is what we measured. Inverts the superellipse."""
    hw, zb, zt = station_at(s_m)
    e = squareness_at(s_m / BODY_LEN)
    zc, zr = (zt + zb) * 0.5, (zt - zb) * 0.5
    t = max(-1.0, min(1.0, (z - zc) / zr if zr > 1e-6 else 0.0))
    return math.asin(math.copysign(abs(t) ** (1.0 / e), t))


def ang_at_yfrac(s_m, f):
    """The angle in the TOP half of the section at a given fraction of the half-width -- which is how a
    windscreen is specified, because a windscreen covers the top of the section out to nearly its full
    width at the bottom and narrows as it reaches the roof."""
    e = squareness_at(s_m / BODY_LEN)
    return math.acos(max(-1.0, min(1.0, max(f, 0.0) ** (1.0 / e))))


def pane(name, s0, s1, lo0, hi0, lo1, hi1, by, ns=7, na=7, lift=0.012):
    """A window: the patch of skin between two stations, bounded by a pair of limits at each end.

    `by` is "z" for a side window, whose limits are heights, or "yfrac" for a windscreen, whose limits are
    fractions of the half-width and which wraps symmetrically over the top. Specifying a windscreen by
    SECTION ANGLE, which is what the first attempt did, puts its lower edge wherever the rounded belly says
    rather than on the cowl deck, and what came out was a dark smudge down the side of the nose.
    """
    rows = []
    for i in range(ns + 1):
        f = i / ns
        s_m = s0 + (s1 - s0) * f
        lo = lo0 + (lo1 - lo0) * f
        hi = hi0 + (hi1 - hi0) * f
        if by == "yfrac":
            a = ang_at_yfrac(s_m, lo)          # lo is the half-width fraction the glass reaches down to
            a_lo, a_hi = a, math.pi - a
        else:
            a_lo, a_hi = ang_at_z(s_m, lo), ang_at_z(s_m, hi)
        row = []
        for j in range(na + 1):
            ang = a_lo + (a_hi - a_lo) * j / na
            y, z, zc = skin_point(s_m, ang)
            r = math.hypot(y, z - zc)
            k = (r + lift) / r if r > 1e-6 else 1.0
            row.append((NOSE - s_m, y * k, zc + (z - zc) * k))
        rows.append(row)
    return grid_mesh(name, rows)


def mirror(ob, name):
    """The left-hand pane: the same glass, reflected. A window is symmetric and saying so twice is not."""
    me = ob.data.copy()
    for v in me.vertices:
        v.co.y = -v.co.y
    cp = bpy.data.objects.new(name, me)
    cp.location = ob.location.copy()
    cp.location.y = -cp.location.y
    bpy.context.collection.objects.link(cp)
    return cp


# Read straight off the measured side view. The windscreen is the break in the top line between 1.62 and
# 2.05; the doors run from the windscreen post to the wing's trailing edge with their sills at z = +0.30,
# which is about 0.75 m above the cabin floor; the rear windows fill the turtledeck before it drops at 3.84.
_ws = pane("Glass.Windscreen", 1.66, 2.06, 0.93, 0.0, 0.74, 0.0, "yfrac", ns=8, na=16)
_dr = pane("Glass.Door.R", 2.14, 3.08, 0.30, 0.60, 0.31, 0.58, "z", ns=6, na=7)
_rr = pane("Glass.Rear.R", 3.18, 3.86, 0.32, 0.56, 0.37, 0.45, "z", ns=6, na=6)
glazing = [_ws, _dr, _rr, mirror(_dr, "Glass.Door.L"), mirror(_rr, "Glass.Rear.L")]

# ---- WINGS --------------------------------------------------------------------------------------------------
TAPER_START = 0.58                            # a 172's wing is CONSTANT CHORD inboard and tapers only outside
FLAP_SPAN = 0.52                              # inboard 52 % of the half-span carries flap
AIL_START = 0.62
TIP_R = 0.11                                  # the radius the tip rolls over through
DIHEDRAL = math.radians(1.73)                 # the published 172 figure; the config's strips are all at one
                                              # z, so the physics does not carry it and the drawing must


def chord_at(f):
    """Chord at a span fraction. The straight inboard panel is most of what a 172's planform IS -- a single
    linear taper from root to tip is a Cherokee, not a Skyhawk."""
    if f <= TAPER_START:
        return ROOT_CHORD
    g = (f - TAPER_START) / (1.0 - TAPER_START)
    return ROOT_CHORD + (TIP_CHORD - ROOT_CHORD) * g


def wing_loop(side, f, chord_frac, shrink=1.0, y_extra=0.0):
    """One section of wing at a span fraction, optionally shrunk toward its chord line for a rounded tip."""
    chord = chord_at(f)
    le = WING_X + chord * 0.25
    c0, c1 = chord_frac
    # rounding the tip pulls the section in from BOTH edges as well as thinning it, which is what gives a
    # 172 its blunt rolled tip rather than the square cut the first pass left
    mid = (c0 + c1) * 0.5
    pts = []
    for px, py in naca2412():
        if px < c0 or px > c1:
            continue
        qx = mid + (px - mid) * (0.40 + 0.60 * shrink)
        y = f * HALF_SPAN + y_extra
        pts.append((le - qx * chord, side * y, WING_Z + py * chord * shrink + y * math.tan(DIHEDRAL)))
    return pts


def build_panel(side, frac0, frac1, name, chord_frac=(0.0, 1.0), round_tip=False):
    """A spanwise piece of wing between two span fractions, and between two CHORD fractions -- which is how a
    flap or an aileron is cut out of the same section as the wing it belongs to."""
    fs = [frac0, TAPER_START, frac1] if frac0 < TAPER_START < frac1 else [frac0, frac1]
    loops = [wing_loop(side, f, chord_frac) for f in sorted(set(fs))]
    if round_tip:
        for i in range(1, 4):
            a = math.pi * 0.5 * i / 3.0
            loops.append(wing_loop(side, frac1, chord_frac,
                                   shrink=max(math.cos(a), 0.04), y_extra=TIP_R * math.sin(a)))
    return mesh_from_loops(name, loops)


parts = [fus] + glazing
for side, tag in ((-1, "L"), (1, "R")):
    parts.append(build_panel(side, 0.02, 1.0, "Wing." + tag, round_tip=True))
    # flap and aileron as their own objects, cut from the aft 28 % of the section
    fl = build_panel(side, 0.10, FLAP_SPAN, "Flap." + tag, chord_frac=(0.72, 1.0))
    al = build_panel(side, AIL_START, 0.97, "Aileron." + tag, chord_frac=(0.72, 1.0))
    # origins ON the hinge lines
    for ob, f0, f1 in ((fl, 0.10, FLAP_SPAN), (al, AIL_START, 0.97)):
        fm = (f0 + f1) * 0.5
        chord = chord_at(fm)
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


def tail_panel(name, x_le_root, root_c, tip_c, a0, a1, sweep, frac0, frac1, vertical, thick=0.09, n=8):
    """A piece of a tail surface between two CHORD FRACTIONS of one planform.

    A fin and its rudder are not two shapes, they are ONE outline split at a hinge line. Building them as
    separate slabs with their own tapers opened a wedge-shaped gap toward the tip -- the fin's trailing edge
    swept forward as it rose and the rudder's leading edge stayed put, so the aeroplane appeared to have two
    vertical tails (2026-10-21, owner: "two vertical tails or a detached rudder"). Here both come from the same
    planform, so the split follows the taper by construction and cannot gape.

    a0/a1 are the span stations (z for a fin, y for a tailplane); sweep is the leading edge's rake per unit.
    """
    loops = []
    for t in (0.0, 1.0):
        a = a0 + (a1 - a0) * t
        c = root_c + (tip_c - root_c) * t
        le = x_le_root - sweep * abs(a - a0)
        pts = []
        for i in range(n * 2):
            ang = 2 * math.pi * i / (n * 2)
            px = frac0 + (frac1 - frac0) * 0.5 * (1 - math.cos(ang if ang < math.pi else 2 * math.pi - ang))
            # a symmetric section, thinned toward the tip the way a real surface is
            th = thick * 5 * (0.2969 * math.sqrt(max(px, 1e-6)) - 0.126 * px - 0.3516 * px**2
                              + 0.2843 * px**3 - 0.1036 * px**4) * c * 0.5
            th = th if ang < math.pi else -th
            if vertical:
                pts.append((le - px * c, th, a))
            else:
                pts.append((le - px * c, a, th))
        loops.append(pts)
    return mesh_from_loops(name, loops)


hs, vs = surf_stats("hStab"), surf_stats("vStab")
HINGE = 0.62                                   # the elevator and rudder take the aft 38 % of the chord


def half_step(sid, axis=1):
    """Half the gap between adjacent strips.

    A STRIP'S pos IS ITS CENTRE, not its tip. Reading the span as max|y| therefore stops half a strip short
    at each end: the config's eight tailplane strips run out to +-1.509 m, which reads as a 3.02 m span when
    the aeroplane it describes has 3.45 m. The wing happens not to suffer -- its outer strips are bunched at
    the tip -- which is exactly why this was easy to miss on the surface that matters most.
    """
    sf = next((x for x in CFG["surfaces"] if x["id"] == sid), None)
    vals = sorted(set(round(st["pos"][axis], 4) for st in sf["strips"])) if sf else []
    return (vals[-1] - vals[-2]) * 0.5 if len(vals) > 1 else 0.0


def pair_chord(sid, other, axis=1, outer=True, signed=False):
    """Panel chord plus control chord at the same station: the surface's REAL chord there.

    A tailplane is symmetric about y, so picking the station by min/max of pos[1] lands on the two TIPS and
    reports the same chord twice. Key on |y| for a surface that has two sides; a fin has one, and its strips
    run up in z, so there min/max is the right question.
    """
    sf = next(x for x in CFG["surfaces"] if x["id"] == sid)
    ex = next((x for x in CFG["surfaces"] if x["id"] == other), None)
    val = (lambda st: st["pos"][axis]) if signed else (lambda st: abs(st["pos"][axis]))
    key = (max if outer else min)(val(st) for st in sf["strips"])
    c = max(st["chord"] for st in sf["strips"] if abs(val(st) - key) < 1e-3)
    if ex:
        c += max((st["chord"] for st in ex["strips"] if abs(val(st) - key) < 1e-3), default=0.0)
    return c


if hs:
    # root and tip chord from the stabiliser PLUS the elevator, instead of doubling the mean -- the old
    # "chord * 2.0" happened to land near the root chord and made the tip a third too wide.
    hc = pair_chord("hStab", "elevator", 1, outer=False)
    htip = pair_chord("hStab", "elevator", 1, outer=True)
    x_le = hs["x"] + hc * 0.25
    half = hs["span"] / 2 + half_step("hStab", 1)
    for nm, f0, f1, hinged in (("HStab", 0.0, HINGE, False), ("Elevator", HINGE, 1.0, True)):
        for side in (-1, 1):
            ob = tail_panel(nm, x_le, hc, htip, 0.0, side * half, hc * 0.18, f0, f1, vertical=False)
            if hinged:
                ob.name = "Elevator.%s" % ("L" if side < 0 else "R")
                set_origin(ob, (x_le - HINGE * hc, side * half * 0.5, hs["z"]))
            else:
                ob.name = "HStab.%s" % ("L" if side < 0 else "R")
            # lift the whole tailplane to its station
            for v in ob.data.vertices: v.co.z += hs["z"]
            parts.append(ob)
if vs:
    # the fin's strips run up the FIN, so the half-step is in z and the root/tip chords come from its ends
    vstep = half_step("vStab", 2)
    vc = pair_chord("vStab", "rudder-vstab", 2, outer=True, signed=True)     # z is DOWN-positive, so "max z" is the ROOT
    vtip = pair_chord("vStab", "rudder-vstab", 2, outer=False, signed=True)
    x_le = vs["x"] + vc * 0.25
    # THE FIN HAS TO REACH THE BOOM. The config's lowest strip is the lowest place the fin makes LIFT, not
    # the lowest place it has structure, and it sits about 0.27 m above the tailcone -- so building the root
    # at the strip edge leaves the fin floating. Take the root from where the body's own skin is at the fin's
    # mid-chord and bury it 50 mm.
    x_mid = x_le - vc * 0.5
    zbot = station_at(NOSE - x_mid)[2] - 0.05
    ztop = vs["ztop"] + vstep
    # a 172 stands 2.72 m tall on its wheels, and the config puts the wheels 1.15 m below the thrust line,
    # so nothing on this aeroplane reaches higher than 1.57 m above it. The strips alone overshoot.
    ztop = min(ztop, HEIGHT - max(g["pos"][2] for g in CFG.get("gear", [{"pos": [0, 0, 1.15]}])))
    fin = tail_panel("Fin", x_le, vc, vtip, zbot, ztop, vc * 0.42, 0.0, HINGE, vertical=True, thick=0.10)
    parts.append(fin)
    rud = tail_panel("Rudder", x_le, vc, vtip, zbot, ztop, vc * 0.42, HINGE, 1.0, vertical=True, thick=0.09)
    set_origin(rud, (x_le - HINGE * vc, 0.0, (ztop + zbot) * 0.5))
    parts.append(rud)

    # THE DORSAL FIN -- the fillet running forward from the fin's leading edge along the top of the tailcone.
    # It is one of the few lines that says "Cessna" from a distance, and without it the fin looks bolted on.
    def tri(xa, za, xb, zb_, xc, zc, t=0.022):
        loops = []
        for side in (-1, 1):
            loops.append([(xa, side * t * 0.3, za), (xb, side * t, zb_), (xc, side * t, zc)])
        return mesh_from_loops("Fin.Dorsal", loops)

    # forward is +x, so a station FORWARD of the fin is a SMALLER distance aft of the nose
    s_fwd = (NOSE - x_le) - 1.55            # the fillet starts 1.55 m ahead of the fin
    parts.append(tri(NOSE - s_fwd, station_at(s_fwd)[2] - 0.01,
                     x_le, zbot + (ztop - zbot) * 0.30,
                     x_le, station_at(NOSE - x_le)[2] - 0.01))

print("C172 tail: hstab span %.2f root %.2f tip %.2f | fin %.2f..%.2f root %.2f tip %.2f"
      % (half * 2, hc, htip, zbot, ztop, vc, vtip))

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


def sweep(name, path, radii, seg=10, flat=1.0):
    """A tube swept along a path, with a radius per point. `flat` squashes the section in z, which is what
    turns a round tube into the flat spring strip a 172's main leg actually is."""
    loops = []
    for i, c in enumerate(path):
        c = Vector(c)
        nxt = Vector(path[min(i + 1, len(path) - 1)])
        prv = Vector(path[max(i - 1, 0)])
        ax = (nxt - prv)
        if ax.length < 1e-6:
            ax = Vector((0, 0, -1))
        ax.normalize()
        up = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
        u = ax.cross(up).normalized()
        v = ax.cross(u).normalized()
        r = radii[i]
        loops.append([tuple(c + u * (r * math.cos(2 * math.pi * j / seg))
                              + v * (r * flat * math.sin(2 * math.pi * j / seg))) for j in range(seg)])
    return mesh_from_loops(name, loops)


def bend(p0, p1, p2, n=9):
    """A quadratic Bezier -- a spring-steel leg is a curve, not a post."""
    out = []
    for i in range(n):
        t = i / (n - 1.0)
        out.append(tuple((1 - t) ** 2 * Vector(p0) + 2 * (1 - t) * t * Vector(p1) + t * t * Vector(p2)))
    return out


# A 172's undercarriage is not a ball on a stick, which is what the first pass drew and what it looked like.
# The mains are TUBULAR SPRING-STEEL LEGS that leave the fuselage sides just below the floor and bow outward
# and down; the nose leg is an oleo raked forward from the firewall. Both are drawn from where the config
# puts the contact patch, so the aeroplane stands on the wheels the physics stands on.
WHEEL_R, WHEEL_W = 0.205, 0.075        # 6.00-6 mains, 0.41 m across
NOSE_R, NOSE_W = 0.178, 0.065          # 5.00-5 nose


def wheel(name, cx, cy, cz, r, w):
    loops = [[(cx + r * math.cos(2 * math.pi * i / 18), cy + side * w,
               cz + r * math.sin(2 * math.pi * i / 18)) for i in range(18)] for side in (-1, 1)]
    return mesh_from_loops(name, loops)


for g in CFG.get("gear", []):
    gx, gy, gz = g["pos"][0], g["pos"][1], bz(g["pos"][2])
    nose = g.get("isSteerable", False) and not g.get("isTailwheel", False)
    tag = "Nose" if nose else ("Main.L" if gy < 0 else "Main.R")
    if nose:
        axle = gz + NOSE_R
        # the strut leaves the firewall and rakes forward to the axle, with a fork round the wheel
        top = (gx - 0.10, 0.0, axle + 0.70)
        leg = sweep("Gear.Nose.Leg", bend(top, (gx - 0.03, 0.0, axle + 0.30), (gx, 0.0, axle + 0.07)),
                    [0.055] * 9)
        whl = wheel("Gear.Nose.Wheel", gx, gy, axle, NOSE_R, NOSE_W)
        fork = [cyl("Gear.Nose.Fork.%d" % k, (gx, k * 0.085, axle + 0.12), (gx, k * 0.085, axle), 0.022)
                for k in (-1, 1)]
        grp = [o for o in ([leg, whl] + fork) if o]
        for o in grp:
            o.select_set(True)
        bpy.context.view_layer.objects.active = grp[0]
        bpy.ops.object.join()
        joined = bpy.context.view_layer.objects.active
        joined.name = "Gear.Nose.Steer"
        # the origin IS the steering axis, so Unity steers it with a local rotation about z and nothing has
        # to be solved at runtime.
        set_origin(joined, top)
        parts.append(joined)
        bpy.ops.object.select_all(action='DESELECT')
    else:
        axle = gz + WHEEL_R
        side = -1 if gy < 0 else 1
        # the leg's root sits ON the fuselage skin at this station, found from the measured section
        s_root = NOSE - gx
        y_root, z_root, _ = skin_point(s_root, math.radians(-38 if side > 0 else 180 + 38))
        root = (gx, y_root, z_root)
        mid = (gx, (y_root + gy) * 0.55, z_root - 0.30)
        tip = (gx, gy - side * (WHEEL_W + 0.03), axle)
        path = bend(root, mid, tip)
        # the strip tapers outboard, the way a spring leg does
        radii = [0.055 - 0.020 * (i / 8.0) for i in range(9)]
        parts.append(sweep("Gear.%s.Leg" % tag, path, radii, flat=0.55))
        parts.append(wheel("Gear.%s.Wheel" % tag, gx, gy, axle, WHEEL_R, WHEEL_W))

# ---- PROP, SPINNER, STRUTS -----------------------------------------------------------------------------------
px = NOSE + 0.10
spin = mesh_from_loops("Spinner", [[(px - 0.26, 0.11 * math.cos(2 * math.pi * i / 12), 0.11 * math.sin(2 * math.pi * i / 12)) for i in range(12)],
                                   [(px, 0.02 * math.cos(2 * math.pi * i / 12), 0.02 * math.sin(2 * math.pi * i / 12)) for i in range(12)]])
parts.append(spin)
# The blades had a 40 mm chord and read as two wires. A 172's prop is a 160 mm aluminium blade, twisted from
# a broad flat root to a thin tip, and the twist is what catches the light as it turns.
BLADE_C, BLADE_T = 0.165, 0.022
blades = []
for kk in (0, 1):
    base = math.pi * kk
    rings = []
    for r_frac in (0.10, 0.35, 0.65, 0.88, 1.0):
        r = PROP_D * 0.5 * r_frac
        # chord is widest at about 60 % radius; pitch washes out from 28 deg at the root to 10 at the tip
        c = BLADE_C * (0.55 + 0.75 * math.sin(math.pi * min(r_frac * 1.15, 1.0)))
        th = BLADE_T * (1.0 - 0.75 * r_frac) + 0.004
        pitch = math.radians(28.0 - 18.0 * r_frac)
        ring = []
        for i in range(8):
            a = 2 * math.pi * i / 8
            u, v = 0.5 * c * math.cos(a), 0.5 * th * math.sin(a)      # along the chord, across the thickness
            dx = u * math.sin(pitch) + v * math.cos(pitch)            # chordwise tilt becomes fore-and-aft
            dr = u * math.cos(pitch) - v * math.sin(pitch)
            ring.append((px - 0.03 + dx,
                         math.sin(base) * r + math.cos(base) * dr,
                         math.cos(base) * r - math.sin(base) * dr))
        rings.append(ring)
    blades.append(mesh_from_loops("Blade%d" % kk, rings))
for b in blades:
    b.select_set(True)
bpy.context.view_layer.objects.active = blades[0]
bpy.ops.object.join()
prop = bpy.context.view_layer.objects.active
prop.name = "Prop"
set_origin(prop, (px, 0, 0))
parts.append(prop)
bpy.ops.object.select_all(action='DESELECT')

# THE LIFT STRUTS. These ran to y = +-0.50 before, which is beside the fuselage and not on it, so each strut
# stopped in mid-air a hand's breadth from the skin. The lower end is now found the same way the gear leg's
# root is -- by asking the measured section where its own surface is at that station and that angle -- so it
# meets the body wherever the body happens to be. The section is a flattened aerofoil, because a strut is.
STRUT_FRAC = 0.46                      # how far out along the half-span the strut meets the wing
for side in (-1, 1):
    s_root = NOSE - (WING_X - 0.26)
    y_root, z_root, _ = skin_point(s_root, math.radians(-31 if side > 0 else 180 + 31))
    upper = (WING_X - 0.06, side * HALF_SPAN * STRUT_FRAC, WING_Z - 0.06)
    lower = (WING_X - 0.26, side * abs(y_root), z_root)
    st = sweep("Strut.%s" % ("L" if side < 0 else "R"),
               [tuple(Vector(lower) + (Vector(upper) - Vector(lower)) * (i / 8.0)) for i in range(9)],
               [0.048] * 9, flat=0.35)
    if st:
        parts.append(st)

# ---- smooth, thicken the single-sided surfaces, and export ---------------------------------------------------
# Shade smooth. Mesh.use_auto_smooth was removed in Blender 4.1 — the angle-limited version is now a
# "Smooth by Angle" modifier — so mark the polygons smooth and let the exporter write split normals.
# SMOOTH BY ANGLE, not everywhere. Marking every polygon smooth rounds off the windscreen's corners -- the
# one break in the top line that the measurement went to trouble to preserve -- and softens the wing's
# trailing edge into a smear. Blender 4.1 replaced Mesh.use_auto_smooth with a "Smooth by Angle" modifier,
# which bpy.ops.object.shade_auto_smooth applies; fall back to flat-out smooth on an older build.
bpy.ops.object.select_all(action='DESELECT')
for ob in parts:
    ob.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
try:
    bpy.ops.object.shade_auto_smooth(angle=math.radians(38))
except Exception as exc:
    print("C172 shade_auto_smooth unavailable (%s); marking polygons smooth" % exc)
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
