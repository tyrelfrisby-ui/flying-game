# v15 Extra 300/L tandem cockpit (Ty, 2026-10-30): clean-room, every instrument and control rigged. exec'd by build_ea300.py
# in the PRE-yaw build frame (metres, +X nose, +Y port = the pilot's LEFT, +Z up), after the v4-v8 interior shell
# ('Cockpit_Shell': tub, ledges, seats, baggage, panel slabs, tube frame). Geometry is ours, made from primitives and
# placed by eye off public photos (refs listed in docs/models/ea300-own-PROVENANCE.md v15, never traced), the EA 300/L POH
# (markings, control list) and the IPC (part breakdown). Values marked (E) are estimates. Moving parts are separate
# objects; ea_rig.py (post-yaw) puts their origins on the pivots and writes the rig extras. Static parts are one
# multi-material 'Cockpit_Interior'. No intercoms, headsets or tablet mounts; no placard text, no registration, no maker
# text. Rear (PIC) panel carries compact panel-mount radio + transponder control heads (plain faces).
# Nothing here comes from the FlightGear EA300 (or any other third-party model): no file of it is read.
from ea_dialmaps import DIALS, SLIP_DEG
CK = {}; RIGPTS = {}; NEEDLES = {}; CK_INT = 'Cockpit_Interior'
def _o(n):
    if n not in CK: CK[n] = [bmesh.new(), []]
    return CK[n]
def put(n, mat, fn, *a, **k):
    # tag the new faces through material_index (palette per object), never by counting (bmesh ops reuse face slots)
    bm_, tg = _o(n); old_ = set(bm_.faces); r = fn(bm_, *a, **k)
    if mat not in tg: tg.append(mat)
    mi = tg.index(mat)
    for f in bm_.faces:
        if f not in old_: f.material_index = mi
    return r
from mathutils import Vector
V3 = lambda p: np.array(p, float)
def _nz(v): v = V3(v); return v / np.linalg.norm(v)
def tube(bm_, pts, r, n=8, r2=None, cap=True):
    pts = np.array(pts, float); rs = []; m = len(pts)
    for k, p in enumerate(pts):
        d = pts[min(k + 1, m - 1)] - pts[max(k - 1, 0)]; d /= np.linalg.norm(d)
        ref = np.array([0, 0, 1.0]) if abs(d[0]) > 0.8 else np.array([1.0, 0, 0])
        u = np.cross(d, ref); u /= np.linalg.norm(u); w_ = np.cross(d, u)
        rk = r if r2 is None else r + (r2 - r) * k / (m - 1)
        rs.append([tuple(p + rk * (math.cos(t) * u + math.sin(t) * w_)) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)])
    loft_rings(bm_, rs, cap, cap)
def gbox(bm_, c, s, R=None):
    m = Matrix.Translation(tuple(c)) @ (R.to_4x4() if R is not None else Matrix()) @ Matrix.Diagonal((s[0], s[1], s[2], 1))
    bmesh.ops.create_cube(bm_, size=1.0, matrix=m)
def _frame(d, side):
    d = _nz(d); s = V3(side) - np.dot(side, d) * d; s = s / np.linalg.norm(s); n = np.cross(d, s)
    return Matrix((tuple(d), tuple(s), tuple(n))).transposed()
def segbox(bm_, a, b, wd_, th, side=(0, 1, 0)):
    a, b = V3(a), V3(b); gbox(bm_, (a + b) / 2, (np.linalg.norm(b - a), wd_, th), _frame(b - a, side))
def gcyl(bm_, a, b, r, seg=12, r2=None):
    a, b = V3(a), V3(b); d = b - a; ln = np.linalg.norm(d)
    q = Vector((0, 0, 1)).rotation_difference(Vector(tuple(d / ln))).to_matrix().to_4x4()
    bmesh.ops.create_cone(bm_, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=ln, matrix=Matrix.Translation(tuple((a + b) / 2)) @ q)
def gsph(bm_, c, r, u=12, v=8): bmesh.ops.create_uvsphere(bm_, u_segments=u, v_segments=v, radius=r, matrix=Matrix.Translation(tuple(c)))
def prism(bm_, pts, off):
    a = [bm_.verts.new(tuple(p)) for p in pts]; b = [bm_.verts.new(tuple(V3(p) + V3(off))) for p in pts]; n = len(a)
    bm_.faces.new(a[::-1]); bm_.faces.new(b)
    for i in range(n): bm_.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]))
