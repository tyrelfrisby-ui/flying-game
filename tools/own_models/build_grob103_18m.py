"""Deterministic v2 conversion of an exported Blanc v1 asset, without a GPU.

First run build_grob103_blanc.py for fresh v1 inputs, then this script. --source-dir
can point at a preserved v1 pair plus grob103-own-rig.json. Never rescale span.
Derived Florian Blanc mesh: CC BY-SA 4.0; see asset CREDITS.md.
"""
import argparse,copy,hashlib,json,math
from pathlib import Path
import numpy as np
from glb_visuals import Glb
from grob103_metrics import parts,wing_area
from build_grob103 import ROOT,OUT,ID
DEST=ROOT/'renders/grob103/blanc-18m'

def split(poly,axis,offset):
 result=[[],[]]
 for a,b in zip(poly,poly[1:]+poly[:1]):
  da=float(a[:3]@axis-offset);db=float(b[:3]@axis-offset)
  result[da<0].append(a)
  if (da<0)!=(db<0):
   v=a+(b-a)*da/(da-db);result[0].append(v);result[1].append(v)
 return [p for p in result if len(p)>=3]

def replace(g,node,vertices,pivot):
 a=np.asarray(vertices,dtype=float);a[:,:3]-=pivot
 attrs={key:g.accessor(a[:,sl],kind) for key,sl,kind in [('POSITION',slice(0,3),'VEC3'),('NORMAL',slice(3,6),'VEC3'),('TEXCOORD_0',slice(6,8),'VEC2')]}
 a[:,3:6]/=np.maximum(np.linalg.norm(a[:,3:6],axis=1)[:,None],1e-12)
 attrs['NORMAL']=g.accessor(a[:,3:6],'VEC3')
 prim=copy.deepcopy(g.doc['meshes'][node['mesh']]['primitives'][0]);prim.update(attributes=attrs,indices=g.accessor(np.arange(len(a)).reshape(-1,1),'SCALAR',5125))
 g.doc['meshes'][node['mesh']]['primitives']=[prim];node['translation']=list(map(float,pivot));node.pop('rotation',None);node.pop('scale',None);node.pop('matrix',None)

def box(g,name,centre,size,material):
 vertices=np.array([[x,y,z] for x in [-1,1] for y in [-1,1] for z in [-1,1]])*np.array(size)/2+centre
 faces=[[0,1,3],[0,3,2],[4,6,7],[4,7,5],[0,4,5],[0,5,1],[2,3,7],[2,7,6],[0,2,6],[0,6,4],[1,5,7],[1,7,3]]
 g.mesh(name,vertices,faces,material,np.zeros((8,2)))
 return len(g.doc['nodes'])-1

