"""Our own Pitts S-2B -> two liveries from one model.   v4
  PITTS_LIVERY=N31DG (default): -> pitts-s2b-like-own.glb       red/white checkered rudder + checkered lower-wing
                                                                 underside, Hartzell 2-blade (grey, white tips)
  PITTS_LIVERY=N1191          : -> pitts-s2b-like-own-n1191.glb  the factory scheme: three white rudder diamonds, white
                                                                 stripe + diamonds under the lower wing, MT 3-blade
                                                                 (white, red tips)

Clean-room: all geometry is generated here from numbers measured off the public Aviat S-2B three-view and the
published dimensions (wingspan, length, chords, stagger, gear track; see the dimension table below and
docs/models/pitts-s2b-own-PROVENANCE.md).  No third-party or GPL mesh, UV or texture is read or imported.
v4 geometry (Ty): fin/rudder side profile and stab/elevator planform re-traced from the Aviat factory three-view (scaled
to the published 5.71 m length; other S-2B three-views used as checks), slave struts directly behind the I-struts.
v4 paint: the Aviat factory sunburst scheme, re-created procedurally below from published painted three-views (Horizons
NZ, AirshowModels, Munden S-2C; looked at by eye only - nothing traced, sampled or copied into the textures): red base,
sunburst on the top of both wings and on the stab/elevators, white fuselage stripes + nose diamond + cowl swooshes and
bands, white turtledeck behind the canopy, wheel-pant stripes, every white area outlined in a thin (16 mm) navy line.
Lettering art: tools/own_models/art/pitts-*.png, our own renders of OFL fonts (Liberation Sans Bold, Dancing Script).

  PITTS_LIVERY=N1191 blender -b --python tools/own_models/build_pitts_s2b.py
"""
import bpy, bmesh, sys, os, math, numpy as np
from mathutils import Matrix, Vector
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import paint
from paint import View
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
LIV = os.environ.get('PITTS_LIVERY', 'N31DG').upper(); assert LIV in ('N31DG', 'N1191'), LIV
OUT = os.path.join(ROOT, 'unity/Assets/_Project/Resources/Models/pitts-s2b-like/pitts-s2b-like-own%s.glb' % ('' if LIV == 'N31DG' else '-' + LIV.lower()))
TEX = '/tmp/own_pitts_tex_' + LIV.lower(); os.makedirs(TEX, exist_ok=True)
print('LIVERY', LIV)
RED, WHITE, NAVY = (200, 24, 36), (246, 246, 246), (22, 26, 62)
BLADE, STEEL, TYRE, CABIN = (128, 131, 136), (196, 200, 206), (40, 40, 42), (58, 60, 64)
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
# v3 (Ty): bottom line smoothed - less pot belly (up to 4 cm shallower amidships) and the aft recurve removed
# (straight taper from the cockpit to the tail post instead of flattening out under the fin)
ST = [(1.500, 0.175, -0.205, 0.415, None), (1.485, 0.195, -0.235, 0.445, None), (1.450, 0.203, -0.275, 0.460, None),
      (1.380, 0.208, -0.330, 0.468, None), (1.300, 0.212, -0.375, 0.470, None), (1.150, 0.218, -0.455, 0.470, None),
      (0.950, 0.221, -0.540, 0.468, None), (0.770, 0.222, -0.590, 0.464, None), (0.450, 0.222, -0.636, 0.455, None),
      (0.150, 0.222, -0.670, 0.442, None), (-0.130, 0.222, -0.690, 0.430, 0.170), (-0.400, 0.262, -0.701, 0.418, 0.163),
      (-0.700, 0.305, -0.705, 0.402, 0.158), (-1.000, 0.348, -0.700, 0.383, 0.155), (-1.300, 0.390, -0.682, 0.360, 0.155),
      (-1.600, 0.429, -0.640, 0.335, 0.155), (-1.800, 0.395, -0.604, 0.312, None), (-2.060, 0.339, -0.555, 0.268, None),
      (-2.300, 0.300, -0.511, 0.222, None), (-2.512, 0.275, -0.472, 0.182, None), (-2.800, 0.240, -0.419, 0.125, None),
      (-3.050, 0.215, -0.374, 0.070, None), (-3.290, 0.195, -0.330, 0.018, None)]
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
S_U, RT_U = 3.048, 0.5 * 1.016          # v2: half-circle tip in planform (radius = half chord)
LEU0 = lambda y: 0.664 - 0.0948 * np.abs(y)
LEU, TEU = rounded(LEU0, S_U, RT_U)
WU = Surf(LEU, TEU, lambda y: 0.613, 0.12, m=0.010, inc=1.0)
HU = lambda y: LEU0(y) - HF * CH
AU_IN, AU_OUT = 1.20, S_U - RT_U + 0.25   # ailerons run into the tip arc (chord >= ~0.18 m there)
# lower wing: 19 ft span, unswept, 3 deg dihedral, symmetric section, root inside the fuselage
S_L, RT_L, Y_LR = 2.896, 0.5 * 1.016, 0.12
LEL0 = lambda y: 0.0 * np.abs(y)
LEL, TEL = rounded(LEL0, S_L, RT_L)
WL = Surf(LEL, TEL, lambda y: -0.551 + math.tan(math.radians(3)) * abs(y), 0.12, m=0.0, inc=1.0)
HL = lambda y: LEL0(y) - HF * CH
AL_IN, AL_OUT = 1.15, S_L - RT_L + 0.25
def wing_spans(y0, a_in, a_out, S, RT):
    return L(y0, a_in, 6) + L(a_in + 0.0005, a_out, 9) + tipL(a_out + 0.0005, S - 0.001, 16)
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
# horizontal tail (v4): planform traced from the Aviat three-view top view, hinge-referenced: (d = |y|, LE ahead of the
# hinge, TE aft of the hinge) in metres; straight hinge x -3.30, no horn balances on the drawing; elevators from |y| 0.155
H_T, B_S = -3.30, 1.1404
STAB_T = [(0.0, .665, .300), (0.155, .662, .300), (0.172, .661, .353), (0.206, .660, .408), (0.2408, .6588, .4386),
          (0.2752, .6553, .4575), (0.3096, .6502, .4713), (0.344, .6416, .4799), (0.3784, .6364, .485), (0.4128, .6261, .485),
          (0.4472, .6158, .4885), (0.4816, .6054, .4833), (0.516, .5917, .4782), (0.5504, .5762, .4713), (0.5848, .559, .4644),
          (0.6192, .5452, .4558), (0.6536, .5298, .4455), (0.688, .5126, .4352), (0.7224, .4954, .4248), (0.7568, .4764, .4111),
          (0.7912, .4558, .399), (0.8256, .4352, .3836), (0.86, .4111, .3664), (0.8944, .387, .3474), (0.9288, .3578, .3285),
          (0.9632, .3268, .3044), (0.9976, .301, .2752), (1.032, .2614, .2442), (1.0423, .2511, .2322), (1.0526, .2391, .2202),
          (1.063, .2236, .2064), (1.0733, .2098, .1944), (1.0836, .1944, .1754), (1.0939, .1754, .16), (1.1042, .1548, .1462),
          (1.1146, .1359, .1256), (1.1249, .1032, .0998), (1.1352, .0619, .0585), (1.1404, 0.0, 0.0)]
