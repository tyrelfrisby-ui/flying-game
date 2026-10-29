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
| Needle_ASI_F | Instruments_Front | airspeed_mph | rot | [-1.0, 0.0, -0.0] | [-1.744, -0.1, -0.125] | 0→0 20→15 160→345 | ASI 20..160 mph over 15..345 deg (photographed dial layout; AFM arcs: green 38-65, yellow 65-98, red 98), rests at 0 below 20; +deg clockwise from the seat |
| Needle_Alt100_F | Instruments_Front | altitude_ft | rot | [-1.0, 0.0, -0.0] | [-1.744, -0.136, 0.228] | 0→0 1000→360 | altimeter long hand, 1 turn per 1000 ft (unbounded); +deg clockwise from the seat |
| Needle_Alt1000_F | Instruments_Front | altitude_ft | rot | [-1.0, 0.0, -0.0] | [-1.7465, -0.136, 0.228] | 0→0 10000→360 | altimeter short hand, 1 turn per 10000 ft (unbounded); +deg clockwise from the seat |
| Needle_Vario_F | Instruments_Front | vario_kt | rot | [-1.0, 0.0, -0.0] | [-1.744, -0.153, -0.217] | -10→-150 0→0 10→150 | variometer, modelled at 0 (9 o'clock), +10 kt climb = 150 deg clockwise; +deg clockwise from the seat |
| Compass_Card | Instruments_Front | heading_deg | rot | [0.0, 1.0, -0.0] | [-1.74, -0.126, -0.0] | 0→0 360→360 | wet-compass card drum: turns opposite the airplane so the heading sits under the lubber line (modelled at 000; reverse-sensing card) |
| TorqueTube | Controls | aileron | rot | [-1.0, 0.0, -0.0] | [-1.07, -0.907, -0.0] | -1→-18 0→0 1→18 | single torque tube carrying both sticks: +1 = stick tops to the right (roll right), 18 deg (judgement) |
| Stick_F | TorqueTube | elevator | rot | [0.0, 0.0, -1.0] | [-1.46, -0.907, -0.0] | -1→-15 0→0 1→18 | stick pitch on the torque tube: +1 aft 18, -1 forward 15 (judgement) |
| Stick_R | TorqueTube | elevator | rot | [0.0, 0.0, -1.0] | [-0.7, -0.907, -0.0] | -1→-15 0→0 1→18 | stick pitch on the torque tube: +1 aft 18, -1 forward 15 (judgement) |
| Pedal_F_L | Controls | rudder | move | [1.0, 0.0, -0.0] | [-1.9, -0.853, 0.125] | -1→-0.06 0→0 1→0.06 | left pedal slides aft 60 mm at +1 (right rudder) (judgement) |
| Pedal_F_R | Controls | rudder | move | [-1.0, 0.0, -0.0] | [-1.9, -0.853, -0.125] | -1→-0.06 0→0 1→0.06 | right pedal slides forward 60 mm at +1 (right rudder) (judgement) |
| Pedal_R_L | Controls | rudder | move | [1.0, 0.0, -0.0] | [-1.015, -0.853, 0.255] | -1→-0.06 0→0 1→0.06 | left pedal slides aft 60 mm at +1 (right rudder) (judgement) |
| Pedal_R_R | Controls | rudder | move | [-1.0, 0.0, -0.0] | [-1.015, -0.853, -0.255] | -1→-0.06 0→0 1→0.06 | right pedal slides forward 60 mm at +1 (right rudder) (judgement) |
| TowRelease_F | Controls | tow_release | move | [1.0, 0.0, -0.0] | [-1.752, -0.19, -0.0] | 0→0 1→0.05 | tow release: pull the knob full out (50 mm aft) to release (AFM); yellow per Ty, AFM says red |
| TowRelease_R | Controls | tow_release | move | [1.0, 0.0, -0.0] | [-0.815, -0.125, 0.2734] | 0→0 1→0.05 | tow release: pull the knob full out (50 mm aft) to release (AFM); yellow per Ty, AFM says red |
| DiveBrake_Lever_F | Controls | spoiler | rot | [0.0, 0.0, -1.0] | [-1.5368, -0.5418, 0.334] | 0→0 1→33 | AIR BRAKE handle in the left-wall slot: modelled stowed at the forward RETRACT end, swings aft 33 deg to EXTEND (wall placard + AFM: pull aft to extend) |
| DiveBrake_Lever_R | Controls | spoiler | rot | [0.0, 0.0, -1.0] | [-0.6368, -0.6318, 0.3472] | 0→0 1→33 | AIR BRAKE handle in the left-wall slot: modelled stowed at the forward RETRACT end, swings aft 33 deg to EXTEND (wall placard + AFM: pull aft to extend) |
| Trim_Lever | Controls | trim | rot | [0.0, 0.0, -1.0] | [-1.4, -0.62, 0.3127] | -1→-20 0→0 1→20 | red-ball trim lever low on the left wall, 4-notch quadrant: aft = nose up (judgement 20 deg each way) |

## v8 changes (photo-literal interior)
- 26 nodes (was 30). REMOVED: Needle_ASI_R, Needle_Alt100_R, Needle_Alt1000_R, Needle_Vario_R and the Instruments_Rear group -- the reference photos show no rear panel.
- Needle_ASI_F map is now 20→15, 160→345 deg (40-160 mph dial with arcs); front-panel pivots moved (altimeter left, ASI + VSI right, compass under the eyeball vent).
- DiveBrake_Lever_F/R: 0→33 deg (was 60), pivots on the left wall slot panels (EXTEND aft / RETRACT forward, modelled at RETRACT).
- Trim_Lever: red ball lever low and forward on the left wall; TowRelease_R now at the left end of the white cross tube behind the front seat.
- All other pivots shifted slightly with the rebuilt floor/seats; re-read pivot_gltf from this table.

## Params
- `aileron` -1..+1 (+1 roll right), `elevator` -1..+1 (+1 stick aft), `rudder` -1..+1 (+1 right pedal), `spoiler` 0..1 (dive brakes; also drives both DiveBrake_Lever, 0→33 deg aft = EXTEND per the wall placard), `trim` -1..+1 (+1 aft/nose up), `tow_release` 0..1 (both knobs pull 50 mm aft).
- Instruments: `airspeed_mph`, `altitude_ft` (unbounded, wrap), `vario_kt` (±10), `heading_deg` (card drum; at 0 N sits under the lubber line, card reads like a real wet compass: higher headings appear to the LEFT).
- Suggested coupling: wheel brake from spoiler over [0.9, 1.0] (the 2-33 wheel brake is applied at the end of dive-brake handle travel).
