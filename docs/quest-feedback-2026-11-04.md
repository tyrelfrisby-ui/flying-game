# Quest feedback 2026-11-04 (Ty, Quest 3S build 2026.1104.1656): views, wrists, arm clearance, prop blast, prop/mixture

Lane `lane/claude-quest-feedback` (Claude). Nothing here is approved: every item needs Ty's fly-test.

## 1. Views — "no views are available except 'outside' which is actually cockpit"
Root causes:
- `QuestMenu.cs` (View row, old line 238 / `ToggleView` old 745-749): the only view control in the headset was a
  two-way toggle Cockpit <-> RelativeWind, labelled from the per-frame static `ChaseCamera.InCockpit`, so there were
  no external views to choose beyond one.
- `QuestRig.CaptureOrigin` (old lines 274-281): an outside view used ChaseCamera's EXACT flight-path camera as the
  origin with its heading, so every swing of the path translated the head sideways and every heading change rotated
  the world round it. I could not reproduce "Outside = cockpit" in the editor rig (QuestViewShots drives the real
  ChaseCamera + QuestRig per view and finds the camera 17-118 m outside); the device logcat from Ty's session had
  already rolled over. The new `[Quest] view:` log line will say which view and origin the headset actually used.
Fix: `QuestViews` (Cockpit, Chase, Fixed-direction chase, Fly-by; airframe-bolted views left out), `< >` cycles them
and the row reads `ChaseCamera.CurrentView`; Core `QuestOutsideView`: origin rides the aeroplane exactly, offset eased
(tau 0.8 s), heading only in 45 deg snap turns, fly-by world-fixed, never pitch or roll, 1.5 m floor; fly-by parks
closer in a headset (`ChaseCamera.CompactFlyby`). Renders: `agent-lanes/renders/quest-feedback/views/`.

## 2/3. Wrists and arm clearance
Root causes:
- `VrCockpitHands.Arm` (old 522-537): wrist put on the grip->shoulder line, hand aimed along it, roll from the
  controller's own rotation; the forearm comes from the elbow, well off that line -> 30-99 deg wrist bends.
- `VrPilotBody.PoseSkinned`/`Limb`: shortest-arc `FromToRotation` per bone -> arbitrary upper-arm roll (elbow hinging off
  its plane) and all palm roll at the wrist joint (candy-wrapper).
- One fixed elbow pole (`SeatedPilot.ElbowPoleL/R`, 45 cm outboard of the eye) for every cockpit -> elbows into the
  sidewall / Great Lakes left switch box. No collision of any kind.
