# USB rudder pedals

Ty's item 11 (2026-11-02): fly the rudder and the toe brakes with real pedals. That means the Quest over USB-C OTG,
and iOS / Mac where the platform allows it.

**Status: code only.** It has not been built, and it has not met a real pair of pedals. Detection, mapping, invert,
dead zone, calibration, the brake split, hot-plug and fallback are all exercised by
`tools/FlightTests/RudderPedalTests.cs`, using simulated HID devices.

## Plugging in

| Platform | How | Read through |
|---|---|---|
| **Quest 3 / 3S** | A USB-C OTG adapter (or a hub with power pass-through) into the headset's USB-C port, then the pedals' USB-A cable into that. | Unity's **Input System**, Android backend. The Quest build turns it on for the APK only (see *The Quest build*). |
| **Mac app** | Any USB port. | The game's own HID reader (`AeroNative` `HJ_*`), the same one the joystick setup uses. |
| **Unity editor** | Any USB port. | The old Input Manager's per-joystick axes (`PJoy1A1` … `PJoy4A12`). |
| **iPhone / iPad** | **Not supported.** iOS gives apps gamepads (MFi / Xbox / PlayStation, through GameController) but not generic USB HID devices. The pedals are simply never found, and nothing breaks. | — |

Once pedals are connected, the rudder and toe brakes work at once, with no menu needed. The log says what was found:

    [Pedals] reading game controllers through InputSystem
    [Pedals] connected: "Thrustmaster T.Flight Rudder Pedals" 044F:B679 via Android, 3 axes [x y rz], 0 buttons -> PEDALS (score 215: known model Thrustmaster T.Flight Rudder Pedals (TFRP), name says pedals, pedal layout (rudder + two toe axes, no buttons), Thrustmaster)
    [Pedals] using "Thrustmaster T.Flight Rudder Pedals" 044F:B679 (defaults): rudder RZ, left brake X, right brake Y
    [Pedals] disconnected: "Thrustmaster T.Flight Rudder Pedals" 044F:B679 (Android)
    [Pedals] no pedals now: rudder and brakes back on the controllers

On the Quest: `adb logcat -s Unity | grep Pedals`. On the Mac: Player.log.

Every game controller that appears is logged once, pedals or not, with its name, VID:PID, backend, axis names and
button count. That line is what to send when a pair is not recognised.

## What they do

Pedals join the **same** control path as the touch pads, a joystick and the Touch controllers
(`TouchFlightControls.PublishToDriver`). They do not drive the aeroplane on their own:

- **Rudder** adds to the hand's rudder (the left thumbstick X, the pad, a joystick twist), and the total is
  clamped. Pedals at rest leave the thumbstick working, and the two never fight over who wins.
- **Toe brakes:** each wheel gets the harder of its toe brake and the hand's brake. The hand's brake is the left
  trigger, biased by the hand's rudder as before. The sim receives this as its usual brake and bias:
  `brake = (L + R) / 2`, `bias = (R − L) / (L + R)`. The sim brakes the left wheel `brake × (1 − bias)` and the
  right `brake × (1 + bias)`, so each wheel gets exactly its own pedal. Differential braking steers a taildragger
  on the ground.
- **Cockpit:** the moving pedals (the Pitts' carved pedals, the rig types' pedal nodes) already follow the sim's
  rudder, so they follow your feet. The Extra's toe-brake rig parameters follow the sim's brake and bias, so they
  follow your toes.
- **Feet on the pedals hold the rudder.** On a reversible (cable) aeroplane, a centred thumb lets the rudder trail
  with the air loads. With pedals active it does not.
- **No pedals, switched off, or calibrating:** nothing changes. With pedals unplugged the code takes the same path
  it took before pedals existed.

## Detection

Detection does not rely on a VID/PID table. A device is taken for pedals when its score reaches 50:

