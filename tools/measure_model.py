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
if path.lower().endswith(".glb") or path.lower().endswith(".gltf"):
    bpy.ops.import_scene.gltf(filepath=path)
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
    # The body's own top and bottom. A slice also catches the gear hanging below and the fin standing above,
    # so take the MEDIAN first and keep only what is within a body-depth of it before reading the extremes.
    zs = sorted(p[HGT] for p in sel)
    med = zs[len(zs) // 2]
    near = [z for z in zs if abs(z - med) * k <= 0.70]
    if len(near) < 4: near = zs
    zb, zt = near[int(len(near) * 0.05)] * k, near[int(len(near) * 0.95)] * k
    s_m = f * size[LEN] * k
    rows.append((s_m, f, hw, zb, zt))
    print("MEASURE %6.3f %5.3f %9.3f %10.3f %9.3f" % (s_m, f, hw, zb, zt))
# print it ready to paste into a model script
zmid = (rows[0][3] + rows[0][4]) * 0.5 if rows else 0.0
print("MEASURE STATIONS = [")
for s_m, f, hw, zb, zt in rows:
    print("MEASURE     (%.2f, %.3f, %.3f, %.3f)," % (s_m, hw, zb - zmid, zt - zmid))
print("MEASURE ]")
