"""Paint the Talk Flying livery onto the downloaded models, for the podcast.

    blender --background --python tools/talkflying_livery.py -- [id ...]

    ids: pa18-cub-like  c172-like  stearman-pt17-like  boeing-737-like   (default: all four)

Writes build/talkflying/<id>-talkflying.glb and three renders per aeroplane. Nothing here touches the game;
these are podcast assets.

LICENCE. All four models are CC BY 4.0 -- commercial use allowed, ATTRIBUTION REQUIRED. The credit lines are
in assets-incoming/models/CREDITS.md and are reprinted at the end of a run, because a repaint is a derivative
work and the credit travels with it wherever the render goes.

HOW THE MARKINGS GET ON, which is the part worth explaining. A wordmark has to be painted INTO the model's
own texture, not floated over the skin as a quad -- floating quads were tried on the generated airframes and
the owner's verdict was "these look terrible". But a downloaded model's UV layout is somebody else's and
nothing says which texel is the top of the left wing.

So the decal is defined IN SPACE and found through the mesh:

    for every triangle:  is it inside this decal's box, and facing the right way?
                         if so, rasterise ITS UV triangle and, for each texel,
                         interpolate back to the 3-D point, project that onto the decal's plane,
                         and sample the stamp there.

That needs no knowledge of the UV layout at all -- it reads it. Paint lands on the surface that is actually
there, follows compound curvature for free, and a wing that is split across four texture atlases is painted
in all four without anyone saying so.

The stamps themselves are rendered from Blender text objects (there is no image library on this Mac), and
the logo is the project's own PNG.
"""
import bpy, bmesh, os, sys, math, shutil
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

PROBE = os.environ.get("TF_PROBE") == "1"      # print an x/z map of what a scrub changed
REPO = "/Users/tyfrisby/Documents/flying-game"
OUT = os.path.join(REPO, "build/talkflying")
LOGO = os.path.join(REPO, "unity/Assets/_Project/Resources/Decals/talk-flying-logo.png")
SRC = os.path.join(REPO, "assets-incoming/models")

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

# Talk Flying is a monochrome mark, so the aeroplanes are trimmed in graphite rather than a colour.
GRAPHITE = (0.247, 0.263, 0.298)
INK = (0.055, 0.060, 0.070)          # near-black, for marks on a light skin
SILVER = (0.760, 0.775, 0.800)


# =============================================================================================================
# stamps: a wordmark or the logo as an RGBA image, rendered once and reused
# =============================================================================================================
_stamps = {}
_font = []

# The wordmark in talk-flying-logo.png is a high-contrast engraved serif: thick stems, hairline crossbars,
# sharp flared serifs. Bodoni is that face; Didot is the same idea with finer hairlines, and the rest are
# only here so a machine without them still produces something.
FONTS = ["/System/Library/Fonts/Supplemental/Bodoni 72.ttc",
         "/System/Library/Fonts/Supplemental/Didot.ttc",
         "/System/Library/Fonts/Supplemental/Baskerville.ttc",
         "/System/Library/Fonts/Supplemental/Times New Roman Bold.ttf"]


def brand_font():
    """The logo's typeface, re-fetched every time.

    Caching the datablock does not survive: text_stamp makes a scene per word and deletes it afterwards,
    and Blender then garbage-collects the font along with the last object using it. The second word came
    back as "StructRNA of type VectorFont has been removed". Keep the NAME, not the pointer.
    """
    if _font and _font[0] in bpy.data.fonts:
        return bpy.data.fonts[_font[0]]
    for path in FONTS:
        if not os.path.exists(path):
            continue
        try:
            f = bpy.data.fonts.load(path)
        except RuntimeError:
            continue
        f.use_fake_user = True          # nothing else references it between words
        if not _font:
            print("LIVERY   font: %s" % os.path.basename(path))
        _font[:] = [f.name]
        return f
    if not _font:
        print("LIVERY   font: none of the brand faces are installed; using Blender's own")
        _font[:] = [None]
    return None


def text_stamp(text, slant=0.0, width=1024, bold=True):
    """Render a word to a transparent image. Blender's own font, sheared for an N-number's slant."""
    key = ("t", text, slant, width, bold)
    if key in _stamps:
        return _stamps[key]
    sc = bpy.data.scenes.new("stamp")
    prev = bpy.context.window.scene
    bpy.context.window.scene = sc
    cu = bpy.data.curves.new("T", type='FONT')
    cu.body = text
    cu.align_x = 'CENTER'
    cu.align_y = 'CENTER'
    cu.shear = slant
    f = brand_font()
    if f is not None:
        cu.font = f
    if bold:
        # a whisker of bevel, not the 0.012 a sans wanted: on a high-contrast serif that much fills the
        # hairlines in and throws away the very thing that makes the face recognisable
        cu.bevel_depth = 0.004
        cu.resolution_u = 4
    ob = bpy.data.objects.new("T", cu)
    sc.collection.objects.link(ob)
    bpy.context.view_layer.update()
    mat = bpy.data.materials.new("TM")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs[0].default_value = (1, 1, 1, 1)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(em.outputs[0], out.inputs[0])
    cu.materials.append(mat)

    # frame it: the camera is sized to the text's own bounding box plus a small margin
    bb = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    w = max(p.x for p in bb) - min(p.x for p in bb)
    h = max(p.y for p in bb) - min(p.y for p in bb)
    if w <= 0 or h <= 0:
        w = h = 1.0
    cx = (max(p.x for p in bb) + min(p.x for p in bb)) * 0.5
    cy = (max(p.y for p in bb) + min(p.y for p in bb)) * 0.5
    pad = 1.10
    cam = bpy.data.objects.new("C", bpy.data.cameras.new("C"))
    sc.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = w * pad
    cam.location = (cx, cy, 5)
    sc.camera = cam
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.film_transparent = True
    sc.render.resolution_x = width
    sc.render.resolution_y = max(8, int(width * h * pad / (w * pad)))
    path = os.path.join(OUT, "_stamp_%s.png" % "".join(ch for ch in text if ch.isalnum()) or "x")
    sc.render.filepath = path
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    bpy.ops.render.render(write_still=True)
    bpy.context.window.scene = prev
    bpy.data.scenes.remove(sc)
    img = bpy.data.images.load(path)
    _stamps[key] = (list(img.pixels), img.size[0], img.size[1])
    bpy.data.images.remove(img)
    return _stamps[key]


def solid_stamp():
    """Full coverage everywhere: a plain rectangle of paint, for covering somebody else's titles.

    16 texels square rather than 2: with CLIP wrapping and linear filtering, a 2 x 2 image fades to
    transparent across its whole width, so the plate came out as a soft smudge that covered the middle of
    the stock titles and nothing else. Any size works once the sampling is nearest-neighbour, but a few
    texels of margin costs nothing and removes the question.
    """
    if "solid" not in _stamps:
        _stamps["solid"] = ([1.0, 1.0, 1.0, 1.0] * 256, 16, 16)
    return _stamps["solid"]


def logo_stamp():
    if "logo" not in _stamps:
        img = bpy.data.images.load(LOGO)
        _stamps["logo"] = (list(img.pixels), img.size[0], img.size[1])
        bpy.data.images.remove(img)
    return _stamps["logo"]


def crop_stamp(stamp, x0, x1, y0, y1, key):
    """A rectangle cut out of a bigger stamp -- here, the winged-headset badge out of the full lockup,
    which also carries the wordmark and PODCAST underneath it."""
    if key in _stamps:
        return _stamps[key]
    px, w, h = stamp
    a, b = int(x0 * w), int(x1 * w)
    c, d = int(y0 * h), int(y1 * h)
    out = []
    for y in range(c, d):
        row = (y * w + a) * 4
        out += px[row:row + (b - a) * 4]
    _stamps[key] = (out, b - a, d - c)
    return _stamps[key]


def badge_stamp():
    """The wings-and-headset emblem on its own, without the words."""
    return crop_stamp(logo_stamp(), 0.055, 0.945, 0.535, 0.790, "badge")


# THE LOGO'S "BLACK" FIELD IS NOT BLACK. It reads 0.035-0.047 in luminance across nine tenths of the
# image, so a mark taken straight from it carries a 4 % wash of ink over its whole rectangle -- which is
# invisible as near-black on a white 737 and perfectly visible as white on the red Schweizer, where the
# fin logo arrived inside a pale box. Everything of the mark itself is above 0.56. Cutting at 0.08 and
# rescaling what is left keeps the antialiased edges graded (0.7 % of the image lies between the two) and
# throws the field away.
FIELD = 0.08


def coverage(r, g, b, a):
    cov = max(a, 0.2126 * r + 0.7152 * g + 0.0722 * b)
    return 0.0 if cov <= FIELD else (cov - FIELD) / (1.0 - FIELD)


def sample(stamp, u, v):
    """Nearest-neighbour sample; returns (r, g, b, a) with a = 0 outside.

    The project's logo PNG is a white mark on a nominally black field, so its alpha channel says nothing.
    Its luminance does -- the same trick FlyingGame/Decal uses in the game. See FIELD for the cut.
    """
    px, w, h = stamp
    if not (0.0 <= u < 1.0 and 0.0 <= v < 1.0):
        return None
    i = (int(v * h) * w + int(u * w)) * 4
    r, g, b, a = px[i], px[i + 1], px[i + 2], px[i + 3]
    return (r, g, b, coverage(r, g, b, a))


def fit(stamp, height, max_width=None):
    """A decal rectangle sized from the stamp's OWN aspect, so the letters are never stretched.

    Giving every word the same rectangle is what made TALK and FLYING different sizes on the Super Cub:
    four letters and six letters pulled to the same width come out with different glyph widths and
    different apparent stroke weight, which reads as two fonts. Fix the HEIGHT and let the width follow.
    """
    _, w, h = stamp
    a = w / float(h)
    if max_width and a * height > max_width:
        height = max_width / a
    return (a * height, height)


def common_height(stamps, height, max_width):
    """One height that fits ALL of these words inside max_width -- so a pair of words on two wings are the
    same size as each other, which is the whole point of a banner."""
    for st in stamps:
        a = st[1] / float(st[2])
        height = min(height, max_width / a)
    return height


