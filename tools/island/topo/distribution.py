#!/usr/bin/env python3
"""Where each land-cover class sits by terrain, per reference area.

    python -I -B tools/island/topo/distribution.py

Inputs: the class maps written by imagery.py (build/topo-ref/imagery/<area>-classes.png, on
imagery.py's 10 m local transverse-Mercator grid) and AWS Terrain Tiles (Terrarium PNG, zoom 12,
elevation = R*256 + G + B/256 - 32768 m), cached under build/topo-ref/dem-imagery/.
Output: tools/island/topo/distribution.json (sorted keys, fixed rounding, so deterministic).

Method:
  * The zoom-12 tiles covering each bbox are mosaicked in Web Mercator and resampled (cubic)
    onto a 30 m grid sharing the class grid's origin; slope and aspect are central differences
    on that 30 m DEM. Each 10 m class pixel takes its 30 m cell's elevation/slope/aspect
    (nearest), so classes stay at 10 m and terrain is at 30 m. Zoom-12 Terrarium is ~38 m/px
    at the equator, ~18 m at 61 N; its sources vary (SRTM, USGS 3DEP/NED, ArcticDEM...), so
    treat slope as a 30 m-scale quantity and expect glacier surfaces to be from an older DEM.
  * Aspect is the compass direction the slope faces (downhill), 0 = north, clockwise.
    North-facing = 315..45, south-facing = 135..225.
  * Excluded pixels (cloud, shadow, no-data in imagery.py) are left out of every statistic.

Per area:
  bands        class fractions per elevation band (100 m; Dillingham 50 m; band 0 also holds
               anything below 0 m, i.e. the sea floor under water pixels). Only classes with
               >= 1 % cover anywhere in the area are listed; 'n' is usable pixels per band.
  treeline     forest = conifer_forest + broadleaf_tropical_veg, slope > 15 deg: p95 elevation
               on N- vs S-facing ground (the effective treeline by aspect), and the p99 of ALL
               usable ground on those slopes: where the two are close, the terrain ran out first.
  snow / glacier_ice  p5 elevation N- vs S-facing (slope > 10 deg, so aspect means something),
               plus p0.5 and min elevation of the class over all ground ('lowest'). Only where
               the class covers >= 1 % of the area: below that the pixels are mostly surf (Dillingham,
               Sitka) or silty river (Grand Canyon) that imagery.py's NDSI rule mistakes for ice.
               Elevations come from a DEM older than the scene, so on a retreating glacier the
               ice limits are approximate (and below 0 where a lobe reaches tidewater).
  bare_rock_by_slope  bare_rock fraction of usable pixels in slope classes 0-20, 20-35, 35-50,
               >50 deg (and the class pixel count per slope class).
  lowland_sediment  (Knik, Fairweather) on slope < 3 deg and elevation < 200 m: fractions of
               sediment_gravel, water, bare_rock and vegetation (all four vegetation classes).
"""
from __future__ import annotations

import io
import json
import math
import sys
import urllib.request
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import imagery  # noqa: E402  (same directory; AREAS, CLASSES, local_grid)

ROOT = imagery.ROOT
TILES = ROOT / 'build/topo-ref/dem-imagery'
OUT = HERE / 'distribution.json'
TILE_URL = 'https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png'
ZOOM = 12
DEM_PIXEL = 30.0
BAND_M = {'dillingham': 50}
SLOPE_CLASSES = ((0, 20), (20, 35), (35, 50), (50, 90))
FOREST = ('conifer_forest', 'broadleaf_tropical_veg')
VEG = FOREST + ('grass_meadow', 'dry_grass_shrub')
EARTH_HALF = 20037508.342789244


def tile_xy(lon, lat, z):
    n = 2 ** z
    x = (lon + 180) / 360 * n
    y = (1 - math.asinh(math.tan(math.radians(lat))) / math.pi) / 2 * n
    return int(math.floor(x)), int(math.floor(y))


