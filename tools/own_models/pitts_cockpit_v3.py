"""Pitts-only cockpit revision, builder X forward/Y left/Z up (metres).

Run: PYTHONDONTWRITEBYTECODE=1 nice /usr/bin/python3 tools/own_models/pitts_cockpit_v3.py
--switch-groups (the shipped layout, chosen 2026-11-03 for Ty's "one row of switches along the bottom of the
panel"): the 12 switches on ONE horizontal line along the bottom of each panel, six each side at .031 m pitch,
with a central gap that keeps the stick sweep (and the rear sequence card) clear; a uniform 12-across row
intersects the stick sweep at the front panel. The rear panel face is extended down to carry the line.
Without the flag the switches are left where cockpit_controls_v2 put them. Pending Ty's review, not approved.

Deterministic and byte-idempotent: read each livery and the rig from the pinned
pre-edit commit, apply absolute placements, validate before every GLB save. Never
re-export unrelated geometry, remove nodes, change rig maps, or write mirrors.
A different baseline requires deliberate review of BASE. Existing geometry and
all indices remain intact; removed front bows become meshless nodes.

Rear panel lowering uses a 1 mm search for the first rim height below a ray from
the unchanged rear eye to the centreline cowling crown at x=.10,z=.222, with 1 mm
vertical clearance. Includes the complete rear panel/card/control assembly and
any associated rig pivots. No rear needle nodes exist in this baseline (gauges
are static textured faces). Eye_Rear is added at the existing config eye.

The added release knob is 20 mm diameter at the front LEFT sill. Its 3 mm cable
follows the left sill aft and the retained rear bow onto the existing black
rear latch knob; existing jettison nodes/maps remain untouched.
"""
import argparse,json,math,subprocess,tempfile
from pathlib import Path
import numpy as np
from vr_cockpit import Glb,Shape,ROOT,MODELS,gl,builder,skin_y,sill
from drop_card_slot import validate
from gl_quadrant_v3 import set_mesh,atlas,quad
import cockpit_controls_v2 as cc

BASE='4f54176b9ab285abc3b8b33a9c97084a3eea9a77'
MODEL='pitts-s2b-like'
RIG=ROOT/'docs/models/pitts-s2b-own-rig.json'
ROW=['Magneto','Master','FuelPump','Smoke','AlternateAir','Mixture','Prop','Trim','FuelSelector','NavLights','Strobe','LandingLight']
LABEL=['MAGS','MASTER','FUEL PUMP','SMOKE','ALT AIR','MIX','PROP','TRIM','FUEL','NAV','STROBE','LAND']
RADIO=['ComOuter','ComInner','ComVolume','ComSwap','XpdrDigit1','XpdrDigit2','XpdrDigit3','XpdrDigit4','XpdrMode','XpdrIdent']
EYE=np.array((-1.325,0,.300))
COWL=np.array((.100,0,.222))
RIM_TOP=.2968333333
RAY_Z=EYE[2]+(COWL[2]-EYE[2])*(-.808-EYE[0])/(COWL[0]-EYE[0])
LOWER=next(mm/1000 for mm in range(0,101) if RIM_TOP-mm/1000<RAY_Z-.001)

def baseline(path):
 return subprocess.run(['git','show',BASE+':'+str(path.relative_to(ROOT))],cwd=ROOT,check=True,capture_output=True).stdout

