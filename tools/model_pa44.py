"""Model a Piper PA-44-180 Seminole in Blender.

    blender --background --python tools/model_pa44.py        -> build/models/seminole-like.glb

WHY THIS EXISTS. `seminole-like` has been wearing a **Piper PA-23 Apache/Aztec** -- a different aeroplane
with a conventional tail, found by `tools/fleet_accuracy.py` on 2026-10-25 at +18% length/span and +28%
height/span. No scale fixes that: a uniform scale cannot change a ratio, and the Apache's low tailplane is
the first thing anyone sees. The Seminole is a T-tail, and that is the shape this draws.

WHERE THE NUMBERS COME FROM. `assets-incoming/ref-pa44/MEASUREMENTS.md` has the working. In short:

  - the **plan view** of the free factory three-view on Commons, which checks out three ways -- its span
    dimension, its stabilator span (0.5%) and the wing area its planform integrates to (17.03 m2 against a
    published 17.08). Everything lateral and longitudinal is measured off it.
  - the sheet's **printed dimensions**, which agree with the published figures to better than 0.2%:
    span 38' 6.6" = 11.745, length 27' 7.20" = 8.4125, height 8' 6.00" = 2.5908, stabilator 10' = 3.048,
    gear track 10' 6.5" = 3.213, prop centres 12' 7.14" = 3.839, nose to main wheel 8' 4.80" = 2.5603.
  - the **photographs** for the side-view proportions. The sheet's own side view is drawn to a different
    scale from its plan and front views (about 99 px/m across against 112 px/m up), its ground line slopes,
    and dimension leaders cross the outline. Nothing here comes from it.

WHERE IT DEPARTS FROM THE CONFIG, and why. `configs/aircraft/seminole-like.json` is the physics and is
normally the authority for a model's dimensions -- but two of its numbers disagree with the real aeroplane
and the drawing is the one that checks out:

  - **the config's tail is 0.27 m too tall.** `vStab` tops at z -1.862 and the gear contact is z +1.0,
    which makes the aeroplane 2.862 m tall against a published 2.5908 -- +10.5%. Track and wheelbase both
    check out, so the fin is the likelier of the two. Drawn here at 1.5908 above the body axis, with the
    wheels left at the config's z +1.0 so the aeroplane still sits on its own gear.
  - **the config's wing area is 10% low** (15.43 m2 of strips against 17.08 real). Drawn to the measured
    planform, which reproduces the published area.

Both are recorded in MEASUREMENTS.md for the physics side to settle. A third, smaller one is left alone:
the config puts the engines at y +-1.75 where the drawing says +-1.92. The visual is drawn at +-1.92 and
`AirframeVisual` re-centres each prop hub on the model's own propeller when it is within half a radius,
which 0.17 m is -- so the blades bind and spin. The 9% short moment arm is a physics question, not this
file's.

AXES: Blender x forward (nose +x), y right, z up, origin on the body axis at the config's datum. The glTF
export is Y-up, so the file reads length x, height y, span z -- the convention `AirframeModels.OwnSpecs`
expects, the same as the 172 and the 2-33.
"""
import bpy, bmesh, json, math, os
from mathutils import Vector

REPO = os.path.dirname(os.path.dirname(os.path.realpath(__file__)))
OUT = os.path.join(REPO, "build/models")
CFG = json.load(open(os.path.join(REPO, "configs/aircraft/seminole-like.json")))

# A trainer twin's paint: white over a blue belly, which is what the Commons photographs show on
# G-BGCO and on the Waterloo-Wellington ramp. Linear values, because that is what Base Color takes.
WHITE = (0.885, 0.885, 0.890, 1.0)
BLUE = (0.020, 0.075, 0.310, 1.0)
TRIM = (0.247, 0.263, 0.298, 1.0)
GLASS = (0.055, 0.085, 0.115, 1.0)
RUBBER = (0.055, 0.058, 0.062, 1.0)
METAL = (0.520, 0.530, 0.545, 1.0)

# ------------------------------------------------------------------------------------------------------
# the aeroplane, in metres on the body axis
# ------------------------------------------------------------------------------------------------------
NOSE = 2.30                 # length 8.4125 puts the rudder trailing edge at -6.1125
TAIL = NOSE - 8.4125
HALF = 11.745 / 2.0         # 5.8725
GROUND = -1.00              # the config's gear contact; the model sits on it
FIN_TOP = 1.5908            # 2.5908 above the wheels, which is the published height

