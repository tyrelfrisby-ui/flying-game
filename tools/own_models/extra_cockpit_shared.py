"""Extra 300 cockpit, the shared one (iOS/Mac and Quest), pass 11 — Ty 2026-11-04 05:35 and 07:33.

  "the instrument panel needs the sequence card centered with the instruments laid out around it so airspeed and g meter
   on the left stacked vertical altitude and tc on the right stacked vertical 3-1/8 instruments tiny trig style radio and
   transponder on the left 3-1/8 oil pressure oil temperature and fuel pressure 3 in one on the right. lower row tach
   manifold pressure fuel computer/totalizer and ammeter."   "ALL switches/knobs in a single row along the panel bottom
   (not by the knees). Keep the shorter stick. Aileron hinge at 30% of aileron chord with the leading edge poking up."

Runs after extra_panel_p3f.py on the approved GLBs (all three liveries), in place, CPU only (NumPy + Pillow; Blender's
bundled python3.13 has both). Idempotent: a GLB carrying extras.extraCockpitShared is skipped. The three liveries share
one rig table: it is edited once (first livery) and the same edit applied to the others, as P3f did.

Builder metres, x forward, y LEFT, z up; panel u = the pilot's RIGHT (= -y), v = z, as vr_phase3b.

  PYTHONDONTWRITEBYTECODE=1 python3.13 tools/own_models/extra_cockpit_shared.py
"""
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
from drop_card_slot import validate

SEATS = {'Rear': dict(sfx='R', ctl='', x=-1.175), 'Front': dict(sfx='F', ctl='_Front', x=-.150)}
BOTTOM, TOP = .055, .360           # panel face: 30 mm lower than P3f so the lower row and the switch row both fit
# Ty's layout. (u, v): u + = the pilot's right. Every bezel at least 5 mm from its neighbours (asserted below).
LAYOUT = {
    'ASI': (-.115, .316), 'G': (-.115, .2255),                     # left column, 3-1/8
    'Altimeter': (.115, .316), 'TC': (.115, .2255),                # right column, 3-1/8
    'Oil': (.215, .271),                                          # the 3-in-1 (oil P / oil T / fuel flow), 3-1/8, right
    'Tach': (-.140, .135), 'MP': (-.045, .135),                   # lower row, 3-1/8
    'FuelComputer': (.040, .135), 'Amps': (.110, .135),           # lower row, 2-1/4
}
TRIG = {'COM': (-.205, .316), 'XPDR': (-.205, .2255)}              # 2-1/4 in round control heads, left
CARD = (0.0, .265)                                                 # sequence card centre (u, v)
RETIRE_GAUGES = ('Temps', 'Fuel_Wing', 'Fuel_Akro')
R318, R214 = 3.125 * .0254 / 2 + .003, 2.25 * .0254 / 2 + .003  # bezel radii incl. 3 mm flange (vr_phase3b)
# ONE row along the bottom edge. Nothing within STICK_GAP of the centreline: from the rear eye that band of the row is
# behind the stick and the hand (z_sight at the row is ~0.26 m at the grip, the gloved hand's top 0.30).
ROW_Z, STICK_GAP = .075, .085
ROW_LEFT = ['Magneto', 'Master', 'FuelPump', 'AlternateAir', 'Mixture', 'Prop']        # outboard -> inboard, +y side
ROW_RIGHT = ['Trim', 'FuelSelector', 'Smoke', 'NavLights', 'Strobe', 'LandingLight']   # inboard -> outboard, -y side
# Knobs ON the radio heads (a TY91/TT21-style head carries its own): offsets (dy left +, dz up) from the head centre.
HEAD_KNOBS = {
    'COM': {'ComOuter': (-.015, -.015), 'ComInner': (-.015, -.015), 'ComVolume': (.015, -.015), 'ComSwap': (-.0235, -.004)},
    'XPDR': {'XpdrIdent': (.0245, .011), 'XpdrMode': (-.0245, .011),
             'XpdrDigit1': (.0135, -.013), 'XpdrDigit2': (.0045, -.013), 'XpdrDigit3': (-.0045, -.013), 'XpdrDigit4': (-.0135, -.013)},
}
# LCD windows on the heads (CockpitDisplays: COM 128x76, XPDR 156x64, fuel computer 128x76 textures).
LCD = {'Radio_Display': ('COM', (.032, .019), .004), 'Xpdr_Display': ('XPDR', (.039, .016), .011),
       'Totalizer_Display': ('FuelComputer', (.040, .024), .004)}
