#!/usr/bin/env bash
set -u

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_bin="${GOMOKU_DOTNET_BIN:-/private/tmp/gomoku-dotnet10/dotnet}"
result_dir="$repo_root/docs/Detail-phase-1/test-results"
log_file="$result_dir/core-tests.log"
status_file="$result_dir/core-tests.exit-code"

mkdir -p "$result_dir"

if [[ ! -x "$dotnet_bin" ]]; then
  printf 'Không tìm thấy .NET SDK tại %s\n' "$dotnet_bin" | tee "$log_file"
  printf '127\n' > "$status_file"
  exit 127
fi

cd "$repo_root" || exit 1
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/private/tmp/gomoku-dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/private/tmp/gomoku-nuget-online}"

"$dotnet_bin" test tests/Gomoku.Core.Tests/Gomoku.Core.Tests.csproj --no-restore --nologo > "$log_file" 2>&1
status=$?
printf '%s\n' "$status" > "$status_file"
cat "$log_file"
printf '\nMã thoát: %s\nLog: %s\n' "$status" "$log_file"
exit "$status"
