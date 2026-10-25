#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_bin="${GOMOKU_DOTNET_BIN:-}"
if [[ -z "$dotnet_bin" ]]; then
    if [[ -x /private/tmp/gomoku-dotnet10/dotnet ]]; then
        dotnet_bin=/private/tmp/gomoku-dotnet10/dotnet
    else
        dotnet_bin="$(command -v dotnet || true)"
    fi
fi
if [[ -z "$dotnet_bin" || ! -x "$dotnet_bin" ]]; then
    printf 'Không tìm thấy .NET SDK. Cài dotnet vào PATH hoặc đặt GOMOKU_DOTNET_BIN.\n' >&2
    exit 127
fi

export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-${TMPDIR:-/tmp}/gomoku-dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-${TMPDIR:-/tmp}/gomoku-nuget-packages}"
export MSBUILDDISABLENODEREUSE=1
cd "$repo_root"

compose_mode=0
if [[ -z "${GOMOKU_TEST_POSTGRES:-}" ]]; then
    compose_mode=1
    docker compose up -d --wait postgres
    postgres_port="${GOMOKU_POSTGRES_PORT:-55432}"
    test_database="gomoku_test_$(date +%s)_$$"
    restore_database="${test_database}_restored"
    backup_file="$(mktemp "${TMPDIR:-/tmp}/gomoku-backup.XXXXXXXX")"
    docker compose exec -T postgres psql -U gomoku -d postgres -v ON_ERROR_STOP=1 \
        -c "CREATE DATABASE \"$test_database\""
    cleanup() {
        docker compose exec -T postgres psql -U gomoku -d postgres \
            -c "DROP DATABASE IF EXISTS \"$restore_database\" WITH (FORCE)" >/dev/null 2>&1 || true
        docker compose exec -T postgres psql -U gomoku -d postgres \
            -c "DROP DATABASE IF EXISTS \"$test_database\" WITH (FORCE)" >/dev/null 2>&1 || true
        rm -f -- "$backup_file"
    }
    trap cleanup EXIT
    export GOMOKU_TEST_POSTGRES="Host=127.0.0.1;Port=$postgres_port;Database=$test_database;Username=gomoku;Password=${GOMOKU_POSTGRES_PASSWORD:-gomoku_dev}"
fi
export GOMOKU_POSTGRES_CONNECTION="$GOMOKU_TEST_POSTGRES"

"$dotnet_bin" restore tests/Gomoku.Infrastructure.Tests/Gomoku.Infrastructure.Tests.csproj --nologo
"$dotnet_bin" tool restore
"$dotnet_bin" build backend/Gomoku.Infrastructure/Gomoku.Infrastructure.csproj --no-restore --nologo -m:1 -p:UseSharedCompilation=false
"$dotnet_bin" tool run dotnet-ef -- database update \
    --project backend/Gomoku.Infrastructure/Gomoku.Infrastructure.csproj \
    --startup-project backend/Gomoku.Infrastructure/Gomoku.Infrastructure.csproj \
    --context GomokuDbContext --no-build
"$dotnet_bin" test tests/Gomoku.Infrastructure.Tests/Gomoku.Infrastructure.Tests.csproj \
    --no-restore --nologo -m:1 -p:UseSharedCompilation=false --verbosity quiet

if ((compose_mode)); then
    before="$(docker compose exec -T postgres psql -U gomoku -d "$test_database" -Atc 'SELECT COUNT(*) FROM "Moves"')"
    if [[ ! "$before" =~ ^[1-9][0-9]*$ ]]; then
        printf 'FAIL: không có Move mẫu trước khi restart PostgreSQL\n' >&2
        exit 1
    fi
    docker compose restart postgres
    docker compose up -d --wait postgres
    after="$(docker compose exec -T postgres psql -U gomoku -d "$test_database" -Atc 'SELECT COUNT(*) FROM "Moves"')"
    if [[ "$before" != "$after" ]]; then
        printf 'FAIL: số Move thay đổi sau restart (%s → %s)\n' "$before" "$after" >&2
        exit 1
    fi
    printf 'PASS: PostgreSQL giữ %s Move sau container restart\n' "$after"

    counts_sql="SELECT (SELECT COUNT(*) FROM \"AspNetUsers\") || ',' || (SELECT COUNT(*) FROM \"GuestSessions\") || ',' || (SELECT COUNT(*) FROM \"Games\")"
    counts="$(docker compose exec -T postgres psql -U gomoku -d "$test_database" -Atc "$counts_sql")"
    IFS=, read -r account_count guest_count game_count <<< "$counts"
    if ((account_count < 1 || guest_count < 1 || game_count < 1)); then
        printf 'FAIL: backup sample must contain account, guest and Game rows\n' >&2
        exit 1
    fi

    fingerprint_sql="$(cat <<'SQL'
SELECT md5(concat_ws('|',
  COALESCE((SELECT string_agg("Id"::text, ',' ORDER BY "Id") FROM "AspNetUsers"), ''),
  COALESCE((SELECT string_agg("Id"::text, ',' ORDER BY "Id") FROM "GuestSessions"), ''),
  COALESCE((SELECT string_agg("Id"::text || ':' || COALESCE("UserId"::text, '') || ':' || COALESCE("GuestSessionId"::text, ''), ',' ORDER BY "Id") FROM "Matches"), ''),
  COALESCE((SELECT string_agg("Id"::text || ':' || "MatchId"::text, ',' ORDER BY "Id") FROM "Games"), ''),
  COALESCE((SELECT string_agg("Id"::text || ':' || "GameId"::text || ':' || "Step"::text || ':' || "Row"::text || ':' || "Column"::text, ',' ORDER BY "Id") FROM "Moves"), '')))
SQL
)"
    original="$(docker compose exec -T postgres psql -U gomoku -d "$test_database" -Atc "$fingerprint_sql")"
    docker compose exec -T postgres pg_dump -U gomoku -d "$test_database" -Fc > "$backup_file"
    docker compose exec -T postgres psql -U gomoku -d postgres -v ON_ERROR_STOP=1 \
        -c "CREATE DATABASE \"$restore_database\""
    docker compose exec -T postgres pg_restore -U gomoku -d "$restore_database" \
        --no-owner --no-acl < "$backup_file"
    restored="$(docker compose exec -T postgres psql -U gomoku -d "$restore_database" -Atc "$fingerprint_sql")"
    if [[ "$original" != "$restored" ]]; then
        printf 'FAIL: backup/restore thay đổi account, guest, owner Match, Game hoặc Move\n' >&2
        exit 1
    fi
    printf 'PASS: backup/restore giữ account, guest, owner Match, Game và Move\n'
fi
