# Island terrain, rebuilt (Claude, lane/claude-island-terrain, 2026-11-02)

Ty, after flying the island in the F-86: *"The island is pretty bad … I saw no snow at all and no glaciers and
codex landscape was crude and blocky."* This is the rebuild of the island's ground. The layout is untouched
except where listed under **What moved**. Nothing here is approved; Ty decides from the renders in
`~/Developer/agent-lanes/aero-playground/island-terrain-renders/`.

## 1. What the shipping terrain was, measured

* **Shape.** A coast ramp from 30 ft to a flat 4,600 ft table 2 km inland, radial cones for the peaks and 0.5 km
  ribbons for the crests, then the authored layers (pads, lakes, canyons, lesson boxes) — and then **136
  clearance caps** applied as `min()`: 23 IFR airway swaths (2 nm half-width, MEA − 2,025 ft), the 12 published
  approaches (7.4 km at 20:1/34:1, 30 m margin) **and a 7.4 km 20:1 fan off both ends of every one of the 101
  runway/strip/beach ends**. Those fans covered most of the interior; every place a cap cut the table it left a
  dead-flat trench with vertical walls. 18.4 % of the land was flat to within 0.5°.
* **Why there was no snow and no glaciers.**
  1. The rendering (IslandWorldBuilder) drew 100 m flat-shaded cells, one vertex colour per cell, with snow only
     as a height ramp 2,000 → 2,600 m; FlyingGame/Terrain (the shader with the slope/aspect snowline) was never
     bound on the island.
  2. More fundamentally the *ground* could not hold any: the caps had cut the mountains down. The Knik and Ruth
     glacier heads (6,000 / 7,300 ft) and the whole Chugach–Denali crest lie inside the A2 airway swath
     (MEA 5,000 ft → cap 2,975 ft); Frank Church and Sierra lay inside the 7.4 km fans of AP7 and AP10. What
     survived was the summit "reassertion": every authored summit put back as a 240 m flat-topped disc on a
     needle — Denali-like was a 2,000 m pillar with 619 m ground 500 m from its top, and the Bighorn lakes sat on
     rock columns (see `before/13`, `before/20`, `before/22`).
* **Mesh:** 141 chunks, 482,832 triangles, 1,448,496 un-shared vertices, 61 MiB (editor, CPU+GPU), FlyingGame/LitVC.

## 2. My earlier landscape, and what is reused

The legacy plateau world, mostly my commits (session `01TTWftnBQ3cWf9hAxMMRsdn`): d205c14 (Denali, Knik/Colony
glaciers, icefall + 2.8° tongue, braided river), f84380e, f7e17d2, fc248ec (Cascades lakes), 1267cdd (forest as a
function, treeline by height, gullies/spurs/fans/couloirs, snow slope cutoff), 777ade5, 46f7cb2, 36e03d0, e06fcf7
(Massenerhebung), 2b4dd70 (glacier ski zones); code in AlaskaRange.cs, AlpineRange.cs, Forest.cs,
WorldTerrain.LegacyHeightAt; finished by Codex's Terrain.shader passes. It had no generator script or baked
heightmap — every landform was an analytic function. What carries over: the forest-as-a-function (Forest.cs,
now with the island's treeline), the per-pixel idea of Terrain.shader (slope/aspect snow with a slope cutoff,
filtered simplex noise, surface-gradient detail normals, the exponential-atmosphere haze), and the glacier
anatomy. The landforms themselves are new: hand-written analytic gullies do not make drainage networks.

## 3. Climate assumed

A temperate maritime island in the westerlies. Closest real analogue: the **Olympic Mountains** (summits near
2,400 m, glaciers on the shaded faces, rainforest on the windward side, a semi-arid rain shadow a few tens of km
downwind) — at a smaller scale, so the gradients are steeper, as on any high island (Hawai'i's windward/leeward
rainfall changes 10x within 10 km).

