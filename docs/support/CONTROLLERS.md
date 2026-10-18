# Controllers: joysticks, gamepads and RC transmitters

Three different questions hide behind "my controller doesn't work", and the first job is to find out which
one. **Which device, and which platform?**

| | Mac | iPad / iPhone |
|---|---|---|
| USB joystick, HOTAS, pedals, RC dongle | **Yes** — read directly, any stick | **No** — see "Why not on iPad" |
| Bluetooth gamepad (Xbox, PlayStation, Switch Pro, MFi) | Yes | Yes |
| Keyboard | Yes | Yes, with a keyboard attached |
| Touch | — | Yes, the default |

## Mac: setting up any joystick

**OPTIONS → Joystick setup.** It is a guided calibration: it names what it is looking for, shows the raw axes
with the one it is watching lit up, and has BACK / SKIP / CANCEL and FINISH. The world pauses while it runs.
It learns, per axis: which control it is, its centre, both of its ends, and its dead zone. Then it asks which
button does what, from this list — brakes (held), guns (held), flaps down, flaps up, flaps 50 %, gear,
trim nose up, trim nose down, autopilot disconnect, pause/review, reset, change view.

The profile is saved **per joystick name**, so a second stick does not overwrite the first, and plugging the
first one back in picks its profile up again.

Common answers:

- **"The game doesn't see my stick."** The button in OPTIONS reads "No joystick" when nothing is detected. On
  the Mac the game reads joysticks through macOS's own HID manager, so it does not matter whether the game is
  the front window — but the stick does have to be plugged in before the setup is opened. Unplug, replug,
  reopen OPTIONS.
- **"An axis is backwards."** Redo the setup for that axis; the calibration records both ends, so pulling the
  stick the wrong way during the step is what reverses it.
- **"It drifts / won't centre."** Redo the setup, and if it still creeps, raise the dead zone:
  OPTIONS → CONTROL FEEL → the axis's dead zone. "Aircraft defaults" there puts everything back.
- **"It's too twitchy / too dull."** That is expo, not a fault. OPTIONS → CONTROL FEEL → expo per axis. Each
  aeroplane ships with its own value; the slider says "(aircraft)" while it is using it.

## Spektrum, and RC transmitters generally

On the **Mac**, a Spektrum **WS3000T** dongle is a plain USB HID joystick: plug it in, bind the transmitter to
it as you normally would, then run OPTIONS → Joystick setup and calibrate it like any other stick. An MLP6DSM
or any DSM2/DSMX transmitter through that dongle works this way.

On **iPad and iPhone it does not work yet, and that is not something they can fix.** iOS gives apps no access
to a generic USB joystick at all — GameController only accepts Xbox, PlayStation, Switch Pro and MFi pads, and
the HID frameworks are macOS-only. So the dongle is invisible to the app however it is plugged in.

Two things to offer instead:

1. **Fly the iPad with the Mac's joystick, over the same Wi-Fi.** This is built and it works today:
   OPTIONS → "Fly with the Mac's joystick" on the iPad, and on the Mac OPTIONS → "Share this joystick with
   iPad / iPhone". Both devices on the same network; iOS will ask once for local-network permission and it
   has to be allowed.
2. **A box is coming** (`docs/RC-LINK-SPEC.md`): an ESP32 with a USB host port that the dongle plugs into and
   which talks Bluetooth LE to the iPad. The boards were ordered 2026-10-14. **Do not promise a date** — the
   firmware is not written and the shape of the product is still Ty's decision. "It is being worked on, and I
   can't give you a date" is the whole of what we can say.

## Bluetooth gamepads on iPad and iPhone

Pair it in iOS Settings first; the game picks it up with no setup step. The fire button is A or the right
shoulder. The phone's own volume, Action and Camera Control buttons are reserved by iOS and cannot be used.
