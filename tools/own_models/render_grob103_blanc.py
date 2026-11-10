"""Render the exported Blanc GLB using Blender CPU/Cycles (not the authoring scene)."""
import bpy,json,hashlib,math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];DEST=ROOT/'renders/grob103/blanc-v1';ID='glider-grob103-acro-like';PATH=ROOT/'unity/Assets/_Project/Resources/Models'/ID/(ID+'-own.glb')
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(PATH))
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.render.resolution_x=1600;scene.render.resolution_y=1000;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.view_settings.view_transform='AgX'
scene.world=bpy.data.worlds.new('Soft studio');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.62,.70,.8,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.6
for loc,energy,size in [((-4,-6,9),1800,8),((4,5,7),1200,7),((1,-2,-4),300,5)]:
 bpy.ops.object.light_add(type='AREA',location=loc);bpy.context.object.data.energy=energy;bpy.context.object.data.shape='DISK';bpy.context.object.data.size=size;bpy.context.object.rotation_euler=(Vector((1,0,0))-bpy.context.object.location).to_track_quat('-Z','Y').to_euler()
views=[('side',(1.6,-22,.25),(1.6,0,.25),9.4),('three-quarter',(-13,-17,8),(1,0,.2),19.3),('front',(-22,0,1),(0,0,.2),19.3),('gear-main',(-.3,-2.7,-.4),(.4,0,-.43),1.4),('gear-nose',(-2.4,-2,-.35),(-1.7,0,-.40),.9),('gear-tail',(4.2,-2,-.15),(4.6,0,-.19),.85),('canopy',(-3.5,-4,2.6),(-.6,0,.12),3.9),('top',(1,0,24),(1,0,0),19.3)]
for name,eye,target,scale in views:
 bpy.ops.object.camera_add(location=eye);cam=bpy.context.object;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=scale;scene.camera=cam
 if name=='top':cam.rotation_euler.rotate_axis('Z',math.pi/2)
 scene.render.filepath=str(DEST/(name+'.png'));bpy.ops.render.render(write_still=True);bpy.data.objects.remove(cam,do_unlink=True)
(DEST/'render-manifest.json').write_text(json.dumps({'renderer':'Blender 5.2.2 CPU Cycles, 32 samples, exported GLB reimport','views':views,'model_sha256':hashlib.sha256(PATH.read_bytes()).hexdigest(),'visual_approval':'PENDING','images':{f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in DEST.glob('*.png')}},indent=2)+'\n')
