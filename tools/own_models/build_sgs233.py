"""Clean-room Schweizer SGS 2-33A -> glider-2-33-like-own.glb (our own original model, no GPL input).

Every dimension below comes from public references (see docs/models/sgs233-own-PROVENANCE.md):
the Wikimedia Commons colour 3-view (digitised by eye, 1024-px display scale, 0.01548 m/px), published
2-33A data (span 15.54 m, length 7.85 m), and photos. No FlightGear or other third-party mesh is read.

  blender -b --python tools/own_models/build_sgs233.py
"""
import bpy, bmesh, sys, os, math, numpy as np
from mathutils import Matrix
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import paint
from paint import View
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(ROOT, 'unity/Assets/_Project/Resources/Models/glider-2-33-like/glider-2-33-like-own.glb')
TEX = '/tmp/own_sgs_tex'; os.makedirs(TEX, exist_ok=True)
YELLOW, WHITE, NAVY, GREY, TYRE = (255, 204, 0), (246, 246, 246), (24, 34, 90), (120, 122, 128), (88, 90, 96)
bpy.ops.wm.read_factory_settings(use_empty=True)

# ---- reference frame: Blender metres, nose +X, port +Y, up +Z (yawed 180 deg at export) -------
# 3-view side view (display px): nose tip x=228, wing chord line y=197. Wing LE kept at x=0.55 m (as the
# game's previous 2-33) so the origin / CG relation is unchanged.
K = 0.01548
WING_LE_PX = 360
def mx(px): return 0.55 - (px - WING_LE_PX) * K
def mz(py): return 0.47 + (197 - py) * K

def link(name, bm):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); return o

# ---- fuselage: 18 stations (px, top py, bottom py, half-width m, superellipse exponent) --------
# v4: nose stations re-measured on the 3-view (side-view top/bottom, top-view half-width) -> drooped, tapered nose
ST = [(228.5, 258.5, 261.5, 0.02, 2.0), (231.5, 249.6, 269.3, 0.115, 2.0), (234.7, 245.3, 272.5, 0.16, 2.0),
      (238.0, 242.1, 274.7, 0.19, 2.0), (241.1, 239.5, 276.8, 0.218, 2.0), (247.5, 235.7, 280.5, 0.255, 2.1),
      (254.0, 232.5, 283.2, 0.288, 2.2), (260.3, 230.4, 285.9, 0.308, 2.3), (266.7, 228.3, 289.6, 0.33, 2.4),
      (273.0, 226.7, 292.3, 0.34, 2.5), (284, 225, 291, 0.345, 2.6), (297, 225.5, 291, 0.352, 2.7),
      (310, 226, 291, 0.355, 2.8), (340, 226, 291, 0.355, 2.8), (362, 220, 290, 0.35, 2.8),
      (400, 214, 288, 0.335, 2.8), (440, 213, 282, 0.30, 2.8), (480, 212, 271, 0.245, 2.7),
      (530, 212, 260, 0.19, 2.6), (580, 212, 250, 0.145, 2.5), (630, 212, 240, 0.105, 2.4),
      (680, 212, 230, 0.07, 2.3), (715, 212, 223, 0.045, 2.2), (740, 214, 219, 0.015, 2.0)]
NR = 32
def ring(px, top, bot, hw, n):
    zt, zb = mz(top), mz(bot); zc, hh = (zt + zb) / 2, (zt - zb) / 2
    pts = []
    for i in range(NR):
        a = 2 * math.pi * i / NR
        c, s = math.cos(a), math.sin(a)
        pts.append((mx(px), hw * np.sign(s) * abs(s) ** (2 / n), zc + hh * np.sign(c) * abs(c) ** (2 / n)))
    return pts
bm = bmesh.new(); rings = [[bm.verts.new(p) for p in ring(*s)] for s in ST]
for a, b in zip(rings, rings[1:]):
    for i in range(NR):
        j = (i + 1) % NR; bm.faces.new((a[i], a[j], b[j], b[i]))
