# Soft wings: the falling body, the canopy, the paraglider and the hang glider

Four things with no spar between them and no type certificate to read. This is where their numbers come
from, which of them are measured and which are derived, and what is still missing.

## The method, and why it runs backwards

A certificated aeroplane publishes a weight-and-balance sheet, a section and a TCDS, so its config is
*measured* and its performance is a *prediction* the tests then check. None of the four here publish any of
that. What they publish is the answer: a polar, a terminal velocity, a glide ratio. So the derivation runs
the other way — the published performance is the input and the coefficients are the output:

| wing | solved for | from |
| --- | --- | --- |
| falling body | drag area, `CdA = 2mg / (rho V^2)` | the three agreed terminal velocities |
| paraglider | `CD0 = pi*AR*e / (4*(L/D)^2)` | published best glide |
| hang glider | the same, plus `CLmax` from the published stall | published best glide and stall |

This is the same direction Ty chose for the Great Lakes' CLmax on 2026-10-30: *back-solve it from the
published Vso, and write down that the derivation ran backwards.* A backwards derivation proves nothing by
itself — it can only be trusted if the aeroplane it produces, flown by the sim rather than by the
spreadsheet, lands back on the numbers that went in. `tools/FlightTests/SoftWingPolarTests.cs` and
`SkydiverAeroTests.cs` are that check, and the numbers below are the **sim's**, not the generator's.

## 1. The falling body — `src/FlyingGame.Core/SkydiverAero.cs`

A human body is a wing with a three-to-one range of drag area, chosen by what the pilot does with his arms
and legs. That range is the whole reason a falling body is steerable.

| position | drag area (derived) | terminal | published | forward glide |
| --- | --- | --- | --- | --- |
| flat, belly to earth | 0.585 m² | 53.6 m/s (120 mph) | 120 mph | 0.04 |
| steepest useful track | 0.452 m² | 61.0 m/s (136 mph) | ~135 mph | **0.74** |
| head down | 0.191 m² | 93.9 m/s (210 mph) | ~210 mph | 0.04 |

Solved at 105 kg (a 90 kg pilot and 15 kg of kit) in sea-level air. The sources quote terminal velocities
confidently and disagree wildly on area and Cd — one calculator wants 0.9 m² at Cd 1.0, another 0.7 m² at
Cd 1.0, and the two do not give the same speed — so the speeds are taken as the measured thing and the
areas as the consequence. The derived 0.585 m² sits inside the 0.44–0.63 m² the quoted Cd and area ranges
imply, which is the check that the back-solve is not producing nonsense.

Tracking is a **skill**: the glide is a hump peaking at one body angle, so holding it is worth something
and being either side of it costs. Flat or head down, a body goes very nearly straight down.

**Control (new):** the pitch axis, which the canopy does not use. Hands neutral is the steepest track — so
a pilot who has just left an aeroplane flies *away* from it without being told to — back is flat and slow,
forward is head down and fast. The body lags the command by about a second, because a body does.

## 2. The canopy — already built, and better than I expected

`unity/Assets/_Project/Scripts/Bridge/Pilot/Parachute.cs` was already a properly modelled **round
emergency canopy**, and nothing here changed it: 28 gores, D₀ 8.5 m, Cd 0.75 giving CdS 42.6 m², line
stretch then a real inflation on Knacke's fill constant (n ≈ 8), a 1.3× opening shock for 0.3 s, 6.2 m/s
sink at sea level, 3 m/s of drive and 30°/s of turn. That is the right canopy for a bailout from an
aerobatic aeroplane and it is correctly sourced.

**Still missing: a ram-air canopy.** "Flying" a round is 0.48:1 and a heading change. A sport ram-air is a
wing — about 3:1, 10 m/s of forward speed, 3.5–5 m/s of descent, brakes that trade speed for sink, and a
flare that can put the descent near zero. That is a second canopy type, not a change to this one, and it is
**not built** (see NEEDS YOU in `AGENT-LOG.md`).

## 3. Paraglider — `configs/aircraft/paraglider-en-b-like.json`

A mid EN-B wing: 25.0 m² flat, 11.2 m flat span (AR 5.02), 100 kg all-up (85 kg pilot, 10 kg harness and
reserve, 5 kg wing) — mid-range for the class's certified 85–105 kg.

