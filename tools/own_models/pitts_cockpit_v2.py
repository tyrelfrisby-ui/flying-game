"""Ty 2026-11-05 Pitts-only geometry upgrade of the current, postprocessed GLBs.

Run with /usr/bin/python3; or Blender -b --python this_file -- --assets.
Does not run the obsolete full airframe builder or write shared modules / rig JSON.
Builder coordinates: X forward, Y left, Z up, metres. Existing node indices stay stable.
"""
import copy
import json
import math
from pathlib import Path
import numpy as np
from vr_cockpit import Glb, Shape, MODELS, ROOT, gl, builder, skin_y, sill
from gl_quadrant_v3 import set_mesh, quad
from drop_card_slot import validate

MODEL = MODELS/'pitts-s2b-like'
TUBE_R = .009525/2
SLIDE = .1016
OPEN_DEG = 100.
HINGE = np.array((-.8,-.36,.16))
CARD_W, CARD_H = .10795, .1397


def opened(p, slide=SLIDE, angle=OPEN_DEG):
    p=np.array(p,float)-HINGE; t=math.radians(angle)
    # Right-hinged canopy: left rail rises and swings out onto the right wing.
    x,y,z=p
    return HINGE+(-slide+x,y*math.cos(t)-z*math.sin(t),y*math.sin(t)+z*math.cos(t))


class Upgrade:
    def __init__(self,g):
        self.g=g; self.nodes=g.doc['nodes'];self.names={n['name']:i for i,n in enumerate(self.nodes)}
    def node(self,name):return self.nodes[self.names[name]]
    def world(self,name):return self.g.world_matrices()[self.names[name]]
    def vertices(self,name):
        n=self.node(name);w=self.world(name)
        a=np.concatenate([self.g.array(p['attributes']['POSITION']) for p in self.g.doc['meshes'][n['mesh']]['primitives']])
        return np.array([builder(v) for v in a@w[:3,:3].T+w[:3,3]])
    def put(self,name,parts,pivot=(0,0,0),parent=None,extras=None):
        if name not in self.names:
            self.names[name]=len(self.nodes);self.nodes.append({'name':name});self.g.doc['scenes'][0]['nodes'].append(self.names[name])
        i=self.names[name];n=self.nodes[i]
        for p in self.nodes:
            if i in p.get('children',[]):p['children'].remove(i)
        roots=self.g.doc['scenes'][0]['nodes']
        if i in roots:roots.remove(i)
        if parent:self.node(parent).setdefault('children',[]).append(i)
        else:roots.append(i)
        set_mesh(self.g,name,parts,np.array(pivot))
        for key in ['matrix','rotation','scale']:n.pop(key,None)
        par=self.world(parent)[:3,3] if parent else np.zeros(3)
        n['translation']=(gl(pivot)-par).tolist()
        if extras:n.setdefault('extras',{}).update(extras)
        return n
    def move(self,name,target):
        n=self.node(name);w=self.world(name);v=self.vertices(name);c=(v.min(0)+v.max(0))/2
        d=gl(np.array(target)-c);n.pop('matrix',None);n['translation']=(w[:3,3]+d).tolist()
    def hide(self,name):
        if name in self.names:self.node(name).pop('mesh',None)
    def material(self,name,rgb,metal=0,rough=.6):
        i=self.g.material(name,rgb);p=self.g.doc['materials'][i]['pbrMetallicRoughness'];p.update(metallicFactor=metal,roughnessFactor=rough)
        return name


def path_shape(points,radius=TUBE_R):
    s=Shape()
    for a,b in zip(points,points[1:]):
        if np.linalg.norm(np.array(a)-b)>1e-8:s.tube(a,b,radius,12)
    return s


def ring(x):
    top=float(np.interp(-x,[.131,.25,.393,.601,.869,1.05,1.226,1.464,1.601],[.222,.345,.458,.530,.554,.552,.530,.482,.429]))
    z=sill(x)-.002;w=skin_y(x,sill(x))-.004
    return [(x,w*math.copysign(abs(math.cos(t))**(2/2.4),math.cos(t)),z+(top-z)*math.sin(t)**(2/2.4)) for t in np.linspace(0,math.pi,65)]


