"""Where the wing skin meets the aileron, at neutral: steps, gaps, kinks and overlaps, for every GLB in Resources/Models.

Ty 2026-11-04: "there is a weird break in the skin on the wing where the aileron begins that looks bad this is true on
most models fix that." This measures it, so the fix has a work list and a pass mark.

Plain Python + NumPy, no Blender:  python3 tools/own_models/check_aileron_junction.py [--json out.json] [glb ...]

Frame. The GLB is read in its own glTF frame (+y up) with every node's world transform applied. Span is the axis on
which the left and right ailerons' centres differ; chord is the remaining horizontal axis, its FORWARD sign the side
the wing's centre is on relative to the aileron's. Distances are in the file's units (our own models are metres).

What is measured, per aileron, at neutral (the file's rest pose):

  LE    along the aileron's leading edge at 7 stations: the chordwise GAP between the wing's last skin ahead of it and
        the aileron's nose; the vertical STEP of the UPPER and LOWER skin across it (wing 8 mm ahead of the nose against
        the aileron 8 mm behind it); the KINK (change of upper-surface slope across it, degrees).
  ROOT  at the aileron's inboard end, at 25/50/75 % of its chord: the upper and lower skin of the wing 12 mm inboard
        of the end against the aileron 12 mm outboard (STEP), and the spanwise GAP to the last wing skin over that chord.
  UNDER whether the wing's own skin carries on under or over the aileron (a coplanar duplicate within 3 mm: the depth
        test then chooses pixel by pixel, which flickers; or a wing surface standing proud of it).

A junction FAILS where |step| > 2.5 mm, a LE gap > 12 mm, a ROOT gap > 15 mm, a kink > 10 deg, or any UNDER overlap.
Models with no aileron node are listed as RUNTIME CARVE: their ailerons are cut out of the mesh in Unity
(AirframeVisual.CarveModel) and that junction is measured there, not here.
"""
import glob
import json
import math
import re
import struct
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
MODELS = ROOT / 'unity/Assets/_Project/Resources/Models'
LIMIT = dict(step=0.0025, le_gap=0.012, root_gap=0.015, kink=10.0, under=0.003)
COMP = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NCOMP = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}


def read_glb(path):
    b = Path(path).read_bytes()
    jl = struct.unpack('<I', b[12:16])[0]
    j = json.loads(b[20:20 + jl])
    off = 20 + jl
    bl = struct.unpack('<I', b[off:off + 4])[0]
    return j, b[off + 8:off + 8 + bl]


def accessor(j, binb, i):
    a = j['accessors'][i]
    bv = j['bufferViews'][a['bufferView']]
    n, k, dt = a['count'], NCOMP[a['type']], np.dtype(COMP[a['componentType']])
    start = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    stride = bv.get('byteStride', 0) or k * dt.itemsize
    rows = np.frombuffer(binb, dtype=np.uint8, count=stride * (n - 1) + k * dt.itemsize, offset=start)
    rows = np.lib.stride_tricks.as_strided(rows, shape=(n, k * dt.itemsize), strides=(stride, 1))
    return np.ascontiguousarray(rows).view(dt).reshape(n, k).astype(np.float64)


def local(n):
    if 'matrix' in n:
        return np.array(n['matrix'], float).reshape(4, 4).T
    t = np.array(n.get('translation', [0, 0, 0]), float)
    x, y, z, w = n.get('rotation', [0, 0, 0, 1])
    s = np.array(n.get('scale', [1, 1, 1]), float)
    r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    m = np.eye(4)
    m[:3, :3] = r * s
    m[:3, 3] = t
    return m


def world_mats(j):
    nodes = j['nodes']
    parent = {}
    for i, n in enumerate(nodes):
        for c in n.get('children', []):
            parent[c] = i
    cache = {}

    def w(i):
        if i not in cache:
            cache[i] = (w(parent[i]) if i in parent else np.eye(4)) @ local(nodes[i])
        return cache[i]
    return [w(i) for i in range(len(nodes))], parent


