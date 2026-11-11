# Rigging the fleet for hands-off level flight at 75 % power

Owner request, 2026-11-02 (verbatim): *"trim/rig all of the airplanes for hands off level 75% power. so the aileron
will be trimmed so that neutral is zero roll at 75% power and the vertical stabilizer will be angled such that the
yaw/sideslip is zero at 75% level flight."*

## What a rig is here

Two angles per aeroplane, in `configs/aircraft/*.json` under `"rig"` (`RigConfig`), built into the airframe the way a
real one is rigged — not a control input, not a controller, nothing that runs per frame:

| field | meaning | sign |
|---|---|---|
| `aileronRad` | where both ailerons sit relative to the stick, stick centred (a bent fixed aileron tab / re-rigged aileron neutral). Added to the aileron deflection on every aileron strip in `AeroModel.Compute`. | + = the deflection a RIGHT stick gives (rolls right) |
| `finOffsetRad` | incidence of the whole fin, rudder hinged to it, to the centreline. Added to every vertical-tail strip's incidence in `AeroModel.Compute`. | + = leading edge LEFT / trailing edge RIGHT (pushes the tail left, yaws the nose RIGHT) |

`RigTests.PositiveRigRollsAndYawsRight` pins both signs. The other fields (`throttleFraction`, `altitudeM`, `tasMs`,
`alphaRad`, `elevatorTrimRad`, `bankRad`) record the condition and the solution; the sim reads only the two angles,
and the in-air start uses `tasMs`/`alphaRad`/`elevatorTrimRad`/`bankRad` as the Newton starting point.

The elevator is not rigged: the hands-off elevator is the **trim**, and the in-air start sets the trim slider to it.

## The rig condition

* **Powered:** steady, straight, LEVEL flight at **75 % of rated power** (`RigSolver.RigThrottle`), **4,000 ft ISA**
  (`RigSolver.RigAltitudeM` = 1,219.2 m), stick and rudder centred, zero sideslip, zero rates, gear up, flaps up.
  "75 % power" is defined the way the engine model defines power: `PropModel` makes shaft power = `MaxPowerW ×
  throttle`, with no altitude lapse, so throttle 0.75 is 75 % at any height (a jet: 75 % of its thrust). 4,000 ft sits
  between the island's coastal in-air starts (1,514–1,865 ft) and its inland ones (5,817–6,524 ft).
* **Unpowered:** steady glide at **best glide** — the speed of maximum L/D from the model's own polar
  (`RigSolver.BestGlideTas`, golden-section over trimmed glides) — at the same altitude. Exception: the **hang glider**
  is rigged at its hands-off speed (its authored 11 m/s spawn speed), because with the bar left alone at the model's
  best glide (14 m/s) it is divergent in pitch and tucks after ~20 s; a flex-wing pilot flies best glide by holding the
  bar in, not by trimming for it.

## The solver (first principles, no fudge factors)

`src/FlyingGame.Sim/RigSolver.cs`. Newton's method with a numerical Jacobian on the full six-equation steady state —
`[Fx, Fy, Fz]/W` and `[L, M, N]/(W·1 m)` all zero — evaluated on exactly the forces the sim flies:

* `AeroModel.Compute` with the tail slipstream (`Aircraft.TailSlipstream`: momentum-theory ΔV, contracted tube, and
  its swirl, which rolls the aeroplane through the two stab halves), no rates, clear of the ground;
* `Aircraft.PropulsionForces`: thrust and thrust line, torque reaction, P-factor, spiral-slipstream yaw, gyroscopic
  (zero with zero rates), per engine at its mount for multi-engine types;
* the weight-shift swing moment where there is one; gravity at the solved attitude.

`TailSlipstream` and `PropulsionForces` were lifted out of `Aircraft.StepWithDeflectionTargets` verbatim (behaviour
unchanged) so the solver and the sim cannot drift apart.

Unknowns (powered): true airspeed, α, elevator, aileron rig, fin offset, bank. Level flight with zero sideslip fixes
the pitch attitude, tan θ = tan α · cos φ. Unpowered: pitch attitude (the glide path) instead of airspeed. The bank is
a fraction of a degree: with no sideslip the fin's own lift is a side force, and only a sliver of the lift vector can
carry it in straight flight. The power curve has two level speeds; a scan from fast to slow lands Newton on the front
side. All 34 types converge to a residual below 2·10⁻¹² of the weight in 3–4 iterations.

A type without both ailerons and a fin (hang glider, paraglider) has nothing to rig; its lateral angles are held at
zero, and it is symmetric, so its rolling and yawing moments are zero anyway (`HandsOffAtBestGlide` checks < 10⁻⁶).

The rig is computed **offline** by `tools/FlightTests/RigTool.cs` (`RIG_WRITE=1 dotnet test --filter RigTool`), which
solves each type from the bare (unrigged) airframe and writes the `rig` block in place, touching nothing else in the
file. `RIG_PROBE=1` prints the table without writing. `RigTests.ModelStillAsksForTheSameRig` re-solves every type and
fails if the stored rig is stale, so a change to the aero or prop model forces a re-run.

### `--filter RigTool` is the WRITER, and it checks nothing

