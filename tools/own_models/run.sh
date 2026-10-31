#!/bin/bash
# usage: run.sh variant tag views [open]
cd ~/Developer/flying-game-worktrees/codex-own-j3-cub || exit 1
V=$1; T=$2; VIEWS=$3; OPN=$4
J3_VARIANT=$V /opt/homebrew/bin/blender -b --python tools/own_models/build_j3_cub.py 2>&1 | grep -E "Traceback|Error|error:|^  File|^FUS|^GEAR|^PUBLISHED|^BOUNDS|^DOOR|^TRIS|^NO MAT|^WROTE|^FLOAT|^STAB|^GEAR X|^DOOR chevron|^JURY|^FUELCAP|^[A-Za-z]+Error" | grep -v "INFO"
[ "$V" = wheels ] && G=unity/Assets/_Project/Resources/Models/j3-cub-like/j3-cub-like-own.glb || G=unity/Assets/_Project/Resources/Models/j3-cub-floats-like/j3-cub-floats-like-own.glb
mkdir -p ../_xfer/$T && rm -f ../_xfer/$T/*
/opt/homebrew/bin/blender -b --python tools/own_models/j3_fourview.py -- $G ../_xfer/$T/j3$OPN $VIEWS $OPN 2>&1 | grep -E "Traceback|Error" 
cd ../_xfer && tar czf $T.tgz $T && ls $T
