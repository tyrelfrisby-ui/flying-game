# VR cockpit Phase 2 — original geometry, review pending

2026-11-02. Builder frame: X forward, Y pilot-left, Z up, metres. No C#,
Glass Overlay, packages, ProjectSettings, Unity, device or deploy changes. No gate approved.
The published aircraft previews were made by the CPU post-processor from committed Phase 1
assets. Gear is the explicit CPU fallback. **Blender export/rebuild equivalence and all twelve
renders remain unexecuted.** Per task instruction Blender was not started in this sandbox.
Standalone Blender-bundled Python with NumPy runs successfully.

## Reproducible geometry path

Both `build_pitts_s2b.py` and `build_ea300.py` already end in `vr_cockpit.update(OUT)`.
That entry point now always calls `vr_phase2.update`, even when Phase 1 is already present.
The fresh Pitts Phase 1 construction also calls the same `vr_phase2.canopy` function: the
old tall-bow and long-rod construction has been removed from source. Phase 2 replaces named
nodes/meshes **in place**, without removing or renumbering a node. It checks the graph before
saving. A second update is byte-for-byte inert. A fresh pre-Phase-1 `0a1a7b4` base
was replayed through the current builder postprocess for Pitts and Extra: both passed
graph validation and produced Phase 2 nodes/low bows. This exercises the fresh-export
postprocess path without asserting that Blender itself ran. No manual GLB edits are required after building.
`pilot_gear.py` constructs all its own geometry, skin and morph; Blender imports that common
numeric source and exports the final gear GLB. The `--offline` option explicitly identifies
CPU fallback output in asset extras. Rebuilds must be run outside this sandbox before approval.

From this worktree, exact operator commands:

```sh
export PYTHONDONTWRITEBYTECODE=1
zsh tools/own_models/rebuild_vr_phase2.sh
/Applications/Blender.app/Contents/Resources/5.2/python/bin/python3.13 tools/own_models/check_vr_phase2.py --write-report
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python tools/own_models/render_vr_phase2.py -- /Users/tyfrisby/Developer/agent-lanes/aero-playground/vr-cockpit/renders/phase2
```

The rebuild script runs all four `PITTS_LIVERY` values (N31DG, N1191,
CHECKER-UNMARKED, FACTORY-UNMARKED), all three `EA_LIVERY` values (tf, n300uy, unmarked),
then the gear Blender builder, then validation. It supports `BLENDER_BIN` and
`NUMERIC_PYTHON` overrides. It does not build the game, deploy or render.
Inspect any regenerated GLB diffs and rerun validation after Blender exports; CPU preview
success is not evidence that Blender export/import succeeded. The checker validates all
seven aircraft, the gear, graph reachability/cycles/unique parents, names, rig tags, skin
weights, morph names and clearances. See `vr-cockpit-phase2-checks.json` for current numbers.

## Pitts canopy correction

Ty's direct description and the Phase 2 CONTRACT supersede the Phase 1 interpretation.
The two low bows are at X −0.835 / −0.875 m (40 mm apart), centre crown Z .218 m.
Tube radius .00635 m (12.7 mm OD). Their transverse half widths are .347091 / .347664 m;
rail endpoints Z .16065 / .16025 m. Vertical rises .05735 / .05775 m, following a
2.4-power superellipse, **not a circular arc with a single bend radius**. These are
modelling dimensions, not measured Pitts factory dimensions. Ends meet the existing
canopy sill rail volume. A small .053 × .100 × .008 m bridge spans the crowns.
The short pin is 35 mm long, 8 mm diameter, Z .183–.218, X −.855. Its top belongs to
the canopy bracket. The black ball is 28 mm diameter at X −.865, Z .205; Y −.0381 is
pilot-right/locked. Travel .0762 m moves it to +.0381 (pilot-left/unlocked).
White Delrin transverse channel opens at its left end, so the unlocked pin clears the
channel; small stepped lips near the right end show a notch. Detailed detent geometry,
bracket design, pin translation linkage and track mushroom hardware need Ty's reference.
This is an owner-description visual study, not an airworthy latch design.

`Canopy` still owns both bows, bridge, pin, glass and frame. `Canopy_Slide` moves aft
101.6 mm over canopy_open 0–.2; `Canopy` hinges right 0–100° over .2–1. These existing
travel estimates require Ty's confirmation. Knob/block stay on panel. Open only after
unlocking. Intentional closed pin/channel engagement is excluded from bow-clearance tests.

Closed conservative bow clearances: panel **64.65 mm**, instruments **104.84 mm**,
IAC card **174.61 mm**, assumed 280 mm head/helmet sphere **310.56 mm**, both stick
full-control enclosing volumes **228.59 mm**. Centreline sampling ≤1 mm, minus tube
radius and .5 mm sample bound. Helmet envelope is a stated estimate at the authored eye,
not a measured Ty helmet or all possible head lean positions. Control check transforms the published mesh of both
sticks at 1° pitch/roll increments and encloses each in a box dilated by the maximum
vertex movement between samples (both rotation axes), covering intermediate control values. These checks concern the new bows, not every possible cockpit collision.

