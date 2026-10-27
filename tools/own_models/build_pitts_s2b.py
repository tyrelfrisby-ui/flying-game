"""Our own Pitts S-2B in the N31DG paint -> pitts-s2b-like-own.glb.   v1

Clean-room: all geometry is generated here from numbers measured off the public Aviat S-2B three-view and the
published dimensions (wingspan, length, chords, stagger, gear track; see the dimension table below and
docs/models/pitts-s2b-own-PROVENANCE.md).  No third-party or GPL mesh, UV or texture is read or imported.
Paint: N31DG (S-2B c/n 5071) as seen in its one known photo (pittss2b.com, reference only): signal red, white
zig-zag sunburst on the wing tops, rays on the stab, cowl swoosh into twin pinstripes, white N31DG, "Pitts" fin
script, red/white checkered rudder; checkered wing undersides per Ty (not visible in the photo, layout assumed).
Lettering art: tools/own_models/art/pitts-*.png, our own renders of OFL fonts (Liberation Sans Bold, Dancing Script).

  blender -b --python tools/own_models/build_pitts_s2b.py
"""
import bpy, bmesh, sys, os, math, numpy as np
from mathutils import Matrix, Vector
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import paint
from paint import View
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(ROOT, 'unity/Assets/_Project/Resources/Models/pitts-s2b-like/pitts-s2b-like-own.glb')
TEX = '/tmp/own_pitts_tex'; os.makedirs(TEX, exist_ok=True)
RED, WHITE = (200, 24, 36), (246, 246, 246)
BLADE, STEEL, TYRE, CABIN = (30, 30, 32), (196, 200, 206), (40, 40, 42), (58, 60, 64)
bpy.ops.wm.read_factory_settings(use_empty=True)
# frame: metres, nose +X, port +Y, up +Z; origin = lower-wing root LE station / thrust line (yawed 180 deg at export)
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

# ---- fuselage: superellipse sections through the three-view stations ---------------------------------------
# (x, nominal top, bottom, half-width, cockpit sill or None).  The cockpit stations are cut at the sill into a U
# (outer skin + 12 mm inner skin), so the two cockpits are open under the canopy.
NT, NB = 2.4, 3.0
ST = [(1.500, 0.175, -0.205, 0.415, None), (1.485, 0.195, -0.235, 0.445, None), (1.450, 0.203, -0.275, 0.460, None),
      (1.380, 0.208, -0.330, 0.468, None), (1.300, 0.212, -0.375, 0.470, None), (1.150, 0.218, -0.462, 0.470, None),
      (0.950, 0.221, -0.555, 0.468, None), (0.770, 0.222, -0.607, 0.464, None), (0.450, 0.222, -0.645, 0.455, None),
      (0.150, 0.222, -0.695, 0.442, None), (-0.130, 0.222, -0.720, 0.430, 0.170), (-0.400, 0.262, -0.730, 0.418, 0.163),
      (-0.700, 0.305, -0.737, 0.402, 0.158), (-1.000, 0.348, -0.738, 0.383, 0.155), (-1.300, 0.390, -0.728, 0.360, 0.155),
      (-1.600, 0.429, -0.700, 0.335, 0.155), (-1.800, 0.395, -0.655, 0.312, None), (-2.060, 0.339, -0.577, 0.268, None),
      (-2.300, 0.300, -0.500, 0.222, None), (-2.512, 0.275, -0.440, 0.182, None), (-2.800, 0.240, -0.385, 0.125, None),
      (-3.050, 0.215, -0.345, 0.070, None), (-3.290, 0.195, -0.330, 0.018, None)]
X_CF, X_CA = -0.130, -1.600            # cockpit opening: windscreen base / canopy rear bow
def se_pt(t, top, bot, hw):
    zw = bot + 0.62 * (top - bot); c, s = math.cos(t), math.sin(t); up = s >= 0
    n = NT if up else NB; h = (top - zw) if up else (zw - bot)
    return (hw * math.copysign(abs(c) ** (2 / n), c), zw + h * math.copysign(abs(s) ** (2 / n), s))
def t_at(z, top, bot):
    zw = bot + 0.62 * (top - bot)
    if z >= zw: return math.asin(min(1.0, ((z - zw) / (top - zw)) ** (NT / 2)))
    return -math.asin(min(1.0, ((zw - z) / (zw - bot)) ** (NB / 2)))
NR = 48
def ring_closed(x, top, bot, hw):
    return [(x, *se_pt(t, top, bot, hw)) for t in np.linspace(0, 2 * math.pi, NR, endpoint=False)]
