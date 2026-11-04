"""Original Phase 2 meshes. Called by vr_cockpit.update AFTER every base export.
Also updates existing Phase 1 assets without Blender. No node deletion/renumbering.
Dimensions and unresolved aircraft-specific choices: docs/models/vr-cockpit-phase2.md.
"""
import json
import math
from pathlib import Path
import numpy as np
from vr_cockpit import Author, Shape, Glb, MODELS, ROOT, gl, builder, sill, skin_y, sec
from drop_card_slot import validate

PANEL_INSET=.025
BOW_OFFSET=.022
BOW_X=(-.835,-.875)
BOW_RADIUS=.005
PANEL_STATIONS={'Rear':-.80,'Front':-.145}

def tube_path(points, tolerance=.0009):
    """Retain endpoints; every source sample is within 0.9 mm of its chord.
    Source curves are densely sampled independently of the render tessellation.
    """
    p=np.asarray(points,float)
    if len(p)<=2:return p
    d=p[-1]-p[0];dd=np.dot(d,d)
    t=np.clip((p-p[0])@d/dd,0,1) if dd else np.zeros(len(p))
    errors=np.linalg.norm(p-(p[0]+t[:,None]*d),axis=1)
    i=int(np.argmax(errors))
    if errors[i]<=tolerance:return p[[0,-1]]
    return np.vstack((tube_path(p[:i+1],tolerance)[:-1],tube_path(p[i:],tolerance)))

def panel_curve(x,offset=0):
    """Normal inset of the builder's actual upper skin section; no vertical sides.
    The skin exponent is intentionally inherited, not a separate panel superellipse.
    """
    top,bot,hw=sec(x);zw=bot+.62*(top-bot);h=top-zw
    t=np.linspace(0,math.pi,161)
    yz=np.array([(hw*math.copysign(abs(math.cos(v))**(2/2.4),math.cos(v)),zw+h*math.sin(v)**(2/2.4)) for v in t])
    tangent=np.gradient(yz,axis=0);normal=np.column_stack((tangent[:,1],-tangent[:,0]));normal/=np.linalg.norm(normal,axis=1)[:,None]
    return yz+(offset-PANEL_INSET)*normal

def bow_points(x,panel_x=-.80):
    yz=panel_curve(panel_x,BOW_OFFSET)
    # Trim at the actual moving sill rail and land the ends on its centreline.
    rail_z=sill(x)+.006
    yz=yz[yz[:,1]>rail_z]
    rail_y=skin_y(x,sill(x))-.004
    yz=np.vstack(((rail_y,rail_z),yz,(-rail_y,rail_z)))
    return np.column_stack((np.full(len(yz),x),yz))

def pitts_panels(a):
    g=a.g;white=g.material('P2c_Panel_White',(.88,.87,.82));metal=g.material('P2c_Polished_Rim',(.83,.85,.88))
    g.doc['materials'][metal]['pbrMetallicRoughness'].update(metallicFactor=1,roughnessFactor=.16)
    for seat,x in PANEL_STATIONS.items():
        yz=panel_curve(x);sh=Shape();sh.prism(yz,x,.015 if seat=='Rear' else .012)
        a.node('RearPanel_Rounded' if seat=='Rear' else 'FrontPanel_Arched',shape=sh,mat=white)
        sh=Shape();pts=list(tube_path([(x-.008,y,z) for y,z in yz]));pts.append(pts[0])
        for u,v in zip(pts,pts[1:]):sh.tube(u,v,.0025,8)
        a.node('Panel_Rim_'+seat,shape=sh,mat=metal)
    # Remove the old front hexagonal plate from the combined Cockpit mesh only.
    o=g.doc['nodes'][a.idx('Cockpit')]
    for pr in g.doc['meshes'][o['mesh']]['primitives']:
        ids=g.array(pr['indices']).reshape(-1,3);v=np.array([builder(q) for q in g.array(pr['attributes']['POSITION'])]);t=v[ids]
        rm=((abs(abs(t[:,:,0]+.145)-.006)<.00001)&(abs(t[:,:,1])<.2701)&(t[:,:,2]>=-.0801)&(t[:,:,2]<=.1601)).all(1)
        pr['indices']=g.accessor(ids[~rm].reshape(-1,1),'SCALAR',5125)

def pitts_frame(a):
    # Preserve bubble and fixed windscreen; moving sill rails only, no tall end hoops.
    sh=Shape()
    for sy in (-1,1):
        pts=[(x,sy*(skin_y(x,sill(x))-.004),sill(x)+.006) for x in np.linspace(-.13,-1.60,65)]
        pts=tube_path(pts)
        for u,v in zip(pts,pts[1:]):sh.tube(u,v,.014,12)
    a.node('Canopy_Frame',parent='Canopy',shape=sh,mat=a.g.material('P2c_Frame_Red',(.55,.012,.018)))

