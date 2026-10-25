#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fixture_dir="$(mktemp -d "${TMPDIR:-/private/tmp}/gomoku-ranking-index.XXXXXXXX")"
port=$((30000 + RANDOM % 20000))
started=0
cleanup() {
    if ((started)); then
        pg_ctl -D "$fixture_dir/data" -m immediate stop >/dev/null 2>&1 || true
    fi
    rm -rf -- "$fixture_dir"
}
trap cleanup EXIT

LANG=C LC_ALL=C initdb -D "$fixture_dir/data" -A trust --no-instructions >/dev/null
pg_ctl -D "$fixture_dir/data" -l "$fixture_dir/postgres.log" \
    -o "-h 127.0.0.1 -p $port" start >/dev/null
started=1

log="$repo_root/docs/Detail-phase-2/test-results/ranking-index-benchmark.log"
mkdir -p "$(dirname "$log")"
{
    printf 'Synthetic fixture: 100000 users, half with RankingGames > 0. PostgreSQL: %s\n' "$(pg_ctl --version)"
    psql -h 127.0.0.1 -p "$port" -d postgres -X -v ON_ERROR_STOP=1 -q <<'SQL'
CREATE TABLE "AspNetUsers" ("Id" uuid PRIMARY KEY, "RankingPoints" integer NOT NULL, "RankingGames" integer NOT NULL);
INSERT INTO "AspNetUsers"
SELECT md5(number::text)::uuid, number % 1000, number % 2
FROM generate_series(1, 100000) AS number;
ANALYZE "AspNetUsers";
\echo 'Before index: top 50'
EXPLAIN (ANALYZE, BUFFERS)
SELECT "Id", "RankingPoints" FROM "AspNetUsers"
WHERE "RankingGames" > 0 ORDER BY "RankingPoints" DESC, "Id" DESC LIMIT 50;
\echo 'Before index: rank count'
EXPLAIN (ANALYZE, BUFFERS)
SELECT count(*) FROM "AspNetUsers" WHERE "RankingGames" > 0 AND "RankingPoints" > 500;
CREATE INDEX "IX_AspNetUsers_RankingPoints_Id" ON "AspNetUsers" ("RankingPoints" DESC, "Id" DESC)
WHERE "RankingGames" > 0;
ANALYZE "AspNetUsers";
\echo 'After index: top 50'
EXPLAIN (ANALYZE, BUFFERS)
SELECT "Id", "RankingPoints" FROM "AspNetUsers"
WHERE "RankingGames" > 0 ORDER BY "RankingPoints" DESC, "Id" DESC LIMIT 50;
\echo 'After index: rank count'
EXPLAIN (ANALYZE, BUFFERS)
SELECT count(*) FROM "AspNetUsers" WHERE "RankingGames" > 0 AND "RankingPoints" > 500;
SQL
} | tee "$log"
