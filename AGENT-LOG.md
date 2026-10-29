# Agent log — codex/own-ea300

## NEEDS YOU
- **Visuals pass 4, batch S iteration 8 ready for Chief renders (Codex, 2026-10-28, `lane/codex-visuals`).** P4-S7 water/world ERRORS empty. Incoming sunset **6/10**, hero **6/10**, idle **5/10**, day sky/water **4/10**, shore reflection **7/10 correctness**. S8 fixes hero bearing at 25 degrees aft-left / .65 m height / horizon 45% down; retains 78-degree lens and deterministic full mesh/mirror fit. Backlit airframe is modestly darker, reflected airframe strongly desaturated/darkened. Day reflected clouds use a Fresnel-dependent rough sky evaluation; adds compact idle ripple packets/bow shoulders. Sunset sky palette, shore reflection correction, speed overrides, Kelvin V/step whitewater, spray, haze tiers and waterfall sheets preserved. Chief: compile separate Bridge/Editor assemblies, strict Metal import, full water/world rerender; verify full J-3/Beaver mirror framing, dark backlit reflection, reduced near-water cloud contrast, visible subtle idle ripples and iPad timing. Source checks only; uncommitted. Cliff/peak reflection occlusion, Balanced stairs and procedural scenery remain open.
- **A green test suite is not a build, and it cost us a day's build (Claude, 2026-10-28).** `BiplaneCellule` used `{ get; private init; }`. The .NET 8 suite compiled it and reported 776 passing; Unity's compiler has no `System.Runtime.CompilerServices.IsExternalInit`, so the same file was 30 x CS0518 and the iOS player would not build **at all** from 14:41 to 16:22. Fixed in `141be0b`. **Anyone finishing a change that touches `src/` must run `tools/deploy/deploy.sh` and see `** BUILD SUCCEEDED **`, not just the test suite** -- they are two compilers and two runtimes, and only one of them was being asked.
- **The Great Lakes needs a look, and three things on it are not sourced (Claude, 2026-10-28).** It is in the fleet picker and flies; see the Log. (a) Its **stall is 10 % low** — Vso 51 mph against the manufacturer's 57 — which is a lift limit, so no drag change moves it; closing it needs a lower cellule CLmax and the wing section is not known for this type. (b) Its **inertias are estimates** scaled off the Pitts and the Extra; there is no mass breakdown. (c) Its **CG height** (0.25 m below the thrust line) is derived — a TCDS gives no vertical CG. Also: the GLB is exported with the gear EXTENDED, so until the oleo rig is animated the drawn wheels hang about 0.23 m below where the physics has the aeroplane standing. **Ty: fly it and say whether it feels right**, and if you have a section or a weight-and-balance sheet for the type, those are the two numbers that would close it.
- **Pitts ground handling after the rebuild (Claude, 2026-10-28): three tests are red and I do not think they should be forced green.** With the wing quarter-chords corrected, the drawn main wheels sit 0.39 m ahead of the CG (they were 0.23 m), and the vertical tail lost 12 % of its area, so the Pitts is far more ground-loop prone: the scripted pilot swings 91–97° off the runway on the landing roll (`ThreePointAndWheelLandingsAreWorkable` x2, `PittsNeedsQuickFeetTheExtraForgivesRelaxedOnes`). I tried the tailwheel steering spring (15 → 25/40/60) and its cornering stiffness (2200 → 3200/4500); neither moves it, because the cause is geometric, not a tuning value. Two facts say the aeroplane is right and the benchmark is the stale part: TCDS A8SO says the S-2B moved its "wings and landing gear forward 6 inches" against the S-2A, and the scripted pilot is already faster than human on the rudder (no rate limit on its command; the surface slews at 229°/s, neutral to full in 0.13 s, and `Kr = 6` saturates the pedal at a 9.5°/s yaw-rate error — it runs out of authority, not speed). **Ty: fly a few landings and tell me whether it feels like an S-2B or like a bug.** If it is right, `QuickFeet` wants raising for this type; if it feels wrong, the drawn main-wheel station is the thing to question (Codex).
- **Pitts CG height (Claude, 2026-10-28).** `mass.cg` z is 0.20 m below the datum, derived from what the ground handling needs, because a TCDS gives no vertical CG. The station (x) is now sourced from TCDS A8SO. A mass breakdown per the new-airplane protocol's step 1c would settle the height.
- **Flat-spin smoke check is red (Claude, 2026-10-28), and it is not the flight model.** `AirframeFlowTests.SmokeGoesRoundTheAirframeInTheFlatSpin` reports a marker inside the airframe. Cause: one plate covers a whole tailplane including the part inside the fuselage, so near the root "off the plate" and "outside the body" are mutually unsatisfiable and `KeepOut`'s two pushes swap the marker back and forth. It surfaced because the Extra's tail is now at the arm its drawing gives it, which roots the stabiliser further aft in a thinner part of the body. I had a partial fix (skip a plate where it is buried in the body); it cleared the tail root and then failed the same way at the wing root, so I reverted it rather than leave an unverified change to shared flow code inside this diff. The fix belongs in how AirframeFlow builds its plates — a surface should start at the body skin.
- **The menu overhaul wants a look on a device (Claude, 2026-10-28).** OPTIONS is eight chapters and the landing page five. It ran clean through `AERO_SELFTEST=menu` at all four screen shapes, but nobody has thumbed it yet. Check in particular that a chapter never needs its scrollbar on an iPhone in portrait, and that the "world paused" note reads right.
- **EA300 GPL — decision needed before App Store release (Claude's review, 2026-10-27; see the Log entry below).** Our own EA300 is now the in-game Extra. Claude's assessment: treat the airframe shell (v2–v12) as a likely derivative of Patrizio Melis's GPL FlightGear EA300, and treat GPL content as not safe to ship through the App Store. **Ty: pick one before App Store release:** (a) ask Patrizio Melis (github.com/cobe571) for written permission to use the measured shape in a closed, commercial App Store game (a draft request is in the log entry); or (b) put the v12 cockpit, engine, inlets, prop and paint onto the clean-room v1 shell (638c05a). This does not block TestFlight or internal builds.
- **Pitts / Extra ground handling (Claude, 2026-10-28): please feel-test on a device.** The gear now matches the models and both types were retuned (see the Log entry). Things to try:
  - Pitts: taxi, a full-rudder turn at walking pace, and three-point and wheel landings. It is meant to need quick feet: relaxed rudder on the rollout loops it, active rudder holds it.
  - Both: full brakes at a fast taxi (12 kt). This still noses over (Pitts flips, Extra strikes its prop), while half brakes, or full brakes at a walk, do not. That is realistic, but if it feels harsh on the touch pad, the knob is the global brake capacity in `LandingGear.Compute` (the 2026-09-13 0.675 tune), not these configs.
- **Left over from matching the models (Claude, 2026-10-28): physics left unchanged, decide if wanted.**
  - Pitts: the drawn thrust line is 0.135 m above the sim's CG (it was 0.33 m), so it still appears to pivot a little low. Closing that means moving `mass.cg` z or `propulsion.thrustLineZ`, which is a flight-model change.
  - Extra: the config's tail surfaces sit about 0.5 m ahead of the drawn tail. The config span is 7.43 m against the model's 7.99 m, so the model is drawn at 93 %. Takeoff lift-off is 74 kt, against about 65 kt published for the Extra 330LT.
  - Pitts takeoff roll is 135 m, against 170 m (557 ft) published; acceleration comes from the shared propulsion model.
- **Livery picker (Claude, 2026-10-28): please look at it on a device.** The LIVERY row is now one drop-down for both types: four Pitts schemes (Checkered, Factory, N31DG, N1191) and three Extra (Unmarked, Talk Flying, N300UY). It compiles and the swap is verified in the editor, but nobody has seen it on an iPhone or iPad yet. `AERO_SELFTEST=menu` saves `selftest-menu-<shape>-livery.png` (Pitts selected) for each of the four screen shapes. Also check that the unmarked defaults are what you want a new player to see first.
- **Extra unmarked label (Claude, 2026-10-28):** I named it "Unmarked", not the HANDOFF's "Extra". The AIRCRAFT field right above it already says Extra 300, so "Extra" would repeat it. Easy to rename in `LiveryChoice.Options` and `Livery.UnmarkedExtra` if you prefer something else.

## HANDOFF
- [codex → Claude] codex/unmarked-liveries · Ty (2026-10-28): unmarked liveries, make them the game DEFAULTS · **DONE by Claude on lane/claude-livery-picker (2026-10-28), see Log. The props below are the wrong way round: checker-unmarked has the Hartzell 2-blade and factory-unmarked the MT 3-blade, the same as N31DG / N1191.** · details:
  - New GLBs (same geometry/props as the approved v10.1 Pitts and v12 Extra; only markings removed, removed areas left in the plain underlying paint):
    - pitts-s2b-like/pitts-s2b-like-own-checker-unmarked.glb: id 'checker-unmarked' (N31DG checkered scheme, MT 3-blade prop, no registration). **Pitts DEFAULT.**
    - pitts-s2b-like/pitts-s2b-like-own-factory-unmarked.glb: id 'factory-unmarked' (N1191 factory diamonds scheme, Hartzell 2-blade prop, no registration).
    - extra-300-like/extra-300-like-own-unmarked.glb: id 'unmarked' (no N-number, no Talk Flying / TALKFLYING.COM / TF badge / Sunrise Aviation; fin stars and stripes kept; red underwing bar like N300UY). **Extra DEFAULT.**
  - Task (lane/claude-livery-picker, src/FlyingGame.Core/LiveryChoice.cs): add the ids to Options (pitts-s2b-like: ("checker-unmarked","Checkered"), ("factory-unmarked","Factory"); extra-300-like: ("unmarked","Extra")), map OwnSuffix pitts checker-unmarked→"-own-checker-unmarked", factory-unmarked→"-own-factory-unmarked", extra unmarked→"-own-unmarked", and make them the per-type defaults (Pitts = checker-unmarked, Extra = unmarked). Keep the marked liveries (n31dg, n1191, talk-flying, n300uy) selectable. New GLBs have no .meta yet (Unity generates it).
  - Build: PITTS_LIVERY=CHECKER-UNMARKED|FACTORY-UNMARKED tools/own_models/build_pitts_s2b.py; EA_LIVERY=unmarked tools/own_models/build_ea300.py. Renders (agent box): /workspace/unmarked-v1/{pitts-checker-unmarked,pitts-factory-unmarked,extra-unmarked}-{side,top,front,bottom}.png.
- [codex → Claude] codex/own-pitts-s2b · APPROVED by Ty (2026-10-27, v10 c8b90bc: "send them to aero"; v10.1 removes the fin "Pitts S-2B" script on Ty's request — use v10.1) as DONE: **DONE by Claude on lane/claude-pitts-own (2026-10-27), see Log; open points under NEEDS YOU** · own clean-room Pitts S-2B in two liveries · wire into Aero Playground as the in-game Pitts S-2B · details:
  - Models: unity/Assets/_Project/Resources/Models/pitts-s2b-like/pitts-s2b-like-own.glb (N31DG: red/white sunburst, checkered rudder, Hartzell 2-blade prop, grey with white tips) and pitts-s2b-like-own-n1191.glb (N1191: factory sunburst with diamonds, MT 3-blade prop, white with red tips). The n1191 GLB has no .meta yet (Unity generates it).
  - Task: make these the in-game Pitts S-2B (existing flight config configs/aircraft/pitts-s2b-like.json). N31DG (-own) is the default; N1191 (-own-n1191) is selectable the same way as the Extra's N300UY (AirframeModels suffix / AERO_LIVERY), or a picker if easy. Check scale/orientation, US prop rotation (clockwise from the cockpit), control-surface binding.
  - Credits/licensing (docs/models/pitts-s2b-own-PROVENANCE.md): fully clean-room procedural Blender build (tools/own_models/build_pitts_s2b.py); no third-party mesh used or measured; dimensions from Aviat/Jane's/TCDS A8SO; paint from reference photos only; fonts SIL OFL 1.1. No GPL question.
- [codex → Claude] codex/own-ea300 · APPROVED by Ty (2026-10-27, v12 d0c6130) as DONE: own EA300 in two liveries · swap in as the in-game Extra 300, review provenance · **DONE by Claude on lane/claude-ea300-swap (2026-10-27), see Log; the GPL decision is in NEEDS YOU** · details:
  - Models: unity/Assets/_Project/Resources/Models/extra-300-like/extra-300-like-own.glb (Talk Flying: TF fin badge, TALKFLYING.COM sides) and extra-300-like-own-n300uy.glb (plain N300UY, red tapered underwing bar, no TF branding). Both have a 3-blade prop (1/1 propellers bound), cockpit, D-shaped cowl inlets and a simplified AEIO-540 engine. Built by tools/own_models/build_ea300.py (EA_LIVERY=tf|n300uy) + tools/own_models/paint.py, art in tools/own_models/art/.
  - Task: replace the current Extra (the FlightGear-based EA300 from codex/fg-models) with these as the in-game Extra 300. Make Talk Flying (-own) the default; make N300UY (-own-n300uy) selectable through the AirframeModels.cs suffix naming (as with the 2-33's OwnSuffix/PickOwn). Retire the FG EA300 from shipping use (no FG EA300 asset referenced by any shipping livery).
  - Credits/licensing (docs/models/ea300-own-PROVENANCE.md): from v2 on, the geometry was measured from the GPL FlightGear EA300 by Patrizio Melis (codex/fg-models 5fdacad), by Ty's explicit choice. No FG mesh, UVs or textures are copied, but the shape may count as a derivative work. Fonts: Liberation Sans Bold and Archivo Black, both SIL OFL 1.1. Before App Store release, Claude MUST complete a GPL derivative review: keep the GPL credit, get Patrizio Melis's permission, or fall back to the clean-room v1 (638c05a). Update docs/CREDITS.md to match.
  - Four-views (agent box): /workspace/ea300-own-v12/ea300-tf-4view.png, /workspace/ea300-own-v12/ea300-n300uy-4view.png (plus ea300-front-inlets.png, ea300-front-3q.png).
- [codex → Claude] codex/own-ea300 · decide the GPL status of extra-300-like-own.glb v2 (see NEEDS YOU and docs/models/ea300-own-PROVENANCE.md). **Reviewed by Claude 2026-10-27 (Log). The decision now goes to Ty under NEEDS YOU.** Possible outcomes:
  - Accept it.
  - Ask Patrizio Melis for permission.
  - Revert to the v1 clean-room commit 638c05a.

## Log
### 2026-10-28 — Codex · lane/codex-visuals · pass 4, batch S iteration 8

- **Self-review:** opened S7 J-3 target/hero comparison, Beaver hero, Balanced sunset, day idle wake, Beaver spray, J-3 wake, world overview/hills/alpine-range; compared the unchanged target in the comparison, lake-glow 2, calm-lake-reflection 1, cumulus-day 1 and snow-ridge 1. Both ERRORS files empty. Sunset **6/10**: warm horizon and cooler upper sky/mirror work, repetitive cloud texture and graphic shore remain. Hero **6/10**: full-width/full mirror, but bright yellow reflected underside and weak float emphasis miss the photo. Idle **5/10**: slow attitude correct, water disturbance nearly absent. Day sky/water **4/10**: clouds still look pasted onto lake, terrain silhouettes remain very unlike references; near contrast retained. Shore reflection **7/10 correctness**: anchored cliff retained; peaks below cliff remain an occlusion defect. No outgoing visual scores without new renders.
- **Changes:** hero now fixes 25-degree aft-left bearing throughout the 24-step distance fit (previous fixed lateral offset allowed angle to vary with aircraft/fit). Height .65 m, horizontal optical axis, fixed vertical projection shift for horizon 45% down; 78-degree lens, 94% horizontal silhouette width limit, full actual-vertex/mirror vertical margin checks and diagnostic output retained. Same deterministic stage/seed, original sunset.png unchanged. New shared sunset material response uses horizontal sun/view alignment: backlit surface .78 brightness, reflected surface blends .86 toward a dark cool luminance tint. Applied in Airframe/Lit only, keeps rim/metal detail and leaves day shading unchanged.
- **Day water / idle:** skybox DaySky remains the same; water alone broadens cloud edges and lowers cloud contrast with unresolved surface roughness proportional to (1-Fresnel), so distant grazing reflections recover sky clarity. Existing Schlick Fresnel still composites body/planar/sky, near body shifts deeper blue-green; sunset exact shared sky/mirror weight untouched. No extra cloud samples, passes or textures. Idle-only optics add short radial crest packets bounded to 4.5 m around wet steps plus slightly stronger existing bow/diverging shoulder normals/glints; no planing foam, solver/history/forcing or float-state changes. 2.5 m/s zero-throttle default and explicit speed override unchanged. Low-priority cliff/peak occlusion deferred to preserve corrected shore visibility.
- **Validation / scope:** delimiter, whitespace and existing-meta checks passed on five edited Unity sources. No Unity/tests/compiler/build/git attempted under this loop's explicit restrictions; Chief must compile both assemblies, import strict Metal, rerender and measure iPad shader cost. No new assets/reference copies, targets, runtime draws or texture memory. Existing haze/quality controls, sunset palette, shore reflection camera/terrain fix, Kelvin V/step whitewater, bow/prop spray and waterfall sheets unchanged. No Core/Sim/config/flight-test/GLB/prop-hub/gameplay/collision/chart changes; built-in pipeline, no demonstrated URP blocker. Left uncommitted for runner; no push/merge/deploy.

### 2026-10-28 — Codex · lane/codex-visuals · pass 4, batch S iteration 7

- **Self-review:** opened P4-S6 J-3 normal sunset and target/hero comparison, Beaver/Balanced heroes, day idle wake, J-3 spray/wake/raw reflection, Beaver spray, AirCam bow, and world overview/hills/first-wall/desert-range/alpine-range/alaska-massif/topdown-valley/waterfall-front-detail. Compared original sunset target (unaltered in comparison), lake-glow 2, sunset-altocumulus 1, cumulus-day 1 and snow-ridge 1. Both ERRORS empty. Sunset **6/10**: horizon warmth/mirror good, but peach dominates upper sky and water rather than the target's blue-grey/lavender. Hero **6/10**: width and full mirror improved, nearly axial view and tiny floats still miss the photo. Idle **6/10**: slow displacement/no step whitewater, rails now faint; cloud stamps dominate water. Day sky/haze **4/10**: local contrast retained, but regular bright-edged cloud stickers, hard horizon fade and simplistic terrain overwhelm depth. Scenery reflection **7/10 correctness**: cliff remains attached to shore without detached slab; Balanced stairs persist. No outgoing scores without renders.
- **Changes:** sunset gold/peach-to-pink transition ends at ray height .26 (was .38), cool blue-grey transition now .18–.62 (was .25–.90); cloud light/shadow transitions follow. Horizon gold/aureole and identical sky/water evaluation retained; calm mirror strength and aircraft warm fill/rim unchanged. Hero lens 62 → 78 degrees, left offset .25 → .50 span, minimum aft .60 → .35 span; retains 1 m camera, 24-step actual-vertex/full-mirror 94% fit and projection centring. This targets stronger near-float perspective while keeping whole wings/reflection; actual fitted pose remains in hero-diagnostics.txt. Original sunset output retained.
- **Day:** horizontal deck projection makes distant clouds shrink toward horizon, empty/variable-size cells break the regular one-per-cell pattern, stronger erosion and shaded rounded interiors replace the all-around bright rim. Pixel-footprint edge filtering and lower horizon fade reduce hard stamp edges. Four candidates and existing Low/normal erosion tier retained; adds derivative/sqrt arithmetic, no extra noise/hash evaluations. Added two editor-only world sky inspection shots; all existing names/flags retained. Existing distance/altitude blue haze and tier controls untouched: terrain silhouettes/world boundary cannot be corrected by atmosphere tuning.
- **Validation / scope:** delimiter, whitespace, existing meta and fragment-only derivative-call checks; no Unity/compiler/tests/build/git attempted per loop. Chief must compile separate assemblies, import strict Metal and rerender full set plus new sky shots, then check iPad GPU cost. No new assets, textures, runtime draws/passes/targets or reference copies. Reflection/terrain/water-wake/spray/waterfall code unchanged; Kelvin V, step whitewater, bow/prop spray and idle speed override preserved. No Core/Sim/config/flight-test/GLB/prop-hub/chart/gameplay/collision edits; built-in pipeline, no demonstrated URP blocker. Left uncommitted for runner; no push/merge/deploy.

### 2026-10-28 — Codex · lane/codex-visuals · pass 4, batch S iteration 6

- **Self-review:** opened P4-S5 J-3 normal sunset/hero comparison, Beaver/Balanced heroes, J-3 spray/raw reflection/scenery-off/wake, Beaver spray, AirCam bow, idle wake, and world overview/hills/first-wall/desert-range/alpine-range/alaska-massif/topdown-valley/waterfall-front-detail. Compared original sunset target, lake-glow 2, sunset altocumulus 1, daytime cumulus 1 and snow-ridge 1; also opened S4 day J-3 spray directly for comparison. Both S5 ERRORS empty. Sunset **7/10**: gold/peach/pink returned, reflected sky coherent; still repetitive clouds, graphic shore and plain yellow airframe. Hero **5/10**: behind-left and full mirror now, but about 78% width, tiny floats, excess sky/water. Idle **5/10**: correct slow displacement, long straight optical rails remain. Day sky/haze **4/10** vs photos: near contrast survives and haze is blue, but melted connected cloud patches, smooth mountain forms and world boundary dominate. Reflection **7/10 correctness**: S5 cliff contacts shore; detached green city band/teeth seen in S4 are absent in J-3 and Beaver. Balanced reflection still stair-steps. No outgoing scores without renders.
- **Reflection first / root cause:** examined raw source and scenery-off before changing anything. The offending plateau top was already in S4's raw planar target; S5 replaces that visible top/city band with the missing front cliff and restores shore contact. Combined with the S5 source change, this identifies incorrect terrain face visibility in the mirrored view as the cause of the old band/teeth: reflection rasterizer-facing rejection had retained plateau backs and rejected cliff fronts. True geometric face orientation from world derivatives, oriented to the outward mesh normal and tested against the mirrored eye, fixes that observed defect. This does not establish the precise Metal winding conversion responsible. Plane audit is consistent (fixture/reflection/harbour all Y=0); no water UV, plane-height or scenery masking workaround introduced. Terrain/WaterReflection untouched this run to preserve the successful S5 correction.
- **Hero:** fixed 62-degree vertical FOV, height 1 m, LEFT offset .25 of mesh-bounds span; aim above the bounds centre toward the wing-root region. Starts with .6-span minimum aft of the bounds tail station; 24 deterministic bisection steps fit actual mesh vertices plus their mirror into a 94% envelope. Optical projection shift centres the envelope while preserving the aim. This replaces the changing side angle, 88-degree lens and coarse distance increments. The narrow portrait/full-mirror constraints may require farther than the requested nominal .6-span aft; hero-diagnostics.txt reports nominal/actual distance, bounds, pose, unshifted envelope and projection shift for review. Existing sunset.png/comparisons and every flag/output remain. Full silhouette, >=90% wing width and near-float dominance require J-3/Beaver render confirmation.
- **Idle / day sky:** idle-only optical envelope now curves, oscillates along and across the wake into short wave packets, decays over 2.8 m and ends by 10 m (was 17); glint .18 → .055 and normal tilt .065 → .035, small bow retained. 2.5 m/s zero-throttle default, explicit speed override, displacement attitude, solver/history/forces and planing/bow/prop spray gates unchanged. DaySky replaces broad thresholded noise with four nearest cellular cloud envelopes, each a broad base plus turret, two scales of eroded edge detail on normal tiers and one on Low. Retains deep-blue zenith, lighter blue horizon, shaded bases and shared exact sky/water evaluation. SunsetSky/Environment, warm aircraft lighting, mirror weight and day distance/altitude haze controls unchanged. Water evaluates the day or sunset sky in explicit branches.
- **Budget / validation / scope:** three Unity sources edited, existing metas confirmed; no new assets, textures, geometry, draw calls, targets, passes or allocations at runtime. Hero fitting/diagnostic text are editor-only. Day cloud cost is four cell candidates (12 hash evaluations) plus 4/8 hash evaluations for Low/normal edge erosion, versus prior 12/16 hash evaluations; adds lobe arithmetic/branches and needs iPad GPU measurement, especially full-screen sky/water. Source checks passed balanced braces, existing metas and whitespace; Editor accesses use public Unity members. No Unity/compiler/tests/build/git attempted per loop. Chief owns separate Bridge/Editor compilation, strict Metal import and full water/world rerender. Kelvin V, step whitewater, bow/prop spray and waterfall sheets preserved. No refs copied, no Core/Sim/config/flight-test/GLB/prop-hub/chart/gameplay/collision edits, commit/push/merge/deploy. Built-in only; no demonstrated URP blocker. Left uncommitted for runner.

### 2026-10-28 — Codex · lane/codex-visuals · pass 4, batch S iteration 5

- **Self-review:** opened P4-S4 J-3 target/hero comparison and normal sunset, Beaver/Balanced heroes, day J-3 spray/raw planar source, idle wake, world overview, hills, first-wall, desert-range, alpine-range, alaska-massif and topdown-valley. Opened original sunset target, lake-glow 2, sunset altocumulus 1, daytime cumulus 1 and snow-ridge 1. Both ERRORS empty. Sunset **6/10**: S4 restored gold at the horizon, but cool upper sky/water still dominates, cloudlets repetitive and skin flat. Hero **4/10**: whole mirror fits but plane only occupies about 60% width, nearly straight astern with tiny floats. Idle **5/10**: correct slow displacement/no step whitewater, faint regular ripple rails and scenery reflection contaminate it. Day sky/haze **5/10**: better contrast/blue depth in hills, still graphic clouds, smooth mountain shapes and sparse depth in overview. Scenery reflection **2/10**: green land/city band and cliff teeth already exist in raw camera target. No outgoing scores without new renders.
- **Reflection:** source audit finds FloatHydro surface converted directly to Unity Y, consistent with CoordinateMap (no rebasing there); harbour is the fixture lake. Current projection is finite/non-oblique, so there is no oblique sign/offset to adjust. The raw image exposes plateau tops above inverted mountains while the near cliff is mostly missing: inconsistent physical face visibility is the working hypothesis, **not a confirmed backend diagnosis**. Terrain now disables hardware culling, retains main-view VFACE rejection, and during reflection uses ddx/ddy of world position to obtain a true triangle normal, oriented with the mesh outward normal, then clips faces pointing away from the explicitly supplied mirrored eye. This replaces dependence on reflected rasterizer winding without the old incorrect smooth-normal visibility test. Water half-space clip, finite projection, plane height, reach, scenery masks and reflection sampling stay unchanged. Added fixture reflection-diagnostics.txt (fixture/reflected/harbour heights, Unity/sim positions, both eyes, view determinant, near/far and matrices) beside existing raw/scenery-off outputs. Chief must confirm cliff-base contact and absence of band/slivers; if this fails, do not mark the bug fixed.
- **Hero/sunset:** rear-left offset grows from .16 to .42 of rear distance, camera remains 1 m above water. Fits actual MeshFilter vertices and their reflected points rather than empty bounding-box corners, targeting 97% width with full vertical mirror margins; avoids particle bounds. Original sunset.png/comparison retained. Gold/peach lower sky and pink middle occupy a larger elevation range; overhead is muted lavender-grey rather than denim. Broader sun-facing aureole and warmer cloud undersides share the exact same sky function with water. Low-frequency aircraft environment now follows this horizon/upper/underside palette, and Lit/Airframe sunset diffuse fill also samples that environment with retained cool base fill/warm directional light. Float aluminium classification retained. Camera/paint/floats still need Chief review; no claim of photo match.
- **Budget / validation / scope:** no new assets, textures, targets, passes, draw calls or geometry; all six edited Unity sources have existing metas. Terrain Cull Off increases raster work for backfaces even though main fragments discard immediately; reflection adds derivative/cross/dot ALU. Aircraft sunset fill adds an analytic environment evaluation; no extra cloud octaves. Needs iPad profiling, especially reflected cliff coverage. Mesh vertex fit is editor-only and may read many vertices; no runtime readback. Static checks confirmed balanced braces, existing metas and no trailing whitespace; public Editor accesses reviewed. No Unity/compiler/tests/build/git attempted per this loop. Chief owns separate Bridge/Editor compilation, Metal import and complete water/world rerender. Day haze/sky tuning and tier knobs, idle speed/attitude, water shader, Kelvin V/step whitewater, bow/prop spray and waterfall sheets untouched. No reference copied; no Core/Sim/config/flight-test/GLB/prop-hub/chart/gameplay/collision edits, commit/push/merge/deploy. Built-in pipeline only; no demonstrated URP blocker. Left uncommitted for runner.

### 2026-10-28 — Codex · lane/codex-visuals · pass 4, batch S iteration 4

- **Self-review:** opened P4-S3 J-3 normal sunset and target/hero comparison, Beaver sunset, Balanced hero, idle wake, Beaver spray/raw reflection/scenery-off, J-3 wake and AirCam bow; world overview, hills, first-wall, desert-range, alpine-range, alaska-massif and topdown-valley. Compared the unaltered target in the comparison, sunset altocumulus 1, lake-glow 2, day cumulus 1 and snow-ridge 1. Both ERRORS empty. Sunset **6/10**: warm glow and mirror readable, cloud carpet still repetitive, aircraft/shore graphic. Hero **3/10**: close float is stronger but tail and reflected wings crop badly, worse composition than the normal sunset. Idle **5/10**: slow/no step whitewater now readable, but shoulders form long straight rails and aircraft crops at left. Day sky/haze **4/10**: clouds visible in horizon shots but too soft/large, overview uniformly blue and elevated ridges weakly separated; terrain silhouettes remain unlike eroded photo ridges. Scenery reflection **2/10**: raw target still contains broad land/city band and cliff slivers; scenery-off isolates it. Balanced stairs remain. No outgoing visual scores without new renders.
- **Changes:** hero retains low 1 m camera, moves nearer the rear axis and fits only airframe MeshRenderer bounds plus their reflected envelope (3.5% image margins), avoiding particle bounds; 88-degree FOV and camera distance/bounds logged. This deliberately replaces the fixed close crop after P4-S3 showed it losing aircraft/reflection. Chief must judge whether resulting size still earns a hero view; original sunset.png retained. Sunset horizon/middle lightened toward peach/pink, cool upper retained, cloud opacity reduced with identical sky/water function. Existing warm aircraft light, float environment and calm mirror weights unchanged. Idle shoulder envelope falls off sooner, ends within 17 m and has slightly irregular spacing; wake inspection camera raised/pulled back and aims closer to aircraft to include it. Speed/override/displacement attitude and planing/spray gates untouched.
- **Day atmosphere/budget:** cloud density gives more weight to existing fine octave, tighter edges and a shaded interior/bright edge. Low still skips that octave. Terrain optical depth integrates an exponential altitude profile along the view segment rather than boosting all long paths; shared default density .000065 → .000052, clear near range remains 850 m, existing tier and CLI overrides retained. Lower density and shorter elevated paths target the milky overview without removing distant blue extinction. No added sky samples, targets, textures, geometry, draw calls or passes; terrain replaces one altitude exponential/long-range smoothstep with two exponentials and needs device ALU timing. Camera fit runs only in editor. No new assets/reference copies; all six edited Unity assets have existing metas.
- **Validation / limits:** source inspection and balanced braces, meta presence and whitespace checks only. No Unity/compiler/tests/build/git attempted under the lane rule. Chief must compile Bridge and Editor separately, import Metal shaders and rerender all fixed water/world shots plus heroes. Reflected scenery band/slivers not solved or hidden; reflection code, wake solver/history, Kelvin V/step whitewater, bow/prop spray, waterfall sheets untouched. Shore silhouette, flat aircraft detail and terrain shape remain limits. Built-in pipeline only; no demonstrated URP blocker. No Core/Sim/config/flight-test/GLB/prop-hub/chart/collision/gameplay geometry edits; left uncommitted for runner, no push/merge/deploy.

### 2026-10-28 — Codex · lane/codex-visuals · pass 4, batch S iteration 3

- **Self-review:** opened P4-S2 J-3 target/hero comparison, Beaver sunset, Balanced hero, idle wake, J-3 spray and raw reflection source; world overview, hills, first-wall, desert-range, alpine-range, alaska-massif, topdown-valley. Opened lake-glow 2, sunset altocumulus 1, day cumulus 1 and snow-ridge 1; target photo visible unaltered in comparison. Both ERRORS empty. Sunset **6/10**: gold/pink restored and cool overhead improved, but uniform mottle and graphic tree line persist. Hero **5/10**: aircraft now wide but too directly astern, near float small, grey plastic finish. Idle **4/10**: slow fixture correct, disturbance almost invisible, camera too high, reflected volume panes conspicuous. Day sky/haze **4/10**: hills/first-wall do show clouds, but overview/alpine have flat background and mountains remain nearly equal contrast. Scenery reflection **2/10**: detached city/land band, cliff teeth and glass slivers are already present in raw planar target; not a water blending artifact. Terrain geometry itself remains unlike eroded photo ridges and was not changed. No outgoing scores without new renders.
- **Hero/sunset:** replaced all-extremity fit loop with camera 1 m above lake, 4 m behind and 2.5 m left of configured left float tail, looking forward-right, 94-degree portrait vertical FOV. Wings may crop per brief; full vertical reflection/crop needs Chief review on both types. Camera pose logged. Original sunset.png/flags/comparisons preserved. Floats alone use new AluminiumFloat surface classification and uniform shader flag, reflecting warm sky above/dark below; other metal, GLBs and prop hubs retain their shading. Shore keeps same tree/crown counts but varies height more, narrows crowns and separates four rows by 95 m with muted depth colours in one LitVC batch; no transparent haze cards. Lower sky has broader breaks near the sun and a smooth clear band, shared exactly with water sky lookup. Warm lighting/calm mirror floor unchanged.
- **Reflection/idle:** removed 3/6 km reflection-only far cutoffs; projection and reach match the main camera, preserving per-lake plane selection and explicit world half-space clipping. This addresses a concrete frustum mismatch; **not proof that it explains every land-band/sliver artifact**, next raw target + scenery-off comparison must establish that. Glass non-aircraft navigation panes are cached once, hidden only during reflection and restored in finally; no per-frame scene searches/allocations. WorldRender hides those panes for scenery review while retaining navigation frames and gameplay objects. Idle camera lower/closer; normal bow/ripple shoulders stronger with subtle reflective crest highlights, no new foam or physical forcing. Existing 2.5 m/s default with explicit speed override, displacement pose, fast V/step whitewater and bow/prop spray remain.
- **Day atmosphere/budget:** WorldRender explicitly assigns camera Skybox material after ApplyDay and disables uniform fog. Skybox direction uses cube vertex direction directly (standard built-in skybox convention), avoiding an object transform in the analytic ray. Blue optical depth default .000038 → .000065, weaker altitude attenuation for elevated ridges; clear range/tier/CLI overrides retained. Same shader remains gameplay/water source. Reflection resolution/masks/one-pass count unchanged, but full main-camera far reach increases reflected terrain submission on Balanced/High and must be measured on iPad; correctness must not rely on severing a visible reflected shore. No additional targets/textures/particles/runtime mesh vertices. Shore adds vertex colours only in editor stage; float environment arithmetic has a uniform float-only branch. Existing Low sky octave skip remains.
- **Validation/scope:** source inspection, balanced braces on all ten edited Unity assets, existing metas present. No new assets/reference copies; no Unity/compiler/tests/build/git attempted under lane rule. Chief must compile Bridge/Editor separately, import Metal shaders and rerender the full fixed set plus heroes before acceptance. No claims of compilation, rendered improvement or solved reflection geometry. Built-in only; no effect established as URP-blocked. No Core/Sim/config/flight-test/GLB/prop-hub/chart/collision/gameplay geometry edits; waterfall sheets untouched. Uncommitted for runner, no push/merge/deploy.

### 2026-10-28 — Codex · lane/codex-visuals · pass 4, batch S iteration 2

- **Self-review:** opened P4-S1 J-3 target/hero comparison, Beaver sunset, Balanced hero, day idle wake, Beaver spray, J-3 wake and AirCam bow; world overview, hills, first-wall, desert-range, alpine-range, alaska-massif and topdown-valley. Compared the unaltered target photo, sunset altocumulus 1, lake-glow 2, day cumulus 1, snow-ridge 1 and Kelvin-wake 1 (ship, useful for wake structure only). Both ERRORS empty. Sunset **5/10**: warmth returned but peach dominates overhead and foreground, unlike blue/lavender photo; procedural aircraft still flat/plastic. Hero **4/10**: full mirror retained but aircraft too small and too side-on; floats/near wing lack dominance. Idle **4/10**: log proves 2.5 m/s, 43.8 N planing lift versus 729.6 N buoyancy; no fast whitewater, but gentle water disturbance almost invisible. Day sky/haze **4/10**: local contrast improved, clouds still stretched flat, distant mountains too similar in contrast. Terrain shapes are far from real eroded ridges; overview exposes world edges and debug volumes. Day reflection slab/slivers and Balanced stairs persist. No outgoing scores without renders.
- **Changes:** brought sunset rose/cool transition down into visible elevations (.13–.53 sky-ray height); kept gold horizon/aureole and warm sun-facing cloud undersides, but confined warm cloud illumination nearer horizon. Same direction function still drives water mirror; .91 calm mirror floor and warm aircraft lighting unchanged. Broader lower shore crowns overlap more with identical mesh counts. Hero remains 1 m high, now ~19-degree rear quarter and fits readable mesh vertices plus mirrored vertices rather than empty combined-AABB corners; 3.5% horizontal margin, 5.5% vertical. Existing sunset and comparison outputs preserved. Editor-only fit cost; no new runtime mesh/target.
- **Idle/day:** preserved throttle/speed override logic and level displacement fixture. Closer idle wake view. Wet slow origins now produce short diverging normal ripples, small bow shoulders and faint reflected crest glints; no foam, solver or forcing changes. Branch requires wet origin beam, speed > .05 and < 4 m/s; calm and 18 m/s paths unchanged. Day sky uses dome projection to reduce horizon cloud stretching, stronger crown/base shading and slightly deeper blue. Terrain optical depth ramps up along long paths beyond the existing 850 m clear range; scattering remains blue-grey with altitude attenuation. Existing `-skyQuality`, `-hazeDensity`, `-hazeClearRange` and Low octave skip retained.
- **Validation/budget:** source review, balanced braces and existing metas on four edited Unity assets. No new assets/reference copies; no extra passes, textures, targets, particles or runtime vertices. Slow wet-water branch adds arithmetic; existing tier controls retained, device ALU/GPU measurement remains Chief's. No Unity/tests/build/git commands attempted, no compile/render success claimed. Chief owns separate Bridge/Editor compilation, Metal import and next full render set. Built-in pipeline; no effect established as URP-blocked. No Core/Sim/config/flight-test/GLB/prop-hub/chart/gameplay geometry changes. Waterfall sheets, fast wake/spray and reflection clipping/culling untouched. Left uncommitted for runner; no push/merge/deploy.

### 2026-10-28 — Codex · lane/codex-visuals · pass 4, batch S iteration 1

- **Self-review:** opened B4-2 J-3 reference comparison (unaltered target photo visible alongside), Beaver and Balanced sunset; day idle wake, J-3/Beaver spray, AirCam bow; B4-2-world overview, hills, first-wall, desert-range, alpine-range, alaska-massif and topdown-valley. Opened both new lake-glow photos, sunset altocumulus 1, daytime cumulus 1 and snow-ridge 1. Both ERRORS files empty. Sunset **5/10**: narrow yellow band, broad flat mauve cloud field and synthetic aircraft finish; hero composition **4/10**: too squarely behind tail, weak near-float perspective, regular triangular trees. Idle fixture **2/10**: 18 m/s tow wake despite zero throttle. Day sky/haze **3/10**: pale horizon, continuous white cloud ceiling, milky distant terrain and weak blue ridge separation; mountain shapes themselves remain very unlike real ridges. Lake-glow 1 is an intensely orange rippled sunset, useful only for directional glow, not the calm target palette; lake-glow 2 is a subdued mountain lake, useful for mirror fidelity. Existing day reflection slab/slivers and Balanced reflection stairs remain visible; no claim they are solved. No outgoing scores without new renders.
- **Sunset / hero:** widened gold/peach-to-rose transition, moved cool blue-grey transition higher, warmer sun-facing cloud undersides and directional aureole shared by sky and water. Calm mirror floor .72 → .91 (grazing maximum .98) gently darkens the foreground without washing out reflected aircraft silhouettes. Warm aircraft light/fill/rim retained. Existing stage crowns shorter/full-body, overlapping more, with warmer muted distant shore; same geometry budget. Added automatic 1000×1600 `sunset-hero.png` and `reference-left-hero-right.png`, camera 1 m above surface, 34-degree rear quarter, 64-degree FOV, projected bounds fit including mirrored corners and 5.5% margins. Original sunset camera/output preserved. Chief must verify actual near-wing/float dominance and complete strut/float reflection.
- **Idle:** parse throttle before speed; zero-throttle default 2.5 m/s, calm remains 0, explicit `-speed` wins. Below 4 m/s prescribed attitude is level rather than planing pitch; slow wake view closer with 7 m aft aim instead of 24 m. Existing real FloatHydro and solver signals retained. Cosmetic whitewater envelope now starts above 4 rather than 2 m/s, eliminating idle step foam while preserving 18 m/s Kelvin V/step turbulence; solver forcing unchanged. Existing wet bow and prop RPM/clearance spray gates retained, as are planing-only step/stern/mist gates. Idle prop-wash visibility needs actual render confirmation.
- **Day / budget:** darker blue zenith and blue horizon, separated cumulus coverage and offset-noise shading for bright crowns/cool bases. Water now uses the same day direction function instead of constant sky blue. Terrain optical depth starts after 850 m, density .000038/m rather than .000055/m, blue-grey scattering (.32,.49,.68), mean ray altitude attenuation for high/top-down views. Public `SunsetLighting.ConfigureDayAtmosphere` sets globals; runtime uses Unity quality name, Low/Fast skips finest cloud octave. Both render fixtures expose `-skyQuality Low|Balanced|High`, `-hazeDensity 0.000038`, `-hazeClearRange 850` for tier tuning. No textures, targets, runtime passes, draw calls or mesh vertices added. Day sky has one extra noise lookup for directional cloud shading; day water newly evaluates procedural sky (4 noise calls high/balanced, 3 low) instead of constant color, so iPad ALU/GPU timing remains required. Reflection target sizes and masks unchanged; hero adds an editor capture only.
- **Validation / scope:** inspected public cross-assembly API access, shader globals/declarations, brace balance, whitespace and existing metas on all seven touched Unity assets. No new assets/reference copies. No Unity/tests/build/git commands attempted per this loop's explicit restriction; Chief owns separate Bridge/Editor compilation, Metal import, full next renders and device performance. Built-in pipeline; no effect established as URP-blocked. No physics/config/flight-test/GLB/prop-hub/chart/gameplay geometry changes. Reflection clipping/culling and waterfall sheets untouched. Left uncommitted for runner; no push/merge/deploy.

### 2026-10-28 — Codex · lane/codex-visuals · pass 3, batch B4 iteration 2

- **Self-review:** opened all nine requested B4-1-world shots, city-facade/avalon/eiffel/arch/seaplane-base detail views, both city-aerial and both harbour-seaside photos. World and water ERRORS empty. City/town **3/10**: window grids improve blank boxes, but tall rows are stretched, roof tops empty and tiny sidewalks leave a huge asphalt parking lot. Harbour/base **2/10**: conifers obscure hangar/apron and Avalon houses; waterfront still lacks believable urban continuity. Overview **2/10**: exposed world edges, black city rectangles, debug-looking combat volumes/clouds dominate. Eiffel detail **3/10**, arch detail **2/10**, provisional realism scores without matching photo references; sparse tower lattice and simple arch finish remain. Original street/Eiffel/arch cameras still **1/10 presentation**, often below terrain. City references are near duplicates; harbour references are an anchorage at dusk and historical industrial port, not carrier/hangar design references. Opened J-3/Beaver/Balanced sunsets, Beaver spray, J-3 wake, AirCam bow and idle wake: sunset **5/10**, wakes/spray **4/10**, scenery reflection **2/10**; broad reflected terrain slab/slivers and Balanced stair steps remain. No outgoing scores claimed.
- **Changes:** city towers keep footprints/heights/positions but now occupy 140 m pale sidewalk blocks on the existing 170 m grid, leaving 30 m asphalt streets; two flat green inset beds per lot soften paving. Existing plaza is drawn afterward. Three flush dark roof service panels per tower add aerial roof variation without raised obstacles. Facade cap 24 → 40 rows and taller/wider style-dependent panes reduce the stretched window spacing on tall towers; low houses retain their existing small row counts and pitched roofs. Seaplane hangar gains door panel joints and rear/side clerestory glazing in its existing DockFittings colour batch. Trees, collisions, landmarks, pads/gates, streets' gameplay geometry and all water/sky/shaders untouched.
- **Budget / validation:** no new materials, textures, passes, render targets or assets; all three edited Bridge sources have existing tracked metas. Added opaque details share existing meshes/draw calls; facade upper bound now 960 quads / 3,840 vertices (~173 KiB vertex/index payload per maximum tower at 40 bytes/vertex + uint indices), up from 576 quads. Actual towers vary; CPU copies and reflection rendering add cost. iPad memory/GPU time and pane shimmer need Chief measurement; tiers/reflection masks retained. Source reviewed only, no Unity/compiler/tests/build/git run per lane restriction. Chief must compile Bridge/Editor separately and rerender all named WorldRender shots plus full fixed WaterRender set into B4-2 folders. Check planted beds/plaza boundaries, pane aliasing, roof-panel depth and obscured hangar before acceptance. Forest intrusions, detailed waterfront integration and landmark finish remain open. Built-in pipeline; no effect established as URP-blocked. No reference copies, forbidden files, charts, GLB or prop-hub changes; left uncommitted for runner, no push/merge/deploy.

### 2026-10-28 — Codex · lane/codex-visuals · pass 3, batch B4 iteration 1

- **Self-review:** opened all nine requested B3-2-world shots and both city-aerial/both harbour-seaside photos. Both ERRORS summaries empty: B3 airport shader magenta is gone in the visible base surface. Buildings/city **2/10**: blank boxes, repetitive black floor bands and continuous pale ground rather than streets/blocks. Town **2/10**: original town is deliberately another tower city per earlier owner instruction; preserved this layout, while pitched houses belong to Avalon. Harbour/base **2/10**: base buried in conifers, carrier distant, island housing barely legible. Overview **2/10**: toy-like city rectangles and exposed world edges. Original street, Eiffel and arch shots **1/10 presentation**: below terrain; landmark finishes cannot honestly be scored. City reference JPEGs are effectively duplicate historic Manhattan imagery, useful for muted masonry, dark panes and roof surfaces, not a town template. Harbour 1 is a dark sunset anchorage; harbour 2 a historic industrial port, neither a matching carrier/seaplane-base detail reference. No matching tower/arch photo supplied, so no photo-matched landmark score. Opened three sunset cases, Beaver spray, J-3 wake, AirCam bow, idle wake: sunset **5/10**, wakes/spray **4/10**, scenery reflection **2/10**; slab/slivers and Balanced stair steps persist. No outgoing scores without new renders.
- **Changes:** existing vertex-colour batches gain opaque blue-grey window quads on all four city/house faces, deterministic modest pane variation; tower glass muted. Removed repeated heavy floor bands, added restrained cornices, different inset roof membrane colours and pale individual sidewalk blocks over darker asphalt. All tower/bridge/pad/gate coordinates and structural extents retained. Avalon houses get original procedural pitched clay/slate roofs with closed gables in place of flat tile lids, plus windows; existing pier gains edge fascia, fenders and cleats. Arch/tower visual beams/floors now share one LitVC batch per plateau instead of individual primitive renderers/materials, retaining beam endpoints/cross-sections; added diagonal lattice within tower leg envelope. Road and its existing paint retained. Four public-API editor detail views added, old names/cameras preserved.
- **Budget / remaining work:** no new materials/textures/shaders/render targets/passes/assets, existing metas cover all four edited Unity sources. Facade cost capped at 6 columns × 24 rows × 4 faces (576 quads / 2,304 vertices per tower maximum, about 104 KiB vertex + index payload at 40 bytes/vertex and uint indices, excluding CPU lists/copies); actual count follows dimensions. Uses the same single opaque batch, no per-pane draw calls or transparent layers. Landmark combining removes many renderer/material instances, but this does not establish a measured iPad frame/memory budget. Existing reflection tiers/masks unchanged; Balanced/High will also draw added panes. Capped rows can stretch floor spacing on the tallest towers; no pane screen-size fade, so Chief must check shimmer. No extra roof structures near rooftop challenges. Still needs better city greenery, less sparse block massing, base/carrier facade detail and waterfront integration after usable detail renders. Shared forest/collision and all gameplay geometry untouched. Built-in pipeline only, no effect proved blocked on URP.
- **Validation / handoff:** source inspected facade orientation, batch APIs and editor public Core members; compile remains unverified. No Unity/compiler/tests/build/git run under sandbox instruction. Chief must compile Bridge/Editor separately and render all WorldRender shots including `city-facade-detail`, `eiffel-detail`, `arch-detail`, `avalon-detail`, plus complete fixed WaterRender set into B4-1. Check roof/pane normals, sidewalk overlap, aliasing and GPU/memory cost before accepting. No reference photos copied, no Core/Sim/config/flight-test/GLB/prop-hub/chart changes, no commit/push/merge/deploy; left uncommitted for runner.

### 2026-10-28 — Codex · lane/codex-visuals · pass 3, batch B3 iteration 2

- **Self-review:** opened all eleven requested B3-1-world shots and nine B3 detail views plus all six airport/grass/harbour reference JPEGs. World ERRORS contains the Metal `line` syntax error; water ERRORS empty. Airport, gravel/grass, seaplane and carrier **1/10 presentation**: magenta surfaces are a regression, so material realism cannot honestly be scored. Pad **2/10**: seams now visible but still a plain disc on basic supports. Grass detail shows surface/terrain intersections. Seaplane apron/hangar remains buried in trees; carrier angled paint extends over water, island is still blocky. Photos show subdued surface variation, mowing/wear and modest buildings; small-aerial-2 is a much larger airport, harbour photos do not establish seaplane or carrier markings. Carrier/pad scores remain provisional realism judgements without matching photos. Opened all three sunset cases, Beaver spray, J-3 wake, AirCam bow and idle wake: sunset **5/10**, wakes/spray **4/10**, scenery reflection **2/10**. Existing slab/slivers and stepped Balanced reflection persist; day reflections also contain magenta airport surfaces. No outgoing scores claimed.
- **Fixes:** renamed HLSL reserved `line` helper to `surfaceLine` at declaration and all calls. Added built-in fog variants/interpolator/application; matched surface sunset fill to existing LitVC, retaining reflection half-space guard. Centred wrapping asphalt/timber joints for symmetric filtering; fade medium-scale noise with pixel footprint and add subtle broad grass dry variation. Carrier `Stripe` clips paint endpoints against deck rectangle inset by half stripe width, preventing guide lines/tie-downs floating beyond the deck. All original deck/hull/island/wire/catapult geometry and sim dimensions retained. Apron tee placement checked against existing slab and left unchanged. No more dressing added before the broken finishes can be assessed.
- **Limits / handoff:** shared forest trees may have sim collision, so no hiding/removal; grass mesh/terrain intersections left unchanged under the geometry restriction. Airport seven-segment numerals are still not exact FAA stencil outlines. Chief must compile Bridge and Editor separately, import AirportSurface on Metal and verify player inclusion, then rerender every WorldRender and fixed WaterRender case into B3-2 folders. No Unity/compiler/tests/build/git run in this sandbox; source inspection only. Existing metas cover both edited Unity assets. No new assets/photos/textures/targets/draw calls or passes; fog adds variants and surface arithmetic still needs iPad timing. Water/sky/reflection/wake code and quality tiers untouched. Built-in pipeline only; no effect demonstrated blocked on URP. No forbidden Core/Sim/config/flight-test/GLB/prop-hub/gameplay/collision changes, no commit/push/merge/deploy; left uncommitted for runner.

### 2026-10-28 — Codex · lane/codex-visuals · pass 3, batch B3 iteration 1

- **Self-review:** opened all eleven requested B2-2-world B3 shots and all six airport/grass/harbour reference JPEGs in the image viewer. Both ERRORS summaries empty. Airport **2/10** (very narrow flat surfaces, sparse apron; Valley shot largely city and gates, Summit too distant for paint). Gravel/grass **1/10 presentation**: both original cameras are underground, so honest surface-detail scoring is unavailable. Pad **2/10** (plain black disc, basic supports). Seaplane base/docks **2/10** (featureless concrete/timber; trees grow through pad/hangar, dominate both views). Carrier **2/10** (flat slab, block island, continuous stripe under dashed landing centreline, little deck detail). Photo small-aerial-1 is a grass field, useful for mowing and low hangar scale; small-aerial-2 is a much larger coastal airport. Harbour-1 is a dark sunset anchorage, harbour-2 a historical industrial port: neither establishes seaplane-base or carrier markings. No carrier photo supplied, so carrier score is a provisional realism judgement, not a matched carrier-photo comparison. Opened incoming J-3/Beaver/Balanced sunsets, Beaver spray, J-3 wake, AirCam bow and idle wake: sunset **5/10**, wakes/spray **4/10**, scenery reflection **2/10**; day slab/slivers still conspicuous. No outgoing scores claimed.
- **Changes:** new original `AirportSurface.shader` provides metre-scale aggregate/mottling, sparse repair cells/joints, touchdown rubber, concrete panels/stains, gravel wheel tracks, grass mowing/wear and timber plank grain. Used only on existing airport/base surfaces plus a thin carrier deck finish; no surface footprint/height/transform changes. Fine grain fades with pixel footprint and thin joints broaden analytically. Included shader in BuildScript runtime shader list; new tracked-source shader has its own meta. Threshold bars use width-dependent counts and a central gap; long-runway TDZ sets corrected/restricted to avoid reciprocal overlap, short crosswind omits TDZ. Numbers remain the existing block construction, not exact FAA stencil outlines. FAA AIM reference: https://www.faa.gov/air_traffic/publications/atpubs/aim_html/chap2_section_3.html ; consulted official AC markings guidance for stripe spacing/aim dimensions. Not claiming a surveyed or fully standards-compliant airport.
- **Dressing:** one opaque batch per airport adds parking tees/lead-in on the existing apron, two simple original static light-aircraft silhouettes, small office/service box and hangar standing seams; existing windsocks retained. No new taxiway pavement outside sim paved areas. One seaplane batch adds dock cleats/fenders; existing ramp/docks/pad dimensions and pontoon placement retained. Carrier removes the solid underlay beneath dashed centreline, adds alternating foul-line segments, tie-down crosses, deck-edge catwalk ribs and island glazing in its existing batch; deck finish adds one opaque draw. Existing hull/deck/island geometry, wires, catapult locations, movement and wake untouched. Pad gets fine expansion seams in its existing city batch; retains fixed-wing ring/centreline purpose and all landing geometry.
- **Cameras / limits:** nine extra terrain/public-data-derived inspection shots: `valley-airport-detail`, `runway-threshold-detail`, `airport-apron-detail`, `summit-airport-detail`, `stol-strip-detail`, `grass-strip-detail`, `valley-pad-detail`, `seaplane-base-detail`, `seaplane-dock-detail`. All old names/cameras retained. Camera clearance uses terrain height; trees/buildings/framing still need render review. Seaplane trees come from the shared forest and may have collision: left visible rather than hiding collision-bearing obstacles. Chief/owner needs a separate collision-authorized forest-clearance decision if the base should be usable/clear. Existing airport numerals/paint cube draw count and block aircraft props remain realism limits; next iteration must judge actual close views before further elaboration.
- **Budget / validation:** no photo copied, no textures/render targets/transparent passes. Surface replacements keep their existing draw counts; additional dressing is four airport draws, one dock draw and one deck finish draw for the world (plus existing reflection rendering where included). Added small geometry in existing carrier/city batches. Existing reflection tiers, water/sunset/wake shaders, fixture cameras and particle caps untouched; scenery can change reflections and requires full WaterRender regression. New procedural ALU, grain shimmer and opaque deck overlay need iPad timing. Source-only review: balanced braces, existing metas for edited assets, new shader meta, runtime inclusion and public Editor member access inspected. No compiler/Unity/tests/build/git attempted under this loop's explicit restriction. No Core/Sim/config/flight-test/GLB/prop-hub changes, no physics/collision edits, no push/merge/deploy; left uncommitted. Built-in pipeline only; no effect demonstrated blocked or requiring URP.

### 2026-10-28 — Codex · lane/codex-visuals · pass 3, batch B2 iteration 2

- **Self-review:** opened seven requested B2-1-world originals and all seven detail views, all six B2 reference photos, J-3 reference/render comparison, Beaver and Balanced sunsets, Beaver spray, J-3 wake, AirCam bow and idle wake. Both ERRORS files empty. River **2/10** (flow/rapids not readable in supplied views), lake water **3/10** (better dark teal depth, still flat and regular), shore integration **1/10** (hard cliff/grass boundaries, no convincing wet shore), waterfall **2/10** (visible layered sheet, but huge stretched streaks, weak base, front camera crops both endpoints). Slot camera looks upward; lip camera is obstructed by the shelf. Old original cameras remain underground. Forest and cliff simplification remain major contextual limits. River photo 2 is misty forest; photo 1 is an engineered channel. Waterfall photos support finer sheet breakup and a distinct impact cloud, not this world's dimensions. Sunset **5/10**, wakes/spray **4/10**, reflection **2/10**; existing day slab/slivers persist. No outgoing scores claimed.
- **Changes:** falling sheet noise now varies down world height at metre scales rather than stretching six cells over the whole cliff; additional wandering streak coordinates, denser lower aeration and fewer large holes. Mist uses a wider soft extinction profile and stronger alpha without adding cards. Impact height now iteratively samples `WaterSurfaceAt` at the ballistic endpoint, so a lake drowning the river can stop the visual fall at the visible water. This addresses a concrete source mismatch, but its role in the nearly missing B2-1 pool is unproven until rerender. Foam draws after water explicitly. Original lip and horizontal fall trajectory retained; no collision/sim changes. Front-detail camera pulled back to fit the full fall; three extra inspection shots target impact, river reach and ordinary lake shore. Existing names retained.
- **Water:** day scenery replaces crossing sinusoidal detail with three irregular value-noise samples, slightly lighter depth colour and a 1.2 m optical-depth alpha transition (was .35 m) into the bank. Lake boundaries and heights unchanged. Sunset disables these scenery changes; wake/foam history, reflection cameras and sunset fixture untouched. Generic water with `_SceneryWater=0` retains prior ripples. The day scenery shader changes still require the complete WaterRender regression set; no claim of unchanged rendered output without Chief's check.
- **Budget / validation:** same meshes, draw count, texture memory, render targets and reflection tiers; more per-pixel noise arithmetic on day scenery and broader existing mist coverage need iPad timing/overdraw review. No new assets; existing tracked metas present for all four edited Unity assets. Source checks only: balanced braces, public Editor API calls and nullable water sample handling inspected. No Unity/compiler/tests/build/git attempted per this loop's restriction. No refs copied, no Core/Sim/config/flight-test/GLB/prop-hub/gameplay edits; left uncommitted, no push/merge/deploy. Built-in pipeline; no demonstrated URP blocker. Chief must assess the new pool height/foam ordering and shore gaps before considering this visually improved.

### 2026-10-28 — Codex · lane/codex-visuals · pass 3, batch B2 iteration 1

- **Self-review:** opened all seven B1-2-world B2 shots, both river, waterfall and calm lake photos at full image-viewer resolution, plus B1-2 J-3 sunset/reference comparison, Beaver sunset/day spray and J-3 wake. Both ERRORS files empty. Incoming river **2/10**, lake water **2/10**, shore integration **1/10**, waterfall presentation **1/10**. Water is flat saturated blue, banks abrupt, no readable flow or white impact pool. Old waterfall-front/slot/lip and bridge cameras are underground; seaplane-lane is inside trees; river-lake mostly frames the coast, gorge-flight frames the canyon lake. Cannot honestly score close waterfall detail from these. River photo 2 is actually misty forest (no river); photo 1 is an engineered channel, useful for bank/colour contrast but not a natural meander target. Waterfall photos are narrow falls, useful for sheet breakup and impact separation rather than matching this world's much broader fall dimensions. Sunset **5/10** (better gold, still flat aircraft/cutout shore); wakes/spray **4/10**; scenery reflection **2/10**, slab/slivers visibly persist. No outgoing scores claimed without fresh renders.
- **Changes:** lake water now uses 96-segment, eight-ring meshes concentrated near shores; rivers and canyon lake gain interior cross-channel samples. Optional mesh UV2 carries actual terrain-to-water optical depth. Day scenery water gets a deeper blue-green body, mineral shallows and sub-metre alpha transition into the existing bank. Terrain vertex colours add a narrow damp mineral band at ordinary/alpine lake, river and canyon waterlines. No terrain positions/normals or water surface heights/boundaries changed. River-only scrolling procedural normal detail follows each river's axis; slope-gated broken foam marks rapids. No wave solver/foam history changes. New properties default off and day palette/edge effects switch off at sunset, preserving the portrait's water compositing path. WorldRender explicitly requests depth, retains all old shots and adds seven terrain-derived `*-detail` inspection shots. The slot shot intentionally stays in the recess; external cameras clear terrain, but occlusion/framing still needs Chief's review.
- **Waterfalls:** retained original lips, fall paths and two sheets; added 16 cross-sheet subdivisions with small downstream ragged offsets, darker translucent upper water and more aerated lower streaks with holes/soft edges. Each fall gains one softly bounded turbulent foam oval and one batch of 32 perforated rising spray fragments. Existing three mist cards retain their footprint and get softer base/height/noise extinction. Procedural only; reuse existing Waterfall shader and pass-3 water noise/foam approach rather than adding textures or another simulation. Spray remains bounded cards, not volumetric fluid; pool is an oval approximation, and exact terrain/pool intersections must be checked in the new renders. No built-in pipeline effect demonstrated blocked; no URP request.
- **Budget / validation / limits:** no new assets, textures, targets, simulation steps or reflection passes; all five edited Unity assets have existing metas. Water shader stays one pass and water tiers/targets are unchanged. Two extra transparent draws per fall (six total); falling sheets rise from 192 to 3,072 triangles per fall, plus 128 triangles for pool/spray. Each ordinary/alpine lake is 1,440 single-sided triangles versus 192 duplicated triangles previously; shader already uses Cull Off, so redundant backfaces were removed from changed water meshes. River/canyon cross sections increase geometry and depth channel adds 8 bytes per vertex; river pixels add two value-noise evaluations. Existing LOD/culling remains, no new quality setting or texture memory. iPad ALU/overdraw/timing is unverified. Source inspection checked indices, winding, shader fields, balanced delimiters, .meta presence and Editor calls against public runtime APIs; this is **not compilation**. No Unity/compiler/tests/build/git attempted under this loop's explicit restriction. No refs copied or tracked, no Core/Sim/config/flight-test/GLB/prop-hub/gameplay changes, no commit/push/merge/deploy. Chief must rerender WorldRender and every WaterRender case before judging regressions or quality.

### 2026-10-28 — Codex · lane/codex-visuals · pass 3, batch B1 iteration 2

- **Self-review:** opened all ten requested B1-1-world shots plus hungryridge-high, reference contact sheet and full-size daytime sky, sunset altocumulus, mountain, desert and forest photos. Opened latest J-3 comparison, Beaver/Balanced sunset, day Beaver spray/raw reflection and J-3 Kelvin wake. Both supplied ERRORS summaries empty. Scores vs photos: day sky/lighting **3/10** (flat cloud sheets, vertical horizon curtains), mountains **3/10** (plaid grid-like detail, smooth cones, snow icing), desert **3/10** (warmer but still smooth brown domes, weak beds), forests **3/10** (isolated angular cones, pale floor, patch boundaries), sunset **5/10** (some gold survives, but dominant lavender, weak localized glow, cutout shore). Kelvin V remains legible; day reflection slab/slivers remain unresolved. Hungryridge's old camera is still below terrain; high shot is usable. Photo mountain 2 is a rocky desert ridge rather than a snow target, so only useful for erosion/detail; forest photo 1 also includes standing dead trees and open regeneration, not a uniformly closed canopy.
- **Changes:** day cloud coverage fades before the projection clamp to target the horizon curtain, with larger separated banks. Terrain uses rotated world projections and weaker nonperiodic grain/joints to reduce checks, broader beds subdued in alpine grey and stronger in sandstone, restrained snow crevice variation and continuous darker crown-scale canopy. Existing aerial haze retained; no terrain positions/normals/collision changed. Sunset broad gold-to-rose transition, stronger aureole and warm sun-facing cloudlets share the same function with water. Low sun direction moved toward the portrait view; sun and shader direction stay identical. Fixture camera lowered to .85 m, closer with 68-degree FOV and less lateral offset; bounds fit retained. Shore crowns broadened, varied in height and spread across deeper rows, plus one muted distant ridge to fill gaps. No outgoing quality claim without renders.
- **Budget / validation / limits:** same shader noise sample counts, no textures/render targets/passes, no new assets (all four edited assets retain existing metas). Sunset fixture alone adds one opaque draw and 358 triangles for the haze ridge; existing crown triangle count unchanged. Runtime streamed tree mesh and collision footprints unchanged. Mountain silhouettes and near tree cones remain limitations; no built-in effect proven blocked and no URP request. Reviewed shader variable usage, balanced source delimiters and public editor calls; no Unity/compiler/tests/build/git attempted per current loop instructions. Reference photos remain only in ignored build paths. Left uncommitted for runner; no Core/Sim/config/flight-test/GLB/prop-hub changes, push/merge/deploy.

### 2026-10-28 — Codex · lane/codex-visuals · pass 3, batch B1 iteration 1

- **Self-review:** opened all ten requested it0-world B1 shots, it4 day Beaver spray and J-3 sunset, both daytime sky, mountain, desert and conifer reference photos. Both ERRORS.txt files empty. Incoming scores vs photos: sky/lighting **2/10** (solid blue, no aerial depth), mountains **3/10** (smooth stretched ribs, white icing, compressed silhouettes), desert **2/10** (featureless brown domes and field grid), forests **3/10** (square cone skirts and painted ground). Mountain photo 1 is sunset, not a day colour target; conifer photo 2 is a sparse regenerating stand, not a mature canopy target. No outgoing scores claimed without new renders.
- **Changes:** existing included sky shader now selects an analytic day gradient, small solar disc, broad glow and separated soft cloud banks when sunset is off. WorldBuilder installs it for gameplay; WorldRender and day WaterRender now actually use skybox clear. SunsetSky function and sunset setup remain unchanged. Terrain adds world-space irregular strata/joints/grain, slope-limited rock detail and restrained snow crevice shading, canopy mottling tied to the existing Forest.CoverAt result encoded in negative vertex alpha, and blue distance/height haze. Mesh colours are float Color arrays. Thin dark field boundaries replace the pervasive bright graph-paper treatment in day; desert no longer carries a farm mask. Red-rock palette is warmer. No terrain vertices, normals, heights, strips or collision changed. Existing four-face tree skirts get uneven branch radii and less alternating face colour, at the identical triangle/chunk/draw-call budget. Added `desert-hungryridge-high` because the existing named shot is below terrain; kept all old names and cameras.
- **Limits / next review:** analytic cloud banks are a starting point, not volumetric cumulus; existing spherical thermal clouds remain. Shader detail cannot fix smooth mountain silhouettes without visual mesh refinement. Forest canopy remains ground shading beyond streamed trees. Haze currently applies to terrain only; distant props can contrast too strongly. it4 reflection slab remains visibly unresolved; no reflection-camera change in B1. Water/sunset need the full Chief regression set, not an assumed pass. No built-in effect proved blocked; no URP request.
- **Budget / validation:** no textures, assets, passes, render targets or increased tree geometry; existing reflection tiers and streaming budgets retained. Four terrain value-noise evaluations per day fragment plus analytic haze increase ALU; band/canopy fine detail attenuates with screen footprint, but mobile timing and shimmer remain unverified. Reviewed Spire callers, shader varyings, public editor/runtime calls and existing .meta presence. No reference photos copied or tracked. No Unity/compiler/tests/build/git, forbidden source/config/flight-test/GLB/prop-hub edits, push/merge/deploy. Left uncommitted for runner.

### 2026-10-28 — Codex · lane/codex-visuals · pass 3, iteration 4

- **Self-review:** opened target photo, it3 J-3/Beaver/Balanced sunset, day Beaver spray/raw reflection/scenery-off, J-3 wake/bow, AirCam bow and idle wake; compared local Kelvin and prop-spray photos. Scores: sunset **5/10**, scenery reflection **4/10**, wakes/spray **4/10**. it3 ERRORS.txt empty. Mirror brightness is much improved and aircraft fills width, but aircraft still looks synthetic with corrugated shading; trees remain sparse triangular silhouettes, clouds too uniform, Balanced edges visibly stepped. Broad field slab is largely gone, but cliffs reflect as isolated slivers and harbour details as thin streaks. Wake has long smooth rails, circular interior cues, weak turbulent step water and bead-like mist. Idle tow correctly retains water disturbance at 18 m/s. No outgoing scores claimed.
- **Reflection:** removed grazing-angle oblique projection (which tilts the far clip along with the near plane) and uses a conventional finite perspective frustum. Water half-space clipping remains in Terrain/Lit/LitVC/Airframe and now Spray. Removed the previous Terrain visibility discard based on interpolated smooth normals: those are not triangle-facing normals and can cut valid cliff faces into strips. Rasterizer winding remains active with mirrored culling. **Source diagnosis, not verified fix:** Chief must compare raw reflection and scenery-off and check whether the slab returns; do not approve on this explanation alone. Other special transparent shaders (glass/waterfall/text) do not have the explicit water guard and need inspection if submerged artifacts appear; no scenery hidden to pass review.
- **Sunset:** replaced nearly flat hemisphere fill with a stronger upward/downward sky response while retaining cool underside fill and warm sun/rim. Reduced exaggerated procedural detail-normal depth (day .25 of previous, sunset .025) without changing geometry or materials. Cooler clear upper sky, longer cloud-free horizon transition and slightly softer cloud opacity. Four irregular depth rows of narrower conifer crowns replace two sparse rows; same low rear-left camera and full wing/reflection fit retained. Still procedural scenery; no photorealism claim.
- **Wakes/spray:** wider irregular cusp foam edges, earlier downstream breakup, stronger short step turbulence and weaker transverse foam. Reduced only optical solver slope and foam-history visibility to suppress concentric forcing rings; solver state/physics constants untouched. Trail mist emits from a low narrow box rather than a large sphere, with lower launch, smaller particles and more gravity; slightly larger sheet fragments target thin step spray. Existing draft/speed/load/prop-RPM/clearance gates and particle caps retained. Analytic wake follows instantaneous heading; curved history remains a limitation. No new render targets or passes; shoreline stage mesh grows, so device timing remains required.
- **Validation/scope:** source review only, including Spray interpolator/half-space wiring and existing tracked metas for every touched Unity asset. No new assets, Core/Sim/config/flight-test/GLB/prop-hub changes. No Unity/compiler/tests/build/git run; Chief owns Bridge/Editor compilation and seven renders. Built-in pipeline, tier dimensions and memory allocations preserved. No effect established as blocked on URP. Left uncommitted; no push/merge/deploy.

### 2026-10-28 — Codex · lane/codex-visuals · pass 3, iteration 3

- **Self-review:** opened target photo, it2 sunset J-3/Beaver/Balanced, day Beaver spray and raw reflection, J-3 wake/bow, AirCam bow, idle wake, and local water photos. Incoming scores: sunset **5/10**, scenery reflection **2/10**, wakes/spray **4/10**. Aircraft fills the width but still reads mustard-flat, floats are pale plastic, cloud texture is too uniformly fine, shoreline has an artificial straight base and regularly spaced trees. Water loses sky brightness far too quickly. Balanced reflection visibly stair-steps. Wake arms are dotted chains with circular interior ripples and weak near-step turbulence. One supplied takeoff-labelled reference is an airborne aircraft, not usable takeoff-spray evidence. `it2/ERRORS.txt` is empty. No post-change scores claimed.
- **Slab:** raw reflection includes the green field-grid slab, ruling out a water-body-color/compositing-only cause. Existing world-height discards cannot reject above-water plateau undersides viewed from the submerged mirrored eye. Terrain now explicitly rejects surfaces whose interpolated world normal faces away from that eye during reflection; retains water half-space clipping, oblique projection, main-view rendering and scenery masks. This is a targeted visibility correction, **not a confirmed root-cause diagnosis or verified fix**. Next renders must show whether it removes the slab; if not, isolate individual terrain batches and inspect oblique far-plane truncation. Fixture additionally writes `reflection-scenery-off.png` at the identical spray camera, respecting/restoring the requested scenery flag. No runtime extra pass from this diagnostic.
- **Sunset:** camera now behind-left at 1.05 m above water (lateral offset 48% of rear distance), retains aircraft bounds fit. Calm sunset reflection gets a .72 minimum mirror contribution instead of dropping toward near-black across the lower half; reflection silhouettes and sky receive the same weight. Broad analytic sky-environment highlights added to Airframe and Lit, without cloud noise/cubemap passes on those surfaces; sunset detail normals reduced further. Cloudlets have larger scales and softer/lower opacity. Stage bank lowered and tree bases sunk into it; clustered tree heights, wider shore-depth variation and irregular offsets reduce the ruler-row effect. Full photorealism is not established; the stage still uses procedural conifer geometry.
- **Wake/spray:** replaced serial arm-breakup gates with a connected young foam line transitioning to lengthwise broken streaks with age; raised short turbulent step foam, preserving the two outer V arms and faint transverse bands. Reduced optical wake-slope multiplier from 2 to .65 to stop exaggerating the solver's radial forcing pulses into rings; no solver constants or height field changed. Slightly larger/longer spray fragments with lower launch angle target thin step/chine sheets, using existing speed/draft/load signals and particle caps. Existing prop RPM/clearance gates retained. Instantaneous analytic foam still does not record curved heading history or linger after lift-off; no claim of resolving that limitation.
- **Validation/scope:** source inspection only; all modified files already have metas, no new assets. No Unity/tests/build/git attempted. No physics/config/flight-test/GLB/prop-hub edits. Built-in pipeline only, same reflection targets/tiers/particle caps; added cheap rough environment arithmetic needs device timing. Bridge/Editor compile and all seven Chief renders remain required. Left uncommitted for runner; no push/merge/deploy.

### 2026-10-28 — Codex · lane/codex-visuals · pass 3, iteration 2

- **Self-review:** opened the real target, it1 sunset J-3/Beaver/Balanced, day Beaver spray, J-3 wake/bow, AirCam bow, idle wake and local water reference photos. Incoming scores: sunset **5/10**, reflection correctness **2/10**, wakes/spray **4/10**. Aircraft and reflection are much larger and readable now, but wings nearly touch the edges, fuselage still looks flat, normal detail is exaggerated, clouds look like camouflage and the trees like stacked triangles. Balanced has visible reflection stair-stepping. Harbour slab survives the prior Terrain-only clipping. Wakes have a clear V but four too-regular rails; near-step whitewater is weak and the spray rises as a diffuse plume. Idle tow still produces wakes, correctly for its 18 m/s speed. Supplied ERRORS.txt is empty. No new scores claimed for edits below.
- **Reflection:** the prior half-space discard was present only in Terrain; Lit/LitVC (including batched harbour/island structures and submerged geometry) had no equivalent fragment guard and are two-sided. Added the same interpolated world-plane distance discard to Lit, LitVC and Airframe, only during planar rendering. Preserved the oblique plane, mirrored frustum and water-layer exclusion. Added fixture output `reflection-source.png` (RGBA) for the spray view, exposing the actual planar camera result before water Fresnel/distortion. **The exact offending renderer and slab root cause are still not conclusively established without Chief's next render**; this closes a concrete clipping gap, not proof the visible slab is solved. If it remains in source, Chief should isolate the Terrain and LitVC batches; if source is clean, inspect water UV/compositing instead. Do not hide scenery to mark this resolved.
- **Sunset:** increased hemisphere sky/lake fill in custom aircraft/scenery shaders; reduced only sunset airframe detail-normal strength to remove the ribbed highlight exaggeration and tinted the rim warm. Kept low warm sun. Reworked cloud density with finer multiscale cloudlets, continuous density and lit/shadow variation, cooler blue/lavender overhead and softer gold horizon. Reduced the harsh calm-water sun streak. Fixture leaves a 10% width margin and uses a slightly more off-axis rear camera; shoreline crowns now have nine irregular short tiers rather than three broad triangles, with lower varied heights. Fixture-only geometry cost rises; no runtime forest changes.
- **Wakes/spray:** existing wet-state, speed, beam, heading and hydrodynamic load still drive the supplemental foam. Each float contributes its outward arm (single hull retains both), avoiding four parallel rails. Arms wander subtly, break into patches and fade faster; short expanding turbulent step trails gain breakup, while transverse interior bands remain faint. Spray sheets now have narrower sizes, stable orientation, more velocity stretch and lower outboard/aft fan trajectories. Prop clearance/RPM gates and all particle caps are unchanged. Supplemental analytic foam still follows instantaneous heading, so sharp turns and residual foam after lift-off remain limitations; the solver/history is unchanged.
- **Validation/scope:** source inspection only; no Unity, compiler, tests, builds or git commands attempted. No physics/config/flight-test/GLB/prop-hub changes. Built-in pipeline, reflection tiers and all fixture flags preserved. No new source assets, so existing metas cover all edits. No extra runtime render passes, targets or particle counts; finer sky noise adds shader arithmetic and needs iPad measurement. Chief must compile Bridge/Editor separately and render all seven fixed cases into `it2` before judging improvements. Work left uncommitted for runner; no push/merge/deploy.

### 2026-10-28 — Codex · lane/codex-visuals · pass 3, iteration 1

- **Self-review:** opened target photo, sunset J-3/Beaver/Balanced, day Beaver spray, J-3 wake/bow, AirCam bow, idle wake, and local wake/spray reference photos. Baseline scores: sunset 4/10 (muddy aircraft, simple triangular conifers, coarse clouds; Beaver wings already crop), scenery reflection 2/10 (large green terrain slab), wakes/spray 2/10 (mist stamps, no legible foam V, weak step whitewater). Some locally named spray references actually show airborne aircraft or quiet taxi; did not treat those as takeoff spray evidence. `it0/ERRORS.txt` is empty. These are baseline scores, not scores for the new code.
- **Sunset:** fixture camera now fits rendered aircraft bounds to 94% width, slightly off-axis, 1.4 m above water, with a lower aim point to retain the full reflection. This moves the Cub closer and moves the wider Beaver back enough to avoid cropping. The actual source of dark aircraft was the hard-coded .19/.22/.30 sunset fill in Airframe/Lit/LitVC, independent of RenderSettings ambient; raised it to .48/.46/.50 while retaining warm directional light and specular. Raised built-in ambient ground fill for other materials.
- **Reflection investigation/fix:** WaterLayer is already assigned by WorldBuilder.Spawn and excluded from the reflection; the slab shows terrain/field colours. Added explicit world-space half-space clipping in Terrain during reflection only, rejecting fragments below water + 3 cm, alongside the oblique plane at that same height. Explicitly sets the mirrored oblique culling frustum after CopyFrom. Reflection-only globals and winding restore in finally. This targets submerged terrain and stale frustum causes without hiding scenery. **The root cause is not yet conclusively isolated by an A/B render**; Chief's day captures must establish whether this removes the slab or further mesh/material investigation is needed.
- **Foam/spray:** publishes two wet-step origins, measured speed, attitude heading, beam and hydrodynamic load; a bounded-strength cosmetic 19.5-degree Kelvin envelope adds broken fading arm foam, compact step whitewater and faint transverse crest bands inside the V. Existing solver height/history/forcing/constants are untouched. Analytic envelope follows current heading rather than recorded curved tracks; this is a known limitation for sharp turns and lift-off loses this supplemental envelope while solver/history remains. Step spray uses square-root drag response and denser emission; bow/chine/step emitters use narrow boxes rather than spherical clouds. Prop entrainment gets mist material and moderately higher emission with existing disc-clearance/RPM gates retained.
- **Budget/scope:** no new textures, assets, render targets or passes. Existing reflection quality tiers/resolution overrides remain. Additional per-water-pixel arithmetic for two float envelopes skips inactive origins; mobile cost still needs measurement. Existing particle caps retained. Built-in pipeline only, no effect established as requiring URP. No physics/config/test/GLB/prop-hub changes. No Unity/compiler/tests/build/git commands attempted. All edits left uncommitted for runner; all touched assets already have metas. Chief owns the fixed seven-case rerender and validation; look remains unapproved.

### 2026-10-28 — Codex · lane/codex-visuals · editor assembly fix-up

- Replaced only the two direct internal `WorldBuilder.Spawn` calls with a fixture-local reflection wrapper, matching existing non-public fixture access and retaining mesh/water-layer behavior; no game API changes. Audited every editor addition in `d0e492d` / `33f23d4`: `WaterRender`'s remaining directly accessed runtime types/members are public; private lifecycle/solver/texture accesses already use reflection; `BuildScript` added only shader-name strings. No other accessibility fixes needed.
- Compiled **all** current Bridge scripts into `FlyingGame.Bridge.dll`, then **all** Editor scripts referencing that new DLL (no Bridge sources or cached Bridge DLL in the Editor check). Both passed with warnings only. Core/Sim and package references remain cached Unity assemblies; this is an accessibility/C# check, not Unity shader import or a player build. A control run using committed `WaterRender` failed with exactly the two reported CS0117 errors. Response files/logs are ignored under `build/renders/check-*`; regenerate and run from this worktree:

```sh
python3 - <<'PYCOMPILE'
from pathlib import Path
base = Path('build/renders/compile-sunset.rsp').read_text().splitlines()
refs = [x for x in base if x.startswith('-r:') and 'FlyingGame.Bridge.dll' not in x and 'WaterVisualCompile.dll' not in x]
refs += ['-r:"' + str(next(Path('unity/Library/PackageCache').glob('com.unity.nuget.newtonsoft-json@*/Runtime/Newtonsoft.Json.dll'))) + '"']
for kind, assembly in [('Bridge', 'Bridge'), ('Editor', 'EditorTools')]:
    lines = ['-target:library', '-langversion:9', '-define:UNITY_EDITOR,UNITY_STANDALONE_OSX', '-out:build/renders/FlyingGame.' + assembly + '.dll'] + refs
    lines += ['-unsafe'] if kind == 'Bridge' else ['-r:build/renders/FlyingGame.Bridge.dll']
    lines += [str(p) for p in sorted(Path('unity/Assets/_Project/Scripts', kind).rglob('*.cs'))]
    Path('build/renders/check-' + kind.lower() + '.rsp').write_text('\n'.join(lines) + '\n')
PYCOMPILE
~/.dotnet/dotnet /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/Resources/Scripting/DotNetSdkRoslyn/csc.dll @build/renders/check-bridge.rsp > build/renders/check-bridge.log 2>&1 &&
~/.dotnet/dotnet /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/Resources/Scripting/DotNetSdkRoslyn/csc.dll @build/renders/check-editor.rsp > build/renders/check-editor.log 2>&1
```

- `git diff --check` passes. Only `WaterRender.cs` and this log changed; no look restart, Core/Sim/config/test/GLB/prop-hub changes, commit, push, merge or deploy. Chief owns the render/suite/dummy-UDID build rerun using the continuation commands below.

### 2026-10-28 — Codex · lane/codex-visuals · sunset photo target continuation

**Chief commands (from this worktree; graphics required, never `-nographics`):**

```sh
water_render() {
  /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "$PWD/unity" -executeMethod FlyingGame.EditorTools.WaterRender.RenderFromArgs "$@"
}
# Four calm sunset portraits + reference comparisons, plus existing three views in each folder.
for aircraft in j3-cub-floats-like pa18-floats-like dhc2-beaver-floats-like aircam-amphib-like; do
  water_render -aircraft "$aircraft" -preset sunset -calm -reflectionQuality High -reflectionSize 2048 -out "$PWD/build/renders/sunset-$aircraft" -logFile "$PWD/build/renders/sunset-$aircraft.log"
done
# Actual world, moving/spraying, including terrain in reflections; compare foam to d0e492d PNGs.
for aircraft in j3-cub-floats-like pa18-floats-like dhc2-beaver-floats-like aircam-amphib-like; do
  water_render -aircraft "$aircraft" -out "$PWD/build/renders/revised-$aircraft" -logFile "$PWD/build/renders/revised-$aircraft.log"
done
# Tier defaults, without resolution override. Low intentionally omits scenery.
for tier in Low Balanced High; do
  water_render -preset sunset -calm -reflectionQuality "$tier" -out "$PWD/build/renders/sunset-$tier" -logFile "$PWD/build/renders/sunset-$tier.log"
done
water_render -throttle 0 -out "$PWD/build/renders/revised-idle" -logFile "$PWD/build/renders/revised-idle.log"
water_render -lift 5 -out "$PWD/build/renders/revised-airborne" -logFile "$PWD/build/renders/revised-airborne.log"
~/.dotnet/dotnet test tools/FlightTests/FlightTests.csproj -p:UseSharedCompilation=false
tools/deploy/deploy.sh 00000000-0000-0000-0000-000000000000
```

- **Look at outputs:** `sunset.png` is the new 1000×1600 rear portrait; `reference-left-render-right.png` places Ty's unchanged lock-screen reference on the left and the render on the right, scaled with aspect preserved. Ignore the photo's UI when judging. All folders still contain `spray.png`, `wake.png`, `bow.png`. Chief should inspect shader compiler messages/pink objects, full wing/strut/float reflections, shoreline reflection continuity, gold-to-lavender sky and foreground darkening, absence of foam at rest, and retention of spray/wake under tow. New images cannot be produced inside this sandbox; the fixture produces the comparison during Chief's run.
- **Evidence reviewed first:** all 15 supplied before/after PNGs (baseline plus J-3/PA-18/Beaver/AirCam), reference photo, and render logs. No shader compiler errors found in supplied logs. Logs do contain pre-existing edit-mode `Destroy` errors from world construction (`PlaygroundBuilder` etc.), licensing-token/USB messages; they are not clean logs. First-pass foam visibly saturates into a wide continuous white trail on every type. Aircraft shapes themselves remain visibly procedural on several fleet types; no model/axis/hub changes were made here.
- **Sky/light:** new shared `SunsetSky.cginc` generates a gold horizon, rose/lavender mottled cloud deck, low sun and soft aureole. `SunsetSky.shader` draws the built-in skybox; water evaluates the same world-direction sky for consistent sky reflections without rendering a skybox into the planar texture. `SunsetLighting.Apply` supplies a warm 2.6-degree sun and trilight ambient. Custom Airframe/Lit/LitVC/Terrain lighting now responds to the opt-in sunset global; daytime shading remains its existing values. Runtime developer opt-in: launch with `AERO_SUNSET=1` (no new product UI). Sky shader is in BuildScript's always-included list.
- **Reflections:** Low = 384-pixel longest edge, aircraft/props only, 3 km far clip; Balanced (runtime default) = 768, main-camera scene mask excluding water/UI/IgnoreRaycast, 3 km; High = 1536, same scene mask, 6 km. `TextureSize`/`-reflectionSize` override longest edge up to 2048. Aspect follows the main camera, so the portrait does not stretch reflections. At landscape 1.6 aspect, approximate RGBA32 + D16 target memory: 0.53 / 2.11 / 8.44 MiB (2048 override: 15 MiB); no MSAA, one reflected render per main render, no skipped-frame shimmer. Actual GPU time and driver allocations need iPad profiling; these are budgets, not measurements. Tier and scenery mask are exposed on `WaterReflection`; fixture flags are `-reflectionQuality`, `-reflectionSize`, `-reflectionScenery off`.
- **Reflection correctness:** assigns every procedural water surface to reserved Unity Water layer 4 and excludes it to avoid water sampling its own render target. Mirrors camera transform as well as view matrix for specular world position; preserves `GL.invertCulling` with try/finally. Tier-specific far clip also reaches the oblique projection. Texture resizes/reclaims resources safely in editor and player. Default-layer terrain and forest now participate on Balanced/High.
- **Water:** Schlick air/water Fresnel (F0=.0204), depth absorption from the camera depth texture, dusk body colors, warm sun glint and reduced screen distortion. Terrain/Lit provide built-in fallback ShadowCaster passes for camera depth (no pipeline package). Calm mode reduces only ambient ripple normal amplitude to .0007; physical wake slope is still applied. Water is visually opaque at the surface; depth color supplies its shallow/deep transition rather than fixed blue alpha blending. Other water planes retain sky fallback when the planar texture belongs to a different height.
- **Foam correction:** raised visible crest breaking thresholds, shortened cosmetic history e-fold from 12 s to 4 s and gated surface coverage by sparse world-space lace (max .65) instead of accumulating a solid white mask. Must judge the new PNGs before calling this tuned. Wave equation, forcing, decay and shore physics were not changed; bow/prop/step spray code is retained.
- **Fixture:** `-preset sunset -calm` uses a deterministic visual stage at the real harbour surface with a low mesh bank and 360 procedural conifers; real FloatHydro and WakeWaves still run. This is a composition/lighting fixture, not a new simulated lake or a terrain replacement. The ordinary day fixture still uses the actual world to verify terrain reflections. Calm defaults to zero speed/throttle and level pose; `-speed` and `-throttle` can override for drift/prop-wash studies. Existing moving fixture remains 18 m/s with its prescribed pose. No scene/config is saved.
- **Checks performed:** installed Unity Roslyn C#9 compiler against installed Unity reference assemblies/cached game assemblies passed for changed C# plus their source dependencies (response file `build/renders/compile-sunset.rsp`). Including current PlaygroundBuilder source was necessary because cached assemblies predate its internal city methods. This checks syntax/APIs only, not shader import or Unity player compilation. `git diff --check` passes; new script/shader/include all have GUID `.meta` files. Unity, VSTest, deploy and git commit were deliberately not retried, per Chief's session instructions. Chief must read the suite summary and `** BUILD SUCCEEDED **`, and check `build/ios.log` for `error CS`; this pass has no new results for either.
- **Scope/ownership:** no Core/Sim/config/test changes, GLB changes, prop hub edits, chart/map edits, migration, push, merge, device deploy or commit. The old dirty-worktree audit remains resolved by Chief's prior note recorded below. All continuation changes are left for the runner to commit.

### 2026-10-28 — Codex · lane/codex-visuals · water rendering WIP

- Started clean in the supplied separate worktree, already on `lane/codex-visuals` at `1b67dd5` (the supplied split-surfaces head). Chief's note superseded the dirty-worktree audit: J-3 was committed by Chief; the other two contain disposable logs/reference images. No other worktree was modified.
- `FloatSplash`: perforated procedural sheet fragments, varied particle sizes, velocity-stretched droplets and a separate low-density mist material. Bow spray now responds to wet bow draft and speed before planing; step/stern/trailing mist retain their existing planing gate. Corrected negative-component MinMaxCurve ordering. Particle systems clear on disable; owned particle objects/materials are destroyed with the effect.
- New prop spray reads the existing `PropDisc` mesh radius and world transform (including twin pushers), aircraft RPM, engine-stopped state and `WorldTerrain.WaterSurfaceAt`. Projected bottom-disc clearance gates entrainment; idle, dry ground and high clearance suppress it. The spray is cosmetic and applies no forces. No StyleFor dimensions, prop binding, models, configs, Core/Sim code or flight tests changed.
- `WakeFoam.shader` maintains a cosmetic foam history from positive wave-crest compression/slope with a 12-second e-fold decay. Two RHalf buffers and one extra blit per 20 Hz wake step; harbour allocation is about 1.13 MiB extra. Water shader uses world-space foam breakup, suppresses glint on foam and retains the original steep-crest fallback. WaveSim, its constants, forcing and shoreline behavior are unchanged. The new hidden shader is explicitly included by BuildScript to prevent player stripping. Built-in pipeline retained.
- Added `WaterRender` editor fixture and tracked metas for both new assets. A prescribed 18 m/s, 3-degree harbour tow samples **real FloatHydro** and steps the real wave solver at 20 Hz. This is an effect fixture, not a free-flight takeoff validation. It writes spray/wake/bow views and checks supported shaders/compiler messages. `-aircraft`, `-throttle` and `-lift` allow fleet/idle/airborne comparisons. No scene or config is saved.
- **Verification:** changed C# (`FloatSplash`, `WakeWaves`, `WaterRender`, `BuildScript`) compiles with Unity 6000.3.23f1's Roslyn compiler against installed Unity reference assemblies/cached game assemblies, C# 9. This is a limited API/syntax check, **not a Unity import, shader compile or player build**. `git diff --check` passes. Standard dotnet test command was attempted; a single-node/no-restore retry compiled the suite but VSTest aborted on forbidden local sockets, so there is no new suite result. The previously documented four failures remain unassessed here.
- Required build attempted **only** as `tools/deploy/deploy.sh 00000000-0000-0000-0000-000000000000`. It could not complete Unity startup/package-manager IPC; Xcode build and install were not reached. `build/ios.log` has no `error CS`, but does contain `listen EPERM`; absence of C# errors here is **not** build success. No real device was targeted. No push, merge, publishing or migration occurred.
- Attempted to stage and commit as `WIP: add water spray detail and persistent wake foam`; Git could not create this worktree's `index.lock` (`Operation not permitted`). No commit was created and no file was discarded. All source edits and both new tracked-intended `.meta` files remain in this worktree for review/commit.
- Local evidence: `build/renders/before.log`, `after.log`, `build-check.log`, `compile.log`, `compile.rsp`, `flight-tests*.log`; original water source snapshots in `build/renders/baseline-source/`. These are ignored build outputs. No before/after images could be generated; visual tuning and mobile GPU cost still need review.

Commands to resume from this worktree in an unrestricted local terminal (graphics required for the render):

```sh
/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "$PWD/unity" -executeMethod FlyingGame.EditorTools.WaterRender.RenderFromArgs -out "$PWD/build/renders/after" -logFile "$PWD/build/renders/after.log"
# Repeat with -aircraft pa18-floats-like / dhc2-beaver-floats-like / aircam-amphib-like;
# use -throttle 0 for idle and -lift 5 for airborne suppression.
~/.dotnet/dotnet test tools/FlightTests/FlightTests.csproj -p:UseSharedCompilation=false
tools/deploy/deploy.sh 00000000-0000-0000-0000-000000000000
```

For a matching before capture, use the parent `1b67dd5` with only the new `WaterRender.cs` and its meta copied into an isolated baseline checkout; this fixture supports both versions. Save output under this lane's `build/renders/before`. Do not reverse or overwrite the lane's changes to obtain the baseline. Review the planing and bow closeups, prop clearance on all four fleet types, dry/idle suppression, lingering foam after liftoff, and device GPU frame time before approval.

### 2026-10-28 — Claude · lane/claude-menu-aero · handover state for a ChatGPT/Codex visuals pass

Ty is handing the app to ChatGPT for a visuals upgrade. What the next agent is walking into, written down so it
does not have to be rediscovered:

**Branch.** `lane/claude-menu-aero` is **65 commits ahead of `origin/split-surfaces` and 0 behind**, so it
fast-forwards. `origin` carries only `main` and `split-surfaces` -- **no `lane/*` branch has ever been pushed**.
Until this lane lands on `split-surfaces`, anything branched from it is building on work that is not yet on the
shared branch. That merge is Ty's.

**Builds and tests at handover.** Suite: 4 failures, all pre-existing and all written up above (one flat-spin
smoke check, three Pitts ground-handling). iOS player: `** BUILD SUCCEEDED **` at 16:22, and the resulting build
`2026.1028.1623` is installed on the iPhone 17 Pro and the iPad mini and published at
`https://mac-studio.tail9aad2a.ts.net/ota`.

**`deploy.sh` printed a guess as a diagnosis (fixed here).** Its install step reported `installed on 0 of 3
device(s)` at 16:22 and explained each one as "(asleep, or off the network)". That reason was invented: the real
message from `devicectl` went into a `grep -q` and was discarded. I read the guess as the diagnosis and sent Ty
to the OTA route; he said the devices were awake, and `devicectl device install app` then put the build on both
by hand. **The 16:22 failure itself was probably genuine** -- the devices were woken after that run -- so the
defect is the reporting, not the discovery: `devicectl list devices` calls all three `available (paired)` even
while `device info details` shows the phone `connected` with a live tunnel, so there is nothing to sort by and
the install must be its own test. Now the failure branch prints devicectl's own error and names the device
(verified on both branches: a real install, and a bogus UDID giving
`ERROR: The specified device was not found ... error 1000`).

**Uncommitted work nobody should touch.** Four worktrees are dirty and none of it is from this lane:
`flying-game` (main, `split-surfaces`) 2 files, `codex-own-j3-cub` 6, `codex-talkflying-livery-v2` 2,
`codex-beaver-edo-floats` 1. The main checkout's `OptionsPanel.cs` change is an **earlier fix for the same
unclosable-menu bug** this lane fixed by rewriting the panel into chapters (scroll view, Close outside the scroll
view, panel clamped to the screen -- the same three rules). It is superseded, and it will conflict with this lane.
**Ty: it is yours to keep or drop; I have not touched it.**

**The boundary for a visuals agent.** `src/FlyingGame.Core`, `src/FlyingGame.Sim`, `configs/aircraft/*.json` and
`tools/FlightTests` are physics and stay put -- every config number is measured off a model or read off a type
certificate. Two things in the visual half are load-bearing for physics and are the easy ways to break it
silently: `AirframeVisual.StyleFor` is where the **propeller hub** comes from (`Prop(Body[0].x + 0.12f, 0f,
BodyAxisZ, PropRadius, ...)`, blades bind only within `0.5 x hub.Radius`, and a miss freezes the prop with no
error), and the **GLB axis convention** (nose -x, up +y, span z -> `config_x = -glb_x`, `config_y = +glb_z`,
`config_z = -glb_y`) is what the aero measuring scripts read, so a re-export at a different orientation or with a
scale on a node makes that aeroplane's physics wrong without a single test failing.

**Do not install URP or HDRP.** Built-in pipeline is deliberate: 18 hand-written shaders and every procedural
material are written for it. `FirePotLights.cs` has two `Shader.Find("Universal Render Pipeline/...")` calls with
fallbacks; they are vestigial and do not mean URP is in use. A pipeline migration is Ty's decision, not a step
inside a visuals pass.

**Every asset wants a tracked `.meta`.** All 117 `.cs` files under `src/` have one; `BiplaneCellule` was the only
one without, because it was written after the last Unity import (`4c2b94e`). An untracked `.meta` means Unity
invents a fresh GUID on the next machine and the same file arrives as a different asset. This applies to any new
GLB, shader or texture.


### 2026-10-28 — Claude · lane/claude-menu-aero · biplane cellules measured for the whole fleet, and the stall estimator stops truncating

Owner: *"account for cellule clmax of a biplane configuration and don't just do it as a one off for this airplane
build it as a tool/table that applies to all biplanes current and future."* Table, numbers and the reasoning are in
`docs/CONTROL-SURFACES.md` ("Biplane cellules, and what a stall speed is actually measuring").

- **`FlyingGame.Core.Aero.BiplaneCellule`** measures a cellule off the strips alone — gap, stagger, decalage, span,
  area ratio, Prandtl's σ and the CLmax factor. Nothing hand-entered per type, so a biplane added tomorrow is
  covered. The interference curve is a table (`ClMaxByGapOverChord`) left in the open and labelled as a
  standard-form fit rather than a transcribed source, so it can be replaced from Diehl or NACA TR 256/269 without
  touching anything else. `BiplaneCelluleTests` prints the fleet table and guards detection, the table's shape, and
  that the AeroModel wiring is actually reachable.
- **It is wired but OFF** (`cellule.liftInterference`, per type). The Stearman has the tightest cellule we own and
  already matches its book stall to +1.3 % with no interference term; the Pitts needs 44 % MORE lift than it makes.
  The fleet's section tables are evidently carrying this effect already, so a type may only switch it on once its
  tables are a true 2-D section. On the Great Lakes it would move Vso 51.2 → 53.5 mph (−10.2 % → −6.1 % vs book).
- **`EstimateVso`'s ceiling was a flat 14° of alpha** and truncated any type whose wing stalls above it — the scan
  stopped before the aeroplane had stalled and reported a speed faster than the stall. It is now the type's own wing
  stall in body axes plus 3°. The 172 went from +13.4 % to +9.3 % against book. It did NOT explain the Extra or the
  Decathlon, which sit exactly at 14° and did not move; their error and the Pitts' +20 % have other causes.
- 776 of 783 pass. The four failures are the long-standing Pitts ground-handling three and the flat-spin smoke
  check, all under NEEDS YOU.

### 2026-10-28 — Claude · lane/claude-menu-aero · the Great Lakes 2T-1A-2 is in the game, measured off its model and its type certificate

Owner: *"The red Great Lakes is in ~/Desktop/GreatLakes-red-handoff/, import it into aero and work the
aerodynamics and physics off of the model by measuring the model just like you did with the pitts and extra.
reference the tcds for the cg location."*

A new type, not a correction to an old one — the first whose flight model was built from the mesh and the
certificate from the start. Full write-up in `docs/CONTROL-SURFACES.md` ("Great Lakes 2T-1A-2: a new type
measured off its model").

**Installed.** `great-lakes-like-own.glb` (the red-sunburst livery, installed as the default own-model file so
`AirframeModels.HasOwn` finds it), `tools/own_models/build_great_lakes.py`,
`docs/models/great-lakes-own-PROVENANCE.md`, a `LICENSE.txt` beside the model and a `docs/CREDITS.md` line
(both of which the suite requires). Wired into `SessionSettings.Fleet`, into `AirframeModels.OwnSpecs` with
`namedSurfaces` — its ailerons, elevators and rudder are meshes of their own — and into `PickOwn`, because
there is no downloaded Great Lakes and no procedural style for the type, so this GLB IS the aeroplane. No
`Livery` entry: the paint is baked into the model's textures.

**One gotcha worth carrying forward.** A type that is always drawn from its own GLB still needs a
`AirframeVisual.StyleFor` entry, because that is where the PROPELLER HUB comes from — not just the procedural
airframe, which for such a type is never rendered. Without one the Great Lakes fell through to the default arm
(a 2-33 trainer, `PropRadius` 0), no hub was created, and its propeller would have been frozen. `AircraftRender`
reports it: `0/0 propellers bound` before the entry, `1/1` after. Control surfaces bind either way, since those
come from the config's control strips, so nothing in the test suite sees this.

**The model measures true.** Wing area 17.460 m² against a published 187.6 sq ft = 17.429 (+0.2 %), span
8.126 m against 26 ft 8 in (−0.02 %), length 6.214 m against 20 ft 4 in, main tyre radius 0.222 m exactly the
600×6's 17.5 in. Areas scaled ×0.9982 onto published; everything else is the measurement.

**Locating the datum was the interesting part.** A18EA gives it as "centre of most forward lateral fuselage
tube", which is not a feature you can point at in a mesh. But the same sheet gives M.A.C. 46.0 in with its LE
at Sta. 9.0, so the MAC quarter-chord is 0.521 m aft of datum — and the cellule's area-weighted quarter-chord
measures at config x −0.720, putting the datum at −0.200. The model's own firewall station puts it at −0.175.
**Two independent routes, 25 mm apart.** The TCDS CG at 1800 lb (+20.0 to +23.8 in aft of datum) is then
config x −0.708 to −0.804, which is 23.9 % to 32.2 % MAC — a textbook light-aircraft envelope. `mass.cg` is
the mid-range, −0.756.

**Static margin 14.6 % MAC on the first run**, with no tuning at all. It passed pitch stability, stall
authority, the new-airplane protocol, spawn trim, takeoff and ground loop first time; the only two things it
broke were bookkeeping (the glide table needed regenerating and the credits needed mirroring to
StreamingAssets).

**`cd0Area` is calibrated, not guessed.** It is the one number that is neither geometry nor certificate — the
parasite area of fuselage, gear, struts and wires. `BOOK_SPEEDS=1` now sweeps it for this type too: at **0.59**
it cruises at **120 mph on 75 % power**, exactly the Waco/Great Lakes spec sheet's max cruise, and runs to
133 mph at full power. (The sheet's "maximum speed 153 mph" is Vne repeated, not a level speed; 133 mph for
180 hp on a wire-braced biplane is the sensible reading.)

Also from A18EA rev 11: 1800 lb max weight, Vne 153 mph, load factors **+5.40 / −4.00** (NOTE 6, specific to
the 2T-1A-2 and confirmed independently by the manufacturer's sheet), IO-360 at 180 hp / 2700 rpm, the 74-inch
Hartzell HC-C2YK-4F, and control travel — aileron 24 up / 22 down, elevator 21 up / 29 down, rudder ±35°.
Derived gains from the measured chord ratios: aileron 0.348, elevator 0.395, rudder 0.395. Rigging from the
maintenance manual via the provenance: 3° incidence both wings, 2° dihedral on the lower wing only.

### 2026-10-28 — Claude · lane/claude-menu-aero · menu overhaul, remembered settings, and the Pitts/Extra surfaces rebuilt from their models

Owner, in one go: *"overhaul the landing page and the menue so it takes up less space put more things into pull downs
or buttons that open pages maybe use a chapter/page setup. in normal (not multiplayer) the game should pause while you
are changing settings … i keep crashing because i'm turning off bubbles etc. it should also remember how you last set
it up instead of going back to defaults every time. default everything off (bubbles flightpath vector force vector
etc) and hud as the instrument display. with the new extra and pitts models i want you to recheck all of the
dimensions and sizes for lifting surfaces … adjust the formulas for the control powers as needed … check the fuselage
aerodynamic contribution as well."*

**The menu**
- OPTIONS is now a header that never scrolls plus eight chapter tabs — Display · Aids · Controls · Audio · Clips ·
  Badges · Game · Help — one page at a time, two rows of four on a phone in portrait and one row of eight in
  landscape. Every control the old one-long-list panel had is still there, on a page.
- The unclosable menu cannot come back. OPTIONS and Close are drawn BEFORE the scroll view, the panel is clamped to
  the screen, and a page's height is MEASURED from where its last row actually ended on the previous pass rather than
  counted by hand. Adding a row to a page can no longer make the panel the wrong size — the worst it can now do is
  show a scrollbar.
- The landing page is five chapters — Aircraft · Start · Lessons · Weather · Players — one column, the same shape in
  portrait and landscape so it does not move under your thumb when the iPad turns. FLY, HELP, CHART and CREDITS stay
  where they were, and a one-line summary beside the title says what FLY will do, so a setting on another chapter is
  never a surprise.
- The world stops while OPTIONS is open: `SessionSettings.PauseWhenMenuOpen`, on by default, switchable on the Game
  page. Multiplayer is the exception — `MultiplayerLive` keeps time running when other real pilots are up, and the
  header says which it is doing.
- Everything is remembered. `SessionSettings.LoadPrefs`/`SavePrefs` persist the display settings, the aids, the badge
  switches, the pause choice, the nav radios and the landing page's own choices; the panels write on every change.
  The settings that already kept themselves (clips, voice, volume, livery, badge publishing, control feel) are NOT on
  that list and must not be added to it.
- New defaults, per the owner: bubbles OFF, flight path vector OFF, force vectors OFF, flight test data OFF, lift
  markers OFF (they used to come on for every glider), instruments = HUD. These are only ever seen on a fresh install.
  Nav radios are left ON — the chart shows no position, so they are the only way to know where you are, and they were
  not on the owner's list.
- `AERO_SELFTEST=menu` now shoots every chapter of both panels at the four screen shapes instead of the top and bottom
  of one long page. It ran clean through all four shapes and all thirteen chapters.

**The Pitts and the Extra: lifting surfaces rebuilt from the models**

Full write-up, method and before/after tables in `docs/CONTROL-SURFACES.md` ("Pitts and Extra: the surfaces rebuilt
from the models"). The short version:

| | before | after |
|---|---|---|
| Pitts static margin | 44 % MAC | **12.9 %** |
| Pitts elevator saturation | 120° (non-physical) | **19.4°**, full aft balances on its 18° target |
| Extra static margin | 9 % MAC | **17.1 %** |
| Extra elevator saturation | 13.5°, 4.8° short of target | **30.8°**, on target |
| Extra wing span | 7.43 m (model shrunk to fit) | **7.994 m** (published 8.00) |

- The Extra's tail arm was **1.10 m short** — a quarter of the real arm — and its fin was twice the drawn size: two
  errors cancelling to a plausible-looking 9 % MAC.
- The Pitts' stabiliser was 29 % larger than the drawn one and its fin 81 % larger. It was the 44 % MAC type the
  control-surfaces doc had been carrying as open work since 2026-10-06, and the reason the stall calibration wanted
  its elevator chord ×3.6. It now calibrates like any other type, so it came off `StallAuthorityTests.ManualElevator`.
- Control gains are derived from the chord ratio the model has, not hand-set: g = (0.85·τ(E) − E)/(1 − E). The 0.45
  that was on every aileron and rudder becomes 0.35–0.40.
- Areas are scaled to the published total (Pitts ×1.023 to 11.6 m², Extra ×1.082 to 10.84 m²) because these meshes
  were only ever checked on length/span and height/span, never on area. The spanwise distribution, arms, chords,
  heights and stations are the measurements.
- The fuselage body terms were re-measured as rasterised union silhouettes. The Extra's side area was 2.4× low and its
  centroid 0.88 m forward of where the body is. `cd0Area` and the crossflow `cd` are untouched on purpose.
- **The Pitts' CG, fact-checked against FAA TCDS A8SO** (Ty asked). The config datum is the lower wing leading edge
  (the model's origin is exactly there). The S-2B sheet gives the datum as 91.81" forward of that edge and the
  **Acrobatic** C.G. range as (+89.58 to +90.50) at **1625 lb** — and `massKg` 737 is exactly 1625 lb, so that is the
  range that applies. It works out to **0.033–0.057 m FORWARD of the lower wing LE**. The old config had the CG
  0.030 m **AFT** of it: the wrong side of the datum feature, about 3 in outside the envelope. `mass.cg` is now the
  acrobatic mid-range, **0.045 m**, which gives 12.9 % MAC and an elevator that calibrates to 19.4°. The TCDS also
  notes the S-2B has "wings and landing gear moved forward 6 inches" against the S-2A, which is why its mains sit so
  far ahead of the CG — the aeroplane really is short-coupled on the ground.
  The CG **height** (0.20 m below the datum) is still derived, not certified: a TCDS gives no vertical CG.
- **Control-surface travel is now the certificates' (Ty asked for the CG fact-check; this fell out of it).** Both
  configs carried `maxDeflRad` 0.52 rad = 29.8° on all three axes — an unsourced deflection limit, and therefore
  unsourced control power. A8SO gives the S-2B aileron 25°, elevator 27°, rudder 30°; EASA.A.362 gives the EA 300
  aileron 30°, elevator 25°, rudder 30°. So the Pitts loses 19 % of its aileron travel and 10 % of its elevator, and
  the Extra 19 % of its elevator. Both elevators re-calibrated afterwards and land on their targets (Pitts saturation
  20.3° for an 18.0° balance, Extra 48.7° for 20.0°).
- **The Extra's CG needed no change.** EASA.A.362 Issue 09: datum is the plane of the firewall, C.G. 75.0–89.8 cm aft
  of it at 820 kg or below, and `massKg` is 820. With the firewall at the aft face of the engine (config x +0.79,
  ±0.05 — the model has no firewall node) that is config x +0.04 to −0.11 against `mass.cg` −0.02: inside, slightly
  forward of mid-range, and its 17.1 % MAC agrees.
- The gear moved with it: the model is lined up on the wing chord line, and the wing's quarter-chords were 0.26 m out,
  so the drawn wheels sit 0.26 m forward of where the config had them (Pitts) and the Extra's grew 6.6 % with its
  span. Taken from `GearMatchesTheModels`' own measurement of the drawn contacts.

**Pitts v13 (owner, 2026-10-28, ~/Desktop/Pitts-v13-handoff)** — installed over the v12 GLBs in place, keeping the
.meta files and GUIDs. Checked against v12 before installing: every lifting surface is identical to 4 decimal places
in area and extent, the node set is the same, and **the slave struts did not move** (same frontal area, same spanwise
station 1.918–2.035 m, same chord position) — their 10-inch inboard move was v11→v12 and is already in the
measurements above. The only change the airstream sees is the cabane feet moving inboard: +0.0014 m² of frontal area
on an aeroplane presenting 2.83 m², which is 0.05 % and lives inside the lumped `cd0Area`. No aerodynamic rework.
`checker-unmarked` (no tail number) was already the default and stays it.

### 2026-10-28 — Claude · lane/claude-livery-picker · unmarked liveries are the defaults (Pitts Checkered, Extra Unmarked)
**What the GLBs really carry**

I checked each GLB directly: blade runs counted round the spinner axis, plus prop materials, node names and per-node vertex data.

| livery id (picker name) | GLB | prop | same mesh as |
|---|---|---|---|
| checker-unmarked (Checkered) **Pitts default** | pitts-s2b-like-own-checker-unmarked | **Hartzell 2-blade**, grey with white tips, r 1.016 m | -own (N31DG), node for node |
| factory-unmarked (Factory) | pitts-s2b-like-own-factory-unmarked | **MT 3-blade**, white with red tips, r 0.950 m | -own-n1191, node for node |
| n31dg (N31DG) | pitts-s2b-like-own | Hartzell 2-blade | |
| n1191 (N1191) | pitts-s2b-like-own-n1191 | MT 3-blade | |
| unmarked (Unmarked) **Extra default** | extra-300-like-own-unmarked | 3-blade, r 1.000 m | -own-n300uy, node for node |
| talk-flying (Talk Flying) | extra-300-like-own | 3-blade | |
| n300uy (N300UY) | extra-300-like-own-n300uy | 3-blade | |

- The HANDOFF's prop text was reversed. The GLBs agree with `build_pitts_s2b.py` (CHECKER-UNMARKED is the N31DG scheme with the Hartzell; FACTORY-UNMARKED is the N1191 scheme with the MT), so nothing is wrong in the art.
- The tests encode the counts from the files.

**Changes**
- Core `LiveryChoice`:
  - Options, default first: Pitts checker-unmarked / factory-unmarked / n31dg / n1191; Extra unmarked / talk-flying / n300uy.
  - `Resolve`: with no override and no valid saved id, it now returns the type's first option (`Default`), not "type". Saved ids from before (n31dg, n1191, talk-flying, n300uy) still resolve to themselves. Unknown ids fall back to the unmarked default.
  - `OwnSuffix` is now a switch. Anything not explicitly marked, including "type", goes to the unmarked GLB. Pitts "talk-flying" stays N31DG and Extra "sunrise" stays Talk Flying, as before.
- `Livery.cs`: schemes `CheckerPitts` "Checkered", `FactoryPitts` "Factory" and `UnmarkedExtra` "Unmarked", so the picker shows those names.
- `AircraftRender` accepts the new ids.
- .meta files for the three new GLBs: the siblings' glTFast importer settings, each with its own GUID (Unity accepted them without rewriting them).
- Picker: the LIVERY row is now always a drop-down, for both types.
  - With three or four segment buttons in a column, each button gets about 90 pt on an iPhone in portrait, so "Talk Flying" would be shrunk below the page's type size.
  - A drop-down keeps the row the same shape when the player switches between Pitts and Extra, and matches every other field on the page.
- Credits (docs and StreamingAssets), the Pitts LICENSE.txt and both PROVENANCE docs list the unmarked GLBs. They are the same clean-room / EA300 builds, so there is nothing new to credit. The EA300 GPL question in NEEDS YOU covers the unmarked Extra too.

**Verified**
- Unity 6000.3.23f1 batchmode compile is clean.
- `LiverySwapCheck` PASS: every pick loads its own GLB and paint with no leak on repeats. Pitts binds 6/6 control surfaces and 1/1 propellers in all four liveries; Extra binds 4/4 and 1/1 in all three.
- `GroundStanceRender`: all three unmarked GLBs SIT ON THEIR WHEELS.
  - Pitts: three-point 10.10°, mains −2.8 cm, tailwheel −2.5 cm.
  - Extra: 9.25°, −2.5 cm and −2.3 cm.
  - These match the marked siblings, so the ground-handling retune applies unchanged. `GearMatchesTheModels` now also runs on the unmarked GLBs.
- Renders of all 7 liveries (AircraftRender) are in /tmp/unmarked-liveries/<type>-<livery>/, and ground-stance shots are in /tmp/unmarked-liveries/stance/.
  - The unmarked Pittses carry no registration.
  - The unmarked Extra has no N-number, no Talk Flying and no Sunrise Aviation; the fin stars and stripes and the red stripes are kept.
- FlightTests (full suite): **769 passed, 0 failed, 3 skipped** (772 total).
### 2026-10-28 — Grok Bot executor · codex/unmarked-liveries · unmarked liveries (Ty): Pitts checker-unmarked + factory-unmarked (no registration), Extra unmarked (no N-number, no Talk Flying / Sunrise Aviation); built from 8fea251 with livery switches only; handed to Claude as the new defaults (see HANDOFF). Local commit only.
### 2026-10-28 — Claude · lane/claude-livery-picker · ground handling: Pitts S-2B and Extra 300 gear matched to the models, retuned
**Gear geometry** (config frame: x forward, z down from the config origin; measured from the GLBs placed as `AirframeModels.Place` places them, lined up on the wings)

| | mains x / ±y / z | tailwheel x / z | three-point |
|---|---|---|---|
| Pitts, before | 0.123 / 0.80 / 1.000 | −3.500 / 0.361 | 10.0° |
| Pitts, after (= model) | 0.202 / 0.81 / 1.194 | −3.864 / 0.470 | 10.1° (model 10.1°) |
| Extra, before | 0.100 / 1.00 / 0.950 | −3.200 / 0.368 | 10.0° |
| Extra, after (= model) | 0.250 / 0.827 / 1.386 | −4.796 / 0.564 | 9.25° (model 9.25°) |

- Pitts: mains were 0.19 m short and 8 cm aft; the tailwheel was 0.36 m forward. Both liveries share the airframe and measure identically.
- Extra: mains were 0.44 m short and 15 cm aft; the tailwheel was **1.6 m forward**. With the old gear, the 2.0 m prop cleared the ground by −5 cm in a level attitude, and the drawn wing sat 0.44 m above the physics wing.
- Tyre radii now come from the models: Pitts 0.17 / 0.075 m, Extra 0.145 / 0.067 m.
- The tailwheel's share of the weight is now Pitts 10 % (was 8 %) and Extra 9 % (was 8 %).
- Visual pivot: the Pitts' sim CG is now 0.135 m below the drawn thrust line (was 0.33 m). The Extra's is 0.07 m above it (was 0.37 m below).

**Retune** (`tailwheel steerSpringNmPerRad`; nothing else in the physics changed)
- Pitts 6 → 15. At 6 the castor ran free, so the tailwheel did no steering. A full-rudder taxi turn wound up into a loop, and no pilot could hold a rollout below about 20 m/s.
- Extra 75 → 60. The longer, correct tail arm made it too stable at 75: ×0.28, below the owner's 0.3–1.6 band.

**Ground numbers, after (before on the old configs)**, with the scripted pilot in the new `AerobatGroundHandlingTests`:
- Taxi:
  - 90° turns take about 7 s.
  - A full-rudder turn at 5 kt is steady: Pitts 11.9 °/s, Extra 14.2 °/s.
  - The steering never unlocks and the tail never lifts.
  - Half brakes from 8 kt stop in 3.2 m with the tail down. Before, the Pitts **flipped** and the Extra **struck its prop**.
- Takeoff:
  - Pitts: tail up at 13.6 m/s; lift-off 34.7 m/s (78 mph) after **135 m** (published S-2B ground roll 170 m). It needs +19 % right rudder, and swings 95° left in 5 s hands-off.
  - Extra: lift-off 38.0 m/s (74 kt) after **163 m** (published 300L 115–200 m). It needs +10 % right rudder and swings 51° left hands-off.
- Landing (quick feet, no brakes / half brakes below 20 m/s):

  | | three-point | wheel landing |
  |---|---|---|
  | Pitts | 595 / **246 m** (published 229 m) | 803 / 485 m |
  | Extra | 712 / 326 m | 896 / 525 m |

  - All of them stay within 0.5 m of the centreline and 2.3° of heading, and none strikes the prop.
  - The wheel-landing rolls are longer because the tail is held up, with no braking, until it comes down by itself.
- Ground loop:
  - Hands-off yaw-rate growth at 8 m/s: Pitts ×6.6 (was ×5.2), Extra ×0.46 (was ×0.68).
  - Relaxed feet loop the Pitts on a three-point rollout (180°) but keep the Extra straight (1.7°).
  - An 8° swerve at 12 or 18 m/s is caught by rudder and differential brake: worst excursion Pitts 22°, Extra 14°.

**Tests**
- New `tools/FlightTests/AerobatGroundHandlingTests.cs`, 13 tests plus an opt-in probe (`GROUND_PROBE=1`, `GROUND_TRACE=1`, `GROUND_CFG_DIR=` for before/after runs):
  - `GearMatchesTheModels`: all four GLBs, gear within 2.5 cm, three-point within 0.3°;
  - taxi;
  - takeoff (bands from the published rolls);
  - three-point and wheel landings;
  - the quick-feet/relaxed-feet ground-loop split.
- `GroundLoopTests.SteeringUnlocksInAGroundLoop…` is re-seeded from 20 to 40 °/s at 12 m/s. With the tailwheel where the model has it, the 20 °/s loop bleeds its speed off with the castor peaking at 28–33°, even at the old spring. A 40 °/s loop still unlocks it (49°). The assertion is unchanged.
- `OwnPittsModelTests`: GLB readers made `internal` for reuse. No other test changed.
- `~/.dotnet/dotnet test tools/FlightTests`: **756 passed, 3 skipped, 0 failed** (11 m 19 s; was 742 + 14 new cases, the 3 skips are the same pre-existing ones).

**Unity**
- Unity 6000.3.23f1 batchmode compile: clean.
- New editor `GroundStanceRender` parks the airframe the way `LandingGear.RestingState` does, over a runway strip, and logs the drawn tyre heights. Renders are in `/tmp/ground-retune/*-ground-{side,3q}.png`.
  - N31DG and N1191: three-point 10.10°; mains −2.8 cm, tailwheel −2.5 cm (the struts' intended 2 cm settle); the lowest other part is 29 cm up.
  - Talk Flying and N300UY: 9.25°; mains −2.5 cm, tailwheel −2.3 cm; the belly is 45 cm up.
  - All four log **SITS ON ITS WHEELS**.
- `AircraftRender.Capture` and its size constants are now `internal` so the new tool can reuse them.
- Local commits only. No push, merge, deploy or device build. No GLB or art touched.

### 2026-10-28 — Claude · lane/claude-livery-picker · in-game LIVERY picker (Pitts N31DG/N1191, Extra Talk Flying/N300UY), per aircraft, saved
**In the UI**
- Landing page (FLIGHT SETUP), first column: a **LIVERY** row appears right under the AIRCRAFT drop-down, but only when the chosen type has more than one scheme (`Liveries.Available(id).Count > 1`). Today that is the Pitts S-2 (**N31DG** | **N1191**) and the Extra 300 (**Talk Flying** | **N300UY**). Every other type shows no row.
- Up to three schemes are side-by-side buttons, the same size and style as the glider's TOW PLANE buttons; the chosen one is green. Four or more would become a drop-down like the others. The row takes the place the TOW PLANE row takes for a glider, so the page's height is unchanged from the glider case the menu self-test already covers (iPhone and iPad, portrait and landscape).
- One tap chooses and saves. The aeroplane repaints at once: behind the page if it is the type you are flying (MENU mid-flight → tap a livery → FLY), otherwise when you FLY.
- The choice is **per aircraft**: N1191 on the Pitts leaves the Extra alone, and the reverse. It **persists** across launches in PlayerPrefs (`livery.<aircraftId>`).
- If `AERO_LIVERY` is set (captures, dev builds) it still wins for every type, and the header reads "LIVERY (AERO_LIVERY set)".

**How**
- New `src/FlyingGame.Core/LiveryChoice.cs` (pure, so FlightTests holds it): per-type options with the default first, `Resolve(id, env, saved)` = env, else the saved id if it is one of the type's options, else `"type"`; `Selected`, `PrefKey`, and the livery → GLB `OwnSuffix` mapping (moved here from AirframeModels, unchanged).
- `"type"` remains the default, which is exactly what everything wore before: N31DG for the Pitts, the Talk Flying GLB for the Extra, type colours for everyone else.
- `SessionSettings.LiveryId` (one fleet-wide id) is replaced by `LiveryOverride` (AERO_LIVERY, or a render's `-livery`) plus `LiveryFor(aircraftId)` and `SetLivery(aircraftId, id)` (PlayerPrefs, cached per type).
- `AirframeModels.PickOwn`/`OwnSuffix`, `AirframeBuilder.Build` (`[Livery]` log line) and the editor renders all go through `LiveryFor`/`LiveryOverride`. `Liveries.Available` is built from `LiveryChoice`; types with no choice now get just their type colours, so ">1" is the picker's gate.
- `AirframeVisual` remembers the livery it built and rebuilds through its existing path when `LiveryFor(type)` changes, the same way the Options model toggles do. It skips that while wings or components are broken off; the next flight's rebuild picks the new livery up.
- `OptionsPanel.cs` is **not touched**.

**Verification**
- Unity 6000.3.23f1 batchmode compile: clean, 0 errors, no new warnings. Unity modified no tracked files.
- `AircraftRender` into `/tmp/livery-picker/`:
  - `pitts-s2b-like-n31dg`: checkered rudder, 2-blade grey prop. **6/6, 1/1** bound.
  - `pitts-s2b-like-n1191`: rudder diamonds, 3-blade white prop with red tips. **6/6, 1/1** bound.
  - `extra-300-like-talk-flying`: TALKFLYING.COM, TF fin badge. **4/4, 1/1** bound.
  - `extra-300-like-n300uy`: N300UY, no badge. **4/4, 1/1** bound.
  - All exited 0. The "stray child 'Model' cleared" line is in every earlier render log too (render teardown).
- New editor `LiverySwapCheck.Run` (log `/tmp/livery-picker/swap.log`): **PASS**.
  - For each type with a choice it picks A, B, A, B through `SetLivery` (no override) and rebuilds the same root, as the game does.
  - Each pick resolves to itself and loads its own GLB and paint. A repeat leaves an identical hierarchy (Extra 116 objects, Pitts 123), and the scene root count stays 1, so nothing leaked. Every build binds 4/4+1/1 (Extra) and 6/6+1/1 (Pitts).
  - The player's saved prefs are restored afterwards.
- New `tools/FlightTests/LiveryPickerTests.cs` (6 tests): options and defaults per type, env > saved > default, invalid or foreign saved ids ignored, other types unchanged, per-aircraft isolation, OwnSuffix for every choice with its GLB on disk, and the Unity wiring (source). `OwnPittsModelTests` now reads the mapping from LiveryChoice.
- `~/.dotnet/dotnet test tools/FlightTests`: **742 passed, 3 skipped, 0 failed** (14 m 20 s; was 736 + the 6 new).
- HANDOFF fix: N31DG = Hartzell 2-blade (grey, white tips), N1191 = MT 3-blade (white, red tips). Those two NEEDS YOU items are gone.
- Local commits only. No push, merge, deploy or device build.

### 2026-10-27 — Claude · lane/claude-pitts-own · own Pitts S-2B (v10.1) is now the in-game Pitts, N31DG by default and N1191 as livery `n1191`
**Wiring** (follows the EA300 swap)
- `AirframeModels`:
  - New `OwnSpecs["pitts-s2b-like"]`: nose +x and Y-up, like the EA300. It has a scale trim of 6.094/5.996, so the model comes out at its true 6.10 m span and 5.72 m length. Span-fitting alone would size it to the config's outermost strip span.
  - `PickOwn` always takes our model. The procedural Pitts style remains only as the fallback when the GLB is missing.
  - `OwnSuffix`: livery `n1191` loads `-own-n1191`; every other livery loads `-own` (N31DG). New overload `OwnSuffix(model, livery)`.
- `Livery.cs`:
  - Added `N31DGPitts` (`n31dg`) and `N1191Pitts` (`n1191`), plus a per-type talk-flying entry with no tints and no marks. Log shows `default=True marks=0` for every id.
  - `Available(pitts)` returns N31DG and N1191. There was no picker UI then (one was added 2026-10-28, see that entry).
- `AircraftRender`: accepts `-livery n31dg|n1191`. New optional `-deflect <deg>`.
- Model folder: added a `.meta` for the N1191 GLB (new GUID, same glTFast settings), plus `LICENSE.txt` and its `.meta`.
- Credits: added to `docs/CREDITS.md` and the shipped `StreamingAssets/credits.md`. The model is clean-room and ours; the font is Liberation Sans Bold, SIL OFL 1.1.

**Rig**
- New `Spec.NamedSurfaces`, set for the Pitts only.
  - The model's `Aileron_UL/UR/LL/LR`, `Elevator_L/R` and `Rudder` meshes each move whole, hinged on the mesh's own leading edge and its own hinge axis.
  - The measured axes are: upper ailerons 5.4° (wing sweep), lower ailerons ±3° (dihedral), and the rudder vertical, 11.8° from the config's tilted axis.
- Why: the generic carve hinged at the config's chord fraction (~32%) along the config's unswept axes. It cut 5–7 cm into the wings, stab and fin, and smeared the paint when deflected (seen in a `-deflect 20` render).
- The Extra and the 2-33 are untouched. The Extra re-render still binds 4/4 surfaces and 1/1 prop.
- Binding:
  - **N31DG: 6/6 control surfaces (4 ailerons, elevator pair, rudder), 1/1 propellers** (Hartzell 2-blade).
  - **N1191: 6/6, 1/1** (MT 3-blade).
- Prop rotation: clockwise from the cockpit. Every hub turns −θ about Unity +z (nose), and the GLB blades are pitched for US rotation.

**Verification**
- Unity 6000.3.23f1 batchmode compile: clean, no errors. The renders below ran on the final code.
- Renders under `/tmp/pitts-own-unity/`: `n31dg`, `n1191`, `type`, `talk-flying`, `n31dg-deflect20`, `n1191-deflect20` and `extra-regression`. All exited 0.
  - Nose forward and upright.
  - N31DG has the checkered rudder and 2 blades. N1191 has the rudder diamonds and 3 blades.
  - `type` and `talk-flying` both show N31DG.
- On the ground: placement puts the main wheels on the contact plane. The model's three-point attitude is 10.1° against the config gear's 10.0°.
- New `tools/FlightTests/OwnPittsModelTests.cs` (7 tests):
  - GLBs and metas present (unique GUIDs, same importer settings as the EA300);
  - span 6.07–6.12 m and length 5.69–5.75 m;
  - nose at −x and up at +y in the file;
  - rig mesh names present;
  - 2 blades on N31DG and 3 on N1191;
  - the livery-id → GLB mapping in `AirframeModels.cs` and `Livery.cs`;
  - the CREDITS entry.
- `~/.dotnet/dotnet test tools/FlightTests`: **736 passed, 3 skipped, 0 failed** (13 m 53 s).
- No physics changed. `configs/aircraft/pitts-s2b-like.json` and its StreamingAssets copy are untouched; the gear mismatch is in NEEDS YOU.
- Aside: `glider-2-33-like-own-tf.glb`, which `OwnSuffix` maps the 2-33's `talk-flying` livery to, is not on this branch. `PickOwn` falls back, so it is not broken, but it may be worth bringing over.
- Local commits only. No push, merge, deploy or device build.

### 2026-10-27 — Grok Bot executor · codex/own-pitts-s2b · v10.1: fin "Pitts"/"S-2B" script removed from both liveries (Ty); GLBs rebuilt; renders /workspace/pitts-own-v10-1 (box). Local commit only.
### 2026-10-27 — Grok Bot executor · codex/own-pitts-s2b · Ty approved Pitts S-2B v10 (c8b90bc) in N31DG + N1191; handed to Claude (see HANDOFF). Local commit only.

### 2026-10-27 — Claude · lane/claude-ea300-swap · own EA300 swapped in as the Extra 300, plus a GPL review
**Swap**
- `AirframeModels.PickOwn`: `extra-300-like` now always loads our model, whatever the livery or `UseOwnModels` setting.
- `OwnSuffix`: livery `n300uy` loads `extra-300-like-own-n300uy.glb`. Every other livery (`type`, `talk-flying`, `sunrise` and anything unknown) loads the Talk Flying `extra-300-like-own.glb`.
- `Livery.cs` changes:
  - Added `Liveries.N300UYExtra` (`"n300uy"`, "N300UY").
  - Added a per-type `extra-300-like` Talk Flying entry.
  - `SunriseExtra` no longer carries its four White tints.
  - All Extra schemes now have no tints and no markings (log: `default=True marks=0`), so nothing is laid over the baked paint.
  - New `Liveries.Available(aircraftId)` lists Talk Flying and N300UY for the Extra. **No livery picker UI exists on this branch** (or on main, codex/fg-models or the atmosphere lane): `LiveryId` comes only from `AERO_LIVERY`. A picker should list `Liveries.Available(id)`.
- `AirframeVisual`: removed the `Sunrise.*` node toggle. It belonged to the old `tools/model_extra300.py` GLB, and the new GLBs have no such nodes.
- `AircraftRender`: now accepts `-livery n300uy`, and the Extra check now points at `build_ea300.py`.

**FG EA300 retired**
- The FlightGear-derived GLB only ever existed as `extra-300-like-own.glb` on codex/fg-models. That file has been replaced here by our v12 (d0c6130). `third_party/flightgear/` is not on this branch.
- Result: no FG EA300 mesh, UV or texture is in this branch's Resources or anywhere else in the tree. There was nothing to move or exclude.
- The downloaded Sketchfab Extra (`extra-300-like.glb`, Kemal Çolak, CC-BY) stays on disk, following the owner's 2026-10-25 "keep the downloaded ones" rule. It is no longer loaded by any livery. Because it is in Resources it still ships in the build (~0.7 MB). Move it out if size matters.
- `tools/own_models/ea300_ref_tables.json` (measurements taken from the GPL mesh) is in tools/ only and is not in the build.

**Verification**
- Unity 6000.3.23f1 batchmode compile: clean.
- `AircraftRender.RenderFromArgs -aircraft extra-300-like -livery <id> -out /tmp/ea300-swap/<id>` for tf (`talk-flying`), `n300uy`, `sunrise` and `type`. All exited 0, and every one logged `[Airframe] model rig: 4/4 control surfaces, 1/1 propellers bound`.
- In the renders, `talk-flying`, `sunrise` and `type` show the TF fin badge and TALKFLYING.COM, and `n300uy` shows N300UY with no TF badge. All have the 3-blade prop.
- FlightTests (`~/.dotnet/dotnet test tools/FlightTests`, 15 min): 707 passed, 3 skipped, 1 failed (`CreditsReachTheUser`: the shipped credits were stale). Fixed by copying docs/CREDITS.md to `unity/Assets/StreamingAssets/credits.md`; that test now passes on a rerun. The game shows the new Extra credit in-app.

**GPL derivative review (not legal advice; for Ty's decision)**
- **Source licence.** Patrizio Melis's EXTRA-EA300 is GPL. Its `LICENSE`/`COPYNG` and the forum release say v2. Its `License.txt`/`README` say "v3 or later". Either way it is copyleft.
- **Why the shell is likely a derivative.** Copyright in a 3-D model covers the modeller's sculpted surface, not the real aircraft's dimensions.
  - From v2 on, the fuselage, wing, stab, fin, control surfaces, gear, pants, spinner and canopy were made by sampling Melis's mesh densely: 121 stations × 64 rays of fuselage, plus 41-point sections. They were then lofted back through those samples to within a few cm (99%+ of the silhouette is within 25 mm).
  - Copying is admitted and documented. Resampling a surface and re-meshing it is closer to re-tracing than to independent creation, and it reproduces that modeller's particular errors and choices along with the real aeroplane.
  - "Both just match the real Extra" is a weak defence here, because the fit was made to his mesh, not to drawings.
  - Conservative reading: **the v2–v12 airframe shell is a derivative of the GPL work.**
- **What is ours.** None of this is measured from FG, so none of it is derivative: the v4–v8 cockpit and baggage area, the v11 engine, the v12 inlets, the 3-blade prop, and all paint and lettering.
  - The lettering uses Liberation Sans Bold and Archivo Black, both OFL 1.1. They are pre-rendered into PNG art and no font files ship, so the OFL places no obligation on us. They are credited anyway.
- **What GPL would require.**
  - Applying the GPL to the GLB alone is arguable: as a separate data file loaded by the game, it is "mere aggregation", not a combined program.
  - We would then have to ship the GLB under the GPL, with the licence text and notice, and offer its source in the preferred form for modification: `build_ea300.py`, `paint.py`, `art/` and the ref tables.
  - That much is doable. The CREDITS entry already carries the GPL credit.
- **App Store problem.** Apple's App Store terms (usage rules / DRM, and limits on further redistribution) add restrictions that GPLv2 §6 / GPLv3 §10 forbid. This is the 2010–11 VLC case, in which a GPL copyright holder got VLC pulled from the store.
  - So "keep the GPL credit" alone is **not a safe App Store path**. If Melis objected, Apple would remove the app.
  - Internal builds and TestFlight are fine for now, as Ty decided. Strictly, external TestFlight is also distribution under Apple's terms, but the risk is low.
- **Recommendation**, in order:
  1. Email Patrizio Melis (github.com/cobe571, or the forum thread forum.flightgear.org t=33892). Ask for a written, non-exclusive permission to use a shape measured from his EA300 in a closed-source commercial iOS game distributed through the App Store, with credit. A suggested wording: "May we use a model whose shape was measured from your GPL EXTRA-EA300 in a commercial iOS game on the App Store, outside the GPL, with you credited as the shape reference?"
  2. If he says no, or hasn't answered before App Store submission: rebuild the shell from the clean-room v1 (638c05a), which was built from NZ CAA TAR drawings and photos. Then carry over the v4–v12 cockpit, engine, inlets, prop and paint, none of which is derived from FG.
  3. Until then, keep the GPL credit and keep the build source in the repo.
- **Status: OPEN, and it blocks the App Store only. It does not block TestFlight or internal builds.**
- Local commit only. No push, no merge, no iOS build.

### 2026-10-26 — Codex · codex/own-ea300 · EA 300 v2 redraw to the FG v9 shape
- Added tools/own_models/ea300_measure.py, which slices the v9 FG GLB into tables.
- Rewrote build_ea300.py to loft all-new geometry from those tables, with the v9 N300UY paint rules unchanged.
- Added ea300_compare.py, which measures the 3-D and silhouette deviation.
- Visible-surface p99 is ≤ 3.7 cm in every part. Silhouette outlines are within 25 mm on 99.3–99.9% of the outline. Details are in PROVENANCE.
- Unity rig binds 4/4 surfaces and 1/1 prop.
- Local commit only: no push and no merge.

## 2026-10-26 EA 300 own model v3 (codex/own-ea300)
Did the v3 shape pass and visual QA (see docs/models/ea300-own-PROVENANCE.md v3). Outputs are in the box at /workspace/ea300-own-v3/. Not pushed. The GPL NEEDS YOU note from v2 still stands.
- 2026-10-27 Chief/executor: EA300 own model v4 — added clean-room cockpit interior (seats, harness, sticks, front + rear panels, tub, tube frame); exterior unchanged; renders in /workspace/ea300-own-v4 (box).
- 2026-10-27 Chief/executor: EA300 own v5 — smaller/lower seats+sticks, front seat fwd 0.16 m, angular rear dash top 0.20 m deep with panel under; renders /workspace/ea300-own-v5 (box).

- 2026-10-27 Grok Bot executor: EA300 own v6 cockpit layout (seats/panel/sticks moved, black dash top, collision fixes). Local commit only.
- 2026-10-27 Grok Bot executor: EA300 own v7 cockpit layout (pans -6in, panel/front seat/stick fwd 6in, sticks +4in). Local commit only.
- 2026-10-27 Grok Bot executor: EA300 own v8 (baggage area, TALKFLYING.COM, TALK FLYING PODCAST). Local commit only.

- 2026-10-27 EA300 own v9: two liveries off one build (EA_LIVERY=tf -> extra-300-like-own.glb with fin TF badge; n300uy -> extra-300-like-own-n300uy.glb, N300UY sides, red tapered underwing bar, no TF branding). Side font now Liberation Sans Bold (OFL 1.1). Awaiting Ty sign-off; not handed to Aero Playground.
- 2026-10-27 EA300 own v10: parallel stab paint, side lettering centred wing TE/stab LE, 2nd thin red stripe, 22% underwing bar (plain), 3-blade prop (1/1 bound), TF fin badge centred. Awaiting Ty sign-off.
- 2026-10-27 EA300 own v11: straight spanwise stab paint lines; open cowl inlets; simplified AEIO-540 flat six (staggered banks, left 0.13 m aft), baffles, left governor. Awaiting Ty sign-off; not handed to Aero Playground.
- 2026-10-27 EA300 own v12: D-shaped cowl inlets (~2:1) with lips draped on the skin; front baffle lowered. Awaiting Ty sign-off; not handed to Aero Playground.
# Agent log — lane/claude-regional-atmosphere

## NEEDS YOU
Questions for Ty about the regional atmosphere. Nothing here blocks the branch; each item has a working default.
1. **The calm zone covers the foot of the first bluff.** The zone is a stadium round the Valley's main runway, radius 1,200 m. The first wall's toe is about 1,100 m west of the centreline, so the lower face of the wall is inside the calm column for the zone's 6 km length, below its 762 m MSL ceiling. Ridge lift above the crest and north and south of the zone is untouched. Three options: (a) keep it like this; (b) narrow the zone on the bluff side, with a right-hand pattern to the east only; (c) shorten it along the wall. Default: (a).
2. **Events.** Only free flight uses the regional air. Challenges, scored contests, lessons, practice and every `event:` (races, STOL, dogfight) fly the uniform air exactly as before. Should any events fly the regional air?
3. **"Turbulence: Calm" in regional mode** currently means "the regions' own turbulence" (×1). Light, Moderate and Severe scale it ×1.5, ×2.2 and ×3. Should Calm switch regional turbulence off entirely? The home field is calm either way.
4. **The menu temperature** sets the air mass's ISA deviation everywhere except the home field, which stays exactly ISA as asked. The spawn trim solver still uses the session deviation, so a hot-day spawn at home is trimmed for slightly thinner air than it meets. The difference is tiny. Is this OK?
5. **Altimeters** read true altitude. Regions now have their own QNH (±3 hPa). Should the altimeter read pressure altitude with a local QNH setting? The flight-test data block now shows the regional static pressure.
6. **Sim clock (pre-existing).** Every `Aircraft.Step` advances `Atmosphere.SimTimeSec`, AI aircraft included, so turbulence and the airport-2/3 wind processes run faster when there is AI traffic. This was already true of the old turbulence. Should one place own the clock?
7. **Airport 3's wind is from 060.** I chose 060 because the obvious north-westerly put Mesa in the lee of the wall 900 m west of it, and rotors would have changed the gusts' character. Do you prefer another direction?
8. **Mountain magnitudes.** In 23 kt over the Alaska range, the lee sink reaches about −3.5 m/s and σ about 4.7 m/s, which is moderate-to-severe. This is physically plausible but harsh. Should it be tuned down (`orographicScale`, `leeSinkFraction`)?
9. **Not flown in the editor or on a device.** Unity batchmode compiled everything cleanly, but nobody has looked at the J overlay, the moved Valley thermals or the feel in the air yet.
10. **Moved thermals.** The Valley's four hand-placed thermals and the crop-field one sat inside the zone or its blend band. They now start just outside it (~350–600 m further from the runway). Is that OK, or should they be re-authored?

## Log
### 2026-10-27 — Claude · lane/claude-regional-atmosphere · regional atmosphere, calm home field
Based on split-surfaces `ed2cc7c`. Commits, oldest first:
- `61fe42d` The air differs from place to place now, and is dead calm over the home field
- `9c97ed7` The wings feel the air where each of them is, and the tests pin the new weather
- `55920df` Free flight flies in the regional air, and J shows you what it is doing
- (this log) The atmosphere lane's log

**Where it lives**
- `src/FlyingGame.Core/AtmosphereField.cs`: the field. It holds regions, the calm zone, blending, thermodynamics, the land-cover heating map, the terrain cache and sampling (`Sample(pos, t)` → `AtmosphereSample`).
- `src/FlyingGame.Core/Orography.cs`: speed-up, slope lift, lee sink, rotor, lee waves and mechanical turbulence, all from the height field.
- `src/FlyingGame.Core/WindProcesses.cs`: `SeededNoise`, `VariableWind` (airport 2) and `GustyWind` (airport 3).
- `src/FlyingGame.Core/Turbulence.cs`: extended, not duplicated. It adds a spectral (von Kármán / Dryden) mode and `TurbulenceScales` (MIL-HDBK-1797). The legacy `Light/Moderate/Severe` fields are unchanged.
- `src/FlyingGame.Core/Atmosphere.cs`: `Atmosphere.Field` (null = the old uniform atmosphere, bit for bit), plus `SampleAt`, `WindFromSample`, `SteadyWindAt`, `DensityAt`, `PressureAt` and `TryAddThermal`.
- `src/FlyingGame.Core/DataContracts/AtmosphereFieldConfig.cs` (+ loader): the config classes.
- `configs/atmosphere/regional-atmosphere.json`: the data. It is mirrored byte-for-byte to `unity/Assets/StreamingAssets/atmosphere/`, and `ConfigSync` now mirrors that folder too.
- `WorldTerrain.DefaultAirportIndex` (= 0, the Valley) is the default start airport. `SessionSettings.AirportIndex` defaults to it, and the calm zone anchors to it.
- Flight model: `Aircraft.SampleRegionalAir`, and a `WindGradient` parameter on `AeroModel.Compute`.
- Unity: `SessionSettings` (regional vs uniform), `WeatherController`, `SoaringScenery`, `BubbleField`, `LiftBubbles`, `LiftField`, `SmokeTrail`, `AnalogGauges`, `FlightAudio`, `StartMenu` (an "Air: REGIONAL / uniform" button), plus the new `UnityAtmosphereLoader` and `AtmosphereOverlay` (J).

**The calm start airport: exact definition**
| field | value | why |
|---|---|---|
| id | `home-airport-calm` | |
| anchor | `defaultStartAirport` → `WorldTerrain.Airports[WorldTerrain.DefaultAirportIndex]` = **Valley** (sim 400, 0), field elevation 304.8 m (1,000 ft) | Tied to the default start in code and config, with no typed coordinates. If the default start changes, the zone moves with it. |
| centre | the anchor's **main-runway centre** = airport + `AirportStrips[0]` offset = **(−50, 0)** | The runway, not the airport reference point, is what the pattern is flown around. |
| shape | **stadium** round the main-runway centreline (heading 0°, sim x = north–south), centreline extended **1,800 m** past each threshold → half-length **3,000 m** (x −3,050 … 2,950) | Covers final from about 1.5 km out, the base turn, and the climb-out to pattern height on either runway direction. |
| radius | **1,200 m** either side of the centreline | Holds a downwind leg flown ~1 km abeam on either side, plus a 200 m turning margin. The world is compressed, so patterns are tight here. |
| ceiling | **457.2 m (1,500 ft) above field elevation** → flat at **762.0 m MSL (2,500 ft)** | Pattern altitude is 1,000 ft AFE (304.8 m; `TugPilot.PatternAglM` 300 m); +500 ft margin for pattern-height slop and an overhead join. Measured above **field elevation (AFE)**, not local AGL, so the ceiling is one flat MSL height over the whole zone. |
| blend width | **600 m** outward from the stadium edge (fully regional 1,800 m from the centreline) | Smootherstep (C²), monotonic, with the steepest change 0.31 % of the full change per metre. |
| ceiling blend | **150 m** upward (fully regional at 912 m MSL) | |

*How the calm surface is exactly zero.*
- `CalmZone.Weight` is a product of a horizontal and a vertical smootherstep. Inside the stadium and below the ceiling it is exactly 1.0.
- At weight 1, `AtmosphereField.Sample` returns `CalmSample` directly. That sample is standard ISA at that altitude: QNH 1013.25 and deviation 0, so pressure equals `PressureAtAltitude` and density equals the ISA density to the bit. Wind, gust, σ, thermal factor, convection, slope, lee and wave are all literal zeros. It is the calm sample itself, not a blend that happens to be small.
- `Atmosphere.WindFromSample` adds the authored thermals × the sample's thermal factor (0) and the glacier wind × (1 − calm) (0).
- While a field is set, the legacy `SteadyWind`, `ActiveTurbulence`, `ActiveRidge` and `SlopeLiftEnabled` are not read. The aircraft's density comes from the field.
- Thermals: `TryAddThermal` refuses any thermal centred in the zone or its blend band, and `NearestThermalSite` moves authored ones straight out of it. In the blend band every regional quantity, including the authored thermals' strength, is multiplied by (1 − calm weight), so the edge has no step.
- Tests: `StartAirportIsExactlyCalmIsaFromTheSurfaceThroughThePattern` samples 128 points at four sim times. They are the runway centre and both thresholds at 0.5–405 m, the downwind legs, both finals, the climb-out and the cross-runway ends. Each asserts `x == 0 && y == 0 && z == 0` for the mean wind, the gust and `Atmosphere.WindAtPosition`, σ == 0, and pressure/temperature/density == ISA exactly. A 12 m/s thermal just outside the zone is active during the test. Also `CalmZoneIsTheDefaultStartAirportsAndCoversThePattern`, `CalmZoneBlendsOutSmoothlyAndMonotonically` (calm weight, wind, σ and ISA deviation monotonic across the band; steepest step bounded; beyond the band equal to a field with no calm zone) and `NoThermalStandsInTheCalmZone`.

**Regions (data, `configs/atmosphere/regional-atmosphere.json`)**
Regions are layered in list order over a global `background`. Each paints over what is below it with weight 1 inside its shape, falling to 0 across its blend band by smootherstep. The calm zone is applied last, over everything.

| region | where | air |
|---|---|---|
| background | everywhere | synoptic (menu) wind, open-country profile α 0.14 to 600 m, ISA lapse |
| coast-marine | east of the shore (box) | ISA −4, QNH +2, stable (4.5 K/km), z0 0.0002, onshore ×1.3, thermals ×0.25, slight subsidence |
| outwash-glacier | the braided plain | ISA −6, stable, smooth, weak thermals |
| high-desert | Reno | ISA +15, QNH −3, 8.5 K/km, thermals ×1.8, 3 km boundary layer |
| alaska-range | ice and rock | ISA −12, wind ×1.6, z0 0.3, orographic ×1.3, stable 5 K/km (waves) |
| alpine-range | granite | ISA −8, wind ×1.4, veer −15°, z0 0.5, orographic ×1.3 |
| frank-church | forested canyon | ISA +6, sheltered (×0.8), z0 0.8, thermals ×1.4 |
| airport2-variable | Bench (airport 2), r 1,400 m, blend 700 m, to 600 m AFE (+300 m blend) | light and variable, 5 kt |
| airport3-gusty | Mesa (airport 3), r 1,400 m, blend 700 m, to 600 m AFE (+300 m blend) | 060 at 5 kt gusting 15 |

Every region has its own ISA deviation and QNH, which give temperature, pressure, density and density altitude. Pressure uses the altimeter relation p = QNH/1013.25 · p_ISA(h), so a standard region reproduces ISA exactly. Each region also has a lapse rate (stability, which drives lee waves) and a 10 m wind with a power-law profile up to a gradient height. The wind can be in one of four modes: `synoptic` (the air mass, scaled and veered), `steady`, `variable` or `gusty`. Each region also has roughness z0, turbulence and clear-air σ, a thermal multiplier, a boundary-layer depth and a background vertical drift.

Thermals and sink come from the ground. A land-cover heating map is built from the world's own data: `WorldTerrain.SurfaceAt`, `Forest.CoverAt`, `FloatHydro.WaterSurfaceAt` and the slope aspect. It is cached on a 100 m grid. Rock, gravel and scree heat best (+0.85), then dry rough ground (+0.55), fields and pavement, then grass (+0.35). Forest becomes a weak sink (−0.15), and water (−0.7) and snow (−0.5) sink. Sun-facing (south-facing) slopes gain heat. Over hot ground the background convection is cellular, using the Lenschow & Stephens profile up to the region's boundary-layer depth. Over cold ground it is a gentle sink. The authored thermal columns still fly, scaled by the region's thermal multiplier.

**Bluff / ridge lift (owner requirement A)**
- *Speed-up.* ΔS = MaxSpeedUp · clamp(Hw / SpeedUpRelief) · e^(−agl/SpeedUpDecay). Hw is how far the ground here stands above the distance-weighted ground upwind, over a 3.2 km look along the wind. This follows Jackson & Hunt and the Taylor–Lee guidelines (ΔS ≈ 1.6 H/L ridge, 0.8 H/L escarpment), capped at 0.6 because these walls are steeper than attached flow allows. Climbing the slope at constant height above it, the wind therefore strengthens progressively to its peak at the crest.
- *Upslope.* w = V·∇h is the existing `SlopeLift`: the kinematic boundary condition carried up tilted streamlines, blocking reach ahead of the face, and the 40° separation cap. It is now driven by the sped-up wind and runs over any height field.
- *Lee.* The upwind crest is found by a soft arg-max weighted toward the nearest sample at full drop (behind an escarpment the "crest" is the rim). The lee terms are:
  - a downdraught −LeeSinkFraction·U·e^(−s/2H)·e^(−((z/H−0.6)/0.7)²);
  - a Lamb–Oseen-style rotor 1.2 H downwind and 0.45 H up, spinning at 0.7 × the crest-level wind with its top downwind, which reverses the surface flow under it.
  Downdraughts fade in between 2 and 6 m/s and rotors between 4 and 10 m/s, so a 5 kt breeze makes neither.
- Tests:
  - `WindStrengthensAndSlopeLiftGrowsTowardTheBluff`: synthetic 600 m escarpment, 15 kt, 100 m above the ground. U rises from 10.65 to 12.7 m/s and beyond, w from 0 to 8.3 m/s, both monotonic, and the crest beats the free stream by > 20 %.
  - `RealBluffHasLiftInFrontOfIt`: the Valley's first wall at x = −5,000.
  - `LeeHasSinkRotorAndWaves`: at 25 kt the lee sink is −3.9 m/s and the near-ground wind under the rotor is −2.5 m/s against a free 14.2 m/s; no reversal at 5 kt.

**Turbulence model (B)**
- Owned by the field. The spectral field in `Turbulence.cs` is a fixed set of 48 random Fourier modes, divergence-free, log-spaced over eddies from ~30 km down to ~1.6 m. The amplitudes are chosen per sample from a ladder table for the local length scale, so L can vary smoothly over the map. The spectrum is the 3-D von Kármán E(k) ∝ (ℓk)⁴/(1+(ℓk)²)^(17/6), ℓ = 1.339 L (default), or Dryden (Lk)⁴/(1+(Lk)²)³, set by `turbulence.spectrum`.
- Intensities and length scales come from the MIL-F-8785C / MIL-HDBK-1797 low-altitude model: L_w = h, L_u = h/(0.177+0.000823h)^1.2, σ_w = 0.1 W20, σ_u = σ_w/(0.177+0.000823h)^0.4. Above 1,000 ft they run to 2,500 ft (von Kármán) or 1,750 ft (Dryden) by 2,000 ft. W20 comes from the region's own profile. The result is then scaled by:
  - roughness, ln(10/0.03)/ln(10/z0);
  - boundary-layer decay above 1,000 ft;
  - the region's turbulence multiplier;
  - convective turbulence (0.6 w* (z/zi)^⅓);
  - orographic turbulence (below);
  - clear-air σ.
  These are combined in quadrature.
- The field drifts with the synoptic wind (Taylor) and slowly evolves. It is zero at the start airport.
- Tests: `SpectralTurbulenceStatisticsAreSane` over 6 seeds × 120 km.
  - von Kármán: rms 0.997, mean −0.001, integral scale 179 m for L = 150, D(2r)/D(r) = 1.62 (theory 1.62 with this cut-off).
  - Dryden: rms 0.997, integral 192 m, D(2r)/D(r) 1.97 (theory 1.93).
  - `MilHandbookScalesAreTheHandbooks`.

**Orographic (mountain) turbulence (C)**
- σ_oro = (SlopeTurbulence·|∇h|·U·e^(−agl/max(150 m, relief)) + LeeTurbulence·U_crest·e^(−s/3H)·e^(−agl/H)) × the region's orographic scale. It grows with wind speed, slope and roughness (via z0 in the MIL part), is strongest against the ground and in the rotor, and decays above the ridge tops.
- Lee waves stand when the region is stable (N² = g/T·(Γd − Γ) > 0) and the wind is over 6 m/s. They alternate sink and lift downwind of the crest above crest height, with wavelength 2πU/N × 0.35.
- Tests: `TurbulenceGrowsTowardTheTerrainAndDecaysWithHeight` gives lee σ_w of 2.29 / 2.09 / 1.73 / 0.92 / 0.48 / 0.32 m/s at 20 / 100 / 300 / 700 / 1,200 / 2,000 m, and the lee is > 1.3× open ground. Waves reach ±3 m/s in stable air and are exactly 0 in unstable air.

**Airport 2: light and variable (D)**
- `VariableWind`: a filtered random walk. The direction's turn rate is an Ornstein–Uhlenbeck process (σ 2.5°/s, correlation 40 s, hard cap 6°/s). The direction integrates the rate, and the speed wanders about 5 kt (σ 0.8 kt).
- Knots are generated every 1 s from hashed draws of (seed, index) and joined by cubic Hermite curves, so the result is seeded, deterministic, smooth (C¹ direction), free of jumps, and the same at a given sim time whatever the frame rate.
- Test: over an hour the direction visits all 36 10° sectors, the mean speed is 4.79 kt and the maximum turn rate is 6.0°/s.

**Airport 3: 5 kt gusting 15 (E)**
- `GustyWind`: a steady 060 lull with discrete 1 − cos gusts (the MIL-F-8785C discrete-gust / CS-25.341 shape). Gaps are exponential (mean 18 s), durations uniform 4–10 s, and amplitudes uniform from 75–100 % of (peak − lull).
- The lull (3.6 kt) is solved from those statistics so that the long-run mean is the METAR's 5 kt.
- Gusts sweep across the airport with the wind (Taylor delay), so they reach a downwind point, or wing, later.
- Test: mean 5.03 kt, peak 14.97 kt, 142 gusts an hour, direction steady to ±0.00° (turbulence off to isolate the gust train; it is tested separately), and the steepest onset 4.3 m/s² (a step would read about 20 m/s²).

**Flight model (4)**
- When a field is set, each step samples the air at the CG, both wing tips and the tail mean (4 samples).
- `AeroModel` gets the wind gradient across the airframe (per metre along body x and y), so every strip sees the wind where it actually is. A spanwise gradient therefore rolls and yaws the aircraft.
- `ALiftedWingRollsTheAircraft`: a thermal under the right wing gives −5.6°/s of roll in 0.4 s against −0.0°/s with CG-only sampling.
- Deterministic and frame-rate independent:
  - The sample is a pure function of (position, sim time).
  - The sim runs fixed 200 Hz steps whatever the render rate; 20 fps vs 100 fps chunks are bit-identical.
  - 200 Hz vs 400 Hz physics in turbulence differ by 0.03 m / 0.01° after 6 s.
  - The wind is held through RK4 sub-steps exactly as the uniform wind always was.
- Cost: ~10.7 µs a sample including cache fills on a flight across the bluff (`SamplingIsCheap`; ~7 µs warm), so ~45 µs per 200 Hz step. A 20 m tiled terrain-height cache took it down from 75 µs.

**Uniform override (1)**
- `Atmosphere.Field = null` is the old atmosphere, unchanged: every existing test passes untouched.
- Free flight uses the field. The menu wind, temperature, turbulence and thermals become the air mass.
- Challenges, contests, practice, lessons and events stay uniform. This covers the headwind and crosswind challenges' contest wind, and `PracticeScenario.Spawn` clears the field itself.
- In flight, K / [ / ] (one wind for the map) drop to the uniform air.
- A one-region global config is also a uniform atmosphere.

**Realism vs variance: the compression (F)**
The map is ~16 × 27 km. Real air over that would be practically one air mass. The physics keeps its real form, and the scales of the things that make places differ are squeezed so that several kinds of air coexist:
- Air masses ~10× closer together than in nature. Regions are 1.4–3.5 km across with 0.6–1.2 km blends, and differences that need hundreds of km in the real world sit a few km apart: +15 °C desert beside −12 °C ice, ±3 hPa QNH.
- The pressure differences are not allowed to drive winds. A real 6 hPa over 10 km would be a hurricane-force gradient wind.
- Convective cells 700 m apart (real ≈ 1.5 z_i ≈ 3 km).
- Lee wavelength ×0.35 (real 5–15 km → ~2 km at 20 kt).
- Speed-up relief and decay of 400 m / 300 m, matched to this world's walls rather than to real hill lengths.
- Lee rotor and sink are scaled by the terrain's own drop H, so they are not compressed.
- Airport micro-climates are 1.4 km radius, capped at 600 m AFE.
- Turbulence length scales are not compressed: they matter to the airframe, not to the map.

Tunable knobs, all in the config:
- `synoptic.*` (overwritten by the menu in free flight);
- `calmZone.{anchor, radiusM, extendBeyondThresholdsM, ceilingAfeM, blendWidthM, ceilingBlendM}`;
- per region: shape, anchor, size, blend, ceiling, `isaDeviationC`, `qnhOffsetHpa`, `lapseRateCPerKm`, `roughnessM`, `turbulenceScale`, `clearAirSigmaMs`, `orographicScale`, `thermalScale`, `boundaryLayerM`, `backgroundVerticalMs`;
- `wind.{mode, speedScale, veerDeg, fromDeg, speedKt, gustPeakKt, meanGustIntervalS, min/maxGustDurationS, speedJitterKt, turnRateSigmaDegS, turnRateMaxDegS, correlationS, seed, profileExponent, gradientHeightM}`;
- `turbulence.{spectrum, modes, seed, scale, minLengthM, maxLengthM}`;
- `orography.{maxSpeedUp, speedUpReliefM, speedUpDecayM, leeSinkFraction, rotorStrength, rotorDistanceDrops, leeMin/FullWindMs, rotorMin/FullWindMs, minLeeDropM, slopeTurbulence, leeTurbulence, waveLengthCompression, waveAmplitude, minWaveWindMs}`;
- `convection.{backgroundUpdraftMs, cellSizeM, gridM}`.

**Debug visualisation (5)**
`AtmosphereOverlay`, toggled with J (`SessionSettings.ShowAtmosphereMap`), draws:
- each region's outline on the ground in its own colour, with its blend edge dimmer;
- the calm zone's stadium and blend edge, and its ceiling in the air with posts;
- wind arrows on an 11 × 11 grid round the aircraft at its height (a green or red tick for rising or sinking air);
- the lift/sink map 300 m above the ground (green or red crosses);
- the thermal columns;
- a readout of the local air: region, wind, w, σ and L, OAT and ISA deviation, QNH, density altitude, and the slope, lee, wave and convective parts.

**Test results**
- Baseline before any change: **708 passed, 3 skipped, 0 failed** (711).
- After: **729 passed, 3 skipped, 0 failed** (732; the 3 skips are the same pre-existing ones). That is the 708 old tests unchanged, plus 19 new tests and 2 opt-in probes.
- New: `RegionalAtmosphereTests`, 19 tests (all pass). `RegionalAtmosphereProbe` is opt-in diagnostics (`REGATM_PROBE=1|2`) and passes trivially otherwise.
- Unity 6000.3.23f1 batchmode compile (fresh Library in this worktree): Core, Sim, Bridge and EditorTools compile with no errors, and Unity modified no tracked files.
