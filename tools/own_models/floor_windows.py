"""Clear Lexan floor windows for our own Pitts S-2B and Extra 300 (Ty 2026-11-02: "the floor of the pitts is a clear
window you can see through same for the extra").

A GLB post-process, so it runs the same way on a fresh Blender export (build_pitts_s2b.py / build_ea300.py call it
after export_scene.gltf), on the offline update (sequence_card.offline_update) and on a GLB another lane has rebuilt:

  python3 tools/own_models/floor_windows.py unity/Assets/_Project/Resources/Models/pitts-s2b-like/pitts-s2b-like-own.glb ...

What it does, per window (numbers in WINDOWS, builder frame: metres, nose +x, port +y, up +z; glTF x = -x, y = z, z = y):
  * CUTS a real hole: every triangle of the belly skin (outer and, in the Pitts' cockpit U, inner skin), of the
    cockpit floor tub and of anything else named in `cut` is CLIPPED exactly to the window outline (straddling
    triangles are split, their normals and UVs interpolated); nothing is shifted, no node is renamed or removed, so
    the rig (Extra) and the runtime stick/pedal cuts (Pitts, FlyingGame.Core.CockpitLive) bind as before.
  * Floor_Window_Glass: one surface flush with the belly skin's outer line (raycast on the uncut skin), outward
    normals, material `Lexan` (alpha blended, double-sided; 0.090-0.100 in sheet, drawn as a single surface).
  * Floor_Window_Frame: the 19 mm (0.75 in) retaining strip round each pane outside (under the belly) and inside, the
    edge between them, #6 pan-head screw heads (7 mm, 6-sided) at 50 mm (2 in) pitch, material `Floor_Frame`
    (satin aluminium, paint.mat_floor_frame); and the cut edge of the floor (material of the floor it belongs to).
  * Floor_Tubes: the steel tubes the window shows - lower longerons, the cross tubes at each end of a bay, and on the
    Pitts the control torque tube under the cockpit floor with its two stick sockets.
Refuses to run twice on one file (Floor_Window_Glass already there). Python + NumPy (Blender's will do); Pillow is
not needed. docs/pending/floor-windows/README.md has the sources and what is measured vs estimated.
"""
import math
import sys
import types
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
try:
    import PIL  # noqa: F401  (glb_visuals imports it for textures; nothing here reads one)
except ImportError:
    sys.modules['PIL'] = types.ModuleType('PIL'); sys.modules['PIL'].Image = None
import numpy as np
from glb_visuals import Glb

STRIP, HOLE, SCREW_PITCH, SCREW_R = 0.019, 0.006, 0.050, 0.0035   # 0.75 in strip, skin cut 6 mm outside the clear,
                                                                  # screws at 2 in, #6 pan head 7 mm
STRIP_T = 0.0016                                                  # retaining strip thickness (0.063 in)
PANE_T = 0.0024                                                   # 0.090-0.100 in Lexan (inner strip sits on it)
TUBE_R = 0.0095                                                   # 3/4 in steel tube

