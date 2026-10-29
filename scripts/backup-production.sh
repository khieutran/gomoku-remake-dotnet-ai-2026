#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 3 ]]; then
    printf 'Usage: bash scripts/backup-production.sh ENV_FILE BACKUP_DIRECTORY AGE_RECIPIENT\n' >&2
    exit 2
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$1"
backup_dir="$2"
age_recipient="$3"
project_name="${GOMOKU_COMPOSE_PROJECT:-gomoku-production}"

command -v age >/dev/null || { printf 'age is required for encrypted backups.\n' >&2; exit 127; }
command -v docker >/dev/null || { printf 'docker is required.\n' >&2; exit 127; }
python3 "$repo_root/scripts/production-preflight.py" "$env_file"

umask 077
mkdir -p "$backup_dir"
backup_dir="$(cd "$backup_dir" && pwd)"
temp_dir="$(mktemp -d "$backup_dir/.gomoku-backup.XXXXXXXX")"
trap 'rm -rf "$temp_dir"' EXIT

compose=(docker compose --env-file "$env_file" -f "$repo_root/compose.production.yaml" -p "$project_name")
"${compose[@]}" exec -T postgres pg_dump -U gomoku -d gomoku -Fc > "$temp_dir/gomoku.dump"
test -s "$temp_dir/gomoku.dump" || { printf 'Database dump is empty.\n' >&2; exit 1; }

mkdir "$temp_dir/keys"
"${compose[@]}" cp api:/app/keys/. "$temp_dir/keys/"
find "$temp_dir/keys" -name '*.xml' -print -quit | grep -q . || {
    printf 'Data Protection key ring is empty; backup is incomplete.\n' >&2
    exit 1
}

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
archive="$backup_dir/gomoku-$timestamp.tar.age"
if [[ -e "$archive" ]]; then
    printf 'Backup already exists: %s\n' "$archive" >&2
    exit 1
fi
tar -C "$temp_dir" -cf - gomoku.dump keys | age -r "$age_recipient" -o "$temp_dir/backup.age"
test -s "$temp_dir/backup.age" || { printf 'Encrypted backup is empty.\n' >&2; exit 1; }
mv "$temp_dir/backup.age" "$archive"
printf 'Encrypted backup: %s\nMove it to off-host storage and verify restore before release.\n' "$archive"
