# Pitts S-2B — `pitts-s2b-like` — mass, CG and inertia build-up

Lane `lane/claude-inertia`, 2026-11-01. Ty: *"The Pitts and Great Lakes seem to have too much pitch inertia ... The
Extra is pretty good."* First principles, no fudge factors; published inertias and rules of thumb are validation only.
The aerodynamic half of the same report (the tail "letting go") is `tail-poststall-DERIVATION.md`.

Generated, not hand-edited:

```
/usr/bin/python3 tools/aircraft_builder/inertia_glb_measure.py   # GLB stations and fuselage rings -> data/inertia_glb.json
python3 tools/aircraft_builder/build_inertia.py                  # prints every number on this page
python3 tools/aircraft_builder/build_inertia.py --write          # mass.inertia into the config
python3 tools/aircraft_builder/build_inertia.py --check          # what InertiaBuildUpTests runs
```

## Result

| | Ixx | Iyy | Izz | Ixz | kx / ky / kz (m) | R̄x / R̄y / R̄z |
|---|---|---|---|---|---|---|
| config before (fa1fa15) | 660 | 1121 | 1341 | 50 | 0.946 / 1.233 / 1.349 | 0.311 / 0.475 / 0.479 |
| **build-up (now)** | **401** | **1048** | **1272** | **12** | 0.737 / 1.193 / 1.314 | 0.242 / 0.460 / 0.466 |
| change | −39 % | −7 % | −5 % | | | |

kg·m², about the CG, body axes +x forward, +y right, +z down (`RigidBody6DOF`: the tensor carries −Ixz off the
diagonal, so mass forward-and-below the CG adds +Ixz). R̄x = 2kx/b, R̄y = 2ky/L, R̄z = 2kz/((b+L)/2) with b 6.08 m
(span) and L 5.19 m (spinner to tail post), the Grob 103 lane's definitions.

**The pitch inertia was about right.** The rough check that started this lane (Pitts ky 1.23 m > the Extra's 1.16 m,
"so the Pitts must be 1.5–1.8× high") was right that the two were inconsistent and wrong about which one: the build-up
puts the Pitts at 1.19 m and the EXTRA at 1.58 m (`extra-300-inertia-DERIVATION.md`). The light-aircraft rule of thumb
ky ≈ 0.16 L (≈ 600 kg·m² here) is for aeroplanes whose payload sits near the CG; the S-2B carries a 174 kg engine and
a 36 kg propeller 1.1–1.6 m ahead of it and both occupants behind it, on a 5.2 m airframe. The ROLL inertia was
high: 660 → 401.

## Method

As the Grob 103 (`git show lane/claude-grob103:docs/aircraft/grob103-acro-DERIVATION.md` §4): every component a point
or distributed mass with its own centroidal inertia, summed with parallel-axis terms about the composite CG.

1. **Published items** at their published mass and measured station (table below).
2. **Unpublished structure groups** — wings, tails, fuselage, gear, "all else" — take their RELATIVE sizes from
   Raymer's Table 15.2 (*Aircraft Design: A Conceptual Approach*, general aviation single: wing 2.5 lb/ft² of planform,
   tails 2.0 lb/ft², fuselage 1.4 lb/ft² wetted with S_wet = 3.4 (S_top + S_side)/2 (eq. 7.13), gear 0.057 W_TO,
   installed engine 1.4 × dry, all else 0.1 W_TO) and are scaled TOGETHER by one factor so the empty aeroplane weighs
   its published empty weight. The factor is fixed by the empty weight alone: **k = 0.696** (wood-and-fabric
   wings and a steel-tube fuselage are lighter per square foot than Raymer's metal norm).
3. **Distributed over the real geometry**: the wings over the config's own 40 strips (span, chord, stagger, gap,
   dihedral, both wings and all four ailerons), each strip's mass at 40 % of its chord with its chordwise and spanwise
   extent as its own inertia; the tails over their strips; the fuselage over its 23 skin rings measured off the GLB
   (`Fuselage_Painted`, firewall to tail post, perimeter of the elliptical section at each ring).
