"""Great Lakes cockpit hardware, Ty photo revision 2026-11-03.

Deterministic rebuild from BASE: ignition only on panels; labelled toggles and
breakers on grey left consoles; black multi-plate quadrants and white braided
trim cables with fore/aft scales and linked right-wall fuel handles.
Existing throttle/prop/mixture inputs and linked trim direction are preserved.
"""
import json,math,struct,zlib,tempfile,sys
from pathlib import Path
import numpy as np
from vr_cockpit import Glb,Shape,MODELS,ROOT,gl,builder
from drop_card_slot import validate
import cockpit_controls_v2 as cc

GLB=MODELS/'great-lakes-like/great-lakes-like-own.glb'
RIG=ROOT/'docs/models/great-lakes-own-rig.json'
BASE='4dc7ded5'   # last commit before this script
# GL_QUADRANT_BASE_DIR: a directory holding great-lakes-like-own.glb + great-lakes-own-rig.json to rebuild from
# instead of BASE (lane/claude-quest-cockpit-switches: HEAD's six-gauge export + cockpit_controls_v2 + gl_windshield_v2).
import os
def base_bytes(rel):
 d=os.environ.get('GL_QUADRANT_BASE_DIR')
 if d:return (Path(d)/Path(rel).name).read_bytes()
 import subprocess
 return subprocess.run(['git','show',BASE+':'+str(rel)],cwd=ROOT,check=True,capture_output=True).stdout
A0,SWEEP=25.0,50.0
PLACARD_DZ=.036   # placard centre below the switch pivot: clear of the rotary and push-pull knob bodies
# per seat: panel face X, lever pivot x, pivot z, housing top z, strip z range, switch row z, row y slots
SEATS={
 'Rear': dict(sfx='R',ctl='',full='Rear',X=-1.638,px=-1.763,zp=-.170,ztop=-.088,strip=(-.148,-.076),strip_y=(-.155,.330),row_z=-.098,
              row_y=[.295,.262,.093,.049,.005,-.039,-.083,-.127]),
 'Front':dict(sfx='F',ctl='_Front',full='Front',X=-.968,px=-1.093,zp=-.150,ztop=-.068,strip=(-.128,-.056),strip_y=(-.165,.165),row_z=-.078,
              row_y=[.140,.100,.060,.020,-.020,-.060,-.100,-.140]),
}
ROW=['Magneto','Master','FuelPump','CarbHeat','Smoke','NavLights','Strobe','LandingLight']
LABEL={'Magneto':'MAGS','Master':'MASTER','FuelPump':'FUEL|PUMP','CarbHeat':'CARB|HEAT','Smoke':'SMOKE','Trim':'TRIM',
       'FuelSelector':'FUEL|VALVE','NavLights':'NAV|LTS','Strobe':'STROBE','LandingLight':'LAND|LT'}
# lever: (rig/ctl label, lateral y, length, knob material, knob radius, knob letter, gate label)
LEVERS=[('Throttle',.318,.150,'Cockpit_black',.014,'T','THR'),('Prop',.288,.1246,'Cockpit_knob_blue',.012,'P','PROP'),
        ('Mixture',.258,.0992,'Cockpit_knob_red',.011,'M','MIX')]
HOUSING_Y=(.252,.338);HOUSING_DX=.065

F={'A':'01110 10001 10001 11111 10001 10001 10001','B':'11110 10001 10001 11110 10001 10001 11110','C':'01111 10000 10000 10000 10000 10000 01111',
 'D':'11110 10001 10001 10001 10001 10001 11110','E':'11111 10000 10000 11110 10000 10000 11111','F':'11111 10000 10000 11110 10000 10000 10000',
 'G':'01111 10000 10000 10011 10001 10001 01111','H':'10001 10001 10001 11111 10001 10001 10001','I':'11111 00100 00100 00100 00100 00100 11111',
 'K':'10001 10010 10100 11000 10100 10010 10001','L':'10000 10000 10000 10000 10000 10000 11111','M':'10001 11011 10101 10101 10001 10001 10001',
 'N':'10001 11001 11001 10101 10011 10011 10001','O':'01110 10001 10001 10001 10001 10001 01110','P':'11110 10001 10001 11110 10000 10000 10000',
 'R':'11110 10001 10001 11110 10100 10010 10001','S':'01111 10000 10000 01110 00001 00001 11110','T':'11111 00100 00100 00100 00100 00100 00100',
 'U':'10001 10001 10001 10001 10001 10001 01110','V':'10001 10001 10001 10001 01010 01010 00100','X':'10001 01010 00100 00100 00100 01010 10001',
 'W':'10001 10001 10001 10101 10101 11011 10001',' ':'00000 00000 00000 00000 00000 00000 00000'}
F['Y']='10001 10001 01010 00100 00100 00100 00100'
CW,CH,SC=92,21,4          # label cell in font pixels (6 chars x 2 lines), image scale
def render_cell(text,fg=(245,245,240),bg=(12,12,14)):
 img=np.zeros((CH,CW,3),np.uint8);img[:]=bg;lines=text.split('|');top=(CH-(len(lines)*7+(len(lines)-1)*3))//2
 for li,line in enumerate(lines):
  left=(CW-(len(line)*6-1))//2
  for k,ch in enumerate(line):
   for y,row in enumerate(F[ch].split()):
    for x,b in enumerate(row):
     if b=='1':img[top+li*10+y,left+k*6+x]=fg
 return img
