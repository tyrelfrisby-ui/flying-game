# Aerobatic sights — 2026-10-31

All eight own-livery GLBs contain spec-generated red rod sights on both sides. The shared helper `tools/own_models/sights.py` is called from `sequence_card.blender_add` / `native_add`, which all three builders already invoke. Dimensions, eye source and measured mount/support positions live in [sights-spec.json](sights-spec.json). No eyepoint statuses or application/config files change.

**Unresolved Great Lakes visibility:** the exact requested eye is inside opaque fuselage geometry under the current importer placement. All 606 sight visibility rays are blocked. Both full-scene eye renders retain and label this obstruction; separately labelled frame-isolation renders show the geometric angles only. The eye was not raised, the cockpit was not cut away in the full-scene renders, and no historical transform was silently substituted. Ty/operator resolution is needed before this type can satisfy the visibility requirement.

## Reference and coordinate mapping

Read-only source: `git show aab015b:docs/models/eyepoints.json`. The requested coordinates are confirmed in `entries[rear].v4.position_body` (the outer historic v3 `position_body` fields differ). Extra (-1.597,0,-0.518); Pitts (-1.325,0,-0.300); Great Lakes (-1.970,0,-0.264). Ty accepts the Pitts point as the sight reference; no gate or eyepoint status was edited.

Body metres are +X forward, +Y right, +Z down relative to aircraft root/CG. Raw GLB is nose -X, up +Y, left +Z. Builder is forward +X, left +Y, up +Z; builder→GLB = (-x,z,y). For raw GLB g, **body = (-s*g.x+tx, -s*g.z, -s*g.y+tz)**:

| Type | s | tx (m) | tz (m) | Eye in raw GLB (m) |
|---|---:|---:|---:|---|
| extra-300-like | 0.990387630570 | 0.009618783452 | 0.002452081110 | (1.622212085, 0.525503414, 0) |
| pitts-s2b-like | 0.999032710972 | -0.012249326602 | 0.000285530929 | (1.314021712, 0.300576275, 0) |
| great-lakes-like | 0.982968201148 | -0.033458951018 | -0.197892647325 | (1.970095316, 0.067252789, 0) |

Transforms reproduce current `AirframeModels.Place` readable-vertex span fit (including Pitts scale trim), outer-wing chord midpoint alignment, lateral centring and lowest-wheel alignment. The checker recomputes them from each final GLB and asserts no placement drift. New frames lie outside the importer’s 65–92% half-span sampling band; their bounds remain inside the original aircraft span. The biplane supports attach to their struts then step inboard before reaching aft.

The source commit’s Great Lakes transform is stale relative to this lane’s current geometry: tx=-0.0439, tz=+0.02528, versus the values above. It maps the requested eye to raw (1.95948,0.29429,0), about 227 mm above the actual-current mapped eye. This explains the conflict. Extra/Pitts source translation differences are only source rounding. Source transforms are retained separately in the spec for audit.

## Dimensions and attachments

Each of the three bars is 0.500 m end-to-end; vertical height 0.500 m, diagonal width/height 0.353553391 m. Rod diameter 8 mm, support diameter 10 mm; 18 mm diameter × 20 mm long attachment fitting (8 mm embedded / 12 mm projecting). Red RGB (200,24,36), original geometry inspired by Ty’s ref1.png (Extra wingtip) and ref2/ref3.png (Pitts I-strut). Photos were opened and inspected; no pixels or mesh were copied.

L = negative body Y, R = positive body Y. Coordinates below are mirrored; JSON retains full precision. Attachment is the surface fitting, C is the three-bar intersection. The aft-reaching support is needed because each eye station is behind its structure.

| Type | Attachment L/R, body m | C L/R, body m | Structure |
|---|---|---|---|
| extra-300-like | (-0.039900598, ±3.936790832, -0.119150832) | (-1.597, ±3.936790832, -0.518) | Wing |
| pitts-s2b-like | (-0.082181616, ±2.235188945, -0.300000000) | (-1.325, ±1.850000000, -0.300) | Strut_Interplane |
| great-lakes-like | (-1.242509838, ±2.816103577, -0.264000000) | (-1.970, ±2.450000000, -0.264) | Strut_Interplane |

Nominal attachment coordinates come from triangle/surface ray intersections. Mirrored fittings overlap both structure surfaces; the triangulated Pitts waist’s mirrored surface differs by 0.0066 mm, below the 0.010 mm surface tolerance and far inside the 8 mm fitting engagement. All non-fitting rod/support capsules clear the opaque static airframe by at least 6.93 mm. Intended rod/rod joins at C and support elbows overlap.

