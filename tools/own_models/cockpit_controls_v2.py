"""Cockpit controls v2 (owner Ty 2026-11-02: "the switches were outside of the great lakes on the sides").

P3a put every interactive Ctl_ control on a "side console" at panel_x+.045..+.175, i.e. FORWARD of the
instrument panel, at |y| .215-.323 -- on the Great Lakes / Pitts that is outside the fuselage skin, on the
Extra it is hidden behind the panel or ahead of the front cockpit. This script moves every control (and its
Mount_; Grip_/Poke_ children follow) onto an aft-facing panel face inside the cockpit:

  * Great Lakes: the real rear/front instrument panels (Cockpit_Interior 'Cockpit_panel' faces at x -1.638 /
    -0.968). Mag switch on the mag-key plate, COM/XPDR knobs on the avionics faceplates, engine/fuel left
    (throttle side), lights right -- per the 2T-1A-2 panel photos (greatlakes2t.co.uk/gl_pics/inst.html).
  * Pitts S-2B: rear RearPanel_Rounded face (x -.808) and front FrontPanel_Arched face (x -.151).
  * Extra 300: new lower switch subpanel under the rear gauge panel (x -1.172, below z .075 -- keeps the
    check_vr_phase3 "below the instrument rectangle" rule) and the same subpanel under the front gauge panel
    (x -.147, under the x -.150 front panel).

Placement = greedy nearest-free-slot to a per-label anchor on a 1 mm grid. A slot is legal only if the knob
footprint is on the panel outline, clear of every other mesh in the 45 mm in front of the face (gauges,
bezels, displays, tubes, canopy bows), >= 31 mm from other controls and clear of the check_vr_phase3 knee and
stick/throttle sweep volumes. Panel normal is still builder -X, so rig axes are unchanged. Idempotent:
placements are absolute and computed on each model's base GLB, then applied identically to every livery (they
share one rig json). Static duplicates of now-interactive Great Lakes controls become inert empties.
"""
import json,sys
import numpy as np
from vr_cockpit import Glb,Shape,MODELS,ROOT,gl,builder
from vr_phase3 import SPECS
from drop_card_slot import validate

RES=.001;Y0,Z0=-.5,-.3;NY,NZ=1000,800
GL_POP=('MagKey_R','Radio_TuneOuter_R','Radio_TuneInner_R','Radio_VolumeKnob_R','Xpdr_Knob_R','FuelValve_R','FuelValve_F')
# Materials of the panel itself or of plates the knobs legitimately mount on.
MOUNT_OK={'Cockpit_panel','Avionics_Face_COM','Avionics_Face_XPDR','Cockpit_blank_plug','Dial_MAG','P3e_Subpanel'}
XR=('Rear','Front')

def rect(y0,y1,z0,z1):return [((y0,z0),(y1,z0),(y1,z1)),((y0,z0),(y1,z1),(y0,z1))]
# model -> seat -> panel: face x, outline source, optional new subpanel rectangles
PANELS={
 'great-lakes-like':{'Rear':dict(x=-1.638,mat='Cockpit_panel'),'Front':dict(x=-.968,mat='Cockpit_panel')},
 'pitts-s2b-like':{'Rear':dict(x=-.808,node='RearPanel_Rounded'),'Front':dict(x=-.151,node='FrontPanel_Arched')},
 'extra-300-like':{'Rear':dict(x=-1.172,zmax=.034,new={'Subpanel_P3e_Switches_R':rect(-.32,.32,-.078,.075)}),
                   'Front':dict(x=-.147,zmax=.034,new={'Subpanel_P3e_Switches_F':rect(-.32,.32,-.078,.075)})},
}
# Anchors (builder y left+, z up). Order = placement priority (tight clusters first).
GL_REAR=dict(Magneto=(.279,.020),ComVolume=(-.030,.100),ComOuter=(-.090,.098),ComSwap=(-.059,.080),ComInner=(-.060,.128),
 XpdrMode=(-.090,.045),XpdrIdent=(-.030,.040),XpdrDigit1=(-.190,.150),XpdrDigit2=(-.159,.150),XpdrDigit3=(-.128,.150),XpdrDigit4=(-.097,.150),
 Master=(.262,.075),FuelPump=(.293,.075),Smoke=(.324,.045),CarbHeat=(.330,.005),Mixture=(.300,-.060),Prop=(.330,-.035),Trim=(.200,.135),
 NavLights=(-.235,.100),Strobe=(-.266,.100),LandingLight=(-.297,.080),FuelSelector=(-.300,-.020))
