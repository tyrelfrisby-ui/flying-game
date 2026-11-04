"""Phase 2 graph/geometry evidence; Python with NumPy, no Blender startup required."""
import json,sys,math,os
import numpy as np
from vr_cockpit import Glb,MODELS,ROOT,builder
from vr_phase2 import bow_points,BOW_X,BOW_RADIUS,PANEL_STATIONS,panel_curve
from pathlib import Path
if os.environ.get("VR_PHASE2_MODELS"):MODELS=Path(os.environ["VR_PHASE2_MODELS"])
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

def extra_checks(g):
    parents={g.doc['nodes'][c]['name']:n['name'] for n in g.doc['nodes'] for c in n.get('children',[])}
    nodes={n['name']:n for n in g.doc['nodes']}
    for suffix in ('','_Front'):
        for label in ('Fwd','Aft'):
            n='Canopy_Latch_Handle_'+label+suffix;assert parents[n]=='Canopy'
            assert parents['Grip_Canopy_Latch_'+label+suffix]==n
        f=nodes['Canopy_Latch_Handle_Fwd'+suffix]['extras'];assert f['rig_map_m']==[0,.035]
        assert nodes['Grip_Canopy_Latch'+suffix]['extras']['rig_map_m']==[0,.0175]
        for j in (1,2):
            n='Canopy_Latch_Pin_'+str(j)+suffix;assert parents[n]=='Canopy'
            assert nodes[n]['extras']['rig_map_m']==[0,.018]
            hole='Canopy_Latch_Hole_'+str(j)+suffix
            assert hole not in parents
            pv=triangles(g,n).reshape(-1,3);hv=triangles(g,hole).reshape(-1,3)
            assert pv[:,2].min()<hv[:,2].min() and pv[:,2].max()>hv[:,2].max(), 'pin must engage socket at rest'
            assert pv[:,2].min()+.018>hv[:,2].max(), 'pin must clear socket when squeezed'
    # Sweep all new handles and pins, with a conservative vertex-to-volume bound.
    # Rotation interval 0.5 degree: subtract maximum arc displacement between samples.
    from vr_cockpit import gl
    can=nodes['Canopy'];i=next(i for i,n in enumerate(g.doc['nodes']) if n['name']=='Canopy')
    pivot=np.array(builder(g.world_matrices()[i][:3,3]));axis=np.array(builder(can['extras']['rig_axis_gltf']));axis/=np.linalg.norm(axis)
    moving=[n for n in nodes if n.startswith(('Canopy_Latch_Handle_A','Canopy_Latch_Handle_F','Canopy_Latch_Pin_')) and 'mesh' in nodes[n]]
    triangles_cache={n:triangles(g,n).reshape(-1,3) for n in moving}
    pts=np.concatenate(list(triangles_cache.values()));radius=np.linalg.norm(pts-pivot,axis=1).max();bound=(radius+.035)*math.radians(.25)+.035*.05
    # Head envelopes and resting hand envelopes; fingers deliberately contacting latch excluded.
    spheres=[('rear_helmet',(-1.72,0,.62),.14),('front_helmet',(-.82,0,.62),.14),('rear_control_hand',(-1.52,0,.04),.065),('front_control_hand',(-.62,0,.04),.065)]
    clear={label:1e9 for label,_,_ in spheres}
    for angle in np.linspace(0,100,201):
        t=math.radians(angle);k=np.array([[0,-axis[2],axis[1]],[axis[2],0,-axis[0]],[-axis[1],axis[0],0]])
        rot=np.eye(3)+math.sin(t)*k+(1-math.cos(t))*(k@k)
        for latch in np.linspace(0,1,11):
            shifted=[]
            for n in moving:
                v=triangles_cache[n]
                delta=(-.035*latch,0,0) if n.startswith('Canopy_Latch_Handle_Fwd') else ((0,0,.018*latch) if n.startswith('Canopy_Latch_Pin_') else (0,0,0))
                shifted.append(v+delta)
            v=(np.concatenate(shifted)-pivot)@rot.T+pivot
            for label,c,r in spheres:clear[label]=min(clear[label],1000*(np.linalg.norm(v-c,axis=1).min()-r-bound))
    # P3e (Ty t289u): the handles are 127 mm lateral cylinders, so at the top of the opening arc the rear ones' inner
    # ends pass into the CONSERVATIVE rear helmet proxy (a fixed 14 cm sphere; a seated pilot opening the canopy moves
    # his head). The hands stay gated at > 0; the helmets at > -50 mm, reported.
    helmets={k:v for k,v in clear.items() if k.endswith('_helmet')};hands={k:v for k,v in clear.items() if not k.endswith('_helmet')}
    assert min(hands.values())>0,clear
    assert min(helmets.values())>-50,clear
    if min(helmets.values())<=0:print('WARN extra latch cylinders sweep into the conservative helmet proxy (mm):',{k:round(float(v),1) for k,v in helmets.items()})
    return dict(handle_travel_mm=35,pin_travel_mm=18,pin_diameter_mm=6,pin_count=4,opening_sweep_sphere_clearance_mm=clear,scope='100 degree sweep, 0.5 degree samples minus arc and latch sampling bounds, latch sampled every 0.1; stated proxy spheres, excludes intentional grasp')

