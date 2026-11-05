"""Independent exported-world geometry measurements for the Blanc wing and gear."""
import numpy as np
from glb_visuals import Glb

def parts(path):
 g=Glb(path);w=g.world_matrices();out={};tri={}
 for i,n in enumerate(g.doc['nodes']):
  if 'mesh' not in n:continue
  vertices=[];triangles=[]
  for p in g.doc['meshes'][n['mesh']]['primitives']:
   v=g.array(p['attributes']['POSITION'])@w[i][:3,:3].T+w[i][:3,3]
   ids=g.array(p['indices']).reshape(-1,3)
   vertices.extend(v);triangles.extend(v[ids])
  out[n['name']]=np.array(vertices);tri[n['name']]=np.array(triangles)
 return out,tri

def wing_area(tri):
 t=np.concatenate([v for n,v in tri.items() if n.startswith(('Wing_Blanc','Aileron_','Spoiler_'))]);area=0
 for sign in [-1,1]:
  edges=np.concatenate([t[:,[0,1]],t[:,[1,2]],t[:,[2,0]]]);edges=edges.copy();edges[:,:,2]*=sign
  tip=edges[:,:,2].max();ys=np.linspace(.45,tip,2001);chords=[]
  lo=edges[:,:,2].min(1);hi=edges[:,:,2].max(1);valid=hi-lo>1e-9
  for y in ys:
   e=edges[(lo<=y)&(hi>=y)&valid]
   x=e[:,0,0]+(y-e[:,0,2])/(e[:,1,2]-e[:,0,2])*(e[:,1,0]-e[:,0,0])
   chords.append(float(np.ptp(x)) if len(x) else 0)
  area+=float(np.trapezoid(chords,ys)+.45*chords[0])
 return area

if __name__=='__main__':
 import sys,json
 for p in sys.argv[1:]:
  v,t=parts(p);allv=np.concatenate(list(v.values()))
  print(json.dumps(dict(file=p,span_m=float(np.ptp(allv[:,2])),wing_area_m2=wing_area(t),contacts_author={k:[float(-(v[k+'_Wheel'][:,0].min()+v[k+'_Wheel'][:,0].max())/2),0.,float(v[k+'_Wheel'][:,1].min())] for k in ['Nose','Main','Tail']}),indent=2))
