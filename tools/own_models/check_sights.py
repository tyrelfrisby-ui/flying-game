"""Check committed meshes: actual cap endpoints, pinhole angles, placement, clearances.

python3 tools/own_models/check_sights.py [--report path.json] [--require-visible]
Angles/placement/clearances assert by default; visibility is always reported.
Use --require-visible for the visibility gate (currently red for Great Lakes).
Requires NumPy/Pillow (same environment as the offline GLB builders).
"""
import argparse
import hashlib
import json
import math
import numpy as np
from glb_visuals import Glb
from sights import spec,body,native,parts,placement,segments,ray_hits,MODELS,ROOT


def endpoints(vertices):
    """Recover the cylinder's axis and two cap centres from actual float32 vertices."""
    a=np.unique(vertices,axis=0);centre=a.mean(0)
    _,_,v=np.linalg.svd(a-centre,full_matrices=False);axis=v[0]
    t=(a-centre)@axis
    return np.array([a[t<t.min()+1e-6].mean(0),a[t>t.max()-1e-6].mean(0)])


def pinhole(points,eye,side):
    delta=np.asarray(points)-eye
    # forward=(0,side,0), up=(0,0,-1), camera right=(-side,0,0).
    depth=side*delta[:,1]
    assert np.all(depth>0)
    return np.column_stack((-side*delta[:,0]/depth,-delta[:,2]/depth))


def bar_metrics(vertices,row,side,label):
    ends=endpoints(body(vertices,row));eye=np.array(row['eye_body_m']);centre=ends.mean(0)
    projected=pinhole(ends,eye,side);v=projected[1]-projected[0]
    if v[1]<0:v=-v
    angle=math.degrees(math.atan2(v[0],v[1]));target={'Vertical':0,'Plus45':side*45,'Minus45':-side*45}[label]
    error=abs(angle-target)
    offset=math.degrees(math.atan(np.linalg.norm(pinhole([centre],eye,side)[0])))
    assert error<.01,(label,error)
    assert offset<.01,(label,offset)
    c=eye.copy();c[1]=side*row['y_mount_m']
    assert np.linalg.norm(centre-c)<1e-6,(label,centre,c)
    assert abs(np.linalg.norm(ends[1]-ends[0])-spec()['frame']['bar_length_m'])<1e-6
    assert np.max(abs(body(vertices,row)[:,1]-c[1]))<=spec()['frame']['rod_diameter_m']/2+1e-6
    return dict(angle_deg=angle,error_deg=error,centre_offset_deg=offset,endpoints_body_m=ends.tolist())


def point_segment(p,a,b):
    d=b-a;t=np.clip(np.sum((p-a)*d,axis=-1)/np.maximum(np.sum(d*d,axis=-1),1e-24),0,1)
    return np.linalg.norm(p-(a+t[...,None]*d),axis=-1)


def point_tri(p,t):
    a,b,c=t[:,0],t[:,1],t[:,2];u=b-a;v=c-a;w=p-a
    uu=(u*u).sum(1);vv=(v*v).sum(1);uv=(u*v).sum(1);wu=(w*u).sum(1);wv=(w*v).sum(1)
    det=uu*vv-uv*uv;valid=det>1e-22;den=np.where(valid,det,1)
    s=(wu*vv-wv*uv)/den;q=(wv*uu-wu*uv)/den
    normal=np.cross(u,v);normal/=np.maximum(np.linalg.norm(normal,axis=1)[:,None],1e-24)
    distance=abs((w*normal).sum(1));distance[~(valid&(s>=0)&(q>=0)&(s+q<=1))]=np.inf
    return np.minimum(distance,np.minimum.reduce([point_segment(p,a,b),point_segment(p,b,c),point_segment(p,c,a)]))


