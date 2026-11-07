# v13 Great Lakes 2T-1A-2 tandem open cockpits. exec'd by build_great_lakes.py in the PRE-yaw build frame
# (metres, +X nose, +Y port = the pilot's LEFT, +Z up). Geometry is ours, made from primitives, placed by eye off the
# greatlakes2t.co.uk 2T-1A-2 cockpit / panel photos (see PROVENANCE v13), the TCDS A18EA seat arms and the manual's
# control descriptions. Values marked (E) are estimates. Moving parts are separate objects; gl_rig.py (post-yaw) puts
# their origins on the pivots and writes the rig extras. Static parts are one multi-material 'Cockpit_Interior'.
# No intercoms, headsets or tablet mounts; no placard text, no registration. v13.1 (Ty, 2026-10-30): the GL has electrics, so the
# rear panel carries compact panel-mount radio + transponder control heads (plain faces, no maker text); no other avionics.
from gl_dialmaps import DIALS, SLIP_DEG
CK = {}; RIGPTS = {}; NEEDLES = {}; CK_INT = 'Cockpit_Interior'
def _o(n):
    if n not in CK: CK[n] = [bmesh.new(), []]
    return CK[n]
def put(n, mat, fn, *a, **k):
    # bmesh ops free temporary faces internally and later faces reuse those slots, so creation order != storage order:
    # tag the new faces directly through material_index (palette per object), never by counting
    bm_, tg = _o(n); old_ = set(bm_.faces); r = fn(bm_, *a, **k)
    if mat not in tg: tg.append(mat)
    mi = tg.index(mat)
    for f in bm_.faces:
        if f not in old_: f.material_index = mi
    return r
V3 = lambda p: np.array(p, float)
def _nz(v): v = V3(v); return v / np.linalg.norm(v)
def gbox(bm_, c, s, R=None):
    m = Matrix.Translation(tuple(c)) @ (R.to_4x4() if R is not None else Matrix()) @ Matrix.Diagonal((s[0], s[1], s[2], 1))
    bmesh.ops.create_cube(bm_, size=1.0, matrix=m)
def _frame(d, side):
    d = _nz(d); s = V3(side) - np.dot(side, d) * d; s = s / np.linalg.norm(s); n = np.cross(d, s)
    return Matrix((tuple(d), tuple(s), tuple(n))).transposed()
def segbox(bm_, a, b, wd_, th, side=(0, 1, 0)):
    """box from a to b, width wd_ along 'side' (made normal), thickness th along the third axis"""
    a, b = V3(a), V3(b); gbox(bm_, (a + b) / 2, (np.linalg.norm(b - a), wd_, th), _frame(b - a, side))
def gcyl(bm_, a, b, r, seg=12, r2=None):
    a, b = V3(a), V3(b); d = b - a; ln = np.linalg.norm(d)
    q = Vector((0, 0, 1)).rotation_difference(Vector(tuple(d / ln))).to_matrix().to_4x4()
    bmesh.ops.create_cone(bm_, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=ln, matrix=Matrix.Translation(tuple((a + b) / 2)) @ q)
def gsph(bm_, c, r, u=12, v=8): bmesh.ops.create_uvsphere(bm_, u_segments=u, v_segments=v, radius=r, matrix=Matrix.Translation(tuple(c)))
def gtube(bm_, pts, r, n=8): tube(bm_, pts, r, n)
def prism(bm_, pts, off):
    """closed prism: polygon pts (3D, planar) swept by the vector off"""
    a = [bm_.verts.new(tuple(p)) for p in pts]; b = [bm_.verts.new(tuple(V3(p) + V3(off))) for p in pts]; n = len(a)
    bm_.faces.new(a[::-1]); bm_.faces.new(b)
    for i in range(n): bm_.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]))