_sd = [t[0] for t in STAB_T]
SLE = lambda y: H_T + np.interp(np.abs(y), _sd, [t[1] for t in STAB_T])
STE = lambda y: H_T - np.interp(np.abs(y), _sd, [t[2] for t in STAB_T])
ST_ = Surf(SLE, STE, lambda y: 0.071, 0.09)
sp = sorted(set([round(d, 4) for d in _sd if d < B_S] + [round(v, 4) for v in tipL(0, B_S * (1 - 1e-4), 10)]))
sp = [-s_ for s_ in sp[::-1] if s_ > 0] + sp
# v5: centre of the stab TE cut back ahead of the rudder nose so the swinging rudder skins never reach it
sp = sorted(set(sp + [-0.045, -0.065, 0.045, 0.065]))
P['Stab'] = surf_obj('Stab', ST_, sp, x_to=lambda s: H_T + 0.004 + 0.022 * float(np.clip((0.065 - abs(s)) / 0.02, 0, 1)))
# v5 (Ty: "the elevator halves should come to a point at the rudder post/hingeline ... a v shaped gap between them for
# the rudder to move through its travel ... clearance it needs plus an inch"): each elevator's inboard end is a straight
# diagonal cut from a point on the hinge line at |y| = E_IN, running aft-outboard at E_PHI to the aft centreline, until
# it meets the trailing edge (|y| = E_MEET). Sized for TCDS A8SO S-2B rudder travel L/R 30 deg +/- 0.75 deg: at 30.75 deg
# the rudder (TE, side skins and the hinge-post corner, every rudder height the elevator can reach at +/-27.75 deg)
# clears the cut edge by >= 1 in at every elevator angle; measured values + first-contact angles in the provenance (v5).
E_IN, E_PHI = 0.082, 30.0
E_TAN = math.tan(math.radians(E_PHI))
E_CUT = lambda s: H_T - 0.004 - (abs(s) - E_IN) / E_TAN          # x of the diagonal cut at span s (aft is -x)
_m = np.linspace(E_IN, 0.6, 6001); E_MEET = float(_m[np.argmax(E_CUT(_m) <= STE(_m))])
def e_to(s):
    return float(E_CUT(s)) if E_CUT(s) > STE(s) else None
e_sp = [E_IN] + [E_IN + (E_MEET - E_IN) * t for t in np.linspace(0, 1, 15)[1:]] + [d for d in _sd if E_MEET + 0.01 < d < B_S] + [B_S * (1 - 1e-4)]
for side, sg in (('L', 1), ('R', -1)):
    n = 'Elevator_' + side
    P[n] = surf_obj(n, ST_, [sg * s_ for s_ in e_sp], x_from=lambda s: H_T - 0.004, x_to=e_to)
