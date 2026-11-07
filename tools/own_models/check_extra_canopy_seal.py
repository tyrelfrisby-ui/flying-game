"""Does the Extra's canopy seal to the airframe, as the pilot sees it? (Ty 2026-11-05 ADDENDUM: "the front-seat view shows
a GAP between the canopy and the airplane. Close it ... verified from the front-seat eye point.")

Two measurements, both seats, all three liveries; exit 1 if either fails:
  RIM   every point along the canopy glass's free edge lies within RIM_MAX_MM of the sill, the frame or the skin.
  VIEW  from each seat's eye, no ray reaches exterior livery (a Paint_ea_* / Paint_white surface) without first passing
        through glass. Through the canopy that paint is the outside of the aeroplane, which is fine; WITHOUT glass in
        between it is the inside of a double-sided skin, or the wing running through the cockpit, which is what read
        as a white-and-red gap under the canopy (fixed by extra_cockpit_v3.py: side liners, firewall plate, the wing
        clipped at the fuselage).
Mirrored in tools/FlightTests/ExtraCanopySealTests.cs.

  PYTHONDONTWRITEBYTECODE=1 /usr/bin/python3 tools/own_models/check_extra_canopy_seal.py [--step DEG]
"""
import math
import sys
import numpy as np

from vr_cockpit import Glb, MODELS
from sights import parts

EYES = {'rear': np.array((-1.622, 0.0, .526)), 'front': np.array((-.703, 0.0, .537))}   # builder, as placed
RIM_MAX_MM = 3.0
FLOOR_Z = -.15        # VIEW gates what is above the cockpit floor; below it (the floor windows' wells) is reported only
LIVERY = ('Paint_ea', 'Paint_white')
GLASS = ('Canopy_Glass', 'Window_Glass', 'Floor_Window_Glass')
HOST = ('Canopy_Sill', 'Canopy_Frame', 'Fuselage_Painted')


def load(path):
    g = Glb(path); mats = g.doc['materials']; T, own, mat = [], [], []
    for name, p, a, ids in parts(g):
        if name.startswith(('Legacy', 'Sight')): continue
        b = np.stack([-a[:, 0], a[:, 2], a[:, 1]], 1)[ids]; c = b.mean(1)
        keep = (np.abs(c[:, 1]) < .9) & (c[:, 0] > -2.6) & (c[:, 0] < 1.2) & (c[:, 2] > -.8)
        T.append(b[keep]); own += [name] * int(keep.sum()); mat += [mats[p['material']]['name'] if 'material' in p else ''] * int(keep.sum())
    return np.concatenate(T), np.array(own), np.array(mat)


def rim_gaps(T, own):
    glass = T[own == 'Canopy_Glass']; host = T[np.isin(own, HOST)]
    key = {}; rid = np.zeros(glass.shape[:2], int)
    for i, t in enumerate(glass):
        for j, v in enumerate(t): rid[i, j] = key.setdefault(tuple(np.round(v, 5)), len(key))
    cnt = {}
    for t in rid:
        for a, b in ((0, 1), (1, 2), (2, 0)): e = (min(t[a], t[b]), max(t[a], t[b])); cnt[e] = cnt.get(e, 0) + 1
    P = {v: np.array(k) for k, v in key.items()}
    pts = [P[a] * (1 - s) + P[b] * s for (a, b), c in cnt.items() if c == 1 for s in (0, .25, .5, .75)]
    from scipy.spatial import cKDTree
    s = np.linspace(0, 1, 9); bary = [(u, v) for u in s for v in s if u + v <= 1]
    S = np.concatenate([host[:, 0] + u * (host[:, 1] - host[:, 0]) + v * (host[:, 2] - host[:, 0]) for u, v in bary])
    d, _ = cKDTree(S).query(np.array(pts))
    return np.array(pts), d


def leaks(T, own, mat, eye, step):
    e1 = T[:, 1] - T[:, 0]; e2 = T[:, 2] - T[:, 0]
    glass = np.isin(own, GLASS); liv = np.array([m.startswith(LIVERY) for m in mat])
    out = []
    for el in np.arange(-60, 20.1, step):
        for az in np.arange(-180, 180, step):
            a, b = math.radians(az), math.radians(el)
            d = np.array((math.cos(b) * math.cos(a), math.cos(b) * math.sin(a), math.sin(b)))
            h = np.cross(d, e2); det = (e1 * h).sum(1); inv = np.divide(1, det, out=np.zeros_like(det), where=abs(det) > 1e-12)
            s = eye - T[:, 0]; u = (s * h).sum(1) * inv; q = np.cross(s, e1); v = (d * q).sum(1) * inv; t = (e2 * q).sum(1) * inv
            ok = (abs(det) > 1e-12) & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 1e-6)
            solid = ok & ~glass
            if not solid.any(): continue
            k = np.where(solid)[0][np.argmin(t[solid])]
            if liv[k] and not (ok & glass & (t < t[k])).any(): out.append((el, az, own[k], mat[k], eye + t[k] * d))
    return out


def main():
    step = float(sys.argv[sys.argv.index('--step') + 1]) if '--step' in sys.argv else 2.0
    bad = []
    for path in sorted((MODELS / 'extra-300-like').glob('*own*.glb')):
        T, own, mat = load(path)
        pts, d = rim_gaps(T, own)
        k = int(np.argmax(d))
        print(f'{path.name}: RIM {len(pts)} samples, max gap {d.max() * 1000:.1f} mm at {np.round(pts[k], 3).tolist()}')
        if d.max() * 1000 > RIM_MAX_MM: bad.append((path.name, 'rim', round(float(d.max()) * 1000, 1)))
        for seat, eye in EYES.items():
            A = leaks(T, own, mat, eye, step); L = [r for r in A if r[4][2] > FLOOR_Z]
            print(f'  {seat:5s} eye: {len(L)} rays reach exterior livery without glass above the floor'
                  f' ({len(A) - len(L)} below it, through the floor wells: not gated)' +
                  ''.join(f'\n     el {el:+.0f} az {az:+.0f}: {o} / {m} at {np.round(p, 3).tolist()}' for el, az, o, m, p in L[:8]))
            if L: bad.append((path.name, seat, len(L)))
    print('CANOPY SEAL: ' + ('PASS' if not bad else f'FAIL {bad}'))
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
