"""Our own Great Lakes 2T-1A-2 style biplane (Aero Playground).   v3

Clean-room: all geometry is generated here from numbers measured by eye off a public 1970 three-view (American Aircraft
Modeler, Great Lakes 2T-1A, scaled to the TCDS A18EA span 26 ft 8 in / length 20 ft 4 in), the FAA TCDS A18EA (dimensions,
chord, sweep, engine / propeller, control travel), the Great Lakes aircraft maintenance manual (rigging; landing gear
layout drawings I-7 / I-9) and public photos of 2T-1A-2 aircraft. See docs/models/great-lakes-own-PROVENANCE.md. No
third-party or GPL mesh, UV or texture is read or imported, and nothing from the references is traced, sampled or copied
into the textures.

Variant: 2T-1A-2, Lycoming IO-360 class flat four, 180 hp, round Lycoming cowl + spinner, Hartzell 2-blade 74 in
constant-speed prop (clockwise from the cockpit), "big tail" fin/rudder, wheel pants, ailerons on the lower wing only
(factory 2-aileron), N-type interplane struts. Swept upper outer panels (9 deg), flat centre section with a semicircular
TE cut-out, 46 in chord. v2 rigging per the manual: upper wing 0 deg dihedral, lower 2 deg, 3 deg incidence on both.

v2 landing gear (rigged, separate parts): axle U-joint hinge at the fuselage centreline (Gear_Pivot_L/R), compression
strut aft to the lower longeron fitting (Gear_HingeAxis_<s> marks the far end of the hinge axis), oleo shock strut from the
outrigger top pin (Gear_OleoTop_<s>) to the axle end. Wheel + axle + pant rotate about the hinge axis while the oleo
telescopes (Oleo_Cylinder_<s> re-aims at Gear_OleoTop, Oleo_Shaft_<s> at the cylinder). Stroke 9 in (Ty-supplied
8-10 in; no published figure); the published 7 ft 4 in height corresponds to static load (~6.2 in compression). The GLB
is exported fully extended; the pivot extras carry stroke and hinge angles. GEARSTATE lines print the 3-point states.

v3 tail (Ty): the fin/rudder and stab/elevator plan forms follow the 1970 AAM 2T-1A three-view line for line (FLE/RTE
and STAB_T tables, measured off the drawing at 0.0115 m/px), checked by overlaying model silhouettes on the drawing
(overlay.py: side, top, bottom). The fin root is the concave blend into the top longeron exactly as drawn (dense
FIN_ST stations so the outline is not faceted). Rudder +-35 deg / elevator 21 up / 29 down clearances kept.

Liveries (GL_LIVERY): v1 = the original white placeholder (registration N719GL); red-sunburst and white-stripes = Ty's
two schemes, painted by eye from his photos, no registration, no logos or text. v3: the sunburst rays sweep AFT on the
upper wing (Ty); white-stripes redone from white-1/2/3 (see the livery comment below).

Frame: metres, nose +X, port +Y, up +Z; origin = upper-wing centre-section LE station on the thrust line (yawed 180 deg
at export, like the Pitts / EA300).
  blender -b --python tools/own_models/build_great_lakes.py
"""
import bpy, bmesh, sys, os, math, json, numpy as np
from mathutils import Matrix, Vector
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import paint
from paint import View
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
LIV = os.environ.get('GL_LIVERY', 'v1').lower()                    # v1 (placeholder) | red-sunburst | white-stripes
assert LIV in ('v1', 'red-sunburst', 'white-stripes'), LIV
OUT = os.environ.get('GL_OUT') or os.path.join(ROOT, 'unity/Assets/_Project/Resources/Models/great-lakes-like/great-lakes-like-own%s.glb' % ('' if LIV == 'v1' else '-' + LIV))
TEX = os.environ.get('GL_TEX', '/tmp/own_greatlakes_tex_' + LIV); os.makedirs(TEX, exist_ok=True)
RED, WHITE, NAVY = (196, 26, 38), (245, 245, 243), (22, 30, 70)
BLADE, STEEL, TYRE, CABIN = (60, 62, 66), (196, 200, 206), (40, 40, 42), (58, 60, 64)
bpy.ops.wm.read_factory_settings(use_empty=True)
IN = 0.0254
P = {}


def link(name, bm):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); return o
def loft_rings(bm, rings, cap0=True, cap1=True, closed=True):
    vs = [[bm.verts.new(p) for p in r] for r in rings]; fs = []
    n = len(rings[0])
    for a, b in zip(vs, vs[1:]):
        for i in range(n if closed else n - 1):
            j = (i + 1) % n; fs.append((bm.faces.new((a[i], a[j], b[j], b[i])), i, j))
    if cap0: bm.faces.new(vs[0][::-1])
    if cap1: bm.faces.new(vs[-1])
    return vs, fs
def tube(bm, pts, r, n=8, r2=None, cap=True):
    """round tube along a polyline (robust frame for paths along x)"""
    pts = np.array(pts, float); rs = []; m = len(pts)
    for k, p in enumerate(pts):
        d = pts[min(k + 1, m - 1)] - pts[max(k - 1, 0)]; d /= np.linalg.norm(d)
        ref = np.array([0, 0, 1.0]) if abs(d[0]) > 0.8 else np.array([1.0, 0, 0])
        u = np.cross(d, ref); u /= np.linalg.norm(u); w = np.cross(d, u)
        rk = r if r2 is None else r + (r2 - r) * k / (m - 1)
        rs.append([tuple(p + rk * (math.cos(t) * u + math.sin(t) * w)) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)])
    loft_rings(bm, rs, cap, cap)
def blade_strut(bm, a, b, chords, th, n=10):
    """streamlined strut from a to b; chord axis = flight direction (x) made normal to the strut; chords per station"""
    a, b = np.array(a, float), np.array(b, float); d = (b - a) / np.linalg.norm(b - a)
    cx = np.array([1.0, 0, 0]) - d[0] * d; cx /= np.linalg.norm(cx); ty = np.cross(d, cx)
    rs = []; k = len(chords)
    for i, c in enumerate(chords):
        p = a + (b - a) * i / (k - 1)
        ring = []
        for t in np.linspace(0, 2 * math.pi, 16, endpoint=False):
            u = 0.5 * c * math.cos(t); v = 0.5 * th * math.sin(t) * (1 - 0.35 * math.cos(t))   # blunt LE, fine TE
            ring.append(tuple(p + u * cx + v * ty))
        rs.append(ring)
    loft_rings(bm, rs)


# ---- fuselage: superellipse sections (flat-sided steel-tube fuselage, rounded turtledeck, round Lycoming cowl) -------
# Lines measured off the AAM three-view (side: top / bottom; top view: half-breadth) and, for the nose, the G-GLII and
# D-EBGL photos (Lycoming cowl: thrust line ~0.25 m below the cowl top, cowl wider than the fuselage for the flat four).
NT, NB, ZWF = 3.0, 4.0, 0.62
def tbl(t):
    xs = [p[0] for p in t][::-1]; ys = [p[1] for p in t][::-1]
    return lambda x: float(np.interp(x, xs, ys))
F_TOP0 = tbl([(0.57, 0.238), (0.47, 0.247), (-0.17, 0.25), (-0.40, 0.235),
             (-0.70, 0.215), (-0.95, 0.20), (-2.03, 0.20), (-2.30, 0.18), (-3.0, 0.13), (-3.8, 0.066), (-4.3, 0.0), (-4.565, -0.04)])
# v6 (red-2-ramp back-projection + nose crop): the real cowl chin is ~6-10 cm shallower than the three-view lines
F_BOT0 = tbl([(0.57, -0.50), (0.47, -0.55), (0.30, -0.615), (0.10, -0.69),
             (-0.17, -0.79), (-0.40, -0.81), (-0.70, -0.82), (-1.80, -0.82), (-4.565, -0.455)])
F_HW0 = tbl([(0.57, 0.41), (0.47, 0.435), (0.30, 0.445), (0.10, 0.445),
            (-0.17, 0.43), (-0.40, 0.405), (-0.70, 0.40), (-2.03, 0.40), (-2.30, 0.385), (-3.77, 0.20), (-4.30, 0.085),
            (-4.565, 0.02)])
# v4 (Ty: "the cowling is left chopped off on the front"): the cowl nose is a full rounded lip, a quarter-ellipse in
# side and plan from the x 0.57 station (tangent to the cowl sides) to a smaller front face at X_FACE 0.69 round the
# spinner, by eye from red-2-ramp / white-3-ramp / white-2-grass, instead of the v3 flat face with a 3 cm chamfer.
# v6 (red-2-ramp, measured against the spinner): the cowl runs ~0.11 m further forward and the spinner is shorter
# (0.30 m) and slimmer (r 0.155), spinner tip unchanged at x 1.10, so the navy bowl reads as long as on the photo.
X_LIP0, X_FACE = 0.64, 0.80
LIP_FACE = dict(top=0.17, bot=-0.26, hw=0.27)   # v6: smaller face ring hugging the slimmer spinner (red-2-ramp)
def _lip(f0, face):
    b = f0(X_LIP0)
    def g(x):
        if x <= X_LIP0: return f0(x)
        q = min((x - X_LIP0) / (X_FACE - X_LIP0), 1.0)
        return face + (b - face) * math.sqrt(max(1 - q * q, 0.0))
    return g
F_TOP, F_BOT, F_HW = _lip(F_TOP0, LIP_FACE['top']), _lip(F_BOT0, LIP_FACE['bot']), _lip(F_HW0, LIP_FACE['hw'])
SILL = 0.12
# v6 (Ty: "very tight cockpit openings ... rounded, like the real ones"; white-3-ramp, N3765F, three-view top view):
# two small oval holes in the rounded top deck (rounded front and rear ends in plan, a black padded coaming rim round
# each), instead of the v1-v5 full-width square-ended cuts down to a flat sill. (front x, aft x, half-width) per cockpit.
CKP = [(-0.97, -1.33, 0.19), (-1.64, -2.08, 0.215)]   # rear placed off the N3765F side photo (spinner-rudder scale)
P_CK = 2.0                                                         # plan superellipse exponent of the openings
XC1F, XC1A, XC2F, XC2A = CKP[0][0], CKP[0][1], CKP[1][0], CKP[1][1]  # front / rear cockpit openings (top view)
def ck_w(x, ck):
    xf, xa, hw = ck; u = (x - 0.5 * (xf + xa)) / (0.5 * (xf - xa))
    return hw * max(1 - abs(u) ** P_CK, 0.0) ** (1 / P_CK) if abs(u) < 1 else 0.0
def ck_xs(ck, n=19):
    xf, xa, _ = ck; return [0.5 * (xf + xa) + 0.5 * (xf - xa) * math.cos(t) for t in np.linspace(0, math.pi, n)]
def se_pt(t, top, bot, hw):
    zw = bot + ZWF * (top - bot); c, s = math.cos(t), math.sin(t); up = s >= 0
    n = NT if up else NB; h = (top - zw) if up else (zw - bot)
    return (hw * math.copysign(abs(c) ** (2 / n), c), zw + h * math.copysign(abs(s) ** (2 / n), s))
def t_at(z, top, bot):
    zw = bot + ZWF * (top - bot)
    if z >= zw: return math.asin(min(1.0, ((z - zw) / (top - zw)) ** (NT / 2)))
    return -math.asin(min(1.0, ((zw - z) / (zw - bot)) ** (NB / 2)))
NR = 48
def stn(x): return (x, F_TOP(x), F_BOT(x), F_HW(x))
def ring_closed(x, top, bot, hw):
    return [(x, *se_pt(t, top, bot, hw)) for t in np.linspace(0, 2 * math.pi, NR, endpoint=False)]
def ring_u(x, top, bot, hw, sill, wall=0.012, m=30):
    t0 = t_at(sill, top, bot); ts = np.linspace(t0, -math.pi - t0, m)
    out = [(x, *se_pt(t, top, bot, hw)) for t in ts]
    inn = [(x, *se_pt(t, top - wall, bot + wall, hw - wall)) for t in ts]
    inn[0] = (x, inn[0][1], sill); inn[-1] = (x, inn[-1][1], sill)
    return out + inn[::-1]
def ring_cut(x, top, bot, hw, w, wall=0.012, m=49):
    """section with an opening of half-width w cut out of the top deck (w ~ 0 closes it)"""
    # v6: fixed angular sampling clipped at the opening edge, so every station's vertex k sits at the same angle
    # (the old per-station spacing twisted the side quads and smeared the side stripe map into scallops)
    w = max(w, 0.003); t0 = math.acos(min((w / hw) ** (NT / 2), 1.0))
    ts = np.clip(np.linspace(math.pi / 2, -1.5 * math.pi, m), -math.pi - t0, t0)
    out = [(x, *se_pt(t, top, bot, hw)) for t in ts]
    inn = [(x, *se_pt(t, top - wall, bot + wall, hw - wall)) for t in ts]
    return out + inn[::-1]
def sill_w(x):
    top, bot, hw = F_TOP(x), F_BOT(x), F_HW(x)
    return abs(se_pt(t_at(SILL, top, bot), top, bot, hw)[0]), SILL
def fus_z(x, y, up=True):
    """fuselage surface height at station x, half-breadth y"""
    top, bot, hw = F_TOP(x), F_BOT(x), F_HW(x); zw = bot + ZWF * (top - bot); r = min(abs(y) / hw, 1.0)
    return zw + (top - zw) * (1 - r ** NT) ** (1 / NT) if up else zw - (zw - bot) * (1 - r ** NB) ** (1 / NB)
