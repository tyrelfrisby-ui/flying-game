"""Original straight-ski variant of our own J-3. No third-party geometry.
Run Blender -b --python tools/own_models/build_j3_skis.py
Imports the current own J-3 GLB to preserve its later cockpit/rig edits.
Dimensions match the proposed ski-physics lane; see docs/models/j3-skis-PROVENANCE.md.

TY, 2026-11-10, on the straight ski (NOT the wheel ski, which is SkiGear's hinged installation):
    "The straight ski (not the wheel ski) has a pedestal the radius of a normal tire for that airplane so
     the aoa on the ground is the same. The pedestal is a tapered 'tower'. The top of the ski has a keel
     for strength and the front has a handle for lifting/dragging the skis"
and, on both kinds of ski: "The front of the skis should curve up not a sharp angle".

Four things, and the first is a DERIVED CONSTRAINT, not a dimension anybody gets to pick:

1. THE PEDESTAL HEIGHT IS MEASURED, NEVER AUTHORED. It was the literals .2235 (main) and .076 (tail) --
   right for this J-3 and silently wrong for the next aeroplane, which is exactly how a deck angle drifts.
   What the constraint really says is "put the running surface where the tyre's bottom was": the aeroplane
   must stand on its skis at the same three-point attitude, and therefore at the same wing alpha, as it
   does on its wheels. So the height is taken from THIS MODEL'S OWN WHEEL MESH, per leg, before the wheels
   are deleted: pedestal height = (the axle this ski hangs from).z - (that leg's wheel's lowest vertex).z.
   Where the ski's pivot is the wheel's own axle -- both mains -- that reduces exactly to Ty's words, the
   tyre's radius, and it comes out at 0.2235 m with nothing typed in. Where it is NOT the axle it still
   holds the attitude: the tail ski hangs from the leaf spring's swivel head, 5 mm above and 0.125 m ahead
   of the old tailwheel centre, so its pedestal is 0.071 m, not the tailwheel's 0.076 m radius. The old
   literal stood the tail 5 mm low -- 0.06 deg of extra nose-up on a 4.6 m wheelbase. Small, and precisely
   the kind of thing Ty says to assert rather than eyeball: check_j3_skis.py now does, from the two GLBs.
2. The pedestal is a tapered TOWER (below), not the two flat sheet cheeks it was.
3. The top of the ski carries a KEEL -- a longitudinal stiffening rib. The formed hat section was already
   there but stood only 55 mm proud and read as flat in the render; it is now a keel you can see.
4. The front carries a HANDLE for lifting and dragging. There was none.
"""
import bpy, math, os, sys
from pathlib import Path
from mathutils import Vector, Matrix
ROOT = Path(__file__).resolve().parents[2]
OUT = Path(os.environ.get('J3_OUT', ROOT/'unity/Assets/_Project/Resources/Models/j3-cub-skis-like/j3-cub-skis-like-own.glb'))
BASE = ROOT/'unity/Assets/_Project/Resources/Models/j3-cub-like/j3-cub-like-own.glb'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(BASE))

# The wheels are measured BEFORE they are removed: this is the only place the pedestal heights come from.
WHEEL_BOTTOM_Z = {}
for leg, wheel in (('L','Wheel_L'), ('R','Wheel_R'), ('Tail','Wheel_Tail')):
    o = bpy.data.objects[wheel]
    zs = [(o.matrix_world@v.co).z for v in o.data.vertices]
    WHEEL_BOTTOM_Z[leg] = min(zs)
    print('J3 SKIS: %s wheel radius %.4f m, bottom z %.4f' % (wheel, (max(zs)-min(zs))/2, min(zs)))

for name in ('Wheel_L','Wheel_R','Wheel_Tail','TailWheel_Steer','TailWheel_Links'):
    obj=bpy.data.objects.get(name)
    if obj: bpy.data.objects.remove(obj, do_unlink=True)

def material(name,color,metal=0):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=.36
    return m
