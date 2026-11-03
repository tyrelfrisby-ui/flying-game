"""Original, no-head flight-suit pilot. Shared numeric geometry for Blender and GLB.
Blender: blender -b --factory-startup --python tools/own_models/pilot_body.py
Offline (when Blender cannot initialise): python tools/own_models/pilot_body.py --offline
No external mesh, skeleton, texture or package is used. Open, relaxed fingers at rest.
"""
import sys, json, math
from pathlib import Path
import numpy as np
from vr_cockpit import Shape, Glb, ROOT, MODELS, gl, builder

OUT=MODELS/'pilot-body/pilot-body-own.glb'
def ik(a,b,l1,l2,bend):
    a,b=np.array(a),np.array(b);d=b-a;length=np.linalg.norm(d)
    assert abs(l1-l2)<length<l1+l2,(length,l1,l2)
    d/=length;u=np.array(bend,float);u-=np.dot(u,d)*d;u/=np.linalg.norm(u)
    x=(l1*l1-l2*l2+length*length)/(2*length)
    return a+x*d+math.sqrt(l1*l1-x*x)*u

def definition(kind='pitts'):
    """Bone heads/tails, parent and radii in the aircraft builder's frame.
    Stature 1.736 m, sitting shoulder 0.594 m: NRC 1983 civilian male median.
    Segment lengths are modelling estimates, NOT independently median body segments.
    """
    bones=[];parts=[];segments=[]
    def bone(n,a,b,parent,r=None,material=0):
        a,b=np.array(a,float),np.array(b,float);bones.append((n,a,b,parent))
        if r:
            sh=Shape();sh.tube(a,b,r,12);parts.append((n,sh,material));segments.append((n,a,b,r))
        return b
    hip=np.array((-1.30,0,-.335))
    if kind=='extra': hip=np.array((-1.72,0,-.01))
    bone('Hips',hip,hip+(0,0,.14),None)
    bone('Spine',hip+(0,0,.14),hip+(-.035,0,.34),'Hips')
    bone('Chest',hip+(-.035,0,.34),hip+(-.06,0,.49),'Spine')
    # Rounded flight suit torso; ellipsoid authored from sphere numbers.
    sh=Shape();sh.sphere((0,0,0),1,20,12);sh.v=[hip+np.array((v[0]*.115-.025,v[1]*.175,v[2]*.23+.24)) for v in sh.v];parts.append(('Spine',sh,0))
    grips={}
    g=Glb(MODELS/('pitts-s2b-like/pitts-s2b-like-own.glb' if kind=='pitts' else 'extra-300-like/extra-300-like-own.glb'))
    worlds=g.world_matrices()
    def target(n):
        i=next(i for i,nod in enumerate(g.doc['nodes']) if nod['name']==n)
        return builder(worlds[i][:3,3])
    for side,sy in [('L',1),('R',-1)]:
        shldr=hip+(-.06,sy*.18,.49)
        bone(side+'_Shoulder',hip+(-.06,sy*.07,.49),shldr,'Chest')
        grip=target('Grip_Throttle_Rear' if side=='L' else 'Grip_Stick_Rear');wrist=grip-np.array((.075,0,0));grips[side+'_Hand']=grip
        elbow=ik(shldr,wrist,.31,.26,(0,sy*.6,-1))
        bone(side+'_UpperArm',shldr,elbow,side+'_Shoulder',.049)
        bone(side+'_LowerArm',elbow,wrist,side+'_UpperArm',.037)
        bone(side+'_Hand',wrist,grip+(.025,0,0),side+'_LowerArm',.033,1)
        # Palm and five three-link fingers, all independently skinned.
        # Each finger is built as three tapered-diameter cylinders, no downloaded rig.
        for finger,offset,length in [('Thumb',-.040,.062),('Index',-.023,.078),('Middle',-.006,.086),('Ring',.012,.080),('Little',.029,.065)]:
            root=wrist+np.array((.075,sy*offset,-.005));parent=side+'_Hand'
            direction=np.array((.88,-sy*.45,-.12)) if finger=='Thumb' else np.array((1,sy*(offset/.15),-.08))
            direction/=np.linalg.norm(direction)
            for j,frac in enumerate((.45,.32,.23),1):
                end=root+direction*length*frac;n=f'{side}_{finger}{j}'
                bone(n,root,end,parent,.009 if finger!='Little' else .0075,1);parent=n;root=end
        ball=target('Foot_'+side+'_Rear') if kind=='pitts' else target('Pedal_R_'+side)+np.array((-.045,0,.09))
        ankle=ball+np.array((-.105,0,-.035));hp=hip+(0,sy*.09,0)
        knee=ik(hp,ankle,.42,.43,(0,sy*.1,1))
        bone(side+'_UpperLeg',hp,knee,'Hips',.060)
        bone(side+'_LowerLeg',knee,ankle,side+'_UpperLeg',.043)
        bone(side+'_Foot',ankle,ball,side+'_LowerLeg',.043,2)
        bone(side+'_Toes',ball,ball+(.075,0,0),side+'_Foot',.040,2)
    return bones,parts,segments,grips

