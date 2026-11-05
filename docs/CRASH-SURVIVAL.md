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

## 3. What leaves the aeroplane, and how (Core `CanopyEgress`, `Vr.CanopyBailout`; Bridge `AirframeVisual.CanopyParts` / `DetachCanopy`)

The model's own nodes, never a stand-in (the translucent dome, `CanopyJettison`, is deleted):

- `Canopy_Detachable` (Pitts, Extra), else `Canopy`;
- `Canopy_Frame` + `Canopy_Glass` + `Canopy_Hardware` together (2-33, Grob 103);
- a hinged cabin door on the exit side with its glass and latch (172: `C172_Door_R`, `Glass.Door.R`, `C172_Door_latch_R`);
- nothing for an open cockpit (Great Lakes, Jenny, Stearman), the Cub's fold-out door (`DoorUpper_R` / `DoorLower_R`),
  or a model with no separate canopy or door (the downloaded P-51, F-86, SR22, PA-28, EB-29R etc.: their canopies are
  part of the fuselage mesh);
- the procedural shell's glass dome only where the type's canopy comes off (`CanopyEgress.ShellCanopyLeaves`: Cassutt,
  Glasair III, Swift today). On a cabin type it is the windows, on the Aircam two windscreens, and a hang glider or
  paraglider has no cockpit, so it stays.

**How it goes (Ty 06:38: reuse the canopy system that already exists).** The bail-out does what the headset's hands do:
the red handle pulls, the latch opens, the hinge pins come out (`CanopyDynamics.ReleaseHingePins`), and from then on the
canopy's own physics decides. It uses the headset's `CanopyDynamics` where it has one (the Pitts, the Extra), else Core's
written-down Pitts/Extra spec, else a spec sized from the part itself (`CanopySpec.ForPart`: its bounds in the
aeroplane's axes, the Pitts'/Extra's 10.8 kg/m², and a door turned to hang on its front edge). In the air (q above that
of 20 kt) it goes on the first step. At rest, a wreck, his hand shoves the free edge (the Pitts slides its 4 in first)
through CanopyDynamics' own bounded hand servo until it is past 10° and free. He does not jump until it has gone. The parts
are then detached with everything under them and flown as one `JettisonedCanopy` by `CanopyFlight` (Core
`CanopyFreeBody`: the canopy's own mass, drag and lift), and the aeroplane sheds the mass. The next flight's rebuild brings
them back. While the bail-out works it (`CockpitRigState.EgressCanopy`) the headset's hands leave that canopy alone, so it
is stepped once a frame.

## Checks

- `tools/FlightTests/OccupantInjuryTests.cs`, `CanopyEgressTests.cs`, `CanopyBailoutTests.cs` (.NET suite).
- `BailoutHeavenCheck` (Unity, `-executeMethod FlyingGame.EditorTools.BailoutHeavenCheck.Run`): every type, flying and at
  rest, against a hand-copied table of what each type loses (exactly those parts gone from the airframe, flown by
  `CanopyFlight`, nothing else spawned, he gets out; open cockpits lose nothing); a fatal crash → ghost rising and translucent; a gentle crash → lands, grade kept;
  an in-flight bail-out → unchanged.
