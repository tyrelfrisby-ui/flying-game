#!/usr/bin/env python3
"""The Frank Church valleys (2026-11-03, lane/claude-frank-church-valleys): the island's Middle Fork strips put DOWN in
river canyons instead of on the mesa tops Ty found them on ("always deep in the valleys").

This is the authoring source for two things in docs/pending/island-spec.json, which it rewrites in place:
  * the "C2-*" canyons — each river as a chain of straight reaches with a floor that falls downstream, wide flats
    where the strips are, a "walls" lower bound for the relief generator, and "floor": "exact" on the strip reaches
    ("ceiling" on the gorges where a river leaves for lower ground);
  * the nine "backcountry_strips" — placed on a reach (t along it, metres to one bank), set so that BOTH ends sit at
    least `bench` m above the river beside them, one-way strips rotated up into their wall; plus the F14 gravel bar
    on the Middle Fork's east-west reach.
Run from the repo root, then: python3 tools/island/generate_runtime.py; dotnet run -c Release --project
tools/island/TerrainGen -- generate; python3 tools/island/generate_gradient.py (only if the evaluator changed);
and the FrankChurchTests. E,N in km, heights in metres MSL.
"""
import json,math
FT=.3048
def vec(h):return math.sin(math.radians(h)),math.cos(math.radians(h))
def hdg(dx,dy):return math.degrees(math.atan2(dx,dy))%360
RIVERS={
 'MF':dict(name='Middle Fork',top=1500,v=[(2.80,-1.95,1128,240),(2.95,-1.10,1126,500),(3.10,0.45,1118,640),(3.95,0.62,1100,640),(4.25,0.95,1090,420),(4.40,2.45,1062,540),(4.75,2.85,1040,260),(5.10,3.25,850,120),(5.45,3.65,600,100)]),
 'SC':dict(name='Sulphur Creek',top=1500,v=[(2.55,2.30,1180,320),(2.70,1.85,1165,360),(3.10,0.45,1118,540)]),
 'BC':dict(name='Big Creek',top=1500,v=[(5.05,0.62,1145,300),(4.25,0.95,1090,420)]),
 'CC':dict(name='Chamberlain Creek',top=1580,v=[(-0.05,-1.05,1226,500),(-0.60,-3.10,1186,540),(-1.45,-4.25,1160,520),(-1.75,-4.65,1100,220),(-1.85,-5.15,700,100)]),
}
STRIPS=[ # id, river, seg, t, side m (+ left of downstream), min bench m, slope %, L, W, surface, one_way, rot (deg from upstream)
 ('Thomas Creek','MF',1,.50,-185, 5,1.2,1220,30,'gravel',False,0),
 ('Mackay Bar',  'MF',2,.64, 280, 5,1.8, 460,24,'gravel',False,0),
 ('Indian Creek','MF',4,.50,-185, 5,1.6,1220,30,'gravel',False,0),
 ('Sulphur Creek','SC',1,.50,-185,5,2.0, 915,30,'turf',  False,0),
 ('Cabin Creek', 'BC',0,.32, 110, 6,6.5, 460,20,'dirt',  True, 0),
 ('Soldier Bar', 'CC',1,.22,-200,12,4.5, 400,20,'dirt',  True,-30),
 ('Chamberlain', 'CC',1,.55, 185, 5,1.2,1100,40,'gravel',False,0),
 ('Dewey Moore', 'CC',0,.26, 200,10,7.0, 370,18,'dirt',  True, 32),
 ('Big Creek',   'CC',0,.74, 185, 5,1.0,1070,34,'gravel',False,0),
]
def seg(r,i):a=RIVERS[r]['v'][i];b=RIVERS[r]['v'][i+1];return a,b
def floor_near(r,p):
 """river floor at the thalweg point nearest p, over the whole river"""
 best=(1e9,0)
 for i in range(len(RIVERS[r]['v'])-1):
  a,b=seg(r,i);dx,dy=b[0]-a[0],b[1]-a[1];q=dx*dx+dy*dy;t=max(0,min(1,((p[0]-a[0])*dx+(p[1]-a[1])*dy)/q))
  d=math.hypot(p[0]-a[0]-t*dx,p[1]-a[1]-t*dy)
  if d<best[0]:best=(d,a[2]+(b[2]-a[2])*t)
 return best[1],best[0]
