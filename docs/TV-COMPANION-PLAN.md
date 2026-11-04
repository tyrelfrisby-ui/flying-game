# TV and companion modes: plan

Ty's request (2026-11-04), lane `lane/claude-tv-companion` off `lane/claude-island-nav`:

1. **Mirroring.** AirPlay and HDMI mirroring work cleanly, with the right aspect ratio and nothing broken.
2. **Game on the TV.** When a second screen (AirPlay or HDMI) connects to an iPhone or iPad, the game renders full size
   on the TV and the phone becomes a full-screen touch controller: stick pad, rudder, throttle, trim, flaps and
   spoilers, and buttons for view, pause and smoke.
3. **TV plus a Bluetooth pad.** The same TV mode with a game controller (the 8BitDo Pro 2 already works,
   `Bridge/GamepadInput.cs`). The phone becomes a touchpad of extra buttons: quick access to views, switches and menus.
4. **Companion app.** An iPad or iPhone pairs with the Mac app over the local network and works as a touch control and
   quick-access button panel, including in the outside views. It follows the Glass Overlay companion pattern
   (`docs/glass-companion-notes.md`, read-only study of `~/Developer/glass-overlay`).

Scope of this lane: this plan, then features 1 and 2 with tests, then 3 if time allows. Feature 4 stays at plan stage
unless 1 to 3 are done and solid. Nothing here is approved until Ty says so.

---

## 0. What is already in the repo (and gets reused)

| Piece | Where | What it gives us |
|---|---|---|
| Touch controller | `Bridge/TouchFlightControls.cs` | Two RC-style pads (rudder+throttle/brakes, aileron+elevator), pitch/aileron/rudder trim, flaps (three detents), gear, smoke, CAM ref, look buttons, tow, bail/eject, fire. Raw multitouch from `Input.touches` in `Update`; OnGUI only draws. **This is already the phone controller.** In TV mode it keeps running unchanged. The only difference is that the phone stops drawing the world and the instruments. |
| Toolbar | `ViewPanel`, `StartMenu`, `OptionsPanel` via `UiLayout.ToolbarSlot` | MENU · VIEW (menu of all 11 views) · REPLAY (= pause and review) · CLIP · REC · AERO · TIME · CHART · OPTIONS. Stays on the phone. |
| Gamepad | `Bridge/GamepadInput.cs`, `Core/GamepadShaping.cs` | Pad connected: `TouchFlightControls.TouchSticksHidden` hides the pads, keeps the buttons, and `ScreenLayout.FullScreenView` gives the view the whole screen. Feature 3 builds on that switch. |
| Instruments into a texture | `ClipRecorder.OnGUI` → `AnalogGauges.DrawForClip()`, `HudOverlay.RenderForClip()` / `DrawClipLabels()` | **Already on device:** IMGUI drawn into a `RenderTexture` during the Repaint pass (`RenderTexture.active` + `GL.LoadPixelMatrix`). The TV picture uses the same mechanism. No second HUD gets written. |
| Bonjour, UDP | `Plugins/iOS/JoyLink.mm` (`_aerojoy._udp`) | Network.framework listener and browser, Mac → iPad joystick channels. Shows that the same `.mm` builds into iOS and into `Plugins/macOS/AeroNative.bundle` (`tools/native-mac/build.sh`). |
| Field groups | `Plugins/iOS/AeroPeer.mm` (MultipeerConnectivity, `_aeroplay._tcp/_udp`) | Peer-to-peer multiplayer. Not reused for the companion (Multipeer has no TXT-role model and no control over the handshake). |
| TCP JSON control | `Bridge/Widget/WidgetServer.cs` (TCP 47830, NDJSON) | The command vocabulary for controls/views/pause that Glass already drives (`docs/WIDGET.md`). The companion's command set borrows its names so there is one vocabulary. |
| Swift in the iOS build | `Plugins/iOS/AeroStore.swift` | Swift sources already compile into UnityFramework. The companion transport can be Swift with no new build plumbing on iOS. |

### What Unity 6000.3's iOS trampoline actually does with a second screen

Read from the exported Xcode project (`Classes/Unity/DisplayManager.mm`, `Classes/UI/UnityScene.mm`, `Info.plist`):

- `DisplayManager` registers every `UIScreen` on `UIScreenDidConnectNotification`. The screen shows up in
  `Display.displays` straight away, but **no window and no render surface are created until `Display.Activate()`**.
  Until then iOS mirrors the phone. Mirroring is therefore the default, and Unity does not interfere with it.
