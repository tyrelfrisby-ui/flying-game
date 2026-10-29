"""Clean-room Schweizer SGS 2-33A -> glider-2-33-like-own.glb (our own original model, no GPL input).

Every dimension below comes from public references (see docs/models/sgs233-own-PROVENANCE.md):
the Wikimedia Commons colour 3-view (digitised by eye, 1024-px display scale, 0.01527 m/px), published
2-33A data (span 15.54 m, length 7.85 m), the Schweizer POH cockpit figure and FAA flight manual
(control travels, cockpit layout), and photos. No FlightGear or other third-party mesh is read.

  blender -b --python tools/own_models/build_sgs233.py
"""
import bpy, bmesh, sys, os, math, numpy as np
from mathutils import Matrix, Vector
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import paint
from paint import View
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
# SGS_LIVERY=talk-flying builds the Talk Flying version (same yellow base, black/silver trim, TF lettering)
LIV = os.environ.get('SGS_LIVERY', 'factory')
OUT = os.path.join(ROOT, 'unity/Assets/_Project/Resources/Models/glider-2-33-like/glider-2-33-like-own'
                   + ('-tf' if LIV == 'talk-flying' else '') + '.glb')
TEX = '/tmp/own_sgs_tex'; os.makedirs(TEX, exist_ok=True)
YELLOW, WHITE, NAVY, GREY, TYRE = (255, 204, 0), (246, 246, 246), (24, 34, 90), (120, 122, 128), (88, 90, 96)
bpy.ops.wm.read_factory_settings(use_empty=True)

# ---- reference frame: Blender metres, nose +X, port +Y, up +Z (yawed 180 deg at export) -------
# 3-view side view (display px): nose tip x=228, wing chord line y=197. Wing LE kept at x=0.55 m (as the
# game's previous 2-33) so the origin / CG relation is unchanged.
# v7: scale re-fitted so the side view's nose-to-rudder length is the published 25 ft 9 in (7.85 m)
K = 0.01527
WING_LE_PX = 360
def mx(px): return 0.55 - (px - WING_LE_PX) * K
def mz(py): return 0.47 + (197 - py) * K

def link(name, bm):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); return o

# ---- fuselage stations (px, top py, bottom py, half-width m, superellipse exponent) --------------
# v7: aft of the wing re-measured on the 3-view (side-view top / bottom lines, top-view half-width); the
# fuselage now ends at the rudder post (px 716) instead of running under the rudder; extra rings at the
# cockpit opening's ends (279 / 395).
ST = [(228.5, 258.5, 261.5, 0.02, 2.0), (231.5, 249.6, 269.3, 0.115, 2.0), (234.7, 245.3, 272.5, 0.16, 2.0),
      (238.0, 242.1, 274.7, 0.19, 2.0), (241.1, 239.5, 276.8, 0.218, 2.0), (247.5, 235.7, 280.5, 0.255, 2.1),
      (254.0, 232.5, 283.2, 0.288, 2.2), (260.3, 230.4, 285.9, 0.308, 2.3), (266.7, 228.3, 289.6, 0.33, 2.4),
      (273.0, 226.7, 292.3, 0.34, 2.5), (279, 225.8, 291.6, 0.343, 2.55), (284, 225, 291, 0.345, 2.6),
      (297, 225.5, 291, 0.352, 2.7), (310, 226, 291, 0.355, 2.8), (325, 226, 291, 0.355, 2.8),
      (340, 226, 291, 0.355, 2.8), (351, 223.0, 290.5, 0.353, 2.8), (362, 220, 290, 0.35, 2.8),
      (373, 218.2, 289.4, 0.346, 2.8), (384, 216.2, 288.0, 0.342, 2.8), (395, 214.6, 286.5, 0.337, 2.8),
      (400, 214, 285.5, 0.335, 2.8), (420, 213, 280, 0.318, 2.8), (440, 212.5, 274.6, 0.30, 2.8),
      (460, 212, 269.3, 0.272, 2.75), (480, 212, 262, 0.245, 2.7), (500, 212, 257.8, 0.222, 2.65),
      (520, 212, 254.6, 0.201, 2.6), (540, 212, 252.5, 0.183, 2.55), (560, 212, 250.5, 0.165, 2.5),
      (580, 212, 248.2, 0.145, 2.5), (600, 212, 246, 0.13, 2.45), (620, 212, 243.9, 0.115, 2.4),
      (640, 212, 241.9, 0.10, 2.4), (660, 212, 239.6, 0.086, 2.35), (680, 212, 237.4, 0.07, 2.3),
      (700, 211.5, 236.3, 0.052, 2.2), (710, 211, 235, 0.04, 2.2), (716, 210.5, 234, 0.014, 2.0)]
STa = np.array(ST)
def fus(px):
    return [np.interp(px, STa[:, 0], STa[:, k]) for k in range(1, 5)]
DROP = 0.08                                   # canopy / pylon spring line: 8 cm below the fuselage top
NTOP, NLOW = 8, 26                            # ring vertices: top arc between the sill points, rest
NR = NTOP + NLOW
def sill_angle(px):
    t, b_, hw, n = fus(px); zt, zb = mz(t), mz(b_); zc, hh = (zt + zb) / 2, (zt - zb) / 2
    r = min(max((zt - DROP - zc) / hh, 0.05), 0.98)
    return math.acos(r ** (n / 2))
def ring_ang(px):
    a_s = sill_angle(px)
    top = list(np.linspace(-a_s, a_s, NTOP + 1))[:-1]
    low = list(np.linspace(a_s, 2 * math.pi - a_s, NLOW + 1))[:-1]
    return top + low                           # index 0..NTOP-1: top arc (port sill .. stbd sill), NTOP: stbd sill
def se_pt(px, a, inset=0.0):
    t, b_, hw, n = fus(px); zt, zb = mz(t), mz(b_); zc, hh = (zt + zb) / 2, (zt - zb) / 2
    hw, hh = hw - inset, hh - inset
    c, s = math.cos(a), math.sin(a)
    return (mx(px), -hw * np.sign(s) * abs(s) ** (2 / n), zc + hh * np.sign(c) * abs(c) ** (2 / n))
def ring(px, inset=0.0):
    return [se_pt(px, a, inset) for a in ring_ang(px)]
def side_y(px, z):
    """outer half-width of the fuselage (superellipse) at height z"""
    t, b_, hw, n = fus(px); zt, zb = mz(t), mz(b_); zc, hh = (zt + zb) / 2, (zt - zb) / 2
    return hw * max(1 - abs((z - zc) / hh) ** n, 0) ** (1 / n)
CUT0, CUT1 = 279, 395                         # cockpit opening (canopy front edge .. rear seat back)
bm = bmesh.new(); rings = [[bm.verts.new(ring(s[0]) [i]) for i in range(NR)] for s in ST]
for (sa, a), (sb, b) in zip(zip(ST, rings), list(zip(ST, rings))[1:]):
    for i in range(NR):
        if i < NTOP and sa[0] >= CUT0 and sb[0] <= CUT1: continue   # open top between the sills
        j = (i + 1) % NR; bm.faces.new((a[i], a[j], b[j], b[i]))
