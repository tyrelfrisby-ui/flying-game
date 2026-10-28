# Great Lakes 2T-1A-2 style biplane: our own model (v2)

- Outputs (`unity/Assets/_Project/Resources/Models/great-lakes-like/`): `great-lakes-like-own.glb` (v1 placeholder paint),
  `great-lakes-like-own-red-sunburst.glb`, `great-lakes-like-own-white-stripes.glb`
- Build: `GL_LIVERY=v1|red-sunburst|white-stripes blender -b --python tools/own_models/build_great_lakes.py`
- Gear poses: `blender -b --python tools/own_models/gl_gearpose.py -- <glb> <extended|static|full|deg> <outprefix>`
- Renders: `blender -b --python tools/own_models/gl_fourview.py -- <glb> <outprefix>`

## Clean-room statement
All geometry is generated procedurally by `build_great_lakes.py`: superellipse fuselage sections, NACA-style airfoil
lofts and simple primitives. No third-party or GPL mesh, UV, texture or model file is read or imported. The reference
drawings and photos were used **by eye only**: numbers were read off them with a pixel grid and typed into the script
as tables. Nothing was traced, auto-digitised, sampled or projected into the mesh or the textures. Every paint boundary
is our own simple geometry (lines, offsets, |y| bands). The registration art
`tools/own_models/art/greatlakes-n719gl.png` is our own PIL render of Liberation Sans Bold (SIL OFL). There are no
manufacturer logos or trademarks on the model.

## References (kept off-repo on the build box, `/workspace/refs/great-lakes/`)
- American Aircraft Modeler, Sept 1970, Great Lakes 2T-1A three-view. This is the airframe outline: fuselage lines,
  wing planform and stagger, tail, struts and gear. Scale is 0.0115 m/px, which matches the published span and length.
- FAA TCDS A18EA rev 11: 2T-1A-2 engine and propeller (IO-360 180 hp, Hartzell HC-C2YK-4F 74 in), MAC 46 in, control
  travel (rudder +-35 deg).
- Great Lakes aircraft maintenance manual (GL-ACMaintenanceManual.pdf): wing rigging (dihedral, incidence) and the
  landing gear layout drawings I-7 / I-9 (axle hinge at the centreline, aft compression strut, oleo shock strut).
  Read by eye; no drawing was traced.
- Ty (owner): main gear shock-strut stroke roughly 8-10 in; two paint schemes from his own photos
  (`/workspace/refs/great-lakes/ty-schemes/`).
- Published specs: span 26 ft 8 in, length 20 ft 4 in, height 7 ft 4 in, wing area 187.6 sq ft.
- Wikimedia Commons photos of 2T-1A-2 aircraft (G-GLII, D-EBGL, D-EHMM, N100GL, N6196L, N3765F, N6220L and others;
  authors and licences listed in REFERENCES.md). These were used for the nose, the big-tail rudder outline, the gear
  and pants, the cockpits and paint ideas.

## Variant modelled (v1 assumptions, to confirm)
- 2T-1A-2 with a Lycoming IO-360 class flat four, round cowl with two inlets, Hartzell spinner.
- 2-blade 74 in prop, grey with white tips. It turns **clockwise seen from the cockpit** (American / Lycoming). The
  blade sections advance along `cross(+X, radial)`, the same rule as our Pitts.
- "Big tail" fin and rudder, and wheel pants.
- Ailerons on the lower wing only (factory 2-aileron). Friese-style, hinge at 76 % chord, |y| 1.32-3.46 m.
- N interplane struts, an N cabane each side, double flying and landing wires, struts under the stab and wires above.

## Geometry summary (model frame)
- Frame: metres, +X nose, +Y port, +Z up. The origin is the upper-wing centre-section LE station on the thrust line.
  The model is yawed 180 deg at export, like the Pitts and EA300. Note that the Pitts origin is the lower-wing LE, so
  this origin is different.
- Fuselage: the spinner tip is at +1.10 and the rudder TE at -5.11, so overall length is 6.21 m (published 20 ft 4 in =
  6.20 m). The cowl face is at +0.66 and the firewall at -0.17. The two open cockpits run -0.95..-1.28 and
  -1.48..-2.03, each with a small windscreen. A headrest fairing runs aft to the fin.
- Wings: 46 in chord, equal 8.126 m span (26 ft 8 in), 3 deg incidence on both (manual; v1 had 1 deg), ~12 % section with slight camber.
  - Upper: flat centre section +-0.92 m with a semicircular TE cut-out (r 0.43, depth 0.17). The outer panels are
    swept 9 deg at the LE with 0 deg dihedral (manual; v1 had 2.75), and the chord line is 0.69 m above the thrust line.
  - Lower: LE 0.65 m aft of the upper LE (stagger), unswept, 2 deg dihedral (manual; v1 had 2.5). The interplane gap was checked against the strut pin
    lengths and left unchanged.
  - Tips: asymmetric elliptical rounding (LE over 0.38 m, TE over 0.60 m).
- Tail: the hinge line is at x -4.565 and the stab at z -0.14. The stab span is 3.07 m (table in the script). The
  elevators have a V cut-out for the rudder (E_IN 0.06, 38 deg). Fin LE and rudder TE are tables, and the fin top is at
  z 0.957.
- Gear (v2, rigged; see "Main gear and suspension" below). The tailwheel (r 0.10) sits on a spring.

