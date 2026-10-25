"""Measure a published three-view drawing: the views, their bounding boxes, and a side-view profile.

  blender --background --python tools/measure_threeview.py -- <image> [view-index]

A three-view has no scale bar, so what it gives you is RATIOS -- span against length against height. Compare
those with the mesh's own bounds (glTF accessor min/max, which is exact) and the published figures. Two of
the three agreeing is the corroboration; a drawing alone is one person's draughting.

Run with no view index it segments the drawing and prints every view it found. Give it a view's index and it
flood-fills that one and prints its outline column by column -- the top and bottom line of the silhouette,
which is where a model that measures right still reads wrong.

**Segment by connected component, never by bounding rectangle.** On the 2-33A drawing the front view's wing
spans the full width and its rows overlap the side view's, so cropping the side view's rectangle pulled in
front-view ink and reported the fin reaching the top of the frame.

Blender runs it because Blender has numpy and can read any image format; nothing here touches the scene.
"""

import sys

import bpy
import numpy as np

INK = 0.80      # anything darker than this in all three channels is the drawing, not the paper


def load_ink(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    buf = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    rgb = buf.reshape(h, w, 4)[::-1][:, :, :3]      # Blender's origin is bottom-left
    return rgb.min(axis=2) < INK


def fill(ink, seed):
    """The connected component containing seed, 4-neighbour, iterative."""
    H, W = ink.shape
    todo = ink.copy()
    mask = np.zeros_like(ink)
    y, x = seed
    if not todo[y, x]:
        raise SystemExit("seed (%d,%d) is not on ink" % (x, y))
    todo[y, x] = False
    mask[y, x] = True
    stack = [(y, x)]
    while stack:
        y, x = stack.pop()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < H and 0 <= nx < W and todo[ny, nx]:
                todo[ny, nx] = False
                mask[ny, nx] = True
                stack.append((ny, nx))
    return mask


def components(ink, stride=3):
    """Every component, biggest first, as (pixels, x0, x1, y0, y1, seed). A stride of 3 skips
    seeding inside blobs already found; the fill itself is still exact."""
    H, W = ink.shape
    seen = ~ink
    out = []
    for y0 in range(0, H, stride):
        for x0 in range(0, W, stride):
            if seen[y0, x0]:
                continue
            n = 0
            minx = maxx = x0
            miny = maxy = y0
            stack = [(y0, x0)]
            seen[y0, x0] = True
            while stack:
                y, x = stack.pop()
                n += 1
                minx = min(minx, x); maxx = max(maxx, x)
                miny = min(miny, y); maxy = max(maxy, y)
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < H and 0 <= nx < W and not seen[ny, nx]:
                        seen[ny, nx] = True
                        stack.append((ny, nx))
            out.append((n, minx, maxx, miny, maxy, (y0, x0)))
    out.sort(reverse=True)
    return out


def seed_for(ink, box):
    """The middle of the widest ink run on a box's centre row -- the fuselage, in any view."""
    x0, x1, y0, y1 = box
    row = (y0 + y1) // 2
    best, run = [], []
    for c in list(range(x0, x1 + 1)) + [None]:
        if run and (c is None or not ink[row, c]):
            if len(run) > len(best):
                best = run
            run = []
        elif c is not None and ink[row, c]:
            run.append(c)
    if not best:
        raise SystemExit("no ink on the centre row of %s" % (box,))
    return row, best[len(best) // 2]


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    if not argv:
        raise SystemExit(__doc__)
    ink = load_ink(argv[0])
    comps = components(ink)
    print("image %dx%d" % (ink.shape[1], ink.shape[0]))
    for i, (n, a, b, c, d, _) in enumerate(comps[:6]):
        print("  [%d] px=%-8d x %4d..%4d (w %4d)   y %4d..%4d (h %4d)  ratio h/w %.4f"
              % (i, n, a, b, b - a + 1, c, d, d - c + 1, (d - c + 1) / (b - a + 1)))
    if len(argv) < 2:
        print("\npass a view index to profile that view column by column")
        return
    n, a, b, c, d, _ = comps[int(argv[1])]
    mask = fill(ink, seed_for(ink, (a, b, c, d)))
    ys, xs = np.nonzero(mask)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    sub = mask[y0:y1 + 1, x0:x1 + 1]
    Wv, Hv = x1 - x0 + 1, y1 - y0 + 1
    print("\nview %s: x %d..%d (w %d)  y %d..%d (h %d)" % (argv[1], x0, x1, Wv, y0, y1, Hv))
    cols = []
    for col in range(Wv):
        r = np.nonzero(sub[:, col])[0]
        cols.append((int(r.min()), int(r.max())) if len(r) else (None, None))
    print("frac   col    top  bot  depth      (row 0 = top of the view)")
    for f in [0.0, 0.03, 0.06, 0.10, 0.15, 0.25, 0.35, 0.50, 0.65, 0.75, 0.85, 0.92, 0.96, 1.0]:
        col = int(f * (Wv - 1))
        t, bo = cols[col]
        print("%5.2f  %4d   %s" % (f, col, "--" if t is None
                                   else "top=%4d bot=%4d depth=%4d" % (t, bo, bo - t + 1)))
    tops = [(cols[c][0], c) for c in range(Wv) if cols[c][0] is not None]
    bots = [(cols[c][1], c) for c in range(Wv) if cols[c][1] is not None]
    t, ct = min(tops)
    bo, cb = max(bots)
    print("highest point: col %d (frac %.3f), row %d" % (ct, ct / (Wv - 1), t))
    print("lowest  point: col %d (frac %.3f), row %d" % (cb, cb / (Wv - 1), bo))


main()