def dquad(bm_, pts):
    """double-sided flat polygon (two faces, own verts) for linings seen from either side"""
    for flip in (0, 1):
        v = [bm_.verts.new(tuple(p)) for p in pts]; bm_.faces.new(v if flip else v[::-1])
UVR = 0.492
def dial(bm_, x, yc, zc, r, dep=0.003, nseg=40):
    """closed instrument disc, aft face (toward the seat, -x) at x - dep, UV-mapped to an ea-dial-*.png"""
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
def bezel(bm_, x, yc, zc, r):
    pts = [(x - 0.004, yc + 0.95 * r * math.sin(t), zc + 0.95 * r * math.cos(t)) for t in np.linspace(0, 2 * math.pi, 33)]
    tube(bm_, pts, 0.05 * r + 0.0015, 6, cap=False)
def texbox(bm_, x, yc, zc, w_, h_, dep):
    """closed box, panel plane, aft face at x - dep UV-mapped 0..1 (u to the pilot's right, v up)"""
    uv = bm_.loops.layers.uv.verify(); q = [(yc + w_ / 2, zc - h_ / 2), (yc - w_ / 2, zc - h_ / 2), (yc - w_ / 2, zc + h_ / 2), (yc + w_ / 2, zc + h_ / 2)]
    a = [bm_.verts.new((x, y, z)) for y, z in q]; b = [bm_.verts.new((x - dep, y, z)) for y, z in q]
    bm_.faces.new(a[::-1]); f = bm_.faces.new(b)
    for i in range(4): bm_.faces.new((a[i], a[(i + 1) % 4], b[(i + 1) % 4], b[i]))
    for lp in f.loops: lp[uv].uv = ((yc + w_ / 2 - lp.vert.co.y) / w_, (lp.vert.co.z - (zc - h_ / 2)) / h_)
hwf_ = lambda x: float(np.interp(-x, -RX, RW_))            # outer skin half-width
_wz = lambda x: float(np.interp(-x, [-q for q in _WX], _WZ)) + 0.032   # side-ledge top (v6 ledges)

# ---- canopy: Ty's rule (2026-10-30): the real canopy is a separate part that opens, closes and jettisons. EA 300/L: ONE
# transparency from the windscreen back (AAIB G-ZXCL), hinged on the RIGHT sill, two red lock handles (airshows.aero egress
# notes). Shell touch: the v3 glass / frame objects are split by connected piece; the bubble + its frame move (z > 0.2),
# the small lower side / floor windows stay with the fuselage as Window_Glass / Window_Frame.
def _split(o, newname, keep):
    bm_ = bmesh.new(); bm_.from_mesh(o.data); bm_.faces.ensure_lookup_table(); seen = set(); mv = set()
    for f in bm_.faces:
        if f.index in seen: continue
        st = [f]; comp = []; seen.add(f.index)
        while st:
            g = st.pop(); comp.append(g.index)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen: seen.add(h.index); st.append(h)
        zs = [l.vert.co.z for i in comp for l in bm_.faces[i].loops]
        if keep(min(zs), max(zs)): mv.update(comp)
    b2 = bm_.copy(); b2.faces.ensure_lookup_table()
    bmesh.ops.delete(bm_, geom=[f for f in bm_.faces if f.index not in mv], context='FACES'); bm_.to_mesh(o.data); bm_.free()
    bmesh.ops.delete(b2, geom=[f for f in b2.faces if f.index in mv], context='FACES')
    if b2.faces: P[newname] = link(newname, b2)
    else: b2.free()
for n_, nn_ in (('Canopy_Glass', 'Window_Glass'), ('Canopy_Frame', 'Window_Frame')): _split(P[n_], nn_, lambda z0, z1: z0 > 0.2)
_cg = np.array([v.co[:] for v in P['Canopy_Glass'].data.vertices]); _cf = np.array([v.co[:] for v in P['Canopy_Frame'].data.vertices])
_ca = np.concatenate([_cg, _cf])
def _edge_pt(x, sy):   # outermost low point of the moving canopy near station x on side sy
    s = _ca[np.abs(_ca[:, 0] - x) < 0.03]; s = s[s[:, 1] * sy > 0]; s = s[s[:, 2] < s[:, 2].min() + 0.02]
    return s[np.argmax(s[:, 1] * sy)]
