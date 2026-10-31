"""Shared PIC panel layouts; panel coordinates u=pilot right, v=model up (metres).
Original base dials are anonymous placeholders. These names assign visual functions,
not measured factory equipment or working runtime instruments.
"""
import math
import os
from pathlib import Path

# name, u, v, bezel diameter. Extra preserves ten dial slots plus its compass.
LAYOUTS = {
 'extra-300-like': [
  ('Airspeed',-.24,.201,.05715),('Altimeter',-.16,.201,.05715),
  ('Attitude',-.08,.201,.05715),('G_meter',0,.201,.05715),
  ('Tachometer',.13,.220,.040),('Oil_pressure',.23,.220,.040),
  ('Oil_temperature',.13,.174,.040),('Fuel_pressure',.23,.174,.040),
  ('Fuel_quantity',.23,.365,.054),('Voltmeter',-.23,.365,.054),
  ('Compass',.13,.365,.060)],
 'pitts-s2b-like': [
  ('Airspeed',-.21,.065,.068),('Altimeter',-.16,-.035,.060),
  ('G_meter',-.11,.065,.068),('Tachometer',.11,.065,.068),
  ('Oil_pressure',.21,.065,.068),('Oil_temperature',.11,-.035,.060),
  ('Compass',.21,-.035,.060)],
 'great-lakes-like': [
  ('Airspeed',-.21,.04,.064),('Altimeter',-.16,-.05,.056),
  ('G_meter',-.11,.04,.064),('Tachometer',.11,.04,.064),
  ('Oil_pressure',.21,.04,.064),('Oil_temperature',.11,-.05,.056),
  ('Compass',.21,-.05,.056)]}
GAUGE_X = {'extra-300-like':-1.163,'pitts-s2b-like':-.808,'great-lakes-like':-1.638}

def before(aircraft):
    if aircraft=='extra-300-like':
        return [(f'Anonymous_{i+1}',-y,z,.062) for i,(z,y) in enumerate((z,y) for z in (.193,.263) for y in (-.24,-.12,0,.12,.24))]+[('Static_compass',.145,.366,.066)]
    if aircraft=='pitts-s2b-like': ys,z,d,ys2,z2,d2=(-.18,-.06,.06,.18),.065,.068,(-.12,0,.12),-.035,.060
    else: ys,z,d,ys2,z2,d2=(-.16,-.055,.055,.16),.04,.064,(-.11,0,.11),-.05,.056
    return [(f'Anonymous_{i+1}',-y,v,diam) for i,(y,v,diam) in enumerate([(y,z,d) for y in ys]+[(y,z2,d2) for y in ys2])]

def strip_old_dials(glb, aircraft):
    """Filter only triangles wholly inside each old dial/needle volume.
    Base Cockpit material tags are mixed across primitives, so use geometry, not tags.
    Existing vertex buffers remain byte-identical; only triangle-index references change.
    """
    import numpy as np
    node=next(n for n in glb.doc['nodes'] if n.get('name')=='Cockpit')
    count=0
    for p in glb.doc['meshes'][node['mesh']]['primitives']:
        a=glb.array(p['attributes']['POSITION'])
        ids=glb.array(p['indices']).reshape(-1,3)
        tris=a[ids]; remove=np.zeros(len(ids),dtype=bool)
        for name,u,v,d in before(aircraft):
            if name=='Static_compass': continue
            # raw glTF x=-builder x, y=v, z=-u
            x=1.163 if aircraft=='extra-300-like' else -GAUGE_X[aircraft]
            inside=(abs(tris[:,:,0]-x)<=.0106)&(abs(tris[:,:,1]-v)<=d/2+.0001)&(abs(tris[:,:,2]+u)<=d/2+.0001)
            remove |= inside.all(1)
        if remove.any():
            p['indices']=glb.accessor(ids[~remove].reshape(-1,1),'SCALAR',5125)
            count+=int(remove.sum())
    # 16-sided cylinders (bezel + face) plus needle cube, 132 triangles per dial.
    expected=(len(before(aircraft))-(aircraft=='extra-300-like'))*132
    assert count==expected,(aircraft,count,expected)