nose = bm.verts.new((mx(227.8), 0, mz(260))); tail = bm.verts.new((mx(741), 0, mz(216.5)))
for i in range(NR):
    j = (i + 1) % NR
    bm.faces.new((rings[0][j], rings[0][i], nose)); bm.faces.new((rings[-1][i], rings[-1][j], tail))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
P = {'Fuselage': link('Fuselage', bm)}

# ---- canopy (v4): faired into the fuselage. Each section springs from the fuselage side 8 cm below its top,
# at the fuselage's own width there, and rises as a superellipse to the crown measured on the 3-view.
STa = np.array(ST)
def fus(px):
    return [np.interp(px, STa[:, 0], STa[:, k]) for k in range(1, 5)]
def sec(px, top_py, m=2.4, M_=16, grow=0.0, drop=0.08):
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
CAN = [(279, 226.5), (285, 219.7), (293, 213.3), (301, 208.5), (309, 204.8), (317, 202.1), (325, 199.5),
       (333, 197.9), (341, 196.3), (349, 195.7), (357, 195.2), (362, 194.5)]
P['Canopy_Glass'] = loft_secs('Canopy_Glass', [sec(px, tp) for px, tp in CAN])
bm = bmesh.new()   # frame bows at the windscreen arch, between the seats, and the aft edge
for px, tp in (CAN[1], CAN[7], CAN[-1]):
    pa = sec(px + 1.3, tp, grow=0.008); pb = sec(px - 1.3, tp, grow=0.008)
    va = [bm.verts.new(p) for p in pa]; vb = [bm.verts.new(p) for p in pb]
    for i in range(len(va) - 1): bm.faces.new((va[i], va[i + 1], vb[i + 1], vb[i]))
P['Canopy_Frame'] = link('Canopy_Frame', bm)

# ---- wing (planform defined earlier from the 3-view; new loft) ---------------------------------
HALF = 15.54 / 2; Y_KINK = 3.43   # v3: kink measured on the 3-view top view (417 px from centreline at 0.00822 m/px)
ROOT_LE, ROOT_C, TIP_LE, TIP_C = 0.55, 1.52, 0.36, 0.77   # v3: tip chord 93 px, tip LE 23 px aft of root LE
Z0 = 0.47; DIH = math.radians(1.5)   # flight manual: dihedral 1.5 deg
def chord_at(y):
    y = abs(y)
    if y <= Y_KINK: return ROOT_LE, ROOT_C
    t = (y - Y_KINK) / (HALF - Y_KINK); return ROOT_LE + t * (TIP_LE - ROOT_LE), ROOT_C + t * (TIP_C - ROOT_C)
def airfoil(f):
    t, m, p = 0.13, 0.035, 0.4
    yt = 5 * t * (0.2969 * np.sqrt(f) - 0.126 * f - 0.3516 * f**2 + 0.2843 * f**3 - 0.1036 * f**4)
    yc = np.where(f < p, m / p**2 * (2 * p * f - f**2), m / (1 - p)**2 * ((1 - 2 * p) + 2 * p * f - f**2))
    return yc + yt, yc - yt
def section(y, f0, f1, n=14):
    le, c = chord_at(y); f = f0 + (f1 - f0) * (1 - np.cos(np.linspace(0, math.pi, n))) / 2
    up, lo = airfoil(f); z0 = Z0 + abs(y) * math.tan(DIH)
    pts = [(le - fi * c, y, z0 + u * c) for fi, u in zip(f, up)] + [(le - fi * c, y, z0 + l * c) for fi, l in zip(f[::-1], lo[::-1])]
    if f0 == 0: pts = pts[:n] + pts[n + 1:-1]
    return pts
