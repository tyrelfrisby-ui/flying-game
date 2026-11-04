# C172 rudder pedals v1: above the floor, forward, tested with the seated mannequin

NEEDS YOU: Ty, visual approval. Local commit on `lane/claude-172-instruments`; not merged, not deployed.

Ty, 2026-11-04: "move the rudder pedals above the cabin floor and forward. Put the simulated human inside and test
geometry."

## What changed

Only the four pedals, their toe brakes and two new static rudder bars (`tools/own_models/c172_cockpit.py`,
`PEDAL_*`). A full rebuild was diffed node by node against 935ff945: no node added or lost, and nothing else moved.
The pedal and toe-brake hinges in `docs/models/c172-panel-bindings.json` move with them. `c172-like-own-rig.json`
regenerates byte-identical because the runtime rig binds no pedals.

| | before (935ff945) | after |
|---|---|---|
| pad, fore-aft centre (body x) | 0.295 m, at the panel face | 0.494 m (+0.199 m, 7.8 in forward) |
| pad bottom above carpet | 85 mm (a box standing on the floor) | 152 mm; ball contact 201 mm |
| lowest pedal part above carpet, any rudder | 80 mm | 36 mm (the hinge arm at the rudder bar) |
| hinge | bottom of the box, 0.300 / -0.4625 | rudder bar 0.546 / -0.505 (FS 16.5) |
| pad | vertical | 56 deg face, lower half for the ball of the foot, toe brake flush above |

## Sources

- **172 Maintenance Manual (1996 series) 27-20-00, rudder pedal installation:** "Position rudder bars in area below
  instrument panel"; "Set distance from pivot shaft to firewall at 6.50 inches."
  (manualslib 1637920, p. 430)
- **C172N POH Sec. 6** (via `docs/pending/loading-arm-estimates.md`): datum is the firewall front face, at body
  x = 0.9658; front-seat arm 37 in, adjustable 34-46 in.
- **Cessna Skyhawk Specification & Description, Dec 2015 Rev A, Fig. II:** firewall to aft baggage bulkhead
  142 in; cabin 39.5 in wide trim to trim.
- **Mannequin:** Codex's round-3 1.778 m rigid-segment mannequin (lane/codex-eyepoints f05f1511,
  `eyepoints_v3.py`: Plagenhoef et al. 1983 Table 8; FAA HFDS App. B), with its 12 deg torso recline for the 172.
  Its segment lengths are copied by hand into the checker.

**Where the published figure and the approved eye disagree.** The new hinge is at FS 16.5, not FS 6.5. From the
approved eye, the mannequin's hip is at FS 49.8 and its knee at FS 33.6. A pivot at FS 6.5 would put the pads about
0.25 m further forward, beyond what a 1.778 m leg reaches with any bend left in it. So the pedals sit as far forward
as that leg goes with the heel resting on the carpet. That gives a 132 deg knee, inside the 110-135 deg target. The
target range is an engineering assumption: I could not open the Porter & Gyi (1998) paper to check its values. The
cabin between the panel face (FS 26) and the firewall is short against the real aeroplane. Fixing that is a
seat/eye or firewall-anchor question, not a pedal question.

## The test: `tools/own_models/check_c172_pedal_fit.py`

    blender -b --factory-startup --python tools/own_models/check_c172_pedal_fit.py -- MODEL.glb OUT.json [DIR PREFIX]

The checker seats the mannequin backwards from the approved eye (`CockpitEyeTests` literal) and solves each foot
onto the actual pedal face, using Codex's method: exact shank length, foot pitch 0-85 deg. It extends that method
in three ways: the solid heel and shank may not sink into the carpet, the sole may not press more than 5 mm into its
own pedal, and the thigh may lift up to 20 deg when its pedal comes back. Each pose is tested at neutral and at full
rudder each way (18 deg about each pedal's own hinge, one pedal forward and one back), with the pilot's yoke at all
9 corners of its rig travel (column +/-0.07 m, wheel +/-90 deg). Clearances are unsigned surface distance minus the
limb radius; the floor is measured vertically. `check_c172_cockpit.py` also now asserts, without Blender, that the
hinges have not moved, that every pedal part is 30 mm or more above the carpet, and that every pedal is at least
150 mm forward of the panel face.

| check (pilot seat, worst over neutral and full rudder) | before | after |
|---|---|---|
| feet reach pedals | only with the thighs lifted 12 deg | yes, residual <= 1 mm, thighs on the cushion (2 deg at full aft pedal) |
| knee included angle, neutral | 84-85 deg: FAIL | 132.0 / 132.3 deg: PASS (129.7-132.3 at full travel) |
| legs to panel | 90 mm | 114 mm: PASS |
| legs to floor | heels resting, <= 3.0 mm | heels resting 14-22 mm above carpet; solid heel <= 2.9 mm into carpet pile: PASS |
| foot to own pedal | -5.3 mm: FAIL | -2.3 ... +0.2 mm, resting on the tread: PASS |
| legs to other pedals | 51 mm | 32 mm: PASS |
| pedals above carpet, full travel | 80 mm | 36 mm: PASS |
| legs to yoke, full travel | -57 mm: FAIL | **-17 mm (wheel 90 deg, column aft) / -29 mm at full rudder: FAIL** |
| torso to yoke, column full aft | 77 mm | 77 mm |

**Still red, and not a pedal fault: yoke against the right thigh at full wheel.** With the wheel at 90 deg, the
lower end of the pilot's right-hand horn swings down to z = -0.109. The approved eye puts the mannequin's thigh top
at -0.091. The wheel clears the legs by 10 mm or more only up to 35 deg, at any column position. Nothing about the
yoke was changed: it is approved v4 geometry and the thigh is fixed by the approved eye. Ways to clear it are a
yoke hub about 40 mm higher, a smaller displayed wheel travel, or an eye or seat change. That decision is Ty's.

**Warnings (not gated):**
- The left thigh is 23 mm into the left door trim. The approved eye is 59 mm outboard of the seat centre
  (-0.337 against -0.278), and the hips are centred under it.
- The mannequin's back is about 0.20 m forward of the seat back, and its buttocks sit 57 mm into the cushion
  (the mannequin allows 15 mm). Both follow from the approved eye, not from the pedals.

## Build and tests

`deploy.sh` ** BUILD SUCCEEDED ** (0 error CS, build 2026.1104.0503, dummy UDID, so nothing was installed; the
bundleVersion bump was not committed). `check_c172_cockpit.py` PASS on the model and interior GLBs.
C172InstrumentTests and CockpitEyeTests: 131/131 pass.

## Renders

`renders/c172/pedals-v1/` (also in `/tmp/c172-pedals-renders/`), made by the checker in Blender Cycles from
935ff945's GLB (before) and this build (after):
- `before_eye.png`, `after_eye.png`: from the approved eye. Head and torso are not drawn; arms, legs and feet are.
- `before_side.png`, `after_side.png`: left-side orthographic cutaway, with the outer skin and left wall removed.
- `before-fit.json`, `after-fit.json`: every measurement above.
