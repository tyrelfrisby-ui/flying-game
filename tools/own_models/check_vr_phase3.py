"""CPU P3a controls + P3b measured panel circles, coplanarity and needle binding checks."""
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
   # A Great Lakes quadrant throttle (v7, Ty 2026-11-03) is a lever on an arc, not the push-pull this box models;
   # its full-mesh sweep against every console part is measured by check_gl_quadrant.py, which must pass too.
   if label=='Throttle' and 'GL_Quadrant_'+seat[0] in idx:continue
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
 panel=check_panels(g,path) if path.parent.name=='extra-300-like' else None
 return dict(panel=panel,file=str(path.relative_to(ROOT)),controls=len(controls),clearances=rows,scope='Conservative 40mm control sphere plus full pull stroke; estimated knee spheres and primary sweep boxes. No anthropometric/VR approval.')
def check_panels(g,path):
 from vr_phase3b import LAYOUT,SCALES
 meta=g.doc['extras']['vrCockpitPhase3b'];world=g.world_matrices();nodes={n['name']:(i,n) for i,n in enumerate(g.doc['nodes'])}
 rig=json.loads((ROOT/'docs/models/ea300-own-rig.json').read_text())['rig'];report=[]
 def vertices(name):
  i,n=nodes[name];m=world[i]
  v=np.concatenate([g.array(p['attributes']['POSITION']) for p in g.doc['meshes'][n['mesh']]['primitives']])
  return np.array([builder(q) for q in v@m[:3,:3].T+m[:3,3]])
 for name in meta['retired_nodes']:
  assert 'mesh' not in nodes['Legacy_P3b_'+name][1],name
 # P3d (Ty 2026-11-02) restored the hood as RearPanel_Top_P3d / RearPanel_Glareshield_P3d; no OLD RearPanel_ node may return.
 assert not any(n.startswith('RearPanel_') and not n.endswith('_P3d') for n in nodes)
 for panel in meta['panels']:
  rows=panel['gauges'];assert [r['gauge'] for r in rows]==[r[0] for r in LAYOUT]
  circles=[];maxerr=0;count=0
  for row in rows:
   u,v,r=row['u'],row['v'],row['bezel_radius_m'];face=vertices(row['face'])
   err=float(abs(face[:,0]-panel['plane_x']).max());maxerr=max(maxerr,err);assert err<=.001
   assert np.allclose(face[:,0],face[0,0],atol=1e-6)
   # Triangle normals point toward pilot (builder -X); both sections same normal.
   normals=np.cross(face[1::3]-face[::3],face[2::3]-face[::3]);normals/=np.linalg.norm(normals,axis=1)[:,None]
   assert np.allclose(normals,[-1,0,0],atol=1e-5)
   assert np.allclose([-face[:,1].mean(),face[:,2].mean()],[u,v],atol=.002)
   bezel=vertices('Bezel_P3b_'+row['gauge']+'_'+panel['suffix'])
   measured=np.linalg.norm(bezel[:,1:]-np.array([-u,v]),axis=1).max();assert abs(measured-r)<1e-6
   assert abs(u)+r<=.22 and .075<=v-r and v+r<=.36
   circles.append((u,v,float(measured)))
   for name in row['needles']:
    k=name[:-2];k=k[7:] if k.startswith('Needle_') else k
    param,points,angles,unit=SCALES[k];entries=[e for e in rig if e['node']==name];assert len(entries)==1,(name,len(entries))
    e=entries[0];i,n=nodes[name];assert 'mesh' in n and e['param']==param
    initial=e['extra']['rig_dial_ang0_deg'];assert np.allclose(e['map'],list(zip(points,np.array(angles)-initial)))
    assert abs(float(np.interp(e['model'],points,np.array(angles)-initial)))<1e-6
    assert np.allclose(e['axis_gltf'],[-1,0,0]);assert np.allclose(world[i][:3,3],e['pivot_gltf'])
    p=builder(world[i][:3,3]);assert np.allclose(p[1:],[-u,v],atol=1e-6)
    if k.startswith('Alt'):assert n['extras']['rig_map_extrapolate']==1
    count+=1
  gaps=[np.linalg.norm(np.array(c[:2])-d[:2])-c[2]-d[2] for i,c in enumerate(circles) for d in circles[i+1:]]
  assert min(gaps)>=.006-1e-6,min(gaps)
  # Every relocated control's conservative sphere stays below the instrument area.
  for c in g.doc['extras']['vrCockpitPhase3a']['controls']:
   if c['seat']==panel['seat']:assert builder(c['pivot_gltf'])[2]+c['sweep_radius_m']<.075
  report.append(dict(seat=panel['seat'],gauges=len(rows),live_needle_nodes=count,min_bezel_gap_mm=round(float(min(gaps))*1000,4),max_face_plane_error_mm=round(maxerr*1000,6)))
 return report
if __name__=='__main__':
 report=[check(p) for model in SPECS for p in sorted((MODELS/model).glob('*own*.glb'))]
 (ROOT/'docs/models/vr-cockpit-phase3-checks.json').write_text(json.dumps(report,indent=1)+'\n')
 print('PASS',len(report),'assets;',sum(r['controls'] for r in report),'controls; minimum clearance mm',min(v for r in report for c in r['clearances'] for v in c['clearance_mm'].values()))

 for r in report:
  if r['panel']:print(r['file'],r['panel'])
