# Extra 300L — `extra-300-like` — mass, CG and inertia build-up (the control)

Lane `lane/claude-inertia`, 2026-11-01. Ty: *"The Extra is pretty good."* It was built up by the same method as the
Pitts and the Great Lakes (`pitts-s2b-inertia-DERIVATION.md`), as the control. **It is the one whose pitch inertia was
wrong — by half.** Aerodynamics: `tail-poststall-DERIVATION.md`.

## Which Extra

`build_batch1.py` (f1acf66, 2026-09-07) built "Extra 300S (single-seat, symmetric wing, no flaps, ±10g)". The config
has since become a two-seat **300L**: two seats at the 300L POH arms (rear PIC 207 cm, front 98 cm aft of the firewall),
the 300L's centre and wing tanks, span 8.00 m and wing area 10.84 m² (the 300L's, NZ CAA TAR 8/21B/31), and on
2026-10-28 its tail arm was moved 1.10 m aft to the model's. So the build-up is the **300L with an AEIO-540-L1B5 and an
MT MTV-9 three-blade** — which is what the config now is.

## Result

| | Ixx | Iyy | Izz | Ixz | kx / ky / kz (m) | R̄x / R̄y / R̄z |
|---|---|---|---|---|---|---|
| config before (fa1fa15) | 800 | 1100 | 1800 | 30 | 0.988 / 1.158 / 1.482 | 0.247 / 0.336 / 0.398 |
| **build-up (now)** | **701** | **2038** | **2575** | **80** | 0.924 / 1.576 / 1.772 | 0.231 / 0.457 / 0.476 |
| change | −12 % | **+85 %** | +43 % | | | |

b 8.00 m, L 6.89 m (spinner to tail post).

## Frame and stations

The own GLB (`extra-300-like-own.glb`) shares the config origin (config x = −X, z = −Y; stab height −0.186 both).
EASA TCDS A.362 datum: the plane of the firewall, at config x +0.79 (the engine's aft face; ±0.05 m,
docs/CONTROL-SURFACES.md), so config x = 0.79 − arm(cm)/100. Engine x +0.786 to +1.797 (1.011 m), prop disc +1.921,
fuselage rings +1.80 to −4.58 (137).

## Components

| component | kg | x | \|y\| | z | source / arithmetic |
|---|---|---|---|---|---|
| engine, Lycoming AEIO-540-L1B5, dry | 203.7 | +1.372 | 0 | +0.047 | FAA TCDS 1E4 Rev 25 Note 5: **449 lb**, less starter and alternator; CG 42 % (ASSUMED) |
| starter / alternator | 7.7 / 5.0 | +1.697 | 0 | +0.113 / −0.020 | ASSUMED |
| oil | 10.0 | +1.372 | 0 | +0.183 | ASSUMED 12 US qt |
| propeller + spinner, MT MTV-9-B-C/C200-15 | 29.6 | +1.921 | 0 | 0 | **62 lb** (Hartzell's Talon page, the MT it replaces) + 1.5 kg spinner (ASSUMED); polar 1.8 = config `propInertia` 2.5 less the 0.7 its own comment gives the crank |
| engine installation | 39.1 | +1.114 | 0 | +0.105 | Raymer 1.4 × 203.7 = 285.2 less dry, prop, starter, alternator |
| battery | 10.9 | +0.686 | 0 | +0.047 | ASSUMED, aft face of the firewall |
| wing (with ailerons) | 111.1 | −0.211 | 1.86 | −0.054 | Raymer 2.5 lb/ft² × 10.84 m² × k (0.840), over the config strips |
| horizontal tail | 20.0 | −4.106 | 0.74 | −0.186 | Raymer 2.0 lb/ft² × 2.43 m² × k |
| vertical tail | 8.9 | −4.547 | 0 | −0.570 | Raymer 2.0 lb/ft² × 1.09 m² × k |
| fuselage structure | 104.6 | −1.273 | 0 | +0.084 | Raymer 1.4 lb/ft² × S_wet 18.23 m² × k, over 137 rings, ramp 1.8 : 1 |
| main gear / tailwheel | 41.5 / 4.0 | +0.347 / −5.032 | 0.73 / 0 | +1.114 / +0.431 | Raymer 0.057 × 950 × k; tailwheel ASSUMED |
| all else | 70.6 | −1.004 | 0 | +0.009 | Raymer 0.1 × 950 × k less the battery |
| **empty** | **666.8** | +0.100 | | +0.089 | **1470 lb**, Extra 300L specification sheet |
| rear pilot (PIC) | 86.0 | −1.280 | 0 | −0.104 | 300L POH §6: 207 cm |
| fuel, acro/centre (full) | 36.7 | +0.492 | 0 | −0.130 | 300L POH moment table: 51 L |
| fuel, wing tanks | 15.3 + 15.3 | +0.289 | 1.15 | −0.070 | the remainder of 820 kg, split evenly |
| **as flown** | **820.0** | **−0.020** | | +0.053 | |

820 kg is the mass A.362 quotes its CG range "at or below"; one pilot plus 67 kg of fuel reaches it from the 300L's
empty weight with no assumption beyond the even wing-tank split. The config's loading block is unchanged.

## CG check

| | config x | A.362 arm |
|---|---|---|
| A.362 range at ≤ 820 kg | +0.040 (fwd) … −0.108 (aft) | 75.0 … 89.8 cm |
| build-up, perimeter-only shell | −0.057 | 84.7 cm — **inside** |
| **build-up, solved ramp 1.8 : 1** | **−0.020** | **81.0 cm** |
| authored `mass.cg` | −0.020 | 81.0 cm |

The Extra's CG closes inside the certificate with no help (the ramp only moves it within the band to the authored
point).

