"""The J-3's rig tables (docs/models/j3-cub-own-rig.json, j3-cub-floats-own-rig.json), read out of its own GLBs.

Codex's J-3 build (build_j3_cub.py, v15) stores the rig ON THE NODES as glTF extras (docs/models/j3-cub-own-RIG.md),
but it never wrote the -rig.json table the game reads (AirframeRig.Load: StreamingAssets/rigs/<model>.json, mirrored
from docs/models by ConfigSync). Without a table the game falls back to CARVING the model by the config's hinge
lines, which on a model that already has its own hinged parts tears the geometry and deflects it twice. This writes
the table from the extras, so the GLB stays the one source and a rebuilt model only needs this re-run.

Only parameters the sim supplies are written (FlyingGame.Core.RigInputs.TryGet); a node driven by something the sim
does not have yet stays where it was drawn, and is listed under "static" with the reason, rather than being bound
and warning once per flight. Two names differ between Codex's extras and the sim: engine_rpm -> rpm and
oil_psi -> oil_press_psi. The magneto switch reads OFF / L / R / BOTH where the sim counts 0 OFF, 1 R, 2 L, 3 BOTH,
so its map is re-ordered to put each sim position on the right label.

Engine-free, standard library only:
  python3 -I tools/own_models/j3_rig_json.py          # writes both tables
  python3 -I tools/own_models/j3_rig_json.py --check  # exits 1 if either table is out of step with its GLB
"""
import json
import math
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MODELS = ROOT / 'unity/Assets/_Project/Resources/Models'
CASES = [('j3-cub-like', 'j3-cub-own-rig.json'), ('j3-cub-floats-like', 'j3-cub-floats-own-rig.json')]

RENAME = {'engine_rpm': 'rpm', 'oil_psi': 'oil_press_psi'}
# What the sim cannot drive yet, and why the node stays as drawn.
STATIC = {
    'carb_heat': 'no carburettor heat in the sim',
    'fuel_valve': 'the sim has no J-3 fuel shut-off; drawn ON',
    'fuel': 'nose-tank float wire: no per-tank gauge feed for the J-3; drawn FULL',
    'door_open': 'no door control in the sim; drawn closed',
    'prop_rpm': 'the propeller is spun by AirframeVisual.BindModelProp, not by the rig',
}
# Sim magneto 0 OFF, 1 R, 2 L, 3 BOTH (RigInputs.Magneto) onto the J-3 plate's OFF / L / R / BOTH detent angles.
MAGNETO_SIM_ORDER = ['OFF', 'R', 'L', 'BOTH']
# v18 (Ty 2026-11-02, t284u): the hand-worked switches and knobs (AirframeRig.ParseControls / VrCockpitHands.BindPanel on
# the Quest hands build; ignored by builds without the hands). Only the primer so far: a push-pull pulled straight aft 50 mm.
# The sim has no priming system: RigInputs.Primer sits at 0 (in) and the hand moves the knob (visual only, logged).
CONTROLS = [
    dict(node='Primer_Plunger', param='primer', kind='PushPull', seat='Rear', travel=50, travel_unit='mm', sim_ends=[0, 1],
         detents=[], detent_count=0, spring_return=False, spring_return_from=1, spring_return_to=0, poke='', grip='Grip_Primer',
         position_status='E', sweep_radius_m=0.04, semantics='absolute'),
    # The floats' water-rudder retract handle (lane/claude-j3-water-rudder): the hand pulls it up its rig slide from the
    # floor fairlead to the front-seat-back hook. Let go near the hook it hangs there (UP), anywhere else the spring takes
    # it back to the floor (DOWN): the hands route this param to CockpitWaterRudder.Hold / Release, not to a panel value.
    dict(node='WaterRudder_Handle', param='water_rudder_up', kind='PushPull', seat='Rear', travel=258, travel_unit='mm',
         sim_ends=[0, 1], detents=[], detent_count=0, spring_return=False, spring_return_from=1, spring_return_to=0, poke='',
         grip='Grip_WaterRudder', position_status='E', sweep_radius_m=0.06, semantics='absolute', optional=True),
]


def read_glb(path):
    b = path.read_bytes()
    n = struct.unpack('<I', b[12:16])[0]
    return json.loads(b[20:20 + n])


def world_origins(doc):
    """Each node's origin in the GLB's own frame, and its parent's name. Every rigged J-3 node has identity rotation
    and unit scale (j3-cub-own-RIG.md), so origins add up; anything else is refused rather than mis-placed."""
    parent = {}
    for i, nd in enumerate(doc['nodes']):
        for c in nd.get('children', []): parent[c] = i
    out = {}
    for i, nd in enumerate(doc['nodes']):
        p, j = [0.0, 0.0, 0.0], i
        while j is not None:
            node = doc['nodes'][j]
            if 'matrix' in node or any(abs(a - b) > 1e-6 for a, b in zip(node.get('rotation', [0, 0, 0, 1]), [0, 0, 0, 1])):
                raise SystemExit(f"{nd['name']}: an ancestor ({node['name']}) is rotated; the rig's frame is not the GLB's")
            t = node.get('translation', [0, 0, 0])
            p = [p[k] + t[k] for k in range(3)]
            j = parent.get(j)
        out[nd['name']] = ([round(v, 4) for v in p], doc['nodes'][parent[i]]['name'] if i in parent else None)
    return out


