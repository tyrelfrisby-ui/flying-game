"""Our own Great Lakes 2T-1A-2 style biplane (Aero Playground).   v2

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

Liveries (GL_LIVERY): v1 = the original white placeholder (registration N719GL); red-sunburst and white-stripes = Ty's
two schemes, painted by eye from his photos, no registration, no logos or text.

Frame: metres, nose +X, port +Y, up +Z; origin = upper-wing centre-section LE station on the thrust line (yawed 180 deg
at export, like the Pitts / EA300).
  blender -b --python tools/own_models/build_great_lakes.py
"""
import bpy, bmesh, sys, os, math, numpy as np
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
F_TOP = tbl([(0.66, 0.21), (0.645, 0.226), (0.63, 0.235), (0.57, 0.245), (0.47, 0.25), (-0.17, 0.25), (-0.40, 0.235),
             (-0.70, 0.215), (-0.95, 0.20), (-2.03, 0.20), (-2.30, 0.18), (-3.0, 0.13), (-3.8, 0.06), (-4.3, 0.0), (-4.53, -0.04)])
F_BOT = tbl([(0.66, -0.42), (0.645, -0.465), (0.63, -0.50), (0.57, -0.56), (0.47, -0.61), (0.30, -0.67), (0.10, -0.73),
             (-0.17, -0.79), (-0.40, -0.81), (-0.70, -0.82), (-1.80, -0.82), (-4.53, -0.46)])
F_HW = tbl([(0.66, 0.33), (0.645, 0.36), (0.63, 0.38), (0.57, 0.41), (0.47, 0.435), (0.30, 0.445), (0.10, 0.445),
            (-0.17, 0.43), (-0.40, 0.405), (-0.70, 0.40), (-2.03, 0.40), (-2.30, 0.385), (-3.77, 0.20), (-4.30, 0.085),
            (-4.53, 0.02)])
SILL = 0.12
XC1F, XC1A, XC2F, XC2A = -0.95, -1.28, -1.48, -2.03      # front / rear cockpit openings (top view)
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
def sill_w(x):
    top, bot, hw = F_TOP(x), F_BOT(x), F_HW(x)
    return abs(se_pt(t_at(SILL, top, bot), top, bot, hw)[0]), SILL
def fus_z(x, y, up=True):
    """fuselage surface height at station x, half-breadth y"""
    top, bot, hw = F_TOP(x), F_BOT(x), F_HW(x); zw = bot + ZWF * (top - bot); r = min(abs(y) / hw, 1.0)
    return zw + (top - zw) * (1 - r ** NT) ** (1 / NT) if up else zw - (zw - bot) * (1 - r ** NB) ** (1 / NB)
X_FACE = 0.66
SEG_FWD = [0.66, 0.655, 0.645, 0.63, 0.60, 0.57, 0.52, 0.47, 0.40, 0.30, 0.10, -0.17, -0.40, -0.70, -0.85, XC1F]
SEG_U1 = list(np.linspace(XC1F, XC1A, 6))
SEG_BR = [XC1A, -1.38, XC2F]
SEG_U2 = list(np.linspace(XC2F, XC2A, 7))
SEG_AFT = [XC2A, -2.30, -2.70, -3.10, -3.50, -3.80, -4.10, -4.30, -4.45, -4.53]
bm = bmesh.new()
fv, _ = loft_rings(bm, [ring_closed(*stn(x)) for x in SEG_FWD], cap0=False, cap1=True)
loft_rings(bm, [ring_u(*stn(x), SILL) for x in SEG_U1])
loft_rings(bm, [ring_closed(*stn(x)) for x in SEG_BR])
loft_rings(bm, [ring_u(*stn(x), SILL) for x in SEG_U2])
loft_rings(bm, [ring_closed(*stn(x)) for x in SEG_AFT])
# cowl front face: two round cooling inlets beside the spinner, as real holes
INL = dict(y=0.25, z=0.0, a=0.055, b=0.075)
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
HR_X0, HR_X1, HR_Z0, HR_Z1 = -2.03, -3.75, 0.345, 0.07
bm = bmesh.new(); rs = []
for k, x in enumerate([-2.025, -2.032, -2.045, -2.07, -2.15, -2.40, -2.70, -3.00, -3.30, -3.55, -3.75]):
    f = (HR_X0 - x) / (HR_X0 - HR_X1); hw = 0.115 - 0.085 * f; top = HR_Z0 + (HR_Z1 - HR_Z0) * f
    if k < 3: hw *= (0.55, 0.82, 0.95)[k]; top -= (0.05, 0.015, 0.004)[k]
    base = fus_z(x, hw) - 0.02
    ring = []
    for t in np.linspace(0, math.pi, 17):
        c, s = math.cos(t), math.sin(t)
        ring.append((x, hw * math.copysign(abs(c) ** (2 / 2.6), c), base + (top - base) * abs(s) ** (2 / 2.6)))
    rs.append(ring)
