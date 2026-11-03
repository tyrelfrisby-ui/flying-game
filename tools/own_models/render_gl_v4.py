"""Operator-only Blender renderer for the Great Lakes cockpit v4 review (Ty 2026-11-03).
blender -b --factory-startup --python tools/own_models/render_gl_v4.py -- OUT PREFIX [--model FILE]
Same cameras before and after. Builder metres (X forward, Y left, Z up); lens mm. Nothing hidden or cut.
"""
import argparse,json,sys,math
from pathlib import Path
import bpy
from mathutils import Vector
p=argparse.ArgumentParser();p.add_argument('output',type=Path);p.add_argument('prefix')
p.add_argument('--model',type=Path,default=Path('unity/Assets/_Project/Resources/Models/great-lakes-like/great-lakes-like-own.glb'))
p.add_argument('--only',default='')
a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);a.output.mkdir(parents=True,exist_ok=True)
CAMERAS=[
 ('fuel_valve_rear_right',(-2.00,-.04,.02),(-1.70,-.322,-.19),30,None),
 ('fuel_valve_front_right',(-1.32,-.04,.02),(-1.04,-.322,-.19),30,None),
 ('fuel_valve_off',(-2.00,-.04,.02),(-1.70,-.322,-.19),26,None),
 ('trim_run',(-.90,.28,-.30),(-2.20,.322,-.285),18,None),
 ('left_console_photo',(-2.04,.035,.20),(-1.73,.27,-.15),38,None),
 ('trim_rear_left_wall',(-1.91,.04,-.32),(-1.843,.322,-.285),42,None),
 ('trim_front_left_wall',(-1.35,.06,-.08),(-1.173,.322,-.285),34,None),
 ('threequarter_front_left',(1.4,3.3,.48),(-1.35,0,.18),38,None),
 ('threequarter_rear_right',(-5.4,-3.6,1.9),(-1.3,0,.10),40,None),
 ('threequarter_cockpits_close',(-3.3,1.7,1.05),(-1.35,0,.18),40,None),
 ('windshields_close_left',(-.75,1.45,.42),(-1.35,0,.24),35,None),
 ('side',(-2.0,8.0,-.25),(-2.0,0,-.25),50,6.8),
 ('side_cockpits',(-1.3,6.0,.10),(-1.3,0,.10),50,2.2),
 ('rear_seat_eye',(-1.95,0,.29),(-1.2,0,.10),20,None),
 ('rear_seat_eye_left',(-1.95,0,.29),(-1.55,.35,-.05),20,None),
 ('rear_seat_panel',(-1.95,0,.29),(-1.64,0,.03),20,None),
 ('front_seat_panel',(-1.22,0,.27),(-.97,0,.03),20,None),
 ('front_seat_eye',(-1.22,0,.27),(-.5,0,.08),20,None),
 # throttle quadrant (Ty 2026-11-03) and single-row labelled switches
 ('quadrant_rear_close',(-1.97,.05,.17),(-1.73,.30,-.06),24,None),
 ('quadrant_front_close',(-1.30,.05,.17),(-1.06,.30,-.06),24,None),
 ('quadrant_rear_side',(-1.76,.02,.02),(-1.76,.33,-.08),22,None),
 ('switchrow_rear',(-1.92,.08,.06),(-1.638,.08,-.11),24,None),
 ('switchrow_front',(-1.25,0,.04),(-.968,0,-.09),24,None),
 ('placards_rear_close',(-1.80,-.01,-.07),(-1.638,-.01,-.12),30,None),
 ('placards_front_close',(-1.13,0,-.05),(-.968,0,-.10),30,None),
]
def frame(v):return Vector((-v[0],-v[1],v[2]))
for name,pos,aim,lens,ortho in CAMERAS:
 if a.only and name not in a.only.split(','):continue
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(a.model.resolve()))
 if name=='fuel_valve_off':
  for obj in bpy.data.objects:
   if obj.name in ('Ctl_Rotary_FuelSelector','Ctl_Rotary_FuelSelector_Front'):
    obj.rotation_mode='XYZ';obj.rotation_euler=(-math.pi/2,0,0)
 s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=24;s.cycles.use_denoising=True
 s.render.resolution_x=1600;s.render.resolution_y=1200;s.render.resolution_percentage=100
 s.world=bpy.data.worlds.new('Review World');s.world.use_nodes=True
 s.world.node_tree.nodes['Background'].inputs[0].default_value=(.65,.70,.78,1);s.world.node_tree.nodes['Background'].inputs[1].default_value=.65
 d=bpy.data.cameras.new(name);o=bpy.data.objects.new(name,d);s.collection.objects.link(o);s.camera=o
 o.location=frame(pos);target=frame(aim);o.rotation_euler=(target-o.location).to_track_quat('-Z','Y').to_euler();d.lens=lens;d.sensor_width=36;d.clip_start=.005
 if ortho:d.type='ORTHO';d.ortho_scale=ortho
 for obj in list(bpy.data.objects):
  if obj.get('default_visible') is False:obj.hide_render=True
 for poslight,energy,size in [((0,3,4),700,4),((-3,-2,2),450,3),((2,-1,1),250,2),((-1.6,0,1.2),60,1)]:
  light=bpy.data.lights.new('Softbox','AREA');light.energy=energy;light.size=size
  obj=bpy.data.objects.new('Softbox',light);s.collection.objects.link(obj);obj.location=frame(poslight);obj.rotation_euler=(target-obj.location).to_track_quat('-Z','Y').to_euler()
 if name=='trim_run':
  light=bpy.data.lights.new('Trim review fill','AREA');light.energy=2;light.size=.18
  obj=bpy.data.objects.new('Trim review fill',light);s.collection.objects.link(obj);obj.location=frame((-.94,.25,-.29));obj.rotation_euler=(target-obj.location).to_track_quat('-Z','Y').to_euler()
 s.render.filepath=str(a.output/f'{a.prefix}_{name}.png');bpy.ops.render.render(write_still=True)
(a.output/f'{a.prefix}_cameras.json').write_text(json.dumps(dict(model=str(a.model),builder_axes='X forward, Y left, Z up',sensor_width_mm=36,cameras=[dict(name=n,position_m=p,target_m=t,lens_mm=l,ortho_scale_m=o) for n,p,t,l,o in CAMERAS if not a.only or n in a.only.split(',')]),indent=2)+'\n')
