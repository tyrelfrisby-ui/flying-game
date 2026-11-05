#!/bin/zsh
# Reserve the complete Extra verification workflow using the standing deploy gate convention.
# Other lanes check ^zsh tools/deploy/deploy; this process stays visible during .NET tests,
# Unity shots and the gaps between stages. Only this verifier PID is excluded from its own gate.
set -euo pipefail
cd "${0:A:h:h:h}"
export EXTRA_VERIFY_RESERVATION_PID=$$
mkdir -p build
case "${1:-all}" in
 all) ;;
 ios)
  # Resume only after the shot tool completed; useful if Unity hangs during CoreShutdown.
  grep -q '^\[CockpitEye\] PASS: 8 shots, 0 failures' build/extra-shots.log
  if grep -q 'error CS' build/extra-shots.log; then exit 1; fi
  ;;
 *) echo 'usage: zsh tools/deploy/deploy-extra-verification.sh [all|ios]' >&2; exit 64 ;;
esac
busy() {
 pgrep -f '^/Applications/Unity/Hub/Editor/[^ ]*/Unity.app/Contents/MacOS/Unity' >/dev/null || pgrep -x xcodebuild >/dev/null || pgrep -f '^zsh tools/deploy/deploy' | awk -v own="${EXTRA_VERIFY_RESERVATION_PID:-0}" '$1 != own {found=1} END {exit !found}' || pgrep -f '^dotnet test' >/dev/null
}
wait_slot() {
 local idle=0
 while (( idle < 3 )); do
  if busy; then idle=0; else idle=$((idle+1)); fi
  date '+waiting for build slot: %H:%M:%S'
  sleep 10
 done
}
wait_slot
if [[ "${1:-all}" == all ]]; then
if nice -n 5 "$HOME/.dotnet/dotnet" test tools/FlightTests --filter 'FullyQualifiedName~ExtraRearDiagonalTests|FullyQualifiedName~ExtraCanopySealTests|FullyQualifiedName~ExtraCockpitV3Tests|FullyQualifiedName~NoRed233Tests' > build/extra-tests.log 2>&1; then
 echo "Targeted tests PASS"
else
 echo "Targeted tests RED: inspect build/extra-tests.log; no passing gate claimed"
fi
wait_slot
export COCKPIT_OUT="$HOME/Developer/agent-lanes/renders/cockpit-1105/codex-extra-remove-diagonals"
mkdir -p "$COCKPIT_OUT/front-iac-small"
export COCKPIT_SHOTS='rear-eye-iphone|extra-300-like|rear|0|-10|72|sun||2160x1000;rear-eye-quest|extra-300-like|rear|0|-10|90;front-eye-iphone|extra-300-like|front|0|-10|72|sun||2160x1000;rear-left-junction|extra-300-like|rear|-35|-25|60;rear-right-junction|extra-300-like|rear|35|-25|60;front-iac-small/front-eye-iphone|extra-300-like|front|0|-10|72|sun||2160x1000;front-iac-small/front-eye-quest|extra-300-like|front|0|-10|90;front-iac-small/front-iac-closeup|extra-300-like|front|16|-22|45'
nice -n 5 /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "$PWD/unity" -buildTarget iOS -executeMethod FlyingGame.EditorTools.CockpitEyeShots.Run -logFile "$PWD/build/extra-shots.log"
fi
wait_slot
nice -n 5 zsh tools/deploy/deploy.sh NOINSTALL > build/extra-ios.log 2>&1
