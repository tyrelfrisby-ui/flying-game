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

## The Swift S-1, and which of these two gaps was in its way (2026-10-19)

Ty wanted a spin out of a thermal filmed for Talk Flying Ep4, in the Swift. **The Swift as shipped would not
spin at all** — on the widget's own recipe (wings level, full aft stick to the stall, then full rudder, held) it
managed 0.10 turns in 30 s against the Extra's 3.60. It stalled, dropped its nose, and flew out into a 45–54 m/s
dive with the stick on the aft stop and full rudder held. Out of a thermal turn it was worse: at 40–53° of bank
it never stalled at all, because full aft stick settled at α 7–8° against a 14° stall — the turn's own pitch rate
eats the elevator.

Two things were in its way, and **either one alone is enough to make it spin**:

| rotary table | elevator class | turns / 30 s | s/turn | ft/turn | stalled |
|---|---|---:|---:|---:|---:|
| off | trainer (as shipped) | 0.10 | — | — | 20 % |
| off | **aerobatic** | 3.10 | 9.7 | 742 | 81 % |
| monoplane | trainer | 6.53 | 4.6 | 376 | 96 % |
| monoplane | aerobatic | 5.82 | 5.2 | 404 | 96 % |

**Elevator class** is `StallAuthorityTests.TargetMarginDeg`: aerobatic types balance 6° PAST the wing stall at
full aft, everyone else 0.5° under it. The Swift S-1 is a fully aerobatic sailplane cleared for spins and was
sitting in the everyone-else bucket, so its full aft stick was calibrated to stop just short of the stall. That
is a classification error, it claims no new aerodynamic data, and the number it produces (saturation 5.2 → 16.8°)
comes from the repo's own bisection. **Ty's call, 2026-10-19: take this one.**

**The rotary table** is left off, and the reason is worth keeping: TR-379's wings are aspect ratio 6. That is a
fair match for the Extra (AR ≈ 6) but the Swift's wing is nearer AR 13, so using the table as published on a
glider wing extrapolates past the tested geometry rather than citing it.

### What that leaves, which is not a developed spin

Measured over the LAST 20 s of a 45 s hold, so the incipient phase is excluded
(`SwiftThermalSpinProbe.DoesTheRotationSettle`):

| | α | pitch | rate | s/turn | stalled | leaves the stall |
|---|---:|---:|---:|---:|---:|---:|
| Extra 300 (for scale) | 50 ±0 | −38 ±0 | −49 ±1 °/s | 7.4 | 100 % | 0 % |
| Swift, aerobatic elevator | 33 ±17 | −40 ±22 | −36 ±33 °/s | 10.0 | 79 % | **11 %** |
| Swift, + rotary table | 44 ±13 | −43 ±12 | −81 ±30 °/s | 4.4 | 100 % | 0 % |

The Extra's ±0 is a spin that has *settled*. The Swift with the elevator fixed alone never settles: α cycles
between 0 and 92°, it rolls through inverted, unloads, picks up 30 kt and departs again. Filmed, it reads as a
violent oscillatory departure rather than the clean few turns the shot wanted — 0.77 turns in 14 s, 1,138 ft a
turn. With the rotary table as well it is 100 % stalled throughout and makes 2.34 turns in the same 14 s at
378 ft a turn.

So the elevator class is the half of the fix that is certainly right; whether an AR-13 wing should carry the
AR-6 rotary table is still open, and until it does, **the Swift departs but does not hold a spin**.
