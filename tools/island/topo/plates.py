"""Imagery-vs-render comparison plates for the six real-topography areas (lane/claude-island-terrain).

Each plate is 2 x 2, all four panels in the ISLAND frame of the area's template window (north up, same km grid):
    real satellite image (Sentinel-2, imagery.py)  |  the island, rendered straight down in Unity (WorldRender)
    real DEM hillshade (dem.py)                     |  the island's relief hillshade, glacier ice tinted
The real panels are the real place moved into the island frame by the template's own transform (rotation, and
compaction c), so like is beside like. Inputs are all under build/ (git-ignored); nothing here is committed
output. Run after dem.py, imagery.py and the Unity render:
    /private/tmp/topo-venv/bin/python -I tools/island/topo/plates.py build/island-terrain/after out_dir
"""
import base64, json, math, os, re, struct, sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
sys.path.insert(0, HERE)
import dem  # noqa: E402

PX_KM = 120  # panel resolution, pixels per island km


def relief():
    """Main-island lattice heights and ice fraction from IslandRelief.Generated.cs (same format as the runtime)."""
    text = open(os.path.join(REPO, 'src/FlyingGame.Core/IslandRelief.Generated.cs')).read()
    body = text[text.index('Base64 ='):]
    data = base64.b64decode(''.join(re.findall(r'"([A-Za-z0-9+/=]*)"', body)))
    o = 4

    def v():
        nonlocal o
        r = s = 0
        while True:
            b = data[o]; o += 1; r |= (b & 0x7f) << s
            if b < 0x80:
                return r
            s += 7

    def d():
        nonlocal o
        x = struct.unpack_from('<d', data, o)[0]; o += 8; return x
    count = v(); out = []
    for _ in range(count):
        e0, n0, cell = d(), d(), d(); w, h = v(), v(); size = w * h
        z = np.empty(size, np.int64)
        raw = np.empty(size, np.int64)
        for k in range(size):
            u = v(); raw[k] = (u >> 1) ^ -(u & 1)
        q = raw.reshape(h, w)
        # planar predictor: q[j,i] = raw + q[j,i-1] + q[j-1,i] - q[j-1,i-1]
        z = np.zeros((h, w), np.int64)
        for j in range(h):
            for i in range(w):
                pred = z[j, i-1] + z[j-1, i] - z[j-1, i-1] if i and j else z[j, i-1] if i else z[j-1, i] if j else 0
                z[j, i] = pred + q[j, i]
        chans = []
        for _c in range(4):
            a = np.zeros(size, np.uint8); k = 0
            while k < size:
                val = data[o]; o += 1; run = v(); a[k:k + run] = val; k += run
            chans.append(a.reshape(h, w))
        out.append(dict(e0=e0, n0=n0, cell=cell, w=w, h=h, z=z * .05 - 30, ice=chans[0] / 255.0))
        break  # the main island is the first lattice
    return out[0]


def hillshade(z, cell_m):
    return dem.hillshade(z, cell_m)


def panel_from_function(window, f):
    e0, e1, n0, n1 = window
    W, H = int((e1 - e0) * PX_KM), int((n1 - n0) * PX_KM)
    E, N = np.meshgrid(e0 + (np.arange(W) + .5) / PX_KM, n1 - (np.arange(H) + .5) / PX_KM)
    return f(E, N)


def satellite_panel(area, window, pal):
    import rasterio.warp
    from rasterio.crs import CRS
    path = os.path.join(REPO, 'build/topo-ref/imagery', area['id'] + '.png')
    img = np.asarray(Image.open(path).convert('RGB'))
    bbox = pal['bbox']; lon0, lat0 = (bbox[0] + bbox[2]) / 2, (bbox[1] + bbox[3]) / 2
    crs = CRS.from_proj4(f'+proj=tmerc +lat_0={lat0} +lon_0={lon0} +k=1 +x_0=0 +y_0=0 +datum=WGS84 +units=m +no_defs')
    x0, y0, x1, y1 = rasterio.warp.transform_bounds('EPSG:4326', crs, *bbox, densify_pts=21)
    x0, y1 = math.floor(x0 / 10) * 10, math.ceil(y1 / 10) * 10

    def f(E, N):
        lat, lon = dem.to_real(area, E, N)
        xs, ys = rasterio.warp.transform('EPSG:4326', crs, lon.ravel().tolist(), lat.ravel().tolist())
        c = ((np.array(xs) - x0) / 10).astype(int).reshape(E.shape); r = ((y1 - np.array(ys)) / 10).astype(int).reshape(E.shape)
        ok = (c >= 0) & (r >= 0) & (c < img.shape[1]) & (r < img.shape[0])
        out = np.zeros(E.shape + (3,), np.uint8); out[...] = (25, 25, 25)
        out[ok] = img[r[ok], c[ok]]
        return out
    return panel_from_function(window, f)


def real_hillshade_panel(area, window):
    e0, e1, n0, n1 = window
    W, H = int((e1 - e0) * PX_KM), int((n1 - n0) * PX_KM)
    E, N = np.meshgrid(e0 + (np.arange(W) + .5) / PX_KM, n1 - (np.arange(H) + .5) / PX_KM)
    lat, lon = dem.to_real(area, E, N)
    m = dem.Mosaic(area['zoom'], lat.min() - .01, lat.max() + .01, lon.min() - .02, lon.max() + .02)
    zr = m.sample(lat, lon)
    hs = hillshade(np.flipud(zr), 1000 * area['c'] / PX_KM); hs = np.flipud(hs)
    t = np.clip((zr - np.percentile(zr, 1)) / (np.percentile(zr, 99.5) - np.percentile(zr, 1) + 1e-9), 0, 1)
    col = np.dstack([70 + 140 * t, 110 + 90 * t, 70 + 120 * t]) * (.35 + .75 * hs[..., None])
    col[zr < 1] = (40, 70, 110)
    return np.clip(col, 0, 255).astype(np.uint8)


