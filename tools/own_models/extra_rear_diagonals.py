"""Extra 300 cockpit pass 16 (iOS/Mac and Quest, one GLB): the rear seat's padded diagonals. Ty 2026-11-05 08:37:

  "the diagonal tubes in the rear seat are in the same plane as the upper longerons they do not go up the go in"
  "toward the center"

  Pass 13 (extra_front_seat.DIAG) ran Tube_P3h_PanelDiagonal_L/_R from the side rail at z 0.20 UP to the panel's upper
  corners at z 0.345: seen from the rear eye they climbed toward the canopy. They now lie in the plane of the upper
  longerons -- the canopy sills, which carry the rear cockpit's upper longerons (z ~0.27 here; the plane through the
  left and right sill lines is z = z_sill(x), level across) -- and run forward and INBOARD: from the inboard face of
  each sill at x -1.55 to the panel face at |y| 0.272, just outboard of the outermost instrument bezels (|y| 0.258,
  the oil gauge), so no gauge is covered. Each end's height is the sill's measured centre at that station.
  Moving the tube uncovered sill faces still in exterior paint: the rails' visible inner faces are repainted again.

Runs after extra_cockpit_v3.py (pass 15), in place on all three liveries. No rig change.
Idempotent (extras.extraRearDiagonals). Builder metres, x forward, y LEFT, z up.

  PYTHONDONTWRITEBYTECODE=1 /usr/bin/python3 tools/own_models/extra_rear_diagonals.py
"""
import json
import numpy as np

from vr_cockpit import Glb, Shape, MODELS
from vr_phase2e import compact
from extra_cockpit_shared import mat_index
from extra_front_seat import set_parts
from extra_panel_p3f import idx, tris_of
from extra_cockpit_v3 import world_tris, section, EYE
from drop_card_slot import validate

R = .013                       # the pass-13 tube radius (padded)
X_AFT, X_FWD = -1.55, -1.182    # aft end on the sill; forward end against the panel's aft face (x -1.175)
Y_FWD = .272                    # forward end's |y|: outboard of the oil gauge's bezel (|y| 0.258) by more than R
# Where the pass-13 tubes stood, as the rear eye sees it (|az| 38-54 deg, el -38..-17 deg), with a margin: 0.5 deg rays.
UNCOVER_AZ, UNCOVER_EL, UNCOVER_STEP = (25., 70.), (-50., -5.), .5
BOX = dict(x=(-1.70, -.95), z=(-.10, .65))     # every triangle a ray from the rear eye can meet in that window


def sill_at(sill, x, side):
    """The sill's cut at station x on one side: (inboard |y|, centre z)."""
    P = np.array([q for s in section(sill, x, side) for q in s])
    return float(np.abs(P[:, 0]).min()), float((P[:, 1].min() + P[:, 1].max()) / 2)


def first_hits(o, D, T):
    """Möller-Trumbore, rays o + t D against triangles T: (index of the nearest hit or -1, its t)."""
    e1 = T[:, 1] - T[:, 0]; e2 = T[:, 2] - T[:, 0]; s = o - T[:, 0]; q = np.cross(s, e1)
    best = np.full(len(D), -1); tb = np.full(len(D), np.inf)
    for k0 in range(0, len(D), 256):
        d = D[k0:k0 + 256][:, None, :]
        h = np.cross(d, e2[None]); det = (e1[None] * h).sum(2)
        ok = np.abs(det) > 1e-12; inv = np.where(ok, 1 / np.where(ok, det, 1), 0)
        u = (s[None] * h).sum(2) * inv; v = (d * q[None]).sum(2) * inv; t = (e2[None] * q[None]).sum(2) * inv
        t = np.where(ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 1e-6), t, np.inf)
        j = t.argmin(1); tb[k0:k0 + 256] = t[np.arange(len(j)), j]; best[k0:k0 + 256] = np.where(np.isfinite(tb[k0:k0 + 256]), j, -1)
    return best, tb