print('ELEV_V E_IN %.3f E_PHI %.1f E_MEET %.4f' % (E_IN, E_PHI, E_MEET))
# fin + rudder: one symmetric section family over height z, cut at the rudder hinge
# v4 (Ty: "the rudder accurate"): fin LE and rudder outline traced from the Aviat factory three-view side view (scaled
# to 5.715 m nose-to-rudder, thrust line = spinner axis); HNZ and AirshowModels S-2B sheets used as checks (overlays in
# the provenance). The tail-light bump on the drawing's trailing edge is smoothed. (z, x) pairs.
FLE = [(-0.40, -2.55), (0.27, -2.55), (0.304, -2.566), (0.335, -2.614), (0.376, -2.654), (0.422, -2.688), (0.467, -2.723),
       (0.511, -2.760), (0.557, -2.794), (0.603, -2.829), (0.647, -2.866), (0.691, -2.902), (0.731, -2.943), (0.766, -2.988),
       (0.796, -3.037), (0.820, -3.089), (0.836, -3.144), (0.845, -3.200), (0.847, -3.290)]
RTE = [(-0.322, -3.310), (-0.305, -3.370), (-0.289, -3.416), (-0.278, -3.463), (-0.268, -3.510), (-0.255, -3.556),
       (-0.236, -3.600), (-0.211, -3.642), (-0.181, -3.680), (-0.147, -3.713), (-0.108, -3.743), (-0.060, -3.772),
       (-0.010, -3.795), (0.054, -3.812), (0.102, -3.819), (0.150, -3.824), (0.198, -3.826), (0.247, -3.825), (0.295, -3.820),
       (0.343, -3.813), (0.390, -3.802), (0.436, -3.789), (0.482, -3.772), (0.526, -3.754), (0.570, -3.732), (0.612, -3.708),
       (0.652, -3.681), (0.689, -3.649), (0.723, -3.615), (0.753, -3.577), (0.780, -3.537), (0.803, -3.494), (0.823, -3.450),
       (0.834, -3.400), (0.842, -3.360), (0.847, -3.310)]
Z_FT = 0.8465
fle = lambda z: float(np.interp(z, *zip(*FLE))); rte = lambda z: float(np.interp(z, *zip(*RTE)))
FN = Surf(fle, rte, lambda z: 0.0, 0.065, axis='z')
R_POST = lambda z: float(yt((fle(z) - H_T) / (fle(z) - rte(z)), 0.065) * (fle(z) - rte(z)))   # rudder half-thickness at the hinge
# v5: fin aft face set ahead of the rudder nose (local post radius + 3 mm) so the rudder swings without entering the fin
P['Fin'] = surf_obj('Fin', FN, tipL(0.15, Z_FT, 14), x_to=lambda s: H_T + R_POST(s) + 0.003)
rz = [-0.320 + (Z_FT + 0.320) * (1 - math.cos(math.pi * t)) / 2 for t in np.linspace(0, 1, 28)]
# v5: rudder nose = half-cylinder about the hinge line (radius = local skin half-thickness at the hinge, i.e. the
# rudder post), so it is unchanged by deflection and the skins aft of it swing clear of the fin / stab / elevators
def rudder_sec(z, nn=9):
    sec = FN.section(z, x_from=H_T); rho = abs(sec[0][1])
    return sec + [(H_T + rho * math.cos(t), rho * math.sin(t), z) for t in np.linspace(-math.pi / 2, math.pi / 2, nn + 2)[1:-1]]
bm = bmesh.new(); loft_rings(bm, [rudder_sec(z) for z in rz]); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
P['Rudder'] = link('Rudder', bm)

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
# v4 (Ty): aileron slave struts immediately behind the I-struts - same span stations and lean, on the ailerons just aft
# of the hinge (the I-strut's aft flange ends at ~66 % chord, the hinge is at 75 %)
for side, sy in (('L', 1), ('R', -1)):   # aileron slave struts (upper aileron drives the lower)
    bm = bmesh.new(); xu = float(HU(YI_U)) - 0.06; xl = float(HL(YI_L)) - 0.06
    blade_strut(bm, (xu, sy * YI_U, wl_low(YI_U, 0.81)[0] + 0.006), (xl, sy * YI_L, wl_low(YI_L, 0.81)[1] - 0.006), [0.045] * 3, 0.016)
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

# ---- undercarriage: S-2B steel-tube A-frame legs + bungee "cabane" struts to the belly, pants, tailwheel spring ----
# Per Ty + the Aviat three-view (side: the leg is a wide A at the fuselage, ~x +0.47 front / -0.21 rear, closing to the
# axle; front: legs leave the lower longerons ~y +-0.22) and the VH-ZZZ head-on photo: two streamlined struts run from
# the inboard leg abeam the axle up to the belly centreline, where bungee cords take the landing loads.
X_AX, Y_AX, Z_AX, R_W = 0.476, 0.81, -1.157, 0.17
def fus_low(x, y):
    """point on the fuselage lower surface at station x, half-breadth y (interpolated ST superellipse)"""
    xs = [r[0] for r in ST][::-1]; k = lambda i: float(np.interp(x, xs, [r[i] for r in ST][::-1]))
    top, bot, hw = k(1), k(2), k(3); zw = bot + 0.62 * (top - bot)
    c = min(1.0, abs(y) / hw) ** (NB / 2); s_ = math.sqrt(max(0.0, 1 - c * c)) ** (2 / NB)
    return np.array((x, y, zw - (zw - bot) * s_))
