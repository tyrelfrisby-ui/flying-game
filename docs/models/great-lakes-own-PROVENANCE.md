# Great Lakes 2T-1A-2 style biplane: our own model (v6, NOT approved)

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
- Tail (v3, Ty: "exact to the drawing"): the fin/rudder side profile (FLE / RTE tables) and the stab/elevator plan
  form (STAB_T) are measured off the 1970 AAM 2T-1A three-view at 0.0115 m/px. The rudder hinge line is at x -4.59
  (drawing px 1232.5) and the stab at z -0.14; the fin top is at z 0.914. The aft-most point (rudder/elevator TE) is
  at -5.14, so the length over the spinner is 6.24 m against the published 6.20 m (the drawing governs the tail; 0.6 %).
  The fin root is the concave blend into the top longeron as drawn (Ty's review of overlay-side.png); the top
  longeron is at z 0.066 at x -3.8. Dense fin stations (FIN_ST) keep the outline from faceting. The elevators have a
  diagonal cut-out for the rudder (E_IN 0.06, 32 deg); rudder +-35 deg (min clearance 0.036 m) and elevator 21 up /
  29 down are kept.
  - Check method: `tools/own_models/gl_overlay.py -- <glb> <outprefix>` renders the Fin + Rudder + Fuselage silhouette (side) and the
    Stab + Elevators (top/bottom) orthographically at the drawing scale and draws the outline in red over the drawing
    (grey/black). Outputs overlay-side/top/bottom.png. This is a visual check only; no drawing pixels go into the model.
  - Where the drawing's views disagree slightly, the side view governs the fin/rudder and the plan views govern the
    stab/elevator outline.
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
No registration, logos or text on the liveries. Painted by eye from Ty's photos as our own simple geometry (bands,
offsets, rays, level sets); no photo pixel is sampled or traced. Registrations and watermarks in the photos are ignored.
- `red-sunburst`: all-red airframe; navy, white-outlined fuselage stripes from the nose that taper to a point at the
  stab LE (v4); navy cowl front; red/white/navy sunburst rays from the centre-section TE on the wings (top and bottom),
  sweeping AFT from the root; on the stab/elevators (v4, from red-tail-closeup) rays from a hub at |y| 0.16 behind the
  stab LE at 12/30/64 deg, navy outer sectors, red/navy alternating, white pinstripes, white 12 mm edge (no navy rim);
  navy tips and edge trim on the wings; fin/rudder outline red edge + navy + white inner line that starts thin at the
  fin root, follows the fin LE, top and rudder TE and tapers to a point at the rudder's bottom-front corner (v4: it no
  longer comes down/forward to meet the fuselage stripes); red pants with a navy stripe; red struts and gear; white
  spinner; Hartzell 2-blade prop.
- `white-stripes` (v3 from white-1/2/3; v4 wings/tail from white-wings-1/2): white airframe. Fuselage, top to bottom:
  white decking, red, white separation, light blue, white separation, navy, white belly; the light blue and navy start
  behind a white nose bowl and the red starts at the cowl top. v4: the bands are now constant-depth offsets below ONE
  straight reference line (cowl z 0.24 down to the fin-root LE), so they run straight along the side and only curve up
  at the vertical tail, where they become level sets of the envelope (fin LE, fin top, rudder TE): white rim, red outer
  arc, light-blue fin, navy core in the lower fin/rudder. v4 wings (top AND bottom of both wings): predominantly white;
  each tip has a red cap with a diagonal inboard edge, then white, light-blue, white, navy bands, raked aft-outboard
  (edge line |y| − 0.12·(distance aft of LE); red > 3.55 m, light blue 3.21–3.49, navy 2.90–3.15). Stab/elevators (both
  faces): same treatment, rake 0.42 (red > 1.10, light blue 0.88–1.06, navy 0.66–0.84), rest white. Widths and angles
  read off white-wings-2 by eye (~10 mm/px at the tip). Pants: white nose; light-blue and red bands rising at the front
  and tapering aft; low navy swoosh. White struts and gear; chrome spinner; Hartzell 2-blade prop.

### v1 placeholder (default file `great-lakes-like-own.glb`)
- White base with a red nose bowl.
- Red cheat line sloping down aft, with a navy pinstripe below it.
- Red fin and rudder cap with a navy pinstripe.
- Red wing tips (|y| > 3.45) and stab tips (|y| > 1.20), each with a navy pinstripe.
- Red struts, red spinner, red pant noses.
- Placeholder registration N719GL. It has not been checked against the FAA registry, so swap it for Ty's choice.

