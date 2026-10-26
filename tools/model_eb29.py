"""Model a Binder EB29 in Blender.

    blender --background --python tools/model_eb29.py        -> build/models/glider-eb29r-like.glb

WHY THIS EXISTS. `glider-eb29r-like` has been wearing UNDERPOLY's generic *Free Sailplane Glider* -- a
roughly 15 m club ship standing in for a 29.3 m open-class aeroplane. `tools/fleet_accuracy.py` found it on
2026-10-25 at **+57% length/span and +127% height/span**, the worst in the fleet by a wide margin, and no
scale fixes a ratio. An open-class ship is mostly wing; the stand-in is mostly fuselage.

WHERE THE NUMBERS COME FROM. There is no free three-view of an EB29 and only **two** freely-licensed
photographs on Commons, so this is the thin end of the reference scale (`docs/MODEL-PIPELINE.md` section 6
lists the four types in that position). What exists:

  - the published `{{Aircraft specs}}` figures in `assets-incoming/ref-eb29/DIMENSIONS.md`:
    **span 29.3 m, length 8.0 m, height 1.85 m, wing area 16.8 m2**. Those four set the model's overall
    size and the wing's planform, and the first three are what `fleet_accuracy.py` checks it against.
  - `configs/aircraft/glider-eb29r-like.json` for the shape within that size -- the wing's taper and its
    kink at about 6.3 m, the dihedral (0.06 rad), where the tail sits. The chords are scaled so the drawn
    planform integrates to the published 16.8 m2; the config's own strips sum to 18.6, about 11% high.
  - the two photographs of **D-KJHS** and **D-KTEL** for everything a number cannot say: the long bubble
    canopy running most of the pod, the slender boom, the swept T-tail fin, and the **winglets**, which are
    half of what makes an open-class tip read as one.

HEIGHT, as on the PA-44. The config's fin tops out 1.556 m above the body axis with the monowheel contact
0.75 below it, which is 2.31 m against a published 1.85 -- **+25%**. The wheel stays at the config's
contact so the aeroplane sits on its own gear, and the fin is drawn to put the top at 1.10 above the axis,
which is the published height. Recorded in `assets-incoming/ref-eb29/MEASUREMENTS.md`.

AXES: Blender x forward (nose +x), y right, z up, origin on the body axis. The glTF export is Y-up, so the
file reads length x, height y, span z -- what `AirframeModels.OwnSpecs` expects.
"""
import bpy, bmesh, json, math, os
from mathutils import Vector

REPO = os.path.dirname(os.path.dirname(os.path.realpath(__file__)))
OUT = os.path.join(REPO, "build/models")
CFG = json.load(open(os.path.join(REPO, "configs/aircraft/glider-eb29r-like.json")))

WHITE = (0.905, 0.905, 0.900, 1.0)        # a glass ship is white because the resin cannot take the heat
RED = (0.420, 0.030, 0.035, 1.0)
TRIM = (0.247, 0.263, 0.298, 1.0)
GLASS = (0.075, 0.105, 0.130, 1.0)
RUBBER = (0.055, 0.058, 0.062, 1.0)
METAL = (0.520, 0.530, 0.545, 1.0)

SPAN, LENGTH, HEIGHT, AREA = 29.3, 8.0, 1.85, 16.8
HALF = SPAN / 2.0
TAIL = -6.43                              # the config's rudder trailing edge
NOSE = TAIL + LENGTH                      # 1.57
GROUND = -0.75                            # the config's monowheel contact
FIN_TOP = HEIGHT + GROUND                 # 1.10 above the body axis
WING_Z = -0.15
DIHEDRAL = 0.06                           # rad, from the config
WINGLET = 0.45                            # how much of the span the tip turns up over
WINGLET_RISE = 0.35                       # and how far up it goes. A glider winglet is about this tall;
                                          # taller and the tips, already 0.88 m up on 0.06 rad of
                                          # dihedral, climb past the fin and the aeroplane measures taller
                                          # than its published height, which is to the top of the FIN.


