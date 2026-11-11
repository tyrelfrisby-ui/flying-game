#!/bin/zsh
# Archive the exported Xcode project, upload to App Store Connect (TestFlight), then export the OTA dev ipa to build/ota.
set -u
# This script's own directory, resolved through any symlink -- ${D:h:h} below is then the repo root, which
# is how the git check already found it.
D="${0:A:h}"
ARCH=/tmp/flyinggame-asc/AeroPlayground.xcarchive
# Refuse a stale iOS export (2026-10-11: four uploads re-sent the 07:25 export, each a "new build" email, none with
# the day's changes). The export must be newer than the last commit to the game's code; run deploy.sh first.
PL=/private/tmp/flyinggame-ios/Info.plist
[ -f $PL ] || { echo "NO iOS EXPORT — run tools/deploy/deploy.sh first"; exit 1; }
LAST=$(git -C ${D:h:h} log -1 --format=%ct -- unity/Assets src)
[ $(stat -f %m $PL) -gt $LAST ] || { echo "STALE iOS EXPORT ($(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' $PL)) — older than the last code commit; run tools/deploy/deploy.sh first"; exit 1; }
# WHICH BRANCH THIS CHECKOUT IS ON -- A REFUSAL, NOT A WARNING. 2026-11-10: the main checkout sits on
# `split-surfaces`, 1448 commits behind the iOS lane, and that branch's own HEAD message says "the branch this
# checkout builds from". Anyone running this from the obvious place would have shipped an app months old. Worse,
# BOTH trunks still carry the Pitts' propeller inertia at 9.0 against the lanes' blade-rule 3.06 -- a 2.9x
# gyroscopic couple that rolls the aeroplane out of an inverted spin, waiting in a branch that invites you to
# build from it. check-parity.sh already KNOWS this and prints "whatever is built here is in NEITHER lane"; only
# the enforcement was missing, and a printed warning in a 20-minute build scrolls past unread.
# Deliberately NOT fixed by merging the trunks current: a trunk merged current today drifts again tomorrow, and a
# kept-current trunk is a lane in all but name, against the trunk rule installed 2026-11-10.
# FLYINGGAME_ALLOW_OFF_LANE=1 overrides, for a deliberate experiment.
DEPLOY_BRANCH=$(git -C ${D:h:h} rev-parse --abbrev-ref HEAD 2>/dev/null)
if [ -z "${FLYINGGAME_ALLOW_OFF_LANE:-}" ] && ! printf '%s' "$DEPLOY_BRANCH" | grep -Eq '^lane/'; then
  echo "REFUSING: this checkout is on '$DEPLOY_BRANCH', which is not a lane/* branch."
  echo "  A build from here goes into NEITHER shipping lane, and the trunks carry known-stale flight-model"
  echo "  numbers (Pitts propInertia 9.0 against the lanes' 3.06). Build from a lane worktree instead:"
  echo "    ~/Developer/flying-game-worktrees/claude-tv-companion   (iOS)"
  echo "  FLYINGGAME_ALLOW_OFF_LANE=1 to override deliberately."
  exit 2
fi
rm -rf /tmp/flyinggame-asc; mkdir -p /tmp/flyinggame-asc
echo "== archive $(date)"
xcodebuild -project /private/tmp/flyinggame-ios/Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration Release \
  -destination 'generic/platform=iOS' -archivePath $ARCH archive -derivedDataPath /tmp/flyinggame-asc/dd \
  -allowProvisioningUpdates -allowProvisioningDeviceRegistration 2>&1 | grep -E 'error|BUILD|ARCHIVE' | tail -10
echo "== upload $(date)"
xcodebuild -exportArchive -archivePath $ARCH -exportOptionsPlist $D/ExportOptions-asc.plist -exportPath /tmp/flyinggame-asc/export -allowProvisioningUpdates 2>&1 | grep -E 'Upload succeeded|EXPORT|error' | tail -5
echo "== ota $(date)"
rm -rf /tmp/flyinggame-asc/dev
xcodebuild -exportArchive -archivePath $ARCH -exportOptionsPlist $D/ExportOptions-dev.plist -exportPath /tmp/flyinggame-asc/dev -allowProvisioningUpdates 2>&1 | grep -E 'EXPORT|error' | tail -3
cp /tmp/flyinggame-asc/dev/*.ipa ${D:h:h}/build/ota/FlyingGame.ipa && echo "== OTA ipa refreshed"
echo "== DONE $(date)"