## v4 changes (2026-10-28)
- **Cowl (all liveries):** v3 ended the cowl at an open, chopped front. The cowl tables now stop at x 0.57 and a
  quarter-ellipse lip closes them to a rounded face at x 0.69 (face half-sizes top 0.165, bottom −0.30, half-width
  0.30); 12 sine-spaced loft stations over the lip; the two cylinder inlets (y ±0.215, 48×66 mm) are cut in the face;
  spinner backplate moved to the new face. Lip shape by eye from red-2-ramp / white-3-ramp.
- **red-sunburst tail** and **white-stripes wings/tail/fuselage**: see Liveries above.
- Renders and photo compares: `/workspace/greatlakes-own-v4/` on the build box (four-views, iso, tail, nose, photo and
  compare-*.png). References used by eye only; nothing is traced or sampled.

## v5 changes (2026-10-28). NOT approved by Ty.
Ty's notes covered the red nose and pants, and the white nose, wing tips, tail and lower rudder. Everything is by eye from his scheme photos; nothing is traced or sampled.
- **red-sunburst nose and side stripes** (red-1, red-2, red-3):
  - The navy nose bowl now ends in an aft-pointing chevron, XB(z). The aft edge is x ~0.47 at the cowl top, the chevron point is at (0.37, −0.15) on the lower stripe line, and the edge runs forward to x ~0.53–0.56 under the chin. A white outline runs just aft of it.
  - The upper stripe flares at the front into a tall navy panel. Its top edge sweeps up in a long hockey-stick curve from about x −0.35 to ~4 cm under the cowl top. Its lower edge drops toward the chevron. Its front edge parallels the bowl outline, with a thin red gap (white, red, white).
  - The lower stripe sits ~0.16 m below. It tapers symmetrically to a point that runs into the chevron.
  - Both stripes keep the v4 straight run and taper aft. The top and bottom plan maps use the same XB edge.
- **red-sunburst wheel pants:**
  - One long navy wedge runs from the pant nose. Its white-edged top sweeps from the top of the nose down-aft to a point at du −0.23. Its bottom edge rises from the nose bottom.
  - A white pinstripe continues along that lower edge almost to the pant tail. The pant is red above and below.
- **white-stripes nose** (white-3 / white-1 / white-2):
  - The white bowl's aft edge is near-vertical over the light blue (x ~0.51–0.53). Below that, the navy's front edge slants forward going down (x 0.575 at z −0.30, 0.62 at −0.45), so the navy wraps under the cowl toward the chin.
  - Light blue runs up onto the cowl top front. The red wedge now starts at x ~0.22 and reaches full width by x ~−0.20.
  - The navy's lower edge drops into a smooth chin lobe, deepest (z ~−0.60) around x 0.05–0.22, and sweeps back up to the straight band edge by x ~−0.6.
- **white-stripes wings and stab** ("several parallel stripes running the span along the leading edge then they turn and angle back at the tips"):
  - The bands are nested L shapes. Each band has a spanwise leg parallel to the LE, at a fixed distance aft of the local LE, and a tip leg raked aft-outboard as in v4. The colour is the outer of the two legs, so they meet in a mitred corner.
  - Order from the LE and tip inward: red, white, light blue, white, navy, white.
  - Distances aft of the LE: wings 0.11 / 0.14 / 0.24 / 0.27 / 0.36 m; stab 0.06 / 0.085 / 0.15 / 0.175 / 0.235 m.
  - The plan map is shared by the top and bottom skins, so the LE legs also appear underneath. Judgement call: the photos only show the underside of the upper wing, and that has them.
- **white-stripes fin and rudder** (white-3 fin crop, white-wings-2): v4 painted the whole fin and rudder below the stab navy. Now:
  - The rudder below the stab is white.
  - Under the stab, forward of the hinge, a 0.12 m navy band (the fuselage navy run-out) reaches the tail post, with white below.
  - Above the stab, the v4 arch (navy core, white rim, light blue, red rings) is unchanged.
- Renders and photo compares are in `/workspace/greatlakes-own-v5/` on the build box.

