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

**The in-air start** (`FlightSimDriver.Spawn`, default branch) now calls `RigSolver.SolveLevel` (rig held: speed, α,
elevator, bank) at the start's own altitude, spawns there with the trim slider on that elevator, and the touch
throttle preset for air starts is 75 % (`TouchFlightControls.PresetTrim`, was 50 % with a POWER-OFF glide trim —
which is why the old air start pitched up). Gliders start at best glide (hang glider: its hands-off speed). The other
starts (final, thermal, STOL, practice, challenges) are unchanged: they were already on neutral aileron and rudder and
trim the elevator for their own condition.

## Result (copied into `RigTests.RigTable`)

Fin + = leading edge left. Aileron + = right-roll deflection. Elevator + = trailing edge down. 4,000 ft ISA.

| aircraft | power | rig speed KTAS / KIAS | α ° | elevator trim ° | aileron rig ° | fin offset ° | bank ° |
|---|---|---|---|---|---|---|---|
| AirCam (land) | 75 %, 2 pushers, both CW | 99.4 / 93.7 | −1.93 | +0.20 | +0.247 | +0.239 | +0.48 |
| AirCam amphib | 75 %, both CW | 97.1 / 91.5 | −0.99 | −0.51 | +0.257 | +0.255 | +0.41 |
| Boeing 737 | 75 % thrust, jet | 350.2 / 330.0 | −0.05 | +3.88 | 0 | 0 | 0 |
| Cessna 172 | 75 % | 123.4 / 116.3 | −0.11 | +1.57 | +0.133 | +0.124 | +0.20 |
| Cassutt F1 | 75 % | 159.2 / 150.0 | +0.65 | +0.45 | +0.293 | +0.114 | +0.29 |
| Cirrus SR22 | 75 % | 162.5 / 153.1 | −0.21 | +2.07 | +0.197 | +0.095 | +0.19 |
| DC-3 | 75 %, both CW | 152.5 / 143.7 | −0.83 | +0.43 | +0.127 | +0.079 | +0.11 |
| Decathlon | 75 % | 107.8 / 101.6 | +0.84 | +2.04 | +0.196 | +0.205 | +0.35 |
| Beaver floats | 75 % | 114.5 / 107.9 | +0.72 | +0.60 | +0.254 | +0.286 | +0.21 |
| Beaver | 75 % | 123.3 / 116.2 | −0.48 | +0.72 | +0.219 | +0.241 | +0.21 |
| Extra 300 | 75 % | 160.0 / 150.8 | +1.90 | −1.05 | +0.197 | +0.168 | +0.41 |
| F-86 Sabre | 75 % thrust, jet | 320.5 / 302.0 | +2.22 | +2.63 | 0 | 0 | 0 |
| Gee Bee R-2 | 75 % | 188.1 / 177.3 | −1.29 | −2.03 | +0.654 | +0.311 | +0.42 |
| Glasair III | 75 % | 235.9 / 222.3 | −0.38 | +0.08 | +0.380 | +0.093 | +0.32 |
| Great Lakes | 75 % | 108.6 / 102.3 | −2.44 | +0.84 | +0.376 | +0.362 | +0.40 |
| Hughes H-4 | 75 %, 8 × CW | 150.7 / 142.0 | −0.59 | −0.35 | +0.030 | +0.017 | +0.03 |
| J-3 Cub floats | 75 % | 55.5 / 52.3 | +4.39 | −1.88 | +0.330 | +0.456 | +0.38 |
| J-3 Cub | 75 % | 65.5 / 61.8 | +1.21 | −0.10 | +0.229 | +0.327 | +0.35 |
| JN-4 Jenny | 75 % | 52.5 / 49.5 | +2.39 | +0.31 | +0.397 | +0.589 | +0.26 |
| P-51D | 75 % | 209.4 / 197.4 | +0.47 | −1.43 | +0.433 | +0.128 | +0.24 |
| PA-18 bush | 75 % | 92.6 / 87.2 | −0.46 | +0.75 | +0.197 | +0.235 | +0.31 |
| PA-18 Cub | 75 % | 95.7 / 90.1 | −0.91 | +0.96 | +0.185 | +0.220 | +0.31 |
| PA-18 floats | 75 % | 88.3 / 83.2 | +0.60 | +0.28 | +0.217 | +0.260 | +0.29 |
| Pawnee | 75 % | 110.3 / 103.9 | −1.81 | +0.98 | +0.194 | +0.211 | +0.37 |
| PA-28 Archer | 75 % | 108.9 / 102.6 | +1.21 | +0.59 | +0.242 | +0.187 | +0.22 |
| Pitts S-2B | 75 % | 139.3 / 131.3 | +1.98 | −1.11 | +0.272 | +0.517 | +0.59 |
| Seminole | 75 %, counter-rotating | 143.2 / 134.9 | −0.03 | +1.59 | −0.003 | −0.000 | 0.00 |
| Stearman PT-17 | 75 % | 107.8 / 101.6 | −3.56 | −0.92 | +0.375 | +0.272 | +0.28 |
| Target drone | 75 % (it has a prop) | 159.2 / 150.0 | +0.65 | +0.45 | +0.293 | +0.114 | +0.29 |
| SGS 2-33 | glider, best glide | 41.9 / 39.5 | +3.84 | +0.74 | 0 | 0 | 0 |
| EB29R | glider, best glide | 62.4 / 58.8 | +5.51 | −3.28 | 0 | 0 | 0 |
| Swift S-1 | glider, best glide | 62.9 / 59.3 | +4.59 | −1.08 | 0 | 0 | 0 |
| Hang glider | weight shift, hands-off speed | 21.4 / 20.2 | +8.98 | −15.51 (bar) | — | — | 0 |
| Paraglider | best glide | 19.7 / 18.6 | −0.49 | +9.53 (brakes) | — | — | 0 |

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
  (book ≈ 120), J-3 66 KTAS (≈ 73 mph cruise), 2-33 best glide 39.5 KIAS (AFM 50 mph ≈ 43 kt). Treat as a sanity
  check only.

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
