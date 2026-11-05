"""Ty 2026-11-04: four front dials, no front switches; four named rear switches.
Patch current assets, preserving node indices and unrelated meshes. No pinned old
baseline: safe to apply after the current cockpit pass and shared paint pass.
"""
import copy, json, tempfile
from pathlib import Path
import numpy as np
from vr_cockpit import Glb, Shape, MODELS, ROOT, gl
from gl_quadrant_v3 import atlas, quad
from drop_card_slot import validate

MODEL = 'pitts-s2b-like'

def update(g, rig):
    if g.doc.get('extras', {}).get('pittsPanelV4'): return rig
    nodes=g.doc['nodes']; byname={n['name']:i for i,n in enumerate(nodes)}
    controls=g.doc['extras']['vrCockpitPhase3a']['controls']
    removed=set()
    def retire(name):
        if name not in byname:return
        def visit(i):
            n=nodes[i]; removed.add(n['name']); n.pop('mesh',None)
            n['extras']={'retiredPittsPanelV4':True}
            for child in n.get('children',[]):visit(child)
        visit(byname[name])
    keep={'Master','FuelPump','Smoke','Mixture','Prop','FuelSelector'}
    retained=[]
    for c in controls:
        label=c['grip'][5:]
        if c['seat']=='Front' or label not in keep:
            retire(c['node']);retire('Mount_'+label)
        else:retained.append(c)
    for name in ['Pitts_Switch_Placards_Front','Pitts_Switch_Placards_Rear',
                 'Pitts_Radio_Faceplates_Front','Pitts_Radio_Faceplates_Rear']:
        retire(name)
    # Remove the three anonymous front faces, cases and painted needles from the
    # mixed Cockpit mesh. Only triangles wholly inside the original dial volumes.
    cockpit=nodes[byname['Cockpit']]
    for p in g.doc['meshes'][cockpit['mesh']]['primitives']:
        if 'indices' not in p:continue
        ids=g.array(p['indices']).reshape(-1,3); pos=g.array(p['attributes']['POSITION'])[ids]
        mask=np.zeros(len(ids),dtype=bool)
        for y in [-.13,0,.13]:
            inside=(abs(pos[:,:,0]-.155)<.012)&(abs(pos[:,:,1]-.045)<.037)&(abs(pos[:,:,2]-y)<.037)
            mask |= inside.all(1)
        if mask.any():p['indices']=g.accessor(ids[~mask].reshape(-1,1),'SCALAR',5125)
    # Copy four named rear faces/cases to the front; same material, UV and scale.
    # Centre row fits below the arched crown, above the stick and clear of rim.
    W=g.world_matrices()
    for key,y in [('Airspeed',.126),('Altimeter',.042),('G_meter',-.042),('Tachometer',-.126)]:
        for kind in ['Face','Bezel']:
            old=nodes[byname['RearPanel_'+key+'_'+kind]]
            new=copy.deepcopy(old); new['name']='FrontPanel_'+key+'_'+kind
            mesh=copy.deepcopy(g.doc['meshes'][old['mesh']]);new['mesh']=len(g.doc['meshes']);g.doc['meshes'].append(mesh)
            # Rear meshes contain absolute coordinates (and the panel lowering
            # lives on the node). Bake the old world transform before moving.
            for p in mesh['primitives']:
                a=g.array(p['attributes']['POSITION']);world=(W[byname[old['name']]]@np.column_stack((a,np.ones(len(a)))).T).T[:,:3]
                c=(world.min(0)+world.max(0))/2
                target=np.array([.1635 if kind=='Face' else .157,.045,y])
                p['attributes']['POSITION']=g.accessor(world-c+target,'VEC3')
            new.pop('matrix',None);new.pop('translation',None);new.pop('children',None)
            nodes.append(new);g.doc['scenes'][0]['nodes'].append(len(nodes)-1)
    # Alternator is a labelled visual toggle: no invented electrical simulation.
    source=nodes[byname['Ctl_Toggle_Master']]
    alt=copy.deepcopy(source);alt['name']='Pitts_Alternator_Switch';alt.pop('children',None);alt.pop('extras',None)
    alt['translation']=[.809,-.2178,.176];nodes.append(alt);g.doc['scenes'][0]['nodes'].append(len(nodes)-1)
    # Existing Master mesh includes its switch; its grip children were interaction
    # markers, so clone only its visible switch geometry.
    data,uv=atlas([(k,t,(255,255,255),(8,8,8)) for k,t in [
        ('Master','MASTER'),('Alternator','ALTERNATOR'),('FuelPump','FUEL|PUMP'),('Smoke','SMOKE'),
        ('Mixture','MIXTURE'),('Prop','PROP'),('FuelSelector','FUEL|VALVE')]])
    with tempfile.TemporaryDirectory() as td:
        path=Path(td)/'labels.png'
        path.write_bytes(data);mat=g.material('Pitts_V4_Legends',png=path)
    shape=Shape();uvs=[]
    rows={c['grip'][5:]:c['pivot_gltf'] for c in retained};rows['Alternator']=alt['translation']
    for label,pivot in rows.items():
        x,z,y=pivot
        quad(shape,uvs,(-x-.0008,y,z-.028),(0,-1,0),(0,0,1),.047 if label=='Alternator' else .029,.012,uv[label])
    g.mesh('Pitts_V4_Rear_Legends',[gl(v) for v in shape.v],shape.t,mat,uvs)
    # Front prop lever parallels the existing front throttle on the left wall.
    throttle=nodes[byname['Throttle_Front']]
    prop=copy.deepcopy(throttle);prop['name']='PropLever_Front';prop.pop('children',None)
    prop['translation']=list(prop['translation']);prop['translation'][2]-=.035
    prop['extras']=copy.deepcopy(prop.get('extras',{}));prop['extras']['rig_param']='prop'
    nodes.append(prop);g.doc['scenes'][0]['nodes'].append(len(nodes)-1)
    # Blue handle (the throttle's original handle is a separate child).
    handle=copy.deepcopy(nodes[byname['Grip_Throttle_Front']]);handle['name']='Grip_PropLever_Front'
    nodes.append(handle);prop['children']=[len(nodes)-1]
    # Share its proven lever travel/calibration, reading the existing prop input.
    entry=copy.deepcopy(next(r for r in rig['rig'] if r['node']=='Throttle_Front'))
    entry['node']='PropLever_Front';entry['param']='prop';entry['pivot_gltf']=prop['translation']
    rig['rig']=[r for r in rig['rig'] if r['node'] not in removed]+[entry]
    rig['controls']=retained;g.doc['extras']['vrCockpitPhase3a']['controls']=retained
    g.doc['extras']['pittsPanelV4']={'front':['Airspeed','Altimeter','G_meter','Tachometer'],
        'rear_switches':['MASTER','ALTERNATOR','FUEL PUMP','SMOKE'], 'alternator':'visual only'}
    validate(g, ROOT/'pitts-panel-v4')
    return rig

def main():
    rp=ROOT/'docs/models/pitts-s2b-own-rig.json';original=json.loads(rp.read_text());result=None
    for path in sorted((MODELS/MODEL).glob('*-own*.glb')):
        g=Glb(path);result=update(g,copy.deepcopy(original))
        from pitts_reference_visuals import update as reference_visuals
        reference_visuals(g);g.save(path);print(path.name)
    rp.write_text(json.dumps(result,indent=1)+'\n')

if __name__=='__main__':main()
