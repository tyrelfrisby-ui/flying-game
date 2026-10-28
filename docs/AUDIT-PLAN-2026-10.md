# Aero Playground — October 2026 audit and proposed plan

**2026-10-28 · Codex · lane/codex-audit-plan · baseline 39f942c · owner identifies shipped OTA as 2026.1028.0803**

**Approval gate: Ty must approve this document before ANY implementation from it starts.** This is a static, read-only implementation audit, with this document and the requested AGENT-LOG additions as the only edits. No Unity session, builds, tests, deployment, commits, or device profiling were performed. Performance numbers below are proposed acceptance budgets, not measurements. Existing log results are historical evidence, not independently reproduced results. The OTA-to-source relationship is owner-provided, not binary-verified.

Paths below are repository-relative. `Bridge/` means `unity/Assets/_Project/Scripts/Bridge/`; `Shaders/` means `unity/Assets/_Project/Shaders/`. Recommendations describe future work requiring approval. Estimates are engineering days for one person, excluding owner review, device availability and external permissions.

## Executive summary

The strongest asset is the shared, engine-independent flight simulation and extensive flight-test harness. This is already a broad flight playground with touch controls, lessons, multiplayer, multiple scenery regions, water operations and animated aircraft. The next improvement should consolidate fidelity and device evidence rather than replace these systems wholesale.

Water is an upgrade, not a greenfield feature: `FloatHydro` already computes buoyancy, planing lift, drag and water-rudder forces; `FloatSplash`, `WakeWaves`, `WaterReflection` and three shaders already provide spray, ripples and reflections. Their limitations are clear enough to support an incremental plan: publish reliable per-aircraft water telemetry, distinguish the water regimes, add layered spray and persistent foam, then replace the constant-speed wake approximation with a dispersive Kelvin representation.

**Pipeline correction:** despite `docs/ARCHITECTURE.md` saying URP, this baseline uses **Built-in forward rendering**. `GraphicsSettings.asset` has `m_CustomRenderPipeline: {fileID: 0}`, all six quality tiers have null `customRenderPipeline`, and `Packages/manifest.json` contains neither URP nor HDRP nor VFX Graph. Existing water shaders use `UnityCG.cginc`, not URP shader libraries. Retain Built-in for the first focused water improvement; consider URP as a separately approved migration, with URP the preferred mobile destination. HDRP water is unsuitable for this iPad product.

### Top 10 findings, ranked by impact

| Rank | Finding and evidence | Impact / recommendation |
|---|---|---|
| 1 | Per-type flight fidelity needs evidence beyond broad test success. `src/FlyingGame.Core/Aero/AeroModel.cs`, `src/FlyingGame.Sim/Aircraft.cs`, `docs/CONTROL-SURFACES.md`, `docs/STICK-AND-RUDDER.md`, `docs/SPIN-ROTARY-DATA.md`. | Training credibility depends on stall/spin/authority calibration. Reconcile dated documented failures with current code; obtain bilateral spin/recovery and book-performance traces before retuning. Claude owns this. |
| 2 | Physics effects state is global and written inside force evaluation: `FloatHydro.Floats`, `LastReport`; `Aircraft.StepWithDeflectionTargets` calls hydro inside RK4's force callback. | Effects can observe an intermediate or another aircraft's state. Establish an immutable per-aircraft, completed-step snapshot before adding more effects or multiplayer wakes. |
| 3 | Existing wake is nondispersive: `WakeWaves.WaveSpeedMs = 6`, `Shaders/WaveSim.shader`. Its V angle is `asin(c/V)`, not a Kelvin envelope. | At 10 m/s this is about 36.9°, at 18 about 19.5°, at 30 about 11.5°. Add explicit dispersive transverse/divergent wake components; keep coarse rings as a separate effect. |
| 4 | Water's broad visual cues are limited: `Shaders/Water.shader` uses a flat mesh, procedural normal ripples, fixed alpha/color and sky-color fallback. | No depth absorption, real displaced silhouette or wind spectrum. Add depth/shore masks, consistent Fresnel and wind-scaled normals before expensive ocean simulation. |
| 5 | White-water spray is gated entirely by planing: `FloatSplash.LateUpdate` stops every emitter unless lift exceeds buoyancy and speed >4 m/s. | Displacement bow disturbance and nonplaning impacts disappear; there is no dedicated prop-wash emitter. Preserve the owner's current planing-only trailing mist rule while separating bow/impact/prop effects for approval. |
| 6 | Render-pipeline intent diverges from actual settings and shader implementation (paths above). | A surprise URP/HDRP conversion would expand risk across all art and cameras. Make the pipeline decision explicit; no HDRP migration for iOS. |
| 7 | C172's good nose-gear geometry is not a complete animation rig. `tools/model_c172.py`, own GLB, `AirframeModels.OwnSpecs`, `AirframeVisual.SetDeflections`. | Named surfaces exist but C172 named binding is off; flap/steering/compression channels are absent. Preserve geometry and add binding plus a separable oleo/wheel hierarchy. |
| 8 | UI uses raw screen dimensions and aggressive text shrinking: `UiLayout`, `ScreenLayout`, `StartMenu`, `OptionsPanel`. No `Screen.safeArea` use found in those layout/control files. | Notch/home-indicator clearance, minimum readable text and physical touch size need explicit rules and device evidence. Existing no-overlap intent is good but insufficient. |
| 9 | Device budgets are not demonstrated by this source audit. `SceneBootstrap` requests 60 fps; `SimLoop` documents previous iPad mini overload; water adds an extra camera and persistent RT work. | Budget the whole frame, cap effects by tier and profile sustained flight before increasing fill/compute load. |
| 10 | Model selection/provenance and geometry parity remain product risks. `docs/MODEL-PIPELINE.md`, `AirframeModels`, `AGENT-LOG.md` NEEDS YOU, `docs/models/ea300-own-PROVENANCE.md`. | Downloaded/own alternatives can differ in shape and binding; existing EA300 licensing decision remains open. Maintain per-variant rig/scale/rights records; Ty resolves existing release gate with appropriate advice. |

