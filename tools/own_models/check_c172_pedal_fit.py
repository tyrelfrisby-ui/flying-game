"""Seat the existing 1.778 m mannequin at the approved 172 pilot eye and test the legs against the cockpit.

    blender -b --factory-startup --python tools/own_models/check_c172_pedal_fit.py -- MODEL.glb OUT.json [RENDER_DIR PREFIX]

THE HUMAN IS NOT NEW. It is Codex's round-3 rigid-segment mannequin (lane/codex-eyepoints f05f1511,
tools/own_models/eyepoints_v3.py MANNEQUIN / S): Plagenhoef, Evans & Abdelnour (1983) Table 8 joint-to-joint
ratios times 1.778 m, FAA HFDS App. B external dimensions scaled by 1.778/1.767, and its 12-degree torso recline
for the 172 (torso_up_fru in docs/models/eyepoints.json on that branch). The segment lengths below are copied by
hand from that table, so a change there has to be a deliberate change here. Its feet use the same convention as
its pose(): the ankle sits on the sole line, the ball of the foot is 0.161 m ahead of it, and the foot pitch is
searched 0..85 degrees for the one that puts the ball on the actual pedal face at an exact shank length.

THE EYE IS FIXED. It is the approved pilot eye (CockpitEyeTests.ApprovedEyes, c172-like/pilot, body
-0.346, -0.337, -0.548), and everything else, hip, knees and feet, is solved backwards from it: nothing here
moves the eye or the seat to make the legs fit.

What it measures, at neutral, at full rudder each way (the pedals on their own -Y hinges, one forward, the
other back, the rig's 18 degrees) and with the pilot's yoke over its rig travel (column -0.07, 0, +0.07 m,
wheel every 10 degrees over +/-90; yoke-v1 gates the yoke at YOKE_CLEAR 15 mm from every leg part):
  * that each foot reaches its pedal (shank-length residual) and the included knee angle;
  * signed clearance of thighs, shanks and feet to the panel, the yokes, the floor, the pedals and everything else;
  * how far every pedal stays above the carpet;
  * the seat (seat-v1): the torso's gap to the seatback along its length and the pelvis's, perpendicular to the
    12-degree torso; how far the buttocks press into the cushion; and that neither front seat runs into the trim
    wheel, the pedestal, the fuel selector or a door armrest.
Any failure is printed and the script exits non-zero; the numbers go to OUT.json.
"""
import bpy, json, math, os, sys
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:]
MODEL, OUT_JSON = argv[0], argv[1]
RENDER_DIR, PREFIX = (argv[2], argv[3]) if len(argv) > 3 else (None, None)

# ---- the mannequin: copied by hand from eyepoints_v3.py S (f05f1511) ----------------------------------------
H = 1.778; FAA = H / 1.767
S = dict(contact_to_h_point=.095 * FAA, torso_h_to_shoulder=.300 * H, pelvis_h_to_waist=.093 * H,
         neck_shoulder_to_skull_base=.927 * FAA - .095 * FAA - .300 * H - .240, skull_base_to_center=.120,
         head_center_to_eye_forward=.0775, head_sphere_radius=.120, hip_to_knee=.232 * H, knee_radius=.043 * FAA,
         knee_to_ankle=.247 * H, foot_heel_to_toe=.260 * FAA, ankle_from_heel=.060 * FAA, ankle_to_ball=.160 * FAA,
         hip_joint_half_width=.113 * H / 2, shoulder_joint_half_width=.245 * H / 2, torso_half_depth=.278 * FAA / 2,
         pelvis_half_depth=.299 * FAA / 2, hip_half_width=.180 * FAA, upper_arm=.172 * H, forearm=.157 * H,
         thigh_radius=.065, neck_radius=.045, hand_radius=.035)
TORSO_UP = Vector((-0.20791169, 0, 0.9781476))      # eyepoints.json c172 torso_up_fru, as (fwd, -, up)
FOOT_HALF = 0.035                                     # foot solid: capsule riding on the sole line, dorsal side

# Approved pilot eye, body (x fwd, y right, z down) -> Blender (x fwd, y left, z up). Literal, not read back.
EYE = Vector((-0.346, 0.337, 0.548))
REACH_TOL = 0.010              # shank-length residual that still counts as "on the pedal"
KNEE_RANGE = (110.0, 135.0)    # neutral knee included angle: engineering target for a seated pedal posture
CLEAR = 0.010                  # minimum clearance of a leg to the panel, the yokes and the other pedals
YOKE_CLEAR = 0.015             # yoke-v1 (Ty 2026-11-04): the yoke may not come within 15 mm of a leg anywhere in its travel
FLOOR_TOL = 0.003              # a heel may rest on the carpet; it may not sink into it
PEDAL_CONTACT = 0.005          # the sole may press this far into its own pedal face (tread ribs)
PEDAL_FLOOR_MIN = 0.020        # every pedal part at least this far above the carpet at full travel
THIGH_LIFT_MAX = 20           # degrees the thigh may rise off the cushion (hip flexion) to follow a pedal back
RUDDER_DEG, COLUMN_M, WHEEL_DEG = 18.0, 0.07, 90.0   # rig ranges, docs/models/c172-panel-bindings.json

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=MODEL)
SC = bpy.context.scene
OB = {o.name: o for o in SC.objects}
BASE = {o.name: o.matrix_world.copy() for o in SC.objects}


def unit(v): return v.normalized()


def world_tris(o, keep=None):
    me = o.data; me.calc_loop_triangles(); mw = o.matrix_world
    V = [mw @ v.co for v in me.vertices]; out = []
    for t in me.loop_triangles:
        tri = [V[i] for i in t.vertices]
        mat = o.material_slots[t.material_index].material.name if o.material_slots and o.material_slots[t.material_index].material else ''
        if keep is None or keep(tri, mat): out.append((tri, mat))
    return out


