"""Our own Extra EA 300 (early two-seat MID-WING, c/n 047 / N300UY) -> extra-300-like-own.glb.   v8 (cockpit interior + baggage area, TALKFLYING.COM / TALK FLYING PODCAST)

Shape: by Ty's decision (2026-10-26) matched to ~1 in to the approved FlightGear-based Extra (codex/fg-models v9
5fdacad, wing raised ~10 in), which is the FlightGear EA300 by Patrizio Melis (GPL).  That model is used ONLY as a
measured dimensional/shape reference: tools/own_models/ea300_measure.py samples its sections at our own stations into
ea300_ref_tables.json (numbers only).  Every surface below is new geometry lofted through those numbers - no vertex,
face, UV or texture of the reference is imported, copied or reused.  See docs/models/ea300-own-PROVENANCE.md.
Paint: the approved N300UY / Sunrise Aviation scheme with TALKFLYING (codex/fg-models v9 5fdacad parameters).

  blender -b --python tools/own_models/build_ea300.py
"""
import bpy, bmesh, sys, os, math, json, numpy as np
from mathutils import Matrix
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import paint
from paint import View
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
# v9: two liveries off one geometry. EA_LIVERY=tf (default) -> extra-300-like-own.glb, the Talk Flying scheme the game
# already loads; EA_LIVERY=n300uy -> extra-300-like-own-n300uy.glb, the plain owner's copy with no Talk Flying branding.
# unmarked (Ty 2026-10-28): EA_LIVERY=unmarked -> extra-300-like-own-unmarked.glb, the N300UY base scheme with NO
# registration and NO Talk Flying / Sunrise Aviation text or badge anywhere; those areas keep the plain underlying paint.
LIVERY = os.environ.get('EA_LIVERY', 'tf'); assert LIVERY in ('tf', 'n300uy', 'unmarked'), LIVERY
UNMARKED = LIVERY == 'unmarked'
OUT = os.path.join(ROOT, 'unity/Assets/_Project/Resources/Models/extra-300-like/extra-300-like-own%s.glb' % ('' if LIVERY == 'tf' else '-' + LIVERY))
TEX = '/tmp/own_ea300_tex'; os.makedirs(TEX, exist_ok=True)
NAVY, RED, WHITE = (27, 42, 74), (200, 24, 36), (246, 246, 246)
NICKEL, TYRE, CABIN = (205, 208, 212), (88, 90, 96), (44, 56, 92)   # v3: navy cabin, not grey
R = json.load(open(os.path.join(HERE, 'ea300_ref_tables.json')))
bpy.ops.wm.read_factory_settings(use_empty=True)
# frame: metres, nose +X, port +Y, up +Z (yawed 180 deg at export, as before)

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

# ---- fuselage, canopy glass and frame: one lofted envelope through the measured sections ----------------------
rings = R['fuselage']['rings']; NA = len(rings[0]['pts'])
bms = {k: bmesh.new() for k in ('Fuselage', 'Canopy_Glass', 'Canopy_Frame')}
names = ['Fuselage', 'Canopy_Glass', 'Canopy_Frame']
RP = [np.array(r['pts']) for r in rings]
# v13: the tail cone loft stops 3 mm ahead of the rudder's half-round nose (XCUT is set from the rudder hinge line and
# thickness below; the measured cone ran ~11 mm past the hinge into the lower rudder).  Only the loft is trimmed: RP/RX
# (paint, cockpit, baggage) keep the measured rings.
_rr = R['fin']; _hf = np.polyfit([t['s'] for t in _rr if t['hinge'] is not None and t['xle'] - t['hinge'] > 0.02],
                                [t['hinge'] for t in _rr if t['hinge'] is not None and t['xle'] - t['hinge'] > 0.02], 1)
def _half_t(t, x):
    fx = t['xle'] - np.array(R['chord_fractions']) * (t['xle'] - t['xte'])
    return 0.5 * (float(np.interp(-x, -fx, t['up'])) - float(np.interp(-x, -fx, t['dn'])))
_rx = [np.polyval(_hf, t['s']) for t in _rr]
XCUT = max(x + max(_half_t(t, x if t['xle'] - x > 0.003 else t['xle'] - 0.012), 0.0) for t, x in zip(_rr, _rx) if t['s'] < 0.26) + 0.003
RPL = [r for r in RP if r[0, 0] > XCUT]
_k = len(RPL)
if _k < len(RP):
    ra, rb = RP[_k - 1], RP[_k]; f = (ra[0, 0] - XCUT) / (ra[0, 0] - rb[0, 0]); rc = ra + f * (rb - ra); rc[:, 3] = ra[:, 3]; RPL.append(rc)
print('TAIL CONE cut at x %.4f (%d -> %d rings)' % (XCUT, len(RP), len(RPL)))
for k, (ra, rb) in enumerate(zip(RPL, RPL[1:])):
    for i in range(NA):
        j = (i + 1) % NA; t = [ra[i, 3], ra[j, 3], rb[j, 3], rb[i, 3]]
        tag = max(set(t), key=t.count) if t.count(max(set(t), key=t.count)) >= 3 else 0
        bm = bms[names[int(tag)]]
        bm.faces.new([bm.verts.new((p[0], p[1], p[2])) for p in (ra[i], ra[j], rb[j], rb[i])])
for end, ring in ((0, RPL[0]), (1, RPL[-1])):
    bm = bms['Fuselage']; c = ring[:, :3].mean(0); c[0] += 0.004 if end == 0 else 0.0   # v13: flat rudder-post face
    cv = bm.verts.new(c)
    for i in range(NA):
        j = (i + 1) % NA; a, b = ring[i, :3], ring[j, :3]
        bm.faces.new((bm.verts.new(a), bm.verts.new(b), cv) if end == 0 else (bm.verts.new(b), bm.verts.new(a), cv))
P = {}
for k, bm in bms.items():
    if not bm.faces: bm.free(); continue
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    P[k] = link(k, bm)
# v3: the glass/body split follows whole quads, which left a stair-stepped cockpit edge; a smooth coaming tube is
# swept along the (Laplacian-smoothed) glass boundary to give the clean rim the v9 model shows
gb = bmesh.new(); gb.from_mesh(P['Canopy_Glass'].data); bd = [e for e in gb.edges if e.is_boundary]
adj = {}
for e in bd:
    a, c = e.verts; adj.setdefault(a, []).append(c); adj.setdefault(c, []).append(a)
seen = set(); loops = []
for v0 in adj:
    if v0 in seen: continue
    lp = [v0]; seen.add(v0); cur = v0
    while True:
        nx = [w for w in adj[cur] if w not in seen]
        if not nx: break
        cur = nx[0]; seen.add(cur); lp.append(cur)
    if len(lp) > 8: loops.append(np.array([tuple(v.co) for v in lp]))
cb = bmesh.new()
for lp in loops:
    for _ in range(6): lp = 0.5 * lp + 0.25 * (np.roll(lp, 1, 0) + np.roll(lp, -1, 0))
    seg = 8; rad = 0.012; rs = []
    for i in range(len(lp)):
        t = lp[(i + 1) % len(lp)] - lp[i - 1]; t /= np.linalg.norm(t)
        u = np.cross(t, (0, 0, 1)); u = u / (np.linalg.norm(u) + 1e-9) if np.linalg.norm(u) > 1e-3 else np.array((0, 1.0, 0)); w = np.cross(t, u)
        rs.append([tuple(lp[i] + rad * (math.cos(a) * u + math.sin(a) * w)) for a in np.linspace(0, 2 * math.pi, seg, endpoint=False)])
    loft_rings(cb, rs + [rs[0]])