## 1. Physics and flight model

### Current state

`src/FlyingGame.Sim/SimLoop.cs` defaults to 200 Hz player integration and 50 Hz AI; `RigidBody6DOF` is integrated by RK4 from `Aircraft`. The accumulator caps catch-up at 0.05 s to prevent runaway overload. Unity renders the core's result rather than using PhysX for flight. `Aero/AeroModel.cs`, `AirfoilTable.cs`, `AirframeFlow.cs` and `StripFlowState.cs` support strip-local flow, finite-span effects, post-stall behavior and lagged flow state. `PropModel.cs`, `LandingGear.cs`, `WheelHydro.cs`, `AirframeContact.cs` and aircraft JSON supply propulsion, surface contacts and equipment.

The paired fixed/hinged surface approach and control power are documented in `CONTROL-SURFACES.md`. Current `Aircraft` has reversible-control settling, actuator slew and lagged separation/downwash. Do not mistake older architecture stubs for current implementation. `tools/FlightTests` contains dedicated adverse-yaw, stall-break, stall-authority, pitch-stability, reversible-control, spin, gear, tailwheel, aerodynamic ground-handling, retractable-gear, wheel-hydro, float and flying-boat tests.

### Gaps and recommendations

- **Calibration uncertainty:** `CONTROL-SURFACES.md` explicitly lists per-type tail/CG and hinge-coefficient concerns. `STICK-AND-RUDDER.md` records October 9 spin shortcomings, but October 13 `SPIN-ROTARY-DATA.md` and current spin code add later work. Those old failures are a revalidation checklist, not proof they still fail. `SpinTests` uses `TestAircraftConfig.Load()` and is not itself a fleet-wide certification; `FleetSpinSweep` is an opt-in diagnostic. Require matched loading, power, entry technique, direction, sustained stalled fraction, turn rate, attitude and recovery metrics per supported training type.
- **Ground feel:** recent AGENT-LOG entries report Pitts/Extra gear matching and retuning, with device feel still requested. Keep walking-pace turns, brake nose-over/prop-strike behavior, three-point/wheel landings and relaxed-versus-active rudder as explicit owner acceptance cases. Do not globally soften braking to conceal a model/gear mismatch.
- **Configuration authority:** follow `NEW-AIRPLANE-PROTOCOL.md`: book data → builder → configs → byte-identical StreamingAssets copies → performance/glide gates. Match physical axes, CG, thrust line, gear contact and drawn geometry with measurements; never move an aerodynamic surface simply to repair art.
- **Float regime:** `FloatHydro` already blends displacement/ventilation with Savitsky-style planing, station buoyancy, vertical damping, crossflow and water rudders. Beaver `dhc2-beaver-floats-like.json` has 7.9 m floats, 0.95 m beam, 3.2 m spread and 0.09 m² water rudders. `FloatTests` identifies these as EDO 4930s; Cub tests identify EDO 2000s. Those labels are repo intent, not independently verified dimensional surveys.
- **Water fidelity:** the core samples water height but the reviewed hydro model has no shared time-varying wave normal/orbital-velocity/current field. World-horizontal force assumptions and fixed freshwater density constrain rough-water realism. Water-rudder force fades with ventilation; this is not a pilot-operated retract/deploy model. Quantitative porpoising damping, crosswind weathervaning, asymmetric immersion/water-loop and step-exit validation deserve explicit tests. Current float takeoff tests use pitch-hold pilot logic and generous max-pitch guards; successful takeoff is not a stability-envelope measurement.
- **Runtime reliability:** global hydro reports plus per-call temporary float lists are worth isolating and profiling at RK4 frequency. Publish once after the accepted step; do not introduce Unity objects or GPU readbacks into the pure core. Preserve deterministic test water overrides and reset them between tests.

## 2. UI/UX, controls and onboarding

### Current state

`StartMenu.cs` implements FLIGHT SETUP, persisted choices, drop-down modality and drag scrolling. The newest livery-picker entry in AGENT-LOG reports four Pitts and three Extra choices, with device review outstanding. `UiLayout.cs` centralizes toolbar/text stacking and fit-to-width labels. `ScreenLayout.cs` supports both portrait control tray and landscape view; the older architecture's landscape-only statement is stale.

