#!/usr/bin/env python3
"""Phase 1A review artefacts. Python 3 standard library only.
Run from any directory. Optional --png uses macOS AppKit through Swift; never installs dependencies.
The binary is a documented review interchange, not an existing engine import format.
"""
import argparse, hashlib, html, itertools, json, math, os, struct, subprocess, tempfile, zlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
S=json.loads((ROOT/'island-spec.json').read_text()); W=S['world']; FT=.3048

def smooth(v):
 v=max(0.,min(1.,v)); return v*v*(3-2*v)
def dist(a,b):return math.hypot(a[0]-b[0],a[1]-b[1])
def vec(h):return math.sin(math.radians(h)),math.cos(math.radians(h))
def seg(p,a,b):
 dx=b[0]-a[0];dy=b[1]-a[1];q=dx*dx+dy*dy
 t=max(0,min(1,((p[0]-a[0])*dx+(p[1]-a[1])*dy)/q)) if q else 0
 return dist(p,[a[0]+t*dx,a[1]+t*dy]),t

def inside(p,poly):
 x,y=p;c=False
 for a,b in zip(poly,poly[1:]+poly[:1]):
  if (a[1]>y)!=(b[1]>y) and x<(b[0]-a[0])*(y-a[1])/(b[1]-a[1])+a[0]:c=not c
 return c

def shore(p,poly):
 d=min(seg(p,a,b)[0] for a,b in zip(poly,poly[1:]+poly[:1]));return d if inside(p,poly) else -d

def ellipse(c,axes,h,n=160):
 u=vec(h);v=(u[1],-u[0]);return [[c[0]+axes[0]*u[0]*math.cos(i*2*math.pi/n)+axes[1]*v[0]*math.sin(i*2*math.pi/n),c[1]+axes[0]*u[1]*math.cos(i*2*math.pi/n)+axes[1]*v[1]*math.sin(i*2*math.pi/n)] for i in range(n)]
CAT=ellipse(W['catalina']['centre'],W['catalina']['semi_axes_km'],W['catalina']['long_axis_true_deg'])
COAST=W['coast_polygon']
FIELDS=[]
for a in W['airports']:
 for r in a['runways']:FIELDS.append(dict(r,field=a['id'],key=a['id']+' '+r['id'],elevation_ft=a['elevation_ft'],start_group=a['start_group']))
for a in W['airparks']:FIELDS.append(dict(a,field=a['id'],key=a['id'],start_group=None))
for a in W['backcountry_strips']:FIELDS.append(dict(a,field=a['id'],key=a['id'],start_group=None))

def dimensions(r):return r.get('length_m',r.get('length_ft',0)*FT)/1000,r.get('width_m',r.get('width_ft',0)*FT)/1000

def local(p,r):
 ux,uy=vec(r['heading_true_deg']);x=p[0]-r['centre'][0];y=p[1]-r['centre'][1];return x*ux+y*uy,x*uy-y*ux

def rect(r,padded=False):
 l,b=dimensions(r);pad=r.get('pad',{});l=l/2+(pad.get('end_shoulder_m',60)/1000 if padded else 0);b=b/2+(pad.get('shoulder_m',60)/1000 if padded else 0);u=vec(r['heading_true_deg']);v=(u[1],-u[0]);c=r['centre']
 return [[c[0]+a*l*u[0]+s*b*v[0],c[1]+a*l*u[1]+s*b*v[1]] for a,s in [(-1,-1),(1,-1),(1,1),(-1,1)]]

def rectgap(a,b):
 pa,pb=rect(a,True),rect(b,True)
 # Separating-axis overlap, then vertex-edge distance.
 overlap=True
 for poly in [pa,pb]:
  for x,y in zip(poly,poly[1:]+poly[:1]):
   n=(y[1]-x[1],x[0]-y[0]);aa=[p[0]*n[0]+p[1]*n[1] for p in pa];bb=[p[0]*n[0]+p[1]*n[1] for p in pb]
   if max(aa)<min(bb) or max(bb)<min(aa):overlap=False
 if overlap:return 0.
 return min([seg(p,x,y)[0] for p in pa for x,y in zip(pb,pb[1:]+pb[:1])]+[seg(p,x,y)[0] for p in pb for x,y in zip(pa,pa[1:]+pa[:1])])

def pathnear(p,path):
 lengths=[dist(a,b) for a,b in zip(path,path[1:])];total=sum(lengths);run=0;best=(float('inf'),0)
 for a,b,l in zip(path,path[1:],lengths):
  d,t=seg(p,a,b)
  if d<best[0]:best=(d,(run+t*l)/max(.00001,total))
  run+=l
 return best

def height(x,y):
 p=(x,y);dm=shore(p,COAST);dc=shore(p,CAT);d=max(dm,dc)
 if d<-.35:return -30.
 if dc>dm:
  h=30*FT
 else:
  h=(30+4570*smooth(dm/2))*FT
 for region in W['height_field'].get('regional_overrides',[]):
  rr=math.hypot((x-region['centre'][0])/region['radii_km'][0],(y-region['centre'][1])/region['radii_km'][1]);a=1-smooth(rr);h=h*(1-a)+region['elevation_ft']*FT*a
 # Compact raised envelopes. Each reaches zero relief smoothly at radius.
 for q in W['height_field']['crests']:
  dd,t=pathnear(p,q['path']);a=1-smooth(dd/q['width_km']);h=max(h,h+(q['top_ft']*FT-h)*a)
 for q in W['height_field']['peaks']:
  a=1-smooth(dist(p,q['centre'])/q['radius_km']);h=max(h,h+(q['top_ft']*FT-h)*a)
 # Lakes cut bed; independent water surfaces are recorded in the spec.
 for lake in W['water']:
  rx,ry=lake['radii_km'];cx,cy=lake['centre'];rr=math.hypot((x-cx)/rx,(y-cy)/ry);a=1-smooth((rr-.85)/.45)
  h=h*(1-a)+min(h,lake['elevation_ft']*FT-lake['depth_m'])*a
 for g in W['glaciers']:
  dd,t=pathnear(p,g['path']);a=(1-smooth((dd-g['half_width_m']/1000)/.3))*smooth(t/.12)
  z=(g['head_ft']*(1-t)+g['ski_zone']['elevation_ft']*t)*FT;h=h*(1-a)+z*a
  q=g['ski_zone'];a=1-smooth((dist(p,q['centre'])-.15)/.25);h=h*(1-a)+q['elevation_ft']*FT*a
 for c in W['canyons']:
  dd,t=pathnear(p,c['path']);rw=c['rim_width_m'][0]*(1-t)+c['rim_width_m'][-1]*t;fw=c['floor_width_m'][0]*(1-t)+c['floor_width_m'][-1]*t;z=(c['floor_ft'][0]*(1-t)+c['floor_ft'][-1]*t)*FT
  a=1-smooth((dd*1000-fw/2)/max(1,(rw-fw)/2));h=h*(1-a)+min(h,z)*a
 for lesson in W['lesson_terrain']:
  dd,t=pathnear(p,lesson['safe_path']);a=1-smooth((dd-.12)/.25);z=lesson['saddle_ft']*FT;h=h*(1-a)+min(h,z)*a
  dd,t=pathnear(p,lesson['box_path']);a=1-smooth((dd-.08)/.12);z=lesson['saddle_ft']*FT+lesson['box_floor_rise_m']*t;h=h*(1-a)+z*a
 # McCall box bench. Explicitly level only within 1 km square; blend outside.
 b=W['aerobatic_boxes'][2];a=1-smooth((max(abs(x-b['centre'][0]),abs(y-b['centre'][1]))-.5)/.2);h=h*(1-a)+5000*FT*a
 # Preserve all authored peak heights after erosional cuts, with continuous compact influence.
 for q in W['height_field']['peaks']:
  rr=dist(p,q['centre']);a=1-smooth((rr-.12)/.28);h=h*(1-a)+q['top_ft']*FT*a
 # Simultaneous overlapping claims use nearest rectangle; conflicts reported separately.
 claims=[]
 for original in FIELDS:
  r=original
  if r['field']=='DRAKE':
   # Cave interior is not an open trench. Only the visible 152 m is a terrain pad.
   full,_=dimensions(r);u=vec(r['heading_true_deg']);offset=-full/2+.076
   r=dict(r,length_m=152,centre=[r['centre'][0]+offset*u[0],r['centre'][1]+offset*u[1]])
  along,cross=local(p,r);l,b=dimensions(r);pad=r['pad'];da=max(0,abs(along)-l/2-pad.get('end_shoulder_m',60)/1000);db=max(0,abs(cross)-b/2-pad['shoulder_m']/1000);dd=math.hypot(da,db);blend=pad['blend_m']/1000
  if dd<blend:
   z=r['elevation_ft']*FT+max(-l/2,min(l/2,along))*1000*r.get('slope_percent',0)/100-(W['height_field']['water_lane_bed_offset_m'] if r['surface']=='water' else 0)
   claims.append((dd,1-smooth(dd/blend),z,r['key']))
 if claims:
  dd,a,z,_=min(claims);h=h*(1-a)+z*a
 # Coast fade is last; a field crossing the shore will FAIL rather than fabricate land silently.
 a=smooth(d/W['edge']['land_shore_fade_km']);return -30+(h+30)*a

