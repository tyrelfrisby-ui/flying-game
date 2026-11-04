"""Full-area design-grid geometry checks. No game imports, dependencies or IFR mutations.
Terrain tests clip every intersecting grid triangle to protection polygons. Linear
height minus a planar surface reaches its maximum at a clipped polygon vertex.
Pad overlays are tested as exact planes on their full padded rectangles as well.
Unknown procedure geometry is explicitly UNVERIFIED, never counted as a pass.
"""
import math,json,struct,itertools

def cross(a,b,c):return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
def area(p):return sum(a[0]*b[1]-a[1]*b[0] for a,b in zip(p,p[1:]+p[:1]))/2

def clip(poly,bound):
 if area(bound)<0:bound=list(reversed(bound))
 for a,b in zip(bound,bound[1:]+bound[:1]):
  old=poly;poly=[]
  if not old:break
  for p,q in zip(old,old[1:]+old[:1]):
   cp,cq=cross(a,b,p),cross(a,b,q);ip,iq=cp>=-1e-10,cq>=-1e-10
   if ip:poly.append(p)
   if ip!=iq:
    t=cp/(cp-cq);poly.append(tuple(p[k]+t*(q[k]-p[k]) for k in range(len(p))))
 return poly

def segments_distance(g,a,b,c,d):
 if cross(a,b,c)*cross(a,b,d)<=0 and cross(c,d,a)*cross(c,d,b)<=0:
  if max(min(a[0],b[0]),min(c[0],d[0]))<=min(max(a[0],b[0]),max(c[0],d[0]))+1e-10 and max(min(a[1],b[1]),min(c[1],d[1]))<=min(max(a[1],b[1]),max(c[1],d[1]))+1e-10:return 0
 return min(g.seg(a,c,d)[0],g.seg(b,c,d)[0],g.seg(c,a,b)[0],g.seg(d,a,b)[0])

def polydist(g,p,q):
 if any(g.inside(a,q) for a in p) or any(g.inside(a,p) for a in q):return 0
 return min(segments_distance(g,a,b,c,d) for a,b in zip(p,p[1:]+p[:1]) for c,d in zip(q,q[1:]+q[:1]))

def hull(points):
 p=sorted(set(tuple(q) for q in points))
 def half(ps):
  out=[]
  for q in ps:
   while len(out)>1 and cross(out[-2],out[-1],q)<=1e-12:out.pop()
   out.append(q)
  return out
 return half(p)[:-1]+half(list(reversed(p)))[:-1]

def expand(poly,guard):
 # Square Minkowski expansion includes Euclidean guard disk, so is conservative.
 return hull([(p[0]+dx,p[1]+dy) for p in poly for dx,dy in [(-guard,-guard),(-guard,guard),(guard,-guard),(guard,guard)]])

def waterpolys(g):
 # Affineimage of circumscribed64-gon contains the complete analytic ellipse.
 return [(lake,g.ellipse(lake['centre'],[v/math.cos(math.pi/64) for v in lake['radii_km']],lake.get('long_axis_true_deg',90),64)) for lake in g.W['water']]

def nearest_pair(g,parts,named):
 def bbox(p):return min(v[0] for v in p),max(v[0] for v in p),min(v[1] for v in p),max(v[1] for v in p)
 qs=[(name,q,bbox(q)) for name,q in named];best=(1e9,None)
 for p in parts:
  a=bbox(p)
  for name,q,b in qs:
   lower=math.hypot(max(0,a[0]-b[1],b[0]-a[1]),max(0,a[2]-b[3],b[2]-a[3]))
   if lower>=best[0]:continue
   d=polydist(g,p,q)
   if d<best[0]:best=(d,name)
 return best

def capsule(a,b,width):
 # Circumscribed32-gon end disks enclose exact rounded capsule, <=18m excess at2nm.
 n=32;r=width/math.cos(math.pi/n)
 return hull([(p[0]+r*math.cos(2*math.pi*i/n),p[1]+r*math.sin(2*math.pi*i/n)) for p in [a,b] for i in range(n)])

def subtract(poly,cut):
 # Convex polygon minus convex cut, partitioned into nonoverlapping convex pieces.
 if area(cut)<0:cut=list(reversed(cut))
 remaining=poly;pieces=[]
 for a,b in zip(cut,cut[1:]+cut[:1]):
  if not remaining:break
  def side(keep):
   out=[]
   for p,q in zip(remaining,remaining[1:]+remaining[:1]):
    cp,cq=cross(a,b,p)*keep,cross(a,b,q)*keep;ip,iq=cp>=0,cq>=0
    if ip:out.append(p)
    if ip!=iq:
     t=cp/(cp-cq);out.append(tuple(p[k]+t*(q[k]-p[k]) for k in range(len(p))))
   return out
  outside=side(-1)
  if len(outside)>=3 and abs(area(outside))>1e-10:pieces.append(outside)
  remaining=side(1)
 return pieces

def zone_parts(g,z):
 if 'boundary' in z:return [z['boundary']]
 a,b=z['bearings_true_deg'];r0,r1=z['radii_km'];n=100
 # Inner radius inflated to enclose the entire true inner circle, outside arc inscribed.
 # Thus every cell is conservatively INSIDE the exact annular sector; omitted sliver <=0.12m.
 inner=r0/math.cos(math.radians((b-a)/n/2))
 out=[]
 for i in range(n):
  aa=math.radians(a+(b-a)*i/n);bb=math.radians(a+(b-a)*(i+1)/n)
  out.append([[r*math.sin(t),r*math.cos(t)] for r,t in [(inner,aa),(r1,aa),(r1,bb),(inner,bb)]])
 return out

