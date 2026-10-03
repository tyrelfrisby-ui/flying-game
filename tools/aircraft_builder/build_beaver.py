"""de Havilland Canada DHC-2 Beaver on floats (owner 2026-10-02), derived from the tuned pa18-floats-like config (high
strut-braced wing on floats) by geometric transform. Run: python3 tools/aircraft_builder/build_beaver.py

Data (DHC-2 Mk I POH / type certificate figures): span 48 ft (14.63 m), wing area 250 ft2 (23.2 m2), length 30 ft 3 in
(9.22 m; ~32 ft on floats), NACA 64A416 wing, empty ~3,000 lb on floats, gross 5,090 lb on floats, P&W R-985 Wasp Jr
450 hp, Hamilton Standard 2-blade constant-speed 8 ft 6 in (2.59 m); Vne 180 mph (156 kt), cruise ~110 kt on floats,
stall ~52 kt clean / ~45 kt full flap. EDO 4930 floats (4,930 lb displacement each; length ~25 ft 10 in). Controls:
reversible cables, elevator trim tab, rudder trim tab, tailwheel-type rudder linkage absent on floats (water rudders).
"""
import copy, json, math, os

REPO = os.path.expanduser('~/Documents/flying-game')
CFG = os.path.join(REPO, 'configs/aircraft')
CUB = json.load(open(os.path.join(CFG, 'pa18-floats-like.json')))
c = copy.deepcopy(CUB)

CUB_SPAN, CUB_AREA, CUB_LEN = 10.73, 16.58, 6.88
SPAN, AREA, LEN = 14.63, 23.2, 9.6
ky, kx = SPAN / CUB_SPAN, LEN / CUB_LEN


def surf(sid):
    return next(s for s in c['surfaces'] if s['id'] == sid)


c['id'], c['displayName'], c['spawnIasMs'] = 'dhc2-beaver-floats-like', 'Beaver on Floats (DHC-2)', 48

raw = sum(st['area'] * ky for sid in ('wing', 'wing-aileron') for st in surf(sid)['strips'])
k = AREA / raw                                   # chord scale
for sid in ('wing', 'wing-aileron'):
    for st in surf(sid)['strips']:
        st['pos'] = [round(st['pos'][0] * k, 3), round(st['pos'][1] * ky, 3), st['pos'][2]]
        st['area'] = round(st['area'] * ky * k, 4)
        st['chord'] = round(st['chord'] * k, 3)
        if 'flap' in st:   # Beaver flaps go to ~58 deg (landing) — a bigger camber increment than the Cub's
            st['flap'] = dict(st['flap'], maxDeltaAlphaRad=0.30, maxClMax=0.75)
tail = (AREA / CUB_AREA) * k / kx                # same tail volume
for sid in ('hStab', 'elevator', 'vStab', 'rudder-vstab'):
    for st in surf(sid)['strips']:
        x, y, z = st['pos']
        st['area'] = round(st['area'] * tail, 4)
        st['chord'] = round(st['chord'] * math.sqrt(tail), 3)
        st['pos'] = [round(x * kx, 3), round(y * math.sqrt(tail), 3), round(z * math.sqrt(tail), 3)]

MASS = 2200.0                                     # ~4,850 lb: pilot + a load, on floats
m = MASS / CUB['mass']['massKg']
s2 = ((ky + kx) / 2) ** 2
c['mass'] = {'massKg': MASS, 'cg': [round(CUB['mass']['cg'][0] * kx, 3), 0, 0],
             'inertia': {key: round(v * m * s2) for key, v in CUB['mass']['inertia'].items()}}
c['propulsion'] = dict(CUB['propulsion'], maxPowerW=336000, propDiameterM=2.59, maxRpm=2300, idleRpm=600,
                       propInertia=9.0, efficiency=0.78, constantSpeed=True, governedRpm=2000)
c['fuselage'] = copy.deepcopy(CUB['fuselage'])
c['fuselage']['cd0Area'] = 1.25                   # radial cowl + EDO 4930s + struts
cf = c['fuselage']['crossflow']
cf.update(bodyRadiusM=0.75, planArea=round(cf['planArea'] * s2, 1), sideArea=round(cf['sideArea'] * s2, 1),
          planCenterX=round(cf['planCenterX'] * kx, 2), sideCenterX=round(cf['sideCenterX'] * kx, 2), lengthM=LEN)
c['fuselage']['sideForceArea'] = round(CUB['fuselage']['sideForceArea'] * s2, 1)
c['limits'] = {'vneMs': 80, 'gMax': 3.5, 'gMin': -1.4}

# EDO 4930 floats: ~7.9 m long, ~0.95 m beam; depth set so each displaces 4,930 lb (DisplacementTests)
f = c['floats']
# The STEP sits just aft of the CG (as on real floats; scaling the Cub's bow position put it 0.65 m aft and the boat
# porpoised): step 0.15 m behind the CG -> bow = step + stepFraction * length.
cg_x = c['mass']['cg'][0]
f.update(lengthM=7.9, beamM=0.95, depthM=0.47, spreadM=3.2, bowX=round(cg_x - 0.15 + f['stepFraction'] * 7.9, 2),
         keelZ=round(f['keelZ'] * 1.25, 2), waterRudderAreaM2=0.09)
for g in c['gear']:   # float-keel contact points follow the bigger floats
    g['pos'] = [round(g['pos'][0] * kx, 3), math.copysign(1.6, g['pos'][1]) if g['pos'][1] else 0.0, round(g['pos'][2] * 1.25, 3)]
    g['springN'] = round(g['springN'] * m)
    g['dampNs'] = round(g['dampNs'] * m)

# Reversible cables; elevator and rudder trim TABS (no springs on floats — water rudders, no tailwheel springs)
for a in ('aileron', 'elevator', 'rudder'):
    c['controls'][a].update(reversible=True, centeringSpringKt=0, trimType='tab', freeTauS=0.14)
c['controls']['elevator'].update(hingeChAlpha=-0.12, hingeChDelta=-0.40)
c['controls']['rudder'].update(hingeChAlpha=-0.12, hingeChDelta=-0.40)

json.dump(c, open(os.path.join(CFG, 'dhc2-beaver-floats-like.json'), 'w'), indent=2)
wa = sum(st['area'] for sid in ('wing', 'wing-aileron') for st in surf(sid)['strips'])
print(f"dhc2-beaver-floats-like: wing {wa:.1f} m2, tail x{tail:.2f}, mass {MASS:.0f} kg, inertia {c['mass']['inertia']}")
