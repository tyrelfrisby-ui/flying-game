# Control surfaces: hinged sections, control power, stick-free float

Owner 2026-10-06: *"the previous modeling with each control surface acting as its own 'wing' has me wondering how
accurate our control power calculations were … research both control power and stick free control position now that
the hinged surface model has been adopted."*

## How a surface is built

Each lifting surface is a set of spanwise strips. A control surface is split into a FIXED row (wing / stabiliser / fin)
and a HINGED row (aileron / elevator / rudder) at the same stations. A deflection δ:

- turns the hinged strip (gain ±1): it sees its local α + δ;
- adds camber to the fixed strip ahead of it (gain g): it sees α + g·δ.

The section's lift change per δ, relative to its lift slope, is the flap effectiveness **τ = E + (1 − E)·g**, with
**E = c_f / c** the hinged chord over the whole section chord.

## What was wrong

| Issue | Effect | Fix |
|---|---|---|
| Induced drag per PIECE: the wing and its aileron row each had their own aspect ratio (aileron row ≈ 50+) | 172 induced drag ~20 % low, best glide 60 kt (POH 65) | `AeroModel.SystemAspectRatio`: one AR per lifting system |
| Stabiliser strips sampled `naca0012-like`, a **2-D** section (6.3 /rad), with no finite-span correction | Tail ~1.7× too strong: stick-fixed static margins 25–70 % MAC across the fleet | `AeroModel.SectionTableKey`: 2-D tail tables scaled to the DATCOM/Helmbold slope for the system AR (fins × 1.55 end-plate) |
| Elevator/rudder strips on a different table (`fin-lowAR`, 3.7 /rad, stall 30°) than the fixed surface | The hinged share of section lift and its stall were inconsistent with the section | Hinged strips fly the fixed surface's section table |
| Fixed-strip gain hand-set to 0.45–0.55 everywhere | τ (theory-level, inviscid) regardless of chord ratio | g from thin-airfoil τ(E) × 0.85 (viscous, DATCOM small-δ ratio for t/c ≈ 12 %) |

### Pitch stability: what else the research turned up

| Issue | Effect | Fix |
|---|---|---|
| Each piece of a hinged section loaded at its OWN quarter-chord | the section's α-lift acted at ~37 % chord; the 172's aileron row (0.9 m behind the CG) added ~0.3 of spurious Cmα stability; same at the tail | `AeroModel.LoadPoint`: a fixed/hinged pair loads at the SECTION quarter-chord, LE − ¼(c_f + c_h) |
| No fuselage pitch moment | every type with an honest CG and tail came out 25–45 % MAC stable | Gilruth & White (NACA TR 711): M = 57.3·K_f·q·w²·L·½sin 2α, K_f from the wing root's position along the body (`FuselageMunkM3`); `fuselage.munkScale` per type (Gee Bee 0.55 → +3 % MAC, owner: "just barely stable") |
| No tail dynamic-pressure ratio | tail ~10 % too strong | η_t = 0.9 (DATCOM 0.85–0.95; the slipstream raises it under power) |
| CG of the 172, Super Cub, bush Cub (commit c01221c, 2026-09-15), SR22, Archer and Glasair set from the strips as if `pos` were the LEADING EDGE (it is the quarter-chord, DATA-CONTRACTS) | CG near 50 % MAC instead of 27 %; oversized tails and the 2-D tail slope hid it | CG back to 27 % MAC (gear moved with it, so ground handling is unchanged) |
| 172 horizontal tail 3.71 m², elevator 32 % chord | — | 3.35 m², elevator 40 % (Wolfram System Modeler 172: stab 2.0 m² + elevator 1.35 m²) |

### Static margin, stick fixed (% MAC), before → after

| Type | Before | After |
|---|---|---|
| Skyhawk | 29 | 24 (UIUC 172 data: 17; Cmδe −1.52 vs −1.28) |
| Archer | 28 | 21 |
| SR22 | 26 | 14 |
| Glasair III | 21 | 15 |
| Super Cub | 15 | 18 |
| 2-33 | 18 | 6 |
| P-51 | 35 | 12 |
| Extra 300 | 42 | 9 |
| Pawnee | 32 | 4 |
| Gee Bee R-2 | 35 | 3 |
| 737 | 53 | 18 |

