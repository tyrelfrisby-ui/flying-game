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
        t = outward(v, t)
        p = v[t].reshape(-1, 3)
        n = np.cross(v[t[:, 1]] - v[t[:, 0]], v[t[:, 2]] - v[t[:, 0]])
        n /= np.maximum(np.linalg.norm(n, axis=1)[:, None], 1e-12)
        prims.append(dict(attributes=dict(POSITION=g.accessor(p.astype(np.float32), 'VEC3'),
                                          NORMAL=g.accessor(np.repeat(n, 3, 0).astype(np.float32), 'VEC3')),
                          material=mats[mat], mode=4))
    g.doc['meshes'].append(dict(name=name, primitives=prims))
    return len(g.doc['meshes']) - 1


def outward(v, t):
    """Every closed convex piece (a connected set of triangles) wound so its faces point away from its own centre:
    single-sided materials cull by winding (CLAUDE.md, 'CPU GLB export preserves the winding you supply')."""
    par = list(range(len(v)))
    def find(a):
        while par[a] != a:
            par[a] = par[par[a]]; a = par[a]
        return a
    for a, b, c in t:
        for x_ in (b, c):
            ra, rb = find(a), find(x_)
            if ra != rb: par[ra] = rb
    root = np.array([find(a) for a in t[:, 0]])
    t = t.copy()
    for r in set(root.tolist()):
        sel = root == r
        idx = np.unique(t[sel])
        centre = v[idx].mean(0)
        tri = v[t[sel]]
        n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
        flip = np.einsum('ij,ij->i', n, tri.mean(1) - centre) < 0
        sub = t[sel]; sub[flip] = sub[flip][:, ::-1]; t[sel] = sub
    return t


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


# ---- the switch consoles: two MAG toggles join the six, every switch labelled below / aft, "ON" above, ON = FORWARD ----
# Ty 2026-11-05: "the label goes BELOW/AFT of each switch, with the word ON above each switch, so you know what it is and
# which way is on. ON = FORWARD. Shrink the switches as needed." The lid (gl_quadrant_v3's GL_SwitchStrip_*, 130 x 70 mm)
# takes four fore/aft columns of two: the MAGS in the aft column, nearest the hand, L outboard (the pilot's left) and R
# inboard; then MASTER / ALT, FUEL PUMP / NAV, STROBE / LDG. Each 32.5 mm cell: "ON" 14 mm forward of the pivot, the
# switch, its name 12 mm aft. Text faces up and reads left to right for a pilot looking FORWARD (its "up" is forward).
CONSOLE = [('MagL', 'MAG L', 'mag_left'), ('MagR', 'MAG R', 'mag_right'), ('Master', 'MASTER', None), ('Alternator', 'ALT', None),
           ('FuelPump', 'F PUMP', None), ('NavLights', 'NAV', None), ('Strobe', 'STROBE', None), ('LandingLight', 'LDG', None)]
COL_DX = (-0.0525, -0.0175, 0.0175, 0.0525)          # builder x of the four columns from the lid centre, aft -> forward
ROW_DY = (0.017, -0.017)                             # outboard, inboard
ON_DX, NAME_DX = 0.0135, -0.0135                     # "ON" forward of the pivot, the name aft of it
ON_W = 0.016                                         # "ON" is the smaller word
TIP = np.array((-0.0065, 0.0, 0.016))                # OFF: the stem leans aft; +80 deg about builder +y swings it forward
CELL_W, CELL_H, CELL_SC = 44, 9, 6                   # label cell, font px (7 chars of 6 px, one line), image scale
LABEL_W = 0.026


def label_atlas(texts):
    import gl_quadrant_v3 as q
    cells = []
    for t in texts:
        img = np.zeros((CELL_H, CELL_W, 3), np.uint8); img[:] = (12, 12, 14)
        left = (CELL_W - (len(t) * 6 - 1)) // 2
        for k, ch in enumerate(t):
            for y, row in enumerate(q.F[ch].split()):
                for x, b in enumerate(row):
                    if b == '1': img[1 + y, left + k * 6 + x] = (245, 245, 240)
        cells.append(np.kron(img, np.ones((CELL_SC, CELL_SC, 1), np.uint8)))
    n = len(texts)
    return q.png(np.concatenate(cells, 0)), {t: (0.0, i / n, 1.0, (i + 1) / n) for i, t in enumerate(texts)}


