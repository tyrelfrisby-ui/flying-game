# The cockpit in a headset: hands, grabs, latches, the body

Phase 1 of three (owner Ty, 2026-11-02: "there should be hands and legs and feet on controls etc so the user sees
themselves while flying … the user can just 'grab' the stick throttle canopy latch etc and move them"). Phase 2 is the
bailout: harness, headset cable, canopy, freefall, ripcord (below). Phase 3 is tracked hands. This document is the design;
the controls a pilot uses are in [quest-controls.md](quest-controls.md).

Code: the math is engine-free in `src/FlyingGame.Core/Vr/` (`VrHand.cs`, `HandControls.cs`, `CanopyLatch.cs`,
`TwoBoneIk.cs`, `CockpitGrab.cs`, `SeatedPilot.cs`) and pinned by `tools/FlightTests/VrCockpitTests.cs`. The Unity glue is
`Bridge/VrCockpitHands.cs` (the orchestrator), `Bridge/XrHandInput.cs` (grip poses and haptics) and
`Bridge/VrPilotBody.cs` (the drawn body). Touch points in existing files are small: `QuestRig.TrackingToSeat`,
the hook in `TouchFlightControls.QuestInput`, `CockpitRigState.HandLatch`, `RigInputs.CanopyLatch`, one line in
`SceneBootstrap`.

## The frame

Everything is in the **cockpit frame**: the aeroplane's position and rotation, **no scale**, Unity axes (+x right,
+y up, +z toward the nose), metres. It is the frame the seat's eye (`CockpitView.EyeLocal`) and the head rig use: after
a recentre, `XrHeadPose.ToSeat` gives any tracked point in the seat's axes, so a hand is at `eye + ToSeat(p)` and moves
with the aeroplane for free. Nothing has to be re-solved when the aeroplane rolls.

## The hand abstraction

`HandPose` (Core) is one hand, whatever measures it: `Source` (None / Controller / TrackedHand), `Position` (the centre
of the closed hand), `Rotation`, `Grip` 0..1 (how closed), `Pinch` 0..1 (thumb to index). Everything downstream — the
grab, the latches, the IK — reads only this.

- **Controllers (Phase 1):** `XrHandInput.TryGripPose` reads OpenXR's grip pose (devicePosition / deviceRotation of
  the held-in-hand controller), which sits at the centre of the closed hand, so it needs no offset. Grip = the grip
  trigger, Pinch = the index trigger.
- **Tracked hands (Phase 3):** the same struct from XR Hands joints — Position = palm joint, Grip = mean finger curl,
  Pinch = the runtime's pinch strength — set in `VrCockpitHands.ReadHands` where the comment says so. Nothing else
  changes. (Today, with the controllers put down, `HandsOnly` is set and the hands simply rest on the stick and
  throttle.)

## The grab

`CockpitGrab` (Core) decides who holds what:

| Rule | Value | Why |
|---|---|---|
| Reach | **6 cm** from the hand's centre to the grab point, nearest wins | Ty's spec |
| Grab | the grip **closing** (rising through 0.6) while in reach | a fist already closed and swept past does not catch the stick |
| Hold | until the grip opens below 0.4, however far the hand then goes | hysteresis; the control's own stops decide where it can go |
| One each | one hand per control, one control per hand | |
| Menu | while the VR menu is open nothing can be grabbed and everything held is let go | Ty's gate |
| Head | nothing in the grab system touches the camera | Ty's gate: grabbing never moves the head |

Haptics (`XrHandInput.Tick`): a light tick (0.15, 15 ms) on coming into reach, a firmer one (0.45, 35 ms) on closing,
a click (0.8, 30 ms) on a latch detent or the Extra's pins coming out / going in.

**The grips in the cockpit.** The left grip is the left hand's: it no longer grabs the throttle from anywhere (the
lever is where it is). The right grip is the right hand's whenever the hand is on or holding a control, so closing
on the stick never toggles the smoke; a right grip closed in free air is still the smoke. Outside views keep the old
mapping exactly.

### Grab points: Codex's nodes, else proxies

