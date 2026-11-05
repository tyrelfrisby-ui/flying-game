# C172 yoke v1: raised clear of the thighs; 1 in suction gauge

NEEDS YOU: Ty, visual approval. Local commit on `lane/claude-c172-yoke` (from lane/claude-island-nav 94b70e6b);
not merged, not installed.

Ty, 2026-11-04 14:58: "Move the yoke up. I was concerned about that when the instruments got put in the yoke moved
down. It should go up where it does not touch the thighs with full deflection. If you need to move some instruments
around, that's fine. You can put the suction gage as a tiny instrument with a green range and two red lines, one too
low and one too high, for the pressure. Usually the suction gage is a tiny 1 inch instrument."

## What changed (`tools/own_models/c172_cockpit.py`)

| | before (94b70e6b) | after |
|---|---|---|
| both yokes (column, hub, horns, badge) | column centre z 0.040 | **z 0.100: 60 mm higher** (`YOKE_RAISE`) |
| column tube | fwd -0.01 ... 0.26: 37 mm short of the panel face at neutral, 107 mm at full aft (it floated) | fwd -0.01 ... 0.38: into the panel at every column position |
| yoke span, wheel travel, column travel, fore-aft place | 0.27 m horn centres, +/-90 deg, +/-70 mm, hub 0.32 m aft of the panel face | unchanged |
| suction gauge | 2-1/4 in class (face radius 28 mm) under the DG at z 0.113; 0-2-4-6-8 numerals, 36 deg/inHg | **1 in face (radius 12.7 mm)**, still straight under the DG, at z 0.145 |
| suction scale | numerals, no arc | 0-10 inHg over 270 deg (27 deg/inHg, clockwise from 7:30), ticks every inHg, long ticks at 0/5/10, **green arc 4.5-5.4**, **red radial at 4.5 and at 5.4** (Ty 2026-11-04 16:42: 172N 5.4), no numerals |
| runtime rig `C172_Suction_needle` | `[[0,35],[10,395]]`, model -0.972 | `[[0,0],[10,270]]`, model 0 |

Nothing else moved. A node-by-node diff of the exported GLB against 94b70e6b: same 163 nodes; only
`C172_Yoke_L/R_pitch` (+0.060 up; origin +0.060 forward with the longer tube), `C172_Yoke_L/R_roll` (local -0.060
to keep the hub where it was fore-aft), `C172_Suction_case`, `C172_Suction_needle` (+0.032 up) and the joined
static interior (the suction face and its panel hole) changed. The eye, the seat and the pedals are
the approved ones.

Why the suction gauge, and only it: the raised shaft (radius 13 mm) now runs at the height of the old third row,
directly under the DG where the suction gauge was. A 1 in gauge fits in the 60 mm between the DG's mount and the
shaft, so the six-pack, the tach under the VSI and the fuel gauges under the TC all stay where Ty put them in v3.

## How high, and why 60 mm

The fit is set by the seated 1.778 m mannequin at the approved eye (`check_c172_pedal_fit.py`, unchanged human).
Its thigh tops are at z -0.091 (radius 65 mm on a horizontal thigh at H-point z -0.156); at full left rudder the
right thigh lifts 2 deg to follow its pedal. Before, a horn at 90 deg of wheel bottomed at z -0.109. Ty asked for
"does not touch with full deflection"; I set a 15 mm margin as the gate and raised the yoke until the worst corner
of the envelope (full wheel, full forward column, full rudder) cleared it with room: 45 mm was the minimum, 60 mm
gives 30.6 mm.

## Real-172 geometry: what I could and could not source

- **Wheel travel:** about 90 deg each way (180 lock to lock) on Cessna single trainers, per a pilot forum and a
  C172-modelled sim yoke's specification. Not a Cessna document. The rig already uses +/-90 deg.
- **Suction green arc:** 4.5 to 5.5 in. Hg (C172R AFM supplement, STC SA2196CE, "Suction Gage Green Arc (Normal
  Operating)"). The 1980 172N Information Manual gives a desired range of 4.5 to 5.4 in. Hg, and UMA/PMA replacement
  gauges are marked 4.5-5.4. First drawn 4.5-5.5; Ty chose 5.4 (1980 172N manual) on 2026-11-04 16:42, so the green arc
  ends and the upper red line sits at 5.4 (2.7 deg lower). `VacuumSystem.RegulatorInHg` is 5.0, mid-arc.
