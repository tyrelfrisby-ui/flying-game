"""PA-28-181 Archer and Cirrus SR22 (owner 2026-10-02), derived from the tuned c172-like config by geometric transform
so they inherit its proven balance, stall dynamics, gear model and drag calibration; data:
docs/research/PA28-SR22-DATA.md (POH / TCDS; [E] estimates noted there). Run: python3 tools/aircraft_builder/build_pa28_sr22.py
"""
import copy, json, math, os

REPO = os.path.expanduser('~/Documents/flying-game')
CFG = os.path.join(REPO, 'configs/aircraft')
C172 = json.load(open(os.path.join(CFG, 'c172-like.json')))
C172_SPAN, C172_WING_AREA = 10.92, 16.2


def surf(c, sid):
    return next(s for s in c['surfaces'] if s['id'] == sid)


def wing_area(c):
    return sum(st['area'] for sid in ('wing', 'wing-aileron') for st in surf(c, sid)['strips'])


def make(idname, name, span, area, taper_tip, dihedral_deg, wing_z, mass, cg_x, inertia, tail_scale, fin_scale,
         stabilator, prop, cd0, limits, spawn, ctl, flap_delta_rad, gear_main_x, gear_nose_x, gear_z, crossflow_len):
    c = copy.deepcopy(C172)
    c['id'], c['displayName'], c['spawnIasMs'] = idname, name, spawn
    ky = span / C172_SPAN
    # ---- wing: span scaled, LOW wing (strips below the body axis), dihedral, chord from area with a taper toward the tip
    for sid in ('wing', 'wing-aileron'):
        s = surf(c, sid)
        s['heightAboveBodyAxisM'] = -wing_z
        for st in s['strips']:
            y = st['pos'][1] * ky
            frac = abs(y) / (span / 2)
            st['pos'] = [st['pos'][0], round(y, 3), wing_z]
            st['dihedralRad'] = round(math.radians(dihedral_deg), 5)
            st['_taper'] = 1.0 - (1.0 - taper_tip) * max(0.0, (frac - 0.45) / 0.55)   # constant-chord inner, tapered outer
    # chord/area: scale so the total matches, with the taper shape; ailerons keep their x offset behind the wing TE
    raw = sum(st['area'] * ky * st['_taper'] for sid in ('wing', 'wing-aileron') for st in surf(c, sid)['strips'])
    k = area / raw
    kc = k * ky   # chord scale on a strip (its area scaled by span ky too)
    for sid in ('wing', 'wing-aileron'):
        for st in surf(c, sid)['strips']:
            t = st.pop('_taper')
            st['area'] = round(st['area'] * ky * t * k, 4)
            st['chord'] = round(st['chord'] * t * k, 3)
            st['pos'][0] = round(st['pos'][0] * t * k, 3)    # x offsets (aileron rows behind the TE) follow the chord
            if 'flap' in st:
                st['flap'] = dict(st['flap'], maxDeltaAlphaRad=flap_delta_rad)
    # ---- tail: same arm as the C172 (both ~15 ft), areas scaled
    for sid, sc in (('hStab', tail_scale), ('elevator', tail_scale), ('vStab', fin_scale), ('rudder-vstab', fin_scale)):
        for st in surf(c, sid)['strips']:
            st['area'] = round(st['area'] * sc, 4)
            st['chord'] = round(st['chord'] * math.sqrt(sc), 3)
            if sid in ('hStab', 'elevator'):
                st['pos'][1] = round(st['pos'][1] * math.sqrt(sc), 3)
    if stabilator:
        # All-moving stabilator: the whole horizontal tail rotates (every row a hinged row, gain 1).
        for st in surf(c, 'hStab')['strips']:
            st['control'] = {'surface': 'elevator', 'gain': 1.0}
    # ---- mass / inertia / propulsion / drag / limits
    c['mass'] = {'massKg': mass, 'cg': [cg_x, 0, 0], 'inertia': inertia}
    c['propulsion'] = dict(c['propulsion'], **prop)
    c['fuselage'] = copy.deepcopy(c['fuselage'])
    c['fuselage']['cd0Area'] = cd0
    c['fuselage']['crossflow']['lengthM'] = crossflow_len
    c['limits'] = limits
    for axis, vals in ctl.items():
        c['controls'][axis].update(vals)
    # ---- gear: tricycle, mains under the low wing, steerable nosewheel linked to the pedals
    for g in c['gear']:
        if g['isSteerable']:
            g['pos'] = [gear_nose_x, 0, gear_z]
        else:
            g['pos'] = [gear_main_x, math.copysign(1.55 if span > 11 else 1.5, g['pos'][1]), gear_z]
    json.dump(c, open(os.path.join(CFG, idname + '.json'), 'w'), indent=2)
    print(f"{idname}: wing {wing_area(c):.2f} m2 (target {area}), span {span} m, mass {mass} kg, tail x{tail_scale}")


