"""Offline asset acceptance checks, including original mesh preservation against round-1 HEAD 38e8746.
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
from sequence_card import ROOT, PLACEMENTS, ART, WIDTH, HEIGHT, OUTLINES
from rear_panel import LAYOUTS

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
    assert sum(m.get('name')=='SequenceCard_Face' for m in d['materials'])==1
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
    assert np.allclose(xyz.max(0)-xyz.min(0),(0,HEIGHT,WIDTH),atol=1e-6)
    assert abs(WIDTH/HEIGHT-8.5/11)<1e-12
    assert np.allclose((WIDTH,HEIGHT),(.10795,.1397))
    assert spec['above']==spec['card_above_panel']==spec['holder_protrusion']==0
    assert spec['cockpit']=='rear'
    panel_x=-spec['panel_face_builder']
    hardware=[]
    for name in ('SequenceCard_Holder','SequenceCard_Clip','SequenceCard_Stock','SequenceCard_Face'):
        for p in d['meshes'][nodes[name]['mesh']]['primitives']:
            a=glb.array(p['attributes']['POSITION']);hardware.extend(a)
            assert np.all(a[:,0]>=panel_x-1e-6) and np.all(a[:,0]<=panel_x+.003001),'flush hardware > 3mm'
    hardware=np.array(hardware)
    assert np.allclose(xyz[:,0],panel_x+.0012,atol=1e-6),'card not flush to rear panel face'
    if path.parent.name in PLACEMENTS:
        model=path.parent.name
        # All actual holder/card vertices must be inside the convex rear slab outline.
        outline=np.array(OUTLINES[model]); points=np.column_stack((-hardware[:,2],hardware[:,1]))
        for a,b in zip(outline,np.roll(outline,-1,axis=0)):
            edge=b-a; delta=points-a
            assert np.all(edge[0]*delta[:,1]-edge[1]*delta[:,0]>=-1e-7),'card/holder outside rear panel'
        # Verify flush contact against the actual rear slab, not only a declared face coordinate.
        if model=='extra-300-like':
            assert spec['panel_tier']=='upper' and 'RearPanel_FlushPanel' not in nodes
            assert np.isclose(spec['centre_gltf'][1],.350)
            assert np.isclose(spec['lower_panel_face_builder'],-1.162)
        surface=[]
        for slabname in spec.get('mount_surface_nodes',['Cockpit']):
            slab=nodes[slabname]
            for prim in d['meshes'][slab['mesh']]['primitives']:
                positions=glb.array(prim['attributes']['POSITION'])
                ids=glb.array(prim['indices']).reshape(-1,3) if 'indices' in prim else np.arange(len(positions)).reshape(-1,3)
                tris=positions[ids]
                surface.extend(tris[np.all(abs(tris[:,:,0]-panel_x)<1e-6,axis=1)][:,:,[2,1]])
        surface=np.array(surface)
        for point in ((-WIDTH/2,spec['centre_gltf'][1]-HEIGHT/2),(WIDTH/2,spec['centre_gltf'][1]-HEIGHT/2),
                      (-WIDTH/2,spec['centre_gltf'][1]+HEIGHT/2),(WIDTH/2,spec['centre_gltf'][1]+HEIGHT/2)):
            inside=[]
            for tri in surface:
                edges=np.roll(tri,-1,axis=0)-tri;delta=np.array(point)-tri
                cross=edges[:,0]*delta[:,1]-edges[:,1]*delta[:,0]
                inside.append(np.all(cross>=-1e-8) or np.all(cross<=1e-8))
            assert any(inside),'flush card has no actual rear-panel surface behind it'
        assert not any(n.startswith('PanelCompass_') for n in nodes),'obsolete duplicate compass'
        assert sum(n=='RearPanel_Compass_Face' for n in nodes)==1
        # True rear-eye projection, plus panel-plane circle vs holder rectangle.
        evidence=spec['rear_eye']
        eye=np.linalg.solve(evidence['raw_model_to_body_matrix'],np.array(evidence['body_m'])-evidence['raw_model_to_body_translation_m'])
        assert np.allclose(eye,evidence['gltf_m'])
        assert eye[0]>panel_x and abs(eye[2])<.001,'rear eye must be aft, centred'
        project=lambda a: (a[:,[2,1]]-eye[[2,1]])/(eye[0]-a[:,0])[:,None]
        # Separating-axis check uses convex hulls of projected vertices (all geometry, not centres).
        def hull(a):
            points=sorted(set(map(tuple,a))); lower=[]; upper=[]
            cross=lambda o,a,b:(a[0]-o[0])*(b[1]-o[1])-(a[1]-o[1])*(b[0]-o[0])
            for p in points:
                while len(lower)>=2 and cross(lower[-2],lower[-1],p)<=0: lower.pop()
                lower.append(p)
            for p in reversed(points):
                while len(upper)>=2 and cross(upper[-2],upper[-1],p)<=0: upper.pop()
                upper.append(p)
            return np.array(lower[:-1]+upper[:-1])
        def separate(a,b):
            for poly in (a,b):
                edges=np.roll(poly,-1,axis=0)-poly
                for e in edges:
                    axis=np.array([-e[1],e[0]]);pa=a@axis;pb=b@axis
                    if pa.max()<pb.min() or pb.max()<pa.min(): return True
            return False
        cardproj=hull(project(hardware));cz=spec['centre_builder'][2]
        lo=np.array([-spec['holder_width']/2,cz-spec['holder_height']/2]);hi=np.array([spec['holder_width']/2,cz+spec['holder_height']/2])
        for name,u,v,diam in LAYOUTS[model]:
            nearest=np.clip([u,v],lo,hi)
            assert np.linalg.norm(np.array([u,v])-nearest)>diam/2+.001,'bezel intersects holder rectangle'
            for suffix in ('Bezel','Face'):
                mesh=d['meshes'][nodes['RearPanel_'+name+'_'+suffix]['mesh']]
                a=glb.array(mesh['primitives'][0]['attributes']['POSITION'])
                assert separate(cardproj,hull(project(a))),name+' occludes holder from rear eye'
        if model=='extra-300-like':
            for name,u,v,diam in LAYOUTS[model]:
                if v>.272: continue
                a=glb.array(d['meshes'][nodes['RearPanel_'+name+'_Face']['mesh']]['primitives'][0]['attributes']['POSITION'])
                assert np.allclose(a[:,0],1.173),'lower instruments must return to original recessed panel'
        # Full opaque scene ray test: 9x11 points across the printable card must be visible.
        # This catches glareshield/knob/placard occlusion that bezel-only tests cannot catch.
        triangles=[];world=glb.world_matrices()
        for index,matrix in world.items():
            node=d['nodes'][index]
            if 'mesh' not in node or node.get('name')=='SequenceCard_Face': continue
            for prim in d['meshes'][node['mesh']]['primitives']:
                if d['materials'][prim['material']].get('alphaMode')=='BLEND': continue
                points=glb.array(prim['attributes']['POSITION'])@matrix[:3,:3].T+matrix[:3,3]
                ids=glb.array(prim['indices']).reshape(-1,3) if 'indices' in prim else np.arange(len(points)).reshape(-1,3)
                triangles.extend(points[ids])
        tri=np.array(triangles);e1=tri[:,1]-tri[:,0];e2=tri[:,2]-tri[:,0];origin=eye-tri[:,0]
        q=np.cross(origin,e1)
        for u in np.linspace(-WIDTH*.47,WIDTH*.47,9):
            for v in np.linspace(cz-HEIGHT*.47,cz+HEIGHT*.47,11):
                destination=np.array([panel_x+.0012,v,-u]);direction=destination-eye
                h=np.cross(direction,e2);det=(e1*h).sum(1)
                inv=np.divide(1,det,out=np.zeros_like(det),where=abs(det)>1e-10)
                bu=(origin*h).sum(1)*inv;bv=(direction*q).sum(1)*inv;t=(e2*q).sum(1)*inv
                hit=(abs(det)>1e-10)&(bu>=0)&(bv>=0)&(bu+bv<=1)&(t>1e-6)&(t<1-1e-5)
                assert not hit.any(),f'{path.name}: card occluded at u={u},v={v}'
        # Every pre-existing vertex/index buffer and node survives the incremental update byte-for-byte.
        rel=str(path.relative_to(ROOT));baseline=subprocess.check_output(['git','show','38e8746:'+rel],cwd=ROOT)
        with tempfile.TemporaryDirectory(prefix='iac-check-') as td:
            oldpath=Path(td)/'base.glb';oldpath.write_bytes(baseline);old=Glb(oldpath)
        size=old.doc['buffers'][0]['byteLength']
        assert glb.bin[:size]==old.bin[:size],rel+' original buffer changed'
        original_nodes=[n for n in old.doc['nodes'] if not n['name'].startswith(('SequenceCard','PanelCompass'))]
        assert d['nodes'][:len(original_nodes)]==original_nodes
        for node in original_nodes:
            if 'mesh' not in node: continue
            oldmesh=old.doc['meshes'][node['mesh']];newmesh=d['meshes'][node['mesh']]
            for a,b in zip(oldmesh['primitives'],newmesh['primitives']):
                assert a['attributes']['POSITION']==b['attributes']['POSITION']
                if node['name']=='Cockpit':
                    assert {k:v for k,v in a.items() if k!='indices'}=={k:v for k,v in b.items() if k!='indices'}
                    oldids=old.array(a['indices']).reshape(-1,3);newids=glb.array(b['indices']).reshape(-1,3)
                    positions=old.array(a['attributes']['POSITION'])
                    # Front-panel / seat / control triangles outside the rear dial station survive exactly.
                    front=positions[oldids][:,:,0].max(1)<panel_x-.10
                    preserved={tuple(t) for t in newids}
                    assert all(tuple(t) in preserved for t in oldids[front]),'front geometry changed'
                    assert preserved.issubset({tuple(t) for t in oldids}),'original cockpit triangles unexpectedly added'
                else: assert a==b,'airframe/paint changed'
        assert path.with_suffix('.glb.meta').exists()

    print('PASS',path.relative_to(ROOT))
print('PASS: 9 GLBs; half-letter, flush rear-panel fit, zero protrusion, bezel clearance and 99 full-scene visibility rays per aircraft, front panels/airframe/paint preserved.')