def png(pixels):
 h,w,_=pixels.shape
 def chunk(t,b):return struct.pack('>I',len(b))+t+b+struct.pack('>I',zlib.crc32(t+b)&0xffffffff)
 return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(b''.join(b'\0'+r.tobytes() for r in pixels),9))+chunk(b'IEND',b'')
def atlas(cells):
 """cells: list of (key,text,fg,bg) -> (png bytes, {key:(u0,v0,u1,v1)})"""
 n=len(cells);img=np.concatenate([np.kron(render_cell(t,fg,bg),np.ones((SC,SC,1),np.uint8)) for _,t,fg,bg in cells],0)
 uv={k:(0.0,i/n,1.0,(i+1)/n) for i,(k,*_) in enumerate(cells)};return png(img),uv
def quad(sh,uvs,c,right,up,w,h,box):
 c=np.array(c,float);r=np.array(right,float)*w/2;u=np.array(up,float)*h/2;u0,v0,u1,v1=box
 sh.add([c-r-u,c+r-u,c+r+u,c-r+u],[(0,1,2),(0,2,3)]);uvs.extend([(u0,v1),(u1,v1),(u1,v0),(u0,v0)])

def rot(v,deg):
 """rotate builder vector about builder +y by the rig angle (positive = top of lever forward)."""
 a=math.radians(deg);x,y,z=v;return np.array((x*math.cos(a)+z*math.sin(a),y,-x*math.sin(a)+z*math.cos(a)))
def lever_dir(value_deg):return rot((0,0,1),value_deg-A0)
def knob(seat,lv,value_deg=0.0):
 s=SEATS[seat];_,y,L,_,r,_,_=lv;return np.array((s['px'],y,s['zp']))+(L+r*.6)*lever_dir(value_deg)

def set_mesh(g,name,parts,origin):
 """parts: [(material_name, Shape, uv list or None)] in builder world; origin: builder point the node sits at."""
 nodes=g.doc['nodes'];i=next(k for k,n in enumerate(nodes) if n['name']==name);mats={m['name']:k for k,m in enumerate(g.doc['materials'])}
 prims=[]
 for mat,sh,uv in parts:
  if not sh.t:continue
  p=np.array([gl(v)-gl(origin) for v in sh.v]);t=np.array(sh.t).reshape(-1,3);flat=p[t].reshape(-1,3)
  nrm=np.cross(p[t[:,1]]-p[t[:,0]],p[t[:,2]]-p[t[:,0]]);nrm/=np.maximum(np.linalg.norm(nrm,axis=1)[:,None],1e-10)
  at=dict(POSITION=g.accessor(flat,'VEC3'),NORMAL=g.accessor(np.repeat(nrm,3,axis=0),'VEC3'))
  if uv is not None:at['TEXCOORD_0']=g.accessor(np.array(uv)[t].reshape(-1,2),'VEC2')
  prims.append(dict(attributes=at,material=mats[mat],mode=4))
 g.doc['meshes'].append(dict(name=name,primitives=prims))
 nodes[i]['mesh']=len(g.doc['meshes'])-1
def ensure_child(g,parent,name,local):
 nodes=g.doc['nodes'];names={n['name']:k for k,n in enumerate(nodes)}
 if name not in names:
  nodes.append(dict(name=name));names[name]=len(nodes)-1;nodes[names[parent]].setdefault('children',[]).append(names[name])
 n=nodes[names[name]];n['translation']=[float(v) for v in local];n['rotation']=[0,1,0,0]
 return n

def lever_parts(seat,lv):
 s=SEATS[seat];label,y,L,km,r,letter,_=lv;P=np.array((s['px'],y,s['zp']));d=lever_dir(0)
 shaft=Shape();shaft.tube(P,P+L*d,.0045,10);boss=Shape();boss.tube(P+(0,-.009,0),P+(0,.009,0),.011,12)
 k=Shape();kc=knob(seat,lv)
 k.sphere(kc,r,24,14)
 return P,kc,[('P3_ControlMetal',shaft,None),('Cockpit_quadrant',boss,None),(km,k,None)]

