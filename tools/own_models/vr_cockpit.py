"""Phase 1 original cockpit geometry, shared by offline and Blender export paths.
Builder coordinates metres: X forward, Y left, Z up. No reference meshes.
Run with a NumPy/Pillow Python: python tools/own_models/vr_cockpit.py
An already updated asset is left untouched. Rebuild base to change these numbers.
"""
import ast
import json
import math
from pathlib import Path
import numpy as np
from glb_visuals import Glb
from drop_card_slot import validate

ROOT = Path(__file__).resolve().parents[2]
MODELS = ROOT/'unity/Assets/_Project/Resources/Models'
def gl(p):
    x,y,z = p
    return np.array((-x,z,y),float)
def builder(p):
    x,y,z = p
    return np.array((-x,z,y),float)

class Shape:
    def __init__(self): self.v=[]; self.t=[]
    def add(self,v,t):
        off=len(self.v); self.v.extend(v); self.t.extend(tuple(off+i for i in f) for f in t)
    def tube(self,a,b,r,n=12):
        a,b=np.array(a),np.array(b); d=b-a; d=d/np.linalg.norm(d)
        ref=(0,0,1) if abs(d[2])<.8 else (0,1,0)
        u=np.cross(d,ref);u/=np.linalg.norm(u);w=np.cross(d,u)
        v=[p+r*(math.cos(t)*u+math.sin(t)*w) for p in (a,b) for t in np.linspace(0,2*math.pi,n,endpoint=False)]
        t=[]
        for i in range(n):
            j=(i+1)%n; t.extend(((i,j,n+j),(i,n+j,n+i)))
        t.extend((0,i+1,i) for i in range(1,n-1)); t.extend((n,n+i,n+i+1) for i in range(1,n-1))
        self.add(v,t)
    def box(self,c,s):
        c=np.array(c);s=np.array(s)/2
        self.add([c+s*np.array(q) for q in ((-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1))],
                 [(0,2,1),(0,3,2),(4,5,6),(4,6,7),(0,1,5),(0,5,4),(1,2,6),(1,6,5),(2,3,7),(2,7,6),(3,0,4),(3,4,7)])
    def sphere(self,c,r,n=16,m=8):
        c=np.array(c);v=[c+r*np.array((math.sin(p)*math.cos(t),math.sin(p)*math.sin(t),math.cos(p))) for p in np.linspace(0,math.pi,m+1) for t in np.linspace(0,2*math.pi,n,endpoint=False)]
        t=[]
        for j in range(m):
            for i in range(n):
                a=j*n+i;b=j*n+(i+1)%n;t.extend(((a,b,b+n),(a,b+n,a+n)))
        self.add(v,t)
    def prism(self,poly,x,th):
        n=len(poly);v=[(xx,y,z) for xx in (x-th/2,x+th/2) for y,z in poly];t=[]
        # Convex curved outline.
        t.extend((0,i,i+1) for i in range(1,n-1));t.extend((n,n+i+1,n+i) for i in range(1,n-1))
        for i in range(n):
            j=(i+1)%n;t.extend(((i,n+i,n+j),(i,n+j,j)))
        self.add(v,t)