- `Display.Activate()` → `UnityActivateScreenForRendering` → `createView` makes a `UIWindow` and sets
  `window.windowScene = [self sceneForScreen:screen]`. That looks for a connected `UIWindowScene` **whose screen is the
  TV**.
- The app uses the **UIScene lifecycle**. `UIApplicationSceneManifest` declares only `UIWindowSceneSessionRoleApplication`,
  with delegate `UnityScene`. With no external-display role declared, iOS never creates a scene for the TV, so
  `sceneForScreen` returns nil and the activated window belongs to no scene. That window is never shown.
  **Without an Info.plist change, `Display.Activate()` gives a black or unchanged TV.** This is the single most
  important finding for feature 2.
- `UnityScene` forwards *every* scene's lifecycle to the app controller (`sceneDidBecomeActive` →
  `applicationDidBecomeActive`, and so on). Declaring `UnityScene` for the external role would therefore pause and
  resume the game whenever the TV comes and goes. The external role needs **its own inert scene delegate**.
- There is no `Display.Deactivate()`. Once activated, the TV keeps Unity's window until it disconnects. Going back to
  mirroring while still connected needs the native plugin to hide that window.
- `UIScreen.overscanCompensation` is set to `None`. Old HDMI TVs that overscan will crop the edges, so the TV layout
  keeps a title-safe margin.
- `UnityDisplayManager_SetRenderingResolution` works per display, and the OS scaler upscales, so a 4K TV can be fed
  1080p.

---

## 1. Architecture

```
                ┌──────────────── TvOutput (Bridge, one hidden runner; inert unless Supported) ─────────────┐
 Display.       │  TvModeLogic.Decide(setting, displays, platform, widget) → Handheld | Mirror | GameOnTv     │
 onDisplays ───▶│  GameOnTv:                                                                                 │
 Updated        │    Display.displays[1].Activate(); SetRenderingResolution(TvResolution.Choose(...))        │
                │    main camera.targetTexture = back RT (TV size)          ← ChaseCamera etc. unchanged     │
                │    OnGUI Repaint:  RenderTexture.active = back RT; UiSurface.Begin(tvW, tvH)               │
                │                    AnalogGauges.DrawForTv() / HudOverlay.DrawTvLabels()  (= the clip path) │
                │    LateUpdate:     swap back/front;  presenter camera (targetDisplay 1) blits FRONT        │
                │    phone backdrop camera (targetDisplay 0, culls nothing, clears dark)                     │
                └────────────────────────────────────────────────────────────────────────────────────────────┘
 Phone (display 0): TouchFlightControls + toolbar + menus + text lines, exactly as handheld, laid out for the phone.
 TV    (display 1): the world (main camera, including the HUD's GL lines, FPV, force vectors) + dials or HUD labels.
```

### 1.1 One rule decides what goes where

**The TV shows what a passenger sees. The phone shows everything you touch.**

- **TV:** the main camera's picture (world, aircraft, the HUD's GL symbology drawn in `HudOverlay.OnPostRender`,
  flight-path vector, force vectors, tracers) plus the instruments (`AnalogGauges`, or `HudOverlay`'s labels in HUD
  mode).
- **Phone:** `TouchFlightControls`, the toolbar (MENU, VIEW, REPLAY = pause, CLIP, REC, AERO, TIME, CHART, OPTIONS), every
  menu and panel, and the text lines (events, challenge, lesson). The text lines already lay themselves out in the band
  between the pads (`UiLayout.NextLine`), so they never land under a thumb. Lesson cards and menus are interactive,
  so they have to be where the fingers are.

Version 1 puts only the instruments on the TV. Moving the challenge and event lines there as well is open question Q3.

### 1.2 `UiSurface`: the screen IMGUI is laying out for

The HUD code reads `Screen.width/height/safeArea`, and on iOS that is always the phone. A small static,
`UiSurface` (`Width`, `Height`, `IsTv`, `Begin(w, h)` / `End()`), replaces `Screen.*` **only in the code the TV
pass runs**: `UiLayout` (S, safe insets, OptionsRect, band), `AnalogGauges`, `HudOverlay`, and the keep-out padding in
`ScreenLayout`. Outside the TV pass, `UiSurface` *is* `Screen`, so handheld play is bit-for-bit what it was. Inside the
pass:

