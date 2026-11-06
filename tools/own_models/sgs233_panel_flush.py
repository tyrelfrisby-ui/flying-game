"""Lower only the 2-33 panel assembly; preserve all later shipping-GLB additions.

python sgs233_panel_flush.py --update   # re-export both yellow shipping variants
python sgs233_panel_flush.py            # assert exported crown is 20 mm below the skin
Coordinates: glTF nose -X, up +Y, span Z. No eye/config/skin changes.
"""
import json
import struct
import sys
from pathlib import Path
import numpy as np
from glb_visuals import Glb

# Same PPX=280.5 station, K=.01527 and fus() interpolation as build_sgs233.
SKIN_Z = 0.47 + (197 - (225.8 + (225 - 225.8) * 1.5 / 5)) * 0.01527
# Q118 follow-up: lower the whole assembly a further 20 mm below flush.
BELOW_SKIN_M = 0.020
TARGET_Z = SKIN_Z - BELOW_SKIN_M
ASSEMBLY = {'Panel_Face', 'Panel_Front', 'Panel_Coaming', 'Panel_Front_Bezels',
            'Panel_Vent', 'Compass_Housing', 'Compass_Card', 'TowRelease_F',
            'Dial_ALT_F', 'Dial_ASI_F', 'Dial_VARIO_F', 'Needle_ASI_F',
            'Needle_Alt100_F', 'Needle_Alt1000_F', 'Needle_Vario_F'}
MODELS = Path(__file__).resolve().parents[2] / 'unity/Assets/_Project/Resources/Models/glider-2-33-like'


def crown(g):
    w = g.world_matrices()
    i = next(i for i, n in enumerate(g.doc['nodes']) if n['name'] == 'Panel_Coaming')
    return max((g.array(p['attributes']['POSITION']) @ w[i][:3, :3].T + w[i][:3, 3])[:, 1].max()
               for p in g.doc['meshes'][g.doc['nodes'][i]['mesh']]['primitives'])


def update(path):
    g = Glb(path)
    before = crown(g); delta = TARGET_Z - before
    w = g.world_matrices()
    parents = {c: i for i, n in enumerate(g.doc['nodes']) for c in n.get('children', [])}
    found = {n['name'] for n in g.doc['nodes']} & ASSEMBLY
    assert found == ASSEMBLY, ASSEMBLY - found
    for i, n in enumerate(g.doc['nodes']):
        if n['name'] not in ASSEMBLY: continue
        assert not any(g.doc['nodes'][c]['name'] in ASSEMBLY for c in n.get('children', []))
        parent = w[parents[i]] if i in parents else np.eye(4)
        d = np.linalg.solve(parent[:3, :3], [0, delta, 0])
        if 'matrix' in n:
            m = np.array(n['matrix']).reshape(4, 4).T.copy(); m[:3, 3] += d
            n['matrix'] = m.T.reshape(-1).tolist()
        else: n['translation'] = (np.array(n.get('translation', [0, 0, 0])) + d).tolist()
    assert abs(crown(g) - TARGET_Z) < 1e-7
    # Repack JSON only: buffers, textures, meshes, rig maps and unrelated nodes stay intact.
    js = json.dumps(g.doc, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4)
    buf = bytes(g.bin); buf += b'\0' * (-len(buf) % 4)
    path.write_bytes(struct.pack('<4sII', b'glTF', 2, 28 + len(js) + len(buf)) +
                     struct.pack('<I4s', len(js), b'JSON') + js +
                     struct.pack('<I4s', len(buf), b'BIN\0') + buf)
    print(f'{path.name}: crown {before:.8f} -> {crown(g):.8f}; lowered {-delta * 1000:.3f} mm')


if __name__ == '__main__':
    for name in ('glider-2-33-like-own.glb', 'glider-2-33-like-own-tf.glb'):
        p = MODELS / name
        if '--update' in sys.argv: update(p)
        z = crown(Glb(p))
        assert abs(z - TARGET_Z) < 1e-6, (name, z, TARGET_Z)
        print(f'PANEL FLUSH PASS {name}: crown {z:.8f} m; below skin {BELOW_SKIN_M * 1000:.3f} mm')
