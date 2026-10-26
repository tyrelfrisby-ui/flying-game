#!/usr/bin/env python3
"""Measure every shipped airframe model against the published dimensions of the real aeroplane.

  python3 tools/fleet_accuracy.py

A uniform scale cannot change a ratio, and `AirframeModels.Place` scales every model to the config's span at
runtime -- so the only thing a model's raw size tells you is nothing, and the only thing worth measuring is
**length/span and height/span**. That is the same test that caught the Swift mesh being short-coupled by 26%.

Where the numbers come from:

- the mesh: `unity/Assets/_Project/Resources/Models/<id>/<id>.obj|.glb`, the file `Place()` actually loads,
  read exactly -- glTF accessor min/max carried through the node tree, OBJ vertices directly.
- which mesh axis is which: from the MESH, not from `AirframeModels.Specs`. Those vectors describe the model
  AFTER the importer has had it, and reading a raw file with them put the SR22's "up" along its span and
  reported it 330% out. Height is the smallest of the three extents for every aeroplane in this fleet; of the
  other two the larger is the span, unless the type's Spec says `largestIsSpan: false` (the 737). Where the
  two largest extents are within 10% of each other that rule cannot tell them apart, and the row is marked `?`.
- the aeroplane: `assets-incoming/ref-<slug>/DIMENSIONS.md`, written by `tools/ref_photos.py --dims` from the
  {{Aircraft specs}} template of the type's Wikipedia article. The config cannot supply these --
  `fuselage.crossflow.lengthM` is the body's crossflow length, not the aeroplane's.

Prints a table sorted worst first. A model whose ratios are within a few percent is the right shape at the
wrong scale, which `Place()` fixes. One that is 15% out is the wrong shape, and no scale will save it.
"""

import json
import os
import re
import struct
import sys

REPO = os.path.dirname(os.path.dirname(os.path.realpath(__file__)))
MODELS = os.path.join(REPO, "unity/Assets/_Project/Resources/Models")
CS = os.path.join(REPO, "unity/Assets/_Project/Scripts/Bridge/AirframeModels.cs")

sys.path.insert(0, os.path.join(REPO, "tools"))
import importlib.util                                                    # noqa: E402
_spec = importlib.util.spec_from_file_location("ref_photos", os.path.join(REPO, "tools/ref_photos.py"))
ref_photos = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ref_photos)

# Dimensions Wikipedia does not carry, with the source that does.
OVERRIDE = {"swift-s1": {"height": (1.74, "EASA TCDS A.038, flight attitude")}}


def specs():
    """id -> (forward, up) straight out of AirframeModels.Specs, so this file cannot drift from the C#."""
    src = open(CS).read()
    block = src[src.index("Specs = new()"):]
    block = block[:block.index("\n        };")]
    out = {}
    for line in block.split("\n"):
        line = line.split("//")[0].strip()
        m = re.match(r'\{\s*"([^"]+)"\s*,\s*new Spec\((.*)\)\s*\}\s*,?$', line)
        if not m:
            continue
        ident, args = m.group(1), m.group(2)
        parts, depth, cur = [], 0, ""
        for ch in args:
            if ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1
            if ch == "," and depth == 0:
                parts.append(cur); cur = ""
                continue
            cur += ch
        parts.append(cur)
        largest_is_span, pitch_down = True, 0.0
        for p in parts:
            flat = p.replace(" ", "").lower()
            if flat.startswith("largestisspan:"):
                largest_is_span = "false" not in flat
            elif flat.startswith("pitchdowndeg:"):
                pitch_down = float(flat.split(":", 1)[1].rstrip("f"))
        pos = [p for p in parts if ":" not in p.split("(")[0]]
        if len(pos) > 2 and "false" in pos[2].lower():
            largest_is_span = False
        out[ident] = (largest_is_span, pitch_down)
    return out


def obj_points(path):
    pts = []
    for line in open(path, errors="replace"):
        if line.startswith("v "):
            t = line.split()
            pts.append((float(t[1]), float(t[2]), float(t[3])))
    return pts


def glb_points(path):
    """The eight corners of every primitive's POSITION box, carried through the node tree.

    Node transforms matter: glTF exports keep parts tilted on their nodes, so an accessor's min/max read flat
    is the part's box in ITS OWN frame, not the aeroplane's."""
    with open(path, "rb") as f:
        data = f.read()
    assert data[:4] == b"glTF", path
    off, js = 12, None
    while off < len(data):
        ln, ty = struct.unpack_from("<II", data, off)
        if ty == 0x4E4F534A:
            js = json.loads(data[off + 8:off + 8 + ln])
        off += 8 + ln
    def mul(a, b):
        return [sum(a[i + k * 4] * b[k + j * 4] for k in range(4)) for j in range(4) for i in range(4)]
    def trs(n):
        if "matrix" in n:
            return list(n["matrix"])
        t = n.get("translation", [0, 0, 0]); r = n.get("rotation", [0, 0, 0, 1]); s = n.get("scale", [1, 1, 1])
        x, y, z, w = r
        rot = [1-2*(y*y+z*z), 2*(x*y+z*w), 2*(x*z-y*w), 0,
               2*(x*y-z*w), 1-2*(x*x+z*z), 2*(y*z+x*w), 0,
               2*(x*z+y*w), 2*(y*z-x*w), 1-2*(x*x+y*y), 0,
               0, 0, 0, 1]
        for c in range(3):
            for r_ in range(3):
                rot[c * 4 + r_] *= s[c]
        rot[12], rot[13], rot[14] = t
        return rot
    pts = []
    def walk(idx, m):
        n = js["nodes"][idx]
        m = mul(m, trs(n))
        if "mesh" in n:
            for p in js["meshes"][n["mesh"]]["primitives"]:
                a = js["accessors"][p["attributes"]["POSITION"]]
                lo, hi = a["min"], a["max"]
                for cx in (lo[0], hi[0]):
                    for cy in (lo[1], hi[1]):
                        for cz in (lo[2], hi[2]):
                            pts.append((m[0]*cx + m[4]*cy + m[8]*cz + m[12],
                                        m[1]*cx + m[5]*cy + m[9]*cz + m[13],
                                        m[2]*cx + m[6]*cy + m[10]*cz + m[14]))
        for c in n.get("children", []):
            walk(c, m)
    ident = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
    scene = js.get("scenes", [{}])[js.get("scene", 0)]
    for root in scene.get("nodes", range(len(js.get("nodes", [])))):
        walk(root, ident)
    return pts