def triangles(j, binb, node_ids, mats):
    """World triangles (T, 3, 3) of the meshes on these nodes, every primitive."""
    out = []
    for i in node_ids:
        n = j['nodes'][i]
        if 'mesh' not in n:
            continue
        for p in j['meshes'][n['mesh']]['primitives']:
            if p.get('mode', 4) != 4:
                continue
            pos = accessor(j, binb, p['attributes']['POSITION'])
            idx = accessor(j, binb, p['indices']).astype(np.int64).ravel() if 'indices' in p else np.arange(len(pos))
            pw = (mats[i] @ np.c_[pos, np.ones(len(pos))].T).T[:, :3]
            out.append(pw[idx.reshape(-1, 3)])
    return np.concatenate(out) if out else np.zeros((0, 3, 3))


def descendants(j, i):
    out, stack = [], [i]
    while stack:
        k = stack.pop()
        out.append(k)
        stack.extend(j['nodes'][k].get('children', []))
    return out


class Skin:
    """Vertical ray casts into a triangle set, in (chord, span, up) coordinates."""

    def __init__(self, tris):
        self.t = tris
        self.lo = tris.min(axis=1)
        self.hi = tris.max(axis=1)

    def hits(self, c, s):
        m = (self.lo[:, 0] <= c) & (self.hi[:, 0] >= c) & (self.lo[:, 1] <= s) & (self.hi[:, 1] >= s)
        t = self.t[m]
        if len(t) == 0:
            return np.array([])
        a, b, d = t[:, 0], t[:, 1], t[:, 2]
        # Barycentric in the (chord, span) plane.
        v0, v1 = b[:, :2] - a[:, :2], d[:, :2] - a[:, :2]
        v2 = np.array([c, s]) - a[:, :2]
        den = v0[:, 0] * v1[:, 1] - v1[:, 0] * v0[:, 1]
        ok = np.abs(den) > 1e-14
        den = np.where(ok, den, 1.0)
        u = (v2[:, 0] * v1[:, 1] - v1[:, 0] * v2[:, 1]) / den
        v = (v0[:, 0] * v2[:, 1] - v2[:, 0] * v0[:, 1]) / den
        ok &= (u >= -1e-9) & (v >= -1e-9) & (u + v <= 1 + 1e-9)
        z = a[:, 2] + u * (b[:, 2] - a[:, 2]) + v * (d[:, 2] - a[:, 2])
        return np.sort(z[ok])

    def upper(self, c, s):
        h = self.hits(c, s)
        return h[-1] if len(h) else None

    def lower(self, c, s):
        h = self.hits(c, s)
        return h[0] if len(h) else None


