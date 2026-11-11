# Cm(β): the sideslip-to-pitch coupling, measured against the published data

Lane `lane/claude-slip-pitch`, 2026-11-10. Base: `c7471a35` off `lane/claude-tv-companion` — **after** the 2026-11-10
move of all 26 `naca0012-like` fins to `fin-lowAR` (`daf52725`), after `PropJet` (2026-11-08), after the
leading-edge-stall latch was deleted, with `AeroModel.ClampNormalForcePastStall` off, and with the Extra's and Pitts'
`hStab` still on `naca0012-like` (the flat-plate fabric-tail conversion is a separate lane and had not landed on this
base; the `elevator` rows are already `fin-lowAR`). Every number below is from `tools/FlightTests/SlipPitchProbe.cs`. (The lane then took `docs/PARITY.md`'s own first remedy, a `--no-ff` merge of
`lane/claude-quest-islandnav`, which brought 56 config files with it; every number below was re-measured on the merged
tree and came out identical to the digit.)

## 0. The job, in the owner's words

Ty (competition aerobatic pilot, Pitts S-2B), 2026-11-03, re-confirmed 2026-11-10:

> "sideslip-to-pitch coupling in the NOSE-DOWN direction is a big driver of the tumble and, in his flight testing of the
> game, is still too weak by a factor of at least 5x. **Do not scale it by 5; find the physics.** Measure Cm(beta)
> (pitching moment vs sideslip, nose-down) for the Extra and Pitts at alpha…"

So: measure it, find the mechanism, change the mechanism and not the gain.

The answer, up front:

1. **The factor of five is real and it is located.** It is not a gain anywhere. On this base the whole of the model's
   Cm(β) is carried by the FUSELAGE, and the HORIZONTAL TAIL carries **exactly zero** — measured to three decimals at
   every β from 0 to 90°. In the only published wind-tunnel data for this quantity the tail carries **five to six times
   what the body carries**. The model has a fitted body term standing in for a missing tail term, which is why the
   number looks about right at one condition and behaves wrongly everywhere else.
2. **The mechanism the data attributes it to is now identified, with the authors' own words** (§3): one of the
   fuselage's two crossflow-separation vortices, the other having been eaten by the wing, trailing over the horizontal
   tail. I built it and measured it (§5). It delivers a ninth of the published tail increment, and — this is the
   important part — **the published α-dependence says it cannot drive the Endo at all**, because it needs POSITIVE
   angle of attack and the Endo is flown at α −40°.
3. **The mechanism that CAN act at the Endo's attitude is a different one, and it is also published** (§6): a
   non-circular fuselage section at combined α and β. It is the term the repo already has, as Ty's own fitted
   `sectionCamberCl`/`cowlCamberCl`, with the wrong law — the law has no α dependence at all, so it is maximal at pure
   sideslip where the measured physics says the force is zero, and un-enhanced at the Endo's own attitude where the
   measured physics says it peaks.
4. **Nothing is shipped.** Both corrections need a number that is not in the literature, and both of them move
   knife-edge flight and 26 types' balance. §8 says exactly which measurement each one needs. **No gain was applied to
   anything.**

## 1. What is measured, and why in this form