loft_rings(bm, rs, closed=False); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Headrest'] = link('Headrest', bm)

# ---- windscreens: one small curved screen ahead of each cockpit --------------------------------------------------
def screen(xb, hw=0.23, h=0.17, rake=38, n=13):
    rows = []
    for k, q in enumerate(np.linspace(0, 1, 5)):
        row = []
        for y in np.linspace(-hw, hw, n):
            w = hw * (1 - 0.18 * q); yy = y * w / hw
            zb = fus_z(xb, yy) - 0.004
            z = zb + h * q * (1 - 0.35 * (yy / hw) ** 2)
            row.append((xb - h * q * math.tan(math.radians(rake)), yy, z))
        rows.append(row)
    return rows
bm_g = bmesh.new(); bm_f = bmesh.new()
for xb in (-0.86, -1.35):
    rows = screen(xb); loft_rings(bm_g, rows, False, False, closed=False)
    tube(bm_f, rows[-1], 0.008, 6); tube(bm_f, [r[0] for r in rows], 0.008, 6); tube(bm_f, [r[-1] for r in rows], 0.008, 6)
P['Windscreen_Glass'] = link('Windscreen_Glass', bm_g)
for sy in (1, -1):   # cockpit coaming rails round both openings
    for xa, xb in ((XC1F, XC1A), (XC2F, XC2A)):
        tube(bm_f, [(x, sy * (sill_w(x)[0] - 0.004), SILL + 0.006) for x in np.linspace(xa, xb, 8)], 0.013, 8)
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
ck_prism('ck_panel', [(-0.30, -0.10), (0.30, -0.10), (0.33, 0.08), (0.20, 0.14), (-0.20, 0.14), (-0.33, 0.08)], XC1F + 0.008, 0.012)
for y in (-0.16, -0.05, 0.05, 0.16): ck_gauge(XC1F + 0.002, y, 0.03, 0.034)
ck_seat(-0.98, ZFL + 0.08, 0.06, 0.22, pan_l=0.30)
ck_stick(-0.90, ZFL, 0.55)
# rear (PIC) cockpit: panel on the bulkhead behind the front seat, seat, stick
ck_prism('ck_panel', [(-0.32, -0.12), (0.32, -0.12), (0.34, 0.10), (0.22, 0.15), (-0.22, 0.15), (-0.34, 0.10)], XC2F + 0.008, 0.015)
ck_box('ck_black', (XC2F + 0.02, 0, 0.152), (0.06, 0.56, 0.012))
for y in (-0.20, -0.07, 0.07, 0.20): ck_gauge(XC2F + 0.002, y, 0.06, 0.034)
for y in (-0.13, 0.0, 0.13): ck_gauge(XC2F + 0.002, y, -0.04, 0.030)
ck_seat(-1.66, ZFL + 0.08, 0.10, 0.22, pan_l=0.32)
ck_stick(-1.58, ZFL, 0.56)
for sy in (1, -1):                                                                                     # steel-tube longerons + diagonals
    p = lambda x, dz=0.0: (x, sy * (sill_w(x)[0] - 0.035), SILL - 0.025 + dz)
    for xa, xb in ((XC1F, XC1A), (XC2F, XC2A)):
        ck_cyl('ck_tube', p(xa - 0.01), p(xb + 0.01), 0.011, 8)
        ck_cyl('ck_tube', p(xa - 0.01), (xb + 0.01, sy * (sill_w(xb)[0] - 0.05), ZFL + 0.05), 0.010, 8)