def bvh(tris):
    if not tris: return None
    verts, faces = [], []
    for tri, _ in tris:
        faces.append((len(verts), len(verts) + 1, len(verts) + 2)); verts.extend(tri)
    return BVHTree.FromPolygons(verts, faces)


def centroid(tri): return (tri[0] + tri[1] + tri[2]) / 3


# ---- the human, solved backwards from the eye -----------------------------------------------------------------
up = TORSO_UP
eye_from_h = (S['torso_h_to_shoulder'] + S['neck_shoulder_to_skull_base']) * up + Vector((S['head_center_to_eye_forward'], 0, S['skull_base_to_center']))
HP = EYE - eye_from_h
J = dict(h_point=HP, waist=HP + S['pelvis_h_to_waist'] * up, shoulder=HP + S['torso_h_to_shoulder'] * up)
J['skull_base'] = J['shoulder'] + S['neck_shoulder_to_skull_base'] * up
J['head_center'] = J['skull_base'] + Vector((0, 0, S['skull_base_to_center']))
LEGS = dict(left=+1, right=-1)                       # Blender +Y is the pilot's left
for side, sign in LEGS.items():
    J[side + '_hip'] = HP + Vector((0, sign * S['hip_joint_half_width'], 0))
    J[side + '_knee'] = J[side + '_hip'] + S['hip_to_knee'] * Vector((1, 0, 0))
PEDAL_OF = dict(left='C172_Pedal_L_L', right='C172_Pedal_L_R')
TOE_OF = dict(left='C172_Toe_brake_L_L', right='C172_Toe_brake_L_R')

PEDALS = sorted(n for n in OB if n.startswith('C172_Pedal_') or n.startswith('C172_Toe_brake_'))
YOKES = sorted(n for n in OB if n.startswith('C172_Yoke_'))


def depth(o): return 0 if o.parent is None else 1 + depth(o.parent)


def reset():
    for n in sorted(BASE, key=lambda n: depth(OB[n])):  # parents first, or a child moves twice
        OB[n].matrix_world = BASE[n]; bpy.context.view_layer.update()


def rotate_about_origin(name, axis, deg):
    o = OB[name]; p = BASE[name].translation
    o.matrix_world = Matrix.Translation(p) @ Matrix.Rotation(math.radians(deg), 4, axis) @ Matrix.Translation(-p) @ BASE[name]


def pose_rudder(rudder):
    """+1 = full right rudder: right pedal of each pair forward, left one back, about each pedal's own hinge.
    The rig hinge is -Y (Blender): +deg about -Y tips a pedal's top AFT, so forward is -deg."""
    for n in PEDALS:
        if not n.startswith('C172_Pedal_'): continue
        tag = n[-1]; push = rudder if tag == 'R' else -rudder
        rotate_about_origin(n, Vector((0, -1, 0)), -push * RUDDER_DEG)
    bpy.context.view_layer.update()                    # toe brakes ride on their pedal (glTF parent)


def pose_yoke(column, wheel):
    """column +1 = full forward (push), wheel +1 = full right; pilot's yoke only."""
    for side in 'LR':
        pitch = OB['C172_Yoke_%s_pitch' % side]
        pitch.matrix_world = Matrix.Translation(Vector((column * COLUMN_M, 0, 0))) @ BASE[pitch.name]
        bpy.context.view_layer.update()
        roll = OB['C172_Yoke_%s_roll' % side]; p = roll.matrix_world.translation.copy()
        roll.matrix_world = Matrix.Translation(p) @ Matrix.Rotation(math.radians(wheel * WHEEL_DEG), 4, Vector((1, 0, 0))) @ Matrix.Translation(-p) @ roll.matrix_world
    bpy.context.view_layer.update()


SEAT_MATS = ('saddle_vinyl', 'warm_ivory', 'woven_ochre')


def is_seat_upholstery(t, m):
    """The front seats' cushion, back and belts, wherever the seat is: upholstery between the pedestal and the
    door trim (the trim's inner faces are at |y| >= 0.47, its armrest at >= 0.4065), aft of the panel, below the
    headliner. Works on the old seat place and the new one, so before and after are measured the same way."""
    c = centroid(t)
    return any(s in m for s in SEAT_MATS) and 0.085 < abs(c.y) < 0.40 and -0.85 < c.x < 0.05 and -0.40 < c.z < 0.32


def scene_groups():
    """Triangle groups in their current pose. Interior_static is one joined mesh, so it is split by place/material."""
    st = OB['C172_Interior_static']
    is_floor = lambda t, m: 'carpet' in m and centroid(t).z < -0.50
    # Seat: the upholstery above (which follows the seat when it moves), plus the pedals-v1 rule unchanged, so the
    # leg clearances are measured against the same "other" structure as before.
    is_seat = lambda t, m: not is_floor(t, m) and (is_seat_upholstery(t, m) or (centroid(t).x < -0.20 and centroid(t).z < -0.05 and abs(centroid(t).y) > 0.06))
    is_panel = lambda t, m: centroid(t).x > 0.15 and centroid(t).z > -0.12
    g = dict(floor=world_tris(st, is_floor), seat=world_tris(st, is_seat), panel=world_tris(st, lambda t, m: is_panel(t, m) and not is_floor(t, m)))
    g['other'] = world_tris(st, lambda t, m: not (is_floor(t, m) or is_seat(t, m) or is_panel(t, m)))
    for n, o in OB.items():
        if o.type != 'MESH' or n == 'C172_Interior_static': continue
        if n.startswith('C172_Yoke_'): g.setdefault('yoke', []).extend(world_tris(o))
        elif n.startswith('C172_Pedal_') or n.startswith('C172_Toe_brake_'): g.setdefault('pedal:' + n, []).extend(world_tris(o))
        elif n.startswith('C172_Seat_base'): g['seat'].extend(world_tris(o))
        elif n.startswith('C172_') and o.data.vertices and max((o.matrix_world @ v.co).x for v in o.data.vertices) > 0.15 and min((o.matrix_world @ v.co).z for v in o.data.vertices) > -0.12:
            g['panel'].extend(world_tris(o))
        elif n.startswith('C172_'): g['other'].extend(world_tris(o))
    return {k: (bvh(v), v) for k, v in g.items() if v}


