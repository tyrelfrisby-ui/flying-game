"""Original primitive-built late-172M cabin. Run via model_c172.py or Blender -b --python this_file.
Design frame: (right, forward, up); mapped to Blender (+forward,+left,+up), metres.
"""
import bpy, math, json, os, hashlib
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'renders/c172/v1'; OUT.mkdir(parents=True,exist_ok=True)
RIG=[]; CREATED=[]
def xyz(p): return (p[1],-p[0],p[2])
def mat(n,c,metal=0):
 m=bpy.data.materials.new('172_'+n);m.diffuse_color=(*c,1);m.use_nodes=True
 bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(*c,1);bs.inputs['Metallic'].default_value=metal;bs.inputs['Roughness'].default_value=.48 if metal else .76
 return m
black=mat('charcoal',(.018,.022,.021)); face=mat('instrument_black',(.006,.009,.009)); ivory=mat('warm_ivory',(.73,.65,.48)); white=mat('markings',(.85,.87,.75)); metal=mat('brushed_metal',(.30,.33,.32),.7); tan=mat('saddle_vinyl',(.29,.13,.055)); cloth=mat('woven_ochre',(.48,.31,.14)); carpet=mat('brown_carpet',(.065,.043,.025)); red=mat('mixture_red',(.52,.025,.012)); orange=mat('amber_digits',(.95,.36,.045)); blue=mat('gyro_sky',(.09,.31,.48)); earth=mat('gyro_earth',(.30,.15,.065)); green=mat('normal_arc',(.04,.38,.13)); yellow=mat('caution',(.85,.57,.04))
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
 cu=bpy.data.curves.new(n,'FONT');cu.body=t;cu.align_x='CENTER';cu.align_y='CENTER';cu.size=size;cu.extrude=0
 o=bpy.data.objects.new('C172_'+n,cu);bpy.context.collection.objects.link(o);o.location=xyz(p)
 # local text X = right (-world Y), Y = up; front normal = aft (-world X)
 from mathutils import Matrix
 o.rotation_euler=Matrix(((0,0,-1),(-1,0,0),(0,1,0))).to_euler();o.data.materials.append(m);CREATED.append(o);return o
def rig(o,kind,axis,limits,signal):
 entry=dict(node=o.name,kind=kind,axis=axis,range=limits,signal=signal,pivot_blender_m=list(o.location));o['rig']=json.dumps(entry);RIG.append(entry);return o
def parent(child,par):
 bpy.context.view_layer.update()
 world=child.matrix_world.copy();child.parent=par;child.matrix_world=world
 return child
def needle(n,x,z,angle=0,length=.028,m=white,f=.251):
 a=math.radians(angle);o=box(n,(x,f,z),(.002,.001,length),m,.0003)
 # Put origin on gauge centre, mesh extends upwards at zero.
 for v in o.data.vertices:v.co.z+=length*.35
 o.rotation_euler.x=-a
 return rig(o,'rotation','+X forward (Blender)',[-180,180],n)
def knob(n,x,z,r=.008,f=.265,kind='rotation',signal=None,m=black):
 o=cyl(n,(x,f,z),r,.018,m);rig(o,kind,'+X forward (Blender)',[0,360] if kind=='rotation' else [0,.06],signal or n)
 stripe=box(n+'_index',(x,f-.010,z+r*.5),(.002,.001,r*.5),white,.0002);parent(stripe,o);return o
# Width 1.02m, independent 3-1/8 inch instrument cases at 102mm centres.
box('Panel',(0,.32,.185),(1.04,.045,.405),black,.035)
box('LowerPanel',(0,.30,-.025),(1.00,.045,.095),ivory,.012)
box('Glareshield',(0,.34,.405),(1.08,.25,.036),black,.025)
for xx in [-.485,-.05,.49]:
 for zz in [.025,.365]:cyl('Panel_screw',(xx,.293,zz),.003,.002,metal)
def dial(n,x,z,r=.040,labels=None):
 cyl(n+'_case',(x,.286,z),r+.004,.018,metal);cyl(n+'_bezel',(x,.274,z),r+.002,.010,black);cyl(n+'_face',(x,.267,z),r,.002,face)
 for i in range(60):
  if n=='Turn_coordinator' or (n=='Attitude' and i not in [0,5,10,50,55]):continue
  a=math.tau*i/60;rr=r*.88;l=r*(.14 if i%5==0 else .065)
  line(n+'_tick', (x+math.sin(a)*rr,.264,z+math.cos(a)*rr),(x+math.sin(a)*(rr-l),.264,z+math.cos(a)*(rr-l)),.0005,white)
 if labels:
  for i,t in enumerate(labels):
   a=math.tau*i/len(labels);text(n+'_numeral',str(t),(x+math.sin(a)*r*.64,.261,z+math.cos(a)*r*.64),r*.20)
 text(n+'_label',n.upper().replace('_',' '),(x,.260,z-r*.38),r*.14)
 return (x,z)
