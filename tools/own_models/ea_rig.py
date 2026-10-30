# v15 Extra 300 cockpit + canopy rig (exec'd by build_ea300.py AFTER the 180 deg yaw and the HINGES loop, before the
# export). Same conventions as the Great Lakes v13 / SGS 2-33 v10 rigs: each moving node has its origin on its pivot,
# identity rotation, and extras rig_param / rig_axis_gltf (or rig_move_axis_gltf) / rig_map_param / rig_map_deg (or
# rig_map_m) / rig_param_model / rig_unit / rig_desc / rig_link. Rotations are RELATIVE TO THE MODELLED POSE (the map value
# is 0 at rig_param_model). Positive angle = right-hand rule about rig_axis_gltf. Post-yaw frame: forward -x, starboard +y.
from ea_dialmaps import DIALS, SLIP_DEG
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
CKG = group('Cockpit', (-1.20, 0, -0.10))
INF = group('Instruments_Front', (XP_F, 0, 0.20), CKG); INR = group('Instruments_Rear', (XP_R, 0, 0.15), CKG)
CTL = group('Controls', (-1.20, 0, ZF), CKG); INT = group('Interior', (-1.20, 0, -0.10), CKG)
for n in ('Cockpit_Shell', CK_INT, 'Footwell_Liner'): origin2(n, (-1.20, 0, -0.10), INT)
for n, e in (('Eye_Front', EYE_F), ('Eye_Rear', EYE_R)):
    g = group(n, e, CKG); g['note'] = 'design eye point (E): seated eye over the cushion, for the cockpit camera'
    if n == 'Eye_Rear': g['default_view'] = True; g['note'] += '; DEFAULT view (rear seat = PIC, solo from the rear)'
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
DISP = []                                          # v13.1: avionics display surfaces (text drawn by the sim, not a needle rig)
for n, d in DISPLAYS.items():
    o = origin2(n, d['c'], INR); prm = ['com_freq_mhz', 'com_standby_mhz'] if n.startswith('Radio') else ['xpdr_squawk', 'xpdr_mode']
    o['rig_display_params'] = prm; o['rig_display_size_m'] = [round(d['size'][0], 4), round(d['size'][1], 4)]
    o['note'] = 'dark LCD face: UV 0..1 over the window (u to the pilot\'s right, v up); the sim draws the text on it (render texture / TMP)'
    DISP.append(dict(node=n, parent=INR.name, params=prm, centre_gltf=gl_pt(wloc(o)), facing_gltf=gl_axis(AFT), size_m=[round(d['size'][0], 4), round(d['size'][1], 4)],
                     note=o['note'] + ('; active + standby frequency, 25 / 8.33 kHz' if n.startswith('Radio') else '; 4-digit octal squawk + mode (OFF / SBY / ON / ALT), IDENT'))) 
    print('DISPLAY %s params %s centre %s size %s' % (n, prm, gl_pt(wloc(o)), o['rig_display_size_m']))
for n in ('Switch_Master_R',):
    o = origin2(n, RIGPTS[n], INR)
    rig2(o, 'master', STBD, [0, 1], [60, 0], '0 OFF / 1 ON', 'battery master toggle: modelled ON (up), OFF = 60 deg down (E)',
         tv=Vector((0.6, 0, 0.8)), want=lambda d: d.z < 0, extra={'rig_detents': [0, 1]})
for n0, prm, lab in (('Prop', 'prop', 'propeller rpm vernier (black knob, rear panel; constant-speed)'), ('Mixture', 'mixture', 'mixture vernier (red knob, rear panel)')):
    o = origin2(n0 + '_R', RIGPTS[n0 + '_R'], INR)
    rig2(o, prm, FWD, [0, 1], [-0.05, 0.0], '0..1', lab + ': modelled IN (1 = full forward / rich), 0 = pulled 50 mm aft (E)', kind='move', want=lambda d: d.x < 0,
         link='rear seat only (POH)')
