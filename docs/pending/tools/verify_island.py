#!/usr/bin/env python3
"""Validate Phase1B evidence; --rebuild also proves byte-identical regeneration.
A package PASS never changes a reported fit FAIL/UNVERIFIED or approves a gate.
"""
import argparse,hashlib,json,math,struct,subprocess,sys,xml.etree.ElementTree as ET
from pathlib import Path
P=Path(__file__).resolve().parents[1];REPO=P.parents[1]
OUTPUTS=['island-heightfield.f32','island-heightfield.json','island-elevation-base.png','island-elevation.svg','island-elevation.png','island-map.svg','island-map.png','island-fit-check.json','ISLAND-WORLD-PLAN.md']
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def canonical(o):return hashlib.sha256(json.dumps(o,sort_keys=True,separators=(',',':')).encode()).hexdigest()
def snapshot():return {n:sha(P/n) for n in OUTPUTS}
args=argparse.ArgumentParser();args.add_argument('--rebuild',action='store_true');args=args.parse_args()
if args.rebuild:
 before=snapshot();r=subprocess.run([sys.executable,str(P/'tools/generate_island.py'),'--png'],cwd=REPO,capture_output=True,text=True)
 if r.returncode:raise SystemExit(r.stdout+r.stderr)
 after=snapshot();assert before==after, 'Non-deterministic/stale outputs: '+str([n for n in OUTPUTS if before[n]!=after[n]])
 print('Full generator, full-area fit checks and both native rasters rerun: byte-identical.')
s=json.loads((P/'island-spec.json').read_text());w=s['world'];ifr=s['ifr'];meta=json.loads((P/'island-heightfield.json').read_text());m=json.loads((P/'island-fit-check.json').read_text())
base=json.loads(subprocess.check_output(['git','show','048d9cd:docs/pending/island-spec.json'],cwd=REPO))
assert s['phase']=='1B' and s['gate']=='G1 — PENDING Ty'
assert canonical(w)==s['revision_audit']['final_world_sha256'], 'Stale final allocation ledger'
assert w['radius_km']==20 and w['frame']['variation_east_deg']==7
assert ifr==base['ifr'], 'Claude-owned IFR data changed'
assert len(ifr['approaches'])==12 and len([q for q in ifr['approaches'] if q.get('only_AN_range_approach')])==1
ksna=[q for q in ifr['approaches'] if q['airport']=='KSNA'];assert len(ksna)==1 and ksna[0]['title']=='LOC BC RWY 2L' and ksna[0]['glideslope'] is False
locked=dict(PHDH=14,PANC=151,PALH=79,PATK=365,KSNA=56,KMYL=5024,**{'3U2':4960},KMEV=4724,KPGA=4317,KAVX=1602,DRAKE=4700)
for a in w['airports']:
 if a['id'] not in locked:continue
 assert a['elevation_ft']==locked[a['id']]
 old=next(q for q in base['world']['airports'] if q['id']==a['id'])
 assert len(a['runways'])==len(old['runways'])
 for r,q in zip(a['runways'],old['runways']):
  for key in ['length_ft','width_ft','length_m','width_m']:
   if key in q:assert r[key]==q[key],(a['id'],key)
assert w['catalina']==base['world']['catalina']
assert next(q for q in w['water'] if q['id']=='Lake Powell')['elevation_ft']==3700
assert all(1000<=q['ski_zone']['elevation_ft']<=1300 for q in w['glaciers'])
assert len(w['airparks'])==14 and len(w['hazards']['towers'])==6 and len(w['hazards']['wires'])==5
assert all(q['charted'] for q in w['hazards']['towers']+w['hazards']['wires'])
assert {z['id'] for z in w['combat_zones']}=={'CZ1','CZ2','CZ3','CZ5'} and not any(t['id']=='CZ4' for t in w['starts'])
assert len(w['aerobatic_boxes'])==3 and sum(q['density_altitude']=='high' for q in w['aerobatic_boxes'])==1
assert next(q for q in w['aerobatic_boxes'] if q['density_altitude']=='high')['airport']=='KMYL'
for b in w['aerobatic_boxes']:
 assert b['side_m']==1000 and b['floor_agl_m']==100 and b['ceiling_agl_m']==1100
 assert abs((b['floor_msl_ft_target']-b['centre_ground_ft'])*.3048-100)<.001
 assert abs((b['ceiling_msl_ft_target']-b['centre_ground_ft'])*.3048-1100)<.001
