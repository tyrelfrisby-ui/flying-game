"""Great Lakes panels v2 (Ty's cockpit spec 2026-11-05, GREAT LAKES section). The LAST deterministic step of the
Great Lakes build: it starts from the GLB + rig table at BASE (build_great_lakes.py -> gl_postbuild.py ->
gl_quadrant_v3.py, as committed) and writes both again, so running it twice gives the same file.

  REAR (PIC) PANEL, "STILL not arranged for a centred IAC card ... the tenth time I have asked":
    - a CENTRED clear space for a half-letter IAC card (4.25 x 5.5 in, portrait): the card sits on the panel's centre
      line, flush on its face, and nothing else is within 10 mm of it (GreatLakesCardSpaceTests holds this).
    - the instruments move out to either side of it, two rows each: ASI / ALT over G / turn coordinator on the
      pilot's left, tach / manifold pressure over oil / CHT on his right. Real 3-1/8 and 2-1/4 in cases, 8 mm of air
      between bezels at least.
    - out: the Hobbs, the VSI, the mag-key dial and the ignition rotary (TWO MAG toggles on the switch console instead),
      the blank placard, and the big grey knob cluster round the radios.
    - the Trig radio + transponder heads go UNDER the panel, side by side on a black subpanel on the pilot's right,
      outboard of the stick's full-right, full-forward corner; their knobs are the heads' own size now.
    - the wet compass sits 40 mm further into the panel (20 mm proud instead of 60), centred over the card.
    - the altimeter gets its setting knob at the bezel's lower left and a Kollsman window node at 3 o'clock, the
      G-meter a reset button at its lower left (the cockpit-common lane's popup / recorder bind to those names).
  FRONT PANEL: a second card (FrontPanel_SequenceCard_*) hung from the panel's lower right corner, where the front
    pilot sees it over the stick; it shows the card selected in the back (common lane). Altimeter knob + Kollsman nodes.

Frames: glTF metres, +x AFT, +y up, +z the pilot's LEFT (rear panel face x 1.638, front 0.968). Builder (x fwd, y left,
z up) only where gl_quadrant_v3's Shape helpers are reused.

  PYTHONDONTWRITEBYTECODE=1 /usr/bin/python3 tools/own_models/gl_panel_v2.py
"""
import json
import math
import subprocess
import sys
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from glb_visuals import Glb                      # noqa: E402
from drop_card_slot import validate              # noqa: E402

ROOT = HERE.parents[1]
GLB_REL = 'unity/Assets/_Project/Resources/Models/great-lakes-like/great-lakes-like-own.glb'
RIG_REL = 'docs/models/great-lakes-own-rig.json'
BASE = '6f659d0d'        # lane/claude-island-nav when this lane forked: gl_quadrant_v3's last committed export
GLB, RIG = ROOT / GLB_REL, ROOT / RIG_REL

REAR_X, FRONT_X = 1.638, 0.968
R_BIG, R_SMALL = 0.040, 0.028                     # bezel radii (3-1/8 in, 2-1/4 in cases), gl_cockpit.py
CARD_W, CARD_H = 4.25 * 0.0254, 5.5 * 0.0254

# ---- rear instruments: where they were (gl_cockpit.py PANEL['R'], glTF (z, y)) and where they go ------------------
OLD = {'G': (0.124, 0.071, R_BIG), 'MP': (-0.154, 0.071, R_BIG), 'CHT': (-0.228, 0.032, R_SMALL), 'VSI': (0.203, -0.019, R_BIG),
       'ALT': (0.115, -0.019, R_BIG), 'ASI': (0.028, -0.019, R_BIG), 'TC': (-0.059, -0.019, R_BIG), 'TACH': (-0.148, -0.019, R_BIG),
       'OIL': (-0.224, -0.029, R_SMALL), 'HOBBS': (0.296, -0.032, 0.025), 'MAG': (0.279, 0.020, 0.014)}
UPPER, LOWER, LOWER_SMALL = 0.059, -0.032, -0.038   # row heights: lower bezels clear the panel's bottom edge (-0.080)
INNER, OUTER = 0.118, 0.209                         # |z| of the columns: inner bezel 19 mm outside the card holder
NEW = {'ASI': (INNER, UPPER), 'ALT': (OUTER, UPPER), 'G': (INNER, LOWER), 'TC': (OUTER, LOWER),
       'TACH': (-INNER, UPPER), 'MP': (-OUTER, UPPER), 'OIL': (-INNER, LOWER_SMALL), 'CHT': (-OUTER, LOWER_SMALL)}
