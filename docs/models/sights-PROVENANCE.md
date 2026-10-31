# Aerobatic sights — round 2, 2026-10-31

This round supersedes the round-1 bent supports in `1c038c0`. All eight livery GLBs use the same spec-driven geometry as the Blender builders: `sequence_card.blender_add` and `native_add` already call `sights.py`; no duplicate sight implementation is left in the builders. CPU updates start from immutable `38e8746` buffers, preserving the original airframe/front panels/paint and round-1 lowered Pitts card. No Unity, Blender, dotnet or deploy script was run.

**Great Lakes remains unresolved:** E=(-1.970,0,-0.264) is unchanged and all 606 full-scene sight rays remain blocked by the opaque fuselage. Full-scene views retain and label the obstruction; separate frame-isolation diagnostics only demonstrate angles. No eye, gate or approval status changed.

## Reference and axes

Opened Ty's `~/Developer/agent-lanes/prompts/sights-refs/ref1.png`, `ref2.png`, and `ref3.png`. Extra: two angled wingtip tubes converging at the frame. Pitts: I-strut carrier plus a lower diagonal brace; **the sight brace was absent in round 1 and is newly added**, independently of the existing flying/landing wires and aileron slave strut. Great Lakes uses two points on its N-strut rear post.

Body metres: +X forward, +Y right, +Z down, origin aircraft root/CG. L mirrors the table's +Y values to -Y. Raw GLB = (-builder X, builder Z, builder Y). Current measured importer mapping remains body=(-s*gltf.x+tx, -s*gltf.z, -s*gltf.y+tz):

| Type | s | tx m | tz m |
|---|---:|---:|---:|
| Extra | 0.990387630570 | 0.009618783452 | 0.002452081110 |
| Pitts | 0.999032710972 | -0.012249326602 | 0.000285530929 |
| Great Lakes | 0.982968201148 | -0.033458951018 | -0.197892647325 |

Eye source remains `git show aab015b:docs/models/eyepoints.json`, `entries[rear].v4.position_body`. Source Great Lakes tz=+0.02528 differs from current tz=-0.197892647, explaining the historical eye conflict; it has not been substituted.

## Mounts and frame positions

All support tubes are **12 mm diameter**; bars remain 8 mm diameter and 500 mm long. Each tube starts at a **26 mm diameter socket** (8 mm fitting engagement / 22 mm projection from the measured surface) and converges at a metal frame hub. Pitts/Great Lakes have **24 mm wide, 3 mm wall collars** following the actual strut sections, with only 0.25 mm fitting engagement. Extra has **40 mm diameter × 4 mm thick plates**, seated 1–5 mm above the wing surface, joined by the fastening spigots. Only the fittings engage structure; the tubes do not enter a skin.

The common hub sits outboard/behind the bars from the eye: Extra 12 mm, Pitts/Great Lakes 25 mm. The two anchors and common hub form a triangle using the intervening rigid wing/strut as its base. Tube ends terminate in fittings at both ends, without a floating elbow. Main/brace tube axes below are socket → hub; structural surface witnesses differ from socket positions by the 22 mm projection.

| Type / fitting | Surface witness, R body m | Tube start, R body m | Tube end / hub, R body m | Length m |
|---|---|---|---|---:|
| Extra / Front | (0.180000000, 3.936790832, -0.098426507) | (0.180000000, 3.936790832, -0.120426507) | (-1.597000000, 3.948790832, -0.518000000) | 1.820972 |
| Extra / Rear | (-0.080000000, 3.936790832, -0.116865444) | (-0.080000000, 3.936790832, -0.138865444) | (-1.597000000, 3.948790832, -0.518000000) | 1.563706 |
| Pitts / Main | (-0.080000000, 2.235266380, -0.300000000) | (-0.080000000, 2.213266380, -0.300000000) | (-1.325000000, 1.875000000, -0.300000000) | 1.290135 |
| Pitts / Brace | (-0.300000000, 2.187765536, 0.150000000) | (-0.300000000, 2.165765536, 0.150000000) | (-1.325000000, 1.875000000, -0.300000000) | 1.156577 |
| Great Lakes / Main | (-1.200000000, 2.815991540, -0.480000000) | (-1.200000000, 2.793991540, -0.480000000) | (-1.970000000, 2.475000000, -0.264000000) | 0.860995 |
| Great Lakes / Brace | (-1.300000000, 2.817267179, 0.050000000) | (-1.300000000, 2.795267179, 0.050000000) | (-1.970000000, 2.475000000, -0.264000000) | 0.806267 |