`TouchFlightControls.cs` reads raw multitouch with finger ownership, independent of IMGUI drawing. It has large RC pads, rudder/elevator/aileron, sticky power/trim, throttle-to-brake dead band, differential brake bias, lesson strips and held ejection. `FlightHud`, `AnalogGauges`, `HudOverlay`, `OptionsPanel`, `Practice/PracticeHud`, `LessonIllustrator` and `LessonDebrief` already provide substantial instruction and flight feedback. Options can restore per-aircraft feel, and `JoystickSetup` covers controllers.

### Gaps and recommendations

1. Add one safe-area coordinate contract used by menu, modal, HUD, tray and touch hit regions. Validate rotation during flight and an open menu, interrupted touches, app background/return and control release. Preserve realistic elevator sense and the owner's camera behavior (`HANDOFF-2026-10-01.md`), not a generic chase-camera redesign.
2. Prefer a minimum readable text size with wrapping/scrolling over `UiLayout.Fit` shrinking to 45% (minimum six pixels). Define minimum 44×44 pt interactive targets after converting pixels to logical points, not a raw-pixel constant. Validate longest aircraft/livery labels on small iPhone and iPad split layouts if supported.
3. FLIGHT SETUP exposes a broad product. Propose a concise first-flight preset and progressive advanced disclosure, with a persistent summary of aircraft, start, weather and lesson; retain expert speed. Clearly distinguish airframe type from livery and own/downloaded model selection. No evidence from this static read establishes that a new user can find and complete the first lesson unaided.
4. Add a short skippable/replayable RC control introduction: elevator direction, sticky throttle, brake detent, trim and pause/reset. For floatplanes teach displacement/plowing/step/liftoff and water-rudder status once physics exposes it. Do not label a visually guessed regime as a physics fact.
5. Establish HUD priority: flight-critical status first; optional force vectors, lessons and multiplayer text must not compete. Provide redundant text/shape for color-coded state, contrast checks against bright sky/water, and explicit accessibility scope. IMGUI does not by itself establish VoiceOver navigation; assess separately before promising it.

## 3. Imagery, scenery and asset pipeline

### Current state

`WorldBuilder` creates terrain meshes, fine patches, lakes as ellipse fans, river strips, water bases/docks and waterfalls. `AlpineBuilder`, `AlaskaBuilder`, `ForestBuilder`, `SeasideBuilder`, `HighDesertBuilder` and `SoaringScenery` supply regional content; world geometry comes from the shared terrain definitions. `Cumulus` and its shader provide cloud lighting cues, while `SceneBootstrap.BuildSun` creates a directional sun. Custom terrain, lit, airframe, glass, cloud and prop-blur shaders keep the visual stack relatively direct.

`AirframeModels` places imported/own variants with orientation and span scaling; `AirframeVisual` builds procedural fallbacks and fits/skins imported control regions. Pitts explicitly uses named-surface binding. `ModelTextureAssigner` assigns OBJ material textures; glTFast 6.9.1 is present. `MODEL-PIPELINE.md` records measurement pitfalls, mismatched substitutes, separate own/downloaded placement and reference-based review. Existing image checks and `OwnPittsModelTests` are useful precedents.

### Gaps and recommendations

- Set an art reference for water, terrain, sky and aircraft together. Fixed sun/flat-color fallback and water's independent sky color risk inconsistent illumination; use a shared sun/sky/haze contract and reference captures for morning/noon/haze. This is a source-based consistency risk, not an observed screenshot defect.
- Audit active geometry/material/texture cost by model variant and region. There is no demonstrated fleet-wide LOD/ASTC/residency gate in the reviewed path. Define near/medium/far aircraft LOD, distant forest/prop instancing, terrain patch transitions, mipmaps and iOS texture caps. Keep important wing/control silhouettes at lesson distances. Avoid multiplying unique materials for each livery panel.
- Prioritize shoreline depth/color and aircraft-water contact over generic terrain detail. Derive shore masks from authoritative water/terrain geometry, including river and alpine water, rather than a second unrelated ellipse.
- Preserve references and credits. The existing EA300 rights question in AGENT-LOG is an owner release decision, not settled by this audit. Verify texture/art licenses as well as geometry and fonts before release. Do not delete retained source alternatives.
- Convert custom shaders, reflections, cloud blending, model material assignment and shader inclusion explicitly if URP is approved; a package installation is not a finished migration. Review all quality tiers, gamma/linear appearance and Metal stripping.

### C172 animation inventory — owner special request

Inspected `tools/model_c172.py` and parsed the shipped own GLB JSON in `Resources/Models/c172-like/c172-like-own.glb` without importing or modifying it. This confirms node names/transforms and generator intent; it does not substitute for visual deflection tests.

