#!/usr/bin/env python3
"""Island design/runtime review artefacts. Python 3 standard library only.
Run from any directory. Optional --png uses macOS AppKit through Swift; never installs dependencies.
The binary is a documented review interchange, not an existing engine import format.
"""
import argparse, base64, hashlib, html, itertools, json, math, os, struct, subprocess, tempfile, zlib
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
OUTER=[(i['coast_polygon'],i) for i in W.get('outer_islands',[])]
ISLETS=[(ellipse(i['centre'],i['radii_km'],90,24),i) for i in W.get('islets',[])]
LAND=[COAST,CAT]+[p for p,i in OUTER+ISLETS]
OUTER_BOUNDS=[(poly,i,(min(p[0] for p in poly)-.35,max(p[0] for p in poly)+.35,min(p[1] for p in poly)-.35,max(p[1] for p in poly)+.35)) for poly,i in OUTER+ISLETS]
CAT_BOUNDS=(min(p[0] for p in CAT)-.35,max(p[0] for p in CAT)+.35,min(p[1] for p in CAT)-.35,max(p[1] for p in CAT)+.35)
FIELDS=[]
for a in W['airports']:
 for r in a['runways']:FIELDS.append(dict(r,field=a['id'],key=a['id']+' '+r['id'],elevation_ft=a['elevation_ft'],start_group=a['start_group'],shore_class=r.get('shore_class',a['shore_class'])))
for a in W['airparks']:
 FIELDS.append(dict(a,field=a['id'],key=a['id'],start_group=None))
 if a.get('float_pond'):
  q=a['float_pond'];FIELDS.append(dict(q,id=q.get('numbers_magnetic','35W/17W'),field=a['id'],key=a['id']+' float pond',start_group=None,elevation_ft=q['elevation_ft'],shore_class=a['shore_class']))
