#!/bin/zsh
# Builds Plugins/iOS/AeroLink.swift with AeroLinkSelfTest.swift into a throwaway Mac executable and runs it: the real
# TLS-PSK listener and client over 127.0.0.1 (Q10). No Unity, no device. Exit 0 = passed.
set -e
HERE=${0:A:h}; REPO=${HERE:h:h}
TMP=$(mktemp -d)
cp "$HERE/AeroLinkSelfTest.swift" "$TMP/main.swift"   # (top-level code is allowed only in main.swift)
swiftc -O -module-name AeroLinkSelfTest -o "$TMP/aerolink-selftest" "$REPO/unity/Assets/Plugins/iOS/AeroLink.swift" "$TMP/main.swift"
"$TMP/aerolink-selftest"
rc=$?
rm -rf "$TMP"
exit $rc
