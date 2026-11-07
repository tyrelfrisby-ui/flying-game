# Water visuals overhaul — 2026-11-05

Review lane: `lane/codex-water-visuals`. Local only; no merge, push or device install.
Renders: `~/Developer/agent-lanes/renders/water-visuals-overhaul/`.

## What changed

The extra airplane-shaped mark is the planar ground-shadow path, independent of the
single mirrored camera. `GroundShadow` projected a black copy 8 cm ABOVE the water,
then submitted it to every camera (including the mirror camera). It could therefore
appear both as a surface silhouette and as a displaced reflected silhouette. Water
now receives no surface-projected shadow. Land projection is unchanged. A future
shallow-bed shadow would need an actual submerged receiver and volume attenuation.

`Water.shader` uses the water/air Fresnel term in every lighting preset. Calm sunset
previously forced a minimum 91% mirror weight even when looking down into the water.
The new body contribution follows Beer-Lambert transmission along the view ray;
existing opaque geometry is visible through the transparent pass at short optical
paths. Authored bed depths cap the path without hiding nearer submerged parts.
There is no full-screen colour copy: this implements transmission, without screen
space refraction displacement or underwater caustics. The shared depth-error and
recorder fallbacks remain in place. Five bilinear samples soften the one reflection
texture; camera count, resolution tiers and reflection masks are unchanged.

`Spray.shader` softens particle edges and opaque-surface intersections using the
camera's depth, with the existing mirror/recorder/poor-depth fallbacks. Visual footprints are smaller and fade in opacity faster; foam uses its own broken-cell material. Particle
emission, velocities, drag, parcel counts, audio and wake forcing are unchanged.
`FloatSplash` exposes its existing frame body as a private explicit-time step so
editor captures advance the actual parcel field rather than edit-mode delta time.

## Capture fixture

`WaterRender` now uses the first current island water lane, its heading and measured
surface. The previous legacy Harbor coordinates were dry under the current island;
those initial captures are retained under `invalid-dry-fixture/` and are NOT review
evidence. The tool now rejects a dry fixture. It includes `GroundShadow`, uses a
fixed 1/60 s parcel step, and captures Tail, rear-quarter Chase and close bow views.
The pose is a prescribed tow, not proof of a free-running takeoff. `WakeWaves` has
an extracted allocation method so the editor can allocate at the current lane
without changing legacy lake tables or runtime wake selection/physics.

## Validation and limits

iOS ** BUILD SUCCEEDED **, 2026.1105.2035 (202611052035), zero C# / shader errors; explicit NOINSTALL. Seven wet-fixture capture invocations completed. WaterMagentaCheck PASS: all six main and six mirror targets had zero magenta pixels. Strict platform parity: OK. See the render-folder README for evidence paths.
Phone/iPad/Quest frame time and appearance still require Ty's review. These shared
shader and Bridge changes need to enter both platform lanes together after review;
this lane does not merge either platform branch.
