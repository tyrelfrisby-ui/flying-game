"""Shared sequence-holder geometry for Blender builders and CPU incremental GLB updates.

Builder axes: nose +X, port +Y, up +Z. All dimensions in metres.
Native output uses the builders' existing baked glTF frame (-x,z,y).
"""
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
ART = HERE/'art/sequence-card-sportsman-known-2026.png'
WIDTH = .10795
HEIGHT = WIDTH*11/8.5
MATERIAL_RGB = dict(frame=(.012,.013,.016), clip=(.38,.40,.43), paper=(.92,.92,.87))
# Panel outline in (pilot-right u, model-up v), independent of the glareshield.
OUTLINES = {
 'extra-300-like':[(-.31,.272),(.31,.272),(.31,.338),(.20,.428),(-.20,.428),(-.31,.338)],
 'pitts-s2b-like':[(-.30,-.12),(.30,-.12),(.31,.12),(.22,.15),(-.22,.15),(-.31,.12)],
 'great-lakes-like':[(-.26,-.12),(.26,-.12),(.27,.06),(.16,.13),(-.16,.13),(-.27,.06)]}
PLACEMENTS = {
 'extra-300-like':dict(panel_face=-1.202,panel_top=.428,centre_z=.350),
 'pitts-s2b-like':dict(panel_face=-.8075,panel_top=.158,centre_z=.015),
 'great-lakes-like':dict(panel_face=-1.6395,panel_top=.136,centre_z=.005)}


def box(centre, size):
    x,y,z = centre; a,b,c = [v/2 for v in size]
    v = [(x+sx*a,y+sy*b,z+sz*c) for sx,sy,sz in
         ((-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1))]
    faces = ((0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7))
    return v,[(a,b,c) for a,b,c,d in faces]+[(a,c,d) for a,b,c,d in faces]


def geometry(panel_face=0, panel_top=.1, centre_z=0):
    # Flush surface mount: paper face 1.2 mm aft of panel; all hardware <= 3 mm.
    cz=centre_z; x=panel_face-.0012; rail=.003
    top=cz+HEIGHT/2+rail
    boxes=[]
    def add(c,s,material): boxes.append((*box(c,s),material))
    add((panel_face-.0004,0,cz),(.0008,WIDTH+2*rail,HEIGHT+2*rail),'frame')
    for side in (-1,1):
        add((panel_face-.0015,side*(WIDTH/2+rail/2),cz),(.003,rail,HEIGHT+2*rail),'frame')
    add((panel_face-.0015,0,cz-HEIGHT/2-rail/2),(.003,WIDTH+2*rail,rail),'frame')
    add((panel_face-.0009,0,cz),(.0004,WIDTH,HEIGHT),'paper')
    add((panel_face-.0015,0,top-rail/2),(.003,.022,rail),'clip')
    # Pilot sees u increase to their right (-Y), v increase up. glTF texture v is downward.
    face = [(x,WIDTH/2,cz-HEIGHT/2),(x,-WIDTH/2,cz-HEIGHT/2),
            (x,-WIDTH/2,cz+HEIGHT/2),(x,WIDTH/2,cz+HEIGHT/2)]
    return boxes,face,dict(centre_builder=[x,0,cz],centre_gltf=[-x,cz,0],
                          panel_top=panel_top,holder_top=top,above=0,width=WIDTH,height=HEIGHT,
                          panel_face_builder=panel_face,face_offset_m=.0012,max_hardware_offset_m=.003,
                          holder_width=WIDTH+.006,holder_height=HEIGHT+.006,
                          cockpit='rear',panel_coordinates='u=pilot right, v=model up; origin on centreline at model Z=0',
                          card_top=cz+HEIGHT/2,card_above_panel=max(0,cz+HEIGHT/2-panel_top),
                          holder_protrusion=0)


def blender_add(parts, aircraft):
    import bpy
    import paint
    boxes,face,spec = geometry(**PLACEMENTS[aircraft])
    mats = dict(frame=paint.mat_colour('SequenceCard_Frame', (28,30,34),rough=.9),
                clip=paint.mat_colour('SequenceCard_Clip',(165,170,176),rough=.7,metal=.3),
                paper=paint.mat_colour('SequenceCard_Stock',(246,246,240),rough=.9),
                face=paint.mat_image('SequenceCard_Face',bpy.data.images.load(str(ART),check_existing=True),.9))
    for key,rgb in MATERIAL_RGB.items():
        mats[key].node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(*rgb,1)
    for name,kind in (('SequenceCard_Holder','frame'),('SequenceCard_Clip','clip'),('SequenceCard_Stock','paper')):
        vertices=[]; triangles=[]
        for v,t,k in boxes:
            if k != kind: continue
            off=len(vertices);vertices.extend(v);triangles.extend(tuple(i+off for i in tri) for tri in t)
        me=bpy.data.meshes.new(name);me.from_pydata(vertices,[],triangles);me.materials.append(mats[kind])
        obj=bpy.data.objects.new(name,me);bpy.context.scene.collection.objects.link(obj);parts[name]=obj
    me=bpy.data.meshes.new('SequenceCard_Face');me.from_pydata(face,[],[(0,1,2),(0,2,3)]);me.materials.append(mats['face'])
    uv=me.uv_layers.new();uvs=((0,0),(1,0),(1,1),(0,1))
    for poly in me.polygons:
        for index in poly.loop_indices: uv.data[index].uv=uvs[me.loops[index].vertex_index]
    obj=bpy.data.objects.new('SequenceCard_Face',me);bpy.context.scene.collection.objects.link(obj);parts[obj.name]=obj
    from rear_panel import blender_add as add_panel
    add_panel(parts, aircraft)
    return spec