# Clear openings (x aft end, x fwd end, half-width, corner radius) in the builder frame, and what each aeroplane cuts.
#   cut: (node, material name or None for all, z band (lo, hi), which outline: 'hole' | 'floor' | 'wide')
WINDOWS = {
    'pitts-s2b-like': dict(
        # The cockpit is the open U from the windscreen bulkhead (x -0.13) to the canopy rear bow. Each pane is where
        # the pilot's feet are, between the lower longerons:
        windows=[('front', -0.30, 0.26, 0.180, 0.030),     # from under the front seat's front edge (x -0.26) forward
                                                            # through the bulkhead into the footwell, to the bungee
                                                            # housing (x 0.306)
                 ('rear', -1.10, -0.64, 0.180, 0.030)],    # under the seat front (x -1.04) forward past the rear stick
                                                            # and pedals to the front seat's back (x -0.60)
        # The windscreen-station bulkhead was a full disc from the panel to the belly: the front seat's front edge
        # (z -0.43 at x -0.26) and that disc hid every bit of front-cockpit floor from the front eye. As in the Extra's
        # v15 footwell, it is opened under the front panel and lined (Footwell_Liner, the tub's navy-grey) to x 0.31,
        # so the front pilot's feet - and his window - go forward under the panel as in the aeroplane.
        footwell=dict(x=(-0.130, 0.31), half=0.205, top=-0.10, material='Paint_cabin'),
        cut=[('Fuselage_Painted', None, (-0.80, -0.55), 'hole'),
             ('Cockpit', 'Paint_cabin', (-0.595, -0.575), 'floor')],    # the tub floor box (z -0.59 .. -0.58)
        # the bulkhead disc (and the U wall's end cap) at x -0.130, below the panel, between the footwell walls
        cut_yz=[('Fuselage_Painted', None, (-0.134, -0.126), [(-0.205, -0.80), (0.205, -0.80), (0.205, -0.10), (-0.205, -0.10)])],
        floor_walls=[('Paint_cabin', -0.590, -0.580)],
        # The lower wing's root runs on inside the fuselage to |y| 0.12, 6 cm above the cockpit floor, right across
        # the front window's line of sight; trimmed back to the fuselage wall (between the outer and inner skins).
        wing=('Wing_Lower', 0.02, -1.03, -0.55),
        screw_r=SCREW_R,                                     # #6 pan head, 0.27 in (biplaneforum: #6 screws and nuts)
        longeron_y=0.212, longeron_x=(0.30, -1.32),
        # control torque tube on the centreline under the floor, a socket up to each stick's boot; the rudder pedals
        # (x -0.22 / -0.80, |y| 0.13) hang on a cross shaft under their heels instead of standing on floorboard
        torque=dict(x=(-0.15, -0.97), z=-0.635, r=0.016, sticks=(-0.17, -0.93), floor=-0.590),
        pedal_bars=dict(x=(-0.22, -0.80), z=-0.552, half=0.20)),
    'extra-300-like': dict(
        # The measured reference has its own belly window (x -1.500 .. -1.159, |y| 0.197 .. 0.211), covered by the
        # navy Cabin box and the tub floor. In our cockpit it lies under the rear seat pan (front edge x -1.42, top
        # z +0.01): from the rear eye only floor forward of x ~-1.20 can be seen at all. So the pane keeps that hole's
        # aft end and sides (less 7 mm) and runs on forward to x -0.95, under the rear pilot's calves, aft of the rear
        # pedals (x -0.75); the radio stack's foot (x -1.11 .. -1.16) stands over it.
        windows=[('rear', -1.492, -0.95, 0.195, 0.020),
                 # Ty 2026-11-04 19:32: clear floor panels in BOTH cockpits. The front pane runs from just forward of the
                 # front seat's front edge (x -0.55) under the front pilot's calves and stick to short of the front
                 # pedals' heel bar (x 0.22); nothing between the tub floor (z -0.084) and the belly (z -0.63 .. -0.68)
                 # there: no wing carry-through, gear or engine (measured on the v-front-seat GLB).
                 ('front', -0.52, 0.14, 0.170, 0.020)],
        footwell=None, cut_yz=[],
        cut=[('Fuselage_Painted', None, (-0.80, -0.50), 'hole'),
             ('Window_Glass', None, (-0.80, -0.50), 'wide'),     # the old belly glass (the side windows stay)
             ('Window_Frame', None, (-0.80, -0.50), 'wide'),     # and its raised frame
             ('Cabin', 'Paint_cabin', (-0.110, -0.084), 'floor'),
             ('Cockpit_Shell', 'Paint_cabin', (-0.087, -0.081), 'floor'),    # tub floor quads at ZF -0.084
             ('Footwell_Liner', None, (-0.087, -0.081), 'floor')],          # the front footwell's floor (x > 0.0)
        floor_walls=[('Paint_cabin', -0.106, -0.084)],
        skin_extra=['Window_Glass'],
        wing=None,
        screw_r=0.0051,      # Parts Catalog EA 300 PC-23201A3 attachment: AN526C-1032-R8 truss head (#10-32), ~0.40 in
        longeron_y=0.226, longeron_x=(0.20, -1.70),
        torque=None, pedal_bars=None),
}
FILES = {'pitts-s2b-like': ['pitts-s2b-like-own.glb', 'pitts-s2b-like-own-n1191.glb',
                            'pitts-s2b-like-own-checker-unmarked.glb', 'pitts-s2b-like-own-factory-unmarked.glb'],
         'extra-300-like': ['extra-300-like-own.glb', 'extra-300-like-own-n300uy.glb', 'extra-300-like-own-unmarked.glb']}


def to_b(g):  # glTF -> builder
    g = np.asarray(g, float); return np.stack((-g[..., 0], g[..., 2], g[..., 1]), -1)


def to_g(b):  # builder -> glTF
    b = np.asarray(b, float); return np.stack((-b[..., 0], b[..., 2], b[..., 1]), -1)


def lin(c):
    return [((x / 255 + 0.055) / 1.055) ** 2.4 if x / 255 > 0.04045 else x / 255 / 12.92 for x in c]


def rounded_rect(x0, x1, hw, r, grow=0.0, seg=3, step=0.09):
    """CCW (seen from above) outline; same point count for every `grow`, so offset outlines pair up point for point."""
    x0, x1, hw, r = x0 - grow, x1 + grow, hw + grow, r + grow
    cs = [(x1 - r, hw - r, 0.0), (x0 + r, hw - r, 90.0), (x0 + r, -hw + r, 180.0), (x1 - r, -hw + r, 270.0)]
    n_x = max(1, int(round((x1 - x0 - 2 * r) / step))); n_y = max(1, int(round((2 * hw - 2 * r) / step)))
    pts = []
    for k, (cx, cy, a0) in enumerate(cs):
        for i in range(seg + 1):
            a = math.radians(a0 + 90.0 * i / seg); pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
        ex, ey = pts[-1]; nx_, ny_ = cs[(k + 1) % 4][:2]
        tx, ty = nx_ + r * math.cos(math.radians(a0 + 90)), ny_ + r * math.sin(math.radians(a0 + 90))
        n = n_x if k % 2 == 0 else n_y          # corner 0 -> 1 runs along x (the port edge), 1 -> 2 along y (aft edge)
        for i in range(1, n):
            pts.append((ex + (tx - ex) * i / n, ey + (ty - ey) * i / n))
    return np.array(pts)


