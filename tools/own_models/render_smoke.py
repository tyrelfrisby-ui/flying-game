"""Offline smoke measurements and annotated CPU renders (no Unity/Blender process).
Run from the repository root with Python + numpy + OpenImageIO. For example:
 /Applications/Blender.app/Contents/Resources/5.2/python/bin/python3.13 tools/own_models/render_smoke.py
Models are read with git show into a TemporaryDirectory, never into this worktree.
PNG views use neutral materials to expose geometry; texture art is not reproduced.
"""
import subprocess, tempfile, math, sys
import OpenImageIO as oiio
import json,struct,pathlib,numpy as np

def load_glb(path):
 b=pathlib.Path(path).read_bytes(); n=struct.unpack_from('<I',b,12)[0];g=json.loads(b[20:20+n]); binary=b[28+n:]
 def acc(i):
  a=g['accessors'][i];v=g['bufferViews'][a['bufferView']]; dt={5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1'}[a['componentType']]; size={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']];o=v.get('byteOffset',0)+a.get('byteOffset',0); d=np.dtype(dt)
  return np.ndarray((a['count'],size),dtype=d,buffer=binary,offset=o,strides=(v.get('byteStride',size*d.itemsize),d.itemsize)).copy()
 out=[]
 def visit(i,parent):
  n=g['nodes'][i];m=np.eye(4)
  if 'matrix' in n:m=np.array(n['matrix']).reshape(4,4).T
  else:
   if 'rotation' in n:
    x,y,z,w=n['rotation'];m[:3,:3]=[[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]]
   m[:3,:3]=m[:3,:3]@np.diag(n.get('scale',[1,1,1]));m[:3,3]=n.get('translation',[0,0,0])
  m=parent@m
  if 'mesh'in n:
   for p in g['meshes'][n['mesh']]['primitives']:
    v=acc(p['attributes']['POSITION']);v=v@m[:3,:3].T+m[:3,3];f=acc(p['indices']).reshape(-1,3) if 'indices'in p else np.arange(len(v)).reshape(-1,3)
    mat=g.get('materials',[])[p['material']] if 'material'in p else {};col=mat.get('pbrMetallicRoughness',{}).get('baseColorFactor',[.65,.7,.76,1])
    out.append((n.get('name',''),v,f,col))
  for j in n.get('children',[]):visit(j,m)
 for i in g['scenes'][g.get('scene',0)]['nodes']:visit(i,np.eye(4))
 return out

def load_obj(path):
    vertices, out, faces, name = [], [], [], ''
    def finish():
        if not faces: return
        indices, inverse = np.unique(faces, return_inverse=True)
        out.append((name, np.array(vertices)[indices], inverse.reshape(-1, 3), [.72,.74,.68,1]))
    for line in pathlib.Path(path).read_text().splitlines():
        t = line.split()
        if not t: continue
        if t[0] == 'v': vertices.append(tuple(map(float, t[1:4])))
        elif t[0] == 'o': finish(); faces = []; name = t[1]
        elif t[0] == 'f':
            ind = [int(x.split('/')[0])-1 for x in t[1:]]
            faces.extend((ind[0], ind[k], ind[k+1]) for k in range(1, len(ind)-1))
    finish()
    return out

