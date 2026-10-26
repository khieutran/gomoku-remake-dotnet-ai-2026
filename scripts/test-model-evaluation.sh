#!/usr/bin/env bash
set -euo pipefail

# Run only after Tasks 5–9 code is complete and the requested test approval is given.
"${GOMOKU_DOTNET_BIN:-dotnet}" test \
  tests/Gomoku.ModelEvaluation.Tests/Gomoku.ModelEvaluation.Tests.csproj \
  --nologo -m:1 -p:UseSharedCompilation=false