SEG_FWD = [round(X_LIP0 + (X_FACE - X_LIP0) * math.sin(a), 5) for a in np.linspace(math.pi / 2, 0, 12)] + [ 0.52, 0.47, 0.40, 0.30, 0.10, -0.17, -0.40, -0.70, -0.85, XC1F]
SEG_U1 = ck_xs(CKP[0])
SEG_BR = [XC1A, 0.5 * (XC1A + XC2F), XC2F]
SEG_U2 = ck_xs(CKP[1])
SEG_AFT = [XC2A, -2.30, -2.70, -3.10, -3.50, -3.80, -4.10, -4.30, -4.45, -4.565]
bm = bmesh.new()
fv, _ = loft_rings(bm, [ring_closed(*stn(x)) for x in SEG_FWD], cap0=False, cap1=True)
loft_rings(bm, [ring_cut(*stn(x), ck_w(x, CKP[0])) for x in SEG_U1], False, False)
loft_rings(bm, [ring_closed(*stn(x)) for x in SEG_BR])
loft_rings(bm, [ring_cut(*stn(x), ck_w(x, CKP[1])) for x in SEG_U2], False, False)
loft_rings(bm, [ring_closed(*stn(x)) for x in SEG_AFT])
# cowl front face: two round cooling inlets beside the spinner, as real holes
INL = dict(y=0.20, z=-0.02, a=0.04, b=0.058)   # v4: moved in to sit on the smaller rounded face; v6 smaller again
def inlet_pts(sy, s=1.0, x=X_FACE, n=24):
    return [(x, sy * INL['y'] + s * INL['a'] * math.cos(t), INL['z'] + s * INL['b'] * math.sin(t)) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)]
outer = fv[0]; edges = [bm.edges.get((outer[i], outer[(i + 1) % NR])) for i in range(NR)]
for sy in (1, -1):
    vv = [bm.verts.new(p) for p in inlet_pts(sy)]
    edges += [bm.edges.new((vv[i], vv[(i + 1) % len(vv)])) for i in range(len(vv))]
res = bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=edges)
newf = [g for g in res['geom'] if isinstance(g, bmesh.types.BMFace)]
holes = [f for f in newf if any(((f.calc_center_median().y - sy * INL['y']) / INL['a']) ** 2 + ((f.calc_center_median().z - INL['z']) / INL['b']) ** 2 < 1 for sy in (1, -1))]
bmesh.ops.delete(bm, geom=holes, context='FACES_ONLY')
print('COWL FACE tris', len(newf), 'removed in inlets', len(holes))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Fuselage'] = link('Fuselage', bm)

# ---- headrest fairing behind the rear cockpit (the drawing's "metal" headrest, narrowing aft into the fin root) -------
HR_X0, HR_X1, HR_Z0, HR_Z1 = -2.10, -3.75, 0.345, 0.07   # v6: starts behind the moved rear coaming
bm = bmesh.new(); rs = []
for k, x in enumerate([HR_X0 + 0.005, HR_X0 - 0.002, HR_X0 - 0.015, HR_X0 - 0.04, HR_X0 - 0.12, -2.40, -2.70, -3.00, -3.30, -3.55, -3.75]):
    f = (HR_X0 - x) / (HR_X0 - HR_X1); hw = 0.115 - 0.085 * f; top = HR_Z0 + (HR_Z1 - HR_Z0) * f
    if k < 3: hw *= (0.55, 0.82, 0.95)[k]; top -= (0.05, 0.015, 0.004)[k]
    base = fus_z(x, hw) - 0.02
    ring = []
    for t in np.linspace(0, math.pi, 17):
        c, s = math.cos(t), math.sin(t)
        ring.append((x, hw * math.copysign(abs(c) ** (2 / 2.6), c), base + (top - base) * abs(s) ** (2 / 2.6)))
    rs.append(ring)
loft_rings(bm, rs, closed=False); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Headrest'] = link('Headrest', bm)

# ---- windscreens: v6 (Ty: "should wrap around"): a bent three-panel wraparound screen ahead of each cockpit, a flat
# centre panel and two side wings swept back round the front of the opening, framed at the top, sides and bends. The
# rear screen moved aft (base x -1.35 -> -1.56) so it stands right at the front of the rear coaming, as in white-3-ramp
# and the N3765F side photo.
def wrap_screen(xc, W, h, D, rake, n=21, nr=6):
    rows = []
    for q in np.linspace(0, 1, nr):
        row = []
        for v in np.linspace(-1, 1, n):
            bend = max(abs(v) - 0.5, 0.0) / 0.5
            y = v * W * (1 - 0.10 * q); x0 = xc - D * bend
            hz = h * (1 - 0.30 * bend ** 1.5)
            row.append((x0 - hz * q * math.tan(math.radians(rake)) * (1 - 0.35 * bend), y, fus_z(x0, y) - 0.003 + hz * q))
        rows.append(row)
    return rows
bm_g = bmesh.new(); bm_f = bmesh.new()
for xc, W, h, D, rk in ((-0.90, 0.215, 0.15, 0.07, 40), (-1.56, 0.24, 0.18, 0.08, 38)):
    rows = wrap_screen(xc, W, h, D, rk); loft_rings(bm_g, rows, False, False, closed=False)
    tube(bm_f, rows[-1], 0.008, 6); tube(bm_f, [r[0] for r in rows], 0.008, 6); tube(bm_f, [r[-1] for r in rows], 0.008, 6)
    for k in (5, 15): tube(bm_f, [r[k] for r in rows], 0.005, 6)                       # bend posts
P['Windscreen_Glass'] = link('Windscreen_Glass', bm_g)
for ck in CKP:   # black padded coaming rim round each opening
    xf, xa, hw = ck; xm, a_ = 0.5 * (xf + xa), 0.5 * (xf - xa); pts = []
    for t in np.linspace(0, 2 * math.pi, 73):
        c, s_ = math.cos(t), math.sin(t)
        x = xm + a_ * math.copysign(abs(c) ** (2 / P_CK), c); y = (hw + 0.008) * math.copysign(abs(s_) ** (2 / P_CK), s_)
        pts.append((x, y, fus_z(x, y) + 0.008))
    tube(bm_f, pts, 0.019, 8, cap=False)
P['Windscreen_Frame'] = link('Windscreen_Frame', bm_f)


# ---- cockpit interior: tandem, two seats, two sticks, two panels (original primitives) ------------------------
ck = bmesh.new(); ck_tag = []
CK_T = ['ck_seat', 'ck_strap', 'ck_panel', 'ck_gauge', 'ck_face', 'ck_stick', 'ck_tube', 'ck_tub', 'ck_black']
def _ck(tag, fn):
    n0 = len(ck.faces); fn(); ck_tag.extend([CK_T.index(tag)] * (len(ck.faces) - n0))
def ck_box(tag, c, s, ry=0.0, rx=0.0):
    _ck(tag, lambda: bmesh.ops.create_cube(ck, size=1.0, matrix=Matrix.Translation(c) @ Matrix.Rotation(ry, 4, 'Y') @ Matrix.Rotation(rx, 4, 'X') @ Matrix.Diagonal((s[0], s[1], s[2], 1))))
def ck_cyl(tag, a, b, r, seg=12):
    a, b = np.array(a, float), np.array(b, float); d = b - a; ln = np.linalg.norm(d)
    q = Vector((0, 0, 1)).rotation_difference(Vector(d / ln)).to_matrix().to_4x4()
    _ck(tag, lambda: bmesh.ops.create_cone(ck, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=ln, matrix=Matrix.Translation(tuple((a + b) / 2)) @ q))
def ck_prism(tag, poly, x, th):
    def f():
        fr, bk = [], []
        for y, z in poly:
            fr.append(ck.verts.new((x - th / 2, y, z))); bk.append(ck.verts.new((x + th / 2, y, z)))
        ck.faces.new(fr[::-1]); ck.faces.new(bk); n = len(poly)
        for i in range(n): ck.faces.new((fr[i], fr[(i + 1) % n], bk[(i + 1) % n], bk[i]))
    _ck(tag, f)
def ck_gauge(x, y, z, r):
    """round instrument facing aft (-x) on a panel whose aft face is at x"""
    ck_cyl('ck_gauge', (x + 0.002, y, z), (x - 0.006, y, z), r, 16)
    ck_cyl('ck_face', (x - 0.006, y, z), (x - 0.008, y, z), r * 0.82, 16)
    ck_box('ck_stick', (x - 0.0095, y + r * 0.2, z + r * 0.15), (0.002, r * 0.6, r * 0.08), rx=-0.6)
def ck_seat(x0, zf, back_top, hw, rec_deg=20, pan_l=0.34):
    rec = math.radians(rec_deg); bx = x0 - pan_l
    ck_box('ck_seat', (x0 - pan_l / 2, 0, zf + 0.03), (pan_l, 2 * hw, 0.05), ry=math.radians(-6))
    ck_box('ck_seat', (x0 - 0.03, 0, zf + 0.07), (0.06, 2 * hw, 0.05))
    bl = (back_top - zf) / math.cos(rec); bc = (bx - math.sin(rec) * bl / 2, 0, zf + math.cos(rec) * bl / 2)
    ck_box('ck_seat', bc, (0.05, 2 * hw, bl), ry=-rec)
    for sy in (1, -1):
        ck_box('ck_seat', (bc[0] + 0.03, sy * (hw - 0.02), bc[2] - 0.05), (0.10, 0.04, bl * 0.8), ry=-rec)
        ck_box('ck_seat', (x0 - pan_l / 2, sy * (hw - 0.02), zf + 0.08), (pan_l * 0.9, 0.04, 0.07))
    for sy in (0.07, -0.07):
        ck_box('ck_strap', (bc[0] + 0.03, sy, bc[2] + 0.02), (0.008, 0.045, bl * 0.75), ry=-rec)
        ck_box('ck_strap', (bx + 0.06, sy * 2.2, zf + 0.09), (0.12, 0.045, 0.008), ry=math.radians(40))
    ck_box('ck_strap', (x0 - 0.12, 0, zf + 0.065), (0.12, 0.045, 0.008))
    ck_box('ck_strap', (bx + 0.10, 0, zf + 0.085), (0.06, 0.07, 0.03))
def ck_stick(x, zf, h):
    ck_cyl('ck_seat', (x, 0, zf), (x, 0, zf + 0.07), 0.034, 12)                                      # boot
    ck_cyl('ck_stick', (x, 0, zf + 0.05), (x - 0.045, 0, zf + h - 0.09), 0.010, 10)
    ck_cyl('ck_stick', (x - 0.045, 0, zf + h - 0.10), (x - 0.055, 0, zf + h), 0.016, 12)            # grip
ZFL = -0.70                                                                                           # cockpit floor
for xa, xb in ((XC1F + 0.12, XC1A - 0.02), (XC2F + 0.10, XC2A - 0.02)):
    ck_box('ck_tub', (0.5 * (xa + xb), 0, ZFL - 0.005), (xa - xb, 0.62, 0.01))
# front cockpit: small panel under the windscreen, seat, stick (seat pan front x -1.00)
ck_prism('ck_panel', [(-0.24, -0.10), (0.24, -0.10), (0.25, 0.05), (0.15, 0.12), (-0.15, 0.12), (-0.25, 0.05)], XC1F + 0.008, 0.012)  # v6: fits under the tighter deck
for y in (-0.13, -0.045, 0.045, 0.13): ck_gauge(XC1F + 0.002, y, 0.02, 0.032)
ck_seat(-0.98, ZFL + 0.08, 0.06, 0.22, pan_l=0.30)
ck_stick(-0.90, ZFL, 0.55)
# rear (PIC) cockpit: panel on the bulkhead behind the front seat, seat, stick
ck_prism('ck_panel', [(-0.26, -0.12), (0.26, -0.12), (0.27, 0.06), (0.16, 0.13), (-0.16, 0.13), (-0.27, 0.06)], XC2F + 0.008, 0.015)
ck_box('ck_black', (XC2F + 0.02, 0, 0.13), (0.06, 0.34, 0.012))
for y in (-0.16, -0.055, 0.055, 0.16): ck_gauge(XC2F + 0.002, y, 0.04, 0.032)
for y in (-0.11, 0.0, 0.11): ck_gauge(XC2F + 0.002, y, -0.05, 0.028)
ck_seat(-1.74, ZFL + 0.08, 0.10, 0.22, pan_l=0.32)
ck_stick(-1.66, ZFL, 0.56)
for sy in (1, -1):                                                                                     # steel-tube longerons + diagonals
    p = lambda x, dz=0.0: (x, sy * (sill_w(x)[0] - 0.035), SILL - 0.025 + dz)
    for xa, xb in ((XC1F, XC1A), (XC2F, XC2A)):
        ck_cyl('ck_tube', p(xa - 0.01), p(xb + 0.01), 0.011, 8)
        ck_cyl('ck_tube', p(xa - 0.01), (xb + 0.01, sy * (sill_w(xb)[0] - 0.05), ZFL + 0.05), 0.010, 8)
for x0 in (-0.80, -1.50):                                                                              # rudder pedals (under the decks)
    for sy in (1, -1): ck_box('ck_stick', (x0, sy * 0.13, ZFL + 0.10), (0.03, 0.08, 0.13), ry=0.35)
bmesh.ops.recalc_face_normals(ck, faces=ck.faces); P['Cockpit'] = link('Cockpit', ck)


# ---- flying surfaces --------------------------------------------------------------------------------------------
def yt(f, tc):    # NACA 4-digit half-thickness (slightly open TE so the TE edge is not degenerate)
    f = np.clip(f, 0, 1); return 5 * tc * (0.2969 * np.sqrt(f) - 0.1260 * f - 0.3516 * f ** 2 + 0.2843 * f ** 3 - 0.1015 * f ** 4)
