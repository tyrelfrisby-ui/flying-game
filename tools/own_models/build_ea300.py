"""Clean-room Extra EA 300 (early two-seat MID-WING, c/n 047 / N300UY) -> extra-300-like-own.glb.

Our own original model: no FlightGear (or any other third-party) mesh, UV or texture is read.
Geometry is digitised by eye from Extra Flugzeugbau drawing EA-061025 "Extra 300/L 3-View-Drawing" (published as an
attachment to NZ CAA Type Acceptance Report TAR 8/21B/31) for planform, fuselage profile and tail, moved to the
EA 300's mid-wing position measured on the airport-data.com N300UY side photo; dimensions are the drawing's
(span 8000, length 6960, height 2620, tailplane span 3200, tip chord 870, fin tip chord 500, tailplane tip chord 600).
Sources: docs/models/ea300-own-PROVENANCE.md.  Paint: the approved N300UY / Sunrise Aviation scheme with TALKFLYING
(codex/fg-models v9 5fdacad parameters, re-derived on this geometry).

  blender -b --python tools/own_models/build_ea300.py
"""
import bpy, bmesh, sys, os, math, numpy as np
from mathutils import Matrix
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import paint
from paint import View
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(ROOT, 'unity/Assets/_Project/Resources/Models/extra-300-like/extra-300-like-own.glb')
TEX = '/tmp/own_ea300_tex'; os.makedirs(TEX, exist_ok=True)
NAVY, RED, WHITE = (27, 42, 74), (200, 24, 36), (246, 246, 246)
NICKEL, TYRE = (205, 208, 212), (88, 90, 96)
bpy.ops.wm.read_factory_settings(use_empty=True)

# ---- frame: Blender metres, nose +X, port +Y, up +Z, thrust line z = 0 (yawed 180 deg at export) -----------
# s = metres aft of the spinner tip, as read off the 3-view side view (0.0166 m per thumbnail px, see PROVENANCE).
# The wing root leading edge sits 1.87 m aft of the spinner tip; it is put at X = 0.43 (the config's wing LE).
S_TIP = 2.30
def X(s): return S_TIP - s

def link(name, bm):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); return o

# ---- fuselage: (s, top z, bottom z, half-width, superellipse exponent) ----------------------------------------
ST = [(0.36, 0.150, -0.200, 0.200, 2.0), (0.42, 0.190, -0.270, 0.270, 2.1), (0.55, 0.235, -0.360, 0.335, 2.3),
      (0.80, 0.275, -0.465, 0.395, 2.5), (1.10, 0.300, -0.535, 0.435, 2.6), (1.46, 0.320, -0.565, 0.450, 2.7),
      (1.90, 0.345, -0.555, 0.450, 2.7), (2.50, 0.375, -0.520, 0.440, 2.7), (3.10, 0.410, -0.470, 0.420, 2.7),
      (3.70, 0.445, -0.410, 0.375, 2.6), (4.30, 0.430, -0.340, 0.305, 2.5), (4.90, 0.355, -0.280, 0.225, 2.4),
      (5.50, 0.275, -0.230, 0.150, 2.3), (6.00, 0.225, -0.200, 0.100, 2.2), (6.40, 0.200, -0.180, 0.065, 2.1),
      (6.70, 0.185, -0.160, 0.035, 2.0), (6.82, 0.175, -0.150, 0.012, 2.0)]
NR = 36
def ring(s, top, bot, hw, n):
    zc, hh = (top + bot) / 2, (top - bot) / 2; pts = []
    for i in range(NR):
        a = 2 * math.pi * i / NR; c, sn = math.cos(a), math.sin(a)
        pts.append((X(s), hw * np.sign(sn) * abs(sn) ** (2 / n), zc + hh * np.sign(c) * abs(c) ** (2 / n)))
    return pts
