# Island terrain: glacier and mountain surfaces (spec from Ty, 2026-11-02)

Status: spec for the next run on lane/claude-island-terrain. Not yet implemented.

## Glacier surfaces are either SNOW or ICE

- **SNOW**: smooth and landable everywhere, with consistent ski drag.
- **ICE**: has some smooth patches, but it is never level. It undulates, so a Super Cub on
  tundra tires could land on it but would bounce around.
  - Other ice areas are heavily crevassed, with jagged bright-blue ice and deep cuts.
    These are hazards, and collisions there are real.

## Moraines

- Moraines must look real: lateral and medial moraine streaks of rock, plus giant
  boulders sitting on the glacier.

## Glaciated mountains

- Small snow patches scattered around. All of them are landable but have different
  slopes, so a pilot can try touch-and-goes on a tiny patch high on a mountain.

## Dry mountains

- Small ledges here and there (flat, sloped or rounded) that can also be landed on for fun.

## Physics

- Surface types must drive the physics (friction, ski drag, bumpiness) through the
  existing surface system, and this needs tests.
