"""Operator-only Blender renders. No Unity/deploy. Twelve Phase 2 plus nine P2c 1600x1000 PNGs.
blender -b --factory-startup --python tools/own_models/render_vr_phase2.py -- OUTPUT_DIR
Imported frame: nose -X, port -Y, Z up. Sight_ excluded from EVERY non-eye view.
"""
import sys,math
from pathlib import Path
import bpy
from mathutils import Vector,Quaternion
HERE=Path(__file__).resolve().parent;sys.path.insert(0,str(HERE))
from vr_cockpit import MODELS
OUT=Path(sys.argv[sys.argv.index('--')+1]);OUT.mkdir(parents=True,exist_ok=True)
BASE={}

def load(kind='pitts',aircraft=True):
    bpy.ops.wm.read_factory_settings(use_empty=True);BASE.clear()
    if aircraft:
        model='pitts-s2b-like' if kind=='pitts' else 'extra-300-like'
        bpy.ops.import_scene.gltf(filepath=str(MODELS/model/(model+'-own.glb')))
    s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=24;s.cycles.use_denoising=True
    s.render.resolution_x=1600;s.render.resolution_y=1000;s.render.resolution_percentage=100
    s.world=bpy.data.worlds.new('Studio');s.world.use_nodes=True
    s.world.node_tree.nodes['Background'].inputs[0].default_value=(.55,.65,.8,1)
    s.world.node_tree.nodes['Background'].inputs[1].default_value=.7
    for loc,power,size in [((1,-3,4),900,4),((-2,3,4),700,4),((0,0,9),1300,6)]:
        l=bpy.data.lights.new('Softbox','AREA');l.energy=power;l.shape='DISK';l.size=size
        o=bpy.data.objects.new('Softbox',l);s.collection.objects.link(o);o.location=loc
        o.rotation_euler=(Vector((1,0,0))-o.location).to_track_quat('-Z','Y').to_euler()
    s.view_settings.view_transform='AgX'

def shot(name,loc,target,scale=None,eye=False):
    for o in bpy.data.objects:
        if o.name.startswith('Sight_'):o.hide_render=not eye
    s=bpy.context.scene;c=bpy.data.cameras.new(name);o=bpy.data.objects.new(name,c);s.collection.objects.link(o)
    o.location=loc;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
    if scale:c.type='ORTHO';c.ortho_scale=scale
    else:c.lens=18;c.sensor_width=36
    c.clip_start=.008;c.clip_end=100;s.camera=o;s.render.filepath=str(OUT/('after2_'+name+'.png'))
    bpy.ops.render.render(write_still=True)

def rig(param,value):
    for o in bpy.data.objects:
        if o.get('rig_param')!=param:continue
        if o.name not in BASE:BASE[o.name]=(o.location.copy(),o.rotation_quaternion.copy() if o.rotation_mode=='QUATERNION' else o.rotation_euler.to_quaternion())
        loc,quat=BASE[o.name];pts=list(o['rig_map_param'])
        key='rig_map_m' if 'rig_move_axis_gltf' in o else 'rig_map_deg';vals=list(o[key])
        v=vals[-1]
        for j in range(len(pts)-1):
            if value<=pts[j+1]:
                v=vals[j]+(vals[j+1]-vals[j])*(value-pts[j])/(pts[j+1]-pts[j]);break
        axis=o['rig_move_axis_gltf'] if key=='rig_map_m' else o['rig_axis_gltf'];axis=Vector((axis[0],-axis[2],axis[1]))
        if key=='rig_map_m':o.location=loc+axis*v
        else:o.rotation_mode='QUATERNION';o.rotation_quaternion=quat@Quaternion(axis,math.radians(v))
    bpy.context.view_layer.update()

def hide_skin():
    for o in bpy.data.objects:
        if o.type=='MESH' and any(n in o.name for n in ('Fuselage','Canopy_Glass','Wing','Aileron','Strut','Wire','Cabane')):o.hide_render=True

def body(kind='pitts'):
    from pilot_body import build
    return build(kind,export=False)

def gear(headset=False):
    before=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(MODELS/'pilot-gear/pilot-gear-own.glb'))
    objects=set(bpy.data.objects)-before
    for o in objects:
        if o.name.startswith('Headset') or o.name.startswith('Grip_Headset'):o.hide_render=not headset
    return objects

