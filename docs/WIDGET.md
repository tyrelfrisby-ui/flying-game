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
| `start` | `condition`: `cruise` \| `final` \| `spin`; `aircraft` (optional); `view` (+ `from`) (optional) | **Protocol 5:** start from one of three known states with one tap (below). Replies with `condition`, `view`, `viewFrom`, `viewFixed`, `hold`. A view the condition doesn't allow gets `ok:false` with the reason; the start still happens, in the default view. |
| `scenario` | `name`: `flare` \| `spin` \| `cruise`; `aircraft`: fleet id (optional); `flaps`: 0…1 (optional, flare only) | Load or restart the scenario. |
| `controls` | `aileron`, `elevator`, `rudder`: −1…1; `throttle`, `brake`, `brakeL`, `brakeR`: 0…1; `handsOff`: bool; `flaps`: 0…1; `spoilers`: 0…1; `spoilersArmed`: bool; `gear`: `up` \| `down`; `source`: label | Set the controls (any subset). **Elevator + = stick FORWARD (nose down); − = pull.** Aileron + = right, rudder + = right. `handsOff` frees the elevator to float on its trim. `brake` sets both toe brakes. The levers (protocol 4) are described below. Reply `accepted: false` (+ `reason`) when the sender isn't the pilot flying or review is paused. |
| `preset` | `name`: `spin-entry` \| `spin-developed` \| `spin-recovery` \| `flare-demo` \| `flare-hands-off` | Run a scripted moment. Any control input from anywhere cancels it. |
| `pause` | | Freeze on the current frame and enter **review** (below). |
| `resume` | `from`: `live` (optional) | Fly on **from the frame shown**: the history after it is discarded (a branch), and the aircraft continues with that frame's pose, rates, actuator positions and controls. `from:"live"` snaps back to where it was paused instead. |
| `step` | `frames`: ±N (default −1) | While paused: N frames back (−) or forward (+) through the history, clamped at the oldest. Forward past the live edge flies the sim on N frames (1/30 s each) and stays paused. |
| `rewind` | `seconds`: S | Jump back S seconds (clamped to the history). |
| `seek` | `offsetMs`: ≤ 0 | Jump to an absolute offset from the live edge (0 = live). |
| `play` | `rate`: 0.25 \| 0.5 \| 1; `direction`: `forward` \| `reverse` | Play through the history while paused (reverse = rewind in motion); stops at either end. Any step/seek/rewind stops it. |
| `timescale` | `value`: 0.05…1 | Slow motion. |
| `reset` | | Restart the current scenario. |
| `view` | `name`: `side` \| `behind` \| `front` \| `top` \| `chase` \| `locked` \| `body`; `from` (locked only): `current` (default) \| `side` \| `behind` \| `front` \| `top` | Spin camera (the flare is always side-on and accepts any view). **`locked` = direction lock:** the aircraft stays centred at the same distance, but the camera's direction is fixed in the world, so the viewer sees the airplane rotate, pitch and roll in place. `current` freezes the direction the camera has when engaged; the others lock to that world-fixed direction. `side`, `behind`, `front` and `top` are world-fixed too; only `chase` turns with the aircraft. The altitude wrap moves the camera with the aircraft (no jump). **`body` = airplane-fixed** (from `left`): the camera is rigidly attached to the airframe and looks at its left side, so the airplane holds still while the horizon, the ground grid, the relative wind and the weight vector turn around it (vectors keep their true world directions). In FINAL every view is refused (`"view is fixed in final"`); in the SPIN condition only `locked` (from side) and `body` (from left) are accepted. |
| `hello` | | Reply with app, version, `protocol: 5`, `conditions`, `features`, `controlInputs`, `controlsDisplay`, `fleet` (each with its `controls` fit), `commands`, `scenarios`, `views`, `viewFrom`, `presets`, `show` keys, frame size, stream names, ports, and `review: {historySeconds: 60, fps: 30, commands: [step, rewind, seek, play]}`. |
| `show` | any of `lift drag weight thrust wind total axis wheels labels readout strips review controlsDisplay controlTraces horizon`: bool | Toggle vectors and text. `strips` = every strip's lift and drag; `review` = the "REVIEW -2.4 s" tag in the readout; `controlsDisplay` = the NTSB-style control panel; `controlTraces` = its 10 s time-history strips; `horizon` = the horizon line and the ground grid (cruise and spin). |
| `controlsDisplay` | `place`: `bottom` \| `left` \| `right`; `size`: 0.2…0.5; `show`: bool (optional) | Where the control panel goes and how much of the picture its strip takes. Default: off, bottom, 0.28. |
| `pilot` | `id` (null clears), `name`, `color` (`#RRGGBB`), `managed`: bool (default true) | Name the pilot flying (below). |
| `requestControls` | `from`: `web-…`, `name` | Sent by the web remote's **Request controls** button; the widget pushes `{"event":"controlRequest",…}` to subscribers. |
| `fleet` | | Reply `fleet: [{id, name, controls}]`; `controls` = `{flaps: [detent °…], spoilers: bool, gear: fixed\|retractable, engines: n}`. |
| `state` | | Reply with the state (below). |
| `subscribe` | `hz`: 1…60 | Stream state lines on this connection at that rate. |
| `snapshot` | `path` (optional) | Write the current frame (RGBA PNG) and reply with `path`. |
| `gamepad` | `aileron`, `elevator`, `rudder`, `throttle`: joystick axis numbers 1–8; `invertElevator`, `throttleFromAxis`: bool | Remap the gamepad. |