- the safe insets are a TV title-safe margin (Q5) instead of the phone's notch;
- the band is the full TV width, because there are no pads on the TV;
- `TouchFlightControls.TouchSticksHidden` reads true, so the dials take the same full-width row they take with a
  gamepad (`HudInstrumentLayout`'s `pad` layout);
- the aircraft keep-out is in TV pixels (it is computed from the main camera, which now renders the TV).

`ScreenLayout.AircraftKeepOut` belongs to whichever screen shows the aircraft. In TV mode the phone sees an empty
keep-out (so the pads stop dodging an aircraft they cannot see) and the TV pass sees the real one.

### 1.3 The TV frame pipeline (why two textures)

OnGUI runs after every camera in the frame. A presenter camera that showed the texture the main camera had just
rendered would therefore always show the world *without* the instruments that the same frame's OnGUI draws on top,
whatever the camera order. So the pipeline ping-pongs two TV-sized textures:

- frame N: the main camera renders **back**, and OnGUI draws the instruments into **back**;
- `LateUpdate` of N+1 swaps them, and the presenter camera on display 1 blits **front** (frame N, complete).

That costs one frame of latency (16 ms) on a link that already adds 100 ms or more of its own (AirPlay), and it does
not depend on camera order. Memory is 2 × 1920 × 1080 × (4 + 4) bytes ≈ 33 MB. The main camera keeps its depth,
effects and `ChaseCamera` placement. Only its `targetTexture` changes.

The **phone backdrop camera** (display 0, culling mask 0, solid dark clear, lowest depth) stops display 0 showing
stale frames once no world camera draws there. Its cost is a clear.

### 1.4 Resolution policy (pure, `Core/TvLayout.cs`, tested)

`TvResolution.Choose(systemW, systemH, maxLong = 1920)` keeps the TV's own aspect ratio exactly. Only the size is
scaled down to fit 1920 px on the long side, with even dimensions. A 4K HDMI TV is fed 1920 × 1080 and the TV's
scaler does the rest. A phone GPU cannot draw this game at 3840 × 2160 and still drive its own screen.

Aspect ratio is correct by construction: the camera renders a texture of the TV's shape, so its projection matches
the TV with no stretch, and the dials are laid out for that shape by `UiSurface`.

### 1.5 Mode state machine (pure, `Core/TvLayout.cs`, tested)

```
setting:  TV SCREEN = GAME ON TV (default, Q1) | MIRROR          (Options › Display, saved, iOS only)
inputs:   external display present · platform supports it · not the Aero Widget app · not in a self-test

Handheld ──TV connects, setting GAME ON TV──▶ GameOnTv ──TV disconnects──▶ Handheld (camera back to the phone)
Handheld ──TV connects, setting MIRROR─────▶ Mirror (iOS mirrors; nothing of ours runs)
GameOnTv ──setting → MIRROR────────────────▶ Mirror (native plugin hides Unity's TV window → iOS mirrors again)
Mirror   ──setting → GAME ON TV────────────▶ GameOnTv (Activate, or re-show the hidden window)
```

Changing mode never resets the flight. The camera is retargeted between two frames, and the controls, the sim and
the menus never notice.

### 1.6 The native side, iOS only (`Plugins/iOS/AeroTv.mm`)

- `AeroExternalDisplayScene`: a `UIResponder <UIWindowSceneDelegate>` that does nothing. Its only job is to make iOS
  create a `UIWindowScene` on the TV for Unity's `DisplayManager` to attach to, without the app-lifecycle forwarding
  that `UnityScene` does.
- `AeroTv_SetExternalShown(int)`: hides or re-shows the windows on external-display scenes (Mirror ↔ Game on TV
  while connected).
- `AeroTv_ExternalInfo`: the TV's native pixel size, for logging and for the resolution policy.
- **Info.plist** (`BuildScript.OnPostProcessBuild`): add to `UIApplicationSceneManifest › UISceneConfigurations`:
  - `UIWindowSceneSessionRoleExternalDisplayNonInteractive` (iOS 16+);
  - `UIWindowSceneSessionRoleExternalDisplay` (iOS 15, which is the deployment target);
  - each with `UISceneConfigurationName` "External Display" and `UISceneDelegateClassName` `AeroExternalDisplayScene`.
- C#: `[DllImport("__Internal")]` under `#if UNITY_IOS && !UNITY_EDITOR`. Everywhere else the calls are stubs, and
  `TvOutput.Supported` is false, so nothing is created at runtime.

### 1.7 Audio

AirPlay mirroring and AirPlay video routes carry the app's audio to the TV, and HDMI adapters route audio to HDMI. This
is iOS routing and needs no code. Our `AudioSessionPlayback.mm` category (`playback`) allows it. The thing to check on
the device is that the radio and intercom (`VoiceIO`, play-and-record) still route correctly with HDMI connected (Q7).

---

## 2. Feature 1: mirroring

Mirroring is iOS's own behaviour, as long as we do not `Activate()` the display, and with the setting on MIRROR we never
do. What this lane adds or checks:

- **Correct aspect.** iOS letterboxes or pillarboxes the phone's picture into the TV, so nothing gets stretched.
  Nothing in the game reads `Display.displays` today, so mirroring cannot break anything of ours. The test is on the
  device (§7.3).
- **A clean TV picture.** In landscape a 19.5:9 iPhone mirrors onto a 16:9 TV with thin bars top and bottom, and an
  iPad (4:3) with bars on the sides. In portrait the TV shows a tall picture with wide side bars. That is iOS's call
  and cannot be changed from the app. GAME ON TV is the full-screen option.
- **Switching while connected.** Going from GAME ON TV to MIRROR must give the TV back to mirroring
  (`AeroTv_SetExternalShown(0)`). This depends on iOS resuming mirroring when the external scene has no visible window.
  That has to be verified on a device (risk R2).

---

## 3. Feature 2: game on the TV, phone as the controller

### 3.1 Phone, landscape (the recommended way to hold it)

```
┌───────────────────────────────────────────────────────────────────────────────┐
│ MENU  VIEW: Tail  REPLAY  CLIP  REC  AERO  TIME  CHART            OPTIONS     │  toolbar (unchanged)
│ ┌──────────────┐   Showing on the TV · Pitts S-2B   ┌──────────────┐ ┃        │
│ │   rudder ↔   │   (event / challenge lines)         │  aileron ↔   │ ┃ trim   │
│ │   throttle ↕ │                                     │  elevator ↕  │ ┃        │
│ │   brakes ⇣   │   RESET  AIRCRAFT  FLAPS 0 ½ 1      │              │ ┃        │
│ └──────────────┘   GEAR  SMOKE  CAM  ◀ LOOK ●  ▶     └──────────────┘ ┃        │
│   ═══ rudder trim ═══                                  ═══ aileron trim ═══   │
└───────────────────────────────────────────────────────────────────────────────┘
          dark backdrop: no world and no dials on the phone; the pads keep their handheld size and feel
```

Everything here is the existing handheld layout. With no aircraft keep-out on the phone, the buttons settle into the
same fixed band they use in the cockpit view. Pause is REPLAY (`FlightReplay.Enter`, freeze and review), which is
what the joystick's `pause` binding already calls. A one-line "Showing on the TV" status takes the first text line.

### 3.2 Phone, portrait

The existing portrait tray (pads at the bottom) with the upper area dark except for the toolbar and the text lines.
It works, but it wastes the phone. Q4 asks whether to lock TV mode to landscape.

### 3.3 TV

The full-screen world at the TV's own aspect, with dials (ANALOG) or the HUD (HUD) laid out for the TV's shape:
the full-width dial row under a title-safe margin, the engine row under it, the cockpit panel along the bottom in
the cockpit view. No buttons, because nothing on the TV can be touched.

### 3.4 Unity API usage

| Need | API |
|---|---|
| Is a TV there | `Display.displays.Length > 1`, `Display.onDisplaysUpdated` (also polled once a second, belt and braces) |
| Turn it on | `Display.displays[1].Activate()` once per connection, then `SetRenderingResolution(w, h)` |
| Its size | `Display.displays[1].systemWidth/systemHeight` (native), `renderingWidth/renderingHeight` (ours) |
| World onto it | main camera `targetTexture` = back RT; presenter camera `targetDisplay = 1` + `CommandBuffer.Blit(front, CameraTarget)` at `AfterEverything` |
| Instruments onto it | OnGUI Repaint with `RenderTexture.active` = back RT and `GL.LoadPixelMatrix(0, w, h, 0)`, as `ClipRecorder` does |
| Phone backdrop | camera `targetDisplay = 0`, `cullingMask = 0`, `clearFlags = SolidColor` |

---

## 4. Feature 3: TV plus a Bluetooth controller

With a pad connected the touch sticks are already hidden (`TouchSticksHidden`). In TV mode that leaves the whole
phone free, so it becomes a **button pad** (`TvButtonPad`): big targets in a grid, laid out from the safe area,
landscape or portrait.

```
┌─────────────────────────────────────────────────────────────────────┐
│ MENU                    Showing on the TV · 8BitDo Pro 2     OPTIONS │
│ ┌────────┐┌────────┐┌────────┐┌────────┐┌────────┐┌────────┐         │
│ │ CHASE  ││COCKPIT ││  TAIL  ││  SIDE  ││ FLY-BY ││  LOCK  │  views  │
│ └────────┘└────────┘└────────┘└────────┘└────────┘└────────┘         │
│ ┌────────┐┌────────┐┌────────┐┌────────┐┌────────┐┌────────┐         │
│ │ SMOKE  ││FLAPS ▲ ││FLAPS ▼ ││  GEAR  ││CAM REF ││ LOOK ● │ switches│
│ └────────┘└────────┘└────────┘└────────┘└────────┘└────────┘         │
│ ┌────────┐┌────────┐┌────────┐┌────────┐┌────────┐┌────────┐         │
│ │ PAUSE  ││ RESET  ││AIRCRAFT││  CLIP  ││  REC   ││  AERO  │  actions│
│ └────────┘└────────┘└────────┘└────────┘└────────┘└────────┘         │
└─────────────────────────────────────────────────────────────────────┘
```

- Every button calls exactly what the existing button or gamepad binding calls (`ChaseCamera.SetView`,
  `SmokeController.Toggle`, `FlapFraction`, `SetGear`, `CameraReference.Toggle`, `CockpitView.Centre`,
  `FlightReplay.Enter`, `ResetFlight`, `ClipRecorder.SaveClip/ToggleRecording`, `GameAeroPanel.Toggle`). No new
  behaviour, just more doors to the same rooms.
- The pad keeps all its bindings. The phone only adds to it.
- Only the buttons that apply are shown (no GEAR on a fixed-gear type, no SMOKE without smoke, no FLAPS without
  flaps), and the grid re-flows. The button list is a pure function of the aircraft's features (`Core/TvLayout.cs`,
  tested); the drawing lives in Bridge.
