#!/usr/bin/env python3
"""Satellite colour reference for the island's terrain materials.

Six real places stand in for the island's ground types. For each, one low-cloud summer
Sentinel-2 L2A scene (Earth Search STAC, AWS open data) is windowed to a small bbox over
HTTP, reprojected north-up, classified into material classes by simple spectral rules, and
summarised as colour statistics. This is a PALETTE reference, not a texture source.

    python -I tools/island/topo/imagery.py            # pinned scenes -> PNGs + palette.json
    python -I tools/island/topo/imagery.py --search   # re-run the scene search (prints, pins nothing)
    python -I tools/island/topo/imagery.py --refetch  # ignore the local band cache

Needs numpy, pillow, scipy, rasterio (GDAL with /vsicurl). Writes:
    build/topo-ref/imagery/<area>.png          true colour, 10 m/px, north up (fixed stretch, below)
    build/topo-ref/imagery/<area>-classes.png  indexed class map on the same grid (CLASS_COLOURS)
    build/topo-ref/imagery/<area>-plate.png    1024 px wide true colour with a 1 km scale bar
    build/topo-ref/imagery/cache/<area>.npz    the warped bands, so reruns need no network
    tools/island/topo/palette.json             the committed summary (< 30 KB)

Determinism: the scenes are pinned in SCENES; the grid, resampling, rules and percentiles
are fixed, PNGs carry no timestamps and the JSON is written with sorted keys.

Grid: a transverse Mercator centred on each bbox (so north is exactly up at the centre and
metres are true), 10 m pixels covering the bbox's projected extent. Bands are warped from the
scene's UTM tiles with GDAL: bilinear for reflectance and TCI, nearest for SCL.

True-colour PNGs use ONE fixed stretch for every area so the plates compare honestly:
    display = sRGB_OETF( clip( reflectance / 0.5, 0, 1 ) )
i.e. linear BOA reflectance with white at 0.5, encoded with the sRGB transfer curve (no
per-scene percentile stretch). Snow and fresh ice above 0.5 reflectance clip to white.

Colour statistics per class (pixels inside the window, excluding no-data, cloud and shadow):
    frac              share of the usable pixels
    tci_p10/p50/p90   per-channel 10/50/90th percentiles of ESA's 8-bit TCI ("visual" asset),
                      the product as a human sees it. ESA's TCI is linear in reflectance and
                      saturates near 0.25 (measured TCI ~ 0.1 * DN, i.e. 255 at reflectance
                      ~0.255); it is NOT gamma-encoded, so it reads brighter than albedo_hex.
    tci_hex           the p50 triple as hex
    refl_p50          median linear BOA reflectance R,G,B (B04,B03,B02, scale/offset from STAC)
    albedo_hex        sRGB_OETF(refl_p50): the median as an albedo / base-colour value,
                      which is the number to start a material from.

Classification (reflectances; SCL = Sentinel-2 scene classification), first match wins:
    excluded  : no-data, SCL 0/1 (no data, saturated), 3 (cloud shadow), 8/9/10 (cloud, cirrus),
                or SCL 2 (dark/topographic shadow) with NDVI < 0.4
    water     : after snow/ice: (SCL 6, or NDWI > 0.15 and NDVI < 0.05) and SWIR16 < 0.07 and brightness < 0.3.
                The SWIR cap matters: SCL calls the Knik's wet gravel bars water (SWIR 0.08-0.15),
                while open, silty and glinting water all stay below ~0.06.
    snow      : NDSI > 0.4 and visible brightness > 0.55 (or SCL 11 above that brightness)
    glacier_ice: NDSI > 0.4 (or SCL 11) and brightness 0.12..0.55 (bare/blue ice, firn). The
                part above 0.2 is tested BEFORE water, because crevassed blue ice is as dark in
                SWIR as water; the dimmer part only after water has had its pick
    NDVI >= 0.5:  conifer_forest if NIR < 0.25; else broadleaf_tropical_veg if NDVI >= 0.65;
                  else grass_meadow
    0.3 <= NDVI < 0.5:  grass_meadow
    0.12 <= NDVI < 0.3: dry_grass_shrub, unless SCL 5 (not vegetated) and NDVI < 0.22 and
                brightness > 0.1: Grand Canyon's iron-stained rock reads NDVI 0.1-0.2 and goes bare
    NDVI < 0.12 or that rock exception (bare):
      red_rock      : red/green > 1.25 and brightness > 0.06 (desert sandstone, red soils)
      (distances below are to water bodies of >= 2 ha, so melt ponds on ice don't count)
      beach_sand    : within 60 m of water, brightness > 0.15, red/green 1.05..1.25 (warm, bright)
      sediment_gravel: within 300 m of water and red/green <= 1.25 (bars of a braided river)
      bare_rock     : everything else bare (light/grey rock, talus, moraine, roads/roofs)
    NDVI = (NIR-R)/(NIR+R), NDWI = (G-NIR)/(G+NIR), NDSI = (G-SWIR16)/(G+SWIR16),
    brightness = mean(R,G,B), NIR = B08 (10 m), SWIR16 = B11 (20 m, bilinear to 10 m).
These are deliberately simple; confusions are listed in the summary notes of palette.json.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys
import urllib.request
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'build/topo-ref/imagery'
CACHE = OUT / 'cache'
PALETTE = Path(__file__).resolve().parent / 'palette.json'
STAC = 'https://earth-search.aws.element84.com/v1'
COLLECTION = 'sentinel-2-l2a'
PIXEL_M = 10.0
WHITE_REFLECTANCE = 0.5
PLATE_WIDTH = 1024
CACHE_VERSION = '2'  # bump when fetch() changes what it stores

# (lon_min, lat_min, lon_max, lat_max), preferred months, max scene cloud for the search
AREAS = {
    'sitka':        ((-135.55, 56.98, -135.18, 57.12), (6, 9), 30),
    'fairweather':  ((-137.95, 58.70, -137.35, 59.00), (7, 9), 30),
    'grandcanyon':  ((-112.25, 36.02, -111.98, 36.20), (5, 10), 10),
    'dillingham':   ((-158.30, 21.53, -158.12, 21.62), (1, 12), 20),
    'knik':         ((-148.80, 61.33, -148.30, 61.50), (7, 9), 30),
    'johnsoncreek': ((-115.56, 44.86, -115.42, 44.98), (7, 9), 10),
}

# Chosen by `--search` (clearest SCL window among the lowest-cloud passes), pinned here.
# A list holds the tiles of ONE pass (same date and platform) when the bbox straddles tiles.
SCENES = {
    'sitka': ['S2B_8VMJ_20200913_1_L2A'],
    'fairweather': ['S2B_8VLL_20230822_0_L2A'],
    'grandcanyon': ['S2A_12SUE_20230805_0_L2A', 'S2A_12SUF_20230805_0_L2A',
                    'S2A_12SVE_20230805_0_L2A', 'S2A_12SVF_20230805_0_L2A'],
    'dillingham': ['S2A_4QEJ_20200930_1_L2A', 'S2A_4QEK_20200930_1_L2A'],
    'knik': ['S2B_6VUP_20200911_1_L2A', 'S2B_6VVP_20200911_1_L2A'],
    'johnsoncreek': ['S2B_11TPK_20200715_1_L2A'],
}

CLASSES = ['water', 'glacier_ice', 'snow', 'bare_rock', 'red_rock', 'sediment_gravel',
           'beach_sand', 'conifer_forest', 'broadleaf_tropical_veg', 'grass_meadow',
           'dry_grass_shrub']
EXCLUDED = 255
CLASS_COLOURS = {  # class-map display colours only (not measured)
    'water': (30, 80, 200), 'glacier_ice': (150, 220, 255), 'snow': (255, 255, 255),
    'bare_rock': (150, 150, 150), 'red_rock': (200, 90, 50), 'sediment_gravel': (210, 200, 160),
    'beach_sand': (250, 230, 120), 'conifer_forest': (20, 90, 40),
    'broadleaf_tropical_veg': (60, 200, 60), 'grass_meadow': (160, 230, 90),
    'dry_grass_shrub': (190, 170, 80),
}
LICENCE_S2 = ('Copernicus Sentinel data, free and open (Copernicus licence); '
              'attribution: Contains modified Copernicus Sentinel data {year}')
LICENCE_LANDSAT = 'USGS, public domain'
BANDS = ('red', 'green', 'blue', 'nir', 'swir16')
CLOUD_SCL = (3, 8, 9, 10)

GDAL_ENV = dict(AWS_NO_SIGN_REQUEST='YES', GDAL_DISABLE_READDIR_ON_OPEN='EMPTY_DIR',
                CPL_VSIL_CURL_ALLOWED_EXTENSIONS='.tif,.TIF', GDAL_HTTP_MULTIRANGE='YES',
                GDAL_HTTP_MERGE_CONSECUTIVE_RANGES='YES', GDAL_HTTP_MAX_RETRY='4',
                GDAL_HTTP_RETRY_DELAY='2', VSI_CACHE='TRUE', GDAL_CACHEMAX=512)


# ---------------------------------------------------------------- STAC

def _post(url, body):
    req = urllib.request.Request(url, data=json.dumps(body).encode(),
                                 headers={'Content-Type': 'application/json'})
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.load(r)


def get_item(scene_id):
    with urllib.request.urlopen(f'{STAC}/collections/{COLLECTION}/items/{scene_id}', timeout=120) as r:
        return json.load(r)


def search(bbox, months, max_cloud, years=range(2019, 2026)):
    items = []
    for y in years:
        m0, m1 = months
        last = (31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31)[m1 - 1]
        body = {'collections': [COLLECTION], 'bbox': list(bbox), 'limit': 200,
                'datetime': f'{y}-{m0:02d}-01T00:00:00Z/{y}-{m1:02d}-{last}T23:59:59Z',
                'query': {'eo:cloud_cover': {'lt': max_cloud}}}
        while body:
            d = _post(f'{STAC}/search', body)
            items += d['features']
            nxt = [l for l in d.get('links', []) if l.get('rel') == 'next']
            body = nxt[0].get('body') if nxt else None
    # keep the newest processing of each tile/date, then group tiles of one pass by date+platform
    latest = {}
    for it in items:
        base = it['id'].rsplit('_', 2)[0]
        if base not in latest or it['id'] > latest[base]['id']:
            latest[base] = it
    groups = {}
    for it in latest.values():
        p = it['properties']
        groups.setdefault((p['datetime'][:10], p.get('platform')), []).append(it)
    return [sorted(g, key=lambda it: it['id']) for g in groups.values()
            if contains([it['geometry'] for it in g], bbox)]


def _in_ring(x, y, ring):
    inside = False
    for (x1, y1), (x2, y2) in zip(ring, ring[1:] + ring[:1]):
        if (y1 > y) != (y2 > y) and x < x1 + (y - y1) * (x2 - x1) / (y2 - y1):
            inside = not inside
    return inside


def contains(geoms, bbox):
    """True if every sample of the bbox lies in at least one footprint."""
    polys = []
    for g in geoms:
        polys += [g['coordinates']] if g['type'] == 'Polygon' else g['coordinates']
    rings = [[tuple(c[:2]) for c in p[0]] for p in polys]
    x0, y0, x1, y1 = bbox
    pts = [(x0 + (x1 - x0) * i / 8, y0 + (y1 - y0) * j / 8) for i in range(9) for j in range(9)]
    return all(any(_in_ring(x, y, r) for r in rings) for x, y in pts)


def window_cloud(items, bbox):
    """Fraction of the bbox window that SCL calls cloud/shadow/no-data (60 m local grid)."""
    import rasterio
    from rasterio.enums import Resampling
    crs, transform, w, h = local_grid(bbox, 60.0)
    with rasterio.Env(**GDAL_ENV):
        scl = warp(items, 'scl', crs, transform, w, h, Resampling.nearest)[0]
    bad = np.isin(scl, (0, 1) + CLOUD_SCL)
    return float(bad.mean()), float((scl == 11).mean())


def do_search(names):
    for name in names:
        bbox, months, max_cloud = AREAS[name]
        items = search(bbox, months, max_cloud)
        if len(items) < 3:
            items = search(bbox, (1, 12), max(max_cloud, 30))
        cc = lambda g: max(it['properties']['eo:cloud_cover'] for it in g)  # noqa: E731
        items.sort(key=lambda g: (cc(g), g[0]['id']))
        print(f'{name}: {len(items)} passes covering the bbox')
        best = []
        for g in items[:12]:
            ids = [it['id'] for it in g]
            try:
                cf, snow = window_cloud(g, bbox)
            except Exception as e:  # noqa: BLE001 - report and move on
                print('   ', ids, 'failed', e)
                continue
            best.append((round(cf, 3), cc(g), ids))
            print(f"    {' + '.join(ids):60s} {g[0]['properties']['datetime'][:10]} scene {cc(g):5.1f}%"
                  f"  window cloud/shadow {100 * cf:5.1f}%  snow {100 * snow:5.1f}%", flush=True)
        best.sort()
        print(f'  -> {best[0][2] if best else None}')


# ---------------------------------------------------------------- read + warp

def local_grid(bbox, pixel=PIXEL_M):
    from rasterio.crs import CRS
    from rasterio.transform import from_origin
    from rasterio.warp import transform_bounds
    lon0, lat0 = (bbox[0] + bbox[2]) / 2, (bbox[1] + bbox[3]) / 2
    crs = CRS.from_proj4(f'+proj=tmerc +lat_0={lat0} +lon_0={lon0} +k=1 +x_0=0 +y_0=0 '
                         '+datum=WGS84 +units=m +no_defs')
    x0, y0, x1, y1 = transform_bounds('EPSG:4326', crs, *bbox, densify_pts=21)
    x0, y0 = math.floor(x0 / pixel) * pixel, math.floor(y0 / pixel) * pixel
    x1, y1 = math.ceil(x1 / pixel) * pixel, math.ceil(y1 / pixel) * pixel
    w, h = int(round((x1 - x0) / pixel)), int(round((y1 - y0) / pixel))
    return crs, from_origin(x0, y1, pixel, pixel), w, h


def warp(items, key, crs, transform, w, h, resampling):
    """Warp one asset of each item onto the grid; earlier items win where tiles overlap."""
    import rasterio
    from rasterio.vrt import WarpedVRT
    out = None
    for it in items:
        with rasterio.open(it['assets'][key]['href']) as src, WarpedVRT(
                src, crs=crs, transform=transform, width=w, height=h,
                resampling=resampling, src_nodata=0, nodata=0) as vrt:
            a = vrt.read()
        if out is None:
            out = a
        else:
            hole = np.all(out == 0, axis=0)
            out[:, hole] = a[:, hole]
    return out


def boa_offset(item, key):
    """Reflectance offset still to apply to this asset's DN.

    STAC lists offset -0.1 for processing baseline >= 04.00, but Earth Search's COGs have it
    already applied when `earthsearch:boa_offset_applied` is true (checked: TCI ~ 0.1 * DN
    with a near-zero intercept on every pinned scene). Applying it twice drives water negative.
    """
    if item['properties'].get('earthsearch:boa_offset_applied'):
        return 0.0
    return (item['assets'][key].get('raster:bands') or [{}])[0].get('offset', 0.0)


def fetch(name, items, refetch):
    cache = CACHE / f'{name}.npz'
    sid = '+'.join(it['id'] for it in items)
    if cache.exists() and not refetch:
        z = np.load(cache, allow_pickle=False)
        if 'version' in z.files and str(z['version']) == CACHE_VERSION and str(z['scene']) == sid:
            return {k: z[k] for k in z.files if k not in ('scene', 'version')}
    import rasterio
    from rasterio.enums import Resampling
    crs, transform, w, h = local_grid(AREAS[name][0])
    out = {}
    with rasterio.Env(**GDAL_ENV):
        for key in BANDS + ('scl', 'visual'):
            rs = Resampling.nearest if key == 'scl' else Resampling.bilinear
            a = warp(items, key, crs, transform, w, h, rs)
            if key in BANDS:
                scales = {(it['assets'][key].get('raster:bands') or [{}])[0].get('scale', 1e-4) for it in items}
                offsets = {boa_offset(it, key) for it in items}
                assert len(scales) == len(offsets) == 1, (key, scales, offsets)
                scale, offset = scales.pop(), offsets.pop()
                valid = a[0] != 0
                a = np.where(valid, a[0].astype(np.float32) * scale + offset, np.nan).astype(np.float32)
            elif key == 'scl':
                a = a[0]
            else:
                a = np.moveaxis(a, 0, -1)
            out[key] = a
            print(f'    {name}: {key} {a.shape}', flush=True)
    CACHE.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(cache, scene=np.array(sid), version=np.array(CACHE_VERSION), **out)
    return out


# ---------------------------------------------------------------- classify + stats

def _ratio(a, b):
    with np.errstate(divide='ignore', invalid='ignore'):
        return (a - b) / (a + b)


def classify(d):
    from scipy.ndimage import distance_transform_edt, label
    r, g, b, nir, sw, scl = d['red'], d['green'], d['blue'], d['nir'], d['swir16'], d['scl']
    ndvi, ndwi, ndsi = _ratio(nir, r), _ratio(g, nir), _ratio(g, sw)
    bright = (r + g + b) / 3
    with np.errstate(divide='ignore', invalid='ignore'):
        rg = r / g
    nodata = (~np.isfinite(bright) | ~np.isfinite(nir) | ~np.isfinite(sw) | (scl == 0)
              | np.all(d['visual'] == 0, axis=-1))
    excluded = nodata | np.isin(scl, (1,) + CLOUD_SCL) | ((scl == 2) & ~(ndvi >= 0.4))
    cls = np.full(r.shape, EXCLUDED, np.uint8)
    free = ~excluded

    def take(mask, name):
        m = free & mask
        cls[m] = CLASSES.index(name)
        free[m] = False

    snowy = (ndsi > 0.4) | (scl == 11)
    take(snowy & (bright > 0.55), 'snow')
    take(snowy & (bright > 0.2), 'glacier_ice')  # blue/crevassed ice is dark in SWIR like water
    take(((scl == 6) | ((ndwi > 0.15) & (ndvi < 0.05))) & (sw < 0.07) & (bright < 0.3), 'water')
    take(snowy & (bright > 0.12), 'glacier_ice')  # dirty ice / firn that is not water
    take((ndvi >= 0.5) & (nir < 0.25), 'conifer_forest')
    take((ndvi >= 0.65), 'broadleaf_tropical_veg')
    take((ndvi >= 0.3), 'grass_meadow')
    canyon_rock = (scl == 5) & (ndvi < 0.22) & (bright > 0.1)  # iron-oxide rock reads NDVI 0.1-0.2
    take((ndvi >= 0.12) & ~canyon_rock, 'dry_grass_shrub')
    take((rg > 1.25) & (bright > 0.06), 'red_rock')
    water = cls == CLASSES.index('water')
    lab, n = label(water)  # proximity counts only water bodies >= 2 ha, not melt ponds on ice
    if n:
        big = np.bincount(lab.ravel()) >= 200
        big[0] = False
        water = big[lab]
    dist_m = distance_transform_edt(~water) * PIXEL_M if water.any() else np.full(r.shape, np.inf)
    take((dist_m <= 60) & (bright > 0.15) & (rg >= 1.05) & (rg <= 1.25), 'beach_sand')
    take((dist_m <= 300) & (rg <= 1.25), 'sediment_gravel')
    take(np.ones_like(free), 'bare_rock')
    return cls, nodata


def srgb_oetf(x):
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.0031308, 12.92 * x, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def hexc(rgb):
    return '#' + ''.join(f'{int(v):02x}' for v in rgb)


def colour_stats(d, m, n):
    k = int(m.sum())
    if k == 0:
        return {'frac': 0.0}
    tci = d['visual'][m].astype(np.float64)
    p10, p50, p90 = (np.percentile(tci, q, axis=0) for q in (10, 50, 90))
    refl = np.array([np.median(d[c][m]) for c in ('red', 'green', 'blue')])
    alb = np.round(srgb_oetf(refl) * 255).astype(int)
    return {
        'frac': round(k / n, 4),
        'tci_p10': [int(round(v)) for v in p10], 'tci_p50': [int(round(v)) for v in p50],
        'tci_p90': [int(round(v)) for v in p90], 'tci_hex': hexc(np.round(p50)),
        'refl_p50': [round(float(v), 4) for v in refl], 'albedo_hex': hexc(alb),
    }


def stats(d, cls, nodata):
    usable = cls != EXCLUDED
    n = int(usable.sum())
    out = {name: colour_stats(d, cls == i, n) for i, name in enumerate(CLASSES)}
    total = int((~nodata).sum())
    return out, round(1 - n / total, 4) if total else 1.0


# A beach is two or three 10 m pixels wide, so the area-wide beach_sand class barely samples it.
# 'sand' is an extra, dedicated sample over one shoreline (not part of the class map, and its
# frac is of the usable pixels in SAND_SAMPLES' bbox only). Rule, on the same warped bands:
#   within 50 m of the area's largest water body (the ocean), NDVI < 0.2, brightness > 0.15,
#   SWIR16 > 0.2, and not cloud/shadow/no-data by SCL. The SWIR floor is what separates dry
#   sand (SWIR16 0.2-0.4) from surf and wet foam, which are as bright but absorb in the SWIR.
SAND_SAMPLES = {'dillingham': (-158.26, 21.57, -158.12, 21.62)}  # Mokuleia - Dillingham shore


def sand_sample(name, d):
    from rasterio.transform import rowcol
    from rasterio.warp import transform_bounds
    from scipy.ndimage import distance_transform_edt, label
    r, g, b, nir, sw, scl = d['red'], d['green'], d['blue'], d['nir'], d['swir16'], d['scl']
    with np.errstate(divide='ignore', invalid='ignore'):
        ndvi = (nir - r) / (nir + r)
    bright = (r + g + b) / 3
    lab, n = label((scl == 6) & (sw < 0.07))
    ocean = lab == (np.argmax(np.bincount(lab.ravel())[1:]) + 1)
    dist_m = distance_transform_edt(~ocean) * PIXEL_M
    crs, transform, w, h = local_grid(AREAS[name][0])
    x0, y0, x1, y1 = transform_bounds('EPSG:4326', crs, *SAND_SAMPLES[name], densify_pts=21)
    (ra, ca), (rb, cb) = rowcol(transform, [x0, x1], [y1, y0])
    win = np.zeros(r.shape, bool)
    win[max(ra, 0):rb + 1, max(ca, 0):cb + 1] = True
    usable = win & np.isfinite(bright) & np.isfinite(sw) & ~np.isin(scl, (0, 1) + CLOUD_SCL)
    m = usable & (dist_m <= 50) & (ndvi < 0.2) & (bright > 0.15) & (sw > 0.2)
    out = colour_stats(d, m, int(usable.sum()))
    out.update(bbox=list(SAND_SAMPLES[name]), pixels=int(m.sum()),
               rule='<=50 m from ocean, NDVI<0.2, brightness>0.15, SWIR16>0.2, SCL clear')
    return out


# ---------------------------------------------------------------- images

def truecolour(d):
    rgb = np.stack([d['red'], d['green'], d['blue']], -1)
    img = np.round(srgb_oetf(np.nan_to_num(rgb, nan=0.0) / WHITE_REFLECTANCE) * 255).astype(np.uint8)
    return img


def write_png(img, path):
    from PIL import Image
    im = img if hasattr(img, 'save') else Image.fromarray(img)
    im.save(path, optimize=False, compress_level=6)


def class_png(cls, path):
    from PIL import Image
    im = Image.fromarray(cls, mode='P')
    pal = [0] * 768
    for i, name in enumerate(CLASSES):
        pal[3 * i:3 * i + 3] = CLASS_COLOURS[name]
    pal[3 * EXCLUDED:3 * EXCLUDED + 3] = (255, 0, 255)  # excluded (cloud, shadow, no-data)
    im.putpalette(pal)
    write_png(im, path)


def plate(img, path):
    from PIL import Image, ImageDraw
    h, w = img.shape[:2]
    ph = int(round(h * PLATE_WIDTH / w))
    im = Image.fromarray(img).resize((PLATE_WIDTH, ph), Image.LANCZOS)
    m_per_px = w * PIXEL_M / PLATE_WIDTH
    bar = int(round(1000 / m_per_px))
    dr = ImageDraw.Draw(im)
    x0, y1 = 24, ph - 24
    dr.rectangle([x0 - 10, y1 - 34, x0 + bar + 10, y1 + 10], fill=(0, 0, 0))
    dr.rectangle([x0, y1 - 8, x0 + bar, y1], fill=(255, 255, 255))
    dr.rectangle([x0 + bar // 2, y1 - 8, x0 + bar, y1 - 4], fill=(0, 0, 0))
    dr.text((x0, y1 - 28), '1 km', fill=(255, 255, 255))
    dr.text((PLATE_WIDTH - 24, 14), 'N', fill=(255, 255, 255))
    dr.polygon([(PLATE_WIDTH - 21, 30), (PLATE_WIDTH - 26, 42), (PLATE_WIDTH - 16, 42)], fill=(255, 255, 255))
    write_png(im, path)


# ---------------------------------------------------------------- main

NOTES = [
    'Palette reference only: colours are BOA reflectance from one summer scene per place.',
    'tci_* are ESA TCI 8-bit (linear, saturating near 0.25 reflectance): bright classes clip.',
    'albedo_hex = sRGB-encoded median reflectance; start base colours from this.',
    'Rules are spectral and simple; see the module docstring of imagery.py for thresholds.',
]


def run(names, refetch):
    OUT.mkdir(parents=True, exist_ok=True)
    palette = json.loads(PALETTE.read_text()) if PALETTE.exists() else {}
    areas = palette.get('areas', {})
    for name in names:
        ids = SCENES[name]
        if not ids:
            sys.exit(f'{name}: no pinned scene; run --search and pin it in SCENES')
        items = [get_item(i) for i in ids]
        p = items[0]['properties']
        print(f'{name}: {ids}', flush=True)
        d = fetch(name, items, refetch)
        cls, nodata = classify(d)
        cs, excl = stats(d, cls, nodata)
        img = truecolour(d)
        write_png(img, OUT / f'{name}.png')
        class_png(cls, OUT / f'{name}-classes.png')
        plate(img, OUT / f'{name}-plate.png')
        year = p['datetime'][:4]
        landsat = p.get('platform', '').startswith('landsat')
        areas[name] = {
            'scene_id': ids[0] if len(ids) == 1 else ids,
            'datetime': p['datetime'], 'platform': p.get('platform'),
            'cloud_cover': round(max(float(it['properties']['eo:cloud_cover']) for it in items), 2),
            'window_excluded_frac': excl, 'bbox': list(AREAS[name][0]),
            'source_urls': [f'{STAC}/collections/{COLLECTION}/items/{i}' for i in ids] +
                           [it['assets']['visual']['href'] for it in items],
            'licence': LICENCE_LANDSAT if landsat else LICENCE_S2.format(year=year),
            'grid': {'crs': 'local tmerc at bbox centre', 'pixel_m': PIXEL_M,
                     'size': [int(cls.shape[1]), int(cls.shape[0])]},
            'classes': cs,
        }
        if name in SAND_SAMPLES:
            areas[name]['classes']['sand'] = sand_sample(name, d)
    palette = {'generator': 'tools/island/topo/imagery.py', 'stac': STAC, 'collection': COLLECTION,
               'classes': CLASSES, 'notes': NOTES, 'areas': {k: areas[k] for k in AREAS if k in areas}}
    PALETTE.write_text(json.dumps(palette, indent=1, sort_keys=True) + '\n')
    summary(palette)


def summary(palette):
    print(f'\n{PALETTE.relative_to(ROOT)}: {PALETTE.stat().st_size} bytes')
    for name, a in palette['areas'].items():
        print(f"\n{name}  {a['scene_id']}  {a['datetime'][:10]}  scene cloud {a['cloud_cover']}%"
              f"  window excluded {100 * a['window_excluded_frac']:.1f}%")
        for c in CLASSES:
            s = a['classes'][c]
            if s['frac'] > 0.01:
                print(f"   {c:24s} {100 * s['frac']:5.1f}%  tci {s['tci_hex']}  albedo {s['albedo_hex']}"
                      f"  refl {s['refl_p50']}")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('areas', nargs='*', default=list(AREAS))
    ap.add_argument('--search', action='store_true')
    ap.add_argument('--refetch', action='store_true')
    a = ap.parse_args()
    for n in a.areas:
        if n not in AREAS:
            sys.exit(f'unknown area {n}')
    if a.search:
        do_search(a.areas)
    else:
        run(a.areas, a.refetch)


if __name__ == '__main__':
    os.environ.setdefault('PYTHONDONTWRITEBYTECODE', '1')
    main()