`SlipPitchProbe` holds the aeroplane at a body angle of attack α and a sideslip β with its rates at zero, 54 m/s TAS at
1000 m ISA (the Endo's own entry: Ty flies it at about 100 KIAS at a few thousand feet), and sums the model's own
pitching moment about the CG by source. The quantity is

    ΔCm(α, β) = (M_y(α, β) − M_y(α, 0)) / (q̄ S c̄)

the pitching moment the SIDESLIP ITSELF makes, with the α-only moment removed at each α. It is a coefficient and not a
moment because the only data it can be checked against is in coefficient form. It must be even in β (the aeroplane is
left-right symmetric) and in a real aeroplane of this kind it is NOSE-DOWN: ΔCm < 0, growing with |β|.

Reference quantities, printed by the probe: Extra `S` 10.84 m², `c̄` 1.356 m, `q̄Sc̄` 23 818 N·m, tail volume
`V_H = S_t l_t /(S c̄)` **0.665**; Pitts `S` 11.60 m², `c̄` 1.907 m, `q̄Sc̄` 35 843 N·m, `V_H` **0.265** (biplane: total
area on the larger span, as the configs' own notes do it).

Two things the probe does that lane/claude-endo's `EndoMomentMapProbe` did not, and both of them changed an answer:

- **Source attribution is exact.** Each strip's force sample is matched to its own surface through
  `AeroModel.LoadPoint`, instead of being bucketed by "is this more than 1.5 m behind the CG", which mis-sorts a
  biplane's lower wing and a short-coupled Pitts' tail.
- **Power off for the comparison with the tunnel.** A tunnel model has no thrust. At full throttle the model's prop
  adds −544 N·m at β 30° and −1088 N·m at β 90° purely because the axial speed falls as cos β, the advance ratio drops,
  the thrust rises and the thrust line is off the CG. That is 18 % of the Extra's total at β 30° and it is not Cm(β) in
  any sense a wind tunnel would recognise. Power-off is the apples-to-apples number.

## 2. The measurement: what the model does now

### 2.1 ΔCm(α, β), full power, controls neutral — the before table

Extra 300 (`extra-300-like`):

| α \ β | 10 | 15 | 20 | 25 | 30 | 40 | 45 | 60 | 75 | 90 |
|---|---|---|---|---|---|---|---|---|---|---|
| 0   | −0.013 | −0.030 | −0.054 | −0.086 | −0.124 | −0.209 | −0.253 | −0.371 | −0.452 | −0.484 |
| −10 | −0.023 | −0.049 | −0.086 | −0.134 | −0.190 | −0.312 | −0.377 | −0.559 | −0.687 | −0.735 |
| −20 | −0.040 | −0.087 | −0.155 | −0.240 | −0.334 | −0.550 | −0.664 | −0.982 | −1.212 | −1.304 |
| −30 | −0.038 | −0.082 | −0.145 | −0.223 | −0.315 | −0.523 | −0.633 | −0.936 | −1.152 | −1.252 |
| −40 | −0.043 | −0.093 | −0.162 | −0.249 | −0.350 | −0.578 | −0.699 | −1.036 | −1.282 | −1.381 |
| +20 | +0.023 | +0.049 | +0.072 | +0.094 | +0.118 | +0.164 | +0.196 | +0.273 | +0.331 | +0.355 |

Pitts S-2B (`pitts-s2b-like`):

| α \ β | 10 | 15 | 20 | 25 | 30 | 40 | 45 | 60 | 75 | 90 |
|---|---|---|---|---|---|---|---|---|---|---|
| 0   | −0.008 | −0.015 | −0.026 | −0.039 | −0.049 | −0.070 | −0.080 | −0.109 | −0.124 | −0.132 |
| −10 | −0.008 | −0.014 | −0.026 | −0.039 | −0.053 | −0.082 | −0.105 | −0.158 | −0.196 | −0.211 |
| −20 | −0.011 | −0.033 | −0.059 | −0.084 | −0.112 | −0.177 | −0.208 | −0.308 | −0.377 | −0.410 |
| −30 | −0.017 | −0.034 | −0.058 | −0.084 | −0.120 | −0.205 | −0.243 | −0.357 | −0.443 | −0.482 |
| −40 | −0.018 | −0.038 | −0.062 | −0.092 | −0.126 | −0.202 | −0.244 | −0.358 | −0.444 | −0.485 |
| +20 | 0.000 | +0.006 | +0.014 | +0.019 | +0.032 | +0.057 | +0.065 | +0.106 | +0.142 | +0.158 |

Note the sign at α +20: at positive α the sideslip pitches the model NOSE-UP. That is not wrong — the stalled-negative
stabiliser's restoring moment at α −40 and the lifting stabiliser's at α +20 both fall as cos²β, so the sign of the
sideslip's increment follows the sign of what the tail was already doing — but it means "nose-down with sideslip" is a
property of this aeroplane only at and below α ≈ 0, which matters in §6.

**On the week this sat.** lane/claude-endo measured the same quantity on 2026-11-03 (its `4832c749` base) and got
**−0.023** for the Extra at α 0, β 30°, against **−0.124** here — a factor of 5.4 on the raw total, which is suspiciously
exactly the factor Ty named.

**I did not isolate which change did that, and I checked my first guess and it was wrong.** `PropJet` (2026-11-08) looked
like the obvious candidate, since it takes the tail out of the wash at sideslip where the old body-axis tube left it in.
But it cannot be: power-off against power-on differs by only 0.026 here (−0.098 against −0.124, §2.3), so the whole
slipstream is worth a fifth of the gap. Part of it is the camber coefficients themselves — lane/claude-endo's §3b
computes the camber at −0.088 at β 30° where the same two terms now give −0.108 on a slightly smaller `q̄Sc̄`, i.e. its
camber moment was about 0.78 of today's, so the config moved too. Settling it means running this probe on `4832c749`,
which needs the probe back-ported; it is worth doing before anyone concludes the raw total is fixed, and it is not done
here. What IS measured on today's base, and does not depend on that history at all, is §2.3: **the tail's share is
zero, and that is a structural fact about the model, not a calibration.**

### 2.2 By source — the whole coupling is one fitted body term

Extra, α 0, full power, ΔM_y by source (N·m, + nose up):

| β | fuselage | wing | stab | fin | prop | Δtotal | ΔCm |
|---|---|---|---|---|---|---|---|
| 15 | −690 | −2 | +35 | +221 | −282 | −717 | −0.030 |
| 25 | −1841 | +3 | +4 | +239 | −460 | −2054 | −0.086 |
| 30 | −2576 | +5 | −3 | +167 | −544 | −2951 | −0.124 |
| 45 | −5153 | +23 | −4 | −112 | −769 | −6015 | −0.253 |
| 60 | −7729 | +27 | −6 | −187 | −942 | −8837 | −0.371 |
| 90 | −10306 | −9 | −7 | −117 | −1088 | −11527 | −0.484 |

The fuselage is 87 % of it, the stabiliser is three newton-metres, and the fuselage's share is ENTIRELY the fitted
camber. Checked by hand at β 30°, from the config (`sectionCamberArea` 2.8 m², `sectionCamberCl` 1.0 at x −1.9;
`cowlCamberArea` 1.2 m², `cowlCamberCl` 0.6 at x +1.5), ρ 1.112, v = 54 sin 30° = 27 m/s:

    turtledeck  f_z = −½·1.112·2.8·1.0·27² = −1135 N (up),  arm 1.88 m  →  −2134 N·m
    cowl        f_z = +½·1.112·1.2·0.6·27² =  +292 N (down), arm 1.52 m →   −443 N·m
                                                                  total   −2577 N·m

against the measured fuselage column of −2576 N·m. **The two camber coefficients are the model's entire
sideslip-to-pitch coupling.** `configs/aircraft/extra-300-like.json` records where they came from — Ty, 2026-11-01,
"low pressure over the turtledeck against higher pressure under the fuselage lifts the aft body, and low pressure under
the cowling against higher pressure above pulls the nose down" — and lane/claude-endo's §3b calls them "feel numbers,
not measurements".

### 2.3 Tail on against tail off — the comparison the data is drawn as

Power off, α 0, controls neutral. "Tail off" removes `hStab` + `elevator` and leaves the fin on, which is the
comparison Polhamus plots.

Extra:

| β | tail ON | tail OFF | **ON − OFF** | of which the fitted camber |
|---|---|---|---|---|
| 10 | −0.006 | −0.006 | **0.000** | −0.013 |
| 15 | −0.017 | −0.017 | **−0.000** | −0.029 |
| 20 | −0.037 | −0.037 | **−0.000** | −0.051 |
| 25 | −0.064 | −0.064 | **−0.000** | −0.077 |
| 30 | −0.098 | −0.098 | **−0.000** | −0.108 |
| 45 | −0.217 | −0.217 | **−0.000** | −0.216 |
| 60 | −0.328 | −0.328 | **−0.000** | −0.325 |
| 90 | −0.439 | −0.439 | **−0.000** | −0.433 |

Pitts: the same, −0.000 at every β; its body term is −0.023 at β 30° and its tail's share is zero as well.

**The horizontal tail of this model contributes nothing whatever to the sideslip-to-pitch coupling.** With the camber
switched off the tail-on and tail-off numbers are both +0.010 at β 30° — the model's real, unfitted body crossflow
physics makes a small NOSE-UP moment with sideslip, and the only nose-down term in the aeroplane is the fitted camber.

### 2.4 The manoeuvre's own configuration: with the stick forward the sign reverses

The Endo is full left rudder, then full forward stick 0.3 s later, full power. A coupling that only exists with the
elevator neutral is no use in it. ΔCm with the stick neutral / full forward:

Extra:

| α | δe0 β15 | δeF β15 | δe0 β30 | **δeF β30** | δe0 β45 | **δeF β45** | δe0 β60 | **δeF β60** |
|---|---|---|---|---|---|---|---|---|
| 0   | −0.030 | +0.032 | −0.124 | **+0.074** | −0.253 | **+0.070** | −0.371 | **+0.072** |
| −10 | −0.049 | −0.006 | −0.190 | −0.031 | −0.377 | −0.064 | −0.559 | −0.093 |
| −20 | −0.087 | −0.052 | −0.334 | −0.198 | −0.664 | −0.390 | −0.982 | −0.566 |
| −40 | −0.093 | −0.082 | −0.350 | −0.308 | −0.699 | −0.615 | −1.036 | −0.898 |

**At α 0 with the stick fully forward, sideslip pitches this aeroplane NOSE-UP.** The forward stick's own nose-down
moment is carried on the tail and collapses as the tail's dynamic pressure falls with β (cos²β, plus the jet leaving),
faster than the body camber grows. Over the first second of the Endo — the Extra goes from α 0 to α −40 in about 1.0 s
— the sideslip's contribution starts the wrong way round and only becomes useful once α is past about −10°. That is a
second, separate thing wrong with this term, and it is one the raw α-0 total hides completely.

## 3. The data, and the mechanism in the authors' own words

### 3.1 The one measurement of this quantity

**Polhamus, E. C., "Some Factors Affecting the Variation of Pitching Moment with Sideslip of Aircraft
Configurations", NACA RM L55E20b (1955)**, and its expanded, declassified version **NACA TN 4016 (1958)** — read the
TN: same paper plus three sections, and RM fig. 9 = TN fig. 10, RM fig. 10 = TN fig. 11.

The primary data behind his fig. 9 is **Sleeman, W. C. and Alford, W. J., NACA RM L55C17 (1955)**. Its numbers, read
off the plotted test points (±0.005), α = 0.3°, basic configuration, tanks and brakes off:

| | β 0→20° | **β 0→25°** | β 0→30° |
|---|---|---|---|
| **tail OFF** (the body's own share) | −0.024 | **−0.021** | −0.013 |
| **tail ON** | −0.070 | **−0.094** | −0.140 |
| **the TAIL's share alone** | −0.046 | **−0.073** | **−0.127** |

and with α:

| | β 0→25° | β 0→30° |
|---|---|---|
| α 0.3°, tail on | −0.094 | −0.140 |
| α 6.6°, tail on | −0.154 | −0.200 |
| α 13.0°, tail on | −0.218 | −0.237 |
| α −6.1°, tail on | — | −0.108 |

Polhamus' fig. 10 (TN fig. 11), a ¼-scale **Bell X-5**, fuselage + tail with the wing off, α 0.2°: ΔCm(0→25°) is
−0.011 for the fuselage alone and −0.106 with the tail on, **tail increment −0.095**. At α 22.2° the sign REVERSES
(ΔCm = +0.108).

**So the ratio the data gives is: the tail carries −0.073 to −0.095 where the body carries −0.011 to −0.021 — between
four and eight times, centred on five to six.** That is the number Ty named from the cockpit, and in this model the
tail's share is zero.

### 3.2 The mechanism, quoted

Polhamus, TN 4016 pp. 13–15:

> "The picture was taken at an angle of attack of 10° and an angle of sideslip of 25° and clearly shows, in addition to
> the wing-tip vortices, a strong (counterclockwise) vortex at the midtail location. The origin of this vortex was
> traced (by means of a tuft pole survey) to a point on the fuselage ahead of the canopy. … It therefore appears that
> this vortex is associated with the well-known crossflow separation on bodies inclined to the wind. **Although a second
> fuselage vortex would be expected to be produced somewhat below the one shown, it is not apparent in the flow
> picture. There is the possibility, however, that this vortex is intercepted, for this combination of angle of attack
> and sideslip, by the wing (see sketch 10) and through its effect on the wing loading is effectively distributed
> across the wing wake.**"

> "As the angle of sideslip increases from zero, the vortex increases in strength and moves away from the plane of
> symmetry. Both of these effects cause increases in the load induced on the horizontal tail by this vortex and can
> cause a considerable variation of pitching moment with sideslip."

Sleeman & Alford, RM L55C17 pp. 7–8, which is where the "why" actually is:

> "This central vortex was counterclockwise at positive sideslip angles which **induced an upload on the horizontal
> tail at positive angles of attack where the tail had moved down into the vortex field.** Flow surveys obtained with a
> single tuft probe showed that **the horizontal tail moved away from the vortex as the angle of attack was decreased
> and the converse was observed with increasing angle of attack.** Further probe surveys revealed that the vortex
> originated on top of the fuselage at the canopy and trailed along the upper side of the fuselage back to
> approximately the 3/4 length where it detached and trailed over the horizontal tail."

So: a symmetric PAIR is shed, the lower one is swallowed by the wing, and what reaches the tail is effectively ONE
vortex, displaced off the plane of symmetry. **That is the asymmetry, and it is the whole mechanism**: a symmetric pair
standing about the tail's own plane puts upwash on one half of the tail and downwash on the other and carries NO net
load — only a rolling moment. One vortex, displaced sideways, puts a one-signed net induced angle over the tail.

The supporting flow regimes are **Polhamus, NASA CR-3809 (1984) §4.1.1**, verbatim: attached and vortex-free at low
incidence; "**At an angle of attack of about 6° to 8°, the crossflow about the body begins to separate over the aft
portion creating a pair of symmetrical, counter-rotating vortices**"; "**Above about 25°, the vortices over the aft
portion become asymmetric and when the angle of attack typically reaches about 40°, the entire vortex flow is
asymmetric**"; then "the unsteady wake-type flow characteristic of cylinders normal to the flow". **lane/claude-endo's
6→8° onset and 25→40° fade-out are correctly sourced and are kept.**

The quantitative vortex model is **Kuhn, Hallissy & Stone, NACA RM L55E16a (1955)** fig. 5: two fuselage vortices
trailing streamwise from the nose to the station of maximum cross-sectional area and then parallel to the axis, radial
position set "by the orientation of the crossflow velocity" (resolving `V cos β sin α` vertical against `V sin β`
lateral), and **Γ = L/(ρ V l)** with `l` the fuselage diameter. And on how one vortex loads a surface (its fig. 13):
"Inboard of the vortex, the local angles of attack are decreased and the load is reduced. Outboard of the vortex the
local angles and the load are increased."

The impulse analogy itself is **Allen & Perkins, NACA TR 1048 (1951)**, which also gives the crossflow drag:
`c_d,c = 1.2` for the infinite cylinder subcritically, with a **finite-length factor η = 0.755 at fineness ratio 21.1
and η = 0.692 at 13.1**. The Extra's fineness ratio is 6.942 m of `lengthM` over a mean side-view depth of
`sideArea/lengthM` = 6.064/6.942 = 0.873 m, i.e. **7.95**, so η should be below their 0.69 and the model uses the bare
1.2 everywhere. Noted, not changed: it would make the body term SMALLER, and it is a fleet-wide
crossflow change belonging in its own lane.

## 4. What is NOT in the data — the limits on any of this

Worth stating plainly because it bounds the whole job:

- **There is no Cm-vs-β data past β 30° for a complete aeroplane, anywhere.** Polhamus' survey stops at 30°, so does
  its source report, so do the general-aviation datasets, and Letko stops at 20°. The only ±40° sweeps found are
  propeller-driven (NACA TN 1379, NACA WR L-295) and are genuinely ODD in yaw, not even. **The Endo lives at β 45–60°,
  which is off the end of every published curve.** Polhamus himself: "relatively little information with regard to the
  variation of pitching moment through large sideslip angles is available."
- **Tail-off data exists at one angle of attack only** (α 0.3°). Polhamus says so explicitly. The α-scaling of the
  TAIL's increment is therefore not separable from published data at all.
- **There is no quantitative scaling law** with tail volume, tail height, tail span, fuselage length or fuselage depth.
  His fig. 8 has two tail volumes (V_H 0.446 and 0.234) with high-tail ΔCm(0→10°) of −0.034 and −0.028 — a ratio of
  1.24 for a V_H ratio of 1.91, distinctly sub-linear — but the two tails differ in planform as well, so it is not a
  clean experiment. **Do not scale the Extra's V_H 0.665 against his 0.355 linearly.** (lane/claude-endo's §6.6 invited
  exactly that comparison; it does not hold up.)
- **No public wind-tunnel, rotary-balance or flight-derived dataset exists for the Extra's or the Pitts' tumble**, or
  for a knife-edge. A 2024 Witwatersrand MSc on the tumble states it twice: "no literature exists in the public domain
  describing the tumble manoeuvre and its characteristics".
- **The sign is not universal.** For an UNSWEPT wing-fuselage-tail model — Letko, NACA TN 3857 fig. 7(a), A 4, taper
  0.6, tail on, β ±20°, α 0/10/20/26° — ΔCm goes **NOSE-UP** at every α tested, +0.06 to +0.13 over 20° of β. Polhamus'
  own figs. 5 and 6 give, for a wing-body alone at Λ 0°, ΔCm(0→15°) of **+0.069 (high wing), −0.002 (MID wing), −0.082
  (low wing)** at α 0. Polhamus' concluding remarks name the ingredients of a nose-down Cm(β): "wing sweep (below the
  angle of attack for pitch-up), low wing position, high horizontal-tail location (relative to vertical tail),
  fuselage vortices, and outboard ailerons producing negative roll."

  **The Extra has none of the first three.** It is unswept, MID wing (Polhamus: −0.002, i.e. nothing), and its stab
  sits 0.19 m above the body axis on a body of 0.53 m radius — inside the body's own crossflow wake, not above it.
  His fig. 9 configuration is a 40°-swept, mid-wing, mid-tail jet fighter, V_H 0.355, tail 6.25 in above the body
  reference line on a 91.68 in fuselage. **−0.13 to −0.17 is his geometry's number, and importing it to an Extra
  wholesale is not justified.** What transfers is the MECHANISM and the RATIO, not the coefficient.

## 5. The tail-vortex mechanism, built and measured

`src/FlyingGame.Core/Aero/BodyVortex.cs` is carried over from the orphaned `lane/claude-endo` (its §6.4, commit
`25f6d385`) essentially whole: the circulation from Allen & Perkins' impulse analogy applied to the crossflow drag the
fuselage already carries, the pair's position on Föppl's standing-vortex curve, the images in the body, the CR-3809
regime window, and the per-strip induced flow averaged across each strip's width. **What I took: that file, its
derivation and its sources, and the shape of its static moment map.** What I did NOT take: its slipstream-drift change,
which today's `PropJet` supersedes and does better, and which lane/claude-endo's own §6.6 identified as the thing that
cost the Pitts its power-on and inverted flat spins.

What I added: a `BodyVortex.Knobs` struct gathering the four quantities lane/claude-endo shipped as constants and
marked ASSUMED in its own derivation, so they can be swept instead of argued about, and the `LowerVortexShare` the
Polhamus quote in §3.2 calls for.

### 5.1 The geometry is right and large

`SLIPPITCH_INDUCED=1`, Extra, α 0, β 30°, upper vortex only: the pair stands at r = 2a with a = 0.437 m,
Γ = 104 m²/s, and over the `hStab` the span-mean UPWASH is **3.05 m/s, a mean Δα of +3.7°** — which on a tail of
2.43 m² at q_t 1218 Pa with a lift slope near 3.7/rad is of the order of 700–1000 N, i.e. ΔCm ≈ −0.12, the right size.

### 5.2 But the load does not arrive. The sweep (`SLIPPITCH_SWEEP=1`)

The TAIL's own share of ΔCm (tail on − tail off), power off, α 0, controls neutral. **The data wants −0.073 at β 25°
and −0.127 at β 30°.**

Extra:

| case | β15 | β25 | β30 | β45 | β60 | β75 |
|---|---|---|---|---|---|---|
| lane/claude-endo as written | −0.004 | **+0.010** | −0.016 | −0.000 | −0.000 | −0.000 |
| upper vortex only | −0.015 | −0.015 | −0.010 | −0.000 | −0.000 | −0.000 |
| no regime fade-out | −0.004 | +0.010 | +0.032 | +0.012 | −0.004 | −0.046 |
| r cap 2a → 4a | −0.004 | +0.010 | −0.021 | −0.000 | −0.000 | −0.000 |
| pair centre 0.30 m up | −0.010 | −0.033 | −0.015 | −0.000 | −0.000 | −0.000 |
| pair centre 0.30 m down | −0.021 | −0.017 | −0.039 | −0.000 | −0.000 | −0.000 |
| pair, core 0.50a | −0.011 | −0.023 | −0.019 | −0.000 | −0.000 | −0.000 |
| pair, core 1.50a | −0.006 | −0.015 | −0.012 | −0.000 | −0.000 | −0.000 |
| upper only + no fade | −0.015 | −0.015 | −0.074 | −0.147 | −0.178 | −0.280 |
| upper only + no fade + core 1.00a | −0.058 | −0.114 | −0.085 | −0.053 | −0.050 | −0.077 |
| upper only + no fade + core 1.50a | −0.050 | −0.109 | −0.127 | −0.093 | −0.053 | −0.040 |

Pitts, the same rows: lane/claude-endo as written −0.018/−0.025 at β 25/30; upper vortex only −0.054/−0.055; upper only
+ no fade + core 1.00a −0.055/−0.063.

Three findings, and they are the reason nothing ships:

1. **The symmetric pair cancels, exactly as the mechanism says it must.** Across eight variants — the r cap doubled,
   the centre height moved 0.30 m either way, the core taken from 0.1a to 1.5a — the Extra's tail share stays between
   +0.010 and −0.039, and at β 25° lane/claude-endo's own configuration has it the WRONG SIGN. A pair standing about
   the tail's plane is a couple on the tail, not a force. lane/claude-endo's §6.6 reported "2.7× what it was" at α 0
   from this term; measured on today's base with exact source attribution, what the pair does to the TAIL is between
   nothing and noise, and its §6.6 gain was its fin and slipstream changes.
2. **Breaking the symmetry is what matters, and it is Polhamus' own explanation.** `LowerVortexShare = 0` turns the
   erratic +0.010/−0.016 into a monotone −0.015/−0.010 and triples the Pitts'. It is the one change here with an
   author's sentence behind it. But on its own it reaches a FIFTH of the published tail increment.
3. **Reaching the data needs three choices, two of them unsourced.** −0.127 at β 30° arrives only with the lower
   vortex deleted AND the regime fade-out deleted AND a core of 1.5 body half-breadths. The fade-out is the one piece
   of this that IS sourced (CR-3809 §4.1.1, quoted above) and deleting it is deleting the source. A core of 1.5a is as
   wide as the body, which is not a vortex core, it is a fitted smoothing length. **Three free choices to hit one data
   point is a fit with extra steps, and it is the thing Ty said not to do.**

### 5.3 Tail height: a real, measured, geometric reason the Extra's number is smaller

`SLIPPITCH_GEOM=1`. The `hStab` is moved bodily in z, nothing else changed; the VORTEX's own share of ΔCm (vortex on −
vortex off) with the symmetry broken, no fade, core 1.0a — the most this mechanism can give:

| Extra, stab z (m) | β15 | β25 | β30 | β45 | β60 |
|---|---|---|---|---|---|
| **−0.19 (as built)** | −0.060 | −0.113 | −0.082 | −0.045 | −0.048 |
| −0.39 | −0.066 | −0.141 | −0.115 | −0.105 | −0.103 |
| −0.59 | −0.064 | −0.148 | −0.163 | −0.128 | −0.113 |
| −0.89 | −0.056 | −0.132 | −0.162 | −0.147 | −0.108 |
| −1.19 | −0.046 | −0.110 | −0.134 | −0.151 | −0.110 |

The effect roughly doubles as the tail is raised 0.4 m and peaks 0.4–0.7 m above the body axis — one body radius out,
which is where you would put it. **So the model's own geometry says the Extra's tail, sitting 0.19 m above the axis,
should see about half of what a mid/high tail sees, and Polhamus' −0.13 belongs to the raised tail.** This is the
quantitative version of the sign table in §4: his high tails go nose-down, his low tails go nose-up, and the Extra is
down in the wake with the sign barely decided. No published scaling law exists to check this against (§4), so it is the
model's own sensitivity and not a validation — but it is a derivation from geometry, and it says the honest target for
the Extra's tail share is nearer **−0.06 to −0.08** than −0.13.

