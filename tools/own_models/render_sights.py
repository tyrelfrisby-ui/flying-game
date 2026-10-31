"""CPU-only review renders of committed sights and lowered Pitts rear card.
python3 tools/own_models/render_sights.py
"""
import json
import hashlib
import math
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw,ImageFont
from glb_visuals import Glb
from sights import ROOT,MODELS,spec,native,parts
from check_sights import bar_metrics
from render_iac_round3 import project
import render_iac_visuals as cpu

OUT=ROOT/'docs/pending/sights-renders'

def font(size):
    p=Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/pdfjs-dist/standard_fonts/LiberationSans-Regular.ttf'
    return ImageFont.truetype(str(p),size) if p.exists() else ImageFont.load_default(size=size)

def eye_overlay(camera,metrics,title,blocked=False,isolated=False):
    base=Image.open(camera['path']);w,h=base.size;im=Image.new('RGB',(w,h+150),'#142032');im.paste(base,(0,0));d=ImageDraw.Draw(im)
    cx,cy=w/2,h/2
    # Dotted 1 px reference lines keep the red rods visible on either side.
    for dx,dy in [(0,1),(1,1),(-1,1)]:
        for t in range(-int(h*.42),int(h*.42),10):
            d.line((cx+dx*t,cy+dy*t,cx+dx*(t+4),cy+dy*(t+4)),fill='#00e5ed',width=1)
    d.ellipse((cx-8,cy-8,cx+8,cy+8),outline='#00e5ed',width=1)
    lines=[title+' | dotted cyan = true vertical / +/-45 deg; pinhole optical centre = eye lateral line',
           'Measured mesh axes: '+', '.join(f"{k}: {v['angle_deg']:+.6f} deg" for k,v in metrics.items()),
           'Errors: '+', '.join(f"{k}: {v['error_deg']:.6f} deg" for k,v in metrics.items())+f"; centre offset <= {max(v['centre_offset_deg'] for v in metrics.values()):.6f} deg",
           ('GEOMETRY ISOLATION: opaque aircraft omitted; full-scene eye render is blocked.' if isolated else 'Opaque aircraft retained; transparent glass omitted by CPU renderer. Sight rods are baked GLB geometry.')]
    for j,line in enumerate(lines):d.text((20,h+12+j*32),line,font=font(21),fill='white')
    if blocked:
        d.rectangle((10,48,w-10,115),fill='#792b23')
        d.text((24,66),'VISIBILITY FLAG: specified body eye is inside current fuselage; rods are occluded.',font=font(25),fill='white')
    im.save(camera['path'])

def main():
    OUT.mkdir(parents=True,exist_ok=True);manifest={}
    for aircraft,row in spec()['aircraft'].items():
        short={'extra-300-like':'extra','pitts-s2b-like':'pitts','great-lakes-like':'great-lakes'}[aircraft]
        suffix='-checker-unmarked' if short=='pitts' else '-unmarked' if short=='extra' else ''
        path=MODELS/aircraft/(aircraft+'-own'+suffix+'.glb');g=Glb(path);eye=native(row['eye_body_m'],row)
        entries=[]
        for side,sign in [('L',-1),('R',1)]:
            centre=np.array(row['eye_body_m']);centre[1]=sign*row['y_mount_m'];target=native(centre,row)
            fov=math.degrees(2*math.atan(.70/row['y_mount_m']))
            name=f'{short}-rear-eye-{side}.png'
            camera=cpu.render(path,OUT/name,eye,target,fov,label=f'{short} rear eye looking '+('LEFT' if side=='L' else 'RIGHT'),perspective=True,image_size=(1800,1350))
            metrics={}
            for n,p,a,i in parts(g):
                label=n.removeprefix(f'Sight_{side}_')
                if n.startswith(f'Sight_{side}_') and label in ['Vertical','Plus45','Minus45']:metrics[label]=bar_metrics(a,row,sign,label)
            eye_overlay(camera,metrics,short+' '+side,blocked=short=='great-lakes')
            entries.append(dict(path=name,eye_gltf_m=eye.tolist(),target_gltf_m=target.tolist(),hfov_deg=fov,metrics=metrics))
            if short=='great-lakes':
                isolated=f'{short}-rear-eye-{side}-frame-isolated.png'
                camera=cpu.render(path,OUT/isolated,eye,target,fov,include=lambda n:n.startswith('Sight_'),label='Great Lakes GEOMETRY ISOLATION; cockpit omitted; actual full-scene view is blocked',perspective=True,image_size=(1800,1350))
                eye_overlay(camera,metrics,'Great Lakes '+side+' GEOMETRY ISOLATION',isolated=True)
                entries.append(dict(path=isolated,geometry_isolation=True,opaque_aircraft_omitted=True,eye_gltf_m=eye.tolist(),target_gltf_m=target.tolist(),hfov_deg=fov))
        name=f'{short}-exterior-34.png'
        camera=cpu.render(path,OUT/name,(9,3,-5),(.6,0,0),11.5 if short=='great-lakes' else 10,label=short+' rear exterior 3/4; both red sight frames',image_size=(2200,1650),view_kind='exterior 3/4')
        im=Image.open(camera['path']);draw=ImageDraw.Draw(im)
        for side,sign in [('LEFT',-1),('RIGHT',1)]:
            centre=np.array(row['eye_body_m']);centre[1]=sign*row['y_mount_m'];at=project(camera,native(centre,row))
            x,y=at;draw.ellipse((x-38,y-38,x+38,y+38),outline='#00e5ed',width=2)
            draw.text((x-40,y+45),side+' SIGHT',font=font(20),fill='#002e3b',stroke_width=2,stroke_fill='#cfe9ec')
        im.save(camera['path'])
        entries.append(dict(path=name,eye_gltf_m=[9,3,-5],target_gltf_m=[.6,0,0],orthographic_width_m=11.5 if short=='great-lakes' else 10))
        manifest[aircraft]=dict(model=str(path.relative_to(ROOT)),model_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),views=entries)
        if short=='pitts':
            target=np.array([.8087,-.035,0])
            cpu.render(path,OUT/'pitts-panel-rear-eye.png',eye,target,70,label='Pitts lowered panel card from Ty rear sight eye',perspective=True,image_size=(2200,1650))
            only=lambda n:n=='Cockpit' or n.startswith(('RearPanel','SequenceCard'))
            clip=lambda n,a:n!='Cockpit' or (a[:,0].min()>.79 and a[:,0].max()<.83)
            cpu.render(path,OUT/'pitts-panel-closeup.png',(1.8,-.005,0),(.8075,-.005,0),.72,only,'Pitts rear panel; card DOWN 50 mm; instruments unchanged',triangle_filter=clip,image_size=(2200,1650))
            manifest[aircraft]['panel_views']=['pitts-panel-rear-eye.png','pitts-panel-closeup.png']
    (OUT/'render-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')

if __name__=='__main__':main()
