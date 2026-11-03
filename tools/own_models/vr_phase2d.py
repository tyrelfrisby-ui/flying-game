"""CPU-only detachable canopy postprocess; builder coordinates metres, X forward/Y left/Z up."""
import json
import numpy as np
from vr_cockpit import Glb, Shape, gl, builder, ROOT
from vr_phase2 import Edit, PANEL_STATIONS, sill, skin_y, pitts_frame, tube_path
from drop_card_slot import validate


def surface(g, name):
    i=next(i for i,n in enumerate(g.doc['nodes']) if n['name']==name)
    n=g.doc['nodes'][i];m=g.world_matrices()[i];out=[]
    for pr in g.doc['meshes'][n['mesh']]['primitives']:
        v=g.array(pr['attributes']['POSITION']);v=v@m[:3,:3].T+m[:3,3]
        ids=g.array(pr['indices']).reshape(-1,3) if 'indices' in pr else np.arange(len(v)).reshape(-1,3)
        out.extend(v[ids])
    return np.array(out)


def update(path):
    g=Glb(path)
    if g.doc.get('extras',{}).get('vrCockpitPhase2d'):return
    kind='pitts' if path.parent.name=='pitts-s2b-like' else 'extra';a=Edit(g)
    if kind=='pitts':pitts_frame(a)  # P2d removes both tall end arcs, including P2c inputs.
    root=a.node('Canopy_Detachable')
    a.attach_existing('Canopy_Slide' if kind=='pitts' else 'Canopy','Canopy_Detachable')
    # Keep legacy opening pivots and their full subtree. Fixed sill/socket parts stay outside.
    metal=g.material('P2d_Hinge_Steel',(.42,.44,.46));red=g.material('P2d_Jettison_Red',(.8,.015,.01))
    can=g.doc['nodes'][a.idx('Canopy')];pivot=builder(g.world_matrices()[a.idx('Canopy')][:3,3])
    axis=builder(can['extras']['rig_axis_gltf']);axis/=np.linalg.norm(axis)
    can['extras'].pop('rig_jettison_param',None)
    can['extras'].pop('rig_jettison_desc',None)
    can['extras']['detachable_root']='Canopy_Detachable'
    stations=[('Rear',-.855),('Front',-.200)] if kind=='pitts' else [('Rear',-1.55),('Front',-.70)]
    for seat,x in stations:
        p=np.array((x,-(skin_y(x,sill(x))-.004),sill(x)+.006)) if kind=='pitts' else pivot+axis*((x-pivot[0])/axis[0])
        # Each knuckle occupies a different fore/aft segment on the same pin axis.
        for half,dx,parent in [('Airframe',-.018,None),('Canopy',.018,'Canopy')]:
            q=p+(dx,0,0);sh=Shape();sh.tube(q+(-.015,0,0),q+(.015,0,0),.010)
            sh.box(q+(0,0,-.015 if half=='Airframe' else .015),(.030,.022,.025))
            a.node('Canopy_Hinge_'+half+'_'+seat,q,parent,sh,metal)
        q=p+(-.018,0,.012);sh=Shape()
        sh.add([q+v for v in [(-.014,-.009,0),(.014,-.009,0),(.009,.008,.017),(-.010,.008,.006)]],[(0,1,2),(0,2,3),(2,1,0),(3,2,0)])
        n=a.node('Canopy_Hinge_Broken_'+seat,q,'Canopy_Hinge_Airframe_'+seat,sh,metal)
        n['extras']={'default_visible':False,'show_when':'canopy departed'}
        if kind=='pitts':
            # Confirmed right bow/rail junction, pulls inboard along Y. On moving frame.
            q=p+(-.020,.025,.018);n='Canopy_Jettison_Knob_'+seat
            sh=Shape();sh.tube(q+(0,-.025,0),q,.003);sh.sphere(q,.016)
            a.node(n,q,'Canopy',sh,red)
            a.rigged(n,'canopy_jettison',(0,1,0),[0,1],[0,.040],kind='move',desc='Right bow/sill junction confirmed by Ty 2026-11-02; estimated 40 mm inboard pull releases right hinge pins at any speed')
            a.node('Grip_Canopy_Jettison_'+seat,q,n)
            sh=Shape();sh.tube(q+(0,-.025,0),p,.002)
            a.node('Canopy_Jettison_Cable_'+seat,parent='Canopy',shape=sh,mat=metal)
            placard(a,seat,q+(.025,.008,.020))
    # Separate fixed cockpit sill rails; moving bubble rail remains part of frame.
    if kind=='pitts':
        for side,sy in [('L',1),('R',-1)]:
            sh=Shape();pts=[(x,sy*(skin_y(x,sill(x))-.004),sill(x)-.025) for x in np.linspace(-.13,-1.60,65)]
            pts=tube_path(pts)
            for u,v in zip(pts,pts[1:]):sh.tube(u,v,.009,10)
            a.node('Canopy_Sill_Airframe_'+side,shape=sh,mat=metal)
    # Surface integration: acrylic 3 mm * 1180 kg/m3; steel tube skin 1 mm * 7850.
    # Remaining moving fittings estimated 0.8 kg, distributed with frame surface.
    clouds=[];weights=[];areas={}
    for name,density in [('Canopy_Glass',.003*1180),('Canopy_Frame',.001*7850)]:
        t=surface(g,name);area=np.linalg.norm(np.cross(t[:,1]-t[:,0],t[:,2]-t[:,0]),axis=1)/2
        areas[name]=float(area.sum());clouds.append(t.mean(1));weights.append(area*density)
    # Include white bows as tube mass too.
    if kind=='pitts':
        for name in [n['name'] for n in g.doc['nodes'] if n['name'].startswith('Canopy_Bow_')]:
            t=surface(g,name);ar=np.linalg.norm(np.cross(t[:,1]-t[:,0],t[:,2]-t[:,0]),axis=1)/2
            clouds.append(t.mean(1));weights.append(ar*.001*7850)
    weights[1]+= .8*weights[1]/weights[1].sum()
    v=np.concatenate(clouds);w=np.concatenate(weights);mass=w.sum();com=np.average(v,axis=0,weights=w);d=v-com
    tensor=np.eye(3)*np.sum(w*np.sum(d*d,axis=1))-np.einsum('i,ij,ik->jk',w,d,d)
    moments,axes=np.linalg.eigh(tensor)
    if np.linalg.det(axes)<0:axes[:,2]*=-1
    a.node('Canopy_COM',builder(com),'Canopy_Detachable')
    root['extras']={'mass_kg':float(mass),'com_gltf_m':com.tolist(),'inertia_tensor_gltf_kg_m2':tensor.tolist(),'principal_inertia_kg_m2':moments.tolist(),'principal_axes_gltf_columns':axes.tolist(),'mass_estimate':{'acrylic_area_m2':areas['Canopy_Glass'],'acrylic_thickness_m':.003,'acrylic_density_kg_m3':1180,'steel_wall_m':.001,'steel_density_kg_m3':7850,'fittings_kg':.8},'detach_includes_opening_rig':True,'rig_jettison_param':'canopy_jettison','rig_jettison_desc':'Detach this complete root, preserving current world transform; Pitts emergency pull releases at any speed'}
    rp=ROOT/'docs/models'/('pitts-s2b-own-rig.json' if kind=='pitts' else 'ea300-own-rig.json');rig=json.loads(rp.read_text());names={r['node'] for r in a.rig};rig['rig']=[r for r in rig['rig'] if r['node'] not in names]+a.rig
    for r in rig['rig']:
        if r['node']==('Canopy_Slide' if kind=='pitts' else 'Canopy'):r['parent']='Canopy_Detachable'
        if r['node']=='Canopy':
            r.setdefault('extra',{}).pop('rig_jettison_param',None)
            r['extra'].pop('rig_jettison_desc',None)
            r['extra']['detachable_root']='Canopy_Detachable'
    rp.write_text(json.dumps(rig,indent=1)+'\n')
    g.doc.setdefault('extras',{})['vrCockpitPhase2d']=True
    validate(g,path);g.save(path);print('P2d',path.name, json.dumps(root['extras']))


