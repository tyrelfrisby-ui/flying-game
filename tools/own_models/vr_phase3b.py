"""Extra CPU panel replacement. Original geometry/art, placements/case selection E.
Panel u right, v up; builder (-depth,-u,v). No Blender dependency.
"""
import json, math, tempfile
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from instrument_face_style import font, finish
from vr_cockpit import Author, Shape, Glb, ROOT, gl, builder
from vr_phase2e import compact
from drop_card_slot import validate
# key, u, v, nominal case inches, needles
# ASI, altimeter, turn coordinator and g-meter are the SHARED INSTRUMENT LIBRARY'S (tools/own_models/instruments, Ty 2026-11-05):
# their slots get a bezel and a blank face only (the later Extra passes move those nodes), and the game puts the library
# instrument there (InstrumentLibrary.Extra, Bridge/SharedInstruments). No needles, symbol or ball of their own.
LIBRARY=('ASI','Altimeter','TC','G')
LAYOUT=[('ASI',-.15,.30,3.125,[]),('Altimeter',-.05,.30,3.125,[]),('TC',.05,.30,3.125,[]),('G',.15,.30,3.125,[]),
 ('Tach',-.15,.20,3.125,['Tach']),('MP',-.05,.20,3.125,['MP']),('Oil',.05,.20,3.125,['OilP','OilT']),('Temps',.15,.20,3.125,['EGT','CHT']),
 ('Amps',-.10,.11,2.25,['Amps']),('Fuel_Wing',0,.11,2.25,['Fuel_Wing']),('Fuel_Akro',.10,.11,2.25,['Fuel_Akro'])]
# param, values, clockwise angles from vertical, unit
SCALES={
 'Tach':('rpm',[0,3000],[-150,150],'rpm'), 'MP':('manifold_inhg',[10,35],[-150,150],'inHg'),
 'OilP':('oil_press_psi',[0,120],[-80,80],'psi'), 'OilT':('oil_temp_f',[100,250],[100,260],'degF'),
 'EGT':('egt_f',[800,1800],[-80,80],'degF'), 'CHT':('cht_f',[100,500],[100,260],'degF'),
 'Amps':('amps',[-60,0,60],[-120,0,120],'A'), 'Fuel_Wing':('fuel_wing_gal',[0,31.7],[-120,120],'US gal'), 'Fuel_Akro':('fuel_akro_gal',[0,2.6,10,10.6],[-120,-120,120,120],'US gal')}
LABELS={'Tach':'RPM','MP':'inHg','Oil':'OIL','Temps':'EGT / CHT °F','Amps':'AMPS','Fuel_Wing':'WING','Fuel_Akro':'AKRO'}
TICKS={'Tach':list(range(0,3001,500)), 'MP':list(range(10,36,5)), 'OilP':[0,25,55,90,120], 'OilT':[100,140,210,250], 'EGT':[800,1000,1200,1400,1600,1800], 'CHT':[100,200,300,400,500], 'Amps':[-60,-30,0,30,60], 'Fuel_Wing':[0,8,16,24,31.7], 'Fuel_Akro':[2.6,6.3,10]}
ARCS={'MP':[(10,25,'#45d765'),(25,29.5,'#ffe044'),(29.5,29.5,'#ff4040')], 'Tach':[(700,2400,'#45d765'),(2400,2700,'#ffe044'),(2700,2700,'#ff4040')], 'OilP':[(25,55,'#ffe044'),(55,90,'#45d765'),(90,100,'#ffe044'),(25,25,'#ff4040'),(100,100,'#ff4040')], 'OilT':[(100,140,'#ffe044'),(140,210,'#45d765'),(210,245,'#ffe044'),(245,245,'#ff4040')], 'CHT':[(100,150,'#ffe044'),(150,435,'#45d765'),(435,500,'#ffe044'),(500,500,'#ff4040')]}
def angle(k,v):return float(np.interp(v,SCALES[k][1],SCALES[k][2]))
def art(key,needles,path):
 im=Image.new('RGB',(1024,1024),(12,16,20));d=ImageDraw.Draw(im)
 if key in LIBRARY:finish(im).save(path);return   # blank: the library instrument covers it
 def point(a,r):a=math.radians(a);return (512+r*math.sin(a),512-r*math.cos(a))
 dual=key in ('Oil','Temps')
 for k in needles:
  ticks=TICKS.get(k,[])
  for v in ticks:
   a=angle(k,v)
   d.line([point(a,415),point(a,455)],fill='white',width=5)
   text=({2.6:'E',6.3:'1/2',10:'F'}[v] if k=='Fuel_Akro' else ('+'+str(v)) if k=='Amps' and v>0 else str(v))
   d.text(point(a,355),text,font=font(34 if dual else 42),anchor='mm',fill='white')
  for lo,hi,color in ARCS.get(k,[]):
   a0,a1=angle(k,lo),angle(k,hi)
   if lo==hi:d.line([point(a0,400),point(a0,468)],fill=color,width=9)
   else:d.line([point(a,442) for a in np.linspace(a0,a1,150)],fill=color,width=11)
 d.text((512,510 if dual else 660),LABELS[key],font=font(40 if dual else 54),anchor='mm',fill='white')
 if dual:
  for label,y in ([('P psi',270),('T °F',754)] if key=='Oil' else [('EGT',270),('CHT',754)]):d.text((512,y),label,font=font(40),anchor='mm',fill='white')
 if key.startswith('Fuel'):d.text((512,740),'E - F' if key=='Fuel_Akro' else 'US GAL',font=font(40),anchor='mm',fill='white')
 finish(im).save(path)

