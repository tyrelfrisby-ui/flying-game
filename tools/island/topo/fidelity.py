"""How true each real-topography area stayed after the generator put it under the island's constraints.

For every area, over the land of its template window (feathered edge excluded: the inner window shrunk by the
0.6 km feather), compares the compacted real surface (the template, which is the real DEM scaled by c both
ways, so its slopes ARE the real slopes) with the island's final relief (IslandRelief.Generated.cs):
  * slope distribution p25 / p50 / p75 / p90 (deg), on the same 50 m island lattice
  * hypsometric integral (mean - min) / (max - min) and the relief range
  * share of the window where the island is within 25 m / 75 m of the template, and where constraints
    (pads, clearance surfaces, the coast) held it more than 75 m away
Writes tools/island/topo/fidelity.json. Run: /private/tmp/topo-venv/bin/python -I tools/island/topo/fidelity.py
"""
import json, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import plates  # noqa: E402  (relief decoder)

FEATHER_KM = 0.6


def stats(z, cell_m):
    gy, gx = np.gradient(z, cell_m)
    s = np.degrees(np.arctan(np.hypot(gx, gy)))[1:-1, 1:-1]
    zz = z[1:-1, 1:-1]
    return s, zz


def main():
    doc = json.load(open(os.path.join(HERE, 'templates.json')))
    blob = np.fromfile(os.path.join(HERE, 'templates.bin'), '<i2')
    R = plates.relief()
    out = []
    for a in doc['areas']:
        w, h = a['w'], a['h']; e0, e1, n0, n1 = a['window']
        t = blob[a['offset']:a['offset'] + w * h].reshape(h, w).astype(float)
        sea = t == -32768; t = t / 10
        E, N = np.meshgrid(e0 + np.arange(w) * a['cell_km'], n0 + np.arange(h) * a['cell_km'])
        i = np.clip(np.round((E - R['e0']) / R['cell']).astype(int), 0, R['w'] - 1)
        j = np.clip(np.round((N - R['n0']) / R['cell']).astype(int), 0, R['h'] - 1)
        z = R['z'][j, i]
        inner = (E > e0 + FEATHER_KM) & (E < e1 - FEATHER_KM) & (N > n0 + FEATHER_KM) & (N < n1 - FEATHER_KM)
        land = inner & ~sea & (z > 1)
        ts, tz = stats(np.where(sea, 0, t), a['cell_km'] * 1000)
        zs, zz = stats(z, a['cell_km'] * 1000)
        m = land[1:-1, 1:-1]
        if not m.any():
            continue
        q = lambda arr: [round(float(np.percentile(arr[m], p)), 1) for p in (25, 50, 75, 90)]
        hi = lambda arr: round(float((arr[m].mean() - arr[m].min()) / (arr[m].max() - arr[m].min() + 1e-9)), 3)
        d = np.abs(zz - tz)[m]
        out.append(dict(id=a['id'], c=a['c'], land_km2=round(float(m.sum()) * a['cell_km'] ** 2, 1),
                        slope_deg_p25_p50_p75_p90=dict(real=q(ts), island=q(zs)),
                        hypsometric_integral=dict(real=hi(tz), island=hi(zz)),
                        relief_m=dict(real=[round(float(tz[m].min())), round(float(tz[m].max()))], island=[round(float(zz[m].min())), round(float(zz[m].max()))]),
                        within_25m=round(float((d < 25).mean()), 3), within_75m=round(float((d < 75).mean()), 3),
                        held_off_over_75m=round(float((d >= 75).mean()), 3)))
        r = out[-1]
        print(f"{a['id']:13s} c {a['c']:3.1f} slope p50/p90 real {r['slope_deg_p25_p50_p75_p90']['real'][1]:5.1f}/{r['slope_deg_p25_p50_p75_p90']['real'][3]:5.1f}"
              f" island {r['slope_deg_p25_p50_p75_p90']['island'][1]:5.1f}/{r['slope_deg_p25_p50_p75_p90']['island'][3]:5.1f}"
              f"  HI {r['hypsometric_integral']['real']:.2f}/{r['hypsometric_integral']['island']:.2f}  within 25 m {r['within_25m']:.0%}, 75 m {r['within_75m']:.0%}")
    with open(os.path.join(HERE, 'fidelity.json'), 'w') as f:
        json.dump(dict(method=__doc__.strip().splitlines()[0], areas=out), f, indent=1); f.write('\n')


if __name__ == '__main__':
    main()
