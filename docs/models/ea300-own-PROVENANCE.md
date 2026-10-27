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
