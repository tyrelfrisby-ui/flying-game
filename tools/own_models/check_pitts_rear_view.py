"""Pitts S-2B rear cockpit v3.1 (Ty 2026-11-03: "lower the panel 2 inches and make the tubes conform to the panel
so there is less forward obstruction"). Builder metres: X forward, Y left, Z up.

Run: PYTHONDONTWRITEBYTECODE=1 /usr/bin/python3 tools/own_models/check_pitts_rear_view.py [file.glb ...]
(default: all four Pitts liveries). With --view only the centreline view over the nose is reported, for any GLB
(e.g. a `git show` of the previous commit), without the v3.1 assertions.

Asserts, on every livery:
 1. The rear panel rim top is where v3.1 put it: 0.2190 m (0.2968 before v3, less 27 mm v3, less 50.8 mm v3.1).
    Literal, copied from the change, not read back from the builder.
 2. Nothing of the rear panel assembly (panel, rim, extension, gauges, card, placards, faceplates, every rear
    Ctl_/Mount_), the two rear bows, the latch, or the release cable pokes out of the fuselage: below the sill each
    vertex is inside the loft (skin_y), above it within the skin's width at the sill; nothing below the floor.
 3. The panel and its extension stay clear of the rear stick's full control envelope (pitch -12..18, roll +-18),
    of the rear pedals, and of the rear pilot's legs (SeatedPilot hip to the Foot_ grips, 60 mm radius).
 4. From the rear eye, the two rear bows never rise above the lowered rim's silhouette (0.5 mm tolerance).
Reports the centreline view: the cowling's top edge angle and the occluder edge below which nothing of the nose
is visible, and the angle subtended by the latch knob (the one part allowed above the panel line: it is a handle).
"""
import math,sys
from pathlib import Path
import numpy as np
from vr_cockpit import Glb,MODELS,builder,skin_y,sill,sec
from check_vr_phase1 import triangles

EYE=np.array((-1.325,0,.300))
RIM_TOP_V31=.2190
PANEL_PARTS=('RearPanel_','Panel_Rim_Rear','SequenceCard_','Pitts_RearPanel_Extension','Pitts_Switch_Placards_Rear','Pitts_Radio_Faceplates_Rear')
CANOPY_PARTS=('Canopy_Bow_F','Canopy_Bow_R','Canopy_Latch_Block','Canopy_Latch_Bracket','Canopy_Latch_Pin','Canopy_Latch_Knob','Canopy_Latch_Placard','Pitts_Release_Cable_Left')  # the front-left release knob sits ON the sill rail by design

def meshes(g,prefixes,exclude_front=True):
 out={}
 for n in g.doc['nodes']:
  name=n['name']
  if 'mesh' not in n or not name.startswith(prefixes):continue
  if exclude_front and name.endswith('_Front'):continue
  out[name]=triangles(g,name)
 return out

def rear_controls(g):
 return {n['name']:triangles(g,n['name']) for n in g.doc['nodes'] if 'mesh' in n and n['name'].startswith(('Ctl_','Mount_'))
         and not n['name'].endswith('_Front')}

def containment(parts):
 worst=(1e9,None)
 for name,t in parts.items():
  v=t.reshape(-1,3)
  for x,y,z in v:
   s=sill(x)
   gap=skin_y(x,min(z,s))-abs(y)   # above the sill: within the skin's width at the sill (the canopy sits on it)
   gap=min(gap,z-sec(x)[1])
   if gap<worst[0]:worst=(gap,name)
 return worst