def ring_u(x, top, bot, hw, sill, wall=0.012, m=30):
    t0 = t_at(sill, top, bot); ts = np.linspace(t0, -math.pi - t0, m)
    out = [(x, *se_pt(t, top, bot, hw)) for t in ts]
    inn = [(x, *se_pt(t, top - wall, bot + wall, hw - wall)) for t in ts]
    inn[0] = (x, inn[0][1], sill); inn[-1] = (x, inn[-1][1], sill)
    return out + inn[::-1]
def sill_w(x):   # outer half-width of the cockpit opening at station x
    xs = [s[0] for s in ST if s[4] is not None][::-1]
    k = lambda i: np.interp(x, xs, [s[i] for s in ST if s[4] is not None][::-1])
    top, bot, hw, sill = k(1), k(2), k(3), k(4)
    return abs(se_pt(t_at(sill, top, bot), top, bot, hw)[0]), sill

bm = bmesh.new()
fwd = [s for s in ST if s[0] >= X_CF]; ck_ = [s for s in ST if s[4] is not None]; aft = [s for s in ST if s[0] <= X_CA]
fv, _ = loft_rings(bm, [ring_closed(*s[:4]) for s in fwd], cap0=False, cap1=True)
loft_rings(bm, [ring_u(*s) for s in ck_])
loft_rings(bm, [ring_closed(*s[:4]) for s in aft])
# cowl front face with the two AEIO-540 cooling inlets as real holes (planar scanfill with hole loops)
X_FACE = ST[0][0]
INL = dict(y=0.255, z=0.035, a=0.100, b=0.062)
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

# ---- canopy: one bubble from the windscreen base to the rear bow, on the cockpit sills ---------------------------
CAN = [(-0.131, 0.222), (-0.25, 0.345), (-0.393, 0.458), (-0.601, 0.530), (-0.869, 0.554), (-1.05, 0.552), (-1.226, 0.530),
       (-1.464, 0.482), (-1.601, 0.429)]
can_top = lambda x: float(np.interp(-x, [-c[0] for c in CAN], [c[1] for c in CAN]))
def can_ring(x, inset=0.004, n=26, grow=0.0):
    w, s = sill_w(x); w -= inset; t = can_top(x) + grow; s -= 0.002
    return [(x, (w + grow) * math.copysign(abs(math.cos(p)) ** (2 / 2.4), math.cos(p)), s + (t - s) * abs(math.sin(p)) ** (2 / 2.4)) for p in np.linspace(0, math.pi, n)]
CX = list(np.linspace(-0.13, -1.60, 18))
bm = bmesh.new(); loft_rings(bm, [can_ring(x) for x in CX], False, False, closed=False); P['Canopy_Glass'] = link('Canopy_Glass', bm)
bm = bmesh.new()
for x in (-0.135, -0.47, -1.595): tube(bm, can_ring(x, grow=0.004), 0.011, 8)           # windscreen base, windscreen bow, rear bow
for sy in (1, -1):                                                                          # sill rails
    tube(bm, [(x, sy * (sill_w(x)[0] - 0.004), sill_w(x)[1] + 0.006) for x in np.linspace(-0.13, -1.60, 12)], 0.014, 8)
P['Canopy_Frame'] = link('Canopy_Frame', bm)

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
ZFL = -0.58                                                                                           # cockpit floor
ck_box('ck_tub', (0.5 * (X_CF + X_CA), 0, ZFL - 0.005), (X_CF - X_CA - 0.02, 0.46, 0.01))
# front cockpit: small panel under the windscreen, seat, stick (TCDS front-seat arm 13.3 in aft of datum ~ x -0.34)
ck_prism('ck_panel', [(-0.25, -0.08), (0.25, -0.08), (0.27, 0.10), (0.15, 0.16), (-0.15, 0.16), (-0.27, 0.10)], -0.145, 0.012)
for y in (-0.13, 0.0, 0.13): ck_gauge(-0.151, y, 0.045, 0.036)
ck_seat(-0.26, ZFL + 0.07, -0.06, 0.20)
ck_stick(-0.17, ZFL, 0.52)
# rear (PIC) cockpit: full panel on the frame behind the front seat, seat, stick (TCDS rear-seat arm ~ x -1.14)
ck_prism('ck_panel', [(-0.30, -0.12), (0.30, -0.12), (0.31, 0.12), (0.22, 0.15), (-0.22, 0.15), (-0.31, 0.12)], -0.80, 0.015)
ck_box('ck_black', (-0.79, 0, 0.152), (0.06, 0.50, 0.012))                                        # glareshield lip
for y in (-0.18, -0.06, 0.06, 0.18): ck_gauge(-0.808, y, 0.065, 0.034)
for y in (-0.12, 0.0, 0.12): ck_gauge(-0.808, y, -0.035, 0.030)
ck_seat(-1.04, ZFL + 0.07, 0.20, 0.20)
ck_stick(-0.93, ZFL, 0.55)
for sy in (1, -1):                                                                                     # steel-tube longerons + diagonals
    p = lambda x, dz=0.0: (x, sy * (sill_w(x)[0] - 0.035), sill_w(x)[1] - 0.025 + dz)
    ck_cyl('ck_tube', p(-0.14), p(-1.59), 0.011, 8)
    for xa, xb in ((-0.14, -0.55), (-0.55, -1.05), (-1.05, -1.59)):
        ck_cyl('ck_tube', p(xa), (xb, sy * (sill_w(xb)[0] - 0.05), ZFL + 0.05), 0.010, 8)