def yc(f, m, p=0.4):
    f = np.clip(f, 0, 1); return np.where(f < p, m / p ** 2 * (2 * p * f - f ** 2), m / (1 - p) ** 2 * ((1 - 2 * p) + 2 * p * f - f ** 2))
class Surf:
    def __init__(self, le, te, z, tc, m=0.0, inc=0.0, axis='y', minc=0.012):
        self.le, self.te, self.z, self.tc, self.m, self.inc, self.axis, self.minc = le, te, z, tc, m, math.radians(inc), axis, minc
    def section(self, s, x_from=None, x_to=None, n=22):
        le, te = self.le(s), self.te(s)
        if le - te < self.minc: mid = 0.5 * (le + te); le, te = mid + 0.5 * self.minc, mid - 0.5 * self.minc
        c = le - te; xa = le if x_from is None else x_from; xb = te if x_to is None else x_to
        if xa - xb < 0.006: xb = xa - 0.006
        xq = xa + (xb - xa) * (1 - np.cos(np.linspace(0, math.pi, n))) / 2
        f = (le - xq) / c; zc = self.z(s) - f * c * math.sin(self.inc) + yc(f, self.m) * c; t = yt(f, self.tc) * c
        loop = list(zip(xq, zc + t)) + list(zip(xq[::-1], (zc - t)[::-1]))
        if x_from is None: loop = loop[:-1]
        if self.axis == 'y': return [(x, s, z) for x, z in loop]
        return [(x, z, s) for x, z in loop]            # fin/rudder: (x, thickness along y, height)
def surf_obj(name, surf, spans, x_from=None, x_to=None):
    bm = bmesh.new()
    secs = [surf.section(s, None if x_from is None else x_from(s), None if x_to is None else x_to(s)) for s in spans]
    loft_rings(bm, secs); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return link(name, bm)
L = lambda a, b, n: list(np.linspace(a, b, n))
def tipL(a, b, n):  # spans clustered toward the tip b
    return [a + (b - a) * math.sin(0.5 * math.pi * t) for t in np.linspace(0, 1, n)]
# 46 in chord both wings (TCDS / three-view), NACA 4-digit-like section ~12 % (the drawing's M-6-like profile), 1 deg
# incidence. Equal span 26 ft 8 in. Upper wing: flat centre section +-0.92 m with a semicircular TE cut-out over the
# front cockpit, outer panels swept 9 deg (LE) with 2.75 deg dihedral; lower wing unswept, 2.5 deg dihedral, root in
# the fuselage side. Tips: asymmetric elliptical rounding (LE over the last 0.38 m, TE over the last 0.60 m), as drawn.
CH, GAP = 46 * IN, 0.006
S_W = 26 * 0.3048 / 2 + 8 * IN / 2                                  # 4.064 m semi-span
RL, RT = 0.38, 0.60
def tip_round(le0):
    def le(y):
        d = np.clip((np.abs(y) - (S_W - RL)) / RL, 0, 1); return le0(y) - 0.35 * CH * (1 - np.sqrt(np.maximum(1 - d ** 2, 0)))
    def te(y):
        d = np.clip((np.abs(y) - (S_W - RT)) / RT, 0, 1); return le0(y) - CH + 0.65 * CH * (1 - np.sqrt(np.maximum(1 - d ** 2, 0)))
    return le, te
Y_CS, SWEEP_U, DIH_U, DIH_L = 0.92, 9.0, 0.0, 2.0   # v2: rigging manual III-4: 0 deg upper, 2 deg lower
LEU0 = lambda y: -np.maximum(np.abs(y) - Y_CS, 0) * math.tan(math.radians(SWEEP_U))
Z_U = lambda y: 0.69 + max(abs(y) - Y_CS, 0) * math.tan(math.radians(DIH_U))
LEU, TEU = tip_round(LEU0)
WU = Surf(LEU, TEU, Z_U, 0.12, m=0.02, inc=3.0)   # v2: 3 deg incidence (rigging manual)
CUT_R, CUT_D = 0.43, 0.17                                           # centre-section TE cut-out (half-width, depth)
def wu_to(s):
    return (-CH + CUT_D * math.sqrt(max(1 - (s / CUT_R) ** 2, 0.0)) + 0.0005) if abs(s) < CUT_R else None
sp_u = sorted(set([round(v, 4) for v in list(CUT_R * np.sin(np.linspace(0, math.pi / 2, 9))) + L(0.55, Y_CS, 3) +
                   L(Y_CS + 0.15, S_W - RT, 8) + tipL(S_W - RT + 0.02, S_W - 0.001, 16)]))
sp_u = [-s for s in sp_u[::-1] if s > 0] + sp_u
P['Wing_Upper'] = surf_obj('Wing_Upper', WU, sp_u, x_to=wu_to)
LE_L, Y_LR = -0.65, 0.36
LEL0 = lambda y: LE_L + 0.0 * np.abs(y)
Z_L = lambda y: -0.74 + abs(y) * math.tan(math.radians(DIH_L))
LEL, TEL = tip_round(LEL0)
WL = Surf(LEL, TEL, Z_L, 0.12, m=0.02, inc=3.0)
HF, AL_IN, AL_OUT = 0.76, 1.32, 3.46                               # Friese ailerons, lower wing only (factory 2-aileron)
HL = lambda y: LEL0(y) - HF * CH
def wl_to(s): return (HL(s) + GAP) if AL_IN < abs(s) <= AL_OUT else None
sp_l = L(Y_LR, AL_IN, 7) + L(AL_IN + 0.0005, AL_OUT, 11) + tipL(AL_OUT + 0.0005, S_W - 0.001, 16)
bm = bmesh.new()
for sg in (1, -1):
    secs = [WL.section(sg * s, None, wl_to(s)) for s in sp_l]
    loft_rings(bm, secs if sg > 0 else secs[::-1])
bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Wing_Lower'] = link('Wing_Lower', bm)
for side, sg in (('L', 1), ('R', -1)):
    n = 'Aileron_L' + side
    P[n] = surf_obj(n, WL, [sg * s for s in L(AL_IN + 0.004, AL_OUT - 0.004, 10)], x_from=lambda s: HL(s) - GAP)
# horizontal tail: (|y|, LE ahead of the hinge, TE aft of the hinge) from the three-view top view, hinge-referenced
H_T, Z_S = -4.59, -0.14   # hinge line as drawn on the side view (px 1232.5)
STAB_T = [(0.0, .62, .10), (0.07, .62, .126), (0.20, .615, .40), (0.31, .61, .44), (0.38, .605, .47), (0.48, .592, .515),
          (0.667, .569, .548), (0.771, .546, .535), (0.955, .495, .505), (1.058, .454, .44), (1.242, .360, .357),
          (1.346, .276, .265), (1.415, .218, .21), (1.461, .161, .17), (1.505, .09, .10), (1.545, 0.0, 0.0)]
B_S = STAB_T[-1][0]
_sd = [t[0] for t in STAB_T]
SLE = lambda y: H_T + np.interp(np.abs(y), _sd, [t[1] for t in STAB_T])
STE = lambda y: H_T - np.interp(np.abs(y), _sd, [t[2] for t in STAB_T])
ST_ = Surf(SLE, STE, lambda y: Z_S, 0.08)
sp = sorted(set([round(d, 4) for d in _sd if d < B_S] + [round(v, 4) for v in tipL(0, B_S * (1 - 1e-4), 12)] + [0.045, 0.065]))
sp = [-s_ for s_ in sp[::-1] if s_ > 0] + sp
P['Stab'] = surf_obj('Stab', ST_, sp, x_to=lambda s: H_T + 0.004 + 0.022 * float(np.clip((0.065 - abs(s)) / 0.02, 0, 1)))
# elevators: inboard ends cut on a diagonal from the hinge line at |y| = E_IN so the rudder (TCDS travel ~ +-35 deg is
# assumed here pending a check) swings through a V gap, as on our Pitts
E_IN, E_PHI = 0.06, 32.0
E_TAN = math.tan(math.radians(E_PHI))
E_CUT = lambda s: H_T - 0.004 - (abs(s) - E_IN) / E_TAN
_m = np.linspace(E_IN, 0.8, 8001); E_MEET = float(_m[np.argmax(E_CUT(_m) <= STE(_m))])
def e_to(s): return float(E_CUT(s)) if E_CUT(s) > STE(s) else None
e_sp = [E_IN] + [E_IN + (E_MEET - E_IN) * t for t in np.linspace(0, 1, 15)[1:]] + [d for d in _sd if E_MEET + 0.01 < d < B_S] + [v for v in tipL(1.2, B_S * (1 - 1e-4), 8)]
e_sp = sorted(set(e_sp))
for side, sg in (('L', 1), ('R', -1)):
    n = 'Elevator_' + side
    P[n] = surf_obj(n, ST_, [sg * s_ for s_ in e_sp], x_from=lambda s: H_T - 0.004, x_to=e_to)
print('ELEV_V E_IN %.3f E_PHI %.1f E_MEET %.4f' % (E_IN, E_PHI, E_MEET))
# fin + rudder: the ORIGINAL 2T-1A outline exactly as drawn on the 1970 AAM three-view side view (Ty: "exact to the
# drawing"), read off a 5 px grid at 0.0115 m/px, hinge-referenced: FLE (z, m ahead of the hinge), RTE (z, m aft)
FLE = [(-0.4, 0.86), (0.0, 0.83), (0.04, 0.80), (0.061, 0.779), (0.068, 0.752), (0.084, 0.727), (0.102, 0.702),
       (0.128, 0.677), (0.167, 0.650), (0.192, 0.645),   # fin root: concave blend into the top longeron, as drawn (Ty)
       (0.249, 0.639), (0.307, 0.629), (0.364, 0.616), (0.422, 0.602), (0.479, 0.582), (0.537, 0.557),
       (0.594, 0.524), (0.652, 0.487), (0.709, 0.442), (0.767, 0.378), (0.824, 0.294), (0.882, 0.199),
       (0.899, 0.142), (0.911, 0.073), (0.914, 0.0)]
RTE = [(-0.437, 0.0), (-0.418, 0.077), (-0.383, 0.176), (-0.326, 0.291), (-0.268, 0.364), (-0.211, 0.413),
       (-0.153, 0.447), (-0.096, 0.474), (-0.038, 0.496), (0.019, 0.512), (0.077, 0.52), (0.134, 0.524),
       (0.25, 0.52), (0.364, 0.508), (0.422, 0.496), (0.479, 0.479), (0.537, 0.459), (0.594, 0.43), (0.652, 0.398),
       (0.709, 0.36), (0.767, 0.307), (0.824, 0.225), (0.882, 0.11), (0.914, 0.0)]
Z_FT = 0.914
fle = lambda z: H_T + float(np.interp(z, *zip(*FLE))); rte = lambda z: H_T - float(np.interp(z, *zip(*RTE)))
FN = Surf(fle, rte, lambda z: 0.0, 0.065, axis='z')
R_POST = lambda z: float(yt((fle(z) - H_T) / (fle(z) - rte(z)), 0.065) * (fle(z) - rte(z)))
FIN_ST = sorted(set([round(z, 4) for z in tipL(-0.06, Z_FT - 0.004, 16)] + [0.03, 0.055, 0.061, 0.068, 0.076, 0.084, 0.093, 0.102, 0.115, 0.128, 0.147, 0.167, 0.18, 0.192, 0.22, 0.249]))   # dense through the root blend
P['Fin'] = surf_obj('Fin', FN, FIN_ST, x_to=lambda s: H_T + R_POST(s) + 0.003)
rz = [-0.433 + (Z_FT - 0.003 + 0.433) * (1 - math.cos(math.pi * t)) / 2 for t in np.linspace(0, 1, 30)]
def rudder_sec(z, nn=9):
    sec = FN.section(z, x_from=H_T); rho = max(abs(sec[0][1]), 0.004)
    return sec + [(H_T + rho * math.cos(t), rho * math.sin(t), z) for t in np.linspace(-math.pi / 2, math.pi / 2, nn + 2)[1:-1]]
bm = bmesh.new(); loft_rings(bm, [rudder_sec(z) for z in rz]); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
P['Rudder'] = link('Rudder', bm)
_mg = 9.0   # clearance check: rudder at +-35 deg vs the elevator V gap (plan view; conservative z band for 21 up / 29 down)
for _v in P['Rudder'].data.vertices:
    _a, _y, _z = H_T - _v.co.x, _v.co.y, _v.co.z
    for _d in (35.0, -35.0):
        _c, _s_ = math.cos(math.radians(_d)), math.sin(math.radians(_d))
        _aa, _yy = _a * _c - _y * _s_, abs(_a * _s_ + _y * _c)
        if abs(_z - Z_S) < _aa * math.tan(math.radians(29)) + 0.05 and _yy > E_IN:
            _mg = min(_mg, _aa - (_yy - E_IN) / E_TAN)
print('RUDDER_35_CLEARANCE_MIN %.3f m' % _mg)

# ---- struts and wires ------------------------------------------------------------------------------------------
def wsurf(W_, y, f, upper):
    """point on a wing's upper / lower skin at span y, chord fraction f"""
    le, te = float(W_.le(y)), float(W_.te(y)); c = le - te
    zc = W_.z(y) - f * c * math.sin(W_.inc) + float(yc(f, W_.m)) * c; t = float(yt(f, W_.tc)) * c
    return np.array((le - f * c, y, zc + (t if upper else -t)))