# =============================================================================================================
# decals: defined in space, found through the mesh
# =============================================================================================================
class Decal:
    """A rectangle in space, a direction it faces, and what to stamp in it.

    origin   centre of the rectangle
    vdir     direction the top of the letters points
    size     (along the reading direction, along vdir) in metres
    facing   surface normal the paint sticks to; a face whose normal is more than 70 deg off is skipped
    colour   what to paint; the stamp supplies coverage, not colour
    """

    def __init__(self, stamp, origin, vdir, size, facing, colour, depth=0.9):
        self.stamp = stamp
        self.o = Vector(origin)
        self.n = Vector(facing).normalized()
        self.v = Vector(vdir).normalized()
        # THE READING DIRECTION IS NOT A FREE CHOICE. Give the stamp a u and a v by hand and half of them
        # come out mirrored, because which way "left to right" runs depends on which side of the surface you
        # are standing. It is fixed by one invariant -- u x v must be the direction the paint faces -- and
        # u = v x n satisfies it for any v perpendicular to n. The Super Cub's banner came out backwards
        # exactly once, which was once more than necessary.
        self.u = self.v.cross(self.n).normalized()
        self.su, self.sv = size
        self.colour = colour
        self.literal = False          # true for a decal that keeps the artwork's own colours
        # WHAT TO DO WITH A COVERED TEXEL. Normally: blend the decal's colour in. A scrub wants to read the
        # texel it is standing on and decide, which is how the stock titles come OUT of the paint instead of
        # disappearing under a patch of a slightly different grey. apply(buf, i, cov, key, p3).
        self.apply = None
        # HOW DEEP THE PAINT REACHES. Without this a decal is a projector shining through the whole
        # aeroplane: the 737's titles came out twice, once on the fuselage side and again on the belly
        # below the wing, because a belly face also faces outward enough and projects into the rectangle.
        self.depth = depth

    def uv_of(self, p):
        d = Vector(p) - self.o
        if abs(d.dot(self.n)) > self.depth:
            return -9.0, -9.0
        a = d.dot(self.u) / self.su + 0.5
        b = d.dot(self.v) / self.sv + 0.5
        return a, b


def paint(objs, decals):
    """Rasterise each decal into whatever base-colour images the covered faces use."""
    # group the work by image so each image's pixel buffer is read and written once
    buffers, sizes = {}, {}

    def image_of(mat):
        if mat is None or not mat.use_nodes:
            return None
        for n in mat.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image is not None:
                for o in n.outputs[0].links:
                    if o.to_socket.name in ("Base Color", "Color"):
                        return n.image
        for n in mat.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image is not None:
                return n.image
        return None

    hits = 0
    for ob in objs:
        me = ob.data
        if not me.uv_layers:
            continue
        uvl = me.uv_layers.active.data
        mw = ob.matrix_world
        for poly in me.polygons:
            mat = ob.material_slots[poly.material_index].material if ob.material_slots else None
            img = image_of(mat)
            if img is None or img.size[0] == 0:
                continue
            wn = (mw.to_3x3() @ poly.normal).normalized()
            live = [d for d in decals if wn.dot(d.n) > 0.34]
            if not live:
                continue
            vs = [mw @ me.vertices[vi].co for vi in poly.vertices]
            uvs = [uvl[li].uv.copy() for li in poly.loop_indices]
            key = img.name
            if key not in buffers:
                buffers[key] = list(img.pixels)
                sizes[key] = (img.size[0], img.size[1], img)
            W, H, _ = sizes[key]
            buf = buffers[key]
            # fan-triangulate the polygon; both the 3-D and the UV triangle share the fan
            for t in range(1, len(vs) - 1):
                P = (vs[0], vs[t], vs[t + 1])
                T = (uvs[0], uvs[t], uvs[t + 1])
                for d in live:
                    # cheap reject: the whole triangle outside the decal's box
                    ab = [d.uv_of(p) for p in P]
                    if all(a < -0.05 or a > 1.05 for a, _ in ab) or all(b < -0.05 or b > 1.05 for _, b in ab):
                        continue
                    x0 = max(0, int(min(q.x for q in T) * W) - 1)
                    x1 = min(W - 1, int(max(q.x for q in T) * W) + 1)
                    y0 = max(0, int(min(q.y for q in T) * H) - 1)
                    y1 = min(H - 1, int(max(q.y for q in T) * H) + 1)
                    if x1 < x0 or y1 < y0 or (x1 - x0) * (y1 - y0) > 4_000_000:
                        continue
                    ax, ay = T[0].x * W, T[0].y * H
                    bx, by = T[1].x * W, T[1].y * H
                    cx, cy = T[2].x * W, T[2].y * H
                    den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
                    if abs(den) < 1e-9:
                        continue
                    for py in range(y0, y1 + 1):
                        fy = py + 0.5
                        for px_ in range(x0, x1 + 1):
                            fx = px_ + 0.5
                            l0 = ((by - cy) * (fx - cx) + (cx - bx) * (fy - cy)) / den
                            l1 = ((cy - ay) * (fx - cx) + (ax - cx) * (fy - cy)) / den
                            l2 = 1.0 - l0 - l1
                            if l0 < -0.002 or l1 < -0.002 or l2 < -0.002:
                                continue
                            p3 = P[0] * l0 + P[1] * l1 + P[2] * l2
                            a, b = d.uv_of(p3)
                            s = sample(d.stamp, a, b)
                            if s is None or s[3] < 0.08:
                                continue
                            cov = min(1.0, s[3])
                            i = (py * W + px_) * 4
                            if d.apply is not None:
                                d.apply(buf, i, cov, key, p3)
                            else:
                                for c in range(3):
                                    buf[i + c] = buf[i + c] * (1 - cov) + d.colour[c] * cov
                            hits += 1
    flush(buffers, sizes)
    print("LIVERY   painted %d texels across %d images" % (hits, len(buffers)))
    return buffers, sizes


def flush(buffers, sizes):
    """Hand the pixel buffers back to their images. Separate from paint() because a scrub reads first and
    writes second, and both passes work on the same buffers."""
    for key, buf in buffers.items():
        W, H, img = sizes[key]
        img.pixels = buf
        img.update()


def _stamp_image(stamp, name, colour, literal=False):
    """A Blender image of the stamp with real alpha, ready to hang on a material.

    The project's logo PNG is a white mark on an OPAQUE BLACK field -- its alpha says nothing and its
    luminance says everything, the same as in the game's FlyingGame/Decal shader. So coverage is
    max(alpha, luminance), and the colour is the livery's, not the file's.
    """
    px, w, h = stamp
    img = bpy.data.images.new(name, w, h, alpha=True)
    out = [0.0] * (w * h * 4)
    for i in range(w * h):
        j = i * 4
        if literal:
            # the artwork as drawn, background and all -- which is what a decal applied to a real
            # aeroplane is: a printed panel, not an inked stencil
            out[j], out[j + 1], out[j + 2], out[j + 3] = px[j], px[j + 1], px[j + 2], 1.0
        else:
            a = coverage(px[j], px[j + 1], px[j + 2], px[j + 3])
            out[j], out[j + 1], out[j + 2], out[j + 3] = colour[0], colour[1], colour[2], a
    img.pixels = out
    img.update()
    return img


_bvh_cache = {}


def surface_tree(objs):
    """One BVH of the whole aeroplane, built from the vertices themselves.

    Not Object.ray_cast and not Scene.ray_cast: both go through evaluated geometry from the depsgraph, and
    this tool rewrites meshes in place (load_canonical bakes the transform into the vertices), so the
    depsgraph holds the aeroplane as it was before it was turned round. Every ray missed and the mark
    silently went nowhere. BVHTree.FromPolygons takes the data as it is now and has no opinion about it.
    """
    key = tuple(sorted(o.name for o in objs))
    if key in _bvh_cache:
        return _bvh_cache[key]
    verts, polys = [], []
    for ob in objs:
        base = len(verts)
        mw = ob.matrix_world
        verts += [mw @ v.co for v in ob.data.vertices]
        for poly in ob.data.polygons:
            idx = [base + i for i in poly.vertices]
            for t in range(1, len(idx) - 1):
                polys.append((idx[0], idx[t], idx[t + 1]))
    tree = BVHTree.FromPolygons(verts, polys)
    _bvh_cache[key] = tree
    print("LIVERY   surface: %d triangles" % len(polys))
    return tree