for i, (xa, xb) in enumerate(((-0.30, -0.55), (-1.10, -1.35))):                                      # rudder pedals
    for sy in (1, -1): ck_box('ck_stick', (xa + 0.08 if i == 0 else xa + 0.30, sy * 0.13, ZFL + 0.10), (0.03, 0.08, 0.13), ry=0.35)
bmesh.ops.recalc_face_normals(ck, faces=ck.faces); P['Cockpit'] = link('Cockpit', ck)

# ---- lifting surfaces ----------------------------------------------------------------------------------------
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
CH, HF, GAP = 1.016, 0.75, 0.003        # 40 in chord both wings, hinge at 75 % chord, 6 mm hinge gap
def rounded(le0, S, RT):
    """planform with constant chord CH, LE le0(y), elliptically rounded tip over the last RT of span S"""
    def k(y):
        d = np.clip((np.abs(y) - (S - RT)) / RT, 0, 1); return np.sqrt(np.maximum(1 - d ** 2, 0.0004))
    le = lambda y: le0(y) - 0.5 * CH * (1 - k(y)); te = lambda y: le0(y) - CH + 0.5 * CH * (1 - k(y))
    return le, te
# upper wing: 20 ft span, LE 0.664 ahead of the lower LE at the root (stagger), ~5.4 deg sweep, chord line z 0.613
S_U, RT_U = 3.048, 0.22
LEU0 = lambda y: 0.664 - 0.0948 * np.abs(y)
LEU, TEU = rounded(LEU0, S_U, RT_U)
WU = Surf(LEU, TEU, lambda y: 0.613, 0.12, m=0.010, inc=1.0)
HU = lambda y: LEU0(y) - HF * CH
AU_IN, AU_OUT = 1.20, S_U - RT_U
# lower wing: 19 ft span, unswept, 3 deg dihedral, symmetric section, root inside the fuselage
S_L, RT_L, Y_LR = 2.896, 0.20, 0.12
LEL0 = lambda y: 0.0 * np.abs(y)
LEL, TEL = rounded(LEL0, S_L, RT_L)
WL = Surf(LEL, TEL, lambda y: -0.551 + math.tan(math.radians(3)) * abs(y), 0.12, m=0.0, inc=1.0)
HL = lambda y: LEL0(y) - HF * CH
AL_IN, AL_OUT = 1.15, S_L - RT_L
def wing_spans(y0, a_in, a_out, S, RT):
    return L(y0, a_in, 6) + L(a_in + 0.0005, a_out, 9) + tipL(a_out + 0.0005, S - 0.002, 9)
def wing_to(Hf, a_in, a_out):
    return lambda s: (Hf(s) + GAP) if a_in < abs(s) <= a_out else None
spans_u = wing_spans(0.0, AU_IN, AU_OUT, S_U, RT_U)
spans_u = [-s for s in spans_u[::-1]] + spans_u[1:]
P['Wing_Upper'] = surf_obj('Wing_Upper', WU, spans_u, x_to=wing_to(HU, AU_IN, AU_OUT))
bm = bmesh.new()
for sg in (1, -1):
    secs = [WL.section(sg * s, None, wing_to(HL, AL_IN, AL_OUT)(s)) for s in wing_spans(Y_LR, AL_IN, AL_OUT, S_L, RT_L)]
    loft_rings(bm, secs if sg > 0 else secs[::-1])
bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Wing_Lower'] = link('Wing_Lower', bm)
for nm, W_, Hf, a, b in (('U', WU, HU, AU_IN, AU_OUT), ('L', WL, HL, AL_IN, AL_OUT)):
    for side, sg in (('L', 1), ('R', -1)):
        n = 'Aileron_%s%s' % (nm, side)
        P[n] = surf_obj(n, W_, [sg * s for s in L(a + 0.004, b - 0.004, 8)], x_from=lambda s: Hf(s) - GAP)