gb.free()
if loops: P['Canopy_Frame'] = paint.join([P['Canopy_Frame'], link('_coam', cb)], 'Canopy_Frame') if 'Canopy_Frame' in P else link('Canopy_Frame', cb)
print('COAMING loops', [len(l) for l in loops])
RX = np.array([r['x'] for r in rings])
RT = np.array([p[p[:, 3] == 0][:, 2].max() if (p[:, 3] == 0).any() else p[:, 2].max() for p in RP])
RB = np.array([p[:, 2].min() for p in RP]); RW_ = np.array([p[:, 1].max() for p in RP])
topf = lambda x: np.interp(-np.asarray(x), -RX, RT)
RS_ = np.array([abs(p[p[:, 3] == 0][np.argmax(p[p[:, 3] == 0][:, 2]), 1]) if (p[:, 3] == 0).any() else p[:, 1].max() for p in RP])   # v6: sill half-width
sillw = lambda x: float(np.interp(-x, -RX, RS_))
botf = lambda x: np.interp(-np.asarray(x), -RX, RB)
# cabin tub so the clear canopy does not show the sky through the fuselage: floor only (v3: seat-back boxes read as crude grey blocks)
gx = RX[[(p[:, 3] == 1).any() for p in RP]]; CX0, CX1 = gx.max(), gx.min()
bm = bmesh.new()
for x0, x1, z0, z1, hw in ((CX0, CX1, 0.046 - 0.152, 0.064 - 0.152, 0.30),):   # v6: under the lowered cockpit floor
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(((x0 + x1) / 2, 0, (z0 + z1) / 2)) @ Matrix.Diagonal((abs(x0 - x1), 2 * hw, z1 - z0, 1)))
P['Cabin'] = link('Cabin', bm)

# ---- v4 cockpit interior: original geometry sized from EA 300L tandem cockpit photos (refs: ea300-refs/interior,
# reference only).  Rear seat = PIC with the main panel on the frame behind the front seat; small arch-top front panel
# under the windscreen; grey moulded seat shells with 5-point harness straps; dark sticks in boots; light-grey tube frame.
ck = bmesh.new(); ck_tag = []
CK_T = ['ck_seat', 'ck_strap', 'ck_panel', 'ck_gauge', 'ck_face', 'ck_stick', 'ck_tube', 'ck_tub', 'ck_black']
def _ck(tag, fn):
    n0 = len(ck.faces); fn(); ck_tag.extend([CK_T.index(tag)] * (len(ck.faces) - n0))
def ck_box(tag, c, s, ry=0.0, rx=0.0):
    _ck(tag, lambda: bmesh.ops.create_cube(ck, size=1.0, matrix=Matrix.Translation(c) @ Matrix.Rotation(ry, 4, 'Y') @ Matrix.Rotation(rx, 4, 'X') @ Matrix.Diagonal((s[0], s[1], s[2], 1))))
def ck_cyl(tag, a, b, r, seg=12):
    a, b = np.array(a, float), np.array(b, float); d = b - a; ln = np.linalg.norm(d)
    from mathutils import Vector
    q = Vector((0, 0, 1)).rotation_difference(Vector(d / ln)).to_matrix().to_4x4()
    _ck(tag, lambda: bmesh.ops.create_cone(ck, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=ln, matrix=Matrix.Translation(tuple((a + b) / 2)) @ q))
def ck_prism(tag, poly, x, th, lean=0.0, z0=0.0):
    """panel slab: poly (y,z) outline in the YZ plane at station x, thickness th along x, top leaning forward by lean rad"""
    def f():
        fr, bk = [], []
        for y, z in poly:
            dx = z * math.tan(lean)
            fr.append(ck.verts.new((x + dx - th / 2, y, z0 + z))); bk.append(ck.verts.new((x + dx + th / 2, y, z0 + z)))
        ck.faces.new(fr[::-1]); ck.faces.new(bk); n = len(poly)
        for i in range(n): ck.faces.new((fr[i], fr[(i + 1) % n], bk[(i + 1) % n], bk[i]))
    _ck(tag, f)
def ck_gauge(x, y, z, r, lean=0.0):
    """round instrument facing aft (-x): light bezel ring, pale face, dark needle"""
    a = (x, y, z); dn = np.array((-math.cos(lean), 0, math.sin(lean)))
    ck_cyl('ck_gauge', np.array(a) - 0.002 * dn, np.array(a) + 0.006 * dn, r, 16)
    ck_cyl('ck_face', np.array(a) + 0.006 * dn, np.array(a) + 0.008 * dn, r * 0.82, 16)
    ck_box('ck_stick', tuple(np.array(a) + 0.0095 * dn + np.array((0, r * 0.25, r * 0.15))), (0.002, r * 0.6, r * 0.08), ry=lean, rx=-0.6)
def ck_seat(x0, zf, back_top, hw, rec_deg=22, back_z0=None):
    """moulded shell: pan from x0 aft, raised thigh lip, reclined back (30 deg), side bolsters, 5-pt straps"""
    pan_l, rec = 0.34, math.radians(rec_deg); bx = x0 - pan_l
    ck_box('ck_seat', (x0 - pan_l / 2, 0, zf + 0.03), (pan_l, 2 * hw, 0.05), ry=math.radians(-6))
    ck_box('ck_seat', (x0 - 0.03, 0, zf + 0.07), (0.06, 2 * hw, 0.05))                        # thigh lip
    bz = zf if back_z0 is None else back_z0                     # v7: reclined back keeps its top; vertical extension down to a lowered pan
    if bz > zf + 0.01:
        ck_box('ck_seat', (bx - 0.005, 0, (zf + bz) / 2 + 0.01), (0.05, 2 * hw, bz - zf + 0.02))
        for sy in (1, -1): ck_box('ck_seat', (bx + 0.025, sy * (hw - 0.02), (zf + bz) / 2 + 0.01), (0.10, 0.04, bz - zf + 0.02))
    bl = (back_top - bz) / math.cos(rec); bc = (bx - math.sin(rec) * bl / 2, 0, bz + math.cos(rec) * bl / 2)
    ck_box('ck_seat', bc, (0.05, 2 * hw, bl), ry=-rec)
    for sy in (1, -1):
        ck_box('ck_seat', (bc[0] + 0.03, sy * (hw - 0.02), bc[2] - 0.05), (0.10, 0.04, bl * 0.8), ry=-rec)   # back bolsters
        ck_box('ck_seat', (x0 - pan_l / 2, sy * (hw - 0.02), zf + 0.08), (pan_l * 0.9, 0.04, 0.07))         # pan bolsters
    # harness: two shoulder straps down the back face, lap belt, crotch strap
    for sy in (0.07, -0.07):
        ck_box('ck_strap', (bc[0] + 0.03, sy, bc[2] + 0.02), (0.008, 0.045, bl * 0.75), ry=-rec)
        ck_box('ck_strap', (bx + 0.06, sy * 2.2, zf + 0.09), (0.12, 0.045, 0.008), ry=math.radians(40))
    ck_box('ck_strap', (x0 - 0.12, 0, zf + 0.065), (0.12, 0.045, 0.008))
    ck_box('ck_strap', (bx + 0.10, 0, zf + 0.085), (0.06, 0.07, 0.03))                          # buckle
def ck_stick(x, zf, h=0.36, floor=None):
    """v6: zf = stick pivot (may sit below the floor in a well); boot sits at the floor opening"""
    fl = zf if floor is None else floor
    if floor is not None and floor > zf + 0.02:          # navy stick well down through the floor to the pivot
        w = 0.06
        for sx, sy, lx, ly in ((w, 0, 0.006, 2 * w), (-w, 0, 0.006, 2 * w), (0, w, 2 * w, 0.006), (0, -w, 2 * w, 0.006)):
            ck_box('ck_tub', (x + sx, sy, (zf - 0.02 + fl) / 2), (lx, ly, fl - zf + 0.02))
        ck_box('ck_tub', (x, 0, zf - 0.02), (2 * w, 2 * w, 0.006))
    ck_cyl('ck_seat', (x, 0, fl), (x, 0, fl + 0.07), 0.034, 12)                                 # rubber boot
    ck_cyl('ck_stick', (x, 0, zf + 0.05), (x - 0.045, 0, zf + h - 0.09), 0.010, 10)
    ck_cyl('ck_stick', (x - 0.045, 0, zf + h - 0.10), (x - 0.055, 0, zf + h), 0.016, 12)        # grip