# ---- the exact cut: clip triangles against a convex outline, keep what is outside ------------------------------------
def _split(poly, a, b):
    """poly: list of (bpos2, attrvec). Split by the line a->b: returns (inside(left), outside(right))."""
    ex, ey = b[0] - a[0], b[1] - a[1]
    side = [ex * (p[0][1] - a[1]) - ey * (p[0][0] - a[0]) for p in poly]
    ins, out = [], []
    n = len(poly)
    for i in range(n):
        p, q = poly[i], poly[(i + 1) % n]; sp, sq = side[i], side[(i + 1) % n]
        (ins if sp >= 0 else out).append(p)
        if (sp >= 0) != (sq >= 0):
            t = sp / (sp - sq); m = (p[0] + t * (q[0] - p[0]), p[1] + t * (q[1] - p[1]))
            ins.append(m); out.append(m)
    return ins, out


def clip_out(tri2, attrs, outline):
    """tri2: (3, 2) builder xy; attrs: (3, k). Returns list of (3, k) pieces outside the convex CCW outline."""
    poly = [(tri2[i], attrs[i]) for i in range(3)]
    keep = []
    for i in range(len(outline)):
        a, b = outline[i], outline[(i + 1) % len(outline)]
        ins, out = _split(poly, a, b)
        if len(out) >= 3: keep.append(out)
        poly = ins
        if len(poly) < 3: break
    pieces = []
    for pg in keep:
        for j in range(1, len(pg) - 1):
            pieces.append(np.array([pg[0][1], pg[j][1], pg[j + 1][1]]))
    return pieces


def _inside_all(p2, outline):
    """(m, 2) points -> (m,) True where strictly inside the convex CCW outline."""
    ok = np.ones(len(p2), bool)
    for i in range(len(outline)):
        a, b = outline[i], outline[(i + 1) % len(outline)]
        ok &= (b[0] - a[0]) * (p2[:, 1] - a[1]) - (b[1] - a[1]) * (p2[:, 0] - a[0]) > 0
    return ok


def _outside_any_edge(t2, outline):
    """(m, 3, 2) triangles -> (m,) True where all three vertices are outside one same edge (no overlap possible)."""
    res = np.zeros(len(t2), bool)
    for i in range(len(outline)):
        a, b = outline[i], outline[(i + 1) % len(outline)]
        s = (b[0] - a[0]) * (t2[..., 1] - a[1]) - (b[1] - a[1]) * (t2[..., 0] - a[0])
        res |= (s <= 0).all(1)
    return res


class Doc:
    def __init__(self, g):
        self.g = g; self.world = g.world_matrices()
        self.node = {n.get('name'): i for i, n in enumerate(g.doc['nodes'])}
        self.mat = {m.get('name'): i for i, m in enumerate(g.doc['materials'])}

    def prims(self, name, material=None):
        i = self.node.get(name)
        if i is None or 'mesh' not in self.g.doc['nodes'][i]: return []
        out = []
        for p in self.g.doc['meshes'][self.g.doc['nodes'][i]['mesh']]['primitives']:
            if material is not None and self.g.doc['materials'][p.get('material', -1)].get('name') != material: continue
            out.append((p, self.world[i]))
        return out

    def tris_b(self, name, material=None):
        """builder-frame triangles of a node (all its primitives)"""
        out = []
        for p, w in self.prims(name, material):
            a = self.g.array(p['attributes']['POSITION']); a = a @ w[:3, :3].T + w[:3, 3]
            ids = self.g.array(p['indices']).reshape(-1, 3) if 'indices' in p else np.arange(len(a)).reshape(-1, 3)
            out.append(to_b(a)[ids])
        return np.concatenate(out) if out else np.zeros((0, 3, 3))

    def cut(self, name, material, zband, outlines, axes=(0, 1), band_axis=2):
        """Clip every triangle of the node (optionally one material) lying wholly in the band (on band_axis, z by
        default) against each convex outline, given CCW in the (axes) plane (x, y by default)."""
        removed = added = 0
        for p, w in self.prims(name, material):
            g = self.g; names = sorted(p['attributes'])
            arrs = {k: g.array(p['attributes'][k]).astype(np.float64) for k in names}
            for k in names: assert g.doc['accessors'][p['attributes'][k]]['componentType'] == 5126, (name, k)
            ids = g.array(p['indices']).reshape(-1, 3).astype(np.int64) if 'indices' in p else np.arange(len(arrs['POSITION'])).reshape(-1, 3)
            wpos = arrs['POSITION'] @ w[:3, :3].T + w[:3, 3]; bp = to_b(wpos)
            dims = [arrs[k].shape[1] for k in names]; vec = np.concatenate([arrs[k] for k in names], 1)
            tb = bp[ids]
            work = list(map(tuple, ids)); removed0 = removed
            for ol in outlines:
                ax = list(axes)
                nxt = []; t2all = np.array([bp[list(t)][:, ax] for t in work]) if work else np.zeros((0, 3, 2))
                bandw = np.array([((bp[list(t)][:, band_axis] >= zband[0]).all() and (bp[list(t)][:, band_axis] <= zband[1]).all()) for t in work])
                far = _outside_any_edge(t2all, ol) if len(work) else np.zeros(0, bool)
                for t, b_, f_ in zip(work, bandw, far):
                    if not b_ or f_: nxt.append(t); continue
                    t = list(t); pieces = clip_out(bp[t][:, ax], np.concatenate([vec[t], bp[t]], 1), ol)
                    if len(pieces) == 1 and np.allclose(pieces[0][:, -3:], bp[t]):
                        nxt.append(tuple(t)); continue
                    removed += 1
                    for pc in pieces:
                        base = len(vec); vec = np.vstack([vec, pc[:, :-3]]); bp = np.vstack([bp, pc[:, -3:]])
                        nxt.append((base, base + 1, base + 2)); added += 1
                work = nxt
            if removed == removed0: continue                     # nothing of this primitive was in a window
            # drop slivers the clipper leaves on an edge
            work = [t for t in work if np.linalg.norm(np.cross(bp[t[1]] - bp[t[0]], bp[t[2]] - bp[t[0]])) > 1e-10]
            used = sorted({i for t in work for i in t}); remap = {o: n for n, o in enumerate(used)}
            vec = vec[used]; new_ids = np.array([[remap[i] for i in t] for t in work], np.uint32)
            off = 0
            for k, d in zip(names, dims):
                col = vec[:, off:off + d]; off += d
                if k == 'NORMAL': col = col / np.maximum(np.linalg.norm(col, axis=1, keepdims=True), 1e-12)
                p['attributes'][k] = g.accessor(col.astype(np.float32), {2: 'VEC2', 3: 'VEC3', 4: 'VEC4'}[d])
            p['indices'] = g.accessor(new_ids.reshape(-1, 1), 'SCALAR', 5123 if len(vec) < 65536 else 5125)
        return removed, added