# horizontal tail: straight hinge x -3.30, rounded LE / TE converging at the tip
H_T, B_S = -3.30, 1.17
SLE = lambda y: H_T + 0.70 * np.maximum(1 - (np.abs(y) / B_S) ** 3, 0) ** (1 / 3)
STE = lambda y: H_T - 0.40 * np.maximum(1 - (np.abs(y) / B_S) ** 2.5, 0) ** 0.4
ST_ = Surf(SLE, STE, lambda y: 0.071, 0.09)
sp = tipL(0, B_S * (1 - 1e-4), 14); sp = [-s for s in sp[::-1]] + sp[1:]
P['Stab'] = surf_obj('Stab', ST_, sp, x_to=lambda s: H_T + 0.004)
E_IN = 0.12
for side, sg in (('L', 1), ('R', -1)):
    n = 'Elevator_' + side
    P[n] = surf_obj(n, ST_, [sg * s for s in tipL(E_IN, B_S * (1 - 1e-4), 12)], x_from=lambda s: H_T - 0.004)
# fin + rudder: one symmetric section family over height z, cut at the rudder hinge
FLE = [(-0.40, -2.55), (0.27, -2.55), (0.34, -2.637), (0.45, -2.72), (0.62, -2.86), (0.76, -3.05), (0.81, -3.17), (0.833, -3.29)]
RTE = [(-0.335, -3.32), (-0.33, -3.45), (-0.29, -3.57), (-0.20, -3.68), (-0.07, -3.76), (0.12, -3.80), (0.30, -3.81),
       (0.50, -3.78), (0.66, -3.71), (0.76, -3.60), (0.82, -3.45), (0.833, -3.31)]
fle = lambda z: float(np.interp(z, *zip(*FLE))); rte = lambda z: float(np.interp(z, *zip(*RTE)))
FN = Surf(fle, rte, lambda z: 0.0, 0.065, axis='z')
P['Fin'] = surf_obj('Fin', FN, tipL(0.15, 0.828, 12), x_to=lambda s: H_T + 0.004)
rz = [-0.333 + (0.831 + 0.333) * (1 - math.cos(math.pi * t)) / 2 for t in np.linspace(0, 1, 20)]
P['Rudder'] = surf_obj('Rudder', FN, rz, x_from=lambda s: H_T - 0.004)

# ---- struts and wires ------------------------------------------------------------------------------------------
def wl_low(y, f):  # upper-wing lower surface z / lower-wing upper surface z at chord fraction f (nominal)
    return 0.613 - yt(f, 0.12) * CH + yc(f, 0.01) * CH, -0.551 + math.tan(math.radians(3)) * abs(y) + yt(f, 0.12) * CH
bm = bmesh.new()   # cabane: an N on each side, from the upper longerons to the centre section
for sy in (1, -1):
    ft, rt = (0.50, sy * 0.10, 0.575), (0.06, sy * 0.10, 0.565)
    fb, rb = (0.53, sy * 0.30, 0.185), (0.22, sy * 0.30, 0.185)
    for a, b in ((fb, ft), (rb, rt), (fb, rt)): blade_strut(bm, a, b, [0.050] * 4, 0.020)
P['Strut_Cabane'] = link('Strut_Cabane', bm)
YI_U, YI_L = 2.28, 2.18
bm = bmesh.new()   # interplane I-struts: flared ends spread over ~50 % chord, narrow waist
for sy in (1, -1):
    xu = float(LEU0(YI_U)) - 0.40 * CH; xl = float(LEL0(YI_L)) - 0.40 * CH
    zu = wl_low(YI_U, 0.4)[0] + 0.012; zl = wl_low(YI_L, 0.4)[1] - 0.012
    ch = [0.13 + 0.40 * abs(2 * t - 1) ** 5 for t in np.linspace(0, 1, 13)]
    blade_strut(bm, (xu, sy * YI_U, zu), (xl, sy * YI_L, zl), ch, 0.030)
P['Strut_Interplane'] = link('Strut_Interplane', bm)
YS = 2.62
for side, sy in (('L', 1), ('R', -1)):   # aileron slave struts (upper aileron drives the lower)
    bm = bmesh.new(); xu = float(HU(YS)) - 0.10; xl = float(HL(YS)) - 0.10
    blade_strut(bm, (xu, sy * YS, wl_low(YS, 0.85)[0] + 0.006), (xl, sy * (YS - 0.01), wl_low(YS, 0.85)[1] - 0.006), [0.045] * 3, 0.016)
    P['Strut_Slave_' + side] = link('Strut_Slave_' + side, bm)