for x0 in (-0.80, -1.45):                                                                              # rudder pedals (under the decks)
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
H_T, Z_S = -4.565, -0.14
STAB_T = [(0.0, .67, .10), (0.07, .67, .126), (0.155, .67, .33), (0.30, .664, .47), (0.50, .621, .53), (0.73, .564, .53),
          (0.96, .477, .47), (1.19, .362, .385), (1.35, .27, .30), (1.449, .19, .213), (1.50, .10, .11), (1.535, 0.0, 0.0)]
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
E_IN, E_PHI = 0.06, 38.0
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
# fin + rudder ("big tail"): fin LE from the three-view, rudder outline from the G-GLII side photo, hinge-referenced
FLE = [(-0.40, 1.02), (0.07, 1.02), (0.12, 0.92), (0.20, 0.80), (0.322, 0.65), (0.473, 0.517), (0.645, 0.388), (0.774, 0.28),
       (0.893, 0.151), (0.957, 0.03), (0.965, 0.0)]
RTE = [(-0.46, 0.0), (-0.456, 0.129), (-0.412, 0.259), (-0.326, 0.367), (-0.196, 0.453), (-0.067, 0.507), (0.105, 0.543),
       (0.278, 0.550), (0.450, 0.528), (0.601, 0.485), (0.752, 0.410), (0.860, 0.302), (0.935, 0.151), (0.957, 0.0)]
Z_FT = 0.957
fle = lambda z: H_T + float(np.interp(z, *zip(*FLE))); rte = lambda z: H_T - float(np.interp(z, *zip(*RTE)))
FN = Surf(fle, rte, lambda z: 0.0, 0.065, axis='z')
R_POST = lambda z: float(yt((fle(z) - H_T) / (fle(z) - rte(z)), 0.065) * (fle(z) - rte(z)))
P['Fin'] = surf_obj('Fin', FN, tipL(-0.06, Z_FT - 0.004, 16), x_to=lambda s: H_T + R_POST(s) + 0.003)
rz = [-0.455 + (Z_FT - 0.003 + 0.455) * (1 - math.cos(math.pi * t)) / 2 for t in np.linspace(0, 1, 30)]
def rudder_sec(z, nn=9):
    sec = FN.section(z, x_from=H_T); rho = max(abs(sec[0][1]), 0.004)
    return sec + [(H_T + rho * math.cos(t), rho * math.sin(t), z) for t in np.linspace(-math.pi / 2, math.pi / 2, nn + 2)[1:-1]]
bm = bmesh.new(); loft_rings(bm, [rudder_sec(z) for z in rz]); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
P['Rudder'] = link('Rudder', bm)

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
PANT = [(0.46, 0, .01, .01, .01), (0.42, 0, .11, .10, .083), (0.31, 0, .20, .165, .12), (0.13, 0, .26, .165, .13),
        (0, 0, .27, .165, .13), (-0.17, .02, .245, .155, .12), (-0.355, .045, .18, .12, .094), (-0.53, .065, .09, .055, .05),
        (-0.64, .075, .01, .01, .01)]          # (dx from the axle, dz centre, top half-height, bottom half-height, half-width)
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
SPB, SPX, SPR = 0.675, 1.10, 0.185
bm = bmesh.new(); ts = np.linspace(0.97, 0.0, 12)
rs = [[bm.verts.new((SPB + (SPX - SPB) * t, SPR * (1 - t ** 2) ** 0.6 * math.sin(a), SPR * (1 - t ** 2) ** 0.6 * math.cos(a))) for a in np.linspace(0, 2 * math.pi, 28, endpoint=False)] for t in ts]
tipv = bm.verts.new((SPX, 0, 0))
for i in range(28): j = (i + 1) % 28; bm.faces.new((tipv, rs[0][i], rs[0][j]))
for a_, b2 in zip(rs, rs[1:]):
    for i in range(28): j = (i + 1) % 28; bm.faces.new((a_[i], b2[i], b2[j], a_[j]))
