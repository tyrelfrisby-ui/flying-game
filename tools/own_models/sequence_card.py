"""Shared sequence-holder geometry for Blender builders and CPU incremental GLB updates.

Builder axes: nose +X, port +Y, up +Z. All dimensions in metres.
Native output uses the builders' existing baked glTF frame (-x,z,y).
"""
from pathlib import Path
import math

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
ART = HERE/'art/sequence-card-sportsman-known-2026.png'
WIDTH = .120
HEIGHT = WIDTH*11/8.5
MATERIAL_RGB = dict(frame=(.012,.013,.016), clip=(.38,.40,.43), paper=(.92,.92,.87))
PLACEMENTS = {
    'extra-300-like': dict(panel_face=-1.202, panel_top=.428, above=.050),
    'pitts-s2b-like': dict(panel_face=-.8075, panel_top=.150, above=.170),
    'great-lakes-like': dict(panel_face=-1.6395, panel_top=.136, above=.170),
}


def box(centre, size):
    x,y,z = centre; a,b,c = [v/2 for v in size]
    v = [(x+sx*a,y+sy*b,z+sz*c) for sx,sy,sz in
         ((-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1))]
    faces = ((0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7))
    return v,[(a,b,c) for a,b,c,d in faces]+[(a,c,d) for a,b,c,d in faces]


def geometry(panel_face=0, panel_top=0, above=.170):
    top = panel_top+above; cz = top-.006-HEIGHT/2; x = panel_face-.012
    boxes = []
    def add(c,s,material): boxes.append((*box(c,s),material))
    add((x+.007,0,cz),(.004,WIDTH+.012,HEIGHT+.012),'frame')
    for side in (-1,1): add((x+.0005,side*(WIDTH/2+.003),cz),(.005,.006,HEIGHT+.012),'frame')
    add((x-.0005,0,cz-HEIGHT/2-.003),(.007,WIDTH+.012,.006),'frame')
    add((x+.002,0,cz),(.003,WIDTH,HEIGHT),'paper')
    add((x-.002,0,top-.003),(.004,.030,.006),'clip')
    # Short central mounting foot reaches down to the panel when the whole card is above it.
    bottom = cz-HEIGHT/2-.006
    if bottom > panel_top:
        add((panel_face-.003,0,(bottom+panel_top)/2),(.012,.022,bottom-panel_top+.008),'frame')
    # Pilot sees u increase to their right (-Y), v increase up. glTF texture v is downward.
    face = [(x,WIDTH/2,cz-HEIGHT/2),(x,-WIDTH/2,cz-HEIGHT/2),
            (x,-WIDTH/2,cz+HEIGHT/2),(x,WIDTH/2,cz+HEIGHT/2)]
    return boxes,face,dict(centre_builder=[x,0,cz],centre_gltf=[-x,cz,0],
                          panel_top=panel_top,holder_top=top,above=above,width=WIDTH,height=HEIGHT)


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
    if aircraft == 'extra-300-like':
        for name,v,t,uv,png in compass_geometry():
            me=bpy.data.meshes.new(name);me.from_pydata(v,[],t)
            mat=paint.mat_image('PanelCompass_Face',bpy.data.images.load(str(png),check_existing=True),.9) if png else paint.mat_colour('PanelCompass_Housing',(25,27,30),rough=.9)
            if not png: mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.01,.011,.013,1)
            me.materials.append(mat)
            if uv:
                layer=me.uv_layers.new()
                for poly in me.polygons:
                    for index in poly.loop_indices:
                        u,vv=uv[me.loops[index].vertex_index];layer.data[index].uv=(u,1-vv)
            obj=bpy.data.objects.new(name,me);bpy.context.scene.collection.objects.link(obj);parts[name]=obj
    return spec


def native_add(glb, aircraft=None):
    if any(n.get('name')=='SequenceCard_Face' for n in glb.doc['nodes']):
        # Refresh authored art without accumulating duplicate meshes on repeated incremental builds.
        for name,png in (('SequenceCard_Face',ART),('PanelCompass_Face',HERE/'art/panel-compass-static.png')):
            for mat in glb.doc['materials']:
                if mat.get('name') != name: continue
                tex=mat['pbrMetallicRoughness']['baseColorTexture']['index']
                image=glb.doc['images'][glb.doc['textures'][tex]['source']]
                view=glb.doc['bufferViews'][image['bufferView']];start=view.get('byteOffset',0)
                raw=png.read_bytes()
                if bytes(glb.bin[start:start+view['byteLength']]) != raw: image['bufferView']=glb.data(raw)
        return
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
    if aircraft == 'extra-300-like':
        for name,v,t,uv,png in compass_geometry():
            mat=glb.material(name,png=png) if png else glb.material(name,(.01,.011,.013))
            glb.mesh(name,convert(v),t,mat,uv)


def compass_geometry():
    """Static panel compass for this base; the newer Extra cockpit lane has a rigged replacement."""
    x,y,z=-1.202,-.145,.366
    segments=64
    ring=lambda xx,r:[(xx,y+r*math.cos(t),z+r*math.sin(t)) for t in [k*2*math.pi/segments for k in range(segments)]]
    vertices=ring(x+.002,.033)+ring(x-.009,.033)
    tris=[]
    for k in range(segments):
        j=(k+1)%segments;tris.extend([(k,segments+j,j),(k,segments+k,segments+j)])
    tris.extend((segments,segments+k+1,segments+k) for k in range(1,segments-1))
    housing=('PanelCompass_Housing',vertices,tris,None,None)
    vertices=[(x-.010,y,z)]+ring(x-.010,.030)
    tris=[(0,1+(k+1)%segments,1+k) for k in range(segments)]
    uv=[(.5-(yy-y)/.060,.5-(zz-z)/.060) for xx,yy,zz in vertices]
    return [housing,('PanelCompass_Face',vertices,tris,uv,HERE/'art/panel-compass-static.png')]


def offline_update(aircraft, filename):
    from glb_visuals import Glb
    path=ROOT/'unity/Assets/_Project/Resources/Models'/aircraft/filename
    glb=Glb(path);native_add(glb,aircraft);glb.save(path)
    print('WROTE',path)


if __name__=='__main__':
    import json
    from glb_visuals import Glb
    glb=Glb();native_add(glb)
    path=ROOT/'unity/Assets/_Project/Resources/Models/sequence-card/sequence-card-holder.glb'
    path.parent.mkdir(parents=True,exist_ok=True);glb.save(path)
    specs={key:geometry(**value)[2] for key,value in PLACEMENTS.items()}
    specs['reusable-decathlon']=geometry()[2]
    (ROOT/'docs/models/sequence-card-spec.json').write_text(json.dumps(specs,indent=2)+'\n')
    print('WROTE',path)
