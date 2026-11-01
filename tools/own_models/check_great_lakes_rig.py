"""Check the exported GLB against the runtime rig without starting Unity.

Usage: python3 tools/own_models/check_great_lakes_rig.py [path.glb]
"""
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
DEFAULT = ROOT / 'unity/Assets/_Project/Resources/Models/great-lakes-like/great-lakes-like-own.glb'


def check(path):
    raw = path.read_bytes()
    assert raw[:4] == b'glTF' and struct.unpack_from('<I', raw, 4)[0] == 2
    size = struct.unpack_from('<I', raw, 12)[0]
    doc = json.loads(raw[20:20 + size])
    nodes = doc['nodes']
    names = {n['name']: i for i, n in enumerate(nodes)}
    assert len(names) == len(nodes), 'Duplicate node names'
    parents = {c: i for i, n in enumerate(nodes) for c in n.get('children', [])}

    def position(i):
        parent = position(parents[i]) if i in parents else (0, 0, 0)
        return [a + b for a, b in zip(parent, nodes[i].get('translation', (0, 0, 0)))]

    def has_mesh(i):
        return 'mesh' in nodes[i] or any(has_mesh(c) for c in nodes[i].get('children', []))

    for n in nodes:
        assert n.get('scale', [1, 1, 1]) == [1, 1, 1], (n['name'], 'scale')
        assert n.get('rotation', [0, 0, 0, 1]) == [0, 0, 0, 1], (n['name'], 'rotation')
        assert 'matrix' not in n, (n['name'], 'matrix')
    rig = json.loads((ROOT / 'docs/models/great-lakes-own-rig.json').read_text())
    for row in rig['rig']:
        name = row['node']
        assert name in names, ('Missing binding', name)
        i = names[name]
        parent = nodes[parents[i]]['name'] if i in parents else None
        assert parent == row['parent'], (name, 'parent', parent)
        assert max(abs(a - b) for a, b in zip(position(i), row['pivot_gltf'])) < .00011, (name, 'pivot')
        assert nodes[i].get('extras', {}).get('rig_param') == row['param'], (name, 'parameter')
        assert has_mesh(i), (name, 'No moving geometry')
    for name in ('Prop', 'Spinner', 'Eye_Front', 'Eye_Rear', 'Radio_Display_R', 'Xpdr_Display_R',
                 'SequenceCard_Face', 'Sight_L_Level', 'Sight_R_Level'):
        assert name in names, name
    # Export frame: prop ahead of tail along -x, vertical tail above the gear, span along z.
    assert position(names['Rudder_Pivot'])[0] > position(names['Prop'])[0]
    assert position(names['Rudder_HingeAxis'])[1] > position(names['Gear_Pivot_L'])[1]
    assert abs(position(names['Aileron_LL_HingeAxis'])[2]) > 3
    assert Path(str(path) + '.meta').exists(), 'Missing Unity metadata'
    print(f'{path.name}: {len(rig["rig"])}/{len(rig["rig"])} bindings, parents, pivots, moving meshes, axes and unit scales OK')


if __name__ == '__main__':
    check(Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT)
