# MikroTik Manager 0.2.3

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
