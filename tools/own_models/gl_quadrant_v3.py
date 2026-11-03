"""Great Lakes throttle quadrant + single-row labelled switch strip (owner Ty, 2026-11-02).

Ty: "the Great Lakes throttle is a QUADRANT LEVER (not push-pull)". Ty (steering): "arrange the Great Lakes
switches in a SINGLE ROW along the BOTTOM of the instrument panel, each with a legible placard label".

QUADRANT, each cockpit, on the LEFT sidewall just aft of the panel with the knobs at panel-bottom height, as in
the 2T-1A front-cockpit photo (ref IMG_1666) and the Ferriere 2T-1A notes: three levers in one gate,
left-to-right from the seat THROTTLE (black), PROP (blue), MIXTURE (red) -- Ty's order, see gl_cockpit.py and
check_great_lakes_rig.py. All three rest (rig value 0) 25 deg aft = idle / coarse / lean and swing 50 deg
forward about the gate's lateral axis (glTF +z, the existing rig axis) to full / fine / rich at param 1.
  * Throttle_R / Throttle_F are rebuilt as the black lever (same node, same parent, same rig row and param
    'throttle'); Grip_Throttle_Rear / Grip_Throttle_Front empties at the knob give VrCockpitHands its arm
    (the Pitts/Extra contract), so the hand drives Throttle01 and the rig draws both levers from it.
  * Ctl_PushPull_Prop / Ctl_PushPull_Mixture (+_Front) become the blue / red levers: same node names and
    PanelKind.PushPull (a continuous grabbed control), rig row now kind 'rot' about the gate axis instead of a
    panel slide, so BindPanel's SlideKnob runs between the lever's idle and full knob points. Their Mount_
    twins are the gate slots. The old static Prop_/Mixture_ lever meshes become inert empties.
  * The old hip-height console box and front gate plate (gl_cockpit.py 'ck_quadrant') are removed.

SWITCH ROW, each cockpit: the eight non-radio panel controls on ONE line along the bottom of the panel, on a
flush black strip under the panel (aft face exactly on the panel face plane, like the Extra subpanel), each with
a white-on-black placard under it. Rear row gaps around the drop-down sequence card. Radio knobs unchanged.
Rerunnable: it always starts from the pre-quadrant GLB and rig json at commit BASE (read with git show), so a
second run gives the same bytes. great-lakes-like-own.glb only (the legacy liveries are not built). It also drops
the exact-duplicate rig rows an earlier generator appended (each P3a control was listed 9 times, identical).
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
LEVERS=[('Throttle',.312,.150,'Cockpit_black',.016,'T','THR'),('Prop',.288,.135,'Cockpit_knob_blue',.013,'P','PROP'),
        ('Mixture',.264,.120,'Cockpit_knob_red',.013,'M','MIX')]
HOUSING_Y=(.252,.338);HOUSING_DX=.065

F={'A':'01110 10001 10001 11111 10001 10001 10001','B':'11110 10001 10001 11110 10001 10001 11110','C':'01111 10000 10000 10000 10000 10000 01111',
 'D':'11110 10001 10001 10001 10001 10001 11110','E':'11111 10000 10000 11110 10000 10000 11111','F':'11111 10000 10000 11110 10000 10000 10000',
 'G':'01111 10000 10000 10011 10001 10001 01111','H':'10001 10001 10001 11111 10001 10001 10001','I':'11111 00100 00100 00100 00100 00100 11111',
 'K':'10001 10010 10100 11000 10100 10010 10001','L':'10000 10000 10000 10000 10000 10000 11111','M':'10001 11011 10101 10101 10001 10001 10001',
 'N':'10001 11001 11001 10101 10011 10011 10001','O':'01110 10001 10001 10001 10001 10001 01110','P':'11110 10001 10001 11110 10000 10000 10000',
 'R':'11110 10001 10001 11110 10100 10010 10001','S':'01111 10000 10000 01110 00001 00001 11110','T':'11111 00100 00100 00100 00100 00100 00100',
 'U':'10001 10001 10001 10001 10001 10001 01110','V':'10001 10001 10001 10001 01010 01010 00100','X':'10001 01010 00100 00100 00100 01010 10001',
 'W':'10001 10001 10001 10101 10101 11011 10001',' ':'00000 00000 00000 00000 00000 00000 00000'}
CW,CH,SC=38,21,4          # label cell in font pixels (6 chars x 2 lines), image scale
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
 k=Shape();kc=knob(seat,lv);k.sphere(kc,r,14,8)
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
   letter=lv[5];d=lever_dir(0);r=lv[4];deco=Shape();duv=[]
   quad(deco,duv,kc+d*(r+.0006),(0,-1,0),np.cross(d,(0,-1,0)),.014,.0078,UV['knob'+letter])
   parts.append(('GL_Placard_Atlas',deco,duv))
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

def check_row(g):
 """The row positions must pass cockpit_controls_v2's own legality (outline incl. the strip, obstacles, spacing)."""
 spec=cc.PANELS['great-lakes-like']
 for seat,s in SEATS.items():
  z0,z1=s['strip'];ya,yb=s['strip_y'];spec[seat]['new']={'GL_SwitchStrip_'+s['sfx']:cc.rect(ya,yb,z0,z1)}
 MOUNT=set(cc.MOUNT_OK)|{'GL_Placard_Atlas','P3e_Subpanel'};cc.MOUNT_OK.clear();cc.MOUNT_OK.update(MOUNT)
 for seat,s in SEATS.items():
  X,outline,obst=cc.seat_geometry(g,'great-lakes-like',seat);out=~outline
  ok=(cc.boxsum(obst,-.017,.017,-.017,.025)==0)&(cc.boxsum(out,-.016,.016,-.018,.023)==0)
  obs=cc.obstacles_for(g,'great-lakes-like',seat)
  for lab_,y in zip(ROW,s['row_y']):
   i=int((y-cc.Y0)/cc.RES);j=int((s['row_z']-cc.Z0)/cc.RES)
   cl=cc.clearance(np.array([[X-.001,y,s['row_z']]]),.04,obs)[0]
   assert ok[i,j],(seat,lab_,y,'slot not legal (outline/obstacle)')
   assert cl>.003,(seat,lab_,'too close to knee/stick/throttle sweep',cl)
  ys=sorted(s['row_y']);assert min(np.diff(ys))>=.031,(seat,'spacing')
 print('switch rows legal')

if __name__=='__main__':
 import subprocess
 def base(path):return subprocess.run(['git','show',BASE+':'+str(path.relative_to(ROOT))],cwd=ROOT,check=True,capture_output=True).stdout
 GLB.write_bytes(base(GLB));doc=json.loads(base(RIG));g=Glb(GLB)
 seen=set();rows=[]
 for r in doc['rig']:
  k=json.dumps(r,sort_keys=True)
  if k not in seen:seen.add(k);rows.append(r)
 doc['rig']=rows
 build(g,doc)
 validate(g,GLB);g.save(GLB);RIG.write_text(json.dumps(doc,indent=1)+'\n')
 (ROOT/'unity/Assets/StreamingAssets/rigs/great-lakes-like-own.json').write_text(RIG.read_text())   # ConfigSync's mirror
 check_row(Glb(GLB));print('quadrant + switch row written',GLB.name)
