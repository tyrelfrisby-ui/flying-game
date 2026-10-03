"""Hughes H-4 Hercules (owner 2026-10-02), derived from the tuned dc3-like config by geometric transform (big piston
multi-engine with real NACA inertia), re-sized to the H-4. Run: python3 tools/aircraft_builder/build_h4.py

Data (Hughes/Evergreen museum, Wikipedia figures): span 320 ft 11 in (97.54 m), length 218 ft 8 in (66.65 m), wing
area 11,430 ft2 (1,061.9 m2), empty 250,000 lb, gross 400,000 lb (flown 2 Nov 1947 near 300,000 lb), 8 x P&W R-4360
3,000 hp, Hamilton Standard 4-blade 17 ft 2 in (4.27 m) constant-speed props; cruise ~250 mph, design max 351 mph.
Flying-boat hull (beam ~25 ft) with wingtip floats; FULLY HYDRAULIC controls (Hughes' own artificial-feel system) — no
reversibility. The one flight: ~1 mile at ~70 ft, ~135 mph.
"""
import copy, json, math, os

REPO = os.path.expanduser('~/Documents/flying-game')
CFG = os.path.join(REPO, 'configs/aircraft')
DC3 = json.load(open(os.path.join(CFG, 'dc3-like.json')))
c = copy.deepcopy(DC3)

DC3_SPAN, DC3_LEN = 28.96, 19.66
SPAN, LEN, AREA = 97.54, 66.65, 1061.9
ky, kx = SPAN / DC3_SPAN, LEN / DC3_LEN


def surf(sid):
    return next(s for s in c['surfaces'] if s['id'] == sid)


c['id'], c['displayName'], c['spawnIasMs'] = 'hughes-h4-like', 'Hughes H-4 Hercules', 70

# ---- wing: shoulder-mounted on the hull (2.5 m above the CG), small dihedral, area matched
WING_Z = -2.5
for sid in ('wing', 'wing-aileron'):
    s = surf(sid)
    s['heightAboveBodyAxisM'] = -WING_Z
    for st in s['strips']:
        st['pos'] = [st['pos'][0] * kx, st['pos'][1] * ky, WING_Z]
        st['dihedralRad'] = round(math.radians(2.0), 5)
raw = sum(st['area'] * ky for sid in ('wing', 'wing-aileron') for st in surf(sid)['strips'])
k = AREA / raw
for sid in ('wing', 'wing-aileron'):
    for st in surf(sid)['strips']:
        st['area'] = round(st['area'] * ky * k, 3)
        st['chord'] = round(st['chord'] * k, 3)
        st['pos'] = [round(v, 3) for v in st['pos']]

# ---- tail: arm scales with length; areas for the same tail volume (S*c/l): x(AREA/91.9)*(chord ratio)/(arm ratio)
tail_area = (AREA / 91.87) * k / kx
for sid in ('hStab', 'elevator', 'vStab', 'rudder-vstab'):
    for st in surf(sid)['strips']:
        st['area'] = round(st['area'] * tail_area, 3)
        st['chord'] = round(st['chord'] * math.sqrt(tail_area), 3)
        x, y, z = st['pos']
        st['pos'] = [round(x * kx, 3), round(y * math.sqrt(tail_area), 3), round(z * math.sqrt(tail_area) - 2.0, 3)]

# ---- mass / inertia (DC-3's NACA-measured inertia scaled by mass x size^2)
MASS = 136000.0
s2 = ((ky + kx) / 2) ** 2
m = MASS / DC3['mass']['massKg']
c['mass'] = {'massKg': MASS, 'cg': [round(DC3['mass']['cg'][0] * kx, 2), 0, 0],
             'inertia': {k2: round(v * m * s2) for k2, v in DC3['mass']['inertia'].items()}}

# ---- eight R-4360s along the leading edge
c['propulsion'] = dict(DC3['propulsion'], maxPowerW=2237000, propDiameterM=4.27, maxRpm=2700, governedRpm=2700,
                       propInertia=60.0, efficiency=0.82, constantSpeed=True)
c['engines'] = [{'pos': [round(4.5 - 0.06 * abs(y), 2), y, WING_Z - 0.3], 'rotationSign': 1, 'throttleScale': 1.0}
                for y in (-33.5, -25.0, -16.5, -8.5, 8.5, 16.5, 25.0, 33.5)]

# ---- hull drag / crossflow
c['fuselage'] = copy.deepcopy(DC3['fuselage'])
c['fuselage']['cd0Area'] = 24.0
cf = c['fuselage']['crossflow']
cf.update(bodyRadiusM=3.9, planArea=round(cf['planArea'] * s2), sideArea=round(cf['sideArea'] * s2 * 1.4),
          planCenterX=round(cf['planCenterX'] * kx, 2), sideCenterX=round(cf['sideCenterX'] * kx, 2), lengthM=LEN)
c['fuselage']['sideForceArea'] = round(DC3['fuselage']['sideForceArea'] * s2 * 1.4)

# ---- a boat: no wheels; planing hull on the centreline + wingtip floats
c['gear'] = []
c['retractableGear'] = False
c.pop('gearDragAreaM2', None)
c['floats'] = {
    'count': 1, 'lengthM': 60.0, 'beamM': 7.6, 'depthM': 7.0, 'deadriseDeg': 22, 'stepFraction': 0.52,
    'spreadM': 0.0, 'bowX': 31.0, 'keelZ': 6.0, 'forebodyKeelDeg': 3.0, 'afterbodyKeelDeg': 6.0,
    'waterRudderAreaM2': 0.0, 'waterRudderMaxRad': 0.0,
    'tipFloats': {'count': 2, 'lengthM': 7.0, 'beamM': 1.6, 'depthM': 1.5, 'deadriseDeg': 20, 'stepFraction': 0.5,
                  'spreadM': 74.0, 'bowX': 2.5, 'keelZ': 5.2, 'forebodyKeelDeg': 3.0, 'afterbodyKeelDeg': 5.0,
                  'waterRudderAreaM2': 0.0, 'waterRudderMaxRad': 0.0}}

c['limits'] = {'vneMs': 140, 'gMax': 2.5, 'gMin': -1.0}
for a in ('aileron', 'elevator', 'rudder'):
    c['controls'][a] = {key: v for key, v in c['controls'][a].items()
                        if key not in ('hingeChAlpha', 'hingeChDelta', 'centeringSpringKt', 'trimType', 'freeTauS')}
    c['controls'][a]['reversible'] = False            # hydraulic, artificial feel
    c['controls'][a]['rateRadPerSec'] = 0.8           # big boosted surfaces move slowly

json.dump(c, open(os.path.join(CFG, 'hughes-h4-like.json'), 'w'), indent=2)
wa = sum(st['area'] for sid in ('wing', 'wing-aileron') for st in surf(sid)['strips'])
print(f"hughes-h4-like: wing {wa:.0f} m2, tail x{tail_area:.1f}, mass {MASS:.0f} kg, inertia {c['mass']['inertia']}")