# End-ring centres and preceding centreline point, in FILE axes, measured on the meshes.
# GLB axes: x longitudinal, y up, z span. OBJ uses the same axis order here.
# PA18 mouth = centre of the 32-vertex outer lip, not its lowest vertex.
SOURCES = {
    'pitts-s2b-like': ('f9c2e03','pitts-s2b-like','-own.glb',-1,6.094/5.996,[-.74,-.63,.21],[-.86,-.60,.20],[1.72,0,-.05]),
    'great-lakes-like': ('f9c2e03','great-lakes-like','-own.glb',-1,1,[.12,-.84,.19],[.02,-.78,.18],[.85,0,0]),
    'c172-like': ('340cc5a','c172-like','-own.glb',1,1,[1.3625,-.85,.205],[1.4325,-.71,.20],[2.42,0,0]),
    'pa18-cub-like': ('c0f2ad5','pa18-cub-like','.obj',-1,1,[-262.36355,-60.0699,-25.0751],[-262.36355,-50.917,-21.025],[2.12,0,-.25]),
    'j3-cub-like': ('c16f0bd','j3-cub-like','-own.glb',-1,1,[-1.027,-.459054,-.18],[-1.087,-.411054,-.175],[2.07,0,-.25]),
    'j3-cub-floats-like': ('c16f0bd','j3-cub-floats-like','-own.glb',-1,1,[-1.027,-.459054,-.18],[-1.087,-.411054,-.175],[2.07,0,-.25]),
}
SOURCES['pa18-bush-like'] = SOURCES['pa18-cub-like']
SOURCES['pa18-floats-like'] = SOURCES['pa18-cub-like']
ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT/'docs/pending/smoke-renders'

def place(ms, cfg, sign, trim, own_gear):
    # Reproduce AirframeModels.Place / OuterWingMid / ConfigOuterWingMid.
    # In particular, the current ConfigOuterWingMid does NOT subtract mass.cg.x.
    ms = [(n, v[:,[0,2,1]]*[sign,1,1], f,c) for n,v,f,c in ms]
    vertices = np.concatenate([v for n,v,f,c in ms])
    strips = [st for sf in cfg['surfaces'] if 'wing' in sf['id'].lower() for st in sf['strips']]
    half = max(abs(st['pos'][1]) for st in strips)
    scale = 2*half/np.ptp(vertices[:,1])*trim
    vertices *= scale
    outer = vertices[(abs(vertices[:,1])>=.65*half)&(abs(vertices[:,1])<=.92*half)]
    selected = [st for st in strips if .65*half<=abs(st['pos'][1])<=.92*half]
    cmid = (min(st['pos'][0]-.75*st['chord'] for st in selected)+max(st['pos'][0]+.25*st['chord'] for st in selected))/2
    shift = np.array([cmid-(outer[:,0].min()+outer[:,0].max())/2, -(vertices[:,1].min()+vertices[:,1].max())/2, 0.])
    gear_down = max([g['pos'][2] for g in cfg['gear']] or [0])
    # As in Place: keel replaces wheel contact only for one hull with no gear.
    if cfg.get('floats',{}).get('count') == 1 and not cfg['gear']: gear_down=cfg['floats']['keelZ']
    shift[2] = -gear_down-vertices[:,2].min()
    if own_gear: shift[2] = np.mean([-st['pos'][2]+cfg['mass']['cg'][2] for st in selected])-outer[:,2].mean()
    return [(n,v*scale+shift,f,c) for n,v,f,c in ms], scale, shift


