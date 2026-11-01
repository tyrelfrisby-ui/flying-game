# Floating aerobatic sights — round 3, 2026-10-31

## NEEDS YOU

- Run the external Blender rebuilds and Unity visibility check below, then inspect on device. Neither Blender nor Unity/dotnet/xcodebuild was run in this sandbox. The new EditMode command exercises imported renderers and the actual camera callback delegates without a graphics device; it is not a pixel-render test. No gate approved. Ty's Pitts eye acceptance remains feedback only.
- **Camera mismatch, reported only:** the game uses the hidden procedural `Canopy` child, not the specified rear eye. `BuildCanopy` positions it at U(x,0,z+BodyAxisZ); `AirframeBuilder.Build` subtracts config `mass.cg`; `PilotEgress.CockpitLocal` adds Unity up×0.15; `ChaseCamera.FixedView` adds up×0.10. Body +Z is down. Values below are source-derived, pending runtime confirmation by the editor check's logged coordinates. Camera positions, eyepoints and configs are unchanged.

| Type | Actual cockpit camera (body m) | Camera minus rear E (body m) | Centre off lateral axis ° (L/R) |
|---|---|---|---:|
| Extra | (-0.280, +0.000, -0.900) | (+1.317, +0.000, -0.382) | 19.204490 |
| Pitts | (-0.645, +0.000, -0.900) | (+0.680, +0.000, -0.600) | 26.113912 |
| Great Lakes | (-0.744, +0.000, -0.950) | (+1.226, +0.000, -0.686) | 29.830740 |

**Actual default cockpit view:** the camera looks forward with 4° down pitch, 72° vertical FOV. Every endpoint of all eight sight bars is behind that camera's forward plane for all three types. Consequently no bar has a visible projected angle/error in that default view. `check-sights.json` records each endpoint depth range and null visible-angle errors. A view must look sideways/aft to see the frames; adding such a view or moving the camera is outside this change.

To quantify the position mismatch independently of the current forward orientation, aim a diagnostic camera from the actual cockpit position directly at C, using body up=-Z (zero roll). Absolute errors relative to the ideal rear-eye line angles are below, identical on L/R to the shown precision. These are **hypothetical re-aimed views**, not current game-camera renders:

| Type | Level error ° | Vertical error ° | Plus45 error ° | Minus45 error ° |
|---|---:|---:|---:|---:|
| Extra | 1.755838 | 0.000000 | 2.220893 | 0.549921 |
| Pitts | 6.109797 | 0.000000 | 3.398738 | 2.629152 |
| Great Lakes | 6.930246 | 0.000000 | 5.341361 | 1.074512 |

Looking along fixed body ±Y instead leaves each plane parallel to the image: line-angle errors stay at rear-eye float32 precision, but the centre misses the optical axis by the angles in the camera table. Thus camera offset alone does not define a unique bar-angle error without a look direction/roll convention. Both diagnostics are in the JSON.

- **Great Lakes eye still occluded:** E=(-1.970,0,-0.264) remains inside opaque fuselage under this lane's current importer transform. All 808/808 four-bar rays are blocked; the historical three-bar subset is still 606/606 blocked. The full-scene images retain the obstruction; frame-isolated images are diagnostics. No eye/cockpit correction or approval added.

## Geometry and provenance

This replaces all round-1/2 sight mounts on top of `ae1e0d8`, per Ty: “No attaching arms just a floating level vertical and 45 each direction with the exact center of the intersecting lines on the eye line”. Each LEFT/RIGHT frame has exactly four intersecting red cylinders (0.500 m × 8 mm), with axes body X (level), body Z (vertical), and (±1,0,1)/√2. No hub, support, fitting, tube, brace, socket, plate or collar; no centre marker/end treatment added. Existing airframe struts/wires and panel-card hardware remain aircraft geometry.

