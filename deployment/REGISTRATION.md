# Registration and device management — release 1.3

Live registration: https://uphill-cofounder-trident.ngrok-free.dev/device-registration

Dashboard: https://uphill-cofounder-trident.ngrok-free.dev/device-admin

The dashboard account remains `root` with the configured password. Passwords and per-device credentials are not recorded in this document.

## Employee experience

1. Choose **Click to download**, download and open **MiLifeDeviceRegistration.exe**. Windows/SmartScreen may require confirmation because the executable is unsigned.
2. The app submits hardware immediately and displays a compact dialog requiring the employee's full name. There is no Skip button. Closing before completion can leave an inventory record without an employee name; it does not report registration success.
3. Click **Register**. After the server saves the name and enrollment, the app installs its per-user startup shortcut and shows **Registration successful**. It hands over to the installed agent and attempts to delete only the identical downloaded executable. Cleanup is best effort if Windows or antivirus holds the file open.
4. The installed agent remains in `%LOCALAPPDATA%\MiLifeDeviceAgent` with a visible notification-area icon. It starts at sign-in, reports availability every five minutes and polls for support requests every 30 seconds. Closing the status window keeps it running. Tray commands allow disable or exit.

The one-line management notice explains hardware, availability, Windows-permitted location and approval for support commands. No AD account or elevation is needed. It runs only while its Windows user is signed in; this is not a system service. The current collector and agent download routes serve the same combined executable. Legacy v1.1 heartbeat clients continue working; they need the new app for employee names and support.

## Dashboard

The navy dashboard lists employee names alongside device serials, supports employee search, and retains Windows usernames in hardware details. Online means a heartbeat within 15 minutes; availability does not measure employee activity.

Device details show Windows-reported latitude/longitude, accuracy and capture time. An unavailable fix is labelled unavailable; an old fix retains its timestamp. The agent uses existing Windows location permission and never bypasses it. There is no external IP-geolocation provider or inferred city. No live fix was available on the acceptance-test PC, so location capture on a permission-enabled device remains to be verified.

Under **PowerShell support**, an administrator enters a command and requests approval on the selected PC. The employee sees the exact command and requester, and must approve that command. Closing, declining or ignoring the dialog does not execute it. Requests expire after five minutes. Approved commands run with the normal signed-in token, without execution-policy bypass or elevation; elevated agent processes decline requests. Execution is limited to 60 seconds and reported output to 16,000 characters. This is one command per approval, not an unattended interactive shell. A new process is used for each command, so shell state does not persist between requests.

The database retains the requester, script, request time, approval/decline time, completion state, output and exit code. History remains visible for disabled agents. ResultUnavailable means no completion arrived within two minutes; commands are never automatically replayed. Only send commands appropriate to the employee's permissions. Revocation prevents pending approvals and future requests; it cannot undo a command already running.

## Build and deployment

- Build the combined download with `scripts/build-agent.ps1`, using `DEVICE_COLLECTOR_API_URL` and `DEVICE_INTAKE_TOKEN`. Publish the API with `scripts/publish-api.ps1`.
- `Intake__AgentPath` supplies both public download routes; the old `CollectorPath` is no longer used by those routes.
- SQLite and MySQL RegistrationManagement migrations add employee/location fields and the SupportJobs audit table. The live MySQL migration was applied with temporary schema permissions, then runtime privileges reverted to SELECT/INSERT/UPDATE/DELETE.
- `deployment/upgrade-registration.py` backs up the independent database, API and prior agent before deploying. Existing inventory service/database and ngrok routing remain unchanged.
- Support routes share the existing `/device-admin/api/` authentication/CSRF boundary. Agent routes require per-device credentials. TLS verification is enabled; no self-signed certificate installation is required.
- The EXE is approximately 73 MiB. Ngrok may download slowly; byte-range downloads are supported. A complete public binary transfer has not been verified, but server and local release SHA-256 match.

## Validation on 2026-09-18

- 60 automated tests pass (20 collector, 40 API), covering required names, location validation, device authentication, command ownership, approval/decline, replay, expiry, revocation, CSRF and audit history.
- Public Edge tests pass for simplified registration, navy dashboard login, hardware details, mobile layout, disabled-agent support history and logout.
- Live non-elevated registration rejected an empty name, saved **IT Acceptance Test**, installed the agent, reported a heartbeat and removed the downloaded test executable after handover.
- A live approved test command returned output and confirmed the process was not elevated. A declined request did not execute. Both decisions are retained as audit records.
- The acceptance-test agent was disabled and its startup shortcut removed after testing. Its name and audit records are intentionally retained for review.
- No live location fix was available; validation confirms unavailable-state handling and API coordinate validation, not physical location accuracy.


## Check-in wording and CSV export update