| Part | What exists | Missing / action |
|---|---|---|
| Ailerons | `Aileron.L`, `.R`, separate meshes with translated origins; generator places origins at the leading hinge edge. | C172 `OwnSpecs` does not set `namedSurfaces: true`. Currently uses generic carving. Bind named parts and verify opposing signs, span selection and no fixed-wing deformation. |
| Elevators / rudder | `Elevator.L`, `.R`, `Rudder`, separate named meshes and generator hinge origins. | Verify real hinge axes after export/placement, paired elevator motion and rudder sign with named binding. Pitts's binder reconstructs hinge geometry; merely preserving an origin does not activate it. |
| Flaps | `Flap.L`, `.R` present as separate own-GLB meshes; generator splits them from the wing. | `SetDeflections` has no flap argument; named `Kind()` recognizes only aileron/elevator/rudder. Add a flap channel, calibrated travel/kinematics and rest transforms, with no aileron double-binding. Physics already has flap effects; art is not proof of animation. |
| Nose steering | `Gear.Nose.Steer`, one mesh, origin at upper leg steering axis. GLB translation approximately (1.100, −0.272, 0) in exported Y-up coordinates. | No steering-angle consumer found in the reviewed visual update. Feed actual gear steering angle, transform axis correctly (Blender Z-up becomes glTF Y-up), preserve neutral pose. Do not blindly apply the generator comment's local-Z instruction to imported coordinates. |
| Nose strut / wheel | Good visible leg, fork and wheel geometry is joined by the generator into `Gear.Nose.Steer`. | No separate upper/lower oleo, fork and axle-pivot wheel nodes: cannot independently compress or roll wheel with current hierarchy. Split parts without changing approved appearance; define travel axis/limits and consume actual contact/compression, not gear-retraction fraction. |
| Downloaded/default C172 | Separate model spec; own model is conditional on `SessionSettings.UseOwnModels`, unlike forced-own Pitts/Extra. | Own-GLB rig evidence does not certify the downloaded model. Inspect/bind each retained runtime variant or clearly scope rig support to own C172. |

Acceptance: neutral plus full-left/full-right controls and each flap detent; steering at rest and while taxiing; load/unload strut within limits; wheel rotation about axle; no fuselage/wing creep; same pivots after repeated aircraft/model switches. Keep the nose-gear silhouette Ty likes.

## 4. Water level-up: detailed Unity approach

### What exists and the important boundaries

- `FloatHydro.FloatState` exposes wet status, drafts, drag, lift, buoyancy, speed and bow/step/stern positions; hulls duplicate slot 0 into slot 1, and optional wingtip floats occupy slots 2/3. Effects currently process only two slots. A hull can therefore get duplicated spray, and tip-float effects are not represented individually.
- `FloatSplash` builds eight world-space Shuriken systems with radial unlit soft billboards. Maximum configured capacities total **4,000 particles** (two sides × four systems × their ×4 capacities); this is a cap, not a measured live count. There is trail mist and bow/stern spray, but no distinct prop-wash source or connected water sheet.
- `WakeWaves` uses three RHalf RTs, nominal ~2 m cells (coarser for the harbor), 20 Hz stepping, 100 s damping parameter and 300 s lifetime after forcing. Grid size scales with lake aspect ratio; the code's nominal 1024 limit is based on `Rx`, so audit both dimensions. Bow/step Gaussian stamps excite one active `WorldTerrain.Lakes` field. It does not generalize automatically to every river/alpine/ocean surface, retain multiple lakes, or process all aircraft.
- `WaveSim.shader` uses a 9-point Laplacian and an ellipse boundary with edge damping. Fixed zero-height boundaries reflect with phase inversion: useful stylization, not depth-aware shore refraction/breaking. Multiplying each recurrence result by `_Damp` also means the comment's decay interpretation should be measured, not assumed.
- `Water.shader` bends shading normals from this field but **does not displace vertices**. `WaterReflection` renders aircraft layer 8 and prop layer 9 into a 512² ARGB32/depth16 planar reflection each applicable render; the rest is a uniform sky fallback. It chooses a single water height and accepts surfaces within 1.5 m. Nearby separate bodies/heights and below-water cameras need review. No SSR, depth absorption or bathymetry-driven foam exists in this shader.

### Pipeline decision

Implement the first visual slice in existing **Built-in**, in hand-authored HLSL/CG with reusable math and data contracts. No new render pipeline is required for Shuriken, render-texture stamping, mesh wakes or planar reflection. If Ty approves a broader rendering investment, make **URP forward** the destination and port the water math to an HLSL include, optionally wrapped by Shader Graph for art parameters. Shader Graph is not currently installed, and URP must not be described as already active.

Do not choose HDRP for its water package: Unity's [HDRP 17.3 requirements](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.3/manual/System-Requirements.html) list desktop/console platforms, not iOS. For this mobile app the recommended alternative is custom water on Built-in now / URP later. Pipeline migration must prove parity before replacing the current renderer.

### Surface, depth, wind and reflections