GL_FRONT=dict(Magneto=(.279,.020),Master=(.262,.075),FuelPump=(.293,.075),Smoke=(.324,.045),CarbHeat=(.330,.005),Mixture=(.300,-.035),Prop=(.255,-.035),Trim=(.200,.140),
 NavLights=(-.235,.090),Strobe=(-.266,.090),LandingLight=(-.297,.070),FuelSelector=(-.290,-.020),
 ComOuter=(-.110,-.035),ComInner=(-.080,-.035),ComVolume=(-.050,-.035),ComSwap=(-.140,-.035),
 XpdrDigit1=(.010,-.035),XpdrDigit2=(.040,-.035),XpdrDigit3=(.070,-.035),XpdrDigit4=(.100,-.035),XpdrMode=(.130,-.035),XpdrIdent=(.160,-.035))
PITTS_REAR=dict(ComVolume=(-.070,.120),ComOuter=(-.100,.120),ComInner=(-.130,.120),ComSwap=(-.040,.120),
 XpdrDigit1=(.040,.120),XpdrDigit2=(.070,.120),XpdrDigit3=(.100,.120),XpdrDigit4=(.130,.120),XpdrMode=(.160,.120),XpdrIdent=(.010,.120),
 Magneto=(.290,-.040),Master=(.270,.020),FuelPump=(.300,.020),Smoke=(.330,.020),Mixture=(.270,.080),Prop=(.300,.080),AlternateAir=(.320,-.010),Trim=(.250,-.050),
 NavLights=(-.270,.020),Strobe=(-.300,.020),LandingLight=(-.330,.020),FuelSelector=(-.290,-.040))
PITTS_FRONT=dict(Magneto=(.280,.000),Master=(.250,.060),FuelPump=(.280,.060),Smoke=(.310,.060),Mixture=(.220,.100),Prop=(.250,.100),AlternateAir=(.280,.100),Trim=(.220,.000),
 NavLights=(-.250,.060),Strobe=(-.280,.060),LandingLight=(-.310,.060),FuelSelector=(-.280,.000),
 ComOuter=(-.100,.100),ComInner=(-.130,.100),ComVolume=(-.070,.100),ComSwap=(-.160,.100),
 XpdrDigit1=(.040,.100),XpdrDigit2=(.071,.100),XpdrDigit3=(.102,.100),XpdrDigit4=(.133,.100),XpdrMode=(.164,.100),XpdrIdent=(.010,.100))
EXTRA_REAR=dict(ComSwap=(-.015,-.012),ComVolume=(-.045,-.012),ComOuter=(-.075,-.012),ComInner=(-.105,-.012),
 XpdrDigit1=(.015,-.012),XpdrDigit2=(.045,-.012),XpdrDigit3=(.075,-.012),XpdrDigit4=(.105,-.012),XpdrMode=(.140,.030),XpdrIdent=(.140,-.012),
 Magneto=(.175,.030),Mixture=(.210,.030),Prop=(.245,.030),AlternateAir=(.280,.030),Master=(.170,-.050),FuelPump=(.200,-.050),Smoke=(.230,-.050),Trim=(.270,-.045),
 NavLights=(-.170,-.050),Strobe=(-.200,-.050),LandingLight=(-.230,-.050),FuelSelector=(-.200,.030))
