"""CPU review images from exported GLBs; glass enabled outside, omitted at eyes."""
from pathlib import Path
import math,json,hashlib
import numpy as np
from glb_visuals import Glb
from grob103_render_cpu import render
from build_grob103 import OUT,ID,ROOT,cv,EYES
D=ROOT/'renders/grob103-own';D.mkdir(parents=True,exist_ok=True)
def run():
 for suffix,label in [('', 'white'),('-tf','tf')]:
    path=OUT/(ID+'-own'+suffix+'.glb')
    views={'side':((1,.1,22),(1,.1,0),9.6,(0,1,0)), 'top':((1,24,0),(1,.1,0),19.4,(-1,0,0)), 'front':((-22,1,0),(0,.1,0),19.4,(0,1,0)), 'three-quarter-front':((-15,9,17),(1,.1,0),18.8,(0,1,0)), 'three-quarter-rear':((17,8,-17),(1,.1,0),18.8,(0,1,0)), 'underside':((1,-24,0),(1,.1,0),19.4,(-1,0,0))}
    for n,(eye,target,scale,up) in views.items():
        render(path,D/f'{label}-{n}.png',eye,target,scale,up=up,image_size=(1400,1000),render_glass=True,smooth_normals=True,label=label+' '+n,view_kind='exterior')
    for seat,eye in EYES.items():
        eye=cv(eye);target=eye+[-1,-.28,0]
        render(path,D/f'{label}-{seat}-seat-eye.png',eye,target,85,perspective=True,image_size=(1400,1000),smooth_normals=True,label=label+' '+seat+' true seat eye')
        for side,sg in [('left',1),('right',-1)]:
            render(path,D/f'{label}-{seat}-eye-{side}.png',eye,eye+[-.22,-.35,sg*.29],75,perspective=True,image_size=(1200,1000),smooth_normals=True,label=label+' '+seat+' true eye / '+side)
        px=1.73 if seat=='front' else .53;z=.13 if seat=='front' else .18
        render(path,D/f'{label}-{seat}-panel.png',cv([px-.48,0,z+.05]),cv([px,0,z]),.66,image_size=(1000,1000),smooth_normals=True)
 # Full signed travels, plus the opposite pitch/yaw/roll extreme; spoilers remain extended.
 rig=json.loads((ROOT/'docs/models/grob103-own-rig.json').read_text())['rig']
 for sign,pose in [(1,'positive'),(-1,'negative')]:
    g=Glb(OUT/(ID+'-own.glb'))
    for r in rig:
        if r['param'] not in ['aileron','elevator','rudder','spoiler']:continue
        n=next(n for n in g.doc['nodes'] if n['name']==r['node']);axis=np.array(r['axis_gltf']);v=r['map'][-1 if sign==1 or r['param']=='spoiler' else 0][1]
        if r['kind']=='move':n['translation']=(np.array(n['translation'])+axis*v).tolist()
        else:
            a=math.radians(v)/2;n['rotation']=[*(axis*math.sin(a)),math.cos(a)]
    tmp='/tmp/grob103-deflected-'+pose+'.glb';g.save(tmp)
    for name,eye,sc in [('front',(-16,7,18),18.8),('rear',(16,7,18),18.8),('tail',(11,4,6),5)]:
        render(tmp,D/f'deflected-{pose}-{name}.png',eye,(5.2 if name=='tail' else 1,.5 if name=='tail' else .2,0),sc,image_size=(1400,1000),render_glass=True,smooth_normals=True,label=pose+' control extremes / spoilers +170mm')
 manifest={'renderer':'project-derived CPU; Blender startup crashed','images':{},'models':{}}
 for f in OUT.glob('*.glb'):manifest['models'][f.name]=hashlib.sha256(f.read_bytes()).hexdigest()
 for f in D.glob('*.png'):manifest['images'][f.name]=hashlib.sha256(f.read_bytes()).hexdigest()
 (D/'render-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
if __name__=='__main__':run()
