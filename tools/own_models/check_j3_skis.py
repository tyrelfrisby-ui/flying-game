"""Blender geometry check: exported ski contract and unchanged own J-3 airframe."""
import bpy
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
BASE=ROOT/'unity/Assets/_Project/Resources/Models/j3-cub-like/j3-cub-like-own.glb'
SKIS=ROOT/'unity/Assets/_Project/Resources/Models/j3-cub-skis-like/j3-cub-skis-like-own.glb'
def read(path):
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(path));bpy.context.view_layer.update()
    return {o.name: sorted(tuple(round(c,5) for c in o.matrix_world@v.co) for v in o.data.vertices) for o in bpy.context.scene.objects if o.type=='MESH'}
base=read(BASE);ski=read(SKIS)
removed={'Wheel_L','Wheel_R','Wheel_Tail','TailWheel_Steer','TailWheel_Links'}
assert removed.isdisjoint(bpy.data.objects.keys())
count=0
for name,verts in base.items():
    if name in removed:continue
    assert set(ski.get(name,[]))==set(verts), 'airframe geometry moved: '+name+' counts '+str((len(verts),len(ski.get(name,[])),len(set(verts)),len(set(ski.get(name,[])))))
    count+=1
print('PASS unchanged non-target mesh world vertices:',count)
for leg,length,width,fromtip,height in [('L',1.75,.222,.95,.2235),('R',1.75,.222,.95,.2235),('Tail',.60,.10,.30,.076)]:
    pivot=bpy.data.objects['SkiPivot_'+leg]
    assert list(pivot['pitch_axis_gltf'])==[0,0,-1]
    for part in ('Ski_','SkiTip_','SkiPedestal_','SkiBungee_','SkiCheckCable_','SkiSafetyCable_'):
        assert bpy.data.objects[part+leg].parent==pivot
    pts=[pivot.matrix_world.inverted()@(o.matrix_world@v.co) for part in ('Ski_','SkiTip_') for o in [bpy.data.objects[part+leg]] for v in o.data.vertices]
    lo=[min(v[i] for v in pts) for i in range(3)];hi=[max(v[i] for v in pts) for i in range(3)]
    assert abs(hi[0]-lo[0]-length)<1e-5
    assert abs(hi[1]-lo[1]-width)<1e-5
    assert abs(lo[0]+fromtip)<1e-5
    assert abs(lo[2]+height)<1e-5
    for anchor in ('Toe','Heel','Contact','AirframeFront','AirframeRear'):assert 'SkiAnchor_'+anchor+'_'+leg in bpy.data.objects
    # Resting axle remains coincident with the original wheel axle / tail spring swivel.
    expected=Vector((4.638,0,.095) if leg=='Tail' else (-.337,-.92 if leg=='L' else .92,-.975))
    assert (pivot.matrix_world.translation-expected).length<1e-5
    print('PASS',leg,'dimensions, axle, six placeholder names and anchors')
# Exported glTF hierarchy: single parent, no dangling indices, every node reachable.
import json,struct
raw=SKIS.read_bytes();n,kind=struct.unpack_from('<II',raw,12);doc=json.loads(raw[20:20+n]);nodes=doc['nodes'];seen=set()
def visit(i):
    assert i not in seen,'cycle/multiple parent';seen.add(i)
    for child in nodes[i].get('children',[]):assert 0<=child<len(nodes);visit(child)
for scene in doc['scenes']:
    for i in scene['nodes']:visit(i)
assert len(seen)==len(nodes)
print('PASS exported graph:',len(nodes),'nodes, no orphans/cycles/multiple parents')
print('J3-SKIS MODEL CHECK PASS')
