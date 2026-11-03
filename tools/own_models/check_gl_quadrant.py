"""Validate Great Lakes console, lever travel and linkage contracts.

Checks rig direction, one-degree sampled environment clearance, continuous
full-mesh separation from aft console hardware, independent free shaft/grip
lateral clearance, IAC sightlines, linked trim and right-wall fuel controls.
Pivot bosses and the intentional slotted gate are excluded from the free-lever
clearance test. No runtime or pilot approval is implied.
Usage: /usr/bin/python3 tools/own_models/check_gl_quadrant.py
"""
import json,math,sys
import numpy as np
from vr_cockpit import Glb,MODELS,ROOT,builder
import gl_quadrant_v3 as q

def pt_tri(p,T):
 """distance from point p to each triangle in T (n,3,3)."""
 a,b,c=T[:,0],T[:,1],T[:,2];ab=b-a;ac=c-a;n=np.cross(ab,ac);nn=np.linalg.norm(n,axis=1);ok=nn>1e-12
 n=n/np.maximum(nn,1e-12)[:,None];d=np.einsum('ij,ij->i',p-a,n);pr=p-d[:,None]*n
 def bary(x):
  v0,v1,v2=ab,ac,x-a;d00=(v0*v0).sum(1);d01=(v0*v1).sum(1);d11=(v1*v1).sum(1);d20=(v2*v0).sum(1);d21=(v2*v1).sum(1)
  den=np.maximum(d00*d11-d01*d01,1e-18);v=(d11*d20-d01*d21)/den;w=(d00*d21-d01*d20)/den;return 1-v-w,v,w
 u,v,w=bary(pr);inside=(u>=0)&(v>=0)&(w>=0)&ok
 def seg(p,s0,s1):
  e=s1-s0;t=np.clip(np.einsum('ij,ij->i',p-s0,e)/np.maximum((e*e).sum(1),1e-18),0,1);return np.linalg.norm(p-(s0+t[:,None]*e),axis=1)
 de=np.minimum(np.minimum(seg(p,a,b),seg(p,b,c)),seg(p,c,a))
 return np.where(inside,np.abs(d),de)

