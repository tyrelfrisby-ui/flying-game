# Stick and Rudder — coverage (Aero Widget, protocol 8)

The widget exists so every concept Wolfgang Langewiesche teaches in *Stick and Rudder* (1944) can be shown accurately and live, plus spin aerodynamics beyond the book. The concepts here are in our own words; no text is copied from the book.

**How a scene runs:**
- `{"cmd":"lesson","id":"<id>"}` sets up the aircraft, start state, wind, loading, view, vectors and insets, then flies a short scripted demonstration in phases.
- The phases use the autopilot's pitch channel for altitude and α holds, and the lesson's own aileron and rudder.
- Each demonstration writes what it measured onto a caption line (`lessonResults` in the state).
- Review steps through any of it, and pilot input ends the script.
- The NTSB display (protocol 6) and the forces (protocol 7) work in every scene.

**Status key:**
- ✓ — the scene shows it and the numeric test passes.
- partial — the scene exists, but the test is weak or fails, for the reason noted.
- missing — not built.

Test results come from the batch run of every lesson (`lessons-run`, 2026-10-09) and the sim-level `tools/FlightTests/StickAndRudderTests`.

| # | Concept (our words) | What the viewer sees | Scene | Numeric test (measured 2026-10-09) | Status |
|---|---|---|---|---|---|
| 1 | The wing answers to angle of attack, not to speed or attitude | AoA gauge with the critical α; CL–α inset with a live dot | `aoa-attitude` (α 6° climbing / about level / descending) | α held at 6° through all three phases; the CL is not yet logged per phase | partial |
| 2 | The elevator sets the angle of attack, and so the speed | AoA gauge, CL–α, speed tape | `elevator-is-aoa` (α 2° → 5° → 9° at fixed power) | the speed settles lower at each α (69 KIAS at α 9°); per-phase speeds not yet logged | partial |
| 3 | The throttle sets the climb, not the speed | Flight-path angle and VS against the attitude; relative wind | `throttle-is-climb` (α 5°; full / 55 % / idle) | flies cleanly (max 2 g); per-phase flight path not yet logged | partial |
| 4 | The stall is an angle, at any speed | AoA gauge; stall speed rising with √n | `stall-any-speed`, `accelerated-stall` | **1 g: α 15.6° at 50 KIAS. 60° bank: α 15.6° at 74 KIAS, 2.0 g. Speed ratio 1.49 vs √(n ratio) 1.47** | ✓ |
| 5 | You can't stretch a glide | L/D inset; glide ratio per speed | `stretch-the-glide` (idle; speed held by the AP's level-change pitch) | **best 11.7:1 at 80 KIAS > 15 kt slow 10.1:1 > 20 kt fast 6.3:1.** Sim test: best glide 12.66:1 at 82 kt | ✓ |
| 6 | The back side of the power curve | Power-required inset with a dot; AP holding altitude as the speed falls | `back-side` | power 27 % at 81 kt → **17 % at 70** → 20 % at 60 → 20 % at 52 (more power below the minimum-power speed; a mild curve) | ✓ (mild) |
| 7 | The airplane flies in the air mass | Air velocity, ground track and wind arrows | `air-mass` (20 kt wind, 30° turn) | turn flown at constant airspeed; drift not yet logged | partial |
| 8 | How an airplane turns | Lift split into vertical and horizontal parts; load and stall speed | `how-it-turns` (30 / 45 / 60°, level) | **nz 1.14 / 1.39 / 2.09 vs 1/cos φ = 1.15 / 1.41 / 2.00** | ✓ |
| 9 | Adverse yaw; what the rudder is for | Each wing's drag; ball; sideslip | `adverse-yaw` | **aileron only: β −2.7° in the first 1.2 s (nose away from the turn). With rudder: β +0.7° (ball near centre)** | ✓ |
| 10 | Slips and skids | β, ball, relative wind from the side | `forward-slip`, `skid` | scenes fly; β and sink not yet logged | partial |
| 11 | The skidding base-to-final stall-spin | Each wing's α; ball | `base-to-final` | inside (left) wing stalled first, **but only just: L α 16° vs R α 15°** | partial |
| 12a | Longitudinal stability | CG and NP marks, static margin; AoA | `longitudinal-stability` | **trimmed α −0.3° → disturbed 1.6° → hands-off 25 s later −0.4°** (CG 23 %, NP 50 % MAC) | ✓ |
| 12b | Directional stability | Relative wind, sideslip, fin force | `weathervane` | **β −4.4° after the kick → +0.1° 15 s later** | ✓ |
| 12c | Lateral stability (dihedral) | Bank against time | `dihedral` | bank 13° released → 14° after 20 s: the C172 model is about neutral, so no visible roll-back | partial |
| 12d | The spiral tendency | Bank and altitude against time | `spiral` | bank 18° → 20° in 40 s, −188 ft: about neutral | ✓ (as modelled) |
| 13 | Trim sets the speed | AoA; speed | `trim-is-speed` | nose-up trim, hands off: 119 → 115 KIAS, α 1.6° | ✓ (small step) |
| 14a | The aim point | An aim-point marker that holds still on a steady path | `aim-point` | the runway and aim bar are drawn; the moving / still aim-point marker (`aimPoint`) isn't built yet | partial |
| 14b | Roundout and flare | Side view, ground line, vectors | `flare` | the lesson's flare law lands it (25 kt at the touch) | ✓ |
| 14c | Ground effect | Induced drag / downwash change near the ground | `ground-effect` | the model has it (φ, downwash), but there's no readout or inset of it yet | partial |
| 15 | Left-turning tendencies | Torque, P-factor, slipstream and gyro each as a vector or moment | `left-turning` | the scene flies (full power, α 10°); the per-effect breakdown (`propEffects`) isn't drawn yet | partial |
| 16 | Power-off / power-on stalls; slow flight | Tail flow with prop blast vs blanketing (P7); AoA | `power-off-stall`, `power-on-stall`, `slow-flight` | **power-off: α 15.6° at 50 KIAS. Power-on: α 15.6° at 40 KIAS.** Slow flight held at 52 KIAS (α 16.5°). P7 tail η: idle 0.90, full power 1.78 | ✓ |
| 17 | The instruments vs the feel | The AoA gauge beside the airspeed tape | every scene (the NTSB display) | always on in NTSB mode | ✓ |
| 18 | Spin basics | Wing winds, α, the two moments | `spin-basics` | **fails:** the sim doesn't sustain the spin (see below) | partial (model) |
| A | Autorotation | Per-strip lift; rolling moment | `autorotation` | the strips draw, but the motion is a rolling departure, not a steady spin | partial (model) |
| B | Inertia coupling | AERO vs INERTIA pitch-moment arcs about the CG | `inertia-coupling` | averages: AERO −2,272 vs INERTIA +1,908 ft·lb: **16 % apart (target ≤ 10 %)** | partial (model) |
| C | Spin modes and the CG | W&B inset, CG/NP marks, moments | `spin-modes` | **CG forward: pitch −59°, yaw −80°/s. Aft limit: −51°, −60°/s. Behind the limit: −44°, −45°/s** — flatter and slower aft, the right direction | ✓ (direction) / partial (model) |
| D | Recovery factors | Moments; rudder and elevator | `recovery-factors` | rudder alone barely slows it (yaw −41 → −39°/s); **+ stick forward: −19°/s, α 0°** | partial (model) |
| E | Incipient / accelerated / power-in-spin | Rates and α | `incipient`, `accelerated-spin`, `power-in-spin` | power-in-spin: full power reversed the rotation (yaw −61 → +54°/s) — not credible | partial (model) |
| E2 | Ailerons in the spin | Wing-wind α labels; moments; control display | `spin-ailerons` | **neutral yaw −64°/s, α 18°. WITH: −24°/s, α 8°. AGAINST: −12°/s, roll reversed.** The aileron clearly acts, but the base spin isn't a steady one | partial (model) |
| W&B | Weight and balance | W&B envelope inset, CG/NP marks, OUT OF LIMITS | the `loading` command (any scene) | **+20 % weight: Vs ×1.086 (√1.2 = 1.095), same best-glide angle 12.66:1, speed 82 → 90 kt. Forward vs aft CG: elevator −1.7° vs +1.6°, tail 446 N down vs 83 N up.** Every type is stable at its default CG | ✓ |
| AP | Autopilot holding through loading changes | AP inputs on the panel; status | the `autopilot` command | +20 % weight: power 0.58 → 0.70, α 1.1° → 2.1°, altitude within 8 ft. The forward / aft CG limits read correctly. Behind the NP it fails, and at double weight: CAN'T HOLD — FULL POWER | ✓ |

