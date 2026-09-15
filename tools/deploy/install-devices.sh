#!/bin/zsh
# Install + launch the last xcodebuild product on every one of the owner's devices that is connected (phone, iPad Air 11, iPad mini).
APP="$(ls -d /private/tmp/flyinggame-dd/Build/Products/Release-iphoneos/*.app | head -1)"
for D in 757C3F5F-062C-5070-9AF1-60E428B8E9C7:iPhone 5BE5CD6C-75BA-5918-87E1-B102853FCDE1:iPadAir E7648C42-7C18-5EB6-8467-442B07B65154:iPadMini; do
  ID=${D%%:*}; NAME=${D##*:}
  echo "== $NAME"
  xcrun devicectl device install app --device $ID "$APP" 2>&1 | grep -E 'error|Error|installed|databaseSequence' | tail -1
  xcrun devicectl device process launch --terminate-existing --device $ID com.tyrelfrisby.aeroplayground 2>&1 | tail -1
done
