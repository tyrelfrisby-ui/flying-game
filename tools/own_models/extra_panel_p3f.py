"""Extra 300 panels P3f (Ty 2026-11-03, seen on the Quest): "the Extra's instrument panel is a little too tall and its
stick is too long", "put ONE row of switches along the bottom of the Extra's instrument panel", and "the rear
instrument panel floats, with gaps on its left and right sides: extend it so it attaches to both sides of the
fuselage". CPU only, numpy; deterministic and idempotent (a GLB already carrying extras.vrCockpitPhase3f is skipped).

Reference (the only dimensioned evidence found; no Extra document gives a panel height or a stick length in text:
the Extra 300L Information Manual Fig. 7.6.1 and the Illustrated Parts Catalogue 46204.000-LK / 86502.008-01 carry
none): Wikimedia Commons "Extra 330 LX cockpit.jpg" (rear seat, same rear-panel family as the 300L), scaled from its
3-1/8 in airspeed indicator: the upper rear panel, which carries the gauges and ONE row of switches along its bottom
edge, is ~0.25 m tall, bolted to the frame tubes on both sides; a narrower centre subpanel hangs below it with the
radio / intercom equipment. "Cockpit Extra330LC (4435531411).jpg" shows the same arrangement on a bare frame.

What this does, per seat (Rear: panel plane x -1.175, Front: x -0.150; builder X fwd, Y left, Z up, metres):
  * Panel_P3b_<s>: rebuilt as a slab that runs out to the cockpit side walls (measured by slicing Cockpit_Shell /
    Fuselage_Painted / Canopy_Glass at the panel plane, 4 mm clearance) from z 0.085 to the old top 0.360.
    Before: 0.44 m wide (y +-0.22), 0.285 m tall, plus the 0.153 m P3e switch subpanel below it = 0.438 m of panel
    face from z -0.078. After: 0.275 m of full-width panel face (z 0.085..0.360).
  * Subpanel_P3e_Switches_<s> retired (mesh dropped, node kept: no renumbering).
  * The three 2-1/4 in gauges (Amps, Fuel_Wing, Fuel_Akro) move from the bottom row into the new side wings;
    faces, bezels, needles, rig pivots and the phase3b metadata move with them.
  * ONE row of the 12 switch controls along the bottom edge (z 0.115, y +-0.274, ~50 mm pitch).
  * The 10 COM / XPDR knobs (Ty: radios become tap-to-type keypads; they are not switches) go in one line on a new
    centre radio subpanel Subpanel_P3f_Radio_<s> (y +-0.172, z -0.035..0.087) below the panel, under the radio and
    transponder displays (rear) - the real aeroplane's lower centre subpanel.
  * Sticks: 0.06 m shorter (shaft only; grip, button, boot and pivot unchanged; Grip_Stick_* follows). Rear pivot to
    grip top 0.481 -> 0.421 m, front 0.461 -> 0.401 m. Basis: in both photographs the rear grip top, seen from behind
    the seat, lines up with the bottom edge of the main panel and never reaches the gauge rows; from our rear eye the
    old grip top projected onto the panel at z 0.173 (the middle gauge row). Putting it on the new panel's bottom
    edge needs 0.048 m; the 330 LX photo's projection (onto the lower subpanel) implies ~0.078 m; 0.06 m is between.
    An estimate from photographs, not a published dimension.

  PYTHONDONTWRITEBYTECODE=1 /usr/bin/python3 tools/own_models/extra_panel_p3f.py
"""
import json
import numpy as np
from vr_cockpit import Glb, Shape, MODELS, ROOT, gl, builder
from drop_card_slot import validate

SEATS = {'Rear': dict(sfx='R', ctl='', x=-1.175), 'Front': dict(sfx='F', ctl='_Front', x=-.150)}
BOTTOM, TOP = .085, .360
ROW_Z, ROW_HALF = .115, .274
ROW = ['Magneto', 'Master', 'FuelPump', 'Smoke', 'AlternateAir', 'Mixture', 'Prop', 'Trim', 'FuelSelector',
       'NavLights', 'Strobe', 'LandingLight']                     # pilot's left to right
