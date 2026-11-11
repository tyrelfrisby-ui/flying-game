# Aero RC Link — fly the iPad and iPhone with an RC transmitter

Owner request 2026-10-12: "a hardware device like a raspberry pi etc that the dongle plugs into and it bluetooths to the
ipad/iphone".

Owner decision 2026-11-10, choosing the build shape after the M5Stack boards arrived: **"ESP32 reads PPM/SBUS → BLE to
the phone. The board sits on the transmitter's trainer port, decodes the channel stream and presents itself as a BLE
gamepad. The phone needs no special code — it sees a standard controller."** Picked over a custom BLE protocol and over
a wired USB-C link.

**The firmware is in `tools/rc-link/`. Its README is the operating manual; this file is the decision record.**
Everything written so far is **untested** — nobody has flashed it, nobody has the hardware. The pure half (decoders,
calibration, failsafe, mapping, packets, throttle servo) is covered by 199 host checks; `src/main.cpp` is not covered at
all.

## Why a box

iPadOS and iOS give apps no access to a generic USB joystick. GameController only accepts Xbox, PlayStation, Switch Pro
and MFi controllers, and IOKit HID and CoreHID are macOS-only. So on the Mac the game reads a joystick directly, and on
iOS it needs something in between. That has been the premise since October and it still holds.

## The finding that changes the shape — 2026-11-10

**The trainer port and the decode are right. "The phone needs no special code" does not hold for a BLE HID gamepad on
iOS, and the counter-evidence was already in this file** (the line above about GameController). Unity on iOS gets its
pads from Apple's GameController framework and from nowhere else — `GamepadInput.cs` says so in its own comment: "the
player's own GameController observer (GCControllerDidConnectNotification, compiled into UnityFramework) hands it to the
Input System". A home-made BLE HID gamepad pairs with an iPhone and is then invisible to every app. Sources are listed
in `tools/rc-link/README.md` §2; the short version is that this is reported repeatedly on Apple's own forums, that
keyboards and mice over BLE HID *do* work on iOS while the gamepad profile does not, and that one report on iOS 27 logs
`Un-authenticated game controller device attached`.

**Not verified on hardware by me.** `tools/rc-link/README.md` §7 is a thirty-second test that settles it either way,
and it is deliberately the first thing to do after the two-minute smoke test.

### And a second finding, from the code rather than from Apple

Even if iOS did see the pad, a standard gamepad cannot carry a transmitter. Measured off `GamepadInput.Read()` and
`GamepadShaping.cs`, not assumed:

| | |
|---|---|
| **Four analogue channels, not six.** | The triggers are not free: `GamepadShaping.Rudder` adds (RT − LT) *to the rudder*, so an aux channel on a trigger is a standing rudder deflection. The two knobs have nowhere analogue to go. |
| **The throttle is a RATE.** | `GamepadShaping.ThrottleRate` + `Lever`: full travel in 2.0 s through a 0.25 dead zone, because a thumb stick springs back. An RC throttle does not. Mapped straight across it becomes a four-position throttle — above centre+25 % ramps to full, below ramps to idle, the middle does nothing. |
| **No gear.** | `GamepadInput.Frame` has no gear field. The gear / water-rudder slot is reached from the touch tray and from `JoystickInput`'s `gear` action, never from a pad. |
| **Double expo.** | The pad path applies the game's 0.65 expo on top of whatever curves are dialled into the transmitter. |

## The chain, as built

```
transmitter ──PPM or SBUS, trainer port──▶ ESP32 (M5Stack) ──┬── Wi-Fi: Bonjour + UDP "JoyLink" ──▶ iPad / iPhone / Mac
                                                             └── BLE HID gamepad ────────────────▶ Android / Mac / Windows / PC sims
```

Both transports are built, both can run at once, and **neither needs a line changed in `unity/`.**

**The default is JoyLink, and it is not a new protocol** — it is the one the game already ships. `JoyLink.cs` builds and
reads the packets, `Plugins/iOS/JoyLink.mm` is the Bonjour + UDP transport, `BuildScript.cs` already declares
`_aerojoy._udp` in `NSBonjourServices` (iOS refuses to browse without it) and already supplies
`NSLocalNetworkUsageDescription`. The board stands in for the Mac on that service. It carries 8 analogue channels and 32
switches instead of a pad's 4 and 16, its throttle is **absolute**, it reaches the gear, and its calibration is the
wizard in `JoystickSetup.cs` — which was written for an RC radio in the first place ("a 2- or 3-position switch channel",
"an RC radio's dongle sends its buttons and switches as axes", a throttle as a non-centring axis with idle and full
ends).

