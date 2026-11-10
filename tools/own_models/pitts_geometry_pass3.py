"""Pitts photo pass 3: static geometry only, builder X forward / Y left / Z up.
Floor cutouts reuse floor_windows.py from 8890328f, including Lexan materials.
Control pivots are named for a later Claude runtime wiring pass; no rig edits.
"""
import tempfile
from pathlib import Path
import numpy as np
from vr_cockpit import Shape, gl
from gl_quadrant_v3 import set_mesh, atlas, quad
from drop_card_slot import validate


def update(g):
    if g.doc.get('extras', {}).get('pittsGeometryPass3'): return
    from floor_windows import apply
    apply(g, 'pitts-s2b-like')
    nodes=g.doc['nodes']; names={n['name']:i for i,n in enumerate(nodes)}
    # Keep historical indices and rig references, but remove every obsolete mixture mesh.
    for n in nodes:
        if 'Mixture' in n.get('name',''): n.pop('mesh',None)
    def put(name,parts,pivot=(0,0,0)):
        if name not in names:
            names[name]=len(nodes);nodes.append({'name':name})
            g.doc['scenes'][0]['nodes'].append(names[name])
        set_mesh(g,name,parts,np.array(pivot))
        nodes[names[name]]['translation']=gl(pivot).tolist()
    metal='Pitts_Reference_Metal';black='Pitts_Reference_Black';red='Pitts_Mixture_Red'
    g.material(metal,(.42,.44,.46));g.material(black,(.008,.008,.009));g.material(red,(.72,.008,.012))
    # Wide photo: outboard of left tube, below and aft of rear throttle.
    # Push/pull rod points fore/aft; shown pushed in (rich).
    P=np.array((-1.205,.302,-.275))
    shaft=Shape();knob=Shape();boss=Shape()
    shaft.tube(P+(.045,0,0),P,.0045)
    knob.tube(P+(-.010,0,0),P+(.010,0,0),.017,20)
    boss.tube(P+(.044,0,0),P+(.054,0,0),.011)
    put('Mixture',[(metal,shaft,None),(red,knob,None)],P)
    put('Mixture_Mount',[(metal,boss,None)])
    nodes[names['Mixture']]['extras']={'visualOnly':True,'motion':'push forward rich; pull aft lean / idle cutoff','axis_builder':[1,0,0]}
    # Lower left beneath the leg: slotted gate with three clearly separated notches.
    P=np.array((-1.26,.245,-.445));gate=Shape();ticks=Shape();lever=Shape();grip=Shape()
    gate.box((-1.20,.259,-.445),(.19,.008,.045))
    for x in [-1.27,-1.20,-1.13]:
        ticks.box((x,.253,-.434),(.004,.004,.022))
        ticks.box((x+.009,.253,-.425),(.020,.004,.004))
    lever.tube(P,P+(.045,-.023,.052),.005)
    grip.tube(P+(.035,-.023,.052),P+(.060,-.023,.052),.012)
    put('CowlFlap',[(metal,lever,None),(black,grip,None)],P)
    put('CowlFlap_Detent_Gate',[(black,gate,None),(metal,ticks,None)])
    nodes[names['CowlFlap']]['extras']={'visualOnly':True,'detents':['CLOSED','MID','OPEN']}
    data,uv=atlas([(k,k,(255,255,255),(8,8,8)) for k in ['CLOSED','MID','OPEN','MIXTURE']])
    with tempfile.TemporaryDirectory() as td:
        p=Path(td)/'labels.png';p.write_bytes(data);g.material('Pitts_Pass3_Legends',png=p)
    sh=Shape();tex=[]
    for key,x in [('CLOSED',-1.27),('MID',-1.20),('OPEN',-1.13)]:
        quad(sh,tex,(x,.251,-.466),(1,0,0),(0,0,1),.060,.012,uv[key])
    quad(sh,tex,(-1.205,.297,-.308),(1,0,0),(0,0,1),.070,.012,uv['MIXTURE'])
    put('Pitts_Pass3_Legends',[('Pitts_Pass3_Legends',sh,tex)])
    # Alias the established frame without changing its mesh, material or cutouts.
    frame=nodes[names['Floor_Window_Frame']]
    alias=len(nodes);nodes.append({'name':'Floor_Frame','children':[names['Floor_Window_Frame']]})
    roots=g.doc['scenes'][0]['nodes'];roots.remove(names['Floor_Window_Frame']);roots.append(alias)
    g.doc.setdefault('extras',{})['pittsGeometryPass3']={'visualOnly':True,'floorSource':'8890328f','switchLabels':['MASTER','ALTERNATOR','FUEL PUMP','SMOKE']}
    validate(g,Path('pitts-pass3'))