CAN_H0, CAN_H1 = _edge_pt(-0.5, -1), _edge_pt(-1.9, -1)     # hinge line on the right sill (E: exact hinge pins not modelled)
CAN_L = _edge_pt(-1.2, 1)                                    # a point on the left (opening) edge, for the rig sign
print('CANOPY hinge', CAN_H0.round(3), '->', CAN_H1.round(3), 'left edge', CAN_L.round(3), 'glass verts', len(_cg))
# fixed sill rim the canopy closes onto (the hole in the fuselage skin), 6 mm under the opening edge
_fb = bmesh.new(); _fb.from_mesh(P['Fuselage'].data); _adj = {}
for e in _fb.edges:
    if e.is_boundary:
        a, c = e.verts; _adj.setdefault(a, []).append(c); _adj.setdefault(c, []).append(a)
_seen = set(); _loops = []
for v0 in _adj:
    if v0 in _seen: continue
    lp = [v0]; _seen.add(v0); cur = v0
    while True:
        nx = [q for q in _adj[cur] if q not in _seen]
        if not nx: break
        cur = nx[0]; _seen.add(cur); lp.append(cur)
    a_ = np.array([tuple(v.co) for v in lp])
    if len(lp) > 8 and a_[:, 2].mean() > 0.15: _loops.append(a_)
_fb.free(); sb = bmesh.new()
for lp in _loops:
    for _ in range(6): lp = 0.5 * lp + 0.25 * (np.roll(lp, 1, 0) + np.roll(lp, -1, 0))
    lp = lp + np.array((0, 0, -0.006)); rs = []
    for i in range(len(lp)):
        t = lp[(i + 1) % len(lp)] - lp[i - 1]; t /= np.linalg.norm(t)
        u = np.cross(t, (0, 0, 1)); u = u / (np.linalg.norm(u) + 1e-9) if np.linalg.norm(u) > 1e-3 else np.array((0, 1.0, 0)); w_ = np.cross(t, u)
        rs.append([tuple(lp[i] + 0.010 * (math.cos(a) * u + math.sin(a) * w_)) for a in np.linspace(0, 2 * math.pi, 8, endpoint=False)])
    loft_rings(sb, rs + [rs[0]])
P['Canopy_Sill'] = link('Canopy_Sill', sb); print('CANOPY SILL loops', [len(l) for l in _loops])
# two red lock handles inside the canopy frame on the LEFT (opening) side, front + rear seat (both cockpits, POH); and
# a red emergency-release pull on the right sill for each seat (E: position by eye; the sim owns jettison)
for sfx, xl in (('F', -0.70), ('R', -1.55)):
    pe = _edge_pt(xl, 1); pv = V3((xl, pe[1] - 0.030, pe[2] + 0.020)); n = 'CanopyLock_' + sfx
    put(n, 'ck_quadrant', gcyl, pv + V3((0, 0.010, 0)), pv + V3((0, -0.006, 0)), 0.008, 10)
    put(n, 'ck_knob_red', segbox, pv, pv + V3((-0.075, 0, 0)), 0.012, 0.016, (0, 1, 0))
    put(n, 'ck_knob_red', gsph, pv + V3((-0.078, 0, 0)), 0.011, 10, 6); RIGPTS[n] = tuple(pv)
    pr = _edge_pt(xl, -1); c = V3((xl, pr[1] + 0.035, pr[2] - 0.045)); n = 'CanopyRelease_' + sfx
    put(CK_INT, 'ck_quadrant', gbox, c + V3((0, -0.010, 0)), (0.05, 0.006, 0.03))
    put(n, 'ck_stick', gcyl, c + V3((0, -0.010, 0)), c + V3((0, 0.012, 0)), 0.003, 8)
    put(n, 'ck_knob_red', segbox, c + V3((0.022, 0.016, 0)), c + V3((-0.022, 0.016, 0)), 0.010, 0.012, (0, 0, 1)); RIGPTS[n] = tuple(c)

