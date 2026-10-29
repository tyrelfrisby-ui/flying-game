"""3-view overlay: project the GLB's triangles onto the Commons 3-view (display-px registration) -> compare PNGs."""
import bpy, sys, os, numpy as np
from PIL import Image, ImageDraw, ImageFilter
glb, out = sys.argv[sys.argv.index('--')+1:][:2]
REF=os.environ.get('OV_REF') or '/workspace/aero-refs/233/Schweizer_SGS_2-33A_two-seat_glider_colour_3-view_drawing.jpg'  # reference image is never committed
import os
S=2500/1024; K=float(os.environ.get('OV_K','0.01527')); KT=float(os.environ.get('OV_KT','0.01542')); KF=float(os.environ.get('OV_KF',str(K)))
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=glb)
tris=[]; names=[]
dg=bpy.context.evaluated_depsgraph_get()
for o in bpy.context.scene.objects:
    if o.type!='MESH' or o.hide_render or o.name.endswith('_HingeAxis'): continue
    if not o.visible_get(): continue
    m=o.evaluated_get(dg).to_mesh(); m.calc_loop_triangles(); M=o.matrix_world
    V=np.array([(M@v.co)[:] for v in m.vertices]);
    if len(V)==0: continue
    V[:,0]*=-1; V[:,1]*=-1      # undo export yaw: nose +X, port +Y
    T=np.array([t.vertices[:] for t in m.loop_triangles]); tris.append(V[T]); names.append(o.name)
views={
 'side':  lambda P: np.c_[360-(P[...,0]-0.55)/K, 197-(P[...,2]-0.47)/K],
 'top':   lambda P: np.c_[510+P[...,1]/KT, 655+(P[...,0]-0.55)/KT],
 'front': lambda P: np.c_[510+P[...,1]/K, 197-108.5-(P[...,2]-0.47)/KF],
}
ref=Image.open(REF).convert('RGB'); W,H=ref.size
def mask(view, sel=None):
    im=Image.new('L',(W,H),0); d=ImageDraw.Draw(im)
    for n,T in zip(names,tris):
        if sel and not sel(n): continue
        if view=='side' and SIDE_ROOT and n.split('.')[0].split('_')[0] in ('Wing','Aileron','Spoiler'):
            T=T[np.abs(T[...,1]).max(1)<0.62]   # drawing's side view shows only the wing root (no dihedral rise)
            if len(T)==0: continue
        P=views[view](T.reshape(-1,3)).reshape(-1,3,2)*S
        for t in P: d.polygon([tuple(p) for p in t],fill=255)
    return im
crops={'side':(200,90,780,320),'top':(0,535,1024,793),'front':(0,0,1024,200),
       'side_nose':(220,180,420,310),'side_tail':(600,90,760,270),'top_tail':(420,265,600,340),'side_root':(330,150,480,230),'top_fus':(440,250,580,720),'top_nose':(440,640,580,800),'front_fus':(400,40,620,200)}
VIEW={'top_nose':'top','top_fus':'top','side_root':'side','side_nose':'side','side_tail':'side','top_tail':'top','front_fus':'front'}
SIDE_ROOT=False
for v,c in crops.items():
    SIDE_ROOT=(v=='side_root')
    mk=mask(VIEW.get(v,v)); e=np.asarray(mk.filter(ImageFilter.MaxFilter(3)))^np.asarray(mk.filter(ImageFilter.MinFilter(3)))
    base=np.asarray(ref).astype(float); m=np.asarray(mk)>0
    base[m]=base[m]*0.6+np.array([0,170,255])*0.4
    base[e>0]=[230,0,0]
    im=Image.fromarray(base.astype(np.uint8)).crop(tuple(int(x*S) for x in c)); im=im.resize((im.width*2,im.height*2)) if v in VIEW else im; im.save(f'{out}_{v}.png'); print('wrote',f'{out}_{v}.png',im.size)
