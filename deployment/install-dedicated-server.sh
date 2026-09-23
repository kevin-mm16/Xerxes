#!/usr/bin/env bash
set -euo pipefail

release=/tmp/server-221-release.tar.gz
staging=/tmp/milife-device-intake-release
app_dir=/opt/milife-device-intake
config_dir=/etc/milife-device-intake

[[ $EUID -eq 0 ]] || { echo 'Run as root.' >&2; exit 1; }
[[ -f $release ]] || { echo "Missing $release" >&2; exit 1; }

rm -rf -- "$staging"
mkdir -p "$staging"
tar -xzf "$release" -C "$staging"
for required in app/MiLife.DeviceIntake.Api app/downloads/MiLifeDeviceAgent.exe \
  device-intake.env bootstrap.sql milife-device-intake-self-contained.service \
  milife-dedicated.nginx.conf milife-forwarding.nginx.conf; do
  [[ -f "$staging/$required" ]] || { echo "Release is missing $required" >&2; exit 1; }
done

if ! id milife-intake >/dev/null 2>&1; then
  useradd --system --home /nonexistent --shell /usr/sbin/nologin milife-intake
fi

systemctl stop milife-device-intake.service 2>/dev/null || true
install -d -o root -g root -m 0755 "$app_dir"
find "$app_dir" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
cp -a "$staging/app/." "$app_dir/"
chown -R root:root "$app_dir"
find "$app_dir" -type d -exec chmod 0755 {} +
find "$app_dir" -type f -exec chmod 0644 {} +
chmod 0755 "$app_dir/MiLife.DeviceIntake.Api"

install -d -o root -g milife-intake -m 0750 "$config_dir"
install -o root -g milife-intake -m 0640 "$staging/device-intake.env" \
  "$config_dir/device-intake.env"

mysql < "$staging/bootstrap.sql"

install -o root -g root -m 0644 "$staging/milife-device-intake-self-contained.service" \
  /etc/systemd/system/milife-device-intake.service
install -o root -g root -m 0644 "$staging/milife-forwarding.nginx.conf" \
  /etc/nginx/snippets/milife-forwarding.conf
install -o root -g root -m 0644 "$staging/milife-dedicated.nginx.conf" \
  /etc/nginx/sites-available/milife-device-intake
ln -sfn /etc/nginx/sites-available/milife-device-intake \
  /etc/nginx/sites-enabled/milife-device-intake
rm -f /etc/nginx/sites-enabled/default

install -o root -g root -m 0755 "$staging/backup-device-intake.sh" \
  /usr/local/sbin/backup-milife-device-intake
install -o root -g root -m 0644 "$staging/milife-device-intake-backup.service" \
  /etc/systemd/system/milife-device-intake-backup.service
install -o root -g root -m 0644 "$staging/milife-device-intake-backup.timer" \
  /etc/systemd/system/milife-device-intake-backup.timer
install -d -o root -g root -m 0700 /var/backups/milife-device-intake

nginx -t
systemctl daemon-reload
systemctl enable --now nginx milife-device-intake.service \
  milife-device-intake-backup.timer
systemctl reload nginx

for attempt in {1..30}; do
  if curl --fail --silent --show-error http://127.0.0.1:5088/health >/dev/null; then
    break
  fi
  if [[ $attempt -eq 30 ]]; then
    journalctl -u milife-device-intake.service -n 100 --no-pager >&2
    exit 1
  fi
  sleep 1
done

mysql <<'SQL'
REVOKE ALL PRIVILEGES, GRANT OPTION FROM 'milife_intake'@'localhost';
GRANT SELECT, INSERT, UPDATE, DELETE ON milife_device_intake.* TO 'milife_intake'@'localhost';
FLUSH PRIVILEGES;
SQL

systemctl restart milife-device-intake.service
for attempt in {1..30}; do
  if curl --fail --silent --show-error http://127.0.0.1:5088/health >/dev/null; then
    break
  fi
  if [[ $attempt -eq 30 ]]; then
    journalctl -u milife-device-intake.service -n 100 --no-pager >&2
    exit 1
  fi
  sleep 1
done

systemctl start milife-device-intake-backup.service
rm -f -- "$release"
rm -rf -- "$staging"

echo DEPLOYMENT_READY
systemctl --no-pager --full status milife-device-intake.service | sed -n '1,12p'
systemctl --no-pager --full status milife-device-intake-backup.timer | sed -n '1,10p'
curl --fail --silent --show-error http://127.0.0.1:5088/health
