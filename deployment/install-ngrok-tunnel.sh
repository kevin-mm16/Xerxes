#!/usr/bin/env bash
set -euo pipefail

token_file=/tmp/milife-ngrok.env
archive=/tmp/ngrok-v3-stable-linux-amd64.tgz
extract_dir=/tmp/milife-ngrok-install

[[ $EUID -eq 0 ]] || { echo 'Run as root.' >&2; exit 1; }
[[ -f $token_file ]] || { echo "Missing $token_file" >&2; exit 1; }
grep -Eq '^NGROK_AUTHTOKEN=[A-Za-z0-9_-]{30,}$' "$token_file" || {
  echo 'The ngrok environment file is invalid.' >&2
  exit 1
}

curl --fail --silent --show-error --location \
  https://bin.ngrok.com/c/bNyj1mQVY4c/ngrok-v3-stable-linux-amd64.tgz \
  --output "$archive"
rm -rf -- "$extract_dir"
mkdir -p "$extract_dir"
tar -xzf "$archive" -C "$extract_dir"
[[ -f "$extract_dir/ngrok" ]] || { echo 'ngrok binary missing from archive.' >&2; exit 1; }
install -o root -g root -m 0755 "$extract_dir/ngrok" /usr/local/bin/ngrok
/usr/local/bin/ngrok version

if ! id milife-tunnel >/dev/null 2>&1; then
  useradd --system --home /var/lib/milife-ngrok --shell /usr/sbin/nologin milife-tunnel
fi
install -d -o root -g milife-tunnel -m 0750 /etc/milife-ngrok
install -o root -g milife-tunnel -m 0640 "$token_file" /etc/milife-ngrok/ngrok.env
install -o root -g root -m 0644 /tmp/milife-ngrok.service \
  /etc/systemd/system/milife-ngrok.service

systemctl daemon-reload
systemctl enable --now milife-ngrok.service
rm -f -- "$token_file" "$archive"
rm -rf -- "$extract_dir"

for attempt in {1..30}; do
  if curl --fail --silent --show-error \
    -H 'ngrok-skip-browser-warning: true' \
    https://uphill-cofounder-trident.ngrok-free.dev/collector/health >/dev/null; then
    echo TUNNEL_READY
    exit 0
  fi
  if [[ $attempt -eq 30 ]]; then
    journalctl -u milife-ngrok.service -n 100 --no-pager >&2
    exit 1
  fi
  sleep 2
done
