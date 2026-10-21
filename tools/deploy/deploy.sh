#!/bin/zsh
# Unity export → xcodebuild → install + launch on the owner's phone (USB). Clean caches every build (stale
# serialized-object bug). Logs to build/ios.log.
set -e
set -o pipefail   # a failed xcodebuild must stop the script (its output is piped through tail)
cd /Users/tyfrisby/Documents/flying-game
# Every physical device that is reachable right now gets the build — iPhone, iPad mini, iPad Air — or just the
# ones named as arguments (UDIDs). The phone used to be hard-coded, and with it asleep the script failed at the
# install and the iPads got nothing, which on 2026-10-17 meant installing by hand four times.
if (( $# > 0 )); then
  DEVICES=("$@")
else
  DEVICES=(${(f)"$(xcrun devicectl list devices 2>/dev/null | awk '/physical/ && (/connected/ || /available \(paired\)/) { for (i=1;i<=NF;i++) if ($i ~ /^[0-9A-Fa-f]{8}-[0-9A-Fa-f]+$/) print $i }')"})
fi
rm -rf /private/tmp/flyinggame-ios /private/tmp/flyinggame-dd unity/Library/PlayerDataCache unity/Library/Bee unity/Library/il2cpp_cache unity/Library/BuildPlayerData
# iCloud conflict copies break the build. The old guard matched `* 2.*` only, which catches a duplicated FILE
# but not a duplicated DIRECTORY — and on 2026-10-21 iCloud duplicated whole package folders inside the package
# cache (klak.ndi/Runtime 2, klak.syphon/Editor 2, ...). Duplicate asmdefs and duplicate types took the entire
# compile down, surfacing as 200+ bogus "Sse2 does not exist" errors inside Unity's own Burst package. Match
# directories too, and delete deepest-first so a parent going first does not orphan the walk.
# -L because unity/Library is a SYMLINK since 2026-10-21 (to ~/Library/Application Support/FlyingGame/
# unity-Library, which iCloud does not sync); find would otherwise look at the link and walk nothing.
find -L unity/Assets unity/Library unity/Packages src \( -name "* 2" -o -name "* 2.*" \) -depth -exec rm -rf {} + 2>/dev/null || true
echo "== unity export $(date)"
FLYINGGAME_IOS_OUT=/private/tmp/flyinggame-ios \
  /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -nographics -projectPath "$PWD/unity" -buildTarget iOS \
  -executeMethod FlyingGame.EditorTools.BuildScript.BuildiOS -logFile "$PWD/build/ios.log"
echo "== unity done $(date)"
cd /private/tmp/flyinggame-ios && xattr -cr .
echo "== xcodebuild $(date)"
xcodebuild -project Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration Release \
  -destination "generic/platform=iOS" -allowProvisioningUpdates -allowProvisioningDeviceRegistration \
  -derivedDataPath /private/tmp/flyinggame-dd DEVELOPMENT_TEAM=DH425V439F build 2>&1 \
  | { grep -E 'error:|error generated|BUILD|The following build commands failed|^\s+(CompileC|Ld|CodeSign) ' || true; } | tail -14
# (the lines are filtered rather than tailed: a plugin that fails to compile puts its reason hundreds of
# lines above the summary, and `tail -5` showed only "(2 failures)". pipefail still carries xcodebuild's
# own exit status past the grep, so a failed build still stops the script.)
APP="$(ls -d /private/tmp/flyinggame-dd/Build/Products/Release-iphoneos/*.app | head -1)"
if (( ${#DEVICES} == 0 )); then
  echo "== no device reachable — the build is at $APP; re-run with a UDID, or wake a device and run again"
  echo "== DONE $(date)"
  exit 0
fi
# A device that has gone to sleep must not take the others down with it, so the installs are not under set -e.
INSTALLED=0
for D in $DEVICES; do
  echo "== install $D $(date +%H:%M:%S)"
  if xcrun devicectl device install app --device $D "$APP" 2>&1 | grep -q 'App installed'; then
    xcrun devicectl device process launch --terminate-existing --device $D com.tyrelfrisby.aeroplayground 2>&1 | tail -1
    INSTALLED=$((INSTALLED + 1))
  else
    echo "== $D would not take it (asleep, or off the network)"
  fi
done
echo "== installed on $INSTALLED of ${#DEVICES} device(s)"
echo "== DONE $(date)"
