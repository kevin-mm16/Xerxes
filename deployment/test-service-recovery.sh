#!/usr/bin/env bash
set -euo pipefail

[[ $EUID -eq 0 ]] || { echo 'Run as root.' >&2; exit 1; }
service=milife-device-intake.service
before=$(systemctl show "$service" --property=NRestarts --value)
pid=$(systemctl show "$service" --property=MainPID --value)
[[ $pid =~ ^[1-9][0-9]*$ ]] || { echo 'Service has no running process.' >&2; exit 1; }

kill -KILL "$pid"
for attempt in {1..30}; do
  after=$(systemctl show "$service" --property=NRestarts --value)
  if [[ $after -gt $before ]] && curl --fail --silent --show-error \
    http://127.0.0.1:5088/health >/dev/null; then
    echo "PASS: systemd restarted the service after a forced process failure ($before -> $after)."
    exit 0
  fi
  sleep 1
done

journalctl -u "$service" -n 100 --no-pager >&2
exit 1
