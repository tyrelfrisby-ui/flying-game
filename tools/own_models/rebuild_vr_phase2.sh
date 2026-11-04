#!/bin/zsh
# Operator runs OUTSIDE sandbox; no renders or deploy are started by this script.
set -euo pipefail
export PYTHONDONTWRITEBYTECODE=1
cd "${0:A:h:h:h}"
BLENDER_BIN="${BLENDER_BIN:-/Applications/Blender.app/Contents/MacOS/Blender}"
for liv in N31DG N1191 CHECKER-UNMARKED FACTORY-UNMARKED; do
  PITTS_LIVERY="$liv" "$BLENDER_BIN" -b --factory-startup --python tools/own_models/build_pitts_s2b.py
done
for liv in tf n300uy unmarked; do
  EA_LIVERY="$liv" "$BLENDER_BIN" -b --factory-startup --python tools/own_models/build_ea300.py
done
"$BLENDER_BIN" -b --factory-startup --python tools/own_models/pilot_gear.py
# Blender's bundled standalone Python can validate without starting Blender/Metal.
NUMERIC_PYTHON="${NUMERIC_PYTHON:-/Applications/Blender.app/Contents/Resources/5.2/python/bin/python3.13}"
"$NUMERIC_PYTHON" tools/own_models/check_vr_phase2.py --write-report
