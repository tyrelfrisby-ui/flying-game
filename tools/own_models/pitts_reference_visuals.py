"""Photo-led visual pass only. Keeps simulation, rig calibration and node indices intact.
Builder coordinates: X forward, Y pilot left, Z up. Call after pitts_panel_v4.
"""
import math, tempfile
from pathlib import Path
import numpy as np
from vr_cockpit import Glb, Shape, MODELS, ROOT, gl
from gl_quadrant_v3 import set_mesh, atlas, quad
from drop_card_slot import validate

# Photo proportions fitted inside the existing lowered panel, not factory dimensions.
REAR = {'Altimeter':(.170,.103), 'Airspeed':(.170,-.023),
        'Compass':(0,.141), 'G_meter':(0,-.023),
        'Oil_pressure':(-.160,.092), 'Oil_temperature':(-.160,-.030),
        'Tachometer':(-.285,-.045)}
FRONT = {'Airspeed':(.150,.015), 'Altimeter':(.095,.100),
         'G_meter':(-.015,.105), 'Tachometer':(-.150,.025)}

def update(g):
    if g.doc.get('extras',{}).get('pittsReferenceVisuals'): return
    nodes=g.doc['nodes']; names={n['name']:i for i,n in enumerate(nodes)}
    W=g.world_matrices()
    def put(name,parts):
        if name not in names:
            names[name]=len(nodes);nodes.append({'name':name})
            g.doc['scenes'][0]['nodes'].append(names[name])
        set_mesh(g,name,parts,np.zeros(3));nodes[names[name]]['translation']=[0,0,0]
    # Preserve textured meshes and UVs; move each complete case/face pair in world space.
    for seat,layout in [('Rear',REAR),('Front',FRONT)]:
        for key,(y,z) in layout.items():
            for kind in ['Face','Bezel']:
                i=names[f'{seat}Panel_{key}_{kind}'];n=nodes[i]
                for p in g.doc['meshes'][n['mesh']]['primitives']:
                    a=g.array(p['attributes']['POSITION']);a=(W[i]@np.column_stack((a,np.ones(len(a)))).T).T[:,:3]
                    center=(a.min(0)+a.max(0))/2
                    a[:,1:]=(a[:,1:]-center[1:])*(1.4 if seat=='Rear' else 1.25)
                    a[:,1]+=z;a[:,2]+=y
                    p['attributes']['POSITION']=g.accessor(a,'VEC3')
                n.pop('matrix',None);n['translation']=[0,0,0]
    # Rough black panel finish on both curved shells and the lower switch apron.
    from PIL import Image
    rng=np.random.default_rng(27);grain=rng.integers(13,29,(256,256),dtype=np.uint8)
    with tempfile.TemporaryDirectory() as td:
        path=Path(td)/'crinkle.png';Image.fromarray(np.repeat(grain[:,:,None],3,2)).save(path)
        mat=g.material('Pitts_Black_Crinkle',png=path)
    for name in ['RearPanel_Rounded','FrontPanel_Arched','Pitts_RearPanel_Extension']:
        n=nodes[names[name]]
        for p in g.doc['meshes'][n['mesh']]['primitives']:
            p['material']=mat
            a=g.array(p['attributes']['POSITION'])
            p['attributes']['TEXCOORD_0']=g.accessor((a[:,[2,1]]-a[:,[2,1]].min(0))/np.maximum(np.ptp(a[:,[2,1]],axis=0),1e-6),'VEC2')
    metal='Pitts_Reference_Metal';blue='Pitts_Reference_Blue';black='Pitts_Reference_Black'
    g.material(metal,(.42,.44,.46));g.material(blue,(.015,.10,.65));g.material(black,(.008,.008,.009))
    # Static reference models: no new interaction or simulation claims. Existing
    # controls/rig data remain intact; hide only their obsolete panel knob meshes.
    for name in ['Ctl_PushPull_Prop','Mount_Prop','Ctl_Rotary_FuelSelector','Mount_FuelSelector']:
        nodes[names[name]].pop('mesh',None)
    shaft=Shape();handle=Shape();mount=Shape()
    # Left prop: short inboard spindle, blue blade hanging DOWN in the locked pose.
    P=np.array((-1.08,.275,-.125))
    mount.box(P,(.115,.008,.045));shaft.tube(P,P+(0,-.025,0),.006)
    handle.box(P+(0,-.029,-.032),(.023,.013,.070))
    put('Pitts_Prop_Left_Locked',[(metal,shaft,None),(blue,handle,None),(black,mount,None)])
    # Right fuel valve: upright blade ON, with left-pointing OFF legend.
    P=np.array((-.97,-.277,-.160));shaft=Shape();handle=Shape()
    shaft.tube(P,P+(0,.023,0),.009);handle.box(P+(0,.027,.030),(.019,.012,.065))
    put('Pitts_FuelValve_Right_On',[(metal,shaft,None),(black,handle,None)])
    # Trim lies beneath the left throttle, pointing forward along the wall.
    P=np.array((-1.24,.270,-.240));shaft=Shape();handle=Shape()
    shaft.tube(P,P+(.205,0,.035),.005);handle.tube(P+(.180,0,.031),P+(.218,0,.037),.010)
    put('Pitts_Trim_Left_Forward',[(metal,shaft,None),(black,handle,None)])
    data,uv=atlas([(k,t,(255,255,255),(8,8,8)) for k,t in [
        ('prop','PROP|RPM'),('lock','DOWN LOCK|UP RELEASE'),('fuel','ON'),('off','OFF'),
        ('trim','TRIM'),('nose','NOSE UP'),('down','NOSE DOWN')]])
    with tempfile.TemporaryDirectory() as td:
        p=Path(td)/'sidewall.png';p.write_bytes(data);g.material('Pitts_Sidewall_Legends',png=p)
    sh=Shape();tex=[]
    for key,p,w in [('prop',(-1.08,.263,-.069),.065),('lock',(-1.08,.263,-.198),.110),
                    ('fuel',(-.97,-.261,-.088),.030),('off',(-.925,-.261,-.160),.032),
                    ('trim',(-1.14,.260,-.280),.045),('nose',(-1.255,.260,-.278),.065),
                    ('down',(-1.025,.260,-.278),.070)]:
        quad(sh,tex,p,(1,0,0),(0,0,1),w,.013,uv[key])
    put('Pitts_Sidewall_Markings',[('Pitts_Sidewall_Legends',sh,tex)])
    ticks=Shape()
    for x in np.linspace(-1.24,-1.04,9):ticks.box((x,.260,-.260),(.002,.001,.012 if abs(x+1.14)<.001 else .007))
    g.material('Pitts_Trim_White',(.95,.95,.95));put('Pitts_Trim_Position_Ticks',[('Pitts_Trim_White',ticks,None)])
    # Rebuild rear legends so retired panel prop/fuel labels disappear.
    old=nodes[names['Pitts_V4_Rear_Legends']];old.pop('mesh',None)
    labels=[('Master','MASTER'),('Alternator','ALTERNATOR'),('FuelPump','FUEL PUMP'),('Smoke','SMOKE')]
    data,uv=atlas([(k,t,(255,255,255),(8,8,8)) for k,t in labels])
    with tempfile.TemporaryDirectory() as td:
        p=Path(td)/'switches.png';p.write_bytes(data);g.material('Pitts_Reference_Switch_Legends',png=p)
    sh=Shape();tex=[]
    for key,_ in labels:
        name='Pitts_Alternator_Switch' if key=='Alternator' else 'Ctl_Toggle_'+key
        p=np.array(nodes[names[name]]['translation']);quad(sh,tex,(-p[0]-.001,p[2],p[1]-.028),(0,-1,0),(0,0,1),.049,.012,uv[key])
    put('Pitts_Reference_Switch_Legends',[('Pitts_Reference_Switch_Legends',sh,tex)])
    g.doc['extras']['pittsReferenceVisuals']={'source':'Ty Pitts photos 2026-11-04','scope':'static visuals only; existing rig calibration unchanged','rear_yz':REAR,'front_yz':FRONT}
    validate(g,ROOT/'pitts-reference-visuals')

def main():
    for p in sorted((MODELS/'pitts-s2b-like').glob('*-own*.glb')):
        g=Glb(p);update(g);g.save(p);print(p.name)
if __name__=='__main__':main()
