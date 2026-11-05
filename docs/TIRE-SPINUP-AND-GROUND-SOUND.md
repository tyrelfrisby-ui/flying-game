# Tyre spin-up, ground-roll sound, brake sound (Claude, 2026-11-04)

Branch `lane/claude-tire-spinup` off `lane/claude-island-nav` b7d1a680. §1–6 are the analysis (line numbers against
b7d1a680); Ty approved the build at ~09:40 the same day, and **§7 is what was built**.

## 1. What the code does today

**There is no wheel rotation in the sim.** No ω state, no wheel inertia, no spin-up. `src/FlyingGame.Core/WheelHydro.cs:42` says so
outright ("Not modelled (stated): the wheel's spin-up"). Every tyre is treated as rolling perfectly from the first instant of contact.
The visual wheels are static meshes too: `AirframeVisual.cs` ~3463/3480 builds them, and only the props spin.

Per gear leg, in `LandingGear.Compute` (`src/FlyingGame.Core/LandingGear.cs`):

| What | Line | Formula / constant |
|---|---|---|
| Normal load | 492 | `N = SpringN·penetration + DampNs·max(0, compressionRate)` (spring/damper, one-sided damping) |
| First-contact "impact" | 481-488 | records closing rate → `StructuralDamage.OnHardImpact` (96-108) → `TireChirp(clamp01((closing−0.4)/3))` if ≥ 0.4 m/s |
| Leg break | 95, 481-488 | closing > BreakSinkMs (default 4.0 m/s mains, 8.0 tailwheel) × ImpactStrength |
| Friction limit | 552 | `muLimit = TireMu · (tailwheel 0.5) · SurfaceGrip(surface) · N` |
| Cornering | 544-548 | `slip = atan2(vSide, |vFwd|+0.5)`, `CorneringPerLoad 12` (tailwheel 4) |
| Rolling resistance | 563 | `rollN = WorldTerrain.RollingCoefficient(surface) · N` (snow: `SnowSinkageRolling`, 222) |
| Brake | 397-399, 559-561 | per wheel `brake·(1∓bias)` by side; `brakeDemandN = wheelBrake · TireMu · staticLoadN · 0.675`, `staticLoadN = m·g·0.88/nBraked` (387) |
| Lock-up | 568-573 | if `brakeDemandN > muLimit && speed > 0.3`: force = −0.8·muLimit along velocity |
| Longitudinal force | 580-588 | `longN = −(rollN + brakeDemandN)·sign(vFwd)`, clipped to the friction circle |

So **at touchdown the only fore-aft tyre force is rolling resistance** (2.5 % of N on asphalt). The real spin-up
drag spike (μN for tens of ms), the speed it costs, and the nose-down moment it makes are all missing.

Surface tables:
- `LandingGear.SurfaceGrip` (116-132), multiplied by TireMu 0.8: Paved 1.0 (μ 0.80), Gravel 0.75 (0.60), Grass 0.65 (0.52),
  Rough 0.55 (0.44), Snow 0.20, PackedGlacierSnow 0.40, GlacierIce 0.106.
- `WorldTerrain.RollingCoefficient` (519): Paved 0.025, Gravel 0.05, Grass 0.065, Rough 0.15, Snow 0.14, Packed 0.18, Ice = paved.
- `WorldTerrain.Surface` enum (493): Paved, Gravel, Grass, Rough, Snow, PackedGlacierSnow, GlacierIce. **There is no Dirt or Sand.**
  `StripFinish.Rolls` maps Paved→Paved, Turf→Grass, and **Dirt, Sand and Water→Gravel**. `FrankChurch.SurfaceAt` (306) maps Dirt→Gravel.

