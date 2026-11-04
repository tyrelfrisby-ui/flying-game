"""XFOIL polars for the Grob G 103 sections (2026-11-01, lane/claude-grob103; E583 added 2026-11-04 for the Twin III Acro).

    XFOIL=/path/to/xfoil python3 tools/aircraft_builder/grob103_xfoil.py

Writes tools/aircraft_builder/data/grob103_xfoil.json, which build_grob103.py reads, so building the config does not
need XFOIL. The run is XFOIL 6.99 (MIT, Drela), built from the published source with gfortran and the X11 plot driver
stubbed out (no plotting is used). Coordinates are fetched from the UIUC Airfoil Coordinates Database
(https://m-selig.ae.illinois.edu/ads/coord/), Lednicer format, and converted to XFOIL's Selig order here.

Settings, all stated rather than tuned:
  - 200 panels (PPAR N 200), free transition, Ncrit 9 (the e^N "average wind tunnel" value XFOIL documents; free air in
    a thermal is noisier than a low-turbulence tunnel, not quieter, so 9 is not optimistic).
  - Mach 0 (the glider never exceeds M 0.21).
  - Reynolds numbers 0.6, 0.8, 1.0, 1.25, 1.5, 2.0, 2.5 million: the wing's chord-Reynolds range from the tip at
    70 km/h to the root at 200 km/h.
  - alpha swept outward from 0 in both directions, 0.25 deg steps, so each point starts from a converged neighbour.
"""
import json, math, os, subprocess, sys, tempfile, urllib.request

HERE = os.path.dirname(os.path.realpath(__file__))
OUT = os.path.join(HERE, 'data', 'grob103_xfoil.json')
XFOIL = os.environ.get('XFOIL', 'xfoil')
FOILS = {
    # wing: Eppler E603 (Twin Astir / Twin II wing section) -- kept for the record, no longer flown
    'e603': 'https://m-selig.ae.illinois.edu/ads/coord/e603.dat',
    # wing: Eppler E583, the G 103 C Twin III Acro's root and tip section (UIUC "Incomplete Guide to Airfoil Usage":
    # "Grob 103C Twin III Acro  Eppler 583  Eppler 583")
    'e583': 'https://m-selig.ae.illinois.edu/ads/coord/e583.dat',
    # tail: Wortmann FX 71-L-150/30, a flapped-tail section of the period (see the derivation doc for the assumption)
    'fx71l150': 'https://m-selig.ae.illinois.edu/ads/coord/fx71l150.dat',
}
RES = {'e603': [0.6e6, 0.8e6, 1.0e6, 1.25e6, 1.5e6, 2.0e6, 2.5e6],
       'e583': [0.6e6, 0.8e6, 1.0e6, 1.25e6, 1.5e6, 2.0e6, 2.5e6],
       'fx71l150': [0.4e6, 0.6e6, 0.8e6, 1.0e6, 1.25e6]}   # tail chords 0.3-0.8 m
NCRIT = 9


def lednicer_to_selig(text):
    rows = [l.split() for l in text.strip().splitlines()[1:]]
    rows = [r for r in rows if len(r) == 2]
    nu, nl = int(float(rows[0][0])), int(float(rows[0][1]))
    pts = [(float(a), float(b)) for a, b in rows[1:]]
    up, lo = pts[:nu], pts[nu:nu + nl]
    if up[0][0] > up[-1][0]: up = up[::-1]   # make both run LE -> TE
    if lo[0][0] > lo[-1][0]: lo = lo[::-1]
    return up[::-1] + lo[1:]                  # TE -> upper -> LE -> lower -> TE


def coords(name, work):
    path = os.path.join(work, name + '.dat')
    raw = urllib.request.urlopen(FOILS[name], timeout=60).read().decode('latin-1')
    first = raw.strip().splitlines()[1].split()
    pts = lednicer_to_selig(raw) if float(first[0]) > 1.5 else [tuple(map(float, l.split())) for l in raw.strip().splitlines()[1:] if len(l.split()) == 2]
    with open(path, 'w') as f:
        f.write(name.upper() + '\n')
        for x, y in pts: f.write(f'{x:.6f} {y:.6f}\n')
    t = max(y for x, y in pts) - min(y for x, y in pts)
    return path, pts


def run(name, path, re, work):
    pols = []
    for lo, hi, st in ((0, 20, 0.25), (-0.25, -12, -0.25)):
        pf = os.path.join(work, f'{name}_{int(re)}_{hi}.pol')
        if os.path.exists(pf): os.remove(pf)
        cmd = f"""PLOP
G F

LOAD {path}
PPAR
N 200


OPER
VISC {re:.0f}
VPAR
N {NCRIT}

ITER 300
PACC
{pf}

ASEQ {lo} {hi} {st}
PACC

QUIT
"""
        try:
            subprocess.run([XFOIL], input=cmd, capture_output=True, text=True, timeout=240, cwd=work)   # its scratch files stay in work
        except subprocess.TimeoutExpired:
            print(f'  {name} Re {re/1e6:.2f}M sweep to {hi}: XFOIL hung; keeping the points it wrote')
        if not os.path.exists(pf): continue
        with open(pf) as f:
            for line in f:
                p = line.split()
                if len(p) >= 7:
                    try: pols.append(tuple(float(v) for v in p[:7]))
                    except ValueError: pass
    pols = sorted({round(p[0], 3): p for p in pols}.values())
    return [{'alphaDeg': p[0], 'cl': p[1], 'cd': p[2], 'cm': p[4]} for p in pols]


def main():
    work = tempfile.mkdtemp(prefix='gx', dir='/tmp')   # short: XFOIL truncates long file names
    out = json.load(open(OUT)) if os.path.exists(OUT) else None
    if out is None or out.get('ncrit') != NCRIT:
        out = {'_source': __doc__.strip().splitlines()[0], 'ncrit': NCRIT, 'xfoil': 'XFOIL 6.99', 'foils': {}}
    for name in FOILS:
        path, pts = coords(name, work)
        # thickness and camber from the coordinates (upper/lower at matched x by interpolation)
        out['foils'].setdefault(name, {'url': FOILS[name], 'polars': {}})
        for re in RES[name]:
            if f'{re:.0f}' in out['foils'][name]['polars']: continue   # checkpointed
            pol = run(name, path, re, work)
            out['foils'][name]['polars'][f'{re:.0f}'] = pol
            cl = [p['cl'] for p in pol]
            json.dump(out, open(OUT, 'w'), indent=1)
            print(f'{name} Re {re/1e6:.2f}M: {len(pol)} points, clmax {max(cl):.3f}, cdmin {min(p["cd"] for p in pol):.5f}')
    json.dump(out, open(OUT, 'w'), indent=1)
    print('wrote', OUT)


if __name__ == '__main__':
    main()
