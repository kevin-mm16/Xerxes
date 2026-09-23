#!/usr/bin/env bash
set -euo pipefail

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y nginx mysql-server curl ca-certificates tar gzip jq unzip
systemctl enable nginx mysql
systemctl start nginx mysql

echo BASE_READY
nginx -v
mysql --version
curl --version | head -1
