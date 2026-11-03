# Original pilot body — Phase 1

All meshes and the 49-bone skeleton are generated from numbers in `tools/own_models/pilot_body.py`.
No third-party mesh, rig, texture, scan, human model or training-derived raster asset is imported.
Flight suit torso stub, shoulders, arms, Nomex-coloured open gloves, five three-bone fingers per
hand, thighs, shins, boots and toes; no head.

**v2 (2026-11-02, Claude, lane/claude-vr-hands-fix):** the `--offline` build now emits ONE skinned mesh
`Pilot_Body` from `tools/own_models/pilot_body_smooth.py` instead of 49 rigid flat-shaded boxes/tubes:
lofted superellipse torso (Hips/Spine/Chest blended), continuous arm and leg lofts that bend at the
elbow/knee with smoothstep two-bone weights, harness straps, lap belt and buckle, gauntlet gloves with a
rounded palm and capsule phalanges, lofted boots. Indexed vertices with welded smooth normals, MikkTSpace-
style tangents from the UVs, and five materials (flight suit, Nomex gloves, boots, harness, buckle) with
256² normal maps (twill, leather, grain, webbing) generated procedurally from code in the same file.
**8,080 triangles**, 1 skinned renderer, 5 submeshes, 4 small textures — below the 15,000 Quest target.
Same 49-bone skeleton, names, rest pose and inverse bind matrices as v1 (v1 was 2,416 triangles).
Still original: no third-party mesh, texture, scan or training-derived asset.

**v3 (2026-11-03, approved by Ty and shipped; shown only with VR menu → Fly by hand):** `pilot_body.py --offline`
now builds v3 into Resources (`--offline --v2` rebuilds v2 into `PilotBodyCandidates/`, outside the build, for
comparisons). Feet flat on the pedal face (sole lean 13°, `L/R_SoleOnPedal` marker nodes drive the foot pose in
`VrPilotBody.HeelFromBall`), boots with heel / rubber sole / toe box / laces / shaft and collar, deltoid caps and a
trapezius yoke, knuckled gloves with cuff strap and snap, elbow / knee / waist folds, zip and chest pockets, softer
normal maps, a lighter suit. **14,256 triangles**, 49 joints (same skeleton and names), 6 materials (new
`Pilot_BootSole`) — still below the 15,000 Quest target. Byte-identical to the candidate Ty reviewed (f32ebca5).
Same generator, same rule: no third-party mesh, texture, scan or training-derived asset.

Anthropometric anchor: National Research Council, *Video Displays, Work, and Vision* (1983),
[Anthropometry and Biomechanics in VDT Applications](https://www.ncbi.nlm.nih.gov/books/NBK216490/?report=reader),
Civilian males 20–60, 50th percentile: stature **1.736 m**, seated shoulder **0.594 m**,
buttock–knee **0.594 m**. These anchor a representative median-sized male, not a claim that
all body dimensions simultaneously have the same percentile. Clothing and joint-centre
dimensions are authored estimates: upper arm .310, forearm .260, palm .100, finger lengths
.062/.078/.086/.080/.065, upper leg .420, lower leg .430 m. Shoulder centres .360 m apart;
boot ball–toe .075 m and ankle–ball .111 m. Suit radii: upper arm .049, forearm .037,
thigh .060, shin .043 m. A clinical or scanned anthropometric body is not claimed.

Bone names follow CONTRACT.md exactly: Hips, Spine, Chest; L/R_Shoulder, UpperArm,
LowerArm, Hand; L/R_Thumb1–3, Index1–3, Middle1–3, Ring1–3, Little1–3;
L/R_UpperLeg, LowerLeg, Foot, Toes. Rest fingers are extended with a slight relaxed splay.
Segments are independently weighted to their bones; no IK code or hand-tracking implementation
is included. Claude owns posing/curl. Rigid segment weights are lightweight; joint deformation
quality under extreme curl still needs Blender/Unity visual review.

The default rest pose is seated at the Pitts rear controls, not a standing T pose. Hip centre
(-1.30,0,-.335) in builder coordinates. Two-link IK construction preserves arm/leg lengths.
Right hand aims at Grip_Stick_Rear, left at Grip_Throttle_Rear, balls of feet at Foot_L/R_Rear.
The same builder has an `extra` pose derived from the Extra's actual world-space controls for
render evidence. It does not change the shared asset's rig to aircraft-specific bone names.

Build with Blender:
`blender -b --factory-startup --python tools/own_models/pilot_body.py`.
In this sandbox Blender 5.2.2 segfaulted in Metal device detection before Python ran. The
delivered GLB was generated with `pilot_body.py --offline` using the SAME numeric definition,
explicit glTF skin, weights and inverse bind matrices. It is **not a successfully Blender-generated
asset** yet. Blender export equivalence and appearance remain unverified; do not describe that gate as passed.

`check_vr_phase1.py --write-report` validates bone/skin weights and prints conservative limb
capsule clearances to the actual fixed Pitts mesh triangles, including front-seat region,
rear panel and new truss. Samples are <=3 mm apart with another 1.5 mm subtracted. These
checks enclose the limb cylinders. The torso surface additionally checks vertex distances and
edge intersections. These do not certify body self-collision, full motion ranges or the Extra pose. Intended hand/control and sole/pedal contacts are excluded.
Exact current numbers are in `vr-cockpit-phase1-checks.json`; negative numbers are failures.
