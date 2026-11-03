# J-3 Cub own model: animation rig spec (v17)

This is for Claude/Aero, for the aircraft configs `j3-cub-like` (wheels) and `j3-cub-floats-like` (floats). Both GLBs carry the same cockpit rig, and every value below is also stored on its node as glTF `extras`, so a config loader can read the rig straight from the file.

## Frame and conventions
- The GLB is in the glTF frame: **+x aft, +y up, +z port**. The nose points to −x, and the origin is the config datum, the same as the v1 to v14 handoff.
- All pivots and axes below are in that glTF frame, in metres. A pivot is the node's world origin at the rest pose.
- Every node rotates or translates about **its own origin**. Its rest transform has identity rotation, with the parent empties in between.
- `rig_map_param` / `rig_map_deg` (or `rig_map_m` for translations) is a piecewise-linear table from the control parameter to the angle or offset, measured **from the modelled pose**.
  - Clamp at the table ends, unless `rig_map_extrapolate` = 1. That applies only to the altimeter hands, which are unbounded.
  - `rig_map_wrap` = 360 means take the parameter mod 360 (compass).
  - The angle is right-handed about `rig_axis_gltf`: `q = AxisAngle(rig_axis_gltf, deg) * q_rest`.
- `rig_param_model` is the parameter value that the mesh is modelled at. The map is zero there.
- Nodes that share a `rig_param` are mechanically linked and must be driven together:
  - both throttles;
  - both sticks;
  - all four pedals with the rudder, the tailwheel and the water rudder;
  - trim crank and stabilizer.
- v16 (Ty 2026-11-02): the carb heat lever is the ONLY carb heat control. The rear-seat knob and its push-pull rod
  are gone (`CarbHeat_Knob_Rear` no longer exists); from the rear seat it is a lean forward.