def main():
 g=Glb(q.GLB);N=g.doc['nodes'];names={n['name']:i for i,n in enumerate(N)};W=g.world_matrices()
 rig={r['node']:r for r in json.loads(q.RIG.read_text())['rig']};fails=[]
 def tris(i):
  out=[]
  for p in g.doc['meshes'][N[i]['mesh']]['primitives']:
   v=g.array(p['attributes']['POSITION'])@W[i][:3,:3].T+W[i][:3,3];v=np.array([builder(x) for x in v])
   ids=g.array(p['indices']).reshape(-1,3) if 'indices' in p else np.arange(len(v)).reshape(-1,3);out.append(v[ids])
  return np.concatenate(out)
 for seat,s in q.SEATS.items():
  own={'Throttle_'+s['sfx'],'GL_Quadrant_'+s['sfx']}
  for lab in ('Prop','Mixture'):own|={'Ctl_PushPull_'+lab+s['ctl'],'Mount_'+lab+s['ctl']}
  lo=np.array((s['px']-.2,.15,s['zp']-.1));hi=np.array((s['px']+.2,.45,s['ztop']+.25));env=[]
  for i,n in enumerate(N):
   if 'mesh' not in n or i not in W or n['name'] in own or n['name'].startswith(('Grip_','Poke_')):continue
   if n['name'].startswith(('Throttle_','Ctl_PushPull_Prop','Ctl_PushPull_Mixture','Mount_Prop','Mount_Mixture','GL_Quadrant_')):continue
   t=tris(i);m=(t.max(1)>=lo).all(1)&(t.min(1)<=hi).all(1)
   if m.any():env.append(t[m])
  env=np.concatenate(env);worst=(9,None)
  for lv in q.LEVERS:
   label,y,L,km,r,_,_=lv;node='Throttle_'+s['sfx'] if label=='Throttle' else 'Ctl_PushPull_'+label+s['ctl']
   row=rig.get(node)
   if row is None or row['kind']!='rot' or row['axis_gltf']!=[0.0,0.0,1.0] or row['map']!=[[0.0,0.0],[1.0,50.0]]:fails.append((seat,node,'rig row',row and row['map']));continue
   if label=='Throttle' and row['param']!='throttle':fails.append((seat,node,'not on throttle'))
   P=builder(W[names[node]][:3,3]);gname='Grip_Throttle_'+s['full'] if label=='Throttle' else 'Grip_'+label+s['ctl']
   if gname not in names:fails.append((seat,gname,'missing grip'));continue
   kc0=builder(W[names[gname]][:3,3])
   k1=P+q.rot(kc0-P,50)
   if not k1[0]>kc0[0]+.05:fails.append((seat,node,'full is not forward of idle',kc0,k1))
   if not (k1[1]>.2 and kc0[1]>.2):fails.append((seat,node,'not on the LEFT side'))
   for ang in range(0,51):
    d=q.lever_dir(ang);top=P+L*d;kc=P+q.rot(kc0-P,ang)
    zs=np.linspace(0,1,12);shaft=[P+t*(top-P) for t in zs if (P+t*(top-P))[2]>s['ztop']+.002]
    for pt,rad in [(x,.0045) for x in shaft]+[(kc,.022 if label=='Prop' else .013 if label=='Mixture' else r)]:
     dd=pt_tri(np.asarray(pt),env).min()-rad
     if dd<worst[0]:worst=(dd,(label,ang))
  if worst[0]<.003:fails.append((seat,'lever sweep clearance',worst))
  print(seat,'quadrant min clearance over sweep %.1f mm'%(worst[0]*1000),worst[1])
  # Exact moving mesh X extrema over the continuous rotation, including paddle/decal.
  # A separating X plane proves clearance from the box AND all attached hardware.
  fixed=[]
  for nm in ['GL_SwitchStrip_'+s['sfx'],'GL_ConsoleHardware_'+s['sfx']]+[c['node'] for c in g.doc['extras']['vrCockpitPhase3a']['controls'] if c['seat']==seat and c['param'] not in ('throttle','prop','mixture','trim','fuel_valve','magneto') and c.get('pivot_gltf') and builder(np.array(c['pivot_gltf']))[1]>.25]:
   if nm in names and 'mesh' in N[names[nm]]:fixed.append(tris(names[nm]).reshape(-1,3))
  aftmax=np.concatenate(fixed)[:,0].max();moving_min=9
  for lv in q.LEVERS:
   nm='Throttle_'+s['sfx'] if lv[0]=='Throttle' else 'Ctl_PushPull_'+lv[0]+s['ctl']
   P=np.array((s['px'],lv[1],s['zp']));v=tris(names[nm]).reshape(-1,3)-P
   for x,y,z in v:
    angles=[0.,math.radians(50)]
    base=math.atan2(z,x)
    angles += [base+k*math.pi for k in range(-2,3) if 0<=base+k*math.pi<=math.radians(50)]
    moving_min=min(moving_min,min(P[0]+x*math.cos(a)+z*math.sin(a) for a in angles))
  # Rotation preserves Y: disjoint lateral mesh slabs prove no lever-to-lever
  # contact even when the three inputs move independently to different values.
  slabs=[]
  for lv in q.LEVERS:
   nm='Throttle_'+s['sfx'] if lv[0]=='Throttle' else 'Ctl_PushPull_'+lv[0]+s['ctl']
   # Pivot bosses occupy the shared mounting gate; compare free shafts/grips.
   node=N[names[nm]];ys=[]
   for prim in g.doc['meshes'][node['mesh']]['primitives']:
    if g.doc['materials'][prim['material']]['name'] in ('Cockpit_quadrant','GL_Placard_Atlas'):continue
    v=g.array(prim['attributes']['POSITION'])@W[names[nm]][:3,:3].T+W[names[nm]][:3,3]
    ys.extend(builder(vv)[1] for vv in v)
   slabs.append((min(ys),max(ys)))
  lever_gap=min(slabs[i][0]-slabs[i+1][1] for i in range(2))
  assert lever_gap>.003,(seat,'independent lever sweep slabs',lever_gap)
  print(seat,'independent lever full-travel lateral separation %.1f mm'%(lever_gap*1000))
  gap=moving_min-aftmax
  assert gap>.003,(seat,'console swept mesh separation',gap)
  print(seat,'continuous full-mesh sweep / console X separation %.1f mm'%(gap*1000))
  C=g.doc['extras']['vrCockpitPhase3a']['controls']
 # The compact consoles must neither touch the IAC holder nor mask its face from either eye.
 boxes=[tris(names['GL_SwitchStrip_'+s['sfx']]).reshape(-1,3) for s in q.SEATS.values()]
 card=tris(names['SequenceCard_Face']).reshape(-1,3)
 holder=tris(names['SequenceCard_Holder']).reshape(-1,3)
 for box in boxes:
  lo,hi=box.min(0),box.max(0)
  assert np.any((holder.max(0)<lo)|(holder.min(0)>hi)),'console touches card holder bounds'
  for seat in q.SEATS:
   eye=builder(W[names['Eye_'+seat]][:3,3])
   for target in card:
    delta=target-eye
    t0=(lo-eye)/np.where(abs(delta)>1e-12,delta,1e-12)
    t1=(hi-eye)/np.where(abs(delta)>1e-12,delta,1e-12)
    assert max(0,np.minimum(t0,t1).max())>min(1,np.maximum(t0,t1).min()),(seat,'console occludes card')
 print('IAC holder bounds and both eye-to-card face sightlines clear of console boxes')
 q.check_row(g)
 # Wall trim contract: linked translation, ND forward, NU aft; no fuel valve mesh or binding.
 for seat,s in q.SEATS.items():
  c=next(c for c in C if c['seat']==seat and c['param']=='trim');r=rig[c['node']]
  p=builder(np.array(c['pivot_gltf']))
  if not(p[1]>.30 and -.35<p[2]<-.20 and r['kind']=='move' and r['map']==[[-1,-.06],[0,0],[1,.06]] and r['axis_gltf']==[1.,0.,0.]):fails.append((seat,'wall trim contract'))
  for nm in ('Trim_'+s['sfx'],'FuelValve_'+s['sfx'],'Mount_FuelSelector'+s['ctl']):
   if 'mesh' in N[names[nm]]:fails.append((seat,nm,'obsolete mesh'))
 for seat,s in q.SEATS.items():
  nm='Ctl_Rotary_FuelSelector'+s['ctl'];r=rig[nm];p=builder(np.array(r['pivot_gltf']))
  if not(p[1]<-.30 and abs(p[0]-s['px'])<1e-6 and r['param']=='fuel_valve' and r['map']==[[0,-90],[1,0]] and r['model']==1 and r['axis_gltf']==[1.,0.,0.]):fails.append((seat,'fuel valve linkage'))
 for nm in ['GL_TrimCord_Run','GL_TrimCord_Fairleads','GL_FuelValve_SharedRod','GL_FuelValve_RodGuides']:
  if 'mesh' not in N[names[nm]]:fails.append((nm,'missing linkage geometry'))
 print('FAIL' if fails else 'OK',*fails,sep='\n ')
 return not fails
if __name__=='__main__':sys.exit(0 if main() else 1)
