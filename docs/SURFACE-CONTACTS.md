# Surface contacts: the events the tracks are drawn from

Surface-tracks spec, PART A (lane/claude-surface-tracks, 2026-11-06). Ty 2026-11-04: "anything contacting a surface
leaves tracks", and 09:48: tracks are a teaching tool, so they must read accurately. This is the data side only.

**Taken forward on lane/claude-imprints, 2026-11-10** (Ty: *"if it is penetrating it should leave imprints behind as it
slides and rolls displacing the dirt or snow etc"*). The contact events below are unchanged except for one new field,
`SurfaceWorld`, and what is DONE with them is `docs/GROUND-IMPRINTS.md`: the marks the ground keeps.

## What you get

`Aircraft.SurfaceContacts` (a `SurfaceContactBuffer`) holds what touched the surface in the last physics step. It is
refilled in place every step; `Aircraft.SurfaceContactsStepped` is raised after every step with it, so a renderer
that runs several physics steps per frame sees each one (a touchdown can last a single step). `buffer.Step` counts
steps, so reading the same step twice can be detected. Nothing is allocated per step.

Each `SurfaceContact` (all vectors world NED, z down; Unity converts as it does every other sim position):

| field | meaning |
|---|---|
| `Type` | `Tire`, `Ski`, `Float`, `Skid`, `Foot` |
| `Source` | gear-leg index (tyres, on-ski wheels, keels on land, skids, feet); `FloatHydro.Floats` slot for a float on water (0 L, 1 R, 2/3 tip floats); the ski's leg for `AddSki` |
| `PositionWorld` | tyre bottom / ski centre of pressure / float step |
| `SurfaceWorld` | the UNDISTURBED surface at the contact: `PositionWorld` carried back up `SurfaceUpWorld` by the whole contact compression. NOT the same point — a J-3's patch is 1.5 cm below the grass standing still and up to 21 cm below it while it settles after a touchdown, because the leg and the tyre have compressed. This is where a mark in the ground lies. |
| `SurfaceUpWorld` | the surface normal there |
| `HeadingWorld` | the way the part points along the surface (a tyre's steered direction, the keel line) |
| `GroundVelocityWorld` | the patch's velocity over the surface (deck- and current-relative) |
| `SlipWorld` | how fast the part's own surface slides over the ground: ~0 for a rolling tyre, the whole speed for a locked one (the skid mark); always the whole speed for skis, skids, keels and feet |
| `WidthM` / `LengthM` | footprint width / length (tyre: TireModel at its deflection, a rigid legacy tyre its section width; ski: its running surface; float: beam, and λ × beam wetted on water) |
| `DepthM` | tyre rut on soft ground (below), tyre depth in water, ski sinkage (from the ski model), float draft at the step |
| `PenetrationM` | the whole contact compression along `SurfaceUpWorld` — how far the part's lowest point is inside the ground. ELASTIC (a spring holds it; it returns when the load leaves), unlike `DepthM`, which is the soil's permanent set. The only depth a leg with NO sinkage model has: a bearing ski, a skid shoe, a float keel on land, feet. Added 2026-11-10. |
| `LoadN`, `PressurePa` | normal load; mean ground pressure |
| `SlipPowerW`, `Locked` | tyre friction power (fore-aft + side) and lock-up |
| `Material`, `Surface` | `GroundMaterial` (the key the sound uses too) and the physics surface |
| `SnowWetness01` | 0 dry … 1 soaking, on Snow/PackedSnow only |
| `TimeSec` | sim time of the step |

A float keel on land or snow is reported per keel point (the floats config has several per float): group them by
side if you draw one V per float.

## Where the numbers come from

- **Tyre rut.** The rolling-resistance model (`TireModel.RollingCoefficient`) is Bekker's compaction resistance,
  R/W = z/((n+1)·l). `TireModel.GroundSinkageM` inverts it: the rut is exactly the sinkage the physics already charges
  the aeroplane for. 0 on pavement and ice. A Cub (8.00-6) at rest measures gravel 4.6 mm, dirt 3.3 mm, grass
  3.8 mm, sand 10 mm, settled snow 34 mm. Gravel above dirt is the model's pressure exponent 1/n at a soft tyre's
  low ground pressure, not a choice made here.
- **Snow wetness.** `SurfaceContacts.WetnessFromLiquidWater`: liquid water content over the ICSSG "soaking" class
  (Fierz et al. 2009: dry 0, moist 0–3, wet 3–8, very wet 8–15, soaking > 15 % by volume). The LWC comes from
  `SurfaceContacts.SnowLiquidWaterPercent`, which the snow model sets (lane/claude-ski-physics `SnowConditions`).
  Without one, nothing in the physics makes snow wet, so every contact reads dry.
- **Skis.** SUPERSEDED 2026-11-10: a bearing ski now reports the REAL running surface of the ski the type carries
  (`SkiGear.Geometry`, which `lane/claude-quest-islandnav` brought that day — a Super Cub's mains 0.289 × 1.980 m, its
  tailwheel ski 0.173 × 1.188 m), recorded per leg by `LandingGear`, with its ground pressure. Still no SINKAGE: see
  `PenetrationM`, which is what a leg with no sinkage model marks from. As written on 2026-11-06, a wheel-ski on its
  ski reported the ski model's default size (0.25 × 1.8 m) and no sinkage. When ski-physics merges, its `SkiContact` legs call `SurfaceContacts.AddSki(...)` after `Collect` in
  the same step, with their own width, length, sinkage and load, and they must not also be reported as `OnSki`
  wheels.
- **Estimates, flagged:** skid shoe width 0.06 m, feet 0.25 m (no config carries either).

## Not here yet

- **Mud.** `GroundMaterial` has no mud and no world surface produces it. A mud track needs a mud surface first
  (`IslandRelief`'s wetness channel would be where it comes from). Raised as **Q145** on 2026-11-06 and still open on
  2026-11-10; `docs/GROUND-IMPRINTS.md` says what three numbers a mud row would need.
- **Ski sinkage and real ski sizes** come with lane/claude-ski-physics.