## v6 changes (2026-10-28). NOT approved by Ty.
Method: Ty said "just copy what is in the photos". The work ran as iterative photo-compare passes, with cameras PnP-solved on landmarks in Ty's photos. References were used by eye only, and no third-party meshes were used. Notes and compares are in `/workspace/greatlakes-own-v6/` on the build box.
- **The white-stripes wing and stab maps are frozen (approved by Ty) and were not touched.**
- **Red-sunburst side stripes and nose bowl** (traced from red-2-ramp back-projected onto the side map):
  - The two navy stripes converge aft to one point at x -3.78.
  - At the front, the upper tip curls up and the lower tip curls down, away from the bowl chevron.
  - The bowl edge is near-vertical up top, has an aft chevron at about z -0.12, and the chin sweeps forward.
  - The white bowl outline is 0.014 m.
- **Nose geometry (all liveries):**
  - The cowl runs about 0.11 m further forward (lip/face 0.64/0.80) and the chin is 6-10 cm shallower.
  - The spinner is 0.30 m long with r 0.155 (tip unchanged at x 1.10), and the prop plane is at 0.85.
  - The cooling inlets are smaller.
  - The spinner is chrome on red-sunburst and white-stripes, and red on v1.
- **Cockpits (all liveries; Ty: "very tight cockpit openings ... rounded"):** two small oval holes in the rounded top deck, each with a black padded coaming rim, replacing the full-width square-ended cuts.
  - Front opening: x -0.97..-1.33, half-width 0.19.
  - Rear opening: x -1.64..-2.08, half-width 0.215.
  - Placed from the N3765F side photo (spinner-to-rudder scale) and the three-view top view, and narrowed on Ty's request.
  - The headrest starts at -2.10, and the interior was shrunk to fit.
  - The cut rings use fixed angular sampling, so the side stripe map stays clean under the openings.
- **Windscreens (Ty: "should wrap around"):** each is a bent three-panel wraparound (flat centre, side wings swept back round the opening, frame on the top, sides and bends).
  - The rear screen moved aft from -1.35 to -1.56.
- **Remaining differences:**
  - The stab sits about 0.1 m high against red-2-ramp.
  - The white-3-ramp camera fits poorly (wide-angle phone shot), so the white compares are approximate, and the white fin/rudder is unchanged from v5.
  - The red-1/red-3 cameras sit about 0.2 m low on the starboard side.

## v7 changes (2026-10-28). NOT approved by Ty.
Photo-match passes on the white-stripes nose paint and fin/rudder only (white-1/2/3 and white-wings-1/2, by eye).
The white wings and horizontal tail are frozen and unchanged; the v1 and red-sunburst liveries are unchanged (no rebuild).
The camera solve on white-3 was unreliable, so the edges were placed by feature relationships (cowl lip, pinstripe,
cabane leg, gear fairing, hinge line, stab, fin top).
- Nose (white-stripes): the white is now a ring behind the cowl lip, not a bowl over the whole cowl. The light-blue
  front edge runs parallel to the lip (x 0.735 at the top, slanting to 0.625 at the pinstripe). The navy's front-top
  corner is rounded and set back (x 0.575), and its front edge sweeps forward going down to x 0.70 at the cowl
  bottom. The knots are in depth below the stripe reference line, so the edges follow the bands.
- Chin: the navy covers the whole lower cowl side to the bottom line from x 0.70 back to x 0.30. Its lower edge then
  turns up in a quarter ellipse and joins the band edge at x 0.08, ahead of the gear fairing. The navy also wraps
  under the cowl (bottom map, x 0.30 to 0.685).
- Red wedge: the front tip is at x ~0.40, ahead of the front cabane leg, and widens in an S-curve to its full 0.13 m
  by about x 0.05.
- Fin/rudder: the navy core ellipse is flatter and higher-shouldered (centre hinge-0.26 m, z -0.10; radii 0.58 x
  0.72). Its front edge sits about 0.08 m further aft, so the light-blue band is about 0.21 m wide as in white-3
  (v6: 0.15 m). Below the stab and ahead of the hinge, the navy now runs right down to the fuselage bottom at the
  tail post. The rudder below the stab stays white (Ty, v5).
- Horizontal tail height: NOT changed. The three-view side view (0.0115 m/px) puts the stab chord line 92 px
  (1.06 m) below the fin top and 17 px (0.20 m) below the fuselage top line at the fin root. The model already has
  1.054 m and 0.20 m (Z_S -0.14), so the drawing does not support lowering it by 0.1 m.
