# Contributing

Contributions to MikroTik Manager are welcome.

1. Open an issue describing the change or bug.
2. Fork the repository and create a focused branch.
3. Build with the .NET 8 SDK and test on Windows with a non-production MikroTik router.
4. Do not commit WinBox databases, credentials, router exports, binary backups or logs.
5. Submit a pull request describing the behaviour and validation performed.

Router upgrades and backups affect live infrastructure. New automation must remain sequential by default, honour cancellation, verify reconnects and avoid logging secrets.