def png(path,width,height,pixels):
 def chunk(k,v):return struct.pack('>I',len(v))+k+v+struct.pack('>I',zlib.crc32(k+v)&0xffffffff)
 raw=b''.join(b'\0'+pixels[y*width*3:(y+1)*width*3] for y in range(height))
 path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw,9))+chunk(b'IEND',b''))

def tint(z):
 if z<0:return (24,68,94)
 stops=[(0,(176,202,150)),(700,(115,155,99)),(1500,(191,173,128)),(2200,(139,119,110)),(2800,(246,244,235))]
 for (a,c),(b,d) in zip(stops,stops[1:]):
  if z<=b:
   t=max(0,(z-a)/(b-a));return tuple(round(x+(y-x)*t) for x,y in zip(c,d))
 return stops[-1][1]

def bake():
 grid=W['height_field']['grid'];n=grid['rows'];step=grid['spacing_m']/1000;low=grid['min_km'];data=[]
 for j in range(n):
  for i in range(n):data.append(height(low+i*step,low+j*step))
 raw=struct.pack('<'+'f'*len(data),*data);(ROOT/'island-heightfield.f32').write_bytes(raw)
 meta=dict(grid,byte_order='little',altitude_units='m MSL',row_order='south to north',column_order='west to east',sha256=hashlib.sha256(raw).hexdigest(),spec_sha256=hashlib.sha256((ROOT/'island-spec.json').read_bytes()).hexdigest(),engine_import=False)
 (ROOT/'island-heightfield.json').write_text(json.dumps(meta,indent=2)+'\n')
 pixels=bytearray()
 for j in range(n-1,-1,-1):
  for i in range(n):
   z=data[j*n+i];dx=data[j*n+min(n-1,i+1)]-data[j*n+max(0,i-1)];dy=data[min(n-1,j+1)*n+i]-data[max(0,j-1)*n+i];shade=max(.55,min(1.25,1+(-dx+dy)/700));pixels.extend(min(255,round(c*shade)) for c in tint(z))
 png(ROOT/'island-elevation.png',n,n,pixels)
 return data

# A deliberately thin overlay, never published procedure geometry.
def finals():
 out=[]
 for a in S['ifr']['approaches']:
  field=next(q for q in W['airports'] if q['id']==a['airport']);r=field['runways'][0];c=a['final_magnetic_deg']+7;u=vec(c);ru=vec(r['heading_true_deg']);length=dimensions(r)[0]
  same=math.cos(math.radians(c-r['heading_true_deg']))>=0
  th=a.get('threshold',[r['centre'][0]+(-1 if same else 1)*ru[0]*length/2,r['centre'][1]+(-1 if same else 1)*ru[1]*length/2]);outer=[th[0]-u[0]*7.408,th[1]-u[1]*7.408]
  out.append(dict(airport=a['airport'],title=a['title'],threshold=th,outer=outer,course_true_deg=c,precision=('ILS' in a['title'] or 'LOC' in a['title'] or 'LDA' in a['title']),elevation_ft=field['elevation_ft'],circling=a.get('circling_only',False)))
 return out

