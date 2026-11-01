"""CPU-only round-3 review renders of exported floating sights.

python3 tools/own_models/render_sights.py [--out docs/pending/sights-renders/v3]
Rear-eye pinholes retain opaque geometry. External views exclude all Sight_ nodes.
"""
import json
import hashlib
import math
import argparse
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw,ImageFont
from glb_visuals import Glb
from sights import ROOT,MODELS,spec,native,parts
from check_sights import bar_metrics
import render_iac_visuals as cpu

OUT=ROOT/'docs/pending/sights-renders/v3'


def font(size):
    p=Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/pdfjs-dist/standard_fonts/LiberationSans-Regular.ttf'
    return ImageFont.truetype(str(p),size) if p.exists() else ImageFont.load_default(size=size)


def eye_overlay(camera,metrics,title,blocked=False,isolated=False):
    base=Image.open(camera['path']);w,h=base.size;im=Image.new('RGB',(w,h+170),'#142032');im.paste(base,(0,0));d=ImageDraw.Draw(im)
    cx,cy=w/2,h/2
    # Dotted one-pixel references preserve the visible red bar edges.
    for dx,dy in [(1,0),(0,1),(1,1),(-1,1)]:
        for t in range(-int(h*.42),int(h*.42),10):
            d.line((cx+dx*t,cy+dy*t,cx+dx*(t+4),cy+dy*(t+4)),fill='#00e5ed',width=1)
    d.ellipse((cx-8,cy-8,cx+8,cy+8),outline='#00e5ed',width=1)
    lines=[title+' | dotted cyan = true level / vertical / +/-45 deg; optical centre = rear eye lateral line',
           'Measured mesh angles from horizontal: '+', '.join(f"{k}: {v['angle_deg']:+.7f} deg" for k,v in metrics.items()),
           'Absolute errors: '+', '.join(f"{k}: {v['error_deg']:.7f} deg" for k,v in metrics.items()),
           f"Maximum bar-centre offset: {max(v['centre_offset_deg'] for v in metrics.values()):.7f} deg. Floating bars only; no hub or attachments.",
           ('FRAME ISOLATION: opaque aircraft omitted; full-scene rear-eye view is blocked.' if isolated else 'Opaque aircraft retained; transparent glass omitted by CPU renderer. Cockpit-only bars included for this eye view.')]
    for j,line in enumerate(lines):d.text((18,h+10+j*31),line,font=font(19),fill='white')
    if blocked:
        d.rectangle((10,48,w-10,115),fill='#792b23')
        d.text((24,66),'VISIBILITY FLAG: specified rear eye is inside current fuselage; all four bars are occluded.',font=font(24),fill='white')
    im.save(camera['path'])


def main():
    ap=argparse.ArgumentParser();ap.add_argument('--out',default=str(OUT.relative_to(ROOT)));args=ap.parse_args()
    out=ROOT/args.out;out.mkdir(parents=True,exist_ok=True)
    manifest=dict(schema_version=3,renderer='Deterministic CPU textured triangle z-buffer; transparent glass omitted',
        visibility='Rear-eye views include Sight_ nodes. External views filter every Sight_ node before rasterization, matching cockpit-only exclusion.',
        angle_convention='Undirected screen angle from horizontal with up=-body Z: Level=0, Vertical=90, Plus45=side*45, Minus45=-side*45; side L=-1, R=+1.',
        runtime_limit='CPU geometry evidence only; Unity camera callback check and device visual review remain operator tasks.',aircraft={})
    for aircraft,row in spec()['aircraft'].items():
        short={'extra-300-like':'extra','pitts-s2b-like':'pitts','great-lakes-like':'great-lakes'}[aircraft]
        suffix='-checker-unmarked' if short=='pitts' else '-unmarked' if short=='extra' else ''
        path=MODELS/aircraft/(aircraft+'-own'+suffix+'.glb');g=Glb(path);eye=native(row['eye_body_m'],row)
        entries=[]
        for side,sign in [('L',-1),('R',1)]:
            centre=np.array(row['eye_body_m']);centre[1]=sign*row['y_frame_m'];target=native(centre,row)
            fov=math.degrees(2*math.atan(.70/row['y_frame_m']))
            name=f'{short}-cockpit-rear-eye-{side}.png'
            camera=cpu.render(path,out/name,eye,target,fov,label=f'{short} COCKPIT rear-eye looking '+('LEFT' if side=='L' else 'RIGHT'),perspective=True,image_size=(1800,1350))
            metrics={}
            for n,p,a,i in parts(g):
                label=n.removeprefix(f'Sight_{side}_')
                if n.startswith(f'Sight_{side}_') and label in spec()['frame']['directions_body']:metrics[label]=bar_metrics(a,row,sign,label)
            eye_overlay(camera,metrics,short+' '+side,blocked=short=='great-lakes')
            entries.append(dict(path=name,eye_body_m=row['eye_body_m'],eye_gltf_m=eye.tolist(),target_gltf_m=target.tolist(),hfov_deg=fov,
                cockpit_only_nodes_included=True,opaque_aircraft_retained=True,metrics=metrics))
            if short=='great-lakes':
                isolated=f'{short}-cockpit-rear-eye-{side}-frame-isolated.png'
                camera=cpu.render(path,out/isolated,eye,target,fov,include=lambda n:n.startswith(f'Sight_{side}_'),
                    label='Great Lakes FRAME ISOLATION; actual full-scene rear-eye view is blocked',perspective=True,image_size=(1800,1350))
                eye_overlay(camera,metrics,'Great Lakes '+side+' FRAME ISOLATION',isolated=True)
                entries.append(dict(path=isolated,geometry_isolation=True,opaque_aircraft_omitted=True,
                    include_node_prefix=f'Sight_{side}_',eye_gltf_m=eye.tolist(),target_gltf_m=target.tolist(),hfov_deg=fov))
        name=f'{short}-exterior-34.png'
        excluded=[n['name'] for n in g.doc['nodes'] if n.get('name','').startswith('Sight_')]
        cpu.render(path,out/name,(9,3,-5),(.6,0,0),11.5 if short=='great-lakes' else 10,
            include=lambda n:not n.startswith('Sight_'),label=short+' EXTERIOR 3/4 | cockpit-only sights HIDDEN (all Sight_ nodes excluded)',
            image_size=(2200,1650),view_kind='exterior 3/4')
        entries.append(dict(path=name,eye_gltf_m=[9,3,-5],target_gltf_m=[.6,0,0],orthographic_width_m=11.5 if short=='great-lakes' else 10,
            cockpit_only_nodes_included=False,exclude_node_prefix='Sight_',excluded_nodes=excluded))
        manifest['aircraft'][aircraft]=dict(model=str(path.relative_to(ROOT)),model_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),views=entries)
    for entry in manifest['aircraft'].values():
        for view in entry['views']:view['png_sha256']=hashlib.sha256((out/view['path']).read_bytes()).hexdigest()
    (out/'render-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')


if __name__=='__main__':main()