def model_file(ident, own=False):
    for ext in (".obj", ".glb"):
        p = os.path.join(MODELS, ident, ident + ("-own" if own else "") + ext)
        if os.path.exists(p):
            return p
    return None


def axes(pts, largest_is_span, pitch_down=0.0):
    """(span, length, height, ambiguous) from the mesh's own extents.

    `pitch_down` is the type's Spec value: the model was drawn sitting on its tail, and `Place()` levels it
    before it is ever seen. Measuring the raw file without that correction reports a tall, short aeroplane --
    on the Extra, +41% in height/span that the game never shows."""
    big, mid, small = sorted((max(p[i] for p in pts) - min(p[i] for p in pts) for i in range(3)),
                             reverse=True)
    ambiguous = mid > 0 and (big - mid) / big < 0.10
    # A model with a non-zero PitchDownDeg was drawn sitting on its tail, and `Place()` levels it before it is
    # ever seen. Its raw height/span is therefore not comparable with a published height, and no correction is
    # attempted here: rotating by the Spec's angle made the Extra's error worse rather than better, which means
    # the sign convention is not what it looks like, and a number I cannot verify is worse than no number.
    span = big if largest_is_span else mid
    length = mid if largest_is_span else big
    return span, length, small, ambiguous


def published(slug):
    p = os.path.join(REPO, "assets-incoming", "ref-" + slug, "DIMENSIONS.md")
    if not os.path.exists(p):
        return {}
    m = re.search(r"Derived: span ([\d.None]+) m, length ([\d.None]+) m, height ([\d.None]+) m",
                  open(p).read())
    if not m:
        return {}
    out = {}
    for k, v in zip(("span", "length", "height"), m.groups()):
        if v != "None":
            out[k] = float(v)
    for k, (v, _src) in OVERRIDE.get(slug, {}).items():
        out.setdefault(k, v)
    return out


def main():
    sp = specs()
    slug_of = {cid: t[0] for cid, t in ref_photos.TYPES.items()}
    # Ours too, wherever one exists. A model we built is measured on exactly the same terms as a download:
    # ratios against the published aeroplane, and no credit for having drawn it ourselves. Our exports land
    # nose +x, up +y, span z, so the largest extent is the span and there is no three-point attitude.
    jobs = [(ident, lis, pd, False) for ident, (lis, pd) in sorted(sp.items())]
    for cid in sorted(ref_photos.TYPES):
        if model_file(cid, own=True):
            jobs.append((cid, True, 0.0, True))
    rows = []
    for ident, largest_is_span, pitch_down, own in jobs:
        path = model_file(ident, own=own)
        slug = slug_of.get(ident)
        if path is None or slug is None:
            if own:
                continue
            rows.append((None, ident, "no model file" if path is None else "no reference", None, None, None, None, False))
            continue
        pts = obj_points(path) if path.endswith(".obj") else glb_points(path)
        S, L, H, ambiguous = axes(pts, largest_is_span, pitch_down)
        real = published(slug)
        if "span" not in real or "length" not in real or not S:
            rows.append((None, ident, "no published dims", None, None, None, None, False))
            continue
        ls_m, ls_r = L / S, real["length"] / real["span"]
        hs_m = H / S if S else None
        hs_r = real["height"] / real["span"] if "height" in real else None
        if pitch_down:
            hs_r = None                 # drawn three-point; its raw height is not the aeroplane's
        e_l = 100.0 * (ls_m - ls_r) / ls_r
        e_h = 100.0 * (hs_m - hs_r) / hs_r if hs_r else None
        worst = max(abs(e_l), abs(e_h) if e_h is not None else 0.0)
        rows.append((worst, ident + (" (ours)" if own else ""), os.path.basename(path),
                     ls_m, ls_r, e_l, e_h, ambiguous))
    rows.sort(key=lambda r: (-1 if r[0] is None else -r[0]))
    print("%-29s %-30s %7s %7s %8s %8s" % ("type", "file", "L/S", "real", "dL/S %", "dH/S %"))
    print("-" * 99)
    for worst, ident, f, lsm, lsr, el, eh, amb in rows:
        if worst is None:
            print("%-29s %-30s %s" % (ident, f, "--"))
            continue
        print("%-29s %-28s%s %7.4f %7.4f %+8.1f %s"
              % (ident, f[:28], " ?" if amb else "  ", lsm, lsr, el,
                 "%+8.1f" % eh if eh is not None else "       -"))


if __name__ == "__main__":
    main()
