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
import re
import subprocess
import numpy as np
from glb_visuals import Glb
from sights import spec,body,native,parts,placement,segments,cylinder,ray_hits,MODELS,ROOT


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


def line_angle(projected):
    v=projected[1]-projected[0]
    angle=(math.degrees(math.atan2(v[1],v[0]))+90)%180-90
    return 90. if abs(angle+90)<1e-8 else angle


def angle_error(angle,target): return abs((angle-target+90)%180-90)


def bar_metrics(vertices,row,side,label):
    ends=endpoints(body(vertices,row));eye=np.array(row['eye_body_m']);centre=ends.mean(0)
    angle=line_angle(pinhole(ends,eye,side))
    target={'Level':0,'Vertical':90,'Plus45':side*45,'Minus45':-side*45}[label]
    error=angle_error(angle,target)
    offset=math.degrees(math.atan(np.linalg.norm(pinhole([centre],eye,side)[0])))
    assert error<.01,(label,error)
    c=eye.copy();c[1]=side*row['y_frame_m']
    lateral_residual=float(np.linalg.norm((centre-eye)[[0,2]]))
    assert np.linalg.norm(centre-c)<1e-6,(label,centre,c)
    assert lateral_residual<=1e-6,(label,'off eye lateral line',lateral_residual)
    assert abs(np.linalg.norm(ends[1]-ends[0])-spec()['frame']['bar_length_m'])<1e-6
    assert np.max(abs(body(vertices,row)[:,1]-c[1]))<=spec()['frame']['rod_diameter_m']/2+1e-6
    return dict(angle_deg=angle,target_deg=target,error_deg=error,centre_offset_deg=offset,
                eye_lateral_line_residual_m=lateral_residual,centre_body_m=centre.tolist(),endpoints_body_m=ends.tolist())