# Pilot primary six-pack, clear of the separate nav heads.
for n,x,z,labels in [('Airspeed',-.423,.323,list(range(0,200,20))),('Attitude',-.321,.323,None),('Altimeter',-.219,.323,list(range(10))),('Turn_coordinator',-.423,.218,None),('DG',-.321,.218,['N',3,6,'E',12,15,'S',21,24,'W',30,33]),('VSI',-.219,.218,[0,5,10,15,20,15,10,5])]:
 dial(n,x,z,labels=labels)
 if n=='Attitude':
  # Gimballed hemispheric horizon: circular presentation, independent roll and pitch.
  o=cyl('AI_roll',(x,.267,z),.030,.001,blue);rig(o,'rotation','+X',[-180,180],'attitude.roll')
  bpy.ops.mesh.primitive_uv_sphere_add(segments=48,ring_count=24,radius=.0295,location=xyz((x,.267,z)))
  q=finish(bpy.context.object,'AI_pitch',blue);q.data.materials.append(earth)
  for poly in q.data.polygons:
   poly.material_index=1 if poly.center.z<0 else 0;poly.use_smooth=True
  rig(q,'rotation','-Y',[-25,25],'attitude.pitch');parent(q,o)
  for zz in [-.016,-.008,0,.008,.016]:
   parent(line('AI_pitch_mark',(x-.009,.234,z+zz),(x+.009,.234,z+zz),.00045,white),q)
  for side in [-1,1]:box('AI_fixed_airplane',(x+side*.016,.231,z),(.022,.001,.002),orange,.0002)
  knob('AI_cage',x,z-.035,r=.004);text('Vacuum','VAC',(x,.230,z+.022),.005)
 elif n=='DG':
  # The compass rose itself rotates; fixed aircraft remains on the face.
  o=cyl('DG_card',(x,.259,z),.030,.001,face);rig(o,'rotation','+X',[0,360],'heading.nonSlavedDeg')
  for i,t in enumerate(['N',3,6,'E',12,15,'S',21,24,'W',30,33]):
   a=math.tau*i/12;parent(text('DG_card_text',str(t),(x+math.sin(a)*.024,.257,z+math.cos(a)*.024),.006),o)
  box('DG_aircraft',(x,.254,z),(.022,.001,.002),white,0);box('DG_aircraft_body',(x,.254,z),(.002,.001,.021),white,0)
  bug=box('DG_heading_bug',(x,.251,z),(.006,.001,.006),orange,.0005)
  for v in bug.data.vertices:v.co.z+=.031
  rig(bug,'rotation','+X',[0,360],'headingBugDeg');knob('DG_sync_adjust',x-.027,z-.030,.005);knob('DG_bug_adjust',x+.027,z-.030,.005)
 elif n=='Turn_coordinator':
  q=box('TC_airplane',(x,.254,z+.004),(.045,.001,.003),white,0);parent(box('TC_fuselage',(x,.253,z+.004),(.003,.001,.017),white,0),q);rig(q,'rotation','+X',[-30,30],'turn.rate')
  box('TC_ball_track',(x,.258,z-.020),(.039,.002,.007),ivory,.003)
  q=cyl('TC_slip_ball',(x,.253,z-.020),.003,.002,black);rig(q,'translation','-Y',[-.015,.015],'slip');text('TC_electric','ELEC   2 MIN',(x,.250,z+.021),.005)
 else:
  if n=='Airspeed':
   for lo,hi,rr,ma in [(49,100,.030,white),(57,128,.033,green),(128,164,.033,yellow)]:
    for v in range(lo,hi,2):
     a=math.tau*v/200;b=math.tau*(v+2)/200
     line('ASI_speed_arc',(x+math.sin(a)*rr,.253,z+math.cos(a)*rr),(x+math.sin(b)*rr,.253,z+math.cos(b)*rr),.0012,ma)
   text('ASI_units','MPH',(x,.249,z+.010),.005)
  needle(n+'_needle',x,z,55 if n=='Airspeed' else 0)
  if n=='Altimeter':
   needle('Altimeter_1000',x,z,20,.019);needle('Altimeter_10000',x,z,5,.012);knob('Altimeter_baro',x-.027,z-.030,.005);text('Baro_window','29.92',(x+.015,.253,z-.014),.005)