| Quantity | Value | Why |
|---|---|---|
| Moist flow | from **310°** true (v1: 280°) | the storm track of the Gulf of Alaska coasts the north-west areas stand for (§11); keeps the dry shadow over the authored red-rock desert (Page/Powell/Grand Canyon, SE) |
| Background precipitation | 2.2 m/yr at the windward coast | wet temperate coast (Hoquiam 2.0, Hokitika 2.9) |
| Orographic precipitation | **30** m/yr per unit upslope (v1: 14; why in §11.4), carried 4 km downwind; lee evaporation 0.7 of the same; depletion e-folds after 2.6 km of cumulative lift | Smith (2003) linear upslope model reduced to 1-D; v2 gives 7.5 m/yr on the compacted Fairweather, 7–9 m/yr on the windward central massif, 2–2.6 m/yr at Johnson Creek, 0.5–1.3 m/yr in the SE desert |
| Summer (JJA) mean at sea level | 18 °C | warm maritime summer (Seattle 17.5, Port Angeles 15, Victoria 16 — warmer inland) |
| Lapse rate | 6.5 K/km | standard |
| Equilibrium-line altitude | T_JJA(ELA) = 0.6 P / (4 mm/°C/day × 92 days) = 1.63 P | degree-day balance: winter snow (60 % of precipitation) = summer melt |
| → ELA | v2: ~620 m at 7.5 m/yr (Fairweather, central massif windward), 1,470 m at 5.2, 2,220 m at 2.2; above every summit in the desert | Fairweather Range 900–1,100 m; Westland (Franz Josef) glaciers end in rainforest at 300 m under a ~1,800 m ELA |
| Treeline | 1,460 m (8.5 °C JJA isotherm), ±130 m with aspect, ±35 m wander | warmest-season isotherm of 8–10 °C (Körner); the 25 km island is 0.2° of latitude, so the legacy "falls going north" lapse is gone |
| Forest water limit | none below 0.55 m/yr, full above 0.95 m/yr | conifer woodland/steppe boundary |
| Snow shown | **early summer**: continuous above (local ELA − 220 m), 160 m lower on a steep north face and higher on a south one, patches in wet shaded hollows down to 400 m below that; nothing stays on ~>50° | what a pilot sees in late June over a maritime range (Paradise, Mt Rainier, 1,650 m, is snow-covered into July) |

The ELA is stored per lattice node (the shader and Forest.cs read it back), so snow, glaciers and forest come from
one precipitation field.

## 4. Method

### 4.1 One source of truth
`IslandHeightIndex.Evaluate` (and its reference twin and the Python evaluator) used to start from a coast ramp +
regional envelope + crest ribbons + radial peaks. It now starts from **`IslandRelief.Height(p)`** and then runs
every authored layer exactly as before (lakes, canyon floors, lesson boxes and headwalls, AB3 bench, Drake and
pirate cave roofs, MOA terrace, combat ridges, hazard knoll, clearance caps, mesa pads, field pads, summit
cores). `HeightAt`, `HeightAndGradientAt` (the analytic normal, regenerated by `generate_gradient.py`), the chart
raster, contact and the mesh all read that one function. The 100 m review bake is still only evidence.

`IslandRelief` holds two lattices (main island 840 × 752, Catalina 217 × 157) at **25 m**, heights at 5 cm.
Between nodes the surface is **Catmull-Rom bicubic** — through every node, C1 everywhere, so the analytic normal
has no jump at a cell edge. A query reads a fixed 4 × 4 block: bounded, allocation-free. Four byte channels ride
on the same lattice: glacier ice, debris, wetness (log discharge), local ELA. Outer islands keep their analytic
heights.

The data is `src/FlyingGame.Core/IslandRelief.Generated.cs` (1.64 MB of base64: planar-predictor varint heights,
run-length channels; no StreamingAssets, no runtime file I/O). `TerrainGen check` regenerates it from scratch and
compares byte for byte; `docs/pending/tools/island_relief.py` decodes it for the Python evaluator.

### 4.2 The generator (`tools/island/TerrainGen`, C# .NET 8, ~2 min)
1. **Constraints** (`Constraints.cs`) mirror every runtime layer as equalities (pads with shoulders, lakes, mesa
   pads, ski zones, box floors, headwalls, AB3, T4 knoll, Drake crown) and inequalities (cave roofs from below;
   clearance caps — instrument finals with their guard and 30 m margin, visual ends with 10 m, airways — lesson
   saddles, canyon floors, and charted wires under airway swaths from above).
2. **Physical bounds.** A 16-neighbour distance envelope gives the highest ground that can stand beside a ceiling at
   a 42° mountainside (60° as an absolute cliff limit) and beside the sea at 55°. Anchors get a supporting bench
   (18 % for 200 m, then 42°); alpine tarns get rising cirque rims. Where an anchor and a ceiling are too close for
   any of that, **the ceiling wins in the relief** and the anchor stands on its own footprint.
3. **Summits**: authored position; height = authored, unless the bounds forbid it (then the highest ground that can
   stand there), and never below the ground its neighbouring anchors hold up.
4. **Macro surface**: a harmonic membrane through the anchors (maximum principle: no overshoot; a thin plate sagged
   to the coastal floor between strips), plus massifs for summits (r 1–3.2 km, smoothstep shoulders) and ridges
   along the authored crests.
5. **Erosion**, 300 steps of 2,000 yr: priority-flood routing (Barnes 2014) with slope-weighted stochastic receivers
   (Rho8; plain D8 makes grid-aligned parallel gullies), discharge = upstream sum of precipitation, implicit stream
   power (Braun & Willett 2013, m = 0.5, n = 1, K = 2.2e-5 ± 60 % lithology; red-rock country soft with resistant
   caprock benches at the two mesa-pad levels and every 120 m), creep D = 0.04 m²/yr, talus at 42°, uplift relaxation
   toward the macro. Initial roughness scales with macro *slope*, so the 1,450 m upland stays an upland.
