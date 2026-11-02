"""Blender evidence cameras. Imported frame: nose -X, port -Y, up Z.
blender -b --factory-startup --python tools/own_models/render_vr_phase1.py -- before|after output_dir
Before is run before asset edits; no body existed in that baseline.
"""
import sys
if '--contact' in sys.argv:
    from pathlib import Path
    from PIL import Image, ImageDraw
    directory=Path(sys.argv[-1])
    names=['pitts_eye','pitts_eye_body','pitts_frame','pitts_pedals','pitts_latch_locked','pitts_latch_unlocked','extra_latch_closed','extra_latch_squeezed','extra_body']
    paths=[directory/f'{phase}_{name}.png' for name in names for phase in ('before','after')]
    missing=[p.name for p in paths if not p.exists()]
    if missing: raise SystemExit('Contact sheet requires all 18 renders; missing: '+', '.join(missing))
    canvas=Image.new('RGB',(1600,530*len(names)),(25,28,32));draw=ImageDraw.Draw(canvas)
    for i,path in enumerate(paths):
        im=Image.open(path).convert('RGB');im.thumbnail((800,500))
        x=(i%2)*800;y=(i//2)*530;canvas.paste(im,(x,y+30));draw.text((x+12,y+9),path.name,fill='white')
    canvas.save(directory/'contact_phase1.png');raise SystemExit(0)
import bpy, sys, math, subprocess, tempfile
from pathlib import Path
from mathutils import Vector
ROOT = Path(__file__).resolve().parents[2]
phase, directory = sys.argv[sys.argv.index('--')+1:]
OUT = Path(directory)
def load(kind):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    model = 'pitts-s2b-like' if kind == 'pitts' else 'extra-300-like'
    relative=f'unity/Assets/_Project/Resources/Models/{model}/{model}-own.glb'
    if phase=='before':
        # Always recover the pre-lane published baseline, even on a later rerun.
        baseline=Path(tempfile.gettempdir())/f'vr-cockpit-phase1-before-{model}.glb'
        baseline.write_bytes(subprocess.check_output(['git','show','0a1a7b4:'+relative],cwd=ROOT))
        path=baseline
    else: path=ROOT/relative
    bpy.ops.import_scene.gltf(filepath=str(path))
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'; sc.cycles.samples = 12; sc.cycles.use_denoising = True
    sc.render.resolution_x = 1600; sc.render.resolution_y = 1000; sc.render.resolution_percentage = 100
    sc.world = bpy.data.worlds.new('Studio'); sc.world.use_nodes = True
    sc.world.node_tree.nodes['Background'].inputs[0].default_value = (.55,.65,.8,1)
    sc.world.node_tree.nodes['Background'].inputs[1].default_value = .7
    for loc, power in [((1,-3,4),700),((-2,2,3),500)]:
        l=bpy.data.lights.new('Softbox','AREA'); l.energy=power; l.shape='DISK'; l.size=4
        o=bpy.data.objects.new('Softbox',l); sc.collection.objects.link(o); o.location=loc
        o.rotation_euler=(Vector((.8,0,0))-o.location).to_track_quat('-Z','Y').to_euler()
    sc.view_settings.view_transform='AgX'
def shot(name,loc,target,scale=None):
    sc=bpy.context.scene; c=bpy.data.cameras.new(name); o=bpy.data.objects.new(name,c); sc.collection.objects.link(o)
    o.location=loc; o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
    if scale: c.type='ORTHO'; c.ortho_scale=scale
    else: c.lens=18; c.sensor_width=36
    c.clip_start=.008; sc.camera=o; sc.render.filepath=str(OUT/f'{phase}_{name}.png')
    bpy.ops.render.render(write_still=True)
def hide_skin():
    for o in bpy.data.objects:
        if o.type=='MESH' and any(s in o.name for s in ('Fuselage','Canopy_Glass','Wing','Aileron','Strut','Wire','Cabane')): o.hide_render=True
def body(kind):
    if phase=='after':
        sys.path.insert(0,str(Path(__file__).parent))
        from pilot_body import build
        build(kind, export=False)
load('pitts')
shot('pitts_eye',(1.325,0,.30),(-1,0,-.4))
body('pitts')
shot('pitts_eye_body',(1.325,0,.30),(-1,0,-.4))
for o in bpy.data.objects:
    if o.name.startswith('Pilot'): o.hide_render=True
hide_skin()
shot('pitts_frame',(1.0,-3,.55),(.85,0,-.20),2.1)
shot('pitts_pedals',(.62,0,2),(.62,0,-.4),1.6)
shot('pitts_latch_locked',(1.13,-.9,.7),(.8,0,.28),1.05)
if phase=='after':
    o=bpy.data.objects.get('Canopy_Latch_Knob')
    if o: o.location.y-=.0762
    pin=bpy.data.objects.get('Canopy_Latch_Pin')
    if pin: pin.location.y-=.0762
shot('pitts_latch_unlocked',(1.13,-.9,.7),(.8,0,.28),1.05)
load('extra')
hide_skin()
shot('extra_latch_closed',(1.8,-1.2,.9),(1.55,-.32,.23),.65)
if phase=='after':
    for o in bpy.data.objects:
        if o.name.startswith('Canopy_Latch_Handle_') and not o.name.endswith('_White'): o.rotation_euler.x=math.radians(-25 if '_Handle_L' in o.name else 25)
shot('extra_latch_squeezed',(1.8,-1.2,.9),(1.55,-.32,.23),.65)
body('extra')
shot('extra_body',(2.6,-2,1.3),(1.35,0,.0),2.4)
