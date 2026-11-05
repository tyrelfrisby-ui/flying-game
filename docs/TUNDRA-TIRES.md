# Tundra tyres and a tyre catalog (Claude, 2026-11-04)

Ty approved this at 2:39 PM PT ("Yes and 35's") and added a heavy-duty 35 for the Beaver at 2:40 PM.
Branch `lane/claude-tundra-tires`, off `lane/claude-tire-spinup` e8e2978b. The spin-up physics it builds on is
`docs/TIRE-SPINUP-AND-GROUND-SOUND.md` §7.

## 1. What was there

- **Tyre sizes and masses were hand-set in every config.** Each config carried `tireRadiusM` and `wheelMassKg` (81 wheel
  legs). Inertia was a flat 0.65·m·r².
- **The Bushwheels session option doubled things.** It doubled the radius and the rotating mass of every wheel on any
  aeroplane, and halved the pressure (gross/200). The `gearType: "bushwheel"` field on pa18-bush-like was read only by
  the procedural wheel visual.
- **A tyre was not a spring.** The ground force was the leg's spring and damper alone. Tyre width was a flat 0.38 of
  the diameter, and pressure was used only for water planing.

## 2. The catalog: one source of truth (`src/FlyingGame.Core/TireCatalog.cs`)

A C# table in shared Core, the repo's idiom for this (like `GroundMaterials`):
- Unity compiles it on iOS and Quest alike, so there is nothing to load from StreamingAssets.
- Configs name a tyre by id (`"tire"`) and state the pressure they run it at (`"tirePressurePsi"`). A main leg can
  also name the tyre the bushwheel option fits (`"tundraTire"`).
- Figures are kept in the units their sources print (in, lb, psi). Each entry's `Source` says where every number came
  from and which numbers are estimates (EST).

| id | tyre | OD × width, rim (in) | tyre lb | wheel / disc lb | psi min/typ/max | rotating kg | I kg·m² | tyre's own k (I/mR²) |
|---|---|---|---|---|---|---|---|---|
| 8.00-6 | GY 806C61-5 6-ply | 19.5 × 7.95, 6 | 12.3 | 4.5 / 2.5 EST | 12/18/35 | 9.7 | 0.282 | 0.70 |
| 8.50-6 | GY 856T61-1 6-ply TL | 22.1 × 8.85, 6 | 16 (unverified) | 4.5 / 2.5 EST | 12/18/30 | 10.4 | 0.417 | 0.70 |
| 6.00-6 | GY 606C66-8 6-ply | 17.5 × 6.3, 6 | 8.9 | 4.0 / 2.5 EST | 15/30/42 | 7.4 | 0.165 | 0.70 |
| 5.00-5 | GY 505C66-5 6-ply | 14.2 × 4.95, 5 | 5.5 | 3.0 / 2.0 EST | 18/35/50 | 5.2 | 0.070 | 0.68 |
| 8.00-4 | GY 804C41-1 4-ply | 18.0 × 8.3, 4 | 9 | 3.5 / 2.0 EST | 8/15/24 | 7.5 | 0.172 | 0.68 |
| 8.50-10 | GY 850C86-2 8-ply | 25.65 × 8.7, 10 | 23.2 | 13.5 / 4.0 EST | 20/30/55 | 19.8 | 0.998 | 0.73 |
| scott-8in | 2.80/2.50-4 tailwheel | 8 × 2.8, 4 | 2.0 | 1.3 / — | 10/20/35 | 1.7 | 0.009 | 0.64 |
| 26126.R | Alaskan Bushwheel 26x12x6 | 26 × 12, 6 | 26 | 4.0 / 2.5 EST | 8/12/20 | 14.7 | 0.923 | 0.71 |
| 29136.R | Alaskan Bushwheel 29x13x6 | 29 × 13, 6 | 31 | 4.0 / 2.5 EST | 8/12/20 | 17.0 | 1.369 | 0.71 |
| 31136.R | Alaskan Bushwheel 31x13x6 | 31 × 13, 6 | 35 | 4.0 / 2.5 EST | 8/12/20 | 18.8 | 1.766 | 0.71 |
| 351510.R1 | 35x15x10 Super Cub lightweight | 35 × 15, 10 | 44 | 9.6 (ABI-10650-LW) / 2.5 EST | 6/8/30 | 25.4 | 2.944 | 0.73 |
| 351510.R | 35x15x10 Beaver heavy duty | 35 × 15, 10 | 60 | 13.5 (40-134) EST / 4.0 EST | 10/12/30 | 35.2 | 4.021 | 0.73 |

