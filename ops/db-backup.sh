#!/bin/sh
# PostgreSQL backups for the public VM, run by the `backup` service in compose.tunnel.yaml.
# Connection comes from the libpq variables PGHOST, PGUSER, PGPASSWORD and PGDATABASE.
#
#   sh ops/db-backup.sh            # back up now
#   sh ops/db-backup.sh --if-due   # back up only if the newest backup is older than BACKUP_EVERY_HOURS
#   sh ops/db-backup.sh --health   # exit 1 when the newest backup is missing or too old (container healthcheck)
#
# Writes /backups/<db>-<UTC time>.dump (pg_dump custom format, restore with pg_restore) and keeps the newest BACKUP_KEEP.
set -eu

dir=/backups
keep="${BACKUP_KEEP:-7}"
every_hours="${BACKUP_EVERY_HOURS:-24}"
db="${PGDATABASE:?set PGDATABASE}"

newest() { ls -1t "$dir"/"$db"-*.dump 2>/dev/null | head -n 1; }

age_hours() {
  file="$(newest)"
  [ -n "$file" ] || { echo 999999; return; }
  echo $(( ($(date +%s) - $(stat -c %Y "$file")) / 3600 ))
}

case "${1:-}" in
  --health)
    # A missed run is tolerated for two hours before the container turns unhealthy.
    [ "$(age_hours)" -lt $((every_hours + 2)) ]
    exit
    ;;
  --if-due)
    [ "$(age_hours)" -ge "$every_hours" ] || exit 0
    ;;
  "") ;;
  *) echo "usage: db-backup.sh [--if-due|--health]" >&2; exit 2 ;;
esac

mkdir -p "$dir"
target="$dir/$db-$(date -u +%Y%m%d-%H%M%S).dump"
# Write to a temporary name first so a failed dump never looks like a valid backup.
pg_dump -Fc --no-owner -f "$target.partial"
pg_restore --list "$target.partial" >/dev/null   # the archive must be readable
mv "$target.partial" "$target"
echo "backup: $target ($(du -h "$target" | cut -f1))"

ls -1t "$dir"/"$db"-*.dump | tail -n +$((keep + 1)) | while read -r old; do
  rm -f "$old"
  echo "backup: removed $old"
done