- **NOT found:** a published column exit height above the panel bottom, the wheel's hub height above the seat
  reference point, the wheel diameter / horn span, or the column's fore-aft travel. The 172 maintenance manual
  chapter 27 rigging pages were not readable in this session. One forum post gives ~14 in of column travel on a
  150/152, which I could not confirm and did not use (the rig keeps +/-70 mm). These are the numbers to check
  against your aeroplane: hub height above the cushion, horn span, and column travel. See NEEDS YOU.

Sources:
- [AFM Supplement STC SA2196CE (C172R)](https://txwg.cap.gov/media/cms/AFM_Suppl__STC_SA2196CE_AirPlains_C_21839006FDB25.pdf)
- [PPRuNe, Cessna 172 suction gauge tolerances (quotes 1980 172N Information Manual)](https://www.pprune.org/tech-log/482876-cessna-172-suction-gauge-tolerances.html)
- [Aircraft Spruce, UMA suction gauge 4.5-5.4 green](https://aircraftspruce.com/catalog/pnpages/10-00311.php)
- [AVSIM, yoke measurement (150/152 wheel ~90 deg each way, column travel)](https://www.avsim.com/forums/topic/525287-yoke-measurement-favour-please/)
- [Aerosoft/SimKits C172-style yoke, 90 deg each way](https://aerosoft.com/en/shop/flight/hardware/yokes/4355/simkits-usb-yoke-system)

## Tests

- **`tools/FlightTests/C172YokeClearanceTests.cs` (new, .NET suite).** Reads both yokes' triangles from the shipped
  `c172-like-own.glb` (sampled at <= 2 mm), sweeps the column -70..+70 mm in 10 mm steps and the wheel -90..+90 deg
  in 2 deg steps, and measures against both seats' thighs: literal capsules copied by hand from the checker's
  mannequin solution, flat and lifted 2 deg. Gate: >= 15 mm.
  - On 94b70e6b's GLB: **FAIL, -28.7 mm** (pilot right thigh, column +70 mm, wheel +90). CFI -27.3 mm.
  - After: **PASS, 30.6 mm** (pilot, same corner); CFI 31.1 mm.
  - A second test holds the suction gauge to a 1 in case under the DG and the 27 deg/inHg rig map.
- **`check_c172_pedal_fit.py` (Blender).** Wheel now sampled every 10 deg (was -90/0/+90) and the yoke gated at
  15 mm (`YOKE_CLEAR`; the other leg clearances keep 10 mm). Before: FAIL -17.1 / -28.7 mm. After: **PASS**; yoke to
  legs 42.5 mm at neutral rudder, 30.6 mm at full left rudder; torso to yoke 81.9 mm (was 76.6). Every other
  number (knees 132.3/132.0, panel 114.5 mm, seat back -2.9 mm, ...) unchanged; the left-thigh-into-door-pleats
  warning (23 mm) is unchanged and still yours.
- **`check_c172_cockpit.py`:** PASS on the model and the interior GLB. Its yoke-shaft keep-out moved to z 0.100,
  and it now asserts the shafts' height and the 1 in suction case.

## Renders

`~/Developer/agent-lanes/renders/c172-yoke/` (all 34; `renders/c172/yoke-v1/` keeps ten of them, `neutral_eye`,
`fore-right_side`, `fore-right_section`, `panel_eye`, `suction_closeup`, before and after, plus both fit reports and
the GLB validation; the interior .blend/.glb the build writes there are not committed), Blender Cycles, made by
`C172_YOKE_RENDERS=1 blender -b --factory-startup --python tools/own_models/check_c172_pedal_fit.py -- MODEL.glb
OUT.json DIR PREFIX`. Prefix `before_` is 94b70e6b's GLB, `after_` this one. Rudder neutral; the mannequin's arms
on the grips. Poses: `neutral`, `aft-left`, `aft-right`, `fore-left`, `fore-right` (column full aft / full forward,
wheel full left / full right). Per pose:
- `_eye`: from the approved pilot eye (head and torso not drawn).
- `_section`: looking aft, orthographic, everything forward of body x 0.075 clipped away so the knees do not hide
  the horn. Mirror-reversed: the pilot's LEFT is on the RIGHT of the picture.
- `_side`: left-side orthographic cutaway, the left leg and arm not drawn, so the right thigh is visible under the
  right horn.
Plus `after_panel_eye.png`, `after_suction_closeup.png` (and the `before_` pair).

## Found, not changed

- The yokes are not in the runtime rig (`docs/models/c172-like-own-rig.json` has no `C172_Yoke_*` row), so in game
  the own 172's yokes do not move with the controls. Their authored bindings (`elevator`, `aileron`) are in
  `c172-panel-bindings.json` only.
- The suction face is procedural (lines and arcs), not painted art. Codex follow-up if a printed face is wanted.
