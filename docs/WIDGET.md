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
| `display` | `mode`: `ntsb` (default) \| `classic`; `split`: 0.35…0.6 | **Protocol 6:** the NTSB layout (airplane above, instrument plates below) or the classic picture. |
| `vectorStyle` | `width`: 1…3 (×); `smoothingMs`: 0…400 | **Protocol 7:** line width and the vector filter's time constant (default 130 ms). |
| `lesson` | `id` (or `"stop"`) | **Protocol 8:** start a Stick-and-Rudder scene with its scripted demonstration (docs/STICK-AND-RUDDER.md). |
| `loading` | `grossWeightLb`, `cgPercentMac` (either), or `reset:true` | **Protocol 8:** live weight and balance, in any condition, the spin included. Changes are slewed so a dragged slider never jolts the airplane. Out of limits is allowed. |
| `autopilot` | `on`, `mode` (`alt` \| `vs` \| `flc` \| `hdg` \| `rol`), `altitudeFt`, `vsFpm`, `kias`, `heading` / `headingBump` / `headingSync`, `yawDamper`, `autothrottle` | **Protocol 8:** the autopilot (below). `mode` `lnav` / `loc` / `app` replies `"not available yet"`. |
| `wind` | `fromDeg`, `kt` | Steady wind (the air mass moves). |
| `inset` | `name`: `clAlpha` \| `liftDrag` \| `powerRequired` \| `ball` \| `aoa` \| `wb`; `show` | Small plates in the picture's top-right, each a curve from the sim's own aero model with a live dot. |
| `hello` | | Reply with app, version, `protocol: 8`, `lessons`, `insets`, `autopilotModes`, the fleet's `loading` fits, `display`, `controlsLayout`, `conditions`, `features`, `controlInputs`, `controlsDisplay`, `fleet` (each with its `controls` fit), `commands`, `scenarios`, `views`, `viewFrom`, `presets`, `show` keys, frame size, stream names, ports, and `review: {historySeconds: 60, fps: 30, commands: [step, rewind, seek, play]}`. |
| `show` | any of `vectors lift drag weight thrust wind total axis wheels labels readout strips review controlsDisplay controlTraces horizon wingWind tailWind inertial tail moments`: bool | Toggle vectors and text. `strips` = every strip's lift and drag; `review` = the "REVIEW -2.4 s" tag in the readout; `controlsDisplay` = the NTSB-style control panel; `controlTraces` = its 10 s time-history strips; `horizon` = the horizon line and the ground grid (cruise and spin). |
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

### NTSB display (protocol 6) — the default

A live reconstruction in the style of the NTSB's accident animations (Colgan 3407), keyed over the show on a **transparent** background. There's no clock and no CVR box.

**Layout:**
- **Top (55 %):** the airplane in the current view, with the ground references. The vectors are off unless `show {"vectors":true}`.
- **Header (top-left):** the aircraft's name.
- **Bottom (45 %, `split`):** a row of instrument **plates**:
  - **PFD:**
    - attitude with pitch ladder and bank scale, its sky and ground only inside the face;
    - airspeed tape, with the stall speed for the current flaps as a red band and the speed boxed;
    - altitude tape with a ±2,000 fpm VS pointer;
    - heading, pitch / bank and G.
  - **AoA gauge:** the type's critical α in red, the warning α in amber.
  - **Controls:** the column from the side (PUSH / PULL %), the wheel from the front, the rudder pedals.
  - **Power:** a lever per engine, %, RPM.
  - **Flaps · gear · spoilers:** dimmed N/A / FIXED where the type lacks them.
  - **Annunciators:**
    - STALL, or STICK SHAKER on the 737, when α ≥ the warning α;
    - SPIN when autorotating (|yaw| > 40°/s past the critical α);
    - HOLD, REVIEW −x.x s, and PF <name>.

**Transparency:**
- Nothing fills the frame. Each plate is black at 55 % with a thin grey rule, and the show shows through the gaps.
- All text has a dark outline.
- The line shader's alpha blend is "over", so a 55 % plate stays 55 %: before, a 55 % plate was stored at 30 %.
- Checked over a bright colour-bar test card: the card shows everywhere except the airplane, the lines, the plates and the text.

**Values per type:**
- **Critical α:** the angle of peak lift, swept on the aero model for the current flaps.
- **Stall speed:** the sim's own estimate (EstimateVso) for those flaps.

**Review:** every value on the panel shows the frame being reviewed.

