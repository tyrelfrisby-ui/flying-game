"""Extra 300 cockpit pass 13 (iOS/Mac and Quest, one GLB): the front seat stripped, the rear panel's switches labelled,
the verniers on the left wall, a pistol-grip throttle and the linked fuel shut-off. Ty 2026-11-04 19:05-19:14:

  "the front seat of the Extra just has altitude, g meter and airspeed, and a throttle and stick and rudder, nothing
   else."  -> front: a small sub-panel with three 2-1/4 in instruments, L->R AIRSPEED, G-METER, ALTIMETER; every other
   gauge, radio head, LCD, switch and knob of the front seat retired (nodes kept, meshes dropped, out of the rig and of
   the hands' `controls`). Throttle, stick, pedals, canopy handles, harness and jack are untouched.
  "the Extra's fuel ON/OFF valve is the same type as the Great Lakes' ... on the RIGHT side, rotates 90 degrees, with a
   lever in BOTH the front and rear cockpits, linked"  -> the two old fuel-selector rotaries become the GL valve: a red
   handle ON down, OFF 90 deg inboard, on one fore/aft torque rod with guides along the right wall; param `fuel_valve`
   (Aircraft.FuelValveOpen, a real shut-off).
  Rear panel: grey (the AirSpace 300L's); ONLY six switches, labelled on placards beneath: MASTER, ALT, FUEL PUMP | NAV,
   STROBE, SMOKE (an ALTERNATOR toggle added, cloned from MASTER); magneto, alternate air, trim and landing light gone;
   no unlabelled knobs (the COM/XPDR heads keep their own knobs: they are the radios, legended on the head); EGT/CHT
   added (2-1/4 in, lower right). Rear diagonal tubes removed by Ty 2026-11-05.
  Prop and mixture: vernier push-pulls stacked vertically on the left wall BESIDE the throttle, outboard of it, knobs
   aft (prop blue above, mixture red below), IN = forward.
  Throttle (both seats): a ribbed black pistol grip with a rubber boot and an end button, canted aft from the arm
   (Extra 330LX photo, Wikimedia "Extra_330_LX_cockpit.jpg").

Runs after extra_cockpit_shared.py, in place on all three liveries; the shared rig table edited once (first livery).
Idempotent (extras.extraFrontSeat). Builder metres, x forward, y LEFT, z up; panel u = pilot's right = -y.

  PYTHONDONTWRITEBYTECODE=1 /usr/bin/python3 tools/own_models/extra_front_seat.py
"""
import copy
import json
import math
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from vr_cockpit import Glb, Shape, Author, MODELS, ROOT, gl, builder
from vr_phase2e import compact
from vr_phase3b import art as p3b_art, angle as p3b_angle, SCALES as P3B_SCALES
from extra_panel_p3f import wall_halfwidth, slab, idx
from extra_cockpit_shared import R318, R214, ROW_Z, STICK_GAP, mat_index, round_face, place_control, retire, move
from drop_card_slot import validate

X_R, X_F = -1.175, -.150                          # panel planes (aft faces)
FRONT_ROW = {'ASI': -.075, 'G': 0.0, 'Altimeter': .075}   # u, L -> R as the front pilot sees them
FRONT_V = .300
FRONT_SUB = (.125, .245, .355)                    # sub-panel half width, bottom, top
S_FRONT = R214 / R318                             # 3-1/8 in -> 2-1/4 in, bezel flange included
REAR_RETIRE = ('Magneto', 'AlternateAir', 'Trim', 'LandingLight')
SWITCHES_L = [('Master', 'MASTER'), ('Alternator', 'ALT'), ('FuelPump', 'FUEL PUMP')]   # outboard -> inboard
SWITCHES_R = [('NavLights', 'NAV'), ('Strobe', 'STROBE'), ('Smoke', 'SMOKE')]           # inboard -> outboard
LABEL_W, LABEL_H, LABEL_Z = .052, .0105, .0608
# The row 15 mm higher than P3f's and the bats at 0.65 (a real MS24523 bat is ~22 mm): seen from the rear eye a
# 36 mm bat projects ~30 mm down the panel and hid the placard beneath it (first render, 2026-11-04 19:50).
ROW_Z_REAR, TOGGLE_SCALE = .090, .65
TEMPS = (.200, .150)                              # EGT/CHT (u, v), 2-1/4 in
# Verniers: on the left wall beside the rear throttle, outboard of its lever (y .278), knobs facing aft.
VERNIER_X, VERNIER_Y = -1.287, .326
VERNIERS = {'Prop': .204, 'Mixture': .162}         # z of the knob centre
VERNIER_TRAVEL = .045
# Fuel valve: GL type (gl_quadrant_v3.build_linkages), right wall, one rod.
VALVE_Y, VALVE_Z = -.284, .045
VALVE_X = {'Rear': -1.400, 'Front': -.500}
# Ty 2026-11-05: rear diagonal tubes removed completely.
GRIP_CANT_DEG, GRIP_LEN = 15.0, .110