def stick_envelope(g):
 local=triangles(g,'Stick_Rear').reshape(-1,3)-np.array((-.93,0,-.58));lo=np.full(3,np.inf);hi=-lo
 for pitch in np.linspace(-12,18,31):
  t=math.radians(-pitch);ry=np.array([[math.cos(t),0,math.sin(t)],[0,1,0],[-math.sin(t),0,math.cos(t)]])
  for roll in np.linspace(-18,18,37):
   t=math.radians(roll);rx=np.array([[1,0,0],[0,math.cos(t),-math.sin(t)],[0,math.sin(t),math.cos(t)]])
   p=local@(rx@ry).T+np.array((-.93,0,-.58));lo=np.minimum(lo,p.min(0));hi=np.maximum(hi,p.max(0))
 return lo,hi

def box_gap(v,lo,hi):return float(np.linalg.norm(np.maximum(np.maximum(lo-v,v-hi),0),axis=1).min())

def seg_gap(v,a,b):
 d=b-a;t=np.clip((v-a)@d/(d@d),0,1);return float(np.linalg.norm(v-(a+t[:,None]*d),axis=1).min())

def world(g,name):
 i=next(i for i,n in enumerate(g.doc['nodes']) if n['name']==name);return builder(g.world_matrices()[i][:3,3])

def centreline_view(g,yaw_deg=0.):
 """First hit along rays from the rear eye in the vertical plane at yaw_deg, pitch 0 .. -15 deg (0.05 deg)."""
 tris=[];owner=[];names=[]
 for n in g.doc['nodes']:
  if 'mesh' not in n:continue
  t=triangles(g,n['name']);mats={g.doc['materials'][p['material']]['name'] for p in g.doc['meshes'][n['mesh']]['primitives']}
  if 'Glass' in mats and len(mats)==1:continue   # canopy and windscreen are clear
  keep=(t[:,:,0].max(1)>EYE[0]+.05)&(np.abs(t[:,:,1]).min(1)<.6)&(t[:,:,2].max(1)>-.2)&(t[:,:,2].min(1)<.7)
  if keep.any():tris.append(t[keep]);owner+= [len(names)]*int(keep.sum());names.append(n['name'])
 T=np.concatenate(tris);owner=np.array(owner);e1=T[:,1]-T[:,0];e2=T[:,2]-T[:,0]
 rows=[]
 yaw=math.radians(yaw_deg)
 for pitch in np.arange(0,-15.0001,-.05):
  p=math.radians(pitch);d=np.array((math.cos(p)*math.cos(yaw),-math.cos(p)*math.sin(yaw),math.sin(p)))
  h=np.cross(d,e2);det=(e1*h).sum(1);ok=np.abs(det)>1e-12;inv=np.where(ok,1/np.where(ok,det,1),0)
  s=EYE-T[:,0];u=(s*h).sum(1)*inv;q=np.cross(s,e1);v=(q@d)*inv;dist=(e2*q).sum(1)*inv
  hit=ok&(u>=0)&(v>=0)&(u+v<=1)&(dist>1e-4)
  if not hit.any():rows.append((pitch,None,None));continue
  k=np.argmin(np.where(hit,dist,np.inf));P=EYE+dist[k]*d;rows.append((pitch,names[owner[k]],P))
 nose=[r for r in rows if r[1]=='Fuselage_Painted' and r[2][0]>-.13]
 top=nose[0][0] if nose else None
 # Contiguous band of nose from its top edge downwards; the first ray that is NOT nose is the occluder edge.
 edge=None;blocker=None
 if nose:
  i=rows.index(nose[0])
  while i<len(rows) and rows[i][1]=='Fuselage_Painted' and rows[i][2][0]>-.13:i+=1
  if i<len(rows):edge,blocker=rows[i][0],rows[i][1]
 return dict(cowl_top_deg=top,first_occluded_deg=edge,occluder=blocker,visible_band_deg=None if top is None or edge is None else round(top-edge,2))

