"""1105 model contract: measured exported geometry, independent literal dimensions."""
import json
import numpy as np
from pitts_cockpit_v2 import Upgrade,MODEL
from vr_cockpit import Glb,builder
from check_vr_phase1 import triangles,point_tri
from drop_card_slot import validate


def check(path):
    g=Glb(path);u=Upgrade(g);validate(g,path)
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
    for name in ['Canopy_Frame','Canopy_TopTube_F','Canopy_TopTube_R']:
        assert u.node(name)['extras']['tubeDiameterM']==.009525
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
    return {'file':path.name,'card':'both centered, 108 x 140 mm, clear of gauges','stop_gap_mm':round(gap*1000,5),'trim_length_in':16,'windshield':'fixed; seam at -0.470 m','holes':'entry cutout + through-slot measured','finite_vertices':True}


if __name__=='__main__':
    for path in sorted(MODEL.glob('*-own*.glb')):print(json.dumps(check(path)))