ZF = 0.068 - 0.152  # v7: floor lowered 0.152 with the seat pans; v6: cockpit floor top, lowered 0.102 with the seats (wing carry-through trimmed under the tub)
# navy tub: floor + inner side walls so the wing root and the painted fuselage skin never show inside
def ck_tub():
    xs = list(np.linspace(CX0 + 0.02, CX1 - 0.02, 12)); hw = lambda x: float(np.interp(-x, -RX, RW_)) - 0.05   # v6: inset 0.035->0.05 (sill clash)
    for a, b in zip(xs[:-1], xs[1:]):
        for sy in (1, -1):
            for flip in (0, 1):
                q = [(a, sy * hw(a), ZF), (b, sy * hw(b), ZF), (b, sy * min(hw(b), sillw(b) - 0.03), float(topf(b)) - 0.03), (a, sy * min(hw(a), sillw(a) - 0.03), float(topf(a)) - 0.03)]
                v = [ck.verts.new(p) for p in q]; ck.faces.new(v if flip else v[::-1])
        for flip in (0, 1):
            q = [(a, -hw(a), ZF), (b, -hw(b), ZF), (b, hw(b), ZF), (a, hw(a), ZF)]
            v = [ck.verts.new(p) for p in q]; ck.faces.new(v if flip else v[::-1])
_ck('ck_tub', ck_tub)
# v6: side ledges over the kept outboard wing carry-through strips (|y| 0.30 -> wall), top 12 mm above the wing skin
_WX = [0.2, 0.0, -0.2, -0.4, -0.6, -0.8, -1.0, -1.2, -1.4]; _WZ = [0.197, 0.193, 0.186, 0.166, 0.145, 0.135, 0.115, 0.099, 0.076]
_lx = list(np.linspace(CX0 - 0.005, -1.42, 10))
for a, b in zip(_lx[:-1], _lx[1:]):
    m = (a + b) / 2; zt = float(np.interp(-m, [-x for x in _WX], _WZ)) + 0.012 + 0.02; ww = min(float(np.interp(-max(a, b), -RX, RW_)) - 0.05, sillw(a) - 0.04, sillw(b) - 0.04)
    if zt > ZF + 0.005 and ww > 0.30:
        for sy in (1, -1): ck_box('ck_tub', (m, sy * (0.30 + ww) / 2 * 1.0, (ZF + zt) / 2), (abs(a - b) + 0.002, ww - 0.295, zt - ZF))
# front (student) seat + stick
ck_seat(-0.694 + 0.152, ZF, 0.328, 0.17, rec_deg=17, back_z0=0.068); ck_stick(-0.504 + 0.152, 0.17 - 0.305, 0.34 + 0.102, floor=ZF)   # v7: pan -0.152 z, seat+stick fwd 0.152, stick +0.102 long   # v6: seat -0.102 z, seat+stick 0.254 aft, stick -0.305 z; recline 22->17 deg to clear the dash top
# rear (PIC) seat + stick
ck_seat(-1.42, ZF, 0.348, 0.17, back_z0=0.068); ck_stick(-1.355, 0.17 - 0.305, 0.36 + 0.102, floor=ZF)   # v7: pan -0.152 z, stick +0.102 long   # v6: stick -0.305 z; moved aft 0.115 so it stays behind the moved panel
# v8: baggage area behind the rear seat: shelf, front lip, side walls (tub walls) and an aft bulkhead following the skin
BG_X0, BG_X1, BG_Z = -1.80, -2.15, 0.10
_bw = lambda x: float(np.interp(-x, -RX, RW_)) - 0.06
ck_box('ck_tub', ((BG_X0 + BG_X1) / 2, 0, BG_Z - 0.003), (abs(BG_X1 - BG_X0), 2 * _bw(BG_X0), 0.006))
ck_box('ck_tub', (BG_X0, 0, (ZF + BG_Z) / 2), (0.006, 2 * _bw(BG_X0), BG_Z - ZF))
_ri = int(np.argmin(np.abs(RX - BG_X1))); _rp = RP[_ri][:, 1:3]
_rp = _rp[_rp[:, 1] >= BG_Z]; _zc = (_rp[:, 1].max() + BG_Z) / 2
_sc = min(0.92, (_bw(BG_X1) + 0.005) / max(np.abs(_rp[:, 0]).max(), 1e-3))
_ang = np.arctan2(_rp[:, 1] - _zc, _rp[:, 0]); _rp = _rp[np.argsort(_ang)]
_poly = [(float(y * _sc), float(_zc + (z - _zc) * 0.92 - BG_Z)) for y, z in _rp]
_poly = [(-_bw(BG_X1) + 0.005, 0.0), (_bw(BG_X1) - 0.005, 0.0)] + [q for q in _poly if q[0] > 0][::-1] + [q for q in _poly if q[0] <= 0][::-1]
print('BAGGAGE ring x %.3f top z %.3f, bulkhead pts %d' % (RX[_ri], _rp[:, 1].max(), len(_poly)))
ck_prism('ck_tub', _poly, BG_X1, 0.01, 0.0, BG_Z)
# front panel: small arch-top strip under the windscreen, 5 gauges
arch = [(-0.27, 0.0), (0.27, 0.0)] + [(0.27 * math.cos(t), 0.07 + 0.08 * math.sin(t)) for t in np.linspace(0, math.pi, 11)]
FPX, FPZ, FL = -0.14, 0.17, math.radians(18)
ck_prism('ck_panel', arch, FPX, 0.02, FL, FPZ)
for y in (-0.19, -0.095, 0, 0.095, 0.19):
    zz = 0.085 if abs(y) < 0.1 else 0.06
    ck_gauge(FPX + zz * math.tan(FL) - 0.011, y, FPZ + zz, 0.036, FL)
# rear (main) panel, v5: angular-cornered dash top (glareshield) ~0.20 m deep fore-aft behind the front seat, after the
# 300L photo; the rectangular instrument panel hangs under its aft lip; radio stack below
RPX, RPZ = -1.314 + 0.152, 0.148                                       # panel face station / panel bottom
hood = [(-0.31, 0.0), (0.31, 0.0), (0.31, 0.03), (0.20, 0.12), (-0.20, 0.12), (-0.31, 0.03)]   # v6: narrowed 0.04/side to clear the canopy glass
_h0 = len(ck_tag); ck_prism('ck_panel', hood, RPX + 0.06, 0.20, 0.0, RPZ + 0.16)    # x RPX-0.04 .. RPX+0.16
for _i in (2, 3, 4): ck_tag[_h0 + 2 + _i] = CK_T.index('ck_black')          # v6: flat matte black top (+ angled corners)
ck_prism('ck_panel', [(-0.30, 0.0), (0.30, 0.0), (0.30, 0.16), (-0.30, 0.16)], RPX + 0.015, 0.03, 0.0, RPZ)
ck_box('ck_panel', (RPX + 0.03, 0, (ZF + RPZ) / 2), (0.05, 0.24, RPZ - ZF))   # v6: stack trimmed to the floor
ck_box('ck_face', (RPX - 0.002, 0, (ZF + RPZ) / 2), (0.004, 0.13, 0.05))                                 # radio face
for z in (0.045, 0.115):
    for y in (-0.24, -0.12, 0.0, 0.12, 0.24):
        ck_gauge(RPX - 0.001, y, RPZ + z, 0.031, 0.0)
# light-grey steel tube frame visible along the cockpit sides (sill longeron + diagonals)
for sy in (1, -1):
    hw_at = lambda x: float(np.interp(-x, -RX, RW_)) - 0.075
    xs = [-0.10, -0.55, -1.00, -1.45, -1.95]
    for a, b in zip(xs[:-1], xs[1:]):
        ck_cyl('ck_tube', (a, sy * min(hw_at(a), sillw(a) - 0.05), float(topf(a)) - 0.06), (b, sy * min(hw_at(b), sillw(b) - 0.05), float(topf(b)) - 0.06), 0.012, 8)
        ck_cyl('ck_tube', (a, sy * min(hw_at(a), sillw(a) - 0.05), float(topf(a)) - 0.06), (b, sy * (hw_at(b) - 0.02), ZF + 0.02), 0.011, 8)
P['Cockpit'] = link('Cockpit', ck)
print('COCKPIT faces', len(ck_tag))