def dial_art(path, name):
    from PIL import Image, ImageDraw, ImageFont
    im=Image.new('RGB',(512,512),(19,23,27)); d=ImageDraw.Draw(im)
    fontdir=Path(os.environ['IAC_FONT_DIR'])
    font=lambda n:ImageFont.truetype(str(fontdir/'LiberationSans-Bold.ttf'),n)
    if name=='Attitude':
        d.ellipse((20,20,492,492),fill=(87,130,162)); d.pieslice((20,20,492,492),0,180,fill=(124,81,48));d.line((30,256,482,256),fill='white',width=5)
    for k in range(33):
        t=math.radians(-140+k*280/32); r=216 if k%4==0 else 228
        d.line((256+r*math.sin(t),256-r*math.cos(t),256+242*math.sin(t),256-242*math.cos(t)),fill='white',width=4)
    labels={'Airspeed':('AIRSPEED','MPH','90'),'Altimeter':('ALT','FT','2500'), 'G_meter':('G','ACCEL','+1'),
     'Tachometer':('RPM','x100','24'),'Oil_pressure':('OIL','PSI','70'),'Oil_temperature':('OIL','TEMP F','180'),
     'Fuel_pressure':('FUEL','PSI','25'),'Fuel_quantity':('FUEL','GAL','20'),'Voltmeter':('VOLTS','DC','14'),
     'Compass':('COMPASS','N','N'),'Attitude':('ATTITUDE','','LEVEL')}
    title,unit,value=labels[name]
    d.text((256,120),title,font=font(42 if len(title)<8 else 34),anchor='mm',fill='white')
    d.text((256,375),unit,font=font(29),anchor='mm',fill='white')
    d.text((256,320),value,font=font(38),anchor='mm',fill='white')
    if name!='Compass':
        d.line((256,266,174,100),fill=(255,210,70),width=10);d.ellipse((243,243,269,269),fill='white')
    im.save(path)

def meshes(aircraft):
    """Builder-space mesh tuples matching SequenceCard helper interface."""
    if aircraft=='extra-300-like':
        from sequence_card import box
        # The 120 mm original upper face is shorter than the 145.7 mm frame.
        # Extend its bottom 36 mm, leaving the top/glareshield fixed; restore lower recess.
        v,t=box((-1.199,0,.290),(.006,.620,.036))
        yield 'RearPanel_UpperExtension',v,t,None,None
    for name,u,v,diam in LAYOUTS[aircraft]:
        x=-1.202 if aircraft=='extra-300-like' and v>.272 else GAUGE_X[aircraft]
        y=-u;r=diam/2;segments=64
        ring=lambda xx,rr:[(xx,y+rr*math.cos(k*2*math.pi/segments),v+rr*math.sin(k*2*math.pi/segments)) for k in range(segments)]
        verts=ring(x+.002,r)+ring(x-.009,r)
        tris=[]
        for k in range(segments):
            j=(k+1)%segments;tris.extend([(k,segments+j,j),(k,segments+k,segments+j)])
        tris.extend((segments,segments+k+1,segments+k) for k in range(1,segments-1))
        yield 'RearPanel_'+name+'_Bezel',verts,tris,None,None
        verts=[(x-.010,y,v)]+ring(x-.010,r*.88)
        tris=[(0,1+(k+1)%segments,1+k) for k in range(segments)]
        uv=[(.5-(yy-y)/(r*1.76),.5-(zz-v)/(r*1.76)) for xx,yy,zz in verts]
        png=Path(__file__).resolve().parent/'art/rear-panel'/(name+'.png')
        yield 'RearPanel_'+name+'_Face',verts,tris,uv,png

def native_add(glb, aircraft):
    strip_old_dials(glb,aircraft)
    for name,v,t,uv,png in meshes(aircraft):
        mat=glb.material(name,png=png) if png else glb.material(name,(.30498737,.32314324,.35153270) if name=='RearPanel_UpperExtension' else (.025,.027,.030))
        glb.mesh(name,[(-x,z,y) for x,y,z in v],t,mat,uv)
    glb.doc.setdefault('extras',{})['rearPanelLayout']=LAYOUTS[aircraft]

def blender_add(parts, aircraft):
    import bpy, paint
    for name,v,t,uv,png in meshes(aircraft):
        me=bpy.data.meshes.new(name);me.from_pydata(v,[],t)
        if png:
            im=bpy.data.images.load(str(png));im.pack();mat=paint.mat_image(name,im,.9)
        else:
            mat=paint.mat_colour(name,(44,46,49),rough=.9)
            if name=='RearPanel_UpperExtension':
                mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.30498737,.32314324,.35153270,1)
        me.materials.append(mat)
        if uv:
            layer=me.uv_layers.new()
            for poly in me.polygons:
                for idx in poly.loop_indices:
                    u,vv=uv[me.loops[idx].vertex_index];layer.data[idx].uv=(u,1-vv)
        obj=bpy.data.objects.new(name,me);bpy.context.scene.collection.objects.link(obj);parts[name]=obj

if __name__=='__main__':
    # Bake once with CPU Python; Blender loads committed PNGs without Pillow.
    directory=Path(__file__).resolve().parent/'art/rear-panel'
    directory.mkdir(parents=True,exist_ok=True)
    for name in sorted({row[0] for layout in LAYOUTS.values() for row in layout}):
        dial_art(directory/(name+'.png'),name)
        print('WROTE',directory/(name+'.png'))
