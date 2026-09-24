#!/usr/bin/env bash
set -euo pipefail

expected=fd253106a462c5c12834cf4fe64888c19371ece4e7352e215e1ea3f5ec4159f5
gateway=(--header 'Host: uphill-cofounder-trident.ngrok-free.dev' --header 'X-Forwarded-Proto: https' --header 'X-Forwarded-For: 127.0.0.1')

systemctl is-active --quiet milife-device-intake.service nginx.service milife-ngrok.service
nginx -t >/dev/null
curl --fail --silent --show-error http://127.0.0.1:5088/health | grep -q '"healthy"'
curl --fail --silent --show-error "${gateway[@]}" http://127.0.0.1:5089/device-admin | grep -q 'DEVICE OPERATIONS'
curl --fail --silent --show-error "${gateway[@]}" http://127.0.0.1:5089/device-registration | grep -q 'management notice'
react_html="$(curl --fail --silent --show-error "${gateway[@]}" http://127.0.0.1:5089/device-admin/react-preview)"
grep -q 'MiLife | Device operations' <<<"$react_html"
react_asset="$(grep -o '/device-admin-react-preview/assets/index-[^\"]*\.js' <<<"$react_html" | head -1)"
[[ -n "$react_asset" ]]
curl --fail --silent --show-error "${gateway[@]}" "http://127.0.0.1:5089${react_asset}" | grep -q 'Live updates connected'

production_hash="$(sha256sum /opt/milife-device-intake/downloads/MiLifeDeviceAgent.exe | cut -d' ' -f1)"
preview_hash="$(sha256sum /opt/milife-device-intake/downloads/MiLifeDeviceAgentPreview.exe | cut -d' ' -f1)"
[[ "$production_hash" == "$expected" && "$preview_hash" == "$expected" ]]

grants="$(mysql -N -e "SHOW GRANTS FOR 'milife_intake'@'localhost';")"
grep -q 'SELECT, INSERT, UPDATE, DELETE' <<<"$grants"
if grep -Eq 'ALTER|CREATE|DROP|INDEX|REFERENCES' <<<"$grants"; then
  echo 'Runtime database account has migration privileges.' >&2
  exit 1
fi

echo "PROMOTION_VERIFIED"
echo "Production agent: $production_hash"
echo "Preview agent: $preview_hash"
