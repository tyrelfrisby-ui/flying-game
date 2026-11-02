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
| **Left thumbstick Y** | **Throttle lever, as a rate** | Push to open, pull to close, **let go and it stays**. Idle to full in 2 s. On a glider the same lever is the spoiler handle (forward = stowed, back = out), exactly as the touch pad's upper band. |
| **Left grip (hold) + move hand** | **Grab the throttle lever** — *outside views only* | Squeeze, move your hand forward / back, let go: 20 cm of hand travel is the full lever. Starts from wherever the lever is, so a reset never makes it jump. **In the cockpit the throttle is grabbed where it is** (below). |
| **Left trigger** | Wheel brakes (analog) | Rudder biases them left / right, as on the touch pad (toe-brake feel). |
| **Right trigger** | Guns (held) | Only where the guns are hot (combat zone / dogfight). Otherwise nothing. |
| **Right grip** | Smoke on / off | One toggle per squeeze. In the cockpit only when the hand is NOT on a control (closing on the stick never toggles the smoke). |
| **A (hold)** | Pitch trim **nose up** | 0.5 of the trim range per second, the same rate as a joystick's TRIM buttons. |
| **B (hold)** | Pitch trim **nose down** | B sits further from the thumb: "forward" = nose down, like a trim wheel. |
| **X** | Flaps one notch **down** | 0 → 50 → 100, from where the flaps actually are. |
| **Y** | Flaps one notch **up** | |
| **Right stick click** | Gear up / down | Retractable types. On a glider it is **TOW** on the runway and **RELEASE** on tow. |
| **Left stick click** | **Recentre** | Puts your head where it is now at the seat's eye, facing the nose. |
| **Menu (☰, left)** | **VR menu (pauses)** | The small button with three lines on the LEFT controller, below the thumbstick. Press again, or B / Y, to close. |
| **Left stick click, held 1 s** | **VR menu** too | The fallback if ☰ is hard to find: it recentres on the press (harmless) and opens / closes the menu after a second. |
| **Meta button (hold)** | The headset's own recentre | The game follows it: same as the left stick click. |

Dead zones: thumbsticks have a 0.10 hardware dead zone (rescaled, so full deflection is still full), the throttle
rate 0.25. The aeroplane's own dead zone and expo (`AircraftConfig`) come on top, exactly as for the touch pads.
One stick carries rudder and throttle, so whichever axis is clearly dominant wins: a bootful of rudder with your
thumb drifting 0.3 forward does not open the throttle, and easing the power off does not kick the rudder.

## Grabbing the aeroplane's own controls (cockpit view)

Reach out with the controller to the control itself; within **6 cm** the controller ticks lightly. **Squeeze the grip**
to take hold (a firmer tick); your drawn hand snaps onto it. Let go by opening the grip. Nothing can be grabbed while
the VR menu is open, and grabbing never moves your head. The design is in [vr-cockpit.md](vr-cockpit.md).

| Control | Hand | How it moves | Let go |
|---|---|---|---|
| **Stick** | either (right, normally) | Follows your hand about its real pivot: roll gimbal on the torque tube, pitch yoke on it. Limits from the model's rig (Extra / Great Lakes: ±18° roll, 18° aft, 12° fwd); proxy otherwise. The surfaces are linear in stick angle (no expo on a real stick). The right thumbstick does nothing while you hold it. | **Free stick**: on a reversible type it trails with the surfaces' hinge moments (the sim's free-control model), and you see it move. |
| **Throttle** | either (left, normally) | On its real arc on the left of the seat, both cockpits: move your hand forward = power. Sideways hand movement does nothing. | Stays where it was left. The left thumbstick Y still works as before when you are not holding it. |
| **Canopy latch, Pitts** | either | Grab the **black ball** on top of the rear panel and slide it **left / right** (3 in). **Fully RIGHT = LOCKED**, with a detent click at the end; pull 1 cm left to come out of it. | Stays where it was left. |
| **Canopy latch, Extra** | either, or one each | On the LEFT canopy frame, ahead of you: a fixed REAR handle and a spring-loaded FRONT one. Grab the pair with one hand and pull the **trigger**, or grab each handle and close your hands together, or pull the front handle aft on its own (35 mm). Past about 80 % the pins come out = UNLOCKED (click). The rear handle alone does nothing. Your hand on them is on the canopy: push up to open it. | The front handle springs forward; with the canopy shut the pins snap in (click). With it open they cannot. |
| **Canopy jettison** | either | **Pitts:** the RED knob on the far RIGHT, where the bow tubes meet the canopy's lower rail (each cockpit). **Extra:** the red emergency release on the RIGHT sill. Grab and pull toward you, firmly (8 mm before it gives), all the way. | One-way: it stays out. With the latch open the canopy leaves at any speed; with the latch shut it stays until you open the latch. Short of the end, it springs back and nothing happens. |

Opening the canopy, the belts, the headset plug, the sills and the ripcord: see "Bailing out by hand" below. The rudder is still the **left thumbstick X**, and your feet ride the pedals.

**You see yourself:** arms, gloved hands, legs and boots, posed from the controllers and the pedals. With a controller
put down, that hand rests on its control. Hidden in outside views.

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
small world-space menu. **It opens by itself at launch** (the landing page is invisible in the headset) and in flight
from the **left ☰ button** or the **left stick click held for a second**. It pauses the flight while it is open
(the title says PAUSED). On the first three flights a small card low in the cockpit says "MENU / OPTIONS: press the
left MENU button (or hold the left stick click)" for 12 s, below the windscreen line so it never covers the
horizon; once you have opened the menu yourself in flight it is not shown again (persisted).

**Point** with either hand and pull the **trigger** (or press **A / X**), or move the highlight with a
**thumbstick** and push it **left / right** to change a value. **B / Y / ☰** closes it in flight.

| Row | Does |
|---|---|
| FLY / RESUME | Starts a **free flight** with the choices below (lessons, challenges, weather and multiplayer have OnGUI cards of their own and are not reachable in VR yet). In flight: closes the menu. |
| RESTART FLIGHT | Starts again with the current aircraft / start choice. |
| Aircraft | The fleet, cycled. In flight it applies on RESTART. |
| Start | In the air · On the runway · On final · Aerobatic box · Combat zone · In a thermal. |
| Seat | The seats of the aircraft **that will fly** (on the landing page: the one on the Aircraft row). Remembered per aircraft, by seat (rear / front / pilot …), so the rear seat is the rear seat in every type. Nothing chosen = the solo / PIC seat: **rear** in the Pitts, Great Lakes, Extra and Cub, **front** in the 2-33, **left** in the 172. In flight your head goes to the new seat's eye at once. |
| View | Cockpit / outside (the chase view, levelled to the horizon). |
| HUD in cockpit | **Off** (default) / On. Persisted. In the headset's cockpit view the aeroplane's own instruments are what count, so the green HUD is not drawn there unless this is On. Outside views keep the HUD as before, and the iPhone / iPad / Mac are unchanged. Only the HUD's lines exist in the headset (its numbers are a flat overlay). |
| Display | 72 Hz (default) / 90 Hz. Persisted. |
| Recentre the view | Same as the left stick click. |
| Reset flight | The touch pad's RESET. |
| Bail out / Deploy BRS / Eject | Asks twice: the first press arms it for 3 s. The timed SHORTCUT (outside view, automatic chute); the hands-on way is the table above. |

Not in VR yet: the replay / review screen, lessons, challenges, the joystick setup, the full Options panel (all
OnGUI). The rows above are the settings that matter in a headset.
The autopilot disconnect belongs to the widget, which does not exist on a Quest.

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