def capsule_clear(tree, a, b, r, step=0.004):
    """Surface distance from the capsule's axis minus its radius: negative = interpenetration depth. Unsigned on
    purpose: the interior is one joined mesh of overlapping boxes, where a face normal cannot say inside from out.
    (The floor, the one thing a limb could be wholly inside, is measured vertically instead: floor_clear.)"""
    n = max(2, int((b - a).length / step) + 1); best = (9.0, None)
    for i in range(n):
        p = a.lerp(b, i / (n - 1)); loc, nrm, idx, d = tree.find_nearest(p)
        if loc is not None and d - r < best[0]: best = (d - r, loc)
    return best


def floor_clear(tree, a, b, r, step=0.004):
    n = max(2, int((b - a).length / step) + 1); best = (9.0, None)
    for i in range(n):
        p = a.lerp(b, i / (n - 1)); h = floor_height(tree, p)
        if h is not None and p.z - h - r < best[0]: best = (p.z - h - r, Vector((p.x, p.y, h)))
    return best


def floor_height(tree, p):
    hit = tree.ray_cast(Vector((p.x, p.y, 0.5)), Vector((0, 0, -1)))
    return hit[0].z if hit[0] is not None else None


def solve_foot(side, groups, allow_floor=False):
    """Codex's search: pedal-face points x foot pitch 0..85, ankle at an exact shank length from the fixed knee;
    reject floor penetration; of the exact solutions take the one whose sole lies flattest on the face."""
    for lift in range(0, THIGH_LIFT_MAX + 1, 2):
        r = solve_foot_at(side, groups, allow_floor, lift)
        if r is not None: return r
    return None


def solve_foot_at(side, groups, allow_floor, lift):
    knee = J[side + '_hip'] + S['hip_to_knee'] * Vector((math.cos(math.radians(lift)), 0, math.sin(math.radians(lift))))
    tris = groups['pedal:' + PEDAL_OF[side]][1] + groups.get('pedal:' + TOE_OF[side], (None, []))[1]
    floor = groups['floor'][0]; own = bvh(tris); pts = []
    for tri, _ in tris:
        nrm = (tri[1] - tri[0]).cross(tri[2] - tri[0])
        if nrm.length < 1e-9: continue
        nrm.normalize()
        if nrm.x > -0.30: continue                     # the face the foot pushes on faces aft
        for w in [(1/3, 1/3, 1/3), (.6, .2, .2), (.2, .6, .2), (.2, .2, .6)]:
            pts.append((w[0] * tri[0] + w[1] * tri[1] + w[2] * tri[2], nrm))
    best = None
    for q, nrm in pts:
        for a in range(86):
            f = Vector((math.cos(math.radians(a)), 0, math.sin(math.radians(a))))
            ankle_t = q - S['ankle_to_ball'] * f
            err = abs((ankle_t - knee).length - S['knee_to_ankle'])
            if err > 0.012: continue
            ankle = knee + unit(ankle_t - knee) * S['knee_to_ankle']
            heel = ankle - S['ankle_from_heel'] * f; toe = ankle + (S['foot_heel_to_toe'] - S['ankle_from_heel']) * f
            flat = abs(f.dot(nrm)); key = (round(err / 0.002), round(abs(q.y - knee.y) / 0.015), round(flat / 0.03), abs(q.y - knee.y))
            if best is not None and key >= best[0]: continue
            cand = dict(err=err, ball=ankle + S['ankle_to_ball'] * f, ankle=ankle, heel=heel, toe=toe, pitch=a, face=nrm, knee=knee, lift=lift)
            # The SOLID foot and shank, not just their points: a rounded heel resting on the carpet must not sink
            # into it, and the sole may press its own pedal face only as far as the tread.
            if not allow_floor:
                caps = leg_capsules(side, cand)
                if any(floor_clear(floor, a_, b_, r_, 0.01)[0] < -FLOOR_TOL for _, a_, b_, r_ in caps[1:]): continue
                if capsule_clear(own, caps[2][1], caps[2][2], caps[2][3], 0.01)[0] < -PEDAL_CONTACT: continue
            best = (key, cand)
    return best[1] if best else None


def knee_angle(side, foot):
    a = J[side + '_hip'] - foot['knee']; b = foot['ankle'] - foot['knee']
    return math.degrees(a.angle(b))


def leg_capsules(side, foot):
    sole_up = Vector((-math.sin(math.radians(foot['pitch'])), 0, math.cos(math.radians(foot['pitch']))))
    lift = FOOT_HALF * sole_up
    return [(side + '_thigh', J[side + '_hip'], foot['knee'], S['thigh_radius']),
            (side + '_shank', foot['knee'], foot['ankle'] + lift, S['knee_radius']),
            (side + '_foot', foot['heel'] + lift, foot['toe'] + lift, FOOT_HALF)]