Spec C=(E.x,±y_frame,E.z): Extra E=(-1.597,0,-0.518), y=3.936790831515333; Pitts E=(-1.325,0,-0.300), y=1.850; Great Lakes E=(-1.970,0,-0.264), y=2.450. `sights-spec.json` retains the measured GLB→body placement transforms. `docs/models/eyepoints.json` is absent at this HEAD; the checker verifies the retained values against `aab015b:docs/models/eyepoints.json`, entries[rear].v4.position_body. No eyepoint status changed.

`sights.py` supplies both native and Blender meshes through the existing `sequence_card.py` calls in all three builders. No duplicate geometry needed changing in the builders. Deleted `fit_sight_mounts.py` and all mount fields/emitters. Offline regeneration starts from immutable `38e8746` (before sights), so obsolete buffers/meshes are absent. All eight GLBs were regenerated; rerunning each builder's `--offline-update` produced identical SHA-256 hashes. All 11 PNGs and the render manifest are also byte-identical on regeneration.

## Mesh checks and renders

`check_sights.py` measures actual exported float32 cap centres and cylinder axes. It rejects missing/extra/dormant sight nodes or meshes, checks the eight meshes against the four-bar spec and rejects the old clamp material. Four-axis least-squares intersection, cap-midpoint coincidence and eye-lateral-line residuals must each be ≤1e-6 m. Maximum measured residual across those checks: 4.943486576e-08 m. Current Place invariance, static structure clearance, continuous full-aileron-travel enclosure, prop swept slab and lowered Pitts-card visibility checks remain. `check_iac_assets.py` passes all 9/9 assets, preserving airframe/front panels/paint and card fit.

Pinhole angle convention: undirected angle from image horizontal, up=-body Z. Level=0°, vertical=90°, Plus45=L:-45°/R:+45°, Minus45=L:+45°/R:-45°. Absolute errors (all liveries agree):

| Type | Side | Level error ° | Vertical error ° | Plus45 error ° | Minus45 error ° |
|---|---|---:|---:|---:|---:|
| Extra | L | 0.000000000 | 0.000000000 | 0.000000448 | 0.000000448 |
| Extra | R | 0.000000000 | 0.000000000 | 0.000000448 | 0.000000448 |
| Pitts | L | 0.000000000 | 0.000000000 | 0.000003581 | 0.000003581 |
| Pitts | R | 0.000000000 | 0.000000000 | 0.000003581 | 0.000003581 |
| Great Lakes | L | 0.000000000 | 0.000000000 | 0.000001113 | 0.000001113 |
| Great Lakes | R | 0.000000000 | 0.000000000 | 0.000001113 | 0.000001113 |

Pitts and Extra have 808/808 clear opaque-airframe rays. Great Lakes is flagged above; `--require-visible` deliberately fails. The known Great Lakes blockage is not waived.

Committed `docs/pending/sights-renders/v3/`: 6 `*-cockpit-rear-eye-{L,R}.png`, 3 `*-exterior-34.png`, 2 Great Lakes frame-isolated variants, `check-sights.json`, `render-manifest.json`. Dotted true level/vertical/±45° references and measured errors are printed on eye views. Exterior rendering passes `include=lambda n:not n.startswith('Sight_')` before rasterization; the manifest lists the eight excluded nodes for each exterior view. Transparent glass is omitted by the CPU renderer. These are asset geometry evidence, not Unity lighting/UI screenshots. Older evidence is historical only.

## Runtime visibility and audited camera paths

`AirframeModels.Place` immediately installs `CockpitSights` on every model with `Sight_` nodes. It caches the renderers once, disables them at spawn and disables their shadows. Only `AirframeVisual.Rebuild` binds a local driver. The guard brackets Built-in Pipeline `Camera.onPreCull` / `onPostRender`: enable only for Camera.main with an active matching ChaseCamera, matching local driver/target, Cockpit view, no side lesson, no override/egress, and neither replay flag. A camera stack hides for nested renders and restores the outer camera; the final post-render returns to OFF. Disable/unbind/rebuild fail closed. The decision is recomputed per render, so a stale static `InCockpit` cannot leak sights.

