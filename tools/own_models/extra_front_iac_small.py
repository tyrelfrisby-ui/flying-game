"""Q070, Ty 2026-11-05: compact front IAC card and backing, right of the normal subpanel.
Run after extra_cockpit_v3 / extra_remove_diagonals. Rear card and panel silhouettes stay unchanged.
"""
import copy
import json
import numpy as np
from vr_cockpit import Glb, MODELS, ROOT
from vr_phase2e import compact
from drop_card_slot import validate

SCALE = .55
OLD = np.array((.150, .275, -.190))  # glTF: forward plane, height, lateral; scale about old plate centre
NEW = np.array((.150, .300, -.190))


def apply(g):
    if g.doc.get('extras', {}).get('extraFrontIacSmall'): return False
    W = g.world_matrices(); report = {}
    for i, n in enumerate(g.doc['nodes']):
        if not n.get('name', '').startswith('FrontPanel_SequenceCard_') or 'mesh' not in n: continue
        mesh = copy.deepcopy(g.doc['meshes'][n['mesh']]); mesh['name'] = n['name']
        # Front parts share the rear card meshes. Clone, never mutate the rear's accessors.
        n['mesh'] = len(g.doc['meshes']); g.doc['meshes'].append(mesh)
        inv = np.linalg.inv(W[i]); all_pts = []
        for p in mesh['primitives']:
            v = np.asarray(g.array(p['attributes']['POSITION']), float)
            world = v @ W[i][:3, :3].T + W[i][:3, 3]
            world = (world - OLD) * np.array((1, SCALE, SCALE)) + NEW
            local = world @ inv[:3, :3].T + inv[:3, 3]
            p['attributes']['POSITION'] = g.accessor(local.astype(np.float32), 'VEC3')
            if 'NORMAL' in p['attributes']:
                lin = inv[:3, :3] @ np.diag((1, SCALE, SCALE)) @ W[i][:3, :3]
                norm = np.asarray(g.array(p['attributes']['NORMAL']), float) @ np.linalg.inv(lin)
                norm /= np.linalg.norm(norm, axis=1)[:, None]
                p['attributes']['NORMAL'] = g.accessor(norm.astype(np.float32), 'VEC3')
            all_pts.extend(world)
        a = np.array(all_pts); report[n['name']] = dict(min=a.min(0).round(6).tolist(), max=a.max(0).round(6).tolist())
    g.doc['extras']['extraFrontIacSmall'] = dict(owner='Ty Q070 2026-11-05: small front card and plate, right of normal subpanel',
        scale=SCALE, report=report)
    prune_meshes(g)
    return True


def prune_meshes(g):
    used = sorted({n['mesh'] for n in g.doc['nodes'] if 'mesh' in n})
    if len(used) == len(g.doc['meshes']): return False
    remap = {old: new for new, old in enumerate(used)}
    g.doc['meshes'] = [g.doc['meshes'][i] for i in used]
    for n in g.doc['nodes']:
        if 'mesh' in n: n['mesh'] = remap[n['mesh']]
    return True


def update(path):
    g = Glb(path)
    changed = apply(g)
    if prune_meshes(g) or changed:
        compact(g); validate(g, path); g.save(path)
        print(path.name, 'front IAC paper 59.4 x 76.8 mm; backing 79.2 x 91.3 mm, z 254..346 mm')


def update_rig():
    path = ROOT / 'docs/models/ea300-own-rig.json'; d = json.loads(path.read_text())
    row = next(s for s in d['card_slots'] if s['node'] == 'FrontPanel_SequenceCard_Face')
    row['centre_gltf'] = [.1525, .300, -.19275]
    row['size_m'] = [.0593725, .076835]
    row['note'] = 'Ty Q070 2026-11-05: compact card + backing right of front subpanel; same rear selection'
    path.write_text(json.dumps(d, indent=1) + '\n')


if __name__ == '__main__':
    for p in sorted((MODELS / 'extra-300-like').glob('*own*.glb')): update(p)
    update_rig()
