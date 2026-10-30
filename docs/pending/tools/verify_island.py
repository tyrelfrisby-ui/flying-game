#!/usr/bin/env python3
"""Validate the Phase 1A package; does not run or alter the game."""
import hashlib,json,struct,xml.etree.ElementTree as ET
from pathlib import Path
P=Path(__file__).resolve().parents[1]
s=json.loads((P/'island-spec.json').read_text());w=s['world'];ifr=s['ifr'];meta=json.loads((P/'island-heightfield.json').read_text())
assert s['phase']=='1A' and s['gate']=='G1a — PENDING Ty'
assert w['radius_km']==20 and w['frame']['variation_east_deg']==7
assert len(w['airparks'])==14 and len(w['hazards']['towers'])==6 and len(w['hazards']['wires'])==5
assert len(w['aerobatic_boxes'])==3 and sum(z['enabled'] for z in w['combat_zones'])==3
assert not w['combat_zones'][3]['enabled'] and w['combat_zones'][3]['optional']
starts=[a for a in w['airports'] if a['start_group'] in ['coastal','inland']]
assert len(starts)==8
for a in starts:
 for mode in ['OnTheRunway','OnFinal']:assert any(t.get('airport')==a['id'] and t['mode']==mode for t in w['starts'])
assert next(a for a in w['airports'] if a['id']=='PANC')['runways'][0]['length_ft']==10865
assert {a['id']:a['elevation_ft'] for a in w['airports']}==dict(PHDH=14,PANC=151,PALH=79,PATK=365,KSNA=56,KMYL=5024,**{'3U2':4960},KMEV=4724,KPGA=4317,KAVX=1602,DRAKE=4700)
assert next(a for a in w['water'] if a['id']=='Lake Powell')['elevation_ft']==3700
assert all(1000<=g['ski_zone']['elevation_ft']<=1300 for g in w['glaciers'])
assert len(ifr['approaches'])==12
ksna=[a for a in ifr['approaches'] if a['airport']=='KSNA'];assert len(ksna)==1 and ksna[0]['title']=='LOC BC RWY 2L' and ksna[0]['glideslope'] is False
ids=[n['ident'] for k in ['vor','ndb','localizers'] for n in ifr['navaids'][k]]+[ifr['navaids']['range']['ident']];assert len(ids)==len(set(ids))
assert len([a for a in ifr['approaches'] if a.get('only_AN_range_approach')])==1
assert len(next(f for f in w['features'] if f['id']=='RACE')['elements'])==12
for name in ['island-map.svg']:
 root=ET.parse(P/name).getroot();assert root.tag.endswith('svg')
for name,expected in [('island-map.png',(1800,2100)),('island-elevation.png',(401,401))]:
 data=(P/name).read_bytes();assert data[:8]==b'\x89PNG\r\n\x1a\n';assert struct.unpack('>II',data[16:24])==expected
raw=(P/'island-heightfield.f32').read_bytes();assert len(raw)==401*401*4
assert hashlib.sha256(raw).hexdigest()==meta['sha256']
assert hashlib.sha256((P/'island-spec.json').read_bytes()).hexdigest()==meta['spec_sha256']
fit=json.loads((P/'island-fit-check.json').read_text());assert [r['rule'] for r in fit['fit_check']]==list(range(1,11))
assert all(all(k in c for k in ['item','old','new','reason']) for c in s['changes'])
print('Phase 1A package checks passed. Known fit failures are intentionally reported; this is not G1 approval.')
