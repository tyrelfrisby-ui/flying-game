"""CPU-only review of the exported ski GLB; no Unity or shared build paths."""
import bpy,sys
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[2]
out=Path.home()/'Developer/agent-lanes/renders/j3-skis';out.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(root/'unity/Assets/_Project/Resources/Models/j3-cub-skis-like/j3-cub-skis-like-own.glb'))
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.device='CPU';s.cycles.samples=20
s.render.resolution_percentage=100;s.render.image_settings.file_format='PNG'
s.world=bpy.data.worlds.new('Review world');s.world.use_nodes=True
s.world.node_tree.nodes['Background'].inputs[0].default_value=(.70,.76,.84,1);s.world.node_tree.nodes['Background'].inputs[1].default_value=.8
for loc,power,size in [((-4,-5,7),1500,5),((2,4,5),1100,4)]:
    l=bpy.data.lights.new('Softbox','AREA');l.energy=power;l.size=size;o=bpy.data.objects.new('Softbox',l);s.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,0))-o.location).to_track_quat('-Z','Y').to_euler()
s.view_settings.view_transform='AgX'
for name,loc,target,scale,res in [
 ('three-quarter',(-8,-11,4),(1.3,0,-.05),11.7,(1500,1000)),
 ('side',(1.6,-20,-.1),(1.6,0,-.1),7.6,(1500,1000)),
 ('front',(-20,0,-.1),(0,0,-.1),11.4,(1600,850)),
 ('pedestal',(-2.5,-3,-.3),(-.35,-.92,-1.02),2.2,(1400,1000)),
 ('tailski',(5.5,-1.6,.7),(4.65,0,.035),1.25,(1200,900))]:
    c=bpy.data.cameras.new(name);c.type='ORTHO';c.ortho_scale=scale;o=bpy.data.objects.new(name,c);s.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();s.camera=o
    s.render.resolution_x,s.render.resolution_y=res;s.render.filepath=str(out/(name+'.png'));bpy.ops.render.render(write_still=True)
print('J3 SKIS REVIEW RENDERS DONE',out)
