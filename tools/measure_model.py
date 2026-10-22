"""Measure a reference model's true cross-sections, so our own mesh can be built to them.

    blender --background --python tools/measure_model.py -- <model.obj|glb> [stations]

WHY THIS IS NOT COPYING. Copyright protects a modeller's EXPRESSION -- their topology, their UV layout, their
texture art. It does not protect the shape of a real Cessna 172, which is a fact about an aeroplane Cessna
built; three modellers measuring the same real machine converge on the same cross-sections. This reads
DIMENSIONS out of a reference and prints them as numbers. Nothing is copied into our mesh: our mesh is built
from the numbers, with our own topology, exactly as the Jenny's config was built from measurements taken off
helijah's mesh (docs/research/JN4-JENNY-DATA.md 6).

Averaging the MESHES would be a different thing entirely, and a worse one -- a derivative of every source at
once, and mush where the sharp features were. Averaging the MEASUREMENTS converges on the real aeroplane.
"""
import bpy, sys, os, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
path = argv[0]
N = int(argv[1]) if len(argv) > 1 else 14
# A downloaded model is in whatever units its author used. Give the REAL span and everything is reported in
# metres, which is what makes two references comparable and averageable.
TRUE_SPAN = float(argv[2]) if len(argv) > 2 else 0.0

bpy.ops.wm.read_factory_settings(use_empty=True)
lower = path.lower()
if lower.endswith((".glb", ".gltf")):
    bpy.ops.import_scene.gltf(filepath=path)
elif lower.endswith((".usdz", ".usd", ".usda", ".usdc")):
    bpy.ops.wm.usd_import(filepath=path)        # Sketchfab hands out USDZ to Apple platforms
elif lower.endswith(".fbx"):
    bpy.ops.import_scene.fbx(filepath=path)
else:
    bpy.ops.wm.obj_import(filepath=path)

objs = [o for o in bpy.data.objects if o.type == 'MESH']
pts = []
for o in objs:
    m = o.matrix_world
    for v in o.data.vertices:
        pts.append(m @ v.co)
if not pts:
    print("MEASURE no geometry"); sys.exit()

mn = Vector((min(p[i] for p in pts) for i in range(3)))
mx = Vector((max(p[i] for p in pts) for i in range(3)))
size = mx - mn
# the longest axis is the span, the next the length, the shortest the height -- true of every aeroplane
order = sorted(range(3), key=lambda i: -size[i])
SPAN, LEN, HGT = order[0], order[1], order[2]
print("MEASURE file=%s" % os.path.basename(path))
print("MEASURE extents span=%.3f length=%.3f height=%.3f (axes %d/%d/%d)" % (size[SPAN], size[LEN], size[HGT], SPAN, LEN, HGT))

k = (TRUE_SPAN / size[SPAN]) if TRUE_SPAN > 0 else 1.0
print("MEASURE scale=%.6f -> span %.3f length %.3f height %.3f m" % (k, size[SPAN]*k, size[LEN]*k, size[HGT]*k))

# Slice along the LENGTH axis. The fuselage is isolated by a band that is ABSOLUTE in metres, not a fraction
# of span: a fraction catches the wing root on a short-coupled aeroplane and reports a 0.9 m half-width on a
# body that is really 0.5 m. The 90th percentile rather than the max rejects a stray vertex on a fairing.
BODY = 0.62                                   # m from the centreline: wider than any light single's body
lo, hi = mn[LEN], mx[LEN]
def sect(c, band):
    return [p for p in pts if abs(p[LEN] - c) <= band and abs(p[SPAN]) * k <= BODY]
band0 = (hi - lo) / (N * 2.0)
def hw_of(sel):
    if not sel: return 0.0
    w = sorted(abs(p[SPAN]) for p in sel)
    return w[int(len(w) * 0.9)] * k