def measure(path):
    j, binb = read_glb(path)
    mats, parent = world_mats(j)
    nodes = j['nodes']
    name = lambda i: nodes[i].get('name', '')
    ail_ids = [i for i, n in enumerate(nodes) if re.fullmatch(r'Aileron[._]?(L|R|UL|UR|LL|LR)', n.get('name', '')) and 'mesh' in n]
    if not ail_ids and not (ROOT / 'configs/aircraft' / (Path(path).parent.name + '.json')).exists():
        return dict(file=Path(path).name, status='NOT AN AIRFRAME', note='no aircraft config of this name')
    if not ail_ids:
        return dict(file=Path(path).name, status='RUNTIME CARVE', note='no aileron node: cut out in Unity (AirframeVisual.CarveModel)')
    wing_ids = [i for i, n in enumerate(nodes) if re.fullmatch(r'Wing([._]?(L|R|Upper|Lower))?', n.get('name', '')) and 'mesh' in n]
    if not wing_ids:
        return dict(file=Path(path).name, status='NO WING NODE', ailerons=[name(i) for i in ail_ids])
    # Axes: up = glTF +y. Span = the horizontal axis on which the ailerons' centres spread most.
    cents = {i: triangles(j, binb, [i], mats).reshape(-1, 3).mean(axis=0) for i in ail_ids}
    cc = np.array(list(cents.values()))
    span_ax = 0 if np.ptp(cc[:, 0]) > np.ptp(cc[:, 2]) else 2
    chord_ax = 2 if span_ax == 0 else 0
    wing_all = triangles(j, binb, [k for w in wing_ids for k in descendants(j, w) if not name(k).startswith('Aileron')], mats)
    fwd = np.sign(wing_all.reshape(-1, 3)[:, chord_ax].mean() - cc[:, chord_ax].mean()) or 1.0

    def csu(t):   # -> (chord forward +, span, up)
        return np.stack([t[..., chord_ax] * fwd, t[..., span_ax], t[..., 1]], axis=-1)
    report = dict(file=Path(path).name, status='OK', ailerons=[])
    STEP = 0.002
    wings = {}
    for w in wing_ids:
        wt = triangles(j, binb, [k for k in descendants(j, w) if not name(k).startswith('Aileron')], mats)
        if len(wt):
            wings[w] = csu(wt)
    for ai in ail_ids:
        at = csu(triangles(j, binb, [k for k in descendants(j, ai)], mats))
        A = Skin(at)
        av = at.reshape(-1, 3)
        side = np.sign(av[:, 1].mean())
        s_in = av[:, 1].min() if side > 0 else av[:, 1].max()      # inboard end (nearer the centreline)
        s_out = av[:, 1].max() if side > 0 else av[:, 1].min()
        c_lo, c_hi = av[:, 0].min() - 0.01, av[:, 0].max() + 0.01

        def edges(s):
            """(leading edge, trailing edge) of the aileron at span s, by scanning vertical rays along the chord."""
            cs = np.arange(c_hi, c_lo, -STEP)
            hit = [c for c in cs if len(A.hits(c, s))]
            return (hit[0], hit[-1]) if hit else (None, None)
        s_mid = 0.5 * (s_in + s_out)
        le_mid, te_mid = edges(s_mid)
        if le_mid is None:
            continue
        # The wing this aileron belongs to: skin just ahead of its nose at mid-span, nearest in height.
        za_mid = A.upper(le_mid - 0.01, s_mid)
        best, bestd = None, 1e9
        for w, wt in wings.items():
            h = Skin(wt).hits(le_mid + 0.03, s_mid)
            if len(h) and za_mid is not None and abs(h[-1] - za_mid) < bestd:
                best, bestd = w, abs(h[-1] - za_mid)
        if best is None:
            report['ailerons'].append(dict(aileron=name(ai), wing=None, fail=['no wing skin ahead of the aileron'], le=[], root=[], under=[], chord=0))
            continue
        W = Skin(wings[best])
        res = dict(aileron=name(ai), wing=name(best), span_in=round(float(s_in), 4), span_out=round(float(s_out), 4),
                   chord=round(float(le_mid - te_mid), 4), le=[], root=[], under=[], fail=[])
        for f in np.linspace(0.06, 0.94, 7):
            s = s_in + f * (s_out - s_in)
            le, te = edges(s)
            if le is None:
                continue
            chord = le - te
            # The chordwise GAP: from the aileron's nose forward to the first wing skin.
            ahead = [c for c in np.arange(le, le + 0.3 * chord, STEP / 2) if len(W.hits(c, s))]
            gap = float(ahead[0] - le) if ahead else float('nan')
            row = dict(f=round(float(f), 2), span=round(float(s), 3), chord=round(float(chord), 3),
                       gap_mm=round(gap * 1000, 1) if gap == gap else None)
            for surf in ('upper', 'lower'):
                zw = getattr(W, surf)(le + gap + 0.008 if gap == gap else le + 0.008, s)
                za = getattr(A, surf)(le - 0.008, s)
                row[surf + '_step_mm'] = round((za - zw) * 1000, 1) if zw is not None and za is not None else None
            g0 = (gap if gap == gap else 0.0)
            zw2, zw1 = W.upper(le + g0 + 0.030, s), W.upper(le + g0 + 0.008, s)
            za1, za2 = A.upper(le - 0.008, s), A.upper(le - 0.030, s)
            if None not in (zw2, zw1, za1, za2):
                sw = math.degrees(math.atan2(zw1 - zw2, 0.022))
                sa = math.degrees(math.atan2(za2 - za1, 0.022))
                row['kink_deg'] = round(sa - sw, 1)
            res['le'].append(row)
            # UNDER: wing skin at the aileron's mid-chord within 3 mm of the aileron's skin, or standing above it.
            cm = le - 0.5 * chord
            ha, hw = A.hits(cm, s), W.hits(cm, s)
            if len(ha) and len(hw):
                d = min(abs(x - y) for x in ha for y in hw)
                if d < LIMIT['under'] or hw[-1] > ha[-1] + 1e-4:
                    res['under'].append(dict(span=round(float(s), 3), nearest_mm=round(d * 1000, 2), wing_top_above=bool(hw[-1] > ha[-1] + 1e-4)))
        # ROOT: just outboard of the inboard end, compare with the wing just inboard of it, across the local chord.
        s_a = s_in + side * 0.012
        le0, te0 = edges(s_a)
        if le0 is not None:
            res['root_chord'] = round(float(le0 - te0), 3)
            for fc in (0.25, 0.5, 0.75):
                c = le0 - fc * (le0 - te0)
                row = dict(fc=fc)
                inward = [k for k in np.arange(0.0, 0.3, STEP) if len(W.hits(c, s_in - side * k))]
                row['gap_mm'] = round(float(inward[0]) * 1000, 1) if inward else None
                sw = s_in - side * max(0.012, (inward[0] + 0.006) if inward else 0.012)
                for surf in ('upper', 'lower'):
                    zw, za = getattr(W, surf)(c, sw), getattr(A, surf)(c, s_a)
                    row[surf + '_step_mm'] = round((za - zw) * 1000, 1) if zw is not None and za is not None else None
                row['wing_inboard'] = bool(inward)
                res['root'].append(row)
        for row in res['le']:
            for k in ('upper_step_mm', 'lower_step_mm'):
                if row.get(k) is not None and abs(row[k]) > LIMIT['step'] * 1000:
                    res['fail'].append(f"LE {k[:5]} step {row[k]} mm at span {row['span']}")
            if row.get('gap_mm') is not None and row['gap_mm'] > LIMIT['le_gap'] * 1000:
                res['fail'].append(f"LE gap {row['gap_mm']} mm at span {row['span']}")
            if row.get('gap_mm') is None:
                res['fail'].append(f"LE no wing skin within 30 % chord ahead of the nose at span {row['span']}")
            if row.get('kink_deg') is not None and abs(row['kink_deg']) > LIMIT['kink']:
                res['fail'].append(f"LE kink {row['kink_deg']} deg at span {row['span']}")
        for row in res['root']:
            for k in ('upper_step_mm', 'lower_step_mm'):
                if row.get(k) is not None and abs(row[k]) > LIMIT['step'] * 1000:
                    res['fail'].append(f"ROOT {k[:5]} step {row[k]} mm at {int(row['fc'] * 100)} % chord")
            if row.get('gap_mm') is not None and row['gap_mm'] > LIMIT['root_gap'] * 1000:
                res['fail'].append(f"ROOT gap {row['gap_mm']} mm at {int(row['fc'] * 100)} % chord")
            if not row['wing_inboard']:
                res['fail'].append(f"ROOT no wing skin within 0.3 m inboard at {int(row['fc'] * 100)} % chord (aileron reaches aft of the inboard trailing edge)")
        for u in res['under']:
            res['fail'].append(f"UNDER wing skin {'above' if u['wing_top_above'] else 'coplanar with'} the aileron at span {u['span']} ({u['nearest_mm']} mm)")
        report['ailerons'].append(res)
    if any(a['fail'] for a in report['ailerons']):
        report['status'] = 'FAIL'
    return report