## Main gear and suspension (v2)
Layout from the manual drawings I-7 / I-9, the three-view and photos (Ty's white-3 shows the near-vertical oleo plus the
aft compression strut). Each side is a rigid triangle (axle + compression strut) hinged on the line from the axle
U-joint at the fuselage centreline to the compression-strut fitting on the lower longeron; the oleo shock strut runs
from the outrigger top pin to the axle end and telescopes. Tyre 600x6, 17.5 in diameter.

| Pin (model frame, m) | Point |
|---|---|
| H (-0.36, +-0.05, -0.83) | axle U-joint hinge (Gear_Pivot_<s>) |
| C (-0.85, +-0.40, -0.78) | compression-strut fitting (Gear_HingeAxis_<s>) |
| T (-0.20, +-0.62, -0.60) | oleo top pin (Gear_OleoTop_<s>) |
| (-0.377, +-0.97, -1.487) | wheel centre, extended |

Stroke: **9 in (0.2286 m)**. No published figure was found (manual, TCDS); this is Ty's 8-10 in, mid value.

| State | Source | Hinge | Oleo length | Compression | Wheel rise | 3-pt height | 3-pt pitch |
|---|---|---|---|---|---|---|---|
| Extended (as exported) | geometry | 0 deg | 0.931 m (36.6 in) | 0 | 0 | 2.469 m (97.2 in) | 9.96 deg |
| Static (derived) | solved so height = published 7 ft 4 in | 15.79 deg | 0.774 m | 6.19 in (69 %) | 0.227 m | 2.235 m (88.0 in) | 6.91 deg |
| Full (Ty stroke) | 9 in stroke | 22.92 deg | 0.702 m | 9.00 in | 0.342 m | 2.119 m (83.4 in) | 5.41 deg |

At full compression the wheel also moves 0.148 m aft and 0.158 m outboard (track widens). The extended gear explains the
whole v1 height gap (2.469 m vs 2.235 m): the published 7 ft 4 in is consistent with the aircraft sitting on loaded
(static) gear at ~6.2 in of the 9 in stroke, not with extended gear. The static figure is a derived estimate, not a
published number. Caveat: if Ty's 8-10 in is wheel travel rather than strut stroke, the strut stroke would be ~6 in
(wheel-travel / strut ratio ~1.5) and static would sit near the bottom of the stroke; to confirm with Ty.

Rig in the GLB: Gear_Pivot_<s> (at H) parents Gear_Axle, Wheel, Wheel_Pant and Oleo_Cylinder (origin at the axle end).
Rotate the pivot about (Gear_HingeAxis - Gear_Pivot) by the hinge angle (sign in extras wheel_up_sign_model_frame; check
the wheel goes up after import), then aim Oleo_Cylinder at Gear_OleoTop and Oleo_Shaft (fixed, origin at T) at the
cylinder origin (re-aim 3.1 deg static, 4.4 deg full). Pivot extras: stroke_m, strut_len_extended_m, hinge_deg_static,
hinge_deg_full.

## Liveries
No registration, logos or text on the v2 liveries. Painted by eye from Ty's photos as our own simple geometry (bands,
offsets, rays); no photo pixel is sampled. Registrations and watermarks in the photos are ignored.
- `red-sunburst`: all-red airframe; navy, white-outlined fuselage stripes nose to tail; navy cowl front; red/white/navy
  sunburst rays from the centre-section TE on the wings (top and bottom) and the stab; navy tips and edge trim; navy
  fin/rudder outline; red pants with a navy stripe; red struts and gear; white spinner; Hartzell 2-blade prop.
- `white-stripes`: white airframe; swooping red over light blue over navy stripes from cowl to tail; red edge/tip trim
  with blue/navy pinstripes on wings and stab; light-blue fin and navy rudder with a red outline; red/blue/navy striped
  white pants; white struts; chrome spinner; Hartzell 2-blade prop.

### v1 placeholder (default file `great-lakes-like-own.glb`)
- White base with a red nose bowl.
- Red cheat line sloping down aft, with a navy pinstripe below it.
- Red fin and rudder cap with a navy pinstripe.
- Red wing tips (|y| > 3.45) and stab tips (|y| > 1.20), each with a navy pinstripe.
- Red struts, red spinner, red pant noses.
- Placeholder registration N719GL. It has not been checked against the FAA registry, so swap it for Ty's choice.

## Unity part names
The part names are compatible with AirframeVisual / RemoteAircraft:

| Group | Parts |
|---|---|
| Fuselage and cockpit | Fuselage_Painted, Headrest, Windscreen_Glass, Windscreen_Frame, Cockpit |
| Wings | Wing_Upper, Wing_Lower, Aileron_LL, Aileron_LR |
| Tail | Stab, Elevator_L, Elevator_R, Fin, Rudder |
| Struts and wires | Strut_Cabane, Strut_Interplane, Strut_Stab, Wire_Bracing |
| Gear | Gear_Main (fixed tubes), Wheel_Tail, Gear_Pivot_L/R > Gear_Axle_<s>, Wheel_<s>, Wheel_Pant_<s>, Oleo_Cylinder_<s>; Oleo_Shaft_<s>; markers Gear_HingeAxis_<s>, Gear_OleoTop_<s> |
| Nose | Prop, Spinner, Engine, Exhaust |

There is no aircraft config or Unity integration yet; those are a later handoff.

## Known differences / open items
- Height: resolved in v2 by the gear model (see the suspension table).
- Still open with Ty: ailerons (2 vs 4), tail, which livery is the default, N-number. 180 hp 2T-1A-2 kept.
- Simplified items: the engine is primitives only, the cockpit has a few gauges only, and the cowl has no hinge or
  fastener detail.
