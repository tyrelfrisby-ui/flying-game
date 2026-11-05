"""Pitts 1105 review cameras, shared by Blender and explicit CPU fallback.

nice -n 5 blender -b --python tools/own_models/pitts_cockpit_v2_render.py
/usr/bin/python3 tools/own_models/pitts_cockpit_v2_render.py --cpu
Outputs stay in this worktree because the requested agent-lanes directory is read-only.
Closeups are isolated assemblies; the two seat shots include the whole aircraft.
"""
import json,sys,math
from pathlib import Path
import numpy as np
from vr_cockpit import ROOT,Glb,gl
from pitts_cockpit_v2 import MODEL, opened

OUT=ROOT/'renders/cockpit-1105/codex-pitts-cockpit-v2'
ASSET=MODEL/'pitts-s2b-like-own-checker-unmarked.glb'
# name, builder eye, builder target, horizontal field of view, assembly prefixes (None = entire model)
SHOTS=[
 ('rear-seat-eye',(-1.325,0,.300),(-.60,0,.105),88,None),
 ('front-seat-eye',(-.564,0,.308),(.12,0,.09),88,None),
 ('windshield-trim',(-.92,.92,.68),(-.32,0,.30),49,('Windshield','Canopy_Frame','Canopy_Glass','Canopy_TopTube')),
 ('latch-cable',(-1.16,.76,.47),(-.65,.31,.17),47,('Canopy_Latch_Knob','Pitts_Release_Cable_Left','Canopy_Frame','Canopy_TopTube')),
 ('wing-cutout-handle',(-1.05,.50,1.45),(-.24,0,.61),42,('Wing_Upper','UpperWing_')),
 ('stop-tab-contact',(-.95,-1.05,.92),(-.57,-.70,.655),40,('Wing_Upper','Canopy_Frame','Canopy_Stop_')),
 ('fuel-tank',(-.53,.65,-.02),(.12,0,-.19),48,('FuelTank_',)),
 ('rear-panel',(-1.325,0,.25),(-.818,0,.025),82,('RearPanel_','Panel_Rim_Rear','SequenceCard_','Pitts_RearPanel_','WhiskeyCompass','Altimeter_','G_meter_')),
 ('front-panel',(-.595,0,.28),(-.1635,0,.025),88,('FrontPanel_','Panel_Rim_Front')),
 ('whiskey-compass',(-1.02,.055,.23),(-.835,0,.177),36,('WhiskeyCompass',)),
 ('fuel-valve',(-1.23,.04,.05),(-.87,-.277,-.13),52,('FuelValve_',)),
 ('prop-lever',(-1.39,-.02,.17),(-.92,.25,.025),62,('PropLever_Rear','Throttle_Rear')),
 ('trim-lever',(-1.53,-.13,.10),(-1.16,.27,-.26),65,('TrimLever_Rear','Throttle_Rear','Pitts_Trim_Position')),
 ('throttle',(-1.34,-.02,.015),(-1.145,.225,-.13),45,('Throttle_Rear',)),
]


def open_copy():
    g=Glb(ASSET);nodes={n['name']:n for n in g.doc['nodes']}
    nodes['Canopy_Slide']['translation'][0]+=.1016
    # glTF x is builder -x, so rotation about builder +X is glTF -X.
    a=math.radians(100)/2;nodes['Canopy']['rotation']=[-math.sin(a),0,0,math.cos(a)]
    p=OUT/'pitts-open-contact-review.glb';g.save(p);return p


def main():
    OUT.mkdir(parents=True,exist_ok=True);opened_asset=open_copy()
    if '--cpu' in sys.argv:
        from grob103_render_cpu import render
        for name,eye,target,fov,prefix in SHOTS:
            path=opened_asset if name=='stop-tab-contact' else ASSET
            render(path,OUT/(name+'-CPU.png'),gl(eye),gl(target),fov,
                   include=(lambda n,p=prefix:n.startswith(p)) if prefix else None,
                   label=name+' | SOFTWARE PREVIEW (Blender startup blocked)',perspective=True,
                   image_size=(1280,960),render_glass=True)
    else:
        import bpy
        from mathutils import Vector
        for name,eye,target,fov,prefix in SHOTS:
            bpy.ops.wm.read_factory_settings(use_empty=True)
            bpy.ops.import_scene.gltf(filepath=str(opened_asset if name=='stop-tab-contact' else ASSET))
            for o in bpy.context.scene.objects:
                if o.type=='MESH' and prefix:
                    # Blender may split material primitives into children of the named GLB node.
                    p=o;keep=False
                    while p:
                        if p.name.startswith(prefix):keep=True
                        p=p.parent
                    o.hide_render=not keep
            sc=bpy.context.scene;sc.render.engine='CYCLES';sc.cycles.device='CPU';sc.cycles.samples=32
            world=bpy.data.worlds.new('ReviewWorld');sc.world=world;world.use_nodes=True
            world.node_tree.nodes['Background'].inputs[0].default_value=(.70,.78,.86,1)
            world.node_tree.nodes['Background'].inputs[1].default_value=.8
            for loc,power,size in [((-2,-2,3),400,3),((1,2,2),250,2)]:
                d=bpy.data.lights.new('Softbox','AREA');d.energy=power;d.shape='DISK';d.size=size
                o=bpy.data.objects.new('Softbox',d);sc.collection.objects.link(o);o.location=loc
                o.rotation_euler=(Vector((0,0,0))-o.location).to_track_quat('-Z','Y').to_euler()
            # Imported Blender frame: builder X/Y are both negated; Z remains up.
            transform=lambda p:Vector((-p[0],-p[1],p[2]))
            d=bpy.data.cameras.new(name);d.lens=36/(2*math.tan(math.radians(fov)/2));d.clip_start=.001
            o=bpy.data.objects.new(name,d);sc.collection.objects.link(o);o.location=transform(eye)
            o.rotation_euler=(transform(target)-o.location).to_track_quat('-Z','Y').to_euler();sc.camera=o
            sc.render.resolution_x=1600;sc.render.resolution_y=1200;sc.render.resolution_percentage=100
            sc.render.filepath=str(OUT/(name+'-BLENDER.png'));bpy.ops.render.render(write_still=True)
    (OUT/'cameras.json').write_text(json.dumps(SHOTS,indent=2)+'\n')


if __name__=='__main__':main()
