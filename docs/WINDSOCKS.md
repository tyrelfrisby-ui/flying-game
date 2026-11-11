# Windsocks

Ty, 2026-11-05 08:43: "every airport has a wind sock off to the side of the runway at each end and in the middle".

## One shared part

| What | Where |
|---|---|
| Design: dimensions, placement, safety areas, motion | `src/FlyingGame.Core/WindsockPart.cs` (`WindsockPart`, `WindsockDynamics`) |
| Sites (built with the rest of the world's geometry) | `IslandGeometry.Windsocks`, filled at the end of `IslandGeometry.Build()` |
| Mast as a solid (an aeroplane that hits it hits it) | `WindsockPart.MastSolid`, added to `WorldSolids.Shapes` |
| The ONLY drawer | `unity/Assets/_Project/Scripts/Bridge/Windsock.cs`, called once from `IslandWorldBuilder.Build` |
| Review shots | `unity/Assets/_Project/Scripts/Editor/WindsockShots.cs` → `renders/windsocks/` |
| Tests | `tools/FlightTests/WindsockTests.cs` (18) |

No airport, builder or GLB carries a sock of its own; `WindsockTests.TheSockIsOneSharedPart` scans every script and
every GLB under `unity/Assets` for one. The old pieces are gone: the island's one static pole + orange cable per field
(`IslandGeometry.BuildAirparks`) and the old-world `WorldBuilder.BuildWindsocks`/`Windsock(...)` and
`AirportVisuals` sock (that world is no longer built: `WorldBuilder.BuildAll` builds only the island).

Same part on iOS and Quest: nothing here is behind a platform flag.

## The sock

FAA AC 150/5345-27E, L-806 size 2: 12 ft (3.66 m) long, 36 in (0.91 m) throat, 0.36 m across the tail, five bands orange /
white / orange / white / orange, on a 10 cm mast with the hoop turning on a spindle through its own vertical diameter,
hoop centre 6.1 m (20 ft) up. Concrete footing. No lit frame: **the game has no night** (`WorldBuilder.BuildAll`
always calls `SunsetLighting.ApplyDay()`; `_CityNight` is only ever set to 1 by an editor render tool, and sunset is
a debug env var), so the lighted L-807 variant was skipped.

## Where they stand

Every airport runway gets three: abeam the approach end, abeam the middle and abeam the far end.

- **Airports:** every land runway except the six beaches (B1-B6, landing sites with no airport), the two water
  lanes (no side to stand on) and Drake's strip (it runs into a cave). 43 runways, 129 socks. Big Creek IS included:
  the airparks' own `Field.StartsWith("B")` drops it from theirs (it gets no hangars; not changed here).
- **Along:** the end socks stand `min(150 m, 15 % of the length)` in from each threshold, abeam the touchdown zone,
  so a pilot on final sees one on the near end; the middle one at midfield.
- **Across (the runway safety area):** FAA AC 150/5300-13B RSA widths by design group:

  | Runway | RSA width | RSA half-width | Beyond each end | Sock from centreline |
  |---|---|---|---|---|
  | up to 75 ft wide, any surface | 120 ft (A/B-I small) | 18.3 m | 240 ft | **30.3 m** |
  | over 75 ft, or unpaved of any width | 150 ft (B-II) | 22.9 m | 300 ft | **34.9 m** |
  | paved, 150 ft or wider | 500 ft (C/D) | 76.2 m | 1000 ft | **88.2 m** |

  The mast stands `RsaSetbackM` = 12 m outside the RSA edge (a wingtip over the RSA edge still clears it).
- **Clear of everything else:** not inside ANY runway's RSA (crossing and parallel runways, the water lanes too), at
  least 15 m from every building / parked-aeroplane block, not in water, not in a cave, not on ground steeper than
  1:4 and within 4 m of the runway's own height abeam it (no sock on a cut bank or down in a river bed). The island
  has no taxiways.
- **Search:** the right side of the runway (+V) first, then the left at the same spot; then 8, 16, 26, 40 m further out;
  then 25, 50, 80 m along (never past the threshold). Every one of the 129 found a spot; 123 at the first try. The
  exceptions: PANC, KSNA 2L and KPGA 16/34 midfield go left (hangars on the right), AP3 midfield 40 m further out,
  KMYL approach end 40 m out and 80 m along, KPGA 10/28 approach end on the left.
- **Height:** the mast foot is at `IslandTerrain.HeightKm` at its own point (what contact uses).

## How it moves (nothing scripted)

Each sock is stepped every frame in `WindsockPart.AirAt(hoop)` = **`Atmosphere.WindAtPosition`, the same query every
aeroplane flies in** (mean wind with its height profile, gusts/turbulence, thermals, slope and glacier winds).

- **Swing:** a weathervane on a swivel. Normal force on the planform, Allen & Perkins (2 sin β |cos β| + 1.2 sin β |sin β|),
  at the planform centroid (0.42 L), against the inertia of 1.6 kg of fabric plus the air inside, and Coulomb swivel
  friction. The flow meets the centroid at β less its own swing speed over V, so the force that rights it also damps it:
  a 60° shift at 10 kt is within 10° in 0.45 s with an 8° overshoot. Friction is set from the spec's 3 kt point
  (aligns within 15° at 3 kt); 1 kt does not turn it.
- **Fill:** air enters only by the wind's component along the sock's axis; the fabric fills from the throat back, to
  V_axis / 15 kt (one band per 3 kt), in about one transit time (L / V), and empties as fast as cloth falls (√(L/g)).
  Filled bands lie along the 3-D flow (a down-gust pitches them down); empty bands hang 80° below horizontal.

| Wind (kt) | 0 | 3 | 6 | 9 | 12 | 15+ |
|---|---|---|---|---|---|---|
| Tail below the hoop (deg) | 80.0 | 66.7 | 49.5 | 30.5 | 13.3 | 0.0 |

- **Gusts:** 6 → 15 kt fills it to 90 % in 0.85 s; moderate turbulence on 10 kt swings it with σ 25° and fill σ 0.08.

Cost: socks more than 2.5 km from the camera hold still, more than 4 km are not drawn. One small dynamic mesh each
(~300 vertices, no garbage per frame); masts are static batches per 2 km region.

## Not in the field the sock reads (reported, not faked)

- **Wake vortices and prop wash.** Neither is in `Atmosphere.WindAtPosition` on island-nav: an aeroplane's own wake
  (TailWake) and slipstream live inside its aero model, per strip. `lane/claude-wake-vortex` (not merged) adds a world
  wake field (`VortexWake.SampleVelocity`, with the prop slipstream carried between the vortex lines) but feeds it to
  aeroplanes per strip, also not through `WindAtPosition`. When it merges, adding it to `WindsockPart.AirAt` is the
  one line that makes a sock feel a passing aeroplane.
- **Uniform-air gusts stand still over the ground.** In uniform air (`Atmosphere.Field == null`) the game sets
  `ActiveTurbulence` without `Turbulence.MeanWind`, so the frozen turbulence field never drifts: a fixed point sees one
  constant gust, and an aeroplane feels turbulence only by flying through it. A sock therefore holds still in "gusty"
  uniform air. The regional field convects its gusts (Taylor), but:
- **The regional atmosphere does not load on the island.** `configs/atmosphere/regional-atmosphere.json` anchors
  regions to old-world airports (`airport:Bench`); `AtmosphereField` throws "unknown airport anchor" (also red on the
  branch point: `MountainRoughnessTests` 6 of 7). `SessionSettings.RegionalField` catches it, logs
  "[Atmosphere] regional config not loaded … uniform air", and free flight runs in uniform air.