| | sim | published |
| --- | --- | --- |
| best glide | **8.59:1 at 9.7 m/s (35 km/h)** | 9.0 (class range 8.3–9.5) |
| min sink | **1.04 m/s at 8.0 m/s (29 km/h)** | 1.05–1.16 |
| trim | 10 m/s (36 km/h) | 36 km/h |
| full bar | 14 m/s (50 km/h) | 48–55 km/h |
| Vso (glide table) | 8.0 m/s (**28.9 km/h**) | ~25 km/h |

**The paraglider stalls 15 % fast.** The section's CLmax 1.35 should break at 7.0 m/s (25 km/h), but flown
by the sim it goes at 8.0 (28.9), because a tapered wing stalls at its root first and loses lift before the
whole planform reaches CLmax. Published EN-B stalls are 23–26 km/h, so this is outside the range, not just
at its edge. The lever is the section's CLmax or the all-up weight; it is reported rather than tuned,
because 25 km/h at 4.0 kg/m² is a demanding number and I would rather Ty saw the gap.

**Where the drag is.** CD0 works out at 0.0438, and only a third of it is wing: 0.700 m² of parasite area —
roughly 0.30 m² of line and 0.40 m² of harnessed pilot — is 0.0280 of it. *That* is why a paraglider glides
at 9 and a sailplane at 40, and it is carried as a real drag area in `fuselage.cd0Area`, not hidden in the
section.

**Why min sink is not better than book.** A clean parabolic polar predicts 0.87 m/s, which is 20 % better
than any EN-B wing actually achieves. The difference is real: below trim the canopy's angle of attack grows,
its profile drag climbs steeply and the lines contribute more. That is carried in the section's drag bucket
(`k_cd` 0.055), so the model is *worse* than a parabola at the low end, as the real wing is. A soft wing
that beats the book is flattering itself.

**Control:** brakes. One toggle deflects that side's trailing edge and the wing yaws and rolls into it
(mapped to `aileron`, outboard of 45 % span); both toggles add camber and angle of attack across the span,
which is the only pitch control a tailless pendulum wing has (mapped to `elevator`, inboard). No rudder.

**The pendulum.** The wing sits **6.18 m above the cg**, which is the defining thing about the type — it is
what makes a paraglider recover itself. `TheWingHangsAboveItsPilot` pins it, because a config that lost it
would still glide correctly and fly like nothing real.

## 4. Hang glider — `configs/aircraft/hang-glider-like.json`

An intermediate flex wing of the Sport-3 class: 14.4 m² (155 ft²), 10.0 m span (AR 6.94), 110 kg all-up
(80 kg pilot, 30 kg glider).

| | sim | published |
| --- | --- | --- |
| best glide | **13.09:1 at 13.5 m/s (49 km/h)** | 13 |
| min sink | **0.93 m/s at 10.6 m/s (38 km/h)** | 0.85 |
| trim | 11 m/s (40 km/h) | ~40 km/h |
| Vne | 23.6 m/s (85 km/h) | 85 km/h (Wills Wing, T2 / T3 / Sport 3) |
| Vso (glide table) | 9.1 m/s (32.8 km/h) | ~30 km/h (read as min controllable) |

The sim's 32.8 km/h is within 0.8 km/h of the 32 the back-solved CLmax 1.55 predicts, which is the check
that the CLmax reasoning below actually lands where it claims.

**CLmax is back-solved, and it does not close cleanly — say so.** The published 30 km/h stall at 110 kg over
14.4 m² demands **CL 1.78**, which no sail section gives. A flex wing's quoted "stall" is a mush with the
tips washed out and already separated, so the honest reading is that the published figure is a *minimum
controllable speed*, not a CL break. CLmax is set to 1.55 — the top of the credible range for a cambered
double-surface sail — which puts the break at 8.9 m/s (32 km/h), **7 % above book**. That gap is reported,
not tuned away.

