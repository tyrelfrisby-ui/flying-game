"""Model a Marganski Swift S-1 in Blender.

    blender --background --python tools/model_swift_s1.py        -> build/models/glider-swift-s1-like.glb

WHY THIS EXISTS. `glider-swift-s1-like` has no model at all -- it is drawn procedurally from its config, and
the config gets the one thing about this aeroplane that a photograph had to settle **wrong**.

**THE TAIL IS CRUCIFORM.** Three freely-licensed photographs of SP-P600 agree: the tailplane attaches at
roughly half the fin's height, clearly above the boom and clearly below the fin tip. The config puts
`hStab` and `elevator` at z 0.000, the boom centreline, which is below even the fin root at z -0.319 (the
config's z is positive down). No published source settles it -- the EASA certificate gives span, length,
height, wing area, MAC and CG and never mentions the empennage. The photograph was the only reference that
was ever going to say so, which is the whole argument for gathering them.

WHERE THE NUMBERS COME FROM -- `assets-incoming/ref-swift-s1/MEASUREMENTS.md` has the working:

  - **EASA TCDS No. EASA.A.038** to the millimetre: span 12,68 m, length 6,91 m, wing area 11,73 m2,
    height 1,74 m *(flight attitude)*. There is no three-view of this aeroplane anywhere free, and the
    certificate is better than one would have been: a three-view gives span, length, height and areas, and
    the TCDS gives all four exactly.
  - `configs/aircraft/glider-swift-s1-like.json` for the shape inside that size -- taper, the kink at about
    3 m, dihedral 0.02 rad, fin and tailplane chords. Its chords are scaled so the drawn planform
    integrates to the certificated 11,73 m2; the config's own strips sum to 10.76, about 8% low, and its
    span is 11.956 against 12.68.
  - the photographs for the canopy, the boom and the tail's height on the fin.

ABOUT THAT HEIGHT, because it is not the same story as the PA-44 and the EB29. Their configs put the fin
too high. **This one does not.** 1,74 m is flight attitude -- gear up -- and the config's belly sits 0.36
below the body axis with the fin tip 1.331 above it, which is 1.69 m, within 3%. So the fin is drawn where
the config puts it, and because the config carries no `retractableGear` the gear is drawn DOWN, which makes
the mesh 2.03 m tall. `tools/fleet_accuracy.py` suppresses the height comparison for this type rather than
report a number that measures two different things.

AXES: Blender x forward (nose +x), y right, z up, origin on the body axis. The glTF export is Y-up, so the
file reads length x, height y, span z -- what `AirframeModels.OwnSpecs` expects.
"""
import bpy, bmesh, json, math, os
from mathutils import Vector

REPO = os.path.dirname(os.path.dirname(os.path.realpath(__file__)))
OUT = os.path.join(REPO, "build/models")
CFG = json.load(open(os.path.join(REPO, "configs/aircraft/glider-swift-s1-like.json")))

WHITE = (0.905, 0.905, 0.900, 1.0)
RED = (0.430, 0.028, 0.030, 1.0)
GLASS = (0.075, 0.105, 0.130, 1.0)
RUBBER = (0.055, 0.058, 0.062, 1.0)
METAL = (0.520, 0.530, 0.545, 1.0)

SPAN, LENGTH, AREA = 12.68, 6.91, 11.73       # EASA TCDS A.038
HALF = SPAN / 2.0
TAIL = -4.67                                   # rudder trailing edge, from the config's rudder strips
NOSE = TAIL + LENGTH                           # 2.24
WING_Z = -0.05                                 # the config's wing strips, z positive down there
DIHEDRAL = 0.02                                # rad, from the config
MAIN_X, MAIN_Z, MAIN_R = 0.20, -0.70, 0.160    # the config's gear, contact at z -0.70
TAILW_X, TAILW_Z, TAILW_R = -4.00, -0.40, 0.050