# The three-in-one: three needles from one pivot, each in its own third of the dial.
SCALES3 = {
    'OilP': ('oil_press_psi', [0, 120], [-150, -30], 'psi'),
    'OilT': ('oil_temp_f', [50, 250], [30, 150], 'degF'),
    # Ty 2026-11-04 15:23: the real 300L (EASA.A.362, AEIO-540-L1B5) has no fuel pressure gauge; its UI6331 is MP/FF.
    # POH: fuel flow green arc 0-35 gal/h, red radial at 35, no yellow. Full power is about 23 gph low down.
    'FuelF': ('fuel_flow_gph', [0, 40], [210, 150], 'gph'),
}
ARCS3 = {'OilP': [(0, 25, '#ff4040'), (25, 55, '#ffe044'), (55, 95, '#45d765'), (95, 115, '#ffe044'), (115, 120, '#ff4040')],
         'OilT': [(50, 140, '#ffe044'), (140, 210, '#45d765'), (210, 245, '#ffe044'), (245, 250, '#ff4040')],
         'FuelF': [(0, 35, '#45d765')]}
RADIALS3 = {'FuelF': [(35, '#ff4040')]}                            # red radial lines, across the arc band
TICKS3 = {'OilP': [0, 30, 60, 90, 120], 'OilT': [50, 100, 150, 200, 250], 'FuelF': [0, 10, 20, 30]}   # no 40: it sat on OIL T's 250
HINGE_FRAC = .30                   # of the aileron's own chord, aft of its nose


from instrument_face_style import font, finish


def ang3(k, v): return float(np.interp(v, SCALES3[k][1], SCALES3[k][2]))


def art_three_in_one(path):
    im = Image.new('RGB', (1024, 1024), (10, 10, 10)); d = ImageDraw.Draw(im)
    def point(a, r): a = math.radians(a); return (512 + r * math.sin(a), 512 - r * math.cos(a))
    for k, (param, vals, angs, unit) in SCALES3.items():
        for lo, hi, col in ARCS3[k]:
            d.line([point(a, 445) for a in np.linspace(ang3(k, lo), ang3(k, hi), 60)], fill=col, width=12)
        for v, col in RADIALS3.get(k, []):
            a = ang3(k, v); d.line([point(a, 425), point(a, 468)], fill=col, width=10)
        for v in TICKS3[k]:
            a = ang3(k, v); d.line([point(a, 412), point(a, 462)], fill='#f2f2f2', width=6)
            d.text(point(a, 362), str(v), font=font(44), anchor='mm', fill='#f2f2f2')
    d.text((300, 370), 'OIL P', font=font(46), anchor='mm', fill='#f2f2f2')
    d.text((724, 370), 'OIL T', font=font(46), anchor='mm', fill='#f2f2f2')
    d.text((512, 700), 'FUEL FLOW', font=font(46), anchor='mm', fill='#f2f2f2')
    d.text((300, 425), 'PSI', font=font(34), anchor='mm', fill='#f2f2f2'); d.text((724, 425), '°F', font=font(34), anchor='mm', fill='#f2f2f2')
    d.text((512, 755), 'GPH', font=font(34), anchor='mm', fill='#f2f2f2')
    finish(im).save(path)


def art_head(path, title, sub):
    """A Trig-style round control head face: black, a white legend; the LCD window is a separate quad."""
    im = Image.new('RGB', (512, 512), (14, 15, 17)); d = ImageDraw.Draw(im)
    d.ellipse((8, 8, 504, 504), outline=(70, 72, 76), width=6)
    d.text((256, 70), title, font=font(46), anchor='mm', fill='#f2f2f2')
    d.text((256, 450), sub, font=font(30), anchor='mm', fill='#c8c8c8')
    finish(im).save(path)


def art_fuel_computer(path):
    im = Image.new('RGB', (512, 512), (10, 10, 10)); d = ImageDraw.Draw(im)
    d.text((256, 92), 'FUEL', font=font(52), anchor='mm', fill='#f2f2f2')
    d.text((256, 400), 'FF GPH  /  USED', font=font(34), anchor='mm', fill='#f2f2f2')
    finish(im).save(path)


def mat_index(g, name): return next(i for i, m in enumerate(g.doc['materials']) if m['name'] == name)


