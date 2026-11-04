"""Great Lakes windshields v2: one frameless curved plexi sheet per cockpit, seated on the
turtle deck AHEAD of each opening, with only a thin polished-aluminium base strip + screws.
Replaces the P3c/P3d half-ellipse screens (which wrapped half way down the opening sides).

Shapes from the Great Lakes Aircraft Parts windshields (estimates, not surveyed):
 W-2591 front: 21"W x 13"H x 11"D, low, flatter top.  W-2592 rear "tall": 26"W x 13"H x 11"D.
 .187" acrylic, frameless; screws only along the base.
Keeps node indices: replaces meshes on Windscreen_Front/Rear and renames
Windscreen_Base_Screws_<Seat> -> GL_Windshield_Base_Trim_<Seat> (so Unity does not class the
metal strip as glass). Cabane, gear and coaming work from vr_phase3c is untouched.
Run: python3 gl_windshield_v2.py   (idempotent: rebuilds from the same parameters)
"""
import numpy as np
from vr_cockpit import Glb, MODELS, Shape, gl, builder
from vr_phase3c import vertices, ray_skin
from drop_card_slot import validate

THICK = .0047   # 3/16 in
# seat: half width, centre forward sweep, centre height, side height, top superellipse exponent,
#       aft lean (horizontal per unit height) centre/sides, side tuck-in, vertical bulge
SPEC = dict(
    Front=dict(top=.370, w2=.225, d=.22, hc=.150, hs=.080, n=4.0, lean_c=.62, lean_s=.12, tuck=.10, bulge=.008),
    Rear=dict(top=.395, w2=.250, d=.25, hc=.185, hs=.055, n=2.2, lean_c=.52, lean_s=.10, tuck=.12, bulge=.012))
NU, NV = 97, 25
TRIM_H, TRIM_T = .014, .0015

def smooth_normals(v, t):
    v = np.asarray(v); t = np.asarray(t); n = np.zeros_like(v)
    fn = np.cross(v[t[:, 1]] - v[t[:, 0]], v[t[:, 2]] - v[t[:, 0]])
    for k in range(3): np.add.at(n, t[:, k], fn)
    return n / np.maximum(np.linalg.norm(n, axis=1)[:, None], 1e-12)

def put(g, name, sh, mat, smooth=False):
    """Replace node `name`'s mesh (node already at origin, identity). Optional smooth normals."""
    i = next(i for i, n in enumerate(g.doc['nodes']) if n['name'] == name)
    w = g.world_matrices()[i]; assert np.allclose(w, np.eye(4), atol=1e-7), (name, w)
    v = [gl(p) for p in sh.v]; g.mesh(name, v, sh.t, mat)
    if smooth:
        prim = g.doc['meshes'][g.doc['nodes'][i]['mesh']]['primitives'][0]
        nrm = smooth_normals(v, sh.t)[np.asarray(sh.t)].reshape(-1, 3)
        prim['attributes']['NORMAL'] = g.accessor(nrm, 'VEC3')

