#!/usr/bin/env bash
set -euo pipefail

# Run only after Tasks 5-9 code is complete and the requested test approval is given.
cd "$(dirname "$0")/.."
"${GOMOKU_DOTNET_BIN:-dotnet}" test \
  tests/Gomoku.SelfPlay.Tests/Gomoku.SelfPlay.Tests.csproj \
  --nologo -m:1 -p:UseSharedCompilation=false
