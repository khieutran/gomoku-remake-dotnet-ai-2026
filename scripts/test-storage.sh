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
    docker compose exec -T postgres psql -U gomoku -d postgres -v ON_ERROR_STOP=1 \
        -c "CREATE DATABASE \"$test_database\""
    cleanup() {
        docker compose exec -T postgres psql -U gomoku -d postgres \
            -c "DROP DATABASE IF EXISTS \"$test_database\" WITH (FORCE)" >/dev/null 2>&1 || true
    }
    trap cleanup EXIT
    export GOMOKU_TEST_POSTGRES="Host=127.0.0.1;Port=$postgres_port;Database=$test_database;Username=gomoku;Password=gomoku_dev"
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
fi
