#!/usr/bin/env python3
"""Queue a release for online agents through the production command tables."""

import json
import subprocess
import sys
import time
import uuid
from pathlib import Path


TARGET_VERSION = "1.6.2"
EXPECTED_BYTES = 76_737_513
ACTIVE_STATES = "'Queued','AwaitingApproval','Running'"


def mysql(sql: str) -> str:
    result = subprocess.run(
        ["mysql", "--batch", "--skip-column-names", "milife_device_intake"],
        input=sql, text=True, check=True, capture_output=True)
    return result.stdout.strip()


def hex_text(value: str) -> str:
    return "CONVERT(0x" + value.encode().hex() + " USING utf8mb4)"


def targets():
    rows = mysql("""
SELECT Id,SerialNumber,COALESCE(EmployeeName,''),AgentVersion
FROM Agents
WHERE IsEnabled=1 AND IsRevoked=0
  AND LastSeenAtUtc >= UTC_TIMESTAMP() - INTERVAL 15 MINUTE
  AND CAST(SUBSTRING_INDEX(AgentVersion,'.',1) AS UNSIGNED)=1
  AND (CAST(SUBSTRING_INDEX(SUBSTRING_INDEX(AgentVersion,'.',2),'.',-1) AS UNSIGNED)<6
       OR (CAST(SUBSTRING_INDEX(SUBSTRING_INDEX(AgentVersion,'.',2),'.',-1) AS UNSIGNED)=6
           AND CAST(SUBSTRING_INDEX(AgentVersion,'.',-1) AS UNSIGNED)<2));
""")
    return [dict(zip(("id", "serial", "employee", "version"), row.split("\t")))
            for row in rows.splitlines() if row]


def queue(agent: dict, script: str, name: str) -> str:
    active = mysql(f"SELECT COUNT(*) FROM SupportJobs WHERE AgentId='{agent['id']}' AND Status IN ({ACTIVE_STATES});")
    if active != "0":
        raise RuntimeError(f"{agent['serial']} already has an active command")
    job = str(uuid.uuid4())
    mysql(f"""
INSERT INTO SupportJobs
(Id,AgentId,Script,CommandType,DisplayName,RunSilently,RequestedBy,Status,RequestedAtUtc,ExpiresAtUtc)
VALUES ('{job}','{agent['id']}',{hex_text(script)},'agent-upgrade',{hex_text(name)},1,
        'deployment-1.6.2','Queued',UTC_TIMESTAMP(),UTC_TIMESTAMP()+INTERVAL 24 HOUR);
""")
    return job


def wait_jobs(jobs: dict, timeout=150):
    deadline = time.monotonic() + timeout
    pending = dict(jobs)
    finished = {}
    while pending and time.monotonic() < deadline:
        for agent_id, job_id in list(pending.items()):
            row = mysql(f"SELECT Status,HEX(COALESCE(Output,'')),COALESCE(ExitCode,-999) FROM SupportJobs WHERE Id='{job_id}';")
            if not row:
                continue
            status, encoded_output, exit_code = row.split("\t", 2)
            output = bytes.fromhex(encoded_output).decode(errors="replace")
            if status in {"Completed", "Declined", "Expired", "TimedOut", "ResultUnavailable", "Cancelled"}:
                if status != "Completed" or exit_code not in {"-999", "0"}:
                    raise RuntimeError(f"Agent {agent_id} command failed: {status} {output}")
                finished[agent_id] = output
                del pending[agent_id]
        if pending:
            time.sleep(10)
    if pending:
        raise RuntimeError("Commands did not complete: " + ", ".join(pending))
    return finished


def main():
    if len(sys.argv) != 3:
        raise SystemExit("Usage: rollout-online-agent.py <start-script> <finalize-script>")
    start_script = Path(sys.argv[1]).read_text(encoding="utf-8-sig")
    finalize_script = Path(sys.argv[2]).read_text(encoding="utf-8-sig")
    if len(start_script) > 4096 or len(finalize_script) > 4096:
        raise SystemExit("A rollout stage exceeds the API command limit")
    agents = targets()
    print(json.dumps({"version": TARGET_VERSION, "targets": agents}, indent=2), flush=True)
    if not agents:
        return

    staged = {}
    for agent in agents:
        existing = mysql(f"SELECT Id FROM SupportJobs WHERE AgentId='{agent['id']}' AND RequestedBy='deployment-1.6.2' AND DisplayName='Download agent 1.6.2' ORDER BY RequestedAtUtc DESC LIMIT 1;")
        staged[agent["id"]] = existing or queue(agent, start_script, "Download agent 1.6.2")
    wait_jobs(staged)
    print("All online targets started verified BITS downloads.", flush=True)

    incomplete = {agent["id"]: agent for agent in agents}
    status_script = ("Import-Module BitsTransfer;$j=Get-BitsTransfer -Name 'MiLifeAgent162' "
        "-ErrorAction SilentlyContinue;if(!$j){'Missing'}else{\"$($j.JobState)|$($j.BytesTransferred)|$($j.BytesTotal)\"}")
    deadline = time.monotonic() + 1800
    while incomplete and time.monotonic() < deadline:
        checks = {agent_id: queue(agent, status_script, "Check agent download")
                  for agent_id, agent in incomplete.items()}
        results = wait_jobs(checks)
        for agent_id, output in results.items():
            state = output.strip().splitlines()[-1]
            print(f"{incomplete[agent_id]['serial']}: {state}", flush=True)
            parts = state.split("|")
            if len(parts) == 3 and parts[0] == "Transferred" \
                    and int(parts[1]) == EXPECTED_BYTES and int(parts[2]) == EXPECTED_BYTES:
                del incomplete[agent_id]
            elif parts[0] in {"Error", "TransientError", "Cancelled"}:
                raise RuntimeError(f"{agent_id} BITS entered {parts[0]}")
        if incomplete:
            time.sleep(30)
    if incomplete:
        raise RuntimeError("Downloads did not complete: " + ", ".join(incomplete))

    finalized = {agent["id"]: queue(agent, finalize_script, "Install agent 1.6.2") for agent in agents}
    wait_jobs(finalized)
    print("All verified replacements are scheduled.", flush=True)

    pending = {agent["id"]: agent for agent in agents}
    deadline = time.monotonic() + 600
    while pending and time.monotonic() < deadline:
        for agent_id, agent in list(pending.items()):
            row = mysql(f"SELECT AgentVersion,IF(LastSeenAtUtc >= UTC_TIMESTAMP()-INTERVAL 15 MINUTE,'Online','Offline') FROM Agents WHERE Id='{agent_id}';")
            if row:
                version, availability = row.split("\t", 1)
                if version == TARGET_VERSION and availability == "Online":
                    print(f"{agent['serial']}: confirmed online on {TARGET_VERSION}", flush=True)
                    del pending[agent_id]
        if pending:
            time.sleep(15)
    if pending:
        raise RuntimeError("No 1.6.2 heartbeat from: " + ", ".join(pending))


if __name__ == "__main__":
    main()
