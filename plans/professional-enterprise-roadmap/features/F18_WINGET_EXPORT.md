# F18 — WinGet Manifest Exporter

**Outcome:** release builds include ready-to-validate WinGet manifests.

**Design:** generate singleton/multi-file YAML with package identity, locale, architecture, URL, SHA-256, scope, upgrade behavior, installer switches and dependencies; correlate publisher/name with Add/Remove Programs.

**Acceptance:** `winget validate` passes; local-manifest sandbox install/upgrade/uninstall succeeds; hash changes regenerate manifest; multi-architecture and locale selection tests. Depends on F10 and signed release artifacts.

## Implemented slice — 2026-08-30

Added the first WinGet multi-file manifest exporter as a release metadata adapter over the existing `.bsetup` project model and F10 enterprise command contract.

- Added `WinGetManifestExporter` in Core under `Deployment`.
- Added `/WINGET=<script.bsetup>` CLI dispatch.
- Added canonical arguments and response-file fields:
  - `/INSTALLER=<file>` computes `InstallerSha256` from the local artifact.
  - `/INSTALLERURL=<url>` sets the public release URL.
  - `/SHA256=<64 hex>` supports URL-only release metadata generation.
  - `/PACKAGEID=<Publisher.Package>` overrides generated package identity.
  - `/PACKAGELOCALE=<locale>`, `/LICENSE=<value>`, `/DESCRIPTION=<value>` and `/MONIKER=<value>` populate locale metadata.
- Integrated F11 policy enforcement before manifest export.
- Generated winget-pkgs style multi-file manifests under:
  - `manifests/<first-letter>/<publisher>/<package>/<version>/<packageId>.yaml`
  - `<packageId>.locale.<locale>.yaml`
  - `<packageId>.installer.yaml`
- Added YAML schema headers so `winget validate` runs without schema-header warnings.
- Populated installer metadata from the existing installer contract:
  - `InstallerType`
  - `Architecture`
  - `Scope`
  - silent/silent-with-progress switches
  - install-location and log switches
  - repair switch
  - `InstallerSuccessCodes: 3010`
  - Add/Remove Programs correlation entry.
- Added `Beep.Installer/samples/ServiceApp.winget.response.json`.

## Implemented slice — 2026-09-01

Promoted WinGet export from “manifest files only” to a release-qualification package that can be handed to an engineer or CI runner for local manifest evidence.

- Added `winget-qualification.json` at the WinGet export root.
- Added `Qualification/Test-WinGetLocalManifest.ps1`.
- The qualification metadata records:
  - package identifier and version
  - manifest directory
  - installer type, architecture, scope, URL and SHA-256
  - validate/install/upgrade/uninstall commands
  - expected success/reboot-required exit codes.
- The generated PowerShell harness:
  - always runs `winget validate`
  - optionally runs local-manifest install, upgrade and uninstall with explicit switches
  - captures exit code, stdout, stderr, start/end timestamps and pass/fail state per step
  - writes `Qualification/winget-localmanifest-evidence.json`
  - fails closed when WinGet is missing or a step returns an unexpected exit code.
- Kept this inside the canonical `WinGetManifestExporter`; no duplicate exporter or compatibility path was added.

## Implemented slice — 2026-09-01 dependency metadata

Mapped finalized package-suite authoring into WinGet installer dependencies.

- Mandatory `[Packages]` entries with WinGet-style package IDs now emit under installer-level `Dependencies.PackageDependencies`.
- Internal package IDs that are not valid WinGet identifiers are ignored instead of polluting the public manifest.
- Optional packages are not emitted as hard WinGet dependencies.
- `winget-qualification.json` now records the same dependency list so release evidence can explain why the manifest depends on external packages.
- This uses the existing `.bsetup` package-node model and the existing `WinGetManifestExporter`; no duplicate dependency map or sidecar-only behavior was added.

## Implemented slice — 2026-09-01 multi-architecture artifacts

Replaced single-artifact WinGet export internals with a normalized installer-artifact collection while keeping one-artifact usage as the natural degenerate case.

- Added `WinGetInstallerArtifact` input records and `WinGetInstallerResult` output evidence.
- A single `/INSTALLER`, `/INSTALLERURL` and `/SHA256` still produces one manifest installer entry based on the project architecture.
- New arch-specific release inputs are supported by the existing `/WINGET` command:
  - `/INSTALLERX86=`, `/INSTALLERURLX86=`, `/SHA256X86=`
  - `/INSTALLERX64=`, `/INSTALLERURLX64=`, `/SHA256X64=`
  - `/INSTALLERARM64=`, `/INSTALLERURLARM64=`, `/SHA256ARM64=`
  - `/INSTALLERNEUTRAL=`, `/INSTALLERURLNEUTRAL=`, `/SHA256NEUTRAL=`
