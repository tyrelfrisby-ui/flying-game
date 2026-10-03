#!/bin/zsh
# Mac app (owner 2026-10-02: Mac + iPad + iPhone kept in parity — run this with every iOS deploy).
# Unity build (universal, Mono) to /private/tmp (Google Drive xattrs break codesign in ~/Documents) → ad-hoc sign →
# /Applications/Aero Playground.app → relaunch. Log: build/mac.log
set -e
REPO=${0:A:h:h:h}
UNITY=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity
OUT=/private/tmp/aero-mac
rm -rf "$OUT"
FLYINGGAME_MAC_OUT=$OUT "$UNITY" -batchmode -quit -projectPath "$REPO/unity" -buildTarget OSXUniversal \
  -executeMethod FlyingGame.EditorTools.BuildScript.BuildMac -logFile "$REPO/build/mac.log"
grep -q "macOS build SUCCEEDED" "$REPO/build/mac.log" || { echo "MAC BUILD FAILED — see build/mac.log"; exit 1; }
APP="$OUT/Aero Playground.app"
xattr -rc "$APP"; codesign --force --deep -s - "$APP"
pkill -f "Aero Playground.app/Contents/MacOS" || true
rm -rf "/Applications/Aero Playground.app"; cp -R "$APP" /Applications/
open "/Applications/Aero Playground.app"
echo "== DONE $(date) — $(defaults read "/Applications/Aero Playground.app/Contents/Info" CFBundleShortVersionString)"