RADIO = ['ComOuter', 'ComInner', 'ComVolume', 'ComSwap', 'XpdrIdent', 'XpdrMode',
         'XpdrDigit1', 'XpdrDigit2', 'XpdrDigit3', 'XpdrDigit4']   # pilot's left to right (XPDR display is left)
RADIO_Z, RADIO_HALF, SUB = .005, .1395, (-.172, .172, -.035, BOTTOM + .002)
DISPLAY_DZ = .010                                                  # radio / xpdr displays up 10 mm on the subpanel
# gauge: new (u, v); u + is the pilot's RIGHT (builder -y), as in vr_phase3b.LAYOUT
MOVE = {'Amps': (.250, .315), 'Fuel_Wing': (-.250, .260), 'Fuel_Akro': (-.250, .190)}   # the IAC card holder covers the right wing below z .273
STICK_CUT = .060
WALL_CLEAR = .004
WALL_NODES = ('Cockpit_Shell', 'Fuselage_Painted', 'Canopy_Glass', 'Canopy_Frame', 'Canopy_Sill')


def tris_of(g, world, i):
    n = g.doc['nodes'][i]; m = world[i]; out = []
    for p in g.doc['meshes'][n['mesh']]['primitives']:
        v = np.asarray(g.array(p['attributes']['POSITION']), float) @ m[:3, :3].T + m[:3, 3]
        b = np.stack([-v[:, 0], v[:, 2], v[:, 1]], 1)
        t = np.asarray(g.array(p['indices'])).reshape(-1, 3) if 'indices' in p else np.arange(len(b)).reshape(-1, 3)
        out.append(b[t])
    return np.concatenate(out) if out else np.zeros((0, 3, 3))


def wall_halfwidth(g, x, zs):
    """Innermost |y| of the cockpit sides at builder x, for each z (slice of the side-wall meshes)."""
    world = g.world_matrices(); segs = []
    for i, n in enumerate(g.doc['nodes']):
        if 'mesh' not in n or not n['name'].startswith(WALL_NODES): continue
        for tri in tris_of(g, world, i):
            d = tri[:, 0] - x; pts = []
            for a, c in ((0, 1), (1, 2), (2, 0)):
                if (d[a] < 0) != (d[c] < 0): s = d[a] / (d[a] - d[c]); pts.append(tri[a] + s * (tri[c] - tri[a]))
            if len(pts) == 2: segs.append((pts[0][1:], pts[1][1:]))
    out = []
    for z in zs:
        ys = [abs(a[0] + (z - a[1]) / (b[1] - a[1]) * (b[0] - a[0])) for a, b in segs
              if (a[1] - z) * (b[1] - z) <= 0 and a[1] != b[1]]
        ys = [y for y in ys if y > .15]
        out.append(min(ys))
    return np.array(out)


def idx(g, name): return next(i for i, n in enumerate(g.doc['nodes']) if n.get('name') == name)


def shift(g, name, d_builder):
    n = g.doc['nodes'][idx(g, name)]
    n['translation'] = (np.array(n.get('translation', [0, 0, 0]), float) + gl(d_builder)).tolist()


def slab(x, poly_levels, th=.008):
    """Panel slab: aft face at x, th forward; outline y-monotone in z: [(z, half_width)] bottom to top."""
    s = Shape()
    for (z0, w0), (z1, w1) in zip(poly_levels, poly_levels[1:]):
        for xf, flip in ((x, False), (x + th, True)):
            q = [(xf, w0, z0), (xf, -w0, z0), (xf, -w1, z1), (xf, w1, z1)]
            s.add(q, [(0, 2, 1), (0, 3, 2)] if not flip else [(0, 1, 2), (0, 2, 3)])
        for sg in (1, -1):                                        # side edges
            q = [(x, sg * w0, z0), (x + th, sg * w0, z0), (x + th, sg * w1, z1), (x, sg * w1, z1)]
            s.add(q, [(0, 1, 2), (0, 2, 3)] if sg > 0 else [(0, 2, 1), (0, 3, 2)])
    (zb, wb), (zt, wt) = poly_levels[0], poly_levels[-1]
    s.add([(x, -wb, zb), (x, wb, zb), (x + th, wb, zb), (x + th, -wb, zb)], [(0, 1, 2), (0, 2, 3)])
    s.add([(x, wt, zt), (x, -wt, zt), (x + th, -wt, zt), (x + th, wt, zt)], [(0, 1, 2), (0, 2, 3)])
    return s


