"""de Havilland Canada DHC-2 Beaver (owner 2026-10-02 on floats; the LANDPLANE added 2026-10-16 because the owner
asked for a Beaver that can land on the glacier and floats cannot take skis). Both are derived from the tuned Cub
configs — pa18-floats-like for the float version, pa18-cub-like for the landplane — by the same geometric transform,
so the two Beavers share one aerodynamic source of truth and differ only in what they stand on.
Run: python3 tools/aircraft_builder/build_beaver.py

Data (DHC-2 Mk I POH / type certificate figures): span 48 ft (14.63 m), wing area 250 ft2 (23.2 m2), length 30 ft 3 in
(9.22 m; ~32 ft on floats), NACA 64A416 wing, empty ~2,650 lb on wheels / ~3,000 lb on floats, gross 5,100 lb, P&W
R-985 Wasp Jr 450 hp, Hamilton Standard 2-blade constant-speed 8 ft 6 in (2.59 m); Vne 180 mph (156 kt), cruise
~124 kt on wheels and ~110 kt on floats, stall ~52 kt clean / ~45 kt full flap. EDO 4930 floats (4,930 lb
displacement each; length ~25 ft 10 in). Landplane undercarriage: tailwheel, main track 9 ft 10 in (3.0 m), main
tyres 8.50-10. Controls: reversible cables, elevator trim tab, rudder trim tab.
"""
import copy, json, math, os

REPO = os.path.expanduser('~/Documents/flying-game')
CFG = os.path.join(REPO, 'configs/aircraft')

CUB_SPAN, CUB_AREA, CUB_LEN = 10.73, 16.58, 6.88
SPAN, AREA, LEN = 14.63, 23.2, 9.6
ky, kx = SPAN / CUB_SPAN, LEN / CUB_LEN


def beaver(base_id):
    """The Beaver airframe, scaled up out of a Cub: wing, tail, mass, engine and body. Everything above the
    undercarriage is identical between the two versions, which is the point of doing it here."""
    cub = json.load(open(os.path.join(CFG, base_id + '.json')))
    c = copy.deepcopy(cub)

    def surf(sid):
        return next(s for s in c['surfaces'] if s['id'] == sid)

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
    return cub, c, k, tail


def common(cub, c, mass_kg):
    m = mass_kg / cub['mass']['massKg']
    s2 = ((ky + kx) / 2) ** 2
    c['mass'] = {'massKg': mass_kg, 'cg': [round(cub['mass']['cg'][0] * kx, 3), 0, 0],
                 'inertia': {key: round(v * m * s2) for key, v in cub['mass']['inertia'].items()}}
    c['propulsion'] = dict(cub['propulsion'], maxPowerW=336000, propDiameterM=2.59, maxRpm=2300, idleRpm=600,
                           propInertia=9.0, efficiency=0.78, constantSpeed=True, governedRpm=2000)
    c['fuselage'] = copy.deepcopy(cub['fuselage'])
    cf = c['fuselage']['crossflow']
    cf.update(bodyRadiusM=0.75, planArea=round(cf['planArea'] * s2, 1), sideArea=round(cf['sideArea'] * s2, 1),
              planCenterX=round(cf['planCenterX'] * kx, 2), sideCenterX=round(cf['sideCenterX'] * kx, 2), lengthM=LEN)
    c['fuselage']['sideForceArea'] = round(cub['fuselage']['sideForceArea'] * s2, 1)
    c['limits'] = {'vneMs': 80, 'gMax': 3.5, 'gMin': -1.4}

    # Reversible cables; elevator and rudder trim TABS
    for a in ('aileron', 'elevator', 'rudder'):
        c['controls'][a].update(reversible=True, centeringSpringKt=0, trimType='tab', freeTauS=0.14)
    c['controls']['elevator'].update(hingeChAlpha=-0.12, hingeChDelta=-0.40)
    c['controls']['rudder'].update(hingeChAlpha=-0.12, hingeChDelta=-0.40)
    return m, s2


def write(c, out_id):
    json.dump(c, open(os.path.join(CFG, out_id + '.json'), 'w'), indent=2)

    def surf(sid):
        return next(s for s in c['surfaces'] if s['id'] == sid)
    wa = sum(st['area'] for sid in ('wing', 'wing-aileron') for st in surf(sid)['strips'])
    print(f"{out_id}: wing {wa:.1f} m2, mass {c['mass']['massKg']:.0f} kg, cd0Area {c['fuselage']['cd0Area']}, "
          f"inertia {c['mass']['inertia']}")


# ---------------------------------------------------------------- on floats
cub, c, k, tail = beaver('pa18-floats-like')
c['id'], c['displayName'], c['spawnIasMs'] = 'dhc2-beaver-floats-like', 'Beaver on Floats (DHC-2)', 48
m, s2 = common(cub, c, 2200.0)                        # ~4,850 lb: pilot + a load, on floats
c['fuselage']['cd0Area'] = 1.25                       # radial cowl + EDO 4930s + struts
for a in ('aileron', 'elevator', 'rudder'):           # no springs on floats — water rudders, no tailwheel spring
    c['controls'][a]['centeringSpringKt'] = 0

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
write(c, 'dhc2-beaver-floats-like')

# ---------------------------------------------------------------- on wheels (and therefore on wheel-skis)
cub, c, k, tail = beaver('pa18-cub-like')
c['id'], c['displayName'], c['spawnIasMs'] = 'dhc2-beaver-like', 'Beaver (DHC-2)', 50
# 4,500 lb: the same pilot and load as the float version, less the ~350 lb of EDO 4930s and their rigging.
m, s2 = common(cub, c, 2040.0)
# Radial cowl, struts and fixed tailwheel gear, no floats. Checked against the book cruise: 124 kt at 5,000 ft wants
# ~2.6 kN of drag, which is 285 shaft hp through a 0.78 prop — about 63% power, where the Beaver is actually flown.
# The 1.25 on floats is this plus the EDO 4930s, and the 12 kt the real float installation costs falls out of it.
c['fuselage']['cd0Area'] = 1.00
# Tailwheel undercarriage: main track 3.0 m (type certificate), mains and tailwheel at the Cub's stations scaled by
# length, legs and tyres a quarter taller than the scale because the Beaver stands high on 8.50-10s.
for g in c['gear']:
    tw = g.get('isTailwheel', False)
    g['pos'] = [round(g['pos'][0] * kx, 3),
                math.copysign(1.5, g['pos'][1]) if g['pos'][1] else 0.0,
                round(g['pos'][2] * 1.25, 3)]
    g['springN'] = round(g['springN'] * m)
    g['dampNs'] = round(g['dampNs'] * m)
    g['corneringStiffnessN'] = round(g['corneringStiffnessN'] * m)
    g['rollResistN'] = round(g['rollResistN'] * m)
    if 'steerSpringNmPerRad' in g:
        g['steerSpringNmPerRad'] = round(g['steerSpringNmPerRad'] * m, 1)
write(c, 'dhc2-beaver-like')