def offline():
    bones,parts,segments,grips=definition();g=Glb();materials=[g.material('Pilot_FlightSuit',(.10,.18,.13)),g.material('Pilot_Nomex',(.28,.30,.24)),g.material('Pilot_Boots',(.025,.028,.025))]
    # glTF skin in exported model frame, all node rotations identity. Joint translations
    # and inverse bind matrices define the same seated relaxed-open rest pose as Blender.
    roots=g.doc['scenes'][0]['nodes'];lookup={};heads={}
    for n,a,b,parent in bones:
        i=len(g.doc['nodes']);lookup[n]=i;heads[n]=gl(a)
        node=dict(name=n,translation=(heads[n]-(heads[parent] if parent else 0)).tolist())
        g.doc['nodes'].append(node)
        if parent:g.doc['nodes'][lookup[parent]].setdefault('children',[]).append(i)
        else:roots.append(i)
    for n,sh,material in parts:
        g.mesh('Pilot_'+n,[gl(v) for v in sh.v],sh.t,materials[material]);node=g.doc['nodes'][-1];node['skin']=0
        prim=g.doc['meshes'][node['mesh']]['primitives'][0];count=g.doc['accessors'][prim['attributes']['POSITION']]['count']
        prim['attributes']['JOINTS_0']=g.accessor(np.tile((lookup[n],0,0,0),(count,1)),'VEC4',5123)
        prim['attributes']['WEIGHTS_0']=g.accessor(np.tile((1.,0,0,0),(count,1)),'VEC4')
    ib=[]
    for n,a,b,parent in bones:
        m=np.eye(4);m[:3,3]=-gl(a);ib.append(m.T.reshape(-1))
    v=g.data(np.array(ib,dtype='<f4').tobytes());acc=len(g.doc['accessors']);g.doc['accessors'].append(dict(bufferView=v,componentType=5126,count=len(bones),type='MAT4'))
    g.doc['skins']=[dict(name='Pilot_Skeleton',joints=list(range(len(bones))),skeleton=lookup['Hips'],inverseBindMatrices=acc)]
    tris=sum(len(sh.t) for _,sh,_ in parts)
    g.doc['extras']=dict(originalGeometry=True,build='CPU fallback, Blender startup unavailable; same numeric source as Blender builder',triangles=tris,bones=len(bones),anthropometry='NRC 1983 civilian male 50th: stature 1.736 m, sitting shoulder .594 m; segment estimates documented in PROVENANCE')
    OUT.parent.mkdir(parents=True,exist_ok=True);g.save(OUT);print('PILOT',tris,'triangles',len(bones),'bones',OUT)
    return tris

def build(kind='pitts',export=True):
    import bpy
    from mathutils import Vector, Matrix
    bones,parts,segments,grips=definition(kind)
    arm=bpy.data.armatures.new('Pilot_Skeleton');obj=bpy.data.objects.new('Pilot_Armature',arm);bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active=obj;obj.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
    # Imported Blender frame: nose -X, port -Y, up Z.
    conv=lambda p:Vector((-float(p[0]),-float(p[1]),float(p[2])))
    for n,a,b,parent in bones:
        eb=arm.edit_bones.new(n);eb.head=conv(a);eb.tail=conv(b)
        if parent: eb.parent=arm.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    mats=[]
    for name,col in [('Pilot_FlightSuit',(.10,.18,.13)),('Pilot_Nomex',(.28,.30,.24)),('Pilot_Boots',(.025,.028,.025))]:
        m=bpy.data.materials.new(name);m.diffuse_color=(*col,1);mats.append(m)
    for n,sh,mi in parts:
        me=bpy.data.meshes.new('Pilot_'+n);me.from_pydata([conv(p) for p in sh.v],[],sh.t);me.materials.append(mats[mi])
        o=bpy.data.objects.new('Pilot_'+n,me);bpy.context.collection.objects.link(o);o.parent=obj
        group=o.vertex_groups.new(name=n);group.add(list(range(len(sh.v))),1,'REPLACE');mod=o.modifiers.new('Pilot_Skin','ARMATURE');mod.object=obj
    if export:
        bpy.ops.object.select_all(action='DESELECT');obj.select_set(True)
        for child in obj.children:child.select_set(True)
        OUT.parent.mkdir(parents=True,exist_ok=True)
        bpy.ops.export_scene.gltf(filepath=str(OUT),export_format='GLB',use_selection=True,export_extras=True)
    return obj

if __name__=='__main__':
    if '--offline' in sys.argv: offline()
    else:
        import bpy
        bpy.ops.wm.read_factory_settings(use_empty=True);build()
