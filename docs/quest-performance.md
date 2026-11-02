# Meta Quest 3S performance budget

**Status: still unmeasured ON THE HEADSET.** Every frame number in the budget table is a budget. On 2026-11-02 the
3S was on the cable but `adb devices -l` listed it as **unauthorized** (the "Allow USB debugging" prompt has to be
accepted inside the headset), so no logcat could be read. What IS measured is the sim's cost on the Mac
(*The stutter*, below). The build logs a `[QuestPerf]` line every 5 s, so the next capture fills in the rest.

Hardware: Snapdragon XR2 Gen 2, 1832 × 1920 per eye, refresh 72 / 90 / 120 Hz. The runtime picks the recommended
eye-buffer size; `[Quest] XR on` in the log prints it.

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

    [QuestPerf] 71.9 fps at 72 Hz | frame max 15.1 ms, 0 long | cpu main 7.8 (max 11.2) ms | gpu app 9.4 ms | compositor 0.9 ms | dropped 0 in 5s | gc +1 heap 212 MB | sim capped 0 | scale 1.00 fov-level 0.50 | runtime none | rec off

- `frame max` — the longest frame in the window; `long` — frames over 1.5 display periods.
- `cpu main` from `FrameTimingManager` (the Quest build switches it on); `gpu app`, `compositor`, `dropped` from
  the OpenXR display subsystem.
- `gc +n` — garbage collections started in the window; `heap` — the managed heap (`GC.GetTotalMemory`).
- `sim capped` — frames on which the sim stopped chasing real time (`FlightSimDriver.CatchUpCappedFrames`).
- `runtime` — the last performance notice from the runtime (`Cpu Thermal Normal->Warning`, …), or `none`.
  Each notice is also its own `[QuestPerf] runtime: …` line when it happens, and the start of the flight logs
  `[Quest] performance level hint sustained-high: CPU accepted, GPU accepted`.

`[Freeze]` lines (FreezeWatch) add the forest streamer's worst chunk and any frame over 300 ms. Capture with:

    adb logcat -s Unity | grep -E "\[Quest|\[QuestPerf\]"

For more detail use Meta's OVR Metrics Tool on the headset (GPU / CPU levels, stale frames, foveation), or
`adb logcat -s VrApi` for the runtime's own once-a-second line (`FPS=72/72 … GPU%=…`). Only timings are logged —
never where the aeroplane is.
