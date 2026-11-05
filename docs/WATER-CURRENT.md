# Moving water: rivers, tide, and everything that floats in them

Ty, 2026-11-04: "What happens if a plane crashes in a river? It should be carried by the current; model it correctly,
and model current for floatplanes accurately too." 19:35: an upstream take-off is longer and a downstream one shorter,
and a strong downstream wind can turn that round. 19:36: "NO special-case logic for upstream/downstream at all, just
let the physics do the work."

## The rule

Aero forces come from the velocity through the AIR (ground velocity minus the wind), as they always have. Every
hydrodynamic force now comes from the velocity through the WATER (ground velocity minus `WaterCurrent`). Nothing in
the code knows what "upstream" means. The take-off numbers below came out of the physics, and the tests only check
them.

| Where | What changed |
|---|---|
| `FloatHydro.Compute` (floats, hulls, all ten terms, both water rudders) | each float's state taken relative to the current at its step |
| `FloatHydro.CorrectForAddedMass` | done in the water's frame (entrained water starts at the current's speed) |
| `FloatHydro.SprayRoots` | the jet leaves at the water-relative speed and is then carried with the water |
| `LandingGear` → `WheelHydro` (wheels, skis, amphibian keels on water) | tyre velocity through the water |
| `AirframeContact` ditching contact (the non-hydro callers) | friction against the water, not the ground |
| `HydroBody.Coefficients/Correct`, `WaterSlam.Damage/ElementForce` (the ditched airframe, its joint loads, every wreck piece) | drag −C·(v − v_w), added-mass momentum relative to the water, the current's SHEAR as a torque |
| `AirframeFlooding`, `WreckSim` (`IWreckWorld.CurrentAround`) | sample the current over the body once per step |
| `FuelSlick` | 3 % of the 10 m wind plus the surface current under the slick's centre, integrated in 1 s steps |

The ASI was already air-relative (`FlightSimDriver.ApplyStateToTransform`). `Aircraft.WaterVelocityWorld` exposes the
water's velocity, so over the ground, through the water and through the air are three separate numbers. No HUD shows
the water speed yet.

## Where the water runs (`WaterCurrent`, `IslandWater.ColumnAt`)

`src/FlyingGame.Core/WaterCurrent.cs` carries the sources in its summary. In short:

- **Lakes and float ponds:** still.
- **Sea:** a rectilinear M2 tidal stream (12.42 h period), 0.3 m/s (0.6 kt) depth-averaged at peak, flooding toward 045°
  true. Slack at sim time 0. (Q032 asks Ty if he wants something else.)
- **Canyon rivers and outwash braids:** flow follows the centreline toward the channel's low end. Speed comes from
  Jarrett (1984), V = R^0.827 S^0.12 / 0.32, where S is the centreline slope from the terrain (±100 m, held to
  0.002–0.04) and the depth comes from the channel's width (width/depth 15, parabolic section). The lateral profile is
  u(s) = U_c (1 − s²)^0.827: fastest mid-channel, zero at the banks. The centreline speed is held to 2–8 kt.
- **Bend eddy:** below a bend the inner bank blends toward a back-current of 0.2 U_c over three channel widths (Ferguson
  et al. 2003), at full strength for a 60° turn. The island's wet bends are only 20–37°, so there the inner bank runs
  at 17–54 % of the outer bank's speed. Only a sharper bend gives a true back-current (tested on `WaterCurrent.Channel`
  directly).
- **Vertical:** the log law, u(z)/ū = ln(z/z₀)/(ln(h/z₀) − 1). z₀ is 12 mm for a cobble bed and 0.4 mm for sea sand.

The island's centreline speeds come out at 5.1–8.0 kt (`EveryIslandStreamCarriesAFloatDownhillAtAMountainRiversSpeed`
prints each one).

**Known limit (Q031):** the island draws its rivers as a 0.15 m water sheet over the canyon floor. The current uses the
river's hydraulic depth, because at 0.15 m Jarrett gives only 0.6 kt. Floating and grounding still follow the drawn
water, so a floatplane or a buoyant wreck in an island river sits on the gravel. Moving-water physics on a river you can
actually float in needs deeper channels, which is terrain and water-mesh work. Tests use flat water with a current.

## What the physics gives (tools/FlightTests/WaterCurrentTests.cs, 2026-11-04)

J-3 on floats, the HydroJ3 take-off pilot (pitch schedule on airspeed), the same function every run. The current is
2 m/s (≈4 kt) and "wind" is 15 kt blowing downstream:

| case | unstick | through the water | over the ground | water speed at unstick |
|---|---|---|---|---|
| no current, calm | 19.9 s | 221 m | 220 m | 21.4 m/s |
| upstream, calm | 22.8 s | 270 m | 224 m | 23.3 m/s |
| downstream, calm | 14.9 s | 166 m | 195 m | 19.5 m/s |
| upstream, into the wind | 10.5 s | 88 m | 66 m | 15.9 m/s |
| downstream, tailwind | 30.6 s | 505 m | 565 m | 26.4 m/s |

Lift-off airspeed is the same in every case (≈21.4 m/s). The water speed at unstick is airspeed + current upstream and
airspeed − current downstream, which is the whole of Ty's point. With the 15 kt wind the preference reverses.

Other results:

- **Drift with the engine stopped:** a J-3 on floats in a 1.5 m/s cross-current drifts at 1.25 m/s, 0.25 m/s through
  the water. Aligned with the stream, its floats' only low-speed resistance is skin friction, so the airframe's drag in
  the still air it drifts through holds it back (leeway).
- **Ditched C172:** carried at 1.24 of 1.5 m/s. It floats high, so windage holds it back too.
- **Idle into a 4 kt river:** the J-3 makes 0.43 m/s through the water and goes backwards at 1.57 m/s over the ground.
  At 30 % power it makes 3.0 m/s up the river.
- **Floating C172 wing:** carried at 1.74 of 1.8 m/s after 120 s. A shear of 0.2 m/s per metre across its span turns it
  88° in 60 s; still water or a uniform current turns it 13–16°.
- **263 kg nose piece in the 0.15 m sheet:** half its weight is buoyed, and it creeps at 0.09 m/s (6 % of a 1.5 m/s
  current), grounded on the bed.
- **Zero current:** the moving-water code path with the water not moving reproduces the still-water take-off and the
  still-water wreck to the last digit.

## Also changed

`FloatHydro.PlaningMaxSideslipRad` (60°): Savitsky's planing is a flow along the keel. With the water coming square
across the keel, which is a floatplane sitting in a cross-current, the trim "along the flow" went to 90°, tan τ blew up
and the state went NaN on the first step. Beyond 60° of sideslip the planing terms are now off and the crossflow terms
(7) carry the hull. Below 60° nothing changed.

## Not modelled (stated, not fudged)

- Rotary tide, coastline steering, tidal height.
- River plumes at the mouths (the sea starts where the ground goes below 0).
- Confluences, and the joins between the Middle Fork's separately-drawn reaches.
- Turbulence in the current.
- Waves made by the current.
- The wake waves' drift (cosmetic). `WakeWaves` takes its strength from `FloatHydro.Floats[].SpeedMs`, which is now the
  speed through the water, but the waves are drawn in the ground's frame and do not drift with the current.
- Asleep wreck pieces never wake (a tide that turns does not lift a piece off the bed).
- A floating airframe's own windage is the aero model's, computed as for flight (adequate at drift speeds, never
  calibrated for them).