X_GF, X_GR, Y_G = X_AX, -0.21, 0.22               # A-frame fuselage pick-ups (front / rear) on the lower longerons
X_BG = X_AX                                        # v3: bungee housing at the front-leg station, so the belly struts lie in
                                                   # the same transverse plane as the front gear legs (Ty)
Z_BG = fus_low(X_BG, 0.0)[2]
bm = bmesh.new()
for sy in (1, -1):
    ax = np.array((X_AX, sy * (Y_AX - 0.085), Z_AX + 0.035))                                 # leg foot, inside the pant
    for xg, ch in ((X_GF, 0.070), (X_GR, 0.075)):
        top = fus_low(xg, sy * Y_G) + np.array((0, 0, 0.02))
        blade_strut(bm, top, ax, list(np.linspace(ch, 0.055, 6)), 0.034)                    # streamlined steel tube leg
        tube(bm, [top + np.array((0, 0, 0.03)), top - np.array((0, 0, 0.012))], 0.024, 10)   # pick-up fitting
    tube(bm, [(X_AX, sy * 0.70, Z_AX), (X_AX, sy * (Y_AX + 0.07), Z_AX)], 0.022, 10)        # axle
    bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=0.05, radius2=0.05, depth=0.135,
                          matrix=Matrix.Translation((X_AX, sy * Y_AX, Z_AX)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))   # hub
    # bungee strut: inboard leg abeam the axle -> belly centreline (spool end at the bungee housing)
    blade_strut(bm, (X_GF, sy * 0.70, Z_AX + 0.04), (X_GF, sy * 0.075, Z_BG - 0.085), list(np.linspace(0.065, 0.05, 6)), 0.026)
TW = dict(c=(-3.59, 0.0, -0.53), r=0.075, w=0.05)
tube(bm, [(-3.13, 0, -0.335), (-3.30, 0, -0.39), (-3.45, 0, -0.45), (-3.56, 0, -0.475)], 0.016, 8, r2=0.010)   # tail spring
for sy in (1, -1): blade_strut(bm, (-3.56, sy * 0.035, -0.47), (-3.59, sy * 0.035, -0.53), [0.04] * 3, 0.008)  # fork
bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Gear_Main'] = link('Gear_Main', bm)
# v3 (Ty): fabric fairing over the A-frame - a thin doped-fabric panel filling the gap between front and rear legs
bm = bmesh.new()
for sy in (1, -1):
    foot = np.array((X_AX, sy * (Y_AX - 0.085), Z_AX + 0.035))
    Ft, Rt = fus_low(X_GF, sy * Y_G) + np.array((0, 0, 0.02)), fus_low(X_GR, sy * Y_G) + np.array((0, 0, 0.02))
    nrm = np.cross(Rt - Ft, foot - Ft); nrm = nrm / np.linalg.norm(nrm)
    rings = []
    for sv in np.linspace(0.0, 0.94, 14):
        Fs, Rs = Ft + (foot - Ft) * sv, Rt + (foot - Rt) * sv; ring = []
        us = np.linspace(0.0, 1.0, 16)
        for side in (1, -1):
            for u in (us if side > 0 else us[::-1]):
                p = Fs + (Rs - Fs) * u
                if sv == 0.0: p = fus_low(p[0], p[1]) + np.array((0, 0, 0.03))      # top edge buried in the belly skin
                ring.append(p + side * nrm * (0.004 + 0.010 * math.sin(math.pi * u)))
        rings.append([tuple(q) for q in ring])
    loft_rings(bm, rings)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Gear_Fairing'] = link('Gear_Fairing', bm)
# bungee housing: belly blister on the centreline + cross spool; the shock cords are wrapped round the spool ends
bm = bmesh.new()
rs = []
for i, (dx, hz, hy) in enumerate(((-0.17, 0.004, 0.02), (-0.13, 0.035, 0.07), (-0.06, 0.060, 0.095), (0.0, 0.066, 0.10),
                                 (0.06, 0.060, 0.095), (0.12, 0.035, 0.07), (0.16, 0.004, 0.02))):
    x = X_BG + dx; zb = fus_low(x, 0.0)[2] + 0.01
    rs.append([(x, hy * math.cos(t), zb - hz * max(0.0, -math.sin(t)) + 0.012 * max(0.0, math.sin(t))) for t in np.linspace(0, 2 * math.pi, 20, endpoint=False)])
