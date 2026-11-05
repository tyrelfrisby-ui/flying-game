"""Offline regression checks. No Unity, dotnet or Blender invocation."""
import ast, copy, hashlib, io, json, subprocess, tempfile
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from vr_cockpit import ROOT, MODELS, Glb
from drop_card_slot import validate
from instrument_face_style import apply_glb, finish, is_face

def main():
    for path in (ROOT/'tools/own_models').glob('*.py'):
        ast.parse(path.read_text(), filename=str(path))
    changed = subprocess.check_output(['git','diff','--name-only'],cwd=ROOT,text=True).splitlines()
    total = colors = 0
    samples=[]
    with tempfile.TemporaryDirectory() as td:
        for name in changed:
            if not name.endswith('.glb'):continue
            path=ROOT/name;g=Glb(path);validate(g,path)
            # The common paint pass must never accumulate stroke or buffer bytes.
            before=hashlib.sha256(bytes(g.bin)+json.dumps(g.doc,sort_keys=True).encode()).hexdigest()
            assert apply_glb(g, cream=path.parent.name.startswith(('j3-','pa18-')))==0,path
            after=hashlib.sha256(bytes(g.bin)+json.dumps(g.doc,sort_keys=True).encode()).hexdigest()
            assert before==after,path
            original=Path(td)/path.name
            original.write_bytes(subprocess.check_output(['git','show','HEAD:'+name],cwd=ROOT))
            old=Glb(original);oldm={m.get('name'):m for m in old.doc['materials']}
            for m in g.doc['materials']:
                title=m.get('name','')
                if not is_face(title) or title not in oldm:continue
                ti=m.get('pbrMetallicRoughness',{}).get('baseColorTexture',{}).get('index')
                oi=oldm[title].get('pbrMetallicRoughness',{}).get('baseColorTexture',{}).get('index')
                if ti is None or oi is None:continue
                a=np.array(old.image(oi));b=np.array(g.image(ti));assert a.shape==b.shape
                assert np.array_equal(np.array(old.image(oi, mode="RGBA"))[:,:,3], np.array(g.image(ti, mode="RGBA"))[:,:,3]), (path,title,"alpha changed")
                saturated=a.max(2).astype(int)-a.min(2).astype(int)>=80
                assert np.array_equal(a[saturated],b[saturated]),(path,title,'coloured markings moved')
                colors+=int(saturated.sum());total+=1
                if title in ('P3b_Dial_ASI','Dial_ASI','Panel_gauge_faces') and not any(s[0]==path.parent.name for s in samples):
                    samples.append((path.parent.name,old.image(oi),g.image(ti)))
            if path.parent.name=='pitts-s2b-like':
                nodes={n['name']:n for n in g.doc['nodes']};controls=g.doc['extras']['vrCockpitPhase3a']['controls']
                assert not any(c['seat']=='Front' for c in controls)
                assert {c['node'] for c in controls}=={'Ctl_Toggle_Master','Ctl_Toggle_Alternator','Ctl_Toggle_FuelPump','Ctl_Toggle_Smoke','PropLever_Rear','Mixture','FuelValve_Rear','TrimLever_Rear','CowlFlap'}  # pitts_controls_live.py
                for key in ['Airspeed','Altimeter','G_meter','Tachometer']:
                    face=nodes['FrontPanel_'+key+'_Face'];bezel=nodes['FrontPanel_'+key+'_Bezel']
                    fv=g.array(g.doc['meshes'][face['mesh']]['primitives'][0]['attributes']['POSITION'])
                    bv=g.array(g.doc['meshes'][bezel['mesh']]['primitives'][0]['attributes']['POSITION'])
                    assert fv[:,0].min()>bv[:,0].max(),key+' face behind bezel'
                assert 'PropLever_Front' in nodes and 'Throttle_Front' in nodes
                # Every retained existing rig calibration is byte-for-byte data equal.
                rig=json.loads((ROOT/'docs/models/pitts-s2b-own-rig.json').read_text())
                base=json.loads(subprocess.check_output(['git','show','HEAD:docs/models/pitts-s2b-own-rig.json'],cwd=ROOT))
                maps={r['node']:r for r in base['rig']}
                for row in rig['rig']:
                    if row['node'] in maps:assert row==maps[row['node']],row['node']
    # Visual source-art comparison; Cub first cell is a complete cream dial.
    out=ROOT/'renders/instrument-style';out.mkdir(parents=True,exist_ok=True)
    if not samples:
        print('PASS: Python syntax, GLB graph, paint, texture preservation, Pitts face clearance and retained rig maps; no comparison-sheet samples in changed assets.')
        return
    sheet=Image.new('RGB',(1000,300*len(samples)),(75,75,75));draw=ImageDraw.Draw(sheet)
    for i,(title,a,b) in enumerate(samples):
        draw.text((10,i*300+10),title+'   original / shared style',fill='white')
        if title.startswith('j3-'):a=a.crop((0,0,512,512));b=b.crop((0,0,512,512))
        sheet.paste(a.resize((260,260)),(150,i*300+30));sheet.paste(b.resize((260,260)),(550,i*300+30))
    sheet.save(out/'face-comparison.png')
    print('PASS: Python syntax; GLB graph; idempotent paint; Pitts front face clearance, layout and retained rig maps.')
    print(f'PASS: {total} embedded textures preserve {colors} saturated arc/horizon pixels exactly.')

if __name__=='__main__':main()
