"""One-time t933 texture migration; preserve existing glyph/tick/redline pixels.

Usage: /usr/bin/python3 tools/own_models/thin_engine_bands.py <original-git-ref>
The explicit baseline prevents repeated thinning on reruns. Updated source generators
use half-width arcs for future redraws; this migration avoids unrelated raster drift.
"""
import io
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]


def thin(image):
    pixels = np.array(image.convert('RGB'))
    rgb = pixels.astype(int)
    r, g, b = (rgb[:, :, i] for i in range(3))
    green = (g > r * 1.25) & (g > b * 1.25) & (g > 100)
    yellow = (r > 140) & (g > 100) & (b < 90) & (r < g * 1.8)
    yy, xx = np.indices(r.shape)
    radius = np.hypot(xx - image.width / 2, yy - image.height / 2)
    colors, counts = np.unique(pixels.reshape(-1, 3), axis=0, return_counts=True)
    background = colors[counts.argmax()]
    for mask in (green, yellow):
        if not np.any(mask):
            continue
        assert radius[mask].min() > min(image.size) * .35, 'not a dedicated radial operating band'
        midpoint = (radius[mask].min() + radius[mask].max()) / 2
        pixels[mask & (radius < midpoint)] = background
    # This pass writes exclusively green/yellow band pixels. Glyphs and redlines cannot change.
    assert np.array_equal(pixels[~(green | yellow)], rgb[~(green | yellow)])
    return Image.fromarray(pixels)


def main():
    baseline = sys.argv[1]
    paths = [p for p in (ROOT / 'tools/own_models/art').glob('*-dial-*.png') if p.name.startswith(('ea-', 'gl-'))]
    paths += list((ROOT / 'unity/Assets/_Project/Resources/CockpitDials').glob('ea-dial-*.png'))
    for path in paths:
        rel = path.relative_to(ROOT)
        raw = subprocess.check_output(['git', 'show', baseline + ':' + str(rel)], cwd=ROOT)
        before = Image.open(io.BytesIO(raw)).convert('RGB')
        after = thin(before)
        if np.array_equal(np.array(before), np.array(after)):
            path.write_bytes(raw)
        else:
            after.save(path)
            print(rel)


if __name__ == '__main__':
    main()