- v17 (Ty 2026-11-02 19:00): the mag switch moved aft to the front seat back's top; the panel order is now oil /
  tach / compass / altimeter / ASI left to right (Ty's photo), so the tach, ASI and oil needles' pivots moved with
  their faces. New static (unrigged) nodes `Fuel_Tank` and `Fuel_Drain_Hose`; still 26 of 26 rigged nodes driven.
- The axis signs were asserted by the build script against probe points on the meshes. The build fails if a sign is wrong.
- Reference implementation: `tools/own_models/j3_fourview.py` with `J3_POSE="param=value,..."`.
- **In the game** (2026-11-02): the table the game reads is `docs/models/j3-cub-own-rig.json` (wheels) and
  `j3-cub-floats-own-rig.json` (floats), written FROM these extras by `tools/own_models/j3_rig_json.py`. Re-run it
  after a rebuild (`--check` fails when a table is out of step with its GLB). It renames `engine_rpm` -> `rpm` and
  `oil_psi` -> `oil_press_psi`, re-orders the magneto map for the sim's 0 OFF / 1 R / 2 L / 3 BOTH, and leaves out
  carb heat, primer, fuel valve, fuel wire, doors and water-rudder retract (no such sim value yet: drawn as modelled).
  So v16's renames (`FuelValve_Lever` -> `FuelValve_Knob`, `CarbHeat_Knob_Rear` removed) change only the tables'
  `static` lists: the game still drives 26 of 26 rigged nodes on each variant.

## Hierarchy (cockpit)
```
Cockpit (empty)
├── Instruments (empty, at the panel)
│   ├── Needle_Tach, Needle_Airspeed, Needle_Alt_100ft, Needle_Alt_1000ft, Needle_OilTemp, Needle_OilPress
│   ├── Compass_Card
│   └── Primer_Plunger
├── Controls (empty)
│   ├── Stick_Front_Roll (empty, roll) ── Stick_Front (pitch)
│   ├── Stick_Rear_Roll  (empty, roll) ── Stick_Rear  (pitch)
│   ├── Pedal_Front_L, Pedal_Front_R, Pedal_Rear_L, Pedal_Rear_R
│   ├── Throttle_Lever_Front, Throttle_Lever_Rear
│   ├── FuelValve_Knob, Trim_Crank, CarbHeat_Lever
└── MagSwitch_Knob
```
- The static plates, labels, gauge faces, bezels, primer barrel and lubber line stay in `Cabin` and `Panel_Gauges`.
- The heel brakes are static. There is no brake rig.

## Control parameters
| param | range | meaning |
|---|---|---|
| aileron | −1..+1 | +1 = stick right (roll right) |
| elevator | −1..+1 | +1 = stick aft (nose up) |
| rudder | −1..+1 | +1 = right pedal |
| trim | −1..+1 | +1 = full nose-up trim |
| throttle | 0..1 | 0 = CLOSED, 1 = OPEN |
| fuel_valve | 0/1 | 1 = ON (pushed in, modelled), 0 = OFF (pulled out 30 mm) |
| carb_heat | 0..1 | 0 = OFF (modelled), 1 = ON |
| magneto | 0,1,2,3 | integer detents OFF, L, R, BOTH (modelled OFF) |
| primer | 0..1 | 0 = locked in, 1 = pulled out 50 mm |
| engine_rpm | rpm | tach |
| airspeed_mph | mph | ASI (the dial is in mph) |
| altitude_ft | ft | altimeter (both hands) |
| oil_temp_f / oil_psi | °F / psi | oil gauge (two needles) |
| heading_deg | deg magnetic | compass card |

## Cockpit and instrument nodes
| node | parent | param | type | axis (glTF) | pivot (glTF) | map (param→deg or m) | notes |
|---|---|---|---|---|---|---|---|
| Needle_Tach | Instruments | engine_rpm | rotate | [-1.0, 0.0, -0.0] | [-0.58, 0.233, 0.075] | 0→0, 500→5, 3500→275 deg | tach x100 rpm dial 500..3500 over 270 deg (pegged below 500); +deg clockwise from the seat |
| Needle_Airspeed | Instruments | airspeed_mph | rotate | [-1.0, 0.0, -0.0] | [-0.58, 0.233, -0.181] | 0→0, 40→12, 140→312, 150→327 deg | ASI 40..140 mph over 300 deg, rests on its pin below 40; +deg clockwise from the seat |
| Needle_Alt_100ft | Instruments | altitude_ft | rotate | [-1.0, 0.0, -0.0] | [-0.5792, 0.233, -0.095] | 0→0, 1000→360 deg | altimeter long hand, 1 turn per 1000 ft (unbounded); +deg clockwise from the seat; rig_map_extrapolate=1 |
| Needle_Alt_1000ft | Instruments | altitude_ft | rotate | [-1.0, 0.0, -0.0] | [-0.58, 0.233, -0.095] | 0→0, 10000→360 deg | altimeter short hand, 1 turn per 10000 ft (unbounded); +deg clockwise from the seat; rig_map_extrapolate=1 |
| Needle_OilTemp | Instruments | oil_temp_f | rotate | [-1.0, 0.0, -0.0] | [-0.58, 0.238, 0.164] | 100→0, 250→120 deg | oil temperature 100..250 F over 120 deg (upper dial); +deg clockwise from the seat |
| Needle_OilPress | Instruments | oil_psi | rotate | [-1.0, 0.0, -0.0] | [-0.58, 0.2082, 0.164] | 0→0, 80→120 deg | oil pressure 0..80 psi over 120 deg (lower dial); +deg clockwise from the seat |
| Compass_Card | Instruments | heading_deg | rotate | [0.0, 1.0, -0.0] | [-0.579, 0.233, -0.011] | 0→0, 360→360 deg | wet-compass card drum: turns opposite the airplane so the heading sits under the lubber line (modelled at 000; reverse-sensing numbers); rig_map_wrap=360.0 |
| Throttle_Lever_Front | Controls | throttle | rotate | [-0.0, -0.0, 1.0] | [-0.482, 0.065, 0.3056] | 0→0, 1→41.3322 deg | ball swings forward along the plate top edge CLOSED (aft) -> OPEN (fwd) about the plate tip |
| Throttle_Lever_Rear | Controls | throttle | rotate | [-0.0, -0.0, 1.0] | [0.388, 0.065, 0.3075] | 0→0, 1→41.3322 deg | ball swings forward along the plate top edge CLOSED (aft) -> OPEN (fwd) about the plate tip |
| FuelValve_Knob | Controls | fuel_valve | translate | [0.0, 0.0, -1.0] | [-0.323, 0.035, 0.3074] | 0→0.03, 1→0 m | v16 push-pull: red knob on a chrome shaft through the D plate on the left wall (front pilot's left knee); modelled ON (pushed in); OFF pulls it 30 mm straight out of the wall into the cabin |
| Trim_Crank | Controls | trim | rotate | [0.0, 0.0, -1.0] | [0.048, -0.087, 0.3071] | -1→-450, 0→0, 1→450 deg | trim crank, 1.25 turns each way from neutral (judgement); +nose-up turns the top of the crank aft (toward UP). v16: plate, scale and crank 0.30 m aft of v15 (beside the front seat) |
| Stab | None | trim | rotate | [0.0, 0.0, -1.0] | [4.373, 0.485, 0.0] | -1→2.5, 0→0, 1→-4 deg | stabilizer incidence: +1 nose-up trim = LE down 4 deg, -1 = LE up 2.5 deg (TCDS A-691) |
| CarbHeat_Lever | Controls | carb_heat | rotate | [0.0, -0.0, -1.0] | [-0.358, -0.064, -0.307] | 0→0, 1→35 deg | carb heat lever about its fork pivot; ON swings the forward ball up 35 deg (judgement). v16: the only carb heat control |
| MagSwitch_Knob | Cockpit | magneto | rotate | [0.34202, 0.0, -0.93969] | [0.313, 0.7122, 0.281] | 0→0, 1→-37, 2→-73, 3→-115 deg | v17: rotary mag switch on the LEFT root-rib wall (the vertical face above the left window), ABEAM THE TOP OF THE FRONT SEAT BACK (station 2.130; v16 was 1.743), plate on a wedge facing inboard and 20° aft toward the rear seat; detents at the plate labels OFF/L/R/BOTH (clockwise seen from the cabin); rig_detents=['OFF', 'L', 'R', 'BOTH'] |
| Primer_Plunger | Instruments | primer | translate | [1.0, 0.0, -0.0] | [-0.57, 0.164, -0.24] | 0→0, 1→0.05 m | primer plunger pulls straight aft 50 mm (judgement) |
| Stick_Front_Roll | Controls | aileron | rotate | [-1.0, 0.0, -0.0] | [-0.267, -0.4576, -0.0] | -1→-20, 0→0, 1→20 deg | stick roll on the torque tube: +1 = stick top to the right (roll right) |
| Stick_Front | Stick_Front_Roll | elevator | rotate | [0.0, 0.0, -1.0] | [-0.267, -0.4576, -0.0] | -1→-15, 0→0, 1→20 deg | stick pitch: +1 = stick aft 20 deg, -1 = forward 15 deg (judgement) |
| Stick_Rear_Roll | Controls | aileron | rotate | [-1.0, 0.0, -0.0] | [0.483, -0.4077, 0.0] | -1→-20, 0→0, 1→20 deg | stick roll on the torque tube: +1 = stick top to the right (roll right) |
| Stick_Rear | Stick_Rear_Roll | elevator | rotate | [0.0, 0.0, -1.0] | [0.483, -0.4077, 0.0] | -1→-15, 0→0, 1→20 deg | stick pitch: +1 = stick aft 20 deg, -1 = forward 15 deg (judgement) |
| Pedal_Front_L | Controls | rudder | rotate | [0.0, 0.0, -1.0] | [-0.697, -0.395, 0.13] | -1→-18, 0→0, 1→18 deg | L pedal hinged at the floor: +1 (right rudder) moves its top aft |
| Pedal_Front_R | Controls | rudder | rotate | [-0.0, -0.0, 1.0] | [-0.697, -0.395, -0.13] | -1→-18, 0→0, 1→18 deg | R pedal hinged at the floor: +1 (right rudder) moves its top forward |
| Pedal_Rear_L | Controls | rudder | rotate | [0.0, 0.0, -1.0] | [0.033, -0.395, 0.13] | -1→-18, 0→0, 1→18 deg | L pedal hinged at the floor: +1 (right rudder) moves its top aft |
| Pedal_Rear_R | Controls | rudder | rotate | [-0.0, -0.0, 1.0] | [0.033, -0.395, -0.13] | -1→-18, 0→0, 1→18 deg | R pedal hinged at the floor: +1 (right rudder) moves its top forward |

## Outside surfaces (v7 rig, unchanged)
These nodes use the older extras: `rig_deg_pos` for param > 0 and `rig_deg_neg` for param < 0, both linear. The water-rudder retract and the doors use `(p − rig_param_model) * rig_deg_pos`.

| node | variant | param | axis (glTF) | pivot (glTF) | +1 | −1 |
|---|---|---|---|---|---|---|
| Aileron_L | both | aileron | [0.0, -0.02284, -0.99974] | [0.853, 0.7975, 3.465] | 18 | -18 |
| Elevator_L | both | elevator | [-0.0, -0.0, 1.0] | [4.373, 0.485, 0.7] | 34 | -29 |
| Aileron_R | both | aileron | [-0.0, -0.02284, 0.99974] | [0.853, 0.7975, -3.465] | 18 | -18 |
| Elevator_R | both | elevator | [-0.0, -0.0, 1.0] | [4.373, 0.485, -0.7] | 34 | -29 |
| Rudder | both | rudder | [0.0, 1.0, -0.0] | [4.438, 0.835, 0.0] | 30 | -30 |
| TailWheel_Steer | wheels | rudder | [0.0, 1.0, -0.0] | [4.638, 0.095, 0.0] | 30 | -30 |
| WaterRudder_L | floats | rudder | [0.0, 1.0, -0.0] | [2.3551, -0.9387, 0.9525] | 25 | -25 |
| WaterRudder_Blade_L | floats | water_rudder_up | [0.0, 0.0, 1.0] | [2.3551, -1.1584, 0.9525] | 90 | -0 |

- The stabilizer trim is in the cockpit table above (`Stab`, param `trim`). Elevator_L and Elevator_R are children of Stab, so trim and elevator add up.
- The prop spins about `spin_axis` at 6 deg/s per rpm, clockwise as seen from the cockpit.

## Travel limits, and where they come from
- **Surfaces:** aileron ±18°, elevator +34° up / −29° down, rudder ±30° (from the v7 rig, TCDS A-691). Stabilizer LE +2.5° up / −4° down (TCDS A-691).
- **Stick travel at the grip** (by eye from the Cub cockpit geometry; the TCDS gives surface travel, not stick travel):
  - roll ±20°;
  - pitch aft 20°, forward 15°.
- **Pedals:** ±18° about the floor hinge (by eye).
- **Tailwheel steer:** ±30° with the rudder. **Water rudder:** ±25° with the rudder.
- **Throttle:** 41.3°. That is the arc that takes the ball from the aft (CLOSED) end of the plate to the forward (OPEN) end.
- **Fuel valve:** push-pull, 30 mm stroke (v16; was a 90° lever). **Carb heat:** lever 35° (v16: no rear knob). **Primer:** 50 mm pull. **Magneto:** detents at the plate label angles 0 / −37 / −73 / −115°.
- **Trim crank:** ±450° (1.25 turns each way). This is a judgement for a game-friendly feel. The real J-3 crank takes several turns stop to stop.
- **Gauges:**
  - ASI 40–140 mph over 300°, resting on its pin below 40;
  - tach 500–3500 rpm over 270°;
  - altimeter: long hand one turn per 1000 ft, short hand one turn per 10 000 ft;
  - oil temperature 100–250 °F over 120°; oil pressure 0–80 psi over 120°.
  - All are modelled at the rest reading (engine off, 0 ft, 000 heading).
- **Compass:** the card turns about the vertical axis, so the heading sits under the lubber line. At N, the 3 and 6 show to the left of the lubber line, as on a real wet compass.