6. **Restore the macro** at low frequency (h + blur(M − h), σ ≈ 0.5 km), lowland hummocks (10–60 m), 30 more
   erosion steps, feathered aprons round fixed surfaces, soft clamp under every ceiling (no flat cut planes).
7. **Glaciers** (`Glaciers.cs`): balance 0.7 m w.e. per 100 m about the aspect-shifted local ELA, capped +2.0;
   ground steeper than 40° sheds its snow downslope (avalanche feeding); ice flux routed downhill (multiple-flow) until
   the cumulative balance runs out — that sets each terminus; thickness from the shallow-ice approximation with
   sliding (A = 2.4e-24 Pa⁻³ s⁻¹, n = 3, factor 2), capped at 120 m; ice fills its valley to its own surface;
   lateral/end moraines (≤14 m) outside ablation-zone ice; debris cover thickening down-glacier.
8. **Antialias** (two binomial passes on free nodes) so the bicubic surface and the triangulated mesh agree.

### 4.3 The mesh (`IslandTerrainMesh.cs`)
2 km chunks; each a `LODGroup` of 25 / 50 / 100 / 200 m grids; the level changes where its 95th-percentile error
(measured per chunk against the 25 m surface) falls under 2.5 px on the iPad mini's 1,488 px height (then ×
lodBias 0.7 on iOS Medium). Skirts deeper than each level's error. Shared vertices with the analytic normal (same at
every LOD, so a level change does not change the lighting). 28-byte vertices (float3, snorm8×4 normal, unorm8×4
channels, half4). Vertices only where triangles use them. 5 m cells only round the cave mouths (the shipping
PortalHole/skylight rule, with fan stitching).

### 4.4 The shader (`FlyingGame/IslandTerrain`)
Per pixel: meadow ↔ dry grass by precipitation; heath above the treeline; conifer canopy from the forest
channel (so forest reads as forest at altitude where no trees are drawn); bare rock above ~50° in vegetation and
~40° in the alpine, more of it the higher above the treeline; joints and bedding on near-vertical faces; scree on
debris and below cliffs; red-rock strata in the desert; beach sand by height and slope; gravel and turf from the
surface channels; the snowline of §3; glacier ice with crevasses on steep ice and dark debris; filtered simplex
noise at three scales; a surface-gradient detail normal that fades out with distance; the legacy exponential-
atmosphere haze. Same reflection-pass handling as Terrain.shader.

## 5. What the relief came out as

* Summits (authored → now): Frank Church 9,020 → **8,072 ft**; Sierra 7,550 → **7,329**; Chugach 6,890 →
  **5,915**; Denali-like 8,530 → **2,176**; Mokuleia 2,950, Catalina 2,100, Drake Hill 5,750 unchanged. Each lowered
  summit is the highest ground that can physically stand at its authored position next to the clearance surfaces
  and the sea (spec `changes` ledger has the reason for each; `tools/island/sync_summits.py` keeps the table equal
  to the ground). Denali-like is the big one: its summit is 0.45 km from the north coast (a 55° sea-cliff face reaches
  ~650 m there), and it was a 2,000 m needle (`before/22`).
* Glaciers: **10.5 km²** of ice on the central massif (Frank Church, Sierra, the Bighorn cirques), volume
  0.69 km³, thickest 120 m, accumulation-area ratio 0.54 (real glaciers: 0.5–0.7), lowest ice at 1,025 m.
* Drainage: dendritic river networks to the coast everywhere (`after/relief-hillshade-with-ice.png`), Middle Fork
  upland at strip level, rain-shadow badlands with caprock benches round Powell/Page.
* Mesh vs sim (LOD0 triangles vs the continuous HeightAt, 2.18 M samples over land): median 0.24 m, 95 % 2.5 m,
  99 % 5.7 m; on every runway/strip surface ≤ 0.17 m. The maximum (184 m) is a Bighorn tarn wall (§7).

## 6. What moved

Nothing in the KEEP list moved: every airport, runway, strip, pad, lake (position and level), water lane, cave,
mesa pad, beach strip, outer island, nav-aid site, spawn/start, lesson box, combat zone and aerobatic box is where
and at the height it was. IFR data (`ifr` in the spec) is byte-identical. What changed, all through the spec's own
ledger (`changes`, `deleted_features`, `revision_audit.final_world_sha256`):

1. **Visual-only runway-end protection** (`generate_island.runway_finals`, 101 ends): 7.408 km at 20:1 with a
   0.25 km guard and 30 m margin → **FAA Part 77 visual approach surface: 20:1 for 5,000 ft (1.524 km) from the
   runway end, at its elevation, 10 m margin, 0.1 km blend**. The 12 published instrument approaches and all 23
   airway swaths are unchanged. Consequence: the combat zones' *active* polygons (zone minus protected approach
   areas, derived) are larger where those fans used to cut them; regenerated in `IslandData.Generated.cs`.