## Advanced spin aerodynamics — the open problem

The spin scenes (18, A–E2) all depend on the sim producing a sustained, developed spin with pro-spin controls held. It currently doesn't, for most types. Found on 2026-10-09 with `tools/FlightTests/SpinProbe` (`SPIN_PROBE=1`):

| Type | Held pro-spin (full aft, full rudder) | Diagnosis |
|---|---|---|
| C172 | Prop at idle: right spins sustain (75°/s); **left spins die** after about 1 turn. Without the prop's gyroscopic term: both directions sustain (±80–94°/s). | The spin is on a knife-edge: a ~160 N·m gyroscopic moment from the idling prop decides it. |
| Pitts S-2B | Even with the prop removed (mirror-symmetric), it follows the rudder for ~10 s, then settles into a steady **rolling departure the other way**: roll 240°/s, yaw 50°/s, **α 13° (below the stall)**, β 28°. | Full aft stick can't keep the wing stalled once it's rolling. The fin weathervanes into the sideslip (+3,900 N·m), beating the held rudder. |
| Extra 300 | Pulsating, on-off rotation. | Same family. |
| Decathlon | A slow spiral, not a spin. | Same family. |

**What it needs:** a spin-model calibration pass, tested against known spin data (attitude, α, rate, turns to recover per type):
- elevator authority and inertial pitch-up at high α;
- the fin's shielding by the stab's wake;
- dihedral effect at large sideslip;
- prop gyroscopics.

The current values came from the owner's earlier flight-test tuning ("spin blanketing weakened: too flat / no authority"), so this pass needs the owner rather than a silent re-tune.

## OWNER'S ADVANCED NOTES

*(Add concepts here; each new one gets a scene and a numeric test the same way.)*
