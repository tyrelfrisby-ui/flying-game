# Great Lakes 2T-1A-2 — `great-lakes-like` — mass, CG and inertia build-up

Lane `lane/claude-inertia`, 2026-11-01. Same method, script and commands as `pitts-s2b-inertia-DERIVATION.md` (read
that page for the method); this page is the Great Lakes' numbers. Aerodynamics: `tail-poststall-DERIVATION.md`.

## Result

| | Ixx | Iyy | Izz | Ixz | kx / ky / kz (m) | R̄x / R̄y / R̄z |
|---|---|---|---|---|---|---|
| config before (fa1fa15) | 1220 | 1260 | 2100 | 50 | 1.223 / 1.243 / 1.604 | 0.302 / 0.438 / 0.466 |
| **build-up (now)** | **1147** | **1310** | **2045** | **−8** | 1.186 / 1.267 / 1.583 | 0.293 / 0.447 / 0.460 |
| change | −6 % | +4 % | −3 % | | | |

b 8.11 m, L 5.67 m (spinner to tail post). **The pitch inertia was about right** (+4 %); so was the rest.

## Frame and stations

The own GLB (`great-lakes-like-own-white-stripes.glb`) shares the config origin: config x = −X, z = −Y — checked on
the tailwheel (config −4.83, GLB centre 4.83) and the stab height (+0.140 both). FAA TCDS A18EA: datum "centre of most
forward lateral fuselage tube", located at config x −0.200 (MAC route; firewall route −0.175 — docs/WEIGHT-AND-BALANCE.md),
so config x = −0.200 − 0.0254 × arm(in). Engine x −0.175 to +0.700 (0.875 m), prop disc +0.750, firewall −0.175,
fuselage rings +0.69 to −4.57 (42).

## Components

| component | kg | x | \|y\| | z | source / arithmetic |
|---|---|---|---|---|---|
| engine, Lycoming IO-360-B1F6, dry | 126.0 | +0.332 | 0 | +0.280 | EASA TCDS IM.E.032 Issue 03 §5: **126 kg / 277 lb** (the AEIO-360-B1G6 is also 126 kg); CG 42 % of length (ASSUMED) |
| starter / alternator | 7.7 / 5.0 | +0.600 | 0 | +0.540 / +0.020 | ASSUMED, as the Pitts |
| oil | 6.7 | +0.332 | 0 | +0.610 | ASSUMED 8 US qt |
| propeller + spinner, Hartzell HC-C2YK-4F 74 in | 24.5 | +0.750 | 0 | 0 | ASSUMED ~50 lb with spinner (prop model from A18EA); polar 4.3 = config `propInertia` 5.0 less 0.7 |
| engine installation | 13.2 | +0.119 | 0 | +0.435 | Raymer 1.4 × 126 = 176.4 less dry, prop, starter, alternator |
| battery | 10.9 | −0.275 | 0 | +0.280 | ASSUMED, aft face of the firewall |
| wings (both, with the lower ailerons) | 144.9 | −0.878 | 2.06 | −0.046 | Raymer 2.5 lb/ft² × 17.43 m² × k (0.784) = 166.9, less 22 of bracing, over the config strips |
| cabane / I-struts / wires | 6.0 / 8.0 / 8.0 | −0.460 / −0.925 / −0.925 | 0 / 2.89 / 1.44 | −0.396 / −0.015 / −0.021 | ASSUMED, at the GLB strut boxes |
| horizontal tail (strut-braced) | 19.9 | −4.482 | 0.70 | +0.140 | Raymer 2.0 lb/ft² × 2.60 m² × k |
| vertical tail | 8.4 | −4.493 | 0 | −0.320 | Raymer 2.0 lb/ft² × 1.10 m² × k |
| fuselage structure | 76.8 | −1.783 | 0 | +0.303 | Raymer 1.4 lb/ft² × S_wet 14.33 m² × k, over 42 rings, ramp **6.0 : 1 (at the cap)** |
| main gear (oleo, wheels, pants) | 31.5 | −0.479 | 0.89 | +1.201 | Raymer 0.057 × 816 × k less the tailwheel |
| tailwheel | 5.0 | −4.830 | 0 | +0.830 | ASSUMED |
| all else | 55.5 | −1.556 | 0 | +0.173 | Raymer 0.1 × 816 × k less the battery |
| **empty** | **557.9** | −0.834 | | +0.212 | **1230 lb**, Plane & Pilot type specification (a 2024 Waco-built one is 1290 lb) |
| rear pilot (PIC) / front occupant | 86.0 / 86.0 | −1.762 / −0.898 | 0 | +0.001 / +0.085 | A18EA seats 61.5 / 27.5 in |
| fuel, top-wing tank (full) / header (full) | 70.9 / 3.8 | −0.530 / −0.327 | 0 | −0.690 / −0.215 | A18EA 26.0 gal at +13.0 / 1.4 gal at +5.0 |
| baggage | 11.4 | −2.537 | 0 | +0.100 | A18EA aft compartment +92.0 in; the remainder of 816 kg, inside its 40 lb placard |
| **as flown** | **816.0** | **−0.934** | | +0.094 | |

### What the authored 816 kg represents

816 kg = 1800 lb, the A18EA maximum. Solo with full fuel is 1230 + 190 + 164 = 1584 lb; 1800 lb needs two occupants.
The build-up flies two 86 kg occupants, full fuel and 11.4 kg of baggage. The config's `loading` block is unchanged
(one 86 kg reference pilot, no fuel; the open W&B decision), and the reference loading reproduces the new inertia
exactly.

