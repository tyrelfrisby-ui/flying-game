"""Check gl_quadrant_v3's output: the Great Lakes throttle quadrant and the single labelled switch row.

  * Throttle_R/F, Ctl_PushPull_Prop/Mixture(_Front) are quadrant levers on the LEFT sidewall, rig kind rot about
    glTF +z with 0 -> 0 and 1 -> 50 deg, so param 1 (full / fine / rich) puts the knob FORWARD of param 0.
  * Grip_Throttle_Rear/Front exist under the throttle levers at the knob (the VrCockpitHands contract).
  * Over the whole sweep, every lever's shaft and knob stays >= 3 mm from all other cockpit geometry (tubes,
    sidewall, panel, card, stick, switch row) and inboard of the sidewall.
  * The ten non-radio controls per seat sit on ONE row (same z), on the switch strip, each with a placard.
Usage: python3 check_gl_quadrant.py   (Blender's python; no Unity)
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
   for ang in range(0,51,5):
    d=q.lever_dir(ang);top=P+L*d;kc=P+q.rot(kc0-P,ang)
    zs=np.linspace(0,1,12);shaft=[P+t*(top-P) for t in zs if (P+t*(top-P))[2]>s['ztop']+.002]
    for pt,rad in [(x,.0045) for x in shaft]+[(kc,r)]:
     dd=pt_tri(np.asarray(pt),env).min()-rad
     if dd<worst[0]:worst=(dd,(label,ang))
  if worst[0]<.003:fails.append((seat,'lever sweep clearance',worst))
  print(seat,'quadrant min clearance over sweep %.1f mm'%(worst[0]*1000),worst[1])
  # switch row
  C=g.doc['extras']['vrCockpitPhase3a']['controls'];zs=[]
  for lab in q.ROW:
   c=next(c for c in C if c['seat']==seat and c['grip'][5:].replace('_Front','')==lab);p=builder(np.array(c['pivot_gltf']));zs.append(p[2])
   if abs(p[0]-(s['X']-.001))>1e-4:fails.append((seat,lab,'not on panel face'))
   z0,z1=s['strip'];ya,yb=s['strip_y']
   if not(z0+.016<=p[2]<=z1 and ya+.016<=p[1]<=yb-.016):fails.append((seat,lab,'off the strip',p))
   if not c.get('label'):fails.append((seat,lab,'no placard label'))
  if max(zs)-min(zs)>1e-6:fails.append((seat,'switch row not single',zs))
 print('FAIL' if fails else 'OK',*fails,sep='\n ')
 return not fails
if __name__=='__main__':sys.exit(0 if main() else 1)
