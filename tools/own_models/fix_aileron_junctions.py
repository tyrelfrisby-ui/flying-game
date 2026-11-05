"""The break in the skin where the aileron begins (Ty 2026-11-04: "this is true on most models fix that") — offline pass.

  PYTHONDONTWRITEBYTECODE=1 /usr/bin/python3 tools/own_models/fix_aileron_junctions.py [glb ...]

For every own-model GLB with aileron nodes (default: the list below; the Extra has its own, extra_cockpit_shared.py,
because its hinge also moves). Measured and checked by check_aileron_junction.py; this changes only geometry at neutral,
never a hinge line, an origin or a rig row. Per aileron, at every station:

  * NOSE: the aileron's top and bottom skins are filled out to the wing's lip height ahead of them (wing 8 mm ahead of the
    gap), fully over the front 3 % of the aileron chord and faded out by 25 %. A plain aileron sitting lower or higher
    than the wing it continues is the visible step.
  * GAP: where the chordwise gap to the wing exceeds 8 mm, the aileron's nose region is drawn forward to leave 5 mm
    (same fade). A real fabric aileron's hinge gap is a few millimetres.
  * UNDER: wing skin lying on (within 3 mm of) or above the aileron's skin over its planform — the wing built full-chord
    with the aileron drawn on top (the 172) — is tucked 6 mm inside the aileron, so the two no longer z-fight and the
    wing's continuation is still there to see when the aileron deflects.

Idempotent: a GLB with extras.aileronJunctionFix is skipped. Vertex normals of the edited primitives are recomputed
from their own triangles (split vertices stay split). The node graph is validated before each save.
"""
import sys
from pathlib import Path

import numpy as np

from glb_visuals import Glb
from vr_phase2e import compact
from drop_card_slot import validate
from check_aileron_junction import Skin, MODELS

DEFAULT = ['c172-like/c172-like-own.glb', 'glider-2-33-like/glider-2-33-like-own.glb', 'glider-2-33-like/glider-2-33-like-own-tf.glb',
           'great-lakes-like/great-lakes-like-own.glb', 'j3-cub-like/j3-cub-like-own.glb', 'j3-cub-floats-like/j3-cub-floats-like-own.glb',
           'pitts-s2b-like/pitts-s2b-like-own.glb', 'pitts-s2b-like/pitts-s2b-like-own-checker-unmarked.glb',
           'pitts-s2b-like/pitts-s2b-like-own-factory-unmarked.glb', 'pitts-s2b-like/pitts-s2b-like-own-n1191.glb']
AILERON = ('Aileron_L', 'Aileron_R', 'Aileron.L', 'Aileron.R', 'Aileron_UL', 'Aileron_UR', 'Aileron_LL', 'Aileron_LR')
WING = ('Wing', 'Wing.L', 'Wing.R', 'Wing_Upper', 'Wing_Lower')
GAP_MAX, GAP_TO, TUCK = .008, .005, .006


def fade(f): return .5 * (1 + np.cos(np.pi * np.clip((f - .03) / .22, 0, 1)))


class Frame:
    """(chord forward +, span, up) from glTF world: up = +y; span/chord found from the left/right ailerons."""
    def __init__(self, cents, wing_c):
        cc = np.array(cents); self.s = 0 if np.ptp(cc[:, 0]) > np.ptp(cc[:, 2]) else 2; self.c = 2 - self.s
        self.f = 1.0 if wing_c[self.c] > cc[:, self.c].mean() else -1.0
    def to(self, w): return np.stack([w[..., self.c] * self.f, w[..., self.s], w[..., 1]], -1)
    def back(self, b):
        w = np.zeros_like(b); w[..., self.c] = b[..., 0] * self.f; w[..., self.s] = b[..., 1]; w[..., 1] = b[..., 2]; return w


def prim_world(g, world, i):
    out = []
    for p in g.doc['meshes'][g.doc['nodes'][i]['mesh']]['primitives']:
        v = np.asarray(g.array(p['attributes']['POSITION']), float)
        out.append((p, v @ world[i][:3, :3].T + world[i][:3, 3]))
    return out


def tris(g, p, w):
    t = np.asarray(g.array(p['indices'])).reshape(-1, 3) if 'indices' in p else np.arange(len(w)).reshape(-1, 3)
    return w[t]


