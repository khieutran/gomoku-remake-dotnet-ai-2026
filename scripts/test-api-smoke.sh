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
result_dir="$repo_root/docs/Detail-phase-2/test-results"
log_file="$result_dir/api-smoke.log"
port=$((20000 + RANDOM % 20000))
url="http://127.0.0.1:$port/health"

mkdir -p "$result_dir"

if [[ -z "$dotnet_bin" || ! -x "$dotnet_bin" ]]; then
    printf 'Không tìm thấy .NET SDK. Cài dotnet vào PATH hoặc đặt GOMOKU_DOTNET_BIN.\n' >&2
    exit 127
fi

export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-${TMPDIR:-/tmp}/gomoku-dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-${TMPDIR:-/tmp}/gomoku-nuget-packages}"

cd "$repo_root"
if ! "$dotnet_bin" build backend/Gomoku.Api/Gomoku.Api.csproj --nologo > "$log_file" 2>&1; then
    cat "$log_file" >&2
    exit 1
fi
cd "$repo_root/backend/Gomoku.Api"
ASPNETCORE_ENVIRONMENT=Development GOMOKU_DP_KEYS_DIR="${TMPDIR:-/tmp}/gomoku-api-smoke-keys" \
    "$dotnet_bin" bin/Debug/net10.0/Gomoku.Api.dll \
    --urls "http://127.0.0.1:$port" >> "$log_file" 2>&1 &
server_pid=$!

cleanup() {
    kill "$server_pid" 2>/dev/null || true
    wait "$server_pid" 2>/dev/null || true
}
trap cleanup EXIT

for ((attempt = 0; attempt < 100; attempt++)); do
    if response="$(curl --silent --show-error --fail --max-time 1 "$url" 2>/dev/null)"; then
        if ! kill -0 "$server_pid" 2>/dev/null ||
            ! grep -Fq "Now listening on: http://127.0.0.1:$port" "$log_file"; then
            cat "$log_file" >&2
            printf 'FAIL %s → API process did not own the listening port\n' "$url" >&2
            exit 1
        fi

        if [[ "$response" == "Healthy" ]]; then
            printf 'PASS %s → %s\n' "$url" "$response"
            exit 0
        fi

        printf 'FAIL %s → unexpected response: %s\n' "$url" "$response" >&2
        exit 1
    fi

    if ! kill -0 "$server_pid" 2>/dev/null; then
        cat "$log_file" >&2
        exit 1
    fi

    sleep 0.2
done

cat "$log_file" >&2
printf 'FAIL %s → server did not become ready\n' "$url" >&2
exit 1