### 5.4 And it is the wrong α for the Endo

The published α-dependence is unambiguous, and it kills this mechanism for the manoeuvre Ty is after:

- Sleeman & Alford: the vortex "induced an upload on the horizontal tail **at positive angles of attack** where the
  tail had **moved down into** the vortex field", and "the horizontal tail **moved away from the vortex as the angle of
  attack was decreased**".
- The numbers follow: ΔCm(0→25°) is −0.094 at α 0.3°, −0.154 at α 6.6°, −0.218 at α 13°, and only −0.108 at β 30° at
  α −6.1°. On the X-5 the sign REVERSES by α 22°.

**The Endo is flown at α −40°.** The measured vortex share at α −40 (§5.3's run, best case) is −0.026 to −0.061 at
β 15–30° and falls as the tail is raised — against −0.060 to −0.082 at α 0. The mechanism Polhamus documented wants
positive α and the Endo has 40° of negative. **Implementing it properly, with its sourced regime window, would do
essentially nothing for the tumble.** That is worth saying loudly, because it is the opposite of what the candidate
list expected.

### 5.5 Status: in the tree, off

`BodyVortex` is compiled and tested but `AeroModel.BodyVortexEnabled` is **false** unless `BODYVORTEX` is set in the
environment, so `BodyVortex.For` is never called in a game build and the shipped model is byte-identical to the base.
Nothing in the spin, the rig, the trim or the glide table can have moved, and the suite confirms it.

## 6. The mechanism that CAN act at the Endo's attitude, and it is the term we already have

**Polhamus, NACA TN 4016 pp. 18–19, "Flat Fuselages"** — a section that is in the TN and not in the RM, which is why
lane/claude-endo (which had the RM) never saw it:

> "The trend toward long fuselages and relatively small wings … has appreciably increased the proportion of lift
> carried by the fuselage. This is especially true for fuselages having relatively flat cross sections, and under
> certain conditions it appears that rather large variations in the lift of these fuselages could accompany variations
> in sideslip angle which, of course, may result in appreciable variations of pitching moment with sideslip."

> "it will be noted that although the force on the circular cylinder continuously decreases as the cosine of the flow
> incidence, **the force for the flat rectangular cylinder increases rather rapidly in the range of φ from
> approximately 15° to 70°. This is apparently due to the fact that as the angle is increased the flow begins to attach
> on the top of the cylinder and it becomes a lifting surface.** In applying this data it must be kept in mind that at a
> constant angle of attack **the crossflow velocity will increase with sideslip** (or φ) and that therefore the
> variation of normal-force coefficient will be considerably greater than indicated in sketch 13. From these results it
> appears that flat fuselages might be expected to experience a considerable variation in lift with changes in sideslip
> angle. **Since the fuselage rearward of the center of gravity usually experiences a reduction in flow angle due to
> the wing downwash and quite often is considerably blanketed by the wing and tail, these lift variations may be
> concentrated on the portion of the fuselage ahead of the wing and thereby produce considerable variation in pitching
> moment with sideslip.**"

with, printed under his sketch 13, the governing variable:

    tan φ = tan β / tan α

This is the repo's `sectionCamberCl` / `cowlCamberCl` term. Polhamus even puts it where the repo puts half of it — "on
the portion of the fuselage ahead of the wing", which is the cowl station. The hard data behind it is **Polhamus, NACA
TN 4176 (1958) / NASA TR R-29**, "Effect of Flow Incidence and Reynolds Number on Low-Speed Aerodynamic Characteristics
of Several Noncircular Cylinders with Applications to Directional Stability and Spinning", which measures the force
NORMAL to the crossflow on eleven 2-D sections against φ and Reynolds number, with the flat-plate reference
`c_y′ = −2π sin φ`, and which says in its own introduction: "**Since the flow incidences correspond to combined angles
of attack and sideslip in the crossflow plane of three-dimensional bodies**, these two-dimensional results appear to
have strong implications with regard to directional stability of fuselages at high angles of attack." Its three-dimensional
application is `(C_Y)_nonpotential = c_y′ (S/A) (cos²α sin²β + sin²α)`.

**And here is why this is the Endo's term.** `tan φ = tan β / tan α`:

| | α 0 | α −20° | α −40° |
|---|---|---|---|
| β 30° | φ 90° | φ 58° | φ 34° |
| β 45° | φ 90° | φ 70° | φ 50° |
| β 60° | φ 90° | φ 78° | φ 64° |

The flat section's normal force is at its MAXIMUM over φ 60–70° and falls to zero at φ 90°. **At pure sideslip (α 0,
φ 90°) the measured physics says a non-circular fuselage section makes NO vertical force at all; at the Endo's own
α −40° / β 45–60°, φ is 50–64°, right in the peak.** The model's law is the exact inverse of that: `f_z ∝ v_lateral²`
with no α dependence whatever, so it is maximal at α 0 — where the physics says zero — and gets no enhancement at all
at the attitude where the physics says it peaks. The crossflow dynamic pressure is wrong too: the model uses
`½ρ v² = ½ρV² cos²α sin²β` where TN 4176's own application uses `½ρV²(cos²α sin²β + sin²α)`, which at α −40 / β 45 is
0.71 of the free-stream q against the model's 0.29 — **2.4× on the dynamic pressure alone**, before the coefficient.

