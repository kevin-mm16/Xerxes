#!/usr/bin/env bash
set -euo pipefail

[[ $EUID -eq 0 ]] || { echo 'Run as root.' >&2; exit 1; }

echo SERVICES
systemctl is-active ssh nginx mysql milife-device-intake.service milife-ngrok.service
systemctl is-enabled nginx mysql milife-device-intake.service \
  milife-device-intake-backup.timer milife-ngrok.service

echo HEALTH
curl --fail --silent --show-error http://127.0.0.1:5088/health
echo

echo DATABASE_COUNTS
mysql --batch --skip-column-names -e \
  'SELECT COUNT(*) FROM milife_device_intake.Devices;
   SELECT COUNT(*) FROM milife_device_intake.DeviceSubmissions;
   SELECT COUNT(*) FROM milife_device_intake.Agents;
   SELECT COUNT(*) FROM milife_device_intake.AgentActions;'

echo DATABASE_GRANTS
mysql --batch --skip-column-names -e \
  "SHOW GRANTS FOR 'milife_intake'@'localhost';"

echo FILE_PERMISSIONS
stat -c '%a %U:%G %n' \
  /etc/milife-device-intake \
  /etc/milife-device-intake/device-intake.env \
  /etc/milife-ngrok \
  /etc/milife-ngrok/ngrok.env \
  /opt/milife-device-intake/MiLife.DeviceIntake.Api \
  /opt/milife-device-intake/downloads/MiLifeDeviceAgent.exe

echo BACKUPS
find /var/backups/milife-device-intake -maxdepth 1 -type f \
  -name 'milife_device_intake-*.sql.gz' -printf '%f %s bytes\n'

echo LISTENERS
ss -lnt | grep -E ':(22|3306|5088|5089) '

echo AGENT_SHA256
sha256sum /opt/milife-device-intake/downloads/MiLifeDeviceAgent.exe
