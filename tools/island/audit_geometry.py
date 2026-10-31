#!/usr/bin/env python3
"""Screen actual generated blocks against all known whole final fans and airway budgets."""
import json,math,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'docs/pending/tools'))
import generate_island as g
from fit_revision import clip,capsule
src=json.load(open(sys.argv[1]));fail=[];checks=0
allblocks=list(src['blocks'])
for s in src['slopes']:allblocks.append(dict(Id=s['Id'],X=s['x'],Y=s['y'],Top=s['Elevation']+abs(s['Grade'])*s['Length']/2,Length=s['Length'],Width=s['Width'],Heading=s['Heading']))
for i,a in enumerate(src['arches']):
 dx=a['Bx']-a['Ax'];dy=a['By']-a['Ay'];length=math.hypot(dx,dy)
 allblocks.append(dict(Id='arch:'+str(i),X=(a['Ax']+a['Bx'])/2,Y=(a['Ay']+a['By'])/2,Top=a['FootM']+a['OpeningM']+a['ThickM']*2.2,Length=length,Width=a['WidthM'],Heading=math.degrees(math.atan2(dy,dx))))
for a in src['rings']:allblocks.append(dict(Id=a['Id'],X=a['x'],Y=a['y'],Top=a['Up']+a['Radius']+1.4,Length=2*(a['Radius']+1.4),Width=2*(a['Radius']+1.4),Heading=0))
for b in allblocks:
 c=[b['Y']/1000,b['X']/1000];u=g.vec(b['Heading']);v=[u[1],-u[0]]
 poly=[[c[k]+s*b['Length']/2000*u[k]+t*b['Width']/2000*v[k] for k in [0,1]] for s,t in [(-1,-1),(1,-1),(1,1),(-1,1)]]
 for f in g.finals()+g.runway_finals():
  pp=clip([tuple(p) for p in poly],g.finalpoly(f))
  if not pp:continue
  u0=g.vec(f['course_true_deg']);ratio=34 if f['precision'] else 20
  cap=min(f['elevation_ft']*g.FT-((p[0]-f['threshold'][0])*u0[0]+(p[1]-f['threshold'][1])*u0[1])*1000/ratio for p in pp)
  checks+=1
  if b['Top']>cap+.05:fail.append(dict(id=b['Id'],kind='final',surface=f['title'],penetration_m=round(b['Top']-cap,3)))
 for e in g.CLEARANCE_ENVELOPES:
  if e['kind']!='airway':continue
  if not clip([tuple(p) for p in poly],capsule(e['a'],e['b'],3.704)):continue
  checks+=1
  if b['Top']>e['cap_m']+25*g.FT+.05:fail.append(dict(id=b['Id'],kind='airway',penetration_m=round(b['Top']-e['cap_m']-25*g.FT,3)))
report=dict(block_count=len(src['blocks']),checks=checks,failures=fail,passes=not fail,scope='All oriented built block/ramp footprints, conservative whole arch/ring bounds, whole known operational finalfans and2nm airway/perimeter swaths; no Drake exemption. Cables/tower/wire envelopes remain separately conservatively checked in fit. Unknown IFR stays unverified.')
(ROOT/'docs/pending/island-runtime-solids.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2)[:12000])
