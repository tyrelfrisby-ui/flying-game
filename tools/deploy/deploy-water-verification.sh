#!/bin/zsh
# Reserve current-source water captures, magenta regression and NOINSTALL proof as one serialized workflow.
set -euo pipefail
cd "${0:A:h:h:h}"
water_verify_pid=$$
water_out="$HOME/Developer/agent-lanes/renders/water-visuals-overhaul"
mkdir -p "$water_out" build/water-overhaul
busy() {
 pgrep -f '^/Applications/Unity/Hub/Editor/[^ ]*/Unity.app/Contents/MacOS/Unity' >/dev/null || pgrep -x xcodebuild >/dev/null || pgrep -f '^zsh tools/deploy/deploy' | awk -v own="$water_verify_pid" '$1 != own {found=1} END {exit !found}' || pgrep -f '^dotnet test' >/dev/null
}
wait_slot() {
 local idle=0
 while (( idle < 3 )); do
  if busy; then idle=0; else idle=$((idle+1)); fi
  sleep 10
 done
}
shot() {
 nice -n 5 /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "$PWD/unity" -buildTarget iOS -executeMethod FlyingGame.EditorTools.WaterRender.RenderFromArgs -out "$water_out/$1" -logFile "$PWD/build/water-overhaul/$1.log" "${@:2}"
 grep -q 'WaterRender: wrote' "build/water-overhaul/$1.log"
 if grep -q 'error CS\|Shader error' "build/water-overhaul/$1.log"; then return 1; fi
}
case "${1:-all}" in
 all|renders|ios) ;;
 *) echo 'usage: zsh tools/deploy/deploy-water-verification.sh [all|renders|ios]' >&2; exit 64 ;;
esac
wait_slot
if [[ "${1:-all}" != ios ]]; then
 shot after-taxi -calm -speed 2.5 -throttle 0
 shot after-spray -speed 18 -throttle 1
 shot after-submerged -calm -speed 0 -throttle 0 -lift -0.35
 shot after-sunset -preset sunset -calm -speed 2.5 -throttle 0
 nice -n 5 /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "$PWD/unity" -buildTarget iOS -executeMethod FlyingGame.EditorTools.WaterMagentaCheck.Run -logFile "$PWD/build/water-overhaul/magenta-check.log"
 grep -q '^PASS$' build/water-magenta/report.txt
fi
if [[ "${1:-all}" != renders ]]; then
nice -n 5 zsh tools/deploy/deploy.sh NOINSTALL > build/water-overhaul/ios-build.log 2>&1
grep '\*\* BUILD SUCCEEDED \*\*' build/water-overhaul/ios-build.log

fi