EXTRA_FRONT=dict(Magneto=(.200,.200),Master=(.200,.160),FuelPump=(.230,.160),Smoke=(.260,.160),Mixture=(.200,.110),Prop=(.230,.110),AlternateAir=(.260,.110),Trim=(.230,.060),
 NavLights=(-.200,.160),Strobe=(-.230,.160),LandingLight=(-.260,.160),FuelSelector=(-.200,.200),
 ComOuter=(-.200,.110),ComInner=(-.230,.110),ComVolume=(-.260,.110),ComSwap=(-.290,.110),
 XpdrDigit1=(-.200,.060),XpdrDigit2=(-.230,.060),XpdrDigit3=(-.260,.060),XpdrDigit4=(-.290,.060),XpdrMode=(.290,.160),XpdrIdent=(.290,.110))
EXTRA_FRONT=EXTRA_REAR
ANCHORS={'great-lakes-like':{'Rear':GL_REAR,'Front':GL_FRONT},'pitts-s2b-like':{'Rear':PITTS_REAR,'Front':PITTS_FRONT},'extra-300-like':{'Rear':EXTRA_REAR,'Front':EXTRA_FRONT}}

def base(model):return MODELS/model/f'{model}-own.glb'
def node_tris(g,world,i):
 n=g.doc['nodes'][i];m=world[i];out=[]
 for p in g.doc['meshes'][n['mesh']]['primitives']:
  v=g.array(p['attributes']['POSITION'])@m[:3,:3].T+m[:3,3];v=np.array([builder(q) for q in v])
  ids=g.array(p['indices']).reshape(-1,3) if 'indices' in p else np.arange(len(v)).reshape(-1,3)
  mat=g.doc['materials'][p['material']]['name'] if 'material' in p else ''
  out.append((mat,v[ids]))
 return out
def samples(t,k=8):
 w=np.array([(a/k,b/k,(k-a-b)/k) for a in range(k+1) for b in range(k+1-a)])
 return np.einsum('sw,twd->tsd',w,t).reshape(-1,3)
def raster_tris(tris2):
 """Boolean grid of cells whose centre is inside any (y,z) triangle."""
 mask=np.zeros((NY,NZ),bool);yy,zz=np.meshgrid(Y0+RES*(np.arange(NY)+.5),Z0+RES*(np.arange(NZ)+.5),indexing='ij')
 for a,b,c in np.asarray(tris2,float):
  d=(b[0]-a[0])*(c[1]-a[1])-(c[0]-a[0])*(b[1]-a[1])
  if abs(d)<1e-12:continue
  l1=((b[0]-yy)*(c[1]-zz)-(c[0]-yy)*(b[1]-zz))/d;l2=((c[0]-yy)*(a[1]-zz)-(a[0]-yy)*(c[1]-zz))/d
  mask|=(l1>=-1e-9)&(l2>=-1e-9)&(1-l1-l2>=-1e-9)
 return mask
def boxsum(grid,dy0,dy1,dz0,dz1):
 """For each cell centre, count of True cells inside [y+dy0,y+dy1]x[z+dz0,z+dz1] (out-of-grid counts as True)."""
 ii=np.zeros((NY+1,NZ+1));ii[1:,1:]=np.cumsum(np.cumsum(grid,0),1)
 a0=int(round(dy0/RES));a1=int(round(dy1/RES));b0=int(round(dz0/RES));b1=int(round(dz1/RES))
 I=np.arange(NY)[:,None];J=np.arange(NZ)[None,:]
 lo_i=I+a0;hi_i=I+a1+1;lo_j=J+b0;hi_j=J+b1+1
 inside=(lo_i>=0)&(hi_i<=NY)&(lo_j>=0)&(hi_j<=NZ)
 lo_i=np.clip(lo_i,0,NY);hi_i=np.clip(hi_i,0,NY);lo_j=np.clip(lo_j,0,NZ);hi_j=np.clip(hi_j,0,NZ)
 s=ii[hi_i,hi_j]-ii[lo_i,hi_j]-ii[hi_i,lo_j]+ii[lo_i,lo_j]
 return np.where(inside,s,1e9)

