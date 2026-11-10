"""Meshes and the GLB writer for the instrument library. Instrument frame: face radius 1, +z toward the pilot, +y up,
+x to the pilot's right (glTF). Every front face winds counter-clockwise seen from +z (glTF front faces)."""
import io
import json
import math
import struct
from pathlib import Path

import numpy as np


class Mesh:
    """Positions, triangles and (optional) UVs for one primitive."""

    def __init__(self):
        self.v, self.t, self.uv = [], [], []

    def _add(self, pts, tris, uvs=None):
        off = len(self.v)
        self.v.extend([tuple(map(float, p)) for p in pts])
        self.uv.extend(uvs if uvs is not None else [(0.0, 0.0)] * len(pts))
        self.t.extend(tuple(off + i for i in f) for f in tris)
        return self

    # -- flat pieces facing +z ------------------------------------------------------------------------------------

    def disc(self, r, z, n=96, uv_r=None):
        """A disc facing +z. uv_r: the texture's radius in face radii (the image's half-width), UV v down."""
        uv_r = uv_r or r
        pts = [(0, 0, z)] + [(r * math.cos(a), r * math.sin(a), z) for a in np.linspace(0, math.tau, n, endpoint=False)]
        uvs = [self._uv(p, uv_r) for p in pts]
        return self._add(pts, [(0, 1 + i, 1 + (i + 1) % n) for i in range(n)], uvs)

    def annulus(self, r0, r1, z, n=96, uv_r=None):
        """A ring facing +z between r0 and r1, optionally textured with a disc texture of radius uv_r."""
        pts = []
        for a in np.linspace(0, math.tau, n, endpoint=False):
            pts += [(r0 * math.cos(a), r0 * math.sin(a), z), (r1 * math.cos(a), r1 * math.sin(a), z)]
        tris = []
        for i in range(n):
            a0, b0, a1, b1 = 2 * i, 2 * i + 1, 2 * ((i + 1) % n), 2 * ((i + 1) % n) + 1
            tris += [(a0, b0, b1), (a0, b1, a1)]
        uvs = [self._uv(p, uv_r) for p in pts] if uv_r else None
        return self._add(pts, tris, uvs)

    def wall(self, r, z0, z1, n=96, outward=True):
        """A cylinder wall between z0 < z1, normals out (or in)."""
        pts = []
        for a in np.linspace(0, math.tau, n, endpoint=False):
            pts += [(r * math.cos(a), r * math.sin(a), z0), (r * math.cos(a), r * math.sin(a), z1)]
        tris = []
        for i in range(n):
            a0, b0, a1, b1 = 2 * i, 2 * i + 1, 2 * ((i + 1) % n), 2 * ((i + 1) % n) + 1
            tris += [(a0, a1, b1), (a0, b1, b0)] if outward else [(a0, b1, a1), (a0, b0, b1)]
        return self._add(pts, tris)

    def plate(self, poly, z0, z1):
        """A CONVEX polygon (x, y) extruded from z0 to z1: front at z1, sides; the back is never seen."""
        poly = [tuple(p) for p in poly]
        if _area(poly) < 0: poly = poly[::-1]
        n = len(poly)
        front = [(x, y, z1) for x, y in poly]
        self._add(front, [(0, i, i + 1) for i in range(1, n - 1)])
        side = []
        for x, y in poly: side += [(x, y, z0), (x, y, z1)]
        tris = []
        for i in range(n):
            a0, b0, a1, b1 = 2 * i, 2 * i + 1, 2 * ((i + 1) % n), 2 * ((i + 1) % n) + 1
            tris += [(a0, a1, b1), (a0, b1, b0)]
        return self._add(side, tris)

    def sphere(self, c, r, n=20, m=10):
        pts, tris = [], []
        for j in range(m + 1):
            th = math.pi * j / m
            for i in range(n):
                ph = math.tau * i / n
                pts.append((c[0] + r * math.sin(th) * math.cos(ph), c[1] + r * math.sin(th) * math.sin(ph), c[2] + r * math.cos(th)))
        for j in range(m):
            for i in range(n):
                a, b = j * n + i, j * n + (i + 1) % n
                c_, d = a + n, b + n
                tris += [(a, c_, d), (a, d, b)]
        return self._add(pts, tris)

    @staticmethod
    def _uv(p, uv_r):
        return (0.5 + p[0] / (2 * uv_r), 0.5 - p[1] / (2 * uv_r))

    def arrays(self):
        return np.asarray(self.v, float), np.asarray(self.t, int).reshape(-1, 3), np.asarray(self.uv, float)

    def check_winding(self, toward=(0, 0, 1)):
        """Every triangle whose normal is near +z must face +z (the pilot)."""
        p, t, _ = self.arrays()
        nrm = np.cross(p[t[:, 1]] - p[t[:, 0]], p[t[:, 2]] - p[t[:, 0]])
        d = nrm @ np.asarray(toward, float)
        flat = np.abs(nrm[:, 2]) > 0.9 * np.linalg.norm(nrm, axis=1)
        return int(np.sum(flat & (d < 0)))


def _area(poly):
    return 0.5 * sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(poly, poly[1:] + poly[:1]))


