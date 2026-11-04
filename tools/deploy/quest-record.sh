#!/bin/zsh
# Record what the Meta Quest shows, to a file on the Mac — or pull the recordings the game made itself.
#
#   zsh tools/deploy/quest-record.sh                 record the LEFT eye (with sound) until Ctrl-C
#   zsh tools/deploy/quest-record.sh --full          both eyes, uncropped
#   zsh tools/deploy/quest-record.sh --watch         also show it live in a window on the Mac
#   zsh tools/deploy/quest-record.sh --no-audio      video only
#   zsh tools/deploy/quest-record.sh --crop W:H:X:Y  your own crop (device pixels, natural orientation)
#   zsh tools/deploy/quest-record.sh -o FILE.mp4     somewhere else
#   zsh tools/deploy/quest-record.sh --pull          copy the in-game recordings (hold A + B in the headset) here
#   any of them with  -s SERIAL / --serial SERIAL   when more than one device is attached
#
# Files land in ~/Movies/AeroQuest/ (recordings named <date>_<time>.mp4; pulled ones keep their AeroQuest-… names).
# Recording uses scrcpy (Homebrew, 5.x): H.264 + AAC in an .mp4 that QuickTime opens. Ctrl-C stops it, and scrcpy
# finalises the file before it exits. Sound is the headset's app audio ("playback", Android 13+), duplicated so it
# still plays in the headset; if the headset cannot capture it, scrcpy records the video alone and says so.
# Without scrcpy it falls back to `adb shell screenrecord` (no sound, no crop, at most 3 minutes) and pulls the file.
#
# Exit status: 0 = a file was written / pulled; 1 = recording failed; 2 = no authorized Quest attached; 64 = usage.
set -o pipefail
SELF="${0:A}"
cd "${0:A:h:h:h}"

APP_ID=com.tyrelfrisby.aeroplayground.quest
IN_APP_DIR=/sdcard/Android/data/$APP_ID/files/Movies
DEST="$HOME/Movies/AeroQuest"

# adb: the Unity Android module's own first (the version Unity builds against), then Homebrew, then PATH — the same
# order as deploy-quest.sh. Its directory goes on PATH because scrcpy looks for `adb` there (or in $ADB).
ADB=""
for c in /Applications/Unity/Hub/Editor/6000.3.23f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb /opt/homebrew/bin/adb; do
  [ -x "$c" ] && { ADB="$c"; break; }
done
[ -n "$ADB" ] || ADB="$(command -v adb || true)"
if [ -z "$ADB" ]; then
  echo "== no adb found (Unity Android module, Homebrew or PATH)"
  exit 2
fi
export ADB
export PATH="${ADB:h}:$PATH"
SCRCPY=""
for c in /opt/homebrew/bin/scrcpy "$(command -v scrcpy 2>/dev/null)"; do
  [ -n "$c" ] && [ -x "$c" ] && { SCRCPY="$c"; break; }
done

usage() { sed -n '2,13p' "$SELF" | sed 's/^# \{0,1\}//'; exit ${1:-64}; }

MODE=record
SERIAL=""
FULL=0
WATCH=0
AUDIO=1
CROP=""
OUT=""
while (( $# )); do
  case "$1" in
    -s|--serial) [ -n "$2" ] || usage; SERIAL="$2"; shift 2 ;;
    --serial=*)  SERIAL="${1#*=}"; shift ;;
    --full)      FULL=1; shift ;;
    --watch)     WATCH=1; shift ;;
    --no-audio)  AUDIO=0; shift ;;
    --crop)      [ -n "$2" ] || usage; CROP="$2"; shift 2 ;;
    --crop=*)    CROP="${1#*=}"; shift ;;
    -o|--out)    [ -n "$2" ] || usage; OUT="$2"; shift 2 ;;
    --out=*)     OUT="${1#*=}"; shift ;;
    --pull)      MODE=pull; shift ;;
    -h|--help)   usage 0 ;;
    *)           echo "== unknown option: $1"; usage ;;
  esac
done
if [ -n "$CROP" ] && [[ ! "$CROP" =~ '^[0-9]+:[0-9]+:[0-9]+:[0-9]+$' ]]; then
  echo "== --crop wants width:height:x:y in device pixels, got '$CROP'"
  exit 64
fi

# ---- the headset (same diagnosis as deploy-quest.sh) ----
"$ADB" start-server >/dev/null 2>&1 || true
DEVICES="$("$ADB" devices -l | tail -n +2 | sed '/^\s*$/d')"
if [ -z "$SERIAL" ]; then
  SERIAL=$(print -r -- "$DEVICES" | awk '$2 == "device" { print $1; exit }')
elif ! print -r -- "$DEVICES" | awk -v s="$SERIAL" '$1 == s && $2 == "device" { f = 1 } END { exit !f }'; then
  echo "== $SERIAL is not an attached, authorized device. adb devices -l:"
  [ -n "$DEVICES" ] && print -r -- "$DEVICES" | sed 's/^/     /' || echo "     (none)"
  exit 2
fi
if [ -z "$SERIAL" ]; then
  if print -r -- "$DEVICES" | grep -q 'unauthorized'; then
    echo "== the Quest is attached but UNAUTHORIZED: put the headset on and accept 'Allow USB debugging'."
  elif print -r -- "$DEVICES" | grep -q 'offline'; then
    echo "== the Quest is attached but OFFLINE: unplug and replug the cable, or restart adb (adb kill-server)."
  else
    echo "== no Quest attached: plug it in by USB-C, with Developer Mode on (Meta Horizon app > Devices > Headset settings)."
  fi
  exit 2
