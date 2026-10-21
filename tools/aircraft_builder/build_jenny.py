"""Curtiss JN-4D "Jenny" (1917) — `jn4-jenny-like`.

Data, sources and every derivation: docs/research/JN4-JENNY-DATA.md. Nothing in here is a number chosen to make a
test pass. The one calibrated value is fuselage.cd0Area, which the protocol directs to be calibrated against the
book speeds, and §9 of the research doc shows the drag bookkeeping behind it.

Run: python3 tools/aircraft_builder/build_jenny.py

WHERE THE GEOMETRY COMES FROM. Two authorities, and they are used for different things:

  * The **JN-4D specification sheet** for everything it publishes — the two spans, the chord, the three wing areas,
    the four tail areas, the gap, the stagger, the incidence, the dihedral, the weights. These win on anything they
    state.
  * **helijah's model** (assets-incoming/jenny/source/jenny.obj, the same mesh the game draws) for the STATIONS the
    spec sheet is silent about: where along the fuselage the wings, wheels, cockpits, engine and tail sit. It is a
    dimensioned three-view that happens to be a mesh, and a faithful one — its span reads 13.310 m against a
    published 13.294, its chord 1.499 against 1.5113, its length 8.253 against 8.331, its height 2.982 against
    3.013, and its wheels are 26 in. Each station below names the mesh object it was read off, so any of it can be
    re-measured.

    This replaced a first pass built on stations scaled off a published three-view by eye. The two disagreed by up
    to half a metre — the wings are much further forward on this aeroplane than they look — and the mesh won.

The mass, the CG and the whole inertia tensor are COMPUTED from the component breakdown (protocol §1c) by
B.mass_properties. Move a component and the CG, the inertias, the wing positions, the gear and the three-point
attitude all move with it.

Three things make this aeroplane different from everything else in the hangar:

  1. **Ailerons on the UPPER wing only**, and large: 35.20 sq ft between two surfaces, over the outer half of the
     span, overhung past the tip. The spec sheet counts two of them, not four — four is the JN-4Can and the JN-4H.
     (The Stearman is the mirror image: lower wing only.)
  2. **A fin of 3.80 sq ft.** That gives a tail volume V_v of 0.017 against the protocol's 0.03–0.05 band, and it
     is not a mistake in the measurement — it is why the Jenny wallows and needs a bootful of rudder.
  3. **No wheel brakes.** It stops on a tail skid, in grass. Both mains have brake=false.
"""
import sys, os, math, json

sys.path.insert(0, os.path.dirname(__file__))
import builder as B

IN, FT = 0.0254, 0.3048

# ---- published dimensions (research doc §2) -------------------------------------------------------------------
UP_HALF = (43 * FT + 7.375 * IN) / 2         # 6.647 m   (mesh: 6.655 to the overhung aileron tip)
LO_HALF = (33 * FT + 11.25 * IN) / 2         # 5.172 m   (mesh: 5.151)
UP_SPAN, LO_SPAN = 2 * UP_HALF, 2 * LO_HALF
CHORD = 59.5 * IN                             # 1.5113 m, both planes, constant (mesh: 1.499)
GAP = 60 * IN                                 # 1.524 m   (mesh: 1.606)
STAGGER = 16 * IN                             # 0.4064 m, upper forward (mesh: 0.328)
LENGTH = 27 * FT + 4 * IN                     # 8.331 m   (mesh: 8.253)
UP_AREA = (167.94 + 35.20) * FT * FT          # 18.872 m², upper planes PLUS the ailerons
LO_AREA = 149.42 * FT * FT                    # 13.882 m²
WING_AREA = UP_AREA + LO_AREA                 # 32.754 m², the published total supporting surface
AIL_AREA = 35.20 * FT * FT / 2                # 1.635 m² each
STAB_AREA, ELEV_AREA = 28.70 * FT * FT, 22.00 * FT * FT
FIN_AREA, RUD_AREA = 3.80 * FT * FT, 12.00 * FT * FT
PROP_D = 95 * IN                              # 2.413 m, measured from an original OX-5 propeller (mesh: 2.518)