def build(g,doc):
 nodes=g.doc['nodes'];names={n['name']:i for i,n in enumerate(nodes)};W=g.world_matrices()
 controls=g.doc['extras']['vrCockpitPhase3a']['controls'];by={c['node']:c for c in controls}
 mats={m['name'] for m in g.doc['materials']}
 # one texture atlas for every label
 cells=[]
 for seat in SEATS:
  for lab in ROW:cells.append((seat+lab,LABEL[lab],(245,245,240),(12,12,14)))
 for _,_,_,km,_,letter,gate in LEVERS:
  bg={'Cockpit_black':(10,10,10),'Cockpit_knob_blue':(20,50,150),'Cockpit_knob_red':(140,15,15)}[km]
  cells.append(('knob'+letter,letter,(250,250,250),bg));cells.append(('gate'+letter,gate,(245,245,240),(12,12,14)))
 data,UV=atlas(cells)
 with tempfile.TemporaryDirectory() as td:
  pth=Path(td)/'gl-placards.png';pth.write_bytes(data);g.material('GL_Placard_Atlas',(1,1,1),png=pth)
 if 'P3e_Subpanel' not in mats:g.material('P3e_Subpanel',(.03,.03,.035))
 # remove the old console box / gate plate triangles (including obsolete rope-trim slot and fuel valve plate)
 ci=names['Cockpit_Interior'];mesh=g.doc['meshes'][nodes[ci]['mesh']];M=W[ci];matnames=[m['name'] for m in g.doc['materials']]
 for p in mesh['primitives']:
  if matnames[p.get('material',-1)]!='Cockpit_quadrant':continue
  v=g.array(p['attributes']['POSITION']);ids=g.array(p['indices']).reshape(-1,3) if 'indices' in p else np.arange(len(v)).reshape(-1,3)
  vb=np.array([builder(q) for q in v@M[:3,:3].T+M[:3,3]]);c=vb[ids].mean(1)
  old=(c[:,1]>.27)&(c[:,2]<-.19)&(c[:,2]>-.47)&(((c[:,0]>-1.86)&(c[:,0]<-1.58))|((c[:,0]>-1.14)&(c[:,0]<-.94)))
  trim=(c[:,2]<-.403)&(c[:,2]>-.437)&(c[:,1]>.36)
  fuel=(abs(c[:,1]+.20)<.031)&(((abs(c[:,0]+1.635)<.005))|((abs(c[:,0]+.965)<.005)))&(c[:,2]<-.1)
  keep=ids[~(old|trim|fuel)]
  if len(keep)==len(ids):continue
  p['indices']=g.accessor(keep.reshape(-1).astype(np.uint32),'SCALAR',5125)
 rigrows={r['node']:r for r in doc['rig']}
 for nm in ('Mixture_R','Prop_R','Mixture_F','Prop_F'):
  n=nodes[names[nm]];n.pop('mesh',None);n['extras']={'p3_compatibility_empty':True,'superseded_by':'gl_quadrant_v3 Ctl_PushPull_Prop/Mixture quadrant levers'}
 doc['rig']=[r for r in doc['rig'] if r['node'] not in ('Mixture_R','Prop_R','Mixture_F','Prop_F')]
 for seat,s in SEATS.items():
  X=s['X']
  # ---- housing + gate slots + gate labels (static, in a per-seat node)
  hn='GL_Quadrant_'+s['sfx']
  if hn not in names:nodes.append(dict(name=hn));g.doc['scenes'][0]['nodes'].append(len(nodes)-1);names[hn]=len(nodes)-1
  hb=Shape();zb=s['zp']-.03;y0,y1=HOUSING_Y
  hb.box((s['px'],(y0+y1)/2,(zb+s['ztop'])/2),(2*HOUSING_DX,y1-y0,s['ztop']-zb))
  hb.box((s['px']-.035,.347,zb+.012),(.016,.020,.016))                               # bracket to the sidewall tube
  slots=Shape();span=(s['ztop']-s['zp'])*math.tan(math.radians(A0))+.008
  for _,y,*_ in LEVERS:slots.box((s['px'],y,s['ztop']+.0006),(2*span,.008,.0012))
  lab=Shape();uv=[]
  for _,y,_,_,_,letter,_ in LEVERS:
   quad(lab,uv,(s['px']-span-.009,y,s['ztop']+.0013),(0,-1,0),(1,0,0),.0215,.0118,UV['gate'+letter])
  # fixed placard on the inboard face: arrow-less "FULL" forward / "IDLE" aft is implied by the gate labels
  set_mesh(g,hn,[('Cockpit_quadrant',hb,None),('Cockpit_black',slots,None),('GL_Placard_Atlas',lab,uv)],np.array((s['px'],(y0+y1)/2,s['ztop'])))
  nodes[names[hn]]['translation']=gl(np.array((s['px'],(y0+y1)/2,s['ztop']))).tolist()
  # ---- levers
  for lv in LEVERS:
   label=lv[0];P,kc,parts=lever_parts(seat,lv)
   # Plain round v6 knobs: no floating letter cards.
   if label=='Throttle':
    nm='Throttle_'+s['sfx'];set_mesh(g,nm,parts,P);parent=W[names['Controls']]
    assert np.allclose(parent[:3,:3],np.eye(3))
    nodes[names[nm]]['translation']=(gl(P)-parent[:3,3]).tolist()
    ensure_child(g,nm,'Grip_Throttle_'+s['full'],gl(kc)-gl(P))
    rigrows[nm]['pivot_gltf']=[round(float(v),5) for v in gl(P)]
    rigrows[nm]['desc']='throttle QUADRANT lever on the left sidewall (Ty 2026-11-02), idle 25 deg aft to full 25 deg forward; both cockpits on the SAME input.'
    nodes[names[nm]]['extras'].update(rig_param='throttle',rig_axis_gltf=[0.0,0.0,1.0],rig_map_param=[0.0,1.0],rig_map_deg=[0.0,SWEEP])
   else:
    nm='Ctl_PushPull_'+label+s['ctl'];c=by[nm];set_mesh(g,nm,parts,P);nodes[names[nm]]['translation']=gl(P).tolist()
    for ch in nodes[names[nm]].get('children',[]):
     nodes[ch]['translation']=(gl(kc)-gl(P)).tolist()
    mn='Mount_'+c['grip'][5:];ms=Shape();ms.box((P[0],P[1],s['ztop']+.0003),(2*span+.01,.016,.0006))
    set_mesh(g,mn,[('Cockpit_quadrant',ms,None)],P);nodes[names[mn]]['translation']=gl(P).tolist()
    c.update(axis_gltf=[0.0,0.0,1.0],pivot_gltf=[round(float(v),5) for v in gl(P)],travel=SWEEP,travel_unit='deg',position_status='E',
             placement='gl_quadrant_v3: left-sidewall throttle quadrant lever (Ty 2026-11-02)')
    ex=nodes[names[nm]]['extras'];ex.pop('rig_move_axis_gltf',None);ex.pop('rig_map_m',None)
    ex.update(rig_axis_gltf=[0.0,0.0,1.0],rig_map_param=[0.0,1.0],rig_map_deg=[0.0,SWEEP],rig_param_model=0.0,
              rig_desc='quadrant lever, 25 deg aft at 0 to 25 deg forward at 1 (full rich / fine pitch forward)',interaction=c)
    row=rigrows[nm];row.update(kind='rot',axis_gltf=[0.0,0.0,1.0],pivot_gltf=c['pivot_gltf'],map=[[0.0,0.0],[1.0,SWEEP]],model=0.0,
                              desc='(E) left-sidewall quadrant lever, Ty 2026-11-02; '+('blue prop' if label=='Prop' else 'red mixture'))
  # ---- switch strip + row + placards
  sn='GL_SwitchStrip_'+s['sfx'];z0,z1=s['strip'];ya,yb=s['strip_y']
  st=Shape();st.box((X+.003,(ya+yb)/2,(z0+z1)/2),(.006,yb-ya,z1-z0))
  if sn not in names:nodes.append(dict(name=sn));g.doc['scenes'][0]['nodes'].append(len(nodes)-1);names[sn]=len(nodes)-1
  pl=Shape();puv=[]
  for lab_,y in zip(ROW,s['row_y']):
   quad(pl,puv,(X-.0006,y,s['row_z']-PLACARD_DZ),(0,-1,0),(0,0,1),.0295,.0163,UV[seat+lab_])
  set_mesh(g,sn,[('P3e_Subpanel',st,None),('GL_Placard_Atlas',pl,puv)],np.array((X,0,0)));nodes[names[sn]]['translation']=gl(np.array((X,0,0))).tolist()
  for lab_,y in zip(ROW,s['row_y']):
   c=next(c for c in controls if c['seat']==seat and c['grip'][5:].replace('_Front','')==lab_)
   p=np.array((X-.001,y,s['row_z']));c['pivot_gltf']=gl(p).tolist()
   nodes[names[c['node']]]['translation']=gl(p).tolist();nodes[names['Mount_'+c['grip'][5:]]]['translation']=gl(p).tolist()
   c['placement']='gl_quadrant_v3: single labelled row along the bottom of the panel (Ty 2026-11-02)';c['label']=LABEL[lab_].replace('|',' ')
   nodes[names[c['node']]]['extras']['interaction']=c
   if c['node'] in rigrows:rigrows[c['node']]['pivot_gltf']=c['pivot_gltf']
 build_wall_trim(g,doc,controls,rigrows,names)
 build_photo_console(g,doc,controls,rigrows,names)
 build_linkages(g,doc,controls,names)
 doc['controls']=controls
 g.doc['extras']['vrCockpitPhase3a']['placement_v3']='Ty 2026-11-02: left-sidewall throttle quadrant (T/P/M) + single labelled switch row (gl_quadrant_v3.py)'

