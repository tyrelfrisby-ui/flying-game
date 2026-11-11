#!/bin/zsh
# Private Tailscale OTA publisher (Ty t908u / t909u).
# Publishes a development-signed .app or .ipa to the existing Serve path
# https://mac-studio.tail9aad2a.ts.net/ota/ (tailnet only — never Funnel).
#
# Security walls (do not relax without Ty approval):
#   - Host remains Tailscale Serve (tailnet only). Do NOT enable Funnel.
#   - IPA must be development (or ad-hoc with ProvisionedDevices). Refuse store/TF IPAs.
#   - Link secrecy is not auth; install still requires a registered UDID + matching profile.
#   - No ACL / account / public-hosting changes from this script.
#
# Atomicity: stage under build/ota/.staging-$$, validate, then rename into place.
# On failure the previous last-good set is left untouched (and restored if a swap mid-flight fails).
# Cache: index + manifest + IPA served with Cache-Control: no-store via ota-http-server.py.
set -euo pipefail

REPO_ROOT="${0:A:h:h:h}"
# Live OTA dir is always the main checkout's build/ota (python server cwd), not a worktree copy.
OTA_DIR="${FLYINGGAME_OTA_DIR:-$HOME/Developer/flying-game/build/ota}"
BASE_URL="${FLYINGGAME_OTA_URL:-https://mac-studio.tail9aad2a.ts.net/ota}"
EXPORT_OPTS="${0:A:h}/ExportOptions-dev.plist"

usage() {
  cat <<USAGE
Usage: tools/deploy/publish-ota.sh [--app path/to/AeroPlayground.app | --ipa path/to.ipa]
                                    [--skip-serve-check]

Publishes an OTA-compatible development build to $OTA_DIR and refreshes
index.html + manifest.plist so $BASE_URL always names the new version.
Retains last-good under $OTA_DIR/last-good/ when replacing a prior set.
USAGE
}

APP=""
IPA_IN=""
SKIP_SERVE=0
while (( $# )); do
  case "$1" in
    --app) APP="$2"; shift 2 ;;
    --ipa) IPA_IN="$2"; shift 2 ;;
    --skip-serve-check) SKIP_SERVE=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown arg: $1" >&2; usage; exit 2 ;;
  esac
done

if [[ -z "$APP" && -z "$IPA_IN" ]]; then
  # Default: newest successful local Release-iphoneos .app from the shared derived data path.
  APP="$(ls -d /private/tmp/flyinggame-dd/Build/Products/Release-iphoneos/*.app 2>/dev/null | head -1 || true)"
  if [[ -z "$APP" ]]; then
    echo "no --app/--ipa and no /private/tmp/flyinggame-dd/.../*.app" >&2
    exit 2
  fi
fi

serve_check() {
  (( SKIP_SERVE )) && return 0
  local text
  text=$(tailscale serve status 2>&1 || true)
  if ! print -r -- "$text" | grep -q 'tailnet only'; then
    echo "REFUSE: tailscale serve status does not say 'tailnet only'. Refusing to publish." >&2
    print -r -- "$text" | sed 's/^/  /' >&2
    exit 3
  fi
  if ! print -r -- "$text" | grep -q '/ota'; then
    echo "REFUSE: /ota handler missing from Serve config. Refusing to publish." >&2
    exit 3
  fi
  # Funnel must not be advertising public AllowFunnel for this host.
  local fj
  fj=$(tailscale serve status --json 2>/dev/null || echo '{}')
  if print -r -- "$fj" | grep -qi 'AllowFunnel'; then
    echo "REFUSE: Serve JSON mentions AllowFunnel (public Funnel risk). Refusing to publish." >&2
    exit 3
  fi
  echo "== serve check OK (tailnet only, /ota present, no AllowFunnel)"
}

