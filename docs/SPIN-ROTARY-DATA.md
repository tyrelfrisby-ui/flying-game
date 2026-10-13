# Spin rotary data: wing rolling moment due to sideslip above the stall

2026-10-13. The owner's target: the Extra 300's upright power-on flat spin settles 10–15° nose-low.

## The gap

The strip-theory wing does not autorotate in roll at α 30–60°, so the spins were too slow and too steep.

At **zero yaw**, published data agrees with the model: a wing above about α 28° damps roll. NACA TR-379, Fig. 19 (Clark Y monoplane, yaw 0): the curves at α 28°, 30° and 40° slope negative through pb/2V = 0, and only α 14–25° show a propelling hump.

What the model lacked is the effect of **sideslip (yaw) on the separated wing**. TR-379's conclusions:

1. "At angles of attack above that of maximum lift the rolling moments on wings due to yaw (or sidelip) from 5° to 20° are of the same order of magnitude as those due to rolling."
2. "The rate and range of stable autorotation for the monoplane wings are considerably increased by yaw."

The cause: the leading half-wing (sweep-forward) carries more load, most at its tip, and the trailing half (sweep-back) carries less. Pressure-distribution results in TR-379, Fig. 11–13.

A straight strip wing has none of this, because each strip sees only its own section α.

## Source

Knight, M. and Wenzinger, C. J., *Rolling Moments Due to Rolling and Yaw for Four Wing Models in Rotation*, NACA TR-379, 1932 (NTRS 19930091451). Rectangular wings, aspect ratio 6, 5-ft wind tunnel, autorotation dynamometer.

Stable-autorotation rates (Table V, Clark Y monoplane):

| Yaw | α | pb/2V |
|---|---|---|
| 0° | 14° | 0.257 |
| 0° | 18° | 0.306 |
| 0° | 24° | 0.323 |
| 0° | 27° | 0.291 |
| 0° | beyond 30° | none |
| 20° | 20° | 0.362 |
| 20° | 28° | 0.500 |
| 20° | 35° | 0.615 |
| 20° | 40° | 0.643 |

## Numbers used

Rolling-moment coefficient C_l = L / (q·S·b) at pb/2V = 0, at **20° yaw**. These are read from the published curves every 4°, and the model uses them per unit of (β / 20°).

- The 5°, 10° and 20° curves are close to linear in yaw above α ≈ 28°.
- β is clamped at ±20°, because there is no data beyond it.

| α (°) | 0 | 8 | 12 | 16 | 20 | 24 | 28 | 32 | 36 | 40 | 44 | 48 | 52 | 56 | 60 | 90 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Monoplane (Fig. 28) | 0 | .003 | .015 | .045 | .078 | .056 | .038 | .034 | .031 | .028 | .026 | .024 | .022 | .020 | .018 | 0* |
| Biplane (Fig. 27) | 0 | .003 | .010 | .023 | .038 | .050 | .050 | .043 | .036 | .031 | .027 | .025 | .023 | .021 | .019 | 0* |

\* The tests stop at α 60°. From 60° to 90° the model goes linearly to 0. This is an **assumption**: at 90° the wing is broadside, so yaw only turns it in its own plane and there is no leading or trailing half. TR-379 Fig. 30 shows all four wings converging, so above α ≈ 32° they read about the same.

## How the model applies it

`AeroModel.ApplyWingSideslipRoll`, config `spinAero.rollDueToSideslip`:
- "monoplane" for the Extra 300; "biplane" for the Pitts S-2B; empty (off) for every other type.
- No scale factor: the coefficient is used as published, referenced to the total wing area S and the span b.

Sign conventions:
- Upright (α > 0), a wind from the right (β > 0) loads the right (leading) wing, which rolls the airplane LEFT.
- Inverted, the loads are negative lift, so the moment reverses.
- In reversed flow (|α| > 90°), α is folded back about 90°.

## Effect (FlatSpinProbe / SpinChar, 2026-10-13)

| Case | Off (before) | On |
|---|---|---|
| Extra upright power-on (left rudder, full power, full right aileron), full aft | pitch −48°, α 38°, 3.2 s/turn, 98 kt TAS | **pitch −11.6°, α 78°, 1.57 s/turn, 57 kt TAS** |
| Same, full forward | pitch −64°, α −25° (left the stall), 3.9 s/turn | **pitch −13.6°, α 76°, 1.59 s/turn, 56 kt TAS** |
| Extra upright idle | α 31°, 7.3 s/turn, 1,075 ft/turn | α 50°, 7.4 s/turn, 920 ft/turn |
| Extra inverted, full power | α −27°, 11.3 s/turn, 2,047 ft/turn | α −50°, 2.9 s/turn, 410 ft/turn |
| Pitts upright idle | α 13°, 1.4 s/turn (a spiral) | α 34°, 2.1 s/turn, 319 ft/turn (a spin) |
