# Great Lakes 2T-1A-2 style biplane: our own model (v1 draft)

- Output: `unity/Assets/_Project/Resources/Models/great-lakes-like/great-lakes-like-own.glb`
- Build: `blender -b --python tools/own_models/build_great_lakes.py`
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
- Wings: 46 in chord, equal 8.126 m span (26 ft 8 in), 1 deg incidence, ~12 % section with slight camber.
  - Upper: flat centre section +-0.92 m with a semicircular TE cut-out (r 0.43, depth 0.17). The outer panels are
    swept 9 deg at the LE with 2.75 deg dihedral, and the chord line is 0.69 m above the thrust line.
  - Lower: LE 0.65 m aft of the upper LE (stagger), unswept, 2.5 deg dihedral.
  - Tips: asymmetric elliptical rounding (LE over 0.38 m, TE over 0.60 m).
- Tail: the hinge line is at x -4.565 and the stab at z -0.14. The stab span is 3.07 m (table in the script). The
  elevators have a V cut-out for the rudder (E_IN 0.06, 38 deg). Fin LE and rudder TE are tables, and the fin top is at
  z 0.957.
- Gear: the axle is at (-0.37, +-0.97, -1.43) with 0.40 m tyres. There is an oleo leg, a drag tube and an inner V per
  side. The tailwheel (r 0.10) sits on a spring.
  - 3-point attitude is ~8.9 deg nose up.

## Livery (v1 placeholder; no logos)
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
| Gear | Gear_Main, Wheels, Wheel_Pants |
| Nose | Prop, Spinner, Engine, Exhaust |

There is no aircraft config or Unity integration yet; those are a later handoff.

## Known differences / open items
- Height: the model reaches ~2.43-2.46 m to the upper wing in 3-point. The published 7 ft 4 in (2.235 m) was not
  forced, because the drawing and photos agree with the model.
- Variant choices (engine, 2- vs 4-aileron, tail, pants, livery, N-number) are assumptions awaiting Ty.
- Simplified items: the engine is primitives only, the cockpit has a few gauges only, and the cowl has no hinge or
  fastener detail.
