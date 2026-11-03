# J-3 cockpit v18: before / after (2026-11-02, Ty t282u–t286u)

Ty's words:
- "Make the diagonal tubes under the tank black and have them come to a point lower almost 'under the floor' and they
  go under the tank with close clearance." (keep the hidden cross bar behind the bottom of the panel)
- Panel, left to right (t283u, replacing t282u's order): **TACH, AIRSPEED, COMPASS, ALTIMETER, OIL TEMP/PRESSURE**,
  the airspeed the same size as the altimeter, the high-contrast lettering kept.
- t284u: at the far right of the panel, after the oil gauge, the J-3 plunger primer (small round knob on a brass
  barrel), grabbable in VR (pull out, push in).
- t285u: the fuel drain (sump + clear hose) at the CENTRE of the tank bottom, the hose straight down to a floor
  grommet at the centre, clear of the black tubes.
- t286u (water rudder): "the RETRACTED water-rudder blade must sit COMPLETELY BEHIND the vertical round tube (the
  rudder post/strut at the float stern), aligned with it in side and top views. Right now it looks like it's next to it
  (offset sideways). Fix the hinge axis and retracted pose so it tucks in directly behind the post, on the float
  centreline."

Every pair uses the **wheels** variant. **Before** is the GLB at ab890890 (v17). **After** is the GLB on
`lane/claude-j3-cockpit` (v18). The floats variant has the same cockpit plus the water-rudder handle
(lane/claude-j3-water-rudder, merged here; its own shots are made by `tools/own_models/j3_water_rudder_shots.py`).

**Camera.** Shots without "cutaway" or "low" in the name sit at the approved **rear eye** that the game uses, glTF
(0.7774, 0.5067, 0), at the 96° Quest field of view; the close-ups are a narrow field of view from that eye.

**How they were made.** `blender -b --python tools/own_models/j3_cockpit_shots.py -- <glb> <dir> <before|after> v18-...`.

