"""Extra 300 cockpit pass 15 (iOS/Mac and Quest, one GLB): Ty's 2026-11-05 05:07 spec, EXTRA 300 section + ADDENDUM 05:08.

  "Throttles are way too high: put them down on the left at a natural bent-elbow position."
      -> Both levers keep their geometry and sweep (idle 25-30 deg aft of vertical, +50 deg to full) and move so the
         idle grip sits where the rear one now does relative to its seat's SHOULDER (FlyingGame.Core.Vr.SeatedPilot:
         0.23 below and 0.08 behind the eye, 0.17 to the side): 0.32 m ahead of it, 0.23 m below it, 0.10 m outboard.
         Shoulder to grip is 0.40 m at idle and ~0.50 m at full for a 0.65 m arm: the elbow stays bent through the
         sweep. Rear: the lever and its quadrant 0.10 m lower. Front: the lever came off the top of the left console
         (z 0.19, which is why its knob stood 0.42 m up, level with the sill) to the console's inboard face, with a
         quadrant plate like the rear's.
  "FRONT throttle is a plain black round knob, not the pistol grip (rear stays the pistol grip)."
      -> the front pistol grip, its button and its arm extension removed; a 38 mm (1-1/2 in) black ball on the arm.
  "Fuel valve rod currently runs the wrong way (toward the tail). It must run FORWARD to the engine firewall past the
   front seat, with handles in both seats."
      -> Extra_FuelValve_SharedRod now runs from the rear handle (its aft end) forward past the front handle to the
         valve body on the firewall (the footwell's forward face, x +0.60), guides along the right wall. Both
         handles stay on the one rod and the one param (`fuel_valve` = Aircraft.FuelValveOpen).
  "Front panel: an IAC card somewhere visible, matched to the rear."
      -> FrontPanel_SequenceCard_Face / _Stock / _Holder / _Clip: the rear holder's parts (108 x 140 mm, half-letter
         4.25 x 5.5 in portrait) on a grey extension plate to the pilot's RIGHT of the front sub-panel (the throttle
         is on the left). Same card material as the rear; the shared selection is lane/claude-cockpit-common's.
  ADDENDUM "the front-seat view shows a GAP between the canopy and the airplane. Close it."
      -> Measured first: the canopy glass rim touches the sill / frame / skin everywhere (0.0 mm), and no ray from
         either eye leaves the airframe. What the pilot saw is the INSIDE of the outer skin: Fuselage_Painted, the
         sill and the frame are double-sided with the exterior livery, and the dark cockpit liner (Cockpit_Shell)
         stops 6-13 cm below the sill, so a band of white-and-red paint ran along both sides and into the corners
         beside the front panel, reading as daylight under the canopy. Cockpit_SideLiner_L / _R (cockpit paint) now
         line the skin from below the consoles' tops up to the sill, the full length of both cockpits.
         check_extra_canopy_seal.py and ExtraCanopySealTests hold both: rim gap < 3 mm, and no exterior livery
         seen from either eye below the canopy.

Runs after floor_windows.py (pass 14), in place on all three liveries; the rig table edited once.
Idempotent (extras.extraCockpitV3). Builder metres, x forward, y LEFT, z up.

  PYTHONDONTWRITEBYTECODE=1 /usr/bin/python3 tools/own_models/extra_cockpit_v3.py
"""
import copy
import json
import math
import numpy as np

from vr_cockpit import Glb, Shape, MODELS, ROOT, gl, builder
from vr_phase2e import compact
from extra_panel_p3f import idx, tris_of
from extra_cockpit_shared import mat_index
from extra_front_seat import node_world, set_parts, set_local, new_root
from drop_card_slot import validate