def seat_geometry(g,model,seat):
 """Outline mask, obstacle mask for the seat panel; also returns subpanel meshes to author."""
 spec=PANELS[model][seat];X=spec['x'];world=g.world_matrices();names={n['name']:i for i,n in enumerate(g.doc['nodes'])}
 outline=[];pts=[]
 for i,n in enumerate(g.doc['nodes']):
  if 'mesh' not in n or i not in world:continue
  nm=n['name']
  if nm.startswith(('Ctl_','Mount_','Grip_','Poke_','Windscreen','GL_Windshield','Subpanel_P3e_Switches')) or nm in GL_POP or n.get('extras',{}).get('p3_compatibility_empty'):continue
  for mat,t in node_tris(g,world,i):
   lo,hi=t[:,:,0].min(1),t[:,:,0].max(1);sel=(hi>=X-.047)&(lo<=X+.0005)
   if not sel.any():continue
   t=t[sel]
   isface=(np.abs(t[:,:,0]-X)<.004).all(1)
   if spec.get('mat')==mat or spec.get('node')==nm:
    outline+= [q[:,1:] for q in t[isface]];continue
   if mat in MOUNT_OK:continue
   s=samples(t);s=s[(s[:,0]>=X-.047)&(s[:,0]<=X+.0005)];pts.append(s)
 for nm,tr in spec.get('new',{}).items():outline+=[np.array(q) for q in tr]
 obst=np.zeros((NY,NZ),bool)
 if pts:
  p=np.concatenate(pts);iy=((p[:,1]-Y0)/RES).astype(int);iz=((p[:,2]-Z0)/RES).astype(int);ok=(iy>=0)&(iy<NY)&(iz>=0)&(iz<NZ);obst[iy[ok],iz[ok]]=True
 return X,raster_tris(outline),obst

def obstacles_for(g,model,seat):
 key,xs,z=SPECS[model];seatx=xs[XR.index(seat)];world=g.world_matrices();idx={n['name']:i for i,n in enumerate(g.doc['nodes'])};out=[]
 for sy in (1,-1):out.append(('knee',np.array((seatx-.20,sy*.105,z-.24)),None,.065))
 for label in ('Stick','Throttle'):
  name='Grip_'+label+'_'+seat
  if name in idx:v=builder(world[idx[name]][:3,3])
  else:
   legacy=label+('_R' if seat=='Rear' else '_F')
   if legacy not in idx:continue
   v=builder(world[idx[legacy]][:3,3])+np.array((-.05,0,.48 if label=='Stick' else .11))
  out.append((label,v,np.array((.17,.13,.025)) if label=='Stick' else np.array((.11,.01,.03)),.018))
 return out
def clearance(centres,radius,obs):
 c=np.min([np.linalg.norm(centres-v,axis=1)-r-radius if ext is None else np.linalg.norm(np.maximum(np.abs(centres-v)-ext,0),axis=1)-radius-r for _,v,ext,r in obs],axis=0)
 return c

def allocate(model):
 g=Glb(base(model));controls=g.doc['extras']['vrCockpitPhase3a']['controls'];result={}
 for seat in XR:
  X,outline,obst=seat_geometry(g,model,seat);obs=obstacles_for(g,model,seat);px=X-.001
  yc=Y0+RES*(np.arange(NY)+.5);zc=Z0+RES*(np.arange(NZ)+.5);YY,ZZ=np.meshgrid(yc,zc,indexing='ij')
  out=~outline
  legal={}
  for toggle in (False,True):
   top=.025 if toggle else .017
   ok=(boxsum(obst,-.017,.017,-.017,top)==0)&(boxsum(out,-.016,.016,-.018,max(.018,top-.002))==0)
   if 'zmax' in PANELS[model][seat]:ok&=ZZ<=PANELS[model][seat]['zmax']
   legal[toggle]=ok
  placed=[];anchors=ANCHORS[model][seat]
  seatc=[c for c in controls if c['seat']==seat];bylabel={c['grip'][5:].replace('_Front',''):c for c in seatc}
  assert set(bylabel)==set(anchors),(model,seat,set(bylabel)^set(anchors))
  for label,(ay,az) in anchors.items():
   c=bylabel[label];toggle=c['kind']=='Toggle';radius=c['sweep_radius_m']+(c['travel']/1000 if c['travel_unit']=='mm' else 0)
   ok=legal[toggle].copy()
   for (py,pz,pt) in placed:
    d=np.hypot(YY-py,ZZ-pz);ok&=d>=.031
    if pt or toggle:ok&=~((np.abs(YY-py)<.026)&(np.abs(ZZ-pz)<.040))
   cand=np.argwhere(ok)
   assert len(cand),(model,seat,label,'no legal slot')
   cy,cz=yc[cand[:,0]],zc[cand[:,1]];centres=np.stack([np.full(len(cy),px),cy,cz],1)
   good=clearance(centres,radius,obs)>.003;cand,cy,cz=cand[good],cy[good],cz[good]
   assert len(cand),(model,seat,label,'no clear slot')
   k=np.argmin((cy-ay)**2+(cz-az)**2);p=(round(float(cy[k]),3),round(float(cz[k]),3))
   placed.append((p[0],p[1],toggle));result[c['node']]=(px,)+p
 return result

