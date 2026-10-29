"""2-33A cockpit for build_sgs233.py (exec'd in its namespace after the airframe is built).

v8: rebuilt to match nine real 2-33 interior photos (by eye; no third-party meshes) and the POH cockpit figure:
black D-shaped front panel under a padded black coaming at the canopy front edge (altimeter left, fresh-air eyeball
vent top centre with the wet compass under it, ASI (mph) and vario right, FLIGHT LIMITS / checklist placards, big
yellow ball tow release at the bottom centre over a RELEASE label), cast-aluminium frame rudder pedals either side of a
maroon box channel, light-grey riveted floor, chrome bent sticks with ribbed black grips in red leather boots on
riveted square plates, white liner with sculpted side panels, left-wall AIR BRAKE handles in EXTEND/RETRACT slots
(front and rear), red-ball trim lever low on the left wall, birch plywood front seat board with a red/cream striped
cushion, black webbing belts with aluminium lift-lever buckles, white seat-back cross tube with the rear RELEASE knob
at its left end, W&B placard on the rear-seat left wall. No radio / intercom / battery (omitted on purpose).
Build frame: metres, nose +X, port (pilot's LEFT) +Y, up +Z. Fills P (meshes), CK_MAT (node -> material key, or a
list of keys when the faces carry material indices), CK_STATIC and RIGPTS (pivots / probe points for the rig)."""
CK_MAT = {}; RIGPTS = {}; CK_STATIC = []; CK_DECALS = []
def ck(name, bm, mat, static=True):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    P[name] = link(name, bm); CK_MAT[name] = mat
    if static: CK_STATIC.append(name)
    return P[name]
def part(bm, k, fn, *a, **kw):
    """run a mesh helper and tag the faces it made with material index k (multi-material nodes)"""
    n0 = len(bm.faces); fn(bm, *a, **kw); bm.faces.ensure_lookup_table()
    for i in range(n0, len(bm.faces)): bm.faces[i].material_index = k