Fix: Core `ArmPose` (hand wraps the control's own grip axis: stick shaft; throttle knob across its lever hinge, thumb
inboard; wrist a fist behind; elbow swivel searched +-80 deg against the cockpit), `VrArmClearance` (airframe triangles
round the seat voxelised at 2.5 cm, unsigned distance), `VrPilotBody.ArmLimb` (upper arm placed by direction and its
rest bend, forearm a pure hinge then 75 % of the hand's roll, optional ForearmTwist bones), palm from the glove's own
knuckles. Codex built a v4 body candidate with forearm twist bones (NOT shipped; renders in
`renders/quest-feedback/codex-v4-*.png`, GLB in `renders/quest-feedback/v4-candidate/`).

Per-aircraft penetration (mm, deepest arm capsule point inside the structure, exact triangle distances) and wrist bend
(deg), before -> after, VrArmCheck (`logs/arms/table.md`; renders `agent-lanes/renders/quest-feedback/arms/`):

| aircraft | seat | pose | L before mm | L after mm | R before mm | R after mm | wrist L before/after ° | wrist R before/after ° | swing L/R ° |
|---|---|---|---|---|---|---|---|---|---|
| great-lakes-like | rear | neutral, throttle idle | 50 | 42 | 9 | 0 | 80/97 | 48/5 | 0/50 |
| great-lakes-like | rear | neutral, throttle full | 47 | 0 | 9 | 0 | 62/2 | 48/5 | 80/50 |
| great-lakes-like | rear | stick full left + aft | 46 | 2 | 50 | 0 | 72/2 | 39/7 | 80/50 |
| great-lakes-like | rear | stick full right + fwd | 46 | 2 | 0 | 0 | 72/2 | 36/57 | 80/-80 |
| great-lakes-like | front | neutral, throttle idle | 49 | 0 | 0 | 0 | 99/3 | 58/1 | 80/0 |
| great-lakes-like | front | neutral, throttle full | 46 | 0 | 0 | 0 | 79/2 | 58/1 | 80/0 |
| great-lakes-like | front | stick full left + aft | 49 | 0 | 6 | 0 | 90/3 | 46/1 | 80/20 |
| great-lakes-like | front | stick full right + fwd | 49 | 0 | 17 | 0 | 90/3 | 49/1 | 80/-20 |
| extra-300-like | rear | neutral, throttle idle | 46 | 18 | 16 | 4 | 60/11 | 67/22 | 20/0 |
| extra-300-like | rear | neutral, throttle full | 48 | 0 | 16 | 4 | 43/1 | 67/22 | 30/0 |
| extra-300-like | rear | stick full left + aft | 47 | 3 | 33 | 0 | 53/1 | 68/25 | 30/30 |
| extra-300-like | rear | stick full right + fwd | 47 | 3 | 16 | 15 | 53/1 | 60/22 | 30/-20 |
| extra-300-like | front | neutral, throttle idle | 49 | 4 | 15 | 10 | 72/2 | 55/23 | 60/-20 |
| extra-300-like | front | neutral, throttle full | 48 | 3 | 15 | 10 | 57/2 | 55/23 | 50/-20 |
| extra-300-like | front | stick full left + aft | 49 | 5 | 26 | 0 | 65/10 | 59/2 | 60/40 |
| extra-300-like | front | stick full right + fwd | 49 | 5 | 0 | 6 | 65/10 | 47/15 | 60/-40 |
| pitts-s2b-like | rear | neutral, throttle idle | 35 | 0 | 0 | 0 | 78/70 | 46/1 | 70/0 |
| pitts-s2b-like | rear | neutral, throttle full | 17 | 0 | 0 | 0 | 63/10 | 46/1 | 50/0 |
| pitts-s2b-like | rear | stick full left + aft | 26 | 0 | 23 | 8 | 71/55 | 49/9 | 60/-10 |
| pitts-s2b-like | rear | stick full right + fwd | 26 | 0 | 14 | 0 | 71/55 | 31/1 | 60/-20 |
| pitts-s2b-like | front | neutral, throttle idle | 0 | 0 | 8 | 0 | 82/90 | 44/8 | 0/10 |
| pitts-s2b-like | front | neutral, throttle full | 0 | 0 | 8 | 0 | 67/2 | 44/8 | 0/10 |
| pitts-s2b-like | front | stick full left + aft | 0 | 0 | 21 | 6 | 75/2 | 46/2 | 0/-20 |
| pitts-s2b-like | front | stick full right + fwd | 0 | 0 | 2 | 0 | 75/2 | 30/1 | 0/-10 |
| glider-2-33-like | front | neutral, throttle idle | 23 | 0 | 4 | 0 | 30/30 | 51/9 | 20/-20 |
| glider-2-33-like | front | neutral, throttle full | 23 | 0 | 4 | 0 | 30/30 | 51/9 | 20/-20 |
| glider-2-33-like | front | stick full left + aft | 23 | 0 | 0 | 0 | 30/30 | 49/1 | 20/0 |
| glider-2-33-like | front | stick full right + fwd | 23 | 0 | 29 | 0 | 30/30 | 29/1 | 20/-30 |
| glider-2-33-like | rear | neutral, throttle idle | 0 | 0 | 0 | 0 | 30/30 | 49/1 | 0/0 |
| glider-2-33-like | rear | neutral, throttle full | 0 | 0 | 0 | 0 | 30/30 | 49/1 | 0/0 |
| glider-2-33-like | rear | stick full left + aft | 0 | 0 | 0 | 0 | 30/30 | 51/1 | 0/0 |
| glider-2-33-like | rear | stick full right + fwd | 0 | 0 | 0 | 0 | 30/30 | 24/1 | 0/0 |
| j3-cub-like | rear | neutral, throttle idle | 48 | 0 | 0 | 0 | 52/9 | 44/3 | 30/0 |
| j3-cub-like | rear | neutral, throttle full | 46 | 0 | 0 | 0 | 35/6 | 44/3 | 30/0 |
| j3-cub-like | rear | stick full left + aft | 50 | 0 | 0 | 0 | 44/8 | 48/6 | 30/0 |
| j3-cub-like | rear | stick full right + fwd | 50 | 0 | 0 | 0 | 44/8 | 10/0 | 30/0 |
| j3-cub-floats-like | rear | neutral, throttle idle | 48 | 0 | 0 | 0 | 52/9 | 44/3 | 30/0 |
| j3-cub-floats-like | rear | neutral, throttle full | 46 | 0 | 0 | 0 | 35/6 | 44/3 | 30/0 |
| j3-cub-floats-like | rear | stick full left + aft | 50 | 0 | 0 | 0 | 44/8 | 48/6 | 30/0 |
| j3-cub-floats-like | rear | stick full right + fwd | 50 | 0 | 0 | 0 | 44/8 | 10/0 | 30/0 |

Still red after the fix (VrArmCheck FAIL, 8): Great Lakes rear throttle at idle (left arm 42 mm, wrist 97 deg),
Great Lakes rear stick full right+fwd (right wrist 57 deg), Extra rear throttle idle (left arm 18 mm), Pitts rear/front
throttle aft (left wrist 55-90 deg). These are the knob right back by the hip: the arm folds and no swing clears it.

## 4. Prop blast keyed to throttle
Root cause: `Aircraft.TailSlipstream` (Aircraft.cs, `Step` old line 1114, function old 1405-1416) ran `PropModel.Compute`
on the throttle alone and never looked at `_noseLost`; a single's prop strike (`LoseComponent(Propeller)` ->
`_noseLost`) zeroed `PropulsionForces` but left the wash over the rudder at full power. Measured on base 73d7702b:
Extra, prop stopped, full right rudder, 3 s on the ground: **-127.8 deg of heading at full throttle vs -0.08 at idle**.
Branch: both -0.0845 deg (identical to 9 places).

