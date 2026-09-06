# Vertical Slice (v0) — Arena 1, Challenges 1–2

The smallest build that proves the whole idea: real 6DOF glider physics, visible air, RC touch controls, and two graded challenges. Everything below ships; nothing else does.

## What v0 contains

**Aircraft — one:** Schweizer 2-33–like glider from `AircraftConfig` JSON (~25:1 glide, spoilers wired in config but no spoiler challenge yet). Full strip-theory aero including post-stall tables — the model that will later spin ships now, even though no challenge asks for it. That's the point: Challenge 1–2 validate the same physics the whole curriculum rides on.

**World:** flat farm grid (tiling ground texture), four floating N/S/E/W letters on the horizon, still air (wind system present, set to zero).

**Bubble field v1:** instanced, air-mass-fixed, wrapping grid around the aircraft; profiled on the oldest supported device before art polish.

**Controls:** dual RC touchpads (LEFT vertical = speed brake on the glider — neutral stowed, full aft fully deployed, forward inert; LEFT horizontal = rudder; RIGHT elevator+aileron), dead zone/expo from config.

**Camera:** chase cam v1.

**HUD:** subtle IAS + altimeter, challenge callout text, pass/fail card with accuracy %.

**Challenges (data-driven, from `ChallengeDefinition` JSON):**
1. *Wings level, hold heading* — spawn at altitude on tow-release trim; hold heading ±5° and bank ±5° for N seconds; score = % of time inside the bands.
2. *Pitch for best glide / min sink* — hold target IAS band; score = % of time on speed (glide-ratio-achieved shown on the result card).

**Fail handling v0:** ground contact = challenge failed, instant free restart. No lives, no economy.

**Save:** local `ProgressSave` JSON with best score per challenge.

**Flight-test harness:** `tools/FlightTests` running headless on the C# oracle: trim convergence, static stability signs, roll-rate sanity, **adverse-yaw sign test**, stall-break behavior. Green harness = physics credible before it's ever flown by thumb. Once the C++ port exists (build step 3), the same scenarios replay against it and must match the oracle's golden trajectories within tolerance — the oracle is the spec, the C++ module is what ships.

## Explicitly NOT in v0
Challenges 3–11, spins as a challenge, prop/engine, other aircraft, wind/thermals, landings, lives/credits/IAP, unlocks, free play, menus beyond challenge select, sound design, final art, name/branding.

## Definition of done
- Runs 60 fps on the oldest supported iPhone with the full bubble field.
- Ty flies both challenges and the glider *feels like a 2-33* on trim, control harmony, and adverse yaw (visible in the bubbles: nose swings opposite the roll input).
- Oracle flight-test harness green in CI, AND the C++ port's golden-trajectory replay matches the oracle within tolerance.
- Both challenges pass/fail correctly from JSON with no challenge-specific C++.

## Build order (when "build the slice" is given)
1. Core math + 6DOF + flight-test harness scaffold (headless C# oracle, no engine yet)
2. Strip-theory aero + 2-33 config + oracle harness tests green
3. Unreal project + C++ port of Core/Sim (validated against oracle golden trajectories) + Bridge (sim→Actor transform, chase cam, ground plane)
4. Bubble field + on-device perf pass ← gate: fail here means rethink the visual before proceeding
5. Touchpads + feel tuning on device
6. ChallengeRunner + the two challenge JSONs + HUD + save