AL=material('Ski aluminum',(.57,.62,.68),.7)
STEEL=material('Ski steel',(.16,.19,.22),.75)
CORD=material('Ski shock cord',(.12,.10,.075))
SOLE=material('Ski running sole',(.23,.26,.29),.3)
def empty(name,point,parent=None):
    o=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(o);o.location=point
    if parent:
        o.parent=parent;o.matrix_parent_inverse=parent.matrix_world.inverted()
    return o

def mesh(name,verts,faces,mat,parent):
    m=bpy.data.meshes.new(name);m.from_pydata(verts,[],faces);m.update()
    o=bpy.data.objects.new(name,m);bpy.context.collection.objects.link(o);o.data.materials.append(mat)
    o.parent=parent # geometry is authored relative to the axle
    return o

def rod(name,a,b,r,mat,parent):
    d=Vector(b)-Vector(a);mid=(Vector(a)+Vector(b))/2
    bpy.ops.mesh.primitive_cylinder_add(vertices=10,radius=r,depth=d.length,location=mid)
    o=bpy.context.object;o.name=name;o.rotation_euler=d.to_track_quat('Z','Y').to_euler();o.data.materials.append(mat);o.parent=parent
    return o

def plank(name,stations,widths,zs,th,mat,parent):
    v=[]
    for x,w,z in zip(stations,widths,zs):v.extend([(x,-w/2,z),(x,w/2,z),(x,w/2,z+th),(x,-w/2,z+th)])
    f=[(3,2,1,0)]
    for i in range(len(stations)-1):
        for j in range(4):f.append((4*i+j,4*i+(j+1)%4,4*(i+1)+(j+1)%4,4*(i+1)+j))
    k=4*(len(stations)-1);f.append((k,k+1,k+2,k+3))
    return mesh(name,v,f,mat,parent)

