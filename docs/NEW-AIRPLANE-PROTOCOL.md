# New-airplane protocol (owner 2026-10-03)

Every aircraft goes through the same steps, in order. An airplane is not "in the game" until every box is ticked and
the automated gate is green. Applies equally to re-working an existing type.

## 1. Book data (before any numbers go in)
Collect from the POH / type certificate data sheet / reputable references, and write the sources at the top of the builder script:
- Span, wing area, length, height; airfoil if known.
- Empty and max gross weight; the weight we fly at; CG.
- Engine power and RPM; prop diameter; fixed-pitch or constant-speed; number of engines.
- **Speeds**: Vso, Vs1, Vx, Vy, best glide (speed **and** ratio), cruise at 75 %, Vne, Vfe per flap setting.
- **Flaps**: type (plain / slotted / Fowler / split), settings (e.g. 10° / 20° / 30°), Vso at full flap.
- **Controls**: reversible (cables) or powered; centering springs (which surfaces, how strong); trim type (tab / spring) on each axis.
- Gear: tricycle / tailwheel / retractable / floats / hull; ejection seat.

## 1b. Design the airplane from its three-view drawing
Work from a dimensioned three-view (manufacturer drawing, TCDS, or a scaled published three-view). Scale it by the known
span, then measure:
- **Wing**: span, root and tip chord, sweep of the quarter-chord line, dihedral, incidence (rigging angle), washout, and
  the aileron span and chord fraction (inboard and outboard ends). Note the flap span, chord fraction and type.
- **Strip layout**: split each half-wing into strips (8–10 per side, finer toward the tip and at the aileron and flap
  ends). Each strip gets its quarter-chord position (x aft-positive from the datum, then rebased to the CG; y spanwise;
  z from the dihedral and the high or low wing), chord, area, and airfoil. Control strips (aileron, flap, elevator,
  rudder) carry their hinge-line chord fraction (gain).
- **Tails**: horizontal tail area, span, root and tip chords, elevator chord fraction, and the tail arm (quarter-chord of
  the wing to quarter-chord of the tail). Check the **tail volume** V_h = S_h·l_h / (S·c̄). Typical values are 0.5–0.7 for
  light aircraft and about 0.35–0.45 for aerobatic types. Do the same for the fin and rudder (V_v ≈ 0.03–0.05).
- **Fuselage**: length, maximum width and height, and the side-view and plan-view projected areas and their centroids. These
  feed the crossflow and side-force terms and the parasite drag (cd0Area ≈ C_D0 of the fuselage, gear, struts and cowl
  times the reference area — calibrate it against the book cruise speed and best glide).
- **Gear**: wheel positions from the side and front views (main track, wheelbase, tailwheel or nosewheel), and the static
  attitude (the three-point angle for a taildragger).
- **Engine and prop**: thrust-line height and angle, prop diameter, and position (the nose, or the nacelle positions for twins).

## 1c. Mass and moments of inertia
- **Mass breakdown** (empty weight split into components): wing ≈ 10–14 % of gross, fuselage ≈ 10–12 %, tail ≈ 2–3 %,
  engine and prop (book dry weight + about 15 % for mount and accessories), gear ≈ 4–6 %, fuel, occupants and baggage at
  their stations. Place each item at its own position from the three-view.
- **CG**: Σ(m·x)/Σm. It must fall inside the POH envelope at the weight we fly. If it doesn't, the positions are wrong;
  don't move the CG by hand.
- **Inertias**: sum each component as a point mass plus its own shape term:
  - Ixx (roll) is dominated by the wings: a wing panel of mass m and span b/2 gives about m·(b/2)²/3 per side; add the
    tip tanks or engines at their y² (twins: the nacelles dominate).
  - Iyy (pitch) is dominated by the engine and the tail at their x² from the CG, plus the fuselage as a rod (m·L²/12).
  - Izz (yaw) ≈ Ixx + Iyy for a conventional light airplane (Izz ≈ Ixx + Iyy − small); never less than either.
  - Ixz: small (0 for most light aircraft unless the engine sits well above or below the CG).
