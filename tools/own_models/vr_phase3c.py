"""CPU-only Great Lakes P3c. Preserve all existing rig nodes/transforms/extras.
Photo-informed dimensions are estimates, not surveyed aircraft measurements.
"""
import numpy as np
from vr_cockpit import Glb, Author, Shape, builder, gl
from drop_card_slot import validate
CKP=[(-.97,-1.33,.19),(-1.64,-2.08,.215)]

def vertices(g,name):
 i=next(i for i,n in enumerate(g.doc['nodes']) if n['name']==name);n=g.doc['nodes'][i];m=g.world_matrices()[i]
 out=[]
 for p in g.doc['meshes'][n['mesh']]['primitives']:
  v=g.array(p['attributes']['POSITION']);v=np.array([builder(q) for q in v@m[:3,:3].T+m[:3,3]])
  ids=g.array(p['indices']).reshape(-1,3) if 'indices' in p else np.arange(len(v)).reshape(-1,3)
  out.extend(v[ids])
 return np.array(out)

def ray_skin(tris,x,y,upper=True):
 a=tris[:,0];u=tris[:,1]-a;v=tris[:,2]-a;det=u[:,0]*v[:,1]-u[:,1]*v[:,0]
 good=abs(det)>1e-12;d=np.array([x,y])-a[:,:2]
 s=np.divide(d[:,0]*v[:,1]-d[:,1]*v[:,0],det,out=np.zeros(len(a)),where=good)
 t=np.divide(u[:,0]*d[:,1]-u[:,1]*d[:,0],det,out=np.zeros(len(a)),where=good)
 hit=good&(s>=-1e-7)&(t>=-1e-7)&(s+t<=1+1e-7)
 z=(a+s[:,None]*u+t[:,None]*v)[hit,2];assert len(z),(x,y)
 return float(max(z) if upper else min(z))

def distance(p,tris):
 """Exact point-to-triangle distance, including edges and face projections."""
 p=np.asarray(p);a,b,c=tris.transpose(1,0,2);u=b-a;v=c-a;n=np.cross(u,v);nn=(n*n).sum(1)
 d=p-a;proj=p-n*np.divide((d*n).sum(1),nn,out=np.zeros(len(n)),where=nn>1e-20)[:,None]
 w=proj-a;uu=(u*u).sum(1);uv=(u*v).sum(1);vv=(v*v).sum(1);den=uu*vv-uv*uv
 s=np.divide((w*u).sum(1)*vv-(w*v).sum(1)*uv,den,out=np.zeros(len(n)),where=abs(den)>1e-20)
 t=np.divide((w*v).sum(1)*uu-(w*u).sum(1)*uv,den,out=np.zeros(len(n)),where=abs(den)>1e-20)
 ds=np.where((s>=0)&(t>=0)&(s+t<=1)&(nn>1e-20),np.linalg.norm(p-proj,axis=1),np.inf)
 for q,r in ((a,b),(b,c),(c,a)):
  e=r-q;ee=(e*e).sum(1);f=np.clip(np.divide(((p-q)*e).sum(1),ee,out=np.zeros(len(e)),where=ee>0),0,1)
  ds=np.minimum(ds,np.linalg.norm(p-q-f[:,None]*e,axis=1))
 return float(ds.min())