def strip(bm_, pts, wd_, th, nrm):
    """flat closed strap along a polyline, width across, thickness along nrm"""
    pts = [V3(p) for p in pts]; rs = []; nrm = _nz(nrm)
    for k, p in enumerate(pts):
        t = _nz(pts[min(k + 1, len(pts) - 1)] - pts[max(k - 1, 0)]); s = _nz(np.cross(t, nrm)); nn = np.cross(s, t)
        rs.append([tuple(p + u_ * s * wd_ / 2 + v_ * nn * th / 2) for u_, v_ in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    loft_rings(bm_, rs)
UVR = 0.492                                   # dial artwork circle radius / image size
def dial(bm_, x, yc, zc, r, dep=0.003, nseg=40):
    """closed instrument disc, aft face (toward the seat, -x) at x - dep, UV-mapped to a gl-dial-*.png"""
    uv = bm_.loops.layers.uv.verify(); ring = []
    for xx in (x, x - dep):
        ring.append([bm_.verts.new((xx, yc + r * math.sin(t), zc + r * math.cos(t))) for t in np.linspace(0, 2 * math.pi, nseg, endpoint=False)])
    f_aft = bm_.faces.new(ring[1]); bm_.faces.new(ring[0][::-1])
    for i in range(nseg):
        j = (i + 1) % nseg; bm_.faces.new((ring[0][i], ring[0][j], ring[1][j], ring[1][i]))
    for lp in f_aft.loops:
        y, z = lp.vert.co.y, lp.vert.co.z; lp[uv].uv = (0.5 + (yc - y) / r * UVR, 0.5 + (z - zc) / r * UVR)
def dirv(th):   # unit vector in the panel plane, th deg clockwise from 12 as seen from the seat (pilot's right = -y)
    t = math.radians(th); return V3((0.0, -math.sin(t), math.cos(t)))
def needle(bm_, x, c, L, wd_, th_deg, tail=0.25, th=0.0012):
    d = dirv(th_deg); p = dirv(th_deg + 90); c = V3((x, c[1], c[2]))
    pts = [c - tail * L * d - p * wd_ / 2, c + 0.82 * L * d - p * wd_ / 2, c + L * d, c + 0.82 * L * d + p * wd_ / 2, c - tail * L * d + p * wd_ / 2]
    prism(bm_, pts, (-th, 0, 0))
def hub(bm_, x, c, r): gcyl(bm_, (x + 0.001, c[1], c[2]), (x - 0.004, c[1], c[2]), r, 10)
def bezel(bm_, x, yc, zc, r, n=6):
    # n is the ring's CROSS-SECTION facet count. Six is plenty for the dark bezels, but the chrome one on the
    # front oil-pressure gauge is specular at about 2.8 mm section radius, and six flat facets caught the light
    # unevenly enough to read as a BROKEN ring from the seat (Ty, 2026-11-01). The ring was never broken: measured
    # off the export it is 384 triangles covering all 360 degrees with no uncovered angle anywhere. Shading, so a
    # shading fix -- more facets, no hole to close.
    pts = [(x - 0.004, yc + 0.95 * r * math.sin(t), zc + 0.95 * r * math.cos(t)) for t in np.linspace(0, 2 * math.pi, 33)]
    tube(bm_, pts, 0.05 * r + 0.0015, n, cap=False)

# ---- frame helpers: inner skin of the fuselage section ------------------------------------------------------------
WALL_IN = 0.014
def in_sec(x): return F_TOP(x) - WALL_IN, F_BOT(x) + WALL_IN, F_HW(x) - WALL_IN
def wall_y(x, z):
    top, bot, hw = in_sec(x); return abs(se_pt(t_at(z, top, bot), top, bot, hw)[0])
ZFL = -0.70                                   # cockpit floor (E: photo n76l_4 + seat arms)
ZTT = -0.74                                   # aileron torque tube under the floor (E)
XP_F, XP_R = XC1F + 0.002, XC2F + 0.002       # aft faces of the front / rear instrument panels
ZPB_F, ZPB_R = -0.06, -0.08                   # panel bottom edges (E, KPT_0054 / IMG_1664)
X_FW = -0.40                                  # firewall (TCDS datum = front face of firewall, seat arms 27.5 / 61.5 in)
EYE_F, EYE_R = (-1.22, 0.0, 0.27), (-1.95, 0.0, 0.29)   # (E) 95th-pct seated eye over the cushions

# ---- liner: inward-facing inner skin where the v12 hull was capped (footwell to the firewall, the bay between pits) --
bl = bmesh.new()
for xs in ([X_FW, -0.55, -0.70, -0.85, XC1F], [XC1A, 0.5 * (XC1A + XC2F), XC2F]):
    vs, fs = loft_rings(bl, [ring_closed(x, *in_sec(x)) for x in xs], False, False)
    for f, i, j in fs:
        c_ = f.calc_center_median(); f.normal_update()
        top, bot, hw = in_sec(c_.x); zw = bot + ZWF * (top - bot)
        if f.normal.y * c_.y + f.normal.z * (c_.z - zw) > 0: f.normal_flip()     # face the inside
    if xs[0] == X_FW:
        cap = bl.faces.new(vs[0]); cap.normal_update()
        if cap.normal.x > 0: cap.normal_flip()                                   # firewall faces aft
CK['Cockpit_Liner'] = [bl, ['ck_wall']]

# ---- floor, frame tubes -------------------------------------------------------------------------------------------------
YFL = min(wall_y(x, ZFL + 0.01) for x in (X_FW, -1.0, -1.6, -2.0)) - 0.006
put(CK_INT, 'ck_floor', gbox, ((X_FW - 0.02 + XC2A + 0.02) / 2, 0.05 + (YFL - 0.05) / 2, ZFL - 0.006), (X_FW - XC2A - 0.04, YFL - 0.05, 0.012))
put(CK_INT, 'ck_floor', gbox, ((X_FW - 0.02 + XC2A + 0.02) / 2, -0.05 - (YFL - 0.05) / 2, ZFL - 0.006), (X_FW - XC2A - 0.04, YFL - 0.05, 0.012))
for sy in (1, -1):
    for zz in (0.06, ZFL + 0.02):                                    # upper / lower longerons (E)
        put(CK_INT, 'ck_tube', gtube, [(x, sy * (wall_y(x, zz) - 0.013), zz) for x in np.linspace(X_FW - 0.01, XC2A + 0.01, 9)], 0.011, 8)
    st = [X_FW + 0.01, -0.97, -1.33, -1.64, XC2A + 0.01]
    for k, x in enumerate(st):                                       # verticals + one diagonal per bay (Warren-style, E)
        put(CK_INT, 'ck_tube', gtube, [(x, sy * (wall_y(x, z) - 0.013), z) for z in np.linspace(ZFL + 0.02, 0.06, 6)], 0.010, 8)
        if k + 1 < len(st):
            xa, xb = (x, st[k + 1]) if k % 2 == 0 else (st[k + 1], x)
            put(CK_INT, 'ck_tube', gtube, [(xa + (xb - xa) * q, sy * (wall_y(xa + (xb - xa) * q, ZFL + 0.02 + 0.76 * q) - 0.013), ZFL + 0.02 + 0.76 * q) for q in np.linspace(0, 1, 6)], 0.009, 8)

# ---- seats (aluminium buckets, dark cushions), 5-point harness + aerobatic lap belt ---------------------------------
def seat(xf, xb, ztop, hw, back_top, tag):
    zc = ztop - 0.05; zp = zc - 0.03
    put(CK_INT, 'ck_seat', gbox, ((xf + xb) / 2, 0, zp - 0.01), (xf - xb, 2 * hw, 0.02))                      # pan
    put(CK_INT, 'ck_cushion', gbox, ((xf + xb) / 2 - 0.005, 0, zc - 0.005), (xf - xb - 0.02, 2 * hw - 0.03, 0.065))
    b0 = V3((xb - 0.01, 0, zp - 0.02)); b1 = V3((back_top[0], 0, back_top[1])); ln = np.linalg.norm(b1 - b0)
    R = _frame(b1 - b0, (0, 1, 0))
    put(CK_INT, 'ck_seat', gbox, (b0 + b1) / 2 - 0.012 * np.array([1, 0, 0]), (ln, 2 * hw, 0.02), R)          # back shell
    put(CK_INT, 'ck_cushion', gbox, (b0 + b1) / 2 + V3((0.02, 0, 0.0)) + 0.02 * V3((0, 0, -1)), (ln * 0.78, 2 * hw - 0.05, 0.04), R)
    for sy in (1, -1):
        put(CK_INT, 'ck_seat', prism, [(xf, sy * hw, zp - 0.02), (xf, sy * hw, zc + 0.03), (xb, sy * hw, zc + 0.10), (b1[0], sy * hw, b1[2] - 0.10), (b1[0] - 0.03, sy * hw, b1[2] - 0.14), (xb - 0.02, sy * hw, zp - 0.02)], (0, -sy * 0.012, 0))
        for xx in (xf - 0.03, xb + 0.03):                                                                  # seat legs
            put(CK_INT, 'ck_tube', gcyl, (xx, sy * (hw - 0.03), ZFL), (xx, sy * (hw - 0.03), zp - 0.02), 0.010, 8)
    # harness: shoulder straps down the back, lap belts from the sides, crotch strap, buckle; wide aerobatic lap belt
    bk = _nz(b0 - b1); nb_ = V3((0.0, 0, 0)) + np.cross(bk, (0, 1, 0)); nb_ = _nz(nb_) if nb_[0] > 0 else -_nz(nb_)
    bkl = V3((xf - 0.11, 0, zc + 0.035))
    for sy in (1, -1):
        s0 = b1 + V3((0.025, sy * 0.075, -0.02)); s1 = b0 + (b1 - b0) * 0.25 + V3((0.035, sy * 0.06, 0)); s2 = bkl + V3((-0.03, sy * 0.03, 0.005))
        put(CK_INT, 'ck_belt', strip, [s0, s0 + (s1 - s0) * 0.5, s1, s2], 0.045, 0.004, nb_)
        put(CK_INT, 'ck_buckle', gbox, s0 + (s1 - s0) * 0.35 + 0.004 * nb_, (0.02, 0.05, 0.006), _frame(s1 - s0, (0, 1, 0)))
        l0 = V3((xb + 0.06, sy * (hw - 0.02), zc + 0.04)); put(CK_INT, 'ck_belt', strip, [l0, l0 + V3((0.06, -sy * 0.08, 0.002)), bkl + V3((0, sy * 0.035, 0))], 0.045, 0.004, (0, 0, 1))
        a0 = V3((xb + 0.10, sy * (hw - 0.015), zc + 0.044)); put(CK_INT, 'ck_belt2', strip, [a0, a0 + V3((0.05, -sy * 0.09, 0.004)), V3((xf - 0.16, sy * 0.045, zc + 0.045))], 0.070, 0.004, (0, 0, 1))
        put(CK_INT, 'ck_buckle', gbox, V3((xf - 0.16, sy * 0.05, zc + 0.05)), (0.045, 0.03, 0.006))
    put(CK_INT, 'ck_belt', strip, [V3((xf - 0.005, 0, zc + 0.03)), bkl + V3((0.03, 0, 0))], 0.045, 0.004, (0, 0, 1))
    put(CK_INT, 'ck_buckle', gcyl, bkl + V3((0, 0, 0.002)), bkl + V3((0, 0, 0.012)), 0.032, 16)              # rotary buckle
    put(CK_INT, 'ck_belt2', gbox, V3((xf - 0.16, 0, zc + 0.052)), (0.06, 0.08, 0.008))                         # aerobatic belt latch
seat(-0.99, -1.28, -0.50, 0.19, (-1.39, 0.00), 'F')
seat(-1.71, -2.00, -0.49, 0.20, (-2.08, 0.02), 'R')

# ---- instrument panels: black plates following the inner deck, round instruments -----------------------------------
def panel_plate(xp, zb):
    top, bot, hw = in_sec(xp); top -= 0.004; hw -= 0.004; t0 = t_at(zb, top, bot)
    pts = [(xp, *se_pt(t, top, bot, hw)) for t in np.linspace(t0, math.pi - t0, 31)]
    put(CK_INT, 'ck_panel', prism, pts, (0.006, 0, 0))
panel_plate(XP_F, ZPB_F); panel_plate(XP_R, ZPB_R)
# ASI, ALT, TC, VSI and G are the SHARED INSTRUMENT LIBRARY'S (tools/own_models/instruments, Ty 2026-11-05: one of each,
# used by every aircraft): this panel draws only their bezels, and the game puts the library instrument in each
# (FlyingGame.Core.Instruments.InstrumentLibrary.GreatLakes, Bridge/SharedInstruments). No face, needle or symbol here.
LIBRARY = ('ASI', 'ALT', 'TC', 'VSI', 'G')
DIALMAT = {'TACH': 'dial_tach', 'MP': 'dial_mp', 'OIL': 'dial_oil', 'CHT': 'dial_cht',
           'HOBBS': 'dial_hobbs', 'MAG': 'dial_mag', 'OILP2': 'dial_oilpress'}
HANDS = {'TACH': [('TACH', 0.86, 0.075)], 'MP': [('MP', 0.86, 0.075)], 'OIL': [('OILP', 0.84, 0.09), ('OILT', 0.84, 0.09)], 'OILP2': [('OILP2', 0.86, 0.075)],
         'CHT': [('CHT', 0.84, 0.09)]}
HNAME = {'TACH': 'Tach', 'MP': 'MP', 'OILP': 'OilP', 'OILT': 'OilT',
         'CHT': 'CHT', 'OILP2': 'OilP2'}
# (y, z, bezel radius) on each panel, +y = pilot's left. 3-1/8 in instruments r 0.040, 2-1/4 in r 0.028 (by eye off the photos)
# FRONT, laid out from Ty's photograph of the real front cockpit (art/GL-FRONT-PANEL-AUDIT.md). One row of SIX,
# left to right as the pilot sees it (+y is his LEFT, so y descends across the row): altimeter, airspeed, the
# small centre instrument, the combined oil temp/pressure, the SEPARATE chrome-bezel oil pressure, tachometer.
#
# SIZES ARE REAL INSTRUMENT CASES, and the audit's photo sizes are what told me so. Aircraft instruments come in
# 3-1/8 in and 2-1/4 in cases and nothing between. Measured off the photograph the three small ones are 0.71,
# 0.70 and 0.67 of the ASI -- 56.4, 55.6 and 53.2 mm against a 57.2 mm 2-1/4 in case, all three the same
# instrument size. The large ones read 1.10 (ALT) and 0.90 (TACH) of the ASI, and a 3-1/8 in case is a 3-1/8 in
# case: the altimeter is nearest the oblique camera and reads 10 percent large, the tachometer is furthest and
# reads 10 percent small. So the photo's spread is the camera, not the panel, and all three are 3-1/8 in.
#
# SPACING IS THE AUDIT'S RATIOS IN METRES -- 1.54, 1.31, 1.05, 1.05, 1.06 ASI DIAMETERS -- and taking them any
# other way is what put the chrome bezel in pieces. Last time round I scaled them to the row's existing 290 mm
# width, reasoning that an oblique photo does not fix absolute scale. It does not, but the instruments do: at
# 290 mm every adjacent pair in the row INTERPENETRATED, by 8 to 16 mm, and the only one bright enough to show
# it was the chrome ring on the oil pressure gauge, which the tachometer's face cut into at both ends (Ty saw a
# broken ring, 2026-11-01). Real instruments cannot occupy the same space. At one ASI diameter per unit the row
# is 477 mm on a 724 mm panel and the tightest pair -- oil pressure beside the tachometer -- has 13 mm of air.
#
# There is NO front G-meter. The thing above centre in the photograph is a wet compass, built below.
_ASI_R, _SMALL_R = 0.0397, 0.0286                      # 3-1/8 in and 2-1/4 in instrument cases
_ROW = [('ALT', _ASI_R), ('ASI', _ASI_R), ('TC', _SMALL_R), ('OIL', _SMALL_R), ('OILP2', _SMALL_R), ('TACH', _ASI_R)]
_GAPS = [1.54, 1.31, 1.05, 1.05, 1.06]
_STEP = 2 * _ASI_R                                     # the ratios are multiples of the ASI DIAMETER
_ROWZ, _YC = 0.030, -0.005                             # the row's height, and its centre on the panel
_Y0 = _YC + sum(_GAPS) * _STEP / 2
_FY, _acc = {}, _Y0
for _i, (_k, _rr) in enumerate(_ROW):
    _FY[_k] = (round(_acc, 4), _ROWZ, round(_rr, 4))
    if _i < len(_GAPS): _acc -= _GAPS[_i] * _STEP
# No two instruments may share space. This is the assertion the panel did not have.
for (_a, _ra), (_b, _rb) in zip(_ROW, _ROW[1:]):
    _d = _FY[_a][0] - _FY[_b][0]
    assert _d > _ra + _rb + 0.004, ('front row instruments interpenetrate', _a, _b, _d, _ra + _rb)
PANEL = {'F': (XP_F, dict(_FY)),
         'R': (XP_R, {'G': (0.124, 0.071, 0.040), 'MP': (-0.154, 0.071, 0.040), 'CHT': (-0.228, 0.032, 0.028), 'VSI': (0.203, -0.019, 0.040),
                      'ALT': (0.115, -0.019, 0.040), 'ASI': (0.028, -0.019, 0.040), 'TC': (-0.059, -0.019, 0.040), 'TACH': (-0.148, -0.019, 0.040),
                      'OIL': (-0.224, -0.029, 0.028), 'HOBBS': (0.296, -0.032, 0.025), 'MAG': (0.279, 0.020, 0.014)})}
for sfx, (xp, inst) in PANEL.items():
    for key, (y, z, r) in inst.items():
        rf = 0.9 * r if key != 'MAG' else r
        if key in LIBRARY:                                              # the library's: the panel's bezel only
            put(CK_INT, 'ck_bezel', bezel, xp, y, z, r, 6)
            continue
        put(CK_INT, DIALMAT[key], dial, xp, y, z, rf)
        # The standalone oil-pressure gauge is the one bright rim on a black panel (audit): chrome, not the
        # shared dark bezel every other instrument gets.
        if key != 'MAG':
            chrome = key == 'OILP2'
            put(CK_INT, 'ck_bezel_chrome' if chrome else 'ck_bezel', bezel, xp, y, z, r, 16 if chrome else 6)
        c = (xp, y, z)
        for hk, lf, wf in HANDS.get(key, []):
            n = 'Needle_%s_%s' % (HNAME[hk], sfx); mat = 'ck_needle'
            dx = 0.0028
            put(n, mat, needle, xp - 0.0045 - (0.0028 - dx), c, lf * rf, wf * rf * 0.6, DIALS[hk][3])
            if hk in ('TACH', 'MP', 'OILP', 'CHT'): put(n, 'ck_hub', hub, xp - 0.003, c, 0.06 * rf + 0.0015)
            NEEDLES[n] = dict(c=c, key=hk, grp=sfx)
        if key == 'MAG':
            n = 'MagKey_' + sfx; a0 = DIALS['MAG'][3]
            put(n, 'ck_buckle', gcyl, V3(c) + V3((-0.003, 0, 0)), V3(c) + V3((-0.012, 0, 0)), 0.0035, 8)
            put(n, 'ck_buckle', prism, [V3(c) + V3((-0.012, 0, 0)) + q for q in (dirv(a0 + 90) * 0.009, dirv(a0) * 0.016 + dirv(a0 + 90) * 0.009,
                                                                                  dirv(a0) * 0.016 - dirv(a0 + 90) * 0.009, -dirv(a0 + 90) * 0.009)], (-0.0025, 0, 0))
            NEEDLES[n] = dict(c=c, key='MAG', grp=sfx)
# rear panel extras: wet compass on the glareshield, blank plug, blank placard (no text); front: toggle + red light
cx, cy, cz = XP_R - 0.03, 0.032, 0.128
for c_, s_ in (((cx, cy, cz + 0.026), (0.06, 0.07, 0.006)), ((cx, cy, cz - 0.026), (0.06, 0.07, 0.006)), ((cx + 0.028, cy, cz), (0.006, 0.07, 0.058)),
               ((cx, cy + 0.032, cz), (0.06, 0.006, 0.058)), ((cx, cy - 0.032, cz), (0.06, 0.006, 0.058))):
    put(CK_INT, 'ck_bezel', gbox, c_, s_)
put(CK_INT, 'ck_tube', gbox, (cx + 0.02, cy, cz - 0.034), (0.03, 0.02, 0.012))                   # bracket to the panel top
put(CK_INT, 'ck_lubber', gbox, (cx - 0.0245, cy, cz), (0.001, 0.0016, 0.03))                      # lubber line
bc = bmesh.new(); uv = bc.loops.layers.uv.verify(); N_ = 48; RC, HC = 0.022, 0.016   # card drum, band UVs u = 1 - k / N
ra = [[bc.verts.new((cx - RC * math.cos(2 * math.pi * k / N_), cy + RC * math.sin(2 * math.pi * k / N_), cz + h)) for k in range(N_ + 1)] for h in (-HC / 2, HC / 2)]
for k in range(N_):
    f = bc.faces.new((ra[0][k], ra[0][k + 1], ra[1][k + 1], ra[1][k]))
    for lp, (u_, v_) in zip(f.loops, ((1 - k / N_, 0), (1 - (k + 1) / N_, 0), (1 - (k + 1) / N_, 1), (1 - k / N_, 1))): lp[uv].uv = (u_, v_)
for h, rr in ((0, ra[0][:N_]), (1, ra[1][:N_])): bc.faces.new(rr if h else rr[::-1])
CK['Compass_Card_R'] = [bc, ['compass']]; NEEDLES['Compass_Card_R'] = dict(c=(cx, cy, cz), key='COMPASS', grp='R')
# FRONT wet compass (Ty's photo, 2026-11-01): the thing above the centre instrument is a whiskey compass, not a
# G-meter. Same drum mechanism as the rear -- the card turns OPPOSITE the aeroplane so the heading sits under the
# lubber line -- with Codex's own front band and a static RGBA case/window overlay carrying the lubber line.
#
# Sized from art/GL-FRONT-PANEL-AUDIT.md: the housing is about 0.8 ASI diameters wide and 0.4-0.45 high, and the
# face texture carries transparent padding, so the PLANE is bigger than the visible case by 512/456 across and
# 512/256 up. Placed above the centre instrument and a little toward the pilot's right, where the photo has it
# (photo x 410 against the centre instrument's 368, at the audit's 0.0008 m/px).
fcx = XP_F - 0.004
fcy = _FY['TC'][0] - 0.033
# On the GLARESHIELD, clear above the gauges, like the rear one at z 0.128. The audit's photo offset (80 px
# above the centre instrument) is foreshortened by a camera looking down, and taken literally it put the case
# 3 mm above the gauge tops while standing 28 mm proud of the panel, so from the seat it leaned over the
# combined oil gauge and hid it. Checked on a seat-eye render, not by arithmetic.
fcz = _ROWZ + 0.095
FC_W, FC_H = 0.0719, 0.0688                               # the face PLANE, padding included
FW_W, FW_H = 0.0533, 0.0145                               # the window the card shows through
# A WHISKEY COMPASS IS A BOX WITH A WINDOW IN IT, and the first cut had the stack inside out. The pilot sits at
# SMALLER builder x, and the shallow case ran fcx-0.018..fcx while the card drum ran fcx-0.031..fcx+0.023: the drum
# stuck 13 mm out of the case's aft face and hung in the open, and the face plate (fcx-0.002..fcx) was buried
# INSIDE the case, invisible (Ty, 2026-11-01, off front-panel-seat-eye.png). So, from the pilot outwards: the
# alpha face plate is the aft-most surface, then the case, then the card wholly inside it. The case is five walls
# with the AFT SIDE OPEN -- a solid box would hide the very card the window exists to show -- and it is deep,
# because the card is 53 mm across and has to fit in there, which is how deep a real wet compass is.
CASE_D, WALL_ = 0.062, 0.004                                 # case depth forward of the face plate, wall thickness
cxm = fcx + CASE_D / 2
put(CK_INT, 'ck_bezel', gbox, (cxm, fcy, fcz + 0.015), (CASE_D, 0.064, WALL_))                 # roof
put(CK_INT, 'ck_bezel', gbox, (cxm, fcy, fcz - 0.015), (CASE_D, 0.064, WALL_))                 # floor
for _sy in (1, -1):
    put(CK_INT, 'ck_bezel', gbox, (cxm, fcy + _sy * 0.030, fcz), (CASE_D, WALL_, 0.034))       # sides
put(CK_INT, 'ck_bezel', gbox, (fcx + CASE_D - WALL_ / 2, fcy, fcz), (WALL_, 0.064, 0.034))     # far end
put(CK_INT, 'ck_tube', gbox, (fcx + 0.014, fcy, fcz - 0.024), (0.014, 0.016, 0.014))        # bracket down to the panel
# The card drum, axis VERTICAL like the rear one, inside the case and seen ONLY through the window in the face.
bf = bmesh.new(); uvf = bf.loops.layers.uv.verify(); NF_ = 48; RF_, HF_ = FW_W / 2, FW_H
DRUM_X = fcx + 0.003 + RF_                   # nearest point 3 mm forward of the face plate, so nothing pokes through
raf = [[bf.verts.new((DRUM_X - RF_ * math.cos(2 * math.pi * k / NF_), fcy + RF_ * math.sin(2 * math.pi * k / NF_), fcz + h))
        for k in range(NF_ + 1)] for h in (-HF_ / 2, HF_ / 2)]
for k in range(NF_):
    f = bf.faces.new((raf[0][k], raf[0][k + 1], raf[1][k + 1], raf[1][k]))
    for lp, (u_, v_) in zip(f.loops, ((1 - k / NF_, 0), (1 - (k + 1) / NF_, 0), (1 - (k + 1) / NF_, 1), (1 - k / NF_, 1))): lp[uvf].uv = (u_, v_)
for h, rr in ((0, raf[0][:NF_]), (1, raf[1][:NF_])): bf.faces.new(rr if h else rr[::-1])
CK['Compass_Card_F'] = [bf, ['compass_f']]; NEEDLES['Compass_Card_F'] = dict(c=(DRUM_X, fcy, fcz), key='COMPASS', grp='F')
# v13.1 (Ty, 2026-10-30): the GL has electrics, so the rear panel's radio hole gets real compact avionics: a TY91/TY96-style
# radio control head over a TT21/TT22-style transponder control head, each 63 W x 44 H mm (Trig datasheets), stacked on a
# black mounting plate that fills the old blank hole. Plain faces (gl-avx-*.png, no text); knob / display layout E.
def texbox(bm_, x, yc, zc, w_, h_, dep):
    """closed box, panel plane, aft face (toward the seat) at x - dep UV-mapped 0..1 (u to the pilot's right, v up)"""
    uv = bm_.loops.layers.uv.verify(); q = [(yc + w_ / 2, zc - h_ / 2), (yc - w_ / 2, zc - h_ / 2), (yc - w_ / 2, zc + h_ / 2), (yc + w_ / 2, zc + h_ / 2)]
    a = [bm_.verts.new((x, y, z)) for y, z in q]; b = [bm_.verts.new((x - dep, y, z)) for y, z in q]
    bm_.faces.new(a[::-1]); f = bm_.faces.new(b)
    for i in range(4): bm_.faces.new((a[i], a[(i + 1) % 4], b[(i + 1) % 4], b[i]))
    for lp in f.loops: lp[uv].uv = ((yc + w_ / 2 - lp.vert.co.y) / w_, (lp.vert.co.z - (zc - h_ / 2)) / h_)
AVY, AVZ = -0.059, 0.074; AVW, AVH = 0.063, 0.044
put(CK_INT, 'ck_plug', gbox, (XP_R - 0.001, AVY, AVZ), (0.003, AVW + 0.008, 2 * AVH + 0.010))       # mounting plate
DISPLAYS = {}
for nm, zc, art, lcd, knobs in (('Radio', AVZ + AVH / 2 + 0.0005, 'avx_com', (150, 50, 480, 250), (('COMVOL', 75, 200, 0.0045, 0.012), ('COMOUT', 555, 200, 0.0075, 0.010), ('COMIN', 555, 200, 0.0048, 0.017))),
                                ('Xpdr', AVZ - AVH / 2 - 0.0005, 'avx_xpdr', (40, 50, 440, 220), (('XPKNOB', 540, 200, 0.0058, 0.012),))):
    put(CK_INT, art, texbox, XP_R - 0.0025, AVY, zc, AVW, AVH, 0.010)                               # head body, face at XP_R - 0.0125
    px = lambda u, v: (AVY + AVW / 2 - u / 630 * AVW, zc + AVH / 2 - v / 440 * AVH)                  # face art px -> (y, z)
    (y0, z0), (y1, z1) = px(lcd[0], lcd[1]), px(lcd[2], lcd[3]); n = nm + '_Display_R'
    put(n, 'ck_lcd', texbox, XP_R - 0.0125, 0.5 * (y0 + y1), 0.5 * (z0 + z1), abs(y1 - y0) - 0.001, abs(z1 - z0) - 0.001, 0.0006)
    DISPLAYS[n] = dict(c=(XP_R - 0.0131, 0.5 * (y0 + y1), 0.5 * (z0 + z1)), size=(abs(y1 - y0) - 0.001, abs(z1 - z0) - 0.001))
    for hk, u, v, r, ln in knobs:
        y, z = px(u, v); n = {'COMVOL': 'Radio_VolumeKnob_R', 'COMOUT': 'Radio_TuneOuter_R', 'COMIN': 'Radio_TuneInner_R', 'XPKNOB': 'Xpdr_Knob_R'}[hk]
        x0 = XP_R - 0.0125; put(n, 'ck_grip', gcyl, (x0, y, z), (x0 - ln, y, z), r, 16)
        put(n, 'ck_buckle', segbox, (x0 - ln - 0.0004, y, z + 0.25 * r), (x0 - ln - 0.0004, y, z + 0.92 * r), 0.0012, 0.0008, (0, 1, 0))   # index line
        NEEDLES[n] = dict(c=(XP_R - 0.0125, y, z), key=hk, grp='R')
put(CK_INT, 'ck_placard', gbox, (XP_R - 0.001, 0.205, 0.095), (0.002, 0.06, 0.028))
# The toggle used to sit at y 0.21, z 0.05, which is inside the altimeter now that the row is spaced properly.
# It goes up beside the warning lamp, in the panel's top outboard corner, where a switch belongs on a real panel.
put(CK_INT, 'ck_grip', gbox, (XP_F - 0.004, 0.265, 0.095), (0.008, 0.018, 0.026))                # front toggle switch + red lamp (static)
put(CK_INT, 'ck_buckle', gcyl, (XP_F - 0.008, 0.265, 0.095), (XP_F - 0.024, 0.265, 0.103), 0.0025, 6)
put(CK_INT, 'ck_bezel', gcyl, (XP_F, 0.23, 0.095), (XP_F - 0.006, 0.23, 0.095), 0.009, 12)
put(CK_INT, 'ck_lamp', gsph, (XP_F - 0.006, 0.23, 0.095), 0.0065, 10, 6)

# The front compass's static case front: window, housing and the fixed lubber line, in front of its card.
put(CK_INT, 'compass_face_f', texbox, fcx, fcy, fcz, FC_W, FC_H, 0.002)

# ---- flight controls: aileron torque tube with both sticks, four rudder pedals, four heel brakes ---------------------
put('TorqueTube', 'ck_tube', gcyl, (-0.80, 0, ZTT), (-1.70, 0, ZTT), 0.019, 12); RIGPTS['TorqueTube'] = (-1.25, 0, ZTT)
for n, pv, tp in (('Stick_F', (-0.92, 0, ZTT), (-1.02, 0, -0.17)), ('Stick_R', (-1.60, 0, ZTT), (-1.72, 0, -0.17))):
    pv, tp = V3(pv), V3(tp); d = _nz(tp - pv)
    put(n, 'ck_tube', gcyl, pv + V3((0, -0.03, 0)), pv + V3((0, 0.03, 0)), 0.022, 10)                         # pitch yoke on the tube
    put(n, 'ck_stick', gcyl, pv, tp - 0.11 * d, 0.011, 10)
    put(n, 'ck_grip', gcyl, tp - 0.115 * d, tp, 0.0165, 12); put(n, 'ck_grip', gsph, tp, 0.0165, 12, 6)
    RIGPTS[n] = tuple(pv); RIGPTS[n + '_tip'] = tuple(tp)
PED_TILT = math.radians(15)                                    # pedal frames lean forward from the floor hinge (E)
for sfx, x0, yy in (('F', -0.50, 0.13), ('R', -1.12, 0.25)):
    for side, sy in (('L', 1), ('R', -1)):
        n = 'Pedal_%s_%s' % (sfx, side); h0 = V3((x0, sy * yy, ZFL + 0.015)); up_ = V3((math.sin(PED_TILT), 0, math.cos(PED_TILT)))
        put(n, 'ck_tube', gcyl, h0 + V3((0, 0.045, 0)), h0 + V3((0, -0.045, 0)), 0.009, 8)                     # hinge tube
        for q in (0.04, -0.04): put(n, 'ck_pedal', gcyl, h0 + V3((0, q, 0)), h0 + V3((0, q, 0)) + 0.20 * up_, 0.007, 8)   # frame sides
        for z_ in (0.10, 0.20): put(n, 'ck_pedal', gcyl, h0 + z_ * up_ + V3((0, 0.045, 0)), h0 + z_ * up_ + V3((0, -0.045, 0)), 0.007, 8)
        put(n, 'ck_pedal', gbox, h0 + 0.16 * up_ - 0.008 * V3((math.cos(PED_TILT), 0, -math.sin(PED_TILT))), (0.006, 0.075, 0.05), _frame(up_, (0, 1, 0)) @ Matrix.Rotation(math.pi / 2, 3, 'Y'))
        RIGPTS[n] = tuple(h0)
        n = 'Brake_%s_%s' % (sfx, side); hb = V3((x0 - 0.10, sy * yy, ZFL + 0.012)); ub = V3((math.sin(math.radians(40)), 0, math.cos(math.radians(40))))
        put(n, 'ck_tube', gcyl, hb + V3((0, 0.03, 0)), hb + V3((0, -0.03, 0)), 0.007, 8)
        put(n, 'ck_pedal', segbox, hb, hb + 0.075 * ub, 0.055, 0.006, (0, 1, 0)); RIGPTS[n] = tuple(hb)
put(CK_INT, 'ck_tube', gcyl, (-1.50, 0.09, ZFL), (-1.50, 0.09, ZFL + 0.08), 0.022, 12)          # brake reservoir ahead of the rear stick (manual)

# ---- engine controls on the left wall: throttle / prop / mixture quadrant, stabilizer trim, fuel shut-off -----------
LEV_L, LEV_A0 = 0.15, -25.0                    # lever length, angle from vertical at 0 (aft); +50 deg forward at 1 (E)
# THE TWO QUADRANTS ARE NOT THE SAME, and Ty's photographs are why (2026-11-01).
#   FRONT: a curved raw-metal gate plate on the sidewall, long levers angled up and aft (ref-real-great-lakes-
#          quadrant.png).
#   REAR:  a grey console BOX on the left sidewall at hip/elbow height with SHORT levers standing out of its top
#          face, switch panels below (ref-real-great-lakes-rear-quadrant.jpg).
# LEVER ORDER, left to right as the pilot sees it from the seat: THROTTLE (black), PROP (blue), MIXTURE (red).
# Ty flies a real Great Lakes and that is his order. On 2026-11-01 I reversed it to prop/mixture/throttle from my
# own reading of a dark photograph; it was right before I touched it. Do not re-derive this from axis signs --
# render it from the seat eye and look (GreatLakesQuadrantShots).
for sfx, xq in (('F', -1.04), ('R', -1.72)):
    zq = -0.32; yw = wall_y(xq, zq); rear = sfx == 'R'
    if rear:
        zq = -0.30                                                                                            # console top, hip/elbow height
        yc_ = yw - 0.055
        put(CK_INT, 'ck_quadrant', gbox, (xq, yc_, zq - 0.075), (0.24, 0.105, 0.15))                          # console box
        put(CK_INT, 'ck_quadrant', gbox, (xq, yc_, zq + 0.004), (0.22, 0.095, 0.008))                         # its top plate
        put(CK_INT, 'ck_grip', gbox, (xq - 0.085, yw - 0.030, zq - 0.115), (0.07, 0.052, 0.055))              # switch panel below, forward
        put(CK_INT, 'ck_grip', gbox, (xq + 0.040, yw - 0.030, zq - 0.115), (0.07, 0.052, 0.055))              # switch panel below, aft
    else:
        put(CK_INT, 'ck_quadrant', gbox, (xq, yw - 0.006, zq + 0.05), (0.15, 0.012, 0.13))                    # wall plate (gate)
        put(CK_INT, 'ck_quadrant', gbox, (xq, 0.5 * (yw - 0.012 + 0.29), zq), (0.06, yw - 0.012 - 0.29, 0.04))  # lever box
    lev_l = 0.085 if rear else LEV_L                                                                          # short levers in the console
    base_y = (yw - 0.055 - 0.030) if rear else 0.300
    # Three levers side by side on one gate, same length and same shaft. The "thinner prop lever set higher and
    # further aft" that was here briefly only ever existed to prop up the wrong order, and goes with it.
    # k ascends toward the pilot's LEFT (measured from a render taken AT THE SEAT EYE, not reasoned about), so
    # the leftmost lever is the LAST one in this tuple.
    for k, (n0, knob) in enumerate((('Mixture', 'ck_knob_red'), ('Prop', 'ck_knob_blue'), ('Throttle', 'ck_grip'))):
        n = '%s_%s' % (n0, sfx); y_ = base_y + 0.025 * k
        pv = V3((xq, y_, zq)); a = math.radians(LEV_A0); d = V3((math.sin(a), 0, math.cos(a)))
        put(n, 'ck_quadrant', gcyl, pv + V3((0, -0.006, 0)), pv + V3((0, 0.006, 0)), 0.012, 10)
        put(n, 'ck_stick', segbox, pv, pv + lev_l * d, 0.006, 0.012, (0, 1, 0))
        put(n, knob, gsph, pv + (lev_l + 0.008) * d, 0.015, 12, 8); RIGPTS[n] = tuple(pv); RIGPTS[n + '_tip'] = tuple(pv + lev_l * d)
    zt = -0.42; yt_ = wall_y(xq, zt)
    put(CK_INT, 'ck_quadrant', gbox, (xq, yt_ - 0.006, zt), (0.16, 0.012, 0.03))                               # trim slot
    n = 'Trim_' + sfx; put(n, 'ck_stick', gcyl, (xq, yt_ - 0.012, zt), (xq, yt_ - 0.05, zt), 0.005, 8)
    put(n, 'ck_trim', gsph, (xq, yt_ - 0.055, zt), 0.014, 12, 8); RIGPTS[n] = (xq, yt_ - 0.03, zt)
    # fuel shut-off under the right side of the panel: quarter-turn handle, ON = blade vertical (modelled), OFF = 90 cw (E)
    xp = XP_F if sfx == 'F' else XP_R; zb = ZPB_F if sfx == 'F' else ZPB_R; c = V3((xp, -0.20, zb - 0.045))
    put(CK_INT, 'ck_quadrant', gbox, c + V3((0.003, 0, 0.012)), (0.006, 0.06, 0.07))
    n = 'FuelValve_' + sfx
    put(n, 'ck_bezel', gcyl, c + V3((0, 0, 0)), c + V3((-0.014, 0, 0)), 0.008, 10)
    put(n, 'ck_knob_red', segbox, c + V3((-0.016, 0, -0.022)), c + V3((-0.016, 0, 0.026)), 0.012, 0.008, (0, 1, 0))
    RIGPTS[n] = tuple(c)
print('COCKPIT objects', len(CK), 'needles', len(NEEDLES), 'faces', {n: len(v[0].faces) for n, v in CK.items() if n in (CK_INT, 'Cockpit_Liner')})
CK_TAGS = {}
for n, (bm_, tg) in CK.items():
    CK_TAGS[n] = [tg[f.material_index] for f in bm_.faces]; P[n] = link(n, bm_)
