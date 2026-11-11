# J-3 straight skis — Part A visual proposal

2026-11-06, lane/codex-j3-skis. **PENDING TY VISUAL APPROVAL.** NOINSTALL, NOUPLOAD; no merge or push.
Part B contact physics belongs to Claude. This lane does not register a playable aircraft, alter configs,
change friction/contact, or touch the held J-3 cork/spray and Decathlon panel work.

## Construction and references

`build_j3_cub.py` now accepts `J3_VARIANT=skis`, dispatching to `build_j3_skis.py`.
The ski builder reads ONLY our existing `j3-cub-like-own.glb`, keeping the current cockpit, paint, airframe,
control origins and rig extras rather than regenerating older cockpit content. It removes Wheel_L/R/Tail,
TailWheel_Steer (fork) and TailWheel_Links; the original bungee gear, gear pivots and tail leaf spring remain.
Every new ski vertex is generated analytically by our script. No third-party mesh, UV or texture is imported.
The original J-3 provenance is `j3-cub-own-PROVENANCE.md`.

References viewed for shape/installation only (nothing from them is shipped):

- Federal A-1500 photographs: https://www.ebay.com/p/2175371199
  Direct photo: https://i.ebayimg.com/images/g/LDgAAOSwZUllqWBb/s-l1600.jpg
  Shows the rounded narrow metal ski, formed longitudinal center section, rivet flanges and axle pedestal.
- Real J-3 ski installation: https://www.scharch.org/Ed_Scharch/usn-aircraft/01-piper-j3.htm
  Direct photo: https://www.scharch.org/Ed_Scharch/usn-aircraft/PiperJ-3-Cub/piper-j3-cub-ski-2.jpg
  Shows straight skis beneath the existing Cub V gear, forward elastic restraint and rear check cable.
  This photographed aircraft retains a tailwheel; Ty's explicitly requested tailski takes precedence.
- Federal installation drawing listing: https://www.skiplane.org/Ski%20Drawings/Federal%20A1500.pdf
  No exact dimension is claimed to have been read from the indistinct scan.

Local reference photos live in ignored `assets-incoming/ref-j3-skis/photos/`; they are not delivered assets.
These are viewing/measurement references, not assertions of a redistribution licence.

Dimensions are copied from the **proposed, unapproved** `lane/claude-ski-physics` J-3 skiGear config
(97bd8d5d lineage), so Part A and Part B can meet at the same anchors. They are not independently certified
Federal factory measurements. Main: 1.75 m × .222 m, axle .95 m from tip. Tail: .60 m × .10 m, axle .30 m from tip.
Pedestal sheet gauge, formed spine, rivet spacing, cable gauge and tail fitting are visual judgements.
This is a Federal A-1500A-class visual proposal, not a claim to reproduce a particular installation drawing.

### 2026-11-10: Ty's four corrections, and what each one is now derived from

> "The straight ski (not the wheel ski) has a pedestal the radius of a normal tire for that airplane so the aoa on
> the ground is the same. The pedestal is a tapered 'tower'. The top of the ski has a keel for strength and the
> front has a handle for lifting/dragging the skis"

and, on both kinds of ski, "The front of the skis should curve up not a sharp angle", with reference photographs
(an Aero Ski pair, a Federal A-1500 pair, and a page of Federals from several angles).

1. **Pedestal height — DERIVED, not authored.** It was the literals .2235 m (main) and .076 m (tail). It is now
   measured per leg off this very model, before the wheels are deleted: `axle.z − (that leg's wheel's lowest
   vertex).z`. On the mains the ski's pivot IS the axle, so it comes out at the tyre's radius, .2235 m, which is
   Ty's words exactly, with nothing typed in. On the tail the pivot is the leaf spring's swivel head, 5 mm above
   and .125 m ahead of the old tailwheel centre, so it comes out **.071 m, not .076 m** — the old literal stood the
   tail 5 mm low, 0.06° of extra nose-up on a 4.6 m wheelbase. `check_j3_skis.py` now asserts the mains are the
   tyre radius to 1 µm and that the three-point pitch attitude on skis equals the one on wheels to 0.01°
   (it reports 13.4798° against 13.4798°, mains and tail contact heights identical).
2. **Pedestal: a tapered tower.** Was two flat sheet cheeks on edge either side of the axle — one thin fin from the
   side, nothing from ahead. Now a four-sided tower with a flared, riveted base (main: .37 m fore-and-aft × .13 m
   across at the ski, drawing in to .13 × .075 at the bush). Proportions are a visual judgement, as the sheet gauge
   and rivet spacing always were.
3. **Keel.** Was a 55 mm crown spread over four fifths of the width and read as a flat top. Now a narrow, tall
   formed member down the centreline: 90 mm proud on the main ski and 35 mm on the tail, over three fifths of the
   width, running 0.03 to 0.97 of the length. In every photograph this is a major feature of a ski seen from above.
   The heights are ESTIMATES (5.1 % and 5.8 % of each ski's length — the depth a formed-sheet beam of that span
   wants); no Federal drawing has been found for any of it.