Throttle-keyed effects audit:
| effect | where | was keyed to | now |
|---|---|---|---|
| tail slipstream (rudder/elevator blast) + swirl | Aircraft.TailSlipstream | throttle, ignored prop strike | real drive: gone=0, windmill=small wake deficit, running unchanged |
| thrust | PropModel.Compute | throttle (zeroed for nose lost) | drive (dead: windmill/stopped drag only) |
| torque roll | PropModel.Compute | MaxPower x throttle | real shaft power (0 dead) |
| P-factor | PropModel.Compute | thrust | running engine only |
| spiral slipstream yaw | PropModel.Compute | MaxPower x throttle | real shaft power |
| gyroscopic precession | PropModel.GyroMoment | rpm from throttle | prop's real rpm (windmill/0) |
| multi-engine failed mounts in tail slipstream | Aircraft.TailSlipstream | counted at full | ThrottleScale-weighted |
| engine rpm (gauges, prop disc, audio) | Aircraft.EngineRpm | throttle | windmill rpm when dead; governor; mixture |
| fuel flow | Aircraft.FuelFlowKgPerSec | throttle | 0 dead; mixture-metered; governor |
| prop-strike engine torque | AircraftContactDamage.PropStrike | throttle | ShaftPower01 |
| engine sound | FlightAudio.PushTelemetry | throttle + rpm | ShaftPower01, rpm 0 when not running |
| smoke flow / exhaust jet | SmokeController, AeroWidget | throttle, rpm>100 | EngineRunning + ShaftPower01 |
| manifold pressure gauge | CockpitRigState.ManifoldPressureInHg | throttle if rpm>50 | ambient when not running |
| oil temp / CHT targets | CockpitRigState.FillEngineGauges | throttle | ShaftPower01, cool when not firing |
| Aero Widget prop vectors | WidgetVectors | throttle | live: the aircraft's Drive |
| HUD THR %, VR power readout | HudOverlay, QuestMenu | throttle | unchanged: they show the LEVER (correct) |
| AI / formation / tow | FormationPilot, TowController | throttle | unchanged: commands, not effects |
| trim solver | TrimSolver/RigSolver | throttle (running) | unchanged (solves a running engine) |