def measure():
 measurements={};rows=[]
 def row(n,status,evidence):rows.append(dict(rule=n,status=status,evidence=evidence))
 # Verify across each runway/pad from the continuous function, not only coarse grid nodes.
 worst=(0,'');pad_samples=0
 for r in FIELDS:
  l,b=dimensions(r);u=vec(r['heading_true_deg']);v=(u[1],-u[0])
  for t in ([-.5] if r['field']=='DRAKE' else [-.5,0,.5]):
   for c in [-.5,0,.5]:
    x=r['centre'][0]+t*l*u[0]+c*b*v[0];y=r['centre'][1]+t*l*u[1]+c*b*v[1];want=r['elevation_ft']*FT+t*l*1000*r.get('slope_percent',0)/100-(W['height_field']['water_lane_bed_offset_m'] if r['surface']=='water' else 0);err=abs(height(x,y)-want);pad_samples+=1
    if err>worst[0]:worst=(err,r['key'])
 conflicts=[];grades=[]
 for a,b in itertools.combinations(FIELDS,2):
  if a['field']==b['field']:continue
  gap=rectgap(a,b);dz=abs(a['elevation_ft']-b['elevation_ft'])*FT
  if gap<1e-7 and dz>.01:conflicts.append({'fields':[a['key'],b['key']],'height_difference_m':round(dz,3)})
  if not a.get('slope_percent',0) and not b.get('slope_percent',0):grades.append((dz/(gap*1000) if gap>0 else (1e9 if dz else 0),a['key'],b['key'],gap))
 measurements['pad_conflicts']=conflicts;measurements['pad_max_error_m']=round(worst[0],3)
 row(1,'FAIL' if worst[0]>.05 else 'PASS sampled',f'{pad_samples} continuous samples; maximum runway surface error {worst[0]:.2f} m at {worst[1]}; {len(conflicts)} incompatible overlapping pad pairs. 60 m shoulders are authored; Drake cave interior excluded (3D floor/roof unverified); PALH gravel datum and 3U2 slope remain targets.')
 badgrades=sorted((g for g in grades if g[0]>.35),reverse=True);measurements['grade_failures']=[dict(grade_percent=None if g>=1e8 else round(g*100,2),fields=[a,b],edge_gap_km=round(d,4)) for g,a,b,d in badgrades]
 tight=[q for q in grades if q[1].startswith('PALH') and q[2].startswith('KMEV') or q[2].startswith('PALH') and q[1].startswith('KMEV')]
 row(2,'FAIL' if badgrades else 'PASS geometric',f'{len(badgrades)} flat-field pairs exceed 35% mean edge-to-edge grade; worst finite '+str(round(max([g*100 for g,a,b,d in grades if g<1e8],default=0),2))+'%. PALH–KMEV pairs: '+', '.join(f'{a}/{b} {g*100:.2f}%' for g,a,b,d in tight)+'. This is an endpoint feasibility test, not a slope-profile certificate.')
 penetrations=[]
 for f in finals():
  u=vec(f['course_true_deg']);v=(u[1],-u[0]);slope=34 if f['precision'] else 20;worstpen=-1e9;where=None
  for i in range(75):
   d=7.408*i/74
   for c in [-1,0,1]:
    off=c*(.15+.05*d);p=[f['threshold'][0]-u[0]*d+v[0]*off,f['threshold'][1]-u[1]*d+v[1]*off];pen=height(*p)-(f['elevation_ft']*FT+d*1000/slope)
    if pen>worstpen:worstpen=pen;where=p
  penetrations.append(dict(airport=f['airport'],title=f['title'],max_terrain_penetration_m=round(worstpen,2),location=where,sampled_half_width='150 m + 0.05 * distance; design screen only'))
 measurements['approach_terrain_screen']=penetrations
 # Obstacle screen against the same fan: towers and a sampled sagging wire.
 obstacle_hits=[]
 for f in finals():
  u=vec(f['course_true_deg']);v=(u[1],-u[0]);slope=34 if f['precision'] else 20
  obstacles=[(t['id'],t['centre'],height(*t['centre'])+t['height_agl_m']) for t in W['hazards']['towers']]
  for wire in W['hazards']['wires']:
   a,b=wire['endpoints']
   for i in range(21):
    t=i/20;p=[a[0]+t*(b[0]-a[0]),a[1]+t*(b[1]-a[1])];z=height(*p)+wire['lowest_agl_m']+dist(a,b)*1000*wire['sag_fraction']*(2*t-1)**2;obstacles.append((wire['id'],p,z))
  for name,p,z in obstacles:
   dx=p[0]-f['threshold'][0];dy=p[1]-f['threshold'][1];along=-(dx*u[0]+dy*u[1]);cross=abs(dx*v[0]+dy*v[1])
   if 0<=along<=7.408 and cross<=.15+.05*along:
    pen=z-(f['elevation_ft']*FT+along*1000/slope)
    if pen>0:obstacle_hits.append(dict(approach=f['airport']+' '+f['title'],obstacle=name,penetration_m=round(pen,2)))
 measurements['approach_obstacle_screen']=obstacle_hits
 row(3,'FAIL',f'{sum(x["max_terrain_penetration_m"]>.05 for x in penetrations)}/12 supplied finals penetrate sampled terrain fan; {len(obstacle_hits)} sampled tower/wire penetrations. All-runway-end surfaces, buildings, trees, non-KSNA misses/holds remain unverified. KSNA data retained unchanged.')
 nav={n['ident']:n['centre'] for k in ['vor','ndb'] for n in S['ifr']['navaids'][k]};nav['MEV']=S['ifr']['navaids']['range']['centre'];airwaybad=[]
 for route in S['ifr']['airways']:
  ids=route['route'].split('-')
  for i,(a,b) in enumerate(zip(ids,ids[1:])):
   if a not in nav or b not in nav:continue
   aa,bb=nav[a],nav[b];dx=bb[0]-aa[0];dy=bb[1]-aa[1];l=dist(aa,bb);v=(dy/l,-dx/l);top=-1e9
   for t in range(21):
    for k in range(-5,6):
     p=[aa[0]+dx*t/20+v[0]*3.704*k/5,aa[1]+dy*t/20+v[1]*3.704*k/5];top=max(top,height(*p)/FT)
   target=route['mea_ft_targets'][min(i,len(route['mea_ft_targets'])-1)];margin=target-top
   if margin<2000:airwaybad.append(dict(route=route['id'],segment=a+'-'+b,terrain_ft=round(top),mea_target_ft=target,clearance_ft=round(margin)))
 measurements['airway_failures']=airwaybad
 row(4,'FAIL' if airwaybad else 'UNVERIFIED',f'{len(airwaybad)} sampled airway segments have less than 2000 ft target clearance within 2 nm; T201 coordinates absent. Full-width grid/obstacle sweep and final computed MEAs belong to Claude.')
 maxland=max(math.hypot(*p) for p in COAST+CAT);row(5,'PASS footprint / UNVERIFIED flight volumes',f'Coast/Catalina vertices maximum radius {maxland:.3f} km <20; active CZ outer radius 19.5 km; ocean bed is continuous and has no wall. BALBO hold outer radius 19.2 km is supplied target, not computed; T201/misses need geometry.')
 spacing=[];shores=[]
 for a,b in itertools.combinations(FIELDS,2):
  if a['field']==b['field']:continue
  waiver={a['field'],b['field']}=={'PANC','PALH'}
  threshold=1.5 if a['field'].startswith('AP') or b['field'].startswith('AP') else .8
  # Minimum pad gap is conservative; also expose centre distances to avoid ambiguity.
  cd=dist(a['centre'],b['centre']);gap=rectgap(a,b)
  if cd<threshold and not waiver:spacing.append(dict(fields=[a['key'],b['key']],centre_distance_km=round(cd,3),pad_gap_km=round(gap,3),required_km=threshold))
 for r in FIELDS:
  if r['field'] not in [a['id'] for a in W['airports'] if a['start_group'] in ['coastal','inland']]:continue
  u=vec(r['heading_true_deg']);l,b=dimensions(r);ends=[[r['centre'][0]+s*l*u[0]/2,r['centre'][1]+s*l*u[1]/2] for s in [-1,1]];dd=min(shore(p,COAST) for p in ends);required=.3 if r['start_group']=='coastal' else 2
  if dd<required:shores.append(dict(field=r['key'],minimum_end_shore_km=round(dd,3),required_km=required))
 measurements['spacing_failures']=spacing;measurements['shore_setback_failures']=shores
 row(6,'FAIL' if spacing or shores else 'PASS sampled',f'{len(spacing)} centre-spacing violations (already fails before pad-edge interpretation); {len(shores)} runway-end shore setback violations. PANC/PALH waiver alone applied. Backcountry and airpark relocation needs approval/revision.')
 climb=[]
 for l in W['lesson_terrain']:
  # Rise rate required at reference groundspeed, target geometry rather than aircraft model result.
  fpm=l['box_floor_rise_m']/FT/(l['box_length_m']/(80*1852/3600)/60);climb.append(dict(lesson=l['id'],required_fpm_target=round(fpm),trainer_reference_fpm_target=300))
 measurements['box_climb_targets']=climb
 row(7,'PASS target / UNVERIFIED flight',', '.join(f'{q["lesson"]}: {q["required_fpm_target"]} ft/min required at 80 kt versus 300 ft/min reference' for q in climb)+'. Need actual loaded trainer/DA trajectory and headwall mesh; no sim claim.')
 boxfails=[]
 for box in W['aerobatic_boxes']:
  c=box['centre'];corners=[[c[0]+x*.5,c[1]+y*.5] for x,y in [(-1,-1),(1,-1),(1,1),(-1,1)]]
  for f in finals():
   d=min(seg(p,f['threshold'],f['outer'])[0] for p in corners+[c])
   if d<.5:boxfails.append(dict(box=box['id'],final=f['airport']+' '+f['title'],sampled_distance_km=round(d,3)))
 measurements['box_final_warnings']=boxfails
 race=next(f for f in W['features'] if f['id']=='RACE');race_min=(100,'')
 for e in race['elements']:
  for f in finals():
   d=seg(e['centre'],f['threshold'],f['outer'])[0]-.024
   if d<race_min[0]:race_min=(d,f['airport']+' '+f['title'])
 measurements['race_known_final_clearance_km']=round(race_min[0],3)
 measurements['race_nearest_final']=race_min[1]
 measurements['peak_actual_ft']=[dict(id=q['id'],target_ft=q['top_ft'],computed_ft=round(height(*q['centre'])/FT,2)) for q in W['height_field']['peaks']]
 measurements['practice_line_terrain_range_m']=[dict(id=line['id'],range_m=round(max(height(a[0]+(b[0]-a[0])*i/12,a[1]+(b[1]-a[1])*i/12) for i in range(13))-min(height(a[0]+(b[0]-a[0])*i/12,a[1]+(b[1]-a[1])*i/12) for i in range(13)),2)) for line in W['practice_lines'] for a,b in [line['path']]]
 zoneclear=[]
 for z in W['combat_zones']:
  if not z['enabled']:continue
  poly=zonepoly(z);best=(100,'')
  for f in finals():
   for i in range(75):
    t=i/74;p=[f['threshold'][0]+t*(f['outer'][0]-f['threshold'][0]),f['threshold'][1]+t*(f['outer'][1]-f['threshold'][1])];d=0 if inside(p,poly) else min(seg(p,a,b)[0] for a,b in zip(poly,poly[1:]+poly[:1]))
    if d<best[0]:best=(d,f['airport']+' '+f['title'])
  for route in S['ifr']['airways']:
   ids=route['route'].split('-')
   for aa,bb in zip(ids,ids[1:]):
    if aa not in nav or bb not in nav:continue
    for i in range(51):
     t=i/50;p=[nav[aa][0]+t*(nav[bb][0]-nav[aa][0]),nav[aa][1]+t*(nav[bb][1]-nav[aa][1])];d=0 if inside(p,poly) else min(seg(p,a,b)[0] for a,b in zip(poly,poly[1:]+poly[:1]))
     if d<best[0]:best=(d,route['id']+' '+aa+'-'+bb)
  zoneclear.append(dict(zone=z['id'],minimum_sampled_distance_km=round(best[0],3),nearest=best[1],passes_known_segments=best[0]>=1.85))
 measurements['zone_clearance_screen']=zoneclear
 row(8,'UNVERIFIED' if all(q['passes_known_segments'] for q in zoneclear) and not boxfails else 'FAIL / UNVERIFIED',f'{len(boxfails)} box/final proximity warnings (<0.5 km screen); known CZ/final/airway boundary distances: '+', '.join(f'{q["zone"]} {q["minimum_sampled_distance_km"]:.3f} km' for q in zoneclear)+'. Missing T201, missed/hold polygons prevent 1 nm certification; supplied box/MEA targets retained.')
 row(9,'PASS generated','No random calls; authored integer seeds; continuous evaluator and binary/raster generated in fixed iteration order. Re-run SHA-256 comparison required before commit.')
 row(10,'PASS design / UNVERIFIED integration','One continuous evaluator drives bake/render/checks. Source engine remains unchanged analytic WorldTerrain; Phase 1B must bind physics and renderer to same evaluator. Towers/wires/cave shells remain separate WorldSolids.')
 measurements['fit_check']=rows
 (ROOT/'island-fit-check.json').write_text(json.dumps(measurements,indent=2)+'\n');return measurements