- **Sanity check** with radii of gyration (R = 2·√I/(m·b) for roll, with fuselage length L for pitch): typical
  R̄x ≈ 0.25–0.30, R̄y ≈ 0.35–0.40, R̄z ≈ 0.38–0.45 (Roskam Part V). Then fly it: roll response to full aileron, the
  phugoid period (≈ 0.135·V s for V in kt), the short period and the dutch roll should all look like the real airplane.
- **Prop inertia**: blade mass × (0.6·R)² × blades + crank and flywheel (about 1–2 kg·m² for light singles). It drives the
  gyroscopic precession and the RPM response.

## 2. Config
- Build it with a script in `tools/aircraft_builder/build_<type>.py`, derived from the closest existing type. Never
  hand-edit generated numbers; change the script and re-run it.
- File name = id: `configs/aircraft/<id>.json` (id ends `-like`; no brand names in the display name).
- Flaps: put a `flap` block on the inboard (non-aileron) wing strips if the real airplane has flaps.
- Idle prop drag (`propulsion.idleDragCd`): the default is 0.15. Set the type's own value if its **published power-off glide**
  isn't matched (e.g. Extra 0.08, P-51 0.05). Never zero it to make a test pass.
- Copy the file into `unity/Assets/StreamingAssets/aircraft/`. It must be byte-identical; the gate checks.

## 3. Model (free only, until the app makes money)
- Licence: CC0, CC BY or Sketchfab Free Standard only. Save `assets-incoming/models/<id>/LICENSE.txt` and add a line to `CREDITS.md`.
- Import the GLB to `unity/Assets/Resources/Models/<id>/<id>.glb` (glTFast), then add `AirframeModels.Specs` (forward and up axes, scale).
- Render check: screenshot it from the chase, side and cockpit views; check ailerons sit at the wing trailing edge.

## 4. Menu
- Add it to the aircraft list (SessionSettings), with a short display name.
- If it has flaps, the lesson page offers UP / HALF / FULL automatically.

## 5. Tests (all must pass)
- `NewAircraftPerformanceTests`: one row with the book Vso, Vy and cruise. The sim must land near the book.
- Special equipment: floats or hull → `FloatTests` / `FlyingBoatTests` / `DisplacementTests`; reversible controls →
  a row in `ReversibleControlTests`; retractable gear, ejection seat, guns → their tests.
- **The gate** (`GlideTableTests`):
  - `EveryAircraftPassesTheNewAirplaneProtocol`: the file name matches the id, the StreamingAssets copy is identical,
    it trims at its spawn speed, Vso is plausible, and every flap setting's idle glide is sane and trimmable.
  - `GlideTableIsCurrentForEveryAircraftAndFlapSetting`: the airplane is in the **idle-glide table**, and the table is up to date.

## 6. Idle-glide table
- Regenerate with `REGEN_GLIDE_TABLE=1 ~/.dotnet/dotnet test --filter GlideTable` (from `tools/FlightTests`).
- This writes `configs/glide-table.json`, copies it to StreamingAssets, and writes `docs/GLIDE-TABLE.md`.
- The landing lessons start on the table row for the aircraft and flap setting: idle, 1.3 Vso, on the power-off glide,
  with the elevator trim from the table. Commit all three files.

## 7. Fly it on every surface (parity rule)
- iPhone, iPad and Mac. Fly the round-out and flare lesson with each flap setting (`AERO_SELFTEST=lesson` for screenshots).
- Check it starts on speed, at idle, in trim (hands off, it holds the glide), and that the speed bleeds in the hold-off.
- Check that no text or UI overlaps in landscape or portrait.

## 8. Ship
Run the full test suite green, commit, push, deploy to all devices, and upload to TestFlight.
