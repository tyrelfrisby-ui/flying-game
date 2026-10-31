# Agent log — Codex preparation review

## NEEDS YOU
- **The rigs mirror is no longer committed on this lane, and merging it needs `lane/claude-config-mirror` first (Claude, 2026-10-30).** `unity/Assets/StreamingAssets/rigs/` was gitignored-but-tracked here, the same trap that let great-lakes-like ship with no runtime config: `git add` on a new rig mirror does nothing, only `git add -f` works, and nothing notices. The mirror is now untracked and gitignored; `ConfigSync` on this lane still writes it at editor load, so the lane works standing alone. **Ty: merge order is `config-mirror`, then `flow-plates`, then `extra-cockpit`, then `sgs233-rig`.** `ConfigSync.cs` WILL conflict when this lane merges after `config-mirror` — both rewrote the file from the same base. The resolution is fixed: **take `config-mirror`'s version** (its `Mirrors` table already declares the rigs mirror and derives the destination name from each rig table's own `model` field), **drop this lane's bespoke rigs block, and re-add `AirframeRig.Forget()` where a changed rig is written** — that call clears an editor-side cache and `config-mirror`'s generic sync cannot make it, because `AirframeRig` does not exist on its base. This lane branched at `ed2cc7ca`, not `1b67dd5`, so it needs bringing forward before it is merged — it is the last of the four for that reason.
- [Ty/codex] lane/claude-sgs233-rig · the 2-33's YAW STRING swings the WRONG WAY, and barely swings · one sign in docs/models/sgs233-own-rig.json · details:
  - `axis_gltf` on YawString_1..4 is meant to be the canopy normal, which must be square to the string. It is 21-29 deg off the string's own direction instead, so each link twists about its own length: at beta +20 the tip moves 17 mm to STARBOARD, where the rig doc's own text says the tail should swing to PORT.
  - The y component has the wrong sign. Flip it and both problems go: `0.56524, -0.82493, 0` / `0.5145, -0.85749, 0` (links 2 and 3) / `0.50718, -0.86184, 0`. Checked by hand: with those the axes are square to the links and the tip lays over 40 mm to PORT at beta +20, exactly as the doc describes.
  - I have NOT touched the GLB or the rig doc (not my lane). Wired as written, so until it is fixed the string reads backwards in play. `SgsRigCheck` now fails on it, so the fix will be confirmed by re-running the check.
- [Ty] lane/claude-sgs233-rig · the 2-33 now gets WHEEL BRAKE from the air-brake handle over 0.9..1.0, per the AFM and the rig doc's advice. On the touch pad full spoiler is anywhere below 25 % of the left pad, so landing with the brakes fully out now also locks the wheel. That is the real aeroplane, but it is a change of feel on the roll-out — say if you want the band moved or the coupling off (`controls.spoiler.wheelBrakeFromSpoiler` in the config; 0 turns it off).
- [Ty] lane/claude-sgs233-rig · the trim quadrant has four notches and NONE in the middle, so at zero trim the lever sits in the nearest one, 6.7 deg to the nose-down side. The sim's trim is still continuous; only the lever is notched (which is what the rig doc asks for). Say if you would rather see it move smoothly.
- [Ty/Chief] codex/own-sgs233 (4f11104 + this log commit) · merge only after Claude has reviewed the own SGS 2-33 handoff below and confirmed provenance · nothing pushed or merged by Codex.
- Merge note: AGENT-LOG.md was created separately on codex/fg-models and on codex/own-sgs233 (copied from codex/fg-models, then appended), so merging both gives an add/add conflict on this file -- keep the union of the entries.
- Draft glider warning is stale: split-surfaces is clean at ed2cc7c, containing the eight specified files. Claude owns their validation; Codex has not approved calibration.
- Register existing external work before headless dispatch: ../flying-game-reference/out/reference-fleet/ contains the new C172 rough draft; ../flying-game-reference/out/beaver-v2/ contains the reference-built Beaver; ../flying-game-audit/ contains the unfinished physics audit. Geometry must follow drawings/photos, not existing fleet meshes or aerodynamic config fitting.

