"""Offline review renders from shipping GLBs at the unchanged config front eye.
blender -b -t 4 --python tools/own_models/render_sgs233_panel_flush.py -- OUTPUT
"""
import bpy
import math
import sys
import json
from pathlib import Path
from mathutils import Vector
sys.path.insert(0, str(Path(__file__).resolve().parent))
from glb_visuals import Glb
from sights import placement, ROOT
from sgs233_panel_flush import MODELS
out = Path(sys.argv[sys.argv.index('--') + 1]); out.mkdir(parents=True, exist_ok=True)
cfg = json.loads((ROOT / 'configs/aircraft/glider-2-33-like.json').read_text())
eye = cfg['loading']['seats'][0]['eye']
import numpy as np

def B(v):  # raw glTF to Blender importer
    return Vector((v[0], -v[2], v[1]))

def render(path, tag):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    for o in bpy.data.objects:
        if o.get('default_visible') is False or o.name.startswith('Pilot'): o.hide_render = True
    g = Glb(path); m, tr = placement(g, 'glider-2-33-like')
    e = B(np.linalg.solve(m, np.array(eye) - tr))
    scene = bpy.context.scene; scene.render.engine = 'CYCLES'; scene.cycles.samples = 12
    scene.world = bpy.data.worlds.new('Sky'); scene.world.use_nodes = True
    bg = scene.world.node_tree.nodes['Background']; bg.inputs[0].default_value = (.48, .67, .9, 1)
    bg.inputs[1].default_value = .8
    sun = bpy.data.lights.new('Sun', 'SUN'); sun.energy = 2
    so = bpy.data.objects.new('Sun', sun); scene.collection.objects.link(so); so.rotation_euler = (.4, -.5, -.6)
    # Level reference ground supplies an actual horizon in both before/after frames.
    bpy.ops.mesh.primitive_plane_add(size=20000, location=(0, 0, -30))
    ground = bpy.context.object; mat = bpy.data.materials.new('Review ground'); mat.diffuse_color = (.17, .25, .15, 1)
    ground.data.materials.append(mat)
    data = bpy.data.cameras.new('ReviewCamera'); camera = bpy.data.objects.new('ReviewCamera', data)
    scene.collection.objects.link(camera); scene.camera = camera; data.clip_start = .005; data.clip_end = 20000
    shots = [('eye_iphone', e, e + Vector((-2, 0, 0)), 1170, 540, 90),
             ('eye_quest_wide', e, e + Vector((-2, 0, 0)), 1400, 800, 105),
             ('eye_panel', e, e + Vector((-1, 0, -.38)), 1200, 1000, 90)]
    # Both sides of the crown/deck junction, through the unchanged clear canopy.
    shots += [('junction_left', Vector((-1.40, -.48, .23)), Vector((-1.78, 0, .015)), 1100, 800, 65),
              ('junction_right', Vector((-1.40, .48, .23)), Vector((-1.78, 0, .015)), 1100, 800, 65)]
    for name, loc, target, width, height, fov in shots:
        if '--panel-only' in sys.argv and name != 'eye_panel': continue
        camera.location = loc; camera.rotation_euler = (target - loc).to_track_quat('-Z', 'Y').to_euler()
        data.lens_unit = 'FOV'; data.angle = math.radians(fov)
        scene.render.resolution_x = width; scene.render.resolution_y = height; scene.render.resolution_percentage = 100
        scene.render.filepath = str(out / f'{tag}_{name}.png'); bpy.ops.render.render(write_still=True)
    print('REVIEW EYE raw Blender:', list(e), 'config:', eye)

name = 'glider-2-33-like-own-tf.glb'
if '--after-only' not in sys.argv:
    render(out / 'baseline' / name, 'before')
render(MODELS / name, 'after')
