"""Independent CPU geometry measurements; no Blender required."""
import json
import numpy as np
from vr_cockpit import Glb, MODELS, ROOT, builder
from vr_phase3c import vertices, distance, CKP
from drop_card_slot import validate

def segment_hits(p,q,tris):
 a=tris[:,0];u=tris[:,1]-a;v=tris[:,2]-a;d=np.asarray(q)-p
 h=np.cross(np.broadcast_to(d,v.shape),v);det=(u*h).sum(1);ok=abs(det)>1e-12
 inv=np.divide(1,det,out=np.zeros(len(det)),where=ok);s=np.asarray(p)-a
 b=(s*h).sum(1)*inv;r=np.cross(s,u);c=(r*d).sum(1)*inv;t=(r*v).sum(1)*inv
 return bool(np.any(ok&(b>=0)&(c>=0)&(b+c<=1)&(t>1e-6)&(t<1-1e-6)))

def solid_distance(p,tris):
 # Treat a point inside a solid bolt as contact, rather than its radius as a gap.
 direction=np.array((1.,.2317,.1731));a=tris[:,0];u=tris[:,1]-a;v=tris[:,2]-a
 h=np.cross(np.broadcast_to(direction,v.shape),v);det=(u*h).sum(1)
 inv=np.divide(1,det,out=np.zeros(len(det)),where=abs(det)>1e-12)
 d=np.asarray(p)-a;b=(d*h).sum(1)*inv;r=np.cross(d,u);c=(r*direction).sum(1)*inv;t=(r*v).sum(1)*inv
 hits=t[(abs(det)>1e-12)&(b>=0)&(c>=0)&(b+c<=1)&(t>0)]
 return 0. if len(np.unique(np.round(hits,8)))%2 else distance(p,tris)

def clip(poly,axis,bound,sign):
 out=[]
 for p,q in zip(poly,poly[1:]+poly[:1]):
  a=sign*(p[axis]-bound);b=sign*(q[axis]-bound)
  if a>=0:out.append(p)
  if (a>=0)!=(b>=0):out.append(p+(q-p)*a/(a-b))
 return out

def aperture_intersection(tris,ck,ellipse):
 xf,xa,hw=ck
 candidates=(tris[:,:,2].max(1)>-.02)&(tris[:,:,2].min(1)<.7)&(tris[:,:,0].min(1)<xf)&(tris[:,:,0].max(1)>xa)&(tris[:,:,1].min(1)<hw)&(tris[:,:,1].max(1)>-hw)
 for tri in tris[candidates]:
  if tri[:,2].max()<=-.02 or tri[:,2].min()>=.7:continue
  if tri[:,0].min()>=xf or tri[:,0].max()<=xa or abs(tri[:,1]).min()>hw and np.all(tri[:,1]*tri[0,1]>0):continue
  poly=clip(list(tri),2,-.02,1);poly=clip(poly,2,.7,-1)
  if not poly:continue
  if not ellipse:
   for axis,bound,sign in [(0,xa+.00001,1),(0,xf-.00001,-1),(1,-hw+.00001,1),(1,hw-.00001,-1)]:
    poly=clip(poly,axis,bound,sign)
    if not poly:break
   if poly:return True
  else:
   # Project the z-clipped convex polygon to the normalized aperture ellipse.
   uv=[np.array(((v[0]-(xf+xa)/2)/((xf-xa)/2),v[1]/hw)) for v in poly]
   signs=[];minimum=1e9
   for p,q in zip(uv,uv[1:]+uv[:1]):
    d=q-p;dd=np.dot(d,d);t=np.clip(-np.dot(p,d)/dd,0,1) if dd else 0
    minimum=min(minimum,float(np.linalg.norm(p+t*d)))
    signs.append(float((q[0]-p[0])*(-p[1])-(q[1]-p[1])*(-p[0])))
   if minimum<1-1e-5 or (max(signs)>1e-8 and min(signs)>=-1e-8) or (min(signs)<-1e-8 and max(signs)<=1e-8):return True
 return False