- Handheld with a pad and no TV is unchanged: the view fills the screen and the buttons float at the bottom.

---

## 5. Feature 4: companion app over the local network (plan only)

An iPad or iPhone running Aero Playground pairs with **the Mac app** and becomes its touch controller and button
panel. The Mac flies. The device only sends commands and shows the state the Mac sends back.

### 5.1 Transport: a Swift Network.framework plugin (`Plugins/iOS/AeroLink.swift`)

Following `docs/glass-companion-notes.md`:

- **Swift and Network.framework only.** .NET sockets in Unity can do neither Bonjour nor peer-to-peer Wi-Fi (AWDL).
  The same Swift source builds into UnityFramework on iOS and into `AeroNative.bundle` on the Mac.
  `tools/native-mac/build.sh` gains a `swiftc -emit-library` step, and the C-callable surface is `@_cdecl`
  functions, like `AeroStore.swift`.
- **Service type `_aeroplayground._tcp`**, one type for every role, told apart by TXT records:
  - `role` = `game` (the Mac or any device that can be controlled) or `companion`;
  - `v` = protocol version;
  - `id` = a stable random device id (Keychain);
  - `name` = the device name;
  - `paired` = a hash of the paired key's id, so a device can find "its" Mac without trying every one.
  One type from day one sidesteps the macOS behaviour where the Bonjour types allowed at the first local-network grant
  are cached, and a type added later fails with −65555. A new role is a new TXT value, never a new type. Glass's
  `_overlaystudio-control._tcp` and `_aerowidget._tcp` are never used.