loft_rings(bm, rs)
P['Gear_Bungee_Housing'] = link('Gear_Bungee_Housing', bm)
bm = bmesh.new()
tube(bm, [(X_BG, -0.105, Z_BG - 0.085), (X_BG, 0.105, Z_BG - 0.085)], 0.016, 12)                    # spool
for sy in (1, -1):
    for k in range(4):                                                                             # cord wraps
        yk = sy * (0.040 + 0.012 * k); ring = []
        for t in np.linspace(0, 2 * math.pi, 16):
            ring.append((X_BG + 0.075 * math.cos(t) - 0.03, yk, Z_BG - 0.085 + 0.030 * math.sin(t) + 0.012))
        tube(bm, ring, 0.0055, 6)
P['Gear_Bungee'] = link('Gear_Bungee', bm)
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

# ---- spinner and prop (TCDS options) -----------------------------------------------------------------------------
# N31DG: Hartzell HC-C2YR-4CF / FC8477A-4 2-blade, 80 in, grey with white tips.
# N1191: MT-Propeller MTV-9-B-C / C190-18a 3-blade composite, 74.8 in, wide paddle blades, white with red tips.
# Both turn clockwise seen from the cockpit (American / Lycoming rotation).
SPB, SPX, SPR = 1.518, 1.899, 0.13
bm = bmesh.new(); ts = np.linspace(0.97, 0.0, 12)
rs = [[bm.verts.new((SPB + (SPX - SPB) * t, SPR * (1 - t ** 2) ** 0.6 * math.sin(a), SPR * (1 - t ** 2) ** 0.6 * math.cos(a))) for a in np.linspace(0, 2 * math.pi, 28, endpoint=False)] for t in ts]
tipv = bm.verts.new((SPX, 0, 0))
for i in range(28): j = (i + 1) % 28; bm.faces.new((tipv, rs[0][i], rs[0][j]))
for a_, b2 in zip(rs, rs[1:]):
    for i in range(28): j = (i + 1) % 28; bm.faces.new((a_[i], b2[i], b2[j], a_[j]))
bm.faces.new(rs[-1]); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Spinner'] = link('Spinner', bm)
MT = LIV == 'N1191'
PROP_X = 1.60
R_P, PITCH, NBL, TIP = (74.8 / 2 * IN, 1.9, 3, 5.0 * IN) if MT else (40 * IN, 1.9, 2, 4 * IN)
def chord(q):
    if MT:   # paddle: short round shank, wide flat-sided blade to ~85 %, then a broad rounded tip
        c = 0.075 + 0.135 * math.sin(0.5 * math.pi * min(q / 0.42, 1)) ** 1.4 + 0.012 * max(0.0, min((q - 0.42) / 0.3, 1))
        if q > 0.82: c *= math.sqrt(max(1 - ((q - 0.82) / 0.18) ** 2, 0.0)) * 0.75 + 0.25 * (1 - (q - 0.82) / 0.18)
        return max(c, 0.025)
    c = (0.085 + 0.075 * math.sin(math.pi * min(q / 0.5, 1) * 0.5)) - 0.03 * max(q - 0.5, 0) - (0.09 * ((q - 0.9) / 0.1) ** 2 if q > 0.9 else 0)
    return max(c, 0.02)
bm = bmesh.new(); face_tag = []
for blade in range(NBL):
    ph = math.pi / 2 + blade * 2 * math.pi / NBL          # one blade parked horizontal (as in the N31DG photo)
    rad = np.array([0, math.sin(ph), math.cos(ph)]); tan = np.cross([1.0, 0, 0], rad)   # v2: direction of travel, clockwise seen from the cockpit (Lycoming)
    ax = np.array([1.0, 0, 0]); hub = np.array([PROP_X, 0, 0])
    rr = np.linspace(0.10, R_P, 24 if MT else 18); secs = []; us = []
    for r in rr:
        q = (r - 0.10) / (R_P - 0.10)
        c = chord(q)
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
            r_mid = 0.5 * (rr[k] + rr[k + 1]); face_tag.append(1 if r_mid > R_P - TIP else 0)   # painted tips
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

# ---- paint (v4): the Aviat factory sunburst scheme, re-created procedurally ---------------------------------------
# Layout judged by eye from the published painted three-views (Horizons NZ + AirshowModels S-2B sheets, Munden S-2C as
# a cross-check, the Aviat drawing's own stab rays); every boundary below is our own simple geometry (lines, arcs, rays
# from a hub), nothing is traced or sampled from those images.
X0, X1, Z0, Z1 = -4.0, 2.0, -1.45, 1.0
Q_R = 0.098                     # N31DG rudder checks ~3.9 in: ~5 columns x 12 rows, as counted on the photo crop
OUTLINE = 0.016                 # navy outline round every white area, 16 mm
def dilate(m, rx, ry):
    out = m.copy()
    for dy in range(-ry, ry + 1):
        for dx in range(-rx, rx + 1):
            if (dx / max(rx, 1)) ** 2 + (dy / max(ry, 1)) ** 2 <= 1.0 and (dx or dy):
                out |= np.roll(np.roll(m, dy, 0), dx, 1)
    return out
