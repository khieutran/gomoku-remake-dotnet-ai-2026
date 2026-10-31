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
out=frontend/public/videos/finish
mkdir -p "$out"
for scene in sword arrow fire electric; do
  for format in wide tall; do
    # Playwright's ffmpeg has no pipe protocol, so stage the MJPEG stream in a file.
    "$renderer" "$scene" "$format" >"$work/frames.mjpeg"
    "$ffmpeg_bin" -loglevel error -y -f image2pipe -framerate 24 -vcodec mjpeg -i "$work/frames.mjpeg" \
        -c:v libvpx -b:v 900k -crf 10 -an "$out/$scene-$format.webm"
    echo "rendered $out/$scene-$format.webm"
  done
done