for leg in ('L','R','Tail'):
    tail=leg=='Tail';sg=-1 if leg=='L' else 1
    # Imported own-model frame: nose -X, pilot's left -Y, up +Z.
    axle=Vector((6.455-1.817,0,-.04+.135) if tail else (1.48-1.817,sg*.92,-1.11+.135))
    pivot=empty('SkiPivot_'+leg,axle);bpy.context.view_layer.update()
    pivot['pitch_axis_gltf']=[0,0,-1];pivot['note']='Axle origin; tip-up positive about Blender +Y. No physics implemented.'
    length,width,fromtip=(.60,.10,.30) if tail else (1.75,.222,.95)
    # THE PEDESTAL HEIGHT, DERIVED (see the module note). Not a number in this file: it is how far this ski's own
    # pivot stands above the bottom of the wheel it replaces, measured off the wheels model a few lines above. Mains:
    # the pivot IS the axle, so it comes out as the tyre's radius, 0.2235 m. Tail: the pivot is the spring's swivel
    # head, 5 mm above the old tailwheel centre, so it comes out 0.071 m and the tail sits where it sat.
    height=axle.z-WHEEL_BOTTOM_Z[leg]
    pivot['length_m']=length;pivot['width_m']=width;pivot['pedestal_from_tip_m']=fromtip;pivot['pedestal_height_m']=height
    pivot['pedestal_height_source']='measured: axle z minus the lowest vertex of the wheel this ski replaces, so the ski stands the aeroplane at its wheel three-point attitude (Ty 2026-11-10)'
    toe=-fromtip;heel=length-fromtip
    th=.012 if not tail else .008
    # ---- THE BOTTOM: ONE LONG SHALLOW SWEEP AT EACH END, FLAT UNDER THE PEDESTAL -------------------------------
    # Ty 2026-11-10: "The front of the skis should curve up not a sharp angle", and then his own reference
    # photographs (an Aero Ski pair, a Federal A-1500 pair, and a page of Federals). Those photographs say the
    # sweep is LONG and SHALLOW -- the bottom is flat under the pedestal and is already rising well before you can
    # point at where it started -- and that the TAIL is swept too. This ski's old tip was a parabola over the
    # forward 0.30 m: tangent, so it never had the crease Ty objected to on the wheel ski, but far too short a
    # curve and with a square heel.
    #
    # The profile is now the one SkiGear.BottomRiseAt uses, so the two ski stacks describe one ski, and it was
    # measured, not chosen: the bottom edge of the side-on Aero Ski photograph was traced by threshold over its
    # whole 561-pixel length (the long note in src/FlyingGame.Core/SkiGear.cs has the trace and the power-law fit,
    # the exponent 2.5, and the caveat that a photograph is not an orthographic view).
    #     nose rises 0.127 of the ski's length, over the forward 0.45 of it
    #     tail rises 0.032 of the ski's length, over the aft 0.12 of it
    #     flat in between -- 0.45 to 0.88 back from the nose, and the pedestal is at 0.54 (main) / 0.50 (tail)
    # Superseded: the A-1500A "upturn 0.30 x 0.10 m" in docs/models/j3-skis-PROVENANCE.md, which was marked
    # ESTIMATE with no Federal drawing found. It is about half the rise and a third of the reach of the trace.
    NOSE_RISE,NOSE_SWEEP,TAIL_RISE,TAIL_SWEEP,POWER=.127,.45,.032,.12,2.5
    def rise(u):
        if u<NOSE_SWEEP: return NOSE_RISE*length*((NOSE_SWEEP-u)/NOSE_SWEEP)**POWER
        if u>1-TAIL_SWEEP: return TAIL_RISE*length*((u-(1-TAIL_SWEEP))/TAIL_SWEEP)**POWER
        return 0.
    # The plan narrows into a rounded nose and a narrower heel, as every one of the photographs does; a constant
    # width plank with a curved end still reads as a plank. Widest from a third back to seven tenths.
    def wide(u):
        w=width*(1-.58*max(0.,(.34-u)/.34)**1.8-.34*max(0.,(u-.70)/.30)**1.8)
        # and both ends rounded off in PLAN over the last 2 % -- in the photographs a ski's nose and heel are round,
        # not pointed, and a flat end cap on a tapering plank reads as a chisel.
        e=min(u,1-u)
        return w*(1 if e>.02 else math.sqrt(max(.02,1-(1-e/.02)**2)))
    def station(u): x=toe+u*length; return x,wide(u),-height+rise(u)
    # Two meshes, split where the nose sweep ends, because the exported node contract names both (PROVENANCE).
    NS=20
    nose_u=sorted(set([NOSE_SWEEP*i/NS for i in range(NS+1)]+[0.,.004,.008,.013,.02]))
    body_u=sorted(set([NOSE_SWEEP+(1-NOSE_SWEEP)*i/NS for i in range(NS+1)]+[1.,.996,.992,.987,.98]))
    for nm,us in (('SkiTip_'+leg,nose_u),('Ski_'+leg,body_u)):
        st=[station(u) for u in us]
        plank(nm,[p[0] for p in st],[p[1] for p in st],[p[2] for p in st],th,AL,pivot)
    xs=[p[0] for p in (station(u) for u in nose_u)]
    # ---- THE KEEL ------------------------------------------------------------------------------------------------
    # Ty: "The top of the ski has a keel for strength". It was here as a 55 mm crown spread over four fifths of the
    # width, which read as a flat top in the render. In the photographs it is a MAJOR feature of a ski seen from
    # above -- on the Aero Ski a defined ridge from near the nose almost to the tail, on the Federals what looks
    # like a raised tube down the centreline -- so it is now a narrow, tall formed member: 90 mm proud on the main
    # ski and 35 mm on the tail, over three fifths of the width instead of four fifths, running from 0.03 to 0.97
    # of the length. Those heights are 5.1 % and 5.8 % of each ski's length, which is the depth a formed-sheet beam
    # of that span wants. ESTIMATE, as the sheet gauge and the rivet spacing beside it always were: no Federal
    # drawing has been found for any of it.
    keel=.090 if not tail else .035
    keel_u=[.03+.94*i/44 for i in range(45)]
    channel_v=[]
    for u in keel_u:
        xx,ww,zz=station(u)
        for fraction,r in [(-.30,0),(-.22,keel*.11),(-.15,keel*.62),(-.08,keel*.95),(0,keel),(.08,keel*.95),(.15,keel*.62),(.22,keel*.11),(.30,0)]:
            channel_v.append((xx,ww*fraction,zz+th+.001+r))
    channel_f=[]
    for row in range(len(channel_v)//9-1):
        for col in range(8):channel_f.append((row*9+col,(row+1)*9+col,(row+1)*9+col+1,row*9+col+1))
    channel=mesh('SkiSpine_'+leg,channel_v,channel_f,AL,pivot)
    channel.modifiers.new('formed sheet','SOLIDIFY').thickness=.002
    for u in [.30+.62*i/22 for i in range(23)]:
        xx,ww,zz=station(u)
        for yy in (-ww*.40,ww*.40):
            rod('SkiFlangeRivet_'+leg,(xx,yy,zz+th),(xx,yy,zz+th+.005),.003,STEEL,pivot)
    # The hard running strip down the centre of the FLAT, where the ski actually bears; it stops where the sweeps do.
    rod('SkiRunner_'+leg,(station(NOSE_SWEEP)[0],0,-height-.007),(station(1-TAIL_SWEEP)[0],0,-height-.007),.006 if not tail else .003,SOLE,pivot)
    # THE PEDESTAL: A TAPERED TOWER (Ty 2026-11-10: "The pedestal is a tapered 'tower'"). It was two flat sheet
    # cheeks standing on edge either side of the axle, which from the side read as one thin fin and from ahead as
    # nothing at all. A tower is what the load wants anyway: the ski carries the whole leg through this one joint, so
    # it is broad where it is riveted to the ski -- a long, wide footprint spreading the load into the keel and the
    # flanges -- and gathers in to the axle bush at the top, where it only has to hold the bore.
    # Proportions are a VISUAL JUDGEMENT, as the sheet gauge and the rivet spacing always were (PROVENANCE): base
    # 0.37 m fore-and-aft by 0.13 m across on the main ski, drawing in to 0.13 x 0.075 at the bush. It sits over the
    # axle, pushed slightly aft like the cheeks were, because the axle is aft of the ski's middle.
    for side,inout in ((-1,'_Inner'),(1,'_Outer')):
        if side==-1: continue          # built once, as a solid; the two names below keep the old node contract
        bx0,bx1,by=(-.185,.185,.065) if not tail else (-.105,.105,.038)
        tx0,tx1,ty=(-.062,.068,.0375) if not tail else (-.035,.040,.022)
        base_z=-height+th; top_z=.016 if not tail else .010
        v=[(bx0,-by,base_z),(bx1,-by,base_z),(bx1,by,base_z),(bx0,by,base_z),
           (tx0,-ty,top_z),(tx1,-ty,top_z),(tx1,ty,top_z),(tx0,ty,top_z)]
        f=[(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
        tower=mesh('SkiPedestal_'+leg,v,f,AL,pivot)
        tower.modifiers.new('formed sheet','SOLIDIFY').thickness=.0025
        # The riveted feet where the tower lands on the ski, down both sides of the base.
        for xx in [bx0+ (bx1-bx0)*i/5 for i in range(6)]:
            for yy in (-by,by):
                rod('SkiRivet_'+leg,(xx,yy,-height+.010),(xx,yy,-height+.024),.006 if not tail else .0035,STEEL,pivot)
    rod('SkiAxleBush_'+leg,(0,-.065,0),(0,.065,0),.027 if not tail else .015,STEEL,pivot)
    # THE HANDLE (Ty 2026-11-10: "the front has a handle for lifting/dragging the skis"). A bent tube bail standing
    # on the very nose, where the photographs put the nose fitting: in every one of them the front of the ski
    # carries a bracket or an eye, and it is the same place the nose cable picks the ski up from. That is not an
    # accident -- the nose is the end a man lifts to break a frozen ski out, and the end he drags it by -- so the
    # handle and the cable share it here too, the cable anchoring on the bail's own crossbar.
    hu=.045
    hx,hw,hz0=station(hu)
    hy=hw*.40; hz=hz0+th+(.080 if not tail else .038)                 # bar height above the top skin at the nose
    hr=.014 if not tail else .007
    for yy in (-hy,hy):
        rod('SkiHandle_'+leg,(hx,yy,hz0+th),(hx,yy,hz),hr,STEEL,pivot)
    rod('SkiHandle_'+leg,(hx,-hy,hz),(hx,hy,hz),hr,STEEL,pivot)
    front=Vector((hx,0,hz));rear=Vector((station(.93)[0],0,station(.93)[2]+th+.012))
    # Existing front/rear gear pin locations, distinct tabs for redundant front restraint.
    fixedfront=Vector((1.46-1.817,sg*.32,-.625+.135)) if not tail else Vector((6.10-1.817,0,.035+.135))
    fixedrear=Vector((2.04-1.817,sg*.32,-.605+.135)) if not tail else Vector((6.33-1.817,.04,.12+.135))
    for label,p in [('Toe',front),('Heel',rear),('Contact',(0,0,-height))]:
        e=empty('SkiAnchor_'+label+'_'+leg,(0,0,0));e.parent=pivot;e.location=p
    for label,p in [('Front',fixedfront),('Rear',fixedrear)]:empty('SkiAnchor_Airframe'+label+'_'+leg,p)
    for kind,start,end,r,mat in [('Bungee',front+Vector((0,-.022,0)),fixedfront-axle,.012 if not tail else .006,CORD),('SafetyCable',front+Vector((0,.022,0)),fixedfront-axle+Vector((0,.025,0)),.0025,STEEL),('CheckCable',rear,fixedrear-axle,.0025,STEEL)]:
        for endpoint,point in [('Ski',start),('Airframe',end)]:
            anchor=empty('SkiAnchor_'+kind+endpoint+'_'+leg,(0,0,0))
            if endpoint=='Ski':anchor.parent=pivot;anchor.location=point
            else:anchor.location=axle+Vector(point)
        obj=rod('Ski'+kind+'_'+leg,start,end,r,mat,pivot)
        obj['airframe_anchor']='SkiAnchor_'+kind+'Airframe_'+leg
        obj['ski_anchor']='SkiAnchor_'+kind+'Ski_'+leg
        obj['note']='Rest-pose cable: re-aim/stretch between anchors when ski pitches; do not rotate airframe endpoint.'
        # Visible eye fitting and a short metal ferrule.
        direction=(Vector(end)-Vector(start)).normalized()
        rod('SkiFerrule_'+kind+'_'+leg,Vector(start)+direction*.025,Vector(start)+direction*.065,.006,STEEL,pivot)
    # One rivet mesh per ski avoids dozens of tiny draw calls on mobile/Quest.
    rivets=[o for o in pivot.children if o.name.startswith('SkiFlangeRivet_') or o.name.startswith('SkiRivet_')]
    bpy.ops.object.select_all(action='DESELECT')
    for o in rivets:o.select_set(True)
    bpy.context.view_layer.objects.active=rivets[0]
    bpy.ops.object.join();rivets[0].name='SkiRivets_'+leg
    if not tail:
        gear=bpy.data.objects.get('Gear_Pivot_'+leg)
        if gear:
            pivot.parent=gear;pivot.matrix_parent_inverse=gear.matrix_world.inverted()
bpy.context.view_layer.update()
OUT.parent.mkdir(parents=True,exist_ok=True)
bpy.ops.export_scene.gltf(filepath=str(OUT),export_format='GLB',export_yup=True,export_apply=True,export_extras=True)
print('J3 SKIS MODEL EXPORTED',OUT)