def paint_white(v, white, outline=True):
    """white areas + a thin navy outline ring just outside them (keeps the white at its nominal size)"""
    if outline:
        rx = max(1, round(OUTLINE / abs(v.a1 - v.a0) * v.w)); ry = max(1, round(OUTLINE / abs(v.b1 - v.b0) * v.h))
        v.fill(dilate(white, rx, ry) & ~white, NAVY)
    v.fill(white, WHITE)
def poly_dist(A, B, pts):
    """distance from every (A, B) to a polyline"""
    d = np.full(A.shape, np.inf, np.float32)
    for (a0, b0), (a1, b1) in zip(pts, pts[1:]):
        ea, eb = a1 - a0, b1 - b0; L2 = ea * ea + eb * eb
        t = np.clip(((A - a0) * ea + (B - b0) * eb) / L2, 0, 1)
        d = np.minimum(d, np.hypot(A - (a0 + t * ea), B - (b0 + t * eb)))
    return d
def band(A, B, pts, widths):
    """white band of varying width along a polyline (widths per vertex, interpolated along the arc length)"""
    pts = np.array(pts); seg = np.r_[0, np.cumsum(np.hypot(*np.diff(pts, axis=0).T))]
    m = np.zeros(A.shape, bool)
    for k in range(len(pts) - 1):
        a0, b0 = pts[k]; a1, b1 = pts[k + 1]; ea, eb = a1 - a0, b1 - b0; L2 = ea * ea + eb * eb
        t = np.clip(((A - a0) * ea + (B - b0) * eb) / L2, 0, 1)
        d = np.hypot(A - (a0 + t * ea), B - (b0 + t * eb)); w = widths[k] + (widths[k + 1] - widths[k]) * t
        m |= d < 0.5 * w
    return m
def diamond(A, B, ca, cb, ha, hb):
    return np.abs(A - ca) / ha + np.abs(B - cb) / hb < 1.0
# fuselage stripes (side view): two straight white stripes sloping gently down aft to the rudder post; the upper one
# leaves the cowl top in a swoosh (the white band across the cowl top), the lower one the cowl bottom (the chevron under
# the cowl); a white diamond on the cowl side between the two swooshes
ZU_ = lambda x: 0.050 + 0.0289 * (x - 0.745); WU_ = lambda x: 0.044 - 0.003 * (0.745 - x)   # upper stripe centre / width
ZL_ = lambda x: -0.031 + 0.0259 * (x - 0.745); WL_ = 0.032
X_ST = -3.275                                                                                # stripes end at the tail post
DM_X, DM_Z, DM_HA, DM_HB = 1.10, 0.019, 0.105, 0.090                                       # nose diamond (V apex)
XS_U = XS_L = DM_X - DM_HA + 0.03                                                             # stripes run aft from the diamond
CB_X0, CB_X1 = 1.21, 1.33                                                                    # cowl-top band
def swoosh(p0, p1, p2, w0, w1, n=24):
    # quadratic Bezier from the diamond out to the cowl top / bottom, widening as it goes
    pts, ws = [], []
    for t in np.linspace(0, 1, n):
        pts.append(tuple((1 - t) ** 2 * np.array(p0) + 2 * t * (1 - t) * np.array(p1) + t * t * np.array(p2))); ws.append(w0 + (w1 - w0) * t)
    return pts, ws
