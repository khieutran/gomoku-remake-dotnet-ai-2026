#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."
: "${GOMOKU_E2E_BASE_URL:?Set GOMOKU_E2E_BASE_URL to the isolated same-origin API URL}"
if [[ -z "${GOMOKU_E2E_BROWSER_CHANNEL:-}" && -x '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome' ]]; then
  export GOMOKU_E2E_BROWSER_CHANNEL=chrome
fi

for project in desktop-chromium mobile-chromium; do
  if [[ "$project" == mobile-chromium && "${GOMOKU_E2E_RESTART_BETWEEN_PROJECTS:-}" == 1 ]]; then
    docker compose restart api
    docker compose up --wait -d api
    curl --fail --silent --show-error "$GOMOKU_E2E_BASE_URL/ready" >/dev/null
  fi
  npx playwright test "$@" --config frontend/playwright.config.ts --project="$project"
done
