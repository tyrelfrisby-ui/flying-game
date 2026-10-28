# matched-camera ortho overlay of the model's tail outline on the 1970 AAM three-view (0.0115 m/px)
import bpy, os, sys, math, numpy as np
from PIL import Image, ImageDraw
from mathutils import Vector
glb, out = sys.argv[-2], sys.argv[-1]
DRW = os.environ.get('GL_DRAWING', '/workspace/refs/great-lakes/aam1970-2t-1a-1e-3-view-sep-1970-aam-1600x966px.jpg')   # off-repo reference (not committed)
S = 0.0115; UP = 6; H_T = -4.59
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'FLAT'; sc.display.shading.color_type = 'OBJECT'
sc.render.film_transparent = True; sc.view_settings.view_transform = 'Standard'
sc.render.image_settings.file_format = 'PNG'; sc.render.image_settings.color_mode = 'RGBA'
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.clip_end = 100
TAIL = {'side': ('Fin', 'Rudder', 'Fuselage'), 'bottom': ('Stab', 'Elevator_L', 'Elevator_R'), 'top': ('Stab', 'Elevator_L', 'Elevator_R')}
def render(view, crop, keep):
    x0, y0, x1, y1 = crop; w, h = x1 - x0, y1 - y0
    for o in bpy.data.objects:
        if o.type == 'MESH':
            o.hide_render = not any(o.name.startswith(k) for k in keep); o.color = (1, 0, 0, 1)
    sc.render.resolution_x, sc.render.resolution_y = w * UP, h * UP
    cam.data.ortho_scale = max(w, h) * S
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    if view == 'side':   # nose left; X_model = (833 - px) S, Z = (815.6 - py) S - 0.39 ; imported X = -X_model
        Xm = (833 - cx) * S; Z = (815.6 - cy) * S - 0.39
        cam.location = (-Xm, -20, Z); cam.rotation_euler = (math.pi / 2, 0, 0)
    else:                # bottom view (nose right, hinge px HX, centreline CY) / top view (nose left)
        HX, CY = {'bottom': (1096, 483), 'top': (506, 484)}[view]
        sgn = 1 if view == 'bottom' else -1
        Xm = H_T + sgn * (cx - HX) * S; Yd = (CY - cy) * S
        cam.location = (-Xm, Yd, 20); cam.rotation_euler = (0, 0, 0)
    p = os.path.join(os.environ.get('TMPDIR', '/tmp'), 'ov_%s.png' % view); sc.render.filepath = p; bpy.ops.render.render(write_still=True)
    a = np.array(Image.open(p))[:, :, 3] > 127
    if view == 'bottom': a = a[:, ::-1]     # camera looks down with nose left; bottom view has nose right
    return a
def edge(m):
    e = m & ~(np.roll(m, 1, 0) & np.roll(m, -1, 0) & np.roll(m, 1, 1) & np.roll(m, -1, 1))
    return e | np.roll(e, 1, 0) | np.roll(e, 1, 1)
def overlay(view, crop, name):
    m = render(view, crop, TAIL[view]); x0, y0, x1, y1 = crop
    d = Image.open(DRW).convert('L').crop(crop).resize(((x1 - x0) * UP, (y1 - y0) * UP), Image.LANCZOS).convert('RGB')
    a = np.array(d); e = edge(m); a[e] = (230, 0, 0); a[m & ~e] = (a[m & ~e] * 0.8 + np.array([255, 200, 200]) * 0.2).astype(np.uint8)
    im = Image.fromarray(a); dr = ImageDraw.Draw(im)
    dr.text((8, 8), 'red = model outline (%s), grey = 1970 AAM three-view, 0.0115 m/px' % view, fill=(0, 0, 200))
    im.save(out + name)
overlay('side', (1140, 690, 1300, 830), '-side.png')
overlay('bottom', (1030, 335, 1170, 630), '-bottom.png')
overlay('top', (430, 340, 580, 640), '-top.png')
