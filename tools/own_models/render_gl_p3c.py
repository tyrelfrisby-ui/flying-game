"""Operator-only Blender renderer. Standalone: works with GLBs from any commit.
blender -b --factory-startup --python tools/own_models/render_gl_p3c.py -- OUT [--model FILE]
Camera coordinates below are builder metres (X forward, Y left, Z up); lens mm.
No geometry hiding/cutting to make review images pass. Same cameras before/after.
"""
import argparse,json,sys
from pathlib import Path
import bpy
from mathutils import Vector
p=argparse.ArgumentParser();p.add_argument('output',type=Path);p.add_argument('--model',type=Path,default=Path('unity/Assets/_Project/Resources/Models/great-lakes-like/great-lakes-like-own.glb'))
a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);a.output.mkdir(parents=True,exist_ok=True)
CAMERAS=[
 ('windscreen_rear',(-2.22,1.0,.63),(-1.65,0,.27),55,None),
 ('windscreen_front',(-1.40,1.0,.60),(-.97,0,.26),55,None),
 ('gear_front',(3.8,0,-.72),(-.30,0,-.98),55,2.5),
 ('cockpit',(-2.16,.30,.62),(-1.60,0,.06),28,None),
 ('cabane_gear_side',(.90,2.65,.42),(-.35,0,-.40),48,None),
 ('cockpits_above',(-2.75,2.10,2.10),(-1.40,0,.12),50,None),
 ('cabane_closeup',(-1.42,-1.35,.46),(-.48,-.23,.28),38,None),
 ('side_gear_cabane',(-.40,4.5,-.35),(-.40,0,-.35),50,2.65),
]
def frame(v):return Vector((-v[0],-v[1],v[2]))
for name,pos,aim,lens,ortho in CAMERAS:
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(a.model.resolve()))
 s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=64
 s.render.resolution_x=1800;s.render.resolution_y=1400;s.render.resolution_percentage=100
 s.world=bpy.data.worlds.new('Review World');s.world.use_nodes=True
 s.world.node_tree.nodes['Background'].inputs[0].default_value=(.65,.70,.78,1);s.world.node_tree.nodes['Background'].inputs[1].default_value=.65
 d=bpy.data.cameras.new(name);o=bpy.data.objects.new(name,d);s.collection.objects.link(o);s.camera=o
 o.location=frame(pos);target=frame(aim);o.rotation_euler=(target-o.location).to_track_quat('-Z','Y').to_euler();d.lens=lens;d.sensor_width=36;d.clip_start=.005
 if ortho:d.type='ORTHO';d.ortho_scale=ortho
 for obj in list(bpy.data.objects):
  if obj.get('default_visible') is False:obj.hide_render=True
 for poslight,energy,size in [((0,3,4),700,4),((-3,-2,2),450,3),((2,-1,1),250,2)]:
  light=bpy.data.lights.new('Softbox','AREA');light.energy=energy;light.size=size
  obj=bpy.data.objects.new('Softbox',light);s.collection.objects.link(obj);obj.location=frame(poslight);obj.rotation_euler=(target-obj.location).to_track_quat('-Z','Y').to_euler()
 s.render.filepath=str(a.output/('gl3c_'+name+'.png'));bpy.ops.render.render(write_still=True)
(a.output/'gl3c_cameras.json').write_text(json.dumps(dict(model=str(a.model),builder_axes='X forward, Y left, Z up',sensor_width_mm=36,cameras=[dict(name=n,position_m=p,target_m=t,lens_mm=l,ortho_scale_m=o) for n,p,t,l,o in CAMERAS]),indent=2)+'\n')
