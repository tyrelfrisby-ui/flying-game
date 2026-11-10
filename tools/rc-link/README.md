# Aero RC Link — fly the iPad from a real RC transmitter

> ## This firmware is UNTESTED.
> Nobody has flashed it. Nobody has the board in hand. Every line that touches a pin, a radio or a Bluetooth stack
> has never been run. What **has** been run is the pure half — the PPM and SBUS decoders, the format detector, the
> calibration, the failsafe decision, both output mappings, the packet bytes and the throttle servo — which lives
> in `include/*.h` and is exercised by 199 checks in `test/host_test.cpp` on the Mac (`test/run.sh`, no board and
> no toolchain needed). `src/main.cpp` is wiring, and it has only been syntax-checked against stub headers.
>
> So **start with the two-minute test below, not with a flight.** It is built to fail fast and tell you *which
> half* is wrong — whether the board can see your transmitter at all, or whether the phone can see the board at
> all — instead of leaving you guessing at 500 ft.

---

## 1. The two-minute test (do this first)

```sh
cd tools/rc-link
pio run -e esp32dev -t upload          # or your board's env; see platformio.ini
pio device monitor -b 115200
```

Plug the trainer lead in, switch the transmitter on, and read one line:

```
[rc] format SBUS  link LIVE  frames 1483   ch 1502 1498  988 1501 1000 2000 1500 1500  | joylink 192.168.1.64 1 device(s)
     ^^^^^^^^^^^                           ^^^^^^^^^^^^                                  ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
     half 1: the radio                     the channels, in microseconds                 half 2: the phone
```

| What you see | What is wrong | Where to look |
|---|---|---|
| `format none … listening for SBUS on pin 32` | **The board cannot see your transmitter.** | The lead, the pin, the trainer port. §4. |
| `format PPM` or `SBUS` and the `ch` numbers move with the sticks | The radio half is **fine**. Stop looking there. | — |
| `joylink … 0 device(s)` | **The phone is not reaching the board.** | Same Wi-Fi? Options switch on? §6. |
| `joylink … 1 device(s)` | Both halves are up. | Anything still odd is mapping (§8) or calibration (§10). |
| `link LOST` / `link DEAD` with the transmitter on | Frames are arriving corrupted or in failsafe | §9. Check the lead's ground. |

Neither half ever guesses at the other half's problem. That is the whole reason both are printed side by side.

---

## 2. The iOS problem — read this before you build anything on the BLE gamepad

Ty, 2026-11-10, choosing the shape:

> "The board sits on the transmitter's trainer port, decodes the channel stream and presents itself as a BLE
> gamepad. The phone needs no special code — it sees a standard controller."

**The first sentence is right and is exactly what this firmware does. The second does not hold on iOS, and the
evidence was already in this repo.** `docs/RC-LINK-SPEC.md`, written in October for the previous shape, says it in
one line:

> "GameController only accepts Xbox, PlayStation, Switch Pro and MFi controllers, and IOKit HID and CoreHID are
> macOS-only."

And the game's own `GamepadInput.cs` says where its pads come from:

> "the player's own GameController observer (GCControllerDidConnectNotification, compiled into UnityFramework)
> hands it to the Input System"

So Unity on iOS sees **only** what Apple's GameController framework hands it, and that framework recognises a
fixed set of controllers. A home-made BLE HID gamepad **pairs with an iPhone and is then invisible to every app**:

