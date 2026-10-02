# Flight-model data: Piper PA-28-181 Archer and Cirrus SR22

Research date: 2026-10-02. Values are tagged **[S]** when taken directly from a cited source, and **[E]** when estimated or derived (the method is stated). Speeds are KIAS unless noted.

## 1. Piper PA-28-181 Archer (Archer III / LX / TX, S/N 2843001+)

### Geometry
| Item | Value | Tag / source |
|---|---|---|
| Wingspan | 35.0 ft (10.67 m); some brochures say 35 ft 6 in including tips | [S] POH 3-view [A1] |
| Wing area | 170 ft² (15.8 m²) | [S] POH wing loading 15.0 lb/ft² at 2550 lb [A1]; RotatePilot [A4] |
| Mean chord | ≈ 4.86 ft (1.48 m) = S/b | [E] |
| MAC | ≈ 4.9–5.0 ft (tapered outer panel, constant-chord inner) | [E] |
| Aspect ratio | ≈ 7.2 | [E] b²/S |
| Airfoil | NACA 65₂-415 (Lednicer lists "NACA 65-415" for the PA-28 root and tip) | [S] [A3] |
| Dihedral | ≈ 7° | [E] common published figure for the PA-28 tapered wing, not confirmed in the POH |
| Washout | ≈ 2–3° | [E] |
| Length / height | 24.0 ft / 7.3 ft | [S] [A4] |
| Stabilator span | 12 ft 10.5 in (3.92 m) | [S] POH 3-view [A1] |
| Stabilator area | ≈ 24–25 ft² | [E] span × ~1.9 ft chord; not sourced |
| Tail arm (wing AC to stab AC) | ≈ 15 ft (4.6 m) | [E] from 3-view proportions |
| Seats / baggage | 4 (2 at +80.5 in, 2 at +118.1 in); 200 lb at +142.8 in | [S] TCDS [A2] |

### Control surface travel (TCDS 2A13, Archer III) [A2]
| Surface | Travel |
|---|---|
| Flaps | 0°, 10°, 25°, 40° (±2°); manual Johnson-bar handle, spring-loaded to retract |
| Ailerons | 25° up / 12.5° down (±2°) |
| Rudder | 28° L / 28° R (±1°) |
| Stabilator | 14° up / 2° down (±1°) (TE up = nose up) |
| Stabilator anti-servo/trim tab | 3° up / 12° down (±1°) |
| Nosewheel | 20° L/R (S/N 2843001+), via the pedals |

### Weights and CG [A1, A2]
| Item | Value |
|---|---|
| Max takeoff / landing | 2550 lb (1157 kg) Normal; 2130 lb Utility |
| Max ramp | 2558 lb |
| Empty (typical) | ≈ 1590–1680 lb (sample POH weighing: 1670.2 lb at +88.49 in) |
| CG range (Normal) | +82.0 to +93.0 in at ≤ 2050 lb; +88.6 to +93.0 in at 2550 lb; straight-line between (datum 78.4 in ahead of the wing LE, Piper standard) |
| Fuel | 50 gal total / 48 usable at +95 in |

### Powerplant [A1, A2]
- Lycoming O-360-A4M, 180 hp at 2700 rpm, 361 in³, CR 8.5:1, carbureted.
- Propeller: **Sensenich 76EM8S14-0-62, 2-blade fixed pitch, 76 in (1.93 m) diameter, 62 in pitch**. Static rpm 2240–2340 at sea level.

### Speeds [A1] (POH, 2550 lb)
| Speed | KIAS |
|---|---|
| Vs0 (full flaps 40°) | 45 |
| Vs1 (flaps 0°) | 50 |
| Vr | 55–60 (normal takeoff rotation 60; short field 55) |
| Vx | 64 |
| Vy | 76 |
| Va / Vo | 113 at 2550 lb; 98 at 1917 lb |
| Vfe | 102 (all flap settings) |
| Vno | 125 (121 KCAS) |
| Vne | 154 (148 KCAS) |
| Best glide | 76 (flaps up, 2550 lb); about 10:1 [A4] |
| Final approach (flaps 40) | 66 |
| Cruise | ≈ 122–128 KTAS at 75% power [A4] |
| Service ceiling | 14,085 ft [S] POH performance chart |
| Max demonstrated crosswind | 17 kt |