EYE = {'Rear': np.array((-1.622, 0.0, .526)), 'Front': np.array((-.703, 0.0, .537))}   # configs eyes as placed
SHOULDER_FROM_EYE = np.array((-.08, .17, -.23))                                          # SeatedPilot.ShoulderL
# The idle grip relative to the seat's left shoulder (builder): the rear grip lowered 0.10 m.
GRIP_FROM_SHOULDER = np.array((.317, .108, -.227))
THROTTLE_Y = {'Rear': .278, 'Front': .272}       # lever planes: inboard of the consoles' faces (y .297)
REAR_DROP = -.100
FRONT_KNOB_R = .019
# Fuel valve rod (extra_front_seat.VALVE_*): right wall, forward to the firewall.
VALVE_Y, VALVE_Z = -.284, .045
ROD_AFT, FIREWALL_X = -1.412, .600
GUIDES_X = (-1.43, -1.10, -.80, -.47, -.15, .20, .52)
# Front card: face 2.5 mm aft of the plate, centred to the pilot's right of the sub-panel.
CARD_W, CARD_H = .108, .140
CARD_C = np.array((-.1525, -.195, .275))
PLATE = dict(x0=-.150, x1=-.142, y0=-.118, y1=-.262, z0=.192, z1=.358)
# Liner: from LINER_ZLO (behind / below the console tops) to the sill, inset from the skin.
LINER_X = (-2.170, .600)          # rear of the canopy to the firewall
FIREWALL_PLATE_X = .605            # closes the engine bay's corners beside the footwell (they showed the skin past the engine)
LINER_ZLO, LINER_INSET, LINER_TOP_GAP, LINER_ZMAX = -.120, .005, .003, .330
CARD_PARTS = ('SequenceCard_Face', 'SequenceCard_Stock', 'SequenceCard_Holder', 'SequenceCard_Clip')


def world_tris(g, name):
    W = g.world_matrices(); return tris_of(g, W, idx(g, name))


def section(tris, x, side):
    """The mesh's cut at station x on one side (sign of y): segments [((y0, z0), (y1, z1))]."""
    tris = tris[(tris[:, :, 0].min(1) <= x) & (tris[:, :, 0].max(1) >= x)]
    d = tris[:, :, 0] - x; P = []
    for a, c in ((0, 1), (1, 2), (2, 0)):
        cr = (d[:, a] < 0) != (d[:, c] < 0)
        s = np.where(cr, d[:, a] / np.where(cr, d[:, a] - d[:, c], 1), 0)
        P.append((tris[:, a] + s[:, None] * (tris[:, c] - tris[:, a]), cr))
    segs = []
    for k in range(len(tris)):
        q = [p[k][1:] for p, cr in P if cr[k]]
        if len(q) == 2 and side * q[0][0] > .15 and side * q[1][0] > .15: segs.append((q[0], q[1]))
    return segs


def halfwidth_grid(skin, xs, zs):
    """The skin's outer half-width at each (x, z) of a grid (left side; the airframe is symmetric), nan where none."""
    out = np.full((len(xs), len(zs)), np.nan)
    for i, x in enumerate(xs):
        segs = section(skin, x, 1)
        for j, z in enumerate(zs):
            y = skin_y_at(segs, z)
            if y is not None: out[i, j] = y
    return out


def clip_wing_inside(g):
    """Drop the Wing triangles wholly inside the fuselage: the wing's skin ran through the front cockpit, its livery
    showing ahead of the consoles (seen from the front eye as white and red under the canopy)."""
    skin = world_tris(g, 'Fuselage_Painted')
    xs = np.arange(-1.36, .52, .01); zs = np.arange(-.12, .26, .005)
    hw = halfwidth_grid(skin, xs, zs)
    i = idx(g, 'Wing'); M = g.world_matrices()[i]; mesh = g.doc['meshes'][g.doc['nodes'][i]['mesh']]; dropped = snapped = 0
    for p in mesh['primitives']:
        v = np.asarray(g.array(p['attributes']['POSITION']), float) @ M[:3, :3].T + M[:3, 3]
        b = np.stack([-v[:, 0], v[:, 2], v[:, 1]], 1)
        ids = np.asarray(g.array(p['indices'])).reshape(-1, 3) if 'indices' in p else np.arange(len(b)).reshape(-1, 3)
        xi = np.clip(np.round((b[:, 0] - xs[0]) / .01).astype(int), 0, len(xs) - 1)
        zi = np.clip(np.round((b[:, 2] - zs[0]) / .005).astype(int), 0, len(zs) - 1)
        inside_v = (b[:, 0] >= xs[0]) & (b[:, 0] <= xs[-1]) & (b[:, 2] >= zs[0]) & (b[:, 2] <= zs[-1]) & (np.abs(b[:, 1]) < np.nan_to_num(hw[xi, zi], nan=-1) - .004)
        keep = ~inside_v[ids].all(1); dropped += int((~keep).sum())
        # A root triangle that straddles the skin keeps its outside part: its inside corners go out onto the skin
        # (the wing then ends at the fuselage; dropping it would open a hole up to 14 cm outside).
        kept = ids[keep]; used = np.unique(kept)
        mv = used[inside_v[used]]
        b2 = b.copy(); b2[mv, 1] = np.sign(b[mv, 1]) * (hw[xi[mv], zi[mv]] + .001); snapped += len(mv)
        g2 = np.stack([-b2[:, 0], b2[:, 2], b2[:, 1]], 1)
        loc = (g2 - M[:3, 3]) @ np.linalg.inv(M[:3, :3]).T
        remap = -np.ones(len(b), int); remap[used] = np.arange(len(used))
        for k, acc in list(p['attributes'].items()):
            assert g.doc['accessors'][acc]['componentType'] == 5126, k
            arr = loc[used] if k == 'POSITION' else np.asarray(g.array(acc))[used]
            p['attributes'][k] = g.accessor(arr.astype(np.float32), {1: 'SCALAR', 2: 'VEC2', 3: 'VEC3', 4: 'VEC4'}[arr.shape[1] if arr.ndim > 1 else 1])
        p['indices'] = g.accessor(remap[kept].reshape(-1).astype(np.uint32), 'SCALAR', 5125)
    return dropped, snapped


