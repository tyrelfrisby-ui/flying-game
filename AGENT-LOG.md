# Agent log — codex/own-ea300

## NEEDS YOU
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
- [codex → Claude] lane/codex-extra-cockpit · Extra 300 v15 clean-room rigged cockpit (NOT yet approved by Ty) · wire the cockpit rig and canopy into the game · details:
  - Model: extra-300-like-own{,-n300uy,-unmarked}.glb rebuilt (tools/own_models/build_ea300.py + new ea_cockpit.py, ea_rig.py, ea_dialmaps.py, make_ea_dials.py, art/ea-dial-*.png, ea-avx-*.png, ea-compass-band.png). Same GLB axes, scale, node names for the airframe and v13 hinged surfaces. The old `Cockpit` mesh is now `Interior/Cockpit_Shell`; `Cockpit` is a group. The canopy's side windows are split off as static Window_Glass / Window_Frame.
  - Rig: docs/models/ea300-own-RIG.md (node table, params, estimates marked E) and docs/models/ea300-own-rig.json (machine-readable; the same data is in each node's glTF extras). 60 rig nodes + 2 displays. Front seat: ASI, ALT, G (with max/min tell-tales), TACH, oil. Rear (PIC) seat: full panel, ASI / TC / ALT / G / MP / VSI / CHT / fuel / oil / tach / mag key / compass, plus a Trig-style radio head (TY91/TY96 type) over a transponder head (TT21/TT22 type), each 63 x 44 mm, plain dark faces, no maker text. Rigged: com_freq_mhz, com_standby_mhz, com_volume, xpdr_squawk, xpdr_mode, xpdr_knob_clicks (displays: Radio_Display_R, Xpdr_Display_R, text drawn by the game).
  - Controls on real pivots in both seats: stick roll+pitch, pedals with toe brakes, throttle; rear only: prop, mixture, trim, master, mag key. Fuel selector and canopy lock in both seats.
  - Canopy: root `Canopy` on the right-sill hinge (Canopy_HingeAxis), opens 0..100 deg to starboard (canopy_open), red locks CanopyLock_F/R (canopy_lock, 1 = locked), CanopyRelease_F/R pull handles (canopy_jettison). On a bailout, jettison the actual Canopy node (detach it and give it the airframe velocity); nothing else comes off.
  - Task: cockpit camera at Eye_Rear by default (extra `default_view: true`); Eye_Front selectable. Drive every rig node from rig.json each frame (local rotation about axis_gltf, maps piecewise linear and clamped unless rig_map_extrapolate / rig_map_wrap). Draw the radio / transponder text on the two display quads. No intercom, headset or cable items.
  - Physics: unchanged. Surface travels in the rig are the EA 300/L POH values (aileron ±30, elevator ±25, rudder ±30).
  - Follow-ups: no elevator trim tab is modelled (Trim_R moves only). The GPL question on the v2–v12 shell is still Ty's (NEEDS YOU); the v15 cockpit itself is clean-room. The Pitts needs the same cockpit treatment next.
  - Renders: agent box /workspace/extra-cockpit/ (Mac copy: flying-game-worktrees/_xfer/extra-cockpit/): eye views, panels, avionics, canopy closed/half/open/jettisoned, MIN/NEU/MAX sheets, 3 photo comparisons.
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
### 2026-10-30 — Grok Bot executor · lane/codex-extra-cockpit · Extra 300 v15 rigged cockpit

Built to the Great Lakes v13 standard on Ty's request (2026-10-30). The new cockpit is clean-room, placed by eye from public photos (rear-panel photo, a 330LX tandem cockpit, an EA 300-L side view) and text sources (POH limits and travels, AAIB G-ZXCL and Patty Wagstaff notes on the one-piece canopy hinged right, the 300L IPC). The sources are listed in docs/models/ea300-own-PROVENANCE.md (v15). No photos are committed. Rig: 60 nodes + 2 displays, every instrument a node with a map; the estimates are marked E in docs/models/ea300-own-RIG.md. Canopy opens, locks and jettisons as its own part. The Trig-style heads sit at the right edge of the rear panel, clear of the rear stick. All three liveries were rebuilt and re-imported in Blender (60 rig nodes each, Eye_Rear default). Physics and configs were not touched. Open: Ty's GPL decision on the v2–v12 shell (NEEDS YOU), and the Pitts needs the same cockpit.

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