## CG check — the build-up does NOT close

| | config x | A18EA arm |
|---|---|---|
| A18EA range at 1800 lb | −0.708 (fwd) … −0.8045 (aft) | +20.0 … +23.8 in |
| build-up, perimeter-only shell | −0.985 | +30.9 in |
| **build-up, ramp at the 6 : 1 cap** | **−0.934** | **+28.9 in — 5.1 in AFT of the aft limit** |
| authored `mass.cg` | −0.756 | +21.9 in (mid-range) |

For two occupants at 27.5 and 61.5 in, full fuel at 13 in and the baggage at 92 in to balance at mid-range, the EMPTY
aeroplane must balance about 14.7 in (0.37 m) aft of the datum; the build-up's empty CG is 0.63 m aft of it
(+24.9 in). No fore-aft redistribution of the unpublished structure inside a believable range closes 0.26 m on a
558 kg airframe (it would need the fuselage's centroid ahead of the firewall). Something PUBLISHED-looking is off
instead — candidates, none verifiable here: the 1230 lb empty weight (newer GLs weigh 1290–1314 lb, with the extra
mass forward: constant-speed prop, inverted oil, starter), the battery's real station, the wing group's split, or the
datum's location relative to the model (two routes agree to 25 mm, so unlikely). **Reported, not fixed: GAP and
question for Ty.** The inertia is taken about the build-up's own CG; its sensitivity to the 0.18 m miss is small
(moving 816 kg of mass 0.18 m is m·d² ≈ 26 kg·m² on Iyy, 2 %).

## Contributions (as flown)

| group | kg | Ixx | % | Iyy | % | Izz | % | Ixz |
|---|---|---|---|---|---|---|---|---|
| horizontal tail | 19.9 | 13 | 1 | **252** | **19** | 265 | 13 | −3.2 |
| engine (dry) | 126.0 | 18 | 2 | **222** | **17** | 217 | 11 | 29.7 |
| fuselage structure | 76.8 | 17 | 2 | **141** | **11** | 135 | 7 | −12.1 |
| landing gear | 36.5 | 69 | 6 | 124 | 9 | 109 | 5 | 1.5 |
| vertical tail | 8.4 | 2 | 0 | 109 | 8 | 107 | 5 | 12.4 |
| wings | 144.9 | **863** | **75** | 98 | 7 | 822 | 40 | −25.5 |
| occupants | 172.0 | 15 | 1 | 77 | 6 | 67 | 3 | 6.6 |
| propeller + spinner | 24.5 | 5 | 0 | 72 | 5 | 72 | 4 | −3.9 |
| all else | 55.5 | 1 | 0 | 58 | 4 | 56 | 3 | −3.3 |
| fuel | 74.7 | 44 | 4 | 57 | 4 | 13 | 1 | −23.1 |
| engine accessories | 12.7 | 2 | 0 | 31 | 2 | 30 | 1 | 4.7 |
| baggage | 11.4 | 0 | 0 | 29 | 2 | 29 | 1 | −0.1 |
| engine installation | 13.2 | 2 | 0 | 17 | 1 | 15 | 1 | 4.4 |
| oil | 6.7 | 2 | 0 | 12 | 1 | 11 | 1 | 4.4 |
| bracing | 22.0 | 94 | 8 | 6 | 0 | 92 | 5 | −1.4 |
| battery | 10.9 | 0 | 0 | 5 | 0 | 5 | 0 | 1.3 |
| **total** | **816.0** | **1147** | | **1310** | | **2045** | | **−7.7** |

Top three in pitch: horizontal tail (19 %), engine (17 %), fuselage (11 %). Ixx + Iyy − Izz = 412 (20 % of Izz):
the widest gap in the fleet (1.36 m), the top-wing tank 0.69 m above the datum line and the gear 1.2 m below.
Sensitivity: shell uniform per metre, Iyy 1370 (+4.5 %); tails 25 % heavier, Iyy 1384 (+5.7 %).

## Why the old numbers were what they were

docs/CONTROL-SURFACES.md (2026-10-28): "The inertias are estimates, from radii of gyration scaled off the Pitts and
the Extra. There is no mass breakdown for the type (new-airplane protocol step 1c). This is the only part of the config
with no source." Scaling from two references, one of which (the Pitts) was roughly right and one of which (the Extra)
was half its real pitch inertia, landed within 6 % of the build-up on every axis — by luck rather than construction.

## Validation

R̄x 0.29 sits in the protocol's Roskam band (0.25–0.30); R̄y 0.45 and R̄z 0.46 above it (tail-heavy short biplane, see
the Pitts page). No measured Great Lakes inertia was found. Flown: inertia alone leaves the spin unchanged
(3.05 → 3.09 s, 290 → 295 ft a turn) and the let-go about the same (43° → 37° peak α, within a chaotic hold's
scatter); the PARE recovery lengthens from 0.5 to 1.6 turns. Roll unchanged (83 °/s; the wing dominates Ixx).

## Gaps

- **The CG does not close** (above) — the biggest open item on this page.
- Empty weight 1230 lb is a type figure; the IO-360 vs AEIO-360 installation and its accessories are ASSUMED.
- ASSUMED: prop mass, starter, alternator, oil, battery, bracing, tailwheel, engine CG fraction.
- Raymer's 2.5 lb/ft² wing ratio gives the wings 20 % of W_TO, high for a braced biplane (bracing relieves bending);
  a lighter wing group would move mass aft and worsen the CG miss, so it is not the explanation.
