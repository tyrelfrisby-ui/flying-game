# Aero Widget — transparent physics scenarios for Glass Overlay

Owner request (2026-10-07): a stripped-down Aero Playground that Glass Overlay can call on during a podcast. Two fixed
scenarios on a **transparent** background, driven by the game's own physics and aerodynamics (`src/`), with any aircraft
in the fleet.

| Scenario | Picture |
|---|---|
| **flare** | Side view only, orthographic. The aircraft, a white line for the surface, lift / drag ×3 / weight / thrust at the CG, the relative wind with α, the chord line, the wheel loads at the touch. |
| **spin** | The aircraft alone, the camera following. For each wing at mid-semispan: the local relative wind (the rotation adds ω × r) with its α and STALLED flag, that wing's lift and drag; weight, the total aerodynamic force, the rotation axis. |

Small labels plus a top-left readout (KIAS, α, pitch; the flare adds sink and height, the spin adds yaw / roll rate
and each wing's α). Labels have a dark drop shadow.

## App

- **`/Applications/Aero Widget.app`** (`com.tyrelfrisby.aerowidget`). Built from the Aero Playground Unity project.
  - Build: `tools/deploy/widget.sh` (log: `build/widget.log`).
  - `SceneBootstrap` sees the bundle id and builds the widget instead of the game.
- **Window:** a 720 × 720 preview of the frame over a checkerboard (the checks are the transparent part), plus status text.
- **Runs in the background** and keeps streaming while another app has focus.

## Video out (both at once, 1080 × 1080 RGBA, ~60 fps)

| Transport | Name | Use |
|---|---|---|
| **Syphon** | server **"Aero Widget"** (app "Aero Widget") | Same Mac: zero-copy GPU texture **with alpha**. |
| **NDI** | source **"<MAC NAME> (Aero Widget)"** | Another machine on the LAN, **with alpha** (UYVA). |

Premultiplied? No: straight alpha. The aircraft and lines are opaque (α = 1); everything else is α = 0.

## Control

### TCP 47830 (Glass Overlay / its MCP layer)

Newline-delimited JSON, one command per line, one JSON reply line per command (`{"ok":true,"cmd":…}`, or `{"ok":false,"error":…}`).

| Command | Fields | Effect |
|---|---|---|
| `scenario` | `name`: `flare` \| `spin`; `aircraft`: fleet id (optional); `flaps`: 0…1 (optional, flare only) | Load or restart the scenario. |
| `controls` | `aileron`, `elevator`, `rudder`: −1…1; `throttle`, `brake`: 0…1; `handsOff`: bool; `source`: label | Set the controls (any subset). **Elevator + = stick FORWARD (nose down); − = pull.** Aileron + = right, rudder + = right. `handsOff` frees the elevator to float on its trim. |
| `preset` | `name`: `spin-entry` \| `spin-developed` \| `spin-recovery` \| `flare-demo` \| `flare-hands-off` | Run a scripted moment. Any control input from anywhere cancels it. |
| `pause` / `resume` | | Freeze or continue the physics. |
| `timescale` | `value`: 0.05…1 | Slow motion. |
| `reset` | | Restart the current scenario. |
| `view` | `name`: `side` \| `behind` \| `front` \| `top` \| `chase` | Spin camera (the flare is always side-on). |
| `show` | any of `lift drag weight thrust wind total axis wheels labels readout strips`: bool | Toggle vectors and text. `strips` = every strip's lift and drag. |
| `fleet` | | Reply `fleet: [{id, name}]`. |
| `state` | | Reply with the state (below). |
| `subscribe` | `hz`: 1…60 | Stream state lines on this connection at that rate. |
| `snapshot` | `path` (optional) | Write the current frame (RGBA PNG) and reply with `path`. |
| `gamepad` | `aileron`, `elevator`, `rudder`, `throttle`: joystick axis numbers 1–8; `invertElevator`, `throttleFromAxis`: bool | Remap the gamepad. |

State (reply to `state`, and the `subscribe` stream):

```json
{"ok":true,"type":"state","scenario":"spin","aircraft":"c172-like","flaps":0,"paused":false,"timescale":1,"view":"side",
 "preset":"spin-entry","source":"network","kias":58.8,"ktas":62,"alpha":29.4,"beta":-3,"q":540,"pitch":-52,"roll":-20,
 "heading":212,"sinkFpm":5400,"heightFt":7948,"rollRate":-95,"pitchRate":4,"yawRate":-140,"nz":1.1,
 "leftWingAlpha":34.9,"rightWingAlpha":23.9,"leftStalled":true,"rightStalled":true,"onGround":false,
 "controls":{"aileron":0,"elevator":-1,"rudder":-1,"throttle":0,"brake":0,"handsOff":false},
 "syphon":"Aero Widget","ndi":"Aero Widget","frame":1080}
```

### HTTP 47831 (phone / iPad remote)

- **`http://<mac-ip>:47831/` in Safari:** a touch stick, rudder, power and brake sliders, the scenario, aircraft and view pickers, the presets, pause and slow-motion. No app to install.
- **`POST /cmd`:** takes the same JSON as TCP and replies the same way.
- **`GET /state`:** returns the state.

### Gamepad / joystick and keyboard (on the widget's Mac)

- **Gamepad:** default axes are 1 aileron, 2 elevator, 4 rudder, 3 throttle (if `throttleFromAxis`). Remap with `gamepad`.
- **Keys:**

  | Key | Action |
  |---|---|
  | 1 / 2 | Flare / spin |
  | Arrows | Stick |
  | A / D | Rudder |
  | W / S | Power |
  | B | Brakes |
  | H | Hands-off |
  | Space | Pause |
  | R | Reset |
  | P | Next preset |
  | [ / ] | Slower / faster |

**Who has the airplane:** the last source to move. A preset overrides all sources until it ends or anyone touches the controls.

## Physics notes

- **Flare:** the flare lesson's own start (on the power-off glide at 1.3 Vso, in trim, with the flap setting chosen) and its longitudinal constraint. The demo preset flies the lesson's own law.
- **Spin:**
  - **Start:** 2,500 m up, at 1.15 Vs clean, power off, in trim.
  - **Altitude wrap:** below 600 m the aircraft is lifted 1,800 m and nothing else changes, so the spin never runs out of sky.
  - **Stall flag:** a wing reads STALLED at α > 15°.
- **World:** flat at 0, with no thermals, wind or turbulence.
