#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mode="${1:-}"
if [[ "$mode" != preflight && "$mode" != verify ]]; then
    printf 'Cách dùng: bash scripts/test-identity.sh preflight|verify\n' >&2
    exit 2
fi

dotnet_bin="${GOMOKU_DOTNET_BIN:-$(command -v dotnet || true)}"
if [[ -z "$dotnet_bin" && -x /usr/local/share/dotnet/dotnet ]]; then
    dotnet_bin=/usr/local/share/dotnet/dotnet
fi
if [[ -z "$dotnet_bin" && -x /private/tmp/gomoku-dotnet10/dotnet ]]; then
    dotnet_bin=/private/tmp/gomoku-dotnet10/dotnet
fi
if [[ -z "$dotnet_bin" || ! -x "$dotnet_bin" ]]; then
    printf 'FAIL: không tìm thấy .NET SDK qua PATH hoặc GOMOKU_DOTNET_BIN\n' >&2
    exit 127
fi

for tool in initdb pg_ctl createdb pg_isready; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        printf 'FAIL: không tìm thấy %s; cần PostgreSQL local để chạy test cô lập\n' "$tool" >&2
        exit 127
    fi
done

if [[ "$mode" == preflight ]]; then
    printf 'SDK: %s (%s)\n' "$dotnet_bin" "$("$dotnet_bin" --version)"
    printf 'PostgreSQL: %s\n' "$(pg_ctl --version)"
    if pg_isready -h 127.0.0.1 -p 55432 >/dev/null 2>&1; then
        printf 'Cổng 55432 đang có PostgreSQL; verify sẽ tạo cluster riêng, không dùng cổng này.\n'
        account="$(id -un)"
        existing="$(psql -h 127.0.0.1 -p 55432 -U "$account" -d postgres -Atc \
            "SELECT datname FROM pg_database WHERE datname = 'gomoku_task3'" 2>/dev/null || true)"
        if [[ "$existing" == gomoku_task3 ]]; then
            printf 'Database gomoku_task3 đã tồn tại ở cổng 55432; verify sẽ không chạm tới nó.\n'
        fi
    else
        printf 'Cổng 55432 chưa có PostgreSQL phản hồi.\n'
    fi
    if [[ -f "$repo_root/tests/Gomoku.Api.Tests/Gomoku.Api.Tests.csproj" ]]; then
        printf 'Project HTTP integration tests đã có.\n'
    else
        printf 'Project HTTP integration tests chưa có; verify chỉ chạy sau khi code Task 3 hoàn tất.\n'
    fi
    exit 0
fi

if [[ ! -f "$repo_root/tests/Gomoku.Api.Tests/Gomoku.Api.Tests.csproj" ]]; then
    printf 'FAIL: chưa có HTTP integration tests của Task 3\n' >&2
    exit 2
fi

task_dir="$(mktemp -d "${TMPDIR:-/private/tmp}/gomoku-task3.XXXXXXXX")"
postgres_started=0
cleanup() {
    if ((postgres_started)); then
        pg_ctl -D "$task_dir/data" -m immediate stop >/dev/null 2>&1 || true
    fi
    if [[ "$task_dir" == */gomoku-task3.* && -d "$task_dir" ]]; then
        rm -rf -- "$task_dir"
    fi
}
trap cleanup EXIT

mkdir -p "$task_dir/socket" "$task_dir/keys" "$repo_root/docs/Detail-phase-2/test-results"
log_file="$repo_root/docs/Detail-phase-2/test-results/identity-tests.log"
exec > >(tee "$log_file") 2>&1

export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-${TMPDIR:-/private/tmp}/gomoku-dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-${TMPDIR:-/private/tmp}/gomoku-nuget-packages}"
export MSBUILDDISABLENODEREUSE=1
export GOMOKU_DP_KEYS_DIR="$task_dir/keys"

account="$(id -un)"
port=$((30000 + RANDOM % 20000))
LANG=C LC_ALL=C initdb -D "$task_dir/data" -A trust --no-instructions
pg_ctl -D "$task_dir/data" -l "$task_dir/postgres.log" \
    -o "-h 127.0.0.1 -p $port -k $task_dir/socket" start
postgres_started=1
createdb -h 127.0.0.1 -p "$port" -U "$account" gomoku_task3

export GOMOKU_TEST_POSTGRES="Host=127.0.0.1;Port=$port;Database=gomoku_task3;Username=$account"
export GOMOKU_POSTGRES_CONNECTION="$GOMOKU_TEST_POSTGRES"
export ConnectionStrings__Gomoku="$GOMOKU_TEST_POSTGRES"

cd "$repo_root"
"$dotnet_bin" restore Gomoku.sln --nologo
"$dotnet_bin" tool restore
"$dotnet_bin" build backend/Gomoku.Infrastructure/Gomoku.Infrastructure.csproj \
    --no-restore --nologo -m:1 -p:UseSharedCompilation=false
"$dotnet_bin" tool run dotnet-ef -- database update \
    --project backend/Gomoku.Infrastructure/Gomoku.Infrastructure.csproj \
    --startup-project backend/Gomoku.Infrastructure/Gomoku.Infrastructure.csproj \
    --context GomokuDbContext --no-build
detail_log="$repo_root/docs/Detail-phase-2/test-results/identity-tests-detail.log"
if ! "$dotnet_bin" test Gomoku.sln --no-restore --nologo -m:1 -p:UseSharedCompilation=false \
    --verbosity quiet --logger 'console;verbosity=detailed' > "$detail_log" 2>&1; then
    tail -n 120 "$detail_log"
    exit 1
fi
rg 'Test Run Successful|Total tests:' "$detail_log" || true
(unset ConnectionStrings__Gomoku GOMOKU_POSTGRES_CONNECTION; bash scripts/test-api-smoke.sh)
"$dotnet_bin" format Gomoku.sln --verify-no-changes --no-restore \
    --include backend/Gomoku.Api backend/Gomoku.Application/Datasets backend/Gomoku.Application/History backend/Gomoku.Application/Identity \
    backend/Gomoku.Application/Matches backend/Gomoku.Infrastructure/Identity \
    backend/Gomoku.Infrastructure/Datasets backend/Gomoku.Infrastructure/History backend/Gomoku.Infrastructure/Matches tests/Gomoku.Api.Tests \
    tests/Gomoku.Application.Tests tests/Gomoku.Infrastructure.Tests/Datasets tests/Gomoku.Infrastructure.Tests/History \
    tests/Gomoku.Infrastructure.Tests/Identity tests/Gomoku.Infrastructure.Tests/Matches \
    tools/Gomoku.DatasetExport \
    --verbosity quiet
printf 'PASS: Phase 2 integration, Core và API smoke tests\n'
