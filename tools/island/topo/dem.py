"""Real topography as inspiration for six island areas (lane/claude-island-terrain).

Downloads public elevation tiles (AWS Open Data "Terrain Tiles", Terrarium encoding: USGS 3DEP / NED in the
US incl. Alaska and Hawaii, SRTM elsewhere; public domain / free use, attribution "Mapzen, USGS 3DEP"),
and turns each real place into a compacted TEMPLATE on the island's frame:

    island point (e, n) km  ->  real point = anchor_real + R(-rot) * (e - e_a, n - n_a) * c
    template height (m)     =  (z_real - z_ref) / c + z_island

The SAME factor c is applied horizontally and vertically, so every slope, every valley width/depth ratio and the
shape of the hypsometric curve are those of the real place; only its size changes. The generator
(tools/island/TerrainGen/Topo.cs) blends these templates into its macro surface UNDER every authored constraint
(pads, lakes, clearance surfaces, the coast), then erodes on top.

Raw tiles go to build/topo-ref/dem/ (git-ignored). The committed artifacts are small and deterministic:
    tools/island/topo/templates.json   per-area placement, scale, statistics
    tools/island/topo/templates.bin    int16 little-endian, decimetres, 50 m island lattice, row-major south->north
Run:  /private/tmp/topo-venv/bin/python -I tools/island/topo/dem.py [--plates]
Needs numpy + pillow (and network on first run). Re-running with the tiles cached is byte-identical.
"""
import io, json, math, os, sys, urllib.request
import numpy as np
from PIL import Image

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
CACHE = os.path.join(REPO, 'build', 'topo-ref', 'dem')
OUT = os.path.dirname(os.path.abspath(__file__))
TILE_URL = 'https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png'
CELL_KM = 0.05

# anchor_real (lat, lon) maps to anchor_island (e, n) km. rot: island bearing = real bearing + rot (deg).
# c: compaction (horizontal AND vertical). z_ref -> z_island: the height datum match. window: island km.
AREAS = [
    dict(id='fairweather', name='Mount Fairweather, AK', real=(58.9064, -137.5264), island=(-0.5, 6.3), rot=-90, c=2.6,
         z_ref=560.0, z_island=0.0, window=(-4.4, 1.6, 4.0, 7.9), zoom=12,
         why='summit 1.2 km from the north coast, in the one place outside the A2 swath and the PHDH 8 visual surface where 1,600 m can stand; turned so the real NW valley-glacier system runs to the Ruth/Fairweather ski zone; the 25 km piedmont foreland is cut out, which the 560 m datum drop stands for'),
    dict(id='knik', name='Knik Glacier and Knik River, AK', real=(61.4150, -148.5750), island=(-4.6, -0.9), rot=-18, c=4.0,
         z_ref=40.0, z_island=30.0, window=(-7.6, -2.6, -2.4, 1.4), zoom=12,
         why='glacier face at the head of canyon C4; the braided valley runs WNW down C4 to the Knik ice lake'),
    dict(id='sitka', name='Sitka, AK (Baranof shore, Mt Verstovia, Indian River)', real=(57.0530, -135.3350), island=(-10.9, 2.4), rot=45, c=2.2,
         z_ref=0.0, z_island=0.0, window=(-11.6, -5.4, 0.7, 4.7), zoom=12,
         why='Sitka town shore on the island\'s west coast, Baranof mountains rising behind it'),
    dict(id='grandcanyon', name='Grand Canyon, AZ (Bright Angel / Phantom Ranch)', real=(36.1005, -112.0950), island=(5.0, -2.5), rot=180, c=5.0,
         z_ref=2150.0, z_island=1330.0, window=(3.0, 8.4, -4.9, -0.2), zoom=11,
         why='inner gorge E-W across the red-rock plateau; South Rim level matched to the plateau strips (KPGA 4,317 ft)'),
    dict(id='dillingham', name='Dillingham Airfield and the north Waianae ridge, Oahu', real=(21.5795, -158.1970), island=(4.99, 5.9), rot=12, c=1.0,
         z_ref=0.0, z_island=0.0, window=(2.8, 8.6, 2.4, 6.9), zoom=13,
         why='PHDH runway on PHDH, the Mokuleia ridge behind it to the south'),
    dict(id='johnsoncreek', name='Johnson Creek (3U2) and Yellow Pine, Frank Church, ID', real=(44.9116, -115.4859), island=(1.82, -0.76), rot=9, c=2.0,
         z_ref=1504.0, z_island=1512.0, window=(-0.2, 3.1, -2.7, 1.3), zoom=12,
         why='3U2 strip on 3U2; Johnson Creek valley along canyon C1'),
]