nose = bm.verts.new((mx(227.8), 0, mz(260)))
tail = bm.verts.new((mx(716.4), 0, mz(222)))
for i in range(NR):
    j = (i + 1) % NR
    bm.faces.new((rings[0][j], rings[0][i], nose)); bm.faces.new((rings[-1][i], rings[-1][j], tail))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
P = {'Fuselage': link('Fuselage', bm)}

# ---- canopy: faired into the fuselage. Each section springs from the fuselage side DROP below its top,
# at the fuselage's own width there, and rises as a superellipse to the crown measured on the 3-view.
def sec(px, top_py, m=2.4, M_=16, grow=0.0, drop=DROP):
    t, b_, hw, n = fus(px); zt, zb = mz(t), mz(b_); zc, hh = (zt + zb) / 2, (zt - zb) / 2
    z0 = zt - drop; wb = hw * max(1 - abs((z0 - zc) / hh) ** n, 0) ** (1 / n) + 0.004 + grow
    zcr = max(mz(top_py), zt + 0.002) + grow
    return [(mx(px), wb * np.sign(math.cos(a)) * abs(math.cos(a)) ** (2 / m), z0 + (zcr - z0) * math.sin(a) ** (2 / m))
            for a in np.linspace(0, math.pi, M_ + 1)]