def round_face(x, c_y, c_z, rr, n=64):
    verts = [(x, c_y, c_z)] + [(x, c_y - rr * math.sin(t), c_z + rr * math.cos(t)) for t in np.linspace(0, 2 * math.pi, n, endpoint=False)]
    tris = [(0, (i + 1) % n + 1, i + 1) for i in range(n)]
    uv = [(.5 + (-(yy - c_y)) / (2 * rr), .5 - (zz - c_z) / (2 * rr)) for xx, yy, zz in verts]
    return verts, tris, uv


def set_translation(g, name, p_gltf):
    g.doc['nodes'][idx(g, name)]['translation'] = list(map(float, p_gltf))


def move(g, name, d_builder):
    n = g.doc['nodes'][idx(g, name)]
    n['translation'] = (np.array(n.get('translation', [0, 0, 0]), float) + gl(d_builder)).tolist()


def retire(g, doc, name):
    n = g.doc['nodes'][idx(g, name)]
    n.pop('mesh', None); n['extras'] = {'p3g_retired': True}; n['name'] = 'Legacy_P3g_' + name
    doc['rig'] = [r for r in doc['rig'] if r['node'] != name]


HEAD_KNOB_SCALE = .45              # the panel knobs are 24-32 mm across; a 57 mm head's own knobs are 10-14 mm


def place_control(g, doc, c, p, note, scale=None):
    nodes = g.doc['nodes']; n = nodes[idx(g, c['node'])]
    n['translation'] = gl(p).tolist(); c['pivot_gltf'] = gl(p).tolist()
    mount = nodes[idx(g, 'Mount_' + c['grip'][5:])]
    mount['translation'] = gl(p).tolist()
    if scale is not None:
        n['scale'] = [scale] * 3; mount['scale'] = [scale] * 3; c['knob_scale'] = scale
    c['position_status'] = 'E'; c['placement'] = note
    n.setdefault('extras', {})['interaction'] = c
    for e in doc['rig']:
        if e['node'] == c['node']: e['pivot_gltf'] = c['pivot_gltf']


def check_layout():
    items = [(k, u, v, R214 if k in ('FuelComputer', 'Amps') else R318) for k, (u, v) in LAYOUT.items()]
    items += [('Trig_' + k, u, v, R214) for k, (u, v) in TRIG.items()]
    for i, (a, ua, va, ra) in enumerate(items):
        assert va + ra <= TOP - .001 and va - ra >= ROW_Z + .014, (a, 'off the panel band')
        for b, ub, vb, rb in items[i + 1:]:
            gap = math.hypot(ua - ub, va - vb) - ra - rb
            assert gap >= .005, (a, b, round(gap * 1000, 1), 'mm: bezels share space')
        cu, cv = CARD; hw, hh = .057, .073                         # holder outline (card 108 x 140 + lip)
        du, dv = max(abs(ua - cu) - hw, 0), max(abs(va - cv) - hh, 0)
        assert math.hypot(du, dv) >= ra + .005, (a, 'over the sequence card')


# ---- the aileron: hinge at 30 % of its chord, and its nose made flush with the wing at the split ----

def aileron_geometry(g, world, name):
    """Per-vertex data of the aileron in builder frame, plus its nose/TE per station."""
    i = idx(g, name); n = g.doc['nodes'][i]; m = world[i]
    return i, n, m