def font(n, bold=True):
    return ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial %s.ttf' % ('Bold' if bold else ''), n) if bold \
        else ImageFont.truetype('/System/Library/Fonts/Supplemental/Arial.ttf', n)


def label_atlas(path, texts):
    """One cell per placard, stacked vertically: white engraved-style capitals on black."""
    W, H = 512, 104
    im = Image.new('RGB', (W, H * len(texts)), (14, 14, 16)); d = ImageDraw.Draw(im)
    for k, t in enumerate(texts):
        n = 76
        while d.textlength(t, font=font(n)) > W - 40: n -= 2
        d.rectangle((3, k * H + 3, W - 4, k * H + H - 4), outline=(90, 90, 94), width=3)
        d.text((W / 2, k * H + H / 2), t, font=font(n), anchor='mm', fill=(242, 242, 240))
    im.save(path)
    return {t: (k / len(texts), (k + 1) / len(texts)) for k, t in enumerate(texts)}


def placard(sh, uvs, c, w, h, v0, v1):
    """Quad facing the pilot (normal -x) centred at c (builder), uv band v0..v1."""
    c = np.array(c, float)
    sh.add([c + (0, w / 2, -h / 2), c + (0, -w / 2, -h / 2), c + (0, -w / 2, h / 2), c + (0, w / 2, h / 2)], [(0, 1, 2), (0, 2, 3)])
    uvs.extend([(0, v1), (1, v1), (1, v0), (0, v0)])


def node_world(g, name): return g.world_matrices()[idx(g, name)]


def set_parts(g, name, parts):
    """Replace node `name`'s mesh with parts [(material index, Shape, uv or None)] authored in builder WORLD
    coordinates, stored in the node's own frame (whatever its parents)."""
    Mi = np.linalg.inv(node_world(g, name)); prims = []
    for mat, sh, uv in parts:
        if not sh.t: continue
        p = np.array([(Mi @ np.append(gl(v), 1.0))[:3] for v in sh.v]); t = np.array(sh.t).reshape(-1, 3); flat = p[t].reshape(-1, 3)
        nrm = np.cross(p[t[:, 1]] - p[t[:, 0]], p[t[:, 2]] - p[t[:, 0]]); nrm /= np.maximum(np.linalg.norm(nrm, axis=1)[:, None], 1e-10)
        at = dict(POSITION=g.accessor(flat, 'VEC3'), NORMAL=g.accessor(np.repeat(nrm, 3, axis=0), 'VEC3'))
        if uv is not None: at['TEXCOORD_0'] = g.accessor(np.array(uv)[t].reshape(-1, 2), 'VEC2')
        prims.append(dict(attributes=at, material=mat, mode=4))
    g.doc['meshes'].append(dict(name=name, primitives=prims))
    g.doc['nodes'][idx(g, name)]['mesh'] = len(g.doc['meshes']) - 1


def new_root(g, name, p_builder):
    g.doc['nodes'].append(dict(name=name, translation=gl(p_builder).tolist()))
    g.doc['scenes'][0]['nodes'].append(len(g.doc['nodes']) - 1)
    return len(g.doc['nodes']) - 1


def set_local(g, name, p_builder):
    """Put node `name`'s origin at builder world point p (its parent's frame respected)."""
    i = idx(g, name); par = next((j for j, n in enumerate(g.doc['nodes']) if i in n.get('children', [])), None)
    P = g.world_matrices()[par] if par is not None else np.eye(4)
    g.doc['nodes'][i]['translation'] = (np.linalg.inv(P) @ np.append(gl(p_builder), 1.0))[:3].tolist()