# ---- footwell + front bulkhead: v14 left the space ahead of the front panel open to the engine bay (rocker covers showed
# above the front arch). Dark lining from the front seat's panel to the firewall (E stations), double-sided.
X_BH, X_FW, YW_, ZC_ = -0.09, 0.60, 0.29, 0.27
def _fw(x): return min(YW_, hwf_(x) - 0.04), max(ZF, float(botf(x)) + 0.03), min(ZC_, float(topf(x)) - 0.03)
_xs = list(np.linspace(X_BH, X_FW, 8))
for a, b in zip(_xs[:-1], _xs[1:]):
    (ya, za0, za1), (yb, zb0, zb1) = _fw(a), _fw(b)
    put('Footwell_Liner', 'ck_wall', dquad, [(a, -ya, za0), (b, -yb, zb0), (b, yb, zb0), (a, ya, za0)]); put('Footwell_Liner', 'ck_wall', dquad, [(a, -ya, za1), (a, ya, za1), (b, yb, zb1), (b, -yb, zb1)])
    for sy in (1, -1): put('Footwell_Liner', 'ck_wall', dquad, [(a, sy * ya, za0), (b, sy * yb, zb0), (b, sy * yb, zb1), (a, sy * ya, za1)])
y_, z0_, z1_ = _fw(X_FW); put('Footwell_Liner', 'ck_wall', dquad, [(X_FW, -y_, z0_), (X_FW, y_, z0_), (X_FW, y_, z1_), (X_FW, -y_, z1_)])   # firewall
y_, z0_, z1_ = _fw(X_BH); zt_ = float(topf(X_BH)) - 0.03; hwt = float(np.interp(-X_BH, -RX, RW_)) - 0.05
for sy in (1, -1):    # bulkhead sides out to the v6 tub wall line, top piece over the footwell mouth up to the deck
    put('Footwell_Liner', 'ck_wall', dquad, [(X_BH, sy * y_, ZF), (X_BH, sy * hwt, ZF), (X_BH, sy * min(hwt, sillw(X_BH) - 0.03), zt_), (X_BH, sy * y_, zt_)])
put('Footwell_Liner', 'ck_wall', dquad, [(X_BH, -y_, z1_), (X_BH, y_, z1_), (X_BH, y_, zt_), (X_BH, -y_, zt_)])
print('FOOTWELL', [tuple(round(q, 3) for q in _fw(x)) for x in (X_BH, 0.2, X_FW)], 'bulkhead top', round(zt_, 3), 'tub hw', round(hwt, 3))

# ---- eyes (E): seated eye over each cushion; the rear seat is the default solo view
EYE_F, EYE_R = (-0.80, 0.0, 0.55), (-1.70, 0.0, 0.55)

# ---- instruments: front arch panel (vertical, FL = 0) and the rear (PIC) panel ----------------------------------
XP_F, XP_R = FPX - 0.01, RPX
DIALMAT = {k: 'dial_' + k.lower() for k in ('ASI', 'ALT', 'VSI', 'TACH', 'MP', 'OIL', 'CHT', 'G', 'TC', 'MAG', 'FUEL')}
HANDS = {'ASI': [('ASI', 0.86, 0.075)], 'ALT': [('ALT100', 0.86, 0.06), ('ALT1000', 0.56, 0.12)], 'VSI': [('VSI', 0.86, 0.075)],
         'TACH': [('TACH', 0.86, 0.075)], 'MP': [('MP', 0.86, 0.075)], 'OIL': [('OILP', 0.84, 0.09), ('OILT', 0.84, 0.09)],
         'CHT': [('CHT', 0.84, 0.09)], 'FUEL': [('FUEL', 0.84, 0.09)], 'G': [('G', 0.86, 0.075), ('GMAX', 0.80, 0.05), ('GMIN', 0.80, 0.05)]}
HNAME = {'ASI': 'ASI', 'ALT100': 'Alt100', 'ALT1000': 'Alt1000', 'VSI': 'VSI', 'TACH': 'Tach', 'MP': 'MP', 'OILP': 'OilP', 'OILT': 'OilT',
         'CHT': 'CHT', 'FUEL': 'Fuel', 'G': 'G', 'GMAX': 'GMax', 'GMIN': 'GMin'}
# (y, z, bezel radius), +y = pilot's left. 3-1/8 in r 0.040, 2-1/4 in r 0.028 (E layout by eye off the 300L photos)
PANEL = {'F': (XP_F, {'ASI': (0.095, 0.255, 0.040), 'G': (0.0, 0.255, 0.040), 'ALT': (-0.095, 0.255, 0.040),   # Ty: front ASI, ALT, G
                      'TACH': (0.200, 0.230, 0.028), 'OIL': (-0.200, 0.230, 0.028)}),                          # (E) small engine pair
         'R': (XP_R, {'ASI': (0.092, 0.258, 0.040), 'TC': (0.0, 0.258, 0.040), 'ALT': (-0.092, 0.258, 0.040),
                      'G': (0.092, 0.152, 0.040), 'MP': (0.0, 0.152, 0.040), 'VSI': (-0.092, 0.152, 0.040),
                      'CHT': (0.185, 0.270, 0.028), 'FUEL': (0.185, 0.190, 0.028), 'OIL': (-0.180, 0.262, 0.030), 'TACH': (-0.180, 0.160, 0.036),
                      'MAG': (0.262, 0.275, 0.014)})}
