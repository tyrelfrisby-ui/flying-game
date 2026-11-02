"""Original analytic Twin II Acro. CPU GLB generation using shared glb_visuals.
Run with a Python containing NumPy/Pillow. See provenance for measured/estimated values.
Author: nose +X, port +Y, up +Z; export (-X,Z,Y), matching the SGS own convention.
"""
import json, math, sys
from pathlib import Path
import numpy as np
from glb_visuals import Glb
from make_grob103_dials import make,ART
ROOT=Path(__file__).resolve().parents[2]
ID='glider-grob103-acro-like'
OUT=ROOT/'unity/Assets/_Project/Resources/Models'/ID
DOC=ROOT/'docs/models'
# x, half width, bottom, sill/top. Upper fuselage is omitted over cockpit aperture.
ST=np.array([(2.50,.008,-.24,-.22),(2.40,.12,-.35,-.14),(2.20,.24,-.43,-.03),(1.95,.32,-.48,.02),(1.70,.36,-.50,.06),(1.35,.375,-.51,.09),(.8,.38,-.51,.12),(.2,.37,-.49,.15),(-.4,.34,-.49,.18),(-.75,.30,-.46,.19),(-1.2,.235,-.34,.18),(-2,.16,-.15,.15),(-3,.105,-.09,.13),(-4,.075,-.06,.13),(-4.9,.055,-.04,.16),(-5.45,.012,.01,.20)])[::-1]
CAN=np.array([(-.75,.30,.19,.19),(-.55,.26,.175,.56),(-.25,.325,.16,.69),(.2,.35,.14,.72),(.75,.36,.12,.70),(1.3,.35,.095,.61),(1.7,.315,.065,.45),(1.95,.25,.025,.24),(2.10,.27,.0,.0)])
# Reduced factual E603 ordinates, UIUC Airfoil Coordinates Database e603.dat.
# Linear interpolation of these measurements is our authored section approximation.
E603_U=np.array([(0,0),(.01552,.02753),(.06908,.06474),(.15638,.09762),(.27006,.12026),(.40004,.12786),(.53636,.11544),(.67748,.08372),(.81231,.04956),(.92006,.02231),(.98548,.00366),(1,0)])
E603_L=np.array([(0,0),(.01949,-.01875),(.08835,-.03814),(.19738,-.05252),(.33427,-.06074),(.43366,-.06209),(.58259,-.05521),(.72446,-.03094),(.86085,-.00417),(.96385,.00340),(1,0)])
def e603(f,upper):
    a=E603_U if upper else E603_L;return float(np.interp(f,a[:,0],a[:,1]))
