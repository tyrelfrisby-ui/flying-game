# Instrument fleet / flicker audit — t932–t934 / t938u

Source baseline: f99069137d81e35288e7aac1801008b03ba8e526. No new flight/visual approval. t921 Pitts ASI PASS retained. Cub atlas and airframes untouched.

## Source findings

- `Bridge/AirframeVisual.cs:158` calls RigInputsNow once in LateUpdate, then drives both own rig and CockpitLiveParts with the same reading. RigInputsNow calls CockpitRigState.Fill once. The old CockpitRigState comment saying twice per frame is stale; corrected as documentation only. The C172 frame guard remains. No duplicate-fill fix is warranted by this source.
- CockpitRigState.Fill reads IAS, pressure altitude, rpm and G from sim state; VSI, vario, compass and turn rate are derived/filtered. Engine oil/CHT/manifold presentation readings use engine state and lag, with no added periodic jitter found in this file. Actual input changes can still move needles.
- SharedInstruments.Update and CockpitLiveParts.Update assign needle/card transforms from calibrated values and rest transforms; no random or time-periodic term. SharedInstruments.Place disables listed old renderers, but completeness of every hide list must be checked in the imported scene before ruling out duplicate geometry.
- AirframeRig.ApplyState has 3.1/7.3 Hz sine terms only for bound `sideslip_deg` nodes (yaw string), scaled by airspeed. Other rig nodes receive no flutter. This is intentional wool motion, not evidence of instrument jitter.
- ChaseCamera.PropShake applies random head offsets only for nonzero damaged-prop vibration, cockpit view, non-Android. This is intentional damage feedback; keep it separate from intact-aircraft instrument proof.
- Instrument.shader has no time uniform or oscillation: texture, sun/sky, floor/glow and inherited alpha/depth state determine colour. Library face is at local z=0, needles proud of it (library.py). Those nominal separations do not prove imported scene depth clearance. Z-fighting, duplicate old parts, subpixel texture aliasing and fluctuating sim input remain hypotheses, not diagnoses.

## Readability coverage

f9906913 changes six shared faces: ASI, altimeter, attitude, G, heading and VSI; plus four per-aircraft ASIs (172, Extra, Great Lakes, Pitts) and three G faces (Extra, Great Lakes, Pitts). Numeral label stroke +1 px, ticks +2 px at 1024; coloured arcs half width. Text drawn by `text_at` is unchanged. Turn coordinator GLB was not changed by this commit; retains its earlier treatment.

This is not treatment of every fleet face: embedded engine gauges (tach, oil, manifold/CHT/fuel as fitted), compass and other model-specific art and CockpitDials textures are outside the 13 changed assets. Shared replacements hide some embedded faces at runtime; do not count those hidden originals as visible failures. Audit visible survivors in the imported Pitts/Great Lakes panels after captures before selecting further art edits. Cub is explicitly excluded. No blanket fleet generator run was performed.

## Offline evidence

Directory: `~/Developer/agent-lanes/renders/t932-instrument-fleet-flicker/`.
`before/`, `after-textures/`, `diff/`, 13 `*-comparison.png` triptychs, and `offline-texture-audit.json` compare f9906913^ with f9906913. JSON records image hashes, changed-pixel counts and dimensions. Six GLBs were decoded and their node/mesh/accessor structures and all accessor buffer-view bytes compared identically. Pitts ASI triptych opened: heavier numerals/ticks and thinner arcs visible. No scale/needle-map change inferred from pixels; geometry byte comparison supports unchanged geometry only. These are offline texture images, not cockpit, physical-phone or temporal evidence.

## Exact deferred capture / proof plan