# ---- stations MEASURED off the mesh. s = m aft of the spinner's front face; y = m up, propeller axis at y 0.025.
# The mesh object each one was read from is named; re-measure with the loop in the research doc §6.
S_PROP, Y_PROP = 0.056, 0.025                 # helice  s 0.004..0.108, y centred 0.025
S_ENGINE, Y_ENGINE = 0.879, 0.285             # moteurs s 0.549..1.208, y 0.171..0.398 (a V-8's banks sit high)
S_UP_LE, Y_UP = 1.279, 1.033                  # aileH   s 1.279..2.778 (chord 1.499), y mid 1.033
S_AXLE, Y_AXLE = 1.531, -1.165                # essieu  s 1.495..1.567, y mid -1.165
WHEEL_R, TRACK_HALF = 0.334, 0.803            # roueG   y -1.498..-0.831, z mid 0.803  -> 26 in wheels, 1.606 m track
S_SEAT_F, S_SEAT_R = 2.10, 3.15               # interieur s 1.553..3.634; the two 'panel' clusters at 1.70 and 2.74
S_STAB_LE, S_ELEV_LE = 6.256, 7.295           # ailes2 s 6.256..7.296 (chord 1.040); profondeur s 7.295..8.206
STAB_CHORD, ELEV_CHORD = 1.040, 0.911
STAB_HALF, ELEV_HALF = 1.463, 1.596           # ailes2 z ±1.463; profondeur z ±1.596
S_RUD_LE, RUD_CHORD = 7.292, 0.961            # direction s 7.292..8.253, y -0.152..1.452
S_FIN_LE, FIN_CHORD = 6.90, 0.50              # the fin is part of the fuselage mesh; it fairs into the rudder hinge
S_SKID, Y_SKID = 7.099, -0.510                # pate: its lowest point

# The lower wing takes the PUBLISHED gap and stagger from the upper wing's measured position (the mesh's own gap
# and stagger are 5 % and 20 % off the sheet, and the sheet wins on anything it states).
S_LO_LE, Y_LO = S_UP_LE + STAGGER, Y_UP - GAP

# ---- the component mass breakdown (research doc §7) ----------------------------------------------------------
# The first nine lines are the empty weight and come to the published 1,430 lb; the last four are the published
# 490 lb useful load. The struts and wires get their own line because on a two-bay biplane they are 65 lb of
# structure sitting BETWEEN the wings, well forward, and burying them in a wing line puts them in the wrong place.
ITEMS = [
    B.item('propeller, wood 95 in',          30, S_PROP, 0.025 - Y_PROP, shape=('rod_y', PROP_D)),
    B.item('radiator and coolant',           65, 0.35, 0.025 - 0.20, shape=('box', 0.25, 0.65, 0.85)),
    B.item('OX-5, dry (4.33 lb/hp x 90)',   390, S_ENGINE, 0.025 - Y_ENGINE, shape=('box', 0.66, 1.00, 0.23)),
    B.item('bearers, mount, accessories',    70, 1.05, 0.025 - 0.20),
    B.item('landing gear, bungee axle',     100, S_AXLE, 0.025 - Y_AXLE, y_pair=TRACK_HALF),
    B.item('upper wing, ailerons, cabane',  230, S_UP_LE + 0.40 * CHORD, 0.025 - Y_UP, shape=('rod_y', UP_SPAN)),
    B.item('lower wing',                    165, S_LO_LE + 0.40 * CHORD, 0.025 - Y_LO, shape=('rod_y', LO_SPAN)),
    B.item('interplane struts and wires',    65, 2.04, 0.025 - 0.23, shape=('rod_y', 9.1)),
    B.item('cockpits, seats, instruments',   70, 2.40, 0.025),
    B.item('fuselage, fabric, controls',    185, 3.35, 0.025 - 0.15, shape=('rod_x', 5.90)),
    B.item('tail group and skid',            60, 7.10, 0.025 - 0.10, shape=('rod_y', 2 * ELEV_HALF)),
    #  ---- the published 490 lb useful load: fuel 130, oil 30, two occupants at 165 ----
    B.item('oil, 4 gal',                     30, 1.20, 0.025 + 0.20),
    B.item('fuel, 21 US gal',               130, 2.10, 0.025 - 0.55, shape=('box', 0.60, 0.70, 0.40)),
    B.item('front occupant',                165, S_SEAT_F, 0.025, shape=('box', 0.50, 0.50, 1.20)),
    B.item('rear occupant',                 165, S_SEAT_R, 0.025, shape=('box', 0.50, 0.50, 1.20)),
]
EMPTY = 11          # the first EMPTY items are the empty weight

# Ailerons: the mesh puts them from z 3.112 outboard, which is 47 % of the half-span — half the wing, not the
# third a modern aeroplane uses. Their chord fraction then follows from the PUBLISHED area rather than being
# measured off the mesh, because the mesh draws them deflected: 1.635 m² over 1.5113 m of chord and 3.536 m of
# span is 0.306 of the chord.
AIL_FRAC = 3.112 / UP_HALF
AIL_CHORD_FRAC = AIL_AREA / (CHORD * UP_HALF * (1 - AIL_FRAC))


