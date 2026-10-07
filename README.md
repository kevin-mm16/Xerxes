> Production runs release 1.5. The interactive React administration portal is live at [device admin](https://uphill-cofounder-trident.ngrok-free.dev/device-admin), with device-type and availability filters, multi-device script execution, command activity and server health. See [the deployment record](deployment/DEPLOYED.md).

# MiLife Device Inventory Collector

A standalone, user-run Windows hardware collector and Ubuntu intake API. This project has its own staging database and authentication: SQLite for local development, or MySQL for server deployment. It does not connect to, change, or create assets in the existing inventory application. See [the server deployment record](deployment/DEPLOYED.md) for the active installation and public links.

```text
Windows PC
   |
MiLifeDeviceCollector.exe (visible, standard-user process)
   |
HTTPS POST /api/device-intake
   |
ngrok (or a trusted local HTTPS reverse proxy)
   |
Ubuntu: 127.0.0.1:5088
   |
.NET 8 Device Intake API
   |
Independent staging database -> authenticated IT review
```

## Repository

```text
src/
  MiLife.DeviceContracts/       Shared, strictly bounded JSON contract and validation
  MiLife.DeviceCollector/       Windows hardware collection, submission, recovery and logs
  MiLife.DeviceIntake.Api/      Intake, SQLite migrations, review API and download page
tests/
  MiLife.DeviceCollector.Tests/
  MiLife.DeviceIntake.Api.Tests/
scripts/
  build-collector.ps1
  start-local.ps1
  publish-api.ps1
  publish-api.sh
deployment/
  milife-device-intake.service
  device-intake.env.example
```

## Development setup

Use Windows x64 with the .NET 8 SDK, feature band 8.0.400 or newer 8.0.4xx servicing SDK, for the complete solution. `global.json` pins that feature band. API development and API tests also work on Linux with that SDK. Ubuntu only needs the ASP.NET Core 8 runtime to run the published API.

Dependency restoration contacts NuGet.org. Build/test commands below may restore missing packages. No collector data is sent by these commands.

```powershell
dotnet restore MiLife.DeviceInventory.sln
dotnet test MiLife.DeviceInventory.sln --configuration Release
```

Tests use temporary SQLite databases and synthetic devices; they do not collect real hardware. HTTP retry tests use mocked handlers, while API integration tests exercise the full ASP.NET Core pipeline and EF migrations. Tests cover normalization, validation, tokens, malformed/oversized JSON, missing serials, repeat devices, concurrent intake, idempotency, rate limiting, HTTPS enforcement, review auditing, recovery files, and retry behavior.

### Build the React dashboard

The React and TypeScript source is in `src/MiLife.DeviceIntake.Dashboard`. Its Vite build
writes hashed static assets directly to the API's
`wwwroot/device-admin-react-preview` directory.

```powershell
cd src\MiLife.DeviceIntake.Dashboard
npm.cmd ci
npm.cmd run build
```

The preview uses the existing cookie and CSRF-protected dashboard API. Its authenticated
server-sent event stream refreshes visible data every ten seconds, with a 30-second timer
as a fallback.

### Start locally with HTTPS

Check for a trusted development certificate:

```powershell
dotnet dev-certs https --check --trust
```

If it is missing, `dotnet dev-certs https --trust` creates/trusts a local development certificate and may prompt for confirmation. Never distribute that certificate to employee PCs or disable TLS validation in the collector.

```powershell
.\scripts\start-local.ps1 -BuildCollector
```

If the workstation's default PowerShell execution policy blocks local scripts, run them in a process scoped to `RemoteSigned`, for example `powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\scripts\start-local.ps1 -BuildCollector`. This does not change the machine/user execution policy and cannot override an enforced Group Policy.

This creates random, distinct local tokens in ignored `artifacts/local/settings.json`, builds the collector for `https://localhost:7088/api/device-intake`, and starts the API. Open `https://localhost:7088/device-registration` or run `publish/collector/MiLifeDeviceCollector.exe` on this same PC. The local SQLite database is in `artifacts/local/`.

The local executable only targets the development PC. Rebuild it with the server URL and deployment enrollment token before distributing it. `-InitializeOnly` prepares local configuration without starting the API. Keep local settings because rebuilding with new tokens would invalidate previous local builds.

For API-only development, HTTP can be explicitly enabled **only for loopback clients in the Development environment** with `Intake__AllowInsecureLocalDevelopment=true`. The collector still requires HTTPS. Production never accepts this exception. `/health` can be queried over loopback HTTP for service monitoring.

## Build and publish the Windows executable

Use an HTTPS intake URL and a random enrollment token of at least 32 printable ASCII characters. Keep the token in an environment variable rather than putting it in command history:

```powershell
$env:DEVICE_COLLECTOR_API_URL = 'https://YOUR-NGROK-HOST/api/device-intake'
# Populate DEVICE_INTAKE_TOKEN securely with the same enrollment token as the API.
.\scripts\build-collector.ps1
```

Output: `publish/collector/MiLifeDeviceCollector.exe`. The script cleans only that output directory, embeds one configuration resource, publishes `win-x64` with `SelfContained=true`, `PublishSingleFile=true`, single-file compression, no trimming, and removes the temporary configuration file afterward. .NET does not need to be installed on employee PCs. The native runtime can extract its bundled files to the user's temporary directory as part of .NET single-file startup; there is no installation or persistence.

The centralized settings are in `Configuration/CollectorSettings.cs`; `collector-settings.example.json` documents the embedded format. Production configuration and tokens are excluded from source control. A static enrollment token can be extracted from the executable: it grants intake only, is not an admin credential, and should be rotated by rebuilding/replacing downloads. Short-lived enrollment can later replace this mechanism at the configuration/submission boundary.

For an explicitly unconfigured packaging check:

```powershell
.\scripts\build-collector.ps1 -Unconfigured
```

That executable collects and saves a recovery copy, but cannot submit. Do not distribute it as a configured production build.

Optional branch override remains available:

```powershell
.\MiLifeDeviceCollector.exe --branch ACCRA1
```

No override means `UNKNOWN`. Branch-specific links/builds are not implemented, as requested. A normal webpage cannot auto-launch an executable; staff download and open it, then collection/submission proceeds without form input.

### Signing

Unsigned builds may show a SmartScreen/unrecognized-app warning. Do not disable Defender, bypass SmartScreen, or tell staff to bypass an unexplained warning. IT should verify and sign the release using an organizational Authenticode certificate.

```powershell
.\scripts\build-collector.ps1 -SigningScript 'C:\IT\sign-collector.ps1'
```

The optional script receives `-FilePath` after publishing and must sign/timestamp the binary, throw on error, and return a signature that Windows validates. Keep certificate credentials out of this repository. Signing or timestamping may require separate network access. SHA-256 is printed after the signing stage so IT can publish the final file hash.

## Collection and failure behavior

Collects only computer name, BIOS manufacturer/model/serial, logged-in Windows username, CPU information, RAM/modules, physical disks, Windows details, active non-loopback adapters with MAC addresses, optional branch, version and timestamps. Active VPN/virtual adapters can appear. IP addresses are not enumerated by the collector; the API records the transport source IP as receipt metadata.

WMI queries run under the launching user's token (`asInvoker`). No shell, PowerShell commands, service installation, scheduled task, registry modification, remote execution, or elevation is used by the collector. Missing properties remain null or `Unknown`. Disk data uses `MSFT_PhysicalDisk` with `Win32_DiskDrive` fallback; disks are not guessed to be SSDs from their model name. NVMe is identified from the reported bus. RAM values use binary GiB (shown as GB for consistency with the brief); disks use decimal GB, matching vendor capacities. CPU counts sum all reported processors.

The collector makes at most three attempts with 2- and 4-second delays and a 30-second timeout per request. It retries transport failures, timeout/408, 429 and server errors; other HTTP errors stop immediately. It honors short `Retry-After` delays. Longer than 30 seconds stops with local recovery rather than retrying too early. HTTP redirects are not followed, so enrollment tokens cannot be redirected to another host. TLS certificate validation is never disabled.

Every run gets a new `collectionId`. Network retries reuse it; the API returns the same receipt for the same payload. Reusing an ID with changed content returns 409. A fresh run on an existing serial is a new historical submission, marked `isRepeat=true`, associated with the same normalized device identity. Every fresh submission starts `PendingReview`, even if an earlier submission was approved.

Serials are trimmed and uppercased without removing meaningful punctuation/spaces. Missing or obvious firmware placeholders such as `Unknown` and `To be filled by O.E.M.` are not trustworthy identities. Those runs save a local copy for manual IT identification instead of inventing an identity or merging unrelated PCs. Real devices can still have duplicated manufacturer serials; an IT reviewer must resolve such exceptions.

Recovery files:

```text
%LOCALAPPDATA%\MiLifeDeviceCollector\Pending\SERIALNUMBER-yyyyMMdd-HHmmss.json
```

Filename characters are restricted and collisions get a random suffix; existing files are never overwritten. All unsuccessful submissions try to save a recovery copy, including validation/authentication errors. If writing fails, the message says so. Pending files are not automatically resubmitted or deleted. IT should retrieve them through an approved channel, validate/repair missing identities, and submit with a new collection ID if content changes. They contain usernames, MAC addresses and hardware identifiers, so handle them as internal inventory data.

Logs: `%LOCALAPPDATA%\MiLifeDeviceCollector\Logs\`, at most four files of roughly 256 KiB each. Logs include time, collector version, controlled collection diagnostics, API status and outcome. They omit tokens, HTTP bodies, and exception messages that could include sensitive data. Hardware data is not printed to logs. The console displays the result and waits for a key so double-click users can read it; redirected stdin exits automatically.

## Publish the Ubuntu API

From Windows:

```powershell
.\scripts\publish-api.ps1
```

Or from Linux with the SDK:

```bash
bash scripts/publish-api.sh
```

Both publish a framework-dependent `linux-x64` build into `publish/api/`. Copy those contents into `/opt/milife-device-intake/`, separately from the existing inventory application. Copy the correctly configured collector into `/opt/milife-device-intake/downloads/MiLifeDeviceCollector.exe`.

Use port **5088**, leaving the existing inventory backend on 5000 untouched. MySQL deployments use a new `milife_device_intake` database and `milife_intake` account; there is no connection to the inventory application's database. SQLite remains the default unless `Database__Provider=MySql` is set.

### Environment variables

| Variable | Purpose |
| --- | --- |
| `DEVICE_INTAKE_TOKEN` | Required intake-only secret, 32–512 printable ASCII characters |
| `DEVICE_ADMIN_TOKEN` | Required distinct admin secret, same constraints |
| `ASPNETCORE_ENVIRONMENT` | `Production` on Ubuntu |
| `ASPNETCORE_URLS` | `http://127.0.0.1:5088` behind local ngrok |
| `Database__Provider` | `Sqlite` (default) or `MySql` |
| `Database__MySqlVersion` | MySQL server version, e.g. `8.4.11` |
| `ConnectionStrings__DeviceIntake` | `Data Source=/var/lib/milife-device-intake/device-intake.db;Default Timeout=10` |
| `Intake__CollectorPath` | Absolute path of the published collector |
| `Intake__RequestsPerMinute` | Per-source-IP intake limit, default 20 |
| `Intake__GlobalRequestsPerMinute` | Global request limit, default 300 |
| `Intake__RequestLimitBytes` | Maximum body size, default 131072 (128 KiB) |

The API refuses to start with absent, identical, or invalid tokens. Generate two different secrets, for example by running `openssl rand -hex 32` twice on the deployment machine. Never put actual secrets in a committed settings file or the systemd unit.

For MySQL, use `deployment/device-intake.mysql.env.example`. `deployment/install-server.py` installs a staged Linux release, creates only this project's MySQL database/user, and configures its systemd service. It expects `api.tar.gz`, the service unit, and a private JSON file with three distinct generated secrets: `IntakeToken`/`AdminToken` are 64 lowercase hexadecimal characters; `DatabasePassword` is independently generated 64-character hex plus `aA1!` to satisfy the server's character-class policy. Transfer these over SSH, run the installer as root, check the migration and health, then revoke `CREATE, ALTER, INDEX, REFERENCES, DROP` from the runtime user. Keep only `SELECT, INSERT, UPDATE, DELETE` on this schema during normal operation. Future schema upgrades need a controlled temporary grant or a separate migration account. The service can restart successfully after those DDL grants are revoked.

### systemd setup

After copying the published files, an administrator can run:

```bash
sudo useradd --system --home /var/lib/milife-device-intake --shell /usr/sbin/nologin milife-intake
sudo install -d -m 0755 /opt/milife-device-intake/downloads
sudo install -d -m 0700 /etc/milife-device-intake
sudo install -m 0600 deployment/device-intake.env.example /etc/milife-device-intake/device-intake.env
sudoedit /etc/milife-device-intake/device-intake.env
# Replace both CHANGE_ME values and verify the paths.
sudo chown -R root:root /opt/milife-device-intake
sudo chmod -R go-w /opt/milife-device-intake
sudo install -m 0644 deployment/milife-device-intake.service /etc/systemd/system/milife-device-intake.service
sudo systemctl daemon-reload
sudo systemctl enable --now milife-device-intake
sudo systemctl status milife-device-intake --no-pager
curl --fail http://127.0.0.1:5088/health
```

Skip user creation if that service user already exists. systemd reads the root-only environment file, creates `/var/lib/milife-device-intake` owned by the service user, and restricts writes to that state directory and private temporary storage. The app and collector must be readable by the service user. The database is created/migrated on startup; migration failures stop startup. Use one instance for this SQLite deployment.

For a foreground diagnostic run under the appropriate account with the same environment configured:

```bash
cd /opt/milife-device-intake
dotnet MiLife.DeviceIntake.Api.dll
```

Inspect diagnostics with `sudo journalctl -u milife-device-intake -n 100 --no-pager`. Set journald retention/storage limits for your host. Application logs do not include token headers or raw payloads; avoid enabling sensitive EF logging or HTTP-body logging.

## Expose using ngrok

With ngrok installed and authenticated separately:

```bash
ngrok http http://127.0.0.1:5088 --inspect=false
```

Disabling traffic inspection avoids retaining inventory bodies and token headers in ngrok's local inspector. Use the **HTTPS** forwarding URL. Staff visit:

```text
https://YOUR-NGROK-HOST/device-registration
```

The download endpoint is `GET /download/device-collector`; it returns the fixed configured file as an attachment named `MiLifeDeviceCollector.exe`, never a filename from the request. If the release file is missing, it returns 503 with a clear message.

Do not assume the assigned hostname will stay the same. When it changes, rebuild and redistribute the collector with the new URL. Configure the collector after the intended public HTTPS URL is known. Confirm your ngrok account permits an additional tunnel alongside the existing inventory tunnel before starting one; this repository does not stop or modify the existing service.

The API only trusts forwarded client IP/protocol headers from a loopback proxy, and only one hop. Bind Kestrel to loopback on Ubuntu. Never expose port 5088 publicly or trust arbitrary forwarded headers. If introducing Nginx or a multi-hop proxy, explicitly review the trust chain and forwarded-header configuration first.

Ngrok traffic passes through its service; use your company's approved account and access/retention policies. Multiple PCs sharing a branch's public IP share the per-IP intake limit; tune it for enrollment batches without removing the global limit.

## Inspect and review submissions

Use `X-Admin-Token` for these routes. An enrollment token cannot read or review records. The browser dashboard at `/device-admin` uses separate cookie authentication. Submission data requires authentication.

| Route | Action |
| --- | --- |
| `GET /health` | Database-backed readiness, healthy/unhealthy only |
| `GET /api/admin/submissions?status=PendingReview` | List pending submissions |
| `GET /api/admin/submissions?serialNumber=ABC123` | Exact normalized serial match/history |
| `GET /api/admin/submissions?computerName=SALES&branch=ACCRA1` | Computer-name substring and branch filters |
| `GET /api/admin/submissions?page=1&pageSize=25` | Paginated records, maximum 100 per page |
| `GET /api/admin/submissions/{id}` | Full hardware data, source IP, review history |
| `POST /api/admin/submissions/{id}/review` | Set `Matched`, `Approved` or `Rejected` |

Example from PowerShell against the local development API:

```powershell
$local = Get-Content .\artifacts\local\settings.json -Raw | ConvertFrom-Json
$headers = @{ 'X-Admin-Token' = $local.AdminToken }
$pending = Invoke-RestMethod 'https://localhost:7088/api/admin/submissions?status=PendingReview' -Headers $headers
$pending.items
$id = $pending.items[0].id
Invoke-RestMethod "https://localhost:7088/api/admin/submissions/$id" -Headers $headers
$review = @{ status = 'Approved'; expectedStatus = 'PendingReview'; note = 'Hardware checked by IT' } | ConvertTo-Json
Invoke-RestMethod "https://localhost:7088/api/admin/submissions/$id/review" -Method Post -Headers $headers -ContentType 'application/json' -Body $review
```

Review writes check `expectedStatus` to avoid overwriting a changed decision and retain an audit entry. Admin access initially uses one shared credential, so the audit actor is `admin-token`; replace it with individual identity/roles before multi-reviewer production use. Protect admin access further with your organization's VPN/proxy policy if needed. Approving or matching changes staging status only and creates no authoritative asset.

## Windows acceptance testing

1. On the development PC, start the HTTPS API and build its configured executable as above.
2. Run the EXE from a **standard-user, non-elevated** session. Confirm no UAC prompt, visible collection, and a success summary.
3. Confirm the new `PendingReview` record through the admin API and compare hardware against Windows System Information.
4. Run the EXE again. Confirm a new historical record with `isRepeat=true` under the same normalized serial.
5. Stop the API and run again. Confirm bounded retries and a JSON file in the user's Pending folder. Restore the API afterward.
6. Test the server-configured build on a second non-domain Windows x64 PC with no .NET runtime. This validates portability and firmware/provider differences that unit tests cannot cover.
7. Confirm the public page, download hash, valid HTTPS chain, and intake response before staff rollout. Do not test a development `localhost` build on employee PCs.

For an automated **real local hardware** smoke test after building the local-configured EXE and trusting the localhost development certificate, stop any existing local API and run `scripts/test-local.ps1`. It starts a temporary API process, runs the actual EXE twice, verifies repeat history and the downloaded binary hash, and stops its API process. It retains the two real inventory records and detailed console output under ignored `artifacts/local/`. Unlike unit tests, this intentionally collects the development PC's hardware and username.

Windows security policies, antivirus, browser download controls, inaccessible WMI providers, firmware serial quality, and unsupported Windows versions can affect collection. WMI query enumeration has an eight-second timeout per query; collection may take longer on a slow PC. A single-file self-contained .NET executable is portable but roughly tens of MB, not a tiny native utility. No ARM/x86 build is included.

## Database maintenance and future changes

EF Core migrations and the model snapshot are committed. For a future SQLite schema change:

```powershell
dotnet tool restore
dotnet ef migrations add DescribeChange --context IntakeDbContext --project src/MiLife.DeviceIntake.Api --output-dir Data/Migrations
# MySQL uses its own context, snapshot and migration history:
dotnet ef migrations add DescribeChange --context MySqlIntakeDbContext --project src/MiLife.DeviceIntake.Api --output-dir Data/MySqlMigrations
```

The design-time factory uses a separate development connection and does not require production secrets. Inspect generated migrations before deployment. Back up the SQLite database using SQLite's online backup facility or stop this service and copy the database plus any WAL/SHM files consistently before upgrading. Test restore and establish a retention policy for pending/rejected records, raw JSON and source IPs. There is deliberately no automatic record deletion.

Database registration is centralized in `Program.cs`; entities and submission/review services are separated from the provider. MySQL and SQLite are implemented with separate migration sets. MySQL identity races are retried after transaction rollback; review status changes use an atomic expected-status condition. `deployment/smoke-test-server.py` exercises real MySQL concurrency, retry idempotency, review conflicts, authentication and validation, then removes only its own synthetic records. PostgreSQL/SQL Server still require a provider package, registration/connection changes, provider-specific migrations, and concurrency tests. SQLite serializes writes and is intended for a modest internal enrollment workload.

To replace ngrok, put a managed HTTPS domain in front of the loopback API, configure its certificate and trusted proxy headers, and rebuild the collector for the permanent URL. Preserve the independent staging database and keep enrollment/admin credentials separate.

If integration with the existing inventory system is requested later, add an explicit reviewed export/import operation with a source submission ID and serial-based reconciliation. Keep staging review and official asset creation separate so importing can be audited and repeated safely. No such integration is enabled in this repository.

## Historical release 1.1: dashboard and optional heartbeat � 2026-09-18

Dashboard: https://uphill-cofounder-trident.ngrok-free.dev/device-admin

Agent: https://uphill-cofounder-trident.ngrok-free.dev/download/device-agent

The application username is `root`; the requested password is stored as a PBKDF2 hash in the private server environment. Configure `Admin__Username`, `Admin__PasswordHash`, persistent `Admin__DataProtectionPath` and `Intake__AgentPath`. Login uses secure HTTP-only cookies and CSRF protection. Reviews record the signed-in account. The older token-based admin API remains private through SSH; authenticated browser review is publicly reachable.

Users explicitly click Enable heartbeat. The agent registers inventory, stores a DPAPI-protected per-user credential, adds a current-user Startup shortcut and provides a visible tray icon. It runs without AD or elevation while the user is signed in. Heartbeats send the agent ID/version every five minutes; Online expires after 15 minutes without a heartbeat. Disable heartbeat removes the startup shortcut and notifies the server. IT can revoke an agent. No remote commands or PowerShell are implemented. HTTPS uses normal public certificate validation; no self-signed certificate installation is needed. The executables are unsigned.

Build with scripts/build-agent.ps1 using the collector API URL and enrollment-token environment variables. The agent is approximately 73 MiB and downloads may be slow over ngrok. Full public binary download completion remains unverified; the local release matching the deployed file was used for the live agent test.

Validation: 56 automated tests pass. Real Edge browser checks passed for public login, device list, hardware details, mobile layout and logout. A non-elevated agent registered and sent a public HTTPS heartbeat; the dashboard showed Online. Disabling removed its Startup shortcut and disabled it on the server. The test process is no longer running. One disabled test agent and three inventory submissions for one device remain for review.

MySQL migration 20260917172148_HeartbeatAgents is applied. Runtime DB grants remain SELECT/INSERT/UPDATE/DELETE. Nginx also forwards /device-admin and its children, /api/device-agent/ and /download/device-agent to the collector API.
