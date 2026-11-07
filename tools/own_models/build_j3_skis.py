"""Original straight-ski variant of our own J-3. No third-party geometry.
Run Blender -b --python tools/own_models/build_j3_skis.py
Imports the current own J-3 GLB to preserve its later cockpit/rig edits.
Dimensions match the proposed ski-physics lane; see docs/models/j3-skis-PROVENANCE.md.
"""
import bpy, math, os, sys
from pathlib import Path
from mathutils import Vector, Matrix
ROOT = Path(__file__).resolve().parents[2]
OUT = Path(os.environ.get('J3_OUT', ROOT/'unity/Assets/_Project/Resources/Models/j3-cub-skis-like/j3-cub-skis-like-own.glb'))
BASE = ROOT/'unity/Assets/_Project/Resources/Models/j3-cub-like/j3-cub-like-own.glb'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(BASE))
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
    length,width,tip,up,fromtip,height=(.60,.10,.10,.04,.30,.076) if tail else (1.75,.222,.30,.10,.95,.2235)
    pivot['length_m']=length;pivot['width_m']=width;pivot['pedestal_from_tip_m']=fromtip;pivot['pedestal_height_m']=height
    toe=-fromtip;heel=length-fromtip;knee=toe+tip
    plank('Ski_'+leg,[knee,0,heel-.07,heel],[width*.98,width,width*.90,width*.68],[-height]*4,.012 if not tail else .008,AL,pivot)
    xs=[toe+(tip*i/12) for i in range(13)];ws=[width*(.16+.82*math.sin(math.pi*.5*i/12)) for i in range(13)]
    zs=[-height+up*(1-i/12)**2 for i in range(13)]
    plank('SkiTip_'+leg,xs,ws,zs,.012 if not tail else .008,AL,pivot)
    # Formed longitudinal hat section, broad flanges and rivets (own analytic topology).
    channel_v=[]
    for xx,ww,zz in zip(xs+[0,heel-.08,heel],ws+[width,width*.9,width*.68],zs+[-height]*3):
        for fraction,rise in [(-.40,0),(-.26,.006),(-.16,.045 if not tail else .020),(0,.055 if not tail else .025),(.16,.045 if not tail else .020),(.26,.006),(.40,0)]:
            channel_v.append((xx,ww*fraction,zz+.013+rise))
    channel_f=[]
    for row in range(len(channel_v)//7-1):
        for col in range(6):channel_f.append((row*7+col,(row+1)*7+col,(row+1)*7+col+1,row*7+col+1))
    channel=mesh('SkiSpine_'+leg,channel_v,channel_f,AL,pivot)
    channel.modifiers.new('formed sheet','SOLIDIFY').thickness=.002
    for xx in [knee+(heel-knee)*i/22 for i in range(23)]:
        for yy in (-width*.32,width*.32):
            rod('SkiFlangeRivet_'+leg,(xx,yy,-height+.012),(xx,yy,-height+.017),.003,STEEL,pivot)
    rod('SkiRunner_'+leg,(knee,0,-height-.007),(heel-.04,0,-height-.007),.006 if not tail else .003,SOLE,pivot)
    # Two tapered sheet cheeks: axle bore/bushing above a broad riveted base.
    for side in (-1,1):
        yy=side*(.035 if not tail else .020);dz=.012
        verts=[(-.14,yy,-height+dz),(.17,yy,-height+dz),(.055,yy,.016),(-.055,yy,.016)]
        ped=mesh('SkiPedestal_'+leg+('_Outer' if side==1 else '_Inner'),verts,[(0,1,2,3)],AL,pivot)
        mod=ped.modifiers.new('sheet thickness','SOLIDIFY');mod.thickness=.004
        for xx in (-.105,.12):
            rod('SkiRivet_'+leg,(xx,yy,-height+.01),(xx,yy,-height+.025),.007 if not tail else .004,STEEL,pivot)
    bpy.data.objects['SkiPedestal_'+leg+'_Outer'].name='SkiPedestal_'+leg
    rod('SkiAxleBush_'+leg,(0,-.065,0),(0,.065,0),.027 if not tail else .015,STEEL,pivot)
    front=Vector((toe+.08,0,-height+up*.55+.02));rear=Vector((heel-.07,0,-height+.025))
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