- **Mac:** `NWListener` (TCP, port 0, `includePeerToPeer = true`), advertised as `Aero Playground <Mac name>`.
  **iPad/iPhone:** `NWBrowser` for `_aeroplayground._tcp` with `includePeerToPeer = true`, filtered to `role = game`.
  It auto-connects to the last paired Mac, or else lists the Macs it finds.
- **TCP options** as Glass uses them: `noDelay`, keepalive idle 5 s / interval 5 s / count 2 (≈15 s to notice a dead
  link). Reconnect every 2 s, or 20 s after a pairing refusal. Guard `conn === self.connection` and nil the
  `stateUpdateHandler` before `cancel()`, so a late callback from an old connection cannot tear down the new one.
- **Info.plist in both apps:** `NSLocalNetworkUsageDescription` and
  `NSBonjourServices = [_aerojoy._udp, _aeroplay._tcp, _aeroplay._udp, _aeroplayground._tcp]` (iOS:
  `BuildScript.OnPostProcessBuild`, Mac: `tools/deploy/mac.sh`). The Mac app is ad-hoc signed and not sandboxed today.
  If it is ever sandboxed it needs `com.apple.security.network.server` and `.client`. No multicast entitlement is
  needed.

### 5.2 Pairing: PIN or QR, then HMAC on every connection