# ---- raycasts on the uncut skin -----------------------------------------------------------------------------------
def up_hits(tris, xy):
    """for each (x, y): sorted z of every triangle crossing the vertical line"""
    out = []
    a, b, c = tris[:, 0], tris[:, 1], tris[:, 2]
    for x, y in xy:
        sel = (np.minimum(np.minimum(a[:, 0], b[:, 0]), c[:, 0]) <= x) & (np.maximum(np.maximum(a[:, 0], b[:, 0]), c[:, 0]) >= x) & \
              (np.minimum(np.minimum(a[:, 1], b[:, 1]), c[:, 1]) <= y) & (np.maximum(np.maximum(a[:, 1], b[:, 1]), c[:, 1]) >= y)
        zs = []
        for t in tris[sel]:
            (x0, y0, z0), (x1, y1, z1), (x2, y2, z2) = t
            d = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
            if abs(d) < 1e-14: continue
            l0 = ((y1 - y2) * (x - x2) + (x2 - x1) * (y - y2)) / d; l1 = ((y2 - y0) * (x - x2) + (x0 - x2) * (y - y2)) / d
            l2 = 1 - l0 - l1
            if min(l0, l1, l2) >= -1e-9: zs.append(l0 * z0 + l1 * z1 + l2 * z2)
        out.append(sorted(zs))
    return out


class Skin:
    def __init__(self, tris, zband):
        lo, hi = zband
        z = tris[..., 2]; self.t = tris[(z.max(1) <= hi) & (z.min(1) >= lo)]

    def z(self, xy):
        """outer skin z and inner surface z (second skin within 3 cm, else the pane's top) per point"""
        zo, zi = [], []
        for zs in up_hits(self.t, xy):
            assert zs, 'no belly skin over a window point: is the outline outside the fuselage?'
            zs = sorted(set(round(v, 6) for v in zs)); zo.append(zs[0])
            nxt = [v for v in zs[1:] if 0.004 < v - zs[0] < 0.03]
            zi.append(nxt[0] if nxt else zs[0] + PANE_T)
        return np.array(zo), np.array(zi)


