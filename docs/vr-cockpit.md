# The cockpit in a headset: hands, grabs, latches, the body

Phase 1 of three (owner Ty, 2026-11-02: "there should be hands and legs and feet on controls etc so the user sees
themselves while flying … the user can just 'grab' the stick throttle canopy latch etc and move them"). Phase 2 is the
bailout: harness, headset cable, canopy, freefall, ripcord. Phase 3 is tracked hands. This document is the design;
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

## Where Phase 2 and Phase 3 plug in

- **New grab points** (harness buckle, headset jack, canopy rail, ripcord): add to `Grabbable`, set their positions in
  `VrCockpitHands.UpdatePoints` from the CONTRACT's `Harness_Buckle_*`, `Headset_Jack_*`, `Grip_Canopy_Rail`, and give
  each a small Core state machine like the latches (`Grab` / `Follow` / `Release`, tested headless).
- **Canopy opening:** gated on `CockpitRigState.CanopyLocked`; `ExtraCanopyLatch.Step` already takes `canopyOpen01`.
- **Freefall in first person:** `VrCockpitHands.Active` drops when `PilotEgress.PilotOut`; the body and hands then
  belong to whatever drives the jumper. The IK, the hands and `HandPose` are frame-agnostic and can be re-rooted on
  the jumper instead of the seat.
- **Tracked hands:** fill `HandPose` with `Source = TrackedHand` in `ReadHands`. `QuestControlMapper`'s pinch gestures
  (menu, recentre) stay as they are; grabbing a control then uses `Grip` = curl.