def main(argv):
    out = None
    if '--json' in argv:
        k = argv.index('--json')
        out = argv[k + 1]
        argv = argv[:k] + argv[k + 2:]
    files = argv or sorted(glob.glob(str(MODELS / '*/*.glb')))
    reports = []
    for p in files:
        try:
            r = measure(p)
        except Exception as e:     # a file this cannot read is reported, not hidden
            r = dict(file=Path(p).name, status='ERROR', note=repr(e))
        reports.append(r)
        print(f"{r['file']:44s} {r['status']}" + (f"  ({r.get('note')})" if r.get('note') else ''))
        for a in r.get('ailerons', []):
            worst = {}
            for row in a['le'] + a['root']:
                for k in ('upper_step_mm', 'lower_step_mm', 'gap_mm', 'kink_deg'):
                    if row.get(k) is not None and abs(row[k]) > abs(worst.get(k, 0)):
                        worst[k] = row[k]
            print(f"    {a['aileron']:11s} on {a['wing']:10s} chord {a['chord']:.3f}  worst: " +
                  ', '.join(f"{k} {v}" for k, v in worst.items()) + f"  ->  {len(a['fail'])} faults")
            for f in a['fail'][:6]:
                print('       ', f)
    if out:
        Path(out).write_text(json.dumps(reports, indent=1))
    return 1 if any(r['status'] == 'FAIL' for r in reports) else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