def inner_y(px, z, gap=0.035): return max(side_y(px, z) - gap, 0.02)
def xpx(x): return WING_LE_PX + (0.55 - x) / K            # build x -> 3-view px
def wall_y(x, z, gap=0.016): return inner_y(xpx(x), z, gap)
def strip(bm, pts, wdt, thk, side=(0, 0, 1)):
    """flat strap through pts (polyline), width wdt across `side`-perp direction, thickness thk"""
    pts = [np.array(p, float) for p in pts]; nrm = None if side == 'Y' else np.array(side, float)
    rows = []
    for i, p in enumerate(pts):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]); d /= np.linalg.norm(d)
        if side == 'Y': a = np.array((0.0, 1.0, 0.0)); a = a - d * np.dot(a, d); a /= np.linalg.norm(a)   # width across y
        else: a = np.cross(d, nrm); a /= np.linalg.norm(a)
        n2 = np.cross(a, d)
        rows.append([bm.verts.new(p + a * s * wdt / 2 + n2 * t * thk / 2) for s, t in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    for r0, r1 in zip(rows, rows[1:]):
        for k in range(4): bm.faces.new((r0[k], r0[(k + 1) % 4], r1[(k + 1) % 4], r1[k]))
    bm.faces.new(rows[0][::-1]); bm.faces.new(rows[-1])
def poly_tube(bm, pts, r, n=8):
    for a, b in zip(pts, pts[1:]): tube(bm, a, b, r=r, f=1.0, n=n)
def disc_uv(name, c, r, mat, seg=40, normal='AFT'):
    """textured dial face: disc in the y-z plane facing aft (toward the pilot); u to the pilot's right (-y), v up"""
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap'); cx, cy, cz = c
    vs = [bm.verts.new((cx, cy + r * math.sin(t), cz + r * math.cos(t))) for t in np.linspace(0, 2 * math.pi, seg, endpoint=False)]
    f = bm.faces.new(vs[::-1] if normal == 'AFT' else vs)
    for l in f.loops:
        l[uvl].uv = (0.5 - (l.vert.co.y - cy) / (2 * r), 0.5 + (l.vert.co.z - cz) / (2 * r))
    P[name] = link(name, bm); CK_MAT[name] = mat; CK_STATIC.append(name); return P[name]
def wall_decal(name, x0, x1, z0, z1, mat, nu=6):
    """textured quad strip on the LEFT wall facing inboard (-y); u runs aft -> forward (as seen from inside), v up;
    follows the wall slope (y = liner - 16 mm at each vertex)"""
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap')
    xs = np.linspace(x0, x1, nu + 1); zs = np.linspace(z0, z1, 4)
    V = [[bm.verts.new((x, wall_y(x, z), z)) for z in zs] for x in xs]
    for i in range(nu):
        for j in range(3):
            f = bm.faces.new((V[i][j], V[i][j + 1], V[i + 1][j + 1], V[i + 1][j]))   # winding -> normal -y (inboard)
            f.normal_update()
            if f.normal.y > 0: f.normal_flip()
            for l in f.loops:
                l[uvl].uv = ((l.vert.co.x - x0) / (x1 - x0), (l.vert.co.z - z0) / (z1 - z0))
    P[name] = link(name, bm); CK_MAT[name] = mat; CK_STATIC.append(name); CK_DECALS.append(name); return P[name]

# ---- interior liner (white), floorboard (light grey, riveted), bulkheads -------------------------------------
LX0, LX1 = 247.5, 395                         # liner from the nose bulkhead to the rear seat back
lst = [s[0] for s in ST if LX0 <= s[0] <= LX1]
bm = bmesh.new(); LR = [[bm.verts.new(p) for p in ring(px, inset=0.012)] for px in lst]
for (pa, a), (pb, b) in zip(zip(lst, LR), list(zip(lst, LR))[1:]):
    for i in range(NR):
        if i < NTOP and pa >= CUT0: continue
        j = (i + 1) % NR; bm.faces.new((b[i], b[j], a[j], a[i]))
c0 = bm.verts.new((mx(LX0), 0, np.mean([v.co.z for v in LR[0]])))
for i in range(NR): bm.faces.new((LR[0][i], LR[0][(i + 1) % NR], c0))
P['Cockpit_Liner'] = link('Cockpit_Liner', bm); CK_MAT['Cockpit_Liner'] = 'liner_white'; CK_STATIC.append('Cockpit_Liner')
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
P['Cockpit_Bulkhead'] = link('Cockpit_Bulkhead', bm); CK_MAT['Cockpit_Bulkhead'] = 'liner_white'; CK_STATIC.append('Cockpit_Bulkhead')
ZF = -0.865                                   # floorboard top (fuselage bottom at the seats is z -0.965)
bm = bmesh.new(); fl = [px for px in np.linspace(262, 393, 14)]
rows = [[bm.verts.new((mx(px), s * inner_y(px, ZF, 0.02), ZF)) for s in (1, -1)] for px in fl]
rows2 = [[bm.verts.new((mx(px), s * inner_y(px, ZF, 0.02), ZF - 0.015)) for s in (1, -1)] for px in fl]
for i in range(len(fl) - 1):
    bm.faces.new((rows[i][0], rows[i][1], rows[i + 1][1], rows[i + 1][0]))
    bm.faces.new((rows2[i + 1][0], rows2[i + 1][1], rows2[i][1], rows2[i][0]))
ck('Cockpit_Floor', bm, 'floor_grey')
bm = bmesh.new()   # rivet rows on the floor sheet (photo: two longitudinal rows each side + lateral seams)
for x_ in np.arange(0.05, 2.02, 0.055):
    for y_ in (0.105, 0.215):
        for sg in (1, -1):
            if abs(y_) < inner_y(xpx(x_), ZF, 0.03): box(bm, (x_, sg * y_, ZF + 0.001), (0.007, 0.007, 0.003))
for x_ in (0.58, 0.86, 1.33, 1.62):
    for y_ in np.arange(-0.24, 0.245, 0.04): box(bm, (x_, y_, ZF + 0.001), (0.007, 0.007, 0.003))
ck('Floor_Rivets', bm, 'metal')

# ---- tube structure visible in the cockpit: white sill longerons + floor-edge longerons, rear-seat-back cross tube
bm = bmesh.new(); RT_ = 0.0125
FR = [279, 290, 302, 316, 331, 346, 362, 378, 393]
zs_top = lambda px: mz(fus(px)[0]) - DROP - 0.02
zs_bot = lambda px: ZF + 0.05
for sg in (1, -1):
    for a, b in zip(FR, FR[1:]):
        tube(bm, (mx(a), sg * inner_y(a, zs_top(a)), zs_top(a)), (mx(b), sg * inner_y(b, zs_top(b)), zs_top(b)), r=RT_, f=1.0, n=8)
        tube(bm, (mx(a), sg * inner_y(a, zs_bot(a), 0.02), zs_bot(a)), (mx(b), sg * inner_y(b, zs_bot(b), 0.02), zs_bot(b)), r=RT_, f=1.0, n=8)
z_ = zs_top(393) - 0.03
tube(bm, (mx(393), inner_y(393, z_), z_), (mx(393), -inner_y(393, z_), z_), r=RT_, f=1.0, n=8)
ck('Cockpit_Tubes', bm, 'tube_white')

# ---- front panel: black D panel at the canopy front edge, padded coaming, instruments -----------------------
PPX = 280.5; PF_X = mx(PPX)                   # panel face x (~1.764, just aft of the canopy front edge px 279)
ZB = -0.20                                    # flat panel bottom
PW, PH = 0.31, 0.2325                         # texture rectangle (make_sgs_dials.py sgs-panel-face.png)
ZT = mz(fus(PPX)[0]) - 0.012
def _pnl_outline(x, gap):
    zz = list(np.linspace(ZB, ZT - 0.03, 9)) + list(np.linspace(ZT - 0.025, ZT, 6))
    L = [(x, max(side_y(PPX, z) - gap, 0.0), z) for z in zz]
    L = [p for p in L if p[1] > 0.015] + [(x, 0.0, ZT)]
    return L + [(x, -p[1], p[2]) for p in L[-2::-1]]
OUT_ = _pnl_outline(PF_X, 0.012)
bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap')
f = bm.faces.new([bm.verts.new(p) for p in OUT_]); f.normal_update()
if f.normal.x > 0: f.normal_flip()
for l in f.loops: l[uvl].uv = ((PW - l.vert.co.y) / (2 * PW), (l.vert.co.z - ZB) / PH)
P['Panel_Face'] = link('Panel_Face', bm); CK_MAT['Panel_Face'] = 'panel_face'; CK_STATIC.append('Panel_Face'); CK_DECALS.append('Panel_Face')
bm = bmesh.new()   # panel sheet behind the face (closed slab)
a_ = [bm.verts.new((PF_X + 0.001, y, z)) for _, y, z in OUT_]; b_ = [bm.verts.new((PF_X + 0.007, y, z)) for _, y, z in OUT_]
bm.faces.new(a_); bm.faces.new(b_[::-1])
for i in range(len(a_)): j = (i + 1) % len(a_); bm.faces.new((a_[i], a_[j], b_[j], b_[i]))
ck('Panel_Front', bm, 'panel')
bm = bmesh.new()   # padded black coaming along the arch (sill to sill)
arc_ = [p for p in _pnl_outline(PF_X - 0.008, 0.0) if p[2] > -0.075]
poly_tube(bm, [(x, y * 0.985, z + 0.008) for x, y, z in arc_], 0.019, n=10)
ck('Panel_Coaming', bm, 'coaming')
PANEL_LAYOUT = {'ALT': (+0.228, 0.064, 0.039), 'ASI': (-0.125, 0.100, 0.042), 'VARIO': (-0.217, 0.047, 0.037)}   # y, h above ZB, r
PANEL_F = {k: (y, ZB + h, r) for k, (y, h, r) in PANEL_LAYOUT.items()}
bm = bmesh.new()
for k, (y, z, r) in PANEL_F.items(): cyl(bm, (PF_X - 0.008, y, z), 'X', r + 0.006, 0.016, seg=32)
ck('Panel_Front_Bezels', bm, 'bezel')
for k, (y, z, r) in PANEL_F.items(): disc_uv('Dial_%s_F' % k, (PF_X - 0.0165, y, z), r, 'dial_' + k.lower())
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
xn = PF_X - 0.020
(ya, za, ra), (yl, zl, rl), (yv, zv, rv) = PANEL_F['ASI'], PANEL_F['ALT'], PANEL_F['VARIO']
needle('Needle_ASI_F', (xn, ya, za), ra * 0.82, w=ra * 0.09)
needle('Needle_Alt100_F', (xn, yl, zl), rl * 0.85, w=rl * 0.08)
needle('Needle_Alt1000_F', (xn + 0.0025, yl, zl), rl * 0.55, w=rl * 0.13)
needle('Needle_Vario_F', (xn, yv, zv), rv * 0.82, w=rv * 0.09, ang0=-90.0)
# fresh-air eyeball vent, top centre
VNT = (PF_X, 0.0, ZB + 0.165)
bm = bmesh.new(); cyl(bm, (VNT[0] - 0.006, 0, VNT[2]), 'X', 0.037, 0.012, seg=28)
bmesh.ops.create_uvsphere(bm, u_segments=18, v_segments=10, radius=0.027, matrix=Matrix.Translation((VNT[0] - 0.012, 0, VNT[2])))
cyl(bm, (VNT[0] - 0.042, 0, VNT[2] - 0.003), 'X', 0.013, 0.012, seg=16)
ck('Panel_Vent', bm, 'vent')
# small wet compass under the vent: open-fronted square housing on the panel, card drum turning about the vertical
CMP = (PF_X - 0.024, 0.0, ZB + 0.074)
bm = bmesh.new(); hw_, hh_, dp_ = 0.025, 0.025, 0.040
box(bm, (PF_X - dp_ / 2, 0, CMP[2] + hh_), (dp_, 2 * hw_ + 0.006, 0.005)); box(bm, (PF_X - dp_ / 2, 0, CMP[2] - hh_), (dp_, 2 * hw_ + 0.006, 0.005))
for sg in (1, -1): box(bm, (PF_X - dp_ / 2, sg * hw_, CMP[2]), (dp_, 0.005, 2 * hh_))
for sg in (1, -1): box(bm, (PF_X - dp_, sg * (hw_ - 0.006), CMP[2]), (0.004, 0.012, 2 * hh_))   # bezel cheeks
box(bm, (PF_X - dp_, 0, CMP[2] + hh_ - 0.005), (0.004, 2 * hw_, 0.010)); box(bm, (PF_X - dp_, 0, CMP[2] - hh_ + 0.005), (0.004, 2 * hw_, 0.010))
box(bm, (CMP[0] - 0.0215, 0, CMP[2]), (0.002, 0.0025, 0.026))                                 # lubber line
for sg in (1, -1): cyl(bm, (PF_X - 0.003, sg * 0.032, CMP[2] + 0.018), 'X', 0.004, 0.006, seg=8)   # mount screws
ck('Compass_Housing', bm, 'bezel')
bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap'); RC, HC = 0.019, 0.016; N_ = 36
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

# ---- nose floor: maroon box channel between the pedals, pedal hinge rails -------------------------------------
bm = bmesh.new()
c_a, c_b = np.array((1.80, 0, ZF + 0.012)), np.array((2.07, 0, -0.50))        # aft-low end on the floor -> forward-high under the deck
dv = c_b - c_a; L_ = float(np.linalg.norm(dv)); ang_ = -math.atan2(dv[2], dv[0]); R_ = Matrix.Rotation(ang_, 3, 'Y'); cm_ = (c_a + c_b) / 2
def _rb(off, s): box(bm, tuple(cm_ + np.array(R_ @ Vector(off))), s, rot=R_)
_rb((0, 0, 0.003), (L_, 0.080, 0.006)); _rb((0, 0.038, 0.045), (L_, 0.005, 0.085)); _rb((0, -0.038, 0.045), (L_, 0.005, 0.085))
_rb((-L_ / 2 + 0.004, 0, 0.045), (0.006, 0.080, 0.085))
for k in range(3): _rb((-L_ / 2 + 0.08 + 0.09 * k, 0, 0.088), (0.012, 0.080, 0.004))     # straps across the open top
ck('Nose_Channel', bm, 'red_channel')
PDX_F = 1.90; PDY_F = 0.125
bm = bmesh.new()
for sg in (1, -1):
    tube(bm, (PDX_F + 0.10, sg * PDY_F, ZF + 0.012), (PDX_F - 0.06, sg * PDY_F, ZF + 0.012), r=0.010, f=1.0, n=8)   # adjustment rails
    box(bm, (PDX_F, sg * PDY_F, ZF + 0.012), (0.03, 0.10, 0.02))
ck('Pedal_Frames', bm, 'alu_cast')

# ---- white sculpted side panels and cables ------------------------------------------------------------------
bm = bmesh.new()
for sg in (1, -1):
    v_ = box(bm, (1.09, sg * 0.285, -0.50), (0.46, 0.085, 0.34))
    bmesh.ops.bevel(bm, geom=list({e for v in v_ for e in v.link_edges}), offset=0.035, segments=3, affect='EDGES')
ck('Cockpit_SidePanels', bm, 'liner_white')
bm = bmesh.new()
for sg in (1, -1):   # rudder cables from the pedals up and aft along the walls
    poly_tube(bm, [(PDX_F - 0.02, sg * PDY_F, ZF + 0.03), (1.72, sg * (wall_y(1.72, -0.80, 0.03)), -0.78), (1.40, sg * wall_y(1.40, -0.66, 0.03), -0.66),
                   (0.55, sg * wall_y(0.55, -0.66, 0.03), -0.66), (0.10, sg * wall_y(0.10, -0.70, 0.03), -0.70)], 0.0025, n=5)
    poly_tube(bm, [(1.95, sg * wall_y(1.95, -0.45, 0.03), -0.45), (1.30, sg * wall_y(1.30, -0.40, 0.03), -0.40), (0.30, sg * wall_y(0.30, -0.40, 0.03), -0.40)], 0.0022, n=5)
ck('Cockpit_Cables', bm, 'cable')

# ---- front seat: birch board, striped cushion, light-grey back panel, white cross tube -------------------------
SB_B, SB_T = (0.955, ZF + 0.09), (0.855, -0.13)     # seat-back bottom / top (x, z)
SW = 0.245; SX0, SX1 = 0.93, 1.33; ZSEAT = ZF + 0.075
bm = bmesh.new(); box(bm, ((SX0 + SX1) / 2, 0, ZSEAT - 0.006), (SX1 - SX0, 0.44, 0.012)); ck('Seat_Front_Board', bm, 'birch')
bm = bmesh.new()   # board supports + seat-back side tubes + the cross tube wall to wall
for sg in (1, -1):
    tube(bm, (SX0 + 0.02, sg * 0.20, ZSEAT - 0.02), (SX1 - 0.02, sg * 0.20, ZSEAT - 0.02), r=0.011, f=1.0, n=8)
    tube(bm, (SX1 - 0.02, sg * 0.20, ZSEAT - 0.02), (SX1 - 0.02, sg * 0.20, ZF), r=0.011, f=1.0, n=8)
    tube(bm, (SB_B[0], sg * SW, ZF), (SB_T[0], sg * SW, SB_T[1]), r=0.013, f=1.0, n=8)
YCT = wall_y(SB_T[0], SB_T[1], 0.012)
tube(bm, (SB_T[0], YCT, SB_T[1]), (SB_T[0], -YCT, SB_T[1]), r=0.015, f=1.0, n=12)
ck('Seat_Front_Frame', bm, 'tube_white')
ang = math.atan2(SB_T[0] - SB_B[0], SB_T[1] - SB_B[1])
bm = bmesh.new(); box(bm, (0.5 * (SB_B[0] + SB_T[0]) + 0.004, 0, 0.5 * (SB_B[1] + SB_T[1]) - 0.01), (0.008, 2 * SW - 0.02, 0.74), rot=Matrix.Rotation(ang, 3, 'Y'))
ck('Seat_Front_Back', bm, 'fabric_grey')
def cushion(name, c, s, back=None):
    """striped cushion: rounded box with UVs (u along y, v along x) so the stripes run across the seat"""
    bm = bmesh.new(); v_ = box(bm, c, s)
    bmesh.ops.bevel(bm, geom=list({e for v in v_ for e in v.link_edges}), offset=0.015, segments=2, affect='EDGES')
    if back:
        bc, bs, br = back; v2 = box(bm, bc, bs, rot=br)
        bmesh.ops.bevel(bm, geom=list({e for v in v2 for e in v.link_edges}), offset=0.015, segments=2, affect='EDGES')
    uvl = bm.loops.layers.uv.new('UVMap')
    for f in bm.faces:
        for l in f.loops: l[uvl].uv = (l.vert.co.y * 1.2, (l.vert.co.x + l.vert.co.z) * 1.3)
    ck(name, bm, 'cushion_stripe')
cushion('Seat_Front_Cushion', ((SX0 + SX1) / 2 + 0.01, 0, ZSEAT + 0.03), (0.36, 0.40, 0.06),
        back=((0.5 * (SB_B[0] + SB_T[0]) + 0.04, 0, 0.5 * (SB_B[1] + SB_T[1]) + 0.04), (0.06, 0.40, 0.46), Matrix.Rotation(ang, 3, 'Y')))
bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap')   # RELEASE placard sleeve on the cross tube's left end (faces aft/up)
y0_, y1_, rr_ = 0.31, 0.17, 0.0165
th_ = np.linspace(math.radians(100), math.radians(250), 7)      # around the tube axis: 90 = up, 180 = aft
V = [[bm.verts.new((SB_T[0] + rr_ * math.cos(t), y, SB_T[1] + rr_ * math.sin(t))) for t in th_] for y in (min(y0_, YCT - 0.01), y1_)]
for j in range(len(th_) - 1):
    f = bm.faces.new((V[0][j], V[1][j], V[1][j + 1], V[0][j + 1])); f.normal_update()
    mid = np.array(f.calc_center_median()) - np.array((SB_T[0], f.calc_center_median().y, SB_T[1]))
    if np.dot(np.array(f.normal), mid) < 0: f.normal_flip()
    for l in f.loops:
        tt = (math.atan2(l.vert.co.z - SB_T[1], l.vert.co.x - SB_T[0]) % (2 * math.pi))
        l[uvl].uv = ((y0_ - l.vert.co.y) / (y0_ - y1_), (th_[-1] - tt) / (th_[-1] - th_[0]))
P['Placard_Release_Tube'] = link('Placard_Release_Tube', bm); CK_MAT['Placard_Release_Tube'] = 'release_tube'; CK_STATIC.append('Placard_Release_Tube'); CK_DECALS.append('Placard_Release_Tube')
def harness(name, back_top, back_bot, buckle, zpan, wide=0.20, pad=0.075):
    """black webbing: shoulder straps from an anchor behind the seat back, over the back top (cross tube) and down the
    cushion face; lap belts from the board's rear corners; aluminium lift-lever buckle"""
    bm = bmesh.new(); bx, bz = back_top; ux, uz = back_bot
    def face_x(z): return ux + (z - uz) * (bx - ux) / (bz - uz) + pad        # front face of the back cushion
    zm = 0.5 * (bz + buckle[2]) + 0.05
    for sg in (1, -1):
        strip(bm, [(bx - 0.06, sg * 0.10, bz - 0.20), (bx - 0.03, sg * 0.095, bz + 0.005), (bx, sg * 0.09, bz + 0.024),
                   (bx + 0.035, sg * 0.088, bz + 0.012), (face_x(zm), sg * 0.075, zm), (buckle[0] - 0.03, sg * 0.03, buckle[2] + 0.02)], 0.048, 0.005, side='Y')
        strip(bm, [(ux + pad, sg * (wide + 0.02), zpan), (ux + pad + 0.06, sg * wide, zpan + 0.06), (buckle[0] - 0.01, sg * 0.04, buckle[2])], 0.05, 0.005, side=(0, 0, 1))
    ck(name, bm, 'webbing')
    bm = bmesh.new(); bxx, _, bzz = buckle
    box(bm, (bxx, 0, bzz + 0.004), (0.075, 0.055, 0.006))                       # latch body
    box(bm, (bxx + 0.005, 0, bzz + 0.011), (0.06, 0.036, 0.006), rot=Matrix.Rotation(math.radians(-8), 3, 'Y'))   # lift lever
    cyl(bm, (bxx - 0.03, 0, bzz + 0.01), 'Y', 0.006, 0.05, seg=10)
    for sg in (1, -1): box(bm, (bxx - 0.045, sg * 0.03, bzz + 0.004), (0.03, 0.012, 0.004))   # strap tangs
    ck(name + '_Buckle', bm, 'metal')
harness('Harness_Front', SB_T, (SB_B[0], SB_B[1]), (1.10, 0, ZSEAT + 0.075), ZSEAT + 0.065)

# ---- rear seat: striped cushion on the floor + back pad, white back frame, harness ------------------------------
RB_B, RB_T = (0.125, ZF + 0.06), (0.045, -0.10)
ang_r = math.atan2(RB_T[0] - RB_B[0], RB_T[1] - RB_B[1])
cushion('Seat_Rear_Cushion', (0.33, 0, ZF + 0.045), (0.42, 0.44, 0.07),
        back=((0.5 * (RB_B[0] + RB_T[0]) + 0.035, 0, 0.5 * (RB_B[1] + RB_T[1]) + 0.02), (0.05, 0.42, 0.62), Matrix.Rotation(ang_r, 3, 'Y')))
bm = bmesh.new()
for sg in (1, -1): tube(bm, (RB_B[0] - 0.02, sg * 0.24, RB_B[1]), (RB_T[0] - 0.02, sg * 0.24, RB_T[1]), r=0.012, f=1.0, n=8)
tube(bm, (RB_T[0] - 0.02, 0.24, RB_T[1]), (RB_T[0] - 0.02, -0.24, RB_T[1]), r=0.012, f=1.0, n=8)
ck('Seat_Rear_Frame', bm, 'tube_white')
harness('Harness_Rear', RB_T, RB_B, (0.40, 0, ZF + 0.095), ZF + 0.08, wide=0.20, pad=0.06)

# ---- controls ----------------------------------------------------------------------------------------------
ZTT = ZF - 0.042                                                     # torque tube axis (under the floor sheet, hidden)
bm = bmesh.new(); tube(bm, (0.62, 0, ZTT), (1.56, 0, ZTT), r=0.019, f=1.0, n=12, cap=True)
for xb_ in (0.64, 1.54): box(bm, (xb_, 0, ZTT), (0.03, 0.045, 0.045))
P['TorqueTube'] = link('TorqueTube', bm); CK_MAT['TorqueTube'] = 'tube'
bm = bmesh.new()
for xb_ in (0.64, 1.54): box(bm, (xb_, 0, ZTT - 0.01), (0.06, 0.08, 0.04))
ck('TorqueTube_Bearings', bm, 'metal')
def stick(name, x):
    """chrome bent tube with a ribbed black grip (mat 0 chrome, 1 grip); red leather boot on a riveted square plate"""
    bm = bmesh.new()
    pts = [(x, 0, ZTT), (x, 0, ZF + 0.10), (x - 0.015, 0, ZF + 0.25), (x - 0.045, 0, -0.42)]
    part(bm, 0, poly_tube, pts, 0.010, n=10)
    g0 = np.array((x - 0.045, 0, -0.42)); g1 = np.array((x - 0.058, 0, -0.30)); NR_ = 9
    for k in range(NR_):
        a_ = g0 + (g1 - g0) * k / NR_; b_ = g0 + (g1 - g0) * (k + 1) / NR_
        part(bm, 1, tube, a_, b_, r=0.0175 if k % 2 == 0 else 0.0155, f=1.0, n=12, cap=True)
    P[name] = link(name, bm); CK_MAT[name] = ['chrome', 'grip']; RIGPTS[name] = (x, 0, ZTT)
    bm = bmesh.new(); n = 16; rings_ = []
    for k, (h, r) in enumerate(((0.0, 0.085), (0.03, 0.075), (0.06, 0.055), (0.09, 0.035), (0.12, 0.02), (0.145, 0.013))):
        rings_.append([bm.verts.new((x - 0.003 * k + r * math.cos(t) * (1.0 + 0.08 * math.sin(3 * t + k)), r * math.sin(t), ZF + 0.006 + h)) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)])
    for a, b in zip(rings_, rings_[1:]):
        for i in range(n): j = (i + 1) % n; bm.faces.new((a[i], a[j], b[j], b[i]))
    part(bm, 0, box, (x, 0, ZF + 0.003), (0.23, 0.23, 0.006))
    for (rx, ry) in [(dx, dy) for dx in (-0.1, 0.0, 0.1) for dy in (-0.1, 0.1)] + [(dx, dy) for dx in (-0.1, 0.1) for dy in (0.0,)]:
        part(bm, 1, cyl, (x + rx, ry, ZF + 0.0065), 'Z', 0.005, 0.003, seg=8)
    ck(name + '_Boot', bm, ['boot_red', 'metal'])
stick('Stick_F', 1.46); stick('Stick_R', 0.70)
def pedal(name, x, y, z0, w=0.11, h=0.22, rec=14.0):
    """cast-aluminium frame pedal hinged at its bottom (x, y, z0): side rails, bottom bar, middle bar, arched top with a
    name band, reclined aft by `rec` deg; +rudder translates along x (no maker's lettering)"""
    bm = bmesh.new(); R = Matrix.Rotation(math.radians(-rec), 3, 'Y'); o = np.array((x, y, z0)); th = 0.012
    def pb(s, t, ws, ht, a=0.0):
        rr = R @ Matrix.Rotation(math.radians(a), 3, 'X'); box(bm, tuple(o + np.array(R @ Vector((0, s, t)))), (th, ws, ht), rot=rr)
    for sg in (1, -1): pb(sg * (w / 2 - 0.009), (h - w / 2) / 2, 0.018, h - w / 2)
    pb(0, 0.012, w, 0.024); pb(0, 0.50 * h, w, 0.026); pb(0, 0.72 * h, w, 0.03)
    rc = h - w / 2
    for k in range(6):
        t0, t1 = math.pi * k / 6, math.pi * (k + 1) / 6; tm = 0.5 * (t0 + t1)
        s_, t_ = (w / 2 - 0.009) * math.cos(tm), rc + (w / 2 - 0.009) * math.sin(tm)
        pb(s_, t_, 0.018, (w / 2) * math.pi / 6 + 0.004, a=math.degrees(tm))
    P[name] = link(name, bm); CK_MAT[name] = 'alu_cast'; RIGPTS[name] = (x, y, z0)
PDX_R = 1.015
for sd, sg in (('L', 1), ('R', -1)):
    pedal('Pedal_F_' + sd, PDX_F, sg * PDY_F, ZF + 0.012, w=0.12, h=0.29)
    pedal('Pedal_R_' + sd, PDX_R, sg * 0.255, ZF + 0.012, w=0.085, h=0.16)
def knob(name, base, length, r=0.017, mat='knob_yellow'):
    """push-pull knob: shaft from `base` aft by `length` with a ball; translates aft when pulled"""
    bm = bmesh.new(); bx, by, bz = base
    tube(bm, (bx, by, bz), (bx - length, by, bz), r=0.005, f=1.0, n=8, cap=True)
    bmesh.ops.create_uvsphere(bm, u_segments=18, v_segments=10, radius=r, matrix=Matrix.Translation((bx - length - r * 0.6, by, bz)))
    P[name] = link(name, bm); CK_MAT[name] = mat; RIGPTS[name] = (bx - length, by, bz)
knob('TowRelease_F', (PF_X, 0.0, ZB + 0.010), 0.012, r=0.024)            # big yellow ball, panel bottom centre over RELEASE
TRR = (SB_T[0] - 0.02, YCT - 0.02, SB_T[1] + 0.005)                    # rear knob at the cross tube's left end, facing aft
knob('TowRelease_R', TRR, 0.02, r=0.018)
bm = bmesh.new(); box(bm, (TRR[0] + 0.006, TRR[1], TRR[2]), (0.012, 0.04, 0.04))
poly_tube(bm, [(TRR[0] + 0.01, TRR[1], TRR[2] - 0.02), (TRR[0] + 0.03, TRR[1] + 0.005, SB_T[1] - 0.15), (TRR[0] + 0.08, wall_y(TRR[0] + 0.08, -0.45, 0.03), -0.45),
               (1.60, wall_y(1.60, -0.45, 0.03), -0.45), (PF_X + 0.02, 0.05, ZB - 0.02)], 0.0025, n=5)
ck('TowRelease_R_Bracket', bm, 'metal')
def airbrake(name, xm, zs_, L=0.30, half=16.5):
    """AIR BRAKE handle: lever pivoted behind the left wall panel, black grip through the slot, stowed at the forward
    (RETRACT) end; mat 0 metal arm, 1 black grip. Modelled tilted forward by `half` deg."""
    yw = inner_y(xpx(xm), zs_, 0.004)                # arm between the liner and the skin (hidden), stub through the slot
    pz = zs_ - L * math.cos(math.radians(half)); piv = (xm, yw, pz); a = math.radians(half)
    tp = (xm + L * math.sin(a), yw, pz + L * math.cos(a))
    bm = bmesh.new()
    part(bm, 0, box, ((piv[0] + tp[0]) / 2, yw, (piv[2] + tp[2]) / 2), (0.016, 0.006, L), rot=Matrix.Rotation(a, 3, 'Y'))
    yd = wall_y(tp[0], tp[2])
    part(bm, 0, tube, tp, (tp[0], yd - 0.012, tp[2]), r=0.006, f=1.0, n=8)
    g0 = (tp[0], yd - 0.012, tp[2]); g1 = (tp[0] - 0.005, yd - 0.095, tp[2] + 0.012)
    for k in range(6):
        a_ = np.array(g0) + (np.array(g1) - np.array(g0)) * k / 6; b_ = np.array(g0) + (np.array(g1) - np.array(g0)) * (k + 1) / 6
        part(bm, 1, tube, a_, b_, r=0.0125 if k % 2 == 0 else 0.011, f=1.0, n=10, cap=True)
    P[name] = link(name, bm); CK_MAT[name] = ['metal', 'grip']; RIGPTS[name] = (piv, tp)
    return piv
# decal 0.32 x 0.12 m; slot centre at 0.62 of the height from the top (sgs-wall-airbrake.png) -> zs_ = z1 - 0.12 * 0.618
for nm, x0, z1 in (('F', 1.34, -0.18), ('R', 0.44, -0.27)):
    wall_decal('Wall_AirBrake_' + nm, x0, x0 + 0.32, z1 - 0.12, z1, 'wall_airbrake')
    airbrake('DiveBrake_Lever_' + nm, x0 + 0.32 * 0.615, z1 - 0.12 * 0.618)
wall_decal('Wall_WB_Placard', 0.36, 0.86, -0.255, -0.18, 'wall_wb')
TRP = (1.40, wall_y(1.40, -0.62) - 0.02, -0.62)                     # red-ball trim lever low on the left wall
bm = bmesh.new(); tp_ = (TRP[0] + 0.03, TRP[1] - 0.03, TRP[2] + 0.15)
part(bm, 0, tube, TRP, tp_, r=0.007, f=1.0, n=8)
part(bm, 1, lambda b_: bmesh.ops.create_uvsphere(b_, u_segments=16, v_segments=9, radius=0.019, matrix=Matrix.Translation(tp_)))
P['Trim_Lever'] = link('Trim_Lever', bm); CK_MAT['Trim_Lever'] = ['tube_white', 'knob_red']; RIGPTS['Trim_Lever'] = (TRP, tp_)
bm = bmesh.new(); box(bm, (TRP[0], TRP[1] + 0.012, TRP[2] + 0.09), (0.13, 0.006, 0.05))
for k in range(4): box(bm, (TRP[0] - 0.045 + 0.03 * k, TRP[1] + 0.008, TRP[2] + 0.115), (0.008, 0.01, 0.012))
box(bm, (TRP[0], TRP[1] + 0.01, TRP[2]), (0.04, 0.02, 0.04))
ck('Trim_Quadrant', bm, 'metal')
