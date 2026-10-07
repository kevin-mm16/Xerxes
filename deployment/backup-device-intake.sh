#!/usr/bin/env bash
set -euo pipefail

umask 077
backup_dir=/var/backups/milife-device-intake
mkdir -p "$backup_dir"
stamp=$(date -u +%Y%m%dT%H%M%SZ)
target="$backup_dir/milife_device_intake-$stamp.sql.gz"
partial="$target.partial"

trap 'rm -f "$partial"' EXIT
mysqldump --single-transaction --routines --triggers milife_device_intake | gzip -9 > "$partial"
test -s "$partial"
mv "$partial" "$target"
marker=$(mktemp)
trap 'rm -f "$partial" "$marker"' EXIT
date -u +%FT%TZ > "$marker"
install -o root -g milife-intake -m 0640 "$marker" /var/lib/milife-device-intake/last-backup.utc
rm -f "$marker"
find "$backup_dir" -maxdepth 1 -type f -name 'milife_device_intake-*.sql.gz' -mtime +14 -delete
trap - EXIT