def measure(rudder, groups, yoke_poses):
    feet = {s: solve_foot(s, groups) for s in LEGS}
    res = dict(rudder=rudder, legs={})
    for side, foot in list(feet.items()):
        floor_ok = foot is not None
        if foot is None:                              # show where the foot WOULD have to go: through the floor
            foot = feet[side] = solve_foot(side, groups, allow_floor=True)
        if foot is None:
            res['legs'][side] = dict(reach=False, reason='no pedal-face point at shank length from the knee'); continue
        L = dict(reach=floor_ok and foot['err'] <= REACH_TOL, floor_admissible=floor_ok, residual_m=round(foot['err'], 4), knee_deg=round(knee_angle(side, foot), 1),
                 foot_pitch_deg=foot['pitch'], thigh_lift_deg=foot['lift'], ball=[round(v, 3) for v in foot['ball']], heel=[round(v, 3) for v in foot['heel']])
        fh = floor_height(groups['floor'][0], foot['heel']); L['heel_above_carpet_m'] = round(foot['heel'].z - fh, 4) if fh is not None else None
        caps = leg_capsules(side, foot); clear = {}
        for gname, (tree, _) in groups.items():
            if gname == 'yoke' or gname == 'seat': continue
            for cname, a, b, r in caps:
                d, loc = (floor_clear if gname == 'floor' else capsule_clear)(tree, a, b, r)
                own = gname in ('pedal:' + PEDAL_OF[side], 'pedal:' + TOE_OF[side])
                key = ('own_pedal' if own and cname.endswith('foot') else 'pedals' if gname.startswith('pedal:') else gname)
                if key not in clear or d < clear[key][0]: clear[key] = (d, cname, gname, loc)
        L['clearance_m'] = {k: dict(min=round(v[0], 4), part=v[1], against=v[2], at=[round(c, 3) for c in v[3]] if v[3] else None) for k, v in clear.items()}
        res['legs'][side] = L; res['legs'][side]['_caps'] = caps
    # yoke over its full travel against the legs at this rudder
    worst = None
    for column, wheel in yoke_poses:
        reset(); pose_rudder(rudder); pose_yoke(column, wheel)
        yt = bvh(world_tris_named('C172_Yoke_'))
        for side in LEGS:
            for cname, a, b, r in res['legs'][side].get('_caps', []):
                d, loc = capsule_clear(yt, a, b, r)
                if worst is None or d < worst[0]: worst = (d, cname, column, wheel, loc)
        torso = capsule_clear(yt, J['waist'], J['shoulder'], S['torso_half_depth'])
        if 'torso_yoke' not in res or torso[0] < res['torso_yoke']['min']:
            res['torso_yoke'] = dict(min=round(torso[0], 4), column=column, wheel=wheel)
    if rudder == 0 and worst and worst[0] < YOKE_CLEAR:
        limit = 0
        for deg in range(int(WHEEL_DEG), -1, -5):
            ok = True
            for column in (-1, 0, 1):
                for sgn in (-1, 1):
                    reset(); pose_rudder(0); pose_yoke(column, sgn * deg / WHEEL_DEG); yt = bvh(world_tris_named('C172_Yoke_'))
                    if any(capsule_clear(yt, a, b, r)[0] < YOKE_CLEAR for sd in LEGS for _, a, b, r in res['legs'][sd].get('_caps', [])): ok = False
            if ok: limit = deg; break
        res['wheel_deg_clearing_legs_at_any_column'] = limit
    res['yoke_legs'] = dict(min=round(worst[0], 4), part=worst[1], column=worst[2], wheel=worst[3], at=[round(c, 3) for c in worst[4]] if worst[4] else None) if worst else None
    reset(); pose_rudder(rudder)
    return res, feet


def world_tris_named(prefix):
    out = []
    for n, o in OB.items():
        if n.startswith(prefix) and o.type == 'MESH': out.extend(world_tris(o))
    return out


def pedal_heights(groups):
    floor = groups['floor'][0]; low = None
    for n in PEDALS:
        o = OB[n]
        for v in o.data.vertices:
            p = o.matrix_world @ v.co; h = floor_height(floor, p)
            if h is None: continue
            if low is None or p.z - h < low[0]: low = (p.z - h, n, p)
    return low


def pedal_pad_centre(name):
    o = OB[name]; vs = [o.matrix_world @ v.co for v in o.data.vertices]
    return Vector([(min(v[i] for v in vs) + max(v[i] for v in vs)) / 2 for i in range(3)]), min(v.z for v in vs)


YOKE_POSES = [(c, w / 9) for c in (-1, 0, 1) for w in range(-9, 10)]   # wheel every 10 deg: a horn's low point is not at 90
report = dict(model=MODEL, mannequin='Codex round-3 rigid-segment 1.778 m (lane/codex-eyepoints f05f1511 eyepoints_v3.py)',
              eye_blender=list(EYE), joints={k: [round(c, 4) for c in v] for k, v in J.items()}, poses={}, failures=[])
reset()
g0 = scene_groups()
carpet_top = max(v.z for tri, _ in g0['floor'][1] for v in tri)
pc, plow = pedal_pad_centre(PEDAL_OF['left'])
report['carpet_top_z'] = round(carpet_top, 4)
report['pilot_left_pedal'] = dict(centre=[round(c, 4) for c in pc], bottom_above_carpet_m=round(plow - carpet_top, 4))
seat_top = floor_height(bvh(g0['seat'][1]), HP + Vector((-0.05, 0, 0)))
report['cushion'] = dict(top_z=round(seat_top, 4) if seat_top else None, buttock_contact_z=round(HP.z - S['contact_to_h_point'], 4),
                         compression_m=round(seat_top - (HP.z - S['contact_to_h_point']), 4) if seat_top else None)