for a in W['backcountry_strips']+W.get('landable_beaches',[]):FIELDS.append(dict(a,field=a['id'],key=a['id'],start_group=None))

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
 p=(x,y);protected_volcano=any(i.get('volcano') and dist(p,i['volcano']['centre'])<=i['volcano']['radius_km'] for poly,i in OUTER);dm=shore(p,COAST);dc=shore(p,CAT) if CAT_BOUNDS[0]<=x<=CAT_BOUNDS[1] and CAT_BOUNDS[2]<=y<=CAT_BOUNDS[3] else -100;d=max(dm,dc);outer_owner=None
 for poly,i,bounds in OUTER_BOUNDS:
  if not(bounds[0]<=x<=bounds[1] and bounds[2]<=y<=bounds[3]):continue
  dd=shore(p,poly)
  if dd>d:d=dd;outer_owner=i
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
  rx,ry=lake['radii_km'];cx,cy=lake['centre'];u=vec(lake.get('long_axis_true_deg',90));dx=x-cx;dy=y-cy;rr=math.hypot((dx*u[0]+dy*u[1])/rx,(dx*u[1]-dy*u[0])/ry);a=1-smooth((rr-.85)/.45)
  h=h*(1-a)+min(h,lake['elevation_ft']*FT-lake['depth_m'])*a
 for g in W['glaciers']:
  dd,t=pathnear(p,g['path']);a=(1-smooth((dd-g['half_width_m']/1000)/.3))*smooth(t/.12)
  z=(g['head_ft']*(1-t)+g['ski_zone']['elevation_ft']*t)*FT;h=h*(1-a)+z*a
  q=g['ski_zone'];a=1-smooth((dist(p,q['centre'])-.15)/.25);h=h*(1-a)+q['elevation_ft']*FT*a
 for c in W['canyons']:
  lengths=[dist(a,b) for a,b in zip(c['path'],c['path'][1:])];total=sum(lengths);run=0
  for a0,b0,ll in zip(c['path'],c['path'][1:],lengths):
   dd,t0=seg(p,a0,b0);t=(run+t0*ll)/total;run+=ll;rw=c['rim_width_m'][0]*(1-t)+c['rim_width_m'][-1]*t;fw=c['floor_width_m'][0]*(1-t)+c['floor_width_m'][-1]*t;z=(c['floor_ft'][0]*(1-t)+c['floor_ft'][-1]*t)*FT
   alpha=1-smooth((dd*1000-fw/2)/max(1,(rw-fw)/2));h=h*(1-alpha)+min(h,z)*alpha
 for lesson in W['lesson_terrain']:
  dd,t=pathnear(p,lesson['safe_path']);a=1-smooth((dd-.12)/.25);z=lesson['saddle_ft']*FT;h=h*(1-a)+min(h,z)*a
  dd,t=pathnear(p,lesson['box_path']);a=1-smooth((dd-.08)/.12);z=lesson['saddle_ft']*FT+lesson['box_floor_rise_m']*t;h=h*(1-a)+z*a
 # McCall box bench. Explicitly level only within 1 km square; blend outside.
 b=W['aerobatic_boxes'][2];a=1-smooth((max(abs(x-b['centre'][0]),abs(y-b['centre'][1]))-.5)/.2);h=h*(1-a)+5000*FT*a
 # Outer islands are deliberately low coastal terrain, with a radial cratered cone at Sitka.
 if outer_owner:
  # Max of independently faded island contributions is continuous even where
  # dissimilar island/sea-stack footprints overlap; no nearest-owner height switch.
  hs=[]
  for poly,owner,bounds in OUTER_BOUNDS:
   if not(bounds[0]<=x<=bounds[1] and bounds[2]<=y<=bounds[3]):continue
   sd=shore(p,poly)
   if sd<=0:continue
   zh=owner.get('base_ft',owner.get('elevation_ft',35))*FT
   relief=owner.get('relief')
   if relief:zh+=(relief['top_ft']*FT-zh)*(1-smooth(dist(p,owner['centre'])/relief['radius_km']))
   q=owner.get('volcano')
   if q:
    r=dist(p,q['centre']);base=owner['base_ft']*FT;rim=q['rim_ft']*FT;rr=q['rim_radius_km']
    if r<=rr:zh=q['crater_floor_ft']*FT+(rim-q['crater_floor_ft']*FT)*smooth(r/rr)
    elif r<q['radius_km']:zh=base+(rim-base)*(q['radius_km']-r)/(q['radius_km']-rr)
   hs.append(-30+(zh+30)*smooth(sd/W['edge']['land_shore_fade_km']))
  h=max(hs,default=-30)
 terrace=W['height_field'].get('land_moa_terrace')
 if terrace:
  dd=-shore(p,terrace['boundary']);a=1-smooth(max(0,dd)/(terrace['blend_m']/1000));h=h*(1-a)+terrace['elevation_ft']*FT*a
 # Two gentle ridges define the land MOA canyon; upper terrace floor remains level.
 for z in W['combat_zones']:
  for f in z.get('combat_features',[]):
   if f['type']=='paired ridges and dry canyon':
    cx,cy=f['centre'];a=1-smooth(max(0,abs(y-cy)-f['length_m']/2000)/.025)
    cut=(1-smooth((abs(x-cx)-f['canyon_floor_width_m']/2000)/.02))*(1-smooth((abs(y-cy)-.12)/.03));h=h*(1-cut)+min(h,f['canyon_floor_ft']*FT)*cut
    for sign in [-1,1]:
     rr=abs(x-cx-sign*(f['canyon_width_m']/2000+f['ridge_half_width_m']/1000))/(f['ridge_half_width_m']/1000)
     h+=(f['rim_ft']*FT-h)*(1-smooth(rr))*a
 for q in W['height_field'].get('hazard_benches',[]):
  a=1-smooth((dist(p,q['centre'])-q['core_radius_m']/1000)/(q['blend_m']/1000));h=h*(1-a)+q['elevation_ft']*FT*a
 for z in W['combat_zones']:
  for f in z.get('combat_features',[]):
   if f['type']=='rock spire pair':
    for c in f['stack_centres']:
     a=1-smooth(dist(p,c)/(f['radius_m']/1000));h=max(h,h+(f['top_msl_ft']*FT-h)*a)
 # Reserve full design clearance corridors, with continuous outer fades.
 for e in CLEARANCE_ENVELOPES:
  if protected_volcano:continue
  if e['kind']=='airway':
   dd,_=seg(p,e['a'],e['b']);alpha=1-smooth((dd-e['width'])/e['blend']);cap=e['cap_m']
  else:
   u=e['u'];v=(u[1],-u[0]);dx=x-e['a'][0];dy=y-e['a'][1];ad=-(dx*u[0]+dy*u[1]);cross=abs(dx*v[0]+dy*v[1]);width=.15+.05*max(0,ad)
   dd=max(-ad-.25,ad-e['length']-.25,cross-width-.25,0);alpha=1-smooth(dd/e['blend']);cap=e['elevation_m']+max(0,ad)*1000/e['ratio']-30
  if alpha>0:h=h*(1-alpha)+min(h,cap)*alpha
 # Restore finite box-canyon floors and headwalls; real clearance conflicts are reported.
 for lesson in W['lesson_terrain']:
  dd,t=pathnear(p,lesson['box_path']);a=1-smooth((dd-lesson['floor_half_width_m']/1000)/(lesson['outer_blend_m']/1000));z=lesson['saddle_ft']*FT+lesson['box_floor_rise_m']*t;h=h*(1-a)+z*a
  q=lesson['headwall_geometry'];a=1-smooth((dist(p,q['centre'])-q['core_radius_m']/1000)/(q['blend_m']/1000));h=max(h,h+(q['top_ft']*FT-h)*a)
 # Preserve all authored peak heights after erosional cuts, with continuous compact influence.
 for q in W['height_field']['peaks']:
  rr=dist(p,q['centre']);a=1-smooth((rr-.12)/.28);h=h*(1-a)+q['top_ft']*FT*a
 # McCall box bench is a detail reservation, reasserted after broad terrain cuts.
 b=W['aerobatic_boxes'][2];a=1-smooth((max(abs(x-b['centre'][0]),abs(y-b['centre'][1]))-.5)/.15);h=h*(1-a)+b['centre_ground_ft']*FT*a
 # Exact Drake roof/crown envelope; the intentional northcliff has a height-aware opening.
 drake=next(f for f in W['features'] if f['id']=='F1');ds=drake.get('shell_geometry')
 if ds:
  rr=dict(centre=ds['chamber_centre'],heading_true_deg=180);ad,cr=local(p,rr);dd0=math.hypot(max(0,abs(ad)-.410),max(0,abs(cr)-.210));a=1-smooth(dd0/(ds['roof_blend_m']/1000));h=max(h,h+(ds['minimum_roof_msl_m']-h)*a)
  a=1-smooth((dist(p,ds['crown_centre'])-.25)/.4);h=max(h,h+(ds['crown_msl_ft']*FT-h)*a)
 # Upper shell roof; the internal cave floor stays a separate height-aware deck.
 cave=next(f for f in W['features'] if f['id']=='F2');ci=cave['interior'];dd=dist(p,ci['chamber_centre']);a=1-smooth((dd-cave['chamber_radius_m']/1000)/.08);h=max(h,h+(ci['roof_top_msl_m']-h)*a)
 # Physical high-lake beds and narrow closed banks after broad clearancecuts.
 # Their full waterplanes and footprints are separately screened, so a lowcut bed
 # can never hide a high water-surface obstruction.
 for lake in W['water']:
  if not lake.get('basin_detail'):continue
  u=vec(lake.get('long_axis_true_deg',90));dx=x-lake['centre'][0];dy=y-lake['centre'][1];rx,ry=lake['radii_km'];rr=math.hypot((dx*u[0]+dy*u[1])/rx,(dx*u[1]-dy*u[0])/ry);water=lake['elevation_ft']*FT
  if rr<=1:h=water-lake['depth_m']*(1-smooth(rr))
  else:
   # Minaxis conversion conservatively limits the normal bank width to40m.
   dd=(rr-1)*min(rx,ry)*1000;cfg=lake['basin_detail'];width=cfg['bank_width_m'];core=cfg['ridge_offset_m']
   if dd<core:h=water+cfg['ridge_height_m']*smooth(dd/core)
   elif dd<width:
    a=1-smooth((dd-core)/(width-core));h=h*(1-a)+(water+cfg['ridge_height_m'])*a
 # Landable Page mesa detailplanes; continuous blend before authoritative fieldpads.
 for q in W.get('mesa_pads',[]):
  ad,cr=local(p,q);da=max(0,abs(ad)-q['length_m']/2000);db=max(0,abs(cr)-q['width_m']/2000);dd=math.hypot(da,db);h=h*(smooth(dd/(q['blend_m']/1000)))+q['elevation_ft']*FT*(1-smooth(dd/(q['blend_m']/1000)))
 # Simultaneous overlapping claims use nearest rectangle; conflicts reported separately.
 claims=[]
 for original in FIELDS:
  r=original
  if r['field']=='DRAKE':
   # Cave interior is not an open trench. Only the visible 152 m is a terrain pad.
   full,_=dimensions(r);u=vec(r['heading_true_deg']);exterior=drake['exterior_strip_m']/1000;offset=-full/2+exterior/2
   # Southshoulder belongs to interior deck, never cut the upperroof to floorheight.
   if ds and y<ds['mouth'][1]:continue
   r=dict(r,length_m=drake['exterior_strip_m'],centre=[r['centre'][0]+offset*u[0],r['centre'][1]+offset*u[1]])
  along,cross=local(p,r);l,b=dimensions(r);pad=r['pad'];da=max(0,abs(along)-l/2-pad.get('end_shoulder_m',60)/1000);db=max(0,abs(cross)-b/2-pad['shoulder_m']/1000);dd=math.hypot(da,db);blend=pad['blend_m']/1000
  if dd<blend:
   z=r['elevation_ft']*FT+max(-l/2,min(l/2,along))*1000*r.get('slope_percent',0)/100-(W['height_field']['water_lane_bed_offset_m'] if r['surface']=='water' else 0)
   claims.append((dd,1-smooth(dd/blend),z,r['key']))
 if claims:
  dd,a,z,_=min(claims)
  if dd>1e-10:
   weights=[aa/(d0*d0) for d0,aa,z0,key in claims];z=sum(ww*claim[2] for ww,claim in zip(weights,claims))/sum(weights);a=max(q[1] for q in claims)
  h=h*(1-a)+z*a
 # Clip only exterior pad blends to exact final planes; full authored pad planes
 # remain authoritative and any incompatible pad itself remains a reported failure.
 if claims and dd>1e-10:
  release=min(smooth((dist(p,q['centre'])-.12)/.28) for q in W['height_field']['peaks']);clipweight=max(q[1] for q in claims)*release
  for e in CLEARANCE_ENVELOPES:
   if protected_volcano:continue
   if e['kind']=='airway':
    d0,_=seg(p,e['a'],e['b']);alpha=(1-smooth((d0-e['width'])/e['blend']))*smooth(dd/.03)*clipweight;h=h*(1-alpha)+min(h,e['cap_m'])*alpha;continue
   u=e['u'];v=(u[1],-u[0]);dx=x-e['a'][0];dy=y-e['a'][1];ad=-(dx*u[0]+dy*u[1]);cr=abs(dx*v[0]+dy*v[1]);ww=.15+.05*max(0,ad)
   d0=max(-ad-.25,ad-e['length']-.25,cr-ww-.25,0);alpha=1-smooth(d0/e['blend']);cap=e['elevation_m']+ad*1000/e['ratio']
   alpha*=smooth(dd/.03)*clipweight;h=h*(1-alpha)+min(h,cap)*alpha
 # Airway budget is a continuous global layer, not conditional on a pad claim.
 # Protect exact fieldplanes and authored peakcores; fade at actual2nm boundary.
 release=min(smooth((dist(p,q['centre'])-.12)/.28) for q in W['height_field']['peaks']);padrelease=smooth(dd/.03) if claims else 1
 for e in CLEARANCE_ENVELOPES:
  if e['kind']!='airway':continue
  d0,_=seg(p,e['a'],e['b']);alpha=(1-smooth((d0-3.704)/e['blend']))*padrelease*release;h=h*(1-alpha)+min(h,e['cap_m'])*alpha
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
 png(ROOT/'island-elevation-base.png',n,n,pixels)
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