for b in w['aerobatic_boxes'][:2]:
 assert len(b['rafts'])==9 and len(b['corner_buoys'])==4 and b['total_raft_count']==10
 assert all(q['moored'] for q in b['rafts']+[b['judge_raft']]+b['corner_buoys'])
 assert sorted(q['shape'] for q in b['rafts'])==['L']*4+['T']*4+['cross']
for z in w['combat_zones']:
 assert z['default_status']=='HOT' and set(z['available_statuses'])=={'HOT','COLD'} and z['enabled']
 assert all(k in z['hot_entry_behavior'] for k in ['HOT','COLD','entry_definition'])
 assert z['gunnery_targets'] and all('ground' in t['platform'] for t in z['gunnery_targets'])
 assert {'bridge','rock arch'}<={f['type'] for f in z['combat_features']}
 assert z['drone_orbit']['count']==3
assert len(w['outer_islands'])==5 and len(w['outer_navaid_sites'])==5 and len(w['landable_beaches'])==6
assert all(r['numbers_magnetic']==r['id'] for a in w['airports'] for r in a['runways'])
assert len([q for q in w['islets'] if q['id'].startswith('SS')])==12
for i in w['outer_islands']:
 a=next(a for a in w['airports'] if a['id']==i['airport']);assert len(a['runways'])>=1 and a['start_group'] not in ['coastal','inland']
for b in w['landable_beaches']:
 assert b['surface']=='hard sand' and b['length_m']>0 and b['width_m']>0 and 'slope_percent'in b and b['pad']['shoulder_m']==60
assert next(q for q in w['airparks'] if q['id']=='AP8')['float_pond']['surface']=='water'
starts=[a for a in w['airports'] if a['start_group'] in ['coastal','inland']];assert len(starts)==8
for a in starts:
 for mode in ['OnTheRunway','OnFinal']:assert any(t.get('airport')==a['id'] and t['mode']==mode for t in w['starts'])
