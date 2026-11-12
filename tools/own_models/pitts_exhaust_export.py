"""Replace ONLY the Exhaust and Engine meshes in the committed Pitts S-2B GLBs.

Why this exists.  The four committed Pitts GLBs are not the plain output of `build_pitts_s2b.py` any more: the cockpit
v2 pass, the sequence card, the sights, the rear panel and the VR hardware were added by offline updaters afterwards,
so a full rebuild from the builder would DROP 85 nodes
(docs/models/pitts-s2b-own-PROVENANCE.md, 2026-11-03: "a full rebuild from this script would drop them").  The way to
change one part of the model is therefore the `build_ea300.py --wing-only` way: build to a scratch GLB, then lift just
the changed meshes into each committed file, keeping its own materials by NAME.

Usage (one Blender process per model - this repo's GLBs only reproduce when built one at a time):

    PITTS_LIVERY=N31DG PITTS_OUT=/tmp/pitts-scratch.glb blender -b --python tools/own_models/build_pitts_s2b.py
    python3 tools/own_models/pitts_exhaust_export.py /tmp/pitts-scratch.glb

With no target given it patches all four committed liveries.  That is correct for these two meshes because neither one
is livery-dependent: the exhaust is one flat steel material and the engine's five are flat colours, so the scratch
build's Engine and Exhaust are the SAME geometry in every livery (the propeller is what differs).  It is checked, not
assumed: this script refuses a target whose Exhaust or Engine geometry is not the one the previous builder produced
unless --force is given, so it cannot quietly paper over a livery that had drifted.
"""
import sys, os, hashlib
from glb_visuals import Glb

NAMES = ('Exhaust', 'Engine')
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
MODELS = os.path.join(ROOT, 'unity/Assets/_Project/Resources/Models/pitts-s2b-like')
LIVERIES = ('pitts-s2b-like-own.glb', 'pitts-s2b-like-own-n1191.glb',
            'pitts-s2b-like-own-checker-unmarked.glb', 'pitts-s2b-like-own-factory-unmarked.glb')


def fingerprint(glb, name):
    """sha1 of a node's mesh POSITION data, so a target can be checked before it is written to."""
    node = next(n for n in glb.doc['nodes'] if n.get('name') == name)
    h = hashlib.sha1()
    for p in glb.doc['meshes'][node['mesh']]['primitives']:
        h.update(glb.array(p['attributes']['POSITION']).tobytes())
    return h.hexdigest()


def replace_exhaust(baseline, generated, output, names=NAMES, expect=None):
    old, new = Glb(baseline), Glb(generated)
    materials = {m['name']: i for i, m in enumerate(old.doc['materials'])}
    nodes = {n['name']: n for n in old.doc['nodes'] if 'name' in n}
    for name in names:
        src = next(n for n in new.doc['nodes'] if n.get('name') == name)
        dst = nodes[name]
        for key in ('translation', 'rotation', 'scale', 'matrix'):
            assert dst.get(key) == src.get(key), (name, key, 'node transform changed')
        if expect is not None:
            was = fingerprint(old, name)
            assert was == expect[name], (baseline, name, 'geometry is not the one the builder last produced: ' + was)
        prims = []
        for p in new.doc['meshes'][src['mesh']]['primitives']:
            attrs = {k: old.accessor(new.array(v), new.doc['accessors'][v]['type'], new.doc['accessors'][v]['componentType'])
                     for k, v in p['attributes'].items()}
            ix = p['indices']; acc = new.doc['accessors'][ix]
            prims.append(dict(attributes=attrs,
                              indices=old.accessor(new.array(ix), acc['type'], acc['componentType']),
                              material=materials[new.doc['materials'][p['material']]['name']]))
        old.doc['meshes'][dst['mesh']]['primitives'] = prims
    old.save(output)
    return {name: fingerprint(Glb(output), name) for name in names}


if __name__ == '__main__':
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    force = '--force' in sys.argv
    generated = args[0]
    targets = args[1:] or [os.path.join(MODELS, f) for f in LIVERIES]
    # What the PREVIOUS builder produced for these two meshes, as a guard that every livery is starting from the
    # same place.  Taken from the first target, so it also catches a half-patched set.
    expect = None if force else {n: fingerprint(Glb(targets[0]), n) for n in NAMES}
    for t in targets:
        before = os.path.getsize(t)
        got = replace_exhaust(t, generated, t, expect=expect)
        print('PATCHED %-44s %8d -> %8d bytes  %s' % (os.path.basename(t), before, os.path.getsize(t),
                                                      ' '.join('%s=%s' % (k, v[:12]) for k, v in got.items())))
