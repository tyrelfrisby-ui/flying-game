"""Original primitive-built late-172M cabin. Run via model_c172.py or Blender -b --python this_file.
Design frame: (right, forward, up); mapped to Blender (+forward,+left,+up), metres.
"""
import bpy, math, json, os, hashlib
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/os.environ.get('C172_OUT','renders/c172/instruments-v3'); OUT.mkdir(parents=True,exist_ok=True)
RIG=[]; CREATED=[]; LABELS=[]; PLACARDS=[]
def xyz(p): return (p[1],-p[0],p[2])
def mat(n,c,metal=0):
 m=bpy.data.materials.new('172_'+n);m.diffuse_color=(*c,1);m.use_nodes=True
 bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(*c,1);bs.inputs['Metallic'].default_value=metal;bs.inputs['Roughness'].default_value=.48 if metal else .76
 return m
black=mat('charcoal',(.018,.022,.021)); face=mat('instrument_black',(.006,.009,.009)); ivory=mat('warm_ivory',(.73,.65,.48)); white=mat('markings',(.98,.98,.90)); metal=mat('brushed_metal',(.30,.33,.32),.7); tan=mat('saddle_vinyl',(.29,.13,.055)); cloth=mat('woven_ochre',(.48,.31,.14)); carpet=mat('brown_carpet',(.065,.043,.025)); red=mat('mixture_red',(.52,.025,.012)); orange=mat('amber_digits',(.95,.36,.045)); blue=mat('gyro_sky',(.09,.31,.48)); earth=mat('gyro_earth',(.30,.15,.065)); green=mat('normal_arc',(.04,.38,.13)); yellow=mat('caution',(.85,.57,.04)); limit_red=mat('limit_red',(.80,.035,.02))
tube_glass=mat('inclinometer_glass',(.33,.43,.42),.25)
tube_glass.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.12
def finish(o,n,m):
 o.name='C172_'+n;o.data.materials.append(m);CREATED.append(o);return o
def box(n,p,s,m=black,bevel=.004):
 bpy.ops.mesh.primitive_cube_add(size=1,location=xyz(p));o=bpy.context.object;o.dimensions=(s[1],s[0],s[2]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bevel:
  mod=o.modifiers.new('rounded_edges','BEVEL');mod.width=bevel;mod.segments=3
  o.modifiers.new('weighted_normals','WEIGHTED_NORMAL')
 return finish(o,n,m)
def cyl(n,p,r,d,m=black,axis='f'):
 bpy.ops.mesh.primitive_cylinder_add(vertices=48,radius=r,depth=d,location=xyz(p));o=bpy.context.object
 direction={'f':(1,0,0),'u':(0,0,1),'r':(0,-1,0)}[axis];o.rotation_euler=Vector((0,0,1)).rotation_difference(Vector(direction)).to_euler()
 return finish(o,n,m)
def line(n,a,b,r=.003,m=metal):
 a,b=Vector(xyz(a)),Vector(xyz(b));d=b-a;bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=r,depth=d.length,location=(a+b)/2);o=bpy.context.object;o.rotation_euler=d.to_track_quat('Z','Y').to_euler();return finish(o,n,m)
def text(n,t,p,size=.008,m=white):
 cu=bpy.data.curves.new(n,'FONT');cu.body=t;cu.align_x='CENTER';cu.align_y='CENTER';cu.size=max(size,.0063);cu.extrude=0
 o=bpy.data.objects.new('C172_'+n,cu);bpy.context.collection.objects.link(o);o.location=xyz(p)
 # local text X = right (-world Y), Y = up; front normal = aft (-world X)
 from mathutils import Matrix
 o.rotation_euler=Matrix(((0,0,-1),(-1,0,0),(0,1,0))).to_euler();o.data.materials.append(m);CREATED.append(o);LABELS.append((o,t,cu.size));return o
def rig(o,kind,axis,limits,signal):
 entry=dict(node=o.name,kind=kind,axis=axis,range=limits,signal=signal,pivot_blender_m=list(o.location));o['rig']=json.dumps(entry);RIG.append(entry);return o
def parent(child,par):
 bpy.context.view_layer.update()
 world=child.matrix_world.copy();child.parent=par;child.matrix_world=world
 return child
def needle(n,x,z,angle=0,length=.028,m=white,f=.251,width=.002):
 a=math.radians(angle);o=box(n,(x,f,z),(width,.001,length),m,.0003)
 # Put origin on gauge centre, mesh extends upwards at zero.
 for v in o.data.vertices:v.co.z+=length*.35
 o.rotation_euler.x=-a
 return rig(o,'rotation','+X forward (Blender)',[-180,180],n)
def knob(n,x,z,r=.008,f=.265,kind='rotation',signal=None,m=black):
 o=cyl(n,(x,f,z),r,.018,m);rig(o,kind,'+X forward (Blender)',[0,360] if kind=='rotation' else [0,.06],signal or n)
 if kind=='translation':return o
 stripe=box(n+'_index',(x,f-.010,z+r*.5),(.002,.001,r*.5),white,.0002);parent(stripe,o);return o
# Width 1.02m, independent 3-1/8 inch instrument cases at 102mm centres.
panel=box('Panel',(0,.32,.185),(1.04,.045,.405),black,.018)
DIALS=[]
def cut_hole(target,x,z,r):
 cutter=cyl('mount_cut',(x,.32,z),r,.18)
 bpy.context.view_layer.objects.active=target
 mod=target.modifiers.new('recess','BOOLEAN');mod.operation='DIFFERENCE';mod.object=cutter
 bpy.ops.object.modifier_apply(modifier=mod.name)
 CREATED.remove(cutter);bpy.data.objects.remove(cutter,do_unlink=True)
def silhouette(n,x,f,z,points,m=white):
 # Front face points aft (-Blender X); Unity culls the back of these face polygons.
 if sum(a*d-c*b for (a,b),(c,d) in zip(points,points[1:]+points[:1]))<0:points=list(reversed(points))
 mesh=bpy.data.meshes.new(n);mesh.from_pydata([xyz((a,0,b)) for a,b in points],[],[tuple(range(len(points)))])
 o=bpy.data.objects.new(n,mesh);bpy.context.collection.objects.link(o);o.location=xyz((x,f,z));return finish(o,n,m)
box('LowerPanel',(0,.30,-.025),(1.00,.045,.095),ivory,.004)
box('Glareshield',(0,.34,.405),(1.08,.25,.036),black,.025)
for xx in [-.485,-.05,.49]:
 for zz in [.025,.365]:cyl('Panel_screw',(xx,.293,zz),.003,.002,metal)
