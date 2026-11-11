# Wake vortices (lane/claude-wake-vortex, 2026-11-05)

Ty, 2026-11-05 06:05: wispy layers that react to "the airplane's wake with wingtip vortex based on the actual AOA so no
lift no vortex lots of AOA/lift lots of vortex. Model wake and vortex for multi player and glider tow as well — the
glider will box the wake — and formation flying should realistically roll the following plane".

Nothing in this model applies a "wake force". Every aeroplane's strips meet the air the other aeroplanes' trailing
lines move, and the roll, the sink and the slipstream push are what its own wing does with that air.

## Code

| Where | What |
| --- | --- |
| `src/FlyingGame.Core/Aero/Wake/WakeVortex.cs` | The physics as functions: Γ₀, the Burnham–Hallock segment law, vorticity, core growth, linking time, slipstream spreading. Sources and judgement calls are in its header. |
| `src/FlyingGame.Core/Aero/Wake/WakeSource.cs` | One aeroplane's wake: live head at the tips, a ring of 320 stations (one per 0.4 s, 128 s), each pair ageing, decaying and moving on its partner's induction (and ground images) plus the mean wind. |
| `src/FlyingGame.Core/Aero/Wake/VortexWake.cs` | The registry and the queries (process-wide, empty unless something registers). |
| `src/FlyingGame.Sim/Aircraft.cs` | `Aircraft.Wake` (what it sheds, from the aero force its own step made) and `FeelsWake`; `SampleWakeAtStrips` gives every strip the wake's air at that strip. |
| `src/FlyingGame.Core/Aero/AeroModel.cs` | `stripAirBody`: one more term in each strip's relative wind, next to the regional wind gradient. |
| `src/FlyingGame.Sim/Replay/ReplayPlayback.cs` | Recorded ships shed from the load factor the track carries (`ReplayActor.ShedsWakes`). |
| `unity/.../Bridge/Wake/WakeField.cs` | `Aero.Wake.WakeField`, the contract's Unity API, and the host (`Ensure`, `Tick`). |
| `unity/.../Shaders/WakeFieldBake.shader` | `WriteToTexture`: the sheet the wisps read, evaluated on the GPU from ≤ 64 segments. |
| `tools/FlightTests/WakeVortexTests.cs`, `tools/wake/plot_wake_tests.py` | Tests and their plots. |

## Who sheds

| Aeroplane | How |
| --- | --- |
| Player, tug, combat drones | `WakeField.Ensure(aircraft)`: from the aerodynamic force of each sim step. |
| Recorded ship (kinematic) | From the recorded load factor nz: aero force n·W along body −z. In wreck mode, from its own physics. |
| Network peer | From its motion: m (a − g), a from successive poses, smoothed 0.3 s. A jump (respawn) starts a fresh wake. |

Lift is the part of the aero force square to the flight path AND to the span, so a fuselage's knife-edge side force
does not count as wing circulation. No lift (a ballistic arc, a zero-g pushover, a wingless wreck): Γ = 0, no vortex.

## Physics, briefly

- Γ₀ = L / (ρ V b₀), b₀ = π/4 b (elliptic loading). Lines at ±b₀/2 along V̂ × L̂ (Kutta–Joukowski), so inverted flight
  swaps their sense on its own.
- Each line is a chain of straight segments; a segment's field is the regularised Biot–Savart law, which for a long line
  is the Burnham–Hallock profile v = Γ/2π · h/(h² + r_c²). r_c starts at 0.05 b and grows by Lamb–Oseen with Squire's
  eddy viscosity ν + 2×10⁻⁴ Γ₀.
- Descent is not a formula: each node moves with what the partner line (within ±6 b₀ along the wake) and, within 3 b₀ of
  the ground or water, both lines' images induce. The tests measure Γ₀/(2π b₀) coming out within 1 %.
- Decay: Γ holds, then collapses once the air has linked the pair: T* = 0.8 η^(−3/4) (capped 6), η = (ε b₀)^(1/3)/w₀,
  collapse e-folding 0.6 t₀. ε from the turbulence model (σ³/L) or 10⁻⁴ m²/s³ in calm air. The coefficient, the cap
  and the collapse time are judgement against the published 2–8 t₀ lifetimes, not a fit.
- Prop slipstream (towplane): Gaussian jet between the lines with the momentum-theory tube's flux, spreading at
  dσ/dt = 0.094 ΔU, ΔU σ² and swirl σ³ conserved. Twins blow on the centreline (simplification).
- An aeroplane does not feel its own wake younger than 5 s (that is its induced drag and downwash); a tight turn into
  its own older wake is felt.

## Measured (WakeVortexTests, all literals worked by hand)

| Check | Model | Hand |
| --- | --- | --- |
| 2-33 trimmed glide, Γ | 11.60 m²/s | W cos γ/(ρ V b₀) 11.80 |
| 900 kg, 11 m, 30 m/s, n = 1 / 2 / 4 / −1 / 0 | 30.6 / ×2 / ×4 / same / 0 | 30.6 |
| C172-size pair, early descent | 0.387 m/s | Γ₀/(2π b₀) 0.392 |
| Downwash midway between the lines | 0.588 m/s | 2Γ/(π b₀) 0.600 |
| 2-33 with its CG on a 2-33's line 4 s back: roll rate | 0.094 rad/s, in the line's sense | strip theory 6Γ/(π b²) 0.091 |
| C172 pair lifetime (Γ half) | calm 60 s, ε 10⁻² 25 s | — |
| 2-33 61 m behind a Pawnee: roll acceleration on a line | ±80 °/s² (full aileron: 106) | — |

The 6Γ/(π b²) result is worth knowing: the roll a line forces on a follower of span b does not depend on speed, chord
or lift slope, and goes as 1/b². A like-sized follower is rolled firmly (5 °/s here); one of half the span would be
rolled four times as fast.

Plots: `~/Developer/agent-lanes/renders/clouds-wake/wake_*.png` (decay and descent, follower roll, the tow "box", Γ vs
load factor).

## Cost

Worst case measured on the bench (Mac, load ~30): four full wakes (1920 segments) plus a 2-33 flying inside one:
+50 ms per sim-second for the follower's strips, 28 ms per second for the field, no garbage from the field. Segments
within about a span of the airframe are evaluated at every strip; farther ones at four points and spread linearly.
Not yet measured on the Quest.

## Open

- Q: should the wake be on by default for every aeroplane in single-player free flight? It costs nothing unless another
  aeroplane is shedding (only tow, drones, recorded ships and peers do).
- Not modelled: one wake moving another (pairs are carried by the wind and their own partner only), the Crow
  instability's visible rings, stratification, crosswind shear tilting a pair.
