"""CPU GLB geometry/texture review; no lighting or Unity build. Rear Eye_Rear, front inspection view."""
import sys,math
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0,str(Path.cwd()/'tools/own_models'))
from vr_cockpit import Glb,MODELS

def render(path,eye,target,out):
 g=Glb(path);W=g.world_matrices();eye=np.array(eye);forward=np.array(target)-eye;forward/=np.linalg.norm(forward)
 right=np.cross(forward,[0,1,0]);right/=np.linalg.norm(right);up=np.cross(right,forward)
 camera=np.array([right,up,forward]);width,height=1200,900;focal=800
 canvas=np.full((height,width,3),145,dtype=np.uint8);depth=np.full((height,width),np.inf);textures={}
 for ni,world in W.items():
  node=g.doc['nodes'][ni];name=node.get('name','')
  if 'mesh' not in node or name.startswith(('Sight_','Canopy_Glass')):continue
  for prim in g.doc['meshes'][node['mesh']]['primitives']:
   m=g.doc['materials'][prim['material']] if 'material' in prim else {};pbr=m.get('pbrMetallicRoughness',{})
   if m.get('alphaMode')=='BLEND':continue
   a=g.array(prim['attributes']['POSITION']);a=(world@np.column_stack((a,np.ones(len(a)))).T).T[:,:3];a=(camera@(a-eye).T).T
   ids=g.array(prim['indices']).reshape(-1,3) if 'indices' in prim else np.arange(len(a)).reshape(-1,3)
   if not len(ids):continue
   uv=g.array(prim['attributes']['TEXCOORD_0']) if 'TEXCOORD_0' in prim['attributes'] else None
   tex=pbr.get('baseColorTexture',{}).get('index');image=None
   if tex is not None and uv is not None:
    if tex not in textures:textures[tex]=np.array(g.image(tex))
    image=textures[tex]
   color=np.array(pbr.get('baseColorFactor',[.6,.6,.6,1])[:3]);color=np.clip(color,0,1)
   for idx in ids:
    t=a[idx]
    if t[:,2].min()<.02:continue
    xy=t[:,:2]/t[:,2,None]*focal;xy[:,0]+=width/2;xy[:,1]=height/2-xy[:,1]
    x0,y0=np.maximum(np.floor(xy.min(0)).astype(int),0);x1,y1=np.minimum(np.ceil(xy.max(0)).astype(int),[width-1,height-1])
    if x0>x1 or y0>y1:continue
    den=(xy[1,1]-xy[2,1])*(xy[0,0]-xy[2,0])+(xy[2,0]-xy[1,0])*(xy[0,1]-xy[2,1])
    if abs(den)<1e-9:continue
    yy,xx=np.mgrid[y0:y1+1,x0:x1+1];xx=xx+.5;yy=yy+.5
    b0=((xy[1,1]-xy[2,1])*(xx-xy[2,0])+(xy[2,0]-xy[1,0])*(yy-xy[2,1]))/den
    b1=((xy[2,1]-xy[0,1])*(xx-xy[2,0])+(xy[0,0]-xy[2,0])*(yy-xy[2,1]))/den;b2=1-b0-b1
    inv=b0/t[0,2]+b1/t[1,2]+b2/t[2,2];z=1/np.maximum(inv,1e-9)
    roi=depth[y0:y1+1,x0:x1+1];mask=(b0>=0)&(b1>=0)&(b2>=0)&(z<roi)
    if not mask.any():continue
    pixels=canvas[y0:y1+1,x0:x1+1]
    if image is not None:
     uvp=(b0[:,:,None]*uv[idx[0]]/t[0,2]+b1[:,:,None]*uv[idx[1]]/t[1,2]+b2[:,:,None]*uv[idx[2]]/t[2,2])/inv[:,:,None]
     tx=np.clip((uvp[:,:,0]*image.shape[1]).astype(int),0,image.shape[1]-1);ty=np.clip((uvp[:,:,1]*image.shape[0]).astype(int),0,image.shape[0]-1)
     rgb=image[ty,tx]*color
    else:rgb=np.broadcast_to(np.power(color,1/2.2)*255,(*mask.shape,3))
    pixels[mask]=np.clip(rgb[mask],0,255).astype('uint8');roi[mask]=z[mask]
 Image.fromarray(canvas).save(out)

out=Path.cwd()/'renders/instrument-style';out.mkdir(parents=True,exist_ok=True)
p=MODELS/'pitts-s2b-like/pitts-s2b-like-own.glb'
render(p,[1.325,.300,0],[.808,-.04,0],out/'pitts-rear.png')
render(p,[.53,.300,0],[.151,.015,0],out/'pitts-front.png')