# ---- lifting surfaces: sections interpolated from the measured tables, split at the measured hinge lines --------
FR = np.array(R['chord_fractions'])
class Surf:
    def __init__(self, tab, signed=False, minc=0.05):
        tab = [t for t in tab if t['xle'] - t['xte'] > minc]; self.sg = signed
        self.s = np.array([t['s'] if signed else abs(t['s']) for t in tab]); o = np.argsort(self.s); tab = [tab[i] for i in o]; self.s = self.s[o]
        self.le = np.array([t['xle'] for t in tab]); self.te = np.array([t['xte'] for t in tab])
        self.up = np.array([t['up'] for t in tab]); self.dn = np.array([t['dn'] for t in tab])
        h = [(t['s'], t['hinge']) for t in tab if t['hinge'] is not None]
        self.hs = np.array([abs(a) for a, b in h]); self.hx = np.array([b for a, b in h])
    def at(self, s):
        s = s if self.sg else abs(s); f = lambda arr: np.array([np.interp(s, self.s, arr[:, k]) for k in range(arr.shape[1])])
        return float(np.interp(s, self.s, self.le)), float(np.interp(s, self.s, self.te)), f(self.up), f(self.dn)
    def hinge_line(self):     # straight-line fit through the measured hinge points (drops cut-out outliers)
        a, b = np.polyfit(self.hs, self.hx, 1); r = self.hx - (a * self.hs + b); k = np.abs(r) < 2.5 * np.median(np.abs(r)) + 1e-3
        a, b = np.polyfit(self.hs[k], self.hx[k], 1); return lambda s: a * abs(s) + b
    def section(self, s, x_from=None, x_to=None, sgn=1, axis='y', n=22):
        le, te, up, dn = self.at(s); xa = le if x_from is None else x_from; xb = te if x_to is None else x_to
        xq = xa + (xb - xa) * (1 - np.cos(np.linspace(0, math.pi, n))) / 2
        fx = le - FR * (le - te)                       # decreasing x
        u = np.interp(-xq, -fx, up); d = np.interp(-xq, -fx, dn)
        loop = list(zip(xq, u)) + list(zip(xq[::-1], d[::-1]))
        if x_from is None: loop = loop[:-1]            # shared LE point
        if axis == 'y': return [(x, sgn * s, z) for x, z in loop]
        return [(x, z, s) for x, z in loop]            # fin: (x, y-thickness, z)
def surf_obj(name, surf, spans, x_from=None, x_to=None, sgn=1, axis='y'):
    bm = bmesh.new()
    secs = [surf.section(s, None if x_from is None else x_from(s), None if x_to is None else x_to(s), sgn, axis) for s in spans]
    loft_rings(bm, secs); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return link(name, bm)
L = lambda a, b, n: list(np.linspace(a, b, n))
# v13 real hinges (Ty 2026-10-29: "fix that to be real hinged surface at the hinge line").  Every moving surface is a
# separate mesh with a half-round nose centred on ONE straight hinge line (x from the measured hinge fit, mid-plane
# fitted straight across the span), radius = the local half-thickness there (Pitts v5 style).  The fixed surface ahead
# ends 3 mm clear of that nose, so rotating the whole mesh about the hinge line never opens a gap or pushes a corner
# through the fixed skin.  The node origin sits on the hinge line and an empty '<Name>_HingeAxis' marks its far end.
class Hinge:
    CLEAR = 0.003
    def __init__(self, surf, hinge, spans):
        self.f, self.h = surf, hinge
        a, b = np.polyfit(spans, [self._ud(s, self.xh(s), mid=True) for s in spans], 1); self.c = lambda s: a * abs(s) + b
    def _ud(self, s, x, mid=False):
        le, te, up, dn = self.f.at(s); fx = le - FR * (le - te)
        u, d = float(np.interp(-x, -fx, up)), float(np.interp(-x, -fx, dn)); return 0.5 * (u + d) if mid else (u, d)
    def xh(self, s):                  # hinge x, or just behind the LE where the LE is aft of the hinge (rudder foot)
        return min(self.h(s), self.f.at(s)[0] - 0.0005)
    def r(self, s):                   # nose radius about (xh, c); LE on the hinge -> thickness read 12 mm aft
        le = self.f.at(s)[0]; x = self.xh(s); c = self.c(s); rr = []
        for xr in ((x, le - 0.012) if le - x < 0.003 else (x,)):
            u, d = self._ud(s, xr); rr += [u - c, c - d]
        return max(rr)
    def x_to(self, s): return self.xh(s) + self.r(s) + self.CLEAR
    def ring(self, s, sgn=1, axis='y', n=22, nn=9):
        le, te, up, dn = self.f.at(s); fx = le - FR * (le - te); x0 = self.xh(s); c = self.c(s); r = self.r(s)
        xq = x0 + (te - x0) * (1 - np.cos(np.linspace(0, math.pi, n))) / 2
        u = np.interp(-xq, -fx, up); d = np.interp(-xq, -fx, dn)
        w = np.clip(1 - (x0 - xq) / 0.02, 0, 1)                       # fade the skin onto the nose over 20 mm
        u = u + max(0.0, c + r - u[0]) * w; d = d - max(0.0, d[0] - (c - r)) * w
        loop = list(zip(xq, u)) + list(zip(xq[::-1], d[::-1]))
        loop += [(x0 + r * math.cos(t), c + r * math.sin(t)) for t in np.linspace(-math.pi / 2, math.pi / 2, nn + 2)[1:-1]]
        if axis == 'y': return [(x, sgn * s, z) for x, z in loop]
        return [(x, z, s) for x, z in loop]
    def obj(self, name, spans, sgn=1, axis='y'):
        bm = bmesh.new(); loft_rings(bm, [self.ring(s, sgn, axis) for s in spans]); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); return link(name, bm)
    def axis_pts(self, s0, s1, sgn=1, axis='y'):   # straight hinge line end points (build frame)
        p = lambda s: (self.h(s), sgn * s, self.c(s)) if axis == 'y' else (self.h(s), self.c(s), s)
        return p(s0), p(s1)
HINGES = {}
W = Surf(R['wing']); HW = W.hinge_line(); A_IN, A_OUT = R['aileron_span']; W_TIP = W.s.max()
wing = []
AS = L(A_IN + 0.008, W_TIP - 0.004, 22); HA = Hinge(W, HW, AS)
def prism(bm, xy, z0, z1, sg=1):       # vertical extrusion of a plan outline (y mirrored by sg)
    pts = xy if sg > 0 else xy[::-1]
    loft_rings(bm, [[(x, sg * y, z0) for x, y in pts], [(x, sg * y, z1) for x, y in pts]])
for sg, sd in ((1, 'L'), (-1, 'R')):
    # v3: forward wing is one loft root->tip ending at the hinge line; the aft strip inboard of the aileron is its own
    # loft, so the only end wall at the aileron's inboard end is the real gap wall behind the hinge
    sp = L(0.0, A_IN, 8) + L(A_IN + 0.0005, W_TIP, 24)   # full chord inboard, steps to the hinge line at A_IN
    wing.append(surf_obj('_wf' + sd, W, sp, x_to=lambda s: None if s <= A_IN else HA.x_to(s), sgn=sg))
    P['Aileron_' + sd] = HA.obj('Aileron_' + sd, AS, sgn=sg)   # v13: half-round nose on the hinge line (was a flat cut 6 mm aft)
    HINGES['Aileron_' + sd] = HA.axis_pts(AS[0], AS[-1], sg)
P['Wing'] = paint.join(wing, 'Wing')
# v6: trim the hidden mid-wing carry-through faces inside the cockpit tub (inside the fuselage, never seen from
# outside) so the lowered floor/seats/stick wells do not show white wing skin
_bm = bmesh.new(); _bm.from_mesh(P['Wing'].data)
for _co, _no in (((0, 0.30, 0), (0, 1, 0)), ((0, -0.30, 0), (0, 1, 0)), ((CX0 - 0.005, 0, 0), (1, 0, 0))):
    _g = _bm.verts[:] + _bm.edges[:] + _bm.faces[:]; bmesh.ops.bisect_plane(_bm, geom=_g, plane_co=_co, plane_no=_no)
