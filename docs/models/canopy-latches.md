# Fleet canopy and door latch audit — Phase 1, 2026-11-02

This is a geometry inventory, not an approved operating procedure. Only Pitts and Extra were
changed. `UNVERIFIED` means no variant-specific primary reference was established in this run;
it is a question for Ty, not permission to copy another aeroplane's mechanism.
Inventory: configs/aircraft/*.json, tools/own_models, tools/model_*.py and published own GLB nodes.

| Type / config | Published own model now | Real mechanism / evidence | Proposed later work / question for Ty |
|---|---|---|---|
| Pitts S-2B | New aft-slide/right-hinge root, paired cross bows, black lateral knob, pin and white channel block | Ty's description; orchestrator's S-2 owner extracts from **Factory S-2 Canopy - Please Explain the Problem**, Biplane Forum. Fully right = locked. Full thread URL not recovered; supplied extracts, not independent verification | Confirm bow spacing, latch bracket/pin length, left-track button and forward fixed windscreen boundary. 101.6 mm aft, 76.2 mm knob and 100° stop are visual estimates |
| Extra 300 mid-wing (all 3 liveries) | Red/white paired squeeze paddles in both cockpits; existing right hinge retained | Ty: red AND white. [Patty Wagstaff Extra 300S egress card, ICAS](https://airshows.aero/ExtractDocs/35/): squeeze paired red handles, canopy hinges right. 300S source supports action, not exact early two-seat 300 geometry; [manufacturer reference index](https://extraaircraft.com/tech-service/) | Confirm handle plane, spacing, pivot axis and front/rear placement on Ty's early mid-wing type; 25° squeeze is estimated |
| Great Lakes 2T-1A-2 | Open tandem cockpits, windscreens, no enclosing canopy/door nodes | Ty flies this type; existing `gl_cockpit.py` and Great Lakes provenance document open cockpits | No canopy latch to model. Is any access-side fitting wanted? |
| J3 Cub, wheels | `DoorUpper_R`, `DoorLower_R`, `Window_R`; door pivots in own rig; no separately named graspable latch | Split right doorway reflected in builder; exact latch shapes and sequence **UNVERIFIED** | Confirm upper window catch, lower-door inside handle and hold-open catch; model each independently |
| J3 Cub, floats | Same split right door model and existing hinge rig | Float installation does not establish cockpit latch detail; **UNVERIFIED** | Same questions; any floatplane-specific catches? |
| SGS 2-33A | `Canopy_Glass`, `Canopy_Frame`, joined `Canopy_Hardware`; builder comments describe right front-canopy hinge and left rear door latch | Builder geometry is not a primary reference; latch action/colours **UNVERIFIED** | Confirm front canopy vs rear entry door, release direction and actual handle locations before separating hardware |
| Swift S-1 | One static `Canopy`; no named latch | **UNVERIFIED** | Need exact opening side, lock handle shape, jettison control, and whether canopy frame is a separate moving assembly |
| EB29R | One static `Canopy`; no named latch | **UNVERIFIED** | Need variant-specific opening/locking and emergency-release reference; do not borrow a Grob mechanism |
| Grob G 103 | No G103 aircraft config or own mesh found | **UNVERIFIED**, not modelled here | Which G103 variant, if being added? Need both canopy locks and emergency-release references |
| C172 (config 172S) | Own mesh has `Glass.Door.L/R`; cabin/door structure combined, no named operating handle | Exact 172S handle/catch arrangement **UNVERIFIED** in this run | Separate both full doors, inside handles and locks from shell using 172S references; do not copy the early straight-tail door |
| Piper Seminole | Own PA44 model, no separately named door/latch node found | **UNVERIFIED** | Confirm main entry door handle, latch points and opening direction, plus emergency-exit treatment |
| SR22 | No `*-own.glb` in this checkout | **UNVERIFIED** (generation unspecified here) | Establish generation and handle/door reference before adding any latch; CAPS handle is a different system and outside Phase 1 |
| P-51D | No `*-own.glb` in this checkout | **UNVERIFIED** | Need canopy crank/lock and emergency-release reference; do not infer from generic bubble-canopy parts |
| F-86 | No `*-own.glb` in this checkout | **UNVERIFIED** (exact series matters) | Establish series and powered canopy controls/manual release; no generic latch proposal |

Remaining configured types have no own GLB in this checkout: PA18 Cub/bush/floats, AirCam/amphib,
Gee Bee R-2, Stearman, PA28 Archer, DC3, H4, PA25 Pawnee, DHC2 wheels/floats, Boeing 737,
Cassutt, JN4 Jenny, Decathlon, Glasair III, target drone, paraglider and hang glider.
Their enclosure/latch implementations are **UNVERIFIED** and no latch was changed. Open-air
types are not assigned fictitious door mechanisms. Before expanding this audit into modelling,
Ty should identify the intended variant and reference cockpit for each enclosed type.

All downloaded-model nodes remain outside this own-model audit. Nothing in this table claims
that absence of a named node means absence of latch-shaped triangles inside a combined mesh.

## Phase 2 correction, 2026-11-02

The Phase 1 tall bows/long vertical rod were a misreading of Ty's description and are
superseded. Two **low** panel-top cross bows, 40 mm apart, now carry a small bridge and
35 mm capture pin. The Delrin channel is open at the pilot-left end; 76.2 mm leftward
travel releases it. Black ball fully pilot-right is locked. All dimensions and latch
notch/linkage details remain estimates requiring Ty's eye review. See
[vr-cockpit-phase2.md](vr-cockpit-phase2.md) for numbers, continuous opening-clearance
proof, sources, unresolved questions and operator render/rebuild commands.