**Sources** (each entry's `Source` field names its own):
- **[GY]** Goodyear Aviation Data Section 2022, §4C Type III (goodyearaviation.com/resources/pdf/Data-Section-2022.pdf;
  the 2018 data book has the same rows).
- **[CD]** Michelin Condor data sheet, Avg. Wgt. (media.mcfarlaneaviation.com/documents/Condor_Data_Sheet_QpzYmtL.pdf).
- **[GYT]** Goodyear 2018 tube table.
- **[MCF]** McFarlane Aviation, mcfarlaneaviation.com/products/29136--R/ and /351510--R/. Bushwheel tyre weights are
  copied verbatim from the brief.
- **[ICA]** Airframes Alaska / Alaskan Bushwheels Installation Instructions & ICA rev A, 2021
  (documents.alaskagearcompany.com/docs/Alaskan-Bushwheels-Airstreaks-Installation-Instructions.pdf). It gives sizes,
  pressures, wheels, the "approximate finished weight" (quoted in brackets in each entry), and the 1/4 in HD tread.
- **[ASW]** aircraftsupply.com ABI-10650LW-1.5: 9.6 lb.

**Not published, so estimated (on NEEDS YOU):**
- Every Cleveland wheel and disc weight. Only shipping weights exist: 3 lb for a 164-xxx disc.
- The disc OD (1.15 × the rim diameter).
- The bushwheels' "typical" 12 psi and the R1's 6 psi floor.
- Bushwheel load ratings, ply counts and the HD carcass details.
- Measured ODs. Bushwheel sizes are nominal.
- The 8.50-6 tyre weight. It came from an unopened search snippet.

**Inertia is derived, not set.**
- **Tyre:** a bias tyre's mass is 60 % tread and crown within ½ in of the OD, 30 % sidewall annulus, and 10 % bead at the
  rim. This split is an estimate from a cut section.
- **Tube:** a thin torus.
- **Wheel:** 55 % barrel at the rim radius and 45 % web as a disc.
- **Disc:** an annulus from 0.7 to 1.0 of its OD.
- **Result:** a tundra tyre's own k = I/(m R²) comes out at 0.71–0.73, and the whole wheel at 0.57–0.61.

**Legacy entries** cover every type whose tyre was not researched in this lane: Pitts, Extra, P-51, DC-3, F-86, 737,
JN-4, Stearman, Gee Bee, Cassutt, the drone, the SR22 mains, the AirCam, the EB29R and Swift, and several tailwheels.
- They keep the spin-up lane's radius and mass exactly.
- They have no carcass data, so they are rigid, with the old κ0 of 0.08, the surface table's rolling resistance and
  0.65·m·R². **Those types behave exactly as before.**

## 3. Physics (`src/FlyingGame.Core/TireModel.cs`)

All of it comes from NACA TN 4110 / NASA TR R-64, Smiley & Horne (ntrs 19930084870). p is inflation pressure, p_r is
rated pressure (`MaxPsi`), w is section width and d is outside diameter.

- **Vertical spring:** eq. 24, Type III,
  `F = (p + 0.08 p_r)·w·√(wd)·2.4·[δ/w − 0.03(1 − e^(−20δ/w))]`. The 0.08 p_r term is the carcass. A bottoming term
  (×20) applies past 0.85 of the section height (EST).
  - Check: the Goodyear 8.00-6 at 2,050 lb and 35 psi deflects 2.05 in. The data book's static loaded radius gives
    2.20 in.
- **In series with the leg** (`TireModel.Junction`): a massless axle with `k_l(x−δ) + c_l·max(0, ẋ−δ̇) = F_t(δ) + c_t·δ̇`.
  - It is advanced implicitly per RK4 stage from the step's start value. Stage 0 uses τ = 0, stages 1–2 use dt/2 and
    stage 3 uses dt. Stage 3's value starts the next step.
  - A soft tyre takes the first of the touchdown, because the leg's damper is stiff at that instant, so the load starts
    near zero and builds at the tyre's rate.
  - c_t is the tyre's own hysteresis, with ζ = 0.05 (EST). Tyres are bouncy.
- **Footprint:**
  - length `1.7√(dδ−δ²)` (eq. 5);
  - width `2w√(δ/w−(δ/w)²)`, capped at w (eq. 8);
  - gross area `2.3·δ·√(wd)` (eq. 12).
  - The ground pressure F/A comes out at about p + carcass.
- **Rolling radius:** `r_e = R − δ/3` (eq. 66). The wheel's spin, slip and brake torque all use it.
- **Slip curve:** `κ0 = μN / (r_e·K_x)` with `K_x = 0.53·d·(p + 4p_r)·∛(δ/d)` (eqs. 48, 90), clamped to 0.02–0.3.
  - The friction circle and the spin-up lane's implicit ω integration are unchanged; κ0 is now a parameter.
- **Rolling resistance:** the tyre's hysteresis plus the ground's compaction share.
  - Hysteresis is ∝ √(δ/d). It is the whole of the coefficient on pavement.
  - The ground share is Bekker's `z/((n+1)l)`. Against the reference tyre it scales as `(p_g/p_ref)^(1/n)·(l_ref/l)`.
  - Wong's n is used per soil: snow 1.6, sand 1.1, gravel 1.1 (EST), grass/dirt/rough 0.7.
  - The reference is a 6.00-6 at 30 psi carrying 4 kN, the light-aircraft tyre the surface table's literature numbers
    are for.
- **Packed snow:** sinkage is `p_g / 125 kPa` of the crust (EST), from the 0.18 floor toward 0.35. It replaces the old
  `Bushwheels ? 6000 : 2500 N` rule.
- **Water:** WheelHydro takes the tyre's real section width and its stated pressure. The water branch itself is
  unchanged.
- **The spin-up nose-down impulse is not added anywhere.** It is the drag at the patch, I·V/r_e² of impulse, at the
  patch's distance below the CG.

**The bushwheel option** (`TireFit`) fits each main wheel with a tundra tyre:
- **Which tyre:** the leg's `tundraTire` if it names one. Otherwise, the lightest of 26/29/31/HD 35 that carries the
  static load at its rated deflection (TR R-64 eq. 27, F ≈ 0.57·p·w√(wd)) without going above its typical pressure.
  If none qualifies (DC-3, 737), the stock tyre stays.
- **Pressure:** the one that puts that tyre at its rated deflection, held to the tyre's own range.
- **Geometry:** the axle stays where it is, so the contact point drops by the difference in radius.
- **Off again:** restores the stock tyre exactly.
- The flag is now only the session's choice. No physics reads it.

## 4. Configs changed (configs/aircraft and its StreamingAssets copy, identical)

- **Every wheel leg in 28 configs:** `tireRadiusM` and `wheelMassKg` were replaced by `"tire": <id>` and
  `"tirePressurePsi"`.
- **Pressures** are the values in effect before (book figures, else the owner's gross/100 rule).
  - **Raised legs:** on a researched tyre, a leg whose rule pressure left the tyre below its RATED DEFLECTION for its
    static load was raised to the pressure that puts it there (TR R-64 eq. 27, `TireCatalog.PressureForLoadPsi`). No
    leg was lowered. The gross/100 rule decided only planing before; it is now the tyre's spring, and at 24 psi a
    Glasair's 5.00-5 under 1,090 kg sits on its rim:

    | config | leg | before | after (psi) |
    |---|---|---|---|
    | glasair3-like | mains | 24 | 44.7 |
    | glider-2-33-like | main | 6.2 | 28.7 |
    | decathlon-8kcab-like | mains | 18 | 21.0 |
    | decathlon-8kcab-like | tail | 14.4 | 28.6 |
    | great-lakes-like | mains | 18 | 21.0 |
    | great-lakes-like | tail | 14.4 | 28.6 |
    | seminole-like | mains | 38 | 42 |
    | pa25-pawnee-like | tail | 16 | 31.8 |
    | aircam-like | tail | 12.9 | 25.6 |

  - **Unchanged:** the book figures (C172, PA-28, PA-18s, Beaver, 737).
- **pa18-bush-like:** 351510.R1 at 8 psi.
- **pa18-cub-like:** 8.00-6 at 18 psi.
- **dhc2-beaver-like:** stock 8.50-10 at 30 psi; `tundraTire: 351510.R`, the option's tyre (10.1 psi for its static
  load).
- **dhc2-beaver-floats-like:** not changed (floats).

**Real-number corrections, with radius and rotating mass before → after:**

| config / leg | before | after | why |
|---|---|---|---|
| pa18-cub-like mains | 0.28 m, 9.7 kg, I 0.494 | 0.248 m (8.00-6 is 19.5 in), 9.7 kg, I 0.282 | GY OD; derived I |
| pa18-bush-like mains | 0.445 m, 16 kg, I 2.06 | 0.4445 m, 25.4 kg, I 2.94 | 44 lb tyre + 9.6 lb LW wheel + disc |
| pa25-pawnee-like mains | 0.28 m, 10 kg | 0.248 m, 9.7 kg | 8.00-6 |
| j3-cub-like mains | 0.27 m, 7.5 kg | 0.229 m (8.00-4 is 18.0 in), 7.5 kg | GY OD |
| dhc2-beaver-like mains | 0.33 m, 14 kg | 0.326 m, 19.8 kg (EST wheel) | 23.2 lb tyre, 40-134 wheel |
| c172 / pa28 / decathlon / great-lakes mains | 0.222 m, 7.6 kg | 0.222 m, 7.4 kg | 6.00-6 |
| seminole mains | 0.222 m, 8.0 kg | 0.222 m, 7.4 kg | 6.00-6 6-ply (the 8-ply is not in the catalog) |
| 5.00-5 noses (c172, pa28, seminole, sr22, glasair) | 0.18 m, 4.6 kg | 0.180 m, 4.3 kg | GY + CD |
| glasair mains, 2-33 main | 0.18 m, 5.6 / 5.0 kg | 0.180 m, 5.2 kg | 5.00-5 braked |
| Scott 8 in tails (pa18-cub, decathlon, great-lakes, aircam, pawnee) | 0.10 m, 1.3–1.5 kg | 0.102 m, 1.7 kg | 3 lb 13 oz wheel + tyre (unverified) |

Every one of those legs now also has a tyre spring in series, a footprint, TR R-64 slip and tyre rolling resistance.

## 5. Measured (tools/FlightTests/TireCatalogTests.cs)

AGENT-LOG has the touchdown matrices.
- **Spin-up cost** (cold − pre-spun ground speed) on the PA-18 at 43 kt, matching ΣI·V/r_e²/m:
  - 0.26 m/s on 8.00-6s;
  - 0.77 m/s on the 35 LW.
- **Spin-up nose-down rate** (first 0.15 s, cold wheels):
  - 8.00-6: −9.7 deg/s;
  - 26 in: −15.7;
  - 29 in: −18;
  - 31 in: −20;
  - 35 LW: −25.
  - Pre-spun it is about −3 deg/s on every tyre.
- **Bounce:** cold wheels bounce less, because the spin-up's nose-down cancels the bungee's nose-up. At 6 ft/s:
  - 8.00-6: 0.10 m cold, 0.22 m pre-spun;
  - 35 LW: 0.00 m cold, 0.25 m pre-spun.
- **Beaver HD 35 vs LW 35:**
  - rotating mass 35.2 vs 25.4 kg;
  - I 4.02 vs 2.94 kg·m²;
  - spin-up ΔV on the Beaver 0.55 vs 0.40 m/s.
