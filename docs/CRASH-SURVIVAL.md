# Crash survival, the ghost bail-out, and what leaves the aeroplane

Ty, 2026-11-05 06:35: "Bailing out after a crash has the pilot surviving because the comment is only looking at his
impact and it is low. Make the comments apply to crashes too and when a pilot bails out after crashing a semi
transparent image of them getting out and [deploying] their parachute takes them up to the sky and it says something
corny about them going to heaven." 06:37: on any bail-out use the real canopy or door (not on a Cub or a Great Lakes),
not the phony clear canopy, and make sure it is gone from the aeroplane.

## 1. What the crash did to the pilot (Core `OccupantInjury`, Sim `Aircraft.Occupant`)

Until this, the only injury was the parachute landing's (impact speed 6 / 9 / 14 m/s → bruised / injured / dead). A
pilot who flew into the ground at 60 m/s and then pressed BAIL OUT stepped off a stopped wreck, "landed" at 1 m/s and
walked away.

Now the aeroplane carries an occupant model through the whole flight:

- **Measured seat acceleration.** Each sim step, the seat point's world velocity (CG velocity + ω × r) is taken before
  and after the WHOLE step, so whatever moved it counts: contact, crush, trees, water, a part leaving. Less gravity, in
  body axes, it is what an accelerometer on the seat would read. A state written between steps (restart, teleport,
  replay) is never read as an acceleration.
- **Brinkley Dynamic Response Criterion** (Brinkley & Shaffer 1971; Brinkley 1985; NASA-STD-3001): one damped
  spring-mass per body axis, DR = ω²x/g, ω/ζ = 62.8/0.20 (x), 58.0/0.09 (y), 52.9/0.224 (z, the spine). It filters the
  pulse the way a body does, so a crash that stops in a crush stroke is survivable and the same speed into something
  rigid is not. β = √Σ(DR/limit)² against the restrained Low / Moderate / High limits (x +35/−28, +40/−35, ±46; y ±14,
  ±17, ±22; z +13.1/−13.4, +15.2/−16.5, +18.0/−20.4). **These were copied from memory of the NASA-STD-3001 table,
  not re-read from the document in this session.**
- **Grades:** β_low ≥ 1 → Bruised; β_moderate ≥ 1 → Injured; β_high ≥ 1.5 → Dead (**Q for Ty**: Brinkley stops at
  "high risk"; 1.5 × is DRz 27, DRx −69).
- **Livable space:** the cabin crush the sim already keeps (`FuselageCrush.CabinCrushM`): any → Bruised, one crush
  stroke (0.15 m) → Injured, the full geometric fold (0.30 m) → Dead.
- **Combined:** when he leaves the aeroplane the crash verdict is frozen (`PilotEgress.CrashOutcome`); the final
  outcome is the worse of it and the parachute landing's. A bail-out from a flying aeroplane has an Unhurt crash
  verdict, so it is graded on the landing exactly as before.

Measured (tests, flat ground, hands off, 8 s):

| Case | seat peak | DR x / z | β high | cabin crush | verdict |
|---|---|---|---|---|---|
| C172 level flight 60 m/s | 1.4 g | −0.2 / 1.4 | 0.08 | 0 | Unhurt |
| J-3 20 m/s, 2 m/s down (prop strike) | 4.6 g | −0.2 / 4.7 | 0.26 | 0 | Unhurt |
| C172 25 m/s, 4 m/s down, nose gear first (gear + prop go) | 18.0 g | −11.6 / 15.6 | 0.90 | 0 | Injured |
| C172 40 m/s, 15 m/s down, nose 20° low | 42.4 g | −38.5 / 31.8 | 1.96 | 18 cm | Dead |
| Pitts 50 m/s, 35 m/s down, 45° dive | 208.6 g | −118.7 / 94.7 | 5.86 | 234 cm | Dead |

## 2. The ghost

If the crash verdict is Dead when he leaves (timed BAIL OUT, ejection or the headset's hands-on exit), he is a ghost:
every renderer of the figure and of his canopy is swapped for a translucent copy of its own colour (paled toward
blue-white, alpha 0.42, `FlyingGame/UnlitTransparent`, now single-pass-instanced ready for the Quest). He climbs out,
the canopy opens 1.2 s later by itself, and a lift of 1.6 g against his weight carries him UP, the canopy's drag
setting the climb (~5 m/s). He never lands. Once the canopy is full the line comes up — label **GONE WEST**, one of:

- "Blue skies and tailwinds, Captain. Next stop: the Good Ship Lollipop."
- "Cleared direct to the pearly gates. Climb and maintain forever."
- "Tailwinds all the way up. Save me a window seat on the Good Ship Lollipop."

Original text, no song lyrics. Flat screens show it on the egress status line; a headset shows it as world-space text in
front of the eyes (the same path as the landing one-liner), and the ghost and canopy are world objects.

## 3. What leaves the aeroplane (Core `CanopyEgress`, Bridge `AirframeVisual.JettisonCanopy`)

The model's own nodes, never a stand-in (the translucent dome, `CanopyJettison`, is deleted):

- `Canopy_Detachable` (Pitts, Extra), else `Canopy` (Swift, EB-29R);
- `Canopy_Frame` + `Canopy_Glass` + `Canopy_Hardware` together (2-33, Grob 103);
- a hinged cabin door on the exit side with its glass and latch (172: `C172_Door_R`, `Glass.Door.R`, `C172_Door_latch_R`);
- nothing for an open cockpit (Great Lakes), the Cub's fold-out door (`DoorUpper_R` / `DoorLower_R`), or a model with no
  separate canopy or door (the downloaded P-51, F-86, SR22, PA-28 etc.: their canopies are part of the fuselage mesh);
- the procedural shell's own `Canopy` part where no model is placed.

The parts are detached with everything under them and fly off as one `DebrisBody`; the next flight's rebuild brings
them back.

## Checks

- `tools/FlightTests/OccupantInjuryTests.cs`, `CanopyEgressTests.cs` (.NET suite).
- `BailoutHeavenCheck` (Unity, `-executeMethod FlyingGame.EditorTools.BailoutHeavenCheck.Run`): every type's bail-out
  jettison on the real components; a fatal crash → ghost rising and translucent; a gentle crash → lands, grade kept;
  an in-flight bail-out → unchanged.