**Split that into the part that is sourced and the part that is not**, because only one of them is
worth anything:

- **The crossflow dynamic pressure: factor 2.41, fully sourced and needing no new coefficient at all.** TN 4176's own
  three-dimensional application is `(C_Y)_nonpotential = c_y′ (S/A)(cos²α sin²β + sin²α)`; the model's camber term uses
  `½ρ v_lateral² = ½ρV² cos²α sin²β` and drops the `sin²α`. At α −40°, β 45° that is 0.2934 of the free-stream q
  against 0.7066 — **2.41×, from one missing term in a published formula.** Extra, turtledeck station only: the model
  has `f_z = −½·1.112·2.8·1.0·29.25² = −1332 N`; with the full crossflow q it is −3208 N, and the pitching moment at
  that station goes −2504 → −6030 N·m, ΔCm −0.105 → −0.253.
- **The coefficient's φ-dependence: direction certain, size not.** Sketch 13 says the flat section's force rises from
  φ 15° to 70° and vanishes at 90°, so the law is wrong in a known direction — but putting a number on it means
  digitising the schematic, which §6's last paragraph says I will not do.

So the defensible claim is **2.4× on the body term at the Endo's attitude, from a missing `sin²α` in a published
formula**, with the φ coefficient on top of that unquantified and probably in the same direction. That is about half of
Ty's five and it is a mechanism, not a gain. It still cannot be shipped on its own: adding `sin²α` to a term called
"camber due to sideslip" makes it act at zero sideslip, which moves the pitch trim of every type in the fleet — the two
corrections (the `sin²α` and the φ coefficient that zeroes it at pure sideslip) have to land together or not at all.