EYES={'front':[1.15,0,.40],'rear':[-.04,0,.45]}
def cv(p):return np.asarray(p)[...,[0,2,1]]*[-1,1,1]
def section(x):return [float(np.interp(x,ST[:,0],ST[:,k])) for k in range(1,4)]
def canopy(x):return [float(np.interp(x,CAN[:,0],CAN[:,k])) for k in range(1,4)]
class Builder:
 def __init__(self,tf=False):
    self.g=Glb();self.rig=[];self.parts={};self.tf=tf
    cols={'white':(245,246,243),'navy':(23,39,65),'red':(201,39,49),'panel':(24,27,31),'liner':(192,195,192),'seat':(76,88,104),'belt':(35,37,40),'metal':(170,177,182),'tyre':(45,47,50),'blue':(25,92,225),'green':(30,172,69),'yellow':(255,209,22)}
    self.m={}
    for n,rgb in cols.items():
        c=np.array(rgb)/255;c=np.where(c<=.04045,c/12.92,((c+.055)/1.055)**2.4);self.m[n]=self.g.material(n,c.tolist())
    self.m['glass']=self.g.material('Canopy_Glass',(0.5,.65,.73));m=self.g.doc['materials'][self.m['glass']];m['alphaMode']='BLEND';m['doubleSided']=True;m['pbrMetallicRoughness']['baseColorFactor'][3]=.24;m['pbrMetallicRoughness']['roughnessFactor']=.08
    self.g.doc['materials'][self.m['metal']]['pbrMetallicRoughness'].update(metallicFactor=.6,roughnessFactor=.3)
    for n in ['asi','alt','vario','compass']:self.m[n]=self.g.material('Own_Dial_'+n,png=ART/f'grob103-dial-{n}.png')
    for n in ['wordmark','mark']:
        self.m[n]=self.g.material(n,png=ART/f'grob103-{n}.png');self.g.doc['materials'][self.m[n]]['doubleSided']=True
 def mesh(self,n,v,f,mat='white',pivot=None,uv=None):
    v=np.array(v,float);tri=[(q[0],q[i],q[i+1]) for q in f for i in range(1,len(q)-1)]
    # A whole closed analytic part establishes outward orientation before GLB flattening.
    if not n.startswith(('Dial_', 'Needle_', 'Mark_', 'Canopy_Glass', 'Cheatline', 'Cockpit_Liner')):
        t=v[np.array(tri)];volume=np.einsum('ij,ij->i',t[:,0],np.cross(t[:,1],t[:,2])).sum()/6
        if volume<0:tri=[(a,c,b) for a,b,c in tri]
    if pivot is None:pivot=np.zeros(3)
    self.g.mesh(n,cv(v-np.array(pivot)),tri,self.m[mat],uv)
    node=self.g.doc['nodes'][-1];node['translation']=cv(pivot).tolist();self.parts[n]=(v,np.array(tri))
    if n.startswith(('Fuselage','Wing_','Aileron','Stabilizer','Canopy_Glass')):
        prim=self.g.doc['meshes'][-1]['primitives'][0];pos=self.g.array(prim['attributes']['POSITION'])
        tt=pos.reshape(-1,3,3);nn=np.repeat(np.cross(tt[:,1]-tt[:,0],tt[:,2]-tt[:,0]),3,axis=0)
        _,inv=np.unique(np.round(pos,6),axis=0,return_inverse=True);summed=np.zeros((inv.max()+1,3));np.add.at(summed,inv,nn)
        nn=summed[inv];nn/=np.maximum(np.linalg.norm(nn,axis=1)[:,None],1e-10);prim['attributes']['NORMAL']=self.g.accessor(nn,'VEC3')
    return node
 def attach(self,child,parent):
    ns=self.g.doc['nodes'];ci=next(i for i,n in enumerate(ns) if n['name']==child);pi=next(i for i,n in enumerate(ns) if n['name']==parent)
    ns[ci]['translation']=(np.array(ns[ci].get('translation',[0,0,0]))-np.array(ns[pi].get('translation',[0,0,0]))).tolist()
    ns[pi].setdefault('children',[]).append(ci);self.g.doc['scenes'][0]['nodes'].remove(ci)
    for r in self.rig:
        if r['node']==child:r['parent']=parent
 def empty(self,n,p):
    self.g.doc['nodes'].append(dict(name=n,translation=cv(p).tolist()));self.g.doc['scenes'][0]['nodes'].append(len(self.g.doc['nodes'])-1)
 def loft(self,n,rows,mat='white',pivot=None,caps=True):
    nr=len(rows[0]);f=[]
    for i in range(len(rows)-1):
        for j in range(nr):f.append((i*nr+j,i*nr+(j+1)%nr,(i+1)*nr+(j+1)%nr,(i+1)*nr+j))
    if caps:f.extend([tuple(range(nr-1,-1,-1)),tuple((len(rows)-1)*nr+j for j in range(nr))])
    return self.mesh(n,np.array(rows).reshape(-1,3),f,mat,pivot)
 def box(self,n,c,size,mat='white',pivot=None):
    a=np.array(c);s=np.array(size)/2;v=[a+s*[i,j,k] for i,j,k in [(-1,-1,-1),(-1,-1,1),(-1,1,-1),(-1,1,1),(1,-1,-1),(1,-1,1),(1,1,-1),(1,1,1)]]
    return self.mesh(n,v,[(0,4,6,2),(1,3,7,5),(0,1,5,4),(2,6,7,3),(0,2,3,1),(4,5,7,6)],mat,pivot)
 def tube(self,n,a,b,r,mat='metal',pivot=None):
    a,b=np.array(a,float),np.array(b,float);d=b-a;d/=np.linalg.norm(d);u=np.cross(d,[1,0,0] if abs(d[0])<.9 else [0,0,1]);u/=np.linalg.norm(u);v=np.cross(d,u)
    return self.loft(n,[[c+r*(u*math.cos(t)+v*math.sin(t)) for t in np.linspace(0,2*math.pi,24,endpoint=False)] for c in [a,b]],mat,pivot)
 def rigpart(self,n,p,axis,param,values,kind='rot',marker=None):
    node=next(q for q in self.g.doc['nodes'] if q['name']==n);axis=np.array(axis,float);axis/=np.linalg.norm(axis)
    ps=[-1,0,1] if len(values)==3 else [0,1]
    node['extras']={'rig_param':param,('rig_axis_gltf' if kind=='rot' else 'rig_move_axis_gltf'):cv(axis).tolist(),'rig_map_param':ps,('rig_map_deg' if kind=='rot' else 'rig_map_m'):values,'rig_param_model':0.,'rig_unit':'deg' if kind=='rot' else 'm'}
    end=np.array(p)+axis if marker is None else np.array(marker)
    self.empty(n+'_HingeAxis',end);node['extras']['hinge_axis_marker']=n+'_HingeAxis'
    self.rig.append(dict(node=n,parent=None,param=param,unit='-1..+1' if len(values)==3 else '0..1',kind=kind,axis_gltf=cv(axis).tolist(),pivot_gltf=cv(p).tolist(),map=list(map(list,zip(ps,values))),model=0.,desc='TCDS travel' if n.startswith(('Aileron','Elevator','Rudder')) else 'Estimated cockpit travel' if kind=='rot' else 'Linear travel',link='',extra={},axis_marker=n+'_HingeAxis'))
 def surface(self,n,ys,le,ch,z,f0=0,f1=1,pivot=None):
    rows=[]
    for y in ys:
        c=ch(abs(y));l=le(abs(y));zz=z(abs(y));ff=np.linspace(f0(y) if callable(f0) else f0,f1(y) if callable(f1) else f1,28)
        def pt(f,sg):
            t=5*.14*(.2969*math.sqrt(max(f,0))-.126*f-.3516*f*f+.2843*f**3-.1036*f**4)
            cam=.018*4*f*(1-f)
            xx=l-c*f;yy=y
            if n.startswith('Wing_Inner_') and abs(y)<1e-9:
                # Root contact follows the host's varying half-width; no wing across the rear cockpit.
                yy=(1 if n.endswith('_L') else -1)*(section(xx)[0]+.001)
            return (xx,yy,zz+c*(e603(f,sg>0) if n.startswith(('Wing_','Aileron')) else sg*t*.65))
        rows.append([pt(f,1) for f in ff]+[pt(f,-1) for f in ff[::-1]])
    return self.loft(n,rows,pivot=pivot)
 def build(self):
    # Fuselage opening uses an ellipse cut at its shoulder; no opaque roof under glass.
    rows=[]
    for x in np.linspace(-5.45,2.50,130):
        w,bt,tp=section(x)
        if -.75<=x<=2.10:
            w,sill,roof=canopy(x)
            # Opening edges meet the glass, lower half is an original egg section.
            upper=[(x,w*math.cos(t),sill+.03*math.sin(t)) for t in np.linspace(math.pi,0,13)[:-1]]
            lower=[(x,w*math.cos(t),sill+(sill-bt)*math.sin(t)) for t in np.linspace(0,-math.pi,37)[:-1]]
            rows.append(upper+lower)
        else:
            upper=[(x,w*math.cos(t),tp+.015*math.sin(t)) for t in np.linspace(math.pi,0,13)[:-1]]
            lower=[(x,w*math.cos(t),tp+(tp-bt)*math.sin(t)) for t in np.linspace(0,-math.pi,37)[:-1]]
            rows.append(upper+lower)
    f=[]
    for i in range(len(rows)-1):
        for j in range(48):
            if -.75<rows[i][0][0]<2.10 and j<12:continue
            f.append((i*48+j,i*48+(j+1)%48,(i+1)*48+(j+1)%48,(i+1)*48+j))
    f.extend([tuple(range(47,-1,-1)),tuple((len(rows)-1)*48+j for j in range(48))])
    self.mesh('Fuselage_Painted',np.array(rows).reshape(-1,3),f)
    # Interior shell is separate inward-facing geometry, 14mm inside exterior, open at the sills.
    rr=[]
    for x in np.linspace(-.72,2.08,70):
        w,sill,roof=canopy(x);bt=section(x)[1];w=max(.01,w-.014);bt+=.014
        rr.append([(x,w*math.cos(t),sill+(sill-bt)*math.sin(t)) for t in np.linspace(0,-math.pi,36)])
    fs=[(i*36+j,(i+1)*36+j,(i+1)*36+j+1,i*36+j+1) for i in range(69) for j in range(35)]
    self.mesh('Cockpit_Liner',np.array(rr).reshape(-1,3),[tuple(reversed(q)) for q in fs],'liner')
    self.g.doc['materials'][self.m['liner']]['doubleSided']=True
    for end,x in [('Front',2.08),('Rear',-.72)]:
        w,sill,roof=canopy(x);bt=section(x)[1];w-=.014;bt+=.014;sill-=.008
        pts=[(x,w*math.cos(t),sill+(sill-bt)*math.sin(t)) for t in np.linspace(0,-math.pi,36)]
        self.mesh('Cockpit_Bulkhead_'+end,pts,[tuple(range(36))],'liner')
    # Separate canopy sections at the fixed bow (photo and manual three-view).
    for name,x0,x1 in [('Front',.54,2.10),('Rear',-.75,.54)]:
        rr=[]
        for x in np.linspace(x0,x1,36):
            w,sill,top=canopy(x);rr.append([(x,w*math.cos(t),sill+(top-sill)*math.sin(t)) for t in np.linspace(0,math.pi,32)])
        fs=[(i*32+j,i*32+j+1,(i+1)*32+j+1,(i+1)*32+j) for i in range(35) for j in range(31)]
        self.mesh('Canopy_Glass_'+name,np.array(rr).reshape(-1,3),fs,'glass')
    for x in [-.60,.54,1.97]:
        w,sill,top=canopy(x);pts=[(x,w*math.cos(t),sill+(top-sill)*math.sin(t)) for t in np.linspace(0,math.pi,28)]
        for i,(a,b) in enumerate(zip(pts,pts[1:])):self.tube(f'Canopy_Bow_{x}_{i}',a,b,.006)
    for sg in [-1,1]:
        pts=[(x,sg*canopy(x)[0],canopy(x)[1]) for x in np.linspace(-.75,2.10,50)]
        for i,(a,b) in enumerate(zip(pts,pts[1:])):self.tube(f'Canopy_Sill_{sg}_{i}',a,b,.008)
    le=lambda s:float(np.interp(s,[0,5,8.75],[0,-.05,-.12]))
    ch=lambda s:float(np.interp(s,[0,5,8.75],[1.3475,1.08,.43]))
    z=lambda s:.12+s*math.tan(math.radians(2.5))
    AF=1-.208/ch(5.25)
    aup=math.degrees(math.asin(90/208));adn=math.degrees(math.asin(50/208))
    eup=math.degrees(math.asin(3.82/9.45));edn=math.degrees(math.asin(2.99/9.45))
    for sg,sd in [(1,'L'),(-1,'R')]:
        self.surface('Wing_Inner_'+sd,sg*np.r_[0,np.linspace(.45,5.25,32)],le,ch,z)
        self.surface('Wing_Outer_'+sd,sg*np.linspace(5.25,8.75,32),le,ch,z,0,AF)
        p=np.array([le(5.25)-AF*ch(5.25),sg*5.25,z(5.25)+.5*(e603(AF,True)+e603(AF,False))*ch(5.25)])
        q=np.array([le(8.75)-AF*ch(8.75),sg*8.75,z(8.75)+.5*(e603(AF,True)+e603(AF,False))*ch(8.75)])
        n='Aileron_'+sd;self.surface(n,sg*np.linspace(5.25,8.75,32),le,ch,z,AF+.004,1,p)
        # L axis points starboard, R points port; positive roll lowers L and raises R.
        self.rigpart(n,p,(q-p)*-1,'aileron',[-aup,0,adn] if sd=='L' else [-adn,0,aup],marker=q)
        # Top-surface Schempp-Hirth blades: no lower spoiler.
        p=[-.58,sg*2.9,z(2.9)+ch(2.9)*e603((le(2.9)+.58)/ch(2.9),True)];n='Spoiler_'+sd
        self.box(n,[p[0],p[1],p[2]-.085],[.045,1.25,.17],'red',p)
        self.rigpart(n,p,[0,0,1],'spoiler',[0,.170],kind='move')
        self.box('Airbrake_Cap_'+sd,[p[0],p[1],p[2]],[.08,1.29,.006]);self.attach('Airbrake_Cap_'+sd,n)
        tle=lambda s:float(np.interp(s,[0,1.6],[-4.68,-4.88]));tch=lambda s:float(np.interp(s,[0,1.6],[.83,.54]));tz=lambda s:.83
        self.surface('Stabilizer_'+sd,sg*np.linspace(.01,1.6,20),tle,tch,tz,0,lambda y:(tle(abs(y))+5.35997)/tch(abs(y)))
        p=[-5.35997,sg*.02,.83]
        # Elevator constant hinge line; trapezoidal trailing edge.
        rows=[[(p[0],sg*s,.83+.018),(-5.68+.16*s/1.6,sg*s,.83+.008),(-5.68+.16*s/1.6,sg*s,.83-.008),(p[0],sg*s,.83-.018)] for s in np.linspace(.02,1.6,20)]
        n='Elevator_'+sd;self.loft(n,rows,pivot=p);self.rigpart(n,p,[0,1,0],'elevator',[-edn,0,eup],marker=[p[0],sg*1.6,.83])
    # Raked fin, T-tail at 0.83; ground at -.67 => total 1.55.
    self.loft('Fin',[[(-4.55,sg*.055,.10),(-5.23,sg*.055,.10),(-5.23,sg*.018,.88),(-4.91,sg*.018,.88)] for sg in [-1,1]])
    p=[-5.23,0,.10];n='Rudder'
    self.loft(n,[[(-5.23,sg*.018,.10),(-5.68,sg*.01,.10),(-5.55,sg*.01,.86),(-5.23,sg*.018,.88)] for sg in [-1,1]],pivot=p)
    ru=math.degrees(math.asin(233/450));self.rigpart(n,p,[0,0,1],'rudder',[-ru,0,ru],marker=[-5.23,0,.88])
    for label,x,r,zc in [('Nose',1.65,.16,-.51),('Main',-.44,.22,-.45)]:
        self.tube(label+'_Wheel',(x,-.062,zc),(x,.062,zc),r,'tyre')
        self.tube(label+'_Hub',(x,-.065,zc),(x,.065,zc),r*.40)
        # Partial fairing only over upper wheel, no black rectangular landing gear.
        self.box(label+'_Fairing',(x,0,zc+.16),(.43,.18,.10))
    self.tube('Tail_Skid',(-5.26,0,.035),(-5.45,0,-.10),.018,'metal')
    self.tube('Pitot_Fin',(-4.85,0,.68),(-4.58,0,.68),.006)
    for x,n in [(2.44,'Nose_Tow_Hook'),(-.35,'CG_Tow_Hook')]:self.tube(n,(x,-.016,-.33 if x>0 else -.46),(x,.016,-.33 if x>0 else -.46),.009)
    for sg in [-1,1]:self.box('Static_Port_'+str(sg),(.53,sg*.38,-.04),(.018,.005,.018),'metal')
    from grob103_cockpit import build_cockpit
    build_cockpit(self)
    self.empty('TorqueTube',[.60,0,-.36]);self.rigpart('TorqueTube',[.60,0,-.36],[1,0,0],'aileron',[-18,0,18])
    for seat in ['F','R']:self.attach('Stick_'+seat,'TorqueTube')
    # Original house livery, original lettering; no reference registrations.
    def side_skin(x,z,sg):
        w,bt,tp=section(x);return sg*(w*math.sqrt(max(0,1-((z-tp)/(tp-bt))**2))+.005)
    for sg in [-1,1]:
        for name,x0,x1,z0,z1,mat,nx,nz in [('Cheatline',-.9,-4.1,.055,.095,'navy' if self.tf else 'red',70,8),('Mark',-1.8,-3.0,-.10,.10,'wordmark' if self.tf else 'mark',50,12)]:
            v=[];uv=[]
            for i,x in enumerate(np.linspace(x0,x1,nx)):
                for j,z in enumerate(np.linspace(z0,z1,nz)):
                    v.append((x,side_skin(x,z,sg),z));uv.append((i/(nx-1) if sg==1 else 1-i/(nx-1),1-j/(nz-1)))
            fs=[(i*nz+j,i*nz+j+1,(i+1)*nz+j+1,(i+1)*nz+j) for i in range(nx-1) for j in range(nz-1)]
            if sg==1:fs=[tuple(reversed(q)) for q in fs]
            self.mesh(name+'_'+str(sg),v,fs,mat,uv=uv if name=='Mark' else None)
    for seat,p in EYES.items():self.empty('Pilot_Eye_'+seat,p)
    self.g.doc['extras']={'cleanRoom':True,'datum':'wing root leading edge; CG not established','authorAxes':'nose +X port +Y up +Z','glTFaxes':'nose -X up +Y port +Z','livery':'talk-flying' if self.tf else 'factory','visualApproval':'pending'}
    OUT.mkdir(parents=True,exist_ok=True);path=OUT/(ID+'-own'+('-tf' if self.tf else '')+'.glb');self.g.save(path)
    self.rig.sort(key=lambda r: bool(r['parent']))
    if not self.tf:(DOC/'grob103-own-rig.json').write_text(json.dumps(dict(model=ID+'-own',livery='factory',rig=self.rig),indent=2)+'\n')
    print('WROTE',path,len(self.g.doc['nodes']),'nodes')
if __name__=='__main__':
    make()
    for tf in [False,True]:Builder(tf).build()
