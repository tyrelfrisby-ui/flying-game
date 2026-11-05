"""1105 model contract: measured exported geometry, independent literal dimensions."""
import json
import numpy as np
from pitts_cockpit_v2 import Upgrade,MODEL
from vr_cockpit import Glb,builder
from check_vr_phase1 import triangles,point_tri
from drop_card_slot import validate


def throttle_clearances(g):
    """Enclose exported parts in capsules; bound space/time sampling error.

    Centreline spacing <=2 mm and angular spacing 1 degree. Subtract 1 mm
    plus the maximum half-step arc displacement, so this bounds the full sweep.
    """
    u=Upgrade(g);result={}
    obstacles={'wall':np.concatenate([triangles(g,n) for n in ['Fuselage_Painted','Cockpit']]),
               'prop':np.concatenate([triangles(g,n) for n in ['PropLever_Rear','PropLever_Rear_Slide']]),
               'trim':triangles(g,'TrimLever_Rear')}
    for seat,x in [('Rear',-1.145),('Front',-.425)]:
        name='Throttle_'+seat;pivot=np.array((x,.238,-.290))
        assert np.allclose(builder(u.world(name)[:3,3]),pivot,atol=1e-7)
        assert np.allclose(builder(u.world('Grip_Throttle_'+seat)[:3,3]),[x,.221,-.130],atol=1e-7)
        assert u.names['Grip_Throttle_'+seat] in u.node(name)['children']
        capsules=[(np.array((x,.238,-.290)),np.array((x,.238,-.130)),.009),
                  (np.array((x,.260,-.130)),np.array((x,.182,-.130)),.021),
                  (np.array((x,.182,-.130)),np.array((x,.178,-.130)),.008)]
        primitives=g.doc['meshes'][u.node(name)['mesh']]['primitives']
        assert len(primitives)==3
        arm=u.vertices(name)
        assert abs(arm[:,2].min()+.290)<1e-7
        # Every exported vertex must be contained by its primitive's capsule.
        for pr,(a,b,r) in zip(primitives,capsules):
            v=g.array(pr['attributes']['POSITION']);w=u.world(name)
            v=np.array([builder(q) for q in v@w[:3,:3].T+w[:3,3]])
            d=b-a;t=np.clip((v-a)@d/(d@d),0,1)
            assert np.linalg.norm(v-a-t[:,None]*d,axis=1).max()<=r+1e-7
        gaps={k:float('inf') for k in obstacles}
        for a,b,r in capsules:
            samples=np.linspace(a,b,int(np.ceil(np.linalg.norm(b-a)/.002))+1)
            reach=max(np.linalg.norm(a-pivot),np.linalg.norm(b-pivot))
            error=.001+reach*np.deg2rad(.5)
            for angle in np.deg2rad(np.linspace(-25,25,51)):
                c,t=np.cos(angle),np.sin(angle)
                rot=np.array([[c,0,t],[0,1,0],[-t,0,c]])
                points=(samples-pivot)@rot.T+pivot
                for key,tris in obstacles.items():
                    # Distant triangles cannot violate the required 10 mm gap.
                    lo=points.min(0)-r-error-.011;hi=points.max(0)+r+error+.011
                    near=tris[(tris.max(1)>=lo).all(1)&(tris.min(1)<=hi).all(1)]
                    gap=min(point_tri(q,near)-r-error for q in points) if len(near) else .011
                    gaps[key]=min(gaps[key],gap)
        assert min(gaps.values())>=.010,(name,gaps)
        result[seat]={k:round(v*1000,3) for k,v in gaps.items()}
    return result


