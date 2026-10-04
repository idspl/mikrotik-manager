# MikroTik Manager 0.6.0

[![Windows build](https://github.com/idspl/mikrotik-manager/actions/workflows/windows-build.yml/badge.svg)](https://github.com/idspl/mikrotik-manager/actions/workflows/windows-build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[Download the latest self-contained Windows EXE](https://github.com/idspl/mikrotik-manager/releases/latest/download/MikroTikManager.exe)

Standalone Windows desktop software for importing MikroTik routers from WinBox `.cdb` or legacy `.wbx` files and running scheduled, orderly RouterOS and RouterBOARD upgrades through the RouterOS API.

**Made for MikroTik.** MikroTik, RouterOS, RouterBOARD and WinBox are trademarks of MikroTikls SIA. MikroTik Manager is an independent open-source project from Indigo Data Services Pvt Ltd and is not affiliated with, sponsored by or endorsed by MikroTikls SIA.

## Open source

MikroTik Manager is published by Indigo Data Services Pvt Ltd under the MIT licence. Source, issues and build history are available at <https://github.com/idspl/mikrotik-manager>.

Permanent compiled releases and SHA-256 checksums are available on the [GitHub Releases page](https://github.com/idspl/mikrotik-manager/releases/latest).

Requirements: Windows 10/11 or Windows Server 2019+, and the .NET 8 SDK when building from source. Run `build-release.cmd` to create a self-contained `win-x64` executable in `release\win-x64`.

## Safe download and Windows SmartScreen

MikroTik Manager is currently distributed without a paid Authenticode certificate. Windows can therefore display **Windows protected your PC** and identify the publisher as **Unknown publisher**. This warning does not by itself mean the file is malicious, but only bypass it after verifying the download.

1. Download `MikroTikManager.exe` and `MikroTikManager.exe.sha256.txt` from the [official GitHub release](https://github.com/idspl/mikrotik-manager/releases/latest).
2. Open PowerShell in the download folder and calculate the executable hash:

   ```powershell
   Get-FileHash .\\MikroTikManager.exe -Algorithm SHA256
   Get-Content .\\MikroTikManager.exe.sha256.txt
   ```

3. Confirm that both SHA-256 values match exactly. If Windows renamed the download, such as `MikroTikManager (2).exe`, use that actual filename in `Get-FileHash`.
4. Optionally upload the executable to [VirusTotal](https://www.virustotal.com/gui/home/upload) for independent multi-engine scanning.
5. Only after the checksum matches, open the executable. On the SmartScreen window select **More info**, confirm the application name, and select **Run anyway**.

Do not run the file if it came from another website, the checksum differs, VirusTotal reports multiple credible detections, or the release cannot be matched to this repository. A single generic or heuristic detection can be a false positive; investigate it rather than assuming either safety or infection.

Use a test router before production deployment. Router upgrades, reboots and sensitive configuration exports can interrupt services or expose credentials if operated without appropriate controls.

## 0.6.0 health monitoring and backup usability

- New Health page: CPU, memory use, free storage, uptime, supported temperature sensors, Online/Offline/Stale status, last check and reading timestamps. Failed checks retain the old reading with its original timestamp.
- Settings → Health interval enables periodic polling while the app is open (0 = off, default). At most eight devices are checked concurrently; checks are bounded and deferred during maintenance, updates, dialogs and due schedules. This is desktop polling, not a Windows monitoring service; no retained time-series charts yet.
- Device / Interfaces adds RX/TX error and queue-drop counters to existing link, traffic, speed and optical diagnostics. Unsupported sensors/counters show Unavailable.
- Backups adds Retry Failed Backups (selected failures, or all recorded failures if nothing is selected), Open Backup Folder, Select Changed and configuration-change indicators. Changes compare the last two exports after each successful new backup, ignoring the export timestamp; comparison is limited to 4 MiB per file. Existing histories acquire indicators on their next backup. Compare Exports keeps content hidden unless explicitly revealed.
- Job labels and confirmations distinguish Backup only from Backup + upgrade. Existing jobs retain their actual type: a backup-like name does not convert an upgrade job. Upgrade safety backups remain on-router; Backup only downloads local files.
- Dashboard tables now use themed grids across their full width, removing the white trailing header areas. Navigation has consistent line icons and more spacing; light/dark appearance remains in Settings.
- Device data is read via RouterOS API only. No SSH, Winbox-port automation, PPPoE-specific requirement or native macOS build.

## 0.5.5 backup selection and maintenance controls

- Backups supports Ctrl/Shift multi-selection, Select All, Clear Selection and a selection count. Backup Selected Devices and Backup History use that page's complete selection, independently of Devices-page filters or selection.
- Coverage refresh preserves selected devices and scroll position. Compare Exports requires exactly one selected device.
- Skip Failed and Continue now matches the other maintenance buttons, with a readable label and explanation. It becomes available when an upgrade has stopped with failed and unstarted devices; it excludes completed, failed and interrupted devices. Backup-only jobs continue automatically.

## 0.5.4 independent parallel backups

- Manual and scheduled backup-only jobs start all selected devices in parallel, including previously saved schedules. Upgrade failure policies do not apply to backups.
- Connection or device timeouts fail only that device. Each device has a ten-minute backup deadline; remote cleanup is limited to ten seconds. Explicit cancellation stops the batch after active tasks finish cleanup.
- Progress and the CSV manifest show each device result, with a final successful/failed count. Parallel manifest writes and scheduled history persistence are serialized.
- Backup-before-upgrade retains the upgrade engine's safety and failure rules. Failed backup-only runs retain earlier recovery copies and recurring schedules remain scheduled.

## 0.5.3 appearance and updater

- Choose Light or Dark only in Settings → Appearance. Dark is the default for new settings and older settings without a theme; saved choices are preserved.
- Compressed standalone EXE reduces download size. Compression can add startup work; the application still includes its .NET runtime.
- Updates show downloaded size, percentage (when supplied by GitHub), average transfer speed and checksum verification status. Download and verification run away from the UI thread.
- Downloads use one asynchronous HTTP stream, not parallel segments. SHA-256 verification and the previous-EXE recovery copy remain in place.

## 0.5.2 dark mode

- Switch Light/Dark from Settings → Appearance; saved immediately and restored on startup.
- Applies to dashboard, inventory, maintenance, backups, settings, logs, menus and application dialogs without resetting selection or scroll.
- Status colours and export comparisons retain readable success/failure colours in both themes.
- Windows-provided message boxes, file pickers, date fields/calendar popups, title bars and some native control buttons/borders/scrollbars continue to follow Windows appearance.
- The branded splash screen remains dark in both modes. Saved theme choices are preserved.
- See [feature audit](FEATURE-AUDIT.md) for current coverage and candidate future work.

## 0.5.1 display scaling and layout corrections

- Explicit per-monitor DPI scaling for the main window and dialogs.
- Dashboard cards size to their content; Maintenance navigation uses a shorter label.
- Device columns fill available width, with distinct Group and Channel headings and readable minimum widths.
- Upgrade scheduling uses a resizable, scrolling form with full date/time fields and a separate button footer.
- Backup coverage summary expands above the table; date headings are readable.
- Layout regression fixtures exercise 100%, 125%, 150% and 200% scale factors. Physical mixed-DPI monitor testing is still required.

## 0.5.0 workspace and parallel maintenance

- Sidebar navigation, an inventory selection action bar, configurable/persisted column widths and visibility, clearer schedule creation, dedicated Backups workspace and live maintenance totals.
- Double-buffered fixed-height grids batch progress at 200 ms intervals. Only visible inventory rows are invalidated; progress no longer resets row bindings for each message. Upgrade log updates are batched. Jobs preserve the inventory's scroll/selection; explicit filtering changes which rows are visible.
- Upgrade preview supports concurrency (default 5 for manual upgrades; 1 preserves sequential behavior; 0 means all selected), a final-failure threshold, and an optional test router per upgrade group with approval. With test mode, groups run one at a time. Stop policies stop new starts after the first final failure. Active routers finish when paused or stopped by a failure limit; cancellation interrupts API waits but cannot undo commands sent. Use parallel mode only for independently reachable devices; dependency topology is not inferred.
- Scheduled upgrades expose concurrency. Existing schedules default to 1. Run Now uses the schedule's saved concurrency. Scheduled jobs do not prompt for test-device approval.
- Device Details (double-click or Inventory menu) reads resource, board, health and interfaces via the existing RouterOS API. Read a selected port's traffic, Ethernet link rate and SFP diagnostics where supported. Readings are on-demand snapshots with timestamps; unsupported/denied readings remain unavailable. No SSH, REST or arbitrary scripting is introduced.
- Site and tags are independent of upgrade groups. Select devices and use Site / Tags to assign in bulk; inventory search matches these fields, then Select All Visible targets the matching devices.
- Backups shows never backed up, no active schedule, overdue jobs, last attempt failures and missing local files. Refresh Coverage probes files off the UI thread. Counts may overlap. Backup comparison highlights added/removed lines, ignores export timestamps, and hides all configuration content until explicitly revealed. RouterOS version changes remain visible as changes. Large diffs use a bounded replacement comparison; files over 4 MB or 20,000 lines require an external editor. No restore action is added.
- Configuration archive schema 3 preserves site/tags and parallel schedule options; schemas 1 and 2 remain importable. Older applications cannot import schema 3.
- Validation includes synthetic dispatcher concurrency/failure/pause tests, configuration comparison and Windows workspace construction. Actual router upgrades, optics compatibility and interactive scrolling require testing on your hardware.

## 0.4.1 schedule actions

- **Schedules > Run Job Now** (also right-click): run the selected backup or upgrade immediately after confirmation, then follow Maintenance Progress. A successful one-time job becomes Completed. A recurring backup keeps its future due time; an overdue run advances to the next occurrence. Running a disabled/imported recurring backup does not enable it. Manual upgrade runs explicitly ignore the saved cutoff, as stated in the confirmation.
- **Schedules > Delete Job / Schedule** (also right-click): remove the job and its exact Windows scheduled task. Windows may request administrator approval. Cancellation or task-removal failure retains the job. Backup files, routers and maintenance history are preserved. Running jobs cannot be deleted; other operations must finish first.

## 0.4.0 scheduled backups, channels and dashboard

- **Maintenance > Schedule Local Backups**: select routers first, then choose a one-time, daily or weekly schedule, a local fixed-drive folder and retention days (0 keeps all). Edit or disable a backup schedule on the Schedules tab. Editing also enables it again with a future date. Each schedule has its own subfolder and produces encrypted `.backup`, sensitive `.rsc` exports, hashes and a manifest containing backup passwords. Protect the destination folder accordingly.
- Windows Task Scheduler runs as SYSTEM when the app is closed; the desktop runs due jobs when open and idle. Both identities need write access to the backup folder. Keep the EXE at its installed path and the PC powered on. No wake-from-sleep guarantee is provided. When the desktop catches up a missed run, it runs once and advances to the next future occurrence; it does not replay every missed day. Daily/weekly times use the PC's local timezone. A crash-interrupted job requires review and editing to re-enable it.
- Retention removes old backup-run folders only after a fully successful replacement run, only inside that schedule's subfolder. Failed runs preserve previous copies. Disable prevents further backup execution; the Windows task registration remains and can be removed in Windows Task Scheduler if no longer needed.
- **Edit Router > Upgrade channel** sets a router override. **Upgrade > Set Channel for Selected** applies it in bulk. Choose App default to inherit Settings. The inventory and upgrade preview show the choice; upgrades resolve that channel independently for each router. Changing the saved choice alone does not send a command to the router. No automatic downgrade or cross-major selection is introduced.
- **Dashboard** is the new landing page: inventory/API counts, same-major RouterOS update counts by channel, backups older than seven days or never recorded, group summaries, upcoming schedules and last-run results. Counts are last-known observations, not continuous monitoring. Development-channel updates are excluded from website comparisons. Backup age reflects recorded successful downloads; use Backup History to check that files still exist and their hashes match.
- Tables use consistent colours, readable column widths and horizontal scrolling. The schedule toolbar wraps on smaller windows. Grid selection and scrolling remain available during jobs.
- **Skip Failed & Continue** continues only unstarted routers after a failure, in original queue order, with a fresh preview. It does not retry failed or interrupted routers. Backup progress cannot be retried as an upgrade.
- Configuration exports now use schema version 2 to preserve backup schedules and router channels. This release imports legacy version 1 archives; older releases must not import version 2 archives. Imported schedules remain disabled until activated.

## 0.3.0 inventory freshness and maintenance controls

- API status resets to Not checked at startup. Saved versions remain visible with their last-check time. Settings controls the default-on startup API/version refresh (eight concurrent queries). Cancel retains successful results. Startup refresh and maintenance do not run concurrently.
- Inventory / right-click > Edit Router changes name, host, API port, group, credentials and optional critical interfaces. A blank edit password keeps the saved password; Use empty password explicitly clears it. It never changes the router's user accounts.
- Selection > Select Outdated Routers compares visible online routers against the configured website release channel and available firmware. It selects only newer versions within the same major branch, never guesses cross-major upgrades, and uses the saved version-check timestamp. Fetch versions first. Development-channel OS selection is not supported; firmware selection still works. Actual targets are resolved on the router during upgrade.
- Backup History records successful local backups created from this release onward, file paths, SHA-256 hashes and download verification. It can open the folder or recheck both hashes. It reports files removed by retention or moved to another PC; the archive does not contain backup files.
- Upgrade preview offers a test-router approval after the first successful router in each group. Failure of that test router stops the queue. Choose No to pause, then use Resume Incomplete Queue. This approval is interactive only; scheduled jobs have no approval dialogs.
- Optional maintenance cutoff applies to manual and scheduled runs. No new router starts at or after the cutoff. The current router and its retries finish. Review and resume unprocessed routers explicitly.
- Critical interface names are comma-separated per router. They must exist and be running before upgrade; after upgrade the app waits up to the configured recovery time and fails verification if they do not recover. No PPPoE-specific assumptions are made.
- Interactive completion shows Upgraded, Already current, Failed and Skipped/not-started rows. Existing Retry Failed, Resume Incomplete and saved reports remain available.
- Inventory > Export Configuration creates a password-encrypted `.mtmconfig` archive containing routers, credentials, groups, schedule definitions and settings. Encryption uses AES-256-GCM and PBKDF2-SHA256 (600,000 iterations); keep the password separately. Import validates and replaces configuration, saves a local encrypted pre-import snapshot, and closes the app to reload settings. Imported schedules receive new IDs and remain disabled. Select one in Schedules, choose a future Run at time and optional cutoff, then Activate Imported Schedule. Local backup files and Windows task registrations are not transferred.
- The open desktop app runs due scheduled jobs when idle and displays their Maintenance Progress. The Task Scheduler launcher waits for that result; if the desktop closes before the job starts, it takes over. Only one process owns maintenance and configuration writes. A job interrupted by an app crash requires review/resume and is never automatically replayed. If a headless job is already running, opening another GUI is still blocked. The waiting launcher times out after 24 hours with a failure exit code.
- The EXE remains unsigned. Windows compilation and isolated regression checks do not replace a live-router maintenance trial.

## 0.2.5 faster version checks and release visibility

- Fetch Current Versions queries up to eight routers concurrently, updates rows as results arrive, and preserves completed results when cancelled.
- The router screen shows Latest Stable, Latest Long-term and Latest Testing from MikroTik's official downloads page. It refreshes at startup or with Refresh, shows the check time, and marks retained values stale if a refresh fails. These are website channel listings, not a device-specific upgrade recommendation.
- Confirmed upgrades, retries and resumed queues automatically open Maintenance Progress. You can still switch tabs during a job.
- From 0.2.4, use Help > Check for Updates to download, verify and install this version. Older versions require manual EXE replacement.

## 0.2.4 maintenance controls and self-update

- **Right-click > Edit Credentials:** change the saved username/password, optionally test API login, then save with DPAPI encryption. A blank new-password field preserves the saved password; an explicit checkbox permits an empty password. This does not modify accounts on the router.
- **Upgrade > Pause After Current Router:** completes the current router and its configured retries, then ends the queue. Use Resume Incomplete Queue to continue after reviewing a fresh preview.
- **Upgrade > Maintenance History:** encrypted, atomically saved stage history under `C:\ProgramData\MikroTik Manager\History`. Each stage includes timestamps, attempt, versions and result. The newest run's incomplete queue is restored after restart; no router operation resumes automatically.
- Every interactive upgrade/retry/resume shows a live readiness preview and exact queue order. The RouterOS target is resolved at execution, not pinned. Safety backups are on-router `preupgrade-*` files; use Backup Selected for local copies.
- Progress shows elapsed time, numbered reconnect attempts, a stability countdown and an Already current outcome. Percentages are stage estimates, not measured download progress.
- **Help > Check for Updates:** download the official GitHub EXE and its SHA-256 checksum, verify, then confirm install/restart. Startup checks use the same flow when enabled. Updates preserve the executable path, settings and scheduled-task paths; the previous EXE is retained as `.previous`.
- The updater needs write access to the EXE folder and does not silently elevate. If access is denied, replace the EXE manually or run from an appropriate writable folder. Downloaded updates remain unsigned; a checksum is not a malware scan or digital signature.
- Only one process owns maintenance at a time. From 0.3.0 the desktop executes due schedules while open; the scheduled launcher waits for its result. Update and close actions are blocked during maintenance. Cancellation cannot undo a command already sent to a router.
- PPPoE-specific checks are intentionally excluded. This release retains general RouterOS/API checks for routers and switches.

Install 0.2.4 manually once to gain the self-updater for later releases. No change to the installed application is made until you confirm restart.

## 0.2.3 live upgrade-stage status

- Fixed the main Routers grid remaining on **Running preflight** while an upgrade continued in the background.
- The Status column now shows **Backing up configuration**, **Checking RouterOS packages**, **Upgrading RouterOS packages**, restart/reconnect waits, **Stability hold**, **Upgrading RouterBOARD firmware**, final verification and completion.
- Added explicit progress events for both RouterOS and firmware reboots, API reconnect attempts and the configured stability wait.
- The Maintenance Progress tab and main Router Status column now follow the same live maintenance stage.

## 0.2.2 streamlined router workspace

- Replaced the crowded router action buttons with compact **Inventory**, **Selection**, **Groups**, **Maintenance**, **Upgrade** and **Help** menus.
- Kept search, status, group, failure behavior and retry controls visible in a dedicated filter bar.
- Renamed **Run Preflight** to the clearer **Pre-Upgrade Check**.
- Added pre-upgrade check and backup actions to the router right-click menu.
- Reduced the toolbar height and removed the unused blank space above the router grid.
- Improved grid headers, row spacing and alternating-row colours for easier inventory scanning.

## 0.2.1 model-aware storage preflight

- Reads the model, free storage and total storage directly from RouterOS during preflight.
- Automatically requires **3 MB** free on 16–20 MB devices, **8 MB** on 21–64 MB devices and **16 MB** on devices with 65 MB or more.
- CCR models always require at least **16 MB** free.
- When total storage is unavailable, hEX and CCR model-family rules are used before the configurable fallback.
- Added a **Storage** column showing free, total and required space for each router.
- The fallback setting is only used when the router does not report enough information for an automatic rule.
- Product storage capacities can be cross-checked against MikroTik's official Product Matrix; runtime preflight uses the live router value so revised models such as hEX and hEX S are classified correctly.

## 0.2.0 maintenance control and reporting

- Added integrated pre-upgrade health checks for API access, RouterOS identity/version and configurable minimum free storage.
- Added a live **Maintenance Progress** dashboard with per-router stage, attempt, percentage, result and message columns.
- Added **stop**, **skip**, **retry then skip** and **retry then stop** failure policies, plus **Retry Failed** and **Resume Incomplete** actions.
- Every upgrade run creates credential-free CSV and PDF maintenance reports under `C:\ProgramData\MikroTik Manager\Reports`.
- Bulk backups now verify both downloaded files, calculate SHA-256 hashes, record sizes and hashes in the manifest, and apply configurable retention.
- Added instant router search and status filters; **Select All** operates on the filtered rows.
- Added a GitHub release update checker with an optional automatic startup check.
- Scheduled upgrades store their failure policy and retry count and also generate maintenance reports.

## 0.1.12 defaults and interactive job view

- New installations default to API port **8728**, API-SSL off, self-signed certificates off, a **120-second** connection timeout, a **3-minute** reboot reconnect timeout, a **10-second** stability wait and the **stable** RouterOS update channel.
- The main router grid remains enabled while checks, backups or upgrades run.
- Scrolling and row selection continue to work while live status updates arrive.
- Editable router connection fields are temporarily locked during an operation, and action handlers continue to prevent a second conflicting job.
- Right-click can select a row during a job, while its action menu remains safely unavailable until the running operation finishes.

## 0.1.11 MikroTik Manager rebrand

- Renamed the application, executable, project and splash screen to **MikroTik Manager**.
- Renamed the executable to `MikroTikManager.exe`.
- Added visible **Made for MikroTik** compatibility branding and trademark attribution.
- Migrates existing router, settings, schedule and log data from `C:\ProgramData\Indigo Router Scheduler\` to `C:\ProgramData\MikroTik Manager\` without deleting the legacy copy.
- Existing scheduled tasks created by an older executable should be recreated after installing the renamed executable.

## 0.1.10 application icon

- Added a dedicated MikroTik Manager router-and-clock icon.
- Embedded a multi-resolution icon in the Windows executable for Explorer and desktop shortcuts.
- Applied the icon to the taskbar, main window and application dialogs.
- Included the editable SVG icon source with the project.

## 0.1.9 upgrade groups

- Added persistent user-defined upgrade groups with an editable **Upgrade Group** column.
- Select multiple routers and use **Set Group** or **Clear Group**.
- Choose a saved group and click **Select Group** to highlight the complete batch.
- API status checks, version retrieval, backups, immediate upgrades and scheduled upgrades work with the selected group.
- Routers inside an upgrade group are still processed safely, strictly one at a time.
- Manual router entry now accepts an optional upgrade group.

## 0.1.8 router model and responsive upgrades

- Added a read-only **Model** column to the router list.
- **Fetch Current Versions** now retrieves the RouterBOARD model or RouterOS board name together with RouterOS and firmware versions.
- Upgrade prechecks also populate the router model.
- The complete sequential upgrade engine now runs on a worker task so reconnect waits, package checks and router operations cannot block the Windows UI message loop.
- Live upgrade logs and status updates continue to appear safely on the main window.

## 0.1.7.1 context-menu fix

- Replaced the per-click disposable router menu with one form-lifetime context menu.
- The grid now positions the menu natively at the mouse pointer.
- Fixed the `ContextMenuStrip` `ObjectDisposedException` after closing or using the menu.
- Right-clicking empty grid space no longer opens router actions.

## 0.1.7 selection, context menu and splash screen

- Replaced the persistent Run checkboxes with standard Windows row selection.
- Click one row, use **Ctrl+click** for separate routers, **Shift+click** for a range, or click **Select All**.
- API status checks, version fetches, backups, upgrades and schedules now use the highlighted rows.
- Added **Remove Selected** with confirmation.
- Right-click a router for **Check API Status**, **Fetch Current Versions** and **Remove Router**.
- Removed the WinBox-port field and column. Only the editable RouterOS API port is retained.
- Added a two-second MikroTik Manager splash screen before the main window opens.

## 0.1.6.1 build compatibility fix

- Removed the ref-struct operation from the asynchronous API file reader.
- Fixes compiler error `CS9202` when building with the .NET 8 SDK and C# 12.

## 0.1.6 API status and bulk local backups

- API-only management: no SSH, WinBox automation or RouterOS script-provisioning mode.
- Added API status checking in batches of eight.
- Added local bulk backup download. It processes selected routers sequentially and downloads both a password-encrypted binary `.backup` and a show-sensitive `.rsc` export into a dated local folder.
- The backup folder includes `BackupManifest.csv` with router results and the password required to restore each encrypted binary backup.
- Temporary `.backup` and sensitive `.rsc` files are removed from the router after download.

The downloaded `.rsc` files and `BackupManifest.csv` contain passwords and other sensitive configuration. Store the selected local folder securely.

## 0.1.5 router list and port fixes

- Removed the Group column from the router list.
- Added **Add Manually** with router name, address, port, username and password.
- Added functional ascending/descending sorting on router-grid column headers.
- Renamed **Test Selected** to **Fetch Current Versions**; it retrieves the current RouterOS and RouterBOARD firmware versions for every selected router.

## 0.1.4 STA drag-and-drop fix

- Changed the application entry point from `async Task Main` to a synchronous `[STAThread]` Windows entry point.
- Scheduled async jobs now run through a controlled synchronous bridge when the app starts with `--run-job`.
- Fixes `DragDrop registration did not succeed` and the related JIT exception.

## 0.1.3 import-button hang fix

- Removed the blocking Windows shell file picker from the Import button.
- Added a lightweight in-app path window: paste a file path or drag and drop one `.cdb`/`.wbx` file.
- Added direct drag and drop onto the main application window.
- Added stage timings to `C:\ProgramData\MikroTik Manager\Logs\import.log`.
- File existence checks, parsing and encrypted saves all run outside the UI thread.

## 0.1.2 import fixes and WBX support

- CDB parsing and encrypted saving now run away from the Windows UI thread.
- Router rows are merged in bulk, followed by one grid refresh and one encrypted save.
- Added automatic import for `.cdb` and legacy WinBox 3 `.wbx` managed-router files.
- Added strict format and length validation so damaged or unknown files show an error instead of hanging the interface.

## 0.1.1 startup fix

- Fixed an immediate startup crash caused by assigning API port `8728` before increasing the Settings control's default maximum value of `100`.
- Added a visible startup error message and `startup-crash.log` for future startup failures.

## Validated input

The CDB parser was validated against the supplied `123.cdb` file:

- 8,538-byte WinBox v3 database
- 64 router records
- all records consumed without structural errors
- names, IP addresses, usernames and passwords detected

The supplied CDB is deliberately **not included** in this project because it contains live credentials.

The legacy WBX parser is covered by a generated WinBox 3-format test fixture. Validate the first real `.wbx` import before relying on it for production because MikroTik does not publish a field-level WBX specification.

## Safe upgrade workflow

Routers are processed strictly one at a time. For every router the app:

1. Connects and reads identity, RouterOS version and RouterBOARD firmware versions.
2. Creates a password-encrypted binary backup.
3. Creates an `.rsc` export with RouterOS's default sensitive-value hiding for the pre-upgrade safety copy.
4. Sets the selected update channel and checks for RouterOS updates.
5. Installs an available update and waits up to the configured timeout for API login to succeed again.
6. Verifies the installed RouterOS version. Intermediate updates are repeated, up to four passes.
7. Runs `/system routerboard upgrade` when `current-firmware` differs from `upgrade-firmware`.
8. Reboots, reconnects, waits for stability and verifies both versions.
9. Only then continues with the next router.

The queue stops at the first failed backup, API command, reconnect timeout, RouterOS mismatch or firmware mismatch.

## Security

- WinBox CDB passwords are imported because this CDB format stores them in recoverable form.
- Imported routers, login passwords, backup passwords and scheduled job definitions are encrypted with Windows DPAPI using the local-machine scope.
- Passwords are not displayed in the grid and are redacted from application logs.
- Application data is kept in `C:\ProgramData\MikroTik Manager\`.
- Backups created on routers are password encrypted. Their password defaults to that router's imported login password; a random password is generated when the imported password is empty.
- API-SSL is supported. Plain API port 8728 remains available for existing networks.

Restrict the RouterOS API service to trusted management source addresses and firewall it from the public internet. Test API access on a non-critical router before a bulk maintenance window.

## Build a standalone EXE

Requirements: Windows 10/11 or Windows Server 2019+, and the .NET 8 SDK.

Run:

```bat
build-release.cmd
```

The self-contained single-file executable is created at:

```text
release\win-x64\MikroTikManager.exe
```

The target computers do not need .NET installed.

## First use

1. Start `MikroTikManager.exe`.
2. Open **Settings** and choose API port 8728 or API-SSL port 8729.
3. Import the WinBox `.cdb` file.
4. Correct any router that uses a custom API port; imported routers initially use the default API port from Settings.
5. Select routers using click, Ctrl+click, Shift+click or **Select All**.
6. Click **Check API Status**, then verify any failed rows.
7. Select one non-critical router and click **Fetch Current Versions**.
8. Run a test upgrade or choose a future time on **Schedules**.

Scheduled jobs are registered as elevated Windows SYSTEM tasks so they can run while the UI is closed and no operator is signed in. Do not move or rename the EXE after schedules have been created.

## RouterOS preparation

The `api` or `api-ssl` service must be enabled and reachable from this Windows computer. The RouterOS user needs permissions for API login, read/write operations, backup/export, reboot, package update and RouterBOARD upgrade.

Bulk local download uses RouterOS `/file/read` in chunks. Routers running an older release without this command are marked failed; the app does not enable FTP or SSH as a fallback.

For production, prefer a dedicated automation user and restrict both the service and firewall rule to the scheduler computer's management IP.