# Two distinct nav heads and ADF, not an HSI.
for idx,z in [(1,.323),(2,.218)]:
 x=-.112;dial('NAV'+str(idx),x,z,.039,['N',3,6,'E',12,15,'S',21,24,'W',30,33])
 rose=cyl('NAV%d_OBS_card'%idx,(x,.258,z),.027,.001,face);rig(rose,'rotation','+X',[0,360],'nav%d.obsDeg'%idx)
 for i,t in enumerate(['N','E','S','W']):
  a=i*math.pi/2;parent(text('OBS_card',t,(x+math.sin(a)*.023,.255,z+math.cos(a)*.023),.006),rose)
 for j in [-2,-1,0,1,2]:cyl('CDI_dot',(x+j*.007,.250,z),.001,.001,white)
 q=box('NAV%d_CDI'%idx,(x,.248,z),(.0013,.001,.038),white,0);rig(q,'translation','-Y',[-.018,.018],'nav%d.cdiNormalized'%idx)
 if idx==1:
  q=box('NAV1_GS',(x,.246,z),(.038,.001,.0013),orange,0);rig(q,'translation','+Z',[-.018,.018],'nav1.gsNormalized')
 knob('NAV%d_OBS'%idx,x-.026,z-.030,.005)
 for flag in ['NAV','TO_FROM']+(['GS'] if idx==1 else []):
  q=text('NAV%d_%s_flag'%(idx,flag),flag,(x+.013,.245,z+.012+(['NAV','TO_FROM','GS'].index(flag)*.007)),.0045,red);rig(q,'rotation','+X',[0,90],'nav%d.%s'%(idx,flag))
x=-.112;z=.111;dial('ADF',x,z,.039,['N',3,6,'E',12,15,'S',21,24,'W',30,33]);needle('ADF_bearing',x,z,35,.028,orange,.252);knob('ADF_card_adjust',x-.026,z-.03,.005)
# Engine/systems cluster on right, grouped as actual separate instruments.
for n,x,z,r,labs in [('Tach',.303,.314,.042,[0,5,10,15,20,25,30,35]),('Suction',.443,.315,.026,[0,2,4,6,8]),('Oil_press',.264,.213,.026,[0,25,50,75,100]),('Oil_temp',.335,.213,.026,[75,100,150,200,245]),('Fuel_L',.264,.139,.026,['E','1/4','1/2','3/4','F']),('Fuel_R',.335,.139,.026,['E','1/4','1/2','3/4','F']),('Ammeter',.443,.213,.026,[-60,-30,0,30,60]),('EGT',.443,.139,.026,[8,10,12,14,16])]:
 dial(n,x,z,r,labs);needle(n+'_needle',x,z,35,r*.7,f=.251)
 if n=='Tach':text('tach_hours','02483.6',(x,.249,z-.019),.007)
# Avionics stack: independent mechanically tuned COM/NAV radios, ADF receiver and DME.
for n,z,h in [('Audio',.366,.028),('KX170B_1',.307,.075),('KX170B_2',.224,.075),('KR85_ADF',.151,.060),('DME',.094,.042),('KT76',.042,.048)]:
 x=.095;box(n,(x,.280,z),(.177,.036,h),black,.003)
 for sx in [-1,1]:cyl(n+'_rack_screw',(x+sx*.083,.259,z+h*.36),.002,.002,metal)
 if n.startswith('KX'):
  text(n+'_type','KING KX 170B',(x,.257,z+h*.36),.005)
  for off,freq,label in [(-.042,'122.80','COMM'),(.040,'110.30' if n.endswith('1') else '114.60','NAV')]:
   box(n+'_window',(x+off,.255,z+.007),(.072,.002,.022),face,.002);text(n+'_frequency',freq,(x+off,.253,z+.007),.012,ivory);text(n+'_section',label,(x+off,.252,z+.025),.004)
   outer=knob(n+'_'+label+'_coarse',x+off,z-.023,.009,f=.249);inner=knob(n+'_'+label+'_fine',x+off,z-.023,.005,f=.235);parent(inner,outer)
   knob(n+'_'+label+'_volume',x+off-.025,z-.023,.004,f=.248)
 elif n=='KR85_ADF':
  text(n+'_label','KING KR 85   ADF',(x,.253,z+.021),.006);text(n+'_freq','0350',(x,.252,z+.003),.014,ivory)
  for off in [-.060,.060]:knob(n+('_mode' if off<0 else '_tune'),x+off,z-.012,.007,f=.245)
 elif n=='DME':
  text(n+'_readout','042.6  NM',(x-.018,.253,z+.006),.012,orange);text(n+'_type','DME',(x-.050,.252,z-.010),.005)
  knob('DME_channel',x+.063,z,.006,f=.246);knob('DME_mode',x+.040,z-.013,.004,f=.246)
 elif n=='KT76':
  text(n+'_readout','1 2 0 0',(x,.253,z+.006),.013,ivory)
  for i in range(4):knob('XPDR_digit'+str(i),x-.045+i*.028,z-.014,.004,f=.246)
  knob('XPDR_mode',x+.070,z,.006,f=.246)
 else:
  for i,t in enumerate(['COM1','COM2','NAV1','NAV2','ADF','SPKR']):
   xx=x-.070+i*.028;text('audio_label',t,(xx,.252,z+.006),.0035);rig(box('Audio_'+t,(xx,.247,z-.006),(.008,.008,.007),ivory,.001),'rotation','-Y',[0,25],'audio.'+t)