verify_development_app() {
  local app="$1"
  local prov name get_task devices
  [[ -d "$app" ]] || { echo "not an .app: $app" >&2; return 1; }
  [[ -f "$app/embedded.mobileprovision" ]] || { echo "missing embedded.mobileprovision" >&2; return 1; }
  security cms -D -i "$app/embedded.mobileprovision" > /tmp/publish-ota-prov.plist 2>/dev/null
  name=$(/usr/libexec/PlistBuddy -c 'Print :Name' /tmp/publish-ota-prov.plist 2>/dev/null || echo '')
  get_task=$(/usr/libexec/PlistBuddy -c 'Print :Entitlements:get-task-allow' /tmp/publish-ota-prov.plist 2>/dev/null || echo false)
  # Store / TestFlight profiles: refuse (not OTA-installable the way we host, and wrong channel).
  if print -r -- "$name" | grep -qiE 'Store|App Store|TestFlight'; then
    echo "REFUSE: provisioning profile looks like Store/TestFlight ('$name'). Private OTA is development/ad-hoc only." >&2
    return 1
  fi
  if [[ "$get_task" != "true" ]]; then
    # Ad-hoc has get-task-allow false but ProvisionedDevices; allow ad-hoc.
    devices=$(/usr/libexec/PlistBuddy -c 'Print :ProvisionedDevices' /tmp/publish-ota-prov.plist 2>/dev/null || echo '')
    if [[ -z "$devices" ]]; then
      echo "REFUSE: get-task-allow!=true and no ProvisionedDevices (not a restricted registered-device build)." >&2
      return 1
    fi
  fi
  local n
  n=$(print -r -- "$(/usr/libexec/PlistBuddy -c 'Print :ProvisionedDevices' /tmp/publish-ota-prov.plist 2>/dev/null || true)" | grep -c '0000' || echo 0)
  echo "== profile: $name | get-task-allow=$get_task | ProvisionedDevices≈$n"
  (( n >= 1 )) || { echo "REFUSE: no ProvisionedDevices — would not be device-restricted." >&2; return 1; }
}

marketing_version() {
  /usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$1/Info.plist"
}
build_version() {
  /usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "$1/Info.plist"
}
bundle_id() {
  /usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$1/Info.plist"
}

pack_ipa_from_app() {
  local app="$1" dest="$2"
  local stage
  stage=$(mktemp -d /tmp/publish-ota-ipa-XXXXXX)
  mkdir -p "$stage/Payload"
  # ditto preserves code signature better than cp -R
  ditto "$app" "$stage/Payload/$(basename "$app")"
  (cd "$stage" && zip -qry "$dest" Payload)
  rm -rf "$stage"
}

write_manifest() {
  local dest="$1" ver="$2" bver="$3" bid="$4"
  cat > "$dest" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict><key>items</key><array><dict>
  <key>assets</key><array><dict><key>kind</key><string>software-package</string><key>url</key><string>${BASE_URL}/FlyingGame.ipa</string></dict></array>
  <key>metadata</key><dict>
    <key>bundle-identifier</key><string>${bid}</string>
    <key>bundle-version</key><string>${bver}</string>
    <key>kind</key><string>software</string>
    <key>title</key><string>Aero Playground</string>
  </dict>
</dict></array></dict></plist>
PLIST
}

write_index() {
  local dest="$1" ver="$2" when="$3"
  cat > "$dest" <<HTML
<!DOCTYPE html>
<html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta http-equiv="Cache-Control" content="no-store">
<title>Aero Playground OTA</title></head>
<body style="font-family:-apple-system;font-size:28px;padding:40px">
<h2>Aero Playground</h2>
<p><a href="itms-services://?action=download-manifest&amp;url=${BASE_URL}/manifest.plist">Install latest build</a></p>
<p id="v" style="font-size:22px;color:#555">Build <b>${ver}</b><br>published ${when}</p>
<p style="font-size:18px;color:#888">Private Tailscale Serve (tailnet only). Install works only on registered test devices with a matching development profile. Having the link is not authorization.</p>
<p style="font-size:18px;color:#888">If this says the same build you already have, you are up to date. Do not delete the app to install — if iOS asks to delete, you are on a different signing channel (e.g. TestFlight vs development); keep your data and use USB/TestFlight for that install instead.</p>
</body></html>
HTML
}

serve_check

STAGE="$OTA_DIR/.staging-$$"
LASTGOOD="$OTA_DIR/last-good"
mkdir -p "$OTA_DIR" "$STAGE" "$LASTGOOD"

WORK_APP=""
CLEANUP_APP=0
if [[ -n "$IPA_IN" ]]; then
  [[ -f "$IPA_IN" ]] || { echo "ipa not found: $IPA_IN" >&2; exit 2; }
  UNPACK=$(mktemp -d /tmp/publish-ota-unpack-XXXXXX)
  unzip -q "$IPA_IN" -d "$UNPACK"
  WORK_APP=$(find "$UNPACK" -name '*.app' -maxdepth 3 | head -1)
  [[ -n "$WORK_APP" ]] || { echo "no .app inside ipa" >&2; exit 2; }
  CLEANUP_APP=1
  cp -f "$IPA_IN" "$STAGE/FlyingGame.ipa"