1. Use a shared water-body ID, surface height and shore/depth mask. On low tier bake/derive a compact depth map; on higher tier optionally use camera depth for intersections, after accounting for its pass/bandwidth. Depth-dependent absorption transitions shallow green/brown to deeper blue, with restrained scatter and tunable local color.
2. Use Schlick Fresnel with water-like normal-incidence reflectance near 0.02, roughness-dependent sun glint and sky environment sampling. Keep stylization adjustable. Reflection and scene lighting must share sky/sun settings.
3. Begin with 4–8 analytic Gerstner components for visible wind waves on a locally tessellated patch, plus two scrolling normal octaves for capillaries. Bound steepness, attenuate near shore and blend to cheaper distant normals. Small wind chop may remain visual-only initially; large displacement must wait for a shared CPU surface sampler so the floats cannot visibly hover. Wind direction/speed comes from the atmosphere snapshot, not shader time alone.
4. Avoid FFT as the first milestone. Ocean-scale spectral waves would add compute, memory, sampling synchronization and validation; consider only as an optional high-tier later experiment with measured benefit. Gerstner does not itself solve Kelvin wakes.
5. Keep one selective planar reflection for the nearby aircraft and essential docks, 256² low / 512² standard. Gate by visible water and distance; test 15–30 Hz reflection updates with pose-change refresh and fall back to probes/environment when off. Static reflection probes/cubemaps supply scenery and sky; avoid runtime probe recapture. SSR adds depth/history/bandwidth and off-screen omissions: exclude from baseline iPad tier, optional high-tier experiment only. Restore `GL.invertCulling` with exception-safe handling in a future robustness pass.

### Ripples, Kelvin wakes and persistent foam

Separate three scales so a harbor-sized coarse grid is not asked to resolve a float's idle ripples:

- **Contact ripples:** a local 128–256 m region, 256²/512² RHalf height ping-pong field (three buffers if retaining second-order integration), or a bounded analytic ring buffer. Drive small rings by changing immersion/contact impulses and slow motion, not speed alone. Stationary steady immersion must settle rather than pump energy forever. Shade normals and planar reflection with the same ripple gradient. Use a body mask; fade/scroll local fields without teleporting old rings.
- **Kelvin wake:** generate pooled segments along each real hull/float's water-relative path, storing birth position, heading, speed, strength, body ID and time. Use an instanced/combined ribbon or wedge mesh with bounded segment history. In a custom HLSL function integrate a small precomputed angular wave set, or evaluate a precomputed dispersive wake atlas, containing both transverse crests and divergent branches. For deep-water gravity waves use `omega² = g*k`; the stationary pattern obeys `omega = k·V`. The ideal envelope approaches `asin(1/3) = 19.47°`, not `asin(6/V)`. Dominant visible ridges may narrow with speed/Froude number, and shallow water requires a different dispersion relation. Do not draw two fixed white lines and call that a Kelvin wake.
- **Turns/transients:** freeze each emitted segment's source pose and age; evaluate crest phase using simulation time, taper into the evolving path and dissipate smoothly. Blend low-speed displacement rings into the dispersive trail, then stronger step spray/foam; wake amplitude follows displaced volume/drag with caps, not arbitrary throttle. Test slow idle taxi, accelerating through hump, turns, liftoff and shutdown. After liftoff existing wake continues propagating while emission stops.
- **Foam/trail:** use a world-mapped RG render texture (foam coverage and age/energy) stamped by bow/step sources and the wake footprint, decayed/advection-updated at 10–20 Hz. Sample it in the water shader. This gives lingering white-water streaks without thousands of transparent decal objects. On URP a custom pass can do the same; do not assume stock decals automatically project onto transparent water. Use flow maps for river advection and shoreline foam; normals carry nonbreaking idle wakes with little or no white foam.
- **Shore behavior:** replace the ellipse-only mask with the water body's actual boundary/depth field. Low tier absorbs waves in a shore band; high tier may add calibrated partial reflection and depth-dependent wavelength. Preserve the owner's earlier five-minute wake request through a coarse low-energy far field or bounded stored segments, not full-resolution simulation for five minutes across every visited lake. Obtain Ty's priority for lifetime versus device cost.

These are appearance approximations with explicit physical constraints, not a full fluid solver. Verify an isolated straight-run wake top-down against a 19.47° envelope and expected crest wavelength before layering noise/foam. For deep-water transverse stationary waves, `lambda = 2*pi*V²/g` is a useful reference; finite hull size and the angular spectrum determine visible amplitudes.

### Spray and prop wash

Retain **Shuriken** as baseline. Use pooled, world-space emitters sharing materials/atlases: (a) short directional bow/chine droplets, (b) thin animated mesh ribbons for step sheets, (c) larger but sparse trailing mist sprites, (d) contact-impact bursts. Wind advects mist; droplets use ballistic trajectories; water-plane kill/fade avoids submerged white clouds. Low tier uses no particle collisions, dynamic lights or per-particle shadowing. Sheets should break into droplets with speed/immersion, not become opaque white curtains covering the airplane.

Prop wash is a separate near-surface effect derived from prop disc/hub, thrust/slipstream direction, water distance and aircraft attitude. Emit where the slipstream intersects water, with threshold/hysteresis; do not equate throttle at any altitude with spray. Keep it visually subordinate at idle. Claude supplies the physically meaningful propulsion signal; Codex supplies the emitter and art. The current owner rule limits planing trail mist, so changes to displacement/prop-wash spray behavior need explicit visual approval.