**Control: weight shift — new code.** A flex wing has no moving surface at all, so its control axes are not
deflections; they carry **the angle the pilot swings on the hang strap**, limited by how far a prone pilot
can actually move: `atan(0.45 / 1.09) = 0.391 rad` (22.4°) laterally and 0.351 rad fore-and-aft. The offset
is `h·tan(theta)` — exact, not a small-angle approximation, because a hang glider pilot really does swing
most of the way to the control frame.

The moment is the pilot's **apparent** weight at that offset, `m_pilot·(g − a)`. Since gravity is already
applied at the cg with no moment, what is left is `−(m_pilot/m)·(the aerodynamic force)`: nothing in free
fall, double in a 2 g turn. So **roll authority grows with load factor**, as it does in the air — a plain
`m·g·d` model gets that wrong. `WeightShiftConfig.SwingMoment` is the single implementation, called by both
`Aircraft.ForceMoment` and `TrimSolver`; that matters, because the first version was only in the sim and the
hang glider **would not trim at any speed** — the trim solver was hunting for an elevator that does not
exist and the polar sweep found zero flyable points.

**The hang point is the balance point.** Neither type has a tail, so the only thing setting trim is where
the hang point sits fore and aft of the wing's own centre of lift. Pitch equilibrium at trim wants
`x_cg = x_ac − q·S·c̄·Cm / W`, which for a nose-down section puts the cg *ahead* of the ac — and that offset
is also the static margin, so a wing balanced this way is stable for the same reason it trims. The first
hang glider was built with its cg at the origin and its ac 0.68 m aft: a 734 N·m nose-down moment against
314 N·m of available swing, untrimmable at any speed. Balanced, it is **5.4 % MAC** (paraglider: 13.3 %).

## What is not here

- **No ram-air canopy** (§2).
- **No model.** Both wings fall through to `AirframeVisual`'s default "high-wing strut-braced tandem
  trainer", so they fly right and look wrong. Art is Codex's.
- **Launch is untouched.** Both spawn in the air at trim speed. Foot-launching off a slope, and a tow, are
  separate work.
- **Inertias are derived, not measured** — a span-wise bar for the wing and a point mass for the pilot about
  the combined cg. Nobody publishes a hang glider's Ixx.

## Sources

Terminal velocities and drag-area ranges: [Speed skydiving](https://en.wikipedia.org/wiki/Speed_skydiving),
[Terminal velocity](https://en.wikipedia.org/wiki/Terminal_velocity),
[ScienceABC](https://www.scienceabc.com/eyeopeners/skydiving-how-fast-can-you-fall-through-the-air-terminal-velocity),
[Owlcation](https://owlcation.com/stem/terminal-velocity-free-fall-drag-force-how-fast-humans-fall).
Canopy aerodynamics and glide: [Skydive Safety, basic aerodynamics of a ram-air
parachute](http://www.skydive-safety.com/Articles-Basic-Aerodynamics.htm),
[MC-6](https://en.wikipedia.org/wiki/MC-6_parachute), [PD Sabre2 flight
characteristics](http://www.skydive-safety.com/Parachuting-Forms/Parachute-Equipmemt/PD-Sabre2-Flight-Characteristics.pdf).
Paraglider polars: [Flybubble, speed to fly](https://flybubble.com/blogs/blog/speed-to-fly-basics),
[SkyNomad](https://www.skynomad.com/s2f_basics-html), [CUGC wiki on polars and
ballast](https://wiki.cugc.org.uk/wiki/Polar%2C_Performance%2C_and_Water_Ballast). Hang glider Vne, areas
and aspect ratio: [Wills Wing T2C](https://www.willswing.com/hang-gliders/t2c/),
[T3](https://www.willswing.com/hang-gliders/t3/), [Sport 3](https://www.willswing.com/hang-gliders/sport-3/),
[Falcon 4](https://willswing.org/hang-gliders/falcon-4-hang-glider/). Polar theory: [FAA Glider Flying
Handbook ch. 5](https://www.faa.gov/regulations_policies/handbooks_manuals/aviation/glider_handbook/gfh_chapter_5.pdf).

No published glide ratio, minimum sink or trim speed was found for any specific hang glider — Wills Wing
publishes Vne, sizes and weight ranges but not a polar — so those three are **class figures**, and that is
the weakest link in this file. A manufacturer polar would settle it.