def hull_edges(pts):
 """Convex hull (monotone chain) of 2-D points as (point, outward unit normal) per edge, counter-clockwise."""
 P=sorted(map(tuple,np.round(pts,6)));cross=lambda o,a,b:(a[0]-o[0])*(b[1]-o[1])-(a[1]-o[1])*(b[0]-o[0])
 lo=[];hi=[]
 for p in P:
  while len(lo)>=2 and cross(lo[-2],lo[-1],p)<=0:lo.pop()
  lo.append(p)
 for p in reversed(P):
  while len(hi)>=2 and cross(hi[-2],hi[-1],p)<=0:hi.pop()
  hi.append(p)
 h=np.array(lo[:-1]+hi[:-1]);out=[]
 for a,b in zip(h,np.roll(h,-1,axis=0)):
  d=b-a;n=np.array((d[1],-d[0]))/np.linalg.norm(d);out.append((a,n))
 return out

def check(path):
 g=Glb(path);rep={}
 rim=triangles(g,'Panel_Rim_Rear').reshape(-1,3);top=float(rim[:,2].max());rep['rim_top_m']=round(top,4)
 assert abs(top-RIM_TOP_V31)<=.0005,(path.name,'rim top',top)
 panel=meshes(g,PANEL_PARTS);canopy=meshes(g,CANOPY_PARTS);controls=rear_controls(g)
 gap,name=containment({**panel,**canopy,**controls});rep['min_skin_gap_mm']=(round(gap*1000,1),name)
 assert gap>0,(path.name,'outside the fuselage',name,gap)
 pv=np.concatenate([t.reshape(-1,3) for t in panel.values()])
 lo,hi=stick_envelope(g);rep['panel_to_stick_envelope_mm']=round(box_gap(pv,lo,hi)*1000,1)
 assert rep['panel_to_stick_envelope_mm']>5,(path.name,rep)
 pedals=np.concatenate([triangles(g,n).reshape(-1,3) for n in ('Pedal_L_Rear','Pedal_R_Rear')])
 rep['panel_to_pedal_mm']=round(float(min(np.linalg.norm(pv-p,axis=1).min() for p in pedals[::7]))*1000,1)
 assert rep['panel_to_pedal_mm']>20,(path.name,rep)
 legs=[]
 for side,sy in (('L',1),('R',-1)):
  hip=EYE+np.array((-.05,sy*.09,-.70));foot=world(g,f'Foot_{side}_Rear');legs.append(seg_gap(pv,hip,foot)-.06)
 rep['panel_to_leg_mm']=round(min(legs)*1000,1);assert min(legs)>0,(path.name,rep)
 # Project every bow vertex from the eye onto the rim's plane; it must lie inside the rim's (convex) outline.
 edges=hull_edges(rim[:,1:]);over=[]
 for name in ('Canopy_Bow_F','Canopy_Bow_R'):
  for x,y,z in canopy[name].reshape(-1,3):
   if z<sill(x)+.012:continue   # the feet that run out to the sill rails are below the deck line
   k=(-.808-EYE[0])/(x-EYE[0]);q=np.array((k*y,EYE[2]+k*(z-EYE[2])))
   over.append(max(float(n@(q-a)) for a,n in edges))
 rep['bow_above_rim_silhouette_mm']=round(max(over)*1000,2);assert max(over)<=.0005,(path.name,rep)
 knob=canopy['Canopy_Latch_Knob'].reshape(-1,3)
 rep['latch_knob_top_deg']=round(math.degrees(math.atan2(knob[:,2].max()-EYE[2],knob[np.argmax(knob[:,2]),0]-EYE[0])),2)
 rep['rim_top_deg']=round(math.degrees(math.atan2(top-EYE[2],-.808-EYE[0])),2)
 rep['view']=centreline_view(g)
 return rep

if __name__=='__main__':
 args=[a for a in sys.argv[1:] if a!='--view']
 paths=[Path(a) for a in args] or sorted((MODELS/'pitts-s2b-like').glob('*-own*.glb'))
 for p in paths:
  if '--view' in sys.argv:print(p.name,centreline_view(Glb(p)))
  else:print('PASS',p.name,check(p))
