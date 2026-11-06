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

## Trying it on the whole fleet (2026-10-19), and why it is not on the whole fleet

Ty: *"we should really apply this to all airplanes right?"* The case for it is strong, and measured — flying every
type on the widget's recipe, with the table off, **seven types answer full aft stick and full rudder with a
SPIRAL**, the wing not stalled at all in the last 20 s of a 45 s hold:

| | stalled, off | stalled, on | s/turn off → on |
|---|---:|---:|---|
| Cessna 172 | 100 % | 100 % | 2.4 → 2.1 |
| Archer | 100 % | 100 % | 1.9 → 2.0 |
| **SR22** | **0 %** | 100 % | 12.9 → 2.6 |
| **P-51** | **0 %** | 100 % | 13.8 → 3.4 |
| **Gee Bee** | **0 %** | 100 % | 9.1 → 6.8 |
| **Cassutt** | **0 %** | 100 % | 1.6 → 3.8 |
| **Pitts, with its own table off** | **0 %** | 100 % | 1.4 → 2.1 |
| 2-33 | 2 % | 72 % | — → 5.8 |

The Cassutt is the clearest tell: 1.6 s a turn at α 14 reads as a fast spin until you look at the angle of attack.
That is not a conservative default, it is a missing term. (The sweep also reproduces the Extra's documented
numbers — α 31 → 50, 7.3 → 7.4 s a turn — which is how we know the harness is honest.)

**It still is not on the fleet, because applied at every angle of attack it is a double count.** TR-379 measured
the SEPARATED wing; in attached flow the strip wing already makes its own roll-due-to-sideslip out of dihedral
and geometry. Hands on the trim through 40 s of moderate turbulence:

| | peak bank, off | peak bank, on |
|---|---:|---:|
| Cessna 172 | 4° | 4° |
| **Super Cub** | **7°** | **52°** |
| Swift | 38° | 62° |
| 2-33 | 57° | 65°, and 22 m/s trim wound up to 43 |

A Super Cub does not roll to 52° in moderate turbulence. Three tests failed on it:
`TurbulenceTests.GliderBuffetsButStaysStableInModerate`, `SpinTests.TomRecoveryStopsTheSpin` (the 2-33 no longer
recovers on neutral elevator and opposite rudder inside 6 s) and `DronePilotTests.AerobaticDroneFliesVaried…`.

**And the faithful fix breaks the Pitts.** Gating the term on the wing's stalled fraction — so it only acts where
the paper measured, and goes to zero in attached flow on its own — fixes the double count and leaves the Extra
untouched (α 50, 7.4 s a turn, either way). The Pitts goes from 9.52 turns at 100 % stalled to **13.92 turns at
0 % stalled**: straight back to a spiral. The reason is a latch-out. The Pitts cannot reach the stall on its own
— with no table at all it spirals at α 13 — so it was using the table's *attached-flow* part to climb into the
stall, and a gate that waits for the stall never opens. Its elevator is the known cause: the Pitts is on
`StallAuthorityTests.ManualElevator` because the calibration wanted its elevator chord ×3.6, "a geometry problem
to fix with data".

So the fleet-wide rollout is blocked behind a real decision, not a flag:

1. **Fix the elevators first.** The Pitts, the Stearman and the F-86 cannot be held past the stall at all — full
   aft stick pins them at α 13, 10 and 9. Fix those and the stall gate becomes available, and with the gate the
   table is safe to put on everything.
2. **Or ship it ungated and accept** that aeroplanes with real dihedral get roughly double their roll-due-to-
   sideslip near the stall, which is what the Cub's 52° of bank is.
3. **Or leave it where it is**, which is what was done: the Extra, the Pitts and now the Swift.

Worth saying plainly: option 1 is the right one, and the Pitts' elevator has been the blocker twice now.

## CORRECTION (2026-10-20): the Pitts row above was an artefact, and option 1 rests on it

Everything above this heading was measured with a **wrong stall reference for every biplane**, and the conclusion
drawn from it — "fix the Pitts', the Stearman's and the F-86's elevators first" — does not survive the correction.