State (reply to `state`, and the `subscribe` stream):

```json
{"ok":true,"type":"state","scenario":"spin","aircraft":"c172-like","flaps":0,"paused":false,"timescale":1,"view":"side",
 "viewFrom":"","preset":"spin-entry","source":"network","kias":58.8,"ktas":62,"alpha":29.4,"beta":-3,"q":540,"pitch":-52,"roll":-20,
 "heading":212,"sinkFpm":5400,"heightFt":7948,"rollRate":-95,"pitchRate":4,"yawRate":-140,"nz":1.1,
 "leftWingAlpha":34.9,"rightWingAlpha":23.9,"leftStalled":true,"rightStalled":true,"onGround":false,
 "controls":{"aileron":0,"elevator":-1,"rudder":-1,"throttle":0,"brake":0,"brakeL":0,"brakeR":0,"handsOff":false,
   "flaps":0,"flapsDeg":0,"flapsActual":0,"flapsActualDeg":0,"spoilers":0,"spoilersArmed":false,"gear":"fixed","gearLights":"down"},
 "condition":"spin","viewFixed":false,"hold":true,
 "pilot":{"id":"sam","name":"Sam","color":"#4FC3F7","managed":true},"remotes":[{"id":"web-k3j9x2","name":"Rae"}],
 "controlsDisplay":{"shown":true,"place":"bottom","size":0.28,"traces":false},
 "syphon":"Aero Widget","ndi":"Aero Widget","frame":1080,
 "review":{"active":false,"offsetMs":0,"historyMs":60000,"playing":false,"rate":0.25,"direction":"reverse"}}
```

### Review mode (protocol 3)

For teaching: pause at the moment that matters (the stall break, the flare, the recovery) and step back and forth through it.

- **History:** a ring buffer of the last **60 s of simulation time at 30 frames/s** (1,800 frames, the oldest dropped). Each frame holds the aircraft state (pose, velocities, rates), the actuator positions, flaps, gear, power, Nz, the controls, every force sample the vectors are drawn from, and the camera. A past frame is redrawn **exactly** as it looked: arrows, labels, readout, per-wing α and STALLED flags.
- **Offsets:** one frame = 33.3 ms of sim time (slow motion records fewer frames per real second; 60 s is always 60 s of flight).
- **Picture:** Syphon and NDI show the reviewed frame. The readout gets a `REVIEW -2.4 s` tag (plus ◀/▶ ×rate while playing); hide it with `show {"review":false}`.
- **Read-only:** controls touched while paused (port, remote, keyboard, gamepad) are ignored. Nothing moves the airplane until `resume`.
- **Replies:** every review command (and `pause` / `resume`) replies with the shown frame:

```json
{"ok":true,"cmd":"step","offsetMs":-33,"frame":{"index":-1,"offsetMs":-33,"simTime":15.78,"kias":61.9,"alpha":15.6,
 "pitch":8.8,"roll":-0.1,"yawRate":-17,"leftWingAlpha":16.4,"rightWingAlpha":15.1,"leftStalled":true,"rightStalled":true}}
```

In review, `controls` (and everything else in the state) is the frame being shown.

### Start conditions (protocol 5)

The instructor starts every lesson from one of three known states with one tap. `{"cmd":"start","condition":…}`. The web remote has three big buttons for them at the top (Cruise · Final · Spin).

