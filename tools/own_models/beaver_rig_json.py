"""The Beaver's rig table (docs/models/dhc2-beaver-own-rig.json), read out of its own GLB.

The GLB (unity/Assets/_Project/Resources/Models/dhc2-beaver-like/dhc2-beaver-like-own.glb) is the beaver-v2 reference
airframe (flying-game-reference/out/beaver-v2, our own geometry traced from the public-domain USAF L-20A three-view),
rigged by tools/own_models/rig_beaver_reference.py (Codex). Its hinged nodes carry the J-3 v7 rig extras, so this
reuses tools/own_models/j3_rig_json.py's reader. Params the sim does not supply stay as drawn ("static").

  python3 -I tools/own_models/beaver_rig_json.py          # writes the table
  python3 -I tools/own_models/beaver_rig_json.py --check  # exits 1 if out of step with the GLB
"""
import json, math, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import j3_rig_json as j

j.RENAME = dict(j.RENAME, elevator_trim='trim')
j.STATIC = dict(j.STATIC, flaps='no flap-angle input in ModelRig yet; flaps drawn up')
AIRCRAFT, NAME = 'dhc2-beaver-like', 'dhc2-beaver-own-rig.json'


def table():
    doc = j.read_glb(j.MODELS / AIRCRAFT / f'{AIRCRAFT}-own.glb')
    origins = j.world_origins(doc)
    rows, static = [], []
    for nd in doc['nodes']:
        ex = nd.get('extras') or {}
        if 'rig_param' not in ex: continue
        r, why = j.row_for(nd['name'], ex, *origins[nd['name']])
        if r: rows.append(r)
        else: static.append(dict(node=nd['name'], param=ex['rig_param'], why=why))
    for r in rows:
        if abs(math.sqrt(sum(a * a for a in r['axis_gltf'])) - 1) > 1e-3: raise SystemExit(f"{r['node']}: axis not unit")
    return dict(model=f'{AIRCRAFT}-own', livery='talk-flying',
                source=f'unity/Assets/_Project/Resources/Models/{AIRCRAFT}/{AIRCRAFT}-own.glb node extras; written by tools/own_models/beaver_rig_json.py',
                rig=rows, static=static, controls=[])


out = j.ROOT / 'docs/models' / NAME
text = json.dumps(table(), indent=1) + '\n'
if '--check' in sys.argv:
    ok = out.exists() and out.read_text() == text
    print(NAME, 'in step' if ok else 'OUT OF STEP'); sys.exit(0 if ok else 1)
out.write_text(text); t = json.loads(text)
print(NAME, len(t['rig']), 'rigged:', ', '.join(r['node'] + '/' + r['param'] for r in t['rig']), '| static:', ', '.join(s['node'] for s in t['static']))