def tile(z, x, y):
    path = TILES / f'{z}_{x}_{y}.png'
    if not path.exists():
        TILES.mkdir(parents=True, exist_ok=True)
        with urllib.request.urlopen(TILE_URL.format(z=z, x=x, y=y), timeout=60) as r:
            data = r.read()
        if not data.startswith(b'\x89PNG'):
            raise ValueError(f'tile {z}/{x}/{y} is not a PNG')
        path.write_bytes(data)
    from PIL import Image
    a = np.asarray(Image.open(io.BytesIO(path.read_bytes())).convert('RGB')).astype(np.float64)
    return a[..., 0] * 256 + a[..., 1] + a[..., 2] / 256 - 32768


def dem_on_grid(bbox, crs, transform, w, h):
    """Mosaic the Terrarium tiles over bbox and resample onto the given grid (cubic)."""
    from rasterio.transform import from_origin
    from rasterio.warp import Resampling, reproject
    pad = 0.01
    x0, y0 = tile_xy(bbox[0] - pad, bbox[3] + pad, ZOOM)
    x1, y1 = tile_xy(bbox[2] + pad, bbox[1] - pad, ZOOM)
    rows = [np.hstack([tile(ZOOM, x, y) for x in range(x0, x1 + 1)]) for y in range(y0, y1 + 1)]
    mosaic = np.vstack(rows).astype(np.float32)
    size = 2 * EARTH_HALF / (2 ** ZOOM) / 256
    src_t = from_origin(-EARTH_HALF + x0 * 256 * size, EARTH_HALF - y0 * 256 * size, size, size)
    out = np.zeros((h, w), np.float32)
    reproject(mosaic, out, src_transform=src_t, src_crs='EPSG:3857', dst_transform=transform,
              dst_crs=crs, resampling=Resampling.cubic)
    return out


def terrain(name, shape10):
    from PIL import Image  # noqa: F401
    from rasterio.transform import from_origin
    bbox = imagery.AREAS[name][0]
    crs, t10, w, h = imagery.local_grid(bbox)
    assert (h, w) == shape10, (name, (h, w), shape10)
    w30, h30 = math.ceil(w / 3), math.ceil(h / 3)
    t30 = from_origin(t10.c, t10.f, DEM_PIXEL, DEM_PIXEL)
    z = dem_on_grid(bbox, crs, t30, w30, h30).astype(np.float64)
    dzdrow, dzdcol = np.gradient(z, DEM_PIXEL)
    dzde, dzdn = dzdcol, -dzdrow  # rows run south
    slope = np.degrees(np.arctan(np.hypot(dzde, dzdn)))
    aspect = np.degrees(np.arctan2(-dzde, -dzdn)) % 360  # downhill direction
    up = lambda a: np.repeat(np.repeat(a, 3, 0), 3, 1)[:h, :w]  # noqa: E731
    return up(z), up(slope), up(aspect)


def pct(a, q):
    return None if a.size == 0 else int(round(float(np.percentile(a, q))))


def facing(aspect, slope, min_slope):
    north = ((aspect >= 315) | (aspect < 45)) & (slope > min_slope)
    south = (aspect >= 135) & (aspect < 225) & (slope > min_slope)
    return north, south