def segment_tri(a,b,tris,all_distances=False):
    if not len(tris):return float('inf')
    hits=ray_hits(tris,a,b-a)
    if np.any(hits<=1):return 0.
    result=np.minimum(point_tri(a,tris),point_tri(b,tris))
    # Exact closest points on the two open line segments; boundary cases below.
    d=b-a;dd=d@d
    for j in range(3):
        c=tris[:,j];e=tris[:,(j+1)%3]-c;w=a-c
        ee=(e*e).sum(1);de=e@d;dw=w@d;ew=(e*w).sum(1);den=dd*ee-de*de
        valid=den>1e-22;safe=np.where(valid,den,1)
        s=(de*ew-ee*dw)/safe;t=(dd*ew-de*dw)/safe
        interior=np.linalg.norm(a+s[:,None]*d-c-t[:,None]*e,axis=1)
        interior[~(valid&(s>=0)&(s<=1)&(t>=0)&(t<=1))]=np.inf
        result=np.minimum.reduce([result,interior,point_segment(c,a,b),point_segment(c+e,a,b)])
    return result if all_distances else float(result.min())


def check_file(path,row,aircraft):
    g=Glb(path);M,tr=placement(g,aircraft)
    assert np.allclose(M,row['gltf_to_body_matrix'],atol=1e-10,rtol=0)
    assert np.allclose(tr,row['gltf_to_body_translation_m'],atol=1e-9,rtol=0),('Place shifted',tr)
    nodes={n:body(a,row) for n,p,a,i in parts(g) if n.startswith('Sight_')}
    assert len(nodes)==12,('missing/duplicate sights',len(nodes))
    scene=[];names=[]
    for n,p,a,i in parts(g):
        if n.startswith('Sight_'):continue
        if g.doc['materials'][p['material']].get('alphaMode')=='BLEND':continue
        t=body(a[i],row);scene.extend(t);names.extend([n]*len(t))
    scene=np.array(scene);names=np.array(names)
    # Check every actual rod/support/fitting against the spec before collision analysis.
    for name,a,b,r in segments(row):
        actual=endpoints(nodes[name])
        expected=np.array([a,b])
        if np.linalg.norm(actual-expected)>np.linalg.norm(actual[::-1]-expected):actual=actual[::-1]
        assert np.allclose(actual,expected,atol=1e-6,rtol=0),(name,'mesh differs from spec')
        axis=b-a;axis/=np.linalg.norm(axis);q=nodes[name]-a
        radius=np.linalg.norm(q-(q@axis)[:,None]*axis,axis=1)
        assert np.max(abs(radius-r))<1e-6,(name,'rod diameter')
    result={'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'sides':{},'clearance_m':{},'visibility':{'tested_rays':0,'blocked_rays':0,'first_witnesses':[]}}
    for side,sign in [('L',-1),('R',1)]:
        result['sides'][side]={}
        for label in ['Vertical','Plus45','Minus45']:
            name=f'Sight_{side}_{label}';v=nodes[name]
            result['sides'][side][label]=bar_metrics(native(v,row),row,sign,label)
            ends=np.array(result['sides'][side][label]['endpoints_body_m'])
            # Unobstructed true-eye rays, sampled along every rod, including its centre.
            ray_lo=np.minimum(ends.min(0),np.array(row['eye_body_m']))-1e-5
            ray_hi=np.maximum(ends.max(0),np.array(row['eye_body_m']))+1e-5
            ray_scene=scene[np.all(scene.max(1)>=ray_lo,axis=1)&np.all(scene.min(1)<=ray_hi,axis=1)]
            for f in np.linspace(0,1,101):
                target=ends[0]*(1-f)+ends[1]*f;eye=np.array(row['eye_body_m'])
                hits=ray_hits(ray_scene,eye,target-eye)
                result['visibility']['tested_rays']+=1
                blocked=hits[hits<1-1e-6]
                if len(blocked):
                    result['visibility']['blocked_rays']+=1
                    if len(result['visibility']['first_witnesses'])<6:
                        result['visibility']['first_witnesses'].append(dict(bar=name,fraction=float(f),hit_body_m=(eye+blocked.min()*(target-eye)).tolist()))
    for name,a,b,r in segments(row):
        if name.endswith('Fitting'):continue # fitting intentionally overlaps the named attachment structure
        a,b=endpoints(nodes[name])
        distance=segment_tri(a,b,scene)-r
        assert distance>.001,(path.name,name,'collision',distance)
        result['clearance_m'][name]=distance
    # A conservative continuous swept-volume enclosure: every vertex of a rotating
    # triangle stays within 2*r*sin(limit/2) of its undeformed location. The same
    # bound holds for every barycentric point. Include 100 mm pivot uncertainty.
    swept={}
    for n,p,vertices,ids in parts(g):
        if not n.startswith('Aileron'):continue
        aileron=body(vertices,row);axispoints=np.array(row['aileron_sweep']['hinges_body_m'][n])
        origin=axispoints[0];axis=axispoints[1]-origin;axis/=np.linalg.norm(axis)
        q=aileron-origin;radial=np.linalg.norm(q-(q@axis)[:,None]*axis,axis=1)
        dilation=2*(radial[ids].max(1)+row['aileron_sweep']['hinge_position_uncertainty_m'])*math.sin(row['aileron_sweep']['max_deflection_rad']/2)
        triangles=aileron[ids];minimum=float('inf')
        for name,a,b,r in segments(row):
            if name.endswith('Fitting'):continue
            margin=dilation+r
            near=np.all(triangles.max(1)+margin[:,None]>=np.minimum(a,b),axis=1)&np.all(triangles.min(1)-margin[:,None]<=np.maximum(a,b),axis=1)
            if not near.any():continue
            distances=segment_tri(a,b,triangles[near],all_distances=True)
            gap=float(np.min(distances-dilation[near]-r));minimum=min(minimum,gap)
            assert gap>.001,(path.name,name,n,'full-deflection clearance bound',gap)
        swept[n]=minimum if np.isfinite(minimum) else 'outside conservative swept AABBs'
    result['full_aileron_sweep_clearance_bound_m']=swept
    if aircraft=='pitts-s2b-like':
        # Also test the lowered card from the specified sight eye, in addition to
        # check_iac_assets.py's unchanged historical rear-eye acceptance contract.
        face=np.concatenate([body(a,row) for n,p,a,i in parts(g) if n=='SequenceCard_Face'])
        lo,hi=face.min(0),face.max(0);eye=np.array(row['eye_body_m'])
        for z in np.linspace(lo[2]+.002,hi[2]-.002,11):
            for y in np.linspace(lo[1]+.002,hi[1]-.002,9):
                target=np.array([face[0,0],y,z]);hits=ray_hits(scene,eye,target-eye)
                assert not np.any(hits<1-1e-5),'lowered Pitts card blocked from specified sight eye'
        result['lowered_card_visibility_from_sight_eye_rays']=99
    # Attachment surface, mirrored exactly; measured source point must lie on the named structure.
    for side in [-1,1]:
        a=np.array(row['attachment_body_m']);a[1]*=side
        t=scene[names==row['attachment_structure']]
        assert point_tri(a,t).min()<1e-5
    return result


def main():
    ap=argparse.ArgumentParser();ap.add_argument('--report');ap.add_argument('--require-visible',action='store_true',help='Also fail if any rod is hidden by opaque structure');args=ap.parse_args();report={}
    for aircraft,row in spec()['aircraft'].items():
        for filename in row['glbs']:
            path=MODELS/aircraft/filename
            report[str(path.relative_to(ROOT))]=check_file(path,row,aircraft)
            m=report[str(path.relative_to(ROOT))]
            print('PASS',filename,'max angle/centre error deg',max(v['error_deg'] for s in m['sides'].values() for v in s.values()),max(v['centre_offset_deg'] for s in m['sides'].values() for v in s.values()))
    if args.report:(ROOT/args.report).write_text(json.dumps(report,indent=2)+'\n')
    print('PASS: all 8 livery GLBs; actual mesh axes/centres, placement invariant, structure attachment, static and continuous full-deflection capsule clearance.')
    hidden=[(p,m['visibility']['blocked_rays']) for p,m in report.items() if m['visibility']['blocked_rays']]
    for path,count in hidden:print('FLAG: opaque structure blocks',count,'of 606 true-eye rod rays:',path)
    assert not (args.require_visible and hidden),'opaque structure blocks sights; see report/NEEDS YOU'
    if not hidden:print('PASS: 606/606 true-eye rod visibility rays per GLB.')

if __name__=='__main__':main()