def write_prim(g, p, w_world, M):
    inv = np.linalg.inv(M); loc = w_world @ inv[:3, :3].T + inv[:3, 3]
    old = np.asarray(g.array(p['attributes']['POSITION']), float)
    if np.allclose(old, loc, atol=1e-7): return False
    p['attributes']['POSITION'] = g.accessor(loc, 'VEC3')
    if 'NORMAL' in p and 'indices' in p:
        t = np.asarray(g.array(p['indices'])).reshape(-1, 3)
        fn = np.cross(loc[t[:, 1]] - loc[t[:, 0]], loc[t[:, 2]] - loc[t[:, 0]]); vn = np.zeros_like(loc)
        for k in range(3): np.add.at(vn, t[:, k], fn)
        on = np.asarray(g.array(p['attributes']['NORMAL']), float); ln = np.linalg.norm(vn, axis=1, keepdims=True)
        vn = np.where(ln > 1e-12, vn / np.maximum(ln, 1e-12), on)
        if (np.sum(vn * on, 1) < 0).mean() > .5: vn = -vn
        moved = np.linalg.norm(old - loc, axis=1) > 1e-7
        # Only the moved vertices and their triangle neighbours change shading.
        near = np.zeros(len(loc), bool); near[moved] = True
        near[t[near[t].any(1)].ravel()] = True
        p['attributes']['NORMAL'] = g.accessor(np.where(near[:, None], vn, on), 'VEC3')
    return True


def under_coplanar(W, A, ev, s0, s1):
    """True where the wing's own skin runs on (within 3 mm of) the aileron's over its planform."""
    for s in np.linspace(s0 + .05, s1 - .05, 5):
        c = .5 * (ev['nose'](s) + ev['te'](s)); ha, hw = A.hits(c, s), W.hits(c, s)
        if len(ha) and len(hw) and min(abs(x - y) for x in ha for y in hw) < .003: return True
    return False


def clip(polys, k, val, keep_above):
    """Sutherland-Hodgman: each polygon (list of (pos, normal)) against the plane coordinate k = val."""
    out = []
    for poly in polys:
        res = []
        for a, b in zip(poly, poly[1:] + poly[:1]):
            da, db = a[0][k] - val, b[0][k] - val
            ina, inb = (da >= 0) == keep_above or da == 0, (db >= 0) == keep_above or db == 0
            if ina: res.append(a)
            if (da > 0) != (db > 0) and da != 0 and db != 0:
                t = da / (da - db); res.append((a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1])))
        if len(res) >= 3: out.append(res)
    return out


def recut_bay(g, world, wi, F, ev, s0, s1):
    """A wing built full-chord with the aileron lying ON it (the 172: one bay of triangles from span 3.17 to 5.46 runs
    under the whole aileron). Clip the wing's triangles at the aileron's two ends, and between them end the wing at a
    closed lip GAP_TO ahead of the aileron's nose: every vertex aft of that line goes onto it, at the skin's own height
    there (upper or lower). Outside the aileron's span the wing is unchanged."""
    from check_aileron_junction import Skin as _Skin
    node = g.doc['nodes'][wi]; M = world[wi]
    for p in g.doc['meshes'][node['mesh']]['primitives']:
        pos = np.asarray(g.array(p['attributes']['POSITION']), float); nor = np.asarray(g.array(p['attributes']['NORMAL']), float)
        wpos = pos @ M[:3, :3].T + M[:3, 3]; b = F.to(wpos); bn = F.to(nor @ M[:3, :3].T)
        t = np.asarray(g.array(p['indices'])).reshape(-1, 3) if 'indices' in p else np.arange(len(b)).reshape(-1, 3)
        S = _Skin(b[t])
        lo, hi = min(s0, s1), max(s0, s1)
        polys_out = []
        for tri in t:
            poly = [(b[k].copy(), bn[k].copy()) for k in tri]
            smin, smax = min(q[0][1] for q in poly), max(q[0][1] for q in poly)
            if smax <= lo or smin >= hi: polys_out.append((poly, False)); continue
            inner = clip(clip([poly], 1, lo, True), 1, hi, False)
            outer = clip([poly], 1, lo, False) + clip([poly], 1, hi, True)
            polys_out += [(q, False) for q in outer] + [(q, True) for q in inner]
        P, Nn = [], []
        for poly, mid in polys_out:
            pts = []
            for q, n in poly:
                q = q.copy()
                if mid:
                    cut = ev['nose'](q[1]) + GAP_TO
                    if q[0] < cut:
                        h = S.hits(cut, q[1])
                        if len(h) >= 2:
                            m0 = S.hits(q[0], q[1]); zmid = .5 * (m0[0] + m0[-1]) if len(m0) >= 2 else .5 * (h[0] + h[-1])
                            q[0], q[2] = cut, (h[-1] if q[2] >= zmid else h[0])
                pts.append((q, n))
            for k in range(1, len(pts) - 1):
                tri = [pts[0], pts[k], pts[k + 1]]
                if np.linalg.norm(np.cross(tri[1][0] - tri[0][0], tri[2][0] - tri[0][0])) < 1e-12: continue
                P += [q for q, _ in tri]; Nn += [n for _, n in tri]
        P, Nn = F.back(np.array(P)), F.back(np.array(Nn))
        inv = np.linalg.inv(M); loc = P @ inv[:3, :3].T + inv[:3, 3]; ln = Nn @ inv[:3, :3].T
        ln /= np.maximum(np.linalg.norm(ln, axis=1, keepdims=True), 1e-12)
        p['attributes'] = dict(POSITION=g.accessor(loc, 'VEC3'), NORMAL=g.accessor(ln, 'VEC3'))
        p.pop('indices', None)