bm.faces.new(rs[-1]); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Spinner'] = link('Spinner', bm)
PROP_X, R_P, PITCH, NBL, TIP = 0.75, 37 * IN, 1.7, 2, 4 * IN
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
    X_TP = -4.53                                                    # tail post
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
    # ---- liveries (v2): Ty's two schemes, from his photos by eye (no tracing, no sampling); no registration, no text/logos.
    #  red-sunburst : all red; navy nose bowl; two navy, white-outlined fuselage stripes nose to tail; red/white/navy sunburst
    #                 rays on both wings (top and bottom) and the stab; navy tips/outline; navy fin/rudder border; red pants
    #                 with a navy nose and stripe; red struts and gear; white spinner.
    #  white-stripes: white; swooping red / light-blue / navy fuselage stripes cowl to tail; red edge trim with light-blue and
    #                 navy pinstripes on wings and stab; light-blue + navy fin/rudder with a red border; striped pants;
    #                 white struts and gear; chrome spinner.
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
    X_TP = -4.53
    def XN(z): return 0.36 - 0.06 * (z - 0.25)                  # aft edge of the nose bowl
    def lerp(x, xs, zs): return np.interp(x, xs[::-1], zs[::-1]) if xs[0] > xs[-1] else np.interp(x, xs, zs)
    BASE = RED if LIV == 'red-sunburst' else WHITE
    PANT_Z = Z_AX + 0.0
    def side(name, flip):
        v = View(name, (X0, X1), (Z0, Z1), 4096, 1748, flip_a=flip, axes=(0, 2)); A, Z = v.A, v.B
        v.fill(np.ones_like(A, bool), BASE)
        fin = close(sil(v, ['Fin', 'Rudder']), 0.03, v); ext = fin | ((Z < 0.30) & (A > X_TP - 0.7) & (A < X_TP + 1.4))
        pant = (Z < -1.15)
        if LIV == 'red-sunburst':
            z1 = lerp(A, [0.70, -0.5, -4.5], [0.02, -0.03, -0.13]); w1 = lerp(A, [0.70, -4.5], [0.075, 0.035])
            z2 = z1 - lerp(A, [0.70, -4.5], [0.13, 0.055]); w2 = lerp(A, [0.70, -4.5], [0.04, 0.02])
            ok = (A < XN(Z) + 0.01) & (A > X_TP - 0.05) & ~fin & ~pant
            for zc, wc in ((z1, w1), (z2, w2)):
                v.fill(ok & (np.abs(Z - zc) < 0.5 * wc + 0.014), WHITE); v.fill(ok & (np.abs(Z - zc) < 0.5 * wc), NAVY)
            v.fill((A > XN(Z) - 0.02) & (Z > -0.95) & ~pant, WHITE); v.fill((A > XN(Z)) & (Z > -0.95) & ~pant, NAVY)   # nose bowl
            bands(v, ext, [(0.012, RED), (0.05, NAVY), (0.014, WHITE)], clip=fin)                                   # fin / rudder border
            v.fill(pant & (A > X_AX + 0.30), NAVY); v.fill(pant & (A > X_AX + 0.28) & (A <= X_AX + 0.30), WHITE)  # pant nose
            v.fill(pant & (np.abs(Z - (PANT_Z + 0.07 - 0.10 * (A - X_AX))) < 0.022) & (A <= X_AX + 0.28), NAVY)  # pant stripe
        else:
            zs = lerp(A, [1.0, 0.3, -1.0, -2.6, -3.9, -4.5], [0.06, -0.05, -0.20, -0.24, -0.17, 0.10])
            f = lerp(A, [1.0, -1.0, -4.5], [1.0, 0.9, 0.55])
            ok = (A > X_TP - 0.6) & ~pant & (Z > -0.98)
            v.fill(ok & (Z > zs + 0.105 * f) & (Z < zs + 0.17 * f) & (A < 0.45), RED)
            v.fill(ok & (Z > zs - 0.005 * f) & (Z < zs + 0.085 * f), LBLUE)
            v.fill(ok & (Z > zs - 0.20 * f) & (Z < zs - 0.025 * f), NAVY)
            v.fill(ok & (A > 0.45) & (Z < zs - 0.025 * f) & (Z > -0.98), NAVY)                     # navy lower cowl
            rud = fin & (A < H_T + 0.06) & (Z < 0.80); v.fill(fin, LBLUE); v.fill(rud, NAVY)
            bands(v, ext, [(0.05, RED), (0.014, WHITE)], clip=fin)
            for dz, wdt, col in ((0.155, 0.045, RED), (0.08, 0.05, LBLUE), (-0.01, 0.06, NAVY)):
                v.fill(pant & (np.abs(Z - (PANT_Z + dz + 0.06 * (A - X_AX))) < 0.5 * wdt) & (A > X_AX - 0.25), col)
        return v
    VL, VR = side('gl_side_l', True), side('gl_side_r', False)
    VFT, Y, X = plan('gl_fus_top', True, (-0.5, 0.5), (X0, X1), 1024, 4096); VFT.fill(np.ones_like(Y, bool), BASE)
    VFB, Y, X = plan('gl_fus_bot', False, (-0.5, 0.5), (X0, X1), 1024, 4096); VFB.fill(np.ones_like(Y, bool), BASE)
    if LIV == 'red-sunburst':
        for VF, up in ((VFT, True), (VFB, False)):
            VF.fill(VF.B > XN(np.vectorize(lambda x, y: fus_z(x, y, up))(VF.B, VF.A)) - 0.02, WHITE)
            VF.fill(VF.B > XN(np.vectorize(lambda x, y: fus_z(x, y, up))(VF.B, VF.A)), NAVY)
    else:
        VFB.fill(VFB.B > -0.6, NAVY)
    def surface(v, names, x0, rays):
        m = sil(v, names); Y_, X_ = v.A, v.B
        if LIV == 'red-sunburst':
            v.fill(m, RED); ph = np.degrees(np.arctan2(np.abs(Y_), X_ - x0))
            for c in rays:
                v.fill(m & (np.abs(ph - c) < rays[c] + 1.2), WHITE); v.fill(m & (np.abs(ph - c) < rays[c]), NAVY)
            bands(v, m, [(0.035, NAVY), (0.012, WHITE)])
            v.fill(m & (np.abs(Y_) > np.abs(Y_).max() * 0 + v.tipcut), NAVY)
        else:
            v.fill(m, WHITE); bands(v, m, [(0.06, RED), (0.012, None), (0.022, LBLUE), (0.01, None), (0.018, NAVY)])
            v.fill(m & (np.abs(Y_) > v.tipcut), RED)
    VUW, Y, X = plan('gl_wing_u', True, (-4.1, 4.1), (-1.75, 0.05), 4096, 1024); VUW.tipcut = S_W - 0.16
    surface(VUW, ['Wing_Upper'], -CH + 0.02, {16: 3.0, 34: 3.5, 52: 3.5, 70: 3.0})
    VLW, Y, X = plan('gl_wing_l', True, (-4.1, 4.1), (-1.85, -0.60), 4096, 768); VLW.tipcut = S_W - 0.16
    surface(VLW, ['Wing_Lower', 'Aileron_LL', 'Aileron_LR'], LE_L - CH + 0.02, {16: 3.0, 34: 3.5, 52: 3.5, 70: 3.0})
    VST, Y, X = plan('gl_stab', True, (-1.6, 1.6), (-5.15, -3.85), 2048, 1024)
    _sv = np.array([x.co[:] for n in ('Stab', 'Elevator_L', 'Elevator_R') for x in P[n].data.vertices])
    VST.tipcut = np.abs(_sv[:, 1]).max() - 0.10
    surface(VST, ['Stab', 'Elevator_L', 'Elevator_R'], _sv[:, 0].min() + 0.02, {22: 4.0, 48: 4.5, 74: 4.0})

