"""2-33A cockpit for build_sgs233.py (exec'd in its namespace after the airframe is built).

Layout from the Schweizer 2-33 POH cockpit figure (instrument pod at the windscreen base, release knob under it,
front dive-brake lever in a gate on the left wall, toe pedals on a nose frame, booted sticks on one torque tube,
trim lever left of the front stick, laced tubular front seat back with the rear release knob top-left, rear
dive-brake lever on the left wall just aft of the front seat, rear cushion on the floor) and the flight-manual text.
Build frame: metres, nose +X, port (pilot's LEFT) +Y, up +Z. Fills P (meshes), CK_MAT (node -> material key),
CK_UV (nodes with their own UVs) and RIGPTS (pivots / probe points for the rig)."""
CK_MAT = {}; RIGPTS = {}; CK_STATIC = []
def ck(name, bm, mat, static=True):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    P[name] = link(name, bm); CK_MAT[name] = mat
    if static: CK_STATIC.append(name)
    return P[name]
def inner_y(px, z, gap=0.035): return max(side_y(px, z) - gap, 0.02)
def xpx(x): return WING_LE_PX + (0.55 - x) / K            # build x -> 3-view px
def strip(bm, pts, wdt, thk, side=(0, 0, 1)):
    """flat strap through pts (polyline), width wdt across `side`-perp direction, thickness thk"""
    pts = [np.array(p, float) for p in pts]; nrm = np.array(side, float)
    rows = []
    for i, p in enumerate(pts):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]); d /= np.linalg.norm(d)
        a = np.cross(d, nrm); a /= np.linalg.norm(a); n2 = np.cross(a, d)
        rows.append([bm.verts.new(p + a * s * wdt / 2 + n2 * t * thk / 2) for s, t in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    for r0, r1 in zip(rows, rows[1:]):
        for k in range(4): bm.faces.new((r0[k], r0[(k + 1) % 4], r1[(k + 1) % 4], r1[k]))
    bm.faces.new(rows[0][::-1]); bm.faces.new(rows[-1])
def disc_uv(name, c, r, mat, seg=40, normal='AFT'):
    """textured dial face: disc in the y-z plane facing aft (toward the pilot); u to the pilot's right (-y), v up"""
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap'); cx, cy, cz = c
    vs = [bm.verts.new((cx, cy + r * math.sin(t), cz + r * math.cos(t))) for t in np.linspace(0, 2 * math.pi, seg, endpoint=False)]
    f = bm.faces.new(vs[::-1] if normal == 'AFT' else vs)
    for l in f.loops:
        l[uvl].uv = (0.5 - (l.vert.co.y - cy) / (2 * r), 0.5 + (l.vert.co.z - cz) / (2 * r))
    P[name] = link(name, bm); CK_MAT[name] = mat; CK_STATIC.append(name); return P[name]

# ---- interior liner (grey), floorboard, bulkheads -----------------------------------------------------------
LX0, LX1 = 247.5, 395                         # liner from the nose bulkhead to the rear seat back
lst = [s[0] for s in ST if LX0 <= s[0] <= LX1]
bm = bmesh.new(); LR = [[bm.verts.new(p) for p in ring(px, inset=0.012)] for px in lst]
for (pa, a), (pb, b) in zip(zip(lst, LR), list(zip(lst, LR))[1:]):
    for i in range(NR):
        if i < NTOP and pa >= CUT0: continue
        j = (i + 1) % NR; bm.faces.new((b[i], b[j], a[j], a[i]))
c0 = bm.verts.new((mx(LX0), 0, np.mean([v.co.z for v in LR[0]])))
for i in range(NR): bm.faces.new((LR[0][i], LR[0][(i + 1) % NR], c0))
P['Cockpit_Liner'] = link('Cockpit_Liner', bm); CK_MAT['Cockpit_Liner'] = 'liner'; CK_STATIC.append('Cockpit_Liner')
bm = bmesh.new()   # aft bulkhead behind the rear seat (px 395) up to the wing underside, and the ceiling under the pylon
rb = ring(LX1, inset=0.012)[NTOP:] + [ring(LX1, inset=0.012)[0]]
top = pyl_sec(LX1, np.interp(LX1, [p for p, _ in PYL], [q for _, q in PYL]))
arch = [(mx(LX1) + 0.002, y * 0.96, min(z, Z0 - 0.02)) for _, y, z in top]
poly = [bm.verts.new((mx(LX1) + 0.002, p[1], p[2])) for p in rb] + [bm.verts.new(p) for p in arch[1:-1]]
bm.faces.new(poly)
cs = [[(mx(px), y * 0.96, min(z, Z0 - 0.02) - 0.01) for _, y, z in pyl_sec(px, np.interp(px, [p for p, _ in PYL], [q for _, q in PYL]))] for px in (362, 373, 384, LX1)]
cv = [[bm.verts.new(p) for p in s_] for s_ in cs]
for a, b in zip(cv, cv[1:]):
    for i in range(len(a) - 1): bm.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
P['Cockpit_Bulkhead'] = link('Cockpit_Bulkhead', bm); CK_MAT['Cockpit_Bulkhead'] = 'liner'; CK_STATIC.append('Cockpit_Bulkhead')
ZF = -0.865                                   # floorboard top (fuselage bottom at the seats is z -0.965)
bm = bmesh.new(); fl = [px for px in np.linspace(262, 393, 14)]
rows = [[bm.verts.new((mx(px), s * inner_y(px, ZF, 0.02), ZF)) for s in (1, -1)] for px in fl]
rows2 = [[bm.verts.new((mx(px), s * inner_y(px, ZF, 0.02), ZF - 0.015)) for s in (1, -1)] for px in fl]
for i in range(len(fl) - 1):
    bm.faces.new((rows[i][0], rows[i][1], rows[i + 1][1], rows[i + 1][0]))
    bm.faces.new((rows2[i + 1][0], rows2[i + 1][1], rows2[i][1], rows2[i][0]))
ck('Cockpit_Floor', bm, 'plywood')

# ---- steel-tube structure inside the cockpit (welded 4130 frame, light grey), visible above and below the sill
bm = bmesh.new(); RT_ = 0.0125
FR = [279, 290, 302, 316, 331, 346, 362, 378, 393]
zs_top = lambda px: mz(fus(px)[0]) - DROP - 0.02
zs_bot = lambda px: ZF + 0.05
for sg in (1, -1):
    for a, b in zip(FR, FR[1:]):
        tube(bm, (mx(a), sg * inner_y(a, zs_top(a)), zs_top(a)), (mx(b), sg * inner_y(b, zs_top(b)), zs_top(b)), r=RT_, f=1.0, n=8)
        tube(bm, (mx(a), sg * inner_y(a, zs_bot(a)), zs_bot(a)), (mx(b), sg * inner_y(b, zs_bot(b)), zs_bot(b)), r=RT_, f=1.0, n=8)
        # diagonal (Warren truss) in the side wall
        lo, hi = (a, b) if FR.index(a) % 2 == 0 else (b, a)
        tube(bm, (mx(lo), sg * inner_y(lo, zs_bot(lo)), zs_bot(lo)), (mx(hi), sg * inner_y(hi, zs_top(hi)), zs_top(hi)), r=0.010, f=1.0, n=8)
    for a in FR:   # verticals following the side curve (3 segments)
        zz = np.linspace(zs_bot(a), zs_top(a), 4)
        for z0_, z1_ in zip(zz, zz[1:]):
            tube(bm, (mx(a), sg * inner_y(a, z0_), z0_), (mx(a), sg * inner_y(a, z1_), z1_), r=RT_, f=1.0, n=8)
for a in (279, 331, 393):   # cross tubes under the sill line (the 393 one is the rear-seat back frame top)
    z_ = zs_top(a) - (0.03 if a != 279 else 0.0)
    tube(bm, (mx(a), inner_y(a, z_), z_), (mx(a), -inner_y(a, z_), z_), r=RT_, f=1.0, n=8)
ck('Cockpit_Tubes', bm, 'tube')

# ---- front seat: laced tubular back (reclined), pan + cushions, harness -----------------------------------
SB_B, SB_T = (0.975, ZF + 0.07), (0.855, -0.13)     # seat-back bottom / top (x, z)
SW = 0.245
bm = bmesh.new()
for sg in (1, -1):
    tube(bm, (SB_B[0], sg * SW, SB_B[1]), (SB_T[0], sg * SW, SB_T[1]), r=0.013, f=1.0, n=8)
tube(bm, (SB_T[0], SW, SB_T[1]), (SB_T[0], -SW, SB_T[1]), r=0.013, f=1.0, n=8)
tube(bm, (SB_B[0], SW, SB_B[1]), (SB_B[0], -SW, SB_B[1]), r=0.013, f=1.0, n=8)
for sg in (1, -1):   # seat pan side rails to the floor
    tube(bm, (SB_B[0], sg * SW, SB_B[1]), (1.40, sg * SW, ZF + 0.07), r=0.012, f=1.0, n=8)
    tube(bm, (1.40, sg * SW, ZF + 0.07), (1.40, sg * SW, ZF), r=0.012, f=1.0, n=8)
ck('Seat_Front_Frame', bm, 'tube')
bm = bmesh.new()   # laced canvas back panel + lacing cord
def sb(t, y, off=0.0):
    x = SB_B[0] + (SB_T[0] - SB_B[0]) * t; z = SB_B[1] + (SB_T[1] - SB_B[1]) * t; return (x + off, y, z)
q = [bm.verts.new(sb(t, y, -0.004)) for t, y in ((0.04, SW - 0.03), (0.04, -SW + 0.03), (0.96, -SW + 0.03), (0.96, SW - 0.03))]
bm.faces.new(q)
ck('Seat_Front_Canvas', bm, 'canvas')
bm = bmesh.new()
for k in range(10):
    t0, t1 = 0.06 + 0.09 * k, 0.06 + 0.09 * (k + 0.5)
    for sg in (1, -1):
        tube(bm, sb(t0, sg * (SW - 0.005), 0.006), sb(t1, sg * (SW - 0.035), 0.006), r=0.003, f=1.0, n=5)
        tube(bm, sb(t1, sg * (SW - 0.035), 0.006), sb(t0 + 0.09, sg * (SW - 0.005), 0.006), r=0.003, f=1.0, n=5)
ck('Seat_Front_Lacing', bm, 'webbing')
bm = bmesh.new()   # cushions: seat pan and back pad (pilot side)
CUS_BACK = (0.5 * (SB_B[0] + SB_T[0]) + 0.035, 0.5 * (SB_B[1] + SB_T[1]) + 0.03)
bm = bmesh.new()
box(bm, (1.19, 0, ZF + 0.10), (0.40, 0.46, 0.07))
ang = math.atan2(SB_T[0] - SB_B[0], SB_T[1] - SB_B[1])
box(bm, (CUS_BACK[0], 0, CUS_BACK[1]), (0.05, 0.44, 0.54), rot=Matrix.Rotation(ang, 3, 'Y'))
ck('Seat_Front_Cushion', bm, 'cushion')
def harness(name, back_top, back_bot, pan_front, zpan, wide=0.17):
    bm = bmesh.new(); bx, bz = back_top; ux, uz = back_bot
    buckle = (pan_front[0] - 0.16, 0, zpan + 0.07)
    for sg in (1, -1):
        # shoulder strap: over the back top, down the cushion face to the buckle
        strip(bm, [(bx - 0.02, sg * 0.10, bz + 0.03), (bx + 0.05, sg * 0.10, bz - 0.02),
                   (0.5 * (bx + ux) + 0.08, sg * 0.08, 0.5 * (bz + uz) + 0.02), (buckle[0] - 0.02, sg * 0.03, buckle[2] + 0.02)], 0.045, 0.006, side=(1, 0, 0))
        # lap belt: from the floor anchor at the seat side over the cushion to the buckle
        strip(bm, [(ux + 0.05, sg * (wide + 0.06), zpan + 0.01), (ux + 0.15, sg * wide, zpan + 0.075), (buckle[0], sg * 0.05, buckle[2])], 0.05, 0.006, side=(0, 0, 1))
    ck(name, bm, 'webbing')
    bm = bmesh.new(); box(bm, buckle, (0.06, 0.08, 0.012)); box(bm, (buckle[0], 0, buckle[2] + 0.008), (0.035, 0.035, 0.006))
    ck(name + '_Buckle', bm, 'metal')
harness('Harness_Front', (SB_T[0] + 0.04, SB_T[1]), SB_B, (1.40, 0, 0), ZF + 0.065)

# ---- rear seat: back pad on the aft bulkhead, cushion on the floor, harness -----------------------------
RB_B, RB_T = (0.125, ZF + 0.06), (0.045, -0.10)
bm = bmesh.new()
box(bm, (0.33, 0, ZF + 0.045), (0.42, 0.44, 0.07))          # floor cushion
ang = math.atan2(RB_T[0] - RB_B[0], RB_T[1] - RB_B[1])
box(bm, (0.5 * (RB_B[0] + RB_T[0]) + 0.035, 0, 0.5 * (RB_B[1] + RB_T[1]) + 0.02), (0.05, 0.42, 0.62), rot=Matrix.Rotation(ang, 3, 'Y'))
ck('Seat_Rear_Cushion', bm, 'cushion')
bm = bmesh.new()   # rear seat-back frame (tubes on the bulkhead)
for sg in (1, -1): tube(bm, (RB_B[0] - 0.02, sg * 0.24, RB_B[1]), (RB_T[0] - 0.02, sg * 0.24, RB_T[1]), r=0.012, f=1.0, n=8)
tube(bm, (RB_T[0] - 0.02, 0.24, RB_T[1]), (RB_T[0] - 0.02, -0.24, RB_T[1]), r=0.012, f=1.0, n=8)
ck('Seat_Rear_Frame', bm, 'tube')
harness('Harness_Rear', (RB_T[0] + 0.06, RB_T[1]), RB_B, (0.55, 0, 0), ZF + 0.01, wide=0.18)

# ---- front instrument pod (hooded, at the windscreen base) ------------------------------------------------
PF_X = 1.645; PF_Z = -0.155; PF_W = 0.21; R_I = 0.040            # panel face x, instrument centre z, half width, 80 mm dials
bm = bmesh.new()
box(bm, (PF_X + 0.06, 0, PF_Z - 0.01), (0.12, 2 * PF_W, 0.24))     # pod body (face at PF_X)
box(bm, (PF_X + 0.05, 0, PF_Z + 0.115), (0.17, 2 * PF_W + 0.03, 0.012))   # hood / glare shield
for sg in (1, -1): box(bm, (PF_X - 0.01, sg * (PF_W + 0.008), PF_Z + 0.05), (0.06, 0.012, 0.13))   # hood cheeks
tube(bm, (PF_X + 0.06, 0, PF_Z - 0.13), (PF_X + 0.20, 0, ZF + 0.02), r=0.012, f=1.0, n=8)          # pod support to the floor frame
ck('Panel_Front', bm, 'panel')
PANEL_F = {'ASI': 0.135, 'ALT': 0.0, 'VARIO': -0.135}            # +y = pilot's left
bm = bmesh.new()
for k, y in PANEL_F.items(): cyl(bm, (PF_X - 0.008, y, PF_Z), 'X', R_I + 0.006, 0.016, seg=32)
ck('Panel_Front_Bezels', bm, 'bezel')
for k, y in PANEL_F.items(): disc_uv('Dial_%s_F' % k, (PF_X - 0.0165, y, PF_Z), R_I, 'dial_' + k.lower())
bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap')
pv = [bm.verts.new((PF_X - 0.002, y, z)) for y, z in ((0.06, PF_Z - 0.065), (-0.06, PF_Z - 0.065), (-0.06, PF_Z - 0.095), (0.06, PF_Z - 0.095))]
f = bm.faces.new(pv)
for l, uv in zip(f.loops, ((0, 1), (1, 1), (1, 0), (0, 0))): l[uvl].uv = uv
P['Placard_Front'] = link('Placard_Front', bm); CK_MAT['Placard_Front'] = 'placard'; CK_STATIC.append('Placard_Front')
NEEDLES = {}
def needle(name, c, length, w=0.004, tail=0.012, ang0=0.0, hub=0.006, mat='needle'):
    """needle in the dial plane (x = c.x), pointing at ang0 (deg clockwise from 12 o'clock as seen from the seat)"""
    bm = bmesh.new(); t = math.radians(ang0)
    d = np.array((0, -math.sin(t), math.cos(t)))                 # pilot's right = -y
    pr = np.array((0, math.cos(t), math.sin(t)))
    cc = np.array(c, float)
    pts = [cc - d * tail + pr * w / 2, cc - d * tail - pr * w / 2, cc + d * length - pr * w * 0.25, cc + d * length + pr * w * 0.25]
    a = [bm.verts.new(p) for p in pts]; b = [bm.verts.new(p + np.array((0.002, 0, 0))) for p in pts]
    bm.faces.new(a[::-1]); bm.faces.new(b)
    for i in range(4): j = (i + 1) % 4; bm.faces.new((a[i], a[j], b[j], b[i]))
    cyl(bm, tuple(cc + np.array((0.001, 0, 0))), 'X', hub, 0.004, seg=12)
    P[name] = link(name, bm); CK_MAT[name] = mat; NEEDLES[name] = tuple(cc); return P[name]
def instruments(sfx, x, zc, ys, r):
    xn = x - 0.020
    needle('Needle_ASI_' + sfx, (xn, ys['ASI'], zc), r * 0.82, w=r * 0.09)
    needle('Needle_Alt100_' + sfx, (xn, ys['ALT'], zc), r * 0.85, w=r * 0.08)
    needle('Needle_Alt1000_' + sfx, (xn + 0.0025, ys['ALT'], zc), r * 0.55, w=r * 0.13)
    needle('Needle_Vario_' + sfx, (xn, ys['VARIO'], zc), r * 0.82, w=r * 0.09, ang0=-90.0)
instruments('F', PF_X, PF_Z, PANEL_F, R_I)
# wet compass on top of the hood (centre), card drum turning about the vertical
CMP = (PF_X + 0.03, 0.0, PF_Z + 0.155)
bm = bmesh.new(); box(bm, (CMP[0] + 0.03, 0, CMP[2]), (0.06, 0.075, 0.065)); cyl(bm, (CMP[0] + 0.005, 0, CMP[2]), 'X', 0.033, 0.012, seg=24)
box(bm, (CMP[0] + 0.03, 0, CMP[2] - 0.036), (0.07, 0.05, 0.01))
ck('Compass_Housing', bm, 'bezel')
bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap'); RC, HC = 0.024, 0.018; N_ = 36
lo_ = [bm.verts.new((CMP[0] + RC * math.cos(t), RC * math.sin(t), CMP[2] - HC / 2)) for t in np.linspace(0, 2 * math.pi, N_ + 1)]
hi_ = [bm.verts.new((CMP[0] + RC * math.cos(t), RC * math.sin(t), CMP[2] + HC / 2)) for t in np.linspace(0, 2 * math.pi, N_ + 1)]
for i in range(N_):
    f = bm.faces.new((lo_[i], lo_[i + 1], hi_[i + 1], hi_[i]))
    for l in f.loops:
        k = lo_.index(l.vert) if l.vert in lo_ else hi_.index(l.vert)
        l[uvl].uv = (0.5 + k / N_, 0.0 if l.vert in lo_ else 1.0)   # aft face (t = pi) shows N at heading 0; u repeats
P['Compass_Card'] = link('Compass_Card', bm); CK_MAT['Compass_Card'] = 'compass'; NEEDLES['Compass_Card'] = CMP
# yaw string: taped to the canopy crown at x 1.60 (in view of both seats), streaming aft
_px = xpx(1.60); _tp = float(np.interp(_px, [p for p, _ in CAN], [q for _, q in CAN])); _s = sec(_px, _tp)[8]
bm = bmesh.new(); box(bm, (_s[0], 0, _s[2] + 0.004), (0.03, 0.02, 0.002))
_px2 = xpx(1.46); _s2 = sec(_px2, float(np.interp(_px2, [p for p, _ in CAN], [q for _, q in CAN])))[8]
strip(bm, [(_s[0] - 0.01, 0, _s[2] + 0.006), (0.5 * (_s[0] + _s2[0]), 0.004, 0.5 * (_s[2] + _s2[2]) + 0.012), (_s2[0], 0.0, _s2[2] + 0.016)], 0.006, 0.0015)
ck('YawString', bm, 'yawstring')

# ---- rear instrument panel: small panel on top of the front seat back (rear-seat ASI / altimeter / vario) ----
PR_X = SB_T[0] - 0.035; PR_Z = SB_T[1] + 0.075; PR_R = 0.0285      # 57 mm instruments, face toward the rear seat (aft)
PANEL_R = {'ASI': 0.075, 'ALT': 0.0, 'VARIO': -0.075}
bm = bmesh.new(); box(bm, (PR_X + 0.025, 0, PR_Z), (0.05, 0.23, 0.085))
for sg in (1, -1): tube(bm, (PR_X + 0.03, sg * 0.10, PR_Z - 0.04), (SB_T[0], sg * 0.10, SB_T[1]), r=0.008, f=1.0, n=6)
ck('Panel_Rear', bm, 'panel')
bm = bmesh.new()
for k, y in PANEL_R.items(): cyl(bm, (PR_X - 0.006, y, PR_Z), 'X', PR_R + 0.005, 0.012, seg=28)
ck('Panel_Rear_Bezels', bm, 'bezel')
for k, y in PANEL_R.items(): disc_uv('Dial_%s_R' % k, (PR_X - 0.0125, y, PR_Z), PR_R, 'dial_' + k.lower())
instruments('R', PR_X + 0.0045, PR_Z, PANEL_R, PR_R)

# ---- controls ----------------------------------------------------------------------------------------------
ZTT = ZF + 0.035                                                     # torque tube axis (on the floor centreline)
bm = bmesh.new(); tube(bm, (0.62, 0, ZTT), (1.52, 0, ZTT), r=0.019, f=1.0, n=12, cap=True)
for xb_ in (0.64, 1.50): box(bm, (xb_, 0, ZTT), (0.03, 0.045, 0.045))
P['TorqueTube'] = link('TorqueTube', bm); CK_MAT['TorqueTube'] = 'tube'
bm = bmesh.new()
for xb_ in (0.64, 1.50): box(bm, (xb_, 0, (ZTT + ZF) / 2 - 0.005), (0.06, 0.08, ZTT - ZF + 0.01))
ck('TorqueTube_Bearings', bm, 'metal')
def stick(name, x):
    bm = bmesh.new(); top = (x - 0.03, 0, -0.30)
    tube(bm, (x, 0, ZTT), top, r=0.011, f=1.0, n=10)
    tube(bm, (top[0] - 0.002, 0, top[2] - 0.01), (top[0] - 0.008, 0, top[2] + 0.10), r=0.017, f=1.0, n=12, cap=True)   # grip
    P[name] = link(name, bm); CK_MAT[name] = 'grip'; RIGPTS[name] = (x, 0, ZTT)
    bm = bmesh.new(); n = 14; rings_ = []
    for k, (h, r) in enumerate(((0.0, 0.075), (0.035, 0.06), (0.07, 0.038), (0.10, 0.022), (0.13, 0.014))):
        rings_.append([bm.verts.new((x - 0.004 * k + r * math.cos(t), r * math.sin(t), ZF + 0.004 + h)) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)])
    for a, b in zip(rings_, rings_[1:]):
        for i in range(n): j = (i + 1) % n; bm.faces.new((a[i], a[j], b[j], b[i]))
    ck(name + '_Boot', bm, 'boot')
stick('Stick_F', 1.42); stick('Stick_R', 0.70)
def pedal(name, x, y, z0, toe=True):
    """pedal hinged at its bottom (x, y, z0): an upright arm with a tread plate; +rudder translates along x"""
    bm = bmesh.new(); h = 0.20 if toe else 0.16
    tube(bm, (x, y, z0), (x - 0.03, y, z0 + h), r=0.010, f=1.0, n=8)
    box(bm, (x - 0.04, y, z0 + h - 0.05), (0.012, 0.075, 0.11 if toe else 0.08))
    P[name] = link(name, bm); CK_MAT[name] = 'metal'; RIGPTS[name] = (x, y, z0)
PDX_F, PDX_R = 2.00, 1.015
for sd, sg in (('L', 1), ('R', -1)):
    pedal('Pedal_F_' + sd, PDX_F, sg * 0.115, ZF + 0.02)
    pedal('Pedal_R_' + sd, PDX_R, sg * (SW + 0.035), ZF + 0.02, toe=False)
bm = bmesh.new()   # front pedal frame in the nose (adjustable: rails with holes -> plain rails here)
tube(bm, (PDX_F + 0.02, 0.22, ZF + 0.02), (PDX_F + 0.02, -0.22, ZF + 0.02), r=0.012, f=1.0, n=8)
for sg in (1, -1):
    tube(bm, (PDX_F + 0.18, sg * 0.16, ZF + 0.02), (PDX_F - 0.12, sg * 0.16, ZF + 0.02), r=0.010, f=1.0, n=8)
    tube(bm, (PDX_R + 0.03, sg * (SW + 0.035), ZF + 0.02), (PDX_R - 0.06, sg * (SW + 0.035), ZF + 0.02), r=0.009, f=1.0, n=8)
ck('Pedal_Frames', bm, 'tube')
def knob(name, base, length, r=0.017, mat='knob_yellow'):
    """push-pull knob: shaft from `base` aft by `length` with a ball; translates aft when pulled"""
    bm = bmesh.new(); bx, by, bz = base
    tube(bm, (bx, by, bz), (bx - length, by, bz), r=0.005, f=1.0, n=8, cap=True)
    bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=8, radius=r, matrix=Matrix.Translation((bx - length - r * 0.6, by, bz)))
    P[name] = link(name, bm); CK_MAT[name] = mat; RIGPTS[name] = (bx - length, by, bz)