For the **entire continuous opening sweep**, minimum bow-to-panel separation is at least
**21.15 mm**: rotation about the fore-aft X axis preserves every bow vertex's X, while
aft translation increases its separation from the panel. Thus a separating plane exists
at every opening value, including between angle samples. This is a stronger proof than
checking a few sampled pictures. It does not certify clearance to the front-seat occupant,
all surrounding fuselage geometry or the original glass during opening.

## Red star in side view

Both render scripts now filter **every `Sight_` object in every non-eye shot**.
The unchanged sight centres are at builder Y ±1.85179 m; wingtip mesh reaches ±3.047 m.
They are **not physical wingtip-mounted hardware**: the prior owner-approved design is
floating cockpit-only references centred on the eye line inside the wingtips. See
[sights-PROVENANCE.md](sights-PROVENANCE.md), round 3, and `sights-spec.json` (body lateral
station 1.850 m). Their side projection produces the red star over the cockpit. This is a
render-only visibility artefact, not a reason to relocate those existing sights.

## Harness choice and uncertainty

Both aircraft, both seats: `Harness_Rear/Front`, `Harness_Buckle_Rear/Front`,
`Grip_Harness_Release_Rear/Front`, `Harness_Strap_*`, plus explicitly separate
`Harness_Buckle_Secondary_Rear/Front`. Five primary points: two lap, two shoulder and
one crotch strap. A **second independent lap belt** has its own two webbing pieces and
release. Straps are flat 44.45 mm-wide webbing (width used as a modelling estimate).
Roots stay with the aircraft seat when the pilot exits. Meshes pivot away/down on
`harness_latch` 0 latched .. 1 released. This is visual fall-open travel, not simulated cloth.

