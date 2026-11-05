# Integration (Claude, 2026-11-04, lane/claude-grob103)

- **Placement.** `AirframeModels.OwnSpecs` gives the Grob `atDatum: true`: the GLB goes in at scale 1, on the
  config's datum along the length and across. The generic path would have fitted the span to the config's outermost
  strip CENTRE (8.84 m, so 1.8 % small) and slid the model 75 mm forward to line up the outer wings, which would have
  put the wheels 75 mm off their stations. The model is still stood on the config's wheels.
- **Measured against the config** (`Grob103ModelTests`). Span 18.000 m. Wheel stations nose +1.4595, main −0.5397,
  tail −4.8768, all exact. **Vertical, NOT matched, recorded:** the model's tyre bottoms are 0.600 m below the datum
  (tail 0.260), and the config's contacts are 0.69 (tail 0.33). Standing it on the config's wheels drops the whole
  model **90 mm**, and the drawn tail wheel then sits 0.35 m below the datum against the config's 0.33. Neither side
  was moved to hide this. One of them is wrong, and nothing published says which.
- **Eyes.** The approved native eyes (front (−1.15, .16, 0), rear (.04, .22, 0), Ty 2026-11-03) are put where the
  placed model draws them. That is the same 90 mm drop, so the config's seat eyes are front (1.15, 0, −0.07) and rear
  (−0.04, 0, −0.13), body m, +z down. They are recorded as v6 in `eyepoints.json` and asserted in `CockpitEyeTests`.
- **Nose steering.** As delivered, `Nose_Steering` ran off `rudder` with the Rudder's own sign about the same +Y axis.
  The rudder's trailing edge is AFT of its hinge and the wheel's front is AHEAD of its pivot, so right pedal would
  have turned the drawn wheel LEFT. It now reads `nose_wheel_steer_deg` (`RigInputs.NoseWheelSteerDeg`, + = right).
  That is the sim's own steering angle: rudder deflection / max × the config's ±25° lock, the product
  `LandingGear.SteerRad` steers the tyre by. The map is [[−90, 90], [0, 0], [90, −90]]: one for one, negated. The GLB
  extras still carry the old `rudder` map. The runtime reads this JSON, so this is only a note for the next export.

# Current rig: Blanc Twin III Acro v2, 18 m (2026-11-04)

Ty approved the Blanc conversion and eyes on 2026-11-03. The approved native
glTF eye transforms remain exactly front (-1.15, .16, 0), rear (.04, .22, 0).
The historical approval requests and pivot tables below are superseded by this
section and the generated JSON. Visual acceptance of **v2** remains PENDING.

Both exported assets measure 18.000046 m span and 17.500000 m² planform under
the section integration method (the central 0–.45 m per side uses the .45 m
chord). V1 already measured 18.000045 m span, but 17.232331 m² area. V2 retains
span and scales wing chord by 1.0155329673, retaining the source taper.
Ailerons now occupy author |Y|=5.17–8.68 m; upper-surface-only Type GROB
spoiler panels occupy 2.43–3.91 m. Their origins/axis markers follow the cuts.
Tip skids are centred at author Y=±8.9 m on the lower wing skin.

All 29 previous driven names, maps, parents, descriptions and extra fields are
retained; the four wing pivots and their axis witnesses follow the geometry.
The 30th node is `Nose_Steering`: scene-root, `rudder`, `rot`, native glTF axis
(0,1,0), pivot (-1.4595,-.43,0), map [-1,-25], [0,0], [1,25] degrees.
**±25° is ESTIMATED: no published travel found.** `rig_estimated=true` appears
in its GLB extras and JSON. One child `Nose_Steering_Assembly` carries the fork,
tyre and hub; the fixed fillet stays in the skin. An independent
`Nose_Steering_HingeAxis` witnesses the vertical fork steering axis.