def side(name, flip):
    v = View(name, (X0, X1), (Z0, Z1), 4096, 1672, flip_a=flip, axes=(0, 2)); A, Z = v.A, v.B
    v.fill(np.ones_like(A, bool), RED)
    W = np.zeros(A.shape, bool)
    on = (A > X_ST) & (A <= XS_U + 0.01)
    W |= on & (np.abs(Z - ZU_(A)) < 0.5 * WU_(A))
    W |= (A > X_ST) & (A <= XS_L + 0.01) & (np.abs(Z - ZL_(A)) < 0.5 * WL_)
    # the swooshes touch the diamond only at its top / bottom corners, the stripes at its aft edges, so it reads as a diamond
    pu, wu = swoosh((DM_X + 0.012, DM_Z + DM_HB - 0.012), (1.205, 0.14), (0.5 * (CB_X0 + CB_X1), 0.245), 0.04, CB_X1 - CB_X0)
    pl, wl = swoosh((DM_X + 0.012, DM_Z - DM_HB + 0.012), (1.205, -0.11), (1.25, -0.40), 0.04, 0.12)
    roi = (A > 0.9) & (A < 1.6)
    W[roi] |= band(A[roi], Z[roi], pu, wu) | band(A[roi], Z[roi], pl, wl)
    W |= (A > CB_X0) & (A < CB_X1) & (Z > 0.17)                            # where the cowl-top band rolls over the top
    # turtledeck: the white deck behind the canopy shows as a thin wedge above the canopy-rear line in side view
    # (painted from the top view; nothing extra here)
    # wheel pants: a white diamond on the nose of the pant and two thin stripes running aft along it
    xs_p = [r[0] for r in PANT][::-1]; zc = np.interp(A, xs_p, [r[1] for r in PANT][::-1]); ht = np.interp(A, xs_p, [r[2] for r in PANT][::-1])
    pz = (Z < -0.85)
    W |= pz & diamond(A, Z, 0.665, -1.147, 0.065, 0.048)
    for sgn in (1, -1):
        W |= pz & (A > -0.08) & (A < 0.575) & (np.abs(Z - (zc + sgn * 0.52 * ht)) < 0.011 + 0.006 * np.clip((A + 0.08) / 0.65, 0, 1))
    # rudder
    if LIV == 'N1191':        # three white diamonds stacked on the rudder, tip to tip (factory)
        for zc_ in (0.60, 0.28, -0.04):
            W |= diamond(A, Z, -3.52, zc_, 0.085, 0.14) & (A < H_T - 0.006)
    paint_white(v, W)
    paint_white(v, diamond(A, Z, DM_X, DM_Z, DM_HA, DM_HB))   # nose diamond at the apex of the V, own outline on top
    if LIV == 'N31DG':        # checkered rudder: aligned with the hinge line and horizontal, red check at the top-forward corner
        i = np.floor((H_T - A) / Q_R); j = np.floor((Z_FT - Z) / Q_R)
        v.fill((A < H_T - 0.002) & (Z > -0.40) & ((i + j) % 2 == 1), WHITE)
    # registration: white block letters (9 in) on the aft fuselage under the stripes, both sides reading nose -> tail
    rh = 0.25; png = 'pitts-%s.png' % LIV.lower(); aspect = {'N31DG': 687 / 162, 'N1191': 607 / 162}[LIV]
    v.stamp(png, -1.30, -1.30 - rh * aspect, -0.40, -0.40 + rh)
    # fin: "Pitts" script and "S-2B"
    v.stamp('pitts-script.png', -2.86, -3.183, 0.40, 0.56)
    v.stamp('pitts-s2b.png', -2.975, -3.115, 0.335, 0.385)
    return v
VL, VR = side('pitts_side_l', True), side('pitts_side_r', False)
def plan(name, flip, yr, xr, w, h):
    v = View(name, yr, xr, w, h, flip_a=flip, axes=(1, 0)); return v, v.A, v.B
# fuselage top / belly (plan views): white turtledeck wedge behind the canopy, cowl-top band; chevron under the cowl
VFT, Y, X = plan('pitts_fus_top', True, (-0.5, 0.5), (-4.0, 2.0), 1024, 4096)
VFT.fill(np.ones_like(Y, bool), RED)
paint_white(VFT, ((X < X_CA + 0.02) & (X > -2.20) & (np.abs(Y) < 0.215 * (X + 2.20) / (X_CA + 2.20 + 0.02))) |
            ((X > CB_X0) & (X < CB_X1)))
VFB, Y, X = plan('pitts_fus_bot', False, (-0.5, 0.5), (-4.0, 2.0), 1024, 4096)
VFB.fill(np.ones_like(Y, bool), RED)
paint_white(VFB, np.abs(X - (1.25 - 0.12 * np.abs(Y))) < 0.5 * 0.12)   # the lower swooshes meet under the cowl
# sunburst (top of both wings): rays fan aft and outboard from a hub on each side at the root, just behind the red
# leading-edge band beside the red centre V; ray boundaries in degrees from straight aft (0) toward outboard (90)
BAND, Y_HUB = 0.13, 0.38
X_HUB = float(LEU0(Y_HUB)) - BAND; X_APEX = float(LEU0(0.0)) - 0.55
RAYS_RED = [(12, 26), (38, 52), (62, 71), (77, 81.5)]
def sunburst_white(Y, X):
    th = np.degrees(np.arctan2(np.abs(Y) - Y_HUB, X_HUB - X)); red = np.zeros(Y.shape, bool)
    for a, b in RAYS_RED: red |= (th >= a) & (th < b)
    red |= (np.abs(Y) < Y_HUB * np.clip((X - X_APEX) / (X_HUB - X_APEX), 0, 1)) & (X > X_APEX)   # centre V
    red |= th >= 84.0
    return ~red
VUT, Y, X = plan('pitts_uw_top', True, (-3.15, 3.15), (-0.75, 0.72), 4096, 1024)
VUT.fill(np.ones_like(Y, bool), RED)
paint_white(VUT, sunburst_white(Y, X) & (X < LEU(Y) - BAND))
VLT, Y, X = plan('pitts_lw_top', True, (-3.0, 3.0), (-1.06, 0.04), 4096, 1024)
VLT.fill(np.ones_like(Y, bool), RED)
paint_white(VLT, sunburst_white(Y, X) & (X < LEL(Y) - 0.06))
VLB, Y, X = plan('pitts_lw_bot', False, (-3.0, 3.0), (-1.06, 0.04), 4096, 1024)
VLB.fill(np.ones_like(Y, bool), RED); f = (LEL(Y) - X) / CH
if LIV == 'N1191':   # white spanwise stripe just behind the LE + three white diamonds per side at mid-chord
    wb = (f > 0.09) & (f < 0.20) & (np.abs(Y) > 0.36) & (np.abs(Y) < 2.75)
    for yc_ in (0.77, 1.58, 2.35): wb |= diamond(np.abs(Y), f, yc_, 0.55, 0.25, 0.20)
    paint_white(VLB, wb)