**What changed in the model** (build_j3_cub.py's log):
- UNDER-TANK v18: the two V tubes are black (`Cabin_tube_black`), from the side clusters (station 1.218, ±0.293,
  z 0.004) to ONE point at the firewall centre, station 0.830, **z −0.522 (floor level; v17 met at −0.43)**, between
  the pedals. The cross bar at station 1.218 behind the panel's bottom edge stays (black too).
- FUELTANK v18: the tank's bottom rises from −0.265 to −0.216 so the tubes pass under it with **2.7 cm** of air from
  tube surface to tank, and the tank stays 12.0 US gal (45.4 L; it was bisected back to the v17 volume).
- Drain: sump at (0.940, 0.000, −0.246), the centre of the tank bottom; the clear hose runs straight down to a floor
  grommet at (0.940, 0); the tubes are ±0.087 m out there, ~7 cm of air each side.
- Panel: tach (slot 1, left), airspeed (2), compass (3), altimeter (4), oil temp/pressure (5, right). The airspeed's
  face is the altimeter's size (0.0355 bezel / 0.030 face).
- Primer: brass hex nut, brass barrel and lip on the panel to the right of the oil gauge (y −0.244, below the knee),
  a chrome rod and a dark round knob. The knob is the rigged node `Primer_Plunger` (param `primer`, 0 in .. 1 out,
  50 mm straight aft) with the hand point `Grip_Primer`; the rig table lists it as a PushPull control for the
  hands build. In the iOS build it sits pushed in (RigInputs.Primer = 0); the sim has no primer system yet.

Nothing here is approved. The headset is the judge.

| file | what it shows |
|---|---|
| `v18-01-panel-rear-seat-{before,after}` | The panel from the rear eye at 52°. **After:** tach, airspeed, compass, altimeter, oil; the V tubes black, meeting low between the pedals. From here the primer is behind the right cabane strut. |
| `v18-closeup-gauges-{before,after}` | 34° on the panel: the new order, airspeed and altimeter the same size, the primer's brass nut at the far right. |
| `v18-closeup-primer-{before,after}` | 14° on the primer: brass nut and barrel, round knob, to the right of the oil gauge. |
| `v18-02-under-tank-rear-seat-{before,after}` | From the rear eye, down under the panel: the black tubes running under the tank to the floor. |
| `v18-closeup-centre-drain-{before,after}` | 20° on the drain: **before** the hose at the right side, **after** at the centre, straight down between the tubes. |
| `v18-03-tank-footwell-cutaway-{before,after}` | A camera in the footwell looking forward: the V meeting at the floor, the tank above it, the centred hose. |
| `v18-04-tank-side-cutaway-{before,after}` | From the left with the left side taken away: the tubes passing under the tank bottom, the hose straight down. |
| `v18-05-primer-tank-side-cutaway-{before,after}` | Higher and further aft from the left: panel side, tank, tubes and hose in one view. |
| `v18-06-tubes-under-tank-low-{before,after}` | A low camera aft of the footwell: the tubes' clearance under the tank. |

## Water-rudder blade (t286u): `water-rudder/`

**Before** = the floats GLB at ccde8a48 (v17 water rudder); **after** = this lane's floats GLB. All six stern views per
state are ORTHOGRAPHIC, 1.0 m (side, rear) / 0.85 m (top) wide, centred 0.12 m aft of the blade hinge:
`v18-stern-{side,top,rear}-{down,up}.png`, from `tools/own_models/j3_water_rudder_shots.py -- <floats.glb> <dir>
v18-stern-...` (it poses the blade from the GLB's own retract_axis_gltf / retract_deg, as the game does). Side = from
outboard (left), aft to the right; top = from above under the tailplane, aft to the right; rear = from aft, looking
forward. `after/07-blade-down.png`, `after/08-blade-up.png` are the v17 perspective blade shots re-made.

What was wrong: v17's blade straddled the hinge (centre only 4.3 cm aft, a 12 cm lobe AHEAD of the post) and swung
140°, so retracted the lobe came round beside the post and the blade sat up and forward of it, not behind it.

What changed (build_j3_cub.py, configs `floats.waterRudder*`, FloatHydro is unchanged code):
- The hinge stays on the post axis at its foot, the retract axis stays the float's lateral axis (the blade swings in
  its own plane, the float centreline Y_FL). The **whole blade is now aft of the post**: its leading edge and top edge
  are 17 mm (post radius 11 mm + 6 mm) from the hinge, carried by a strap into the yoke.
- **Retract 90°** (was 140°): the bottom swings aft and up, and the blade stands vertical **directly behind the post**:
  every point 14 mm+ aft of the post axis, within 4 mm of its plane (the post is 22 mm across, the blade 8 mm thick),
  its top 0.247 m above the hinge, **under the post top (0.255 m)**, so from aft the post hides it (rear-up view). The
  build asserts all three.
- To fit under the post top the blade's chord went 0.326 → **0.2305 m** and its depth 0.172 → **0.2459 m** (area kept:
  0.0440 → 0.0445 m²; down, its bottom is 0.263 m below the hinge, was 0.189). Configs: chord 0.2305, span 0.2459,
  bladeAft 0.1323, bladeBelow 0.1399, area 0.0445, retractRad 1.5708.
- **Hydro side effect, Ty to judge:** the deeper blade has twice the aspect ratio (0.67 → 1.36), so Helmbold's lift
  slope is 1.03 → 1.93 /rad and the fully wet blade at 3 m/s and 25° makes 169 N (was 89 N): the water rudder steers
  harder than v17. Hooked it is still dry afloat at rest and taxiing at 5.5 m/s.
- Test: `J3WaterRudderTests.RetractedItStandsWhollyBehindThePostInLineWithIt` (the sim's drawn ellipse, down and
  90° up: every edge point > 11 mm aft of the post axis, all above the hinge up) and the literals of the S/AR/a/lift
  tests updated to the new blade.

| file | what it shows |
|---|---|
| `v18-stern-side-down` | Side: before the oval centred under the hinge, half of it ahead of the post; after wholly aft of the post. |
| `v18-stern-side-up` | Side: before tilted up and forward over the float; after standing vertical just aft of the post, below its top. |
| `v18-stern-top-down` / `-up` | Top: the blade a thin line on the float centreline, aft of the post, both states. |
| `v18-stern-rear-down` | Rear: the blade's edge under the post, on the centreline. |
| `v18-stern-rear-up` | Rear: after, hidden behind the post. |
