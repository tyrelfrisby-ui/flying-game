"""Shared helpers for repainting the FlightGear models with projected paint textures.

The paint is a set of planar projections: each face of a painted part goes to the
port-side view, starboard-side view, top view or a plain colour, and each view is a
numpy image computed as a 2-D function of the projected coordinates (stripes, caps,
lettering).  Blender axes: nose +X, port wing +Y, up +Z.
"""
import bpy, bmesh, os, numpy as np

ART = os.path.join(os.path.dirname(__file__), 'art')


def srgb(c):
    return np.array([c[0] / 255, c[1] / 255, c[2] / 255, 1.0], dtype=np.float32)


class View:
    """A projection: maps (a,b) world coords to (u,v) and owns an RGBA float image."""
    def __init__(self, name, a_rng, b_rng, w, h, flip_a=False, axes=(0, 2)):
        self.axes = axes
        self.name, self.a0, self.a1, self.b0, self.b1 = name, a_rng[0], a_rng[1], b_rng[0], b_rng[1]
        self.w, self.h, self.flip_a = w, h, flip_a
        # world coordinate of each pixel centre
        ua = (np.arange(w) + 0.5) / w
        vb = (np.arange(h) + 0.5) / h
        av = (self.a1 - ua * (self.a1 - self.a0)) if flip_a else (self.a0 + ua * (self.a1 - self.a0))
        bv = self.b0 + vb * (self.b1 - self.b0)
        self.A, self.B = np.meshgrid(av, bv)          # shape (h, w); row 0 = bottom
        self.img = np.ones((h, w, 4), dtype=np.float32)

    def coords(self, co):
        return co[self.axes[0]], co[self.axes[1]]

    def uv(self, a, b):
        u = (a - self.a0) / (self.a1 - self.a0)
        if self.flip_a:
            u = 1 - u
        return u, (b - self.b0) / (self.b1 - self.b0)

    def px(self, a, b):
        u, v = self.uv(a, b)
        return u * self.w, v * self.h

    def fill(self, mask, col):
        self.img[mask] = srgb(col)

    def stamp(self, png, a_start, a_end, b_lo, b_hi, alpha_tint=None):
        """Paste png (read upright) into the world rect; text reads in increasing u."""
        im = bpy.data.images.load(os.path.join(ART, png), check_existing=True)
        sw, sh = im.size
        src = np.array(im.pixels[:], dtype=np.float32).reshape(sh, sw, 4)
        x0, y0 = self.px(a_start, b_lo)
        x1, y1 = self.px(a_end, b_hi)
        xl, xr = sorted((x0, x1)); yl, yr = sorted((y0, y1))
        c0, c1, r0, r1 = int(xl), int(np.ceil(xr)), int(yl), int(np.ceil(yr))
        acc = np.zeros((r1 - r0, c1 - c0, 4), dtype=np.float32)
        ss = 3
        for i in range(ss):
            for j in range(ss):
                cc = (np.arange(c0, c1) + (i + 0.5) / ss - xl) / (xr - xl)
                rr = (np.arange(r0, r1) + (j + 0.5) / ss - yl) / (yr - yl)
                inside = (cc[None, :] >= 0) & (cc[None, :] < 1) & (rr[:, None] >= 0) & (rr[:, None] < 1)
                si = np.clip((cc * sw).astype(int), 0, sw - 1)
                sj = np.clip((rr * sh).astype(int), 0, sh - 1)
                s = src[sj[:, None], si[None, :]].copy()
                s[~inside] = 0
                acc += s
        acc /= ss * ss
        a = acc[..., 3:4]
        rgb = np.where(a > 1e-4, acc[..., :3] / np.maximum(a, 1e-4), 0)
        dst = self.img[r0:r1, c0:c1]
        dst[..., :3] = dst[..., :3] * (1 - a) + rgb * a
        self.img[r0:r1, c0:c1] = dst

    def stamp_warp(self, png, a_start, a_end, lo, hi):
        """Like stamp, but the letter height follows the surface: at span position a the art fills b = lo(a)..hi(a)
        (e.g. a fixed band of the local chord on a tapered wing), so letters shrink toward the tip (trapezoid warp)."""
        im = bpy.data.images.load(os.path.join(ART, png), check_existing=True)
        sw, sh = im.size
        src = np.array(im.pixels[:], dtype=np.float32).reshape(sh, sw, 4)
        aa = np.array([a_start, a_end]); blo = min(lo(a_start), lo(a_end)); bhi = max(hi(a_start), hi(a_end))
        x0, y0 = self.px(a_start, blo); x1, y1 = self.px(a_end, bhi)
        xl, xr = sorted((x0, x1)); yl, yr = sorted((y0, y1))
        c0, c1, r0, r1 = int(xl), int(np.ceil(xr)), int(yl), int(np.ceil(yr))
        acc = np.zeros((r1 - r0, c1 - c0, 4), dtype=np.float32)
        ss = 3
        for i in range(ss):
            for j in range(ss):
                pc = np.arange(c0, c1) + (i + 0.5) / ss; pr = np.arange(r0, r1) + (j + 0.5) / ss
                a = a_start + (pc - x0) / (x1 - x0) * (a_end - a_start)          # world span coordinate per column
                b = blo + (pr - y0) / (y1 - y0) * (bhi - blo)                    # world chord coordinate per row
                cc = (pc - xl) / (xr - xl)
                l, h = lo(a), hi(a)
                rr = (b[:, None] - l[None, :]) / (h - l)[None, :]
                inside = (cc[None, :] >= 0) & (cc[None, :] < 1) & (rr >= 0) & (rr < 1)
                si = np.clip((cc * sw).astype(int), 0, sw - 1)
                sj = np.clip((rr * sh).astype(int), 0, sh - 1)
                s = src[sj, np.broadcast_to(si[None, :], sj.shape)].copy()
                s[~inside] = 0
                acc += s
        acc /= ss * ss
        a = acc[..., 3:4]
        rgb = np.where(a > 1e-4, acc[..., :3] / np.maximum(a, 1e-4), 0)
        dst = self.img[r0:r1, c0:c1]
        dst[..., :3] = dst[..., :3] * (1 - a) + rgb * a
        self.img[r0:r1, c0:c1] = dst

    def to_image(self, path):
        im = bpy.data.images.new(self.name, self.w, self.h, alpha=False)
        im.pixels.foreach_set(self.img.ravel())
        im.filepath_raw = path
        im.file_format = 'PNG'
        im.save()
        return im