Real light aircraft fly at roughly 5–20 % MAC. Still high (CG or tail geometry to check against data): Pitts 44,
EB29R 42, Stearman 26. `PitchStabilityTests` prints them all and fails any type that is unstable.

### Full-aft authority (StallAuthorityTests, every type now)

With real stability most types pulled far past the stall at full aft (737 45°, Beaver 36°, Extra 34°). The protocol's
plain-flap saturation is now calibrated for every type. Low values (< 8°: F-86 3.0, Gee Bee 4.9, Swift 5.2, 2-33 6.0,
Pawnee 7.5, Cassutt 7.8, SR22 7.9) mean an elevator large for the type's stability — candidates for real geometry.
Rejected and left PENDING: the Pitts (the calibrator wanted its elevator chord ×3.6) and the Stearman (too stable to
reach its target); the Cub on floats (×1.4 chord for 0.5°) keeps its elevator.

## Control effectiveness τ (thin-airfoil theory)

Flat plate with a plain flap, Glauert: cos θ_h = 2E − 1, A₀ = α + δ(π − θ_h)/π, A_n = 2δ sin(nθ_h)/(nπ).

| E | τ theory | τ × 0.85 | fixed-strip g |
|---|---|---|---|
| 0.22 | 0.57 | 0.48 | 0.34 |
| 0.25 | 0.61 | 0.52 | 0.36 |
| 0.32 | 0.68 | 0.58 | 0.38 |
| 0.35 | 0.71 | 0.60 | 0.39 |
| 0.40 | 0.75 | 0.64 | 0.39 |
| 0.45 | 0.79 | 0.67 | 0.40 |

Large deflections: plain-flap effectiveness falls past ~15–20° (separation off the deflected surface). The elevator's
`SaturationDeg` (δs·tanh(δ/δs)) models it; it is set per type by the stall calibration (`StallAuthorityTests`).

## Hinge moments and stick-free float

A released surface floats where its hinge moment balances: C_h = C_hα·α + C_hδ·δ (+ tab, + centring spring), so
δ_free = −(C_hα/C_hδ)·α. Reference: hinged-strip area × chord, local q; α is the local flow at the hinged strip
(downwash, slipstream and rates included).

Thin-airfoil, 2-D, per rad, from the same Glauert loading:

| E | c_hα | c_hδ | float ratio c_hα/c_hδ |
|---|---|---|---|
| 0.25 | −0.57 | −0.95 | 0.60 |
| 0.30 | −0.63 | −0.97 | 0.65 |
| 0.35 | −0.69 | −0.99 | 0.69 |
| 0.40 | −0.75 | −1.01 | 0.74 |

Real sections carry about 0.55 × c_hα and 0.72 × c_hδ (boundary-layer thickening at the trailing edge, t/c ≈ 12 %).
On a finite surface: C_hα = c_hα·(a/a₀); C_hδ = c_hδ − c_hα·τ·(1 − a/a₀).

For an unbalanced 172-size elevator (E 0.32, tail AR 3.2) that gives **C_hα ≈ −0.19, C_hδ ≈ −0.61** (float ratio 0.31).
Aerodynamic balance cuts both: a horn balance roughly ×0.6 on C_hα and ×0.75 on C_hδ; Frise ailerons roughly halve C_hδ.
The configs carry C_hα −0.12…−0.20, C_hδ −0.30…−0.45: the values of balanced surfaces, plausible for the 172
(horn-balanced elevator and rudder, Frise ailerons) but copied across types without per-type sources.

## Open per-type work

- Pitts, EB29R, 737, Seminole, Stearman, floatplanes, DC-3 and others still sit at 25–60 % MAC static margin: CG and
  tail sizes to check against each type's data.
- Seminole: the real aircraft has an all-moving stabilator with an anti-servo tab; it is modelled as stab + elevator.
- Hinge coefficients per type from each surface's balance (none / horn / overhang / Frise / anti-servo tab).