## Hardware

| # | Part | Why |
|---|------|-----|
| 1 | An **M5Stack ESP32 board** — Atom Lite, M5StickC, Core2 or AtomS3 (the boards ordered 2026-10-14) | Wi-Fi and BLE in one unit, with a Grove connector that saves soldering. `platformio.ini` has an env for each. |
| 2 | A **trainer lead** for the transmitter (3.5 mm jack) | the signal source |
| 3 | A **1 kΩ resistor** in series with the signal | some trainer ports swing to 5 V; an ESP32 pin is rated to 3.6 V |
| 4 | USB-C cable, and a power bank or charger | flashing, then power |

Two wires: tip through the resistor to the signal pin, sleeve to ground. **No inverter for SBUS** — the ESP32's own UART
does the inversion, which is the one thing every SBUS wiring guide gets wrong for this chip. **Do not power the board
from the trainer port.**

An ESP32-**S3** is fine (this firmware only uses BLE and Wi-Fi). It has no Bluetooth Classic, which closes off
impersonating a console pad; that was rejected anyway — see below.

## Firmware (`tools/rc-link/`, PlatformIO + Arduino)

```
include/rc_decode.h       PPM and SBUS decoders, format auto-detection   } pure C++, no Arduino,
include/rc_map.h          calibration, failsafe, both output maps, the   } TESTED host-side
                          throttle servo                                 }
include/joylink_packet.h  the AJ1D / AJ1P / AJ1H wire format             }
src/main.cpp              pins, interrupt, UART, Wi-Fi, mDNS, UDP, BLE   -- UNTESTED wiring
test/host_test.cpp        199 checks                                     -- test/run.sh, clang, no board
platformio.ini            one env per board; `pio run -t upload`
```

- **Auto-detect, no jumper.** SBUS and PPM arrive on the same pin, so detection runs in alternating 600 ms phases — the
  UART with the interrupt detached, then the interrupt with the UART stopped — and locks on four consecutive valid
  frames. SBUS gets the first phase because watching a 100 kbaud stream with an edge interrupt is 50 000 interrupts a
  second. A locked format that goes quiet for a second unlocks and hunts again, so changing radios needs no reflash and
  no reboot.
- **No PPM polarity setting.** A channel's value is the time from one mark to the next, not a mark's width, and
  inverting the signal shifts every mark by one constant pulse width — so rising-to-rising and falling-to-falling
  intervals come out identical. Derived in `rc_decode.h`, tested in `host_test.cpp`. Positive and negative trainer ports
  decode the same and the owner never has to know which his radio puts out.
- **No Mode 1 / Mode 2 setting.** Mode lives inside the transmitter and decides which gimbal drives which *function*;
  the channel stream is numbered by function, so channel 2 is the elevator in either mode. Nothing on the board can
  tell, and nothing needs to. What genuinely differs by brand is the channel **order** (`RC_ORDER`: AETR / TAER / RETA).
  A knob wired to nothing is worse than no knob.
- **Senses are fixed on the radio, not on the board.** A transmitter has a servo-reverse switch per channel; a second
  reverse here would be a second place to look when the elevator works backwards.
- **ESP-IDF was dropped** (the October plan named it). Nothing here needs it, and `pio run` installs its own toolchain
  in one command, which matters for an owner who will not debug a build process. The platform is pinned to
  `espressif32@6.9.0` because the BLE code is written against Arduino core 2.x's Bluedroid API; core 3.x moved to NimBLE
  and changed those calls.
- **No Wi-Fi credentials anywhere in this repo** (it is public). The board brings up its own open AP on first power-up
  and serves a two-field form at `192.168.4.1`; the credentials live in NVS. Holding the button at power-up keeps it an
  AP, so the iPad can join the board directly with no router at all — the mode for flying away from home.

## Failsafe — the decision

| State | When | What the aeroplane gets |
|---|---|---|
| LIVE | frames arriving, flags clear | the controls, untouched |
| LOST | no good frame for 100 ms, or SBUS's failsafe / frame-lost flag | aileron, elevator, rudder **centred**; throttle **held** |
| DEAD | still nothing after 2 s | centred, throttle **closed** |

