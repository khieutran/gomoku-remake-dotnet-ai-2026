#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
mode="${1:-}"
if [[ "$mode" != preflight && "$mode" != verify ]]; then
    printf 'Usage: bash scripts/verify-phase2.sh preflight|verify\n' >&2
    exit 2
fi
for command_name in docker curl python3; do
    if ! command -v "$command_name" >/dev/null 2>&1; then
        printf 'Missing command: %s\n' "$command_name" >&2
        exit 127
    fi
done
docker compose config --quiet
bash scripts/test-cache.sh preflight
dotnet_bin="${GOMOKU_DOTNET_BIN:-$(command -v dotnet || true)}"
if [[ -z "$dotnet_bin" && -x /usr/local/share/dotnet/dotnet ]]; then
    dotnet_bin=/usr/local/share/dotnet/dotnet
fi
if [[ -z "$dotnet_bin" && -x /private/tmp/gomoku-dotnet10/dotnet ]]; then
    dotnet_bin=/private/tmp/gomoku-dotnet10/dotnet
fi
if [[ "$mode" == preflight ]]; then
    printf 'PASS: Phase 2 acceptance prerequisites\n'
    exit 0
fi

started_colima=0
if ! docker info >/dev/null 2>&1; then
    colima start
    started_colima=1
fi
export COMPOSE_PROJECT_NAME="gomoku_phase2_verify_$$_$RANDOM"
export GOMOKU_POSTGRES_PORT="$((30000 + RANDOM % 10000))"
export GOMOKU_REDIS_PORT="$((40000 + RANDOM % 10000))"
export GOMOKU_API_PORT="$((50000 + RANDOM % 10000))"
test_dir="$(mktemp -d "${TMPDIR:-/tmp}/gomoku-phase2.XXXXXXXX")"
log_dir="$repo_root/docs/Detail-phase-2/test-results"
mkdir -p "$log_dir"
cleanup() {
    docker compose down -v --remove-orphans >/dev/null 2>&1 || true
    rm -rf -- "$test_dir"
    if ((started_colima)); then
        colima stop >/dev/null 2>&1 || true
    fi
}
trap cleanup EXIT
base_url="http://127.0.0.1:$GOMOKU_API_PORT"

printf 'Building API and migration image for isolated Compose project %s\n' "$COMPOSE_PROJECT_NAME"
docker compose build migrate
docker compose up -d --wait redis api
assert_http() {
    local expected="$1" method="$2" path="$3" jar="$4" body="${5:-}" token="${6:-}"
    local status
    if [[ "$method" == GET ]]; then
        status="$(curl --silent --show-error --max-time 10 -b "$jar" -c "$jar" \
            -o "$test_dir/response.json" -w '%{http_code}' "$base_url$path")"
    else
        status="$(curl --silent --show-error --max-time 10 -b "$jar" -c "$jar" \
            -H 'Content-Type: application/json' -H "X-CSRF-TOKEN: $token" \
            -d "$body" -o "$test_dir/response.json" -w '%{http_code}' "$base_url$path")"
    fi
    if [[ "$status" != "$expected" ]]; then
        printf 'FAIL %s %s: expected %s, got %s\n' "$method" "$path" "$expected" "$status" >&2
        cat "$test_dir/response.json" >&2
        exit 1
    fi
}
json_value() {
    python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))[sys.argv[2]])' \
        "$test_dir/response.json" "$1"
}
first_jar="$test_dir/first.cookies"
second_jar="$test_dir/second.cookies"
assert_http 200 GET /health "$first_jar"
assert_http 200 GET /ready "$first_jar"
assert_http 200 GET /api/v1/auth/csrf "$first_jar"
first_csrf="$(json_value requestToken)"
assert_http 201 POST /api/v1/guest-sessions "$first_jar" '{}' "$first_csrf"
guest_id="$(json_value guestSessionId)"
assert_http 201 POST '/api/v1/matches?scope=guest' "$first_jar" \
    '{"commandId":"phase2-guest"}' "$first_csrf"
guest_match="$(json_value matchId)"
assert_http 201 POST /api/v1/auth/register "$first_jar" \
    '{"email":"phase2-first@example.test","password":"Phase2-Test-2026!"}' "$first_csrf"
account_id="$(json_value userId)"
assert_http 200 GET /api/v1/auth/csrf "$first_jar"
first_csrf="$(json_value requestToken)"
assert_http 201 POST '/api/v1/matches?scope=account' "$first_jar" \
    '{"commandId":"phase2-account"}' "$first_csrf"
account_match="$(json_value matchId)"

GOMOKU_ACCEPTANCE_POSTGRES="Host=127.0.0.1;Port=$GOMOKU_POSTGRES_PORT;Database=gomoku;Username=gomoku;Password=${GOMOKU_POSTGRES_PASSWORD:-gomoku_dev}" \
    "$dotnet_bin" run --project tools/Gomoku.AcceptanceSeed/Gomoku.AcceptanceSeed.csproj -- \
    "$account_id" "$account_match" "$guest_id" "$guest_match"

assert_http 200 GET /api/v1/auth/csrf "$second_jar"
second_csrf="$(json_value requestToken)"
assert_http 201 POST /api/v1/guest-sessions "$second_jar" '{}' "$second_csrf"
assert_http 404 GET "/api/v1/matches/$guest_match?scope=guest" "$second_jar"
assert_http 201 POST /api/v1/auth/register "$second_jar" \
    '{"email":"phase2-second@example.test","password":"Phase2-Test-2026!"}' "$second_csrf"
