"""Operator-only Blender presets. Run: blender -b --factory-startup --python ... -- OUT"""
import sys,math,json
from pathlib import Path
import bpy
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from vr_phase3 import SPECS
from vr_cockpit import MODELS,ROOT
out=Path(sys.argv[sys.argv.index('--')+1]);out.mkdir(parents=True,exist_ok=True)

def render(model,seat,label=None,value=0):
 bpy.ops.wm.read_factory_settings(use_empty=True)
 bpy.ops.import_scene.gltf(filepath=str(MODELS/model/(model+'-own.glb')))
 key,xs,z=SPECS[model];x=xs[seat=='Front'];suffix='_Front' if seat=='Front' else ''
 for o in bpy.data.objects:
  if o.name.startswith(('Sight_','Canopy_Glass')) or o.get('default_visible') is False:o.hide_render=True
 if label:
  kind='Toggle' if label=='Smoke' else 'Rotary';o=bpy.data.objects[f'Ctl_{kind}_{label}{suffix}'];o.rotation_mode='AXIS_ANGLE'
  # Imported Blender frame is X=-builder X, Y=-builder Y, Z=builder Z.
  axis=(0,-1,0) if kind=='Toggle' else (-1,0,0)
  o.rotation_axis_angle=(math.radians((40 if label=='Smoke' else 130)*value),*axis)
  target=o.matrix_world.translation.copy();scale=.18
 else:target=Vector((-x-.10,0,z+.22));scale=.95
 s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=32;s.render.resolution_x=1600;s.render.resolution_y=1100;s.render.resolution_percentage=100
 s.world=bpy.data.worlds.new('World');s.world.use_nodes=True;s.world.node_tree.nodes['Background'].inputs[0].default_value=(.6,.65,.75,1)
 for loc in [target+Vector((1,-1,2)),target+Vector((.4,1,1))]:
  data=bpy.data.lights.new('Softbox','AREA');data.energy=500;data.size=2;o=bpy.data.objects.new('Softbox',data);s.collection.objects.link(o);o.location=loc;o.rotation_euler=(target-o.location).to_track_quat('-Z','Y').to_euler()
 data=bpy.data.cameras.new('Review');camera=bpy.data.objects.new('Review',data);s.collection.objects.link(camera);camera.location=target+Vector((.8,0,.65));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=scale;data.clip_start=.005;s.camera=camera
 name=f'after3a_{model}_{seat}'+(f'_{label}_{value}' if label else '_panel_controls')
 s.render.filepath=str(out/(name+'.png'));bpy.ops.render.render(write_still=True)
for model in SPECS:
 for seat in ('Rear','Front'):render(model,seat)
 render(model,'Rear','Smoke',0);render(model,'Rear','Smoke',1)
for v in (0,1):render('pitts-s2b-like','Rear','Magneto',v)