Gear constants (from configs/aircraft/*.json; r = `GearConfig.TireRadiusM`, else `LandingGear.TireRadiusOf` 27-39 =
clamp(fuselage·0.045, 0.15, 0.65), tailwheel ×0.45, bushwheels ×2):

| Aircraft | mass kg | main k N/m | main c N·s/m | TireMu | main r m | note |
|---|---|---|---|---|---|---|
| J-3 Cub | 553 | 60 000 | 3 000 | 0.8 | 0.307 auto | tailwheel k 25 000 c 3 750 μ 0.6 |
| PA-18 | 794 | 60 000 | 3 000 | 0.8 | 0.310 auto | |
| Pitts S-2B | 737 | 80 000 | 4 000 | 0.8 | 0.170 | tailwheel r 0.075 |
| Extra 300 | 820 | 80 000 | 4 000 | 0.8 | 0.155 | |
| C172 | 1 043 | 90 000 | 4 500 | 0.8 | 0.261 auto | nose k 50 000 c 7 500 |
| Decathlon | 816 | 70 000 | 3 500 | 0.8 | 0.314 auto | tailwheel k 28 000 c 4 200 |
| Great Lakes | 816 | 80 000 | 4 000 | 0.8 | 0.222 | |
| SGS 2-33 | 350 | 50 000 | 2 500 | 0.8 | 0.2475 auto | single centre main, plus tail/tip/nose skids |
| 737 (ref) | 65 000 | 1.8e6 | 1e5 | 0.8 | 0.55 | one leg per side |

No config has a wheel inertia or mass field. `GearConfig.RollResistN` (default 40) is **read nowhere** (dead field).

## 2. Spin-up physics and results

The wheel lands with ω = 0 at ground speed V. Slip friction μN spins it up: I·dω/dt = μ·N(t)·r until ω·r = V.
- With constant N: **t = I·V / (μ·N·r²)**. With I = k·m_w·r² this becomes **t = k·m_w·V/(μN)**, so the radius cancels.
  Spin-up time depends on wheel+tyre mass, speed, μ and load.
- The impulse per wheel is ∫μN dt = **I·V/r²**, independent of μ and N. It costs the aircraft ΔV = Σ(I·V/r²)/m.
- That drag acts at the contact patch, below the CG. It gives a nose-down pitch moment ≈ μN·h for the spin-up time.
- **N is not constant.** It ramps up as the oleo/bungee compresses (`k·x + c·ẋ`), so the gear spring and damper set the ramp rate.
  The probe uses the real `LandingGear.Compute` for N(t).

Probe (`docs/tire-spinup-probe/`, a .NET console that references FlyingGame.Core):
- It drops each config level at 50 kt with lift = 0.9 W, using the real `LandingGear.Compute` at dt 1e-4.
- It integrates ω for each main wheel with μ = TireMu·SurfaceGrip.
- **Assumed** wheel+tyre+brake mass: Cub/PA-18 7.5, Pitts/Extra/2-33 4.5, Great Lakes 6.0, C172/Decathlon 6.5, 737 150 kg (2 per leg).
  I = 0.65·m·r².
- "Dirt" = the Gravel row, because that is what the code uses for dirt.
- To run: `dotnet run -c Release --project docs/tire-spinup-probe -- configs/aircraft 0.9`.

**Spin-up time, ms, 50 kt, level touchdown, lift 0.9 W:**

| Aircraft | r m | I kg·m² (assumed) | sink ft/s | peak N/wheel | asphalt μ0.80 | dirt(=gravel) μ0.60 | dry grass μ0.52 | rough μ0.44 |
|---|---|---|---|---|---|---|---|---|
| J-3 Cub | 0.307 | 0.461 | 1 | 1173 | 153.7 | 578.1 | 626.8 | 701.6 |
| J-3 Cub | 0.307 | 0.461 | 3 | 3244 | 50.4 | 67.2 | 78.4 | 95.2 |
| J-3 Cub | 0.307 | 0.461 | 6 | 6372 | 26.3 | 34.5 | 39.5 | 46.5 |
| J-3 Cub | 0.307 | 0.461 | 10 | 10547 | 16.3 | 21.3 | 24.4 | 28.6 |
| PA-18 | 0.310 | 0.467 | 1 | 1451 | 118.7 | 160.4 | 188.9 | 243.1 |
| PA-18 | 0.310 | 0.467 | 3 | 3848 | 46.5 | 60.2 | 68.6 | 80.1 |
| PA-18 | 0.310 | 0.467 | 6 | 7480 | 25.2 | 32.7 | 37.2 | 43.2 |
| PA-18 | 0.310 | 0.467 | 10 | 12329 | 15.9 | 20.6 | 23.5 | 27.4 |
| Pitts S-2B | 0.170 | 0.085 | 1 | 1564 | 63.8 | 85.0 | 99.2 | 120.3 |
| Pitts S-2B | 0.170 | 0.085 | 3 | 4325 | 23.7 | 31.0 | 35.4 | 41.5 |
| Pitts S-2B | 0.170 | 0.085 | 6 | 8495 | 12.4 | 16.2 | 18.6 | 21.7 |
| Pitts S-2B | 0.170 | 0.085 | 10 | 14060 | 7.6 | 10.0 | 11.5 | 13.4 |
| Extra 300 | 0.155 | 0.070 | 1 | 1659 | 61.6 | 80.9 | 93.3 | 111.6 |
| Extra 300 | 0.155 | 0.070 | 3 | 4531 | 23.4 | 30.5 | 34.8 | 40.6 |
| Extra 300 | 0.155 | 0.070 | 6 | 8874 | 12.3 | 16.1 | 18.4 | 21.5 |
| Extra 300 | 0.155 | 0.070 | 10 | 14670 | 7.6 | 10.0 | 11.4 | 13.3 |
| C172 | 0.261 | 0.288 | 1 | 2005 | 74.6 | 97.9 | 113.1 | 135.5 |
| C172 | 0.261 | 0.288 | 3 | 5400 | 29.0 | 37.6 | 42.8 | 49.9 |
| C172 | 0.261 | 0.288 | 6 | 10539 | 15.4 | 20.1 | 22.9 | 26.7 |
| C172 | 0.261 | 0.288 | 10 | 17398 | 9.6 | 12.5 | 14.3 | 16.7 |
| Decathlon | 0.314 | 0.417 | 1 | 1565 | 94.2 | 126.6 | 148.1 | 180.7 |
| Decathlon | 0.314 | 0.417 | 3 | 4212 | 36.4 | 47.2 | 53.8 | 62.9 |
| Decathlon | 0.314 | 0.417 | 6 | 8219 | 19.4 | 25.3 | 28.9 | 33.6 |
| Decathlon | 0.314 | 0.417 | 10 | 13568 | 12.1 | 15.8 | 18.1 | 21.1 |
| Great Lakes | 0.222 | 0.192 | 1 | 1654 | 81.0 | 109.2 | 128.3 | 156.1 |
| Great Lakes | 0.222 | 0.192 | 3 | 4521 | 30.5 | 39.8 | 45.5 | 53.3 |
| Great Lakes | 0.222 | 0.192 | 6 | 8855 | 16.1 | 21.1 | 24.1 | 28.1 |
| Great Lakes | 0.222 | 0.192 | 10 | 14640 | 10.0 | 13.1 | 15.0 | 17.5 |
| SGS 2-33 | 0.247 | 0.179 | 1 | 1253 | 85.7 | 111.1 | 127.6 | 151.5 |
| SGS 2-33 | 0.247 | 0.179 | 3 | 3302 | 34.5 | 44.5 | 50.5 | 58.5 |
| SGS 2-33 | 0.247 | 0.179 | 6 | 6407 | 18.6 | 24.2 | 27.5 | 32.0 |
| SGS 2-33 | 0.247 | 0.179 | 10 | 10555 | 11.7 | 15.2 | 17.4 | 20.2 |
| 737 (ref) | 0.550 | 29.494 | 1 | 43596 | 110.9 | 137.0 | 152.3 | 172.7 |
| 737 (ref) | 0.550 | 29.494 | 3 | 102069 | 50.4 | 63.4 | 71.0 | 80.9 |
| 737 (ref) | 0.550 | 29.494 | 6 | 192154 | 28.5 | 36.4 | 41.0 | 47.1 |
| 737 (ref) | 0.550 | 29.494 | 10 | 312718 | 18.3 | 23.6 | 26.7 | 30.8 |

Reading it:
- A firm 3 ft/s arrival spins up in roughly 25–50 ms on asphalt and 30–80 ms on grass. A 6 ft/s arrival takes about half that.
- 1 ft/s greasers take 60–150 ms. The Cub at 1 ft/s takes 580–700 ms on soft surfaces because it skips: N drops to zero mid spin-up at lift 0.9.
  At lift 0.7 the Cub's 1 ft/s values are 110 / 146 / 170 ms.
- From 3 ft/s up, lift 0.7 vs 0.9 changes the results by < 5 %.
- The impulse costs ΔV = 0.18 (Extra) to 0.45 m/s (J-3).
- Peak nose-down moment at 3 ft/s on asphalt: Cub 6, Pitts 8, Extra 10, C172 9.5, Decathlon 9, Great Lakes 9, 2-33 2.5 kN·m.
  At 10 ft/s it is about 3× those. The 737 reference is 0.8–2 MN·m.

## 3. What is physically off

1. **No spin-up at all.** Wheels roll instantly, so there is no drag spike, no load dependence, no inertia, no ΔV and no spin-up pitch-down.
   The pitch-down matters for taildraggers in particular: it partly offsets the bungee's nose-up bounce on a wheel landing.
2. **The touchdown chirp is cosmetic and keyed wrongly.**
   - It only fires above 0.4 m/s (1.3 ft/s) closing, so the softest landings are silent. Physically they chirp longest.
   - Its duration is fixed at 0.16–0.28 s and the 1500→2400 Hz sweep at 0.12 s (`OneShotSynth` 150-155, 234-235, 346-352).
     Neither depends on μ, load, V or the real spin-up time, which is 8–150 ms.
   - It plays on grass, gravel, snow and ice too (only water is excluded) and for the tailwheel/nosewheel.
3. **No wheel rotation in the visuals, no skid marks, no tyre smoke.** `ContactEffects` dust/scrape covers airframe scrapes only
   (`AircraftContactDamage.UpdateScrapes` 500).
4. **The auto tyre radius is too big for small-tyre types:**
   - C172: 0.261 m vs about 0.19 m for a real 6.00-6.
   - Decathlon: 0.314 m vs about 0.20 m.
   - Cub: 0.307 m vs about 0.26–0.27 m for an 8.00-4/-6.

   This doesn't change spin-up time (r cancels), but it would put wheel rpm, and so bearing-whir pitch, ~30 % low.
5. **`RollResistN` is dead config.** Either delete it or make it the bearing/brake-drag term.
6. **No Dirt/Sand physics surface.** Dirt and sand roll and grip like gravel. Real dry dirt is ≈ μ 0.55–0.65 / μr 0.04–0.08. Loose sand is μr 0.15–0.3 with big sinkage.
   Grass (0.52) and gravel (0.60) values are reasonable for dry conditions. Wet grass (≈0.2–0.3) is not modelled.
7. **The brake is a force demand sized from static load, not a torque on a spinning wheel.** Lock-up is a branch rather than a slip ratio, so there is no near-lockup state to drive squeal or ABS-like feel.
   That is fine for now, but the sounds below want the ratio `brakeDemandN / muLimit`.
8. **Ground sound (`SurfaceSynth`, `FlightAudio.PushTelemetry` ~280-290):**
   - It samples one surface at the CG, not under each wheel.
   - Asphalt and grass are silent, snow is silent, and GlacierIce gets the Rough "thud".
   - There is no crossfade beyond a 50 ms gain smoother.
   - No bearing whir, no brake sounds, no float/water running sound (water audio is crash-only).

## 4. Proposed physics fix (needs Ty's OK; parity-shared Core)

- Add a per-wheel `Omega` state in `LandingGear`.
  - Config: `GearConfig.WheelInertiaKgM2` (default 0.65·m_w·r² with m_w from tyre size) and real `TireRadiusM` for the C172, Decathlon and Cub.
- Longitudinal force from slip velocity `s = vFwd − ω·r`, with κ = s/max(|vFwd|, 1 m/s): `Fx = −μN·tanh(κ/0.08)` (smooth, no chatter).
- Wheel equation: `I·ω̇ = Fx·r − T_brake − T_bearing`.
  - The brake becomes a torque, `T_brake = brake_i·T_max` with T_max = today's `TireMu·staticLoad·0.675·r`.
  - Lock-up then emerges when T_brake > μNr.
  - The airborne bearing decay τ is about 20–40 s.
- Stiffness: the slip time constant is about 1 ms. Integrate ω semi-implicitly per physics sub-step: clamp so ω·r never overshoots vFwd within a step.
- Drop the 0.3 m/s lock-branch hack and the unused `RollResistN`, or reuse it as bearing drag.
- Expose `WheelContact[]` per wheel: load N, surface key, vFwd, ω, slip κ, slip power μN·|s|, brake_i, locked, onSki, overWater, side.
  The new sounds, skid marks and tyre smoke all read from that.
- Tests: the probe's numbers become a FlightTests case. Checks: spin-up time within 10 % of t = I·V/(μ̄N̄r²), ΔV = ΣIV/(r²m), and lock-up at the right brake.

## 5. Ground/tyre SOUND proposal (Ty steering 2026-11-04)

The existing audio is procedural on the audio thread: `Dsp.cs` noise/biquads, `SurfaceSynth`, `OneShotSynth`, `WindSynth`, `EngineSynth` (merlin is the only sample engine).
`FlightAudio.PushTelemetry` fills `AudioTelemetry` on the main thread, and `OnAudioFilterRead` mixes stereo L/R (FlightAudio 337-370).
The plan keeps that pattern: no allocations on the audio thread, all telemetry pushed by value.

### 5a. One ground-material key shared by physics, imagery and sound

There are two sources today:
- Physics `WorldTerrain.Surface`, which is coarse.
- What the renderer paints, which is finer:
  - `StripFinish.Kind` Paved/Gravel/Dirt/Turf/Sand/Water;
  - `FrankChurch.Ground` Turf/Gravel/Dirt;
  - the island mesh attributes `IslandTerrainMesh.Attributes` 164-178: vertex colour (ice, debris, wet, forest), uv1 = (gravel−turf, snow−crevasse field).

Proposal: a Core `GroundMaterial` key with a `GroundMaterial.At(x, y)` that returns the same answer the renderer uses
(the strip's `StripFinish`, Frank Church `Ground`, island class + channel weights). Physics maps key → `Surface`.
Sound and (later) skid marks/dust read the key. **The visual code is Codex-owned and stays untouched.**
Codex would adopt the same key in the mesh/splat code when they choose to (NEEDS-Codex). Until then audio reads the same Core
functions the mesh already calls (`IslandTerrain.SurfaceAt`, `StripFinish.Of`).

Schema (`configs/ground-materials.json`, one row per key):
```
{ "id": "asphalt", "physics": "Paved", "visual": { "stripFinish": ["Paved"], "islandClass": null, "splatChannel": null },
  "mu": 0.80, "muRoll": 0.025, "wheelSpinup": true, "bearingWhir": true, "chirp": 1.0,
  "rolling": { "voice": "tarmacHum", "gainVsSpeed": [[0,0],[2,0.15],[15,0.5],[30,0.7]], "pitchVsSpeed": [[0,0.8],[30,1.3]],
               "bumpHzPerMs": 0.0, "bandHz": [180, 900] },
  "brakeScrape": true, "brakeSqueal": true, "skidMark": true, "dust": null, "crossfadeS": 0.20,
  "skiVoice": null, "floatVoice": null }
```

| key | physics | visual link (read-only) | μ / μr | spin-up chirp | bearing whir | rolling voice (gain/pitch ∝ speed) | brakes |
|---|---|---|---|---|---|---|---|
| asphalt / concrete | Paved | StripFinish Paved | 0.80 / 0.025 | full squeal | yes | low tyre hum + expansion-joint ticks | scrape + squeal |
| grass / turf | Grass | StripFinish Turf, FC Turf, uv1.x→turf | 0.52 / 0.065 | soft swish only | yes | swish + low bump rumble | scrape, rare squeal |
| gravel | Gravel | StripFinish Gravel, FC Gravel, uv1.x→gravel | 0.60 / 0.05 | short crunch spurt | yes | today's gravel crackle | scrape + stone crunch |
| dirt (new) | Gravel → new Dirt | StripFinish Dirt, FC Dirt | 0.60 / 0.06 | dull scuff | yes | soft rumble, few crackles | scrape |
| sand (new) | Gravel → new Sand | StripFinish Sand | 0.55 / 0.20 | none | yes | hiss/shush | scrape |
| rough / pasture | Rough | default island class | 0.44 / 0.15 | none | yes | today's thud + rattle | scrape |
| snow | Snow / PackedGlacierSnow | uv1.y snow, ski zones | 0.16–0.32 / 0.14–0.18 | **none** | **none** | crunch (wheels), ski hiss (skis) | none on skis |
| ice | GlacierIce | vertex colour ice | 0.11 / 0.025 | none | yes (wheel rolls) | thin hiss, cracks | scrape, no squeal |
| water | (FloatHydro / WheelHydro) | water plane / StripFinish Water | — | **none** | **none** | splash/spray ∝ speed | **none** |
| deck / metal | ContactPoint.SurfaceSolid | carrier/ship decks | 0.70 / 0.02 | full | yes | hollow drum | scrape + squeal |

Transitions:
1. Each physics frame, sample the key under **each wheel's contact point** (not the CG).
2. Give each wheel a weight vector over keys. It is a one-hot target for strips, or the island channel weights where the mesh blends materials.
3. Smooth each weight with a one-pole filter of time constant `crossfadeS` (0.15–0.3 s) and use equal-power gains.
4. Final gain per voice = Σ_wheels w_key · loadShare_i · gainVsSpeed(v_i).
5. Voices are aggregated per key per side (L/R), so a wheel sitting half on the runway and half on grass plays half and half,
   and a main wheel crossing a strip edge fades over about 0.2 s.
6. Pan by the wheel's side (±0.5 in cockpit, by camera-relative position in chase view).

Water: amphib wheels or floats in water get no chirp and no whir.
- Floats: `FloatHydro` planing gives hull slap at low speed, then a planing hiss and spray. Gain and brightness scale with hull speed and spray mass flux from `SprayField`.
  That would be the audio twin of `FloatSplash`, sharing its inputs.
- Floats on land or grass: keel scrape from `KeelSlidingCoefficient`, with no wheel sounds.

Snow: with skis carrying the load (`OnSki`, wheel-ski load share), there is no chirp, no whir and no brake sound, only ski hiss/crunch ∝ speed.
- Wheels on snow: wheel crunch, still no chirp or whir per Ty.
- A wheel-ski whose wheel protrudes onto pavement uses the normal wheel row.

### 5b. Bearing whir
- Per wheel, from ω: once the ω model exists use it; until then use kinematic ω = vFwd/r on the ground and decay it in the air with τ ≈ 20–40 s.
- Tone: f ≈ 9 × wheel rev/s plus partials at ×2/×3, with a little filtered noise. A Cub at 50 kt is about 13 rev/s → ~120 Hz plus 1–3 kHz shimmer.
- Gain: ∝ (ω/ω_ref)^1.5, very low (about −36 dB re engine). It is mainly audible engine-off, in gliders, or while coasting at idle.
- It fades naturally as the wheel slows. None on water or snow.

### 5c. Spin-up chirp done right
- Replace the closing-rate trigger. Chirp per wheel while slip power μN·|s| > 0. Gain ∝ log(slip power), duration = the actual spin-up time,
  and the pitch sweep ends when s → 0.
- Scale by the key's `chirp` (1 on asphalt/deck, small on dirt/gravel, 0 on grass-soft/snow/water).
- The same slip energy (½Iω² ≈ ½I(V/r)²) drives a tyre-smoke puff and a short skid mark on paved (Codex visuals).

### 5d. Brakes, per wheel L/R (inputs only: `lane/claude-trigger-brakes` is untouched)
- Per wheel brake pressure `p_i = brake·(1∓bias)`, exactly what `LandingGear.Compute` 397-399 and `CockpitRigState` 154-156 already compute.
- **Drag/scrape:**
  - Gain = k_d · p_i · clamp(v_i/15 m/s), which is ∝ brake power.
  - Voice: band-passed noise 300–900 Hz, centre rising with wheel speed, plus a faint pad grind.
  - Only when the wheel turns (v_i > 0.3) and the key has `brakeScrape`.
- **Squeal:**
  - Enabled when p_i > 0.6 or demand ratio ρ = brakeDemandN/muLimit > 0.8 (near lock-up). With the ω model, use slip κ > 0.08.
  - Voice: resonant tone 2.2–3.5 kHz, slight FM. Pitch rises with brake force; gain ∝ (ρ−0.8)·clamp(v_i/10).
  - It fades as the wheel stops, with an optional last "creak" below 1 m/s.
  - A locked wheel switches to the tyre-skid voice: on asphalt a broadband scrub with key `skidMark`.
- None on water, and none when floats or skis carry the load.
- Implementation:
  1. Add `AudioTelemetry` fields for up to 4 wheels: side, load, v, ω, p, ρ, key weights, onSki, overWater.
  2. Add a `TyreBrakeSynth` (Bridge/Audio) holding two side channels (L, R), each with whir, chirp, drag and squeal voices, mixed into the existing `l`/`r` sums in FlightAudio 359-369.
  3. Make `SurfaceSynth` table-driven by key.

## 6. Other lanes checked
- `lane/claude-crash-tuning` (Claude PID 35956, **read only**): its uncommitted structureType edits (34 configs + AircraftConfig.cs) and the untracked `CrashSlideProbe.cs` don't touch gear or tyres.
- `lane/claude-crash-physics` (da163a65) is already merged into island-nav, and it has no tyre or spin-up commits.

## 7. As built (2026-11-04, after Ty's approval)

### Physics (`src/FlyingGame.Core`, shared)
- **`WheelSpin.cs`** — one wheel equation, `I·ω̇ = D·r − T_brake − T_bearing`, and the slip force
  `D = μN·fall(κ)·sat(σ)·σx/σ` with `σx = κ/0.08`, `κ = (v_fwd − ω·r)/max(|v_fwd|, 1 m/s)` and `σy` = the tyre's
  lateral demand over μN (combined slip). `sat` is tanh for pure longitudinal slip and the old clamp for pure
  cornering, so **cornering is unchanged to the newton**. `fall` takes μ from its peak (κ ≤ 0.15) to the sliding
  0.8 μ by κ = 1, which is the same μ_k the old locked branch had.
- **Integration:** once a step, on RK4 stage 0, in sub-steps of ≤ 0.5 ms. It is linearly implicit in the slip, never
  carried through synchronism in one sub-step. The force on the aircraft is the one that changed the wheel's
  angular momentum, so **wheel and aircraft impulse agree exactly**. Stages 1–3 apply the same force per newton of
  their own load.
- **Brake = Coulomb torque**: `T = pedal_i · 0.675 · TireMu · W_static/n_braked · r`, which is Ty's 2026-09-13 tune
  written as a torque. The same `(1 ∓ bias)` split applies.
  - **Lock-up is emergent**: T > peak D·r and the wheel stops. The old `brakeDemandN > muLimit` branch is gone.
  - Skis keep their load-limited brake: a ski has no wheel against the snow.
- **Rolling resistance stays a direct force at the patch.** It is mostly ploughing, and on snow it exceeds the grip.
- **Bearing**: Coulomb `0.04 N·m/kg` plus viscous `I/30 s` (spin-down τ 30 s). `GearConfig.RollResistN` (dead) is
  **removed** from the code and from all 33 configs.
- **Per-wheel contact state** `WheelState` (`Aircraft.Wheels`): load, surface, `GroundMaterial`, v_fwd, ω, slip
  ratio, slip power, brake, brake torque, locked, onSki, overWater, side, plus slip energy/time accumulators for the
  sound.
- **Non-wheels** (float-keel, nose/tip/tail skids, `feet`) and direct `LandingGear.Compute` calls without state use
  the wheel's steady state: the old brake force, with the same lock threshold.
- **Seeding**: an aircraft placed on the ground (spawn, teleport, a test setting `State`) gets rolling wheels. One
  placed in the air gets stopped wheels.
- **Surfaces**: `WorldTerrain.Surface.Dirt` (7) and `Sand` (8), appended.
  - Dirt: grip 0.75 (μ 0.60), μr 0.06.
  - Hard sand: grip 0.6875 (μ 0.55), μr 0.08. The island's "hard sand" is wet-packed beach; loose sand would be 0.15–0.30.
  - Mapping: FrankChurch Dirt → Dirt. Island "dirt"/"hard sand" fields → Dirt/Sand via `StripFinish.Wheels` /
    `IslandTerrain.WheelSurfaceAt`, which is the physics' query.
  - **`IslandTerrain.SurfaceAt` and `StripFinish.Rolls`, which the terrain mesh reads, are unchanged**, so nothing
    anyone sees moves.
  - Heating, the chart tint, the micro-rattle, keel sliding and the forest read `WorldTerrain.Coarse(s)` (dirt/sand
    → gravel), so they are bit-identical.
- **Tyre radii** are now real sizes; the auto rule is a fallback only, and every wheeled config now states its own.
  Main / nose-or-tail, m:
  - C172 0.222 / 0.18 (6.00-6 mains, 5.00-5 nose).
  - Decathlon 0.222 / 0.10.
  - J-3 0.27 / 0.076 (8.00-4).
  - PA-18 0.28 / 0.10 (8.00-6).
  - PA-28 and Seminole 0.222 / 0.18.
  - SR22 0.19 / 0.18 (15×6.00-6).
  - Beaver 0.33 / 0.13 (8.50-10).
  - Stearman 0.31 / 0.11.
  - Pawnee 0.28 / 0.10.
  - JN-4 0.33 (26×4); its tail is a `tail-skid`.
  - Gliders 0.15–0.19 / 0.075–0.105.
  - 737 nose 0.343 (27×7.75-15; it was 0.5).
- **Rotating mass** (`wheelMassKg`: tyre + tube + wheel + disc), estimates from catalogue weights:
  - 5.00-5 ≈ 5.6 with brake, 4.6 nose.
  - 6.00-6 ≈ 7.6.
  - 8.00-4/-6 ≈ 7.5–9.7.
  - Tundra 16.
  - 737 main leg 380 (2 × ~190), nose 70.

### Sound
- **`GroundMaterial` + `GroundMaterials.Row`** (Core): the key shared by physics, sound and (later) imagery. Each row
  has its physics surface (grip and μr are *read* from the physics, never copied), the visual link (documentation
  only), the chirp scale, whir / brake-scrape / squeal / skid flags, the rolling voice with its gain- and pitch-vs-speed
  curves, and the crossfade time (0.15–0.30 s).
- **`GroundSoundMix`** (Core, tested):
  - Per-wheel key weights eased with each row's crossfade time and normalised to Σw = 1; voices get √w (equal power).
  - Equal-power pan ±0.5 by wheel side.
  - Chirp = the wheel's own slip energy / time, so its duration is the real spin-up's. The level is
    log10(P/50 W)/3 × the row's chirp: 1 on asphalt/deck, 0.25 gravel, 0.15 dirt, 0 on grass/sand/rough/snow/ice/water.
  - Whir from ω: 9 × rev/s, gain ∝ (v/25.7)^1.5 × 0.016, also in the air, so it fades as the wheel spins down.
    None in water, on snow or on skis.
  - Brake scrape ∝ pressure × rim speed. Squeal when p > 0.6 or κ > 0.08, ∝ rim speed, so it fades as the wheel stops.
    Neither in water, on skis, or where the row says no.
  - Water splash/hiss from wheels in water and from the floats (FloatHydro planing vs buoyancy share), spray from the
    floats' spray force, ski hiss, keel/skid scrape.
- **Bridge/Audio**:
  - `SurfaceSynth` is now a table of rolling voices, nine recipes × 2 sides.
  - `TyreBrakeSynth` (new) renders whir, chirp, scrape and squeal per side, and water, spray, ski and keel centred.
  - `FlightAudio` copies the mix into `AudioTelemetry` once a frame. Slip time crosses threads as a cumulative µs
    counter, so no chirp is lost between frames.
  - The old closing-rate `TireChirp` call in `StructuralDamage` is gone; the `FxKind` stays.

### Measured (tools/FlightTests/WheelSpinTests, GroundSoundMixTests)
- **Spin-up time, model vs probe**, from 3 ft/s up: +6 … +10 % on every row (the probe held μ at its peak; the model slides
  at μ_k while κ is large and fades by tanh at the end, timed to 5 % slip). The 1 ft/s rows (skips) are printed, not asserted.
- **Impulse** = ΣI·V/r² to within 4 % on asphalt and 10 % on grass. Rolling resistance runs on top.
- **C172 touchdown at 50 kt, 3 ft/s:**
  - ΔV 0.31 m/s against prespun wheels (ΣI·V/r²/m with the nosewheel ≈ 0.32).
  - Peak decel 1.05 g vs 0.08 g.
  - Pitch rate a further 3.9 deg/s nose-down during the spin-up.
- **Lock-up**: 0.95 × peak·r keeps turning (κ 0.14), 1.05 × locks. C172 on grass at full pedal locks (main load 0.62 of
  static); on asphalt at 60 % it does not. **On asphalt at FULL pedal the mains lock** once braking has moved load to the
  nose (0.69 of static against the 0.675 threshold), and they stay locked until the pedal comes off. A real locked wheel
  does, but the old per-stage branch only flickered. Ty's call (AGENT-LOG).
- **Taildragger wheel landing (PA-18, 25 m/s, 1.5 m/s sink, 6° pitch):**
  - Peak q is +6.4 deg/s with the wheels already turning (the bounce).
  - With cold wheels it is +0.4: the spin-up is ≈ ΣI·V/r²·h/I_yy ≈ 15 deg/s nose-down.
- **P-51, calm three-point landing:** the same impulse (≈ 16 deg/s) lifts the tail into a prop strike on the rollout
  (`TaildraggerNoTipStrikeTests`, left red). With zero wheel mass it passes.
- **Swift S-1 on water:** its real 300×100 tyre (r 0.15) digs in where the auto rule's 0.31 skimmed (`WheelWaterTests`, left red).

### 7b. Squeal pitch, scrub and a quieter bearing (Ty 2026-11-05, lane/claude-tire-audio)
Ty, after a stiff-crosswind J-3 landing: "the tire sounds need work it is a pitch/frequency thing for the chirp and
bearing noise is too loud".
- **What was wrong.** The chirp's pitch came from the slip RATIO alone: 1500 + 2200·min(3|κ|, 1) Hz. At touchdown
  κ ≈ 1, so every tyre on every type chirped at the same 3.7 kHz narrow band (measured in the harness: 3700 Hz for the
  first four frames of the J-3's spin-up). Sideways slip was not heard at all. The whir was 0.016 at 25.7 m/s.
- **Now (Core `GroundSoundMix`, tested in `TyreSquealTests`).** Each wheel keeps its patch's slip energy, audible time
  and ∫|F|dt fore-aft (WheelSpin's own slip) AND sideways (`LandingGear`: |F_y·v_side|, for the sound only), so the
  mean slip speed is E / ∫|F|dt.
  - Pitch: f = 700 Hz·m / OD × (v_slip / 10 m/s)^0.2, bounded ×0.6–1.35. The 700 Hz·m is an ESTIMATE from passenger-
    car tyre squeal (~1.1 kHz on a 25 in tyre); f ∝ 1/OD is the tread band's modes scaling with the tyre. 8.00-4
    (J-3) 1531 Hz, 6.00-6 1575, 8.00-6 1413, 5.00-5 1941, 8 in tailwheel 3445, 35 in bushwheel 787.
  - Squeal or scrub: the share of the slip power that squeals is a smoothstep of slip speed from 1.5 to 6 m/s
    (ESTIMATE; rubber friction falls past a low sliding speed, and that falling branch is what locks stick-slip into a
    tone). The rest is a broad, low SCRUB at 0.42 × the resonance × (v/3 m/s)^0.3.
  - Level: log10(P/50 W)/3, as before, per voice, times the row's chirp (0 on grass, so no scrub there either).
  - Whir: 0.0032 × (v_rim/25.7)² (−14 dB at the reference, and steeper: at a 5 m/s taxi it is a further 28 dB down).
- **Bridge `TyreBrakeSynth`.** The squeal is a gliding tone with 2nd/3rd partials, ±2–3 % stick-slip jitter and a
  narrow (Q 9) band of noise at the same pitch; the scrub is Q 1.1 band noise plus a low body under a 35 Hz rough
  flutter. Both play for exactly the slip time given (cumulative µs counters, as the chirp already did). The whir's
  noise shimmer is halved.
- **J-3, three-point on asphalt at 17 m/s, crabbed (harness and `CrabbedTouchdown…` test):** 1.5 m/s drift: squeal
  1697 → 1154 Hz over ~70 ms, then a scrub 600 → 386 Hz for ~300 ms as the drift dies. 4 m/s drift: squeal 225 ms
  (part of the faster side slide squeals, as a car tyre does at the limit), scrub ≤ 696 Hz. Other taildraggers: PA-18
  and Pawnee (8.00-6) 1590–1700 Hz start, Decathlon and Great Lakes (6.00-6) ~1890–1900, PA-18 bush (35 in) 865 Hz.
  Spectrograms and WAVs: `~/Developer/agent-lanes/renders/tire-audio/` (`compare-touchdown-*.png`: before above,
  after below; the harness renders tyre voices and the unchanged rolling voices only, no engine).
- Not changed: the rolling (ground) voices in `SurfaceSynth`, the brake scrape/squeal, the unused `FxKind.TireChirp`.

### 7c. All ground contact −4 dB (Ty 2026-11-05 08:48, lane/claude-ground-volume)

Ty: "the tire screeching and scraping is pretty good just a little too loud so lower all ground contact volume a bit".
One shared gain, `GroundSoundMix.OutputGainDb = −4` (linear 0.630957), applied after the synths so no level, pitch or
filter inside them moves: `FlightAudio.Render` multiplies the ground bus (`SurfaceSynth` rolling voices +
`TyreBrakeSynth`: squeal, scrub, whir, brake scrape/squeal, thump, flat-tyre slap, burst, water splash/hiss/spray,
ski hiss, float keel scrape) by it, and `ContactEffects` multiplies the airframe-scrape voices by the same constant.
Not touched: engine, wind, one-shots (`OneShotSynth`, incl. the unused `FxKind.TireChirp`) and crash impacts.

## 8. Tyre wear, burst and the flat tyre (Ty 2026-11-04 20:21, Q026 item 1)

Ty: keep the 172's full-pedal main-wheel lock-up (it is real), and let a locked tyre screech, smoke and wear through to a
burst; a flat drags hard, rides on the rim (the aeroplane sits down on that side), rolls lumpily and thumps in time with the
wheel. No special-case logic. Item 2: the 0.222 m (6.00-6) radius is confirmed.

### The model (`src/FlyingGame.Core/TyreWear.cs`, per wheel on `WheelState.Tyre`)

- **Wear = γ(T) × friction work** (Archard / Schallamach). The work is the wheel's own slip work, the
  `WheelSpin.StepResult.SlipEnergyJ` already computed for the chirp: nothing new decides it. The tread is 72 strips round the
  circumference; each step the work is shared over the strips the patch swept (patch length l = N/(p·w), w = 0.5 r) while the
  wheel turned. A locked wheel puts it all on one 11 cm patch (the flat spot); a spin-up smears it over the arc it turned.
- **γ depends on the surface temperature**, which is the Blok flash temperature of a sliding contact: the flux
  q = W/(A·dt) shared between rubber and ground so both surfaces match, each taking √π·e/(2√t) per kelvin, with e = √(kρc)
  (rubber 648; asphalt 1300, dirt/sand 900, turf 1600, snow/ice "melts", 20000) and t how long it has been heated: the ground
  for l/v_slip, each tread strip for as long as it has been in the patch. q per area is μ·p·v_slip, so a locked tyre's life is
  a DISTANCE (NASA TN D-6963: tread life at a slip ratio of 1 "can be measured in terms of meters along the runway"); a
  longer patch heats the ground longer, so a heavier tyre runs a little hotter (2 kN: 68 m, 5 kN: 54 m to wear through).
- γ goes from the road figure **2·10⁻¹² m³/J** (car tyre: ~1.4 L of tread in 50,000 km for ~6·10⁸ J of slip work) below a
  100 K rise to **1.0·10⁻⁹ m³/J** above 180 K (natural rubber's reversion, ~200 °C: the reverted-rubber skid). The hot figure
  is the one fitted constant (anchor: a 6.00-6 at 38 psi locked at 25 m/s on asphalt goes through in ~60 m). The independent
  check: a 50 kt spin-up then takes 0.06 mm off a quarter of the tread, ~0.015 mm a landing averaged round the tyre, ~450
  landings of 7 mm — about what a training aeroplane's mains last.
- **Abrasivity**: Schallamach abrasion is cutting; asphalt, gravel, sand 1.0, packed dirt 0.6, turf 0.1, snow/ice 0.02.
- **Burst** when any strip is worn through tread + carcass, 0.045 r (10 mm on a 6.00-6). Pressure then leaks with τ = 0.15 s.
- **The flat** is geometry: patch length l = N/(p_eff·w), capped where the wheel stands on rim (0.40 r) + two casings
  (2 × 0.04 r); p_eff = the air's plus 3 % for what the casing holds by itself. The axle **drop** (soft-tyre extra squash at the
  tyre's STATIC load, plus the mean wear under the patch) raises the gear's contact point and comes off the rolling radius
  that `WheelSpin.Advance` uses. Taken at the static load on purpose: a drop following the instantaneous load fed back on the
  leg spring and set the axle chattering.
- **Rolling drag**: a tyre's hysteresis loss per metre goes with its deflection, so the extra rolling coefficient is
  μr_paved × Δδ/δ_ref (δ_ref the sound tyre at its static load): ≈ +0.16 for a flat 172 main.
- **Lumps** are the tread turning under the axle: the worn-through strip changes the drop once a revolution. The work the
  axle does on it (N·|Δdrop| per step) is the thump.

### Sound and smoke (Bridge, shared by iOS/Mac and Quest)

- Core `GroundSoundMix`: per side, `ThumpJ` (the axle's work this frame on the lumps, so it arrives when the bad strip passes),
  `FlapHz`/`FlapGain` (a flat casing's slap at the wheel's own rev/s, level ∝ (1 − pressure) × load share × speed), `Pops`
  (bursts this frame). The screech is the existing skid voice from the slip time, unchanged.
- `TyreBrakeSynth`: a ~70 Hz knock for the thump, a 450 Hz slap once a revolution, a crack + falling boom for the pop.
- `TyreSmoke` (new, installed beside ContactEffects): particles at the patch in proportion to the rubber torn off
  (`WheelState.AbradedM3`, 0.15 cm³ a particle): a puff at a spin-up, a trail from a locked skid, almost none on grass.

### Measured (`tools/FlightTests/TyreWearTests`, C172, idle, full pedal held to a stop)

| case | worn (mm) | burst | where | peak rise |
|---|---|---|---|---|
| asphalt from 60 kt (31 m/s, a flapless arrival) | 10.7 | yes | after 65 m of 209 m | 242 K |
| asphalt from 25 kt (13 m/s) | 2.9 | no (flat spot) | — | 152 K |
| grass from 55 kt (28 m/s) | 0.38 | no | — | 130 K |
| asphalt from 49 kt at 60 % pedal (no lock) | 0.0003 | no | — | 25 K |
| touchdown 50 kt, 3 ft/s (spin-up) | 0.06 over 18 of 72 strips | no | — | — |

Flat left main at rest: the 172 sits 2.3° left-wing-down; rolling at 8 m/s it decelerates 0.67 m/s² more than on sound tyres
(≈ +0.16 rolling coefficient on that wheel); the flat wheel's load swings 28 % (sound: 5 %), ~460 J/s of thump work at
12 rev/s.

Not modelled: lateral (scrub) slip work does not wear the tread (only the longitudinal slip's); a flat's cornering stiffness is
unchanged; the wheel visual does not show the flat (the aeroplane's sit and roll do); tyres are new at every spawn.
