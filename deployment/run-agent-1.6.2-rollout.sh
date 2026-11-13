#!/bin/sh
set -eu

package=/home/mitech/rollout-agent-1.6.2.tar.gz
directory=/home/mitech/rollout-1.6.2
echo "29a179d0139d4ec6ba5695f9cdd72d226ffbfc4655593d8caf774d1131dee209  $package" | sha256sum -c -
rm -rf "$directory"
mkdir "$directory"
tar -xzf "$package" -C "$directory"
python3 "$directory/deployment/rollout-online-agent.py" \
    "$directory/extre/start-agent-1.6-download.ps1" \
    "$directory/extre/finalize-agent-1.6.ps1"
