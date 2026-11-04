#!/bin/zsh
set -eu
export PYTHONDONTWRITEBYTECODE=1
export PATH="$HOME/.dotnet:$PATH"
cd "${0:A:h}/../.."
work=$(mktemp -d /tmp/crash-audio-render.XXXXXX)
trap 'rm -rf "$work"' EXIT
# Frozen pre-change baseline: contract commit, before this audio lane.
for name in OneShotSynth Dsp; do
  git show "564bcad:unity/Assets/_Project/Scripts/Bridge/Audio/$name.cs" | sed 's/namespace FlyingGame.Bridge/namespace OldAudio/' > "$work/Old$name.cs"
done
out=${1:-/Users/tyfrisby/Developer/agent-lanes/aero-playground/crash-audio}
mkdir -p "$out"
dotnet build tools/CrashAudio/CrashAudio.csproj --nologo -v:q -p:NoWarn=CS0649 -p:OldAudioDir="$work" -p:BaseIntermediateOutputPath="$work/obj/" -p:OutputPath="$work/bin/"
dotnet "$work/bin/CrashAudio.dll" "$out"
${AUDIO_PYTHON:-python3} tools/CrashAudio/analyze.py "$out"