def loft(name, ys, sec):
    bm = bmesh.new(); rs = [[bm.verts.new(p) for p in sec(y)] for y in ys]; m = len(rs[0])
    for a, b in zip(rs, rs[1:]):
        for i in range(m): j = (i + 1) % m; bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(rs[0][::-1]); bm.faces.new(rs[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return link(name, bm)
A_IN, A_OUT, A_F = Y_KINK + 0.10, HALF - 0.12, 0.74
SP_IN, SP_OUT = 0.25 * HALF, 0.39 * HALF
L = lambda a, b, n: list(np.linspace(a, b, n))
wing = []
for sg, sd in ((1, 'L'), (-1, 'R')):
    wing.append(loft('_wi' + sd, [sg * y for y in L(0, A_IN, 9)], lambda y: section(y, 0, 1)))
    wing.append(loft('_wf' + sd, [sg * y for y in L(A_IN, A_OUT, 7)], lambda y: section(y, 0, A_F)))
    wing.append(loft('_wt' + sd, [sg * y for y in L(A_OUT, HALF, 2)], lambda y: section(y, 0, 1)))
    P['Aileron_' + sd] = loft('Aileron_' + sd, [sg * y for y in L(A_IN, A_OUT, 7)], lambda y: section(y, A_F + 0.005, 1))
    bm = bmesh.new(); le, c = chord_at(SP_IN)
    z = Z0 + airfoil(np.array([0.43]))[0][0] * c + SP_IN * math.tan(DIH) + 0.004
    x0, x1 = le - 0.38 * c, le - 0.48 * c
    bmesh.ops.create_cube(bm, size=1)
    for v in bm.verts:
        v.co = (x0 + (x1 - x0) * (v.co.x + 0.5), sg * (SP_IN + (SP_OUT - SP_IN) * (v.co.y + 0.5)), z + 0.012 * (v.co.z + 0.5))
    P['Spoiler_' + sd] = link('Spoiler_' + sd, bm)
P['Wing'] = paint.join(wing, 'Wing')
bm = bmesh.new(); bm.from_mesh(P['Wing'].data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.001); bm.to_mesh(P['Wing'].data); bm.free()
# wing-root pylon (v4): lofted fairing following the 3-view profile from the canopy's aft end (px 362) up into the
# wing underside and down the turtle deck to px 475; sections narrow toward the top so it reads as a fillet, not a box
PYL = [(362, 194.5), (372, 190), (385, 188), (410, 188), (435, 192), (450, 197), (461, 202.7), (469, 208), (475, 211)]
def pyl_sec(px, tp, M_=16):
    s_ = sec(px, tp, m=2.4, M_=M_); zw = Z0 + 0.03; out = []
    for x, y, z in s_:
        if z > zw: y *= max(0.55, 1 - (z - zw) * 2.0); z = min(z, zw + 0.06)
        out.append((x, y, z))
    return out
P['Fuselage_Pylon'] = loft_secs('Fuselage_Pylon', [pyl_sec(px, tp) for px, tp in PYL])

# ---- tail: fin/rudder (swept LE 640,212 -> 680,117; hinge px 692) and stab/elevator -------------
def slab(name, outline, axis, t):
    """Extrude a 2-D outline (list of (x,z) or (x,y)) to a symmetric thin slab with rounded-ish edges."""
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
P['Rudder'] = slab('Rudder', [(mx(HT + 1), mz(116)), (mx(722), mz(115)), (mx(724), mz(160)), (mx(720), mz(222)), (mx(HB + 1), mz(224))], 'y', 0.05)
ZS = mz(211)
# measured on the 3-view top view (v2): span 2.30, chord 0.85, elevator 0.38 (45%), LE at side-view px 683 = 5.0 m behind wing LE
SH = 2.30 / 2; SC = 0.85; SLE = mx(683); SHG = SLE - 0.47
def rounded(y0, y1, x0, x1, r, n=6):
    pts = []
    for i in range(n + 1):   # port tip, rounded
        a = math.pi / 2 * i / n
        pts.append((x0 - r + r * math.cos(a) if False else x0 - r * (1 - math.cos(a)), y1 - r * (1 - math.sin(a))))
    return pts
# stab: LE to hinge; elevator: hinge to TE; tips rounded (top view)
def plan(x_front, x_back, rf, rb, n=6):
    pts = []
    for i in range(n + 1):
        a = math.pi / 2 * i / n; pts.append((x_front - rf * (1 - math.sin(a)), SH - rf * (1 - math.cos(a))))
    for i in range(n + 1):
        a = math.pi / 2 * i / n; pts.append((x_back + rb * (1 - math.cos(a)), SH - rb * (1 - math.sin(a))))
    return pts
RT = 0.35   # stab outline: straight LE to a rounded LE tip, square at the hinge
tip = [(SLE - RT * (1 - math.cos(math.pi / 2 * i / 6)), SH - RT + RT * math.sin(math.pi / 2 * i / 6)) for i in range(7)]
shalf = [(SLE, 0.0)] + tip + [(SHG, SH), (SHG, 0.0)]
st = shalf + [(x, -y) for x, y in shalf[::-1][1:-1]]
P['Stab'] = slab('Stab', st, 'z', 0.05)
for sg, sd in ((1, 'L'), (-1, 'R')):
    el = [(SHG - 0.01, 0.06 * sg), (SHG - 0.01, SH * sg), (SLE - SC + 0.35, SH * sg)] + \
         [(SLE - SC + 0.35 * (1 - math.sin(math.pi / 2 * i / 6)), sg * (SH - 0.35 * (1 - math.cos(math.pi / 2 * i / 6)))) for i in range(1, 7)] + \
         [(SLE - SC, 0.06 * sg)]
    if sg < 0: el = el[::-1]
    P['Elevator_' + sd] = slab('Elevator_' + sd, el, 'z', 0.035)

# ---- struts (one streamlined strut per side, fuselage px 395 lower longeron -> wing 3.13 m out) ---
def tube(bm, p0, p1, r=0.03, f=2.2, n=10):
    p0, p1 = np.array(p0), np.array(p1); d = p1 - p0; d /= np.linalg.norm(d)
    u = np.cross(d, [1, 0, 0]); u /= np.linalg.norm(u); w = np.cross(d, u)
    a = [bm.verts.new(p0 + r * (math.cos(t) * w * f + math.sin(t) * u)) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)]
    b = [bm.verts.new(p1 + r * (math.cos(t) * w * f + math.sin(t) * u)) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)]
    for i in range(n): j = (i + 1) % n; bm.faces.new((a[i], a[j], b[j], b[i]))