# ---- the seat against the mannequin (Ty, 2026-11-04: "the pilot should sit at the back of the seat with his back
# against the backrest") and against the cabin it moves through ---------------------------------------------------
SEAT_COMPRESSION_MAX = 0.015   # buttocks into the cushion: the mannequin's own allowance (pedals-v1 report)
BACK_GAP_MAX = 0.010           # torso to seatback, wherever the seatback is behind it: "resting", not floating
BACK_PRESS_MAX = 0.015         # torso or pelvis into the seatback foam
BACK_REST_SPAN = 0.20          # torso length, from the waist up, that must rest on the seatback within those limits


def islands(o):
    """Connected triangle islands of a joined mesh: [(bbox_min, bbox_max, material, tris)]."""
    me = o.data; me.calc_loop_triangles(); mw = o.matrix_world
    weld = {}; key = [weld.setdefault(tuple(round(c, 5) for c in v.co), len(weld)) for v in me.vertices]   # glTF splits at seams
    par = list(range(len(weld)))
    def root(i):                                      # i: a vertex index; union-find on its welded position
        k = key[i]
        while par[k] != k: par[k] = par[par[k]]; k = par[k]
        return k
    for t in me.loop_triangles:
        a = root(t.vertices[0])
        for v in t.vertices[1:]: par[root(v)] = a
    V = [mw @ v.co for v in me.vertices]; groups = {}
    for t in me.loop_triangles:
        tri = [V[i] for i in t.vertices]; mat = o.material_slots[t.material_index].material.name
        groups.setdefault(root(t.vertices[0]), []).append((tri, mat))
    out = []
    for g in groups.values():
        ps = [p for tri, _ in g for p in tri]
        out.append((Vector([min(p[k] for p in ps) for k in range(3)]), Vector([max(p[k] for p in ps) for k in range(3)]), g[0][1], g))
    return out


def seat_fit():
    st = OB['C172_Interior_static']
    up_tris = world_tris(st, lambda t, m: is_seat_upholstery(t, m) and centroid(t).y > 0)   # pilot's (left) seat
    tree = bvh(up_tris); aft = -Vector((up.z, 0, -up.x))                  # perpendicular to the torso, toward the back
    out = {}
    # Cushion: the buttock contact plane (HP - contact_to_h_point) against the cushion top under the pelvis.
    contact = HP.z - S['contact_to_h_point']; comp = []
    for dx in (-0.08, -0.06, -0.04, -0.02, 0.0, 0.02, 0.04, 0.06):
        for dy in (-0.06, 0.0, 0.06):
            hit = tree.ray_cast(Vector((HP.x + dx, HP.y + dy, HP.z)), Vector((0, 0, -1)))
            if hit[0] is not None: comp.append((hit[0].z - contact, dx, dy))
    at_hp = [c for c, dx, dy in comp if dx == 0 and dy == 0]
    out['cushion'] = dict(compression_at_h_point_m=round(at_hp[0], 4) if at_hp else None,
                          compression_max_m=round(max(c for c, _, _ in comp), 4) if comp else None,
                          compression_min_m=round(min(c for c, _, _ in comp), 4) if comp else None, samples=len(comp))
    # Back: rays from the torso axis straight back (perpendicular to the 12-degree torso), centre and +/-80 mm.
    gaps = []; top = J['shoulder'] - 0.06 * up; step = (top - J['waist']).length / 20
    for i in range(21):
        p = J['waist'].lerp(top, i / 20)
        for dy in (-0.08, 0.0, 0.08):
            hit = tree.ray_cast(p + Vector((0, dy, 0)), aft, 0.6)
            gaps.append((hit[3] - S['torso_half_depth'] if hit[0] is not None else None, round(p.z, 3), dy, i))
    held = [g for g in gaps if g[0] is not None]
    hz = sorted({g[1] for g in held})
    rest = 0                                            # consecutive heights from the waist up where all 3 rest
    for i in range(21):
        row = [g[0] for g in gaps if g[3] == i]
        if not all(g is not None and -BACK_PRESS_MAX <= g <= BACK_GAP_MAX for g in row): break
        rest = i + 1
    out['back'] = dict(torso_gap_max_m=round(max(g[0] for g in held), 4) if held else None,
                       torso_gap_min_m=round(min(g[0] for g in held), 4) if held else None,
                       resting_span_from_waist_m=round(max(rest - 1, 0) * step, 3) if rest else 0.0,
                       torso_length_m=round(20 * step, 3),
                       torso_gap_centre_m=[(g[1], round(g[0], 4)) for g in held if g[2] == 0.0],
                       supported_z=[hz[0], hz[-1]] if hz else None,
                       unsupported_samples=len(gaps) - len(held))
    # Pelvis: the same ray from the H-point; it meets whichever seat surface is behind the buttocks (the cushion top,
    # on a seat too far aft).
    pel = [tree.ray_cast(HP + Vector((0, dy, 0)), aft, 0.6) for dy in (-0.06, 0.0, 0.06)]
    pel = [h[3] - S['pelvis_half_depth'] for h in pel if h[0] is not None]
    out['back']['pelvis_gap_min_m'] = round(min(pel), 4) if pel else None
    # The seat may not run into the trim wheel, the pedestal, the fuel selector or the door armrests, either side.
    # Interior_static is one joined mesh, but every box in it is still its own island, so the seat and the armrest
    # are told apart by island (a per-triangle filter cannot: the seat back's bevel strips lie beside the armrest).
    isl = islands(st)
    lo = lambda i, k: i[0][k]; hi = lambda i, k: i[1][k]
    for side, sgn in (('L', 1), ('R', -1)):
        ay = lambda i: sorted((sgn * lo(i, 1), sgn * hi(i, 1)))
        is_arm = lambda i: 'saddle_vinyl' in i[2] and ay(i)[0] > 0.39 and ay(i)[1] < 0.53 and lo(i, 2) > -0.10 and hi(i, 2) < -0.01
        is_st = lambda i: (any(s_ in i[2] for s_ in SEAT_MATS) and not is_arm(i) and 0.085 < sgn * (lo(i, 1) + hi(i, 1)) / 2 < 0.46
                           and lo(i, 0) > -0.85 and hi(i, 0) < 0.12 and lo(i, 2) > -0.53 and hi(i, 2) < 0.32)
        seat = world_tris(OB['C172_Seat_base_' + side]) + [t for i in isl if is_st(i) for t in i[3]]
        obst = dict(trim_wheel=world_tris(OB['C172_Trim_wheel']), fuel_selector=world_tris(OB['C172_Fuel_selector']),
                    pedestal=[t for i in isl if 'charcoal' in i[2] and max(abs(lo(i, 1)), abs(hi(i, 1))) < 0.09 and lo(i, 0) > -0.6 and hi(i, 0) < 0.15 and hi(i, 2) < -0.15 for t in i[3]],
                    armrest=[t for i in isl if is_arm(i) for t in i[3]])
        st_tree = bvh(seat)
        out['interference_' + side] = {k: len(st_tree.overlap(bvh(v))) if v else None for k, v in obst.items()}
        out['interference_' + side]['_islands'] = dict(seat=sum(1 for i in isl if is_st(i)), armrest=sum(1 for i in isl if is_arm(i)))
    return out


