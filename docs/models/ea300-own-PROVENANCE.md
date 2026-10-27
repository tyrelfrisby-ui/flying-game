# extra-300-like-own.glb — clean-room Extra EA 300 (c/n 047 / N300UY style)

Built entirely by `tools/own_models/build_ea300.py` (Blender, procedural). No third-party mesh was opened, imported,
traced or compared — in particular not the FlightGear Extra 300 model. The existing `extra-300-like.glb`
(Sketchfab "Aerobatic Airplane", Kemal Çolak, CC-BY) is untouched and not used.

## Sources (dimensions only)
| Item | Value | Source |
|---|---|---|
| Span | 8.00 m | NZ CAA TAR 8/21B/31 Rev 1, p.11 Extra drawing EA-061025 (3-view, 300/L) |
| Length | 6.96 m | same |
| Height | 2.62 m | same |
| Stab span | 3.20 m | same |
| Wing tip chord | 0.87 m | same |
| Stab tip chord | 0.60 m | same |
| Fin tip chord | 0.50 m | same |
| Root chord ~1.62 m, fin/stab/gear positions | measured off the same drawing (~181 px/m) | same |
| Mid-wing height, canopy line | photo, N300UY KSNA 2006-01-30 © Nick Taylor (airport-data.com #665837), visual only | |
| Mid-wing vs 300/L low wing | TAR text | same PDF |

URL: https://www.aviation.govt.nz/assets/aircraft/type-acceptance-reports/Extra-EA-300-Series.pdf

Font: lettering art (SUNRISE / AVIATION / TALK / FLYING / TALKFLYING) uses **Archivo Black**, Omnibus-Type, SIL Open Font License 1.1.

## Paint
Approved N300UY / TALKFLYING scheme (fg-models v9 5fdacad parameters): navy (27,42,74), red (200,24,36), white (246,246,246);
fuselage red stripes LP1 0.085 / LP2 0.405 (0.025 wide) with TALKFLYING between, aft of the wing; navy ends just above the stab;
fin navy cap with 3 white stars and red line; wing/stab navy front 32 %, 0.03 gap, 0.025 red pinstripe; SUNRISE/AVIATION on top,
TALK/FLYING underneath (tapered, off the ailerons); clear canopy; white prop with 5 in red tips and 1 in nickel LE; navy-topped wheel pants with red stripe.

## Known differences from the real aircraft
- Ailerons start at y 1.40 m to match the game's aileron rig; the real ones run further inboard.
- Airfoil is a generic symmetric 16→12 % section; no dihedral modelled; root chord and planform measured from the 300/L drawing.
- Fuselage cross-sections are superellipses — no cowl seams, louvres, exhausts, access panels, antennas or step.
- Canopy is a single smooth bubble with three frame bows; no hinge hardware, no interior/pilots.
- Gear legs, pants and tailwheel are simplified; no brakes.
- Registration N300UY is replaced by TALKFLYING per the approved scheme; real stripe geometry differs.
- Belly is plain white (side paint not wrapped underneath).
- In-game the model is rescaled to the config span (~0.94) and the config tail arm is ~15 cm shorter, so elevator hinge alignment is approximate.


---

## v2 (2026-10-26): redrawn to the FlightGear-based v9 shape — reference disclosure

**This section replaces the "clean-room" claim for v2.** By Ty Frisby's explicit decision (he was warned about the GPL risk), the v2 geometry was fitted to match the FlightGear EA300 model by **Patrizio Melis (GPL)**. Specifically, it matches the version used in this repo on codex/fg-models at commit 5fdacad (v9, with the wing raised about 10 in). That model was used as a **dimensional and shape reference**.

How it was done:
1. `tools/own_models/ea300_measure.py` (Blender) loads the v9 GLB and records numeric measurements only. Output: `tools/own_models/ea300_ref_tables.json`. The measurements are:
   - Fuselage cross-section rings (121 stations × 64 rays, tagged body/glass/frame).
   - Wing, stab and fin aerofoil sections (41 chord points each) and the hinge lines.
   - The gear leg, tail spring and pant sections.
   - The tyre profile, the aileron-spade outlines, and the spinner profile and prop position.
2. `tools/own_models/build_ea300.py` builds **all-new meshes** by lofting through those tables.
   - No vertices, faces, UVs, normals, materials or textures from the FG model are imported, copied or reused.
   - The paint is the approved v9 N300UY TALKFLYING/Sunrise scheme, re-derived procedurally on our own geometry.
3. `tools/own_models/ea300_compare.py` measures the result against the reference. It uses area-sampled nearest-surface distance in both directions, and leaves out points hidden inside either fuselage.

Measured deviation (metres; max / p99), v2 against FG v9:
- Within 1 in at max: canopy glass and frame, fuselage (ref→ours 0.021), stab, wheel pants, spinner, gear tail, fin, elevators (0.036 max, p99 ≤ 0.02).
- Over 1 in, all local:
  - Wing root/trailing cap at the inboard aileron end (0.112).
  - Fuselage tail end cap under the stab (ours→ref 0.112, p99 0.031).
  - Rudder bottom fairing (0.060).
  - Aileron spades (0.060).
  - Tyre sidewalls (0.060).
  - Main gear leg root (0.041).
- Silhouette outline deviation at 4 mm/px:
  - Side: max 34 mm, p99 18 mm.
  - Top: max 60 mm, p99 24 mm.
  - Front: max 48 mm, p99 24 mm.
  - 99.3–99.9% of the outline is within 25 mm.
- Not reproduced: the FG cockpit interior and engine detail (we use a simple cabin tub).

Licensing status: **unresolved.** A shape deliberately fitted to a GPL model may be treated as a derivative work. See the AGENT-LOG.md NEEDS YOU entry, which asks Claude to assess this, with the option of asking Patrizio Melis for permission. The previous fully clean-room v1 is commit 638c05a.

## v3 (2026-10-26, overnight agent pass)
Same method: measured the FG v9 reference into number tables (`ea300_measure.py`), then rebuilt every surface from those numbers. No FG geometry, UVs or textures are copied.
- Wing: one loft from root to tip. It is full chord inboard and steps to the hinge line at the aileron's inboard end. This removes the internal full-chord end cap (was 11 cm). Wing station 2.498 had read the spade strut as wing skin; it is now rebuilt from the stations either side.
- Aileron spades are redrawn from measurements: a 7 mm plan-outline plate, the wedge-shaped weight, and a slanted strut lofted through 6 z-levels.
- Rudder: added the steering horn bar and two 4 mm cables, measured from the reference.
- Fin: added a leading-edge root fillet (hull of the measured fillet points).
- Elevators: denser stab stations at the rudder cut-out. The elevator root now starts at y 0.075.
- Gear: section rings resampled at 48 points (was 16).
- Canopy: a smooth coaming tube runs along the glass boundary, replacing the stair-stepped edge. The seat-back boxes were removed and the cabin is now navy. The glass tint is bluer and denser to match v9.
- Compare now ignores fully enclosed points and reports clusters.
- Result: every part is within 2.5 cm in 3D except three:
  - Fuselage nose cap, 7.1 cm. It sits inside the spinner and cannot be seen.
  - Tail wheel, 2.6–2.8 cm. Our tail wheel is a simple cone; the reference is a tyre.
  - Aileron spade-arm top, 2.8 cm.
  The last two are still within 1 inch + 3 mm.
- Silhouettes, % of outline within 25 mm: side 99.98, front 100, top 99.2. The top-view residual is around the tail-cone/rudder root.

## v4 — cockpit interior (2026-10-27)
New `Cockpit` object (built in `tools/own_models/build_ea300.py`, block "v4 cockpit interior"): navy tub (floor + inner side walls), two tandem grey moulded seat shells with 5-point harness straps, a dark-grey stick with boot ahead of each seat, a small arch-top front panel (5 gauges) under the windscreen, the main rear (PIC) trapezoid panel (10 gauges + lower radio stack) on the frame behind the front seat, and light-grey side tube frame. All primitives (boxes, cylinders, extruded outlines) were drawn new from dimensions estimated against the fuselage stations; no FlightGear interior mesh, UV or texture was opened, imported or copied. The layout and colours were matched to public photos used as visual reference only (not shipped): the Extra 300L rear panel (i.imgur.com/xWpVN2o.jpg, r/flying), Wikimedia Commons "Extra 330 LX cockpit.jpg" and "1996 EXTRA ... EA 300-L c-n 019 Chuck Coleman". The exterior is unchanged (compare deviations are the same as v3), and the canopy tint is kept. Close-up renders: `tools/own_models/ea300_cockpit_render.py`.

## v5 — cockpit revision (2026-10-27, Ty feedback)
Seats are smaller and lower: the pan is 0.34 m deep (was 0.40), the half-width is 0.17 m (was 0.20/0.19), and the back tops are at z 0.43/0.45 (were 0.56/0.58). Sticks are 0.34/0.36 m tall (were 0.44/0.46), with thinner grips and boots. The front seat moved forward 0.16 m (front edge x −0.60 → −0.44) and its stick moved from −0.33 to −0.25. The front panel moved from x −0.17 to −0.14 to keep clearance from the stick. The rear stick moved from −1.20 to −1.24.
New rear dash top (glareshield) behind the front seat. It has the angular-cornered outline of the 300L rear panel photo (0.70 m wide, angled 0.13 m corners, 0.44 m flat top, 0.12 m tall) and is 0.20 m deep fore-aft (x −1.10 to −0.90, z 0.46–0.58). The rear instrument panel now hangs under it as a 0.66 × 0.16 m panel with 2 rows of 5 gauges (z 0.30–0.46), and the radio stack sits below. All geometry is new primitives. The exterior, paint and canopy tint are unchanged (the deviation is the same as v3/v4). Renders come from `tools/own_models/ea300_cockpit_render.py`, which adds an ortho side cutaway clipped at the centreline.
Interior materials are matte (roughness 0.9, metallic 0, specular 0), per the correction.

## v6 cockpit layout (2026-10-27)
Owner moves: both seats down 0.102 m; rear panel + dash top + radio stack down 0.152 m and aft 0.254 m as one unit; both sticks down 0.305 m (same length); front seat + stick aft 0.254 m; dash-top upper faces flat matte black (sRGB 39 ≈ 0.02 linear, roughness 0.9).
Fixes made so nothing collides or pokes through the skin: cockpit floor 0.17→0.068 and the old Cabin plate moved under it; the hidden wing carry-through inside the tub (|y|<0.30) removed, with navy side ledges over the remaining strips; navy stick wells below the floor (lowest z −0.158, belly ≈ −0.5); front seat recline 22→17° to clear the dash top; rear stick 0.115 m further aft to clear the moved panel; radio stack trimmed to fit under the panel; dash top/panel narrowed 0.03–0.04 m per side to clear the canopy glass; tub walls and side tubes inset to the sill line. BVH check: no cockpit overlap with the fuselage skin or canopy. Exterior, paint and tint unchanged; compare deviations same as v5.

## v7 cockpit layout (2026-10-27)
Owner moves: both seat pans down 0.152 m with each seatback top kept in place (vertical back extension + bolsters down to the pan); rear panel + black dash top + radio stack, front seat and front stick forward 0.152 m together; both sticks 0.102 m longer with pivots unchanged.
Fixes: cockpit floor lowered 0.068→−0.084 (no separate seat wells); Cabin plate, side ledges and radio stack follow the floor. BVH check: no cockpit overlap with the fuselage skin or canopy; lowest cockpit z −0.158 (belly ≈ −0.5). Exterior, paint and tint unchanged; compare unchanged.

## v8 baggage area + lettering (2026-10-27)
- Baggage area behind the rear seat: navy matte shelf at z 0.10 from x −1.80 to −2.15, a front lip down to the floor, the existing tub side walls, and an aft bulkhead at x −2.15 following the fuselage ring (inset), so the aft fuselage cannot be seen through the canopy. BVH check: no skin or canopy overlap.
- Fuselage sides: TALKFLYING → TALKFLYING.COM (art/ea-talkflying-com.png), navy, between the two lower red stripes at the same placement. Letter height 0.21 → 0.19 so it fits aft of the wing root; forward end unchanged. Nimbus Sans Bold (URW; licence not verified at the time, replaced in v9 by Liberation Sans Bold, SIL OFL 1.1), the closest match to the existing side wordmark, horizontally scaled ×1.044 to match it.
- Wing underside: TALK | FLYING → TALK FLYING | PODCAST, split across the wings, in Archivo Black (OFL) sheared 0.15, navy with a red outline like SUNRISE AVIATION. Both wings use the same letter scale (69 % of SUNRISE height), with PODCAST centred (art/ea-talk-flying.png, art/ea-podcast.png).

## v9 two liveries + OFL side font (2026-10-27)
Same geometry, two liveries; `EA_LIVERY=tf|n300uy blender -b --python tools/own_models/build_ea300.py` (default tf).
- **Talk Flying** (`extra-300-like-own.glb`, the file the game already loads): as v8, plus the Talk Flying wings-and-headset mark (art/ea-tf-badge.png) on both sides of the fin in the white field between the red line and the tailplane, sized to the largest rectangle that fits (0.90 × 0.23 m), reading the same way on both sides (not mirrored). The mark is cropped from the project's own `unity/Assets/_Project/Resources/Decals/talk-flying-logo.png` (same crop as the 2-33 TF badge in tools/talkflying_livery.py, wordmark and PODCAST excluded), its black field keyed out by luminance and recoloured navy.
- **Plain N300UY** (`extra-300-like-own-n300uy.glb`): no Talk Flying branding. Fuselage sides read N300UY (art/ea-n300uy.png), navy, centred between the two lower red stripes, same height as the wordmark. Wing undersides carry no lettering; instead a red bar sits behind the navy leading edge (after the same 0.03 m white gap), 15 % of local chord deep at the root tapering linearly to 7.5 % at the tip. SUNRISE / AVIATION on top unchanged.
- Side lettering font: Nimbus Sans → **Liberation Sans Bold** (Liberation Fonts 2.x, Red Hat, **SIL Open Font License 1.1**), Helvetica/Arial-metric; best shape match to the old wordmark among OFL candidates (IoU 0.81 vs Inter Bold 0.81 / Work Sans 0.81), scaled ×0.962 horizontally to keep the old wordmark's width per letter. Wing lettering stays **Archivo Black** (Omnibus-Type, **SIL OFL 1.1**). Arimo (Apache 2.0) deliberately not used.
- Four-view renders via tools/own_models/ea300_fourview.py (Blender, ortho top/bottom/side/iso + fin close-ups).

## v10 Ty feedback (2026-10-27)
- Stab (both): navy bar aft edge and red line are now a constant chordwise offset (0.32 × mean stab chord) from each side's LE, parallel to it, so there's no V at the root.
- Side lettering (both): centred fore-aft between wing-root TE and stab-root LE (x −2.47), vertically centred between the lower red stripes, height 0.19 (fits, no shrink).
- Second thin red stripe (both) under the lower stripe: 0.025 m, 0.02 m white gap, parallel (as on the real N300UY).
- Plain underwing bar: 22 % chord at root, same 50 % taper (11 % at tip).
- Propeller: 3 blades at 120°, same paint (white, 5 in red tips, 1 in nickel LE). Unity render log: 4/4 control surfaces, 1/1 propellers bound.
- TF fin badge: vertically centred between stab top and the cap's red line; shrunk slightly to fit (0.81 × 0.21 m).

## v11 (2026-10-27)
- Stab/elevator paint (both liveries): navy aft edge and red line are now straight spanwise lines at constant x (no sweep), placed where the v10 line crossed mid-semispan.
- Cowl: two round inlets opened either side of the spinner (faces cut from the lofted skin), with white lips and short dark ducts.
- Engine: original, simplified flat six drawn from primitives in build_ea300.py (no third-party mesh), sized from published Lycoming O/AEIO-540 data (bore 5.125 in, overall width ~33-34 in). Black crankcase/sump, 3 finned grey cylinders per side (barrel + head fins, darker core), blue rocker covers top and bottom, intake runners and exhaust stubs, front/inboard baffles, prop governor on the front-left of the case. Lycoming numbering: right bank (#1/3/5) forward, left bank (#2/4/6) 0.13 m aft; pitch 0.25 m. The stagger and pitch are estimated from the cylinder fin diameter (~0.19 m), not taken from a drawing.
- Overlap check: the engine has no faces through the cowl skin; the only contact is the intended inlet-lip/skin seam. Unity render log: 1/1 propellers bound.
