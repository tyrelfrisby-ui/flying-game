"""Measure a published three-view drawing: the views, their bounding boxes, and a side-view profile.

  blender --background --python tools/measure_threeview.py -- <image> [view-index]
          [--box LEFT,TOP,RIGHT,BOTTOM] [--width-m METRES] [--steps N] [--no-fill]

A three-view has no scale bar, so what it gives you is RATIOS -- span against length against height. Compare
those with the mesh's own bounds (glTF accessor min/max, which is exact) and the published figures. Two of
the three agreeing is the corroboration; a drawing alone is one person's draughting.

Run with no view index it segments the drawing and prints every view it found. Give it a view's index and it
flood-fills that one and prints its outline column by column -- the top and bottom line of the silhouette,
which is where a model that measures right still reads wrong.

`--box` throws away every pixel outside a rectangle, given the way you read the image: origin at the TOP
left, right and bottom exclusive. Reach for it when one view's component swallows another -- a factory sheet
carries dimension lines between its views, and a leader line that touches two outlines welds them into one
component (the PA-44 sheet merges its front and side views exactly so). A box drawn through white paper
between the views separates them without touching the outlines.

`--width-m` is the view's full width in metres -- span for a top or front view, length for a side view, read
off the drawing's own dimension line. The profile then prints in metres from the view's top left, which is
what you type into a model, instead of pixels, which are nothing.

`--no-fill` profiles everything inside the box instead of one flood-filled component, and `--steps N` sets
how many stations it prints. On line art that is usually what you want: the first and last ink in a column is
the silhouette whether or not the strokes join.

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


# Eight neighbours, not four. A factory sheet is line art, and a one-pixel diagonal stroke is not
# 4-connected -- the PA-44's side view came apart into a dozen pieces under 4-neighbour, none of them the
# aeroplane. The 2-33's colour drawing has filled areas and reads the same either way.
NEIGHBOURS = [(dy, dx) for dy in (-1, 0, 1) for dx in (-1, 0, 1) if dy or dx]


def fill(ink, seed):
    """The connected component containing seed, 8-neighbour, iterative."""
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
        for dy, dx in NEIGHBOURS:
            ny, nx = y + dy, x + dx
            if 0 <= ny < H and 0 <= nx < W and todo[ny, nx]:
                todo[ny, nx] = False
                mask[ny, nx] = True
                stack.append((ny, nx))
    return mask


def components(ink, stride=3):
    """Every component, biggest first, as (pixels, x0, x1, y0, y1, seed), 8-neighbour. A stride of 3 skips
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
                for dy, dx in NEIGHBOURS:
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


def opt(argv, name, cast=str):
    """--name VALUE, removed from argv."""
    if name not in argv:
        return None
    i = argv.index(name)
    v = argv[i + 1]
    del argv[i:i + 2]
    return cast(v)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    if not argv:
        raise SystemExit(__doc__)
    box = opt(argv, "--box", lambda v: [int(t) for t in v.split(",")])
    width_m = opt(argv, "--width-m", float)
    steps = opt(argv, "--steps", int)
    no_fill = "--no-fill" in argv
    if no_fill:
        argv.remove("--no-fill")
    ink = load_ink(argv[0])
    if box:
        # load_ink already returns the image the way it is read: row 0 is the top.
        left, top, right, bottom = box
        keep = np.zeros_like(ink)
        keep[top:bottom, left:right] = True
        ink = ink & keep
        print("box: x %d..%d, y %d..%d from the top -- %d ink pixels kept"
              % (left, right, top, bottom, int(ink.sum())))
    print("image %dx%d" % (ink.shape[1], ink.shape[0]))
    if no_fill:
        # Profile whatever is left in the box, outline by outline. A factory sheet's line art comes apart
        # under any flood fill -- the PA-44's side view breaks into a dozen strokes, and its dimension
        # leaders weld the views it does not break. The first and last ink in a column is the silhouette
        # whether or not the strokes touch, so a box drawn clear of the dimension lines needs no fill.
        mask = ink
    else:
        comps = components(ink)
        for i, (n, a, b, c, d, _) in enumerate(comps[:6]):
            print("  [%d] px=%-8d x %4d..%4d (w %4d)   y %4d..%4d (h %4d)  ratio h/w %.4f"
                  % (i, n, a, b, b - a + 1, c, d, d - c + 1, (d - c + 1) / (b - a + 1)))
        if len(argv) < 2:
            print("\npass a view index to profile that view column by column, or --no-fill to profile the box")
            return
        n, a, b, c, d, _ = comps[int(argv[1])]
        mask = fill(ink, seed_for(ink, (a, b, c, d)))
    ys, xs = np.nonzero(mask)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    sub = mask[y0:y1 + 1, x0:x1 + 1]
    Wv, Hv = x1 - x0 + 1, y1 - y0 + 1
    label = "box" if no_fill else argv[1]
    print("\nview %s: x %d..%d (w %d)  y %d..%d (h %d)" % (label, x0, x1, Wv, y0, y1, Hv))
    cols = []
    for col in range(Wv):
        r = np.nonzero(sub[:, col])[0]
        cols.append((int(r.min()), int(r.max())) if len(r) else (None, None))
    k = width_m / Wv if width_m else None
    unit = "m" if k else "px"
    if k:
        print("scale: %.5f m/px -- the view is %.3f m tall" % (k, Hv * k))
    print("frac   %-7s %s      (0 = the view's top left)" % ("across", unit))
    fracs = ([i / (steps - 1.0) for i in range(steps)] if steps else
             [0.0, 0.03, 0.06, 0.10, 0.15, 0.25, 0.35, 0.50, 0.65, 0.75, 0.85, 0.92, 0.96, 1.0])
    for f in fracs:
        col = int(f * (Wv - 1))
        t, bo = cols[col]
        if t is None:
            print("%5.2f  %7s --" % (f, "%.3f" % (col * k) if k else col))
            continue
        vals = (t * k, bo * k, (bo - t + 1) * k) if k else (t, bo, bo - t + 1)
        fmt = "top=%7.3f bot=%7.3f depth=%7.3f" if k else "top=%4d bot=%4d depth=%4d"
        print("%5.2f  %7s %s" % (f, "%.3f" % (col * k) if k else col, fmt % vals))
    tops = [(cols[c][0], c) for c in range(Wv) if cols[c][0] is not None]
    bots = [(cols[c][1], c) for c in range(Wv) if cols[c][1] is not None]
    t, ct = min(tops)
    bo, cb = max(bots)
    if k:
        print("highest point: %.3f m across (frac %.3f), %.3f m down" % (ct * k, ct / (Wv - 1), t * k))
        print("lowest  point: %.3f m across (frac %.3f), %.3f m down" % (cb * k, cb / (Wv - 1), bo * k))
    else:
        print("highest point: col %d (frac %.3f), row %d" % (ct, ct / (Wv - 1), t))
        print("lowest  point: col %d (frac %.3f), row %d" % (cb, cb / (Wv - 1), bo))


main()