for sfx, (xp, inst) in PANEL.items():
    put(CK_INT, 'ck_plug', gbox, (xp + 0.0005, 0, 0.2 if sfx == 'F' else 0.204), (0.001, 0.001, 0.001))   # keeps the palette order stable
    for key, (y, z, r) in inst.items():
        rf = 0.9 * r if key != 'MAG' else r
        put(CK_INT, DIALMAT[key], dial, xp, y, z, rf)
        if key != 'MAG': put(CK_INT, 'ck_bezel', bezel, xp, y, z, r)
        c = (xp, y, z)
        for hk, lf, wf in HANDS.get(key, []):
            n = 'Needle_%s_%s' % (HNAME[hk], sfx); mat = 'ck_telltale' if hk in ('GMAX', 'GMIN') else 'ck_needle'
            dx = {'GMAX': 0.0, 'GMIN': 0.0015, 'ALT1000': 0.0015}.get(hk, 0.0028)
            put(n, mat, needle, xp - 0.0045 - (0.0028 - dx), c, lf * rf, wf * rf * 0.6, DIALS[hk][3])
            if hk in ('ASI', 'ALT100', 'VSI', 'TACH', 'MP', 'OILP', 'CHT', 'G', 'FUEL'): put(n, 'ck_hub', hub, xp - 0.003, c, 0.06 * rf + 0.0015)
            NEEDLES[n] = dict(c=c, key=hk, grp=sfx)
        if key == 'TC':
            n = 'TC_Symbol_' + sfx; s_ = rf / 0.492 / 512
            put(n, 'ck_needle', prism, [V3(c) + V3((-0.0045, 0, 0)) + q for q in (dirv(-90) * 0.62 * rf + V3((0, 0, -0.003)), dirv(90) * 0.62 * rf + V3((0, 0, -0.003)),
                                                                              dirv(90) * 0.62 * rf + V3((0, 0, 0.003)), dirv(-90) * 0.62 * rf + V3((0, 0, 0.003)))], (-0.0012, 0, 0))
            put(n, 'ck_needle', gcyl, V3(c) + V3((-0.003, 0, 0)), V3(c) + V3((-0.0058, 0, 0)), 0.09 * rf, 12)
            put(n, 'ck_needle', segbox, V3(c) + V3((-0.0052, 0, 0.0)), V3(c) + V3((-0.0052, 0, 0.2 * rf)), 0.006, 0.001, (0, 1, 0))
            NEEDLES[n] = dict(c=c, key='TC', grp=sfx)
            pv = V3(c) + V3((0, 0, 40 * s_)); n = 'TC_Ball_' + sfx
            put(n, 'ck_ball', gsph, pv + V3((-0.004, 0, -200 * s_)), 18 * s_, 10, 6)
            NEEDLES[n] = dict(c=tuple(pv), key='BALL', grp=sfx)
        if key == 'MAG':
            n = 'MagKey_' + sfx; a0 = DIALS['MAG'][3]
            put(n, 'ck_buckle', gcyl, V3(c) + V3((-0.003, 0, 0)), V3(c) + V3((-0.012, 0, 0)), 0.0035, 8)
            put(n, 'ck_buckle', prism, [V3(c) + V3((-0.012, 0, 0)) + q for q in (dirv(a0 + 90) * 0.009, dirv(a0) * 0.016 + dirv(a0 + 90) * 0.009,
                                                                                  dirv(a0) * 0.016 - dirv(a0 + 90) * 0.009, -dirv(a0 + 90) * 0.009)], (-0.0025, 0, 0))
            NEEDLES[n] = dict(c=c, key='MAG', grp=sfx)