FIN_ROOT_Z, FIN_TOP_Z = 0.319, 1.331           # the config's vStab, z flipped to up-positive
FIN_ROOT_LE, FIN_TIP_LE = -3.73, -4.20
# Half way up the fin, which is where three photographs of SP-P600 put it.
STAB_Z = FIN_ROOT_Z + 0.5 * (FIN_TOP_Z - FIN_ROOT_Z)
STAB_HALF, STAB_LE = 1.269, -3.60
STAB_ROOT_C, STAB_TIP_C = 0.713, 0.487


def cfg_wing():
    """Wing chord PLUS aileron chord, which is the planform.

    The two are separate surfaces in this config and the `wing` strips stop at the hinge line: their chord
    drops from 0.917 to 0.612 at y 3.165, exactly where `wing-aileron` starts, and on their own they
    integrate to 9.60 m2 against a certificated 11.73. Added together the step disappears and the taper is
    straight, which is the check that this is the right reading. (The EB29's config is NOT built this way
    -- there the `wing` strips are the full chord -- so this cannot be done blind.)"""
    w = [s for s in CFG["surfaces"] if s["id"] == "wing"][0]
    asurf = [s for s in CFG["surfaces"] if s["id"] == "wing-aileron"][0]
    ail = {round(st["pos"][1], 3): st["chord"] for st in asurf["strips"]}
    pts = []
    for s in sorted(w["strips"], key=lambda s: s["pos"][1]):
        y = s["pos"][1]
        if y <= 0:
            continue
        pts.append((y, s["chord"] + ail.get(round(y, 3), 0.0)))
    k = HALF / pts[-1][0]
    return [(y * k, c) for y, c in pts]


def area_of(table):
    a, prev = 0.0, (0.0, table[0][1])
    for y, c in table:
        a += 0.5 * (prev[1] + c) * (y - prev[0])
        prev = (y, c)
    return 2.0 * a


TABLE = cfg_wing()
KC = AREA / area_of(TABLE)
CHORD = [(0.0, TABLE[0][1] * KC)] + [(y, c * KC) for y, c in TABLE]


def lerp_chord(y):
    y = min(abs(y), HALF)
    for (a, ca), (b, cb) in zip(CHORD, CHORD[1:]):
        if y <= b:
            t = 0.0 if b == a else (y - a) / (b - a)
            return ca + (cb - ca) * t
    return CHORD[-1][1]


def wing_le(y):
    return 0.40 - 0.012 * abs(y)


def wing_z(y):
    return WING_Z + abs(y) * math.tan(DIHEDRAL)


# ------------------------------------------------------------------------------------------------------
def naca(t, u, upper):
    y = 5 * t * (0.2969 * math.sqrt(u) - 0.1260 * u - 0.3516 * u ** 2 + 0.2843 * u ** 3
                 - 0.1036 * u ** 4) + 0.0018 * u
    return y if upper else -y


def cos_u(n):
    return [0.5 * (1 - math.cos(math.pi * i / n)) for i in range(n + 1)]


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


def paint(ob, rgba, rough=0.18):
    mat = bpy.data.materials.new(ob.name + "Mat")
    mat.use_nodes = True
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = rgba
    if "Roughness" in b.inputs:
        b.inputs["Roughness"].default_value = rough
    ob.data.materials.append(mat)
    return ob


def smooth(ob):
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


def cyl(name, p0, p1, r, seg=12):
    p0, p1 = Vector(p0), Vector(p1)
    axis = p1 - p0
    up = Vector((0, 0, 1)) if abs(axis.normalized().z) < 0.95 else Vector((1, 0, 0))
    u = axis.cross(up).normalized() * r
    v = axis.cross(u).normalized() * r
    return mesh_from_loops(name, [[(p + u * math.cos(2 * math.pi * i / seg)
                                    + v * math.sin(2 * math.pi * i / seg))[:] for i in range(seg)]
                                  for p in (p0, p1)])