report['seat'] = seat_fit()
FEET = {}
for rudder in (0, -1, 1):
    reset(); pose_rudder(rudder); g = scene_groups()
    r, feet = measure(rudder, g, YOKE_POSES); FEET[rudder] = feet
    low = pedal_heights(g); r['lowest_pedal_above_carpet'] = dict(min=round(low[0], 4), part=low[1])
    report['poses'][str(rudder)] = r
F = report['failures']
for key, r in report['poses'].items():
    tag = 'rudder %s' % {'0': 'neutral', '-1': 'full left', '1': 'full right'}[key]
    for side, L in r['legs'].items():
        caps = L.pop('_caps', None)
        if not L.get('reach'):
            F.append('%s: %s foot does not reach its pedal (%s)' % (tag, side, 'only through the floor' if L.get('floor_admissible') is False else L.get('residual_m', L.get('reason'))))
            if 'clearance_m' not in L: continue
        if key == '0' and not (KNEE_RANGE[0] <= L['knee_deg'] <= KNEE_RANGE[1]): F.append('%s: %s knee %.1f deg outside %s' % (tag, side, L['knee_deg'], KNEE_RANGE))
        if L['knee_deg'] > 165: F.append('%s: %s knee %.1f deg is a locked leg' % (tag, side, L['knee_deg']))
        c = L['clearance_m']
        for k in ('panel', 'pedals'):
            if k in c and c[k]['min'] < CLEAR: F.append('%s: %s %s %.1f mm from %s (%s)' % (tag, side, c[k]['part'], c[k]['min'] * 1000, k, c[k]['against']))
        if 'floor' in c and c['floor']['min'] < -FLOOR_TOL: F.append('%s: %s %s %.1f mm into the floor' % (tag, side, c['floor']['part'], -c['floor']['min'] * 1000))
        if 'own_pedal' in c and c['own_pedal']['min'] < -PEDAL_CONTACT: F.append('%s: %s foot %.1f mm into its own pedal' % (tag, side, -c['own_pedal']['min'] * 1000))
        if 'other' in c and c['other']['min'] < 0: report.setdefault('warnings', []).append('%s: %s %s %.1f mm into other cabin structure at %s' % (tag, side, c['other']['part'], -c['other']['min'] * 1000, c['other']['at']))
    if r['yoke_legs'] and r['yoke_legs']['min'] < YOKE_CLEAR: F.append('%s: yoke %.1f mm from %s at column %+d wheel %+.2f' % (tag, r['yoke_legs']['min'] * 1000, r['yoke_legs']['part'], r['yoke_legs']['column'], r['yoke_legs']['wheel']))
    if r['lowest_pedal_above_carpet']['min'] < PEDAL_FLOOR_MIN: F.append('%s: %s only %.1f mm above the carpet' % (tag, r['lowest_pedal_above_carpet']['part'], r['lowest_pedal_above_carpet']['min'] * 1000))
sf = report['seat']; cu, bk = sf['cushion'], sf['back']
if cu['compression_at_h_point_m'] is None or cu['compression_at_h_point_m'] < 0: F.append('seat: buttocks do not touch the cushion under the H-point (%s)' % cu['compression_at_h_point_m'])
if cu['compression_max_m'] is not None and cu['compression_max_m'] > SEAT_COMPRESSION_MAX: F.append('seat: buttocks %.1f mm into the cushion (mannequin allows %.0f)' % (cu['compression_max_m'] * 1000, SEAT_COMPRESSION_MAX * 1000))
if bk['torso_gap_max_m'] is None: F.append('seat: no seatback behind the torso at all')
else:
    if bk['resting_span_from_waist_m'] < BACK_REST_SPAN: F.append('seat: back rests on the seatback over only %.0f mm from the waist (want %.0f within %.0f mm; worst gap %.1f mm)' % (bk['resting_span_from_waist_m'] * 1000, BACK_REST_SPAN * 1000, BACK_GAP_MAX * 1000, bk['torso_gap_max_m'] * 1000))
    elif bk['torso_gap_max_m'] > BACK_GAP_MAX: report.setdefault('warnings', []).append('seat: above the resting span the torso is up to %.1f mm off the seatback (its rounded top edge)' % (bk['torso_gap_max_m'] * 1000))
    if bk['torso_gap_min_m'] < -BACK_PRESS_MAX: F.append('seat: back %.1f mm into the seatback' % (-bk['torso_gap_min_m'] * 1000))
