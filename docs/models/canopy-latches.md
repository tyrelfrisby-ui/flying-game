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

## P2c / P2c-2 supersession — Ty photo correction

The earlier Phase 1 table and Phase 2 pin dimensions are historical. Pitts now has
normal-inset skin-section white panels with polished rims, 22 mm offset low white bows
at both panels, 12 mm hidden catch, and a 28 mm black ball travelling 76.2 mm leftward.
Latch blocks/knobs/bows are on Canopy. Intermediate red hoop removed; bubble edge and
sill rails retained. Fixed windscreen unchanged.

Extra has fore/aft red/white handles **on the moving canopy frame**, at both cockpit
stations. Aft fixed; spring-forward Fwd translates aft 35 mm to squeeze, retracting
two 6 mm pins per cockpit upward 18 mm from airframe sockets. Releasing returns them.
Grip midpoint travels half as far. Nodes: Canopy_Latch_Handle_Aft/Fwd,
Grip_Canopy_Latch_Aft/Fwd, Canopy_Latch_Pin_1/2, Canopy_Latch_Hole_1/2; front pair
appends _Front. Old L/R handles and CanopyLock_F/R retained as inert compatibility
empties; separate CanopyRelease_F/R jettison pulls remain on airframe.

All dimensions, derived placement and clearance proxy assumptions are estimates;
see [P2c dimensions and checks](vr-cockpit-phase2.md). canopy_latch 0 = spring released,
pins in; 1 = squeezed, pins out. Opening requires pins retracted; interaction logic
remains Claude's responsibility. No operating procedure or approval is asserted.

## P2d-2 Pitts jettison — confirmed by Ty, 2026-11-02

Ty's 12:40 PST confirmation supersedes the unconfirmed-location fallback: **red
knob on the far right, where the parallel curved bows meet the lower moving
canopy frame/sill rail, at the right hinge end of the bows**. All five supplied
reference photos were reviewed; internal photos 3–5 are consistent with this
junction. Ty's direct confirmation is the placement authority; no POH-derived
measurement is claimed.

Modelled on the **moving canopy frame**, adjacent to the canopy-side hinge
fitting, not on the fixed airframe. At rest it sits at that bow/rail junction.
Both cockpits have low bow pairs in this model, so both receive reachable knobs:
`Canopy_Jettison_Knob_Rear/Front` and child `Grip_Canopy_Jettison_Rear/Front`.
The front control is plausible because it repeats the confirmed bow/hinge junction
on a dual-control tandem canopy; independent front-seat release hardware is an
estimate, not separate confirmation from a maintenance manual.

Rear/front knob centres: X −.875/−.220 m, Y = right rail +25 mm inboard,
Z = rail +18 mm. Red ball 32 mm diameter; short 6 mm cable stem. Pull axis is
builder +Y (pilot-left/inboard), **40 mm estimated travel**, driven by
`canopy_jettison` 0 stowed → 1 pulled; grips follow knobs. Thin 4 mm cable runs
`Canopy_Jettison_Cable_Rear/Front` terminate at the right hinge-pin release
junction on `Canopy_Hinge_Airframe_Rear/Front` when closed. These short release
links leave with the canopy, disengaging from the fixed fittings; no long cable
floats after departure. `Canopy_Jettison_Placard_Rear/Front` use an original
procedurally generated red-on-white **CANOPY JETTISON** texture. Knob pull releases
right hinge pins at any speed; Claude implements release semantics.

CONTRACT additions for operator copy (no root CONTRACT.md created):
- Both types: `Canopy_Detachable` (sole detach target; mass/inertia extras),
  `Canopy_COM`, `Canopy_Hinge_Airframe_Rear/Front`,
  `Canopy_Hinge_Canopy_Rear/Front`, `Canopy_Hinge_Broken_Rear/Front`
  (`default_visible=false`; show on departure).
- Pitts: `Canopy_Sill_Airframe_L/R`, `Canopy_Jettison_Knob_Rear/Front`,
  `Grip_Canopy_Jettison_Rear/Front`, `Canopy_Jettison_Cable_Rear/Front`,
  `Canopy_Jettison_Placard_Rear/Front`; rig `canopy_jettison`, 0..1, 40 mm inboard.
- Existing Canopy/Canopy_Slide and canopy_open maps remain; Extra handles stay
  inside the new detachable root; fixed sill, latch holes and emergency pulls stay
  airframe. Full properties and ownership: [P2d](vr-cockpit-phase2.md#p2d--p2d-2--separable-canopy-2026-11-02).
