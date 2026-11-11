"""The 2-33's front canopy is CLEAR all the way down to the fuselage (Ty t295u, 2026-11-02: "REMOVE the yellow band
across the 2-33 canopy near the front. The real canopy is clear all the way to the fuselage with no painted band or
frame strip"). Engine-free, from the committed GLBs, every 2-33 variant (factory and Talk Flying):

  1. Canopy_Frame has nothing ahead of its one bow (BOW_X), so no windscreen-arch bow and no forward sill rails.
     That bow was the door/canopy split at px 325 until 2026-11-10, when Ty moved it aft to abeam the wing root's
     leading edge (px 360); rule 1 moved with it, and is therefore stricter than it was;
  2. no OPAQUE (non-glass) geometry of any node lies on the front bubble: within 15 mm of the Canopy_Glass surface,
     ahead of FRONT_X (which did NOT move -- it is where the door begins, and a door is not a band), more than
     30 mm above the glass's lower edge AT THAT STATION (below that is the sill, where the glass meets the
     fuselage and the grey hinge/latch hardware sits; the old rails stood 35 mm). Exempt by name: the
     yaw string (red wool taped on the OUTSIDE of the glass, as on the real glider) and the panel's glare-shield
     coaming (black, INSIDE under the nose of the bubble; it is the panel, not a band across the glass). Hits spanning
     under 40 mm (a latch fitting at the sill) are reported as INFO, not failed: a band or strip is longer.

  python3 tools/own_models/check_sgs233_canopy.py   -> prints "SGS233 CANOPY OK" or "SGS233 CANOPY FAIL ..." (exit 1)
"""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import numpy as np
from vr_cockpit import Glb, builder, MODELS

VARIANTS = ['glider-2-33-like/glider-2-33-like-own.glb', 'glider-2-33-like/glider-2-33-like-own-tf.glb']
# TWO STATIONS, not one. They were the same number until 2026-11-10, when the canopy's one bow started moving,
# and collapsing them again would make this check assert something it was never written to assert.
BOW_X = 0.657            # builder x of the canopy's one bow (px 353), the frame the glass ends on and the rear
                         # door's forward frame -- one pair on the real aeroplane. It was 1.085, then 0.55 for a
                         # few hours, and is now the station the Commons three-view puts it at, with Ty's photo of
                         # N5711S agreeing to 19 mm. Rule 1 below -- Canopy_Frame carries nothing ahead of the bow
                         # -- is measured from here; the bow's 1.3 px half-width is 0.020 m, inside the slack.
FRONT_X = 1.085          # aft end of the FRONT BUBBLE, the glass over the front seat that rule 2 keeps clear of
                         # opaque geometry. It has stayed put through all three moves of BOW_X, because it is not
                         # the bow: it is where the front cockpit's glass ends and hardware is allowed. Policing
                         # the rear door, its hinges and latch, and the rear side glazing as "a band across the
                         # front canopy" is a different claim from the one in this file's title.
NEAR_M, EDGE_M = 0.015, 0.030
FITTING_M = 0.040   # hits spanning less than this are a fitting (INFO), not a band
EXEMPT = ('YawString', 'Yaw_String', 'Panel_Coaming')


def geo(g, W, i):
    n = g.doc['nodes'][i]; out = []
    for p in g.doc['meshes'][n['mesh']]['primitives']:
        v = g.array(p['attributes']['POSITION']); m = W[i]; v = v @ m[:3, :3].T + m[:3, 3]
        v = np.array([builder(q) for q in v])
        idx = g.array(p['indices']).reshape(-1, 3) if 'indices' in p else np.arange(len(v)).reshape(-1, 3)
        mat = g.doc['materials'][p['material']] if 'material' in p else {}
        out.append((v, idx, mat))
    return out


def glassy(name, mat):
    s = (name + ' ' + mat.get('name', '')).lower()
    return 'glass' in s or mat.get('alphaMode') == 'BLEND'


def check(rel):
    g = Glb(MODELS / rel); W = g.world_matrices(); names = [n['name'] for n in g.doc['nodes']]
    problems = []
    fi = names.index('Canopy_Frame'); gi = names.index('Canopy_Glass')
    fv = np.concatenate([v for v, _, _ in geo(g, W, fi)])
    ahead = fv[fv[:, 0] > BOW_X + 0.025]
    if len(ahead): problems.append(f'Canopy_Frame has {len(ahead)} vertices ahead of the bow (x up to {ahead[:, 0].max():.3f})')
    # the glass surface, sampled densely (barycentric grid) on its front part
    pts = []
    for v, idx, _ in geo(g, W, gi):
        t = v[idx]; t = t[t[:, :, 0].max(1) > FRONT_X]
        for a in np.linspace(0, 1, 9):
            for b in np.linspace(0, 1 - a, max(2, int(9 * (1 - a)))):
                pts.append(t[:, 0] * (1 - a - b) + t[:, 1] * a + t[:, 2] * b)
    S = np.concatenate(pts)
    def zlow(x):   # the glass's lower edge at each station (±30 mm)
        return np.array([S[np.abs(S[:, 0] - q) < 0.03, 2].min() for q in x])
    gx0, gx1 = FRONT_X + 0.025, S[:, 0].max()
    hits = {}
    for i, n in enumerate(g.doc['nodes']):
        if n.get('mesh') is None or i == gi or n['name'].startswith(EXEMPT): continue
        for v, idx, mat in geo(g, W, i):
            if glassy(n['name'], mat): continue
            c = v[(v[:, 0] > gx0) & (v[:, 0] < gx1)]
            if len(c): c = c[c[:, 2] > zlow(c[:, 0]) + EDGE_M]
            if not len(c): continue
            for k in range(0, len(c), 2000):
                d = np.sqrt(((c[k:k + 2000, None, :] - S[None, :, :]) ** 2).sum(-1)).min(1)
                bad = c[k:k + 2000][d < NEAR_M]
                if len(bad): hits.setdefault(f"{n['name']} [{mat.get('name', '?')}]", []).append(bad)
    for k, b in hits.items():
        b = np.concatenate(b)
        if np.ptp(b, axis=0).max() < FITTING_M:   # a small fitting (a latch), not a band or a strip
            print(f'SGS233 CANOPY INFO {rel}: {k}: a {np.ptp(b, axis=0).max() * 1000:.0f} mm fitting at x {b[:, 0].mean():.3f}, z {b[:, 2].mean():.3f}')
            continue
        problems.append(f'{k}: {len(b)} opaque vertices on the front glass, x {b[:, 0].min():.3f}..{b[:, 0].max():.3f}, z {b[:, 2].min():.3f}..{b[:, 2].max():.3f}')
    return problems, len(S)


if __name__ == '__main__':
    fails = 0
    for rel in VARIANTS:
        p, n = check(rel)
        for m in p: print(f'SGS233 CANOPY FAIL {rel}: {m}')
        if not p: print(f'SGS233 CANOPY PASS {rel}: front bubble clear to the fuselage ({n} glass samples)')
        fails += len(p)
    print('SGS233 CANOPY OK' if fails == 0 else f'SGS233 CANOPY PROBLEMS {fails}')
    sys.exit(1 if fails else 0)
