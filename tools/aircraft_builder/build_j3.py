"""Piper J-3 Cub with a Continental C-85 (owner 2026-10-18: "j3 cub 85hp ... floats skis and wheels for champ
and cub"). Both configs are derived from the already-tuned Super Cub — pa18-cub-like and pa18-floats-like — by
a transform that is very nearly the identity, because THE AEROPLANES SHARE A WING. That is not a convenience:
the PA-18's wing IS the J-3's, USA 35B, same span and same area, which is why the Super Cub is the Cub's
descendant rather than a different aeroplane. So the two share one aerodynamic source of truth and differ in
the three things that actually differ.

Run: python3 tools/aircraft_builder/build_j3.py

WHAT CHANGES, and the source for each:
  * MASS. 1,220 lb gross = 553 kg against the Super Cub's 794 kg. FAA Type Certificate A-691 (model J3C);
    corroborated by AOPA and the Museum of Flight, and by CASA registry entries showing 553 kg MTOW.
  * NO FLAPS. The J-3 has none. The Super Cub's 50-degree plain flaps come straight out of the strips.
  * POWER. Continental C-85, 85 hp = 63.4 kW, against the Super Cub's O-320 at 150 hp. Fixed-pitch, and a
    72-inch (1.83 m) prop, which is what a C-85 of this era swings.

WHAT DOES NOT CHANGE: span 35 ft 3 in (10.74 m) against the Super Cub's 10.73, wing area 178.5 ft2
(16.58 m2) against 16.58, length 22 ft 5 in (6.83 m) against 6.88. Those are the AOPA and Museum of Flight
figures and they are the Super Cub's to within a centimetre, so the wing strips, the tail and the airfoil
table are taken over unchanged. Inertia scales with mass; the geometry does not.

ONE NUMBER I COULD NOT SOURCE PROPERLY, and it is flagged rather than buried: Vne. The widely repeated
figure is 122 mph (106 kt, 54.5 m/s) and that is what is used below, but I could not get at TCDS A-691
itself to confirm it, and the sources that quote it are secondary. If somebody has the data sheet, this is
the line to check. Everything else above is from a primary or a well-corroborated source.
"""
import copy, json, math, os

REPO = os.path.expanduser('~/Documents/flying-game')
CFG = os.path.join(REPO, 'configs/aircraft')

# Super Cub (the tuned source) against the J-3 (the target). All four are published figures.
CUB_SPAN, CUB_AREA, CUB_LEN = 10.73, 16.58, 6.88
SPAN, AREA, LEN = 10.74, 16.58, 6.83
ky, kx = SPAN / CUB_SPAN, LEN / CUB_LEN

J3_MASS = 553.0          # 1,220 lb, TC A-691
C85_W = 63400.0          # 85 hp
PROP_M = 1.83            # 72 in
VNE_MS = 54.5            # 122 mph — SECONDARY SOURCE, see the module docstring


def j3(base_id):
    """The J-3 airframe out of the Super Cub's. The wing is the same wing, so this mostly takes the flaps off."""
    cub = json.load(open(os.path.join(CFG, base_id + '.json')))
    c = copy.deepcopy(cub)

    def surf(sid):
        return next(s for s in c['surfaces'] if s['id'] == sid)

    raw = sum(st['area'] * ky for sid in ('wing', 'wing-aileron') for st in surf(sid)['strips'])
    k = AREA / raw                                   # chord scale; within a thousandth of 1
    for sid in ('wing', 'wing-aileron'):
        for st in surf(sid)['strips']:
            st['pos'] = [round(st['pos'][0] * k, 3), round(st['pos'][1] * ky, 3), st['pos'][2]]
            st['area'] = round(st['area'] * ky * k, 4)
            st['chord'] = round(st['chord'] * k, 3)
            st.pop('flap', None)                     # the J-3 HAS NO FLAPS
    tail = (AREA / CUB_AREA) * k / kx                # same tail volume coefficient
    for sid in ('hStab', 'elevator', 'vStab', 'rudder-vstab'):
        for st in surf(sid)['strips']:
            x, y, z = st['pos']
            st['area'] = round(st['area'] * tail, 4)
            st['chord'] = round(st['chord'] * math.sqrt(tail), 3)
            st['pos'] = [round(x * kx, 3), round(y * math.sqrt(tail), 3), round(z * math.sqrt(tail), 3)]
    return cub, c, k, tail