def ribbon(points,width=.04445,thick=.003):
    """Flat webbing, constant rectangular section, continuous between waypoints."""
    sh=Shape();p=np.array(points,float)
    # Width axis follows local surface: lateral for shoulder/crotch, vertical for lap.
    d=p[-1]-p[0];u=np.array((0,1,0) if abs(d[2])>abs(d[1]) else (0,0,1),float)
    for a,b in zip(p,p[1:]):
        tangent=b-a;tangent/=np.linalg.norm(tangent);v=np.cross(tangent,u);v/=np.linalg.norm(v)
        verts=[q+s*width/2*u+t*thick/2*v for q in (a,b) for s,t in ((-1,-1),(1,-1),(1,1),(-1,1))]
        sh.add(verts,[(0,2,1),(0,3,2),(4,5,6),(4,6,7),(0,1,5),(0,5,4),(1,2,6),(1,6,5),(2,3,7),(2,7,6),(3,0,4),(3,4,7)])
    return sh

class Edit(Author):
    def __init__(self,g):
        super().__init__(g)
        self.orig={n['name']:m[:3,3] for i,m in g.world_matrices().items() for n in [g.doc['nodes'][i]]}
    def node(self,n,p=(0,0,0),parent=None,shape=None,mat=None):
        if n not in self.orig:return super().node(n,p,parent,shape,mat)
        i=self.idx(n);o=self.g.doc['nodes'][i]
        # Replace geometry in-place; retain index and all unrelated references.
        if shape is not None:
            if 'mesh' not in o:
                o['mesh']=len(self.g.doc['meshes']);self.g.doc['meshes'].append({})
            self.g.mesh(n,[gl(v)-gl(p) for v in shape.v],shape.t,mat)
        for other in self.g.doc['nodes']:
            if i in other.get('children',[]):other['children'].remove(i)
        for scene in self.g.doc['scenes']:
            if i in scene['nodes']:scene['nodes'].remove(i)
        if parent:self.g.doc['nodes'][self.idx(parent)].setdefault('children',[]).append(i)
        else:self.g.doc['scenes'][0]['nodes'].append(i)
        for k in ('matrix','rotation','scale'):o.pop(k,None)
        self.orig[n]=gl(p);o['translation']=(gl(p)-(self.orig[parent] if parent else 0)).tolist()
        if n.startswith('Grip_'):o['rotation']=[0,1,0,0]
        return o

def canopy(a):
    g=a.g;white=g.material('P2c_Bow_White',(.92,.92,.88));black=g.material('VR_Black',(.012,.014,.016));metal=g.material('P2c_Latch_Metal',(.55,.57,.60))
    for seat,xp in PANEL_STATIONS.items():
        suffix='' if seat=='Rear' else '_Front'
        for label,dx in (('F',-.035),('R',-.075)):
            sh=Shape();pts=tube_path(bow_points(xp+dx,xp))
            for p,q in zip(pts,pts[1:]):sh.tube(p,q,BOW_RADIUS,10)
            a.node('Canopy_Bow_'+label+suffix,parent='Canopy',shape=sh,mat=white)
        z=panel_curve(xp,BOW_OFFSET)[:,1].max();x=xp-.055
        sh=Shape();sh.box((x,0,z),(.055,.110,.024))
        a.node('Canopy_Latch_Block'+suffix,parent='Canopy',shape=sh,mat=white)
        sh=Shape();sh.box((x,0,z-.014),(.053,.10,.005))
        a.node('Canopy_Latch_Bracket'+suffix,parent='Canopy',shape=sh,mat=metal)
        # Short internal catch, entirely inside the block. No hanging rod.
        sh=Shape();sh.tube((x,-.0381,z-.006),(x,-.0381,z+.006),.003)
        a.node('Canopy_Latch_Pin'+suffix,(x,-.0381,z),'Canopy',sh,metal)
        a.rigged('Canopy_Latch_Pin'+suffix,'canopy_latch',(0,1,0),[0,1],[0,.0762],kind='move')
        p=(x-.022,-.0381,z+.025);sh=Shape();sh.tube((x,-.0381,z+.007),p,.003);sh.sphere(p,.014)
        n='Canopy_Latch_Knob'+suffix;a.node(n,p,'Canopy',sh,black)
        a.rigged(n,'canopy_latch',(0,1,0),[0,1],[0,.0762],kind='move')
        a.node('Grip_Canopy_Latch'+suffix,p,n)
        sh=Shape();sh.box((x-.028,0,z),(.001,.050,.017))
        a.node('Canopy_Latch_Placard'+suffix,parent='Canopy',shape=sh,mat=g.material('P2c_Placard_Red',(.7,.015,.015)))
        sh=Shape();sh.box((x-.0286,0,z+.005),(.0005,.046,.004))
        a.node('Canopy_Latch_Placard_White'+suffix,parent='Canopy',shape=sh,mat=white)