def wheel(name, cx, cy, cz, r, width, seg=18):
    return mesh_from_loops(name, [[(cx + r * math.cos(2 * math.pi * i / seg), cy + s * width * 0.5,
                                    cz + r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]
                                  for s in (-1, 1)])


# ------------------------------------------------------------------------------------------------------
STATIONS = [
    (NOSE, 0.030, -0.050, 0.050),
    (2.08, 0.125, -0.175, 0.145),
    (1.88, 0.215, -0.270, 0.225),
    (1.62, 0.288, -0.330, 0.282),
    (1.32, 0.325, -0.355, 0.312),
    (0.98, 0.340, -0.360, 0.330),
    (0.58, 0.336, -0.355, 0.330),
    (0.14, 0.310, -0.330, 0.310),
    (-0.36, 0.264, -0.295, 0.275),
    (-0.96, 0.204, -0.235, 0.215),
    (-1.66, 0.150, -0.175, 0.160),
    (-2.46, 0.110, -0.130, 0.120),
    (-3.26, 0.085, -0.100, 0.096),
    (-3.96, 0.068, -0.080, 0.078),
    (-4.42, 0.056, -0.066, 0.064),
]
NA = 14


def body_loop(hw, zb, zt, power=2.1):
    zc, zr = 0.5 * (zb + zt), 0.5 * (zt - zb)
    e = 2.0 / power
    out = []
    for i in range(2 * NA):
        th = 2 * math.pi * i / (2 * NA)
        c, s = math.cos(th), math.sin(th)
        out.append((0.0, hw * math.copysign(abs(c) ** e, c), zc + zr * math.copysign(abs(s) ** e, s)))
    return out


def at_x(x):
    if x >= STATIONS[0][0]:
        return STATIONS[0][1:]
    for (xa, ha, ba, ta), (xb, hb, bb, tb) in zip(STATIONS, STATIONS[1:]):
        if x >= xb:
            f = (xa - x) / (xa - xb)
            return (ha + (hb - ha) * f, ba + (bb - ba) * f, ta + (tb - ta) * f)
    return STATIONS[-1][1:]


def build_body():
    return smooth(paint(mesh_from_loops(
        "Pod", [[(x, p[1], p[2]) for p in body_loop(hw, zb, zt)] for x, hw, zb, zt in STATIONS]), WHITE))


def build_canopy():
    rows = []
    for x in (2.06, 1.88, 1.64, 1.36, 1.04, 0.72, 0.42, 0.20):
        hw, zb, zt = at_x(x)
        f = max(0.0, min(1.0, (2.06 - x) / 0.70))
        g = max(0.0, min(1.0, (x - 0.20) / 0.45))
        w = hw * (0.24 + 0.74 * f) * (0.35 + 0.65 * g)
        top = zt + 0.032 * f * g
        rows.append([(x, w * (i / 6.0), top - (zt - zb) * 0.30 * (i / 6.0) ** 2) for i in range(-6, 7)])
    return smooth(paint(grid_mesh("Canopy", rows), GLASS, rough=0.05))


# ------------------------------------------------------------------------------------------------------
def wing_section(y, n=18, shrink=1.0):
    c = lerp_chord(y) * shrink
    xle, z0 = wing_le(y), wing_z(y)
    us = cos_u(n)
    # Symmetric, because the config flies `swift-sym` and the config is the physics. The manufacturer
    # quotes a cambered NACA 64(1)-412; the disagreement is recorded in MEASUREMENTS.md, not resolved here.
    t = 0.135 - 0.015 * (abs(y) / HALF)
    pts = [(xle - u * c, y, z0 + naca(t, u, True) * c) for u in us]
    pts += [(xle - u * c, y, z0 + naca(t, u, False) * c) for u in reversed(us[1:-1])]
    return pts


def build_wing(side):
    ys = [0.0, 0.8, 1.6, 2.4, 3.0, 3.6, 4.3, 5.0, 5.6, 6.05, 6.25, HALF]
    return smooth(paint(mesh_from_loops(
        "Wing%d" % side,
        [wing_section(side * y, shrink=1.0 if y < HALF - 0.3 else 0.45 if y < HALF else 0.18)
         for y in ys]), WHITE))


def build_fin():
    loops = []
    for z in (FIN_ROOT_Z - 0.28, FIN_ROOT_Z, 0.60, 0.85, 1.10, FIN_TOP_Z):
        f = max(0.0, (z - FIN_ROOT_Z) / (FIN_TOP_Z - FIN_ROOT_Z))
        xle = FIN_ROOT_LE + (FIN_TIP_LE - FIN_ROOT_LE) * f
        c = TAIL - xle
        t = 0.135 - 0.025 * f
        us = cos_u(10)
        ring = [(xle - u * abs(c), naca(t, u, True) * abs(c), z) for u in us]
        ring += [(xle - u * abs(c), naca(t, u, False) * abs(c), z) for u in reversed(us[1:-1])]
        loops.append(ring)
    return smooth(paint(mesh_from_loops("Fin", loops), WHITE))


def build_stab():
    """The tailplane, HALF WAY UP THE FIN. This is the line the photographs drew and the config does not."""
    loops = []
    for y in (-STAB_HALF, -STAB_HALF * 0.8, -STAB_HALF * 0.4, 0.0,
              STAB_HALF * 0.4, STAB_HALF * 0.8, STAB_HALF):
        f = abs(y) / STAB_HALF
        c = STAB_ROOT_C - (STAB_ROOT_C - STAB_TIP_C) * f
        xle = STAB_LE - 0.10 * f
        us = cos_u(10)
        ring = [(xle - u * c, y, STAB_Z + naca(0.105, u, True) * c) for u in us]
        ring += [(xle - u * c, y, STAB_Z + naca(0.105, u, False) * c) for u in reversed(us[1:-1])]
        loops.append(ring)
    return smooth(paint(mesh_from_loops("Stabiliser", loops), WHITE))


def build_gear():
    obs = [smooth(paint(wheel("Mainwheel", MAIN_X, 0.0, MAIN_Z + MAIN_R, MAIN_R, 0.075), RUBBER)),
           smooth(paint(wheel("Tailwheel", TAILW_X, 0.0, TAILW_Z + TAILW_R, TAILW_R, 0.028), RUBBER)),
           smooth(paint(cyl("TailLeg", (TAILW_X, 0.0, -0.09), (TAILW_X, 0.0, TAILW_Z + TAILW_R), 0.016),
                        METAL))]
    return obs


def build_trim():
    """A red band round the boom ahead of the fin, which is how SP-P600 is painted."""
    loops = []
    for x in (-3.20, -3.45, -3.70):
        hw, zb, zt = at_x(x)
        loops.append([(x, p[1] * 1.03, p[2] * 1.03) for p in body_loop(hw, zb, zt)])
    return smooth(paint(mesh_from_loops("BoomTrim", loops, close_ends=False), RED))


# ------------------------------------------------------------------------------------------------------
def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts = [build_body(), build_canopy(), build_fin(), build_stab(), build_trim()]
    for side in (-1, 1):
        parts.append(build_wing(side))
    parts += build_gear()

    for ob in parts:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    os.makedirs(OUT, exist_ok=True)
    glb = os.path.join(OUT, "glider-swift-s1-like.glb")
    bpy.ops.export_scene.gltf(filepath=glb, export_format='GLB', use_selection=True, export_apply=True)

    pts = [o.matrix_world @ v.co for o in parts for v in o.data.vertices]
    span = max(p.y for p in pts) - min(p.y for p in pts)
    length = max(p.x for p in pts) - min(p.x for p in pts)
    height = max(p.z for p in pts) - min(p.z for p in pts)
    print("SWIFT parts %d  faces %d" % (len(parts), sum(len(o.data.polygons) for o in parts)))
    print("SWIFT span %.3f (TCDS %.2f)  length %.3f (%.2f)  height %.3f (gear down; TCDS 1.74 is gear up)"
          % (span, SPAN, length, LENGTH, height))
    print("SWIFT L/S %.4f (real %.4f)" % (length / span, LENGTH / SPAN))
    print("SWIFT wing area %.2f m2 drawn, %.2f certificated (chords scaled by %.3f)"
          % (area_of(CHORD), AREA, KC))
    print("SWIFT tailplane at z %.3f, half way up a fin running %.3f to %.3f" % (STAB_Z, FIN_ROOT_Z, FIN_TOP_Z))
    print("SWIFT wrote %s" % glb)


main()
