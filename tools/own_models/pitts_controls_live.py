"""Ty 2026-11-04 19:27-19:31 (agent-lanes/refs/pitts/PITTS-REF-NOTES.md): the Pitts' rear cockpit controls, working.

pitts_reference_visuals.py and pitts_geometry_pass3.py drew them from Ty's photos as static models and named their
pivots "for a later Claude runtime wiring pass". This is that pass: rig rows (the param each one moves on, about the
pivot Codex built it at) and `controls` rows (what a headset hand works), on all four liveries.

  blue PROP lever, left wall   PropLever_Rear_Slide (move fore/aft, param prop) carrying PropLever_Rear (the blade,
                               rot about its spindle, param prop_lock: 0 hanging DOWN = locked .. 1 HORIZONTAL forward
                               = released). The slide moves only while released (Core PropLockLever).
  red MIXTURE push-pull        Mixture (move aft, param mixture: 1 pushed in rich .. 0 pulled out, idle cut-off).
  FUEL valve, right wall       FuelValve_Rear (rot about its shaft, param fuel_valve: 1 UP = ON .. 0 forward = OFF).
  TRIM lever, left wall        TrimLever_Rear (move fore/aft, param trim: +1 aft NOSE UP .. -1 forward NOSE DOWN).
  COWL FLAP, under the knee    CowlFlap (rot, param cowl_flap: 0 CLOSED, 0.5 MID, 1 OPEN detents).

Directions follow the legends Codex painted beside each control (OFF forward of the valve, NOSE UP aft, OPEN
forward on the gate). Travels are (E): the blade's 90 deg, an 80 mm prop slot, 50 mm of mixture, +-50 mm of trim and
+-35 deg of cowl flap; Ty's to move. The panel's old push-pull PROP, MIXTURE and rotary FUEL SELECTOR rows leave the
table: pitts_reference_visuals.py already took their geometry away (a hand could otherwise grab an invisible knob).

Rig rows turn a node about pivot_gltf, so no mesh moves: only the prop blade is split off its mount (a new node for
its shaft and blade under a new slide node; node indices are preserved, two nodes are appended). Idempotent.
Run with /usr/bin/python3 (numpy).
"""
import copy, json
from vr_cockpit import Glb, MODELS, ROOT
from drop_card_slot import validate

MODEL = 'pitts-s2b-like'
RETIRED_ROWS = {'Ctl_PushPull_Prop', 'Ctl_PushPull_Mixture', 'Ctl_Rotary_FuelSelector'}
# glTF pivots: builder (X fwd, Y left, Z up) -> glTF (-X, Z, Y), from pitts_reference_visuals.py / pitts_geometry_pass3.py.
PROP_P = [1.08, -0.125, 0.275]
VALVE_P = [0.97, -0.160, -0.277]
TRIM_P = [1.24, -0.240, 0.270]


def rig_row(node, param, kind, axis, pivot, mapping, model, unit, desc, parent=None):
    return {'node': node, 'param': param, 'parent': parent, 'kind': kind, 'axis_gltf': axis, 'pivot_gltf': pivot,
            'map': mapping, 'model': model, 'unit': unit, 'desc': desc, 'extra': {}, 'link': '', 'note': ''}


def control_row(node, param, kind, axis, pivot, travel, unit, ends, detents, label):
    return {'node': node, 'param': param, 'kind': kind, 'seat': 'Rear', 'axis_gltf': axis, 'pivot_gltf': pivot,
            'travel': travel, 'travel_unit': unit, 'sim_ends': ends, 'detents': detents, 'detent_count': len(detents),
            'spring_return': False, 'spring_return_from': ends[1], 'spring_return_to': ends[0], 'poke': '', 'grip': '',
            'position_status': 'E', 'sweep_radius_m': 0.04, 'semantics': 'absolute',
            'placement': "Pitts v4 reference pass (Ty's photos 2026-11-04)", 'label': label}


