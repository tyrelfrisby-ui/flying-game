"""CPU-only textured GLB review renders. No Unity, Blender or graphics device required.

python render_iac_visuals.py [--undersides | --cockpits]
Images are evidence of asset geometry/materials, not Unity lighting or runtime UI.
"""
import argparse
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from glb_visuals import Glb

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT/'renders/iac-pitts'
MODELS = ROOT/'unity/Assets/_Project/Resources/Models'


def render(path, output, eye, target, scale, include=None, label='', up=(0,1,0)):
    glb = Glb(path); w,h = 1600,1200
    eye,target,up = map(lambda x:np.array(x,dtype=float),(eye,target,up))
    forward = target-eye; forward /= np.linalg.norm(forward)
    right = np.cross(forward,up); right /= np.linalg.norm(right)
    vertical = np.cross(right,forward)
    basis = np.array([right,vertical,forward]).T
    frame = np.empty((h,w,3),dtype=np.uint8); frame[:] = (211,222,231)
    depth = np.full((h,w),np.inf)
    materials = glb.doc['materials']; textures = {}
    for i,m in enumerate(materials):
        tex = m.get('pbrMetallicRoughness',{}).get('baseColorTexture')
        if tex: textures[i] = np.array(glb.image(tex['index']))
    light = np.array((-.35,.75,.55)); light /= np.linalg.norm(light)
    for node in glb.doc['nodes']:
        name = node.get('name','')
        if 'mesh' not in node or (include and not include(name)): continue
        assert not any(k in node for k in ('translation','rotation','scale','matrix')), name
        for p in glb.doc['meshes'][node['mesh']]['primitives']:
            mi = p['material']; mat = materials[mi]
            if mat.get('alphaMode') == 'BLEND': continue
            pos = glb.array(p['attributes']['POSITION']); v = (pos-eye)@basis
            screen = np.column_stack((w/2+v[:,0]*w/scale,h/2-v[:,1]*w/scale))
            inds = glb.array(p['indices']).reshape(-1,3) if 'indices' in p else np.arange(len(pos)).reshape(-1,3)
            uv = glb.array(p['attributes']['TEXCOORD_0']) if 'TEXCOORD_0' in p['attributes'] else None
            col = np.array(mat.get('pbrMetallicRoughness',{}).get('baseColorFactor',[1,1,1,1])[:3])
            col = np.where(col<=.0031308,col*12.92,1.055*np.power(col,1/2.4)-.055)*255
            for ids in inds:
                xy = screen[ids]; z = v[ids,2]
                if z.min() <= 0: continue
                lo = np.maximum(np.floor(xy.min(0)).astype(int),0); hi = np.minimum(np.ceil(xy.max(0)).astype(int),(w-1,h-1))
                if np.any(hi<lo): continue
                a,b,c = xy; det = (b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
                if abs(det)<1e-9: continue
                xx,yy = np.meshgrid(np.arange(lo[0],hi[0]+1)+.5,np.arange(lo[1],hi[1]+1)+.5)
                u = ((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/det
                t = ((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/det
                r = 1-u-t; zz = u*z[0]+t*z[1]+r*z[2]
                sl = np.s_[lo[1]:hi[1]+1,lo[0]:hi[0]+1]
                mask = (u>=-1e-6)&(t>=-1e-6)&(r>=-1e-6)&(zz<depth[sl])
                if not mask.any(): continue
                n = np.cross(pos[ids[1]]-pos[ids[0]],pos[ids[2]]-pos[ids[0]])
                n /= max(np.linalg.norm(n),1e-9)
                shade = .76+.24*abs(n@light)
                if mi in textures and uv is not None:
                    tuv = u[:,:,None]*uv[ids[0]]+t[:,:,None]*uv[ids[1]]+r[:,:,None]*uv[ids[2]]
                    tex = textures[mi]; th,tw = tex.shape[:2]
                    tx = np.clip((tuv[:,:,0]*tw).astype(int),0,tw-1)
                    ty = np.clip((tuv[:,:,1]*th).astype(int),0,th-1)
                    rgb = tex[ty,tx]*shade
                else: rgb = np.broadcast_to(col*shade,(*mask.shape,3))
                frame[sl][mask] = np.clip(rgb[mask],0,255).astype('uint8'); depth[sl][mask] = zz[mask]
    im = Image.fromarray(frame); draw = ImageDraw.Draw(im)
    draw.rectangle((0,0,w,40),fill=(27,35,46)); draw.text((18,14),label+' | CPU GLB review / orthographic cutaway',fill='white')
    OUT.mkdir(parents=True,exist_ok=True); im.save(OUT/output)
    print(OUT/output)


def main():
    ap = argparse.ArgumentParser(); ap.add_argument('--undersides',action='store_true'); ap.add_argument('--cockpits',action='store_true'); args=ap.parse_args()
    if args.undersides:
        path = MODELS/'pitts-s2b-like/pitts-s2b-like-own-checker-unmarked.glb'
        render(path,'pitts-checker-below.png',(1,-10,0),(1,0,0),8.5,label='Pitts Checkered - below',up=(-1,0,0))
        render(path,'pitts-checker-below-front-quarter.png',(-7,-8,6),(1,0,0),8.5,label='Pitts Checkered - below/front quarter')
        render(path,'pitts-checker-upper-tail-below.png',(1,-10,0),(1,0,0),8.5,
               include=lambda n:n in ('Wing_Upper','Aileron_UL','Aileron_UR','Stab','Elevator_L','Elevator_R'),
               label='Pitts Checkered - upper wing and tail underside isolation',up=(-1,0,0))
    if args.cockpits:
        from sequence_card import PLACEMENTS, geometry
        for aircraft,model,filename in (
            ('extra','extra-300-like','extra-300-like-own-unmarked.glb'),
            ('pitts','pitts-s2b-like','pitts-s2b-like-own-checker-unmarked.glb'),
            ('great-lakes','great-lakes-like','great-lakes-like-own.glb')):
            cx,_,cz = geometry(**PLACEMENTS[model])[2]['centre_builder']
            filename = model+'/'+filename
            path = MODELS/filename
            # Convert builder coordinates to raw glTF (-x,z,y).
            convert = lambda x,y,z:(-x,z,y)
            only = lambda n:n.startswith(('Cockpit','SequenceCard','PanelCompass'))
            render(path,aircraft+'-sequence-panel.png',convert(cx-.9,-.12,cz+.16),convert(cx,0,cz-.05),.74,only,
                   aircraft+' - centre panel / sequence card')
            render(path,aircraft+'-cockpit-cutaway.png',convert(cx-1.35,-1.1,cz+1.1),convert(cx-.13,0,cz-.17),1.95,only,
                   aircraft+' - cockpit cutaway / sequence holder')


if __name__ == '__main__': main()
