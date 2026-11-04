"""Extra 300 (Ty t289u): rear throttle quadrant lowered, canopy latch handles as lateral cylinders. CPU only, idempotent.

1. Rear throttle. Ty: the rear throttle sits LOWER on the left side wall than ours did (on top of the ledge at the sill,
   knob 0.29 m below the eye). Reference: rear-seat photo of an Extra 300L (r/flying "Extra 300L - Backseat panel",
   i.imgur.com/xWpVN2o.jpg, 2015): the rear throttle knob is low on the left, down by the lower frame tubes, well below
   the sill. The whole lever (pivot block, arm, knob) moves 0.125 m down and 0.062 m inboard onto the ledge's inner face,
   with a dark gate plate there; knob top now at the ledge top. Same lever, same 50 deg sweep about the same lateral axis,
   Grip_Throttle_Rear rides along (VrCockpitHands reads it at the lever's live position). The front throttle is unchanged.
   Our rear prop and mixture are panel push-pulls (Ctl_PushPull_Prop/_Mixture), not quadrant levers; they are not moved.
2. Canopy latches. Ty: the handles are CYLINDERS ~25 mm (1 in) in diameter and ~127 mm (5 in) long, PARALLEL to the
   lateral axis, sticking straight in from the side wall (the canopy frame) toward the centreline. All four handles
   (Aft and Fwd in both cockpits) become a 25 mm red cylinder with a white inner end, 127 mm from the frame's inner
   face. Node origins, the 35 mm squeeze of the Fwd handle and the pins are unchanged; the Grip_ points move to the
   middle of each cylinder (and the compatibility midpoint Grip_Canopy_Latch* with them).
Builder frame: X forward, Y left, Z up (scene glTF = (-x, z, y)).
"""
import json
import numpy as np
from vr_cockpit import Glb, Shape, gl, builder, ROOT
from drop_card_slot import validate

THROTTLE_SHIFT = np.array((0.0, -0.062, -0.125))   # builder, rear lever only
LATCH_D, LATCH_L, WHITE_L = .025, .127, .020         # cylinder diameter, length from the frame face, white inner end

def idx(g, n): return next(i for i, o in enumerate(g.doc['nodes']) if o['name'] == n)
def parent_of(g, i): return next((j for j, o in enumerate(g.doc['nodes']) if i in o.get('children', [])), None)
def world(g, n): return g.world_matrices()[idx(g, n)]

def set_world_translation(g, n, p_builder):
    """Move node n so its world origin is p_builder; rotation/scale untouched."""
    i = idx(g, n); j = parent_of(g, i); W = g.world_matrices()
    P = W[j] if j is not None else np.eye(4)
    local = np.linalg.inv(P) @ np.append(gl(p_builder), 1.0)
    g.doc['nodes'][i].pop('matrix', None); g.doc['nodes'][i]['translation'] = local[:3].tolist()

def local_mesh(g, n, shape, mat):
    """Shape authored in builder world coordinates, stored in node n's local frame."""
    Wi = np.linalg.inv(world(g, n))
    v = [(Wi @ np.append(gl(q), 1.0))[:3] for q in shape.v]
    g.mesh(n, v, shape.t, mat)

def update(path):
    if path.parent.name != 'extra-300-like': return
    g = Glb(path)
    if g.doc.get('extras', {}).get('vrCockpitPhase3e'): return
    assert g.doc.get('extras', {}).get('vrCockpitPhase3d'), path
    # ---- 1. rear throttle ----
    old = builder(world(g, 'Throttle_R')[:3, 3])
    new = old + THROTTLE_SHIFT
    set_world_translation(g, 'Throttle_R', new)
    gate = g.material('P3e_Quadrant', (.025, .025, .028))
    sh = Shape()
    face_y = new[1] + .019                           # lever's outer face +3 mm
    sh.box((new[0]-.015, face_y+.003, new[2]+.035), (.16, .006, .11))     # gate plate on the ledge face
    sh.box((new[0]-.015, face_y-.001, new[2]+.088), (.13, .004, .006))    # slot rim, top
    sh.box((new[0]-.015, face_y-.001, new[2]-.018), (.13, .004, .006))    # slot rim, bottom
    g.mesh('Throttle_Quadrant_R_P3e', [gl(q) for q in sh.v], sh.t, gate)
    # ---- 2. canopy latch cylinders ----
    red = g.material('VR_Latch_Red', (.7, .015, .015)); white = g.material('VR_Latch_White', (.9, .9, .86))
    moved = {}
    for suffix in ('', '_Front'):
        for label in ('Aft', 'Fwd'):
            n = 'Canopy_Latch_Handle_' + label + suffix
            q = builder(world(g, n)[:3, 3])
            y0 = q[1] + .018                         # the frame sample the P2c handle hung from (vr_phase2.extra_latch)
            y1, yw = y0 - LATCH_L, y0 - LATCH_L + WHITE_L
            r = LATCH_D / 2
            sh = Shape(); sh.tube((q[0], y0, q[2]), (q[0], yw, q[2]), r, 20); local_mesh(g, n, sh, red)
            sh = Shape(); sh.tube((q[0], yw, q[2]), (q[0], y1, q[2]), r + .0003, 20); local_mesh(g, n + '_White', sh, white)
            grip = np.array((q[0], (y0 + y1) / 2, q[2]))
            set_world_translation(g, 'Grip_Canopy_Latch_' + label + suffix, grip); moved['Grip_Canopy_Latch_' + label + suffix] = grip
        n = 'Grip_Canopy_Latch' + suffix
        q = builder(world(g, n)[:3, 3]); q[1] = moved['Grip_Canopy_Latch_Aft' + suffix][1]
        set_world_translation(g, n, q); moved[n] = q
    g.doc['extras']['vrCockpitPhase3e'] = dict(
        throttle_rear_shift_builder_m=THROTTLE_SHIFT.tolist(), throttle_rear_pivot_builder=new.round(4).tolist(),
        latch_cylinder_d_m=LATCH_D, latch_cylinder_len_m=LATCH_L,
        source='Ty t289u; Extra 300L rear-seat photo (r/flying 2015, i.imgur.com/xWpVN2o.jpg)')
    validate(g, path); g.save(path)
    print('EXTRA', path.name, 'rear throttle', old.round(3), '->', new.round(3), '; latch cylinders', len(moved))
    return new, moved

def update_rig(new_throttle, moved):
    rp = ROOT/'docs/models/ea300-own-rig.json'; rig = json.loads(rp.read_text())
    pts = {'Throttle_R': new_throttle, **moved}
    for e in rig['rig']:
        if e['node'] in pts: e['pivot_gltf'] = gl(pts[e['node']]).round(6).tolist()
    rp.write_text(json.dumps(rig, indent=1) + '\n')

if __name__ == '__main__':
    from vr_cockpit import MODELS
    res = None
    for p in sorted((MODELS/'extra-300-like').glob('*own*.glb')): res = update(p) or res
    if res: update_rig(*res)