# Wing chord against distance from the centreline, straight off the plan view (MEASUREMENTS.md).
CHORD = [(0.00, 1.840), (0.98, 1.784), (1.47, 1.647), (2.44, 1.647), (2.93, 1.589), (3.42, 1.500),
         (3.91, 1.412), (4.40, 1.313), (4.89, 1.225), (5.38, 1.137), (5.60, 1.100), (HALF, 1.050)]
LE_ROOT = 0.70              # leading edge at the centreline
LE_SWEEP = math.radians(4.83)
DIHEDRAL = math.radians(7.0)
WING_Z = -0.20              # the config's wing strips, z positive down there and up here
TIP_ROUND = 0.30            # the last of the half-span, rolled into a rounded tip

NAC_Y = 3.8392 / 2.0        # prop centres 12' 7.14"
PROP_R = 0.94
TRACK = 3.2131 / 2.0        # main wheels, 10' 6.5"
MAIN_X = -0.30              # the config's mains; nose to main wheel is then 2.60 against a printed 2.5603
NOSE_X = 1.50               # the config's nose gear


def lerp_chord(y):
    y = abs(y)
    for (a, ca), (b, cb) in zip(CHORD, CHORD[1:]):
        if y <= b:
            t = 0.0 if b == a else (y - a) / (b - a)
            return ca + (cb - ca) * t
    return CHORD[-1][1]


def wing_le(y):
    return LE_ROOT - abs(y) * math.tan(LE_SWEEP)


def wing_z(y):
    return WING_Z + abs(y) * math.tan(DIHEDRAL)


def wing_area():
    """Trapezoidal over the measured table, both wings -- the check that the planform is the real one."""
    a = 0.0
    for (y0, c0), (y1, c1) in zip(CHORD, CHORD[1:]):
        a += 0.5 * (c0 + c1) * (y1 - y0)
    return 2.0 * a


# ------------------------------------------------------------------------------------------------------
# sections
# ------------------------------------------------------------------------------------------------------
def naca(t, u, upper):
    y = 5 * t * (0.2969 * math.sqrt(u) - 0.1260 * u - 0.3516 * u ** 2 + 0.2843 * u ** 3
                 - 0.1036 * u ** 4) + 0.0021 * u
    return y if upper else -y


def clark(u, upper):
    """Clark-Y-like, 12% thick: what a Piper wing reads as end-on, flat underneath aft of a third."""
    m, p, t = 0.032, 0.40, 0.120
    u = min(max(u, 0.0), 1.0)
    yc = (m / p ** 2 * (2 * p * u - u * u) if u < p else
          m / (1 - p) ** 2 * ((1 - 2 * p) + 2 * p * u - u * u))
    yt = naca(t, u, True)
    if upper:
        return yc + yt
    return max(yc - yt, 0.0) if u > 0.32 else yc - yt


def cos_u(n):
    return [0.5 * (1 - math.cos(math.pi * i / n)) for i in range(n + 1)]


# ------------------------------------------------------------------------------------------------------
# mesh plumbing
# ------------------------------------------------------------------------------------------------------
def mesh_from_loops(name, loops, close_ends=True):
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


def paint(ob, rgba, rough=0.34):          # a painted metal trainer is glossier than doped fabric
    mat = bpy.data.materials.new(ob.name + "Mat")
    mat.use_nodes = True
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = rgba
    if "Roughness" in b.inputs:
        b.inputs["Roughness"].default_value = rough
    if rgba is GLASS and "Roughness" in b.inputs:
        b.inputs["Roughness"].default_value = 0.08      # glazing is a mirror, not a matt panel
    ob.data.materials.append(mat)
    return ob


def smooth(ob):
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


def cyl(name, p0, p1, r, seg=14):
    p0, p1 = Vector(p0), Vector(p1)
    axis = p1 - p0
    up = Vector((0, 0, 1)) if abs(axis.normalized().z) < 0.95 else Vector((1, 0, 0))
    u = axis.cross(up).normalized() * r
    v = axis.cross(u).normalized() * r
    loops = [[(p + u * math.cos(2 * math.pi * i / seg) + v * math.sin(2 * math.pi * i / seg))[:]
              for i in range(seg)] for p in (p0, p1)]
    return mesh_from_loops(name, loops)


def wheel(name, cx, cy, cz, r, width, seg=20):
    loops = []
    for s in (-1, 1):
        loops.append([(cx + r * math.cos(2 * math.pi * i / seg), cy + s * width * 0.5,
                       cz + r * math.sin(2 * math.pi * i / seg)) for i in range(seg)])
    return mesh_from_loops(name, loops)