**`--filter RigTool` selects exactly one test**, `RigTool.SolveAndWriteTheFleetRig`, and with neither `RIG_WRITE` nor
`RIG_PROBE` set that test returns on its first line and **passes in under a second**. It is not a check and never was;
measured 2026-11-10, that filter reports `Total tests: 1 / Passed: 1`. The checks are all in `RigTests`:

```
dotnet test --filter 'FullyQualifiedName~FlyingGame.FlightTests.RigTests'   # the pins: ~190 cases, ~20 min
RIG_WRITE=1 dotnet test --filter RigTool                                    # the writer: rewrites every config's rig
RIG_PROBE=1 dotnet test --filter RigTool                                    # the writer, printing only: ~30 s
```

Do not shorten that first filter to `--filter Rig`: `Rig` is a substring of **`Right`**, which this suite uses in dozens
of test names (`PositiveYawRateSwingsTheNoseRight`, `RightEngineOutYawsRight`, …), so `--filter Rig` selects 108 tests
across 40 classes and buries the nine that matter. That is not pedantry. On 2026-11-10 the fleet's lateral rig had been
stale for a day and a half — seven configs 3–5× off their own table — and no filtered run all evening selected
`ConfigRigMatchesTheHandCopiedTable`, because the 4,444-test suite no longer finishes in a sitting and the only command
this document gave was the one above that runs the writer and passes.

**A literal check cannot see a rig that went stale because the MODEL moved.** `ConfigRigMatchesTheHandCopiedTable`
compares each config against the table below; when an aero change invalidates a rig, the config and the table stay in
perfect agreement and go wrong together. That night it was green for 27 of 34 types whose stored rig was stale by up to
5× — the red seven were only the ones an earlier commit had rewritten in the config without re-copying the table. The
test that sees the real thing is **`ModelStillAsksForTheSameRig`** (33 of 34 red that night). Run it after anything that
touches a surface section, a tail, the propeller or the slipstream.

**The in-air start** (`FlightSimDriver.Spawn`, default branch) now calls `RigSolver.SolveLevel` (rig held: speed, α,
elevator, bank) at the start's own altitude, spawns there with the trim slider on that elevator, and the touch
throttle preset for air starts is 75 % (`TouchFlightControls.PresetTrim`, was 50 % with a POWER-OFF glide trim —
which is why the old air start pitched up). Gliders start at best glide (hang glider: its hands-off speed). The other
starts (final, thermal, STOL, practice, challenges) are unchanged: they were already on neutral aileron and rudder and
trim the elevator for their own condition.

## Result (copied into `RigTests.RigTable`)

**Re-solved for the WHOLE fleet, 2026-11-10**, after this week's two tail changes: `daf52725` moved all 26 fins to the
low-aspect-ratio plate and `b7396cd0` made twelve types' horizontal tail a flat plate. All 35 configs converged
(residuals 2·10⁻¹⁶ – 1·10⁻¹²), and the Grob 103 is rigged for the first time.

A weaker fin needs more incidence for the same yawing moment, and that is the whole story of the big numbers below: the
fabric types' fin offsets went up four to six times (J-3 0.364° → 2.260°, Pitts 0.475° → 1.997°, Great Lakes 0.361° →
1.950°) while their aileron rigs came *down* by a third to two thirds (Pitts 0.386° → 0.096°) and the solved bank with
them. The flat-plate horizontal tail moved the elevator trims and, through its drag, the rig *speed* — Pitts 139.1 →
126.2 KTAS at the same 75 %, Great Lakes 108.5 → 99.8. The J-3 landing at 2.26° is worth something on its own: Ty
measured about 1.76° of fin-chord-plane offset on his own aeroplane, and nothing here is tuned toward it.

Before this re-solve, 33 of the 34 rigged types' stored rigs were stale against the model (`ModelStillAsksForTheSameRig`),
and the hands-off launch left every powered type rolling to its propeller's torque side — the Pitts 36°, the Extra 26°,
the Cubs and the Decathlon 3–5°, in ten seconds. Seven configs had been rewritten on 2026-11-09 by `b11819f7` (the
measured-slipstream commit) without re-copying this table, which is the only reason the literal check was red at all;
it was green for the other twenty-seven, which were just as stale. See "`--filter RigTool` is the WRITER" above.

**RE-SOLVED AGAIN, 2026-11-10, for the five FITTED-BLADE types only** — both Cubs (`bladePitch75Deg` 15.33) and the
three constant-speed types with fitted stops (Pitts, Extra, Great Lakes). **The rig was solving a propeller the sim
does not fly**, three ways, and every one of them reaches only a SOLVED propeller, which is why they landed on these
five and nothing else:

1. `RigSolver.SteadyForces` took its drive from `PropModel.SteadyDrive`, which governs a constant-speed blade
   unconditionally and so carries a shaft rpm, while `AircraftEngine.SolvesShaft` leaves a GOVERNING constant-speed
   type on the throttle law. `PropModel.IsSolved` was therefore true in the solve and false in flight for the Pitts,
   the Extra and the Great Lakes — not two estimates of one propeller but two different propellers, and that
   predicate also decides whether the FIN stands in the slipstream's geometric swirl or takes the bulk
   `Parts.SlipYaw` moment. The predicate now lives in one place (`PropModel.IntegratesShaft`) and the rig asks it
   through `PropModel.SteadyFlownDrive`.
