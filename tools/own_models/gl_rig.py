# v13 rig (exec'd by build_great_lakes.py AFTER the 180 deg yaw, gear / stab / aileron empties and slave pins, before
# the export). Same conventions as the SGS 2-33 v10 rig (docs/models/sgs233-own-RIG.md): each moving node has its
# origin on its pivot, identity rotation, and extras rig_param / rig_axis_gltf (or rig_move_axis_gltf) / rig_map_param
# / rig_map_deg (or rig_map_m) / rig_param_model / rig_unit / rig_desc / rig_link. Rotations are RELATIVE TO THE
# MODELLED POSE (the map value is 0 at rig_param_model). Positive angle = right-hand rule about rig_axis_gltf.
from gl_dialmaps import DIALS, SLIP_DEG
RZ3 = RZ.to_3x3(); RIG2 = []
def w(p): return RZ @ Vector(tuple(float(c) for c in p))
def wd(v): return RZ3 @ Vector(tuple(float(c) for c in v))
def wloc(o):
    v = Vector(o.location)
    while o.parent is not None: o = o.parent; v = v + o.location
    return v
def gl_axis(v): v = Vector(v).normalized(); return [round(v.x, 5), round(v.z, 5), round(-v.y, 5)]
def gl_pt(v): return [round(v.x, 4), round(v.z, 4), round(-v.y, 4)]
def origin2(n, p, parent=None):
    o = P[n]; org = w(p); o.data.transform(Matrix.Translation(-org)); o.parent = parent
    o.location = org - (wloc(parent) if parent is not None else Vector()); return o
def group(name, p, parent=None):
    e = _empty(name, w(p)); e.parent = parent
    if parent is not None: e.location = w(p) - wloc(parent)
    return e
def reparent(o, parent):   # keep world placement (no rotations anywhere in this rig)
    pw = wloc(o); o.parent = parent; o.location = pw - wloc(parent)
def rig2(o, param, ax, ps, vals, unit, label, tv=None, want=None, kind='rot', link='', note='', extra=None):
    ax = Vector(tuple(ax)).normalized()
    if want is not None:
        ok = False
        for sg in (1, -1):
            d = ((Matrix.Rotation(math.radians(5), 3, sg * ax) @ Vector(tv)) - Vector(tv)) if kind == 'rot' else sg * ax
            if want(d): ax = sg * ax; ok = True; break
        assert ok, 'rig sign: ' + o.name
    o['rig_param'] = param; o['rig_axis_gltf' if kind == 'rot' else 'rig_move_axis_gltf'] = gl_axis(ax)
    o['rig_map_param'] = [float(q) for q in ps]; o['rig_map_deg' if kind == 'rot' else 'rig_map_m'] = [float(q) for q in vals]
    o['rig_param_model'] = float(ps[[abs(q) < 1e-9 for q in vals].index(True)]); o['rig_unit'] = unit; o['rig_desc'] = label
    if link: o['rig_link'] = link
    if note: o['note'] = note
    for k_, v_ in (extra or {}).items(): o[k_] = v_
    RIG2.append(dict(node=o.name, parent=o.parent.name if o.parent else None, param=param, unit=unit, kind=kind, axis_gltf=gl_axis(ax),
                     pivot_gltf=gl_pt(wloc(o)), map=[[float(a_), float(b_)] for a_, b_ in zip(ps, vals)], model=o['rig_param_model'],
                     desc=label, link=link, note=note, extra=extra or {}))
    print('RIG %-22s %-14s %s axis %s pivot %s map %s' % (o.name, param, kind, gl_axis(ax), gl_pt(wloc(o)), ' '.join('%g:%g' % (a_, b_) for a_, b_ in zip(ps, vals))))
    return ax
FWD, AFT, UP, STBD = Vector((-1, 0, 0)), Vector((1, 0, 0)), Vector((0, 0, 1)), Vector((0, 1, 0))
CW = dict(tv=UP, want=lambda d: d.y > 0)            # clockwise as seen from the seat (about FWD)

# ---- cockpit hierarchy ----------------------------------------------------------------------------------------------
CKG = group('Cockpit', (-1.30, 0, -0.30))
INF = group('Instruments_Front', (XP_F, 0, 0.0), CKG); INR = group('Instruments_Rear', (XP_R, 0, 0.0), CKG)
CTL = group('Controls', (-1.30, 0, ZFL), CKG); INT = group('Interior', (-1.30, 0, -0.30), CKG)
for n in (CK_INT, 'Cockpit_Liner'): origin2(n, (-1.30, 0, -0.30), INT)
for n, e in (('Eye_Front', EYE_F), ('Eye_Rear', EYE_R)):
    g = group(n, e, CKG); g['note'] = 'design eye point (E): seated eye over the cushion, for the cockpit camera'
