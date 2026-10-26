#!/usr/bin/env bash
set -euo pipefail

# Run only after the Phase 3 code review and approval to execute the green gate.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
mode="${1:-}"
if [[ "$mode" != preflight && "$mode" != verify ]]; then
    printf 'Usage: bash scripts/test-phase3.sh preflight|verify\n' >&2
    exit 2
fi

for file in \
    scripts/verify-phase2.sh \
    scripts/test-training.sh \
    scripts/test-model-evaluation.sh \
    scripts/test-self-play.sh \
    scripts/test-phase3-pipeline.sh \
    docs/Detail-phase-3/fixtures/ai-baseline-positions.json \
    tests/Gomoku.SelfPlay.Tests/Gomoku.SelfPlay.Tests.csproj \
    tests/Gomoku.AI.Tests/Gomoku.AI.Tests.csproj \
    tests/Gomoku.Infrastructure.Tests/Gomoku.Infrastructure.Tests.csproj \
    training/tests/test_training.py; do
    if [[ ! -f "$file" ]]; then
        printf 'FAIL: required Phase 3 gate input missing: %s\n' "$file" >&2
        exit 2
    fi
done

for tool in docker curl python3; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        printf 'FAIL: missing command: %s\n' "$tool" >&2
        exit 127
    fi
done

dotnet_bin="${GOMOKU_DOTNET_BIN:-$(command -v dotnet || true)}"
if [[ -z "$dotnet_bin" && -x /usr/local/share/dotnet/dotnet ]]; then
    dotnet_bin=/usr/local/share/dotnet/dotnet
fi
if [[ -z "$dotnet_bin" && -x /private/tmp/gomoku-dotnet10/dotnet ]]; then
    dotnet_bin=/private/tmp/gomoku-dotnet10/dotnet
fi
if [[ -z "$dotnet_bin" || ! -x "$dotnet_bin" ]]; then
    printf 'FAIL: .NET SDK missing; set GOMOKU_DOTNET_BIN or add dotnet to PATH\n' >&2
    exit 127
fi
export GOMOKU_DOTNET_BIN="$dotnet_bin"

if ! docker info >/dev/null 2>&1; then
    printf 'FAIL: Docker daemon is unavailable; start it before the approved verify run\n' >&2
    exit 127
fi

# Phase 2 preflight checks local PostgreSQL, Compose, and Docker/Colima availability.
bash scripts/verify-phase2.sh preflight
docker compose config --quiet
if [[ "$mode" == preflight ]]; then
    printf 'PASS: Phase 3 gate inputs and local dependencies are present\n'
    printf 'Verify will create and remove an isolated Compose stack; Docker daemon stays running.\n'
    exit 0
fi

log_dir="$repo_root/docs/Detail-phase-3/test-results"
mkdir -p -m 700 "$log_dir"
chmod 700 "$log_dir"
run_id="$(date -u +%Y%m%dT%H%M%SZ)-$$"
summary="$log_dir/phase3-$run_id-summary.log"
printf 'Phase 3 acceptance run: %s\n' "$run_id" > "$summary"
printf 'Start UTC: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" >> "$summary"

run_gate() {
    local name="$1"
    shift
    local log="$log_dir/phase3-$run_id-$name.log"
    printf 'RUN %s\n' "$name" | tee -a "$summary"
    if "$@" > "$log" 2>&1; then
        printf 'PASS %s | %s\n' "$name" "$log" | tee -a "$summary"
    else
        local status=$?
        printf 'FAIL %s (exit %s) | %s\n' "$name" "$status" "$log" | tee -a "$summary" >&2
        tail -n 100 "$log" >&2
        exit "$status"
    fi
}

# The Compose gate also runs the full solution, API/PostgreSQL integration,
# retry/concurrency, Redis outage, backup/restore, and API smoke checks.
run_gate compose-and-dotnet bash scripts/verify-phase2.sh verify

# Self-play has a focused gate in addition to the full solution check.
run_gate self-play bash scripts/test-self-play.sh

# Task 5 trains and checks a fixture candidate/ONNX contract in Linux/amd64.
run_gate training bash scripts/test-training.sh

# A real fixture-trained ONNX crosses the Python → .NET adapter/evaluator boundary.
run_gate trainer-to-evaluator bash scripts/test-phase3-pipeline.sh "$run_id"

# Task 7 evaluates a private fixture candidate; it must never promote it.
run_gate model-evaluation bash scripts/test-model-evaluation.sh

printf 'End UTC: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" >> "$summary"
printf 'PASS: Phase 3 acceptance gates. Summary: %s\n' "$summary"
