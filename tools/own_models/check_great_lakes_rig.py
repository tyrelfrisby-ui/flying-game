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

    # THE QUADRANT, both cockpits (owner Ty, 2026-11-01: "the throttle quadrant lever does not move"). The two
    # throttles must be on the SAME input so they move together, the prop and mixture levers must be pinned full
    # forward rather than following an engine that can stop, and the three knobs must be the colours a pilot
    # reaches for without looking: throttle BLACK, mixture dark RED, prop BLUE.
    by_node = {row['node']: row for row in rig['rig']}
    materials = [m.get('name') for m in doc.get('materials', [])]

    def knob_materials(node_name):
        i = names[node_name]
        return {materials[p['material']] for p in doc['meshes'][nodes[i]['mesh']]['primitives']}

    for seat in ('F', 'R'):
        throttle = by_node['Throttle_%s' % seat]
        assert throttle['param'] == 'throttle', (seat, 'throttle not on the throttle input')
        travel = [v for _, v in throttle['map']]
        assert max(travel) - min(travel) > 30, (seat, 'throttle has no travel', throttle['map'])
        assert 'Cockpit_black' in knob_materials('Throttle_%s' % seat), (seat, 'throttle knob is not black')
        for lever, param, knob in (('Prop', 'prop', 'Cockpit_knob_blue'), ('Mixture', 'mixture', 'Cockpit_knob_red')):
            row = by_node['%s_%s' % (lever, seat)]
            assert row['param'] == param, (seat, lever, 'parameter')
            held = {v for _, v in row['map']}
            assert len(held) == 1 and max(held) == max(travel), (seat, lever, 'not pinned full forward', row['map'])
            assert knob in knob_materials('%s_%s' % (lever, seat)), (seat, lever, 'knob colour', knob)

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

        across = [lateral('%s_%s' % (l, seat)) for l in ('Throttle', 'Prop', 'Mixture')]
        assert across[0] > across[1] > across[2], (
            seat, 'lever order is not throttle, prop, mixture left to right', across)

    # THE FRONT PANEL, from Ty's photograph of the real front cockpit (art/GL-FRONT-PANEL-AUDIT.md): ONE ROW OF
    # SIX, left to right as the pilot sees it -- altimeter, airspeed, the small centre instrument, the combined
    # oil temp/pressure, the SEPARATE chrome-bezel oil pressure, tachometer. Same lateral convention as the
    # levers above: the glTF lateral axis runs to the pilot's LEFT, so left-to-right is DESCENDING.
    front_row = ['Needle_Alt100_F', 'Needle_ASI_F', 'TC_Symbol_F', 'Needle_OilP_F', 'Needle_OilP2_F', 'Needle_Tach_F']
    for name in front_row:
        assert name in names, ('front panel', name, 'missing')
    seen = [lateral(n) for n in front_row]
    for a, b, lo, hi in zip(front_row, front_row[1:], seen, seen[1:]):
        assert lo > hi, ('front row out of order left to right', a, lo, b, hi)

    # Above centre is a WET COMPASS, not a G-meter. Ty's photo settles it, and the G hands are gone from the
    # front panel entirely -- the rear keeps its own G-meter and its own compass.
    assert 'Compass_Card_F' in names, 'front wet compass missing'
    for gone in ('Needle_G_F', 'Needle_GMax_F', 'Needle_GMin_F'):
        assert gone not in names, (gone, 'the front G-meter was replaced by the compass')
    for kept in ('Needle_G_R', 'Compass_Card_R'):
        assert kept in names, (kept, 'the REAR G-meter and compass must survive')

    def height(node_name):
        i = names[node_name]
        lo, hi = [], []
        for prim in doc['meshes'][nodes[i]['mesh']]['primitives']:
            a = doc['accessors'][prim['attributes']['POSITION']]
            lo.append(a['min'][1]); hi.append(a['max'][1])
        return (min(lo) + max(hi)) / 2 + position(i)[1]

    assert height('Compass_Card_F') > height('TC_Symbol_F') + 0.03, 'the front compass must sit ABOVE the row'
    # ...and it is above the CENTRE of the panel, not out by the airspeed indicator where the G-meter used to be.
    assert lateral('Needle_OilP_F') < lateral('Compass_Card_F') < lateral('Needle_ASI_F'), (
        'the front compass should sit above centre, between the ASI and the combined oil gauge')
    # Export frame: prop ahead of tail along -x, vertical tail above the gear, span along z.
    assert position(names['Rudder_Pivot'])[0] > position(names['Prop'])[0]
    assert position(names['Rudder_HingeAxis'])[1] > position(names['Gear_Pivot_L'])[1]
    assert abs(position(names['Aileron_LL_HingeAxis'])[2]) > 3
    assert Path(str(path) + '.meta').exists(), 'Missing Unity metadata'
    print(f'{path.name}: {len(rig["rig"])}/{len(rig["rig"])} bindings, parents, pivots, moving meshes, axes and unit scales OK')


if __name__ == '__main__':
    check(Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT)