# rear panel, left column under the key: master toggle, prop vernier (black), mixture vernier (red) - verniers rear only (POH)
c = V3((XP_R, 0.262, 0.230)); put(CK_INT, 'ck_bezel', gcyl, c, c + V3((-0.004, 0, 0)), 0.008, 12)
n = 'Switch_Master_R'; put(n, 'ck_buckle', gcyl, c + V3((-0.003, 0, 0)), c + V3((-0.020, 0, 0.012)), 0.0025, 8); RIGPTS[n] = tuple(c + V3((-0.003, 0, 0)))
for n, z_, knob in (('Prop_R', 0.180, 'ck_grip'), ('Mixture_R', 0.130, 'ck_knob_red')):
    c = V3((XP_R, 0.262, z_)); put(CK_INT, 'ck_bezel', gcyl, c, c + V3((-0.004, 0, 0)), 0.011, 14)
    put(n, 'ck_buckle', gcyl, c + V3((0.06, 0, 0)), c + V3((-0.010, 0, 0)), 0.0035, 8)
    put(n, knob, gcyl, c + V3((-0.008, 0, 0)), c + V3((-0.026, 0, 0)), 0.012, 16); put(n, knob, gsph, c + V3((-0.026, 0, 0)), 0.012, 14, 6)
    RIGPTS[n] = tuple(c)
# wet compass on the glareshield (hood) top, offset right of the canopy centreline
cx, cy, cz = XP_R - 0.008, -0.12, 0.463
for c_, s_ in (((cx, cy, cz + 0.026), (0.06, 0.07, 0.006)), ((cx, cy, cz - 0.026), (0.06, 0.07, 0.006)), ((cx + 0.028, cy, cz), (0.006, 0.07, 0.058)),
               ((cx, cy + 0.032, cz), (0.06, 0.006, 0.058)), ((cx, cy - 0.032, cz), (0.06, 0.006, 0.058))):
    put(CK_INT, 'ck_bezel', gbox, c_, s_)
put(CK_INT, 'ck_bezel', gbox, (cx + 0.01, cy, cz - 0.034), (0.04, 0.03, 0.012))
put(CK_INT, 'ck_lubber', gbox, (cx - 0.0245, cy, cz), (0.001, 0.0016, 0.03))
bc = bmesh.new(); uv = bc.loops.layers.uv.verify(); N_ = 48; RC, HC = 0.022, 0.016
ra = [[bc.verts.new((cx - RC * math.cos(2 * math.pi * k / N_), cy + RC * math.sin(2 * math.pi * k / N_), cz + h)) for k in range(N_ + 1)] for h in (-HC / 2, HC / 2)]
for k in range(N_):
    f = bc.faces.new((ra[0][k], ra[0][k + 1], ra[1][k + 1], ra[1][k]))
    for lp, (u_, v_) in zip(f.loops, ((1 - k / N_, 0), (1 - (k + 1) / N_, 0), (1 - (k + 1) / N_, 1), (1 - k / N_, 1))): lp[uv].uv = (u_, v_)
for h, rr in ((0, ra[0][:N_]), (1, ra[1][:N_])): bc.faces.new(rr if h else rr[::-1])
CK['Compass_Card_R'] = [bc, ['compass']]; NEEDLES['Compass_Card_R'] = dict(c=(cx, cy, cz), key='COMPASS', grp='R')
# compact avionics stacked at the right edge of the rear panel, clear of the rear stick (Ty, 2026-10-30): TY91/TY96-style radio head over a TT21/TT22-
# style transponder head, each 63 W x 44 H mm (Trig datasheets), on a black mounting plate. Plain faces, no text; layout E.
AVY, AVZ = -0.258, 0.225; AVW, AVH = 0.063, 0.044
put(CK_INT, 'ck_plug', gbox, (XP_R - 0.001, AVY, AVZ), (0.003, AVW + 0.008, 2 * AVH + 0.010))
DISPLAYS = {}
for nm, zc, art, lcd, knobs in (('Radio', AVZ + AVH / 2 + 0.0005, 'avx_com', (150, 50, 480, 250), (('COMVOL', 75, 200, 0.0045, 0.012), ('COMOUT', 555, 200, 0.0075, 0.010), ('COMIN', 555, 200, 0.0048, 0.017))),
                                ('Xpdr', AVZ - AVH / 2 - 0.0005, 'avx_xpdr', (40, 50, 440, 220), (('XPKNOB', 540, 200, 0.0058, 0.012),))):
    put(CK_INT, art, texbox, XP_R - 0.0025, AVY, zc, AVW, AVH, 0.010)
    px = lambda u, v: (AVY + AVW / 2 - u / 630 * AVW, zc + AVH / 2 - v / 440 * AVH)
    (y0, z0), (y1, z1) = px(lcd[0], lcd[1]), px(lcd[2], lcd[3]); n = nm + '_Display_R'
    put(n, 'ck_lcd', texbox, XP_R - 0.0125, 0.5 * (y0 + y1), 0.5 * (z0 + z1), abs(y1 - y0) - 0.001, abs(z1 - z0) - 0.001, 0.0006)
    DISPLAYS[n] = dict(c=(XP_R - 0.0131, 0.5 * (y0 + y1), 0.5 * (z0 + z1)), size=(abs(y1 - y0) - 0.001, abs(z1 - z0) - 0.001))
    for hk, u, v, r, ln in knobs:
        y, z = px(u, v); n = {'COMVOL': 'Radio_VolumeKnob_R', 'COMOUT': 'Radio_TuneOuter_R', 'COMIN': 'Radio_TuneInner_R', 'XPKNOB': 'Xpdr_Knob_R'}[hk]
        x0 = XP_R - 0.0125; put(n, 'ck_grip', gcyl, (x0, y, z), (x0 - ln, y, z), r, 16)
        put(n, 'ck_buckle', segbox, (x0 - ln - 0.0004, y, z + 0.25 * r), (x0 - ln - 0.0004, y, z + 0.92 * r), 0.0012, 0.0008, (0, 1, 0))
        NEEDLES[n] = dict(c=(XP_R - 0.0125, y, z), key=hk, grp='R')