def check(path):
    g=Glb(path);u=Upgrade(g);validate(g,path)
    throttle_gaps=throttle_clearances(g)
    assert len(u.names)==len(u.nodes),'duplicate node names'
    for n in u.nodes:
        if 'mesh' not in n:continue
        for p in g.doc['meshes'][n['mesh']]['primitives']:
            assert np.isfinite(g.array(p['attributes']['POSITION'])).all(),n['name']
    parents={c:n['name'] for n in u.nodes for c in n.get('children',[])}
    assert u.names['Windshield_Fixed'] not in parents
    assert u.vertices('Windshield_Fixed')[:,0].min()>-.470001
    assert u.vertices('Canopy_Glass')[:,0].max()<-.469999
    assert parents[u.names['Canopy_Frame']]=='Canopy'
    assert u.node('Canopy_Frame')['extras']['tubeDiameterM']==.009525
    # Ty 2026-11-05 07:26: no grey hoops across the canopy.
    for name in ['Canopy_TopTube_F','Canopy_TopTube_R']:
        assert name not in u.names or 'mesh' not in u.node(name),name
    # Every descendant is measured in world space, including nested relics.
    pending=list(u.node('Canopy').get('children',[]))
    front_hoop=-.4652375  # front surface of the 3/8-inch hoop at builder X=-.470
    while pending:
        n=u.nodes[pending.pop()];pending.extend(n.get('children',[]))
        if 'mesh' in n:
            assert u.vertices(n['name'])[:,0].max()<=front_hoop+.030+1e-6,('ahead of front hoop',n['name'])
    for suffix in ['', '_Front']:
        lever='Canopy_Latch_Knob'+suffix;grip='Grip_Canopy_Latch'+suffix
        assert parents[u.names[grip]]==lever
        assert np.linalg.norm(u.world(grip)[:3,3]-u.world(lever)[:3,3])>=.040
    from pitts_cockpit_v2 import front_latch_clearance
    assert min(front_latch_clearance(g))>=.005
    assert 'mesh' not in u.node('Canopy_Jettison_Knob_Front')
    assert not any('mesh' in n and n['name'].startswith(('Harness_','Headset_')) for n in u.nodes)
    for prefix in ['', 'FrontPanel_']:
        v=u.vertices(prefix+'SequenceCard_Face');lo,hi=v.min(0),v.max(0)
        assert abs((lo[1]+hi[1])/2)<1e-6
        assert .1078<hi[1]-lo[1]<.1082 and .1395<hi[2]-lo[2]<.1401,(prefix,hi-lo)
        card=(lo[1:]-.003,hi[1:]+.003)
        for n in u.nodes:
            panelprefix='RearPanel_' if not prefix else 'FrontPanel_'
            if 'mesh' not in n or not n['name'].startswith(panelprefix) or not n['name'].endswith(('_Face','_Bezel')):continue
            if 'SequenceCard' in n['name']:continue
            a=u.vertices(n['name']);a0,a1=a[:,1:].min(0),a[:,1:].max(0)
            assert not np.all(np.minimum(a1,card[1])>np.maximum(a0,card[0])),('card overlap',n['name'])
        for part in ['Altimeter_Knob','Altimeter_Kollsman','G_meter_Reset']:assert 'mesh' in u.node(prefix+part)
    trim=u.vertices('TrimLever_Rear');pivot=builder(u.world('TrimLever_Rear')[:3,3])
    assert np.allclose(pivot,[-1.36,.27,-.26]);assert abs(trim[:,0].max()-pivot[0]-.4064)<.001
    assert np.ptp(trim[:,2])<.020,'trim neutral not straight forward'
    assert u.vertices('PropLever_Rear')[:,2].min()>u.vertices('Throttle_Rear')[:,2].max()+.08
    assert u.vertices('FuelValve_Firewall_Rod')[:,0].max()>.64
    assert u.vertices('PropLever_Rear_Slide')[:,0].max()>.64
    # Normal-travel transformed rail tangent must meet the rubber capsule to <0.02 mm.
    rail=np.array([-.47,.19865990391,.438348446068]);hinge=np.array([-.8,-.36,.16]);q=rail-hinge
    a=np.deg2rad(100);q=np.array([q[0]-.1016,q[1]*np.cos(a)-q[2]*np.sin(a),q[1]*np.sin(a)+q[2]*np.cos(a)])+hinge
    frame=triangles(g,'Canopy_Frame');rel=frame-hinge
    moving=rel.copy();moving[:,:,0]-=.1016
    moving[:,:,1]=rel[:,:,1]*np.cos(a)-rel[:,:,2]*np.sin(a)
    moving[:,:,2]=rel[:,:,1]*np.sin(a)+rel[:,:,2]*np.cos(a);moving+=hinge
    # The contact normal is perpendicular to the actual arcing rail, not vertical.
    pad=u.vertices('Canopy_Stop_Rubber');padcenter=(pad.min(0)+pad.max(0))/2
    direction=padcenter-q;direction/=np.linalg.norm(direction)
    physical=q+direction*.0047625
    railgap=point_tri(physical,moving);padgap=point_tri(physical,triangles(g,'Canopy_Stop_Rubber'))
    gap=max(railgap,padgap)
    assert gap<.00002,('surface contact gap',gap)
    assert -.78<q[1]<-.68 and .65<q[2]<.68
    # Real through-slot and entry cutout: no wing triangle covers these vertical rays.
    wing=triangles(g,'Wing_Upper');xy=wing[:,:,:2]
    for point in [(-.25,0),(-.115,0),(-.115,.065)]:
        d=np.array(point)-xy[:,0];b=xy[:,1]-xy[:,0];c=xy[:,2]-xy[:,0];det=b[:,0]*c[:,1]-b[:,1]*c[:,0]
        ok=abs(det)>1e-10;den=np.where(ok,det,1)
        s=(d[:,0]*c[:,1]-d[:,1]*c[:,0])/den;t=(b[:,0]*d[:,1]-b[:,1]*d[:,0])/den
        assert not (ok&(s>=0)&(t>=0)&(s+t<=1)).any(),('hole filled',point)
    bridge=(-.145,0);assert u.node('UpperWing_Handle_Slot_Walls')['extras']['slotLengthM']==.170
    tank=u.vertices('FuelTank_Barrel');assert tank[:,2].min()<-.40 and tank[:,0].min()>-.15
    mat=next(m for m in g.doc['materials'] if m['name']=='Pitts1105_Bare_Aluminum')
    assert mat['pbrMetallicRoughness']['metallicFactor']>.8
    return {'file':path.name,'throttle_clearance_lower_bounds_mm':throttle_gaps,'card':'both centered, 108 x 140 mm, clear of gauges','stop_gap_mm':round(gap*1000,5),'trim_length_in':16,'windshield':'fixed; seam at -0.470 m','holes':'entry cutout + through-slot measured','finite_vertices':True}


if __name__=='__main__':
    for path in sorted(MODEL.glob('*-own*.glb')):print(json.dumps(check(path)))
