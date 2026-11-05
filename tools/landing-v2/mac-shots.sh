#!/bin/zsh
# Landing v2 bench renders + thumbnail pass in a Mac player (docs/LANDING-V2.md). NOT an install: the app is built to
# /private/tmp, re-identified as com.tyrelfrisby.aeroplayground.landingshots (its own PlayerPrefs, so the run cannot
# touch the real Mac app's settings) and run from there.
#   tools/landing-v2/mac-shots.sh [pages|thumbs|both]   (default both; SKIP_BUILD=1 reuses the last build)
# Shots: $SHOTS (default ~/Developer/agent-lanes/renders/landing-v2/claude). Thumbnails: the app's
# persistentDataPath/landing-thumbs (copy into Resources with tools/landing-v2/import-thumbs.sh).
set -e
REPO=${0:A:h:h:h}; cd "$REPO"
UNITY=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity
OUT=/private/tmp/aero-mac-landing-v2
SHOTS=${SHOTS:-$HOME/Developer/agent-lanes/renders/landing-v2/claude}
MODE=${1:-both}
mkdir -p build/landing "$SHOTS"
if [[ -z "$SKIP_BUILD" ]]; then
  # One Unity/xcodebuild at a time, machine-wide: two idle polls in a row (CLAUDE.md, "Other agents' prompts are in ps").
  idle=0
  # (a deploy that is only installing to devices holds no build slot, so deploy.sh itself is not matched)
  while (( idle < 2 )); do
    if pgrep -f "^/Applications/Unity/Hub/Editor/[^ ]*/Unity.app/Contents/MacOS/Unity" >/dev/null || pgrep -x xcodebuild >/dev/null; then idle=0; else idle=$((idle+1)); fi
    sleep 20
  done
  rm -rf "$OUT"
  FLYINGGAME_MAC_OUT=$OUT nice -n 10 "$UNITY" -batchmode -quit -projectPath "$REPO/unity" -buildTarget OSXUniversal \
    -executeMethod FlyingGame.EditorTools.BuildScript.BuildMac -logFile "$REPO/build/landing/mac.log" || true
  git checkout -- unity/ProjectSettings/ProjectSettings.asset 2>/dev/null || true   # the build's version stamp is not ours to commit
  grep -q "macOS build SUCCEEDED" build/landing/mac.log || { echo "MAC BUILD FAILED: build/landing/mac.log"; grep "error CS" build/landing/mac.log | sort -u | head; exit 1; }
fi
APP="$OUT/Aero Playground.app"
/usr/libexec/PlistBuddy -c "Set :CFBundleIdentifier com.tyrelfrisby.aeroplayground.landingshots" "$APP/Contents/Info.plist"
xattr -rc "$APP"; codesign --force --deep -s - "$APP" 2>/dev/null
run() {
  echo "== AERO_SELFTEST=$1 $(date +%H:%M:%S)"
  AERO_SELFTEST=$1 AERO_SELFTEST_QUIT=1 AERO_SHOT_DIR="$SHOTS" nice -n 5 "$APP/Contents/MacOS/Aero Playground" -logFile "$REPO/build/landing/player-$1.log" &
  local pid=$!
  # The run quits itself; this only stops a hung one.
  local t=0; while kill -0 $pid 2>/dev/null; do sleep 5; t=$((t+5)); (( t > 1500 )) && { kill $pid; echo "== $1 timed out"; }; done
  grep -E "\[LandingSelfTest\] DONE|Exception" "$REPO/build/landing/player-$1.log" | head -5
}
[[ $MODE == pages || $MODE == both ]] && run landing
[[ $MODE == thumbs || $MODE == both ]] && run landing-thumbs
echo "== shots in $SHOTS"