CONTRACT.md names the nodes. Where one is missing at runtime the code falls back to a proxy in `CockpitProxy`
(offsets from the seat's authored eye) and logs `[VrCockpit] missing node <name>` once.

| Control | Looked for, in order | Proxy (from the eye: right, up, fore) |
|---|---|---|
| Stick pivot | `StickRoll_R/F`, `Stick_R/F` (existing rigs) | (0, −1.05, +0.42) |
| Stick length | distance to `Grip_Stick_Rear/Front` | grip 0.50 below the eye, 0.55 m stick |
| Stick limits | the rig's `StickRoll_*` / `Stick_*` map ends | ±18° roll, 18° aft, 12° fwd |
| Throttle pivot, sweep | `Throttle_R/F` and its map | (−0.26, −0.58, +0.20), 50° |
| Throttle arm, idle angle | `Grip_Throttle_Rear/Front`, read at the lever's current position | 0.20 m, 20° aft of vertical |
| Feet | `Foot_L/R_Rear/Front`, `Pedal_L/R_Rear/Front`, `Pedal_R/F_L/R` (+0.12 m up the pedal face) | (±0.13, −0.85, +0.85), ±0.07 m |
| Pitts latch | `Grip_Canopy_Latch`, `Canopy_Latch_Knob` | REAR seat's eye + (+0.038, −0.15, +0.55) locked |
| Extra latch | `Grip_Canopy_Latch`, the rig's `CanopyLock_R/F` | (−0.20, −0.10, +0.35) |

Seat ids `rear` / `front` map to the contract's `Rear` / `Front` and the existing rigs' `_R` / `_F`. Other seats (the
172's `pilot`) use proxies only. Each bind logs one `[VrCockpit] <type> seat '<id>': stick … throttle … feet … latch …`
line saying which of the two each control came from.

## The stick

`StickGimbal` (Core): roll gimbal OUTER on the fore-aft torque tube, pitch yoke INNER (the Extra's `StickRoll_* >
Stick_*`, the Great Lakes' `TorqueTube > Stick_*`). Stick direction `d(φ,θ) = (sin φ cos θ, cos φ cos θ, −sin θ)`;
from a hand at `v = h − pivot`: **φ = atan2(vx, vy)**, **θ = atan2(−vz, √(vx² + vy²))**. Roll + right, pitch + aft. Each
angle clamps on its own stop. At the grab the offset between the stick and the hand's direction is kept, so closing
the hand off-centre never jumps the stick.

**Into the flight model, by the same path as the thumbsticks.** `TouchFlightControls.QuestInput` overwrites its own
`_aileron` / `_elevator` with the held stick, which `PublishToDriver` then sends exactly as it sends a thumbstick. The
stick fraction is mapped through `Aircraft.StickForDeflection` (the inverse of the type's dead zone and expo), so the
surface deflection is **linear in stick angle**, as a cable is; expo is a thumbstick aid, and a real stick has none.
The trim bias that `PublishToDriver` adds is subtracted first, so the surface sits where the hand put the stick,
trimmed or not. `InvertElevator` is a thumb preference and does not apply: aft on a stick is always nose up. A held
stick is never "let go", even at exact neutral.

**Let go, and the stick is free.** With the hand off the stick and the thumbstick centred, `ControlInputs` carries
`AileronFree` / `ElevatorFree`, and on a reversible type the sim floats each surface to where its hinge moment
balances (`Aircraft.FreeFloat`): `H(δ) = Σ g·q·S·c·Chα·α_flow + Σ q·S·c·Chδ·(δ − δ_tab) − K·δ = 0`, settling with the
linkage's `freeTauS`. The Pitts' numbers are already in its config (elevator Chα −0.20, Chδ −0.45; aileron −0.10 /
−0.30; τ 0.12 s). The cockpit stick is drawn from the resulting deflections (`RigInputs.FromDeflections`), so it
trails with the elevator exactly, in a spin as in a loop, and the next grab picks it up from there. No new physics was
needed and none was added.

## The throttle

`LeverArc` (Core): the lever turns about the lateral axis through its pivot. Hand angle `α = atan2(vz, vy)` (0 =
straight up, + forward); `lever = (α + offset − idle) / sweep`, clamped 0..1; offset taken at the grab. The hand's
sideways position does not move it. Released it stays (friction, no spring). Every type with a throttle gets this one
lever on the LEFT, in both cockpits; the Great Lakes' prop and mixture levers are not grabbable yet and keep their
order (throttle / prop / mixture, left to right).

## The canopy latches

`ICanopyLatch` (Core): `Locked`, `Latch01` (the rig's `canopy_latch`, 0 locked .. 1 open / squeezed), `Reset`. The
latch lives on `CockpitRigState.HandLatch`, is reset with the aeroplane, drives `canopy_latch` into the rig, and sets
`CockpitRigState.CanopyLocked` when it changes. **Phase 2's canopy opening asks `CanopyLocked`.**

- **Pitts S-2 — `PittsCanopyLatch`** (black ball on top of the rear panel, slides left / right): 3 in (0.0762 m) of
  travel, **fully right = locked**, followed 1:1 by the hand's lateral travel. Within 4 mm of the right end it snaps
  home into the detent (click, LOCKED); to leave the detent the hand must pull 10 mm left of it — the detent's
  spring, in hand travel, because a controller cannot measure force. Released, it stays where it was left.
- **Extra 300 — `ExtraCanopyLatch`** (two red-and-white handles squeezed together): grab the pair with one hand and
  pull the **trigger** to squeeze both. Both past 0.85 = pins out = UNLOCKED (click). Let go and the springs push
  them back: with the canopy shut it re-locks (click); with it lifted (Phase 2) it stays unlocked until shut.
  Two-handed squeezing (one hand per handle) is in the state machine (`Step(left, right, …)`) but not offered by the
  grab yet.
- **The rest of the fleet:** the Great Lakes is an open cockpit (no latch). The Cub and the 172 have doors, not a
  canopy. The 2-33's canopy, and every other canopy type, has no latch yet — `CanopyLatchKind.None` — rather than a
  guessed one; see the questions in AGENT-LOG.

## The body and the IK

`TwoBoneIk.Solve` (Core): law of cosines, `x = (a² − b² + d²)/2d`, `h = √(a² − x²)`, middle joint `R + x·û + h·p̂` with the
pole's perpendicular part p̂; unreachable targets leave the chain straight and pointing at the target, too-close ones
fold it; both bones keep their lengths exactly.

- **Arms:** shoulders fixed to the seat (strapped in), relative to the eye (`SeatedPilot`, ANSUR II 50th-percentile male:
  upper arm 0.31, forearm 0.26, wrist to fist 0.08; shoulders ±0.17, 0.23 below and 0.08 behind the eye). The wrist is a
  fist's depth back from the hand toward the shoulder. Elbows bend out, down and back. The visible hand **snaps to the
  grip** of whatever it holds, finger curl = 1 there, = the grip trigger otherwise. An untracked hand rests on its
  control (right on the stick, left on the throttle).
- **Legs:** hips 0.70 below the eye, ±0.09; thigh 0.42, shin 0.45; ankle 0.15 behind and 0.08 above the ball of the foot,
  which is on the pedal. The pedal nodes swing with the rudder (still the left thumbstick X); the proxy pedals slide
  ±0.07 m (right rudder: right pedal forward, left pedal aft). Knees bend up and forward.
- **Drawn:** Codex's skinned `Resources/Models/pilot-body/pilot-body-own.glb` if present — each limb bone is AIMED at
  the next solved joint (so its bone axes do not matter), the IK uses ITS shoulder / hip joints and ITS bone lengths,
  and fingers curl about each bone's local +X. Until then a stand-in: 22 primitive parts (a capsule per long bone, palm
  box + four finger capsules + thumb per hand, a box per boot), one material each for suit / glove / boot
  (`FlyingGame/Lit`, which is in the always-included list), no colliders, no shadows.
- **Shown** only with the headset on, in the cockpit view, with the pilot in the aeroplane. Hidden in every outside
  view and after bailing out.

**Per-frame cost.** Four IK chains (~60 flops each), ~6 distance checks, two controller pose reads, 22 transform writes,
no allocation: an estimate of well under 0.1 ms of the 13.9 ms a 72 Hz frame has. Draw cost: 22 small meshes on three
materials. Unmeasured on the device — the `[QuestPerf]` line every 10 s is the number to read.

## Phase 2: the bail-out, by hand

Ty, 2026-11-02: "i want the first person view to be able to unlatch the canopy and the seatbelts and the headset cables
and jump out. if they forget to pull the headset cables they get pulled by the plane. the person retains first person
view while parachuting etc and they need to look down and grab and pull the rip chord for their parachute."

The reference sequence (Extra 300 POH bail-out; Patty Wagstaff's Extra 300S egress card): inform the passenger, slow to
about 100 KIAS, mixture lean, open the canopy (it flips open), take the headset off, open the seat belt (two quick
releases: bottom one left, top one right), leave to the LEFT, avoid the wing and the tail, open the parachute.

Code: the physics is engine-free in `src/FlyingGame.Core/Vr/` (`CanopyHinge.cs`, `Harness.cs`, `HeadsetCord.cs`,
`Egress.cs`, `Ripcord.cs`) and pinned by `tools/FlightTests/VrBailoutTests.cs` (28 cases, every expected value a literal
worked by hand). The glue is `Bridge/VrCockpitHands.Bailout.cs`. Small additive touch points: `Aircraft.ExtraDragAreaM2`
(the open canopy's drag), `CockpitRigState.HandCanopy / Harness / Cord / Climb` (reset with the aeroplane),
`PilotEgress.LeaveByHand / PullRipcord`, `PilotBody.FirstPerson / TumbleSec`, one line in `QuestRig.CaptureOrigin`
(`VrCockpitHands.AdjustOrigin`), the rig param `harness_latch`, and `Parachute` now runs on Core's `CanopyOpening`.

(E) marks an estimate: a number of mine, not from a source. Every one is a constant with that name in the code.

### 1. The canopy, in the air (`CanopyDynamics`)

Grab `Grip_Canopy_Rail` (the Pitts has it; elsewhere the frame's free edge, abeam the eye) once the latch is off, and
push. **Pitts:** slides AFT 4 in (0.1016 m, Codex's rig) on its left track, 1:1 with the hand; only then can it swing,
because the mushroom buttons hold it down; then up and over to the RIGHT about the right-rail hinge to 100° (Codex's rig).
**Extra:** up and over to the RIGHT about the right-sill hinge to 100°. The rig's `canopy_open`: Extra 0..1 = 0..100°;
Pitts 0..0.2 = the slide, 0.2..1 = 0..100°.

The canopy is a plate from the hinge (right) to its free edge (left), width w, angle β. With u = (−cos β, sin β, 0) and
n = (sin β, cos β, 0), a force F at the centroid opens it by M = (w/2)·(F·n).

- **Airload.** NACA RM L50B03 (Huston & Skopinski, 1950, flight and full-scale-tunnel pressures on a sliding canopy):
  the highest net load on a closed canopy is "in the exploding direction", with the cockpit inside "slightly less than
  free stream". The report's coefficients are in its figures, not its text; I take the net outward **ΔCp = 0.5 (E)** and
  hold it constant as the canopy opens (E). **M_aero = (w/2)·ΔCp·q·A**, q = ½ρ₀V_e² (the sim's IAS is equivalent airspeed).
- **Gravity** (apparent, the load factor in it): **M_g = (w/2)·m·(g_app·n)**, = −(w/2)·m·g·cos β level.
- **The hand** on the free edge (arm w) is a stiff servo (12 rad/s, critically damped, E) capped at **200 N (E)**:
  past that the canopy is torn out of the hand (a 1.0 buzz, logged).
- **I β¨ = M_aero + M_g + M_hand − c β˙**, I = m w²/3, c = 2 N·m·s (E), a stop at 100° with restitution 0.2 (E).

Numbers (E unless said): Extra w 0.75 m, L 2.0 m, A = 0.8·w·L = 1.2 m², 14 kg; Pitts w 0.70 m (Codex's hinge y −0.36 to
rail y +0.34; measured from the nodes at run time where present), L 1.5 m, A 0.84 m², 9 kg.

| | Extra | Pitts |
|---|---|---|
| M_aero / q | 0.225 m³ | 0.147 m³ |
| M_g, shut, 1 g | 51.5 N·m | 30.9 N·m |
| **flips open by itself, unlatched, above** | q 229 Pa = **37.6 KEAS** | q 210 Pa = **36.0 KEAS** |
| a hand can hold it shut up to | q 895 Pa = **74.3 KEAS** | q 1163 Pa = **84.7 KEAS** |

So at the POH's ~100 KIAS it flips open whatever the hand does — what the Extra's POH says it does. **Whether it then
departs** at high q is not modelled: no hinge-strength figure, so it stays on its stop (NEEDS YOU).

**Drag into the flight model:** ΔCdA = **0.20 m² (E)** · min(1, β/60°) (≈ 1.2 × the canopy's ~0.17 m² frontal arch; the
Extra's whole airframe is ~0.27 m²), applied along the relative wind at the cockpit through `Aircraft.ExtraDragAreaM2`,
so it also pitches and yaws the aeroplane (`ForceDebug` "extra-drag"). At 100 KIAS that is 324 N. While a hands-on canopy
exists, its physics set `CanopyOpen01` and the menu's open/shut command (and its "pulled shut above 60 kt" rule) stands down.

### 2. The belts (`SeatHarness`, `HarnessRelease`)

Every independent restraint is a `HarnessRelease`; the pilot **cannot leave the seat while any is latched** — the climb
stops at 2 cm of webbing stretch. One-way: refastening in flight is not modelled (NEEDS YOU).

| Fitting | Gesture | Threshold |
|---|---|---|
| Lift lever (lever buckle, lap belt) | close the hand on it and LIFT it, up and forward away from the belly | 4 cm along the lift axis (E: ~60° of a 4 cm lever) |
| Rotary cam-lock (5-point) | close the hand on it and TWIST the wrist about the dial's axis, either way | 60° of twist (E), swing ignored (swing–twist decomposition) |

Let go short of it and it springs back. **Extra:** two, per the 300S card — main (TOP, on the RIGHT) and lap (BOTTOM, on
the LEFT), both levers (E: the card names them "quick releases", not their type). **Pitts:** one rotary, centred, plus a
lap lever if Codex models `Grip_Harness_Release_Lap_*`. Rig param `harness_latch` (0 latched .. 1 released) = the main
fitting's travel. Until Codex's `Harness_*` meshes are in the build, primitive straps (lap belt, two shoulder straps) are
drawn and vanish as their release opens.

### 3. The headset cord (`HeadsetCord`, `VerletRope`)

Unplug by hand: grab the plug at `Headset_Jack_*` and draw it **3 cm (E, the plug's engaged length)** out along the jack
axis (from the jack toward the pilot). The cord is drawn as a position-based rope, 20 segments, **1.5 m** (CONTRACT's
default until Codex's handoff gives a real headset's) and 4 mm, from `Headset_Cable_Root` (proxy: the bottom of the LEFT
ear cup, E) to the jack; unplugged, the plug end hangs free.

Physics (the load path is straight-line, so it is exact once taut and zero while slack): **T = k·max(0, d − L)**,
k = 2000 N/m (E). The weakest link lets go at its force: **plug retention 25 N (E)** — no PJ-055/PJ-068 withdrawal figure
was found; phone-jack datasheets give 3–40 N (e.g. Haoyu PJ-611) — or the **headset pulled off at 40 N (E)** when the plug
holds harder (a locking LEMO). The pull lasts F/(k·v) ≈ 3 ms at 4 m/s: on 105 kg that is an impulse well under 1 N·s, a
TUG, not a tow. **What is shown:** a sideways offset of the whole head rig along the cord, T / 1000 N/m (E, the neck's
give) = **2.5 cm at 25 N, capped at 4 cm**, relaxing with τ = 0.15 s, translation only; a 1.0 buzz for 0.15 s in both
controllers; the plug pops out and streams on its cord. **Audio:** this game has no radio or intercom audio to cut; once
out, the mix is already his own wind (FlightAudio's PilotOut). Nothing was faked to stand in for it.

### 4. Climbing out, and the fall (`ClimbOut`, `JumperAero`, `EgressClearance`)

**The gesture:** grab a cockpit SILL (beside you at elbow height, either side) and PUSH DOWN — the seat, and so your view,
rises by exactly what your hand pushed, the way VR climbing works. The view moves only because your hand moved it.

| Rise | What happens |
|---|---|
| ≤ 2 cm | any belt still latched |
| ≤ 8 cm | the canopy not open past 60° (nor gone): your head is against it |
| 30 cm | shoulders above the rail: the slipstream pulls **F = q·0.35 m² (E) · exposed fraction** aft |
| F > 200 N | you are pulled off the sill: out, on the side you held (at first exposure above 52.9 m/s = 103 KEAS) |
| ≥ 50 cm and let go | over the side |

Let go short of 50 cm and you sit back down. **Leave to the LEFT** (Extra POH; the Pitts' canopy is over on the right).

**Exit velocity** = the aeroplane's velocity at your position (finite-differenced from its pose, so its roll and pitch
rates are in it: v_cg + ω × r) + a push of 1.0 m/s outboard and 0.5 m/s up (E). His centre starts 0.30 m under his eye,
so the view does not jump as he goes; the push carries him clear.

**The fall** is the existing `PilotBody` (105 kg = 90 kg pilot + 15 kg kit, ISA density with the day's deviation, the
sim's wind, implicit quadratic drag): **D = ½ρ·CdA·v²**. CdA: **tumbling 0.7 m² (E)** for the first 3 s (E), then
belly-to-earth **0.585 m²** = 2·105·9.80665 / (1.225·53.6²), back-solved from the 120 mph every source agrees on
(`SkydiverAero`). **Terminal v_t = √(2mg/(ρ·CdA))**: belly **53.6 m/s** at sea level, **62.2 m/s** at 3000 m (ρ 0.9093);
tumbling 49.0 m/s. The right thumbstick forward trades it for speed (track, head down); neutral is the stable arch.

**First person.** The head rig's origin is the falling body (eye 0.30 m above the centre in free fall, 0.75 m hanging or
standing, E), **level, on the heading he left on**, and never rotated by the game after that: your real head looks
around. Tumbling is not shown by turning the view (NEEDS YOU). The menu-shortcut figure is not drawn; the VR body is, from
the same IK, with the hands at the controllers.

**The tail.** For 8 s after the exit the jumper (radius 0.30 m, E) is checked against boxes built from the config's own
strips (each strip [x − ¾c, x + ¼c], ±½·area/chord of span, ±0.12 m thick): a hit is logged with the surface and the
relative speed, and buzzes both hands hard. Reported only — no injury model (NEEDS YOU).

### 5. The ripcord and the canopy (`Ripcord`, `CanopyOpening`)

Strong Enterprises Para-Cushion Owner's Manual, Rev. H (11/2000): "The ripcord handle is located near the chest strap on
the wearer's LEFT front of the harness. The key is to LOOK at the ripcord handle … grab it with both hands (or typically
with your right hand and left thumb) … push the handle DOWN toward your feet" (back type). So: **look down, grab the handle
on your left chest strap, pull it 10 cm DOWN** (E: a pin's engaged length plus slack; the manual says pull it right out).
A real one takes up to **22 lbf (98 N)** — TSO-C23's maximum manual pull force, as quoted by the USPA and APF RAC 502B;
the TSO text itself was not re-read. A partial pull stays partial. Proxy position: 0.40 below, 0.15 ahead and 0.10 left of
the eye (E), or Codex's `Grip_Ripcord`. Out of the aeroplane only (the manual: "Get clear of the aircraft, then pull your
ripcord. In that order. If the parachute begins to open while you're still aboard, the wind may inflate it, dragging you
out, or into the tail").

**The opening**, shared with the menu's BAIL OUT:

| Stage | Model | Numbers |
|---|---|---|
| Pilot chute | spring-launched, decelerates on its own drag; the gap grows by the speed difference | 8 m/s (E), 30 in Cd 0.9 → CdS 0.41 m² (E), 0.25 kg, + 3 kg bag after the 1.5 m bridle (E) |
| Line stretch | gap = bridle + lines | 1.5 + 7.0 m (E) |
| Inflation | **t_f = n·D0 / V_ls** (Knacke, NWC TP 6575: filling over a constant distance, n ≈ 8 for solid canopies — quoted, not re-read); **CdS(t) = CdS_pc + (CdS0 − CdS_pc)(t/t_f)²** (E: the quadratic drag-area-time form) | D0 26 ft = 7.925 m (Para-Cushion); Cd0 0.80 (E) → CdS0 39.46 m² |
| Opening shock | **not put in** — it comes out of m·dv/dt = −½ρv²(CdS + CdA_body) + mg | below |
| Steady descent | **v = √(2mg / (ρ(CdS0 + 0.5)))** | **6.49 m/s** sea level, 7.16 m/s at 2000 m |

Integrated (`CanopyOpening.Simulate`, 105 kg):

| From | Line stretch | t_f | Peak | Pins → steady | Height lost |
|---|---|---|---|---|---|
| 53.6 m/s, sea level (belly terminal) | 0.23 s | 1.22 s | **6.7 g** | 3.2 s | 64 m (210 ft) |
| 62.2 m/s, 3000 m | 0.21 s | 1.04 s | **8.1 g** | 3.4 s | 73 m |
| 25 m/s, sea level | 0.44 s | 2.30 s | 2.7 g | 4.2 s | 67 m |

The manual: "opening times are from 2 to 3 seconds and the distance fallen would be from 150 feet to 300 feet". The old
Parachute's fill-time clamp (1.8–2.5 s) and its 1.3× drag-area "shock" bump are gone. Forward drive is now 2.7 m/s (the
manual's "about 6 MPH"). The pendulum and steering (right thumbstick X, ≤ 30°/s) are the existing ones.

**Shown:** Codex's `Parachute_Deployed` with `Parachute_Canopy`'s "Inflate" shape key = t/t_f, when the pilot-gear asset
is in the build (the primitive dome then hides); else the existing gored dome. Haptics: both hands buzz with the load,
(g − 1)/7, every 50 ms, through the shock. **Landing:** the existing ground/water contact, slope tumble and PLF outcome
(`PilotPhrases.Outcome` from the hardest impact speed; at a steady 6.5 m/s sink plus the wind's drift) — logged with the
landing speed, and a 1.0 buzz. No text is drawn in the headset, and nothing anywhere shows where he is.

### 6. After he goes, and the menu

The aeroplane flies on under the sim, unmanned (`PilotEgress.AbandonAircraft`: stick and rudder neutral, trim and throttle
kept), with the open canopy's drag still on it, until it hits something through the existing crash path.

**The menu's "Bail out / Deploy BRS / Eject" row stays as the shortcut:** the timed sequence (canopy jettisoned at 3 s,
over the RIGHT side at 5 s, the chute opening by itself 3 s later), seen from outside, as on the iPhone. The hands-on path
is the real one: every step yours, first person, no automatic opening. Both use the same body, air and canopy model.

### Comfort decisions

- No rotation is ever forced on the view. The one discontinuity: at the exit the origin snaps from the aeroplane's attitude
  to level on the same heading (an instant snap is gentler than a slow forced roll; at a wings-level exit it is nothing).
- Translation the pilot did not make himself: the fall itself and the opening shock (real motion, unavoidable), and the
  cord's tug (≤ 4 cm, 0.15 s). The climb moves the view only by what his own hand pushed.
- Under the canopy the view does not yaw with a steering turn: you turn your head. The canopy and the risers turn above you.

## Where Phase 2 and Phase 3 plug in

(Phase 2 is built: the section above. What follows is the plan it was built to; Phase 3 still is.)

- **New grab points** (harness buckle, headset jack, canopy rail, ripcord): add to `Grabbable`, set their positions in
  `VrCockpitHands.UpdatePoints` from the CONTRACT's `Harness_Buckle_*`, `Headset_Jack_*`, `Grip_Canopy_Rail`, and give
  each a small Core state machine like the latches (`Grab` / `Follow` / `Release`, tested headless).
- **Canopy opening:** gated on `CockpitRigState.CanopyLocked`; `ExtraCanopyLatch.Step` already takes `canopyOpen01`.
- **Freefall in first person:** `VrCockpitHands.Active` drops when `PilotEgress.PilotOut`; the body and hands then
  belong to whatever drives the jumper. The IK, the hands and `HandPose` are frame-agnostic and can be re-rooted on
  the jumper instead of the seat.
- **Tracked hands:** fill `HandPose` with `Source = TrackedHand` in `ReadHands`. `QuestControlMapper`'s pinch gestures
  (menu, recentre) stay as they are; grabbing a control then uses `Grip` = curl.