def tile_xy(lat, lon, z):
    n = 2 ** z
    x = (lon + 180) / 360 * n
    y = (1 - np.arcsinh(np.tan(np.radians(lat))) / math.pi) / 2 * n
    return x, y


def fetch(z, x, y):
    path = os.path.join(CACHE, str(z), str(x), f'{y}.png')
    if not os.path.exists(path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with urllib.request.urlopen(TILE_URL.format(z=z, x=x, y=y), timeout=60) as r:
            data = r.read()
        with open(path, 'wb') as f:
            f.write(data)
    a = np.asarray(Image.open(path).convert('RGB'), dtype=np.float64)
    return a[..., 0] * 256 + a[..., 1] + a[..., 2] / 256 - 32768


class Mosaic:
    def __init__(self, z, lat0, lat1, lon0, lon1):
        x0, y1 = tile_xy(lat0, lon0, z); x1, y0 = tile_xy(lat1, lon1, z)
        self.z, self.tx0, self.ty0 = z, int(x0) - 1, int(y0) - 1
        tx1, ty1 = int(x1) + 1, int(y1) + 1
        rows = [np.hstack([fetch(z, tx, ty) for tx in range(self.tx0, tx1 + 1)]) for ty in range(self.ty0, ty1 + 1)]
        self.a = np.vstack(rows)

    def sample(self, lat, lon):
        x, y = tile_xy(lat, lon, self.z)
        px, py = (x - self.tx0) * 256 - .5, (y - self.ty0) * 256 - .5
        i, j = np.floor(px).astype(int), np.floor(py).astype(int); fx, fy = px - i, py - j
        a = self.a
        return (a[j, i] * (1 - fx) + a[j, i + 1] * fx) * (1 - fy) + (a[j + 1, i] * (1 - fx) + a[j + 1, i + 1] * fx) * fy


def to_real(area, e, n):
    """Island km -> real (lat, lon)."""
    c, r = area['c'], math.radians(-area['rot'])
    de, dn = (e - area['island'][0]) * c, (n - area['island'][1]) * c
    re, rn = de * math.cos(r) + dn * math.sin(r), -de * math.sin(r) + dn * math.cos(r)
    lat0, lon0 = area['real']
    return lat0 + rn / 110.574, lon0 + re / (111.320 * math.cos(math.radians(lat0)))


def slopes(z, cell_m):
    gy, gx = np.gradient(z, cell_m)
    return np.degrees(np.arctan(np.hypot(gx, gy)))


def build(area):
    e0, e1, n0, n1 = area['window']
    w, h = int(round((e1 - e0) / CELL_KM)) + 1, int(round((n1 - n0) / CELL_KM)) + 1
    E, N = np.meshgrid(e0 + np.arange(w) * CELL_KM, n0 + np.arange(h) * CELL_KM)
    lat, lon = to_real(area, E, N)
    m = Mosaic(area['zoom'], lat.min() - .01, lat.max() + .01, lon.min() - .02, lon.max() + .02)
    zr = m.sample(lat, lon)
    sea = zr < 1.0
    zt = (zr - area['z_ref']) / area['c'] + area['z_island']
    zt[sea] = -999.9  # real sea: the generator uses its own low ground there
    land = ~sea
    sr = slopes(zr, CELL_KM * 1000 * area['c'])  # real slope at the template's real sample spacing
    st = slopes(np.where(sea, area['z_island'], zt), CELL_KM * 1000)
    q = lambda a, p: float(np.percentile(a[land], p)) if land.any() else 0.0
    stats = dict(
        real_relief_m=[round(q(zr, 1), 1), round(q(zr, 99.9), 1)],
        template_relief_m=[round(q(zt, 1), 1), round(q(zt, 99.9), 1)],
        real_slope_deg_p50_p90=[round(q(sr, 50), 1), round(q(sr, 90), 1)],
        template_slope_deg_p50_p90=[round(q(st, 50), 1), round(q(st, 90), 1)],
        real_sample_m=round(CELL_KM * 1000 * area['c'], 1),
        land_fraction=round(float(land.mean()), 3))
    return dict(w=w, h=h, z=zt, zr=zr, sea=sea, stats=stats, lat=lat, lon=lon)


def hillshade(z, cell_m, az=315, alt=45):
    gy, gx = np.gradient(z, cell_m)
    slope = np.arctan(np.hypot(gx, gy)); aspect = np.arctan2(-gx, gy)
    a, b = math.radians(alt), math.radians(az)
    hs = np.sin(a) * np.cos(slope) + np.cos(a) * np.sin(slope) * np.cos(b - aspect)
    return np.clip(hs, 0, 1)


def main():
    plates = '--plates' in sys.argv
    manifest, blob = [], bytearray()
    for area in AREAS:
        r = build(area)
        q = np.round(np.where(r['sea'], -32768, r['z'] * 10)).astype('<i2')
        entry = {k: area[k] for k in ('id', 'name', 'real', 'island', 'rot', 'c', 'z_ref', 'z_island', 'window', 'zoom', 'why')}
        entry.update(cell_km=CELL_KM, w=r['w'], h=r['h'], offset=len(blob) // 2, stats=r['stats'])
        manifest.append(entry); blob += q.tobytes()
        print(f"{area['id']:13s} {r['w']}x{r['h']}  real {r['stats']['real_relief_m']} m -> template {r['stats']['template_relief_m']} m;"
              f" slope p50/p90 real {r['stats']['real_slope_deg_p50_p90']} template {r['stats']['template_slope_deg_p50_p90']}")
        if plates:
            d = os.path.join(REPO, 'build', 'topo-ref', 'plates'); os.makedirs(d, exist_ok=True)
            hs = hillshade(np.where(r['sea'], 0, r['zr']), CELL_KM * 1000 * area['c'])
            rgb = np.dstack([hs * 235 + 20] * 3)
            rgb[r['sea']] = (40, 70, 110)
            Image.fromarray(np.flipud(rgb).astype(np.uint8)).resize((r['w'] * 4, r['h'] * 4), Image.LANCZOS).save(os.path.join(d, f"{area['id']}-real-hillshade-island-frame.png"))
    with open(os.path.join(OUT, 'templates.bin'), 'wb') as f:
        f.write(bytes(blob))
    doc = dict(source='AWS Terrain Tiles (Terrarium), ' + TILE_URL + '; elevation sources USGS 3DEP/NED (US, incl. AK and HI), SRTM; public domain, attribution: Mapzen / USGS',
               transform='real = anchor_real + R(-rot)*(island - anchor_island)*c ; z = (z_real - z_ref)/c + z_island ; sea = -32768',
               encoding='int16 LE decimetres, row-major, rows south to north, 50 m island lattice', areas=manifest)
    with open(os.path.join(OUT, 'templates.json'), 'w') as f:
        json.dump(doc, f, indent=1)
        f.write('\n')
    print(f'templates.bin {len(blob)} bytes')


if __name__ == '__main__':
    main()