def apply(path,model,placement):
 g=Glb(path)
 # A later pass owns these controls now; re-placing them here would undo it (and re-add the retired P3e subpanel).
 ex=g.doc.get('extras',{});later=[k for k in ('vrCockpitPhase3f','pittsCockpitV3') if ex.get(k)]
 if ex.get('vrCockpitPhase3a',{}).get('placement_v4'):later.append('gl_quadrant_v3')
 assert not later,(path.name,'controls already placed by',later,'- rebuild from the base GLB instead');names={n['name']:i for i,n in enumerate(g.doc['nodes'])};nodes=g.doc['nodes']
 key=SPECS[model][0];rigpath=ROOT/f'docs/models/{key}-own-rig.json';doc=json.loads(rigpath.read_text())
 if model=='great-lakes-like':
  for nm in GL_POP:
   if nm in names:n=nodes[names[nm]];n.pop('mesh',None);n['extras']={'p3_compatibility_empty':True,'superseded_by':'cockpit_controls_v2 interactive Ctl_'}
  doc['rig']=[r for r in doc['rig'] if r['node'] not in GL_POP]
 for seat,spec in PANELS[model].items():
  for nm,tr in spec.get('new',{}).items():
   y0,z0=np.min([q for t in tr for q in t],0);y1,z1=np.max([q for t in tr for q in t],0)
   s=Shape();s.box(((spec['x']+.003),(y0+y1)/2,(z0+z1)/2),(.006,y1-y0,z1-z0))
   g.mesh(nm,[gl(q) for q in s.v],s.t,g.material('P3e_Subpanel',(.03,.03,.035)))
 controls=g.doc['extras']['vrCockpitPhase3a']['controls']
 for c in controls:
  p=np.array(placement[c['node']]);n=nodes[names[c['node']]]
  n['translation']=gl(p).tolist();c['pivot_gltf']=gl(p).tolist()
  nodes[names['Mount_'+c['grip'][5:]]]['translation']=gl(p).tolist()
  c['position_status']='E';c['placement']='cockpit_controls_v2: aft-facing panel face, inside cockpit'
  n['extras']['interaction']=c
  for entry in doc['rig']:
   if entry['node']==c['node']:entry['pivot_gltf']=c['pivot_gltf']
 doc['controls']=controls
 g.doc['extras']['vrCockpitPhase3a']['placement_v2']='Ty 2026-11-02: controls on aft-facing panel faces inside each cockpit (cockpit_controls_v2.py)'
 validate(g,path);g.save(path);rigpath.write_text(json.dumps(doc,indent=1)+'\n')

if __name__=='__main__':
 models=[a for a in sys.argv[1:] if not a.startswith('--')] or list(PANELS)
 for model in models:
  pl=allocate(model)
  json.dump({k:list(v) for k,v in pl.items()},open(f'/tmp/cc/{model}.json','w'),indent=0)
  if '--dry' in sys.argv:continue
  for path in sorted((MODELS/model).glob('*own*.glb')):apply(path,model,pl);print('placed',path.name,len(pl))