# ---- new geometry ---------------------------------------------------------------------------------------------------
class Mesh:
    def __init__(self): self.p, self.n, self.t = [], [], []

    def add(self, pts, nrm, tris):
        b = sum(len(x) for x in self.p); self.p.append(np.asarray(pts, float)); self.n.append(np.asarray(nrm, float))
        self.t.extend((np.asarray(tris) + b).tolist())

    def band(self, A, B, nrm_sign):
        """quad strip between two closed loops (same count); faces wound so their normal points along nrm_sign
        (+1 up, -1 down) for horizontal bands, or toward the loop centre for vertical walls (nrm_sign 0)"""
        n = len(A); P = np.vstack([A, B]); tris = []
        for i in range(n):
            j = (i + 1) % n; tris += [(i, j, n + j), (i, n + j, n + i)]
        tris = np.array(tris)
        fn = np.cross(P[tris[:, 1]] - P[tris[:, 0]], P[tris[:, 2]] - P[tris[:, 0]])
        if nrm_sign:
            flip = fn[:, 2] * nrm_sign < 0
        else:
            c = A.mean(0); flip = ((c - P[tris].mean(1)) * fn).sum(1) < 0
        tris[flip] = tris[flip][:, ::-1]
        fn = np.cross(P[tris[:, 1]] - P[tris[:, 0]], P[tris[:, 2]] - P[tris[:, 0]])
        vn = np.zeros_like(P)
        for k in range(3): np.add.at(vn, tris[:, k], fn)
        self.add(P, vn / np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-12), tris)

    def tube(self, pts, r, n=6):
        pts = np.asarray(pts, float); rings, nr = [], []
        for k, p in enumerate(pts):
            d = pts[min(k + 1, len(pts) - 1)] - pts[max(k - 1, 0)]; d /= np.linalg.norm(d)
            ref = np.array([0, 0, 1.0]) if abs(d[2]) < 0.9 else np.array([1.0, 0, 0])
            u = np.cross(d, ref); u /= np.linalg.norm(u); w = np.cross(d, u)
            for a in np.linspace(0, 2 * math.pi, n, endpoint=False):
                o = math.cos(a) * u + math.sin(a) * w; rings.append(p + r * o); nr.append(o)
        tris = []
        for k in range(len(pts) - 1):
            for i in range(n):
                j = (i + 1) % n; a0, a1, b0, b1 = k * n + i, k * n + j, (k + 1) * n + i, (k + 1) * n + j
                tris += [(a0, b0, b1), (a0, b1, a1)]
        P = np.array(rings); T = np.array(tris)
        fn = np.cross(P[T[:, 1]] - P[T[:, 0]], P[T[:, 2]] - P[T[:, 0]])
        out = ((P[T].mean(1) - np.repeat(pts[:-1], 2 * n, 0)) * fn).sum(1) < 0
        T[out] = T[out][:, ::-1]
        self.add(P, nr, T)

    def screw(self, c, down, r=SCREW_R):
        """6-sided pan / truss head of radius r on a strip at c; `down` for a head under the belly (pointing -z)"""
        s = -1.0 if down else 1.0; P, N = [], []
        for rr, dz in ((r, 0.0), (r * 0.75, 0.0011)):
            for a in np.linspace(0, 2 * math.pi, 6, endpoint=False):
                P.append(c + np.array((rr * math.cos(a), rr * math.sin(a), s * dz)))
                N.append(np.array((math.cos(a), math.sin(a), s * 0.8)) / math.hypot(1, 0.8))
        P.append(c + np.array((0, 0, s * 0.0015))); N.append(np.array((0, 0, s)))
        T = []
        for i in range(6):
            j = (i + 1) % 6; T += [(i, j, 6 + j), (i, 6 + j, 6 + i), (6 + i, 6 + j, 12)]
        T = np.array(T); P = np.array(P)
        fn = np.cross(P[T[:, 1]] - P[T[:, 0]], P[T[:, 2]] - P[T[:, 0]])
        flip = ((P[T].mean(1) - (c - np.array((0, 0, s * 0.002)))) * fn).sum(1) < 0
        T[flip] = T[flip][:, ::-1]
        self.add(P, N, T)

    def arrays(self):
        P = np.vstack(self.p); N = np.vstack(self.n); return P, N, np.array(self.t, np.uint32)


def add_node(g, name, prims):
    """prims: [(Mesh, material index)] in builder coords -> one node at the scene root"""
    out = []
    for m, mat in prims:
        P, N, T = m.arrays()
        out.append(dict(attributes=dict(POSITION=g.accessor(to_g(P).astype(np.float32), 'VEC3'),
                                        NORMAL=g.accessor(to_g(N).astype(np.float32), 'VEC3')),
                        indices=g.accessor(T.reshape(-1, 1), 'SCALAR', 5125), material=mat, mode=4))
    g.doc['meshes'].append(dict(name=name, primitives=out))
    g.doc['nodes'].append(dict(name=name, mesh=len(g.doc['meshes']) - 1))
    g.doc['scenes'][g.doc.get('scene', 0)]['nodes'].append(len(g.doc['nodes']) - 1)
    return int(sum(len(m.t) for m, _ in prims))


def add_material(g, mat):
    names = [m.get('name') for m in g.doc['materials']]
    if mat['name'] in names: g.doc['materials'][names.index(mat['name'])] = mat; return names.index(mat['name'])
    g.doc['materials'].append(mat); return len(g.doc['materials']) - 1