bm = bmesh.new(); RWI = 0.0035
for sy in (1, -1):
    for dx in (0.0, -0.07):   # double flying wires: lower fuselage at the lower-wing root -> upper wing at the I-strut
        tube(bm, [(0.06 + dx, sy * 0.36, -0.57), (0.20 + dx, sy * (YI_U - 0.03), 0.555)], RWI, 6)
    for dx in (0.0, -0.07):   # double landing wires: cabane top -> lower wing at the I-strut
        tube(bm, [(0.40 + dx, sy * 0.13, 0.56), (-0.25 + dx, sy * (YI_L - 0.03), -0.40)], RWI, 6)
    tube(bm, [(0.50, sy * 0.10, 0.56), (0.22, -sy * 0.30, 0.19)], RWI, 6)                  # cabane cross wire
    tube(bm, [(-3.12, 0.0, 0.60), (-3.02, sy * 0.62, 0.083)], RWI, 6)                     # tail: fin -> stab top
    tube(bm, [(-3.02, sy * 0.62, 0.059), (-3.08, sy * 0.025, -0.34)], RWI, 6)            # stab bottom -> tail post
P['Wire_Bracing'] = link('Wire_Bracing', bm)

# ---- undercarriage: flat spring legs, pants, tailwheel spring -----------------------------------------------------
X_AX, Y_AX, Z_AX, R_W = 0.476, 0.81, -1.157, 0.17
bm = bmesh.new()
for sy in (1, -1):
    a, b = np.array((0.30, sy * 0.20, -0.66)), np.array((0.47, sy * 0.76, -1.14))
    blade_strut(bm, a, b, list(np.linspace(0.16, 0.10, 6)), 0.030)
    tube(bm, [(X_AX, sy * 0.73, Z_AX), (X_AX, sy * (Y_AX + 0.07), Z_AX)], 0.022, 10)      # axle
    bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=0.05, radius2=0.05, depth=0.135,
                          matrix=Matrix.Translation((X_AX, sy * Y_AX, Z_AX)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))   # hub
TW = dict(c=(-3.59, 0.0, -0.53), r=0.075, w=0.05)
tube(bm, [(-3.13, 0, -0.335), (-3.30, 0, -0.39), (-3.45, 0, -0.45), (-3.56, 0, -0.475)], 0.016, 8, r2=0.010)   # tail spring
for sy in (1, -1): blade_strut(bm, (-3.56, sy * 0.035, -0.47), (-3.59, sy * 0.035, -0.53), [0.04] * 3, 0.008)  # fork
bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Gear_Main'] = link('Gear_Main', bm)
bm = bmesh.new(); WP = [(-0.062, 0.09), (-0.058, 0.14), (-0.045, 0.164), (-0.02, 0.172), (0.02, 0.172), (0.045, 0.164), (0.058, 0.14), (0.062, 0.09)]
for sg in (1, -1):
    rs = [[(X_AX + r * math.sin(a), sg * Y_AX + dy, Z_AX + r * math.cos(a)) for a in np.linspace(0, 2 * math.pi, 32, endpoint=False)] for dy, r in WP]
    loft_rings(bm, rs if sg > 0 else rs[::-1])
bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=TW['r'], radius2=TW['r'], depth=TW['w'],
                      matrix=Matrix.Translation(TW['c']) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
P['Wheels'] = link('Wheels', bm)
PANT = [(0.80, -1.160, 0.012, 0.012, 0.012), (0.77, -1.150, 0.090, 0.080, 0.070), (0.70, -1.148, 0.145, 0.120, 0.098),
        (0.60, -1.145, 0.168, 0.132, 0.110), (0.476, -1.142, 0.172, 0.135, 0.112), (0.34, -1.132, 0.160, 0.125, 0.104),
        (0.18, -1.112, 0.130, 0.100, 0.086), (0.02, -1.085, 0.090, 0.070, 0.060), (-0.10, -1.062, 0.048, 0.040, 0.034),
        (-0.18, -1.045, 0.010, 0.010, 0.010)]       # (x, z centre, top half-height, bottom half-height, half-width)
bm = bmesh.new()
for sg in (1, -1):
    rs = []
    for x, zc, ht, hb, hw in PANT:
        rs.append([(x, sg * Y_AX + hw * math.cos(t), zc + (ht if math.sin(t) > 0 else hb) * math.sin(t)) for t in np.linspace(0, 2 * math.pi, 24, endpoint=False)])
    loft_rings(bm, rs)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Wheel_Pants'] = link('Wheel_Pants', bm)