GONE = ('VSI', 'HOBBS', 'MAG')
NEEDLES = {'Needle_ASI_R': 'ASI', 'Needle_Alt100_R': 'ALT', 'Needle_Alt1000_R': 'ALT', 'Needle_G_R': 'G', 'Needle_GMax_R': 'G',
           'Needle_GMin_R': 'G', 'TC_Symbol_R': 'TC', 'TC_Ball_R': 'TC', 'Needle_Tach_R': 'TACH', 'Needle_MP_R': 'MP',
           'Needle_OilP_R': 'OIL', 'Needle_OilT_R': 'OIL', 'Needle_CHT_R': 'CHT'}

# ---- the card: centred on the rear panel, flush on its face -------------------------------------------------------
CARD_NODES = ('SequenceCard_Clip', 'SequenceCard_Face', 'SequenceCard_Holder', 'SequenceCard_Stock')
CARD_FACE_LOCAL_YC = 0.005            # the card meshes' own centre (gl y); z centre 0
REAR_CARD_YC = 0.008                  # holder -0.065 .. 0.081: 15 mm over the panel's bottom edge
REAR_CARD_T = [0.0015, REAR_CARD_YC - CARD_FACE_LOCAL_YC, 0.0]
# front: hung from the panel's lower right corner, top 4 mm under the tachometer's bezel, outboard of the stick's reach
FRONT_CARD_ZC, FRONT_CARD_TOP = -0.257, -0.016
FRONT_CARD_T = [FRONT_X + 0.0025 - 1.6407, FRONT_CARD_TOP - CARD_H / 2 - CARD_FACE_LOCAL_YC, FRONT_CARD_ZC]

# ---- the compass: centred over the card, 40 mm further into the panel ---------------------------------------------
COMPASS_BOX = ((1.628, 1.71), (0.082, 0.166), (-0.01, 0.075))
COMPASS_D = np.array((-0.040, 0.007, -0.032))
# ---- the radios: under the panel on the right ---------------------------------------------------------------------
AV_W, AV_H = 0.063, 0.044
AV_OLD = {'COM': (-0.059, 0.0965), 'XPDR': (-0.059, 0.0515)}
AV_YC = -0.109
AV_NEW = {'COM': (-0.2365, AV_YC), 'XPDR': (-0.3045, AV_YC)}
SUBPANEL = dict(x=(1.6335, 1.6395), y=(-0.139, -0.079), z=(-0.341, -0.200))
AV_FACE_X = REAR_X + 0.0125           # the heads' aft faces
# knob: control node -> (head, art u px, art v px, radius, length) on the 630 x 440 px face art (gl-avx-*.png)
KNOBS = {'Ctl_Rotary_ComVolume': ('COM', 75, 200, 0.0045, 0.012), 'Ctl_Rotary_ComOuter': ('COM', 555, 200, 0.0070, 0.008),
         'Ctl_Rotary_ComInner': ('COM', 555, 200, 0.0045, 0.014), 'Ctl_PushPull_ComSwap': ('COM', 195, 335, 0.0030, 0.004),
         'Ctl_Rotary_XpdrMode': ('XPDR', 540, 200, 0.0060, 0.012), 'Ctl_Rotary_XpdrDigit1': ('XPDR', 66, 320, 0.0032, 0.006),
         'Ctl_Rotary_XpdrDigit2': ('XPDR', 134, 320, 0.0032, 0.006), 'Ctl_Rotary_XpdrDigit3': ('XPDR', 202, 320, 0.0032, 0.006),
         'Ctl_Rotary_XpdrDigit4': ('XPDR', 270, 320, 0.0032, 0.006), 'Ctl_PushPull_XpdrIdent': ('XPDR', 406, 320, 0.0030, 0.004)}
RETIRED = ['Ctl_Rotary_Magneto', 'Grip_Magneto', 'Poke_Magneto', 'Mount_Magneto', 'MagKey_R', 'Needle_VSI_R',
           'Ctl_Rotary_Magneto_Front', 'Grip_Magneto_Front', 'Poke_Magneto_Front', 'Mount_Magneto_Front']


def base_bytes(rel):
    return subprocess.run(['git', 'show', BASE + ':' + rel], cwd=ROOT, check=True, capture_output=True).stdout


def names(g):
    return {n['name']: i for i, n in enumerate(g.doc['nodes'])}