_cut = [f for f in _bm.faces if CX1 < f.calc_center_median().x < CX0 - 0.005 and abs(f.calc_center_median().y) < 0.30]
bmesh.ops.delete(_bm, geom=_cut, context='FACES'); _bm.to_mesh(P['Wing'].data); _bm.free(); print('WING trimmed faces', len(_cut))
for sg, sd in ((1, 'L'), (-1, 'R')):   # aileron spades: measured plate outline, inboard weight and slanted strut
    bm = bmesh.new(); SPP = R['spade_plate']; SW = R['spade_weight']; ST = R['spade_strut']
    prism(bm, SPP['xy'], SPP['z'][0], SPP['z'][1], sg)
    hp = [bm.verts.new((x, sg * y, z)) for x, y, z in R['spade_weight_pts']]
    bmesh.ops.convex_hull(bm, input=hp)
    zs = [SPP['z'][1] - 0.001] + [r['z'] for r in ST[1:]]
    loft_rings(bm, [[(x, sg * y, z) for x, y in (r['xy'] if sg > 0 else r['xy'][::-1])] for r, z in zip(ST, zs)])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    P['Aileron_' + sd] = paint.join([P['Aileron_' + sd], link('_sp' + sd, bm)], 'Aileron_' + sd)
S = Surf(R['stab']); HS = S.hinge_line(); E_IN, E_OUT = R['elevator_span']; S_TIP = S.s.max()
S.s[0] = 0.0
ES = sorted(set(L(max(E_IN, 0.075), S_TIP - 0.003, 14)) | {s for s in S.s if 0.075 < s < 0.25}); HE = Hinge(S, HS, ES)
P['Stab'] = paint.join([surf_obj('_s' + sd, S, [sg * y for y in sorted(set(L(0, S_TIP, 14)) | set(ES))], x_to=HE.x_to, sgn=1) for sg, sd in ((1, 'L'), (-1, 'R'))], 'Stab')
for sg, sd in ((1, 'L'), (-1, 'R')):   # v13: half-round nose on the hinge line (was a flat cut 4 mm aft, 8 mm open slot)
    P['Elevator_' + sd] = HE.obj('Elevator_' + sd, ES, sgn=sg); HINGES['Elevator_' + sd] = HE.axis_pts(ES[0], ES[-1], sg)
F = Surf(R['fin'], signed=True, minc=0.012)
FZ0 = R['fin_only_zmin']; FZ1 = R['bounds']['Fin'][1][2]; FIN_TOP = R['fin_z'][1]
_hfit = np.polyfit([t['s'] for t in R['fin'] if t['hinge'] is not None and t['xle'] - t['hinge'] > 0.02],
                   [t['hinge'] for t in R['fin'] if t['hinge'] is not None and t['xle'] - t['hinge'] > 0.02], 1)
HF = lambda z: float(np.polyval(_hfit, z))   # v13: straight rudder hinge line (the horn rows and the foot are excluded from the fit)
FZ1G = FZ1 + 0.003                           # balance horn floats 3 mm above the fin cap
RS = sorted(set(L(F.s.min(), FZ1G, 18)) | {z for z in F.s if F.s.min() < z < FZ1G}); HR = Hinge(F, HF, RS)
# full-height rudder: foot (below the fuselage line) to the fin cap is one hinged loft, nose half-round about the hinge
_fz0 = next(z for z in np.linspace(FZ0, FZ1, 400) if F.at(z)[0] > HR.x_to(z) + 0.02)   # fin starts where it has chord ahead of the nose (inside the fuselage)
P['Fin'] = surf_obj('Fin', F, L(_fz0, FZ1, 14), x_to=HR.x_to, axis='z')
print('FIN root z %.3f (fin_only_zmin %.3f), aft face x %.4f..%.4f' % (_fz0, FZ0, HR.x_to(_fz0), HR.x_to(FZ1)))
bm = bmesh.new(); bmesh.ops.convex_hull(bm, input=[bm.verts.new(p) for p in R['fin_fillet_pts']])   # LE root fillet
P['Fin'] = paint.join([P['Fin'], link('_ff', bm)], 'Fin')
P['Rudder'] = paint.join([HR.obj('_r0', RS, axis='z'), surf_obj('_r1', F, L(FZ1G, F.s.max(), 6), axis='z')], 'Rudder')
HINGES['Rudder'] = HR.axis_pts(F.s.min(), F.s.max(), axis='z')
RH = R['rudder_horn']; bm = bmesh.new()      # steering horn across the rudder foot (moves with the rudder)
c = [(a + b) / 2 for a, b in zip(RH['min'], RH['max'])]; d = [b - a for a, b in zip(RH['min'], RH['max'])]
bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(c) @ Matrix.Diagonal((d[0], d[1], d[2], 1)))
P['Rudder'] = paint.join([P['Rudder'], link('_horn', bm)], 'Rudder')
bm = bmesh.new()                             # v13: the two cables forward are their own static part (no 'rudder' in the name)
for sg in (1, -1):
    x0, x1 = RH['cable_x']
    bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.002, radius2=0.002, depth=x1 - x0,
                          matrix=Matrix.Translation(((x0 + x1) / 2, sg * RH['cable_y'], RH['cable_z'])) @ Matrix.Rotation(math.pi / 2, 4, 'Y'))
P['Tail_Cables'] = link('Tail_Cables', bm)
for k, (a, b) in HINGES.items(): print('HINGE %-11s %s -> %s' % (k, np.round(a, 4), np.round(b, 4)))
print('NOSE r rudder %.4f..%.4f, elevator %.4f..%.4f, aileron %.4f..%.4f' % (min(map(HR.r, RS)), max(map(HR.r, RS)), min(map(HE.r, ES)), max(map(HE.r, ES)), min(map(HA.r, AS)), max(map(HA.r, AS))))

# ---- undercarriage ------------------------------------------------------------------------------------------------
def tube_path(bm, pts, wx, wy, n=12):
    pts = np.array(pts, float); rs = []
    for k, p in enumerate(pts):
        d = pts[min(k + 1, len(pts) - 1)] - pts[max(k - 1, 0)]; d /= np.linalg.norm(d)
        u = np.cross(d, [1.0, 0, 0]); u /= np.linalg.norm(u); w = np.cross(d, u)
        rs.append([p + 0.5 * wx[k] * math.cos(t) * w + 0.5 * wy[k] * math.sin(t) * u for t in np.linspace(0, 2 * math.pi, n, endpoint=False)])
    loft_rings(bm, rs)
bm = bmesh.new()
GR = R['gear_rings']
for sg in (1, -1):     # main legs: lofted through the measured horizontal sections
    loft_rings(bm, [[(p[0], sg * p[1], g['z']) for p in (g['xy'] if sg > 0 else g['xy'][::-1])] for g in GR])
TRR = R['tail_rings']  # tailwheel spring: lofted through measured vertical sections
loft_rings(bm, [[(t['x'], p[0], p[1]) for p in t['yz']] for t in TRR])
bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Gear_Main'] = link('Gear_Main', bm)
mw = R['main_wheel']; X_AX, Y_AX, Z_AX, R_W = mw['c'][0], mw['c'][1], mw['c'][2], mw['r']
tw = R['tail_wheel']
bm = bmesh.new(); WP = R['wheel_profile']
for sg in (1, -1):     # tyre: lathe through the measured radius-vs-width profile
    rs = [[(X_AX + r * math.sin(a), sg * Y_AX + dy, Z_AX + r * math.cos(a)) for a in np.linspace(0, 2 * math.pi, 32, endpoint=False)] for dy, r in WP]
    loft_rings(bm, rs if sg > 0 else rs[::-1])
bmesh.ops.create_cone(bm, cap_ends=True, segments=16, radius1=tw['r'], radius2=tw['r'], depth=tw['w'],
                      matrix=Matrix.Translation(tuple(tw['c'])) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
P['Wheels'] = link('Wheels', bm)
bm = bmesh.new(); PR = R['pants']['rings']
for sg in (1, -1):
    loft_rings(bm, [[(r['x'], sg * p[0], p[1]) for p in (r['pts'] if sg > 0 else r['pts'][::-1])] for r in PR])
bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Wheel_Pants'] = link('Wheel_Pants', bm)
PX0, PX1 = min(r['x'] for r in PR), max(r['x'] for r in PR)

# ---- spinner and three-blade propeller (v10: 3 blades at 120 deg) -----------------------------------------------------------------------------------
SPX = R['spinner_tip_x']; PROP_X = R['prop']['x']
prof = [(x, r) for x, r in R['spinner'] if x < SPX - 0.004]
bm = bmesh.new(); rs = [[bm.verts.new((x, r * math.sin(a), r * math.cos(a))) for a in np.linspace(0, 2 * math.pi, 28, endpoint=False)] for x, r in prof]
tipv = bm.verts.new((SPX, 0, 0))
for i in range(28): j = (i + 1) % 28; bm.faces.new((tipv, rs[0][i], rs[0][j]))
for a_, b2 in zip(rs, rs[1:]):
    for i in range(28): j = (i + 1) % 28; bm.faces.new((a_[i], b2[i], b2[j], a_[j]))
bm.faces.new(rs[-1]); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); P['Spinner'] = link('Spinner', bm)
R_P, PITCH = R['prop']['r'], 1.9
bm = bmesh.new(); face_tag = []
for blade in range(3):
    ph = blade * 2 * math.pi / 3
    rad = np.array([0, math.sin(ph), math.cos(ph)])
    tan = np.array([0, math.cos(ph), -math.sin(ph)])   # direction of travel (same sense as the old 2-blade)
    ax = np.array([1.0, 0, 0]); hub = np.array([PROP_X, 0, 0])
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