bm = bmesh.new(); rings = [[bm.verts.new(p) for p in ring(*s_)] for s_ in ST]
for a, b in zip(rings, rings[1:]):
    for i in range(NR): j = (i + 1) % NR; bm.faces.new((a[i], a[j], b[j], b[i]))
nose = bm.verts.new((X(0.37), 0, -0.02)); tail = bm.verts.new((X(6.84), 0, 0.012))
for i in range(NR):
    j = (i + 1) % NR; bm.faces.new((rings[0][j], rings[0][i], nose)); bm.faces.new((rings[-1][i], rings[-1][j], tail))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
P = {'Fuselage': link('Fuselage', bm)}
STa = np.array(ST)
def fus(s): return [float(np.interp(s, STa[:, 0], STa[:, k])) for k in range(1, 5)]
topf = lambda x: np.interp(S_TIP - x, STa[:, 0], STa[:, 1])
botf = lambda x: np.interp(S_TIP - x, STa[:, 0], STa[:, 2])

# ---- canopy: blended into the fuselage contour (each section springs from the side 9 cm under the top) ----------
def sec(s, crown, m=2.3, M_=18, grow=0.0, drop=0.09):
    t, b_, hw, n = fus(s); zc, hh = (t + b_) / 2, (t - b_) / 2
    z0 = t - drop; wb = hw * max(1 - abs((z0 - zc) / hh) ** n, 0) ** (1 / n) + 0.004 + grow
    zcr = max(crown, t + 0.003) + grow
    return [(X(s), wb * np.sign(math.cos(a)) * abs(math.cos(a)) ** (2 / m), z0 + (zcr - z0) * math.sin(a) ** (2 / m))
            for a in np.linspace(0, math.pi, M_ + 1)]