put(CK_INT, 'ck_placard', gbox, (XP_R - 0.001, -0.25, 0.130), (0.002, 0.05, 0.022))         # blank placard (no text)

# ---- fuel selector in each cockpit (POH): OFF / ACRO&CENTRE / WING, modelled on ACRO&CENTRE (E detent angles)
c = V3((XP_R, 0.0, -0.040)); put(CK_INT, 'dial_fuelsel', dial, XP_R, 0.0, -0.040, 0.030)
n = 'FuelSel_R'; put(n, 'ck_bezel', gcyl, c, c + V3((-0.010, 0, 0)), 0.008, 12)
put(n, 'ck_knob_red', segbox, c + V3((-0.014, 0, -0.012)), c + V3((-0.014, 0, 0.030)), 0.012, 0.008, (0, 1, 0)); RIGPTS[n] = tuple(c)
zl = _wz(-0.40); c = V3((-0.40, -0.34, zl))
put(CK_INT, 'ck_plug', gcyl, c, c + V3((0, 0, 0.004)), 0.030, 20); n = 'FuelSel_F'
put(n, 'ck_bezel', gcyl, c, c + V3((0, 0, 0.012)), 0.008, 12)
put(n, 'ck_knob_red', segbox, c + V3((-0.010, 0, 0.016)), c + V3((0.032, 0, 0.016)), 0.012, 0.008, (0, 0, 1)); RIGPTS[n] = tuple(c)

# ---- flight controls: two sticks (roll gimbal + pitch), four pedals with toe-brake plates ----------------------------
for sfx, x, h in (('F', -0.352, 0.442), ('R', -1.355, 0.462)):
    pv = V3((x, 0, -0.135)); n = 'StickRoll_' + sfx
    put(n, 'ck_tube', gcyl, pv + V3((0.03, 0, 0)), pv + V3((-0.03, 0, 0)), 0.014, 10); RIGPTS[n] = tuple(pv)
    n = 'Stick_' + sfx; tp = pv + V3((-0.055, 0, h))
    put(n, 'ck_tube', gcyl, pv + V3((0, -0.02, 0)), pv + V3((0, 0.02, 0)), 0.012, 10)
    put(n, 'ck_stick', gcyl, pv, V3((x - 0.045, 0, -0.135 + h - 0.09)), 0.010, 10)
    put(n, 'ck_grip', gcyl, V3((x - 0.045, 0, -0.135 + h - 0.10)), tp, 0.016, 12); put(n, 'ck_grip', gsph, tp, 0.016, 12, 6)
    put(n, 'ck_knob_red', gcyl, tp + V3((-0.010, 0, -0.025)), tp + V3((-0.020, 0, -0.025)), 0.005, 8)   # trigger (no function)
    RIGPTS[n] = tuple(pv); RIGPTS[n + '_tip'] = tuple(tp)
