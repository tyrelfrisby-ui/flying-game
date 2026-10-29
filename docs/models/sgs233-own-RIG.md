# SGS 2-33 own model: rig (glider-2-33-like-own.glb / -own-tf.glb)

Generated from `docs/models/sgs233-own-rig.json` (written by `SGS_RIGJSON=<path> blender -b --python tools/own_models/build_sgs233.py`).

Conventions (same as the Extra): glTF frame (Y up). Every moving node has its origin on its hinge/pivot, so `kind: rot` = rotate the node's local rest pose by `angle_deg = interp(map, param)` about `axis_gltf` (unit vector, model space at rest); `kind: slide` = translate by `interp(map)` metres along `axis_gltf`. The 9 hinged flight surfaces (Aileron_L/R, Elevator_L/R, Rudder, Spoiler_L/R, Spoiler_Lower_L/R) also carry glTF extras `hinge_axis_marker` (= `<node>_HingeAxis`, a top-level empty at the far end of the hinge line, so axis = marker − node origin) and `hinge_axis_gltf`. Cockpit nodes (needles, compass card, torque tube, sticks, pedals, knobs, levers) have their origin on the pivot; their axis is in this table only. Child nodes (`parent`) are already parented in the GLB (Stick_F/R ride on TorqueTube).

| node | parent | param | kind | axis_gltf | pivot_gltf | map (param -> deg or m) | notes |
|---|---|---|---|---|---|---|---|
| Aileron_L |  | aileron | rot | [0.08891, -0.03442, -0.99545] | [0.5749, 0.5513, 4.09] | -1→-38 0→0 1→18 | L aileron: +1 (roll right) TE down 18, -1 TE up 38 (FAA AFM travel) |
| Elevator_L |  | elevator | rot | [0.0, 0.0, 1.0] | [4.91, 0.2562, 0.02] | -1→-23 0→0 1→25 | elevator: +1 (stick aft) TE up 25, -1 TE down 23 (FAA AFM travel, 1080 lb rigging) |
| Spoiler_L |  | spoiler | rot | [-0.0, 0.02618, 0.99966] | [0.1644, 0.618, 1.9] | 0→0 1→85 | upper dive-brake door hinged at its front edge, opens up 85 deg at 1 (FAA AFM travel 85 +-5) |
| Spoiler_Lower_L |  | spoiler | rot | [0.0, -0.02618, -0.99966] | [0.1644, 0.4518, 1.9] | 0→0 1→75 | lower dive-brake door hinged at its front edge, opens down 75 deg at 1 (FAA AFM travel 75 +-5) |
| Aileron_R |  | aileron | rot | [0.08891, -0.03442, 0.99545] | [0.5749, 0.5513, -4.09] | -1→-18 0→0 1→38 | R aileron: +1 (roll right) TE up 38, -1 TE down 18 (FAA AFM travel 38/18 +-2) |
| Elevator_R |  | elevator | rot | [-0.0, -0.0, 1.0] | [4.91, 0.2562, -0.02] | -1→-23 0→0 1→25 | elevator: +1 (stick aft) TE up 25, -1 TE down 23 (FAA AFM travel, 1080 lb rigging) |
| Spoiler_R |  | spoiler | rot | [-0.0, -0.02618, 0.99966] | [0.1644, 0.618, -1.9] | 0→0 1→85 | upper dive-brake door hinged at its front edge, opens up 85 deg at 1 (FAA AFM travel 85 +-5) |
| Spoiler_Lower_R |  | spoiler | rot | [0.0, 0.02618, -0.99966] | [0.1644, 0.4518, -1.9] | 0→0 1→75 | lower dive-brake door hinged at its front edge, opens down 75 deg at 1 (FAA AFM travel 75 +-5) |
| Rudder |  | rudder | rot | [0.13991, 0.99016, 0.0] | [4.4586, 0.2868, 0.0] | -1→-30 0→0 1→30 | rudder on the raked hinge: +1 (right pedal) TE to starboard 30 (FAA AFM travel 30 +-2) |
| Needle_ASI_F | Instruments_Front | airspeed_mph | rot | [-1.0, 0.0, -0.0] | [-1.625, -0.155, 0.135] | 0→0 20→10 140→310 150→335 | ASI 20..140 mph over 300 deg (AFM arcs: green 38-65, yellow 65-98, red 98), rests at 0 below 20; +deg clockwise from the seat |
| Needle_Alt100_F | Instruments_Front | altitude_ft | rot | [-1.0, 0.0, -0.0] | [-1.625, -0.155, -0.0] | 0→0 1000→360 | altimeter long hand, 1 turn per 1000 ft (unbounded); +deg clockwise from the seat |
| Needle_Alt1000_F | Instruments_Front | altitude_ft | rot | [-1.0, 0.0, -0.0] | [-1.6275, -0.155, -0.0] | 0→0 10000→360 | altimeter short hand, 1 turn per 10000 ft (unbounded); +deg clockwise from the seat |
| Needle_Vario_F | Instruments_Front | vario_kt | rot | [-1.0, 0.0, -0.0] | [-1.625, -0.155, -0.135] | -10→-150 0→0 10→150 | variometer, modelled at 0 (9 o'clock), +10 kt climb = 150 deg clockwise; +deg clockwise from the seat |
| Needle_ASI_R | Instruments_Rear | airspeed_mph | rot | [-1.0, 0.0, -0.0] | [-0.8045, -0.055, 0.075] | 0→0 20→10 140→310 150→335 | ASI 20..140 mph over 300 deg (AFM arcs: green 38-65, yellow 65-98, red 98), rests at 0 below 20; +deg clockwise from the seat |
| Needle_Alt100_R | Instruments_Rear | altitude_ft | rot | [-1.0, 0.0, -0.0] | [-0.8045, -0.055, -0.0] | 0→0 1000→360 | altimeter long hand, 1 turn per 1000 ft (unbounded); +deg clockwise from the seat |
| Needle_Alt1000_R | Instruments_Rear | altitude_ft | rot | [-1.0, 0.0, -0.0] | [-0.807, -0.055, -0.0] | 0→0 10000→360 | altimeter short hand, 1 turn per 10000 ft (unbounded); +deg clockwise from the seat |
| Needle_Vario_R | Instruments_Rear | vario_kt | rot | [-1.0, 0.0, -0.0] | [-0.8045, -0.055, -0.075] | -10→-150 0→0 10→150 | variometer, modelled at 0 (9 o'clock), +10 kt climb = 150 deg clockwise; +deg clockwise from the seat |
| Compass_Card | Instruments_Front | heading_deg | rot | [0.0, 1.0, -0.0] | [-1.675, 0.0, -0.0] | 0→0 360→360 | wet-compass card drum: turns opposite the airplane so the heading sits under the lubber line (modelled at 000; reverse-sensing card) |
| TorqueTube | Controls | aileron | rot | [-1.0, 0.0, -0.0] | [-1.07, -0.83, -0.0] | -1→-18 0→0 1→18 | single torque tube carrying both sticks: +1 = stick tops to the right (roll right), 18 deg (judgement) |
| Stick_F | TorqueTube | elevator | rot | [0.0, 0.0, -1.0] | [-1.42, -0.83, -0.0] | -1→-15 0→0 1→18 | stick pitch on the torque tube: +1 aft 18, -1 forward 15 (judgement) |
| Stick_R | TorqueTube | elevator | rot | [0.0, 0.0, -1.0] | [-0.7, -0.83, -0.0] | -1→-15 0→0 1→18 | stick pitch on the torque tube: +1 aft 18, -1 forward 15 (judgement) |
| Pedal_F_L | Controls | rudder | move | [1.0, 0.0, -0.0] | [-2.0, -0.845, 0.115] | -1→-0.06 0→0 1→0.06 | left pedal slides aft 60 mm at +1 (right rudder) (judgement) |
| Pedal_F_R | Controls | rudder | move | [-1.0, 0.0, -0.0] | [-2.0, -0.845, -0.115] | -1→-0.06 0→0 1→0.06 | right pedal slides forward 60 mm at +1 (right rudder) (judgement) |
| Pedal_R_L | Controls | rudder | move | [1.0, 0.0, -0.0] | [-1.015, -0.845, 0.28] | -1→-0.06 0→0 1→0.06 | left pedal slides aft 60 mm at +1 (right rudder) (judgement) |
| Pedal_R_R | Controls | rudder | move | [-1.0, 0.0, -0.0] | [-1.015, -0.845, -0.28] | -1→-0.06 0→0 1→0.06 | right pedal slides forward 60 mm at +1 (right rudder) (judgement) |
| TowRelease_F | Controls | tow_release | move | [1.0, 0.0, -0.0] | [-1.615, -0.29, -0.0] | 0→0 1→0.05 | tow release: pull the knob full out (50 mm aft) to release (AFM); yellow per Ty, AFM says red |
| TowRelease_R | Controls | tow_release | move | [1.0, 0.0, -0.0] | [-0.815, -0.16, 0.205] | 0→0 1→0.05 | tow release: pull the knob full out (50 mm aft) to release (AFM); yellow per Ty, AFM says red |
| DiveBrake_Lever_F | Controls | spoiler | rot | [0.0, 0.0, -1.0] | [-1.56, -0.56, 0.3037] | 0→0 1→60 | dive-brake handle swings aft 60 deg (judgement) as the doors open; unlock = push forward/down then pull straight back (AFM) |
| DiveBrake_Lever_R | Controls | spoiler | rot | [0.0, 0.0, -1.0] | [-0.8, -0.52, 0.309] | 0→0 1→60 | dive-brake handle swings aft 60 deg (judgement) as the doors open; unlock = push forward/down then pull straight back (AFM) |
| Trim_Lever | Controls | trim | rot | [0.0, 0.0, -1.0] | [-1.47, -0.845, 0.17] | -1→-20 0→0 1→20 | bungee trim lever in a 4-notch quadrant: aft = nose up (judgement 20 deg each way) |

## Params
- `aileron` -1..+1 (+1 roll right), `elevator` -1..+1 (+1 stick aft), `rudder` -1..+1 (+1 right pedal), `spoiler` 0..1 (dive brakes; also drives both DiveBrake_Lever), `trim` -1..+1 (+1 aft/nose up), `tow_release` 0..1 (both knobs pull 50 mm aft).
- Instruments: `airspeed_mph`, `altitude_ft` (unbounded, wrap), `vario_kt` (±10), `heading_deg` (card drum; at 0 N sits under the lubber line, card reads like a real wet compass: higher headings appear to the LEFT).
- Suggested coupling: wheel brake from spoiler over [0.9, 1.0] (the 2-33 wheel brake is applied at the end of dive-brake handle travel).