def island_panel(R, window):
    def f(E, N):
        i = ((E - R['e0']) / R['cell']).round().astype(int); j = ((N - R['n0']) / R['cell']).round().astype(int)
        return i, j
    e0, e1, n0, n1 = window
    i0, i1 = int((e0 - R['e0']) / R['cell']), int((e1 - R['e0']) / R['cell'])
    j0, j1 = int((n0 - R['n0']) / R['cell']), int((n1 - R['n0']) / R['cell'])
    z = R['z'][j0:j1, i0:i1]; ice = R['ice'][j0:j1, i0:i1]
    hs = hillshade(z, R['cell'] * 1000)
    t = np.clip(z / 2000, 0, 1)
    col = np.dstack([70 + 140 * t, 110 + 90 * t, 70 + 120 * t]) * (.35 + .75 * hs[..., None])
    col = col * (1 - ice[..., None] * .8) + np.array([205, 225, 250]) * (.45 + .6 * hs[..., None]) * ice[..., None] * .8
    col[z < .6] = (40, 70, 110)
    im = Image.fromarray(np.flipud(np.clip(col, 0, 255)).astype(np.uint8))
    return np.asarray(im.resize((int((e1 - e0) * PX_KM), int((n1 - n0) * PX_KM)), Image.BILINEAR))


def render_panel(render_dir, area_id, window):
    e0, e1, n0, n1 = window
    if not os.path.exists(os.path.join(render_dir, 'plates.txt')):
        return np.full((int((n1 - n0) * PX_KM), int((e1 - e0) * PX_KM), 3), 60, np.uint8)
    rows = [l.split('\t') for l in open(os.path.join(render_dir, 'plates.txt')).read().splitlines()[1:] if l]
    row = next(r for r in rows if r[0] == area_id)
    up, fov, w, h = float(row[5]), float(row[6]), int(row[7]), int(row[8])
    img = Image.open(os.path.join(render_dir, f'plate-{area_id}-topdown.png')).convert('RGB')
    km_per_px = 2 * up * math.tan(math.radians(fov) / 2) / 1000 / h  # at sea level
    e0, e1, n0, n1 = window
    cx, cy = w / 2, h / 2
    box = (cx - (e1 - e0) / 2 / km_per_px, cy - (n1 - n0) / 2 / km_per_px, cx + (e1 - e0) / 2 / km_per_px, cy + (n1 - n0) / 2 / km_per_px)
    return np.asarray(img.crop(tuple(int(round(b)) for b in box)).resize((int((e1 - e0) * PX_KM), int((n1 - n0) * PX_KM)), Image.LANCZOS))


def grid(im, window):
    d = ImageDraw.Draw(im); e0, e1, n0, n1 = window
    for e in range(math.ceil(e0), math.floor(e1) + 1):
        x = (e - e0) * PX_KM; d.line([(x, 0), (x, im.height)], fill=(255, 255, 255)); d.text((x + 2, 2), f'{e}', fill=(255, 255, 0))
    for n in range(math.ceil(n0), math.floor(n1) + 1):
        y = (n1 - n) * PX_KM; d.line([(0, y), (im.width, y)], fill=(255, 255, 255)); d.text((2, y + 2), f'{n}', fill=(255, 255, 0))
    return im


def main():
    render_dir, out_dir = sys.argv[1], sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    tpl = json.load(open(os.path.join(HERE, 'templates.json')))['areas']
    pal = json.load(open(os.path.join(HERE, 'palette.json')))['areas']
    R = relief()
    for area in tpl:
        win = tuple(area['window'])
        panels = [('Sentinel-2 (real, moved into the island frame)', satellite_panel(area, win, pal[area['id']])),
                  ('Unity render, straight down', render_panel(render_dir, area['id'], win)),
                  (f"real DEM hillshade (c = {area['c']}, rot {area['rot']} deg)", real_hillshade_panel(area, win)),
                  ('island relief, glacier ice tinted', island_panel(R, win))]
        w, h = panels[0][1].shape[1], panels[0][1].shape[0]
        sheet = Image.new('RGB', (2 * w + 30, 2 * h + 90), (16, 18, 22)); d = ImageDraw.Draw(sheet)
        d.text((10, 6), f"{area['name']}  ->  island window E {win[0]}..{win[1]} km, N {win[2]}..{win[3]} km;"
               f" compaction {area['c']} (horizontal AND vertical), rotation {area['rot']} deg, datum {area['z_ref']} m -> {area['z_island']} m", fill=(230, 230, 230))
        for k, (title, a) in enumerate(panels):
            x, y = 10 + (k % 2) * (w + 10), 30 + (k // 2) * (h + 30)
            im = grid(Image.fromarray(np.ascontiguousarray(a[:h, :w])), win)
            sheet.paste(im, (x, y)); d.text((x, y + h + 4), title, fill=(230, 230, 230))
        sheet.save(os.path.join(out_dir, f"comparison-{area['id']}.png"))
        print('wrote', area['id'])


if __name__ == '__main__':
    main()