def zonepoly(z):
 a,b=z['bearings_true_deg'];r0,r1=z['radii_km'];return [[r*math.sin(math.radians(a+(b-a)*i/100)),r*math.cos(math.radians(a+(b-a)*i/100))] for r,seq in [(r1,range(101)),(r0,range(100,-1,-1))] for i in seq]

# SVG chart. All spatial geometry comes from the JSON, headings/lengths are true.
def map_svg():
 parts=['<svg xmlns="http://www.w3.org/2000/svg" width="1800" height="2100" viewBox="0 0 1800 2100">','<defs><pattern id="moa" width="9" height="9" patternUnits="userSpaceOnUse"><path d="M-2 2 L2 -2 M0 9 L9 0 M7 11 L11 7" stroke="#d847a4" stroke-width="1" opacity=".55"/></pattern></defs>','<rect width="1800" height="2100" fill="#f4f0e7"/>']
 scale=37.;cx=900;cy=900
 def xy(p):return cx+p[0]*scale,cy-p[1]*scale
 def text(p,t,size=13,colour='#1a2930',anchor='start'):
  x,y=xy(p);parts.append(f'<text x="{x:.2f}" y="{y:.2f}" font-family="Arial, sans-serif" font-size="{size}" fill="{colour}" text-anchor="{anchor}" >{html.escape(str(t))}</text>')
 def line(a,b,col='#536771',width=1,dash=''):
  x,y=xy(a);xx,yy=xy(b);parts.append(f'<path d="M{x:.2f},{y:.2f} L{xx:.2f},{yy:.2f}" fill="none" stroke="{col}" stroke-width="{width}" stroke-dasharray="{dash}"/>')
 def poly(p,fill,stroke='#546e5f',width=1):
  v=' '.join(f'{x:.2f},{y:.2f}' for x,y in map(xy,p));parts.append(f'<polygon points="{v}" fill="{fill}" stroke="{stroke}" stroke-width="{width}"/>')
 def circle(p,r,fill,stroke='none'):
  x,y=xy(p);parts.append(f'<circle cx="{x:.2f}" cy="{y:.2f}" r="{r}" fill="{fill}" stroke="{stroke}"/>')
 text([-22,22.6],'AERO PLAYGROUND / ISLAND WORLD',30);text([-22,21.7],'PHASE 1A · G1a PENDING Ty · DESIGN TARGETS / SEE FIT FAILURES',18,'#a43a35')
 circle([0,0],20*scale,'#d9e8ed','#829ca8');poly(COAST,'#c2d0ac');poly(CAT,'#c2d0ac')
 for k in range(-20,21,5):
  line([k,-20],[k,20],'#b0c6cd',.5);line([-20,k],[20,k],'#b0c6cd',.5);text([k,-20.7],f'{k} km',11,anchor='middle');text([-21,k],str(k),11)
 for c in W['canyons']:
  for a,b in zip(c['path'],c['path'][1:]):line(a,b,'#a1845e',7);line(a,b,'#508aa4',1.2)
  text(c['path'][0],c['id'],13,'#695337')
 for water in W['water']:poly(ellipse(water['centre'],water['radii_km'],90,40),'#80b6d3','#397b9c',.6)
 for g in W['glaciers']:
  for a,b in zip(g['path'],g['path'][1:]):line(a,b,'#f4fcff',9)
  c=g['ski_zone']['centre'];circle(c,4,'white','#4b7a94');text([c[0]+.15,c[1]+.12],g['id']+' ski '+str(g['ski_zone']['elevation_ft'])+' ft',11)
 for peak in W['height_field']['peaks']:
  x,y=peak['centre'];poly([[x,y+.16],[x-.14,y-.12],[x+.14,y-.12]],'#6b6153');text([x+.15,y+.15],peak['id']+' '+str(peak['top_ft']),11)
 for z in W['combat_zones']:
  poly(zonepoly(z),'url(#moa)','#b52d8d',1.8 if z['enabled'] else .8);a=sum(z['bearings_true_deg'])/2;r=sum(z['radii_km'])/2;c=[r*math.sin(math.radians(a)),r*math.cos(math.radians(a))];text([c[0],c[1]+.15],z['id']+' '+z['name'],14,'#9d2077','middle');text([c[0],c[1]-.35],'500–12,500 ft MSL · '+('HOT' if z['enabled'] else 'OPTIONAL / OFF'),12,'#9d2077','middle')
 for f in finals():line(f['threshold'],f['outer'],'#766f9b',.9,'5 4')
 # The specified BC fixes are exact supplied positions, not derived from runway.
 bc=S['ifr']['approaches'][2]
 for q in bc['fixes']:
  circle(q['centre'],3,'#75658d');text([q['centre'][0]-.15,q['centre'][1]-.25],q['id'],11,'#75658d','end')
 for a in W['airports']:
  c=a['runways'][0]['centre'];group=a['start_group'];circle(c,9,'none','#147b89' if group=='coastal' else '#a56a27' if group=='inland' else '#34434d')
  if group=='inland':poly([[c[0],c[1]+.3],[c[0]+.3,c[1]],[c[0],c[1]-.3],[c[0]-.3,c[1]]],'none','#a56a27',1.5)
  text([c[0]+.28,c[1]+.18],a['id']+' '+str(a['elevation_ft'])+' ft',13)
 for r in FIELDS:
  poly(rect(r),'#3c4651' if not r['field'].startswith('AP') else '#577d37','#243744',.5)
  c=r['centre']
  if r['field'].startswith('AP'):text([c[0]+.18,c[1]-.05],r['field'],12,'#375c29')
  elif r['field'] not in [a['id'] for a in W['airports']]:text([c[0]+.1,c[1]],r['field'],9,'#6a5f3f')
  else:
   l,b=dimensions(r);u=vec(r['heading_true_deg']);text([c[0]+u[0]*(l/2+.1),c[1]+u[1]*(l/2+.1)],r['id'],10)
 for t in W['hazards']['towers']:
  x,y=t['centre'];poly([[x,y+.17],[x-.11,y-.1],[x+.11,y-.1]],'none','#a53243',1.5);text([x+.17,y+.06],t['id']+' '+str(t['height_agl_m'])+'m',10,'#a53243')
 for q in W['hazards']['wires']:
  a,b=q['endpoints'];line(a,b,'#a53243',2);text([(a[0]+b[0])/2,(a[1]+b[1])/2+.12],q['id'],11,'#a53243')
 for l in W['lesson_terrain']:
  for p in ['safe_path','box_path']:
   for a,b in zip(l[p],l[p][1:]):line(a,b,'#ce7739' if p=='safe_path' else '#b63b27',1.5,'3 2')
  circle(l['fork'],4,'#d28c41');text([l['fork'][0]+.1,l['fork'][1]+.25],l['id'],13,'#8e491e')
 for f in W['features']:
  c=f['centre'];circle(c,3,'#385b51');text([c[0]+.12,c[1]-.3],f['id'],11,'#284d42')
  if f['id']=='RACE':
   for a,b in zip(f['elements'],f['elements'][1:]):line(a['centre'],b['centre'],'#806327',1)
   for e in f['elements']:circle(e['centre'],1.5,'#806327')
 for road in W['roads']:
  for a,b in zip(road['path'],road['path'][1:]):line(a,b,'#977b55',.8,'4 2')
 for p in W['practice_lines']:line(*p['path'],'#537f4c',.8,'2 2')
 for s in W['lift_terrain']:text(s['centre'],s['id'],12,'#946127')
 for st in W['starts']:
  if st['mode'] in ['OnFinal','InAeroBox','InCombatZone','InThermal'] and st.get('enabled',True):
   c=st['centre'];line([c[0]-.07,c[1]-.07],[c[0]+.07,c[1]+.07],'#077c95',1.5);line([c[0]-.07,c[1]+.07],[c[0]+.07,c[1]-.07],'#077c95',1.5)
 for b in W['aerobatic_boxes']:
  x,y=b['centre'];poly([[x-.5,y-.5],[x+.5,y-.5],[x+.5,y+.5],[x-.5,y+.5]],'none','#dd7828',2)
  line([x-.18,y],[x+.18,y],'white',3);line([x,y-.18],[x,y+.18],'white',3)
  for dx,dy in [(-.5,-.5),(-.5,.5),(.5,-.5),(.5,.5)]:
   a=[x+dx,y+dy];line(a,[a[0]-.1*(1 if dx>0 else -1),a[1]],'white',2);line(a,[a[0],a[1]-.1*(1 if dy>0 else -1)],'white',2)
  for dx,dy in [(0,.5),(0,-.5),(.5,0),(-.5,0)]:
   a=[x+dx,y+dy];line([a[0]-.06,a[1]],[a[0]+.06,a[1]],'white',2);line(a,[a[0],a[1]-.07],'white',2)
  text([x,y+.75],b['id'],14,'#a14c16','middle');text([x,y-.85],str(b['floor_msl_ft_target'])+'–'+str(b['ceiling_msl_ft_target'])+' ft MSL',11,'#a14c16','middle')
 # True and magnetic north both clockwise from map north, M is 7E.
 for h,n,col in [(0,'TRUE N','#263c47'),(7,'MAG N 7E','#9f4e44')]:
  u=vec(h);a=[18,15];b=[a[0]+u[0]*3,a[1]+u[1]*3];line(a,b,col,2);poly([b,[b[0]-.12,b[1]-.35],[b[0]+.12,b[1]-.35]],col,col);text([16.8,18.5] if h==0 else [19.4,18.5],n,12,col,'end' if h==0 else 'start')
 for i in range(5):poly([[-20+i, -22],[-19+i,-22],[-19+i,-21.8],[-20+i,-21.8]],'#253944' if i%2==0 else 'white')
 text([-20,-22.5],'0                  5 km · field geometry at true length/heading',12)
 # Spacious keyed inventory, so dense main chart uses concise identifiers.
 y=1775;parts.append(f'<line x1="85" y1="{y-30}" x2="1715" y2="{y-30}" stroke="#8a9a91"/>')
 legends=[('LEGEND / READ WITH FIT CHECK',['Teal ring: coastal start; ochre diamond: inland start','Dark ring: non-start airport / PALH float variant','Runway rectangles: true length and heading; text: magnetic numbers','Green AP: airpark; small brown text: Middle Fork strip','Red triangle: tower T1–T6; red line: wire W1–W5','Orange fork L1–L4: lesson safe path; red: box branch','Brown triangle: peak + elevation ft; blue: water; white: glacier','Blue ×: final / box / zone / thermal start; S1–S7: lift terrain','Orange square + white markers: AB box; labels are target MSL','Magenta hatch: combat boundary; CZ4 optional/off','Purple dash: supplied IFR final overlay (not procedure plate)','Green dot + ID: gameplay feature; green dash: S-turn line']),('FEATURE KEY', [f['id']+' '+f['name'] for f in W['features'][:10]]),('FEATURE KEY / AIRPARKS',[f['id']+' '+f['name'] for f in W['features'][10:]]),('REVIEW STATUS',['No fitted-world approval claimed: see rule-by-rule failures.','Thin finals use supplied magnetic course + 7E.','No airway/hold clearance claim without missing IFR geometry.','AB1/AB2 water rafts; AB3 5000 ft bench (target).','CZ1–CZ3 HOT; CZ4 is shown only for Ty’s optional decision.','All elevations/geometry are targets except locked values.','Grid: 5 km; extent: 20 km radius from Frank Church.','Main-island coast is a design polygon, not real geography.','PALH belongs to PANC start; KAVX is not a start.','PNG and SVG generated from the same spec.'])]
 for col,(title,ls) in enumerate(legends):
  xx=85+col*420;parts.append(f'<text x="{xx}" y="{y}" font-family="Arial" font-size="15" font-weight="bold" fill="#233c44">{title}</text>')
  for i,t in enumerate(ls):parts.append(f'<text x="{xx}" y="{y+25+i*20}" font-family="Arial" font-size="11" fill="#263e46">{html.escape(t)}</text>')
 parts.append('</svg>');(ROOT/'island-map.svg').write_text('\n'.join(parts)+'\n')