def sheet(skin, xf, s, ahead=None):
    u = np.linspace(-1, 1, NU); vv = np.linspace(0, 1, NV)
    xside = xf + .015
    # Stay on the deck: never sweep into the cockpit opening ahead (legacy liveries are closer).
    d = s['d'] if ahead is None else min(s['d'], ahead - xside - .03)
    bx = xside + d * (1 - np.abs(u) ** 2.2); by = s['w2'] * u
    bz = np.array([ray_skin(skin, float(x), float(y)) + .004 for x, y in zip(bx, by)])
    # Outward horizontal normal of the base curve (forward at the centre).
    tx = np.gradient(bx); ty = np.gradient(by); on = np.stack([ty, -tx], 1) * np.sign(u + 1e-9)[:, None]
    on[np.abs(u) < 1e-9] = (1, 0); on = on * np.where(on[:, :1] < 0, -1, 1)
    on /= np.linalg.norm(on, axis=1)[:, None]
    # Absolute top height (front a little lower than rear on every livery/deck variant).
    hc = s['top'] - bz[NU // 2]; hs = s['hs'] * hc / s['hc']
    h = hs + (hc - hs) * (1 - np.abs(u) ** s['n']) ** (1 / s['n'])
    lean = s['lean_s'] + (s['lean_c'] - s['lean_s']) * (1 - u ** 2)
    P = np.zeros((NV, NU, 3))
    for j, v in enumerate(vv):
        z = v * h; b = s['bulge'] * np.sin(np.pi * v)
        P[j, :, 0] = bx - lean * z + b * on[:, 0]
        P[j, :, 1] = by - s['tuck'] * z * u + b * on[:, 1]
        P[j, :, 2] = bz + z
    return P, on

def solid(P):
    """Closed slab: inner surface P, outer = P + THICK * normal, edges stitched (one material)."""
    du = np.gradient(P, axis=1); dv = np.gradient(P, axis=0); n = np.cross(dv, du)
    n /= np.linalg.norm(n, axis=2)[:, :, None]
    if n[0, NU // 2, 0] < 0: n = -n                   # outward = forward
    O = P + THICK * n; R, C = P.shape[:2]; cnt = R * C
    sh = Shape(); sh.v = P.reshape(-1, 3).tolist() + O.reshape(-1, 3).tolist()
    for j in range(R - 1):
        for i in range(C - 1):
            k = j * C + i
            sh.t += [(k, k + C, k + 1), (k + 1, k + C, k + C + 1)]                       # inner
            sh.t += [(cnt + k, cnt + k + 1, cnt + k + C), (cnt + k + 1, cnt + k + C + 1, cnt + k + C)]
    ring = list(range(C)) + [j * C + C - 1 for j in range(1, R)] + \
        [(R - 1) * C + i for i in range(C - 2, -1, -1)] + [j * C for j in range(R - 2, 0, -1)]
    for a, b in zip(ring, ring[1:] + ring[:1]):
        sh.t += [(a, b, b + cnt), (a, b + cnt, a + cnt)]
    return sh, O, n

def trim(O, n, on):
    """Thin polished strip hugging the outer base of the glass + 9 pan-head screws."""
    C = O.shape[1]; base = O[0]; nb = n[0]
    up = O[1] - O[0]; up /= np.linalg.norm(up, axis=1)[:, None]
    rows = []
    for off in (.0004, .0004 + TRIM_T):
        for hgt in (0, TRIM_H):
            rows.append(base + nb * off + up * hgt)
    sh = Shape(); sh.v = np.concatenate(rows).tolist()
    # rows: 0 inner-bottom, 1 inner-top, 2 outer-bottom, 3 outer-top; quads between ring order 0-1-3-2
    loop = [0, 1, 3, 2]
    for a, b in zip(loop, loop[1:] + loop[:1]):
        for i in range(C - 1):
            p, q = a * C + i, b * C + i
            sh.t += [(p, q, q + 1), (p, q + 1, p + 1)]
    for i, sg in ((0, 1), (C - 1, -1)):
        f = [r * C + i for r in loop]; sh.t += [tuple(f[:3]), (f[0], f[2], f[3])]
    for i in np.linspace(4, C - 5, 9).round().astype(int):
        p = base[i] + nb[i] * (.0004 + TRIM_T) + up[i] * TRIM_H / 2
        sh.tube(p, p + nb[i] * .0012, .0028, 10)
    return sh

def update(path):
    g = Glb(path); meta = g.doc['extras']['vrCockpitPhase3c']; skin = vertices(g, 'Fuselage_Painted')
    names = {n['name'] for n in g.doc['nodes']}
    glass = next(i for i, m in enumerate(g.doc['materials']) if m['name'] == 'GL3c_Clear_Acrylic')
    gm = g.doc['materials'][glass]; gm['pbrMetallicRoughness'].update(baseColorFactor=[.95, .985, 1, .22], roughnessFactor=.04)
    alu = g.material('GL_Windshield_Trim_Aluminium', (.80, .82, .85))
    g.doc['materials'][alu]['pbrMetallicRoughness'].update(metallicFactor=.95, roughnessFactor=.28)
    screens = []
    for seat, ck in zip(('Front', 'Rear'), meta['cockpits']):
        s = SPEC[seat]; xf = ck[0]
        P, on = sheet(skin, xf, s, None if seat == 'Front' else meta['cockpits'][0][1]); sh, O, n = solid(P)
        put(g, 'Windscreen_' + seat, sh, glass, smooth=True)
        old, new = 'Windscreen_Base_Screws_' + seat, 'GL_Windshield_Base_Trim_' + seat
        if old in names:
            nd = next(x for x in g.doc['nodes'] if x['name'] == old); nd['name'] = new
            meta['new_nodes'] = [new if x == old else x for x in meta['new_nodes']]
        put(g, new, trim(O, n, on), alu)
        allv = np.concatenate([P.reshape(-1, 3), O.reshape(-1, 3)])
        c = NU // 2
        screens.append(dict(node='Windscreen_' + seat, trim_node=new, revision='v2', rows=NV, columns=NU,
            thickness_m=THICK, single_sheet=True, frame=False, edge_material='same as glass',
            base_side_x_m=float(P[0, 0, 0]), base_centre_x_m=float(P[0, c, 0]),
            base_forward_distance_m=float(P[0, c, 0] - xf), half_width_m=s['w2'],
            top_height_m=float(allv[:, 2].max()), centre_height_above_skin_m=float(P[-1, c, 2] - P[0, c, 2]),
            centre_rake_deg_from_horizontal=float(np.degrees(np.arctan2(P[-1, c, 2] - P[0, c, 2], P[0, c, 0] - P[-1, c, 0]))),
            top_exponent=s['n'], trim_height_m=TRIM_H, trim_thickness_m=TRIM_T, screws=9))
    meta['screens'] = screens; meta['revision'] = 'P3d+windshield-v2'
    validate(g, path); g.save(path); print('windshield v2', path.name, [(x['node'], round(x['top_height_m'], 3), round(x['centre_rake_deg_from_horizontal'], 1)) for x in screens])

if __name__ == '__main__':
    for p in sorted((MODELS / 'great-lakes-like').glob('*own*.glb')): update(p)