def place_control(g, doc, c, p):
    nodes = g.doc['nodes']; n = nodes[idx(g, c['node'])]
    n['translation'] = gl(p).tolist(); c['pivot_gltf'] = gl(p).tolist()
    nodes[idx(g, 'Mount_' + c['grip'][5:])]['translation'] = gl(p).tolist()
    c['position_status'] = 'E'
    c['placement'] = 'extra_panel_p3f: one switch row along the bottom of the panel' if c['grip'][5:].replace('_Front', '') in ROW \
        else 'extra_panel_p3f: one line on the centre radio subpanel'
    n.setdefault('extras', {})['interaction'] = c
    for e in doc['rig']:
        if e['node'] == c['node']: e['pivot_gltf'] = c['pivot_gltf']


def shorten_stick(g, name):
    """Move the grip, button and the shaft's top ring STICK_CUT down the shaft (node-local, so the pivot stays)."""
    i = idx(g, name); n = g.doc['nodes'][i]
    prims = g.doc['meshes'][n['mesh']]['primitives']
    shaft = next(p for p in prims if g.doc['materials'][p['material']]['name'] == 'Cockpit_stick')
    v = np.asarray(g.array(shaft['attributes']['POSITION']), float)
    lo, hi = v[v[:, 1] < v[:, 1].min() + .01].mean(0), v[v[:, 1] > v[:, 1].max() - .01].mean(0)
    d = (hi - lo) / np.linalg.norm(hi - lo); cut = hi[1] - .02
    before = max(float(np.asarray(g.array(p['attributes']['POSITION']))[:, 1].max()) for p in prims)
    for p in prims:
        v = np.asarray(g.array(p['attributes']['POSITION']), float)
        sel = v[:, 1] >= cut
        if not sel.any(): continue
        v[sel] -= STICK_CUT * d
        p['attributes']['POSITION'] = g.accessor(v, 'VEC3')
    for c in n.get('children', []):
        k = g.doc['nodes'][c]
        if k['name'].startswith('Grip_Stick'): k['translation'] = (np.array(k['translation']) - STICK_CUT * d).tolist()
    return before, d


