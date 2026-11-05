"""Blender review renders for the Great Lakes panels v2 (Ty 2026-11-05). Same lighting / frame as render_gl_v7.py.
blender -b --factory-startup --python tools/own_models/render_gl_panel_v2.py -- OUT PREFIX [--model FILE] [--only a,b]
Builder metres (X forward, Y left, Z up); lens mm. Poses: 'on' = every console toggle forward (ON, +80 deg about glTF +z);
'fuel=<f>' = the sight gauge's float at that fraction of the top-wing tank.
"""
import argparse, json, math, sys
from pathlib import Path
import bpy
from mathutils import Vector

p = argparse.ArgumentParser(); p.add_argument('output', type=Path); p.add_argument('prefix')
p.add_argument('--model', type=Path, default=Path('unity/Assets/_Project/Resources/Models/great-lakes-like/great-lakes-like-own.glb'))
p.add_argument('--only', default='')
a = p.parse_args(sys.argv[sys.argv.index('--') + 1:]); a.output.mkdir(parents=True, exist_ok=True)
TUBE = (-0.836, -0.690, 0.535)
CAMERAS = [
    # name, eye, aim, lens, pose
    ('rear_seat_eye', (-1.95, 0, .29), (-1.64, 0, .02), 20, ''),
    ('rear_seat_eye_wide', (-1.95, 0, .29), (-1.2, 0, .10), 16, ''),
    ('front_seat_eye', (-1.22, 0, .27), (-.97, -.05, -.02), 20, ''),
    ('front_seat_eye_wide', (-1.22, 0, .27), (-.5, 0, .08), 16, ''),
    ('rear_card_space_close', (-1.86, 0, .07), (-1.64, 0, .008), 28, ''),
    ('rear_panel_straight', (-2.02, 0, .06), (-1.64, 0, .03), 24, ''),
    ('front_card_close', (-1.17, -.12, .06), (-.97, -.257, -.086), 30, ''),
    ('radios_under_panel', (-1.86, -.14, .05), (-1.64, -.27, -.109), 32, ''),
    ('switch_console_rear_off', (-1.90, .12, .12), (-1.963, .305, -.111), 30, ''),
    ('switch_console_rear_on', (-1.90, .12, .12), (-1.963, .305, -.111), 30, 'on'),
    ('mag_switches_close_off', (-1.96, .21, .01), (-2.0155, .305, -.111), 45, ''),
    ('mag_switches_close_on', (-1.96, .21, .01), (-2.0155, .305, -.111), 45, 'on'),
    ('switch_console_front', (-1.23, .12, .14), (-1.293, .305, -.091), 30, ''),
    ('sight_tube_full', (-1.22, 0, .27), TUBE, 55, 'fuel=1'),
    ('sight_tube_half', (-1.22, 0, .27), TUBE, 55, 'fuel=0.5'),
    ('sight_tube_low', (-1.22, 0, .27), TUBE, 55, 'fuel=0.15'),
    ('sight_tube_context', (-1.22, 0, .27), (-.80, -.55, .45), 22, 'fuel=0.5'),
    ('sight_tube_outside', (-0.35, -1.45, .45), (-.80, -.62, .52), 35, 'fuel=0.5'),
]
EMPTY_DY = -0.108                     # gl_panel_v2: BALL_EMPTY - BALL_FULL


def frame(v): return Vector((-v[0], -v[1], v[2]))


for name, pos, aim, lens, pose in CAMERAS:
    if a.only and name not in a.only.split(','): continue
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=str(a.model.resolve()))
    if pose == 'on':
        for obj in bpy.data.objects:
            if obj.name.startswith('Ctl_Toggle_') and 'Smoke' not in obj.name:
                obj.rotation_mode = 'XYZ'; obj.rotation_euler.y = -math.radians(80)
    if pose.startswith('fuel='):
        f = float(pose.split('=')[1]); obj = bpy.data.objects['GL_FuelSight_Float']; obj.location.z += EMPTY_DY * (1 - f)
    s = bpy.context.scene; s.render.engine = 'CYCLES'; s.cycles.samples = 24; s.cycles.use_denoising = True
    s.render.resolution_x = 1400; s.render.resolution_y = 1050; s.render.resolution_percentage = 100
    s.world = bpy.data.worlds.new('Review World'); s.world.use_nodes = True
    s.world.node_tree.nodes['Background'].inputs[0].default_value = (.65, .70, .78, 1); s.world.node_tree.nodes['Background'].inputs[1].default_value = .65
    d = bpy.data.cameras.new(name); o = bpy.data.objects.new(name, d); s.collection.objects.link(o); s.camera = o
    o.location = frame(pos); target = frame(aim); o.rotation_euler = (target - o.location).to_track_quat('-Z', 'Y').to_euler(); d.lens = lens
    d.sensor_width = 36; d.clip_start = .005
    for obj in list(bpy.data.objects):
        if obj.get('default_visible') is False: obj.hide_render = True
    for poslight, energy, size in [((0, 3, 4), 700, 4), ((-3, -2, 2), 450, 3), ((2, -1, 1), 250, 2), ((-1.6, 0, 1.2), 60, 1)]:
        light = bpy.data.lights.new('Softbox', 'AREA'); light.energy = energy; light.size = size
        lo = bpy.data.objects.new('Softbox', light); s.collection.objects.link(lo); lo.location = frame(poslight)
        lo.rotation_euler = (target - lo.location).to_track_quat('-Z', 'Y').to_euler()
    s.render.filepath = str(a.output / f'{a.prefix}_{name}.png'); bpy.ops.render.render(write_still=True)
(a.output / f'{a.prefix}_cameras.json').write_text(json.dumps(dict(model=str(a.model), builder_axes='X forward, Y left, Z up', cameras=[
    dict(name=n, eye_m=p_, aim_m=t, lens_mm=l, pose=po) for n, p_, t, l, po in CAMERAS if (a.output / f'{a.prefix}_{n}.png').exists()]), indent=1) + '\n')