# ---- v11: cowl inlets + an original, simplified Lycoming AEIO-540 flat six visible through them ----------------------
# Sized from published Lycoming data (bore 5.125 in, overall width ~33-34 in) and generic flat-six layout; drawn from
# primitives here, no third-party mesh. Lycoming numbering: odd cylinders right (starboard, -y), even left (port, +y);
# #1 right-front is the most forward cylinder, the left bank sits one crank-throw spacing aft (EN_STAG).
# v12: D-shaped inlets like the real Extra cowl (ref photo): wide and low either side of the spinner, near-flat inboard
# edge, rounded outboard/top/bottom, ~2:1. Boundary = superellipse, near-flat (n=8) inboard, round (n=2.2) outboard.
IN_YI, IN_YO, IN_Z, IN_H = 0.195, 0.405, 0.005, 0.105          # inboard/outboard |y|, centre z, full height
def _d_ring(sy, s=1.0, n_pts=48):
    yc, a, b = 0.5 * (IN_YI + IN_YO), 0.5 * (IN_YO - IN_YI), 0.5 * IN_H
    out = []
    for t in np.linspace(0, 2 * math.pi, n_pts, endpoint=False):
        c, si = math.cos(t), math.sin(t); n = 2.2 if c >= 0 else 8.0
        out.append((sy * (yc + s * a * np.sign(c) * abs(c) ** (2 / n)), IN_Z + s * b * np.sign(si) * abs(si) ** (2 / 2.6)))
    return out
def _in_d(y, z, s):
    yc, a, b = 0.5 * (IN_YO + IN_YI), 0.5 * (IN_YO - IN_YI), 0.5 * IN_H
    u = (abs(y) - yc) / (s * a); v = (z - IN_Z) / (s * b); n = 2.2 if u >= 0 else 8.0
    return abs(u) ** n + abs(v) ** 2.6 < 1.0
from mathutils.bvhtree import BVHTree
from mathutils import Vector
_fb = bmesh.new(); _fb.from_mesh(P['Fuselage'].data)
_front = [f for f in _fb.faces if f.calc_center_median().x > 1.5]
bmesh.ops.subdivide_edges(_fb, edges=list({e for f in _front for e in f.edges}), cuts=4, use_grid_fill=True)
_fb.normal_update(); _fb.to_mesh(P['Fuselage'].data)
SKIN = BVHTree.FromBMesh(_fb)                                    # uncut skin, used to drape the lips
def _hit(y, z):
    loc, nrm, _, _ = SKIN.ray_cast(Vector((2.6, y, z)), Vector((-1, 0, 0)))
    if nrm.x < 0: nrm = -nrm
    return np.array(loc), np.array(nrm)
_cut = [f for f in _fb.faces if f.calc_center_median().x > 1.55 and _in_d(f.calc_center_median().y, f.calc_center_median().z, 1.06)]
bmesh.ops.delete(_fb, geom=_cut, context='FACES'); _fb.to_mesh(P['Fuselage'].data); _fb.free()
print('INLET faces cut', len(_cut))

en_bms = {}; en_tag = []
EN_T = ['en_core', 'en_cyl', 'en_case', 'en_rocker', 'en_pipe', 'en_baffle', 'en_lip', 'en_dark']
def _en(tag, fn):   # one bmesh per material, merged at the end (material index = block order)
    global en
    en = en_bms.setdefault(tag, bmesh.new()); fn()
def en_box(tag, c, s):
    _en(tag, lambda: bmesh.ops.create_cube(en, size=1.0, matrix=Matrix.Translation(c) @ Matrix.Diagonal((s[0], s[1], s[2], 1))))
def en_cyl(tag, a, b, r, seg=16, r2=None):
    from mathutils import Vector
    a, b = np.array(a, float), np.array(b, float); d = b - a; ln = float(np.linalg.norm(d))
    q = Vector((0, 0, 1)).rotation_difference(Vector(d / ln)).to_matrix().to_4x4()
    _en(tag, lambda: bmesh.ops.create_cone(en, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=ln, matrix=Matrix.Translation(tuple((a + b) / 2)) @ q))
EN_X1, EN_PITCH, EN_STAG = 1.52, 0.25, 0.13      # #1 cylinder axis x; per-bank pitch; left bank aft of right
EN_Z = 0.0                                        # crankshaft on the thrust line
en_box('en_case', (EN_X1 - 0.30, 0, EN_Z), (0.86, 0.24, 0.24))                     # crankcase, black
en_cyl('en_case', (EN_X1 + 0.13, 0, EN_Z), (EN_X1 + 0.18, 0, EN_Z), 0.075)          # nose case / prop flange boss
en_box('en_case', (EN_X1 - 0.33, 0, EN_Z - 0.17), (0.70, 0.20, 0.12))              # sump
for side, sy in (('R', -1), ('L', 1)):
    for k in range(3):
        cx = EN_X1 - k * EN_PITCH - (EN_STAG if side == 'L' else 0.0)
        y0, y1, y2 = 0.12, 0.29, 0.40                                                # case face, barrel/head joint, head end
        en_cyl('en_core', (cx, sy * y0, EN_Z), (cx, sy * y1, EN_Z), 0.072, 20)        # barrel
        for f in np.linspace(y0 + 0.02, y1 - 0.01, 9):                               # barrel cooling fins
            en_cyl('en_cyl', (cx, sy * (f - 0.003), EN_Z), (cx, sy * (f + 0.003), EN_Z), 0.090, 20)
        en_cyl('en_core', (cx, sy * y1, EN_Z), (cx, sy * y2, EN_Z), 0.082, 20)        # head
        for f in np.linspace(y1 + 0.012, y2 - 0.012, 6):                             # head fins (taller, flatter)
            en_cyl('en_cyl', (cx, sy * (f - 0.003), EN_Z), (cx, sy * (f + 0.003), EN_Z), 0.104, 20)
        en_box('en_rocker', (cx, sy * (y1 + 0.055), EN_Z + 0.118), (0.12, 0.085, 0.045))   # rocker covers top (exhaust)
        en_box('en_rocker', (cx, sy * (y1 + 0.055), EN_Z - 0.118), (0.12, 0.085, 0.045))   # and bottom (intake)
        en_cyl('en_pipe', (cx + 0.03, sy * (y1 + 0.03), EN_Z - 0.10), (cx + 0.03, sy * 0.20, EN_Z - 0.20), 0.022, 10)  # intake runner to sump plenum
        en_cyl('en_pipe', (cx - 0.03, sy * (y1 + 0.07), EN_Z - 0.10), (cx - 0.05, sy * (y1 + 0.07), EN_Z - 0.26), 0.026, 10)  # exhaust stub
    # front baffle: lower plate ahead of the front cylinder and an inboard plate, so the inlet looks into a plenum
    fx = EN_X1 + 0.11 - (EN_STAG if side == 'L' else 0.0)
    en_box('en_baffle', (fx, sy * 0.26, EN_Z - 0.135), (0.008, 0.20, 0.09))   # v12: top at z -0.09, below the inlet floor
    en_box('en_baffle', (fx - 0.10, sy * 0.13, EN_Z + 0.04), (0.20, 0.008, 0.20))