def lathe(sh, base, axis, prof, n=28):
    """Surface of revolution along `axis` from `base`: prof = [(s, r)], r = 0 caps the ends."""
    a = np.array(axis, float); a /= np.linalg.norm(a)
    u = np.cross(a, (0, 1, 0)); u /= np.linalg.norm(u); w = np.cross(a, u)
    rings = [[np.array(base) + s * a + r * (math.cos(t) * u + math.sin(t) * w) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)]
             for s, r in prof]
    v = [q for ring in rings for q in ring]; tri = []
    for k in range(len(rings) - 1):
        for i in range(n):
            j = (i + 1) % n; a0, a1, b0, b1 = k * n + i, k * n + j, (k + 1) * n + i, (k + 1) * n + j
            tri.extend(((a0, b0, b1), (a0, b1, a1)))
    sh.add(v, tri)


def scale_gauge_nodes(g, names, c_builder, s):
    """Shrink meshes about the gauge centre in the panel plane (builder y, z), x untouched. Shared meshes are copied."""
    users = {}
    for n in g.doc['nodes']:
        if 'mesh' in n: users[n['mesh']] = users.get(n['mesh'], 0) + 1
    W = g.world_matrices()
    for nm in names:
        i = idx(g, nm); n = g.doc['nodes'][i]
        if 'mesh' not in n: continue
        if users[n['mesh']] > 1:
            g.doc['meshes'].append(copy.deepcopy(g.doc['meshes'][n['mesh']])); users[n['mesh']] -= 1
            n['mesh'] = len(g.doc['meshes']) - 1; users[n['mesh']] = 1
        M = W[i]; Mi = np.linalg.inv(M); c = gl(c_builder)
        S = np.diag([1.0, s, s])                     # glTF (x=-bx, y=bz, z=by): scale glTF y and z
        for p in g.doc['meshes'][n['mesh']]['primitives']:
            v = np.asarray(g.array(p['attributes']['POSITION']), float)
            w = v @ M[:3, :3].T + M[:3, 3]
            w2 = c + (w - c) @ S.T
            p['attributes']['POSITION'] = g.accessor(w2 @ Mi[:3, :3].T + Mi[:3, 3], 'VEC3')


def retire_node(g, doc, name, why):
    retire(g, doc, name)
    n = g.doc['nodes'][idx(g, 'Legacy_P3g_' + name)]; n['name'] = 'Legacy_P3h_' + name
    n['extras'] = {'p3h_retired': True, 'reason': why}


def wall_hits(g, x, z):
    """Every |y| of the cockpit-side meshes at (x, z): a probe for what a part along the wall would run through."""
    from extra_panel_p3f import tris_of, WALL_NODES
    world = g.world_matrices(); ys = []
    for i, n in enumerate(g.doc['nodes']):
        if 'mesh' not in n or n['name'].startswith('Legacy'): continue
        for tri in tris_of(g, world, i):
            d = tri[:, 0] - x; pts = []
            for a, c in ((0, 1), (1, 2), (2, 0)):
                if (d[a] < 0) != (d[c] < 0): s = d[a] / (d[a] - d[c]); pts.append(tri[a] + s * (tri[c] - tri[a]))
            if len(pts) == 2:
                (ya, za), (yb, zb) = pts[0][1:], pts[1][1:]
                if (za - z) * (zb - z) <= 0 and za != zb: ys.append((n['name'], ya + (z - za) / (zb - za) * (yb - ya)))
    return ys