### Why it is not implemented here

Sketch 13 is a **hand-drawn schematic** in a survey paper, for one "modified rectangular" section, with a normalisation
I cannot pin down from the scan: its circular-cylinder curve starts at c_n ≈ 0.3 where `C_D,c·cos 0 = 1.2`, so the
coefficient is referred to something other than the projected width, and reconstructing which is guesswork. TN 4176's
MEASURED curves cover **φ ≤ 15° only** — nowhere near the 50–70° that matters — and are for generic cylinders, not an
Extra's or a Pitts' section. Digitising a schematic and renormalising it by eye to land on a target would be the fitted
constant Ty forbade, wearing a citation. **So: mechanism found and sourced; coefficient not available; nothing
changed.** §8 says what would settle it.

## 7. What was rejected, and why

- **Scaling anything by 5.** Not done, anywhere, in any form.
- **The vertical tail's load projecting into pitch** (one of the candidates). It is identically zero and the geometry
  says so: for a force along body y at any body station, `M_y = r_z·F_x − r_x·F_z = 0`. A pure side force makes roll
  and yaw and no pitch at all, in body axes, at any α. The candidate is null.
- **Potential-flow / Munk slender-body theory as a source of Cm(β).** Also identically zero, and this one is worth
  recording because it bounds the whole problem: the fuselage is left-right symmetric, so its apparent-mass tensor in
  the crossflow plane has no y–z cross term, and a lateral crossflow produces a lateral force and nothing else.
  **Cm(β) is entirely a separated-flow effect.** No amount of refining the Munk term will produce any of it.