# prop governor: front of the case on the left (port) side, Lycoming front-mounted governor pad
en_cyl('en_case', (EN_X1 + 0.06, 0.10, EN_Z + 0.08), (EN_X1 + 0.06, 0.10, EN_Z + 0.15), 0.040, 16)
en_cyl('en_rocker', (EN_X1 + 0.06, 0.10, EN_Z + 0.15), (EN_X1 + 0.06, 0.10, EN_Z + 0.17), 0.028, 12)
en_box('en_pipe', (EN_X1 + 0.06, 0.15, EN_Z + 0.13), (0.02, 0.06, 0.02))              # control arm
# v12 lips draped on the skin: outer edge lies 0.8 mm proud of the cowl and rolls over the opening into a dark duct,
# so there is one continuous lip, no separate ring standing off a sloped face.
for sy in (1, -1):
    rows = []
    for (s, dn, dx, tag) in ((1.30, 0.0008, 0.0, 'en_lip'), (1.14, 0.006, 0.0, 'en_lip'), (1.04, 0.004, -0.012, 'en_lip'),
                             (0.99, 0.0, -0.030, 'en_lip'), (0.97, 0.0, -0.075, 'en_dark')):
        ring = []
        for (y, z) in _d_ring(sy, s):
            p, nr = _hit(y, z); ring.append(tuple(p + dn * nr + np.array((dx, 0, 0))))
        rows.append((ring, tag))
    for (ra, _), (rb, tb) in zip(rows, rows[1:]):
        _en(tb if tb == 'en_dark' else 'en_lip', lambda ra=ra, rb=rb: loft_rings(en, [ra, rb], cap0=False, cap1=False))
    print('INLET %+d lip x %.3f..%.3f' % (sy, min(p[0] for p in rows[0][0]), max(p[0] for p in rows[0][0])))

_all = bmesh.new()
for t in EN_T:
    if t not in en_bms: continue
    _me = bpy.data.meshes.new('_en_' + t); en_bms[t].to_mesh(_me); en_bms[t].free()
    n0 = len(_all.faces); _all.from_mesh(_me); en_tag.extend([EN_T.index(t)] * (len(_all.faces) - n0)); bpy.data.meshes.remove(_me)
P['Engine'] = link('Engine', _all)

# ---- paint (approved v9 N300UY rules, re-derived on this geometry) -------------------------------------------------
X0, X1, Z0p, Z1p = -5.40, 2.45, -1.45, 1.45
XT = RX.min()                                   # tail cone end
YR = RW_.max() * 0.95                           # wing/fuselage junction span
LE_R, TE_R_, UP_R, DN_R = W.at(YR)
XWR = TE_R_                                     # wing-root trailing edge
ZWT = float(np.interp(0.30, FR, UP_R))          # wing-root top
XW, ZW = XWR + 0.15, ZWT + 0.03
SLE, STE, SUP, SDN = S.at(0.1); ZS = 0.5 * (float(SUP.max()) + float(SDN.min()))
ZT = ZS + 0.04                                  # navy ends just above the tailplane
_k = (ZW - ZT) / (XW - XT)
zb = lambda x: np.minimum(ZT + _k * (x - XT), ZW - 0.02)
LP1, LP2, RW = 0.085, 0.405, 0.025; LP3_GAP = 0.02
FIN_LE = lambda z: np.interp(z, F.s, F.le)
SIDE_ART = 'ea-talkflying-com.png' if LIVERY == 'tf' else 'ea-n300uy.png'   # v9: both set in Liberation Sans Bold (OFL 1.1)
_sw, _sh = {'ea-talkflying-com.png': (2203, 203), 'ea-n300uy.png': (955, 203)}[SIDE_ART]
# v10: centred fore-aft between the wing-root TE and the stab-root LE, on the white band between the stripes;
# height shrinks so the (sloping) band still clears the letters by 0.025 m top and bottom.
XLC = 0.5 * (XWR + float(S.at(0.1)[0]))
TH = 0.19   # v8
_band = LP2 - (LP1 + RW) - 0.05
TH = min(TH, _band / (1 + abs(_k) * _sw / _sh)); TX0 = XLC + 0.5 * TH * _sw / _sh; TX1 = XLC - 0.5 * TH * _sw / _sh
print('SIDE LETTERS centre x %.2f height %.3f' % (XLC, TH))   # v8: TALKFLYING.COM, height 0.21->0.19
print('NAVY LINE tail %.3f wing %.3f (x %.2f)  TALKFLYING x %.2f..%.2f  bottom there %.2f..%.2f, letters %.2f..%.2f' %
      (ZT, ZW, XW, TX0, TX1, botf(TX0), botf(TX1), zb(TX1) - 0.13 - TH, zb(TX0) - 0.13))
ZSTAR = FIN_TOP - 0.18; SLE_, STE_ = float(np.interp(ZSTAR, F.s, F.le)), float(np.interp(ZSTAR, F.s, F.te))
def _fin_logo(asp=891 / 228, m=0.04, h0=0.23):
    """v10: vertically centred between the stab top and the bottom of the cap's red line; v9 size, shrunk if needed."""
    zc = 0.5 * (float(SUP.max()) + FIN_TOP - 0.36)
    for h in np.linspace(h0, 0.05, 60):
        zz = np.linspace(zc - h / 2, zc + h / 2, 9)
        xf = float(np.min(FIN_LE(zz))) - m; xa = float(np.max(np.interp(zz, F.s, F.te))) + m; w = asp * h
        if w <= xf - xa:
            xc = 0.5 * (xf + xa); return (xc + w / 2, xc - w / 2, zc - h / 2, zc + h / 2)
def _fin_logo_v9(asp=891 / 228, m=0.04):
    """Largest badge rectangle that sits wholly in the fin's white field (below the red line, above the fuselage)."""
    best = None
    for zhi, h in [(zh, hh) for zh in np.linspace(FIN_TOP - 0.36 - m, FIN_TOP - 1.2, 60) for hh in np.linspace(0.05, 0.6, 111)]:
        if best and h <= best[3] - best[2]: continue
        zlo = zhi - h; zz = np.linspace(zlo, zhi, 9)
        xf = float(np.min(FIN_LE(zz))) - m; xa = float(np.max(np.interp(zz, F.s, F.te))) + m
        w = asp * h
        if w > xf - xa: continue
        xc = 0.5 * (xf + xa); xs = np.linspace(xc - w / 2, xc + w / 2, 9)
        if zlo < float(np.max(topf(xs))) + m: continue
        best = (xc + w / 2, xc - w / 2, zlo, zhi)
    return best
FIN_LOGO = _fin_logo(); print('FIN LOGO', FIN_LOGO)
def side(name, flip):
    v = View(name, (X0, X1), (Z0p, Z1p), 4096, 1520, flip_a=flip, axes=(0, 2)); A, Z = v.A, v.B
    finz = (A < FIN_LE(Z) + 0.02) & (Z > topf(A) + 0.02) & (A < -3.3)
    body = ~finz & (Z > -0.9)
    d = zb(A) - Z
    v.fill(body & (d < 0.05), NAVY)
    v.fill(body & (d >= LP1) & (d < LP1 + RW), RED)
    v.fill(body & (d >= LP2) & (d < LP2 + RW), RED)
    v.fill(body & (d >= LP2 + RW + LP3_GAP) & (d < LP2 + 2 * RW + LP3_GAP), RED)   # v10: second thin stripe under the lower one
    v.fill(finz & (Z > FIN_TOP - 0.30), NAVY)
    v.fill(finz & (Z > FIN_TOP - 0.36) & (Z < FIN_TOP - 0.33), RED)
    for f in (0.2, 0.5, 0.8):
        xc = SLE_ - f * (SLE_ - STE_); v.stamp('ea-star.png', xc + 0.075, xc - 0.075, ZSTAR - 0.075, ZSTAR + 0.075)
    pants = (Z < -0.9) & (A > PX0 - 0.02) & (A < PX1 + 0.02)
    v.fill(pants & (Z > Z_AX + 0.05), NAVY)
    v.fill(pants & (Z > Z_AX + 0.005) & (Z < Z_AX + 0.03), RED)
    zm = float(zb(XLC)) - 0.5 * (LP1 + RW + LP2)   # both liveries: centred on the band between the stripes
    if not UNMARKED: v.stamp(SIDE_ART, TX0, TX1, zm - 0.5 * TH, zm + 0.5 * TH)
    if LIVERY == 'tf':   # v9: wings-and-headset badge in the fin's white field, both sides, reading the same way (like the text)
        v.stamp('ea-tf-badge.png', *FIN_LOGO)
    return v
