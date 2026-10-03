#!/bin/zsh
# Mac app native plugin (owner 2026-10-02: Mac/iPad/iPhone parity). The SAME .mm sources the iOS build compiles in Xcode
# (unity/Assets/Plugins/iOS: VoiceIO = radio/intercom, ClipEncoder = clips/recording, PilotSpeech = callouts), built here
# as a universal macOS bundle the Mac player loads with [DllImport("AeroNative")]. Re-run after editing those sources.
set -e
REPO=${0:A:h:h:h}
SRC=$REPO/unity/Assets/Plugins/iOS
OUT=$REPO/unity/Assets/Plugins/macOS/AeroNative.bundle
TMP=$(mktemp -d)
for f in VoiceIO ClipEncoder PilotSpeech; do
  for arch in arm64 x86_64; do
    clang++ -c -std=c++17 -fobjc-arc -O2 -arch $arch -mmacosx-version-min=11.0 -x objective-c++ "$SRC/$f.mm" -o "$TMP/$f-$arch.o"
  done
done
for arch in arm64 x86_64; do
  clang++ -bundle -arch $arch -mmacosx-version-min=11.0 -fobjc-arc "$TMP"/*-$arch.o -o "$TMP/AeroNative-$arch" \
    -framework Foundation -framework AVFoundation -framework CoreMedia -framework CoreVideo -framework QuartzCore \
    -framework Photos -framework CoreAudio -framework AudioToolbox
done
mkdir -p "$OUT/Contents/MacOS"
lipo -create "$TMP/AeroNative-arm64" "$TMP/AeroNative-x86_64" -output "$OUT/Contents/MacOS/AeroNative"
cat > "$OUT/Contents/Info.plist" <<PL
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>AeroNative</string>
<key>CFBundleIdentifier</key><string>com.tyrelfrisby.aeroplayground.native</string>
<key>CFBundlePackageType</key><string>BNDL</string>
<key>CFBundleName</key><string>AeroNative</string>
<key>CFBundleShortVersionString</key><string>1.0</string>
</dict></plist>
PL
rm -rf "$TMP"
codesign --force -s - "$OUT" >/dev/null 2>&1 || true
echo "built $OUT"; lipo -info "$OUT/Contents/MacOS/AeroNative"