def update(path, doc, write_doc):
    g = Glb(path)
    if g.doc['extras'].get('vrCockpitPhase3f'): print('EXTRA', path.name, 'already P3f'); return False
    world = g.world_matrices()
    meta = g.doc['extras']['vrCockpitPhase3b']; controls = g.doc['extras']['vrCockpitPhase3a']['controls']
    zs = np.round(np.arange(BOTTOM, TOP + 1e-9, .005), 4)
    report = {}
    for seat, s in SEATS.items():
        sfx, x = s['sfx'], s['x']
        half = wall_halfwidth(g, x, zs) - WALL_CLEAR
        levels = list(zip(zs.tolist(), half.tolist()))
        # The panel slab, in its node's frame (node at its old origin).
        pn = 'Panel_P3b_' + sfx; q = world[idx(g, pn)][:3, 3]; origin = builder(q)
        sh = slab(x, levels)
        g.mesh(pn, [gl(v) - q for v in sh.v], sh.t, next(i for i, m in enumerate(g.doc['materials']) if m['name'] == 'P3b_Panel'))
        sub = 'Subpanel_P3e_Switches_' + sfx
        g.doc['nodes'][idx(g, sub)].pop('mesh', None)
        g.doc['nodes'][idx(g, sub)]['extras'] = {'p3f_retired': True, 'superseded_by': pn}
        # The small gauges into the wings.
        panel = next(p for p in meta['panels'] if p['seat'] == seat)
        for row in panel['gauges']:
            if row['gauge'] not in MOVE: continue
            (u0, v0), (u1, v1) = (row['u'], row['v']), MOVE[row['gauge']]
            d = np.array((0., -(u1 - u0), v1 - v0))
            for nm in ['Face_P3b_' + row['gauge'] + '_' + sfx, 'Bezel_P3b_' + row['gauge'] + '_' + sfx] + row['needles']:
                shift(g, nm, d)
                for e in doc['rig']:
                    if e['node'] == nm: e['pivot_gltf'] = (np.array(e['pivot_gltf']) + gl(d)).tolist()
            row['u'], row['v'] = u1, v1
        panel['outline_zw'] = [[round(z, 4), round(w, 4)] for z, w in levels]
        # Centre radio subpanel.
        y0, y1, z0, z1 = SUB; sh = Shape(); sh.box((x + .004, 0, (z0 + z1) / 2), (.008, y1 - y0, z1 - z0))
        rn = 'Subpanel_P3f_Radio_' + sfx
        g.mesh(rn, [gl(v) for v in sh.v], sh.t, next(i for i, m in enumerate(g.doc['materials']) if m['name'] == 'P3b_Panel'))
        g.doc['nodes'][idx(g, rn)]['extras'] = {'p3f': 'centre radio subpanel below the rear/front panel'}
        if sfx == 'R':
            for nm in ('Radio_Display_R', 'Xpdr_Display_R'):
                for k in (nm, 'Subpanel_' + nm): shift(g, k, np.array((0, 0, DISPLAY_DZ)))
                for disp in doc.get('displays', []):
                    if disp['node'] == nm: disp['centre_gltf'] = (np.array(disp['centre_gltf']) + gl((0, 0, DISPLAY_DZ))).tolist()
        # Controls.
        by = {c['grip'][5:].replace('_Front', ''): c for c in controls if c['seat'] == seat}
        assert set(by) == set(ROW) | set(RADIO), set(by) ^ (set(ROW) | set(RADIO))
        for k, lab in enumerate(ROW):
            place_control(g, doc, by[lab], (x - .001, ROW_HALF - k * 2 * ROW_HALF / (len(ROW) - 1), ROW_Z))
        for k, lab in enumerate(RADIO):
            place_control(g, doc, by[lab], (x - .001, RADIO_HALF - k * 2 * RADIO_HALF / (len(RADIO) - 1), RADIO_Z))
        report[seat] = dict(panel_bottom=BOTTOM, panel_top=TOP, halfwidth_at_bottom=round(levels[0][1], 3),
                            halfwidth_max=round(max(w for _, w in levels), 3))
    # Sticks.
    for nm in ('Stick_R', 'Stick_F'):
        before, d = shorten_stick(g, nm); report[nm] = dict(local_top_before=round(before, 3), cut=STICK_CUT)
    doc['controls'] = controls
    g.doc['extras']['vrCockpitPhase3f'] = dict(
        owner='Ty 2026-11-03: panel a little too tall, stick too long, one switch row, rear panel side gaps',
        panel_bottom_m=BOTTOM, panel_top_m=TOP, row_z_m=ROW_Z, row_half_m=ROW_HALF, radio_subpanel=list(SUB),
        stick_cut_m=STICK_CUT, gauges_moved=MOVE, report=report,
        reference='Wikimedia Commons "Extra 330 LX cockpit.jpg", "Cockpit Extra330LC (4435531411).jpg"; see the script docstring')
    doc['phase3b'] = meta
    validate(g, path); g.save(path)
    print('EXTRA', path.name, json.dumps(report))
    return True


if __name__ == '__main__':
    # The three liveries share one rig table: shift its pivots once (first livery), apply the same edit to the rest.
    rigpath = ROOT / 'docs/models/ea300-own-rig.json'
    base = rigpath.read_text(); doc = json.loads(base); wrote = False
    for k, p in enumerate(sorted((MODELS / 'extra-300-like').glob('*own*.glb'))):
        wrote |= update(p, doc if k == 0 else json.loads(base), k == 0)
    if wrote: rigpath.write_text(json.dumps(doc, indent=1) + '\n')