bm = bmesh.new()
for sg in (1, -1):
    yw = 3.13; le, c = chord_at(yw)
    tube(bm, (mx(398), sg * 0.30, mz(278)), (le - 0.30 * c, sg * yw, Z0 + yw * math.tan(DIH) - 0.06))
    tube(bm, (mx(395), sg * 0.30, mz(278)), (mx(395), sg * 0.30, mz(224)), r=0.02, f=1.0)   # cabane (side view pole)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
P['Struts'] = link('Struts', bm)

# ---- gear: monowheel (px 390, y 290, dia 0.38), nose skid, tail skid --------------------------
bm = bmesh.new()
bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=0.19, radius2=0.19, depth=0.11,
                      matrix=Matrix.Translation((mx(390), 0, mz(296))) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
P['Gear'] = link('Gear', bm)
bm = bmesh.new()
tube(bm, (mx(246), 0, mz(279)), (mx(275), 0, mz(292)), r=0.03, f=1.0)
tube(bm, (mx(275), 0, mz(292)), (mx(372), 0, mz(293)), r=0.03, f=1.0)
tube(bm, (mx(715), 0, mz(223)), (mx(728), 0, mz(232)), r=0.015, f=1.0)
P['Skids'] = link('Skids', bm)

# ---- paint: all-yellow scheme sampled from the Commons 3-view (255,204,0), N2033A kept ----
X0, X1, Z0p, Z1p = -6.0, 3.0, -1.2, 2.0
def side(name, flip):
    v = View(name, (X0, X1), (Z0p, Z1p), 4096, 1456, flip_a=flip, axes=(0, 2)); X, Z = v.A, v.B
    xn, xt = mx(236), mx(600)
    t = np.clip((xn - X) / (xn - xt), 0, 1)
    zc = mz(256) + (mz(228) - mz(256)) * t
    hh = 0.16 * (1 - t) + 0.015
    band = (X < xn) & (X > xt) & (Z < zc + hh) & (Z > zc - hh)
    v.fill(X > -1e9, YELLOW)   # v3: the Commons 3-view is all yellow, no band or pinstripe
    im = bpy.data.images.load(os.path.join(paint.ART, 'n2033a.png')); w_, h_ = im.size
    h = 0.16; x0 = mx(470); x1 = x0 - h * w_ / h_
    zl = mz(236) - 0.02
    v.stamp('n2033a.png', x0, x1, zl, zl + h)
    return v