def clip_mesh_x(u,name,cut,keep_front,newname=None):
    """Clip textured triangles exactly at the fixed/moving windscreen seam."""
    g=u.g;n=u.node(name);w=u.world(name);prims=[]
    for p in g.doc['meshes'][n['mesh']]['primitives']:
        attrs={k:g.array(v) for k,v in p['attributes'].items()}
        pos=attrs['POSITION'];pos=pos@w[:3,:3].T+w[:3,3]
        attrs['POSITION']=pos;ids=g.array(p['indices']).reshape(-1,3) if 'indices' in p else np.arange(len(pos)).reshape(-1,3)
        keys=list(attrs);result={k:[] for k in keys}
        for ids3 in ids:
            poly=[{k:attrs[k][i] for k in keys} for i in ids3];out=[]
            for a,b in zip(poly,poly[1:]+poly[:1]):
                da=-a['POSITION'][0]-cut;db=-b['POSITION'][0]-cut
                ia=da>=-1e-9 if keep_front else da<=1e-9;ib=db>=-1e-9 if keep_front else db<=1e-9
                if ia:out.append(a)
                if ia!=ib:
                    t=da/(da-db);out.append({k:a[k]+t*(b[k]-a[k]) for k in keys})
            for j in range(1,len(out)-1):
                for q in (out[0],out[j],out[j+1]):
                    for k in keys:result[k].append(q[k])
        if result['POSITION']:
            prims.append(dict(attributes={k:g.accessor(np.array(v),'VEC2' if k.startswith('TEXCOORD') else 'VEC3') for k,v in result.items()},material=p['material'],mode=4))
    if newname:
        u.put(newname,[],extras={'fixedToAirframe':True});n=u.node(newname)
    else:
        for key in ['matrix','rotation','scale']:n.pop(key,None)
        parent=next((p['name'] for p in u.nodes if u.names[name] in p.get('children',[])),None)
        n['translation']=(-u.world(parent)[:3,3] if parent else np.zeros(3)).tolist()
    g.doc['meshes'].append({'name':newname or name,'primitives':prims});n['mesh']=len(g.doc['meshes'])-1


def subtract_hole(u,name,polygon):
    """Subtract a convex vertical polygon, interpolating UVs at every new edge.

    Sutherland-Hodgman halfplanes keep outside fragments; discard the final inside.
    Unlike dropping triangles this preserves the exact circular lip and handle bridge.
    """
    g=u.g;n=u.node(name)
    for p in g.doc['meshes'][n['mesh']]['primitives']:
        attrs={k:g.array(v) for k,v in p['attributes'].items()};keys=list(attrs);pos=attrs['POSITION']
        ids=g.array(p['indices']).reshape(-1,3) if 'indices' in p else np.arange(len(pos)).reshape(-1,3)
        output={k:[] for k in keys}
        for tri in ids:
            remaining=[{k:attrs[k][i] for k in keys} for i in tri];pieces=[]
            xyz=np.array([builder(q['POSITION']) for q in remaining])
            box=np.array(polygon)
            if np.any(xyz[:,:2].max(0)<box.min(0)) or np.any(xyz[:,:2].min(0)>box.max(0)):
                pieces=[remaining];remaining=[]
            for a,b in zip(polygon,polygon[1:]+polygon[:1]):
                if not remaining:break
                a,b=np.array(a),np.array(b);edge=b-a
                def dist(q):
                    v=builder(q['POSITION'])[:2]-a;return edge[0]*v[1]-edge[1]*v[0]
                inside=[];outside=[]
                for q,r in zip(remaining,remaining[1:]+remaining[:1]):
                    dq,dr=dist(q),dist(r);iq=dq>=0;ir=dr>=0
                    (inside if iq else outside).append(q)
                    if iq!=ir:
                        t=dq/(dq-dr);v={k:q[k]+t*(r[k]-q[k]) for k in keys};inside.append(v);outside.append(v)
                if len(outside)>=3:pieces.append(outside)
                remaining=inside
            for poly in pieces:
                for j in range(1,len(poly)-1):
                    for q in (poly[0],poly[j],poly[j+1]):
                        for k in keys:output[k].append(q[k])
        p['attributes']={k:g.accessor(np.array(v),'VEC2' if k.startswith('TEXCOORD') else 'VEC3') for k,v in output.items()};p.pop('indices',None)


