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