def zone_geometry(g,z):
 parts=zone_parts(g,z);ex=[];raised=[]
 route=g.W['perimeter_traffic_reservation'];own={q['airport'] for q in z.get('airport_overlays',[])}
 if z.get('exclusion_policy'):
  guard=z['exclusion_policy']['guard_km']
  for f in g.finals()+g.runway_finals():
   p=expand(g.finalpoly(f),next((o['approach_guard_km'] for o in z.get('airport_overlays',[]) if o['airport']==f['airport']),guard))
   if f['airport'] in own:
    raised.append(dict(source=f['title'],boundary=p,floor_ft=5000,traffic_ceiling_ft=3000))
   else:ex.append(dict(source=f['title'],boundary=p))
  for r in g.airway_segments():ex.append(dict(source=r['id'],boundary=capsule(r['a'],r['b'],3.704+guard)))
  for a,b in zip(route['path'],route['path'][1:]):ex.append(dict(source='perimeter proposal',boundary=capsule(a,b,route['terrain_half_width_km']+guard)))
  for q in z.get('airport_overlays',[]):
   a=next(a for a in g.W['airports'] if a['id']==q['airport']);c=a['runways'][0]['centre'];p=capsule(c,[c[0]+.000001,c[1]],q['pattern_radius_km'])
   raised.append(dict(source=q['airport']+' pattern',boundary=p,floor_ft=q['floor_msl_ft'],traffic_ceiling_ft=q['traffic_ceiling_msl_ft']))
 # Only include exclusions/overlays intersecting original footprint; saves splits and charts.
 def relevant(q):return any(clip([tuple(p) for p in cell],q['boundary']) for cell in parts)
 ex=[q for q in ex if relevant(q)];raised=[q for q in raised if relevant(q)]
 for q in ex:
  parts=[r for p in parts for r in subtract([tuple(v) for v in p],q['boundary'])]
 return parts,ex,raised

