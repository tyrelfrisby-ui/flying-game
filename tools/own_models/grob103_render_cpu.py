"""Grob-local CPU textured GLB review renders. No Unity, Blender or graphics device required.

Used by render_grob103.py; localized from the project Swift CPU renderer.
Images are evidence of asset geometry/materials, not Unity lighting or runtime UI.
"""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from glb_visuals import Glb

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT/'renders/grob103-own'
MODELS = ROOT/'unity/Assets/_Project/Resources/Models'


def render(path, output, eye, target, scale, include=None, label='', up=(0,1,0), perspective=False, triangle_filter=None, image_size=None, clip_skin=False, view_kind=None, render_glass=False, smooth_normals=False):
    glb = Glb(path); w,h = (2400,1800) if str(output).endswith(('rear-panel-straight.png','rear-panel-before.png')) else (1600,1200)
    if image_size: w,h=image_size
    eye,target,up = map(lambda x:np.array(x,dtype=float),(eye,target,up))
    forward = target-eye; forward /= np.linalg.norm(forward)
    right = np.cross(forward,up); right /= np.linalg.norm(right)
    vertical = np.cross(right,forward)
    basis = np.array([right,vertical,forward]).T
    frame = np.empty((h,w,3),dtype=np.uint8); frame[:] = (211,222,231)
    depth = np.full((h,w),np.inf)
    glass_frame=frame.copy(); glass_depth=depth.copy(); glass_alpha=np.zeros((h,w))
    materials = glb.doc['materials']; textures = {}
    for i,m in enumerate(materials):
        tex = m.get('pbrMetallicRoughness',{}).get('baseColorTexture')
        if tex: textures[i] = np.array(glb.image(tex['index']))
    light = np.array((-.35,.75,.55)); light /= np.linalg.norm(light)
    world=glb.world_matrices()
    for node_index,node in enumerate(glb.doc['nodes']):
        if node_index not in world: continue
        name = node.get('name','')
        if 'mesh' not in node or (include and not include(name)): continue
        for p in glb.doc['meshes'][node['mesh']]['primitives']:
            mi = p['material']; mat = materials[mi]
            transparent=mat.get('alphaMode') == 'BLEND'
            if transparent and not render_glass: continue
            active_depth=glass_depth if transparent else depth
            active_frame=glass_frame if transparent else frame
            pos = glb.array(p['attributes']['POSITION'])
            normals = glb.array(p['attributes']['NORMAL']) if smooth_normals else None
            matrix=world[node_index];pos=pos@matrix[:3,:3].T+matrix[:3,3]
            # Sutherland-Hodgman clip of the near-side fuselage skin at raw Z=0.
            uv = glb.array(p['attributes']['TEXCOORD_0']) if 'TEXCOORD_0' in p['attributes'] else None
            inds = glb.array(p['indices']).reshape(-1,3) if 'indices' in p else np.arange(len(pos)).reshape(-1,3)
            if clip_skin and name.startswith(('Fuselage','Headrest','Cockpit_Liner')):
                clipped=[]; clipped_uv=[]
                for ids in inds:
                    polygon=[(pos[i],uv[i] if uv is not None else np.zeros(2)) for i in ids]
                    result=[]
                    for a,b in zip(polygon,polygon[1:]+polygon[:1]):
                        ina=a[0][2]>=0;inb=b[0][2]>=0
                        if ina:result.append(a)
                        if ina!=inb:
                            t=-a[0][2]/(b[0][2]-a[0][2]);result.append((a[0]+t*(b[0]-a[0]),a[1]+t*(b[1]-a[1])))
                    for i in range(1,len(result)-1):
                        for vtx,tex in (result[0],result[i],result[i+1]):clipped.append(vtx);clipped_uv.append(tex)
                if not clipped:continue
                pos=np.array(clipped);uv=np.array(clipped_uv) if uv is not None else None;inds=np.arange(len(pos)).reshape(-1,3)
            v = (pos-eye)@basis
            if perspective:
                focal=w/(2*np.tan(np.radians(scale)/2))
                screen=np.column_stack((w/2+v[:,0]*focal/np.maximum(v[:,2],.001),h/2-v[:,1]*focal/np.maximum(v[:,2],.001)))
            else:
                screen = np.column_stack((w/2+v[:,0]*w/scale,h/2-v[:,1]*w/scale))
            col = np.array(mat.get('pbrMetallicRoughness',{}).get('baseColorFactor',[1,1,1,1])[:3])
            col = np.where(col<=.0031308,col*12.92,1.055*np.power(col,1/2.4)-.055)*255
            for ids in inds:
                if triangle_filter and not triangle_filter(name,pos[ids]): continue
                if smooth_normals and not mat.get('doubleSided',False):
                    face_normal=np.cross(pos[ids[1]]-pos[ids[0]],pos[ids[2]]-pos[ids[0]])
                    if face_normal@(eye-pos[ids].mean(0)) <= 0: continue
                xy = screen[ids]; z = v[ids,2]
                if z.min() <= 0: continue
                lo = np.maximum(np.floor(xy.min(0)).astype(int),0); hi = np.minimum(np.ceil(xy.max(0)).astype(int),(w-1,h-1))
                if np.any(hi<lo): continue
                a,b,c = xy; det = (b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
                if abs(det)<1e-9: continue
                xx,yy = np.meshgrid(np.arange(lo[0],hi[0]+1)+.5,np.arange(lo[1],hi[1]+1)+.5)
                u = ((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/det
                t = ((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/det
                r = 1-u-t
                if perspective:
                    inverse=u/z[0]+t/z[1]+r/z[2]
                    zz=1/np.maximum(inverse,1e-12)
                else: zz = u*z[0]+t*z[1]+r*z[2]
                sl = np.s_[lo[1]:hi[1]+1,lo[0]:hi[0]+1]
                mask = (u>=-1e-6)&(t>=-1e-6)&(r>=-1e-6)&(zz<active_depth[sl])
                if not mask.any(): continue
                n = np.cross(pos[ids[1]]-pos[ids[0]],pos[ids[2]]-pos[ids[0]])
                n /= max(np.linalg.norm(n),1e-9)
                shade = .76+.24*abs(n@light)
                if normals is not None:
                    nn=u[:,:,None]*normals[ids[0]]+t[:,:,None]*normals[ids[1]]+r[:,:,None]*normals[ids[2]]
                    nn/=np.maximum(np.linalg.norm(nn,axis=2)[:,:,None],1e-9)
                    shade=(.76+.24*np.abs(nn@light))[:,:,None]
                if mi in textures and uv is not None:
                    if perspective:
                        tuv=(u[:,:,None]*uv[ids[0]]/z[0]+t[:,:,None]*uv[ids[1]]/z[1]+r[:,:,None]*uv[ids[2]]/z[2])/np.maximum(inverse[:,:,None],1e-12)
                    else: tuv = u[:,:,None]*uv[ids[0]]+t[:,:,None]*uv[ids[1]]+r[:,:,None]*uv[ids[2]]
                    tex = textures[mi]; th,tw = tex.shape[:2]
                    tx = np.clip((tuv[:,:,0]*tw).astype(int),0,tw-1)
                    ty = np.clip((tuv[:,:,1]*th).astype(int),0,th-1)
                    rgb = tex[ty,tx]*shade
                else: rgb = np.broadcast_to(col*shade,(*mask.shape,3))
                active_frame[sl][mask] = np.clip(rgb[mask],0,255).astype('uint8'); active_depth[sl][mask] = zz[mask]
                if transparent: glass_alpha[sl][mask]=mat.get('pbrMetallicRoughness',{}).get('baseColorFactor',[1,1,1,.22])[3]
    if render_glass:
        alpha=np.where(glass_depth<depth,glass_alpha,0)[:,:,None]
        frame=np.uint8(frame*(1-alpha)+glass_frame*alpha)
    im = Image.fromarray(frame); draw = ImageDraw.Draw(im)
    draw.rectangle((0,0,w,40),fill=(27,35,46)); draw.text((18,14),label+(' | CPU GLB / perspective; glass omitted' if perspective else ' | CPU GLB / '+(view_kind or 'orthographic panel isolation')),fill='white')
    OUT.mkdir(parents=True,exist_ok=True); im.save(OUT/output)
    print(OUT/output)
    return dict(eye=eye,basis=basis,w=w,h=h,scale=scale,perspective=perspective,path=OUT/output)
