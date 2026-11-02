# VR cockpit Phase 2 — original geometry, review pending

2026-11-02. Builder frame: X forward, Y pilot-left, Z up, metres. No C#,
Glass Overlay, packages, ProjectSettings, Unity, device or deploy changes. No gate approved.
P2c supersedes the earlier canopy/panel dimensions below. All five local Pitts photos,
the Extra open-canopy photo, and prior frame/eye renders were inspected. CPU previews
were generated only in `/tmp/p2c-models`, preserving committed Unity assets. Seven
liveries pass the revised numeric checker. **Blender rebuild and 21 renders remain
operator work; no gate approved.** No Blender process was started.

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
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python tools/own_models/render_vr_phase2.py -- /Users/tyfrisby/Developer/flying-game-worktrees/codex-vr-cockpit/renders/phase2c
```

The rebuild script runs all four `PITTS_LIVERY` values (N31DG, N1191,
CHECKER-UNMARKED, FACTORY-UNMARKED), all three `EA_LIVERY` values (tf, n300uy, unmarked),
then the gear Blender builder, then validation. It supports `BLENDER_BIN` and
`NUMERIC_PYTHON` overrides. It does not build the game, deploy or render.
Inspect any regenerated GLB diffs and rerun validation after Blender exports; CPU preview
success is not evidence that Blender export/import succeeded. The checker validates all
seven aircraft, the gear, graph reachability/cycles/unique parents, names, rig tags, skin
weights, morph names and clearances. See `vr-cockpit-phase2-checks.json` for current numbers.

## P2c — Pitts photo correction (supersedes previous Phase 2 geometry)

`panel_curve` derives each outline from `build_pitts_s2b.ST` / `sec` at X −.800 rear,
−.145 front: actual upper skin section, inset **25 mm along its normal**. The builder's
2.4-power skin section is inherited; the previous independently scaled squarish crown
and vertical sides are gone. The outline runs continuously from one side through the
crown to the other, closed by a straight lower edge. No fixed windscreen or bubble
vertices move. The old front hexagonal plate is removed from the combined Cockpit mesh.
White-painted panels and 5 mm diameter polished edge tubes (metallic 1, roughness .16)
follow the full perimeter. Estimated rear panel width 741.3 mm, height 363.7 mm,
bottom Z −69.4 mm, crown Z 294.3 mm; front width 808.7 mm, height 322.8 mm,
bottom −123.5 mm, crown 199.2 mm. Plate thickness rear/front 15/12 mm.
Rear/front dial perimeter margins are 4.39/109.00 mm minimum; the card retains its original visible
placement and may project below the straight panel bottom. Layout is otherwise unchanged.

Each panel has two 10 mm OD white tubes, 35 and 75 mm aft of its station (40 mm apart).
Their curve is the **22 mm outward normal offset** of the panel arch, truncated at the
moving sill rail; short endpoint segments meet the actual rail centreline. This is a
constant curve offset except the rail landing transitions, not a circular bend radius.
Rear tube stations −.835/−.875; front −.180/−.220 m. Crowns Z 316.3/221.2 mm.
Centreline offset minus 5 mm tube radius and 2.5 mm rim radius gives 14.5 mm nominal
radial surface gap, before accounting for the aft offset. All are estimates from photos.

At each crown: a 55 × 110 × 24 mm block and 53 × 100 × 5 mm bracket at
X panel−55 mm, Z bow crown (bracket 14 mm lower). A 6 mm diameter ×12 mm internal
catch is fully hidden inside the block. Knob ball diameter 28 mm, stem diameter 6 mm;
ball centre X block−22 mm, Z crown+25 mm. It moves from Y −38.1 to +38.1 mm:
**76.2 mm leftward = unlock**, unchanged rig parameter. Block, bracket, pin, knob,
placard and both bows move with `Canopy`; `Grip_Canopy_Latch` rides the ball.
Placard is a simplified red/white 50 ×17 mm face (white band 46 ×4 mm), with no invented
operating text. Front assembly duplicates rear names with `_Front` suffix.

The tall red intermediate hoop at X −.47 was inside the bubble, not its end edge, and
is removed. `Canopy_Frame` now consists of 28 mm OD sill rails and 22 mm OD front/rear
bubble-edge arcs at X −.135/−1.595, using the unchanged bubble loft profile. The rear edge
is still taller than the panel bows because it follows the bubble's own edge. The fixed
windscreen geometry stays unchanged. Retained opening estimates: 101.6 mm aft slide
through canopy_open .0–.2 then 100° right hinge through .2–1.

CPU conservative closed bow clearance, both pairs together: panel **29.72 mm**,
instruments **124.58 mm**, sequence card **271.33 mm**, two 280 mm helmet spheres
**134.98 mm**, both stick full control enclosing volumes **245.40 mm**. Sampling and
control bounds retain the prior ≤1 mm centreline / 0.5 mm deduction and 1° control grid
with angular inflation. Panel opening sweep separation (both bows, block and ball)
**20.00 mm** minimum using continuous X separation; helmet X separation throughout
slide/hinge **77.40 mm**, full-control stick envelopes during opening **221.44 mm** (0.005 opening
steps, dilated by maximum between-step slide/rotation displacement). These concern specified added geometry and proxies; visual
card sightline, all fuselage/bubble collisions and head leaning remain Ty/operator review.

## P2c-2 — Extra latch v2

Ty's supplied open-canopy photo and fore/aft mechanism description supersede L/R
rotating paddles. Both cockpit pairs mount directly to the moving port canopy frame,
selected from actual low frame vertices near X −1.55 rear / −.70 front. Frame centre Y/Z
are derived per export, not hard-coded world points. Handle centres are ±45 mm fore/aft,
18 mm inboard and 30 mm below that rail point. Handles use 12 mm OD stems and
14 ×27 ×55 mm red grips, with 16 ×29 ×20 mm white inserts 12 mm below their centres.
Rear handle fixed; front slides **35 mm aft** on canopy_latch 0 released .. 1 squeezed.
Compatibility grip moves **17.5 mm aft**, staying halfway between the handle grips.
Two 6 mm diameter ×24 mm pins per pair (four total), at rail X ±70 mm, Y rail centre,
Z rail−12 mm, retract **18 mm upward**. Matching airframe socket rings sit another
6 mm lower, with 10 mm centreline diameter and 3 mm section diameter. Rest pins cross
the socket plane, squeezed tips clear it by at least 10 mm. Linkage is simplified,
not copied measured hardware. All placement/travel/dimensions are estimates.

`Canopy_Latch_Handle_Aft/Fwd` and corresponding `Grip_Canopy_Latch_Aft/Fwd` are rear
names; front names append `_Front`. `Canopy_Latch_Pin_1/2` and matching
`Canopy_Latch_Hole_1/2` also have front duplicates. Handle White children follow handles.
All handles/pins/grips are canopy children; holes remain airframe roots. Legacy
`Canopy_Latch_Handle_L/R` (and front duplicates), plus `CanopyLock_F/R`, remain inert
compatibility empties with obsolete mesh/rig removed. `CanopyRelease_F/R` are distinct
right-sill emergency jettison pulls in ea_rig.py, so remain unchanged on the airframe.
Rig JSON contains Fwd/pin translation and midpoint travel; no C# changes.

Opening sweep tests sample 0–100° every .5°, latch 0–1 every .1, subtract both maximum
between-sample displacements. Proxy helmet radius 140 mm at (−1.72,0,.62) and
(−.82,0,.62); resting control hand radius 65 mm at (−1.52,0,.04)/(−.62,0,.04).
Minimum rear/front helmet gaps **46.67/105.31 mm**, hands **280.10/325.26 mm**.
Intentional latch grasp contact is excluded. These proxies do not certify arbitrary hand
poses. Checker verifies pins engage at rest and leave socket bounds when squeezed.

## P2c operator renders and review

Nine additional files, output directory supplied as CLI argument:
`after2c_pitts_eye`, `after2c_pitts_panel_front_on`, `after2c_pitts_latch_locked`,
`after2c_pitts_latch_unlocked`, `after2c_pitts_canopy_side`, `after2c_pitts_frame`,
`after2c_extra_latch_closed`, `after2c_extra_latch_squeezed`, `after2c_extra_canopy_open`
(all `.png`). Eye camera matches Phase 2; front-on camera is horizontal and centred.
Extra closed/squeezed share one camera; opened view restores glass and shows canopy-frame
attachment. Existing twelve Phase 2 renders remain available, now of corrected geometry.
Compare panel/rim and low arcs with Pitts refs 3–5, bubble edge/sill with refs 1–2,
black ball's right/left position in latch closeups, and Extra moving frame/fore-aft handles
with its photo. No visual approval until operator renders are reviewed.

`CONTRACT.md` in this worktree copies the supplied contract and appends implementation
names. The supplied copy in the other worktree was read only; operator can reconcile
this addition into the shared handoff. All report numbers currently describe CPU previews,
not a Blender export. `VR_PHASE2_MODELS` optionally selects a temporary aircraft directory;
normal rebuild/check commands use the exported Unity model directory.

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
Historical Phase 2 airframes before P2c (superseded by JSON report): Pitts 63,198 checker / 64,490 factory, Extra 124,866 triangles;
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
