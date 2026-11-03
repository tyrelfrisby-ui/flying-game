"""Original tandem trainer interior. Layout estimates keyed to manual pp17-20.
No position displays. Author axes: +Y is pilot's LEFT.
"""
import math
import numpy as np

def build_cockpit(b):
 from build_grob103 import canopy,section
 for seat,x,zeye,px in [('F',1.15,.18,1.63),('R',-.04,.26,.53)]:
    b.box('Seat_Pan_'+seat,(x-.06,0,-.29),(.35,.40,.065),'seat')
    b.box('Seat_Back_'+seat,(x-.27,0,-.08),(.065,.40,.44),'seat')
    b.box('Headrest_'+seat,(x-.30,0,.21),(.09,.20,.13),'seat')
    for sg in [-1,1]:
        b.box(f'Shoulder_Harness_{seat}_{sg}',(x-.23,sg*.105,-.02),(.012,.045,.34),'belt')
        b.box(f'Lap_Harness_{seat}_{sg}',(x+.02,sg*.12,-.247),(.055,.22,.012),'belt')
    b.box('Buckle_'+seat,(x+.02,0,-.230),(.065,.060,.022),'metal')
    # Rounded-top panel shield, three primary instruments and separate compass front.
    z=0.0 if seat=='F' else .18
    outline=[(-.25,z-.15),(.25,z-.15),(.25,z+.08),(.20,z+.17),(.11,z+.20),(-.11,z+.20),(-.20,z+.17),(-.25,z+.08)]
    b.loft('Panel_'+seat,[[(xx,y,zz) for y,zz in outline] for xx in [px,px+.028]],'panel')
    layout=[('ASI',.145,z+.065,'asi'),('Vario',-.145,z+.065,'vario'),('Alt100',0,z-.045,'alt')]
    if seat=='F':layout.append(('Compass',0,z+.17,'compass'))
    for instrument,y,zz,mat in layout:
        r=.040 if mat!='compass' else .029
        b.tube('Bezel_'+instrument+'_'+seat,(px-.012,y,zz),(px-.002,y,zz),r+.005,'panel')
        pts=[(px-.014,y+r*math.sin(t),zz+r*math.cos(t)) for t in np.linspace(0,2*math.pi,64,endpoint=False)]
        uv=[(.5-.5*math.sin(t),.5-.5*math.cos(t)) for t in np.linspace(0,2*math.pi,64,endpoint=False)]
        b.mesh('Dial_'+instrument+'_'+seat,pts,[tuple(range(64))],mat,uv=uv)
        if mat=='compass':
            # A rotating magnetic card, kept separate from its case.
            name='Compass_Card';p=[px-.014,y,zz]
            node=next(n for n in b.g.doc['nodes'] if n['name']=='Dial_'+instrument+'_'+seat)
            # Rebase face vertices onto its card centre without deleting graph nodes.
            prim=b.g.doc['meshes'][node['mesh']]['primitives'][0]
            from build_grob103 import cv
            v=b.g.array(prim['attributes']['POSITION'])-cv(p)
            prim['attributes']['POSITION']=b.g.accessor(v,'VEC3');node['name']=name;node['translation']=cv(p).tolist()
            b.rigpart(name,p,[-1,0,0],'heading_deg',[0,360]);b.rig[-1]['map']=[[0,0],[360,360]];node['extras']['rig_map_param']=[0,360];node['extras']['rig_map_wrap']=360;b.rig[-1]['extra']['rig_map_wrap']=360;b.rig[-1]['unit']='deg magnetic'
            continue
        p=[px-.017,y,zz];name='Needle_'+instrument+'_'+seat
        a=math.radians(20 if mat=='asi' else -90 if mat=='vario' else 0)
        tip=np.array(p)+[0,-r*.79*math.sin(a),r*.79*math.cos(a)]
        b.mesh(name,[p,tip+[0,-.002*math.cos(a),-.002*math.sin(a)],tip+[0,.002*math.cos(a),.002*math.sin(a)]],[(0,1,2)],'white',pivot=p)
        param,vals= ('airspeed_kias',[0,320]) if mat=='asi' else ('altitude_ft',[0,360]) if mat=='alt' else ('vario_kt',[-150,0,150])
        b.rigpart(name,p,[1,0,0],param,vals)
        node=next(n for n in b.g.doc['nodes'] if n['name']==name)
        if mat=='asi':node['extras']['rig_map_param']=[0,280/1.852]
        if mat=='alt':node['extras']['rig_map_param']=[0,1000];node['extras']['rig_map_extrapolate']=1;b.rig[-1]['extra']['rig_map_extrapolate']=1
        if mat=='vario':node['extras']['rig_map_param']=[-10,0,10]
        b.rig[-1]['map']=[list(q) for q in zip(node['extras']['rig_map_param'],vals)];b.rig[-1]['unit']={'asi':'kt IAS (art km/h)','alt':'ft','vario':'kt'}[mat]
        if mat=='alt':
            p=[px-.020,y,zz];name='Needle_Alt1000_'+seat
            b.mesh(name,[p,(px-.020,y-.003,zz+r*.46),(px-.020,y+.003,zz+r*.46)],[(0,1,2)],'white',pivot=p)
            b.rigpart(name,p,[1,0,0],'altitude_ft',[0,360]);b.rig[-1]['map']=[[0,0],[10000,360]]
            node=next(n for n in b.g.doc['nodes'] if n['name']==name);node['extras']['rig_map_param']=[0,10000];node['extras']['rig_map_extrapolate']=1;b.rig[-1]['extra']['rig_map_extrapolate']=1;b.rig[-1]['unit']='ft'
    # Stick origin at mechanical pitch hinge. Roll linkage is an integration gap.
    p=[x+.13,0,-.36];n='Stick_'+seat
    b.tube(n,p,[x+.18,0,-.055],.012,'metal',p)
    b.rigpart(n,p,[0,1,0],'elevator',[-15,0,18])
    b.tube('Stick_Grip_'+seat,[x+.18,0,-.10],[x+.18,0,-.025],.019,'panel');b.attach('Stick_Grip_'+seat,n)
    # Pedals in front of panel; front seat cannot hide rear pedals from rear eye.
    for sg,sd in [(1,'L'),(-1,'R')]:
        p=[px+.10,sg*.135,-.29];n='Pedal_'+seat+'_'+sd
        b.box(n,p,(.065,.11,.055),'metal',p);b.rigpart(n,p,[-sg,0,0],'rudder',[-.045,0,.045],'move')
    # LEFT wall = +Y. Deliberate separated coloured controls, not a merged quadrant.
    for stem,dx,zz,col,param,vals in [('DiveBrake_Lever',.05,-.06,'blue','spoiler',[0,32]),('Trim_Lever',.24,-.16,'green','trim',[-18,0,18])]:
        p=[x+dx,.26,zz-.08];n=stem+'_'+seat
        b.tube(n,p,[x+dx,.26,zz],.013,col,p)
        b.rigpart(n,p,[0,1,0],param,vals)
        w,sill,roof=canopy(x+dx);bt=section(x+dx)[1]
        wall=(w-.014)*math.sqrt(max(0,1-((p[2]-sill)/(sill-bt-.014))**2))
        b.tube('HandleMount_'+n,p,[p[0],max(p[1]+.015,wall),p[2]],.007,'metal')
    p=[x+.40,.21,-.06];n='TowRelease_'+seat
    b.tube(n,[p[0],p[1]-.023,p[2]],[p[0],p[1]+.023,p[2]],.021,'yellow',p)
    b.rigpart(n,p,[-1,0,0],'tow_release',[0,.05],'move')
    b.tube('TowCable_'+seat,p,[p[0]+(.04 if seat=='F' else .14),p[1],p[2]-.16 if seat=='F' else p[2]],.004,'metal');b.attach('TowCable_'+seat,n)
    # User requested yellow jettison; manual says RED: documented discrepancy.
    b.box('Canopy_Jettison_'+seat,(x+.12,-.29,.045),(.09,.026,.025),'yellow')
    b.box('Floor_'+seat,(x+.17,0,-.40),(.80,.26,.015),'liner')
