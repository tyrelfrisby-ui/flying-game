"""CPU-only P3a authoring. Builder metres X forward/Y left/Z up; all new placements E."""
import json
import numpy as np
from vr_cockpit import Author, Shape, Glb, ROOT, MODELS, gl, builder
from drop_card_slot import validate
SPECS={'pitts-s2b-like':('pitts-s2b',[-.80,-.145],.02),
       'extra-300-like':('ea300',[-1.162,-.40],.23),
       'great-lakes-like':('great-lakes',[-1.638,-.968],-.06)}

def definitions():
    # name, kind, ModelRig param, end values, travel, detents
    d=[('Magneto','Rotary','magneto',[0,4],130,[0,1,2,3,4])]
    d += [(n,'Toggle',p,[0,1],40,[0,1]) for n,p in [('Master','master'),('FuelPump','fuel_pump'),('NavLights','nav_lights'),('Strobe','strobe'),('LandingLight','landing_light'),('Smoke','smoke')]]
    d += [('Mixture','PushPull','mixture',[0,1],50,[]),('Prop','PushPull','prop',[0,1],50,[]),('AlternateAir','PushPull','alternate_air',[0,1],35,[0,1]),('Trim','Rotary','trim',[-1,1],300,[]),('FuelSelector','Rotary','fuel_selector',[0,2],180,[0,1,2])]
    d += [('ComOuter','Rotary','com_standby_mhz',[118,136],540,list(range(118,137))),('ComInner','Rotary','com_standby_mhz',[0,.975],360,[i*.025 for i in range(40)]),('ComVolume','Rotary','com_volume',[0,1],270,[]),('ComSwap','PushPull','com_swap',[0,1],4,[0,1])]
    d += [(f'XpdrDigit{i}','Rotary','xpdr_squawk',[0,7],315,list(range(8))) for i in range(1,5)]
    d += [('XpdrMode','Rotary','xpdr_mode',[0,3],90,[0,1,2,3]),('XpdrIdent','PushPull','xpdr_ident',[0,1],4,[0,1])]
    return d

def update(path):
    model=path.parent.name;key,xs,z=SPECS[model];g=Glb(path)
    if g.doc.get('extras',{}).get('vrCockpitPhase3a'):return
    a=Author(g);black=g.material('P3_ControlBlack',(.025,.025,.03));red=g.material('P3_MixtureRed',(.8,.025,.025));blue=g.material('P3_PropBlue',(.025,.12,.8));metal=g.material('P3_ControlMetal',(.55,.55,.58))
    rigpath=ROOT/f'docs/models/{key}-own-rig.json';doc=json.loads(rigpath.read_text());controls=[]
    # Superseded hand controls keep inert compatibility nodes, never coincident moving meshes.
    oldparams={'magneto','master','mixture','prop','com_freq_mhz','com_volume','xpdr_knob_clicks','fuel_selector','fuel_valve'}
    obsolete={r['node'] for r in doc['rig'] if r['param'] in oldparams or (r['param']=='trim' and r['node'].startswith('Trim_'))}
    for n in g.doc['nodes']:
        if n['name'] in obsolete:n.pop('mesh',None);n['extras']={'p3_compatibility_empty':True}
    doc['rig']=[r for r in doc['rig'] if r['node'] not in obsolete]
    for seat,x in zip(('Rear','Front'),xs):
        suffix='' if seat=='Rear' else '_Front'
        for j,(label,kind,param,ends,travel,detents) in enumerate(definitions()):
            if model=='great-lakes-like' and label=='AlternateAir':label='CarbHeat';param='carb_heat'
            if model=='great-lakes-like' and label=='FuelSelector':param='fuel_valve';ends=[0,1];travel=90;detents=[0,1]
            # Two reachable side consoles; no new geometry in the central stick/knee corridor.
            side=1 if j<12 else -1;col=(j if j<12 else j-12)%4;row=(j if j<12 else j-12)//4
            p=np.array((x+.045+row*.065,side*(.215+col*.036),z+.22))
            n=f'Ctl_{kind}_{label}{suffix}';s=Shape();axis=(1,0,0) if kind!='Toggle' else (0,1,0)
            if kind=='Toggle':s.tube(p,p+(-.028,0,.012),.004);s.sphere(p+(-.030,0,.012),.006,10,6);contact=p+(-.030,0,.012)
            else:
                s.tube(p,p+(-.023,0,0),.012 if kind=='Rotary' else .010,16);s.box(p+(-.025,0,0),(.008,.024,.006));contact=p+(-.030,0,0)
            mount=Shape();mount.box(p+( .008,0,0),(.012,.032,.036));a.node('Mount_'+label+suffix,p,shape=mount,mat=black)
            a.node(n,p,shape=s,mat=red if label=='Mixture' else blue if label=='Prop' else metal if kind=='Toggle' else black)
            a.node('Grip_'+label+suffix,contact,n);a.node('Poke_'+label+suffix,contact,n)
            move=kind=='PushPull';points=detents if label=='Magneto' else ends
            values=[0,30,60,90,130] if label=='Magneto' else ([0,-travel/1000] if move else [0,travel])
            a.rigged(n,param,axis,points,values,'move' if move else 'rot',desc='(E) P3a reachable side console; owner review pending')
            c=dict(node=n,param=param,kind=kind,seat=seat,axis_gltf=gl(axis).tolist(),pivot_gltf=gl(p).tolist(),travel=travel,travel_unit='mm' if move else 'deg',sim_ends=ends,detents=detents,detent_count=len(detents),spring_return=label in ('Magneto','ComSwap','XpdrIdent'),spring_return_from=4 if label=='Magneto' else 1,spring_return_to=3 if label=='Magneto' else 0,poke='Poke_'+label+suffix,grip='Grip_'+label+suffix,position_status='E',sweep_radius_m=.040,semantics='absolute')
            if label=='ComOuter':c.update(semantics='increment',step=1,wrap=[118,136.975])
            if label=='ComInner':c.update(semantics='increment_fraction',step=.025,alternative_step_mhz=1/120,channel_spacing_requires_owner_choice=True)
            if label.startswith('XpdrDigit'):c.update(semantics='octal_digit',digit_place=10**(4-int(label[-1])),wrap=[0,7])
            g.doc['nodes'][a.idx(n)]['extras']['interaction']=c
            controls.append(c)
    doc['rig']+=a.rig;doc['controls']=controls
    doc['phase3a_notes']='All placements (E). Both seats linked. Pitts/Extra: alternate air, no carb heat. Great Lakes: config 180hp/361in3, builder O-360; carb heat pending Ty engine confirmation. New params require Claude binding. Existing primary controls/canopy/egress retain prior rigs.'
    rigpath.write_text(json.dumps(doc,indent=1)+'\n');g.doc.setdefault('extras',{})['vrCockpitPhase3a']={'controls':controls,'position_status':'E'}
    validate(g,path);g.save(path)