def check(path):
 g=Glb(path);validate(g,path);meta=g.doc['extras']['vrCockpitPhase3c'];nodes={n['name']:n for n in g.doc['nodes']};world=g.world_matrices();idx={n['name']:i for i,n in enumerate(g.doc['nodes'])}
 assert not any('Windscreen_Frame' in n for n in nodes)
 skin=vertices(g,'Fuselage_Painted');report=dict(file=path.name,cabane=[],gear=[],screens=[])
 for group in ('cabane','gear'):
  for leg in meta[group]:
   tris=vertices(g,leg['node']);fit=vertices(g,leg['fitting']);fitting_assembly=np.concatenate([fit,vertices(g,leg['fitting']+'_Bolts')]);p=np.array(leg['bottom']);q=np.array(leg['top'])
   # Tube end-cap surfaces must reach both declared axis endpoints.
   # Cross bolts are solids: an endpoint inside a bolt is in contact.
   endpoint_fit=solid_distance(p,fitting_assembly);assert endpoint_fit<=.002,(leg['node'],endpoint_fit)
   assert distance(p,tris)<=.002 and distance(q,tris)<=.002
   padtris=fit[:nodes[leg['fitting']]['extras']['skin_pad_triangle_count']];pad=np.concatenate([padtris.reshape(-1,3),padtris.mean(1),(padtris[:,0]+padtris[:,1])/2,(padtris[:,1]+padtris[:,2])/2,(padtris[:,2]+padtris[:,0])/2]);lo=pad.min(0)-.01;hi=pad.max(0)+.01;local=skin[((skin.max(1)>=lo)&(skin.min(1)<=hi)).all(1)];assert len(local);padgap=max(distance(v,local) for v in pad);assert padgap<=.002,(leg['fitting'],padgap)
   if group=='cabane':assert np.allclose(q,[-.80 if '_Aft_' in leg['node'] else -.15,.70 if leg['node'].endswith('_L') else -.70,.62],atol=1e-7)
   if group=='gear':assert np.linalg.norm(q-builder(world[idx[leg['top_node']]][:3,3]))<1e-6
   report[group].append(dict(node=leg['node'],endpoint_fitting_mm=endpoint_fit*1000,fitting_skin_mm=padgap*1000,**({'before_endpoint_skin_gap_mm':leg['before_endpoint_skin_gap_mm'],'after_endpoint_skin_gap_mm':distance(p,skin)*1000} if group=='cabane' else {})))
 for screen in meta['screens']:
  n=nodes[screen['node']];mesh=g.doc['meshes'][n['mesh']];assert len(mesh['primitives'])==1
  v=vertices(g,screen['node']).reshape(-1,3);top=float(v[:,2].max());edge=v[np.isclose(abs(v[:,1]),abs(v[:,1]).max(),atol=1e-6),2].max();drop=top-float(edge)
  assert abs(top-screen['top_height_m'])<1e-6 and abs(drop-screen['top_edge_drop_m'])<1e-6
  # Weld coordinates and verify one connected sheet, with no separate frame components.
  unique,ids=np.unique(np.round(v,6),axis=0,return_inverse=True);adj=[set() for _ in unique]
  for face in ids.reshape(-1,3):
   for i in face:adj[i].update(face)
  seen={0};todo=[0]
  while todo:
   for i in adj[todo.pop()]-seen:seen.add(i);todo.append(i)
  assert len(seen)==len(unique)
  report['screens'].append(dict(node=screen['node'],top_height_m=top,top_edge_drop_mm=drop*1000,centre_height_mm=screen['centre_height_above_skin_m']*1000,components=1))
 assert report['screens'][0]['top_height_m']<report['screens'][1]['top_height_m']
 assert report['screens'][0]['top_edge_drop_mm']<report['screens'][1]['top_edge_drop_mm']
 # Test every new triangle against an aperture extrusion above the local coaming.
 # Padding belongs at the aperture boundary; clearance concerns its inner free volume.
 min_aperture=1e9
 for name in meta['new_nodes']:
  tris=vertices(g,name)
  for seat,ck in zip(('Front','Rear'),meta['cockpits']):
   xf,xa,hw=ck;eye=builder(world[idx['Eye_'+seat]][:3,3]) if 'Eye_'+seat in idx else np.array((-1.22 if seat=='Front' else -1.95,0,.27 if seat=='Front' else .29));panel=np.array((xf+.002,0,-.06))
   assert not segment_hits(eye,panel,tris),('eye-panel',name,seat)
   if 'Coaming' in name:continue
   assert not aperture_intersection(tris,ck,path.name=='great-lakes-like-own.glb'),('aperture',name,seat)
 report['clearance']='PASS: new triangles clipped against cockpit aperture extrusion; exact triangle/segment eye-to-panel test both seats. Not a VR gate.'
 return report
if __name__=='__main__':
 reports=[check(p) for p in sorted((MODELS/'great-lakes-like').glob('*own*.glb'))]
 (ROOT/'docs/models/vr-cockpit-phase3c-checks.json').write_text(json.dumps(reports,indent=1)+'\n')
 print('PASS',len(reports),'Great Lakes liveries');print(json.dumps(reports[0],indent=1))