**Final, wide view:** when the side view is so wide the airplane would be a few pixels (FINAL far out), it is drawn larger than life and labelled `AIRPLANE ×N`. Physics, vectors and the camera are unchanged, and it returns to true scale as the frame tightens.

#### The display is also the controller (protocol 6 addendum)

In ntsb mode, `hello` and `state` carry `controlsLayout`: each control instrument's hit area, as 0…1 of the frame with a top-left origin. Glass Overlay lays its own invisible touch controls exactly there and sends normal `controls`.

```json
"controlsLayout":{
 "yoke":{"rect":[0.5111,0.5833,0.1741,0.1333],"axes":"elevator+aileron","elevatorUp":"push","aileronRight":"right"},
 "pedals":{"rect":[0.5111,0.7593,0.1741,0.0926],"axis":"rudder","orientation":"horizontal","right":"right"},
 "throttle":{"rect":[0.7333,0.6074,0.0481,0.2593],"axis":"throttle","orientation":"vertical","top":"full","engines":1},
 "flaps":{"rect":[…],"axis":"flaps","orientation":"vertical","top":"up","detents":[0,0.333,0.667,1],"detentValues":[0,0.333,0.667,1]},
 "spoilers":{"rect":[…],"axis":"spoilers","orientation":"vertical","top":"retracted"}}
```

- **Yoke** (owner 2026-10-09):
  - **The wheel:** one wheel shows both axes, over a fixed black shadow of itself at neutral. Aileron rotates it (90° at full).
  - **The column:** it pivots at the floor, so the wheel drops either way from neutral. Pulled aft it comes toward the pilot, lower and bigger; pushed forward it goes away, lower and smaller.
  - **Gauges:** a thin elevator gauge beside the box (up = push) and a thin aileron gauge under the wheel give direct readings.
  - **Touch:** the whole box is the touch control. It spans ±1 aileron (left → right) and ±1 elevator (top = push).
- **Pedals:** ±1 rudder.
- **Levers:** each rect is exactly the lever's travel. The flaps' `detents` are the detent positions along it (top = 0); `detentValues` are the handle values to send for each.
- **Visible feedback:**
  - The yoke and pedals have a faint dashed box with a thumb at the current input.
  - The levers have their handles.
  - All of them track the current input, including HOLD, the autopilot and review frames.
- **When it's reported:** classic mode has no `controlsLayout`, and a `split` change moves the rects.

### Forces and moments (protocol 7)

Defaults on: `wingWind`, `tailWind`, `inertial`, `total`, `tail`, `moments`, `thrust`, `wheels`, `labels`. Defaults off: `weight`, `wind` (single), `lift`, `drag`, `strips`, `axis`. In ntsb mode all of them also need `vectors:true`.

| Key | What |
|---|---|
| `wingWind` | The local relative wind at **mid-span of each wing**, including ω × r, so in a spin the wings differ. Labelled `L WING α 31°`, turning red with `STALLED` past the critical α. |
| `tailWind` | The flow at mid-span of **each stab half**, as the aero model computes it: downwash ε, slipstream, swirl, rotation. Its **length is the effective local speed √η·V**. Labelled `L STAB α 6° · ε 3°`. |
| `inertial` | **m(g − a) = −(every non-gravity force)** from the CG, labelled `INERTIAL 2.3 g`. Replaces `weight`: in 1-g flight it equals weight; in a turn it tilts out and grows. |
| `total` | The sum of **all aerodynamic forces**, from the **neutral point** (dM/dL of the aero model), labelled `TOTAL AERO 2.3 g`. In a steady power-off state it equals INERTIAL and points the opposite way. With power, aero + thrust does. |
| `tail` | The horizontal tail's force at the tail, `TAIL ↓ 85 lb` / `TAIL ↑ …`. |
| `moments` | Two arcs in the pitch plane, **centred on the CG** (owner 2026-10-09), on a radius that clears the airframe, on one scale. **AERO** (ahead of the nose) is the aerodynamic pitching moment about the CG. **INERTIA** (behind the tail) is the inertia-coupling term −(ω × Iω), which pitches a spin nose-up. The sum of the two is I·q̇: they balance in a steady spin, and the aero arc wins when forward stick breaks it. |

**Tail physics (the sim):**
- **Downwash:** ε = 0.4·α_wing while the wing is attached, collapsing as it stalls, with the flaps' extra.
- **Slipstream:** V_slip = √(V² + 2T/(ρA)), over the stab inside a 0.7 R tube.
- **Swirl** (new, 2026-10-09): tangential speed 0.2 × the axial increase, solid-body across the tube. A right-hand prop carries the air **up** past the left stab half and **down** past the right, so with power the two halves' α split. It's applied to the tail force and moment too.
- **Baseline:** η_t = 0.9 (the fuselage / wing wake) when there's no prop blast. Gliders have no blast, and jets no prop.