if bk['pelvis_gap_min_m'] is None or not (-BACK_PRESS_MAX <= bk['pelvis_gap_min_m'] <= BACK_GAP_MAX): F.append('seat: pelvis to seatback %s m (want %.0f..+%.0f mm)' % (bk['pelvis_gap_min_m'], -BACK_PRESS_MAX * 1000, BACK_GAP_MAX * 1000))
for side in 'LR':
    for k, n in sf['interference_' + side].items():
        if k != '_islands' and n: F.append('seat %s: %d triangle pairs intersect the %s' % (side, n, k))
Path(OUT_JSON).write_text(json.dumps(report, indent=1) + '\n')
print(json.dumps({k: v for k, v in report.items() if k != 'joints'}, indent=1))


# ---- renders: the human in the seat, from the eye and in side cutaway -----------------------------------------
def arm(side, sign, grip):
    sh = J['shoulder'] + Vector((0, sign * S['shoulder_joint_half_width'], 0)); d = grip - sh; L = min(d.length, S['upper_arm'] + S['forearm'] - 1e-4)
    a = (S['upper_arm'] ** 2 - S['forearm'] ** 2 + L ** 2) / (2 * L); h = math.sqrt(max(S['upper_arm'] ** 2 - a ** 2, 0))
    pole = unit(Vector((0, sign * 0.5, -1)) - unit(d) * Vector((0, sign * 0.5, -1)).dot(unit(d)))
    return sh, sh + unit(d) * a + pole * h, sh + unit(d) * L


def add_capsule(name, a, b, r, mat):
    d = b - a; bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=r, depth=max(d.length, 1e-4), location=(a + b) / 2)
    o = bpy.context.object; o.rotation_euler = d.to_track_quat('Z', 'Y').to_euler(); o.name = name; o.data.materials.append(mat)
    for p in (a, b):
        bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=p, segments=24, ring_count=12); s = bpy.context.object; s.name = name + '_end'; s.data.materials.append(mat)


def build_human(feet):
    mat = bpy.data.materials.new('mannequin'); mat.use_nodes = True
    bs = mat.node_tree.nodes.get('Principled BSDF'); bs.inputs['Base Color'].default_value = (.85, .45, .22, 1); bs.inputs['Roughness'].default_value = .55
    made = []
    before = set(SC.objects)
    for side in LEGS:
        foot = feet[side]
        if foot is None: continue
        for cname, a, b, r in leg_capsules(side, foot): add_capsule('Mannequin_' + cname, a, b, r, mat)
    add_capsule('Mannequin_pelvis', HP + Vector((0, S['hip_joint_half_width'], 0)), HP - Vector((0, S['hip_joint_half_width'], 0)), S['pelvis_half_depth'] * .75, mat)
    add_capsule('Mannequin_torso', J['waist'], J['shoulder'] - 0.06 * up, S['torso_half_depth'], mat)
    add_capsule('Mannequin_shoulders', J['shoulder'] + Vector((0, .17, 0)), J['shoulder'] - Vector((0, .17, 0)), .06, mat)
    add_capsule('Mannequin_neck', J['shoulder'], J['skull_base'], S['neck_radius'], mat)
    bpy.ops.mesh.primitive_uv_sphere_add(radius=S['head_sphere_radius'], location=J['head_center'], segments=32, ring_count=16)
    bpy.context.object.name = 'Mannequin_head'; bpy.context.object.data.materials.append(mat)
    for side, sign, yoke in (('left', 1, 'C172_Yoke_L_roll'), ('right', -1, 'C172_Yoke_L_roll')):
        o = OB[yoke]; vs = [o.matrix_world @ v.co for v in o.data.vertices]
        grips = [v for v in vs if (v.y - o.matrix_world.translation.y) * sign > 0.11]
        if not grips: continue
        grip = sum(grips, Vector()) / len(grips)
        sh, el, wr = arm(side, sign, grip)
        add_capsule('Mannequin_%s_upper_arm' % side, sh, el, .045, mat); add_capsule('Mannequin_%s_forearm' % side, el, wr, .037, mat)
    return [o for o in SC.objects if o not in before]


def render(path, cam_loc, target, lens=None, ortho=None, hide=(), clip=0.01):
    for o in SC.objects: o.hide_render = any(o.name.startswith(h) for h in hide)
    cd = bpy.data.cameras.new('cam'); cam = bpy.data.objects.new('cam', cd); SC.collection.objects.link(cam)
    cam.location = cam_loc; cam.rotation_euler = (target - cam_loc).to_track_quat('-Z', 'Y').to_euler()
    if ortho: cd.type = 'ORTHO'; cd.ortho_scale = ortho
    else: cd.lens = lens
    cd.clip_start = clip; SC.camera = cam; SC.render.filepath = path; bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


