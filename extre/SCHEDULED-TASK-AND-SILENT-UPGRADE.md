# MiLife agent 1.6: scheduled task and silent upgrade method

The production 1.6 binary for this rollout has SHA-256:

`7C65A6FF3631F4D0E8088EA28514789F72A18637945AF3A0B7E3C95DB4A040E4`

## Scheduled task created by the agent

`ProfileStore.EnableStartup()` copies the configured agent to `%LOCALAPPDATA%\MiLifeDeviceAgent\MiLifeDeviceAgent.exe` and uses the Windows Task Scheduler COM API. The task is named `MiLife Device Heartbeat` and has:

- a logon trigger restricted to the current Windows identity;
- `TASK_LOGON_INTERACTIVE_TOKEN` (`LogonType = 3`);
- limited-user execution (`RunLevel = 0`), with no UAC prompt or administrator rights;
- `--background` as the agent argument;
- start-when-available, battery operation, no execution time limit, and three one-minute restart attempts;
- `IgnoreNew` for duplicate starts.

The explicit current `UserId` on both the principal and logon trigger is required on MILHQIT. Root-folder registration without that identity returned `E_ACCESSDENIED` under the local domain policy. After registering the task, the agent deletes the legacy Startup-folder shortcut.

The exact C# implementation is in `src/MiLife.DeviceAgent/AgentProfile.cs`. The uninstall helper removes both the scheduled task and any legacy shortcut.

## Silent upgrade

The production rollout used two commands because the ngrok download was too slow for the agent's 60-second command process and child PowerShell processes were terminated with that process tree:

1. `start-agent-1.6-download.ps1` starts a persistent asynchronous Windows BITS transfer.
2. After BITS reports `Transferred`, `finalize-agent-1.6.ps1` completes the transfer, verifies the fixed hash, and creates a current-user one-time task for the swap.

The API download endpoint must accept both `GET` and `HEAD`; BITS initially returned HTTP 405 until `DownloadEndpoints` was updated for both methods. It also uses range processing for resumable transfer.

`silent-upgrade-to-1.6.ps1` is retained as the full combined reference implementation. It does not contain credentials or an administrator password, but the BITS method above is the tested fleet method.

1. The support command starts a separate hidden PowerShell process and returns immediately. This keeps a slow agent download outside the command runner's 60-second execution limit.
2. The detached updater downloads only over HTTPS with redirects disabled.
3. It verifies the complete file against the fixed release SHA-256 before stopping anything.
4. It stops only `MiLifeDeviceAgent.exe` running from the exact `%LOCALAPPDATA%\MiLifeDeviceAgent` path.
5. It keeps the old executable as `.rollback`, replaces it with the verified 1.6 binary, creates the current-user Scheduled Task, and removes the legacy shortcut.
6. It launches 1.6 through Task Scheduler and waits for the exact installed process path.
7. If 1.6 does not remain running, it restores the previous executable and restarts it through the same task.
8. It records local progress in `%LOCALAPPDATA%\MiLifeDeviceAgent\upgrade-1.6.log`.

Before using the script for a rebuilt release, replace `$expectedSha256` with the hash from `Get-FileHash publish\agent\MiLifeDeviceAgent.exe -Algorithm SHA256`. Never remove the hash check.

The safe rollout order is MILHQIT first, confirm a 1.6 heartbeat, then select only online enabled agents below 1.6. Queue the BITS stage silently with a 24-hour delivery window. Poll BITS until the state is `Transferred` and the byte count is `76702566`; then queue the finalizer. Confirm each device reports 1.6 before expanding the batch. Do not queue another support command while an upgrade is active.

On 13 October 2026, the deployment produced eight confirmed 1.6 agents including MILHQIT. Devices whose finalizer did not swap continued heartbeating on 1.4; further live retries were stopped to avoid employee disruption. Offline agents were not forced and remain eligible for a later controlled batch.