def update(path):
 if path.parent.name!='extra-300-like':return
 g=Glb(path)
 if g.doc.get('extras',{}).get('vrCockpitPhase3b'):return
 rigpath=ROOT/'docs/models/ea300-own-rig.json';doc=json.loads(rigpath.read_text());a=Author(g);world=g.world_matrices();retired=[]
 # Fully retire every old instrument and panel mesh, including static faces in mixed meshes.
 for n in g.doc['nodes']:
  name=n['name']
  if name.startswith(('Needle_','TC_','Compass_Card','RearPanel_','Radio_','Xpdr_')) or name in ('FuelSel_F','FuelSel_R','MagKey_R','Mixture_R','Switch_Master_R','Trim_R','Prop_R'):
   retired.append(name);n.pop('mesh',None);n['extras']={'p3b_retired':True};n['name']='Legacy_P3b_'+name
  if name in ('Cockpit_Interior','Cockpit_Shell'):
   mesh=g.doc['meshes'][n['mesh']]
   keep={'Cockpit_quadrant'} if name=='Cockpit_Interior' else {'Cockpit_seat','Cockpit_harness','Cockpit_tube','Paint_cabin'}
   mesh['primitives']=[p for p in mesh['primitives'] if g.doc['materials'][p['material']]['name'] in keep]
 doc['rig']=[r for r in doc['rig'] if r['node'] not in retired and not r['node'].startswith(('Needle_','Ball_'))]
 black=g.material('P3b_Panel',(.028,.032,.038));bezel=g.material('P3b_Bezel',(.008,.009,.012));white=g.material('P3b_Needle',(.95,.95,.88));yellow=g.material('P3b_Telltale',(1,.65,.08));orange=g.material('P3b_SecondNeedle',(1,.40,.08))
 panels=[]
 with tempfile.TemporaryDirectory() as td:
  mats={}
  for key,u,v,case,needles in LAYOUT:
   png=Path(td)/(key+'.png');art(key,needles,png);mats[key]=g.material('P3b_Dial_'+key,png=png)
  for seat,sfx,x in [('Rear','R',-1.175),('Front','F',-.15)]:
   root='Panel_P3b_'+sfx;sh=Shape();sh.box((x+.004,0,.2175),(.008,.44,.285));a.node(root,(x,0,.2175),shape=sh,mat=black)
   rows=[]
   for key,u,v,case,needles in LAYOUT:
    r=case*.0254/2+.003 # 3mm bezel flange beyond nominal case
    p=np.array((x-.0005,-u,v));sh=Shape();sh.tube((x+.003,-u,v),(x-.0003,-u,v),r,64)
    a.node('Bezel_P3b_'+key+'_'+sfx,p,shape=sh,mat=bezel)
    rr=r-.0035;verts=[(x-.0005,-u,v)]+[(x-.0005,-u-rr*math.sin(t),v+rr*math.cos(t)) for t in np.linspace(0,2*math.pi,64,endpoint=False)]
    tris=[(0,(i+1)%64+1,i+1) for i in range(64)]
    uv=[(.5+( -yy-u)/(2*rr),.5-(zz-v)/(2*rr)) for xx,yy,zz in verts]
    face='Face_P3b_'+key+'_'+sfx;g.mesh(face,[gl(q) for q in verts],tris,mats[key],uv)
    row=dict(gauge=key,u=u,v=v,case_inches=case,bezel_radius_m=r,face=face,needles=[])
    for j,k in enumerate(needles):
     name='Needle_'+k+'_'+sfx
     pivot=np.array((x-.0008,-u,v));sh=Shape();length=rr*.83
     if True:
      sh.add([pivot+(0,-.0012,-.004),pivot+(0,.0012,-.004),pivot+(0,0,length)],[(0,2,1),(0,1,2)])
      # Distinguish coincident zero-pose hands by length/width and tiny depth separation.
      sh.v=[np.array(q)+(-j*.00005,0,0) for q in sh.v]
     param,points,angles,unit=SCALES[k]
     model=0 if k=='Amps' else points[0]
     initial=angle(k,model);theta=math.radians(initial);cc,ss=math.cos(theta),math.sin(theta)
     sh.v=[pivot+np.array((q[0]-pivot[0],cc*(q[1]-pivot[1])-ss*(q[2]-pivot[2]),ss*(q[1]-pivot[1])+cc*(q[2]-pivot[2]))) for q in sh.v]
     a.node(name,pivot,shape=sh,mat=orange if k in ('OilT','CHT') else white)
     a.rigged(name,param,(1,0,0),points,[ang-initial for ang in angles],desc='P3b clockwise from 12; map relative to modelled dial angle; E scale/case layout')
     n=g.doc['nodes'][a.idx(name)];n['extras']['rig_unit']=unit;a.rig[-1]['unit']=unit
     n['extras']['rig_param_model']=model;a.rig[-1]['model']=model
     n['extras']['rig_dial_ang0_deg']=initial;a.rig[-1]['extra']['rig_dial_ang0_deg']=initial
     row['needles'].append(name)
    rows.append(row)
   eye=builder(world[next(i for i,n in enumerate(g.doc['nodes']) if n['name']=='Eye_'+seat)][:3,3]).tolist()
   panels.append(dict(seat=seat,suffix=sfx,plane_x=x,normal_builder=[-1,0,0],eye_builder=eye,gauges=rows))
 # P3a side controls lowered below instrument rectangle; preserve hand contact children.
 controls=g.doc['extras']['vrCockpitPhase3a']['controls']
 for c in controls:
  n=g.doc['nodes'][a.idx(c['node'])];delta=np.array((0,-.45,0)) # glTF up = Y
  n['translation']=(np.array(n['translation'])+delta).tolist();c['pivot_gltf']=(np.array(c['pivot_gltf'])+delta).tolist()
  suffix='_Front' if c['seat']=='Front' else '';label=c['grip'][5:];mount=g.doc['nodes'][a.idx('Mount_'+label)];mount['translation']=(np.array(mount['translation'])+delta).tolist()
  for entry in doc['rig']:
   if entry['node']==c['node']:entry['pivot_gltf']=c['pivot_gltf']
  n['extras']['interaction']=c
 doc['controls']=controls
 # Radio/transponder displays now on a dedicated central sub-panel beneath gauges.
 for j,name in enumerate(('Radio_Display_R','Xpdr_Display_R')):
  p=np.array((-1.175,(-.055 if j==0 else .055),.035))
  sh=Shape();sh.box(p+(.004,0,0),(.006,.101,.041));a.node('Subpanel_'+name,p,shape=sh,mat=black)
  corners=[p+(0,.0475,-.0175),p+(0,-.0475,-.0175),p+(0,-.0475,.0175),p+(0,.0475,.0175)]
  g.mesh(name,[gl(q) for q in corners],[(0,1,2),(0,2,3)],g.material('P3b_'+name,(.015,.025,.03)),[(0,1),(1,1),(1,0),(0,0)])
  for display in doc.get('displays',[]):
   if display.get('node')==name:display.update(parent=None,centre_gltf=gl(p).tolist(),size_m=[.095,.035])
 # Move the complete sequence-card holder clear of the instrument rectangle.
 delta=gl((0,-.285,-.15))
 for n in g.doc['nodes']:
  if n['name'].startswith('SequenceCard_'):
   if 'matrix' in n:
    n['matrix'][12:15]=(np.array(n['matrix'][12:15])+delta).tolist()
   else:n['translation']=(np.array(n.get('translation',[0,0,0]))+delta).tolist()
 for card in doc.get('card_slots',[]):
  card['centre_gltf']=(np.array(card['centre_gltf'])+delta).tolist();card['note']='P3b holder relocated to right side, outside gauge rectangle; E.'
 doc['rig']+=a.rig;doc['phase3b']=dict(panels=panels,retired_nodes=retired,case_status='E',new_params=['egt_f','amps','turn_rate_dps','slip_ball','fuel_wing_gal','fuel_akro_gal'])
 g.doc['extras']['vrCockpitPhase3b']=doc['phase3b'];g.doc['extras'].pop('rearPanelLayout',None)
 # Remove unreachable old mesh objects and their buffers, not hidden duplicate geometry.
 used=sorted({n['mesh'] for n in g.doc['nodes'] if 'mesh'in n});mapping={v:i for i,v in enumerate(used)};g.doc['meshes']=[g.doc['meshes'][i] for i in used]
 for n in g.doc['nodes']:
  if 'mesh'in n:n['mesh']=mapping[n['mesh']]
 compact(g);validate(g,path);g.save(path);rigpath.write_text(json.dumps(doc,indent=1)+'\n')