## Contributions (as flown)

| group | kg | Ixx | % | Iyy | % | Izz | % | Ixz |
|---|---|---|---|---|---|---|---|---|
| engine (dry) | 203.7 | 16 | 2 | **415** | **20** | 425 | 17 | −1.8 |
| fuselage structure | 104.6 | 20 | 3 | **380** | **19** | 377 | 15 | 6.8 |
| horizontal tail | 20.0 | 16 | 2 | **336** | **16** | 349 | 14 | 19.5 |
| vertical tail | 8.9 | 4 | 1 | 188 | 9 | 184 | 7 | 25.1 |
| landing gear | 45.5 | 72 | 10 | 154 | 8 | 130 | 5 | 8.6 |
| all else | 70.6 | 1 | 0 | 154 | 8 | 153 | 6 | 6.0 |
| occupant | 86.0 | 9 | 1 | 147 | 7 | 141 | 5 | 17.0 |
| propeller + spinner | 29.6 | 2 | 0 | 113 | 6 | 112 | 4 | −3.0 |
| engine installation | 39.1 | 0 | 0 | 52 | 3 | 52 | 2 | 1.9 |
| engine accessories | 12.7 | 0 | 0 | 37 | 2 | 37 | 1 | 0.2 |
| wing | 111.1 | **517** | **74** | 23 | 1 | 538 | 21 | 2.2 |
| oil | 10.0 | 0 | 0 | 20 | 1 | 19 | 1 | 1.8 |
| fuel | 67.2 | 42 | 6 | 14 | 1 | 53 | 2 | −4.6 |
| battery | 10.9 | 0 | 0 | 5 | 0 | 5 | 0 | 0.0 |
| **total** | **820.0** | **701** | | **2038** | | **2575** | | **79.7** |

Top three in pitch: engine (20 %), fuselage (19 %), horizontal tail (16 %). Ixx + Iyy − Izz = 164 (6 % of Izz: a mid
wing, little vertical spread). Sensitivity: shell uniform per metre, Iyy 2043 (+0.3 %); tails 25 % heavier, 2066
(+1.4 %). Even with both tail groups halved, Iyy is still about 1780 — the old 1100 is not inside any reasonable
version of this airframe.

## Why the old number was half

`build_batch1.py` set Ixx 800 / Iyy 1100 / Izz 1800 / Ixz 30 "from agent research" (f1acf66) for a **300S**: a
single-seater, with the config's tail arm at the time 1.10 m SHORT of the real aeroplane's (docs/CONTROL-SURFACES.md:
"Extra tail arm, stab q/c at −2.88 m, model −3.98 m"). When the surfaces were rebuilt from the model on 2026-10-28 the
tail moved 1.1 m aft and the span grew 7 %, and the inertia was not touched. The tail group alone (29 kg) is worth
29 × (4.2² − 3.1²) ≈ 230 kg·m² for that move; the longer fuselage between and the two-seat cockpit
are the rest. The old Iyy/m gives ky 1.16 m on a 6.9 m aeroplane — R̄y 0.34, the bottom of Roskam's
band, where an aerobatic aeroplane with a 204 kg engine on its nose and its pilot 1.3 m aft does not sit.

## What it means that the Extra "felt right"

Flown (`tail-poststall-DERIVATION.md` §6): the build-up inertia alone HALVES the Extra's initial pitch acceleration
from a full-aft step (280 → 166 °/s²), slows its spin (2.79 → 3.48 s, 377 → 588 ft a turn, nose 10° steeper) and
quickens its recovery. With the wake fix as well — what now ships — its spin is 2.31 s and 329 ft a turn, it no longer
lets go (full aft at the stall peaks at α 27° instead of 41°), and pitch response stays at the build-up's 169 °/s².
The Extra "felt right" with half its real pitch inertia and a tail blanked by a wake it was not in; the two errors
partly cancelled in a spin (light in pitch, weak tail). **Ty should fly it** — the pitch response to a sharp pull will
be noticeably slower than the build he liked.

## Validation

R̄x 0.23 (below Roskam's 0.25–0.30: most of the wing's mass is inboard of a tapered planform and the tanks sit at
1.15 m), R̄y 0.46, R̄z 0.48 (above, as the biplanes). No measured Extra 300 inertia was found; a flight-simulator config
for a 300S lists an empty pitch "MOI" of 717 (units unstated) — not a source.

## Gaps

- Empty weight 1470 lb is the specification sheet's; the 300L's AFM empty weight for a given serial is the input.
- ASSUMED: engine CG fraction, starter, alternator, oil, battery station, spinner, tailwheel, even wing-tank split.
- The firewall anchor is ±0.05 m (no firewall node in the model).
- Raymer's group ratios are a metal-monoplane norm; the 300L's carbon wing and steel-tube/composite fuselage are pinned
  only through the empty-weight total.
