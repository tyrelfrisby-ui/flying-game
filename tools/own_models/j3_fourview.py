"""Four-view + close-ups of the J-3 Cub GLBs (imported frame: nose -x, port -y, up +z; right side +y).
  blender -b --python tools/own_models/j3_fourview.py -- model.glb outprefix [only,list] [open]
'open' rotates every node with an 'open_deg' extra (the doors) about its local x hinge line."""
import bpy, sys, math, mathutils
a = sys.argv[sys.argv.index('--') + 1:]; G, OUT = a[0], a[1]
ONLY = a[2].split(',') if len(a) > 2 and a[2] not in ('', 'all') else None
OPEN = len(a) > 3 and 'open' in a[3]
TRIM = len(a) > 3 and 'trim' in a[3]      # show the stab at full LE-down trim (-4 deg)
import os
HIDE = [h for h in os.environ.get('J3_HIDE', '').split(',') if h]   # hide nodes whose name starts with these (cutaways)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=G)
sc = bpy.context.scene
SHOW = [h for h in os.environ.get('J3_SHOW', '').split(',') if h]   # render only nodes starting with these
for o in list(sc.objects):
    if any(o.name.startswith(h) for h in HIDE): o.hide_render = True
    if SHOW and o.type == 'MESH' and not any(o.name.startswith(h) for h in SHOW): o.hide_render = True
if OPEN:
    for o in sc.objects:
        if 'open_deg' in o.keys():
            o.rotation_mode = 'XYZ'; o.rotation_euler = (math.radians(o['open_deg']), 0, 0)
if TRIM:
    for o in sc.objects:
        if 'trim_axis_gltf' in o.keys():
            g = o['trim_axis_gltf']; ax = mathutils.Vector((g[0], -g[2], g[1]))
            o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = mathutils.Quaternion(ax, -math.radians(o['trim_deg_le_down']))
for e in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try: sc.render.engine = e; break
    except Exception: pass
sc.render.film_transparent = True; sc.render.image_settings.color_mode = 'RGBA'   # composer outlines + puts on white
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.82, 0.86, 0.9, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 0.9
# v5d: sky / ground gradient so polished metal (valve covers, exhaust) reads as chrome rather than flat grey
_nt = w.node_tree; _tc = _nt.nodes.new('ShaderNodeTexCoord'); _sp = _nt.nodes.new('ShaderNodeSeparateXYZ'); _cr = _nt.nodes.new('ShaderNodeValToRGB')
_nt.links.new(_tc.outputs['Generated'], _sp.inputs[0]); _nt.links.new(_sp.outputs[2], _cr.inputs[0])
_cr.color_ramp.elements[0].position = 0.0; _cr.color_ramp.elements[0].color = (0.10, 0.09, 0.08, 1)
_cr.color_ramp.elements[1].position = 0.08; _cr.color_ramp.elements[1].color = (0.82, 0.86, 0.92, 1)
_nt.links.new(_cr.outputs[0], _nt.nodes['Background'].inputs[0])
for rot, e in (((math.radians(40), 0, math.radians(-150)), 3.0), ((math.radians(150), 0, math.radians(60)), 1.2)):
    l = bpy.data.lights.new('s', 'SUN'); l.energy = e; o = bpy.data.objects.new('s', l); o.rotation_euler = rot; sc.collection.objects.link(o)
sc.view_settings.view_transform = 'Standard'
def cam(name, loc, look, scale, res, roll=None, persp=None):
    if ONLY and name not in ONLY: return
    c = bpy.data.cameras.new(name); c.clip_start = 0.02; c.clip_end = 200
    if persp: c.type = 'PERSP'; c.lens = persp
    else: c.type = 'ORTHO'; c.ortho_scale = scale
    o = bpy.data.objects.new(name, c); sc.collection.objects.link(o); o.location = loc
    d = mathutils.Vector(look) - mathutils.Vector(loc)
    o.rotation_euler = d.to_track_quat('-Z', roll or 'Y').to_euler()
    sc.camera = o; sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.filepath = OUT + '-' + name + '.png'; bpy.ops.render.render(write_still=True)