Glass proves an account key. Aero has no accounts, so the shared key comes from a one-time pairing:

1. The Mac shows **PAIR A DEVICE** (Options › Companion): a 6-digit PIN and a QR code, valid for 2 minutes.
2. **QR:** the code carries `aeroplay://pair?id=<macID>&k=<base64 32-byte key>`. The device scans it and has the key.
3. **PIN:** the device types the PIN. The two sides run a PAKE-free exchange on the link: the Mac sends
   `{salt, macID}`, both derive `k = HKDF-SHA256(PIN, salt, "aero-pair")`, and the device proves knowledge with
   HMAC(k, nonce). The Mac then sends the long-term 32-byte key **encrypted under k** (ChaCha20-Poly1305). A 6-digit PIN
   is brute-forceable offline from a captured exchange, so the PIN is single-use, short-lived, and limited to 5 tries
   per window. QR is the recommended path (Q9).
4. Both sides store `{peerID, key}` in the Keychain. **Every later connection** runs Glass's mutual challenge-response
   (`ControlLinkAuth`), with the same message shapes and our own labels:
   - Mac → device `{step: challenge, nonce: sn, device: macID}`
   - device → Mac `{step: proof, nonce: cn, device: devID, proof: HMAC(key, "aero-link-C"|sn|macID|devID|cn)}`
   - Mac → device `{step: result, ok: true, proof: HMAC(key, "aero-link-S"|cn|sn|macID|devID)}`
   The rules carry over too: 10 s timeout, constant-time compare, nothing but `hello` accepted before the result, and one
   live link per device id (a new one replaces the old).
5. The wire is unencrypted after authentication, as in Glass. Control inputs to a flight game are low-value, but
   TLS-PSK with the paired key is available in Network.framework (`sec_protocol_options_add_pre_shared_key`) and is
   the recommended hardening once it works (Q10).

### 5.3 Messages: newline-delimited JSON, Mac authoritative

Each line is `{"kind": "...", "payload": {...}}`.

| kind | direction | payload | rate |
|---|---|---|---|
| `auth` | both | the handshake above | once |
| `hello` | both | app, version, protocol, role, device name | once |
| `controls` | device → Mac | `aileron, elevator, rudder` −1…1, `throttle` 0…1 or `throttleRate` −1…1, `brake`, `trimDelta`, `held: [...]`, `seq` | 60 Hz while a finger is down, 4 Hz idle heartbeat |
| `command` | device → Mac | `{name: view\|smoke\|flaps\|gear\|camRef\|lookCentre\|pause\|resume\|reset\|aircraft\|clip\|rec\|menu, value?}` (the `docs/WIDGET.md` names) | on tap |
| `state` | Mac → device | full snapshot: aircraft id/name, its features (flaps detents, gear, smoke, guns), current view, flaps, gear, smoke on, paused, replay, menu open, IAS/alt for a small readout | on connect, on change, plus 2 Hz |
| `ack` | Mac → device | `{seq, accepted, reason?}` for commands | per command |

The Mac is the authority. The device draws its buttons from the last `state` (a GEAR button exists only if the Mac
says the aircraft has retractable gear), and a toggle is sent as an intent (`smoke: on`) rather than a flip, so a
lost or duplicated message cannot invert it. Coordinates or axes are always normalized. A `controls` message gets no
ack: it is simply superseded by the next one, and if none arrives for 0.5 s the Mac lets go of the controls (the
same release-on-silence rule `GamepadInput` uses when a pad disappears).

### 5.4 Where the companion's inputs enter the sim

At the same seam as the gamepad: `TouchFlightControls.GamepadFlight()` → `PilotControls.Compose(...)`. A
`RemoteControlSource` (Bridge, shared) exposes the same `Frame` shape `GamepadInput` does, so on the Mac a companion
looks like one more pad, with the same "touch sticks hidden" and "release on silence" behaviour, and works in every
view including the outside ones. On the device, the companion mode is the TV-mode controller from features 2 and 3,
with its output going to `AeroLink` instead of into a local sim. One controller codebase serves all three modes.

### 5.5 Companion UI on the device

The device runs the normal app and picks **Companion** on the landing page:

- **Searching:** a list of Macs, with "Pair a new Mac" (camera for the QR, or a PIN field).
- **Connected:** the feature 2 controller (pads, trim, buttons) or, with a pad paired to the iPad, the feature 3
  button grid, plus a slim status bar with the Mac name, link quality (round-trip ms) and the aircraft.
