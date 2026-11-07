#!/bin/zsh
# Capture the asset-only baseline in an isolated APFS clone, using current fixture code/configs.
# No tests, deploy, install or upload. Keep the whole operation visible to other deploy gates.
set -euo pipefail
cd "${0:A:h:h:h}"
instrument_baseline_pid=$$
for instrument_idle_check in 1 2 3; do
 if pgrep -f '^/Applications/Unity/Hub/Editor/[^ ]*/Unity.app/Contents/MacOS/Unity' >/dev/null || pgrep -x xcodebuild >/dev/null || pgrep -f '^([^ ]*/)?dotnet (test|exec .*testhost[.]dll)' >/dev/null || pgrep -f '^zsh tools/deploy/deploy' | awk -v own="$instrument_baseline_pid" '$1 != own {found=1} END {exit !found}'; then
  echo 'Baseline slot busy; no work started.'
  exit 75
 fi
 sleep 10
done
python3 tools/own_models/capture_instrument_baseline.py