`StallAuthorityTests.StallDeg` looked for a surface named exactly `"wing"` and fell back to a hard-coded **15°**
when it could not find one. Every biplane names its wings `wing-upper`/`wing-lower`, so the Pitts, the Stearman
and (as of today) the Jenny were all measured against 15° instead of against their own wings. The Pitts'
`pitts-wing-upper` table peaks at **12°**. Found while putting the JN-4D through the protocol; fixed to read the
type's biggest lifting surface.

**It is not only a reporting threshold.** `FleetSpinSweep` uses the same number to decide when the spin ENTRY is
complete — it holds full aft stick until `α > StallDeg + 2` and only then feeds in pro-spin rudder. For the Pitts
that gate was 17° when its wing stalls at 12°, so the rudder went in three degrees of α late, by which time it had
bled the energy it needed to autorotate.

### What changed, and why the attribution is certain

Re-running the sweep, **seven of the eight rows in the fleet table above reproduce to the digit** — the 172, the
Archer, the SR22, the P-51, the Gee Bee, the Cassutt and the 2-33, all monoplanes whose stall reference the fix
does not touch. Exactly one row moved, and it is the only type in that table whose `StallDeg` changed:

| Pitts, with its own table off | stalled | turns in 20 s | s/turn | mean α |
|---|---:|---:|---:|---:|
| as recorded above (entry gated at 17°) | **0 %** | — | 1.4 | 13 |
| corrected (entry gated at 14°) | **100 %** | 9.56 | 2.1 | **29** |
| corrected, table ON | 100 % | 9.52 | 2.1 | 34 |

**The Pitts spins perfectly well without the table.** Off and on are the same aeroplane: 9.56 turns against 9.52,
2.1 s a turn either way, 100 % stalled either way.

### What this costs the argument above

- **"The Pitts cannot reach the stall on its own" is false.** It reaches it and stays there for the whole 20 s
  window. The latch-out that killed the stall gate — the Pitts using the table's attached-flow part to climb into
  a stall it could not otherwise reach — had no case under it.
- **The gate is worth re-testing.** It was rejected on one measurement, and that measurement was this one. If the
  Pitts no longer needs the table, a gate that switches the table off in attached flow may simply leave it alone.
- **Two of the three "cannot be held past the stall" aeroplanes can be.** With each type read against its own
  wing, the static full-aft balance is:

  | | wing stall | full-aft balance | |
  |---|---:|---:|---|
  | Pitts S-2B | 12.0° | 15.2° | **3.2° past** — short of the aerobatic 6° target, not short of the stall |
  | F-86 | 10.0° | 11.8° | **1.8° past**, and already beyond its own −0.5° target |
  | Stearman PT-17 | 16.5° | 11.2° | **5.3° short** — this one is real |

  The Stearman is the genuine case, and the sweep agrees: 0 % stalled at α 10 with the table off AND on, so the
  table does nothing for it either.

### Where that leaves the fleet-wide question

Option 1 said "fix the elevators first, then the gate becomes available, then the table is safe everywhere", and
called the Pitts' elevator the blocker. The Pitts is not a blocker. The remaining order is:

1. **Re-run the stall gate** against the corrected harness. This is now the cheap step, not the expensive one.
2. If the gate holds, the fleet-wide table goes on with it and the double count in attached flow — the Super Cub's
   7° → 52° of bank in moderate turbulence — goes away by construction.
3. The **Stearman's** elevator is still a real geometry problem, independent of all of this.

Nothing in the fleet's configs was changed by any of today's work: the correction is to the measurement and to
what was concluded from it. `CALIBRATE_ONLY=<id>` now scopes a calibration run to one type, so re-deriving one
aeroplane's elevator cannot quietly rewrite another's (on 2026-10-19 it rewrote two unrelated J-3 configs).


---

## 2026-10-22 — the stall gate, run against the corrected harness

The document's own next step ("re-run the stall gate; this is now the cheap step") was run. **The gate holds.**
It is in `AeroModel.ApplyWingSideslipRoll`: the table's C_l is scaled by the wing's own area-weighted
separated fraction, accumulated from `flowState.Separation` as the strips are flown.