## HANDOFF
- [Claude → codex] lane/claude-sgs233-rig (off lane/codex-sgs233-cockpit fbe7cac) · the v10 rig is WIRED: all 30 nodes, driven from the sim's actual control positions · details:
  - The table is read at runtime from your own file. ConfigSync mirrors docs/models/*-rig.json into StreamingAssets/rigs, keyed by the `model` field inside it, so a rebuilt rig arrives with nothing re-typed on my side and nothing renamed on yours. Nothing is transcribed into C#.
  - The carve is skipped for a model that brings a rig (it existed to cut hinges out of a downloaded solid aeroplane; yours already has them, and carving would tear the mesh and deflect it twice).
  - Conventions I relied on, so you know what not to break: every rigged node's own GameObject keeps its exact glTF name (a second primitive becomes a child `<mesh>_1`, which rides along), no node in the chain carries a rotation, and every pivot is on the node's origin. Confirmed in-engine: 30/30 bound, worst pivot 0.1 mm out, every parent as the table says.
  - One fault found, in NEEDS YOU above: the yaw string's axes are not square to the string (one sign).
  - Verify with: `Unity -batchmode -quit -nographics -projectPath unity -executeMethod FlyingGame.EditorTools.SgsRigCheck.Run` — it measures pivots, parents and which way every control actually goes, and exits non-zero on a fault. `RIG_SHOTS=<dir>` (with graphics) adds cockpit and 3/4 shots.
- [codex → Claude] lane/codex-sgs233-cockpit · own SGS 2-33 v10: every moving part rigged (supersedes the v8 node list) · wire all 30 nodes · details:
  - Rig is now 30 nodes (docs/models/sgs233-own-RIG.md + sgs233-own-rig.json, with neutral/range/coupled columns). NEW: YawString_1..4, a parented 4-link chain on param `sideslip_deg` (+beta = wind from the right, tail to PORT; 8/4/4/4 deg per link at 20 beta; apply local rotations down the chain). The static crown anchor is now `YawString_Tape`.
  - Wire: aileron→Aileron_L/R + TorqueTube (sticks ride on it); elevator→Elevator_L/R + Stick_F/R; rudder→Rudder + all four pedals (60 mm slide); spoiler→4 doors + DiveBrake_Lever_F/R (wheel brake from spoiler 0.9..1.0); tow_release→TowRelease_F/R (50 mm pull); trim→Trim_Lever; airspeed_mph, altitude_ft (100 ft + 1000 ft hands, wrap), vario_kt, heading_deg (wrap), sideslip_deg. Every node origin is on its pivot: rotate the local rest pose by interp(map) about axis_gltf; kind move = translate in metres.
  - Verified at min/neutral/max in renders (agent box /workspace/233-v5/). Judgement values: stick roll ±18 / pitch -15/+18, pedal slide ±60 mm (the real pedals may hinge), trim ±20, yaw-string split. Configs untouched (your lane). No radios/intercoms.
  - Flag for Ty: factory livery still carries N2033A (previously approved).
- [codex → Claude] lane/codex-sgs233-cockpit · own SGS 2-33 v8: photo-literal interior (supersedes the v7 node list) · re-wire the rig · details:
  - Rig is now 26 nodes (docs/models/sgs233-own-RIG.md + sgs233-own-rig.json). REMOVED: Needle_ASI_R, Needle_Alt100_R, Needle_Alt1000_R, Needle_Vario_R and Instruments_Rear (no rear panel in the photos) -- drop their bindings.
  - Changed maps: Needle_ASI_F 20→15, 160→345 deg (40-160 mph dial); DiveBrake_Lever_F/R spoiler 0→33 deg (was 60). All cockpit pivots moved -- re-read pivot_gltf from the doc. Flight-surface nodes unchanged.
  - New static meshes only (no new rig nodes): Canopy_Hardware, Skylight_Glass/Frame, wall placards, cross-tube RELEASE sleeve, cables. Configs untouched (your lane); wheel-brake coupling and contact points as in v7.
  - Flag for Ty: factory livery still carries N2033A (previously approved); photos' red/white paint not applied.
- [codex → Claude] lane/codex-sgs233-cockpit · own SGS 2-33 v7: cockpit + 3-view pass · wire the rig, review · details:
  - Models rebuilt (both liveries) with cockpit: seats, harnesses, front/rear panels, compass, yaw string, torque tube + sticks, pedals, tow releases, dive-brake levers, trim. Nodes/axes/maps: docs/models/sgs233-own-RIG.md (+ sgs233-own-rig.json). 30 rigged nodes; params aileron, elevator, rudder, spoiler, trim, tow_release, airspeed_mph, altitude_ft, vario_kt, heading_deg.
  - Please wire: all 30 nodes per the RIG doc; wheel brake from spoiler over [0.9, 1.0]; config contact points (configs are your lane, untouched): main wheel contact at 3-view side px 388 r 0.18 m, new tail wheel at px 739 r 0.05 m, wing-tip wheels at y ±7.5 m.
  - Wing is now NACA 43012A with 3.5° incidence / 3° washout -- geometry only; aero config untouched.
  - Flag for Ty: factory livery still carries N2033A (previously approved).
- [codex → Claude] codex/own-sgs233 · APPROVED by Ty (2026-10-26) as DONE: own clean-room Schweizer SGS 2-33 with two liveries · review, integrate, confirm provenance · details:
  - Models: unity/Assets/_Project/Resources/Models/glider-2-33-like/glider-2-33-like-own.glb (factory all-yellow 255,204,0, N2033A) and glider-2-33-like-own-tf.glb (Talk Flying: same yellow, black cheat line + silver pinstripe, TALKFLYING, logo plate, talkflying.com; wing top TALK FLYING port / PODCAST starboard with the wings-and-headset emblem centred; underside TALK FLYING / PODCAST). All paint baked; no surface solid black. Built by tools/own_models/build_sgs233.py (SGS_LIVERY=talk-flying for the TF GLB) + tools/own_models/paint.py, art in tools/own_models/art/.
  - Code: AirframeModels.cs -- PickOwn uses the own 2-33 for LiveryId "factory" and "talk-flying"; OwnSuffix() loads -own or -own-tf per livery (Has/Place agree). Livery.cs -- glider-2-33-like "talk-flying" entry is now SgsYellow on all surfaces with no runtime decals (all baked; runtime wing words sat under the high wing, fin logo floated off the fin). AirframeVisual.cs carries the pre-painted-model handling from fg-models.
  - Provenance: original clean-room model from the Wikimedia Commons 2-33A 3-view and photos only -- no FlightGear/GPL geometry or textures (docs/models/sgs233-own-PROVENANCE.md). It REPLACES the FlightGear sgs233 for App Store use; please confirm the provenance doc is sufficient and that no GPL sgs233 asset remains referenced for the 2-33.
  - Render tooling carried over from codex/fg-models: Editor/AircraftRender.cs (RenderFromArgs -aircraft -livery -out; side/top/bottom/3q + black-background variants), Editor/C172Render.cs.
  - Renders for review: /workspace/sgs233-own-v6/ (TF) and /workspace/sgs233-own-v5/factory/ on the agent box; factory output is pixel-identical to the approved v4.
- [codex → Claude] lane/codex-lanes-prep-review · review measured visual/config discrepancies when audit is complete; retain physics ownership · ../flying-game-audit/ and aircraft configs · verify source measurements before any calibration edits; current audit remains unfinished.
- [codex → Chief] lane/codex-lanes-prep-review · register current visual/audit work and resolve runner conflicts before go-live · external locations above and Glass Overlay LANES-PREP-REVIEW-2026-10-26.md · check owner tags, isolated worktrees and no-deploy rule; install drafts only after Ty approval.

## Log

### 2026-10-30 — the rigs mirror stops being committed (Claude)

`unity/Assets/StreamingAssets/rigs/` was gitignored **and** tracked on this lane — `docs/models/sgs233-own-rig.json`
mirrored to `glider-2-33-like-own.json` — which is exactly the trap that shipped the Great Lakes with no runtime config.
In that state `git add` on a new mirror silently does nothing and only `git add -f` works, so a rebuilt or renamed
rig reaches the player only if somebody remembers the flag.

- `.gitignore`: `unity/Assets/StreamingAssets/rigs/` and `rigs.meta`.
- `git rm -r --cached unity/Assets/StreamingAssets/rigs unity/Assets/StreamingAssets/rigs.meta`. No `git add -f`
  anywhere; the files stay on disk and `ConfigSync` rewrites them on editor load, so the cockpit still animates.
- `rigs.meta` sits BESIDE the directory, not inside it, so `git rm -r --cached .../rigs` misses it. It took a second
  `git ls-files | git check-ignore --no-index --stdin -v` to notice. Mind the `--no-index`: without it `check-ignore`
  says nothing about tracked files, which is precisely the set in question.

Tracked-but-ignored on this lane is now the 62 aircraft configs and `build/cg-check.csv` only — both of which are
`lane/claude-config-mirror`'s to remove, by design. `StreamingAssetsTests` is deliberately NOT ported here: its
`NoTrackedFileIsGitignored` check would fail on those 62, and it arrives with the branch that deletes them.

**Why the rigs row lives on `config-mirror` and not here.** The destination name is not a mapping anybody should
type: a rig table declares the model it belongs to in its own `model` field, and that is what names the file the game
looks for. A fixed source→dest table would hold a second copy of something the file already says, and a rebuilt rig
that changed its model name would mirror to the old path and go on looking right. So `config-mirror`'s `Mirror` gained
a content resolver rather than a name field, and its row — `new("docs/models", "rigs", "*-rig.json", destGlob:
"*.json", nameFromContent: RigModelName)` — matches zero files on `split-surfaces` and picks this lane's rig up the
moment it merges. Verified there against this very file: it landed as `glider-2-33-like-own.json`, byte-identical,
and was swept by name when the source was removed.

**Gates.** Suite **1 failed / 719 passed / 3 skipped**. The 4-failure baseline does NOT apply to this lane: it
branched at `ed2cc7ca`, about 90 commits behind `split-surfaces`, and does not even contain
`AerobatGroundHandlingTests.cs`, so the three Pitts failures cannot occur here, and it predates the Extra tail move
that reddened the flat-spin smoke check. The one failure is
`NoseSkidTests.HardBrakingRolloutDoesNotTipOver`, which is **not recorded anywhere in this log** and cannot be mine:
this change is `.gitignore`, this log entry, and three index-only removals — no code at all, and no test on this
branch reads either file. **Ty: re-check that test when the lane is brought forward**; it may well already be fixed
by the ~90 intervening commits. Deleting the whole `rigs/` mirror and running `ConfigSync.Sync` brought
`glider-2-33-like-own.json` back byte-identical.

**Recommended merge order:** `config-mirror` → `flow-plates` → `extra-cockpit` → `sgs233-rig`. This lane is last: it branched at `ed2cc7ca` rather than `1b67dd5`, so it needs bringing forward first.

### 2026-10-29 — Claude · lane/claude-sgs233-rig · the 2-33's 30 moving parts, wired
Codex's v10 rig table now drives the model in play, read from docs/models/sgs233-own-rig.json itself (mirrored to StreamingAssets/rigs by ConfigSync) rather than copied into code. `FlyingGame.Core.ModelRig` holds the engine-free half — the piecewise map, the clamp, the run past the ends for the altimeter hands, the 360 wrap for the compass card, the trim quadrant's notches, and the two glTF-to-Unity axis conversions (a TURN and a SLIDE convert differently, because the importer's x-negation is a reflection). `AirframeRig` binds node names inside the placed model only and applies it.

Driven from the sim's ACTUAL control positions (`CurrentDeflections`), not raw input, so the surfaces, the sticks, the pedals and the handles always agree with the aeroplane being flown — and through `SetDeflections`, so a remote player's aeroplane and the widget's replay move too. Elevator and trim are negated once, in `RigInputs.FromDeflections`: the sim's elevator is positive trailing-edge down and the rig's is positive stick aft (ControlPolarityTests). Instruments: airspeed from IAS, the altimeter off MSL with both hands unbounded, a total-energy vario lagged a second, the compass off the attitude damped a quarter second (variation here is zero), sideslip straight from beta. The tow-release knob is animated in TowController — the rope letting go is an event, the knob is a 50 mm pull that springs back.

Measured in-engine (SgsRigCheck, new): 30/30 nodes bound, worst pivot 0.1 mm from the table's, every parent right, and at full travel — roll right puts the left aileron's trailing edge 93 mm down and the right's 194 mm up with the stick tops 187 mm to starboard; stick aft puts both elevator halves 158 mm up and the grips 185 mm aft; right pedal takes the rudder 264 mm to starboard with the left pedal aft and the right forward; the air brake puts the upper doors 188 mm up, the lower 146 mm down and both handles 181 mm aft. Cockpit renders confirm the dials: 45 mph on the ASI, 2,500 ft on both altimeter hands, +4 kt on the vario, and the wet compass reading 090 with 120 to the left of the lubber line and 060 to the right, which is the real reverse-sensing card.

One fault in the rig itself (NEEDS YOU): the yaw string's four axes are 21-29 deg off square with the string instead of normal to it, so it twists rather than swinging, and the wrong way. Wired as written and reported rather than patched over.

Also: the 2-33's wheel brake now comes on over the last tenth of the air-brake handle (AFM; `controls.spoiler.wheelBrakeFromSpoiler`), which the rig table had been carrying as advice. Local commit only; no push, merge or deploy.
### 2026-10-26 — Codex · lane/codex-lanes-prep-review · prep only
Read all draft lane files. Added this original log in an isolated sparse worktree; no draft was installed or edited. No configs, assets, source or tests changed. Both draft runners pass zsh -n; no live execution, push, merge, deploy or migration. Main checkout unchanged. Stop after reporting as requested.
### 2026-10-26 — Codex · codex/own-sgs233 · own 2-33 handoff
v1-v6 of the own SGS 2-33 committed locally (60e416a..4f11104); Ty approved both liveries. Handoff to Claude added above. No push, merge or deploy; main checkout returned to split-surfaces clean.
### 2026-10-29 — Codex · lane/codex-sgs233-cockpit · 2-33 cockpit + 3-view pass
Cockpit interior, linked controls and instruments added; outline re-fitted to the Commons 3-view (side/top/front overlays). Both GLBs rebuilt in Blender on the Mac. Local commit only; no push, merge or deploy; main checkout untouched.
### 2026-10-29 — Codex · lane/codex-sgs233-cockpit · 2-33 v8 photo-literal interior
Cockpit rebuilt to Ty's nine interior photos (black D panel, eyeball vent + compass, ASI/VSI/altimeter, wall air-brake slots, trim, W&B placard, striped cushions, harnesses, cross tube release, canopy/door hardware, skylight). Rear panel removed; rig 26 nodes. Both GLBs rebuilt in Blender on the Mac. Local commit only; no push, merge or deploy; main checkout untouched.
### 2026-10-29 — Codex · lane/codex-sgs233-cockpit · 2-33 v10 moving-part rig audit
Audited every requested moving part; added the yaw string as a 4-link sideslip chain (30 rig nodes); RIG doc regenerated with neutral/range/coupling; verification renders at min/neutral/max. Both GLBs rebuilt in Blender on the Mac. Local commit only; no push, merge or deploy.
