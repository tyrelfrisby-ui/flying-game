"""Keep ONE sequence card on the Extra: the alloy holder (owner Ty, 2026-11-01, "B").

Two lanes each built the card the rig spec asks for, and the merge kept both. The holder lane made
`SequenceCard_Face` in a frame with a clip and paper stock, flush on the rear panel; the other lane made a bare
`SequenceCard_Slot` quad 9 mm nearer the pilot, with its surround and backing WELDED into the `Cockpit_Interior`
mesh. The welded part is why removing the quad alone does not uncover the holder.

This removes the second card entirely:
  - the `SequenceCard_Slot` node, its mesh and its material;
  - the `Cockpit_black` primitive of `Cockpit_Interior` that is the quad's surround (x 1.202..1.216);
  - the upper box of the `Cockpit_placard_blank` primitive, which is the quad's backing plate, keeping the small
    placard lower down on the panel that shares that primitive.

Nothing else is touched: positions, normals and UVs of every surviving primitive keep their original accessors.
"""
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from glb_visuals import Glb

ROOT = Path(__file__).resolve().parents[2]
MODELS = ROOT / 'unity/Assets/_Project/Resources/Models/extra-300-like'
SLOT = 'SequenceCard_Slot'
# The quad's own footprint in WORLD coordinates (the interior node is offset), which both the welded
# surround and the welded backing are built around.
SLOT_Y_MIN = 0.30


def triangles(glb, prim, matrix):
    pts = glb.array(prim['attributes']['POSITION']) @ matrix[:3, :3].T + matrix[:3, 3]
    ids = (glb.array(prim['indices']).reshape(-1, 3) if 'indices' in prim
           else np.arange(len(pts)).reshape(-1, 3))
    return pts, ids


def surgery(path):
    glb = Glb(path)
    d = glb.doc
    report = []

    world = glb.world_matrices()
    index = next(i for i, n in enumerate(d['nodes']) if n.get('name') == 'Cockpit_Interior')
    interior, matrix = d['nodes'][index], world[index]
    prims = d['meshes'][interior['mesh']]['primitives']
    keep = []
    for prim in prims:
        name = d['materials'][prim['material']].get('name')
        pts, ids = triangles(glb, prim, matrix)
        lo, hi = pts.min(0), pts.max(0)
        # The surround: a black box standing proud of the panel face, over the holder's top half.
        if name == 'Cockpit_black' and lo[0] > 1.2 and hi[1] > 0.47 and lo[1] > 0.31:
            report.append(f'    dropped surround primitive ({name}, {len(ids)} tris)')
            continue
        # The backing: one box of a primitive that also carries a small placard further down the panel.
        if name == 'Cockpit_placard_blank' and hi[1] > 0.47:
            low = ids[np.all(pts[ids][:, :, 1] < SLOT_Y_MIN, axis=1)]
            assert 0 < len(low) < len(ids), path.name + ': placard primitive did not split'
            prim = dict(prim, indices=glb.accessor(low.ravel(), 'SCALAR', 5125))
            report.append(f'    trimmed backing plate ({len(ids)} -> {len(low)} tris, kept the lower placard)')
        keep.append(prim)
    d['meshes'][interior['mesh']]['primitives'] = keep

    node = next((i for i, n in enumerate(d['nodes']) if n.get('name') == SLOT), None)
    if node is None:
        report.append('    no SequenceCard_Slot node')
    else:
        mesh = d['nodes'][node]['mesh']
        material = d['meshes'][mesh]['primitives'][0]['material']
        assert d['materials'][material].get('name') == SLOT, path.name + ': unexpected slot material'
        assert sum('mesh' in n and n['mesh'] == mesh for n in d['nodes']) == 1
        assert sum(p['material'] == material for m in d['meshes'] for p in m['primitives']) == 1

        del d['nodes'][node], d['meshes'][mesh], d['materials'][material]
        shift = lambda i, cut: i - (i > cut)
        # A reference to the node that just went must be DROPPED, not shifted. Shifting it leaves the index
        # pointing at whatever slid into that slot, and then one node has two parents -- which is not a glTF
        # scene graph at all. glTFast rejects the file without a word and TryPlaceModel quietly falls back to
        # the downloaded model, so the only symptom is an aeroplane that looks very slightly wrong.
        drop = lambda ids: [shift(i, node) for i in ids if i != node]
        for n in d['nodes']:
            if 'mesh' in n:
                n['mesh'] = shift(n['mesh'], mesh)
            if 'children' in n:
                n['children'] = drop(n['children'])
                if not n['children']:
                    del n['children']
        for scene in d['scenes']:
            scene['nodes'] = drop(scene['nodes'])
        for m in d['meshes']:
            for p in m['primitives']:
                p['material'] = shift(p['material'], material)
        assert 'skins' not in d and 'animations' not in d, path.name + ': index shift would break a skin'
        report.append(f'    dropped node/mesh/material {SLOT}')

    validate(glb, path)
    glb.save(path)
    return report


def validate(glb, path):
    """Every index in range, and every node with at most one parent. The second one is the trap: an index that
    was shifted instead of dropped still resolves, so nothing throws until the importer refuses the file."""
    d = glb.doc
    n_nodes, n_meshes, n_materials = len(d['nodes']), len(d['meshes']), len(d['materials'])
    parents = {}
    for i, node in enumerate(d['nodes']):
        assert 'mesh' not in node or 0 <= node['mesh'] < n_meshes, f'{path.name}: node {i} mesh out of range'
        for child in node.get('children', []):
            assert 0 <= child < n_nodes, f'{path.name}: node {i} child {child} out of range'
            assert child not in parents, f'{path.name}: node {child} has two parents ({parents.get(child)}, {i})'
            parents[child] = i
    roots = set()
    for scene in d['scenes']:
        for i in scene['nodes']:
            assert 0 <= i < n_nodes, f'{path.name}: scene node {i} out of range'
            assert i not in parents, f'{path.name}: node {i} is both a scene root and a child'
            roots.add(i)
    assert all(i in parents or i in roots for i in range(n_nodes)), path.name + ': orphaned node'
    for mesh in d['meshes']:
        for prim in mesh['primitives']:
            # No 'material' is legal glTF (the default material): the 172's airframe primitives have none, its panel's do.
            if 'material' in prim:
                assert 0 <= prim['material'] < n_materials, path.name + ': primitive material out of range'
            for accessor in list(prim['attributes'].values()) + ([prim['indices']] if 'indices' in prim else []):
                assert 0 <= accessor < len(d['accessors']), path.name + ': accessor out of range'


def main():
    for path in sorted(MODELS.glob('*own*.glb')):
        print(path.name)
        for line in surgery(path):
            print(line)


if __name__ == '__main__':
    main()