### Flight-control system (see the controls report)
Cables everywhere. All-moving stabilator with an anti-servo tab (trim wheel between the seats). Rudder trim is a spring in the pedal torque tube, knob on the pedestal. The nosewheel is linked to the pedals.

## 2. Cirrus SR22 (G3–G7 normally aspirated; data for S/N 3915+ unless noted)

### Geometry
| Item | Value | Tag / source |
|---|---|---|
| Wingspan | 38.3 ft (11.67 m) | [S] EASA TCDS [B1] |
| Wing area | 144.9 ft² (13.46 m²) | [S] [B1] |
| Mean chord | ≈ 3.78 ft (1.15 m) | [E] |
| MAC | ≈ 3.9–4.0 ft | [E] tapered wing |
| Aspect ratio | ≈ 10.1 | [E] |
| Airfoil | Roncz proprietary section (Lednicer [A3]). Outboard leading-edge "stall cuff" (drooped LE discontinuity) on the outer wing | [S]/[E] |
| Dihedral | ≈ 4.5–5° | [E] not found in a primary source. Low enough that FAA rudder-only roll control required the rudder-aileron interconnect [B4] |
| Length / height | 26.0 ft / 8.9 ft (7.92 / 2.71 m) | [S] [B1] |
| Horizontal tail area | ≈ 30 ft² (conventional stab and elevator; no trim tab) | [E] not sourced |
| Tail arm | ≈ 15–16 ft | [E] |
| Seats / baggage | 4 (+1 child seat S/N 3828+); 130 lb at 208 in | [S] [B1] |

### Control surface travel
Not found in public primary sources; the TCDS references the AMM. **[E] modeling values:** ailerons ≈ ±12–13°; elevator ≈ 25° up / 15° down; rudder ≈ ±20°.
Flaps: 0% / 50% / 100% = **0° / 16° / 32°** [E] (widely quoted value, not confirmed from the POH). Electric. Vfe 50% = 150 KIAS, 100% = 110 KIAS (S/N 3915+) [B1].

### Weights and CG [B1]
| Item | Value |
|---|---|
| Max takeoff / landing | 3600 lb (1633 kg) for S/N 3915+; 3400 lb (1542 kg) earlier |
| Max zero fuel | 3400 lb (S/N 3915+) |
| Empty (typical) | ≈ 2250–2350 lb (RotatePilot ≈ 2280 lb [B2]) |
| CG range (FS, datum 100 in ahead of firewall) | Forward 137.8 in at 2099 lb → 139.1 in at 2701 lb → 143.2 in at 3600 lb. Aft 148.1 in (constant). (TCDS metric: fwd 3.500 m at 952 kg → 3.533 m at 1225 kg → 3.637 m at 1633 kg; aft 3.762 m) |
| Fuel | 92 gal usable (94.5 total) for S/N 2438+ |

### Powerplant [B1]
- Continental IO-550-N, 310 hp at 2700 rpm (SR22T: TSIO-550-K, 315 hp).
- Propeller: Hartzell 3-blade constant-speed. **PHC-J3YF-1N/N7605: 78 in (1.98 m)**, low pitch 12.2°, high pitch 35°. Composite variants are 76–78 in.

