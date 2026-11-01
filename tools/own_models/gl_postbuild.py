"""The two deterministic steps that follow every Great Lakes Blender build.

Both were done by hand each time the model was rebuilt, which is how a procedure turns into folklore — one of
them is a GLB edit, and a GLB edit nobody can repeat is worse than no GLB edit. They are here so the next
rebuild is one command and so the reason each exists is written down next to it.

  1. THE PROP AND MIXTURE LEVERS ARE PINNED FULL FORWARD. gl_rig.py emits their honest 0..1 -> 0..50 deg map,
     but nothing in the sim drives a vernier: tying mixture to the engine state snapped the lever aft the moment
     the engine stopped (lane/claude-gl-eye, 2026-11-01). A constant map holds both levers where a pilot leaves
     them. The THROTTLE keeps its real map.
  2. THE SEQUENCE CARD SITS 24 mm PROUD OF THE PANEL, down and left of the gauges. The shared card placement
     helper puts it flush on the panel, where 8 of 25 sample points were hidden behind the instruments from the
     rear eye; moved here it is 25 of 25 (lane/claude-gl-eye). The placement is shared with the other types, so
     it is corrected on this model rather than changed for everyone.

  PYTHONDONTWRITEBYTECODE=1 /usr/bin/python3 tools/own_models/gl_postbuild.py
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GLB = ROOT / 'unity/Assets/_Project/Resources/Models/great-lakes-like/great-lakes-like-own.glb'
RIGJSON = ROOT / 'docs/models/great-lakes-own-rig.json'
MIRROR = ROOT / 'unity/Assets/StreamingAssets/rigs/great-lakes-like-own.json'

PINNED = ('Prop_F', 'Prop_R', 'Mixture_F', 'Mixture_R')
PIN_MAP = [[0.0, 50.0], [1.0, 50.0]]
CARD_NODES = ('SequenceCard_Clip', 'SequenceCard_Face', 'SequenceCard_Holder', 'SequenceCard_Stock')
CARD_XYZ = [0.024, -0.13, 0.17]

sys.path.insert(0, str(Path(__file__).resolve().parent))
from glb_visuals import Glb


def pin_levers():
    rig = json.loads(RIGJSON.read_text())
    hit = 0
    for entry in rig['rig']:
        if entry['node'] in PINNED:
            assert entry['map'] == [[0.0, 0.0], [1.0, 50.0]], (entry['node'], 'unexpected map', entry['map'])
            entry['map'] = [list(p) for p in PIN_MAP]
            hit += 1
    assert hit == len(PINNED), ('pinned %d of %d levers' % (hit, len(PINNED)))
    text = json.dumps(rig, indent=2) + '\n'
    RIGJSON.write_text(text)
    if MIRROR.exists():
        MIRROR.write_text(text)
    print('PINNED  %s -> %s' % (', '.join(PINNED), PIN_MAP))


def move_card():
    glb = Glb(GLB)
    doc = glb.doc
    hit = 0
    for node in doc['nodes']:
        if node.get('name') in CARD_NODES:
            node['translation'] = list(CARD_XYZ)
            hit += 1
    assert hit == len(CARD_NODES), ('moved %d of %d card nodes' % (hit, len(CARD_NODES)))
    glb.save(GLB)
    print('CARD    %s -> %s' % (', '.join(CARD_NODES), CARD_XYZ))


if __name__ == '__main__':
    pin_levers()
    move_card()