- Modern aliases are normalized through the enterprise command-line parser, for example `--installer-x64=`, `--installer-url-arm64=` and `--sha256-neutral=`.
- Multi-architecture exports produce one installer manifest with deterministic `Installers` order: x86, x64, arm64, neutral.
- Duplicate architecture entries fail fast.
- `winget-qualification.json` now uses `installers[]` as the authoritative release evidence shape.
- The CLI prints every emitted architecture/hash/URL tuple.

## Implemented slice — 2026-09-01 release evidence linkage

Connected WinGet manifest qualification to the existing F21/F22 evidence artifacts instead of inventing a second provenance model.

- Added WinGet qualification references for:
  - `/SBOMPATH=<spdx.json>`
  - `/PROVENANCEPATH=<provenance.json>`
  - `/SIGNINGEVIDENCE=<json>`
- Added modern aliases:
  - `--sbom-path=`
  - `--provenance-path=`
  - `--signing-evidence=`
- `winget-qualification.json` now includes a `releaseEvidence[]` array with kind, path, file name, existence, SHA-256 and size when the artifact exists.
- Missing referenced evidence files are recorded as warnings instead of silently producing incomplete release metadata.
- Response-file expansion supports the same evidence paths for CI usage.

## Implemented slice — 2026-09-01 MSIX signature metadata

Added WinGet-ready MSIX signature metadata so F17 MSIX/AppInstaller output can participate in the F18 manifest path.

- Added `SignatureSha256` support for single-artifact and multi-architecture WinGet exports.
- Added CLI and response-file inputs:
  - `/SIGNATURESHA256=`
  - `/SIGNATURESHA256X86=`
  - `/SIGNATURESHA256X64=`
  - `/SIGNATURESHA256ARM64=`
  - `/SIGNATURESHA256NEUTRAL=`
- Added modern aliases such as `--signature-sha256=` and `--signature-sha256-arm64=`.
- MSIX WinGet installer entries emit `SignatureSha256` when supplied.
- MSIX exports warn when signature metadata is missing, with guidance to use `winget hash <msix> --msix`.
- Qualification metadata mirrors each installer's `signatureSha256` value.

## Implemented slice — 2026-09-02 checked-in sample outputs

Added checked-in, metadata-only WinGet sample outputs for the release shapes the exporter supports:

- `Beep.Installer/samples/winget/exe`
- `Beep.Installer/samples/winget/msix`
- `Beep.Installer/samples/winget/msixbundle`

The samples include version/default-locale/installer manifests, `winget-qualification.json` and `Qualification/Test-WinGetLocalManifest.ps1`.

Also hardened the `/WINGET` command so `/FORMAT=exe|msix|msixbundle` is applied before export, allowing sample/release metadata to reflect the intended package format. Qualification metadata now stores the manifest folder relative to the export root, and the generated PowerShell harness resolves its manifest path relative to the `Qualification` directory so checked-in examples do not leak a developer machine path.

## Verification

- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "WinGetManifestExporterTests|EnterpriseCommandLineTests" --no-restore --nologo --verbosity quiet`
- `dotnet test Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "FullyQualifiedName~WinGetManifestExporterTests|FullyQualifiedName~EnterpriseCommandLineTests" --no-restore --nologo --verbosity quiet`
- `dotnet run --project Beep.Installer\Beep.Installer.csproj --no-build -- /WINGET=Beep.Installer\samples\ServiceApp.bsetup /POLICY=Beep.Installer\samples\enterprise-policy.sample.json /INSTALLER=Beep.Installer\bin\Debug\net10.0-windows\Beep.Installer.exe /INSTALLERURL=https://downloads.example.test/Setup-ServiceApp.exe /OUT=%TEMP%\BeepWingetSmoke /MONIKER=serviceapp /LICENSE=MIT`
- `winget validate %TEMP%\BeepWingetSmoke\manifests\t\TheTechIdea\ServiceApp\1.0.0`
- `dotnet run --project Beep.Installer\Beep.Installer.csproj --no-build -- "@Beep.Installer\samples\ServiceApp.winget.response.json" /OUT=%TEMP%\BeepWingetResponseSmoke`
- `winget validate %TEMP%\BeepWingetResponseSmoke\manifests\t\TheTechIdea\ServiceApp\1.0.0`
- `dotnet test Beep.Installer.Tests\Beep.Installer.Tests.csproj --no-build --nologo --verbosity quiet`
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "WinGetManifestExporterTests|EnterpriseProcessContractTests.WinGetCli_AppliesFormatOverrideForMsixBundleSamples" --no-restore --nologo --verbosity quiet`

## Remaining hardening

- Run the generated local-manifest install/upgrade/uninstall harness on clean sandbox devices and archive the evidence JSON.