# ------------------------------------------------------------------------------------------------------
# fuselage: the PA-28 cabin shape, a stepped windscreen and a tailcone that tapers to the fin
# ------------------------------------------------------------------------------------------------------
# (x, half-width, bottom z, top z). Cabin 1.24 m across and 1.34 m deep, off the photographs; the tailcone
# comes down to the 0.26 m the fin and its fairing stand on.
STATIONS = [
    (NOSE, 0.055, -0.09, 0.09),
    (2.14, 0.230, -0.30, 0.28),
    (1.90, 0.360, -0.42, 0.40),
    (1.58, 0.490, -0.52, 0.50),
    (1.25, 0.565, -0.57, 0.56),
    (0.90, 0.610, -0.60, 0.70),
    (0.35, 0.620, -0.62, 0.74),
    (-0.30, 0.618, -0.62, 0.74),
    (-0.95, 0.580, -0.60, 0.70),
    (-1.60, 0.500, -0.56, 0.62),
    (-2.35, 0.395, -0.48, 0.52),
    (-3.15, 0.290, -0.38, 0.41),
    (-3.95, 0.200, -0.28, 0.30),
    (-4.70, 0.140, -0.20, 0.22),
    (-5.15, 0.105, -0.15, 0.17),
]
NA = 16


def body_loop(hw, zb, zt, power=2.8):
    """A superellipse |y|^n + |z|^n = 1 between the belly and the roof. n = 2 is an ellipse; a Cherokee
    cabin is slab-sided with rounded corners, which is n near 3. A nacelle is nearly round, so it asks
    for a lower n."""
    zc, zr = 0.5 * (zb + zt), 0.5 * (zt - zb)
    e = 2.0 / power
    pts = []
    for i in range(2 * NA):
        th = 2 * math.pi * i / (2 * NA)
        c, s = math.cos(th), math.sin(th)
        pts.append((0.0,
                    hw * math.copysign(abs(c) ** e, c),
                    zc + zr * math.copysign(abs(s) ** e, s)))
    return pts


def at_x(x):
    """(half-width, bottom z, top z) of the fuselage at any station, interpolated between STATIONS."""
    if x >= STATIONS[0][0]:
        return STATIONS[0][1:]
    for (xa, ha, ba, ta), (xb, hb, bb, tb) in zip(STATIONS, STATIONS[1:]):
        if x >= xb:
            f = (xa - x) / (xa - xb)
            return (ha + (hb - ha) * f, ba + (bb - ba) * f, ta + (tb - ta) * f)
    return STATIONS[-1][1:]


def build_body():
    loops = []
    for x, hw, zb, zt in STATIONS:
        loops.append([(x, p[1], p[2]) for p in body_loop(hw, zb, zt)])
    return smooth(paint(mesh_from_loops("Fuselage", loops), WHITE))


def build_belly():
    """The blue below the cheatline. A separate skin a few millimetres proud of the body, which is how the
    liveries paint a waterline without needing UVs on a generated hull."""
    loops = []
    for x, hw, zb, zt in STATIONS:
        if x > 2.0 or x < -4.2:
            continue
        zc, zr = 0.5 * (zb + zt), 0.5 * (zt - zb)
        line = zc - 0.18 * zr                      # the cheatline, a little below mid-height
        ring = []
        for p in body_loop(hw * 1.006, zb - 0.004, zt):
            ring.append((x, p[1], min(p[2], line)))
        loops.append(ring)
    return smooth(paint(mesh_from_loops("Belly", loops, close_ends=False), BLUE))


def build_glass():
    """The stepped windscreen and four side windows a side, laid ON the fuselage surface.

    Each point is taken from the body's own section at that station and pushed a few millimetres out along
    its normal, so the glass follows the hull instead of floating over it."""
    obs = []
    screen = []
    for x in (1.62, 1.44, 1.24, 1.05, 0.88):
        hw, zb, zt = at_x(x)
        zc, zr = 0.5 * (zb + zt), 0.5 * (zt - zb)
        e = 2.0 / 2.8
        row = []
        for i in range(-6, 7):
            th = math.pi * 0.5 * (i / 6.0) * 0.86              # over the top, stopping short of the sides
            c, sn = math.sin(th), math.cos(th)
            row.append((x, (hw + 0.006) * math.copysign(abs(c) ** e, c),
                        zc + (zr + 0.006) * abs(sn) ** e))
        screen.append(row)
    obs.append(smooth(paint(grid_mesh("Windscreen", screen), GLASS)))
    for side in (-1, 1):
        for i, (x0, x1) in enumerate(((0.78, 0.30), (0.22, -0.30), (-0.38, -0.86))):
            rows = []
            for x in (x0, 0.5 * (x0 + x1), x1):
                hw, zb, zt = at_x(x)
                top = zt - 0.085
                rows.append([(x, side * (hw + 0.008), top),
                             (x, side * (hw + 0.008), top - 0.42)])
            obs.append(paint(grid_mesh("Window%d_%d" % (side, i), rows), GLASS))
    return obs