def fix_aileron(g, doc, world, side_name, wing_skin, report):
    """Move the hinge to 30 % chord (node origin + mesh offset + rig axis/pivot) and blend the nose to the wing's lip."""
    from check_aileron_junction import Skin
    i = idx(g, side_name); node = g.doc['nodes'][i]; M = world[i]
    assert 'rotation' not in node and 'scale' not in node and 'matrix' not in node, side_name
    org = np.array(node['translation'], float)                   # glTF
    prims = g.doc['meshes'][node['mesh']]['primitives']
    V = [np.asarray(g.array(p['attributes']['POSITION']), float) for p in prims]
    W = [v + org for v in V]                                     # world glTF (no rotation)
    B = [np.stack([-w[:, 0], w[:, 2], w[:, 1]], 1) for w in W]   # builder
    allb = np.concatenate(B)
    tris = []
    for p, b in zip(prims, B):
        t = np.asarray(g.array(p['indices'])).reshape(-1, 3) if 'indices' in p else np.arange(len(b)).reshape(-1, 3)
        tris.append(b[t])
    A = Skin(np.concatenate(tris)[:, :, [0, 1, 2]] * np.array([-1, 1, 1]))   # (chord = -x so aft is +, span, up)
    sgn = 1 if allb[:, 1].mean() > 0 else -1
    y0, y1 = (allb[:, 1].min(), allb[:, 1].max())
    ys = np.linspace(y0 + .03, y1 - .03, 25)
    rows = []
    for y in ys:
        cs = np.arange(allb[:, 0].max() + .01, allb[:, 0].min() - .01, -.002)   # builder x, forward to aft
        hit = [c for c in cs if len(A.hits(-c, y))]
        if not hit: continue
        nose, te = hit[0], hit[-1]
        hn = A.hits(-(nose - .008), y)
        gap = [c for c in np.arange(nose, nose + .05, .001) if len(wing_skin.hits(-c, y))]
        if len(hn) < 2 or not gap: continue
        wl = wing_skin.hits(-(gap[0] + .004), y)
        rows.append((y, nose, te, hn[0], hn[-1], wl[0], wl[-1], gap[0] - nose))
    R = np.array(rows)
    # Robust per-station fits (linear in span: the panel is straight-tapered); the spade bracket is an outlier.
    def fit(col):
        k = np.polyfit(R[:, 0], R[:, col], 1); res = R[:, col] - np.polyval(k, R[:, 0])
        ok = np.abs(res) < 3 * max(np.median(np.abs(res)), 1e-4)
        return np.polyfit(R[ok, 0], R[ok, col], 1)
    F = {k: fit(c) for k, c in (('nose', 1), ('te', 2), ('al', 3), ('au', 4), ('wl', 5), ('wu', 6), ('gap', 7))}
    ev = lambda k, y: np.polyval(F[k], y)
    # Hinge line at 30 % of the local chord.
    yi, yo = (y0, y1) if sgn > 0 else (y1, y0)
    hx = lambda y: ev('nose', y) - HINGE_FRAC * (ev('nose', y) - ev('te', y))
    hz = lambda y: .5 * (ev('au', y) + ev('al', y))
    p_in, p_out = np.array((hx(yi), yi, hz(yi))), np.array((hx(yo), yo, hz(yo)))
    # The nose blend: at the nose the aileron's section is mapped onto the wing's lip (same top, same bottom), fading
    # out over the front 60 % of its chord; only vertices inside the aileron's own envelope (the spade stays put).
    newB = []
    for b in B:
        y = b[:, 1]; nose, te = ev('nose', y), ev('te', y); c = np.maximum(nose - te, 1e-3)
        f = np.clip((nose - b[:, 0]) / c, 0, 1)
        au, al, wu, wl = ev('au', y), ev('al', y), ev('wu', y), ev('wl', y)
        ma, ta = .5 * (au + al), np.maximum(au - al, 1e-3); mw, tw = .5 * (wu + wl), wu - wl
        inside = np.abs(b[:, 2] - ma) < .75 * ta + .006
        # Each skin moves by its own step to the wing's lip (top up by wu - au, bottom by wl - al), fully over the
        # front 3 % and gone by 25 % of the chord (where the wing inboard of it is the aileron's old section); the nose's mid-line goes onto the wing's. Additive, so the aft section keeps its shape.
        w = .5 * (1 + np.cos(np.pi * np.clip((f - .03) / .22, 0, 1))) * inside
        upper = b[:, 2] >= ma
        dz = np.where(upper, np.maximum(wu - b[:, 2], 0), -np.maximum(b[:, 2] - wl, 0)) * w   # fill each skin to the lip
        nb = b.copy(); nb[:, 2] = b[:, 2] + dz
        newB.append(nb)
    # The offset-hinge nose: the front of the aileron becomes an arc of radius R = 0.30 c about the hinge line, so the
    # cove gap stays constant as it turns and the nose rises above the upper skin TE-down (drops below TE-up).
    for nb in newB:
        y = nb[:, 1]; nose, te = ev('nose', y), ev('te', y); c = np.maximum(nose - te, 1e-3)
        hxv, hzv = hx(y), hz(y); Rn = nose - hxv
        f = (nose - nb[:, 0]) / c
        front = (f < .12) & (np.abs(nb[:, 2] - hzv) < Rn)
        arc_x = hxv + np.sqrt(np.maximum(Rn ** 2 - (nb[:, 2] - hzv) ** 2, 0))
        nb[front, 0] = arc_x[front]
    new_org = gl(p_in)
    for p, nb in zip(prims, newB):
        w = np.stack([-nb[:, 0], nb[:, 2], nb[:, 1]], 1)
        p['attributes']['POSITION'] = g.accessor(w - new_org, 'VEC3')
        if 'NORMAL' in p and 'indices' in p:
            t = np.asarray(g.array(p['indices'])).reshape(-1, 3); loc = w - new_org
            fn = np.cross(loc[t[:, 1]] - loc[t[:, 0]], loc[t[:, 2]] - loc[t[:, 0]])
            vn = np.zeros_like(loc)
            for k in range(3): np.add.at(vn, t[:, k], fn)
            old = np.asarray(g.array(p['attributes']['NORMAL']), float)
            ln = np.linalg.norm(vn, axis=1, keepdims=True); vn = np.where(ln > 1e-12, vn / np.maximum(ln, 1e-12), old)
            if (np.sum(vn * old, 1) < 0).mean() > .5: vn = -vn        # keep the file's winding convention
            p['attributes']['NORMAL'] = g.accessor(vn, 'VEC3')
    node['translation'] = new_org.tolist()
    # The wing's trailing face over this aileron becomes the matching cove: an arc of R + gap about the same line.
    wi = idx(g, 'Wing'); wn = g.doc['nodes'][wi]
    assert 'translation' not in wn and 'rotation' not in wn and 'matrix' not in wn
    lo_y, hi_y = min(yi, yo), max(yi, yo)
    for p in g.doc['meshes'][wn['mesh']]['primitives']:
        w = np.asarray(g.array(p['attributes']['POSITION']), float); b = np.stack([-w[:, 0], w[:, 2], w[:, 1]], 1)
        y = b[:, 1]; lip = ev('nose', y) + ev('gap', y)
        sel = (y >= lo_y - .002) & (y <= hi_y + .002) & (np.abs(b[:, 0] - lip) < .004) \
              & (b[:, 2] > ev('wl', y) - .003) & (b[:, 2] < ev('wu', y) + .003)
        if not sel.any(): continue
        Rc = ev('nose', y) - hx(y) + ev('gap', y)
        b[sel, 0] = (hx(y) + np.sqrt(np.maximum(Rc ** 2 - (b[:, 2] - hz(y)) ** 2, 0)))[sel]
        w2 = np.stack([-b[:, 0], b[:, 2], b[:, 1]], 1)
        p['attributes']['POSITION'] = g.accessor(w2, 'VEC3')
    axis_node = idx(g, side_name + '_HingeAxis'); g.doc['nodes'][axis_node]['translation'] = gl(p_out).tolist()
    ax = gl(p_out) - gl(p_in); ax /= np.linalg.norm(ax)
    for e in doc['rig']:
        if e['node'] == side_name:
            if np.dot(ax, e['axis_gltf']) < 0: ax = -ax
            e['axis_gltf'] = ax.tolist(); e['pivot_gltf'] = gl(p_in).tolist()
            e['desc'] = e['desc'].split(' | ')[0] + ' | hinge at 30 % of the aileron chord (Ty 2026-11-04): the nose rises above the upper skin TE-down'
    y_mid = .5 * (yi + yo)
    report[side_name] = dict(hinge_in_builder=np.round(p_in, 4).tolist(), hinge_out_builder=np.round(p_out, 4).tolist(),
                             nose_rise_at_30deg_mid_m=round(float(HINGE_FRAC * (ev('nose', y_mid) - ev('te', y_mid)) * math.sin(math.radians(30))), 4),
                             wing_lip_minus_aileron_thickness_root_mm=round(float(((ev('wu', yi) - ev('wl', yi)) - (ev('au', yi) - ev('al', yi))) * 1000), 1))