def standin_cord():
    # Render evidence only. Runtime cord comes from Claude's rope implementation.
    a=bpy.data.objects['Headset_Cable_Root'].matrix_world.translation
    b=bpy.data.objects['Headset_Plug'].matrix_world.translation+Vector((0,-.034,0))
    curve=bpy.data.curves.new('HeadsetCord_RenderStandin','CURVE');curve.dimensions='3D';curve.bevel_depth=.002;curve.bevel_resolution=1
    sp=curve.splines.new('POLY');sp.points.add(23)
    for j,p in enumerate(sp.points):
        t=j/23;v=a.lerp(b,t)+Vector((0,.08*math.sin(math.pi*t),-.28*math.sin(math.pi*t)));p.co=(*v,1)
    o=bpy.data.objects.new('HeadsetCord_RenderStandin',curve);bpy.context.collection.objects.link(o)
    mat=bpy.data.materials.get('HeadsetCord')
    if mat:curve.materials.append(mat)

def hide_canopy_glass():
    for o in bpy.data.objects:
        if o.name.startswith('Canopy_Glass'):o.hide_render=True

load();body();gear()
shot('pitts_eye',(1.325,0,.30),(-1,0,-.25),eye=True)
load();hide_canopy_glass()
shot('pitts_bows_closeup',(1.12,-.68,.45),(.845,0,.16),.88)
rig('canopy_latch',1)
shot('pitts_latch_unlocked',(1.12,-.68,.45),(.845,0,.16),.88)
rig('canopy_open',1)
shot('pitts_canopy_open',(2.7,-3.1,1.8),(.8,0,.20),3.6)
load();hide_skin()
shot('pitts_frame',(1,-3,.55),(.85,0,-.20),2.1)
load();hide_skin();body()
shot('harness_latched',(.85,-.95,.65),(1.28,0,-.12),1.0)
rig('harness_latch',1)
shot('harness_released',(.85,-.95,.65),(1.28,0,-.12),1.0)
load('extra');hide_skin();body('extra')
shot('extra_harness',(1.15,-1.1,1.0),(1.68,0,.22),1.2)
load();hide_skin();body();gear(headset=True);standin_cord()
shot('headset_plugged',(1.0,.7,.45),(1.22,.18,-.02),.8)
load();body();gear()
shot('pilot_lookdown',(1.325,0,.30),(1.20,0,-.29),eye=True)
load(aircraft=False);body();gear()
shot('parachute_packed',(2.25,-1.3,.5),(1.40,0,-.11),1.45)
load(aircraft=False);arm=body();gear()
# Stand the posed body's limbs into a suspended pose; no aircraft control targets.
for side in ('L','R'):
    for segment,direction in [('UpperLeg',(0,0,-1)),('LowerLeg',(0,0,-1)),('UpperArm',(0,(-.12 if side=='L' else .12),-1)),('LowerArm',(0,0,-1))]:
        pb=arm.pose.bones[side+'_'+segment];bpy.context.view_layer.update()
        q=(pb.tail-pb.head).rotation_difference(Vector(direction));m=pb.matrix.copy();pos=m.translation.copy();m=q.to_matrix().to_4x4()@m;m.translation=pos;pb.matrix=m
# The seated harness remains a wearable mesh. Pose/cloth deformation is runtime work.
d=bpy.data.objects['Parachute_Deployed'];d.scale=(1,1,1)
c=bpy.data.objects['Parachute_Canopy']
if not c.data.shape_keys or 'Inflate' not in c.data.shape_keys.key_blocks:raise RuntimeError('Missing Inflate morph')
c.data.shape_keys.key_blocks['Inflate'].value=1
shot('parachute_deployed',(11,-15,9),(1.335,0,3.5),16)

# P2c photo comparison evidence. Same rear eye camera as after2_pitts_eye.
def shot2c(name,*args,**kwargs):
    shot(name,*args,**kwargs)
    (OUT/('after2_'+name+'.png')).replace(OUT/('after2c_'+name+'.png'))
load();body();gear()
shot2c('pitts_eye',(1.325,0,.30),(-1,0,-.25),eye=True)
load();hide_canopy_glass()
shot2c('pitts_panel_front_on',(1.34,0,.25),(.80,0,.25),.95)
shot2c('pitts_latch_locked',(1.02,-.32,.43),(.855,0,.32),.30)
rig('canopy_latch',1)
shot2c('pitts_latch_unlocked',(1.02,-.32,.43),(.855,0,.32),.30)
load()
shot2c('pitts_canopy_side',(2.7,-3.1,1.35),(.85,0,.22),2.8)
load();hide_skin()
shot2c('pitts_frame',(1,-3,.55),(.85,0,-.20),2.1)
load('extra');hide_canopy_glass()
shot2c('extra_latch_closed',(1.72,0,.62),(1.55,-.35,.25),eye=True)
rig('canopy_latch',1)
shot2c('extra_latch_squeezed',(1.72,0,.62),(1.55,-.35,.25),eye=True)
rig('canopy_open',1)
for o in bpy.data.objects:
    if o.name.startswith('Canopy_Glass'):o.hide_render=False
shot2c('extra_canopy_open',(3.2,-3.4,1.9),(1.1,0,.45),3.9)