**Measured:**

| Condition | Result |
|---|---|
| C172 at 64 KIAS, flaps 30, idle | tail 61 kt vs freestream 64 (η 0.90) |
| Same, full power | tail 97 kt vs freestream 68 (η 1.78); stab α L −0.5°, R −6.9° (swirl) |
| Glider at full throttle | no change |
| C172 cruise | INERTIAL 1.00 g, AERO 1.01 g, tail ↓ 99 lb, η 1.04 |
| 60° level turn | INERTIAL 1.90 g = AERO 1.90 g = nz |

**Drawing:**
- **Arrows:** ≈ 6 px (`vectorStyle width`) with a dark outline and bigger heads, drawn in the end-of-frame compositor.
- **Smoothing:** every vector is low-pass filtered in the **airplane's axes** (a steady spin is steady there, so nothing lags the airframe), τ = `smoothingMs`. Review replays the filter over the frames leading up to the reviewed one.

**Labels:**
- **Fixed slots:** 8 directions × 2 rows on an ellipse around the airplane, each joined to its arrow by a thin leader line.
- **Changing slot:** a label keeps its slot until its arrow swings > 45° past it (and at most once every 1.5 s), then fades in at the new slot without sliding. A slot whose label would collide with one already placed is skipped.
- **Numbers:** update at most 4×/s, with hysteresis. Positions are pixel-snapped.
- **Measured** (Pitts spin, 20 s): median 0 px per frame, 95th percentile ≈ 1.5 px per frame, 14 slot changes.

**State:**

```json
"forces":{"inertialG","aeroG","tailLb" (+ = UP),"momentAero","momentInertia" (ft·lb, nose-up +),"tailAlphaL","tailAlphaR","downwashDeg",
 "tailEta","tailEtaL","tailEtaR","tailSpeedL","tailSpeedR","freestreamKt" (kt),"wingAlphaL","wingAlphaR"}
```

**Known model issue: spins with held pro-spin controls.**
- **The problem:** the spins aren't sustained or aren't in the rudder's direction. C172: about 1 turn, then it stops. Pitts: settles into the OPPOSITE direction (+55°/s right with left rudder). Extra: pulses on and off. Decathlon: a slow spiral.
- **Consequence:** the spin condition's first seconds are a real spin, but it doesn't stay developed.
- **Plan:** taken up in protocol 8 (advanced spin physics).

### Protocol 8 — weight & balance, autopilot, insets, lessons

**Weight & balance.**
- **CG:** given in % of the wing's mean aerodynamic chord (MAC). The MAC comes from the wing strips; the neutral point from the aero model's own totals, x_np = x_cg + dM/dL. (Summing the overlay's samples missed the fuselage's destabilising moment, which is now sampled too.)
- **Weight:** scales the mass and the inertia. A CG shift moves Config.Mass.Cg, which every force and moment is taken about, and adds the parallel-axis term. The drawn airplane is offset by the shift so it stays on the physics.
- **Limits:** the configs carry no POH envelope, so these are approximations: gross = the config's weight, empty ≈ 62 %, CG ±9 % MAC around the default, with the aft limit kept 5 % MAC ahead of the NP.
- **Per type in hello's fleet:** `loading: {emptyLb, maxGrossLb, defaultLb, cgLimitsMac:[fwd,aft], defaultCgMac, neutralPointMac, macM}`.
- **State:** `loading: {grossWeightLb, cgPercentMac, staticMarginMac, withinLimits, overweight, cgOut, targetLb, targetCgMac}`. The display shows OUT OF LIMITS in amber.
- **Kept across starts:** start conditions keep the current loading unless `loading` is passed with the start. A new type takes its own default.
- **Sim-level checks** (tools/FlightTests/StickAndRudderTests):

  | Check | Result |
  |---|---|
  | +20 % weight | stall speed ×1.086 (√1.2 = 1.095); best glide 12.66:1 → 12.66:1 at 82 → 90 kt |
  | Forward vs aft CG (C172, ±0.12 m) | elevator −1.7° vs +1.6°; tail 446 N down vs 83 N up |
  | Static stability | every type stable at its default CG; the Gee Bee nearly neutral |
  | C172 neutral point | 48 % MAC (static margin 27 % at the default CG) |