CX, CZ = 1.55, 0.10
cam('top', (CX, 0, 20), (CX, 0, 0), 11.4, (1600, 1600), 'X')
cam('bottom', (CX, 0, -20), (CX, 0, 0), 11.4, (1600, 1600), 'X')
cam('side', (CX, -20, CZ), (CX, 0, CZ), 7.4, (1600, 1000))            # port side
cam('side_r', (CX, 20, CZ), (CX, 0, CZ), 7.4, (1600, 1000))           # right side (door side)
cam('front', (-20, 0, CZ), (0, 0, CZ), 11.2, (1600, 900))
cam('iso', (-9.0, 11.0, 5.5), (CX, 0, CZ), 11.0, (1600, 1100))
cam('photo', (-6.5, 7.2, 0.2), (1.2, 0, 0.0), 0, (1400, 1000), persp=40)
cam('cyl', (-2.9, 1.9, 0.55), (-1.37, 0.28, 0.08), 1.15, (1400, 1000))
cam('cyl_l', (-2.9, -1.9, 0.55), (-1.37, -0.28, 0.08), 1.15, (1400, 1000))
cam('nose', (-6.0, 0, 0.1), (-1.0, 0, 0.1), 1.6, (1200, 1000))
cam('door', (0.25, 8, 0.25), (0.25, 0, 0.25), 2.4, (1400, 1100))
cam('door_iso', (-2.6, 3.4, 1.1), (0.25, 0.3, 0.2), 0, (1400, 1100), persp=35)
cam('gear_front', (-8, 0, -0.75), (0, 0, -0.75), 2.6, (1400, 900))
cam('gear_iso', (-2.4, 2.7, -0.55), (-0.3, 0.5, -0.85), 0, (1400, 1000), persp=35)
cam('gear_side', (0.25, 8, -0.65), (0.25, 0, -0.65), 1.9, (1400, 1000))         # right main gear, door side
cam('tail_trim', (4.35, 8, 0.45), (4.35, 0, 0.45), 1.7, (1400, 1000))          # right side, ortho (stab at Z_S)
cam('tail', (4.6, 12, 0.6), (4.6, 0, 0.6), 1.9, (1200, 1000))
cam('tailwheel', (5.6, 1.4, 0.2), (4.75, 0, 0.05), 0, (1200, 1000), persp=40)
cam('wsh', (-2.2, 1.6, 1.0), (-0.80, 0.25, 0.33), 0.9, (1200, 900))
cam('wrudder', (4.4, -2.6, -0.45), (2.35, -0.95, -1.12), 0, (1200, 1000), persp=35)
cam('float_iso', (-4.6, -5.2, 0.8), (0.4, -0.4, -0.9), 0, (1600, 1000), persp=38)
cam('float_rig', (-8, 0, -0.75), (0, 0, -0.75), 2.9, (1400, 900))
cam('float_side', (0.95, -20, -0.9), (0.95, 0, -0.9), 5.0, (1600, 700))
cam('struts_low', (-2.6, -3.2, -1.9), (0.3, 0, -0.8), 0, (1400, 1000), persp=40)
# v4 close-ups
cam('jury', (2.2, 4.2, -0.5), (0.2, 2.2, 0.35), 0, (1400, 1000), persp=35)        # right wing struts from aft/below
cam('jury_front', (-8, 2.2, 0.25), (0, 2.2, 0.25), 1.6, (1200, 1000))
cam('jury_up', (0.2, 2.25, -1.6), (0.2, 2.25, 0.4), 1.4, (1200, 1000), 'X')       # looking straight up at the jury station
cam('stripe', (1.45, -20, 0.12), (1.45, 0, 0.12), 5.4, (1600, 600))                # port side, whole bolt
cam('stripe_nose', (-0.3, -20, 0.05), (-0.3, 0, 0.05), 1.6, (1400, 900))
cam('stripe_r', (1.45, 20, 0.12), (1.45, 0, 0.12), 5.4, (1600, 600))
cam('seats', (0.45, 1.9, 0.7), (0.4, 0, -0.15), 0, (1400, 1000), persp=30)
cam('seats_cut', (0.6, 20, -0.05), (0.6, 0, -0.05), 1.9, (1400, 1000))
cam('seats_iso', (-0.9, 1.4, 0.55), (0.35, 0, -0.15), 0, (1400, 1000), persp=28)
cam('door_fold', (-2.4, 2.6, -0.9), (0.25, 0.35, -0.45), 0, (1400, 1000), persp=35)
cam('door_front', (-8, 0.65, -0.4), (0, 0.65, -0.4), 1.1, (1100, 1000))
cam('door_aft', (8, 0.65, -0.4), (0, 0.65, -0.4), 1.1, (1100, 1000))
cam('door_side', (0.3, 8, -0.05), (0.3, 0, -0.05), 1.6, (1400, 1100))
cam('cap', (-2.0, 0.9, 0.85), (-0.95, 0, 0.45), 0, (1200, 1000), persp=50)
cam('cap_side', (-0.95, -20, 0.45), (-0.95, 0, 0.45), 0.6, (1000, 1000))
cam('brow', (-2.6, 1.3, 1.1), (-1.40, 0.28, 0.2), 1.0, (1300, 1000))
cam('brow_l', (-2.6, -1.3, 1.1), (-1.40, -0.28, 0.2), 1.0, (1300, 1000))
cam('brow_front', (-8, 0, 0.15), (0, 0, 0.15), 1.1, (1300, 800))
cam('tail_side_ov', (4.133, -20, 0.835), (4.133, 0, 0.835), 2.0, (1600, 1600))     # 1.25 mm/px, port side, nose left
cam('tail_top_ov', (4.183, 0, 20), (4.183, 0, 0), 3.2, (1600, 1600), 'X')          # 2.0 mm/px
# v5 close-ups (frame: x = xd - 1.817, y = -y_build (right side +y), z = z_build + 0.135)
cam('panel', (-0.55, 0, 0.235), (-0.587, 0, 0.235), 0.66, (1400, 900))                    # straight on (hide fuselage/liner/wing)
cam('panel_rear', (0.80, 0.0, 0.50), (-0.587, 0, 0.19), 0, (1400, 1000), persp=30)        # from the rear seat
cam('panel_rear_wide', (0.80, 0.0, 0.50), (-0.587, 0, 0.19), 0, (1400, 1000), persp=18)
cam('int_door', (0.55, 2.0, 0.40), (-0.40, 0.0, 0.12), 0, (1400, 1000), persp=28)         # through the open door (right side)
cam('exh', (-1.10, 1.6, -0.12), (-1.20, 0.25, -0.04), 0.62, (1300, 1000))                 # right side, hole + stacks
cam('exh_l', (-1.10, -1.6, -0.12), (-1.20, -0.25, -0.04), 0.62, (1300, 1000))
cam('exh_rear', (-0.50, 0.62, -0.05), (-1.32, 0.32, -0.02), 0, (1000, 1300), persp=30)    # like Ty's rear photo
cam('exh_low', (-0.55, 1.2, -0.95), (-1.10, 0.15, -0.38), 0, (1300, 1000), persp=35)      # tail stub under the lower right cowl
cam('prop_front', (-6, 0, 0.135), (-1.9, 0, 0.135), 1.95, (1800, 700))
cam('prop_back', (3, 0, 0.135), (-1.9, 0, 0.135), 1.95, (1800, 700))                       # needs J3_SHOW=Prop
cam('prop_tip_f', (-6, 0.72, 0.135), (-1.9, 0.72, 0.135), 0.42, (1200, 700))
cam('prop_tip_b', (3, 0.72, 0.135), (-1.9, 0.72, 0.135), 0.42, (1200, 700))
cam('prop_hub', (-6, 0, 0.135), (-1.9, 0, 0.135), 0.30, (1000, 1000))
cam('prop_iso', (-3.2, 1.1, 0.75), (-1.9, 0.30, 0.135), 0, (1400, 900), persp=35)
cam('brow_rear', (-0.55, 0.58, 0.06), (-1.35, 0.33, 0.05), 0, (1000, 1300), persp=30)     # rear plate, like Ty's rear photo
cam('brow_ty', (-2.1, -1.45, 0.42), (-1.36, -0.30, 0.10), 0, (1300, 1000), persp=30)     # like Ty's eyebrow photo (left side)
cam('cowl_r', (-1.30, 20, -0.06), (-1.30, 0, -0.06), 0.95, (1400, 1100))
cam('cowl_l', (-1.30, -20, -0.06), (-1.30, 0, -0.06), 0.95, (1400, 1100))
cam('cowl_dzus', (-1.05, 0.75, 0.0), (-1.06, 0.26, -0.1), 0, (1200, 1000), persp=30)
cam('bear_r', (4.13, 20, 0.855), (4.13, 0, 0.855), 0.46, (1000, 1000))
cam('bear_l', (4.13, -20, 0.855), (4.13, 0, 0.855), 0.46, (1000, 1000))
cam('brow_side_l', (-1.25, -1.7, 0.12), (-1.33, -0.30, 0.06), 0, (1300, 1000), persp=30)    # like Ty's green photo
cam('brow_front_c', (-6, -0.33, 0.12), (-1.3, -0.33, 0.12), 0.62, (1100, 900))               # one bank head-on (J3_HIDE=Prop)
cam('brow_ty2', (-1.80, -0.82, 0.13), (-1.38, -0.36, 0.05), 0, (1300, 1000), persp=30)     # Ty yellow photo: low, close, fwd-left
cam('brow_rear2', (-0.80, 0.86, 0.12), (-1.42, 0.37, 0.02), 0, (1000, 1300), persp=30)    # Ty rear photo: close behind the plate
cam('brow_3q', (-2.0, -1.10, 0.55), (-1.36, -0.30, 0.08), 0, (1300, 1000), persp=26)
cam('panel_fseat', (-0.20, 0.0, 0.30), (-0.587, 0, 0.225), 0, (1400, 1000), persp=24)          # from the front seat
cam('brow_grey', (-0.95, -0.85, 0.06), (-1.40, -0.37, 0.07), 0, (1300, 1000), persp=34)     # Ty grey photo: aft-left, low
cam('int_door2', (0.42, 1.25, 0.14), (-0.12, 0.0, -0.12), 0, (1400, 1000), persp=24)          # close, into the open door
cam('cutaway', (0.30, 8, 0.05), (0.30, 0, 0.05), 3.4, (1800, 1000))                        # J3_HIDE=Fuselage,Glass,Window,Door,Wing,Strut
cam('cutaway_3q', (1.6, 3.6, 1.6), (0.10, 0, -0.05), 0, (1600, 1100), persp=30)
cam('tail_photo_r', (4.30, 3.0, 0.95), (4.25, 0, 0.85), 0, (1000, 1100), persp=35)
