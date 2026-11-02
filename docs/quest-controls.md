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
| **Left grip (hold) + move hand** | **Grab the throttle lever** | Squeeze, move your hand forward / back, let go: 20 cm of hand travel is the full lever. Starts from wherever the lever is, so a reset never makes it jump. Same POWER card. |
| **Left trigger** | Wheel brakes (analog) | Rudder biases them left / right, as on the touch pad (toe-brake feel). |
| **Right trigger** | Guns (held) | Only where the guns are hot (combat zone / dogfight). Otherwise nothing. |
| **Right grip** | Smoke on / off | One toggle per squeeze. |
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

Dead zones: thumbsticks have a 0.10 hardware dead zone (rescaled, so full deflection is still full), the throttle
rate 0.25. The aeroplane's own dead zone and expo (`AircraftConfig`) come on top, exactly as for the touch pads.
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
| **Cub** | Not yet: its needles, sticks and pedals are separate nodes with their rig in the GLB, but there is no rig file for the game to read. | Not yet. | Not yet. |
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
| Aircraft | The fleet, cycled. In flight it applies on the row above. |
| Start | In the air · On the runway · On final · Aerobatic box · Combat zone · In a thermal. |
| Seat | The seats of the aircraft **that will fly** (on the landing page: the one on the Aircraft row). Remembered per aircraft, by seat (rear / front / pilot …), so the rear seat is the rear seat in every type. Nothing chosen = the solo / PIC seat: **rear** in the Pitts, Great Lakes, Extra and Cub, **front** in the 2-33, **left** in the 172. In flight your head goes to the new seat's eye at once. |
| View | Cockpit / outside (the chase view, levelled to the horizon). |
| HUD in cockpit | **Off** (default) / On. Persisted. In the headset's cockpit view the aeroplane's own instruments are what count, so the green HUD is not drawn there unless this is On. Outside views keep the HUD as before, and the iPhone / iPad / Mac are unchanged. Only the HUD's lines exist in the headset (its numbers are a flat overlay). |
| Display | 72 Hz (default) / 90 Hz. Persisted. |
| Rudder pedals | none / ON / connected, OFF / calibrating. Opens the **Rudder pedals page** (below). |
| Record video | Off / **ON 0:42** / saving … Starts or stops a recording, the same as A + B held. Starting it in flight also closes the menu, so the film starts on the flying. |
| Recentre the view | Same as the left stick click. |
| Reset flight | The touch pad's RESET. |
| Bail out / Deploy BRS / Eject | Asks twice: the first press arms it for 3 s. |

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
game's sound (engine, wind, radio). Mono, from between your eyes, and without the lens's distorted edge. The HUD's
lines are not in it.

**Files** go to the game's own folder on the headset, `/sdcard/Android/data/com.tyrelfrisby.aeroplayground.quest/files/Movies/`
(no storage permission needed), and **reach the Mac with**

    zsh tools/deploy/quest-record.sh --pull        # → ~/Movies/AeroQuest/, skips the ones already there

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
compositor shows, lens edge and all, at the panel's resolution. The left eye is the left half of `adb shell wm size`;
`--crop W:H:X:Y` sets your own. Sound is the headset's app audio, still playing in the headset (Android 13+; on an
older system scrcpy records video only and says so). Ctrl-C stops it and scrcpy closes the file. Without scrcpy it
falls back to `adb shell screenrecord`: no sound, no crop, 3 minutes at most.

## Hands (no controllers)

Put the controllers down and the Quest switches to hand tracking. **Hands do not fly the aeroplane.** A tracked
hand cannot hold a stick or a throttle lever with anything like the precision a flight model needs, and a hand
resting on a knee pinches all the time; what you get is:

| Gesture | Does |
|---|---|
| **Left pinch, held 1 s** | Opens the VR menu |
| **Right pinch, held 1 s** | Recentre |
| **Point + pinch** (menu open) | Selects a row |

While your hands are tracked the aeroplane keeps whatever the controls were left at (a reversible aeroplane's
surfaces then trail, exactly as with a thumb lifted off a pad). Pick the controllers up again to fly.
