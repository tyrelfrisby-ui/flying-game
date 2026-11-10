# C172 instruments v4 review

NEEDS YOU: Ty, approve the v4 swap. Local commit on `lane/claude-172-instruments`; not merged, not deployed.

Ty, 2026-11-03: "The Cessna panel is good except you need to swap the vertical column of engine instruments and nav
heads so the engine instruments are to the right of the nav heads."

V4 is v3 with one change: the NAV1 / NAV2 / ADF column and the OIL PRESS / OIL TEMP / AMPS / EGT column trade
places. Left to right the panel now reads six-pack (with tach, suction and fuel below), NAV heads at x = -0.112 m,
engine column at x = 0, then the unchanged radio stack. Rows, sizes, faces, knobs, labels and every other part are
as v3. In `tools/own_models/c172_cockpit.py` the two columns are `NAV_X` and `ENGINE_X`.

The rig changes only by those positions: in `docs/models/c172-like-own-rig.json` and `c172-panel-bindings.json` the
only rows that differ from v3 are the pivots of the moved needles, flags, cards and knobs, each shifted by exactly
0.112 m across the panel. Maps, axes and parameters are untouched.

`check_c172_cockpit.py` now asserts the order (altimeter < NAV heads < engine column < radio stack) and that all
three nav heads share x = -0.112 and all four engine gauges x = 0, alongside its existing mount-overlap and
radio-stack clearance checks. The `c172-engine-column` camera in `OwnRigCheck` follows the column to x = 0.

Renders (the BEFORE pair was taken at 35f617b3 before any edit; same Unity OwnRigCheck capture and the same
measured-diagram code as v3, now `renders/c172/instruments-v4/build_diagram.py`, which reads its callouts from the
GLB and prints the measured column order):

- `renders/c172/instruments-v4/before/full-panel-pilot-eye.png`, `.../before/c172-panel-layout-diagram.png`
- `renders/c172/instruments-v4/after/full-panel-pilot-eye.png`, `.../after/c172-panel-layout-diagram.png`
- `c172-pilot-eye-c172-known.png` in each folder: the same seat eye with the instruments posed off zero.

Reproduce:

```sh
C172_OUT=renders/c172/instruments-v4/build \
  /opt/homebrew/bin/blender -b --factory-startup --python tools/model_c172.py
python3 tools/own_models/c172_instrument_rig.py \
  unity/Assets/_Project/Resources/Models/c172-like/c172-like-own.glb docs/models/c172-like-own-rig.json
C172_OUT=renders/c172/instruments-v4/build python3 tools/own_models/check_c172_cockpit.py
RIG_TYPE=c172-like RIG_SHOTS=/tmp/c172v4-after \
  /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD/unity" \
  -executeMethod FlyingGame.EditorTools.OwnRigCheck.Run -logFile /tmp/c172v4-after/unity-rig-check.log
python3 renders/c172/instruments-v4/build_diagram.py renders/c172/instruments-v4/after 'C172 • INSTRUMENTS V4 (AFTER) • MEASURED PANEL LAYOUT'
```

The build folder (`c172-interior.glb/.blend`) is scratch and not committed.
