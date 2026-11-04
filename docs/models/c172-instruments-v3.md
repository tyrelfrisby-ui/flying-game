# C172 instruments v3 review

NEEDS YOU: Ty, approve v3 renders in `~/Developer/agent-lanes/xfer/c172v3/`. This is a local commit on `lane/claude-172-instruments`; no push, merge, deployment or device installation.

V3 retains the standard T six-pack. The tachometer is directly below the VSI; suction is below the DG; separate L/R fuel quantity gauges are below the TC. One vertical engine column between the flight instruments and NAV heads contains OIL PRESS, OIL TEMP, AMPS and EGT, in that order. NAV1/NAV2/ADF move right, followed by the intact radio stack. The former right engine cluster is removed, leaving plain panel. There is no fuel pressure, fuel flow, or propeller control.

All text is flat mesh lettering. Control and switch labels use 8.5 mm type (6.20 mm cap height); dial legends use at least 6.3 mm type (4.59 mm caps), with larger numerals. Panel labels sit 0.2 mm ahead of their actual supporting panel; dial legends sit immediately ahead of their faces. Ivory lettering contrasts with black faces/panel; black lettering contrasts with the ivory lower panel. The lower panel has a shallow 4 mm edge bevel so the lettering stays on its flat face. Engine placards use slightly narrower type and a small optical offset beneath each plunger to clear the shafts from the pilot eye. Switch labels sit below the switch bodies, and the primer label is printed on the ivory panel, so both remain visible from the high pilot eye. The flap name and detent numbers sit above the lever, clear of both its protruding body and the CFI yoke shaft in the pilot view. Radio legends lie on the radio fronts/windows. The VSI uses narrow printed type for its lower legend to clear both scale numerals. Label dimensions and final forward stations are included in the exported static mesh's `printed_labels` metadata and checked headlessly.

The three engine plungers are, left to right, black THROTTLE (13 mm radius), red MIXTURE (10 mm), black CARB HEAT (8 mm). Shafts are coaxial with the knobs; none has a stripe, rotational or vernier detail. All three retain authored translation-only bindings. The dedicated engine-controls image pulls mixture to 0 so all three knobs share the same forward station for comparison; its exact inputs are recorded. Throttle and mixture retain runtime `move` rows. A search of `src/FlyingGame.Core/ModelRig.cs` found **no carb-heat, ammeter, or EGT parameter**: carb heat, `C172_Ammeter_needle` and `C172_EGT_needle` remain authored but unbound in the runtime table. The ammeter is drawn at centre zero, with -60 through +60 markings. Both fuel quantity needles retain `fuel_centre_frac`; the five printed E/¼/½/¾/F positions retain the original 72-degree spacing and 288-degree full-scale rig calibration.

The VSI is drawn pointing exactly left at zero. Its additive clockwise map is `[-2000,-170],[-1000,-90],[0,0],[1000,90],[2000,170]`, with `model: 0` because the authored parameter is zero. The face uses the same piecewise angle function: ±500 reaches the upper/lower 5, ±1000 reaches top/bottom, and ±2000 reaches the two 20s near the right edge. Unlike the ASI/tach, this baked zero needs no map offset. The checker measures the exported horizontal left-pointing geometry as well as the map.

The AI uses a flat sky/earth slide behind a circular mask. All visible horizon art lies within about 1.5 mm behind the panel plane, and the fixed level orange wings and centre dot lie close to it. Pitch translates the horizon down 0.8 mm per degree (10° = 8 mm), matching the 5/10/15/20° ladder; roll still rotates the horizon opposite the bank. Bank indices and the moving sky pointer remain. Straight and CFI-seat images show all three requested attitudes without the v2 sphere/tunnel.

The TC mesh and fuselage are symmetric about a gauge-centred pivot. Its authored range is ±40°, and its runtime map is `[-2,-40],[2,40]`. The L/R indices lie on the ±20° pivot radials and extend from 24 to 34 mm, so the wingtip overlaps the inner end at standard rate. The checker decodes the exported vertices, measures wing symmetry to 0.1 mm, and measures each mark's principal axis and pivot radial against the rig. L/R, 2 MIN, D.C. ELEC., curved glass tube, black ball and two reference wires remain.

The existing flush mounts and four slotted corner screws remain on every gauge, along with the top-view DG airplane, distinct ADF head/tail, TRAINER yokes and 1970s materials. Yoke shafts sit below the third row; the rotating grips' full sweep stays well aft of the panel/radios. The checker compares every square mount against every other mount, the full radio stack, yoke shafts/sweep and lower switch row.

