#!/bin/sh
set -eu

if [ "$#" -ne 2 ]; then
    echo "Usage: $0 <staged-agent> <expected-sha256>" >&2
    exit 2
fi

source_agent=$1
expected_hash=$2
installed_agent=/opt/milife-device-intake/downloads/MiLifeDeviceAgent.exe
backup_directory=/var/backups/milife-agent

test -f "$source_agent"
test -f "$installed_agent"
actual_hash=$(sha256sum "$source_agent" | cut -d' ' -f1)
test "$actual_hash" = "$expected_hash"

stamp=$(date -u +%Y%m%dT%H%M%SZ)
install -d -m 0700 "$backup_directory"
cp "$installed_agent" "$backup_directory/MiLifeDeviceAgent-$stamp.exe"
install -o milife-intake -g milife-intake -m 0644 "$source_agent" "$installed_agent"

sha256sum "$installed_agent"
stat -c %s "$installed_agent"
systemctl is-active milife-device-intake.service