def skin_y_at(segs, z):
    """Outermost |y| where the cut crosses height z (None if it does not)."""
    ys = [abs(a[0] + (z - a[1]) / (b[1] - a[1]) * (b[0] - a[0])) for a, b in segs if (a[1] - z) * (b[1] - z) <= 0 and a[1] != b[1]]
    return max(ys) if ys else None


def liner(g, side):
    """The skin's inner face from LINER_ZLO to the sill, inset, as a strip of stations x rows."""
    skin = world_tris(g, 'Fuselage_Painted')
    skin = skin[(skin[:, :, 0].max(1) > LINER_X[0] - .05) & (skin[:, :, 0].min(1) < LINER_X[1] + .05) & (skin[:, :, 2].max(1) > LINER_ZLO - .05)]
    xs = np.arange(LINER_X[0], LINER_X[1] + 1e-6, .02); rows = []; tops = []
    for x in xs:
        segs = section(skin, x, side)
        if not segs: continue
        # The sill: the highest point of the skin's side, where the glass and the frame sit on it.
        zt = max(max(a[1], b[1]) for a, b in segs if max(abs(a[0]), abs(b[0])) > .25) - LINER_TOP_GAP
        zt = min(zt, LINER_ZMAX)
        zs = list(np.linspace(LINER_ZLO, zt, 14)); row = []
        for z in zs:
            yy = skin_y_at(segs, z)
            if yy is None: row = None; break
            row.append((x, side * (yy - LINER_INSET), z))
        if row: rows.append(row); tops.append(zt)
    sh = Shape(); n = len(rows[0])
    for a, b in zip(rows[:-1], rows[1:]):
        base = len(sh.v); sh.v += [np.array(v) for v in a + b]
        for j in range(n - 1):
            sh.t += [(base + j, base + n + j, base + n + j + 1), (base + j, base + n + j + 1, base + j + 1)]
    V = np.array(sh.v)
    for k, t in enumerate(sh.t):                    # normals inboard (toward y = 0)
        nrm = np.cross(V[t[1]] - V[t[0]], V[t[2]] - V[t[0]])
        if nrm[1] * side > 0: sh.t[k] = (t[0], t[2], t[1])
    return sh, (min(tops), max(tops)), len(rows)


def firewall_plate(g):
    """The skin's section just forward of the footwell's face, filled: the firewall behind the front pilot's feet."""
    skin = world_tris(g, 'Fuselage_Painted'); x = FIREWALL_PLATE_X
    sl, sr = section(skin, x, 1), section(skin, x, -1)
    zs = [z for z in np.arange(-.60, .45, .01) if skin_y_at(sl, z) and skin_y_at(sr, z)]
    sh = Shape()
    for z0, z1 in zip(zs[:-1], zs[1:]):
        a = [(x, skin_y_at(sl, z0) + .003, z0), (x, -(skin_y_at(sr, z0) + .003), z0), (x, -(skin_y_at(sr, z1) + .003), z1), (x, skin_y_at(sl, z1) + .003, z1)]
        sh.add([np.array(q) for q in a], [(0, 2, 1), (0, 3, 2)])          # facing aft, toward the pilot
    return sh, (zs[0], zs[-1])


def lathe_knob(c, r):
    sh = Shape(); sh.sphere(c, r, 28, 16); return sh