# Lower switches, starter, primer and three proper push-pull engine controls.
for i,n in enumerate(['Master_BAT','Master_ALT','Beacon','Landing','Taxi','Nav_lights','Pitot_heat']):
 xx=-.46+i*.039;rig(box(n,(xx,.261,-.019),(.020,.015,.025),red if i<2 else black,.002),'rotation','-Y',[0,25],n);text(n+'_label',n.replace('Master_',''),(xx,.249,-.045),.004,black)
knob('Mags_key',-.43,.070,.012,f=.255);text('ignition_label','OFF R L BOTH START',(-.43,.244,.089),.004)
knob('Primer',-.355,.069,.008,kind='translation',f=.255)
for n,x,m in [('Carb_heat',-.040,black),('Throttle',.035,black),('Mixture',.112,red)]:
 cyl(n+'_shaft',(x,.215,-.041),.004,.13,metal);knob(n,x,-.041,.013,f=.151,kind='translation',m=m);text(n+'_label',n.upper().replace('_',' '),(x,.253,-.066),.005,black)
for n,x in [('Cabin_heat',.255),('Cabin_air',.315)]:knob(n,x,-.030,.008,f=.25,kind='translation');text(n+'_label',n.upper().replace('_',' '),(x,.239,-.052),.0045,black)
rig(box('Flap_switch',(.403,.245,-.020),(.012,.024,.014),ivory,.002),'rotation','-Y',[0,40],'flap.command');text('flap_label','FLAPS  0 10 20 30 40',(.420,.244,-.048),.005,black)
rig(box('Flap_indicator',(.458,.253,-.016),(.010,.002,.004),white,0),'translation','+Z',[0,.023],'flap.position')
# Whiskey compass, mounted above glareshield; no magnetic slaving to DG.
box('Compass_mount',(0,.37,.445),(.075,.065,.055),black,.008);text('Compass_card','W  27  30  N',(0,.334,.448),.009)
rig(CREATED[-1],'rotation','+Z',[0,360],'magneticHeading');box('Compass_lubber',(0,.332,.448),(.001,.001,.020),orange,0)
# Dual control yokes and all four rudder pedals, independent toe brakes.
for side,x in [('L',-.30),('R',.30)]:
 col=cyl('Yoke_'+side+'_pitch',(x,.125,.087),.013,.27,metal);rig(col,'translation','+X',[-.07,.07],'elevator')
 yoke=box('Yoke_'+side+'_roll',(x,-.025,.087),(.120,.046,.066),black,.016);rig(yoke,'rotation','+X',[-90,90],'aileron');parent(yoke,col)
 for s in [-1,1]:
  a=line('Yoke_'+side+'_bar',(x,-.030,.066),(x+s*.135,-.030,.045),.012,black);parent(a,yoke)
  a=line('Yoke_'+side+'_grip',(x+s*.135,-.030,.045),(x+s*.135,-.030,.142),.014,black);parent(a,yoke)
 parent(text('Yoke_badge','SKYHAWK',(x,-.050,.088),.009,ivory),yoke)
 for s,tag in [(-1,'L'),(1,'R')]:
  xx=x+s*.083;o=box('Pedal_'+side+'_'+tag,(xx,.30,-.425),(.125,.042,.075),black,.005);rig(o,'rotation','-Y',[-18,18],'rudder');
  toe=box('Toe_brake_'+side+'_'+tag,(xx,.275,-.399),(.11,.012,.027),metal,.002);rig(toe,'rotation','-Y',[0,15],'brake.'+tag);parent(toe,o)
  for k in range(5):parent(box('Pedal_rib',(xx-.045+k*.022,.271,-.431),(.004,.004,.039),metal,.001),o)
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
for side,x in [('L',-.278),('R',.278)]:
 for xx in [x-.16,x+.16]:box('Seat_track_'+side,(xx,-.47,-.526),(.022,.78,.018),metal,.002)
 box('Seat_base_'+side,(x,-.48,-.39),(.43,.46,.23),tan,.032)
 box('Seat_cushion_'+side,(x,-.48,-.251),(.43,.46,.09),ivory,.040)
 box('Seat_insert_'+side,(x,-.48,-.201),(.31,.34,.012),cloth,.009)
 back=box('Seat_back_'+side,(x,-.728,.035),(.43,.095,.54),tan,.040)
 box('Seat_back_insert_'+side,(x,-.669,.039),(.33,.021,.43),cloth,.025)
 for k in range(7):
  xx=x-.132+k*.044;box('Seat_stitch_'+side,(xx,-.655,.039),(.0015,.002,.38),ivory,.0005);box('Seat_cushion_pleat',(xx,-.48,-.193),(.0015,.30,.0015),ivory,.0004)
 for xx in [x-.19,x+.19]:
  box('Lap_belt_'+side,(xx,-.48,-.190),(.040,.32,.009),carpet,.002)
 box('Belt_buckle_'+side,(x+.16,-.43,-.183),(.048,.043,.014),metal,.003)
 knob('Seat_adjust_'+side,x,-.31,.010,f=-.69,kind='translation')
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
 box('Door_armrest_'+side,(x*.89,-.44,-.057),(.105,.46,.058),tan,.022)
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
bpy.context.view_layer.update()
for e in RIG:
 o=bpy.data.objects[e['node']]
 # Hinge locations rather than visual centres on pedals, visors and door latches.
 delta=Vector((0,0,0))
 if '_Pedal_' in o.name:delta.z=-.0375
 elif '_Toe_brake_' in o.name:delta.z=-.0135
 elif '_Sunvisor_' in o.name:delta.z=.060
 if delta.length:
  children=[(c,c.matrix_world.copy()) for c in o.children]
  for v in o.data.vertices:v.co-=delta
  o.location+=delta
  bpy.context.view_layer.update()
  for c,w in children:c.matrix_world=w
 e['pivot_blender_m']=list(o.matrix_world.translation)
 o['rig']=json.dumps(e)
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
static=[o for o in bpy.context.scene.objects if o.name.startswith('C172_') and o.type=='MESH' and not o.parent and o.name not in rig_names and o.name.removeprefix('C172_') not in keep and not o.name.endswith('_case')]
if static:
 bpy.ops.object.select_all(action='DESELECT')
 for o in static:o.select_set(True)
 bpy.context.view_layer.objects.active=static[0];bpy.ops.object.join();static[0].name='C172_Interior_static'
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
 views=[('full-panel-straight',(0,-.60,.22),(0,.29,.17),26),('pilot-eye',(-.337,-.346,.548),(-.10,.32,.20),22),('cfi-right-seat',(.332,-.346,.548),(-.09,.32,.17),22),('radio-stack',(.095,-.29,.27),(.095,.28,.205),35),('nav-heads',(-.112,-.21,.28),(-.112,.27,.265),55),('cabin-wide',(0,.20,.56),(0,-.95,-.10),18)]
 cameras=[]
 only=set(filter(None,os.environ.get('C172_RENDER_ONLY','').split(',')))
 for n,e,t,lens in views:
  bpy.ops.object.camera_add(location=xyz(e));cam=bpy.context.object;cam.rotation_euler=(Vector(xyz(t))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.lens=lens;cam.data.clip_start=.01;scene.camera=cam
  scene.render.filepath=str(OUT/(n+'.png'));(bpy.ops.render.render(write_still=True) if not only or n in only else None);cameras.append(dict(path=n+'.png',eye_design_m=e,target_design_m=t,lens_mm=lens))
 for c in cameras:
  if (OUT/c['path']).exists():c['sha256']=hashlib.sha256((OUT/c['path']).read_bytes()).hexdigest()
 (OUT/'render-manifest.json').write_text(json.dumps(dict(renderer='Blender Cycles CPU, 32 samples',interior_sha256=hashlib.sha256((OUT/'c172-interior.glb').read_bytes()).hexdigest(),scope='Authored interior only; exterior airframe absent from review renders',cameras=cameras),indent=2)+'\n')
print('C172 cabin:',len(CREATED),'objects,',len(RIG),'moving bindings')