Level-frame tyre-bottom contacts, author (+X forward,+Y port,+Z up), metres:
nose (+1.4595,0,-.600), main (-.5397,0,-.600), tail (-4.8768,0,-.260).
Nose station follows the Claude config rather than v1's +1.700 m. Diameters /
widths remain .260/.085, .440/.150 and .210/.065 m. The nose/main attitude
leaves .340 m under the tail; main/tail support is 4.487° from level and leaves
.1567 m normal clearance under the nose tyre. These are rigid tyre-circle
geometry checks, with no compression. See `ground-support.json` in the renders.

The config handoff has nose `isSteerable=false` and main radius .200 m; Ty's
new steering decision governs the visual rig, and the requested retained main
tyre is .220 m radius. This lane does not modify the physics config. Blade
construction, fork detail, skid shape, chord calibration and wheel vertical
placement remain visual approximations; no maintenance rigging claim.

Validation and CPU fallback evidence: `renders/grob103/blanc-18m/`. Blender
5.2.2 crashes during Metal capability detection before executing Python in
this sandbox; the requested Blender studio renders remain an explicit gap.
No Unity/device validation or gate approval is claimed.

---

# Current rig: Blanc Twin III Acro conversion, review pending

The 2026-11-03 Blanc conversion retains all 29 driven node names, parameters,
travel maps, extra fields and cockpit parent relationships. Surface origins and
axis markers now follow the extracted source surfaces. The generated
`grob103-own-rig.json` is authoritative; the historical pivot table below is
superseded for this asset. No runtime binding or physics station was changed.

Proposed visual eyes in native glTF: front (-1.15, .16, 0), rear (.04, .22, 0).
These fit the source canopy with the same 200 x 210 x 184 mm head-envelope
check as the previous asset. They require Ty's approval and do not change the
application eyepoints. The front panel and its gauge layers moved down .07 m
together. Glass and frame are separate static nodes; canopy animation is not
claimed. The two independent canopy outlines retain the source's engraved bow.
The current conversion is CC BY-SA: see the CREDITS.md beside the GLBs.

---

# Grob G 103A own model rig — review, not approved

Author axes: metres, +X forward, +Y port (pilot LEFT), +Z up. Datum is wing-root LE, not a claimed CG.
Native glTF = (−author X, author Z, author Y): nose −X, up +Y, port +Z.
This matches the SGS yaw/export convention. Expected Unity spec: `Vector3.right, Vector3.up`,
`namedSurfaces: true`, subject to importer verification. No Unity execution was authorized.

The JSON has the SGS schema, and the GLB nodes carry the corresponding `rig_*` extras.
Every rig node has an independent scene-root `<node>_HingeAxis` marker. Rotating surfaces' markers
lie on their hinge lines; a translating paddle's marker is a displacement-axis witness, not a rotary hinge.
Axes are unit vectors in native glTF. Positions in this table are WORLD rest positions; child node
translations are relative to their parent. `Stick_F/R` ride `TorqueTube`; grips ride the sticks.
`Airbrake_Cap_L/R` ride the corresponding spoilers, so caps move with the blades.

## TCDS conversion

Use asin(displacement/radius), treating the stated millimetres/inches as transverse throw, not arc length.
Aileron: asin(90/208) = 25.638542° up; asin(50/208) = 13.909242° down.
Elevator: asin(3.82/9.45) = 23.843060° up; asin(2.99/9.45) = 18.445479° down.
Rudder: asin(233/450) = 31.183307° each way. Airbrakes translate 0.170 m UP.
FAA G39EU's rounded 7.0 in airbrake dimension is 177.8 mm; the requested 170 mm is used.
Later serial-number elevator-radius/travel exceptions in the TCDS are not applied to this generic model.
At the aileron inboard station the modeled chordwise measurement radius is 208 mm;
at elevator half-span it is 240.03 mm (9.45 in); rudder base radius is 450 mm.
These are visual linkage approximations, not maintenance rigging instructions.

## Node table

