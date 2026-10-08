#!/bin/zsh
# Aero Widget (owner 2026-10-07): build the transparent Syphon/NDI widget app → /Applications/Aero Widget.app. Log: build/widget.log
set -e
REPO=${0:A:h:h:h}
UNITY=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity
OUT=/private/tmp/aero-widget
rm -rf "$OUT"; rm -f "$REPO/unity/Temp/UnityLockfile"
FLYINGGAME_WIDGET_OUT=$OUT "$UNITY" -batchmode -quit -projectPath "$REPO/unity" -buildTarget OSXUniversal \
  -executeMethod FlyingGame.EditorTools.BuildScript.BuildWidget -logFile "$REPO/build/widget.log"
grep -q "widget build SUCCEEDED" "$REPO/build/widget.log" || { echo "WIDGET BUILD FAILED — see build/widget.log"; exit 1; }
APP="$OUT/Aero Widget.app"
xattr -rc "$APP"; codesign --force --deep -s - "$APP"
pkill -f "Aero Widget.app/Contents/MacOS" || true
rm -rf "/Applications/Aero Widget.app"; cp -R "$APP" /Applications/
echo "== DONE $(date) — $(defaults read "/Applications/Aero Widget.app/Contents/Info" CFBundleShortVersionString)"
