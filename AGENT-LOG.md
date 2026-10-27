# Agent log — codex/own-ea300

## NEEDS YOU
- **EA300 GPL — decision needed before App Store release (Claude's review, 2026-10-27; see the Log entry below).** Our own EA300 is now the in-game Extra. Claude's assessment: treat the airframe shell (v2–v12) as a likely derivative of Patrizio Melis's GPL FlightGear EA300, and treat GPL content as not safe to ship through the App Store. **Ty: pick one before App Store release:** (a) ask Patrizio Melis (github.com/cobe571) for written permission to use the measured shape in a closed, commercial App Store game (a draft request is in the log entry); or (b) put the v12 cockpit, engine, inlets, prop and paint onto the clean-room v1 shell (638c05a). This does not block TestFlight or internal builds.

## HANDOFF
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