| Node | Parent | glTF pivot (m) | glTF axis | Param / travel |
|---|---|---|---|---|
| `Aileron_L` | scene | (0.883333, 0.368192, 5.250000) | (0.118940, -0.040168, -0.992089) | aileron: [[-1, -25.638542487044873], [0, 0], [1, 13.90924177763508]] rot |
| `Spoiler_L` | scene | (0.580000, 0.392327, 2.900000) | (-0.000000, 1.000000, 0.000000) | spoiler: [[0, 0], [1, 0.17]] move |
| `Elevator_L` | scene | (5.359970, 0.830000, 0.020000) | (-0.000000, 0.000000, 1.000000) | elevator: [[-1, -18.445479046559875], [0, 0], [1, 23.84305963916233]] rot |
| `Aileron_R` | scene | (0.883333, 0.368192, -5.250000) | (0.118940, -0.040168, 0.992089) | aileron: [[-1, -13.90924177763508], [0, 0], [1, 25.638542487044873]] rot |
| `Spoiler_R` | scene | (0.580000, 0.392327, -2.900000) | (-0.000000, 1.000000, 0.000000) | spoiler: [[0, 0], [1, 0.17]] move |
| `Elevator_R` | scene | (5.359970, 0.830000, -0.020000) | (-0.000000, 0.000000, 1.000000) | elevator: [[-1, -18.445479046559875], [0, 0], [1, 23.84305963916233]] rot |
| `Rudder` | scene | (5.230000, 0.100000, 0.000000) | (-0.000000, 1.000000, 0.000000) | rudder: [[-1, -31.183306925848182], [0, 0], [1, 31.183306925848182]] rot |
| `Needle_ASI_F` | scene | (-1.713000, 0.195000, 0.145000) | (-1.000000, 0.000000, 0.000000) | airspeed_kias: [[0, 0], [151.18790496760258, 320]] rot |
| `Needle_Vario_F` | scene | (-1.713000, 0.195000, -0.145000) | (-1.000000, 0.000000, 0.000000) | vario_kt: [[-10, -150], [0, 0], [10, 150]] rot |
| `Needle_Alt100_F` | scene | (-1.713000, 0.085000, 0.000000) | (-1.000000, 0.000000, 0.000000) | altitude_ft: [[0, 0], [1000, 360]] rot |
| `Needle_Alt1000_F` | scene | (-1.710000, 0.085000, 0.000000) | (-1.000000, 0.000000, 0.000000) | altitude_ft: [[0, 0], [10000, 360]] rot |
| `Compass_Card` | scene | (-1.716000, 0.300000, 0.000000) | (1.000000, 0.000000, 0.000000) | heading_deg: [[0, 0], [360, 360]] rot |
| `Pedal_F_L` | scene | (-1.830000, -0.290000, 0.150000) | (1.000000, 0.000000, 0.000000) | rudder: [[-1, -0.045], [0, 0], [1, 0.045]] move |
| `Pedal_F_R` | scene | (-1.830000, -0.290000, -0.150000) | (-1.000000, 0.000000, 0.000000) | rudder: [[-1, -0.045], [0, 0], [1, 0.045]] move |
| `DiveBrake_Lever_F` | scene | (-1.200000, -0.140000, 0.285000) | (-0.000000, 0.000000, 1.000000) | spoiler: [[0, 0], [1, 32]] rot |
| `Trim_Lever_F` | scene | (-1.390000, -0.240000, 0.285000) | (-0.000000, 0.000000, 1.000000) | trim: [[-1, -18], [0, 0], [1, 18]] rot |
| `TowRelease_F` | scene | (-1.550000, -0.060000, 0.210000) | (1.000000, 0.000000, 0.000000) | tow_release: [[0, 0], [1, 0.05]] move |
| `Needle_ASI_R` | scene | (-0.513000, 0.245000, 0.145000) | (-1.000000, 0.000000, 0.000000) | airspeed_kias: [[0, 0], [151.18790496760258, 320]] rot |
| `Needle_Vario_R` | scene | (-0.513000, 0.245000, -0.145000) | (-1.000000, 0.000000, 0.000000) | vario_kt: [[-10, -150], [0, 0], [10, 150]] rot |
| `Needle_Alt100_R` | scene | (-0.513000, 0.135000, 0.000000) | (-1.000000, 0.000000, 0.000000) | altitude_ft: [[0, 0], [1000, 360]] rot |
| `Needle_Alt1000_R` | scene | (-0.510000, 0.135000, 0.000000) | (-1.000000, 0.000000, 0.000000) | altitude_ft: [[0, 0], [10000, 360]] rot |
| `Pedal_R_L` | scene | (-0.630000, -0.290000, 0.150000) | (1.000000, 0.000000, 0.000000) | rudder: [[-1, -0.045], [0, 0], [1, 0.045]] move |
| `Pedal_R_R` | scene | (-0.630000, -0.290000, -0.150000) | (-1.000000, 0.000000, 0.000000) | rudder: [[-1, -0.045], [0, 0], [1, 0.045]] move |
| `DiveBrake_Lever_R` | scene | (-0.010000, -0.140000, 0.285000) | (-0.000000, 0.000000, 1.000000) | spoiler: [[0, 0], [1, 32]] rot |
| `Trim_Lever_R` | scene | (-0.200000, -0.240000, 0.285000) | (-0.000000, 0.000000, 1.000000) | trim: [[-1, -18], [0, 0], [1, 18]] rot |
| `TowRelease_R` | scene | (-0.360000, -0.060000, 0.210000) | (1.000000, 0.000000, 0.000000) | tow_release: [[0, 0], [1, 0.05]] move |
| `TorqueTube` | scene | (-0.600000, -0.360000, 0.000000) | (-1.000000, 0.000000, 0.000000) | aileron: [[-1, -18], [0, 0], [1, 18]] rot |
| `Stick_F` | TorqueTube | (-1.280000, -0.360000, 0.000000) | (-0.000000, 0.000000, 1.000000) | elevator: [[-1, -15], [0, 0], [1, 18]] rot |
| `Stick_R` | TorqueTube | (-0.090000, -0.360000, 0.000000) | (-0.000000, 0.000000, 1.000000) | elevator: [[-1, -15], [0, 0], [1, 18]] rot |