| Condition | State | View |
|---|---|---|
| **cruise** | ~3,000 ft over the flat world, 75 % power, gear up, flaps up, straight and level and **trimmed** (below). Free flight: every control and view works. | Any; default `chase`. `view` + `from` pick the start view. |
| **final** | 300 ft, on final, lined up, wings level, with the type's approach speed and flaps: 1.3 Vso, full flaps on the light types (C172 30°), 30 on the 737. It flies on into the flare and touchdown, and replaces "start the flare from the air". | Side-on (the flare view), **fixed**. |
| **spin** | A developed spin (the spin-entry inputs, then held about 14 s, settled), with **full pro-spin controls held**: stick full aft, rudder full with the rotation, ailerons neutral, idle. **HOLD mode** (below). PARE (preset `spin-recovery`) or any inputs still recover it. | `locked` from the side (default), or `body` from the left. `{"cmd":"start","condition":"spin","view":"body"}` starts in the body view. |

**Cruise trim.** Solved on the sim itself, in two stages:
1. Newton on short trial runs for speed, α and elevator.
2. The widget flies it offscreen for 60 s with a gentle wings-level, ball-centred, zero-VSI hold, the way a pilot trims. The engine, slipstream and torque settle too.

- **Result:** the settled control positions become the trim. Hands-off, the stick and pedals return to them (a C172 needs a touch of right rudder at 75 %), and the start is that settled airplane.
- **C172:** 119 KIAS. Hands-off for 30 s it held altitude within 1 ft, with no bank drift.
- **Gliders:** they have no power, so they glide trimmed at best L/D.
- **Start time:** about 1–2 s.

**Final — what "idle" means.** A light airplane at idle with full flaps glides much steeper than 3°. The widget doesn't fake it.
- **Props and gliders** start at 300 ft at idle on their **own idle glide path**, aimed at the runway numbers. Hands-off they fly it to the runway. Measured paths:

  | Type | Speed | Flaps / spoilers | Path |
  |---|---|---|---|
  | C172 | 64 KIAS | 30° | 8.4° (~0.3 nm out) |
  | Super Cub | 52 KIAS | 50° | 9.3° |
  | 2-33 | 36 KIAS | half spoilers | 5.4° |

- **Jets** (the 737) fly the **3° powered path** of the jet flare lesson, trimmed so hands-off it stays on the path. Power comes off to idle from 50 ft to 5 ft.
  - **737:** 144 KIAS on 31 % power, 3.0° steady, about 1 nm out.
  - **F-86:** no trim exists at its approach flaps. The model's flap pitching moment beats full nose-down elevator, so it pitches up. Open issue in the aircraft model.
- **Picture:** the runway is drawn as a thick white line on the ground plane, with a threshold mark and a 45 m aim-point bar. While the aircraft is still well out, the frame widens so the threshold and the aim point plus 150 m are in view: the aircraft sits at 28 % from the left, the far point at 92 %, the ground 35 % up. It narrows into the flare's own framing over the last ~80 m.
  - At 300 ft the airplane is only a few pixels long (the frame is about 1 km wide). The vectors and labels mark it.
