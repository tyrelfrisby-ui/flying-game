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
        # Meshless Grip_/Poke_ empties carry the hand's grip orientation (VrCockpitHands); nothing hangs off them.
        if not ('mesh' not in n and not n.get('children') and n['name'].startswith(('Grip_', 'Poke_'))):
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

    # THE QUADRANT, both cockpits. Owner Ty, 2026-11-02: the Great Lakes throttle is a QUADRANT LEVER on the
    # left sidewall (not a push-pull). Throttle, prop and mixture are three levers in one gate, each rotating
    # about the gate's lateral axis, forward = full / fine / rich. The two throttles share the throttle input so
    # they move together, and the knobs are the colours a pilot reaches for without looking: throttle BLACK,
    # prop BLUE, mixture dark RED. The throttle carries the Grip_Throttle_* empty the VR hand grabs.
    by_node = {row['node']: row for row in rig['rig']}
    materials = [m.get('name') for m in doc.get('materials', [])]

    def knob_materials(node_name):
        i = names[node_name]
        return {materials[p['material']] for p in doc['meshes'][nodes[i]['mesh']]['primitives']}

    lever_node = {}
    for seat, ctl, full in (('F', '_Front', 'Front'), ('R', '', 'Rear')):
        lever_node[seat] = {'Throttle': 'Throttle_%s' % seat,
                            'Prop': 'Ctl_PushPull_Prop%s' % ctl, 'Mixture': 'Ctl_PushPull_Mixture%s' % ctl}
        throttle = by_node['Throttle_%s' % seat]
        assert throttle['param'] == 'throttle', (seat, 'throttle not on the throttle input')
        assert throttle.get('kind') == 'rot' and throttle.get('axis_gltf') == [0.0, 0.0, 1.0], (seat, 'throttle not a lever')
        travel = [v for _, v in throttle['map']]
        assert max(travel) - min(travel) > 30, (seat, 'throttle has no travel', throttle['map'])
        assert 'Cockpit_black' in knob_materials('Throttle_%s' % seat), (seat, 'throttle knob is not black')
        grip = 'Grip_Throttle_%s' % full
        assert grip in names and parents.get(names[grip]) == names['Throttle_%s' % seat], (seat, 'missing', grip)
        for lever, param, knob in (('Prop', 'prop', 'Cockpit_knob_blue'), ('Mixture', 'mixture', 'Cockpit_knob_red')):
            row = by_node[lever_node[seat][lever]]
            assert row['param'] == param, (seat, lever, 'parameter')
            assert row.get('kind') == 'rot' and row['map'] == throttle['map'], (seat, lever, 'not a quadrant lever', row)
            assert knob in knob_materials(lever_node[seat][lever]), (seat, lever, 'knob colour', knob)
            assert '%s_%s' % (lever, seat) not in by_node, (seat, lever, 'old pinned lever still rigged')

        # LEFT TO RIGHT FROM THE SEAT: throttle, prop, mixture. Ty flies a real Great Lakes and that is his
        # order; it was reversed on 2026-11-01 from a misread photograph and is asserted here so it cannot be
        # "corrected" again by anyone reasoning about axis signs. The glTF lateral axis runs to the pilot's
        # LEFT -- measured from a render taken at the seat eye, after guessing it the other way round twice --
        # so left-to-right is DESCENDING lateral coordinate.
        def lateral(node_name):
            # The accessor's own min/max is enough and needs no binary read.
            i = names[node_name]
            lo, hi = [], []
            for prim in doc['meshes'][nodes[i]['mesh']]['primitives']:
                a = doc['accessors'][prim['attributes']['POSITION']]
                lo.append(a['min'][2]); hi.append(a['max'][2])
            return (min(lo) + max(hi)) / 2 + position(i)[2]

        across = [lateral(lever_node[seat][l]) for l in ('Throttle', 'Prop', 'Mixture')]
        assert across[0] > across[1] > across[2], (
            seat, 'lever order is not throttle, prop, mixture left to right', across)
    # Export frame: prop ahead of tail along -x, vertical tail above the gear, span along z.
    assert position(names['Rudder_Pivot'])[0] > position(names['Prop'])[0]
    assert position(names['Rudder_HingeAxis'])[1] > position(names['Gear_Pivot_L'])[1]
    assert abs(position(names['Aileron_LL_HingeAxis'])[2]) > 3
    assert Path(str(path) + '.meta').exists(), 'Missing Unity metadata'
    print(f'{path.name}: {len(rig["rig"])}/{len(rig["rig"])} bindings, parents, pivots, moving meshes, axes and unit scales OK')


if __name__ == '__main__':
    check(Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT)