YI = 2.875
bm = bmesh.new()   # interplane N struts (front post, rear post, diagonal), streamlined steel tube
for sy in (1, -1):
    y = sy * YI; cu = CH; cl = CH
    fu = wsurf(WU, y, (float(LEU0(y)) + 0.48) / cu, False) + (0, 0, 0.01)
    ru = wsurf(WU, y, (float(LEU0(y)) + 1.11) / cu, False) + (0, 0, 0.01)
    fl = wsurf(WL, y, (LE_L + 0.84) / cl, True) - (0, 0, 0.01)
    rl = wsurf(WL, y, (LE_L + 1.37) / cl, True) - (0, 0, 0.01)
    for a, b in ((fl, fu), (rl, ru), (fl + (0, 0, 0.02), ru - (0.05, 0, 0.02))):
        blade_strut(bm, tuple(a), tuple(b), [0.055] * 4, 0.020)
P['Strut_Interplane'] = link('Strut_Interplane', bm)
bm = bmesh.new()   # cabane: an N each side, upper longerons -> centre section, splayed out
for sy in (1, -1):
    ft, rt = (-0.15, sy * 0.70, 0.62), (-0.80, sy * 0.70, 0.62)
    fb, rb = (-0.12, sy * 0.33, 0.20), (-0.80, sy * 0.33, 0.17)
    for a, b in ((fb, ft), (rb, rt), (rb, ft)): blade_strut(bm, a, b, [0.045] * 4, 0.018)
P['Strut_Cabane'] = link('Strut_Cabane', bm)
bm = bmesh.new(); RWI = 0.0035
for sy in (1, -1):
    for dx in (0.0, -0.07):   # double flying wires: lower fuselage at the lower-wing root -> upper wing at the N strut
        tube(bm, [(-0.90 + dx, sy * 0.40, -0.72), (-0.55 + dx, sy * 2.85, 0.77)], RWI, 6)
    for dx in (0.0, -0.07):   # double landing wires: cabane top -> lower wing at the N strut
        tube(bm, [(-0.25 + dx, sy * 0.70, 0.62), (-0.90 + dx, sy * 2.85, -0.60)], RWI, 6)
    tube(bm, [(-4.40, 0.0, 0.55), (-4.30, sy * 0.95, Z_S + 0.02)], RWI, 6)                  # tail: fin -> stab top
P['Wire_Bracing'] = link('Wire_Bracing', bm)
bm = bmesh.new()
for sy in (1, -1):   # stab struts below (streamlined tube, fuselage bottom -> stab underside)
    blade_strut(bm, (-4.30, sy * 0.06, -0.48), (-4.25, sy * 0.95, Z_S - 0.02), [0.03] * 3, 0.012)
P['Strut_Stab'] = link('Strut_Stab', bm)

# ---- undercarriage (v2): the real 2T-1A-2 layout, rigged so the strut can be animated --------------------------------
# Maintenance & Rigging Manual 2T-1A-2 (Waco Classic), I-7 "Landing Gear Assembly" + I-9 "Shock Strut Assembly":
#   * 20101 axle assy: a split axle, hinged at the fuselage centre line on a U-joint (A-60406), out to the wheel;
#   * A-60399 compression strut (faired, 20119) from the axle end aft-up to a fuselage fitting;
#   * 20102-1 shock strut, near vertical, from the axle end up to a fuselage outrigger fitting, inside the B-60999 oleo
#     streamline fairing with a 62169 cap: the 20102-5 shaft/piston on TOP, the 20102-11 coil spring and the 20102-2
#     cylinder BELOW (oleo + spring, 16 oz MIL-H-5606).  6.00-6 mains (600 x 6, 29 psi).
# So the axle + compression strut are one rigid triangle that swings about the line through its two fuselage pins
# (centre-line hinge H, compression-strut fitting C); the shock strut (top pin T fixed, bottom pin at the axle end A)
# telescopes.  Pin positions: three-view side view (by eye) + photos N3765F, N6196L (side) and the Commons front photo.
# Stroke: no published figure (manual, TCDS); Ty (Great Lakes pilot) gives 8-10 in of travel -> 9 in used here.
# The meshes are built in the EXTENDED (no-load) position; the hinge angles for static and full compression are
# computed below and written to the GLB (node extras) and to PROVENANCE.
R_W = 17.5 * IN / 2                                  # 6.00-6 tyre, 17.5 in OD
X_AX, Y_AX, Z_AX = -0.377, 0.97, -1.487              # wheel centre, extended (three-view side view axle centre)
G_H = np.array((-0.36, 0.05, -0.83))                 # axle hinge (U-joint), just off the centre line under the belly
G_C = np.array((-0.85, 0.40, -0.78))                 # compression-strut fuselage fitting (lower longeron, lower-wing spar)
G_T = np.array((-0.20, 0.62, -0.60))                 # shock-strut top pin on the outrigger fitting (firewall bottom)
G_A = np.array((X_AX, Y_AX - 0.13, Z_AX))            # axle end: shock-strut bottom pin + compression-strut end
STROKE = 9.0 * IN
L_CYL, L_SH = 0.50, 0.44                             # cylinder (+ streamline fairing) / exposed shaft lengths, measured
G_U = (G_C - G_H) / np.linalg.norm(G_C - G_H)        # hinge axis (port side; starboard mirrors y)
def g_rot(p, th, sy=1):
    """rotate point p (port-side coords) about the hinge axis by th (rad, + = wheel up)"""
    u = G_U.copy(); h = G_H.copy(); v = np.array(p, float) - h
    r = v * math.cos(th) + np.cross(u, v) * math.sin(th) + u * np.dot(u, v) * (1 - math.cos(th))
    return h + r
_s = 1.0 if g_rot(G_A, 0.05)[2] > G_A[2] else -1.0  # sign so + raises the wheel
def g_len(th): return float(np.linalg.norm(G_T - g_rot(G_A, _s * th)))
L_EXT = g_len(0.0)
def g_theta_for(dl):
    lo, hi = 0.0, 0.8
    for _ in range(60):
        mid = 0.5 * (lo + hi)
        if L_EXT - g_len(mid) < dl: lo = mid
        else: hi = mid
    return 0.5 * (lo + hi)
TH_FULL = g_theta_for(STROKE)
def g_wheel(th): return g_rot((X_AX, Y_AX, Z_AX), _s * th)
print('GEAR L_ext %.3f m (%.1f in)  stroke %.1f in -> hinge %.2f deg, wheel rise %.3f m, wheel dx %+.3f dy %+.3f'
      % (L_EXT, L_EXT / IN, STROKE / IN, math.degrees(TH_FULL), g_wheel(TH_FULL)[2] - Z_AX, g_wheel(TH_FULL)[0] - X_AX,
         g_wheel(TH_FULL)[1] - Y_AX))
assert L_CYL + L_SH > L_EXT - 0.02 and L_SH - STROKE > 0.05, 'cylinder/shaft lengths must overlap and keep shaft visible'

bm = bmesh.new()                                      # fixed structure: outrigger fittings + strut caps, tail spring
for sy in (1, -1):
    m = np.array((1, sy, 1.0)); T = G_T * m
    blade_strut(bm, (T[0] + 0.01, sy * 0.42, T[2] + 0.02), tuple(T + (0, 0, 0.0)), [0.07] * 3, 0.035)   # outrigger
    d = (G_A - G_T) * m; d /= np.linalg.norm(d)
    blade_strut(bm, tuple(T - 0.03 * d), tuple(T + 0.09 * d), [0.075, 0.09, 0.095], 0.05)                 # 62169 cap
TW = dict(c=(-4.83, 0.0, -0.83), r=0.10, w=0.06)
tube(bm, [(-4.40, 0, -0.47), (-4.55, 0, -0.56), (-4.68, 0, -0.65), (-4.78, 0, -0.73)], 0.018, 8, r2=0.011)   # tail spring
for sy in (1, -1): blade_strut(bm, (-4.78, sy * 0.045, -0.72), (-4.83, sy * 0.045, -0.83), [0.045] * 3, 0.008)  # fork
bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Gear_Main'] = link('Gear_Main', bm)
bm = bmesh.new()
bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=TW['r'], radius2=TW['r'], depth=TW['w'],
                      matrix=Matrix.Translation(TW['c']) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
P['Wheel_Tail'] = link('Wheel_Tail', bm)
WP = [(-0.076, 0.12), (-0.072, 0.183), (-0.055, 0.213), (-0.024, 0.222), (0.024, 0.222), (0.055, 0.213), (0.072, 0.183), (0.076, 0.12)]
PANT_V7 = [(0.46, 0, .01, .01, .01), (0.42, 0, .11, .10, .083), (0.31, 0, .20, .165, .12), (0.13, 0, .26, .165, .13),
        (0, 0, .27, .165, .13), (-0.17, .02, .245, .155, .12), (-0.355, .045, .18, .12, .094), (-0.53, .065, .09, .055, .05),
        (-0.64, .075, .01, .01, .01)]          # (dx from the axle, dz centre, top half-height, bottom half-height, half-width)
PANT = [(0.46, .03, .01, .01, .01), (0.42, .03, .10, .09, .083), (0.31, .02, .18, .145, .12), (0.13, .01, .225, .145, .13),
        (0, 0, .23, .145, .13), (-0.17, .03, .21, .13, .12), (-0.355, .075, .155, .095, .094), (-0.53, .12, .075, .045, .05),
        (-0.64, .155, .01, .01, .01)]          # v8 (red-2-ramp PnP overlay): ~10% shallower (0.375 m), tail swept UP to dz .155;
                                               # length unchanged (nose/tail matched the photo).