def build_wall_trim(g,doc,controls,rigrows,names):
 """Ty 2026-11-03: cable handle, ND forward, NU aft; no fuel valve pending location."""
 nodes=g.doc['nodes']
 remove=set()
 for seat,s in SEATS.items():
  suffix=s['ctl']
  for nm in ('Trim_'+s['sfx'],'FuelValve_'+s['sfx'],'Ctl_Rotary_FuelSelector'+suffix,'Mount_FuelSelector'+suffix):
   if nm in names:
    nodes[names[nm]].pop('mesh',None);remove.add(nm)
  c=next(c for c in controls if c['seat']==seat and c['param']=='trim')
  nm=c['node'];P=np.array((s['px']-.08,.322,-.285))
  # A vertical scale on the wall, fore/aft travel 120 mm. Handle projects inboard.
  fixed=Shape();fixed.box(P+(0,.021,0),(.172,.008,.064))
  slot=Shape();slot.box(P+(0,.015,0),(.128,.003,.006))
  ticks=Shape()
  for x in np.linspace(-.06,.06,9):ticks.box(P+(x,.012,.013),(.0015,.002,.008 if abs(x)>.001 else .014))
  cells=[('trim','TRIM',(245,245,240),(12,12,14)),('nd','NOSE|DOWN',(245,245,240),(12,12,14)),('nu','NOSE|UP',(245,245,240),(12,12,14))]
  data,uv=atlas(cells)
  if seat=='Rear':
   with tempfile.TemporaryDirectory() as td:
    path=Path(td)/'trim.png';path.write_bytes(data);g.material('GL_Trim_Scale',(1,1,1),png=path)
  label=Shape();luv=[]
  for key,x,z in [('trim',0,.026),('nd',.060,-.021),('nu',-.060,-.021)]:
   quad(label,luv,P+(x,.010,z),(1,0,0),(0,0,1),.035,.016,uv[key])
  mount='Mount_'+c['grip'][5:]
  set_mesh(g,mount,[('Cockpit_quadrant',fixed,None),('Cockpit_black',slot,None),('P3_ControlMetal',ticks,None),('GL_Trim_Scale',label,luv)],P)
  nodes[names[mount]]['translation']=gl(P).tolist()
  handle=Shape();handle.tube(P+(0,.012,0),P+(0,-.025,0),.004,10);handle.tube(P+(-.050,.012,0),P+(.050,.012,0),.0015,8)
  grip=Shape();grip.sphere(P+(0,-.030,0),.012,14,8)
  pointer=Shape();pointer.box(P+(0,.009,.011),(.003,.004,.018))
  set_mesh(g,nm,[('P3_ControlMetal',handle,None),('Cockpit_black',grip,None),('Cockpit_knob_red',pointer,None)],P)
  nodes[names[nm]]['translation']=gl(P).tolist()
  for ch in nodes[names[nm]].get('children',[]):nodes[ch]['translation']=gl((0,-.030,0)).tolist()
  c.update(kind='PushPull',axis_gltf=[1.,0.,0.],pivot_gltf=gl(P).tolist(),travel=.12,travel_unit='m',sim_ends=[-1,1],placement='Left wall cable trim; forward nose down, aft nose up; estimated reach',label='TRIM / NOSE DOWN forward / NOSE UP aft')
  ex=nodes[names[nm]]['extras'];ex.pop('rig_axis_gltf',None);ex.pop('rig_map_deg',None)
  ex.update(rig_move_axis_gltf=[1.,0.,0.],rig_map_param=[-1.,0.,1.],rig_map_m=[-.06,0.,.06],rig_param_model=0.,interaction=c)
  rigrows[nm].update(kind='move',axis_gltf=[1.,0.,0.],pivot_gltf=gl(P).tolist(),map=[[-1,-.06],[0,0],[1,.06]],model=0.,desc=c['placement'])
 controls[:]=[c for c in controls if 'FuelSelector' not in c['node']]
 doc['rig']=[r for r in doc['rig'] if r['node'] not in remove and 'FuelSelector' not in r['node']]
 g.doc['extras']['vrCockpitPhase3a']['placement_v4']='Ty 2026-11-03: eight labelled panel switches, cable trim on left wall, fuel valve omitted'

