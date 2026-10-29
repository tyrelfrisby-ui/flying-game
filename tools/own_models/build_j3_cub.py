"""Our own Piper J-3C-65 Cub style trainer (Aero Playground).   v1

Clean-room: every vertex is generated here from numbers measured by eye off public references: a public J-3 three-view
(scaled to the TCDS A-691 span 35 ft 2.5 in / length 22 ft 4.5 in), FAA TCDS A-691 (engine, prop, tyres, control travel),
the Univair J-3 parts catalogue (gear / shock-strut layout) and public photos of J-3C-65 aircraft (NASM, Udvar-Hazy and
others; Wikimedia Commons). Floats (J3_VARIANT=floats): EDO 1320 drawings on Kenmore Air Harbor's EDO page. See
docs/models/j3-cub-own-PROVENANCE.md. No third-party mesh, UV or texture is read or imported; no logo, script or
registration is painted (Cub yellow + a plain black lightning stripe).

Variant wheels: A-65 with the four cylinder heads sticking out of the cowl under black sheet-metal hoods, 72 in wood
prop, no spinner, 8.00-4 low-pressure tyres on 4 in wheels, bungee (shock-cord) main gear with the leather boots, leaf
spring tailwheel with steering springs, right-side split door (upper half folds UP under the wing, lower half folds
DOWN) as separate hinged parts. Variant floats: EDO 1320 floats, struts and wires, ONE water rudder on the left float.

Frame while building: x = -(distance aft of the prop hub), z = up from the thrust line (the three-view frame), y port;
at the end every mesh shifts by (+1.817, 0, +0.135) so the wing LE / thrust line match the Unity config, then the usual
yaw of 180 deg for export (nose +x after glTF import, like the Pitts / EA300 / Great Lakes).
  J3_VARIANT=wheels|floats blender -b --python tools/own_models/build_j3_cub.py
"""
import bpy, bmesh, sys, os, math, numpy as np
from mathutils import Matrix, Vector
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import paint
from paint import View
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
VAR = os.environ.get('J3_VARIANT', 'wheels').lower()
assert VAR in ('wheels', 'floats'), VAR
NAME = 'j3-cub-like' if VAR == 'wheels' else 'j3-cub-floats-like'
OUT = os.environ.get('J3_OUT') or os.path.join(ROOT, 'unity/Assets/_Project/Resources/Models/%s/%s-own.glb' % (NAME, NAME))
TEX = os.environ.get('J3_TEX', '/tmp/own_j3_tex_' + VAR); os.makedirs(TEX, exist_ok=True)
YEL, BLK, TYRE, WOOD = (244, 194, 22), (20, 20, 22), (36, 36, 38), (139, 84, 44)
bpy.ops.wm.read_factory_settings(use_empty=True)
IN = 0.0254
P = {}
SHIFT = np.array((1.817, 0.0, 0.135))


def link(name, bm):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); return o
def tag_new(bm, val):
    """stamp faces not yet tagged (bmesh reuses freed slots, so 'the last n faces' is not reliable)"""
    lay = bm.faces.layers.int.get('tg') or bm.faces.layers.int.new('tg')
    for f in bm.faces:
        if f[lay] == 0: f[lay] = val + 1
