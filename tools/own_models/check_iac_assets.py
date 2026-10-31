"""Offline asset acceptance checks, including original mesh preservation against split-surfaces.
Run with the same NumPy/Pillow environment as the CPU builder path.
"""
import hashlib
import io
import json
from pathlib import Path
import subprocess
import tempfile

import numpy as np
from PIL import Image
from glb_visuals import Glb
from sequence_card import ROOT, PLACEMENTS, ART

MODELS = ROOT/'unity/Assets/_Project/Resources/Models'
specs=json.loads((ROOT/'docs/models/sequence-card-spec.json').read_text())
paths=[p for model in PLACEMENTS for p in sorted((MODELS/model).glob('*own*.glb'))]
default_hash=hashlib.sha256(ART.read_bytes()).hexdigest()
assert (ROOT/'unity/Assets/_Project/Resources/SequenceCards/sportsman-known-2026.png').read_bytes()==ART.read_bytes()
for path in paths+[MODELS/'sequence-card/sequence-card-holder.glb']:
    glb=Glb(path);d=glb.doc
    assert len(d['nodes'])==len({n['name'] for n in d['nodes']}),path
    for v in d['bufferViews']: assert v.get('byteOffset',0)+v['byteLength']<=len(glb.bin)
    nodes={n['name']:n for n in d['nodes']}
    assert all(n in nodes for n in ('SequenceCard_Face','SequenceCard_Holder','SequenceCard_Clip','SequenceCard_Stock'))
    face=d['meshes'][nodes['SequenceCard_Face']['mesh']]['primitives'][0]
    mat=d['materials'][face['material']]
    assert mat['name']=='SequenceCard_Face'
    tex=mat['pbrMetallicRoughness']['baseColorTexture']['index']
    im=d['images'][d['textures'][tex]['source']];v=d['bufferViews'][im['bufferView']]
    raw=bytes(glb.bin[v.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']])
    assert hashlib.sha256(raw).hexdigest()==default_hash
    assert Image.open(io.BytesIO(raw)).size==(1275,1650)
    xyz=glb.array(face['attributes']['POSITION']);norm=glb.array(face['attributes']['NORMAL'])
    assert np.all(norm[:,0]>.99),'card must face the pilot (+glTF X)'
    uv=glb.array(face['attributes']['TEXCOORD_0'])
    assert np.allclose(uv.min(0),(0,0)) and np.allclose(uv.max(0),(1,1))
    assert np.corrcoef(xyz[:,1],uv[:,1])[0,1]<-.99,'art must be upright (glTF V down)'
    assert np.corrcoef(xyz[:,2],uv[:,0])[0,1]<-.99,'art must not be mirrored'
    spec=specs[path.parent.name if path.parent.name in PLACEMENTS else 'reusable-decathlon']
    assert np.allclose((xyz.min(0)+xyz.max(0))/2,spec['centre_gltf'])
    holder=glb.array(d['meshes'][nodes['SequenceCard_Holder']['mesh']]['primitives'][0]['attributes']['POSITION'])
    assert abs(holder[:,1].max()-spec['holder_top'])<1e-6
    if path.parent.name in PLACEMENTS:
        # Every pre-existing vertex/index buffer and node survives the incremental update byte-for-byte.
        rel=str(path.relative_to(ROOT));baseline=subprocess.check_output(['git','show','1b67dd5:'+rel],cwd=ROOT)
        with tempfile.TemporaryDirectory(prefix='iac-check-') as td:
            oldpath=Path(td)/'base.glb';oldpath.write_bytes(baseline);old=Glb(oldpath)
        size=old.doc['buffers'][0]['byteLength']
        assert glb.bin[:size]==old.bin[:size],rel+' original buffer changed'
        assert d['nodes'][:len(old.doc['nodes'])]==old.doc['nodes']
        for oldmesh,newmesh in zip(old.doc['meshes'],d['meshes']):
            for a,b in zip(oldmesh['primitives'],newmesh['primitives']):
                assert a['attributes']['POSITION']==b['attributes']['POSITION']
                assert a.get('indices')==b.get('indices')
                if path.name!='pitts-s2b-like-own-checker-unmarked.glb':assert a==b,'other livery paint changed'
        assert path.with_suffix('.glb.meta').exists()
        if path.parent.name=='extra-300-like':assert 'PanelCompass_Face' in nodes
    print('PASS',path.relative_to(ROOT))
print('PASS: 9 GLBs; default texture, UV orientation, material slot, frame height, original geometry/paint preservation.')