4. **CG check, and the one solved distribution.** With mass per metre following perimeter only, the loaded CG falls
   at x +0.006, 1.0 in aft of the certificated aft limit — so the positions are wrong (protocol 1c), not the
   certificate. A steel-tube fuselage carries heavier tube forward (engine mount, gear and cabane fittings, both
   cockpits) than aft (tail only): the fore-aft density ramp is the least-known distribution in the build-up, and it is
   SOLVED, as `build_grob103.py` solves its shell slope against the published empty CG, for the CG the config already
   flies (the A8SO mid-range, 2026-10-28): **forward bays 4.3 × the aft bays per metre of perimeter**, under the 6 ×
   cap (≈ 1 in × 0.049 in tube at the mount to 5/8 in × 0.028 in at the post, ~3.5 × per tube, with more members per
   bay forward).

## Frame and stations

The config origin is the lower-wing leading edge and the own GLB shares it (glTF +X aft, +Y up, +Z left; config x =
−X, y = −Z, z = −Y; `docs/CONTROL-SURFACES.md`). FAA TCDS A8SO Rev 21, Model S-2B: datum 91.81 in forward of the lower
wing LE, so config x = 2.332 − 0.0254 × arm(in). Measured off `pitts-s2b-like-own.glb`: engine x +0.574 to +1.600
(1.026 m, 0.84 m wide — the AEIO-540's 33.4 in), prop disc x +1.600, firewall (engine aft face) +0.574, fuselage
rings x +1.50 to −3.29.

## Components

| component | kg | x | \|y\| | z | source / arithmetic |
|---|---|---|---|---|---|
| engine, Lycoming AEIO-540-D4A5, dry | 174.2 | +1.169 | 0 | +0.170 | FAA TCDS 1E4 Rev 25 Note 5: **384 lb**, "* Less starter and alternator". CG 42 % of its length aft of the front face (ASSUMED; ±0.05 m); own inertia as a 1.03 × 0.60 × 0.84 m box |
| starter | 7.7 | +1.500 | 0 | +0.320 | ASSUMED lightweight starter (~17 lb), front-bottom of the case |
| alternator | 5.0 | +1.500 | 0 | +0.020 | ASSUMED 60 A (~11 lb), front-top |
| oil | 10.0 | +1.169 | 0 | +0.390 | ASSUMED 12 US qt × 0.88 kg/L in the sump (full oil is part of empty weight) |
| propeller + spinner | 36.3 | +1.600 | 0 | 0 | **80 lb** Hartzell, owner data (commit abcd9d7, 2026-09-07). Own polar moment 8.0 kg·m² = the config's `propInertia` 9.0 less ~1.0 for the 540's crank; transverse 4.0 (a 2-blade prop's mean) |
| engine installation (mount, exhaust, cowl, baffles, hoses) | 20.7 | +0.905 | 0 | +0.270 | Raymer: installed engine = 1.4 × 174.2 = 243.9 kg, less dry, prop, starter and alternator; half at the cowl's centre, half at the firewall foot |
| battery | 10.9 | +0.474 | 0 | +0.170 | ASSUMED 25 Ah sealed, aft face of the firewall |
| wings (both, with 4 ailerons) | 80.5 | −0.150 | 1.46 | −0.094 | Raymer 2.5 lb/ft² × 11.60 m² = 141.6 × k = 98.5 kg, less 18.0 of bracing; spread over the 40 strips ∝ area. Upper 52 %, lower 48 % |
| cabane struts | 5.0 | +0.273 | 0 | −0.299 | ASSUMED, at the GLB `Strut_Cabane` box |
| interplane I-struts | 7.0 | −0.167 | 2.30 | −0.081 | ASSUMED 3.5 kg each, at the `Strut_Interplane` box ends |
| flying + landing wires | 6.0 | −0.167 | 1.15 | −0.077 | ASSUMED; centroid half-way out to the struts, mid-gap |
| horizontal tail (stab + elevator) | 12.8 | −3.147 | 0.52 | −0.071 | Raymer 2.0 lb/ft² × 1.88 m² × k, over the 16 tail strips |
| vertical tail (fin + rudder) | 5.9 | −3.243 | 0 | −0.397 | Raymer 2.0 lb/ft² × 0.87 m² × k, over the 8 strips |
| fuselage structure | 63.0 | −0.867 | 0 | +0.208 | Raymer 1.4 lb/ft² × S_wet 13.24 m² (3.4 × (3.35 + 4.44)/2, the config's measured plan and side areas) × k; over the rings, ramp 4.3 : 1 |
| main gear (legs, wheels, brakes, pants) | 26.1 | +0.463 | 0.67 | +1.048 | Raymer 0.057 × 771 kg × k less the tailwheel; 65 % at the axles (config contacts less 0.20 m tyre radius), 35 % mid-leg |
| tailwheel | 4.5 | −3.599 | 0 | +0.505 | ASSUMED, at the config contact |
| all else (instruments, seats, sticks, controls, wiring, upholstery, smoke system) | 46.1 | −0.802 | 0 | +0.189 | Raymer 0.1 × 771 × k less the battery; half at the two seats, half along the shell |
| **empty** | **521.6** | +0.284 | | +0.157 | **1150 lb**, Aviat/GlobalAir S-2B specification |
| rear pilot (PIC) | 86.0 | −1.135 | 0 | +0.150 | A8SO seat +136.50 in; z from the seated-pilot model (docs/pending/loading-arm-estimates.md); own inertia of a 0.45 × 0.60 × 0.90 m seated body |
| front occupant | 86.0 | −0.339 | 0 | +0.190 | A8SO seat +105.15 in |
| fuel, main tank | 43.4 | +0.267 | 0 | −0.060 | A8SO 24 gal at +81.32 in; the remainder of 737 kg (60 L ≈ 15.9 gal) |
| **as flown** | **737.0** | **+0.045** | | +0.147 | |

### What the authored 737 kg represents

737 kg = 1625 lb, the A8SO **acrobatic** maximum. A solo S-2B with full fuel is 1150 + 190 + 174 = 1514 lb, 111 lb
short of it; the loading that reaches 1625 lb is two 86 kg occupants and 15.9 gal in the main tank. That is what the
build-up assumes. The config's `loading` block is NOT changed: its reference loading still carries one 86 kg pilot and
no fuel (the 172's convention — "whether each airframe's authored massKg includes a pilot or fuel is still the open
W&B decision"), and because `AircraftLoad` solves the empty airframe backwards from whatever is authored, the
reference loading reproduces the new inertia exactly (`InertiaBuildUpTests.TheLoadSheetStillFliesTheAuthoredAeroplane`)
and the in-game load sheet flies it. The empty inertia that path derives (authored less one point-mass pilot) is a
"651 kg empty" that is really empty + front occupant + fuel — consistent with the reference loading, not with a
weighed S-2B (522 kg). NEEDS YOU, below.

## CG check

| | config x | A8SO arm |
|---|---|---|
| A8SO acrobatic range at 1625 lb | +0.0566 (fwd) … +0.0333 (aft) | +89.58 … +90.50 in |
| build-up, perimeter-only shell | +0.0056 | +91.59 in — 1.1 in AFT of the limit |
| **build-up, solved ramp 4.3 : 1** | **+0.0450** | **+90.04 in — mid-range** |
| authored `mass.cg` | +0.0450 | +90.04 in |
| vertical | build-up +0.147 below the datum | authored +0.200 (derived for ground handling, not certified) |

The build-up's vertical CG (0.147 m below the lower-wing LE line) is 53 mm above the authored 0.200; the authored value
was chosen for ground handling (`docs/CONTROL-SURFACES.md`) and is not moved here (inertia about 0.147 vs 0.200 differs
by m·dz² = 2 kg·m²). Worth Ty's eye with the gear lane.

## Contributions (as flown)

| group | kg | Ixx | % | Iyy | % | Izz | % | Ixz |
|---|---|---|---|---|---|---|---|---|
| engine (dry) | 174.2 | 16 | 4 | **241** | **23** | 246 | 19 | 4.5 |
| occupants | 172.0 | 15 | 4 | **149** | **14** | 140 | 11 | −1.8 |
| horizontal tail | 12.8 | 5 | 1 | **132** | **13** | 136 | 11 | 8.9 |
| fuselage structure | 63.0 | 11 | 3 | 112 | 11 | 110 | 9 | −0.8 |
| propeller + spinner | 36.3 | 9 | 2 | 93 | 9 | 92 | 7 | −8.3 |
| landing gear | 30.6 | 35 | 9 | 86 | 8 | 77 | 6 | 4.0 |
| vertical tail | 5.9 | 2 | 1 | 66 | 6 | 64 | 5 | 10.5 |
| all else | 46.1 | 0 | 0 | 57 | 5 | 56 | 4 | −0.5 |
| wings | 80.5 | **254** | **64** | 45 | 4 | 242 | 19 | −7.9 |
| engine accessories | 12.7 | 0 | 0 | 27 | 3 | 27 | 2 | 1.0 |
| engine installation | 20.7 | 1 | 0 | 17 | 2 | 16 | 1 | 1.8 |
| oil | 10.0 | 1 | 0 | 13 | 1 | 13 | 1 | 2.7 |
| bracing | 18.0 | 51 | 13 | 4 | 0 | 49 | 4 | 0.1 |
| fuel | 43.4 | 2 | 0 | 4 | 0 | 2 | 0 | −2.0 |
| battery | 10.9 | 0 | 0 | 2 | 0 | 2 | 0 | 0.1 |
| **total** | **737.0** | **401** | | **1048** | | **1272** | | **12.4** |

Top three in pitch: the engine (23 %), the occupants (14 %) and the horizontal tail (13 %). Sanity relation:
Ixx + Iyy − Izz = 177 kg·m² (14 % of Izz) = 2Σm·dz² + own terms — the two wings 1.1 m apart and the gear 0.9 m below.

**Sensitivity** (the two least-known distributions): shell uniform per metre instead of per perimeter, Iyy 1069
(+2 %); tails 25 % heavier, Iyy 1078 (+3 %) and the CG 11 mm aft. The engine's CG fraction ±0.05 m is ±12 kg·m².

## Why the old numbers were what they were

There was never a build-up. `git log -S` finds the values in hand-arithmetic commit messages, 2026-09-06/07:
30968be put in 660 / 1147 / 1450 / 40 at 700 kg with no derivation; abcd9d7 added an 80-lb prop at +2.1 m and a
rear-seat pilot (Iyy → 1262); 4f2c757 re-placed both from TCDS arms ("prop +1.68 m / engine +1.09 m", pilot 1.18 m aft
of a CG at −0.03) and took the mass to 737 kg (Iyy 1121, Izz 1341). **Ixx 660 was chosen behaviourally**: the
"accelerated-spin litmus" in docs/CALIBRATION-2-33.md swept Ixx 290 / 430 / 660 and kept the only value that made the
spin accelerate, then argued a structural 536 + 127 for ailerons up to it — a tuned number with a structural story.
Then on 2026-10-28 the CG moved 75 mm forward to the A8SO mid-range (−0.030 → +0.045) and nobody recomputed the
inertia about it. So the old Iyy was roughly right by construction (the engine, prop and pilot arms were measured
ones) and slightly high because its arms were taken from a CG 75 mm aft; the old Ixx was 65 % high because it was
tuned to a spin, not built from a wing.

## Validation (not inputs)

- Roskam Part V bands quoted in docs/NEW-AIRPLANE-PROTOCOL.md: R̄x 0.25–0.30, R̄y 0.35–0.40, R̄z 0.38–0.45 for light
  aircraft. The S-2B: R̄x 0.24 (a short, stubby span with the ailerons' and struts' mass inboard of the tips), R̄y
  0.46 and R̄z 0.47 — above the band, for the reasons in the Result. No measured S-2B inertia was found (no swing test
  is published; NASA's Langley spin-tunnel biplane models are not the S-2B).
- Flown (`tail-poststall-DERIVATION.md` §6, `InertiaTailCaseProbe`): the inertia change alone leaves the spin and the
  let-go as they were (3.93 → 4.34 s a turn; full-aft peak α 49° → 50°); the steady roll rate is unchanged
  (392 → 388 °/s at 160 mph, aileron power over damping) and it gets there faster (0.41 → 0.27 s to 90 %), which is
  the lower Ixx.

## Gaps

- Empty weight 1150 lb is the type figure; a particular S-2B's weighing record is the real input.
- ASSUMED: engine CG fraction, starter, alternator, oil quantity, battery mass and station, bracing masses,
  tailwheel; prop mass is owner data, not a Hartzell figure.
- The fuselage ramp is SOLVED for the authored CG, not measured.
- Raymer's group RATIOS are a metal-monoplane norm applied to a wood-and-fabric biplane; only their sum is pinned.
- The vertical CG (0.147 build-up vs 0.200 authored) is not reconciled.