# ---- spinner and 2-blade constant-speed prop (TCDS option: Hartzell HC-C2YR-4CF / FC8477A-4, 80 in) -------------
SPB, SPX, SPR = 1.518, 1.899, 0.13
bm = bmesh.new(); ts = np.linspace(0.97, 0.0, 12)
rs = [[bm.verts.new((SPB + (SPX - SPB) * t, SPR * (1 - t ** 2) ** 0.6 * math.sin(a), SPR * (1 - t ** 2) ** 0.6 * math.cos(a))) for a in np.linspace(0, 2 * math.pi, 28, endpoint=False)] for t in ts]
tipv = bm.verts.new((SPX, 0, 0))
for i in range(28): j = (i + 1) % 28; bm.faces.new((tipv, rs[0][i], rs[0][j]))
for a_, b2 in zip(rs, rs[1:]):
    for i in range(28): j = (i + 1) % 28; bm.faces.new((a_[i], b2[i], b2[j], a_[j]))
bm.faces.new(rs[-1]); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Spinner'] = link('Spinner', bm)
PROP_X, R_P, PITCH = 1.60, 40 * IN, 1.9
bm = bmesh.new(); face_tag = []
for blade in range(2):
    ph = math.pi / 2 + blade * math.pi                   # parked horizontal, as in the N31DG photo
    rad = np.array([0, math.sin(ph), math.cos(ph)]); tan = np.array([0, math.cos(ph), -math.sin(ph)])
    ax = np.array([1.0, 0, 0]); hub = np.array([PROP_X, 0, 0])
    rr = np.linspace(0.10, R_P, 18); secs = []; us = []
    for r in rr:
        q = (r - 0.10) / (R_P - 0.10)
        c = (0.085 + 0.075 * math.sin(math.pi * min(q / 0.5, 1) * 0.5)) - 0.03 * max(q - 0.5, 0) - (0.09 * ((q - 0.9) / 0.1) ** 2 if q > 0.9 else 0)
        c = max(c, 0.02)
        beta = math.atan(PITCH / (2 * math.pi * max(r, 0.25))); th = (0.30 - 0.24 * q) * c if q < 0.12 else (0.16 - 0.10 * q) * c
        sp = []; uu = []
        for a in np.linspace(0, 2 * math.pi, 18, endpoint=False):
            u = 0.5 * c * math.cos(a); v = 0.5 * th * math.sin(a)
            cd = math.cos(beta) * tan + math.sin(beta) * ax; nd = -math.sin(beta) * tan + math.cos(beta) * ax
            sp.append(hub + r * rad + u * cd + v * nd); uu.append(u)
        secs.append([bm.verts.new(p) for p in sp]); us.append(uu)
    for k in range(len(secs) - 1):
        for i in range(18):
            j = (i + 1) % 18; bm.faces.new((secs[k][i], secs[k][j], secs[k + 1][j], secs[k + 1][i]))
            r_mid = 0.5 * (rr[k] + rr[k + 1]); face_tag.append(1 if r_mid > R_P - 4 * IN else 0)   # 4 in white tips
    bm.faces.new(secs[-1]); face_tag.append(1); bm.faces.new(secs[0][::-1]); face_tag.append(0)
P['Prop'] = link('Prop', bm)

# ---- AEIO-540 (simplified flat six from primitives) behind the inlets, inlet ducts, firewall, exhaust ---------------
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
EN_X1, EN_PITCH, EN_STAG = 1.28, 0.25, 0.13
en_box('en_case', (EN_X1 - 0.27, 0, 0), (0.86, 0.24, 0.22))
en_cyl('en_case', (EN_X1 + 0.14, 0, 0), (EN_X1 + 0.32, 0, 0), 0.07)                                  # nose case to the prop flange
for sy, x0 in ((-1, EN_X1), (1, EN_X1 - EN_STAG)):                                                    # odd cyls starboard, even port
    for k in range(3):
        x = x0 - k * EN_PITCH
        en_cyl('en_cyl', (x, sy * 0.12, 0), (x, sy * 0.30, 0), 0.068, 16)
        en_cyl('en_core', (x, sy * 0.29, 0), (x, sy * 0.39, 0), 0.076, 16)
        en_box('en_rocker', (x, sy * 0.405, 0), (0.12, 0.03, 0.15))
for sy in (1, -1):
    _en('en_dark', lambda: loft_rings(en, [inlet_pts(sy, 1.0, X_FACE + 0.002), inlet_pts(sy, 0.92, X_FACE - 0.07)], False, False))
