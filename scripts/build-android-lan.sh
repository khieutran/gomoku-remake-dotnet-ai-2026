#!/usr/bin/env bash
# Builds a debug APK that loads the game from a server URL baked into the app.
#   LAN (same Wi-Fi):  GOMOKU_API_BIND=0.0.0.0 docker compose up -d --build, then run this script as is.
#   Public tunnel:     GOMOKU_APP_URL=https://gomoku.example.com bash scripts/build-android-lan.sh
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ -n "${GOMOKU_APP_URL:-}" ]]; then
  export GOMOKU_LAN_URL="${GOMOKU_APP_URL%/}"
else
  port="${GOMOKU_API_PORT:-58080}"
  ip="${GOMOKU_LAN_IP:-$(ipconfig getifaddr en0 || ipconfig getifaddr en1 || true)}"
  if [[ -z "$ip" ]]; then
    echo "Could not detect this Mac's LAN IP. Set GOMOKU_LAN_IP=192.168.x.x or GOMOKU_APP_URL and retry." >&2
    exit 1
  fi
  export GOMOKU_LAN_URL="http://$ip:$port"
fi
export ANDROID_HOME="${ANDROID_HOME:-$HOME/Library/Android/sdk}"

shell_dir=frontend/.android-lan-shell
rm -rf "$shell_dir" && mkdir -p "$shell_dir"
sed "s|__GOMOKU_LAN_URL__|$GOMOKU_LAN_URL|g" frontend/android-lan-shell/index.html >"$shell_dir/index.html"

(cd frontend && npx cap sync android)
(cd frontend/android && ./gradlew --quiet assembleDebug)

mkdir -p artifacts/android
apk_name=$([[ -n "${GOMOKU_APP_URL:-}" ]] && echo gomoku-debug.apk || echo gomoku-lan-debug.apk)
cp frontend/android/app/build/outputs/apk/debug/app-debug.apk "artifacts/android/$apk_name"
echo "APK: artifacts/android/$apk_name (server: $GOMOKU_LAN_URL)"
if ! curl --fail --silent --max-time 3 "$GOMOKU_LAN_URL/ready" >/dev/null; then
  echo "Warning: $GOMOKU_LAN_URL/ready is not reachable yet. Start the backend (LAN or tunnel) before opening the app." >&2
fi