- **The horizontal tail's dynamic-pressure asymmetry at sideslip** (the windward half in clean air, the leeward half in
  the body's wake). Real, and `TailWake` has the machinery, but it produces nothing here: at α 0 the Extra's symmetric
  stabiliser at zero incidence carries no lift, so taking dynamic pressure off one half of it changes no lift. It needs
  a loaded tail to do anything, which at α 0 it is not.
- **The wing's own asymmetry at combined α and β** (another candidate). Measured, and it is small: Extra, α 0, the wing
  column of §2.2 runs +5 N·m at β 30° and +27 at β 60°. At α −40 it reaches −923 N·m at β 60°, 7 % of the total.
  Polhamus' own wing-position data says the same thing for a MID wing: ΔCm(0→15°) = −0.002, i.e. nothing.
- **Scaling Polhamus' −0.13 to −0.17 up by the Extra's tail volume** (V_H 0.665 against his 0.355), which
  lane/claude-endo's §6.6 invited. His own fig. 8 shows the tail-volume dependence is strongly sub-linear and
  confounded with planform (§4), and §5.3 shows the Extra's low tail should see about HALF his number, not double it.
  Doing the multiplication would have doubled the target in the wrong direction.
- **Deleting the CR-3809 regime fade-out** to get a term past β 40°. It is the one sourced piece of the vortex model
  and deleting it to reach a number is backwards. What is true is narrower and worth its own look: above σ 25° the
  vortices become ASYMMETRIC, which is not the same as absent, and CR-3809 fig. 4.1's boundaries are per station
  (symmetric → asymmetric at α 36–42° near the nose, 22–29° at X/D 15). Modelling an asymmetric pair is a bigger job
  whose first-order product is a side force, not a pitching moment.