if LIV != 'red-sunburst': PANT = PANT_V7          # white-stripes pants unchanged (different aircraft)
GEAR_SIDES = {}
for side, sy in (('L', 1), ('R', -1)):
    m = np.array((1, sy, 1.0)); H, C, T, A = G_H * m, G_C * m, G_T * m, G_A * m
    bm = bmesh.new()                                  # axle assy + compression strut + axle stub + hub (one rigid part)
    tube(bm, [tuple(H), tuple(A)], 0.024, 10)                                                            # 20101 axle
    bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=6, radius=0.035, matrix=Matrix.Translation(tuple(H)))   # U-joint
    blade_strut(bm, tuple(A), tuple(C), [0.065] * 4, 0.028)                                                # A-60399 (faired)
    tube(bm, [tuple(A), (X_AX, sy * (Y_AX + 0.06), Z_AX)], 0.022, 10)                                      # stub axle
    bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=0.06, radius2=0.06, depth=0.16,
                          matrix=Matrix.Translation((X_AX, sy * Y_AX, Z_AX)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Gear_Axle_' + side] = link('Gear_Axle_' + side, bm)
    bm = bmesh.new()
    rs = [[(X_AX + r * math.sin(a), sy * Y_AX + dy, Z_AX + r * math.cos(a)) for a in np.linspace(0, 2 * math.pi, 32, endpoint=False)] for dy, r in WP]
    loft_rings(bm, rs if sy > 0 else rs[::-1]); P['Wheel_' + side] = link('Wheel_' + side, bm)
    bm = bmesh.new()
    rs = [[(X_AX + dx, sy * Y_AX + hw * math.cos(t), Z_AX + dz + (ht if math.sin(t) > 0 else hb) * math.sin(t))
           for t in np.linspace(0, 2 * math.pi, 24, endpoint=False)] for dx, dz, ht, hb, hw in PANT]
    loft_rings(bm, rs); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Wheel_Pant_' + side] = link('Wheel_Pant_' + side, bm)
    d = (T - A) / np.linalg.norm(T - A)
    bm = bmesh.new()                                  # 20102-2 cylinder inside the B-60999 oleo streamline (moves with A)
    blade_strut(bm, tuple(A - 0.02 * d), tuple(A + L_CYL * d), [0.085, 0.10, 0.105, 0.105, 0.10], 0.06)
    P['Oleo_Cylinder_' + side] = link('Oleo_Cylinder_' + side, bm)
    bm = bmesh.new()                                  # 20102-5 shaft / piston (chrome), hangs from T
    tube(bm, [tuple(T), tuple(T - L_SH * d)], 0.019, 14); P['Oleo_Shaft_' + side] = link('Oleo_Shaft_' + side, bm)
    GEAR_SIDES[side] = dict(H=H, C=C, T=T, A=A)

# ---- spinner and prop: Hartzell 2-blade constant speed, 74 in, clockwise seen from the cockpit (Lycoming) -----------
SPB, SPX, SPR = X_FACE + 0.006, 1.10, 0.155
bm = bmesh.new(); ts = np.linspace(0.97, 0.0, 12)
rs = [[bm.verts.new((SPB + (SPX - SPB) * t, SPR * (1 - t ** 2) ** 0.6 * math.sin(a), SPR * (1 - t ** 2) ** 0.6 * math.cos(a))) for a in np.linspace(0, 2 * math.pi, 28, endpoint=False)] for t in ts]
tipv = bm.verts.new((SPX, 0, 0))
for i in range(28): j = (i + 1) % 28; bm.faces.new((tipv, rs[0][i], rs[0][j]))
for a_, b2 in zip(rs, rs[1:]):
    for i in range(28): j = (i + 1) % 28; bm.faces.new((a_[i], b2[i], b2[j], a_[j]))
bm.faces.new(rs[-1]); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Spinner'] = link('Spinner', bm)
PROP_X, R_P, PITCH, NBL, TIP = 0.85, 37 * IN, 1.7, 2, 4 * IN
def chord(q):
    c = (0.085 + 0.075 * math.sin(math.pi * min(q / 0.5, 1) * 0.5)) - 0.03 * max(q - 0.5, 0) - (0.09 * ((q - 0.9) / 0.1) ** 2 if q > 0.9 else 0)
    return max(c, 0.02)
bm = bmesh.new(); face_tag = []
for blade in range(NBL):
    ph = math.pi / 2 + blade * 2 * math.pi / NBL
    rad = np.array([0, math.sin(ph), math.cos(ph)]); tan = np.cross([1.0, 0, 0], rad)   # direction of travel: clockwise from the cockpit
    ax = np.array([1.0, 0, 0]); hub = np.array([PROP_X, 0, 0])
    rr = np.linspace(0.10, R_P, 18); secs = []
    for r in rr:
        q = (r - 0.10) / (R_P - 0.10); c = chord(q)
        beta = math.atan(PITCH / (2 * math.pi * max(r, 0.25))); th = (0.30 - 0.24 * q) * c if q < 0.12 else (0.16 - 0.10 * q) * c
        sp_ = []
        for a in np.linspace(0, 2 * math.pi, 18, endpoint=False):
            u = 0.5 * c * math.cos(a); v = 0.5 * th * math.sin(a)
            cd = math.cos(beta) * tan + math.sin(beta) * ax; nd = -math.sin(beta) * tan + math.cos(beta) * ax
            sp_.append(hub + r * rad + u * cd + v * nd)
        secs.append([bm.verts.new(p) for p in sp_])
    for k in range(len(secs) - 1):
        for i in range(18):
            j = (i + 1) % 18; bm.faces.new((secs[k][i], secs[k][j], secs[k + 1][j], secs[k + 1][i]))
            face_tag.append(1 if 0.5 * (rr[k] + rr[k + 1]) > R_P - TIP else 0)
    bm.faces.new(secs[-1]); face_tag.append(1); bm.faces.new(secs[0][::-1]); face_tag.append(0)
P['Prop'] = link('Prop', bm)

# ---- O-360 (simplified flat four from primitives), inlet ducts, firewall, exhaust ---------------------------------
en_bms = {}
EN_T = ['en_core', 'en_cyl', 'en_case', 'en_rocker', 'en_dark']
def _en(tag, fn):
    global en
    en = en_bms.setdefault(tag, bmesh.new()); fn()
def en_box(tag, c, s):
    _en(tag, lambda: bmesh.ops.create_cube(en, size=1.0, matrix=Matrix.Translation(c) @ Matrix.Diagonal((s[0], s[1], s[2], 1))))
def en_cyl(tag, a, b, r, seg=16, r2=None):
    a, b = np.array(a, float), np.array(b, float); d = b - a; ln = float(np.linalg.norm(d))
    q = Vector((0, 0, 1)).rotation_difference(Vector(d / ln)).to_matrix().to_4x4()
    _en(tag, lambda: bmesh.ops.create_cone(en, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=ln, matrix=Matrix.Translation(tuple((a + b) / 2)) @ q))
EN_X1, EN_PITCH, EN_STAG = 0.48, 0.24, 0.12
en_box('en_case', (0.32, 0, -0.02), (0.50, 0.24, 0.24))
en_cyl('en_case', (0.55, 0, 0), (0.70, 0, 0), 0.07)                                                  # nose case to the prop flange
for sy, x0 in ((-1, EN_X1), (1, EN_X1 - EN_STAG)):
    for k in range(2):
        x = x0 - k * EN_PITCH
        en_cyl('en_cyl', (x, sy * 0.12, 0), (x, sy * 0.30, 0), 0.068, 16)
        en_cyl('en_core', (x, sy * 0.29, 0), (x, sy * 0.38, 0), 0.075, 16)
        en_box('en_rocker', (x, sy * 0.395, 0), (0.12, 0.03, 0.14))
en_box('en_case', (0.20, 0, -0.30), (0.30, 0.20, 0.20))                                               # sump
for sy in (1, -1):
    _en('en_dark', lambda: loft_rings(en, [inlet_pts(sy, 1.0, X_FACE + 0.002), inlet_pts(sy, 0.92, X_FACE - 0.08)], False, False))
en_box('en_dark', (-0.17, 0, -0.28), (0.01, 0.66, 0.82))                                              # firewall
en_tag = []; parts = []
for t in EN_T:
    if t in en_bms:
        b_ = en_bms[t]; me = bpy.data.meshes.new('en_' + t); b_.to_mesh(me); b_.free(); o = bpy.data.objects.new('en_' + t, me)
        bpy.context.scene.collection.objects.link(o); parts.append(o); en_tag += [EN_T.index(t)] * len(me.polygons)
P['Engine'] = paint.join(parts, 'Engine')
bm = bmesh.new()
for sy in (1, -1): tube(bm, [(0.05, sy * 0.18, -0.66), (-0.02, sy * 0.18, -0.78), (-0.12, sy * 0.19, -0.84)], 0.026, 12)
P['Exhaust'] = link('Exhaust', bm)

if LIV == 'v1':
    # ---- paint (v1 placeholder livery, ours): white; red nose bowl, red cheat line with a navy pinstripe; red fin/rudder
    # cap, wing tips and stab tips with navy pinstripes; red struts and spinner; placeholder registration. No logos.
    X0, X1, Z0, Z1 = -5.25, 1.20, -1.80, 1.05
    X_TP = -4.565                                                    # tail post
    def XN(z):   # aft edge of the red nose bowl: a swept line, further aft at the chin
        return 0.36 - 0.06 * (z - 0.25)
    def ZC(x):   # cheat line centre
        return 0.02 + (-0.25 - 0.02) * (X_FACE - x) / (X_FACE - X_TP)
    def WC(x):   # cheat line width
        return 0.11 + (0.05 - 0.11) * (X_FACE - x) / (X_FACE - X_TP)
    PIN, PIN_GAP = 0.025, 0.02
    Z_CAP, CAP_PIN = 0.62, 0.025
    def side(name, flip):
        v = View(name, (X0, X1), (Z0, Z1), 4096, 1748, flip_a=flip, axes=(0, 2)); A, Z = v.A, v.B
        v.fill(np.ones_like(A, bool), WHITE)
        zc, wc = ZC(A), WC(A)
        v.fill((A > H_T + 0.012) & (np.abs(Z - zc) < 0.5 * wc), RED)                                   # cheat line
        pb = zc - 0.5 * wc - PIN_GAP
        v.fill((A > H_T + 0.012) & (Z < pb) & (Z > pb - PIN) & (A < XN(Z) + 0.02), NAVY)                # navy pinstripe below it
        v.fill((A > XN(Z)) & (Z > -0.95), RED)                                                         # nose bowl
        v.fill((A < X_TP + 1.2) & (Z > Z_CAP), RED)                                                   # fin / rudder cap
        v.fill((A < X_TP + 1.2) & (A > X_TP - 1.0) & (Z > Z_CAP - 0.02 - CAP_PIN) & (Z < Z_CAP - 0.02), NAVY)
        v.fill((Z < -1.05) & (A > X_AX + 0.18), RED)                                                   # pant noses
        rh = 0.20; aspect = 764 / 150
        v.stamp('greatlakes-n719gl.png', -2.25, -2.25 - rh * aspect, -0.56, -0.56 + rh)
        return v
    VL, VR = side('gl_side_l', True), side('gl_side_r', False)
    def plan(name, flip, yr, xr, w, h):
        v = View(name, yr, xr, w, h, flip_a=flip, axes=(1, 0)); return v, v.A, v.B
    VFT, Y, X = plan('gl_fus_top', True, (-0.5, 0.5), (X0, X1), 1024, 4096)
    VFT.fill(np.ones_like(Y, bool), WHITE)
    VFT.fill(X > XN(np.vectorize(lambda x, y: fus_z(x, y, True))(X, Y)), RED)
    VFB, Y, X = plan('gl_fus_bot', False, (-0.5, 0.5), (X0, X1), 1024, 4096)
    VFB.fill(np.ones_like(Y, bool), WHITE)
    VFB.fill(X > XN(np.vectorize(lambda x, y: fus_z(x, y, False))(X, Y)), RED)
    def tips(v, Y, y_red, pin):
        v.fill(np.ones_like(Y, bool), WHITE)
        v.fill(np.abs(Y) > y_red, RED)
        v.fill((np.abs(Y) > y_red - pin[0]) & (np.abs(Y) < y_red - pin[1]), NAVY)
    VUW, Y, X = plan('gl_wing_u', True, (-4.1, 4.1), (-1.75, 0.05), 4096, 1024); tips(VUW, Y, 3.45, (0.07, 0.04))
    VLW, Y, X = plan('gl_wing_l', True, (-4.1, 4.1), (-1.85, -0.60), 4096, 768); tips(VLW, Y, 3.45, (0.07, 0.04))
    VST, Y, X = plan('gl_stab', True, (-1.6, 1.6), (-5.15, -3.85), 2048, 1024); tips(VST, Y, 1.20, (0.06, 0.035))

else:
    # ---- liveries (v3): Ty's two schemes, from his photos by eye (no tracing, no sampling); no registration, no text/logos.
    #  red-sunburst : all red; navy nose bowl; two navy, white-outlined fuselage stripes nose to tail; red/white/navy sunburst
    #                 rays on both wings (top and bottom) and the stab; navy tips/outline; navy fin/rudder border; red pants
    #                 with a navy nose and stripe; red struts and gear; white spinner.
    #  white-stripes: white; red / light-blue / navy fuselage bands with white separations, from behind a white nose bowl
    #                 to the tail, sweeping up round the fin as concentric arcs (white rim, red, light blue) over a navy
    #                 core in the lower fin/rudder; navy below the stab; white belly. Wing tops solid red; upper-wing
    #                 underside red LE wrap + light-blue and navy bands full span; lower underside white with red LE;
    #                 stab red tips with light-blue / navy diagonals parallel to the tip; pants white-nosed with rising
    #                 light-blue / red bands and a low navy swoosh; white struts and gear; chrome spinner.
    LBLUE = (72, 150, 214)
    def sil(v, names):
        """plan/side silhouette of the named parts in view v (triangle raster, no external libs)"""
        m = np.zeros(v.A.shape, bool); ia, ib = v.axes
        for n in names:
            o = P[n]; co = np.array([x.co[:] for x in o.data.vertices])
            for poly in o.data.polygons:
                vs = list(poly.vertices)
                for k in range(1, len(vs) - 1):
                    tri = co[[vs[0], vs[k], vs[k + 1]]]; pa, pb = tri[:, ia], tri[:, ib]
                    us, vv = v.px(pa, pb); c0, c1 = int(max(min(us) - 1, 0)), int(min(max(us) + 2, v.w)); r0, r1 = int(max(min(vv) - 1, 0)), int(min(max(vv) + 2, v.h))
                    if c1 <= c0 or r1 <= r0: continue
                    A_, B_ = v.A[r0:r1, c0:c1], v.B[r0:r1, c0:c1]
                    (x1, x2, x3), (y1, y2, y3) = pa, pb
                    den = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3)
                    if abs(den) < 1e-12: continue
                    l1 = ((y2 - y3) * (A_ - x3) + (x3 - x2) * (B_ - y3)) / den
                    l2 = ((y3 - y1) * (A_ - x3) + (x1 - x3) * (B_ - y3)) / den
                    m[r0:r1, c0:c1] |= (l1 >= -1e-6) & (l2 >= -1e-6) & (l1 + l2 <= 1 + 1e-6)
        return m
    def erode(m, metres, v):
        pa = abs(v.a1 - v.a0) / v.w; n = max(1, int(round(metres / pa))); m = m.copy()
        for _ in range(n):
            e = m.copy(); e[1:] &= m[:-1]; e[:-1] &= m[1:]; e[:, 1:] &= m[:, :-1]; e[:, :-1] &= m[:, 1:]
            e[0] = e[-1] = False; e[:, 0] = e[:, -1] = False; m = e
        return m
    def close(m, metres, v):
        pa = abs(v.a1 - v.a0) / v.w; n = max(1, int(round(metres / pa))); d = m.copy()
        for _ in range(n):
            e = d.copy(); e[1:] |= d[:-1]; e[:-1] |= d[1:]; e[:, 1:] |= d[:, :-1]; e[:, :-1] |= d[:, 1:]; d = e
        return erode(d, metres, v) | m
    def bands(v, m, spec, clip=None):
        """concentric edge bands inward from the silhouette: spec = [(width_m, colour or None), ...]"""
        cur = m
        for wdt, col in spec:
            inner = erode(cur, wdt, v)
            if col is not None: v.fill(cur & ~inner & (True if clip is None else clip), col)
            cur = inner
    def plan(name, flip, yr, xr, w, h):
        v = View(name, yr, xr, w, h, flip_a=flip, axes=(1, 0)); return v, v.A, v.B
    X0, X1, Z0, Z1 = -5.25, 1.20, -1.80, 1.05
    X_TP = -4.565
    def XN(z): return 0.36 - 0.06 * (z - 0.25)                  # aft edge of the nose bowl
    # v5 (Ty: "make the side stripes curve and blend into the nose exactly like the photo", red-2-ramp zoom, by eye):
    # the red scheme's navy nose bowl ends in a shallow CHEVRON pointing aft at the level of the LOWER stripe (whose
    # front tapers into it); top edge at x ~0.47, lower edge sweeping forward to x ~0.53-0.56 at the chin.
    # XB(z) = its aft edge (navy forward of it, white outline just aft).
    # v6 (Ty: "just copy what is in the photos"): traced off red-2-ramp BACK-PROJECTED onto this side map through a
    # PnP-matched camera (greatlakes-own-v6/projmap.py), so these are photo positions in model metres, read by eye /
    # colour profile. Near-vertical upper edge at x ~0.55-0.565, aft-pointing chevron at (0.447, -0.12), lower edge
    # sweeping forward to x ~0.60 down the chin.
    _XBZ = [-0.95, -0.62, -0.45, -0.32, -0.24, -0.186, -0.171, -0.156, -0.141, -0.12, -0.10, -0.079, -0.054, -0.03, 0.02, 0.10, 0.19, 0.30]
    _XBX = [0.600, 0.600, 0.590, 0.575, 0.550, 0.520, 0.500, 0.480, 0.460, 0.447, 0.460, 0.480, 0.500, 0.520, 0.548, 0.555, 0.560, 0.565]
    def XB(z): return np.interp(z, _XBZ, _XBX)
    def dil(m, metres, v):
        pa = abs(v.a1 - v.a0) / v.w; n = max(1, int(round(metres / pa))); d = m.copy()
        for _ in range(n):
            e = d.copy(); e[1:] |= d[:-1]; e[:-1] |= d[1:]; e[:, 1:] |= d[:, :-1]; e[:, :-1] |= d[:, 1:]; d = e
        return d
    def sstep(x): x = np.clip(x, 0, 1); return x * x * (3 - 2 * x)
    def lerp(x, xs, zs): return np.interp(x, xs[::-1], zs[::-1]) if xs[0] > xs[-1] else np.interp(x, xs, zs)
    BASE = RED if LIV == 'red-sunburst' else WHITE
    PANT_Z = Z_AX + 0.0
    # white-stripes navy fin core ellipse (centre aft of hinge, z; radii). v7 (white-3 tail crop, measured against the
    # fin top, hinge line and stab): flatter, higher-shouldered dome whose top runs near-level from the hinge (z ~0.54)
    # to the rudder TE, and whose front edge sits further aft (stab level ~0.32 m ahead of the hinge, mid-fin ~0.26 m),
    # so the light-blue band between the red LE stripe and the core is ~0.21 m wide as on the photo (v6: 0.15 m).
    FCX, FCZ, FCA, FCB = 0.26, -0.10, 0.58, 0.72
    def side(name, flip):
        v = View(name, (X0, X1), (Z0, Z1), 4096, 1748, flip_a=flip, axes=(0, 2)); A, Z = v.A, v.B
        v.fill(np.ones_like(A, bool), BASE)
        fin = close(sil(v, ['Fin', 'Rudder']), 0.03, v)
        _ft = np.interp(A, [-4.565, -4.3, -3.8, -3.0], [-0.04, 0.0, 0.06, 0.13])
        ext = fin | ((Z < _ft + 0.012) & (A > H_T + 0.03) & (A < X_TP + 1.4))   # border follows the fin LE + whole rudder, not the fuselage join
        pant = (Z < -1.15)
        if LIV == 'red-sunburst':
            # v5 (Ty via red-1 / red-2 / red-3): the two navy, white-outlined stripes run straight aft from the nose and
            # taper to points at the stab root LE (v4). At the FRONT they blend into the nose bowl as on red-2-ramp:
            #  - upper stripe: flares into a tall navy panel; its top edge sweeps UP (hockey stick) to ~4 cm under the
            #    cowl top, its lower edge drops toward the chevron, and its front edge parallels the bowl outline with a
            #    thin red gap.
            #  - lower stripe (~0.16 m below): tapers to a point that runs into the bowl's aft-pointing chevron.
            # v6: both stripes TRACED from the red-2 back-projection (edge knots = navy edges, model metres). They run
            # aft and converge to one point at x -3.78 just ahead of the stab LE. At the front they curl AWAY from the
            # bowl's chevron: the upper stripe's front is a sharp tip curling UP (0.455, 0.09), the lower stripe's a
            # tip curling DOWN (0.47, -0.33); each ~0.075 m wide at the nose, ~0.15 m red between them.
            ok = ~fin & ~pant & (Z > -0.95)
            WO = 0.013
            UA = [-3.78, -3.6, -3.4, -3.0, -2.2, -1.4, -0.7, -0.3, -0.1, 0.10, 0.25, 0.35, 0.40, 0.42, 0.44, 0.455]
            UH = [-0.270, -0.262, -0.250, -0.216, -0.162, -0.104, -0.048, -0.013, -0.003, 0.019, 0.032, 0.049, 0.057, 0.080, 0.086, 0.090]
            UL = [-0.270, -0.282, -0.268, -0.244, -0.194, -0.146, -0.100, -0.079, -0.068, -0.058, -0.046, -0.016, 0.006, 0.029, 0.066, 0.090]
            LA = [-3.78, -3.6, -3.4, -3.0, -2.2, -1.4, -0.7, -0.3, -0.1, 0.10, 0.25, 0.35, 0.40, 0.42, 0.44, 0.46, 0.47]
            LH = [-0.272, -0.270, -0.272, -0.276, -0.258, -0.252, -0.248, -0.238, -0.226, -0.214, -0.213, -0.221, -0.239, -0.253, -0.268, -0.309, -0.330]
            LL = [-0.272, -0.292, -0.294, -0.304, -0.290, -0.296, -0.304, -0.291, -0.291, -0.292, -0.291, -0.287, -0.303, -0.306, -0.320, -0.322, -0.330]
            nav1 = ok & (A > UA[0]) & (A < UA[-1]) & (Z < np.interp(A, UA, UH)) & (Z > np.interp(A, UA, UL))
            nav2 = ok & (A > LA[0]) & (A < LA[-1]) & (Z < np.interp(A, LA, LH)) & (Z > np.interp(A, LA, LL))
            v.fill(ok & dil(nav1 | nav2, WO, v) & (A < XB(Z) - 0.004), WHITE)
            v.fill(nav1, NAVY); v.fill(nav2, NAVY)
            # nose bowl: white outline (0.014) then navy
            v.fill((A > XB(Z) - 0.014) & (Z > -0.95) & ~pant, WHITE); v.fill((A > XB(Z)) & (Z > -0.95) & ~pant, NAVY)
            # v4 fin / rudder border (Ty, red-tail-closeup): red edge, navy, white pinstripe, following the outline from
            # where the fin LE meets the fuselage, over the top, down the rudder TE and round the rudder bottom; it tapers
            # to a point at BOTH ends (fin root LE; rudder bottom-front at the hinge) and does not run along the fuselage
            # or up the hinge line to meet the cheat lines.
            pth = [(fle(z), z) for z in np.linspace(0.061, Z_FT, 60)] + [(rte(z), z) for z in np.linspace(Z_FT, -0.437, 90)][1:]
            pth = np.array(pth); seg = np.hypot(*np.diff(pth, axis=0).T); s_cum = np.concatenate([[0], np.cumsum(seg)]); S_TOT = s_cum[-1]
            fw = (A < H_T + 1.0) & (Z > -0.6); Af, Zf = A[fw], Z[fw]; dmin = np.full(Af.shape, 9.0); smin = np.zeros(Af.shape)
            for k in range(len(pth) - 1):
                (xa_, za_), (xb_, zb_) = pth[k], pth[k + 1]; ex, ez = xb_ - xa_, zb_ - za_; L2 = ex * ex + ez * ez
                t = np.clip(((Af - xa_) * ex + (Zf - za_) * ez) / max(L2, 1e-12), 0, 1)
                dd = np.hypot(Af - xa_ - t * ex, Zf - za_ - t * ez); b_ = dd < dmin
                dmin = np.where(b_, dd, dmin); smin = np.where(b_, s_cum[k] + t * seg[k], smin)
            gt = np.clip(np.minimum(smin / 0.30, (S_TOT - smin) / 0.45), 0, 1)       # taper lengths: fin root 0.30 m, rudder foot 0.45 m
            dist = np.full(A.shape, 9.0); dist[fw] = dmin; gg = np.zeros(A.shape); gg[fw] = gt
            w_e, w_n, w_w = 0.012 * gg, 0.05 * gg, 0.014 * gg
            v.fill(fin & (dist >= w_e) & (dist < w_e + w_n) & (gg > 0.02), NAVY)
            v.fill(fin & (dist >= w_e + w_n) & (dist < w_e + w_n + w_w) & (gg > 0.02), WHITE)
            # v5 pants (red-2-ramp / red-1-air): the navy nose runs aft as ONE long tapering navy wedge on the pant side:
            # its upper white edge sweeps from the top of the pant nose (du 0.39, dz 0.25) down-aft to a point at
            # (du -0.23, dz -0.015); its lower white edge runs from the nose bottom (du 0.45, dz -0.08) up and aft to
            # dz ~-0.02 and carries on as a white pinstripe almost to the pant tail. Red above and below.
            # v8 (red-2-ramp overlay): navy wedge ends ~0.13 m further forward (du -0.10) and is slimmer; the lower white
            # line sits ~80% down the nose (thin red bottom band) and runs aft into the up-swept tail tip.
            du, dz = A - X_AX, Z - Z_AX
            zu_ = np.interp(du, [-0.10, 0.02, 0.15, 0.30, 0.39, 0.47], [-0.065, 0.0, 0.05, 0.12, 0.20, 0.28])
            zl_ = np.interp(du, [-0.64, -0.40, -0.10, 0.17, 0.33, 0.45, 0.47], [0.150, 0.040, -0.060, -0.075, -0.090, -0.105, -0.11])
            pn = pant & (dz < zu_) & (dz > zl_) & (du > -0.10)
            v.fill(pant & dil(pn, 0.012, v) & (du > -0.12), WHITE)
            v.fill(pant & (np.abs(dz - zl_) < 0.0065 + 0.0) & (du > -0.63) & (du < 0.47), WHITE)   # pinstripe aft, runs into the up-swept tail tip
            v.fill(pn, NAVY)
        else:
            # v3 white-stripes, by eye from white-1/2/3: three bands red / light blue / navy with white separations,
            # laid out as level sets of the depth below one "upper envelope" (fuselage top line -> fin LE -> fin top ->
            # rudder TE), so they run cowl-to-tail and sweep up round the fin as concentric arcs (red outer, light blue
            # inner, a navy core filling the lower rudder), exactly as on white-3 / white-2.
            xa = A[0]; top1 = np.array([F_TOP(x) for x in xa])[None, :]; bot1 = np.array([F_BOT(x) for x in xa])[None, :]
            d = top1 - Z
            XR = float(fle(0.062))                                   # fin LE root on the fuselage top line
            # v4 (Ty: "the lines should be relatively straight along the side until they curve up at the vertical tail",
            # white-2 / white-3 / white-wings-2): depth is measured below ONE straight reference line from the cowl
            # (x 0.66, z 0.24) to the fin LE root, and only aft of x -3.2 does it become the distance to the fin
            # envelope (LE, top, rudder TE), so the bands run straight and parallel and sweep up only round the fin.
            ZR0, ZR1 = 0.24, 0.062
            Zr = lambda x: ZR0 + (ZR1 - ZR0) * (0.66 - x) / (0.66 - XR)
            d = Zr(A) - Z
            env = [(x, Zr(x)) for x in np.linspace(-2.4, XR, 12)] + [(fle(z), z) for z in np.linspace(0.062, Z_FT, 50)] \
                + [(rte(z), z) for z in np.linspace(Z_FT, -0.437, 70)]
            tw = xa < -2.5; At, Zt = A[:, tw], Z[:, tw]; dd = np.full(At.shape, 9.0)
            for (xa_, za_), (xb_, zb_) in zip(env[:-1], env[1:]):
                ex, ez = xb_ - xa_, zb_ - za_; L2 = ex * ex + ez * ez
                t = np.clip(((At - xa_) * ex + (Zt - za_) * ez) / max(L2, 1e-12), 0, 1)
                dd = np.minimum(dd, np.hypot(At - xa_ - t * ex, Zt - za_ - t * ez))
            d[:, tw] = dd
            xe = np.clip(xa, XR, 0.55)[None, :]; q = (0.55 - xe) / (0.55 - XR)            # 0 at the cowl, 1 at the fin root
            GAP_ = 0.03
            # v5 nose (Ty: "make the nose match the pictures exactly", white-3 / white-1 / white-2): light blue runs right up
            # onto the cowl top at the front (thin white above it), the red wedge starts further aft (x ~0.22, over the
            # cabane) and reaches full width by x ~-0.20.
            t0 = lerp(xe, [0.55, 0.20, XR], [0.02, 0.05, 0.035])                     # white above the red
            # v8 (Ty: cowl paint must cross the top like the Pitts; white-2 / white-3 show the light blue rising at the cowl
            # front and running over the top behind the white ring): ahead of x ~0.42 the white strip above the blue
            # vanishes, so the blue reaches the side/top seam; the top map (VFT) continues the same level sets across the
            # centreline, giving one continuous blue collar from side to side.
            t0 = t0 - 0.6 * sstep((A - 0.42) / 0.08)
            # v7 (white-3 nose zoom / white-2): the red wedge's front tip sits ~40 % of the way from the blue's front
            # edge back to the front cabane leg (x ~0.40, ahead of the strut, not over it); its lower edge drops in a
            # steep S (smoothstep) and reaches full width ~0.35 m further aft, then tapers to the fin as before.
            xr_ = A[0][None, :]
            wr = np.where(xr_ > 0.05, 0.13 * sstep((0.40 - xr_) / 0.35), lerp(xe, [0.05, XR], [0.13, 0.07]))
            fr = np.clip((A - (H_T - 0.15)) / 0.45, 0, 1)            # red arc thins out over the fin top toward the rudder
            t1 = t0 + wr * fr; t2 = t1 + GAP_ * np.clip(wr / 0.03, 0, 1)
            t3 = 0.41 + (0.235 - 0.41) * q; t4 = t3 + GAP_                          # light-blue bottom edge: straight
            t5 = 0.68 + (0.365 - 0.68) * q                                            # navy bottom edge: straight
            nb = d < t5
            # v5 nose: the navy's lower edge drops into a CHIN LOBE under the cowl (white-3: deepest just aft of the bowl,
            # sweeping back up to the straight band edge behind the firewall); white below.
            _zb = Zr(A) - t5
            # v7 (white-3 nose zoom, by the cowl / gear-fairing edges): the navy covers the WHOLE lower cowl side down to
            # the bottom line from its front edge (x ~0.70) back to x ~0.30; there its lower edge turns up in a quarter
            # ellipse (steep at the bottom, flattening) and joins the straight band edge at x ~0.08, ahead of the gear
            # fairing; white below aft of that.
            _s = np.clip((0.30 - A) / 0.22, 0, 1)
            _zl = np.where(A >= 0.30, -1.5, _zb - (_zb + 0.66) * np.sqrt(np.clip(1 - _s * _s, 0, 1)))
            nb = Z > np.where(A > 0.08, np.minimum(_zl, _zb), _zb)
            inF = fin & (Z > top1 - 0.005)                           # fin / rudder above the fuselage line
            aft = (A < H_T + 0.01) | inF                             # rudder + fin: no lower limit on the navy core
            core = (((A - (H_T - FCX)) / FCA) ** 2 + ((Z - FCZ) / FCB) ** 2) < 1.0   # navy core (white-3: dome, apex near hinge)
            ok = ~pant & (Z > -1.0)
            v.fill(ok & (d > t0) & (d < t1) & (fr > 0.02), RED)
            v.fill(ok & (d > t2) & ((d < t3) | inF), LBLUE)
            v.fill(ok & (d > t4) & (nb | aft) & ~inF, NAVY)
            v.fill(ok & aft & core & (d > t0) & ~erode(core, 0.022, v) & (d > t1 + 0.01), WHITE)   # white line round the core
            v.fill(ok & aft & erode(core, 0.022, v) & (d > t0), NAVY)
            v.fill(ok & ~(fin | nb) & (A < H_T + 0.40) & (Z < Z_S - 0.03), WHITE)   # white belly under the tail
            # v5 (Ty: the lower navy "grabbed onto the whole lower rudder"; white-3 fin crop / white-wings-2): below the
            # stab the rudder is WHITE; only the fuselage's navy band runs aft to the tail post (forward of the hinge),
            # with the white belly under it. v4 painted the whole fin/rudder below the stab navy.
            _ut = ok & (A < H_T + 0.40) & (A > H_T + 0.005) & (Z < Z_S - 0.03)
            # v7 (white-3 tail crop): ahead of the hinge the navy under the stab runs right down to the fuselage bottom
            # at the tail post (~0.29 m below the stab), not a thin strip; the rudder below the stab stays white.
            _ub = (Z_S - 0.32) + (Zr(A) - t5 - (Z_S - 0.32)) * sstep((A - H_T - 0.08) / 0.34)
            v.fill(_ut & (Z > _ub), NAVY); v.fill(_ut & (Z <= _ub), WHITE)
            v.fill(ok & fin & (A < H_T + 0.005) & (Z < Z_S - 0.01), WHITE)
            # v9 (Ty, overriding the photos -- the deflected-down elevator hides this area in white-1/2/3): the lower rudder
            # is NAVY below a boundary parallel to the lower edge of the navy fuselage/fin stripe, projected aft (_zb =
            # Zr - t5, the band's straight lower edge, continued across the rudder); navy from there down to the rudder
            # bottom and out to the TE. Replaces v5/v7's all-white lower rudder; above the boundary it stays white.
            v.fill(ok & fin & (A < H_T + 0.005) & (Z < Z_S - 0.01) & (Z < _zb), NAVY)
            # v7 (white-3 nose zoom, white-2, white-1): the white nose is only a RING behind the cowl lip. The light-blue
            # front edge runs parallel to the lip, ~6-7 cm behind it (x ~0.73 at the top, slanting aft to ~0.61 at the
            # blue's lower edge); the white widens into a crescent at the blue/navy pinstripe, where the navy's
            # front-top corner is rounded at x ~0.575; the navy front edge then sweeps FORWARD going down to x ~0.70 and
            # meets the cowl bottom line close behind the lip.
            # knots are in depth below the reference line (d = Zr - Z) so they follow the bands: blue 0.05..0.41,
            # pinstripe 0.41..0.44, navy below.
            _fd = [-0.30, 0.05, 0.15, 0.25, 0.33, 0.41, 0.44, 0.50, 0.58, 0.68, 0.80, 2.0]
            _fx = [0.745, 0.735, 0.718, 0.695, 0.665, 0.625, 0.575, 0.588, 0.622, 0.665, 0.695, 0.700]
            v.fill(ok & (A > np.interp(Zr(A) - Z, _fd, _fx)), WHITE)
            # pants (white-3): white nose cap; light-blue and red bands rise at the front and sweep aft along the side as
            # tapering lines; navy swoosh low at the front running aft below them; all converging on the pant tail.
            du, dz = A - X_AX, Z - Z_AX
            def band(pts, ws):
                pts = np.array(pts, float); dmin = np.full(A.shape, 9.0); wmin = np.zeros(A.shape)
                for (pa_, pb_), w0_, w1_ in zip(zip(pts[:-1], pts[1:]), ws[:-1], ws[1:]):
                    e = pb_ - pa_; L2 = float(e @ e); t = np.clip(((du - pa_[0]) * e[0] + (dz - pa_[1]) * e[1]) / L2, 0, 1)
                    dist = np.hypot(du - pa_[0] - t * e[0], dz - pa_[1] - t * e[1]); better = dist < dmin
                    dmin = np.where(better, dist, dmin); wmin = np.where(better, w0_ + (w1_ - w0_) * t, wmin)
                return pant & (dmin < 0.5 * wmin)
            v.fill(band([(0.30, 0.32), (0.29, 0.10), (0.24, 0.035), (0.12, 0.02), (-0.66, 0.065)], [0.075, 0.07, 0.04, 0.03, 0.014]), LBLUE)
            v.fill(band([(0.20, 0.32), (0.19, 0.15), (0.14, 0.09), (0.05, 0.08), (-0.66, 0.085)], [0.065, 0.06, 0.03, 0.022, 0.012]), RED)
            v.fill(band([(0.43, -0.20), (0.33, -0.055), (0.20, -0.03), (-0.66, 0.05)], [0.07, 0.06, 0.035, 0.012]), NAVY)
        return v
    VL, VR = side('gl_side_l', True), side('gl_side_r', False)
    VFT, Y, X = plan('gl_fus_top', True, (-0.5, 0.5), (X0, X1), 1024, 4096); VFT.fill(np.ones_like(Y, bool), BASE)
    VFB, Y, X = plan('gl_fus_bot', False, (-0.5, 0.5), (X0, X1), 1024, 4096); VFB.fill(np.ones_like(Y, bool), BASE)
    if LIV == 'white-stripes':
        # v7: the navy chin wraps under the cowl (side views show it reaching the bottom line); white ring at the lip.
        VFB.fill((VFB.B > 0.30) & (VFB.B < 0.685), NAVY)
    if LIV == 'white-stripes':
        # v8 cowl wrap: evaluate the side bands' level sets on the real top surface (z = fus_z) so the light-blue collar
        # and white ring run continuously over the top centreline into the other side's bands (symmetric in |y|).
        _XR = float(fle(0.062)); _Zr = lambda x: 0.24 + (0.062 - 0.24) * (0.66 - x) / (0.66 - _XR)
        _zz = np.vectorize(lambda x, y: fus_z(x, y, True))(VFT.B, VFT.A); _B = VFT.B; _d = _Zr(_B) - _zz
        _xe = np.clip(_B, _XR, 0.55); _q = (0.55 - _xe) / (0.55 - _XR)
        _t0 = lerp(_xe, [0.55, 0.20, _XR], [0.02, 0.05, 0.035]) - 0.6 * sstep((_B - 0.42) / 0.08)
        _wr = np.where(_B > 0.05, 0.13 * sstep((0.40 - _B) / 0.35), 0.13)
        _t1 = _t0 + _wr; _t2 = _t1 + 0.03 * np.clip(_wr / 0.03, 0, 1); _t3 = 0.41 + (0.235 - 0.41) * _q
        _fr = (_B > 0.0)
        VFT.fill(_fr & (_d > _t0) & (_d < _t1), RED)
        VFT.fill(_fr & (_d > _t2) & (_d < _t3), LBLUE)
        _fd = [-0.30, 0.05, 0.15, 0.25, 0.33, 0.41, 0.44, 0.50, 0.58, 0.68, 0.80, 2.0]
        _fx = [0.745, 0.735, 0.718, 0.695, 0.665, 0.625, 0.575, 0.588, 0.622, 0.665, 0.695, 0.700]
        VFT.fill(_B > np.interp(_d, _fd, _fx), WHITE)
    if LIV == 'red-sunburst':
        for VF, up in ((VFT, True), (VFB, False)):
            _zz = np.vectorize(lambda x, y: fus_z(x, y, up))(VF.B, VF.A)        # v5: same chevron bowl edge XB as the sides
            VF.fill(VF.B > XB(_zz) - 0.014, WHITE)
            VF.fill(VF.B > XB(_zz), NAVY)
    def surface(v, names, x0, rays, y0=0.0):
        m = sil(v, names); Y_, X_ = v.A, v.B
        if LIV == 'red-sunburst':
            # v3: alternating red / navy wedges split by white pinstripes, fanning from a point on the LEADING edge
            # (x0, |y| = y0) so every ray sweeps AFT as it runs outboard (red-1-air / red-3-tail); rays = (boundary
            # angles from straight aft, deg), wedge colours root -> tip). Same plan map on the top and bottom skins.
            bnd, cols = rays; dy, dx = np.abs(Y_) - y0, x0 - X_
            ph = np.degrees(np.arctan2(dy, dx)); r = np.hypot(dy, dx); k = np.searchsorted(bnd, ph)
            for i, c in enumerate(cols): v.fill(m & (k == i), c)
            for b in bnd: v.fill(m & (r * np.abs(np.sin(np.radians(ph - b))) < 0.011) & (np.abs(ph - b) < 90), WHITE)
            if v.kind in ('lower', 'upper'):
                pass
            elif v.kind == 'stab':   # v4 (red-tail-closeup): no navy rim or navy tip on the tail, just a white pinstripe edge
                bands(v, m, [(0.012, WHITE)])
            else:
                bands(v, m, [(0.035, NAVY), (0.012, WHITE)])
                v.fill(m & (np.abs(Y_) > np.abs(Y_).max() * 0 + v.tipcut), NAVY)
        else:
            # v4 white-stripes (Ty via white-wings-1 / white-wings-2, replacing v3's red tops and LE bands): every wing
            # and tail skin, top and bottom, is WHITE; each tip carries a RED cap with a raked inboard edge, then (white
            # separators) a LIGHT-BLUE and a NAVY band parallel to it, raked so they run aft-outboard. Band = level set of
            # u = |y| - k * (distance aft of the local LE); widths read off white-wings-2 at ~10 mm/px of span.
            v.fill(m, WHITE)
            le = np.where(m.any(0), np.where(m, X_, -99).max(0), -99.0)          # LE x per spanwise column
            ya = np.abs(Y_[0]); yc = v.tipcut - 0.25; jc = np.argmin(np.abs(ya - yc) + (le < -50) * 99)
            le = np.where(ya > yc, le[jc], le)[None, :]; dle = le - X_
            # v5 (Ty: "several parallel stripes running the span along the leading edge then they turn and angle back at
            # the tips", white-wings-1/-2, white-3): nested L bands. Each band has a spanwise leg parallel to the LE
            # (level set of dle) and a tip leg raked aft-outboard as in v4 (level set of u); colour = the OUTER of the
            # two, so the legs meet in a mitred corner. Order from the LE / tip inward: red, white, light blue, white,
            # navy, white. Same plan map on top and bottom skins.
            if v.kind == 'stab':
                u = np.abs(Y_) - 0.42 * (float(SLE(1.2)) - X_); RB0 = 1.10
                TE_, LE_ = [0.0, 0.04, 0.22, 0.26, 0.44], [0.06, 0.085, 0.15, 0.175, 0.235]
            else:
                u = np.abs(Y_) - 0.12 * dle; RB0 = 3.55
                TE_, LE_ = [0.0, 0.06, 0.34, 0.40, 0.65], [0.11, 0.14, 0.24, 0.27, 0.36]
            idx = np.minimum(np.searchsorted(TE_, RB0 - u), np.searchsorted(LE_, dle))
            for i_, c_ in enumerate([RED, WHITE, LBLUE, WHITE, NAVY]): v.fill(m & (idx == i_), c_)
    WRAYS = ([13, 27, 41, 54, 66, 76, 84], [RED, NAVY, RED, NAVY, RED, NAVY, RED, NAVY])
    VUW, Y, X = plan('gl_wing_u', True, (-4.1, 4.1), (-1.75, 0.05), 4096, 1024); VUW.tipcut = S_W - 0.16; VUW.kind = 'upper'
    # v8 (red-1-air zoom of the upper-wing underside, red-3-tail starboard underside): same layout as the lower wing --
    # five rays from the LE on the centreline, sectors root -> LE red, navy, red, navy, red (big, to the tip TE), navy
    # (LE band out to the tip); no navy rim / tip cap. The top skin is not visible in any red photo: same plan map.
    _ur = [36.5, 48.5, 56, 68, 78]
    surface(VUW, ['Wing_Upper'], float(LEU0(0.0)), (_ur, [RED, NAVY, RED, NAVY, RED, NAVY]))
    VLW, Y, X = plan('gl_wing_l', True, (-4.1, 4.1), (-1.85, -0.60), 4096, 768); VLW.tipcut = S_W - 0.16; VLW.kind = 'lower'
    # v8 (red-3-tail back-projected onto this plan map through a re-solved PnP camera, err 5.5 px): the lower-wing rays
    # fan from the root LE on the centreline; boundaries 36.5 / 48.5 / 56 / 68 / 78 deg from aft; sectors root -> LE:
    # red, navy, red, navy (big, to the TE), red (big, over the tip TE), navy (LE band out to the tip). No navy rim and
    # no navy tip cap: the tip is navy forward / red aft as the sectors fall.
    surface(VLW, ['Wing_Lower', 'Aileron_LL', 'Aileron_LR'], LE_L, ([36.5, 48.5, 56, 68, 78], [RED, NAVY, RED, NAVY, RED, NAVY]), y0=0.0)
    VST, Y, X = plan('gl_stab', True, (-1.6, 1.6), (-5.15, -3.85), 2048, 1024)
    _sv = np.array([x.co[:] for n in ('Stab', 'Elevator_L', 'Elevator_R') for x in P[n].data.vertices])
    VST.tipcut = np.abs(_sv[:, 1]).max() - 0.10; VST.kind = 'stab'
    if LIV == 'white-stripes': VST.tipcut = 1.12
    # v4 (red-tail-closeup): rays fan from the root LE beside the fuselage (|y| 0.16): navy root sector, red, a big navy
    # outboard sector over the tip, and a red sector along the LE; white pinstripe separators and edge
    surface(VST, ['Stab', 'Elevator_L', 'Elevator_R'], float(SLE(0.16)) - 0.02, ([12, 30, 64], [NAVY, RED, NAVY, RED]), y0=0.16)

