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
