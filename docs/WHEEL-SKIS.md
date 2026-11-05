# Wheel-skis: hinged skis and the WHEELS / SKIS selector

Ty, 2026-11-05 07:58: *"yes i want wheel skis as an option on every airplane. when chosen a selector for 'wheels/skis'
gets put in the cockpit someplace"*. 07:59: *"that raises or lowers the skis they rotate forward and up when raised and
rotate down and aft when lowered and the tire sits on top of the ski when lowered instead of poking down below"*.

This replaces the 2026-10-16 wheel-penetration ski (a ski bolted under the wheel with the tyre always through its slot,
ski on snow, wheel on anything hard, 15 % of the load left on the tyre for the brake).

## The option

- Landing page: `Wheel-skis ON/off` next to Bushwheels (StartMenu, one or the other). Quest menu: `Gear: Wheels /
  Bushwheels / Wheel-skis`. Session state (`SessionSettings.Skis` → `AircraftConfig.Skis`); contests already pin it.
- Every leg that is a wheel (`WheelSpin.IsWheel`) takes a ski: mains, tailwheels, nosewheels, a glider's monowheel, an
  amphibian's wheels. Float keels, skids and feet do not. 35 of 35 configs checked (`WheelSkiHingeTests`).

## The mechanism (`src/FlyingGame.Core/SkiGear.cs`)

Each ski hangs from the AXLE on a pair of parallel links, one each side of the tyre, so it swings on a circle about the
axle and stays parallel to the airframe. Link angle θ from straight down, + = lower end forward:

| | θ | what you see |
|---|---|---|
| SKIS (lowered) | −12° (over centre) | the ski's top on the tyre's bottom — the tyre sits on solid ski, 0.21 r ahead of the pin |
| WHEELS (raised) | +80° | the ski swung forward and up (Cub main: 30 cm forward, 20 cm up), the tyre down through the slot |

- Link length = (tyre radius + 2 mm) / cos 12°, i.e. the pivot is the axle — unless a small tyre under a thick ski needs
  longer links to lift it 4 cm clear (`RaisedClearanceM`), when the pivot is higher up the leg (Cassutt, target drone).
- Ski size from the fuselage (the old rule: a quarter of its length for a main, a twelfth for the tail, 0.16 for a nose
  ski; width 1/24), thickness 3 % of the length. The axle sits 8 % of the length aft of the ski's middle.
- The SLOT is computed, not drawn: the links are swept from lowered to raised and every chord of the tyre that comes
  above the ski's top is cut out. It always reaches to about the pin (in the ski's frame the tyre's centre runs on a
  circle of radius r about it), which is why the lowered position is over centre: that is what puts solid ski under the
  resting tyre. `TheTyreNeverPassesThroughSolidSki` checks 200 link angles × 360 points of the tyre on four types.
- The ski pitches freely on its pin (bungee / check cable), so on the ground it lies on the ground; past its stops
  (tip down 20°, tip up 15°, ESTIMATES) its heel or the front of its flat bears instead.

## The contact (`LandingGear.Compute`, `UpdateTailwheel`)

Per leg, whichever is deeper in the ground — the ski's lowest point (`SkiGear.Probe`) or the tyre's bottom — carries the
leg. No surface test and no per-type pose: lowered it is always the ski, raised always the tyre, and in transit whatever
the links put lowest. Selecting SKIS while standing on the runway swings the skis down under the tyres and jacks the
aeroplane up onto them (9.7 cm on the Cub).