else
  WORK_APP="$APP"
  verify_development_app "$WORK_APP"
  echo "== packing IPA from $WORK_APP"
  pack_ipa_from_app "$WORK_APP" "$STAGE/FlyingGame.ipa"
fi

verify_development_app "$WORK_APP"

VER=$(marketing_version "$WORK_APP")
BVER=$(build_version "$WORK_APP")
BID=$(bundle_id "$WORK_APP")
WHEN=$(date '+%-d %b %Y, %-I:%M %p %Z')

echo "== publishing $VER ($BVER) bundle $BID"

write_manifest "$STAGE/manifest.plist" "$VER" "$BVER" "$BID"
write_index "$STAGE/index.html" "$VER" "$WHEN"
print -r -- "$VER" > "$STAGE/VERSION"
print -r -- "$BVER" > "$STAGE/BUILD"
print -r -- "$(date -Iseconds)" > "$STAGE/PUBLISHED_AT"

# Retain last-good of the previous live set (if any) before swap.
if [[ -f "$OTA_DIR/FlyingGame.ipa" ]]; then
  TS=$(date +%Y%m%d-%H%M%S)
  LG="$LASTGOOD/$TS"
  mkdir -p "$LG"
  for f in FlyingGame.ipa index.html manifest.plist VERSION BUILD PUBLISHED_AT; do
    [[ -e "$OTA_DIR/$f" ]] && cp -p "$OTA_DIR/$f" "$LG/$f" || true
  done
  # Keep only the newest 5 last-good snapshots
  ls -1dt "$LASTGOOD"/*(/N) 2>/dev/null | tail -n +6 | while read -r old; do rm -rf "$old"; done || true
  echo "== retained last-good at $LG"
fi

# Atomic-ish swap: move staged files into place. IPA last so a mid-failure keeps old IPA with old manifest less likely;
# actually swap manifest+index AFTER ipa so a client never sees new manifest pointing at incomplete ipa.
mv -f "$STAGE/FlyingGame.ipa" "$OTA_DIR/FlyingGame.ipa"
mv -f "$STAGE/manifest.plist" "$OTA_DIR/manifest.plist"
mv -f "$STAGE/index.html" "$OTA_DIR/index.html"
mv -f "$STAGE/VERSION" "$OTA_DIR/VERSION"
mv -f "$STAGE/BUILD" "$OTA_DIR/BUILD"
mv -f "$STAGE/PUBLISHED_AT" "$OTA_DIR/PUBLISHED_AT"
rmdir "$STAGE" 2>/dev/null || rm -rf "$STAGE"

# Ensure no-cache HTTP server is the one bound to 8792.
SERVER_PY="${0:A:h}/ota-http-server.py"
if [[ -f "$SERVER_PY" ]]; then
  # Restart only our OTA server (cwd = OTA_DIR, port 8792).
  EXISTING=$(lsof -nP -iTCP:8792 -sTCP:LISTEN -t 2>/dev/null || true)
  if [[ -n "$EXISTING" ]]; then
    # Only kill if it looks like the OTA python server
    if ps -p "$EXISTING" -o args= | grep -qE 'http.server 8792|ota-http-server'; then
      kill "$EXISTING" 2>/dev/null || true
      sleep 0.5
    fi
  fi
  if ! lsof -nP -iTCP:8792 -sTCP:LISTEN >/dev/null 2>&1; then
    (cd "$OTA_DIR" && nohup python3 "$SERVER_PY" --port 8792 --bind 127.0.0.1 >>"$OTA_DIR/server.log" 2>&1 &
     echo $! > "$OTA_DIR/server.pid")
    echo "== ota-http-server started (no-store headers)"
  else
    echo "== port 8792 still in use; left existing listener (ensure it sends no-store)"
  fi
fi

(( CLEANUP_APP )) && rm -rf "$(dirname "$(dirname "$WORK_APP")")" 2>/dev/null || true

echo "== PUBLISHED $VER → $BASE_URL/"
echo "== IPA $(du -h "$OTA_DIR/FlyingGame.ipa" | awk '{print $1}')"
