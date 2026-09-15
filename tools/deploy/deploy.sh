#!/bin/zsh
# Unity export → xcodebuild → install + launch on the owner's phone (USB). Clean caches every build (stale
# serialized-object bug). Logs to build/ios.log.
set -e
cd /Users/tyfrisby/Documents/flying-game
UDID=00008150-001629A83662401C
rm -rf /private/tmp/flyinggame-ios /private/tmp/flyinggame-dd unity/Library/PlayerDataCache unity/Library/Bee unity/Library/il2cpp_cache unity/Library/BuildPlayerData
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
  -derivedDataPath /private/tmp/flyinggame-dd DEVELOPMENT_TEAM=DH425V439F build 2>&1 | tail -5
echo "== install $(date)"
xcrun devicectl device install app --device $UDID "$(ls -d /private/tmp/flyinggame-dd/Build/Products/Release-iphoneos/*.app | head -1)" 2>&1 | tail -3
echo "== launch $(date)"
xcrun devicectl device process launch --terminate-existing --device $UDID com.tyrelfrisby.aeroplayground 2>&1 | tail -2
echo "== DONE $(date)"
