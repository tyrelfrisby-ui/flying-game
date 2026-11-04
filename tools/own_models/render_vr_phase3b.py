"""Operator-only Blender presets, all cameras derived from authored eye nodes.
blender -b --factory-startup --python tools/own_models/render_vr_phase3b.py -- OUT
"""
import sys,math,json
from pathlib import Path
import bpy
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from vr_cockpit import ROOT,MODELS
out=Path(sys.argv[sys.argv.index('--')+1]);out.mkdir(parents=True,exist_ok=True)
rigdoc=json.loads((ROOT/'docs/models/ea300-own-rig.json').read_text());meta=rigdoc['phase3b']
SAMPLE={'airspeed_kias':120,'altitude_ft':2500,'turn_rate_dps':0,'slip_ball':0,'g_load':1,'g_max_seen':4,'g_min_seen':-2,'rpm':2400,'manifold_inhg':24,'oil_press_psi':70,'oil_temp_f':180,'egt_f':1300,'cht_f':350,'amps':5,'fuel_wing_gal':20,'fuel_akro_gal':8}
def frame(p):return Vector((-p[0],-p[1],p[2]))
for panel in meta['panels']:
 for kind,fov in [('eye_normal',75),('eye_tight',45),('panel_ortho',None)]:
  bpy.ops.wm.read_factory_settings(use_empty=True)
  bpy.ops.import_scene.gltf(filepath=str(MODELS/'extra-300-like/extra-300-like-own.glb'))
  # Review pose only, using the exact runtime maps; no static painted hands.
  for entry in rigdoc['rig']:
   if entry['node'].startswith(('Needle_','Ball_TC_')) and entry['param'] in SAMPLE:
    import numpy as np
    o=bpy.data.objects[entry['node']];pairs=entry['map'];deg=float(np.interp(SAMPLE[entry['param']],[p[0] for p in pairs],[p[1] for p in pairs]))
    o.rotation_mode='AXIS_ANGLE';o.rotation_axis_angle=(math.radians(deg),-1,0,0)
  s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=48
  s.render.resolution_x=2000;s.render.resolution_y=1400;s.render.resolution_percentage=100
  s.world=bpy.data.worlds.new('World');s.world.use_nodes=True;s.world.node_tree.nodes['Background'].inputs[0].default_value=(.65,.7,.8,1)
  target=frame((panel['plane_x'],0,.2175))
  data=bpy.data.cameras.new('Review');camera=bpy.data.objects.new('Review',data);s.collection.objects.link(camera);s.camera=camera
  # glTF importer maps builder to Blender (-X,-Y,Z). Preserve the real rig eye for both perspective presets.
  camera.location=frame(panel['eye_builder']) if fov else target+Vector((.35,0,0))
  camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();data.clip_start=.005
  if fov:data.type='PERSP';data.lens_unit='FOV';data.angle=math.radians(fov)
  else:data.type='ORTHO';data.ortho_scale=.48
  for o in bpy.data.objects:
   if o.get('default_visible') is False:o.hide_render=True
  for off in [(1,-.6,1),(1,.6,.4)]:
   light=bpy.data.lights.new('Softbox','AREA');light.energy=100;light.size=1
   o=bpy.data.objects.new('Softbox',light);s.collection.objects.link(o);o.location=target+Vector(off);o.rotation_euler=(target-o.location).to_track_quat('-Z','Y').to_euler()
  s.render.filepath=str(out/f"after3b_Extra_{panel['seat']}_{kind}.png");bpy.ops.render.render(write_still=True)