def cfg_wing():
    """Wing chord PLUS aileron chord, which is the planform.

    The two are separate surfaces and the `wing` strips stop at the hinge line: their chord drops from
    0.703 to 0.493 at y 6.802, exactly where `wing-aileron` starts. Drawn from the wing strips alone the
    aeroplane gets a 30% step in its chord at 46% of the semi-span that the real one does not have. Added
    together the taper is straight, which is the check that this is the right reading."""
    w = [s for s in CFG["surfaces"] if s["id"] == "wing"][0]
    asurf = [s for s in CFG["surfaces"] if s["id"] == "wing-aileron"][0]
    ail = {round(st["pos"][1], 3): st["chord"] for st in asurf["strips"]}
    pts = []
    for st in sorted(w["strips"], key=lambda s: s["pos"][1]):
        y = st["pos"][1]
        if y <= 0:
            continue
        pts.append((y, st["chord"] + ail.get(round(y, 3), 0.0)))
    k = HALF / pts[-1][0]
    return [(y * k, c) for y, c in pts]


def chord_scale(table):
    """One factor on every chord, so the drawn planform integrates to the published area."""
    a = 0.0
    prev = (0.0, table[0][1])
    for y, c in table:
        a += 0.5 * (prev[1] + c) * (y - prev[0])
        prev = (y, c)
    a += 0.5 * table[-1][1] * 0.0
    return AREA / (2.0 * a)


TABLE = cfg_wing()
KC = chord_scale(TABLE)
CHORD = [(0.0, TABLE[0][1] * KC)] + [(y, c * KC) for y, c in TABLE]


def lerp_chord(y):
    y = min(abs(y), HALF)
    for (a, ca), (b, cb) in zip(CHORD, CHORD[1:]):
        if y <= b:
            t = 0.0 if b == a else (y - a) / (b - a)
            return ca + (cb - ca) * t
    return CHORD[-1][1]


def wing_le(y):
    """Straight leading edge inboard of the kink, a touch of sweep outboard -- the photographs' planform."""
    return 0.32 - 0.004 * abs(y)


def wing_z(y):
    y = abs(y)
    z = WING_Z + y * math.tan(DIHEDRAL)
    if y > HALF - WINGLET:                      # the tip turns up over the last half metre
        f = (y - (HALF - WINGLET)) / WINGLET
        z += WINGLET_RISE * f * f
    return z


# ------------------------------------------------------------------------------------------------------
def naca(t, u, upper):
    y = 5 * t * (0.2969 * math.sqrt(u) - 0.1260 * u - 0.3516 * u ** 2 + 0.2843 * u ** 3
                 - 0.1036 * u ** 4) + 0.0018 * u
    return y if upper else -y


def laminar(u, upper):
    """A thin, highly cambered laminar section: 14% thick, which is what a modern open-class wing is."""
    m, p, t = 0.028, 0.45, 0.140
    u = min(max(u, 0.0), 1.0)
    yc = (m / p ** 2 * (2 * p * u - u * u) if u < p else
          m / (1 - p) ** 2 * ((1 - 2 * p) + 2 * p * u - u * u))
    return yc + naca(t, u, upper)


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


def paint(ob, rgba, rough=0.18):           # gelcoat, polished: a glass ship is the shiniest thing on the field
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
    loops = [[(p + u * math.cos(2 * math.pi * i / seg) + v * math.sin(2 * math.pi * i / seg))[:]
              for i in range(seg)] for p in (p0, p1)]
    return mesh_from_loops(name, loops)