# ------------------------------------------------------------------------------------------------------
# wing
# ------------------------------------------------------------------------------------------------------
def wing_section(y, n=20, shrink=1.0):
    c = lerp_chord(y) * shrink
    xle, z0 = wing_le(y), wing_z(y)
    pts = []
    us = cos_u(n)
    for u in us:
        pts.append((xle - u * c, y, z0 + clark(u, True) * c))
    for u in reversed(us[1:-1]):
        pts.append((xle - u * c, y, z0 + clark(u, False) * c))
    return pts


def build_wing(side):
    ys = [0.0, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0, 5.35, 5.60]
    loops = [wing_section(side * y) for y in ys]
    # the tip, rolled shut rather than left as a flat plate
    for f in (0.45, 0.78, 0.94, 1.0):
        y = 5.60 + (HALF - 5.60) * f
        loops.append(wing_section(side * y, shrink=math.sqrt(max(1e-3, 1.0 - f * f)) * 0.30 + 0.70 * (1 - f * f)))
    return smooth(paint(mesh_from_loops("Wing%d" % side, loops), WHITE))


# ------------------------------------------------------------------------------------------------------
# nacelles: engine, cowl and the fairing that runs aft past the wing trailing edge
# ------------------------------------------------------------------------------------------------------
NAC = [                 # (x, half-width, bottom z, top z) about the nacelle centreline
    (1.78, 0.085, -0.085, 0.085),
    (1.62, 0.215, -0.225, 0.215),
    (1.35, 0.300, -0.315, 0.285),
    (0.95, 0.330, -0.360, 0.300),
    (0.40, 0.335, -0.380, 0.300),
    (-0.25, 0.320, -0.370, 0.280),
    (-0.85, 0.265, -0.310, 0.235),
    (-1.25, 0.185, -0.215, 0.170),
    (-1.55, 0.095, -0.110, 0.090),
]


def build_nacelle(side):
    y0 = side * NAC_Y
    z0 = wing_z(y0) - 0.02
    loops = []
    for x, hw, zb, zt in NAC:
        loops.append([(x, y0 + p[1], z0 + p[2]) for p in body_loop(hw, zb, zt, power=2.1)])
    return smooth(paint(mesh_from_loops("Nacelle%d" % side, loops), WHITE))


def build_prop(side):
    """Spinner and two blades. AirframeVisual finds them by radius about the config's hub and spins them,
    so they have to be modelled here -- a model with no propeller gets none."""
    y0 = side * NAC_Y
    z0 = wing_z(y0) - 0.02
    obs = []
    spin = []
    for x, r in ((1.96, 0.02), (1.90, 0.090), (1.80, 0.135), (1.68, 0.150), (1.60, 0.145)):
        spin.append([(x, y0 + r * math.cos(2 * math.pi * i / 14), z0 + r * math.sin(2 * math.pi * i / 14))
                     for i in range(14)])
    obs.append(smooth(paint(mesh_from_loops("Spinner%d" % side, spin), TRIM)))
    # Two blades, each a solid section stack along its own radius: twisted flat near the tip and coarse at
    # the root, which is what reads as a propeller rather than a pair of paddles.
    for b in (1, -1):
        loops = []
        for f in (0.12, 0.35, 0.60, 0.85, 0.97, 1.0):
            r = PROP_R * f
            ch = 0.235 * (1.0 - 0.30 * f * f) * (1.0 if f < 0.97 else 0.30)
            th = 0.13 * (1.0 - 0.6 * f)
            tw = math.radians(32.0 - 24.0 * f)
            us = cos_u(8)
            ring = []
            for u in us:
                ring.append(blade_pt(u, True, ch, th, tw, y0 + b * r, z0))
            for u in reversed(us[1:-1]):
                ring.append(blade_pt(u, False, ch, th, tw, y0 + b * r, z0))
            loops.append(ring)
        obs.append(smooth(paint(mesh_from_loops("Blade%d_%d" % (side, b), loops), TRIM)))
    return obs


def blade_pt(u, upper, chord, thick, twist, y, z0):
    """One point on a blade section: the chord laid in the x-z plane, rotated by the blade's twist."""
    a = (u - 0.5) * chord
    n = naca(thick, u, upper) * chord
    c, s = math.cos(twist), math.sin(twist)
    return (1.83 + a * s + n * c, y, z0 + a * c - n * s)