2. The shaft was seeded at `max(IdleRpm, 500)` rather than at its torque balance, so every in-air start spooled up
   through the wrong torque, P-factor and swirl — the J-3 from 724 rpm against the rig's 2,068.6.
3. `SteadyDrive` fed `PistonEngine` the DENSITY ratio where the parameter is the ambient PRESSURE ratio (0.888
   against 0.864 at 4,000 ft, an engine 3 % stronger in the solve), and `SteadyForces` left `PropulsionForces`'
   `noseBody` at its default, so the arm the disc's in-plane force swings on was −cg instead of nose − cg.

**The three constant-speed types' fin offsets fall from ~1.94° to 0.36–0.49° and their rig speeds rise 7–8 %**,
because the rig no longer stands their fin in a geometric swirl the sim never applies to them. The two Cubs, which
are fixed-pitch and solved on both sides, keep their large offsets (J-3 2.557°, and Ty measured about 1.76° of
fin-chord-plane offset on his own aeroplane). **The other thirty rows reproduced to the last digit**, which is both
the idempotence check and the predicate: only a type whose blade is fitted goes anywhere near the solved propeller.

With this the rig and the sim agree at the rig point for every type in the fleet — the moment the flown model applies
that the solve did not see is |L| ≤ 0.005 N·m, |M| ≤ 0.001 and |N| ≤ 0.005, where for the Pitts it had been L −577
N·m and N +512, the Extra L −418 and N +844 and the Great Lakes L −300 and N +664.

**STILL OPEN, and it is the better physics rather than a bug**: the three constant-speed types never run the
blade-element propeller in normal flight at all, because `SolvesShaft` is gated on the governor having lost its oil.
Integrating the governed shaft would make them solved everywhere, which is the direction the blade fitting has been
going and which the Cubs' measured-supported fin offsets argue for. It is not done here: it moves the propeller
blast over their tails by about 17 % and takes six `EngineStateTests` pins and a `FuelValveTests` pin with it, and
that is a handling change for three aerobatic types rather than a bookkeeping fix.