def extra_latch(a):
    g=a.g;red=g.material('VR_Latch_Red',(.7,.015,.015));white=g.material('VR_Latch_White',(.9,.9,.86));steel=g.material('P2c_Pin_Steel',(.6,.62,.65))
    # Legacy contract aliases retained as inert empties; obsolete paddle meshes retired.
    for n in g.doc['nodes']:
        if n['name'].startswith(('Canopy_Latch_Handle_L','Canopy_Latch_Handle_R','CanopyLock_')):
            n.pop('mesh',None);n.pop('extras',None)
    # Existing right-side CanopyRelease_* are separate jettison pulls: retained.
    frame=g.doc['nodes'][a.idx('Canopy_Frame')];m=g.world_matrices()[a.idx('Canopy_Frame')]
    v=np.concatenate([g.array(pr['attributes']['POSITION']) for pr in g.doc['meshes'][frame['mesh']]['primitives']]);v=np.array([builder(q) for q in ((m[:3,:3]@v.T).T+m[:3,3])])
    for suffix,station in (('',-1.55),('_Front',-.70)):
        # Select port sill frame samples at each original latch station.
        near=v[(abs(v[:,0]-station)<.09)&(v[:,1]>0)]
        low=near[near[:,2]<np.percentile(near[:,2],20)+.012];p=np.array((station,float(np.median(low[:,1])),float(np.median(low[:,2]))))
        for label,dx in (('Aft',-.045),('Fwd',.045)):
            q=p+(dx,-.018,-.030);n='Canopy_Latch_Handle_'+label+suffix
            sh=Shape();sh.tube(p+(dx,0,0),q,.006);sh.box(q,(.014,.027,.055));a.node(n,q,'Canopy',sh,red)
            sh=Shape();sh.box(q+(0,0,-.012),(.016,.029,.020));a.node(n+'_White',q,n,sh,white)
            a.node('Grip_Canopy_Latch_'+label+suffix,q,n)
            if label=='Fwd':a.rigged(n,'canopy_latch',(-1,0,0),[0,1],[0,.035],kind='move',desc='Spring forward at rest; squeeze slides forward handle aft 35 mm; estimated')
        q=p+(0,-.018,-.030);n='Grip_Canopy_Latch'+suffix;a.node(n,q,'Canopy');a.rigged(n,'canopy_latch',(-1,0,0),[0,1],[0,.0175],kind='move',desc='Compatibility midpoint follows half the forward handle travel')
        for j,dx in enumerate((-.070,.070)):
            q=p+(dx,0,-.012);n='Canopy_Latch_Pin_'+str(j+1)+suffix
            sh=Shape();sh.tube(q+(0,0,-.012),q+(0,0,.012),.003)
            a.node(n,q,'Canopy',sh,steel);a.rigged(n,'canopy_latch',(0,0,1),[0,1],[0,.018],kind='move',desc='6 mm pin retracts upward 18 mm from airframe socket; simplified linkage estimate')
            sh=Shape();c=q+(0,0,-.006)
            pts=[c+(.005*math.cos(t),.005*math.sin(t),0) for t in np.linspace(0,2*math.pi,9)]
            for u,vv in zip(pts,pts[1:]):sh.tube(u,vv,.0015,8)
            a.node('Canopy_Latch_Hole_'+str(j+1)+suffix,shape=sh,mat=steel)