def consoles(g, doc):
    import copy
    import tempfile
    import gl_quadrant_v3 as q
    from vr_cockpit import Shape, gl
    texts = ['ON'] + [lab for _, lab, _ in CONSOLE]
    data, uv = label_atlas(texts)
    uv['ON_'] = (0.25, uv['ON'][1], 0.75, uv['ON'][3])         # the middle half of the ON cell: the word, less padding
    with tempfile.TemporaryDirectory() as td:
        pth = Path(td) / 'gl-switch-labels.png'; pth.write_bytes(data)
        g.material('GL_SwitchLabels', (1, 1, 1), png=pth)
    h = LABEL_W * CELL_H / CELL_W
    controls = control_lists(g, doc)
    for seat, S in q.SEATS.items():
        nm = names(g)
        nodes = g.doc['nodes']
        bx, by, z = S['px'] - 0.200, 0.305, S['ztop'] - 0.025
        labels, luv, metal, black = Shape(), [], Shape(), Shape()
        master = next(c for c in controls[0] if c['seat'] == seat and c['param'] == 'master')
        master_row = next(r for r in doc['rig'] if r['node'] == master['node'])
        for j, (short, lab, param) in enumerate(CONSOLE):
            col, row = divmod(j, 2)
            P = np.array((bx + COL_DX[col], by + ROW_DY[row], z + 0.002))
            node = 'Ctl_Toggle_' + short + S['ctl']
            if param is not None and node not in nm:                       # the two new MAG toggles: the master's copy
                for L in controls:
                    c = copy.deepcopy(next(c for c in L if c['node'] == master['node']))
                    c.update(node=node, param=param, grip='Grip_' + short + S['ctl'], poke='Poke_' + short + S['ctl'])
                    L.append(c)
                row_ = copy.deepcopy(master_row); row_.update(node=node, param=param); doc['rig'].append(row_)
                add_node(g, node)
                for ch in ('Grip_', 'Poke_'):
                    add_node(g, ch + short + S['ctl'], parent=node)
                nm = names(g); nodes = g.doc['nodes']
                nodes[nm['Grip_' + short + S['ctl']]]['rotation'] = [0, 1, 0, 0]
            stem = Shape(); tip = P + TIP
            stem.tube(P, tip, 0.0022, 12); stem.sphere(tip, 0.0026, 12, 8)
            q.set_mesh(g, node, [('P3_ControlMetal', stem, None)], P)
            n = nodes[nm[node]]
            n['translation'] = gl(P).tolist()
            for ch in n.get('children', []):
                nodes[ch]['translation'] = gl(tip - P).tolist()
            black.box(P + (0, 0, -0.00075), (0.016, 0.016, 0.0015)); metal.tube(P, P + (0, 0, 0.003), 0.0045, 12)
            q.quad(labels, luv, P + (ON_DX, 0, 0.0002), (0, -1, 0), (1, 0, 0), ON_W, ON_W * CELL_H / CELL_W * 1.6, uv['ON_'])
            q.quad(labels, luv, P + (NAME_DX, 0, 0.0002), (0, -1, 0), (1, 0, 0), LABEL_W, h, uv[lab])
            placement = 'gl_panel_v2: lid 4 x 2 fore/aft toggles; ON = FORWARD, "ON" above, name below (Ty 2026-11-05)'
            for L in controls:
                for c in L:
                    if c['node'] == node:
                        c.update(kind='Toggle', axis_gltf=[0., 0., 1.], pivot_gltf=gl(P).tolist(), travel=80, travel_unit='deg',
                                 placement=placement, label=lab)
            ex = n.setdefault('extras', {})
            ex.update(rig_param=param or ex.get('rig_param'), rig_axis_gltf=[0., 0., 1.], rig_map_param=[0., 1.], rig_map_deg=[0., 80.],
                      rig_param_model=0., interaction=next(c for c in controls[0] if c['node'] == node))
            for r in doc['rig']:
                if r['node'] == node:
                    r.update(kind='rot', axis_gltf=[0., 0., 1.], pivot_gltf=gl(P).tolist(), map=[[0, 0], [1, 80]], model=0., desc=placement)
            mt = nm.get('Mount_' + short + S['ctl'])
            if mt is not None:
                nodes[mt].pop('mesh', None); nodes[mt]['translation'] = gl(P).tolist()
        for k in range(3):                                         # thin dividers between the columns: each ON goes with its own switch
            metal.box((bx + 0.5 * (COL_DX[k] + COL_DX[k + 1]), by, z + 0.0021), (0.0012, 0.062, 0.0004))
        # the six breakers on the inboard face, as v7 had them, relabelled from the same atlas
        for j, lab in enumerate(['MASTER', 'ALT', 'F PUMP', 'NAV', 'STROBE', 'LDG']):
            row, col = divmod(j, 3)
            B = np.array((bx - .027 + col * .027, by - .037, z - .020 - row * .034))
            black.box(B + (0, .001, 0), (.026, .003, .031))
            metal.tube(B, B + (0, -.004, 0), .006, 16); black.tube(B + (0, -.004, 0), B + (0, -.012, 0), .0045, 12)
            q.quad(labels, luv, B + (0, -.004, -.011), (1, 0, 0), (0, 0, 1), LABEL_W, h, uv[lab])
        hw = 'GL_ConsoleHardware_' + S['sfx']
        q.set_mesh(g, hw, [('P3_ControlMetal', metal, None), ('Cockpit_black', black, None), ('GL_SwitchLabels', labels, luv)], (bx, by, z))
        nodes[names(g)[hw]]['translation'] = gl(np.array((bx, by, z))).tolist()
        nodes[names(g)[hw]]['extras'] = {'switch_order': [p or {'Master': 'master', 'Alternator': 'alternator', 'FuelPump': 'fuel_pump', 'NavLights': 'nav_lights',
                                                                 'Strobe': 'strobe', 'LandingLight': 'landing_light'}[c] for c, _, p in CONSOLE], 'layout': [4, 2], 'forward_on': True,
                                         'labels': 'name aft of each switch, ON forward of it', 'switch_surface': 'top', 'breaker_surface': 'inboard face',
                                         'breaker_order': ['master', 'alternator', 'fuel_pump', 'nav_lights', 'strobe', 'landing_light']}