# ------------------------------------------------------------------------------------------------------
# T-tail
# ------------------------------------------------------------------------------------------------------
FIN_ROOT_Z = 0.18
FIN_ROOT_LE = -3.95
FIN_TIP_LE = -5.03
FIN_TE = TAIL                 # the rudder's trailing edge, which is the back of the aeroplane


def build_fin():
    loops = []
    zs = [FIN_ROOT_Z, 0.45, 0.80, 1.15, 1.45, FIN_TOP]
    for z in zs:
        f = (z - FIN_ROOT_Z) / (FIN_TOP - FIN_ROOT_Z)
        xle = FIN_ROOT_LE + (FIN_TIP_LE - FIN_ROOT_LE) * f
        c = FIN_TE - xle
        t = 0.115 - 0.02 * f
        ring = []
        us = cos_u(12)
        for u in us:
            ring.append((xle - u * abs(c), naca(t, u, True) * abs(c), z))
        for u in reversed(us[1:-1]):
            ring.append((xle - u * abs(c), naca(t, u, False) * abs(c), z))
        loops.append(ring)
    return smooth(paint(mesh_from_loops("Fin", loops), WHITE))


def build_stab():
    """The all-moving stabilator, sitting on the fin tip -- the shape that says Seminole and not Apache."""
    half = 3.0480 / 2.0
    z = FIN_TOP - 0.075
    loops = []
    for y in (-half, -half * 0.86, -half * 0.5, 0.0, half * 0.5, half * 0.86, half):
        f = abs(y) / half
        c = 0.962 - (0.962 - 0.737) * f
        xle = FIN_TIP_LE + 0.02 * f
        t = 0.105
        ring = []
        us = cos_u(12)
        for u in us:
            ring.append((xle - u * c, y, z + naca(t, u, True) * c))
        for u in reversed(us[1:-1]):
            ring.append((xle - u * c, y, z + naca(t, u, False) * c))
        loops.append(ring)
    return smooth(paint(mesh_from_loops("Stabilator", loops), WHITE))


# ------------------------------------------------------------------------------------------------------
# gear
# ------------------------------------------------------------------------------------------------------
def build_gear():
    obs = []
    rn, rm = 0.185, 0.225
    # nose: a leg down from the keel, wheel on the config's contact point
    obs.append(smooth(paint(cyl("NoseLeg", (NOSE_X, 0.0, -0.52), (NOSE_X, 0.0, GROUND + rn), 0.045), METAL)))
    obs.append(smooth(paint(wheel("NoseWheel", NOSE_X, 0.0, GROUND + rn, rn, 0.115), RUBBER)))
    for side in (-1, 1):
        y_top = side * (NAC_Y - 0.05)
        y_w = side * TRACK
        ztop = wing_z(y_top) - 0.33
        obs.append(smooth(paint(cyl("MainLeg%d" % side, (MAIN_X, y_top, ztop),
                                    (MAIN_X, y_w, GROUND + rm), 0.055), METAL)))
        obs.append(smooth(paint(wheel("MainWheel%d" % side, MAIN_X, y_w, GROUND + rm, rm, 0.135), RUBBER)))
    return obs


# ------------------------------------------------------------------------------------------------------
def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts = [build_body(), build_belly(), build_fin(), build_stab()]
    parts += build_glass()
    for side in (-1, 1):
        parts.append(build_wing(side))
        parts.append(build_nacelle(side))
        parts += build_prop(side)
    parts += build_gear()

    for ob in parts:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    os.makedirs(OUT, exist_ok=True)
    glb = os.path.join(OUT, "seminole-like.glb")
    bpy.ops.export_scene.gltf(filepath=glb, export_format='GLB', use_selection=True, export_apply=True)

    pts = [o.matrix_world @ v.co for o in parts for v in o.data.vertices]
    span = max(p.y for p in pts) - min(p.y for p in pts)
    length = max(p.x for p in pts) - min(p.x for p in pts)
    height = max(p.z for p in pts) - min(p.z for p in pts)
    print("PA44 parts %d  faces %d" % (len(parts), sum(len(o.data.polygons) for o in parts)))
    print("PA44 span %.3f (real 11.745)  length %.3f (8.4125)  height %.3f (2.5908)" % (span, length, height))
    print("PA44 L/S %.4f (real %.4f)   H/S %.4f (real %.4f)"
          % (length / span, 8.4125 / 11.745, height / span, 2.5908 / 11.745))
    print("PA44 wing area %.2f m2 drawn, 17.08 published, %.2f in the config"
          % (wing_area(), 2 * sum(s["area"] for s in CFG["surfaces"][0]["strips"]) / 2))
    print("PA44 wrote %s" % glb)


main()