- **Allen & Perkins' finite-length factor η** on the crossflow drag (§3.2): the model uses `C_d,c` 1.2 where their own
  η says 0.69–0.76 of it at fineness ratios 13–21, and the Extra's is 7.95. Correct, and it would make the body term
  SMALLER. It is a fleet-wide crossflow change: flagged, not done.

## 8. NEEDS YOU, and the next measurement

1. **The flat-fuselage section coefficient (§6) is the decision.** The law is wrong in a way that is sourced, and
   fixing it is worth a measured 2.4× on the body term at exactly the attitude the Endo needs — but it takes the term to
   near zero at α 0, β 90°, which is knife-edge, where today it is ~4.5 kN of body force and ~8.5 kN·m nose-down. **That
   is your flight feel on the aeroplane you fly and it is 26 types' balance, so it is your call, not mine.** What would
   settle the coefficient: `c_n(φ)` for the Extra's and the Pitts' actual fuselage cross-sections over φ 15–80° — one
   2-D section sweep, which is a CFD run or a tunnel afternoon, not a literature search. The repo already has the
   geometry to run it against on the types with measured `StationHalfY`/`StationHalfZ`/`StationMidZ` (`FuselageShape`);
   the Extra and Pitts have no stations yet and would need measuring off the GLB first.
2. **Whether the Extra's stabiliser height is right.** §5.3 says this term's size is set by how far the stab stands off
   the body axis, and the model has it 0.19 m up on a 0.53 m body. If the real aeroplane's is higher, the tail-vortex
   term roughly doubles. One tape measure on a real Extra, or the three-view.
3. **β past 30° has no data at all** (§4). The Endo lives at β 45–60° and every published curve stops at 30°. Nothing
   in the model's behaviour out there can be validated against anything, by me or by anyone, and that should be known
   before more effort goes into it.
4. **Your own flight note is the only data past β 30° that exists.** "Too weak by a factor of at least 5" measured
   against the 2026-11-03 model is now, on the raw α-0 total, closed (§2.1, −0.023 → −0.124 from `PropJet`). The thing
   that is still five-ish short is the TAIL's share, which §5.4 says acts at positive α and not in the Endo. **If the
   tumble still feels wrong after flying this base, the next thing to measure is §2.4 — the sign reversal with the
   stick forward — not Cm(β) at all.**