def build(g,rig,switch_groups=False):
 nodes=g.doc['nodes']; names={n['name']:i for i,n in enumerate(nodes)}
 controls=g.doc['extras']['vrCockpitPhase3a']['controls']; rr={r['node']:r for r in rig['rig']}
 W=g.world_matrices()
 def put(name,parts,P=(0,0,0),parent=None):
  if name not in names:
   nodes.append(dict(name=name));names[name]=len(nodes)-1
   (nodes[names[parent]].setdefault('children',[]) if parent else g.doc['scenes'][0]['nodes']).append(names[name])
  set_mesh(g,name,parts,np.array(P))
  n=nodes[names[name]]
  n['translation']=(gl(P)-W[names[parent]][:3,3] if parent else gl(P)).tolist()
  return n
 def move(name,dz):
  n=nodes[names[name]]; assert 'matrix' not in n
  n['translation']=(np.array(n.get('translation',[0,0,0]))+gl((0,0,dz))).tolist()
  if name in rr:rr[name]['pivot_gltf']=(np.array(rr[name]['pivot_gltf'])+gl((0,0,dz))).tolist()
 # Move assembly roots only; children (grips/pokes, any needles) follow once.
 moved={n['name'] for n in nodes if n['name'].startswith(('RearPanel_','SequenceCard_')) or n['name']=='Panel_Rim_Rear'}
 for c in controls:
  if c['seat']=='Rear':moved.update((c['node'],'Mount_'+c['grip'][5:]))
 parents={nodes[ch]['name']:n['name'] for n in nodes for ch in n.get('children',[])}
 for name in sorted(moved):
  if parents.get(name) not in moved:move(name,-LOWER)
 for c in controls:
  if c['seat']=='Rear':
   c['pivot_gltf']=(np.array(c['pivot_gltf'])+gl((0,0,-LOWER))).tolist()
   nodes[names[c['node']]]['extras']['interaction']=c
 # Retain indices and all front latch/hinge/jettison nodes.
 for name in ('Canopy_Bow_F_Front','Canopy_Bow_R_Front'):nodes[names[name]].pop('mesh',None)
 # Front-left corner follows the measured sill, +Y is pilot LEFT.
 black='VR_Black'
 def rail(x,side=1):return np.array((x,side*(skin_y(x,sill(x))-.009),sill(x)-.019))
 P=rail(-.150);P[2]+=.012
 knob=Shape();knob.sphere(P,.010,20,12)
 stem=Shape();stem.tube(rail(-.150),P,.0035,10)
 put('Pitts_Release_Knob_Left',[ (black,knob,None),(black,stem,None)],parent='Canopy')
 route=[P,rail(-.15),*[rail(x) for x in np.linspace(-.15,-.875,35)[1:]]]
 from vr_phase2 import bow_points
 bow=bow_points(-.875,-.80)
 route += [q+np.array((-.006,0,0)) for q in bow if q[1]>=-.0381]
 route += [np.array((-.881,-.0381,.3213)),np.array((-.877,-.0381,.3413333333))]
 cable=Shape()
 for a,b in zip(route,route[1:]):cable.tube(a,b,.0015,8)
 put('Pitts_Release_Cable_Left',[ (black,cable,None)],parent='Canopy')
 if 'Eye_Rear' not in names:
  nodes.append(dict(name='Eye_Rear',translation=gl(EYE).tolist()));names['Eye_Rear']=len(nodes)-1;g.doc['scenes'][0]['nodes'].append(names['Eye_Rear'])
 data,UV=atlas([(k,v,(245,245,240),(12,12,14)) for k,v in zip(ROW,LABEL)]+[(k,k,(245,245,240),(12,12,14)) for k in ['COM','XPDR']])
 with tempfile.TemporaryDirectory() as td:
  p=Path(td)/'pitts-placards.png';p.write_bytes(data);g.material('Pitts_Placards',(1,1,1),png=p)
 for seat,X,z,radioz in [('Rear',-.808,-.140-LOWER,.155-LOWER),('Front',-.151,-.096,.135)]:
  # Push-pulls get outer slots because full stroke enlarges their conservative sphere.
  ys=dict(zip(ROW,[.195,.226,.257,.288,.350,.319,-.350,-.195,-.226,-.257,-.288,-.319]))
  if seat=='Rear' and switch_groups:
   panel=Shape();panel.box((X+.0075,0,(-.0694-.200)/2-LOWER),(.015,.766,.200-.0694))
   rim=Shape()
   for y in [-.383,.383]:rim.tube((X,y,-.0694-LOWER),(X,y,-.200-LOWER),.0025,8)
   rim.tube((X,-.383,-.200-LOWER),(X,.383,-.200-LOWER),.0025,8)
   rimat=g.doc['materials'][g.doc['meshes'][nodes[names['Panel_Rim_Rear']]['mesh']]['primitives'][0]['material']]['name']
   put('Pitts_RearPanel_Extension',[('P2c_Panel_White',panel,None),(rimat,rim,None)])
  pl=Shape();uv=[]
  if switch_groups:
   for lab,y in ys.items():quad(pl,uv,(X-.0006,y,z-.028),(0,-1,0),(0,0,1),.029,.012,UV[lab])
   put('Pitts_Switch_Placards_'+seat,[('Pitts_Placards',pl,uv)])
  rp=Shape();rt=Shape();ruv=[]
  ry=np.arange(10)*.031-.1395
  for key,a,b in [('COM',0,4),('XPDR',4,10)]:
   mid=(ry[a]+ry[b-1])/2;width=ry[b-1]-ry[a]+.036
   rp.box((X+.001,mid,radioz+.006),(.002,width,.056))
   quad(rt,ruv,(X-.0007,mid,radioz+.025),(0,-1,0),(0,0,1),.035,.010,UV[key])
  put('Pitts_Radio_Faceplates_'+seat,[(black,rp,None),('Pitts_Placards',rt,ruv)])
  for c in controls:
   if c['seat']!=seat:continue
   lab=c['grip'][5:].replace('_Front','')
   if lab in ys and not switch_groups:continue
   P=np.array((X-.001,ys[lab],z)) if lab in ys else np.array((X-.001,ry[RADIO.index(lab)],radioz))
   radius=c['sweep_radius_m']+(c['travel']/1000 if c['travel_unit']=='mm' else 0)
   assert cc.clearance(P[None,:],radius,cc.obstacles_for(g,MODEL,seat))[0]>0,(seat,lab,P)
   c['pivot_gltf']=gl(P).tolist();c['placement']='Pitts v3: panel face, horizontal line with centre clearance gap' if lab in ys else 'Pitts v3: COM / XPDR upper panel faceplates'
   for name in [c['node'],'Mount_'+c['grip'][5:]]:nodes[names[name]]['translation']=gl(P).tolist()
   nodes[names[c['node']]]['extras']['interaction']=c;rr[c['node']]['pivot_gltf']=c['pivot_gltf']
 rig['controls']=controls
 g.doc['extras']['pittsCockpitV3']=dict(rear_panel_lower_m=LOWER,eye_builder=EYE.tolist(),switch_groups=switch_groups,removed_meshes=['Canopy_Bow_F_Front','Canopy_Bow_R_Front'],cowl_reference=COWL.tolist())
 # Every affected metadata row is checked against the actual world pivot.
 W=g.world_matrices()
 for c in controls:
  assert np.allclose(W[names[c['node']]][:3,3],c['pivot_gltf'])
  assert np.allclose(W[names['Mount_'+c['grip'][5:]]][:3,3],c['pivot_gltf'])
  assert np.allclose(rr[c['node']]['pivot_gltf'],c['pivot_gltf'])
 return rig

def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--switch-groups',action='store_true');args=ap.parse_args()
 rig0=json.loads(baseline(RIG));result=None
 for path in sorted((MODELS/MODEL).glob('*.glb')):
  with tempfile.TemporaryDirectory() as td:
   p=Path(td)/path.name;p.write_bytes(baseline(path));g=Glb(p)
  rig=build(g,json.loads(json.dumps(rig0)),args.switch_groups)
  validate(g,path);g.save(path)
  assert result is None or result==rig
  result=rig
 RIG.write_text(json.dumps(result,indent=1)+'\n')
 print('rear panel lowering',LOWER,'rim top',RIM_TOP,'->',RIM_TOP-LOWER,'cowl ray height at panel',RAY_Z)
 print('rear eye unchanged',EYE.tolist(),'panel angle',math.degrees(math.atan2(RIM_TOP-EYE[2],-.808-EYE[0])),'->',math.degrees(math.atan2(RIM_TOP-LOWER-EYE[2],-.808-EYE[0])),'cowl angle',math.degrees(math.atan2(COWL[2]-EYE[2],COWL[0]-EYE[0])))
if __name__=='__main__':main()
