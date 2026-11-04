# C172 front seats v1: forward to the pilot, back on the seatback

NEEDS YOU: Ty, visual approval. Local commit on `lane/claude-172-instruments`; not merged, not deployed.

Ty, 2026-11-04 5:17: "the 172 rudder ergonomics look pretty good except the pilot should sit at the back of the seat
with his back against the backrest/backboard of the seat... move the seat forward so that is the case."

## What changed

The approved eye, the pedals, the yoke and the panel did not move. The mannequin is solved backwards from the eye
exactly as in pedals-v1, so its body is in the same place. The seat moved to meet it (`tools/own_models/c172_cockpit.py`,
`SEAT_*` / `BACK_*`):

| | before (b2e84ee0) | after |
|---|---|---|
| seat pan (cushion, base, belts, buckle, adjuster), fore-aft | centre body x -0.480 | **-0.241: 239 mm forward (+X, body forward)** |
| cushion top (insert) | z -0.195 | -0.240: **45 mm lower** (base 230 -> 185 mm tall, still on the tracks) |
| seatback | upright, insert face at x -0.659 | **reclined 12 deg** (the mannequin's own 172 torso recline); face 219 mm further forward at hip height, centre 188 mm forward and 51 mm down |
| seat width / centre | 0.430 m at y 0.278 | 0.395 m at y 0.2955 (inboard edge 0.063 -> 0.098; centre 17.5 mm outboard) |
| seat tracks | 0.78 m, x -0.86 ... -0.08 | 0.94 m, x -0.86 ... +0.08 (same rear end, run forward under the seat) |
| door armrests | x -0.67 ... -0.21 | x -0.44 ... -0.21 (end ahead of the reclined back) |
| `Seat_adjust_L/R` pivot | (-0.690, ±0.278, -0.310) | (-0.451, ±0.2955, -0.355), moved with the seat |

Both front seats are identical, and the CFI eye sits at the same fore-aft position, so the right seat got the same
change, mirrored. If you want only the pilot's seat changed, say so: it is one line.

Why the seat also moved down, reclined and narrowed, when Ty only said forward:
- **Down 45 mm.** With the eye fixed, the buttock contact is fixed at z -0.252. Under the old cushion top (-0.195)
  that is 57 mm of sink. Moving the seat forward cannot change that. Only a lower cushion can.
- **Reclined 12 deg.** The torso leans back 12 deg (Codex's 172 posture, `torso_up_fru`). Against an upright back,
  only one line of the torso can touch it: the shoulder blades touch while the lower back floats about 0.08 m off. A
  back at the torso's own angle supports the whole back.
- **Narrower inboard, and shorter armrests.** 239 mm forward, the old seat's inboard 23 mm would run through the
  elevator trim wheel, and the reclined back would run through the door armrest. The old seat already ran 22 mm
  into the fuel-selector base, and its back insert ran into the armrest's aft end. The new checker found both
  (below).

## Sources

- Mannequin and eye: as `docs/models/c172-pedals-v1.md`. Codex's 1.778 m rigid-segment mannequin (lane/codex-eyepoints
  f05f1511), 12 deg torso recline, approved eye `CockpitEyeTests.ApprovedEyes` c172-like/pilot. The torso axis
  through the H-point (-0.2993, -0.1561) is copied into `c172_cockpit.py` as a literal (`TORSO_HP`).
- The C172N POH front-seat arm is 37 in (34-46 adjustable). pedals-v1 put this mannequin's hip at FS 49.8. The old
  seat sat about 0.24 m further aft still, so this move goes toward the POH range, not away from it.

## The test: `tools/own_models/check_c172_pedal_fit.py`, extended

Same command as pedals-v1. New measurements, all gated:
- **Back:** rays run from the torso axis straight back, perpendicular to the torso, at 21 heights from waist to
  shoulder and at three lateral offsets (centre ±80 mm). Gap = distance - torso radius 0.140. The back must *rest*: it must be
  within -15 ... +10 mm over at least 200 mm of continuous torso length from the waist up.
- **Pelvis:** the same ray from the H-point, minus pelvis half-depth 0.150, within -15 ... +10 mm.
- **Cushion:** the buttock contact plane (H-point - 0.0956) against the cushion top, sampled on a 140 x 120 mm patch
  under the pelvis. It must touch (>= 0) and press no more than 15 mm.
- **Interference:** every front-seat part (by mesh island, since the static interior is one joined mesh) against
  the trim wheel, the fuel selector, the pedestal and the fuel-selector base, and the door armrest, both seats.

| measurement | before (b2e84ee0) | after |
|---|---|---|
| torso to seatback | 130 ... 192 mm gap: FAIL | **-2.9 mm** (resting) from the waist over 231 mm of the 308 mm torso: PASS |
| upper torso above the insert | — | 19 ... 30 mm off the back box's rounded top edge (warning, not gated) |
| pelvis to seat behind it | +36.5 mm (to the cushion top; no back there): FAIL | **-13.5 mm** into the back foam: PASS |
| buttocks into cushion | 30 ... 57 mm: FAIL | **11.7 mm** across the whole patch: PASS |
| seat vs trim wheel / fuel selector | 0 / 0 | 0 / 0 |
| seat vs pedestal + fuel-selector base | 37 (L), 38 (R) intersecting triangle pairs: FAIL | 0: PASS |
| seat vs door armrest | 109 (L), 109 (R): FAIL | 0: PASS |

Leg and yoke results. The eye and the legs did not move, so these match pedals-v1 to the 0.1 mm:

| check | after |
|---|---|
| feet reach pedals, neutral and full rudder | yes, thighs on the cushion (2 deg lift at full aft pedal) |
| knee included angle | neutral 132.3 (L) / 132.0 (R) deg; 129.7 ... 132.3 at full rudder: PASS |
| legs to panel | 114.5 mm: PASS |
| legs to other pedals | 32.5 mm: PASS |
| legs to floor | heels resting, solid heel <= 2.9 mm into carpet pile: PASS |
| sole to own pedal | -2.3 ... +0.2 mm: PASS |
| lowest pedal part above carpet | 36.4 mm: PASS |
| right thigh to nearest other structure | 26 mm (was 78): it is now the lap-belt buckle, which moved forward with the seat |
| torso to yoke, column full aft | 76.6 mm |
| **yoke to right thigh, full travel** | **-17.1 mm (wheel 90 deg, column aft) / -28.7 mm at full left rudder: still FAIL.** Not changed; your call. The wheel clears the legs only up to 35 deg |
| **left thigh to door trim** | **23.0 mm into the door pleats** (warning). Unchanged: the approved eye is 59 mm outboard of the seat centre (now 41.5 mm, since the seat moved 17.5 mm outboard) |

## Build and tests

`deploy.sh` ** BUILD SUCCEEDED ** (0 error CS, build 2026.1104.0941, dummy UDID, so nothing was installed; the
bundleVersion bump was not committed). `check_c172_cockpit.py` PASS on the model and interior GLBs.
C172InstrumentTests and CockpitEyeTests: 131/131 pass.

## Renders

`renders/c172/seat-v1/`, made by the checker in Blender Cycles. Before is b2e84ee0's GLB, after is this build:
- `before_side.png`, `after_side.png`: left-side orthographic cutaway. The pilot is in the seat with the feet on
  the pedals, and the outer skin and left wall are removed.
- `before_eye.png`, `after_eye.png`: from the approved eye.
- `before-fit.json`, `after-fit.json`: every number above.

I opened all four. After, the back lies along the reclined seatback, the pelvis sits at the back of the cushion,
and the cushion runs under the thighs to just behind the knees (before, it stopped short of mid-thigh). The trim
wheel stands clear beside the cushion's inboard edge, and the panel view is unchanged. The pelvis in the side
render looks slightly sunk into the cushion. That is the drawn pelvis capsule (radius 0.113). The measured
contact uses the mannequin's 0.0956 m H-point-to-contact figure.
