# Meta Quest 3S performance budget

**Status: UNMEASURED.** Every number below is a budget, not a measurement. The headset was not attached to the Mac
when this was written (2026-11-02, `adb devices -l` listed nothing). The build logs a measurement line every 10 s
(see *Measuring* below), so the first run on the headset fills in the right-hand column.

Hardware: Snapdragon XR2 Gen 2, 1832 × 1920 per eye, refresh 72 / 90 / 120 Hz. The runtime picks the recommended
eye-buffer size; `[Quest] XR on` in the log prints it.

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

Things that are already off in VR and cost nothing: every flat OnGUI overlay (HUD, gauges, touch pads, nav radios)
returns before drawing (`VrMode.SuppressFlatOverlays`); the Cardboard rig never builds its two cameras; the clip
recorder renders only while recording.

**The in-headset recorder** (A + B held, docs/quest-controls.md *Recording*) is the one thing that adds rendering
on purpose: while it runs, a mono camera renders the scene at 1280 × 720 about every other frame (30 fps of 72),
plus one full-screen YUV pack pass and an asynchronous 1.4 MB readback; the encoding is Android's hardware encoder
on Java threads. It is unmeasured. The `[QuestPerf]` line ends `rec ON (n sent, n dropped)` while it runs, so
compare a 10 s window with it on against one with it off. If it costs too much, the levers are in
`QuestRecording` (size, fps) and `QuestRecorder.Capture` (the camera's culling mask, a shorter far plane).

## Measuring

`QuestRig` logs, every 10 s:

    [QuestPerf] 71.9 fps at 72 Hz | cpu main 7.8 (max 11.2) ms | gpu app 9.4 ms | compositor 0.9 ms | dropped 0 in 10s | scale 1.00 fov-level 0.50 | rec off

(`cpu main` comes from `FrameTimingManager`, which the Quest build switches on; `gpu app`, `compositor` and
`dropped` from the OpenXR display subsystem.) Capture with:

    adb logcat -s Unity | grep -E "\[Quest|\[QuestPerf\]"

For more detail use Meta's OVR Metrics Tool on the headset (GPU / CPU levels, stale frames, foveation), or
`adb logcat -s VrApi` for the runtime's own once-a-second line (`FPS=72/72 … GPU%=…`). Only timings are logged —
never where the aeroplane is.
