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
# v3 candidate (feet flat on the pedal face, shoulders, glove detail, folds): OUTSIDE Resources, so the game keeps
# shipping v2 until Ty approves; PilotBodyShots renders it with BODY_ASSET. --v3 builds it.
OUT_V3=MODELS.parent.parent/'PilotBodyCandidates/pilot-body-own-v3.glb'
# v3 foot on the pedal: the sole lies on a face leaning SOLE_LEAN top-forward (the fleet's pedal faces measure
# 12-15 deg, PilotBodyShots BODY_PEDALS); the ankle sits ANKLE_BACK down the sole from the ball and ANKLE_UP off it.
SOLE_LEAN=math.radians(13);ANKLE_BACK=.13;ANKLE_UP=.085;TOES=.07
def sole_frame():
    f=np.array((math.sin(SOLE_LEAN),0,math.cos(SOLE_LEAN)))      # up the pedal face (builder: x nose, z up)
    n=np.array((-math.cos(SOLE_LEAN),0,math.sin(SOLE_LEAN)))     # out of the face, toward the pilot
    return f,n
def ik(a,b,l1,l2,bend):
    a,b=np.array(a),np.array(b);d=b-a;length=np.linalg.norm(d)
    assert abs(l1-l2)<length<l1+l2,(length,l1,l2)
    d/=length;u=np.array(bend,float);u-=np.dot(u,d)*d;u/=np.linalg.norm(u)
    x=(l1*l1-l2*l2+length*length)/(2*length)
    return a+x*d+math.sqrt(l1*l1-x*x)*u

def definition(kind='pitts',v3=False):
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
        if v3: f,n=sole_frame();ankle=ball-f*ANKLE_BACK+n*ANKLE_UP
        else: ankle=ball+np.array((-.105,0,-.035))
        hp=hip+(0,sy*.09,0)
        knee=ik(hp,ankle,.42,.43,(0,sy*(0 if v3 else .1),1))  # v3: knees straight up (rest pose clears the Pitts truss by 7 mm; .1 splay hit it)
        bone(side+'_UpperLeg',hp,knee,'Hips',.060)
        bone(side+'_LowerLeg',knee,ankle,side+'_UpperLeg',.043)
        bone(side+'_Foot',ankle,ball,side+'_LowerLeg',.043,2)
        bone(side+'_Toes',ball,ball+(sole_frame()[0]*TOES if v3 else np.array((.075,0,0))),side+'_Foot',.040,2)
    return bones,parts,segments,grips