Pitts: the main carrier runs aft/inboard from the I-strut waist; the added brace connects a separate lower I-strut collar (z=+0.150) to the hub (z=-0.300). Great Lakes: upper/lower rear N-post collars are fixed structural stations, not intersections recomputed at the eye height. `fit_sight_mounts.py` raycasts the named structure and bakes collar radii/witnesses into the spec; `segments(row)` always recomputes bars, hub and tube vectors from E and |y|. A temporary +100 mm X / -100 mm Z Great Lakes eye perturbation confirmed unchanged anchor/collar geometry and correspondingly moving frame/tube endpoints; it was not saved.

| Type | Old → new frame y, body m | Frame C, R body m |
|---|---|---|
| extra-300-like | ±3.936790832 → ±3.936790832 (0 mm) | (-1.597, 3.936790832, -0.518) |
| pitts-s2b-like | ±1.850000000 → ±1.850000000 (0 mm) | (-1.325, 1.850000000, -0.300) |
| great-lakes-like | ±2.450000000 → ±2.450000000 (0 mm) | (-1.970, 2.450000000, -0.264) |

No frame move was needed. C remains x=E.x, z=E.z and the frame plane is perpendicular to Y. Moving only |y| cannot remove the fore/aft reach between the eye station and the structure; moving the frame forward to the strut would break the required vertical-bar plane x=E.x. No alternative violating that requirement was adopted. Current Place span/chord/gear transforms are remeasured from every final GLB and remain invariant.

## Checks on actual exported geometry

`check_sights.py` passes all eight GLBs. It checks the actual cylinder/collar vertices against the spec, actual bar cap axes/centres, diameters/lengths, exact structure-surface witnesses on both sides, static capsule clearance, collars against all other structure using enclosing spheres, full aileron travel including sockets/collars, and the entire rotating-prop slab. Only anchor fittings are exempt from collision with their explicitly named attachment structure; tubes/bars and hub have no airframe exemptions.

The former single, full-travel aileron dilation was too loose for an angled tube near a wingtip. Its replacement is still a conservative **continuous** enclosure: rotations covering [-maxDefl,+maxDefl] at intervals ≤2°, each triangle dilated by `2*r_max*sin(interval/4) + 2*0.100*sin(maxDefl/2)`. Every point in a triangle and every angle between samples is covered; the full 100 mm pivot-position uncertainty remains. This is a tighter proof, with no angular sampling gaps or clearance waiver. Limits: Extra ±0.5236, Pitts ±0.4363, Great Lakes ±0.4014 rad.

Minimum certified static gaps: Extra 8.647 mm, Pitts 15.900 mm, Great Lakes 15.614 mm. Extra's closest continuous aileron gap bound is 15.985 mm; the biplane mounts lie outside all interval-dilated aileron AABBs. All components lie at least 1.525 m aft of their propeller swept slabs. Tubes/fittings block zero tested bar rays outside the central 20 mm mechanical hub join (560 tests per Extra/Pitts, 556 for Great Lakes due to float32 threshold rounding). Opaque-airframe visibility remains Extra/Pitts 606/606, Great Lakes 0/606. Numerical angles do not approve visibility.