def update(path, doc):
    g = Glb(path)
    if g.doc['extras'].get('extraFrontSeat'): print('EXTRA', path.name, 'already done'); return False
    assert g.doc['extras'].get('extraCockpitShared'), path
    a = Author(g)
    meta = g.doc['extras']['vrCockpitPhase3b']; controls = g.doc['extras']['vrCockpitPhase3a']['controls']
    report = {}
    grey = g.material('P3b_Panel', (.165, .170, .180))           # the AirSpace 300L's grey (linear)
    g.material('P3d_RearPanelTop', (.165, .170, .180))              # the hood over it, the same grey
    bezel = mat_index(g, 'P3b_Bezel'); needle_w = mat_index(g, 'P3b_Needle'); orange = mat_index(g, 'P3b_SecondNeedle')
    metal = mat_index(g, 'P3_ControlMetal'); black = mat_index(g, 'Cockpit_black'); red = mat_index(g, 'Cockpit_knob_red')
    mix_red = mat_index(g, 'P3_MixtureRed'); prop_blue = mat_index(g, 'P3_PropBlue'); nut = mat_index(g, 'P3_ControlBlack')
    button = g.material('P3h_GripButton', (.32, .32, .34))
    temps_mat = mat_index(g, 'P3b_Dial_Temps')

    # ---------------- FRONT SEAT ----------------
    front = next(p for p in meta['panels'] if p['seat'] == 'Front')
    keep = []
    for row in front['gauges']:
        key = row['gauge']; bez = row['face'].replace('Face_', 'Bezel_')
        if key not in FRONT_ROW:
            for nm in [row['face'], bez] + row['needles']: retire_node(g, doc, nm, 'Ty 2026-11-04 19:05: front seat ASI, G, ALT only')
            continue
        c = np.array((X_F, -row['u'], row['v']))
        scale_gauge_nodes(g, [row['face'], bez] + row['needles'], c, S_FRONT)
        u1 = FRONT_ROW[key]; d = np.array((0., -(u1 - row['u']), FRONT_V - row['v']))
        for nm in [row['face'], bez] + row['needles']:
            move(g, nm, d)
            for e in doc['rig']:
                if e['node'] == nm: e['pivot_gltf'] = (np.array(e['pivot_gltf']) + gl(d)).tolist()
        row.update(u=u1, v=FRONT_V, case_inches=2.25, bezel_radius_m=R214)
        keep.append(row)
    front['gauges'] = keep
    for nm in ('Radio_Display_F', 'Xpdr_Display_F', 'Totalizer_Display_F'):
        retire_node(g, doc, nm, 'front seat: no radios, no fuel computer')
    hw, zb, zt = FRONT_SUB
    q = node_world(g, 'Panel_P3b_F')[:3, 3]; sh = slab(X_F, [(zb, hw), (zt, hw)])
    g.mesh('Panel_P3b_F', [gl(v) - q for v in sh.v], sh.t, grey)
    front['outline_zw'] = [[zb, hw], [zt, hw]]
    gone = []
    for c in [c for c in controls if c['seat'] == 'Front']:
        short = c['grip'][5:]
        if short == 'FuelSelector_Front': continue                 # becomes the front fuel-valve lever
        for nm in (c['node'], 'Mount_' + short, c['grip'], c['poke']): retire_node(g, doc, nm, 'front seat: no switches or knobs')
        controls.remove(c); gone.append(short)
    report['front_retired_controls'] = len(gone)

    # ---------------- REAR PANEL ----------------
    rear = next(p for p in meta['panels'] if p['seat'] == 'Rear')
    by = {c['grip'][5:]: c for c in controls if c['seat'] == 'Rear'}
    for k in REAR_RETIRE:
        c = by.pop(k)
        for nm in (c['node'], 'Mount_' + k, c['grip'], c['poke']): retire_node(g, doc, nm, 'Ty 19:08: only six labelled switches, no unlabelled knobs')
        controls.remove(c)
    for nm in ('Subpanel_Radio_Display_R', 'Subpanel_Xpdr_Display_R'):   # empty P3b boxes left below the panel edge
        n = g.doc['nodes'][idx(g, nm)]; n.pop('mesh', None); n['extras'] = {'p3h_retired': True, 'reason': 'empty, under the panel'}
    # The ALTERNATOR toggle: a clone of MASTER (node, mount, grip, poke, control, rig row).
    m = by['Master']; base_n = g.doc['nodes'][idx(g, m['node'])]
    ctl = copy.deepcopy({k: v for k, v in base_n.items() if k != 'children'}); ctl['name'] = 'Ctl_Toggle_Alternator'
    g.doc['nodes'].append(ctl); ci = len(g.doc['nodes']) - 1
    roots = g.doc['scenes'][0]['nodes']
    assert idx(g, m['node']) in roots and idx(g, 'Mount_Master') in roots
    roots.append(ci)
    for ch in base_n.get('children', []):
        k = copy.deepcopy(g.doc['nodes'][ch]); k['name'] = k['name'].replace('Master', 'Alternator')
        g.doc['nodes'].append(k); ctl.setdefault('children', []).append(len(g.doc['nodes']) - 1)
    ctl['children'] = ctl['children'][len(ctl['children']) - len(base_n.get('children', [])):]
    mt = copy.deepcopy(g.doc['nodes'][idx(g, 'Mount_Master')]); mt['name'] = 'Mount_Alternator'
    g.doc['nodes'].append(mt); roots.append(len(g.doc['nodes']) - 1)
    alt = copy.deepcopy(m); alt.update(node='Ctl_Toggle_Alternator', param='alternator', grip='Grip_Alternator', poke='Poke_Alternator', label='ALT')
    controls.append(alt); by['Alternator'] = alt
    ctl.setdefault('extras', {}).update(rig_param='alternator', interaction=alt)
    for e in [e for e in doc['rig'] if e['node'] == m['node']][:1]:
        e2 = copy.deepcopy(e); e2.update(node='Ctl_Toggle_Alternator', param='alternator', desc='ALTERNATOR toggle (Ty 2026-11-04 19:08)'); doc['rig'].append(e2)
    # The six switches: one row, placards beneath, each nut a small hex instead of the P3a block.
    levels = [(z, w) for z, w in rear['outline_zw']]
    wmax = float(min(h for z, h in levels if abs(z - ROW_Z_REAR) <= .013)) - .022
    ys = np.linspace(wmax, STICK_GAP, 3)
    places = [(k, lab, ys[i]) for i, (k, lab) in enumerate(SWITCHES_L)] + [(k, lab, -ys[2 - i]) for i, (k, lab) in enumerate(SWITCHES_R)]
    with tempfile.TemporaryDirectory() as td:
        pl = Path(td) / 'labels.png'; bands = label_atlas(pl, [lab for _, lab, _ in places]); lab_mat = g.material('P3h_SwitchLabels', png=pl)
        tp = Path(td) / 'temps.png'; p3b_art('Temps', ['EGT', 'CHT'], tp); g.material('P3b_Dial_Temps', png=tp)
    lab_sh, lab_uv = Shape(), []
    for k, lab, y in places:
        c = by[k]; c['label'] = lab
        place_control(g, doc, c, (X_R - .001, y, ROW_Z_REAR), 'extra_front_seat: the six labelled switches, one row (Ty 19:08)', TOGGLE_SCALE)
        hexn = Shape(); hexn.tube((X_R - .0002, y, ROW_Z_REAR), (X_R - .0045, y, ROW_Z_REAR), .0068, 6)
        set_parts(g, 'Mount_' + k, [(nut, hexn, None)])
        placard(lab_sh, lab_uv, (X_R - .0011, y, LABEL_Z), LABEL_W, LABEL_H, *bands[lab])
    vv = [gl(v) for v in lab_sh.v]; g.mesh('Placards_P3h_Switches_R', vv, lab_sh.t, lab_mat, lab_uv)
    report['switch_y'] = [round(float(y), 4) for _, _, y in places]
    # EGT / CHT, 2-1/4 in, lower right.
    u, v = TEMPS; x = X_R; rr = R214 - .0035
    shb = Shape(); shb.tube((x + .003, -u, v), (x - .0003, -u, v), R214, 64)
    a.node('Bezel_P3h_Temps_R', (x - .0005, -u, v), shape=shb, mat=bezel)
    fv, ft, fuv = round_face(x - .0005, -u, v, rr); g.mesh('Face_P3h_Temps_R', [gl(q) for q in fv], ft, temps_mat, fuv)
    row = dict(gauge='Temps', u=u, v=v, case_inches=2.25, bezel_radius_m=R214, face='Face_P3h_Temps_R', needles=[])
    for j, k in enumerate(('EGT', 'CHT')):
        name = 'Needle_%s_R' % k; pivot = np.array((x - .0008, -u, v)); shn = Shape()
        shn.add([pivot + (0, -.0011, -.003), pivot + (0, .0011, -.003), pivot + (0, 0, rr * .80)], [(0, 2, 1), (0, 1, 2)])
        shn.v = [np.array(qq) + (-j * .00005, 0, 0) for qq in shn.v]
        param, pts, angs, unit = P3B_SCALES[k]; model = pts[0]; init = p3b_angle(k, model); th = math.radians(init)
        cc, ss = math.cos(th), math.sin(th)
        shn.v = [pivot + np.array((qq[0] - pivot[0], cc * (qq[1] - pivot[1]) - ss * (qq[2] - pivot[2]), ss * (qq[1] - pivot[1]) + cc * (qq[2] - pivot[2]))) for qq in shn.v]
        a.node(name, pivot, shape=shn, mat=orange if k == 'CHT' else needle_w)
        a.rigged(name, param, (1, 0, 0), pts, [ang - init for ang in angs], desc='EGT/CHT 2-1/4 in (Ty 2026-11-04 19:14); E scale')
        nn = g.doc['nodes'][a.idx(name)]; nn['extras'].update(rig_unit=unit, rig_param_model=model, rig_dial_ang0_deg=init)
        a.rig[-1].update(unit=unit, model=model); a.rig[-1]['extra']['rig_dial_ang0_deg'] = init
        row['needles'].append(name)
    rear['gauges'].append(row)
    # Ty 2026-11-05: no rear diagonal tubes.

    # ---------------- VERNIERS (rear, left wall) ----------------
    bracket = Shape(); bracket.box((VERNIER_X + .003, VERNIER_Y + .002, .183), (.006, .044, .090))
    for k, z in VERNIERS.items():
        c = by[k]; knob = mix_red if k == 'Mixture' else prop_blue
        P = np.array((VERNIER_X, VERNIER_Y, z))
        # Drawn IN (rich / fine): nut on the bracket, shaft, knurled knob, centre lock button.
        sh_nut = Shape(); sh_nut.tube(P + (.0, 0, 0), P + (-.007, 0, 0), .0085, 6)
        sh_shaft = Shape(); sh_shaft.tube(P + (.030, 0, 0), P + (-.012, 0, 0), .0032, 12)
        for s in np.arange(-.008, .025, .0025): sh_shaft.tube(P + (s, 0, 0), P + (s - .0008, 0, 0), .0037, 12)  # thread
        sh_knob = Shape(); lathe(sh_knob, P + (-.010, 0, 0), (-1, 0, 0),
                                 [(0, 0), (0, .011), (.003, .0145), (.019, .0150), (.022, .0130), (.022, 0)], 24)
        sh_btn = Shape(); sh_btn.tube(P + (-.031, 0, 0), P + (-.0355, 0, 0), .0055, 16)
        set_parts(g, c['node'], [])                                  # clear first so the node frame stays valid
        set_local(g, c['node'], P)
        set_parts(g, c['node'], [(metal, sh_shaft, None), (knob, sh_knob, None), (black, sh_btn, None)])
        set_local(g, 'Mount_' + k, P); set_parts(g, 'Mount_' + k, [(nut, sh_nut, None)] + ([(nut, bracket, None)] if k == 'Prop' else []))
        set_local(g, c['grip'], P + (-.022, 0, 0)); set_local(g, c['poke'], P + (-.036, 0, 0))
        c.update(pivot_gltf=gl(P).tolist(), axis_gltf=[-1.0, 0.0, 0.0], travel=round(VERNIER_TRAVEL * 1000), position_status='E',
                 label='PROP' if k == 'Prop' else 'MIXTURE',
                 placement='extra_front_seat: vernier on the left wall beside the throttle, outboard of it, knob aft (Ty 19:13)')
        g.doc['nodes'][idx(g, c['node'])].setdefault('extras', {})['interaction'] = c
        rows = [e for e in doc['rig'] if e['node'] == c['node']]
        for e in rows:
            e.update(pivot_gltf=gl(P).tolist(), axis_gltf=[-1.0, 0.0, 0.0], map=[[0, -VERNIER_TRAVEL], [1, 0.0]], model=1.0,
                     desc='vernier push-pull: drawn IN (1 = rich / fine), 0 = pulled %d mm aft' % round(VERNIER_TRAVEL * 1000))
        ex = g.doc['nodes'][idx(g, c['node'])]['extras']
        ex.update(rig_move_axis_gltf=[-1.0, 0.0, 0.0], rig_map_param=[0, 1], rig_map_m=[-VERNIER_TRAVEL, 0.0], rig_param_model=1.0)
    report['verniers'] = dict(x=VERNIER_X, y=VERNIER_Y, z=VERNIERS)

    # ---------------- FUEL VALVE (both seats, linked) ----------------
    rod = Shape(); rod.tube((VALVE_X['Rear'] - .02, VALVE_Y, VALVE_Z), (VALVE_X['Front'] + .02, VALVE_Y, VALVE_Z), .005, 12)
    guides = Shape()
    for xx in (VALVE_X['Rear'] - .03, -1.10, -.80, VALVE_X['Front'] + .03):
        guides.tube((xx - .012, VALVE_Y, VALVE_Z), (xx + .012, VALVE_Y, VALVE_Z), .010, 16)
        guides.box((xx, VALVE_Y - .009, VALVE_Z), (.025, .010, .030))
    for nm, sh, mat in (('Extra_FuelValve_SharedRod', rod, metal), ('Extra_FuelValve_RodGuides', guides, nut)):
        new_root(g, nm, (VALVE_X['Rear'], VALVE_Y, VALVE_Z)); set_parts(g, nm, [(mat, sh, None)])
    for seat in ('Rear', 'Front'):
        suf = '' if seat == 'Rear' else '_Front'; c = next(c for c in controls if c['seat'] == seat and c['grip'] == 'Grip_FuelSelector' + suf)
        P = np.array((VALVE_X[seat], VALVE_Y, VALVE_Z))
        handle = Shape(); handle.tube(P + (0, 0, -.012), P + (0, 0, -.095), .009, 12); handle.sphere(P + (0, 0, -.095), .012, 16, 10)
        boss = Shape(); boss.tube(P + (-.010, 0, 0), P + (.010, 0, 0), .014, 16)
        set_parts(g, c['node'], []); set_local(g, c['node'], P)
        set_parts(g, c['node'], [(red, handle, None), (metal, boss, None)])
        set_local(g, 'Mount_FuelSelector' + suf, P); set_parts(g, 'Mount_FuelSelector' + suf, [])
        g.doc['nodes'][idx(g, 'Mount_FuelSelector' + suf)].pop('mesh', None)
        set_local(g, c['grip'], P + (0, 0, -.075)); set_local(g, c['poke'], P + (0, 0, -.095))
        c.update(param='fuel_valve', kind='Rotary', pivot_gltf=gl(P).tolist(), axis_gltf=[1.0, 0.0, 0.0], travel=90, travel_unit='deg',
                 sim_ends=[0, 1], detents=[0, 1], detent_count=2, spring_return=False, sweep_radius_m=.110, label='FUEL VALVE',
                 position_status='E', placement='Right wall, one fore/aft rod to the other cockpit; ON down, OFF 90 deg inboard (GL type, Ty 19:06)')
        ex = g.doc['nodes'][idx(g, c['node'])].setdefault('extras', {})
        ex.update(rig_param='fuel_valve', rig_axis_gltf=[1., 0., 0.], rig_map_param=[0., 1.], rig_map_deg=[-90., 0.], rig_param_model=1.,
                  interaction=c, mechanically_linked='Extra_FuelValve_SharedRod')
        rows = [e for e in doc['rig'] if e['node'] == c['node']]
        assert rows, c['node']
        for e in rows:
            e.update(param='fuel_valve', kind='rot', pivot_gltf=gl(P).tolist(), axis_gltf=[1., 0., 0.], map=[[0, -90], [1, 0]], model=1, unit='0..1',
                     desc=c['placement'], link='front + rear on one rod (same param)')
    report['fuel_valve'] = dict(x=VALVE_X, y=VALVE_Y, z=VALVE_Z)

    # ---------------- THROTTLES: pistol grip ----------------
    for seat, nm, grip in (('Rear', 'Throttle_R', 'Grip_Throttle_Rear'), ('Front', 'Throttle_F', 'Grip_Throttle_Front')):
        i = idx(g, nm); M = node_world(g, nm); mesh = g.doc['meshes'][g.doc['nodes'][i]['mesh']]
        prims = mesh['primitives']; W = lambda p: np.stack([-(w := np.asarray(g.array(p['attributes']['POSITION']), float) @ M[:3, :3].T + M[:3, 3])[:, 0], w[:, 2], w[:, 1]], 1)
        ball = next(p for p in prims if g.doc['materials'][p['material']]['name'] == 'Cockpit_black')
        bb = W(ball); K = (bb.min(0) + bb.max(0)) / 2
        e = next(r for r in doc['rig'] if r['node'] == nm); P = builder(np.array(e['pivot_gltf']))
        d = (K - P); d[1] = 0; d /= np.linalg.norm(d)
        phi = math.atan2(d[0], d[2]) - math.radians(GRIP_CANT_DEG)
        ax = np.array((math.sin(phi), 0, math.cos(phi)))
        B = K - .012 * d
        prof = [(0, 0), (0, .0205), (.010, .0195), (.016, .0168)]
        for k2 in range(5): prof += [(.022 + .014 * k2, .0186), (.029 + .014 * k2, .0166)]
        prof += [(.092, .0182), (.100, .0175), (.106, .0158), (GRIP_LEN, .0115), (GRIP_LEN, 0)]
        shg = Shape(); lathe(shg, B, ax, prof, 28)
        shb = Shape(); shb.tube(B + ax * (GRIP_LEN - .001), B + ax * (GRIP_LEN + .004), .0075, 18)
        sha = Shape(); sha.tube(K - .02 * d, B + .02 * ax, .0048, 10)          # arm carried up into the grip
        keepp = [p for p in prims if p is not ball]
        Mi = np.linalg.inv(M)
        for mat, sh in ((black, shg), (button, shb), (mat_index(g, 'Cockpit_stick'), sha)):
            pp = np.array([(Mi @ np.append(gl(v), 1.0))[:3] for v in sh.v]); t = np.array(sh.t).reshape(-1, 3); flat = pp[t].reshape(-1, 3)
            nrm = np.cross(pp[t[:, 1]] - pp[t[:, 0]], pp[t[:, 2]] - pp[t[:, 0]]); nrm /= np.maximum(np.linalg.norm(nrm, axis=1)[:, None], 1e-10)
            keepp.append(dict(attributes=dict(POSITION=g.accessor(flat, 'VEC3'), NORMAL=g.accessor(np.repeat(nrm, 3, axis=0), 'VEC3')), material=mat, mode=4))
        mesh['primitives'] = keepp
        set_local(g, grip, B + ax * .055)
        report[nm] = dict(knob_was=np.round(K, 3).tolist(), grip_axis=np.round(ax, 3).tolist(), grip_mid=np.round(B + ax * .055, 3).tolist())

    doc['rig'] += a.rig
    doc['controls'] = controls; doc['phase3b'] = meta
    g.doc['extras']['extraFrontSeat'] = dict(
        owner='Ty 2026-11-04 19:05-19:14: front seat ASI/G/ALT + throttle, stick, rudder, fuel valve; rear: grey panel, six labelled '
              'switches, no unlabelled knobs, EGT/CHT; verniers beside the throttle; pistol-grip throttle; linked fuel valve',
        front_row=FRONT_ROW, row_z_rear=ROW_Z_REAR, toggle_scale=TOGGLE_SCALE, front_v=FRONT_V, front_sub=FRONT_SUB, switches=[lab for _, lab, _ in places], temps=TEMPS,
        verniers=dict(x=VERNIER_X, y=VERNIER_Y, z=VERNIERS, travel=VERNIER_TRAVEL), valve=dict(x=VALVE_X, y=VALVE_Y, z=VALVE_Z),
        report=report)
    compact(g); validate(g, path); g.save(path)
    print('EXTRA', path.name, json.dumps(report))
    return True


def probe():
    """Print what the rod and the parts along the walls would pass through (run before trusting the numbers)."""
    g = Glb(sorted((MODELS / 'extra-300-like').glob('*own*.glb'))[0])
    for x in np.arange(VALVE_X['Rear'], VALVE_X['Front'] + .01, .05):
        hits = sorted({(round(float(y), 3), n) for n, y in wall_hits(g, float(x), VALVE_Z) if -.40 < y < -.15})
        print('rod x %.2f' % x, hits[:6])


if __name__ == '__main__':
    import sys
    if '--probe' in sys.argv: probe(); sys.exit()
    rigpath = ROOT / 'docs/models/ea300-own-rig.json'
    base = rigpath.read_text(); doc = json.loads(base); wrote = False
    for k, p in enumerate(sorted((MODELS / 'extra-300-like').glob('*own*.glb'))):
        wrote |= update(p, doc if k == 0 else json.loads(base))
    if wrote: rigpath.write_text(json.dumps(doc, indent=1) + '\n')
