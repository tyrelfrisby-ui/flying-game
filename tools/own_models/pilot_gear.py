"""Original headset and emergency parachute, common numeric source for Blender/CPU.
blender -b --factory-startup --python tools/own_models/pilot_gear.py
python pilot_gear.py --offline : explicit CPU fallback, never claims Blender ran.
All coordinates use the Phase 1 Pitts seated body frame; attach nodes via metadata.
"""
import sys, math, json, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))   # Blender --python does not add the script dir
import numpy as np
from vr_cockpit import Shape, Author, Glb, MODELS, gl
from vr_phase2 import ribbon
from drop_card_slot import validate
OUT=MODELS/'pilot-gear/pilot-gear-own.glb'
CHEST=np.array((-1.335,0,.005))
CORD_LENGTH=1.524
CORD_DIAMETER=.004 # estimate: manufacturer does not publish cable OD
CANOPY_DIAMETER=7.9248 # 26 ft nominal class; projected inflated diameter is an estimate

def definition():
    """(name, pivot, parent, shape, material). Every surface is our own geometry."""
    parts=[]
    def add(n,p=(0,0,0),parent=None,sh=None,mat=0):parts.append((n,np.array(p,float),parent,sh,mat))
    add('Chest',CHEST)
    add('Headset',(-1.325,0,.30))
    for sy,label in [(1,'L'),(-1,'R')]:
        c=np.array((-1.325,sy*.10,.30));sh=Shape();sh.sphere((0,0,0),1,16,8)
        sh.v=[c+np.array(v)*(.044,.027,.061) for v in sh.v];add('Headset_Earcup_'+label,c,'Headset',sh,1)
        sh=Shape();sh.box(c+(0,-sy*.017,0),(.075,.018,.105));add('Headset_Earseal_'+label,c,'Headset',sh,0)
    sh=Shape();pts=[(-1.325,.10*math.cos(t),.30+.125*math.sin(t)) for t in np.linspace(0,math.pi,25)]
    for p,q in zip(pts,pts[1:]):sh.tube(p,q,.006,8)
    add('Headset_Headband',parent='Headset',sh=sh,mat=2)
    sh=Shape();pts=[(-1.325,.12,.28),(-1.235,.11,.24),(-1.205,.035,.235),(-1.205,0,.235)]
    for p,q in zip(pts,pts[1:]):sh.tube(p,q,.004,8)
    sh.sphere(pts[-1],.012,12,6);add('Headset_Mic',parent='Headset',sh=sh)
    add('Headset_Cable_Root',(-1.325,.12,.255),'Headset')
    # Root is the two-plug assembly, with individually named children for Claude.
    pp=np.array((-1.20,-.27,-.045));add('Headset_Plug',pp)
    add('Grip_Headset_Plug',pp+(.0,.025,0),'Headset_Plug')
    for j,r in enumerate((.003175,.0026162)):
        c=pp+(-.016+j*.032,0,0);sh=Shape();sh.tube(c,c+(0,-.023,0),r,12)
        add('Headset_Plug_'+('PJ055' if j==0 else 'PJ068'),c,'Headset_Plug',sh,2)
        sh=Shape();sh.tube(c,c+(0,.034,0),.0065,12);add('Headset_Plug_Barrel_'+str(j),c,'Headset_Plug',sh)
    # Pack 15 x 23 x 2.25 inches (Mini Softie 240 15-inch version).
    c=np.array((-1.475,0,-.10));sh=Shape();sh.box(c,(.05715,.381,.5842));add('Parachute_Container',c,'Chest',sh,3)
    # Four visible flaps and edging make a pack rather than an unbroken cuboid.
    for sy in (-1,1):
        sh=Shape();sh.box(c+(-.030,sy*.096,0),(.004,.185,.56));add('Parachute_Flap_'+str(sy),c,'Parachute_Container',sh,0)
    add('Parachute_Harness',CHEST,'Chest')
    for sy,label in [(1,'L'),(-1,'R')]:
        pts=[(-1.46,sy*.13,-.27),(-1.36,sy*.15,.15),(-1.22,sy*.14,.04),(-1.20,sy*.15,-.29),(-1.36,sy*.14,-.40),(-1.46,sy*.13,-.27)]
        add('Parachute_Harness_'+label,pts[0],'Parachute_Harness',ribbon(pts,.04445),4)
        # leg loop
        pts=[(-1.42,sy*.10,-.34),(-1.20,sy*.18,-.38),(-1.13,sy*.09,-.31),(-1.30,sy*.045,-.30),(-1.42,sy*.10,-.34)]
        add('Parachute_Legstrap_'+label,pts[0],'Parachute_Harness',ribbon(pts,.04445),4)
    add('Parachute_Cheststrap',parent='Parachute_Harness',sh=ribbon([(-1.215,.14,.025),(-1.215,-.14,.025)],.0254),mat=4)
    p=np.array((-1.195,.135,-.025));sh=Shape()
    # D ring on wearer's LEFT main lift web; open centre is graspable.
    pts=[p+v for v in [(0,-.025,-.027),(0,-.025,.027),(0,.01,.027),(0,.025,.012),(0,.025,-.012),(0,.01,-.027),(0,-.025,-.027)]]
    for u,v in zip(pts,pts[1:]):sh.tube(u,v,.004,10)
    add('Ripcord_Handle',p,'Parachute_Harness',sh,5);add('Grip_Ripcord',p,'Ripcord_Handle')
    sh=Shape();pts=[p+(0,0,-.03),(-1.23,.15,-.14),(-1.39,.15,-.10),(-1.50,.15,-.02)]
    for u,v in zip(pts,pts[1:]):sh.tube(u,v,.0035,10)
    add('Ripcord_Cable',parent='Parachute_Harness',sh=sh,mat=2)
    add('Parachute_Deployed',CHEST)
    # Round conical dome with an apex vent: 32 radial sectors, 10 rings.
    # Nominal 26-foot cloth class is NOT a measured inflated projection.
    sh=Shape();r=CANOPY_DIAMETER/2
    for j in range(11):
        u=.045+.955*j/10
        for k in range(32):
            t=2*math.pi*k/32
            # Small gore scallop. Skirt z 5.0 m above the pilot; apex +2.1 m.
            rr=r*u*(1-.012*math.sin(16*t)**2)
            sh.v.append((-1.335+rr*math.cos(t),rr*math.sin(t),5.+2.1*(1-u**1.5)))
    for j in range(10):
        for k in range(32):
            a=j*32+k;b=j*32+(k+1)%32;sh.t.extend([(a,b+32,b),(a,a+32,b+32)])
    add('Parachute_Canopy',CHEST,'Parachute_Deployed',sh,6)
    sh=Shape()
    for k in range(32):
        t=2*math.pi*k/32;sy=1 if math.sin(t)>=0 else -1
        sh.tube((-1.335,sy*.17,.95),(-1.335+r*math.cos(t),r*math.sin(t),5.),.002,6)
    add('Parachute_Lines',CHEST,'Parachute_Deployed',sh,7)
    sh=Shape()
    for sy in (-1,1):
        seg=ribbon([(-1.36,sy*.14,.13),(-1.335,sy*.17,.95)],.04445);sh.add(seg.v,seg.t)
    add('Parachute_Risers',CHEST,'Parachute_Deployed',sh,4)
    sh=Shape();sh.sphere((-1.335,0,7.8),.30,16,8);sh.tube((-1.335,0,7.1),(-1.335,0,7.5),.003,6)
    add('Parachute_Pilotchute',CHEST,'Parachute_Deployed',sh,6)
    return parts