def loft_secs(name, secs, cap_end=True):
    bm = bmesh.new(); cr = [[bm.verts.new(p) for p in s_] for s_ in secs]
    for a_, b2 in zip(cr, cr[1:]):
        for i in range(len(a_) - 1): bm.faces.new((a_[i], a_[i + 1], b2[i + 1], b2[i]))
    if cap_end: bm.faces.new(cr[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return link(name, bm)
# crown profile measured on the 3-view side view (windscreen foot s 2.12, crown 0.66 m, aft edge onto the deck at 4.25)
CAN = [(2.12, 0.36), (2.25, 0.45), (2.40, 0.52), (2.60, 0.575), (2.85, 0.615), (3.10, 0.640), (3.40, 0.655),
       (3.70, 0.650), (3.95, 0.615), (4.12, 0.545), (4.25, 0.46)]
P['Canopy_Glass'] = loft_secs('Canopy_Glass', [sec(s, c) for s, c in CAN])
bm = bmesh.new()
for s, c in (CAN[1], CAN[6], CAN[-2]):
    pa = sec(s + 0.025, c, grow=0.008); pb = sec(s - 0.025, c, grow=0.008)
    va = [bm.verts.new(p) for p in pa]; vb = [bm.verts.new(p) for p in pb]
    for i in range(len(va) - 1): bm.faces.new((va[i], va[i + 1], vb[i + 1], vb[i]))
P['Canopy_Frame'] = link('Canopy_Frame', bm)

# ---- wing: symmetric section, mid-wing, straight LE, tapered TE, no dihedral -----------------------------------
HALF = 4.00; ROOT_C, TIP_C = 1.62, 0.87; LE_X = X(1.87); ZW0 = -0.08
def chord(y): return ROOT_C + (TIP_C - ROOT_C) * min(abs(y), HALF) / HALF
def tk(y): return 0.16 + (0.12 - 0.16) * min(abs(y), HALF) / HALF
def foil(f, t): return 5 * t * (0.2969 * np.sqrt(f) - 0.126 * f - 0.3516 * f**2 + 0.2843 * f**3 - 0.1015 * f**4)
def section(y, f0, f1, n=16):
    c = chord(y); f = f0 + (f1 - f0) * (1 - np.cos(np.linspace(0, math.pi, n))) / 2; h = foil(f, tk(y)) * c
    pts = [(LE_X - fi * c, y, ZW0 + hi) for fi, hi in zip(f, h)] + [(LE_X - fi * c, y, ZW0 - hi) for fi, hi in zip(f[::-1], h[::-1])]
    if f0 == 0: pts = pts[:n] + pts[n + 1:-1]
    return pts
def loft(name, ys, secf):
    bm = bmesh.new(); rs = [[bm.verts.new(p) for p in secf(y)] for y in ys]; m = len(rs[0])
    for a, b in zip(rs, rs[1:]):
        for i in range(m): j = (i + 1) % m; bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(rs[0][::-1]); bm.faces.new(rs[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return link(name, bm)
# big ailerons, hinge at 74 % chord; inboard end at the config's aileron start so the rig moves all of it
A_IN, A_OUT, A_F = 1.40, 3.93, 0.74
L = lambda a, b, n: list(np.linspace(a, b, n))
wing = []
for sg, sd in ((1, 'L'), (-1, 'R')):
    wing.append(loft('_wi' + sd, [sg * y for y in L(0, A_IN, 6)], lambda y: section(y, 0, 1)))
    wing.append(loft('_wf' + sd, [sg * y for y in L(A_IN, A_OUT, 8)], lambda y: section(y, 0, A_F)))
    wing.append(loft('_wt' + sd, [sg * y for y in L(A_OUT, HALF, 2)], lambda y: section(y, 0, 1)))
    P['Aileron_' + sd] = loft('Aileron_' + sd, [sg * y for y in L(A_IN + 0.01, A_OUT - 0.01, 8)], lambda y: section(y, A_F + 0.006, 1))
P['Wing'] = paint.join(wing, 'Wing')
bm = bmesh.new(); bm.from_mesh(P['Wing'].data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.001); bm.to_mesh(P['Wing'].data); bm.free()

# ---- tail ------------------------------------------------------------------------------------------------------
def slab(name, outline, axis, t, z=0.0):
    bm = bmesh.new(); a = []; b = []
    for p, q in outline:
        if axis == 'y': a.append(bm.verts.new((p, t / 2, q))); b.append(bm.verts.new((p, -t / 2, q)))
        else: a.append(bm.verts.new((p, q, z + t / 2))); b.append(bm.verts.new((p, q, z - t / 2)))
    bm.faces.new(a); bm.faces.new(b[::-1]); n = len(a)
    for i in range(n): j = (i + 1) % n; bm.faces.new((a[j], a[i], b[i], b[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return link(name, bm)
# fin: LE from (s 5.65, deck) to the tip (s 6.25, z 1.30); tip chord 0.50 incl. rudder; rudder hinge s 6.42 -> 6.50
FIN_TOP = 1.30
P['Fin'] = slab('Fin', [(X(5.62), 0.25), (X(6.25), FIN_TOP), (X(6.41), FIN_TOP), (X(6.49), 0.12), (X(6.0), 0.18)], 'y', 0.085)
P['Rudder'] = slab('Rudder', [(X(6.425), FIN_TOP + 0.02), (X(6.66), FIN_TOP + 0.02), (X(6.80), 0.62), (X(6.965), 0.05),
                              (X(6.92), -0.10), (X(6.51), -0.10)], 'y', 0.055)
# tailplane: span 3.20, tip chord 0.60, root 0.82; straight LE at s 5.66, elevator hinge s 6.02 (45 %)
ZS = 0.10; SH = 1.60; S_LE, S_HG = X(5.66), X(6.02); TE_R, TE_T = X(6.48), X(6.26)
RT = 0.10
stab = [(S_LE, 0.0), (S_LE, SH - RT), (S_LE - RT * 0.3, SH - RT * 0.05), (S_LE - RT, SH), (S_HG, SH), (S_HG, 0.0)]
st = stab + [(x, -y) for x, y in stab[::-1][1:-1]]
P['Stab'] = slab('Stab', st, 'z', 0.06, ZS)
for sg, sd in ((1, 'L'), (-1, 'R')):
    el = [(S_HG - 0.008, 0.07 * sg), (S_HG - 0.008, SH * sg), (TE_T + 0.03, SH * sg), (TE_T, (SH - 0.04) * sg), (TE_R + 0.06, 0.07 * sg)]
    if sg < 0: el = el[::-1]
    P['Elevator_' + sd] = slab('Elevator_' + sd, el, 'z', 0.04, ZS)

# ---- undercarriage: spring-steel main legs, wheel pants, tailwheel ---------------------------------------------
def tube(bm, p0, p1, r=0.03, f=1.0, n=10):
    p0, p1 = np.array(p0, float), np.array(p1, float); d = p1 - p0; d /= np.linalg.norm(d)
    u = np.cross(d, [1, 0, 0]); u /= np.linalg.norm(u); w = np.cross(d, u)
    a = [bm.verts.new(p0 + r * (math.cos(t) * w * f + math.sin(t) * u)) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)]
    b = [bm.verts.new(p1 + r * (math.cos(t) * w * f + math.sin(t) * u)) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)]
    for i in range(n): j = (i + 1) % n; bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(a[::-1]); bm.faces.new(b)
S_AX, Y_AX, Z_AX, R_W = 1.72, 0.89, -1.17, 0.19
bm = bmesh.new()
for sg in (1, -1):   # flat leaf (2.2:1), curving out from the lower longeron to the axle
    pts = [(X(1.70), sg * 0.30, -0.53), (X(1.71), sg * 0.52, -0.78), (X(1.72), sg * 0.72, -1.00), (X(S_AX), sg * (Y_AX - 0.08), Z_AX)]
    for p0, p1 in zip(pts, pts[1:]): tube(bm, p0, p1, r=0.022, f=2.6)
pts = [(X(6.42), 0, -0.17), (X(6.62), 0, -0.27), (X(6.74), 0, -0.31)]
for p0, p1 in zip(pts, pts[1:]): tube(bm, p0, p1, r=0.014, f=2.0)
tube(bm, (X(6.74), 0, -0.31), (X(6.77), 0, -0.33), r=0.012)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Gear_Main'] = link('Gear_Main', bm)
bm = bmesh.new()
for sg in (1, -1):
    bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=R_W, radius2=R_W, depth=0.12,
                          matrix=Matrix.Translation((X(S_AX), sg * Y_AX, Z_AX)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=0.07, radius2=0.07, depth=0.05,
                      matrix=Matrix.Translation((X(6.78), 0, -0.335)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
P['Wheels'] = link('Wheels', bm)
# teardrop pants: (s offset from the axle, half-width, top, bottom) - wheel shows ~4 cm below
PANT = [(-0.36, 0.01, 0.00, -0.02), (-0.30, 0.06, 0.09, -0.11), (-0.20, 0.095, 0.16, -0.17), (-0.05, 0.105, 0.20, -0.17),
        (0.10, 0.10, 0.19, -0.165), (0.25, 0.08, 0.15, -0.13), (0.38, 0.05, 0.10, -0.08), (0.48, 0.012, 0.05, -0.02)]
bm = bmesh.new()
for sg in (1, -1):
    rs = []
    for ds, hw, t, b_ in PANT:
        zc, hh = Z_AX + (t + b_) / 2, (t - b_) / 2
        rs.append([bm.verts.new((X(S_AX + ds), sg * Y_AX + hw * math.sin(a), zc + hh * math.cos(a))) for a in np.linspace(0, 2 * math.pi, 20, endpoint=False)])
    for a_, b2 in zip(rs, rs[1:]):
        for i in range(20): j = (i + 1) % 20; bm.faces.new((a_[i], a_[j], b2[j], b2[i]))
    bm.faces.new(rs[0][::-1]); bm.faces.new(rs[-1])
bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Wheel_Pants'] = link('Wheel_Pants', bm)

# ---- spinner and two-blade propeller (diameter 2.0 m, the drawing's prop circle) -------------------------------
S_PROP, R_SP = 0.30, 0.175
bm = bmesh.new(); prof = [(0.0, 0.0)] + [(s, R_SP * math.sin(math.pi / 2 * min(s / 0.40, 1)) ** 0.75) for s in np.linspace(0.02, 0.40, 12)]
rs = [[bm.verts.new((X(s), r * math.sin(a), r * math.cos(a))) for a in np.linspace(0, 2 * math.pi, 28, endpoint=False)] for s, r in prof[1:]]
tipv = bm.verts.new((X(0.0), 0, 0))
for i in range(28): j = (i + 1) % 28; bm.faces.new((tipv, rs[0][i], rs[0][j]))
for a_, b2 in zip(rs, rs[1:]):
    for i in range(28): j = (i + 1) % 28; bm.faces.new((a_[i], b2[i], b2[j], a_[j]))
bm.faces.new(rs[-1]); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Spinner'] = link('Spinner', bm)
R_P, PITCH = 1.00, 1.9
bm = bmesh.new(); face_tag = []
for blade in (0, 1):
    rad = np.array([0, 0, 1.0]) if blade == 0 else np.array([0, 0, -1.0])
    tan = np.array([0, 1.0, 0]) if blade == 0 else np.array([0, -1.0, 0])   # direction of travel
    ax = np.array([1.0, 0, 0]); hub = np.array([X(S_PROP), 0, 0])
    rr = np.linspace(0.14, R_P, 16); secs = []; us = []
    for r in rr:
        q = (r - 0.14) / (R_P - 0.14)
        c = 0.17 - 0.05 * q - (0.08 * ((q - 0.88) / 0.12) ** 2 if q > 0.88 else 0)
        beta = math.atan(PITCH / (2 * math.pi * max(r, 0.25))); th = (0.16 - 0.10 * q) * c
        sp = []; uu = []
        for a in np.linspace(0, 2 * math.pi, 18, endpoint=False):
            u = 0.5 * c * math.cos(a); v = 0.5 * th * math.sin(a)
            cd = math.cos(beta) * tan + math.sin(beta) * ax; nd = -math.sin(beta) * tan + math.cos(beta) * ax
            sp.append(hub + r * rad + u * cd + v * nd); uu.append(u - 0.5 * c)   # 0 at the LE, negative aft
        secs.append([bm.verts.new(p) for p in sp]); us.append(uu)
    for k in range(len(secs) - 1):
        for i in range(18):
            j = (i + 1) % 18; bm.faces.new((secs[k][i], secs[k][j], secs[k + 1][j], secs[k + 1][i]))
            r_mid = 0.5 * (rr[k] + rr[k + 1]); u_mid = 0.25 * (us[k][i] + us[k][j] + us[k + 1][i] + us[k + 1][j])
            face_tag.append(1 if r_mid > R_P - 0.127 else (2 if u_mid > -0.0254 else 0))   # 5 in red tip, 1 in nickel LE
    bm.faces.new(secs[-1]); face_tag.append(1); bm.faces.new(secs[0][::-1]); face_tag.append(0)
P['Prop'] = link('Prop', bm)

# ---- paint -----------------------------------------------------------------------------------------------------
X0, X1, Z0p, Z1p = -4.80, 2.40, -1.45, 1.45
XT = X(6.80)                                   # tail cone end
XWR = LE_X - ROOT_C                            # wing-root trailing edge
ZWT = ZW0 + foil(np.array([0.30]), tk(0))[0] * ROOT_C   # wing-root top
XW, ZW = XWR + 0.15, ZWT + 0.03
ZT = ZS + 0.04                                 # navy ends just above the tailplane
_k = (ZW - ZT) / (XW - XT)
zb = lambda x: np.minimum(ZT + _k * (x - XT), ZW - 0.02)
LP1, LP2, RW = 0.085, 0.405, 0.025
FIN_LE = lambda z: X(5.62) + (X(6.25) - X(5.62)) * (z - 0.25) / (FIN_TOP - 0.25)
TH = 0.21; TX0 = XWR - 0.10; TX1 = TX0 - TH * 1555 / 203
print('NAVY LINE tail %.3f wing %.3f (x %.2f)  TALKFLYING x %.2f..%.2f  bottom there %.2f..%.2f, letters %.2f..%.2f' %
      (ZT, ZW, XW, TX0, TX1, botf(TX0), botf(TX1), zb(TX1) - 0.13 - TH, zb(TX0) - 0.13))
def side(name, flip):
    v = View(name, (X0, X1), (Z0p, Z1p), 4096, 1652, flip_a=flip, axes=(0, 2)); A, Z = v.A, v.B
    finz = (A < FIN_LE(Z) + 0.02) & (Z > topf(A) + 0.02) & (A < X(5.5))
    body = ~finz & (Z > -0.9)
    d = zb(A) - Z
    v.fill(body & (d < 0.05), NAVY)
    v.fill(body & (d >= LP1) & (d < LP1 + RW), RED)
    v.fill(body & (d >= LP2) & (d < LP2 + RW), RED)
    # fin: navy cap with three white stars and a red line under it
    v.fill(finz & (Z > FIN_TOP - 0.30), NAVY)
    v.fill(finz & (Z > FIN_TOP - 0.36) & (Z < FIN_TOP - 0.33), RED)
    for s in (6.24, 6.42, 6.60):
        v.stamp('ea-star.png', X(s) + 0.075, X(s) - 0.075, FIN_TOP - 0.255, FIN_TOP - 0.105)
    # wheel pants: navy top, red stripe
    pants = (Z < -0.9) & (A > X(S_AX + 0.55)) & (A < X(S_AX - 0.45))
    v.fill(pants & (Z > Z_AX + 0.05), NAVY)
    v.fill(pants & (Z > Z_AX + 0.005) & (Z < Z_AX + 0.03), RED)
    zt = min(zb(TX0), zb(TX1)) - 0.13
    v.stamp('ea-talkflying.png', TX0, TX1, zt - TH, zt)
    return v
VL, VR = side('ea_side_l', True), side('ea_side_r', False)
# wings and tailplane, top and bottom views (a = span y, b = x)
WSPLIT = lambda y: (LE_X - chord(y)) + 0.68 * chord(y)       # navy = front 32 % of the chord, root to tip
HLE = lambda y: LE_X - A_F * chord(y)
GAP_W = 0.03
S_SPLIT = lambda y: 0.5 * (S_LE + (TE_R + (TE_T - TE_R) * np.clip(np.abs(y) / SH, 0, 1)))
def plan(name, flip):
    v = View(name, (-4.1, 4.1), (X0, X1), 4096, 3600, flip_a=flip, axes=(1, 0)); Y, Xx = v.A, v.B
    wz = Xx > X(4.8)
    chordv = np.vectorize(chord)
    split = np.where(wz, (LE_X - chordv(Y)) + 0.68 * chordv(Y), S_SPLIT(Y))
    v.fill(Xx > split, NAVY)
    v.fill((Xx <= split - GAP_W) & (Xx > split - GAP_W - RW), RED)
    return v
lo = lambda y: HLE(np.abs(y)) + 0.04
hi = lambda y: (LE_X - chord_v(y)) + 0.68 * chord_v(y) - GAP_W - RW - 0.03
chord_v = lambda y: ROOT_C + (TIP_C - ROOT_C) * np.clip(np.abs(y), 0, HALF) / HALF
HLE = lambda y: LE_X - A_F * chord_v(y)
print('LETTER height root %.2f tip %.2f' % (hi(0.62) - lo(0.62), hi(3.62) - lo(3.62)))
VT = plan('ea_top', True); VT.stamp_warp('ea-sunrise.png', 3.62, 0.62, lo, hi); VT.stamp_warp('ea-aviation.png', -0.62, -3.62, lo, hi)
VB = plan('ea_bot', False); VB.stamp_warp('ea-talk.png', -3.62, -0.62, lo, hi); VB.stamp_warp('ea-flying.png', 0.62, 3.62, lo, hi)

M = {}
for v in (VL, VR, VT, VB): M[v.name] = paint.mat_image('Paint_' + v.name, v.to_image(os.path.join(TEX, v.name + '.png')))
M['white'] = paint.mat_colour('Paint_white', WHITE); M['navy'] = paint.mat_colour('Paint_navy', NAVY)
M['tyre'] = paint.mat_colour('Paint_tyre', TYRE, rough=0.8); M['glass'] = paint.mat_glass('Glass')
M['prop_spinner'] = paint.mat_colour('Prop_spinner', WHITE)
M['prop_blade'] = paint.mat_colour('Prop_blade', WHITE, rough=0.4)
M['prop_tip'] = paint.mat_colour('Prop_blade_tip', RED, rough=0.4)
M['prop_nickel'] = paint.mat_colour('Prop_blade_nickel', NICKEL, rough=0.18, metal=1.0)
def setm(o, names):
    o.data.materials.clear()
    for n in names: o.data.materials.append(M[n])
for n in ('Wing', 'Aileron_L', 'Aileron_R', 'Stab', 'Elevator_L', 'Elevator_R'):
    setm(P[n], ['ea_top', 'white', 'ea_bot'])
    paint.project(P[n], lambda nrm, c: (0, VT) if nrm.z >= 0.45 else ((2, VB) if nrm.z <= -0.45 else (1, None)))
for n in ('Fuselage', 'Canopy_Frame', 'Fin', 'Rudder', 'Wheel_Pants'):
    setm(P[n], ['ea_side_l', 'ea_side_r', 'white'])   # belly faces stay plain white (side paint would smear under)
    paint.project(P[n], lambda nrm, c: (2, None) if nrm.z <= -0.6 else ((0, VL) if nrm.y >= 0 else (1, VR)))
for n, m in (('Canopy_Glass', 'glass'), ('Spinner', 'prop_spinner'), ('Gear_Main', 'white'), ('Wheels', 'tyre')):
    setm(P[n], [m]); [setattr(p, 'material_index', 0) for p in P[n].data.polygons]
setm(P['Prop'], ['prop_blade', 'prop_tip', 'prop_nickel'])
for p, t in zip(P['Prop'].data.polygons, face_tag): p.material_index = t
print('PROP faces', len(face_tag), 'tip', face_tag.count(1), 'nickel', face_tag.count(2))
for o in P.values():
    bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(o.data); bm.free()
    for p in o.data.polygons: p.use_smooth = True
    try: o.data.set_sharp_from_angle(angle=math.radians(50))
    except Exception: pass
P['Fuselage'].name = 'Fuselage_Painted'
allv = np.concatenate([np.array([v.co[:] for v in o.data.vertices]) for o in P.values()])
print('BOUNDS', allv.min(0), allv.max(0), 'size', allv.max(0) - allv.min(0), 'parts', sorted(P))
for o in P.values(): o.data.transform(Matrix.Rotation(math.pi, 4, 'Z'))   # OwnSpecs read nose +x after glTF import
os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.object.select_all(action='DESELECT')
for o in P.values(): o.select_set(True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_yup=True, export_apply=True)
print('WROTE', OUT, os.path.getsize(OUT))
