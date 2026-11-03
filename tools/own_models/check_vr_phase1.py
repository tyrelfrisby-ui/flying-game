"""Numerical Phase 1 evidence. Does not substitute for seat-eye Blender renders.
python tools/own_models/check_vr_phase1.py [--write-report] [--v2]
Checks the shipped v3 body (Resources). --v2 checks the superseded v2 (PilotBodyCandidates/, rebuilt with
pilot_body.py --offline --v2) instead.
Capsule clearance is a conservative enclosing volume around each limb/finger,
sampled at <=3 mm. Report subtracts another 1.5 mm for sampling uncertainty.
"""
import sys,json
from pathlib import Path
import numpy as np
from vr_cockpit import Glb,ROOT,MODELS,builder
from pilot_body import definition,OUT,OUT_V2
from drop_card_slot import validate

def point_tri(p,t):
    a,b,c=t[:,0],t[:,1],t[:,2];ab=b-a;ac=c-a;n=np.cross(ab,ac);nn=np.sum(n*n,axis=1)
    signed=np.sum((p-a)*n,axis=1)/np.maximum(nn,1e-20);q=p-signed[:,None]*n
    v=q-a;aa=np.sum(ab*ab,1);bb=np.sum(ab*ac,1);cc=np.sum(ac*ac,1);d=np.sum(v*ab,1);e=np.sum(v*ac,1)
    den=aa*cc-bb*bb;u=(cc*d-bb*e)/np.maximum(den,1e-20);w=(aa*e-bb*d)/np.maximum(den,1e-20)
    inside=(u>=0)&(w>=0)&(u+w<=1)&(den>1e-18)
    distance=np.where(inside,abs(signed)*np.sqrt(nn),np.inf)
    for x,y in ((a,b),(b,c),(c,a)):
        delta=y-x;pr=np.clip(np.sum((p-x)*delta,1)/np.maximum(np.sum(delta*delta,1),1e-20),0,1)
        distance=np.minimum(distance,np.linalg.norm(p-x-pr[:,None]*delta,axis=1))
    return float(distance.min())

def triangles(g,n):
    worlds=g.world_matrices();i=next(i for i,x in enumerate(g.doc['nodes']) if x['name']==n);o=g.doc['nodes'][i];m=worlds[i];ts=[]
    for pr in g.doc['meshes'][o['mesh']]['primitives']:
        p=g.array(pr['attributes']['POSITION']);p=(m[:3,:3]@p.T).T+m[:3,3];p=np.array([builder(v) for v in p]);ids=g.array(pr['indices']).reshape(-1,3) if 'indices' in pr else np.arange(len(p)).reshape(-1,3);ts.extend(p[ids])
    return np.array(ts)

def edge_hits(a,b,t):
    e1=t[:,1]-t[:,0];e2=t[:,2]-t[:,0];d=b-a;h=np.cross(d,e2);det=(e1*h).sum(1)
    inv=np.divide(1,det,out=np.zeros_like(det),where=abs(det)>1e-10);v=a-t[:,0];u=(v*h).sum(1)*inv
    q=np.cross(v,e1);w=(d*q).sum(1)*inv;length=(e2*q).sum(1)*inv
    return bool(((abs(det)>1e-10)&(u>=0)&(w>=0)&(u+w<=1)&(length>1e-6)&(length<1-1e-6)).any())

def main():
    result={}
    for kind in ('pitts-s2b-like','extra-300-like'):
        for path in sorted((MODELS/kind).glob('*own*.glb')):
            g=Glb(path);validate(g,path)
            # Also reject cycles (the original graph checker catches multi-parent/orphan only).
            seen=set();active=set()
            def visit(i):
                assert i not in active,'cycle';active.add(i)
                for c in g.doc['nodes'][i].get('children',[]):visit(c)
                active.remove(i);seen.add(i)
            for i in g.doc['scenes'][0]['nodes']:visit(i)
            assert len(seen)==len(g.doc['nodes'])
            assert len({n['name'] for n in g.doc['nodes']})==len(g.doc['nodes'])
            result[path.name]=g.doc['extras']['vrCockpitPhase1']
    g=Glb(MODELS/'pitts-s2b-like/pitts-s2b-like-own.glb')
    obstacles={n:triangles(g,n) for n in ('Cockpit','Cockpit_Truss','RearPanel_Rounded')}
    # Material labels in the old cockpit are unreliable; geometry determines front seat region.
    old=obstacles['Cockpit'];obstacles['FrontSeat_region']=old[(old[:,:,0].min(1)>-.79)&(old[:,:,0].max(1)<-.25)&(abs(old[:,:,1]).max(1)<.245)&(old[:,:,2].max(1)<.0)]
    V3='--v2' not in sys.argv;BODY=OUT if V3 else OUT_V2
    bones,parts,segments,grips=definition(v3=V3);clearances={}
    for name,t in obstacles.items():
        best=(float('inf'),'')
        for limb,a,b,r in segments:
            # Finger contacts with controls are deliberate; obstacles contain fixed structure only.
            count=int(np.ceil(np.linalg.norm(b-a)/.003))+1
            distance=min(point_tri(p,t) for p in np.linspace(a,b,count))-r-.0015
            if distance<best[0]:best=(distance,limb)
        clearances[name]=dict(conservative_clearance_mm=best[0]*1000,limb=best[1])
    result['pitts_body_capsule_clearance']=clearances
    torso=next(sh for n,sh,mi in parts if n=='Spine');tv=np.array(torso.v)
    edges={tuple(sorted((f[i],f[(i+1)%3]))) for f in torso.t for i in range(3)}
    torso_report={}
    for name,t in obstacles.items():
        hits=sum(edge_hits(tv[a],tv[b],t) for a,b in edges)
        torso_report[name]=dict(mesh_vertex_clearance_mm=min(point_tri(v,t) for v in tv)*1000,intersecting_torso_edges=hits)
    result['pitts_torso_surface']=torso_report
    body=Glb(BODY);validate(body,BODY)
    assert len(body.doc['skins'][0]['joints'])==49
    assert body.doc['extras']['triangles']<15000
    for m in body.doc['meshes']:
        for p in m['primitives']:
            j=body.array(p['attributes']['JOINTS_0']);w=body.array(p['attributes']['WEIGHTS_0'])
            assert j.max()<49 and np.allclose(w.sum(1),1)
    if V3:
        names={n['name'] for n in body.doc['nodes']}
        assert {'L_SoleOnPedal','R_SoleOnPedal'}<=names,'v3 sole markers missing'
    result['pilot']=body.doc['extras']
    print(json.dumps(result,indent=1))
    if '--write-report' in sys.argv and V3:(ROOT/'docs/models/vr-cockpit-phase1-checks.json').write_text(json.dumps(result,indent=1)+'\n')
    # Leave failures explicit. Capsule contact with structure must be resolved, never gate-approved.
    return 0 if all(v['conservative_clearance_mm']>=0 for v in clearances.values()) and all(v['intersecting_torso_edges']==0 for v in torso_report.values()) else 1
if __name__=='__main__':sys.exit(main())
