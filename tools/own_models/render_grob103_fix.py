"""Twin III review views and uniform-scale POH overlay. No reference pixels in git.
Usage: python render_grob103_fix.py /path/to/TwinIII-POH.pdf
"""
import sys,json,hashlib,subprocess,tempfile
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw,ImageFilter
from grob103_render_cpu import render
from build_grob103 import OUT,ID,ROOT
from glb_visuals import Glb

def run(pdf):
 d=ROOT/'renders/grob/v-fix';d.mkdir(parents=True,exist_ok=True);p=OUT/(ID+'-own.glb')
 views=[('side',(1.6,.2,22),(1.6,.2,0),9.4),('three-quarter',(-15,8,18),(1,.2,0),19.3),('front',(-22,1,0),(0,.2,0),19.3),('gear-main',(0,-.1,3),(.4,-.4,0),1.3),('gear-nose',(-2,-.1,3),(-1.7,-.35,0),.9),('gear-tail',(6,0,3),(4.6,-.03,0),.9)]
 for n,e,t,s in views:
  render(p,d/(n+'.png'),e,t,s,image_size=(1600,1000),render_glass=True,smooth_normals=True,label='Twin III Acro / '+n,view_kind='exterior')
 with tempfile.TemporaryDirectory() as tmp:
  dest=Path(tmp)/'poh'
  subprocess.run(['pdftoppm','-f','14','-l','14','-scale-to','1600','-png',str(pdf),str(dest)],check=True)
  ref=Image.open(str(dest)+'-014.png').convert('RGB');mask=Image.new('L',ref.size);draw=ImageDraw.Draw(mask)
  g=Glb(p);wm=g.world_matrices();scale=854/18
  # Published three-view, page 1.6. Fixed uniform scale from plan span.
  # Side nose (762,708), centreline nose height -0.23; no anisotropic fitting.
  for i,n in enumerate(g.doc['nodes']):
   if 'mesh' not in n or n['name'].startswith(('Wing','Aileron','Spoiler','Airbrake_Cap')):continue
   for prim in g.doc['meshes'][n['mesh']]['primitives']:
    v=g.array(prim['attributes']['POSITION']);v=v@wm[i][:3,:3].T+wm[i][:3,3]
    xy=np.c_[762+(v[:,0]+2.5)*scale,708-(v[:,1]+.23)*scale]
    ids=g.array(prim['indices']).reshape(-1,3) if 'indices' in prim else np.arange(len(v)).reshape(-1,3)
    for t in xy[ids]:draw.polygon([tuple(a) for a in t],fill=255)
  a=np.array(ref);inside=np.array(mask)>0;edge=np.array(mask.filter(ImageFilter.MaxFilter(3)))!=np.array(mask.filter(ImageFilter.MinFilter(3)))
  a[inside]=(a[inside]*.65+np.array([0,190,230])*.35).astype('uint8');a[edge]=[235,35,35]
  im=Image.fromarray(a).crop((735,630,1180,755)).resize((1780,600));ImageDraw.Draw(im).rectangle((0,0,1780,38),fill='white');ImageDraw.Draw(im).text((12,12),'POH 1.6 / cyan model, red contour / uniform 854px : 18m / level axes, nose aligned / wings omitted',fill=(0,0,0));im.save(d/'side-poh-overlay.png')
 manifest={'renderer':'CPU exported GLB, transparent glass','views':views,'models':{f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in OUT.glob('*.glb')},'overlay':{'reference':'https://www.norcalsoaring.org/uploads/1/0/0/6/100675970/grob_g-103-c-twiniii-acro-poh.pdf','sha256':hashlib.sha256(Path(pdf).read_bytes()).hexdigest(),'pdf_page':14,'printed_page':'1.6','pixels_per_m':scale,'nose_pixel':[762,708],'rotation_deg':0,'side_wings_omitted':True,'note':'Uniform scale and nose registration, no contour fitting; scan/drawing alignment is approximate.'},'images':{f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in d.glob('*.png')},'visual_approval':'PENDING'}
 (d/'render-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
if __name__=='__main__':run(sys.argv[1])
