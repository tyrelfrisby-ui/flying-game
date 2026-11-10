#!/bin/zsh
# Unity export → xcodebuild → install + launch on the owner's phone (USB). Clean caches every build (stale
# serialized-object bug). Logs to build/ios.log.
set -e
set -o pipefail   # a failed xcodebuild must stop the script (its output is piped through tail)
# The repo root from THIS SCRIPT's own location, not a hardcoded path. ${0:A} resolves symlinks, so this
# works whether the script is reached through ~/Developer/flying-game or the ~/Documents symlink -- and it
# keeps working if that symlink goes away. tools/deploy/deploy.sh -> three :h to the root.
cd "${0:A:h:h:h}"
# Every physical device that is reachable right now gets the build — iPhone, iPad mini, iPad Air — or just the
# ones named as arguments (UDIDs). The phone used to be hard-coded, and with it asleep the script failed at the
# install and the iPads got nothing, which on 2026-10-17 meant installing by hand four times.
if (( $# > 0 )); then
  DEVICES=("$@")
else
  # DISCOVERY IS NOT REACHABILITY, and `devicectl list devices` cannot tell them apart: it reports all three of
  # these as `available (paired)` even while `devicectl device info details` says the phone's Device State is
  # `connected` with a live tunnel. So there is nothing to sort by here -- take every physical device and let the
  # install itself be the test, which is why the failure branch below has to print devicectl's real reason.
  DEVICES=(${(f)"$(xcrun devicectl list devices 2>/dev/null | awk '/physical/ && (/connected/ || /available \(paired\)/) { for (i=1;i<=NF;i++) if ($i ~ /^[0-9A-Fa-f]{8}-[0-9A-Fa-f]+$/) print $i }')"})
fi
# PER-LANE LIBRARY CACHES, CAPPED. Each lane's unity/Library is a symlink to its own
# ~/Library/Application Support/FlyingGame/unity-Library-<lane>, and nothing ever removed one: 165 GB of them on
# 2026-11-02 with the disk 97% full. So: mark this lane's cache as used (its mtime is the "last used" the pruner
# sorts by), then keep at most FG_CACHE_KEEP (8) caches and FG_CACHE_MAX_GB (80) GB, oldest first, never this
# lane's and never one a running Unity/xcodebuild/gradle has open. See tools/deploy/prune-library-caches.sh
# (it has a --dry-run). A cache the pruner removed comes back here as an empty dir -- a full import, not a broken
# symlink. `|| true`: a prune must never stop a deploy.
if [ -L unity/Library ]; then
  LIB_CACHE=$(readlink unity/Library)
  if [ ! -d "$LIB_CACHE" ]; then
    echo "== unity/Library -> $LIB_CACHE is gone (pruned?); recreating it empty -- Unity will re-import everything"
    mkdir -p "$LIB_CACHE"
  fi
  touch "$LIB_CACHE"
fi
tools/deploy/prune-library-caches.sh --current unity/Library || true
rm -rf /private/tmp/flyinggame-ios /private/tmp/flyinggame-dd unity/Library/PlayerDataCache unity/Library/Bee unity/Library/il2cpp_cache unity/Library/BuildPlayerData
# iCloud conflict copies break the build. This guard has now been wrong twice, in the same way both times:
# it matched too narrowly and the thing it missed took a build down.
#   2026-10-21: it matched `* 2.*` only, which catches a duplicated FILE but not a duplicated DIRECTORY, and
#               iCloud had duplicated whole package folders (klak.ndi/Runtime 2, klak.syphon/Editor 2, ...).
#               Duplicate asmdefs and types took the compile down as 200+ bogus "Sse2 does not exist" errors
#               inside Unity's own Burst package.
#   2026-10-23: it matched " 2" only, and iCloud numbers conflicts " 2", " 3" ... " 22". It also looked only
#               under unity/* and src, so 104 copies under tools/FlightTests were invisible. 943 of them
#               appeared at once; a "Forest 2.cs" beside "Forest.cs" is picked up by the SDK compile glob, so
#               FlyingGame.Core failed with 536 CS0101 errors and the test suite could not build.
# So: match ANY number, and look under tools and configs too.
# DELETE ONLY WHERE A BASE FILE EXISTS. A numbered file with no base is a real file, not a conflict copy --
# Unity writes 82 of them into Assets/Resources (PerformanceTestRunInfo 9.json, PerformanceTestRunSettings
# 14.json.meta, ...) and a blind numeric match would delete every one. Dry-run on 2026-10-23: 123 stale
# copies matched, all 82 Unity files correctly kept.
# -depth so a parent going first does not orphan the walk. -L because unity/Library is a SYMLINK since
# 2026-10-21 (to ~/Library/Application Support/FlyingGame/unity-Library, which iCloud does not sync); find
# would otherwise look at the link and walk nothing.
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
# AND A SHALLOW PASS OVER THE PARENTS, because find starts AT its roots and so can never see a duplicate OF
# one. `unity/Library 2` sat there from 2026-10-21 until 2026-10-23 for exactly that reason -- a conflict copy
# of the 2.6 GB Unity cache, invisible to a walk rooted at `unity/Library`. Same for `build/iOS/Data 2` and
# friends. maxdepth keeps this cheap; build/ is pure output so it is only swept here, not above.
prune_conflicts unity build -maxdepth 2
# assets-incoming/ has no tracked files, so git does not protect it and only a snapshot on the T5 does. A
# snapshot is not a sync: download a new mesh and the newest tarball quietly stops being the truth. Warn here
# because this is the script that actually gets run. Read-only, and `|| true` because `set -e` is on above --
# a stale backup must never stop a deploy.
tools/backup-assets.sh || true
# PARITY (Ty 2026-11-04: "ios/mac and quest must stay in sync at all times"; docs/PARITY.md). Printed so whoever
# deploys sees what the Quest lane is missing, NEVER a block: an iOS build goes ahead whatever it says. The Quest
# deploy is the one that refuses (tools/deploy/deploy-quest.sh on the Quest lane, PARITY_OVERRIDE=1 to force).
tools/check-parity.sh --brief || echo "== parity is RED (above): merge island-nav into the Quest lane the same day. This iOS build goes ahead."
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

# == APP SIZE AGAINST APP STORE CONNECT'S 4 GB ==
# There was no size check anywhere in this pipeline, which is why 4.2 GB was a surprise: build 2026.1105.1550 went
# over ITMS-90558's limit and nobody knew until the upload bounced, and every number in the record after that was
# measured by hand. Apple's limit is on the UNCOMPRESSED payload, so that is what this measures -- the .app as built,
# and the one file that is almost all of it (Data/resources.assets.resS: 93.8 % of that build, and to within 0.04 %
# nothing but GLB textures at 4 bytes a pixel with their mips). It only ever PRINTS: a deploy that refuses because an
# estimate crossed a line would cost more than it saves, and the pilot wants his build.
APP_BYTES=$(du -sk "$APP" 2>/dev/null | awk '{print $1*1024}')
RESS="$APP/Data/resources.assets.resS"
RESS_BYTES=$([ -f "$RESS" ] && stat -f %z "$RESS" || echo 0)
printf '== app size: %.2f GB uncompressed (resources.assets.resS %.2f GB, %d%%) against a 4 GB limit\n' \
  "$(echo "$APP_BYTES/1000000000" | bc -l)" "$(echo "$RESS_BYTES/1000000000" | bc -l)" \
  "$([ "$APP_BYTES" -gt 0 ] && echo $((RESS_BYTES * 100 / APP_BYTES)) || echo 0)"
if [ "$APP_BYTES" -gt 4000000000 ]; then
  echo "== SIZE: OVER 4 GB. App Store Connect will reject the upload (ITMS-90558). One livery per type should be on"
  echo "==       by default (FLYINGGAME_PHONE_ALL_LIVERIES unset); FLYINGGAME_TF_SLIM=1 also drops the fleet to six."
fi
# Explicit build-only mode: never enter device discovery/install/launch for NOINSTALL.
if (( ${#DEVICES} == 1 )) && [[ "$DEVICES[1]" == NOINSTALL ]]; then
  echo "== NOINSTALL — build only: $APP"
  echo "== DONE $(date)"
  exit 0
fi
if (( ${#DEVICES} == 0 )); then
  echo "== no device reachable — the build is at $APP; re-run with a UDID, or wake a device and run again"
  echo "== DONE $(date)"
  exit 0
fi
# A device that has gone to sleep must not take the others down with it, so the installs are not under set -e.
# PRINT devicectl's OWN REASON when an install fails. This used to say "(asleep, or off the network)" -- a guess,
# printed as though it were the diagnosis, with devicectl's actual message thrown away by the pipe into grep. On
# 2026-10-28 that guess sent us to the OTA route when a plain `devicectl device install app` worked by hand on
# both devices moments later; a route that works being reported as unavailable costs more than a failed install.
# Also name the device, because a bare UDID tells you nothing about which one went missing.
INSTALLED=0
# ...and that has to be SAID: `set -e` from the top was still on, so a locked iPad's failing `OUT=$(devicectl ...)`
# ended the whole script with no message and the iPhone after it was never tried (2026-11-02).
set +e
for D in $DEVICES; do
  NAME=$(xcrun devicectl list devices 2>/dev/null | awk -v d="$D" 'index($0, d) { sub(/  +[0-9A-Fa-f]{8}-.*/, ""); print; exit }')
  [ -n "$NAME" ] || NAME=$D
  echo "== install $NAME $(date +%H:%M:%S)"
  OUT=$(xcrun devicectl device install app --device $D "$APP" 2>&1)
  if print -r -- "$OUT" | grep -q 'App installed'; then
    xcrun devicectl device process launch --terminate-existing --device $D com.tyrelfrisby.aeroplayground 2>&1 | tail -1
    INSTALLED=$((INSTALLED + 1))
  else
    echo "== $NAME would not take it:"
    print -r -- "$OUT" | grep -iE 'error|unable|denied|asleep|not (connected|paired|found)|timed out|NSLocalizedDescription|NSLocalizedRecoverySuggestion' | head -4 | sed 's/^/     /'
    print -r -- "$OUT" | grep -qiE 'error|unable|denied|asleep|not (connected|paired|found)|timed out' || print -r -- "$OUT" | tail -4 | sed 's/^/     /'
  fi
done
echo "== installed on $INSTALLED of ${#DEVICES} device(s)"
echo "== DONE $(date)"
