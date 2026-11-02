"""Phase 2 graph/geometry evidence; Python with NumPy, no Blender startup required."""
import json,sys,math
import numpy as np
from vr_cockpit import Glb,MODELS,ROOT,builder
from vr_phase2 import bow_points,BOW_X,BOW_RADIUS
from check_vr_phase1 import triangles,point_tri
from drop_card_slot import validate

def graph(g,path):
    validate(g,path);seen=set();active=set()
    def visit(i):
        assert i not in active,'cycle';assert i not in seen,'duplicate traversal'
        active.add(i)
        for j in g.doc['nodes'][i].get('children',[]):visit(j)
        active.remove(i);seen.add(i)
    for i in g.doc['scenes'][g.doc.get('scene',0)]['nodes']:visit(i)
    assert len(seen)==len(g.doc['nodes']),'unreachable nodes'
    assert len({n['name'] for n in g.doc['nodes']})==len(g.doc['nodes'])

def main():
    report={};counts={}
    for kind in ('pitts-s2b-like','extra-300-like'):
        for path in sorted((MODELS/kind).glob('*own*.glb')):
            g=Glb(path);graph(g,path);assert g.doc['extras']['vrCockpitPhase2']
            names={n['name'] for n in g.doc['nodes']}
            for seat in ('Rear','Front'):
                for prefix in ('Harness_','Harness_Buckle_','Grip_Harness_Release_','Headset_Jack_'):assert prefix+seat in names
                assert any(n.get('extras',{}).get('rig_param')=='harness_latch' for n in g.doc['nodes'] if n['name'].startswith('Harness_Strap_') and n['name'].endswith(seat))
            counts[path.name]=sum(g.doc['accessors'][p['indices']]['count']//3 if 'indices' in p else g.doc['accessors'][p['attributes']['POSITION']]['count']//3 for n in g.doc['nodes'] if 'mesh' in n for p in g.doc['meshes'][n['mesh']]['primitives'])
    g=Glb(MODELS/'pitts-s2b-like/pitts-s2b-like-own.glb')
    # Dense centreline samples, <= 1 mm step; subtract radius + 0.5 mm sampling bound.
    pts=[]
    for x in BOW_X:
        b=bow_points(x)
        for u,v in zip(b,b[1:]):pts.extend(np.linspace(u,v,int(np.ceil(np.linalg.norm(v-u)/.001))+1))
    pts=np.array(pts);clear={}
    for name,x in [('Canopy_Bow_F',-.835),('Canopy_Bow_R',-.875)]:
        actual=triangles(g,name).reshape(-1,3)
        assert abs((actual[:,0].min()+actual[:,0].max())/2-x)<1e-6
        # Ensure the PUBLISHED surface lies inside the centreline tube being tested.
        for p in actual:
            assert np.linalg.norm(pts-p,axis=1).min()<=.00685,'published bow differs from source'
    pin=triangles(g,'Canopy_Latch_Pin').reshape(-1,3)
    assert abs(np.ptp(pin[:,2])-.035)<1e-6,'pin is not 35 mm'
    assert abs(pin[:,2].min()-.183)<1e-6
    assert abs(pin[:,2].max()-.218)<1e-6
    for label,names in [('panel',['RearPanel_Rounded']),('instruments',[n['name'] for n in g.doc['nodes'] if n['name'].startswith('RearPanel_') and n['name']!='RearPanel_Rounded']),('IAC_card',[n['name'] for n in g.doc['nodes'] if n['name'].startswith('SequenceCard_') and 'mesh' in n])]:
        ts=np.concatenate([triangles(g,n) for n in names]);clear[label]=1000*(min(point_tri(p,ts) for p in pts)-BOW_RADIUS-.0005)
    # 280 mm diameter conservative head/helmet sphere at authored eye (no head mesh).
    clear['head_helmet_sphere']=1000*(np.linalg.norm(pts-(-1.325,0,.30),axis=1).min()-.14-BOW_RADIUS-.0005)
    # Both sticks, every extreme and intermediate control: builder axes match rig.
    best=1e9
    for name,x in [('Stick_Rear',-.93),('Stick_Front',-.17)]:
        local=triangles(g,name).reshape(-1,3)-np.array((x,0,-.58))
        angular_bound=4*np.linalg.norm(local,axis=1).max()*math.sin(math.radians(.25))
        for pitch in np.linspace(-12,18,31):
            t=math.radians(-pitch);ry=np.array([[math.cos(t),0,math.sin(t)],[0,1,0],[-math.sin(t),0,math.cos(t)]])
            for roll in np.linspace(-18,18,37):
                t=math.radians(roll);rx=np.array([[1,0,0],[0,math.cos(t),-math.sin(t)],[0,math.sin(t),math.cos(t)]])
                sp=local@(rx@ry).T+np.array((x,0,-.58))
                # Bounding-box distance is conservative for every point in the stick capsule.
                low=sp.min(0)-angular_bound;high=sp.max(0)+angular_bound
                d=np.linalg.norm(np.maximum(np.maximum(low-pts,pts-high),0),axis=1).min()-BOW_RADIUS-.0005
                best=min(best,d)
    clear['both_sticks_full_control_envelope']=best*1000
    # Rotation about X cannot change X; slide is monotonically aft. Thus this
    # separating-axis bound holds for EVERY t in [0,1], not merely sweep samples.
    panel=triangles(g,'RearPanel_Rounded');v=np.concatenate([triangles(g,n).reshape(-1,3) for n in ('Canopy_Bow_F','Canopy_Bow_R')])
    sweep=(panel[:,:,0].min()-v[:,0].max())*1000
    assert sweep>0
    report['bows']=dict(x_m=BOW_X,tube_radius_m=BOW_RADIUS,crown_m=.218,pin_length_m=.035,closed_conservative_clearance_mm=clear,open_sweep_panel_separating_axis_min_mm=sweep,sweep='continuous proof: X-axis hinge preserves X; aft slide only increases separation; bows only, intentional pin/channel engagement excluded')
    assert min(clear.values())>0,clear
    # Sights are specified floating visual references INSIDE wingtip, not structural wingtip hardware.
    sv=np.concatenate([triangles(g,n['name']).reshape(-1,3) for n in g.doc['nodes'] if n['name'].startswith('Sight_')]);wv=np.concatenate([triangles(g,n['name']).reshape(-1,3) for n in g.doc['nodes'] if 'Wing' in n['name'] and 'mesh' in n])
    report['sights']=dict(abs_y_centre_m=float((abs(sv[:,1]).min()+abs(sv[:,1]).max())/2),wingtip_abs_y_m=float(abs(wv[:,1]).max()),finding='Floating cockpit-only bars per sights-PROVENANCE; external star is projection artefact. Do not relocate to wingtip.')
    path=MODELS/'pilot-gear/pilot-gear-own.glb';gear=Glb(path);graph(gear,path)
    names={n['name'] for n in gear.doc['nodes']}
    assert {'Headset','Headset_Cable_Root','Headset_Plug','Grip_Headset_Plug','Parachute_Container','Parachute_Harness','Ripcord_Handle','Grip_Ripcord','Ripcord_Cable','Parachute_Deployed','Parachute_Pilotchute','Parachute_Lines','Parachute_Risers','Parachute_Canopy'}<=names
    canopy=next(n for n in gear.doc['nodes'] if n['name']=='Parachute_Canopy');mesh=gear.doc['meshes'][canopy['mesh']];assert mesh['extras']['targetNames']==['Inflate'];assert len(mesh['primitives'][0]['targets'])==1
    for n in gear.doc['nodes']:
        if 'skin' in n:
            p=gear.doc['meshes'][n['mesh']]['primitives'][0];assert np.allclose(gear.array(p['attributes']['WEIGHTS_0']).sum(1),1)
    report['gear']=gear.doc['extras'];report['airframe_triangles']=counts
    print(json.dumps(report,indent=2))
    if '--write-report' in sys.argv:(ROOT/'docs/models/vr-cockpit-phase2-checks.json').write_text(json.dumps(report,indent=2)+'\n')
if __name__=='__main__':main()