class Author:
    def __init__(self,g): self.g=g;self.rig=[];self.orig={}
    def idx(self,n): return next(i for i,o in enumerate(self.g.doc['nodes']) if o['name']==n)
    def node(self,n,p=(0,0,0),parent=None,shape=None,mat=None):
        q=gl(p);self.orig[n]=q
        if shape is not None:
            self.g.mesh(n,[gl(v)-q for v in shape.v],shape.t,mat)
            o=self.g.doc['nodes'][self.idx(n)]
        else:
            o=dict(name=n); self.g.doc['nodes'].append(o);self.g.doc['scenes'][0]['nodes'].append(len(self.g.doc['nodes'])-1)
        o['translation']=(q-self.orig[parent] if parent else q).tolist()
        if n.startswith('Grip_'): o['rotation']=[0,1,0,0] # local +X = fingers/forward, glTF -X
        if parent:
            i=self.idx(n);self.g.doc['scenes'][0]['nodes'].remove(i)
            self.g.doc['nodes'][self.idx(parent)].setdefault('children',[]).append(i)
        return o
    def attach_existing(self,n,parent):
        """Preserve existing world matrices and every reference, never renumber nodes."""
        i=self.idx(n);g=self.g;world=g.world_matrices()[i];par=self.orig[parent]
        for node in g.doc['nodes']:
            if i in node.get('children',[]): node['children'].remove(i)
        for scene in g.doc['scenes']:
            if i in scene['nodes']: scene['nodes'].remove(i)
        node=g.doc['nodes'][i]; node.pop('matrix',None);node.pop('rotation',None);node.pop('scale',None)
        assert np.allclose(world[:3,:3],np.eye(3),atol=1e-5),(n,world)
        node['translation']=(world[:3,3]-par).tolist();self.orig[n]=world[:3,3]
        g.doc['nodes'][self.idx(parent)].setdefault('children',[]).append(i)
    def rigged(self,n,param,axis,points,values,kind='rot',desc='Estimated visual travel; needs Ty review'):
        axis=gl(axis);axis=axis/np.linalg.norm(axis)
        o=self.g.doc['nodes'][self.idx(n)]
        model=next((float(p) for p,v in zip(points,values) if v==0),0.)
        o['extras']=dict(rig_param=param,rig_param_model=model,rig_map_param=points,rig_unit='0..1' if min(points)>=0 else '-1..1',rig_desc=desc)
        o['extras'].update({('rig_axis_gltf' if kind=='rot' else 'rig_move_axis_gltf'):axis.tolist(),('rig_map_deg' if kind=='rot' else 'rig_map_m'):values})
        self.rig.append(dict(node=n,param=param,parent=next((x['name'] for x in self.g.doc['nodes'] if self.idx(n) in x.get('children',[])),None),kind=kind,
                             axis_gltf=axis.tolist(),pivot_gltf=self.orig[n].tolist(),map=list(map(list,zip(points,values))),model=model,unit=o['extras']['rig_unit'],desc=desc,extra={},link='',note=''))

def stations():
    tree=ast.parse((Path(__file__).parent/'build_pitts_s2b.py').read_text())
    return next(ast.literal_eval(n.value) for n in tree.body if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='ST' for t in n.targets))
ST=stations()
def sec(x): return [float(np.interp(x,[s[0] for s in ST[::-1]],[s[i] for s in ST[::-1]])) for i in (1,2,3)]
def skin_y(x,z):
    top,bot,hw=sec(x);zw=bot+.62*(top-bot);up=z>=zw;n=2.4 if up else 3.;h=top-zw if up else zw-bot
    return hw*max(0,1-(abs(z-zw)/h)**n)**(1/n)
def sill(x): return float(np.interp(x,[s[0] for s in ST[::-1] if s[4] is not None],[s[4] for s in ST[::-1] if s[4] is not None]))
def strip_pitts(g):
    """Only remove old frame, rear panel/lip, sticks and pedals; keep instruments/card/seats."""
    o=next(n for n in g.doc['nodes'] if n['name']=='Cockpit')
    removed={}
    for p in g.doc['meshes'][o['mesh']]['primitives']:
        xyz=g.array(p['attributes']['POSITION']);v=np.array([builder(q) for q in xyz]);ids=g.array(p['indices']).reshape(-1,3);t=v[ids]
        mat=g.doc['materials'][p['material']]['name'];rm=np.zeros(len(ids),bool)
        # The old bmesh builder assigned some faces the wrong material. Match volumes,
        # across ALL primitives, rather than trusting those mixed tags.
        rm |= ((abs(t[:,:,0]+.80)<.0076)&(abs(t[:,:,1])<=.3101)&(t[:,:,2]>=-.1201)&(t[:,:,2]<=.1501)).all(1)
        rm |= ((abs(t[:,:,0]+.79)<.0301)&(abs(t[:,:,1])<.2501)&(abs(t[:,:,2]-.152)<.0061)).all(1)
        def cylinder(u,v,r):
            u,v=np.array(u),np.array(v);d=v-u;proj=((t-u)@d)/np.dot(d,d)
            nearest=u+np.clip(proj,0,1)[:,:,None]*d
            return (np.linalg.norm(t-nearest,axis=2)<=r+.00015).all(1)
        for sy in (-1,1):
            def oldpoint(x): return (x,sy*(skin_y(x,sill(x))-.035),sill(x)-.025)
            rm |= cylinder(oldpoint(-.14),oldpoint(-1.59),.011)
            for xa,xb in ((-.14,-.55),(-.55,-1.05),(-1.05,-1.59)):
                rm |= cylinder(oldpoint(xa),(xb,sy*(skin_y(xb,sill(xb))-.05),-.53),.010)
        for x,h in ((-.17,.52),(-.93,.55)):
            rm |= cylinder((x,0,-.53),(x-.045,0,-.58+h-.09),.010)
            rm |= cylinder((x-.045,0,-.58+h-.10),(x-.055,0,-.58+h),.016)
        for x in (-.22,-.80):
            # Old pedal boxes tilted 0.35 rad about Y.
            for sy in (-1,1):
                q=t-np.array((x,sy*.13,-.48));c,ss=math.cos(.35),math.sin(.35)
                xx=c*q[:,:,0]-ss*q[:,:,2];zz=ss*q[:,:,0]+c*q[:,:,2]
                rm |= ((abs(xx)<=.0151)&(abs(q[:,:,1])<=.0401)&(abs(zz)<=.0651)).all(1)
        if rm.any():
            p['indices']=g.accessor(ids[~rm].reshape(-1,1),'SCALAR',5125);removed[mat]=int(rm.sum())
    return removed