Rejected: **hold everything** (full aileron held through a dropout rolls it in — and it is most RC receivers' default,
so it is worth refusing out loud); **cut the throttle at 100 ms** (a dropout is usually 100–300 ms, and chopping the
power at the top of a vertical turns a 200 ms nothing into a tail slide — centring the *surfaces* is right at any
attitude, closing the throttle is not); and **stopping the stream so the game sees the controller leave**, because the
game drops `JoyLink.Connected` after half a second, which flips `TouchFlightControls.TouchSticksHidden` and brings the
on-screen sticks back under a thumb that is not there. Mid-sequence, the controls changing shape is worse than the
controls going quiet. Aux channels keep their last values: a flap or gear switch snapping on a Wi-Fi hiccup would be its
own emergency.

Both the flags and the clock are honoured, because a receiver in failsafe keeps sending frames at full rate with the bit
set — a time-only failsafe would never fire. There is a test for exactly that.

## Calibration

- **JoyLink path: nothing on the board.** Channels go out against a fixed 1000/1500/2000 reference and the game's wizard
  (OPTIONS → JOYSTICK SETUP) measures the real travel, the real centre and the dead zone from the measured noise, per
  control, stored under the device name `Aero RC Link`. One constant name, always — a board that called itself
  "Aero RC Link (SBUS)" would lose his calibration the day he flew the PPM radio.
- **First flight needs no setup step**: the board sends a default profile at 1 Hz and the iPad adopts it *only* when it
  has none of its own, so the sticks fly immediately and the wizard upgrades it later without ever being overridden.
- **BLE gamepad path: on the board**, because the game has nothing to calibrate a pad with. Hold the button 3 s, sweep
  everything for 12 s, release and keep still for 3 s (that last part is how it learns the centre, which is *not* the
  midpoint of the travel). Stored in NVS.

## Open — Ty's call

1. **The thirty-second iOS test** (`tools/rc-link/README.md` §7). If a BLE HID gamepad really does reach the game, the
   Wi-Fi half can be cut and the shape he picked stands as picked. If it does not, JoyLink is the path and the BLE build
   stays useful for Android, Mac, Windows and PC sims.
2. **The absolute-throttle option in the app.** The board's throttle servo (dead-reckoning the game's own integrator) is
   a good approximation and not the real thing, and it cannot know about a lever the touch tray moved. The honest fix is
   one `if` in `GamepadInput.Read()` plus a `SessionSettings` flag: read `leftStick.y` as a position. Only worth doing
   if (1) comes out in the BLE gamepad's favour.
3. **The board model and its Grove pin.** `platformio.ini` has an env per M5Stack board with pins from the published
   pinouts, unverified against the units in his hands. A wrong pin is a one-line change there.

## Rejected

- **A custom BLE protocol** — the owner's own call, and right. JoyLink is not one: it is the shipping protocol with the
  iOS side already built.
- **A wired USB-C link** — the owner's call; and iOS gives an app no generic USB HID joystick anyway.
- **Impersonating a console controller** (an Xbox pad's VID/PID and report descriptor) to get past Apple's filter — the
  real pad uses Bluetooth *Classic*, which the -S3 and -C3 cannot do at all; nothing found suggests Apple keys off the
  VID alone; and a spoof of somebody else's hardware breaks on an OS update, in the air, with no warning.
- **A BLE HID keyboard or mouse** — iOS does accept those generically, which is the tempting part, but there are no
  analogue axes. Four bang-bang controls is not an aerobatic aeroplane.
- **A WS2812 or a display for status** — every M5 variant wires them differently and each needs its own library. The
  status goes over the serial line, which every board has, and that is what makes the two-minute smoke test work on any
  of them.

## Superseded: the October plan

The first version of this spec routed a **Spektrum WS3000T USB dongle** into an **ESP32-S3-USB-OTG** board over USB
host, and then invented a **custom BLE service** (Info / Channels / Status characteristics) with a matching
CoreBluetooth reader to be added to `AeroNative` on all three platforms. That is superseded on both halves:

- the **input** half, because the owner chose the trainer port over the dongle — simpler, no USB host, no HID report
  descriptor to parse, and it works with any radio rather than one brand's;
- the **output** half, because the custom protocol *and its new app-side code* were the thing he rejected, and because
  `JoyLink` already does the same job with the iOS side already shipping.

What survived from it: the diagnosis of why a box is needed at all (and the GameController line that turned out to be
the key finding), the latency budget (PPM 22 ms + Wi-Fi ≈ 25–40 ms end to end, fine for flying), and the choice of
`tools/rc-link/` for the firmware.