# ---- the fuel sight gauge: glass tube down from the top-wing tank beside the right aft cabane fitting ---------------
# Ty 2026-11-05: "Glass fuel sight tubes coming down from the tank: clear glass with graduations for 3/4, 1/2, 1/4; a red
# ball on a wire goes up into the tank and floats on the fuel with a cork, so it drops in the sight tube as fuel burns. Put
# the sight gauge on the RIGHT side where the cabane attaches to the wing at the rear spar (a leak stays away from the pilot
# with spiral slipstream)." The right aft cabane leg meets the wing at glTF (0.801, 0.620, -0.700); the tube hangs 35 mm
# AFT of it, on the front seat's side, so the leg is behind it from both seats (36 mm ahead, the first try, put the leg
# right across it from the front eye), and beside it, not inboard, because the leg slopes inboard as it goes down. GL_FuelSight_Float (ball + wire +
# cork) is one rig node sliding along +y with fuel_top_wing_frac: the cork rides the fuel in the tank, the ball 150 mm
# below it on the wire. Linear in the fraction (the centre-section tank is a flat box). Its scale plate faces the front seat.
SIGHT_X, SIGHT_Z = 0.836, -0.690
SIGHT_TOP, SIGHT_BOT = 0.600, 0.470                     # the glass
BALL_FULL, BALL_EMPTY = 0.590, 0.482                    # ball centre at full / empty
BALL_R, GLASS_R, WIRE_L = 0.0055, 0.0085, 0.150
MARKS = (('1/4', 0.25), ('1/2', 0.50), ('3/4', 0.75))


def cyl_y(y0, y1, x, z, r, n=20):
    v, t = cyl_x(y0, y1, 0.0, 0.0, r, n)
    return np.c_[v[:, 1] + x, v[:, 0], v[:, 2] + z], t[:, ::-1]