def runway_finals():
 out=[]
 for r in FIELDS:
  l,b=dimensions(r);u=vec(r['heading_true_deg'])
  for sign in [-1,1]:
   if r['field']=='DRAKE' and sign==1:continue  # Ty: single portal, no reverse final.
   c=(r['heading_true_deg']+(180 if sign==1 else 0))%360;th=[r['centre'][0]+sign*u[0]*l/2,r['centre'][1]+sign*u[1]*l/2]
   z=r['elevation_ft']+sign*l*500*r.get('slope_percent',0)/100/FT;v=vec(c)
   out.append(dict(airport=r['field'],title=r['key']+(' end A' if sign==-1 else ' end B'),threshold=th,outer=[th[0]-v[0]*7.408,th[1]-v[1]*7.408],course_true_deg=c,elevation_ft=z,precision=False,source='all-runway-end 20:1 design protection'))
 return out

def finalpoly(f):
 u=vec(f['course_true_deg']);v=(u[1],-u[0]);a=f['threshold'];b=f['outer'];w=.15+.05*7.408
 return [[a[0]-v[0]*.15,a[1]-v[1]*.15],[a[0]+v[0]*.15,a[1]+v[1]*.15],[b[0]+v[0]*w,b[1]+v[1]*w],[b[0]-v[0]*w,b[1]-v[1]*w]]