def wing_surfaces(u):
    from check_vr_phase1 import triangles
    tri=triangles(u.g,'Wing_Upper');xy=tri[:,:,:2]
    b=xy[:,1]-xy[:,0];c=xy[:,2]-xy[:,0];det=b[:,0]*c[:,1]-b[:,1]*c[:,0]
    def heights(point):
        d=np.array(point)-xy[:,0];ok=abs(det)>1e-10;den=np.where(ok,det,1)
        s=(d[:,0]*c[:,1]-d[:,1]*c[:,0])/den;t=(b[:,0]*d[:,1]-b[:,1]*d[:,0])/den
        hit=ok&(s>=-1e-6)&(t>=-1e-6)&(s+t<=1.000001)
        z=tri[:,0,2]+s*(tri[:,1,2]-tri[:,0,2])+t*(tri[:,2,2]-tri[:,0,2])
        return (float(z[hit].min()),float(z[hit].max())) if hit.any() else None
    return heights


def update(g):
    if g.doc.get('extras',{}).get('pittsCockpit1105'):
        followup(g)
        return
    u=Upgrade(g)
    steel=u.material('Pitts1105_Steel',(.46,.49,.52),.85,.28)
    black=u.material('Pitts1105_Black',(.009,.010,.012),0,.55)
    rubber=u.material('Pitts1105_Rubber',(.007,.007,.008),0,.92)
    red=u.material('Pitts1105_Red',(.72,.008,.012),.05,.35)
    blue=u.material('Pitts1105_Blue',(.015,.08,.7),.15,.33)
    alu=u.material('Pitts1105_Bare_Aluminum',(.60,.64,.68),.88,.38)
    white=u.material('Pitts1105_White',(.92,.92,.87))
    # Geometry source removal local to Pitts; common owns the fleet source modules.
    for n in u.nodes:
        if n['name'].startswith(('Harness_','Headset_','Cord_')):n.pop('mesh',None)
    # A windshield is part of the airframe, never a child of Canopy/Canopy_Slide.
    clip_mesh_x(u,'Canopy_Glass',-.47,True,'Windshield_Fixed')
    clip_mesh_x(u,'Canopy_Glass',-.47,False)
    reveal=[(x+.008,y,z) for x,y,z in ring(-.47)]
    u.put('Windshield_Trim',[(black,path_shape(ring(-.47),.009),None),(steel,path_shape(reveal,.0025),None),(black,path_shape(ring(-.13),.006),None)])
    rail={sg:[(x,sg*(skin_y(x,sill(x))-.004),sill(x)+.006) for x in np.linspace(-.47,-1.6,80)] for sg in [-1,1]}
    frame=path_shape(ring(-.47));frame2=path_shape(ring(-1.6))
    frame.add(frame2.v,frame2.t)
    for pts in rail.values():
        s=path_shape(pts);frame.add(s.v,s.t)
    u.put('Canopy_Frame',[(steel,frame,None)],parent='Canopy',extras={'tubeDiameterM':.009525})
    for name,x in [('Canopy_TopTube_F',-.77),('Canopy_TopTube_R',-1.12)]:
        u.put(name,[(steel,path_shape(ring(x)),None)],parent='Canopy',extras={'tubeDiameterM':.009525})
    # Preserve old node names as interaction anchors, eliminate front red release geometry.
    for name in list(u.names):
        if name.startswith(('Canopy_Jettison_','Canopy_Latch_')) and ('_Front' in name or 'Placard' in name):u.hide(name)
    p=np.array((-.49,skin_y(-.49,sill(-.49))-.014,sill(-.49)+.012))
    lever=path_shape([p,p+(.0508,0,0)],.006)
    u.put('Canopy_Latch_Knob_Front',[(black,lever,None)],p,'Canopy',{'motion':'90 degree fore-aft throw; forward latched; aft unlatched','hingeAxisBuilder':[0,1,0],'leverLengthM':.0508})
    rear=np.array((-.872,.278,sill(-.872)+.012))
    u.put('Canopy_Latch_Knob',[(black,path_shape([rear,rear+(.0508,0,0)],.006),None)],rear,'Canopy')
    for name in ['Canopy_Latch_Block','Canopy_Latch_Bracket','Canopy_Latch_Pin']:u.hide(name)
    cable=[p,(p[0],p[1],sill(p[0])-.004)]
    cable += [(x,skin_y(x,sill(x))-.014,sill(x)-.004) for x in np.linspace(-.49,-.872,70)]
    cable += [rear]
    u.put('Pitts_Release_Cable_Left',[(black,path_shape(cable,.0015),None)],parent='Canopy',extras={'links':['Canopy_Latch_Knob_Front','Canopy_Latch_Knob']})
    # Retain the rear emergency release; only the front emergency knob was forbidden.
    # Right-wing stop is positioned from the actual left rail swept through normal travel.
    hoop=ring(-.47);k=min(range(1,len(hoop)-1),key=lambda k:abs(opened(hoop[k])[2]-.662))
    railpoint=np.array(hoop[k]);railcenter=opened(railpoint)
    tangent=opened(hoop[k+1])-opened(hoop[k-1])
    normal=np.cross(tangent,[1.,0,0]);normal/=np.linalg.norm(normal)
    if normal[2]>0:normal=-normal
    contact=railcenter+normal*TUBE_R
    stopcenter=railcenter+normal*(TUBE_R+.006)
    mountx=stopcenter[0]+.19
    mountz=wing_surfaces(u)((mountx,stopcenter[1]))[1]
    points=[(mountx,stopcenter[1],mountz),(mountx,stopcenter[1],stopcenter[2]),stopcenter+(-.020,0,0)]
    u.put('Canopy_Stop_Tab',[(steel,path_shape(points,.003175),None)],extras={'roundStockDiameterM':.00635,'collider':'capsules along bent stock','stockCenterlineBuilder':[list(p) for p in points],'contactBuilder':contact.tolist(),'normalSlideM':SLIDE,'normalAngleDeg':OPEN_DEG})
    pad=Shape();pad.tube(stopcenter+(-.014,0,0),stopcenter+(.014,0,0),.006,64)
    u.put('Canopy_Stop_Rubber',[(rubber,pad,None)],extras={'collider':'capsule','railRadiusM':TUBE_R})
    # Upper-wing entry semicircle and a real through-slot leaving a grip bridge.
    hole=[(-.365+.205*math.cos(t),.205*math.sin(t)) for t in np.linspace(0,2*math.pi,97)[:-1]]
    heights=wing_surfaces(u)
    lip=Shape();cutwall=Shape()
    for a,b in zip(hole,hole[1:]+hole[:1]):
        ha,hb=heights(a),heights(b)
        if ha and hb:
            lip.tube((a[0],a[1],ha[1]+.001),(b[0],b[1],hb[1]+.001),.0025,8)
            cutwall.add([(a[0],a[1],ha[0]),(b[0],b[1],hb[0]),(b[0],b[1],hb[1]),(a[0],a[1],ha[1])],[(0,1,2),(0,2,3)])
    subtract_hole(u,'Wing_Upper',hole)
    slot=[(-.128,-.085),(-.102,-.085),(-.102,.085),(-.128,.085)]
    subtract_hole(u,'Wing_Upper',slot)
    u.put('UpperWing_Entry_Cutout_Trim',[(steel,lip,None)])
    u.put('UpperWing_Entry_Cutout_Walls',[(alu,cutwall,None)])
    # Vertical walls at the slot: real through-hole; no painted black rectangle.
    wall=Shape()
    for a,b in zip(slot,slot[1:]+slot[:1]):
        ha,hb=heights(a),heights(b)
        assert ha and hb
        wall.add([(a[0],a[1],ha[0]),(b[0],b[1],hb[0]),(b[0],b[1],hb[1]),(a[0],a[1],ha[1])],[(0,1,2),(0,2,3)])
    u.put('UpperWing_Handle_Slot_Walls',[(black,wall,None)],extras={'slotWidthM':.026,'slotLengthM':.170})
    # No front-panel crossing tubes, including remnants in the old mixed mesh.
    for name in ['Canopy_Bow_F_Front','Canopy_Bow_R_Front']:u.hide(name)
    for name in ['Cockpit_Truss','Cockpit']:
        n=u.node(name);w=u.world(name)
        for pr in g.doc['meshes'][n['mesh']]['primitives']:
            mat=g.doc['materials'][pr['material']]['name']
            if name=='Cockpit' and 'tube' not in mat.lower() and 'harness' not in mat.lower():continue
            a=g.array(pr['attributes']['POSITION']);v=np.array([builder(q) for q in a@w[:3,:3].T+w[:3,3]])
            ids=g.array(pr['indices']).reshape(-1,3) if 'indices' in pr else np.arange(len(a)).reshape(-1,3)
            rm=((v[ids,:,][...,0]>-.21)&(v[ids][...,2]>-.12)).any(1)
            if 'harness' in mat.lower():rm[:]=True
            pr['indices']=g.accessor(ids[~rm].reshape(-1,1),'SCALAR',5125)
    # Bare aluminum barrel tank across the fuselage, behind the front panel, exposed below it.
    tank=Shape();tank.tube((.12,-.265,-.19),(.12,.265,-.19),.228,96)
    u.put('FuelTank_Barrel',[(alu,tank,None)])
    ribs=Shape()
    for y in np.linspace(-.248,.248,27):
        pts=[(.12+.230*math.cos(t),y,-.19+.230*math.sin(t)) for t in np.linspace(0,2*math.pi,25)]
        for a,b in zip(pts,pts[1:]):ribs.tube(a,b,.0015,4)
    u.put('FuelTank_Vertical_Striations',[(alu,ribs,None)])
    # Card centers moved up into each panel; full half-letter portrait + 3 mm rim.
    layouts={'Rear':{'Altimeter':(.16,.112),'Airspeed':(.16,-.010),'G_meter':(.285,-.052),'Tachometer':(-.285,-.052),'Oil_pressure':(-.16,.108),'Oil_temperature':(-.16,-.010)},
             'Front':{'Altimeter':(.125,.104),'Airspeed':(.20,-.014),'G_meter':(-.125,.104),'Tachometer':(-.20,-.014)}}
    for seat,layout in layouts.items():
        x=-.818 if seat=='Rear' else -.1635
        for key,(y,z) in layout.items():
            for part in ['Face','Bezel']:
                name=f'{seat}Panel_{key}_{part}';v=u.vertices(name);c=(v.min(0)+v.max(0))/2;u.move(name,(c[0],y,z))
        prefix='' if seat=='Rear' else 'FrontPanel_'
        cy,cz=layout['Altimeter'];s=Shape();s.tube((x-.006,cy+.027,cz-.028),(x-.018,cy+.027,cz-.028),.006)
        u.put(prefix+'Altimeter_Knob',[(black,s,None)],(x-.006,cy+.027,cz-.028))
        s=Shape();s.box((x-.001,cy-.021,cz),(.001,.021,.010))
        u.put(prefix+'Altimeter_Kollsman',[(black,s,None)])
        cy,cz=layout['G_meter'];s=Shape();s.tube((x-.005,cy+.025,cz-.030),(x-.012,cy+.025,cz-.030),.0045)
        u.put(prefix+'G_meter_Reset',[(black,s,None)],(x-.005,cy+.025,cz-.030))
        cx=x-.003;z=.067
        holder=Shape();holder.box((cx+.003,0,z),(.003,CARD_W+.006,CARD_H+.006))
        u.put(prefix+'SequenceCard_Holder',[(black,holder,None)])
        stock=Shape();stock.box((cx+.001,0,z),(.0004,CARD_W,CARD_H));u.put(prefix+'SequenceCard_Stock',[(white,stock,None)])
        if seat=='Rear':
            u.move('SequenceCard_Face',(cx,0,z));u.hide('SequenceCard_Clip')
        else:
            src=copy.deepcopy(u.node('SequenceCard_Face'));src['name']=prefix+'SequenceCard_Face';src.pop('translation',None)
            g.doc['meshes'].append(copy.deepcopy(g.doc['meshes'][src['mesh']]));src['mesh']=len(g.doc['meshes'])-1
            u.names[src['name']]=len(u.nodes);u.nodes.append(src);g.doc['scenes'][0]['nodes'].append(len(u.nodes)-1)
            u.move(src['name'],(cx,0,z))
        # A narrow whiskey-compass window with curved horizontal drum, never a dial face.
        if seat=='Rear':
            u.hide('RearPanel_Compass_Face');u.hide('RearPanel_Compass_Bezel')
            c=np.array((x-.012,0,.177));body=Shape()
            for dy,dz,size in [(0,.014,(.033,.065,.005)),(0,-.014,(.033,.065,.005)),(.030,0,(.033,.005,.026)),(-.030,0,(.033,.005,.026))]:
                body.box(c+(0,dy,dz),size)
            drum=Shape();drum.tube(c+(.004,0,-.008),c+(.004,0,.008),.025,48)
            u.put('WhiskeyCompass_Case',[(black,body,None)])
            u.put('WhiskeyCompass_Drum',[(black,drum,None)],c)
            # Curved scale on the exposed aft-facing semicircle, horizontal heading graduations.
            marks=Shape()
            for t in np.linspace(-.92,.92,19):
                q=c+(-.0255*math.cos(t),.0255*math.sin(t),.006)
                marks.box(q,(.001,.0008,.004 if abs(t)<.001 else .002))
            u.put('WhiskeyCompass_Graduations',[(white,marks,None)])
            lubber=Shape();lubber.box(c+(-.027,0,0),(.001,.0007,.019));u.put('WhiskeyCompass_Lubber',[(red,lubber,None)])
            from PIL import Image,ImageDraw,ImageFont
            art=ROOT/'tools/own_models/art/pitts-whiskey-compass-1105.png'
            im=Image.new('RGB',(512,128),(8,9,11));d=ImageDraw.Draw(im)
            font=ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial Bold.ttf',52)
            for xx,text in [(42,'33'),(228,'N'),(408,'03')]:d.text((xx,25),text,font=font,fill=(238,238,222))
            im.save(art);g.material('Pitts1105_Compass_Scale',png=art)
            scale=Shape();uv=[];angles=np.linspace(-1.1,1.1,33)
            for j,(a,b) in enumerate(zip(angles,angles[1:])):
                scale.add([c+(-.0254*math.cos(t),.0254*math.sin(t),z) for z in [-.008,.008] for t in [a,b]],[(0,1,3),(0,3,2)])
                uv.extend([(1-j/32,1),(1-(j+1)/32,1),(1-j/32,0),(1-(j+1)/32,0)])
            u.put('WhiskeyCompass_Heading_Scale',[('Pitts1105_Compass_Scale',scale,uv)])
    # Right fuel valve: long forward shaft, red rotating upright handle.
    p=np.array((-.97,-.277,-.160));shaft=path_shape([p,(.65,p[1],p[2])],.004)
    u.put('FuelValve_Firewall_Rod',[(steel,shaft,None)],p,'FuelValve_Rear')
    sh=path_shape([p,p+(0,0,.060)],.007)
    u.put('FuelValve_Rear',[(red,sh,None)],p,extras={'motion':'rotate about forward shaft; up ON, left OFF','axisBuilder':[1,0,0]})
    # Left prop bar, high above and slightly ahead of throttle, carries its forward rod.
    p=np.array((-1.08,.275,.025));rod=path_shape([p,(.65,.275,.025)],.004)
    u.put('PropLever_Rear_Slide',[(steel,rod,None)],p,extras={'motion':'slide fore-aft','axisBuilder':[1,0,0]})
    bar=Shape();bar.box(p+(0,-.042,-.015),(.018,.084,.025))
    u.put('PropLever_Rear',[(blue,bar,None)],p,'PropLever_Rear_Slide')
    u.hide('Pitts_Prop_Left_Locked')
    # 16 inch trim lever, neutral straight forward, origin physically at the hip hinge.
    p=np.array((-1.36,.27,-.26));arm=path_shape([p,p+(.4064,0,0)],.0045)
    grip=path_shape([p+(.375,0,0),p+(.4064,0,0)],.009)
    u.put('TrimLever_Rear',[(steel,arm,None),(black,grip,None)],p,extras={'motion':'hinge at hip; neutral straight forward','hingeAxisBuilder':[0,1,0],'lengthM':.4064})
    for seat in ['Rear','Front']:
        p=np.array((-1.145 if seat=='Rear' else -.425,.277,-.13))
        stem=path_shape([p,p+(0,-.017,0)],.006)
        knob=path_shape([p+(0,-.017,0),p+(0,-.095,0)],.021)
        button=path_shape([p+(0,-.095,0),p+(0,-.099,0)],.008)
        u.put('Throttle_'+seat,[(steel,stem,None),(black,knob,None),(red,button,None)],p,extras={'motion':'fore-aft throttle','knobAxisBuilder':[0,-1,0]})
    # Communicate new model pivots in embedded metadata; owner updates the external rig JSON.
    updated=['FuelValve_Rear','PropLever_Rear_Slide','PropLever_Rear','TrimLever_Rear','Throttle_Rear','Throttle_Front']
    g.doc['extras']['pittsCockpit1105']={'source':'ty-cockpit-spec-1105.md','tubeDiameterM':.009525,'cardSizeM':[CARD_W,CARD_H],
        'pivotsBuilder':{n:builder(u.world(n)[:3,3]).tolist() for n in updated},'stopRailPointBuilder':railpoint.tolist(),'stopContactBuilder':contact.tolist(),
        'normalSlideM':SLIDE,'normalOpenDeg':OPEN_DEG,'runtimeRigUpdateRequired':True}
    followup(g)
    validate(g,ROOT/'pitts-cockpit-1105')