def head_point(head, u, v):
    zc, yc = AV_NEW[head]
    return zc + AV_W / 2 - u / 630 * AV_W, yc + AV_H / 2 - v / 440 * AV_H


# ---- mesh editing -------------------------------------------------------------------------------------------------
def flat_prim(g, prim):
    """(positions, normals, uv or None) per triangle corner, unindexed."""
    a = prim['attributes']
    pos, nrm = g.array(a['POSITION']), g.array(a['NORMAL'])
    uv = g.array(a['TEXCOORD_0']) if 'TEXCOORD_0' in a else None
    idx = g.array(prim['indices']).ravel().astype(int) if 'indices' in prim else np.arange(len(pos))
    return pos[idx], nrm[idx], (uv[idx] if uv is not None else None)


def write_prim(g, prim, pos, nrm, uv):
    a = dict(POSITION=g.accessor(pos.astype(np.float32), 'VEC3'), NORMAL=g.accessor(nrm.astype(np.float32), 'VEC3'))
    if uv is not None:
        a['TEXCOORD_0'] = g.accessor(uv.astype(np.float32), 'VEC2')
    prim['attributes'] = a
    prim.pop('indices', None)


def edit_interior(g):
    """Move / drop the rear panel's static parts inside Cockpit_Interior. Returns what was moved, for the log."""
    nm = names(g)
    ci = nm['Cockpit_Interior']
    off = g.world_matrices()[ci][:3, 3]
    mats = [m['name'] for m in g.doc['materials']]
    report = {}
    for prim in g.doc['meshes'][g.doc['nodes'][ci]['mesh']]['primitives']:
        mat = mats[prim['material']]
        pos, nrm, uv = flat_prim(g, prim)
        tri = (pos + off).reshape(-1, 3, 3)
        c = tri.mean(1)
        keep = np.ones(len(tri), bool)
        delta = np.zeros((len(tri), 3))
        rear = (c[:, 0] > 1.62) & (c[:, 0] < 1.715) & (c[:, 1] > -0.09) & (c[:, 1] < 0.19)
        if mat in ('Cockpit_harness', 'Cockpit_lap_belt'):          # Ty 2026-11-05: no seat belts (common lane strips old GLBs at runtime)
            keep[:] = False
            report[mat] = int(len(tri))
        elif mat == 'Cockpit_placard_blank':
            keep &= ~rear
            report['placard'] = int(rear.sum())
        elif mat == 'Cockpit_blank_plug':
            keep &= ~rear                                          # the old radio plate; a new subpanel replaces it
            report['radio plate'] = int(rear.sum())
        elif mat in ('Avionics_Face_COM', 'Avionics_Face_XPDR'):
            h = 'COM' if mat.endswith('COM') else 'XPDR'
            d = (0.0, AV_NEW[h][1] - AV_OLD[h][1], AV_NEW[h][0] - AV_OLD[h][0])
            delta[rear] = d
            report[h] = int(rear.sum())
        elif mat != 'Cockpit_panel':
            (x0, x1), (y0, y1), (z0, z1) = COMPASS_BOX
            box = (c[:, 0] > x0) & (c[:, 0] < x1) & (c[:, 1] > y0) & (c[:, 1] < y1) & (c[:, 2] > z0) & (c[:, 2] < z1)
            if mat == 'Cockpit_tube':
                # the compass's bracket to the panel top (gl_cockpit: gbox at cx+0.02): inside the panel once flush
                br = box & (c[:, 0] < 1.668) & (c[:, 1] < 0.105)
                keep &= ~br
                box &= ~br
                if br.any(): report['compass bracket'] = int(br.sum())
            if mat in ('Cockpit_bezel', 'Cockpit_lubber', 'Cockpit_tube'):
                delta[box] = COMPASS_D
                if box.any(): report.setdefault('compass', 0); report['compass'] += int(box.sum())
            inst = rear & ~box & (c[:, 0] < 1.66)
            for k, (z, y, r) in OLD.items():
                sel = inst & (np.hypot(c[:, 2] - z, c[:, 1] - y) < 1.15 * r)
                if mat.startswith('Dial_') and mat != 'Dial_' + k and mat not in ('Dial_OILPRESS',):
                    sel &= False
                if not sel.any():
                    continue
                if k in GONE:
                    keep &= ~sel
                else:
                    nz, ny = NEW[k]
                    delta[sel] = (0.0, ny - y, nz - z)
                report[k] = report.get(k, 0) + int(sel.sum())
                inst &= ~sel
        tri = tri + delta[:, None, :]
        tri, n3 = tri[keep], nrm.reshape(-1, 3, 3)[keep]
        u3 = uv.reshape(-1, 3, 2)[keep] if uv is not None else None
        if not len(tri):
            prim['_empty'] = True
            continue
        write_prim(g, prim, tri.reshape(-1, 3) - off, n3.reshape(-1, 3), u3.reshape(-1, 2) if u3 is not None else None)
    # an emptied primitive is not legal glTF
    mesh = g.doc['meshes'][g.doc['nodes'][ci]['mesh']]
    mesh['primitives'] = [p for p in mesh['primitives'] if not p.get('_empty')]
    return report