def dial(n,x,z,r=.040,labels=None):
 DIALS.append((n,x,z,r,len(CREATED)))
 case=cyl(n+'_case',(x,.286,z),r+.004,.018,metal);case['layout']=json.dumps(dict(name=n,x=x,z=z,half_extent=r+.005));cyl(n+'_bezel',(x,.274,z),r+.002,.010,black);cyl(n+'_face',(x,.267,z),r,.002,face)
 for i in range(60):
  if n in ('Turn_coordinator','VSI','Suction') or (n=='Attitude' and i not in [0,5,10,50,55]):continue
  a=math.tau*i/60;rr=r*.88;l=r*(.14 if i%5==0 else .065)
  line(n+'_tick', (x+math.sin(a)*rr,.264,z+math.cos(a)*rr),(x+math.sin(a)*(rr-l),.264,z+math.cos(a)*(rr-l)),.0005,white)
 if labels and n!='DG' and not n.startswith('NAV'):
  for i,t in enumerate(labels):
   a=math.tau*i/len(labels)
   nr=r*(.64 if n in ('Airspeed','Altimeter','ADF') else .58 if n=='Tach' else .50 if n.startswith('Fuel_') else .48 if n in ('Oil_press','Oil_temp') else .56)
   fs=.0075 if n.startswith('Fuel_') else .0085 if n in ('Oil_press','Oil_temp') else .009
   text(n+'_numeral',str(t),(x+math.sin(a)*nr,.2658,z+math.cos(a)*nr),fs)
 if n in ('Airspeed','Altimeter'):text(n+'_label',n.upper(),(x,.2658,z+(.010 if n=='Altimeter' else -r*.34)),.0063)
 return (x,z)
