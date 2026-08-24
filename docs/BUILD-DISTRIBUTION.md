# Desktop distribution builds

GitHub Actions builds the Avalonia desktop application on every pull request,
when started manually, and for a timestamp release tag in the form
`yyyy.MM.dd.HH.mm.ss`.

Each run first restores, builds, and executes the real integration paths:

- SQLite/filesystem cataloguing;
- metadata and BLAKE3 analysis;
- remote-agent contracts and thermal control; and
- desktop integration tests.

Only after those checks pass, the workflow publishes a self-contained desktop
application for the following supported targets:

| Target | Download | How to use it |
| --- | --- | --- |
| Debian/Ubuntu amd64 | `.deb` | Install with `sudo apt install ./magnetar-photo-heimdall_..._amd64.deb`, then launch **Magnetar Photo Heimdall** from the application menu. |
| Windows x64 | `.zip` containing `Magnetar.Photo.Heimdall.Desktop.exe` | Extract the archive and run the `.exe`. |
| macOS Apple Silicon | `osx-arm64.tar.gz` | Extract it, then run `./Magnetar.Photo.Heimdall.Desktop` from Terminal. |
| macOS Intel | `osx-x64.tar.gz` | Extract it, then run `./Magnetar.Photo.Heimdall.Desktop` from Terminal. |

Workflow artifacts are retained for 30 days. A timestamp tag produces the same
four downloadable workflow artifacts; publishing a GitHub release remains an
explicit, separately authorized operation.

## Deliberate unsigned status

The workflow does **not** sign or notarize Windows or macOS binaries because no
signing certificates, Apple Developer credentials, or notarization secrets are
stored in this repository. Windows SmartScreen and macOS Gatekeeper may warn
about these unsigned builds. This is expected for internal/test distributions;
do not bypass platform security controls for an artifact whose source you do
not trust.

Before a public release, add protected repository secrets and a dedicated
signing/notarization workflow. Packaging remains intentionally separate from
that future credentialed release step.
