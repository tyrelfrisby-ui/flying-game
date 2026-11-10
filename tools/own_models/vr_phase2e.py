"""CPU tube budget migration; leaves node graph, rigs and P2d metadata intact."""
import numpy as np
from vr_cockpit import Glb, Shape, gl
from vr_phase2 import pitts_panels, pitts_frame, canopy, tube_path, sill, skin_y
from drop_card_slot import validate


def compact(g):
    """Discard superseded mesh accessors/buffer views, preserving referenced bytes."""
    refs=[]
    for mesh in g.doc['meshes']:
        for p in mesh['primitives']:
            refs.extend((p['attributes'],k) for k in p['attributes'])
            if 'indices' in p:refs.append((p,'indices'))
            for target in p.get('targets',[]):refs.extend((target,k) for k in target)
    for skin in g.doc.get('skins',[]):
        if 'inverseBindMatrices' in skin:refs.append((skin,'inverseBindMatrices'))
    for anim in g.doc.get('animations',[]):
        for sampler in anim['samplers']:refs.extend((sampler,k) for k in ('input','output'))
    used=sorted({o[k] for o,k in refs});mapping={old:i for i,old in enumerate(used)}
    g.doc['accessors']=[g.doc['accessors'][i] for i in used]
    for o,k in refs:o[k]=mapping[o[k]]
    views=[(a,'bufferView') for a in g.doc['accessors'] if 'bufferView' in a]
    for a in g.doc['accessors']:
        if 'sparse' in a:views.extend((a['sparse'][k],'bufferView') for k in ('indices','values'))
    views.extend((im,'bufferView') for im in g.doc.get('images',[]) if 'bufferView' in im)
    used=sorted({o[k] for o,k in views});mapping={old:i for i,old in enumerate(used)}
    oldviews=g.doc['bufferViews'];oldbin=g.bin;g.bin=bytearray();g.doc['bufferViews']=[]
    for i in used:
        v=oldviews[i].copy();start=v.get('byteOffset',0)
        g.bin.extend(b'\0'*(-len(g.bin)%4));v['byteOffset']=len(g.bin)
        g.bin.extend(oldbin[start:start+v['byteLength']]);g.doc['bufferViews'].append(v)
    for o,k in views:o[k]=mapping[o[k]]


def update(path):
    g=Glb(path)
    if g.doc.get('extras',{}).get('vrCockpitPhase2e'):return
    if path.parent.name=='pitts-s2b-like':
        # Capture shared procedural shapes without editing transforms or rig extras.
        shapes={}
        class Capture:
            def __init__(self):self.g=Glb(path)
            def node(self,n,*args,shape=None,**kwargs):
                if shape is not None:shapes[n]=shape
            def rigged(self,*args,**kwargs):pass
            def idx(self,n):return next(i for i,o in enumerate(self.g.doc['nodes']) if o['name']==n)
        a=Capture();pitts_panels(a);pitts_frame(a);canopy(a)
        for side,sy in [('L',1),('R',-1)]:
            sh=Shape();pts=tube_path([(x,sy*(skin_y(x,sill(x))-.004),sill(x)-.025) for x in np.linspace(-.13,-1.60,65)])
            for u,v in zip(pts,pts[1:]):sh.tube(u,v,.009,10)
            shapes['Canopy_Sill_Airframe_'+side]=sh
        worlds=g.world_matrices()
        for i,n in enumerate(g.doc['nodes']):
            name=n['name']
            if name not in shapes or not name.startswith(('Panel_Rim_','Canopy_Bow_','Canopy_Sill_Airframe_','Canopy_Frame')):continue
            sh=shapes[name];inv=np.linalg.inv(worlds[i]);v=np.array([gl(p) for p in sh.v])
            local=v@inv[:3,:3].T+inv[:3,3]
            mat=g.doc['meshes'][n['mesh']]['primitives'][0]['material']
            g.mesh(name,local,sh.t,mat)
    else:
        # 5 mm socket centreline radius: eight chords give 0.381 mm sagitta.
        for n in g.doc['nodes']:
            if not n['name'].startswith('Canopy_Latch_Hole_'):continue
            pr=g.doc['meshes'][n['mesh']]['primitives'][0]
            c=g.array(pr['attributes']['POSITION']).mean(0)
            sh=Shape()
            pts=[c+np.array((.005*np.cos(t),0,.005*np.sin(t))) for t in np.linspace(0,2*np.pi,9)]
            # Socket lies in builder XY / glTF XZ. Work directly in local glTF.
            for u,v in zip(pts,pts[1:]):sh.tube(u,v,.0015,8)
            g.mesh(n['name'],sh.v,sh.t,pr['material'])
    g.doc.setdefault('extras',{})['vrCockpitPhase2e']={'tube_chord_error_m':.0009}
    compact(g);validate(g,path);g.save(path)