def render_panel(meshes, bounds, view, markers):
    W,H=1600,850
    pix=np.ones((H,W,3),dtype=np.float32)*.96
    depth=np.ones((H,W))*-1e10
    # x forward, y port, z up. Side from starboard; belly from below/starboard.
    camera=np.array([0.,-1.,0.]) if view=='side' else np.array([.18,-.82,-.55])
    camera/=np.linalg.norm(camera)
    right=np.cross(np.array([0,0,1]),camera);right/=np.linalg.norm(right)
    up=np.cross(camera,right)
    center,width=bounds
    def project(v):
        vv=np.atleast_2d(v)-center
        return np.column_stack((vv@right*W/width+W/2,H/2-vv@up*W/width,vv@camera))
    for name,v,faces,col in meshes:
        q=project(v)
        for tri in faces:
            a,b,c=q[tri]
            xmin=max(0,int(min(a[0],b[0],c[0])));xmax=min(W,int(max(a[0],b[0],c[0]))+1)
            ymin=max(0,int(min(a[1],b[1],c[1])));ymax=min(H,int(max(a[1],b[1],c[1]))+1)
            if xmin>=xmax or ymin>=ymax:continue
            den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
            if abs(den)<1e-6:continue
            yy,xx=np.mgrid[ymin:ymax,xmin:xmax]
            u=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
            w=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den;t=1-u-w
            z=u*a[2]+w*b[2]+t*c[2];dst=depth[ymin:ymax,xmin:xmax]
            mask=(u>=0)&(w>=0)&(t>=0)&(z>dst)
            normal=np.cross(v[tri[1]]-v[tri[0]],v[tri[2]]-v[tri[0]])
            normal/=max(np.linalg.norm(normal),1e-12)
            shade=.48+.52*abs(normal@np.array([.25,-.8,.55])/1.004)
            color=np.array(col[:3])*.7+.22
            if 'exhaust' in name.lower():color=np.array([.28,.3,.32])
            pix[ymin:ymax,xmin:xmax][mask]=color*shade;dst[mask]=z[mask]
    buf=oiio.ImageBuf(pix)
    def line(p,q,col):
        p,q=project(p)[0],project(q)[0]
        for dy in [-1,0,1]:oiio.ImageBufAlgo.render_line(buf,int(p[0]),int(p[1])+dy,int(q[0]),int(q[1])+dy,col)
    def dot(p,col,r=8):
        p=project(p)[0]
        for dy in range(-r,r+1):
            dx=int(math.sqrt(r*r-dy*dy));oiio.ImageBufAlgo.render_line(buf,int(p[0])-dx,int(p[1])+dy,int(p[0])+dx,int(p[1])+dy,col)
    authored,mouth,legacy,direction,nose=markers
    line(nose,np.array([mouth[0],nose[1],nose[2]]),(.14,.37,.8))
    dot(nose,(.14,.37,.8),5)
    dot(legacy,(.8,.18,.7),9)
    dot(mouth,(0,.62,.68),12);dot(authored,(.95,.20,.05),7)
    end=authored+direction*.7
    line(authored,end,(.95,.20,.05))
    # arrowhead in the view plane
    a=project(end)[0];b=project(authored)[0];d=(b[:2]-a[:2]);d/=max(np.linalg.norm(d),1e-9)
    for sign in [-1,1]:
        v=a[:2]+d*23+np.array([-d[1],d[0]])*sign*11
        oiio.ImageBufAlgo.render_line(buf,int(a[0]),int(a[1]),int(v[0]),int(v[1]),(.95,.20,.05))
    return buf

ANNOTATIONS = {}

def text(buf,x,y,value,size=25,color=(.08,.10,.14)):
    ANNOTATIONS.setdefault(CURRENT_ID, []).append(dict(x=x,y=y,text=value,size=size,color=color))


