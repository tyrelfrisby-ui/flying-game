# Aero RC Link — fly the iPad and iPhone with an RC transmitter

Owner request 2026-10-12: "a hardware device like a raspberry pi etc that the dongle plugs into and it bluetooths to the
ipad/iphone".

## Why a box

iPadOS and iOS give apps no access to a generic USB joystick. GameController only accepts Xbox, PlayStation, Switch Pro
and MFi controllers, and IOKit HID and CoreHID are macOS-only. The Spektrum WS3000T is a generic USB HID joystick, so on
the Mac the game reads it directly, but on iOS it needs something in between.

## The chain

```
MLP6DSM ──DSM2──▶ WS3000T dongle ──USB──▶ ESP32-S3 (USB host) ──BLE──▶ Aero Playground (iPad / iPhone / Mac)
```

## Hardware

| # | Part | Why | Approx. |
|---|------|-----|---------|
| 1 | **Espressif ESP32-S3-USB-OTG** dev board | A USB-A host port with its own VBUS power switch, so the dongle plugs straight in. It has Bluetooth LE, a 1.3" screen for status, and a Li-ion battery input. | $35 (DigiKey 15822449) |
| 2 | USB-C data cable | Powers the board, and connects it to the Mac to load the firmware | on hand |
| 3 | Power: any USB-C power bank or charger | Runs the box beside the iPad | on hand |
| 4 | Optional: 1-cell Li-ion pack for the board's battery input | A cordless box. Confirm the board's connector before buying. | ~$10 |
| 5 | Optional: case | 3D-print one, or a small project box | — |

No adapter is needed: the dongle's USB-A plug goes into the board's USB-A port.

**Fallback if the dongle won't enumerate on the S3:** Raspberry Pi Zero 2 W, a micro-USB OTG adapter and a microSD card.
It runs the same protocol from a Python service, but boots in about 30 s instead of 1 s.

## Firmware (ESP-IDF, in `tools/rc-link/`)

- **USB host:** esp `usb_host_hid` (the HID class driver).
  - Enumerate the dongle and parse its report descriptor into up to 8 axes and 32 buttons.
  - Normalise each axis to −1…1 with its logical min and max, the same as the Mac's `HidJoystick.mm`, so a calibration
    behaves the same everywhere.
- **BLE peripheral "Aero RC Link":**
  - one custom service with three characteristics:
    - **Info** (read): the joystick's product name (e.g. "Horizon Hobby SPEKTRUM WS3000T"), the firmware version and the
      number of axes.
    - **Channels** (notify): sequence u8, flags u8, 8 × int16 (axis × 32767), buttons u32 — 22 bytes.
    - **Status** (notify): dongle present, transmitter signal (axes not all −1), battery.
  - Sends on any change, up to 100 Hz, plus a 5 Hz keepalive.
  - Asks for a 15 ms connection interval (the iOS minimum).
- **Screen:** dongle found / transmitter bound / app connected, plus live bars for the eight channels.
- **Latency budget:** DSM frame 11–22 ms, USB 1–8 ms, BLE 15 ms, so about 30–45 ms end to end, which is fine for flying.
- **Loading the firmware:** `idf.py flash` over USB-C from the Mac. It's done once, and again only for updates.

## App (all three platforms, shared code)

- **AeroNative gains an `RcLink` reader built on CoreBluetooth (iOS, iPadOS and macOS):**
  - scans for the service and connects automatically to the last box;
  - reconnects after a dropout;
  - exposes the same `HJ_Count / HJ_Name / HJ_Axis / HJ_Button` calls as the Mac's USB reader.
- **`JoystickInput`'s native path turns on for iOS too.** The setup, calibration, guns, flap switch, profiles and every
  consumer (game and widget) then work unchanged.
- **On the Mac,** the box is a second source next to a dongle plugged straight in; the first one connected wins.
- **Info.plist:** `NSBluetoothAlwaysUsageDescription` ("Aero Playground connects to your RC Link to read your
  transmitter."). The app asks once, the first time a link is looked for. That is only when the player opens joystick
  setup, never at launch.
- **Options:** the joystick row shows "RC LINK — connected / searching". On iOS, Joystick setup appears when a link is
  connected.
- **Privacy:** everything stays on the device and nothing is sent anywhere, so "Data Not Collected" still holds. Tell
  Glass about the Bluetooth permission string before shipping.

## Tests

- **Firmware:** a host-side unit test of the report parser against the WS3000T's real descriptor (captured on the Mac).
- **App:** a desk test with the box connected:
  - setup runs on iPad;
  - all four controls reach ±100 %;
  - the flap switch's three positions;
  - guns while held;
  - reconnect after pulling power;
  - latency measured by flicking a switch and watching the panel bar against the transmitter.
- **Parity:** the same profile flow on iPhone, iPad and Mac.
