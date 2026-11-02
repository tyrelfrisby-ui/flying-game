#!/bin/zsh
# Unity Android build -> build/quest/AeroPlayground.apk -> install + launch on a Meta Quest over USB (adb).
# The Quest twin of deploy.sh. Never runs xcodebuild and never touches an iOS device. Clean caches every build
# (stale serialized-object bug, same as deploy.sh). Logs to build/quest.log.
#
# Exit status: 0 = installed and launched; 1 = the build failed; 2 = the APK was built but no authorized Quest is
# attached (the reason is printed — absent, unauthorized, offline). The APK is built FIRST either way, so a missing
# headset never costs a build.
set -e
set -o pipefail
cd "${0:A:h:h:h}"

UNITY=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity
APP_ID=com.tyrelfrisby.aeroplayground.quest
APK="$PWD/build/quest/AeroPlayground.apk"

# adb: the Unity Android module's own first (the version Unity builds against), then Homebrew, then PATH.
ADB=""
for c in /Applications/Unity/Hub/Editor/6000.3.23f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb /opt/homebrew/bin/adb; do
  [ -x "$c" ] && { ADB="$c"; break; }
done
[ -n "$ADB" ] || ADB="$(command -v adb || true)"

# One Unity at a time on this Mac (CLAUDE.md "One Unity build at a time"): wait for any other Unity or xcodebuild.
while pgrep -x Unity >/dev/null || pgrep -x xcodebuild >/dev/null; do
  echo "== waiting for another Unity / xcodebuild to finish $(date +%H:%M:%S)"
  sleep 60
done

rm -rf unity/Library/PlayerDataCache unity/Library/Bee unity/Library/il2cpp_cache unity/Library/BuildPlayerData
# iCloud conflict copies break the build: the same guard as deploy.sh, kept identical (see the long note there on
# why it matches any number and only deletes where a base file exists).
prune_conflicts() {
  find -L "$@" \
       \( -name "* [0-9]" -o -name "* [0-9].*" -o -name "* [0-9][0-9]" -o -name "* [0-9][0-9].*" \) \
       -depth -print0 2>/dev/null |
  while IFS= read -r -d '' dup; do
    base="$(dirname "$dup")/$(basename "$dup" | sed -E 's/ [0-9]+(\.[^.]*)?$/\1/')"
    [ -e "$base" ] && [ "$base" != "$dup" ] && rm -rf "$dup"
  done
  return 0
}
prune_conflicts unity/Assets unity/Library unity/Packages src tools configs
prune_conflicts unity build -maxdepth 2

mkdir -p build/quest
# The build stamps bundleVersion; QuestBuild puts it back itself, and this is the belt to that braces: whatever the
# build leaves in ProjectSettings for the version fields is restored from what was there before it ran.
PS=unity/ProjectSettings/ProjectSettings.asset
VERSION_WAS=$(grep -E '^  (bundleVersion|AndroidBundleVersionCode):' "$PS")
echo "== unity android build $(date)"
BUILD_OK=1
"$UNITY" -batchmode -quit -nographics -projectPath "$PWD/unity" -buildTarget Android \
  -executeMethod FlyingGame.EditorTools.QuestBuild.BuildQuest -logFile "$PWD/build/quest.log" || BUILD_OK=0
echo "== unity done $(date)"
while IFS= read -r line; do
  key=${${line%%:*}// /}
  sed -i '' -E "s|^  ${key}:.*|${line}|" "$PS"
done <<< "$VERSION_WAS"
grep -E 'error CS|\[QuestBuild\]|BuildFailedException|OpenXR Build Failed' build/quest.log | tail -12 || true
if (( ! BUILD_OK )) || [ ! -f "$APK" ]; then
  echo "== BUILD FAILED — see build/quest.log"
  exit 1
fi
echo "== APK $APK ($(du -h "$APK" | cut -f1))"

# ---- the headset ----
if [ -z "$ADB" ]; then
  echo "== no adb found (Unity Android module or Homebrew). The APK is built; install it with: adb install -r $APK"
  exit 2
fi
"$ADB" start-server >/dev/null 2>&1 || true
DEVICES="$("$ADB" devices -l | tail -n +2 | sed '/^\s*$/d')"
echo "== adb devices -l:"
[ -n "$DEVICES" ] && print -r -- "$DEVICES" | sed 's/^/     /' || echo "     (none)"
SERIAL=$(print -r -- "$DEVICES" | awk '$2 == "device" { print $1; exit }')
if [ -z "$SERIAL" ]; then
  if print -r -- "$DEVICES" | grep -q 'unauthorized'; then
    echo "== the Quest is attached but UNAUTHORIZED: put the headset on and accept 'Allow USB debugging' (tick 'Always allow from this computer'). Not working around it."
  elif print -r -- "$DEVICES" | grep -q 'offline'; then
    echo "== the Quest is attached but OFFLINE: unplug and replug the cable, or restart adb (adb kill-server)."
  else
    echo "== no Quest attached: plug it in by USB-C, with Developer Mode on (Meta Horizon app > Devices > Headset settings > Developer Mode)."
  fi
  echo "== the APK is built: $APK"
  exit 2
fi

echo "== install on $SERIAL $(date +%H:%M:%S)"
"$ADB" -s "$SERIAL" install -r "$APK"
"$ADB" -s "$SERIAL" logcat -c || true
echo "== launch"
"$ADB" -s "$SERIAL" shell monkey -p "$APP_ID" -c android.intent.category.LAUNCHER 1 >/dev/null
sleep 12
LOG=build/quest/logcat.txt
"$ADB" -s "$SERIAL" logcat -d -v time > "$LOG" || true
echo "== logcat (Unity, OpenXR, VrApi, the app's own [Quest] lines) — full capture in $LOG"
grep -E "Unity|OpenXR|VrApi|\[Quest|AndroidRuntime|FATAL" "$LOG" | grep -vE "^\s*$" | tail -40 || true
echo "== DONE $(date)"