| Evidence | Score |
|---|---|
| A known model (VID:PID below) | +100 |
| The name says so: *rudder*, *pedal*, *crosswind*, *TFRP*, *T-Rudder*, *pendular*, or starts with *MFG* | +60 |
| The layout says so: a rudder axis (rudder / rz / z) and two toe axes (x / y / brake / gas), at most 4 axes, at most 2 buttons, no hat, no throttle or slider, not a gamepad | +50 |
| A sim-hardware maker's vendor ID: Thrustmaster 044F, Logitech 046D, Saitek 06A3, VKB 231D | +5 |
| A hat, or a throttle / slider axis | −30 |
| 6 or more buttons (a stick) | −40 |
| The platform says it is a gamepad | −100 |

Known models (a hint, nothing more; the IDs are the ones I am sure of):

| VID:PID | Model | Default axes |
|---|---|---|
| 06A3:0763 | Saitek / Logitech Pro Flight Rudder Pedals | rudder Rz, left toe X, right toe Y |
| 044F:B679 | Thrustmaster T.Flight Rudder Pedals (TFRP) | rudder Rz, left toe X, right toe Y |

**MFG Crosswind, VKB T-Rudder and Thrustmaster T-Rudder / TPR** are found by name and by layout. With no known
model, the default mapping goes by axis name: rudder = rudder / rz / z, left toe = x / brake, right toe = y / gas.
Where the platform gives no names (the Mac, the editor), it goes by HID order: X = left toe, Y = right toe, the
third axis = rudder. **Whatever the default, the guided calibration finds the real axes.**

## Mapping and calibration

The settings are in **Options → Controls → Rudder pedals** (iPhone / iPad / Mac) and in the **VR menu → Rudder
pedals** page (the Quest). Both edit the same thing. Every change is saved at once, per device: by VID:PID where
the platform reports one, else by name. The same pedals on another port or another day keep their settings.

| Setting | Options (flat screen) | VR menu |
|---|---|---|
| Device | `<` `>` beside the name: auto-detect → each connected controller → auto. Use it when detection missed your pedals. Gamepads are not offered. | **Device** row, `<` `>` |
| On / off | *Fly the rudder and the toe brakes with the pedals* | **Use pedals** |
| Which axis | `<` `>` on the Rudder / Left toe brake / Right toe brake rows (none, axis 1 … n) | `<` `>` on the **Rudder / L toe / R toe** rows |
| Invert | **Inv** | Select (trigger / A) on that row |
| Dead zone | **dz−** / **dz+**, 1 % steps, 0–25 % | **Dead zone rud** / **Dead zone toes**, `<` `>` |
| Calibrate | **Calibrate (guided)** | **CALIBRATE (guided)** |
| Reset | **Reset to detected**: back to the detected mapping, uncalibrated | **Reset to detected** |

Each row shows its axis live. Check that **right pedal forward reads +**, and that **each toe brake reads 0 %
released and 100 % on the floor**. If one reads backwards, press Inv (or select the row in VR).

**The guided calibration** (the pedals stay out of the flying while it runs):

1. **Feet off, rudder centred, hold still** (1 s). This learns where each axis rests.
2. **Push the RIGHT pedal fully forward and hold.** The axis that moves is the rudder. The direction it moves
   sets its invert.
3. **Centre, then the LEFT toe brake fully, and hold.** This finds the left brake's axis.
4. **The RIGHT toe brake fully, and hold.** This finds the right brake's axis.
5. **Sweep:** full left and right rudder, and each toe brake to the floor, a few times. Then **FINISH**.

The two ends of every axis are learnt live from the first frame. The rudder's centre is where it rested. Each
brake is released wherever it rested, so a toe axis that rests at the top is inverted automatically. Any step can
be skipped (Skip), and that axis keeps its current assignment. An axis that never moved keeps its old range.
**Cancel** changes nothing. Leaving the page mid-way cancels too, and the log says so. A calibration nobody can see
would leave the pedals dead.

