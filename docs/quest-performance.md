**Status: MEASURED on the headset once (build a222bda, 2026-11-02, logcat in /tmp/questlogs/perf-1410), and the
cause of the slowdown found from it — see *Slow near the ground*. The fix is measured on the Mac only.** Above 100 m
that build ran 72 fps with the main thread at 4.5–8 ms and the GPU (app) at 6.7–7 ms (VrApi's own `App=`), 10–11 ms
with the recorder running. Everything else in the budget table is still a budget.

Hardware: Snapdragon XR2 Gen 2, 1832 × 1920 per eye, refresh 72 / 90 / 120 Hz. The runtime picks the recommended
eye-buffer size; `[Quest] XR on` in the log prints it.

## The "freeze" in the dive (Ty's Great Lakes clip AeroQuest-20261102-131749, 1:30–1:38)

**It was not a stalled frame.** The in-app recorder renders on the main thread, so a stalled main thread leaves a
hole in the clip, and there is none. Its frames arrive at 30 a second through every second from 1:26 to 1:48, and no
frame differs from the last more than any other frame of the dive does (no "jump" frame after a hold). The sound is not
silent either: from 1:30.5 to 1:40.5 it is at full scale, clipping (RMS −3 dB, peak 0 dBFS: the wind and the engine
in the dive). It falls to −30 dB at 1:41 and settles at −16 dB. What the frames show: the dive ends nose-first in
the sea at about 1:37, the view fills with water, the propeller windmills slowly at POWER 0 %, and the head moves.
Then power comes in at 1:43 and the aeroplane gets out. That is the aeroplane stopped dead in the water: the
crash-into-water work is not in this build (it is on its own lane), and a stopped aeroplane with the head still
moving reads as a freeze.

**What a real stall looked like in the 677e374 log** (Ty, 15:40, `quest-logs/ty-unity-5517.txt`): one `SLOW FRAME
3554 ms` with `forest live 1 queued 137`. That is the scene's first frame, the landing page being built
(15:40:02.58 → 06.13; the scene's Awake lines are at 05.25–05.43), not a frame in flight. The forest then built 126
chunks in the next 1.4 s against its per-frame budget, which is the "21 fps over 5 s". The 16–18 fps on the ground
from 15:41:40 is the ground-contact cost that 159e5f46 fixed (see the next section).

**So the next one cannot go unexplained:** every frame over 100 ms now writes a line of its own (`QuestRig.HitchWatch`, at most 120 a session):

    [QuestPerf] hitch 3554 ms (frame 212, 0.4 s after load) | agl 437 m alt 461 m 0 m/s | menu open | sim 0 steps | forest live 1->126 queued 137->12 worst 19.5 ms | gc +0 heap 41 MB | wake off reflection off | rec off | notes: model pitts-s2b-like (own) loaded; airframe rebuild pitts-s2b-like

`notes:` is whatever heavy one-off work announced itself in that frame or the two before (`QuestRig.Note`): an
airframe rebuild, a model loaded from Resources, a wake field allocated, FLY, a recording started. `forest a->b` is
the streamer before and after the frame; `gc +n` the collections started in it; `sim n steps` the physics steps it ran.
A hitch with `notes: none`, no forest change and `gc +0` points at something not yet instrumented: a shader
compiled on first sight on the render thread, an audio clip loaded, or the OS. A new line is the first evidence
that is worth having.

**Not done, deliberately:** shader / pipeline prewarming. Nothing in a log yet shows a first-sight compile. On
Vulkan, Unity 6 prewarms pipelines only from a `GraphicsStateCollection` traced on the device itself, which cannot
be made without a build and a device session. If a hitch line ever shows `notes: none` with `forest` unchanged and
`gc +0` on the first sight of something (spray, the water close up), trace one.

## Slow near the ground (Ty, build a222bda, 2026-11-02): "slows to a crawl after a couple of minutes"

**Measured on the device** (logcat, `[Freeze]` + `[QuestPerf]` every 5 s). Not memory (heap steady 37–43 MB), not
the collector (+1…+13 per 5 s), not heat (Thermal Status 0, CPU/GPU level 4/4), not the recorder (on and off alike),
not the GPU (6.7–11 ms). It was the main thread, and it followed **height above the ground**, not time:

| Where (from the log) | fps | cpu main | sim capped / 5 s |
|---|---|---|---|
| any flight above ~100 m agl | 72 | 4.5–8 ms | 0 |
| descending through ~60 m (e.g. 13:59:41, agl 43) | 20 | 48 ms | ~95 (every frame) |
| climbing back through ~100 m (14:01:51, agl 208) | 72 | 6.5 ms | 0 |
| sitting on the water at (8544, 2673) | 62–66 | 14–15 ms | 0–30 |
| on the ground at (5882, 4390) / (6371, 5303) | 19–22 | 46–53 ms | ~95 |
| on the ground at (6188, 5183) | 8–9 | 110–120 ms | ~45 (every frame) |

**The cause: the airframe's ground contact, asked at every RK4 stage.** `AirframeContact.Compute` skips its 21-odd
hard points (the Pitts: wing tips, stab tips, fin top, prop tip, tail cone, belly, cowl …) only while the airframe is
more than `ClearAboveM` = **60 m** above the highest of five ground samples — that is the threshold in the table.
Below it, every point asks for the ground (height field, cave floors, decks, micro-bumps — which needs the surface
type, three dozen region tests with string-keyed lookups into the island description — the water, the solids) at
each of RK4's four stages, 200 times a second: ~17,000 point tests a second. On the ground the gear's three wheels
do the same, and the gear had no altitude test at all, so it asked at 500 m too. Then the frame-rate cap made it
worse: a slow frame makes the sim run its catch-up limit (27.8 ms of sim, 5–6 steps) on every frame instead of 2.8,
so the work per frame doubled exactly when the frame could least afford it — `sim capped` counting every frame.

**Reproduced engine-free** (`tools/FlightTests/QuestLowAltitudeProbe`: the Pitts on the island at the log's own
coordinates, driven at 72 Hz). With `SimProfile` on, `contactPoints` went from 0 to 5–8 ms per 72 Hz frame below 60 m
on this Mac, and island height evaluations from 88 to ~320 a frame. The headset's cores run this code roughly ten
times slower than the Mac's (≈1.5 ms a step in cruise against ≈8 ms below 60 m on the device, inferred from the
log's capped frames), which is the 40 ms.

**The fix** (no flight-model change; `GroundContactCullTests` flies 52 flights — takeoff runs, a crash into rising
ground, low passes, sitting still, a float and a landplane ditching, at the logged spots, in a Pitts, a 172 and a
2-33 — with it on and off and gets **bit-identical** states):

| Change | Where |
|---|---|
| The 60 m "clear of the ground" test once per STEP, not per stage, with the wheels counted and the step's own travel added; when clear, gear and hard points are skipped on all four stages (the old per-stage shortcut returned the same zero) | `AirframeContact.ClearOfTheGround(…, wheels, …, extraM)`, `Aircraft.StepWithDeflectionTargets` |
| Below 60 m, each hard point and wheel is tested exactly on stage 1; one that is clear by more than 0.1 m + 11 × its travel in the step skips stages 2–4 (any slope up to 84°; against a steeper wall a contact can begin one 5 ms step later) | `AirframeContact.StageCullMargin`, `ContactPoint.StageClearM`, `LandingGear.Compute(…, stage, dt, stageClear)` |
| A hard point more than 0.8 m above the bare height field skips the micro-bump surface lookup (it cannot touch a bump) | `WorldTerrain.HardPointGroundAt`, `MaxMicroBumpM` |
| A wheel in the air no longer looks up the surface type | `LandingGear.Compute` |
| The cave and surface tests read their numbers once instead of by string key on every call | `IslandCavities`, `IslandTerrain.SurfaceTables` |

**Measured on the Mac, same session, before (HEAD 677e3745) and after, ms of CPU per 72 Hz frame** (the probe, the
Pitts, the island's world):

| Spot | 500 m | 50 m | 30 m | on the ground |
|---|---|---|---|---|
| (5941, 2618) spawn | 1.15 → 0.65 | 2.61 → 1.28 | 2.87 → 1.16 | 3.16 → 1.58 |
| (5882, 4390) | 1.04 → 0.63 | 2.56 → 1.16 | 2.84 → 1.20 | 2.97 → 1.51 |
| (6188, 5183) | 1.07 → 0.61 | 2.30 → 1.09 | 2.82 → 1.33 | 3.04 → 1.69 |
| (8544, 2673) sea | 0.73 → 0.56 | 1.73 → 0.84 | 2.00 → 0.84 | 1.71 → 1.09 |

Low flight costs about half what it did, cruise ~40 % less (the gear no longer asks for the ground in the air), and
the gap between 500 m and the ground is now ~1 ms on the Mac instead of ~2. **Not measured on the headset.** If the
rate there is still low near the ground, the new `top:` field says which part is left.

**Reading it on the next run** (`adb logcat -s Unity | grep QuestPerf`): the line now ends

    … | sim 3.1 ms/frame, 2.8 steps | top: step 3.0, forces 2.9, aero 1.4, contact 0.9, contactPoints 0.7 ms/frame | height 160 q 85 eval /frame

`sim` is the wall time inside `SimLoop.Advance` per frame and the steps it ran (2.8 at 72 Hz; 5–6 means the cap is
biting); `top:` the five costliest `SimProfile` scopes per frame (inclusive: `step` ⊃ `forces` ⊃ `aero`, `gear`,
`contact` ⊃ `contactClear`, `contactPoints`; `air` is the regional field's samples, `afterStep` the bookkeeping);
`height` the island height queries and the evaluations that missed the memo, per frame. Healthy near the ground:
`steps` ≈ 2.8 and `contactPoints` a small fraction of `step`.

Also fixed on that line: `gpu app` and `compositor` printed thousands of ms at 72 fps. The OpenXR display subsystem
returns them in **milliseconds** on the Quest and the code multiplied by 1000 (a222bda's "gpu app 6886.8" was VrApi's
`App=6.89ms`, "compositor 696.7" its `TW=0.70ms`).

## The stutter (Ty, 2026-11-02): "after a few minutes the frame rate gets really slow, catches up, then slows again"

Found by reading the code against the frame (no device trace was possible), and by measuring the sim headless.
Three things combine:

1. **Garbage, every frame, feeding a collector that cannot hide.** Unity's collector (Boehm, incremental) is not
   generational: a cycle walks the whole managed heap, in slices of up to 3 ms a frame, and the more garbage per
   frame the more often a cycle starts. In ordinary low flight the biggest producer was the **ground shadow**:
   `GroundShadow.LateUpdate` asked `CockpitSights.IsSight` about every airframe mesh every frame, which reads
   `Transform.name` twice per ancestor, and **every `.name` read allocates a new string** — on the order of a
   thousand strings a frame below 1,500 m. Smaller ones: an interpolated layout-key string in
   `TouchFlightControls.Update` (every frame, ~10 boxed values), OnGUI toolbars that draw to a back buffer the
   headset never shows (`ViewPanel`, `ChallengeHud`, `GameAeroPanel`, the last calling `Host.Read()` which samples
   the air and allocates a readout on every GUI event), a JNI round trip to the recorder every frame while idle, the
   Extra's two LCDs formatting their strings every frame to find nothing changed, and the forest streamer
   regrowing a fresh mesh batch (four lists, hundreds of KB) for every chunk it builds (several a second of travel).
2. **The sim fills much of the frame.** Measured on the Mac (`QuestFrameBudgetProbe`, Pitts, island, uniform air,
   72 Hz driving): **203–318 ms of CPU per sim-second (2.8–4.4 ms per 72 Hz frame) and 146 KiB allocated per
   sim-second (~730 B per 200 Hz step)**, with two other test suites running on the same Mac. The Quest's cores are
   several times slower than this Mac's, so the physics alone is plausibly well over half of a 13.9 ms frame.
3. **Then the catch-up loop turns one slow frame into several.** `SimLoop` lets a frame chase up to 1/20 s of lost
   time — ten physics steps. On a phone at 30 fps that is a frame and a half; in a headset at 72 Hz it is 3.6
   frames. A garbage-collection slice or a forest chunk made a frame late, the next frame ran up to ten steps
   (several ms more) and was late too, and so on until the collector finished: slow, catch up, slow again.

**What changed** (all code; none of it measured on the headset yet):

| Change | Where |
|---|---|
| Name tests resolved once per rebuild, not per frame | `GroundShadow.Refresh` (`_cast`) |
| Layout key as a value tuple, not a string | `TouchFlightControls.Update` |
| OnGUI toolbars return at once in a headset | `ViewPanel`, `ChallengeHud`, `GameAeroPanel` |
| Recorder polled twice a second while idle | `QuestRecorder.LateUpdate` |
| LCD strings built only when the numbers change | `CockpitDisplays.Update` |
| One reused mesh batch for every forest chunk | `ForestBuilder.BuildChunk`, `SeasideBuilder.Batch.Clear` |
| Forest stall guard = 1.5 display periods in a headset (20.8 ms at 72 Hz, not 24) | `ForestStreamer.LateUpdate` |
| **Catch-up capped at two display periods in a headset** (27.8 ms at 72 Hz: one dropped frame caught up in full, nothing longer chased); the frames where the cap bit are counted | `SimLoop.HeadsetCatchUpSec`, `FlightSimDriver.Update` |
| **Incremental GC slices of 1 ms** in a headset (restored when XR stops) | `QuestRig.StartRig` |
| **CPU and GPU performance hints: sustained-high** (`XR_EXT_performance_settings`, Unity's own feature, now enabled for Android by the build), and the runtime's performance / **thermal** notifications logged | `QuestPerformanceHints`, `QuestBuild.ConfigureXr` |
| The rig pass ran twice a frame (and a second `RigInputs` fill with no time passed zeroed the vario and turn needle); once now | `AirframeVisual.LateUpdate` |
| The display rate is read once a frame, not by every caller | `XrHeadset.CurrentRefreshHz` |

Not changed, deliberately: the sim's own ~730 B a step (the RK4 force closure and its delegate, a LINQ `Sum`, the
aero model) is the flight model's code; `QuestFrameBudgetProbe` now fails if it grows past 160 KiB per sim-second.
The physics rate (200 Hz) is unchanged. The island regional-atmosphere config does not load in the test world
("unknown airport anchor 'airport:Bench'"), so the probe measures uniform air.

**How to tell from a capture whether it is fixed** (`adb logcat -s Unity | grep QuestPerf`): `fps` steady at the
refresh rate; `frame max` under ~20 ms with `0 long`; `gc +n` small (a collection every few seconds was the old
pattern) and `heap` not climbing; `sim capped 0`. A non-zero `sim capped` that keeps counting means the frame
cannot carry the physics: that is the case for a lower sim rate in VR, which is the flight model's call. A line
`[QuestPerf] runtime: Cpu Thermal Normal->Warning` means the headset itself is throttling.

## The water's light-blue grid and the shadows (Ty's clips, build a222bda)

**The grid on the sea in the clips** was the recorder's own camera. The water shader tints the sea by how deep the
water is (`_CameraDepthTexture` minus the surface). The XR camera has a depth texture (`WaterReflection` asks for it).
The recording camera does not: a depth pass would double the recording's cost. So the water in the recording read
the XR camera's depth, which is in another projection and is foveated and tiled, as if it were its own. Wherever
that buffer held something near (the aeroplane, low over the water) the sea went shallow-turquoise in blocks: the
light-blue checker that flickers through the dark blue near 1:37 in the Great Lakes clip. The recorder now sets
`_WaterSceneDepthOff` (deep water) and `_ReflOn = 0` (the mirror image was rendered for the XR camera's projection
too) just for its own render, and puts them back (`QuestRecorder.Capture`).

**In the headset itself** the same depth term is a precision problem far out. With the cockpit's 5 cm near plane and
a 20 km far plane a 24-bit depth buffer resolves about 1.2 m at 1 km, so over a shallow seabed in the distance the
"depth" jumps by a metre between frames and between the eyes. `QuestRig` gives the shader that error
(`_WaterDepthErrK` = 1 / (near · 2²⁴)), and the shader blends the measurement into deep water from about 460 m to
1 km. Zero on every other platform, so the desktop and iPhone look is unchanged. The near plane stays 5 cm, because
the head can come close to the canopy bow.

**Shadows.** The sun casts no shadow map on any platform (`SceneBootstrap.BuildSun` leaves `Light.shadows` at
None), so there are no cascades, resolution or bias to tune. Medium's "hard shadows to 20 m" draws nothing. Every
shadow in the clips is `GroundShadow`'s planar projection of the airframe onto the ground or water plane. The
"blocky, detached shadow of the wreck" in the Pitts clip is that projection of the crumpled wreck (the spiky skin is
crash damage). It is on the water surface, 8 cm up, and offset from the wreck because the wreck floats high above it
(expected until the crash-water lane lands, item e). Turning on a real shadow map in the headset would cost a whole
shadow-caster pass per frame. It was not done; it is a rendering-quality call for Codex.

## The flickering card (Ty, 2026-11-02: "the IAC card still flickers")

The sequence card's face sits **0.1 mm** in front of its paper stock, over exactly the same area, in the Pitts, the
Extra and the Great Lakes (measured in the GLBs). The 2026-11-01 fix (`CockpitDisplays.SeparateCoplanar`) never
reached the Quest's Pitts: it ran only for a model with a rig (the Pitts has none), it took its direction from
`Eye_Rear` (the Pitts has no eye nodes), and it moved the node in **world** space, where a few kilometres from the
origin a float carries about half a millimetre — so even where it ran, a 1 mm nudge could round to nothing.
Now it runs for every placed model; the direction is toward the seats (the aeroplane's aft — every panel here
faces aft) or toward the eye point where there is no aeroplane to ask; the 1.2 mm move is made in the node's parent
frame; and the stock, which the face covers completely from the seat, is not drawn, so nothing is left behind the
face for the depth test to choose between. Both eyes see the same geometry, so the two eyes can no longer disagree.
The near plane stays 5 cm (`ChaseCamera`): at arm's length that resolves far finer than 1 mm.

## The frame

On Quest the CPU and the GPU work on different frames in a pipeline, so **each** of them has to fit in one frame
period on its own. The compositor (timewarp, lens distortion) takes about 1 ms of GPU on top of the app.

| Per frame | 72 Hz (default) | 90 Hz (opt-in) | Measured |
|---|---|---|---|
| Frame period | 13.9 ms | 11.1 ms | — |
| CPU main thread (sim, scripts, culling) | ≤ 9.0 ms | ≤ 7.0 ms | unmeasured |
| CPU render thread (draw submission) | ≤ 6.0 ms | ≤ 5.0 ms | unmeasured |
| GPU, app | ≤ 12.0 ms | ≤ 9.5 ms | unmeasured |
| GPU, compositor | ~1 ms | ~1 ms | unmeasured |
| Draw calls (multiview: counted once for both eyes) | ≤ 250 | ≤ 200 | unmeasured |
| Triangles in view | ≤ 750 k | ≤ 600 k | unmeasured |

The CPU numbers leave ~3–4 ms of slack because thermal throttling on a standalone headset is real and a frame that
fits only on a cold device does not fit after ten minutes.

72 Hz is the default because it is the Quest's own default and has a quarter more time per frame; 90 Hz is in the
VR menu (Display), persisted, requested through `XR_FB_display_refresh_rate` (`QuestDisplayRateFeature`). If the
runtime refuses a rate, the log says which rates it offers.

## Defaults set here

| Knob | Default | Where |
|---|---|---|
| Refresh rate | 72 Hz | `XrHeadset.Prefer90Hz` (VR menu, PlayerPrefs `quest.hz90`) |
| Eye-buffer scale | 1.0 (the runtime's recommended size) | `XrHeadset.RenderScale` → `XRSettings.eyeTextureResolutionScale` |
| Fixed foveation | 0.5 ("medium") | `XrHeadset.FoveationLevel` → `XRDisplaySubsystem.foveatedRenderingLevel` (legacy API, the only one the built-in pipeline has; Vulkan) |
| Stereo | Single-pass instanced = multiview | OpenXR Android settings |
| Graphics API | Vulkan only | `QuestBuild.ConfigureAndroid` |
| Quality level | Android's existing default, **Medium** (1 pixel light, hard shadows to 20 m, no MSAA) | QualitySettings — the same level the iPhone build defaults to; changing the level itself would change iOS, so a Quest-only override belongs in `QuestRig` |
| CPU / GPU level hint | Sustained high, both | `QuestPerformanceHints` (XR_EXT_performance_settings) |
| Incremental GC slice | 1 ms (Unity's default 3 ms restored when XR stops) | `QuestRig.GcSliceNs` |
| Sim catch-up | Two display periods (27.8 ms at 72 Hz) instead of 50 ms | `SimLoop.HeadsetCatchUpSec` |
| MSAA | Off (Medium has none) | **Codex**: Quest's tiled GPU resolves 4× MSAA almost for free and it is the single biggest image-quality win in a headset; it was not switched on here because rendering quality is his lane |

## What is likely to blow it

In rough order of risk:

1. **Cumulus** (`Cumulus.cs`, `Cumulus.shader`). Alpha-blended cloud volumes are overdraw, and overdraw is the
   most expensive thing a tiled mobile GPU does — twice, one per eye. Flying through or under a cloud field is
   the likeliest place to drop frames. First thing to look at if `gpu app` goes over budget.
2. **Terrain** (the world builders, `FlyingGame/Terrain`). Large meshes to the horizon: triangle count and the
   far clip (20 km on the main camera). Candidates: a shorter far plane in VR, coarser far LODs.
3. **Shadows.** Medium has hard shadows to 20 m: a whole extra pass of the shadow casters every frame. In the
   cockpit almost nothing within 20 m is worth a shadow except the aeroplane itself. Cheapest big saving if the
   GPU or the render thread is over.
4. **`WaterReflection`**: on glassy water it renders the scene a second time into a texture. In VR that pass is
   also mono (one eye's view for both), which Codex may want to fix anyway.
5. **Particles and trails**: smoke, spray, wake foam, fire pots — overdraw again, near the eye when they matter.
6. **The flight sim on the CPU**: the physics step and the bubble / lift fields run on the main thread. These are
   the same cost as on a phone, but a Quest's CPU budget per frame is tighter than an iPhone's at 60 Hz.
7. **Draw calls**: hundreds of small static objects around the airfields (urban facades, windsocks, signs). The
   built-in pipeline has no SRP batcher; static batching / GPU instancing is the lever.

Things that are already off in VR and cost nothing (the OnGUI toolbars `ViewPanel`, `ChallengeHud` and
`GameAeroPanel` return at once in a headset since 2026-11-02): every flat OnGUI overlay (HUD, gauges, touch pads, nav radios)
returns before drawing (`VrMode.SuppressFlatOverlays`); the Cardboard rig never builds its two cameras; the clip
recorder renders only while recording.

**The in-headset recorder** (A + B held, docs/quest-controls.md *Recording*) is the one thing that adds rendering
on purpose: while it runs, a mono camera renders the scene at 1280 × 720 about every other frame (30 fps of 72),
plus one full-screen YUV pack pass and an asynchronous 1.4 MB readback; the encoding is Android's hardware encoder
on Java threads. It is unmeasured. The `[QuestPerf]` line ends `rec ON (n sent, n dropped)` while it runs, so
compare a 5 s window with it on against one with it off. If it costs too much, the levers are in
`QuestRecording` (size, fps) and `QuestRecorder.Capture` (the camera's culling mask, a shorter far plane).

## Measuring

`QuestRig` logs, every 5 s:

    [QuestPerf] 71.9 fps at 72 Hz | frame max 15.1 ms, 0 long | cpu main 7.8 (max 11.2) ms | gpu app 9.4 ms | compositor 0.90 ms | dropped 0 in 5s | gc +1 heap 41 MB | sim capped 0 | scale 1.00 fov-level 0.50 | runtime none | rec off | sim 1.9 ms/frame, 2.8 steps | top: step 1.8, forces 1.7, aero 1.0, contact 0.3, gear 0.2 ms/frame | height 60 q 32 eval /frame

- `frame max` — the longest frame in the window; `long` — frames over 1.5 display periods.
- `cpu main` from `FrameTimingManager` (the Quest build switches it on); `gpu app`, `compositor`, `dropped` from
  the OpenXR display subsystem.
- `gc +n` — garbage collections started in the window; `heap` — the managed heap (`GC.GetTotalMemory`).
- `sim capped` — frames on which the sim stopped chasing real time (`FlightSimDriver.CatchUpCappedFrames`).
- `sim … ms/frame, n steps | top: …` — where the sim's time goes, per subsystem (`SimProfile`; see *Slow near the
  ground*).
- `runtime` — the last performance notice from the runtime (`Cpu Thermal Normal->Warning`, …), or `none`.
  Each notice is also its own `[QuestPerf] runtime: …` line when it happens, and the start of the flight logs
  `[Quest] performance level hint sustained-high: CPU accepted, GPU accepted`.

`[QuestPerf] hitch …` lines add every frame over 100 ms with what was going on (see *The "freeze" in the dive*).
`[Freeze]` lines (FreezeWatch) add the forest streamer's worst chunk and any frame over 300 ms. Capture with:

    adb logcat -s Unity | grep -E "\[Quest|\[QuestPerf\]"

For more detail use Meta's OVR Metrics Tool on the headset (GPU / CPU levels, stale frames, foveation), or
`adb logcat -s VrApi` for the runtime's own once-a-second line (`FPS=72/72 … GPU%=…`). Only timings are logged —
never where the aeroplane is.