# World-shaping reservations derive from unchanged supplied IFR and runway geometry.
def airway_segments():
 nav={n['ident']:n['centre'] for k in ['vor','ndb'] for n in S['ifr']['navaids'][k]};nav['MEV']=S['ifr']['navaids']['range']['centre'];out=[]
 for r in S['ifr']['airways']:
  ids=r['route'].split('-')
  for i,(a,b) in enumerate(zip(ids,ids[1:])):
   if a in nav and b in nav:out.append(dict(id=r['id']+' '+a+'-'+b,a=nav[a],b=nav[b],mea_ft=r['mea_ft_targets'][min(i,len(r['mea_ft_targets'])-1)]))
 return out
CLEARANCE_ENVELOPES=[]
if W['height_field'].get('clearance_envelopes'):
 for r in airway_segments():CLEARANCE_ENVELOPES.append(dict(kind='airway',a=r['a'],b=r['b'],width=3.854,blend=.3,cap_m=(r['mea_ft']-2025)*FT))
 route=W['perimeter_traffic_reservation']
 for i,(a,b) in enumerate(zip(route['path'],route['path'][1:])):CLEARANCE_ENVELOPES.append(dict(kind='airway',a=a,b=b,width=3.854,blend=.3,cap_m=(route['mea_ft_targets'][i]-2025)*FT))
 for f in finals()+runway_finals():CLEARANCE_ENVELOPES.append(dict(kind='final',a=f['threshold'],u=vec(f['course_true_deg']),length=7.408,blend=.2,elevation_m=f['elevation_ft']*FT,ratio=34 if f['precision'] else 20))

def measure():
 from fit_revision import measure_revision
 import sys
 return measure_revision(sys.modules[__name__])

def zonepoly(z):
 if 'boundary' in z:return z['boundary']
 a,b=z['bearings_true_deg'];r0,r1=z['radii_km'];return [[r*math.sin(math.radians(a+(b-a)*i/100)),r*math.cos(math.radians(a+(b-a)*i/100))] for r,seq in [(r1,range(101)),(r0,range(100,-1,-1))] for i in seq]