4. **Handle.** There was none. Now a bent-tube bail standing on the very nose, where every photograph puts a
   bracket or an eye, and where the nose cable picks the ski up from — the same end a man lifts to break a frozen
   ski out and the end he drags it by. The nose cable now anchors on the bail's own crossbar.
5. **The bottom profile.** The old tip was a parabola over the forward .30 m rising .10 m — tangent, so this ski
   never had the crease Ty objected to on the wheel ski, but far too short a curve, and the heel was square. The
   profile is now the one `SkiGear.BottomRiseAt` uses, so both ski stacks describe one ski, and it was MEASURED:
   the bottom edge of the side-on Aero Ski photograph was traced by threshold over its whole 561-pixel length.
   Nose rises **0.127 of the length** over the forward 0.45 of it; **the tail rises 0.032** over the aft 0.12;
   flat in between, which is where the pedestal is. The full trace, the power-law fit (exponent 2.5) and the
   caveat that a photograph is not an orthographic view are in the long note in `src/FlyingGame.Core/SkiGear.cs`.
   **This supersedes the "upturn .30 × .10 m" above**, which was an ESTIMATE with no Federal drawing found and is
   about half the rise and a third of the reach of what the photograph shows.
   The plan now tapers too: widest between 0.34 and 0.70 of the length, narrowing to a rounded nose and a
   narrower rounded heel, as all three photographs show.

## Geometry / attachment contract

For each suffix **L, R, Tail**, names match `BuildOwnSki` in the ski-physics lane's AirframeBuilder
(inside `AirframeVisual.cs`):

- `SkiPivot_<leg>` at the axle; main pivots ride under existing `Gear_Pivot_L/R`.
- Direct children `Ski_<leg>`, `SkiTip_<leg>`, `SkiPedestal_<leg>`, `SkiBungee_<leg>`,
  `SkiCheckCable_<leg>`, `SkiSafetyCable_<leg>`.
- Additional `SkiSpine`, `SkiAxleBush`, `SkiRunner`, consolidated `SkiRivets` and ferrule meshes.
- `SkiAnchor_Toe/Heel/Contact_<leg>` follow the ski.
- `SkiAnchor_AirframeFront/Rear_<leg>` are fixed airframe attachment positions.
- Exact individual cable endpoints are `SkiAnchor_<Bungee|CheckCable|SafetyCable><Ski|Airframe>_<leg>`.
  Each cable mesh stores `ski_anchor` and `airframe_anchor` node names in glTF extras.

Imported Blender frame: nose −X, pilot's left −Y, up +Z. Positive tip-up pitch is Blender +Y,
exported local glTF axis `[0,0,-1]` (recorded as `pitch_axis_gltf`). No `rig_param` is added for ski physics.
Cable meshes depict the rest pose and are parented under the pivot to match the placeholder contract.
**Part B must re-aim/stretch them between the named endpoints when pitching a ski**, keeping the airframe
end fixed; rigidly rotating the whole cable is not an articulated installation. No animation is implemented here.
The tailski axle is at the old swivel/head position on the retained leaf spring, not the old trailing tyre center.

No AirframeModels registration is changed before Ty's visual gate; the GLB is available as a shared Resources
asset for subsequent integration on both platforms. Runtime alignment, eye coincidence and importer binding must
be checked when Part B registers it. Do not use model-bottom grounding to shift the shared cabin.

## Reproduction / verification

```
J3_VARIANT=skis /Applications/Blender.app/Contents/MacOS/Blender -b -t 2 --python tools/own_models/build_j3_cub.py
/Applications/Blender.app/Contents/MacOS/Blender -b -t 2 --python tools/own_models/check_j3_skis.py
/Applications/Blender.app/Contents/MacOS/Blender -b -t 2 --python tools/own_models/render_j3_skis.py
```

Use `nice -n 15` on this shared machine. Renderer uses CPU Cycles, two threads, 20 samples.
Checker verifies all 60 retained non-target meshes' world-space position sets (10 μm quantisation; glTF
re-export can split/weld vertices), absent wheel hardware, literal dimensions, axle origins, six named parts,
anchors and exported single-parent/reachable hierarchy. This is an asset check, not a contact physics test.
Rivets are consolidated per ski rather than dozens of individual draw calls.

Five rendered PNGs, inspected by the author, are outside git:
`~/Developer/agent-lanes/renders/j3-skis/{three-quarter,side,front,pedestal,tailski}.png`.
Build/check/render logs are copied alongside them. All original airframe Resources files are untouched.
`bash tools/check-parity.sh`: **PARITY: OK**, primary lane GLBs 31/31 identical; this review lane is unmerged.
No .NET suite was run: there is no physics/runtime source change, and another lane has an active suite.
Unity/Xcode gate **NOT RUN / PENDING**: serial build slot occupied by canopy-shatter tests during this model pass.
No `BUILD SUCCEEDED` or Unity importer/runtime validation is claimed. That build gate is required before merging,
alongside Ty's visual approval. No install, deploy.sh, upload, push or merge was performed.