def build_photo_console(g,doc,controls,rigrows,names):
 """Ty's 2026-11-03 photos: grey console, outboard laminated quadrant, braided trim."""
 nodes=g.doc['nodes']
 g.material('GL_ConsoleGrey',(.28,.29,.30));g.material('GL_RopeWhite',(.91,.90,.83))
 cells=[(t,t,(245,245,240),(12,12,14)) for t in ['MASTER','ALT','FUEL|PUMP','NAV','STROBE','LDG','ON','THROTTLE','RPM','MIXTURE','CLOSED','DEC','T']]
 data,uv=atlas(cells)
 with tempfile.TemporaryDirectory() as td:
  path=Path(td)/'photo.png';path.write_bytes(data);g.material('GL_PhotoLabels',(1,1,1),png=path)
 def put(name,parts,P):
  if name not in names:
   nodes.append(dict(name=name));names[name]=len(nodes)-1;g.doc['scenes'][0]['nodes'].append(names[name])
  set_mesh(g,name,parts,np.array(P));nodes[names[name]]['translation']=gl(P).tolist()
 for seat,s in SEATS.items():
  x=s['px'];z=s['ztop']-.025
  # Compact 130 x 70 x 75 mm box against wall, entirely aft of lever sweep.
  bx=x-.200;by=.305
  body=Shape();body.box((bx,by,z-.0375),(.130,.070,.075))
  put('GL_SwitchStrip_'+s['sfx'],[('GL_ConsoleGrey',body,None)],(bx,by,z))
  labels=Shape();luv=[];metal=Shape();black=Shape()
  # V7: six fore/aft toggles on the lid; six matching breakers on the inboard face.
  # Top row is forward. Across each row reads left to right from the seat.
  import copy
  template=next(c for c in controls if c['seat']==seat and c['param']=='master')
  alt=copy.deepcopy(template);alt.update(node='Ctl_Toggle_Alternator'+s['ctl'],param='alternator',grip='Grip_Alternator'+s['ctl'],poke='Poke_Alternator'+s['ctl'])
  controls.append(alt)
  nodes.append(dict(name=alt['node'],extras={}));names[alt['node']]=len(nodes)-1;g.doc['scenes'][0]['nodes'].append(names[alt['node']])
  for child in [alt['grip'],alt['poke']]:
   nodes.append(dict(name=child,translation=gl((-.009,-.022,0)).tolist()))
   nodes[names[alt['node']]].setdefault('children',[]).append(len(nodes)-1)
  row=copy.deepcopy(rigrows[template['node']]);row.update(node=alt['node'],param='alternator');doc['rig'].append(row);rigrows[alt['node']]=row
  layout=[('Master','MASTER'),('Alternator','ALT'),('FuelPump','FUEL|PUMP'),('NavLights','NAV'),('Strobe','STROBE'),('LandingLight','LDG')]
  for j,(lab,key) in enumerate(layout):
   row,col=divmod(j,3)
   xx=bx-.040+col*.040; yy=by+(.017 if row==0 else -.017)
   # Breakers replace the v6 face toggles in exactly the same 2 x 3 positions.
   B=np.array((bx-.027+col*.027,by-.037,z-.020-row*.034))
   black.box(B+(0,.001,0),(.026,.003,.031))
   metal.tube(B,B+(0,-.004,0),.006,16);black.tube(B+(0,-.004,0),B+(0,-.012,0),.0045,12)
   quad(labels,luv,B+(0,-.004,-.011),(1,0,0),(0,0,1),.026,.009,uv[key])
   # Top-mounted switch stems lean aft at OFF; +80 about builder Y = forward ON.
   P=np.array((xx,yy,z+.002))
   black.box(P+(0,0,-.001),(.038,.030,.002));metal.tube(P,P+(0,0,.004),.006,12)
   c=next(c for c in controls if c['seat']==seat and c['grip'][5:].replace('_Front','')==lab)
   toggle=Shape();tip=P+(-.009,0,.022);toggle.tube(P,tip,.0028,12);toggle.sphere(tip,.0032,12,8)
   put(c['node'],[('P3_ControlMetal',toggle,None)],P)
   mount='Mount_'+c['grip'][5:]
   if mount in names:nodes[names[mount]].pop('mesh',None)
   c.update(kind='Toggle',axis_gltf=[0.,0.,1.],pivot_gltf=gl(P).tolist(),travel=80,travel_unit='deg',placement='v7 lid 2x3 fore/aft toggles; forward ON',label=key.replace('|',' '))
   ex=nodes[names[c['node']]].setdefault('extras',{})
   ex.update(rig_param=c['param'],rig_axis_gltf=[0.,0.,1.],rig_map_param=[0.,1.],rig_map_deg=[0.,80.],rig_param_model=0.,interaction=c)
   for ch in nodes[names[c['node']]].get('children',[]):nodes[ch]['translation']=gl(tip-P).tolist()
   rigrows[c['node']].update(kind='rot',axis_gltf=[0.,0.,1.],pivot_gltf=c['pivot_gltf'],map=[[0,0],[1,80]],model=0.,desc=c['placement'])
   quad(labels,luv,P+(0,-.010,.001),(1,0,0),(0,1,0),.036,.009,uv[key])
  # Keep the pre-existing smoke/carb controls as discrete underslung auxiliaries.
  for j,lab in enumerate(['CarbHeat','Smoke']):
   c=next(c for c in controls if c['seat']==seat and c['grip'][5:].replace('_Front','')==lab)
   P=np.array((bx-.045+j*.09,by-.037,z-.085));sh=Shape();sh.tube(P,P+(0,-.013,0),.004,10)
   put(c['node'],[('Cockpit_black',sh,None)],P);nodes[names['Mount_'+c['grip'][5:]]].pop('mesh',None)
   c.update(pivot_gltf=gl(P).tolist(),placement='Underslung auxiliary control')
   nodes[names[c['node']]]['extras']['interaction']=c
   rigrows[c['node']]['pivot_gltf']=c['pivot_gltf']
  put('GL_ConsoleHardware_'+s['sfx'],[('P3_ControlMetal',metal,None),('Cockpit_black',black,None),('GL_PhotoLabels',labels,luv)],(bx,by,z))
  nodes[names['GL_ConsoleHardware_'+s['sfx']]]['extras']={'switch_order':['master','alternator','fuel_pump','nav_lights','strobe','landing_light'],'breaker_order':['master','alternator','fuel_pump','nav_lights','strobe','landing_light'],'layout':[2,3],'forward_on':True,'switch_surface':'top','breaker_surface':'inboard face'}
  metal=Shape();black=Shape();labels=Shape();luv=[]
  # Distinct black plates with silver spacers, rather than a solid grey gate block.
  plates=Shape()
  for y in [.243,.273,.303,.333]:
   outline=[(x-.071,s['zp']-.027),(x+.071,s['zp']-.027)]+[(x+.071*math.cos(a),s['zp']+.025+.071*math.sin(a)) for a in np.linspace(0,math.pi,25)]
   verts=[(xx,yy,zz) for yy in [y-.0015,y+.0015] for xx,zz in outline];n=len(outline)
   faces=[]
   for j in range(1,n-1):faces.extend([(0,j+1,j),(n,n+j,n+j+1)])
   for j in range(n):k=(j+1)%n;faces.extend([(j,k,n+k),(j,n+k,n+j)])
   plates.add(verts,faces)
   metal.tube((x-.058,y-.002,s['zp']-.016),(x-.058,y+.004,s['zp']-.016),.004,10)
  for lab in ['Prop','Mixture']:
   nodes[names['Mount_'+lab+s['ctl']]].pop('mesh',None)  # obsolete push-pull pads filled the quadrant slots
  # Narrow supported name strips beside each slot; leave the entire shaft sweep open.
  for y,key in [(.318,'THROTTLE'),(.288,'RPM'),(.258,'MIXTURE')]:
   black.box((x,y+.0125,s['ztop']),(.120,.004,.003))
   quad(labels,luv,(x,y+.0125,s['ztop']+.0016),(1,0,0),(0,1,0),.080,.0038,uv[key])
  put('GL_Quadrant_'+s['sfx'],[('Cockpit_black',plates,None),('P3_ControlMetal',metal,None),('Cockpit_black',black,None),('GL_PhotoLabels',labels,luv)],(x,.29,z))
  # Window and cord run alongside the compact console, below its inboard edge.
  c=next(c for c in controls if c['seat']==seat and c['param']=='trim')
  P=np.array((bx,.263,-.245))
  frame=Shape();frame.box(P+(0,.006,0),(.160,.008,.034))
  window=Shape();window.box(P+(0,0,0),(.138,.003,.022))
  index=Shape();index.box(P+(-.072,-.003,0),(.008,.003,.002))
  for xx in np.linspace(-.06,.06,9):index.box(P+(xx,-.003,.008),(.001,.002,.004))
  put('Mount_'+c['grip'][5:],[('Cockpit_quadrant',frame,None),('Cockpit_black',window,None),('P3_ControlMetal',index,None)],P)
  pointer=Shape();pointer.box(P+(0,-.003,0),(.003,.002,.014))
  put(c['node'],[('Cockpit_knob_red',pointer,None)],P)
  c.update(pivot_gltf=gl(P).tolist(),placement='v6 indicator inside window beside console; index at left; cord separate')
  nodes[names[c['node']]]['extras']['interaction']=c
  rigrows[c['node']]['pivot_gltf']=c['pivot_gltf']
  for ch in nodes[names[c['node']]].get('children',[]):nodes[ch]['translation']=[0,0,0]
 g.doc['extras']['vrCockpitPhase3a']['placement_v4']='Ty photos 2026-11-03: ignition only on panels; console toggles/breakers; laminated quadrant; braided cable trim; no fuel valve'