VL, VR = side('sgs_side_l', True), side('sgs_side_r', False)
M = {'l': paint.mat_image('Paint_sgs_side_l', VL.to_image(TEX + '/l.png')),
     'r': paint.mat_image('Paint_sgs_side_r', VR.to_image(TEX + '/r.png')),
     'yellow': paint.mat_colour('Paint_yellow', YELLOW), 'white': paint.mat_colour('Paint_white', WHITE),
     'grey': paint.mat_colour('Paint_grey', GREY, rough=0.6), 'tyre': paint.mat_colour('Paint_tyre', TYRE, rough=0.8),
     'glass': paint.mat_glass('Glass')}
def setm(o, names):
    o.data.materials.clear()
    for n in names: o.data.materials.append(M[n])
P['Fuselage'] = paint.join([P['Fuselage'], P.pop('Fuselage_Pylon')], 'Fuselage')
for n, o in P.items():
    if n == 'Fuselage':
        setm(o, ['l', 'r']); paint.project(o, lambda nrm, c: (0, VL) if nrm.y >= 0 else (1, VR))
    else:
        col = {'Canopy_Glass': 'glass', 'Gear': 'tyre', 'Skids': 'grey', 'Struts': 'yellow', 'Canopy_Frame': 'yellow'}.get(n)
        if col is None: col = 'yellow' if n.startswith(('Wing', 'Aileron', 'Spoiler', 'Stab', 'Elevator', 'Fin', 'Rudder')) else 'yellow'
        setm(o, [col]); [setattr(p, 'material_index', 0) for p in o.data.polygons]
for n in ('Fuselage', 'Wing', 'Aileron_L', 'Aileron_R', 'Canopy_Glass', 'Gear', 'Canopy_Frame'):
    for p in P[n].data.polygons: p.use_smooth = True
    try: P[n].data.set_sharp_from_angle(angle=math.radians(60))
    except Exception: pass
import bmesh
for o in P.values():  # make every part's normals face outward
    bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(o.data); bm.free()
P['Fuselage'].name = 'Fuselage_Painted'
allv = np.concatenate([np.array([v.co[:] for v in o.data.vertices]) for o in P.values()])
print('BOUNDS', allv.min(0), allv.max(0), 'parts', sorted(P))
# glTF import mirrors x and OwnSpecs read nose +x: yaw 180 deg (rotation, not mirror) as the previous build did.
for o in P.values(): o.data.transform(Matrix.Rotation(math.pi, 4, 'Z'))
bpy.ops.object.select_all(action='DESELECT')
for o in P.values(): o.select_set(True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_yup=True, export_apply=True)
print('WROTE', OUT, os.path.getsize(OUT))