The public page now says **Check in your PC with us** and **Click to download**. The dashboard's **Export CSV** button downloads one row per device across all pages, respecting the current search and review-status filters. Columns include employee name, Windows user, hardware summary, availability, last heartbeat/inventory timestamps, branch and submission count. CSV exports require dashboard login, use UTF-8 with a BOM for Excel, escape quoted values and neutralize spreadsheet formula prefixes.

All 41 API tests pass after this update, including export authorization, more than one page of devices, filters, empty results and CSV escaping. A real browser downloaded and verified the live CSV and checked the updated registration page and dashboard.
## Release 1.3 — simple branded check-in and agent removal

The PC window uses the supplied miLife Insurance logo, white background, navy heading and teal **Check in** button. It shows one required name field and the agreed management notice. Successful check-in briefly replaces the form with **Check-in successful / You're all set**, then closes into the background agent.

Each agent in dashboard device details now has:

- **Disable agent:** blocks telemetry/support immediately on the server, queues a local stop, and removes the startup shortcut when received. The installed EXE, encrypted profile and logs remain. The agent exits after confirming local disable.
- **Completely remove agent:** after confirmation, queues uninstall. The agent acknowledges it, disables startup and hands off to an embedded, fixed cleanup helper. That helper waits for the agent to exit, removes its application directory (EXE, profile, logs and temporary files), checks that directory and the startup shortcut are absent, then reports completion. Credentials are read from DPAPI-protected storage, not passed on the command line. Cleanup refuses linked directories/content and never accepts a server-provided deletion path.

The UI distinguishes **Pending — waiting for PC**, **Acknowledged — confirmation pending**, **Disabled on PC**, **Removal confirmed**, and **Failed**. A failed or undelivered completion is not displayed as confirmed removal. Requester and timestamps remain in the independent database. Removal revokes that installation's credentials but retains device inventory, employee history, location history and command audit records. Windows-managed caches and OS event history are not erased.

Offline/stopped agents cannot receive a request until they run again. Disabling removes automatic startup; if removal is requested afterward, someone must launch the installed agent locally to deliver that request. Removing an active agent directly avoids this extra step.

**Existing installations must upgrade to 1.3 first.** Choose **Exit until next sign-in** in the old agent's tray menu, then run the new download. An already-registered user keeps their name and credentials; the download updates the installed copy and startup shortcut automatically. Version 1.1/1.2 installations show an update-required note and cannot execute the new actions. No remote automatic software update was introduced.

The AgentLifecycle migration adds a separate lifecycle audit table. Requests use the dashboard's existing cookie/CSRF controls. A disabled agent can authenticate to fetch only its own lifecycle action and post acknowledgement/results; this does not permit support commands or telemetry. Pending lifecycle requests block re-enrollment until resolved.

Verification: 63 automated tests pass (20 collector, 43 API). Live tests used the fixed, separate `--acceptance-test` profile directory and shortcut so the user's current installation kept running. They verified mandatory-name validation, installation/handover, disable with files retained, queued removal delivered on restart, deletion of the entire test installation and startup shortcut, and server completion confirmation. Real-browser tests cover check-in, dashboard actions, export and login/logout. The endpoint preview is saved under `artifacts/check-in-window.png`.
The final 1.3 release also passed an existing-registration upgrade/handover test and a second complete disable/removal cycle. Final executable SHA-256: CBE05752700D497CB499EE85F4F86473B7E226586F938049F74D3CC6BE04F3C9.

## Recovery patch 1.3.1 — deployed (2026-09-22)

An authenticated/CSRF-protected **Allow re-enable** action restores a disabled or revoked installation and records the administrator in lifecycle history. It supports legacy 1.2 agents, cancels an undelivered Disable request, and rejects recovery while disable acknowledgement or removal is in progress. Confirmed removed installations cannot be restored. A stopped PC still needs its app opened locally; restoring server access alone cannot start it remotely.

The 1.3.1 PC UI restores the name field and Check in button after an authorization failure so the user can retry once IT restores access. This fixes the previous hidden-button state. No new database migration is needed. 45 API tests pass, including recovery authorization, CSRF, audit, removal protection and rejection of a late acknowledgement after cancellation.

Deployed to the same verified server at its new address, `172.26.50.46`. The API and 1.3.1 download are live; this patch needs no schema migration. The previous API and agent are backed up in the private deployment directory. All 45 API tests passed. A temporary revoked 1.2 agent was restored through the public authenticated recovery endpoint, re-enrolled, sent an accepted heartbeat, and produced an administrator audit entry. A real browser verified the recovery button, device details, CSV export and logout. Temporary recovery-test records were removed; no real employee agent was re-enabled automatically. Agent SHA-256: 584191DEE021923DA0253C1412D78C319A051B674E66C83CFC84AF2F4F3E1EE5.

Recovery: dashboard → device details → **Allow re-enable**. On the PC, close any older running agent, open the 1.3.1 download and click **Check in** if prompted. Server authorization alone cannot start a stopped program or recreate its startup shortcut; the app completes that step locally.