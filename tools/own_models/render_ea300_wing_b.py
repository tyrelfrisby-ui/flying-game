"""Identical CPU/Cycles wing review cameras for baseline and rebuilt GLBs.
blender -b --python tools/own_models/render_ea300_wing_b.py -- GLB before|after
"""
import bpy, bmesh, sys, math
from pathlib import Path
from mathutils import Vector
args=sys.argv[sys.argv.index('--')+1:]; glb,label=args
out=Path(__file__).resolve().parents[2]/'renders/extra-wing-b';out.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=glb)
sc=bpy.context.scene;sc.render.engine='CYCLES';sc.cycles.device='CPU';sc.cycles.samples=24
sc.render.resolution_percentage=100
sc.world=bpy.data.worlds.new('WingReview');sc.world.use_nodes=True
sc.world.node_tree.nodes['Background'].inputs[0].default_value=(.66,.72,.8,1);sc.world.node_tree.nodes['Background'].inputs[1].default_value=.8
for loc,power,size in [((-3,-4,7),1800,6),((3,3,5),1100,5)]:
    l=bpy.data.lights.new('softbox','AREA');l.energy=power;l.shape='DISK';l.size=size
    o=bpy.data.objects.new('softbox',l);sc.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((.4,-2,0))-o.location).to_track_quat('-Z','Y').to_euler()
sc.view_settings.view_transform='Standard'
def cam(loc,target,scale,res,name):
    d=bpy.data.cameras.new(name);d.type='ORTHO';d.ortho_scale=scale;o=bpy.data.objects.new(name,d);sc.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();sc.camera=o
    sc.render.resolution_x,sc.render.resolution_y=res;sc.render.filepath=str(out/(label+'-'+name+'.png'));bpy.ops.render.render(write_still=True)
# Imported Blender coordinates: nose -X, port -Y, up Z.
cam((-5,-7,4),(.25,-1.8,.08),5.7,(1600,1000),'34')
# Isolate a 2 mm thick exposed-root cut, filled, so aft skin cannot hide behind another station.
for o in list(sc.objects):
    if o.type=='MESH' and o.name!='Wing':o.hide_render=True
wing=bpy.data.objects.get('Wing');bm=bmesh.new();bm.from_mesh(wing.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-5)
for y,n in [(-.449,(0,1,0)),(-.451,(0,-1,0))]:
    ret=bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),plane_co=(0,y,0),plane_no=n,clear_outer=True,dist=1e-7)
    edges=[e for e in ret['geom_cut'] if isinstance(e,bmesh.types.BMEdge) and e.is_boundary]
    if edges:bmesh.ops.holes_fill(bm,edges=edges,sides=0)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(wing.data);bm.free()
m=bpy.data.materials.new('Section charcoal');m.diffuse_color=(.035,.055,.085,1);wing.data.materials.clear();wing.data.materials.append(m)
for p in wing.data.polygons:p.material_index=0
cam((.5,3,.075),(.5,-.45,.075),2.25,(1600,600),'root-end')