Aileron travel is read from current configs: Extra ±0.5236 rad, Pitts ±0.4363 rad, Great Lakes ±0.4014 rad. Each actual aileron triangle is dilated by a continuous full-travel displacement bound `2*(max vertex distance to hinge axis + 0.100 m pivot uncertainty)*sin(maxDeflection/2)`. All rod/support capsules lie outside those conservative swept triangle AABBs. This checks every angle in the interval, without gaps from angular sampling. Hinge witnesses, uncertainty and travel are recorded in the spec; Unity rig/import behavior remains an operator check.

## Actual exported-mesh pinhole checks

Each actual cylinder’s cap centres are recovered from its float32 vertices and transformed to body metres. The pinhole camera at E looks along body -Y on L / +Y on R, with true up -Z. Camera right is +X on L / -X on R. Plus45 therefore appears -45° on L and +45° on R; Minus45 is the reverse. C must project at the optical centre. Errors below are absolute degrees:

| Type | Side | Vertical error | Plus45 error | Minus45 error | Maximum C offset |
|---|---|---:|---:|---:|---:|
| extra-300-like | L | 0.000000000 | 0.000000448 | 0.000000448 | 0.000000370 |
| extra-300-like | R | 0.000000000 | 0.000000448 | 0.000000448 | 0.000000370 |
| pitts-s2b-like | L | 0.000000000 | 0.000003581 | 0.000003581 | 0.000000308 |
| pitts-s2b-like | R | 0.000000000 | 0.000003581 | 0.000003581 | 0.000000308 |
| great-lakes-like | L | 0.000000000 | 0.000001113 | 0.000001113 | 0.000000308 |
| great-lakes-like | R | 0.000000000 | 0.000001113 | 0.000001113 | 0.000000308 |

Results are identical across each type’s liveries to the shown precision. The angular/centre target <0.01° passes all eight. The lowered Pitts card also passes 99 full-scene visibility rays from this exact sight-reference eye (in addition to the unchanged historical-eye IAC checks). Visibility is a separate full-scene measurement: Pitts/Extra 606/606 rays pass per GLB; Great Lakes 0/606. Numerical alignment alone does not establish visibility.

## Pitts card

Face centre builder (-0.8087,0,+0.015) → (-0.8087,0,-0.035); raw GLB (0.8087,+0.015,0) → (0.8087,-0.035,0), **50 mm down**. Card top 0.08485 → 0.03485; frame top 0.08785 → 0.03785; frame bottom is -0.10785, 12.15 mm above the panel bottom. Flush 1.2 mm paper / 3 mm hardware, half-letter dimensions and centreline are preserved. **No instruments moved**: existing seven-dial layout clears the lowered holder and each other. All four Pitts liveries updated; original cockpit triangles, front panel, seats and airframe buffers retained.

## Reproduce and verify

CPU Python with NumPy/Pillow:

```sh
python3 tools/own_models/sequence_card.py
python3 tools/own_models/sights.py
python3 tools/own_models/check_sights.py --report docs/pending/sights-renders/check-sights.json
python3 tools/own_models/check_iac_assets.py
python3 tools/own_models/render_sights.py
# Visibility gate (currently fails for Great Lakes; intentionally not waived):
python3 tools/own_models/check_sights.py --require-visible
```

Builders also retain their per-livery `--offline-update` entry points. The native update starts from immutable round-1 `38e8746` buffers; repeated runs do not accumulate dead buffers. No StreamingAssets GLB mirrors exist for these types.

Full Blender verification, **outside the sandbox**, from this worktree:

```sh
for liv in N31DG N1191 CHECKER-UNMARKED FACTORY-UNMARKED; do
  PITTS_LIVERY="$liv" blender -b --python tools/own_models/build_pitts_s2b.py
done
for liv in tf n300uy unmarked; do
  EA_LIVERY="$liv" blender -b --python tools/own_models/build_ea300.py
done
GL_LIVERY=v1 blender -b --python tools/own_models/build_great_lakes.py
```

Full rebuilds can reorder/replace the historical base buffers, so the round-1 byte-preservation portion of `check_iac_assets.py` applies to native incremental exports, not a fresh Blender export. Angle/geometry/visibility checks apply to either export. The unchanged source model already needs Blender/Unity review; this lane did not run Unity, Blender, dotnet or deploy.sh.

## Renders

`docs/pending/sights-renders/` contains each type’s rear-eye L/R with true-line/angle overlays and exterior 3/4 with both sights labelled, Pitts rear-eye panel + panel close-up, Great Lakes frame-isolation diagnostics, machine-readable measurements and camera manifest. Full-scene Great Lakes images explicitly show the unresolved occlusion. No gate is approved.
