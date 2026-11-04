"""Operator-only Blender close-ups for the Extra rear throttle quadrant and the canopy latch cylinders (Ty t289u).
blender -b --factory-startup --python tools/own_models/render_extra_t289.py -- OUT PREFIX [GLB]
Builder frame X fwd, Y left, Z up; the glTF importer gives Blender (-X, -Y, Z).
"""
import sys,math
from pathlib import Path
import bpy
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
from vr_cockpit import MODELS
args=sys.argv[sys.argv.index('--')+1:]
out=Path(args[0]);out.mkdir(parents=True,exist_ok=True);prefix=args[1]
glb=Path(args[2]) if len(args)>2 else MODELS/'extra-300-like/extra-300-like-own.glb'
def B(p):return Vector((-p[0],-p[1],p[2]))
# name: camera (builder), target (builder), fov deg
SHOTS={
 'rear_seat_eye':((-1.70,0,.55),(-1.15,.05,.20),95),
 'rear_seat_left_down':((-1.70,-.02,.55),(-1.33,.30,.12),80),
 'rear_throttle_closeup':((-1.52,.08,.36),(-1.33,.33,.10),50),
 'rear_throttle_side':((-1.33,.02,.18),(-1.33,.34,.10),70),
 'rear_latch_closeup':((-1.55,.02,.40),(-1.55,.30,.225),55),
 'rear_latch_above':((-1.55,.28,.52),(-1.55,.28,.225),45),
 'front_latch_closeup':((-.70,.02,.42),(-.70,.32,.24),55),
 'front_seat_left_down':((-.80,-.02,.55),(-.45,.30,.18),80),
}
for name,(cam,tgt,fov) in SHOTS.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(glb))
    for o in bpy.data.objects:
        if o.get('default_visible') is False or o.name.startswith(('Pilot','Canopy_Glass','Glass')):o.hide_render=True
    s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=40
    s.render.resolution_x=1600;s.render.resolution_y=1100;s.render.resolution_percentage=100
    s.world=bpy.data.worlds.new('World');s.world.use_nodes=True;s.world.node_tree.nodes['Background'].inputs[0].default_value=(.7,.73,.8,1);s.world.node_tree.nodes['Background'].inputs[1].default_value=1.2
    t=B(tgt);data=bpy.data.cameras.new('C');c=bpy.data.objects.new('C',data);s.collection.objects.link(c);s.camera=c
    c.location=B(cam);c.rotation_euler=(t-c.location).to_track_quat('-Z','Y').to_euler();data.clip_start=.005
    data.lens_unit='FOV';data.angle=math.radians(fov)
    for off in [(0,0,.6),(.4,-.4,.3),(-.4,-.3,.3)]:
        L=bpy.data.lights.new('Soft','AREA');L.energy=25;L.size=.8
        o=bpy.data.objects.new('Soft',L);s.collection.objects.link(o);o.location=t+Vector(off);o.rotation_euler=(t-o.location).to_track_quat('-Z','Y').to_euler()
    s.render.filepath=str(out/f"{prefix}_{name}.png");bpy.ops.render.render(write_still=True)