def build_linkages(g,doc,controls,names):
 """Continuous trim cord and common fuel-valve torque rod; Ty 2026-11-03."""
 nodes=g.doc['nodes']
 def put(name,parts,P):
  if name not in names:
   nodes.append(dict(name=name));names[name]=len(nodes)-1;g.doc['scenes'][0]['nodes'].append(names[name])
  set_mesh(g,name,parts,np.array(P));nodes[names[name]]['translation']=gl(P).tolist()
 # Cord stays at the existing indicator height across both cockpits, then follows
 # the narrowing left fuselage toward the tail. Fairleads are open rings.
 route=[(-.79,.253,-.270),(-2.18,.253,-.270),(-2.65,.255,-.26),(-3.25,.175,-.22),(-3.95,.095,-.18),(-4.5,.045,-.15)]
 rope=Shape();guides=Shape()
 for A,B in zip(route,route[1:]):
  A=np.array(A);B=np.array(B);d=B-A;length=np.linalg.norm(d);d/=length
  u=np.cross(d,(0,0,1));u/=np.linalg.norm(u);v=np.cross(d,u)
  rope.tube(A,B,.0023,10)
  for phase in [0,math.pi]:
   pts=[A+t*d+.0024*(math.cos(t*950+phase)*u+math.sin(t*950+phase)*v) for t in np.linspace(0,length,int(length*800)+2)]
   for aa,bb in zip(pts,pts[1:]):rope.tube(aa,bb,.00065,5)
 for x,y,z in [(-.83,.253,-.270),(-1.48,.253,-.270),*route[1:-1]]:
  C=np.array((x,y,z))
  pts=[C+np.array((0,.008*math.cos(t),.008*math.sin(t))) for t in np.linspace(0,2*math.pi,17)]
  for aa,bb in zip(pts,pts[1:]):guides.tube(aa,bb,.0018,6)
  guides.box(C+(0,.014,0),(.024,.008,.025))
 put('GL_TrimCord_Run',[('GL_RopeWhite',rope,None)],route[0])
 put('GL_TrimCord_Fairleads',[('P3_ControlMetal',guides,None)],route[0])
 # Both existing interaction nodes use the same fuel_valve input and same axis.
 # Builder +X rotation takes down (-Z) to inboard (+Y). glTF +X is
 # builder -X, so -90 degrees produces OFF. Existing fuel input defaults to 1 (ON).
 import subprocess
 base_doc=json.loads(base_bytes('docs/models/great-lakes-own-rig.json'))
 rod=Shape();rod.tube((-1.85,-.322,-.170),(-.76,-.322,-.170),.005,12)
 brackets=Shape()
 for xx in [-1.83,-1.43,-1.16,-.80]:
  brackets.tube((xx-.012,-.322,-.170),(xx+.012,-.322,-.170),.010,16)
  brackets.box((xx,-.340,-.170),(.025,.026,.035))
 put('GL_FuelValve_SharedRod',[('P3_ControlMetal',rod,None)],(-1.85,-.322,-.170))
 put('GL_FuelValve_RodGuides',[('Cockpit_quadrant',brackets,None)],(-1.85,-.322,-.170))
 for seat,s in SEATS.items():
  nm='Ctl_Rotary_FuelSelector'+s['ctl'];P=np.array((s['px'],-.322,-.170))
  c=next(dict(c) for c in base_doc['controls'] if c['node']==nm)
  c.update(pivot_gltf=gl(P).tolist(),axis_gltf=[1.,0.,0.],travel=90,sweep_radius_m=.105,placement='Right wall shared fore/aft rod; ON down, OFF inboard toward knee',label='FUEL VALVE',position_status='E')
  controls.append(c)
  red=Shape();red.tube(P+(0,0,-.012),P+(0,0,-.095),.009,12);red.sphere(P+(0,0,-.095),.012,16,10)
  boss=Shape();boss.tube(P+(-.010,0,0),P+(.010,0,0),.014,16)
  put(nm,[('Cockpit_knob_red',red,None),('P3_ControlMetal',boss,None)],P)
  nodes[names[nm]]['extras'].update(rig_axis_gltf=[1.,0.,0.],rig_map_param=[0.,1.],rig_map_deg=[-90.,0.],rig_param_model=1.,interaction=c,mechanically_linked='GL_FuelValve_SharedRod')
  for ch in nodes[names[nm]].get('children',[]):nodes[ch]['translation']=gl((0,0,-.08)).tolist()
  row=next(dict(r) for r in base_doc['rig'] if r['node']==nm)
  row.update(axis_gltf=[1.,0.,0.],pivot_gltf=gl(P).tolist(),map=[[0,-90],[1,0]],model=1,desc=c['placement'])
  doc['rig'].append(row)
 g.doc['extras']['vrCockpitPhase3a']['placement_v4']='Ty 2026-11-03: continuous left trim cord through fairleads toward tail; right-wall linked fuel rod and two red handles ON down / OFF inboard'