**Autopilot** (owner: like a real one).
- **Modes:**
  - PITCH: ALT, VS (it captures a target altitude: VS → ALT* → ALT), or FLC (level change: pitch for the airspeed, with climb power toward a higher altitude and idle toward a lower one).
  - ROLL: ROL (wings level) or HDG (the heading selector: set, ±1/±10, SYNC; ≤ 20° bank).
  - YD: yaw damper / auto-coordination, which can be on without the AP.
  - A/T: autothrottle for the airspeed.
- **Not yet active:** LNAV, LOC and APP exist as buttons and modes but are not active (a later build).
- **Servos and trim:** an elevator servo of about half the stick travel per second, and a pitch-trim servo that offloads it. The AP flies through the ordinary controls, so the panel shows its inputs.
- **Annunciation:** a flight-mode annunciator, e.g. `AP · ALT 3000 · HDG 270 · YD · A/T 100`.
- **When it can't hold:** it says why — `CAN'T HOLD — FULL POWER` / `ELEVATOR/TRIM LIMIT` / `STALL`, or `OSCILLATING` — and keeps flying speed over altitude.
- **Disconnects:** pilot stick input disconnects it (AP DISC for 2 s); pilot rudder input turns off the YD. Loading changes never disconnect it.
- **State:** `autopilot: {on, pitchMode, rollMode, altitudeFt, vsFpm, kias, headingBug, yawDamper, autothrottle, elevator, trim, power, status, disc, fma, lnav/loc/app: "unavailable"}`.
- **Acceptance** (C172, 3,000 ft, 100 KIAS):

  | Case | Altitude | Speed | Power | Trim | α | Tail | Status |
  |---|---|---|---|---|---|---|---|
  | baseline | −8…−3 ft | 98.7–101.2 | 0.58 | +0.06 | 1.1° | −53 lb | holding |
  | +20 % weight | −8…−3 | 97.5–101.6 | 0.70 | +0.04 | 2.1° | −42 lb | holding |
  | CG forward limit | −8…−3 | 98.5–101.1 | 0.59 | −0.05 | 1.3° | −109 lb | holding |
  | CG aft limit | −8…−3 | 98.7–101.2 | 0.58 | +0.11 | 1.0° | +5 lb | holding |
  | CG 4 % behind the NP | −98…−45 | 89–96 | 1.00 | | | | can't hold (fails) |
  | double weight | −27…−13 | 92 | 1.00 | | 7.8° | | can't hold — full power |

**Insets:**
- **CL–α:** a sweep of the aero model, with the critical α in red.
- **L/D–α:** a sweep of the aero model.
- **Power required vs KIAS:** from the glide trim's L/D at each speed, with the available power line. Cached per type, flaps and weight.
- **Slip ball:** from the lateral specific force.
- **AoA:** a bar gauge.
- **W&B envelope:** weight vs CG, with the limits box, the NP line and a live dot.

**State adds:** `lesson`, `lessonPhase`, `lessonResults`, `beta`, `ball`, `loadFactor`, `flightPathDeg`, `cgPercentMac`, `wind`, `insets`, `show` (every switch).

**Remote:**
- An autopilot panel with ALT, VS ▲▼, FLC, SPD ±, ALT ±, HDG with the bug selector, YD and A/T. LNAV / LOC / APP are greyed out.
- A lesson picker.
- Weight and CG sliders.
- A labelled on/off **chip for every vector, display element and inset** (owner: "radio buttons for all the vectors and displays").

**Relative wind (fixed 2026-10-09):** the wing, tail and single relative-wind arrows show the air arriving from ahead, −(the station's velocity through the air). Before, they showed the flight path.

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

### Network: IPv4 + IPv6, Bonjour (2026-10-09)

- **Dual-stack:** both ports (TCP 47830, HTTP 47831) listen on IPv6 in dual-stack mode and accept IPv4 too. A device that reaches the Mac only by an IPv6 (link-local) address connects directly. HTTP is a small built-in HTTP/1.1 server (Mono's HttpListener rejected IPv6 Host headers).
- **Bonjour** (registered through the system's mDNS responder while the widget runs):
  - `_aerowidget._tcp` "Aero Widget" on 47830, TXT `control=47830 http=47831 protocol=<n>`;
  - `_http._tcp` "Aero Widget remote" on 47831, `path=/`.
- **hello** carries `network: {tcpDualStack, httpIPv6, bonjour: [...]}`.

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