def jenny_tail(cgx):
    """The Jenny's own tail, not builder.tail(): that one hard-codes a 2.4 m span and a 0.7 m chord. This
    aeroplane's horizontal tail is 3.19 m across with a 1.95 m total chord, and its fin is a third the area of any
    other type's. Stabilizer incidence 0 deg (spec sheet) against the wings' 2 deg."""
    n = 8
    stab_qc, elev_qc = cgx - (S_STAB_LE + STAB_CHORD / 4), cgx - (S_ELEV_LE + ELEV_CHORD / 4)
    fin_qc, rud_qc = cgx - (S_FIN_LE + FIN_CHORD / 4), cgx - (S_RUD_LE + RUD_CHORD / 4)
    sy = [round(-STAB_HALF + 2 * STAB_HALF * i / (n - 1), 3) for i in range(n)]
    ey = [round(-ELEV_HALF + 2 * ELEV_HALF * i / (n - 1), 3) for i in range(n)]
    stab = [{'pos': [round(stab_qc, 3), y, 0.0], 'chord': round(STAB_CHORD, 3), 'area': round(STAB_AREA / n, 4),
             'incidenceRad': 0.0, 'dihedralRad': 0.0, 'airfoil': 'naca0012-like',
             'control': {'surface': 'elevator', 'gain': 0.45}, 'sweepRad': 0.0} for y in sy]
    elev = [{'pos': [round(elev_qc, 3), y, 0.0], 'chord': round(ELEV_CHORD, 3), 'area': round(ELEV_AREA / n, 4),
             'incidenceRad': 0.0, 'dihedralRad': 0.0, 'airfoil': 'fin-lowAR',
             'control': {'surface': 'elevator', 'gain': 1.0}, 'sweepRad': 0.0} for y in ey]
    # Fin and rudder heights: 'direction' runs y -0.152..1.452, so the rudder reaches BELOW the fuselage axis.
    fin_zs, rud_zs = [-0.35, -0.70, -1.05], [0.10, -0.30, -0.75, -1.20]
    fin = [{'pos': [round(fin_qc, 3), 0.0, z], 'chord': round(FIN_CHORD, 3), 'area': round(FIN_AREA / len(fin_zs), 4),
            'incidenceRad': 0.0, 'dihedralRad': 0.0, 'airfoil': 'naca0012-like',
            'control': {'surface': 'rudder', 'gain': 0.45}, 'sweepRad': 0.0} for z in fin_zs]
    rud = [{'pos': [round(rud_qc, 3), 0.0, z], 'chord': round(RUD_CHORD, 3), 'area': round(RUD_AREA / len(rud_zs), 4),
            'incidenceRad': 0.0, 'dihedralRad': 0.0, 'airfoil': 'fin-lowAR',
            'control': {'surface': 'rudder', 'gain': 1.0}, 'sweepRad': 0.0} for z in rud_zs]
    return stab, elev, fin, rud


def reversible(axis, ch_alpha, ch_delta, spring=0):
    """Cables, no trim tab, no trim spring, no centering spring, and UNBALANCED plain surfaces — no horn balance
    and no mass balance anywhere on a JN-4D, so the hinge moments are one step heavier than the horn-balanced
    Stearman's. trimType 'tab' is the repo default (it sets the elevator's free-float angle); the Jenny's
    stabilizer incidence is ground-set. docs/research/CONTROLS-REVERSIBILITY-AND-SPRINGS.md."""
    axis.update({'reversible': True, 'hingeChAlpha': ch_alpha, 'hingeChDelta': ch_delta,
                 'centeringSpringKt': spring, 'trimType': 'tab', 'freeTauS': 0.12})
    return axis


def cg_of(items):
    tot = sum(i['lb'] for i in items)
    return sum(i['lb'] * i['x'] for i in items) / tot, tot