def update(path, doc):
    g = Glb(path)
    if g.doc['extras'].get('extraCockpitShared'): print('EXTRA', path.name, 'already done'); return False
    world = g.world_matrices(); a = Author(g)
    meta = g.doc['extras']['vrCockpitPhase3b']; controls = g.doc['extras']['vrCockpitPhase3a']['controls']
    black = mat_index(g, 'P3b_Panel'); bezel = mat_index(g, 'P3b_Bezel'); needle_w = mat_index(g, 'P3b_Needle')
    orange = mat_index(g, 'P3b_SecondNeedle'); yellow = mat_index(g, 'P3b_Telltale')
    report = {}
    with tempfile.TemporaryDirectory() as td:
        p3 = Path(td) / 'three.png'; art_three_in_one(p3); m_three = g.material('P3b_Dial_ThreeInOne', png=p3)
        pf = Path(td) / 'fuel.png'; art_fuel_computer(pf); m_fuel = g.material('P3b_Dial_FuelComputer', png=pf)
        heads = {}
        for k, t in (('COM', ('COM', 'VOL    SWAP    MHz')), ('XPDR', ('XPDR', 'IDT   1 2 3 4   MODE'))):
            ph = Path(td) / (k + '.png'); art_head(ph, *t); heads[k] = g.material('P3g_Head_' + k, png=ph)
        lcd_mat = {k: g.material('P3g_' + k, (.015, .025, .03)) for k in LCD}
        for seat, s in SEATS.items():
            sfx, x = s['sfx'], s['x']
            # 1. The panel slab, out to the walls, z BOTTOM..TOP.
            zs = np.round(np.arange(BOTTOM, TOP + 1e-9, .005), 4)
            half = wall_halfwidth(g, x, zs) - .004
            levels = list(zip(zs.tolist(), half.tolist()))
            pn = 'Panel_P3b_' + sfx; q = world[idx(g, pn)][:3, 3]; sh = slab(x, levels)
            g.mesh(pn, [gl(v) - q for v in sh.v], sh.t, black)
            radio_sub = 'Subpanel_P3f_Radio_' + sfx
            g.doc['nodes'][idx(g, radio_sub)].pop('mesh', None)
            g.doc['nodes'][idx(g, radio_sub)]['extras'] = {'p3g_retired': True, 'reason': 'Ty: nothing below the panel'}
            panel = next(p for p in meta['panels'] if p['seat'] == seat)
            keep = []
            for row in panel['gauges']:
                key = row['gauge']
                if key in RETIRE_GAUGES:
                    for nm in [row['face'], 'Bezel_P3b_' + key + '_' + sfx] + row['needles']: retire(g, doc, nm)
                    continue
                u1, v1 = LAYOUT[key]; d = np.array((0., -(u1 - row['u']), v1 - row['v']))
                if key == 'Oil':
                    for nm in row['needles']: doc['rig'] = [r for r in doc['rig'] if r['node'] != nm]
                    row['needles'] = []
                for nm in [row['face'], 'Bezel_P3b_' + key + '_' + sfx] + row['needles']:
                    move(g, nm, d)
                    for e in doc['rig']:
                        if e['node'] == nm: e['pivot_gltf'] = (np.array(e['pivot_gltf']) + gl(d)).tolist()
                row['u'], row['v'] = u1, v1
                keep.append(row)
            # 2. The three-in-one: the old oil face takes the new art, and three needles from one pivot.
            oil = next(r for r in keep if r['gauge'] == 'Oil'); u, v = LAYOUT['Oil']
            fi = idx(g, oil['face']); g.doc['meshes'][g.doc['nodes'][fi]['mesh']]['primitives'][0]['material'] = m_three
            rr = R318 - .0035; oil['gauge_label'] = 'three-in-one: oil P / oil T / fuel flow'
            for j, k in enumerate(('OilP', 'OilT', 'FuelF')):
                name = 'Needle_' + k + '_' + sfx; pivot = np.array((x - .0008, -u, v))
                shn = Shape(); length = rr * .80
                shn.add([pivot + (0, -.0013, -.004), pivot + (0, .0013, -.004), pivot + (0, 0, length)], [(0, 2, 1), (0, 1, 2)])
                shn.v = [np.array(qq) + (-j * .00005, 0, 0) for qq in shn.v]
                param, pts, angs, unit = SCALES3[k]; model = pts[0]; init = angs[0]; th = math.radians(init)
                cc, ss = math.cos(th), math.sin(th)
                shn.v = [pivot + np.array((qq[0] - pivot[0], cc * (qq[1] - pivot[1]) - ss * (qq[2] - pivot[2]), ss * (qq[1] - pivot[1]) + cc * (qq[2] - pivot[2]))) for qq in shn.v]
                if any(nn.get('name') == name for nn in g.doc['nodes']): a.orig.pop(name, None)
                a.node(name, pivot, shape=shn, mat=orange if k == 'OilT' else needle_w)
                a.rigged(name, param, (1, 0, 0), pts, [ang - init for ang in angs], desc='three-in-one (Ty 2026-11-04): its own third of the dial; E scale')
                nn = g.doc['nodes'][a.idx(name)]; nn['extras'].update(rig_unit=unit, rig_param_model=model, rig_dial_ang0_deg=init)
                a.rig[-1].update(unit=unit, model=model); a.rig[-1]['extra']['rig_dial_ang0_deg'] = init
                oil['needles'].append(name)
            # 3. The fuel computer: a 2-1/4 in head with its LCD.
            u, v = LAYOUT['FuelComputer']; rr = R214 - .0035
            shb = Shape(); shb.tube((x + .003, -u, v), (x - .0003, -u, v), R214, 64)
            a.node('Bezel_P3g_FuelComputer_' + sfx, (x - .0005, -u, v), shape=shb, mat=bezel)
            vv, tt, uv = round_face(x - .0005, -u, v, rr); g.mesh('Face_P3g_FuelComputer_' + sfx, [gl(qq) for qq in vv], tt, m_fuel, uv)
            keep.append(dict(gauge='FuelComputer', u=u, v=v, case_inches=2.25, bezel_radius_m=R214, face='Face_P3g_FuelComputer_' + sfx, needles=[]))
            # 4. The Trig-style heads, left.
            for k, (u, v) in TRIG.items():
                shb = Shape(); shb.tube((x + .003, -u, v), (x - .0003, -u, v), R214, 64)
                a.node('Bezel_P3g_Trig_' + k + '_' + sfx, (x - .0005, -u, v), shape=shb, mat=bezel)
                vv, tt, uv = round_face(x - .0005, -u, v, R214 - .0035)
                g.mesh('Face_P3g_Trig_' + k + '_' + sfx, [gl(qq) for qq in vv], tt, heads[k], uv)
                keep.append(dict(gauge='Trig_' + k, u=u, v=v, case_inches=2.25, bezel_radius_m=R214, face='Face_P3g_Trig_' + k + '_' + sfx, needles=[]))
            # 5. LCD windows (rear: the game draws them; front: a dark window).
            for base, (head, (w, h), dz) in LCD.items():
                u, v = TRIG[head] if head in TRIG else LAYOUT[head]
                c = np.array((x - .0011, -u, v + dz))
                corners = [c + (0, w / 2, -h / 2), c + (0, -w / 2, -h / 2), c + (0, -w / 2, h / 2), c + (0, w / 2, h / 2)]
                name = base + '_' + sfx
                existing = any(nn.get('name') == name for nn in g.doc['nodes'])
                if existing:
                    ni = idx(g, name); par = next((k2 for k2, nn in enumerate(g.doc['nodes']) if ni in nn.get('children', [])), None)
                    if par is not None:                                  # its old subpanel parent: retire its mesh, keep the node
                        pnode = g.doc['nodes'][par]; pnode.pop('mesh', None); pnode['translation'] = [0.0, 0.0, 0.0]
                    g.doc['nodes'][ni]['translation'] = [0.0, 0.0, 0.0]
                g.mesh(name, [gl(qq) for qq in corners], [(0, 1, 2), (0, 2, 3)], lcd_mat[base], [(0, 1), (1, 1), (1, 0), (0, 0)])
                if sfx == 'R':
                    disp = next((dd for dd in doc.setdefault('displays', []) if dd.get('node') == name), None)
                    if disp is None:
                        disp = dict(node=name); doc['displays'].append(disp)
                    disp.update(parent=None, centre_gltf=gl(c).tolist(), size_m=[w, h], note='extra_cockpit_shared: on its 2-1/4 in head')
            panel['gauges'] = keep
            panel['outline_zw'] = [[round(z, 4), round(w, 4)] for z, w in levels]
            # 6. Controls: ONE row along the bottom; the radio knobs on their heads.
            by = {c['grip'][5:].replace('_Front', ''): c for c in controls if c['seat'] == seat}
            wmax = float(min(h for z, h in levels if abs(z - ROW_Z) <= .013)) - .022
            pitch = (wmax - STICK_GAP) / (len(ROW_LEFT) - 1)
            for k2, lab in enumerate(ROW_LEFT):
                place_control(g, doc, by[lab], (x - .001, wmax - k2 * pitch, ROW_Z), 'extra_cockpit_shared: the one switch row along the panel bottom')
            for k2, lab in enumerate(ROW_RIGHT):
                place_control(g, doc, by[lab], (x - .001, -(STICK_GAP + k2 * pitch), ROW_Z), 'extra_cockpit_shared: the one switch row along the panel bottom')
            for head, knobs in HEAD_KNOBS.items():
                u, v = TRIG[head]
                for lab, (dy, dz) in knobs.items():
                    place_control(g, doc, by[lab], (x - .001, -u + dy, v + dz), f'extra_cockpit_shared: on the {head} head', HEAD_KNOB_SCALE)
            report[seat] = dict(panel=[BOTTOM, TOP], row_z=ROW_Z, row_y=[round(wmax, 3), STICK_GAP], pitch=round(pitch, 4))
        # 7. The sequence card, centred.
        cy0 = None
        for nn in g.doc['nodes']:
            if nn['name'] == 'SequenceCard_Face' and 'mesh' in nn:
                wm = g.world_matrices()[idx(g, 'SequenceCard_Face')]
                pts = np.concatenate([np.asarray(g.array(p['attributes']['POSITION']), float) @ wm[:3, :3].T + wm[:3, 3]
                                      for p in g.doc['meshes'][nn['mesh']]['primitives']])
                bpts = np.stack([-pts[:, 0], pts[:, 2], pts[:, 1]], 1); cy0 = (bpts.min(0) + bpts.max(0)) / 2
        d_card = np.array((0., -CARD[0] - cy0[1], CARD[1] - cy0[2]))
        for nn in g.doc['nodes']:
            if nn['name'].startswith('SequenceCard_'):
                if 'matrix' in nn: nn['matrix'][12:15] = (np.array(nn['matrix'][12:15]) + gl(d_card)).tolist()
                else: nn['translation'] = (np.array(nn.get('translation', [0, 0, 0])) + gl(d_card)).tolist()
        for card in doc.get('card_slots', []):
            card['centre_gltf'] = (np.array(card['centre_gltf']) + gl(d_card)).tolist(); card['note'] = 'extra_cockpit_shared: centred on the rear panel (Ty)'
        report['card_move_builder'] = np.round(d_card, 4).tolist()
    # 8. The ailerons.
    world = g.world_matrices()
    from check_aileron_junction import Skin
    wi = idx(g, 'Wing'); wn = g.doc['nodes'][wi]; wm = world[wi]
    wt = []
    for p in g.doc['meshes'][wn['mesh']]['primitives']:
        pos = np.asarray(g.array(p['attributes']['POSITION']), float) @ wm[:3, :3].T + wm[:3, 3]
        t = np.asarray(g.array(p['indices'])).reshape(-1, 3)
        wt.append(np.stack([pos[:, 0], pos[:, 2], pos[:, 1]], 1)[t])   # (chord = glTF x = -builder x, span = y left, up)
    wing_skin = Skin(np.concatenate(wt))
    for side in ('Aileron_L', 'Aileron_R'): fix_aileron(g, doc, world, side, wing_skin, report)
    doc['rig'] += a.rig
    doc['controls'] = controls; doc['phase3b'] = meta
    g.doc['extras']['extraCockpitShared'] = dict(
        owner='Ty 2026-11-04 05:35 / 07:33: card centred, ASI over G left, ALT over TC right, Trig radio + xpdr left, 3-in-1 right, '
              'lower row tach / MP / fuel computer / ammeter, one switch row on the panel bottom, short stick kept, aileron hinge 30 %',
        layout=LAYOUT, trig=TRIG, card=CARD, panel=[BOTTOM, TOP], row_z=ROW_Z, stick_gap=STICK_GAP, hinge_frac=HINGE_FRAC, report=report)
    compact(g); validate(g, path); g.save(path)
    print('EXTRA', path.name, json.dumps(report))
    return True


if __name__ == '__main__':
    check_layout()
    rigpath = ROOT / 'docs/models/ea300-own-rig.json'
    base = rigpath.read_text(); doc = json.loads(base); wrote = False
    for k, p in enumerate(sorted((MODELS / 'extra-300-like').glob('*own*.glb'))):
        wrote |= update(p, doc if k == 0 else json.loads(base))
    if wrote: rigpath.write_text(json.dumps(doc, indent=1) + '\n')