def update(path, doc):
    g = Glb(path)
    if g.doc['extras'].get('extraCockpitV3'): print('EXTRA', path.name, 'already done'); return False
    assert g.doc['extras'].get('extraFrontSeat') and g.doc['extras'].get('floorWindows'), path
    report = {}
    cabin = mat_index(g, 'Paint_cabin'); black = mat_index(g, 'Cockpit_black'); metal = mat_index(g, 'P3_ControlMetal')
    nut = mat_index(g, 'P3_ControlBlack'); grey = mat_index(g, 'P3b_Panel')

    # ---------------- CANOPY SEAL: the side liners ----------------
    for side, tag in ((1, 'L'), (-1, 'R')):
        sh, (zt0, zt1), n = liner(g, side)
        g.mesh('Cockpit_SideLiner_' + tag, [gl(v) for v in sh.v], sh.t, cabin)
        report['liner_' + tag] = dict(stations=n, top_z=[round(zt0, 3), round(zt1, 3)])
    sh, zr = firewall_plate(g); g.mesh('Cockpit_FirewallPlate', [gl(v) for v in sh.v], sh.t, cabin)
    report['firewall_plate_z'] = [round(float(z), 3) for z in zr]
    report['wing_inside_fuselage'] = dict(zip(('tris_dropped', 'root_vertices_onto_skin'), clip_wing_inside(g)))

    # ---------------- THROTTLES ----------------
    rig = {e['node']: e for e in doc['rig'] if e['node'] in ('Throttle_F', 'Throttle_R')}
    # Front: the pistol grip off, a plain black ball on the arm (before the lever moves).
    i = idx(g, 'Throttle_F'); M = node_world(g, 'Throttle_F'); Mi = np.linalg.inv(M)
    mesh = g.doc['meshes'][g.doc['nodes'][i]['mesh']]; prims = mesh['primitives']
    names = [g.doc['materials'][p['material']]['name'] for p in prims]
    assert names == ['Cockpit_quadrant', 'Cockpit_stick', 'Cockpit_black', 'P3h_GripButton', 'Cockpit_stick'], names
    K = np.array(g.doc['extras']['extraFrontSeat']['report']['Throttle_F']['knob_was'], float)   # the arm's end
    ball = lathe_knob(K, FRONT_KNOB_R)
    pp = np.array([(Mi @ np.append(gl(v), 1.0))[:3] for v in ball.v]); t = np.array(ball.t).reshape(-1, 3); flat = pp[t].reshape(-1, 3)
    nrm = np.cross(pp[t[:, 1]] - pp[t[:, 0]], pp[t[:, 2]] - pp[t[:, 0]]); nrm /= np.maximum(np.linalg.norm(nrm, axis=1)[:, None], 1e-10)
    mesh['primitives'] = prims[:2] + [dict(attributes=dict(POSITION=g.accessor(flat, 'VEC3'), NORMAL=g.accessor(np.repeat(nrm, 3, axis=0), 'VEC3')),
                                           material=black, mode=4)]
    set_local(g, 'Grip_Throttle_Front', K)
    P_old = builder(np.array(rig['Throttle_F']['pivot_gltf']))
    target = EYE['Front'] + SHOULDER_FROM_EYE + GRIP_FROM_SHOULDER; target[1] = THROTTLE_Y['Front']
    d_front = target - K
    P_front = P_old + d_front
    # Rear: same lever, 0.10 m lower (its idle grip defines GRIP_FROM_SHOULDER).
    Pr_old = builder(np.array(rig['Throttle_R']['pivot_gltf'])); d_rear = np.array((0., 0., REAR_DROP))
    gr_old = node_world(g, 'Grip_Throttle_Rear')[:3, 3]; gr_old = builder(gr_old)
    for nm, P in (('Throttle_F', P_front), ('Throttle_R', Pr_old + d_rear)):
        set_local(g, nm, P); rig[nm]['pivot_gltf'] = gl(P).round(4).tolist()
        ex = g.doc['nodes'][idx(g, nm)].get('extras', {})
        if 'pivot_gltf' in ex: ex['pivot_gltf'] = rig[nm]['pivot_gltf']
    q = idx(g, 'Throttle_Quadrant_R_P3e'); qn = g.doc['nodes'][q]
    qn['translation'] = (np.array(qn.get('translation', [0, 0, 0]), float) + gl(d_rear)).tolist()
    # Front quadrant plate: the rear's, carried to the front lever and set flush on the console face.
    qt = world_tris(g, 'Throttle_Quadrant_R_P3e').reshape(-1, 3)
    shift = P_front - (Pr_old + d_rear); shift[1] = (THROTTLE_Y['Front'] + .016) - (qt[:, 1].min())
    qf = Shape(); qf.add([v + shift for v in qt], [(3 * k, 3 * k + 1, 3 * k + 2) for k in range(len(qt) // 3)])
    g.mesh('Throttle_Quadrant_F_V3', [gl(v) for v in qf.v], qf.t, mat_index(g, 'Cockpit_quadrant'))
    # The front lever's old quadrant box on top of the console (Cockpit_Interior), orphaned now the lever is off it.
    ci = idx(g, 'Cockpit_Interior'); Wc = g.world_matrices()[ci]
    for p in g.doc['meshes'][g.doc['nodes'][ci]['mesh']]['primitives']:
        v = np.asarray(g.array(p['attributes']['POSITION']), float) @ Wc[:3, :3].T + Wc[:3, 3]; b = np.stack([-v[:, 0], v[:, 2], v[:, 1]], 1)
        ids = np.asarray(g.array(p['indices'])).reshape(-1, 3); c = b[ids].mean(1)
        keep = ~((c[:, 0] > -.55) & (c[:, 0] < -.33) & (c[:, 1] > .30) & (c[:, 2] > .18))
        assert (~keep).sum() == 12, (~keep).sum()
        p['indices'] = g.accessor(ids[keep].reshape(-1).astype(np.uint32), 'SCALAR', 5125)
    report['front_old_quadrant_box_removed'] = True
    gf = node_world(g, 'Grip_Throttle_Front')[:3, 3]; gr = node_world(g, 'Grip_Throttle_Rear')[:3, 3]
    for seat, gw in (('Front', builder(gf)), ('Rear', builder(gr))):
        sh = EYE[seat] + SHOULDER_FROM_EYE
        report['throttle_' + seat] = dict(idle_grip=np.round(gw, 3).tolist(), from_shoulder=np.round(gw - sh, 3).tolist(),
                                          shoulder_to_grip_m=round(float(np.linalg.norm(gw - sh)), 3))
    report['throttle_Rear']['was'] = np.round(gr_old, 3).tolist()
    report['throttle_Front']['was'] = np.round(K, 3).tolist()

    # ---------------- FUEL VALVE ROD: forward to the firewall ----------------
    rod = Shape(); rod.tube((ROD_AFT, VALVE_Y, VALVE_Z), (FIREWALL_X - .030, VALVE_Y, VALVE_Z), .005, 12)
    guides = Shape()
    for xx in GUIDES_X:
        guides.tube((xx - .012, VALVE_Y, VALVE_Z), (xx + .012, VALVE_Y, VALVE_Z), .010, 16)
        guides.box((xx, VALVE_Y - .009, VALVE_Z), (.025, .010, .030))
    body = Shape()                                            # the shut-off valve on the firewall, lines through it
    body.box((FIREWALL_X - .020, VALVE_Y, VALVE_Z), (.040, .045, .045))
    body.tube((FIREWALL_X - .041, VALVE_Y, VALVE_Z), (FIREWALL_X - .030, VALVE_Y, VALVE_Z), .012, 16)
    body.tube((FIREWALL_X - .020, VALVE_Y, VALVE_Z + .022), (FIREWALL_X - .020, VALVE_Y, VALVE_Z + .075), .0055, 12)
    body.tube((FIREWALL_X - .020, VALVE_Y, VALVE_Z + .075), (FIREWALL_X + .002, VALVE_Y, VALVE_Z + .075), .0055, 12)
    body.tube((FIREWALL_X - .020, VALVE_Y + .022, VALVE_Z), (FIREWALL_X - .020, VALVE_Y + .060, VALVE_Z), .0055, 12)
    body.tube((FIREWALL_X - .020, VALVE_Y + .060, VALVE_Z), (FIREWALL_X + .002, VALVE_Y + .060, VALVE_Z), .0055, 12)
    flange = Shape(); flange.box((FIREWALL_X - .002, VALVE_Y + .02, VALVE_Z + .03), (.004, .11, .11))
    for nm, shp, mat in (('Extra_FuelValve_SharedRod', rod, metal), ('Extra_FuelValve_RodGuides', guides, nut)):
        set_parts(g, nm, [(mat, shp, None)])
    new_root(g, 'Extra_FuelValve_Body', (FIREWALL_X - .02, VALVE_Y, VALVE_Z))
    set_parts(g, 'Extra_FuelValve_Body', [(mat_index(g, 'Cockpit_knob_red'), Shape(), None), (metal, body, None), (nut, flange, None)])
    report['fuel_rod'] = dict(aft_x=ROD_AFT, fwd_x=FIREWALL_X, y=VALVE_Y, z=VALVE_Z, guides=list(GUIDES_X))

    # ---------------- FRONT IAC CARD ----------------
    face_c = builder(node_world(g, 'SequenceCard_Face')[:3, 3])
    fv = world_tris(g, 'SequenceCard_Face').reshape(-1, 3); rc = (fv.min(0) + fv.max(0)) / 2
    size = fv.max(0) - fv.min(0)
    assert abs(size[1] - CARD_W) < .002 and abs(size[2] - CARD_H) < .002, size
    d = CARD_C - rc
    roots = g.doc['scenes'][0]['nodes']
    for nm in CARD_PARTS:
        src = g.doc['nodes'][idx(g, nm)]; assert 'children' not in src, nm
        par = next((j for j, n in enumerate(g.doc['nodes']) if idx(g, nm) in n.get('children', [])), None)
        n2 = copy.deepcopy(src); n2['name'] = 'FrontPanel_' + nm
        g.doc['nodes'].append(n2); k = len(g.doc['nodes']) - 1
        if par is None: roots.append(k)
        else: g.doc['nodes'][par]['children'].append(k)
        Wp = g.world_matrices()[par] if par is not None else np.eye(4)
        loc = np.linalg.inv(Wp)[:3, :3] @ gl(d)                # the displacement in the parent's frame
        n2['translation'] = (np.array(src.get('translation', [0, 0, 0]), float) + loc).tolist()
        n2.setdefault('extras', {})['mirrors'] = nm
    plate = Shape(); p = PLATE
    plate.box(((p['x0'] + p['x1']) / 2, (p['y0'] + p['y1']) / 2, (p['z0'] + p['z1']) / 2), (p['x1'] - p['x0'], abs(p['y1'] - p['y0']), p['z1'] - p['z0']))
    g.mesh('FrontPanel_SequenceCard_Plate', [gl(v) for v in plate.v], plate.t, grey)
    fc = world_tris(g, 'FrontPanel_SequenceCard_Face').reshape(-1, 3)
    report['front_card'] = dict(centre=np.round((fc.min(0) + fc.max(0)) / 2, 4).tolist(), size_mm=np.round((fc.max(0) - fc.min(0)) * 1000, 1).tolist())
    cc = (fc.min(0) + fc.max(0)) / 2
    if not any(sl.get('node') == 'FrontPanel_SequenceCard_Face' for sl in doc['card_slots']):
        rear = next(sl for sl in doc['card_slots'] if sl['node'] == 'SequenceCard_Face')
        sl = copy.deepcopy(rear); sl.update(node='FrontPanel_SequenceCard_Face', centre_gltf=gl(cc).round(4).tolist(),
                                            note='extra_cockpit_v3: front seat, right of the sub-panel; shows the rear selection (Ty 2026-11-05)')
        sl.pop('holder_top_above_dash_m', None); sl['parent'] = rear['parent']
        doc['card_slots'].append(sl)
    g.doc['extras']['extraCockpitV3'] = dict(
        owner="Ty 2026-11-05 05:07 EXTRA 300 + ADDENDUM 05:08: throttles low and left at a bent elbow, front throttle a black "
              "ball, fuel rod forward to the firewall, front IAC card, canopy sealed to the fuselage (side liners)",
        grip_from_shoulder=GRIP_FROM_SHOULDER.tolist(), throttle_y=THROTTLE_Y, rear_drop=REAR_DROP, front_knob_r=FRONT_KNOB_R,
        fuel_rod=report['fuel_rod'], card_centre=CARD_C.tolist(), report=report)
    compact(g); validate(g, path); g.save(path)
    print('EXTRA', path.name, json.dumps(report))
    return True


if __name__ == '__main__':
    rigpath = ROOT / 'docs/models/ea300-own-rig.json'
    base = rigpath.read_text(); doc = json.loads(base); wrote = False
    for k, p in enumerate(sorted((MODELS / 'extra-300-like').glob('*own*.glb'))):
        wrote |= update(p, doc if k == 0 else json.loads(base))
    if wrote: rigpath.write_text(json.dumps(doc, indent=1) + '\n')