for n, d in NEEDLES.items():
    o = origin2(n, d['c'], INF if d['grp'] == 'F' else INR); k = d['key']
    other = 'front + rear panels both show it (same param)' if n[:-2] + ('_R' if n.endswith('_F') else '_F') in NEEDLES else 'rear (PIC) panel only'
    if k in DIALS:
        prm, ps, ds, a0, un, lab, ex = DIALS[k]
        rig2(o, prm, FWD, ps, ds, un, lab + '; modelled at %g deg clockwise from 12; +deg clockwise from the seat' % a0, link=other, extra=dict(ex, rig_dial_ang0_deg=a0), **CW)
    elif k == 'BALL':
        rig2(o, 'slip_ball', FWD, [-1, 0, 1], [-SLIP_DEG, 0, SLIP_DEG], '-1..+1', 'turn coordinator ball, swings on the tube arc about this pivot (above the dial centre): +1 = ball to the pilot\'s right (E %g deg)' % SLIP_DEG,
             tv=Vector((0, 0, -1)), want=lambda d: d.y > 0, link=other)
    elif k == 'COMPASS':
        rig2(o, 'heading_deg', UP, [0, 360], [0, 360], 'deg magnetic', 'wet-compass card drum: turns opposite the airplane so the heading sits under the lubber line (modelled at 000; reverse-sensing card)',
             tv=AFT, want=lambda d: d.y > 0, link=other, extra={'rig_map_wrap': 360.0})
tt = origin2('TorqueTube', RIGPTS['TorqueTube'], CTL)
rig2(tt, 'aileron', FWD, [-1, 0, 1], [-18, 0, 18], '-1..+1', 'aileron torque tube under the floor carrying both sticks: +1 = stick tops to the right (roll right), 18 deg (E)',
     link='Aileron_LL/LR (drivers) -> UL/UR (slave struts); Stick_F and Stick_R are children', **CW)
for n in ('Stick_F', 'Stick_R'):
    o = origin2(n, RIGPTS[n], tt)
    rig2(o, 'elevator', STBD, [-1, 0, 1], [-12, 0, 18], '-1..+1', 'stick pitch about its yoke on the torque tube: +1 aft 18, -1 forward 12 (E; stops on the aft stick per manual)',
         tv=w(RIGPTS[n + '_tip']) - w(RIGPTS[n]), want=lambda d: d.x > 0, link='Elevator_L/R_Pivot; front + rear sticks linked (same param)')
for n in ('Pedal_F_L', 'Pedal_F_R', 'Pedal_R_L', 'Pedal_R_R'):
    o = origin2(n, RIGPTS[n], CTL); right = n.endswith('_R')
    rig2(o, 'rudder', STBD, [-1, 0, 1], [-15, 0, 15], '-1..+1', '%s rudder pedal frame, floor hinge: +1 (right rudder) top %s 15 deg (E)' % ('right' if right else 'left', 'forward' if right else 'aft'),
         tv=UP, want=(lambda d: d.x < 0) if right else (lambda d: d.x > 0), link='Rudder_Pivot; all four pedals linked (rear pedals beside the front seat)')
for n in ('Brake_F_L', 'Brake_F_R', 'Brake_R_L', 'Brake_R_R'):
    o = origin2(n, RIGPTS[n], CTL); side = 'left' if n.endswith('_L') else 'right'
    rig2(o, 'brake_' + side, STBD, [0, 1], [0, 20], '0..1', '%s heel brake pedal (one per foot, each cockpit, manual): 1 = pushed, top forward 20 deg (E)' % side,
         tv=UP, want=lambda d: d.x < 0, link='wheel brake %s; front + rear linked' % side)
for n0, prm, lab in (('Throttle', 'throttle', 'throttle (black knob, inboard)'), ('Prop', 'prop', 'propeller rpm (black knob, middle; Hartzell constant-speed, F6-31 governor)'),
                     ('Mixture', 'mixture', 'mixture (red knob, outboard)')):
    for sfx in ('F', 'R'):
        n = '%s_%s' % (n0, sfx); o = origin2(n, RIGPTS[n], CTL)
        rig2(o, prm, STBD, [0, 1], [0, 50], '0..1', '%s on the left-wall quadrant: modelled at 0 (full aft), 1 = 50 deg forward (E)' % lab,
             tv=w(RIGPTS[n + '_tip']) - w(RIGPTS[n]), want=lambda d: d.x < 0, link='front + rear quadrants linked (same param)')
