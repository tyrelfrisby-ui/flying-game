#!/usr/bin/env bash
# AERO RC LINK -- run the host tests. No ESP32 toolchain, no board, no PlatformIO: just clang.
#
# These cover the PPM and SBUS decoders, the format detector, the calibration, the failsafe decision, both output
# maps, the JoyLink packet bytes, and the throttle servo (against a transcription of the game's own
# GamepadShaping.Lever). Everything in src/ -- pins, UART, Wi-Fi, BLE -- is NOT covered and is untested firmware
# until it is flashed. See the README.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="${TMPDIR:-/tmp}/aero-rclink-host-test"
c++ -std=c++17 -Wall -Wextra -Wno-unused-parameter -O1 -o "$out" "$here/host_test.cpp"
"$out"