def mat_image(name, image, rough=0.45):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get('Principled BSDF')
    tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = image
    tex.interpolation = 'Linear'
    nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = rough
    return m


def mat_colour(name, col, rough=0.45, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    c = srgb(col)
    lin = [((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c[:3]]
    bsdf.inputs['Base Color'].default_value = (*lin, 1)
    bsdf.inputs['Roughness'].default_value = rough
    bsdf.inputs['Metallic'].default_value = metal
    m.diffuse_color = (*lin, 1)
    return m


def join(objs, name):
    objs = [o for o in objs if o is not None]
    if not objs:
        return None
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name; o.data.name = name
    return o


def project(obj, assign):
    """assign(normal, centre) -> (material_index, view or None). Rewrites UV layer 0."""
    me = obj.data
    if not me.uv_layers:
        me.uv_layers.new(name='UVMap')
    uvl = me.uv_layers[0].data
    for p in me.polygons:
        mi, view = assign(p.normal, p.center)
        p.material_index = mi
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if view is None:
                uvl[li].uv = (0.5, 0.5)
            else:
                a, b = view.coords(co)
                uvl[li].uv = view.uv(a, b)


def mat_glass(name, col=(170, 190, 205), alpha=0.3):
    m = mat_colour(name, col, rough=0.05)
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Alpha'].default_value = alpha
    try:
        m.surface_render_method = 'BLENDED'
    except Exception:
        pass
    m.blend_method = 'BLEND' if hasattr(m, 'blend_method') else None
    return m