def front_latch_clearance(g):
    """Conservative capsule-to-triangle clearance, including sampling error.

    The exported lever is a 6 mm radius tube. Centreline samples are <=0.5 mm
    apart, so subtract another 0.25 mm to bound the unsampled segments.
    """
    from check_vr_phase1 import triangles,point_tri
    u=Upgrade(g);p=builder(u.world('Canopy_Latch_Knob_Front')[:3,3])
    fixed=np.concatenate([triangles(g,n) for n in ['Windshield_Trim','Windshield_Fixed']])
    return [min(point_tri(p+direction*t,fixed) for t in np.linspace(0,.0508,103))-.006-.00025
            for direction in [np.array([1.,0,0]),np.array([0.,0,1])]]


def followup(g):
    """Idempotent repair of both pass-1 assets and freshly generated geometry."""
    if g.doc.get('extras',{}).get('pittsCockpit1105Followup'):return
    u=Upgrade(g);u.hide('Pitts_Release_Knob_Left')
    # Retain node identities, parents, children and the existing fore-aft hinge axis.
    for name,target in [('Canopy_Hinge_Canopy_Front',(.515,.16,-.36)),
                        ('Canopy_Hinge_Airframe_Front',(.551,.16,-.36))]:
        n=u.node(name);parent=next(p['name'] for p in u.nodes if u.names[name] in p.get('children',[])) if any(u.names[name] in p.get('children',[]) for p in u.nodes) else None
        n['translation']=(np.array(target)-(u.world(parent)[:3,3] if parent else 0)).tolist()
    # Aft of the fixed trim with the entire 2-inch lever forward when latched.
    p=np.array((-.55,skin_y(-.55,sill(-.55))-.014,sill(-.55)+.012))
    black='Pitts1105_Black'
    u.put('Canopy_Latch_Knob_Front',[(black,path_shape([p,p+(.0508,0,0)],.006),None)],p,'Canopy')
    rear=builder(u.world('Canopy_Latch_Knob')[:3,3])
    cable=[p,(p[0],p[1],sill(p[0])-.004)]
    cable += [(x,skin_y(x,sill(x))-.014,sill(x)-.004) for x in np.linspace(p[0],rear[0],70)]
    cable += [rear]
    u.put('Pitts_Release_Cable_Left',[(black,path_shape(cable,.0015),None)],parent='Canopy')
    for suffix in ['', '_Front']:
        # Local glTF -X is builder forward; preserve the grip's orientation.
        u.node('Grip_Canopy_Latch'+suffix)['translation']=[-.0508,0,0]
    clearance=front_latch_clearance(g)
    assert min(clearance)>=.005,('front latch windshield clearance',clearance)
    g.doc['extras']['pittsCockpit1105Followup']={'frontLatchPivotGltf':gl(p).tolist(),
        'frontLatchClearanceLowerBoundM':clearance}