1. Wait for smoke post-merge deploy on island-nav 16665d15 to release the exclusive slot (t932–t934 authority: spray FULL_SUITE finished and is in base-compare, not the holder). Do not launch independent test/build commands or hold a waiting reservation. From this lane run only `zsh tools/deploy/deploy-instrument-verification.sh`; if it exits 75, let it exit and retry later when idle.
2. Verifier runs Instrument/NoRed233 tests, then CockpitEyeShots, then deploy.sh NOINSTALL. Preserve `build/instrument-tests.log`, `build/instrument-shots.log`, `build/instrument-ios.log` with run SHA/timestamp. Claim results only from that run. Require four shots / zero failures, no C# errors and actual `** BUILD SUCCEEDED **`.
3. Exact four after fixtures (all sun, yaw 0, 2160x1000): Pitts rear pitch -15 vfov72 (`pitts-phone`); Pitts rear pitch -20 vfov40 (`pitts-closeup`); Great Lakes rear pitch -15 vfov72 (`great-lakes-phone`); Great Lakes rear pitch -20 vfov40 (`great-lakes-closeup`). Files go to `after/`. Fixtures force own models unless COCKPIT_OWN=default; record actual model-binding logs and environment. Fixed readings include 120 mph / 104 kt, 2500 ft, 2300 rpm, 70 psi, 185 F, 1 G. See CockpitEyeShots.One for complete values.
4. Open all four at native resolution and at the intended phone display size. Check numeral/tick distinction, arc weight, needle contrast, hidden/overlapping gauges and surviving engine faces. Label 40-degree shots as closeups, not normal phone FOV. No approval inferred from successful file creation.
5. Existing `before/` contains textures only. For matched cockpit before evidence, use the SAME current code, configs, camera, light and fixtures with only the 13 baseline asset blobs from f9906913^ in an isolated preserved copy, serially through the same reserved workflow; restore final assets before final tests/build. Do not use a different historical camera/build as a matched baseline, overwrite original evidence or alter Cub assets. This baseline capture is still deferred and is not supplied by the current verifier.
6. Four single images cannot prove flicker. Later collect a consecutive fixed-camera, fixed-RigInputs sequence (at least 10 s, record frame times) for both panels; log input values, resulting local needle transforms, camera matrix, visible renderers/material queues and near/far planes per frame. Then repeat intact live flight with device/manual look held steady. Classify: changing input vs transform despite constant input vs static transform/pixel-only changes. Compare whole-panel motion with needle-relative motion. Audit hidden originals, face/needle depth and material alpha only on the failing instrument. Do not disable legitimate wool/damage motion globally. Repeat shadow/dusk and a small camera-angle sweep only if needed to distinguish lighting/aliasing/depth. No speculative smoothing/depth bias until reproduction identifies the mechanism.

## Claude merge checklist

No playable merge yet. Review f9906913 plus this audit commit; finish visible untreated-face inventory and temporal proof. Run serialized verifier on final assets; inspect all images and logs. Preserve foreign packaging/backup dirt using the existing t938u recovery stash (never reset hard or force-add). Require tracked-ignore audit empty before commits. Merge local lane to island-nav and Quest same day under parity lock, with strict parity check; never bypass hooks. No push/main/upload/install authorized here. Remaining matched cockpit before capture and temporal proof need explicitly recorded evidence, not a static-shot PASS.

## Resume #2 — source/evidence only (2026-11-06)

Continued from 37846ceb. No verifier, Unity batch, Xcode or dotnet test invoked. BUILD remains deferred under the smoke reservation; no result from another lane is claimed here.

### Additional draw / shared-face / UV audit

- `SharedInstruments.Place` instantiates the library prefab, lights its materials, then clones the face material before assigning aircraft-specific FaceArt. It does not write per-aircraft art into the cached library material. This argues against that particular shared-material overwrite mechanism; it does not prove imported scenes have no duplicate faces.
- Hide logic disables all descendant renderers of each named old node. It does not remove individual triangles from a combined panel mesh. A surviving face baked into a mixed mesh is still a candidate; source inventory now records material-to-node associations, but only imported visibility/depth inspection can establish overlap.
- `InstrumentLighting.Lit` preserves texture scale/offset, skips a keyword-enabled nonzero texture rotation, copies blend state and disables depth writes for transparent materials. `Instrument.shader` uses TEXCOORD0 only. A source selecting a different texture coordinate set or transform convention needs a glTF/material comparison before changing this path. No UV defect established.
- Library geometry uses independent disc UVs (geometry.py `_uv`), not a shared fleet atlas. Its embedded sampler requests linear magnification / mipmapped linear minification (9729/9987), clamp-to-edge (33071). Pitts per-aircraft PNG importer instead requests mipmaps, bilinear filtering, anisotropy 1 and repeat wrap. Those differing paths are worth recording during a small camera-angle sweep, not grounds to change filtering now. Cub atlas untouched.
- Transparent compass-case overlays retain alpha blending/ZWrite off; their ordering against cards/glass is a plausible angle-dependent candidate. Instrument shader is two-sided. Record actual queues, alpha modes, facing/depth and near/far planes in the failing imported scene. No speculative depth bias or smoothing added.