2. **Canyon C2 deleted** (its cut and its stream ribbon): a polyline through all nine Middle Fork strips with one
   linear floor 300–1,200 ft below each — water on it ran uphill between strips.
3. **Four summit heights** (§5).
4. **Glacier surfaces for the gear**: `SurfaceAt` returns Snow on generated ice (ice fraction > 0.5) instead of
   the 200 m corridors along the Knik/Ruth polylines; the two ski zones stay Snow and stay exactly flat at 1,100 /
   1,300 ft.
5. **Treeline**: Forest.cs now uses the island's climate (§3) — the legacy north-south lapse and alpine mass lift no
   longer apply. Forest thins where precipitation is under 0.95 m/yr.

The chart (`ChartRaster`) draws from `HeightAt`, so it shows the new relief with no code change.

## 7. Conflicts in the authored layout (cannot be fixed by terrain)

`after/anchor-ceiling-conflicts.png`. Where two authored things cannot both be true the terrain shows a cliff:
* the six **Bighorn lakes** (6,400–7,840 ft) sit within a kilometre of approach surfaces of KMYL, AP10, AP11
  (strips at 4,000–5,000 ft) — some stand on short rock walls;
* the **L2 lesson box** (floor 5,500 ft, headwall 6,950 ft) lies 0.3 km inside the A2 airway swath (cap 2,975 ft)
  and next to canyon C4's sea-level floor;
* **KMEV** (4,724 ft) is 1.7 km from C4's sea-level floor.
In total 25 km² of anchor support is steeper than 42° and 0.9 km² is above a runtime ceiling.
**Knik and Ruth cannot be real glaciers here**: the whole west side lies under the A2 swath (MEA 5,000 ft → no
ground above 2,975 ft within 2 nm), PATK's NDB-2 final and canyon C4, so no accumulation basin above any plausible
ELA can feed ice down to 1,100–1,300 ft. The ski zones are kept exactly (flat, Snow, landable); the ice the climate
makes is on the central massif.

## 8. Performance (editor measurements; no device was deployed to)

| | Shipping (fa1fa15) | Rebuilt | Budget |
|---|---|---|---|
| Terrain renderers | 141 (no LOD) | 143 chunks × 4 LODs, ≤ 143 drawn | ≤ 150 draws |
| Triangles drawn, overview / 25,000 ft | 467k – 483k | 45k – 79k | ≤ 250k |
| Triangles drawn, low level / 1,000 ft AGL | 142k – 446k | 51k – 199k | ≤ 250k |
| Triangles, all levels resident | 482,832 | 1,753,628 (LOD0 1,324,916) | — |
| Vertices | 1,448,496 (un-shared) | 995,898 (shared, 28 B) | — |
| Terrain mesh memory | 61.0 MiB (editor, CPU+GPU) | ~37 MiB GPU (vertices × 28 B + 16-bit indices); editor reports 73 MiB | ≤ 40 MiB GPU |
| Relief data | — | 1.23 MB encoded (1.64 MB source); 2.6 MB of float heights + 2.7 MB channels in memory | ≤ 8 MB |
| World build in the editor | 3.7 s | ~7 s (terrain sampling ~3.5–4 s, parallel) | device unmeasured |
| Height query (IslandCost, random) | 0.9 µs | 0.6 µs, allocation-free | ≤ 1 µs |
| Fresh sample near origin | 0.365 µs | 0.215 µs | |
| Fragment shader | LitVC: one N·L, ~30 ALU | IslandTerrain: 3 noise lookups always, +2 in red rock, +1 on ice, +2 on walls; ~250–450 scalar ops (source count, not compiled) | below legacy Terrain.shader |

Per-view figures: `view-stats.txt` in each render folder. Budget reasoning: an iPad mini 6 (A15) draws a few million
triangles a frame at 60 Hz in simple shaders; the aircraft, forest and scenery need most of it, so the ground gets
≤ 250k and ≤ 150 draw calls (the shipping island used up to 483k and 141). The shader cost and load time on a
device are the two numbers still to measure on hardware.

