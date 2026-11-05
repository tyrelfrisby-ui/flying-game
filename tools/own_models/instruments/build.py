"""Build the shared instrument library: one GLB per instrument, one ASI face per aircraft that declares one.

    /usr/bin/python3 tools/own_models/instruments/build.py [--review]

Writes
  unity/Assets/_Project/Resources/Models/instruments/<instrument>.glb     the instrument (face radius 1, see __init__)
  unity/Assets/_Project/Resources/InstrumentFaces/airspeed-<aircraft>.png the aeroplane's own ASI markings
  docs/models/instrument-library.json                                     the needle maps, for reading
  --review: renders/shared-instruments/<instrument>.png (CPU, unlit) close-ups of each GLB at a reading
"""
import argparse
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
ROOT = HERE.parents[2]
OUT = ROOT / 'unity/Assets/_Project/Resources/Models/instruments'
FACES = ROOT / 'unity/Assets/_Project/Resources/InstrumentFaces'

from instruments import library as L  # noqa: E402


def aircraft_asi():
    """Every aircraft config with an instruments.airspeed block."""
    out = {}
    for p in sorted((ROOT / 'configs/aircraft').glob('*.json')):
        spec = json.loads(p.read_text()).get('instruments', {}).get('airspeed')
        if spec: out[p.stem] = spec
    return out


def aircraft_gmeter():
    """Every aircraft config with instruments.gMeter: its red lines are its structural limits (limits.gMax / gMin)."""
    out = {}
    for p in sorted((ROOT / 'configs/aircraft').glob('*.json')):
        d = json.loads(p.read_text())
        if 'gMeter' in d.get('instruments', {}): out[p.stem] = dict(gMax=d['limits']['gMax'], gMin=d['limits']['gMin'])
    return out


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('--review', action='store_true'); args = ap.parse_args()
    OUT.mkdir(parents=True, exist_ok=True); FACES.mkdir(parents=True, exist_ok=True)
    for name, make in L.INSTRUMENTS.items():
        w = make()
        path = OUT / (name + '.glb'); w.save(path)
        print('wrote', path.relative_to(ROOT))
    asi = aircraft_asi()
    for a, spec in asi.items():
        im = L.asi_face(spec)
        p = FACES / ('airspeed-%s.png' % a); im.save(p)
        print('wrote', p.relative_to(ROOT), spec['units'], spec['scaleMin'], spec['scaleMax'])
    for a, lim in aircraft_gmeter().items():
        p = FACES / ('g-meter-%s.png' % a); L.gm_face(lim['gMax'], lim['gMin']).save(p)
        print('wrote', p.relative_to(ROOT), 'red lines', lim['gMin'], lim['gMax'])
    doc = dict(note='The shared instrument library (tools/own_models/instruments). Maps: degrees clockwise as the pilot '
                    'sees the dial (rot) or face radii along the face up (slide). Copied by hand into '
                    'FlyingGame.Core.Instruments.InstrumentLibrary; InstrumentLibraryTests holds both to literals.',
               instruments={k: v for k, v in L.PARTS.items()},
               airspeed={a: dict(spec, param=L.asi_param(spec['units']), map=L.asi_law(spec['scaleMin'], spec['scaleMax']))
                         for a, spec in asi.items()})
    (ROOT / 'docs/models/instrument-library.json').write_text(json.dumps(doc, indent=1) + '\n')
    print('wrote docs/models/instrument-library.json')
    if args.review: review()


def review():
    """Close-ups straight from each GLB, with the moving parts posed at a reading (CPU, unlit)."""
    import numpy as np
    from glb_visuals import Glb
    from render_instrument_review import render
    out = ROOT / 'renders/shared-instruments'; out.mkdir(parents=True, exist_ok=True)
    pose = {'GM_Needle': -90 + 12 * 4.2, 'GM_Max': -90 + 12 * 6.1, 'GM_Min': -90 - 12 * 2.3, 'TC_Airplane': 20.0, 'TC_Ball': -9.0, 'ASI_Needle': 150.0, 'ALT_Hand100': 0.35 * 360, 'ALT_Hand1000': 36 * 4.35,
            'ALT_Hand10000': 36 * 0.435, 'VSI_Needle': -45.0, 'AI_Roll': -20.0, 'DG_Card': -30.0}
    for name in L.INSTRUMENTS:
        g = Glb(OUT / (name + '.glb'))
        for n in g.doc['nodes']:
            deg = pose.get(n.get('name'))
            if deg is None: continue
            # clockwise as the pilot (at +z) sees it = a NEGATIVE turn about +z
            t = -np.radians(deg) / 2
            n['rotation'] = [0.0, 0.0, float(np.sin(t)), float(np.cos(t))]
            if n.get('name') == 'AI_Roll':
                for c in n.get('children', []):
                    if g.doc['nodes'][c]['name'] == 'AI_Pitch': g.doc['nodes'][c]['translation'] = [0.0, -0.15, 0.0]
        import tempfile
        with tempfile.TemporaryDirectory() as td:
            tmp = Path(td) / (name + '.glb'); _save(g, tmp)
            render(tmp, [0.0, 0.0, 2.4], [0.0, 0.0, 0.0], out / (name + '.png'))
        print('rendered', (out / (name + '.png')).relative_to(ROOT))


def _save(g, path):
    import struct
    g.doc['buffers'][0]['byteLength'] = len(g.bin)
    js = json.dumps(g.doc, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4)
    buf = bytes(g.bin); buf += b'\0' * (-len(buf) % 4)
    Path(path).write_bytes(struct.pack('<4sII', b'glTF', 2, 28 + len(js) + len(buf)) + struct.pack('<I4s', len(js), b'JSON') + js
                           + struct.pack('<I4s', len(buf), b'BIN\0') + buf)


if __name__ == '__main__':
    main()
