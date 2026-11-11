"""Blender geometry check: exported ski contract, unchanged own J-3 airframe, and THE GROUND ATTITUDE.

  blender -b --python tools/own_models/check_j3_skis.py

Ty, 2026-11-10: the straight ski "has a pedestal the radius of a normal tire for that airplane so the aoa on the
ground is the same". That is a constraint, not a dimension, and it is the kind that drifts silently -- a wrong
pedestal changes the deck angle, which changes the wing's alpha, which changes the takeoff and the landing of
every ski flight, and nothing in a render would tell you. So it is asserted here, against the wheels model, in the
strongest form available: the ski aeroplane's own three-point contact points must sit at the same heights as the
wheel aeroplane's, and the pitch attitude the two stand at must agree to a hundredth of a degree.
"""
import bpy, math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
BASE=ROOT/'unity/Assets/_Project/Resources/Models/j3-cub-like/j3-cub-like-own.glb'
SKIS=ROOT/'unity/Assets/_Project/Resources/Models/j3-cub-skis-like/j3-cub-skis-like-own.glb'
def read(path):
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(path));bpy.context.view_layer.update()
    return {o.name: sorted(tuple(round(c,5) for c in o.matrix_world@v.co) for v in o.data.vertices) for o in bpy.context.scene.objects if o.type=='MESH'}
base=read(BASE)
# The wheels the skis replace, measured off the SAME model build_j3_skis.py measures: each one's lowest point (what
# the aeroplane stands on) and its radius (Ty's words for the main pedestals, where the pivot is the axle).
WHEEL={n:(min(v[2] for v in base[n]), (max(v[2] for v in base[n])-min(v[2] for v in base[n]))/2, (max(v[0] for v in base[n])+min(v[0] for v in base[n]))/2)
       for n in ('Wheel_L','Wheel_R','Wheel_Tail')}
ski=read(SKIS)
removed={'Wheel_L','Wheel_R','Wheel_Tail','TailWheel_Steer','TailWheel_Links'}
assert removed.isdisjoint(bpy.data.objects.keys())
count=0
for name,verts in base.items():
    if name in removed:continue
    assert set(ski.get(name,[]))==set(verts), 'airframe geometry moved: '+name+' counts '+str((len(verts),len(ski.get(name,[])),len(set(verts)),len(set(ski.get(name,[])))))
    count+=1
print('PASS unchanged non-target mesh world vertices:',count)
SKI_LOW={}
for leg,length,width,fromtip in [('L',1.75,.222,.95),('R',1.75,.222,.95),('Tail',.60,.10,.30)]:
    wheel='Wheel_Tail' if leg=='Tail' else 'Wheel_'+leg
    pivot=bpy.data.objects['SkiPivot_'+leg]
    assert list(pivot['pitch_axis_gltf'])==[0,0,-1]
    for part in ('Ski_','SkiTip_','SkiPedestal_','SkiBungee_','SkiCheckCable_','SkiSafetyCable_'):
        assert bpy.data.objects[part+leg].parent==pivot
    pts=[pivot.matrix_world.inverted()@(o.matrix_world@v.co) for part in ('Ski_','SkiTip_') for o in [bpy.data.objects[part+leg]] for v in o.data.vertices]
    lo=[min(v[i] for v in pts) for i in range(3)];hi=[max(v[i] for v in pts) for i in range(3)]
    assert abs(hi[0]-lo[0]-length)<1e-5
    assert abs(hi[1]-lo[1]-width)<1e-5
    assert abs(lo[0]+fromtip)<1e-5
    # THE PEDESTAL HEIGHT. Not compared against a number written here: against this aeroplane's own wheel, which is
    # where build_j3_skis.py takes it from. Mains, where the ski's pivot IS the axle, must come out at the tyre's
    # radius exactly -- Ty's words. The tail ski hangs from the leaf spring's swivel head, a few millimetres above
    # and well ahead of the old tailwheel centre, so the radius alone would NOT hold the attitude there; what must
    # hold on every leg is that the ski's running surface is where that wheel's bottom was.
    bottom_z, radius, _ = WHEEL[wheel]
    height = pivot.matrix_world.translation.z - bottom_z
    assert abs(-lo[2]-height)<1e-5, (leg,'running surface not at the derived pedestal height',-lo[2],height)
    if leg!='Tail':
        assert abs(height-radius)<1e-6, (leg,'main pedestal must be the tyre radius (Ty 2026-11-10)',height,radius)
    SKI_LOW[leg]=pivot.matrix_world.translation.z+lo[2]
    for anchor in ('Toe','Heel','Contact','AirframeFront','AirframeRear'):assert 'SkiAnchor_'+anchor+'_'+leg in bpy.data.objects
    # Resting axle remains coincident with the original wheel axle / tail spring swivel.
    expected=Vector((4.638,0,.095) if leg=='Tail' else (-.337,-.92 if leg=='L' else .92,-.975))
    assert (pivot.matrix_world.translation-expected).length<1e-5
    print('PASS %s: %.4f x %.4f m, pedestal %.4f m (wheel radius %.4f, wheel bottom z %.4f), axle, names, anchors'
          % (leg,length,width,height,radius,bottom_z))
# THE GROUND ATTITUDE, the thing all of that is for. Three-point: the mean of the two mains against the tail, on
# each model, in the world frame. Same heights, same pitch -- so the aeroplane sits at the same angle of attack on
# skis as it does on wheels, which is what Ty asked for and the reason the pedestal is derived and not typed.
mains_ski=(SKI_LOW['L']+SKI_LOW['R'])/2; tail_ski=SKI_LOW['Tail']
mains_wh=(WHEEL['Wheel_L'][0]+WHEEL['Wheel_R'][0])/2; tail_wh=WHEEL['Wheel_Tail'][0]
x_mains=(WHEEL['Wheel_L'][2]+WHEEL['Wheel_R'][2])/2; x_tail=WHEEL['Wheel_Tail'][2]
base_px=x_tail-x_mains
pitch_ski=math.degrees(math.atan2(tail_ski-mains_ski, base_px))
pitch_wh=math.degrees(math.atan2(tail_wh-mains_wh, base_px))
assert abs(mains_ski-mains_wh)<1e-5, ('mains contact height moved',mains_ski,mains_wh)
assert abs(tail_ski-tail_wh)<1e-5, ('tail contact height moved',tail_ski,tail_wh)
assert abs(pitch_ski-pitch_wh)<0.01, ('three-point attitude changed',pitch_ski,pitch_wh)
print('PASS ground attitude: three-point pitch %.4f deg on skis vs %.4f deg on wheels (mains %+.5f m, tail %+.5f m)'
      % (pitch_ski,pitch_wh,mains_ski-mains_wh,tail_ski-tail_wh))
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
