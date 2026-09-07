# 2-33 Calibration Targets & Scorecard

Real-world reference numbers for the trainer glider, from Ty (CFI, aerobatic/UPRT) and
"Tom's Tips" (Central California Soaring Club instructor booklet on the Schweizer 2-33,
https://www.soarccsc.com/wp-content/uploads/2016/01/Toms-Tip-Booklet.pdf). The sim is graded
against these — Ty's eye is the final acceptance test.

## Reference numbers (real 2-33)

| Quantity | Value | Source |
|---|---|---|
| Stall speed | 34 mph dual / 31 mph solo (indicated; most indicate a few mph higher) | Schweizer manual via Tom's Tips |
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
| Spin sink | ~80–100 ft/s | ~90 ft/s ✓ |
| Spin direction always commanded | yes | yes ✓ (after control-effectiveness fade) |
| Inside-tip AoA >90° in developed spin | yes (Ty) | reached 98–117° in fast-rotation states ✓ |
| ft per turn | 200–300 | ~400 ✗ |
| s per turn | 2.5–3.0 | ~4.5 ✗ |

## Open physics items (in priority order)

1. **Rotation rate** (~4.5 vs ~3 s/turn). Two proven-but-unstable routes hit 2.8 s/turn / 222 ft/turn:
   deeper stab blanketing and deeper lift valley — both limit-cycle (spin falls out & rebuilds).
   Leading hypothesis to stabilize them: **stall hysteresis** (separation ~15°, reattachment lower)
   damping the relaxation oscillation. Second: split control-surface DRAG from lift-effectiveness so
   adverse yaw survives the post-stall fade (currently one Δα mechanism scaled together).
2. **Sideslip magnitude** in developed spin (−30..−50°, should be ~10–20°): fuselage side crossflow
   arm may need tuning; fin stall shape.
3. Stall-speed check vs 34 mph placard once cockpit IAS (pitot-style, α-corrected) exists.
