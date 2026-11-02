"""Uniform-scale manual three-view overlays. Reference pixels stay in assets-incoming.
Run pdftoppm via this script; only the manifest and model-only masks are tracked.
"""
import hashlib,json,subprocess
import numpy as np
from PIL import Image,ImageDraw,ImageFilter
from build_grob103 import ROOT,OUT,ID
from glb_visuals import Glb
REF=ROOT/'assets-incoming/ref-grob103/grob103-flight-manual.pdf'
DEST=ROOT/'assets-incoming/ref-grob103/review-overlays';DEST.mkdir(parents=True,exist_ok=True)
TRACK=ROOT/'renders/grob103-own';TRACK.mkdir(parents=True,exist_ok=True)
def run():
 subprocess.run(['pdftoppm','-f','7','-l','7','-scale-to','1200','-png',str(REF),str(DEST/'manual')],check=True)
 ref=Image.open(DEST/'manual-07.png').convert('RGB');k=17.5/(1070-148)
 g=Glb(OUT/(ID+'-own.glb'));wm=g.world_matrices()
 # Source pixels measured by eye at 1200 px page height, no per-axis warping.
 # Top nose at (823,610), root leading edge (693,610); side nose (330,582).
 funcs={'top':lambda p:np.c_[693-p[:,0]/k,610-p[:,2]/k], 'side':lambda p:np.c_[330-(p[:,1]+.23)/k,582-(p[:,0]+2.5)/k], 'front':lambda p:np.c_[158-(p[:,1]-.12)/k,610-p[:,2]/k]}
 crops={'top':(390,130,840,1090),'side':(230,130,355,600),'front':(60,130,190,1090)}
 manifest=dict(reference_pdf_sha256=hashlib.sha256(REF.read_bytes()).hexdigest(),pdf_page=7,page_render_size=list(ref.size),scale_m_per_pixel=k,measurement_span_pixels=[148,1070],measurements='docs/models/grob103-own-PROVENANCE.md',model_sha256=hashlib.sha256((OUT/(ID+'-own.glb')).read_bytes()).hexdigest(),reference_composites=str(DEST.relative_to(ROOT)),views={})
 for view,project in funcs.items():
    mask=Image.new('L',ref.size,0);d=ImageDraw.Draw(mask)
    for i,n in enumerate(g.doc['nodes']):
        if 'mesh' not in n:continue
        if view=='side' and n['name'].startswith(('Wing','Aileron','Spoiler','Airbrake_Cap')):continue
        for prim in g.doc['meshes'][n['mesh']]['primitives']:
            p=g.array(prim['attributes']['POSITION']);p=p@wm[i][:3,:3].T+wm[i][:3,3]
            ids=g.array(prim['indices']).reshape(-1,3) if 'indices' in prim else np.arange(len(p)).reshape(-1,3)
            xy=project(p)
            for t in xy[ids]:d.polygon([tuple(q) for q in t],fill=255)
    edge=np.asarray(mask.filter(ImageFilter.MaxFilter(3)))!=np.asarray(mask.filter(ImageFilter.MinFilter(3)))
    a=np.asarray(ref).copy();m=np.asarray(mask)>0;a[m]=(a[m]*.65+np.array([0,190,230])*.35).astype(np.uint8);a[edge]=[235,35,35]
    image=Image.fromarray(a).crop(crops[view]);model=mask.crop(crops[view])
    rotation=90 if view=='top' else 270
    image=image.rotate(rotation,expand=True);model=model.rotate(rotation,expand=True)
    image.save(DEST/f'overlay-{view}.png');model.save(TRACK/f'overlay-model-mask-{view}.png')
    manifest['views'][view]=dict(crop_source_pixels=crops[view],rotation_ccw_degrees=rotation,composite=f'assets-incoming/ref-grob103/review-overlays/overlay-{view}.png',side_wings_omitted=view=='side')
 (TRACK/'overlay-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
if __name__=='__main__':run()
