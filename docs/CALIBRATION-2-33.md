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