assert_http 404 GET "/api/v1/matches/$account_match?scope=account" "$second_jar"
printf 'PASS: account and guest owner isolation\n'

docker compose up -d --force-recreate --no-deps --wait api
assert_http 200 GET /api/v1/auth/me "$first_jar"
assert_http 200 GET "/api/v1/matches/$guest_match?scope=guest" "$first_jar"
assert_http 200 GET "/api/v1/matches/$account_match?scope=account" "$first_jar"
printf 'PASS: account and guest cookies survive API restart\n'

docker compose stop redis
docker compose up -d --force-recreate --no-deps --wait api
assert_http 200 GET /ready "$first_jar"
assert_http 200 GET /api/v1/auth/me "$first_jar"
assert_http 200 GET /api/v1/leaderboard "$first_jar"
python3 - "$test_dir/response.json" "$account_id" <<'PY'
import json, sys
items = json.load(open(sys.argv[1]))['items']
assert any(item['accountId'] == sys.argv[2] and item['points'] == 3 for item in items), items
PY
assert_http 200 GET '/api/v1/history?scope=account' "$first_jar"
python3 - "$test_dir/response.json" "$account_match" <<'PY'
import json, sys
items = json.load(open(sys.argv[1]))['items']
assert any(item['matchId'] == sys.argv[2] for item in items), items
PY
assert_http 200 GET '/api/v1/history?scope=guest' "$first_jar"
python3 - "$test_dir/response.json" "$guest_match" <<'PY'
import json, sys
items = json.load(open(sys.argv[1]))['items']
assert any(item['matchId'] == sys.argv[2] for item in items), items
PY
printf 'PASS: API cold start and nonempty PostgreSQL fallback while Redis is stopped\n'
docker compose up -d --wait redis

docker compose stop postgres
assert_http 200 GET /health "$first_jar"
assert_http 503 GET /ready "$first_jar"
docker compose up -d --wait postgres
docker compose up -d --wait api
assert_http 200 GET /ready "$first_jar"
printf 'PASS: readiness follows PostgreSQL while liveness stays available\n'

restore_database="gomoku_api_restored_$RANDOM"
sample_sql="SELECT (SELECT COUNT(*) FROM \"Matches\" WHERE \"Id\" = '$account_match'::uuid AND \"UserId\" = '$account_id'::uuid) || ',' || (SELECT COUNT(*) FROM \"Matches\" WHERE \"Id\" = '$guest_match'::uuid AND \"GuestSessionId\" = '$guest_id'::uuid) || ',' || (SELECT COUNT(*) FROM \"Moves\" WHERE \"GameId\" IN (SELECT \"Id\" FROM \"Games\" WHERE \"MatchId\" IN ('$account_match'::uuid, '$guest_match'::uuid))) || ',' || (SELECT \"RankingPoints\" FROM \"AspNetUsers\" WHERE \"Id\" = '$account_id'::uuid)"
sample_before="$(docker compose exec -T postgres psql -U gomoku -d gomoku -Atc "$sample_sql")"
if [[ "$sample_before" != '1,1,18,3' ]]; then
    printf 'FAIL: unexpected API sample before backup: %s\n' "$sample_before" >&2
    exit 1
fi
docker compose exec -T postgres pg_dump -U gomoku -d gomoku -Fc > "$test_dir/api.dump"
docker compose exec -T postgres psql -U gomoku -d postgres -v ON_ERROR_STOP=1 \
    -c "CREATE DATABASE \"$restore_database\""
docker compose exec -T postgres pg_restore -U gomoku -d "$restore_database" \
    --no-owner --no-acl < "$test_dir/api.dump"
sample_after="$(docker compose exec -T postgres psql -U gomoku -d "$restore_database" -Atc "$sample_sql")"
if [[ "$sample_after" != "$sample_before" ]]; then
    printf 'FAIL: API account/guest sample changed after restore: %s → %s\n' \
        "$sample_before" "$sample_after" >&2
    exit 1
fi
printf 'PASS: API account/guest matches, 18 Moves and 3 points survive backup/restore\n'

bash scripts/test-storage.sh > "$log_dir/task-9-storage.log" 2>&1 || {
    tail -n 100 "$log_dir/task-9-storage.log" >&2
    exit 1
}
rg 'PASS: PostgreSQL|PASS: backup/restore' "$log_dir/task-9-storage.log"
docker compose up -d --wait api
assert_http 200 GET "/api/v1/matches/$account_match?scope=account" "$first_jar"
printf 'PASS: API reads account match after PostgreSQL container restart\n'

# The API's projection worker uses the same Redis keys as the isolated test host.
# Stop it before tests intentionally delete those keys to verify rebuild behavior.
docker compose stop api
bash scripts/test-cache.sh verify > "$log_dir/task-9-full-tests.log" 2>&1 || {
    tail -n 120 "$log_dir/task-9-full-tests.log" >&2
    exit 1
}
rg 'Test Run Successful|Total tests:|PASS: Phase 2' "$log_dir/task-9-full-tests.log"
printf 'PASS: Phase 2 acceptance on isolated Compose stack\n'