def tags_of(bm):
    lay = bm.faces.layers.int.get('tg'); out = [f[lay] - 1 for f in bm.faces]; bm.faces.layers.int.remove(lay); return out
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
def tube_r(bm, pts, rads, n=12):
    """tube with a radius per point"""
    pts = np.array(pts, float); rs = []; m = len(pts)
    for k, p in enumerate(pts):
        d = pts[min(k + 1, m - 1)] - pts[max(k - 1, 0)]; d /= np.linalg.norm(d)
        ref = np.array([0, 0, 1.0]) if abs(d[0]) > 0.8 else np.array([1.0, 0, 0])
        u = np.cross(d, ref); u /= np.linalg.norm(u); w = np.cross(d, u)
        rs.append([tuple(p + rads[k] * (math.cos(t) * u + math.sin(t) * w)) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)])
    loft_rings(bm, rs)
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
def bar(bm, pts, w, t, up=(0, 0, 1.0)):
    """flat bar (leaf spring / fork plate): width w across, thickness t along 'up' x path normal"""
    pts = np.array(pts, float); rs = []; m = len(pts)
    for k, p in enumerate(pts):
        d = pts[min(k + 1, m - 1)] - pts[max(k - 1, 0)]; d /= np.linalg.norm(d)
        nu = np.array(up, float) - np.dot(up, d) * d; nu /= np.linalg.norm(nu); s = np.cross(d, nu)
        rs.append([tuple(p + a_ * 0.5 * w * s + b_ * 0.5 * t * nu) for a_, b_ in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    loft_rings(bm, rs)
def cyl(bm, a, b, r, seg=16, r2=None):
    a, b = np.array(a, float), np.array(b, float); d = b - a; ln = float(np.linalg.norm(d))
    q = Vector((0, 0, 1)).rotation_difference(Vector(d / ln)).to_matrix().to_4x4()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=ln,
                          matrix=Matrix.Translation(tuple((a + b) / 2)) @ q)
def rbox(bm, c, s, rot=None):
    m = Matrix.Translation(tuple(c)) @ (rot if rot is not None else Matrix.Identity(4)) @ Matrix.Diagonal((s[0], s[1], s[2], 1))
    bmesh.ops.create_cube(bm, size=1.0, matrix=m)
def tbl(pts):
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    return lambda x: float(np.interp(x, xs, ys))
X = lambda xd: -xd                      # drawing station (m aft of the prop hub) -> build x
def B(xd, y, z): return (-xd, y, z)

# ---- flying surfaces (NACA 4-digit-like thickness + camber; the J-3 USA 35B is approximated by 12.5 % / 3.5 % camber) --
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
def surf_obj(name, surf, spans, x_from=None, x_to=None, n=22):
    bm = bmesh.new()
    secs = [surf.section(s, None if x_from is None else x_from(s), None if x_to is None else x_to(s), n) for s in spans]
    loft_rings(bm, secs); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return link(name, bm)
L = lambda a, b, n: list(np.linspace(a, b, n))
def tipL(a, b, n):  # spans clustered toward the tip b
    return [a + (b - a) * math.sin(0.5 * math.pi * t) for t in np.linspace(0, 1, n)]
def wsurf(W_, y, f, upper):
    """point on a wing's upper / lower skin at span y, chord fraction f"""
    le, te = float(W_.le(y)), float(W_.te(y)); c = le - te
    zc = W_.z(y) - f * c * math.sin(W_.inc) + float(yc(f, W_.m)) * c; t = float(yt(f, W_.tc)) * c
    return np.array((le - f * c, y, zc + (t if upper else -t)))

# wing: constant 63 in chord (three-view 1.62 m), 35 ft 2.5 in span, LE 1.405 m aft of the prop hub, chord line at the LE
# 0.59 m above the thrust line, 1.8 deg incidence, 1.5 deg dihedral outboard of the centre fitting (config value; the
# drawing's 1 deg is within reading error), continuous over the cabin. Tips: the three-view's asymmetric round tip.
CH, GAP = 1.62, 0.006
LE_D, TE_D = 1.405, 1.405 + CH
S_W = 5.37
RL, RT = 0.42, 0.92
def tip_round(le0):
    def le(y):
        d = np.clip((np.abs(y) - (S_W - RL)) / RL, 0, 1); return le0(y) - 0.38 * CH * (1 - np.sqrt(np.maximum(1 - d ** 2, 0)))
    def te(y):
        d = np.clip((np.abs(y) - (S_W - RT)) / RT, 0, 1); return le0(y) - CH + 0.62 * CH * (1 - np.sqrt(np.maximum(1 - d ** 2, 0)))
    return le, te
DIH, Y_DIH = 1.5, 0.30
LEW0 = lambda y: -LE_D + 0.0 * np.abs(y)
LEW, TEW = tip_round(LEW0)
ZW = lambda y: 0.59 + max(abs(float(y)) - Y_DIH, 0) * math.tan(math.radians(DIH))
W = Surf(LEW, TEW, ZW, 0.125, m=0.035, inc=1.8)
AL_IN, AL_OUT, HA = 2.13, 4.80, -2.67           # aileron span stations and hinge line (three-view top view)
def w_to(s): return (HA + GAP) if AL_IN < abs(s) <= AL_OUT else None
hs = sorted(set([round(v, 4) for v in L(0.0, Y_DIH, 3) + L(Y_DIH + 0.05, AL_IN, 7) + L(AL_IN + 0.0005, S_W - RT, 10) +
                 L(S_W - RT + 0.03, AL_OUT, 7) + [AL_OUT + 0.0005] + tipL(AL_OUT + 0.02, S_W - 0.001, 16)]))
sp_w = [-s for s in hs[::-1] if s > 0] + hs
P['Wing'] = surf_obj('Wing', W, sp_w, x_to=w_to, n=26)
for side, sg in (('L', 1), ('R', -1)):
    P['Aileron_' + side] = surf_obj('Aileron_' + side, W, [sg * s for s in L(AL_IN + 0.004, S_W - RT, 9) + L(S_W - RT + 0.03, AL_OUT - 0.004, 6)],
                                    x_from=lambda s: HA - GAP)
def wing_low_c(xd):            # wing lower skin over the fuselage centreline
    return float(wsurf(W, 0.0, (xd - LE_D) / CH, False)[2])

# ---- fuselage: superellipse sections, flat sides, flat top under the wing, rounded turtledeck aft, rounded cowl -------
# Lines off the three-view side view (top / bottom) and top view (half-breadth), drawing stations xd (m aft of the hub).
def _top_cabin(xd): return wing_low_c(xd) - 0.004
_TOPF = [(0.185, 0.035), (0.19, 0.07), (0.20, 0.10), (0.215, 0.13), (0.24, 0.155), (0.28, 0.170), (0.33, 0.178),
         (0.45, 0.183), (0.60, 0.19), (0.785, 0.20), (0.96, 0.21), (1.05, 0.30), (1.15, 0.39), (1.25, 0.465),
         (1.33, 0.52), (1.40, 0.556)]
_TOPA = [(3.03, None), (3.10, 0.536), (3.27, 0.53), (3.60, 0.512), (4.00, 0.49), (4.32, 0.475), (4.80, 0.478),
         (5.20, 0.488), (5.39, 0.495), (5.80, 0.48), (6.25, 0.45)]
_TOPA[0] = (3.03, _top_cabin(3.02))
def F_TOP(xd):
    if xd <= 1.40: return tbl(_TOPF)(xd)
    if xd <= 1.43: return _TOPF[-1][1] + (_top_cabin(1.43) - _TOPF[-1][1]) * (xd - 1.40) / 0.03
    if xd <= 3.02: return _top_cabin(xd)
    return tbl(_TOPA)(xd)
F_BOT = tbl([(0.185, -0.05), (0.19, -0.15), (0.20, -0.24), (0.22, -0.31), (0.25, -0.36), (0.30, -0.40), (0.33, -0.42),
             (0.45, -0.47), (0.60, -0.52), (0.785, -0.565), (1.09, -0.59), (1.46, -0.635), (2.03, -0.62), (2.50, -0.555),
             (3.39, -0.40), (4.10, -0.30), (6.25, 0.045)])
HW_CAB = 0.315        # cabin half-breadth: judgement between the top view (~0.30) and the front view (~0.345)
F_HW = tbl([(0.185, 0.05), (0.19, 0.09), (0.20, 0.12), (0.22, 0.165), (0.25, 0.20), (0.30, 0.225), (0.33, 0.235),
            (0.45, 0.25), (0.60, 0.262), (0.785, 0.275), (0.96, 0.29), (1.23, 0.31), (1.40, HW_CAB), (2.60, HW_CAB),
            (2.85, 0.31), (3.10, 0.285), (3.60, 0.262), (4.00, 0.231), (4.40, 0.192), (4.80, 0.147), (5.20, 0.105),
            (5.40, 0.086), (5.80, 0.055), (6.25, 0.022)])
F_NT = tbl([(0.185, 2.2), (0.785, 2.6), (0.96, 2.6), (1.40, 6.0), (1.50, 14.0), (2.95, 14.0), (3.25, 2.8), (6.25, 2.4)])
F_NB = tbl([(0.185, 2.2), (0.785, 2.8), (1.00, 5.0), (6.25, 5.0)])
ZWF = 0.55
KLO = [-0.31] + list(np.linspace(-0.215, 0.03, 8))      # v4: a key row on the raised lower-door bottom (zb -0.215)
KHI = [0.06, 0.10, 0.14, 0.18, 0.22, 0.26, 0.30, 0.34, 0.38, 0.42, 0.46, 0.47, 0.49]
KEYZ = KLO + KHI; KMIN, KMAX, KPIV = KEYZ[0], KEYZ[-1], 0.03
MT_, MB_ = 14, 14
def se_pt(t, top, bot, hw, nt, nb):
    zw = bot + ZWF * (top - bot); c, s = math.cos(t), math.sin(t); up = s >= 0
    n = nt if up else nb; h = (top - zw) if up else (zw - bot)
    return (hw * math.copysign(abs(c) ** (2 / n), c), zw + h * math.copysign(abs(s) ** (2 / n), s))
def t_at(z, top, bot, nt, nb):
    zw = bot + ZWF * (top - bot)
    if z >= zw: return math.asin(min(1.0, ((z - zw) / (top - zw)) ** (nt / 2)))
    return -math.asin(min(1.0, ((zw - z) / (zw - bot)) ** (nb / 2)))
def band(xd):
    top, bot, nt, nb = F_TOP(xd), F_BOT(xd), F_NT(xd), F_NB(xd); zw = bot + ZWF * (top - bot)
    mt = (top - zw) * min(max(0.6 / nt, 0.035), 0.3); mb = (zw - bot) * min(max(0.6 / nb, 0.035), 0.3)
    hi = min(KMAX, top - mt); lo = max(KMIN, bot + mb)
    piv = min(max(KPIV, lo + 0.1 * (hi - lo)), hi - 0.1 * (hi - lo))
    out = []
    for k in KEYZ:
        if k <= KPIV: out.append(lo + (k - KMIN) / (KPIV - KMIN) * (piv - lo))
        else: out.append(piv + (k - KPIV) / (KMAX - KPIV) * (hi - piv))
    return out
def ring(xd):
    top, bot, hw, nt, nb = F_TOP(xd), F_BOT(xd), F_HW(xd), F_NT(xd), F_NB(xd)
    zs = band(xd); th = [t_at(z, top, bot, nt, nb) for z in zs]
    tl = list(th)                                                   # port band, low -> high
    ttop = list(np.linspace(th[-1], math.pi - th[-1], MT_ + 2)[1:-1])
    tr = [math.pi - t for t in th[::-1]]                            # starboard band, high -> low
    tbot = list(np.linspace(math.pi - th[0], 2 * math.pi + th[0], MB_ + 2)[1:-1])
    return [(-xd, *se_pt(t, top, bot, hw, nt, nb)) for t in tl + ttop + tr + tbot]
def fus_y(xd, z):
    """half-breadth of the fuselage at station xd, height z"""
    top, bot, hw, nt, nb = F_TOP(xd), F_BOT(xd), F_HW(xd), F_NT(xd), F_NB(xd)
    t = t_at(z, top, bot, nt, nb); return abs(se_pt(t, top, bot, hw, nt, nb)[0])
# stations: dense where windows / door / hoods have edges
XS = [0.185, 0.186, 0.188, 0.191, 0.195, 0.20, 0.207, 0.215, 0.225, 0.238, 0.25, 0.27, 0.29, 0.31, 0.325, 0.345,
      0.37, 0.40, 0.43, 0.46, 0.49, 0.52, 0.545, 0.57, 0.60, 0.65, 0.70, 0.75, 0.785, 0.84, 0.90, 0.96]
XS += L(0.99, 1.23, 9) + L(1.27, 1.59, 7) + L(1.62, 1.94, 17) + L(1.98, 2.15, 4) + [2.19, 2.225] + L(2.25, 2.43, 10) + [2.47]   # 0.02 m through the chevron front + slanted aft door edges
XS += L(2.4975, 2.82, 13) + [2.87, 2.95, 3.03, 3.10, 3.20, 3.35, 3.50, 3.70, 3.90, 4.10, 4.35, 4.60, 4.85, 5.10, 5.30,
                              5.45, 5.60, 5.75, 5.90, 6.02, 6.12, 6.20, 6.25]
XS = sorted(set(round(v, 4) for v in XS))
bm = bmesh.new()
loft_rings(bm, [ring(x) for x in XS], cap0=True, cap1=True)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()
# window / door / hood regions on the skin (drawing coordinates xd, z)
def sill_d(xd): return 0.195 - (xd - 0.96) * (0.165 / 0.27)      # lower edge of the front side window
D_X0, D_X1, D_Z0, D_Z1, D_ZC = 2.47, 2.80, 0.03, 0.47, 0.23     # rear "D" window: straight front, round aft edge
def d_aft(z):
    h = (D_Z1 - D_ZC) if z >= D_ZC else (D_ZC - D_Z0); r = min(abs(z - D_ZC) / h, 1.0)
    return D_X0 + (D_X1 - D_X0) * (1 - r ** 2.5) ** (1 / 2.5)
DOOR = dict(x0=1.70, x1=2.43, zs=0.03, zt=0.49, zb=-0.215, gx0=1.73, gx1=2.40, gz0=0.06, gz1=0.46)
# chevron front edge (photos c37 / c47 / d01, refs/j3-cub): the door's forward point is at the sill (x0); the upper (window)
# half's front edge leans aft toward the top, the lower half's front edge leans aft toward the bottom. Lower door depth
# (sill -> zb) ~ half the sill-to-belly height, bottom well above the lower longeron (photos).
# v4 (Ty): lower panel bottom raised 0.095 m (zb -0.31 -> -0.215) so the folded-down panel (bottom at 2*zb - zs) clears the
# lift struts; same chevron angle (lean per metre unchanged). Aft edge slants so the door is narrow at the bottom and wide
# at the top (photos c47 / d01 / c34): 2.43 at the top, 2.40 at the split line, 2.25 at the bottom of the lower panel.
DOOR.update(up_lean=0.14, lo_lean=0.16 * 0.245 / 0.34, ax_t=2.43, ax_s=2.40, ax_b=2.25)
def door_fx(z):
    """door front edge xd at height z (chevron, forward point at the sill)"""
    if z >= DOOR['zs']: return DOOR['x0'] + DOOR['up_lean'] * (z - DOOR['zs']) / (DOOR['zt'] - DOOR['zs'])
    return DOOR['x0'] + DOOR['lo_lean'] * (DOOR['zs'] - z) / (DOOR['zs'] - DOOR['zb'])
def door_ax(z):
    """door aft edge xd at height z (slanted: narrow at the bottom, wide at the top)"""
    if z >= DOOR['zs']: return DOOR['ax_s'] + (DOOR['ax_t'] - DOOR['ax_s']) * min((z - DOOR['zs']) / (DOOR['zt'] - DOOR['zs']), 1.0)
    return DOOR['ax_s'] - (DOOR['ax_s'] - DOOR['ax_b']) * min((DOOR['zs'] - z) / (DOOR['zs'] - DOOR['zb']), 1.0)
POST = 0.035                                                         # door post between the fixed side window and the door
HOOD = {1: (0.27, 0.545), -1: (0.325, 0.60)}                        # head openings (xd range) per side; z -0.12..0.06
def region(c, n):
    xd, y, z = -c.x, c.y, c.z; side = abs(n.y) > 0.45
    if 0.96 < xd < (door_fx(z) - POST if side else 1.665) and z > (max(0.03, sill_d(xd)) if side else 0.03) and (xd < 1.42 or (side and z < DOOR['zt'])):
        return 'glass'
    if DOOR['x0'] < xd < door_ax(z) and side:
        if y < 0:
            if DOOR['zs'] < z < DOOR['zt'] and xd > door_fx(z):
                return 'door_glass' if (door_fx(z) + 0.03 < xd < door_ax(z) - 0.03 and DOOR['gz0'] < z < DOOR['gz1']) else 'door_up'
            if DOOR['zb'] < z < DOOR['zs'] and xd > door_fx(z): return 'door_lo'
        elif DOOR['zs'] < z < DOOR['zt'] and xd > door_fx(z) and not (2.19 < xd < 2.225):
            return 'glass'
    if D_X0 < xd < D_X1 + 0.03 and D_Z0 < z < D_Z1 and side and xd < d_aft(z):
        return 'glass'
    sy = 1 if y > 0 else -1
    if HOOD[sy][0] < xd < HOOD[sy][1] and -0.12 < z < 0.13 and abs(n.y) > 0.40:
        return 'hole'
    return 'skin'
lab = [region(f.calc_center_median(), f.normal) for f in bm.faces]
# snap the stepped boundaries onto the drawn lines (front window sill diagonal; D window aft curve)
for v in bm.verts:
    ls = set(lab[f.index] for f in v.link_faces)
    if 'glass' in ls and len(ls) > 1:
        xd, z = -v.co.x, v.co.z
        side_v = min(abs(f.normal.y) for f in v.link_faces) > 0.45       # only the side-window sill, never the windshield base
        if 0.96 < xd < 1.235 and side_v and abs(z - sill_d(xd)) < 0.025: v.co.z = sill_d(xd)
        elif D_X0 + 0.01 < xd < D_X1 + 0.05 and D_Z0 + 0.004 < z < D_Z1 - 0.004: v.co.x = -d_aft(z)
# chevron door / window-post edges: pull the stepped boundary verts onto the slanted lines (nearest line within one station)
CHEV = 0
for v in bm.verts:
    ls = set(lab[f.index] for f in v.link_faces)
    if len(ls) < 2 or min(abs(f.normal.y) for f in v.link_faces) < 0.45: continue
    xd, z = -v.co.x, v.co.z
    if not (DOOR['zb'] - 0.005 < z < DOOR['zt'] + 0.005) or not (1.55 < xd < 2.0 or 2.17 < xd < 2.47): continue
    f0 = door_fx(z); tg = []
    if xd > 2.17:                                                                             # v4 slanted aft edge
        if 2.23 < xd and (ls & {'door_up', 'door_lo'} or ('glass' in ls and v.co.y > 0)): tg.append(door_ax(z))
        if 'door_glass' in ls: tg.append(door_ax(z) - 0.03)
        f0 = None
    if f0 is not None and (ls & {'door_up', 'door_lo'} or ('glass' in ls and v.co.y > 0)): tg.append(f0)          # door front edge (left: window)
    if f0 is not None and 'glass' in ls: tg.append(f0 - POST)                                                   # fixed side window aft edge
    if f0 is not None and 'door_glass' in ls: tg.append(f0 + 0.03)                                              # door window front edge
    tg = [t for t in tg if abs(t - xd) < 0.024]
    if tg: v.co.x = -min(tg, key=lambda t: abs(t - xd)); CHEV += 1
print('DOOR chevron verts snapped', CHEV)
def split(labels):
    b2 = bm.copy(); b2.faces.ensure_lookup_table()
    bmesh.ops.delete(b2, geom=[f for f in b2.faces if lab[f.index] not in labels], context='FACES')
    return b2
b_door_up, b_door_lo, b_door_gl, b_glass = split({'door_up'}), split({'door_lo'}), split({'door_glass'}), split({'glass'})
b_skin = split({'skin'})
# cabin liner (so the far wall is visible through the windows with back-face culling): inward copy of the cabin skin
b_lin = split({'skin'}); b_lin.faces.ensure_lookup_table()
bmesh.ops.delete(b_lin, geom=[f for f in b_lin.faces if not (0.80 < -f.calc_center_median().x < 2.95)], context='FACES')
for v in b_lin.verts: v.co -= v.normal * 0.006
bmesh.ops.reverse_faces(b_lin, faces=b_lin.faces)
# v5f: starboard (door-side) half of the liner as its own object so the cutaway can drop it and keep the far wall
b_lin_r = b_lin.copy(); b_lin_r.faces.ensure_lookup_table(); b_lin.faces.ensure_lookup_table()
bmesh.ops.delete(b_lin_r, geom=[f for f in b_lin_r.faces if f.calc_center_median().y > -0.05], context='FACES')
bmesh.ops.delete(b_lin, geom=[f for f in b_lin.faces if f.calc_center_median().y <= -0.05], context='FACES')
bm.free()
print('FUS faces', len(lab), {k: lab.count(k) for k in set(lab)})
P['Fuselage'] = link('Fuselage', b_skin)
P['Cabin_Liner'] = link('Cabin_Liner', b_lin); P['Liner_Stbd'] = link('Liner_Stbd', b_lin_r)
P['Glass'] = link('Glass', b_glass)
for nm, b2 in (('DoorUpper_R', b_door_up), ('DoorLower_R', b_door_lo)):
    bmesh.ops.solidify(b2, geom=b2.faces[:], thickness=0.012)
    P[nm] = link(nm, b2)
P['Window_R'] = link('Window_R', b_door_gl)

# ---- tail: stab + elevators (hinge xd 6.19), fin + balanced rudder (hinge xd 6.255, horn above the fin) ---------------
# v4 tail check against two published 3-views (refs/j3-cub/: Commons J-3 3-view; P. Severin 3-view after Paul Matt /
# Piper J3 service manual). Both put the elevator hinge only ~0.05-0.08 m ahead of the rudder hinge (v3 had 0.205), so
# the stab/elevator group moves 0.14 m aft. Planform = Severin top view (half-span scaled to the owner's-manual 9 ft 6 in
# = 1.448 m): stab exposed ~15 sq ft, elevators ~11 sq ft (manual 15.1 / 11.7). Elevator inboard edge = the drawing's
# diagonal cut-out beside the rudder (table TE offsets at y 0.06-0.30).
H_S, Z_S, H_R = -6.19, 0.35, -6.255
ST = [(0.000, .684, .030), (0.045, .684, .040), (0.060, .684, .066), (0.100, .684, .154), (0.120, .684, .199),
      (0.150, .684, .259), (0.199, .677, .334), (0.249, .677, .384), (0.299, .670, .418), (0.399, .656, .465),
      (0.499, .643, .480), (0.598, .623, .481), (0.698, .596, .470), (0.798, .563, .453), (0.898, .525, .428),
      (0.997, .477, .395), (1.097, .423, .360), (1.197, .354, .316), (1.247, .315, .289), (1.296, .271, .261),
      (1.346, .213, .220), (1.376, .182, .190), (1.396, .152, .161), (1.416, .118, .134), (1.426, .095, .116),
      (1.436, .069, .086), (1.444, .047, .058), (1.448, 0.0, 0.0)]
S_S = 1.448
LES = lambda y: H_S + float(np.interp(abs(y), [r[0] for r in ST], [r[1] for r in ST]))
TES = lambda y: H_S - float(np.interp(abs(y), [r[0] for r in ST], [r[2] for r in ST]))
STB = Surf(LES, TES, lambda y: Z_S, 0.045, minc=0.01)
E_IN, E_PHI = 0.045, math.radians(28)     # e_cut only spaces the root sections; the ST table sets the cut
def e_cut(s): return H_R - 0.004 - max(abs(s) - E_IN, 0) / math.tan(E_PHI)      # elevator inboard edge (rudder swing room)
E_MEET = E_IN + (H_R - 0.004 - TES(0.4)) * math.tan(E_PHI)
ss = sorted(set([round(v, 4) for v in L(0.0, 0.10, 3) + L(0.14, 1.25, 12) + tipL(1.27, S_S - 0.001, 10)]))
P['Stab'] = surf_obj('Stab', STB, [-s for s in ss[::-1] if s > 0] + ss, x_to=lambda s: H_S + GAP)
for side, sg in (('L', 1), ('R', -1)):
    spn = L(E_IN + 0.003, E_MEET, 8) + L(E_MEET + 0.03, 1.25, 9) + tipL(1.27, S_S - 0.004, 9)
    P['Elevator_' + side] = surf_obj('Elevator_' + side, STB, [sg * s for s in spn], x_from=lambda s: H_S - GAP,
                                     x_to=lambda s: max(TES(s), e_cut(s)))
# v4: fin LE / rudder TE nudged to the mean of the two 3-views (LE ~0.02 fwd); lower rudder rounded per both drawings;
# rudder area 6.55 sq ft (manual 6.55), fin above the turtledeck ~4.2 (manual 4.7 incl. the part inside the fairing)
FLE = tbl([(0.40, 5.20), (0.47, 5.30), (0.50, 5.385), (0.53, 5.45), (0.58, 5.515), (0.63, 5.56), (0.80, 5.66), (1.00, 5.79), (1.11, 5.86)])
RTE = tbl([(0.045, 6.26), (0.05, 6.29), (0.06, 6.31), (0.08, 6.38), (0.10, 6.445), (0.12, 6.515), (0.14, 6.58), (0.16, 6.645),
           (0.18, 6.69), (0.20, 6.72), (0.24, 6.752), (0.28, 6.775), (0.32, 6.787), (0.40, 6.795), (0.465, 6.79), (0.60, 6.738),
           (0.80, 6.672), (1.00, 6.607), (1.11, 6.577), (1.19, 6.547), (1.26, 6.513), (1.32, 6.475), (1.36, 6.435), (1.39, 6.385),
           (1.41, 6.25)])
HLE = tbl([(1.11, 5.86), (1.20, 5.915), (1.30, 5.98), (1.35, 6.03), (1.38, 6.095), (1.40, 6.175), (1.41, 6.25)])
def fin_le(z): return -(FLE(z) if z <= 1.11 else HLE(z))
def fin_te(z): return -RTE(max(z, 0.045))
FIN = Surf(fin_le, fin_te, lambda z: 0.0, 0.065, axis='z', minc=0.01)
P['Fin'] = surf_obj('Fin', FIN, L(0.40, 1.105, 12), x_to=lambda z: H_R + GAP)
bmr = bmesh.new()
for zs, xf in ((L(0.045, 0.20, 8) + L(0.24, 1.115, 14), lambda z: H_R - GAP), (L(1.115, 1.409, 8), None)):
    secs = [FIN.section(z, None if xf is None else xf(z), None, 22) for z in zs]
    loft_rings(bmr, secs)
for sy in (1, -1):                                               # rudder horns (steering spring / cable attach)
    bar(bmr, [B(6.27, 0, 0.12), B(6.33, sy * 0.10, 0.12)], 0.04, 0.006)
bmesh.ops.recalc_face_normals(bmr, faces=bmr.faces); P['Rudder'] = link('Rudder', bmr)
bmw = bmesh.new()             # tail brace wires: AFT wires only (one over, one under each side) at the stab rear spar -
for sy in (1, -1):            # the J-3 trims with a jack-screw on the stab LE (no trim tab), so there are no forward wires
    for a, b in (((6.21, 0, 0.98), (6.16, sy * 0.95, Z_S + 0.015)),          # v4: follow the stab aft (rear spar xd 6.16)
                 ((6.16, sy * 0.95, Z_S - 0.015), (6.22, sy * 0.025, 0.04))):
        tube(bmw, [B(*a), B(*b)], 0.003, n=6)
P['Wire_Tail'] = link('Wire_Tail', bmw)

# ---- lift struts (V pair each side, front + rear spar) and jury struts --------------------------------------------------
bms = bmesh.new()
F_ST = (2.05, 0.325, -0.57); Y_ST = 3.40; J_F = 0.62; JURY = {}
for sy in (1, -1):
    root = np.array(B(F_ST[0], sy * F_ST[1], F_ST[2]))
    js = []
    for xs_ in (1.695, 2.40):
        tip = wsurf(W, sy * Y_ST, (xs_ - LE_D) / CH, False) + np.array((0, 0, 0.01))
        blade_strut(bms, root, tip, [0.085] * 6, 0.036)
        # v4 (Ty / NASM + museum photos): each jury strut runs from its lift strut straight up to ITS OWN spar (front strut
        # -> front spar, rear -> rear spar) at the same span station, and a cross tube ties the front and rear lift
        # struts together right at that jury station.
        j = root + J_F * (tip - root); jt = wsurf(W, j[1], (xs_ - LE_D) / CH, False) + np.array((0, 0, 0.008))
        tube(bms, [j, jt], 0.011, n=8); js.append(j)
        JURY[(sy, xs_)] = (np.round(j, 3), np.round(jt, 3))
    tube(bms, js, 0.012, n=8)                                     # front <-> rear lift-strut tie at the jury station
    tube(bms, [root + np.array((0, 0, 0.03)), np.array(B(F_ST[0], sy * (F_ST[1] - 0.03), F_ST[2] + 0.03))], 0.018, n=8)
P['Strut_Wing'] = link('Strut_Wing', bms)
for k_, v_ in JURY.items(): print('JURY', k_, 'strut pt', v_[0], '-> spar', v_[1])

# ---- engine: A-65 heads under black hoods, exhaust, nose bowl inlet, crank flange, fuel cap + float wire -------------
EN_T = []
EN_NAMES = ('cyl', 'head', 'rocker', 'hood', 'exh', 'case', 'dark', 'alu', 'steel', 'seam', 'plug', 'wire')
def en_add(bm, tag, fn):
    fn(); tag_new(bm, EN_NAMES.index(tag))
bme = bmesh.new()
HEADS = {1: (0.335, 0.475), -1: (0.39, 0.53)}      # Continental: left bank forward (#2/#4 left are ahead of #1/#3)
CZ = 0.0          # v5g: cylinders on the crankshaft (thrust) line, as on the A-65; hood/plate ratios then match Ty's rear photo
RIV, RIVP, HOLES = [], [], {}
VC_Y0, VC_Y1 = 0.356, 0.400            # valve cover inboard (head) / outboard face, from the head centreline
def vcover(hx, sy):
    """v5: polished (chrome-look) rocker cover per Ty's first eyebrow photo: rounded-rectangle box with a domed top, red
    gasket at the head joint and four hold-down screws (Ty's rear photo shows black lettered covers; polished kept)."""
    def rr(ins):
        a, b, rc = 0.060 - ins, 0.070 - ins, max(0.024 - ins * 0.6, 0.006); pts = []
        for qx, qz, t0 in ((a - rc, b - rc, 0), (-(a - rc), b - rc, 90), (-(a - rc), -(b - rc), 180), (a - rc, -(b - rc), 270)):
            for t in np.radians(np.linspace(t0, t0 + 90, 6)): pts.append((qx + rc * math.cos(t), qz + rc * math.sin(t)))
        return pts
    # v5h: pillow cover -- straight walls then a quarter-ellipse dome (Ty's yellow photo: rounded, domed pressed covers)
    def rr2(ins):
        a, b = 0.060 - ins, 0.070 - ins; rc = min(max(0.030 - ins * 0.3, 0.010), a - 0.001, b - 0.001); pts = []
        for qx, qz, t0 in ((a - rc, b - rc, 0), (-(a - rc), b - rc, 90), (-(a - rc), -(b - rc), 180), (a - rc, -(b - rc), 270)):
            for t in np.radians(np.linspace(t0, t0 + 90, 7)): pts.append((qx + rc * math.cos(t), qz + rc * math.sin(t)))
        return pts
    prof = [(VC_Y0 + 0.004, 0.0), (VC_Y1 - 0.028, 0.0)] + [(VC_Y1 - 0.028 + 0.036 * math.sin(th), 0.040 * (1 - math.cos(th)))
                                                         for th in np.radians(np.linspace(0, 90, 10))[1:]]
    rings = [[B(hx + px, sy * yy, CZ + pz) for px, pz in rr2(ins)] for yy, ins in prof]
    en_add(bme, 'rocker', lambda: loft_rings(bme, rings))
    en_add(bme, 'alu', lambda: loft_rings(bme, [[B(hx + px, sy * yy, CZ + pz) for px, pz in rr(-0.003)] for yy in (VC_Y0, VC_Y0 + 0.004)]))  # red gasket
    for dx, dz in ((0.042, 0.054), (-0.042, 0.054), (0.042, -0.054), (-0.042, -0.054)):
        en_add(bme, 'steel', lambda: cyl(bme, B(hx + dx, sy * (VC_Y1 - 0.012), CZ + dz), B(hx + dx, sy * (VC_Y1 - 0.003), CZ + dz), 0.0045, 8))
for sy in (1, -1):
    for hx in HEADS[sy]:
        en_add(bme, 'cyl', lambda: cyl(bme, B(hx, sy * 0.10, CZ), B(hx, sy * 0.245, CZ), 0.052, 20))
        for k in range(10):                                                     # head fins
            y0 = 0.245 + k * 0.0115
            en_add(bme, 'head', lambda: cyl(bme, B(hx, sy * y0, CZ), B(hx, sy * (y0 + 0.006), CZ), 0.066 if k % 2 == 0 else 0.062, 20))
            en_add(bme, 'head', lambda: cyl(bme, B(hx, sy * (y0 + 0.006), CZ), B(hx, sy * (y0 + 0.0115), CZ), 0.052, 20))
        vcover(hx, sy)                                                          # v5 polished valve (rocker) cover
        en_add(bme, 'steel', lambda: tube(bme, [B(hx + 0.01, sy * 0.13, CZ - 0.075), B(hx + 0.01, sy * 0.28, CZ - 0.07)], 0.008, n=8))
    # v5c eyebrow (Ty photos ty-eyebrows + ty-cowl-eyebrow-rear, matched side by side): ONE tall, smoothly ROUNDED black
    # hood per bank -- a half-ellipse arch from its inboard foot on the cowl (above the head opening) over to an outboard
    # lip just above the valve covers (covers stay exposed below). Open at the front with a rolled lip; the crown is
    # highest at the front and leads the raked front edge; the hood drops a little towards the back, where it closes
    # into a flat, riveted black plate exactly on the arch (no overhang/flare) that meets the cowl at the opening's aft edge.
    def eyebrow(sy=sy):
        # v5d: rounded-box section (superellipse quarter) -- the top rises off the cowl shoulder (foot zf on the cowl),
        # runs level outboard at the crown Hc, turns through a smooth rounded corner and drops as a near-vertical
        # outboard wall to the lip ZL just above the valve covers. Closed at the back by a flat riveted plate that
        # fills the whole arch down to the lip line and in to the cowl (no flare / no free edge).
        h0, h1 = HEADS[sy]; xf, xr = h0 - 0.090, HOOD[sy][1] + 0.010
        ZL, YO, T_, NE = 0.070, 0.346, 0.005, 2.0   # v5j: narrow half-tube over the fins; lip at the cover/head joint so the covers stand ~2.5 in proud
        xs = L(xf, xr, 16); NA = 22
        def frac(x_): return (x_ - xf) / (xr - xf)
        def zf_(x_): return 0.132 - 0.004 * frac(x_)
        def yf_(x_): return fus_y(min(max(x_, 0.19), 0.95), zf_(x_)) - 0.006
        def hc_(x_): return 0.205 - 0.035 * frac(x_) ** 1.2
        def sect(x_):                                   # v5j: half-tube (tilted half-ellipse) from the cowl foot over the fins to the lip
            yf, zf, Hc = yf_(x_), zf_(x_), hc_(x_); yo = max(YO, yf + 0.06); a = (yo - yf) / 2; ym = yf + a
            b = Hc - (zf + ZL) / 2; out = []
            for t in np.linspace(0, math.pi, NA):
                out.append((ym - a * math.cos(t), zf + (ZL - zf) * (1 - math.cos(t)) / 2 + b * math.sin(t)))
            return out
        def offs(pts, d):                               # inward offset along the 2-D section normal
            P_ = np.array(pts); out = []
            for k in range(len(P_)):
                tg = P_[min(k + 1, len(P_) - 1)] - P_[max(k - 1, 0)]; nrm = np.array((tg[1], -tg[0])); nrm /= np.linalg.norm(nrm)
                out.append(tuple(P_[k] + d * nrm))
            return out
        def shx(x_, p):                                 # rake: the lower wall trails the crown at the front, fades aft
            w = min(max((hc_(x_) - p[1]) / (hc_(x_) - ZL), 0), 1)
            return x_ + 0.070 * w * (1 - frac(x_)) ** 2
        S = [sect(x_) for x_ in xs]
        print('BROW', sy, 'foot y %.3f..%.3f' % (S[0][0][0], S[-1][0][0]), 'lip', S[0][-1], 'crown', max(q[1] for q in S[0]))
        if np.mean([q[1] for q in offs(S[0], T_)]) > np.mean([q[1] for q in S[0]]): T2 = -T_
        else: T2 = T_
        top = [[B(shx(x_, p), sy * p[0], p[1]) for p in S[a]] for a, x_ in enumerate(xs)]
        bot = [[B(shx(x_, p), sy * p[0], p[1]) for p in offs(S[a], T2)] for a, x_ in enumerate(xs)]
        vt = [[bme.verts.new(p_) for p_ in row] for row in top]; vb = [[bme.verts.new(p_) for p_ in row] for row in bot]
        nI, nJ = len(top), len(top[0]); fs = []
        for a_ in range(nI - 1):
            for b_ in range(nJ - 1):
                fs.append(bme.faces.new((vt[a_][b_], vt[a_ + 1][b_], vt[a_ + 1][b_ + 1], vt[a_][b_ + 1])))
                fs.append(bme.faces.new((vb[a_][b_], vb[a_][b_ + 1], vb[a_ + 1][b_ + 1], vb[a_ + 1][b_])))
        rim = [(a_, 0) for a_ in range(nI)] + [(nI - 1, b_) for b_ in range(1, nJ)] + [(a_, nJ - 1) for a_ in range(nI - 2, -1, -1)] + [(0, b_) for b_ in range(nJ - 2, 0, -1)]
        for k_ in range(len(rim)):
            (a0, b0), (a1, b1) = rim[k_], rim[(k_ + 1) % len(rim)]
            fs.append(bme.faces.new((vt[a0][b0], vb[a0][b0], vb[a1][b1], vt[a1][b1])))
        bmesh.ops.recalc_face_normals(bme, faces=fs)
        tube(bme, [top[0][k] for k in range(nJ)], 0.0045, n=8)          # rolled front lip
        # rear plate: fills the arch (foot -> crown -> lip) down to the lip line and in along the cowl profile; built as
        # a quad strip between the arch and that lower boundary (both run foot -> lip), two faces + rim, riveted
        arch = S[-1]; zf, yf = zf_(xr), yf_(xr)
        low = [(fus_y(xr, z_) - 0.004, z_) for z_ in np.linspace(zf, ZL, 7)] + [(y_, ZL) for y_ in np.linspace(fus_y(xr, ZL) - 0.004, YO, 9)[1:]]
        la = np.cumsum([0] + [math.dist(low[i], low[i + 1]) for i in range(len(low) - 1)]); la /= la[-1]
        aa = np.cumsum([0] + [math.dist(arch[i], arch[i + 1]) for i in range(len(arch) - 1)]); aa /= aa[-1]
        lowr = [(float(np.interp(t, la, [q[0] for q in low])), float(np.interp(t, la, [q[1] for q in low]))) for t in aa]
        pf = []
        for xp_, flip in ((xr - 0.0035, False), (xr + 0.0005, True)):
            va = [bme.verts.new(B(xp_, sy * y_, z_)) for y_, z_ in arch]; vl = [bme.verts.new(B(xp_, sy * y_, z_)) for y_, z_ in lowr]
            for i_ in range(len(arch) - 1):
                q = [va[i_], va[i_ + 1], vl[i_ + 1], vl[i_]]
                if i_ == 0: q = [va[0], va[1], vl[1]]
                elif i_ == len(arch) - 2: q = [va[i_], va[i_ + 1], vl[i_]]
                pf.append(bme.faces.new(q[::-1] if flip else q))
        bmesh.ops.recalc_face_normals(bme, faces=pf)
        poly = arch + lowr[::-1][1:-1]
        RIV.append((sy, xr + 0.0005, poly))
        for x_ in L(xf + 0.03, xr - 0.02, 9):                                  # rivets along the attach flange on the cowl
            p_ = sect(x_)[1]; RIVP.append((B(x_, sy * p_[0], p_[1] + 0.003), (0, 0, 1)))
    en_add(bme, 'hood', eyebrow)
    # v5 exhaust (Ty photo ty-cowl-eyebrow-rear): chrome/steel stack drops from each exhaust port, turns aft below the
    # heads, the two join and the collector runs aft out through a round hole in the lower cowl side (dark grommet ring).
    hf, hr = HEADS[sy]; XH = HOOD[sy][1] + 0.045; ZE = -0.158; PY = 0.300; yc_ = fus_y(XH, ZE)
    for hx_ in (hf, hr):
        en_add(bme, 'steel', lambda: tube(bme, [B(hx_ + 0.008, sy * PY, CZ - 0.055), B(hx_ + 0.008, sy * PY, ZE + 0.035), B(hx_ + 0.022, sy * PY, ZE + 0.006),
                                                 B(hx_ + 0.05, sy * PY, ZE), B(hr + 0.06, sy * PY, ZE)], 0.016, n=12))
        en_add(bme, 'dark', lambda: cyl(bme, B(hx_ + 0.008, sy * PY, CZ - 0.05), B(hx_ + 0.008, sy * PY, CZ - 0.065), 0.024, 12))   # port flange
    en_add(bme, 'steel', lambda: tube(bme, [B(hr + 0.04, sy * PY, ZE), B(XH - 0.03, sy * (PY - 0.008), ZE), B(XH, sy * (yc_ + 0.004), ZE),
                                             B(XH + 0.05, sy * (yc_ - 0.035), ZE - 0.004)], 0.020, n=14))
    en_add(bme, 'dark', lambda: cyl(bme, B(hr + 0.065, sy * PY, ZE), B(hr + 0.085, sy * PY, ZE), 0.0215, 14))       # slip-joint clamp
    _nrm = np.array((0, 0.01, -(fus_y(XH, ZE + 0.01) - yc_))); _nrm /= np.linalg.norm(_nrm)
    _c = np.array((XH + 0.012, yc_ - 0.0006, ZE))
    en_add(bme, 'dark', lambda: cyl(bme, B(_c[0], sy * _c[1], _c[2]), B(_c[0], sy * (_c[1] + 0.0025 * _nrm[1]), _c[2] + 0.0025 * _nrm[2]), 0.032, 24))
    HOLES[sy] = (XH + 0.012, ZE)
    if sy == -1:   # heat-darkened tail stub under the lower right cowl (judgement for the 'lower right' exhaust exit)
        xt = 0.74; zb_ = F_BOT(xt)
        en_add(bme, 'exh', lambda: tube(bme, [B(xt - 0.07, -0.175, zb_ + 0.07), B(xt - 0.01, -0.175, zb_ + 0.008), B(xt + 0.05, -0.18, zb_ - 0.04)], 0.020, n=12))
en_add(bme, 'case', lambda: rbox(bme, B(0.47, 0, CZ), (0.34, 0.20, 0.16)))
# v5c ignition (Ty request; Continental A-65: two shielded plugs per cylinder -- top plug on the upper front of the head,
# bottom plug under the head -- each with an elbow terminal; the harness leads run aft along the heads to the two
# magnetos on the accessory case at the back of the engine; yellow leads as in Ty's eyebrow photo)
MAGS = {1: B(0.70, 0.085, CZ + 0.035), -1: B(0.70, -0.085, CZ + 0.035)}
for sy_ in (1, -1):
    m_ = MAGS[sy_]
    en_add(bme, 'dark', lambda: cyl(bme, (m_[0] + 0.04, m_[1], m_[2]), (m_[0] - 0.06, m_[1], m_[2]), 0.036, 16))   # magneto
    en_add(bme, 'alu', lambda: cyl(bme, (m_[0] - 0.06, m_[1], m_[2]), (m_[0] - 0.075, m_[1], m_[2]), 0.030, 16))    # distributor cap
for sy_ in (1, -1):
    for hx_ in HEADS[sy_]:
        for k_, (dx_, dz_, nx_, nz_) in enumerate(((-0.040, 0.058, -0.45, 0.89), (-0.040, -0.060, -0.45, -0.89))):
            y_ = 0.300; base_ = np.array(B(hx_ + dx_, sy_ * y_, CZ + dz_)); nrm_ = np.array((-nx_, 0.0, nz_))   # B flips x
            tip_ = base_ + nrm_ * 0.026; elb_ = tip_ + nrm_ * 0.010
            en_add(bme, 'plug', lambda: cyl(bme, tuple(base_), tuple(tip_), 0.0095, 6))             # hex body / shielding
            en_add(bme, 'steel', lambda: cyl(bme, tuple(tip_), tuple(elb_), 0.0075, 10))           # elbow nut
            mag_ = MAGS[sy_]; tgt_ = np.array((mag_[0] - 0.075, mag_[1] + sy_ * (0.012 * k_ - 0.006), mag_[2]))
            if k_ == 0:      # top lead: up out of the elbow, aft over the head fins under the hood, in to the magneto
                pts_ = [elb_, elb_ + np.array((0, 0, 0.012)), np.array(B(hx_ + 0.01, sy_ * 0.290, CZ + 0.095)),
                        np.array(B(hx_ + 0.04, sy_ * 0.235, CZ + 0.080)), np.array(B(0.64, sy_ * 0.15, CZ + 0.06)), tgt_]   # v5j: inboard into the cowl inside the opening
            else:            # bottom lead: down, then aft in a loose loop just below the valve covers (Ty photo), in to the mag
                pts_ = [elb_, elb_ + np.array((0, 0, -0.010)), np.array(B(hx_ - 0.02, sy_ * 0.345, CZ - 0.105)),
                        np.array(B(hx_ + 0.02, sy_ * 0.385, CZ - 0.088)), np.array(B(hx_ + 0.07, sy_ * 0.370, CZ - 0.100)), np.array(B(0.58, sy_ * 0.30, CZ - 0.10)),
                        np.array(B(0.64, sy_ * 0.16, CZ - 0.02)), tgt_]
            for _ in range(3):                                                  # v5h: Chaikin-smoothed leads (soft loops)
                pts_ = [pts_[0]] + [q for a_, b_ in zip(pts_, pts_[1:]) for q in (0.75 * np.asarray(a_) + 0.25 * np.asarray(b_), 0.25 * np.asarray(a_) + 0.75 * np.asarray(b_))] + [pts_[-1]]
            en_add(bme, 'wire', lambda: tube(bme, [tuple(q) for q in pts_], 0.0052, n=8))

en_add(bme, 'steel', lambda: cyl(bme, B(0.14, 0, 0), B(0.19, 0, 0), 0.055, 20))
en_add(bme, 'dark', lambda: rbox(bme, B(0.197, 0, -0.215), (0.012, 0.11, 0.06), Matrix.Rotation(math.radians(-40), 4, 'Y')))
# v5 eyebrow rear-plate rivets (Ty photo ty-cowl-eyebrow-rear)
for sy_, xp_, poly_ in RIV:
    pp = np.array(poly_); c_ = pp.mean(0); seg_ = list(zip(pp, np.roll(pp, -1, 0)))
    for a_, b_ in seg_:
        n_ = max(1, int(np.linalg.norm(b_ - a_) / 0.026))
        for t_ in np.linspace(0, 1, n_, endpoint=False):
            q_ = a_ + (b_ - a_) * t_; q_ = q_ + (c_ - q_) / np.linalg.norm(c_ - q_) * 0.009
            en_add(bme, 'hood', lambda: cyl(bme, B(xp_, sy_ * q_[0], q_[1]), B(xp_ + 0.0022, sy_ * q_[0], q_[1]), 0.0028, 6, r2=0.0016))
for p_, n_ in RIVP:
    en_add(bme, 'hood', lambda: cyl(bme, p_, (p_[0], p_[1], p_[2] + 0.002), 0.0028, 6, r2=0.0016))
# v5 cowl panel lines (Ty photo ty-cowl-eyebrow-rear): raised dark hairline rings at the cowl / fuselage joint and the nose
# bowl joint, plus the top-centre split; the side split (lower cowl) is painted into the side maps below.
def seam_ring(xd, w_=0.004, off=0.0010):
    pr = np.array(ring(xd)); c_ = pr.mean(0); rs = []
    for dx in (-w_ / 2, w_ / 2):
        q = []
        for p in pr:
            d = p - c_; d[0] = 0; q.append(tuple(np.array((-(xd + dx), p[1], p[2])) + d / np.linalg.norm(d) * off))
        rs.append(q)
    loft_rings(bme, rs, False, False)
for xs_ in (0.787, 0.262): en_add(bme, 'seam', lambda: seam_ring(xs_))
en_add(bme, 'seam', lambda: bar(bme, [B(x_, 0, F_TOP(x_) + 0.0008) for x_ in L(0.262, 0.787, 12)], 0.004, 0.0012))
# v5 Dzus quarter-turn fasteners: black round heads with the steel wire spring clip, along the cowl edges
DZ = [(0.770, z_) for z_ in (-0.40, -0.28, -0.16, -0.04, 0.08)] + [(x_, -0.222) for x_ in (0.34, 0.50)] + [(0.285, z_) for z_ in (-0.24, -0.13)]
def dzus(xd, z, sy):
    y = fus_y(xd, z); y2 = fus_y(xd, z + 0.01); n_ = np.array((0, 0.01 * sy, -(y2 - y))); n_ /= np.linalg.norm(n_)
    p = np.array(B(xd, sy * y, z)) + n_ * 0.0004; e1 = np.array((1.0, 0, 0)); e2 = np.cross(n_, e1)
    en_add(bme, 'dark', lambda: cyl(bme, p, p + n_ * 0.003, 0.0105, 16))
    sl = [p + n_ * 0.0031 + e2 * 0.007, p + n_ * 0.0031 - e2 * 0.007]
    en_add(bme, 'steel', lambda: tube(bme, sl, 0.0011, n=5))
    wl = [p + n_ * 0.0018 + 0.0145 * (math.cos(t) * e1 + math.sin(t) * e2) for t in np.radians(np.linspace(-110, 110, 12))]
    en_add(bme, 'steel', lambda: tube(bme, wl, 0.0011, n=5))
for sy in (1, -1):
    for xd_, z_ in DZ: dzus(xd_, z_, sy)
# v4 (Ty): red fuel cap on the nose-tank filler ahead of the windshield + the float-gauge wire, ~8 in above the cap with a
# 90-degree bend at the top (bend points aft, toward the pilot: judgement)
ZCAP = F_TOP(0.85)
en_add(bme, 'alu', lambda: cyl(bme, B(0.85, 0, ZCAP - 0.006), B(0.85, 0, ZCAP + 0.016), 0.037, 24))      # cap body
en_add(bme, 'alu', lambda: cyl(bme, B(0.85, 0, ZCAP + 0.016), B(0.85, 0, ZCAP + 0.024), 0.031, 24, r2=0.024))  # domed crown
en_add(bme, 'alu', lambda: rbox(bme, B(0.85, 0, ZCAP + 0.028), (0.012, 0.050, 0.012)))                   # wing grip
en_add(bme, 'steel', lambda: cyl(bme, B(0.85, 0, ZCAP + 0.03), B(0.85, 0, ZCAP + 0.055), 0.005, 10))     # wire guide
WIRE_TOP = ZCAP + 0.024 + 0.203
en_add(bme, 'dark', lambda: tube(bme, [B(0.85, 0, ZCAP + 0.05), B(0.85, 0, WIRE_TOP - 0.01), B(0.853, 0, WIRE_TOP - 0.001),
                                       B(0.862, 0, WIRE_TOP), B(0.89, 0, WIRE_TOP)], 0.002, n=6))
print('FUELCAP top z %.3f, wire top z %.3f (%.1f in above the cap)' % (ZCAP + 0.024, WIRE_TOP, 0.203 / 0.0254))
EN_T = [EN_NAMES[t] for t in tags_of(bme)]; P['Engine'] = link('Engine', bme)

# ---- prop: 72 in fixed-pitch wood (TCDS A-691: Sensenich / McCauley 72 in wood props for the A-65), no spinner -------
# v5 (Ty photo refs/j3-cub/ty-prop.png): Sensenich-type planform -- narrow neck off a round hub, widest ~0.7 R, elliptical
# rounded tip; natural birch laminations under amber varnish (texture j3_prop_wood.png, planar UV in the disc plane);
# brass leading-edge sheath from ~0.42 R round the tip with a rivet row just behind it on both faces; red tips (12.4 in
# band, both faces); 6-bolt hub, no faceplate. The maker's decal is a trademark -> generic text-free oval in the texture.
# 42 in pitch (judgement within the TCDS pitch range), clockwise from the cockpit (Continental).
X_P, R_P, PITCH = -0.09, 36 * IN, 42 * IN
CHT = [(0.0, 0.098), (0.06, 0.080), (0.16, 0.074), (0.30, 0.090), (0.45, 0.108), (0.60, 0.119), (0.72, 0.121), (0.82, 0.114)]
SH_R0, SH_W = 0.42 * R_P, 0.014
def chord(q):
    if q <= 0.82: return float(np.interp(q, [a for a, _ in CHT], [b for _, b in CHT]))
    return max(0.114 * math.sqrt(max(0.0, 1 - ((q - 0.82) / 0.18) ** 3)), 0.010)   # v5b blunter round tip
def p_sec(r):
    q = (r - 0.07) / (R_P - 0.07); c = chord(q)
    beta = math.atan(PITCH / (2 * math.pi * max(r, 0.22))); th = (0.60 - 0.5 * q / 0.2) * c if q < 0.2 else max((0.15 - 0.09 * q) * c, 0.006)
    if q < 0.05: c, th, beta = 0.105, 0.10, 0.0
    return c, th, beta
def p_angles(c):
    a_s = math.acos(max(-0.9, 1 - 2 * SH_W / c))
    half = [0.0, a_s / 2, a_s] + list(np.linspace(a_s, math.pi, 9)[1:])
    return half + [2 * math.pi - a for a in half[1:-1][::-1]]
bmp = bmesh.new(); PRIV = []
for blade in range(2):
    ph = math.pi / 2 + blade * math.pi
    rad = np.array([0, math.sin(ph), math.cos(ph)]); tan = np.cross([1.0, 0, 0], rad)
    ax = np.array([1.0, 0, 0]); hub = np.array([X_P, 0, 0])
    rr = sorted(set([round(x_, 5) for x_ in np.linspace(0.07, R_P, 28)] + [round(R_P * (0.82 + 0.18 * (1 - (1 - t_) ** 1.6)), 5) for t_ in np.linspace(0.05, 0.97, 12)] + [round(SH_R0, 5)])); secs = []
    for r in rr:
        c, th, beta = p_sec(r); cd = math.cos(beta) * tan + math.sin(beta) * ax; nd = -math.sin(beta) * tan + math.cos(beta) * ax
        secs.append([bmp.verts.new(hub + r * rad + 0.5 * c * math.cos(a) * cd + 0.5 * th * math.sin(a) * nd) for a in p_angles(c)])
    for k in range(len(secs) - 1):
        rm = 0.5 * (rr[k] + rr[k + 1])
        for i in range(20):
            j = (i + 1) % 20; bmp.faces.new((secs[k][i], secs[k][j], secs[k + 1][j], secs[k + 1][i]))
            tag_new(bmp, 1 if (i in (0, 1, 18, 19) and rm > SH_R0) or rm > R_P - 0.012 else 0)
    bmp.faces.new(secs[-1]); tag_new(bmp, 1); bmp.faces.new(secs[0][::-1]); tag_new(bmp, 0)
    for r in np.arange(SH_R0 + 0.015, R_P - 0.03, IN):                     # sheath rivet row, both faces
        c, th, beta = p_sec(r); cd = math.cos(beta) * tan + math.sin(beta) * ax; nd = -math.sin(beta) * tan + math.cos(beta) * ax
        u = 0.5 * c - 0.010; a_ = math.acos(u / (0.5 * c))
        for sg_ in (1, -1):
            pt_ = hub + r * rad + u * cd + sg_ * 0.5 * th * math.sin(a_) * nd
            cyl(bmp, pt_ - sg_ * nd * 0.0004, pt_ + sg_ * nd * 0.0010, 0.0017, 6, r2=0.0011); tag_new(bmp, 1)
cyl(bmp, (X_P - 0.05, 0, 0), (X_P + 0.05, 0, 0), 0.088, 32); tag_new(bmp, 0)                 # wood hub boss
cyl(bmp, (X_P + 0.0495, 0, 0), (X_P + 0.0505, 0, 0), 0.026, 20); tag_new(bmp, 3)             # crank bore (dark)
cyl(bmp, (X_P + 0.046, 0, 0), (X_P + 0.0515, 0, 0), 0.016, 12); tag_new(bmp, 2)              # crank nose
for k in range(6):                                                                          # 6-bolt hub: holes + bolt heads
    t = math.radians(30 + 60 * k); y_, z_ = 0.042 * math.cos(t), 0.042 * math.sin(t)
    cyl(bmp, (X_P + 0.0496, y_, z_), (X_P + 0.0506, y_, z_), 0.0095, 12); tag_new(bmp, 3)
    cyl(bmp, (X_P + 0.0500, y_, z_), (X_P + 0.0565, y_, z_), 0.0072, 6); tag_new(bmp, 2)
    cyl(bmp, (X_P - 0.0506, y_, z_), (X_P - 0.0496, y_, z_), 0.0095, 12); tag_new(bmp, 3)    # back face: bolt holes
bmesh.ops.recalc_face_normals(bmp, faces=bmp.faces); face_tag = tags_of(bmp); P['Prop'] = link('Prop', bmp)
_uv = P['Prop'].data.uv_layers.new(name='UVMap')
for lp in P['Prop'].data.loops:
    co = P['Prop'].data.vertices[lp.vertex_index].co; _uv.data[lp.index].uv = ((co.y + R_P) / (2 * R_P), (co.z + 0.10) / 0.20)

# ---- cabin: floor, two seats in tandem, two sticks, pedals, panel with five gauges + compass, window frame tubes -------
CK_T = ['floor', 'seat', 'panel', 'gauge', 'face', 'stick', 'tubeY', 'dark', 'chrome', 'alu']
bmc = bmesh.new(); ck_tag = []
def ck(tag, fn):
    fn(); tag_new(bmc, CK_T.index(tag))
def floor():                                                                                 # floorboards follow the belly
    rs = []
    for xd in L(0.86, 2.84, 12):
        zf = F_BOT(xd) + 0.035; hw = fus_y(xd, zf + 0.012) - 0.012
        rs.append([B(xd, hw, zf), B(xd, -hw, zf), B(xd, -hw, zf + 0.012), B(xd, hw, zf + 0.012)])
    loft_rings(bmc, rs)
ck('floor', floor)
def firewall():                                                                              # firewall: the skin section, shrunk
    pr = np.array(ring(0.80)); c = pr.mean(0); pr = c + (pr - c) * np.array([1, 0.985, 0.985])
    bmc.faces.new([bmc.verts.new(p) for p in pr]); bmc.faces.new([bmc.verts.new(p) for p in pr[::-1]])
ck('dark', firewall)
# v5 instrument panel (Ty photo refs/j3-cub/ty-panel.png): black sheet panel, straight bottom, short vertical sides and a
# low arched top; one row of five instruments L->R: tach (chrome bezel), ASI, ball compass, altimeter (black bezels), oil
# temp / pressure (chrome bezel); 'REAR SEAT FOR SOLO FLYING' placard under the compass, small placard top centre, black
# knob upper left, chrome primer lower right, chrome screws round the edge. Faces from j3_panel_atlas.png (Panel_Gauges).
PX, PZB, PHW, PHS, PHC = 1.23, -0.012, 0.28, 0.080, 0.217   # v5f: top raised to the tank deck (.205)
PXA = PX + 0.003                                                     # aft (cockpit) face of the panel
def panel_outline(ins=0.0):
    pts = [(-PHW + ins, PZB + ins), (PHW - ins, PZB + ins)]
    for t in np.linspace(0, math.pi, 25): pts.append(((PHW - ins) * math.cos(t), PZB + PHS + (PHC - PHS - ins) * math.sin(t)))
    return pts
def panel_plate():
    po = panel_outline(); vf = [bmc.verts.new(B(PX - 0.003, y, z)) for y, z in po]; vb = [bmc.verts.new(B(PXA, y, z)) for y, z in po]
    fs = [bmc.faces.new(vf), bmc.faces.new(vb[::-1])]
    for i in range(len(po)):
        k = (i + 1) % len(po); fs.append(bmc.faces.new((vf[i], vb[i], vb[k], vf[k])))
    bmesh.ops.recalc_face_normals(bmc, faces=fs)
ck('panel', panel_plate)
def tank_deck():                                                    # v5f: fuel-tank top between the windshield base and the panel
    rs = []
    for xd in L(0.95, PX - 0.002, 5):
        f = (xd - 0.95) / (PX - 0.002 - 0.95); row = []
        for y in np.linspace(-PHW, PHW, 15):
            za = PZB + PHS + (PHC - PHS) * math.sqrt(max(0.0, 1 - (y / PHW) ** 2)) - 0.004
            row.append(B(xd, y, za))
        rs.append(row)
    for i in range(len(rs) - 1):
        for j in range(len(rs[i]) - 1):
            q = [bmc.verts.new(p) for p in (rs[i][j], rs[i][j + 1], rs[i + 1][j + 1], rs[i + 1][j])]
            bmc.faces.new(q); bmc.faces.new([bmc.verts.new(v.co) for v in q[::-1]])
ck('panel', tank_deck)
ZG = PZB + 0.110
GAUGES = [(0.164, 0.043, 'chrome', 0.036), (0.075, 0.0355, 'gauge', 0.030), (-0.011, 0.0355, 'gauge', 0.027),
          (-0.095, 0.0355, 'gauge', 0.030), (-0.181, 0.041, 'chrome', 0.035)]
def circ(y0, z0, r, n=32): return [(y0 + r * math.cos(t), z0 + r * math.sin(t)) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)]
for gy, rb, bt, rf in GAUGES:
    rings_ = [[B(xx, y, z) for y, z in circ(gy, ZG, rr_)] for xx, rr_ in ((PXA, rb), (PXA + 0.007, rb - 0.001), (PXA + 0.009, rb - 0.004),
                                                                        (PXA + 0.008, rf + 0.002), (PXA + 0.003, rf + 0.001))]
    ck(bt, lambda: loft_rings(bmc, rings_, False, False))
