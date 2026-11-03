"""The J-3's DRAWN EDO 60-1320 floats, as the game places them, cut into the station table the flight model floats on.

Ty 2026-11-02: "the boyancy and planing on step all seem weak do a deep dive on the hydrodynamics" -- and the measured
mismatch (lane/claude-vr-quest ce62f612): the physics float block was a different float from the one drawn off Piper/EDO
dwg 60-S-908A (0.317 m outboard, 16-22 cm deeper at bow and stern). The flight model now floats on THESE lines.

What it reads: Float_L in j3-cub-floats-like-own.glb, placed exactly as AirframeModels.Place places it
(sights.placement, including Spec.SitHighM). Each hull ring of the loft (build_j3_cub.py F_STA / A_STA: deck,
side, chine, V-bottom flute, keel, mirrored) becomes one station: x, then the half-section points (y >= 0 measured
from the float's own centreline) from the deck down to the keel, z DOWN. Printed in the CONFIG DATUM frame (the
placement frame is the aeroplane's root, which sits at the CG: config = placed + cg), ready for
configs/aircraft/j3-cub-floats-like.json floats.stations, and with the float centreline (spread / 2), the step x
and the lowest point of the drawn float (which is what the float-keel contact points must sit on).

  /Applications/Blender.app/Contents/Resources/5.2/python/bin/python3.13 -I tools/own_models/j3_float_stations.py
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
try:
    import PIL  # noqa: F401
except ImportError:
    import types
    sys.modules['PIL'] = types.ModuleType('PIL'); sys.modules['PIL'].Image = None
import numpy as np
from glb_visuals import Glb
from sights import parts, placement, MODELS, ROOT

AIRCRAFT = 'j3-cub-floats-like'


def rings(v, yc):
    """Hull rings: vertex groups at one x with the loft's five half-section points (deck, side, chine, flute, keel)."""
    out = []
    xs = np.round(v[:, 0], 4)
    for x in np.unique(xs):
        g = v[xs == x]
        half = g[g[:, 1] - yc >= -1e-4]
        # the loft ring: exactly one point on the centreline (the keel) and four off it, for a closed V section
        on = half[np.abs(half[:, 1] - yc) < 1e-4]
        off = half[np.abs(half[:, 1] - yc) >= 0.03]          # (keel plate verts 15 mm, transom rudder bracket 25 mm off the centreline)
        if len(on) < 1 or len(off) < 4: continue
        # Deck hardware (hand-hole covers, cleats, strut fittings) sits on or just above the deck, inboard of its edge:
        # at deck level keep only the outermost point, which is the hull ring's deck edge.
        zdeck = off[:, 2].min()
        lvl = off[off[:, 2] <= zdeck + 0.02]
        edge = lvl[np.argmax(np.abs(lvl[:, 1] - yc))]
        off = np.vstack([off[off[:, 2] > zdeck + 0.02], edge[None, :]])
        keel = on[np.argmax(on[:, 2])]
        pts = sorted({(round(float(p[1] - yc), 4), round(float(p[2]), 4)) for p in off}, key=lambda q: q[1])
        if len(pts) != 4:
            print(f'skipped x {x:+.4f}: {len(pts)} points', file=sys.stderr); continue
        out.append((float(x), pts + [(0.0, round(float(keel[2]), 4))]))
    return out


def main():
    g = Glb(MODELS / AIRCRAFT / f'{AIRCRAFT}-own.glb')
    m, tr = placement(g, AIRCRAFT)
    cfg = json.loads((ROOT / 'configs/aircraft' / f'{AIRCRAFT}.json').read_text())
    cg = np.array(cfg['mass']['cg'], dtype=float)
    hull = [(a @ m.T + tr)[ids].reshape(-1, 3) for name, p, a, ids in parts(g) if name == 'Float_L']
    v = np.concatenate(hull)
    yc = float(np.median(v[np.abs(v[:, 2] - v[:, 2].max()) < 0.02][:, 1]))   # the keel line's y = the float centreline
    rs = rings(v, yc)
    # The last few bow rings are too small for the hardware filter (their flute sits inside 3 cm of the centreline);
    # close the hull with the bow tip itself, a zero-width station at the drawn float's foremost point.
    tip = v[np.argmax(v[:, 0])]
    rs.append((float(tip[0]), [(0.0, round(float(tip[2]), 4))] * 5))
    rs.sort(key=lambda r: r[0])
    # The step: the one x where two rings sit within 6 mm (the forebody's last and the afterbody's first).
    xs = [x for x, _ in rs]
    step = None
    for a, b in zip(xs, xs[1:]):
        if b - a < 0.006: step = 0.5 * (a + b)
    stations = [[round(x + cg[0], 4)] + [c for y, z in pts for c in (y, round(z + cg[2], 4))] for x, pts in rs]
    stations.sort(key=lambda s: -s[0])   # bow first
    lowest = float(v[:, 2].max() + cg[2])
    # The water rudder blade (LH float only), when down: its x centre, span (top to tip) and tip z.
    bl = [(a @ m.T + tr)[ids].reshape(-1, 3) for name, p, a, ids in parts(g) if name == 'WaterRudder_Blade_L']
    rudder = None
    if bl:
        b = np.concatenate(bl) + cg
        rudder = dict(x=round(float(0.5 * (b[:, 0].min() + b[:, 0].max())), 4), spanM=round(float(np.ptp(b[:, 2])), 4),
                      tipZ=round(float(b[:, 2].max()), 4), chordM=round(float(np.ptp(b[:, 0])), 4), y=round(float(b[:, 1].mean()), 4))
    print(json.dumps(dict(centreY=round(yc + cg[1], 4), stepX=round(step + cg[0], 4) if step is not None else None,
                          lowestZ=round(lowest, 4), waterRudder=rudder, stations=stations)))


if __name__ == '__main__':
    main()