def harness(a,kind,seat,hip):
    g=a.g;hip=np.array(hip);root='Harness_'+seat;a.node(root,hip)
    web=g.material('Harness_Webbing',(.075,.08,.09));metal=g.material('Harness_Steel',(.55,.57,.58));red=g.material('Harness_Release',(.48,.025,.018))
    buckle=hip+(.10,0,.07)
    # Independent lap belt below primary five-point buckle, both fittings visible.
    for label,dz in [('Primary',0),('Secondary',-.045)]:
        p=buckle+(0,0,dz);name='Harness_Buckle_'+seat if label=='Primary' else 'Harness_Buckle_Secondary_'+seat
        sh=Shape();sh.box(p,(.018,.065,.032));a.node(name,p,root,sh,metal)
        lever=Shape();lever.box(p+(.012,0,.005),(.012,.052,.014))
        ln='Harness_Release_'+label+'_'+seat;a.node(ln,p,name,lever,red)
        # Wagstaff: lower left, upper right, from the seat eye. Pitts lever lift estimate.
        a.rigged(ln,'harness_latch',(0,0,1) if kind=='extra' else (0,1,0),[0,1],[0,(-65 if label=='Primary' else 65) if kind=='extra' else 80],desc='Visual simultaneous release; two physically independent lap belts; Ty review required')
        if label=='Primary':a.node('Grip_Harness_Release_'+seat,p+(.02,0,0),ln)
        for side,sy in [('L',1),('R',-1)]:
            anchor=hip+(-.08,sy*.24,-.11+dz)
            pts=[anchor,hip+(.025,sy*.18,.055+dz),p]
            sn=f'Harness_Strap_Lap_{label}_{side}_{seat}';a.node(sn,anchor,root,ribbon(pts),web)
            a.rigged(sn,'harness_latch',(1,0,0),[0,1],[0,sy*75])
    for side,sy in [('L',1),('R',-1)]:
        anchor=hip+(-.15,sy*.13,.40)
        pts=[anchor,hip+(-.06,sy*.14,.49),hip+(.095,sy*.13,.32),buckle]
        n=f'Harness_Strap_Shoulder_{side}_{seat}';a.node(n,anchor,root,ribbon(pts),web)
        a.rigged(n,'harness_latch',(0,1,0),[0,1],[0,-90])
    anchor=hip+(.045,0,-.09);n='Harness_Strap_Crotch_'+seat
    a.node(n,anchor,root,ribbon([anchor,hip+(.17,0,-.025),buckle]),web)
    a.rigged(n,'harness_latch',(0,1,0),[0,1],[0,100])
    # Twin sockets at an explicitly provisional right-side position.
    jp=hip+(.10,-.27,.29);sh=Shape();sh.box(jp,(.07,.012,.04))
    a.node('Headset_Jack_'+seat,jp,shape=sh,mat=metal)
    for j,r in enumerate((.003175,.0026162)):
        sh=Shape();sh.tube(jp+(-.016+j*.032,-.008,0),jp+(-.016+j*.032,.008,0),r*1.6,12)
        a.node('Headset_Jack_'+seat+('_Phone' if j==0 else '_Mic'),jp,'Headset_Jack_'+seat,sh,g.material('Jack_Insulator',(.008,.008,.008)))

def update(path):
    path=Path(path);g=Glb(path)
    if g.doc.get('extras',{}).get('vrCockpitPhase2c'):return
    kind='pitts' if path.parent.name=='pitts-s2b-like' else 'extra';a=Edit(g)
    if kind=='pitts':
        pitts_panels(a);pitts_frame(a);canopy(a)
    else:extra_latch(a)
    hips={'pitts':[('Rear',(-1.30,0,-.335)),('Front',(-.53,0,-.335))],
          'extra':[('Rear',(-1.72,0,-.01)),('Front',(-.82,0,-.01))]}
    for seat,hip in hips[kind]:harness(a,kind,seat,hip)
    rp=ROOT/'docs/models'/('pitts-s2b-own-rig.json' if kind=='pitts' else 'ea300-own-rig.json')
    rig=json.loads(rp.read_text());names={r['node'] for r in a.rig};rig['rig']=[r for r in rig['rig'] if r['node'] not in names and not (kind=='extra' and r['node'].startswith(('CanopyLock_', 'Canopy_Latch_Handle_L', 'Canopy_Latch_Handle_R')))]+a.rig
    rig['notes']='Phase 2 original visual geometry; dimensions/placement estimates require Ty review. harness_latch drives both independent releases visually.'
    rp.write_text(json.dumps(rig,indent=1)+'\n')
    g.doc.setdefault('extras',{})['vrCockpitPhase2c']=dict(panel_inset_m=PANEL_INSET,bow_normal_offset_m=BOW_OFFSET)
    g.doc.setdefault('extras',{})['vrCockpitPhase2']=dict(originalGeometry=True,bow_tube_radius_m=BOW_RADIUS if kind=='pitts' else None,pin_length_m=.012 if kind=='pitts' else None)
    validate(g,path);g.save(path);print('PHASE2',path.name)

if __name__=='__main__':
    for kind in ('pitts-s2b-like','extra-300-like'):
        for path in sorted((MODELS/kind).glob('*own*.glb')):update(path)