def main():
    for path in sorted(MODEL.glob('*-own*.glb')):
        g=Glb(path);update(g);compact(g);g.save(path);print('WROTE',path.name)


def compact(g):
    """Pitts-local buffer compaction; preserve every node index and rig reference."""
    used=sorted({n['mesh'] for n in g.doc['nodes'] if 'mesh' in n})
    mapping={old:i for i,old in enumerate(used)}
    g.doc['meshes']=[g.doc['meshes'][i] for i in used]
    for n in g.doc['nodes']:
        if 'mesh' in n:n['mesh']=mapping[n['mesh']]
    refs=[]
    for mesh in g.doc['meshes']:
        for p in mesh['primitives']:
            refs.extend((p['attributes'],k) for k in p['attributes'])
            if 'indices' in p:refs.append((p,'indices'))
            for target in p.get('targets',[]):refs.extend((target,k) for k in target)
    for skin in g.doc.get('skins',[]):
        if 'inverseBindMatrices' in skin:refs.append((skin,'inverseBindMatrices'))
    for anim in g.doc.get('animations',[]):
        for sampler in anim['samplers']:refs.extend((sampler,k) for k in ('input','output'))
    used=sorted({o[k] for o,k in refs});mapping={old:i for i,old in enumerate(used)}
    g.doc['accessors']=[g.doc['accessors'][i] for i in used]
    for o,k in refs:o[k]=mapping[o[k]]
    views=[(a,'bufferView') for a in g.doc['accessors'] if 'bufferView' in a]
    for a in g.doc['accessors']:
        if 'sparse' in a:views.extend((a['sparse'][k],'bufferView') for k in ('indices','values'))
    views.extend((im,'bufferView') for im in g.doc.get('images',[]) if 'bufferView' in im)
    used=sorted({o[k] for o,k in views});mapping={old:i for i,old in enumerate(used)}
    oldviews=g.doc['bufferViews'];oldbin=g.bin;g.bin=bytearray();g.doc['bufferViews']=[]
    for i in used:
        v=oldviews[i].copy();start=v.get('byteOffset',0)
        g.bin.extend(b'\0'*(-len(g.bin)%4));v['byteOffset']=len(g.bin)
        g.bin.extend(oldbin[start:start+v['byteLength']]);g.doc['bufferViews'].append(v)
    for o,k in views:o[k]=mapping[o[k]]


if __name__=='__main__':main()