fi
A=("$ADB" -s "$SERIAL")
mkdir -p "$DEST"

# ---- --pull: the game's own recordings ----
if [ "$MODE" = pull ]; then
  echo "== recordings on $SERIAL in $IN_APP_DIR"
  FILES=("${(@f)$("${A[@]}" shell "ls -1 '$IN_APP_DIR' 2>/dev/null" | tr -d '\r' | grep -E '\.mp4$')}")
  FILES=(${FILES:#})
  if (( ${#FILES} == 0 )); then
    echo "== none yet (in the headset: hold A + B for a second, or VR menu > Record video)"
    exit 0
  fi
  pulled=0
  for f in "${FILES[@]}"; do
    remote_size=$("${A[@]}" shell "stat -c %s '$IN_APP_DIR/$f'" | tr -d '\r')
    if [ -f "$DEST/$f" ] && [ "$(stat -f %z "$DEST/$f")" = "$remote_size" ]; then
      echo "   $f — already here"
      continue
    fi
    echo "   $f ($(( remote_size / 1048576 )) MB)"
    "${A[@]}" pull "$IN_APP_DIR/$f" "$DEST/$f" >/dev/null || { echo "== pull failed: $f"; exit 1; }
    pulled=$(( pulled + 1 ))
  done
  echo "== $pulled new in $DEST (the headset keeps its copies; delete them there with: $ADB -s $SERIAL shell rm '$IN_APP_DIR/<name>')"
  exit 0
fi

# ---- record ----
[ -n "$OUT" ] || OUT="$DEST/$(date +%Y-%m-%d_%H-%M-%S).mp4"
[[ "$OUT" == *.mp4 ]] || OUT="$OUT.mp4"
mkdir -p "${OUT:h}"

if [ -z "$SCRCPY" ]; then
  # Fallback: the device's own screenrecord. No sound, no crop, and Android caps one file at 180 s.
  echo "== scrcpy not found (brew install scrcpy): falling back to adb screenrecord — no sound, uncropped, 3 min max"
  REMOTE="/sdcard/Movies/aeroquest-mac-$(date +%s).mp4"
  "${A[@]}" shell "mkdir -p /sdcard/Movies"
  "${A[@]}" shell screenrecord --bit-rate 16000000 --time-limit 180 "$REMOTE" &
  REC=$!
  # Ctrl-C reaches the adb client too, which would kill screenrecord before it writes the index: stop it on the
  # device with SIGINT instead, which makes it finalise, then wait for it.
  trap 'echo; echo "== stopping"; "${A[@]}" shell "pkill -2 screenrecord" >/dev/null 2>&1' INT
  echo "== recording on $SERIAL — Ctrl-C to stop (it stops by itself after 3 minutes)"
  wait $REC 2>/dev/null
  wait $REC 2>/dev/null
  trap - INT
  sleep 1
  "${A[@]}" pull "$REMOTE" "$OUT" >/dev/null && "${A[@]}" shell "rm '$REMOTE'"
  if [ -s "$OUT" ]; then echo "== saved $OUT ($(du -h "$OUT" | cut -f1))"; exit 0; fi
  echo "== nothing recorded"
  exit 1
fi

ARGS=(--serial="$SERIAL" --record="$OUT" --record-format=mp4 --no-control --video-bit-rate=16M)
if (( WATCH )); then
  ARGS+=(--no-audio-playback --window-title="Quest (recording)")
else
  ARGS+=(--no-window --no-audio-playback)
fi
if (( AUDIO )); then
  ARGS+=(--audio-source=playback --audio-dup --audio-codec=aac)
else
  ARGS+=(--no-audio)
fi

# The crop. Every Quest display is one landscape panel with the two eyes side by side, left eye on the left, so
# the left eye is the left half. scrcpy wants the crop in the device's natural orientation, which is what
# `wm size` reports (an override size, if someone set one, wins over the physical one).
if [ -n "$CROP" ]; then
  ARGS+=(--crop="$CROP")
  echo "== crop $CROP (yours)"
elif (( ! FULL )); then
  SIZE=$("${A[@]}" shell wm size | tr -d '\r' | awk -F': ' '/Override size/ { o = $2 } /Physical size/ { p = $2 } END { print (o != "" ? o : p) }')
  W=${SIZE%x*}; H=${SIZE#*x}
  if [[ "$W" == <-> && "$H" == <-> ]] && (( W > H )); then
    HALF=$(( W / 2 / 2 * 2 ))   # even: the encoder wants even sides
    ARGS+=(--crop="$HALF:$(( H / 2 * 2 )):0:0")
    echo "== display $SIZE: recording the left eye, crop $HALF:$(( H / 2 * 2 )):0:0 (--full for both eyes)"
  else
    echo "== display size '$SIZE' is not a side-by-side landscape panel: recording it uncropped"
  fi
else
  echo "== recording both eyes, uncropped"
fi

# Ctrl-C goes to scrcpy (same process group), which closes the file properly. The script only has to wait for it
# rather than die first, which an empty-but-set trap does.
trap 'echo; echo "== stopping — scrcpy is finalising the file"' INT
echo "== recording $SERIAL to $OUT — Ctrl-C to stop"
"$SCRCPY" "${ARGS[@]}"
STATUS=$?
trap - INT
if [ -s "$OUT" ]; then
  echo "== saved $OUT ($(du -h "$OUT" | cut -f1))"
  exit 0
fi
echo "== scrcpy exited $STATUS and wrote nothing. If the crop was refused try --full; if the sound was, --no-audio."
exit 1