for gy, rb, bt, rf in GAUGES:                                                                    # v5b gauge mounting screws
    for t_ in (45, 135, 225, 315):
        y_, z_ = gy + (rb + 0.007) * math.cos(math.radians(t_)), ZG + (rb + 0.007) * math.sin(math.radians(t_))
        ck('chrome', lambda: cyl(bmc, B(PXA, y_, z_), B(PXA + 0.002, y_, z_), 0.0028, 8, r2=0.002))
ck('alu', lambda: rbox(bmc, B(PXA + 0.001, 0, PZB + 0.172), (0.002, 0.034, 0.015)))          # small placard, top centre
ck('dark', lambda: cyl(bmc, B(PXA, 0.215, PZB + 0.140), B(PXA + 0.024, 0.215, PZB + 0.140), 0.014, 16))      # knob, upper left
ck('chrome', lambda: cyl(bmc, B(PXA, -0.240, PZB + 0.041), B(PXA + 0.014, -0.240, PZB + 0.041), 0.007, 12))   # primer barrel
ck('chrome', lambda: cyl(bmc, B(PXA + 0.014, -0.240, PZB + 0.041), B(PXA + 0.024, -0.240, PZB + 0.041), 0.012, 16))  # primer knob
_po = np.array(panel_outline(0.013)); _sl = np.r_[0, np.cumsum(np.linalg.norm(np.diff(np.vstack([_po, _po[:1]]), axis=0), axis=1))]
for s_ in np.linspace(0, _sl[-1], 16, endpoint=False):                                                # chrome screws
    y_ = np.interp(s_, _sl, np.r_[_po[:, 0], _po[0, 0]]); z_ = np.interp(s_, _sl, np.r_[_po[:, 1], _po[0, 1]])
    ck('chrome', lambda: cyl(bmc, B(PXA, y_, z_), B(PXA + 0.0025, y_, z_), 0.0042, 8, r2=0.003))