def main():
    global CURRENT_ID
    OUT.mkdir(parents=True,exist_ok=True)
    report=json.loads((OUT/'measurements.json').read_text()) if (OUT/'measurements.json').exists() else {}
    with tempfile.TemporaryDirectory(prefix='smoke-models-') as tmp:
        for id,(ref,model,suffix,sign,trim,ring,previous,hub) in SOURCES.items():
            if len(sys.argv)>1 and id not in sys.argv[1:]: continue
            CURRENT_ID = id
            cfg=json.loads((ROOT/f'configs/aircraft/{id}.json').read_text())
            path=f'unity/Assets/_Project/Resources/Models/{model}/{model}{suffix}'
            blob=subprocess.check_output(['git','show',ref+':'+path],cwd=ROOT)
            local=pathlib.Path(tmp)/(model+suffix);local.write_bytes(blob)
            raw=(load_obj if suffix=='.obj' else load_glb)(local)
            meshes,scale,shift=place(raw,cfg,sign,trim,id in ['pa18-bush-like','pa18-floats-like'])
            def placed(p):return np.array(p)[[0,2,1]]*[sign,1,1]*scale+shift
            mouth=placed(ring);prior=placed(previous)
            vertices=np.concatenate([v for _,v,_,_ in meshes]);nose=vertices[np.argmax(vertices[:,0])].copy()
            if id=='j3-cub-floats-like':
                # floats extend ahead of the prop: nose is the foremost PROP vertex, not the float bow.
                props=np.concatenate([v for n,v,f,c in meshes if n=='Prop']);nose=props[np.argmax(props[:,0])].copy()
            nose[1]=0
            sm=cfg['smoke'];authored=np.array(sm['outletBodyM'])*[1,-1,-1]
            # point existing schema alone would generate (SmokeTrail.ExhaustsFor).
            legacy=np.array(hub)*[1,-1,-1]+[-sm['aftOfNoseM']+.25,0,-max(.25,cfg['fuselage']['crossflow']['bodyRadiusM'])]
            angle=math.radians(sm['downDeg']);direction=np.array([-math.cos(angle),0,-math.sin(angle)])
            measured_angle=math.degrees(math.atan2(prior[2]-mouth[2],prior[0]-mouth[0]))
            error=float(np.linalg.norm(authored-mouth))
            aft=float(nose[0]-mouth[0])
            report[id]={'modelCommit':subprocess.check_output(['git','rev-parse',ref],cwd=ROOT,text=True).strip(), 'modelPath':path,'scale':scale,'shiftForwardPortUpM':shift.tolist(),'measuredMouthBodyM':(mouth*[1,-1,-1]).tolist(),'aftOfNoseMeasuredM':aft,'downMeasuredDeg':measured_angle,'authoredErrorM':error,'legacyErrorM':float(np.linalg.norm(legacy-mouth))}
            # Shared PA18 block is within the lip radius. J3 floats compensate its 10 cm visual shift.
            assert error<.025,(id,error)
            # Full side context then a front/belly close-up, in one image per requested config.
            full_center=(vertices.min(0)+vertices.max(0))/2;full_center[1]=0
            side_width=np.ptp(vertices[:,0])*1.12
            close_center=mouth+np.array([.1,0,.20]);close_center[1]=0
            side=render_panel(meshes,(full_center,side_width),'side',(authored,mouth,legacy,direction,nose))
            belly=render_panel(meshes,(close_center,3.5),'belly',(authored,mouth,legacy,direction,nose))
            final=oiio.ImageBuf(oiio.ImageSpec(1600,1900,3,oiio.FLOAT));oiio.ImageBufAlgo.fill(final,(.96,.96,.96))
            oiio.ImageBufAlgo.paste(final,0,120,0,0,side)
            oiio.ImageBufAlgo.paste(final,0,990,0,0,belly)
            text(final,35,42,id+' | '+ref+' | side + belly close-up',31)
            text(final,35,80,f'aft {sm["aftOfNoseM"]:.2f} m; down {sm["downDeg"]} deg; {sm["displacementCuIn"]} cu in; outlet {sm["outletBodyM"]}',24)
            text(final,35,968,'BELLY / STARBOARD: points and arrow overlaid through the skin',25)
            text(final,35,1840,'Orange: authored outlet + jet   Cyan: measured mouth   Magenta: old schema point   Blue: nose station',23)
            text(final,35,1876,f'Mouth error {error*1000:.1f} mm | legacy error {np.linalg.norm(legacy-mouth)*100:.1f} cm | neutral materials; measured geometry',23)
            if id.startswith('j3'):text(final,35,160,'Newest J-3 model is not installed on this lane; model adoption / float placement needs review.',23)
            assert final.write(str(OUT/(id+'.png'))),final.geterror()
            print(id,'mouth error mm',round(error*1000,2),'aft',round(aft,4),'down',round(measured_angle,3),flush=True)
    (OUT/'annotations.json').write_text(json.dumps(ANNOTATIONS))
    subprocess.run(['swift','-module-cache-path','/tmp/smoke-swift-cache',str(ROOT/'tools/own_models/smoke_labels.swift'),str(OUT)],check=True)
    (OUT/'annotations.json').unlink()
    (OUT/'measurements.json').write_text(json.dumps(report,indent=2)+'\n')

if __name__=='__main__':main()
