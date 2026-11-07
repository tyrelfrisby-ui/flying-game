#!/bin/zsh
# Reserve tests, phone-sized cockpit captures and iOS build as one serialized workflow.
set -euo pipefail
cd "${0:A:h:h:h}"
mkdir -p build
instrument_reservation_pid=$$
busy() {
 pgrep -f '^/Applications/Unity/Hub/Editor/[^ ]*/Unity.app/Contents/MacOS/Unity' >/dev/null || pgrep -x xcodebuild >/dev/null || pgrep -f '^zsh tools/deploy/deploy' | awk -v own="$instrument_reservation_pid" '$1 != own {found=1} END {exit !found}' || pgrep -f '^([^ ]*/)?dotnet (test|exec .*testhost[.]dll)' >/dev/null
}
# Never remain registered while waiting on another reservation: that deadlocks
# verifiers which see one another as busy. Retry the whole command when idle.
for instrument_idle_check in 1 2 3; do
 if busy; then
  echo 'Build/test slot busy; no work started. Retry this command when other verifiers finish.'
  exit 75
 fi
 sleep 10
done
instrument_test_status=0
"$HOME/.dotnet/dotnet" test tools/FlightTests --filter 'FullyQualifiedName~Instrument|FullyQualifiedName~NoRed233Tests' > build/instrument-tests.log 2>&1 || instrument_test_status=$?
echo "Targeted test exit: $instrument_test_status (failure remains a red gate; collecting build/render evidence)"
export COCKPIT_TEMPORAL=1
export COCKPIT_OUT="$HOME/Developer/agent-lanes/renders/t932-instrument-fleet-flicker/after"
mkdir -p "$COCKPIT_OUT"
export COCKPIT_SHOTS='pitts-phone|pitts-s2b-like|rear|0|-15|72|sun||2160x1000;pitts-closeup|pitts-s2b-like|rear|0|-20|40|sun||2160x1000;great-lakes-phone|great-lakes-like|rear|0|-15|72|sun||2160x1000;great-lakes-closeup|great-lakes-like|rear|0|-20|40|sun||2160x1000;great-lakes-panel-phone|great-lakes-like|rear|0|-30|72|sun||2160x1000;great-lakes-panel-closeup|great-lakes-like|rear|0|-40|40|sun||2160x1000'
/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "$PWD/unity" -buildTarget iOS -executeMethod FlyingGame.EditorTools.CockpitEyeShots.Run -logFile "$PWD/build/instrument-shots.log"
zsh tools/deploy/deploy.sh NOINSTALL > build/instrument-ios.log 2>&1
echo "Verification test exit: $instrument_test_status"
exit "$instrument_test_status"
