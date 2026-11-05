"""Ty 2026-11-05: delete rear diagonals and contain the panel inside the dash bevels.
CPU pass after cockpit v3, all three liveries. No rig or flight geometry changes.
The existing glareshield's 11:9 slants continue down into the panel shoulders.
"""
import numpy as np
from vr_cockpit import Glb, MODELS, gl
from extra_panel_p3f import idx, slab
from extra_cockpit_v3 import world_tris
from vr_phase2e import compact
from drop_card_slot import validate


def remove_diagonals(g):
    d = g.doc
    assert not d.get('skins') and not d.get('animations'), 'node remapping needs skin/animation support'
    gone = {i for i, n in enumerate(d['nodes']) if n.get('name', '').startswith('Tube_P3h_PanelDiagonal_')}
    assert all(not d['nodes'][i].get('children') for i in gone)
    mapping = {old: new for new, old in enumerate(i for i in range(len(d['nodes'])) if i not in gone)}
    d['nodes'] = [n for i, n in enumerate(d['nodes']) if i not in gone]
    for n in d['nodes']:
        if 'children' in n: n['children'] = [mapping[i] for i in n['children'] if i not in gone]
    for s in d['scenes']: s['nodes'] = [mapping[i] for i in s['nodes'] if i not in gone]
    # Discard unreferenced meshes, including the tubes' geometry, then compact their bytes away.
    used = sorted({n['mesh'] for n in d['nodes'] if 'mesh' in n}); remap = {k: i for i, k in enumerate(used)}
    d['meshes'] = [d['meshes'][i] for i in used]
    for n in d['nodes']:
        if 'mesh' in n: n['mesh'] = remap[n['mesh']]
    # Remove tube-only materials; keep every material referenced by surviving geometry.
    used = sorted({p['material'] for m in d['meshes'] for p in m['primitives'] if 'material' in p})
    remap = {k: i for i, k in enumerate(used)}; d['materials'] = [d['materials'][i] for i in used]
    for m in d['meshes']:
        for p in m['primitives']:
            if 'material' in p: p['material'] = remap[p['material']]
    d.setdefault('extras', {}).pop('extraRearDiagonals', None)
    return len(gone)


def update(path):
    g = Glb(path)
    if g.doc.get('extras', {}).get('extraRemoveDiagonals'): return
    count = remove_diagonals(g)
    name = 'Panel_P3b_R'; n = g.doc['nodes'][idx(g, name)]
    v = world_tris(g, name).reshape(-1, 3)
    levels = []
    for z in sorted(set(np.round(v[:, 2], 6))):
        width = float(np.abs(v[np.abs(v[:, 2] - z) < 1e-5, 1]).max())
        # 2 mm inside the hood's diagonal, retaining the original lower side attachments.
        levels.append((float(z), min(width, .200 + (.428 - z) * (11 / 9) - .002)))
    sh = slab(-1.175, levels)
    w = g.world_matrices()[idx(g, name)]; inv = np.linalg.inv(w)
    pts = np.asarray([gl(p) for p in sh.v]); local = pts @ inv[:3, :3].T + inv[:3, 3]
    mat = g.doc['meshes'][n['mesh']]['primitives'][0]['material']
    g.mesh(name, local, sh.t, mat)
    for p in g.doc['extras']['vrCockpitPhase3b']['panels']:
        if p['seat'] == 'Rear': p['outline_zw'] = levels
    g.doc['extras']['extraRemoveDiagonals'] = dict(owner='Ty 2026-11-05: remove completely; panel inside dash angles',
        removed=count, rear_panel_outline_zw=levels, bevel_clearance_m=.002)
    compact(g); validate(g, path); g.save(path)
    print(path.name, 'removed', count, 'panel top halfwidth', levels[-1][1])


if __name__ == '__main__':
    for p in sorted((MODELS / 'extra-300-like').glob('*own*.glb')): update(p)