for sfx in ('F', 'R'):
    o = origin2('Trim_' + sfx, RIGPTS['Trim_' + sfx], CTL)
    rig2(o, 'trim', AFT, [-1, 0, 1], [-0.06, 0, 0.06], '-1 nose down .. +1 nose up', 'stabilizer trim knob in a slot low on the left wall: aft = nose up, 60 mm each way (E)',
         kind='move', want=lambda d: d.x > 0, link='Stab_TrimPivot (jack-screw stabilizer); front + rear linked')
    o = origin2('FuelValve_' + sfx, RIGPTS['FuelValve_' + sfx], CTL)
    rig2(o, 'fuel_valve', FWD, [0, 1], [90, 0], '0 OFF / 1 ON', 'fuel shut-off (a control in each cockpit, manual): modelled ON (handle vertical), OFF = 90 deg clockwise (E)',
         link='front + rear linked', extra={'rig_detents': [0, 1]}, **CW)
# ---- outside surfaces -----------------------------------------------------------------------------------------------
_ev = {s: np.array([v.co[:] for v in P['Elevator_' + s].data.vertices]) for s in ('L', 'R')}
for s in ('L', 'R'):
    o = P['Elevator_' + s]; sg = 1 if _ev[s][:, 1].mean() < 0 else -1          # post-yaw: port is -y
    pre_y = sg * 0.5 * (E_IN + B_S); h0 = (H_T, pre_y, Z_S); h1 = (H_T, sg * B_S, Z_S)
    piv = _empty('Elevator_%s_Pivot' % s, w(h0)); piv.parent = tp; piv.location = w(h0) - wloc(tp)
    _empty('Elevator_%s_HingeAxis' % s, w(h1))
    off = w(h0) - wloc(tp); o.data.transform(Matrix.Translation(-off)); o.parent = piv; o.location = Vector()
    rig2(piv, 'elevator', w(h1) - w(h0), [-1, 0, 1], [-29, 0, 21], '-1..+1', 'elevator: +1 (stick aft) TE up 21, -1 TE down 29 (TCDS A18EA)', tv=AFT, want=lambda d: d.z > 0,
         link='Stick_F/Stick_R; rides on Stab_TrimPivot')
    piv['hinge_axis_marker'] = 'Elevator_%s_HingeAxis' % s
z0, z1 = -0.433, Z_FT; rp = _empty('Rudder_Pivot', w((H_T, 0, 0.5 * (z0 + z1)))); _empty('Rudder_HingeAxis', w((H_T, 0, z1)))
o = P['Rudder']; o.data.transform(Matrix.Translation(-rp.location)); o.parent = rp; o.location = Vector()
rig2(rp, 'rudder', UP, [-1, 0, 1], [-35, 0, 35], '-1..+1', 'rudder on the vertical hinge line at the sternpost: +1 (right pedal) TE to starboard 35 (TCDS A18EA)', tv=AFT, want=lambda d: d.y > 0, link='Pedal_*')
rp['hinge_axis_marker'] = 'Rudder_HingeAxis'
rig2(tp, 'trim', Vector((0, 1, 0)), [-1, 1, 1.0714], [7.25, 0.25, 0.0], '-1 nose down .. +1 nose up',
     'jack-screw stabilizer about the rear beam: LE up 7.25 deg at -1 (nose down), 0.25 at +1 (nose up) (TCDS A18EA travel); modelled at 0 deg = just past +1. Sense: nose-up trim = LE down, to confirm with Ty',
     tv=FWD, want=lambda d: d.z > 0, link='Trim_F/Trim_R')
for key, A in AIL.items():
    piv = bpy.data.objects['Aileron_%s_Pivot' % key]; ax = wd(A['u']); tv_ = wd(A['te'] - A['o']); r_ = RIG[key[1]]
    if key[0] == 'L':
        ps, vs = [-1, 0, 1], ([r_['l_up'], 0, -r_['l_dn']] if key[1] == 'L' else [-r_['l_dn'], 0, r_['l_up']])
        lab = 'lower aileron (driver): +1 roll right = %s' % ('TE down %.2f' % r_['l_dn'] if key[1] == 'L' else 'TE up %.2f' % r_['l_up'])
    else:
        ft = SLAVE[key[1]]['tab']; lt = SLAVE[key[1]]['ltab']
        if key[1] == 'L': ps, vs = [-1, -12 / r_['l_up'], 0, 11 / r_['l_dn'], 1], [ft[4], ft[3], 0.0, ft[1], ft[0]]
        else: ps, vs = [-1, -11 / r_['l_dn'], 0, 12 / r_['l_up'], 1], [ft[0], ft[1], 0.0, ft[3], ft[4]]
        lab = 'upper aileron (follower on Strut_Slave_%s): solved constant-strut-length table' % key[1]
    rig2(piv, 'aileron', ax, ps, [round(float(v), 3) for v in vs], '-1..+1', lab + ' (+deg = TE up)', tv=tv_, want=lambda d: d.z > 0, link='TorqueTube / sticks')
print('RIG nodes: %d' % len(RIG2))
if os.environ.get('GL_RIGJSON'):
    json.dump(dict(model='great-lakes-like-own', livery=LIV, rig=RIG2), open(os.environ['GL_RIGJSON'], 'w'), indent=1)