- Apple developer forums, [thread 741756](https://developer.apple.com/forums/thread/741756) — a BLE HID gamepad
  pairs and iOS "does not recognise it"; the same device works on Android and Windows.
- [thread 109305](https://developer.apple.com/forums/thread/109305) and
  [thread 763679](https://developer.apple.com/forums/thread/763679) — generic BLE HID gamepads pair but never
  appear in the GameController framework, even with report descriptors copied from supported controllers.
- Nordic DevZone, [BLE HID gamepad not recognised on iOS](https://devzone.nordicsemi.com/f/nordic-q-a/117262/ble-hid-gamepad-on-nrf52805-not-recognized-on-ios)
  — the same result on a different chip, and the note that **keyboards and mice over BLE HID do work on iOS while
  the gamepad profile does not**. One report on iOS 27 logs `Un-authenticated game controller device attached`.
- Apple's own [supported controller list](https://support.apple.com/111101) is a list of named products.

This is reported here rather than quietly worked around because it is the finding that changes the choice of
shape, and it should have been checked before firmware was written. **I have not tested it** — §7 is a
thirty-second test that lets you falsify it yourself with the hardware.

### What works instead, with no app change at all

The game already ships a path for exactly this, and it is **not** the custom BLE protocol you rejected — it is the
protocol that is already in the build:

| Already in the repo | What it does |
|---|---|
| `Assets/Plugins/iOS/JoyLink.mm` | browses Bonjour `_aerojoy._udp`, connects, says `AJ1H` twice a second, keeps the latest packet |
| `Scripts/Editor/BuildScript.cs` line 442 | already declares `_aerojoy._udp` in `NSBonjourServices` — iOS refuses to browse without it |
| line 441 | already supplies `NSLocalNetworkUsageDescription` |
| `Scripts/Bridge/JoyLink.cs` | reads the packet: 8 raw axes, 32 buttons, a device name, and the sender's calibration |
| `Scripts/Bridge/JoystickSetup.cs` | the calibration wizard — **written for an RC radio in the first place**: "a 2- or 3-position switch channel", "an RC radio's dongle sends its buttons and switches as axes", a throttle as a non-centring axis with idle and full ends |

The board stands in for the Mac on that service. **Nothing in `unity/` changes.** And it is strictly better than a
gamepad for this job:

| | BLE HID gamepad | JoyLink (this firmware's default) |
|---|---|---|
| Seen by iOS / iPadOS | **no** (§2) | yes — it is the shipping path |
| Analogue channels | 4 (the triggers are not free — see §8) | **8** |
| Switch channels | 16 buttons | **12** (channels 5–16), with 3-position switches read properly |
| Throttle | a **rate** — see §8(b) | **absolute**, 40 % stick is 40 % power |
| Expo | the game's 0.65 expo on top of yours | none — your transmitter's curves are what you fly |
| Gear | **not reachable at all** | yes (`gear` action) |
| Calibration | the board's (§10) | the game's own wizard |
| Works on Mac | yes | yes |
| Works on Android / Windows / a PC sim | **yes** | no |

Both are built. Both can run at once (`-e esp32dev-both`).

---

## 3. What you need

| Part | Why |
|---|---|
| An **M5Stack ESP32 board** (Atom Lite, M5StickC, Core2, AtomS3 — any of the ones you ordered on 2026‑10‑14) | Wi‑Fi and BLE in one unit, with a Grove connector that saves soldering |
| A **trainer lead** for your transmitter — 3.5 mm mono or stereo jack, whichever your radio takes | the signal source |
| A **1 kΩ resistor** in series with the signal, and a 3.3 V clamp if you want belt and braces | some trainer ports swing to 5 V and an ESP32 pin is rated to 3.6 V |
| A USB‑C cable, and a power bank or charger | flashing, then power |

An **ESP32‑S3** board (AtomS3, StampS3) is fine — this firmware only ever uses BLE and Wi‑Fi. Note in passing that
the ‑S3 has no Bluetooth Classic, which closes off the one theoretical route to a pad iOS might accept; §11 rejects
that route for other reasons anyway.

## 4. Wiring

```
   trainer jack                       ESP32
   ------------                       -----
   TIP  (signal) ----[ 1 kΩ ]-------- RC_PIN   (32 on a classic M5 Grove; see platformio.ini)
   SLEEVE (gnd)  -------------------- GND
```

- **Two wires. That is all.** Signal and ground.
- **Do not power the board from the trainer port.** Some radios will drive a little current there and some will
  not, and a brown-out mid-roll is not worth finding out which. USB‑C.
- **No inverter is needed for SBUS.** The inversion is done inside the ESP32's own UART
  (`gRcSerial.begin(100000, SERIAL_8E2, pin, -1, true)` — that last argument), so do not solder a transistor onto
  the lead. This is the thing most SBUS wiring guides will tell you to do, and here it is wrong.
- **The ground matters.** A floating ground is the usual cause of `link LOST` with the transmitter switched on.
- The Grove pin numbers in `platformio.ini` are from the published M5Stack pinouts and differ between revisions.
  **Check yours.** A wrong pin is a one-line change in `platformio.ini`, not a code change.

## 5. Flash it

```sh
cd tools/rc-link
pio run -e esp32dev -t upload           # pick your env from platformio.ini
pio device monitor -b 115200
```

`pio` installs its own toolchain the first time and needs nothing else on the Mac — no ESP‑IDF, no SDK, no export
script. If `pio` is not on the path: `brew install platformio` or `pipx install platformio`.

Envs: `esp32dev` (any classic ESP32 — start here), `m5stack-atom`, `m5stick-c`, `m5stack-core2`,
`m5stack-atoms3`, `m5stack-stamps3`, plus `esp32dev-both` (JoyLink **and** BLE) and `esp32dev-ble` (BLE only).

## 6. Fly it — the JoyLink path

**First power-up: give it a network.** With nothing stored, the board brings up its own open Wi‑Fi called
`Aero-RC-Link`. Join it from your phone, open `http://192.168.4.1`, type your Wi‑Fi name and password, save. It
restarts and joins. Nothing is compiled in and nothing is in this repo — **this repo is public** — so the
credentials live only in the board's NVS.

Then:

1. **Board and iPad on the same Wi‑Fi.** Bonjour does not cross a guest network or a router that filters mDNS;
   that is the first thing to suspect if the board is up and the iPad sees nothing.
2. On the iPad: **OPTIONS → the joystick row → turn the link on.** (Same switch the Mac link uses — `JoyLink.On`.)
3. Allow the **local network** prompt the first time.
4. The row should read `Aero RC Link via Aero RC Link`, and the serial monitor should say `1 device(s)`.
5. **Move the sticks. The aeroplane should fly straight away** — the board sends a default calibration once a
   second, and the iPad adopts it *only* if it has none of its own, so the first flight needs no setup step.
6. When you want the real endpoints: **OPTIONS → JOYSTICK SETUP**, and run the wizard with the transmitter. From
   then on your calibration wins and the board's default is ignored for good.

**Away from a router:** hold the board's button while powering up. It becomes the access point itself; join
`Aero-RC-Link` on the iPad and Bonjour works over it with no router involved.

## 7. The BLE gamepad path — and the thirty-second test of §2

```sh
pio run -e esp32dev-both -t upload      # both transports, so one test covers both claims
```

Then, on the **iPhone or iPad**: Settings → Bluetooth. `Aero RC Link` should appear.

- **If it pairs and the aeroplane responds to the sticks with the JoyLink switch OFF** — I was wrong, iOS does see
  it, and the simpler shape you picked is the right one. Tell me and I will cut the Wi‑Fi half out.
- **If it pairs and nothing in the game responds** (and `[Gamepad] connected:` never appears in the device log) —
  that is §2, confirmed on your hardware. Use JoyLink.
- **If it does not appear in Bluetooth at all** — that is a firmware problem, not an iOS one. The serial monitor
  will say `[ble] advertising`.

On **Android, Windows, macOS or a PC sim** it should just be a gamepad. That is worth knowing for its own sake.

## 8. The mapping

### Channels in

Channel **order** differs by brand and is a build flag, `RC_ORDER`:

| `RC_ORDER` | ch1 | ch2 | ch3 | ch4 | Radios |
|---|---|---|---|---|---|
| `AETR` *(default)* | aileron | elevator | throttle | rudder | FrSky, Futaba, Jeti, EdgeTX/OpenTX |
| `TAER` | throttle | aileron | elevator | rudder | Spektrum, JR |
| `RETA` | rudder | elevator | throttle | aileron | older Hitec, Multiplex |

**Senses are not configurable on the board, on purpose.** Your transmitter has a servo-reverse switch per channel
and that is what it is for. A reverse here as well would mean two places to look when the elevator works
backwards. If a control flies backwards: **reverse it on the radio.**

### Out, on the JoyLink path

| | |
|---|---|
| axes 1–8 | channels 1–8, −1…1 against a fixed 1000/1500/2000 reference |
| buttons 4–15 | channels 5–16, on above 20 % above centre |
| buttons 0–3 | **never set.** The wizard's button steps take the first button they see pressed, so a stick deflected while you read the prompt would get assigned as the GUNS button. Leaving these unset makes that impossible. |
| flap switch | channel 5 by default (`RC_FLAP_CHANNEL`), read three ways: up / half / full |
| first-flight profile | aileron, elevator, rudder, throttle on their channels; `gear` on channel 6, `guns` on 7, `smoke` on 8 |

Eight axes is a ceiling, not a choice: `JoystickInput.AxisCount` is 8 and `JoyLink.mm` checks the packet length
against it. SBUS's channels 9–16 cannot be axes — but they are all reachable as switches, which is what they are.

### Out, on the BLE gamepad path

| Pad control | Carries | Note |
|---|---|---|
| right stick X / Y | roll / pitch | through the game's 0.65 expo, **on top of your transmitter's** |
| left stick X | rudder | |
| left stick Y | throttle, as a **rate** | see (b) |
| triggers | **nothing** | see (a) |
| buttons 1–16 | channels 5–16 | |

Three things about that path, measured off `GamepadInput.cs` and `GamepadShaping.cs`, not assumed:

**(a) Four analogue channels, not six.** The triggers look free and are not: `GamepadShaping.Rudder` adds
(RT − LT) *to the rudder*, so an aux channel parked on a trigger is a standing rudder deflection. Your two knobs
have nowhere analogue to go.

**(b) The throttle is a rate, not a position — and this is the real mismatch.** A pad's left stick springs back to
centre, so the game *integrates* it: full travel in 2.0 s, with a 0.25 dead zone. An RC throttle does not spring
back. Mapped straight across, the stick above centre+25 % ramps to **full** and stays, below centre ramps to
**idle**, and the middle band does nothing: a four-position throttle. The board therefore runs a **throttle
servo** — it keeps its own copy of the game's integrator, dead-reckons where the lever has got to, and commands
whatever deflection drives it to where your stick actually is. Both ends saturate at the stops, so full power and
idle wipe any accumulated error, which an aerobatic sequence does constantly. It is tested against a
transcription of the game's own law in `host_test.cpp`.

> **The one-line app change that would be better.** If you want the gamepad path properly, the honest fix is in
> the app, not the board: an option in `GamepadInput.Read()` to read `leftStick.y` as a **position** instead of
> through `GamepadShaping.ThrottleRate`, and set the lever directly. One `if` and one `SessionSettings` flag. The
> servo above exists so that nothing in `unity/` has to change, which is what you asked for; it is a good
> approximation and not the real thing. Your call.

**(c) No gear.** `GamepadInput.Frame` has no gear field — the gear/water-rudder slot is reached from the touch
tray and from `JoystickInput`'s `gear` action, never from a pad. On the JoyLink path you get it.

### Mode 1 vs Mode 2 — there is nothing to set, and here is why

Mode lives **inside the transmitter**. It decides which physical gimbal drives which *function*. What leaves the
trainer port is numbered by function, not by gimbal: **channel 2 is the elevator on a Mode 1 radio and on a Mode 2
radio alike**, because the radio has already done the mapping. The board cannot tell which mode it is looking at,
and does not need to —

- on the JoyLink path the wizard asks "PULL the stick fully BACK" and finds the channel itself, so mode cannot be
  got wrong even in principle;
- on the gamepad path elevator goes to the right thumbstick and throttle to the left one **by function**, which is
  what the game reads them as. A Mode 1 pilot's left hand moves the right thumbstick's pitch axis, which is
  exactly what he wants, because he is flying the aeroplane, not the pad.

So there is deliberately **no Mode flag** — a knob wired to nothing is worse than no knob, because one day
somebody turns it looking for a fix. What genuinely differs by radio is the channel **order**, above.

## 9. Failsafe — a decision, not a default

SBUS carries explicit failsafe and frame-lost flags; PPM just stops. Both are honoured, and the clock as well,
because a receiver in failsafe keeps sending frames at full rate with the bit set — a time-only failsafe would
never fire.

| State | When | What the aeroplane gets |
|---|---|---|
| **LIVE** | frames arriving, flags clear | your controls, untouched |
| **LOST** | no good frame for **100 ms**, or a failsafe / frame-lost flag | aileron, elevator, rudder **centred**; throttle **held** where it was |
| **DEAD** | still nothing after **2 s** | centred, and the throttle **closed** |

Aux channels keep their last values through a dropout: a flap or gear switch snapping on a 200 ms Wi‑Fi hiccup
would be its own emergency.

**What was rejected, and why it matters:**

- **Hold everything** — the thing that kills the aeroplane. Full aileron held through a dropout rolls it in while
  you are looking at the transmitter. It is also the default of most RC receivers, so it is worth saying no to out
  loud.
- **Cut everything to idle** — rejected *for the throttle*. A dropout is usually 100–300 ms. Chopping the power at
  the top of a vertical turns a 200 ms nothing into a tail slide. Centring the *surfaces* is right at any
  attitude; closing the throttle is not.
- **Stop sending, so the game sees the controller go away** — rejected, and this is the subtle one. The game drops
  `JoyLink.Connected` after half a second of silence, which flips `TouchFlightControls.TouchSticksHidden` and
  **brings the on-screen sticks back under a thumb that is not there.** Mid-sequence, the controls changing shape
  is worse than the controls going quiet. So the board keeps sending, and sends a deliberate safe frame.

The board shouts either way: the serial line says `link LOST -- surfaces centred, throttle held`, and `RC_LED_PIN`
(if your board has one wired) flashes.

## 10. Calibration

**On the JoyLink path there is nothing to calibrate on the board.** It sends every channel against a fixed
1000/1500/2000 reference, and the game's own wizard — OPTIONS → JOYSTICK SETUP — measures your real travel, your
real centre and the dead zone from the measured noise, per control, and stores it under the device name
`Aero RC Link`. Putting a second calibration in the board would mean two places where the elevator's travel is
remembered and a guarantee that one day they disagree.

**On the BLE gamepad path the board has to do it**, because the game has nothing to calibrate a pad with (named
controls, no profile — that is the whole point of `GamepadInput`). So:

> Hold the board's button for **3 seconds** while it is running. The serial line says so. Move every stick to both
> stops and flip every switch both ways for 12 seconds, then **let go of everything and keep still** for the last
> 3 seconds — that last part is how it learns your centre, which is *not* the midpoint of the travel (sub-trim).
> It prints what it measured and stores it in NVS, where it survives a power cycle and a reflash.

A channel that never moved keeps its default rather than being calibrated to a single point. A throttle resting at
idle calibrates correctly (its "centre" is its idle end, which an earlier version of the plausibility check got
wrong and silently discarded — there is a test for it now).

On a Core2, whose buttons are capacitive and not GPIOs, there is no board button: use the JoyLink path.

## 11. What was rejected

- **A custom BLE protocol.** You rejected it, and rightly. Note that JoyLink is *not* one: it is the protocol the
  game already ships, read off `JoyLink.cs` and `JoyLink.mm`, with the iOS side already built and already
  declared in the Info.plist.
- **A wired USB‑C link.** You rejected it, and iOS would not give an app a generic USB HID joystick anyway.
- **Impersonating a console controller** (an Xbox pad's VID/PID and report descriptor) to get past Apple's
  controller filter. Rejected: the real Xbox pad uses Bluetooth **Classic** HID, which an ESP32‑S3 or ‑C3 cannot
  do at all; nothing found suggests Apple keys its support off the VID alone; and a spoof of somebody else's
  hardware is a thing that breaks on an OS update, in the air, with no warning.
- **A BLE HID keyboard or mouse** — iOS *does* accept those generically, which is the tempting part. Rejected: no
  analogue axes. Four bang-bang controls is not an aerobatic aeroplane.
- **A Mode 1 / Mode 2 setting** — §8. There is nothing for it to do.
- **A jumper or a button to pick PPM vs SBUS** — not needed. The format is auto-detected in alternating phases
  and re-detected if the stream stops, so changing radios needs no reflash and no reboot.
- **A WS2812 or a display for status.** Every M5 variant wires those differently and each needs its own library.
  The status that matters goes over the serial line, which every board has.
- **ESP‑IDF**, which `docs/RC-LINK-SPEC.md` originally named. Nothing here needs it, and PlatformIO installs its
  own toolchain in one command.

## 12. What is tested and what is not

```sh
tools/rc-link/test/run.sh        # 199 checks, clang, no board, no toolchain
```

**Tested** (`include/*.h`): the PPM decoder including inverted signals, glitch rejection and the `micros()` wrap;
the SBUS decoder including the 11-bit cursor on channels 9–16, the flag bits, Futaba's counted end byte and
mid-stream resynchronisation; format detection and re-detection; the calibration maths and sweep; the failsafe
state machine including the flags-at-full-rate case; both output maps; the JoyLink packet bytes against the
shipping receiver's own length and offset checks; and the throttle servo against a transcription of
`GamepadShaping.Lever`.

**Not tested** (`src/main.cpp`): the pin, the interrupt, the UART and its inversion, Wi‑Fi, the provisioning page,
mDNS, the UDP socket, NVS, and the BLE HID stack. Syntax-checked against stub headers; that is all. In particular
these are the things most likely to be wrong on the first flash:

1. the Grove pin numbers per board (`platformio.ini`) — published pinouts, not verified against your unit;
2. whether `MDNS.addService` publishes an instance name the iOS browser shows the way `JoyLink.mm` expects;
3. the exact Arduino BLE API spelling, which moved between core 2.x and 3.x — the platform is pinned to 6.9.0 for
   that reason;
4. whether 100 Hz of UDP survives your Wi‑Fi without jitter you can feel.
