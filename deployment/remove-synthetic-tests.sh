#!/usr/bin/env bash
set -euo pipefail

[[ $EUID -eq 0 ]] || { echo 'Run as root.' >&2; exit 1; }
mysql <<'SQL'
USE milife_device_intake;
DELETE r FROM Reviews r JOIN DeviceSubmissions s ON r.SubmissionId = s.Id
  WHERE s.SerialNumber LIKE 'RECOVERY-CHECK-%' OR s.SerialNumber LIKE 'PUBLIC-STACK-%';
DELETE aa FROM AgentActions aa JOIN Agents a ON aa.AgentId = a.Id
  WHERE a.SerialNumber LIKE 'RECOVERY-CHECK-%' OR a.SerialNumber LIKE 'PUBLIC-STACK-%';
DELETE sj FROM SupportJobs sj JOIN Agents a ON sj.AgentId = a.Id
  WHERE a.SerialNumber LIKE 'RECOVERY-CHECK-%' OR a.SerialNumber LIKE 'PUBLIC-STACK-%';
DELETE FROM Agents WHERE SerialNumber LIKE 'RECOVERY-CHECK-%' OR SerialNumber LIKE 'PUBLIC-STACK-%';
DELETE FROM DeviceSubmissions WHERE SerialNumber LIKE 'RECOVERY-CHECK-%' OR SerialNumber LIKE 'PUBLIC-STACK-%';
DELETE FROM Devices WHERE SerialNumber LIKE 'RECOVERY-CHECK-%' OR SerialNumber LIKE 'PUBLIC-STACK-%';
SQL
echo 'Removed synthetic public acceptance records.'
