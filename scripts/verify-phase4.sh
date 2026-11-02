#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."
mode="${1:-}"
if [[ "$mode" != preflight && "$mode" != verify ]]; then
  echo "Usage: bash scripts/verify-phase4.sh preflight|verify" >&2
  exit 2
fi

for program in docker npm npx python3 openssl curl; do
  command -v "$program" >/dev/null || { echo "Missing $program" >&2; exit 1; }
done
dotnet_bin="$(command -v dotnet || true)"
if [[ -z "$dotnet_bin" && -x /usr/local/share/dotnet/dotnet ]]; then dotnet_bin=/usr/local/share/dotnet/dotnet; fi
if [[ -z "$dotnet_bin" ]]; then echo "Missing dotnet SDK" >&2; exit 1; fi
docker info >/dev/null
docker compose version >/dev/null
node --version
npm --version
"$dotnet_bin" --version
if [[ "$mode" == preflight ]]; then
  echo "Phase 4 preflight ready"
  exit 0
fi

umask 077
run_id="$(date -u +%Y%m%dT%H%M%SZ)-$$"
log_dir="docs/Detail-phase-4/test-results/$run_id"
mkdir -p "$log_dir"
export COMPOSE_PROJECT_NAME="gomoku_p4_$$"
export GOMOKU_POSTGRES_PASSWORD="$(openssl rand -hex 24)"
export GOMOKU_REDIS_NAMESPACE="gomoku:p4:$run_id"
read -r GOMOKU_API_PORT GOMOKU_POSTGRES_PORT GOMOKU_REDIS_PORT < <(python3 -c 'import socket; s=[socket.socket() for _ in range(3)]; [x.bind(("127.0.0.1", 0)) for x in s]; print(*(x.getsockname()[1] for x in s)); [x.close() for x in s]')
export GOMOKU_API_PORT GOMOKU_POSTGRES_PORT GOMOKU_REDIS_PORT
export GOMOKU_E2E_BASE_URL="http://127.0.0.1:$GOMOKU_API_PORT"

cleanup() {
  result=$?
  if (( result != 0 )); then
    # Show the failing step's log right in the CI output instead of only a bare "exit code 1".
    failed_log="$(ls -t "$log_dir"/*.log 2>/dev/null | head -1 || true)"
    if [[ -n "$failed_log" ]]; then
      echo "::group::Failed step log: $(basename "$failed_log") (last 80 lines)"
      tail -n 80 "$failed_log" | sed -E 's/\x1b\[[0-9;]*m//g' || true
      echo "::endgroup::"
      grep -hE "✘|Error:|Expected|Received|FAIL|error " "$failed_log" | sed -E 's/\x1b\[[0-9;]*m//g' | head -20 |
        while IFS= read -r line; do echo "::error title=$(basename "$failed_log")::$line"; done || true
    fi
    docker compose logs --no-color >"$log_dir/compose.log" 2>&1 || true
  fi
  docker compose down --volumes --remove-orphans >"$log_dir/cleanup.log" 2>&1 || true
  echo "Phase 4 logs: $log_dir"
  exit "$result"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

if [[ "${GOMOKU_SKIP_NPM_CI:-0}" != 1 ]]; then
  npm ci >"$log_dir/npm-ci.log" 2>&1
fi
bash scripts/test-web.sh >"$log_dir/web.log" 2>&1
docker compose up --build --wait -d redis api >"$log_dir/compose-up.log" 2>&1
test_database=gomoku_phase4_tests
export GOMOKU_TEST_POSTGRES="Host=127.0.0.1;Port=$GOMOKU_POSTGRES_PORT;Database=$test_database;Username=gomoku;Password=$GOMOKU_POSTGRES_PASSWORD"
export GOMOKU_TEST_REDIS="127.0.0.1:$GOMOKU_REDIS_PORT,abortConnect=false,connectTimeout=500,asyncTimeout=500"
export GOMOKU_DP_KEYS_DIR="$PWD/$log_dir/dpkeys"
mkdir -p "$GOMOKU_DP_KEYS_DIR"
curl --fail --silent --show-error "$GOMOKU_E2E_BASE_URL/ready" >"$log_dir/ready.json"
curl --fail --silent --show-error "$GOMOKU_E2E_BASE_URL/" >"$log_dir/index.html"
GOMOKU_E2E_RESTART_BETWEEN_PROJECTS=1 bash scripts/test-web-e2e.sh --workers=1 >"$log_dir/playwright.log" 2>&1
GOMOKU_E2E_REDIS_FAULT=1 bash scripts/test-web-e2e.sh api-failure.spec.ts --grep 'Redis outage' --workers=1 >"$log_dir/redis-outage.log" 2>&1
docker compose up --wait -d redis >"$log_dir/redis-recovery.log" 2>&1
docker compose exec -T postgres createdb -U gomoku "$test_database" >"$log_dir/test-db-create.log" 2>&1
docker compose run --rm --no-deps \
  -e "GOMOKU_POSTGRES_CONNECTION=Host=postgres;Port=5432;Database=$test_database;Username=gomoku;Password=$GOMOKU_POSTGRES_PASSWORD" \
  migrate >"$log_dir/test-db-migrate.log" 2>&1
GOMOKU_REDIS_NAMESPACE=gomoku:development "$dotnet_bin" test tests/Gomoku.Api.Tests/Gomoku.Api.Tests.csproj >"$log_dir/dotnet.log" 2>&1
echo "Phase 4 verification passed; logs: $log_dir"