# Pilot primary six-pack, clear of the separate nav heads.
for n,x,z,labels in [('Airspeed',-.423,.323,list(range(0,200,20))),('Attitude',-.321,.323,None),('Altimeter',-.219,.323,list(range(10))),('Turn_coordinator',-.423,.218,None),('DG',-.321,.218,['N',3,6,'E',12,15,'S',21,24,'W',30,33]),('VSI',-.219,.218,None)]:
 dial(n,x,z,labels=labels)
 if n=='Attitude':
  # Flat sliding horizon, masked by the fixed dial annulus. 0.8 mm / degree.
  o=cyl('AI_roll',(x,.310,z),.001,.0002,face);rig(o,'rotation','+X',[-180,180],'attitude.roll')
  q=box('AI_pitch',(x,.282,z),(.12,.0002,.12),blue,0)
  rig(q,'translation','+Z',[-.020,.020],'attitude.pitch');parent(q,o)
  parent(box('AI_earth',(x,.280,z-.060),(.12,.0002,.12),earth,0),q)
  parent(line('AI_horizon',(x-.060,.280,z),(x+.060,.280,z),.00045,white),q)
  for deg in [-20,-15,-10,-5,5,10,15,20]:
   zz=deg*.0008
   parent(line('AI_pitch_mark',(x-.009,.280,z+zz),(x+.009,.280,z+zz),.00035,white),q)
   if abs(deg)%10==0:parent(text('AI_pitch_number',str(abs(deg)),(x+.018,.2798,z+zz),.0065),q)
  # Fixed mask outside the viewing aperture, close to the moving art.
  mask=cyl('AI_mask',(x,.266,z),.041,.001,face);cut_hole(mask,x,z,.030)
  silhouette('AI_fixed_airplane',x,.2658,z,[(-.025,.001),(-.004,.001),(-.004,-.001),(-.025,-.001)],orange)
  silhouette('AI_fixed_airplane_R',x,.2658,z,[(.004,.001),(.025,.001),(.025,-.001),(.004,-.001)],orange)
  cyl('AI_centre_dot',(x,.2658,z),.0018,.0003,orange)
  for deg in [-90,-60,-30,-20,-10,0,10,20,30,60,90]:
   a=math.radians(deg)
   line('AI_bank_mark',(x+math.sin(a)*.032,.2658,z+math.cos(a)*.032),(x+math.sin(a)*.037,.2658,z+math.cos(a)*.037),.0006,white)
  parent(silhouette('AI_sky_pointer',x,.2658,z,[(0,.030),(-.002,.026),(.002,.026)],orange),o)
  knob('AI_cage',x+.025,z-.030,r=.004);text('Vacuum','VAC',(x,.2653,z-.035),.0063)
 elif n=='DG':
  # The compass rose itself rotates; fixed aircraft remains on the face.
  o=cyl('DG_card',(x,.259,z),.030,.001,face);rig(o,'rotation','+X',[0,360],'heading.nonSlavedDeg')
  for i,t in enumerate(['N',3,6,'E',12,15,'S',21,24,'W',30,33]):
   a=math.tau*i/12;parent(text('DG_card_text',str(t),(x+math.sin(a)*.024,.2573,z+math.cos(a)*.024),.0085),o)
  silhouette('DG_aircraft',x,.254,z,[(0,.018),(.002,.014),(.002,.004),(.018,-.003),(.018,-.006),(.002,-.002),(.0015,-.012),(.007,-.015),(.007,-.017),(0,-.015),(-.007,-.017),(-.007,-.015),(-.0015,-.012),(-.002,-.002),(-.018,-.006),(-.018,-.003),(-.002,.004),(-.002,.014)])
  bug=box('DG_heading_bug',(x,.251,z),(.006,.001,.006),orange,.0005)
  for v in bug.data.vertices:v.co.z+=.031
  rig(bug,'rotation','+X',[0,360],'headingBugDeg');knob('DG_sync_adjust',x-.027,z-.030,.005);knob('DG_bug_adjust',x+.027,z-.030,.005)
 elif n=='Turn_coordinator':
  # Rear-view aeroplane: straight wings, round fuselage, vertical fin and tailplane.
  q=silhouette('TC_airplane',x,.251,z,[(-.026,.001),(-.004,.001),(-.002,.003),(-.001,.011),(.001,.011),(.002,.003),(.004,.001),(.026,.001),(.026,-.001),(.004,-.002),(.002,-.004),(-.002,-.004),(-.004,-.002),(-.026,-.001)])
  parent(cyl('TC_fuselage',(x,.250,z),.003,.001,white),q);rig(q,'rotation','+X',[-40,40],'turn.rate')
  for side,label in [(-1,'L'),(1,'R')]:
   # Marks share the exact pivot radial reached at one rate (20 degrees).
   a=math.radians(20)
   mark=line('TC_rate_mark',(x+side*.024*math.cos(a),.2658,z-.024*math.sin(a)),(x+side*.034*math.cos(a),.2658,z-.034*math.sin(a)),.0007,white);mark['tc_mark_angle_deg']=side*20;mark['tc_pivot_design']=json.dumps([x,z])
   text('TC_direction',label,(x+side*.030,.2658,z+.009),.006)
  text('TC_title','TURN\nCOORDINATOR',(x,.2658,z+.025),.0063)
  text('TC_two_min','2 MIN',(x,.2658,z-.010),.0063)
  text('TC_electric','D.C. ELEC.',(x,.2658,z-.033),.0063)
  # Glass tube follows a shallow U; black ball lies between two wire references.
  for i in range(40):
   a=-.020+i*.001;b=a+.001
   za=z-.022+12*a*a;zb=z-.022+12*b*b
   line('TC_glass_tube',(x+a,.253,za),(x+b,.253,zb),.0038,tube_glass)
   line('TC_glass_highlight',(x+a,.2488,za+.002),(x+b,.2488,zb+.002),.00045,white)
  for side in [-1,1]:line('TC_reference_wire',(x+side*.0045,.248,z-.026),(x+side*.0045,.248,z-.017),.00045,black)
  q=cyl('TC_slip_ball',(x,.248,z-.022),.003,.0015,black);rig(q,'translation','-Y',[-.015,.015],'slip')
 else:
  if n=='Airspeed':
   for lo,hi,rr,ma in [(49,100,.034,white),(57,128,.037,green),(128,164,.037,yellow)]:
    for v in range(lo,hi,2):
     a=math.tau*v/200;b=math.tau*(v+2)/200
     line('ASI_speed_arc',(x+math.sin(a)*rr,.2658,z+math.cos(a)*rr),(x+math.sin(b)*rr,.2658,z+math.cos(b)*rr),.0012,ma)
   text('ASI_units','MPH',(x,.2658,z+.009),.0063)
  if n=='VSI':
   def vsi_angle(fpm):
    v=abs(fpm);return math.copysign(v*.09 if v<=1000 else 90+(v-1000)*.08,fpm)
   def vsi_point(fpm,rr):
    a=math.radians(-90+vsi_angle(fpm));return (x+math.sin(a)*rr,.2658,z+math.cos(a)*rr)
   for fpm in range(-2000,2001,100):
    line('VSI_tick',vsi_point(fpm,.035),vsi_point(fpm,.031 if fpm%500==0 else .033),.00045,white)
   for fpm in [-2000,-1500,-1000,-500,0,500,1000,1500,2000]:text('VSI_number',str(abs(fpm)//100),vsi_point(fpm,.025 if abs(fpm)==2000 else .026),.009)
   for title,zz in [('VERTICAL SPEED',.012),('UP',.004),('DOWN',-.005),('100 FEET',-.011),('PER MINUTE',-.019)]:
    legend=text('VSI_legend',title,(x+(0 if title=='PER MINUTE' else .006),.2658,z+zz),.0063)
    if title=='PER MINUTE':legend.scale.x=.68  # Narrow printed type, same 4.6 mm cap height; clears both 15/5 numerals.
  needle(n+'_needle',x,z,55 if n=='Airspeed' else (90 if n=='VSI' else 0),.038 if n=='VSI' else .028)
  if n=='Altimeter':
   needle('Altimeter_1000',x,z,20,.019);needle('Altimeter_10000',x,z,5,.012);knob('Altimeter_baro',x-.027,z-.030,.005);text('Baro_window','29.92',(x+.014,.2658,z-.014),.005)
# Two distinct nav heads and ADF, not an HSI. As in a real 172 they sit beside the six-pack, LEFT of the engine column
# (Ty, 2026-11-03: "engine instruments to the right of the nav heads").
NAV_X=-.112;ENGINE_X=0
for idx,z in [(1,.323),(2,.218)]:
 x=NAV_X;dial('NAV'+str(idx),x,z,.039,['N',3,6,'E',12,15,'S',21,24,'W',30,33])
 rose=cyl('NAV%d_OBS_card'%idx,(x,.258,z),.027,.001,face);rig(rose,'rotation','+X',[0,360],'nav%d.obsDeg'%idx)
 for i,t in enumerate(['N',3,6,'E',12,15,'S',21,24,'W',30,33]):
  a=i*math.pi/6;parent(text('OBS_card',str(t),(x+math.sin(a)*.023,.2573,z+math.cos(a)*.023),.008),rose)
 for j in [-2,-1,0,1,2]:cyl('CDI_dot',(x+j*.007,.250,z),.001,.001,white)
 q=box('NAV%d_CDI'%idx,(x,.248,z),(.0013,.001,.038),white,0);rig(q,'translation','-Y',[-.018,.018],'nav%d.cdiNormalized'%idx)
 if idx==1:
  q=box('NAV1_GS',(x,.246,z),(.038,.001,.0013),orange,0);rig(q,'translation','+Z',[-.018,.018],'nav1.gsNormalized')
 knob('NAV%d_OBS'%idx,x-.026,z-.030,.005)
 for flag in ['NAV','TO_FROM']+(['GS'] if idx==1 else []):
  zz=z+.008+(['NAV','TO_FROM','GS'].index(flag)*.008)
  box('NAV_flag_window',(x+.014,.2455,zz),(.017,.0002,.0075),face,0)
  q=text('NAV%d_%s_flag'%(idx,flag),'TO' if flag=='TO_FROM' else flag,(x+.014,.2452,zz),.0063,red);rig(q,'rotation','+X',[0,90],'nav%d.%s'%(idx,flag))
x=NAV_X;z=.113;dial('ADF',x,z,.039,['N',3,6,'E',12,15,'S',21,24,'W',30,33]);adf=needle('ADF_bearing',x,z,35,.028,orange,.252);
tail=silhouette('ADF_tail',x,.252,z,[(-.001,0),(-.001,-.022),(-.003,-.026),(.003,-.026),(.001,-.022),(.001,0)],orange)
head=silhouette('ADF_head',x,.251,z,[(0,.030),(-.003,.023),(.003,.023)],orange)
for detail in [tail,head]:
 # Rotate around its gauge-centred origin to align head and tail with the needle.
 detail.rotation_euler.x=-math.radians(35);parent(detail,adf)
knob('ADF_card_adjust',x-.026,z-.03,.005)
# Requested third row and a single engine column between the NAV heads and the radios.
# SUCTION (Ty, 2026-11-04 14:58: "put the suction gage as a tiny instrument with a green range and two red lines, one
# too low and one too high ... Usually the suction gage is a tiny 1 inch instrument"). A 1 in face (radius 12.7 mm),
# still straight under the DG, now in the 60 mm between the DG's mount and the raised yoke shaft. 0-10 inHg over 270
# degrees, 27 deg/inHg clockwise from 7:30; green 4.5-5.5 inHg with a red radial at each end (C172R AFM supplement
# STC SA2196CE: suction green arc 4.5 to 5.5 in. Hg; the 1980 172N manual gives 4.5 to 5.4). VacuumSystem regulates
# at 5.0, mid-arc.
SUCTION_R,SUCTION_Z=.0127,.145
for n,x,z,r,labs in [('Tach',-.219,.113,.040,[0,5,10,15,20,25,30,35]),('Suction',-.321,SUCTION_Z,SUCTION_R,None),('Fuel_L',-.450,.113,.020,['E','1/4','1/2','3/4','F']),('Fuel_R',-.396,.113,.020,['E','1/4','1/2','3/4','F']),('Oil_press',ENGINE_X,.333,.026,[0,25,50,75,100]),('Oil_temp',ENGINE_X,.253,.026,[75,100,150,200,245]),('Ammeter',ENGINE_X,.173,.026,[-60,-30,0,30,60]),('EGT',ENGINE_X,.093,.026,[8,10,12,14,16])]:
 dial(n,x,z,r,labs if n!='Ammeter' else None)
 if n=='Ammeter':
  # A true centre-zero semicircular scale, -60 left, zero up, +60 right.
  for v in [-60,-30,0,30,60]:
   a=math.radians(v*1.5);text('Amps_number',str(v) if v<=0 else '+'+str(v),(x+math.sin(a)*r*.48,.2658,z+math.cos(a)*r*.48),.0072)
  needle(n+'_needle',x,z,0,r*.7,f=.251)
 elif n=='Suction':
  def suction_point(v,rr):
   a=math.radians(-135+27*v);return (x+math.sin(a)*rr,.2658,z+math.cos(a)*rr)
  for v in range(11):line('Suction_tick',suction_point(v,r*.92),suction_point(v,r*(.62 if v%5==0 else .78)),.0004,white)
  for k in range(10):line('Suction_green_arc',suction_point(4.5+k*.1,r*.85),suction_point(4.6+k*.1,r*.85),.0009,green)
  for v in (4.5,5.5):line('Suction_red_radial',suction_point(v,r*.98),suction_point(v,r*.60),.0005,limit_red)
  # No numerals: at the panel's 4.6 mm minimum legend height they collide with the ticks on a 1 in face. Long
  # ticks mark 0, 5 and 10; the placard below names it.
  needle(n+'_needle',x,z,135,r*.72,f=.251,width=.0012)
 else:needle(n+'_needle',x,z,35,r*.7,f=.251)
 if n=='Tach':text('tach_hours','RPM x100',(x+.006,.2658,z-.008),.0063)
 label={'Tach':'TACHOMETER','Suction':'SUCTION','Fuel_L':'FUEL QTY L','Fuel_R':'FUEL QTY R','Oil_press':'OIL PRESS','Oil_temp':'OIL TEMP','Ammeter':'AMPS','EGT':'EGT'}[n]
 # Printed panel placards below the square mounts, not floating in front of the dials.
 PLACARDS.append((n+'_placard',label,(x,.2973,z-r-.012),.0085))
# Map the drafting depths to a shallow dial assembly at the panel plane.
# The frontmost art is at the panel plane; cases remain behind it, never stacked on top.
bpy.context.view_layer.update()
end=len(CREATED)
for idx,(n,x,z,r,start) in enumerate(DIALS):
 stop=DIALS[idx+1][4] if idx+1<len(DIALS) else end
 group=CREATED[start:stop]
 # Work in world space so parented horizon/card detail receives the same affine map once.
 matrices={o:o.matrix_world.copy() for o in group}
 for o in group:
  if o.type=='FONT':
   bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o;bpy.ops.object.convert(target='MESH')
 for o in group:
  w=matrices[o];inv=w.inverted()
  for v in o.data.vertices:
   p=w@v.co
   p.x=.2975+(p.x-.267)*.10
   v.co=inv@p
 # Geometry is mapped above; now move origins without moving geometry, for correct runtime axes.
 for o in group:
  w=matrices[o];new=w.copy()
  new.translation.x=.2975+(w.translation.x-.267)*.10
  o.matrix_world=new
  delta=w.translation-new.translation
  for v in o.data.vertices:v.co+=new.to_3x3().inverted()@delta
 bpy.context.view_layer.update()
 # The sliding horizon is seen through a circular mask, with the panel hiding its overscan.
 if n=='Attitude':
  for o in group:
   if o.name in {'C172_Attitude_case','C172_Attitude_bezel','C172_Attitude_face'}:cut_hole(o,x,z,.030)
  cyl('AI_backing',(x,.303,z),.042,.001,face)
 cut_hole(panel,x,z,r+.002)
 plate=box(n+'_flush_mount',(x,.299,z),(2*(r+.005),.003,2*(r+.005)),black,.002)
 cut_hole(plate,x,z,r+.001)
 for sx in [-1,1]:
  for sz in [-1,1]:
   xx=x+sx*(r+.001);zz=z+sz*(r+.001)
   cyl(n+'_corner_screw',(xx,.2974,zz),.002,.0004,metal)
   box(n+'_screw_slot',(xx,.2971,zz),(.0028,.0002,.0004),black,0)
for args in PLACARDS:text(*args)
# Avionics stack: independent mechanically tuned COM/NAV radios, ADF receiver and DME.
for n,z,h in [('Audio',.366,.028),('KX170B_1',.307,.075),('KX170B_2',.224,.075),('KR85_ADF',.151,.060),('DME',.094,.042),('KT76',.042,.048)]:
 x=.160;box(n,(x,.280,z),(.177,.036,h),black,.003)
 for sx in [-1,1]:cyl(n+'_rack_screw',(x+sx*.083,.259,z+h*.36),.002,.002,metal)
 if n.startswith('KX'):
  text(n+'_type','KING KX 170B',(x,.2618,z+h*.36),.005)
  for off,freq,label in [(-.042,'122.80','COMM'),(.040,'110.30' if n.endswith('1') else '114.60','NAV')]:
   box(n+'_window',(x+off,.255,z+.007),(.072,.002,.022),face,.002);text(n+'_frequency',freq,(x+off,.2538,z+.007),.012,ivory);text(n+'_section',label,(x+off,.2618,z+.025),.004)
   outer=knob(n+'_'+label+'_coarse',x+off,z-.023,.009,f=.249);inner=knob(n+'_'+label+'_fine',x+off,z-.023,.005,f=.235);parent(inner,outer)
   knob(n+'_'+label+'_volume',x+off-.025,z-.023,.004,f=.248)
 elif n=='KR85_ADF':
  text(n+'_label','KING KR 85   ADF',(x,.2618,z+.021),.006);text(n+'_freq','0350',(x,.2618,z+.003),.014,ivory)
  for off in [-.060,.060]:knob(n+('_mode' if off<0 else '_tune'),x+off,z-.012,.007,f=.245)
 elif n=='DME':
  text(n+'_readout','042.6  NM',(x-.018,.2618,z+.006),.012,orange);text(n+'_type','DME',(x-.050,.2618,z-.010),.005)
  knob('DME_channel',x+.063,z,.006,f=.246);knob('DME_mode',x+.040,z-.013,.004,f=.246)
 elif n=='KT76':
  text(n+'_readout','1 2 0 0',(x,.2618,z+.006),.013,ivory)
  for i in range(4):knob('XPDR_digit'+str(i),x-.045+i*.028,z-.014,.004,f=.246)
  knob('XPDR_mode',x+.070,z,.006,f=.246)
 else:
  for i,t in enumerate(['COM1','COM2','NAV1','NAV2','ADF','SPKR']):
   xx=x-.070+i*.028;text('audio_label',t,(xx,.2618,z+.006),.0035);rig(box('Audio_'+t,(xx,.247,z-.006),(.008,.008,.007),ivory,.001),'rotation','-Y',[0,25],'audio.'+t)
# Lower switches, starter, primer and three proper push-pull engine controls.
for i,n in enumerate(['Master_BAT','Master_ALT','Beacon','Landing','Taxi','Nav_lights','Pitot_heat']):
 xx=[-.460,-.421,-.382,-.343,-.267,-.224,-.174][i];rig(box(n,(xx,.261,-.019),(.020,.015,.025),red if i<2 else black,.002),'rotation','-Y',[0,25],n);text(n+'_label',['BAT','ALT','BCN','LAND','TAXI','NAV','PITOT HT'][i],(xx,.2773,-.060),.0085,black)
knob('Mags_key',-.435,.035,.012,f=.255);text('ignition_label','OFF R L BOTH START',(-.435,.2973,.057),.0085)
knob('Primer',-.355,.035,.008,kind='translation',f=.285);text('Primer_label','PRIMER',(-.355,.2773,.008),.0085,black)
for n,x,m in [('Throttle',-.040,black),('Mixture',.035,red),('Carb_heat',.112,black)]:
 shaft=cyl(n+'_shaft',(x,.215,-.030),.004,.13,metal)
 control=knob(n,x,-.030,{'Throttle':.013,'Mixture':.010,'Carb_heat':.008}[n],f=.151,kind='translation',m=m);parent(shaft,control)
 label=text(n+'_label',n.upper().replace('_',' '),(x-(.009 if n=='Throttle' else .004),.2773,-.057),.0085,black)
 label.scale.x=.85  # Narrow placard type keeps the pilot's line of sight clear of the shafts.
for n,x in [('Cabin_heat',.255),('Cabin_air',.335)]:knob(n,x,-.030,.008,f=.269,kind='translation');text(n+'_label',n.upper().replace('_',' '),(x,.2773,-.057),.0085,black)
rig(box('Flap_switch',(.403,.245,-.020),(.012,.024,.014),ivory,.002),'rotation','-Y',[0,40],'flap.command');text('flap_label','FLAPS',(.400,.2773,.006),.0085,black)
text('flap_positions','0 10 20 30 40',(.460,.2773,.006),.0085,black).scale.x=.65
rig(box('Flap_indicator',(.458,.253,-.016),(.010,.002,.004),white,0),'translation','+Z',[0,.023],'flap.position')
# Whiskey compass, mounted above glareshield; no magnetic slaving to DG.
box('Compass_mount',(0,.37,.445),(.075,.065,.055),black,.008);text('Compass_card','W  27  30  N',(0,.3373,.448),.009)
rig(CREATED[-1],'rotation','+Z',[0,360],'magneticHeading');box('Compass_lubber',(0,.337,.448),(.001,.001,.020),orange,0)
# Dual control yokes and all four rudder pedals, independent toe brakes.
# PEDALS (Ty, 2026-11-04: "move the rudder pedals above the cabin floor and forward"). They used to stand on the
# carpet directly under the panel face (f 0.30), 0.18 m ahead of a seated pilot's knee, where no foot can reach them
# without going through the floor. In a 172 the rudder bars sit "in area below instrument panel", their pivot shaft
# set 6.50 in aft of the firewall (172 Maintenance Manual 27-20-00, rudder pedal installation). Each pedal now hangs
# on an arm from a lateral rudder bar 4 cm above the carpet, with an inclined pad whose lower half takes the ball of
# the foot and whose upper part is the toe brake, flush with it. The place is set by the seated 1.778 m mannequin at
# the approved eye: the ball of the foot as far forward as it goes with the heel resting on the carpet, which is a
# 130 degree knee. tools/own_models/check_c172_pedal_fit.py seats the mannequin and measures it.
PEDAL_TILT=56                                                     # pad face, degrees up from horizontal
PEDAL_D=(math.cos(math.radians(PEDAL_TILT)),math.sin(math.radians(PEDAL_TILT)))   # up the face (fwd, up)
PEDAL_N=(-PEDAL_D[1],PEDAL_D[0])                                  # face normal, toward the pilot
PEDAL_BALL=(.486,-.359)                                           # ball-of-foot contact at neutral (fwd, up)
PEDAL_PAD=(PEDAL_BALL[0]-.01*PEDAL_N[0],PEDAL_BALL[1]-.01*PEDAL_N[1])   # pad centre: face 10 mm aft of it
PEDAL_TOE=(PEDAL_PAD[0]+.052*PEDAL_D[0],PEDAL_PAD[1]+.052*PEDAL_D[1])   # toe brake above the pad, same face
PEDAL_PIVOT=(.546,-.505)                                          # rudder bar / pedal hinge (fwd, up)
HINGE={}
def pedal_box(n,x,c,s,m,bevel):
 o=box(n,(x,c[0],c[1]),s,m,bevel);o.rotation_euler.y=math.radians(90-PEDAL_TILT);return o
# YOKES (Ty, 2026-11-04 14:58: "Move the yoke up ... where it does not touch the thighs with full deflection"). With
# the column at z .040 the pilot's right horn, at 90 degrees of wheel, reached 17 mm into the seated mannequin's right
# thigh (29 mm at full left rudder with the column forward, where the thigh rises to its pedal). The whole yoke (column,
# hub, horns) now stands YOKE_RAISE higher; nothing else about it changed: same 0.27 m span, same +/-90 degree wheel,
# same +/-70 mm column. The column tube is 0.12 m longer forward, so it runs into the panel at full aft as
# well (it used to stop 37 mm short of the face at neutral and 107 mm at full aft). The column takes the place under the DG that the 2-1/4 in suction gauge had, as in a real 172,
# whose shaft comes out of the panel straight below the six-pack; the suction gauge is now the 1 in instrument beside
# the fuel gauges (below). Measured by check_c172_pedal_fit.py and C172YokeClearanceTests (docs/models/c172-yoke-v1.md).
YOKE_RAISE=.060;YOKE_Z=.040+YOKE_RAISE                   # m; column centre .040 -> .100
for side,x in [('L',-.30),('R',.30)]:
 col=cyl('Yoke_'+side+'_pitch',(x,.185,YOKE_Z),.013,.39,metal);rig(col,'translation','+X',[-.07,.07],'elevator')   # tube fwd -.01 ... .38
 yoke=box('Yoke_'+side+'_roll',(x,-.025,YOKE_Z),(.120,.046,.066),black,.016);rig(yoke,'rotation','+X',[-90,90],'aileron');parent(yoke,col)
 for s in [-1,1]:
  a=line('Yoke_'+side+'_bar',(x,-.030,YOKE_Z-.021),(x+s*.135,-.030,YOKE_Z-.042),.012,black);parent(a,yoke)
  a=line('Yoke_'+side+'_grip',(x+s*.135,-.030,YOKE_Z-.042),(x+s*.135,-.030,YOKE_Z+.055),.014,black);parent(a,yoke)
 parent(text('Yoke_badge','TRAINER',(x,-.0482,YOKE_Z+.001),.009,ivory),yoke)
 for s,tag in [(-1,'L'),(1,'R')]:
  xx=x+s*.083;o=pedal_box('Pedal_'+side+'_'+tag,xx,PEDAL_PAD,(.125,.020,.065),black,.004);rig(o,'rotation','-Y',[-18,18],'rudder');HINGE[o.name]=xyz((xx,*PEDAL_PIVOT))
  toe=pedal_box('Toe_brake_'+side+'_'+tag,xx,PEDAL_TOE,(.11,.020,.035),metal,.002);rig(toe,'rotation','-Y',[0,15],'brake.'+tag);parent(toe,o)
  HINGE[toe.name]=xyz((xx,PEDAL_TOE[0]-.0175*PEDAL_D[0],PEDAL_TOE[1]-.0175*PEDAL_D[1]))
  for k in range(5):parent(pedal_box('Pedal_rib',xx-.045+k*.022,(PEDAL_PAD[0]+.0115*PEDAL_N[0],PEDAL_PAD[1]+.0115*PEDAL_N[1]),(.004,.003,.050),metal,.0005),o)
  parent(line('Pedal_arm',(xx,PEDAL_PAD[0]-.01*PEDAL_N[0]-.02*PEDAL_D[0],PEDAL_PAD[1]-.01*PEDAL_N[1]-.02*PEDAL_D[1]),(xx,*PEDAL_PIVOT),.009,metal),o)
 line('Rudder_bar_'+side,(x-.16,*PEDAL_PIVOT),(x+.16,*PEDAL_PIVOT),.012,metal)
# Floor, centre pedestal and trim (wheel on vertical fore-aft plane).
box('Carpet',(0,-.47,-.565),(1.04,2.20,.035),carpet,.012)
box('Centre_pedestal',(0,-.12,-.38),(.12,.38,.31),black,.010)
wheel=cyl('Trim_wheel',(-.078,-.13,-.272),.095,.016,black,axis='r');rig(wheel,'rotation','-Y',[-720,720],'elevatorTrim')
for i in range(24):
 a=math.tau*i/24;parent(box('Trim_grip',(-.078,-.13+math.sin(a)*.095,-.272+math.cos(a)*.095),(.019,.013,.012),ivory,.002),wheel)
rig(box('Trim_indicator',(-.086,-.037,-.202),(.002,.010,.005),white,0),'translation','+Z',[-.04,.04],'elevatorTrim');text('Trim_label','TRIM',(-.086,-.04,-.17),.007)
box('Fuel_selector_base',(0,-.29,-.51),(.17,.18,.055),black,.012)
q=box('Fuel_selector',(0,-.29,-.466),(.018,.095,.014),red,.004);rig(q,'rotation','+Z',[-90,180],'fuelSelector')
# Both fully modelled front seats, rear bench, seat tracks, belts, stitched upholstery.
# SEATS (Ty, 2026-11-04: "the pilot should sit at the back of the seat with his back against the backrest/backboard
# of the seat... move the seat forward so that is the case"). The seat used to stand where the 1.778 m mannequin at
# the approved eye floated 0.19 m in front of an upright back, buttocks 57 mm into the cushion. The eye does not
# move, so the seat comes to the body: forward SEAT_DF, cushion down SEAT_DZ, and the back reclined to the
# mannequin's own 12-degree torso, its insert face BACK_FACE behind the torso axis (torso radius 0.140: 3 mm into
# the foam; pelvis 0.150: 13 mm). The torso axis runs through the mannequin's H-point TORSO_HP, solved backwards
# from the eye by check_c172_pedal_fit.py (literal here; a change there has to be a deliberate change here).
# Moved this far the seat reaches the trim wheel and the fuel selector, so its inboard edge comes out to 0.098
# (width 0.43 -> 0.395, centre 17.5 mm outboard), and the door armrests end ahead of the reclined back. The tracks
# stay on the floor and run forward under the seat. Measured by check_c172_pedal_fit.py (seat-v1).
SEAT_DF,SEAT_DZ,SEAT_X,SEAT_W=.239,-.045,.2955,.395
BACK_RECLINE,BACK_FACE,TORSO_HP=12,.137,(-.2993,-.1561)          # degrees aft of vertical; m; (fwd, up)
BACK_U=(-math.sin(math.radians(BACK_RECLINE)),math.cos(math.radians(BACK_RECLINE)))   # up the back (fwd, up)
BACK_N=(BACK_U[1],-BACK_U[0])                                     # out of the back face, toward the pilot
_bu=-.206+SEAT_DZ-.029+.27*BACK_U[1]                              # back's bottom edge 29 mm under the cushion top
BACK_C=(TORSO_HP[0]+(-(BACK_FACE+.0695)-(_bu-TORSO_HP[1])*BACK_N[1])/BACK_N[0],_bu)   # insert face is 69.5 mm in front
def back_box(n,x,a,b,s,m,bevel):
 o=box(n,(x,BACK_C[0]+a*BACK_N[0]+b*BACK_U[0],BACK_C[1]+a*BACK_N[1]+b*BACK_U[1]),s,m,bevel);o.rotation_euler.y=-math.radians(BACK_RECLINE);return o
for side,x in [('L',-SEAT_X),('R',SEAT_X)]:
 f0=-.48+SEAT_DF
 for xx in [x-.16,x+.16]:box('Seat_track_'+side,(xx,-.39,-.526),(.022,.94,.018),metal,.002)
 box('Seat_base_'+side,(x,f0,-.4125),(SEAT_W,.46,.185),tan,.032)
 box('Seat_cushion_'+side,(x,f0,-.251+SEAT_DZ),(SEAT_W,.46,.09),ivory,.040)
 box('Seat_insert_'+side,(x,f0,-.201+SEAT_DZ),(.31,.34,.012),cloth,.009)
 back=back_box('Seat_back_'+side,x,0,0,(SEAT_W,.095,.54),tan,.040)
 back_box('Seat_back_insert_'+side,x,.059,.004,(.33,.021,.43),cloth,.025)
 for k in range(7):
  xx=x-.132+k*.044;back_box('Seat_stitch_'+side,xx,.073,.004,(.0015,.002,.38),ivory,.0005);box('Seat_cushion_pleat',(xx,f0,-.193+SEAT_DZ),(.0015,.30,.0015),ivory,.0004)
 for xx in [x-.175,x+.175]:
  box('Lap_belt_'+side,(xx,f0,-.190+SEAT_DZ),(.040,.32,.009),carpet,.002)
 box('Belt_buckle_'+side,(x+.145,f0+.05,-.183+SEAT_DZ),(.048,.043,.014),metal,.003)
 knob('Seat_adjust_'+side,x,-.31+SEAT_DZ,.010,f=-.69+SEAT_DF,kind='translation')
box('Rear_bench_base',(0,-1.22,-.33),(.94,.43,.29),tan,.035)
box('Rear_bench_cushion',(0,-1.22,-.155),(.94,.45,.08),cloth,.035)
box('Rear_bench_back',(0,-1.48,.065),(.94,.11,.46),tan,.035)
box('Rear_bench_insert',(0,-1.414,.070),(.84,.018,.35),cloth,.020)
for i in range(19):box('Rear_pleat',(-.405+i*.045,-1.402,.070),(.0015,.002,.32),ivory,.0004)
for x in [-.29,-.19,.19,.29]:box('Rear_lap_belt',(x,-1.22,-.106),(.036,.34,.008),carpet,.002)
box('Baggage_shelf',(0,-1.65,.18),(.88,.23,.035),carpet,.008)
# Sculpted door cards below window line; matching headliner and pillars.
for side,x in [('L',-.516),('R',.516)]:
 box('Door_'+side,(x,-.42,-.155),(.038,1.17,.66),ivory,.025)
 box('Door_insert_'+side,(x*.955,-.42,-.12),(.014,.91,.35),cloth,.012)
 box('Door_armrest_'+side,(x*.89,-.325,-.057),(.105,.23,.058),tan,.022)   # ends ahead of the reclined seat back (seat-v1)
 for i in range(10):box('Door_pleat',(x*.932,-.84+i*.088,-.14),(.003,.002,.27),ivory,.0005)
 line('Door_pull_'+side,(x*.91,-.13,.065),(x*.91,-.35,.065),.013,tan)
 q=line('Door_latch_'+side,(x*.925,.03,.073),(x*.925,-.06,.073),.008,metal);rig(q,'rotation','+Z',[0,40],'door.'+side+'.latch')
 q=box('Window_latch_'+side,(x*.93,-.18,.205),(.022,.055,.013),metal,.003);rig(q,'rotation','+Z',[0,70],'window.'+side+'.latch')
 line('A_pillar_'+side,(x,.25,.19),(x*.80,.03,.60),.028,ivory)
 line('B_pillar_'+side,(x,-.98,-.01),(x*.96,-.98,.60),.025,ivory)
box('Headliner',(0,-.73,.603),(1.00,1.60,.032),ivory,.04)
for f in [-.10,-.50,-.95,-1.35]:box('Headliner_seam',(0,f,.582),(.94,.004,.003),tan,.001)
box('Dome_light',(0,-.77,.574),(.095,.07,.016),white,.008)
for side,x in [('L',-.28),('R',.28)]:
 q=box('Sunvisor_'+side,(x,.035,.548),(.32,.035,.12),tan,.006);rig(q,'rotation','-Y',[0,100],'visor.'+side)
# Set all moving pivots and hierarchy in durable export metadata.
(ROOT/'docs/models').mkdir(exist_ok=True)

# Save review interior independently; export actual authored mesh, no raster mock-ups.
bpy.ops.object.select_all(action='DESELECT')
for o in CREATED:o.select_set(True)
# Bake mesh orientations so axes in the rig table are parent-space axes.
bpy.ops.object.convert(target='MESH')
bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
# Keep the upper panel and glareshield inside the measured rounded fuselage section.
for o in CREATED:
 if o.name=='C172_Glareshield':
  for v in o.data.vertices:v.co.y*=.98/1.08
 elif o.name=='C172_Panel':
  for v in o.data.vertices:
   if abs(v.co.y)>.485:v.co.y=math.copysign(.485+(abs(v.co.y)-.485)*(.025/.035),v.co.y)

bpy.context.view_layer.update()
for e in RIG:
 o=bpy.data.objects[e['node']]
 # Hinge locations rather than visual centres on pedals, visors and door latches.
 delta=Vector((0,0,0))
 if o.name in HINGE:delta=Vector(HINGE[o.name])-o.matrix_world.translation
 elif '_Sunvisor_' in o.name:delta.z=.060
 if delta.length:
  children=[(c,c.matrix_world.copy()) for c in o.children]
  for v in o.data.vertices:v.co-=delta
  o.location+=delta
  bpy.context.view_layer.update()
  for c,w in children:c.matrix_world=w
 e['pivot_blender_m']=list(o.matrix_world.translation)
 o['rig']=json.dumps(e)
# Export measured label geometry with the static mesh for the headless checker.
bpy.context.view_layer.update()
label_audit=[]
for o,body,size in LABELS:
 coords=[o.matrix_world@v.co for v in o.data.vertices]
 label_audit.append(dict(name=o.name,text=body,cap_height_m=size*.729,
     forward_m=sum(v.x for v in coords)/len(coords),
     depth_m=max(v.x for v in coords)-min(v.x for v in coords)))
# Consolidate detail that does not move. Preserve semantic shells and all rig descendants.
rig_names={e['node'] for e in RIG}
for e in RIG:
 target=bpy.data.objects[e['node']]
 children=[o for o in target.children if o.type=='MESH' and o.name not in rig_names and not o.children]
 if children:
  bpy.ops.object.select_all(action='DESELECT');target.select_set(True)
  for o in children:o.select_set(True)
  bpy.context.view_layer.objects.active=target;bpy.ops.object.join()
keep={'Seat_base_L','Seat_base_R','Rear_bench_base','Door_L','Door_R','Headliner','Compass_mount','KX170B_1','KX170B_2','KR85_ADF','DME'}
static=[o for o in bpy.context.scene.objects if o.name.startswith('C172_') and o.type=='MESH' and not o.parent and o.name not in rig_names and o.name.removeprefix('C172_') not in keep and not o.name.endswith('_case') and not o.name.startswith('C172_TC_rate_mark')]
if static:
 bpy.ops.object.select_all(action='DESELECT')
 for o in static:o.select_set(True)
 bpy.context.view_layer.objects.active=static[0];bpy.ops.object.join();static[0].name='C172_Interior_static';static[0]['printed_labels']=json.dumps(label_audit)
CREATED=[o for o in bpy.context.scene.objects if o.name.startswith('C172_')]
bpy.ops.object.select_all(action='DESELECT')
for o in CREATED:o.select_set(True)
(ROOT/'docs/models/c172-panel-bindings.json').write_text(json.dumps(dict(version=1,frame='Blender +X forward +Y left +Z up; exported glTF +X forward +Y up +Z right',bindings=RIG),indent=2)+'\n')
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'c172-interior.blend'))
bpy.ops.export_scene.gltf(filepath=str(OUT/'c172-interior.glb'),export_format='GLB',use_selection=True,export_extras=True)
# Optional renders avoid changing airframe geometry or hiding cockpit furniture.
if os.environ.get('C172_RENDER')=='1':
 scene=bpy.context.scene
 # Only authored cabin is visible in these review views; exterior shell not imported/required.
 for o in scene.objects:
  if o not in CREATED:o.hide_render=True
 scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=32
 if scene.world is None:scene.world=bpy.data.worlds.new('172_ReviewWorld')
 scene.world.color=(.42,.42,.42);scene.render.resolution_x=1800;scene.render.resolution_y=1200;scene.render.resolution_percentage=100
 scene.view_settings.view_transform='AgX'
 for n,p,power,size in [('Key',(-1,-.7,1.7),160,2),('Fill',(1,-.2,1.1),110,1.4),('Windshield',(0,1,1.2),180,1.2)]:
  bpy.ops.object.light_add(type='AREA',location=xyz(p));o=bpy.context.object;o.name=n;o.data.energy=power;o.data.shape='DISK';o.data.size=size;o.rotation_euler=(Vector(xyz((0,-.3,.05)))-o.location).to_track_quat('-Z','Y').to_euler()
 views=[('full-panel-straight',(0,-.60,.22),(0,.29,.17),26),('pilot-eye',(-.337,-.346,.548),(-.10,.32,.20),22),('cfi-right-seat',(.332,-.346,.548),(-.09,.32,.17),22),('radio-stack',(.095,-.29,.27),(.095,.28,.205),35),('nav-heads',(NAV_X,-.21,.28),(NAV_X,.27,.265),55),('cabin-wide',(0,.20,.56),(0,-.95,-.10),18)]
 if OUT.name=='instruments-v3':
  views=[('full-panel-pilot-eye',(-.337,-.346,.548),(-.03,.30,.17),19),
   ('six-pack',(-.321,-.20,.29),(-.321,.30,.27),44),
   ('attitude',(-.321,.08,.323),(-.321,.30,.323),55),
   ('directional-gyro',(-.321,.08,.218),(-.321,.30,.218),55),
   ('turn-coordinator',(-.423,.08,.218),(-.423,.30,.218),55),
   ('throttle-mixture',(.065,-.15,.065),(.065,.22,-.04),55),
   ('low-side-flush',(-.10,.05,.19),(-.35,.30,.29),45)]
 cameras=[]
 only=set(filter(None,os.environ.get('C172_RENDER_ONLY','').split(',')))
 for n,e,t,lens in views:
  bpy.ops.object.camera_add(location=xyz(e));cam=bpy.context.object;cam.rotation_euler=(Vector(xyz(t))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.lens=lens;cam.data.clip_start=.01;scene.camera=cam
  scene.render.filepath=str(OUT/(n+'.png'));(bpy.ops.render.render(write_still=True) if not only or n in only else None);cameras.append(dict(path=n+'.png',eye_design_m=e,target_design_m=t,lens_mm=lens))
 for c in cameras:
  if (OUT/c['path']).exists():c['sha256']=hashlib.sha256((OUT/c['path']).read_bytes()).hexdigest()
 (OUT/'render-manifest.json').write_text(json.dumps(dict(renderer='Blender Cycles CPU, 32 samples',interior_sha256=hashlib.sha256((OUT/'c172-interior.glb').read_bytes()).hexdigest(),scope='Authored interior only; exterior airframe absent from review renders',cameras=cameras),indent=2)+'\n')
print('C172 cabin:',len(CREATED),'objects,',len(RIG),'moving bindings')