A bearing ski moves the contact to its own point and takes the tyre out of the load path (the load goes through the
pedestal and links to the axle; the leg's spring and damper stay). The wheel is off the ground, so **the brakes do
nothing on skis**. Friction of the ski base:

| surface | forward | sideways | source |
|---|---|---|---|
| settled snow | 0.055 running (×4 static below 1 m/s) | 0.05 edge | unchanged (`LandingGear.SkiRolling`, `SkiLateralCoefficient`) |
| packed glacier zone | 0.02 | 0.12 | unchanged |
| paved / gravel / dirt / sand / rough / grass / bare ice | 0.35 / 0.45 / 0.40 / 0.40 / 0.30 / 0.20 / 0.03, ×1.3 static | the same (no edge on hard ground) | ESTIMATES, Q081 |

So with the skis down a Cub scrapes to a stop on pavement in 12.7 m from 8 m/s at idle, against 52 m on snow; with them up
it rolls 93 m on its wheels (`LoweredOnPavementTheSkiScrapesOnSnowItRuns`). Skis up on snow it is a wheel plane in snow,
within 5 % of the plain Cub (`RaisedOnSnowItIsAWheelPlane`).

Not modelled yet: a lowered ski on WATER (the tyre's planing model still runs, from the tyre's bottom).

## Weight and drag

- Mass: 22 kg per m² of ski (ESTIMATE): Cub 31 kg, Beaver 61 kg. Added at each ski's pin to the mass, the CG
  (`Aircraft.CgShiftBody`) and the inertia (`Aircraft.FittedMassKg`), the way `ShedMass` takes a part away.
- Drag: 0.8 × width × (thickness + the tip's rise) per ski, up or down: Cub 0.167 m², Beaver 0.323 m².

## The selector: one shared part

- Design: `src/FlyingGame.Core/SkiSelectorPart.cs`. Drawn by `Bridge/SkiSelectorLever.cs` and nowhere else
  (`TheSelectorIsOneSharedPart` fails if any script, model builder or GLB carries one of its own).
- A placard plate (85 × 110 mm), a lever in a two-position gate with a little ski for a knob (shape-coded like a gear
  knob is a wheel), UP = WHEELS, DOWN = SKIS (the command, like a gear handle), and three lamps that say where the skis
  ARE: green WHEELS, amber TRANSIT, green SKIS.
- Placed by ANCHOR only: `loading.seats[].skiSelector` = [x, y, z] (body frame, the plate's centre). Where no seat has
  one, the pilot's (first) seat gets it 0.50 m ahead, 0.22 m left and 0.32 m below the eye; a type with no seats authored
  uses the cockpit view's eye estimate. (Q082)
- Hydraulic, 8 s full travel (`SkiGear.TransitSec`, ESTIMATE, Q080). It starts SKIS over snow and WHEELS elsewhere.
- Inputs, all into `SkiSelectorLever.Move` → `Aircraft.Skis.Selector`, the state the physics reads:
  click on the lever (Mac), tap on it (iPhone/iPad), the GEAR slot's `SKIS UP` / `SKIS DOWN` button and the `L` key and
  the joystick's gear button (`TouchFlightControls`, fixed-gear types), and on the Quest a pointing fingertip or a
  controller with its grip closed at the knob (`VrCockpitHands.DriveSkiSelector`).

## Drawing (`AirframeVisual`: `BuildSkis`, `SetSkis`)

The same `SkiGear.Geometry` the physics uses. A placed 3-D model keeps its own wheels (they sit on the lowered ski and
come through the slot when it is raised); the skis are no longer gated on the model's gear being cut out. Each frame the
links turn to the selector's position and each bearing ski is laid at the pitch the physics found.

## Tests

`tools/FlightTests/WheelSkiHingeTests` (52), and `WheelSkiTests`, `GlacierLandingZoneTests`, `IslandSurfaceTests` updated
for the new truth (no surface-based `OnSki`, brakes do nothing on skis, skis up on pavement = wheels within 5 %).
Pre-existing failures on the base commit 938a865c, unchanged here: `GlacierLandingZoneTests` ×7 (the zones' surface and
grade), `WheelSkiTests.ACubOnSkisCanBePutOnTheKnikAndStop`, `OnSnowASkiRunsOnAfterTheWheelHasStopped(pa18-cub-like)`.

## Renders

`~/Developer/agent-lanes/renders/wheel-skis/` (`WheelSkiShots`): exterior raised / half / lowered for the Super Cub,
C172 and J-3, and the selector from the seat eye in the J-3, C172 and Pitts.