else:                 # N31DG: checks, 10 in, 4 across the chord
    Q_W = CH / 4; VLB.fill((np.floor(np.abs(Y) / Q_W) + np.floor(f * 4)) % 2 == 1, WHITE)
# stab + elevators: rays from a hub at the stab root LE beside the fuselage, as on the Aviat drawing
VST, Y, X = plan('pitts_stab_top', True, (-1.25, 1.25), (-3.85, -2.55), 2048, 1024)
VST.fill(np.ones_like(Y, bool), RED)
th = np.degrees(np.arctan2(np.abs(Y) - 0.205, (H_T + 0.621) - X))
paint_white(VST, (th < 5) | ((th >= 21) & (th < 34)) | ((th >= 49) & (th < 61)))

M = {}
for v in (VL, VR, VFT, VFB, VUT, VLT, VLB, VST): M[v.name] = paint.mat_image('Paint_' + v.name, v.to_image(os.path.join(TEX, v.name + '.png')))
M['red'] = paint.mat_colour('Paint_red', RED); M['white'] = paint.mat_colour('Paint_white', WHITE)
M['steel'] = paint.mat_colour('Wire_steel', STEEL, rough=0.25, metal=0.9)
M['tyre'] = paint.mat_colour('Paint_tyre', TYRE, rough=0.8); M['cabin'] = paint.mat_colour('Paint_cabin', CABIN, rough=0.85)
M['glass'] = paint.mat_glass('Glass', col=(120, 150, 185), alpha=0.35)
M['prop_spinner'] = paint.mat_colour('Prop_spinner', RED)
if MT: M['prop_blade'] = paint.mat_colour('Prop_blade', WHITE, rough=0.35); M['prop_tip'] = paint.mat_colour('Prop_blade_tip', RED, rough=0.35)
else: M['prop_blade'] = paint.mat_colour('Prop_blade', BLADE, rough=0.45, metal=0.3); M['prop_tip'] = paint.mat_colour('Prop_blade_tip', WHITE, rough=0.4)
M['exhaust'] = paint.mat_colour('Exhaust_steel', (120, 112, 104), rough=0.5, metal=0.6)
def setm(o, names):
    o.data.materials.clear()
    for n in names: o.data.materials.append(M[n])
def wing_assign(top, bot):
    return lambda nrm, c: (0, top) if nrm.z >= 0 else (1, bot)
for n, t, b in (('Wing_Lower', VLT, VLB), ('Aileron_LL', VLT, VLB), ('Aileron_LR', VLT, VLB)):   # sunburst top, stripe+diamonds / checks under
    setm(P[n], [t.name, b.name]); paint.project(P[n], wing_assign(t, b))
for n in ('Wing_Upper', 'Aileron_UL', 'Aileron_UR'):                                          # sunburst top, plain red under
    setm(P[n], [VUT.name, 'red']); paint.project(P[n], lambda nrm, c: (0, VUT) if nrm.z >= 0 else (1, None))
for n in ('Stab', 'Elevator_L', 'Elevator_R'):
    setm(P[n], [VST.name, 'red']); paint.project(P[n], lambda nrm, c: (0, VST) if nrm.z >= 0 else (1, None))
def fus_assign(nrm, c):
    if X_CA - 0.01 < c.x < X_CF + 0.01 and c.z < 0.18:            # cockpit inner skin + bulkheads: interior colour
        if abs(nrm.x) > 0.85 or nrm.y * c.y + nrm.z * (c.z + 0.2) < 0: return (3, None)
    lim = 0.92 if c.x > 0.85 else 0.6                                # round cowl: keep its shoulders in the side views (swooshes)
    if nrm.z <= -lim: return (4, VFB)                                # belly: bottom view (cowl chevron)
    if nrm.z >= lim: return (2, VFT)                                 # top: top view (turtledeck, cowl band)
    return (0, VL) if nrm.y >= 0 else (1, VR)
setm(P['Fuselage'], ['pitts_side_l', 'pitts_side_r', 'pitts_fus_top', 'cabin', 'pitts_fus_bot']); paint.project(P['Fuselage'], fus_assign)
for n in ('Canopy_Frame', 'Fin', 'Rudder', 'Wheel_Pants'):
    setm(P[n], ['pitts_side_l', 'pitts_side_r', 'red'])
    paint.project(P[n], lambda nrm, c: (2, None) if nrm.z <= -0.6 else ((0, VL) if nrm.y >= 0 else (1, VR)))
for n, m in (('Canopy_Glass', 'glass'), ('Spinner', 'prop_spinner'), ('Gear_Main', 'red'), ('Gear_Fairing', 'red'), ('Gear_Bungee_Housing', 'red'), ('Gear_Bungee', 'tyre'), ('Wheels', 'tyre'),
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