M = {}
for v in (VL, VR, VFT, VFB, VUW, VLW, VST): M[v.name] = paint.mat_image('Paint_' + v.name, v.to_image(os.path.join(TEX, v.name + '.png')))
M['red'] = paint.mat_colour('Paint_red', RED); M['white'] = paint.mat_colour('Paint_white', WHITE)
M['steel'] = paint.mat_colour('Wire_steel', STEEL, rough=0.25, metal=0.9)
M['tyre'] = paint.mat_colour('Paint_tyre', TYRE, rough=0.8); M['cabin'] = paint.mat_colour('Paint_cabin', CABIN, rough=0.85)
M['glass'] = paint.mat_glass('Glass', col=(120, 150, 185), alpha=0.35)
M['frame'] = paint.mat_colour('Windscreen_frame', (40, 40, 44), rough=0.5)
M['prop_spinner'] = paint.mat_colour('Prop_spinner', RED) if LIV == 'v1' else paint.mat_colour('Prop_spinner_chrome', (225, 228, 232), rough=0.12, metal=1.0)  # v6: red-2-ramp shows a polished spinner too
M['base'] = paint.mat_colour('Paint_base', WHITE if LIV != 'red-sunburst' else RED)
M['strut'] = M['red'] if LIV in ('v1', 'red-sunburst') else M['white']
M['gear'] = M['red'] if LIV == 'red-sunburst' else M['white']
M['prop_blade'] = paint.mat_colour('Prop_blade', BLADE, rough=0.45, metal=0.3); M['prop_tip'] = paint.mat_colour('Prop_blade_tip', WHITE, rough=0.4)
M['exhaust'] = paint.mat_colour('Exhaust_steel', (120, 112, 104), rough=0.5, metal=0.6)
def setm(o, names):
    o.data.materials.clear()
    for n in names: o.data.materials.append(M[n])