## 5. Prop and mixture
Great Lakes: config `great-lakes-like` is a 2T-1A-2 with a 180 hp IO-360 and a Hartzell HC-C2YK constant-speed prop
(TCDS A18EA, `docs/models/great-lakes-own-PROVENANCE.md`), `constantSpeed: true`, governed 2700: so it HAS a prop lever.
Root cause: the sim had no governor or mixture input; `CockpitRigState.Fill` (old 181-182) drew PropLever = 1 and
Mixture = 1 every frame; the VR panel levers were "visual only" (`VrCockpitHands.Panel.Apply`).
Fix: `Aircraft.PropLever` (governor 0.7 MaxRpm .. governedRpm; lever full forward = the old law; power scales with
governed rpm), `Aircraft.MixtureLever` with Core `MixtureModel` (phi from lever and density; full rich = unchanged
power at every height; best power ~phi 1.13; rough below 0.75; quits below 0.6; idle cut-off <= 0.05; relight after
1 s when fuel returns, a starter assumed), EGT, roughness, fuel flow. VR levers drive the sim; the cockpit draws the
sim's levers.

PROP_| aircraft | seat | pose | L before mm | L after mm | R before mm | R after mm | wrist L before/after ° | wrist R before/after ° | swing L/R ° |
|---|---|---|---|---|---|---|---|---|---|
| great-lakes-like | rear | neutral, throttle idle | 50 | 42 | 9 | 0 | 80/97 | 48/5 | 0/50 |
| great-lakes-like | rear | neutral, throttle full | 47 | 0 | 9 | 0 | 62/2 | 48/5 | 80/50 |
| great-lakes-like | rear | stick full left + aft | 46 | 2 | 50 | 0 | 72/2 | 39/7 | 80/50 |
| great-lakes-like | rear | stick full right + fwd | 46 | 2 | 0 | 0 | 72/2 | 36/57 | 80/-80 |
| great-lakes-like | front | neutral, throttle idle | 49 | 0 | 0 | 0 | 99/3 | 58/1 | 80/0 |
| great-lakes-like | front | neutral, throttle full | 46 | 0 | 0 | 0 | 79/2 | 58/1 | 80/0 |
| great-lakes-like | front | stick full left + aft | 49 | 0 | 6 | 0 | 90/3 | 46/1 | 80/20 |
| great-lakes-like | front | stick full right + fwd | 49 | 0 | 17 | 0 | 90/3 | 49/1 | 80/-20 |
| extra-300-like | rear | neutral, throttle idle | 46 | 18 | 16 | 4 | 60/11 | 67/22 | 20/0 |
| extra-300-like | rear | neutral, throttle full | 48 | 0 | 16 | 4 | 43/1 | 67/22 | 30/0 |
| extra-300-like | rear | stick full left + aft | 47 | 3 | 33 | 0 | 53/1 | 68/25 | 30/30 |
| extra-300-like | rear | stick full right + fwd | 47 | 3 | 16 | 15 | 53/1 | 60/22 | 30/-20 |
| extra-300-like | front | neutral, throttle idle | 49 | 4 | 15 | 10 | 72/2 | 55/23 | 60/-20 |
| extra-300-like | front | neutral, throttle full | 48 | 3 | 15 | 10 | 57/2 | 55/23 | 50/-20 |
| extra-300-like | front | stick full left + aft | 49 | 5 | 26 | 0 | 65/10 | 59/2 | 60/40 |
| extra-300-like | front | stick full right + fwd | 49 | 5 | 0 | 6 | 65/10 | 47/15 | 60/-40 |
| pitts-s2b-like | rear | neutral, throttle idle | 35 | 0 | 0 | 0 | 78/70 | 46/1 | 70/0 |
| pitts-s2b-like | rear | neutral, throttle full | 17 | 0 | 0 | 0 | 63/10 | 46/1 | 50/0 |
| pitts-s2b-like | rear | stick full left + aft | 26 | 0 | 23 | 8 | 71/55 | 49/9 | 60/-10 |
| pitts-s2b-like | rear | stick full right + fwd | 26 | 0 | 14 | 0 | 71/55 | 31/1 | 60/-20 |
| pitts-s2b-like | front | neutral, throttle idle | 0 | 0 | 8 | 0 | 82/90 | 44/8 | 0/10 |
| pitts-s2b-like | front | neutral, throttle full | 0 | 0 | 8 | 0 | 67/2 | 44/8 | 0/10 |
| pitts-s2b-like | front | stick full left + aft | 0 | 0 | 21 | 6 | 75/2 | 46/2 | 0/-20 |
| pitts-s2b-like | front | stick full right + fwd | 0 | 0 | 2 | 0 | 75/2 | 30/1 | 0/-10 |
| glider-2-33-like | front | neutral, throttle idle | 23 | 0 | 4 | 0 | 30/30 | 51/9 | 20/-20 |
| glider-2-33-like | front | neutral, throttle full | 23 | 0 | 4 | 0 | 30/30 | 51/9 | 20/-20 |
| glider-2-33-like | front | stick full left + aft | 23 | 0 | 0 | 0 | 30/30 | 49/1 | 20/0 |
| glider-2-33-like | front | stick full right + fwd | 23 | 0 | 29 | 0 | 30/30 | 29/1 | 20/-30 |
| glider-2-33-like | rear | neutral, throttle idle | 0 | 0 | 0 | 0 | 30/30 | 49/1 | 0/0 |
| glider-2-33-like | rear | neutral, throttle full | 0 | 0 | 0 | 0 | 30/30 | 49/1 | 0/0 |
| glider-2-33-like | rear | stick full left + aft | 0 | 0 | 0 | 0 | 30/30 | 51/1 | 0/0 |
| glider-2-33-like | rear | stick full right + fwd | 0 | 0 | 0 | 0 | 30/30 | 24/1 | 0/0 |
| j3-cub-like | rear | neutral, throttle idle | 48 | 0 | 0 | 0 | 52/9 | 44/3 | 30/0 |
| j3-cub-like | rear | neutral, throttle full | 46 | 0 | 0 | 0 | 35/6 | 44/3 | 30/0 |
| j3-cub-like | rear | stick full left + aft | 50 | 0 | 0 | 0 | 44/8 | 48/6 | 30/0 |
| j3-cub-like | rear | stick full right + fwd | 50 | 0 | 0 | 0 | 44/8 | 10/0 | 30/0 |
| j3-cub-floats-like | rear | neutral, throttle idle | 48 | 0 | 0 | 0 | 52/9 | 44/3 | 30/0 |
| j3-cub-floats-like | rear | neutral, throttle full | 46 | 0 | 0 | 0 | 35/6 | 44/3 | 30/0 |
| j3-cub-floats-like | rear | stick full left + aft | 50 | 0 | 0 | 0 | 44/8 | 48/6 | 30/0 |
| j3-cub-floats-like | rear | stick full right + fwd | 50 | 0 | 0 | 0 | 44/8 | 10/0 | 30/0 |

iOS: there is no touch or gamepad input for the prop or mixture on any aircraft (there never was); the 3-D levers now
show the sim's values. Question for Ty.

## Tests
EngineStateTests (12) and QuestViewArmTests (9), expected values as literals. Filtered suite (all Prop/Engine/VR/
Quest/Camera/Pilot/Hand/Cockpit/Seat/Glide/Trim/Rig/Extra/Pitts/Fuel/Smoke/Ground/Spin classes, probes excluded):
base 73d7702b 1040 passed / 16 failed / 4 skipped of 1060; branch 1061 / 16 / 4 of 1081; the same 9 failing names on
both (AerobatGroundHandling x2, GlideTable.CreditsReachTheUser, GroundSurface x2, ModelRig full air brake, PittsRollRate
x2, SpinTests.ProSpinDeparts); no new failures. Editor: QuestViewShots PASS; VrArmCheck FAIL (8, listed above).