class Writer:
    """A GLB from scratch: nodes with children, flat-shaded meshes, PBR materials, embedded PNG textures."""

    def __init__(self, generator='FlyingGame shared instrument library'):
        self.doc = dict(asset=dict(version='2.0', generator=generator), scene=0, scenes=[dict(nodes=[])], nodes=[],
                        meshes=[], materials=[], buffers=[dict(byteLength=0)], bufferViews=[], accessors=[])
        self.bin = bytearray()
        self._mats = {}

    def _view(self, data):
        self.bin.extend(b'\0' * (-len(self.bin) % 4))
        self.doc['bufferViews'].append(dict(buffer=0, byteOffset=len(self.bin), byteLength=len(data)))
        self.bin.extend(data)
        return len(self.doc['bufferViews']) - 1

    def _accessor(self, a, kind, comp=5126):
        a = np.ascontiguousarray(a, dtype='<f4' if comp == 5126 else '<u4')
        e = dict(bufferView=self._view(a.tobytes()), componentType=comp, count=len(a), type=kind)
        if kind == 'VEC3': e.update(min=a.min(0).tolist(), max=a.max(0).tolist())
        self.doc['accessors'].append(e)
        return len(self.doc['accessors']) - 1

    def material(self, name, rgb, png=None, metallic=0.0, rough=0.6, emissive=None):
        if name in self._mats: return self._mats[name]
        m = dict(name=name, doubleSided=False,
                 pbrMetallicRoughness=dict(baseColorFactor=[*rgb, 1.0], metallicFactor=metallic, roughnessFactor=rough))
        if png is not None:
            buf = io.BytesIO(); png.save(buf, format='PNG')
            imgs = self.doc.setdefault('images', []); imgs.append(dict(name=name, bufferView=self._view(buf.getvalue()), mimeType='image/png'))
            smp = self.doc.setdefault('samplers', []); smp.append(dict(magFilter=9729, minFilter=9987, wrapS=33071, wrapT=33071))
            tex = self.doc.setdefault('textures', []); tex.append(dict(source=len(imgs) - 1, sampler=len(smp) - 1))
            m['pbrMetallicRoughness']['baseColorTexture'] = dict(index=len(tex) - 1)
        if emissive: m['emissiveFactor'] = list(emissive)
        self.doc['materials'].append(m)
        self._mats[name] = len(self.doc['materials']) - 1
        return self._mats[name]

    def node(self, name, parent=None, mesh=None, material=None, extras=None):
        """A node at its parent's origin (every pivot of the library is the dial centre)."""
        n = dict(name=name)
        if mesh is not None:
            p, t, uv = mesh.arrays()
            flat = p[t].reshape(-1, 3)
            nrm = np.cross(p[t[:, 1]] - p[t[:, 0]], p[t[:, 2]] - p[t[:, 0]])
            nrm /= np.maximum(np.linalg.norm(nrm, axis=1)[:, None], 1e-12)
            attrs = dict(POSITION=self._accessor(flat, 'VEC3'), NORMAL=self._accessor(np.repeat(nrm, 3, axis=0), 'VEC3'))
            if 'baseColorTexture' in self.doc['materials'][material]['pbrMetallicRoughness']:
                attrs['TEXCOORD_0'] = self._accessor(uv[t].reshape(-1, 2), 'VEC2')
            self.doc['meshes'].append(dict(name=name, primitives=[dict(attributes=attrs, material=material, mode=4)]))
            n['mesh'] = len(self.doc['meshes']) - 1
        if extras: n['extras'] = extras
        self.doc['nodes'].append(n)
        i = len(self.doc['nodes']) - 1
        if parent is None: self.doc['scenes'][0]['nodes'].append(i)
        else: self.doc['nodes'][parent].setdefault('children', []).append(i)
        return i

    def validate(self):
        """Every index in range, every node at most one parent, no orphans (CLAUDE.md: glTFast rejects silently)."""
        nodes = self.doc['nodes']; parents = {}
        for i, n in enumerate(nodes):
            for c in n.get('children', []):
                assert 0 <= c < len(nodes), ('child out of range', n['name'], c)
                assert c not in parents, ('two parents', nodes[c]['name'])
                parents[c] = i
        roots = set(self.doc['scenes'][0]['nodes'])
        for i in range(len(nodes)):
            assert (i in parents) != (i in roots), ('orphan or root with parent', nodes[i]['name'])
        for m in self.doc['meshes']:
            for p in m['primitives']:
                assert 0 <= p['material'] < len(self.doc['materials'])
                for a in p['attributes'].values(): assert 0 <= a < len(self.doc['accessors'])

    def save(self, path):
        self.validate()
        self.doc['buffers'][0]['byteLength'] = len(self.bin)
        js = json.dumps(self.doc, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4)
        buf = bytes(self.bin); buf += b'\0' * (-len(buf) % 4)
        raw = (struct.pack('<4sII', b'glTF', 2, 28 + len(js) + len(buf)) + struct.pack('<I4s', len(js), b'JSON') + js
               + struct.pack('<I4s', len(buf), b'BIN\0') + buf)
        Path(path).parent.mkdir(parents=True, exist_ok=True)
        Path(path).write_bytes(raw)