knob('TowRelease_F', (PF_X, 0.0, PF_Z - 0.135), 0.03)                # centre bottom of the instrument panel
knob('TowRelease_R', (SB_T[0] - 0.015, SW - 0.04, SB_T[1] - 0.03), 0.025)   # top left of the front seat back, facing aft
bm = bmesh.new(); box(bm, (SB_T[0] - 0.008, SW - 0.04, SB_T[1] - 0.03), (0.012, 0.05, 0.05)); ck('TowRelease_R_Bracket', bm, 'metal')
def lever(name, piv, length, knob_r=0.020, mat='metal', kmat=None):
    """lever pivoted at piv (axis = y) standing up, slightly forward; handle at the top"""
    bm = bmesh.new(); px_, py_, pz_ = piv; tp = (px_ + 0.04, py_, pz_ + length)
    tube(bm, piv, tp, r=0.009, f=1.0, n=8)
    tube(bm, (tp[0], py_ + 0.0, tp[2]), (tp[0], py_ - 0.06, tp[2] + 0.005), r=knob_r * 0.6, f=1.0, n=10, cap=True)   # inboard handle
    P[name] = link(name, bm); CK_MAT[name] = mat; RIGPTS[name] = (piv, tp)
WALL = lambda x, z: inner_y(xpx(x), z, 0.045)
DBF = (1.56, WALL(1.56, -0.56), -0.56); DBR = (0.80, WALL(0.80, -0.52), -0.52)
lever('DiveBrake_Lever_F', DBF, 0.21); lever('DiveBrake_Lever_R', DBR, 0.21)
bm = bmesh.new()   # slotted gate brackets on the left wall
for (x_, y_, z_) in (DBF, DBR):
    box(bm, (x_ - 0.05, y_ + 0.018, z_ + 0.17), (0.26, 0.008, 0.07))
    box(bm, (x_ - 0.05, y_ + 0.012, z_ + 0.17 - 0.03), (0.24, 0.02, 0.008)); box(bm, (x_ - 0.05, y_ + 0.012, z_ + 0.17 + 0.03), (0.24, 0.02, 0.008))
    box(bm, (x_, y_ + 0.012, z_), (0.04, 0.02, 0.04))
ck('DiveBrake_Gates', bm, 'metal')
TRP = (1.47, 0.17, ZF + 0.02)                                       # trim lever + 4-notch quadrant, left of the front stick
lever('Trim_Lever', TRP, 0.17, mat='metal')
bm = bmesh.new(); box(bm, (TRP[0] + 0.04, TRP[1] + 0.015, TRP[2] + 0.13), (0.16, 0.006, 0.05))
for k in range(4): box(bm, (TRP[0] - 0.02 + 0.04 * k, TRP[1] + 0.012, TRP[2] + 0.158), (0.008, 0.01, 0.012))
box(bm, (TRP[0], TRP[1], TRP[2] + 0.005), (0.05, 0.05, 0.02))
ck('Trim_Quadrant', bm, 'metal')
# control knobs colour: tow release yellow (per Ty; the manual calls it red), dive brake blue, trim green
CK_MAT['DiveBrake_Lever_F'] = CK_MAT['DiveBrake_Lever_R'] = 'knob_blue'; CK_MAT['Trim_Lever'] = 'knob_green'