for n, V_ in (('Wing_Upper', VUW), ('Wing_Lower', VLW), ('Aileron_LL', VLW), ('Aileron_LR', VLW), ('Stab', VST), ('Elevator_L', VST), ('Elevator_R', VST)):
    setm(P[n], [V_.name]); paint.project(P[n], lambda nrm, c, V_=V_: (0, V_))   # tips are symmetric in y: same map top and bottom
def fus_assign(nrm, c):
    if c.z < 0.215 and any(xa - 0.01 < c.x < xb + 0.01 for xb, xa in ((XC1F, XC1A), (XC2F, XC2A))):
        if abs(nrm.x) > 0.85 or nrm.y * c.y + nrm.z * (c.z + 0.2) < 0: return (3, None)   # cockpit inner skin + bulkheads
    if nrm.z <= -0.7: return (4, VFB)
    if nrm.z >= 0.7: return (2, VFT)
    return (0, VL) if nrm.y >= 0 else (1, VR)
setm(P['Fuselage'], ['gl_side_l', 'gl_side_r', 'gl_fus_top', 'cabin', 'gl_fus_bot']); paint.project(P['Fuselage'], fus_assign)
for n in ('Wheel_Pant_L', 'Wheel_Pant_R', 'Fin', 'Rudder'):
    setm(P[n], ['gl_side_l', 'gl_side_r', 'base'])
    paint.project(P[n], lambda nrm, c: (2, None) if nrm.z <= -0.6 else ((0, VL) if nrm.y >= 0 else (1, VR)))
