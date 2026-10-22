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
from mathutils import Vector

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
    if bold:
        cu.bevel_depth = 0.012          # fattens the strokes; the mark is a heavy sans
        cu.resolution_u = 3
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


def logo_stamp():
    if "logo" not in _stamps:
        img = bpy.data.images.load(LOGO)
        _stamps["logo"] = (list(img.pixels), img.size[0], img.size[1])
        bpy.data.images.remove(img)
    return _stamps["logo"]


def sample(stamp, u, v):
    """Nearest-neighbour sample; returns (r, g, b, a) with a = 0 outside.

    The project's logo PNG is a white mark on an OPAQUE BLACK field, so its alpha channel says nothing. Its
    luminance does -- the same trick FlyingGame/Decal uses in the game. So coverage is max(alpha, luminance).
    """
    px, w, h = stamp
    if not (0.0 <= u < 1.0 and 0.0 <= v < 1.0):
        return None
    i = (int(v * h) * w + int(u * w)) * 4
    r, g, b, a = px[i], px[i + 1], px[i + 2], px[i + 3]
    cov = max(a, 0.2126 * r + 0.7152 * g + 0.0722 * b)
    return (r, g, b, cov)


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

    def __init__(self, stamp, origin, vdir, size, facing, colour):
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

    def uv_of(self, p):
        d = Vector(p) - self.o
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
                            for c in range(3):
                                buf[i + c] = buf[i + c] * (1 - cov) + d.colour[c] * cov
                            hits += 1
    for key, buf in buffers.items():
        W, H, img = sizes[key]
        img.pixels = buf
        img.update()
    print("LIVERY   painted %d texels across %d images" % (hits, len(buffers)))


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
    k = true_span / size[SP]

    # which end is the nose: the widest body section is forward of the midpoint on everything we fly
    lo, hi = mn[LN], mx[LN]
    band = (hi - lo) / 20.0
    widest_f, widest = 0.5, -1.0
    for i in range(21):
        f = i / 20.0
        c = lo + (hi - lo) * f
        sel = sorted(abs(p[SP]) for p in pts if abs(p[LN] - c) <= band and abs(p[SP]) * k < 0.9)
        if len(sel) < 6:
            continue
        w = sel[int(len(sel) * 0.9)]
        if w > widest:
            widest, widest_f = w, f
    sgn = 1.0 if widest_f < 0.5 else -1.0
    # and which way is up: an aeroplane has more of itself above the wing line than below it near the nose
    ctr = (mn + mx) * 0.5
    upper = sum(1 for p in pts if p[HG] > ctr[HG])
    usg = 1.0 if upper <= len(pts) * 0.5 else 1.0        # both references are already z-up; keep it simple

    for o in objs:
        me = o.data
        for v in me.vertices:
            w = o.matrix_world @ v.co
            v.co = Vector((-(w[LN] - ctr[LN]) * sgn * k, (w[SP] - ctr[SP]) * k, (w[HG] - ctr[HG]) * k * usg))
        o.matrix_world.identity()
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
    print("LIVERY   canonical: span %.2f  length %.2f  height %.2f" %
          (size[SP] * k, size[LN] * k, size[HG] * k))
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
    hs = _halfspan(objs)
    xs = sorted(v.co.x for o in objs for v in o.data.vertices
                if 0.34 * hs < v.co.y * side < 0.62 * hs)
    if len(xs) < 12:
        return None
    bx = xs[int(len(xs) * 0.01)], xs[int(len(xs) * 0.99)]
    print("LIVERY   wing side %+d: x %.2f .. %.2f  chord %.2f  (%d verts)" % (side, bx[0], bx[1], bx[1] - bx[0], len(xs)))
    return bx


def fin_box(objs, L):
    """The fin: on the centreline, aft, and in the upper part of what is there."""
    cand = [v.co for o in objs for v in o.data.vertices if abs(v.co.y) < 0.10 and v.co.x < -0.12 * L]
    if len(cand) < 20:
        return None
    zs = sorted(p.z for p in cand)
    zcut = zs[int(len(zs) * 0.72)]
    up = [p for p in cand if p.z >= zcut]
    bx = _biggest_cluster([p.x for p in up])
    if not bx:
        return None
    z0, z1 = zcut, max(p.z for p in up)
    print("LIVERY   fin: x %.2f .. %.2f  z %.2f .. %.2f" % (bx[0], bx[1], z0, z1))
    return bx[0], bx[1], z0, z1


def supercub(objs, L, H):
    """Banner-tow lettering under the wings, one word each, tops toward the leading edge."""
    decals = []
    for side, word in ((1, "TALK"), (-1, "FLYING")):
        bx = wing_box(objs, side)
        if bx is None:
            continue
        x0, x1 = bx
        chord = x1 - x0
        # reading runs -Y so that it reads left to right when you are under it looking up
        decals.append(Decal(text_stamp(word), (x0 + chord * 0.55, side * 2.9, -0.15),
                            (1, 0, 0), (3.4, chord * 0.78), (0, 0, -1), INK))
    paint(objs, decals)