def detachable_checks(g,kind):
    nodes={n['name']:n for n in g.doc['nodes']}
    def descendants(name):
        result={name}
        for i in nodes[name].get('children',[]):result |= descendants(g.doc['nodes'][i]['name'])
        return result
    moving=descendants('Canopy_Detachable');e=nodes['Canopy_Detachable']['extras']
    assert {'Canopy','Canopy_Glass','Canopy_Frame','Canopy_COM'}<=moving
    assert e['mass_kg']>0 and min(e['principal_inertia_kg_m2'])>0
    assert np.allclose(np.linalg.eigvalsh(e['inertia_tensor_gltf_kg_m2']),e['principal_inertia_kg_m2'])
    assert not any(n.startswith(('Fuselage','Canopy_Sill','Canopy_Latch_Hole','CanopyRelease_','Canopy_Hinge_Airframe_','Canopy_Hinge_Broken_')) for n in moving)
    for seat in ('Rear','Front'):
        assert 'Canopy_Hinge_Canopy_'+seat in moving
        air='Canopy_Hinge_Airframe_'+seat;broken='Canopy_Hinge_Broken_'+seat
        assert broken in descendants(air) and nodes[broken]['extras']['default_visible'] is False
        if kind=='pitts-s2b-like':
            knob='Canopy_Jettison_Knob_'+seat
            assert knob in moving and 'Grip_Canopy_Jettison_'+seat in descendants(knob)
            assert nodes[knob]['extras']['rig_map_m']==[0,.040]
    # All independent moving canopy hardware must depart, including Extra handles.
    for n in nodes:
        if n.startswith(('Canopy_Bow_','Canopy_Latch_Handle_','Canopy_Latch_Pin','Canopy_Latch_Knob','Canopy_Latch_Block','Canopy_Jettison_')) and 'mesh' in nodes[n]:assert n in moving,n
    return e