def pitts(path):
    g=Glb(path)
    if g.doc.get('extras',{}).get('vrCockpitPhase1'): return
    removed=strip_pitts(g);a=Author(g)
    grey=g.material('VR_4130',(.42,.45,.48));black=g.material('VR_Black',(.012,.014,.016));white=g.material('VR_Delrin',(.89,.89,.84))
    panel=g.material('VR_Panel',(.035,.038,.042))
    # Cluster points are shared verbatim. Skin offset includes radius and 10 mm air gap.
    xs=[-1.59,-.98,-.14];joints={};segments=[];widths=[]
    for x in xs:
        def inset(z):
            # Inset along the cross-section normal, not just laterally: near the belly,
            # a lateral-only offset lets a round tube break through the curved skin.
            slope=(skin_y(x,z+.00001)-skin_y(x,z-.00001))/.00002
            length=math.sqrt(1+slope*slope);d=.0127+.010
            return skin_y(x,z)-d/length,z+d*slope/length
        wt,zt=inset(sill(x)-.025);wb,zb=inset(sec(x)[1]+.035)
        widths.append(dict(x=x,top_m=2*wt,bottom_m=2*wb))
        assert wt>wb
        for sy in (-1,1):
            joints[x,sy,'t']=(x,sy*wt,zt);joints[x,sy,'b']=(x,sy*wb,zb)
    def member(p,q,r): segments.append((p,q,r))
    for sy in (-1,1):
        for x,y in zip(xs,xs[1:]):
            for level in ('t','b'): member(joints[x,sy,level],joints[y,sy,level],.0127)
            if x==xs[0]: member(joints[x,sy,'b'],joints[y,sy,'t'],.009525)
            # The forward footwell has an open side bay: its X bracing is below
            # the floor, avoiding an invented diagonal through the rear pilot's calves.
        for x in xs:
            member(joints[x,sy,'b'],joints[x,sy,'t'],.009525)
    for x in xs:
        for level in ('t','b'):
            # Top transverse members only at bulkheads; other stations would obstruct occupants.
            if level=='t' and x not in (xs[0],xs[-1]): continue
            member(joints[x,-1,level],joints[x,1,level],.009525)
    for sy in (-1,1): member(joints[-.98,sy,'b'],joints[-.14,-sy,'b'],.009525)
    frame=Shape()
    for p,q,r in segments: frame.tube(p,q,r)
    a.node('Cockpit_Truss',shape=frame,mat=grey)
    errors=[];skin_clear=[]
    for i,(p,q,r) in enumerate(segments):
        for end in (p,q):
            distances=[]
            for k,(u,v,_) in enumerate(segments):
                if k==i: continue
                u,v=np.array(u),np.array(v);d=v-u;t=np.clip(np.dot(np.array(end)-u,d)/np.dot(d,d),0,1)
                distances.append(float(np.linalg.norm(np.array(end)-(u+t*d))))
            errors.append(min(distances))
        for t in np.linspace(0,1,101):
            x,y,z=(1-t)*np.array(p)+t*np.array(q)
            skin_clear.append(skin_y(x,z)-abs(y)-r)
    assert max(errors)<.002
    # Check the actual round tube vertices against the curved loft as well.
    surface_clear=[skin_y(x,z)-abs(y) for x,y,z in frame.v]
    assert min(surface_clear)>0,(min(surface_clear),'tube vertex outside fuselage')
    from vr_phase2 import pitts_panels
    pitts_panels(a)
    hw=sec(-.8)[2]-.025;zb=sec(-.8)[1]+.62*(sec(-.8)[0]-sec(-.8)[1]);top=sec(-.8)[0]-.025
    # Full forward pedals preserved at old x; rear pedals now straddle front seat pan.
    grips={}
    for seat,x,yw in [('Front',-.22,.13),('Rear',-.43,.285)]:
        for side,sy in [('L',1),('R',-1)]:
            n=f'Pedal_{side}_{seat}';p=(x,sy*yw,-.535);sh=Shape();sh.box((x,sy*yw,-.465),(.025,.075,.13))
            a.node(n,p,shape=sh,mat=black);a.rigged(n,'rudder',(0,1,0),[-1,0,1],[15*sy,0,-15*sy])
            foot=(x-.020,sy*yw,-.46);a.node(f'Foot_{side}_{seat}',foot,n);grips[f'Foot_{side}_{seat}']=foot
    for seat,x,h in [('Front',-.17,.52),('Rear',-.93,.55)]:
        p=(x,0,-.58);r=f'StickRoll_{seat}';a.node(r,p);a.rigged(r,'aileron',(1,0,0),[-1,0,1],[-18,0,18])
        n=f'Stick_{seat}';sh=Shape();sh.tube(p,(x-.045,0,-.58+h-.09),.010);sh.tube((x-.045,0,-.58+h-.10),(x-.055,0,-.58+h),.016)
        a.node(n,p,r,sh,black);a.rigged(n,'elevator',(0,-1,0),[-1,0,1],[-12,0,18])
        grip=(x-.05,0,-.58+h-.045);a.node('Grip_Stick_'+seat,grip,n);grips['Grip_Stick_'+seat]=grip
        tx=-.40 if seat=='Front' else -1.12;ty=skin_y(tx,-.12)-.15;p=(tx,ty,-.18)
        sh=Shape();sh.tube(p,(tx-.04,ty,-.065),.007);sh.sphere((tx-.04,ty,-.065),.019)
        n='Throttle_'+seat;a.node(n,p,shape=sh,mat=black);a.rigged(n,'throttle',(0,1,0),[0,1],[0,50])
        grip=(tx-.04,ty,-.065);a.node('Grip_Throttle_'+seat,grip,n);grips['Grip_Throttle_'+seat]=grip
        a.node('Headset_Jack_'+seat,(x-.20,-.26,.0))   # no Harness_Buckle_: no belts (Ty 2026-11-05)
    # Aft translation parent; rotation child keeps its axis attached to the translating right sill.
    a.node('Canopy_Slide',(-.8,-.36,.16));a.rigged('Canopy_Slide','canopy_open',(-1,0,0),[0,.2,1],[0,.1016,.1016],kind='move',desc='S-2B 4 inch estimated aft track; slide first, then hinge')
    a.node('Canopy',(-.8,-.36,.16),'Canopy_Slide');a.rigged('Canopy','canopy_open',(1,0,0),[0,.2,1],[0,0,100],desc='Right side hinge; estimated 100 degree stop; first 20 percent is aft slide')
    for n in ('Canopy_Glass','Canopy_Frame'): a.attach_existing(n,'Canopy')
    # Phase 2 correction is shared with the offline path: LOW panel-top bows,
    # short capture pin and transverse channel. No tall hoops/vertical linkage.
    from vr_phase2 import canopy
    canopy(a)
    a.node('Grip_Canopy_Rail',(-1.15,.34,.16),'Canopy')
    report=dict(tube_members=len(segments),tube_ends=len(errors),max_endpoint_axis_error_mm=max(errors)*1000,lateral_skin_clearance_mm=min(skin_clear)*1000,min_tube_vertex_lateral_skin_gap_mm=min(surface_clear)*1000,
                longeron_widths=widths,tube_od_mm=[25.4,19.05],panel=dict(half_width=hw,bottom=zb,crown=top,min_gauge_margin_mm=None),
                grips_builder=grips,removed_triangles=removed,rear_pedal_eye_reach_m=float(np.linalg.norm(np.array((-.43,.285,-.46))-np.array((-1.325,0,.3)))))
    g.doc.setdefault('extras',{})['vrCockpitPhase1']=report
    validate(g,path);g.save(path)
    rig=dict(model='pitts-s2b-like-own',rig=a.rig,notes='Original Phase 1 visual rig; estimates require owner review; no input or physics changes')
    (ROOT/'docs/models/pitts-s2b-own-rig.json').write_text(json.dumps(rig,indent=1)+'\n')
    print(path.name,json.dumps(report))
    return report