# SVG chart. All spatial geometry comes from the JSON, headings/lengths are true.
def map_svg():
 fit=json.loads((ROOT/'island-fit-check.json').read_text())
 parts=['<svg xmlns="http://www.w3.org/2000/svg" width="1800" height="2100" viewBox="0 0 1800 2100">','<defs><pattern id="moa" width="9" height="9" patternUnits="userSpaceOnUse"><path d="M-2 2 L2 -2 M0 9 L9 0 M7 11 L11 7" stroke="#d847a4" stroke-width="1" opacity=".55"/></pattern></defs>','<rect width="1800" height="2100" fill="#f4f0e7"/>']
 scale=37.;cx=900;cy=900;label_boxes=[];arrange_labels=True
 def xy(p):return cx+p[0]*scale,cy-p[1]*scale
 def text(p,t,size=13,colour='#1a2930',anchor='start'):
  x,y=xy(p);ox,oy=x,y
  if arrange_labels and size<18 and abs(p[0])<20.5 and abs(p[1])<20.5:
   tw=len(str(t))*size*.49;th=size*1.2
   for dx,dy in [(0,0),(0,-14),(0,14),(14,-24),(14,24),(-14,-34),(-14,34),(35,-44),(35,44),(-35,-54),(-35,54),(60,-66),(60,66)]:
    xx,yy=ox+dx,oy+dy;left=xx-tw/2 if anchor=='middle' else xx-tw if anchor=='end' else xx;box=(left-2,yy-th, left+tw+2,yy+3)
    if not any(box[0]<b[2] and box[2]>b[0] and box[1]<b[3] and box[3]>b[1] for b in label_boxes):x,y=xx,yy;label_boxes.append(box);break
   if abs(x-ox)+abs(y-oy)>12:parts.append(f'<path d="M{ox:.2f},{oy:.2f} L{x:.2f},{y:.2f}" stroke="{colour}" stroke-width=".5" opacity=".6"/>')
  # AppKit's SVG reader ignores paint-order; draw a separate halo first so
  # native PNGs retain the same dark, legible text as the vector chart.
  attrs=f'x="{x:.2f}" y="{y:.2f}" font-family="Arial, sans-serif" font-size="{size}" text-anchor="{anchor}"'
  label=html.escape(str(t))
  parts.append(f'<text {attrs} fill="none" stroke="#f4f0e7" stroke-width="2" stroke-linejoin="round">{label}</text><text {attrs} fill="{colour}">{label}</text>')

 def line(a,b,col='#536771',width=1,dash=''):
  x,y=xy(a);xx,yy=xy(b);parts.append(f'<path d="M{x:.2f},{y:.2f} L{xx:.2f},{yy:.2f}" fill="none" stroke="{col}" stroke-width="{width}" stroke-dasharray="{dash}"/>')
 def poly(p,fill,stroke='#546e5f',width=1):
  v=' '.join(f'{x:.2f},{y:.2f}' for x,y in map(xy,p));parts.append(f'<polygon points="{v}" fill="{fill}" stroke="{stroke}" stroke-width="{width}"/>')
 def circle(p,r,fill,stroke='none'):
  x,y=xy(p);parts.append(f'<circle cx="{x:.2f}" cy="{y:.2f}" r="{r}" fill="{fill}" stroke="{stroke}"/>')
 text([-22,22.6],'AERO PLAYGROUND / ISLAND WORLD',30);text([-22,21.7],'PHASE 1B · G1 PENDING Ty · APPROVED LAYOUT / LOCAL FIT EVIDENCE',18,'#a43a35')
 circle([0,0],20*scale,'#d9e8ed','#829ca8')
 for coast in LAND:poly(coast,'#c2d0ac')
 for i in W.get('outer_islands',[]):
  text(i['coast_polygon'][0],i['id']+' '+i['name'],13)
  if i.get('volcano'):
   q=i['volcano'];poly(ellipse(q['centre'],[q['radius_km']]*2,0,48),'#a4947c');poly(ellipse(q['centre'],[q['rim_radius_km']]*2,0,32),'#76665c');text(q['centre'],'crater '+str(q['rim_ft'])+' ft',10)
 for site in W.get('outer_navaid_sites',[]):circle(site['centre'],3,'none','#306a91');text([site['centre'][0]+.1,site['centre'][1]+.2],site['id'],9,'#306a91')
 route=W['perimeter_traffic_reservation']['path']
 for a,b in zip(route,route[1:]):line(a,b,'#378a9b',1.2,'8 4')
 for k in range(-20,21,5):
  line([k,-20],[k,20],'#b0c6cd',.5);line([-20,k],[20,k],'#b0c6cd',.5);text([k,-20.7],f'{k} km',11,anchor='middle');text([-21,k],str(k),11)
 for c in W['canyons']:
  for a,b in zip(c['path'],c['path'][1:]):line(a,b,'#a1845e',7);line(a,b,'#508aa4',1.2)
  text(c['path'][0],c['id'],13,'#695337')
 for water in W['water']:poly(ellipse(water['centre'],water['radii_km'],water.get('long_axis_true_deg',90),40),'#80b6d3','#397b9c',.6)
 for g in W['glaciers']:
  for a,b in zip(g['path'],g['path'][1:]):line(a,b,'#f4fcff',9)
  c=g['ski_zone']['centre'];circle(c,4,'white','#4b7a94');text([c[0]+.15,c[1]+.12],g['id']+' ski '+str(g['ski_zone']['elevation_ft'])+' ft',11)
 for peak in W['height_field']['peaks']:
  x,y=peak['centre'];poly([[x,y+.16],[x-.14,y-.12],[x+.14,y-.12]],'#6b6153');text([x+.15,y+.15],peak['id']+' '+str(peak['top_ft']),11)
 for z in W['combat_zones']:
  geo=fit['zone_geometry'][z['id']];parts0=geo['active_polygons']
  parts.append('<clipPath id="clip-'+z['id']+'">')
  for p in parts0:poly(p,'white','none',0)
  parts.append('</clipPath>')
  for p in parts0:poly(p,'url(#moa)','none',0)
  poly(zonepoly(z),'none','#b52d8d',1.4)
  parts.append('<g clip-path="url(#clip-'+z['id']+')">')
  for o in geo['raised_floor_overlays']:poly(o['boundary'],'#cecee8','#8078ad',.7)
  parts.append('</g>')
  if 'boundary' in z:c=[sum(p[0] for p in z['boundary'])/len(z['boundary']),sum(p[1] for p in z['boundary'])/len(z['boundary'])]
  else:
   a=sum(z['bearings_true_deg'])/2;r=sum(z['radii_km'])/2;c=[r*math.sin(math.radians(a)),r*math.cos(math.radians(a))]
  text([c[0],c[1]+.3],z['id']+' '+z['name'],12,'#9d2077','middle');text([c[0],c[1]-.35],str(z['floor_msl_ft_target'])+'–'+str(z['ceiling_msl_ft_target'])+' ft MSL '+z['default_status'],10,'#9d2077','middle')
  for o in z.get('airport_overlays',[]):
   a=next(a for a in W['airports'] if a['id']==o['airport']);q=a['runways'][0]['centre'];text([q[0]+.3,q[1]-.6],'MOA 5000–12500 / traffic ≤3000',9,'#544879')
  for f in z.get('combat_features',[]):
   q=f['centre'];typ=f['type'];code='B' if typ=='bridge' else 'A' if typ=='rock arch' else 'S' if typ=='rock spire pair' else 'C'
   if typ=='bridge':
    half=f['span_m']/2000;line([q[0]-half,q[1]],[q[0]+half,q[1]],'#633f34',3)
   elif typ=='rock arch':circle(q,4,'none','#633f34')
   else:circle(q,3,'#66875e')
   text([q[0]+.1,q[1]-.14],z['id']+code,8,'#633f34')
  for t in z['gunnery_targets']:circle(t['centre'],2,'#d34936')
 for f in finals()+[f for f in runway_finals() if f['airport'] in ['SITX','TERN','NHVN'] or f['airport'].startswith('B')]:line(f['threshold'],f['outer'],'#766f9b',.9,'5 4')
 # The specified BC fixes are exact supplied positions, not derived from runway.
 bc=S['ifr']['approaches'][2]
 for q in bc['fixes']:
  circle(q['centre'],3,'#75658d');text([q['centre'][0]-.15,q['centre'][1]-.25],q['id'],11,'#75658d','end')
 for a in W['airports']:
  c=a['runways'][0]['centre'];group=a['start_group'];circle(c,9,'none','#147b89' if group=='coastal' else '#a56a27' if group=='inland' else '#34434d')
  if group=='inland':poly([[c[0],c[1]+.3],[c[0]+.3,c[1]],[c[0],c[1]-.3],[c[0]-.3,c[1]]],'none','#a56a27',1.5)
  text([c[0]+.28,c[1]+.18],a['id']+' '+str(a['elevation_ft'])+' ft',13)
 for r in FIELDS:
  poly(rect(r),'#d9c38a' if r['surface']=='hard sand' else '#80b6d3' if r['surface']=='water' else '#3c4651' if not r['field'].startswith('AP') else '#577d37','#243744',.5)
  c=r['centre']
  if r['field'].startswith('AP'):text([c[0]+.18,c[1]-.05],r['field'],12,'#375c29')
  elif r['field'] not in [a['id'] for a in W['airports']]:text([c[0]+.1,c[1]],r['field'],9,'#6a5f3f')
  else:
   l,b=dimensions(r);u=vec(r['heading_true_deg']);text([c[0]+u[0]*(l/2+.1),c[1]+u[1]*(l/2+.1)],r['id'],10)
 for q in W.get('mesa_pads',[]):
  poly(rect(q),'#c2a883','#866749',1);text(q['centre'],q['id']+' '+str(q['elevation_ft'])+' ft',9,'#765838')
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
  for raft in b.get('rafts',[]):circle(raft['centre'],2,'white','#e5842b')
  if b.get('judge_raft'):circle(b['judge_raft']['centre'],2,'white','#e5842b')
  if b.get('judges_line',{}).get('shore_extension_path'):line(*b['judges_line']['shore_extension_path'],'#dc8d43',.8,'3 3')
  for buoy in b.get('corner_buoys',[]):circle(buoy['centre'],3,'#e5842b')
  if b.get('shore_reference_marker'):circle(b['shore_reference_marker']['centre'],3,'white','#e5842b')
  text([x,y+.75],b['id'],14,'#a14c16','middle');text([x,y-.85],str(round(b['floor_msl_ft_target']))+'–'+str(round(b['ceiling_msl_ft_target']))+' ft MSL',11,'#a14c16','middle')
 # True and magnetic north both clockwise from map north, M is 7E.
 for h,n,col in [(0,'TRUE N','#263c47'),(7,'MAG N 7E','#9f4e44')]:
  u=vec(h);a=[18,15];b=[a[0]+u[0]*3,a[1]+u[1]*3];line(a,b,col,2);poly([b,[b[0]-.12,b[1]-.35],[b[0]+.12,b[1]-.35]],col,col);text([16.8,18.5] if h==0 else [19.4,18.5],n,12,col,'end' if h==0 else 'start')
 for i in range(5):poly([[-20+i, -22],[-19+i,-22],[-19+i,-21.8],[-20+i,-21.8]],'#253944' if i%2==0 else 'white')
 text([-20,-22.5],'0                  5 km · field geometry at true length/heading',12)
 # Waterfront and small land MOA insets make the compressed details reviewable.
 arrange_labels=False
 main_transform=(scale,cx,cy)
 def panel(x,y,width,height,title):
  parts.append(f'<rect x="{x}" y="{y}" width="{width}" height="{height}" rx="8" fill="#faf7ee" stroke="#7c9299"/>')
  parts.append(f'<text x="{x+12}" y="{y+22}" font-family="Arial" font-size="13" fill="#233c44">{html.escape(title)}</text>')
 panel(95,1330,350,345,'SITX / WATERFRONT FILL + COVE')
 scale=130;cx=270-(-15)*scale;cy=1510+1.1*scale
 # Exact local coast clipped toinsetwindow; water200m each side of runwaymidsection.
 parts.append('<clipPath id="sit-inset"><rect x="100" y="1360" width="340" height="285"/></clipPath><g clip-path="url(#sit-inset)"><rect x="100" y="1360" width="340" height="285" fill="#d9e8ed"/>')
 i=next(i for i in W['outer_islands'] if i['id']=='OI1');poly(i['coast_polygon'],'#c2d0ac')
 r=next(a for a in W['airports'] if a['id']=='SITX')['runways'][0];poly(rect(r,True),'#d8cdb7');poly(rect(r),'#3c4651')
 text([-14.82,1.65],'35/17 · 1100 × 30 m',10);text([-14.82,1.45],'30 ft asphalt fill',10);text([-14.72,1.05],'sheltered cove',10,'#397b9c');text([-15.9,.3],'ends ≥300 m from shore',9)
 parts.append('</g>')
 scale,cx,cy=main_transform
 panel(1315,1330,390,345,'CZ5 / AP TERN MESA / HOT')
 z=next(z for z in W['combat_zones'] if z['id']=='CZ5');c=[sum(p[0] for p in z['boundary'])/4,sum(p[1] for p in z['boundary'])/4]
 scale=530;cx=1510-c[0]*scale;cy=1495+c[1]*scale
 poly(z['boundary'],'url(#moa)','#b52d8d',2)
 for f in z['combat_features']:
  q=f['centre'];typ=f['type']
  if typ=='bridge':
   h=f['span_m']/2000;line([q[0]-h,q[1]],[q[0]+h,q[1]],'#633f34',4);text([q[0]-.17,q[1]-.06],'bridge: 100 m below deck',9)
  elif typ=='rock arch':circle(q,8,'none','#633f34');text([q[0]-.16,q[1]+.06],'arch: 60 × 55 m opening',9)
  else:
   for dx in [-.085,.085]:line([q[0]+dx,q[1]-.15],[q[0]+dx,q[1]+.15],'#9c8764',8)
 for t in z['gunnery_targets']:circle(t['centre'],4,'#d34936')
 text([c[0]-.31,c[1]-.29],'200–2500 ft MSL · terrain 60–188 ft',10,'#9d2077');text([c[0]-.31,c[1]-.33],'flight tubes 220–300 ft · 2.05 km guard',10,'#9d2077')
 scale,cx,cy=main_transform
 # Spacious keyed inventory, so dense main chart uses concise identifiers.
 y=1775;parts.append(f'<line x1="85" y1="{y-30}" x2="1715" y2="{y-30}" stroke="#8a9a91"/>')
 legends=[('LEGEND / READ WITH FIT CHECK',['Teal ring: coastal start; ochre diamond: inland start','Dark ring: non-start airport / PALH float variant','Runway rectangles: true length and heading; text: magnetic numbers','Green AP: airpark; small brown text: Middle Fork strip','Red triangle: tower T1–T6; red line: wire W1–W5','Orange fork L1–L4: lesson safe path; red: box branch','Brown triangle: peak + elevation ft; blue: water; white: glacier','Blue ×: final / box / zone / thermal start; S1–S7: lift terrain','Orange square + white markers: AB box; labels are target MSL','Magenta hatch: active MOA; gaps=cutouts; violet=raised floor','Purple dash: IFR/outerstrip finals; teal dash: proposed perimeter','Tan=B beach; open circle=nav site; B/A/C/S=bridge/arch/channel/stack']),('FEATURE KEY', [f['id']+' '+f['name'] for f in W['features'][:10]]),('FEATURE KEY / AIRPARKS',[f['id']+' '+f['name'] for f in W['features'][10:]]),('REVIEW STATUS',['G1 PENDING: player build + Ty VFR review remain.','Thin finals use supplied magnetic course + 7E.','Teal dash: proposed perimeter traffic reservation (Claude review).','AB1/AB2: 9 marker rafts + judge raft + 4 buoys each.','CZ1–CZ3/CZ5: HOT entry spawns targets/drones; COLD does not.','All elevations/geometry are targets except locked values.','Grid: 5 km; extent: 20 km radius from Frank Church.','Main-island coast is a design polygon, not real geography.','PALH belongs to PANC start; KAVX is not a start.','PNG and SVG generated from the same spec.'])]
 for col,(title,ls) in enumerate(legends):
  xx=85+col*420;parts.append(f'<text x="{xx}" y="{y}" font-family="Arial" font-size="15" font-weight="bold" fill="#233c44">{title}</text>')
  for i,t in enumerate(ls):parts.append(f'<text x="{xx}" y="{y+25+i*20}" font-family="Arial" font-size="11" fill="#263e46">{html.escape(t)}</text>')
 parts.append('</svg>');svg='\n'.join(parts)+'\n';(ROOT/'island-map.svg').write_text(svg)
 # Identical coordinates and annotation over the shared grid raster, with no map-only terrain.
 raster=base64.b64encode((ROOT/'island-elevation-base.png').read_bytes()).decode()
 marker='<circle cx="900.00" cy="900.00" r="740"'
 # Insert base raster immediately after the ocean ring, before transparent coast outlines.
 elevation=svg.replace('AERO PLAYGROUND / ISLAND WORLD','AERO PLAYGROUND / SHARED HEIGHT FIELD').replace('fill="#c2d0ac"','fill="none"').replace('fill="#a4947c"','fill="none"').replace('fill="#76665c"','fill="none"')
 idx=elevation.index('</circle>')+9 if '</circle>' in elevation else -1
 # Circle primitives are self-closing; locate the first (world ring) end exactly.
 pos=elevation.index('/>',elevation.index('<circle'))+2
 elevation=elevation[:pos]+f'<clipPath id="heightclip"><circle cx="900" cy="900" r="740"/></clipPath><image clip-path="url(#heightclip)" x="160" y="160" width="1480" height="1480" href="data:image/png;base64,{raster}"/><circle cx="900" cy="900" r="740" fill="none" stroke="#829ca8"/>'+elevation[pos:]
 key=[]
 for i,z in enumerate([0,700,1500,2200,2800]):
  col=tint(z);hexcol='#'+''.join(f'{v:02x}' for v in col);xx=730+i*125
  key.append(f'<rect x="{xx}" y="1707" width="125" height="10" fill="{hexcol}"/><text x="{xx}" y="1735" font-family="Arial" font-size="11">{round(z/FT)} ft</text>')
 elevation=elevation.replace('</svg>','<g>'+''.join(key)+'</g></svg>')
 (ROOT/'island-elevation.svg').write_text(elevation)

def report(m):
 from report_revision import write_report
 write_report(S,W,m,ROOT)

def raster_map():
 # macOS AppKit's native SVG reader; Swift module cache stays in a writable temp folder.
 helper=Path(__file__).with_name('render_svg.swift')
 with tempfile.TemporaryDirectory(prefix='island-swift-') as cache:
  for name in ['island-map','island-elevation']:
   cmd=['swift','-module-cache-path',cache,str(helper),str(ROOT/(name+'.svg')),str(ROOT/(name+'.png'))]
   out=subprocess.run(cmd,stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=60)
   if out.returncode:raise SystemExit('Native SVG raster failed: '+out.stderr.decode()[-1200:])

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--png',action='store_true');args=parser.parse_args();bake();m=measure();map_svg();report(m)
 if args.png:raster_map()
 print(json.dumps({'grid':'401 x 401, 100 m','rules':[{k:r[k] for k in ['rule','status']} for r in m['fit_check']],'output':str(ROOT)},indent=2))
if __name__=='__main__':main()