# gauge faces + placard: separate object with UVs into the panel atlas (2560x640: five 512 px faces + placard strip)
bmg = bmesh.new(); _uvg = bmg.loops.layers.uv.new('UVMap'); AW, AH = 2560.0, 640.0
def uvface(vu):
    f = bmg.faces.new([bmg.verts.new(p) for p, _ in vu])
    for l_, (_, uv) in zip(f.loops, vu): l_[_uvg].uv = uv
for i, (gy, rb, bt, rf) in enumerate(GAUGES):
    xf_ = PXA + (0.007 if i == 2 else 0.004); cu = (i * 512 + 256) / AW
    rim_ = [(B(xf_, gy + rf * math.cos(t), ZG + rf * math.sin(t)), ((i * 512 + 256 - 254 * math.cos(t)) / AW, 1 - (256 - 254 * math.sin(t)) / AH))
            for t in np.linspace(0, 2 * math.pi, 32, endpoint=False)]
    for k in range(32): uvface([(B(xf_ + (0.002 if i == 2 else 0), gy, ZG), (cu, 1 - 256 / AH)), rim_[(k + 1) % 32], rim_[k]])
_pw, _ph, _py, _pz = 0.071, 0.0142, -0.011, PZB + 0.037
uvface([(B(PXA + 0.0008, _py + _pw / 2, _pz + _ph / 2), (0, 1 - 512 / AH)), (B(PXA + 0.0008, _py + _pw / 2, _pz - _ph / 2), (0, 0.0)),
        (B(PXA + 0.0008, _py - _pw / 2, _pz - _ph / 2), (640 / AW, 0.0)), (B(PXA + 0.0008, _py - _pw / 2, _pz + _ph / 2), (640 / AW, 1 - 512 / AH))])