def native_add(glb, aircraft=None):
    # Remove obsolete static compass nodes; the new PIC compass is the sole compass.
    if aircraft=='extra-300-like':
        remove={i for i,n in enumerate(glb.doc['nodes']) if n.get('name','').startswith('PanelCompass_')}
        remap={old:new for new,old in enumerate(i for i in range(len(glb.doc['nodes'])) if i not in remove)}
        glb.doc['nodes']=[n for i,n in enumerate(glb.doc['nodes']) if i not in remove]
        for scene in glb.doc['scenes']: scene['nodes']=[remap[i] for i in scene['nodes'] if i not in remove]
    boxes,face,spec=geometry(**PLACEMENTS[aircraft]) if aircraft else geometry()
    materials = dict(frame=glb.material('SequenceCard_Frame',MATERIAL_RGB['frame']),
                     clip=glb.material('SequenceCard_Clip',MATERIAL_RGB['clip']),
                     paper=glb.material('SequenceCard_Stock',MATERIAL_RGB['paper']),
                     face=glb.material('SequenceCard_Face',png=ART))
    glb.doc['materials'][materials['clip']]['pbrMetallicRoughness'].update(metallicFactor=.3,roughnessFactor=.7)
    convert=lambda v:[(-x,z,y) for x,y,z in v]
    for name,kind in (('SequenceCard_Holder','frame'),('SequenceCard_Clip','clip'),('SequenceCard_Stock','paper')):
        vertices=[];triangles=[]
        for v,t,k in boxes:
            if k!=kind:continue
            off=len(vertices);vertices.extend(v);triangles.extend(tuple(i+off for i in tri) for tri in t)
        glb.mesh(name,convert(vertices),triangles,materials[kind])
    glb.mesh('SequenceCard_Face',convert(face),[(0,1,2),(0,2,3)],materials['face'],[(0,1),(1,1),(1,0),(0,0)])
    glb.doc.setdefault('extras',{})['sequenceCard']=spec
    if aircraft:
        from rear_panel import native_add as add_panel
        add_panel(glb, aircraft)


def offline_update(aircraft, filename):
    from glb_visuals import Glb
    path=ROOT/'unity/Assets/_Project/Resources/Models'/aircraft/filename
    # Rebuild from the immutable round-1 GLB: repeatable without dead-buffer accumulation.
    import subprocess, tempfile
    with tempfile.TemporaryDirectory(prefix='iac-v2-base-') as td:
        base=Path(td)/path.name
        base.write_bytes(subprocess.check_output(['git','show','38e8746:'+str(path.relative_to(ROOT))],cwd=ROOT))
        glb=Glb(base)
    native_add(glb,aircraft);glb.save(path)
    print('WROTE',path)


if __name__=='__main__':
    import json
    from glb_visuals import Glb
    glb=Glb();native_add(glb)
    path=ROOT/'unity/Assets/_Project/Resources/Models/sequence-card/sequence-card-holder.glb'
    path.parent.mkdir(parents=True,exist_ok=True);glb.save(path)
    specs={key:geometry(**value)[2] for key,value in PLACEMENTS.items()}
    specs['reusable-decathlon']=geometry()[2]
    from rear_panel import LAYOUTS, before
    for key in PLACEMENTS:
        specs[key].update(panel_outline_uv=OUTLINES[key],instruments=LAYOUTS[key],before_instruments=before(key))
        if key=='extra-300-like':
            specs[key].update(panel_tier='upper',original_upper_height_m=.120,upper_downward_extension_m=.036,
                              lower_panel_face_builder=-1.162,lower_recess_restored=True,
                              lower_panel_outline_uv=[[-.30,.148],[.30,.148],[.30,.308],[-.30,.308]],
                              mount_surface_nodes=['Cockpit','RearPanel_UpperExtension'])
    specpath=ROOT/'docs/models/sequence-card-spec.json'
    prior=json.loads(specpath.read_text()) if specpath.exists() else {}
    for key in PLACEMENTS:
        if 'rear_eye' in prior.get(key,{}): specs[key]['rear_eye']=prior[key]['rear_eye']
    specpath.write_text(json.dumps(specs,indent=2)+'\n')
    print('WROTE',path)