def fix(path):
    g = Glb(path)
    ex = g.doc.setdefault('extras', {})
    if ex.get('aileronJunctionFix'): print(Path(path).name, 'already done'); return
    world = g.world_matrices(); nodes = g.doc['nodes']
    ail = [i for i, n in enumerate(nodes) if n.get('name') in AILERON and 'mesh' in n and i in world]
    wings = [i for i, n in enumerate(nodes) if n.get('name') in WING and 'mesh' in n and i in world]
    cents = [np.concatenate([w for _, w in prim_world(g, world, i)]).mean(0) for i in ail]
    wc = np.concatenate([np.concatenate([w for _, w in prim_world(g, world, i)]) for i in wings]).mean(0)
    F = Frame(cents, wc)
    meshes = [nodes[i]['mesh'] for i in ail]
    assert len(set(meshes)) == len(meshes), (Path(path).name, 'ailerons share a mesh: editing one would edit both')
    report = {}
    for i in ail:
        A_p = [(p, F.to(w)) for p, w in prim_world(g, world, i)]
        A = Skin(np.concatenate([tris(g, p, b) for p, b in A_p]))
        allb = np.concatenate([b for _, b in A_p])
        s0, s1 = allb[:, 1].min(), allb[:, 1].max()
        c_hi, c_lo = allb[:, 0].max() + .01, allb[:, 0].min() - .01

        def edges(s):
            hit = [c for c in np.arange(c_hi, c_lo, -.002) if len(A.hits(c, s))]
            return (hit[0], hit[-1]) if hit else (None, None)
        sm = .5 * (s0 + s1); nose_m, _ = edges(sm)
        best, bd = None, 1e9
        for wi in wings:
            Wt = Skin(np.concatenate([tris(g, p, F.to(w)) for p, w in prim_world(g, world, wi)]))
            h = Wt.hits(nose_m + .03, sm); za = A.upper(nose_m - .01, sm)
            if len(h) and za is not None and abs(h[-1] - za) < bd: best, bd, W = wi, abs(h[-1] - za), Wt
        rows = []
        for s in np.linspace(s0 + .02, s1 - .02, 48):
            nose, te = edges(s)
            if nose is None: continue
            gap = [c for c in np.arange(nose, nose + .06, .001) if len(W.hits(c, s))]
            ha = A.hits(nose - .008, s)
            if not gap or len(ha) < 2: continue
            hw = W.hits(gap[0] + .008, s)
            if len(hw) < 2: continue
            rows.append((s, nose, te, ha[0], ha[-1], hw[0], hw[-1], gap[0] - nose))
        R = np.array(rows)
        # Stations whose step is far from the others' are cutting a strut fitting, horn or spade, not the skin.
        st = np.c_[R[:, 6] - R[:, 4], R[:, 5] - R[:, 3]]
        ok = (np.abs(st - np.median(st, 0)) <= 3 * np.maximum(np.median(np.abs(st - np.median(st, 0)), 0), .002)).all(1)
        R = R[ok]
        ev = {k: (lambda col: (lambda s: np.interp(s, R[:, 0], R[:, col])))(c) for k, c in
              (('nose', 1), ('te', 2), ('al', 3), ('au', 4), ('wl', 5), ('wu', 6), ('gap', 7))}
        changed = False
        for p, b in A_p:
            s = b[:, 1]; nose, te = ev['nose'](s), ev['te'](s); c = np.maximum(nose - te, 1e-3)
            f = np.clip((nose - b[:, 0]) / c, 0, 1); w = fade(f)
            au, al, wu, wl = ev['au'](s), ev['al'](s), ev['wu'](s), ev['wl'](s)
            ma = .5 * (au + al); inside = np.abs(b[:, 2] - ma) < .75 * np.maximum(au - al, 1e-3) + .006
            w = w * inside
            nb = b.copy()
            up = b[:, 2] >= ma
            # Outward (the aileron thinner than the wing's lip): fill the skin out to the lip. Inward (the aileron proud
            # of it, the 2-33's underside): shift the whole skin in by the step, keeping its shape.
            d_up = np.where(wu >= au, np.maximum(wu - b[:, 2], 0), wu - au)
            d_lo = np.where(wl <= al, -np.maximum(b[:, 2] - wl, 0), wl - al)
            nb[:, 2] += np.where(up, d_up, d_lo) * w
            nb[:, 0] += np.maximum(ev['gap'](s) - GAP_TO, 0) * (ev['gap'](s) > GAP_MAX) * w
            changed |= write_prim(g, p, F.back(nb), world[i])
        # UNDER: wing skin inside the aileron's planform, within 3 mm of / above its skin -> tucked inside it.
        tucked = 0
        for p, w in prim_world(g, world, best):
            b = F.to(w); s = b[:, 1]
            inspan = (s > s0 + .003) & (s < s1 - .003)
            idxs = np.nonzero(inspan & (b[:, 0] < ev['nose'](s) - .003) & (b[:, 0] > ev['te'](s) + .003))[0]
            nb = b.copy()
            for k in idxs:
                h = A.hits(b[k, 0], b[k, 1])
                if len(h) < 2: continue
                lo, hi = h[0], h[-1]; mid = .5 * (lo + hi)
                if b[k, 2] >= mid and b[k, 2] > hi - .003:
                    nb[k, 2] = max(mid, hi - TUCK); tucked += 1
                elif b[k, 2] < mid and b[k, 2] < lo + .003:
                    nb[k, 2] = min(mid, lo + TUCK); tucked += 1
            if tucked: changed |= write_prim(g, p, F.back(nb), world[best])
        if tucked == 0 and under_coplanar(W, A, ev, s0, s1):
            recut_bay(g, world, best, F, ev, s0, s1)
            tucked = -1
        report[nodes[i]['name']] = dict(wing=nodes[best]['name'], stations=len(R),
                                        step_up_mm=[round(float(1000 * np.min(R[:, 6] - R[:, 4])), 1), round(float(1000 * np.max(R[:, 6] - R[:, 4])), 1)],
                                        step_lo_mm=[round(float(1000 * np.min(R[:, 5] - R[:, 3])), 1), round(float(1000 * np.max(R[:, 5] - R[:, 3])), 1)],
                                        gap_mm=[round(float(1000 * R[:, 7].min()), 1), round(float(1000 * R[:, 7].max()), 1)], wing_vertices_tucked=tucked if tucked >= 0 else 'bay re-cut at the aileron nose')
    ex['aileronJunctionFix'] = dict(owner='Ty 2026-11-04: the break in the skin where the aileron begins, on most models',
                                    tool='tools/own_models/fix_aileron_junctions.py', before=report)
    compact(g)
    if 'materials' in g.doc: validate(g, path)
    else:                                                # an OBJ-built GLB with no materials: the graph half of validate()
        n = len(g.doc['nodes']); parents = {}
        for k, node in enumerate(g.doc['nodes']):
            for c in node.get('children', []):
                assert 0 <= c < n and c not in parents, (path, 'node graph'); parents[c] = k
            assert node.get('mesh', 0) < len(g.doc['meshes']), (path, 'mesh index')
    g.save(path)
    print(Path(path).name, report)


if __name__ == '__main__':
    for rel in (sys.argv[1:] or DEFAULT):
        p = Path(rel)
        fix(p if p.exists() else MODELS / rel)