def repack(g):
    """drop the buffer views nothing references any more (the cut replaces accessors) and close the gaps"""
    # accessors nothing points at any more go first, then the views only they used
    acc_used = set()
    for m in g.doc['meshes']:
        for p in m['primitives']:
            acc_used |= set(p['attributes'].values())
            if 'indices' in p: acc_used.add(p['indices'])
            for t in p.get('targets', []): acc_used |= set(t.values())
    for s in g.doc.get('skins', []):
        if 'inverseBindMatrices' in s: acc_used.add(s['inverseBindMatrices'])
    for an in g.doc.get('animations', []):
        for sm in an['samplers']: acc_used |= {sm['input'], sm['output']}
    akeep = sorted(acc_used); amap = {o: n for n, o in enumerate(akeep)}
    used = {g.doc['accessors'][i]['bufferView'] for i in akeep if 'bufferView' in g.doc['accessors'][i]}
    used |= {im['bufferView'] for im in g.doc.get('images', []) if 'bufferView' in im}
    keep = sorted(used); remap = {o: n for n, o in enumerate(keep)}; nb = bytearray(); views = []
    for o in keep:
        v = dict(g.doc['bufferViews'][o]); off = v.get('byteOffset', 0)
        nb.extend(b'\0' * (-len(nb) % 16)); data = g.bin[off:off + v['byteLength']]
        v['byteOffset'] = len(nb); nb.extend(data); views.append(v)
    accs = []
    for o in akeep:
        a = dict(g.doc['accessors'][o])
        if 'bufferView' in a: a['bufferView'] = remap[a['bufferView']]
        accs.append(a)
    for m in g.doc['meshes']:
        for p in m['primitives']:
            p['attributes'] = {k: amap[v] for k, v in p['attributes'].items()}
            if 'indices' in p: p['indices'] = amap[p['indices']]
            for t in p.get('targets', []):
                for k in t: t[k] = amap[t[k]]
    for s in g.doc.get('skins', []):
        if 'inverseBindMatrices' in s: s['inverseBindMatrices'] = amap[s['inverseBindMatrices']]
    for an in g.doc.get('animations', []):
        for sm in an['samplers']: sm['input'], sm['output'] = amap[sm['input']], amap[sm['output']]
    for im in g.doc.get('images', []):
        if 'bufferView' in im: im['bufferView'] = remap[im['bufferView']]
    g.doc['accessors'] = accs; g.doc['bufferViews'] = views; g.bin = nb


def validate(g):
    """every node index in range, at most one parent, every mesh/accessor/material reference in range"""
    n = len(g.doc['nodes']); parent = {}
    for i, nd in enumerate(g.doc['nodes']):
        for c in nd.get('children', []):
            assert 0 <= c < n and c not in parent, ('node graph', i, c); parent[c] = i
        if 'mesh' in nd: assert 0 <= nd['mesh'] < len(g.doc['meshes'])
    for s in g.doc['scenes']:
        for c in s['nodes']: assert 0 <= c < n and c not in parent
    for m in g.doc['meshes']:
        for p in m['primitives']:
            for a in list(p['attributes'].values()) + ([p['indices']] if 'indices' in p else []):
                assert 0 <= a < len(g.doc['accessors'])
            assert 0 <= p.get('material', 0) < len(g.doc['materials'])


def ring_pts(outline, zfun, dz=0.0):
    zo, zi = zfun(outline)
    return np.column_stack((outline, zo + dz)), np.column_stack((outline, zi + dz))