def extra(path):
    g=Glb(path)
    if g.doc.get('extras',{}).get('vrCockpitPhase1'): return
    a=Author(g);a.orig['Canopy']=g.world_matrices()[a.idx('Canopy')][:3,3];red=g.material('VR_Latch_Red',(.7,.015,.015));white=g.material('VR_Latch_White',(.9,.9,.86))
    # Keep old lock roots for compatibility but empty their obsolete single-handle geometry.
    oldrig=json.loads((ROOT/'docs/models/ea300-own-rig.json').read_text())
    for n in ('CanopyLock_F','CanopyLock_R'):
        g.doc['nodes'][a.idx(n)].pop('mesh',None)
    for seat,suffix in [('Rear',''),('Front','_Front')]:
        old='CanopyLock_'+('R' if seat=='Rear' else 'F');world=g.world_matrices()[a.idx(old)];pv=builder(world[:3,3]);pv[1]-=.015
        for side,sg in [('L',1),('R',-1)]:
            # Two upright paddles rotate toward each other around fore-aft axes.
            p=pv+np.array((0,sg*.027,0));name='Canopy_Latch_Handle_'+side+suffix
            sh=Shape();sh.tube(p,p+(0,0,.075),.007);sh.box(p+(0,0,.075),(.022,.012,.035))
            a.node(name,p,'Canopy',sh,red)
            # White grip insert is a child of each paddle, so it follows the squeeze.
            sh=Shape();sh.box(p+(0,0,.084),(.024,.014,.013));a.node(name+'_White',p,name,sh,white)
            a.rigged(name,'canopy_latch',(1,0,0),[0,1],[0,25*sg],desc='Red and white squeeze paddles on left sill; 25 degrees estimated; both seats linked')
        a.node('Grip_Canopy_Latch'+suffix,pv+(0,0,.065),'Canopy')
    # Contract grips attach to the already rigged controls; derive their actual world placement.
    worlds=g.world_matrices()
    for seat,sfx in [('Front','F'),('Rear','R')]:
        for control in ('Stick','Throttle'):
            old=control+'_'+sfx;i=a.idx(old);m=worlds[i];node=g.doc['nodes'][i]
            a.orig[old]=m[:3,3];p=builder(m[:3,3]);mesh=g.doc['meshes'][node['mesh']]
            coords=np.concatenate([g.array(pr['attributes']['POSITION']) for pr in mesh['primitives']]);xyz=(m[:3,:3]@coords.T).T+m[:3,3]
            top=xyz[xyz[:,1]>xyz[:,1].max()-.035].mean(0)
            a.node('Grip_'+control+'_'+seat,builder(top),old)
    oldrig['rig']=[r for r in oldrig['rig'] if r['node'] not in ('CanopyLock_F','CanopyLock_R') and not r['node'].startswith('Canopy_Latch_Handle_')]+a.rig
    # Preserve all existing rigs, replace only latch entries.
    (ROOT/'docs/models/ea300-own-rig.json').write_text(json.dumps(oldrig,indent=1)+'\n')
    g.doc.setdefault('extras',{})['vrCockpitPhase1']=dict(latch='red + white paired squeeze paddles; dimensions estimated')
    validate(g,path);g.save(path);print('EXTRA',path.name,'paired squeeze paddles')

def update(path):
    path=Path(path)
    result = pitts(path) if path.parent.name=='pitts-s2b-like' else extra(path)
    from vr_phase2 import update as phase2_update
    phase2_update(path)
    from vr_phase2d import update as phase2d_update
    phase2d_update(path)
    from vr_phase2e import update as phase2e_update
    phase2e_update(path)
    from vr_phase3 import update as phase3_update
    phase3_update(path)
    from vr_phase3b import update as phase3b_update
    phase3b_update(path)
    from vr_phase3d import update as phase3d_update
    phase3d_update(path)
    from vr_phase3e import update as phase3e_update
    phase3e_update(path)
    return result
if __name__=='__main__':
    for model in ('pitts-s2b-like','extra-300-like'):
        for path in sorted((MODELS/model).glob('*own*.glb')): update(path)

    from vr_phase3 import update as phase3_update
    for path in sorted((MODELS/'great-lakes-like').glob('*own*.glb')):
        phase3_update(path)
        from vr_phase3c import update as phase3c_update
        phase3c_update(path)
