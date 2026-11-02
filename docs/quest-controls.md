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
| **Left grip (hold) + move hand** | **Grab the throttle lever** | Squeeze, move your hand forward / back, let go: 20 cm of hand travel is the full lever. Starts from wherever the lever is, so a reset never makes it jump. |
| **Left trigger** | Wheel brakes (analog) | Rudder biases them left / right, as on the touch pad (toe-brake feel). |
| **Right trigger** | Guns (held) | Only where the guns are hot (combat zone / dogfight). Otherwise nothing. |
| **Right grip** | Smoke on / off | One toggle per squeeze. |
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
| Bail out / Deploy BRS / Eject | Asks twice: the first press arms it for 3 s. |

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