VL, VR = side('ea_side_l', True), side('ea_side_r', False)
GAP_W = 0.03
LEv = lambda y: np.interp(np.abs(y), W.s, W.le); TEv = lambda y: np.interp(np.abs(y), W.s, W.te)
SLEv = lambda y: np.interp(np.abs(y), S.s, S.le); STEv = lambda y: np.interp(np.abs(y), S.s, S.te)
STAB_NAVY = 0.32 * float(np.mean(S.le - S.te))
# v11: Ty wants the stab navy edge + red line straight and spanwise (constant x, no sweep): one station, taken where
# the v10 offset line crossed mid-semispan
STAB_NAVY_X = float(np.interp(0.5 * S_TIP, S.s, S.le)) - STAB_NAVY
def plan(name, flip, bar=False):
    v = View(name, (-4.1, 4.1), (X0, X1), 4096, 3870, flip_a=flip, axes=(1, 0)); Y, Xx = v.A, v.B
    wz = Xx > -2.6
    # v11: on the stab the navy bar and red line are straight spanwise lines at constant x (STAB_NAVY_X)
    split = np.where(wz, LEv(Y) - 0.32 * (LEv(Y) - TEv(Y)), STAB_NAVY_X)
    v.fill(Xx > split, NAVY)
    if bar:   # v9 plain underside: a thick red bar behind the navy LE, 22 % chord at the root (v10) tapering to 11 % at the tip
        ch = LEv(Y) - TEv(Y); bw = np.where(wz, 0.22 * ch * (1 - 0.5 * np.clip(np.abs(Y) / W_TIP, 0, 1)), RW)
        v.fill((Xx <= split - GAP_W) & (Xx > split - GAP_W - bw), RED)
    else:
        v.fill((Xx <= split - GAP_W) & (Xx > split - GAP_W - RW), RED)
    return v
lo = lambda y: HW(np.abs(y)) + 0.04
hi = lambda y: LEv(y) - 0.32 * (LEv(y) - TEv(y)) - GAP_W - RW - 0.03
print('LETTER height root %.2f tip %.2f' % (hi(0.62) - lo(0.62), hi(3.62) - lo(3.62)))
VT = plan('ea_top', True)
if not UNMARKED: VT.stamp_warp('ea-sunrise.png', 3.62, 0.62, lo, hi); VT.stamp_warp('ea-aviation.png', -0.62, -3.62, lo, hi)
VB = plan('ea_bot', False, bar=(LIVERY in ('n300uy', 'unmarked')))
if LIVERY == 'tf': VB.stamp_warp('ea-talk-flying.png', -3.62, -0.62, lo, hi); VB.stamp_warp('ea-podcast.png', 0.62, 3.62, lo, hi)   # v8: TALK FLYING | PODCAST split across the wings

M = {}
for v in (VL, VR, VT, VB): M[v.name] = paint.mat_image('Paint_' + v.name, v.to_image(os.path.join(TEX, v.name + '.png')))
M['white'] = paint.mat_colour('Paint_white', WHITE); M['navy'] = paint.mat_colour('Paint_navy', NAVY)
M['tyre'] = paint.mat_colour('Paint_tyre', TYRE, rough=0.8); M['cabin'] = paint.mat_colour('Paint_cabin', CABIN, rough=0.8); M['glass'] = paint.mat_glass('Glass', col=(120, 150, 185), alpha=0.42)   # v3: bluer, denser tint like v9
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
for n in [k for k in ('Fuselage', 'Canopy_Frame', 'Fin', 'Rudder', 'Tail_Cables', 'Wheel_Pants') if k in P]:
    setm(P[n], ['ea_side_l', 'ea_side_r', 'white'])   # belly faces stay plain white (side paint would smear under)
    paint.project(P[n], lambda nrm, c: (2, None) if nrm.z <= -0.6 else ((0, VL) if nrm.y >= 0 else (1, VR)))
for n, m in (('Canopy_Glass', 'glass'), ('Spinner', 'prop_spinner'), ('Gear_Main', 'white'), ('Wheels', 'tyre'), ('Cabin', 'cabin')):
    setm(P[n], [m]); [setattr(p, 'material_index', 0) for p in P[n].data.polygons]
setm(P['Prop'], ['prop_blade', 'prop_tip', 'prop_nickel'])
for p, t in zip(P['Prop'].data.polygons, face_tag): p.material_index = t
print('PROP faces', len(face_tag), 'tip', face_tag.count(1), 'nickel', face_tag.count(2))
M['en_cyl'] = paint.mat_colour('Engine_cylinder', (128, 132, 138), rough=0.55, metal=0.3)
M['en_core'] = paint.mat_colour('Engine_cylinder_core', (74, 77, 82), rough=0.7, metal=0.2)
M['en_case'] = paint.mat_colour('Engine_case', (24, 24, 27), rough=0.6)
M['en_rocker'] = paint.mat_colour('Engine_rocker_cover', (34, 64, 140), rough=0.5)
M['en_pipe'] = paint.mat_colour('Engine_pipe', (92, 88, 84), rough=0.6, metal=0.4)
M['en_baffle'] = paint.mat_colour('Engine_baffle', (70, 72, 76), rough=0.8)
M['en_lip'] = M['white']; M['en_dark'] = paint.mat_colour('Engine_duct', (30, 31, 34), rough=0.9)
setm(P['Engine'], EN_T)
for p, t in zip(P['Engine'].data.polygons, en_tag): p.material_index = t
M['ck_seat'] = paint.mat_colour('Cockpit_seat', (66, 70, 78), rough=0.75)
M['ck_strap'] = paint.mat_colour('Cockpit_harness', (150, 138, 112), rough=0.85)
M['ck_panel'] = paint.mat_colour('Cockpit_panel', (52, 55, 61), rough=0.6)
M['ck_gauge'] = paint.mat_colour('Cockpit_gauge_bezel', (150, 154, 160), rough=0.4, metal=0.5)
M['ck_face'] = paint.mat_colour('Cockpit_gauge_face', (176, 182, 188), rough=0.3)
M['ck_stick'] = paint.mat_colour('Cockpit_stick', (42, 42, 46), rough=0.5)
M['ck_tube'] = paint.mat_colour('Cockpit_tube', (200, 202, 205), rough=0.5)
M['ck_tub'] = M['cabin']; M['ck_black'] = paint.mat_colour('Cockpit_dash_top', (39, 39, 40), rough=0.9)   # ~0.02 linear albedo
setm(P['Cockpit'], CK_T)
for _n in [t for t in CK_T if t != 'ck_tub']:   # v5: whole interior matte = high roughness, no metal, no specular sheen
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
for o in P.values(): o.data.transform(Matrix.Rotation(math.pi, 4, 'Z'))   # OwnSpecs read nose +x after glTF import
# v13 hinge rig data: each moving surface's node origin is ON its hinge line (rudder foot / elevator + aileron inboard
# end) and a top-level empty '<Name>_HingeAxis' sits at the other end, so hinge axis = node origin -> marker (same idea
# as the Great Lakes Gear_Pivot/Gear_HingeAxis).  Rotating the whole mesh about that line is the full deflection model.
from mathutils import Vector
RZ = Matrix.Rotation(math.pi, 4, 'Z'); EXTRA = []
for n, (a, b) in HINGES.items():
    pa, pb = RZ @ Vector(tuple(map(float, a))), RZ @ Vector(tuple(map(float, b)))
    o = P[n]; o.data.transform(Matrix.Translation(-pa)); o.location = pa; o['hinge_axis_marker'] = n + '_HingeAxis'
    e = bpy.data.objects.new(n + '_HingeAxis', None); bpy.context.scene.collection.objects.link(e); e.location = pb; EXTRA.append(e)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.object.select_all(action='DESELECT')
for o in list(P.values()) + EXTRA: o.select_set(True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, export_yup=True, export_apply=True, export_extras=True)
print('WROTE', OUT, os.path.getsize(OUT))