def sphere(c, r, n=16, m=10):
    v = [np.array(c) + r * np.array((math.sin(p) * math.cos(a), math.cos(p), math.sin(p) * math.sin(a)))
         for p in np.linspace(0, math.pi, m + 1) for a in np.linspace(0, 2 * math.pi, n, endpoint=False)]
    t = []
    for j in range(m):
        for i in range(n):
            a, b = j * n + i, j * n + (i + 1) % n
            t += [(a, b + n, b), (a, a + n, b + n)]
    return np.array(v), np.array(t)


def sight_gauge(g, doc):
    import gl_quadrant_v3 as q
    import tempfile
    q.F.update({'1': '00100 01100 00100 00100 00100 00100 01110', '2': '01110 10001 00001 00010 00100 01000 11111',
                '3': '11110 00001 00001 01110 00001 00001 11110', '4': '00010 00110 01010 10010 11111 00010 00010',
                '/': '00001 00010 00010 00100 01000 01000 10000'})
    data, uv = label_atlas([m for m, _ in MARKS])
    with tempfile.TemporaryDirectory() as td:
        pth = Path(td) / 'gl-fuel-sight-marks.png'; pth.write_bytes(data)
        g.material('GL_FuelSight_Labels', (1, 1, 1), png=pth)
    next(m for m in g.doc['materials'] if m['name'] == 'GL_FuelSight_Labels')['doubleSided'] = True   # faces the seat from either side
    mats = g.doc['materials']
    g.material('GL_FuelSight_Glass', (0.95, 0.98, 1.0))
    m = next(m for m in mats if m['name'] == 'GL_FuelSight_Glass')
    m.update(alphaMode='BLEND', doubleSided=True); m['pbrMetallicRoughness'].update(baseColorFactor=[0.95, 0.98, 1.0, 0.18], roughnessFactor=0.04)
    g.material('GL_FuelSight_Ball', (0.86, 0.05, 0.04)); g.material('GL_FuelSight_Cork', (0.63, 0.46, 0.29))
    # the scale plate faces the front seat's eye across the tube (horizontal); "right" is the viewer's right
    eye = np.array((1.22, 0.27, 0.0))
    dh = eye - (SIGHT_X, 0.0, SIGHT_Z); dh[1] = 0.0; dh /= np.linalg.norm(dh)
    right = np.cross(-dh, (0.0, 1.0, 0.0)); right /= np.linalg.norm(right)    # looking along -dh with +y up
    up = np.array((0.0, 1.0, 0.0))
    c0 = np.array((SIGHT_X, 0.0, SIGHT_Z))

    def slab(centre, a, b, n, sa, sb, sn):
        """box with half-extents along three orthogonal unit vectors"""
        v = np.array([centre + i * sa * a + j * sb * b + k * sn * n for i in (-1, 1) for j in (-1, 1) for k in (-1, 1)])
        t = [(0, 1, 3), (0, 3, 2), (4, 6, 7), (4, 7, 5), (0, 4, 5), (0, 5, 1), (2, 3, 7), (2, 7, 6), (0, 2, 6), (0, 6, 4), (1, 5, 7), (1, 7, 3)]
        return v, np.array(t)
    glass = cyl_y(SIGHT_BOT, SIGHT_TOP, SIGHT_X, SIGHT_Z, GLASS_R, 24)
    metal = merge(cyl_y(SIGHT_TOP, 0.6205, SIGHT_X, SIGHT_Z, 0.0115, 20), cyl_y(SIGHT_BOT - 0.014, SIGHT_BOT, SIGHT_X, SIGHT_Z, 0.0115, 20),
                  cyl_y(SIGHT_BOT - 0.024, SIGHT_BOT - 0.014, SIGHT_X, SIGHT_Z, 0.004, 12))           # top boss, bottom cap, drain
    yl, yh = SIGHT_BOT - 0.010, SIGHT_TOP + 0.005
    pc = c0 - 0.0135 * dh + 0.016 * right + up * (yl + yh) / 2
    plate = slab(pc, right, up, dh, 0.030, (yh - yl) / 2, 0.001)
    from vr_cockpit import builder
    ticks, rings, labels, luv = [], [], Shape_(), []
    for lab, f in MARKS:
        y = BALL_EMPTY + f * (BALL_FULL - BALL_EMPTY)
        ticks.append(slab(c0 - 0.0122 * dh + up * y + 0.004 * right, right, up, dh, 0.016, 0.0008, 0.0004))
        rings.append(cyl_y(y - 0.0006, y + 0.0006, SIGHT_X, SIGHT_Z, GLASS_R + 0.0004, 24))
        q.quad(labels, luv, builder(c0 - 0.0122 * dh + up * y + 0.033 * right), builder(right), builder(up), 0.026, 0.026 * CELL_H / CELL_W, uv[lab])
    black = merge(*ticks, *rings)
    add_node(g, 'GL_FuelSight', translation=(0, 0, 0), extras=dict(
        note='gl_panel_v2: top-wing tank sight gauge (Ty 2026-11-05), right aft cabane fitting; marks 1/4 1/2 3/4 of the tank',
        mark_heights_gltf_y={lab: round(BALL_EMPTY + f * (BALL_FULL - BALL_EMPTY), 4) for lab, f in MARKS}),
        mesh=mesh_from(g, 'GL_FuelSight', [('GL_FuelSight_Glass', *glass), ('P3_ControlMetal', *metal), ('GL_Windshield_Trim_Aluminium', *plate),
                                           ('Cockpit_black', *black)]))
    # the labels need UVs: author them with the quadrant module's set_mesh (builder frame)
    lab_node = add_node(g, 'GL_FuelSight_Marks')
    q.set_mesh(g, 'GL_FuelSight_Marks', [('GL_FuelSight_Labels', labels, luv)], np.zeros(3))
    lab_node['translation'] = [0.0, 0.0, 0.0]
    # the float: ball on the wire, cork in the tank; origin = the ball's centre at FULL
    rest = np.array((SIGHT_X, BALL_FULL, SIGHT_Z))
    ball = sphere((0, 0, 0), BALL_R)
    wire = cyl_y(BALL_R * 0.8, WIRE_L, 0.0, 0.0, 0.0006, 8)
    cork = cyl_y(WIRE_L, WIRE_L + 0.016, 0.0, 0.0, 0.0075, 16)
    add_node(g, 'GL_FuelSight_Float', translation=rest,
             mesh=mesh_from(g, 'GL_FuelSight_Float', [('GL_FuelSight_Ball', *ball), ('Wire_steel', *wire), ('GL_FuelSight_Cork', *cork)]),
             extras=dict(rig_param='fuel_top_wing_frac', rig_move_axis_gltf=[0.0, 1.0, 0.0], rig_map_param=[0.0, 1.0],
                         rig_map_m=[round(BALL_EMPTY - BALL_FULL, 4), 0.0], rig_param_model=1.0))
    doc['rig'] = [r for r in doc['rig'] if r['node'] != 'GL_FuelSight_Float']
    doc['rig'].append(dict(node='GL_FuelSight_Float', parent=None, param='fuel_top_wing_frac', unit='0..1', kind='move',
                           axis_gltf=[0.0, 1.0, 0.0], pivot_gltf=rest.tolist(), map=[[0.0, round(BALL_EMPTY - BALL_FULL, 4)], [1.0, 0.0]],
                           model=1.0, desc='fuel sight gauge: red ball on the cork float\'s wire, modelled FULL; drops as the top-wing tank burns '
                                            '(Ty 2026-11-05). Marks 1/4 1/2 3/4 at the ball heights for those fractions.',
                           link='RigInputs.TankFraction(load, "topWing")', note='', extra={}))


def Shape_():
    from vr_cockpit import Shape
    return Shape()


def main():
    GLB.write_bytes(base_bytes(GLB_REL))
    doc = json.loads(base_bytes(RIG_REL))
    g = Glb(GLB)
    g.material('Cockpit_panel_backing', (0.086, 0.086, 0.094))
    rear_panel(g, doc)
    cards(g)
    instrument_hardware(g)
    consoles(g, doc)
    sight_gauge(g, doc)
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
