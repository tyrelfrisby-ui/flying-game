# Agent log — codex/own-ea300

## NEEDS YOU
- **EA300 GPL — decision needed before App Store release (Claude's review, 2026-10-27; see the Log entry below).** Our own EA300 is now the in-game Extra. Claude's assessment: treat the airframe shell (v2–v12) as a likely derivative of Patrizio Melis's GPL FlightGear EA300, and treat GPL content as not safe to ship through the App Store. **Ty: pick one before App Store release:** (a) ask Patrizio Melis (github.com/cobe571) for written permission to use the measured shape in a closed, commercial App Store game (a draft request is in the log entry); or (b) put the v12 cockpit, engine, inlets, prop and paint onto the clean-room v1 shell (638c05a). This does not block TestFlight or internal builds.
- **Pitts S-2B (Claude, 2026-10-27): three small decisions, none blocking.**
  - **Players cannot pick N1191 yet.** N31DG shows for every livery. N1191 appears only with `AERO_LIVERY=n1191`, which is a launch-time env var for captures and dev builds. No livery picker exists in the game, and building one (UI, persistence, rebuilding the airframe) was more than "easy and safe" here. `Liveries.Available("pitts-s2b-like")` already lists N31DG and N1191 for a future picker. Say if you want one.
  - **The flight config's gear doesn't quite match the model (physics left unchanged).** On its wheels the model sits right: same three-point attitude (10.1° vs the config's 10.0°), tailwheel on the config's ground line within 2 cm. But relative to the wings, the config's main gear is about 0.19 m short and its tailwheel about 0.4 m forward of the model's. Visually, the sim CG ends up ~0.33 m below the model's thrust line (about 0.1 m above the lower wing), and the physics wings sit ~0.2 m below the drawn ones. That is where the aeroplane appears to roll and pitch about. Matching it means lengthening the config gear, which changes ground handling and needs re-tuning against GroundLoopTests and the other taildragger tests. Say if you want that.
  - The HANDOFF text has the props swapped. The GLBs and PROVENANCE are right, and the tests now pin it: N31DG has the Hartzell 2-blade, N1191 the MT 3-blade.

## HANDOFF
- [codex → Claude] codex/own-pitts-s2b · APPROVED by Ty (2026-10-27, v10 c8b90bc: "send them to aero"; v10.1 removes the fin "Pitts S-2B" script on Ty's request — use v10.1) as DONE: **DONE by Claude on lane/claude-pitts-own (2026-10-27), see Log; open points under NEEDS YOU** · own clean-room Pitts S-2B in two liveries · wire into Aero Playground as the in-game Pitts S-2B · details:
  - Models: unity/Assets/_Project/Resources/Models/pitts-s2b-like/pitts-s2b-like-own.glb (N31DG: red/white sunburst, checkered rudder, MT 3-blade prop) and pitts-s2b-like-own-n1191.glb (N1191: factory sunburst with diamonds, Hartzell 2-blade prop). The n1191 GLB has no .meta yet (Unity generates it).
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
### 2026-10-27 — Claude · lane/claude-pitts-own · own Pitts S-2B (v10.1) is now the in-game Pitts, N31DG by default and N1191 as livery `n1191`
**Wiring** (follows the EA300 swap)
- `AirframeModels`:
  - New `OwnSpecs["pitts-s2b-like"]`: nose +x and Y-up, like the EA300. It has a scale trim of 6.094/5.996, so the model comes out at its true 6.10 m span and 5.72 m length. Span-fitting alone would size it to the config's outermost strip span.
  - `PickOwn` always takes our model. The procedural Pitts style remains only as the fallback when the GLB is missing.
  - `OwnSuffix`: livery `n1191` loads `-own-n1191`; every other livery loads `-own` (N31DG). New overload `OwnSuffix(model, livery)`.
- `Livery.cs`:
  - Added `N31DGPitts` (`n31dg`) and `N1191Pitts` (`n1191`), plus a per-type talk-flying entry with no tints and no marks. Log shows `default=True marks=0` for every id.
  - `Available(pitts)` returns N31DG and N1191. There is no picker UI (see NEEDS YOU).
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