- **Refused or lost:** a plain message and retry, following Glass's `ControlLinkAuth.message(reason)` wording style.

---

## 6. Platform parity (iOS / Mac / Quest)

| Code | iOS | Mac | Quest (Android) | How it is kept out |
|---|---|---|---|---|
| `Core/TvLayout.cs` (resolution, mode logic, button list) | ✓ | ✓ | ✓ compiled, unused | pure C#, no platform calls |
| `Bridge/TvOutput.cs`, `UiSurface.cs`, `TvButtonPad.cs` | ✓ active | compiled, inert | compiled, inert | `TvOutput.Supported` is `UNITY_IOS && !UNITY_EDITOR` (or a test hook). The runner is never created elsewhere, so there is zero per-frame cost |
| `Plugins/iOS/AeroTv.mm` | ✓ | ✗ (not in `native-mac/build.sh`) | ✗ | the plugin importer is iOS-only (`.meta`) and the `DllImport` sits under `#if UNITY_IOS && !UNITY_EDITOR` |
| `Plugins/iOS/AeroLink.swift` (phase 4) | ✓ | ✓ via AeroNative.bundle | ✗ | same pattern as JoyLink |
| Test asmdefs | Editor only | Editor only | Editor only | `includePlatforms: [Editor]` / `UNITY_INCLUDE_TESTS` |

Nothing here touches `unity/Packages/`, XR settings or `Plugins/Android/`. The Test Framework (1.6.0) already resolves
as a dependency of `com.unity.collections`, so no manifest change is needed. Following docs/PARITY.md on
`lane/claude-parity`, every file above is "shared" and must be merged into the Quest lane the same day as island-nav.
On the Quest it compiles to inert code.

The Mac does not get GAME ON TV in this lane. A Mac with a second monitor can already use it as a big window, and a
dedicated "instruments on the second monitor" layout is a different feature (Q8).

---

## 7. Testing

### 7.1 FlightTests (.NET, `tools/FlightTests`, pure Core)

- `TvResolution.Choose`: aspect preserved to within 1 px for 16:9, 16:10, 4:3, 21:9, 19.5:9 and 4K inputs; never
  larger than the native size; long side ≤ 1920; even dimensions. Expected values are literals.
- `TvModeLogic.Decide`: every row of the state table in §1.5, including "the widget app never uses the TV" and
  "Mirror never activates".
- `TvButtonPad` layout: the button set per aircraft feature set (literals), and that every button rect stays inside the
  safe area with a minimum 44 pt target, for landscape and portrait iPhone and iPad sizes, with no overlaps.

### 7.2 Unity EditMode and PlayMode (`unity/Assets/_Project/Tests/`)

- **EditMode:** `UiSurface` falls through to `Screen` when no override is active and nests correctly. `UiLayout.S`,
  `SafeTop`, `BandLeft/Right` and `OptionsRect` follow the override. `ScreenLayout.CameraViewport` is full while TV is
  active. The aircraft keep-out is empty for the phone and real for the TV.
- **PlayMode (simulated TV):** `TvOutput.SimulateForTest(1920, 1080)` drives the real pipeline, except that the
  presenter blits into a capture texture instead of display 1 (the Editor has a single display). The tests check:
  - the main camera's `targetTexture` is a 1920 × 1080 RT and the TV image is not empty;
  - IMGUI pixels drawn in the TV pass land in the TV image and not in the phone's backbuffer;
  - the phone backdrop camera exists;
  - turning the simulation off returns the camera to the screen with the viewport it had and destroys the extra
    cameras and textures, and repeated connect and disconnect leaks no RenderTexture.
- Unity batch compile with 0 `error CS`, and `deploy.sh` to `** BUILD SUCCEEDED **` (CLAUDE.md: a green suite is not a
  build).

### 7.3 On the device (Ty; this lane installs nothing)

1. AirPlay to an Apple TV and HDMI through a Lightning/USB-C adapter, each with the setting on MIRROR: aspect correct,
   nothing broken, sound on the TV.
2. The same with GAME ON TV: full screen at the TV's shape, dials readable from the couch, the phone shows only the
   controls, and a flight flown entirely from the phone.
3. Connect and disconnect mid-flight in both directions. The flight continues, and the phone gets its view back within
   a frame.
4. Switch MIRROR ↔ GAME ON TV in Options while connected (risk R2).
5. With the 8BitDo connected: the button grid appears, every button works, and pad bindings are unchanged.
6. Radio or intercom with HDMI connected (Q7).

