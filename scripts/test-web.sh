#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
npm run typecheck -w frontend
npm run lint -w frontend
npm run test -w frontend -- --run
npm run build -w frontend
