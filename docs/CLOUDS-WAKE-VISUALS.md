# Cloud playground visuals — lane B handoff, 2026-11-05

Uncommitted visual assets; no gate approved. No scene integration, build, install, or measured performance claim.

## Lane A integration

Add `FlyingGame.Clouds.CloudPlayground` to an independent scenery root. Assign the serialized materials
`Assets/Scripts/Clouds/CloudBillow.mat`, `CloudWisp.mat`, and `CloudAdvect.mat` to billowMaterial,
wispMaterial, and advectionMaterial respectively. Call `Build(islandCentreUnity)` after island scenery placement.
The centre's X/Z sets the layout; authored altitude bands are metres MSL in Unity Y. Assign the active flight
camera (especially the XR centre-eye camera) to viewCamera, and update ambientWind with world X/Z wind in m/s.
No automatic registration, thermal physics, visibility/LOS physics, or Glass Overlay changes are included.
Keep existing thermal-linked Cumulus clouds; avoid placing duplicate scenery banks over these new banks.

Remove WakeFieldStub.cs, or define CLOUDS_WAKEFIELD_LIVE in every player target, when the real Aero.Wake API lands.
This stub returns zero velocity/vorticity and clears its texture; there is deliberately no synthetic vortex.
Texture adapter agreement needed with lane A: linear ARGBHalf RG = signed world X/Z velocity (m/s),
B = vorticity magnitude (1/s), A = vertical velocity (m/s). UV (0,0) = region minimum X/Z, UV (1,1) = maximum.
Sample the sheet altitude at bounds centre Y; region thickness 40 m is metadata, not a vertical integral.
A different packing requires changing both the producer and CloudWispAdvect.shader.
The other API signatures match the contract, using UnityEngine.Vector3/Bounds/RenderTexture.

## Visual design and limits

27 broken horseshoe banks, 20 billowing ellipsoid impostors each: bases 350/1100/2050 m, crowns reaching roughly
2500 m; three rings at 2400/3850/5300 m from island centre. Missing sectors create radial canyons. Horseshoe
centres have roughly 245 m clearance before lobe overlap, with an open side. Placement is deterministic.
Soft spherical lighting and shaded undersides use analytic impostors, no volume raymarch or texture assets.
Secondary lobes fade between 3300–4500 m; whole banks fade at 9200–11000 m and CPU cull at 12500 m.
Impostor quads fade on penetration to prevent a hard full-screen card; these visuals do not implement cloud IFR.
Within-bank transparent lobes have fixed ordering (a known approximation); banks use Unity renderer sorting.

Thin sheets at 650/1550/2450 m: only the nearest altitude within 220 m and within 10 km of the island draws.
One camera-centred 512 m patch blends away at its boundary. Thus wisps are a local fly-through effect, not a
horizon-wide stratus deck. Density starts as stretched multiscale noise, then semi-Lagrangian backtracing uses
actual horizontal wake velocity. Vertical velocity and vorticity dilute density to expose holes. The 16 m tile
recentring reprojects prior density; new edges seed fresh wisps. This is a visual approximation, not fluid physics.
After a long inactive interval a revisited layer retains its old density; first integration should decide whether
to reseed after long pauses. Strong sub-2 m cores need producer filtering and may not be resolved by this grid.

## Budget and verification

Hard geometry cap: 540 quads / 1080 triangles across 27 bank draw calls, plus one two-triangle wisp sheet.
At distance approximately one third of lobes fade out; vertex submission stays bounded at 2160 vertices.
At most 15 simulation updates/sec, no catch-up loop: one 256² wake texture write plus one density blit per tick.
Wake generation cost belongs to lane A and is NOT included in this cap (must bound segment count/work there).
Render targets: six R8 256² density targets + one ARGBHalf 256² wake target = 896 KiB nominal, no depth/mips/MSAA.
Three small per-sheet materials, one advection material; runtime resources released on root destruction.
No per-frame mesh creation, segment lists, LINQ, or texture readback. Stereo shader macros are included.

GPU milliseconds cannot be derived from these counts: cloud overlap/fill rate is the main risk. No Quest 90 Hz
or iPhone 60 Hz result is claimed. Profile inside a bank, along a canyon, skimming a sheet with maximum wake
segments, and from above with all banks visible, both XR eyes and iPhone. Targets are total frames <=11.11 ms
Quest / <=16.67 ms iPhone; measure cloud GPU delta separately and trim overlap/visible radius if needed.

`tools/check-parity.sh --strict`: PARITY OK for current playable branch tips; it does not validate these uncommitted
assets. Shared visual source is identical for every target. `git diff --check`: clean. Unity compilation, shader
compilation, runtime screenshots, stereo QA and device timing remain lane A gates. Process listing is denied in
this sandbox, so lane B cannot establish an idle build slot and did not launch Unity/xcodebuild.
No renders generated. Requested render destination is outside this lane's permitted writable roots; lane A must
save actual Unity renders to ~/Developer/agent-lanes/renders/clouds-wake/ before seeking Ty's render approval.

## Integration (lane A, Claude, 2026-11-05)

Committed from lane/codex-clouds as ca9c48f7 (Codex's files unchanged), merged into lane/claude-wake-vortex, then:

- **Stub gone.** `WakeFieldStub.cs` deleted; `Aero.Wake.WakeField` is the real one (Bridge/Wake/WakeField.cs,
  docs/WAKE-VORTEX.md). `WriteToTexture` keeps Codex's packing exactly (RG = X/Z velocity, B = |ω|, A = Y velocity,
  linear ARGBHalf, UV (0,0) = region min X/Z, sheet at bounds centre Y) and is evaluated on the GPU
  (`Hidden/FlyingGame/WakeFieldBake`) from the 64 strongest segments nearest the sheet plus 16 slipstream pieces.
  A vortex core (0.5–1.5 m) is still under the 2 m texel: thin cores alias into a line of holes, which reads well.
- **Moved into the Bridge** so the game can build it: `Scripts/Bridge/Clouds/CloudPlayground.cs`, materials in
  `Resources/Clouds/` (GUIDs kept). `IslandWorldBuilder.Build` calls `CloudPlayground.Attach`. The camera is
  `Camera.main` (the XR camera on the Quest); the wind is the atmosphere's mean wind at the eye.
- **Banks placed for this island.** It is ~30 km across and 2.4 km high within ~8 km of the centre, so the original
  rings (2.4/3.85/5.3 km, bases 350/1100/2050 m) stood inside the mountains. Now an amphitheatre: 2050 m at 6 km
  (12 sectors), 1100 m at 11 km (16), 450 m at 16.5 km over the coast and sea (20), one sector in four left open, radius
  jittered ±0.9 km, and every bank lifted to 200 m above the highest ground under it. 36 banks, 720 quads, 36 draws.
  Whole-bank fade 15–18 km, CPU cull 19 km, secondary lobes 6–7.5 km.
- **Layers visible from afar.** `FlyingGame/CloudWispFar`: one 44 km quad per layer with the patch's own seed and the
  same wind drift (`_WispDrift`, also added to the advection seed), handing over to the wake-reactive patch with
  exactly the patch's edge fade (`_WispPatch`). Before this the layers existed only within 220 m of their height.
- `StepForTesting` / `WakeTextureForTesting` / `DensityForTesting` for the render tool (Editor/CloudWakeRender.cs);
  `OnDestroy` uses DestroyImmediate outside play mode.