def add_node(g, name, parent=None, translation=(0, 0, 0), mesh=None, extras=None):
    nm = names(g)
    if name in nm:
        n = g.doc['nodes'][nm[name]]
    else:
        n = dict(name=name)
        g.doc['nodes'].append(n)
        i = len(g.doc['nodes']) - 1
        if parent is None:
            g.doc['scenes'][0]['nodes'].append(i)
        else:
            g.doc['nodes'][nm[parent]].setdefault('children', []).append(i)
    n['translation'] = [float(v) for v in translation]
    if mesh is not None:
        n['mesh'] = mesh
    if extras:
        n.setdefault('extras', {}).update(extras)
    return n


def mesh_from(g, name, parts):
    """parts: [(material name, verts (n,3) in the node's frame, tris (m,3))] -> mesh index. Flat-shaded."""
    mats = {m['name']: k for k, m in enumerate(g.doc['materials'])}
    prims = []
    for mat, v, t in parts:
        v, t = np.asarray(v, float), np.asarray(t, int).reshape(-1, 3)
        p = v[t].reshape(-1, 3)
        n = np.cross(v[t[:, 1]] - v[t[:, 0]], v[t[:, 2]] - v[t[:, 0]])
        n /= np.maximum(np.linalg.norm(n, axis=1)[:, None], 1e-12)
        prims.append(dict(attributes=dict(POSITION=g.accessor(p.astype(np.float32), 'VEC3'),
                                          NORMAL=g.accessor(np.repeat(n, 3, 0).astype(np.float32), 'VEC3')),
                          material=mats[mat], mode=4))
    g.doc['meshes'].append(dict(name=name, primitives=prims))
    return len(g.doc['meshes']) - 1


def box(c, s):
    c, s = np.asarray(c, float), np.asarray(s, float) / 2
    v = np.array([[sx, sy, sz] for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]) * s + c
    t = [(0, 1, 3), (0, 3, 2), (4, 6, 7), (4, 7, 5), (0, 4, 5), (0, 5, 1), (2, 3, 7), (2, 7, 6), (0, 2, 6), (0, 6, 4), (1, 5, 7), (1, 7, 3)]
    return v, np.array(t)


def cyl_x(x0, x1, y, z, r, n=16):
    """closed cylinder along glTF x."""
    a = np.linspace(0, 2 * math.pi, n, endpoint=False)
    ring = lambda x: [(x, y + r * math.sin(t), z + r * math.cos(t)) for t in a]
    v = np.array(ring(x0) + ring(x1) + [(x0, y, z), (x1, y, z)])
    t = []
    for i in range(n):
        j = (i + 1) % n
        t += [(i, j, n + j), (i, n + j, n + i), (2 * n, j, i), (2 * n + 1, n + i, n + j)]
    return v, np.array(t)


def merge(*parts):
    vs, ts, o = [], [], 0
    for v, t in parts:
        vs.append(v); ts.append(np.asarray(t) + o); o += len(v)
    return np.concatenate(vs), np.concatenate(ts)


def drop_nodes(g, gone):
    """Delete nodes by name: drop every reference to them first, then renumber (CLAUDE.md, 'Removing a glTF node')."""
    d = g.doc
    kill = {i for i, n in enumerate(d['nodes']) if n['name'] in gone}
    m, k = {}, 0
    for i in range(len(d['nodes'])):
        if i not in kill:
            m[i] = k; k += 1
    for n in d['nodes']:
        if 'children' in n:
            n['children'] = [m[c] for c in n['children'] if c not in kill]
            if not n['children']: n.pop('children')
    for s in d['scenes']:
        s['nodes'] = [m[i] for i in s['nodes'] if i not in kill]
    assert not d.get('skins') and not d.get('animations'), 'node references outside the tree'
    d['nodes'] = [n for i, n in enumerate(d['nodes']) if i not in kill]
    return len(kill)