Before calibration, an axis uses its nominal range. Android reports brake / gas / throttle axes as 0…1 and
everything else as −1…1, so a released Android brake axis reads 0 %, not 50 %.

## Hot-plug and fallback

- **Plug in mid-flight:** the pedals take over the rudder and brakes on the next frame, with their saved profile.
- **Unplug mid-flight:** the next frame is back on the controllers (or pads, or joystick). Nothing stays latched.
- **Unplug while calibrating:** the calibration stops and nothing is saved.
- **Nothing connected:** the rudder and brakes stay on the controllers, exactly as before.

On the Quest, the Input System reports hot-plug itself. The Mac and editor readers look again once a second.

## The Quest build

**No permission and no manifest entry is required.** A USB HID game controller reaches an app through Android's
input stack, not the USB host API. The Quest manifest step (`QuestManifest`) adds one line anyway:
`<uses-feature android:name="android.hardware.usb.host" android:required="false"/>`. It is informative only, and
`required="false"` filters out no device.

**The Input System's Android backend is on for the APK only.** The project's input handler is Old
(`activeInputHandler: 0`), and the iPhone / Mac builds keep it that way. `QuestBuild.BuildQuest` sets it to
**Both** for the length of the Android build and restores it in `finally`, as it does for the orientation. That
gives the APK both:

- the Input System, with each device's VID/PID and Android's own axis list;
- the old Input Manager, which everything else in the game still uses.

The Touch controllers are read through `UnityEngine.XR.InputDevices` either way. The build log says
`[QuestBuild] active input handler 2 (0 Old, 2 Both; was 0)`. `AERO_QUEST_OLD_INPUT=1` builds exactly as before,
and the pedals then fall back to the old Input Manager's per-joystick axes. They still work there, but without
VID/PID or axis names.

**Why the axes are read from the state, on Android.** The Input System's `AndroidJoystick` layout exposes only the
stick, X and Y. The rudder (Rz) and Android's RUDDER / BRAKE / GAS axes exist only in the device's state block, a
float per Android axis code. `RudderPedals` copies that state once a frame (`CopyState`, no garbage) and reads the
axes the device's description lists (`motionAxes`).

## Code

| Where | What |
|---|---|
| `src/FlyingGame.Core/RudderPedals.cs` | Engine-free: `PedalCatalog` (detection, defaults), `PedalAxisCal` (centre / ends / invert / dead zone), `PedalProfile` (persisted, invariant culture), `PedalBlend` (one input path, brake split), `PedalCalibrator`, `PedalSession` (hot-plug, choice, logging, menu text). |
| `unity/Assets/_Project/Scripts/Bridge/RudderPedals.cs` | The three device readers, PlayerPrefs, the `[Pedals]` log, the calibration watchdog. |
| `TouchFlightControls.PublishToDriver` | Where the pedals join the controls. |
| `OptionsPanel.RudderPedalsSection`, `QuestMenu.BuildPedalRows` | The two menus. |
| `QuestBuild` | The input handler for the APK; the manifest line. |
| `ProjectSettings/InputManager.asset` | `PJoy{1-4}A{1-12}`: the old Input Manager's per-joystick axes (the fallback). |
| `tools/FlightTests/RudderPedalTests.cs` | 45 tests. |

## Open

- **Never run against real pedals.** The first build should be checked with `adb logcat -s Unity | grep Pedals`
  while plugging them in. The `connected:` line says whether they were recognised, and with which axes.
- On the Mac, the joystick setup now takes the first HID device whose name is **not** pedals as the stick.
  Before, it took device 0, which is the pedals if they were plugged in first. In the editor, the joystick
  setup's `WJoy` axes still read all joysticks at once, so pedals plugged in there can disturb a calibrated stick.
- The Pitts' carved pedals pivot for rudder only. A toe-brake tilt of the pedal face is not modelled.
