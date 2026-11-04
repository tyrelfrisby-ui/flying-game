"""Operator-only Blender renders of the 2-33 canopy (Ty t295u: clear to the fuselage, no yellow band).
blender -b --factory-startup --python tools/own_models/render_233_canopy.py -- OUT PREFIX GLB [GLB...]
Builder frame X fwd, Y left, Z up; the glTF importer gives Blender (-X, -Y, Z). Glass stays visible here.
"""
import sys,math
from pathlib import Path
import bpy
from mathutils import Vector
args=sys.argv[sys.argv.index('--')+1:]
out=Path(args[0]);out.mkdir(parents=True,exist_ok=True);prefix=args[1];glbs=[Path(a) for a in args[2:]]
def B(p):return Vector((-p[0],-p[1],p[2]))
SHOTS={
 'ext_three_quarter':((4.3,2.6,1.3),(1.1,0,.15),40),
 'ext_front':((4.0,-.9,.55),(1.4,0,.15),35),
 'front_seat':((1.05,0,.30),(2.0,0,.12),95),
 'front_seat_up':((1.05,0,.30),(1.8,.05,.45),95),
}
for glb in glbs:
    tag=glb.stem.replace('glider-2-33-like-own','own')
    for name,(cam,tgt,fov) in SHOTS.items():
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.gltf(filepath=str(glb))
        for o in bpy.data.objects:
            if o.get('default_visible') is False or o.name.startswith('Pilot'):o.hide_render=True
        s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=32
        s.render.resolution_x=1600;s.render.resolution_y=1000;s.render.resolution_percentage=100
        s.world=bpy.data.worlds.new('World');s.world.use_nodes=True;bg=s.world.node_tree.nodes['Background'];bg.inputs[0].default_value=(.62,.74,.9,1);bg.inputs[1].default_value=1.0
        t=B(tgt);data=bpy.data.cameras.new('C');c=bpy.data.objects.new('C',data);s.collection.objects.link(c);s.camera=c
        c.location=B(cam);c.rotation_euler=(t-c.location).to_track_quat('-Z','Y').to_euler();data.clip_start=.01
        data.lens_unit='FOV';data.angle=math.radians(fov)
        sun=bpy.data.lights.new('Sun','SUN');sun.energy=3;so=bpy.data.objects.new('Sun',sun);s.collection.objects.link(so);so.rotation_euler=(math.radians(40),0,math.radians(-30))
        s.render.filepath=str(out/f"{prefix}_{tag}_{name}.png");bpy.ops.render.render(write_still=True)
print('done',len(glbs))