The actual pinhole camera at E looks along -Y on L, +Y on R, up=-Z. Plus45 measures -45° on L, +45° on R; Minus45 is the reverse. Absolute exported-mesh angle errors in degrees (identical across each type's liveries):

| Type | Side | Vertical | Plus45 | Minus45 | Max centre offset |
|---|---|---:|---:|---:|---:|
| extra-300-like | L | 0.000000000 | 0.000000448 | 0.000000448 | 0.000000370 |
| extra-300-like | R | 0.000000000 | 0.000000448 | 0.000000448 | 0.000000370 |
| pitts-s2b-like | L | 0.000000000 | 0.000003581 | 0.000003581 | 0.000000308 |
| pitts-s2b-like | R | 0.000000000 | 0.000003581 | 0.000003581 | 0.000000308 |
| great-lakes-like | L | 0.000000000 | 0.000001113 | 0.000001113 | 0.000000308 |
| great-lakes-like | R | 0.000000000 | 0.000001113 | 0.000001113 | 0.000000308 |

`check_iac_assets.py` passes 9/9 GLBs: card, flush offsets, panel/instrument clearances, 99 visibility rays per aircraft, original front-panel/airframe/paint preservation. Pitts's lowered card also retains 99/99 rays from this round's exact sight eye.

## Ignore rules and generated Great Lakes mirror

Round 1 changed `build/` to `build/*/` and removed `unity/Assets/StreamingAssets/aircraft/`, because both original rules matched already tracked files under the mandatory `--no-index` audit. This round keeps that necessary narrowing and adds only `build/*.log` plus the exact `unity/Assets/StreamingAssets/aircraft/great-lakes-like.json.meta` rule. All current deploy scripts produce directories and `*.log` files under build; both remain ignored. Existing folder metadata rule remains.

Broad directory ignores with precise `!` exceptions were evaluated: `git check-ignore --no-index --stdin -v` prints the matching **negative** rules too, so they cannot meet the operator's exact silent-command requirement. The narrowly scoped output rules avoid matching any tracked file and require no forced add. Future new output formats should receive an explicit rule rather than masking versioned inputs.

The Great Lakes JSON mirror is now tracked, like the other aircraft JSON mirrors: byte-identical to `configs/aircraft/great-lakes-like.json`, SHA-256 `4f02649c2d9f61ae3d1d4880ad311a49de498b71378f110b7e0d5128ae828ae9`. Its newly generated `.meta` remains ignored/unmodified; existing tracked `.meta` files retain their GUIDs and are unmatched by ignore rules. `git status --short --ignored` verifies `!! build/ios.log` and the Great Lakes `.meta`; `git check-ignore -v build/ios.log` resolves to `build/*.log`. The ProjectSettings bundle-version bump and ios.log are left byte-for-byte as found and are excluded from this commit.

## Reproduce and operator commands

CPU Python with NumPy/Pillow (this machine: `~/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/bin/python3`):

```sh
python3 tools/own_models/fit_sight_mounts.py
python3 tools/own_models/sights.py
python3 tools/own_models/check_sights.py --report docs/pending/sights-renders/v2/check-sights.json
python3 tools/own_models/check_iac_assets.py
python3 tools/own_models/render_sights.py --out docs/pending/sights-renders/v2
# Separate visibility gate remains red for Great Lakes; no waiver:
python3 tools/own_models/check_sights.py --require-visible
# Must print nothing before every commit:
git ls-files | git check-ignore --no-index --stdin -v
```

Full Blender rebuild, **operator outside the sandbox**, from this worktree:

```sh
for liv in N31DG N1191 CHECKER-UNMARKED FACTORY-UNMARKED; do
  PITTS_LIVERY="$liv" blender -b --python tools/own_models/build_pitts_s2b.py
done
for liv in tf n300uy unmarked; do
  EA_LIVERY="$liv" blender -b --python tools/own_models/build_ea300.py
done
GL_LIVERY=v1 blender -b --python tools/own_models/build_great_lakes.py
```

Blender uses the same baked body↔GLB mapping and spec/meshes; verify current Place invariance after a full rebuild. If base structure changes, rerun `fit_sight_mounts.py`, then rebuild sights. Fresh Blender exports can reorder original buffers, so `check_iac_assets.py`'s immutable-buffer contract applies to native incremental exports. Import/visual/build review in Unity remains an operator task. No gate approved.

## Renders

All under `docs/pending/sights-renders/v2/` (round-1 evidence retained): for each prefix `extra`, `pitts`, `great-lakes`, files `*-exterior-34.png`, `*-mount-closeup.png`, `*-fittings-detail.png`, `*-rear-eye-L.png`, `*-rear-eye-R.png`. Rear-eye views print measured angles/errors and dotted true vertical/±45° overlays. Great Lakes additionally has `great-lakes-rear-eye-{L,R}-frame-isolated.png`, explicitly labelled geometry-only. Pitts panel views are retained as supplementary evidence. `check-sights.json` contains each livery's mesh measurements; `render-manifest.json` records cameras and model hashes. Full-scene mount/fitting views retain wings, struts, ailerons and wires; CPU renders omit transparent glass.