PED_TILT = math.radians(15)
for sfx, x0, yy in (('F', 0.22, 0.12), ('R', -0.75, 0.235)):
    for side, sy in (('L', 1), ('R', -1)):
        n = 'Pedal_%s_%s' % (sfx, side); h0 = V3((x0, sy * yy, ZF + 0.012)); up_ = V3((math.sin(PED_TILT), 0, math.cos(PED_TILT)))
        nrm = V3((-math.cos(PED_TILT), 0, math.sin(PED_TILT)))
        put(n, 'ck_tube', gcyl, h0 + V3((0, 0.045, 0)), h0 + V3((0, -0.045, 0)), 0.009, 8)
        for q in (0.04, -0.04): put(n, 'ck_pedal', gcyl, h0 + V3((0, q, 0)), h0 + V3((0, q, 0)) + 0.21 * up_, 0.007, 8)
        put(n, 'ck_pedal', gcyl, h0 + 0.21 * up_ + V3((0, 0.045, 0)), h0 + 0.21 * up_ + V3((0, -0.045, 0)), 0.007, 8)
        put(n, 'ck_pedal', gbox, h0 + 0.08 * up_ + 0.006 * nrm, (0.006, 0.080, 0.07), _frame(up_, (0, 1, 0)) @ Matrix.Rotation(math.pi / 2, 3, 'Y'))   # heel / ball plate
        RIGPTS[n] = tuple(h0)
        n = 'Brake_%s_%s' % (sfx, side); hb = h0 + 0.12 * up_ + 0.012 * nrm
        put(n, 'ck_tube', gcyl, hb + V3((0, 0.035, 0)), hb + V3((0, -0.035, 0)), 0.005, 8)
        put(n, 'ck_pedal', gbox, hb + 0.04 * up_ + 0.004 * nrm, (0.006, 0.075, 0.08), _frame(up_, (0, 1, 0)) @ Matrix.Rotation(math.pi / 2, 3, 'Y'))   # toe plate 0.12..0.20
        RIGPTS[n] = tuple(hb); RIGPTS[n + '_up'] = tuple(up_)

# ---- engine controls: throttle on the left ledge in each cockpit (POH); trim lever on the right ledge, rear only ------
LEV_L, LEV_A0 = 0.12, -25.0
for sfx, xq in (('F', -0.45), ('R', -1.30)):
    zq = _wz(xq); pv = V3((xq, 0.34, zq + 0.025))
    put(CK_INT, 'ck_quadrant', gbox, (xq + 0.01, 0.34, zq + 0.0125), (0.13, 0.04, 0.025))
    n = 'Throttle_' + sfx; a = math.radians(LEV_A0); d = V3((math.sin(a), 0, math.cos(a)))
    put(n, 'ck_quadrant', gcyl, pv + V3((0, -0.012, 0)), pv + V3((0, 0.012, 0)), 0.012, 10)
    put(n, 'ck_stick', segbox, pv, pv + LEV_L * d, 0.008, 0.012, (0, 1, 0))
    put(n, 'ck_grip', gsph, pv + (LEV_L + 0.01) * d, 0.016, 12, 8); RIGPTS[n] = tuple(pv); RIGPTS[n + '_tip'] = tuple(pv + LEV_L * d)
xq = -1.30; zq = _wz(xq); pv = V3((xq, -0.34, zq + 0.02))
put(CK_INT, 'ck_quadrant', gbox, (xq, -0.34, zq + 0.01), (0.10, 0.03, 0.02))
n = 'Trim_R'; put(n, 'ck_stick', segbox, pv, pv + V3((0, 0, 0.07)), 0.006, 0.010, (0, 1, 0))
put(n, 'ck_trim', gsph, pv + V3((0, 0, 0.078)), 0.012, 12, 8); RIGPTS[n] = tuple(pv); RIGPTS[n + '_tip'] = tuple(pv + V3((0, 0, 0.07)))
print('LEDGE tops', {x: round(_wz(x), 3) for x in (-0.40, -0.45, -1.30)}, 'skin hw', {x: round(hwf_(x), 3) for x in (0.22, -0.40, -0.75, -1.30)})
print('COCKPIT objects', len(CK), 'needles', len(NEEDLES), 'faces', len(CK[CK_INT][0].faces))
CK_TAGS = {}
for n, (bm_, tg) in CK.items():
    CK_TAGS[n] = [tg[f.material_index] for f in bm_.faces]; P[n] = link(n, bm_)
