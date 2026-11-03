# Pitts Phase 1 own rig

`pitts-s2b-own-rig.json` uses the EA300 rig row schema. `model` is **pitts-s2b-like-own**,
the name passed to `AirframeRig.Bind` by `AirframeModels.PlacedOwnName`. ConfigSync already
mirrors `docs/models/*-rig.json` by the JSON model field to `StreamingAssets/rigs/<model>.json`.
No loader change or hand-written ignored mirror is required. The Unity editor must run its
existing ConfigSync before building; Unity was not run in this lane.

All four Pitts liveries have identical cockpit node names and rest coordinates. Geometry is
authored in +X forward, +Y pilot left, +Z up; exported glTF is (-X,Z,Y). Rig vectors and pivots
are in glTF coordinates. Grip +X points along the builder forward/fingers direction (glTF -X).
Grips are children of the relevant moving control. Foot markers denote ball-of-foot contact.

| Param | Nodes / action | Visual travel (estimated) |
|---|---|---|
| aileron | StickRoll_Front/Rear, linked | ±18° roll |
| elevator | Stick_Front/Rear, nested under roll | -12° forward / +18° aft |
| throttle | Throttle_Front/Rear, linked, left side | 50° forward from idle |
| rudder | Pedal_L/R_Front/Rear, linked | opposite ±15° |
| canopy_latch | Canopy_Latch_Knob and Canopy_Latch_Pin | 0 right/locked; 1 left/released, 76.2 mm |
| canopy_open | Canopy_Slide parent, Canopy child | 0–.2: 101.6 mm aft; .2–1: 100° to right |

Only C# change: `RigInputs.TryGet("canopy_latch")` returns **1 − CanopyLock**. Existing
canopy_lock retains its prior sense. A focused two-state test covers the alias. No input,
flight physics, controller binding or interaction scripts are changed. The latch does not
enforce physical interlocking in this visual lane; interaction owns that constraint.

Contract nodes are present, including Harness_Buckle_Rear/Front and Headset_Jack_Rear/Front
empties for Phase 2. No harness/headset/parachute/ripcord visuals were added. Grip positions,
pedal contact positions, frame measurements and panel bounds are in the generated check JSON.
No CONTRACT name was renamed. New helper Canopy_Slide is additive; Canopy remains the movable
glass/frame/bow assembly. The current canopy includes the whole existing glass/frame rather
than separating a fixed windscreen; Ty must resolve that boundary from an S-2B reference.

Canopy geometry is a kinematic approximation, not a factory latch drawing. The paired bows
are at X -.76 and -.84 m. Pin/link length from panel to crown, block channel dimensions,
hinge station and stops are UNVERIFIED; see AGENT-LOG questions and canopy-latches.md.
