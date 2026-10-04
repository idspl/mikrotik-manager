# Feature audit — 4 October 2026

Compared the public [mikr.app roadmap](https://mikr.app/roadmap.html) with Indigo MikroTik Manager's source at v0.5.1, plus the appearance changes through v0.5.3. The reference lists 155 entries: 128 shipped, four planned and 23 ideas. These are that product's labels, not Indigo delivery commitments. Related entries are grouped below; no feature-parity percentage is claimed.

**Present** means our implementation provides the capability. **Partial** means our version is narrower. **Pending** means no implementation was found. Some differences are intentional: this is an API-only Windows application, not a shared web server.

| Area | What our software provides | Limit or remaining work |
|---|---|---|
| Inventory | Present: CDB/WBX import, manual add/edit, credentials editing, search, status/group filtering, sorting, configurable columns, selection actions. | No subnet scanning, passive discovery, disabled-device state or automatic connection test when adding. Connection fields are edited one router at a time; there is no per-field bulk connection editor. |
| Organisation | Partial: independent site and tag text fields, bulk replacement, search across both; upgrade groups with bulk assignment. | No dedicated site administration, nested folders, attachments, tag autocomplete or multi-tag picker. |
| Dashboard | Partial: fleet totals, upgrade groups, schedule overview and an on-demand fleet refresh. | Counts refresh from saved in-memory observations; there is no continuous per-router polling, configurable widgets, clickable count filters or site-sorting controls. |
| Freshness | Partial: API state resets on opening; timestamps and saved-version notices distinguish old information. | No age-based fading/badge, and cached resource metrics are not a continuous live view. |
| Device detail | Partial: resource/health snapshots and a plain interface table; manual port-rate and supported SFP readings. | No time-series storage, charts, optical history or port diagrams. |
| Network inspection | Pending. | Neighbours/topology, bridging/VLAN membership, STP, MAC/ARP/NDP tables, DHCP pools/leases, firewall/queue views and routing/VPN diagnostics. |
| Radio/cellular | Pending. | Wi-Fi client details, CAPsMAN, roaming, LTE signal/usage/SMS/modem maintenance and geolocation. A wireless interface appearing in the generic table does not supply these capabilities. |
| Configuration editing | Intentionally limited: fixed maintenance operations through RouterOS API. | Arbitrary command consoles, templates, script deployment and CLI translators remain excluded by the earlier product decision. VLAN/service/DHCP configuration editors, compliance checks and rule profiles are also absent; these would require a separate approved scope. |
| Router maintenance | Present: combined RouterOS/RouterBOARD workflow, preflight, safety backup, reconnect/version verification, stability wait and configured critical-interface checks. | No separate standalone reboot or independent OS-only/firmware-only action was found in the current UI. LTE modem firmware is a different operation and is absent. |
| Batch execution | Present: sequential or bounded parallel execution, all-at-once option, groups, canary approval for interactive runs, retries/failure limit, pause, resume and skip-failed controls. | Queue order is not derived from PoE/topology dependencies. Schedules and groups are reusable targeting mechanisms, not a dedicated draggable queue-preset library. |
| Scheduling | Present: one-time upgrade jobs, Run Now/Delete, maintenance cutoff and local backups once/daily/weekly. | No automatically inherited site schedules or arbitrary hourly recurrence. Jobs save specific router IDs; adding a router to a site does not add it to an existing job. |
| Backup creation | Present: sensitive RSC plus encrypted binary backup, bulk download into local dated folders, saved hashes and file verification. | No ZIP export or deduplication. A latest-only mirror for external sync is separate from the existing dated folders and is absent. |
| Backup lifecycle | Present: history, coverage dashboard, retention by days and comparison of recorded exports with timestamp-only changes ignored and contents hidden by default. | No count-based retention, automatic change notifications or guided restore. Coverage has overlapping warning categories, not mutually exclusive totals. |
| Version information | Present: installed OS/firmware, per-router update channel, published Stable/Long-term/Testing and application self-update. | No continuous release notifications; development can be an upgrade choice but is not displayed in the three-channel banner. |
| Data protection | Present: Windows DPAPI-protected stored credentials, passphrase-encrypted configuration archive, optional validated API-SSL. | This is not a web login or a multi-user permission system. Archive import replaces inventory/settings; it does not merge a chosen subset with duplicate detection. |
| Shared access | Not part of the current desktop architecture. | Roles/site permissions, central user audit, MFA/passkeys/directory login, a remotely callable manager API and its access keys would require a server design. Connecting to RouterOS API is not the same capability. |
| Security analysis | Pending. | Vulnerability/release-security assessment, configuration-risk scanning, intrusion detection and automatic blocking. API connection errors are recorded, but there is no distinct network-reachable/management-unavailable state. |
| Integrations | Pending. | External alerts/webhooks, metrics export and syslog ingestion. Live Log currently records this application's work, not the router fleet's syslog. |
| Appearance | Present through v0.5.3: saved Light/Dark preference in Settings, themed application views and dialogs. Dark is the default; saved theme choices are preserved. | Native Windows chrome and system dialogs retain Windows appearance. No mobile/web client, localisation, dedicated feedback/changelog modal or global shortcut scheme. |
| Operation visibility | Partial: Maintenance page, aggregate stages, logs, reports and persisted interrupted-job/history handling. | No globally visible active-task drawer. Closing the app during an active interactive operation is prevented; it is not a browser-independent server session. |

IPv6 management has not been validated end-to-end; generic hostname/address acceptance is not evidence of complete dual-stack parity.

## Suggested order after this release

1. Read-only switch diagnostics: port health, neighbour links, bridge/VLAN membership and MAC lookup. These suit the switch and customer-end devices in this fleet.
2. Background polling with visible freshness, opt-in intervals and bounded concurrency; then retained resource/traffic/optical trends.
3. Backup change detection, count retention, ZIP export and inherited site schedules.
4. Dependency-aware maintenance and a clear manual reboot action with confirmation.
5. Optional external notifications and security advisories.

Only dark mode is included in v0.5.2. The items above are candidates, not implemented or scheduled commitments. PPPoE session recovery is not treated as a universal health requirement.

## Source verification

Inventory/UI: `MainForm.cs`, `MainForm.Workspace.cs`, `MainForm.Inventory.cs`, `ManualRouterDialog.cs`. Device reads: `MainForm.DeviceDetails.cs`. Maintenance: `UpgradeEngine.cs`, `BatchUpgradeQueue.cs`, `MainForm.Maintenance.cs`, `MainForm.Rollout.cs`. Scheduling: `BackupSchedulePolicy.cs`, `MainForm.BackupSchedules.cs`, `MainForm.ScheduleActions.cs`. Backup handling: `RouterBackupService.cs`, `MainForm.Backups.cs`, `ConfigDiff.cs`. Protection/transport: `SecureStore.cs`, `ConfigurationArchive.cs`, `RouterOsApiClient.cs`. Appearance: `AppTheme.cs`, `MainForm.Theme.cs`, `DpiDialog.cs`.