EDGE_BIN = 0.03        # m: a slab of skin thick enough to hold a few vertices
EDGE_FRAC = 0.22       # a bin counts as BODY if it is at least this wide compared with the body
# 0.45 was too strict and biased the TOP LINE LOW on a narrow section: the topmost slabs of a rounded body
# are themselves narrow, so the walk inward skipped past the skin and reported a bin some way down the
# side. On the 172's tailcone that cost 0.1-0.3 m and made the boom droop. 0.22 still rejects a fin (0.03 m
# of half-thickness against a want of 0.066 at the same station) and keeps the skin (0.166).


def edge(sel, hw, top):
    """The body's OWN top or bottom at a station -- not the fin standing above it or the gear hanging below.

    This is the measurement the first pass of the 172 gave up on, and giving up on it is why that model read
    as a teardrop: the half-widths were measured and the DEPTHS WERE A GUESS, so the aeroplane had a correct
    plan view and an invented side view. A 172 lives in its side view -- the raked windscreen, the flat cabin
    roof, the tapered boom -- so a guessed one is most of the shape thrown away.

    The reason a naive min/max fails is that a slice catches everything the aeroplane has at that station. The
    trick is that THE PARTS THAT ARE NOT THE BODY ARE THIN IN SPAN: a fin, a gear leg, a wheel, an aerial. So
    bin the slice by height and walk inward from the extreme, taking the first bin that is still a real
    fraction of the body's own width. The fin is 50 mm thick and is skipped; the skin, 1.1 m across, is not.
    """
    if not sel or hw <= 0:
        return 0.0
    zs = [p[HGT] for p in sel]
    bins = {}
    for p in sel:
        b = int(round(p[HGT] * k / EDGE_BIN))
        w = abs(p[SPAN]) * k
        if w > bins.get(b, -1.0):
            bins[b] = w
    want = EDGE_FRAC * hw
    for b in sorted(bins, reverse=top):
        if bins[b] >= want:
            return b * EDGE_BIN
    # nothing is wide enough (the extreme nose or tailpost): fall back to the plain extreme
    return (max(zs) if top else min(zs)) * k


# WHICH END IS THE NOSE. Comparing the two ends does not work -- both are thin, a spinner at one and a
# tailpost at the other. The CABIN is the tell: on a light single the widest body section sits forward of the
# midpoint, so the nose is whichever end the widest station is nearer to.
widest_f, widest_w = 0.5, -1.0
for i in range(N + 1):
    f = i / N
    w = hw_of(sect(lo + (hi - lo) * f, band0))
    if w > widest_w: widest_w, widest_f = w, f
nose_at_lo = widest_f < 0.5
print("MEASURE widest body section %.3f m at frac %.2f -> nose at %s end" % (widest_w, widest_f, "low" if nose_at_lo else "high"))
print("MEASURE nose at %s end" % ("low" if nose_at_lo else "high"))
print("MEASURE   s(m)  frac  halfwidth   bottom      top     (m, from the nose)")
rows = []
for i in range(N + 1):
    f = i / N
    c = (lo + (hi - lo) * f) if nose_at_lo else (hi - (hi - lo) * f)
    sel = sect(c, band0)
    if len(sel) < 6:
        continue
    w = sorted(abs(p[SPAN]) for p in sel)
    hw = w[int(len(w) * 0.9)] * k
    # The body's own top and bottom -- see edge().
    zb, zt = edge(sel, hw, top=False), edge(sel, hw, top=True)
    s_m = f * size[LEN] * k
    rows.append((s_m, f, hw, zb, zt))
    print("MEASURE %6.3f %5.3f %9.3f %10.3f %9.3f" % (s_m, f, hw, zb, zt))
# print it ready to paste into a model script
zmid = (rows[0][3] + rows[0][4]) * 0.5 if rows else 0.0
print("MEASURE STATIONS = [")
for s_m, f, hw, zb, zt in rows:
    print("MEASURE     (%.2f, %.3f, %.3f, %.3f)," % (s_m, hw, zb - zmid, zt - zmid))
print("MEASURE ]")
