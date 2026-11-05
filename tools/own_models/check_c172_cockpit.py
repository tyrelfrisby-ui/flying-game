"""Validate exported cockpit node hierarchy and binding coverage; no graphics device needed."""
import json, struct, hashlib, os, math
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
MODEL=ROOT/'unity/Assets/_Project/Resources/Models/c172-like/c172-like-own.glb'
def check(path):
 raw=path.read_bytes();assert raw[:4]==b'glTF';size=struct.unpack_from('<I',raw,12)[0];g=json.loads(raw[20:20+size]);nodes=g['nodes']
 names=[n.get('name','') for n in nodes];assert len(names)==len(set(names)),'Duplicate names'
 parents={}
 for i,n in enumerate(nodes):
  for c in n.get('children',[]):
   assert 0<=c<len(nodes) and c not in parents,'Invalid or multiply-parented node';parents[c]=i
 reached=set()
 def visit(i,chain):
  assert i not in chain,'Cycle';reached.add(i)
  for c in nodes[i].get('children',[]):visit(c,chain|{i})
 for i in g['scenes'][g.get('scene',0)]['nodes']:visit(i,set())
 assert len(reached)==len(nodes),'Orphan nodes'
 rig=json.loads((ROOT/'docs/models/c172-panel-bindings.json').read_text())['bindings'];rig_names=[e['node'] for e in rig]
 assert len(rig_names)==len(set(rig_names))
 for e in rig:
  assert names.count(e['node'])==1,e['node'];n=nodes[names.index(e['node'])]
  assert 'mesh' in n,e['node']+' missing moving mesh'
  assert 'rig' in n.get('extras',{}),e['node']+' missing rig metadata'
  pos=[0.,0.,0.];index=names.index(e['node'])
  while True:
   ancestor=nodes[index]
   assert 'matrix' not in ancestor and ancestor.get('rotation',[0,0,0,1])==[0,0,0,1]
   assert ancestor.get('scale',[1,1,1])==[1,1,1]
   pos=[a+b for a,b in zip(pos,ancestor.get('translation',[0,0,0]))]
   if index not in parents:break
   index=parents[index]
  x,y,z=e['pivot_blender_m'];expected=[x,z,-y]
  assert max(abs(a-b) for a,b in zip(pos,expected))<.00001,(e['node'],'pivot mismatch',pos,expected)
 required=['Airspeed_needle','Altimeter_needle','Altimeter_1000','Altimeter_10000','VSI_needle','AI_roll','AI_pitch','DG_card','DG_heading_bug','DG_sync_adjust','DG_bug_adjust','TC_airplane','TC_slip_ball','NAV1_CDI','NAV1_GS','NAV2_CDI','NAV1_OBS_card','NAV2_OBS_card','ADF_bearing','DME_channel','KR85_ADF_tune','Trim_wheel','Trim_indicator','Fuel_selector','Throttle','Mixture','Carb_heat','Flap_switch','Flap_indicator','Mags_key','Primer','Cabin_heat','Cabin_air']
 required+=['Tach_needle','Suction_needle','Oil_press_needle','Oil_temp_needle','Fuel_L_needle','Fuel_R_needle','Ammeter_needle','EGT_needle']
 for side in ['L','R']:
  required+=['Yoke_'+side+'_pitch','Yoke_'+side+'_roll']
  for tag in ['L','R']:required+=['Pedal_'+side+'_'+tag,'Toe_brake_'+side+'_'+tag]
 for n in required:assert 'C172_'+n in rig_names,n+' binding missing'
 assert 'C172_NAV2_GS' not in names,'NAV2 must be course only'
 for n in ['Seat_base_L','Seat_base_R','Rear_bench_base','Door_L','Door_R','Headliner','Compass_mount','KX170B_1','KX170B_2','KR85_ADF','DME']:
  assert 'C172_'+n in names,n
 # Cases are behind the physical panel front, rather than stacked on its seat side.
 for n in nodes:
  if n.get('name','').startswith('C172_') and n.get('name','').endswith('_case'):
   for prim in g['meshes'][n['mesh']]['primitives']:
    a=g['accessors'][prim['attributes']['POSITION']]
    assert n['translation'][0]+a['min'][0]>=.2974,(n['name'],'case protrudes from panel')
 # Check every square mount against every other mount and every radio, including the new third row.
 cases=[]
 for n in nodes:
  if n.get('name','').endswith('_case'):
   layout=json.loads(n['extras']['layout']);cases.append(layout)
   assert abs(n['translation'][2]-layout['x'])<1e-6 and abs(n['translation'][1]-layout['z'])<1e-6
   for p in g['meshes'][n['mesh']]['primitives']:
    a=g['accessors'][p['attributes']['POSITION']]
    assert max(a['max'][i]-a['min'][i] for i in [1,2])/2<=layout['half_extent']+.00001
 for i,a in enumerate(cases):
  for b in cases[i+1:]:
   gap=max(abs(a['x']-b['x']),abs(a['z']-b['z']))-a['half_extent']-b['half_extent']
   assert gap>=.002-1e-6,('mount overlap',a['name'],b['name'],gap)
  for name,z,h in [('Audio',.366,.028),('KX170B_1',.307,.075),('KX170B_2',.224,.075),('KR85_ADF',.151,.060),('DME',.094,.042),('KT76',.042,.048)]:
   assert abs(a['x']-.160)>a['half_extent']+.0885+.002 or abs(a['z']-z)>a['half_extent']+h/2+.002,(a['name'],name)
  # Yoke shafts translate through the panel but sit below every instrument. The rotating grips'
  # complete +/-90 degree sweep stays aft of forward .068, clear of all panel/radio faces (.262+).
  # yoke-v1 (Ty 2026-11-04): shaft centre raised .040 -> .100 to clear the thighs; literal, c172_cockpit.py YOKE_Z.
  for x in [-.30,.30]:
   assert abs(a['x']-x)>a['half_extent']+.013+.002 or abs(a['z']-.100)>a['half_extent']+.013+.002,(a['name'],'yoke shaft')
  assert a['z']-a['half_extent']>.0, (a['name'],'lower switches')
 layout={a['name']:a for a in cases}
 assert layout['Tach']['x']==layout['VSI']['x'] and layout['Tach']['z']<layout['VSI']['z']
 assert layout['Suction']['x']==layout['DG']['x'] and layout['Suction']['z']<layout['DG']['z']
 # yoke-v1: the suction gauge is a 1 in instrument (Ty: "Usually the suction gage is a tiny 1 inch instrument").
 assert abs(layout['Suction']['half_extent']-(.0127+.005))<1e-6,('suction gauge not 1 in',layout['Suction'])
 for side in ['L','R']:
  col=nodes[names.index('C172_Yoke_'+side+'_pitch')]['translation']
  assert abs(col[1]-.100)<1e-4,('yoke shaft height',side,col)
 assert all(layout[n]['x']==0 for n in ['Oil_press','Oil_temp','Ammeter','EGT'])
 # Ty, 2026-11-03: NAV heads beside the six-pack, the engine column to their RIGHT, then the radios.
 assert all(layout[n]['x']==-.112 for n in ['NAV1','NAV2','ADF'])
 assert layout['Altimeter']['x']<layout['NAV1']['x']<layout['Oil_press']['x']<.160
 assert [layout[n]['z'] for n in ['Oil_press','Oil_temp','Ammeter','EGT']]==sorted([layout[n]['z'] for n in ['Oil_press','Oil_temp','Ammeter','EGT']],reverse=True)
 assert abs((layout['Fuel_L']['x']+layout['Fuel_R']['x'])/2-layout['Turn_coordinator']['x'])<1e-6
 assert not any('Fuel_press' in n or 'Fuel_flow' in n or 'Prop_control' in n for n in names)
 def bounds(name):
  n=nodes[names.index(name)];aa=[g['accessors'][p['attributes']['POSITION']] for p in g['meshes'][n['mesh']]['primitives']]
  return [min(a['min'][i] for a in aa) for i in range(3)],[max(a['max'][i] for a in aa) for i in range(3)]
 # The moving horizon and ladder remain behind the aperture but within 5 mm of the dial plane.
 lo,hi=bounds('C172_AI_pitch')
 n=nodes[names.index('C172_AI_pitch')];par=nodes[parents[names.index('C172_AI_pitch')]]
 world_f=n['translation'][0]+par['translation'][0]
 assert world_f+lo[0]>.2975 and world_f+hi[0]-.2975<.005,('AI face depth',world_f,lo,hi)
 # Full yoke-roll sweep is confined to the seat side even at maximum pitch travel.
 for side in ['L','R']:
  name='C172_Yoke_'+side+'_roll';lo,hi=bounds(name)
  # forward glTF X is unchanged by rotating about X. Include parent position and 70 mm travel.
  n=nodes[names.index(name)];par=nodes[parents[names.index(name)]]
  assert n['translation'][0]+par['translation'][0]+hi[0]+.070<.262-.010,('yoke grip sweep',side)
 # Ty, 2026-11-04: rudder pedals clearly above the cabin floor and forward of the panel face. Literals: carpet top
 # -0.5475, panel face 0.2975 (c172_cockpit.py). The full fit, with the seated mannequin at the approved eye and
 # full rudder and yoke travel, is check_c172_pedal_fit.py (Blender).
 for side in ['L','R']:
  for tag in ['L','R']:
   name='C172_Pedal_'+side+'_'+tag;n=nodes[names.index(name)];lo,hi=bounds(name)
   assert abs(n['translation'][0]-.546)<1e-4 and abs(n['translation'][1]+.505)<1e-4,(name,'hinge moved',n['translation'])
   assert n['translation'][1]+lo[1]>-.5475+.030,(name,'within 30 mm of the carpet')
   assert n['translation'][0]+lo[0]>.2975+.15,(name,'not forward of the panel face')
   toe=nodes[names.index('C172_Toe_brake_'+side+'_'+tag)]
   assert n['translation'][1]+toe['translation'][1]>-.5475+.15,(name,'toe brake under 150 mm above the carpet')
 # Moving TC geometry is exactly symmetric about its own origin, centred on its case/pivot.
 lo,hi=bounds('C172_TC_airplane');assert abs(lo[2]+hi[2])<.0001,('asymmetric TC wings',lo,hi)
 tc=nodes[names.index('C172_TC_airplane')]['translation']
 assert next(e for e in rig if e['node']=='C172_TC_airplane')['range']==[-40,40]
 assert abs(tc[1]-layout['Turn_coordinator']['z'])<1e-6 and abs(tc[2]-layout['Turn_coordinator']['x'])<1e-6
 # Decode exported vertices: the rate marks' principal axes must lie on the pivot radial at +/-20deg.
 bin_start=20+size+8
 def vertices(n):
  for p in g['meshes'][n['mesh']]['primitives']:
   a=g['accessors'][p['attributes']['POSITION']];v=g['bufferViews'][a['bufferView']]
   assert a['componentType']==5126
   offset=bin_start+v.get('byteOffset',0)+a.get('byteOffset',0)
   for i in range(a['count']):yield struct.unpack_from('<fff',raw,offset+i*v.get('byteStride',12))
 runtime=json.loads((ROOT/'docs/models/c172-like-own-rig.json').read_text())['rig'];rr={r['node']:r for r in runtime}
 for n in nodes:
  if not n.get('name','').startswith('C172_TC_rate_mark'):continue
  vv=list(vertices(n));cx=sum(v[2] for v in vv)/len(vv);cy=sum(v[1] for v in vv)/len(vv)
  xx=sum((v[2]-cx)**2 for v in vv);yy=sum((v[1]-cy)**2 for v in vv);xy=sum((v[2]-cx)*(v[1]-cy) for v in vv)
  angle=math.degrees(.5*math.atan2(2*xy,xx-yy))
  assert abs(abs(angle)-20)<.001,(n['name'],angle)
  px=n['translation'][2]+cx-tc[2];py=n['translation'][1]+cy-tc[1]
  assert abs(-angle-math.copysign(20,px))<.001,('signed TC radial',n['name'],angle,px)
  assert abs(px*math.sin(math.radians(angle))-py*math.cos(math.radians(angle)))<.0001,'mark misses pivot radial'
  assert rr['C172_TC_airplane']['map']==[[-2,-40],[2,40]]
 assert rr['C172_VSI_needle']['map']==[[-2000,-170],[-1000,-90],[0,0],[1000,90],[2000,170]]
 assert rr['C172_VSI_needle']['model']==0
 lo,hi=bounds('C172_VSI_needle')
 assert lo[2]<-.030 and hi[2]<.006 and max(abs(lo[1]),abs(hi[1]))<.0011,('VSI drawn zero must be exactly LEFT',lo,hi)
 # Apply runtime CW rotation to the actual left-pointing tip: +/-500 hits the upper/lower 45deg radials.
 for angle in [-45,45]:
  right=lo[2]*math.cos(math.radians(angle));up=-lo[2]*math.sin(math.radians(angle))
  assert right<0 and math.copysign(1,up)==math.copysign(1,angle) and abs(abs(up)-abs(right))<1e-8
 knobs=[]
 for name,radius in [('Throttle',.013),('Mixture',.010),('Carb_heat',.008)]:
  node='C172_'+name;lo,hi=bounds(node);knobs.append((nodes[names.index(node)]['translation'][2],(hi[2]-lo[2])/2))
  assert abs(knobs[-1][1]-radius)<.00001
  authored=next(e for e in rig if e['node']==node);assert authored['kind']=='translation' and authored['range']==[0,.06]
  if node in rr:assert rr[node]['kind']=='move'
 assert knobs[0][0]<knobs[1][0]<knobs[2][0] and knobs[0][1]>knobs[1][1]>knobs[2][1]
 assert 'C172_Carb_heat' not in rr and 'C172_Ammeter_needle' not in rr and 'C172_EGT_needle' not in rr
 labels=json.loads(nodes[names.index('C172_Interior_static')]['extras']['printed_labels'])
 texts={l['text'] for l in labels}
 for t in ['THROTTLE','MIXTURE','CARB HEAT','PRIMER','CABIN HEAT','CABIN AIR','FLAPS','0 10 20 30 40','BAT','ALT','BCN','LAND','TAXI','NAV','PITOT HT','OFF R L BOTH START','AIRSPEED','MPH','VERTICAL SPEED','UP','DOWN','100 FEET','PER MINUTE','VAC','TURN\nCOORDINATOR','2 MIN','D.C. ELEC.','OIL PRESS','OIL TEMP','AMPS','EGT','FUEL QTY L','FUEL QTY R']:
  assert t in texts,('missing legend',t)
 for label in labels:
  assert label['depth_m']<.000001,('label not flat',label)
  assert label['cap_height_m']>=.0045,('tiny label',label)
  if label['name'].startswith('C172_AI_pitch_number'):
   assert 0<=.29899-label['forward_m']<=.0003,('floating horizon number',label)
  if label['name'].startswith(('C172_VSI_','C172_Airspeed_','C172_ASI_units','C172_Altimeter_numeral','C172_Altimeter_label','C172_Baro_window','C172_TC_title','C172_TC_direction','C172_TC_two_min','C172_TC_electric')):
   assert 0<=.2974-label['forward_m']<=.0003,('floating dial legend',label)
  if label['name'].endswith('_placard') or label['name'].startswith('C172_flap_') or any(label['name'].startswith('C172_'+n+'_label') for n in ['Throttle','Mixture','Carb_heat','Cabin_heat','Cabin_air','Master_BAT','Master_ALT','Beacon','Landing','Taxi','Nav_lights','Pitot_heat','Primer','ignition','flap']):
   assert label['cap_height_m']>=.006
   surface=.2775 if label['forward_m']<.29 else .2975
   assert 0<=surface-label['forward_m']<=.0003,('floating panel label',label)
 return dict(model=str(path.relative_to(ROOT)),sha256=hashlib.sha256(raw).hexdigest(),nodes=len(nodes),bindings=len(rig),required_bindings=len(required),graph='unique names, one parent, no cycles, no orphan nodes',result='PASS')
if __name__=='__main__':
 reports=[check(MODEL),check(ROOT/os.environ.get('C172_OUT','renders/c172/instruments-v3')/'c172-interior.glb')]
 (ROOT/os.environ.get('C172_OUT','renders/c172/instruments-v3')/'validation.json').write_text(json.dumps(reports,indent=2)+'\n');print(json.dumps(reports,indent=2))
