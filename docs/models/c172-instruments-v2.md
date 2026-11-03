# C172 panel v2 review

Ty approval is pending. This is the 172 instruments lane; no push or merge.

The v1 instrument PNGs were captured at 10:20 on November 3, before the 14:38–14:41 feedback. Their protruding circular cases, plus-shaped DG and rectangular inclinometer match the original builder. The model present when this work began was the original authored GLB (`d59a3a19f2246e5d3739b6b24cc22d1fba59dca5d8c0e5dbbee8d35b469e55b6`, 161 nodes). There were no uncommitted changes. The recognizable authored panel in those pictures supplies no evidence of fallback art; the requested revisions had not reached that geometry.

V2 cuts actual apertures in the panel, recesses the instrument cases, and adds square flush mounting plates with four slotted corner screws per gauge. The panel and plate fronts share the 0.2975 m design forward station. The AI retains a round recessed horizon sphere and a fixed, continuous, level miniature airplane. The DG has a fixed top-view airplane silhouette. The electric TC has a rear-view airplane, L/R standard-rate indices, 2 MIN and D.C. ELEC. legends, and a curved polished glass tube with a black ball and two reference wires.

The fixed-pitch panel has no propeller control. Black throttle and red mixture knobs are plain cylindrical plungers without stripes or vernier detail. Their runtime bindings translate along the shaft only. The ADF retains an arrow head and opposing tail; both yokes read TRAINER. Closed exterior lofts receive consistent outward normals, and the panel shoulders and glareshield ends are narrowed inside the rounded fuselage section. A black rear wall closes the AI case for oblique views.

Reproduce the authored geometry and review images:

```sh
C172_OUT=renders/c172/instruments-v2 C172_RENDER=1 blender -b --factory-startup --python tools/model_c172.py
python3 tools/own_models/c172_instrument_rig.py unity/Assets/_Project/Resources/Models/c172-like/c172-like-own.glb docs/models/c172-like-own-rig.json
C172_OUT=renders/c172/instruments-v2 python3 tools/own_models/check_c172_cockpit.py
```

`C172_OUT` selects the v2 camera set and keeps v1 evidence intact. The builder can produce Cycles views; **all delivered v2 PNGs are fresh Unity/Metal captures of the final imported full aircraft and runtime rig**, including the full panel at the original pilot eye. Reproduce those captures after generating the model and rig:

```sh
RIG_TYPE=c172-like RIG_SHOTS="$PWD/renders/c172/instruments-v2" \
  /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath "$PWD/unity" \
  -executeMethod FlyingGame.EditorTools.OwnRigCheck.Run -logFile /tmp/c172-v2-unity.log
```

OwnRigCheck requires the own model rig to bind and fails if the loader falls back. C172 uses its configured pilot eye; it does not require an Eye_Rear mesh node. The manifest records final model, cabin, source, and PNG hashes, camera coordinates, and validation results. All 14 PNGs were copied to `~/Developer/agent-lanes/xfer/c172v2/` and verified byte-for-byte.

Validation: 32/32 C172 instrument tests pass, including translation-only throttle/mixture bindings. The broader ModelRigTests have 11 passes and one pre-existing glider failure: `FullAirBrakeStopsTheGliderSoonerWithNoHandOnTheBrake` produces NaN rollout distances both here and in an isolated clean-HEAD run. Both exported GLBs pass hierarchy, 104 authored bindings, 45 required bindings, pivot, clearance and case-flushness checks. Unity exits 0 with `RIG OK`, 34/34 runtime nodes bound, zero off-pivot/wrong-parent nodes and zero compiler errors. The original fuselage's signed volume was -4.840003 m³ (inward); the corrected export is +4.867450 m³ (outward). All 28 existing exterior nodes remain. The full panel, six-pack, AI, DG, TC, controls, low-angle mounts and exterior were visually inspected.

No `src/` or `configs/` changes; no device build or deployment was needed or claimed. The Unity-generated ProjectSettings changes were discarded.