VFX Graph is **not categorically impossible on Metal/iPad**: modern Metal hardware can support compute. However, [Unity VFX Graph 17.3 requirements](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.3/manual/System-Requirements.html) require compute and SSBO capabilities, support URP/HDRP, and still mark mobile support as not out of preview. It is absent from this Built-in project. Check `supportsComputeShaders` and `maxComputeBufferInputsVertex`, validate actual graphs on each supported device, and retain Shuriken fallback if a later URP high-tier prototype is approved. Compute availability alone is neither pipeline compatibility nor a thermal-performance guarantee. [Unity's Metal compatibility guide](https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/cross-platform-features/graphics-apis/metal/requirements-and-compatibility) also excludes geometry shaders, so use CPU-generated/instanced sheet meshes rather than geometry-shader amplification.

### Proposed iPad budgets and profiling contract

Provisional qualification devices: **iPad mini 6 (A15)** as low tier, **iPad Air M1** as standard, **iPad Pro M2 or newer** as high-tier regression. Confirm the actual owned mini/Air chips and oldest supported device with Ty; add that oldest device if slower. Also validate a small supported iPhone. No 120 fps target initially.

| Budget (incremental water feature, one nearby floatplane) | Low A15 | Standard M1 / high M2+ baseline |
|---|---:|---:|
| Sustained target | 60 fps, 16.67 ms frame | 60 fps, 16.67 ms frame |
| Water surface GPU | ≤0.65 ms | ≤0.8 ms |
| Reflection GPU, on update frames | ≤0.6 ms | ≤0.8 ms |
| Ripple/wake/foam updates GPU, on update frames | ≤0.35 ms | ≤0.5 ms |
| Spray/sheet GPU | ≤0.6 ms | ≤0.9 ms |
| Total water GPU on coincident update frames | ≤2.2 ms | ≤3.0 ms |
| Water/effects main-thread CPU | ≤0.5 ms | ≤0.7 ms |
| Added draw calls incl. selective reflection | ≤25 | ≤40 |
| Visible live spray particles, all local water effects | ≤800 steady / 1,200 burst | ≤1,500 steady / 2,000 burst |
| Sheet/wake mesh triangles | ≤8k | ≤16k |
| All water textures/RTs resident incl. mipmaps/depth | ≤16 MiB | ≤24 MiB |

Keep the rest of the game within roughly 12 ms GPU / 10 ms CPU on qualification routes, leaving headroom; CPU and GPU timings are overlapping budgets, not sums. At least 20-minute warm runs: harbor step takeoff, idle tight turns, river/alpine transitions, large H-4 hull and multiplayer/AI stress. Record GPU/main-thread p50/p95/p99, frame pacing, draw calls, resident textures, live particles, overdraw and thermal state. Goal p95 frame ≤16.67 ms and p99 ≤20 ms after warm-up, excluding documented scene-load transitions. Render resolution must be recorded; start at ≤1920×1440 equivalent internal pixels with native-resolution UI, tune by aspect ratio/device. Native high-resolution iPad rendering is not assumed free.

Memory examples: three 512² RHalf height RTs ≈1.5 MiB; two 512² RGHalf foam RTs ≈2 MiB; 512² ARGB32+depth16 reflection ≈1.5 MiB before platform overhead; 1024² four-channel 8-bit atlas with mips ≈5.33 MiB uncompressed. Account for actual Metal formats/alignment, masks, normal maps and transient depth copies. Compress static textures with suitable ASTC settings after artifact review; do not count RTs as compressed. A 1024² three-buffer height field alone is ≈6 MiB.

Quality degradation order: reduce reflection frequency/resolution → particle screen coverage/count → local field resolution/far history → wind-wave mesh density. Keep contact/regime cues and flight simulation intact. If low tier cannot sustain 60 after this, seek Ty's approval for a deliberate stable 30 fps mode (33.33 ms), rather than silently oscillating frame rate or slowing sim time. Allocate a shared global budget for all nearby aircraft; do not multiply local budgets per remote plane.

## 5. Ownership and hand-off contracts

| Work | Owner | Contract / review boundary |
|---|---|---|
| Aero, stalls/spins, control authority, gear forces, float/hull geometry parameters, buoyancy, planing, porpoising, water rudders, water/current physics | **Claude** | Pure Core/Sim; reference-backed configs and deterministic flight tests. Codex does not tune forces to match pictures. |
| Water shaders, reflections, wake appearance, foam, spray/sheets/mist, model rig/art, terrain/sky art, menu/HUD art/layout | **Codex** | Consume authoritative snapshots; no visual displacement fed back as an undocumented force. Claude reviews any bridge touching simulation state. |
| Surface sampler and telemetry schema | Claude defines physical semantics; Codex defines rendering needs; both review | Versioned units, axes, lifetime and body identity agreed before either side depends on it. |
| Handling/visual reference acceptance, minimum device, licensing choice and scope | **Ty** | Approve this plan first, then review concrete phase captures and handling traces. |

Proposed `WaterInteractionSnapshot` (name illustrative): aircraft ID, sim timestamp/sequence, water-body ID, valid contact count and per-contact identity/type (left/right hull/tip float), world NED bow/step/stern/contact positions in metres, local water height/normal, water-relative velocity vector m/s, immersion/wetted fraction, drag and lateral force N, buoyancy/planing lift N, accepted attitude/angular rates, water-rudder angle/deployed fraction, and regime `Dry / Displacement / Plowing / Step / Airborne` plus continuous blend. Physics owns threshold hysteresis; visual code smooths only presentation. Include prop hub/disc radius, thrust/slipstream direction and strength for wash; collision/entry impulses carry timestamp and stable event ID so render-rate changes cannot duplicate bursts.

Publish at the end of an accepted fixed step, **per aircraft**, with an immutable or double-buffered view. Never read mutable static RK4 diagnostics as a cross-aircraft API. Clear removed contacts on swap/reset; represent a single hull once, not as duplicated floats. `CoordinateMap` is the one NED→Unity conversion. On replay use recorded sim timestamps/state; on remote aircraft interpolate explicit contact state or disable detailed interaction instead of using local global state.

A shared CPU `WaterSurfaceSample(bodyId, position, simTime)` should return height, unit normal, current/orbital velocity and depth where valid. Initial flat-water behavior remains the reference; GPU uses matching analytic parameters and phase. Any wave coupling into buoyancy is Claude's separately tested change, not part of a shader polish patch.

## 6. Prioritized phased delivery plan

All phases are **proposed only**. Every row lists owner, effort, dependencies and a concrete gate. Phase 0 approval is required before any implementation, including profiling instrumentation. Later tests/builds listed here are future acceptance work, not actions performed by this audit.

| Phase / item | Owner / effort | Dependencies | Acceptance criteria / tests |
|---|---|---|---|
| **0A — Approve scope and baseline** | Ty + both / 0.5–1 d | Ty approval of this document | Record chosen devices, target fps, water reference, retained renderer and authoritative source/OTA pairing. Reconcile newer spin work with dated docs. |
| **0B — Baseline capture and evidence matrix** | Both / 2–3 d | 0A | Run existing FlightTests and relevant opt-in probes after approval; capture four phone/tablet orientations, current C172 variants and water regimes. Profile 20-minute qualification route. Store baseline metrics and existing failures without silently redefining thresholds. |
| **1A — Per-aircraft water snapshot** | Claude / 2–3 d; Codex integration / 1 d | 0B | No RK-stage/static contamination; two simultaneous aircraft yield isolated values; hull appears once, tips clear on swap; unit/axis, reset, replay and timestep invariance tests. Existing float and flying-boat results preserved. |
| **1B — Hydrodynamic envelope validation** | Claude / 3–5 d | 1A | Cub/Beaver/H-4 mass/draft equilibrium, displacement drag curve, hump/step/liftoff distances versus sourced data; perturb pitch and measure damping/growth; left/right taxi, crosswind weathervaning and asymmetric landing traces. Ty approves tolerances before calibration. |
| **1C — C172 binding and gear rig** | Codex / 2–4 d; Claude telemetry / 1 d | 0B, gear-angle/compression contract | Own-model named surfaces bind without carving fixed skin; UP/HALF/FULL match physics; nose steering, oleo compression and axle roll pass inventory acceptance above. Downloaded variant behavior explicitly documented. Geometry/silhouette preserved. |
| **1D — Flight-fidelity regression triage** | Claude / 3–5 d initial | 0B | C172/2-33/Pitts/Extra bilateral stalled spins and recovery measured; book stall/glide/climb and full-aft authority reviewed; each remaining type has an owner and evidence gap. Follow-on retunes estimated separately; no claim to complete fleet calibration in five days. |
| **2A — Water surface and reflection polish** | Codex / 3–5 d | 1A, 0B | Shallow/deep/shore color and Fresnel read correctly; shared lighting; 256/512 reflection tiers, bounded visible-body selection and fallback; no black/magenta water, near-camera clipping or separate-height reflection leaks. Meets GPU/memory budgets on low tier. |
| **2B — Idle ripple and body coverage** | Codex / 2–3 d | 2A | Beaver idle/slow turns show outward rings and reflected ripple cues without white sheets; steady rest decays; lake/river/alpine IDs are handled explicitly; no spill over land or teleport at body transition. |
| **2C — Layered white-water spray** | Codex / 3–5 d | 1A–1B, 2A; Ty approves nonplaning effect scope | Bow contact, step sheets, droplets, trailing mist and near-surface prop wash respond to distinct signals; no prop wash aloft, no duplicated hull/tip effects, no persistent airborne emission; counts/overdraw within tier caps. Device capture against approved reference. |
| **3A — Dispersive Kelvin wake** | Codex / 4–6 d | 1A, 2A–2B | Deep straight runs at 5/10/20/30 m/s show both wave families and appropriate ideal envelope; wavelength/time evolution checked; turns follow history; speed transition continuous; wake persists after liftoff then dissipates. Expose debug footprint/age overlays for review. |
| **3B — Foam history and shore integration** | Codex / 2–4 d | 3A, depth/body masks | Foam advects/fades independently of height, idle wake remains largely reflective, shoreline mask matches terrain; five-minute low-energy history fits cap and has no accumulating allocations. Shore absorption/partial reflection behavior clearly labeled approximate. |
| **4A — Touch/readability and first-flight flow** | Codex / 3–5 d | 0B; 1A for water-state labels | Safe-area and ≥44 pt targets checked, no text below agreed readable minimum; longest dropdown labels, modal drag/tap, rotation, cancel/background, two thumbs plus trim and brake detent pass on phone/iPad. New pilot completes first lesson and water introduction with skippable guidance. |
| **4B — Scenery/model consistency and LOD** | Codex / 4–6 d | 0B, 2A art direction | Near/far aircraft and regional scenery have measured LOD/material/texture budgets; no unacceptable popping, shoreline cracks or lost control silhouette; day/haze lighting and liveries reviewed. Rights/provenance matrix has no unresolved shipping blocker. |
| **5A — Optional URP migration spike** | Codex / 2–3 d decision spike | 0B; separate Ty approval | Port one representative terrain/aircraft/cloud/water view in isolation; compare Metal visual/performance parity and enumerate every custom shader/camera dependency. Deliver go/no-go and migration estimate. No automatic production pipeline switch. |
| **5B — Optional migration / advanced water** | Codex / L, provisional 8–15 d migration only | Approved 5A results | All shaders/scenes/tiers/reflections/transparent effects and build stripping pass device parity. VFX Graph/FFT/SSR each require a separate measured benefit and fallback; not included in baseline water promise. |
| **6 — Integrated acceptance and release handoff** | Both + Ty / 2–3 d | Selected phases complete | Full protocol/flight tests, scene/config parity and warm device budgets pass; repeat model swaps, reset/replay, AI/remote aircraft and water-body transitions; Ty signs handling and visuals. Operator controls commits/build/release authorization. |

**First shippable scope after approval:** 0 → 1A/1B → 2A/2B/2C, with C172 1C and physics triage 1D prioritized alongside it according to staffing. Phase 3 delivers the requested Kelvin upgrade; Phase 4 brings wider usability/scenery consistency. Phase 5 is optional and must not block useful Built-in improvements. Phase 6 gates whichever release scope Ty selects. Estimates are not a commitment to simultaneous work or permission to delegate agents.

## 7. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Effects hide an incorrect step/porpoise model | Physics telemetry and reference traces first; independent regime overlays; Ty handling acceptance before visual tuning. |
| Training claims outpace per-type evidence | Versioned flight matrix, dated probe results, explicit unresolved cases; never equate broad suite pass with certified behavior. |
| GPU appearance diverges from float contact | Flat reference mode, shared analytic surface parameters and time, tight amplitude caps until CPU wave sampling exists. |
| Transparent water/spray consumes mobile fill rate | Screen-coverage limits, lower-res reflection, few mesh sheets, shared atlases, distance culling and whole-scene warm profiling. |
| Persistent wakes consume unbounded memory/work | Hard body/history/count caps, far-field degradation, fixed pools and no unlimited RT per lake/aircraft. |
| New spray contradicts earlier planing-only request | Keep trail mist planing-only; approve separate displacement impact and wash behavior with Ty. |
| Planar reflection affects wrong surface or render state | Body ID and plane tolerances, single selected plane, exception-safe culling restoration, underwater fallback and multi-height regression captures. |
| Migration breaks custom visual stack | Separate URP spike and approval; retain current renderer until all shader/camera paths have parity. |
| Model art fix changes flight behavior | Preserve physics config; measure rig against approved geometry, send geometry/config mismatches to Claude separately. |
| Existing rights question delays App Store | Keep the existing NEEDS YOU decision visible; resolve provenance/permission or approved replacement before release. No new legal conclusion in this audit. |
| Device minimum or native resolution invalidates budget | Confirm hardware, publish resolution/thermal conditions and measurements, then tune tiers or seek explicit fps compromise. |

## 8. Open questions for Ty

1. Approve this phased direction, especially keeping Built-in for the first water slice and treating URP migration separately?
2. Which exact iPad mini/Air and oldest iPhone must qualify, and is sustained 60 fps mandatory on all of them? Is a stable 30 fps low tier acceptable only if budgets cannot be met?
3. Which visual reference should lead: calm reflective freshwater Beaver operations, windy lake chop, or ocean swell? Recommend calm lake/harbor Beaver first.
4. Is the prior five-minute wake lifetime still mandatory everywhere, including after changing lakes, or may older distant wakes become a coarse approximation?
5. Approve nonplaning bow/impact disturbance and near-water prop wash while retaining planing-only trailing spray? Current code intentionally suppresses all spray outside planing.
6. Should the first C172 animation milestone apply to the own model only, or must the downloaded/default alternative have equal animation support immediately?
7. Which flight types and spin cases are authoritative training priorities for the next release, and can Ty provide representative handling/reference acceptance for float step taxi and porpoising?
8. What is the intended accessibility scope (readability/touch first, or full VoiceOver navigation)?
9. Resolve the already-open EA300 provenance/release choice recorded in AGENT-LOG before App Store release; this audit does not make that decision.

## Audit evidence and limits

Reviewed repository status/head, architecture/control/model/new-airplane protocols, handoffs, Stick and Rudder coverage, spin research, current AGENT-LOG, the core/sim and named bridge/shader paths above, relevant aircraft configs and flight-test definitions, Graphics/Quality settings, packages, and the C172 own GLB node metadata. The working tree started clean on the requested branch at 39f942c. Findings are tied to those checked-in implementations. No claim is made that every asset was visually inspected, that every code path was executed, or that historical failures remain current. Device visual/handling review and runtime verification are deliberately scheduled after approval.