def control_lists(g, doc):
    return [g.doc['extras']['vrCockpitPhase3a']['controls'], doc['controls']]


def set_pivot(g, doc, node, pivot):
    for L in control_lists(g, doc):
        for c in L:
            if c['node'] == node: c['pivot_gltf'] = [float(v) for v in pivot]
    n = g.doc['nodes'][names(g)[node]]
    if 'interaction' in n.get('extras', {}): n['extras']['interaction']['pivot_gltf'] = [float(v) for v in pivot]
    for r in doc['rig']:
        if r['node'] == node: r['pivot_gltf'] = [float(v) for v in pivot]


def rear_panel(g, doc):
    rep = edit_interior(g)
    print('interior triangles', rep)
    nm = names(g)
    ir = g.world_matrices()[nm['Instruments_Rear']][:3, 3]
    # needles + the turn coordinator's symbol / ball ride with their instruments
    for n_, k in NEEDLES.items():
        z, y, _ = OLD[k]; nz, ny = NEW[k]
        t = g.doc['nodes'][nm[n_]]['translation']
        g.doc['nodes'][nm[n_]]['translation'] = [t[0], t[1] + ny - y, t[2] + nz - z]
        for r in doc['rig']:
            if r['node'] == n_: r['pivot_gltf'] = [r['pivot_gltf'][0], r['pivot_gltf'][1] + ny - y, r['pivot_gltf'][2] + nz - z]
    t = g.doc['nodes'][nm['Compass_Card_R']]['translation']
    g.doc['nodes'][nm['Compass_Card_R']]['translation'] = list(np.array(t) + COMPASS_D)
    for r in doc['rig']:
        if r['node'] == 'Compass_Card_R': r['pivot_gltf'] = list(np.array(r['pivot_gltf']) + COMPASS_D)
    # the radio heads' displays and their own (empty) knob markers
    for n_, h in (('Radio_Display_R', 'COM'), ('Radio_TuneInner_R', 'COM'), ('Radio_TuneOuter_R', 'COM'),
                  ('Radio_VolumeKnob_R', 'COM'), ('Xpdr_Display_R', 'XPDR'), ('Xpdr_Knob_R', 'XPDR')):
        d = np.array((0.0, AV_NEW[h][1] - AV_OLD[h][1], AV_NEW[h][0] - AV_OLD[h][0]))
        g.doc['nodes'][nm[n_]]['translation'] = list(np.array(g.doc['nodes'][nm[n_]]['translation']) + d)
        for disp in doc['displays']:
            if disp['node'] == n_: disp['centre_gltf'] = list(np.array(disp['centre_gltf']) + d)
    # the subpanel they hang on
    (x0, x1), (y0, y1), (z0, z1) = SUBPANEL['x'], SUBPANEL['y'], SUBPANEL['z']
    c = np.array(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2))
    v, t = box((0, 0, 0), (x1 - x0, y1 - y0, z1 - z0))
    add_node(g, 'GL_AvionicsSubpanel_R', translation=c, mesh=mesh_from(g, 'GL_AvionicsSubpanel_R', [('Cockpit_blank_plug', v, t)]),
             extras=dict(note='gl_panel_v2: Trig COM + transponder heads under the rear panel, right of the stick (Ty 2026-11-05)'))
    # radio knobs: the heads' own size, on the heads
    nm = names(g)
    for node, (h, u, vv, r, L) in KNOBS.items():
        z, y = head_point(h, u, vv)
        P = np.array((AV_FACE_X, y, z))
        kv, kt = cyl_x(0.0, L, 0.0, 0.0, r, 20)
        line = box((L + 0.0004, 0.6 * r, 0.0), (0.0008, 0.7 * r, 0.0012))
        parts = [('Cockpit_black', kv, kt)] + ([('Cockpit_hardware', *line)] if 'Rotary' in node else [])
        n = g.doc['nodes'][nm[node]]
        n['mesh'] = mesh_from(g, node, parts)
        n['translation'] = P.tolist()
        for ch in n.get('children', []):
            g.doc['nodes'][ch]['translation'] = [L, 0.0, 0.0]
        set_pivot(g, doc, node, P)
        mt = g.doc['nodes'][nm['Mount_' + node.split('_', 2)[2]]]
        mt.pop('mesh', None); mt['translation'] = P.tolist()
    # retired nodes + their control rows
    gone = set(RETIRED)
    for L in control_lists(g, doc):
        L[:] = [c for c in L if c['node'] not in gone]
    doc['rig'] = [r for r in doc['rig'] if r['node'] not in gone]
    print('dropped', drop_nodes(g, gone), 'nodes')