- Open: white-wings-1/2 show the fin's navy core top at ~40 % of the fin height above the stab, while white-3 shows
  ~63-70 %. v7 follows white-3 (the primary photo).

## v8 changes (2026-10-28). NOT approved by Ty.

Ty: red fuselage paint APPROVED and frozen; white wings/stab frozen. v8 = red-sunburst wing sunburst and wheel pants photo-matched
(red-1-air, red-2-ramp, red-3-tail, red-tail-closeup) plus a cowl-wrap check on both liveries. Comparisons on the agent box:
/workspace/greatlakes-own-v8/ (compare-red-wings-*.png, compare-red-pants-*.png, nose-{top,bottom,hi34,lo34}-<livery>.png).

- Wing sunburst (red-sunburst, both wings, top + bottom): red-3-tail was back-projected onto the lower-wing plan map through a
  re-solved PnP camera (landmark reprojection ~6 px) and the rays traced there; red-1-air (upper-wing underside) and red-3
  (starboard upper underside) agree. Five rays fan from the LE on the centreline, boundaries 36.5 / 48.5 / 56 / 68 / 78 deg
  measured from straight aft toward outboard (~+-2 deg at photo resolution); white 22 mm pinstripes. Sectors root -> LE:
  red, navy, red, navy (big, reaches the TE), red (big, over the tip TE), navy (band along the LE out to the tip). No navy
  rim and no navy tip cap any more: the tip is navy forward / red aft where the sectors fall. Same map on the upper wing.
  Assumptions: the upper-wing TOP and the lower-wing BOTTOM are not visible in any red photo -> same plan map as the
  observed skins. A thin red rim seen on the upper-wing tip underside in red-1 is ambiguous (likely the red sector / edge
  highlight) and is not modelled. Stab unchanged (v4 rays).
- Wheel pants (red-sunburst only; white-stripes keeps the v7 pants): red-2-ramp PnP overlay showed nose and tail in the right
  place but the pant ~13% too deep and the tail too low. Pant now 0.375 m deep at the axle (was 0.435: top 0.23, bottom
  0.145 -> the tyre shows a little more below), tail point swept up to 0.155 m above the axle, nose rings raised 0.03; length
  1.10 m unchanged; the oleo still enters the pant top with no fillet (as in the photo). Paint: navy nose cap + wedge, its
  upper white edge sweeps from the pant top ahead of the leg down-aft and ends 0.10 m behind the axle (was 0.23); the lower
  white line sits ~80% down the nose (thin red bottom band) and runs aft into the up-swept tail tip as a pinstripe.
  Residual: in red-2 the whole pant still sits ~7 px low (likely camera depth error, since the near pant is ~1 m nearer than
  the fuselage landmarks); gear geometry not changed.
- Cowl wrap (both liveries, Ty: paint must cross the top and bottom centrelines continuously as on the Pitts): red-sunburst
  nose bowl and navy were already continuous and symmetric, so no change. white-stripes: the light-blue collar stopped
  short of the top centreline (white strip above it ahead of x 0.42), so it now runs up to the top seam and joins the
  other side, with the red/light-blue bands painted on the top plan view as well; the bottom navy chin was already continuous.
  Close-ups: nose-top / nose-bottom / nose-hi34 / nose-lo34 for each livery.
- GLBs rebuilt: great-lakes-like-own-red-sunburst.glb (wings, pants), great-lakes-like-own-white-stripes.glb (cowl wrap only).
  Red fuselage paint, white wings and stab untouched.

## v9 changes (2026-10-28). NOT approved by Ty.

- White-stripes livery only: lower rudder (aft of the hinge, below the stab) is now NAVY below a boundary that continues
  the straight lower edge of the navy fuselage/fin stripe (`_zb = Zr(A) - t5`) aft across the rudder to the trailing
  edge; navy fills from that line down to the rudder bottom. Above the line (between it and the stab) stays white.
  Per Ty's description, which overrides the photos (the deflected-down elevator hides this area in white-1/2/3).
- Nothing else changed; red-sunburst GLB not rebuilt. Compares: compare-white-rudder.png, white-tail-side.png.

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
- Still open with Ty: ailerons (2 vs 4), which livery is the default, N-number. (Tail resolved in v3: matched to the drawing.) 180 hp 2T-1A-2 kept.
- Simplified items: the engine is primitives only, the cockpit has a few gauges only, and the cowl has no hinge or
  fastener detail.