**v2 (real topography), same editor measurements** (`after/mesh-stats.txt`, `view-stats.txt`): 143 chunks × 4
LODs, LOD0 1,324,888 triangles, all levels 1,753,596, 995,882 shared vertices, ~37.2 MiB GPU (unchanged within
0.01 %: the lattice and the LOD rule are the same); worst view 143 draws / 215k triangles (37, low over the Knik
valley), all views ≤ 250k; world build 6.6 s in the editor (sampling 3.0 s). Relief data 1.42 MB encoded
(v1 1.24: the ice/ELA channels carry more structure). Height query (TerrainGen bench, DOTNET_TieredCompilation=0,
**taken while other lanes' builds were loading the machine**): HeightAt fresh 0.67 µs, HeightAndGradientAt
1.32 µs, Channels 0.03 µs — query cost depends only on the lattice layout, which v2 did not change; re-measure on
a quiet machine before quoting. Shader: v2 adds one branch-free lerp chain for the tropical region and no
texture or noise lookups.

## 9. Tests, checks, build

**Full FlightTests suite** (in-process xUnit TestRunner, frozen copies, run alone behind the guard):

| Run | Passed | Failed | Skipped |
|---|---|---|---|
| Baseline fa1fa15 (shipping island) | 1,024 | 210 | 3 |
| v2, real topography (d803150) | 1,068 | 166 | 3 |

Unique failing names in v2 that are not in the baseline: 6 —
`NavigationTests.TwoStationsAllTheWayDownTheCorridor`, `SlopeAndThermalTests.WindStrengthensWithHeightAndSoDoesTheRidgeLift`,
`ReversibleControlTests.HandsOffHoldsTrimAndReturnsAfterAPull(p51d-like)` and
`ReversibleControlTests.TailslideSlamsFreeControlsToTheStops(extra-300 / pa18-cub / pitts-s2b)`. Each re-run alone
(same frozen binaries, guard clear): the four `ReversibleControlTests` cases **pass** (suite coupling, CLAUDE.md);
**two fail alone and are caused by the new relief**: `TwoStationsAllTheWayDownTheCorridor` ("loses its cross fix
at y 1000 and 750 m": the legacy VOR corridor along sim x = 400 m now crosses the Johnson Creek/Frank Church
ridges, which shadow a station at 2,000 ft AGL) and `WindStrengthensWithHeightAndSoDoesTheRidgeLift` (evaluates
the legacy escarpment's face at sim x = 0, where the island's ground is now the Johnson Creek template, not a
uniform face). Both assert the **legacy plateau world's** geometry (Navaids.Vors, WorldTerrain.EdgeMeanY) on
whatever ground is active; they need rewriting for the island, which I have not done — listed, not hidden.
No test source was changed in v2; no test hard-codes the new summit, glacier or climate values.

**Checks (v2):** `TerrainGen check` byte-identical; `generate_runtime.py --check` matches; `verify_island.py
--rebuild` regenerates the review rasters byte-identically and then stops at a pre-existing assertion
("Claude-owned source changed: src/FlyingGame.Core/Orography.cs" — a stale hash; the file is untouched since
before this lane); `check_parity.py` max error 2.2e-12 m (PASS); `IslandCheck` PASS (554 checks);
`IslandCheck continuity` PASS (53,656 probes).

**Build (v2):** the editor render at d803150 compiled the Unity sources with 0 `error CS` (build/render-after2.log,
WorldRender wrote 38 plates). The Unity iOS export + xcodebuild was queued behind the guard for the rest of the
run; result: BUILD_STATUS

## 11. Real-topo inspiration (v2, follow-up run 2026-11-02)

Ty's direction: shape six areas on real topography, compacted to fit, with real slopes; take the palette and the
distribution of materials from satellite imagery of the same places; Mount Fairweather replaces the Ruth Glacier.
The v1 infrastructure (generator, lattice, runtime relief, mesh, shader, render tool, tests) is unchanged in
kind; what changed is the macro shape in six windows, the climate's storm track and orographic coefficient, the
ELA channel's range, the palette, and two material regions.

### 11.1 Sources and licences (raw downloads stay in `build/topo-ref/`, git-ignored)

| What | Source | Licence |
|---|---|---|
| Elevation | AWS Open Data *Terrain Tiles*, Terrarium PNG, `https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png`, zoom 11–13 (~20–40 m); underlying data USGS 3DEP/NED (US incl. Alaska and Hawai'i), SRTM elsewhere | public domain (USGS); attribution "Mapzen, USGS 3DEP" |
| Imagery | Sentinel-2 L2A (surface reflectance) COGs via the Earth Search STAC API `https://earth-search.aws.element84.com/v1`, one clear summer scene per place: Sitka `S2B_8VMJ_20200913_1_L2A` (2020-09-13, 0 % cloud); Fairweather `S2B_8VLL_20230822_0_L2A` (2023-08-22, 8.9 % scene / 5.8 % window); Grand Canyon `S2A_12S{UE,UF,VE,VF}_20230805_0_L2A` (2023-08-05); Dillingham `S2A_4Q{EJ,EK}_20200930_1_L2A` (2020-09-30); Knik `S2B_{6VUP,6VVP}_20200911_1_L2A` (2020-09-11, window 0 % cloud); Johnson Creek `S2B_11TPK_20200715_1_L2A` (2020-07-15). Item URLs are in `palette.json` | Copernicus Sentinel data, free and open; "Contains modified Copernicus Sentinel data 2020–2023" |

Committed, small, deterministic artefacts (each from a committed script): `tools/island/topo/templates.json` +
`templates.bin` (108 KB, `dem.py`), `palette.json` (28 KB, `imagery.py`, pinned scene IDs), `distribution.json`
(`distribution.py`), `fidelity.json` (`fidelity.py`). `shader_palette.py` turns `palette.json` into the shader's
constants; `plates.py` makes the comparison plates.

### 11.2 How a real place becomes an island area

`dem.py`: island point (e, n) → real point = anchor_real + R(−rot)·(e − e_a, n − n_a)·**c**; template height =
(z_real − z_ref)/**c** + z_island. One factor c horizontally *and* vertically, so slopes, valley width/depth
ratios and the hypsometric curve are the real ones (`templates.json` records the real and template slope
p50/p90: identical by construction). `Topo.cs` makes the template the macro surface inside its window
(feathered 0.6 km), *under* every authored constraint, and the erosion then only adds texture finer than ~100 m
(initial roughness ×0.15, restoration σ ≈ 60 m instead of 0.5 km). Authored summits and crests the area does not
own keep their massif (template fades out within 1.25 km): Frank Church, Sierra, Chugach, Drake Hill, Mokuleia.

| Area | Real anchor → island | c | Rotation | Datum | Why this placement |
|---|---|---|---|---|---|
| Sitka (Baranof shore, Verstovia, Indian River) | Sitka town shore 57.053 N 135.335 W → west coast (−10.9, 2.4) | 2.2 | real NE → island E (+45°) | sea level = sea level | the coast between Point Mac and PATK under the A2 airway (ground ≤ 2,975 ft): Sitka's 1,000–1,375 m peaks become 450–625 m |
| Mount Fairweather | summit 58.906 N 137.526 W → (−0.5, 6.3) | 2.6 | real NW → island SW (−90°) | real 560 m → sea level (the 25 km piedmont foreland is cut out) | the one place near the north coast where ~1,600 m can stand clear of the A2 swath and the PHDH 8 visual surface; the real NW valley-glacier system runs toward the Ruth/Fairweather zone |
| Knik Glacier / Knik River | terminus 61.415 N 148.575 W → head of canyon C4 (−4.6, −0.9) | 4.0 | −18° (valley down C4) | 40 m → 30 m | the braided valley is C4 (1.2 km floor), the glacier face at its head |
| Grand Canyon (Bright Angel / Phantom Ranch) | river 36.101 N 112.095 W → (5.0, −2.5) | 5.0 | 180° | South Rim 2,150 m → 1,330 m (the plateau strips, KPGA 4,317 ft) | the red-rock plateau between Bullfrog, KPGA and the Page mesas; a 270 m gorge with its side canyons and buttes |
| Dillingham / Mokuleia (Oahu) | PHDH runway centre → PHDH (4.99, 5.9) | **1.0** (true scale) | +12° (runway on runway) | sea level | the real runway is 9,007 ft, as the island's is; the ridge behind it at its real distance |
| Johnson Creek (3U2), Frank Church | 3U2 → 3U2 (1.82, −0.76) | 2.0 | +9° (valley along canyon C1) | 1,504 m → 1,512 m (strip on strip) | the Middle Fork upland; ridges 1,500–2,080 m on the template |

### 11.3 How true the areas stayed (`fidelity.py`, inner window, land only)

| Area | c | Land km² | Slope p50 / p90, real → island (°) | Hypsometric integral real → island | Relief real → island (m) | Within 25 m / 75 m of the template |
|---|---|---|---|---|---|---|
| fairweather | 2.6 | 12.6 | 26.9 / 49.0 → 32.8 / 61.1 | 0.494 → 0.57 | 305–1571 → 138–1581 | 21% / 48% |
| knik | 4.0 | 9.6 | 6.8 / 30.1 → 42.3 / 60.4 | 0.169 → 0.274 | 27–424 → 30–2118 | 14% / 23% |
| sitka | 2.2 | 13.0 | 28.6 / 49.8 → 13.4 / 42.2 | 0.371 → 0.255 | 0–525 → 1–497 | 40% / 67% |
| grandcanyon | 5.0 | 14.0 | 21.6 / 40.1 → 21.0 / 56.6 | 0.466 → 0.797 | 1042–1381 → 2–1364 | 40% / 65% |
| dillingham | 1.0 | 11.9 | 8.1 / 31.5 → 33.9 / 52.3 | 0.246 → 0.325 | 1–518 → 1–1009 | 27% / 42% |
| johnsoncreek | 2.0 | 5.8 | 23.2 / 33.5 → 15.4 / 57.1 | 0.375 → 0.308 | 1490–2015 → 1040–2430 | 7% / 13% |

Where an area is far from its template it is because an authored constraint holds it: Knik's east half is the
KMEV plateau (4,724 ft) and the L2 lesson box, which stand where the real glacier tongue lies; Johnson Creek's
ridges are held ~200 m under the template by the Middle Fork strips' visual surfaces and Frank Church's own
massif; Dillingham's ridge is the authored Mokuleia crest (2,950 ft) where the real ridge is 400–580 m; Sitka
and Fairweather are trimmed by the A2 cap (895 m) and, at Fairweather, by the 55° sea-cliff limit. Higher slope
percentiles on the island (p90 50–60°) are the pad benches and those trims, not the real places.

### 11.4 Climate change for Fairweather, and what it did

With v1's climate the compacted Fairweather received 3.6 m/yr (ELA ≈ 1.87 km, above its 1,581 m summit): no
ice. Lowering the summer temperature instead (real Sitka/Yakutat summers are 11–12.5 °C) would have put the
treeline at ~700 m and taken the forest off Johnson Creek and the Grand Canyon rim, so the precipitation had to
do it, as it does at Fairweather and in Westland: storm track 310° (onshore on the north-west coast, as the Gulf
of Alaska's is at Sitka, Fairweather and the Knik) and 30 m/yr per unit upslope (§3). Result: **38.2 km² of
ice** (v1 10.0), 2.50 km³, accumulation-area ratio 0.62, ice from the Fairweather and central-massif icefields
down to **tidewater on the north coast** (lowest ice 8 m at (0.10, 7.58)) and to the Knik face at the head of C4
(30 m). The icefield west of the summit is held flat at 895 m by the A2 airway cap (MEA 5,000 ft − 2,025 ft).
Side effect: the windward central massif is now icefield too; KMYL, Payette, AP1, AP11 and the Bighorn lakes
are fixed surfaces and stay clear, but the ground round them is ice.

### 11.5 Ski zones

* **Ruth → Fairweather, converted, not moved.** Same centre (−3.9, 4.7), 1,300 ft, 450 × 100 m, still Snow and
  flat (locked decision 7: 1,000–1,300 ft). Its glacier entry is renamed Fairweather and its path now follows the
  icefield's south-west outlet, whose lobe ends 0.6 km SSE of the zone at ~1,400 ft. No generated ice is on the
  zone itself at 1,300 ft: the only ice at 1,000–1,300 ft anywhere is that lobe's snout, too steep and narrow for
  450 m of runway. Putting the zone on the icefield (2,936 ft) would break decision 7; Ty decides.
* **Knik unchanged** (−6.2, 0.1), 1,100 ft. The real-looking glacier face is 1.6 km up-valley at the C4 head.

### 11.6 Palette from the imagery (`shader_palette.py`)

Every material colour in `FlyingGame/IslandTerrain` is now the median Sentinel-2 surface reflectance of a
land-cover class at the real places, sRGB-encoded (the project renders in gamma space and the shader's full-sun
light is ~1.0, so reflectance 1.0 is white paper). v1's colours were 20–60 % brighter and more saturated
(e.g. red rock .62/.36/.22 → .48/.41/.35 median, banded .40–.55; glacier ice .86/.91/.95 → bare summer ice
.62–.70; meadow .25/.37/.17 → .20/.26/.15–.31/.30/.26).

| Shader material | sRGB albedo | Reflectance (R G B) | Class @ areas |
|---|---|---|---|
| conifer_wet | `float3(0.13, 0.18, 0.13)` | 0.015 0.027 0.016 | conifer_forest @ sitka,fairweather |
| conifer_dry | `float3(0.20, 0.23, 0.18)` | 0.034 0.043 0.026 | conifer_forest @ johnsoncreek,knik |
| broadleaf | `float3(0.18, 0.25, 0.15)` | 0.029 0.050 0.020 | broadleaf_tropical_veg @ dillingham |
| meadow | `float3(0.31, 0.30, 0.26)` | 0.077 0.073 0.053 | grass_meadow @ sitka,johnsoncreek,knik |
| dry_grass | `float3(0.40, 0.34, 0.29)` | 0.132 0.095 0.067 | dry_grass_shrub @ grandcanyon,dillingham |
| heath | `float3(0.30, 0.31, 0.28)` | 0.072 0.079 0.065 | grass_meadow @ fairweather |
| rock_crystalline | `float3(0.34, 0.33, 0.32)` | 0.095 0.091 0.081 | bare_rock @ fairweather,knik |
| rock_light | `float3(0.49, 0.45, 0.39)` | 0.205 0.169 0.123 | bare_rock @ grandcanyon |
| red_rock | `float3(0.48, 0.41, 0.35)` | 0.192 0.137 0.099 | red_rock @ grandcanyon |
| gravel_bars | `float3(0.34, 0.34, 0.33)` | 0.097 0.097 0.090 | sediment_gravel @ knik,fairweather |
| sand | `float3(0.58, 0.53, 0.46)` | 0.291 0.243 0.183 | sand @ dillingham |
| snow | `float3(0.86, 0.88, 0.88)` | 0.707 0.740 0.745 | snow @ fairweather,knik |
| glacier_ice | `float3(0.62, 0.63, 0.63)` | 0.343 0.357 0.355 | glacier_ice @ fairweather,knik |
| glacial_water | `float3(0.21, 0.32, 0.33)` | 0.035 0.081 0.086 | water @ fairweather |

Two material regions, feathered over 0.6 km so no colour line follows a grid line: red-rock country (the Grand
Canyon/Page plateau, as before but no longer a hard box) and the leeward tropical coast (Dillingham/Mokuleia:
broadleaf canopy, pasture). Conifer runs from wet coastal spruce-hemlock to dry interior fir with the local
precipitation (ELA channel).

Where the materials sit (`distribution.py`, class map × DEM at 30 m): the real treeline by aspect is
Fairweather 819 m N / 1,146 m S, Sitka ~830 m, Johnson Creek ~2,350 m (forest to the ridge tops; the island's
1,460 m treeline leaves its compacted ridges, 1,500–2,080 m, partly open); snow's lower limit at Fairweather
1,111 m N / 1,331 m S, ice 851 / 1,012 m with the lowest 0.5 % at 16 m (tidewater); bare rock takes 21 % of
35–50° ground and 39 % above 50° in the glaciated areas, under 3 % at any slope in forested Johnson Creek. The
shader's rules match these shapes — a north/south shift of the snowline (±160 m here, 220 m at Fairweather),
rock from ~40° in the alpine and ~50° under vegetation, ice to the sea — at the island's own climate heights.

### 11.7 What moved (v2)

1. **Summit Denali-like → Fairweather**: (0.5, 7.1) 2,176 ft → **(−0.5, 6.3) 5,187 ft** (1.2 km SW; the old
   point is 0.45 km from the sea and could hold only 2,176 ft). Spec `changes` ledger entry.
2. **Glacier Ruth → Fairweather** (id and path; ski zone unchanged), spec ledger entry; gameplay-inventory text
   "Knik/Ruth ski" → "Knik/Fairweather ski".
3. **Climate**: storm track 280° → 310°, orographic coefficient 14 → 30 m/yr per unit slope.
4. **ELA channel** range 1,200–3,240 m → 300–3,360 m (12 m steps), `IslandRelief.ElaBaseM/ElaStepM`.
No airport, runway, strip, pad, lake, water lane, cave, nav aid, spawn/start, lesson box, combat zone,
aerobatic box, chart feature or IFR datum moved. Every check in §9 re-run.

## 10. Open questions for Ty

1. **Summit heights.** Frank Church 8,072 ft (was 9,020), Sierra 7,329, Chugach 5,915, Denali-like **2,176**
   (was 8,530). Higher summits need the approach surfaces beside them moved or the peaks moved; say which.
2. **Visual runway-end surfaces** are now FAA Part 77 (20:1 for 5,000 ft). The old 7.4 km fans off every strip end
   are what made the island flat and trenched. Accept, or name the strips that should keep longer protection?
3. **Knik and Ruth.** They cannot be fed by real ice under the A2 airway (MEA 5,000 ft). Options: raise A2's MEA
   to ~9,500 ft (an IFR change), move the ski zones up to the central-massif ice (~6,000–7,000 ft), or keep them as
   they are (flat snow landing zones in forested valleys — physically implausible in summer).
4. **The Bighorn lakes and the L2 box** conflict with approach surfaces and an airway (§7). Lower/move them, or
   accept the cliffs.
5. **Season.** Snow is drawn as early summer. A later-summer look (less snow) or spring (much more) is one constant.
6. **(v2) The Fairweather ski zone.** Converted in place at 1,300 ft (locked decision 7). Real Fairweather landings
   are on the icefield; here that is ~2,900 ft (flat, held by the A2 cap). Move it up there (and relax decision 7)?
7. **(v2) Wetter windward side.** To ice the compacted Fairweather the storm track and orographic coefficient
   changed (§11.4); the windward central massif is now icefield too, around KMYL, Payette and the Bighorn lakes.
   Accept, or keep the ice to the Fairweather massif (that needs a colder north coast, i.e. a second climate)?
8. **(v2) Colour exposure.** The palette is true surface reflectance and reads dull/grey in the renders; I
   prepared a one-stop exposure (×1.37 in the gamma-space shader, land mid-tones to photographic key) but could not
   get a render slot to verify it before the run's cap, so it is NOT in this branch. Recommended next step.
9. **(v2) Two legacy-world tests** now fail on the new relief (§9): rewrite them for the island?
10. **(v2) Constraints dominate three areas** (§11.3): Knik (KMEV/L2 plateau), Johnson Creek (strip surfaces) and
   Dillingham (authored Mokuleia crest) keep only 7–27 % of their template within 25 m. Lowering Mokuleia or moving
   L2 would let the real shapes through.