### Speeds (S/N 3915+, 3600 lb unless noted)
| Speed | Value | Source |
|---|---|---|
| Vs0 (100% flaps) | ≈ 60–62 KIAS (60 KCAS at 3400) | [B2], [B3] |
| Vs1 (flaps up) | ≈ 70–73 KIAS | [B2] |
| Vr | ≈ 73–80 | [B2] |
| Vx | ≈ 78–81 | [B2] |
| Vy | ≈ 101–108 | [B2] |
| Vo (operating maneuvering) | 140 KIAS at 3600 lb; 133 at 3400; 124 at 2900; 112 at 2400 | [B1] |
| Vfe | 50%: 150 KIAS; 100%: 110 KIAS (older S/N: 119 / 104) | [B1] |
| Vno | 179 KCAS (older: 180) | [B1] |
| Vne | 208 KCAS (older: 204) | [B1] |
| Vpd (CAPS) | 140 KIAS (older: 133) | [B1] |
| Best glide | ≈ 88 KIAS at max weight (L/D ≈ 9–10:1) | [B2] |
| Cruise | ≈ 183 KTAS at 75%, 8000 ft; 155 KTAS at 55% | [B2] |
| Max operating altitude | 17,500 ft (SR22T 25,000 ft) | [B1] |
| SL rate of climb | ≈ 1270 fpm | [B2] |

### Flight-control system
Side yokes with cables and pushrods, all reversible. **No trim tabs.** Pitch and roll trim are electric motors that move spring-cartridge neutrals. The yaw trim cartridge is a spring. A rudder-aileron interconnect bungee is fitted. See the controls report.

## 3. Moments of inertia (estimates)

No published values were found for either aircraft. The estimates below scale the textbook Cessna 172 values (Nelson, *Flight Stability and Automatic Control*, App. B: W = 2650 lb, b = 36 ft, Ixx = 948, Iyy = 1346, Izz = 1967 slug·ft²) as follows: Ixx ∝ m·b², Iyy ∝ m·L², Izz ∝ m·((b+L)/2)². Gross weight, normal fuel. Expect about ±30%.

| Aircraft (gross) | Ixx | Iyy | Izz |
|---|---|---|---|
| PA-28-181 (2550 lb, b 35, L 24) | ≈ 860 slug·ft² (1170 kg·m²) | ≈ 1020 (1390 kg·m²) | ≈ 1660 (2250 kg·m²) |
| SR22 (3600 lb, b 38.3, L 26) | ≈ 1460 slug·ft² (1980 kg·m²) | ≈ 1690 (2300 kg·m²) | ≈ 2780 (3770 kg·m²) |

Cross-check with Roskam radii of gyration (single-engine prop: R̄x 0.25, R̄y 0.38, R̄z 0.39). That method gives higher values: Archer ≈ 1520 / 1650 / 2620 slug·ft². Treat the two methods as the band. The lower, scaled values better match light-GA flight-test data.

## Sources
- [A1] Piper PA-28-181 Archer III POH, VB-2266 (issued 2013): https://www.andrews.edu/cp/aviation/resources/archeriii-poh.pdf
- [A2] FAA TCDS 2A13 Rev 48: https://touringmachine.com/Cherokee/PDFs/Cherokee%20Type%20Certificate%20Data%20Sheet%202A13.pdf
- [A3] Lednicer, Incomplete Guide to Airfoil Usage: https://m-selig.ae.illinois.edu/ads/aircraft.html
- [A4] RotatePilot Archer guide (secondary): https://rotatepilot.com/guides/piper-archer-guide
- [B1] EASA TCDS EASA.IM.A.007 (SR20/SR22/SR22T), Issue 19, 22 Apr 2026: https://www.easa.europa.eu/en/downloads/7512/en
- [B2] RotatePilot SR22 guide (G6, secondary): https://rotatepilot.com/guides/cirrus-sr22-guide
- [B3] AOPA SR22 fact sheet: https://www.aopa.org/go-fly/aircraft-and-ownership/aircraft-fact-sheets/cirrus-sr22
- [B4] COPA forum, interconnect rationale (owner forum): https://forum.cirruspilots.org/t/aileron-and-rudder-interconnect/280504
- Nelson, R.C., *Flight Stability and Automatic Control*, 2nd ed., Appendix B (C172 inertias). Roskam, *Airplane Design Part V/VI* (radii of gyration).