**RE-SOLVED AGAIN, 2026-11-11, for the two J-3s only** — not because the model moved, but because the AEROPLANE did.
Both configs had been authored at their placards, 1,220 lb and 1,300 lb, and they now carry the type's published
empty weight plus the fleet's one-pilot-in-the-rear-seat reference: 680 lb (Piper Service Memo 41) + 86 kg, and
805 lb (that plus the 125 lb seaplane exchange) + 86 kg. `mass.massKg` is what `RigSolver` flies, so the wheels Cub
dropped 553.0 → 394.4 kg and the floats one 589.7 → 451.1 (see `docs/WEIGHT-AND-BALANCE.md`, "what `mass.massKg`
means"). A lighter aeroplane at the same 75 % power is a **different rig**: it flies faster and at a far lower angle
of attack — 56.3 → 62.6 KTAS at α 3.51° → −0.21° on wheels, 59.6 → 65.6 at 3.08° → 0.00° on floats — and the
elevator trim crosses zero with it (−1.74° → +0.54°, −1.51° → +0.55°). The fin offsets move about a tenth of a
degree and keep their shape (+2.557 → +2.447, +2.243 → +2.314), which is the check that this is a weight change and
not a tail change: a tail change moves the fin first. `RIG_ONLY=j3-cub-like,j3-cub-floats-like`, so the other
thirty-two configs were not touched.

This is also the second time the rig has caught a **rig/sim mismatch rather than a rig fault**, and the first time
in the mass rather than the propeller. Authoring the J-3's empty weight ALONE — the obvious reading of the
instruction — would have left `RigSolver` and `PracticeScenario` on `mass.massKg` (1,220 lb) while `AircraftLoad`
handed the sim 870 lb: a 29 % mass error between the aeroplane that was rigged and the aeroplane that flies, which is
exactly the shape of "the rig was solving a propeller the sim does not fly" above. Whatever authors an empty weight
must move `mass.massKg` with it.

**AND IT FIXED THE J-3's THREE RED TESTS.** `RigTests.HandsOffLevelAtRigPower(j3-cub-like)` and
`PowerAwayFromTheRigYawsAndRollsTheRightWay` for both J-3s had been failing for days — the J-3 was the only type
still red — and `J3HandsOffModeProbe` above had traced the hands-off mode to the propeller disc's cross-flow terms
and noted "a real J-3 is directionally stable, so something here is too big". Something was: the aeroplane. At
1,220 lb the rig point is 56.3 KTAS and α 3.51°, where the disc's destabilising cross-flow force beats a fin whose
restoring moment follows ½ρV²; at the weight a J-3 is actually soloed at the same 75 % gives 62.6 KTAS and α
−0.21°, and the fin wins. RigTests went 276/279 to **279/279** with no change to the aero or propeller model at all.

Fin + = leading edge left. Aileron + = right-roll deflection. Elevator + = trailing edge down. 4,000 ft ISA.

| aircraft | power | rig speed KTAS / KIAS | α ° | elevator trim ° | aileron rig ° | fin offset ° | bank ° |
|---|---|---|---|---|---|---|---|
| AirCam (land) | 75 %, 2 pushers, both CW | 99.4 / 93.7 | −1.93 | +0.53 | +0.275 | +0.397 | +0.476 |
| AirCam amphib | 75 %, both CW | 97.1 / 91.5 | −0.99 | −0.24 | +0.286 | +0.432 | +0.407 |
| Boeing 737 | 75 % thrust, jet | 349.6 / 329.4 | −0.04 | +3.13 | 0 | 0 | 0 |
| Cessna 172 | 75 % | 123.4 / 116.3 | −0.10 | +1.65 | +0.175 | +0.144 | +0.200 |
| Cassutt F1 | 75 % | 159.1 / 149.9 | +0.63 | +0.45 | +0.332 | +0.161 | +0.279 |
| Cirrus SR22 | 75 % | 162.3 / 153.0 | −0.22 | +2.16 | +0.231 | +0.104 | +0.182 |
| DC-3 | 75 %, both CW | 152.7 / 143.9 | −0.89 | +0.62 | +0.155 | +0.137 | +0.106 |
| Decathlon | 75 % | 105.8 / 99.7 | +0.98 | +1.86 | +0.249 | +0.348 | +0.331 |
| Beaver floats | 75 % | 114.6 / 108.0 | +0.71 | +0.46 | +0.289 | +0.516 | +0.202 |
| Beaver | 75 % | 123.4 / 116.3 | −0.49 | +0.74 | +0.249 | +0.423 | +0.206 |
| Extra 300 | 75 % | 162.8 / 153.5 | +2.35 | −1.41 | +0.274 | +0.163 | +0.305 |
| F-86 Sabre | 75 % thrust, jet | 320.2 / 301.7 | +2.22 | +1.79 | 0 | 0 | 0 |
| Gee Bee R-2 | 75 % | 187.9 / 177.1 | −1.34 | −1.57 | +0.741 | +0.417 | +0.388 |
| Glasair III | 75 % | 235.3 / 221.8 | −0.38 | +0.24 | +0.438 | +0.131 | +0.310 |
| Great Lakes | 75 % | 106.7 / 100.6 | −2.36 | +0.98 | +0.472 | +0.358 | +0.343 |
| Hughes H-4 | 75 %, 8 × CW | 151.2 / 142.5 | −0.83 | −0.59 | +0.041 | +0.033 | +0.025 |
| J-3 Cub floats | 75 % | 65.6 / 61.9 | −0.00 | +0.55 | +0.148 | +2.314 | +0.008 |
| J-3 Cub | 75 % | 62.6 / 59.0 | −0.21 | +0.54 | +0.161 | +2.447 | +0.007 |
| JN-4 Jenny | 75 % | 51.3 / 48.3 | +2.74 | +0.02 | +0.487 | +0.722 | +0.217 |
| P-51D | 75 % | 209.3 / 197.2 | +0.41 | −1.33 | +0.494 | +0.194 | +0.229 |
| PA-18 bush | 75 % | 91.2 / 86.0 | −0.33 | +0.74 | +0.255 | +0.424 | +0.306 |
| PA-18 Cub | 75 % | 94.2 / 88.8 | −0.78 | +1.01 | +0.239 | +0.381 | +0.297 |
| PA-18 floats | 75 % | 87.1 / 82.1 | +0.76 | +0.16 | +0.279 | +0.458 | +0.280 |
| Pawnee | 75 % | 108.5 / 102.2 | −1.69 | +1.16 | +0.247 | +0.330 | +0.328 |
| PA-28 Archer | 75 % | 108.7 / 102.5 | +1.20 | +0.65 | +0.270 | +0.199 | +0.203 |
| Pitts S-2B | 75 % | 137.0 / 129.1 | +2.08 | −1.99 | +0.413 | +0.490 | +0.534 |
| Seminole | 75 %, counter-rotating | 143.1 / 134.9 | −0.14 | +1.72 | 0 | 0 | 0 |
| Stearman PT-17 | 75 % | 105.0 / 98.9 | −3.45 | −0.55 | +0.514 | +0.371 | +0.221 |
| Target drone | 75 % (it has a prop) | 159.1 / 149.9 | +0.63 | +0.45 | +0.332 | +0.161 | +0.279 |
| SGS 2-33 | glider, best glide | 41.0 / 38.6 | +4.25 | +0.47 | 0 | 0 | 0 |
| Grob 103 Acro | glider, best glide | 50.1 / 47.2 | +0.65 | +0.47 | 0 | 0 | 0 |
| EB29R | glider, best glide | 62.3 / 58.8 | +5.54 | −3.31 | 0 | 0 | 0 |
| Swift S-1 | glider, best glide | 62.7 / 59.1 | +4.62 | −1.30 | 0 | 0 | 0 |
| Hang glider | weight shift, hands-off speed | 21.4 / 20.2 | +8.98 | −15.51 (bar) | — | — | 0 |
| Paraglider | best glide | 19.7 / 18.6 | −0.49 | +9.51 (brakes) | — | — | 0 |

How each kind was handled:

* **Single-engine clockwise props** (all the singles, every one `rotationSign` +1): ailerons rigged for right roll
  against the torque reaction net of the stab-swirl roll; fin set leading-edge-left against spiral slipstream and the
  P-factor at the cruise α (small: α is near zero at 75 %).
* **Co-rotating multis** (DC-3, H-4, AirCam — every prop clockwise as modelled): the torques add, so they rig like a
  single. The H-4's rig is tiny because eight props' torque is small against its inertia and q·S·b.
* **Counter-rotating Seminole**: torque, P-factor and spiral-slipstream cancel engine for engine; the rig comes out
  −0.003° aileron and 0.000° fin — the −0.003° is the tail swirl, which the model takes from ONE centreline prop using
  the base config's rotation sign even on a twin with wing-mounted engines (a model limitation, reported, not fixed).
* **Jets** (737, F-86): no rotating-prop effects; symmetric, so both rig angles are zero to 10⁻¹⁵ and only the elevator
  trim is set. At 75 % thrust the 737 does 330 KIAS at 4,000 ft — legal nowhere below 10,000 ft and close to its
  Vmo; the F-86 302 KIAS.
* **Gliders, hang glider, paraglider**: no power effects; symmetric, so zero roll and yaw with neutral controls at best
  glide (hang glider: hands-off speed); the default trim is set for that speed.

## Validation against published data (validation only — nothing above was fitted to it)

* **Fin offset.** Published numbers are scarce. The P-51D is reported with a 1° fin offset; the Aeronca 11AC
  service manual sets the fin 3/8 in left at its leading edge (roughly 1° on that fin); Cub owners report an offset fin
  without a factory figure. The model asks for **0.13° on the P-51D** and **0.33° on the J-3** — about a third to a
  sixth of the published offsets. Two honest reasons: the real offsets are set for CLIMB (high power, low speed, where
  the slipstream yaw is several times the cruise value), so pilots trim the rest with rudder trim in cruise; and the
  model's spiral-slipstream yaw is a lumped `slipstreamK·P/V_slip` term (not swirl striking the fin), whose cruise
  value has never been calibrated against a measured trim. If Ty's cruise rudder trim on the Great Lakes is
  noticeably right-rudder at 75 %, `slipstreamK` is low.
* **Aileron rig.** No factory figure found; real aeroplanes usually leave this to a ground-adjustable tab bent by
  test flight. Sizes here (0.1–0.65°) are a few millimetres of trailing edge on a light aircraft's aileron, which is
  the size of a typical tab bend.
* **Rig speeds** are in the neighbourhood of the books' 75 % cruises. The book figures here are from memory, not a
  POH in hand, and are quoted at ~8,000 ft where 75 % is full throttle on a normally aspirated engine (so their TAS is
  higher than a 4,000 ft figure): 172 here 123 KTAS (book ≈ 120–124), SR22 163 (book ≈ 170–180), PA-28 Archer 109
  (book ≈ 120), J-3 **62.6 KTAS = 72.0 mph** (≈ 73 mph cruise), 2-33 best glide 39.5 KIAS (AFM 50 mph ≈ 43 kt). Treat
  as a sanity check only. The J-3's figure was 56.3 KTAS = 64.8 mph until the 2026-11-11 empty-weight re-solve above:
  a Cub authored at its 1,220 lb placard could not reach its own book cruise, and one authored at the weight it is
  actually soloed at lands on it — 72.0 against 73 mph, which is the closest agreement of any type on this list. That
  is one number and it was not fitted to, but it points the same way as the owner's "the j3 is noticibly slower" of
  2026-11-09.

## Verification

* **Hands off at the rig condition** (`RigTests.HandsOffLevelAtRigPower`, all 29 powered types): spawned at the
  solved state, stick on the trim, aileron and rudder centred, 75 %. After 20 s settle, over 40 s: rates ≤ 0.04 °/s
  (band 0.25 °/s), sideslip ≤ 0.007° (band 0.2°), altitude within 0.2 m (band 15 m), heading change ≤ 0.42°.
* **Gliders at best glide** (`HandsOffAtBestGlide`, 5 types): rates ≤ 0.004 °/s except the hang glider's pendulum
  (q 0.24 °/s, band 0.25), no roll or yaw.
* **Power away from the rig** (`PowerAwayFromTheRigYawsAndRollsTheRightWay`, 27 single-sense prop types): at the rig
  state, 100 % gives a LEFT rolling and yawing moment and 30 % a right one (moments), and flown for 3 s the heading and
  bank go left with more power and right with less. `CounterRotatingTwinBarelyFeelsPower`: the Seminole's ΔL, ΔN for
  75 → 100 % are 3.8 / 0.0 N·m against −71.6 / −95.0 N·m with both props turning the same way.
* **Off the rig altitude** (probe, `RIG_PROBE=1`, the rig held fixed): at 1,500 ft the same 75 % gives a higher EAS, so
  the fixed rig over-corrects and the aeroplane wanders right; at 6,500 ft, left. Typical: 172 ±8° of heading in 40 s,
  Cubs and PA-28 ±16–20°, DC-3 ±3°. Spirally divergent types run away: J-3 floats and Jenny ~140–170°, Pitts and
  Cassutt ~50°, and the Gee Bee departs at 1,500 ft within the minute. That is a fixed rig doing what a fixed rig does;
  the island starts at 1,500 and 6,500 ft will show it.

## Preset aileron trim for a start away from the rig altitude (2026-11-10)

`RigSolver.AileronTrimForAltitude(config, altitudeM, out why)`, held by `AirStart.Compute` on the aileron and by
`AirStart.HeldInputs`.

**The problem is the one the probe above describes, measured in a shape that matters.** A fixed rig works through
dynamic pressure, so away from 4,000 ft it over- or under-corrects and leaves a rolling moment nothing trims out.
`lane/claude-start-altitude` found it when Ty asked for a start-altitude picker (2026-11-03): hands off for 10 s at
10,000 ft AGL over KMYL the Gee Bee rolled 12.7°, the Glasair 9.9°, the Cassutt 8.6°. **Re-measured on 2026-11-10,
after all 26 fins moved to `fin-lowAR` and twelve types got flat-plate horizontal tails, it is worse** — still air, no
terrain, 10 s hands off, peak |bank change|:

| | at the rig altitude | 3,000 ft AGL | 6,000 | 10,000 |
|---|---|---|---|---|
| Gee Bee R2 | 1.40° | 5.43° | 9.16° | **15.05°** |
| Glasair III | 3.82° | 7.26° | 10.44° | **15.27°** |
| Cassutt | 2.33° | 5.23° | 7.93° | **12.06°** |
| P-51D | 2.31° | 3.99° | 5.53° | 8.07° |
| C-172 | 2.33° | 2.98° | 3.49° | 4.31° |

The rig-altitude column is the floor: what each type drifts even where its rig was solved, because the stored rigs no
longer close there either. Only the growth above it belongs to the altitude.

Ty, offered this or re-solving the whole rig at the start altitude, chose **preset aileron trim**.

**The derivation is a difference of two measurements of the aeroplane, and nothing is typed:**

```
preset = AileronForZeroRoll(at the start height) − AileronForZeroRoll(at the rig height)
```

`RigSolver.AileronForZeroRoll` is a **1-D secant on L(δa)** through `SteadyForces` at an already-solved level state:
the aileron that zeroes the rolling moment, nothing else moved. The rig-height term is cached per config, so only the
first in-air start of a type pays for the extra `SolveLevel` behind it, and the rig-altitude case short-circuits
before any solve at all.

**Why 1-D, when the obvious thing is to re-solve the rig at the start height.** That is what this was first written
as, and it had to be thrown away: `SolveRig` is six unknowns with a 6×6 finite-difference Jacobian, and it measured
**16.1× the cost of the `SolveLevel` a spawn already pays** (3,189 ms against 198 ms on a loaded machine — the
absolutes are contention-dependent, the ratio is not). `AirStart.ComputeRandom` can call `Compute` five times while
it hunts for a separated multiplayer start, so that is up to five of them on one spawn, and a spawn hitch is
something the pilot feels on every flight and reads as a different bug. The 1-D version, with `AirStart.Compute`
passing in the `SolveLevel` result it already has, costs **0.126×** of that same solve.

It is also a more direct answer to "the minimum that cancels the measured drift": it cancels exactly the rolling
moment and touches nothing else, where a rig re-solve would also move the fin offset, the bank, α and the speed to
get there.

**Why a DIFFERENCE and not just the first term.** The stored rigs do not close on today's model — the Extra leaves
L = −1.3 × 10⁻² and the C-172 −7.9 × 10⁻³ at their own 4,000 ft rig point — so the first term alone is **not zero at
the rig altitude**, which is exactly the case that has to stay centred, and it would fold "this config's rig is
stale" into "this start is high". For the same reason it is not measured against `c.Rig.AileronRad`, a hand-copied
literal that is a few hundredths of a degree out on most types (Extra 0.0394°, C-172 0.0322°, Gee Bee 0.0271°).

**Measured, it removes the drift that GROWS WITH HEIGHT — which is the part a fixed rig causes and the only part it
can remove.** What is left is whatever each type already drifts at the rig altitude itself, because the stored rigs
are wrong there too, and nothing a preset does at 14,699 ft can fix that. 10 s hands off, still air, over KMYL:

| | at the rig altitude | 10,000 ft AGL, no preset | with the preset | preset | growth removed |
|---|---|---|---|---|---|
| Gee Bee R2 | 1.40° | 15.05° | **0.45°** | 0.0848° | **107 %** |
| Glasair III | 3.82° | 15.27° | 4.45° | 0.0573° | 94 % |
| Cassutt | 2.33° | 12.06° | 2.76° | 0.0480° | 96 % |
| P-51D | 2.31° | 8.07° | 2.93° | 0.0595° | 89 % |
| C-172 | 2.33° | 4.31° | 2.67° | 0.0228° | 82 % |

The Gee Bee's 107 % is not a typo: with the preset on it drifts **less** at 14,699 ft than it does at 4,000 ft.
`PresetAileronTrimTests.ThePresetRemovesTheDriftThatGrowsWithHeight` gates 75 %, and refuses to pass a case with
less than half a degree of growth to remove, so no type can satisfy it trivially.

**How small it is, said three ways, because the obvious one misleads.** In degrees: under **0.06°** of aileron over
the whole reachable band, against 20° of full deflection. In stick units it reads about **0.034**, which is not the
preset being bigger than it looks — every aileron axis in the fleet has a dead zone of 0.02–0.05 and `ShapeAxis`
returns exactly zero inside it, so the stick that delivers ANY non-zero deflection must sit just past that edge.
**0.034 against a 0.03 dead zone is four thousandths past it**: a value a hand on a stick could not hold if it tried.
On the AIL TRIM bar, which is what the pilot sees, that is under a twentieth of its half-width off centre.

**WHERE THIS FIGHTS "TRIMS CENTRED EVERY FLIGHT", which is Ty's own rule and the stick-free launch fix.** The rule
says a flight begins with the trims centred, so no leftover trim flies the first frame and the aeroplane a player
meets is the same every time. **A preset aileron trim is a non-centred trim at the start. That is a breach, and Ty
chose it knowing** — the lane that found the drift flagged the conflict in those words and he answered "preset
aileron trim" anyway. Three things keep it narrow, each with a test in `tools/FlightTests/PresetAileronTrimTests.cs`:

1. **Exactly zero at the rig altitude** — `0.0`, not "small" — for every type, in the plan, the deflections and the
   held inputs. `LaunchReplicationTests` and `HandsOffLaunchSpiralTests` both spawn at `RigSolver.RigAltitudeM`, so
   every launch they check is bit-identical to one from before this existed.
2. **A pure function of the start, not a memory of the last flight** — which is the half of the rule that actually
   mattered. Two starts at the same height in the same air get the same preset to the bit.
3. **Too small to see**: under 0.1° of aileron anywhere the picker reaches, a stick input under 0.01 of full travel.
   The trim indicator does not visibly leave centre.

**Only the aileron.** The fin offset drifts with height too — more than the aileron on several types — and cancelling
it would want preset rudder trim as well. Ty was offered "aileron/rudder trim" and chose "preset aileron trim", so
the yaw keeps whatever small heading drift the fixed fin leaves.

**SUPERSEDED, 2026-11-10 (the section below is kept for the measurements, not the diagnosis).** The four types it
describes were not failing because their stored rigs were stale — the rig was being SOLVED against a propeller the
sim does not fly, and the re-solve at the top of this document is the fix. `HandsOffLevelAtRigPower` is down from
five red to one (the J-3 alone), and the preset aileron trim can be reconsidered for the three that now pass.

**IT CANNOT SAVE FOUR TYPES, AND THEY ARE A DIFFERENT FAULT.** The Pitts, the Extra, the Great Lakes and the J-3
roll tens of degrees in ten seconds hands off at **every** height, the rig altitude included — the Pitts **36.1° at
4,000 ft**, 43.1° at 3,000 AGL and 61.6° at 10,000; the Extra **24.0°**, 31.1° and 52.4° — because their stored rigs
no longer close against today's model: the Pitts leaves N = −2.6 × 10⁻² and the Extra N = +7.3 × 10⁻³ at their own
4,000 ft rig point, where `RigTests.HandsOffLevelAtRigPower` demands under 2 × 10⁻⁴. Their config rig angles also
disagree with the hand-copied table above, so `RigTests.ConfigRigMatchesTheHandCopiedTable` is red for **seven**
types — all of them ones whose tails changed. **RigTool was re-run and the literals were not re-copied, and the rigs
it produced do not close against the model that flies now**, so re-copying the literals is not the fix. Re-running it
for those seven and re-copying in the same commit belongs to whoever changed the tails.

An aileron preset derived from a rig that does not close has nothing to work with, and measured it barely helps them
(Pitts 61.6° → 56.6°), so those four are deliberately **excluded** from the gating test rather than covered by a
widened band: a band loose enough to pass them would pass a preset that did nothing.

**And the J-3 is worse than that: its rig solve does not converge at all away from the rig altitude.** At 3,000,
6,000 and 10,000 ft AGL `SolveRig` returns no answer, so `AileronTrimForAltitude` leaves the trim centred and sets
its `why` string — the graceful-degradation path firing for real rather than in a test. That is also on the rig pile,
not this one.

## Stick-fixed, stick-free, and why one launch went left (run 2, 2026-11-02)

**The rig is a stick-fixed rig.** `RigSolver.Residual` (`src/FlyingGame.Sim/RigSolver.cs`) evaluates `SteadyForces` with
explicit `ControlDeflections(0, δe, 0, 0)` — aileron and rudder at neutral, the elevator at the solved trim — straight
into `AeroModel.Compute`; nothing in the solver calls `Aircraft.FreeFloat` or `HingeTerms`. `RigTool` only calls the
solver. `RigTests.Fly` steps with the four-argument `ControlInputs`, whose free flags are all false
(`ControlInputs.cs`), so the free-float branch in `Aircraft.Step` never runs there. Nothing was re-solved; no rig angle
changed in run 2.

**Stick-free has the same equilibrium, by construction.** The game flies stick-FREE whenever the pads (or a gamepad at
rest) are let go: `PilotControls.Compose` sets the free flags, and on a reversible axis the surface trails to its float
angle. On the first step after spawn `Aircraft.CalibrateTabs` sets each trim tab so that, in the spawn's own flow, the
free surface floats exactly at the deflection the aircraft was built with — the rig point. So stick-free and
stick-fixed share the trim; what differs is the STABILITY. A free rudder trails with sideslip (Pitts: Chα −0.2,
Chδ −0.45, no centring spring, so it floats at ~0.44 of the local flow angle), which takes a large bite out of the
directional stability, and the spiral mode — already slowly divergent on the Pitts — gets much faster.

**What makes two launches differ (measured, `LaunchProbe`, LAUNCH_PROBE=1/2/3):**

* Inside the sim, nothing: the in-air start is a pure function of the config, the start and the air; the sim clock
  (`Atmosphere.SimTimeSec`, never reset between flights) does not enter it even with turbulence on (the turbulence
  is a frozen spatial field); there is no random seed anywhere on the path. Same start, same air: the same launch to
  the bit. NB the regional atmosphere never loads on the island (`regional-atmosphere.json` anchors a region to
  `airport:Bench`, which the island does not have, so `AtmosphereField.ResolveAirport` throws and SessionSettings falls
  back): free flight on the island flies the UNIFORM air — the menu's wind (default 5 m/s from 090), its turbulence
  step, the thermals and the glacier wind. Reported, not changed here.
* Outside it, three carry-overs the Unity side had, each of which flips the direction on its own (Pitts, still air;
  bank after 60 s, stick-fixed / stick-free):

  | first moments of the flight | at the PHDH air start, 1,514 ft | at the rig altitude, 4,000 ft |
  |---|---|---|
  | nothing (hands off) | **+20.6° / +35.2°** (slow right: 75 % at 1,514 ft is a higher EAS than the rig's) | +0.57° / +0.54° |
  | 0.05 s (10 steps) of full LEFT stick | −12.5° / −24.3° | −19.4° / −26.1° |
  | 0.05 s of the cold-start inputs (`ControlInputs.Neutral`: 50 % power, elevator 0, held) | +22° / (dives by 50 s) | +2.0° / **+32°** |
  | aileron trim left over at 5 % left | −50° in 10 s | −52° in 10 s |
  | gamepad stick resting 10 % off centre (just past its 7 % dead zone) | −28° in 10 s | −30° in 10 s |

  1. **The first frame flew the previous flight's inputs.** `FlightSimDriver.ResetFlight` rebuilt the aeroplane but
     left `Inputs` alone, and `TouchFlightControls.Update` stops publishing while the menu is open, so `Inputs` was
     whatever the stick, pad and power were doing when the menu opened (on a cold start, `Neutral`). Nothing orders
     the driver's `Update` before the controls', and the first frame after a respawn is a long one, capped at
     `SimLoop.MaxCatchUpSec` = 0.05 s: up to ten steps of the last flight's stick on the new one.
     **Fix:** `FlightSimDriver.Spawn` puts `Inputs` on the start's hands-off trim (stick and rudder centred,
     elevator on the trim, the start's power; held) for every start.
  2. **Aileron and rudder trim survived from flight to flight** (and from one aeroplane to the next) —
     `PresetTrim` reset only the pitch trim, and with a gamepad connected the trim bars are not even drawn.
     **Fix:** `PresetTrim` centres both.
  3. **A pad drifting past its dead zone holds the stick off centre.** Not a code fault — a pad at rest inside its
     7 % zone hands the sim exactly zero, so it lets go exactly like a lifted finger — but a worn stick that rests
     past 7 % flies a small, held deflection. Watch for it.

  And beyond those, the airframe: a twentieth of a second of stick is enough to choose the spiral's direction for
  the next minute, stick-fixed, and more so stick-free (the same 0.05 s perturbation at the rig point grows to 2°
  stick-fixed, 32° stick-free). That is the Pitts, and it is the same every launch.

**Where the Pitts air start is:** the default airport's authored in-air start, PHDH, 461 m = **1,514 ft** MSL
(437 m above the ground under it). The rig is set at 4,000 ft. Hands-off at 1,514 ft, in still air, the Pitts
banks slowly right — stick-fixed +1.8° at 10 s, +9.0° at 40 s, +20.6° at 60 s; stick-free +2.0°, +13.3°, +35.2°: the
fixed rig over-correcting at the lower height plus the spiral. Every launch does that, to the bit. A LEFT turn
there is not the airframe: it is something the first moments of the flight put in.

**The game path, engine-free:** `FlyingGame.Sim.AirStart` (the in-air start, lifted out of `FlightSimDriver.Spawn`)
and `FlyingGame.Sim.PilotControls` (the hands → `ControlInputs` composition and free rule, lifted out of
`TouchFlightControls.PublishToDriver`). `LaunchReplicationTests` flies five consecutive launches of every type
through them, at 60 fps through `SimLoop.Advance`, the sim clock running on, launches 2–5 with a gamepad at rest
(seeded noise inside its dead zones), stick-free ON and OFF.