def yoke_renders(exterior):
    """yoke-v1 review set (C172_YOKE_RENDERS=1): the yoke at neutral and at full wheel each way with the column full
    aft and full forward, rudder neutral, the mannequin's arms on the grips. Per pose: the seat eye; a front view from
    section looking aft from body x 0.075 (orthographic, so the horn-to-thigh gap reads true); a left side cutaway
    with the left leg and arm not drawn, so the right thigh under the right horn can be seen. Then the panel from the
    eye and the suction gauge close up, without the mannequin."""
    SC.cycles.samples = 32; SC.render.resolution_x, SC.render.resolution_y = 1600, 1067
    poses = [('neutral', 0, 0), ('aft-left', -1, -1), ('aft-right', -1, 1), ('fore-left', 1, -1), ('fore-right', 1, 1)]
    body = ('Mannequin_head', 'Mannequin_neck', 'Mannequin_torso', 'Mannequin_shoulders', 'Mannequin_pelvis')
    def posed(column, wheel):
        for o in [o for o in SC.objects if o.name.startswith('Mannequin')]: bpy.data.objects.remove(o)
        reset(); pose_rudder(0); pose_yoke(column, wheel); build_human(FEET[0])
    out = lambda tag: os.path.join(RENDER_DIR, '%s_%s.png' % (PREFIX, tag))
    for tag, column, wheel in poses:
        posed(column, wheel)
        render(out(tag + '_eye'), EYE.copy(), Vector((0.36, 0.28, -0.30)), lens=15, hide=exterior + body)
        # Section: the camera clips everything forward of body x 0.075 (just ahead of the wheel at full forward), so
        # the knees and the panel do not hide the horn. Looking aft: the pilot's LEFT is on the RIGHT of the picture.
        render(out(tag + '_section'), Vector((0.27, 0.337, -0.02)), Vector((-0.6, 0.337, -0.02)), ortho=0.75, hide=exterior, clip=0.195)
    for o in [o for o in SC.objects if o.name.startswith('Mannequin')]: bpy.data.objects.remove(o)
    reset(); pose_rudder(0)
    render(out('panel_eye'), EYE.copy(), Vector((0.30, 0.03, 0.17)), lens=19, hide=exterior)
    render(out('suction_closeup'), Vector((0.215, 0.321, 0.150)), Vector((0.2975, 0.321, 0.140)), lens=55, hide=exterior)
    st = OB['C172_Interior_static']; me = st.data
    import bmesh
    bm = bmesh.new(); bm.from_mesh(me); mw = st.matrix_world
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if (mw @ f.calc_center_median()).y > 0.45], context='FACES'); bm.to_mesh(me); bm.free()
    for tag, column, wheel in poses:
        posed(column, wheel)
        render(out(tag + '_side'), Vector((-0.05, 2.5, 0.0)), Vector((-0.05, 0.0, 0.0)), ortho=1.05,
               hide=exterior + ('C172_Door_L', 'C172_Headliner', 'C172_Compass', 'Mannequin_left_'))


if RENDER_DIR:
    os.makedirs(RENDER_DIR, exist_ok=True)
    reset(); pose_rudder(0); human = build_human(FEET[0])
    SC.render.engine = 'CYCLES'; SC.cycles.device = 'CPU'; SC.cycles.samples = 48
    SC.render.resolution_x, SC.render.resolution_y = 1800, 1200
    SC.view_settings.view_transform = 'AgX'
    SC.world = bpy.data.worlds.new('w'); SC.world.color = (.45, .47, .5)
    for n, p, e, s in [('Key', (-.6, .9, 1.6), 260, 2.2), ('Fill', (.6, -1.4, .8), 140, 2.0), ('Fwd', (1.4, .3, .9), 160, 1.5)]:
        ld = bpy.data.lights.new(n, 'AREA'); ld.energy = e; ld.size = s; lo = bpy.data.objects.new(n, ld); SC.collection.objects.link(lo)
        lo.location = p; lo.rotation_euler = (Vector((0, .3, -.2)) - Vector(p)).to_track_quat('-Z', 'Y').to_euler()
    exterior = ('Fuselage', 'Glass', 'Wing', 'Strut', 'Flap', 'Aileron', 'HStab', 'Elevator', 'Rudder', 'Fin', 'Gear', 'Prop', 'Blade', 'Spinner', 'Wheel')
    if os.environ.get('C172_YOKE_RENDERS') == '1':
        yoke_renders(exterior); RENDER_DIR = None
if RENDER_DIR:
    # Seat eye. The head, neck and torso would wrap the camera, so they are not drawn; arms, legs and feet are.
    # The closed outer skin is not drawn either (as in c172_cockpit.py's review renders), or it shuts out the light.
    render(os.path.join(RENDER_DIR, PREFIX + '_eye.png'), EYE.copy(), Vector((0.42, 0.30, -0.36)), lens=15,
           hide=exterior + ('Mannequin_head', 'Mannequin_neck', 'Mannequin_torso', 'Mannequin_shoulders', 'Mannequin_pelvis'))
    # Side cutaway from the pilot's (left) side: the outer skin, glazing and wing are not drawn, and the
    # left cabin wall (door, armrest, pull, pillars) is cut away so the legs can be seen on the pedals.
    st = OB['C172_Interior_static']; me = st.data
    import bmesh
    bm = bmesh.new(); bm.from_mesh(me); mw = st.matrix_world
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if (mw @ f.calc_center_median()).y > 0.45], context='FACES'); bm.to_mesh(me); bm.free()
    hide = exterior + ('C172_Door_L', 'C172_Headliner', 'C172_Compass')
    render(os.path.join(RENDER_DIR, PREFIX + '_side.png'), Vector((-0.10, 2.5, 0.06)), Vector((-0.10, 0.0, 0.06)), ortho=1.95, hide=hide)

for w in report.get('warnings', []): print('  WARN', w)
print('PEDAL FIT', 'FAIL' if F else 'PASS')
for f in F: print('  FAIL', f)
sys.exit(1 if F else 0)
