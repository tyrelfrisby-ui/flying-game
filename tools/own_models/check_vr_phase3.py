"""CPU P3a graph, binding and conservative clearance checks; no visual approval."""
import json
import numpy as np
from vr_cockpit import Glb, MODELS, ROOT, builder
from vr_phase3 import SPECS
from drop_card_slot import validate

def check(path):
 g=Glb(path);validate(g,path);nodes={n['name']:n for n in g.doc['nodes']};world=g.world_matrices();idx={n['name']:i for i,n in enumerate(g.doc['nodes'])};parents={c:i for i,n in enumerate(g.doc['nodes']) for c in n.get('children',[])}
 assert len(nodes)==len(g.doc['nodes'])
 controls=g.doc['extras']['vrCockpitPhase3a']['controls'];rows=[]
 key,xs,z=SPECS[path.parent.name]
 for c in controls:
  n=nodes[c['node']];assert 'mesh' in n
  p=world[idx[c['node']]][:3,3];assert np.allclose(p,c['pivot_gltf'])
  assert np.isclose(np.linalg.norm(c['axis_gltf']),1)
  verts=np.concatenate([g.array(pr['attributes']['POSITION']) for pr in g.doc['meshes'][n['mesh']]['primitives']])
  assert np.linalg.norm(verts,axis=1).max() <= c['sweep_radius_m']+1e-6
  assert n['extras']['rig_param']==c['param']
  travel_map=n['extras'].get('rig_map_m',n['extras'].get('rig_map_deg'))
  measured=max(travel_map)-min(travel_map)
  assert np.isclose(measured,c['travel']/(1000 if c['travel_unit']=='mm' else 1))
  assert 0<c['travel']<= (60 if c['travel_unit']=='mm' else 540)
  assert c['detent_count']==len(c['detents'])
  for contact in (c['poke'],c['grip']):
   assert 'mesh' not in nodes[contact];assert parents[idx[contact]]==idx[c['node']]
  # Conservative spheres: entire control within 40mm for every angle; add full linear stroke.
  radius=c['sweep_radius_m']+(c['travel']/1000 if c['travel_unit']=='mm' else 0)
  centre=builder(p);seat=c['seat'];seatx=xs[0 if seat=='Rear' else 1]
  # Estimated bent knees in the central corridor, 65mm radius each.
  obstacles=[('knee_'+side,np.array((seatx-.20,sy*.105,z-.24)),.065) for side,sy in [('L',1),('R',-1)]]
  # Bound primary-control sweeps by a capsule around sampled physical grip paths.
  for label in ('Stick','Throttle'):
   name='Grip_'+label+'_'+seat
   if name not in idx:
    legacy=label+('_R' if seat=='Rear' else '_F')
    if legacy not in idx:continue
    v=builder(world[idx[legacy]][:3,3]);v=v+np.array((-.05,0,.48 if label=='Stick' else .11))
   else:v=builder(world[idx[name]][:3,3])
   # Sweep bound (E): +/- 170mm stick fore/aft, +/- 130mm lateral; throttle 110mm fore/aft.
   extent=np.array((.17,.13,.025)) if label=='Stick' else np.array((.11,.01,.03))
   delta=np.maximum(np.abs(centre-v)-extent,0)
   gap=np.linalg.norm(delta)-radius-.018
   obstacles.append((label+'_sweep',centre+np.array((gap+radius,0,0)),0))
  clear={label:round(float(np.linalg.norm(centre-v)-r-radius)*1000,2) for label,v,r in obstacles}
  assert min(clear.values())>0,(path.name,c['node'],clear)
  rows.append(dict(node=c['node'],clearance_mm=clear))
 return dict(file=str(path.relative_to(ROOT)),controls=len(controls),clearances=rows,scope='Conservative 40mm control sphere plus full pull stroke; estimated knee spheres and primary sweep boxes. No anthropometric/VR approval.')
if __name__=='__main__':
 report=[check(p) for model in SPECS for p in sorted((MODELS/model).glob('*own*.glb'))]
 (ROOT/'docs/models/vr-cockpit-phase3-checks.json').write_text(json.dumps(report,indent=1)+'\n')
 print('PASS',len(report),'assets;',sum(r['controls'] for r in report),'controls; minimum clearance mm',min(v for r in report for c in r['clearances'] for v in c['clearance_mm'].values()))
