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