def convert(path,rig):
 g=Glb(path);before,tri=parts(path);area=wing_area(tri);factor=17.5/area;nodes=g.doc['nodes'];by={n['name']:n for n in nodes};world=g.world_matrices()
 assert len(rig['rig'])==29,'Requires v1 input; regenerate it with build_grob103_blanc.py'
 rows={r['node']:r for r in rig['rig']};wingnames=[n for n in by if 'mesh' in by[n] and n.startswith(('Wing_Blanc','Aileron_','Spoiler_'))];out={n:[] for n in wingnames}
 for i,n in enumerate(nodes):
  if n['name'] not in wingnames:continue
  for prim in g.doc['meshes'][n['mesh']]['primitives']:
   v=g.array(prim['attributes']['POSITION'])@world[i][:3,:3].T+world[i][:3,3];v[:,0]*=factor
   normal=g.array(prim['attributes']['NORMAL'])@np.linalg.inv(world[i][:3,:3]);normal[:,0]/=factor;normal/=np.linalg.norm(normal,axis=1)[:,None]
   a=np.c_[v,normal,g.array(prim['attributes']['TEXCOORD_0'])]
   for ids in g.array(prim['indices']).reshape(-1,3):
    pieces=[list(a[ids])];sign=1 if v[ids,2].mean()>0 else -1
    planes=[(np.array([0,0,sign]),y) for y in [2.43,3.91,5.17,8.68]]+[(np.array([1,0,0]),x*factor) for x in [.53,.64]]+[(np.array([1,0,.073*sign*factor]),(.8+.073*4.501)*factor)]
    for axis,offset in planes:
     pieces=[q for poly in pieces for q in split(poly,axis,offset)]
    for poly in pieces:
     p=np.mean(poly,axis=0);side='L' if sign>0 else 'R';y=abs(p[2]);up=np.cross(poly[1][:3]-poly[0][:3],poly[2][:3]-poly[0][:3])[1]>0
     name=('Aileron_' if 5.17<y<8.68 and p[0]>(.8-.073*(y-4.501))*factor else 'Spoiler_' if 2.43<y<3.91 and .53*factor<p[0]<.64*factor and up else 'Wing_Blanc_')+side
     for j in range(1,len(poly)-1):
      t=np.array([poly[0],poly[j],poly[j+1]])
      if np.linalg.norm(np.cross(t[1,:3]-t[0,:3],t[2,:3]-t[0,:3]))>1e-12:out[name].extend(t)
 for name,vertices in out.items():
  pivot=np.array(by[name].get('translation',[0,0,0]),float)
  if name.startswith(('Aileron','Spoiler')):
   sign=1 if name.endswith('L') else -1;y=5.17 if name.startswith('Aileron') else 3.17;x=(.8-.073*(y-4.501))*factor if name.startswith('Aileron') else .585*factor
   a=np.array(vertices);near=np.argsort(np.linalg.norm(a[:,[0,2]]-[x,sign*y],axis=1))[:12];pivot=np.array([x,float(a[near,1].mean()),sign*y])
   row=rows[name];axis=np.array(row['axis_gltf']);axis[0]*=factor;axis/=np.linalg.norm(axis);row.update(pivot_gltf=pivot.tolist(),axis_gltf=axis.tolist())
   by[name]['extras']['rig_move_axis_gltf' if row['kind']=='move' else 'rig_axis_gltf']=axis.tolist();by[row['axis_marker']]['translation']=(pivot+axis).tolist()
  replace(g,by[name],vertices,pivot)
 # Fixed gear stations and common nose/main lower tangent. Keep tyre dimensions.
 for label,x,z in [('Nose',1.4595,-.60),('Main',-.5397,-.60),('Tail',-4.8768,-.26)]:
  a=before[label+'_Wheel'];centre=(a.min(0)+a.max(0))/2;delta=np.array([-x-centre[0],z-a[:,1].min(),0])
  for suffix in ['_Wheel','_Hub','_Fairing']:
   n=by[label+suffix];n['translation']=(np.array(n.get('translation',[0,0,0]))+delta).tolist()
 # Separate steering pivot and one assembly child; fillet remains part of skin.
 pivot=np.array([-1.4595,-.43,0]);pi=len(nodes);ai=pi+1
 nodes.append(dict(name='Nose_Steering',translation=pivot.tolist(),children=[ai],extras=dict(rig_param='rudder',rig_axis_gltf=[0,1,0],rig_map_param=[-1,0,1],rig_map_deg=[-25,0,25],rig_param_model=0.,rig_unit='deg',rig_estimated=True)))
 nodes.append(dict(name='Nose_Steering_Assembly',children=[]));roots=g.doc['scenes'][0]['nodes'];roots.append(pi)
 metal=g.material('Nose steering fork metal',(.22,.24,.26))
 for sign in [-1,1]:
  idx=box(g,'Nose_Fork_'+str(sign),[-1.4595,-.46,sign*.049],[.035,.16,.012],metal);roots.remove(idx);nodes[ai]['children'].append(idx);nodes[idx]['translation']=(-pivot).tolist()
 for name in ['Nose_Wheel','Nose_Hub']:
  idx=next(i for i,n in enumerate(nodes) if n['name']==name);roots.remove(idx);nodes[ai]['children'].append(idx);nodes[idx]['translation']=(np.array(nodes[idx].get('translation',[0,0,0]))-pivot).tolist()
 marker='Nose_Steering_HingeAxis';nodes.append(dict(name=marker,translation=(pivot+[0,1,0]).tolist()));roots.append(len(nodes)-1)
 rig['rig'].append(dict(node='Nose_Steering',parent=None,param='rudder',unit='-1..+1',kind='rot',axis_gltf=[0,1,0],pivot_gltf=pivot.tolist(),map=[[-1,-25],[0,0],[1,25]],model=0.,desc='ESTIMATED +/-25 degrees; pedal linked, no published travel',link='',extra={'rig_estimated':True},axis_marker=marker))
 # Rounded wingtip skids: ellipsoids with smooth normals, under the actual lower skin.
 rubber=g.doc['meshes'][by['Nose_Wheel']['mesh']]['primitives'][0]['material']
 for sign in [-1,1]:
  # Find lower skin at the skid station from source section vertices.
  a=np.concatenate([np.array(v)[:,:3] for k,v in out.items() if k.endswith('L' if sign>0 else 'R')]);near=a[abs(a[:,2]-sign*8.9)<.05];x=float((near[:,0].min()+near[:,0].max())/2);z=float(near[:,1].min())-.012
  v=[];f=[]
  for j in range(13):
   phi=math.pi*j/12
   for k in range(24):
    theta=2*math.pi*k/24;v.append([x+.10*math.sin(phi)*math.cos(theta),z+.025*math.cos(phi),sign*8.9+.035*math.sin(phi)*math.sin(theta)])
  for j in range(12):
   for k in range(24):
    a=j*24+k;b=j*24+(k+1)%24;c=b+24;d=a+24;f.extend([[a,b,c],[a,c,d]])
  g.mesh('Tip_Skid_'+('L' if sign>0 else 'R'),v,f,rubber,np.zeros((len(v),2)))
 g.doc['extras'].update(modelVersion='18m-v2',wingAreaM2=17.5,steeringTravel='ESTIMATED +/-25 deg',visualApproval='pending')
 return g,dict(input_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),input_span_m=float(np.ptp(np.concatenate(list(before.values()))[:,2])),input_area_m2=area,chord_scale=factor,area_method='section chord integral; root to .45m extrapolated; exported mesh independent check',license='CC BY-SA 4.0',steering_travel='ESTIMATED +/-25 degrees; no published figure')

def run(source):
 DEST.mkdir(parents=True,exist_ok=True);rigpath=(source/'grob103-own-rig.json') if source else ROOT/'docs/models/grob103-own-rig.json';original=json.loads(rigpath.read_text());manifest={}
 for suffix in ['', '-tf']:
  path=(source or OUT)/(ID+'-own'+suffix+'.glb');rig=copy.deepcopy(original);g,report=convert(path,rig);dest=OUT/path.name;g.save(dest);report['sha256']=hashlib.sha256(dest.read_bytes()).hexdigest();manifest[path.name]=report
 (ROOT/'docs/models/grob103-own-rig.json').write_text(json.dumps(rig,indent=2)+'\n');(DEST/'build-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--source-dir',type=Path);run(p.parse_args().source_dir)