def texel_window(covered, sizes):
    """TF_WINDOW="<image>,x0,y0,x1,y1": where a rectangle of the TEXTURE lands on the AEROPLANE, in metres.

    This is how the scrub rectangles below were arrived at rather than guessed. Find the stock marking by
    eye in the texture (export it and look), give its texel bounds, and this prints the x and z it occupies
    on the model -- which is what scrub() wants. Note the row order is the buffer's, counting up from the
    BOTTOM of the image, not down from the top as an image viewer shows it.
    """
    if not os.environ.get("TF_WINDOW"):
        return
    img, x0, y0, x1, y1 = os.environ["TF_WINDOW"].split(",")
    x0, y0, x1, y1 = int(x0), int(y0), int(x1), int(y1)
    bb, n = [1e9, -1e9, 1e9, -1e9], 0
    for key, items in covered.items():
        if key != img:
            continue
        W = sizes[key][0]
        for i, t, cov, wx, wz in items:
            tx, ty = (i // 4) % W, (i // 4) // W
            if x0 <= tx <= x1 and y0 <= ty <= y1:
                bb[0], bb[1] = min(bb[0], wx), max(bb[1], wx)
                bb[2], bb[3] = min(bb[2], wz), max(bb[3], wz)
                n += 1
    print("WINDOW %s texels=%d  x %.2f..%.2f  z %.2f..%.2f" % (img, n, bb[0], bb[1], bb[2], bb[3]))


def probe_map(hot, rows=22, cols=100):
    """TF_PROBE=1: an x/z map of what a scrub actually changed, drawn side-on.

    A rectangle that is a little too generous does not announce itself in the log -- it quietly takes the
    window line out with the titles. This draws what was changed so that is visible without a render.
    """
    if not (PROBE and hot):
        return
    xs, zs = [q[0] for q in hot], [q[1] for q in hot]
    x0, x1, z0, z1 = min(xs), max(xs), min(zs), max(zs)
    g = [[0] * cols for _ in range(rows)]
    for x, z in hot:
        g[min(rows - 1, int((z1 - z) / max(1e-6, z1 - z0) * rows))][
            min(cols - 1, int((x - x0) / max(1e-6, x1 - x0) * cols))] += 1
    for r, row in enumerate(g):
        print("PROBE %7.2f |%s|" % (z1 - (r + 0.5) / rows * (z1 - z0),
                                    "".join(" .:*#"[min(4, (c + 11) // 12)] for c in row)))
    print("PROBE x %.2f (aft) .. %.2f (nose)" % (x0, x1))


def scrub(objs, origin, vdir, size, facing, depth, name, tol=0.07, soft=0.05, tile=32):
    """Take the model's own markings OUT of its paint, instead of hiding them under a patch of ours.

    The blunt version, which this replaced, laid a patch of the livery's own colour over them. It shows: a
    patch of flat SILVER on a skin that recolour() has already clamped to near-white reads as a grey
    rectangle behind the titles, which is what the owner saw -- "instead of painting the area behind the
    talk flying airlines grey can you just type talk flying airlines over the aircraft's current paint".

    THE RULE IS "WHATEVER IS NOT THE PAINT", not "whatever is dark". Inside the rectangle, the texture is
    cut into tiles and each tile's MEDIAN colour is taken as the paint there -- markings are a minority of
    any tile, and a median over a tile follows the shading gradient instead of flattening it. Every texel
    more than `tol` away from its tile's median in luminance is pulled back to it, with a soft band so the
    anti-aliased edges leave no outline. Panel lines and rivets are within the tolerance and survive.

    Two narrower rules were tried first and each left a ghost, and the ghost is as obvious as the patch was:
      - lifting dark texels to white brought the titles back PALE, because the paint is 0.85, not 1.0;
      - lifting dark texels to the paint beside them left the titles as BRIGHT OUTLINES, because the stock
        lettering is dark with a white edge and the crest is gold -- both of which read brighter than the
        paint, so a dark-only rule never touched them at all.

    SHARED UVS ARE NOT A PROBLEM HERE, unlike a registration: both sides of the fuselage carry the same
    stock titles on the same texels, and both sides want them gone. One write that lands on both is right.
    """
    def luma(buf, j):
        return 0.2126 * buf[j] + 0.7152 * buf[j + 1] + 0.0722 * buf[j + 2]

    covered, tiles = {}, {}

    def look(buf, i, cov, key, p3):
        W = bpy.data.images[key].size[0]      # not sizes[]: paint() has not returned it yet
        px = i // 4
        t = (key, px % W // tile, px // W // tile)
        tiles.setdefault(t, []).append((luma(buf, i), buf[i], buf[i + 1], buf[i + 2]))
        covered.setdefault(key, []).append((i, t, cov, p3.x, p3.z))

    d = Decal(solid_stamp(), origin, vdir, size, facing, (1, 1, 1), depth=depth)
    d.apply = look
    buffers, sizes = paint(objs, [d])
    if not covered:
        print("LIVERY   %s: nothing under it" % name)
        return
    texel_window(covered, sizes)
    # THE PAINT IN EACH TILE, pooled with its eight neighbours before the median is taken. Pooling is not
    # optional: a tile is 32 texels and the crest is taller than that, so a tile wholly inside a marking has
    # that marking as its own median and nothing in it is ever touched. That left the crest and the first
    # few letters of REPUBLIK sharp while the rest of the word scrubbed away. Three tiles across is wider
    # than any marking here, and still narrow enough to follow the shading along the fuselage.
    med = {}
    for key, tx, ty in tiles:
        pool = []
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                pool += tiles.get((key, tx + dx, ty + dy), ())
        pool.sort()
        med[(key, tx, ty)] = pool[len(pool) // 2]      # (luma, r, g, b) of the paint there

    done, box, hot = 0, [1e9, -1e9, 1e9, -1e9], []
    for key, items in covered.items():
        buf = buffers[key]
        for i, t, cov, wx, wz in items:
            ml, mr, mg, mb = med[t]
            k = min(1.0, max(0.0, (abs(luma(buf, i) - ml) - tol) / soft)) * cov
            if k <= 0.0:
                continue
            for c, m in zip(range(3), (mr, mg, mb)):
                buf[i + c] = buf[i + c] * (1 - k) + m * k
            done += 1
            if k > 0.5:
                box[0], box[1] = min(box[0], wx), max(box[1], wx)
                box[2], box[3] = min(box[2], wz), max(box[3], wz)
                if PROBE:
                    hot.append((wx, wz))
    flush(buffers, sizes)
    print("LIVERY   %s: %d of %d texels put back to the paint around them (%d tiles)" %
          (name, done, sum(len(v) for v in covered.values()), len(med)))
    probe_map(hot)
    if box[1] > box[0]:
        # WHERE IT ACTUALLY BIT, in metres. The rectangle is a guess; this is the measurement that says
        # whether it caught the stock titles or also took the window line out with them.
        print("LIVERY   %s: changed x %.2f..%.2f  z %.2f..%.2f" % (name, box[0], box[1], box[2], box[3]))


def geo_decal(objs, d, name, lift=0.010, nu=96, nv=28):
    """Lay the mark on the model's skin, found BY RAY-CASTING a grid of the MARK'S OWN resolution onto it.

    WHY NOT PAINT IT INTO THE TEXTURE. A downloaded model's UV layout belongs to whoever made it, and on
    the 172 the two sides of the fuselage SHARE THEIR TEXELS -- paint the registration on the right and it
    appears mirrored on the left, because it is literally the same pixels. The information to tell the two
    sides apart is not in the file.

    WHY NOT COPY THE FACES EITHER. That works on a dense mesh (the Stearman's fin, 162 faces) and fails
    completely on a coarse one: the 172's fuselage side is about a dozen triangles far bigger than the
    mark, so the patch came out as the whole aft fuselage with the mark lost inside one of them.

    Casting a grid fixes both. The resolution is the mark's, not the model's, so it reads the same on a
    300-face aeroplane and a 300,000-face one -- and every vertex of it was measured off the skin, so it
    follows compound curvature exactly. It is not the floating quad the owner rightly called terrible.
    """
    tree = surface_tree(objs)
    reach = max(d.su, d.sv) + d.depth + 1.0
    grid, hits = [], 0
    for j in range(nv + 1):
        row = []
        for i in range(nu + 1):
            a, b = i / nu, j / nv
            base = d.o + d.u * ((a - 0.5) * d.su) + d.v * ((b - 0.5) * d.sv)
            loc, nor, _, _ = tree.ray_cast(base + d.n * reach, -d.n, reach * 2.2)
            # EITHER orientation counts. A low-poly fin is a single zero-thickness sheet, so which way
            # its normal points is an accident of how it was built; what matters is that the surface is
            # not edge-on and that it is within reach. The first hit coming from outside is the near one.
            if loc is not None and abs(nor.dot(d.n)) > 0.2 and abs((loc - d.o).dot(d.n)) <= d.depth:
                # LIFT TOWARDS THE VIEWER, NOT ALONG THE FACE NORMAL. Which way a face points is an
                # accident on these models -- load_canonical mirrors the aeroplane and reverses every
                # winding to compensate, and a low-poly fin is a zero-thickness sheet with no outside at
                # all -- so lifting along `nor` buries the mark 10 mm INSIDE the skin wherever the normal
                # happens to face inwards. That is exactly what happened on the 172: the registration
                # came out as a row of dashes (the few quads whose normal pointed the right way) and the
                # fin mark and the fuselage badge did not appear at all, though every ray had hit and
                # every quad had been made. The decal already knows which side it is on: d.n.
                out = nor.normalized()
                row.append((loc + (out if out.dot(d.n) > 0 else -out) * lift, a, b))
                hits += 1
            else:
                row.append(None)
        grid.append(row)
    if hits < 8:
        print("LIVERY   %s: nothing under it to stick to" % name)
        return None

    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("decal")
    step = max(d.su / nu, d.sv / nv)
    made = 0
    for j in range(nv):
        for i in range(nu):
            q = (grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i])
            if any(c is None for c in q):
                continue
            # a quad straddling a sharp edge of the model would smear the mark across it
            if max((q[k][0] - q[(k + 1) % 4][0]).length for k in range(4)) > step * 6:
                continue
            vs = [bm.verts.new(c[0]) for c in q]
            try:
                f = bm.faces.new(vs)
            except ValueError:
                continue
            for loop, c in zip(f.loops, q):
                loop[uvl].uv = (c[1], c[2])
            made += 1
    if not made:
        bm.free()
        print("LIVERY   %s: no quad survived" % name)
        return None
    bm.normal_update()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)

    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    # Blender 4.2 replaced Material.blend_method with surface_render_method for EEVEE. Set whichever the
    # build has, or the mark renders as an opaque rectangle instead of a mark.
    for attr, val in (("blend_method", 'BLEND'), ("surface_render_method", 'BLENDED')):
        if hasattr(mat, attr):
            try:
                setattr(mat, attr, val)
            except (TypeError, AttributeError):
                pass
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = _stamp_image(d.stamp, name + "Img", d.colour, literal=d.literal)
    tex.extension = 'CLIP'
    tex.interpolation = 'Closest'
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    if "Roughness" in bsdf.inputs:
        bsdf.inputs["Roughness"].default_value = 0.45
    me.materials.append(mat)
    print("LIVERY   %s: %d quads on the skin (%d of %d rays hit)" % (name, made, hits, (nu + 1) * (nv + 1)))
    return ob


# =============================================================================================================
# base coat
# =============================================================================================================
def recolour(objs, rule):
    """Change the skin's colour while KEEPING EACH PIXEL'S OWN BRIGHTNESS, so the shading, the panel shadow
    and the grime baked into the texture all survive. A flat fill erases them and the aeroplane goes plastic.
    `rule(r, g, b) -> new (r, g, b) or None`."""
    done, changed = set(), 0
    for ob in objs:
        for slot in ob.material_slots:
            m = slot.material
            if m is None or not m.use_nodes:
                continue
            for n in m.node_tree.nodes:
                if n.type != 'TEX_IMAGE' or n.image is None or n.image.name in done:
                    continue
                if n.image.size[0] == 0:
                    continue
                done.add(n.image.name)
                px = list(n.image.pixels)
                for i in range(0, len(px), 4):
                    out = rule(px[i], px[i + 1], px[i + 2])
                    if out is not None:
                        px[i], px[i + 1], px[i + 2] = out
                        changed += 1
                n.image.pixels = px
                n.image.update()
    print("LIVERY   recoloured %d texels" % changed)


def keep_luma(r, g, b, target, ref=0.30):
    lum = 0.2126 * r + 0.7152 * g + 0.0722 * b
    k = min(1.7, lum / ref)
    return (min(1.0, target[0] * k), min(1.0, target[1] * k), min(1.0, target[2] * k))


def whiten(r, g, b, amount=0.55):
    return (r + (1 - r) * amount, g + (1 - g) * amount, b + (1 - b) * amount)


def is_neutral(r, g, b, tol=0.10):
    return max(r, g, b) - min(r, g, b) < tol


# =============================================================================================================
# load and put the aeroplane in a known frame
# =============================================================================================================
def load_canonical(path, true_span):
    """Import, then move into: +X forward, +Y right, +Z up, origin at the wing's mid-span, metres."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    low = path.lower()
    if low.endswith((".glb", ".gltf")):
        bpy.ops.import_scene.gltf(filepath=path)
    elif low.endswith((".usdz", ".usd")):
        bpy.ops.wm.usd_import(filepath=path)
    elif low.endswith(".blend"):
        # LINK NOTHING, APPEND THE OBJECTS. The modern sailplane came off OpenGameArt as a .blend and
        # nothing else, and converting it to a GLB first would put a build artefact between the tool and
        # its source -- re-run the tool on a different machine and the GLB is not there. Appending reads
        # the data-blocks only; no script in the file runs (blender is given --disable-autoexec too).
        with bpy.data.libraries.load(path) as (src_data, dst_data):
            dst_data.objects = list(src_data.objects)
        for o in dst_data.objects:
            if o is not None:
                bpy.context.collection.objects.link(o)
    else:
        bpy.ops.wm.obj_import(filepath=path)
    objs = [o for o in bpy.data.objects if o.type == 'MESH']
    pts = []
    for o in objs:
        mw = o.matrix_world
        pts += [mw @ v.co for v in o.data.vertices]
    mn = Vector((min(p[i] for p in pts) for i in range(3)))
    mx = Vector((max(p[i] for p in pts) for i in range(3)))
    size = mx - mn
    order = sorted(range(3), key=lambda i: -size[i])
    SP, LN, HG = order
    # LONGEST IS NOT ALWAYS THE SPAN. A 737 is longer than it is wide, and taking the longest axis as the
    # span turned it sideways: the "side view" came out nose-on. What tells them apart is the FIN -- the
    # length axis has something tall at one end of it, and the span axis has wingtips at both ends, which
    # are thin and low. So measure the height in the end slabs of each candidate and take the taller.
    def end_height(ax):
        lo, hi = mn[ax], mx[ax]
        slab = (hi - lo) * 0.12
        best = 0.0
        for c, lim in ((lo, lo + slab), (hi - slab, hi)):
            sel = [p[HG] for p in pts if c <= p[ax] <= lim]
            if len(sel) > 8:
                best = max(best, max(sel) - min(sel))
        return best
    if end_height(LN) < end_height(SP):
        SP, LN = LN, SP
        print("LIVERY   axes swapped: this one is longer than it is wide")
    k = true_span / size[SP]

    # WHICH END IS THE NOSE: THE FIN IS AT THE OTHER ONE. The previous test -- the widest body section is
    # forward of the midpoint -- is true of a light single and meaningless on an airliner, whose fuselage
    # is the same width for most of its length. It put the 737 round the wrong way, and then everything
    # placed by the sign of x went on backwards while the printed "nose x" agreed with itself. Nothing on
    # any aeroplane we fly is taller than its tail.
    lo, hi = mn[LN], mx[LN]
    tall = max(pts, key=lambda p: p[HG])
    tail_at_hi = tall[LN] > (lo + hi) * 0.5
    sgn = 1.0 if tail_at_hi else -1.0
    # and which way is up: an aeroplane has more of itself above the wing line than below it near the nose
    ctr = (mn + mx) * 0.5
    upper = sum(1 for p in pts if p[HG] > ctr[HG])
    usg = 1.0 if upper <= len(pts) * 0.5 else 1.0        # both references are already z-up; keep it simple

    for o in objs:
        me = o.data
        for v in me.vertices:
            w = o.matrix_world @ v.co
            v.co = Vector((-(w[LN] - ctr[LN]) * sgn * k, (w[SP] - ctr[SP]) * k, (w[HG] - ctr[HG]) * k * usg))
        o.matrix_world = Matrix.Identity(4)
        me.update()
    # MIRRORING INVERTS EVERY NORMAL. Turning the model around means negating one axis, which is a
    # reflection: the vertices move but the winding does not, so every face ends up inside out. Nothing
    # looks wrong in a render (the shader draws both sides) -- but a decal that asks "is this face pointing
    # down?" gets the opposite answer, and the Super Cub's banner lettering went onto the TOP of the wings.
    if sgn > 0:
        for o in objs:
            bm = bmesh.new()
            bm.from_mesh(o.data)
            bmesh.ops.reverse_faces(bm, faces=bm.faces)
            bm.to_mesh(o.data)
            bm.free()
            o.data.update()
    for o in objs:
        o.data.calc_normals_split() if hasattr(o.data, "calc_normals_split") else None
        o.data.calc_loop_triangles()
    bad = [o.name for o in objs if (o.matrix_world - Matrix.Identity(4)).median_scale > 1e-5]
    if bad:
        print("LIVERY   WARNING: %d objects still carry a transform (%s ...)" % (bad and len(bad), bad[0]))
    xs = [v.co.x for o in objs for v in o.data.vertices]
    print("LIVERY   canonical: span %.2f  length %.2f  height %.2f   nose x=%+.2f  tail x=%+.2f" %
          (size[SP] * k, size[LN] * k, size[HG] * k, max(xs), min(xs)))
    return objs, size[LN] * k, size[HG] * k


# =============================================================================================================
# the four aeroplanes
# =============================================================================================================
def _halfspan(objs):
    return max(abs(v.co.y) for o in objs for v in o.data.vertices)


def _biggest_cluster(xs, min_gap=0.22):
    """The x range of the LARGEST CONTIGUOUS GROUP of stations.

    A spanwise slab catches the wing and, on a short-coupled aeroplane, the tailplane as well -- and taking
    the slab's extremes merges the two into one 4.5 m "chord" on a 1.6 m wing, which stretched TALK across
    the whole aeroplane. Density does not help either: the densest bin is the leading edge, which collapsed
    the chord to 60 mm. What separates them is the EMPTY SPACE between: split at every gap wider than
    `min_gap` of the range and keep the group with the most vertices in it.
    """
    if len(xs) < 12:
        return None
    xs = sorted(xs)
    rng = xs[-1] - xs[0]
    if rng < 1e-6:
        return None
    gap = rng * min_gap
    groups, cur = [], [xs[0]]
    for a, b in zip(xs, xs[1:]):
        if b - a > gap:
            groups.append(cur)
            cur = []
        cur.append(b)
    groups.append(cur)
    # pick by EXTENT, not by count, among the groups holding a real share of the vertices. By count the
    # winner is the leading edge, where a mesh is densest, and the "chord" comes out 60 mm.
    big = [g for g in groups if len(g) >= 0.18 * len(xs)] or groups
    g = max(big, key=lambda q: q[-1] - q[0])
    return g[0], g[-1]


def wing_box(objs, side):
    """Where the wing actually is: a spanwise slab taken as a FRACTION OF THE SPAN, so it sits well outboard
    of any tailplane on anything from a Cub to a 737, and then simply the extremes of what is in it.

    Clever was tried and failed twice. An absolute 1.2-3.4 m band caught the Cub's tailplane and reported a
    4.55 m chord on a 1.38 m wing. Taking the densest histogram bin instead found the leading edge, where a
    mesh is always densest, and reported 60 mm. The slab itself is the thing that has to be right; once it
    is, the extremes are the answer.
    """
    # 0.45 to 0.70 of the half-span: outboard of every tailplane in the fleet (a Stearman's reaches a third
    # of the way out, a 737's nearly 0.37) and inboard of the tip, where a wing starts to taper away
    hs = _halfspan(objs)
    xs = sorted(v.co.x for o in objs for v in o.data.vertices
                if 0.45 * hs < v.co.y * side < 0.70 * hs)
    if len(xs) < 12:
        return None
    bx = xs[int(len(xs) * 0.01)], xs[int(len(xs) * 0.99)]
    print("LIVERY   wing side %+d: x %.2f .. %.2f  chord %.2f  (%d verts)" % (side, bx[0], bx[1], bx[1] - bx[0], len(xs)))
    return bx


def fin_box(objs, L):
    """The fin: on the centreline, aft, and above the fuselage's own top deck.

    Taking the top quarter of everything aft of the quarter-chord is not enough -- a Stearman's turtledeck
    is higher than the bottom of its fin, so the box ran 2.3 m forward onto the fuselage and the mark landed
    behind the cockpit. Find the deck's height just FORWARD of the tail and keep only what stands above it.
    """
    # NEAR THE CENTRELINE, scaled to the aeroplane. A fixed 0.10 m finds a Cub's fin and misses a 737's,
    # whose fin is over a metre thick at the root so its skin is nowhere near the centreline except at the
    # leading and trailing edges. Take a slab a twentieth of the span wide and let the height test below
    # pick the fin out of it.
    tol = max(0.12, 0.05 * _halfspan(objs))
    aft = [v.co for o in objs for v in o.data.vertices if abs(v.co.y) < tol and v.co.x < -0.16 * L]
    if len(aft) < 24:
        return None
    # the deck: the top of the body in a slab just ahead of where a fin ever starts
    deck = [p.z for p in aft if p.x > -0.38 * L]
    if len(deck) < 8:
        deck = [p.z for p in aft]
    deck.sort()
    cut = deck[int(len(deck) * 0.92)] + 0.02 * L
    up = [p for p in aft if p.z >= cut]
    if len(up) < 12:
        zs = sorted(p.z for p in aft)
        cut = zs[int(len(zs) * 0.80)]
        up = [p for p in aft if p.z >= cut]
    if len(up) < 12:
        print("LIVERY   fin: not found (%d aft, %d above the deck)" % (len(aft), len(up)))
        return None
    # the EXTREMES, not percentiles. On a low-poly model the fin is a handful of quads with vertices only
    # at its corners, and 3rd/97th percentile clips to whichever cluster is densest -- on the 172 that was
    # the tip, giving a 0.74 m "fin" at the very back of a 1.5 m one.
    xs = sorted(p.x for p in up)
    x0, x1 = xs[0], xs[-1]
    z0, z1 = cut, max(p.z for p in up)
    print("LIVERY   fin: x %.2f .. %.2f  z %.2f .. %.2f" % (x0, x1, z0, z1))
    return x0, x1, z0, z1


LEFT, RIGHT = -1, 1


def wing_pair(side, text, badge):
    """Which mark goes on which surface of which wing.

    The owner's rule: the words above the left wing and the badge above the right, then the opposite
    underneath -- so whichever wing you are looking at, and from whichever side, you get one of each.
    """
    return (text, badge) if side == LEFT else (badge, text)


def fin_points(objs, L, from_body=False):
    """The fin's outline as (x, z) points, and the height of the deck it stands on.

    TWO WAYS OF FINDING THE DECK, because one does not serve four aeroplanes. The default ranks the
    heights of everything aft and takes a high percentile, which is what the 737's badge was fitted with
    and must stay fitted with: its dorsal fillet blends into the tailcone, so the fuselage's own top deck
    is a metre and a half below anything that looks like a fin, and measuring it lets eleven metres of
    tailcone into the "fin chord".

    `from_body` measures it instead, with body_side()'s rays: at a station they report where the body's
    own skin is and ignore whatever is thin and on the centreline, which is what a fin is. That is what
    the light aeroplanes need, because a percentile cannot tell a tall thin fin from a deck -- the Cub's
    fin carries more vertices than the boom it stands on, so the 92nd percentile landed INSIDE the fin,
    the fin came out 0.21 m tall, and the mark on it was the size of a postage stamp at the very tip.

    Stations are kept only where the ray found something the size of a fuselage: the tailplane is in the
    way at some of them, and at the tailpost everything is thin, so comparing each station with the
    MEDIAN of them throws out both. Comparing with the widest does not -- on the 172 the widest station
    is a tailplane hit and the test then throws out the fuselage.
    """
    tol = max(0.15, 0.08 * _halfspan(objs))
    aft = [v.co for o in objs for v in o.data.vertices if abs(v.co.y) < tol and v.co.x < -0.16 * L]
    if len(aft) < 24:
        return [], 0.0
    cut_z = None
    if from_body:
        bands = [b for b in (body_side(objs, -L * f)
                             for f in (0.18, 0.22, 0.26, 0.30, 0.34, 0.38)) if b]
        if len(bands) >= 3:
            mid = sorted(b[2] for b in bands)[len(bands) // 2]
            decks = [b[1] for b in bands if 0.5 * mid <= b[2] <= 2.0 * mid]
            if decks:
                cut_z = max(decks) + 0.012 * L
    if cut_z is None:
        deck = sorted(p.z for p in aft if p.x > -0.38 * L) or sorted(p.z for p in aft)
        # A 0.02 * L margin lifts the usable region 1.8 m up a 737's fin; 0.005 lets the TAILCONE in and
        # the "fin chord" came out 11.2 m, which is most of the aeroplane. 0.012 is the fin and only it.
        cut_z = deck[int(len(deck) * 0.92)] + 0.012 * L
    up = [(p.x, p.z) for p in aft if p.z >= cut_z]
    if len(up) < 12:
        zs = sorted(p.z for p in aft)
        cut_z = zs[int(len(zs) * 0.80)]
        up = [(p.x, p.z) for p in aft if p.z >= cut_z]
    return up, cut_z


def convex_hull(pts):
    """Andrew's monotone chain. A fin is a swept trapezoid with a rounded tip -- close enough to convex
    that its hull is its outline, and erring inward at the tip is the safe direction to err."""
    pts = sorted(set(pts))
    if len(pts) < 3:
        return pts

    def half(ps):
        out = []
        for q in ps:
            while len(out) >= 2:
                (ax, ay), (bx, by) = out[-2], out[-1]
                if (bx - ax) * (q[1] - ay) - (by - ay) * (q[0] - ax) > 0:
                    break
                out.pop()
            out.append(q)
        return out
    return half(pts)[:-1] + half(pts[::-1])[:-1]


def inside_hull(hull, pt, clear=0.0):
    """Inside the outline, and at least `clear` metres in from every edge of it.

    The clearance is not fussiness. A fin's outline IS its leading edge, and the skin curves away there, so
    a mark whose corner is exactly on the outline is a mark that wraps round the front -- which is what the
    owner saw on the 737: "lower this logo just a bit so it doesn't wrap around the leading edge". Held off
    by a hand's breadth it stays on the flat of the fin.
    """
    n = len(hull)
    for i in range(n):
        ax, ay = hull[i]
        bx, by = hull[(i + 1) % n]
        ex, ey = bx - ax, by - ay
        d = math.hypot(ex, ey)
        if d < 1e-9:
            continue
        if (ex * (pt[1] - ay) - ey * (pt[0] - ax)) / d < clear - 1e-9:
            return False
    return True


def fitted_fin_badge(objs, L, stamp, frac_z=0.45, margin=0.94, prefer=None, keep=0.82, at=None,
                     clear=0.0, from_body=False):
    """The biggest rectangle of this artwork's shape that fits INSIDE the fin, and where to put it.

    Sizing a mark as a fraction of the fin's bounding box hangs it over the leading edge, because the box
    is the ROOT chord and a swept fin is far shorter higher up. Sampling the chord at a height does not
    work either: a low-poly fin has vertices at only a few heights, so a thin band finds nothing at all.
    Take the outline as a convex hull of the fin's own points, then SEARCH FOR THE PLACE as well as the
    size -- a square centred on the mid-chord of a swept fin is pinched by the leading edge, and a hand's
    breadth aft it is half as big again.
    """
    pts, deck = fin_points(objs, L, from_body)
    if len(pts) < 8:
        print("LIVERY   fin badge: no fin found")
        return None
    hull = convex_hull(pts)
    xs_all = [q[0] for q in pts]
    zs_all = [q[1] for q in pts]
    x0, x1 = min(xs_all), max(xs_all)
    # the deck is a floor as well as a filter: nothing of the mark may hang below it
    z0, z1 = max(min(zs_all), deck), max(zs_all)
    asp = stamp[1] / float(stamp[2])

    def biggest_at(cx, cz):
        def fits(w):
            h = w / asp
            if cz - h * 0.5 < deck + clear:
                return False
            return all(inside_hull(hull, (cx + sx * w * 0.5, cz + sz * h * 0.5), clear)
                       for sx in (-1, 1) for sz in (-1, 1))
        if not fits(1e-4):
            return 0.0
        lo, hi = 0.0, (x1 - x0) * 1.2
        for _ in range(26):
            mid = (lo + hi) * 0.5
            if fits(mid):
                lo = mid
            else:
                hi = mid
        return lo

    cand = []
    for i in range(25):
        cz = z0 + (z1 - z0) * (0.02 + 0.96 * i / 24.0)
        for j in range(33):
            cx = x0 + (x1 - x0) * j / 32.0
            w = biggest_at(cx, cz)
            if w > 0:
                cand.append((w, cx, cz))
    if not cand:
        print("LIVERY   fin badge: nothing fits")
        return None
    top = max(c[0] for c in cand)
    # WHERE IT GOES MATTERS AS MUCH AS HOW BIG IT IS. Taking the largest square that fits puts it wherever
    # the fin happens to be fattest; when the mark has a job to do -- covering what was there before --
    # keep every candidate within `keep` of the largest and then pick the one nearest the target.
    if at or prefer:
        px_, pz_ = at if at else (x0 + (x1 - x0) * prefer[0], z0 + (z1 - z0) * prefer[1])
        ok = [c for c in cand if c[0] >= top * keep]
        w, cx, cz = min(ok, key=lambda c: (c[1] - px_) ** 2 + (c[2] - pz_) ** 2)
    else:
        w, cx, cz = max(cand, key=lambda c: c[0])
    w *= margin
    h = w / asp
    if w < (z1 - z0) * 0.06:
        print("LIVERY   fin badge: nothing of a sensible size fits")
        return None
    print("LIVERY   fin badge: %.2f x %.2f at x %.2f z %.2f  (fin %.2f tall, %.2f chord, %.2f clear)"
          % (w, h, cx, cz, z1 - z0, x1 - x0, clear))
    return cx, cz, w, h


def fin_mark(objs, L, prefer=(0.52, 0.50), clear=0.0, lower=0.0, box=None, ink=INK):
    """The Talk Flying mark on the vertical tail. Every aeroplane gets one -- it is the one piece of the
    livery that is the same on all of them, so it lives in one place rather than in four.

    FITTED TO THE FIN'S OUTLINE, not to a fraction of its bounding box, which is what the 737 has done
    since the owner asked for its badge to stop wrapping round the leading edge. The fraction was wrong in
    both directions at once on the others: on the Cub the box reached only from the deck test up, so the
    mark came out 0.18 m tall at the very tip, and on the 172 it sat centred on the rudder hinge with half
    of it on a graphite rudder. Neither was ever seen, because every one of these marks was being lifted
    10 mm INTO the skin (see geo_decal) and none of them rendered at all.

    `lower` drops the fitted mark by that many metres -- the fitter finds the biggest square that fits,
    which is not always where it looks best, and the owner's eye settles that. `box` places it by hand,
    (centre x, centre z, size), for a tail whose outline the fitter cannot read; cessna() says why.
    """
    if box is None:
        fitted = fitted_fin_badge(objs, L, logo_stamp(), prefer=prefer, clear=clear,
                                  from_body=True)
        if not fitted:
            return
        cx, cz, w, h = fitted
    else:
        cx, cz, w = box
        h = w
        print("LIVERY   fin badge: %.2f x %.2f at x %.2f z %.2f  (placed by hand)" % (w, h, cx, cz))
    cz -= lower
    for sd in (1, -1):
        # a square mark wants a square grid: at the default 96 x 28 the cells are three times taller than
        # they are wide, and a dropped cell at a panel step takes a visible bite out of the edge
        geo_decal(objs, Decal(logo_stamp(), (cx, sd * 0.10, cz), (0, 0, 1), (w, h), (0, sd, 0), ink,
                              depth=max(0.35, w * 0.6)),
                  "Mark.Fin." + ("R" if sd > 0 else "L"), nv=96)


def body_side(objs, x, keep=0.72, step=0.02):
    """The band of height over which the fuselage really has a SIDE at station x, found by RAY-CASTING.

    Not from the vertices. This model's tailcone is one long quad from the back of the cabin to the
    tailpost, so between those two stations there is nothing to measure and anything in between is
    interpolation -- which is a guess wearing a decimal point. Rays find the skin wherever it actually is,
    the same way geo_decal finds it when it lays the mark down.

    Two tests pick the side out of the hits. The first keeps only what is out at the SIDE -- within `keep`
    of the widest hit at that station -- because the fin's sheet lies on the centreline and reaches a long
    way forward (x -0.25 on the 172), and without this the "side" runs a hand's breadth too high. The
    second keeps the longest unbroken run, so a window, or the gap between two objects, cannot pass for
    skin. Returns (z bottom, z top, half-width).
    """
    tree = surface_tree(objs)
    hs = _halfspan(objs)
    reach = hs * 0.6
    wide = hs * 0.25                       # wider than any fuselage, narrower than a strut or a wheel
    hits = []
    z = hs * 0.25
    while z > -hs * 0.25:
        loc, _, _, _ = tree.ray_cast(Vector((x, reach, z)), Vector((0, -1, 0)), reach * 2.2)
        if loc is not None and abs(loc.y) < wide:
            hits.append((z, abs(loc.y)))
        z -= step
    if not hits:
        return None
    ymax = max(q[1] for q in hits)
    side = [q[0] for q in hits if q[1] >= keep * ymax]
    if not side:
        return None
    runs, cur = [], [side[0]]
    for a, b in zip(side, side[1:]):
        if abs(b - a) > step * 2.5:
            runs.append(cur)
            cur = []
        cur.append(b)
    runs.append(cur)
    r = max(runs, key=len)
    return min(r), max(r), ymax


def rear_bay(objs, L):
    """The clear stretch of fuselage: aft of the wing's trailing edge, forward of the tailplane's leading
    edge. Returns (aft x, forward x).

    The owner asked for the mark "on the back part of the fuselage behind the wings in front of the
    horizontal tail", which is a statement about two pieces of the aeroplane, not about a fraction of its
    length -- so both ends are measured off those two pieces. The bands are fractions of the SPAN so they
    sit outboard of the body and inboard of the tip on anything from a Cub to an airliner.
    """
    hs = _halfspan(objs)
    # WHICH VERTICES BELONG TO A FLYING SURFACE. Not a spanwise slab: this wing has 140 vertices and they
    # sit at the root and the tip and nowhere in between, so a slab at mid-span is empty and a slab wide
    # enough to catch something catches the wheel fairings as well. The test that works is per OBJECT --
    # anything that reaches further out than a fuselage ever does is a flying surface -- and then its OWN
    # root station is read, which is the end of the bay that matters. (A model with the wing and the
    # fuselage in one object would measure the fuselage instead; the printed bay below is the check.)
    surf = [o for o in objs if o.data.vertices and max(abs(v.co.y) for v in o.data.vertices) > 0.25 * hs]
    wing = [v.co.x for o in surf for v in o.data.vertices if abs(v.co.y) < 0.5 * hs and v.co.x > -0.20 * L]
    tail = [v.co.x for o in surf for v in o.data.vertices if abs(v.co.y) < 0.5 * hs and v.co.x < -0.20 * L]
    if len(wing) < 4 or len(tail) < 4:
        print("LIVERY   rear bay: no wing (%d) or no tailplane (%d)" % (len(wing), len(tail)))
        return None
    xa, xf = max(tail), min(wing)
    print("LIVERY   rear bay: x %.2f .. %.2f  (%.2f m, wing TE to tailplane LE)" % (xa, xf, xf - xa))
    if not 0.10 * L < xf - xa < 0.65 * L:
        print("LIVERY   rear bay: that is not a tailcone -- %.2f m on an %.2f m aeroplane" % (xf - xa, L))
        return None
    return xa, xf


def clean_paint(objs, x0, x1, zc, h, bins=56, drop=0.30, sd=1):
    """The longest stretch of the body's side that is light enough to carry a black mark, in metres.

    A black mark laid across the graphite stripe behind the cabin disappears into it -- the first fitting
    of the wings put one wingtip on the stripe and that wing stopped existing. Where the stripe is is not
    something to be eyeballed off a render and typed in as a number, though: it is in the model's texture,
    so it can be read out of it. This walks the rectangle with paint()'s own machinery, bins the texels it
    covers by station, and keeps the longest run of bins whose median brightness is no more than `drop`
    below the rectangle's own median -- which is the paint, markings being a minority of it.

    The test is ONE-SIDED on purpose. "All one colour" was tried first and it threw away the shaded wedge
    down the side of the tailcone as well, which is a fifth of a stop darker and carries a black mark
    perfectly well; what a mark cannot survive is paint as dark as the mark.

    Nothing is written: the hook only reads. paint() hands the buffers back unchanged.
    """
    seen = {}

    def look(buf, i, cov, key, p3):
        b = min(bins - 1, max(0, int((p3.x - x0) / (x1 - x0) * bins)))
        seen.setdefault(b, []).append(0.2126 * buf[i] + 0.7152 * buf[i + 1] + 0.0722 * buf[i + 2])

    # BOTH SIDES AT ONCE, FROM THE CENTRELINE. paint() only touches a face whose normal faces the decal,
    # and this model's normals point inward, so a decal asking for the right side is answered by the LEFT
    # side's faces -- which are then a whole fuselage width away from the decal's own plane and fall
    # outside its depth wherever the body is fat. The first run of this read the aft half of the tailcone
    # and nothing forward of the decal's centre, and reported that the paint went dark at x -0.91 when
    # really the paint had not been looked at. Standing the rectangle ON the centreline puts both sides
    # within depth, and the two sides share their texels here anyway.
    ds = []
    for side in (1, -1):
        d = Decal(solid_stamp(), ((x0 + x1) * 0.5, 0.0, zc), (0, 0, 1), (x1 - x0, h), (0, side, 0),
                  (1, 1, 1), depth=0.9)
        d.apply = look
        ds.append(d)
    paint(objs, ds)
    if len(seen) < bins // 3:
        print("LIVERY   clean paint: too little of it was reached (%d of %d bins)" % (len(seen), bins))
        return x0, x1
    med = {}
    for b, vs in seen.items():
        vs.sort()
        med[b] = vs[len(vs) // 2]
    whole = sorted(med.values())[len(med) // 2]
    if PROBE:
        for b in range(bins):
            if b in med:
                print("PROBE  x %6.2f  luma %.2f |%s" % (x0 + (b + 0.5) * (x1 - x0) / bins, med[b],
                                                         "#" * int(med[b] * 40)))
    runs, cur = [], []
    for b in range(bins):
        if b in med and med[b] >= whole - drop:
            cur.append(b)
        else:
            runs.append(cur)
            cur = []
    runs.append(cur)
    best = max(runs, key=len)
    if len(best) < 3:
        print("LIVERY   clean paint: none of it is light enough")
        return x0, x1
    step = (x1 - x0) / bins
    a, b = x0 + best[0] * step, x0 + (best[-1] + 1) * step
    print("LIVERY   clean paint: x %.2f .. %.2f of %.2f .. %.2f  (%d of %d bins, median luma %.2f)"
          % (a, b, x0, x1, len(best), bins, whole))
    return a, b


def side_slot(objs, x0, x1, clear=0.05, n=8):
    """The height a mark can keep ALL THE WAY from x0 to x1 on the body's side.

    A tailcone tapers, so a mark sized on the station where it starts hangs off the bottom at the station
    where it ends. Take the tightest of several stations across it and the mark stays on the skin.
    """
    bands = [b for b in (body_side(objs, x0 + (x1 - x0) * i / float(n)) for i in range(n + 1)) if b]
    if not bands:
        return None
    z0, z1 = max(b[0] for b in bands) + clear, min(b[1] for b in bands) - clear
    return (z0, z1) if z1 > z0 else None


def fitted_side_badge(objs, bay, stamp, frac=(0.33, 0.95), clear=0.06):
    """The logo's wings on the rear fuselage, sized to the body that is actually there, both sides.

    `frac` is where in the bay the mark lives, measured from the tailplane end. The size is whichever
    binds: the length of that stretch, or the depth of the shallowest station in it.
    """
    xa, xf = bay
    x0, x1 = xa + (xf - xa) * frac[0], xa + (xf - xa) * frac[1]
    slot = side_slot(objs, x0, x1, clear)
    if slot is None:
        print("LIVERY   side badge: no body under it")
        return None
    z0, z1 = slot
    asp = stamp[1] / float(stamp[2])
    h = min(z1 - z0, (x1 - x0) / asp)
    if h <= 0.04:
        print("LIVERY   side badge: nothing of a sensible size fits (band %.2f m)" % (z1 - z0))
        return None
    w = h * asp
    cx, cz = (x0 + x1) * 0.5, (z0 + z1) * 0.5
    print("LIVERY   side badge: %.2f x %.2f at x %.2f z %.2f  (band z %.2f..%.2f across x %.2f..%.2f)"
          % (w, h, cx, cz, z0, z1, x0, x1))
    return cx, cz, w, h


def supercub(objs, L, H):
    """Banner-tow lettering under the wings, one word each, tops toward the leading edge.

    TALK goes on the aeroplane's LEFT wing and FLYING on its right. That is the opposite of what looks
    right on paper, and it is right for the only viewer who matters: someone standing underneath sees the
    aeroplane's left wing on THEIR right, so the reading order flips. The first pass reasoned it out
    instead of looking, and from the ground it said FLYING TALK.
    """
    decals = []
    boxes = {side: wing_box(objs, side) for side in (1, -1)}
    if any(b is None for b in boxes.values()):
        return
    chord = min(b[1] - b[0] for b in boxes.values())
    stamps = {w: text_stamp(w) for w in ("TALK", "FLYING")}
    # one height for both words, so the banner is one size and one weight all the way across
    h = common_height(stamps.values(), chord * 0.64, 3.9)
    for side, word in ((-1, "TALK"), (1, "FLYING")):
        x0, x1 = boxes[side]
        c = x1 - x0
        decals.append(Decal(stamps[word], (x0 + c * 0.55, side * 2.9, -0.15),
                            (1, 0, 0), fit(stamps[word], h), (0, 0, -1), INK))
    # AND THE SAME WORDS ON TOP, which is what you see of a Cub from the circuit rather than from the
    # ground. The words swap wings. The reading direction is u = v x n, so flipping the surface the paint
    # sticks to reverses it: with the tops of the letters toward the leading edge on both sides, u runs
    # out to the aeroplane's right underneath and to its left on top. Putting TALK on the same wing both
    # times would have it read backwards from one of the two.
    for side, word in ((1, "TALK"), (-1, "FLYING")):
        x0, x1 = boxes[side]
        c = x1 - x0
        decals.append(Decal(stamps[word], (x0 + c * 0.55, side * 2.9, 0.15),
                            (1, 0, 0), fit(stamps[word], h), (0, 0, 1), INK))
    paint(objs, decals)
    fin_mark(objs, L)


def cessna(objs, L, H):
    """White, words and badge on the wings, the mark on the fin, TALKFLYING for a registration."""
    recolour(objs, lambda r, g, b: whiten(r, g, b, 0.62) if is_neutral(r, g, b)
             else (keep_luma(r, g, b, GRAPHITE) if b > 0.22 and b > r + 0.10 else None))
    text, badge = text_stamp("TALK FLYING"), badge_stamp()
    decals = []
    for side in (RIGHT, LEFT):
        bx = wing_box(objs, side)
        if bx is None:
            continue
        x0, x1 = bx
        chord = x1 - x0
        above, below = wing_pair(side, text, badge)
        for st, z, facing in ((above, 0.30, (0, 0, 1)), (below, -0.30, (0, 0, -1))):
            h = chord * (0.58 if st is text else 0.46)
            decals.append(Decal(st, (x0 + chord * 0.5, side * 2.7, z),
                                (1, 0, 0), fit(st, h, 3.8), facing, INK))
    paint(objs, decals)
    # the fin mark, the wings and the registration go on as GEOMETRY: this model's two fuselage sides
    # share their texels, so anything painted into the texture appears mirrored on the far side
    # THE 172'S FIN IS NOT SOMETHING THE FITTER CAN READ, so this one is placed by hand. Its fin and its
    # rudder are two zero-thickness sheets with vertices at their corners and nowhere else -- eight points
    # for the fin, four for the rudder -- so a convex hull of them runs forward along the dorsal fairing
    # and aft across the rudder, and the biggest square inside it is on neither the fin nor the white.
    # Read off the model, in (x, z):
    #     fin     top (-3.32, 1.27) to (-3.68, 1.26), leading edge down through (-2.51, 0.40) to
    #             (-2.35, -0.28), root at (-2.93, -0.42)
    #     rudder  (-4.06, 1.27) (-3.68, 1.25) (-3.45, -0.31) (-2.93, -0.43) -- so the hinge runs from
    #             (-3.68, 1.25) down to (-3.45, -0.31) and everything aft of it is graphite
    # and the boom's deck under it is at about z 0. That leaves a white panel the shape of a wedge, and
    # 0.50 m of mark centred at (-3.15, 0.50) sits inside it with 60 mm to spare at the leading edge and
    # 200 mm at the hinge. The owner: "move the talk flying logo to the white part of the tail a little
    # lower and further forward".
    # -3.10, not -3.15: "move the logo on the tail of the Cessna forward 2 inches" (0.051 m). That spends
    # most of the 60 mm of leading-edge margin, so the ray count in the log is the check -- a mark hanging
    # off the front of the fin loses rays and comes out with a bite taken out of it.
    fin_mark(objs, L, box=(-3.10, 0.50, 0.50))
    # THE WINGS OUT OF THE LOGO, ON THE REAR FUSELAGE, BOTH SIDES -- the owner's words. The badge takes
    # the forward end of the bay, where the tailcone is deepest, and the registration goes in behind it,
    # which is the order the two sit in on a real aeroplane anyway. Both were measured, not placed: the
    # registration used to be put at a fraction of the length with a guessed height, and most of it
    # missed the skin and came out as a row of dashes.
    bay = rear_bay(objs, L)
    if bay:
        # and then only the part of it that is ALL ONE COLOUR -- the stripe behind the cabin is graphite
        # in this livery and a black mark on it is a black mark on black
        band = side_slot(objs, bay[0], bay[1], clear=0.04)
        if band:
            bay = clean_paint(objs, bay[0], bay[1], (band[0] + band[1]) * 0.5, band[1] - band[0])
    x_aft, x_reg = (bay[0] + 0.06, bay[1]) if bay else (0.0, 0.0)
    if bay:
        box = fitted_side_badge(objs, bay, badge, frac=(0.52, 0.97))
        if box:
            cx, cz, w, h = box
            for sd in (1, -1):
                geo_decal(objs, Decal(badge, (cx, sd * 0.36, cz), (0, 0, 1), (w, h),
                                      (0, sd, 0), INK, depth=0.60),
                          "Mark.Body." + ("R" if sd > 0 else "L"))
            x_reg = cx - w * 0.5 - 0.12
    slot = side_slot(objs, x_aft, x_reg, clear=0.05) if x_reg - x_aft > 0.25 else None
    if slot:
        z0, z1 = slot
        st = text_stamp("TALKFLYING", slant=0.26)
        wh = fit(st, (z1 - z0) * 0.80, x_reg - x_aft)
        print("LIVERY   registration: %.2f x %.2f at x %.2f z %.2f"
              % (wh[0], wh[1], (x_aft + x_reg) * 0.5, (z0 + z1) * 0.5))
        for sd in (1, -1):
            geo_decal(objs, Decal(st, ((x_aft + x_reg) * 0.5, sd * 0.34, (z0 + z1) * 0.5),
                                  (0, 0, 1), wh, (0, sd, 0), INK, depth=0.55),
                      "Mark.Reg." + ("R" if sd > 0 else "L"))
    else:
        print("LIVERY   registration: no room for it behind the badge")


def biplane_wings(objs, side):
    """A biplane's spanwise slab holds BOTH wings, one above the other, and taking its extremes reports a
    5.3 m chord on a 1.5 m wing. They are separated in height, not in station, so split on z."""
    hs = _halfspan(objs)
    sel = [(v.co.x, v.co.z) for o in objs for v in o.data.vertices
           if 0.45 * hs < v.co.y * side < 0.70 * hs]
    if len(sel) < 24:
        return []
    zs = sorted(p[1] for p in sel)
    mid = (zs[0] + zs[-1]) * 0.5
    out = []
    for lo, hi, label in ((mid, 1e9, "upper"), (-1e9, mid, "lower")):
        xs = sorted(x for x, z in sel if lo <= z < hi)
        zz = [z for x, z in sel if lo <= z < hi]
        if len(xs) < 12:
            continue
        x0, x1 = xs[int(len(xs) * 0.02)], xs[int(len(xs) * 0.98)]
        out.append((x0, x1, sum(zz) / len(zz), label))
        print("LIVERY   %s wing %+d: x %.2f .. %.2f  chord %.2f  z %.2f"
              % (label, side, x0, x1, x1 - x0, out[-1][2]))
    return out


def stearman(objs, L, H):
    """Keeps its blue and yellow.

    The wordmark goes ONCE, centred on the upper wing's centre section in the gap between the roundels --
    a biplane has nowhere else for it to read as one phrase. The badge stays out on the lower wings'
    undersides, one per panel, because the lower wing has no centre section: at y = 0 it is the fuselage,
    and a mark placed there lands on the belly.
    """
    text, badge = text_stamp("TALK FLYING"), badge_stamp()
    decals = []
    for x0, x1, zc, label in biplane_wings(objs, RIGHT):
        chord = x1 - x0
        if label == "upper":
            decals.append(Decal(text, (x0 + chord * 0.5, 0.0, zc), (1, 0, 0),
                                fit(text, chord * 0.62, 4.2), (0, 0, 1), INK))
        else:
            for side in (RIGHT, LEFT):
                decals.append(Decal(badge, (x0 + chord * 0.5, side * 2.3, zc), (1, 0, 0),
                                    fit(badge, chord * 0.50, 2.8), (0, 0, -1), INK))
    paint(objs, decals)
    # "on the stearman move it down about 6 inches and it will be just right" -- the fitter puts the mark
    # where the biggest square fits, which on this fin is a little high
    fin_mark(objs, L, lower=0.152)


def fuselage_axis(objs, L):
    """The height of the fuselage's own centreline, and its radius. An airliner's bounding box is dominated
    by a fin two-thirds as tall as the aeroplane is wide, so the box centre sits well above the tube, and a
    marking placed from it lands on nothing."""
    mid = [v.co.z for o in objs for v in o.data.vertices if abs(v.co.x) < 0.22 * L and abs(v.co.y) < 0.05 * L]
    if len(mid) < 40:
        return 0.0, 1.0
    mid.sort()
    zc = mid[len(mid) // 2]
    r = (mid[int(len(mid) * 0.97)] - mid[int(len(mid) * 0.03)]) * 0.5
    print("LIVERY   fuselage centreline z %.2f  radius %.2f" % (zc, r))
    return zc, r


def seven37(objs, L, H):
    """Retro silver: TALK FLYING AIRLINES along the forward fuselage where the stock titles were, and the
    logo itself -- black plate and all -- on the fin where the flag was."""
    recolour(objs, lambda r, g, b: keep_luma(r, g, b, SILVER, ref=0.45))
    zc, rad = fuselage_axis(objs, L)
    for sd in (1, -1):
        tag = "R" if sd > 0 else "L"
        # TAKE OFF WHAT CAME WITH THE MODEL -- the REPUBLIK INDONESIA titles with the garuda crest behind
        # them, and the roundel under the cockpit -- by scrubbing them out of the model's own paint. The
        # earlier version laid a plate of flat SILVER over them, and on a skin recolour() has clamped to
        # near-white that reads as a grey rectangle: "instead of painting the area behind the talk flying
        # airlines grey can you just type talk flying airlines over the aircraft's current paint".
        #
        # EACH RECTANGLE IS MEASURED, not guessed. The texels carrying each stock marking were mapped back
        # through the mesh (scrub's own TF_WINDOW probe), and they come out at:
        #     titles + crest   x  4.8 .. 13.9    z -1.68 .. -0.76
        #     roundel          x 15.0 .. 15.8    z -2.89 .. -2.20
        # Tight matters: the first attempt covered the whole forward fuselage and took the window line and
        # the cheatline with it.
        scrub(objs, (9.36, sd * rad * 0.82, -1.22), (0, 0, 1),
              (10.6, 1.5), (0, sd, 0), rad * 0.95, "Scrub.Titles." + tag, tol=0.02, soft=0.02)
        scrub(objs, (15.42, sd * rad * 0.82, -2.55), (0, 0, 1),
              (1.5, 1.3), (0, sd, 0), rad * 0.95, "Scrub.Roundel." + tag)
        # ... and the flag on the fin. It used to be covered by the badge, which tied where the badge sits
        # to where the flag happens to be; with the flag gone the badge can go where it looks right.
        # Measured the same way: x -15.89 .. -13.95, z 1.86 .. 3.04.
        scrub(objs, (-14.92, sd * 0.30, 2.45), (0, 0, 1),
              (2.4, 1.6), (0, sd, 0), 0.6, "Scrub.FinFlag." + tag)
    # ... and only then the livery, so it lands on clean paint. The titles go WHERE THE STOCK ONES WERE,
    # high on the forward fuselage, rather than down across the window line.
    st = text_stamp("TALK FLYING AIRLINES")
    for sd in (1, -1):
        geo_decal(objs, Decal(st, (L * 0.215, sd * rad * 0.82, zc + rad * 0.74),
                              (0, 0, 1), fit(st, L * 0.021, L * 0.27), (0, sd, 0), INK, depth=rad * 0.95),
                  "Mark.Title." + ("R" if sd > 0 else "L"))
    # THE FIN: the logo as drawn, black plate and silver mark, sized and placed to the fin's own outline.
    # It is aimed in metres rather than as a fraction of the fin, and `keep` is loose so position wins over
    # size. CLEAR OF THE LEADING EDGE: fitting inside the outline is not enough, because a fin's outline IS
    # its leading edge -- a corner on it is on the curve, and the mark folds round the front, which is what
    # the owner saw. `clear` holds the mark 0.30 m in from every edge and the target sits it lower, where
    # the fin is wider. A nose-on render is the test; a side view hides a fold and a 3/4 view flatters it.
    box = fitted_fin_badge(objs, L, logo_stamp(), at=(-14.60, 2.10), keep=0.55, margin=0.97, clear=0.30)
    if box:
        cx, cz, w, h = box
        for sd in (1, -1):
            d = Decal(logo_stamp(), (cx, sd * 0.12, cz), (0, 0, 1), (w, h), (0, sd, 0), INK, depth=0.8)
            d.literal = True
            # a square mark wants a square grid: at the default 96 x 28 the cells are three times taller
            # than they are wide, and a dropped cell at a panel step takes a visible bite out of the edge
            geo_decal(objs, d, "Mark.Fin." + ("R" if sd > 0 else "L"), nv=96)


# =============================================================================================================
# the three gliders
# =============================================================================================================
def wing_cut(objs, y, L, side, step=0.015):
    """The wing's chord and both its skins at one spanwise station, BY RAY-CAST.

    Vertices cannot answer this on a sailplane. The 2-33's wing skin has vertices at the root and at the
    tip and nowhere in between, so wing_box()'s spanwise slab of vertices finds only the aileron and calls
    a 1.4 m wing 0.40 m of chord -- the same trap the 172's tailcone set for rear_bay(). A ray dropped from
    above at a station this far outboard meets the wing and nothing else, and the longest unbroken run of
    stations that hit IS the chord.

    Returns (leading x, trailing x, top skin z, bottom skin z).
    """
    tree = surface_tree(objs)
    reach = max(L, 4.0)

    def run(down):
        n = Vector((0, 0, -1.0 if down else 1.0))
        runs, cur = [], []
        x = -L
        while x <= L:
            loc, _, _, _ = tree.ray_cast(Vector((x, y, -n.z * reach)), n, reach * 2.2)
            if loc is not None:
                cur.append((x, loc.z))
            elif cur:
                runs.append(cur)
                cur = []
            x += step
        if cur:
            runs.append(cur)
        return max(runs, key=len) if runs else None

    top, bot = run(True), run(False)
    if not top or not bot or len(top) < 6:
        print("LIVERY   wing %+d at y %.2f: no skin under the rays" % (side, y))
        return None
    x0, x1 = max(top[0][0], bot[0][0]), min(top[-1][0], bot[-1][0])
    zt = sum(z for _, z in top) / len(top)
    zb = sum(z for _, z in bot) / len(bot)
    print("LIVERY   wing %+d at y %.2f: chord %.2f (x %.2f .. %.2f)  skin z %.2f / %.2f"
          % (side, y, x1 - x0, x0, x1, zb, zt))
    return x0, x1, zt, zb


def glider(objs, L, H, out=0.56, hfrac=(0.52, 0.56), ink=INK):
    """The owner's layout for the sailplanes: the winged badge over the LEFT wing and under the RIGHT, the
    wordmark over the RIGHT and under the LEFT, and the whole logo on the side of the tail -- so from any
    angle, above or below, you get one of each.

    ALL OF IT AS GEOMETRY, none of it painted into the texture. Two of these three models have no texture
    to paint into: the 2-33's skin is flat material colour and so is the CC0 sailplane's, and the Grob's
    one 1024 atlas is shared between its two wings, which is how the 172's registration came out mirrored.
    geo_decal asks the file for nothing but the surface itself.
    """
    text, badge = text_stamp("TALK FLYING"), badge_stamp()
    hs = _halfspan(objs)
    for side in (RIGHT, LEFT):
        yc = side * out * hs
        cut = wing_cut(objs, yc, L, side)
        if cut is None:
            continue
        x0, x1, zt, zb = cut
        chord = x1 - x0
        cx = x0 + chord * 0.5
        above, below = (badge, text) if side == LEFT else (text, badge)
        for st, z, facing, tag in ((above, zt, (0, 0, 1), "Top"), (below, zb, (0, 0, -1), "Bot")):
            w, h = fit(st, chord * (hfrac[0] if st is badge else hfrac[1]), hs * 0.80)
            # a long thin mark on a thin wing: the grid is stretched along the span to match, and the
            # depth is kept near the chord so a mark on the top cannot reach round to the bottom skin
            geo_decal(objs, Decal(st, (cx, yc, z), (1, 0, 0), (w, h), facing, ink,
                                  depth=max(0.22, chord * 0.45)),
                      "Mark.Wing.%s.%s" % (tag, "R" if side > 0 else "L"), nu=128, nv=40)
    fin_mark(objs, L, ink=ink)


def glider_glass(objs, L, H):
    """The CC0 ship carries 0.46 m of chord on a 12.66 m span -- an aspect ratio of 27, half again as
    slender as the Grob. The same fraction of chord puts a mark on it half the size, relative to the
    aeroplane, as the one on the other two, so this wing gives up a little more of its chord.
    """
    glider(objs, L, H, hfrac=(0.66, 0.70))


PLANES = {
    "pa18-cub-like":      (os.path.join(SRC, "pa18-cub/pa18-cub-helijah.glb"), 10.63, supercub),
    "c172-like":          (os.path.join(SRC, "c172/c172-tonywony.glb"), 10.92, cessna),
    "stearman-pt17-like": (os.path.join(SRC, "stearman-pt17/stearman-pt17-helijah.glb"), 9.63, stearman),
    "boeing-737-like":    (os.path.join(SRC, "boeing-737/b737-800-mirzaarrafi.glb"), 34.11, seven37),
    # THE THREE GLIDERS, for the podcast animations.
    #   2-33   OURS, drawn by tools/model_glider233.py from the config's own numbers. The downloaded
    #          Schweizer was a 15.5 m span on a 13.3 m fuselage -- the real aeroplane is 15.5 on 7.9 --
    #          so its wings looked stubby and no scaling could fix a ratio. The owner's word on
    #          2026-10-22 was "the wings are too short, it is a lost cause"; it was deleted, and this
    #          took its place, with the lift struts the download never had. Run the model script first.
    #   Grob   helijah's Grob Astir, 15.0 m span, 6.82 m long -- which is the real Astir to within 40 mm,
    #          so this mesh is dimensionally honest. It is in the repo already, under the 2-33's folder:
    #          the game draws the 2-33 procedurally and keeps this model unused (see that LICENSE.txt).
    #   Swift  THERE IS NO SWIFT S-1 MODEL. docs/research/AIRCRAFT-3D-MODELS.md 15 found none and still
    #          holds. This is EGPJET's CC0 glass single-seater standing in for one, scaled to the Swift's
    #          12.66 m span -- a modern 15 m ship with winglets and a T-tail, not an aerobat.
    "glider-2-33-like":   (os.path.join(REPO, "build/models/glider-2-33-like.glb"), 15.44, glider),
    "grob-astir-like":    (os.path.join(REPO, "unity/Assets/_Project/Resources/Models/glider-2-33-like",
                                        "glider-2-33-like.obj"), 15.00, glider),
    "glider-swift-s1-like": (os.path.join(SRC, "sailplane-modern/FFGL.blend"), 12.66, glider_glass),
}

# where an aeroplane's credit line lives, when it is not beside the download under assets-incoming/models
LICENCES = {
    "grob-astir-like": os.path.join(REPO, "unity/Assets/_Project/Resources/Models/glider-2-33-like"),
    "glider-swift-s1-like": os.path.join(SRC, "sailplane-modern"),
}


def render(objs, ident, L, H):
    """Three views on a clean background. No sky and no ground -- the podcast widget composites these."""
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.film_transparent = True
    sc.render.resolution_x, sc.render.resolution_y = 1600, 1000
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    w = bpy.data.worlds.new("W")
    sc.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.68, 0.78, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 1.1
    sun = bpy.data.objects.new("S", bpy.data.lights.new("S", type='SUN'))
    bpy.context.collection.objects.link(sun)
    sun.data.energy = 3.4
    sun.data.angle = math.radians(4)
    sun.rotation_euler = (math.radians(52), math.radians(6), math.radians(140))
    cam = bpy.data.objects.new("C", bpy.data.cameras.new("C"))
    bpy.context.collection.objects.link(cam)
    sc.camera = cam
    # frame on the SPAN as well as the length -- a Cub is half as long as it is wide, and framing on
    # length alone put the camera inside the wing
    span = max(abs(v.co.y) for o in objs for v in o.data.vertices) * 2
    size = max(L, H, span, 1.0)
    # "belly" is STRAIGHT under, not a three-quarter view from below: the Super Cub's banner has to be read
    # by somebody standing on the ground looking up, and that is the only view that settles which word
    # comes first. Reasoning about it instead of looking got the order backwards once already.
    for name, d, lens in (("3q", Vector((-1.0, -1.25, 0.40)), 68),
                          ("belly", Vector((0.0, -0.001, -1.0)), 55),
                          ("side", Vector((0.0, -1.0, 0.06)), 80)):
        cam.data.lens = lens
        cam.location = d.normalized() * size * 2.1
        cam.rotation_euler = (-cam.location).normalized().to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(OUT, "%s-talkflying-%s.png" % (ident, name))
        bpy.ops.render.render(write_still=True)


def main():
    os.makedirs(OUT, exist_ok=True)
    wanted = argv or list(PLANES)
    for ident in wanted:
        if ident not in PLANES:
            print("LIVERY   unknown aeroplane %s" % ident)
            continue
        path, span, fn = PLANES[ident]
        if not os.path.exists(path):
            print("LIVERY   %s: no source model at %s" % (ident, path))
            continue
        print("LIVERY %s" % ident)
        objs, L, H = load_canonical(path, span)
        fn(objs, L, H)
        glb = os.path.join(OUT, "%s-talkflying.glb" % ident)
        bpy.ops.object.select_all(action='SELECT')
        bpy.ops.export_scene.gltf(filepath=glb, export_format='GLB', use_selection=True, export_apply=True)
        print("LIVERY   wrote %s" % glb)
        render(objs, ident, L, H)
    print("LIVERY")
    print("LIVERY CREDIT REQUIRED (CC BY 4.0) -- these are repaints, so the credit travels with them:")
    for ident in wanted:
        lic = os.path.join(LICENCES.get(ident, os.path.join(SRC, ident.replace("-like", ""))), "LICENSE.txt")
        if os.path.exists(lic):
            for line in open(lic):
                # the CC0 sailplane asks for no attribution at all and offers a credit instead; print it,
                # because a repaint that travels without saying where the mesh came from is a nuisance
                # to everyone who meets it later
                if line.startswith("CREDIT LINE:") or line.startswith("Suggested (optional) credit:"):
                    print("LIVERY   " + line.split(":", 1)[1].strip())


main()