def axis(rev=True, ch=(-0.12, -0.40), kt=0, trim='tab', **extra):
    d = {'reversible': rev, 'hingeChAlpha': ch[0], 'hingeChDelta': ch[1], 'centeringSpringKt': kt, 'trimType': trim, 'freeTauS': 0.12}
    d.update(extra)
    return d


# Piper PA-28-181 Archer: 35 ft / 170 ft2, NACA 65-415, 7 deg dihedral, 2550 lb gross (flown ~2400 lb), O-360 180 hp,
# 76 in fixed-pitch prop, ALL-MOVING STABILATOR with anti-servo tab (14 up / 2 down; stick-free returns to trim),
# rudder spring-trim device in the pedal torque tube + nosewheel linked to the pedals; flaps 10/25/40.
make('pa28-archer-like', 'Archer (PA-28-181)', span=10.67, area=15.8, taper_tip=0.62, dihedral_deg=7, wing_z=0.45,
     mass=1090, cg_x=-0.42, inertia={'ixx': 1300, 'iyy': 1500, 'izz': 2620, 'ixz': 70},
     tail_scale=0.72, fin_scale=0.95, stabilator=True,
     prop={'maxPowerW': 134000, 'propDiameterM': 1.93, 'maxRpm': 2700, 'efficiency': 0.72, 'constantSpeed': False},
     cd0=0.40, limits={"vneMs": 79, 'gMax': 3.8, 'gMin': -1.52}, spawn=55,
     ctl={'aileron': axis(ch=(-0.20, -0.45), maxDeflRad=0.35),
          'elevator': axis(ch=(-0.05, -0.45), maxDeflRad=0.24),              # stabilator + anti-servo tab
          'rudder': axis(kt=45, trim='spring', maxDeflRad=0.49)},             # pedal spring device + nosewheel link
     flap_delta_rad=0.24, gear_main_x=-0.55, gear_nose_x=1.55, gear_z=1.05, crossflow_len=7.3)

# Cirrus SR22: 38.3 ft / 144.9 ft2, AR 10, ~4.5 deg dihedral, 3600 lb gross (flown ~3300 lb), IO-550-N 310 hp, 78 in
# 3-blade CONSTANT-SPEED prop, fixed gear with fairings, conventional elevator. EVERY axis spring-loaded: electric trim
# moves the neutral of SPRING CARTRIDGES (no tabs) in pitch and roll; spring yaw-trim cartridge + rudder-aileron
# interconnect bungee; flaps 50 % / 100 % (≈16 / 32 deg).
make('cirrus-sr22-like', 'Cirrus SR22', span=11.67, area=13.46, taper_tip=0.55, dihedral_deg=4.5, wing_z=0.4,
     mass=1500, cg_x=-0.38, inertia={'ixx': 2100, 'iyy': 2400, 'izz': 3900, 'ixz': 90},
     tail_scale=0.82, fin_scale=1.05, stabilator=False,
     prop={'maxPowerW': 231000, 'propDiameterM': 1.98, 'maxRpm': 2700, 'propInertia': 4.5, 'efficiency': 0.80,
           'constantSpeed': True, 'governedRpm': 2700},
     cd0=0.22, limits={'vneMs': 107, 'gMax': 3.8, 'gMin': -1.9}, spawn=75,
     ctl={'aileron': axis(ch=(-0.20, -0.45), kt=45, trim='spring', maxDeflRad=0.30),
          'elevator': axis(ch=(-0.15, -0.45), kt=50, trim='spring', maxDeflRad=0.40),
          'rudder': axis(ch=(-0.15, -0.45), kt=45, trim='spring', maxDeflRad=0.40)},
     flap_delta_rad=0.26, gear_main_x=-0.45, gear_nose_x=1.7, gear_z=1.0, crossflow_len=7.9)
