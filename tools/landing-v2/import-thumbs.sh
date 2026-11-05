#!/bin/zsh
# Copy the bench thumbnail pass (tools/landing-v2/mac-shots.sh thumbs) into the shipped fallback set,
# unity/Assets/_Project/Resources/LandingThumbs/<key>.png (LandingThumbnails, source 2). Both platforms ship the same set.
set -e
REPO=${0:A:h:h:h}
SRC=${1:-$HOME/Library/Application Support/FlyingGame/Aero Playground/landing-thumbs}
DST=$REPO/unity/Assets/_Project/Resources/LandingThumbs
[[ -d "$SRC" ]] || { echo "no captures at $SRC"; exit 1; }
mkdir -p "$DST"
rsync -a --include='*/' --include='*.png' --exclude='*' "$SRC/" "$DST/"
echo "$(find "$DST" -name '*.png' | wc -l | tr -d ' ') thumbnails in $DST ($(du -sh "$DST" | cut -f1))"