def cockpit_camera_report(row,aircraft,sides):
    # Trace the current procedural Canopy position, which PilotEgress uses even when its
    # renderer is hidden by the imported model. Do not replace it with the measured rear eye.
    source=(ROOT/'unity/Assets/_Project/Scripts/Bridge/AirframeVisual.cs').read_text()
    style=source.split('case "'+aircraft+'":')[1].split('};')[0]
    axis=float(re.search(r'BodyAxisZ = ([\d.\-]+)f',style)[1])
    canopy=[float(v.strip().removesuffix('f')) for v in re.search(r'Canopy = \(([^)]+)\)',style)[1].split(',')]
    cg=np.array(json.loads((ROOT/'configs/aircraft'/f'{aircraft}.json').read_text())['mass']['cg'])
    camera=np.array([canopy[0],0,canopy[1]+axis-.15-.1])-cg
    result=dict(camera_body_m=camera.tolist(),offset_from_rear_eye_body_m=(camera-row['eye_body_m']).tolist(),
        derivation='Canopy U(x,0,z+BodyAxisZ) minus config mass.cg, PilotEgress +Unity up*0.15, ChaseCamera +Unity up*0.1',
        convention='Angles from horizontal, up=-body Z; each side re-aimed at C with zero roll. Also report body-lateral fixed-axis projection. Default in-game camera looks forward/down 4 degrees; all bar endpoints are behind its forward plane, so actual visible angle errors are undefined. Re-aimed measurements are hypothetical diagnostics.',sides={})
    chase=(ROOT/'unity/Assets/_Project/Scripts/Bridge/ChaseCamera.cs').read_text()
    pitch=math.radians(float(re.search(r'CockpitLookDownDeg = ([\d.]+)f',chase)[1]))
    actual_forward=np.array([math.cos(pitch),0,math.sin(pitch)])
    result['default_forward_camera_bars']={}
    for side,sign in [('L',-1),('R',1)]:
        centre=np.array(row['eye_body_m']);centre[1]=sign*row['y_frame_m']
        forward=centre-camera;forward/=np.linalg.norm(forward)
        right=np.cross(forward,[0,0,-1]);right/=np.linalg.norm(right)
        up=np.cross(right,forward);basis=np.array([right,up,forward]).T
        rays=pinhole([centre],camera,sign)[0]
        metrics={}
        for label,bar in sides[side].items():
            ends=np.array(bar['endpoints_body_m']);q=(ends-camera)@basis
            angle=line_angle(q[:,:2]/q[:,2,None])
            fixed=line_angle(pinhole(ends,camera,sign))
            depth=(ends-camera)@actual_forward
            result['default_forward_camera_bars'][f'{side}_{label}']=dict(endpoint_forward_depth_range_m=[float(depth.min()),float(depth.max())],
                behind_camera=bool(np.all(depth<0)),visible_angle_error_deg=None)
            metrics[label]=dict(aimed_at_c_angle_deg=angle,aimed_at_c_error_deg=angle_error(angle,bar['target_deg']),
                fixed_body_lateral_angle_deg=fixed,fixed_body_lateral_error_deg=angle_error(fixed,bar['target_deg']))
        result['sides'][side]=dict(centre_off_body_lateral_axis_deg=math.degrees(math.atan(np.linalg.norm(rays))),
            centre_horizontal_bearing_deg=math.degrees(math.atan(rays[0])),centre_vertical_bearing_deg=math.degrees(math.atan(rays[1])),bars=metrics)
    return result


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
    expected_names={n for n,a,b,r in segments(row)}
    assert nodes.keys()==expected_names,('missing/unexpected sights',nodes.keys()^expected_names)
    # Inspect the entire document too: no dormant mount mesh/node may survive outside the scene.
    for key in ['nodes','meshes']:
        sight_names=[n.get('name','') for n in g.doc[key] if n.get('name','').startswith('Sight_')]
        assert len(sight_names)==8 and set(sight_names)==expected_names,('mount/duplicate sight geometry',key,sight_names)
    assert not any(m.get('name')=='Sight_Clamp' for m in g.doc['materials']),'obsolete mount material'
    scene=[];names=[]
    for n,p,a,i in parts(g):
        if n.startswith('Sight_'):continue
        if g.doc['materials'][p['material']].get('alphaMode')=='BLEND':continue
        t=body(a[i],row);scene.extend(t);names.extend([n]*len(t))
    scene=np.array(scene);names=np.array(names)
    # Check every exported bar against the shared geometry before collision analysis.
    meshes=[(n,*cylinder(a,b,r)) for n,a,b,r in segments(row)]
    for name,vertices,triangles in meshes:
        actual=np.unique(nodes[name],axis=0);expected=np.array(vertices)
        distances=np.linalg.norm(actual[:,None]-expected[None,:],axis=2)
        assert max(distances.min(0).max(),distances.min(1).max())<1e-6,(name,'mesh differs from spec')
    result={'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'sides':{},'clearance_m':{},'visibility':{'tested_rays':0,'blocked_rays':0,'first_witnesses':[],'legacy_three_bar_tested_rays':0,'legacy_three_bar_blocked_rays':0}}
    result['mount_attachment_mesh_count']=0
    result['intersection']={}
    for side,sign in [('L',-1),('R',1)]:
        result['sides'][side]={}
        for label in ['Level','Vertical','Plus45','Minus45']:
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
                if label!='Level':result['visibility']['legacy_three_bar_tested_rays']+=1
                blocked=hits[hits<1-1e-6]
                if len(blocked):
                    result['visibility']['blocked_rays']+=1
                    if label!='Level':result['visibility']['legacy_three_bar_blocked_rays']+=1
                    if len(result['visibility']['first_witnesses'])<6:
                        result['visibility']['first_witnesses'].append(dict(bar=name,fraction=float(f),hit_body_m=(eye+blocked.min()*(target-eye)).tolist()))
        bars=result['sides'][side]
        # Fit the common point to all FOUR independently recovered mesh axes.
        projectors=[];midpoints=[]
        for bar in bars.values():
            ends=np.array(bar['endpoints_body_m']);d=ends[1]-ends[0];d/=np.linalg.norm(d)
            projectors.append(np.eye(3)-np.outer(d,d));midpoints.append(ends.mean(0))
        common=np.linalg.solve(np.sum(projectors,axis=0),sum(P@c for P,c in zip(projectors,midpoints)))
        residual=max(np.linalg.norm(P@(common-c)) for P,c in zip(projectors,midpoints))
        midpoint_residual=max(np.linalg.norm(common-c) for c in midpoints)
        lateral_residual=np.linalg.norm((common-np.array(row['eye_body_m']))[[0,2]])
        assert max(residual,midpoint_residual,lateral_residual)<=1e-6,('intersection',side,common,residual)
        result['intersection'][side]=dict(common_body_m=common.tolist(),axis_residual_m=residual,
            midpoint_residual_m=midpoint_residual,eye_lateral_line_residual_m=lateral_residual)
    result['in_game_cockpit_camera']=cockpit_camera_report(row,aircraft,result['sides'])
    for name,a,b,r in segments(row):
        a,b=endpoints(nodes[name])
        distance=segment_tri(a,b,scene)-r
        assert distance>.001,(path.name,name,'collision',distance)
        result['clearance_m'][name]=distance
    # Cover the full motion with <=2-degree intervals. Each sampled triangle is
    # dilated by its maximum movement over HALF an interval, plus pivot-location
    # uncertainty over the full deflection. Thus there are no sampling gaps; this
    # is tighter than dilating an undeformed triangle by the entire +/- travel.
    swept={}
    for n,p,vertices,ids in parts(g):
        if not n.startswith('Aileron'):continue
        aileron=body(vertices,row);axispoints=np.array(row['aileron_sweep']['hinges_body_m'][n])
        origin=axispoints[0];axis=axispoints[1]-origin;axis/=np.linalg.norm(axis)
        q=aileron-origin;radial=np.linalg.norm(q-(q@axis)[:,None]*axis,axis=1)
        limit=row['aileron_sweep']['max_deflection_rad'];steps=math.ceil(2*limit/math.radians(2))
        dilation=2*radial[ids].max(1)*math.sin(limit/(2*steps))+2*row['aileron_sweep']['hinge_position_uncertainty_m']*math.sin(limit/2)
        minimum=float('inf')
        for angle in np.linspace(-limit,limit,steps+1):
            rotated=origin+q*math.cos(angle)+np.cross(axis,q)*math.sin(angle)+(q@axis)[:,None]*axis*(1-math.cos(angle))
            triangles=rotated[ids]
            for name,a,b,r in segments(row):
                margin=dilation+r
                near=np.all(triangles.max(1)+margin[:,None]>=np.minimum(a,b),axis=1)&np.all(triangles.min(1)-margin[:,None]<=np.maximum(a,b),axis=1)
                if not near.any():continue
                distances=point_tri(a,triangles[near]) if np.array_equal(a,b) else segment_tri(a,b,triangles[near],all_distances=True)
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
    # Entire conservative rotating-prop slab: all components must remain aft
    # of even its rearmost blade vertex. Stronger than a finite disc test here.
    prop=np.concatenate([body(a,row) for n,p,a,i in parts(g) if n=='Prop'])
    all_sight=np.concatenate(list(nodes.values()))
    gap=float(prop[:,0].min()-all_sight[:,0].max())
    assert gap>.001,('propeller swept disc slab',gap)
    result['propeller_swept_slab_clearance_m']=gap
    return result


def check_eye_source():
    data=spec();path=ROOT/'docs/models/eyepoints.json'
    source=json.loads(path.read_text() if path.exists() else subprocess.check_output(
        ['git','show',data['source_commit']+':docs/models/eyepoints.json'],cwd=ROOT))
    # Take the NEWEST version each entry carries. v4 is Codex's proposal (lane/codex-eyepoints, marked
    # proposed/requires_approval); v5 is the one Ty approved on 2026-10-31 and is what the configs, the camera
    # and these bars are built on. The Great Lakes rear is the only one where they differ -- v4 was computed
    # with the aeroplane 0.223 m low and sat inside the tub.
    def newest(entry):
        version=max((k for k in entry if k.startswith('v') and k[1:].isdigit()), key=lambda k: int(k[1:]))
        return entry[version]['position_body']
    for aircraft,row in data['aircraft'].items():
        eyes=[newest(e) for e in source['entries']
              if e.get('aircraft_id')==aircraft and e.get('seat_id')=='rear'
              and any(k.startswith('v') and k[1:].isdigit() for k in e)]
        assert eyes and all(np.allclose(eye,row['eye_body_m'],atol=1e-9) for eye in eyes),(aircraft,'rear eye source drift',eyes)


def main():
    check_eye_source()
    ap=argparse.ArgumentParser();ap.add_argument('--report');ap.add_argument('--require-visible',action='store_true',help='Also fail if any rod is hidden by opaque structure');args=ap.parse_args();report={}
    for aircraft,row in spec()['aircraft'].items():
        for filename in row['glbs']:
            path=MODELS/aircraft/filename
            report[str(path.relative_to(ROOT))]=check_file(path,row,aircraft)
            m=report[str(path.relative_to(ROOT))]
            print('PASS',filename,'max angle/centre error deg',max(v['error_deg'] for s in m['sides'].values() for v in s.values()),max(v['centre_offset_deg'] for s in m['sides'].values() for v in s.values()))
    if args.report:(ROOT/args.report).write_text(json.dumps(report,indent=2)+'\n')
    print('PASS: all 8 livery GLBs; actual mesh axes/centres, placement invariant, zero attachments, four common-centre bars, static and continuous full-deflection capsule clearance.')
    hidden=[(p,m['visibility']['blocked_rays']) for p,m in report.items() if m['visibility']['blocked_rays']]
    for path,count in hidden:print('FLAG: opaque structure blocks',count,'of',report[path]['visibility']['tested_rays'],'true-eye bar rays:',path)
    assert not (args.require_visible and hidden),'opaque structure blocks sights; see report/NEEDS YOU'
    if not hidden:print('PASS: 808/808 true-eye bar visibility rays per GLB.')

if __name__=='__main__':main()
