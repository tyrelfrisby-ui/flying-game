#!/bin/zsh
# Checks a Quest APK against Meta's upload rules before it goes near the store (VRC.Quest.Packaging.1,
# developers.meta.com/horizon/resources/publish-mobile-manifest, read 2026-11-03). Prints the signer and every rule,
# exit 1 if any rule fails. Reads the APK only.
#
#   zsh tools/deploy/quest-vrc-check.sh build/quest/release/AeroPlayground.apk [expected-versionCode]
set -o pipefail
APK="$1"; WANT_CODE="$2"
[ -f "$APK" ] || { echo "usage: $0 <apk> [versionCode]"; exit 64; }
AP=/Applications/Unity/Hub/Editor/6000.3.23f1/PlaybackEngines/AndroidPlayer
BT=$(ls -d "$AP"/SDK/build-tools/* | sort -V | tail -1)
export JAVA_HOME="$AP/OpenJDK"
FAIL=0
rule() { if eval "$2"; then echo "  PASS  $1"; else echo "  FAIL  $1"; FAIL=1; fi }

BADGING="$("$BT/aapt2" dump badging "$APK")"
XML="$("$BT/aapt2" dump xmltree --file AndroidManifest.xml "$APK")"
CERTS="$("$BT/apksigner" verify --print-certs "$APK" 2>/dev/null)"; SIG_OK=$?
attr() { print -r -- "$XML" | grep -E "android:$1\(" | head -1 | sed -E 's/.*\)=(0x[0-9a-f]+|"[^"]*"|[0-9]+).*/\1/; s/^"//; s/"$//' }

echo "== $APK ($(du -h "$APK" | cut -f1))"
print -r -- "$BADGING" | grep -E "^package:" | sed 's/^/  /'
print -r -- "$CERTS" | grep -E "certificate (DN|SHA-256)" | sed 's/^/  /'
rule "signature verifies" "(( SIG_OK == 0 ))"
rule "not signed with the Android Debug key" "! print -r -- \"\$CERTS\" | grep -q 'CN=Android Debug'"
rule "package com.tyrelfrisby.aeroplayground.quest" "print -r -- \"\$BADGING\" | grep -q \"package: name='com.tyrelfrisby.aeroplayground.quest'\""
[ -n "$WANT_CODE" ] && rule "versionCode $WANT_CODE" "print -r -- \"\$BADGING\" | grep -q \"versionCode='$WANT_CODE'\""
rule "native code arm64-v8a only" "[[ \"\$(print -r -- \"\$BADGING\" | grep '^native-code')\" == \"native-code: 'arm64-v8a'\" ]]"
rule "minSdkVersion 32 (got $(attr minSdkVersion))" "[[ \$(attr minSdkVersion) == 32 ]]"
rule "targetSdkVersion 34 (got $(attr targetSdkVersion))" "[[ \$(attr targetSdkVersion) == 34 ]]"
rule "installLocation auto (got $(attr installLocation))" "[[ -z \"\$(attr installLocation)\" || \$(attr installLocation) == 0 ]]"
rule "not debuggable" "! print -r -- \"\$XML\" | grep -qE 'android:debuggable\(0x[0-9a-f]+\)=(true|0xffffffff)'"
rule "uses-feature android.hardware.vr.headtracking required, version 1" "print -r -- \"\$BADGING\" | grep -q \"^  uses-feature: name='android.hardware.vr.headtracking' version='1'\""
rule "launcher intent has com.oculus.intent.category.VR" "print -r -- \"\$XML\" | grep -q 'com.oculus.intent.category.VR'"
rule "launcher activity excludeFromRecents" "print -r -- \"\$XML\" | grep -qE 'android:excludeFromRecents\(0x[0-9a-f]+\)=(true|0xffffffff)'"
DEV=$(print -r -- "$XML" | grep -A1 '"com.oculus.supportedDevices"' | tail -1 | sed -E 's/.*\(Raw: "([^"]*)"\).*/\1/')
rule "supportedDevices from Meta's list (got $DEV)" "[[ -n \"\$DEV\" ]] && ! print -r -- \"\$DEV\" | tr '|' '\n' | grep -qvxE 'quest2|questpro|quest3|quest3s|vrglasses'"
rule "no INTERNET permission" "! print -r -- \"\$BADGING\" | grep -q \"uses-permission: name='android.permission.INTERNET'\""
echo "  permissions:"; print -r -- "$BADGING" | grep "^uses-permission" | sed 's/^/    /'
rule "application label set" "print -r -- \"\$BADGING\" | grep -q \"^application-label:'Aero Playground'\""
(( FAIL )) && echo "== VRC check: FAILED" || echo "== VRC check: all rules pass"
exit $FAIL