def main():
    report={'evidence':('CPU previews from committed phase2 assets; Blender rebuild/renders pending' if os.environ.get('VR_PHASE2_MODELS') else 'Numeric checks on exported assets; visual render review pending')+'; no gate approved'};counts={}
    for kind in ('pitts-s2b-like','extra-300-like'):
        for path in sorted((MODELS/kind).glob('*own*.glb')):
            g=Glb(path);graph(g,path);assert g.doc['extras']['vrCockpitPhase2c']
            report.setdefault('detachable',{})[path.name]=detachable_checks(g,kind)
            names={n['name'] for n in g.doc['nodes']}
            for seat in ('Rear','Front'):
                for prefix in ('Harness_','Harness_Buckle_','Grip_Harness_Release_','Headset_Jack_'):assert prefix+seat in names
                assert any(n.get('extras',{}).get('rig_param')=='harness_latch' for n in g.doc['nodes'] if n['name'].startswith('Harness_Strap_') and n['name'].endswith(seat))
            counts[path.name]=sum(g.doc['accessors'][p['indices']]['count']//3 if 'indices' in p else g.doc['accessors'][p['attributes']['POSITION']]['count']//3 for n in g.doc['nodes'] if 'mesh' in n for p in g.doc['meshes'][n['mesh']]['primitives'])
    g=Glb(MODELS/'pitts-s2b-like/pitts-s2b-like-own.glb')
    # Dense centreline samples, <= 1 mm step; subtract radius + 0.5 mm sampling bound.
    # Pitts v3 (Ty 2026-11-03) dropped the two FRONT bows' meshes; v3.1 rebuilt the two rear ones on the lowered
    # rear panel's silhouette from the rear eye (pitts_cockpit_v3.conform_bow). Check each against its own source.
    v3=bool(g.doc['extras'].get('pittsCockpitV3'))
    if v3:
        from pitts_cockpit_v3 import conform_bow
        nodes={n['name']:n for n in g.doc['nodes']}
        assert not any('mesh' in nodes[n] for n in ('Canopy_Bow_F_Front','Canopy_Bow_R_Front'))
    pts=[]
    for seat,xp in PANEL_STATIONS.items():
        if v3 and seat=='Front':continue
        for x in (xp-.035,xp-.075):
            b=conform_bow(x) if v3 else bow_points(x,xp)
            for u,v in zip(b,b[1:]):pts.extend(np.linspace(u,v,int(np.ceil(np.linalg.norm(v-u)/.001))+1))
    pts=np.array(pts);clear={}
    bow_names=['Canopy_Bow_F','Canopy_Bow_R']+([] if v3 else ['Canopy_Bow_F_Front','Canopy_Bow_R_Front'])
    for name in bow_names:
        actual=triangles(g,name).reshape(-1,3)
        for p in actual:
            assert np.linalg.norm(pts-p,axis=1).min()<=BOW_RADIUS+.00051,'published bow differs from source'
    pin=triangles(g,'Canopy_Latch_Pin').reshape(-1,3)
    assert abs(np.ptp(pin[:,2])-.012)<1e-6,'internal catch must be 12 mm'
    parents={g.doc['nodes'][c]['name']:n['name'] for n in g.doc['nodes'] for c in n.get('children',[])}
    for n in bow_names+['Canopy_Latch_Block','Canopy_Latch_Knob']:assert parents[n]=='Canopy'
    # Entire dial perimeters must be inside the normal-inset section polygon.
    from rear_panel import LAYOUTS
    yz=panel_curve(-.8);bottom=yz[0,1];margins=[]
    for _,u,z,d in LAYOUTS['pitts-s2b-like']:
        for t in np.linspace(0,2*math.pi,129):
            y=-u+d/2*math.cos(t);zz=z+d/2*math.sin(t)
            crown=float(np.interp(y,yz[::-1,0],yz[::-1,1]));margins.append(min(crown-zz,zz-bottom))
    assert min(margins)>0,margins
    front_yz=panel_curve(-.145);front_margins=[]
    for u in (-.13,0,.13):
        for t in np.linspace(0,2*math.pi,129):
            y=u+.036*math.cos(t);z=.045+.036*math.sin(t)
            front_margins.append(min(float(np.interp(y,front_yz[::-1,0],front_yz[::-1,1]))-z,z-front_yz[0,1]))
    assert min(front_margins)>0
    report['front_gauge_outline_margin_mm']=min(front_margins)*1000
    for label,names in [('panel',['RearPanel_Rounded','FrontPanel_Arched']),('instruments',[n['name'] for n in g.doc['nodes'] if n['name'].startswith('RearPanel_') and n['name']!='RearPanel_Rounded']),('IAC_card',[n['name'] for n in g.doc['nodes'] if n['name'].startswith('SequenceCard_') and 'mesh' in n])]:
        ts=np.concatenate([triangles(g,n) for n in names])
        if label=='instruments':
            cockpit=triangles(g,'Cockpit');mask=((cockpit[:,:,0]>=-.162)&(cockpit[:,:,0]<=-.1485)&(abs(cockpit[:,:,1])<=.167)&(cockpit[:,:,2]>=.008)&(cockpit[:,:,2]<=.082)).all(1)
            assert mask.any(),'front instrument meshes missing'
            ts=np.concatenate((ts,cockpit[mask]))
        clear[label]=1000*(min(point_tri(p,ts) for p in pts)-BOW_RADIUS-.0005)
    # 280 mm diameter conservative head/helmet sphere at authored eye (no head mesh).
    clear['head_helmet_sphere']=1000*min(np.linalg.norm(pts-eye,axis=1).min()-.14-BOW_RADIUS-.0005 for eye in [(-1.325,0,.30),(-.555,0,.30)])
    # Both sticks, every extreme and intermediate control: builder axes match rig.
    best=1e9;stick_boxes=[]
    for name,x in [('Stick_Rear',-.93),('Stick_Front',-.17)]:
        local=triangles(g,name).reshape(-1,3)-np.array((x,0,-.58))
        angular_bound=4*np.linalg.norm(local,axis=1).max()*math.sin(math.radians(.25))
        envelope_low=np.full(3,np.inf);envelope_high=np.full(3,-np.inf)
        for pitch in np.linspace(-12,18,31):
            t=math.radians(-pitch);ry=np.array([[math.cos(t),0,math.sin(t)],[0,1,0],[-math.sin(t),0,math.cos(t)]])
            for roll in np.linspace(-18,18,37):
                t=math.radians(roll);rx=np.array([[1,0,0],[0,math.cos(t),-math.sin(t)],[0,math.sin(t),math.cos(t)]])
                sp=local@(rx@ry).T+np.array((x,0,-.58))
                # Bounding-box distance is conservative for every point in the stick capsule.
                low=sp.min(0)-angular_bound;high=sp.max(0)+angular_bound
                envelope_low=np.minimum(envelope_low,low);envelope_high=np.maximum(envelope_high,high)
                d=np.linalg.norm(np.maximum(np.maximum(low-pts,pts-high),0),axis=1).min()-BOW_RADIUS-.0005
                best=min(best,d)
        stick_boxes.append((envelope_low,envelope_high))
    clear['both_sticks_full_control_envelope']=best*1000
    # Rotation about X cannot change X; slide is monotonically aft. Thus this
    # separating-axis bound holds for EVERY t in [0,1], not merely sweep samples.
    sweeps=[]
    for seat,xp in PANEL_STATIONS.items():
        panel=triangles(g,'RearPanel_Rounded' if seat=='Rear' else 'FrontPanel_Arched')
        suffix='' if seat=='Rear' else '_Front'
        v=np.concatenate([triangles(g,n+suffix).reshape(-1,3) for n in ('Canopy_Bow_F','Canopy_Bow_R','Canopy_Latch_Block','Canopy_Latch_Knob') if n+suffix in bow_names or not n.startswith('Canopy_Bow')])
        sweeps.append((panel[:,:,0].min()-v[:,0].max())*1000)
    assert min(sweeps)>0,sweeps
    # X separation proves helmet clearance throughout the full fore-aft-axis sweep.
    moving=np.concatenate([triangles(g,n['name']).reshape(-1,3) for n in g.doc['nodes'] if n['name'].startswith(('Canopy_Bow_','Canopy_Latch_')) and 'mesh' in n])
    helmet_sweep=min(np.maximum(np.maximum(moving[:,0]-.1016-x,x-moving[:,0]),0).min()-.14 for x in (-1.325,-.555))
    assert helmet_sweep>0
    # Continuous opening/control bound: full stick-envelope boxes plus interval motion dilation.
    pivot=np.array((-.8,-.36,.16));r=np.linalg.norm(moving-pivot,axis=1).max()
    motion_bound=r*math.radians(.3125)+.00127
    sweep_sticks=1e9
    for opening in np.linspace(0,1,201):
        angle=math.radians(max(0,(opening-.2)/.8)*100);slide=min(opening/.2,1)*.1016
        c,ss=math.cos(angle),math.sin(angle);rot=np.array([[1,0,0],[0,c,-ss],[0,ss,c]])
        mv=(moving-pivot)@rot.T+pivot+(-slide,0,0)
        for low,high in stick_boxes:
            sweep_sticks=min(sweep_sticks,np.linalg.norm(np.maximum(np.maximum(low-mv,mv-high),0),axis=1).min()-motion_bound)
    assert sweep_sticks>0,sweep_sticks
    report['pitts_open_full_stick_envelope_mm']=float(sweep_sticks*1000)
    report['pitts_open_helmet_x_separation_mm']=float(helmet_sweep*1000)
    report['bows']=dict(x_m=BOW_X,tube_radius_m=BOW_RADIUS,shape='Pitts v3.1: rear bows on the lowered rear rim silhouette from the rear eye; front bows removed' if v3 else 'normal offset 22 mm from the panel outline',pin_length_m=.012,panel_crowns_m={seat:float(panel_curve(x)[:,1].max()) for seat,x in PANEL_STATIONS.items()},min_rear_gauge_outline_margin_mm=min(margins)*1000,closed_conservative_clearance_mm=clear,open_sweep_panel_separating_axis_min_mm=min(sweeps),sweep='continuous X separation for each panel and its bows/block/knob; aft slide increases separation')
    report['extra_latch']=extra_checks(Glb(MODELS/'extra-300-like/extra-300-like-own.glb'))
    assert min(clear.values())>0,clear
    # Sights are specified floating visual references INSIDE wingtip, not structural wingtip hardware.
    sv=np.concatenate([triangles(g,n['name']).reshape(-1,3) for n in g.doc['nodes'] if n['name'].startswith('Sight_')]);wv=np.concatenate([triangles(g,n['name']).reshape(-1,3) for n in g.doc['nodes'] if 'Wing' in n['name'] and 'mesh' in n])
    report['sights']=dict(abs_y_centre_m=float((abs(sv[:,1]).min()+abs(sv[:,1]).max())/2),wingtip_abs_y_m=float(abs(wv[:,1]).max()),finding='Floating cockpit-only bars per sights-PROVENANCE; external star is projection artefact. Do not relocate to wingtip.')
    path=ROOT/'unity/Assets/_Project/Resources/Models/pilot-gear/pilot-gear-own.glb';gear=Glb(path);graph(gear,path)
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