assert [r['rule'] for r in m['fit_check']]==list(range(1,11))
assert not m['grade_failures'] and m['maximum_grade']['grade_percent']<=35 and m['flat_pairs_checked']>500
assert not m['spacing_failures'] and not m['shore_setback_failures'] and not m['pad_conflicts'] and not m['pad_surface_errors']
assert not m['water_pad_inundations'] and not m['water_surface_checks']['different_level_overlaps']
assert m['supplied_final_failures']==0 and not m['approach_obstacle_screen']
allowed=set()
assert {a['title'] for a in m['approach_failures']}==allowed, 'Unresolved operational field ends: '+str([a['title'] for a in m['approach_failures']])
assert not m['approach_failures'] and len(m['approach_terrain_screen'])==113
assert not any(q['airport']=='DRAKE' and q['title'].endswith('end B') for q in m['approach_terrain_screen'])
assert len(m['airway_checks'])==23 and not m['airway_failures']
assert all(a['clearance_ft']>=2000-.02 for a in m['airway_checks'])
assert not m['ring_failures'] and len(m['ring_volumes'])>300 and m['ring_missing'] and 'UNVERIFIED' in m['fit_check'][4]['status']
assert m['pirate_cave_verification']['status'].startswith('PASS') and m['drake_shell_verification']['status'].startswith('PASS')
f=next(f for f in w['features'] if f['id']=='F2');assert f['mouth_m']==[60,26] and f['chamber_radius_m']==90
assert m['drake_shell_verification']['minimum_rock_cover_m']>=60
assert all(c['maximum_steady_climb_path_penetration_m']>0 and c['status'].startswith('PASS') and 'UNVERIFIED'in c['flight_validation'] for c in m['box_canyon_checks'])
assert all(q['passes_known'] for q in m['box_clearances']+m['zone_clearances'])
assert all(q['passes'] for q in m['combat_feature_checks']) and m['additional_design_checks']['all_pass']
land=next(z for z in m['zone_clearances'] if z['zone']=='CZ5');assert land['over_land_margin_km']>0 and land['minimum_final_boundary_km']>=2.05 and land['minimum_full_airway_boundary_km']>=2.05
assert land['floor_agl_ft_range']==[12.0,140.0] and land['ceiling_agl_ft_range']==[2312.0,2440.0]
under=[o for z in m['zone_clearances'] for o in z['airport_overlays']];assert len(under)==2 and all(o['passes'] for o in under) and any(o['entire_island_under_active_zone'] for o in under)
assert {o['airport'] for o in under}=={'KITE','RIME'} and all(o['vertical_clearance_ft']>=2000 for o in under)
assert all(o['recommended_default'] and len(o['options'])>=(1 if o['id']=='TY2' else 2) for o in s['open_for_ty'])
import generate_island as author
assert all(abs(author.height(*q['centre'])/author.FT-q['top_ft'])<.001 for q in w['height_field']['peaks'])
assert all(all(k in c for k in ['item','old','new','reason']) for c in s['changes'])
for name in ['island-map.svg','island-elevation.svg']:assert ET.parse(P/name).getroot().tag.endswith('svg')
for name,expected in [('island-map.png',(1800,2100)),('island-elevation.png',(1800,2100)),('island-elevation-base.png',(401,401))]:
 raw=(P/name).read_bytes();assert raw[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',raw[16:24])==expected
raw=(P/'island-heightfield.f32').read_bytes();assert len(raw)==401*401*4 and hashlib.sha256(raw).hexdigest()==meta['sha256']
assert sha(P/'island-spec.json')==meta['spec_sha256']
for q in json.loads((P/'island-source-audit.json').read_text())['files']:
 if q['path'].startswith(('configs/','src/FlyingGame.Core/Nav/','src/FlyingGame.Core/Orography','src/FlyingGame.Core/Atmosphere','src/FlyingGame.Core/Lift')):
  assert sha(REPO/q['path'])==q['sha256'], 'Claude-owned source changed: '+q['path']
# A few independent exact geometry identities protect the full-area algorithms themselves.
from fit_revision import clip,subtract,area,capsule,cross
square=[(0,0),(2,0),(2,2),(0,2)];cut=[(1,-1),(3,-1),(3,3),(1,3)]
assert abs(abs(area(clip(square,cut)))-2)<1e-10 and abs(sum(abs(area(p)) for p in subtract(square,cut))-2)<1e-10
cap=capsule((0,0),(3,0),1)
for j in range(128):
 p=(math.cos(j*math.pi/64),math.sin(j*math.pi/64));assert all(cross(a,b,p)>=-1e-9 for a,b in zip(cap,cap[1:]+cap[:1]))
manifest=dict(source_audit_sha256=sha(P/'island-source-audit.json'),phase='1B',passes=True,comparison='Two complete consecutive executions, including both native rasters; byte-identical in this environment.',spec_sha256=sha(P/'island-spec.json'),ifr_canonical_sha256=canonical(ifr),outputs=snapshot(),tools={p.name:sha(p) for p in sorted((P/'tools').glob('*')) if p.suffix in ['.py','.swift']},external_read_inputs={'configs/aircraft/c172-like.json':sha(REPO/'configs/aircraft/c172-like.json')})
if args.rebuild:(P/'island-determinism.json').write_text(json.dumps(manifest,indent=2)+'\n')
else:
 old=json.loads((P/'island-determinism.json').read_text());assert old==manifest, 'Missing/stale determinism evidence; run --rebuild'
print('Phase1B package PASS. 113 operational fans; no Drake reverse final. Missing IFR protection and flight validation remain UNVERIFIED. G1 is PENDING Ty.')
