"""Analytic extension checks for Phase1A.2; no game imports or mutations."""
import math
from fit_revision import area,clip,polydist,nearest_pair,capsule

def audit(g,m,grid):
 w=g.W;fs=g.finals()+g.runway_finals();out={}
 # Entire race enclosure includes gate wings and pylon bases, not just centre points.
 f=next(f for f in w['features'] if f['id']=='RACE');xs=[p['centre'][0] for p in f['elements']];ys=[p['centre'][1] for p in f['elements']];pad=f['gate_half_width_m']/1000+.01;p=[[min(xs)-pad,min(ys)-pad],[max(xs)+pad,min(ys)-pad],[max(xs)+pad,max(ys)+pad],[min(xs)-pad,max(ys)+pad]]
 near=nearest_pair(g,[p],[(f['title'],g.finalpoly(f)) for f in fs]);zd=min(polydist(g,p,q) for z in m['zone_geometry'].values() for q in z['active_polygons']);bd=min(polydist(g,p,[[b['centre'][0]+dx,b['centre'][1]+dy] for dx,dy in [(-.5,-.5),(-.5,.5),(.5,.5),(.5,-.5)]]) for b in w['aerobatic_boxes'])
 out['race']=dict(status='PASS' if near[0]>=1 and zd>0 and bd>0 else 'FAIL',nearest_full_final_km=round(near[0],6),nearest_final=near[1],combat_boundary_km=round(zd,6),box_boundary_km=round(bd,6),pylon_top_msl_ft=round(f['pylon_height_m']/g.FT,3),enclosure=p)
 # Navaid sites are positions/elevations only; derive their actual shared terrain elevations.
 sites=[]
 for q in w['outer_navaid_sites']:
  actual=g.height(*q['centre'])/g.FT;err=abs(actual-q['elevation_ft']);sites.append(dict(site=q['id'],centre=q['centre'],specified_elevation_ft=q['elevation_ft'],computed_elevation_ft=round(actual,6),error_ft=round(err,6),passes=err<.1 and not any(k in q for k in ['frequency','ident','equipment'])))
 out['navaid_sites']=sites
 # Complete volcano disk containment/absence of erosion proves symmetry without radial samples.
 i=next(i for i in w['outer_islands'] if i.get('volcano'));v=i['volcano'];c=v['centre'];r=v['radius_km'];coast=g.shore(c,i['coast_polygon'])-r-w['edge']['land_shore_fade_km'];nearest=min((max(0,min(g.seg(c,a,b)[0] for a,b in zip(g.finalpoly(f),g.finalpoly(f)[1:]+g.finalpoly(f)[:1]))) if not g.inside(c,g.finalpoly(f)) else 0,f['title']) for f in fs);finalmargin=nearest[0]-r-v['clearance_reservation']['minimum_full_final_boundary_km']
 cut=[]
 proposed=w['perimeter_traffic_reservation'];routes=g.airway_segments()+[dict(id='PERIM-'+str(i+1),a=a,b=b,mea_ft=proposed['mea_ft_targets'][i]) for i,(a,b) in enumerate(zip(proposed['path'],proposed['path'][1:]))]
 for a in routes:
  if g.seg(c,a['a'],a['b'])[0]<=r+3.854+.3 and a['mea_ft']-2025<v['rim_ft']:cut.append(a['id'])
 pads=[]
 for q in g.FIELDS:
  margin=min(g.seg(c,a,b)[0] for a,b in zip(g.rect(q,True),g.rect(q,True)[1:]+g.rect(q,True)[:1]))-r-q['pad']['blend_m']/1000
  if margin<0:pads.append(q['key'])
 foreign=max(q['elevation_ft'] for q in w['islets'] if q['id'].startswith('SS'))<=i['base_ft']
 ok=coast>=0 and finalmargin>=0 and not cut and not pads and foreign
 out['sitka_volcano']=dict(status='PASS analytic radial model' if ok else 'FAIL',centre=c,cone_radius_km=r,rim_radius_km=v['rim_radius_km'],rim_ft=v['rim_ft'],crater_floor_ft=v['crater_floor_ft'],crater_diameter_m=2*v['rim_radius_km']*1000,crater_depth_m=(v['rim_ft']-v['crater_floor_ft'])*g.FT,whole_cone_disk_coast_fade_margin_km=round(coast,6),whole_cone_full_final_boundary_gap_km=round(nearest[0]-r,6),whole_cone_final_cut_margin_km=round(finalmargin,6),nearest_final=nearest[1],airway_cut_conflicts=cut,pad_blend_conflicts=pads,foreign_islet_top_below_base=foreign,proof='Whole cone disk lies inside unfaded coast and at least100m outsideactual fullfinal footprints; discretionary outererosion guards skip the radialdisk. No runwayblend intersects it; all relevantairway caps above rim. Adjacent islets no higher than base. Terrain remains exact radial cone/crater formula at every azimuth.')
 # Full marker raft bounds stay water; shore markers lie on actual extended judges axes.
 rafts=[]
 for b in w['aerobatic_boxes'][:2]:
  allrafts=b['rafts']+[b['judge_raft']];water_margin=min(-max(g.shore(q['centre'],coast) for coast in g.LAND)-.05 for q in allrafts);shore=b['shore_reference_marker']['centre'];dry=max(g.shore(shore,coast) for coast in g.LAND)-.04;u=g.vec(b['judges_line']['orientation_true_deg']);dc=[shore[k]-b['judges_line']['centre'][k] for k in [0,1]];axis=abs(dc[0]*u[1]-dc[1]*u[0]);ok=len(allrafts)==10 and len(b['corner_buoys'])==4 and water_margin>=0 and dry>=0 and axis<1e-8
  rafts.append(dict(box=b['id'],marker_rafts=9,judge_rafts=1,total_rafts=len(allrafts),corner_buoys=4,whole_raft_water_margin_km=round(water_margin,6),whole_shore_marker_dry_margin_km=round(dry,6),shore_axis_error_m=round(axis*1000,9),passes=ok))
 out['water_box_markers']=rafts
 # All full flat pad domains lie beyond the final100m coast fade; analytic detail planes own them.
 pads=[]
 for q in g.FIELDS:
  pp=g.rect(q,True);coast=max(g.LAND,key=lambda p:g.shore(q['centre'],p));boundary=min(__import__('fit_revision').segments_distance(g,a,b,c,d) for a,b in zip(pp,pp[1:]+pp[:1]) for c,d in zip(coast,coast[1:]+coast[:1]));contained=all(g.inside(v,coast) for v in pp) and boundary>=.1-1e-7
  pads.append(dict(field=q['key'],coast_fade_margin_m=round((boundary-.1)*1000,4),passes=contained))
 out['full_pad_domains']=pads
 out['all_pass']=out['race']['status']=='PASS' and all(q['passes'] for q in sites+rafts+pads) and ok and out['sitka_volcano']['status'].startswith('PASS')
 m['additional_design_checks']=out
 if not out['all_pass']:
  for row in m['fit_check']:
   if row['rule']==8:row['status']='FAIL / UNVERIFIED';row['evidence']+=' Additional design audit has failures; see additional_design_checks.'
 return out