for n, m in (('Headrest', 'base'), ('Windscreen_Glass', 'glass'), ('Windscreen_Frame', 'frame'), ('Spinner', 'prop_spinner'),
             ('Gear_Main', 'gear'), ('Gear_Axle_L', 'gear'), ('Gear_Axle_R', 'gear'), ('Oleo_Cylinder_L', 'gear'), ('Oleo_Cylinder_R', 'gear'),
             ('Oleo_Shaft_L', 'steel'), ('Oleo_Shaft_R', 'steel'), ('Wheel_L', 'tyre'), ('Wheel_R', 'tyre'), ('Wheel_Tail', 'tyre'), ('Strut_Cabane', 'strut'), ('Strut_Interplane', 'strut'), ('Strut_Stab', 'strut'),
             ('Wire_Bracing', 'steel'), ('Exhaust', 'exhaust')):
    setm(P[n], [m]); [setattr(p, 'material_index', 0) for p in P[n].data.polygons]
setm(P['Prop'], ['prop_blade', 'prop_tip'])
for p, t in zip(P['Prop'].data.polygons, face_tag): p.material_index = t
print('PROP faces', len(face_tag), 'tip', face_tag.count(1))
M['en_cyl'] = paint.mat_colour('Engine_cylinder', (128, 132, 138), rough=0.55, metal=0.3)
M['en_core'] = paint.mat_colour('Engine_cylinder_head', (74, 77, 82), rough=0.7, metal=0.2)
M['en_case'] = paint.mat_colour('Engine_case', (24, 24, 27), rough=0.6)
M['en_rocker'] = paint.mat_colour('Engine_rocker_cover', (150, 152, 156), rough=0.5, metal=0.4)
M['en_dark'] = paint.mat_colour('Engine_duct', (30, 31, 34), rough=0.9)
setm(P['Engine'], EN_T)
for p, t in zip(P['Engine'].data.polygons, en_tag): p.material_index = t
M['ck_seat'] = paint.mat_colour('Cockpit_seat', (66, 70, 78), rough=0.75)
M['ck_strap'] = paint.mat_colour('Cockpit_harness', (150, 138, 112), rough=0.85)
M['ck_panel'] = paint.mat_colour('Cockpit_panel', (40, 42, 46), rough=0.6)
M['ck_gauge'] = paint.mat_colour('Cockpit_gauge_bezel', (150, 154, 160), rough=0.4, metal=0.5)
M['ck_face'] = paint.mat_colour('Cockpit_gauge_face', (176, 182, 188), rough=0.3)
M['ck_stick'] = paint.mat_colour('Cockpit_stick', (42, 42, 46), rough=0.5)
M['ck_tube'] = paint.mat_colour('Cockpit_tube', (200, 202, 205), rough=0.5)
M['ck_tub'] = M['cabin']; M['ck_black'] = paint.mat_colour('Cockpit_dash_top', (39, 39, 40), rough=0.9)
setm(P['Cockpit'], CK_T)
for _n in [t for t in CK_T if t != 'ck_tub']:
    for _nd in M[_n].node_tree.nodes:
        if _nd.type == 'BSDF_PRINCIPLED':
            _nd.inputs['Roughness'].default_value = 0.9; _nd.inputs['Metallic'].default_value = 0.0
            for _k in ('Specular IOR Level', 'Specular'):
                if _k in _nd.inputs: _nd.inputs[_k].default_value = 0.0
for p, t in zip(P['Cockpit'].data.polygons, ck_tag): p.material_index = t
for o in P.values():
    bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(o.data); bm.free()
    for p in o.data.polygons: p.use_smooth = True
    try: o.data.set_sharp_from_angle(angle=math.radians(50))
    except Exception: pass
P['Fuselage'].name = 'Fuselage_Painted'
# ---- gear travel: hinge angle for a given strut compression, and the 3-point height at each state -----------------
MOV = {s: ['Gear_Axle_' + s, 'Wheel_' + s, 'Wheel_Pant_' + s, 'Oleo_Cylinder_' + s] for s in ('L', 'R')}
_V = {n: np.array([v.co[:] for v in o.data.vertices]) for n, o in P.items()}
def _posed(th):
    out = {}
    for n, a in _V.items():
        side = next((s for s in MOV if n in MOV[s]), None)
        if side is None: out[n] = a; continue
        m = np.array((1, 1 if side == 'L' else -1, 1.0)); b = a * m
        out[n] = np.array([g_rot(p, _s * th) for p in b]) * m
    return out
def _three_point(th):
    V = _posed(th); allv = np.concatenate(list(V.values()))
    mw = np.concatenate([V['Wheel_L'], V['Wheel_R']]); tw = V['Wheel_Tail']
    def rot(a, ph):  # nose-up pitch ph about +y (nose is +x)
        c, s = math.cos(ph), math.sin(ph); return np.stack([a[:, 0] * c - a[:, 2] * s, a[:, 1], a[:, 0] * s + a[:, 2] * c], 1)
    lo, hi = -0.1, 0.5
    for _ in range(60):
        ph = 0.5 * (lo + hi)
        if rot(mw, ph)[:, 2].min() < rot(tw, ph)[:, 2].min(): lo = ph   # tail still high -> more nose-up
        else: hi = ph
    r = rot(allv, ph); g = rot(mw, ph)[:, 2].min()
    return float(r[:, 2].max() - g), math.degrees(ph)
H_PUB = 7 * 0.3048 + 4 * IN                           # 7 ft 4 in (Waco Classic spec sheet)
GEAR_STATES = {}
for nm, th in (('extended', 0.0), ('full', TH_FULL)):
    h, ph = _three_point(th); GEAR_STATES[nm] = (th, h, ph)
lo, hi = 0.0, TH_FULL
if GEAR_STATES['extended'][1] > H_PUB > GEAR_STATES['full'][1]:
    for _ in range(50):
        mid = 0.5 * (lo + hi)
        if _three_point(mid)[0] > H_PUB: lo = mid
        else: hi = mid
    th_st = 0.5 * (lo + hi); GEAR_STATES['static'] = (th_st, *_three_point(th_st))
for nm, (th, h, ph) in GEAR_STATES.items():
    print('GEARSTATE %-8s hinge %5.2f deg  strut %.3f m (%.2f in compressed)  wheel z %+.3f  3pt height %.3f m (%.1f in) pitch %.2f'
          % (nm, math.degrees(th), g_len(th), (L_EXT - g_len(th)) / IN, g_wheel(th)[2], h, h / IN, ph))
print('PUBLISHED height %.3f m' % H_PUB)
allv = np.concatenate([np.array([v.co[:] for v in o.data.vertices]) for o in P.values()])
print('BOUNDS', allv.min(0), allv.max(0), 'size', allv.max(0) - allv.min(0), 'parts', sorted(P))
print('TRIS', sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in P.values()))
for o in P.values(): o.data.transform(Matrix.Rotation(math.pi, 4, 'Z'))   # nose +x after glTF import, as the EA300
os.makedirs(os.path.dirname(OUT), exist_ok=True)
# rig: per side an empty 'Gear_Pivot_<s>' at the axle hinge H (parent of axle assy, wheel, pant, oleo cylinder) and
# marker empties 'Gear_HingeAxis_<s>' at C (hinge axis = Pivot -> HingeAxis) and 'Gear_OleoTop_<s>' at T (where the
# fixed Oleo_Shaft_<s> pivots; the cylinder's origin sits on the bottom pin A and its +axis points at T).
RZ = Matrix.Rotation(math.pi, 4, 'Z'); EXTRA = []
def _empty(name, loc):
    e = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(e); e.location = loc; EXTRA.append(e); return e
for side, d in GEAR_SIDES.items():
    w = lambda p: RZ @ Vector(tuple(float(c) for c in p))
    piv = _empty('Gear_Pivot_' + side, w(d['H']))
    _empty('Gear_HingeAxis_' + side, w(d['C'])); _empty('Gear_OleoTop_' + side, w(d['T']))
    piv['stroke_m'] = round(STROKE, 4); piv['strut_len_extended_m'] = round(L_EXT, 4)
    piv['hinge_deg_full'] = round(math.degrees(TH_FULL), 3)
    if 'static' in GEAR_STATES: piv['hinge_deg_static'] = round(math.degrees(GEAR_STATES['static'][0]), 3)
    piv['wheel_up_sign_model_frame'] = int(_s * (1 if side == 'L' else -1))
    for n in MOV[side]:
        o = P[n]; org = w(d['A']) if n.startswith('Oleo_Cylinder') else piv.location
        o.data.transform(Matrix.Translation(-org)); o.parent = piv; o.location = org - piv.location
    o = P['Oleo_Shaft_' + side]; org = w(d['T']); o.data.transform(Matrix.Translation(-org)); o.location = org
bpy.ops.object.select_all(action='DESELECT')
for o in list(P.values()) + EXTRA: o.select_set(True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_yup=True, export_apply=True, export_extras=True)
print('WROTE', OUT, os.path.getsize(OUT))
