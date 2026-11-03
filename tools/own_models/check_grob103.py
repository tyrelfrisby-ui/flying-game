"""Checks the exported asset, independently pinned physical limits and cockpit separations.
Intentional attachments (harness/cushion, bezel/face, fairing/body) are not collision faults.
"""
import json, math
from pathlib import Path
import numpy as np
from glb_visuals import Glb
from build_grob103 import ROOT,OUT,ID

def check(path):
 g=Glb(path);nodes=g.doc['nodes'];roots=g.doc['scenes'][0]['nodes'];parents={}
 for i,n in enumerate(nodes):
    for c in n.get('children',[]):
        assert 0<=c<len(nodes),(i,c)
        assert c not in parents,('two parents',c)
        parents[c]=i
 for r in roots:assert 0<=r<len(nodes) and r not in parents
 seen=set();active=set()
 def walk(i):
    assert i not in active,'cycle';assert i not in seen,'duplicate root/parent';seen.add(i);active.add(i)
    for c in nodes[i].get('children',[]):walk(c)
    active.remove(i)
 for r in roots:walk(r)
 assert len(seen)==len(nodes),'orphans'
 world=g.world_matrices();parts={};tris={}
 for i,n in enumerate(nodes):
    if 'mesh' not in n:continue
    vv=[];tt=[]
    for p in g.doc['meshes'][n['mesh']]['primitives']:
        v=g.array(p['attributes']['POSITION']);assert np.isfinite(v).all()
        v=v@world[i][:3,:3].T+world[i][:3,3]
        ids=g.array(p['indices']).reshape(-1,3) if 'indices' in p else np.arange(len(v)).reshape(-1,3)
        assert ids.min()>=0 and ids.max()<len(v)
        vv.extend(v);tt.extend(v[ids])
    parts[n['name']]=np.array(vv);tris[n['name']]=np.array(tt)
 points=np.concatenate(list(parts.values()));size=np.ptp(points,axis=0)
 assert abs(size[2]/18.00-1)<=.01,size
 assert abs(size[0]/8.18-1)<=.02,size
 assert abs(size[1]/1.81-1)<=.05,size
 # Twin III geometry witnesses independently pinned to the requested dimensions.
 assert abs(parts['Fin'][:,1].max()+.51-1.71)<1e-5
 glass=np.concatenate([v for n,v in parts.items() if n.startswith('Canopy_Glass')])
 assert abs(np.ptp(glass[:,0])-2.60)<1e-5
 assert .99 < glass[:,1].max()+.51 < 1.001
 assert 'Tail_Skid' not in parts
 for label,aft,diam,width,exposed in [('Nose',.80,.26,.085,.10),('Main',2.90,.44,.15,.12),('Tail',7.10,.21,.065,.07)]:
    v=parts[label+'_Wheel'];centre=(v.min(0)+v.max(0))/2
    assert abs(centre[0]+2.50-aft)<1e-5
    assert np.allclose(np.ptp(v,axis=0),[diam,diam,width],atol=1e-5)
    from build_grob103 import section
    assert abs(section(-centre[0])[1]-v[:,1].min()-exposed)<1e-5
    assert label+'_Fairing' in parts
 rig=json.loads((ROOT/'docs/models/grob103-own-rig.json').read_text())['rig']
 pivots={'Aileron_L':None,'Aileron_R':None,'Elevator_L':[5.35997,1.15,.02],'Elevator_R':[5.35997,1.15,-.02],'Rudder':[5.23,.10,0],'Spoiler_L':None,'Spoiler_R':None}
 for r in rig:
    i=next(i for i,n in enumerate(nodes) if n['name']==r['node']);p=world[i][:3,3];assert np.allclose(p,r['pivot_gltf'],atol=1e-6)
    extra=nodes[i].get('extras',{});assert extra['rig_param']==r['param']
    assert (nodes[parents[i]]['name'] if i in parents else None)==r['parent']
    assert np.allclose(extra['rig_move_axis_gltf' if r['kind']=='move' else 'rig_axis_gltf'],r['axis_gltf'])
    assert np.allclose(np.c_[extra['rig_map_param'],extra['rig_map_m' if r['kind']=='move' else 'rig_map_deg']],r['map'])
    for key,val in r['extra'].items():assert extra[key]==val
    marker=next(i for i,n in enumerate(nodes) if n['name']==r['axis_marker']);d=world[marker][:3,3]-p;a=np.array(r['axis_gltf'])
    assert np.linalg.norm(np.cross(d,a))<1e-5,('axis marker',r['node'])
    if r['node'] in pivots and pivots[r['node']] is not None:assert np.allclose(p,pivots[r['node']],atol=1e-6)
    if r['node'].startswith('Aileron'):
        s=5.25;le=-.05+(.25/4.0)*(-.07);ch=1.08+(.25/4.0)*(.43-1.08);af=1-.208/ch;cam=.5*(np.interp(af,[.67748,.81231],[.08372,.04956])+np.interp(af,[.72446,.86085],[-.03094,-.00417]));expected=[-(le-af*ch),.12+s*math.tan(math.radians(2.5))+cam*ch,s if r['node'].endswith('L') else -s]
        assert np.allclose(p,expected,atol=1e-6)
 # TCDS deflections independently calculated from the stated linear measurements.
 expected_angles={'Aileron_L':[-math.degrees(math.asin(90/208)),0,math.degrees(math.asin(50/208))], 'Aileron_R':[-math.degrees(math.asin(50/208)),0,math.degrees(math.asin(90/208))], 'Elevator_L':[-math.degrees(math.asin(2.99/9.45)),0,math.degrees(math.asin(3.82/9.45))], 'Elevator_R':[-math.degrees(math.asin(2.99/9.45)),0,math.degrees(math.asin(3.82/9.45))], 'Rudder':[-math.degrees(math.asin(233/450)),0,math.degrees(math.asin(233/450))]}
 for r in rig:
    if r['node'] in expected_angles:assert np.allclose([v for p,v in r['map']],expected_angles[r['node']])
    if r['node'].startswith('Spoiler_'):assert r['kind']=='move' and r['map']==[[0.,0.],[1.,.170]]
 # Geometry, not just the maps: full positive input has the intended visible TE direction.
 def rotate(v,a,degrees):
    a=np.asarray(a);t=math.radians(degrees);return v*math.cos(t)+np.cross(a,v)*math.sin(t)+a*np.dot(a,v)*(1-math.cos(t))
 for name,probe,component,sign in [('Aileron_L',[1.091333333,.12+5.25*math.tan(math.radians(2.5)),5.25],1,-1),('Aileron_R',[1.091333333,.12+5.25*math.tan(math.radians(2.5)),-5.25],1,1),('Elevator_L',[5.60,.83,.8],1,1),('Rudder',[5.68,.10,0],2,-1)]:
    r=next(r for r in rig if r['node']==name);v=np.array(probe)-r['pivot_gltf'];d=rotate(v,r['axis_gltf'],r['map'][-1][1])-v
    assert d[component]*sign>0,(name,d)
 # Cockpit: actual exported bezel radii and face-plane distances, all adjacent instruments.
 clearances={}
 for seat in ['F','R']:
    names=[n for n in parts if n.startswith('Bezel_') and n.endswith('_'+seat)]
    margin=[]
    for k,a in enumerate(names):
        ca=(parts[a].min(0)+parts[a].max(0))/2;ra=max(np.ptp(parts[a][:,1:],axis=0))/2
        for b in names[k+1:]:
            cb=(parts[b].min(0)+parts[b].max(0))/2;rb=max(np.ptp(parts[b][:,1:],axis=0))/2
            gap=np.linalg.norm(ca[1:]-cb[1:])-ra-rb;assert gap>0,(a,b,gap);margin.append(gap)
    clearances[seat]=min(margin)
    # Independent controls cannot overlap adjacent colored grips, measured AABBs.
    names=[f'{x}_{seat}' for x in ['DiveBrake_Lever','Trim_Lever','TowRelease','Canopy_Jettison']]
    for k,a in enumerate(names):
        for b in names[k+1:]:assert np.any(parts[a].max(0)<parts[b].min(0)) or np.any(parts[b].max(0)<parts[a].min(0)),(a,b)
 # Every independent cockpit-part pair is broad-phase checked; intended assemblies explicitly excluded.
 prefixes=('Seat_','Headrest_','Shoulder_Harness_','Lap_Harness_','Buckle_','Panel_','Bezel_','Dial_','Needle_','Compass_Card','Stick_','Pedal_','DiveBrake_Lever_','Trim_Lever_','TowRelease_','Canopy_Jettison_','Floor_','Cockpit_Bulkhead_','HandleMount_','TowCable_')
 cockpit=[n for n in tris if n.startswith(prefixes)]
 checked_pairs=0;overlaps=[];intentional=[]
 def family(n):
    for stem in ['Seat_Pan','Seat_Back','Headrest','Shoulder_Harness','Lap_Harness','Buckle','Panel','Bezel','Dial','Needle','Compass_Card','Stick_Grip','Stick','Pedal','DiveBrake_Lever','Trim_Lever','TowRelease','Canopy_Jettison','Floor']:
        if n.startswith(stem):return stem
 def intentional_contact(a,b):
    fa,fb=family(a),family(b)
    if a=='HandleMount_'+b or b=='HandleMount_'+a:return True
    if a.startswith('TowCable_') and b=='TowRelease_'+a[-1] or b.startswith('TowCable_') and a=='TowRelease_'+b[-1]:return True
    # Soft seat/harness/buckle interfaces and stick grip/spar, dial assembly layers.
    if fa in ['Seat_Pan','Seat_Back','Headrest','Shoulder_Harness','Lap_Harness','Buckle'] and fb in ['Seat_Pan','Seat_Back','Headrest','Shoulder_Harness','Lap_Harness','Buckle']:return True
    if {fa,fb}=={'Stick','Stick_Grip'}:return True
    return False
 def segment_hits_triangle(a,b,t):
    d=b-a;e1=t[:,1]-t[:,0];e2=t[:,2]-t[:,0];h=np.cross(d,e2);det=np.einsum('ij,ij->i',e1,h);ok=np.abs(det)>1e-10;den=np.where(ok,det,1);s=a-t[:,0];u=np.einsum('ij,ij->i',s,h)/den;q=np.cross(s,e1);v=q@d/den;dist=np.einsum('ij,ij->i',e2,q)/den
    return np.any(ok&(u>1e-6)&(v>1e-6)&(u+v<1-1e-6)&(dist>1e-6)&(dist<1-1e-6))
 for k,a in enumerate(cockpit):
    for b in cockpit[k+1:]:
        checked_pairs+=1
        if np.any(parts[a].max(0)<=parts[b].min(0)+1e-6) or np.any(parts[b].max(0)<=parts[a].min(0)+1e-6):continue
        if intentional_contact(a,b):intentional.append([a,b]);continue
        collision=False
        for ta,tb in [(tris[a],tris[b]),(tris[b],tris[a])]:
            for t in ta:
                if any(segment_hits_triangle(t[j],t[(j+1)%3],tb) for j in range(3)):collision=True;break
            if collision:break
        # Fully contained objects would escape edge crossings: independent AABBs must not contain.
        contained=np.all(parts[a].min(0)>parts[b].min(0)) and np.all(parts[a].max(0)<parts[b].max(0)) or np.all(parts[b].min(0)>parts[a].min(0)) and np.all(parts[b].max(0)<parts[a].max(0))
        if collision or contained:overlaps.append([a,b])
 assert not overlaps,('cockpit interpenetration',overlaps)
 # The cockpit's independent parts must also stay inside the exported exterior envelope.
 shell=np.concatenate([t for n,t in tris.items() if n=='Fuselage_Painted' or n.startswith('Canopy_Glass')])
 aa=shell[:,0,:2];uu=shell[:,1,:2]-aa;vv=shell[:,2,:2]-aa;dd=uu[:,0]*vv[:,1]-uu[:,1]*vv[:,0];nz=np.abs(dd)>1e-12;dd=np.where(nz,dd,1)
 protrusions=[];envelope_samples=0
 for name in cockpit:
    if name.startswith('HandleMount_'):continue  # Mechanical mounts deliberately meet/enter the shell.
    for point in np.unique(np.round(parts[name],6),axis=0):
        ww=point[:2]-aa;s=(ww[:,0]*vv[:,1]-ww[:,1]*vv[:,0])/dd;r=(uu[:,0]*ww[:,1]-uu[:,1]*ww[:,0])/dd
        ok=nz&(s>=-1e-6)&(r>=-1e-6)&(s+r<=1+1e-6)
        if not ok.any():protrusions.append((name,point.tolist(),'no envelope'));break
        zz=shell[:,0,2]+s*(shell[:,1,2]-shell[:,0,2])+r*(shell[:,2,2]-shell[:,0,2]);envelope_samples+=1
        if point[2]<zz[ok].min()-.002 or point[2]>zz[ok].max()+.002:
            protrusions.append((name,point.tolist(),[float(zz[ok].min()),float(zz[ok].max())]));break
 assert not protrusions,('cockpit outside shell',protrusions)
 # Head ellipsoid 200 x 210 x 184 mm, centre 65mm aft / 50mm above eye.
 canopytris=np.concatenate([t for n,t in tris.items() if n.startswith('Canopy_Glass')])
 def roof(x,z):
    t=canopytris;a=t[:,0][:,[0,2]];b=t[:,1][:,[0,2]];c=t[:,2][:,[0,2]];q=np.array([x,z]);u=b-a;v=c-a;w=q-a;det=u[:,0]*v[:,1]-u[:,1]*v[:,0]
    ok=abs(det)>1e-12;det=np.where(ok,det,1);s=(w[:,0]*v[:,1]-w[:,1]*v[:,0])/det;r=(u[:,0]*w[:,1]-u[:,1]*w[:,0])/det
    ok&=(s>=-1e-7)&(r>=-1e-7)&(s+r<=1+1e-7)
    assert ok.any(),('head outside canopy',x,z)
    return np.max((t[:,0,1]+s*(t[:,1,1]-t[:,0,1])+r*(t[:,2,1]-t[:,0,1]))[ok])
 head={}
 for seat,expected in [('front',[-1.15,.18,0]),('rear',[.04,.26,0])]:
    i=next(i for i,n in enumerate(nodes) if n['name']=='Pilot_Eye_'+seat);eye=world[i][:3,3];assert np.allclose(eye,expected)
    gaps=[]
    for theta in np.linspace(0,2*math.pi,48):
        for phi in np.linspace(0,math.pi,20):
            p=eye+[.065+.100*math.sin(phi)*math.cos(theta),.050+.092*math.cos(phi),.105*math.sin(phi)*math.sin(theta)]
            gaps.append(roof(p[0],p[2])-p[1])
    head[seat]=min(gaps);assert head[seat]>=.066,head
 for n in nodes:
    if n['name'].startswith('Canopy_Glass'):
        for p in g.doc['meshes'][n['mesh']]['primitives']:assert g.doc['materials'][p['material']]['alphaMode']=='BLEND'
 return dict(file=path.name,dimensions_m=dict(span=float(size[2]),length=float(size[0]),height=float(size[1])),nodes=len(nodes),rig_nodes=len(rig),head_clearance_m=head,head_fit='PASS geometric clearance; proposed eyes require Ty review',instrument_gap_m=clearances,graph='PASS',hinge_origins='PASS',canopy_alpha='PASS',cockpit_adjacent_instruments_and_colored_controls='PASS',whole_cockpit_collision_audit=dict(independent_pairs=checked_pairs,unintended_intersections=overlaps,intentional_assembly_contacts=intentional,scope='all independent cockpit mesh pairs; shell/liner contacts and coplanar touching excluded'),cockpit_envelope_samples=envelope_samples,cockpit_outside_shell=protrusions,visual_approval='PENDING')
if __name__=='__main__':
 results=[check(OUT/(ID+'-own'+s+'.glb')) for s in ['', '-tf']]
 a,b=[Glb(OUT/(ID+'-own'+s+'.glb')) for s in ['', '-tf']]
 for na,nb in zip(a.doc['nodes'],b.doc['nodes']):
    assert na['name']==nb['name'] and na.get('translation')==nb.get('translation')
    if 'mesh' in na:
        for pa,pb in zip(a.doc['meshes'][na['mesh']]['primitives'],b.doc['meshes'][nb['mesh']]['primitives']):assert np.array_equal(a.array(pa['attributes']['POSITION']),b.array(pb['attributes']['POSITION']))
 for r in results:r['paired_geometry']='IDENTICAL'

 p=ROOT/'renders/grob103-own/validation.json';p.parent.mkdir(exist_ok=True,parents=True);p.write_text(json.dumps(results,indent=2)+'\n');print(json.dumps(results,indent=2))
