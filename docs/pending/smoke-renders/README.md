# Measured smoke outlets — 2026-10-31

NEEDS YOU: run `tools/deploy/deploy.sh` outside the sandbox and obtain `** BUILD SUCCEEDED **`, then inspect smoke on the device. No build, deployment or approval gate is claimed here. This lane still draws the J-3 procedurally; review/adopt the newer J-3 models on their owning lane to see the measured pipes. No model binaries were copied into this lane.

Each PNG contains a full side view and a belly/starboard close-up, rendered directly from the mesh outside Unity. Neutral materials expose the geometry; texture artwork is omitted. Orange is the authored outlet and projected jet axis, cyan the measured mouth, magenta the point the old schema alone would generate, and blue the measured nose station. Markers are overlaid through the skin so a far-side pipe remains legible. The PA-18 variant panels show their shared OBJ with its reference wheels; the game replaces that gear with bushwheels/floats. The fuselage placement and exhaust measurements use each variant's config.

| Config / render | Aft of nose (m) | Down (deg) | Displacement (in³) | Outlet from CG: forward, right, down (m) |
|---|---:|---:|---:|---|
| [Pitts](pitts-s2b-like.png) | 1.16 | 14 | 541.5 | 0.727, -0.210, 0.630 |
| [Great Lakes](great-lakes-like.png) | 1.20 | 31 | 361 | -0.151, -0.187, 0.628 |
| [Super Cub](pa18-cub-like.png) | 0.87 | 90 | 320 | 0.995, 0.249, 0.467 |
| [Bush Cub](pa18-bush-like.png) | 0.87 | 90 | 320 | 0.995, 0.249, 0.467 |
| [Float Super Cub](pa18-floats-like.png) | 0.87 | 90 | 320 | 0.995, 0.249, 0.467 |
| [J-3](j3-cub-like.png) | 0.75 | 39 | 188 | 0.947, 0.178, 0.468 |
| [Float J-3](j3-cub-floats-like.png) | 0.75 | 39 | 188 | 0.947, 0.178, 0.568 |
| [172](c172-like.png) | 1.01 | 63 | 361 | 1.282, -0.203, 0.644 |

All retain a **4 inch effective smoke-jet diameter and 900 K**. These are the existing visual/flow settings, not claims about the physical pipe diameters. The small mesh pipes (Pitts 60 mm; Great Lakes 52 mm; 172 about 48 mm; Cub roughly 40 mm) would produce much faster jets if given the entire engine mass flow. One pipe is selected for smoke; paired pipes are not duplicated at full engine flow. Full-power jet speeds at 2700 rpm are about 70.6 / 47.1 / 41.7 / 24.5 m/s for 541.5 / 361 / 320 / 188 in³. No displacement fallback was needed to get sensible jets.