en_box('en_dark', (0.74, 0, -0.17), (0.01, 0.70, 0.60))                                               # firewall
en_tag = []; parts = []
for t in EN_T:
    if t in en_bms:
        b_ = en_bms[t]; me = bpy.data.meshes.new('en_' + t); b_.to_mesh(me); b_.free(); o = bpy.data.objects.new('en_' + t, me)
        bpy.context.scene.collection.objects.link(o); parts.append(o); en_tag += [EN_T.index(t)] * len(me.polygons)
P['Engine'] = paint.join(parts, 'Engine')
bm = bmesh.new()
for sy in (1, -1): tube(bm, [(0.95, sy * 0.20, -0.52), (0.86, sy * 0.20, -0.60), (0.74, sy * 0.21, -0.63)], 0.030, 12)
P['Exhaust'] = link('Exhaust', bm)

# ---- paint: N31DG --------------------------------------------------------------------------------------------------
X0, X1, Z0, Z1 = -4.0, 2.0, -1.45, 1.0
Q_R = 0.098                     # rudder checks ~3.9 in: ~5 columns x 12 rows, as counted on the photo crop
def side(name, flip):
    v = View(name, (X0, X1), (Z0, Z1), 4096, 1672, flip_a=flip, axes=(0, 2)); A, Z = v.A, v.B
    v.fill(np.ones_like(A, bool), RED)
    # twin pinstripes: white swoosh off the top of the cowl, two 1.4 in lines sloping gently down aft, pointed ends
    zl = 0.035 + (A - 1.40) * 0.035; X_END = -1.20
    sw = np.clip((A - 0.95) / 0.55, 0, 1) ** 2
    taper = np.clip((A - X_END) / 0.30, 0, 1)
    zu, wu = zl + 0.075 + 0.11 * sw, (0.035 + 0.05 * sw) * taper; wl_ = 0.035 * taper
    on = (A > X_END) & (A < 1.52) & (Z > -0.9)
    v.fill(on & (np.abs(Z - zu) < 0.5 * wu), WHITE)
    v.fill(on & (A < 1.47) & (np.abs(Z - zl) < 0.5 * wl_), WHITE)
    # registration, 13 in white block letters on the aft fuselage, both sides reading nose -> tail
    v.stamp('pitts-n31dg.png', -1.25, -2.62, -0.15 - 0.165, -0.15 + 0.165)
    # fin: "Pitts" script and "S-2B"
    v.stamp('pitts-script.png', -2.86, -3.183, 0.40, 0.56)
    v.stamp('pitts-s2b.png', -2.975, -3.115, 0.335, 0.385)
    # checkered rudder: aligned with the hinge line and horizontal, red check at the top-forward corner
    i = np.floor((H_T - A) / Q_R); j = np.floor((0.833 - Z) / Q_R)
    v.fill((A < H_T - 0.002) & (Z > -0.40) & ((i + j) % 2 == 1), WHITE)
    return v
VL, VR = side('pitts_side_l', True), side('pitts_side_r', False)
def plan(name, flip, yr, xr, w, h):
    v = View(name, yr, xr, w, h, flip_a=flip, axes=(1, 0)); return v, v.A, v.B
def zigzag(Y, f, V):
    yy = np.abs(Y); idx = np.zeros(Y.shape, int)
    for (y0, f0), (y1, f1) in zip(V, V[1:]):
        idx += (yy > y0 + (y1 - y0) * (f - f0) / (f1 - f0))
    return idx
# white zig-zag sunburst on the wing tops: triangles alternating base-on-LE / base-on-TE, white tips
ZZ_U = [(0.30, 0), (0.80, 1), (1.30, 0), (1.80, 1), (2.30, 0), (2.80, 1)]
ZZ_L = [(0.45, 0), (0.95, 1), (1.55, 0), (2.15, 1), (2.75, 0)]
Q_W = CH / 4                    # underside checks: 10 in, 4 across the chord (assumed layout)
def wing_views(tag, le0, zz, yr, xr):
    vt, Y, X = plan('pitts_%s_top' % tag, True, yr, xr, 4096, 1024); f = (le0(Y) - X) / CH
    vt.fill(np.ones_like(Y, bool), RED); vt.fill((len(zz) - 1 - zigzag(Y, f, zz)) % 2 == 0, WHITE)
    vb, Y, X = plan('pitts_%s_bot' % tag, False, yr, xr, 4096, 1024); f = (le0(Y) - X) / CH
    vb.fill(np.ones_like(Y, bool), RED); vb.fill((np.floor(np.abs(Y) / Q_W) + np.floor(f * 4)) % 2 == 1, WHITE)
    return vt, vb