def apply(g, aircraft, log=print):
    spec = WINDOWS[aircraft]; d = Doc(g)
    assert 'Floor_Window_Glass' not in d.node, 'this GLB already has its floor windows'
    tris_before = sum(g.doc['accessors'][p['indices']]['count'] // 3 if 'indices' in p else
                      g.doc['accessors'][p['attributes']['POSITION']]['count'] // 3
                      for m in g.doc['meshes'] for p in m['primitives'])
    fus = d.tris_b('Fuselage_Painted')
    for extra in spec.get('skin_extra', []): fus = np.concatenate([fus, d.tris_b(extra)])
    skin = Skin(fus, (-0.80, -0.45))
    wins = []
    for name, x0, x1, hw, r in spec['windows']:
        C = rounded_rect(x0, x1, hw, r)
        wins.append(dict(name=name, C=C, H=rounded_rect(x0, x1, hw, r, HOLE), S=rounded_rect(x0, x1, hw, r, STRIP),
                         M=rounded_rect(x0, x1, hw, r, STRIP / 2), W=rounded_rect(x0, x1, hw, r, 0.040),
                         box=(x0, x1, hw, r)))
    # the panes, frames and tubes are laid on the UNCUT skin
    glass, frame, walls, tubes = Mesh(), Mesh(), Mesh(), Mesh()
    zf = skin.z
    for w in wins:
        H = w['H']; c = H.mean(0); pts = [c]; rings = []
        for s in (0.33, 0.66, 1.0): rings.append(c + s * (H - c))
        allxy = np.vstack([pts] + rings); zo, _ = zf(allxy)
        P = np.column_stack((allxy, zo)); n = len(H); T = []
        for i in range(n): T.append((0, 1 + (i + 1) % n, 1 + i))
        for k in range(2):
            a0, b0 = 1 + k * n, 1 + (k + 1) * n
            for i in range(n):
                j = (i + 1) % n; T += [(a0 + i, a0 + j, b0 + j), (a0 + i, b0 + j, b0 + i)]
        T = np.array(T)
        fn = np.cross(P[T[:, 1]] - P[T[:, 0]], P[T[:, 2]] - P[T[:, 0]]); flip = fn[:, 2] > 0
        T[flip] = T[flip][:, ::-1]                                  # outward = down
        fn = np.cross(P[T[:, 1]] - P[T[:, 0]], P[T[:, 2]] - P[T[:, 0]]); vn = np.zeros_like(P)
        for k in range(3): np.add.at(vn, T[:, k], fn)
        glass.add(P, vn / np.linalg.norm(vn, axis=1, keepdims=True), T)
        (Co, Ci), (So, Si) = ring_pts(w['C'], zf), ring_pts(w['S'], zf)
        frame.band(Co - (0, 0, STRIP_T), So - (0, 0, STRIP_T), -1)          # outer strip, under the belly
        frame.band(Ci + (0, 0, STRIP_T), Si + (0, 0, STRIP_T), +1)          # inner strip, inside
        frame.band(Co - (0, 0, STRIP_T), Ci + (0, 0, STRIP_T), 0)           # the edge between them, facing the opening
        Mo, Mi = ring_pts(w['M'], zf)
        L = np.r_[0, np.cumsum(np.linalg.norm(np.diff(np.vstack([Mo, Mo[:1]]), axis=0), axis=1))]
        k = max(4, int(round(L[-1] / SCREW_PITCH))); w['screws'] = k
        for s in np.linspace(0, L[-1], k, endpoint=False):
            i = int(np.searchsorted(L, s, 'right') - 1); t = (s - L[i]) / (L[i + 1] - L[i])
            q = Mo[i] + t * (Mo[(i + 1) % len(Mo)] - Mo[i]); frame.screw(q - (0, 0, STRIP_T), True, spec['screw_r'])
        for mat, z0, z1 in spec['floor_walls']:                              # the cut edge of the floor
            S2 = w['S']; walls.band(np.column_stack((S2, np.full(len(S2), z0))), np.column_stack((S2, np.full(len(S2), z1))), 0)
        w['z'] = (float(zo.min()), float(zo.max()))
    # tubes: lower longerons, a cross tube at each end of every window, the Pitts' torque tube and stick sockets
    ly = spec['longeron_y']; xa, xb = spec['longeron_x']
    for sy in (1, -1):
        xs = np.linspace(xa, xb, max(2, int(abs(xa - xb) / 0.2) + 1)); _, zi = zf(np.column_stack((xs, np.full(len(xs), sy * ly))))
        tubes.tube(np.column_stack((xs, np.full(len(xs), sy * ly), zi + TUBE_R + 0.002)), TUBE_R)
    for w in wins:
        x0, x1, hw, r = w['box']
        for xc in (x0 - STRIP - TUBE_R - 0.004, x1 + STRIP + TUBE_R + 0.004):
            _, zi = zf(np.array([[xc, ly], [xc, -ly]]))
            zc = float(zi.max()) + TUBE_R + 0.002
            tubes.tube([(xc, ly, zc), (xc, -ly, zc)], TUBE_R)
    tq = spec['torque']
    if tq:
        tubes.tube([(tq['x'][0], 0, tq['z']), (tq['x'][1], 0, tq['z'])], tq['r'], 8)
        for xs_ in tq['sticks']: tubes.tube([(xs_, 0, tq['z']), (xs_, 0, tq['floor'])], 0.012, 6)
    pb = spec['pedal_bars']
    if pb:
        for xp in pb['x']: tubes.tube([(xp, pb['half'], pb['z']), (xp, -pb['half'], pb['z'])], TUBE_R)
    # ---- now cut
    report = {}
    for node, mat, zb, which in spec['cut']:
        key = {'hole': 'H', 'floor': 'S', 'wide': 'W'}[which]
        rem, add = d.cut(node, mat, zb, [w[key] for w in wins]); report[node] = (rem, add)
    for node, mat, xb, poly in spec['cut_yz']:
        report[node + ' bulkhead'] = d.cut(node, mat, xb, [np.array(poly, float)], axes=(1, 2), band_axis=0)
    liner = None
    fw = spec['footwell']
    if fw:
        liner = Mesh(); (xa_, xf_), h, top = fw['x'], fw['half'], fw['top']
        xs = np.linspace(xa_, xf_, 5)
        for sy in (1, -1):
            _, zi = zf(np.column_stack((xs, np.full(len(xs), sy * h))))
            lo = np.column_stack((xs, np.full(len(xs), sy * h), zi - 0.004)); hi = lo.copy(); hi[:, 2] = top
            n = len(xs); P = np.vstack([lo, hi]); T = [(i, i + 1, n + i + 1) for i in range(n - 1)] + [(i, n + i + 1, n + i) for i in range(n - 1)]
            liner.add(P, np.tile((0, -sy, 0), (len(P), 1)), np.array(T) if sy < 0 else np.array(T)[:, ::-1])
        liner.add([(xa_, h, top), (xf_, h, top), (xf_, -h, top), (xa_, -h, top)], np.tile((0, 0, -1.0), (4, 1)), [(0, 2, 1), (0, 3, 2)])
        ys = np.linspace(-h, h, 5); _, zi = zf(np.column_stack((np.full(len(ys), xf_), ys)))
        n = len(ys); P = np.vstack([np.column_stack((np.full(n, xf_), ys, zi - 0.004)), np.column_stack((np.full(n, xf_), ys, np.full(n, top)))])
        liner.add(P, np.tile((-1.0, 0, 0), (len(P), 1)), [(i, n + i + 1, i + 1) for i in range(n - 1)] + [(i, n + i, n + i + 1) for i in range(n - 1)])
    if spec['wing']:
        wn, xf, xa_, zc = spec['wing']
        # the fuselage wall at the wing's height: half-way between the skins (or 4 mm inside a single skin)
        def wall(x):
            """|y| where the fuselage skins cross the horizontal line (x, *, zc), port side, sorted"""
            hits = []
            for t in fus[np.abs(fus[..., 0].mean(1) - x) < 0.16]:
                for a_, b_ in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
                    if (a_[2] - zc) * (b_[2] - zc) < 0:
                        q = a_ + (zc - a_[2]) / (b_[2] - a_[2]) * (b_ - a_)
                        if abs(q[0] - x) < 0.16 and q[1] > 0: hits.append(round(float(q[1]), 4))
            return sorted(set(hits))
        wf, wa = wall(xf - 0.05), wall(xa_ + 0.05)
        yf = (wf[0] + wf[1]) / 2 if len(wf) > 1 and wf[1] - wf[0] < 0.03 else wf[-1] - 0.004
        ya = (wa[0] + wa[1]) / 2 if len(wa) > 1 and wa[1] - wa[0] < 0.03 else wa[-1] - 0.004
        trap = np.array([(xf, -yf), (xf, yf), (xa_, ya), (xa_, -ya)])[::-1]   # CCW from above
        e0, e1 = trap[1] - trap[0], trap[2] - trap[1]
        if e0[0] * e1[1] - e0[1] * e1[0] < 0: trap = trap[::-1]
        report[wn] = d.cut(wn, None, (-0.70, -0.40), [trap]); report[wn + ' wall y'] = (round(yf, 3), round(ya, 3))
    # ---- materials and the three new nodes
    # The two materials exactly as Blender 5.2 exports paint.mat_lexan('Lexan') and paint.mat_floor_frame('Floor_Frame')
    # (Codex lane; checked by exporting both, 2026-11-02). Unity maps them by these names at placement
    # (Bridge/FloorWindowMaterials.cs): Lexan -> FlyingGame/Lexan, Floor_Frame -> the airframe shader, satin aluminium.
    lex = add_material(g, dict(name='Lexan', doubleSided=True, alphaMode='BLEND', pbrMetallicRoughness=dict(
        baseColorFactor=[*lin((150, 160, 165)), 0.18], metallicFactor=0.0, roughnessFactor=0.06)))
    frm = add_material(g, dict(name='Floor_Frame', doubleSided=True, pbrMetallicRoughness=dict(
        baseColorFactor=[*lin((180, 184, 187)), 1.0], metallicFactor=0.8, roughnessFactor=0.32)))
    d.mat = {m.get('name'): i for i, m in enumerate(g.doc['materials'])}
    tube_mat = d.mat.get('Cockpit_tube')
    if tube_mat is None:
        tube_mat = add_material(g, dict(name='Floor_Tube', doubleSided=True, pbrMetallicRoughness=dict(
            baseColorFactor=[*lin((200, 202, 205)), 1.0], metallicFactor=0.0, roughnessFactor=0.6)))
    tg = add_node(g, 'Floor_Window_Glass', [(glass, lex)])
    tf = add_node(g, 'Floor_Window_Frame', [(frame, frm)] + [(walls, d.mat[spec['floor_walls'][0][0]])])
    tt = add_node(g, 'Floor_Tubes', [(tubes, tube_mat)])
    tl = add_node(g, 'Footwell_Liner', [(liner, d.mat[fw['material']])]) if liner else 0
    repack(g); validate(g)
    tris_after = sum(g.doc['accessors'][p['indices']]['count'] // 3 if 'indices' in p else
                     g.doc['accessors'][p['attributes']['POSITION']]['count'] // 3
                     for m in g.doc['meshes'] for p in m['primitives'])
    g.doc.setdefault('extras', {})['floorWindows'] = dict(
        source='tools/own_models/floor_windows.py', frame='builder: metres, nose +x, port +y, up +z',
        windows=[dict(name=w['name'], clear_x=[w['box'][0], w['box'][1]], clear_half_width=w['box'][2],
                      corner_radius=w['box'][3], skin_z=[round(w['z'][0], 4), round(w['z'][1], 4)], screws=w['screws'])
                 for w in wins])
    log(f'FLOOR WINDOWS {aircraft}: ' + '; '.join(
        f"{w['name']} x {w['box'][0]:+.3f}..{w['box'][1]:+.3f} |y| {w['box'][2]:.3f} skin z {w['z'][0]:.3f}..{w['z'][1]:.3f}, {w['screws']} screws"
        for w in wins))
    log(f'  cut (triangles clipped -> pieces kept): {report}')
    log(f'  added: glass {tg}, frame {tf}, tubes {tt}, footwell liner {tl} triangles; GLB total {tris_before} -> {tris_after} '
        f'({tris_after - tris_before:+d})')
    return dict(before=tris_before, after=tris_after, glass=tg, frame=tf, tubes=tt, liner=tl, cut=report)


def apply_file(path, aircraft=None, log=print):
    path = Path(path)
    if aircraft is None: aircraft = next(a for a in WINDOWS if path.name.startswith(a))
    g = Glb(path); r = apply(g, aircraft, log); g.save(path); log(f'  WROTE {path} {path.stat().st_size} bytes'); return r


if __name__ == '__main__':
    args = [a for a in sys.argv[1:] if not a.startswith('-')]
    if not args:
        root = Path(__file__).resolve().parents[2] / 'unity/Assets/_Project/Resources/Models'
        args = [root / a / f for a, fs in FILES.items() for f in fs]
    for a in args: apply_file(a)