def wheel(name, cx, cy, cz, r, width, seg=18):
    return mesh_from_loops(name, [[(cx + r * math.cos(2 * math.pi * i / seg), cy + s * width * 0.5,
                                    cz + r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]
                                  for s in (-1, 1)])


# ------------------------------------------------------------------------------------------------------
# the pod and boom
# ------------------------------------------------------------------------------------------------------
STATIONS = [
    (NOSE, 0.030, -0.055, 0.055),
    (1.38, 0.145, -0.200, 0.165),
    (1.08, 0.245, -0.320, 0.300),
    (0.68, 0.310, -0.400, 0.430),
    (0.22, 0.335, -0.435, 0.480),
    (-0.22, 0.330, -0.435, 0.480),
    (-0.72, 0.295, -0.405, 0.430),
    (-1.32, 0.235, -0.335, 0.330),
    (-2.05, 0.165, -0.245, 0.225),
    (-2.95, 0.115, -0.170, 0.150),
    (-3.95, 0.085, -0.130, 0.112),
    (-4.95, 0.065, -0.100, 0.088),
    (-5.65, 0.055, -0.085, 0.075),
]
NA = 14


def body_loop(hw, zb, zt, power=2.1):
    zc, zr = 0.5 * (zb + zt), 0.5 * (zt - zb)
    e = 2.0 / power
    return [(0.0,
             hw * math.copysign(abs(math.cos(2 * math.pi * i / (2 * NA))) ** e,
                                math.cos(2 * math.pi * i / (2 * NA))),
             zc + zr * math.copysign(abs(math.sin(2 * math.pi * i / (2 * NA))) ** e,
                                     math.sin(2 * math.pi * i / (2 * NA))))
            for i in range(2 * NA)]


def at_x(x):
    if x >= STATIONS[0][0]:
        return STATIONS[0][1:]
    for (xa, ha, ba, ta), (xb, hb, bb, tb) in zip(STATIONS, STATIONS[1:]):
        if x >= xb:
            f = (xa - x) / (xa - xb)
            return (ha + (hb - ha) * f, ba + (bb - ba) * f, ta + (tb - ta) * f)
    return STATIONS[-1][1:]


def build_body():
    loops = [[(x, p[1], p[2]) for p in body_loop(hw, zb, zt)] for x, hw, zb, zt in STATIONS]
    return smooth(paint(mesh_from_loops("Pod", loops), WHITE))


def build_canopy():
    """One long bubble from the nose back past the wing leading edge, which is most of this aeroplane's
    side view and the thing the stand-in model got most wrong."""
    rows = []
    xs = [1.53, 1.40, 1.22, 1.00, 0.72, 0.40, 0.06, -0.26, -0.54, -0.76]
    for x in xs:
        hw, zb, zt = at_x(x)
        f = max(0.0, min(1.0, (1.53 - x) / 0.75))            # opens out over the first three quarters
        g = max(0.0, min(1.0, (x + 0.76) / 0.55))            # and closes again onto the deck at the back
        w = hw * (0.22 + 0.76 * f) * (0.35 + 0.65 * g)
        top = zt + 0.030 * f * g
        row = []
        for i in range(-6, 7):
            t = i / 6.0
            row.append((x, w * t, top - (zt - zb) * 0.30 * t * t))
        rows.append(row)
    return smooth(paint(grid_mesh("Canopy", rows), GLASS, rough=0.05))


# ------------------------------------------------------------------------------------------------------
def wing_section(y, n=18, shrink=1.0):
    c = lerp_chord(y) * shrink
    xle, z0 = wing_le(y), wing_z(y)
    cant = 0.0
    if abs(y) > HALF - WINGLET:                 # the winglet stands up, so its section rolls with it
        cant = math.radians(72.0) * ((abs(y) - (HALF - WINGLET)) / WINGLET) ** 1.4
    us = cos_u(n)
    pts = []
    for u in us:
        pts.append((xle - u * c, y, z0 + laminar(u, True) * c))
    for u in reversed(us[1:-1]):
        pts.append((xle - u * c, y, z0 + laminar(u, False) * c))
    if cant:
        out = []
        for px, py, pz in pts:
            dz = pz - z0
            out.append((px, py - dz * math.sin(cant) * (1 if y > 0 else -1), z0 + dz * math.cos(cant)))
        pts = out
    return pts


def build_wing(side):
    ys = [0.0, 1.0, 2.5, 4.0, 5.5, 6.3, 7.5, 9.0, 10.5, 12.0, 13.2, 14.05,
          HALF - WINGLET, HALF - WINGLET * 0.6, HALF - WINGLET * 0.25, HALF - 0.04, HALF]
    loops = [wing_section(side * y, shrink=1.0 if y < HALF - 0.05 else 0.35) for y in ys]
    return smooth(paint(mesh_from_loops("Wing%d" % side, loops), WHITE))


def build_root_fairing(side):
    """The wing-root fairing, which on an open-class ship is a long blended shoulder rather than a fillet."""
    rows = []
    for x in (0.42, 0.10, -0.28, -0.62):
        hw, zb, zt = at_x(x)
        rows.append([(x, side * (hw * 0.96), WING_Z + 0.11),
                     (x, side * (hw * 1.9), WING_Z + 0.055),
                     (x, side * (hw * 2.9), WING_Z + 0.02)])
    return smooth(paint(grid_mesh("Fairing%d" % side, rows), WHITE))


# ------------------------------------------------------------------------------------------------------
FIN_ROOT_Z = 0.08
FIN_ROOT_LE = -4.62
FIN_TIP_LE = -5.52


def build_fin():
    loops = []
    for z in (FIN_ROOT_Z, 0.34, 0.60, 0.86, FIN_TOP):
        f = (z - FIN_ROOT_Z) / (FIN_TOP - FIN_ROOT_Z)
        xle = FIN_ROOT_LE + (FIN_TIP_LE - FIN_ROOT_LE) * f
        c = TAIL - xle
        t = 0.105 - 0.015 * f
        us = cos_u(10)
        ring = [(xle - u * abs(c), naca(t, u, True) * abs(c), z) for u in us]
        ring += [(xle - u * abs(c), naca(t, u, False) * abs(c), z) for u in reversed(us[1:-1])]
        loops.append(ring)
    return smooth(paint(mesh_from_loops("Fin", loops), WHITE))


def build_stab():
    half = 1.575 * (HALF / 14.127)             # the config's stabilator, on the published span
    z = FIN_TOP - 0.055
    loops = []
    for y in (-half, -half * 0.8, -half * 0.42, 0.0, half * 0.42, half * 0.8, half):
        f = abs(y) / half
        c = 0.66 - 0.27 * f
        xle = FIN_TIP_LE + 0.09 + 0.05 * f
        us = cos_u(10)
        ring = [(xle - u * c, y, z + naca(0.095, u, True) * c) for u in us]
        ring += [(xle - u * c, y, z + naca(0.095, u, False) * c) for u in reversed(us[1:-1])]
        loops.append(ring)
    return smooth(paint(mesh_from_loops("Stabilator", loops), WHITE))


def build_gear():
    obs = []
    r = 0.195
    obs.append(smooth(paint(wheel("Monowheel", 0.20, 0.0, GROUND + r, r, 0.085), RUBBER)))
    tw = 0.055
    obs.append(smooth(paint(wheel("Tailwheel", -5.40, 0.0, -0.45 + tw, tw, 0.030), RUBBER)))
    obs.append(smooth(paint(cyl("TailLeg", (-5.40, 0.0, -0.22), (-5.40, 0.0, -0.45 + tw), 0.018), METAL)))
    return obs


def build_trim():
    """The red band round the fin root and the boom, which both photographed ships carry."""
    loops = []
    for x in (-4.30, -4.62, -4.95):
        hw, zb, zt = at_x(x)
        loops.append([(x, p[1] * 1.02, p[2] * 1.02) for p in body_loop(hw, zb, zt)])
    return smooth(paint(mesh_from_loops("BoomTrim", loops, close_ends=False), RED))


# ------------------------------------------------------------------------------------------------------
def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts = [build_body(), build_canopy(), build_fin(), build_stab(), build_trim()]
    for side in (-1, 1):
        parts.append(build_wing(side))
        parts.append(build_root_fairing(side))
    parts += build_gear()

    for ob in parts:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    os.makedirs(OUT, exist_ok=True)
    glb = os.path.join(OUT, "glider-eb29r-like.glb")
    bpy.ops.export_scene.gltf(filepath=glb, export_format='GLB', use_selection=True, export_apply=True)

    pts = [o.matrix_world @ v.co for o in parts for v in o.data.vertices]
    span = max(p.y for p in pts) - min(p.y for p in pts)
    length = max(p.x for p in pts) - min(p.x for p in pts)
    height = max(p.z for p in pts) - min(p.z for p in pts)
    area = 0.0
    prev = (0.0, CHORD[0][1])
    for y, c in CHORD:
        area += 0.5 * (prev[1] + c) * (y - prev[0])
        prev = (y, c)
    print("EB29 parts %d  faces %d" % (len(parts), sum(len(o.data.polygons) for o in parts)))
    print("EB29 span %.3f (real %.3f)  length %.3f (%.3f)  height %.3f (%.3f)"
          % (span, SPAN, length, LENGTH, height, HEIGHT))
    print("EB29 L/S %.4f (real %.4f)   H/S %.4f (real %.4f)"
          % (length / span, LENGTH / SPAN, height / span, HEIGHT / SPAN))
    print("EB29 wing area %.2f m2 drawn, %.2f published (chords scaled by %.3f)" % (2 * area, AREA, KC))
    print("EB29 wrote %s" % glb)


main()
