> Dedicated production server: `172.26.50.221`. Release 1.3.1 and the existing public hostname were activated on 2026-09-23.

## Dedicated replacement deployment — 2026-09-23

The replacement host runs Ubuntu 22.04, MySQL 8.0, Nginx, and a self-contained
Linux x64 API. Kestrel and MySQL listen only on loopback. Nginx exposes the
approved collector routes on `127.0.0.1:5089` for the HTTPS tunnel.

The API, database, gateway, dashboard login/CSRF flow, CSV export, download range
support, automatic process recovery, and public agent lifecycle passed live
tests. The deployed agent SHA-256 is:

```text
584191dee021923da0253c1412d78c319a051b674e66c83cfc84af2f4f3e1ee5
```

`milife-device-intake-backup.timer` creates a root-only MySQL backup each day
and keeps 14 days. The first backup completed successfully. The runtime database
account has only SELECT, INSERT, UPDATE, and DELETE after migrations.

The old `.46` host was unavailable. IT explicitly chose a fresh database instead
of restoring the old records. Previously registered PCs therefore need to run
the current download once to receive credentials for this server.

The controlled reboot test passed: MySQL, Nginx, the API, gateway, ngrok tunnel,
and backup timer returned automatically. Public health recovered in about four
minutes. A forced-process failure test also passed; systemd restarted the API
and health recovered without intervention. Restarting only the tunnel restored
the public hostname immediately.

Public acceptance covered inventory intake, agent enrollment, heartbeat,
approved support-command delivery/result, disable, re-enable, complete removal,
dashboard login, device details, CSV export, mobile layout, and logout. Synthetic
records were deleted afterward, leaving the fresh production database empty.
The complete 76,696,629-byte agent was downloaded through the public hostname;
its SHA-256 matched the server and local release.

> Current release and validation: [Registration management 1.2](REGISTRATION.md). The notes below retain the initial deployment history.

# Server deployment

Deployed to `172.26.50.113` on 2026-09-17 as a standalone application.

## Public addresses

- Registration: https://uphill-cofounder-trident.ngrok-free.dev/device-registration
- Windows collector: https://uphill-cofounder-trident.ngrok-free.dev/download/device-collector
- Readiness: https://uphill-cofounder-trident.ngrok-free.dev/collector/health
- Intake: `POST https://uphill-cofounder-trident.ngrok-free.dev/api/device-intake`

The executable embeds the public intake URL and the intake-only token. It remains unsigned. The ngrok hostname is shared with the existing inventory site by explicit approval; the applications, credentials and databases remain separate. Ngrok may display its own visitor interstitial to browser users.

## Server layout

| Resource | Value |
| --- | --- |
| API files | `/opt/milife-device-intake/` |
| Collector download | `/opt/milife-device-intake/downloads/MiLifeDeviceCollector.exe` |
| Service | `milife-device-intake.service`, enabled at boot |
| OS service user | `milife-intake` |
| Private API | `127.0.0.1:5088` |
| MySQL database | `milife_device_intake` |
| MySQL account | `milife_intake@localhost` |
| Runtime DB permissions | SELECT, INSERT, UPDATE, DELETE on this database only |
| Secrets | `/etc/milife-device-intake/device-intake.env`, root-owned, mode 0600 |
| Gateway | Nginx on `127.0.0.1:5089` |
| Existing inventory backend | Unchanged on port 5000 |

MySQL migration `20260917164302_InitialMySql` creates Devices, DeviceSubmissions, Reviews and EF migration history. The runtime password is newly generated and distinct from the supplied administrator password. Both enrollment and admin tokens are independent random secrets. None are committed to Git.

## Shared hostname routing

The original ngrok unit remains intact. Its new user-service drop-in is:

```text
/home/kevin-x/.config/systemd/user/ngrok.service.d/50-milife-gateway.conf
```

It changes the upstream from `localhost:5000` to `127.0.0.1:5089` and disables local request inspection. User lingering is enabled, so the existing ngrok user service can run across logouts/reboots.

Nginx additions:

```text
/etc/nginx/sites-available/milife-gateway
/etc/nginx/sites-enabled/milife-gateway
/etc/nginx/snippets/milife-forwarding.conf
/etc/nginx/conf.d/milife-upgrade.conf
```

The collector routes and the dashboard/heartbeat routes described below go to port 5088. Other paths continue to the inventory backend on port 5000. `/api/admin/submissions` and its child paths return 404 publicly. Browser review is available through authenticated sessions at /device-admin. The older token-based admin API remains private through SSH.

For a private admin session, create an SSH local port forward:

```powershell
ssh -N -L 5088:127.0.0.1:5088 kevin-x@172.26.50.46
```

On the deployment workstation, in another terminal:

```powershell
$secrets = Get-Content .\artifacts\deployment\server-secrets.json -Raw | ConvertFrom-Json
$headers = @{ 'X-Admin-Token' = $secrets.AdminToken; 'X-Forwarded-Proto' = 'https' }
Invoke-RestMethod 'http://127.0.0.1:5088/api/admin/submissions?status=PendingReview' -Headers $headers
```