- **Power:** follows the lesson (idle; the jet's schedule) until anyone moves it.

**HOLD** (spin condition, `"hold":true` in the state). Every input source keeps the stick where it was put: no spring, no auto-centre. Inputs only change when someone moves them, so the instructor can hold pro-spin elevator and rudder while working aileron and power.
- **Network / Glass Overlay:** values stay until the next `controls`.
- **Web remote:** shows HOLD on its stick. The dot stays where released and shows the held position; the rudder slider doesn't spring back.
- **Keyboard:** a held key moves that control; let go and it stays.
- **Gamepad:** its sticks spring back, so in HOLD they **nudge** the held position (deflection = rate).
- **Handover:** a handover never centres the stick in HOLD.
- **Tested** (Pitts): one input of aileron +0.6 and power 50 %, then nothing. Elevator −1 and rudder −1 stayed held, and the spin flattened (α 77°, yaw 152°/s). PARE recovered in about 8 s.

**Review and the control display** work the same in every condition. The display shows the held inputs (PULL 100 %, L 100 %).

### Control display (protocol 4)

An NTSB-animation-style schematic drawn **into the frame**, so it's on Syphon and NDI: a dark translucent panel with white line art, small caps labels and a value under each control.

| Control | Shows |
|---|---|
| **Control wheel** | The wheel rotates with aileron (90° at full deflection). The column is a bar marked PUSH / PULL with %. |
| **Rudder pedals** | The pressed pedal slides forward, the other back; L/R %. The toe brakes light red with braking (L/R). |
| **Throttle** | IDLE…FULL, %. One lever per engine (2 on the twins and the 737, 8 on the H-4). |
| **Flaps** | The handle on a gate with the type's own detents, evenly spaced like the real quadrant (737: UP·1·2·5·10·15·25·30·40). An amber pointer shows the actual position while it lags. |
| **Spoilers / speed brake** | RET · ARM · EXT. |
| **Gear** | Handle UP / DN and three lights: green = down and locked, red = in transit, off = up. |

- **Missing controls:** a type that lacks a control shows it dimmed, `FIXED` or `N/A` (Pitts: gear FIXED, spoilers N/A, flaps N/A). The layout never changes.
- **Header:** `PF: <name>` with the pilot's colour chip, and `HANDS OFF` when hands-off. For 2.5 s after a handover it reads `YOU HAVE THE FLIGHT CONTROLS — <name>` on the pilot's colour.
- **Traces** (`show {"controlTraces":true}`): the last 10 s of elevator (▲ pull), aileron, rudder and throttle, read from the review history. In review they end at the frame shown.
- **Layout:** the panel gets its own strip (`place`, `size`). The scene is rendered to the rest of the picture and composed with the panel, so the panel never covers the airplane, the vectors or the readout. Values are large enough to read when the Glass shows the widget at ¼ size; at ¼, use `size` 0.4–0.5 and traces off.
- **What it shows:** the pilot's inputs. In the flare demo it shows the lesson's law, which is what's flying.

**The levers:**

- **Flaps:** the `flaps` handle snaps to the nearest detent. The flaps follow at the type's rate: C172 electric ~9 s full travel, the 737 ~40 s, manual handles (Cub, PA-28, Seminole, Pawnee) ~1.5 s. They change the flight in both scenarios.
- **Spoilers:**
  - **737:** speed brake on a separate lever (`Aircraft.SpoilerCommand`). The aero model is drag-only: flat-plate drag on 3 m² of panel. Lift dump is not modelled.
  - **Gliders:** the spoilers / dive brakes.
  - **ARMED:** the spoilers deploy at touchdown (weight on the mains) and the lever goes to EXT.
- **Gear:** retractable types transit in 6 s, with the gear drag and wheel contact following the actual extension. Ignored on fixed gear.
- **Brakes:** `brakeL` / `brakeR` are differential (`brake` = both). They act on the wheels in the flare.

### Pilot flying (protocol 4)

Glass Overlay holds the arbitration: who asks, who approves. The widget shows the pilot and enforces the choice.

- **`pilot {"id","name","color","managed":true}`** sets the PF label.
  - **Managed:** only `controls` from the pilot's source move the airplane. For a Glass Overlay user that's the TCP connection that sent `pilot`; for `id: "web-…"` it's that web remote. Everything else is view-only: other connections and remotes get `accepted:false, reason:"<name> is flying"`, and the keyboard and gamepad stop flying (the preview window says "<name> is flying").
  - **Clearing:** `{"cmd":"pilot","id":null}` clears it, and everything flies again.
- **Handover:** the previous pilot's last inputs are held until the new pilot's first input, then eased to it over 0.3 s (smoothstep). If nothing arrives within 2 s, the stick and rudder centre and the power stays.
  - Measured mid-spin: α moved ≤ 0.3° per frame through the ease, and the rates stayed continuous.
- **Web remotes:** each remote has a stable id (`web-…`) and the user's name, both kept in its localStorage.
  - It reports them on every poll, and the state lists `remotes`. `{"event":"remotes","remotes":[…]}` is pushed when the list changes; a remote drops off after 6 s of silence.
  - When another pilot is managed, the remote shows "<name> is flying — view only" and a **Request controls** button. The button asks for a name once, then pushes `{"event":"controlRequest","from":"web-…","name":"…"}` to every `subscribe` stream.
- **Events:** `{"event":…}` lines arrive on `subscribe` connections between the state lines.

### HTTP 47831 (phone / iPad remote)

- **`http://<mac-ip>:47831/` in Safari:** a touch stick, rudder, power and brake sliders, the scenario, aircraft and view pickers, the presets, pause and slow-motion, and the review transport (◀︎◀︎ 1 s / ◀︎ frame / frame ▶︎ / ▶︎▶︎ 1 s, play ¼ either way, Fly from here, Back to live) with a history scrub bar. No app to install.
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
  | Space | Pause (review) / resume from the shown frame |
  | , / . | One frame back / forward (review) |
  | Shift + , / . | One second back / forward |
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