The displacements match the configured engine classes: 260 hp AEIO-540 class; 180 hp 360 class for Great Lakes and 172; 150 hp O-320 class; 85 hp Continental C-85 class. The J-3 *mesh* depicts an A-65, but its **config** has 85 hp, so smoke uses the C-85's 188 in³. O-320's 319.8 is rounded to 320. References checked: [Lycoming aerobatic engine manual, specifications pp. 2-1/2-2](https://www.lycoming.com/sites/default/files/attachments/AEIO-320-360-540%2520Oper%2520Manual%252060297-21.pdf), [Lycoming O-320 manual](https://www.lycoming.com/sites/default/files/attachments/O-320%2520Operator%2520Manual%252060297-30.pdf), [Smithsonian AEIO-540-D4A5 specification](https://airandspace.si.edu/collection-objects/engine-lycoming-aeio-540-d4a5-horizontally-opposed-6/nasm_A19940023000), and [Continental C75/C85/C90 specification sheet held by NTSB](https://data.ntsb.gov/Docket/Document/docBLOB?FileExtension=.PDF&FileName=Continental+C75+Specifications-Master.PDF&ID=40201802).

## Source models and measurement method

- **Pitts and Great Lakes:** `f9c2e034ec47d5a0b4cf2205e693ec5a74432dd4`, `lane/codex-iac-pitts`, using their `*-own.glb` files. These are the current cockpit/IAC models. Compared with `457f2a0`, the Exhaust mesh vertices, topology and world placement are identical on both types; their cockpit changes do not move the exhaust. No replacement model files are needed here.
- **PA-18 and both variants:** newest shared `pa18-cub-like.obj` found at `c0f2ad5`. The game aliases bush/floats to that fuselage. Its exhaust is the small open pipe in `Object_5`, below the starboard cowl. The outer lip centre in file axes is `(-262.36355, -60.0699, -25.0751)`; it points vertically in the longitudinal plane, with a small lateral component. The side projection is therefore **90°**, not a guessed 30°.
- **172:** newest `c172-like-own.glb` found at `340cc5a`. Its named `Exhaust` terminal ring centre is `(1.3625, -0.8500, 0.2050)` in glTF file axes. The preceding ring is `(1.4325, -0.7100, 0.2000)`, giving 63.435° down in side projection.
- **Both J-3s:** newest `j3-cub-like-own.glb` and `j3-cub-floats-like-own.glb` found at `c16f0bd`. Selected the heat-darkened belly stub on the lower right cowl, not the side collectors. Its terminal segment drops 0.048 m over 0.060 m aft: 38.660°. There is **no spinner**: 0.75 m is measured from the foremost prop-hub cap. The two models share this exhaust geometry. The float model sits about 0.1004 m lower under the current placement algorithm; its explicit CG-relative z compensates that placement while preserving identical aft distance, angle, displacement, temperature and pipe width.

Distances use the mouth's ring **centre**, not the bounding-box corner or a cowl station. Angles use the **terminal** segment, not the whole bent pipe. Small lateral components of the mesh pipe axes are projected onto the longitudinal/vertical plane because `downDeg` has no lateral direction field.

The renderer reads each model with `git show` into a temporary directory, decodes node transforms, then reproduces `AirframeModels.Place`: configured span fit and trim, outer-wing chord-midpoint alignment, wheel/keel vertical placement, and outer-wing-height placement for PA-18 aliases. It preserves the current code's distinction that `ConfigOuterWingMid` does not subtract `cg.x`, whereas alias wing height does subtract `cg.z`. Body points are CG-relative as `SmokeTrail` actually consumes them. This matters: procedural prop hubs are not reliable model mouth anchors.

`outletBodyM` is a new optional override because the existing schema cannot express off-centre pipes, their actual belly heights, or the discrepancy between procedural hubs and placed models. Without it, using these measured aft distances and the old fixed drop would miss the mouth by roughly **29 cm Pitts, 26 cm Great Lakes, 43 cm 172, 60–61 cm PA-18s, and 68–72 cm J-3s**. The existing Extra and unconfigured aircraft retain the old path. Invalid or nonfinite vectors also fall back.

After placing each model, the authored point is within **1 mm** of the measured mouth on Pitts, Great Lakes, 172 and both J-3s. The shared PA-18 block uses the midpoint between the normal wheel placement and the aliases' wing-height placement, leaving **10–12 mm** of error, within the physical lip radius. See [measurements.json](measurements.json) for exact transforms, mouth coordinates and errors. Model placement or CG/config geometry changes require remeasuring this absolute override.

## Reproduce and validate

On this Mac, no package install or Blender process is needed:

```sh
/Applications/Blender.app/Contents/Resources/5.2/python/bin/python3.13 tools/own_models/render_smoke.py
```

The script uses Blender's bundled NumPy/OpenImageIO for the CPU rasterizer and system Swift/CoreText for labels. Optional aircraft-id arguments rerender only those types. It asserts that every authored mouth lies within 25 mm of the measured mouth and writes all eight PNGs and the measurement report. It does not write to another lane or invoke Unity.

Validation: **17 new smoke tests plus 8 existing airflow/jet/protocol tests pass (25 unique tests)** using the repository's in-process xUnit runner. Standard VSTest cannot bind its socket in the sandbox. The existing Extra test `AirframeFlowTests.SmokeGoesRoundTheAirframeInTheFlatSpin` failed before the new code was compiled, at the marker from `(-3.5, 1.2, 1.6)` at `t=0.112`; it is recorded separately as one baseline failure, not repaired or counted as passing. The test project's .NET build succeeded; the required Unity deployment/build gate remains pending the operator run.