def common(cub, c, mass_kg):
    """Mass, inertia, engine, body and limits. Inertia scales with the mass, since the geometry is unchanged."""
    m = mass_kg / cub['mass']['massKg']
    s2 = ((ky + kx) / 2) ** 2
    c['mass'] = {'massKg': mass_kg, 'cg': [round(cub['mass']['cg'][0] * kx, 3), 0, 0],
                 'inertia': {key: round(v * m * s2) for key, v in cub['mass']['inertia'].items()}}
    # C-85: fixed pitch, a 72 in prop, and a wooden one is light, so less inertia than the Super Cub's metal.
    c['propulsion'] = dict(cub['propulsion'], maxPowerW=C85_W, propDiameterM=PROP_M,
                           propInertia=2.4, efficiency=0.74)
    c['propulsion'].pop('constantSpeed', None)
    c['propulsion'].pop('governedRpm', None)
    c['fuselage'] = copy.deepcopy(cub['fuselage'])
    cf = c['fuselage']['crossflow']
    cf.update(planArea=round(cf['planArea'] * s2, 1), sideArea=round(cf['sideArea'] * s2, 1),
              planCenterX=round(cf['planCenterX'] * kx, 2), sideCenterX=round(cf['sideCenterX'] * kx, 2),
              lengthM=LEN)
    c['fuselage']['sideForceArea'] = round(cub['fuselage']['sideForceArea'] * s2, 1)
    # +3.8/-1.5 is the Super Cub's; the J-3's certificated envelope is the same normal category, so it stands.
    c['limits'] = {'vneMs': VNE_MS, 'gMax': 3.8, 'gMin': -1.5}
    return m, s2


def write(c, out_id):
    json.dump(c, open(os.path.join(CFG, out_id + '.json'), 'w'), indent=2)

    def surf(sid):
        return next(s for s in c['surfaces'] if s['id'] == sid)
    wa = sum(st['area'] for sid in ('wing', 'wing-aileron') for st in surf(sid)['strips'])
    flaps = sum(1 for sid in ('wing', 'wing-aileron') for st in surf(sid)['strips'] if 'flap' in st)
    print(f"{out_id}: wing {wa:.2f} m2, mass {c['mass']['massKg']:.0f} kg, "
          f"{c['propulsion']['maxPowerW'] / 745.7:.0f} hp, prop {c['propulsion']['propDiameterM']} m, "
          f"flapped strips {flaps}, cd0Area {c['fuselage']['cd0Area']}, inertia {c['mass']['inertia']}")


# ---------------------------------------------------------------- on wheels (and therefore on wheel-skis)
cub, c, k, tail = j3('pa18-cub-like')
c['id'], c['displayName'], c['spawnIasMs'] = 'j3-cub-like', 'Cub (J-3, 85 hp)', 28
m, s2 = common(cub, c, J3_MASS)
# MEASURED, not guessed. The one free parameter here is the body drag, and it is set by walking it against
# the book cruise: a J-3 with a C-85 is quoted at 80 mph, which is 69.5 kt. Through the game's own 75 %
# cruise estimate at 1,000 ft: 0.62 gave 79.2 kt (far too slick for an aeroplane with no wheel pants and its
# struts in the breeze), 0.85 gave 71.4, 0.92 gave 69.5 and 1.00 gave 67.5. So 0.92.
c['fuselage']['cd0Area'] = 0.92
write(c, 'j3-cub-like')

# ---------------------------------------------------------------- on floats
cub, c, k, tail = j3('pa18-floats-like')
c['id'], c['displayName'], c['spawnIasMs'] = 'j3-cub-floats-like', 'Cub on Floats (J-3, 85 hp)', 26
# The seaplane gross is higher than the landplane's on the J3C-65S, but I could not source the figure, so
# this is the landplane gross plus the floats' own weight and nothing else — honest rather than optimistic,
# and it makes the float Cub the underpowered aeroplane it famously is on 85 hp.
m, s2 = common(cub, c, J3_MASS + 60.0)
# Same method against the float figure, which is about 70 mph: 0.95 gave 67.6 kt, 1.25 gave 61.8 and 1.45
# gave 59.8. So 1.25 — and the 8 kt the floats cost falls out of it rather than being put in by hand.
c['fuselage']['cd0Area'] = 1.25
for a in ('aileron', 'elevator', 'rudder'):
    c['controls'][a]['centeringSpringKt'] = 0         # water rudders, no tailwheel spring
write(c, 'j3-cub-floats-like')
