"""POH side overlay, uniform 18 m span calibration. Reference pixels remain review-only."""
import sys,json,hashlib,subprocess,tempfile
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw,ImageFilter
from glb_visuals import Glb
from build_grob103 import ROOT,OUT,ID

def run(pdf,dest=None):
 dest=dest or ROOT/'renders/grob103/blanc-v1';dest.mkdir(parents=True,exist_ok=True);path=OUT/(ID+'-own.glb')
 with tempfile.TemporaryDirectory() as tmp:
  prefix=Path(tmp)/'poh';subprocess.run(['pdftoppm','-f','14','-l','14','-scale-to','1600','-png',str(pdf),str(prefix)],check=True)
  ref=Image.open(str(prefix)+'-014.png').convert('RGB');mask=Image.new('L',ref.size);draw=ImageDraw.Draw(mask);g=Glb(path);world=g.world_matrices();scale=854/18
  for i,n in enumerate(g.doc['nodes']):
   if 'mesh' not in n or n['name'].startswith(('Wing','Aileron','Spoiler','Airbrake','Tip_Skid')):continue
   for p in g.doc['meshes'][n['mesh']]['primitives']:
    v=g.array(p['attributes']['POSITION'])@world[i][:3,:3].T+world[i][:3,3];xy=np.c_[762+(v[:,0]+2.5)*scale,708-(v[:,1]+.23)*scale]
    ids=g.array(p['indices']).reshape(-1,3) if 'indices' in p else np.arange(len(v)).reshape(-1,3)
    for t in xy[ids]:draw.polygon([tuple(a) for a in t],fill=255)
  a=np.array(ref);inside=np.array(mask)>0;edge=np.array(mask.filter(ImageFilter.MaxFilter(3)))!=np.array(mask.filter(ImageFilter.MinFilter(3)));a[inside]=(a[inside]*.65+np.array([0,190,230])*.35).astype('uint8');a[edge]=[235,35,35]
  im=Image.fromarray(a).crop((735,630,1180,755)).resize((1780,600));d=ImageDraw.Draw(im);d.rectangle((0,0,1780,38),fill='white');d.text((12,12),dest.name+' / POH 1.6 / cyan mesh, red contour / uniform 854px : 18m / level axes / wings omitted',fill='black');im.save(dest/'side-poh-overlay.png')
  meta={'source_url':'https://www.norcalsoaring.org/uploads/1/0/0/6/100675970/grob_g-103-c-twiniii-acro-poh.pdf','poh_sha256':hashlib.sha256(Path(pdf).read_bytes()).hexdigest(),'pdf_page':14,'printed_page':'1.6','pixels_per_metre':scale,'nose_pixel':[762,708],'model_nose_height_m':-.23,'rotation_deg':0,'axis_specific_scaling':False,'reference_registration':'Approximate scan nose; level model axes. No contour fitting.','model_sha256':hashlib.sha256(path.read_bytes()).hexdigest()}
  (dest/'overlay-manifest.json').write_text(json.dumps(meta,indent=2)+'\n')
if __name__=='__main__':run(Path(sys.argv[1]),Path(sys.argv[2]) if len(sys.argv)>2 else None)