P['Panel_Gauges'] = link('Panel_Gauges', bmg)
def round_back(bm, xc, zb, ht, sw, th=0.05, lean=0.10):
    r = sw / 2; zc = zb + ht - r
    prof = [(-r, zb), (r, zb)] + [(r * math.cos(t), zc + r * math.sin(t)) for t in np.linspace(0, math.pi, 19)]
    vf = [bm.verts.new(B(xc - th / 2 + lean * (z - zb), y, z)) for y, z in prof]
    vb = [bm.verts.new(B(xc + th / 2 + lean * (z - zb), y, z)) for y, z in prof]
    fs = [bm.faces.new(vf), bm.faces.new(vb[::-1])]
    for i in range(len(prof)):
        k = (i + 1) % len(prof); fs.append(bm.faces.new((vf[i], vb[i], vb[k], vf[k])))
    bmesh.ops.recalc_face_normals(bm, faces=fs)
for x0, x1, zb, zt in ((1.68, 2.05, -0.36, 0.05), (2.45, 2.82, -0.40, 0.18)):            # front seat / rear bench seat
    # v4: black seats; front seat 14 in wide (owner's manual) with a round-topped back ~0.50 m tall, slight recline
    sw = 0.36 if x0 < 2 else min(0.58, 2 * (min(fus_y(x1 + 0.06, zb - 0.04), fus_y(x1 + 0.06, zb + 0.04)) - 0.02))
    ck('seat', lambda: rbox(bmc, B((x0 + x1) / 2, 0, zb), (x1 - x0, sw, 0.08)))
    if x0 < 2: ck('seat', lambda: round_back(bmc, x1 + 0.03, zb - 0.04, 0.50, sw))
    else: ck('seat', lambda: rbox(bmc, B(x1 + 0.03, 0, (zb + zt) / 2 + 0.03), (0.06, sw, zt - zb)))
    zf = F_BOT(x1) + 0.05; ck('dark', lambda: rbox(bmc, B((x0 + x1) / 2, 0, (zb + zf) / 2), (x1 - x0 - 0.05, min(0.40, sw - 0.04), zb - zf)))
for sx in (1.55, 2.30):
    ck('stick', lambda: tube(bmc, [B(sx, 0, F_BOT(sx) + 0.04), B(sx - 0.03, 0, -0.10), B(sx - 0.05, 0, 0.02)], 0.011, n=8))
    ck('dark', lambda: cyl(bmc, B(sx - 0.05, 0, -0.02), B(sx - 0.055, 0, 0.06), 0.017, 10))
for px in (1.12, 1.85):
    for sy in (1, -1):
        ck('stick', lambda: bar(bmc, [B(px, sy * 0.13, -0.53), B(px - 0.04, sy * 0.13, -0.42)], 0.07, 0.01, up=(1, 0, 0.4)))
for sy in (1, -1):                                                                          # frame tubes seen in the windows
    for a, b in (((0.96, 0.195), (1.53, 0.52)), ((1.23, 0.03), (1.56, 0.515)), ((1.665, 0.03), (1.665, 0.50))):
        ck('tubeY', lambda: tube(bmc, [B(a[0], sy * (fus_y(a[0], a[1]) - 0.004), a[1]), B(b[0], sy * (fus_y(b[0], b[1]) - 0.004), b[1])], 0.011, n=8))
    if sy > 0: ck('tubeY', lambda: tube(bmc, [B(1.70, sy * (HW_CAB - 0.012), 0.03), B(2.47, sy * (HW_CAB - 0.012), 0.03)], 0.01, n=8))   # v5f: none across the door
    if sy > 0: ck('dark', lambda: rbox(bmc, B(1.60, 0.27, -0.12), (0.05, 0.02, 0.03)))  # throttle (left wall)
# v5c interior (period J-3 cockpit, On Centerline J3L-65 style as described; geometry by judgement): exposed yellow
# 4130 tube truss on both sidewalls inside the fabric (lower longeron, verticals, diagonals), heel-brake pedals behind
# the rudder pedals front and rear, the stabilizer trim crank on the left wall by the rear seat, floor-board battens.
for sy in (1, -1):
    ZU, ZLo = 0.03, -0.40
    st_ = [1.23, 1.665, 2.06, 2.47, 2.90]
    def wall(x_, z_): return B(x_, sy * (fus_y(x_, z_) - 0.016), z_)
    ck('tubeY', lambda: tube(bmc, [wall(x_, ZLo) for x_ in L(1.23, 2.90, 8)], 0.010, n=8))          # lower longeron
    ck('tubeY', lambda: tube(bmc, [wall(x_, ZU) for x_ in L(1.23, 1.70, 3)], 0.010, n=8))           # upper longeron fwd
    for x_ in st_[1:]:
        if sy < 0 and x_ == 2.06: continue                                                       # v5f: door opening clear (d01)
        ck('tubeY', lambda: tube(bmc, [wall(x_, ZLo), wall(x_, -0.18), wall(x_, ZU)], 0.009, n=8))  # verticals
    for xa_, xb_ in zip(st_[:-1], st_[1:]):
        if sy < 0 and 1.6 < xa_ < 2.3: continue                                                    # v5f: door opening clear (d01)
        ck('tubeY', lambda: tube(bmc, [wall(xa_, ZU), wall(0.5 * (xa_ + xb_), -0.185), wall(xb_, ZLo)], 0.008, n=8))   # diagonals
for px in (1.12, 1.85):                                                                     # heel brakes (black pads)
    for sy in (1, -1):
        ck('dark', lambda: bar(bmc, [B(px + 0.11, sy * 0.13, -0.545), B(px + 0.08, sy * 0.13, -0.49)], 0.05, 0.010, up=(1, 0, 0.6)))
        ck('stick', lambda: cyl(bmc, B(px + 0.115, sy * 0.13, -0.56), B(px + 0.115, sy * 0.13, -0.53), 0.006, 6))
_tc = B(2.38, 0.27, -0.02)                                                                  # trim crank, left wall
ck('dark', lambda: cyl(bmc, _tc, (_tc[0], _tc[1] - 0.025, _tc[2]), 0.045, 20))
ck('chrome', lambda: bar(bmc, [(_tc[0], _tc[1] - 0.026, _tc[2]), (_tc[0], _tc[1] - 0.026, _tc[2] + 0.055)], 0.012, 0.006, up=(0, -1, 0)))
ck('dark', lambda: cyl(bmc, (_tc[0], _tc[1] - 0.026, _tc[2] + 0.05), (_tc[0], _tc[1] - 0.065, _tc[2] + 0.05), 0.009, 8))
ck_tag = tags_of(bmc); P['Cabin'] = link('Cabin', bmc)
TAGS = {}

