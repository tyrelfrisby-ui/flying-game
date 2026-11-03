"""Validate exported cockpit node hierarchy and binding coverage; no graphics device needed."""
import json, struct, hashlib
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
 for side in ['L','R']:
  required+=['Yoke_'+side+'_pitch','Yoke_'+side+'_roll']
  for tag in ['L','R']:required+=['Pedal_'+side+'_'+tag,'Toe_brake_'+side+'_'+tag]
 for n in required:assert 'C172_'+n in rig_names,n+' binding missing'
 assert 'C172_NAV2_GS' not in names,'NAV2 must be course only'
 for n in ['Seat_base_L','Seat_base_R','Rear_bench_base','Door_L','Door_R','Headliner','Compass_mount','KX170B_1','KX170B_2','KR85_ADF','DME']:
  assert 'C172_'+n in names,n
 # 3-1/8 inch primary instruments and two-inch systems instruments must remain separate.
 cases=[]
 for label in ['Airspeed','Attitude','Altimeter','NAV1']:
  n=nodes[names.index('C172_'+label+'_case')]
  assert 'matrix' not in n and n.get('rotation',[0,0,0,1])==[0,0,0,1]
  prim=g['meshes'][n['mesh']]['primitives'][0];a=g['accessors'][prim['attributes']['POSITION']]
  diameter=a['max'][2]-a['min'][2]
  assert .083<diameter<.090,(label,diameter)
  cases.append((n['translation'][2],diameter))
 for (x,d),(x2,d2) in zip(cases,cases[1:]):assert abs(x2-x)>(d+d2)/2+.004,'Bezel overlap'
 return dict(model=str(path.relative_to(ROOT)),sha256=hashlib.sha256(raw).hexdigest(),nodes=len(nodes),bindings=len(rig),required_bindings=len(required),graph='unique names, one parent, no cycles, no orphan nodes',result='PASS')
if __name__=='__main__':
 reports=[check(MODEL),check(ROOT/'renders/c172/v1/c172-interior.glb')]
 (ROOT/'renders/c172/v1/validation.json').write_text(json.dumps(reports,indent=2)+'\n');print(json.dumps(reports,indent=2))