def update(path):
 g=Glb(path)
 if g.doc.get('extras',{}).get('vrCockpitPhase3c'):return
 a=Author(g);skin=vertices(g,'Fuselage_Painted');legacy=path.name!='great-lakes-like-own.glb';cockpits=[(-.95,-1.28,.315),(-1.48,-2.03,.315)] if legacy else CKP;oldpadding=vertices(g,'Windscreen_Frame');meta=dict(cabane=[],gear=[],screens=[],new_nodes=[],cockpits=cockpits)
 metal=g.material('GL3c_Fitting',(.18,.20,.22));steel=g.material('GL3c_Bolts',(.55,.57,.59));black=g.material('GL3c_Coaming',(.025,.021,.018))
 old=next(n for n in g.doc['nodes'] if n['name']=='Strut_Cabane');strutmat=g.doc['meshes'][old['mesh']]['primitives'][0]['material'];old.pop('mesh')
 # Remove the frame NODE without changing node indices (animation/skin references).
 n=next(n for n in g.doc['nodes'] if n['name']=='Windscreen_Frame');n['name']='Cockpit_Coaming_Padding';n.pop('mesh')
 n=next(n for n in g.doc['nodes'] if n['name']=='Windscreen_Glass');n.pop('mesh')
 def node(name,shape,mat,parent=None,p=(0,0,0)):
  a.node(name,p,parent,shape,mat);meta['new_nodes'].append(name)
 def fitting(name,x,y,upper=True):
  z=ray_skin(skin,x,y,upper);sg=1 if upper else -1
  # Conforming pad follows the ACTUAL triangulated painted skin, no analytic loft discrepancy.
  sh=Shape();vs=[];grid=7
  for dz in (0,.003):
   for dx in np.linspace(-.027,.027,grid):
    for dy in np.linspace(-.019,.019,grid):
     vs.append((x+dx,y+dy,ray_skin(skin,x+dx,y+dy,upper)+sg*dz))
  faces=[];layer=grid*grid
  for i in range(grid-1):
   for j in range(grid-1):
    k=i*grid+j;faces.extend([(k,k+grid+1,k+1),(k,k+grid,k+grid+1)])
  basefaces=len(faces)
  faces.extend(tuple(k+layer for k in f[::-1]) for f in faces[:])
  boundary=list(range(grid))+[i*grid+grid-1 for i in range(1,grid)]+list(range(layer-2,layer-grid-1,-1))+[i*grid for i in range(grid-2,0,-1)]
  for i,j in zip(boundary,boundary[1:]+boundary[:1]):faces.extend([(i,j,j+layer),(i,j+layer,i+layer)])
  sh.add(vs,faces)
  end=np.array((x,y,z+sg*.014))
  for dy in (-.012,.012):sh.box(end+(0,dy,-sg*.003),(.034,.004,.022))
  node(name,sh,metal)
  g.doc['nodes'][a.idx(name)]['extras']={'skin_pad_triangle_count':basefaces}
  bolts=Shape();bolts.tube(end+(0,-.022,0),end+(0,.022,0),.004,10)
  for dx in (-.019,.019):
   zz=ray_skin(skin,x+dx,y,upper);bolts.tube((x+dx,y,zz),(x+dx,y,zz+sg*.006),.0035,8)
  node(name+'_Bolts',bolts,steel)
  return end
 for side,sy in [('L',1),('R',-1)]:
  ft,rt=np.array((-.15,sy*.70,.62)),np.array((-.80,sy*.70,.62))
  fb=fitting('Cabane_Fitting_Fwd_'+side,-.12,sy*.33)
  rb=fitting('Cabane_Fitting_Aft_'+side,-.80,sy*.33)
  for label,oldend,end,top,fit in [('Fwd',(-.12,sy*.33,.20),fb,ft,'Fwd'),('Aft',(-.80,sy*.33,.17),rb,rt,'Aft'),('Diagonal',(-.80,sy*.33,.17),rb,ft,'Aft')]:
   name='Cabane_Leg_'+label+'_'+side;sh=Shape();sh.tube(end,top,.012,16);node(name,sh,strutmat)
   meta['cabane'].append(dict(node=name,fitting='Cabane_Fitting_'+fit+'_'+side,bottom=end.tolist(),top=top.tolist(),before_endpoint_skin_gap_mm=distance(oldend,skin)*1000,after_endpoint_skin_gap_mm=distance(end,skin)*1000))
  # Fixed V from the existing oleo top pin to two lower longeron skin fittings.
  w=g.world_matrices();top=builder(w[a.idx('Gear_OleoTop_'+side)][:3,3])
  for label,x,y in [('Fwd',.10,.33),('Aft',-.70,.33)]:
   fit='Gear_Fitting_'+label+'_'+side;end=fitting(fit,x,sy*y,False)
   sh=Shape();sh.tube(end,top,.0175,16);name='Gear_Support_'+label+'_'+side;node(name,sh,strutmat)
   meta['gear'].append(dict(node=name,fitting=fit,bottom=end.tolist(),top=top.tolist(),top_node='Gear_OleoTop_'+side))
  # Keep the centreline U-joint pivot fixed; bridge its existing skin stand-off.
  pivot=builder(g.world_matrices()[a.idx('Gear_Pivot_'+side)][:3,3]);fit='Gear_Fitting_Pivot_'+side
  end=fitting(fit,float(pivot[0]),float(pivot[1]),False);sh=Shape();sh.tube(end,pivot,.014,12);name='Gear_Pivot_Sleeve_'+side;node(name,sh,metal)
  meta['gear'].append(dict(node=name,fitting=fit,bottom=end.tolist(),top=pivot.tolist(),top_node='Gear_Pivot_'+side))
  # Existing moving compression-strut fitting: sleeve joins C to the real lower skin.
  c=builder(g.world_matrices()[a.idx('Gear_HingeAxis_'+side)][:3,3]);fit='Gear_Fitting_Compression_'+side
  end=fitting(fit,float(c[0]),sy*.33,False);sh=Shape();sh.tube(end,c,.014,12);name='Gear_Compression_Sleeve_'+side;node(name,sh,metal)
  meta['gear'].append(dict(node=name,fitting=fit,bottom=end.tolist(),top=c.tolist(),top_node='Gear_HingeAxis_'+side))
 # Preserve only the existing low coaming padding/side rails; discard every screen edge.
 sh=Shape();padding=oldpadding[(oldpadding[:,:,2].max(1)<.25)&(oldpadding[:,:,2].min(1)>.10)]
 # v1 screen side-post bottoms are disconnected from the padded rings: filter by
 # full component in the source, instead of clipping individual triangles.
 u,ids=np.unique(np.round(oldpadding.reshape(-1,3),6),axis=0,return_inverse=True);adj=[set() for _ in u]
 for f in ids.reshape(-1,3):
  for i in f:adj[i].update(f)
 remaining=set(range(len(u)));keep=set()
 while remaining:
  seed=next(iter(remaining));seen={seed};todo=[seed]
  while todo:
   for i in adj[todo.pop()]-seen:seen.add(i);todo.append(i)
  remaining-=seen
  if u[list(seen),2].max()<.25:keep|=seen
 mask=np.array([all(i in keep for i in f) for f in ids.reshape(-1,3)])
 sh.v=oldpadding[mask].reshape(-1,3).tolist();sh.t=np.arange(len(sh.v)).reshape(-1,3).tolist()
 node('GL3c_Coaming_Padding',sh,black)
 glass=g.material('GL3c_Clear_Acrylic',(.91,.97,1));m=g.doc['materials'][glass];m.update(alphaMode='BLEND',doubleSided=True);m['pbrMetallicRoughness'].update(baseColorFactor=[.91,.97,1,.12],roughnessFactor=.09)
 # Alpha fallback remains clear on Unity shaders without transmission support.
 m['extensions']={'KHR_materials_transmission':{'transmissionFactor':.96},'KHR_materials_ior':{'ior':1.49}}
 for ext in m['extensions']:
  if ext not in g.doc.setdefault('extensionsUsed',[]):g.doc['extensionsUsed'].append(ext)
 for seat,ck,h,crown in [('Front',cockpits[0],.125,.008),('Rear',cockpits[1],.175,.030)]:
  xf,xa,hw=ck;rows=[]
  for q in np.linspace(0,1,13):
   row=[]
   for t in np.linspace(-np.pi/2,np.pi/2,65):
    # Forward half ellipse outside the aperture; continuous curved sheet, no bend seams.
    y=(hw+.025)*np.sin(t);x=(xf+xa)/2+((xf-xa)/2+.025)*np.cos(t)
    if legacy:x=xf+.025-.10*max(0,(abs(y)-hw)/.025)**2
    z=ray_skin(skin,float(x),float(y),True)+.004
    topz=ray_skin(skin,xf+.025,0,True)+h-crown*np.sin(t)**2
    row.append((x-q*.015*np.cos(t),y,z*(1-q)+topz*q))
   rows.append(row)
  sh=Shape();sh.v=[p for row in rows for p in row]
  for j in range(12):
   for i in range(64):
    k=j*65+i;sh.t.extend([(k,k+1,k+66),(k,k+66,k+65)])
  name='Windscreen_'+seat;node(name,sh,glass)
  clips=Shape()
  for i in (3,16,32,48,61):
   p=np.array(rows[0][i]);clips.tube(p,p+(0,0,.006),.003,8)
  node('Windscreen_Base_Screws_'+seat,clips,steel)
  meta['screens'].append(dict(node=name,rows=13,columns=65,centre_height_above_skin_m=h,top_crown_m=crown,top_height_m=rows[-1][32][2],top_edge_drop_m=rows[-1][32][2]-rows[-1][0][2]))
 g.doc.setdefault('extras',{})['vrCockpitPhase3c']=meta
 validate(g,path);g.save(path)
 print('P3c',path.name)
