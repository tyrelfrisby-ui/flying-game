#!/bin/zsh
# Archive the exported Xcode project, upload to App Store Connect (TestFlight), then export the OTA dev ipa to build/ota.
set -u
D=/Users/tyfrisby/Documents/flying-game/tools/deploy
ARCH=/tmp/flyinggame-asc/AeroPlayground.xcarchive
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
cp /tmp/flyinggame-asc/dev/*.ipa /Users/tyfrisby/Documents/flying-game/build/ota/FlyingGame.ipa && echo "== OTA ipa refreshed"
echo "== DONE $(date)"