VUT, VUB = wing_views('uw', LEU0, ZZ_U, (-3.15, 3.15), (-0.75, 0.72))
VLT, VLB = wing_views('lw', LEL0, ZZ_L, (-3.0, 3.0), (-1.06, 0.04))
VST, Y, X = plan('pitts_stab_top', True, (-1.25, 1.25), (-3.80, -2.55), 2048, 1024)   # rays from the stab root LE
VST.fill(np.ones_like(Y, bool), RED)
phi = np.degrees(np.arctan2(np.abs(Y), -2.60 - X)); VST.fill((np.searchsorted(np.array([12, 24, 36, 48, 60, 72]), phi) % 2 == 1), WHITE)

M = {}
for v in (VL, VR, VUT, VUB, VLT, VLB, VST): M[v.name] = paint.mat_image('Paint_' + v.name, v.to_image(os.path.join(TEX, v.name + '.png')))
M['red'] = paint.mat_colour('Paint_red', RED); M['white'] = paint.mat_colour('Paint_white', WHITE)
M['steel'] = paint.mat_colour('Wire_steel', STEEL, rough=0.25, metal=0.9)
M['tyre'] = paint.mat_colour('Paint_tyre', TYRE, rough=0.8); M['cabin'] = paint.mat_colour('Paint_cabin', CABIN, rough=0.85)
M['glass'] = paint.mat_glass('Glass', col=(120, 150, 185), alpha=0.35)
M['prop_spinner'] = paint.mat_colour('Prop_spinner', RED)
M['prop_blade'] = paint.mat_colour('Prop_blade', BLADE, rough=0.5)
M['prop_tip'] = paint.mat_colour('Prop_blade_tip', WHITE, rough=0.4)
M['exhaust'] = paint.mat_colour('Exhaust_steel', (120, 112, 104), rough=0.5, metal=0.6)
def setm(o, names):
    o.data.materials.clear()
    for n in names: o.data.materials.append(M[n])
def wing_assign(top, bot):
    return lambda nrm, c: (0, top) if nrm.z >= 0 else (1, bot)
for n, t, b in (('Wing_Upper', VUT, VUB), ('Aileron_UL', VUT, VUB), ('Aileron_UR', VUT, VUB),
                ('Wing_Lower', VLT, VLB), ('Aileron_LL', VLT, VLB), ('Aileron_LR', VLT, VLB)):
    setm(P[n], [t.name, b.name]); paint.project(P[n], wing_assign(t, b))
for n in ('Stab', 'Elevator_L', 'Elevator_R'):
    setm(P[n], [VST.name, 'red']); paint.project(P[n], lambda nrm, c: (0, VST) if nrm.z >= 0 else (1, None))
def fus_assign(nrm, c):
    if X_CA - 0.01 < c.x < X_CF + 0.01 and c.z < 0.18:            # cockpit inner skin + bulkheads: interior colour
        if abs(nrm.x) > 0.85 or nrm.y * c.y + nrm.z * (c.z + 0.2) < 0: return (3, None)
    if nrm.z <= -0.6: return (2, None)                               # belly: plain red
    return (0, VL) if nrm.y >= 0 else (1, VR)
setm(P['Fuselage'], ['pitts_side_l', 'pitts_side_r', 'red', 'cabin']); paint.project(P['Fuselage'], fus_assign)
for n in ('Canopy_Frame', 'Fin', 'Rudder', 'Wheel_Pants'):
    setm(P[n], ['pitts_side_l', 'pitts_side_r', 'red'])
    paint.project(P[n], lambda nrm, c: (2, None) if nrm.z <= -0.6 else ((0, VL) if nrm.y >= 0 else (1, VR)))
for n, m in (('Canopy_Glass', 'glass'), ('Spinner', 'prop_spinner'), ('Gear_Main', 'red'), ('Wheels', 'tyre'),
             ('Strut_Cabane', 'red'), ('Strut_Interplane', 'red'), ('Strut_Slave_L', 'red'), ('Strut_Slave_R', 'red'),
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
allv = np.concatenate([np.array([v.co[:] for v in o.data.vertices]) for o in P.values()])
print('BOUNDS', allv.min(0), allv.max(0), 'size', allv.max(0) - allv.min(0), 'parts', sorted(P))
print('TRIS', sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in P.values()))
for o in P.values(): o.data.transform(Matrix.Rotation(math.pi, 4, 'Z'))   # nose +x after glTF import, as the EA300
os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.object.select_all(action='DESELECT')
for o in P.values(): o.select_set(True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_yup=True, export_apply=True)
print('WROTE', OUT, os.path.getsize(OUT))
