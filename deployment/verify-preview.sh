#!/usr/bin/env bash
set -euo pipefail

expected_production=584191dee021923da0253c1412d78c319a051b674e66c83cfc84af2f4f3e1ee5
expected_preview=fd253106a462c5c12834cf4fe64888c19371ece4e7352e215e1ea3f5ec4159f5

systemctl is-active --quiet milife-device-intake.service nginx.service milife-ngrok.service
nginx -t >/dev/null
curl --fail --silent --show-error http://127.0.0.1:5088/health | grep -q '"healthy"'
gateway=(--header 'Host: uphill-cofounder-trident.ngrok-free.dev' --header 'X-Forwarded-Proto: https' --header 'X-Forwarded-For: 127.0.0.1')
curl --fail --silent --show-error "${gateway[@]}" http://127.0.0.1:5089/device-admin | grep -q 'Device register'
curl --fail --silent --show-error "${gateway[@]}" http://127.0.0.1:5089/device-admin/preview | grep -q 'DEVICE OPERATIONS PREVIEW'
curl --fail --silent --show-error "${gateway[@]}" http://127.0.0.1:5089/device-registration-preview | grep -q 'Download preview'
curl --fail --silent --show-error "${gateway[@]}" http://127.0.0.1:5089/device-admin-preview/support.js | grep -q 'Silent support ready'

production_hash="$(sha256sum /opt/milife-device-intake/downloads/MiLifeDeviceAgent.exe | cut -d' ' -f1)"
preview_hash="$(sha256sum /opt/milife-device-intake/downloads/MiLifeDeviceAgentPreview.exe | cut -d' ' -f1)"
[[ "$production_hash" == "$expected_production" ]]
[[ "$preview_hash" == "$expected_preview" ]]

columns="$(mysql -N -e "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='milife_device_intake' AND ((TABLE_NAME='Agents' AND COLUMN_NAME IN ('ConsentVersion','ConsentAcceptedAtUtc')) OR (TABLE_NAME='SupportJobs' AND COLUMN_NAME IN ('CommandType','DisplayName','RunSilently')));")"
[[ "$columns" == 5 ]]
grants="$(mysql -N -e "SHOW GRANTS FOR 'milife_intake'@'localhost';")"
grep -q 'SELECT, INSERT, UPDATE, DELETE' <<<"$grants"
if grep -Eq 'ALTER|CREATE|DROP|INDEX|REFERENCES' <<<"$grants"; then
  echo 'Runtime database account still has migration privileges.' >&2
  exit 1
fi
synthetic="$(mysql -N -e "SELECT (SELECT COUNT(*) FROM milife_device_intake.Agents WHERE EmployeeName IN ('IT Acceptance Test','Preview Support Test')) + (SELECT COUNT(*) FROM milife_device_intake.Devices WHERE SerialNumber='PREVIEW-COMMAND-TEST');")"
[[ "$synthetic" == 0 ]]

echo "PREVIEW_VERIFIED"
echo "Production agent: $production_hash"
echo "Preview agent: $preview_hash"