---

## 8. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | The external-display scene role behaves differently from what the trampoline reading suggests (for example, iOS stops mirroring as soon as the role is declared, even before we draw). | Declaring the role changes nothing until a window is shown. If iOS stops mirroring early, the MIRROR setting has to show a mirrored view itself, by blitting the phone's image to the TV. The plan keeps that fallback open. |
| R2 | Hiding Unity's TV window may not hand the TV back to mirroring. | If it does not, MIRROR takes effect at the next reconnect, and Options says so. |
| R3 | IMGUI into a RenderTexture during OnGUI is a documented-by-practice technique, not an API promise. | It already ships in `ClipRecorder` on iOS and Mac. The PlayMode test checks pixels land in the TV image. |
| R4 | GPU cost: the phone renders the world at 1080p plus its own UI. | The world renders once, for the TV, and the phone renders only IMGUI on a cleared screen. Net cost is probably *lower* than handheld on a 3× iPhone (2622 × 1206 → 1920 × 1080). There is a thermal check on the device. |
| R5 | Other lanes edit `UiLayout`, `AnalogGauges` and `HudOverlay`, so merge conflicts are possible. | The edits are one-token replacements (`Screen.width` → `UiSurface.Width`) in few lines, and the logic lives in new files. |
| R6 | Quest build breaks. | No platform API outside `#if UNITY_IOS`. Pure C# for the rest, no new packages, and the iOS-only `.meta` plugin importer. The Quest lane compiles the same Bridge, so a Unity batch compile for Android is worth one run before merge (Q11). |
| R7 | AirPlay latency (≈100–200 ms) makes flying from the phone while watching the TV feel soft. | This is inherent to AirPlay. HDMI is near zero. The one-frame pipeline latency we add is negligible next to it. Document it, and recommend HDMI or the pad for aerobatics. |
| R9 | An iPad with **Stage Manager** may use the TV as an extended desktop instead of mirroring. The app's own window can then be dragged onto the TV, and iOS may never offer the external-display role (the app declares a single scene). | Test on an iPad with Stage Manager on and off (§7.3). If extended mode never produces a second `Display`, iPad TV mode needs Stage Manager off, and Options says so. |
| R8 | PIN pairing can be brute-forced offline if the exchange is captured. | Single-use PIN, 2-minute window, 5 tries, QR as the recommended path, and TLS-PSK later. |

---

## 9. Open questions for Ty

- **Q1.** Default for a newly connected TV: **GAME ON TV** (my assumption, it is what you asked for) or MIRROR?
- **Q2.** Should GAME ON TV also apply on the **iPad** when it is AirPlayed (same code), or iPhone only?
- **Q3.** On the TV, instruments only (v1), or also the event, challenge and lesson lines? The lines are on the phone
  in v1 because lesson cards have buttons.
- **Q4.** Lock the phone to landscape while in TV mode, or keep portrait available?
- **Q5.** TV title-safe margin: 0 (Apple TV and modern TVs do not overscan) or 3 % (safe on old HDMI TVs)? v1 uses 2.5 %.
- **Q6.** Feature 3's grid: is the button list in §4 right? Anything you reach for in flight that is missing (for example
  CHART, TIME, a radio frequency)?
- **Q7.** Do you use the radio or intercom while on a TV? If yes, which output should it use?
- **Q8.** Mac: is "instruments on a second monitor" wanted, or is the Mac out of scope for TV mode?
- **Q9.** Companion pairing: QR only, PIN only, or both? (Both is planned, with QR recommended.)
- **Q10.** Companion transport encryption: is TLS-PSK worth doing in v1, or later?
- **Q11.** OK to run one Android (Quest) batch compile from this lane before it merges, to prove the Quest still builds?

---

## 10. Phasing

| Phase | Content | State |
|---|---|---|
| 1 | This plan, plus `docs/glass-companion-notes.md` | this commit |
| 2a | `Core/TvLayout.cs` (resolution, mode logic) + FlightTests | next |
| 2b | `UiSurface`, `TvOutput` pipeline, `AeroTv.mm`, Info.plist scene role, Options setting | next |
| 2c | Unity EditMode/PlayMode tests, batch compile, `deploy.sh` BUILD SUCCEEDED (not installed) | next |
| 3 | `TvButtonPad` (feature 3) + tests | if time allows |
| 4 | `AeroLink.swift`, pairing, companion UI | plan only, after Ty's answers to Q9 and Q10 |