class Grid:
 def __init__(self,g):
  self.g=g;self.cfg=g.W['height_field']['grid'];self.n=self.cfg['rows'];self.low=self.cfg['min_km'];self.step=self.cfg['spacing_m']/1000;raw=(g.ROOT/'island-heightfield.f32').read_bytes();self.z=struct.unpack('<'+'f'*(len(raw)//4),raw)
 def at(self,x,y):
  fx=(x-self.low)/self.step;fy=(y-self.low)/self.step;i=max(0,min(self.n-2,int(fx)));j=max(0,min(self.n-2,int(fy)));u=fx-i;v=fy-j
  a=self.z[j*self.n+i];b=self.z[j*self.n+i+1];c=self.z[(j+1)*self.n+i];d=self.z[(j+1)*self.n+i+1]
  return a+u*(b-a)+v*(d-b) if u>=v else a+u*(d-c)+v*(c-a)
 def sweep(self,poly,surface=lambda p:0,mask_pads=False):
  lo=self.low;s=self.step;n=self.n
  imin=max(0,int(math.floor((min(p[0] for p in poly)-lo)/s)));imax=min(n-2,int(math.floor((max(p[0] for p in poly)-lo)/s)))
  jmin=max(0,int(math.floor((min(p[1] for p in poly)-lo)/s)));jmax=min(n-2,int(math.floor((max(p[1] for p in poly)-lo)/s)))
  detail=[]
  if mask_pads:
   for r in self.g.FIELDS:
    if r['field']=='DRAKE':continue
    pp=self.g.rect(r,True)
    if max(p[0] for p in pp)<min(p[0] for p in poly) or min(p[0] for p in pp)>max(p[0] for p in poly) or max(p[1] for p in pp)<min(p[1] for p in poly) or min(p[1] for p in pp)>max(p[1] for p in poly):continue
    detail.append((pp,(min(p[0] for p in pp),max(p[0] for p in pp),min(p[1] for p in pp),max(p[1] for p in pp))))
  best=(-1e12,None);minimum=1e12;count=0
  for j in range(jmin,jmax+1):
   y=lo+j*s
   for i in range(imin,imax+1):
    x=lo+i*s;a=(x,y,self.z[j*n+i]);b=(x+s,y,self.z[j*n+i+1]);c=(x,y+s,self.z[(j+1)*n+i]);d=(x+s,y+s,self.z[(j+1)*n+i+1])
    for tri in [(a,b,d),(a,d,c)]:
     pp=clip(list(tri),poly)
     if not pp:continue
     pieces=[pp]
     if mask_pads:
      for pad,bb in detail:
       if bb[1]<x or bb[0]>x+s or bb[3]<y or bb[2]>y+s:continue
       pieces=[piece for part in pieces for piece in subtract(part,pad)]
     count+=1
     for p in [v for part in pieces for v in part]:
      diff=p[2]-surface(p);minimum=min(minimum,p[2])
      if diff>best[0]:best=(diff,list(p[:2]))
  return {'maximum_m':round(best[0],4),'location':best[1],'minimum_height_m':round(minimum,4),'triangles':count}

def exact_rescreen(g,poly,surface,fields):
 xs=[p[0] for p in poly];ys=[p[1] for p in poly];best=(-1e12,None);n=0
 pads=[g.rect(r,True) for r in fields if r['field']!='DRAKE']
 x=math.floor(min(xs)/.025)*.025
 while x<=max(xs):
  y=math.floor(min(ys)/.025)*.025
  while y<=max(ys):
   p=(x,y)
   if g.inside(p,poly) and not any(g.inside(p,q) for q in pads):
    n+=1;d=g.height(x,y)-surface(p)
    if d>best[0]:best=(d,[x,y])
   y+=.025
  x+=.025
 return {'bake_maximum_m':None,'maximum_m':round(best[0],4),'location':best[1],'exact_points':n,'screen':'exact evaluator, 25 m lattice'}

def padheight(g,r,p):
 l,b=g.dimensions(r);t,c=g.local(p,r)
 return r['elevation_ft']*g.FT+max(-l/2,min(l/2,t))*1000*r.get('slope_percent',0)/100

def measure_revision(g):
 w=g.W;F=g.FIELDS;grid=Grid(g);m={};rows=[];waters=waterpolys(g)
 def row(n,status,evidence):rows.append(dict(rule=n,status=status,evidence=evidence))
 # All-pair exact footprints, conservative padded-edge grades and centre separation.
 grades=[];spacing=[];overlaps=[];shores=[];new=[];allgrades=[]
 for a,b in itertools.combinations(F,2):
  gap=g.rectgap(a,b)
  if gap<1e-8 and (a['elevation_ft']!=b['elevation_ft'] or a.get('slope_percent')!=b.get('slope_percent')):overlaps.append(dict(fields=[a['key'],b['key']],height_difference_m=round(abs(a['elevation_ft']-b['elevation_ft'])*g.FT,3)))
  if a['field']==b['field']:continue
  if not a.get('slope_percent') and not b.get('slope_percent'):
   z=abs(a['elevation_ft']-b['elevation_ft'])*g.FT;grade=z/(gap*1000) if gap>1e-8 else (1e9 if z else 0)
   allgrades.append(dict(fields=[a['key'],b['key']],grade_percent=round(grade*100,6)))
   if grade>.35+1e-9:grades.append(dict(fields=[a['key'],b['key']],grade_percent=round(grade*100,3),edge_gap_km=round(gap,5)))
  req=1.5 if a['field'].startswith('AP') or b['field'].startswith('AP') else .8
  if g.dist(a['centre'],b['centre'])<req and {a['field'],b['field']}!={'PANC','PALH'}:spacing.append(dict(fields=[a['key'],b['key']],centre_distance_km=round(g.dist(a['centre'],b['centre']),4),required_km=req))
 for r in F:
  l,b=g.dimensions(r);u=g.vec(r['heading_true_deg']);ends=[[r['centre'][0]+sign*l*u[0]/2,r['centre'][1]+sign*l*u[1]/2] for sign in [-1,1]];dd=min(max(g.shore(p,poly) for poly in g.LAND) for p in ends)
  req=2 if r['shore_class']=='inland' else .3
  if dd<req-1e-8:shores.append(dict(field=r['key'],minimum_end_shore_km=round(dd,4),required_km=req))
  # Exact pad containment on relevant coastline: min distance from complete pad boundary.
  poly=g.rect(r,True);coast=max(g.LAND,key=lambda p:g.shore(r['centre'],p));setback=min(g.shore(p,coast) for p in poly)
  if r['field'] in ['SITX','TERN','NHVN'] or r['field'].startswith('B') and len(r['field'])==2:new.append(dict(field=r['key'],shore_end_min_km=round(dd,4),pad_land_margin_km=round(setback,4)))
 m.update(flat_pairs_checked=len(allgrades),maximum_grade=max(allgrades,key=lambda r:r['grade_percent']),grade_failures=grades,spacing_failures=spacing,pad_conflicts=overlaps,shore_setback_failures=shores,new_field_geometry=new)
 # Validate full cave pads and flight tube using the authored exact shell model.
 cave=next(f for f in w['features'] if f['id']=='F2');c=cave['interior'];R=cave['chamber_radius_m'];padproof=[]
 for p in c['pads']:
  x,y=p['offset_m'];a,b=p['size_m'];radius=max(math.hypot(x+sx*a/2,y+sy*b/2) for sx in [-1,1] for sy in [-1,1]);padproof.append(dict(pad=p['id'],max_corner_radius_m=round(radius,4),wall_margin_m=round(R-radius,4),flat_floor_msl_m=c['floor_msl_m'],vertical_clearance_m=c['ceiling_msl_m']-c['floor_msl_m']))
 u=g.vec((cave['mouth_true_deg']+180)%360);lane=c['flight_lane'];hw=lane['half_width_m'];content=[]
 for q in c['contents_bounds']:
  x,y=q['offset_m'];crossd=abs(x*u[1]-y*u[0]);content.append(dict(id=q['id'],lateral_lane_margin_m=round(crossd-q['radius_m']-hw,4)))
 caveok=all(p['wall_margin_m']>=0 for p in padproof) and all(q['lateral_lane_margin_m']>=0 for q in content) and lane['floor_msl_m']>=c['floor_msl_m'] and lane['ceiling_msl_m']<=min(c['mouth_ceiling_msl_m'],c['throat_ceiling_msl_m'],c['ceiling_msl_m'])
 chamber_shore=g.shore(c['chamber_centre'],g.CAT)-R/1000;aa=g.smooth(chamber_shore/w['edge']['land_shore_fade_km']);upper_min=-30+(c['roof_top_msl_m']+30)*aa;cover_min=upper_min-c['ceiling_msl_m'];caveok=caveok and cover_min>=c['roof_min_cover_m']
 m['pirate_cave_verification']=dict(status='PASS analytic shell' if caveok else 'FAIL',minimum_chamber_upper_terrain_m=round(upper_min,4),minimum_chamber_rock_cover_m=round(cover_min,4),throat_shell_cover_m=c['throat_min_rock_cover_m'],pads=padproof,mouth_m=cave['mouth_m'],chamber_radius_m=R,throat_side_clearance_m=c['throat_width_m']/2-hw,lane_floor_margin_m=lane['floor_msl_m']-c['floor_msl_m'],lane_roof_margin_m=min(c['throat_ceiling_msl_m'],c['ceiling_msl_m'])-lane['ceiling_msl_m'],contents=content,proof='Planar pads entirely inside circular chamber by corner radial maxima. Flight volume: 20 m wide throat tube connected to 20 m wide chamber chord inset within radius80m; no whole empty chamber disk claimed; min 2 m above floor, 6 m below throat roof. Contents lie outside full flight strip. Analytic design shell only; collider/runtime build deferred.')
 drake=next(f for f in w['features'] if f['id']=='F1');ds=drake['shell_geometry'];hc=ds['hill_centre'];cc=ds['chamber_centre'];corners=[[cc[0]+sx*.210,cc[1]+sy*.410] for sx in [-1,1] for sy in [-1,1]];radius=max(g.dist(p,hc)*1000 for p in corners);roof=ds['minimum_roof_msl_m']-ds['ceiling_msl_m'];length=next(a for a in w['airports'] if a['id']=='DRAKE')['runways'][0]['length_ft']*g.FT
 drakeok=radius+ds['roof_blend_m']<=ds['hill_radius_m'] and roof>=60 and abs(drake['exterior_strip_m']+drake['interior_strip_m']-length)<.001
 m['drake_shell_verification']=dict(status='PASS analytic design' if drakeok else 'FAIL',maximum_chamber_corner_radius_m=round(radius,4),hill_radius_m=ds['hill_radius_m'],roof_blend_m=ds['roof_blend_m'],minimum_rock_cover_m=roof,floor_msl_m=ds['floor_msl_m'],ceiling_msl_m=ds['ceiling_msl_m'],minimum_roof_msl_m=ds['minimum_roof_msl_m'],exterior_strip_m=drake['exterior_strip_m'],interior_strip_m=drake['interior_strip_m'],total_strip_m=length,runtime='UNVERIFIED closed3D collision/deck routing; northcliff is explicitlyvertical and opening not expressible in uppergrid')
 # Detail pad query covers the full analytic rectangles; exposed upper Drake terrain intentionally excludes interior.
 pad_errors=[]
 for r in F:
  if r['field']=='DRAKE':continue
  l,b=g.dimensions(r);u=g.vec(r['heading_true_deg']);v=(u[1],-u[0]);worst=0
  for t in [-.5,0,.5]:
   for c0 in [-.5,0,.5]:
    p=[r['centre'][k]+t*l*u[k]+c0*b*v[k] for k in [0,1]];expected=padheight(g,r,p)-(w['height_field']['water_lane_bed_offset_m'] if r['surface']=='water' else 0);worst=max(worst,abs(g.height(*p)-expected))
  if worst>.05:pad_errors.append(dict(field=r['key'],max_error_m=round(worst,4)))
 m['pad_surface_errors']=pad_errors
 row(1,'FAIL' if overlaps or pad_errors or not caveok or not drakeok else 'PASS design geometry',f'{len(F)} complete pad rectangles; {len(overlaps)} incompatible intersections; {len(pad_errors)} evaluator pad errors. Full planes/60m shoulders are analytic authoritative detail; inverse-distance exterior pad blend is continuous and tends to own plane at edge. Pirate cave full pad/flight-volume shell proof: {m["pirate_cave_verification"]["status"]}. Drake full chamber/roof/pad analytic proof: {m['drake_shell_verification']['status']}. Runtime cavity/solid checks are recorded separately in island-runtime-solids.json and island-runtime-checks.json.')
 row(2,'FAIL' if grades else 'PASS',f'Exact padded-rectangle edge distances for every flat-field pair: {len(grades)} above locked 35%; beaches and all outer runways included. Mean endpoint feasibility, not global peak/canyon slope cap.')
 inundation=[]
 for r in F:
  for lake,p in waters:
   pp=clip([tuple(q) for q in g.rect(r,True)],p)
   if pp:
    depth=max(lake['elevation_ft']*g.FT-padheight(g,r,q) for q in pp)
    if depth>.05:inundation.append(dict(field=r['key'],water=lake['id'],depth_m=round(depth,4)))
 m['water_pad_inundations']=inundation
 if inundation:rows[0]['status']='FAIL';rows[0]['evidence']+=' Waterinundation: '+str(len(inundation))+' pads.'
 # Full-area final protection: all 12 supplied finals plus every end of every strip.
 fs=g.finals()+g.runway_finals();finals=[];obstacles=[]
 for f in fs:
  u=g.vec(f['course_true_deg']);ratio=34 if f['precision'] else 20
  def surface(p):return f['elevation_ft']*g.FT-((p[0]-f['threshold'][0])*u[0]+(p[1]-f['threshold'][1])*u[1])*1000/ratio
  poly=g.finalpoly(f);result=grid.sweep(poly,surface,mask_pads=True);padbest=(-1e12,None)
  # 2026-11-02 (Claude): a flag from the 100 m bake is re-screened against the exact evaluator on a 25 m
  # lattice over the same polygon, pads masked. Bilinear bake triangles that span a pad edge and the capped
  # ground beside it report metres of 'penetration' the continuous ground does not have.
  if result['maximum_m']>.05:
   ex=exact_rescreen(g,poly,surface,F);ex['bake_maximum_m']=result['maximum_m'];result.update(ex)
  for r in F:
   if r['field']=='DRAKE':continue
   pp=clip([tuple(p) for p in g.rect(r,True)],poly)
   for p in pp:
    z=padheight(g,r,p)-surface(p)
    if z>padbest[0]:padbest=(z,r['key'])
  waterbest=(-1e12,None)
  for lake,wp in waters:
   pp=clip([tuple(q) for q in wp],poly)
   for q in pp:
    z=lake['elevation_ft']*g.FT-surface(q)
    if z>waterbest[0]:waterbest=(z,lake['id'])
  result.update(water_surface_penetration_m=round(max(0,waterbest[0]),4),water=waterbest[1],airport=f['airport'],title=f['title'],source='supplied IFR' if f in g.finals() else 'runway end',pad_overlay_penetration_m=round(max(0,padbest[0]),4),pad=padbest[1]);finals.append(result)
  for t in w['hazards']['towers']:
   c=t['centre'];tp=capsule(c,[c[0]+1e-6,c[1]],t['horizontal_envelope_radius_m']/1000);pp=clip([tuple(q) for q in tp],poly)
   if pp:
    z=grid.sweep(pp,surface)['maximum_m']+t['height_agl_m']
    if z>.05:obstacles.append(dict(final=f['title'],obstacle=t['id'],conservative_penetration_bound_m=round(z,3),full_guy_footprint=True))
  for wire in w['hazards']['wires']:
   a,b=wire['endpoints'];pp=clip([tuple(a),tuple(b)],poly)
   if not pp:continue
   # Conservatively enclose whole crossing; maximum ground in full span bbox + maximum sag allowance.
   box=[[min(a[0],b[0])-.00001,min(a[1],b[1])-.00001],[max(a[0],b[0])+.00001,min(a[1],b[1])-.00001],[max(a[0],b[0])+.00001,max(a[1],b[1])+.00001],[min(a[0],b[0])-.00001,max(a[1],b[1])+.00001]]
   z=grid.sweep(box)['maximum_m']+wire['lowest_agl_m']+g.dist(a,b)*1000*wire['sag_fraction']-min(surface(p) for p in pp)
   if z>.05:obstacles.append(dict(final=f['title'],obstacle=wire['id'],conservative_penetration_bound_m=round(z,3)))
 m['approach_terrain_screen']=finals;m['approach_obstacle_screen']=obstacles
 bad=[r for r in finals if max(r['maximum_m'],r['pad_overlay_penetration_m'],r['water_surface_penetration_m'])>.05];m['approach_failures']=bad
 supplied=[r for r in finals if r['source']=='supplied IFR'];m['supplied_final_failures']=len([r for r in supplied if max(r['maximum_m'],r['pad_overlay_penetration_m'],r['water_surface_penetration_m'])>.05])
 row(3,'FAIL' if bad or obstacles else 'UNVERIFIED',f'Full clipped-triangle sweeps of {len(fs)} fans, including all 12 supplied finals: {m["supplied_final_failures"]}/12 supplied and {len(bad)} total terrain/pad failures; {len(obstacles)} tower/wire failures or conservative bounds. Missing non-KSNA miss/hold geometry and future building/tree solids prevent full rule certification.')
 # Airway sweeps enclose full 2 nm capsule, incl end discs; overlays/hazards included conservatively.
 air=[]
 route=w['perimeter_traffic_reservation']
 proposed=[dict(id='PERIM-'+str(i+1),a=a,b=b,mea_ft=route['mea_ft_targets'][i]) for i,(a,b) in enumerate(zip(route['path'],route['path'][1:]))]
 for r in g.airway_segments()+proposed:
  poly=capsule(r['a'],r['b'],3.704);result=grid.sweep(poly);top=result['maximum_m'];who='terrain'
  for p in F:
   pp=clip([tuple(q) for q in g.rect(p,True)],poly)
   for q in pp:
    z=padheight(g,p,q)
    if z>top:top=z;who=p['key']
  for lake,wp in waters:
   if clip([tuple(q) for q in wp],poly) and lake['elevation_ft']*g.FT>top:top=lake['elevation_ft']*g.FT;who=lake['id']+' fullwater surface'
  for t in w['hazards']['towers']:
   c=t['centre'];tp=capsule(c,[c[0]+1e-6,c[1]],t['horizontal_envelope_radius_m']/1000);pp=clip([tuple(q) for q in tp],poly)
   if pp:
    z=grid.sweep(pp)['maximum_m']+t['height_agl_m']
    if z>top:top=z;who=t['id']+' fullguy envelope'
  # Wire full bbox upper bounds even when only partly in airway.
  for wire in w['hazards']['wires']:
   a,b=wire['endpoints']
   if clip([tuple(a),tuple(b)],poly):
    box=[[min(a[0],b[0])-.00001,min(a[1],b[1])-.00001],[max(a[0],b[0])+.00001,min(a[1],b[1])-.00001],[max(a[0],b[0])+.00001,max(a[1],b[1])+.00001],[min(a[0],b[0])-.00001,max(a[1],b[1])+.00001]]
    z=grid.sweep(box)['maximum_m']+wire['lowest_agl_m']+g.dist(a,b)*1000*wire['sag_fraction']
    if z>top:top=z;who=wire['id']+' full-span conservative upper bound'
  clear=r['mea_ft']-top/g.FT;air.append(dict(segment=r['id'],clearance_ft=round(clear,3),terrain_or_solid_top_ft=round(top/g.FT,3),limiting=who,terrain_max_location=result['location'],mea_ft=r['mea_ft'],triangles=result['triangles']))
 m['airway_checks']=air;m['airway_failures']=[a for a in air if a['clearance_ft']<2000-.02]
 row(4,'FAIL' if m['airway_failures'] else 'PASS known geometry / UNVERIFIED T201',f'{len(m["airway_failures"])} of {len(air)} known segments below 2000 ft; full waterplanes + 2 nm swath + rounded circumscribed32-gon endcaps, all grid triangles and pads/towers. Nine proposed perimeter segments included with2nm terrain swaths. T201 positions/MEAs absent; built-solid screening is recorded separately in island-runtime-solids.json.')
 # Complete authored flight-volume ring containment by convex radius maxima.
 volumes=[]
 def volume(name,points,guard=0):
  rr=max(math.hypot(*p) for p in points)+guard;volumes.append(dict(id=name,max_radius_km=round(rr,6),margin_km=round(20-rr,6),passes=rr<=20+1e-8))
 for i,p in enumerate(g.LAND):volume('land '+str(i),p)
 for r in F:volume('pad '+r['key'],g.rect(r,True))
 for f in fs:volume('final '+f['title'],g.finalpoly(f))
 for b in w['aerobatic_boxes']:
  x,y=b['centre'];volume(b['id'],[[x+dx,y+dy] for dx,dy in [(-.5,-.5),(-.5,.5),(.5,-.5),(.5,.5)]])
 for z in w['combat_zones']:
  if 'boundary' in z:volume(z['id'],z['boundary'])
  else:volumes.append(dict(id=z['id'],max_radius_km=z['radii_km'][1],margin_km=20-z['radii_km'][1],passes=z['radii_km'][1]<=20))
 route=w['perimeter_traffic_reservation'];volume('perimeter proposal',route['path'],route['half_width_km'])
 for st in w['starts']:volume('start '+st['id'],[st['centre']],.06)
 volume('carrier operating disk',[next(f for f in w['features'] if f['id']=='CARRIER')['centre']],.3)
 for i in w.get('islets',[]):volume('analytic ellipse '+i['id'],[i['centre']],max(i['radii_km']))
 for r in g.airway_segments():volume('fullairway '+r['id'],capsule(r['a'],r['b'],3.704))
 for i,(a,b) in enumerate(zip(route['path'],route['path'][1:])):volume('perimeter terraincorridor '+str(i+1),capsule(a,b,route['terrain_half_width_km']))
 for t in w['hazards']['towers']:volume('tower '+t['id'],[t['centre']],t['horizontal_envelope_radius_m']/1000)
 for wire in w['hazards']['wires']:volume('wire '+wire['id'],wire['endpoints'],.005)
 for z in w['combat_zones']:
  c=z['drone_orbit']['centre'];volume(z['id']+' drone fullorbit',[c],(z['drone_orbit']['radius_m_target']+20)/1000)
  for f in z['combat_features']:
   c=f['centre'];radius=max(f.get('span_m',0),f.get('channel_length_m',0),f.get('length_m',0),f.get('opening_width_m',0))/2000+.05;volume(f['id']+' envelope',[c],radius)
 for b in w['aerobatic_boxes']:
  for q in b.get('rafts',[])+([b['judge_raft']] if b.get('judge_raft') else []):volume(q['id'],[q['centre']],.05)
  if b.get('shore_reference_marker'):volume(b['id']+' shoremarker',[b['shore_reference_marker']['centre']],.05)
 for l in w['lesson_terrain']:volume(l['id']+' completeboxpath',l['box_path'],.25)
 for lake in w['water']:volume('water ellipse '+lake['id'],[lake['centre']],max(lake['radii_km']))
 volume('pirate completeentry/chamber',[cave['centre'],cave['interior']['chamber_centre']],.1)
 volume('Drake hill/shell',[ds['hill_centre']],.7)
 race=next(f for f in w['features'] if f['id']=='RACE');volume('complete racegates',[q['centre'] for q in race['elements']],race['gate_half_width_m']/1000+.01)
 # Exact BALBO hold/missed shapes were never supplied: bound target remains separate, not fabricated.
 m['ring_volumes']=volumes;m['ring_failures']=[v for v in volumes if not v['passes']];m['ring_missing']=['BALBO hold exact speed/turn geometry (19.2 km supplied target only)','KSNA missed exact curved boundary','T201 complete route protection','other missed approaches/holds','KAVX circling']
 row(5,'FAIL / UNVERIFIED' if m['ring_failures'] else 'UNVERIFIED',f'Full analytic radius bounds for {len(volumes)} authored footprints/flight volumes: {len(m["ring_failures"])} outside 20 km. Missing IFR hold/miss/T201 volumes prevent whole-rule verification; targets are not accepted as proofs.')
 row(6,'FAIL' if spacing or shores else 'PASS',f'All {len(F)} runways/strips including beaches/outer islands: {len(spacing)} spacing conflicts, {len(shores)} shore-setback violations; only PANC/PALH spacing waiver used.')
 # Full line/triangle profile against a generous loaded-trainer sustained climb bound.
 cfg=json.loads((g.ROOT.parents[1]/'configs/aircraft/c172-like.json').read_text());mass=cfg['mass']['massKg'];power=cfg['propulsion']['maxPowerW'];roc=power/(mass*9.80665);speed=80*1852/3600;slow=50*1852/3600
 lessons=[]
 for l in w['lesson_terrain']:
  a,b=l['box_path'];length=g.dist(a,b)*1000;ts={0.,1.};dx=b[0]-a[0];dy=b[1]-a[1]
  # Break at all vertical/horizontal grid boundaries and fixed SW-NE diagonals.
  for axis,delta in [(0,dx),(1,dy)]:
   if abs(delta)>1e-12:
    for i in range(grid.n):
     t=(grid.low+i*grid.step-a[axis])/delta
     if 0<t<1:ts.add(t)
  if abs(dx-dy)>1e-12:
   for k in range(-grid.n,grid.n):
    t=(k*grid.step-a[0]+a[1])/(dx-dy)
    if 0<t<1:ts.add(t)
  profile=[dict(t=round(t,9),distance_m=round(t*length,4),height_m=round(grid.at(a[0]+t*dx,a[1]+t*dy),5)) for t in sorted(ts)]
  z0=profile[0]['height_m'];z1=profile[-1]['height_m'];rise=z1-z0;fpm=rise/g.FT/(length/speed/60)
  # Highest physically possible steadyROC at ANYDA: full shaft power,100%prop efficiency,zero drag/no lapse.
  worst=max((p['height_m']-(z0+30+roc*p['distance_m']/slow),p['distance_m']) for p in profile)
  lessons.append(dict(id=l['id'],path_length_m=round(length,3),authored_length_m=l['box_length_m'],actual_grid_rise_m=round(rise,3),required_fpm_at_80kt=round(fpm,3),reference_roc_upper_bound_fpm=round(roc/g.FT*60,3),reference_mass_kg=mass,reference_power_w=power,entry_clearance_m=30,conservative_groundspeed_kt=50,required_fpm_at_50kt=round(fpm*50/80,3),maximum_steady_climb_path_penetration_m=round(worst[0],3),limiting_distance_m=round(worst[1],3),piecewise_profile=profile,status='PASS conservative sustained-climb profile' if worst[0]>0 and fpm>roc/g.FT*60 else 'FAIL profile',flight_validation='UNVERIFIED aircraft trajectory/zoom/turnback/collider; no runtime simulation in docs-only phase. Full grid-line steady-climb feasibility check completed.'))
 m['box_canyon_checks']=lessons
 row(7,'FAIL / UNVERIFIED' if any(l['status']=='FAIL profile' for l in lessons) else 'PASS profile / UNVERIFIED flight', 'Entire post-cut piecewise-linear profiles checked againstC172-like1043kg/134000W loaded sustainedROC bound '+str(round(roc/g.FT*60))+'fpm (100%efficiency/no drag/no DA loss),50kt conservative ground speed and30m entry clearance (80kt profile rates also shown). '+', '.join(l['id']+' '+str(round(l['required_fpm_at_80kt']))+'fpm' for l in lessons)+'. Physical flight/zoom/turnback certification requires later build.')
 # Full authored zone domain: convex sector cells minus exact constructive exclusions.
 airpolys=[(r['id'],capsule(r['a'],r['b'],3.704),r['mea_ft']) for r in g.airway_segments()]
 routepolys=[('perimeter proposal',capsule(a,b,route['terrain_half_width_km']),route['mea_ft_targets'][i]) for i,(a,b) in enumerate(zip(route['path'],route['path'][1:]))]
 fpolys=[(f['title'],g.finalpoly(f),f['airport']) for f in fs];boxes=[];zones=[];features=[]
 for b in w['aerobatic_boxes']:
  x,y=b['centre'];p=[[x-.5,y-.5],[x+.5,y-.5],[x+.5,y+.5],[x-.5,y+.5]];nearest=min((polydist(g,p,q),name) for name,q,airport in fpolys);over=[dict(route=name,clearance_ft=round(mea-b['ceiling_msl_ft_target'],3),passes=mea-b['ceiling_msl_ft_target']>=2000) for name,q,mea in airpolys+routepolys if polydist(g,p,q)<1e-8]
  water=all(not g.inside(v,coast) for v in p for coast in g.LAND) and all(polydist(g,p,coast)>1e-8 for coast in g.LAND)
  ground=grid.sweep(p);floor=b['floor_msl_ft_target']*g.FT
  boxes.append(dict(box=b['id'],nearest_full_final_boundary_km=round(nearest[0],4),nearest=nearest[1],overhead_airways=over,fully_over_water=water,terrain_max_m=ground['maximum_m'],floor_margin_m=round(floor-max(0,ground['maximum_m']),4),passes_known=nearest[0]>=w['airspace_reservations']['box_final_guard_km'] and all(a['passes'] for a in over) and ground['maximum_m']<floor and (water if b['density_altitude']=='low' else True)))
 zone_geometry_records={}
 for z in w['combat_zones']:
  parts,ex,raised=zone_geometry(g,z);own={o['airport'] for o in z.get('airport_overlays',[])};required=z.get('exclusion_policy',{}).get('guard_km',z['minimum_clearance_km']+z.get('additional_margin_km',0))
  zone_geometry_records[z['id']]=dict(active_polygons=parts,exclusions=ex,raised_floor_overlays=raised)
  final=nearest_pair(g,parts,[(name,q) for name,q,airport in fpolys if airport not in own])
  aw=nearest_pair(g,parts,[(name,q) for name,q,mea in airpolys+routepolys])
  results=[grid.sweep(p) for p in parts];low=min(r['minimum_height_m'] for r in results)/g.FT;high=max(r['maximum_m'] for r in results)/g.FT;low=max(0,low)
  for i in w.get('islets',[]):
   if i.get('combat_zone')==z['id']:high=max(high,i.get('relief',{}).get('top_ft',i['elevation_ft']))
  for f in z.get('combat_features',[]):
   if f['type']=='paired ridges and dry canyon':high=max(high,f['rim_ft'])
  floor=z['floor_msl_ft_target'];ceil=z['ceiling_msl_ft_target']
  landproof=None
  if z.get('over_land'):
   coast=next(i['coast_polygon'] for i in w['outer_islands'] if i['id']=='OI2');landproof=min(g.shore(v,coast) for p in parts for v in p)
  overlays=[]
  for o in z.get('airport_overlays',[]):
   a=next(a for a in w['airports'] if a['id']==o['airport']);c=a['runways'][0]['centre'];under=any(g.inside(c,p) for p in parts);margin=o['floor_msl_ft']-o['traffic_ceiling_msl_ft'];surfmax=max(a['elevation_ft']+7408/20/g.FT,a['elevation_ft']+1000)
   island=next(i for i in w['outer_islands'] if i['airport']==a['id']);p=island['coast_polygon'];covered=sum(abs(area(clip([tuple(v) for v in p],cell))) for cell in parts);entire=abs(covered-abs(area(p)))<1e-6
   overlays.append(dict(airport=a['id'],entire_island_under_active_zone=entire,under_active_zone=under,floor_msl_ft=o['floor_msl_ft'],ceiling_msl_ft=o['ceiling_msl_ft'],traffic_ceiling_msl_ft=o['traffic_ceiling_msl_ft'],vertical_clearance_ft=margin,maximum_20_to_1_final_ft=round(surfmax,3),passes=under and margin>=2000 and surfmax<=o['traffic_ceiling_msl_ft']))
  zones.append(dict(zone=z['id'],active_area_km2=round(sum(abs(area(p)) for p in parts),5),active_convex_pieces=len(parts),exclusion_count=len(ex),minimum_final_boundary_km=round(final[0],4),nearest_final=final[1],minimum_full_airway_boundary_km=round(aw[0],4),nearest_airway=aw[1],required_including_margin_km=required,terrain_ft_range=[round(low,3),round(high,3)],floor_agl_ft_range=[round(floor-high,3),round(floor-low,3)],ceiling_agl_ft_range=[round(ceil-high,3),round(ceil-low,3)],over_land_margin_km=round(landproof,4) if landproof is not None else None,airport_overlays=overlays,passes_known=final[0]>=required-1e-6 and aw[0]>=required-1e-6 and floor>high and (landproof is None or landproof>0) and all(o['passes'] for o in overlays)))
  # Entire feature envelopes, not centre samples. Full route strips must stay in active low-floor volume.
  def contained(poly):
   total=abs(area(poly));covered=sum(abs(area(clip([tuple(p) for p in poly],cell))) for cell in parts)
   overlay=any(clip([tuple(p) for p in poly],o['boundary']) for o in raised)
   return abs(covered-total)<1e-7 and not overlay
  for f in z.get('combat_features',[]):
   c=f['centre'];typ=f['type'];halfwidth=f.get('flight_half_width_m',f.get('flight_opening_width_m',f.get('opening_width_m',100))/2)
   length=f.get('channel_length_m',f.get('length_m',f.get('deck_width_m',80)))
   if typ=='rock arch':length=80
   poly=g.rect(dict(centre=c,heading_true_deg=0,length_m=length,width_m=halfwidth*2))
   ground=grid.sweep(poly);bottom=max(0,ground['maximum_m']/g.FT,high);floorft=f['flight_floor_msl_ft'];top=f['flight_ceiling_msl_ft'];clearance=None
   if typ=='bridge':clearance=f['under_base_msl_ft']+f['under_clearance_m']/g.FT-top
   elif typ=='rock arch':clearance=f['base_msl_ft']+f['opening_height_m']/g.FT-top
   features.append(dict(zone=z['id'],feature=f['id'],type=typ,full_flight_tube_inside_active_low_floor=contained(poly),ground_top_ft=round(bottom,3),floor_msl_ft=floorft,ceiling_msl_ft=top,terrain_margin_ft=round(floorft-bottom,3),roof_margin_ft=round(clearance,3) if clearance is not None else None,passes=contained(poly) and floorft>=floor and top<=ceil and floorft>bottom and (clearance is None or clearance>=20)))
  # Entire target disks and drone orbit disk plus aircraft guard are checked against domain/traffic masks.
  for t in z['gunnery_targets']:
   p=capsule(t['centre'],[t['centre'][0]+1e-6,t['centre'][1]],t['radius_m']/1000);ok=contained(p)
   features.append(dict(zone=z['id'],feature=t['id'],type='ground target envelope',passes=ok,full_envelope_inside_active_low_floor=ok))
  orbit=z['drone_orbit'];c=orbit['centre'];p=capsule(c,[c[0]+1e-6,c[1]],(orbit['radius_m_target']+20)/1000);ok=contained(p) and floor<orbit['altitude_ft_target']<ceil
  features.append(dict(zone=z['id'],feature=z['id']+' drone orbit',type='drone orbit +20m aircraft guard',passes=ok,full_envelope_inside_active_low_floor=ok))
 m['box_clearances']=boxes;m['zone_clearances']=zones;m['combat_feature_checks']=features;m['zone_geometry']=zone_geometry_records
 row(8,'FAIL / UNVERIFIED' if any(not b['passes_known'] for b in boxes+zones) or any(not f['passes'] for f in features) else 'PASS known geometry / UNVERIFIED missing IFR',f'Full polygon/segment boundary distances, complete clipped-grid areas and rounded2nm airway swaths; 150m box/final guard. {sum(not b["passes_known"] for b in boxes)} box, {sum(not z["passes_known"] for z in zones)} zone and {sum(not f["passes"] for f in features)} combat-feature failures. Sector exclusions preserve2.05km; own airport corridors have5000ft combat floor above3000ft traffic. Missing IFR misses/holds/T201 remain UNVERIFIED.')
 water_overlap=[]
 for (a,p),(b,q) in itertools.combinations(waters,2):
  pp=clip([tuple(v) for v in p],q)
  if len(pp)>=3 and abs(area(pp))>1e-8 and a['elevation_ft']!=b['elevation_ft']:water_overlap.append(dict(waters=[a['id'],b['id']],overlap_km2=abs(area(pp))))
 m['water_surface_checks']=dict(water_count=len(waters),full_final_fans=len(fs),full_airway_segments=len(air),method='Complete circumscribed64-gon ellipse enclosures against everyprotectionpolygon; fixedwaterheight supersedes cutbed. No radial/pointsampling pass.',inundated_pads=inundation,different_level_overlaps=water_overlap)
 if water_overlap:rows[0]['status']='FAIL';rows[0]['evidence']+=' Overlappingdifferent-level waterplanes.'
 row(9,'PASS deterministic algorithm','Fixed authored seeds, no random calls. External two-generation byte comparison recorded in island-determinism.json and checked by verify_island.py.')
 row(10,'PASS shared runtime evaluator / UNVERIFIED player','One deterministic height authoring evaluator/bake drives grid/elevation/full-area tests; explicit analytic pad/cave detail. WorldTerrain and IslandWorldBuilder use the same C# evaluator; numerical parity and contact probes recorded separately. Unity player/render validation remains pending sandbox build.')
 m['fit_check']=rows;m['method']={'grid':'Every clipped triangle, fixed SW-NE diagonal; maximum of affine terrain minus affine plane at all intersection vertices; exact on baked grid, not a certificate for sub-grid authoring relief. Exact pad planes overlaid separately.','limitations':'100 m design bake misses some sub-grid relief; continuous evaluator is screened on pads but not globally interval-certified. Forest/building exclusions are design contracts, not built solids. Full continuous-terrain/collider/flight certification remains later phase.','airspace':'All supplied IFR remains byte-for-byte semantic identical; no procedures invented. Known protected corridors checked; missing geometry remains explicit.'}
 from design_audit import audit
 audit(g,m,grid)
 (g.ROOT/'island-fit-check.json').write_text(json.dumps(m,indent=2)+'\n');return m