def offline(v3=False):
    """One skinned, smooth-shaded, normal-mapped mesh (pilot_body_smooth), the same 49-bone skeleton."""
    from pilot_body_smooth import body, normal_png
    bones,parts,segments,grips=definition(v3=v3);g=Glb();out=OUT_V3 if v3 else OUT
    samplers=g.doc.setdefault('samplers',[]);samplers.append(dict(magFilter=9729,minFilter=9987,wrapS=10497,wrapT=10497))
    def material(name,rgb,rough,metal=0.0,normal=None,scale=1.0):
        m=dict(name=name,doubleSided=False,pbrMetallicRoughness=dict(baseColorFactor=[*rgb,1],metallicFactor=metal,roughnessFactor=rough))
        if normal:
            v=g.data(normal_png(normal,soft=v3));images=g.doc.setdefault('images',[]);images.append(dict(name='Pilot_'+normal+'_n',bufferView=v,mimeType='image/png'))
            tex=g.doc.setdefault('textures',[]);tex.append(dict(source=len(images)-1,sampler=0));m['normalTexture']=dict(index=len(tex)-1,scale=scale)
        g.doc['materials'].append(m);return len(g.doc['materials'])-1
    mats=dict(suit=material('Pilot_FlightSuit',(.075,.10,.072) if v3 else (.045,.07,.05),.9,normal='twill',scale=.45),
              glove=material('Pilot_Nomex',(.16,.13,.085),.7,normal='leather',scale=.3),
              boot=material('Pilot_Boots',(.025,.028,.025),.42,normal='grain',scale=.4),
              strap=material('Pilot_Harness',(.035,.04,.05),.8,normal='webbing',scale=.6),
              metal=material('Pilot_Buckle',(.55,.56,.58),.35,metal=.9))
    if v3: mats['sole']=material('Pilot_BootSole',(.012,.012,.013),.85,normal='grain',scale=.25)
    # glTF skin in exported model frame, all node rotations identity. Joint translations
    # and inverse bind matrices define the same seated relaxed-open rest pose as Blender.
    roots=g.doc['scenes'][0]['nodes'];lookup={};heads={}
    for n,a,b,parent in bones:
        i=len(g.doc['nodes']);lookup[n]=i;heads[n]=gl(a)
        node=dict(name=n,translation=(heads[n]-(heads[parent] if parent else 0)).tolist())
        g.doc['nodes'].append(node)
        if parent:g.doc['nodes'][lookup[parent]].setdefault('children',[]).append(i)
        else:roots.append(i)
    prims=[];tris=0
    for key,acc in body(bones,v3=v3).items():
        p,nrm,tan,sign,uv,j,w,t=acc.arrays();tris+=len(t)
        P=np.array([gl(v) for v in p]);N=np.array([gl(v) for v in nrm]);TA=np.array([gl(v) for v in tan])
        attrs=dict(POSITION=g.accessor(P,'VEC3'),NORMAL=g.accessor(N,'VEC3'),
                   TANGENT=g.accessor(np.hstack([TA,sign[:,None]]),'VEC4'),TEXCOORD_0=g.accessor(uv,'VEC2'),
                   JOINTS_0=g.accessor(j,'VEC4',5123),WEIGHTS_0=g.accessor(w,'VEC4'))
        prims.append(dict(attributes=attrs,indices=g.accessor(t.reshape(-1),'SCALAR',5125),material=mats[key],mode=4))
    g.doc['meshes'].append(dict(name='Pilot_Body',primitives=prims))
    g.doc['nodes'].append(dict(name='Pilot_Body',mesh=0,skin=0));roots.append(len(g.doc['nodes'])-1)
    if v3:
        # Marker (not a joint): this body authors its own foot pose. VrPilotBody then puts the ankle where the rest
        # pose has it relative to the ball (sole flat on the pedal face) instead of v2's toes-down offset.
        for side in 'LR':
            i=len(g.doc['nodes']);_,nn=sole_frame()
            g.doc['nodes'].append(dict(name=side+'_SoleOnPedal',translation=(gl(dict((b[0],b) for b in bones)[side+'_Toes'][1])-heads[side+'_Foot']).tolist()))
            g.doc['nodes'][lookup[side+'_Foot']].setdefault('children',[]).append(i)
    ib=[]
    for n,a,b,parent in bones:
        m=np.eye(4);m[:3,3]=-gl(a);ib.append(m.T.reshape(-1))
    v=g.data(np.array(ib,dtype='<f4').tobytes());acc=len(g.doc['accessors']);g.doc['accessors'].append(dict(bufferView=v,componentType=5126,count=len(bones),type='MAT4'))
    g.doc['skins']=[dict(name='Pilot_Skeleton',joints=list(range(len(bones))),skeleton=lookup['Hips'],inverseBindMatrices=acc)]
    g.doc['extras']=dict(originalGeometry=True,build='CPU builder pilot_body_smooth: one skinned mesh, smooth normals, blended joint weights, procedural normal maps; same numeric skeleton as Blender builder',triangles=tris,bones=len(bones),anthropometry='NRC 1983 civilian male 50th: stature 1.736 m, sitting shoulder .594 m; segment estimates documented in PROVENANCE')
    if v3: g.doc['extras']['version']='v3 candidate: feet flat on the pedal face (sole lean 13 deg), heel/sole/toe-box boots with laces, deltoid+trapezius shoulders, knuckled gloves with cuff straps, elbow/knee/waist folds, zip and chest pockets, softer normal maps, lighter suit'
    out.parent.mkdir(parents=True,exist_ok=True);g.save(out);print('PILOT',tris,'triangles',len(bones),'bones',len(prims),'materials',out)
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
    if '--offline' in sys.argv: offline(v3='--v3' in sys.argv)
    else:
        import bpy
        bpy.ops.wm.read_factory_settings(use_empty=True);build()