M = {}
for v in (VL, VR, VFT, VFB, VUW, VLW, VST): M[v.name] = paint.mat_image('Paint_' + v.name, v.to_image(os.path.join(TEX, v.name + '.png')))
M['red'] = paint.mat_colour('Paint_red', RED); M['white'] = paint.mat_colour('Paint_white', WHITE)
M['steel'] = paint.mat_colour('Wire_steel', STEEL, rough=0.25, metal=0.9)
M['tyre'] = paint.mat_colour('Paint_tyre', TYRE, rough=0.8); M['cabin'] = paint.mat_colour('Paint_cabin', CABIN, rough=0.85)
M['glass'] = paint.mat_glass('Glass', col=(120, 150, 185), alpha=0.35)
M['frame'] = paint.mat_colour('Windscreen_frame', (40, 40, 44), rough=0.5)
M['prop_spinner'] = paint.mat_colour('Prop_spinner', RED) if LIV == 'v1' else (paint.mat_colour('Prop_spinner', WHITE) if LIV == 'red-sunburst' else paint.mat_colour('Prop_spinner_chrome', (225, 228, 232), rough=0.12, metal=1.0))
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
    if c.z < 0.18 and any(xa - 0.01 < c.x < xb + 0.01 for xb, xa in ((XC1F, XC1A), (XC2F, XC2A))):
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