def cessna(objs, L, H):
    """White, wordmark top and bottom of both wings, logo on the fin, TALKFLYING for a registration."""
    recolour(objs, lambda r, g, b: whiten(r, g, b, 0.62) if is_neutral(r, g, b)
             else (keep_luma(r, g, b, GRAPHITE) if b > 0.22 and b > r + 0.10 else None))
    decals = []
    for side in (1, -1):
        bx = wing_box(objs, side)
        if bx is None:
            continue
        x0, x1 = bx
        chord = x1 - x0
        for z, facing in ((0.30, (0, 0, 1)), (-0.30, (0, 0, -1))):
            decals.append(Decal(text_stamp("TALK FLYING"), (x0 + chord * 0.5, side * 2.7, z),
                                (1, 0, 0), (3.6, chord * 0.62), facing, INK))
    fb = fin_box(objs, L)
    if fb:
        xa, xb, z0, z1 = fb
        for s in (1, -1):
            decals.append(Decal(logo_stamp(), ((xa + xb) * 0.5, 0.0, (z0 + z1) * 0.5),
                                (0, 0, 1), ((xb - xa) * 0.85, (z1 - z0) * 0.85), (0, s, 0), INK))
    # the registration, in an N-number's slanted caps, on the aft fuselage
    for s in ((-1,) if os.environ.get("ONE_SIDE") else (1, -1)):
        decals.append(Decal(text_stamp("TALKFLYING", slant=0.28), (-L * 0.22, 0.0, 0.05),
                            (0, 0, 1), (L * 0.30, L * 0.055), (0, s, 0), INK))
    paint(objs, decals)


def stearman(objs, L, H):
    """Keeps its blue and yellow; wordmark where the registration goes, logo on the fin."""
    decals = []
    for side in (1, -1):
        bx = wing_box(objs, side)
        if bx is None:
            continue
        x0, x1 = bx
        chord = x1 - x0
        for z, facing in ((0.9, (0, 0, 1)), (-0.9, (0, 0, -1))):
            decals.append(Decal(text_stamp("TALKFLYING"), (x0 + chord * 0.5, side * 2.4, z),
                                (1, 0, 0), (3.0, chord * 0.55), facing, INK))
    fb = fin_box(objs, L)
    if fb:
        xa, xb, z0, z1 = fb
        for s in (1, -1):
            decals.append(Decal(logo_stamp(), ((xa + xb) * 0.5, 0.0, (z0 + z1) * 0.5),
                                (0, 0, 1), ((xb - xa) * 0.8, (z1 - z0) * 0.8), (0, s, 0), INK))
    paint(objs, decals)


def seven37(objs, L, H):
    """Retro silver, art-deco: TALK FLYING AIR along the fuselage, the mark reversed on the fin, both black
    on bare metal, which is the 1930s airliner look the owner asked for."""
    recolour(objs, lambda r, g, b: keep_luma(r, g, b, SILVER, ref=0.45))
    decals = []
    for s in (1, -1):
        decals.append(Decal(text_stamp("TALK FLYING AIR"), (L * 0.10, 0.0, 0.9),
                            (0, 0, 1), (L * 0.34, L * 0.045), (0, s, 0), INK))
    fb = fin_box(objs, L)
    if fb:
        xa, xb, z0, z1 = fb
        for s in (1, -1):
            decals.append(Decal(logo_stamp(), ((xa + xb) * 0.5, 0.0, z0 + (z1 - z0) * 0.55),
                                (0, 0, 1), ((xb - xa) * 0.7, (z1 - z0) * 0.6), (0, s, 0), INK))
    paint(objs, decals)


PLANES = {
    "pa18-cub-like":      (os.path.join(SRC, "pa18-cub/pa18-cub-helijah.glb"), 10.63, supercub),
    "c172-like":          (os.path.join(SRC, "c172/c172-tonywony.glb"), 10.92, cessna),
    "stearman-pt17-like": (os.path.join(SRC, "stearman-pt17/stearman-pt17-helijah.glb"), 9.63, stearman),
    "boeing-737-like":    (os.path.join(SRC, "boeing-737/b737-800-mirzaarrafi.glb"), 34.11, seven37),
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
    for name, d, lens in (("3q", Vector((-1.0, -1.25, 0.40)), 68),
                          ("belly", Vector((-0.35, -0.30, -1.0)), 60),
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
        lic = os.path.join(SRC, ident.replace("-like", ""), "LICENSE.txt")
        if os.path.exists(lic):
            for line in open(lic):
                if line.startswith("CREDIT LINE:"):
                    print("LIVERY   " + line.split(":", 1)[1].strip())


main()
