# 2-33 Calibration Targets & Scorecard

Real-world reference numbers for the trainer glider, from Ty (CFI, aerobatic/UPRT) and
"Tom's Tips" (Central California Soaring Club instructor booklet on the Schweizer 2-33,
https://www.soarccsc.com/wp-content/uploads/2016/01/Toms-Tip-Booklet.pdf). The sim is graded
against these — Ty's eye is the final acceptance test.

## Reference numbers (real 2-33)

| Quantity | Value | Source |
|---|---|---|
| Stall speed | 34 mph dual / 31 mph solo | Schweizer manual — SIM NOW MATCHES: 33.9 mph with documented wing |
| Best glide | ~20:1 class (strut-braced trainer) | Ty (revised from 25:1) |
| Pattern minimum | 55 mph calm, more when gusty | Tom's Tips |
| Spin altitude loss | **~200 ft per turn** fully developed | Tom's Tips |
| Spin altitude loss | ~300 ft/turn at ~100 ft/s descent | Ty |
| Implied rotation | ~2.5–3.0 s/turn | derived from both |
| Spin CG dependence | 180 lb in BACK seat spins; more front weight = won't spin | Tom's Tips (matches sim's CG sensitivity) |
| Spin character | one wing stalled, one flying; rotation toward low wing | Tom's Tips (matches sim spanwise tables) |
| Entry technique | full aft stick + full rudder + ~3 in OPPOSITE aileron (adverse yaw assist) | Tom's Tips |
| Recovery | full opposite rudder, then merely RELIEVE back pressure | Tom's Tips |

## Sim scorecard (updated 2026-09-06 evening, commit-current)

| Metric | Target | Sim |
|---|---|---|
| Best glide | ~20:1 | 20.0 @ 23 m/s ✓ |
| Spin sink | ~80–100 ft/s | ~85 ft/s ✓ |
| Spin direction always commanded | yes | yes ✓ (after control-effectiveness fade) |
| Inside-tip AoA >90° in developed spin | yes (Ty) | reached 98–117° in fast-rotation states ✓ |
| ft per turn | **200 (owner goal)** | ~354 ✗ (steady, trending) |
| s per turn | 2.5–3.0 | 4.1 steady ✗ |
| spin sideslip | ~10–20° | ~19° ✓ (was −33: plate rudder + finite-AR drag) |
| spin stability (no flatten/fall-out) | stable | very stable ✓ (max α 23, hysteresis) |

## Open physics items (in priority order)

1. **Rotation rate** (goal 200 ft/turn). DONE: stall hysteresis (lagged wake state in Aircraft, fast
   separation ~0.25s / slow washout ~1.0s) — stabilized the thickened-wake config that used to limit-cycle.
   REMAINING LIMITER (measured): at spin beta ~-30 the rudder's local flow angle exceeds the fin airfoil's
   stall, so the control-effectiveness fade guts the pro-spin rudder command. Fixes to explore, in order:
   (a) split control-surface DRAG from lift-effectiveness (deflected rudder/aileron keep their drag and
   its yaw moment even when separated); (b) re-balance the low-AR fin table (fin-lowAR, in config, currently
   unassigned — it weathervanes so hard the spin dies even 75% blanketed) together with deeper fin
   blanketing; (c) beta equilibrium (fuselage side-crossflow arm: -0.5 works, -1.2 tumbles the aircraft).
2. **Sideslip magnitude** in developed spin (−30..−50°, should be ~10–20°): fuselage side crossflow
   arm may need tuning; fin stall shape.
3. Stall-speed check vs 34 mph placard once cockpit IAS (pitot-style, α-corrected) exists.

## Experiments run and closed (don't repeat blind)

- **Stab-stalls-elevator-doesn't** (owner theory, tested 2026-09-06): wing deep-Cm x0.75 + stab
  post-stall lift cut + elevator exempt from fade → pitch-tumbles (alpha 107, reversals). Tempered
  (elevator floor 0.6): developed spin unchanged (349 vs 354 ft/turn) but ENTRY slows to 13+ s
  (real: "a few seconds" per Tom). Reverted. The theory may still be right in a model where the
  stab's own lift and the elevator's command are separate surfaces (today they share one strip row).
- Attractor is robust at ~4.1 s/turn / alpha 24 against: inertia (+17%), tail volume (-45%), blanket
  depth, rudder power, elevator power, deep Cm (x0.49 total), CG (aft tips into reversal).
  Next principled lever: measured full-range section data (Sheldahl/Critzos type) for the separated
  branch at 20-30 deg — where the equilibrium actually sits — rather than more factor tuning.


## Phase-portrait energy budget (branch, V2 cycle, 2026-09-06)

Pitch work per component over one orbit (sign pattern robust; magnitudes approximate —
component isolation cannot replicate per-strip memory): WING +2519 J and INERTIAL COUPLE
+1875 J PUMP the cycle; stab −7295, elevator −5225, ailerons −2538 damp it. The cycle's
engine is the wing's deep-alpha pitching moment — all remaining spin work converges on
measured high-AoA CP-walk data for the wing section.


## Research base + standing methodology (owner directive 2026-09-06: data, not guesses)

Sources in the model now:
- NACA TN-1045 / TN-1329 (Neihouse et al.): stab-wake rudder shielding — wake wedge between a
  60-deg line from the stab LE and a 30-deg line from its TE in spin-vertical flow; fin/rudder area
  inside is blanked, below-stab area survives. IMPLEMENTED in AeroModel (ramps in for flow angles
  30->60 deg). TN-1329 also notes premature down-elevator shields the rudder (recovery realism).
  TDPF/TDR criteria available in TN-1329 (downloaded) for future tail-design checks.
- Sheldahl/Critzos-character measured full-range section curves (finite-AR scaled) for wing + tail.
- Documented geometry: manual wing area/span, 3-view tail arm/areas, component-mass inertia.

Standing rule: model changes must cite measured data or documented geometry. Remaining data wanted:
(1) elevator/rudder effectiveness measurements at high AoA (NASA stall/spin program, Langley
spin-tunnel reports), (2) wing-section CP walk 30-90 deg measured (drives the pitch limit cycle
per the energy budget), (3) 2-33 flight-measured spin numbers beyond Tom's Tips.


## CP-walk sweep results (branch, 2026-09-06 late)

Five wing CP-walk profiles (Cm = -(Cp-0.25)*CN from the table's own forces), V2 spin, 30 s:

| Profile | Cm@45 | net turns | avg s/t | avg alpha | beta | sink | ft/turn | reversal |
|---|---|---|---|---|---|---|---|---|
| P1 extreme (walk done by 45) | -0.242 | 3.4 | 10.9 | 15 | -32 | 60 | 661 | yes |
| P2 strong (by 60) | -0.161 | 4.05 | 8.1 | 29 | -28 | 69 | 558 | none |
| P3 classic/Critzos (by 90) | -0.097 | -1.5 | 4.5 | 43 | +22 | 72 | 325 | SUSTAINED LEFT |
| P4 late | -0.048 | 2.4 | 28 | 8 | -26 | 56 | -- | yes |
| P5 mild | -0.019 | 0.2 | 9.7 | 47 | +13 | 57 | 554 | yes |

KEY FINDING: with measured-realistic (gentler) Cm, the deep fast spin EXISTS (P3: alpha 43,
4.5 s/turn, 325 ft/turn — closest to owner targets all campaign) but locks in REVERSED: at
deep alpha the NACA-shielded rudder cannot referee direction and the beta-dihedral loop picks
its own. Enlarged below-stab rudder (36% per 3-view) insufficient alone. Direction selection
at deep alpha = THE open problem. Next: phase-space basin study; candidate real-ship direction
mechanisms: rotational flow asymmetry at the fin from spin-axis offset, fuselage side-crossflow
asymmetry, pro-spin aileron-drag differential.


## Falsification run: Cm shift removed entirely (2026-09-07)

Owner-directed. Deep Cm clamped to attached camber value across all tables, full matrix re-run:
glider spin COLLAPSES (net -0.2 vs -1.5 — the measured Cp walk is load-bearing); C172 completely
insensitive (steep spin never samples deep Cm); Pitts crossover WORSENS (-7.0 vs -4.0) and ACCEL
mode still accelerates (inertia-driven). VERDICT: keep the measured Cp walk. Crossover cause
re-confirmed as attached-authority grabs during alpha-oscillation dips — the oscillation damping
remains the single blocking physics item.


## Rotary-balance validation round (2026-09-07, NASA CR-3099)

Report downloaded (single-engine trainer, Langley spin tunnel, spin control set). Figures read:
Cn vs Omega*b/2V LINEAR damping slope ~-0.08/unit, alpha-independent 30-90 deg; Cl near-zero
plateau |Omega|<0.4 with strong damping walls (equilibrium spin rates at plateau edges).

Model vs data (virtual rotary balance, C172): Cl structure MATCHES (walls -0.11 vs -0.10, excess
propelling +0.05 in-plateau); Cn damping 6-10x TOO WEAK. Data caught a SIGN BUG in distributed-
crossflow station velocities (omega x r inverted -> anti-damping) — fixed. Physical fuselage
reaches ~-0.03 of the -0.08 target; remainder = wing rotational drag differential (finite-AR
deep-Cd cut removed real damping) -> NEXT REBALANCE: restore deep Cd toward measured, let spins
re-equilibrate per tunnel curves. DF adoption parked until then (owner: data first).

Pitts: owner angular-momentum theory -> Ixx sweep 290/430/660 vs the accelerated-spin litmus:
only Ixx 660 passes (2.1->4.9 rad/s alpha 84). Kept 660/1147/1450/40 — behaviorally validated;
low component estimates must be missing wing/strut mass; swing-test data wanted. Ailerons
verified outboard-only on both wings. Crossover deepens with Ixx (not inertia-limited).


## Tail-resolution round (2026-09-07 late) — OPEN ITEM

Tail resolution doubled fleet-wide (stab/elev 8 spanwise, fin 6 / rudder 8 vertical). Glider/C172
converged (unchanged); Pitts neutral IMPROVED 1.3->2.0. OPEN: Pitts accelerated-spin litmus FAILS
at fine resolution (full-forward recovers at all inertia combos to Ixx1200); vortex-breakdown
decline added to fin-lowAR (0.82/0.52/0.24 at 50/60/75) — insufficient alone. NEXT SESSION FIRST:
stab-wake-on-DOWN-elevator shielding (TN-1329 mechanism applied to the elevator itself), then
re-run litmus + full fleet matrix.


## Anti-spin elevator stall effect (owner-directed, 2026-09-07)

Implemented: elevator DOWN-deflection (anti-spin) loses 50% power, gated by rotation magnitude
(persists through the push, zero in normal flight) — the owner's observed 'stall through neutral
moving anti-spin'. Kept: physical, harmless attached. INSUFFICIENT for the Pitts ACCEL litmus:
even relieving back pressure alone stops our Pitts spin, because it spins at V~26 m/s (tail keeps
too much q) vs real S-2B ~16 m/s where the flyweight couple dominates. ROOT: Pitts spin
equilibrium too fast/shallow (alpha 27 vs real 35-45) — same disease the glider had. NEXT: deepen
Pitts spin equilibrium (alpha up, V down); litmus should return naturally.