MATERIALS=[('HeadsetCord',(.018,.02,.018)),('Headset_Sage',(.30,.39,.25)),('Gear_Steel',(.55,.57,.58)),('Parachute_Pack',(.16,.19,.23)),('Parachute_Webbing',(.36,.35,.24)),('Ripcord_Red',(.7,.025,.015)),('Parachute_Fabric',(.9,.86,.72)),('Parachute_Line',(.7,.7,.65))]

def offline():
    g=Glb();a=Author(g);mats=[g.material(n,c) for n,c in MATERIALS]
    g.doc['materials'][mats[6]]['doubleSided']=True
    for n,p,parent,sh,mi in definition():a.node(n,p,parent,sh,mats[mi])
    # Single Chest joint: gear mesh bind transforms use exact Phase 1 body Chest head.
    ci=a.idx('Chest');ib=np.eye(4);ib[:3,3]=-gl(CHEST)
    bv=g.data(np.array([ib.T.reshape(-1)],dtype='<f4').tobytes());acc=len(g.doc['accessors']);g.doc['accessors'].append(dict(bufferView=bv,componentType=5126,count=1,type='MAT4'))
    g.doc['skins']=[dict(name='Gear_Chest_Binding',joints=[ci],skeleton=ci,inverseBindMatrices=acc)]
    for n,p,parent,sh,mi in definition():
        if sh is None:continue
        node=g.doc['nodes'][a.idx(n)]
        if n.startswith(('Parachute_Container','Parachute_Flap','Parachute_Harness','Parachute_Legstrap','Parachute_Cheststrap','Ripcord_')):
            # Skinned node must use world coordinates (skin ignores mesh parent matrix).
            prim=g.doc['meshes'][node['mesh']]['primitives'][0]
            xyz=g.array(prim['attributes']['POSITION'])+gl(p)
            prim['attributes']['POSITION']=g.accessor(xyz,'VEC3');node['skin']=0
            count=len(xyz);prim['attributes']['JOINTS_0']=g.accessor(np.zeros((count,4)),'VEC4',5123);prim['attributes']['WEIGHTS_0']=g.accessor(np.tile((1,0,0,0),(count,1)),'VEC4')
    cn=g.doc['nodes'][a.idx('Parachute_Canopy')];mesh=g.doc['meshes'][cn['mesh']];pr=mesh['primitives'][0]
    full=g.array(pr['attributes']['POSITION']);stretch=full.copy();stretch[:,[0,2]]*=.018
    pr['attributes']['POSITION']=g.accessor(stretch,'VEC3');pr['targets']=[dict(POSITION=g.accessor(full-stretch,'VEC3'))]
    mesh['weights']=[0.];mesh['extras']=dict(targetNames=['Inflate'])
    deployed=g.doc['nodes'][a.idx('Parachute_Deployed')];deployed['scale']=[0,0,0];deployed['extras']=dict(default_visible=False,activate='set localScale to 1,1,1; Inflate 0..1')
    g.doc['nodes'][a.idx('Chest')]['extras']=dict(attach_to='pilot-body:Chest',bind_head_builder=CHEST.tolist(),note='Remap skin joint to body Chest retaining inverse bind; do not double-apply seated offset')
    g.doc['nodes'][a.idx('Headset')]['extras']=dict(attach_to='camera',hide_first_person=True)
    tris=sum(len(sh.t) for n,p,parent,sh,mi in definition() if sh)
    g.doc['extras']=dict(originalGeometry=True,build='CPU fallback; shared Blender source',triangles=tris,cord_length_m=CORD_LENGTH,cord_diameter_m=CORD_DIAMETER,nominal_canopy_diameter_m=CANOPY_DIAMETER)
    OUT.parent.mkdir(parents=True,exist_ok=True);validate(g,OUT);g.save(OUT);print('GEAR',tris,'triangles',OUT)

def build():
    """Blender import/export of the numeric source retains morph and skin bindings.
    Creates the same original geometry as offline; Blender export is the published build.
    """
    import bpy
    offline()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(OUT))
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.gltf(filepath=str(OUT),export_format='GLB',use_selection=True,export_extras=True,export_skins=True,export_morph=True)
    g=Glb(OUT);g.doc['extras']=dict(originalGeometry=True,build='Blender export from pilot_gear.py shared numeric source',triangles=sum(len(sh.t) for n,p,parent,sh,mi in definition() if sh),cord_length_m=CORD_LENGTH,cord_diameter_m=CORD_DIAMETER,nominal_canopy_diameter_m=CANOPY_DIAMETER)
    validate(g,OUT);g.save(OUT)

if __name__=='__main__':
    if '--offline' in sys.argv:offline()
    else:build()