Source paths checked:

- `ChaseCamera`: every enum value (RelativeWind, Cockpit, Tail, SideLeft, SideRight, Top, Bottom, Front, Flyby, RelativeWindAhead, Locked), SideView lessons, pilot OverrideTarget, null target and disabled camera controller.
- `FlightReplay`: SessionSettings.ReplayActive and driver.Replaying both deny sights, even with Cockpit selected.
- `WaterReflection.OnPreCull`: nested `_refl.CopyFrom(_main)` + `_refl.Render()` is denied and outer state restored.
- `ClipRecorder.LateUpdate`: `_clip.CopyFrom(_main)` + manual Render is denied, including cockpit recordings.
- `Practice/LessonIllustrator`: both manual LessonCamera render paths are denied, including renders of the live local aircraft.
- `Widget/AeroWidget`: WidgetCamera and review camera, WidgetTow and other builder-only models remain unbound.
- `Net/RemoteAircraft`: builder-only model stays unbound; its Show toggle explicitly excludes sight renderers.
- Combat/drone/tow builder-only models and editor ModelRender/AirframeRender/AircraftRender/SkinRender stay unbound; any other camera without the exact local cockpit identity is denied.
- `GroundShadow` explicitly skips sight meshes, which also never join control-skin carving, damage splitting or wing debris lists.

`SightsVisibilityCheck.Run` checks actual imported bars for all 8 liveries twice (including rebuilds), spawn OFF, every View, replay/side/egress/null/disabled states, unbound remote model, CopyFrom secondary camera, nested reflection restoration, final OFF, guard disable/re-enable and unbind. It logs the actual cockpit body position. This check is committed but cannot be run in this sandbox. Device rendering still needs operator review.

## Operator commands

Run from this worktree. CPU environment on this machine is `~/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/bin/python3` (NumPy/Pillow); use that interpreter if system Python lacks them.

```sh
python3 tools/own_models/sights.py
python3 tools/own_models/check_sights.py --report docs/pending/sights-renders/v3/check-sights.json
python3 tools/own_models/check_iac_assets.py
python3 tools/own_models/render_sights.py --out docs/pending/sights-renders/v3
# Expected to fail on the unchanged Great Lakes eye:
python3 tools/own_models/check_sights.py --require-visible
```

Full Blender builds, outside the sandbox:

```sh
for liv in N31DG N1191 CHECKER-UNMARKED FACTORY-UNMARKED; do
  PITTS_LIVERY="$liv" blender -b --python tools/own_models/build_pitts_s2b.py
done
for liv in tf n300uy unmarked; do
  EA_LIVERY="$liv" blender -b --python tools/own_models/build_ea300.py
done
GL_LIVERY=v1 blender -b --python tools/own_models/build_great_lakes.py
```

Re-run mesh checks after Blender to confirm Place invariance. Full Blender export may reorder original buffers: `check_iac_assets.py`'s immutable-buffer contract is specific to native offline exports, so run it on this committed/native set before full Blender rebuilds. No fresh Blender export was claimed validated.

Unity EditMode command (requires this project's imported assets; no graphics device needed), then existing .NET model tests:

```sh
/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -nographics -projectPath "$PWD/unity" \
  -executeMethod FlyingGame.EditorTools.SightsVisibilityCheck.Run \
  -logFile /tmp/flying-game-sights-v3-unity.log
dotnet test tools/FlightTests/FlightTests.csproj --filter FullyQualifiedName~OwnPittsModelTests
```

Then inspect/import/build in Unity and on device, especially nested water reflection and view transitions. No Unity, Blender, dotnet, xcodebuild or deploy command ran here. `src/` and `configs/` are untouched. ProjectSettings bundleVersion deploy leftovers and build/ios.log are preserved and excluded from the local commit. Before every commit: `git ls-files | git check-ignore --no-index --stdin -v` must print nothing. No forced add, push or merge.