# ---- main gear (wheels): Univair J-3 parts list layout ----------------------------------------------------------------
# Each side: a V of two legs (front + rear) pinned to the fuselage lower longeron fittings -> the wheel swings about the
# line through those two pins. Under the belly an apex fitting hangs on an inverted V of tubes from the front fittings;
# the shock strut (bungee cords inside the black boot) runs from the apex out to the axle end. As the wheel rises the
# strut EXTENDS (the cords are stretched), so 'stroke' here is strut extension.
# 8.00-4 tyre (TCDS A-691 'Wheels and tires'; 8.00-4 is the low-pressure option on 4 in wheels): 17.6 in OD, 8 in section.
GEAR_SIDES = {}; MOV = {}
if VAR == 'wheels':
    R_W, W_W = 0.2235, 0.203
    WC = np.array(B(1.48, 0.92, -1.11))
    G_H = np.array(B(1.46, 0.32, -0.625)); G_C = np.array(B(2.04, 0.32, -0.605))     # leg pins (front = pivot, rear)
    G_A = np.array(B(1.48, 0.80, -1.11))                                               # axle end
    # apex pin of the X (shock struts + inverted centre V) lies IN THE PLANE OF THE FRONT LEGS (Ty v3 review): put it on
    # the front-leg line H -> A+(0,0,0.02) at the apex height, so struts, centre V and front legs share one plane
    _Z_T = -0.80; _A2 = G_A + np.array((0.0, 0, 0.02)); _t = (_Z_T - G_H[2]) / (_A2[2] - G_H[2])
    G_T = np.array((G_H[0] + _t * (_A2[0] - G_H[0]), B(0, 0.05, 0)[1], _Z_T))           # apex pin
    WHEEL_RISE = 0.10                    # nominal full travel at the wheel (not published; for Ty to confirm)
    G_U = (G_C - G_H) / np.linalg.norm(G_C - G_H)
    def g_rot(p, th):
        u = G_U; h = G_H; v = np.array(p, float) - h
        return h + v * math.cos(th) + np.cross(u, v) * math.sin(th) + u * np.dot(u, v) * (1 - math.cos(th))
    _s = 1.0 if g_rot(G_A, 0.05)[2] > G_A[2] else -1.0
    def g_len(th): return float(np.linalg.norm(G_T - g_rot(G_A, _s * th)))
    def g_wheel(th): return g_rot(WC, _s * th)
    L_EXT = g_len(0.0)
    lo, hi = 0.0, 0.5
    for _ in range(60):
        mid = 0.5 * (lo + hi)
        if g_wheel(mid)[2] - WC[2] < WHEEL_RISE: lo = mid
        else: hi = mid
    TH_FULL = 0.5 * (lo + hi); STROKE = g_len(TH_FULL) - L_EXT
    print('GEAR L_ext %.3f m  wheel rise %.3f -> hinge %.2f deg, strut extension %+.3f m (%.2f in), wheel dy %+.3f'
          % (L_EXT, WHEEL_RISE, math.degrees(TH_FULL), STROKE, STROKE / IN, g_wheel(TH_FULL)[1] - WC[1]))
    WP = [(0.060, 0.062), (0.083, 0.085), (0.097, 0.115), (0.1015, 0.145), (0.097, 0.175), (0.085, 0.198), (0.062, 0.215),
          (0.030, 0.2225), (0.0, 0.2235)]
    WP = [(-d, r) for d, r in WP] + [(d, r) for d, r in WP[::-1][1:]]
    bmf = bmesh.new()                                                   # fixed: inverted V to the apex + strut boots
    for sy in (1, -1):
        m = np.array((1, sy, 1.0)); T = G_T * m
        tube(bmf, [tuple(T), B(1.46, sy * 0.30, -0.62)], 0.014, n=8)
        tube(bmf, [tuple(T), B(2.04, sy * 0.30, -0.61)], 0.013, n=8)
    cyl(bmf, (G_T[0], 0.07, G_T[2]), (G_T[0], -0.07, G_T[2]), 0.022, 12)
    print('GEAR X apex in front-leg plane: xd %.4f z %.3f' % (-G_T[0], G_T[2]))
    P['Gear_Main'] = link('Gear_Main', bmf)
    for side, sy in (('L', 1), ('R', -1)):
        m = np.array((1, sy, 1.0)); H, C, T, A, Wc = G_H * m, G_C * m, G_T * m, G_A * m, WC * m
        bm = bmesh.new()                                                # V legs + axle stub (rigid, swings about H-C)
        tube(bm, [tuple(H), tuple(A + np.array((0.0, 0, 0.02)))], 0.016, 10)
        tube(bm, [tuple(C), tuple(A + np.array((-0.03, 0, 0.0)))], 0.016, 10)
        tube(bm, [tuple(A - np.array((0, sy * 0.02, 0))), tuple(Wc + np.array((0, sy * 0.07, 0)))], 0.017, 10)
        cyl(bm, tuple(A + np.array((0.02, 0, 0))), tuple(A - np.array((0.05, 0, 0))), 0.022, 10)
        # sheet-metal vee fairing filling the V between the front and rear legs (photos d01, c34, c37, c47)
        _ka = A + np.array((-0.012, 0, 0.035)); _tri = [H + np.array((0.0, 0, -0.005)), C + np.array((0.0, 0, -0.005)), _ka]
        _n = np.cross(_tri[1] - _tri[0], _tri[2] - _tri[0]); _n = _n / np.linalg.norm(_n) * 0.0025
        _vs = [bm.verts.new(tuple(q + sg * _n)) for sg in (1, -1) for q in _tri]
        bm.faces.new(_vs[0:3]); bm.faces.new(_vs[3:6][::-1])
        for i in range(3):
            j = (i + 1) % 3; bm.faces.new((_vs[i], _vs[3 + i], _vs[3 + j], _vs[j]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Gear_Leg_' + side] = link('Gear_Leg_' + side, bm)
        bt = bmesh.new()                                                # tyre
        rs = [[(Wc[0] + r * math.sin(a), Wc[1] + dy, Wc[2] + r * math.cos(a)) for a in np.linspace(0, 2 * math.pi, 40, endpoint=False)] for dy, r in WP]
        loft_rings(bt, rs, False, False); bmesh.ops.recalc_face_normals(bt, faces=bt.faces)
        nt = len(bt.faces)
        for dy in (-0.064, 0.064):                                      # 4 in wheel: flanges, hub drum, hubcap
            cyl(bt, (Wc[0], Wc[1] + dy - 0.006, Wc[2]), (Wc[0], Wc[1] + dy + 0.006, Wc[2]), 0.076, 28)
        cyl(bt, (Wc[0], Wc[1] - 0.064, Wc[2]), (Wc[0], Wc[1] + 0.064, Wc[2]), 0.052, 28)
        for sg in (1, -1):
            cyl(bt, (Wc[0], Wc[1] + sg * 0.07, Wc[2]), (Wc[0], Wc[1] + sg * 0.085, Wc[2]), 0.062, 28, r2=0.05)
        bt.faces.ensure_lookup_table(); wtag = [0] * nt + [1] * (len(bt.faces) - nt)
        P['Wheel_' + side] = link('Wheel_' + side, bt); TAGS['Wheel_' + side] = wtag
        d = (T - A) / np.linalg.norm(T - A)                             # strut: lower tube moves with the leg
        bm = bmesh.new(); tube(bm, [tuple(A), tuple(A + 0.55 * L_EXT * d)], 0.018, 10)
        P['Shock_Lower_' + side] = link('Shock_Lower_' + side, bm)
        dd = -d; bm = bmesh.new()                                       # boot + upper tube hang from the apex pin
        tube(bm, [tuple(T), tuple(T + 0.06 * L_EXT * dd)], 0.02, 10)
        tube_r(bm, [T + k * L_EXT * dd for k in (0.06, 0.08, 0.12, 0.40, 0.50, 0.53)], [0.02, 0.045, 0.052, 0.052, 0.045, 0.022], 14)
        P['Shock_Boot_' + side] = link('Shock_Boot_' + side, bm)
        GEAR_SIDES[side] = dict(H=H, C=C, T=T, A=A, W=Wc)
        MOV[side] = ['Gear_Leg_' + side, 'Wheel_' + side, 'Shock_Lower_' + side]
    # tailwheel: leaf spring, steerable head + fork + arms, 6 x 2 solid tyre, steering springs to the rudder horns
    bmt = bmesh.new()
    _sp = [(6.10, 0.035), (6.25, 0.0), (6.44, -0.045)]                       # master leaf path (xd, z)
    for x0, dz in ((6.10, 0.0), (6.20, -0.0065), (6.29, -0.013)):           # 3-leaf spring (Univair), shorter leaves below
        pts = [(x0, float(np.interp(x0, [q[0] for q in _sp], [q[1] for q in _sp])))] + [q for q in _sp if q[0] > x0 + 0.01]
        bar(bmt, [B(x, 0, z + dz) for x, z in pts], 0.045, 0.006)
    P['TailWheel_Spring'] = link('TailWheel_Spring', bmt)
    TWP = np.array(B(6.455, 0, -0.04)); TWC = np.array(B(6.58, 0, -0.035)); R_TW = 0.076
    bmt = bmesh.new()
    cyl(bmt, B(6.455, 0, -0.075), B(6.455, 0, -0.015), 0.022, 12)
    for sy in (1, -1):
        bar(bmt, [B(6.46, sy * 0.032, -0.06), B(6.58, sy * 0.032, -0.035)], 0.035, 0.006, up=(0, 1, 0))
    bar(bmt, [B(6.45, 0.075, -0.02), B(6.45, -0.075, -0.02)], 0.025, 0.006)
    P['TailWheel_Steer'] = link('TailWheel_Steer', bmt)
    bmt = bmesh.new()
    rs = [[(TWC[0] + r * math.sin(a), dy, TWC[2] + r * math.cos(a)) for a in np.linspace(0, 2 * math.pi, 24, endpoint=False)]
          for dy, r in ((-0.025, 0.03), (-0.026, 0.06), (-0.02, 0.073), (0.0, 0.076), (0.02, 0.073), (0.026, 0.06), (0.025, 0.03))]
    loft_rings(bmt, rs); nt = len(bmt.faces)
    cyl(bmt, (TWC[0], -0.03, TWC[2]), (TWC[0], 0.03, TWC[2]), 0.032, 16); bmt.faces.ensure_lookup_table()
    TAGS['Wheel_Tail'] = [0] * nt + [1] * (len(bmt.faces) - nt); P['Wheel_Tail'] = link('Wheel_Tail', bmt)
    bmt = bmesh.new()
    for sy in (1, -1):
        tube(bmt, [B(6.33, sy * 0.10, 0.12), B(6.45, sy * 0.075, -0.02)], 0.006, n=8)
    P['TailWheel_Links'] = link('TailWheel_Links', bmt)
# ---- EDO 60-1320 floats (J3_VARIANT=floats) ---------------------------------------------------------------------------
# Lines and rigging read by eye off the Piper/EDO installation drawing 60-S-908A ("Float Installation Piper J-4A, J-3",
# 1"=12") and Kenmore Air Harbor's EDO 60-1320 parts list (Figure 1). Drawing pixels (6000 x 4000 scan) are converted
# with the 75 in float spread (top view) -> 16.53 px/in. The forward strut fitting is put on the J-3 front gear fitting
# (lower longeron, xd 1.46); everything else hangs off that one anchor at drawing scale.
FLOAT_INFO = {}
if VAR == 'floats':
    KPX = IN / 16.53                                            # m per drawing pixel
    XB = 1.46 - (3805 - 2648) * KPX                             # bow tip station (front fitting px 3805 -> xd 1.46)
    ZD = -0.625 - (2897 - 2605) * KPX                           # float deck height (front fitting 292 px above the deck)
    def fx(px): return XB + (px - 2648) * KPX
    def fz(py): return ZD - (py - 2897) * KPX
    PX_STEP, PX_TR = 4250, 5528
    # side-view lines (deck sheer, chine, keel) and top-view half-beams, sampled off the scan column by column
    _deck = tbl([(2647, 2985), (2652, 2962), (2660, 2950), (2680, 2944), (2700, 2940), (2800, 2927), (2900, 2918), (3000, 2908),
                 (3100, 2903), (3200, 2899), (3400, 2897), (5528, 2897)])
    _ch_f = tbl([(2647, 2985), (2660, 3005), (2680, 3016), (2700, 3026), (2800, 3065), (2900, 3097), (3000, 3120), (3100, 3136),
                 (3200, 3149), (3300, 3158), (3400, 3165), (4250, 3165)])
    _kl_f = tbl([(2647, 2985), (2660, 3020), (2680, 3038), (2700, 3052), (2800, 3110), (2900, 3146), (3000, 3174), (3100, 3190),
                 (3200, 3204), (3300, 3213), (3400, 3219), (3500, 3222), (4250, 3225)])
    _ch_a = tbl([(4250, 3137), (4300, 3132), (4400, 3122), (4500, 3112), (4600, 3102), (4700, 3093), (4800, 3082), (4900, 3073),
                 (5000, 3062), (5100, 3053), (5200, 3043), (5300, 3034), (5400, 3024), (5528, 3012)])
    _kl_a = tbl([(4250, 3192), (4300, 3187), (4400, 3172), (4500, 3160), (4600, 3149), (4700, 3137), (4800, 3124), (4900, 3113),
                 (5000, 3101), (5100, 3089), (5200, 3077), (5300, 3065), (5400, 3052), (5528, 3038)])
    _cb = tbl([(2647, 0), (2652, 30), (2660, 56), (2680, 74), (2700, 84), (2720, 92), (2740, 99), (2760, 106), (2780, 112), (2800, 119),
               (2900, 140), (3000, 154), (3100, 165), (3200, 172), (3300, 174), (3400, 176), (4250, 176), (4251, 168), (4300, 165),
               (4400, 161), (4500, 157), (4600, 152), (4700, 146), (4800, 140), (4900, 133), (5000, 125), (5100, 119), (5200, 112),
               (5300, 105), (5400, 98), (5528, 89)])                     # chine half-beam, px
    _db = tbl([(2647, 0), (2652, 26), (2660, 40), (2680, 44), (2700, 48), (2720, 52), (2740, 56), (2760, 60), (2780, 63), (2800, 66),
               (2900, 79), (3000, 88), (3100, 95), (3200, 98), (3300, 99), (4300, 97), (4400, 96), (4500, 94), (4600, 93), (4700, 90),
               (4800, 89), (4900, 86), (5000, 82), (5100, 78), (5200, 73), (5300, 69), (5400, 64), (5528, 57)])   # deck half-width, px
    def f_sec(px, aft):
        """(deck z, deck half-w, chine z, chine half-w, keel z) at a drawing station"""
        ch, kl = (_ch_a, _kl_a) if aft else (_ch_f, _kl_f)
        return fz(_deck(px)), _db(px) * KPX, fz(ch(px)), _cb(px) * KPX, fz(kl(px))
    def f_ring(px, aft, ycl):
        zd, wd, zc, wc, zk = f_sec(px, aft); x = fx(px)
        zm = 0.5 * (zc + zk) + 0.08 * (zc - zk) + 0.004                # slightly hollow (fluted) V bottom
        half = [(wd, zd), (0.5 * (wd + wc) + 0.01 * (wc > 0.02), 0.5 * (zd + zc)), (wc, zc), (0.5 * wc, zm), (0.0, zk)]
        pts = half + [(-a, b) for a, b in half[::-1][1:]]
        return [B(x, ycl + a, b) for a, b in pts]
    F_STA = [2648, 2652, 2660, 2670, 2680, 2700, 2730, 2770, 2820, 2880, 2950, 3030, 3120, 3220, 3350, 3500, 3700, 3900, 4100, 4249]
    A_STA = [4251, 4300, 4400, 4500, 4650, 4800, 4950, 5100, 5250, 5400, 5528]
    Y_FL = 75 * IN / 2                                          # float centreline, each side
    # volume (both floats) and level-deck drafts: quick numeric check of the 1320 lb rating, handed to the physics side
    def _area(px, aft, h):                                      # immersed section area below z = h (polygon clip)
        zd, wd, zc, wc, zk = f_sec(px, aft)
        poly = [(wd, zd), (wc, zc), (0, zk), (-wc, zc), (-wd, zd)]
        out = []
        for i in range(len(poly)):
            p, q = poly[i], poly[(i + 1) % len(poly)]
            pin, qin = p[1] <= h, q[1] <= h
            if pin: out.append(p)
            if pin != qin:
                t = (h - p[1]) / (q[1] - p[1]); out.append((p[0] + t * (q[0] - p[0]), h))
        if len(out) < 3: return 0.0
        return 0.5 * abs(sum(out[i][0] * out[(i + 1) % len(out)][1] - out[(i + 1) % len(out)][0] * out[i][1] for i in range(len(out))))
    def f_vol(h=1e9):
        v = 0.0
        for sta, aft in ((np.linspace(2650, 4219, 200), False), (np.linspace(4221, 5525, 160), True)):
            a = [_area(p, aft, h) for p in sta]; xs_ = sta * KPX; v += float(sum(0.5 * (a[i] + a[i + 1]) * (xs_[i + 1] - xs_[i]) for i in range(len(a) - 1)))
        return v
    V1 = f_vol(); FLOAT_INFO['volume_each_m3'] = round(V1, 4)
    print('FLOAT volume each %.3f m3 (%.1f ft3) -> %.0f lb fresh-water buoyancy each' % (V1, V1 / 0.0283168, V1 * 1000 / 0.4536))
    for lbl, lb in (('gross 1160 lb (A-691 Edo)', 1160), ('empty ~730 lb', 730)):
        need = lb * 0.4536 / 1000 / 2; lo, hi = fz(3225), ZD
        for _ in range(40):
            m = 0.5 * (lo + hi)
            if f_vol(m) < need: lo = m
            else: hi = m
        FLOAT_INFO['waterline_z_level_deck_' + str(lb)] = round(m, 4)
        print('FLOAT level-deck waterline for %s: z %.3f (draft at step %.3f m, %.1f in)' % (lbl, m, m - fz(3225), (m - fz(3225)) / IN))
    for side, sy in (('L', 1), ('R', -1)):
        ycl = sy * Y_FL; bm = bmesh.new()
        rings = [f_ring(p, False, ycl) for p in F_STA] + [f_ring(p, True, ycl) for p in A_STA]
        loft_rings(bm, rings); tag_new(bm, 0)
        for px in (2950, 3300, 3700, 4100, 4500, 4950, 5350):         # hand-hole covers on the deck
            zd, wd = fz(_deck(px)), _db(px) * KPX
            cyl(bm, B(fx(px), ycl, zd - 0.002), B(fx(px), ycl, zd + 0.006), min(0.07, 0.75 * wd), 18)
        for px in (2760, 5420):                                     # bow / stern cleats
            zd = fz(_deck(px)); rbox(bm, B(fx(px), ycl, zd + 0.018), (0.08, 0.016, 0.012))
            for dx in (-0.025, 0.025): rbox(bm, B(fx(px) + dx, ycl, zd + 0.008), (0.012, 0.012, 0.016))
        bar(bm, [B(fx(p), ycl, fz(_kl_f(p)) - 0.004) for p in (2720, 2800, 2950, 3200, 3600, 4219)], 0.03, 0.008)   # keel plate
        for px, w_ in ((3459, 0.06), (4480, 0.09)):                 # strut / spreader deck fittings (inboard of centre)
            rbox(bm, B(fx(px), ycl - sy * 0.11, ZD + 0.012), (w_, 0.05, 0.024))
        if side == 'L': rbox(bm, B(fx(PX_TR) + 0.03, ycl, fz(2960)), (0.06, 0.05, 0.03)); rbox(bm, B(fx(PX_TR) + 0.03, ycl, fz(3030)), (0.06, 0.05, 0.03))   # water-rudder transom bracket
        tag_new(bm, 1); TAGS['Float_' + side] = tags_of(bm)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Float_' + side] = link('Float_' + side, bm)
    # struts (streamlined), spreader bars, brace wires: one fixed object each
    XF, XDg, XR = fx(3459), fx(4444), fx(4500)                  # deck feet: front, diagonal, rear strut
    XFT, XDT, XRT = 1.46, 1.505, fx(4512)                       # fuselage fittings (front = J-3 gear front fitting)
    ZRT = F_BOT(XRT) + 0.005; YI = Y_FL - 0.11; YT = 0.30
    FLOAT_INFO.update(front_fitting_xd=XFT, rear_fitting_xd=round(XRT, 3), deck_z=round(ZD, 4), bow_xd=round(XB, 3),
                      step_xd=round(fx(PX_STEP), 3), transom_xd=round(fx(PX_TR), 3), spread_m=round(2 * Y_FL, 4))
    print('FLOAT stations: bow %.3f step %.3f transom %.3f deck z %.3f; rear fitting xd %.3f z %.3f' % (XB, fx(PX_STEP), fx(PX_TR), ZD, XRT, ZRT))
    bms = bmesh.new()
    for sy in (1, -1):
        blade_strut(bms, B(XF, sy * YI, ZD + 0.02), B(XFT, sy * YT, -0.625), [0.055, 0.055, 0.05], 0.022)
        blade_strut(bms, B(XDg, sy * YI, ZD + 0.02), B(XDT, sy * YT, -0.628), [0.045, 0.045, 0.04], 0.018)
        blade_strut(bms, B(XR, sy * YI, ZD + 0.02), B(XRT, sy * YT, ZRT), [0.055, 0.055, 0.05], 0.022)
        for a, b in ((XFT, -0.625), (XRT, ZRT)):                    # fuselage fitting lugs
            rbox(bms, B(a, sy * (YT + 0.01), b - 0.01), (0.07, 0.03, 0.04))
    for x_ in (XF, fx(4480)):                                       # spreader bars at deck level
        tube(bms, [B(x_, Y_FL - 0.146, ZD + 0.03), B(x_, -(Y_FL - 0.146), ZD + 0.03)], 0.019, n=12)
    P['Float_Struts'] = link('Float_Struts', bms)
    bmw = bmesh.new()
    for sy in (1, -1):
        tube(bmw, [B(XF, sy * YI, ZD + 0.02), B(XFT, -sy * YT, -0.625)], 0.004, n=6)      # front cross wires: ends ON the front strut axes (same plane)
        tube(bmw, [B(XR, sy * YI, ZD + 0.03), B(XRT, -sy * YT, ZRT - 0.005)], 0.004, n=6)   # rear cross wires
        tube(bmw, [B(XF, sy * (Y_FL - 0.15), ZD + 0.035), B(fx(4480), -sy * (Y_FL - 0.15), ZD + 0.035)], 0.004, n=6)  # spreader X
    xh = fx(5570)                                                   # LH water-rudder steering cables: horn -> deck -> rear strut -> fuselage
    for dy in (0.06, -0.06):
        tube(bmw, [B(xh, Y_FL + dy, ZD + 0.03), B(fx(4600), Y_FL + dy * 0.5, ZD + 0.02), B(XR + 0.02, YI + 0.01 * dy / 0.06, ZD + 0.05),
                   B(XRT + 0.02, YT + 0.02, ZRT + 0.01)], 0.0025, n=5, cap=False)
    P['Float_Wires'] = link('Float_Wires', bmw)
    # LH water rudder only (Kenmore 60-1320 Fig. 1 key: RH float assembly 'less water rudder')
    WR_P = np.array(B(xh, Y_FL, ZD)); WR_H = np.array(B(xh, Y_FL, fz(3040)))
    bm = bmesh.new()
    cyl(bm, B(xh, Y_FL, ZD + 0.035), B(xh, Y_FL, fz(3045)), 0.011, 12)                    # steering post
    bar(bm, [B(xh, Y_FL + 0.07, ZD + 0.035), B(xh, Y_FL - 0.07, ZD + 0.035)], 0.03, 0.008)  # steering horn
    rbox(bm, B(xh, Y_FL, fz(3040)), (0.04, 0.03, 0.03))                                     # blade yoke / retract pivot
    P['WaterRudder_L'] = link('WaterRudder_L', bm)
    bm = bmesh.new()                                                # blade: the rounded paddle drawn on 60-S-908A (~13 x 7 in)
    blade = [(5598 + 106 * math.cos(t), 3082 + 56 * math.sin(t)) for t in np.linspace(math.radians(-115), math.radians(235), 30)]
    blade = [(5560, 3035)] + blade + [(5585, 3030)]
    vf = [bm.verts.new(B(fx(a), Y_FL + 0.004, fz(b))) for a, b in blade]; vb = [bm.verts.new(B(fx(a), Y_FL - 0.004, fz(b))) for a, b in blade]
    bm.faces.new(vf); bm.faces.new(vb[::-1])
    for i in range(len(blade)):
        j = (i + 1) % len(blade); bm.faces.new((vf[i], vb[i], vb[j], vf[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['WaterRudder_Blade_L'] = link('WaterRudder_Blade_L', bm)
    FLOAT_INFO['water_rudder_blade_down_z'] = round(fz(3138), 4)
#FLOATS-END#

# ---- paint: Cub yellow everywhere, plain black lightning stripe on both fuselage sides (no logo, no registration) -----
# v4: the factory Cub bolt, traced off side photos (refs/j3-cub c38 / c19 / c07) and checked against the Severin 3-view:
# a long thin forward spike at the nose, a Z step (upper bar's front edge leaning forward, a notch and a down-aft barb
# below), then one straight taper rising aft to a point just behind the stab LE (xd, z drawing frame)
STRIPE = [(0.84, -0.20), (1.242, -0.19), (1.122, -0.06), (1.563, -0.049), (2.044, -0.026), (3.123, 0.026), (4.462, 0.14),
          (5.71, 0.258), (4.462, 0.118), (3.123, -0.018), (2.044, -0.09), (1.563, -0.135), (1.308, -0.157), (1.404, -0.26)]
def pip(px, py, poly):
    inside = np.zeros(px.shape, bool); n = len(poly)
    for i in range(n):
        x1, y1 = poly[i]; x2, y2 = poly[(i + 1) % n]
        c = ((y1 > py) != (y2 > py)) & (px < (x2 - x1) * (py - y1) / (y2 - y1 + 1e-12) + x1)
        inside ^= c
    return inside
VL = View('j3_side_l', (-6.9, 0.1), (-0.75, 0.75), 4096, 900, flip_a=True)
VR = View('j3_side_r', (-6.9, 0.1), (-0.75, 0.75), 4096, 900)
for V_ in (VL, VR):
    V_.fill(np.ones(V_.A.shape, bool), YEL)
    V_.fill(pip(-V_.A, V_.B, STRIPE), BLK)
_xd, _z, _w = -VR.A, VR.B, 0.0035                                  # right-side door seams (split line, hinges, edges)
_D = DOOR
_fx = np.where(_z >= _D['zs'], _D['x0'] + _D['up_lean'] * (_z - _D['zs']) / (_D['zt'] - _D['zs']),
               _D['x0'] + _D['lo_lean'] * (_D['zs'] - _z) / (_D['zs'] - _D['zb']))
_ax = np.where(_z >= _D['zs'], _D['ax_s'] + (_D['ax_t'] - _D['ax_s']) * np.clip((_z - _D['zs']) / (_D['zt'] - _D['zs']), 0, 1),
               _D['ax_s'] - (_D['ax_s'] - _D['ax_b']) * np.clip((_D['zs'] - _z) / (_D['zs'] - _D['zb']), 0, 1))
VR.fill(((np.abs(_xd - _fx) < _w * 1.2) | (np.abs(_xd - _ax) < _w * 1.2)) & (_z > _D['zb'] - _w) & (_z < _D['zt'] + _w)
        | ((np.abs(_z - _D['zs']) < _w) | (np.abs(_z - _D['zt']) < _w) | (np.abs(_z - _D['zb']) < _w)) & (_xd > _fx - _w) & (_xd < _ax + _w),
        (150, 112, 8))
for V_ in (VL, VR):   # v5 cowl panel line (Ty photo ty-cowl-eyebrow-rear): side panel / lower cowl split
    V_.fill((np.abs(V_.B + 0.20) < 0.0018) & (-V_.A > 0.262) & (-V_.A < 0.787), (70, 52, 6))
M = {}
for V_ in (VL, VR): M[V_.name] = paint.mat_image('Paint_' + V_.name, V_.to_image(os.path.join(TEX, V_.name + '.png')), rough=0.4)
C = paint.mat_colour
M.update(yellow=C('Paint_cub_yellow', YEL, rough=0.4), black=C('Paint_black', BLK, rough=0.5),
         glass=paint.mat_glass('Glass', col=(150, 170, 185), alpha=0.28), tyre=C('Tyre', TYRE, rough=0.85),
         wood=C('Prop_wood', WOOD, rough=0.3), brass=C('Prop_tip_brass', (190, 160, 90), rough=0.3, metal=0.8),
         steel=C('Steel', (165, 168, 172), rough=0.3, metal=0.9), cabin=C('Cabin_fabric', (150, 128, 96), rough=0.9),
         boot=C('Shock_boot', (24, 24, 24), rough=0.7),
         en_cyl=C('Engine_barrel', (20, 20, 22), rough=0.6, metal=0.2), en_head=C('Engine_head', (24, 24, 26), rough=0.55, metal=0.2),
         en_rocker=C('Engine_rocker_cover', (196, 197, 202), rough=0.12, metal=1.0), en_hood=C('Engine_hood', (16, 16, 18), rough=0.28),
         en_exh=C('Exhaust', (110, 96, 84), rough=0.6, metal=0.5), en_case=C('Engine_case', (120, 122, 125), rough=0.5, metal=0.3),
         en_dark=C('Engine_dark', (25, 25, 27), rough=0.8), en_alu=C('Fuel_cap_red', (196, 28, 24), rough=0.35),
         ck_floor=C('Cabin_floor', (70, 58, 46), rough=0.9), ck_seat=C('Cabin_seat_black', (22, 22, 24), rough=0.75),
         ck_panel=C('Cabin_panel', (52, 40, 30), rough=0.6), ck_gauge=C('Gauge_bezel', (30, 30, 32), rough=0.4, metal=0.4),
         ck_face=C('Gauge_face', (14, 14, 16), rough=0.2), ck_stick=C('Cabin_stick', (28, 28, 30), rough=0.5),
         ck_dark=C('Cabin_dark', (40, 36, 32), rough=0.9))
M['ck_tubeY'] = M['yellow']; M['en_steel'] = M['steel']
# v5 materials: exhaust chrome/steel, cowl gap, black interior (Ty panel photo), panel atlas, varnished prop
TEXD = os.path.join(HERE, 'textures')
M.update(en_plug=C('Spark_plug_shield', (170, 170, 175), rough=0.35, metal=1.0), en_wire=C('Ignition_lead_yellow', (214, 170, 30), rough=0.6),
         en_steel=C('Exhaust_steel', (150, 150, 156), rough=0.30, metal=1.0), en_seam=C('Cowl_panel_gap', (40, 30, 8), rough=0.9),
         en_alu=C('Fuel_cap_red', (196, 28, 24), rough=0.35),
         ck_panel=C('Cabin_panel_black', (20, 20, 22), rough=0.45), ck_gauge=C('Gauge_bezel_black', (14, 14, 16), rough=0.3, metal=0.3),
         ck_chrome=C('Cabin_chrome', (226, 228, 232), rough=0.1, metal=1.0), ck_alu=C('Placard_aluminium', (196, 198, 202), rough=0.3, metal=0.8),
         ck_floor=C('Cabin_floor_black', (30, 29, 28), rough=0.9), ck_dark=C('Cabin_dark', (28, 27, 26), rough=0.9),
         cabin=C('Cabin_fabric_yellow', (176, 138, 36), rough=0.9), door_in=C('Door_inner_black', (30, 29, 28), rough=0.8),   # v5f: sidewalls = inside of the yellow fabric (photos d01, c14)
         ck_atlas=paint.mat_image('Panel_gauge_faces', bpy.data.images.load(os.path.join(TEXD, 'j3_panel_atlas.png')), rough=0.25),
         prop_wood=paint.mat_image('Prop_wood_varnished', bpy.data.images.load(os.path.join(TEXD, 'j3_prop_wood.png')), rough=0.2),
         prop_sheath=C('Prop_LE_sheath_brass', (206, 172, 100), rough=0.25, metal=0.9), prop_dark=C('Prop_bolt_hole', (16, 14, 12), rough=0.8))
def setm(o, names, idx=None):
    o.data.materials.clear()
    for n in names: o.data.materials.append(M[n])
    if idx is not None:
        assert len(idx) == len(o.data.polygons), (o.name, len(idx), len(o.data.polygons))
        for p, t in zip(o.data.polygons, idx): p.material_index = t
    elif len(names) == 1:
        for p in o.data.polygons: p.material_index = 0
def fus_assign(nrm, c):
    if abs(nrm.z) >= 0.7: return (2, None)
    return (0, VL) if c.y >= 0 else (1, VR)
setm(P['Fuselage'], ['j3_side_l', 'j3_side_r', 'yellow']); paint.project(P['Fuselage'], fus_assign)
for n in ('DoorUpper_R', 'DoorLower_R'):
    setm(P[n], ['j3_side_l', 'j3_side_r', 'yellow', 'door_in'])
    paint.project(P[n], lambda nrm, c: (3, None) if nrm.y > 0.3 else fus_assign(nrm, c))
setm(P['Engine'], ['en_' + t for t in EN_NAMES], [EN_NAMES.index(t) for t in EN_T])
setm(P['Cabin'], ['ck_' + t for t in CK_T], ck_tag)
setm(P['Prop'], ['prop_wood', 'prop_sheath', 'steel', 'prop_dark'], face_tag)
setm(P['Panel_Gauges'], ['ck_atlas'])
# ---- v5 fin emblem (Ty photo refs/j3-cub/ty-tail-bear.png for size, placement and colours ONLY): ORIGINAL cartoon bear
# holding a red sign that reads only 'CUB' (j3_fin_bear.png, drawn by j3_textures.py). Both fin faces, conformed to the
# fin surface; UVs run nose->tail on the left and tail->nose on the right so neither side reads mirrored.
from mathutils.bvhtree import BVHTree
DXC, DZC, DW, DH = 5.95, 0.72, 0.265, 0.314
_fm = P['Fin'].data; _bvh = BVHTree.FromPolygons([v.co.copy() for v in _fm.vertices], [tuple(p.vertices) for p in _fm.polygons])
_dm = paint.mat_image('Fin_emblem_bear', bpy.data.images.load(os.path.join(TEXD, 'j3_fin_bear.png')), rough=0.35)
_nt = _dm.node_tree; _tx = [n for n in _nt.nodes if n.type == 'TEX_IMAGE'][0]; _bs = [n for n in _nt.nodes if n.type == 'BSDF_PRINCIPLED'][0]
_rd = _nt.nodes.new('ShaderNodeMath'); _rd.operation = 'ROUND'                  # glTF exporter -> alphaMode MASK
_nt.links.new(_tx.outputs['Alpha'], _rd.inputs[0]); _nt.links.new(_rd.outputs[0], _bs.inputs['Alpha'])
for k_, v_ in (('blend_method', 'CLIP'), ('surface_render_method', 'DITHERED')):
    try: setattr(_dm, k_, v_)
    except Exception: pass
for side, sg in (('L', 1), ('R', -1)):
    bmd = bmesh.new(); _uvd = bmd.loops.layers.uv.new('UVMap'); NU, NV = 12, 14; grid = []
    for j in range(NV + 1):
        row = []
        for i in range(NU + 1):
            fu, fv = i / NU, j / NV; xd_ = DXC - DW / 2 + DW * fu; z_ = DZC - DH / 2 + DH * fv
            hit = _bvh.ray_cast(Vector((-xd_, sg * 1.0, z_)), Vector((0, -sg, 0)))
            y_ = (hit[0].y if hit[0] is not None else 0.0) + sg * 0.0009
            row.append((bmd.verts.new((-xd_, y_, z_)), (fu if sg > 0 else 1 - fu, fv)))
        grid.append(row)
    for j in range(NV):
        for i in range(NU):
            cs = (grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i])
            if sg < 0: cs = cs[::-1]
            f = bmd.faces.new([c[0] for c in cs])
            for l_, c in zip(f.loops, cs): l_[_uvd].uv = c[1]
    P['Fin_Decal_' + side] = link('Fin_Decal_' + side, bmd); P['Fin_Decal_' + side].data.materials.append(_dm)
for n, m in (('Wing', 'yellow'), ('Aileron_L', 'yellow'), ('Aileron_R', 'yellow'), ('Stab', 'yellow'), ('Elevator_L', 'yellow'),
             ('Elevator_R', 'yellow'), ('Fin', 'yellow'), ('Rudder', 'yellow'), ('Wire_Tail', 'steel'), ('Strut_Wing', 'yellow'),
             ('Glass', 'glass'), ('Window_R', 'glass'), ('Cabin_Liner', 'cabin'), ('Liner_Stbd', 'cabin'), ('Gear_Main', 'yellow'), ('Gear_Leg_L', 'yellow'),
             ('Gear_Leg_R', 'yellow'), ('Shock_Lower_L', 'yellow'), ('Shock_Lower_R', 'yellow'), ('Shock_Boot_L', 'boot'),
             ('Shock_Boot_R', 'boot'), ('TailWheel_Spring', 'black'), ('TailWheel_Steer', 'black'), ('TailWheel_Links', 'steel')):
    if n in P: setm(P[n], [m])
M.update(float_alu=C('Float_skin_aluminium', (196, 198, 202), rough=0.35, metal=0.85),
         float_dark=C('Float_hardware', (70, 72, 76), rough=0.5, metal=0.6))
for n, t in TAGS.items():
    if n.startswith('Float_'): setm(P[n], ['float_alu', 'float_dark'], t)
    else: setm(P[n], ['tyre', 'yellow'] if n != 'Wheel_Tail' else ['tyre', 'black'], t)
for n, m in (('Float_Struts', 'float_alu'), ('Float_Wires', 'steel'), ('WaterRudder_L', 'steel'), ('WaterRudder_Blade_L', 'float_alu')):
    if n in P: setm(P[n], [m])
for n in P:
    if not P[n].data.materials: print('NO MATERIAL', n)
for o in P.values():
    bm = bmesh.new(); bm.from_mesh(o.data)
    if o.name not in ('Glass', 'Window_R', 'Cabin_Liner', 'Liner_Stbd', 'Panel_Gauges', 'Fin_Decal_L', 'Fin_Decal_R'): bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data); bm.free()
    for p in o.data.polygons: p.use_smooth = True
    try: o.data.set_sharp_from_angle(angle=math.radians(50))
    except Exception: pass

# ---- gear states (wheels): 3-point height / pitch, extended and full travel -------------------------------------------
_V = {n: np.array([v.co[:] for v in o.data.vertices]) for n, o in P.items()}
H_PUB = 6 * 0.3048 + 8 * IN                  # 6 ft 8 in (Piper J-3 spec)
if VAR == 'wheels':
    def _posed(th):
        out = {}
        for n, a in _V.items():
            side = next((s for s in MOV if n in MOV[s]), None)
            if side is None: out[n] = a; continue
            m = np.array((1, 1 if side == 'L' else -1, 1.0)); out[n] = np.array([g_rot(p, _s * th) for p in a * m]) * m
        return out
    def _three_point(th):
        V = _posed(th); allv = np.concatenate(list(V.values()))
        mw = np.concatenate([V['Wheel_L'], V['Wheel_R']]); tw = V['Wheel_Tail']
        def rot(a, ph):
            c, s = math.cos(ph), math.sin(ph); return np.stack([a[:, 0] * c - a[:, 2] * s, a[:, 1], a[:, 0] * s + a[:, 2] * c], 1)
        lo, hi = -0.1, 0.5
        for _ in range(60):
            ph = 0.5 * (lo + hi)
            if rot(mw, ph)[:, 2].min() < rot(tw, ph)[:, 2].min(): lo = ph
            else: hi = ph
        r = rot(allv, ph); g = rot(mw, ph)[:, 2].min()
        return float(r[:, 2].max() - g), math.degrees(ph)
    GEAR_STATES = {nm: (th, *_three_point(th)) for nm, th in (('extended', 0.0), ('full', TH_FULL))}
    for nm, (th, h, ph) in GEAR_STATES.items():
        print('GEARSTATE %-8s hinge %5.2f deg  strut %.3f m (%+.2f in)  wheel z %+.3f  3pt height %.3f m (%.1f in) pitch %.2f'
              % (nm, math.degrees(th), g_len(th), (g_len(th) - L_EXT) / IN, g_wheel(th)[2], h, h / IN, ph))
    print('PUBLISHED height %.3f m (%.1f in)' % (H_PUB, H_PUB / IN))
allv = np.concatenate(list(_V.values()))
print('BOUNDS(drawing frame)', allv.min(0), allv.max(0), 'size', allv.max(0) - allv.min(0))

# ---- shift to the config frame, yaw 180, origins on hinge lines / axles, rig empties + extras, export ----------------
SH = Matrix.Translation(Vector(tuple(SHIFT))); RZ = Matrix.Rotation(math.pi, 4, 'Z'); EXTRA = []
for o in P.values(): o.data.transform(RZ @ SH)
def w(p): return RZ @ (Vector(tuple(float(c) for c in p)) + Vector(tuple(SHIFT)))
def _empty(name, loc):
    e = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(e); e.location = loc; EXTRA.append(e); return e
def origin(n, p, parent=None):
    o = P[n]; org = w(p) if not isinstance(p, Vector) else p
    o.data.transform(Matrix.Translation(-org))
    if parent is not None: o.parent = parent; o.location = org - parent.location
    else: o.location = org
    return o
def gl_axis(v): v = Vector(v).normalized(); return [round(v.x, 5), round(v.z, 5), round(-v.y, 5)]   # Blender -> glTF (y up)
def hinge_mid(Wsurf_, y, xh):
    f = (float(Wsurf_.le(y)) - xh) / (float(Wsurf_.le(y)) - float(Wsurf_.te(y)))
    return 0.5 * (wsurf(Wsurf_, y, f, True) + wsurf(Wsurf_, y, f, False))
# trimmable stabilizer (jack-screw at the LE, pivot at the rear spar / elevator hinge line); elevators ride on it
st = origin('Stab', (H_S, 0, Z_S))
_le = None
for ax in ((0, 1, 0), (0, -1, 0)):
    R = Matrix.Rotation(math.radians(2.5), 4, Vector(ax))
    _fw = [v for v in st.data.vertices if (w((H_S + 0.4, 0, Z_S)) - st.location).dot(v.co) > 0]
    dz = np.mean([(R @ v.co).z - v.co.z for v in _fw])
    if dz > 0: _le = ax; break
assert _le is not None, 'stab trim axis: positive rotation must raise the LE'
st['trim_axis_gltf'] = gl_axis(_le); st['trim_deg_le_up'] = 2.5; st['trim_deg_le_down'] = 4.0
st['note'] = ('jack-screw stab trim: rotate about trim_axis (origin on the rear spar / elevator hinge line) by +deg = LE up '
              '(nose-down trim), -deg = LE down (nose-up trim); TCDS A-691 stab travel 2.5 up / 4 down. Elevator_L/R are children.')
print('STAB trim axis model', _le, 'LE rise at +2.5 deg %.4f m' % dz)
for side, sg in (('L', 1), ('R', -1)):
    a0, a1 = hinge_mid(W, sg * AL_IN, HA), hinge_mid(W, sg * AL_OUT, HA)
    o = origin('Aileron_' + side, 0.5 * (a0 + a1)); o['hinge_axis_gltf'] = gl_axis(w(a1) - w(a0))
    o = origin('Elevator_' + side, (H_S, sg * 0.7, Z_S), parent=P['Stab']); o['hinge_axis_gltf'] = gl_axis((0, 1, 0))
o = origin('Rudder', (H_R, 0, 0.7)); o['hinge_axis_gltf'] = gl_axis((0, 0, 1))
o = origin('Prop', (X_P, 0, 0)); o['spin_axis_gltf'] = gl_axis(w((1, 0, 0)) - w((0, 0, 0)))
XDH = 0.5 * (door_fx(DOOR['zt']) + door_ax(DOOR['zt'])); XDL = 0.5 * (door_fx(DOOR['zb']) + door_ax(DOOR['zb']))
DOORS = {'DoorUpper_R': ((XDH, -fus_y(XDH, DOOR['zt']), DOOR['zt']), 90.0), 'DoorLower_R': ((XDL, -fus_y(XDL, DOOR['zb']), DOOR['zb']), -175.0)}
for n, (pd, deg) in DOORS.items():
    vs0 = np.array([v.co[:] for v in P[n].data.vertices]).mean(0)
    o = origin(n, B(*pd)); o['hinge_axis_gltf'] = gl_axis((1, 0, 0)); o['open_deg'] = deg
    o['note'] = 'rotate about hinge_axis (local origin on the hinge line) by open_deg to open; 0 = closed'
    for k in (0.5, 1.0):
        R = Matrix.Rotation(math.radians(deg * k), 4, 'X'); vs1 = np.array([(R @ v.co) + o.location for v in o.data.vertices]).mean(0)
        print('DOOR %s at %+.0f deg: centroid y %+.3f -> %+.3f, z %+.3f -> %+.3f' % (n, deg * k, vs0[1], vs1[1], vs0[2], vs1[2]))
        if k == 0.5: assert vs1[1] > vs0[1] + 0.1, 'door must swing outward (+y = right side after the yaw)'
origin('Window_R', w(B(*DOORS['DoorUpper_R'][0])), parent=P['DoorUpper_R'])
# v4 clearance: lower door folded down (-175 deg) vs the right lift struts (min distance of its verts to the strut mesh)
_o = P['DoorLower_R']; _R = Matrix.Rotation(math.radians(-175.0), 4, 'X')
_dv = np.array([(_o.location + _R @ v.co)[:] for v in _o.data.vertices])
_sv = np.array([v.co[:] for v in P['Strut_Wing'].data.vertices]); _sv = _sv[_sv[:, 1] > 0.2]
from mathutils.kdtree import KDTree as _KD
_kd = _KD(len(_sv))
for i_, c_ in enumerate(_sv): _kd.insert(c_, i_)
_kd.balance(); _dm = min(_kd.find(c_)[2] for c_ in _dv)
print('DOOR folded lower panel: bottom z %.3f, min distance to the lift struts %.3f m' % (_dv[:, 2].min(), _dm))
assert _dm > 0.02, 'folded lower door must clear the lift struts' 
if VAR == 'wheels':
    for side, d in GEAR_SIDES.items():
        piv = _empty('Gear_Pivot_' + side, w(d['H'])); _empty('Gear_HingeAxis_' + side, w(d['C'])); _empty('Gear_ShockTop_' + side, w(d['T']))
        piv['wheel_rise_full_m'] = WHEEL_RISE; piv['hinge_deg_full'] = round(math.degrees(TH_FULL), 3)
        piv['strut_len_extended_m'] = round(L_EXT, 4); piv['strut_extension_full_m'] = round(STROKE, 4)
        piv['wheel_up_sign_model_frame'] = int(_s * (1 if side == 'L' else -1))
        piv['note'] = 'bungee gear: legs + wheel + lower strut tube rotate about Pivot->HingeAxis; strut EXTENDS as the wheel rises'
        origin('Gear_Leg_' + side, piv.location, parent=piv)
        o = origin('Wheel_' + side, w(d['W']), parent=piv); o['spin_axis_gltf'] = gl_axis((0, 1, 0))
        origin('Shock_Lower_' + side, w(d['A']), parent=piv)
        origin('Shock_Boot_' + side, w(d['T']))
    st = origin('TailWheel_Steer', TWP); st['steer_axis_gltf'] = gl_axis((0, 0, 1))
    st['steer_deg_springs'] = 30.0; st['full_swivel'] = True
    st['note'] = ('tailwheel swivel pivot: rotate about steer_axis (local origin on the kingpin). Steered by the rudder springs '
                  'through about +/-30 deg (judgement), beyond that it breaks out and casters (full swivel) for ground handling')
    o = origin('Wheel_Tail', w(TWC), parent=st); o['spin_axis_gltf'] = gl_axis((0, 1, 0))
if VAR == 'floats':
    o = origin('WaterRudder_L', w(WR_P)); o['steer_axis_gltf'] = gl_axis((0, 0, 1))
    o['note'] = 'LH water rudder only (60-1320 Fig.1: RH float less water rudder). Steers about steer_axis, linked to the air rudder'
    b = origin('WaterRudder_Blade_L', w(WR_H), parent=o); b['retract_axis_gltf'] = gl_axis((0, -1, 0)); b['retract_deg'] = 90.0
    b['note'] = 'rotate about retract_axis by retract_deg to pull the blade up (retracted); 0 = down (water)'
    vs0 = np.array([v.co[:] for v in b.data.vertices]).mean(0)
    R = Matrix.Rotation(math.radians(90), 4, Vector((0, -1, 0))); vs1 = np.array([(R @ v.co)[:] for v in b.data.vertices]).mean(0)
    print('WRUDDER blade centroid rel hinge down %s -> retracted %s' % (np.round(vs0, 3), np.round(vs1, 3)))
    assert vs1[2] > vs0[2] + 0.05, 'retract must lift the blade'
    for side in ('L', 'R'): o = origin('Float_' + side, w(B(FLOAT_INFO['step_xd'], (1 if side == 'L' else -1) * 75 * IN / 2, FLOAT_INFO['deck_z'])))
    root = P['Fuselage']
    for k, v in FLOAT_INFO.items():
        if k.endswith('_xd'): k, v = k[:-3] + '_x_glb', round(v - SHIFT[0] * -1 * -1, 4) if False else round(float(w(B(v, 0, 0)).x), 4)
        elif '_z' in k: v = round(float(w(B(0, 0, v)).z), 4)
        P['Float_L']['edo1320_' + k] = v
    P['Float_L']['note'] = ('EDO 60-1320 from Piper/EDO dwg 60-S-908A. *_x_glb / *_z values are in the exported (Blender, z-up, nose -x) '
                            'frame; origin of each float at its step, centreline, deck height')
#FLOATRIG-END#
P['Fuselage'].name = 'Fuselage_Painted'
bpy.context.view_layer.update()
fin = np.concatenate([np.array([(o.matrix_world @ v.co)[:] for v in o.data.vertices]) for o in P.values()])
print('BOUNDS', fin.min(0), fin.max(0), 'size', fin.max(0) - fin.min(0), 'parts', sorted(P))
print('TRIS', sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in P.values()))
os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
for o in list(P.values()) + EXTRA: o.select_set(True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_yup=True, export_apply=True, export_extras=True)
print('WROTE', OUT)