The SSH connection encrypts the transport; the forwarded-protocol header is accepted only from the trusted loopback peer. Do not expose this private port or copy admin credentials into the EXE. On another administration workstation, obtain the admin token through your approved secret-sharing process.

## Operations

```bash
sudo systemctl status milife-device-intake --no-pager
sudo journalctl -u milife-device-intake -n 100 --no-pager
curl --fail http://127.0.0.1:5088/health
systemctl --user status ngrok --no-pager
sudo nginx -t
```

Before upgrading, back up the dedicated MySQL database, review the generated MySQL migration, and temporarily grant this database's schema permissions to a migration account (or the service account for the controlled upgrade). Remove DDL permissions after migration. Never grant access to the inventory schemas. Keep the root-only environment file and make an encrypted backup through your normal credential management process.

To return ngrok to its previous inventory-only routing, remove only the `50-milife-gateway.conf` drop-in, then run `systemctl --user daemon-reload` and `systemctl --user restart ngrok` as `kevin-x`. This stops public collector access but preserves the collector service and database. The original unit was also copied to `/home/kevin-x/milife-deploy/ngrok-original.service` during deployment.

## Validation

- 51 automated tests passed locally after adding MySQL support.
- 16 additional checks passed against the deployed MySQL/API, including concurrent identity creation, idempotent retries, review conflicts, serial collation, authentication, invalid JSON, missing serials and request size limits. The test cleaned up only its own synthetic rows.
- Service restarted successfully with DDL permissions removed from the database user.
- Public registration and health return HTTP 200; public admin routing returns 404.
- Inventory homepage content was byte-identical directly and through the gateway, and the public inventory root returns HTTP 200.
- The compressed, self-contained Windows EXE ran twice from a non-elevated Windows session and submitted successfully through the public HTTPS endpoint. MySQL contains two real `PendingReview` records for one device, with the second correctly marked as a repeat. These real records are retained for IT review.
- The public download returns HTTP 200, `application/octet-stream`, the correct attachment filename, and a content length of 35,638,425 bytes (about 34 MiB). The deployed release matches the local build's SHA-256:

```text
73f68e018f06e9d7b5ae5faaafe24d36ed59e43c9084ee32570000e0f698a335
```

The full public binary transfer was slow during acceptance testing: a server-side request received 9,290,640 bytes before its 180-second test timeout. Those bytes match the deployed binary exactly, and the full deployed binary matches the local build. A full public download was not completed within that test timeout. Normal browser downloads can run longer; plan for a slow initial download on this ngrok connection. The real collector-to-API HTTPS submission test completed successfully twice. For larger staff enrollment batches, a stable company HTTPS host is preferable to this temporary ngrok link.

The local ignored `artifacts/deployment/` folder holds release verification output and private deployment credentials. Do not upload or commit that directory. Production source/configuration templates in this repository contain no live secrets.

## Dashboard and optional heartbeat — 2026-09-18

Dashboard: https://uphill-cofounder-trident.ngrok-free.dev/device-admin

Agent: https://uphill-cofounder-trident.ngrok-free.dev/download/device-agent

The application username is `root`; the requested password is stored as a PBKDF2 hash in the private server environment. Configure `Admin__Username`, `Admin__PasswordHash`, persistent `Admin__DataProtectionPath` and `Intake__AgentPath`. Login uses secure HTTP-only cookies and CSRF protection. Reviews record the signed-in account. The older token-based admin API remains private through SSH; authenticated browser review is publicly reachable.

Users explicitly click Enable heartbeat. The agent registers inventory, stores a DPAPI-protected per-user credential, adds a current-user Startup shortcut and provides a visible tray icon. It runs without AD or elevation while the user is signed in. Heartbeats send the agent ID/version every five minutes; Online expires after 15 minutes without a heartbeat. Disable heartbeat removes the startup shortcut and notifies the server. IT can revoke an agent. No remote commands or PowerShell are implemented. HTTPS uses normal public certificate validation; no self-signed certificate installation is needed. The executables are unsigned.

Build with scripts/build-agent.ps1 using the collector API URL and enrollment-token environment variables. The agent is approximately 73 MiB and downloads may be slow over ngrok. Full public binary download completion remains unverified; the local release matching the deployed file was used for the live agent test.

Validation: 56 automated tests pass. Real Edge browser checks passed for public login, device list, hardware details, mobile layout and logout. A non-elevated agent registered and sent a public HTTPS heartbeat; the dashboard showed Online. Disabling removed its Startup shortcut and disabled it on the server. The test process is no longer running. One disabled test agent and three inventory submissions for one device remain for review.

MySQL migration 20260917172148_HeartbeatAgents is applied. Runtime DB grants remain SELECT/INSERT/UPDATE/DELETE. Nginx also forwards /device-admin and its children, /api/device-agent/ and /download/device-agent to the collector API.
