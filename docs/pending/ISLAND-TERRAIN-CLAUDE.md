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

(sections 3–9 below are filled with the final numbers)
