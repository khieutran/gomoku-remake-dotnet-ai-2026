#!/usr/bin/env bash
# Renders the four finish cutscenes in wide (desktop board) and tall (phone board) formats.
# Needs Swift (Xcode command line tools) and an ffmpeg with libvpx; falls back to Playwright's bundled ffmpeg.
set -euo pipefail
cd "$(dirname "$0")/.."

ffmpeg_bin="$(command -v ffmpeg || ls "$HOME"/Library/Caches/ms-playwright/ffmpeg-*/ffmpeg-mac 2>/dev/null | tail -1 || true)"
[[ -x "$ffmpeg_bin" ]] || { echo "ffmpeg with libvpx not found (install ffmpeg or run: npx playwright install ffmpeg)" >&2; exit 1; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
renderer="$work/render-finish-cutscenes"
swiftc -O scripts/render-finish-cutscenes.swift -o "$renderer"
# Vite imports these (content-hashed URLs) from src/features/match/finishScenes.ts.
out=frontend/src/assets/finish
mkdir -p "$out"
for scene in sword arrow fire electric; do
  for format in wide tall; do
    # Playwright's ffmpeg has no pipe protocol, so stage the MJPEG stream in a file.
    "$renderer" "$scene" "$format" >"$work/frames.mjpeg"
    "$ffmpeg_bin" -loglevel error -y -f image2pipe -framerate 24 -vcodec mjpeg -i "$work/frames.mjpeg" \
        -c:v libvpx -b:v 900k -crf 10 -an "$out/$scene-$format.webm"
    # Poster = frame 0 (the MJPEG stream is concatenated JPEGs). Without it Android WebView paints a grey
    # play-button placeholder until the first frame decodes.
    python3 -c 'import sys; d=open(sys.argv[1],"rb").read(); open(sys.argv[2],"wb").write(d[:d.index(b"\xff\xd9")+2])' \
      "$work/frames.mjpeg" "$out/$scene-$format.jpg"
    echo "rendered $out/$scene-$format.webm"
  done
done
