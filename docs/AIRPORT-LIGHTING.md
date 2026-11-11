# Airport lighting

Ty, 2026-11-05 08:44: "only airports with instrument approaches have vasi or papi make sure both are represented at one
airport or more the rest of the airports have no visual aids and roughly half have runway lights for night flying".

## Shared parts

| What | Where |
|---|---|
| Who has what (one row per airport, 39) | `AirportLighting.Table` in `src/FlyingGame.Core/AirportLighting.cs` |
| PAPI design and siting | `PapiPart` (same file) |
| 2-bar VASI design and siting | `VasiPart` |
| Runway edge / threshold / end light | `EdgeLightPart` |
| Instrument-approach flag on the airport list | `WorldTerrain.Airport.InstrumentApproach` (reads the table) |
| The ONLY drawer | `unity/Assets/_Project/Scripts/Bridge/AirportLights.cs`, called once from `IslandWorldBuilder.Build` |
| Review shots | `unity/Assets/_Project/Scripts/Editor/AirportLightingShots.cs` → `renders/airport-lighting/` |
| Tests | `tools/FlightTests/AirportLightingTests.cs` (20) |

`AirportLightingTests.EachAidIsOneSharedPart` fails if any other Bridge or Core file builds a PAPI or VASI, or a GLB
carries one. The old world's static PAPI (always 2 red / 2 white whatever the angle) and its runway lamps were removed
from `AirportVisuals.cs`; that world is not built anyway (`WorldBuilder.BuildAll` builds only the island). Nothing is
behind a platform flag: iOS and Quest draw the same parts.

## Who has what

Instrument approaches are the island's published procedures (`docs/pending/island-spec.json`, `ifr.approaches`):

| Airport | Procedures | Aid | Ends |
|---|---|---|---|
| PANC | ILS 16, ILS 34 | PAPI | 16, 34 |
| KSNA | LOC BC 2L | PAPI | 2L |
| PHDH | ILS 26 | PAPI | 26 |
| KMYL | RNAV 17, LDA/DME 17 | PAPI | 17 |
| KMEV | Range 35, RNAV 35 | PAPI | 35 |
| KPGA | VOR/DME 34, RNAV 16 | PAPI | 16, 34 |
| KAVX | VOR-A (circling) | **VASI** | 5, 23 (circling: no runway of its own, so both) |
| PATK | NDB 2 | **VASI** | 2 |

Every other airport has no glide path aid. Runway lights, 19 of 39: the eight above, PALH, SITX, TERN, NHVN, KITE,
RIME, and the airparks AP1, AP3, AP8, AP10, AP12. Unlit: 3U2, the nine Frank Church strips, Knik River and the other
nine airparks. At a lit airport every land runway is lit except a grass runway beside a hard one (KMEV 13G/31G).

## How the colour happens

Nothing is painted. Each box has a setting angle; it is white to an eye above it and red to an eye below it, over a
narrow pink band, and the drawer recomputes every box from the camera's position every frame
(`GlidePathUnit.WhiteFraction`, `ElevationDeg`). A box is lit only toward its approach (±10°, fading out by 15°).

- **PAPI** (L-880): four boxes, left of the runway, 50 ft from the edge, 30 ft apart, 215 m in from the threshold
  (a 40 ft threshold crossing height on 3°). From the runway outward: 3°30', 3°10', 2°50', 2°30'. Pink band 3'.
  4 white high / 3 white slightly high / 2 + 2 on the path / 3 red slightly low / 4 red low.
- **VASI** (2-bar, VASI-4): two boxes per bar, left of the runway; downwind bar 500 ft in, upwind bar 700 ft beyond.
  Downwind bar 2.75°, upwind 3.25°, a 3° path between them (pink band 0.1°). On path: far bar red over near bar
  white; low: both red; high: both white. Close in the on-path band is wider than ±0.25° (the bars are 213 m apart),
  as on a real VASI.
- **Edge lights** (MIRL): 2 ft outside each edge, at most 200 ft apart, white. On an instrument runway the last
  2,000 ft before each end (or half the runway if shorter) shows yellow to the pilot heading for that end and white
  the other way. Threshold/end bars of 4 lights each side, green out toward the approach, red in toward the runway.
  These shine only at dusk or night (the `AirportGlow` halos, each face masked to its side of the lamp).

## Not done / for Ty

- **The game has no night.** `WorldBuilder.BuildAll` always sets day; dusk halos come on only under the debug
  sunset (`AERO_SUNSET=1`, `_SunsetOn`) or an editor render. The night plate sets up its own dark sky. Flying at
  night needs a time-of-day setting (not in this lane).
- **VASI aiming angles 2.75° / 3.25° are not from a document I read.** The two-bar angles in FAA Order JO 6850.2B
  Appendix E were not retrieved; these follow the federal 3-bar pattern (2.75 / 3.00 / 3.25). The PAPI angles are
  the standard ones.
- The aids are not solids (frangible boxes, like the old ones): an aeroplane passes through them.