Reproduce using Blender 5.2.2 and Unity 6000.3.23f1:

```sh
C172_OUT=renders/c172/instruments-v3 \
  /opt/homebrew/bin/blender -b --factory-startup --python tools/model_c172.py
python3 tools/own_models/c172_instrument_rig.py \
  unity/Assets/_Project/Resources/Models/c172-like/c172-like-own.glb \
  docs/models/c172-like-own-rig.json
C172_OUT=renders/c172/instruments-v3 python3 tools/own_models/check_c172_cockpit.py
RIG_TYPE=c172-like RIG_SHOTS="$PWD/renders/c172/instruments-v3" \
  /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath "$PWD/unity" \
  -executeMethod FlyingGame.EditorTools.OwnRigCheck.Run \
  -logFile "$PWD/renders/c172/instruments-v3/unity-rig-check.txt"
/Users/tyfrisby/.dotnet/dotnet test tools/FlightTests/FlightTests.csproj \
  --filter 'FullyQualifiedName~C172InstrumentTests|FullyQualifiedName~ModelRigTests'
```

All photographic PNGs are Unity/Metal captures of the final imported full aircraft and runtime rig, at 3200×2000 with 4× MSAA. Blender's optional v3 camera set is available with `C172_RENDER=1`, but is not the source of the delivered photographic evidence. OwnRigCheck fails on fallback, missing nodes, incorrect pivots or hierarchy. `capture-inputs.json` records the complete numeric RigInputs and camera for every capture. `render-manifest.json` records each PNG's SHA-256, mtime after this session's start, inputs, and final source/model/rig hashes. The layout diagram is explicitly identified as a measured schematic with no posed rig.

To reproduce the layout diagram, manifest and byte-identical PNG transfer after Unity finishes:

```sh
python3 -m venv renders/c172/instruments-v3/.review-venv
renders/c172/instruments-v3/.review-venv/bin/pip install Pillow
# For a new review session, first write its epoch start time to session-start.txt before capturing.
renders/c172/instruments-v3/.review-venv/bin/python renders/c172/instruments-v3/build_review.py
```

Validation: 45/45 C172 instrument tests pass, including the seven VSI map points, TC standard-rate angles, pitch slide and exported plunger size/order/translation checks. ModelRigTests have 11 passes and the known baseline failure `FullAirBrakeStopsTheGliderSoonerWithNoHandOnTheBrake` (NaN rollout distances). Both GLBs pass the hierarchy, 104 authored bindings, 53 required bindings, flat-label, clearance, pivot, symmetry and calibration checks. Unity reports `RIG OK`, 34/34 runtime nodes bound, no missing/off-pivot/wrong-parent nodes and no compiler errors. Every final PNG was opened and visually reviewed; the copied PNGs are checked byte-for-byte. No `src/` or `configs/` changes; no iOS build was required. Unity-generated ProjectSettings churn was discarded.

Render file list (all in `renders/c172/instruments-v3/` and copied to `~/Developer/agent-lanes/xfer/c172v3/`):

- `c172-ai-bank20-offaxis.png`
- `c172-ai-bank20-straight.png`
- `c172-ai-level-offaxis.png`
- `c172-ai-level-straight.png`
- `c172-ai-pitch10-offaxis.png`
- `c172-ai-pitch10-straight.png`
- `c172-attitude.png`
- `c172-directional-gyro.png`
- `c172-engine-column.png`
- `c172-engine-controls.png`
- `c172-labels-cabin-flaps.png`

- `c172-labels-dials.png`
- `c172-labels-lower.png`

- `c172-labels-switches.png`
- `c172-low-side-flush.png`
- `c172-panel-layout-diagram.png`
- `c172-panel-straight.png`
- `c172-pilot-eye-c172-known.png`
- `c172-pilot-eye-parked.png`
- `c172-sixpack-c172-known.png`
- `c172-sixpack-parked.png`
- `c172-tc-level.png`
- `c172-tc-std-left.png`
- `c172-tc-std-right.png`
- `c172-throttle-mixture.png`
- `c172-turn-coordinator.png`
- `c172-vsi-0.png`
- `c172-vsi-minus500.png`
- `c172-vsi-plus500.png`
- `exterior-cruise.png`
- `exterior-full-controls-canopy-open.png`
- `exterior-parked.png`
- `exterior-pulling-4g.png`
- `full-panel-pilot-eye.png`