def analyse(name):
    from PIL import Image
    cls = np.asarray(Image.open(imagery.OUT / f'{name}-classes.png'))
    elev, slope, aspect = terrain(name, cls.shape)
    usable = cls != imagery.EXCLUDED
    idx = {c: i for i, c in enumerate(imagery.CLASSES)}
    is_ = lambda *names: np.isin(cls, [idx[c] for c in names])  # noqa: E731
    n_usable = int(usable.sum())

    # (1) elevation bands
    step = BAND_M.get(name, 100)
    band = np.floor(np.maximum(elev, 0) / step).astype(int)  # sea floor / below datum -> band 0
    lo, hi = int(band[usable].min()), int(band[usable].max())
    shown = [c for c in imagery.CLASSES if (cls == idx[c]).sum() >= 0.01 * n_usable]
    counts = np.zeros((hi - lo + 1, len(imagery.CLASSES) + 1), np.int64)
    np.add.at(counts, (band[usable] - lo, cls[usable].astype(int)), 1)
    n_band = counts[:, :len(imagery.CLASSES)].sum(1)
    with np.errstate(invalid='ignore', divide='ignore'):
        frac = counts[:, :len(imagery.CLASSES)] / n_band[:, None]
    keep = n_band > 0
    bands = {'step_m': step, 'lo_m': [int(b * step) for b in range(lo, hi + 1) if keep[b - lo]],
             'n': [int(v) for v in n_band[keep]],
             'classes': {c: [round(float(v), 3) for v in frac[keep, idx[c]]] for c in shown}}

    # (2) treeline by aspect
    n15, s15 = facing(aspect, slope, 15)
    forest = is_(*FOREST)
    treeline = {'north_p95_m': pct(elev[forest & n15], 95), 'south_p95_m': pct(elev[forest & s15], 95),
                'pixels_n': int((forest & n15).sum()), 'pixels_s': int((forest & s15).sum()),
                # if the treeline is close to these, the terrain, not climate, is the limit
                'ground_p99_n_m': pct(elev[usable & n15], 99), 'ground_p99_s_m': pct(elev[usable & s15], 99)}

    # (3) snow and ice low limits
    n10, s10 = facing(aspect, slope, 10)
    limits = {}
    for c in ('snow', 'glacier_ice'):
        m = is_(c)
        if m.sum() < 0.01 * n_usable:  # below 1 % cover it is mostly surf/silty water mislabelled
            limits[c] = {'pixels': int(m.sum())}
            continue
        limits[c] = {'north_p5_m': pct(elev[m & n10], 5), 'south_p5_m': pct(elev[m & s10], 5),
                     'lowest_p0_5_m': pct(elev[m], 0.5), 'lowest_min_m': pct(elev[m], 0),
                     'pixels': int(m.sum())}

    # (4) bare rock vs slope
    rock = is_('bare_rock')
    by_slope = {}
    for a, b in SLOPE_CLASSES:
        s = usable & (slope >= a) & (slope < b)
        by_slope[f'{a}-{b}'] = {'frac': round(float((rock & s).sum() / max(s.sum(), 1)), 3),
                                'rock_px': int((rock & s).sum()), 'usable_px': int(s.sum())}

    out = {'bands': bands, 'treeline': treeline, 'low_limits': limits, 'bare_rock_by_slope': by_slope,
           'elev_range_m': [pct(elev[usable], 0), pct(elev[usable], 100)]}

    # (5) lowland sediment
    if name in ('knik', 'fairweather'):
        flat = usable & (slope < 3) & (elev < 200)
        nf = max(int(flat.sum()), 1)
        out['lowland_sediment'] = {
            'usable_px': int(flat.sum()),
            **{k: round(float((m & flat).sum() / nf), 3) for k, m in (
                ('sediment_gravel', is_('sediment_gravel')), ('water', is_('water')),
                ('bare_rock', rock), ('vegetation', is_(*VEG)))}}
    return out


def main():
    result = {'generator': 'tools/island/topo/distribution.py', 'dem': TILE_URL.replace('{z}', str(ZOOM)),
              'dem_note': 'AWS Terrain Tiles (Terrarium) z12, resampled to 30 m; classes at 10 m',
              'areas': {}}
    for name in imagery.AREAS:
        print(name, flush=True)
        result['areas'][name] = analyse(name)
    OUT.write_text(json.dumps(result, sort_keys=True, separators=(',', ':')) + '\n')
    summary(result)


def summary(result):
    print(f'\n{OUT.relative_to(ROOT)}: {OUT.stat().st_size} bytes')
    for name, a in result['areas'].items():
        t, l = a['treeline'], a['low_limits']
        print(f"\n{name}: elev {a['elev_range_m']}  treeline N {t['north_p95_m']} / S {t['south_p95_m']} m"
              f"  (ground p99 N {t['ground_p99_n_m']} / S {t['ground_p99_s_m']})")
        for c in ('snow', 'glacier_ice'):
            if 'north_p5_m' in l[c]:
                print(f"  {c:12s} p5 N {l[c]['north_p5_m']} / S {l[c]['south_p5_m']}  lowest p0.5 "
                      f"{l[c]['lowest_p0_5_m']} min {l[c]['lowest_min_m']}  ({l[c]['pixels']} px)")
        print('  bare_rock by slope: ' + '  '.join(f"{k}: {v['frac']:.3f}" for k, v in a['bare_rock_by_slope'].items()))
        if 'lowland_sediment' in a:
            print('  flat <200 m:', a['lowland_sediment'])


if __name__ == '__main__':
    main()
