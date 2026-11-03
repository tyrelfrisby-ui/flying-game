# Meta Quest controls

The Touch controllers feed the **same** control state the touch pads and a calibrated joystick feed
(`TouchFlightControls`: aileron, elevator, rudder, the sticky throttle knob, trim, brakes and the tap buttons), so
the flight model has one input path. The mapping is engine-free in `src/FlyingGame.Core/Vr/QuestControls.cs` and
pinned by `tools/FlightTests/QuestControlsTests.cs` (signs, dead zones, the throttle holding, the buttons, hands).

Right hand flies, left hand runs the engine, the same split as a real cockpit and as the touch pads (right pad =
stick, left pad = rudder + throttle).

| Control | Does | Notes |
|---|---|---|
| **Right thumbstick X** | Aileron | Right = roll right. |
| **Right thumbstick Y** | Elevator | Forward = **push = nose down** (the realistic sense the pads use). `TouchFlightControls.InvertElevator` flips it for both (it is not in Options today). |
| **Left thumbstick X** | Rudder | Right = right rudder. Springs back. |
| **Left thumbstick Y** | **Throttle lever, as a rate** | Push to open, pull to close, **let go and it stays**. Idle to full in 2 s. On a glider the same lever is the spoiler handle (forward = stowed, back = out), exactly as the touch pad's upper band. A **POWER 65 %** card shows low on the left while it moves (see *The cockpit*). |
| **Left grip (hold) + move hand** | **Grab the throttle lever** — *outside views only* | Squeeze, move your hand forward / back, let go: 20 cm of hand travel is the full lever. Starts from wherever the lever is, so a reset never makes it jump. Same POWER card. **In the cockpit the throttle is grabbed where it is** (below). |
| **Left trigger** | Wheel brakes (analog) | Rudder biases them left / right, as on the touch pad (toe-brake feel). |
| **Right trigger** | Guns (held) | Only where the guns are hot (combat zone / dogfight). Otherwise nothing. |
| **Right grip** | Smoke on / off | One toggle per squeeze. In the cockpit only when the hand is NOT on a control (closing on the stick never toggles the smoke). |
| **A (hold)** | Pitch trim **nose up** | 0.5 of the trim range per second, the same rate as a joystick's TRIM buttons. |
| **B (hold)** | Pitch trim **nose down** | B sits further from the thumb: "forward" = nose down, like a trim wheel. |
| **A + B together, held 1 s** | **Record video on / off** | See *Recording* below. Held together the two trims cancel, so the trim does not move. Not while the VR menu is open (there A selects and B closes; use its Record row). |
| **X** | Flaps one notch **down** | 0 → 50 → 100, from where the flaps actually are. |
| **Y** | Flaps one notch **up** | |
| **Right stick click** | Gear up / down | Retractable types. On a glider it is **TOW** on the runway and **RELEASE** on tow. |
| **Left stick click** | **Recentre** | Puts your head where it is now at the seat's eye, facing the nose. |
| **Menu (☰, left)** | **VR menu (pauses)** | The small button with three lines on the LEFT controller, below the thumbstick. A short press (a long press is Meta's). Read through both the legacy XR and the Input System paths, whichever fires. Press again, or B / Y, to close. |
| **Left stick click, held 1 s** | **VR menu** too | The fallback if ☰ is hard to find: it recentres on the press (harmless) and opens / closes the menu after a second. |
| **The MENU tab** (low left on the panel) | **VR menu** too | Poke it with either hand, or point at it until it lights and pull the trigger. See *Opening the menu*. |
| **Meta button (hold)** | The headset's own recentre | The game follows it: same as the left stick click. |

**USB rudder pedals** (a USB-C OTG adapter into the headset) take the rudder and the toe brakes as well: the
pedals' rudder ADDS to the left stick's, and each wheel brakes with the harder of its toe brake and the left
trigger. Unplug them and the controllers have it all again on the next frame. Mapping and calibration: VR menu →
**Rudder pedals**. Everything else: docs/RUDDER-PEDALS.md.

**Stick feel (Ty 2026-11-02: "far too sensitive, especially around neutral").** In a headset the stick runs
through `out = 0.35·u + 0.65·u³` on aileron, elevator and rudder alike, where `u` is the stick past a **0.05** dead
zone, rescaled so there is no step at its edge (`QuestStickFeel`, `QuestControlMapper.StickDeadZone`; it was 0.10 and
each aircraft's own 10–30 % expo). Full stick is still full deflection; a small movement does about a third of what it
did linearly; half stick is a quarter of the travel (the Pitts' aileron at 0.3 of stick: 0.141 of its travel before,
0.104 now). It is the same setting as Options → Controls' expo on the phone and the Mac — there "unset" still means
the aircraft's own, here it means 65 % — and **VR menu → Stick expo** changes it (`<` `>` in 5 % steps, all three
axes; select = back to the default). The throttle's rate dead zone stays 0.25. One stick carries rudder and
throttle, so whichever axis is clearly dominant wins: a bootful of rudder with your
thumb drifting 0.3 forward does not open the throttle, and easing the power off does not kick the rudder.

**A Bluetooth gamepad** (an 8BitDo, an Xbox or PlayStation pad, any Android pad: pair it in the headset's Settings →
Devices → Bluetooth, with the pad in its Android / X-input mode) is read through the Input System and turned into the
same controller frame (`QuestGamepadMap`), merged with the Touch controllers', so it flies, works the VR menu and has
the same feel — use either, or both. The sticks are Ty's mapping for every platform (2026-11-02), the same as the
iPhone and the Mac and the same split as the Touch controllers: right hand flies, left hand has the rudder and the
power.

| Pad | Does |
|---|---|
| **Right stick X / Y** | Aileron / elevator (forward = push = nose down) — expo as above |
| **Left stick X** | Rudder — expo as above |
| **Left stick Y** | The throttle lever **as a rate**: push to open, pull to close, **let go and it stays** (idle to full in 2 s, the Touch's rate). A pad's stick springs back to centre, so read as a position it would chop the power to half every time the thumb lifted; the iPhone reads it as a rate for the same reason |
| **Left / right trigger** | Wheel brakes / guns (where they are hot) |
| **South (Xbox A) / East (B)** | Pitch trim nose up / nose down — the Touch A / B |
| **West (X) / North (Y)** | Flaps one step down / up — the Touch X / Y |
| **Left stick press** | Gear (glider: tow / release) — the flying stick's click |
| **Right stick press** | Recentre; **held 1 s**, the VR menu |
| **Start** | **VR menu**. In it either stick moves and changes values, South / West or a trigger selects, East / North goes back — the Touch's A / X and B / Y |
| **Select** | Recentre |
| **Right shoulder** | Smoke on / off |

The sticks are read without the Input System's own 0.125 dead zone, so the 0.05 one above is the only one. When a pad
connects or goes, the log says so: `adb logcat -s Unity | grep QuestInput` shows `[QuestInput] gamepad Added: "<name>"
layout <layout> VID:PID xxxx:xxxx …` and `gamepad in use: …`, and the VR menu's **Gamepad** row names it. Buttons
are by POSITION: on an 8BitDo in Nintendo labelling the bottom button (labelled B) is South.
One stick carries rudder and throttle, so whichever axis is clearly dominant wins: a bootful of rudder with your
thumb drifting 0.3 forward does not open the throttle, and easing the power off does not kick the rudder.

## The cockpit

In a headset the flat dials and the HUD stand down, so the aeroplane's own cockpit has to work. What moves, per
aeroplane (from the sim, every frame, the same numbers the iPhone's dials show):

| Aeroplane | Instruments | Stick, pedals | Throttle lever |
|---|---|---|---|
| **Pitts S-2B** | Rear (PIC) panel live: airspeed (mph), altimeter (both hands), g meter with max / min tell-tales, tach, oil (pressure + temperature, one dial), manifold pressure, compass card. The front panel's three small dials are still painted. | Both sticks and all four pedals, cut out of the model's cockpit meshes and hinged at the floor. (Static in builds up to a222bda: see below.) | **Added**: the model has none. A lever on the left wall of the rear cockpit, 5 cm ahead of the seat, 10 cm below the sill. **Ty: say where it really is and I will move it.** |
| **Extra 300** | Front panel: the model's own rig (as before). Rear panel: airspeed (kt), altimeter, attitude (bank only), g meter, tach, oil, manifold pressure, fuel, compass card live; fuel pressure and volts stay painted. | The rig (as before). | The rig (as before). |
| **Great Lakes, 2-33** | The model's own rig. | The rig. | The rig. |
| **J-3 Cub, wheels and floats** (2026-11-02) | The model's own rig (`docs/models/j3-cub-own-rig.json` / `j3-cub-floats-own-rig.json`, written from the GLB by `tools/own_models/j3_rig_json.py`): airspeed (mph), tach, altimeter (both hands), oil temperature + pressure, compass card, mag switch. Carb heat, primer, fuel valve, the nose-tank float wire and the doors stay as drawn (the sim has no such controls). | Both sticks, all four pedals; ailerons, elevators, rudder, the trim crank and the stab; the tailwheel steers / the water rudder steers. | Both throttles (one push rod). |
| Everything else | The downloaded / generated models have no cockpit that moves. | | |

Signs, so they can be checked from the seat: **stick aft = top comes toward you** (18°, 12° forward), **right
aileron = top to the right** (18°), **right rudder = right pedal forward, left pedal back** (15°), **throttle
forward = power** (50° of travel). The table and the signs are in `src/FlyingGame.Core/CockpitLive.cs` and pinned
by `tools/FlightTests/CockpitLiveTests.cs`; the Unity side is `Bridge/CockpitLiveParts.cs`, which logs
`[Cockpit] <model>: …` once per aeroplane (a rebuilt model that no longer matches says `LEFT STATIC` there instead
of carving the wrong triangles). For the Pitts it should read:

    [Cockpit] pitts-s2b-like-own: 7 of 7 dials live, 9 mesh(es) under Cockpit, 9 read; cut Stick_R 80 from 3 mesh(es)
    pivot (…), Stick_F 80 from 5 mesh(es) pivot (…), Pedal_R_L 12 from 2 mesh(es) pivot (…), … , 1 throttle lever(s) added …

Every other aeroplane says whether its own rig moves the cockpit or it is static. `NOT READABLE` or `skinned by the
carve` in that line names the next suspect.

**Why the Pitts' stick and pedals stood still until a222bda.** In the GLB the cockpit is one mesh of nine
primitives (one per material). glTFast gives the node's GameObject only the FIRST primitive (the seats) and puts
each of the others on a child GameObject of its own. The cut read the node's own mesh, found no stick in the seats,
and left every part static (`Stick_R 0 (expected 80: LEFT STATIC)` — the log that would have said so was lost with
the 256 KB logcat buffer). The 80 / 12 triangle counts had been checked over the whole GLB mesh, so they were right
and could not show it. The stick's triangles are in fact spread over three to five of the nine primitives (the
GLB's primitive materials do not match the build script's tags; a separate, visual matter, left to the cockpit
lane). The cut now reads every mesh under the node, transformed into the node's frame from local transforms.

**The POWER card.** Whenever the throttle moves (stick or grip), a small green **POWER 65 %** card shows low on
the left, 0.55 m out, by the throttle hand, for 1.5 s after the lever stops (**AIRBRAKE 40 %** on a glider). On
the first three flights it also says how: *"THROTTLE: left stick forward / back - it stays where you leave it (or
squeeze the left grip and push your hand)"* for 10 s after FLY. It draws over the cockpit (the menu's shader),
never over the horizon.

**The sequence card** no longer flickers in the Pitts: see docs/quest-performance.md, *The flickering card*.

## Grabbing the aeroplane's own controls (cockpit view)

Reach out with the controller to the control itself; within **6 cm** the controller ticks lightly. **Squeeze the grip**
to take hold (a firmer tick); your drawn hand snaps onto it. Let go by opening the grip. Nothing can be grabbed while
the VR menu is open, and grabbing never moves your head. The design is in [vr-cockpit.md](vr-cockpit.md).

| Control | Hand | How it moves | Let go |
|---|---|---|---|
| **2-33 dive brakes, trim, tow release** | either; a controller's grip or a bare hand | See *The 2-33's handles* under *Hands* below | Dive brakes and trim stay; the tow knob springs back |
| **Switches and knobs** (Pitts, Extra, Great Lakes: magnetos, master, fuel pump, lights, **smoke**, mixture, prop, alternate air / carb heat, **trim**, fuel selector, COM, XPDR) | either | Toggles: **poke** with a pointing finger, or grab and flick. Knobs: grab and **twist your wrist**. Push-pull: grab and pull. Only the **trim** and the **smoke** do anything in the sim yet; the rest move, click and are logged ([vr-cockpit.md](vr-cockpit.md#the-switches-and-knobs-pitts-extra-great-lakes)) | Stay; the magneto's START springs to BOTH, COM swap / IDENT spring out |
| **Stick** | either (right, normally) | Follows your hand about its real pivot: roll gimbal on the torque tube, pitch yoke on it. Limits from the model's rig (Extra / Great Lakes: ±18° roll, 18° aft, 12° fwd); proxy otherwise. The surfaces are linear in stick angle (no expo on a real stick). The right thumbstick does nothing while you hold it. | **Free stick**: on a reversible type it trails with the surfaces' hinge moments (the sim's free-control model), and you see it move. |
| **Throttle** | either (left, normally) | On its real arc on the left of the seat, both cockpits: move your hand forward = power. Sideways hand movement does nothing. | Stays where it was left. The left thumbstick Y still works as before when you are not holding it. |
| **Canopy latch, Pitts** | either | Grab the **black ball** on top of the rear panel and slide it **left / right** (3 in). **Fully RIGHT = LOCKED**, with a detent click at the end; pull 1 cm left to come out of it. | Stays where it was left. |
| **Canopy latch, Extra** | either, or one each | On the LEFT canopy frame, ahead of you: a fixed REAR handle and a spring-loaded FRONT one. Grab the pair with one hand and pull the **trigger**, or grab each handle and close your hands together, or pull the front handle aft on its own (35 mm). Past about 80 % the pins come out = UNLOCKED (click). The rear handle alone does nothing. Your hand on them is on the canopy: push up to open it. | The front handle springs forward; with the canopy shut the pins snap in (click). With it open they cannot. |
| **Canopy jettison** | either | **Pitts:** the RED knob on the far RIGHT, where the bow tubes meet the canopy's lower rail (each cockpit). **Extra:** the red emergency release on the RIGHT sill. Grab and pull toward you, firmly (8 mm before it gives), all the way. | One-way: it stays out. With the latch open the canopy leaves at any speed; with the latch shut it stays until you open the latch. Short of the end, it springs back and nothing happens. |

Opening the canopy, the belts, the headset plug, the sills and the ripcord: see "Bailing out by hand" below. The rudder is still the **left thumbstick X**, and your feet ride the pedals.

**You see yourself:** arms, gloved hands, legs and boots, posed from the controllers and the pedals. With a controller
put down, that glove is your own tracked hand, fingers and all; a hand the headset is not tracking rests on its
control. Hidden in outside views.

## Bailing out by hand (cockpit view, Pitts and Extra)

The real way out: every step is yours, in first person, as the Extra's POH and the 300S egress card run it. Nothing
happens by itself except what the air does. Design and numbers: [vr-cockpit.md, Phase 2](vr-cockpit.md#phase-2-the-bail-out-by-hand).

| # | Do | With | How | What you get |
|---|---|---|---|---|
| 0 | Slow down, mixture lean | stick, throttle | as you fly | Unlatched, the suction over the bubble flings the canopy open above ~54 KIAS (Extra) / ~46 (Pitts). It stays on its hinges, flapping, below **80 KIAS (Extra) / 80 mph = 69.5 KIAS (Pitts)**; faster than that it **tears off** and falls clear. A hand can hold it shut to ~108 KIAS. |
| 1 | **Unlatch the canopy** | either hand | Pitts: slide the black ball LEFT. Extra: squeeze the handles (pair + trigger, or one hand on each). **Or jettison it**: pull the red knob (Pitts, far right at the bows) / red release (Extra, right sill) — then it goes at any speed once the latch is open too. | Click; `canopy latch UNLOCKED` / `canopy JETTISON pulled`. |
| 2 | **Open the canopy** | either hand | Grab the canopy rail / frame edge (Pitts: the left rail; Extra: the latch handles are on it). Pitts: push it AFT 4 in, then up and over to the RIGHT. Extra: up and over to the RIGHT. | In flight the air takes it (the Pitts runs aft on its track by itself): flung onto its stop, torn out of your hand if you were holding it back (strong buzz), flapping; or gone. Open, its drag slows the aeroplane and the wind roars; gone, it is lighter and the cockpit roars more. |
| 3 | **Unplug the headset** | either hand | Grab the plug at the jack (right side) and draw it 3 cm straight out. | Click; the plug hangs on its cord from your headset. **Forget it** and the cord goes taut once you are out: a tug on your head (a few cm, short) and a hard buzz, then the plug pulls out. |
| 4 | **Open the belts** | either hand | Lever: grab and LIFT it (4 cm). Rotary: grab and TWIST your wrist (60°). Extra: two — the TOP one on the RIGHT, the BOTTOM one on the LEFT. | Click per fitting; the straps fall open. While any is fastened you cannot rise from the seat. |
| 5 | **Climb out** | either hand, on the LEFT sill (or right) | Grab the sill beside you and PUSH DOWN: you rise by what you push. | Strapped in: nothing. Canopy in the way: your head stops at it. Shoulders above the rail at speed: the slipstream pulls you off the sill. Or rise 50 cm and let go: over the side. |
| 6 | **Fall** | your head | Look around. Right thumbstick forward = track / head down; neutral = stable belly-to-earth. | First person, level, on the heading you left on; the view is never turned for you. ~3 s tumbling, then about 120 mph. A hit on the tail or wing buzzes hard (logged). |
| 7 | **Pull the ripcord** | either hand (right, normally) | LOOK DOWN: the handle is on your LEFT chest strap. Grab it and pull it 10 cm DOWN toward your feet. | Pilot chute, line stretch, the canopy fills (about 1–3 s); the opening shock buzzes both hands (~7 g from terminal). Then ~6.5 m/s down. |
| 8 | **Steer and land** | right thumbstick X | as with the menu's bail-out | Turn up to 30°/s; land into the wind; feet and knees together. |

The menu's **Bail out / Deploy BRS / Eject** row stays: it is the timed shortcut (canopy off, over the right side, the
chute opens by itself), seen from outside. The hands-on sequence above is the real one.

## The VR menu

Every menu in the game is OnGUI, which draws to a screen the headset never shows, so the Quest build has its own
small world-space menu. **It opens by itself at launch** (the landing page is invisible in the headset), and in
flight from any of the ways below. It pauses the flight while it is open (the title says PAUSED). On the first
three flights a small card low in the cockpit, a little right of centre, says *"MENU / OPTIONS: touch the MENU tab
(lower left) or press the left MENU button or hold the left stick click for a second"* for 12 s, below the
windscreen line so it never covers the horizon; once you have opened the menu yourself in flight it is not shown
again (persisted; the count was reset for this build, because the old one was spent while the menu could not open).

### Opening the menu

| Way | How | Why it cannot happen by accident |
|---|---|---|
| **☰** (left controller) | A short press. Read through the legacy XR usage (`CommonUsages.menuButton`) AND the Input System's left-hand XR controller (`menu` / `menuButton` / `start`), whichever says it is down. | A button of its own. |
| **Left stick click, held 1 s** | Hold. The click recentres at once (harmless); the menu opens after a second. | A second of holding. |
| **The MENU tab** | A small dark tab marked MENU, low on the left of the panel (0.18 m left, 0.19 m down, 0.5 m out from the seat's eye), drawn over the panel. **Poke it** with either controller or a tracked fingertip and hold it there a fifth of a second; or **point at it** until it lights up (0.3 s) and **pull the trigger** (pinch with a tracked hand). Cockpit view, in flight, menu closed. | A poke must come IN from outside and stay; a hand resting there when it appears does nothing until it leaves. A ray needs the hover first and a FRESH trigger pull: a trigger already held (brakes, guns) as the ray sweeps over it never counts. No gaze dwell: eyes rest on the panel all the time. Rule and tests: `QuestMenuTab` in `src/FlyingGame.Core/Vr/QuestCockpit.cs`, `tools/FlightTests/QuestCockpitTests.cs`. |
| **Left pinch, held 1 s** | Hands only (controllers put down). | A second of pinching. |

**Why it would not open in builds up to a222bda.** Not the input handling: the panel was parented to the aeroplane's
transform, and every airframe rebuild (every FLY) destroys any child with a renderer it did not build itself
(`AirframeVisual.Clear`, the `[Airframe] stray child 'VR Menu' cleared on rebuild` warning). The menu worked on the
landing page and was a destroyed object from the first flight on, so ☰ and the held click both threw inside
`Open` and nothing appeared. It now rides the rig's own seat anchor (a root object), is rebuilt if anything ever
destroys it, and an error in it is logged rather than lost.

### Changing aircraft without restarting

In flight the menu has two ways:

- **CHANGE AIRCRAFT (main menu) >** goes back to the landing page's rows (FLY, Aircraft, Start, Seat …) in the
  headset. Pick, then **FLY**.
- Or change the **Aircraft** row in flight: the row under RESUME becomes **FLY THE <TYPE> NOW**, which starts it
  straight away (it is RESTART FLIGHT when the row is the one you are flying).

### What the log says (`adb logcat -s Unity | grep QuestInput`)

    [QuestInput] devices (headset started); input handler: Both | legacy XR n: ["Oculus Touch Controller OpenXR" …, menu yes …] | Input System XR n: [… layout …, usages LeftHand, menu menu]
    [QuestInput] Input System menu button: /…/menu + /…/thumbstickClicked
    [QuestInput] MENU tab built at the seat eye + (-0.18, -0.19, 0.50) m …
    [QuestInput] menu trigger: the left MENU button (legacy XR | Input System | both) / the left stick click held 1 s / a poke on the MENU tab / the MENU tab, pointed at + trigger
    [QuestInput] menu OPEN by … (in flight, paused)      [QuestInput] menu closed by B / RESUME / FLY …
    [QuestInput] menu: CHANGE AIRCRAFT - back to the landing page      [QuestInput] FLY: <aircraft>, <start>

The device line is written again on every connect / disconnect (a controller waking up, hands taking over), never
per frame. A `[QuestInput] VR menu … failed:` error line, if one ever appears, carries the exception.

**Point** with either hand and pull the **trigger** (or press **A / X**), or move the highlight with a
**thumbstick** and push it **left / right** to change a value. **B / Y / ☰** closes it in flight.

| Row | Does |
|---|---|
| FLY / RESUME | Starts a **free flight** with the choices below (lessons, challenges, weather and multiplayer have OnGUI cards of their own and are not reachable in VR yet). In flight: closes the menu. |
| RESTART FLIGHT / FLY THE <TYPE> NOW | Starts again with the current aircraft / start choice; named for the new type when the Aircraft row has been changed. |
| CHANGE AIRCRAFT (main menu) > | In flight: back to the landing page's rows, FLY starts the new choice. No app restart. |
| Aircraft | The headset's fleet, cycled: **2-33, J-3, J-3 on floats, Extra, Pitts, Great Lakes** (`QuestFleet`: only our own models with a checked eye). In flight it applies on the row above. |
| Start | In the air · On the runway · On final · Aerobatic box · Combat zone · In a thermal. |
| Seat | The seats of the aircraft **that will fly** (on the landing page: the one on the Aircraft row). Remembered per aircraft, by seat (rear / front / pilot …), so the rear seat is the rear seat in every type. Nothing chosen = the solo / PIC seat: **rear** in the Pitts, Great Lakes, Extra and Cub, **front** in the 2-33, **left** in the 172. In flight your head goes to the new seat's eye at once. The **J-3 has the rear seat only** in the headset: its front eye has a cabin ceiling tube 9 cm from it (`check_eye_interiors.py`), so a saved "front" puts you in the rear. |
| View | Cockpit / outside (the chase view, levelled to the horizon). |
| HUD in cockpit | **Off** (default) / On. Persisted. In the headset's cockpit view the aeroplane's own instruments are what count, so the green HUD is not drawn there unless this is On. Outside views keep the HUD as before, and the iPhone / iPad / Mac are unchanged. Only the HUD's lines exist in the headset (its numbers are a flat overlay). |
| Display | 72 Hz (default) / 90 Hz. Persisted. |
| Rudder pedals | none / ON / connected, OFF / calibrating. Opens the **Rudder pedals page** (below). |
| Record video | Off / **ON 0:42** / saving … Starts or stops a recording, the same as A + B held. Starting it in flight also closes the menu, so the film starts on the flying. |
| Recentre the view | Same as the left stick click. |
| Reset flight | The touch pad's RESET. |
| Bail out / Deploy BRS / Eject | Asks twice: the first press arms it for 3 s. The timed SHORTCUT (outside view, automatic chute); the hands-on way is the table above. |

### The Rudder pedals page

B / Y (or **< BACK**) goes back to the main page. A calibration running when you leave is cancelled (nothing saved).

| Row | Does |
|---|---|
| Device | The pedals in use (name, VID:PID), or *none*. `<` `>`: auto-detect → each connected controller → auto, for a pair the game did not recognise. |
| Use pedals | On / Off. Off = the controllers keep the rudder and brakes. |
| Rudder / L toe / R toe | The axis and its live value (`RZ [----\|##--] +42`, `X [####----] 50%`). `<` `>` picks the axis; **select flips it** (shown as *inv*). Right pedal forward must read +; each toe brake 0 % released, 100 % on the floor. |
| Dead zone rud / toes | `<` `>`, 1 % steps. |
| CALIBRATE (guided) | Feet off → push the RIGHT pedal → LEFT toe brake → RIGHT toe brake → sweep everything → FINISH. Shows each axis live with the ends seen. Skip / Cancel on every step. |
| Reset to detected | Back to the detected mapping, uncalibrated. |

Not in VR yet: the replay / review screen, lessons, challenges, the joystick setup, the full Options panel (all
OnGUI). The rows above are the settings that matter in a headset.
The autopilot disconnect belongs to the widget, which does not exist on a Quest.

## Recording

**In the headset: hold A and B together for a second** (or VR menu → Record video). A red **REC 0:42** shows high
in the left of your view while it runs; hold A + B again to stop, and **Saved AeroQuest-20261102-093005.mp4** shows
for a few seconds. The indicator and that message are never in the film.

What is recorded is what you see, from your head: 1280 × 720 at 30 fps, H.264, about 60° tall (91° wide), with the
game's sound (engine, wind, radio) as AAC at 48 kHz stereo (the first clips were 24 kHz; see below). Mono, from
between your eyes, and without the lens's distorted edge. It has everything the left eye has, including the
aerobatic sight on the wingtip (it used to be missing from every clip), except the REC indicator and its messages.

The water in a clip is drawn as deep water without the lake's mirror image. The recording camera has no depth
texture of its own (a depth pass would double what recording costs), and the water used to read the headset
camera's instead. That painted a flickering light-blue grid over the sea in every clip taken near the water.

**Files** go to the game's own folder on the headset, `/sdcard/Android/data/com.tyrelfrisby.aeroplayground.quest/files/Movies/`
(no storage permission needed), and **reach the Mac with**

    zsh tools/deploy/quest-record.sh --pull        # → ~/Movies/AeroQuest/, skips the ones already there

**Over adb, without the controllers** (the operator's way to take a test clip):

    adb shell am broadcast -a com.tyrelfrisby.aeroplayground.quest.RECORD --es cmd start
    adb shell am broadcast -a com.tyrelfrisby.aeroplayground.quest.RECORD --es cmd stop     # or: toggle
    zsh tools/deploy/quest-record.sh --pull
    ffmpeg -ss 5 -i ~/Movies/AeroQuest/AeroQuest-<…>.mp4 -frames:v 1 /tmp/frame.png        # one frame to look at

The game must be running and in the foreground. The command is read twice a second, and nothing happens while the
app is paused. Taking the headset off pauses it: cover the proximity sensor, or turn it off with
`adb shell am broadcast -a com.oculus.vrpowermanager.prox_close` (and `…automation_disable` to give it back).
Only the adb shell can send it: the receiver requires `android.permission.DUMP`, which no ordinary app has. A
`start` while recording or a `stop` while idle does nothing. Each command logs
`[QuestRec] adb command 'start' while idle: start`. If that line never appears, look for
`QuestRecorder: listening for adb` in `adb logcat -s QuestRecorder`. Rule and tests: `QuestRecording.AdbCommand`.

**What the log says** (`adb logcat -s Unity QuestRecorder | grep -E "QuestRec"`):

    [QuestRec] audio output 48000 Hz, Stereo                                  (at launch; or "24000 Hz … -> asked 48000: accepted")
    [QuestRec] start: audio 48000 Hz 2 ch from the listener's mix, game volume 1.00, listener volume 1.00
    QuestRecorder: audio track: 48000 Hz, 2 ch, audio/mp4a-latm
    [QuestRec] stop: audio 48000 Hz 2 ch, 42.1 s captured, peak -6.3 dBFS — ok; 0 samples dropped (main thread behind)

The stop line's verdict is `ok`, `nearly silent` (below −40 dBFS: the game itself was quiet), `SILENT` (the tap
heard nothing), or `clipping` (the game's mix hit full scale). For the record: Ty's 13:15 Pitts clip peaked at −44 dBFS
because the whole clip is the wreck in the water, engine stopped and no airspeed. The 13:17 Great Lakes clip has full
sound: −11 dB on average, and clipping at full scale through the dive at 1:30–1:40. Both are 24 kHz, and both have
the audio as the first track. The tracks are now written video first.

A recording is only a playable file once it is closed, so it is closed for you when the app is paused (headset
off, the Meta menu's quit) or quits; it also stops by itself after 30 minutes (about 1.8 GB).

**Why A + B.** Every single button already flies something, and a hold on a button that acts on its press does that
thing first: holding the right stick click would cycle the gear (or release the tow) before it started recording,
and the left stick click is recentre and, held, the menu. A and B are the one pair that does nothing when both are
down: nose-up and nose-down trim cancel. Rule and tests: `QuestRecordChord` in `src/FlyingGame.Core/Vr/QuestRecording.cs`,
`tools/FlightTests/QuestRecordingTests.cs`.

**Why the game records itself** rather than Android's screen capture: MediaProjection asks for consent in a 2D
system dialog every session, which takes an immersive OpenXR app out of focus every time, and it is not a supported
way to capture the compositor on Horizon OS; Meta's own capture (Meta button → Camera) has no intent another app may
send. So a second, mono camera at your head renders 30 times a second (not 72), the GPU packs the frame to YUV
(`Shaders/QuestRecordPack.shader`), and `Plugins/Android/QuestRecorder.java` encodes it with MediaCodec on its own
threads. The cost shows in the `[QuestPerf]` line, which says `rec ON (… sent, … dropped)` while it runs.

### From the Mac, without the game's recorder

    zsh tools/deploy/quest-record.sh                # the LEFT eye, with sound, until Ctrl-C → ~/Movies/AeroQuest/<date>_<time>.mp4
    zsh tools/deploy/quest-record.sh --full         # both eyes, uncropped
    zsh tools/deploy/quest-record.sh --watch        # and show it live in a window
    zsh tools/deploy/quest-record.sh --no-audio     # video only
    zsh tools/deploy/quest-record.sh -s SERIAL …    # if more than one device is attached

It uses scrcpy (Homebrew) over the USB cable and records the headset's own mirror of the display: what the
compositor shows, lens edge and all, at the panel's resolution. The sight is part of the scene (the model's own
`Sight_` bars, shown only to the camera in the cockpit by `CockpitSights`), not an OpenXR compositor layer, so the
mirror has it whenever the left eye does. The left eye is the left half of `adb shell wm size`;
`--crop W:H:X:Y` sets your own. Sound is the headset's app audio, still playing in the headset (Android 13+; on an
older system scrcpy records video only and says so). Ctrl-C stops it and scrcpy closes the file. Without scrcpy it
falls back to `adb shell screenrecord`: no sound, no crop, 3 minutes at most.

## Hands (no controllers)

Put the controllers down and the Quest switches to hand tracking; **you see your own hands** (the gloves, drawn from
the headset's 26 tracked joints per hand, fingers and all), and **they take hold of the cockpit's controls** the way the
controllers do — reach to the control and either **pinch it** (thumb to index finger) or **close your hand round it**.
Open your hand to let go. Switching is automatic: pick a controller up and that hand is the controller again, put it
down and after a moment it is your hand. Design and numbers: [vr-cockpit.md, Phase 3](vr-cockpit.md#phase-3-tracked-hands-and-the-2-33s-own-handles).

| Gesture | Does |
|---|---|
| **Pinch or grab a control** | Takes it: the 2-33's dive-brake handle, trim lever and tow release, the stick, the throttle, the latches and everything in *Grabbing* above |
| **Left pinch, held 1 s** (in free air) | Opens the VR menu |
| **Right pinch, held 1 s** (in free air) | Recentre |
| **Point + pinch** (menu open) | Selects a row |

A pinch on a control is the control's: holding the dive-brake handle with a pinch for a minute never opens the menu
or recentres. With nothing held the aeroplane keeps whatever the controls were left at (a reversible aeroplane's
surfaces then trail, exactly as with a thumb lifted off a pad); the rudder and the A / B / X / Y buttons need the
controllers.

### The 2-33's handles (either hand or a controller's grip)

| Handle | Where (front seat) | Moves | Does |
|---|---|---|---|
| **Dive brakes** | the lever on the LEFT wall, knob a hand's span below and ahead of your left shoulder (rear seat: the same, on its own wall) | **pull AFT** to extend, 33° of swing | 0 → 100 % spoiler; the **last 15 %** of the pull is the **wheel brake** (a click in the controller where it starts). It **stays where you leave it**. The thumbstick and the touch strip move the same handle: whichever moved it last, the other carries on from there |
| **Trim** | the red ball lower on the left wall | aft = **nose up**, 40° | the same trim A / B move; a click at each notch |
| **Tow release** | the yellow ball at the bottom centre of the front panel (rear: left end of the seat-back tube) | **pull 5 cm straight out** | releases the tow at the end of the pull; springs back when let go. With nothing on the hook it just moves (and the log says so) |