def report(m):
 rows=m['fit_check'];p=['# Aero Playground island world — Phase 1A design','', '**G1a: PENDING Ty. Documentation and review generator only. This draft has measured fit failures; it is not a passing world or approval to implement Phase 1B.**','', '## Authority and numerical status','', 'Ty’s 2026-10-30 locked v1 decisions, including revised 13 and 20, govern. Cursor Page could not be opened; the complete session restatement is the source. Menu lane is an ancestor of split-surfaces. No root AGENTS.md exists. Current lane rules supersede older ownership notes. Every authored coordinate, dimension, elevation or clearance is a **target** unless explicitly locked by Ty. Values in generated fit-check JSON are **computed design-screen measurements**, with sampling limits below; none is a TERPS result. No runtime, Tests/, menu, IFR or weather code changes are part of this phase.','', '## Frame, scale and regions','', 'Origin is Frank Church summit (0,0). Design x east/y north in km; true north up. Engine is x north/y east in metres: `(simX,simY,simZ)=(1000*y,1000*x,-0.3048*altFt)`. Keep physics headings true; published courses and runway numbering use true minus 7° E. Preserve all supplied IFR values separately under Claude ownership. Main island target 18.3 × 17.5 km; authored coast spans 18.3 × 17.6 km (computed y extent includes southern canyon outlet), with Catalina separate inside the 20 km ocean ring. Coast polygon and Catalina ellipse are explicit in the spec.','', 'North-east is Mokuleia/Dillingham, north-west Talkeetna/Denali/Ruth, west Cook Inlet/Lake Hood/Knik, central high country McCall/Bighorn/Frank Church, south-west Carson/Drake/Chugach, south-east Page/Powell, south OC and offshore Catalina. These are compressed teaching landscapes, not a geographic replica. Coast has a 100 m smooth fade into -30 m sea bed; open ocean continues through the ring. Ring fade is 19.5–20 km with no collision wall.','', '## Height field, landforms and water','', 'The Python evaluator uses smooth compact envelopes, lake/glacier cuts, canyon cuts, lesson forks and runway pad blends in the order in the spec. One evaluator drives elevation render, binary bake and checks. It is a review prototype: ridge envelopes and cuts can compete, and failures are preserved rather than hidden. No established binary terrain loader exists in WorldTerrain (currently analytic), so `island-heightfield.f32` is a review interchange with JSON metadata, not a drop-in engine asset. 401 × 401 samples at 100 m; float32 metres MSL, little endian, south-to-north rows, west-to-east columns, x fastest. Continuous evaluator remains authoritative for features smaller than the grid.','', 'The Invisible Walls rule applies to every operation: fade to zero relief before a footprint ends; continuous weighted blends rather than boolean precedence jumps. No cave or arch roof is represented as a second height field; those become WorldSolids shells and decks in Phase 1B. The review bake shows the upper rock envelope: Drake only applies its exterior 152 m ground pad, while the underground floor remains an independent height-aware deck at 4,700 ft. Contact queries below a roof require explicit shell routing in Phase 1B; they cannot use the upper terrain surface blindly. The 100 m image cannot validate sub-metre continuity or roofs.','', '| Peak | Centre km E,N | Top ft MSL (target) | Envelope radius km (target) |','|---|---|---:|---:|']
 for q in W['height_field']['peaks']:p.append(f'| {q["id"]} | {q["centre"]} | {q["top_ft"]} | {q["radius_km"]} |')
 p+=['','All four authored crest polylines are in the spec: Mokuleia, Sierra, Chugach–Denali and summit. Seven lift terrain sites S1–S7 are retained; ground only is Codex-owned, lift/weather is Claude-owned.','', '| Water | Centre km E,N | Surface ft MSL (target unless locked) | Character |','|---|---|---:|---|']
 for q in W['water']:p.append(f'| {q["id"]} | {q["centre"]} | {q["elevation_ft"]} | '+('locked full-pool elevation' if q['id']=='Lake Powell' else 'landable float lake / open outlet' if q.get('landable') else 'committing bowl' if q.get('landable') is False else 'lake')+' |')
 p+=['','Powell is locked at 3,700 ft; the prototype bed is 50 m below full pool, with dam, arches/spires and marina/ramp at F6. Knik/Ruth tongues carry 1,100/1,300 ft ski pads (locked range 1,000–1,300), glacial troughs, ice lake and bergs. Bighorn retains all six old alpine lake characters: three float-landable open outlets, three committing bowls, including Larch timber and Hidden outlet. Basin/outlet geometry in the review is a target; detailed alpine relief belongs to the later world build.','', '| Canyon | Rim width m | Floor width m | Depth m | Floor ft MSL |','|---|---|---|---|---|']
 for q in W['canyons']:p.append(f'| {q["id"]} | {q["rim_width_m"]} | {q["floor_width_m"]} | {q["depth_m"]} | {q["floor_ft"]} |')
 p+=['','C3 uses the complete five-point supplied path. C1 meets 3U2 and W1; C2 carries the nine retained non-Johnson strips and narrows 900→600→400 m; C4 carries the Knik route and W3. Design turn diameters at 45° bank: 60 kt 194/243 m, 80 kt 345/432 m, 100 kt 539/674 m at sea level/DA 7,500 (supplied targets). Floor widths differ from rim widths: the narrowing requires slow-flight judgement.','', '| Lesson | Fork | Safe route / saddle ft target | Box target |','|---|---|---|---|']
 for q in W['lesson_terrain']:p.append(f'| {q["id"]} | {q["fork"]} | {q["safe_path"]}; {q["saddle_ft"]} | {q["box_length_m"]} m, rise {q["box_floor_rise_m"]} m to headwall {q["headwall_ft"]} ft |')
 p+=['','L1 right SE to Johnson under W1, left NE blocked; L2 right west to Knik/Lake Hood under W3, left SW blocked; L3 right west/SW to OC, left short dry side canyon; L4 right east to Ruth 1,300 ft amphitheatre, left north under Denali wall. Branch dimensions and climb rates are authored targets, not flight-model evidence.','', '## Airports, runway surfaces and pads','', 'All supplied true field elevations and runway lengths are retained. PANC is 10,865 ft. KAVX is not a start. There are eight start airports; PALH is the immediately adjacent float variant of PANC. Every surface has 60 m side/end shoulders, then 200–300 m continuous blends. Pads are flat across; only explicitly sloped strips slope along. 3U2 target 0.5% requires survey confirmation; AP6 is 3%. Legacy sloped backcountry strips preserve their slopes and one-way character. Overlapping pads at different elevations cannot both be flat: conflicts below require redesign. Water lanes retain a flat water plane with submerged bed, rather than a land runway at water height.','', '| Field | Runway magnetic | Centre km E,N | True ° | Length × width ft | Field ft MSL | Surface |','|---|---|---|---:|---|---:|---|']
 for a in W['airports']:
  for r in a['runways']:p.append(f'| {a["id"]} | {r["id"]} | {r["centre"]} | {r["heading_true_deg"]:03} | {r["length_ft"]} × {r["width_ft"]} | {a["elevation_ft"]} | {r["surface"]} |')
 p+=['','PALH water-lane bed is 3 m below the locked 79 ft surface; the bed and water plane are distinct. PALH gravel is provisionally 79 ft because its separate true elevation was not supplied. KMEV 13G/31G centre/dimensions are explicit new targets. 3U2: land 19/depart 1; Drake: runway 17 one-way through hologram, 152 m visible plus ~760 m inside. 2,992 ft = 911.962 m; 152+760 m is rounded. KPGA 075 magnetic tie retains 7/25. AP8 now reads 36/18 for 007 true at 7E (change logged).','', '## Airparks and backcountry','', 'AP1–AP14 each get a 60 ft wide strip, three hangars, five houses, windsock, three seeded parked aircraft; target buildings at least 100 m from centreline and outside approach surfaces. Surface, length and gear are explicit; fictional field elevations below are targets. Willow float pond and Bullfrog marina float access are retained.','', '| ID / airpark | Centre | True ° / magnetic | Surface / length ft | Gear | Elevation ft target |','|---|---|---|---|---|---:|']
 for q in W['airparks']:p.append(f'| {q["id"]} {q["name"]} | {q["centre"]} | {q["heading_true_deg"]:03} / {q["numbers_magnetic"]} | {q["surface"]} / {q["length_ft"]} | {q["gear"]} | {q["elevation_ft"]} |')
 p+=['','Starting positions remain reviewable in the changes ledger. AP2 is proposed farther north and KMEV 150 m east to improve spacing/grade. Four fictional airpark elevation targets are revised; AP9 additionally moves southeast to gain run between low PANC and high Drake. No waiver beyond PANC–PALH is invented. Nine non-Johnson Frank Church strips move to C2. Their legacy positions, dimensions, surface, slope and one-way values are retained in the audit and changes list. This dense provisional allocation does not yet fit; see failures. Johnson Creek becomes 3U2 at real locked dimensions/elevation.','', '| Backcountry strip | New centre | Length × width m | Slope % | One-way true ° |','|---|---|---|---:|---|']
 for q in W['backcountry_strips']:p.append(f'| {q["id"]} | {q["centre"]} | {q["length_m"]} × {q["width_m"]} | {q["slope_percent"]} | {q["one_way_true_deg"]} |')
 p+=['','## Hazards, features and retained gameplay','', 'All six towers and all five wires are charted and deterministic WorldSolids (not height-field spikes). Tower bases read terrain; chart top elevations are recomputed after final terrain. Guy anchors, lights, wire sag and orange marker balls are geometry targets; W minimum AGL is the lowest conductor clearance above the full underlying terrain profile, not merely the midpoint. Legacy crop conductors consolidate into W4 so no sixth uncharted wire appears. Forest has fixed seeds, water/pad/approach exclusions and 42 m maximum trees. Road bridge and Gateway-style arch survive; only the named Golden Gate, Eiffel tower, DC Mall and F5 tunnel are dropped.','', '| Hazard | Geometry | Height |','|---|---|---|']
 for q in W['hazards']['towers']:p.append(f'| {q["id"]} | {q["centre"]}; '+('guyed' if q['guyed'] else 'unguyed')+f' | {q["height_agl_m"]} m AGL |')
 for q in W['hazards']['wires']:p.append(f'| {q["id"]} | {q["endpoints"]} | {q["lowest_agl_m"]} m minimum AGL |')
 p+=['','Drake F1: 1,400 m hill, 320 m relief above 4,700 ft valley, 200 m north cliff, chamber 820 × 420 × 140 m and ≥60 m rock cover. East/left glider slope 70 m high, 260 m run, 70 × 45 m pillow pool; west/right 240 m apron and 240 × 45 m two-level glass labs. No school name. Codex designs shell/interior geometry; Claude owns enrolment/hologram trigger, advanced curriculum and radio logic. The old tunnel-first order is superseded. Mouth/cliff and overhead chamber cover need 3D shell validation; a height map cannot certify them.','', 'Pirate cave F2: Catalina SW/leeward shore, 60 × 26 m mouth pointing 240 true (target), 90 m radius chamber, 10 m skylight, wreck/beach/seeded driftwood. Catalina remains at its locked ellipse/position; Avalon, Casino, moorings/beach on NE shore and 150 m sea arch at isthmus. Carrier target (6.8,-10.5), operating radius 0.3 km; the 18 kt moving deck survives, but route clearance requires full IFR geometry.','', 'OC Fly City retains rooftop race, city grid, through-building hole, sky bridges, fountain plaza, rooftop rings, spinning ring and landable raised disc. New consolidated target city extent 1.02 × 2.55 km; maximum buildings 300 m. RACE retains the exact 12 ordered gates/pylons with explicit per-element transforms in the spec, 1.11 × 0.35 km footprint and 75 m pylons near (7,-0.3); at least 1 km from every final is a target requiring the screen below. STOL/tow at AP3; crop dusting at F11 hay fields. Page retains landable mesa tops, ruts, rock-pile thresholds and playa. Rivers retain seeded gravel bars, clean landing lanes, chalk lines/cones and driftwood.','', '| Gameplay ID | Inventory item | Allocation |','|---|---|---|']
 for q in W['gameplay_inventory']:p.append(f'| {q["id"]} | {q["name"]} | {q["destination"]} |')
 p+=['','Inventory was extracted from current SessionSettings Challenges/Lessons, StartMenu dispatch, all six configs/challenges JSONs, WorldFeatures, Playground, Seaside and regional world sources. Extra retained items beyond the supplied list include six additional lesson variants (climb/level/descend, Vx/Vy and glide views), all practice variants, tow hookup, float ramp/buoys, rooftop disc/rings/sky bridges, playa, plunge pools, river bridge and Gateway arch. Source audit lists all public geometry entry points. No event/scoring code is authored here.','', '## Starts and lesson infrastructure','', 'Eight starts: coastal PHDH/PANC/PATK/KSNA; inland KMYL/3U2/KMEV/KPGA. PALH is a PANC float variant, not a ninth airport start. Every start has OnTheRunway, OnFinal (300 ft above field, 0.8 km before threshold) and InTheAir (1,500 ft above field) candidate positions in the spec. Initial runway points are 80 m in from the threshold. These are Codex geometry proposals; Claude owns runway selection, ApproachSpawn, aircraft-specific trim and terrain-safe spawn validation. Every AB and active CZ has a start; S1–S4 have 1,500 ft AGL thermal start positions. Each field has an explicit straight pad-shoulder section line for S-turns, 30 m outside the runway edge, up to 1.2 km long (shorter where the runway requires); PALH uses a buoy reference. Terrain range is measured below; obstacle and flight checks remain pending.','', '## Aerobatic boxes','', 'Three standard IAC squares, 1,000 × 1,000 m, sides true N/S/E/W; floor 100 m and ceiling 1,100 m above centre ground/water. Judges line 150 m outside airport-facing side. White centre cross arms 40 × 4 m; corner L legs 30 × 4 m; side T markers use 30 × 4 m target legs. Water versions are moored white raft shapes with orange corner buoys and shore judges marker. Exact 100/1,100 m governs; rounded 330/3,610 ft labels in the brief are approximate. Generated values use conversion from metres.','', '| Box | Centre | Density altitude | Centre / floor / ceiling ft MSL targets | Judges side |','|---|---|---|---|---|']
 for b in W['aerobatic_boxes']:p.append(f'| {b["id"]} {b["name"]} | {b["centre"]} | {b["density_altitude"]} | {b["centre_ground_ft"]} / {b["floor_msl_ft_target"]} / {b["ceiling_msl_ft_target"]} | {b["judges_side"]} |')
 p+=['','AB1 supplied proximity targets: ILS 26 final 1.2 km, AP4 final 0.5 km; T201 KAALA–TRADE ≥5,000 ft. AB2 targets: PANC runway 0.9 km, ILS 34 final 1.5 km, PATK NDB final 1.0 km; G1/V1/V5 ≥9,000, adjacent T201 ≥5,000. AB3 targets: KMYL RNAV/LDA final 1.4 km, AP1 0.4 km; A1 11,000, V7 11,500; warm-day DA >8,000 ft. All require final recomputation from complete IFR geometry, especially box-edge versus centre clearance. No additional box at KSNA/KMEV/KPGA.','', '## Combat zones','', 'Charted magenta hatched annular sectors, true bearings from summit, 500–12,500 ft MSL target, HOT by default and switchable COLD. Status is shown on chart/ATIS; Claude owns event:combat/dogfight hook: HOT can engage, COLD never engages. No zone lies in the south Catalina/BC hold/carrier corridor. Required distance ≥1.85 km from every 0–4 nm final, missed approach, hold and airway. Full procedure geometry is incomplete, so preliminary distances cannot certify this requirement. Each active CZ retains six gunnery bullseyes on moored target rafts (five 30 m radii, one 14 m) and a three-drone orbit target. This moves existing targets off the land without adding islands. Claude adapts hit height, drone logic and the six legacy concealment cloud volumes. CZ4 is optional/off, never silently enabled.','', '| Zone | True bearings | Radial band km | Status |','|---|---|---|---|']
 for z in W['combat_zones']:p.append(f'| {z["id"]} {z["name"]} | {z["bearings_true_deg"]} | {z["radii_km"]} | '+('HOT target' if z['enabled'] else 'OPTIONAL / OFF pending Ty')+' |')
 p+=['','MOA naming check: FAA-domain web searches for KAIWI, PAINTED, SUSITNA and KACHEMAK did not yield an authoritative full name-list match. Absence from search results is not uniqueness verification; all names remain explicit fictional placeholders for design review only. Check the [FAA SUA portal](https://sua.faa.gov/) before runtime chart publication.','', '## IFR preservation and ownership','', 'The `ifr` section preserves all supplied navaids/frequencies, routes/courses/MEA targets, twelve approach records, BC fixes/feeders/hold/missed/notes, one range approach, VFR-only fields and weather defaults. It is Claude-owned and does not alter runtime nav. KSNA has only LOC BC RWY 2L on I-SNA 111.75, no glideslope or ILS. Plain CDI reverse sensing; HSI front course 201. Final 021 magnetic/028 true, BALBO/LIDOS/WEDGE and supplied offshore missed/hold constraints are unchanged. Minima are targets; Terps computed results win. Drawn IFR finals are a thin geometric overlay reconstructed from supplied courses/thresholds, not procedure plates. T201 fix coordinates and most missed/hold geometry are not specified, so no fabricated coordinates are assigned. Existing Terps lateral compression and sampling are Claude concerns, not changes in this design.','', '## Fit check — computed design screen','', f'Race course sampled element-to-known-final minimum: **{m["race_known_final_clearance_km"]} km**, nearest {m["race_nearest_final"]}. Includes 24 m lateral gate allowance; full pylon/marker and box/zone hull validation remains to be done.', '', 'Run `python3 docs/pending/tools/generate_island.py --png`. Checks use the continuous prototype height function, not runtime terrain. Final screen: 75 longitudinal × 3 lateral probes over 4 nm, fan half-width 150 m + 5% distance, plus tower and 21-point wire probes. Airway screen: 21 × 11 probes within 2 nm. Pad samples: nine per runway; field distances: padded rectangle separation and centre distance. These screens can find failures; passing samples do not prove full-area clearance. All runtime, solids, forest/buildings, actual climb performance and full IFR validation remain future work.','', '| Design rule | Result | Evidence |','|---|---|---|']
 for r in rows:p.append(f'| {r["rule"]} | {r["status"]} | {r["evidence"]} |')
 for key,title in [('peak_actual_ft','Peak centre elevations after all cuts'),('practice_line_terrain_range_m','Practice line terrain range (not a flatness guarantee)'),('spacing_failures','Centre-spacing failures'),('shore_setback_failures','Runway-end shore failures'),('grade_failures','Grade feasibility failures'),('pad_conflicts','Incompatible pad claims'),('airway_failures','Airway clearance failures'),('approach_terrain_screen','Final terrain screen'),('approach_obstacle_screen','Final obstacle screen'),('box_final_warnings','Box proximity warnings'),('zone_clearance_screen','Zone known-segment screen')]:
  p+=['', '### '+title,'','```json',json.dumps(m[key],indent=2),'```']
 p+=['','## Changes ledger','', 'Every authored relocation/correction is listed below and in spec.changes. All locked elevations, runway lengths and supplied IFR data remain unchanged. Proposed centre/elevation revisions are explicitly listed; KMEV shifts 150 m east, AP2 moves north and AP9 moves southeast. The previously unspecified glider runway has explicit target dimensions. Changes from legacy code are grouped only where the exact per-object transforms are still a later implementation task.','', '| Item | Old | New target | Reason |','|---|---|---|---|']
 for c in S['changes']:p.append('| '+ ' | '.join(str(c[k]).replace('|','/') for k in ['item','old','new','reason'])+' |')
 p+=['','## Open for Ty','']+[f'{i}. {o}' for i,o in enumerate(S['open_for_ty'],1)]
 p+=['','## Artefacts and gate','', '`island-spec.json` is the single authoring source. `tools/generate_island.py` generates SVG/PNG map, elevation PNG, float32 grid + metadata, fit-check JSON and this plan. `island-source-audit.json` records repo inventory/provenance. Rebuilds must be byte-identical for deterministic artefacts. PNG rendering uses macOS AppKit via the included Swift helper; generation of grid/elevation/SVG/report is Python standard library. No engine/deploy test applies because src/ is untouched.','', 'Stop at G1a after local commit and the exact AGENT-LOG pending line. Only Ty changing that line to APPROVED by Ty permits the next phase; this draft explicitly asks review of known failures, not an exemption from them.','']
 (ROOT/'ISLAND-WORLD-PLAN.md').write_text('\n'.join(p))

def raster_map():
 # macOS AppKit's native SVG reader; Swift module cache stays in a writable temp folder.
 helper=Path(__file__).with_name('render_svg.swift')
 with tempfile.TemporaryDirectory(prefix='island-swift-') as cache:
  cmd=['swift','-module-cache-path',cache,str(helper),str(ROOT/'island-map.svg'),str(ROOT/'island-map.png')]
  out=subprocess.run(cmd,stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=45)
  if out.returncode:raise SystemExit('Native SVG raster failed: '+out.stderr.decode()[-1200:])

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--png',action='store_true');args=parser.parse_args();bake();m=measure();map_svg();report(m)
 if args.png:raster_map()
 print(json.dumps({'grid':'401 x 401, 100 m','rules':[{k:r[k] for k in ['rule','status']} for r in m['fit_check']],'output':str(ROOT)},indent=2))
if __name__=='__main__':main()