def repaint_uncovered(g, cabin):
    """Rail faces (Canopy_Sill / _Frame, double-sided in the exterior livery) the rear eye now sees directly -- no glass
    nearer -- where the pass-13 tube used to stand in front of them: cockpit paint, as pass 15 did for the rest."""
    W = g.world_matrices(); eye = EYE['Rear']; T = []; tag = []          # tag: (node, prim, tri) for rail livery, else None
    glass = []
    for i, n in enumerate(g.doc['nodes']):
        nm = n.get('name', '')
        if 'mesh' not in n or nm.startswith(('Legacy', 'Sight')): continue
        M = W[i]
        for pi, p in enumerate(g.doc['meshes'][n['mesh']]['primitives']):
            v = np.asarray(g.array(p['attributes']['POSITION']), float) @ M[:3, :3].T + M[:3, 3]; b = np.stack([-v[:, 0], v[:, 2], v[:, 1]], 1)
            ids = np.asarray(g.array(p['indices'])).reshape(-1, 3) if 'indices' in p else np.arange(len(b)).reshape(-1, 3)
            t = b[ids]; c = t.mean(1)
            keep = np.where((c[:, 0] > BOX['x'][0]) & (c[:, 0] < BOX['x'][1]) & (c[:, 2] > BOX['z'][0]) & (c[:, 2] < BOX['z'][1]))[0]
            mat = g.doc['materials'][p['material']]['name'] if 'material' in p else ''
            rail = nm in ('Canopy_Sill', 'Canopy_Frame') and mat.startswith(('Paint_ea', 'Paint_white'))
            for k in keep:
                T.append(t[k]); tag.append((i, pi, k) if rail else None); glass.append(nm in ('Canopy_Glass', 'Window_Glass', 'Floor_Window_Glass'))
    T = np.array(T); glass = np.array(glass)
    D = []
    for el in np.arange(UNCOVER_EL[0], UNCOVER_EL[1] + 1e-9, UNCOVER_STEP):
        for az in np.arange(UNCOVER_AZ[0], UNCOVER_AZ[1] + 1e-9, UNCOVER_STEP):
            for sgn in (1, -1):
                a, b = np.radians(sgn * az), np.radians(el); D.append((np.cos(b) * np.cos(a), np.cos(b) * np.sin(a), np.sin(b)))
    D = np.array(D)
    js, ts = first_hits(eye, D, T[~glass]); jg, tg = first_hits(eye, D, T[glass])
    solid_ids = np.where(~glass)[0]; hit = set()
    for j, t, t2 in zip(js, ts, tg):
        if j >= 0 and t < t2 and tag[solid_ids[j]] is not None: hit.add(tag[solid_ids[j]])
    moved = {}
    for (i, pi) in sorted({(a, b) for a, b, _ in hit}):
        mesh = g.doc['meshes'][g.doc['nodes'][i]['mesh']]; p = mesh['primitives'][pi]
        ids = np.asarray(g.array(p['indices'])).reshape(-1, 3) if 'indices' in p else np.arange(g.doc['accessors'][p['attributes']['POSITION']]['count']).reshape(-1, 3)
        seen = np.zeros(len(ids), bool); seen[[k for a, b, k in hit if (a, b) == (i, pi)]] = True
        new = []
        for keep, mat in ((~seen, p['material']), (seen, cabin)):
            kept = ids[keep]; used = np.unique(kept); remap = -np.ones(ids.max() + 1, int); remap[used] = np.arange(len(used))
            at = {k: g.accessor(np.asarray(g.array(a))[used].astype(np.float32), g.doc['accessors'][a]['type']) for k, a in p['attributes'].items()}
            new.append(dict(attributes=at, indices=g.accessor(remap[kept].reshape(-1).astype(np.uint32), 'SCALAR', 5125), material=mat, mode=4))
        mesh['primitives'][pi] = new[0]; mesh['primitives'].append(new[1])
        nm = g.doc['nodes'][i]['name']; moved[nm] = moved.get(nm, 0) + int(seen.sum())
    return moved, len(D)


def update(path):
    g = Glb(path)
    if g.doc['extras'].get('extraRearDiagonals'): print('EXTRA', path.name, 'already done'); return
    assert g.doc['extras'].get('extraCockpitV3'), path
    sill = world_tris(g, 'Canopy_Sill'); pad = mat_index(g, 'P3h_Padding'); report = {}
    for side, tag in ((1, 'L'), (-1, 'R')):
        ya, za = sill_at(sill, X_AFT, side); _, zf = sill_at(sill, X_FWD, side)
        a = np.array((X_AFT, side * (ya - R), za)); f = np.array((X_FWD, side * Y_FWD, zf))
        sh = Shape(); sh.tube(a, f, R, 16); sh.sphere(a, R, 16, 8); sh.sphere(f, R, 16, 8)
        set_parts(g, 'Tube_P3h_PanelDiagonal_' + tag, [(pad, sh, None)])
        report[tag] = dict(aft=np.round(a, 4).tolist(), fwd=np.round(f, 4).tolist(), sill_inboard_y_aft=round(ya, 4))
    # The pass-13 tube stood in front of part of the sill's inboard face, which is double-sided with the exterior livery
    # and was never repainted (ExtraCanopySealTests: the rear eye saw Paint_ea_side_r at (-1.273, 0.349, 0.275) once the
    # tube moved). Only faces a ray from the rear eye reaches with no glass nearer, in the window the old tube covered:
    # a wider repaint also caught outside faces of the frame through seams (168 of 367 on a first try, 2026-11-05).
    report['rail_faces_to_cockpit_paint'], report['uncover_rays'] = repaint_uncovered(g, mat_index(g, 'Paint_cabin'))
    g.doc['extras']['extraRearDiagonals'] = dict(
        owner="Ty 2026-11-05 08:37: rear diagonals in the plane of the upper longerons, running in toward the centre",
        radius=R, x_aft=X_AFT, x_fwd=X_FWD, y_fwd=Y_FWD, report=report)
    compact(g); validate(g, path); g.save(path)
    print('EXTRA', path.name, json.dumps(report))


if __name__ == '__main__':
    for p in sorted((MODELS / 'extra-300-like').glob('*own*.glb')):
        update(p)
