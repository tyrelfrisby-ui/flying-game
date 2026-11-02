"""Original Phase 2 meshes. Called by vr_cockpit.update AFTER every base export.
Also updates existing Phase 1 assets without Blender. No node deletion/renumbering.
Dimensions and unresolved aircraft-specific choices: docs/models/vr-cockpit-phase2.md.
"""
import json
import math
from pathlib import Path
import numpy as np
from vr_cockpit import Author, Shape, Glb, MODELS, ROOT, gl, builder, sill, skin_y
from drop_card_slot import validate

BOW_X=(-.835,-.875)
BOW_RADIUS=.00635

def bow_points(x):
    # Same 2.4 superellipse as rounded panel, with 60 mm crown clearance.
    z0=sill(x)+.004; width=skin_y(x,sill(x))
    return np.array([(x,width*math.copysign(abs(math.cos(t))**(2/2.4),math.cos(t)),
                      z0+(.218-z0)*math.sin(t)**(2/2.4)) for t in np.linspace(0,math.pi,49)])

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
    g=a.g;grey=g.material('VR_4130',(.42,.45,.48));white=g.material('VR_Delrin',(.89,.89,.84));black=g.material('VR_Black',(.012,.014,.016))
    for name,x in zip(('Canopy_Bow_F','Canopy_Bow_R'),BOW_X):
        sh=Shape();pts=bow_points(x)
        for p,q in zip(pts,pts[1:]):sh.tube(p,q,BOW_RADIUS,10)
        a.node(name,parent='Canopy',shape=sh,mat=grey)
    # Bridge between low crowns; short downward capture pin. Both move with canopy.
    sh=Shape();sh.box((-.855,0,.218),(.053,.10,.008))
    a.node('Canopy_Latch_Bracket',parent='Canopy',shape=sh,mat=grey)
    sh=Shape();sh.tube((-.855,-.0381,.183),(-.855,-.0381,.218),.004)
    a.node('Canopy_Latch_Pin',(-.855,-.0381,.218),'Canopy',sh,grey)
    a.rigged('Canopy_Latch_Pin','canopy_latch',(0,1,0),[0,1],[0,.0762],kind='move')
    # Transverse channel, open at pilot-left end: +76.2 mm moves pin OUT.
    # A shallow step near the pilot-right end represents the locking notch.
    sh=Shape();sh.box((-.855,-.018,.170),(.052,.084,.016))
    for xx in (-.877,-.833):
        sh.box((xx,-.003,.190),(.008,.054,.024))
        sh.box((xx,-.050,.190),(.008,.020,.024))
        sh.box((xx,-.035,.187),(.008,.010,.018))
    sh.box((-.855,-.056,.187),(.044,.008,.018))
    a.node('Canopy_Latch_Block',shape=sh,mat=white)
    sh=Shape();sh.tube((-.835,-.0381,.187),(-.865,-.0381,.205),.004);sh.sphere((-.865,-.0381,.205),.014)
    a.node('Canopy_Latch_Knob',(-.865,-.0381,.205),shape=sh,mat=black)
    a.rigged('Canopy_Latch_Knob','canopy_latch',(0,1,0),[0,1],[0,.0762],kind='move')
    a.node('Grip_Canopy_Latch',(-.865,-.0381,.205),'Canopy_Latch_Knob')

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
    if g.doc.get('extras',{}).get('vrCockpitPhase2'):return
    kind='pitts' if path.parent.name=='pitts-s2b-like' else 'extra';a=Edit(g)
    if kind=='pitts':canopy(a)
    hips={'pitts':[('Rear',(-1.30,0,-.335)),('Front',(-.53,0,-.335))],
          'extra':[('Rear',(-1.72,0,-.01)),('Front',(-.82,0,-.01))]}
    for seat,hip in hips[kind]:harness(a,kind,seat,hip)
    rp=ROOT/'docs/models'/('pitts-s2b-own-rig.json' if kind=='pitts' else 'ea300-own-rig.json')
    rig=json.loads(rp.read_text());names={r['node'] for r in a.rig};rig['rig']=[r for r in rig['rig'] if r['node'] not in names]+a.rig
    rig['notes']='Phase 2 original visual geometry; dimensions/placement estimates require Ty review. harness_latch drives both independent releases visually.'
    rp.write_text(json.dumps(rig,indent=1)+'\n')
    g.doc.setdefault('extras',{})['vrCockpitPhase2']=dict(originalGeometry=True,bow_tube_radius_m=BOW_RADIUS if kind=='pitts' else None,pin_length_m=.035 if kind=='pitts' else None)
    validate(g,path);g.save(path);print('PHASE2',path.name)

if __name__=='__main__':
    for kind in ('pitts-s2b-like','extra-300-like'):
        for path in sorted((MODELS/kind).glob('*own*.glb')):update(path)