out=dict(canyons=[],strips=[])
for key,r in RIVERS.items():
 v=r['v']
 for i in range(len(v)-1):
  a,b=v[i],v[i+1]
  out['canyons'].append(dict(id=f'C2-{key}{i+1}',name=r['name'],path=[[a[0],a[1]],[b[0],b[1]]],rim_width_m=[a[3]+400,b[3]+400],floor_width_m=[a[3],b[3]],
     floor='ceiling' if (key=='MF' and i>=5) or (key=='CC' and i>=3) else 'exact',depth_m=[0,0],floor_ft=[round(a[2]/FT,1),round(b[2]/FT,1)],walls=dict(top_ft=round(r['top']/FT),run_m=600,crest_m=150)))
for (sid,r,i,t,side,bench,slope,L,W,surf,ow,rot) in STRIPS:
 a,b=seg(r,i);e,n=a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t;dn=hdg(b[0]-a[0],b[1]-a[1]);up=(dn+180)%360
 h=(up+rot)%360;u=vec(h);lv=vec((dn-90)%360)
 cx,cy=e+lv[0]*side/1000,n+lv[1]*side/1000
 lo_end=(cx-u[0]*L/2000,cy-u[1]*L/2000);hi_end=(cx+u[0]*L/2000,cy+u[1]*L/2000)
 zl,_=floor_near(r,lo_end);zh,_=floor_near(r,hi_end);zc,dc=floor_near(r,(cx,cy))
 rise=L*slope/100
 # strip low end z0: both ends at least `bench` above the river beside them
 z0=max(zl+bench,zh+bench-rise);elev=z0+rise/2
 s=dict(id=sid,centre=[round(cx,4),round(cy,4)],heading_true_deg=round(h,1),length_m=L,width_m=W,elevation_ft=round(elev/FT,1),slope_percent=slope,surface=surf,
        one_way_true_deg=round(h,1) if ow else None,river=RIVERS[r]['name'])
 out['strips'].append(s)
 print(f"{sid:14s} {r} ({cx:.3f},{cy:.3f}) hdg {h:5.1f} L {L} elev {elev:6.1f} m ({elev/FT:5.0f} ft) bench low/high {z0-zl:5.1f}/{z0+rise-zh:5.1f} river-dist {dc*1000:4.0f} m slope {slope} {'ONE-WAY land '+str(round(h)) if ow else ''}")
c=[c for c in out['canyons'] if c['id']=='C2-MF3'][0]['path']
out['f14']=[round((c[0][0]+c[1][0])/2,4),round((c[0][1]+c[1][1])/2,4)]   # the F14 bar on the Middle Fork's E-W reach
L=out

# ---- write it into the spec -------------------------------------------------------------------------------
p='docs/pending/island-spec.json'; S=json.load(open(p)); W=S['world'];
W['canyons']=[c for c in W['canyons'] if not c['id'].startswith('C2-')]+L['canyons']
by={s['id']:s for s in L['strips']}
for a in W['backcountry_strips']:
  s=by[a['id']]
  for k in ('centre','heading_true_deg','length_m','width_m','elevation_ft','slope_percent','surface','one_way_true_deg','river'):a[k]=s[k]
  ow=s['one_way_true_deg'] is not None
  a['pad']=dict(shoulder_m=30,end_shoulder_m=50,blend_m=80,sloped_end_shoulders=True) if ow else dict(shoulder_m=40,end_shoulder_m=60,blend_m=110,sloped_end_shoulders=True)
  # Briefing data: which way you land, which way you leave, and whether you can go around.
  a['landing_true_deg']=[s['one_way_true_deg']] if ow else [s['heading_true_deg'],round((s['heading_true_deg']+180)%360,1)]
  a['takeoff_true_deg']=[round((s['one_way_true_deg']+180)%360,1)] if ow else a['landing_true_deg']
  a['go_around']='none: land uphill toward the closing wall; take off downhill, down-canyon' if ow else 'down-canyon either way'
W['features']=[dict(f,centre=L['f14']) if f['id']=='F14' else f for f in W['features']]
json.dump(S,open(p,'w'),indent=2,ensure_ascii=False); open(p,'a').write('\n')
print('patched',len(L['canyons']),'canyons',len(L['strips']),'strips, F14 at',L['f14'])