## Instruments and operation

ASI art reads km/h; the runtime-supported parameter is `airspeed_kias`, converted in the rig map by
1 kt = 1.852 km/h. No new runtime parameter is required. Both altimeters have long and short hands;
their maps extrapolate (1,000 and 10,000 ft per turn). Vario zero points left; positive turns clockwise.
The front magnetic compass card wraps at 360°. All dial graphics are original Pillow artwork.
Colored controls: blue airbrake and green trim LEFT; yellow tow release LEFT/front; yellow canopy
jettison RIGHT. Yellow jettison follows the user's request; the manual calls for RED on both sides.
The exact panel/lever positions and travel are estimates. Front and rear sticks share roll via TorqueTube
and have independent pitch nodes driven by the same elevator parameter.

Canopy glass is static. Opening hinge direction is not confirmed; no canopy-open animation is claimed.
No GPS, position indicator, moving map, or FLARM map is modeled.

## Integration handoff (no runtime files changed)

`AirframeModels.OwnSpecs` currently lacks this id. Register it and select its own assets/livery in
`PickOwn` / livery routing as appropriate; enable named surfaces to prevent procedural carving.
The binding key is `glider-grob103-acro-like-own` for BOTH liveries. ConfigSync discovers
`docs/models/*-rig.json` by its `model` field; do not manually commit generated StreamingAssets rigs.
Claude must rebase datum-relative seats, eyes, wings, gear and CG consistently, then verify the importer's
actual placement/automatic longitudinal and vertical alignment in `AirframeModels.Place` and
`AirframeBuilder.TryPlaceModel`. The eye entries are proposals, not Ty-approved camera replacements.

### Twin III correction, 2026-11-03
All 29 driven nodes/maps/hierarchy retained. Outer aileron witnesses now end at
±9 m span; elevator pivots and markers rise .32 m to author Z=1.15 m. Front
panel instrument pivots move .10 m aft/.13 m down, front pedals follow its
station; front tow cable routed clear. Generated rig JSON is authoritative.
Proposed GLB eyes now (-1.15,.18,0) and (.04,.26,0), pending Ty review; prior
visual eye coordinates above are superseded. No application eyepoints changed.
