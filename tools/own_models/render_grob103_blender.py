"""Optional Blender CPU/Cycles exterior evidence. Uses exported assets, never rewrites them.
PYTHONDONTWRITEBYTECODE=1 blender -b --factory-startup --python tools/own_models/render_grob103_blender.py
The local Blender currently crashes before executing Python; render_grob103.py is the CPU fallback.
"""
import bpy, math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
ID='glider-grob103-acro-like'
DEST=ROOT/'renders/grob103-own/blender';DEST.mkdir(parents=True,exist_ok=True)
def convert(p):return Vector((p[0],-p[2],p[1]))
for suffix,label in [('', 'white'),('-tf','tf')]:
 bpy.ops.wm.read_factory_settings(use_empty=True)
 bpy.ops.import_scene.gltf(filepath=str(ROOT/'unity/Assets/_Project/Resources/Models'/ID/(ID+'-own'+suffix+'.glb')))
 scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=24
 scene.render.resolution_x=1400;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
 scene.render.image_settings.file_format='PNG';scene.world=bpy.data.worlds.new('Review world');scene.world.use_nodes=True
 scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.65,.72,.8,1)
 scene.world.node_tree.nodes['Background'].inputs[1].default_value=.75
 bpy.ops.object.light_add(type='AREA',location=(-4,-6,10));bpy.context.object.data.energy=1300;bpy.context.object.data.shape='DISK';bpy.context.object.data.size=10
 for name,eye,target,scale,up in [('side',(1,.1,22),(1,.1,0),9.6,(0,1,0)),('top',(1,24,0),(1,.1,0),19.4,(-1,0,0)),('front',(-22,1,0),(0,.1,0),19.4,(0,1,0)),('three-quarter-front',(-15,9,17),(1,.1,0),18.8,(0,1,0)),('three-quarter-rear',(17,8,-17),(1,.1,0),18.8,(0,1,0)),('underside',(1,-24,0),(1,.1,0),19.4,(-1,0,0))]:
    bpy.ops.object.camera_add(location=convert(eye));cam=bpy.context.object
    direction=convert(target)-cam.location;cam.rotation_euler=direction.to_track_quat('-Z','Y').to_euler()
    if name in ('top','underside'):cam.rotation_euler.rotate_axis('Z',-math.pi/2)
    cam.data.type='ORTHO';cam.data.ortho_scale=scale;scene.camera=cam
    scene.render.filepath=str(DEST/(label+'-'+name+'.png'));bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam,do_unlink=True)