def build():
    mass, cgx, cgz, inertia = B.mass_properties(ITEMS)

    # Everything below is measured from that computed CG. Config axes: x FORWARD, z DOWN.
    up_qc, lo_qc = round(cgx - (S_UP_LE + CHORD / 4), 3), round(cgx - (S_LO_LE + CHORD / 4), 3)
    up_z, lo_z = round((0.025 - Y_UP) - cgz, 3), round((0.025 - Y_LO) - cgz, 3)

    # ---- wings: two spans, one chord, ailerons outboard on the upper wing only -------------------------------
    uf, ua = B.wing_panel(UP_HALF, 8, lambda f: CHORD, up_z, 1.0, 2.0, 0.0, 'clarkY-like',
                          AIL_FRAC, 1.0, x0=up_qc, ail_chord_frac=AIL_CHORD_FRAC)
    lf, _ = B.wing_panel(LO_HALF, 8, lambda f: CHORD, lo_z, 1.0, 2.0, 0.0, 'clarkY-like',
                         1.1, 0.0, x0=lo_qc)        # ail_frac 1.1: frac never exceeds it, so no ailerons
    ku = UP_AREA / (sum(s['area'] for s in uf) + sum(s['area'] for s in ua))
    kl = LO_AREA / sum(s['area'] for s in lf)
    for s in uf + ua:
        s['area'] = round(s['area'] * ku, 4)
    for s in lf:
        s['area'] = round(s['area'] * kl, 4)

    stab, elev, fin, rud = jenny_tail(cgx)

    ctl = B.controls(0.35, 0.44, 0.45)
    reversible(ctl['aileron'], -0.25, -0.55)       # big, deep, overhung and unbalanced: the heaviest in the fleet
    reversible(ctl['elevator'], -0.15, -0.50)
    reversible(ctl['rudder'], -0.15, -0.50)        # no tailwheel, so no steering springs: centeringSpringKt 0

    B.write('jn4-jenny-like', 'Trainer Biplane (JN-4D)', round(mass, 1), [0, 0, 0], inertia,
            [{'id': 'wing-upper', 'heightAboveBodyAxisM': round(-up_z, 3), 'oswaldE': 0.80, 'strips': uf},
             {'id': 'wing-upper-aileron', 'heightAboveBodyAxisM': round(-up_z, 3), 'oswaldE': 0.80, 'strips': ua},
             {'id': 'wing-lower', 'heightAboveBodyAxisM': round(-lo_z, 3), 'oswaldE': 0.80, 'strips': lf},
             {'id': 'hStab', 'oswaldE': 0.85, 'strips': stab},
             {'id': 'elevator', 'oswaldE': 0.85, 'strips': elev},
             {'id': 'vStab', 'oswaldE': 0.85, 'strips': fin},
             {'id': 'rudder-vstab', 'oswaldE': 0.85, 'strips': rud}],
            # cd0Area CALIBRATED on the published 75 mph maximum level speed, which is the firmest number a 1917
            # aeroplane has (BOOK_SPEEDS=1 sweeps it). 1.42 m² is 15.3 sq ft of flat plate — a radiator, an
            # uncowled V-8, two open cockpits, eight interplane struts, a cabane, flying and landing wires, a
            # bungee axle and a tail skid.
            {'cd0Area': 1.42, 'sideForceArea': 5.0, 'damping': {'p': 0, 'q': 0, 'r': 0},
             'crossflow': {'bodyRadiusM': 0.45, 'planArea': 4.5, 'planCenterX': -0.6,
                           'sideArea': 5.0, 'sideCenterX': -0.5, 'cd': 1.2, 'lengthM': LENGTH}},
            # OX-5: 90 hp at 1,400 rpm. A 95 in one-piece wood propeller is a flywheel — the highest prop inertia
            # per horsepower in the fleet, and the gyroscopic term should show it.
            {'maxPowerW': 67000, 'propDiameterM': round(PROP_D, 3), 'idleRpm': 350, 'maxRpm': 1400,
             'propInertia': 10.0, 'rotationSign': 1, 'efficiency': 0.70,
             'thrustLineZ': round((0.025 - Y_PROP) - cgz, 3), 'pFactorK': 0.35, 'slipstreamK': 0.12,
             'constantSpeed': False},
            # No placarded Vne or load factor was found. 100 mph against a 75 mph maximum level speed, and
            # +3.5/-1.5 g: it was not stressed for aerobatics and barnstormers who looped them lost wings.
            {'vneMs': 44.7, 'gMax': 3.5, 'gMin': -1.5}, ctl, vbf=0.5)

    # ---- the parts builder.write() does not emit: spawn speed, wake blanketing, and the gear ------------------
    path = os.path.join(B.CFG, 'jn4-jenny-like.json')
    c = json.load(open(path))
    c['spawnIasMs'] = 26.8                      # 60 mph book cruise
    # Elevator plain-flap saturation, produced by the repo's OWN bisection, not typed in:
    #   CALIBRATE_STALL=1 CALIBRATE_ONLY=jn4-jenny-like dotnet test --filter StallAuthorityTests
    # It solves for full aft stick balancing 0.5 deg UNDER the wing stall, which is the trainer target, and lands
    # on 12.4 deg (full-aft balance 16.0 against a 16.5 deg clarkY-like stall). It is written here so that
    # re-running this builder cannot silently undo the calibration; re-derive it with the line above after any
    # change to the tail, the CG or the wing table.
    c['controls']['elevator']['saturationDeg'] = 12.4
    c['wakeBlanketMaxLoss'] = 0.45              # as the Stearman: a biplane's lower wing lives in the upper's wake
    main = dict(springN=55000, dampNs=3000, brake=False, isTailwheel=False, isSteerable=False,
                maxSteerRad=0.0, tireMu=0.8, corneringStiffnessN=7000, rollResistN=45)
    z_main = round((0.025 - (Y_AXLE - WHEEL_R)) - cgz, 3)
    z_skid = round((0.025 - Y_SKID) - cgz, 3)
    c['gear'] = [
        {'pos': [round(cgx - S_AXLE, 3), -TRACK_HALF, z_main], **main},
        {'pos': [round(cgx - S_AXLE, 3), TRACK_HALF, z_main], **main},
        # The tail SKID: no brake, no steering, and it scrubs and drags in grass.
        {'pos': [round(cgx - S_SKID, 3), 0.0, z_skid], 'springN': 30000, 'dampNs': 4500, 'brake': False,
         'isTailwheel': True, 'isSteerable': False, 'maxSteerRad': 0.0, 'tireMu': 0.9,
         'corneringStiffnessN': 1200, 'rollResistN': 120, 'steerSpringNmPerRad': 0},
    ]
    json.dump(c, open(path, 'w'), indent=2)

    # ---- the checks the protocol asks for, printed every run --------------------------------------------------
    up = sum(s['area'] for s in uf) + sum(s['area'] for s in ua)
    lo = sum(s['area'] for s in lf)
    wing_ac = (UP_AREA * (S_UP_LE + CHORD / 4) + LO_AREA * (S_LO_LE + CHORD / 4)) / WING_AREA
    sh = STAB_AREA + ELEV_AREA
    lh = (STAB_AREA * (S_STAB_LE + STAB_CHORD / 4) + ELEV_AREA * (S_ELEV_LE + ELEV_CHORD / 4)) / sh - wing_ac
    sv, lv = FIN_AREA + RUD_AREA, (S_RUD_LE + RUD_CHORD / 4) - wing_ac
    rx, ry, rz = B.radii_of_gyration(mass, inertia, UP_SPAN, LENGTH)
    empty_cg, empty_lb = cg_of(ITEMS[:EMPTY])
    # Solo is flown from the REAR seat on a JN-4D, which is the real test of the layout.
    solo_cg, solo_lb = cg_of(ITEMS[:EMPTY] + [ITEMS[-4], ITEMS[-3], ITEMS[-1]])
    pct = lambda s: 100 * (s - S_LO_LE) / CHORD
    print(f"  wing  upper {up:.3f} m2 (book {UP_AREA:.3f})  lower {lo:.3f} ({LO_AREA:.3f})  "
          f"total {up + lo:.3f} ({WING_AREA:.3f})")
    print(f"  ail   outer {100 * (1 - AIL_FRAC):.0f} % of the half-span, {100 * AIL_CHORD_FRAC:.0f} % of the chord"
          f"  -> {AIL_CHORD_FRAC * CHORD * UP_HALF * (1 - AIL_FRAC):.3f} m2 each (book {AIL_AREA:.3f})")
    print(f"  CG    empty {empty_cg:.3f} m = {pct(empty_cg):.0f} % chord at {empty_lb:.0f} lb |  "
          f"gross {cgx:.3f} = {pct(cgx):.0f} % at {sum(i['lb'] for i in ITEMS):.0f} lb |  "
          f"REAR-SEAT SOLO {solo_cg:.3f} = {pct(solo_cg):.0f} % at {solo_lb:.0f} lb")
    print(f"        {1000 * (cgx - wing_ac):.0f} mm aft of the combined wing AC ({wing_ac:.3f} m)")
    print(f"  tails arm {lh:.2f} m   V_h {sh * lh / (WING_AREA * CHORD):.3f} (band 0.5-0.7)   "
          f"V_v {sv * lv / (WING_AREA * UP_SPAN):.4f} (band 0.03-0.05: the 3.8 sq ft fin)")
    print(f"  gyr   Rx {rx:.3f} (0.25-0.30)  Ry {ry:.3f} (0.35-0.40)  Rz {rz:.3f} (0.38-0.45)")
    print(f"  grnd  three-point {math.degrees(math.atan2(Y_SKID - (Y_AXLE - WHEEL_R), S_SKID - S_AXLE)):.1f} deg nose-up,"
          f"  CG {math.degrees(math.atan2(cgx - S_AXLE, z_main)):.1f} deg behind the main wheels")


print("Building the Jenny:")
build()
