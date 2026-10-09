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

RESULTS_TABLE

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
