#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
mode="${1:-}"
if [[ "$mode" != preflight && "$mode" != verify ]]; then
    printf 'Cách dùng: bash scripts/test-cache.sh preflight|verify\n' >&2
    exit 2
fi

bash scripts/test-identity.sh preflight
if ! command -v docker >/dev/null 2>&1; then
    printf 'FAIL: cần Docker CLI để chạy Redis thật\n' >&2
    exit 127
fi
if [[ "$mode" == preflight ]]; then
    if docker info >/dev/null 2>&1; then
        printf 'Docker daemon đang chạy.\n'
    elif command -v colima >/dev/null 2>&1; then
        printf 'Docker daemon chưa chạy; verify sẽ bật Colima tạm thời.\n'
    else
        printf 'FAIL: Docker daemon chưa chạy và không có Colima\n' >&2
        exit 127
    fi
    exit 0
fi

started_colima=0
started_redis=0
cleanup() {
    if ((started_redis)); then
        docker compose stop redis >/dev/null 2>&1 || true
    fi
    if ((started_colima)); then
        colima stop >/dev/null 2>&1 || true
    fi
}
trap cleanup EXIT

if ! docker info >/dev/null 2>&1; then
    colima start
    started_colima=1
    for attempt in {1..60}; do
        if docker info >/dev/null 2>&1; then
            break
        fi
        sleep 2
    done
    if ! docker info >/dev/null 2>&1; then
        printf 'FAIL: Docker daemon chưa sẵn sàng sau khi bật Colima\n' >&2
        exit 1
    fi
fi
if [[ -z "$(docker compose ps -q redis)" ]]; then
    started_redis=1
fi
docker compose up -d --wait redis

redis_port="${GOMOKU_REDIS_PORT:-56379}"
export GOMOKU_TEST_REDIS="127.0.0.1:$redis_port,abortConnect=false,connectTimeout=500,asyncTimeout=500"
bash scripts/test-identity.sh verify
