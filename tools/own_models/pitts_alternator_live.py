"""Ty 2026-11-04 19:22: the Pitts' rear switches are MASTER, ALTERNATOR, FUEL PUMP and SMOKE.

pitts_panel_v4.py drew the ALTERNATOR as a static copy of the master toggle ("visual only": that pass was paint
and geometry, and the sim had no bus). The sim now has one (src/FlyingGame.Core/Engine/ElectricalSystem.cs), so
the copy becomes a control like its neighbours: renamed Ctl_Toggle_Alternator, given the master's Grip_/Poke_
empties, its rig extras and a rig row with param `alternator`, and a `controls` row. Every switch row also gets
its placard text as `label`, which EditMode PittsCockpitInventoryTests checks.

Node indices are preserved (two empties are appended), the mesh is the master's own (shared, untouched). Idempotent:
a GLB that already has Ctl_Toggle_Alternator is left alone. Run with /usr/bin/python3 (numpy, Pillow).
"""
import copy, json
from vr_cockpit import Glb, MODELS, ROOT
from drop_card_slot import validate

MODEL = 'pitts-s2b-like'
LABELS = {'master': 'MASTER', 'alternator': 'ALTERNATOR', 'fuel_pump': 'FUEL PUMP', 'smoke': 'SMOKE'}
ALT_Z = .176   # pitts_panel_v4.py's alternator pivot: 50 mm outboard of MASTER on the same row


def control_row(master):
    row = copy.deepcopy(master)
    row.update(node='Ctl_Toggle_Alternator', param='alternator', grip='Grip_Alternator', poke='Poke_Alternator')
    row['pivot_gltf'] = [master['pivot_gltf'][0], master['pivot_gltf'][1], ALT_Z]
    return row


def label(controls):
    for c in controls:
        if c.get('kind') == 'Toggle' and c.get('param') in LABELS: c['label'] = LABELS[c['param']]
    return controls


def update(g):
    nodes = g.doc['nodes']; by = {n['name']: i for i, n in enumerate(nodes)}
    controls = g.doc['extras']['vrCockpitPhase3a']['controls']
    if 'Ctl_Toggle_Alternator' in by:
        label(controls); return False
    master = nodes[by['Ctl_Toggle_Master']]; alt = nodes[by['Pitts_Alternator_Switch']]
    assert alt['mesh'] == master['mesh'], 'the alternator is no longer a copy of the master toggle'
    assert abs(alt['translation'][2] - ALT_Z) < 1e-6, alt['translation']
    alt['name'] = 'Ctl_Toggle_Alternator'
    mrow = next(c for c in controls if c['node'] == 'Ctl_Toggle_Master')
    row = control_row(mrow)
    extras = copy.deepcopy(master['extras']); extras['rig_param'] = 'alternator'; extras['interaction'] = row
    alt['extras'] = extras
    children = []
    for c in master.get('children', []):
        e = copy.deepcopy(nodes[c]); e['name'] = e['name'].replace('Master', 'Alternator')
        nodes.append(e); children.append(len(nodes) - 1)
    alt['children'] = children
    i = next(k for k, c in enumerate(controls) if c['node'] == 'Ctl_Toggle_Master')
    controls.insert(i + 1, row)
    label(controls)
    v4 = g.doc['extras'].get('pittsPanelV4')
    if v4 is not None: v4['alternator'] = 'live: ElectricalSystem (pitts_alternator_live.py)'
    validate(g, ROOT / 'pitts-alternator-live')
    return True


def update_rig(rig, controls):
    if not any(r['node'] == 'Ctl_Toggle_Alternator' for r in rig['rig']):
        m = next(r for r in rig['rig'] if r['node'] == 'Ctl_Toggle_Master')
        r = copy.deepcopy(m); r.update(node='Ctl_Toggle_Alternator', param='alternator')
        r['pivot_gltf'] = [m['pivot_gltf'][0], m['pivot_gltf'][1], ALT_Z]
        rig['rig'].insert(rig['rig'].index(m) + 1, r)
    rig['controls'] = copy.deepcopy(controls)
    return rig


def main():
    rp = ROOT / 'docs/models/pitts-s2b-own-rig.json'
    rig = json.loads(rp.read_text()); controls = None
    for path in sorted((MODELS / MODEL).glob('*-own*.glb')):
        g = Glb(path); changed = update(g)
        controls = g.doc['extras']['vrCockpitPhase3a']['controls']
        g.save(path); print(path.name, 'converted' if changed else 'already live')
    rp.write_text(json.dumps(update_rig(rig, controls), indent=1) + '\n')


if __name__ == '__main__':
    main()
