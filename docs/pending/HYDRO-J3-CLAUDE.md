# J-3 on floats: the hydrodynamics deep dive (lane/claude-hydro, 2026-11-02)

Ty, 2026-11-02 ~17:30: *"the water drag is too high on the j3 on floats and the stick free stability thing should be
turned off when using a controller … the boyancy and planing on step all seem weak do a deep dive on the
hydrodynamics. the water spray … is clearly not 'physics based' … make it purely physics based"*.

**Status: code, tests and numbers. Not approved: Ty must feel-test the water run, the spray and the gamepad option.**

## Where it lives

| What | File |
|---|---|
| Float / hull hydrodynamics (all terms below) | `src/FlyingGame.Core/FloatHydro.cs` |
| The sectional hull (stations, waterline cut, Michell) | `src/FlyingGame.Core/FloatHull.cs` (new) |
| Spray physics (parcels, drops, Kelvin foam) | `src/FlyingGame.Core/SprayField.cs` (new); drawn by `unity/.../Bridge/FloatSplash.cs` |
| Added mass, applied after each RK4 step | `FloatHydro.AddedMassOf / CorrectForAddedMass`, called in `src/FlyingGame.Sim/Aircraft.cs` |
| Wheels in water | `src/FlyingGame.Core/WheelHydro.cs` (unchanged) |
| Airframe in water (crash/ditching) | `src/FlyingGame.Core/Wreck/*` (lane/claude-crash-water; unchanged, `FloatFlooding` now uses the hull's sections) |
| J-3 floats geometry | `configs/aircraft/j3-cub-floats-like.json` `floats.stations` (+ StreamingAssets mirror), cut from the GLB by `tools/own_models/j3_float_stations.py` |

The hydro code was identical on lane/claude-island-nav and lane/claude-vr-quest; island-nav lacked the J-3 own model on
wheels and floats, its rig JSONs, `check_eye_interiors.py`, `J3OwnModelTests` and `J3FloatsWaterTests` (cherry-picked
from vr-quest a8174695, 9e5f31ad, 541dd6be, ce62f612; e0c4d22c is Quest-only — QuestFleet / QuestMenu / seat-by-ID do
not exist on island-nav — and was skipped: vr-quest already has it). vr-quest lacks the crash-water `Wreck/*` code.

## The floats: drawn = physics

The physics float was a different float from the one drawn off Piper/EDO dwg 60-S-908A. Now the flight model floats on
the drawing: 30 rings cut from `j3-cub-floats-like-own.glb` exactly as `AirframeModels.Place` places it.

| | Old physics block | Drawn (now physics) |
|---|---|---|
| Float centre y | ±1.26 m (track 2.52) | **±0.9435 m (track 1.887)** |
| Bow / step / transom x (datum) | +2.24 / −0.125 / −2.06 | **+1.9945 / −0.4437 / −2.3888** |
| Chine beam at step | 0.585 | **0.536** |
| Keel below CG at step | 1.495 | **1.442** (lowest point, keel plate 1.4496) |
| Deadrise at step | 20° | 18.8° |
| Afterbody keel angle | 4° | 6.9° |
| Step aft of the CG | 0.08 m | 0.39 m (15° from the vertical: the classic rule) |
| Water rudder | both floats, 0.038 m², ±34° | **LH float only** (60-1320 parts list), drawn blade 0.32 × 0.17 m, 0.043 m², ±25° |

Float-keel contacts moved onto the drawn keel, the step one on the GLB's lowest point, so `Spec.SitHighM` 0.0454 → **0**;
`check_eye_interiors.py`: airframes **COINCIDE (0.0 mm)**, drawn vs physics float **0.001 m** laterally, keel at the
contact points within 9 mm; the approved rear eye unchanged (liner 0.159 m overhead, as before).

**The narrower track halves the roll stiffness**: GM_T 3.8 m on the drawn floats (by the waterplane and by the model's
own righting moment) vs ≈ 10 m on the old 2.52 m block. 3.8 m is still large (the righting moment at 5° is 1.9 kN·m,
the weight acting 0.33 m off the centre of buoyancy), and the low-power full-rudder capsize is gone; the boundary
moved to fast plow turns (below).

## Term table (current → corrected)

| Term | Before | Now | Source / why |
|---|---|---|---|
| Buoyancy | 14 stations of a block V at the keel, area at keel depth only, no heel in the section | ρg·A per station of the **drawn closed section cut by the tilted water plane**, at the cut's centroid | Archimedes, strip integration; heel/trim/heave all in the cut |
| Rated displacement | block sized to "1,320 lb" | 0.601 m³ = **1,326 lb** per float | EDO 60-1320 (A-691 Item 206) |
| Heave damping | −0.6ρ·plan·w\|w\| − **250·plan·w** (fudge) | radiation B′₃₃ = ρg²Ā²/ω³, Ā = 2\|sin(kB/2)\|e^(−kT) at ω = √(ρgA_wp/m), faded with the step ventilation; + ½ρ·1.2·B·w\|w\| | Haskind 2-D, Newman 1977 §6.17; Hoerner 1965 C_D 1.2 |
| Added mass | none | strip theory m′₃₃ = ρπc²/2, m′₂₂ = ρπT²/2, roll/pitch/yaw inertias, implicit after RK4 (slam = momentum) | Wagner 1932, Faltinsen 2005 §8; same scheme as HydroBody |
| Side force | quadratic C_D 1.2/1.0 + **1800 N/(m/s)/m² "keel" (fudge)** at the keel | slender-body lift f = U∂(m′v)/∂x to the largest section + crossflow C_Dc 1.0, at **mid-draft** | Munk 1924 / Jones 1946; Hoerner 1965, Faltinsen 2005 |
| Form / wave drag | 0.5ρ·**0.05**·A_max·V² (fudge) | **Michell** thin-ship integral on the drawn hull at its rest waterline, × (vol/vol_rest)² × (1 − ventilated) | Michell 1898 (Wehausen 1973) |
| Ventilation | afterbody drops 0.25(C_V−2.5)… (invented) | step dry by transom-draft Froude F_T 2 → 4; afterbody in the **Savitsky & Morabito 2010** wake H/b = 0.17(A + 0.03L_kτ^1.5) sin((π/C_V)(X/3)^1.5) | Maki/Doctors 2006, Robards & Doctors 2003; S&M 2010 (as Gray-Stephens et al. 2021 eq. 1) |
| Dry step face | none | −ρg·Q_step (= Savitsky's buoyant Δtanτ) | hydrostatics of an open face |
| Planing lift | Savitsky dynamic term, τ at the step, forebody only | same, deadrise ratio C_Lβ/C_L0, **incidence at the CP** (pitch damping), **afterbody planing too** | Savitsky 1964 |
| Planing drag | L tanτ | L tanτ (fore + aft) | Savitsky 1964 |
| Friction | ITTC + 0.0004 on λb²/cosβ only | ITTC-57 + 0.0004 on **max(station girth, Savitsky pile-up area)**, Re on the wetted length, V₁ in planing; afterbody separately | ITTC 1957, Savitsky 1964 |
| Spray drag | none | whisker spray b²/(8 sin2α cosβ) per side, friction × cos2α | Savitsky, DeLorme & Datla 2007 |
| Water rudder | ½ρV²A·**2.5**·sinδ (fudge slope), both floats | Helmbold slope at 2× immersed AR + 1.2 sin²α crossflow, normal to the blade, **geometric immersion** (out of the water on the step), LH only | Helmbold; Whicker & Fehlner 1958; Hoerner 1965 |
| Weathervaning | aero model | aero model (unchanged): 15 kt crosswind holds it into wind at 10–30 % power | AeroModel side force |

Remaining stated constants: F_T 2/4 (the least certain: the regression in Doctors et al. 2007 was not reachable), C_D 1.0/1.2,
ΔC_F 0.0004. Not modelled: twin-float wave interference, deck awash, spray lift, forward-speed radiation.

## BEFORE / AFTER — J-3 on floats, 1,300 lb, calm, no wind

Same technique both: full power, stick full back until the nose has risen and starts down (over the hump), ease to a
6° planing attitude, +4° past 40 mph and let it fly; rudder holds the heading. Probe in the sim, engine on.

| | Before (a985b452) | After |
|---|---|---|
| Water run (to unstick, water < 2 % W) | 19.1 s | **19.1 s** |
| Water run distance | 821 ft (250 m) | **751 ft (229 m)** |
| Hump (peak water drag) | 21.5 mph, 0.19 W, drag ~1,000 N held to 30 mph | **14.5 mph**, 0.21 W |
| On the step (afterbody dry, planing > buoyancy) | 20.9 mph | **21.2 mph** |
| Water drag on the step, 24 / 30 / 36 mph | 1,013 / 938 / 859 N | **815 / 691 / 589 N** |
| Unstick / clear | 47.6 / 49.8 mph | **48.6 / 50.0 mph** |
| Static draft at the step | 0.232 m (block) | **0.285 m level, 0.295 m as it rests (2.1° bow-up)** |
| Static buoyancy vs weight | 5,783 / 5,785 N | 5,783 / 5,785 N |
| Reserve (fully immersed) | 2.02 W | **2.04 W** (rule ≥ 1.8) |
| GM_T | ≈ 10.1 m | **3.8 m** (narrower drawn track) |
| GM_L | 7.9 m | **5.9–6.1 m** |
| Taxi speed, 10 / 20 / 30 % power, stick back | 12.3 / 14.5 / 15.3 mph | **6.1 / 9.4 / 11.5 mph** (plow taxi, not semi-planing) |
| Idle | 0 mph | 0 mph — `PropModel` gives 0 N at idle (open question) |

Ty's bands: run 15–25 s ✓, 600–1000 ft ✓, hump 12–18 mph ✓, step 20–25 mph ✓, liftoff ~45 ± 5 ✓ (48.6).

**Other types** (same probe, before → after): PA-18 hump 22 → 15 mph, step 23 → 27, unstick 59 → 64 mph,
1,159 → 1,322 ft (heavier, 2,000 lb, slower-rotating technique); Beaver hump 29 → 26 mph, unstick 75 → 82 mph,
1,565 → 1,806 ft; GM_T PA-18 9.2 m, Beaver 13.3 m (their block floats unchanged). H-4: its step was 0.14 m AHEAD of the
CG and it water-looped; step moved to 10° aft (stepFraction 0.52 → 0.54). AirCam capsizes at rest before and after
(open, not this lane).

## No flip at low power

`FullRudderOnATaxiDoesNotCapsizeIt` (un-skipped): 10, 20, 30 % power (6–11 mph), full right rudder, aileron neutral / into /
out of the turn, calm and 15 kt from the right: worst roll **9°**, no speed-up in the turn. Boundary: **60 % power,
~13 mph plow turn, 15 kt, aileron out of the turn → 79° (capsize)**; 45 % in wind 15°, calm 60 % 12°. The old capsize
came from the fudged lateral terms applied at the keel.

## Porpoising

Stick held still on the step (75 % power): keel trim ≈2–4° around 30 mph falls off the step and comes back up in a
growing 4–5 s cycle (lower-limit porpoising, pitch swing 7–10°); ≈5–6° at 40 mph runs steady (±0.5°); very high trim
oscillates again (upper limit, the afterbody slapping). Nothing damps it but Savitsky's quasi-steady lift at its own
centre of pressure and the added mass. Open: validate the limits against the Day–Haag curves (Savitsky 1964 Fig. 7).

## Spray

From the same waterline cut: each spray root is the waterline crossing moved out by Wagner's π/2 pile-up; the jet is the
relative water velocity in the facet plane reflected about the root line (2V sin α earth-frame, aircraft rates included);
mass flux ρ|u|δ, δ = (π/4) sin²τ_f c. Parcels of 1–3 mm drops fly under gravity and sphere drag (Clift–Grace–Weber C_D;
2 mm terminal 6.5 m/s vs Gunn & Kinzer 6.49) and die on the water. Kelvin cusp foam at 19.47° at displacement speeds.
Budget 600 parcels + 160 foam marks (the old spray ran ~1,500 particles). Prop-wash mist unchanged.

## Gamepad

OPTIONS › Controls › **"Gamepad: stick-free (controls float when the stick is centred)"**, off by default, saved
(`set.gamepadStickFree`). With a pad connected a centred stick holds the surfaces; touch play unchanged; hot-plug read
every frame. Rule: `GamepadShaping.ControlsFree`.
