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

1. Wait for spray's full reservation, tests, Unity and Xcode to finish. Do not launch independent test/build commands or hold a waiting reservation. From this lane run only `zsh tools/deploy/deploy-instrument-verification.sh`; if it exits 75, let it exit and retry later when idle.
2. Verifier runs Instrument/NoRed233 tests, then CockpitEyeShots, then deploy.sh NOINSTALL. Preserve `build/instrument-tests.log`, `build/instrument-shots.log`, `build/instrument-ios.log` with run SHA/timestamp. Claim results only from that run. Require four shots / zero failures, no C# errors and actual `** BUILD SUCCEEDED **`.
3. Exact four after fixtures (all sun, yaw 0, 2160x1000): Pitts rear pitch -15 vfov72 (`pitts-phone`); Pitts rear pitch -20 vfov40 (`pitts-closeup`); Great Lakes rear pitch -15 vfov72 (`great-lakes-phone`); Great Lakes rear pitch -20 vfov40 (`great-lakes-closeup`). Files go to `after/`. Fixtures force own models unless COCKPIT_OWN=default; record actual model-binding logs and environment. Fixed readings include 120 mph / 104 kt, 2500 ft, 2300 rpm, 70 psi, 185 F, 1 G. See CockpitEyeShots.One for complete values.
4. Open all four at native resolution and at the intended phone display size. Check numeral/tick distinction, arc weight, needle contrast, hidden/overlapping gauges and surviving engine faces. Label 40-degree shots as closeups, not normal phone FOV. No approval inferred from successful file creation.
5. Existing `before/` contains textures only. For matched cockpit before evidence, use the SAME current code, configs, camera, light and fixtures with only the 13 baseline asset blobs from f9906913^ in an isolated preserved copy, serially through the same reserved workflow; restore final assets before final tests/build. Do not use a different historical camera/build as a matched baseline, overwrite original evidence or alter Cub assets. This baseline capture is still deferred and is not supplied by the current verifier.
6. Four single images cannot prove flicker. Later collect a consecutive fixed-camera, fixed-RigInputs sequence (at least 10 s, record frame times) for both panels; log input values, resulting local needle transforms, camera matrix, visible renderers/material queues and near/far planes per frame. Then repeat intact live flight with device/manual look held steady. Classify: changing input vs transform despite constant input vs static transform/pixel-only changes. Compare whole-panel motion with needle-relative motion. Audit hidden originals, face/needle depth and material alpha only on the failing instrument. Do not disable legitimate wool/damage motion globally. Repeat shadow/dusk and a small camera-angle sweep only if needed to distinguish lighting/aliasing/depth. No speculative smoothing/depth bias until reproduction identifies the mechanism.

## Claude merge checklist

No playable merge yet. Review f9906913 plus this audit commit; finish visible untreated-face inventory and temporal proof. Run serialized verifier on final assets; inspect all images and logs. Preserve foreign packaging/backup dirt using the existing t938u recovery stash (never reset hard or force-add). Require tracked-ignore audit empty before commits. Merge local lane to island-nav and Quest same day under parity lock, with strict parity check; never bypass hooks. No push/main/upload/install authorized here. Remaining matched cockpit before capture and temporal proof need explicitly recorded evidence, not a static-shot PASS.