### In ordinary flight the double count is gone

`TurbulenceStabilityProbe` (TURB_PROBE=1), hands on the trim, 40 s of moderate turbulence, table off vs on:

| | bank peak off → on | ended off → on |
|---|---|---|
| Super Cub | 7° → 7° (**was 7° → 52°**) | 33.9 → 33.9 m/s |
| 2-33 | 57° → 54° (**was the 22 m/s trim winding up to 43**) | 32.2 → 31.7 m/s |
| 172, EB29R, Swift | identical | identical |
| Extra | identical (both end on the ground; that is the probe's own scenario, unchanged) | |

The table now does nothing in attached flow, which is what "it acts only where the paper measured" means.

### In the spin the types that have the table are unchanged

`SwiftThermalSpinProbe.TheWidgetsOwnSpinRecipe` (SWIFT_SPIN=1), 30 s of held pro-spin controls:

| | turns | stalled | mean α |
|---|---:|---:|---:|
| Swift, gated | 5.84 (was 5.82) | 96 % | 40° |
| Extra, gated | 3.60 (identical) | 100 % | 49° |

728 tests pass, including the three that failed on the ungated table.

### The 2-33 still cannot spin, and the table was never its problem

Given the gated table it manages **0.91 turns in 30 s, 32 % stalled, mean α 14°** against a 16.5° stall —
worse than with no table at all (2.70 turns), because a gate that waits for the stall never opens and this
aeroplane cannot reach its stall. Full aft stick balances it at **16.0° against a 16.5° stall: half a degree
SHORT**. That is `TargetMarginDeg` doing what it is told — trainers are calibrated to stop just short — but
the 2-33 is placarded for intentional spins and belongs with the types that can hold one.

Moving it to the aerobatic target does not work either: the calibration bisection runs to its cap at a
saturation of 120°, which is not an elevator, and still only reaches 19.8°. **The 2-33's tail as modelled
cannot hold its wing past the stall at any saturation.** What is needed is the real elevator geometry —
chord, area and up-travel from the 2-33A's maintenance data — not a larger number.

### Where the fleet-wide question stands now

The blocker named in October (the double count in attached flow) is measured away. The gate costs the
types that have the table nothing. Turning the table on fleet-wide is now a decision about what each type
should DO, not about whether the term is safe — and it is still the owner's call, because it changes how
seven types behave at the stall.

## 2026-11-05 — Q007: past flat, and the propeller's rpm

Owner (Ty, 13:02): "It should not go more than 95 or 5 nose up and it should not gyrate or roll out of inverted flat
right right spin. Look for the cause". On 3bf6ed2f the Pitts' upright left spin at full power with full out-spin
aileron sat at α 107 (81..128), nose +17°, gyrating; the inverted right spin with right aileron at full power swung
α −175..+170, rolling out through knife-edge into an upright spin every ~13 s.

**What holds a spin past flat.** `SpinBalanceProbe` part `abin` averages each part's moment over the flown spin (25–45 s),
binned by |α|. Upright full L, full R aileron, before:

| \|α\| bin | gyro M | inertial (I_zz−I_xx)pr | stab | wings | fuselage (crossflow + Munk) |
|---|---|---|---|---|---|
| 80–90 | +7116 | −302 | −4181 | −1207 | −603 |
| 100–110 | +5841 | −939 | −3171 | −1202 | −1257 |
| 110–130 | +4961 | −1332 | −2348 | −1040 | −1484 |

(N·m, body, + = nose up.) Past flat, the gyro h·r is the only large nose-up couple. The inertial couple changes sign as
the nose crosses the horizon (p reverses), so it is the term that pulls the nose back; the aero couples do not rise to
meet it, because a plate's normal force peaks at 90°.

**The cause: the gyro turned at the throttle's rpm, not the engine's.** `PropModel` gave every running prop 700 + 2,000 ×
throttle rpm. The engine model (`Aircraft.EngineRpm`) holds a constant-speed prop at its governed 2,500 from 30 %
throttle, 2,700 only firewalled. So the gyro's prop turned at 1,500 at 40 % and 2,700 at full — 1.8× the angular momentum
— while the engine turned 2,500 → 2,700. The Pitts' prop inertia, 9.0 kg·m² since 2026-09 (round 32), had been fitted
against the 1,500 rpm figure; at full power it made h = 2,545 N·m·s.

Held at the 40 % h (inertia 5.0 at full power, nothing else changed) the full-power inverted R+R spin settled at α −90.9
(−92..−90) and stopped rolling out, and the upright out-spin case stopped gyrating (α 100, steady). So the gyro was the
gyration and the roll-out. Past flat, what is left scales with h.

**The fix.**
- `PropModel.RunningRpm`: a constant-speed prop's gyro turns at the governor's rpm. This is the same law the engine
  reports, moved into Core so RigSolver and the sim agree (`Aircraft.GovernedRpmAt` now calls it). The prop lever rides
  in `PropDrive`. Fixed-pitch props keep the throttle law. Idle and full throttle are unchanged for every type; part
  throttle changes for the 8 single-engine constant-speed types. The twins' per-engine copies (DC-3, Seminole, H-4) do
  not carry `ConstantSpeed` and are unchanged. The gyro is zero at zero rates, so no stored rig moves.
- The torque reaction (P/ω) still uses the throttle law. It should follow the real rpm too, but every stored rig was
  solved against the old torque: moving it re-rigs 8 types (Extra aileron rig 0.2033° → 0.1793°). Open.
- Pitts `propInertia` 9.0 → **3.5 kg·m²** (`propInertiaSource`). A mass estimate: ~16 kg of 80 in aluminium blades,
  scaled from JSBSim's C172 (2.26 kg·m² for a 75 in prop), plus hub and crank. Depending on how the blade mass is
  spread, 3–6 is plausible. Sweep, upright left, governed rpm (taken with the torque governed too; the shipped 40 %
  values are ~1° lower, see Result):

| I_p | 40 % neutral α / θ | 40 % out-spin | full neutral | full out-spin |
|---|---|---|---|---|
| 5.4 (keeps the 40 % h) | 96.5 / 7.1 | 99.4 / 9.9 | 99.6 / 10.3 | 101.4 / 12.0 |
| 4.5 | 93.2 / 3.8 | 96.5 / 7.0 | 96.5 / 7.1 | 98.2 / 8.8 |
| 4.0 | 91.7 / 2.2 | 94.9 / 5.4 | 94.6 / 5.3 | 96.4 / 7.0 |
| **3.5** | **90.1 / 0.6** | **93.3 / 3.7** | **92.7 / 3.3** | **94.6 / 5.2** |

  3.5 is the low end of the plausible range, and it was chosen *because* it is the value at which the full-power
  out-spin flat meets the owner's 95° / 5° limit. That is a calibration inside a physical band, not a measurement.

**Result** (full table in AGENT-LOG 2026-11-05):
- upright full L + R aileron: α 94.6 (94..95), θ +5.2, steady;
- upright 40 % L + R aileron: α 92.6 (92..93), θ +3.1, steady (was 98.3 / +9.0);
- inverted R + R aileron: α −85.0 at 40 % and −85.1 at full, θ −4.6 / −4.4, steady, never upright.
- With the ailerons neutral, the shapes hold: idle α within 0.3°, the 40 % left spin still flat (92.9 / +3.9 →
  88.9 / −0.5). The rates move: idle L 183 → 200 deg/s, idle R 245 → 223, 40 % L 201 → 238, 40 % R 318 → 276.
- Lost: idle upright with out-spin aileron no longer goes flat (89.5 → 41.8), and the full-power inverted R spin with
  ailerons neutral no longer flattens on its own (it gyrated before; now α −34, steady).
- Still open, unchanged: the gyro-adverse senses (upright R, inverted L) do not go flat with power. The owner says the
  upright R does at ~85° with out-spin aileron. So the model still lacks a symmetric power-flattening term: the blown
  tail is a pure +x increment that ignores the jet bending in the crossflow, and the prop's in-plane (H) force is not
  modelled. That is the next piece of work, and the inertia should be re-examined with it.