def cards(g):
    nm = names(g)
    for n_ in CARD_NODES:
        g.doc['nodes'][nm[n_]]['translation'] = list(REAR_CARD_T)
    for n_ in CARD_NODES:
        src = g.doc['nodes'][names(g)[n_]]
        add_node(g, 'FrontPanel_' + n_, translation=FRONT_CARD_T, mesh=src['mesh'],
                 extras=dict(card_link='shows the rear SequenceCard selection (cockpit-common card system)'))
    # a black backing plate behind the part of the front card that hangs below the panel's bottom edge (-0.060)
    zc, top = FRONT_CARD_ZC, -0.0605
    bot = FRONT_CARD_TOP - CARD_H - 0.008
    v, t = box((0, 0, 0), (0.004, top - bot, CARD_W + 0.016))
    add_node(g, 'FrontPanel_SequenceCard_Backing', translation=(FRONT_X - 0.0005 - 0.002, (top + bot) / 2, zc),
             mesh=mesh_from(g, 'FrontPanel_SequenceCard_Backing', [('Cockpit_panel_backing', v, t)]))


def instrument_hardware(g):
    """Altimeter knob + Kollsman window, G-meter reset (contract names; the common lane renders / binds them)."""
    def knob(name, parent, base, P, r, L, mat='Cockpit_black'):
        v, t = cyl_x(0.0, L, 0.0, 0.0, r, 20)
        add_node(g, name, parent, translation=np.array(P) - base, mesh=mesh_from(g, name, [(mat, v, t)]))

    def window(name, parent, base, P, w, h):
        v, t = box((0, 0, 0), (0.0006, h, w))
        add_node(g, name, parent, translation=np.array(P) - base, mesh=mesh_from(g, name, [('Cockpit_avionics_display', v, t)]))
    ir = np.array((REAR_X, 0.0, 0.0)); fr = np.array((FRONT_X, 0.0, 0.0))
    s = math.sqrt(0.5)
    az, ay = NEW['ALT']
    knob('Altimeter_Knob', 'Instruments_Rear', ir, (REAR_X + 0.002, ay - 0.047 * s, az + 0.047 * s), 0.0075, 0.012)
    window('Altimeter_Kollsman', 'Instruments_Rear', ir, (REAR_X + 0.0035, ay, az - 0.025), 0.011, 0.007)
    gz, gy = NEW['G']
    knob('G_meter_Reset', 'Instruments_Rear', ir, (REAR_X + 0.002, gy - 0.047 * s, gz + 0.047 * s), 0.0050, 0.008, 'Cockpit_knob_red')
    fz, fy = 0.2336, 0.030                                   # gl_cockpit.py front ALT
    knob('FrontPanel_Altimeter_Knob', 'Instruments_Front', fr, (FRONT_X + 0.002, fy - 0.047 * s, fz + 0.047 * s), 0.0075, 0.012)
    window('FrontPanel_Altimeter_Kollsman', 'Instruments_Front', fr, (FRONT_X + 0.0035, fy, fz - 0.025), 0.011, 0.007)


def main():
    GLB.write_bytes(base_bytes(GLB_REL))
    doc = json.loads(base_bytes(RIG_REL))
    g = Glb(GLB)
    g.material('Cockpit_panel_backing', (0.086, 0.086, 0.094))
    rear_panel(g, doc)
    cards(g)
    instrument_hardware(g)
    g.doc.setdefault('extras', {})['glPanelV2'] = ('Ty 2026-11-05: centred rear IAC card space, decluttered rear panel, radios under '
                                                   'the panel, front card, altimeter knob / Kollsman / G reset nodes (gl_panel_v2.py)')
    validate(g, GLB)
    g.save(GLB)
    RIG.write_text(json.dumps(doc, indent=1) + '\n')
    mirror = ROOT / 'unity/Assets/StreamingAssets/rigs/great-lakes-like-own.json'
    if mirror.parent.is_dir():
        mirror.write_text(RIG.read_text())
    print('wrote', GLB.name, GLB.stat().st_size)


if __name__ == '__main__':
    main()