# ---- controls ---------------------------------------------------------------------------------------------------------
for sfx in ('F', 'R'):
    rr = origin2('StickRoll_' + sfx, RIGPTS['StickRoll_' + sfx], CTL)
    rig2(rr, 'aileron', FWD, [-1, 0, 1], [-18, 0, 18], '-1..+1', 'stick roll gimbal (fore-aft pivot under the floor): +1 = stick top to the right (roll right), 18 deg (E)',
         link='Aileron_L/R; front + rear sticks linked (same param); Stick_%s is the child' % sfx, **CW)
    o = origin2('Stick_' + sfx, RIGPTS['Stick_' + sfx], rr)
    rig2(o, 'elevator', STBD, [-1, 0, 1], [-12, 0, 18], '-1..+1', 'stick pitch on the roll gimbal: +1 aft 18, -1 forward 12 (E)',
         tv=w(RIGPTS['Stick_' + sfx + '_tip']) - w(RIGPTS['Stick_' + sfx]), want=lambda d: d.x > 0, link='Elevator_L/R; front + rear linked')
for n in ('Pedal_F_L', 'Pedal_F_R', 'Pedal_R_L', 'Pedal_R_R'):
    o = origin2(n, RIGPTS[n], CTL); right = n.endswith('_R')
    rig2(o, 'rudder', STBD, [-1, 0, 1], [-15, 0, 15], '-1..+1', '%s rudder pedal, floor hinge: +1 (right rudder) top %s 15 deg (E)' % ('right' if right else 'left', 'forward' if right else 'aft'),
         tv=UP, want=(lambda d: d.x < 0) if right else (lambda d: d.x > 0), link='Rudder; all four pedals linked')
    b = 'Brake_' + n[6:]; ob = origin2(b, RIGPTS[b], o); side = 'left' if n.endswith('_L') else 'right'
    rig2(ob, 'brake_' + side, STBD, [0, 1], [0, 15], '0..1', '%s toe brake (upper pedal plate, child of the pedal): 1 = pressed, top forward 15 deg (E)' % side,
         tv=wd(RIGPTS[b + '_up']), want=lambda d: d.x < 0, link='wheel brake %s; front + rear linked' % side)
for sfx in ('F', 'R'):
    n = 'Throttle_' + sfx; o = origin2(n, RIGPTS[n], CTL)
    rig2(o, 'throttle', STBD, [0, 1], [0, 50], '0..1', 'throttle lever on the left ledge quadrant: modelled at 0 (idle, aft), 1 = 50 deg forward (E)',
         tv=w(RIGPTS[n + '_tip']) - w(RIGPTS[n]), want=lambda d: d.x < 0, link='front + rear linked (same param)')
o = origin2('Trim_R', RIGPTS['Trim_R'], CTL)
rig2(o, 'trim', STBD, [-1, 0, 1], [-25, 0, 25], '-1 nose down .. +1 nose up', 'elevator trim lever on the right ledge (rear seat): +1 nose up = knob aft 25 deg (E)',
     tv=UP, want=lambda d: d.x > 0, link='no trim tab is modelled on Elevator_L/R (sim-only effect)')
o = origin2('FuelSel_R', RIGPTS['FuelSel_R'], CTL)
rig2(o, 'fuel_selector', FWD, [0, 1, 2], [-90, 0, 90], '0 OFF / 1 ACRO+CENTRE / 2 WING', 'fuel selector on the centre stack (rear): modelled at 1 (handle up); 2 = 90 deg clockwise, 0 = 90 deg anticlockwise (E detents)',
     link='front + rear linked', extra={'rig_detents': [0, 1, 2]}, **CW)
o = origin2('FuelSel_F', RIGPTS['FuelSel_F'], CTL)
rig2(o, 'fuel_selector', UP, [0, 1, 2], [-90, 0, 90], '0 OFF / 1 ACRO+CENTRE / 2 WING', 'fuel selector on the right ledge (front): modelled at 1 (handle forward); 2 = handle to the right, 0 = to the left (E)',
     tv=FWD, want=lambda d: d.y > 0, link='front + rear linked', extra={'rig_detents': [0, 1, 2]})