### Remaining source inventory (not a visible-survivor verdict)

New `embedded-face-source-inventory.json` records 118 candidate face/marking/compass materials across 17 non-Cub GLBs, including variants and hidden originals; it deliberately is a broad name-based inventory, not an exhaustive semantic gauge detector. Plain-material geometry can contain markings too (172 is primarily `172_markings`).

- Pitts: rear/front copied tachometer, oil pressure, oil temperature, compass art/scale, and mixed `Cockpit_gauge_face` originals remain outside the changed shared faces. Inspect both seats and all selected liveries before counting visible survivors.
- Great Lakes: TACH, MP, combined OIL, front OILPRESS, CHT, HOBBS, MAG, front/rear compass cards and front case overlay remain untreated by f9906913. COM/XPDR and sequence card are separately drawn controls/placards, not implied engine-gauge work. Legacy livery GLBs still list mixed `Cockpit_gauge_face`; actual model selection matters.
- Extra (all three own variants): Tach, MP, Temps, Amps, ThreeInOne and FuelComputer embedded materials; CockpitDials fuel/oil/MP/tach and live compass card also remain outside shared treatment. ASI/Alt/TC/G originals appear in source but can be hidden by replacement.
- 172: inspect engine/auxiliary markings within the shared `172_markings` material rather than assuming each face has a separate texture.
- SGS 2-33 and Grob 103: dedicated alt/ASI/vario/compass artwork remains outside f9906913; shared replacements may hide originals. Stearman generic gauge art also appears in the source scan. Other procedural/unmatched material names need imported fleet inspection; this list is not a claim of full visible-fleet coverage.
- `untreated-art-source-comparison.json`: all 28 selected non-Cub dial/compass source PNGs are byte-identical to f9906913 parent. This establishes treatment coverage only, not poor readability. No art regeneration performed.

### Review pack and capture additions

`INDEX.md` links all 13 labelled LEFT-before / CENTRE-after / RIGHT-difference triptychs plus the pixel/geometry audit and new source inventories. Opened Great Lakes ASI comparison this resume: thicker ticks/numerals and slimmer arcs visible; no cockpit readability approval inferred. Existing before/after filenames are retained to avoid breaking review links.

For each four-shot fixture above, preserve native 2160x1000 PNG and additionally inspect at 1080x500 (50% display) and 720x333 (one-third display); record display scale, not a claim of physical-phone capture. Before/after must match camera, lighting, model-binding and fixture values. Confirm Pitts rear plus Great Lakes rear engine survivors in normal 72-degree shots; use 40-degree closeups to localize issues only. Physical phone later: same aircraft/livery, rear seat, normal cockpit view, intact aircraft, look inputs held steady, record device/resolution/build and at least ten seconds of panel video plus a still. Render fixtures cannot substitute for device flicker evidence.

Merge checklist remains blocked on this lane's serialized verifier, matched cockpit baseline, inspection and temporal reproduction. Claude merges after BUILD and same-day Quest parity. Preserve foreign packaging and Cub backup files: fresh recovery stash `t932 resume2 preserve foreign packaging and Cub backup dirt` was created and reapplied; stash retained. No packaging or backup asset staged.

## BUILD NOW attempt — 2026-11-06 12:42 PST

At source a6f8f286 the authorized verifier exited 75, then after 20 seconds the single retry also exited 75. PID 99851 held a dotnet test reservation; left untouched. Neither attempt reached tests, Unity, captures or Xcode. BUILD NOT RUN; no new stamp, test PASS or flicker evidence. See render INDEX.md and verification-attempts-20261106-1242.md for exact observation and remaining proof. No further waiting/retries; no playable merge.

## Resume #3 — verification gate and baseline assets

At 7ecb459e, verifier exited 75 twice before any work. Spray 13759/14464 and Q007 25389/25393 remained active and untouched. Fixed the verifier matcher to include absolute dotnet paths and dotnet exec testhost processes; shell syntax passes and actual matcher finds all four. No .NET/Unity/Xcode result claimed.

Prepared all 13 f9906913-parent asset blobs under external render directory baseline-assets/ with baseline-assets-manifest.json SHA256/byte lengths. No Cub, config or working artwork changed. Use this asset-only baseline in an isolated copy with CURRENT camera/config/runtime and the exclusive reservation. These files are not captures. See verification-resume3.md and INDEX.md. Remaining gates and untouched visible-fleet work above still apply.
