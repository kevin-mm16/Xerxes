#!/usr/bin/env bash
set -euo pipefail

echo SERVICES
systemctl is-active milife-device-intake nginx 2>/dev/null || true
systemctl list-unit-files --type=service | grep -E 'ngrok|milife' || true

echo UNITS
systemctl cat milife-device-intake 2>/dev/null || true
systemctl cat ngrok 2>/dev/null || true

echo NGROK_PATHS
find /etc /root/.config /home/kevin-x/.config -maxdepth 4 \
  \( -iname '*ngrok*' -o -iname 'ngrok.yml' \) -print 2>/dev/null || true

echo ENV_KEYS
if [[ -f /etc/milife-device-intake/device-intake.env ]]; then
  sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' \
    /etc/milife-device-intake/device-intake.env
fi

echo NGINX_FILES
find /etc/nginx -maxdepth 3 -type f -print 2>/dev/null | sort

echo VERSIONS
nginx -v 2>&1 || true
mysql --version || true
dotnet --info 2>/dev/null | head -30 || true
ngrok version 2>/dev/null || true

echo APP
du -sh /opt/milife-device-intake /etc/milife-device-intake 2>/dev/null || true
sha256sum /opt/milife-device-intake/wwwroot/downloads/MiLifeDeviceAgent.exe \
  2>/dev/null || true

echo DB_TABLES
mysql -N -e 'SHOW TABLES FROM milife_device_intake;' 2>/dev/null || true

echo DB_COUNTS
mysql -N -e 'SELECT COUNT(*) FROM milife_device_intake.Devices;
SELECT COUNT(*) FROM milife_device_intake.HeartbeatAgents;
SELECT COUNT(*) FROM milife_device_intake.AgentActions;' 2>/dev/null || true