# ---- canopy: one moving transparency on the right-sill hinge; opens to the right, jettisons -----------------------
CAN = _empty('Canopy', w(CAN_H0)); _empty('Canopy_HingeAxis', w(CAN_H1))
for n in ('Canopy_Glass', 'Canopy_Frame'):
    o = P[n]; o.data.transform(Matrix.Translation(-w(CAN_H0))); o.parent = CAN; o.location = Vector()
rig2(CAN, 'canopy_open', w(CAN_H1) - w(CAN_H0), [0, 1], [0, 100], '0 closed .. 1 open', 'canopy on its right-sill hinge line: 1 = open, left edge up and over to the right, 100 deg (E stop)',
     tv=w(CAN_L) - w(CAN_H0), want=lambda d: d.z > 0, link='CanopyLock_F/R must read 0 (unlocked) to open',
     extra={'hinge_axis_marker': 'Canopy_HingeAxis', 'rig_jettison_param': 'canopy_jettison', 'rig_jettison_desc': 'canopy_jettison 1 = the sim detaches this node (with its children) and gives it the airflow; bailout uses it'})
for sfx in ('F', 'R'):
    o = origin2('CanopyLock_' + sfx, RIGPTS['CanopyLock_' + sfx], CAN)
    rig2(o, 'canopy_lock', STBD, [0, 1], [35, 0], '0 unlocked / 1 locked', 'red canopy lock lever on the left canopy frame (moves with the canopy): modelled locked (lever aft, flat); unlocked = 35 deg up (E)',
         tv=AFT, want=lambda d: d.z > 0, link='front + rear linked', extra={'rig_detents': [0, 1]})
    o = origin2('CanopyRelease_' + sfx, RIGPTS['CanopyRelease_' + sfx], CKG)
    rig2(o, 'canopy_jettison', Vector((0, -1, 0)), [0, 1], [0, 0.04], '0..1', 'red emergency canopy release on the right sill: 1 = pulled 40 mm inboard, the sim then jettisons Canopy (E)',
         kind='move', want=lambda d: d.y < 0, link='Canopy (rig_jettison_param)')
# ---- outside surfaces (v13 HINGES nodes: origin on the hinge, <n>_HingeAxis marker at the other end) ---------------
for n, prm, ps, vs, want_, lab in (('Aileron_L', 'aileron', [-1, 0, 1], [30, 0, -30], lambda d: d.z > 0, 'left aileron: +1 roll right = TE down 30 (EA 300/L POH travel)'),
                                   ('Aileron_R', 'aileron', [-1, 0, 1], [-30, 0, 30], lambda d: d.z > 0, 'right aileron: +1 roll right = TE up 30 (EA 300/L POH travel)'),
                                   ('Elevator_L', 'elevator', [-1, 0, 1], [-25, 0, 25], lambda d: d.z > 0, 'left elevator: +1 (stick aft) TE up 25 (EA 300/L POH travel)'),
                                   ('Elevator_R', 'elevator', [-1, 0, 1], [-25, 0, 25], lambda d: d.z > 0, 'right elevator: +1 (stick aft) TE up 25 (EA 300/L POH travel)'),
                                   ('Rudder', 'rudder', [-1, 0, 1], [-30, 0, 30], lambda d: d.y > 0, 'rudder: +1 (right pedal) TE to starboard 30 (EA 300/L POH travel)')):
    o = P[n]; mk = bpy.data.objects[n + '_HingeAxis']
    rig2(o, prm, Vector(mk.location) - Vector(o.location), ps, vs, '-1..+1', lab + ' (+deg about rig_axis = TE up / to starboard)', tv=AFT, want=want_,
         link={'aileron': 'StickRoll_F/R', 'elevator': 'Stick_F/R', 'rudder': 'Pedal_*'}[prm])
print('RIG nodes: %d' % len(RIG2))
if os.environ.get('EA_RIGJSON'):
    json.dump(dict(model='extra-300-like-own', livery=LIVERY, rig=RIG2, displays=DISP), open(os.environ['EA_RIGJSON'], 'w'), indent=1)
