#!/usr/bin/env bash
set -euo pipefail
repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
output="$repo/publish/api"
if [[ -L "$repo/publish" || -L "$output" ]]; then
  echo 'Publish directories must not be symbolic links.' >&2
  exit 1
fi
if [[ -d "$output" ]]; then
  resolved="$(cd -- "$output" && pwd -P)"
  [[ "$resolved" == "$repo/publish/api" ]] || exit 1
  rm -rf -- "$resolved"
fi
dotnet publish "$repo/src/MiLife.DeviceIntake.Api/MiLife.DeviceIntake.Api.csproj" \
  --configuration Release --runtime linux-x64 --self-contained false --output "$output"
echo "API published to $output"
