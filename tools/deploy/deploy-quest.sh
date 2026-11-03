#!/bin/zsh
# Unity Android build -> build/quest/AeroPlayground.apk -> install + launch on a Meta Quest over USB (adb).
# The Quest twin of deploy.sh. Never runs xcodebuild and never touches an iOS device. Clean caches every build
# (stale serialized-object bug, same as deploy.sh). Logs to build/quest.log.
#
# Exit status: 0 = installed and launched; 1 = the build failed; 2 = the APK was built but no authorized Quest is
# attached (the reason is printed — absent, unauthorized, offline). The APK is built FIRST either way, so a missing
# headset never costs a build.
#
#   --build-only   build the APK and stop: never runs adb (no install, no launch, no logcat), whatever is attached.
#                  Exit 0 = APK written, 1 = the build failed.
#   --release --version-code N
#                  Meta Horizon Store build: signed with the upload keystore (~/Developer/agent-lanes/keys/
#                  aeroplayground-quest.keystore, password from the login keychain item aeroplayground-quest-keystore,
#                  handed to Unity in its environment only), versionCode N (must be higher than any upload so far),
#                  written to build/quest/release/. Implies --build-only (a release-signed APK will not install over a
#                  debug-signed one), then runs tools/deploy/quest-vrc-check.sh on it. Gradle writes the password into
#                  its build.gradle, so the Android Gradle project is deleted afterwards.
set -e
set -o pipefail
cd "${0:A:h:h:h}"

BUILD_ONLY=0
RELEASE=0
VERSION_CODE=""
while (( $# )); do
  case "$1" in
    --build-only) BUILD_ONLY=1 ;;
    --release) RELEASE=1; BUILD_ONLY=1 ;;
    --version-code) shift; VERSION_CODE="$1" ;;
    *) echo "usage: $0 [--build-only] [--release --version-code N]"; exit 64 ;;
  esac
  shift
done
if (( RELEASE )); then
  [[ "$VERSION_CODE" == <1-> ]] || { echo "== --release needs --version-code N (a positive integer above the last upload)"; exit 64; }
  KEYSTORE="$HOME/Developer/agent-lanes/keys/aeroplayground-quest.keystore"
  [ -f "$KEYSTORE" ] || { echo "== no upload keystore at $KEYSTORE"; exit 64; }
  KS_PASS="$(security find-generic-password -s aeroplayground-quest-keystore -w 2>/dev/null)" ||
    { echo "== keychain item aeroplayground-quest-keystore not found (or access denied)"; exit 64; }
fi

UNITY=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity
APP_ID=com.tyrelfrisby.aeroplayground.quest
APK="$PWD/build/quest/AeroPlayground.apk"
(( RELEASE )) && APK="$PWD/build/quest/release/AeroPlayground.apk"

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
# (and the signing fields, which a release build sets and Unity re-serialises as '{inproject}: ' when cleared).
VERSION_WAS=$(grep -E '^  (bundleVersion|AndroidBundleVersionCode|AndroidKeystoreName|AndroidKeyaliasName|androidUseCustomKeystore):' "$PS")
echo "== unity android build $(date)"
BUILD_OK=1
if (( RELEASE )); then
  rm -f "$APK"
  AERO_QUEST_RELEASE=1 AERO_QUEST_KEYSTORE="$KEYSTORE" AERO_QUEST_KEYSTORE_PASS="$KS_PASS" AERO_QUEST_VERSION_CODE="$VERSION_CODE" \
  FLYINGGAME_QUEST_OUT="${APK:h}" \
  "$UNITY" -batchmode -quit -nographics -projectPath "$PWD/unity" -buildTarget Android \
    -executeMethod FlyingGame.EditorTools.QuestBuild.BuildQuest -logFile "$PWD/build/quest.log" || BUILD_OK=0
  # The generated Gradle project (and Bee's copies of it and of its inputs) carry the keystore password in plain
  # text: never leave any of it on disk, then make sure nothing else under the project or build/ holds it.
  rm -rf unity/Library/Bee
  if grep -qF -- "$KS_PASS" build/quest.log; then echo "== WARNING: the keystore password appears in build/quest.log; deleting the log"; rm -f build/quest.log; fi
  LEAKS="$(grep -rlF -- "$KS_PASS" unity/Library unity/Temp unity/ProjectSettings unity/Assets build 2>/dev/null || true)"
  if [ -n "$LEAKS" ]; then echo "== the keystore password is still in (deleting):"; print -r -- "$LEAKS" | sed 's/^/     /'; print -r -- "$LEAKS" | while IFS= read -r f; do rm -f "$f"; done; fi
  unset KS_PASS
else
  "$UNITY" -batchmode -quit -nographics -projectPath "$PWD/unity" -buildTarget Android \
    -executeMethod FlyingGame.EditorTools.QuestBuild.BuildQuest -logFile "$PWD/build/quest.log" || BUILD_OK=0
fi
echo "== unity done $(date)"
while IFS= read -r line; do
  key=${${line%%:*}// /}
  sed -i '' -E "s|^  ${key}:.*|${line}|" "$PS"
done <<< "$VERSION_WAS"
grep -E 'error CS|\[QuestBuild\]|BuildFailedException|OpenXR Build Failed' build/quest.log 2>/dev/null | tail -12 || true
if (( ! BUILD_OK )) || [ ! -f "$APK" ]; then
  echo "== BUILD FAILED — see build/quest.log"
  exit 1
fi
echo "== APK $APK ($(du -h "$APK" | cut -f1))"
if (( RELEASE )); then
  zsh tools/deploy/quest-vrc-check.sh "$APK" "$VERSION_CODE" || { echo "== VRC CHECK FAILED: do not upload"; exit 1; }
fi
if (( BUILD_ONLY )); then
  echo "== --build-only: not installing (adb not run) $(date)"
  exit 0
fi

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