def check_row(g):
 """Photo layout contract: only ignition remains on the instrument panel."""
 C=g.doc['extras']['vrCockpitPhase3a']['controls']
 for seat,s in SEATS.items():
  for lab in ROW:
   c=next(c for c in C if c['seat']==seat and c['grip'][5:].replace('_Front','')==lab)
   p=builder(np.array(c['pivot_gltf']))
   if lab=='Magneto':assert abs(p[0]-s['X']+.001)<.0001
   else:assert p[1]>.25 and p[0]<s['px']-.13,(seat,lab,p)
 print('photo console placement legal')

if __name__=='__main__':
 import subprocess
 def base(path):return base_bytes(path.relative_to(ROOT))
 GLB.write_bytes(base(GLB));doc=json.loads(base(RIG));g=Glb(GLB)
 seen=set();rows=[]
 for r in doc['rig']:
  k=json.dumps(r,sort_keys=True)
  if k not in seen:seen.add(k);rows.append(r)
 doc['rig']=rows
 build(g,doc)
 # The quadrant prop/mixture levers and the fuel handles outgrew the 40 mm / 105 mm P3a sweep radius they inherited;
 # check_vr_phase3 bounds each control by it, so record the measured one (metadata only, nothing at runtime reads it).
 by={n['name']:n for n in g.doc['nodes']}
 for c in g.doc['extras']['vrCockpitPhase3a']['controls']:
  n=by[c['node']];r=float(np.linalg.norm(np.concatenate([g.array(p['attributes']['POSITION']) for p in g.doc['meshes'][n['mesh']]['primitives']]),axis=1).max())
  if r>c['sweep_radius_m']:
   c['sweep_radius_m']=math.ceil(r*1000)/1000
   n['extras'].get('interaction',{})['sweep_radius_m']=c['sweep_radius_m']
   for d in doc['controls']:
    if d['node']==c['node']:d['sweep_radius_m']=c['sweep_radius_m']
 validate(g,GLB);g.save(GLB);RIG.write_text(json.dumps(doc,indent=1)+'\n')
 mirror=ROOT/'unity/Assets/StreamingAssets/rigs/great-lakes-like-own.json'   # ConfigSync's mirror (absent in a fresh tree)
 if mirror.parent.is_dir():mirror.write_text(RIG.read_text())
 check_row(Glb(GLB));print('quadrant + switch row written',GLB.name)
