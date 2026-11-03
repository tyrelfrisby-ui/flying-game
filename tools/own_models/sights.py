"""Spec-driven sight meshes shared by the native GLB updater and Blender builders.

Body = (+forward,+right,+down). Raw glTF = (-builder X,builder Z,builder Y).
No runtime code; all positions are baked using the measured Place transform.
"""
import json
import math
from pathlib import Path
import numpy as np

ROOT=Path(__file__).resolve().parents[2]
SPEC=ROOT/'docs/models/sights-spec.json'
MODELS=ROOT/'unity/Assets/_Project/Resources/Models'

def spec(): return json.loads(SPEC.read_text())

def body(a,row):
    return np.asarray(a)@np.array(row['gltf_to_body_matrix']).T+row['gltf_to_body_translation_m']

def native(a,row):
    return np.linalg.solve(row['gltf_to_body_matrix'],(np.asarray(a)-row['gltf_to_body_translation_m']).T).T

def parts(g):
    for i,m in g.world_matrices().items():
        n=g.doc['nodes'][i]
        if 'mesh' not in n: continue
        for p in g.doc['meshes'][n['mesh']]['primitives']:
            a=g.array(p['attributes']['POSITION'])@m[:3,:3].T+m[:3,3]
            ids=g.array(p['indices']).reshape(-1,3) if 'indices' in p else np.arange(len(a)).reshape(-1,3)
            yield n['name'],p,a,ids

# AirframeModels.OwnSpecs sitHighM, copied by hand: the model is drawn this much higher than lowest-point-on-the-
# deepest-contact puts it (the J-3 on floats is held at its landplane's height; check_eye_interiors.py asserts it).
SIT_HIGH={'j3-cub-floats-like':0.0454}

def placement(g,aircraft):
    """Reproduce readable-vertex AirframeModels.Place for these non-alias own models."""
    a=np.concatenate([a for n,p,a,i in parts(g)])
    cfg=json.loads((ROOT/'configs/aircraft'/f'{aircraft}.json').read_text())
    strips=[v for f in cfg['surfaces'] if 'wing' in f['id'].lower() for v in f['strips']]
    half=max(abs(v['pos'][1]) for v in strips)
    scale=2*half/np.ptp(a[:,2])*(6.094/5.996 if aircraft=='pitts-s2b-like' else 1)
    outer=a[(abs(a[:,2]*scale)>=.65*half)&(abs(a[:,2]*scale)<=.92*half)]
    ss=[v for v in strips if .65*half<=abs(v['pos'][1])<=.92*half]
    cmid=(min(v['pos'][0]-.75*v['chord'] for v in ss)+max(v['pos'][0]+.25*v['chord'] for v in ss))/2
    tr=np.array([cmid+scale*(outer[:,0].min()+outer[:,0].max())/2,0,
                 max(v['pos'][2] for v in cfg['gear'])+scale*a[:,1].min()-SIT_HIGH.get(aircraft,0.0)])
    return scale*np.array([[-1,0,0],[0,0,-1],[0,-1,0]]),tr

def ray_hits(tris,origin,direction):
    e1=tris[:,1]-tris[:,0];e2=tris[:,2]-tris[:,0]
    h=np.cross(direction,e2);det=(e1*h).sum(1)
    inv=np.divide(1,det,out=np.zeros_like(det),where=abs(det)>1e-12)
    s=origin-tris[:,0];u=(s*h).sum(1)*inv;q=np.cross(s,e1)
    v=(direction*q).sum(1)*inv;t=(e2*q).sum(1)*inv
    return t[(abs(det)>1e-12)&(u>=-1e-8)&(v>=-1e-8)&(u+v<=1+1e-8)&(t>1e-8)]

def segments(row):
    dimensions=spec()['frame'];half=dimensions['bar_length_m']/2
    for side,sign in [('L',-1),('R',1)]:
        c=np.array(row['eye_body_m'],dtype=float);c[1]=sign*row['y_frame_m']
        for label,d in dimensions['directions_body'].items():
            d=np.array(d,dtype=float)
            yield f'Sight_{side}_{label}',c-half*d,c+half*d,dimensions['rod_diameter_m']/2

def cylinder(a,b,r):
    n=spec()['frame']['cylinder_segments']
    a,b=np.array(a),np.array(b);d=b-a;d/=np.linalg.norm(d)
    u=np.cross(d,[0,1,0] if abs(d[1])<.9 else [1,0,0]);u/=np.linalg.norm(u);v=np.cross(d,u)
    ring=[r*(u*math.cos(k*2*math.pi/n)+v*math.sin(k*2*math.pi/n)) for k in range(n)]
    vertices=[p+q for p in (a,b) for q in ring];triangles=[]
    for k in range(n):
        j=(k+1)%n;triangles.extend([(k,j,n+j),(k,n+j,n+k)])
    triangles.extend((0,k+1,k) for k in range(1,n-1))
    triangles.extend((n,n+k,n+k+1) for k in range(1,n-1))
    return vertices,triangles

def native_add(g,aircraft):
    row=spec()['aircraft'][aircraft];mat=g.material('Sight_Red',(.578,.009,.017))
    for name,a,b,r in segments(row):
        v,t=cylinder(a,b,r);g.mesh(name,native(v,row),t,mat)
    g.doc.setdefault('extras',{})['aerobaticSights']=dict(
        spec='docs/models/sights-spec.json',eyeBodyM=row['eye_body_m'],
        nodePrefix='Sight_',visibility='local-cockpit-only',defaultVisible=False)

def blender_add(objects,aircraft):
    import bpy,paint
    row=spec()['aircraft'][aircraft];mat=paint.mat_colour('Sight_Red',(200,24,36),rough=.65)
    mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.578,.009,.017,1)
    for name,a,b,r in segments(row):
        vertices,triangles=cylinder(a,b,r)
        v=native(vertices,row)
        # Inverse of native (-builder x,builder z,builder y).
        vertices=[(-x,z,y) for x,y,z in v]
        me=bpy.data.meshes.new(name);me.from_pydata(vertices,[],triangles);me.materials.append(mat)
        obj=bpy.data.objects.new(name,me);bpy.context.scene.collection.objects.link(obj);objects[name]=obj

if __name__=='__main__':
    # Rebuild every variant through the builders' existing immutable-baseline path.
    from sequence_card import offline_update
    for aircraft,row in spec()['aircraft'].items():
        for filename in row['glbs']:offline_update(aircraft,filename)