def rows(nodes_by):
    mix_p = nodes_by['Mixture']['translation']; flap_p = nodes_by['CowlFlap']['translation']
    rig = [
        rig_row('PropLever_Rear_Slide', 'prop', 'move', [-1.0, 0.0, 0.0], PROP_P, [[0, -0.04], [1, 0.04]], 0.5, '0..1',
                '(E) 80 mm prop slot; forward = fine (high rpm); moves only with the blade released'),
        rig_row('PropLever_Rear', 'prop_lock', 'rot', [0.0, 0.0, 1.0], PROP_P, [[0, 0], [1, -90]], 0.0, '0..1',
                'blade DOWN = locked .. HORIZONTAL forward = released (Ty); forward is (E)', parent='PropLever_Rear_Slide'),
        rig_row('Mixture', 'mixture', 'move', [1.0, 0.0, 0.0], mix_p, [[0, 0.05], [1, 0]], 1.0, '0..1',
                '(E) 50 mm push-pull: in = rich, out = lean, fully out = idle cut-off'),
        rig_row('FuelValve_Rear', 'fuel_valve', 'rot', [0.0, 0.0, 1.0], VALVE_P, [[0, 90], [1, 0]], 1.0, '0..1',
                'UP = ON, turned forward to horizontal = OFF (Ty; the OFF legend is forward of the valve)'),
        rig_row('TrimLever_Rear', 'trim', 'move', [1.0, 0.0, 0.0], TRIM_P, [[-1, -0.05], [0, 0], [1, 0.05]], 0.0, '-1..1',
                '(E) +-50 mm along the wall ticks: aft = NOSE UP, forward = NOSE DOWN (the legends)'),
        rig_row('CowlFlap', 'cowl_flap', 'rot', [0.0, 0.0, 1.0], flap_p, [[0, -35], [0.5, 0], [1, 35]], 0.5, '0..1',
                '(E) +-35 deg over the gate: CLOSED aft, MID, OPEN forward'),
    ]
    ctl = [
        control_row('PropLever_Rear', 'prop_lock', 'Lever', [0.0, 0.0, 1.0], PROP_P, 90, 'deg', [0, 1], [], 'PROP'),
        control_row('Mixture', 'mixture', 'PushPull', [1.0, 0.0, 0.0], mix_p, 50, 'mm', [0, 1], [], 'MIXTURE'),
        control_row('FuelValve_Rear', 'fuel_valve', 'Rotary', [0.0, 0.0, 1.0], VALVE_P, 90, 'deg', [0, 1], [0, 1], 'FUEL'),
        control_row('TrimLever_Rear', 'trim', 'PushPull', [1.0, 0.0, 0.0], TRIM_P, 100, 'mm', [-1, 1], [], 'TRIM'),
        control_row('CowlFlap', 'cowl_flap', 'Lever', [0.0, 0.0, 1.0], flap_p, 70, 'deg', [0, 1], [0, 0.5, 1], 'COWL FLAP'),
    ]
    return rig, ctl


def update(g):
    nodes = g.doc['nodes']; by = {n['name']: i for i, n in enumerate(nodes)}
    if 'PropLever_Rear' in by: return False
    # The prop blade (shaft + blue handle) leaves its mount for a node of its own, under the slide.
    mount = nodes[by['Pitts_Prop_Left_Locked']]
    mesh = g.doc['meshes'][mount['mesh']]
    mats = g.doc['materials']
    moving = [p for p in mesh['primitives'] if mats[p['material']]['name'] in ('Pitts_Reference_Metal', 'Pitts_Reference_Blue')]
    fixed = [p for p in mesh['primitives'] if p not in moving]
    assert len(moving) == 2 and len(fixed) == 1, [mats[p['material']]['name'] for p in mesh['primitives']]
    mesh['primitives'] = fixed
    g.doc['meshes'].append({'name': 'PropLever_Rear', 'primitives': moving})
    nodes.append({'name': 'PropLever_Rear', 'mesh': len(g.doc['meshes']) - 1})
    nodes.append({'name': 'PropLever_Rear_Slide', 'children': [len(nodes) - 1]})
    g.doc['scenes'][0]['nodes'].append(len(nodes) - 1)
    for old, new in (('Pitts_FuelValve_Right_On', 'FuelValve_Rear'), ('Pitts_Trim_Left_Forward', 'TrimLever_Rear')):
        nodes[by[old]]['name'] = new
    by = {n['name']: i for i, n in enumerate(nodes)}
    for n in ('Mixture', 'CowlFlap'):
        nodes[by[n]].get('extras', {}).pop('visualOnly', None)
    _, ctl = rows({k: nodes[i] for k, i in by.items()})
    controls = g.doc['extras']['vrCockpitPhase3a']['controls']
    controls[:] = [c for c in controls if c['node'] not in RETIRED_ROWS] + ctl
    g.doc['extras']['pittsControlsLive'] = {'controls': [c['node'] for c in ctl], 'retired_rows': sorted(RETIRED_ROWS)}
    validate(g, ROOT / 'pitts-controls-live')
    return True


def update_rig(rig, g):
    nodes = {n['name']: n for n in g.doc['nodes']}
    new_rig, _ = rows(nodes)
    names = {r['node'] for r in new_rig}
    rig['rig'] = [r for r in rig['rig'] if r['node'] not in names] + new_rig
    rig['controls'] = copy.deepcopy(g.doc['extras']['vrCockpitPhase3a']['controls'])
    return rig


def main():
    rp = ROOT / 'docs/models/pitts-s2b-own-rig.json'
    rig = json.loads(rp.read_text()); last = None
    for path in sorted((MODELS / MODEL).glob('*-own*.glb')):
        g = Glb(path); changed = update(g); g.save(path); last = g
        print(path.name, 'rigged' if changed else 'already live')
    rp.write_text(json.dumps(update_rig(rig, last), indent=1) + '\n')


if __name__ == '__main__':
    main()