Pitts: Hooker-style five-point/double-lap arrangement with simplified lift-lever release
fittings. [Hooker's aerobatic system](https://www.hookerharness.com/aerobatic-system)
names Pitts and Extra as common applications. [IAC's Hooker discussion](https://www.iac.org/in-the-loop-safety-series-how-to-fasten-a-hooker-harness)
explicitly discusses two belts, keeping all vertical straps on the same ratchet-belt buckle,
and shows the author's S-1 setup that he also used in his S-2B. This establishes the
arrangement, not the exact release fitting/installation in Ty's aircraft. No unsupported
claim of a universal Pitts buckle or factory anchor station is made. Ratchet detail and
certified anchor topology are not modelled; current routing/hip locations require review.

Extra: [Patty Wagstaff's Extra 300S egress card, ICAS](https://airshows.aero/ExtractDocs/35/)
was independently found by indexed document text: five-place harness, two quick releases,
**bottom left / top right**. Model has lower secondary lever swinging pilot-left and upper
primary lever pilot-right. The 300S card supports the directions, **not exact two-seat early
EA300 installation geometry**. Release angle 65° and buckle details are estimates.
Pitts levers lift 80° (estimate). Seven strap meshes per seat; shoulder pivots fall forward,
lap halves fall outward/down, crotch strap falls toward seat pan.

The seam currently defines one `harness_latch`. It visually releases both fittings together.
Claude must track independent release states in interaction before treating the pilot as free;
he can independently drive the named primary/secondary nodes and lap strap groups. A single
visual parameter must not silently imply that two real releases are one physical buckle.
Ty: lever-latch or cam-lock on your Pitts? Ratchet location? Confirm both aircraft's anchor
routing, belt colours, lap belt order and actual Extra release fittings before approval.

## Headset choice

Original H10-13.4-style over-ear headset, sage cups, black seals, metal band, flex mic.
[David Clark's own specification](https://www.davidclarkcompany.com/aviation/fixed-wing-passive.php)
specifies the mono H10-13.4's **five-foot straight cord and dual plugs**. Chosen GA
PJ-055 phone (6.35 mm) and PJ-068 mic (5.2324 mm) pair, **not LEMO or helicopter single plug**.
Headset_Cord length **1.524 m**, rope diameter **4 mm**. Length is sourced; diameter,
earcup 88 × 54 × 122 mm, 200 mm band width / 125 mm rise, 32 mm plug spacing,
23 mm exposed shafts and 34 mm barrels are explicit geometry estimates, not published DC
dimensions. Headset is a recognisable original approximation without branding or copied mesh.

`Headset_Plug` is the assembly root for **both** `Headset_Plug_PJ055` and
`Headset_Plug_PJ068`; `Grip_Headset_Plug` is on it. Both must disconnect for the current
single-root interaction. `Headset_Cable_Root` is at the left earcup cable exit.
Material `HeadsetCord` is present on plug barrels/mic/band parts so export retains it;
Claude uses it for the simulated rope. There is no static runtime cord mesh. The render
script adds a clearly named `HeadsetCord_RenderStandin` only for the plugged evidence shot;
it illustrates routing and does not establish a rope length or physical restraint.

`Headset_Jack_Rear/Front` are real mesh plates, with two black socket meshes each.
Provisional position: hip +(.10,−.27,.29), pilot-right cockpit sidewall.
**No reliable primary reference established the exact jack station on these aircraft.**
Ty: where are both pairs in your Pitts and the intended Extra? These positions are review
proxies, not asserted factory locations. Hole sizing/plug engagement is simplified.

## Emergency parachute choice

Original Mini Softie-style back container, 15-inch 240 version: **15 W × 23 H × 2.25 D in**
(.381 × .5842 × .05715 m), per [manufacturer dimensions](https://softieparachutes.com/parachutemodels/minisoftie/).
Separate visible pack flaps, main lift webs, chest strap and leg loops.
A red-finished open D-ring is on the **wearer's left main lift web**, visible looking down.
[Softie's manufacturer manual, §4.3.11](https://www.softieparachutes.com/doc/Softie-Packing-Instruction-Rev2.0-08-09.pdf)
locates both the housing and handle pocket there. D-ring size (50 × 54 mm centreline) and
finish are estimates; cable housing is a visible 7 mm OD approximation, not functional pins.

Round/conical emergency canopy in the **26 ft / 7.9248 m nominal class**, using
[Strong Lo-Po Lite's specification](https://strongparachutes.com/Emergency/lo-po-lite-canopy)
for nominal size and conical type. Softie's manual lists Strong Lo-Po variants among
compatible canopies; this is a visual choice, not certification of a specific assembled rig.
Rendered projected diameter is 7.9248 m as an **art estimate**; nominal cloth diameter
and actual inflated projected diameter are different measurements. Dome rise 2.1 m,
line stretch 4.05 m from riser top to skirt, 32 sectors/lines, .30 m pilotchute radius,
vent and scalloping are simplified art dimensions, not claimed Strong manufacturing data.
Ty: is this the pack/canopy family you want? Supply a different actual rig if desired.

`Parachute_Deployed` defaults to **local scale zero**, with `default_visible=false` extras.
Activate by setting local scale (1,1,1), then animate `Parachute_Canopy` morph
**Inflate 0..1** from narrow line-stretch to full dome. Packed container stays visible until
Claude opens it. Roots `Parachute_Pilotchute`, `Parachute_Lines`, `Parachute_Risers` can
be staged independently. Lines/risers are full-length static visual meshes; scale/reveal them
during deployment in code. Inflation does not simulate fabric aerodynamics. Fully inflated
canopy fabric is double-sided and has an open apex vent.

Body gear is rigidly skinned to a `Chest` joint using the Phase 1 Chest head
(−1.335,0,.005). Runtime must **remap the gear skin joint to pilot-body Chest**, retaining
the authored inverse bind, or retarget into the same body frame; do not apply the Pitts
seat offset a second time. Gear body meshes use world bind-space positions as glTF skins
require. Gear `Chest` extras declare the attachment. Headset is a separate camera attachment;
reset/align it using its authored eye (−1.325,0,.30). Plug and deployed roots are independent
of Chest so they can be constrained to aircraft / fall away / deploy. Extra needs the same
body retarget transform as its pilot; this asset does not hard-code an Extra seat offset.

Geometry: **3,780 gear triangles**, plus Phase 1 pilot body's 2,416 (6,196 combined).
New harnesses/jacks add **680 triangles per aircraft** (both seats together).
Current entire airframes: Pitts 63,198 checker / 64,490 factory, Extra 124,866 triangles;
those include pre-existing airframe detail and are not claims of a completed Quest performance
audit. The new gear has one rigid Chest skin, one Inflate target, no downloaded geometry,
texture or package. Released harness and deployment are visual previews until Claude binds them.

## Twelve requested renders / Ty review

All `after2_*.png`, 1600×1000, generated only when the operator runs the command above:

- pitts_eye: rear-eye panel, low bows, card visibility and posed body scale.
- pitts_bows_closeup: low rail-to-rail curvature, spacing, bracket, short pin and right=locked notch.
- pitts_latch_unlocked: 3-inch pilot-left knob/pin travel and channel exit.
- pitts_canopy_open: whole canopy/bows slide aft then hinge right; check physical path and fixed windscreen boundary.
- pitts_frame: side x-ray tube arrangement with skin and all sights hidden; no red star.
- harness_latched: primary five-point routes plus distinct lower independent lap belt.
- harness_released: both releases and all seven strap pieces fall open; identify exact fitting differences.
- extra_harness: early two-seat Extra routing; agree which fittings match the 300S release directions.
- headset_plugged: right-side provisional twin jacks, both plugs and render-only cord route.
- pilot_lookdown: left ripcord is visible/reachable; distinguish parachute chest strap from aircraft restraints.
- parachute_packed: back pack dimensions, flaps, body straps; confirm actual desired rig.
- parachute_deployed: round/conical inflated shape, line attachment, pilot hanging scale. Body limb pose is
  render-only; chest-bound leg loops retain the seated approximation pending runtime deformation.

All renders remain **pending**, no visual gate approved.