def row_for(name, ex, origin, parent):
    param = ex['rig_param']
    if param in STATIC: return None, STATIC[param]
    move = 'rig_move_axis_gltf' in ex and 'rig_axis_gltf' not in ex
    axis = ex['rig_move_axis_gltf' if move else 'rig_axis_gltf']
    if 'rig_map_param' in ex:
        ps, vs = ex['rig_map_param'], ex['rig_map_m' if move else 'rig_map_deg']
    else:
        # The v7 surfaces: rig_deg_pos for param > 0, rig_deg_neg for param < 0, both linear (j3-cub-own-RIG.md).
        pos, neg = float(ex['rig_deg_pos']), float(ex['rig_deg_neg'])
        ps, vs = [-1.0, 0.0, 1.0], [-neg, 0.0, pos]
    extra = {}
    if ex.get('rig_map_extrapolate'): extra['rig_map_extrapolate'] = 1
    if ex.get('rig_map_wrap'): extra['rig_map_wrap'] = float(ex['rig_map_wrap'])
    note = ex.get('note', '')
    if param == 'magneto':
        labels = ex['rig_detents']
        deg = dict(zip(labels, ex['rig_map_deg']))
        ps, vs = [0.0, 1.0, 2.0, 3.0], [deg[k] for k in MAGNETO_SIM_ORDER]
        extra['rig_detents'] = [0.0, 1.0, 2.0, 3.0]
        note = (note + '; ' if note else '') + 'map re-ordered for the sim (0 OFF, 1 R, 2 L, 3 BOTH) onto the plate OFF / L / R / BOTH'
    pairs = sorted(zip((float(p) for p in ps), (float(v) for v in vs)))
    return dict(node=name, parent=parent, param=RENAME.get(param, param), unit=ex.get('rig_unit', ''),
                kind='move' if move else 'rot', axis_gltf=[float(a) for a in axis], pivot_gltf=origin,
                map=[[p, v] for p, v in pairs], model=float(ex.get('rig_param_model', 0.0)),
                desc=ex.get('rig_desc', ''), link=ex.get('rig_link', ''), note=note, extra=extra), None


def table(aircraft):
    glb = MODELS / aircraft / f'{aircraft}-own.glb'
    doc = read_glb(glb)
    origins = world_origins(doc)
    rows, static = [], []
    for nd in doc['nodes']:
        ex = nd.get('extras') or {}
        if 'rig_param' not in ex: continue
        r, why = row_for(nd['name'], ex, *origins[nd['name']])
        if r: rows.append(r)
        else: static.append(dict(node=nd['name'], param=ex['rig_param'], why=why))
    for r in rows:
        n = math.sqrt(sum(a * a for a in r['axis_gltf']))
        if abs(n - 1.0) > 1e-3: raise SystemExit(f"{aircraft} {r['node']}: axis not unit ({n})")
    return dict(model=f'{aircraft}-own', livery='talk-flying',
                source=f'unity/Assets/_Project/Resources/Models/{aircraft}/{aircraft}-own.glb node extras '
                       '(docs/models/j3-cub-own-RIG.md); written by tools/own_models/j3_rig_json.py',
                rig=rows, static=static, controls=controls(rows))


def controls(rows):
    """The CONTROLS rows whose node is rigged, with the rig row's axis and pivot."""
    by = {r['node']: r for r in rows}
    out = []
    for c in CONTROLS:
        r = by.get(c['node'])
        if r is None and c.get('optional'): continue         # e.g. the water rudder: floats only
        if r is None: raise SystemExit(f"control {c['node']} is not a rigged node")
        out.append(dict(node=c['node'], param=c['param'], kind=c['kind'], seat=c['seat'], axis_gltf=r['axis_gltf'],
                        pivot_gltf=r['pivot_gltf'], **{k: v for k, v in c.items() if k not in ('node', 'param', 'kind', 'seat', 'optional')}))
    return out


def main():
    check = '--check' in sys.argv
    bad = False
    for aircraft, name in CASES:
        out = ROOT / 'docs/models' / name
        text = json.dumps(table(aircraft), indent=1) + '\n'
        if check:
            same = out.exists() and out.read_text() == text
            print(f'{name}: {"in step with its GLB" if same else "OUT OF STEP - re-run without --check"}')
            bad |= not same
        else:
            out.write_text(text)
            t = json.loads(text)
            print(f"{name}: {len(t['rig'])} rigged nodes ({', '.join(sorted({r['param'] for r in t['rig']}))}); "
                  f"static: {', '.join(s['node'] for s in t['static'])}")
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