def loft_secs(name, secs, cap_end=True):
    bm = bmesh.new(); cr = [[bm.verts.new(p) for p in s_] for s_ in secs]
    for a_, b2 in zip(cr, cr[1:]):
        for i in range(len(a_) - 1): bm.faces.new((a_[i], a_[i + 1], b2[i + 1], b2[i]))
    if cap_end: bm.faces.new(cr[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return link(name, bm)
CAN = [(279, 225.8), (285, 219.7), (293, 213.3), (301, 208.5), (309, 204.8), (317, 202.1), (325, 199.5),
       (333, 197.9), (341, 196.3), (349, 195.7), (357, 195.2), (362, 194.5)]
P['Canopy_Glass'] = loft_secs('Canopy_Glass', [sec(px, tp) for px, tp in CAN], cap_end=False)
bm = bmesh.new()   # frame: bows at the windscreen arch, mid (door/canopy split) and the aft edge, plus sill rails
def bow(bm, px, tp, wdt=1.3, grow=0.008):
    pa = sec(px + wdt, tp, grow=grow); pb = sec(px - wdt, tp, grow=grow)
    va = [bm.verts.new(p) for p in pa]; vb = [bm.verts.new(p) for p in pb]
    for i in range(len(va) - 1): bm.faces.new((va[i], va[i + 1], vb[i + 1], vb[i]))
for px, tp in (CAN[1], CAN[6], CAN[-1]): bow(bm, px, tp)
for k in (0, -1):   # sill rails along both canopy edges
    pts = [sec(px, tp, grow=0.008)[k] for px, tp in CAN]
    for p0, p1 in zip(pts, pts[1:]):
        v = [bm.verts.new(q) for q in (p0, p1, (p1[0], p1[1], p1[2] + 0.035), (p0[0], p0[1], p0[2] + 0.035))]
        bm.faces.new(v)
# no crown rib: the real canopy is one seamless clear bubble (photo C-FDWB)
P['Canopy_Frame'] = link('Canopy_Frame', bm)

# ---- wing (planform from the 3-view) --------------------------------------------------------------
HALF = 15.54 / 2; Y_KINK = 3.41
ROOT_LE, ROOT_C, TIP_LE, TIP_C = 0.55, 1.52, 0.37, 0.79
Z0 = 0.47; DIH = math.radians(1.5)   # flight manual: dihedral 1.5 deg
def chord_at(y):
    y = abs(y)
    if y <= Y_KINK: return ROOT_LE, ROOT_C
    t = (y - Y_KINK) / (HALF - Y_KINK); return ROOT_LE + t * (TIP_LE - ROOT_LE), ROOT_C + t * (TIP_C - ROOT_C)
INC = math.radians(3.5); WASH = math.radians(3.0)   # root incidence (judgement, fits the 3-view root/TE); AFM: 3 deg washout
def airfoil(f, y=0.0):
    # flight manual: NACA 43012A (5-digit: design CL 0.6, max camber at 15 %, 12 % thick)
    t, r, k1 = 0.12, 0.2025, 15.957 * 2
    f = np.asarray(f, dtype=float)
    yt = 5 * t * (0.2969 * np.sqrt(f) - 0.126 * f - 0.3516 * f**2 + 0.2843 * f**3 - 0.1036 * f**4)
    yc = np.where(f < r, k1 / 6 * (f**3 - 3 * r * f**2 + r * r * (3 - r) * f), k1 * r**3 / 6 * (1 - f))
    a = INC - WASH * max(0.0, (abs(y) - Y_KINK) / (HALF - Y_KINK))
    tw = -(f - 0.25) * math.tan(a)     # pitch the section about its quarter chord
    return yc + yt + tw, yc - yt + tw
def wz(y): return Z0 + abs(y) * math.tan(DIH)
def surf(y, f, upper=True):
    le, c = chord_at(y); u, l = airfoil(np.array([f]), y); return (le - f * c, y, wz(y) + (u[0] if upper else l[0]) * c)
def section(y, f0, f1, n=14):
    le, c = chord_at(y); f = f0 + (f1 - f0) * (1 - np.cos(np.linspace(0, math.pi, n))) / 2
    up, lo = airfoil(f, y); z0 = wz(y)
    pts = [(le - fi * c, y, z0 + u * c) for fi, u in zip(f, up)] + [(le - fi * c, y, z0 + l * c) for fi, l in zip(f[::-1], lo[::-1])]
    if f0 == 0: pts = pts[:n] + pts[n + 1:-1]
    return pts
def loft(name, ys, sec):
    bm = bmesh.new(); rs = [[bm.verts.new(p) for p in sec(y)] for y in ys]; m = len(rs[0])
    for a, b in zip(rs, rs[1:]):
        for i in range(m): j = (i + 1) % m; bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(rs[0][::-1]); bm.faces.new(rs[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return link(name, bm)
A_IN, A_OUT, A_F = 4.09, 7.50, 0.78          # v7: aileron span / hinge measured on the 3-view top view
SP_IN, SP_OUT, SP_F0, SP_F1 = 1.90, 3.04, 0.47, 0.58
L = lambda a, b, n: list(np.linspace(a, b, n))
wing = []
for sg, sd in ((1, 'L'), (-1, 'R')):
    ys_in = sorted(set([round(v, 4) for v in L(0, Y_KINK, 7) + L(Y_KINK, A_IN, 2)]))
    wing.append(loft('_wi' + sd, [sg * y for y in ys_in], lambda y: section(y, 0, 1)))
    wing.append(loft('_wf' + sd, [sg * y for y in L(A_IN, A_OUT, 7)], lambda y: section(y, 0, A_F)))
    wing.append(loft('_wt' + sd, [sg * y for y in L(A_OUT, HALF, 2)], lambda y: section(y, 0, 1)))
    P['Aileron_' + sd] = loft('Aileron_' + sd, [sg * y for y in L(A_IN + 0.01, A_OUT - 0.01, 7)], lambda y: section(y, A_F + 0.006, 1))
    # dive-brake doors (flight manual: upper and lower doors, hinged at their front edge, 85 / 75 deg open)
    for up, nm in ((True, 'Spoiler_'), (False, 'Spoiler_Lower_')):
        bm = bmesh.new(); vs = []
        for y in (SP_IN, SP_OUT):
            for f in (SP_F0, SP_F1):
                x_, y_, z_ = surf(sg * y, f, up); dz = 0.004 if up else -0.004
                vs.append(((x_, y_, z_ + dz), (x_, y_, z_ + dz + (0.01 if up else -0.01))))
        a = [bm.verts.new(p[0]) for p in vs]; b = [bm.verts.new(p[1]) for p in vs]
        for q in ((0, 1, 3, 2),):
            bm.faces.new([a[i] for i in q]); bm.faces.new([b[i] for i in q[::-1]])
        for i, j in ((0, 1), (1, 3), (3, 2), (2, 0)): bm.faces.new((a[i], a[j], b[j], b[i]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        P[nm + sd] = link(nm + sd, bm)
P['Wing'] = paint.join(wing, 'Wing')
bm = bmesh.new(); bm.from_mesh(P['Wing'].data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.001); bm.to_mesh(P['Wing'].data); bm.free()
# wing-root pylon: lofted fairing following the 3-view profile from the canopy's aft end (px 362) up into the
# wing underside and down the turtle deck to px 475
PYL = [(362, 194.5), (372, 188.2), (385, 186.6), (397, 187.2), (410, 188.4), (435, 192), (450, 197), (458, 201.3), (461, 202.8), (462.5, 211)]
def pyl_sec(px, tp, M_=16, grow=0.0):
    s_ = sec(px, tp, m=2.4, M_=M_, grow=grow); zw = Z0 + 0.03; out = []
    for x, y, z in s_:
        if z > zw: y *= max(0.55, 1 - (z - zw) * 2.0); z = min(z, zw + 0.06)
        out.append((x, y, z))
    return out
P['Fuselage_Pylon'] = loft_secs('Fuselage_Pylon', [pyl_sec(px, tp) for px, tp in PYL])
def outer_y(px, z):
    """outer half-width at height z including canopy / pylon above the sill"""
    t, b_, hw, n = fus(px); z0 = mz(t) - DROP
    if z <= z0: return side_y(px, z)
    tp = np.interp(px, [p for p, _ in PYL], [q for _, q in PYL]) if px >= 362 else np.interp(px, [p for p, _ in CAN], [q for _, q in CAN])
    s_ = (pyl_sec if px >= 362 else sec)(px, tp, M_=32)[:17]
    zs = [p[2] for p in s_]; ys = [p[1] for p in s_]
    return float(np.interp(z, zs, ys)) if zs[-1] > zs[0] else ys[0]

# ---- tail: fin/rudder (balanced rudder over the fin top, raked hinge) and stab/elevator ------------
def slab(name, outline, axis, t):
    bm = bmesh.new(); a = []; b = []
    for p, q in outline:
        if axis == 'y':
            a.append(bm.verts.new((p, t / 2, q))); b.append(bm.verts.new((p, -t / 2, q)))
        else:
            a.append(bm.verts.new((p, q, ZS + t / 2))); b.append(bm.verts.new((p, q, ZS - t / 2)))
    bm.faces.new(a); bm.faces.new(b[::-1]); n = len(a)
    for i in range(n): j = (i + 1) % n; bm.faces.new((a[j], a[i], b[i], b[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return link(name, bm)
HT, HB = 701, 688   # rudder hinge is raked: 3-view px 701 at the fin tip, 688 at the root
P['Fin'] = slab('Fin', [(mx(640), mz(213)), (mx(668), mz(150)), (mx(680), mz(118)), (mx(HT), mz(117)), (mx(HB), mz(213))], 'y', 0.07)
# v7: rudder TE vertical at px 723; its foot ends above the stab (the rudder post / tail cone carries on below)
P['Rudder'] = slab('Rudder', [(mx(HT + 1), mz(116)), (mx(722), mz(115)), (mx(723), mz(160)), (mx(723), mz(207)), (mx(HB + 1), mz(209))], 'y', 0.05)
ZS = mz(211)
# v7 (3-view top view): span 2.37 m, chord 0.853 m, LE 4.99 m behind the wing LE, elevator 0.38 m; stadium tips
SH = 2.37 / 2; SC = 0.853; SLE = 0.55 - 4.987; SHG = SLE - (SC - 0.38); ETE = SLE - SC
RT, RE = 0.40, 0.37
tip = [(SLE - RT * (1 - math.cos(math.pi / 2 * i / 8)), SH - RT + RT * math.sin(math.pi / 2 * i / 8)) for i in range(9)]
shalf = [(SLE, 0.0)] + tip + [(SHG, SH), (SHG, 0.0)]
st = shalf + [(x, -y) for x, y in shalf[::-1][1:-1]]
P['Stab'] = slab('Stab', st, 'z', 0.05)
for sg, sd in ((1, 'L'), (-1, 'R')):
    el = [(SHG - 0.006, 0.02 * sg), (SHG - 0.006, SH * sg)] + \
         [(ETE + RE - RE * math.sin(math.pi / 2 * i / 8), sg * (SH - RE + RE * math.cos(math.pi / 2 * i / 8))) for i in range(9)] + \
         [(ETE, 0.02 * sg)]
    if sg < 0: el = el[::-1]
    P['Elevator_' + sd] = slab('Elevator_' + sd, el, 'z', 0.035)

# ---- struts: one streamlined strut per side, low fuselage fitting (px 394, py 266) -> wing 3.13 m out
def tube(bm, p0, p1, r=0.03, f=2.2, n=10, cap=False):
    p0, p1 = np.array(p0, float), np.array(p1, float); d = p1 - p0; d /= np.linalg.norm(d)
    ref = [1, 0, 0] if abs(d[0]) < 0.9 else [0, 0, 1]
    u = np.cross(d, ref); u /= np.linalg.norm(u); w = np.cross(d, u)
    a = [bm.verts.new(p0 + r * (math.cos(t) * w * f + math.sin(t) * u)) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)]
    b = [bm.verts.new(p1 + r * (math.cos(t) * w * f + math.sin(t) * u)) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)]
    for i in range(n): j = (i + 1) % n; bm.faces.new((a[i], a[j], b[j], b[i]))
    if cap: bm.faces.new(a[::-1]); bm.faces.new(b)
def cyl(bm, c, axis, r, depth, seg=20):
    rot = {'X': Matrix.Rotation(math.pi / 2, 4, 'Y'), 'Y': Matrix.Rotation(math.pi / 2, 4, 'X'), 'Z': Matrix.Identity(4)}[axis]
    bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=depth, matrix=Matrix.Translation(c) @ rot)
def box(bm, c, s, rot=None):
    r = bmesh.ops.create_cube(bm, size=1)['verts']
    for v in r:
        p = np.array(v.co) * np.array(s)
        if rot is not None: p = np.array(rot @ Vector(tuple(p)))
        v.co = tuple(np.array(c) + p)
    return r
bm = bmesh.new()
SZ = mz(266); STRUT_FUS = []
for sg in (1, -1):
    yw = 3.13; le, c = chord_at(yw); yf = side_y(394, SZ) + 0.01
    p0 = (mx(394), sg * yf, SZ); p1 = (le - 0.30 * c, sg * yw, surf(yw, 0.30, False)[2] - 0.01)
    tube(bm, p0, p1, r=0.024, f=2.4, cap=True); STRUT_FUS.append(p0)
    box(bm, (p0[0], sg * (yf - 0.01), SZ), (0.10, 0.02, 0.07))          # fuselage fitting
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
P['Struts'] = link('Struts', bm)

# ---- gear: monowheel (3-view px 388, dia 0.36), nose skid, tow hook, tail wheel, tip wheels, pitot
WHEEL_C = (mx(388), 0.0, mz(288.2)); WHEEL_R = 0.18
bm = bmesh.new(); cyl(bm, WHEEL_C, 'Y', WHEEL_R, 0.125, seg=28); P['Gear'] = link('Gear', bm)
bm = bmesh.new(); cyl(bm, WHEEL_C, 'Y', 0.075, 0.14, seg=16)
tube(bm, (mx(246), 0, mz(279)), (mx(275), 0, mz(292.3)), r=0.03, f=1.0)          # nose skid (rubber-shod)
tube(bm, (mx(275), 0, mz(292.3)), (mx(372), 0, mz(292.6)), r=0.03, f=1.0)
box(bm, (mx(262), 0, mz(287.2)), (0.07, 0.05, 0.05))                              # tow hook under the nose
box(bm, (mx(262) - 0.03, 0, mz(288.4)), (0.03, 0.012, 0.03))
TW_C = (mx(739), 0.0, mz(250.6)); TW_R = 0.05                                      # tail wheel on a sprung strut
tube(bm, (mx(716), 0, mz(232)), (TW_C[0] + 0.02, 0, TW_C[2] + 0.03), r=0.014, f=1.6, cap=True)
box(bm, (TW_C[0], 0.03, TW_C[2]), (0.08, 0.006, 0.05)); box(bm, (TW_C[0], -0.03, TW_C[2]), (0.08, 0.006, 0.05))
TIPW = []
for sg in (1, -1):                                                                  # wingtip wheels on wire bows
    yt = 7.50; le, c = chord_at(yt); xb = le - 0.35 * c; zb_ = surf(yt, 0.35, False)[2]
    tc = (xb, sg * yt, zb_ - 0.14); TIPW.append(tc)
    tube(bm, (xb + 0.10, sg * yt, zb_ + 0.005), (xb + 0.01, sg * yt, tc[2]), r=0.006, f=1.0)
    tube(bm, (xb - 0.10, sg * yt, zb_ + 0.005), (xb - 0.01, sg * yt, tc[2]), r=0.006, f=1.0)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
P['Skids'] = link('Skids', bm)
bm = bmesh.new()
cyl(bm, TW_C, 'Y', TW_R, 0.04, seg=16)
for tc in TIPW: cyl(bm, tc, 'Y', 0.05, 0.03, seg=16)
P['Tyres_Small'] = link('Tyres_Small', bm)
bm = bmesh.new()   # stainless pitot-static head on the nose: raked mast + two forward prongs
tube(bm, (mx(263.5), 0, mz(227.2) - 0.02), (mx(253.7), 0, mz(209.6)), r=0.009, f=2.5, cap=True)
for dy in (0.012, -0.012):
    tube(bm, (mx(254.5), dy, mz(209.7)), (mx(247), dy, mz(208.5)), r=0.004, f=1.0, cap=True)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
P['Pitot'] = link('Pitot', bm)
# rear-seat side windows (3-view side view quad, both sides): tinted glass laid on the skin, not cut through
WINQ = [(359.8, 202.5), (355.7, 231), (384.3, 230.4), (384.3, 211)]
bm = bmesh.new(); bmf = bmesh.new()
def lerp2(a, b, t): return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
for sg in (1, -1):
    G = []
    for i in range(7):
        row = []
        for j in range(7):
            u, v = i / 6, j / 6
            pa = lerp2(WINQ[0], WINQ[3], u); pb = lerp2(WINQ[1], WINQ[2], u); px_, py_ = lerp2(pa, pb, v)
            z = mz(py_); row.append((mx(px_), sg * (outer_y(px_, z) + 0.006), z))
        G.append(row)
    vv = [[bm.verts.new(p) for p in r] for r in G]
    for i in range(6):
        for j in range(6): bm.faces.new((vv[i][j], vv[i + 1][j], vv[i + 1][j + 1], vv[i][j + 1]))
    edge = [G[i][0] for i in range(7)] + [G[6][j] for j in range(7)] + [G[i][6] for i in range(6, -1, -1)] + [G[0][j] for j in range(6, -1, -1)]
    for p0, p1 in zip(edge, edge[1:]):
        q = [bmf.verts.new((p[0], p[1] + sg * 0.002, p[2] + dz)) for p, dz in ((p0, -0.012), (p1, -0.012), (p1, 0.012), (p0, 0.012))]
        bmf.faces.new(q)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
P['Window_Rear'] = link('Window_Rear', bm); P['Window_Rear_Frame'] = link('Window_Rear_Frame', bmf)

# ---- v8 exterior details from the photos: right-side canopy hinges, left rear door (rails, hinges, latch), canopy latch,
# wing-root skylight over the rear seat (clear, framed), boarding step under the left door
bm = bmesh.new()
def _sill(px, sg, dz=0.0):
    tp = float(np.interp(px, [p for p, _ in CAN], [q for _, q in CAN])); s_ = sec(px, tp, grow=0.012); lo = s_[0] if sg > 0 else s_[-1]
    return (lo[0], lo[1], lo[2] + dz)
for px in (287, 303, 319):                                                          # canopy hinge knuckles on the right sill
    x_, y_, z_ = _sill(px, -1, 0.012); cyl(bm, (x_, y_ - 0.006, z_), 'X', 0.011, 0.05, seg=12)
x_, y_, z_ = _sill(305, 1, 0.02); box(bm, (x_, y_ + 0.008, z_), (0.06, 0.016, 0.025))          # canopy latch, left sill
d0, d1 = _sill(326, 1, 0.004), _sill(361, 1, 0.004)
tube(bm, d0, d1, r=0.009, f=1.0, n=8)
for dz in (0.06, 0.22):                                                             # door hinges on its forward edge
    x_, y_, z_ = _sill(326.5, 1, dz); cyl(bm, (x_, y_ + 0.004, z_), 'Z', 0.009, 0.05, seg=10)
x_, y_, z_ = _sill(360, 1, 0.14); box(bm, (x_ + 0.02, y_ + 0.012, z_), (0.07, 0.012, 0.018)); box(bm, (x_ + 0.05, y_ + 0.006, z_), (0.012, 0.012, 0.03))  # door latch
SPX = 352; ZST = mz(fus(SPX)[1]) + 0.16                                             # boarding step, lower left
yo = outer_y(SPX, ZST); box(bm, (mx(SPX), yo + 0.05, ZST - 0.05), (0.11, 0.07, 0.012)); tube(bm, (mx(SPX), yo - 0.01, ZST), (mx(SPX), yo + 0.07, ZST - 0.05), r=0.008, f=1.0)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
P['Canopy_Hardware'] = link('Canopy_Hardware', bm)
bm = bmesh.new(); bmf = bmesh.new()                                                 # skylight on the pylon front, px 363..383
SKY = [363, 368, 373, 378, 383]
rows_ = []
for px in SKY:
    tp = float(np.interp(px, [p for p, _ in PYL], [q for _, q in PYL])); s_ = pyl_sec(px, tp, grow=0.006)
    zt = max(z for _, _, z in s_); rows_.append([p for p in s_ if p[2] > zt - 0.05 and abs(p[1]) < 0.20])
nmin = min(len(r) for r in rows_); rows_ = [r[(len(r) - nmin) // 2:(len(r) - nmin) // 2 + nmin] for r in rows_]
vv = [[bm.verts.new(p) for p in r] for r in rows_]
for a, b in zip(vv, vv[1:]):
    for i in range(nmin - 1): bm.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
ring_ = [rows_[0][i] for i in range(nmin)] + [rows_[k][-1] for k in range(1, len(rows_))] + [rows_[-1][i] for i in range(nmin - 2, -1, -1)] + [rows_[k][0] for k in range(len(rows_) - 2, -1, -1)]
for p0, p1 in zip(ring_, ring_[1:]): tube(bmf, (p0[0], p0[1], p0[2] + 0.003), (p1[0], p1[1], p1[2] + 0.003), r=0.008, f=1.0, n=6)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
P['Skylight_Glass'] = link('Skylight_Glass', bm); P['Skylight_Frame'] = link('Skylight_Frame', bmf)

# ---- cockpit (seats, harnesses, panels, instruments, linked controls, tube frame, liner) ----------------
exec(compile(open(os.path.join(HERE, 'sgs233_cockpit.py')).read(), os.path.join(HERE, 'sgs233_cockpit.py'), 'exec'))

# ---- paint: all-yellow scheme sampled from the Commons 3-view (255,204,0), N2033A kept ----
X0, X1, Z0p, Z1p = -6.0, 3.0, -1.2, 2.0
def side(name, flip):
    v = View(name, (X0, X1), (Z0p, Z1p), 4096, 1456, flip_a=flip, axes=(0, 2)); X, Z = v.A, v.B
    v.fill(X > -1e9, YELLOW)   # the Commons 3-view is all yellow, no band or pinstripe
    if LIV == 'talk-flying':
        # Talk Flying trim on the yellow (brand colours from Livery.cs: Black #0D0E10, Silver #C9CCD2)
        xa, xb = mx(236), mx(705)
        t = np.clip((xa - X) / (xa - xb), 0, 1); zc = mz(255) + (mz(219) - mz(255)) * t
        hh = 0.045 * (1 - t) + 0.012; inx = (X < xa) & (X > xb)
        v.fill(inx & (Z < zc + hh) & (Z > zc - hh), (13, 14, 16))
        v.fill(inx & (Z < zc - hh - 0.012) & (Z > zc - hh - 0.032), (201, 204, 210))
        for fn, xf, hgt, zb in (('tf-talkflying.png', mx(400), 0.15, mz(236) + 0.01), ('tf-logo-plate.png', mx(492), 0.26, mz(230) - 0.04), ('tf-talkflying-com.png', mx(585), 0.075, mz(224) + 0.005)):
            im = bpy.data.images.load(os.path.join(paint.ART, fn)); w_, h_ = im.size
            v.stamp(fn, xf, xf - hgt * w_ / h_, zb, zb + hgt)
    else:
        im = bpy.data.images.load(os.path.join(paint.ART, 'n2033a.png')); w_, h_ = im.size
        h = 0.16; x0 = mx(470); x1 = x0 - h * w_ / h_
        zl = mz(236) - 0.02
        v.stamp('n2033a.png', x0, x1, zl, zl + h)
    return v
VL, VR = side('sgs_side_l', True), side('sgs_side_r', False)
WING_TOP = WING_BOT = None
if LIV == 'talk-flying':
    xb0, xb1 = ROOT_LE - ROOT_C - 0.06, ROOT_LE + 0.06
    WING_TOP = View('sgs_wing_top', (-HALF - 0.05, HALF + 0.05), (xb0, xb1), 4096, 512, flip_a=True, axes=(1, 0))
    WING_BOT = View('sgs_wing_bot', (-HALF - 0.05, HALF + 0.05), (xb0, xb1), 4096, 512, flip_a=False, axes=(1, 0))
    xm = ROOT_LE - 0.45 * ROOT_C
    def word(v, png, ymid, h):
        im = bpy.data.images.load(os.path.join(paint.ART, png), check_existing=True); w_, h_ = im.size; L_ = h * w_ / h_
        v.stamp(png, ymid - L_ / 2, ymid + L_ / 2, xm - h / 2, xm + h / 2)
    YMID = 2.25
    for v in (WING_TOP, WING_BOT): v.fill(v.A > -1e9, YELLOW)
    word(WING_TOP, 'tf-wing-talkflying.png', YMID, 0.30)
    word(WING_TOP, 'tf-wing-podcast.png', -YMID, 0.30)
    word(WING_TOP, 'tf-emblem.png', 0.0, 0.48)
    word(WING_BOT, 'tf-wing-talkflying.png', -YMID, 0.30)
    word(WING_BOT, 'tf-wing-podcast.png', YMID, 0.30)
    M_WT = paint.mat_image('Paint_sgs_wing_top', WING_TOP.to_image(TEX + '/wt.png'))
    M_WB = paint.mat_image('Paint_sgs_wing_bot', WING_BOT.to_image(TEX + '/wb.png'))
def _img(fn): return bpy.data.images.load(os.path.join(paint.ART, fn), check_existing=True)
M = {'l': paint.mat_image('Paint_sgs_side_l', VL.to_image(TEX + '/l.png')),
     'r': paint.mat_image('Paint_sgs_side_r', VR.to_image(TEX + '/r.png')),
     'yellow': paint.mat_colour('Paint_yellow', YELLOW), 'white': paint.mat_colour('Paint_white', WHITE),
     'grey': paint.mat_colour('Paint_grey', GREY, rough=0.6), 'tyre': paint.mat_colour('Paint_tyre', TYRE, rough=0.8),
     'glass': paint.mat_glass('Glass'), 'glass_dark': paint.mat_glass('Glass_Tinted', (40, 52, 64), alpha=0.75),
     'steel': paint.mat_colour('Stainless', (200, 202, 206), rough=0.25, metal=0.9),
     'liner_white': paint.mat_colour('Int_Liner', (222, 223, 220), rough=0.7), 'floor_grey': paint.mat_colour('Int_Floor', (196, 198, 200), rough=0.75),
     'tube': paint.mat_colour('Int_Tube', (186, 188, 192), rough=0.5, metal=0.3), 'tube_white': paint.mat_colour('Int_Tube_White', (230, 230, 228), rough=0.45),
     'webbing': paint.mat_colour('Int_Webbing', (30, 31, 34), rough=0.85), 'fabric_grey': paint.mat_colour('Int_Seatback_Fabric', (196, 198, 198), rough=0.9),
     'metal': paint.mat_colour('Int_Metal', (190, 192, 196), rough=0.3, metal=0.8), 'alu_cast': paint.mat_colour('Int_Alu_Cast', (168, 170, 172), rough=0.55, metal=0.7),
     'chrome': paint.mat_colour('Int_Chrome', (220, 222, 226), rough=0.12, metal=1.0), 'panel': paint.mat_colour('Int_Panel', (22, 22, 24), rough=0.7),
     'coaming': paint.mat_colour('Int_Coaming', (18, 18, 20), rough=0.85), 'vent': paint.mat_colour('Int_Vent', (16, 16, 17), rough=0.45),
     'bezel': paint.mat_colour('Int_Bezel', (16, 16, 18), rough=0.5), 'needle': paint.mat_colour('Int_Needle', (245, 245, 240), rough=0.5),
     'grip': paint.mat_colour('Int_Grip', (20, 20, 20), rough=0.75), 'boot_red': paint.mat_colour('Int_Boot_Red', (140, 30, 32), rough=0.7),
     'red_channel': paint.mat_colour('Int_Channel_Maroon', (120, 28, 30), rough=0.6), 'birch': paint.mat_colour('Int_Seat_Birch', (196, 160, 112), rough=0.8),
     'cable': paint.mat_colour('Int_Cable', (60, 62, 66), rough=0.4, metal=0.6),
     'knob_yellow': paint.mat_colour('Knob_Yellow', (255, 205, 0), rough=0.35), 'knob_red': paint.mat_colour('Knob_Red', (200, 30, 25), rough=0.35),
     'yawstring': paint.mat_colour('YawString', (225, 40, 40), rough=0.9),
     'dial_asi': paint.mat_image('Dial_ASI', _img('sgs-dial-asi.png'), 0.4), 'dial_alt': paint.mat_image('Dial_ALT', _img('sgs-dial-alt.png'), 0.4),
     'dial_vario': paint.mat_image('Dial_VARIO', _img('sgs-dial-vario.png'), 0.4), 'compass': paint.mat_image('Compass_Card', _img('sgs-compass-band.png'), 0.4),
     'panel_face': paint.mat_image('Panel_Face', _img('sgs-panel-face.png'), 0.7), 'wall_airbrake': paint.mat_image('Wall_AirBrake', _img('sgs-wall-airbrake.png'), 0.7),
     'wall_wb': paint.mat_image('Wall_WB', _img('sgs-wall-wb.png'), 0.7), 'release_tube': paint.mat_image('Release_Tube', _img('sgs-release-tube.png'), 0.5),
     'cushion_stripe': paint.mat_image('Int_Cushion_Stripe', _img('sgs-cushion-stripe.png'), 0.9)}
def setm(o, names):
    o.data.materials.clear()
    for n in names: o.data.materials.append(M[n])
P['Fuselage'] = paint.join([P['Fuselage'], P.pop('Fuselage_Pylon')], 'Fuselage')
COL = {'Canopy_Glass': 'glass', 'Gear': 'tyre', 'Tyres_Small': 'tyre', 'Skids': 'grey', 'Struts': 'yellow', 'Canopy_Frame': 'yellow',
       'Pitot': 'steel', 'Window_Rear': 'glass_dark', 'Window_Rear_Frame': 'grey', 'Canopy_Hardware': 'grey',
       'Skylight_Glass': 'glass', 'Skylight_Frame': 'grey'}
for n, o in P.items():
    if n == 'Fuselage':
        setm(o, ['l', 'r']); paint.project(o, lambda nrm, c: (0, VL) if nrm.y >= 0 else (1, VR))
    elif WING_TOP is not None and n == 'Wing':
        o.data.materials.clear(); o.data.materials.append(M_WT); o.data.materials.append(M_WB)
        paint.project(o, lambda nrm, c: (0, WING_TOP) if nrm.z >= 0 else (1, WING_BOT))
    elif WING_TOP is not None and n.startswith('Spoiler_') and not n.startswith('Spoiler_Lower'):
        o.data.materials.clear(); o.data.materials.append(M_WT)
        paint.project(o, lambda nrm, c: (0, WING_TOP))
    else:
        col = CK_MAT.get(n) or COL.get(n) or 'yellow'
        if isinstance(col, list):                   # multi-material cockpit node: face material indices set at build time
            mi = [p.material_index for p in o.data.polygons]; setm(o, col)   # (materials.clear() resets the indices)
            for p, k in zip(o.data.polygons, mi): p.material_index = k
        else: setm(o, [col]); [setattr(p, 'material_index', 0) for p in o.data.polygons]
for n in ('Fuselage', 'Wing', 'Aileron_L', 'Aileron_R', 'Canopy_Glass', 'Gear', 'Canopy_Frame', 'Cockpit_Liner', 'Seat_Front_Cushion',
          'Seat_Rear_Cushion', 'Stick_F_Boot', 'Stick_R_Boot', 'Tyres_Small', 'Cockpit_SidePanels', 'Panel_Coaming', 'Panel_Vent',
          'Stick_F', 'Stick_R', 'TowRelease_F', 'TowRelease_R', 'Trim_Lever', 'Skylight_Glass'):
    for p in P[n].data.polygons: p.use_smooth = True
    try: P[n].data.set_sharp_from_angle(angle=math.radians(60))
    except Exception: pass
KEEP_WINDING = {'Cockpit_Liner', 'Cockpit_Bulkhead', 'Cockpit_Floor'} | set(CK_DECALS) | {n for n in P if n.startswith('Dial_')}
for n, o in P.items():  # outward normals (the liner faces inward by construction; dial discs face the pilot)
    if n in KEEP_WINDING: continue
    bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(o.data); bm.free()

# ---- yaw 180, origins on hinge lines / pivots, hinge markers, rig extras, hierarchy, export --------------------
from mathutils import Vector
RZ = Matrix.Rotation(math.pi, 4, 'Z'); RZ3 = RZ.to_3x3(); EXTRA = []; RIG2 = []
for o in P.values(): o.data.transform(RZ)
def w(p): return RZ @ Vector(tuple(float(c) for c in p))
def wd(v): return RZ3 @ Vector(tuple(float(c) for c in v))
def _empty(name, loc):
    e = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(e); e.location = loc; EXTRA.append(e); return e
def wloc(o):
    v = Vector(o.location)
    while o.parent is not None: o = o.parent; v = v + o.location
    return v
def gl_axis(v): v = Vector(v).normalized(); return [round(v.x, 5), round(v.z, 5), round(-v.y, 5)]
def gl_pt(v): return [round(v.x, 4), round(v.z, 4), round(-v.y, 4)]
def origin2(n, p, parent=None):
    o = P[n]; org = w(p); o.data.transform(Matrix.Translation(-org)); o.parent = parent
    o.location = org - (wloc(parent) if parent is not None else Vector()); return o
def group(name, p, parent=None):
    e = _empty(name, w(p)); e.parent = parent
    if parent is not None: e.location = w(p) - wloc(parent)
    return e
def rig2(o, param, ax, ps, vals, unit, label, tv=None, want=None, kind='rot', link='', note='', extra=None):
    ax = Vector(tuple(ax)).normalized()
    if want is not None:
        ok = False
        for sg in (1, -1):
            d = ((Matrix.Rotation(math.radians(5), 3, sg * ax) @ Vector(tv)) - Vector(tv)) if kind == 'rot' else sg * ax
            if want(d): ax = sg * ax; ok = True; break
        assert ok, 'rig sign: ' + o.name
    o['rig_param'] = param; o['rig_axis_gltf' if kind == 'rot' else 'rig_move_axis_gltf'] = gl_axis(ax)
    o['rig_map_param'] = [float(q) for q in ps]; o['rig_map_deg' if kind == 'rot' else 'rig_map_m'] = [float(q) for q in vals]
    o['rig_param_model'] = float(ps[[abs(q) < 1e-9 for q in vals].index(True)]); o['rig_unit'] = unit; o['rig_desc'] = label
    if link: o['rig_link'] = link
    if note: o['note'] = note
    for k_, v_ in (extra or {}).items(): o[k_] = v_
    RIG2.append(dict(node=o.name, parent=o.parent.name if o.parent else None, param=param, unit=unit, kind=kind, axis_gltf=gl_axis(ax),
                     pivot_gltf=gl_pt(wloc(o)), map=[[float(a_), float(b_)] for a_, b_ in zip(ps, vals)], model=o['rig_param_model'],
                     desc=label, link=link, extra=extra or {}))
    print('RIG %-22s %-13s %s axis %s pivot %s map %s  %s' % (o.name, param, kind, gl_axis(ax), gl_pt(wloc(o)),
          ' '.join('%g:%g' % (a_, b_) for a_, b_ in zip(ps, vals)), label))
    return ax
FWD, AFT, UP, STBD = Vector((-1, 0, 0)), Vector((1, 0, 0)), Vector((0, 0, 1)), Vector((0, 1, 0))
def hinge(n, p0, p1, parent=None):
    """Extra-style hinge: node origin on the hinge line at p0, top-level marker empty <n>_HingeAxis at the far end p1"""
    o = origin2(n, p0, parent); _empty(n + '_HingeAxis', w(p1)); o['hinge_axis_marker'] = n + '_HingeAxis'
    ax = w(p1) - w(p0); o['hinge_axis_gltf'] = gl_axis(ax); return o, ax
def hmid(y, f):
    le, c = chord_at(y); u, l = airfoil(np.array([f]), y); return (le - f * c, y, wz(y) + 0.5 * (u[0] + l[0]) * c)
SURF = 'FAA AFM travel'
for sd, sg in (('L', 1), ('R', -1)):
    p0, p1 = hmid(sg * A_IN, A_F), hmid(sg * A_OUT, A_F)
    o, ax = hinge('Aileron_' + sd, p0, p1); te = w(hmid(sg * 0.5 * (A_IN + A_OUT), 1.0)) - w(p0)
    if sd == 'R':
        rig2(o, 'aileron', ax, [-1, 0, 1], [-18, 0, 38], '-1..+1', 'R aileron: +1 (roll right) TE up 38, -1 TE down 18 (%s 38/18 +-2)' % SURF, tv=te, want=lambda d: d.z > 0)
    else:
        rig2(o, 'aileron', ax, [-1, 0, 1], [-38, 0, 18], '-1..+1', 'L aileron: +1 (roll right) TE down 18, -1 TE up 38 (%s)' % SURF, tv=te, want=lambda d: d.z < 0)
    o, ax = hinge('Elevator_' + sd, (SHG, sg * 0.02, ZS), (SHG, sg * SH, ZS)); te = w((ETE, sg * 0.6, ZS)) - w((SHG, sg * 0.02, ZS))
    rig2(o, 'elevator', ax, [-1, 0, 1], [-23, 0, 25], '-1..+1', 'elevator: +1 (stick aft) TE up 25, -1 TE down 23 (%s, 1080 lb rigging)' % SURF, tv=te, want=lambda d: d.z > 0)
    for nm, up, deg in (('Spoiler_', True, 85.0), ('Spoiler_Lower_', False, 75.0)):
        q0, q1 = surf(sg * SP_IN, SP_F0, up), surf(sg * SP_OUT, SP_F0, up)
        o, ax = hinge(nm + sd, q0, q1); te = w(surf(sg * 0.5 * (SP_IN + SP_OUT), SP_F1, up)) - w(q0)
        rig2(o, 'spoiler', ax, [0, 1], [0, deg], '0..1', '%s dive-brake door hinged at its front edge, opens %s %g deg at 1 (%s %g +-5)' % ('upper' if up else 'lower', 'up' if up else 'down', deg, SURF, deg),
             tv=te, want=(lambda d: d.z > 0) if up else (lambda d: d.z < 0), link='DiveBrake_Lever_F/R (same param)')
p0, p1 = (mx(HB), 0, mz(209)), (mx(HT), 0, mz(117))
o, ax = hinge('Rudder', p0, p1); te = w((mx(723), 0, mz(180))) - w(p0)
rig2(o, 'rudder', ax, [-1, 0, 1], [-30, 0, 30], '-1..+1', 'rudder on the raked hinge: +1 (right pedal) TE to starboard 30 (%s 30 +-2)' % SURF, tv=te, want=lambda d: d.y > 0)
o = origin2('Gear', WHEEL_C); o['spin_axis_gltf'] = gl_axis(wd((0, 1, 0))); o['wheel_radius_m'] = WHEEL_R
o['note'] = 'main monowheel (5.00-5 class tyre, 0.36 m dia on the 3-view); spin about spin_axis'
o = origin2('Tyres_Small', TW_C); o['note'] = 'tail wheel (origin) + both wingtip wheels, static'
# cockpit hierarchy: Cockpit > Instruments_Front / Controls / Interior (identity rotations)
CKP = group('Cockpit', (1.0, 0, -0.45))
INF = group('Instruments_Front', (PF_X, 0, ZB + 0.1), CKP)   # v8: no rear instruments (none in the photos or the POH figure)
CTL = group('Controls', (1.1, 0, ZF), CKP); INT = group('Interior', (1.0, 0, -0.45), CKP)
for n in CK_STATIC: origin2(n, (1.0, 0, -0.45), INT)
_NMAP = {'Needle_ASI': ('airspeed_mph', [0, 20, 160], [0, 15, 345], 'mph', 'ASI 20..160 mph over 15..345 deg (photographed dial layout; AFM arcs: green 38-65, yellow 65-98, red 98), rests at 0 below 20', {}),
         'Needle_Alt100': ('altitude_ft', [0, 1000], [0, 360], 'ft', 'altimeter long hand, 1 turn per 1000 ft (unbounded)', {'rig_map_extrapolate': 1}),
         'Needle_Alt1000': ('altitude_ft', [0, 10000], [0, 360], 'ft', 'altimeter short hand, 1 turn per 10000 ft (unbounded)', {'rig_map_extrapolate': 1}),
         'Needle_Vario': ('vario_kt', [-10, 0, 10], [-150, 0, 150], 'kt', 'variometer, modelled at 0 (9 o\'clock), +10 kt climb = 150 deg clockwise', {})}
for sfx, grp in (('F', INF),):
    for base_, (prm, ps, ds, un, lab, ex) in _NMAP.items():
        n_ = base_ + '_' + sfx; origin2(n_, NEEDLES[n_], grp)
        rig2(P[n_], prm, FWD, ps, ds, un, lab + '; +deg clockwise from the seat', tv=UP, want=lambda d: d.y > 0, extra=ex,
             link='front panel only (v8: rear instruments removed)')
origin2('Compass_Card', NEEDLES['Compass_Card'], INF)
rig2(P['Compass_Card'], 'heading_deg', UP, [0, 360], [0, 360], 'deg magnetic', 'wet-compass card drum: turns opposite the airplane so the heading '
     'sits under the lubber line (modelled at 000; reverse-sensing card)', tv=AFT, want=lambda d: d.y > 0, extra={'rig_map_wrap': 360.0})
tt = origin2('TorqueTube', (1.07, 0, ZTT), CTL)
rig2(tt, 'aileron', FWD, [-1, 0, 1], [-18, 0, 18], '-1..+1', 'single torque tube carrying both sticks: +1 = stick tops to the right (roll right), 18 deg (judgement)',
     tv=UP, want=lambda d: d.y > 0, link='Aileron_L/R; Stick_F and Stick_R are children (linked)')
for n_ in ('Stick_F', 'Stick_R'):
    o = origin2(n_, RIGPTS[n_], tt)
    rig2(o, 'elevator', STBD, [-1, 0, 1], [-15, 0, 18], '-1..+1', 'stick pitch on the torque tube: +1 aft 18, -1 forward 15 (judgement)', tv=UP, want=lambda d: d.x > 0,
         link='Elevator_L/R; front + rear sticks linked (same param)')
for n_ in ('Pedal_F_L', 'Pedal_F_R', 'Pedal_R_L', 'Pedal_R_R'):
    o = origin2(n_, RIGPTS[n_], CTL); right = n_.endswith('_R')
    rig2(o, 'rudder', FWD if right else AFT, [-1, 0, 1], [-0.06, 0, 0.06], '-1..+1', '%s pedal slides %s 60 mm at +1 (right rudder) (judgement)' % ('right' if right else 'left', 'forward' if right else 'aft'),
         kind='move', want=(lambda d: d.x < 0) if right else (lambda d: d.x > 0), link='Rudder; all four pedals linked (rear pedals beside the front seat)')
for n_ in ('TowRelease_F', 'TowRelease_R'):
    o = origin2(n_, RIGPTS[n_], CTL)
    rig2(o, 'tow_release', AFT, [0, 1], [0, 0.05], '0..1', 'tow release: pull the knob full out (50 mm aft) to release (AFM); yellow per Ty, AFM says red', kind='move', want=lambda d: d.x > 0,
         link='front (big yellow ball, panel bottom centre) and rear (left end of the white seat-back cross tube) knobs on one cable')
for n_ in ('DiveBrake_Lever_F', 'DiveBrake_Lever_R'):
    pv, tp = RIGPTS[n_]; o = origin2(n_, pv, CTL)
    rig2(o, 'spoiler', STBD, [0, 1], [0, 33], '0..1', 'AIR BRAKE handle in the left-wall slot: modelled stowed at the forward RETRACT end, swings aft 33 deg to EXTEND (wall placard + AFM: pull aft to extend)',
         tv=w(tp) - w(pv), want=lambda d: d.x > 0, link='Spoiler_L/R + Spoiler_Lower_L/R; front + rear levers linked; wheel_brake engages over spoiler 0.9..1.0',
         extra={'wheel_brake_from_spoiler': [0.9, 1.0]})
pv, tp = RIGPTS['Trim_Lever']; o = origin2('Trim_Lever', pv, CTL)
rig2(o, 'trim', STBD, [-1, 0, 1], [-20, 0, 20], '-1 nose down .. +1 nose up', 'red-ball trim lever low on the left wall, 4-notch quadrant: aft = nose up (judgement 20 deg each way)',
     tv=w(tp) - w(pv), want=lambda d: d.x > 0, extra={'rig_detents': [-1.0, -0.3333, 0.3333, 1.0]})
print('RIG nodes: %d' % len(RIG2))
if os.environ.get('SGS_RIGJSON'):
    import json; json.dump(dict(model='glider-2-33-like-own', livery=LIV, rig=RIG2), open(os.environ['SGS_RIGJSON'], 'w'), indent=1)
P['Fuselage'].name = 'Fuselage_Painted'
bpy.context.view_layer.update()
fin = np.concatenate([np.array([(o.matrix_world @ v.co)[:] for v in o.data.vertices]) for o in P.values()])
print('BOUNDS', fin.min(0), fin.max(0), 'size', fin.max(0) - fin.min(0))
print('PARTS', len(P), sorted(P))
print('TRIS', sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in P.values()))
os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.object.select_all(action='DESELECT')
for o in list(P.values()) + EXTRA: o.select_set(True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_yup=True, export_apply=True, export_extras=True)
print('WROTE', OUT, os.path.getsize(OUT))