def placard(a,seat,p):
    import struct,zlib,tempfile
    from pathlib import Path
    # Original 5x7 bitmap lettering, deterministic PNG; no Pillow/Blender dependency.
    glyphs={
        'C':['01111','10000','10000','10000','10000','10000','01111'],
        'A':['01110','10001','10001','11111','10001','10001','10001'],
        'N':['10001','11001','11001','10101','10011','10011','10001'],
        'O':['01110','10001','10001','10001','10001','10001','01110'],
        'P':['11110','10001','10001','11110','10000','10000','10000'],
        'Y':['10001','10001','01010','00100','00100','00100','00100'],
        'J':['00111','00010','00010','00010','10010','10010','01100'],
        'E':['11111','10000','10000','11110','10000','10000','11111'],
        'T':['11111','00100','00100','00100','00100','00100','00100'],
        'I':['11111','00100','00100','00100','00100','00100','11111'],
        'S':['01111','10000','10000','01110','00001','00001','11110']}
    text='CANOPY JETTISON';w=len(text)*6+2;h=11;pixels=np.full((h,w,3),255,dtype=np.uint8)
    for i,ch in enumerate(text):
        for y,row in enumerate(glyphs.get(ch,['00000']*7)):
            for x,b in enumerate(row):
                if b=='1':pixels[y+2,i*6+x+2]=(185,0,0)
    def chunk(t,b):return struct.pack('>I',len(b))+t+b+struct.pack('>I',zlib.crc32(t+b)&0xffffffff)
    png=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(b''.join(b'\0'+row.tobytes() for row in pixels)))+chunk(b'IEND',b'')
    with tempfile.TemporaryDirectory() as td:
        path=Path(td)/'canopy-jettison.png';path.write_bytes(png)
        mat=a.g.material('P2d_Jettison_Placard',(1,1,1),png=path)
    n='Canopy_Jettison_Placard_'+seat;p=np.array(p)
    sh=Shape();sh.add([p+v for v in [(-.012,-.045,0),(.012,-.045,0),(.012,.045,0),(-.012,.045,0)]],[(0,1,2),(0,2,3)])
    a.node(n,parent='Canopy',shape=sh,mat=mat)
    pr=a.g.doc['meshes'][a.g.doc['nodes'][a.idx(n)]['mesh']]['primitives'][0]
    pr['attributes']['TEXCOORD_0']=a.g.accessor(np.array([(0,1),(0,0),(1,0),(0,1),(1,0),(1,1)]),'VEC2')
