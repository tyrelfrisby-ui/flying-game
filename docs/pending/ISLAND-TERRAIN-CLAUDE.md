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
| Moist flow | from 280° true | the westerlies; puts the dry shadow over the authored red-rock desert (Page/Powell, SE) |
| Background precipitation | 2.2 m/yr at the windward coast | wet temperate coast (Hoquiam 2.0, Hokitika 2.9) |
| Orographic precipitation | 14 m/yr per unit upslope, carried 4 km downwind; lee evaporation 0.7 of the same; depletion e-folds after 2.6 km of cumulative lift | Smith (2003) linear upslope model reduced to 1-D; gives 5.2 m/yr on the windward face of the central massif, 0.35–0.6 m/yr in the SE desert |
| Summer (JJA) mean at sea level | 18 °C | warm maritime summer (Seattle 17.5, Port Angeles 15, Victoria 16 — warmer inland) |
| Lapse rate | 6.5 K/km | standard |
| Equilibrium-line altitude | T_JJA(ELA) = 0.6 P / (4 mm/°C/day × 92 days) = 1.63 P | degree-day balance: winter snow (60 % of precipitation) = summer melt |
| → ELA | 1,470 m at 5.2 m/yr; 2,220 m at 2.2 m/yr; above every summit in the desert | Olympics: 1,600–2,000 m on the wet side |
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

## 9. Tests, checks, build

(filled at the end)

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

