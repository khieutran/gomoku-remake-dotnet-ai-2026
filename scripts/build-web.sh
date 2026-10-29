#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."
dotnet_bin="${GOMOKU_DOTNET_BIN:-$(command -v dotnet || true)}"
if [[ -z "$dotnet_bin" && -x /usr/local/share/dotnet/dotnet ]]; then
    dotnet_bin=/usr/local/share/dotnet/dotnet
fi
if [[ -z "$dotnet_bin" ]]; then
    printf 'dotnet SDK missing; set GOMOKU_DOTNET_BIN or add dotnet to PATH.\n' >&2
    exit 1
fi

npm ci
npm run build -w frontend

web_root="backend/Gomoku.Api/wwwroot"
mkdir -p "$web_root"
rsync -a --delete frontend/dist/ "$web_root/"

publish_dir="${1:-artifacts/web-publish}"
"$dotnet_bin" publish backend/Gomoku.Api/Gomoku.Api.csproj -c Release -o "$publish_dir"
