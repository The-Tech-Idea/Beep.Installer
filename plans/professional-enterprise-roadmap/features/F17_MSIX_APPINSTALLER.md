# F17 — MSIX and AppInstaller

## Explicit package identity — 2026-09-06

MSIX uses authored MsixIdentity and MsixPublisher, separate from installer AppId and display metadata. BuildPipeline no longer derives either from product/publisher display names. The existing packager rejects missing identity or publisher before staging and applies the same requirement to manifest/AppInstaller generation. Capability analysis and Store readiness share the package-name validator (3–50 ASCII letters, digits, periods or hyphens); reverse-DNS form is not required. This removes the old display-name and CN=Publisher defaults. Optional related packages may explicitly share the main publisher.

Names and certificate subjects follow [Microsoft's package identity contract](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/package-identity-overview). Generated-manifest rename continuity and no-output rejection are automated checks; actual signed Windows installation/update and Store identity assignment remain deployment qualification, not proven by source generation.

**Outcome:** harden existing MSIX generation and add managed web-update metadata.

**Design:** identity continuity, architecture/assets/capabilities validation, package signing, optional packages, AppInstaller schedule/critical/downgrade settings and explicit diagnostics for unsupported classic resources.

## Implemented slice — AppInstaller update feed metadata

- Added first-class `.appinstaller` XML generation to the existing MSIX packager.
- Reused canonical `AppUpdatesURL` as the feed/package base instead of adding a duplicate AppInstaller URL.
- Added update behavior settings to the project model and `.bsetup` contract:
  - `AppInstallerHoursBetweenUpdateChecks`
  - `AppInstallerShowPrompt`
  - `AppInstallerForceUpdateFromAnyVersion`
- Mapped `AppUpdateMode=required` to AppInstaller `UpdateBlocksActivation=true`.
- Surfaced `BuildResult.AppInstallerPath` and build summary output.
- Added headless build overrides:
  - `/UPDATEURL=`
  - `/APPINSTALLERHOURS=`
  - `/APPINSTALLERNOPROMPT`
  - `/APPINSTALLERFORCEUPDATEFROMANYVERSION`
- Kept the WinForms authoring surface canonical by placing detailed AppInstaller controls in the existing MSIX section only.
- Updated linter and JSON schema support for the new AppInstaller project fields.

## Implemented slice — MSIX capability gate and evidence report

- Added a canonical pre-package MSIX capability analyzer.
- Blocked MSIX builds when the project contains classic installer resources that the current MSIX exporter cannot represent:
  - prerequisite chains/catalogs/package nodes
  - deployment supersedence rules
  - registry writes
  - environment variables
  - arbitrary shortcuts
  - Windows services
  - scheduled tasks
  - firewall rules
  - file associations
  - certificates
  - COM/GAC registration
  - drivers
  - configuration transforms
  - IIS pools/sites
  - custom actions
  - component selection/condition semantics
- Emit `msix-capabilities.json` beside MSIX build output so CI/admin tools can see capability warnings/errors and AppInstaller settings.
- Surface capability diagnostics through `BuildResult.Errors`/`BuildResult.Warnings` instead of silently dropping unsupported intent.

## Implemented slice — MSIX/AppInstaller signing evidence

- Reused the existing `CodeSigningService` for MSIX/AppInstaller signing instead of adding a parallel signer.
- Added `BuildResult.SigningEvidence` for release artifact signing records.
- Added an injectable MSIX package service seam so deterministic packaging/signing tests do not depend on local Windows SDK availability.
- When a code-signing certificate is configured, successful MSIX builds now sign:
  - the generated setup EXE
  - the generated `.msix`
  - the generated `.appinstaller` feed, when present
- Signing evidence is written into `msix-capabilities.json` without persisting resolved secret-reference values.
- A requested MSIX output now fails if the package is not produced, rather than succeeding as only a custom EXE plus staging folder.

## Implemented slice — MSIX bundle and optional related packages

- Promoted package-vs-bundle from an advertised output string to a canonical packager contract.
- `OutputFormat=msixbundle` now produces a `.msixbundle` target path and runs MakeAppx in bundle mode.
- AppInstaller feed generation now follows Microsoft’s package-kind shape:
  - `.msix` outputs emit `<MainPackage>` with `ProcessorArchitecture`.
  - `.msixbundle` outputs emit `<MainBundle>` without package-only architecture metadata.
- Added first-class `.bsetup` authoring for AppInstaller related-set optional packages through `[MsixOptionalPackages]`.
- Optional related packages support package or bundle entries and round-trip through:
  - `InstallProject.MsixOptionalPackages`
  - `InstallerScriptSerializer`
  - `ProjectScriptLinter`
  - `bsetup-1.0.schema.json`
- Capability analysis now blocks invalid optional-package authoring when no AppInstaller feed is configured or required identity/URI fields are missing.
- `msix-capabilities.json` includes optional package metadata so release reviewers can inspect the related set.

## Implemented slice — AppInstaller update channels

- AppInstaller feed selection now honors the selected canonical `AppUpdateChannel`.
- When a selected update channel has `FeedUrl`, that feed URL becomes the AppInstaller/package endpoint base.
- Critical update channels set AppInstaller `UpdateBlocksActivation=true` without requiring duplicate update-mode logic.
- The MSIX capability analyzer treats selected channel `FeedUrl` as a valid AppInstaller feed source.
- Signed update channel feed metadata can be exported and verified beside AppInstaller release artifacts with the shared RSA-SHA256 detached-signature trust path.
- `msix-capabilities.json` includes:
  - selected channel id/name/ring/feed URL
  - rollout percentage
  - deterministic rollout decision with privacy-preserving cohort hash and bucket
  - minimum version
  - deadline
  - critical flag
  - maintenance window
  - rollback version
  - revoked flag
  - all declared channels

## Implemented slice — MSIX/AppInstaller Intune ingestion readiness

- `msix-capabilities.json` now includes an `intuneIngestion` section for MSIX and MSIX bundle releases.
- The report records the intended Intune deployment model, package kind, recommended install command, recommended assignment intent, package/AppInstaller artifact paths and derived feed/package URIs.
- Critical AppInstaller update channels now recommend a required Intune assignment even when the general update mode is optional.
- The readiness checks validate package artifact presence, MSIX identity, signing evidence, AppInstaller feed artifact presence, AppInstaller signing evidence, update interval configuration and optional-package feed coverage.
- This keeps MSIX/AppInstaller ingestion review on the existing MSIX capability report path instead of adding a duplicate deployment-kit generator.

## Remaining hardening

- Clean VM install/update/downgrade/uninstall evidence.
- Execute the completed MSIX/AppInstaller Intune readiness contract against real tenant packaging/upload flows and archive the evidence.

**Acceptance:** package validation, install/update/downgrade/uninstall on clean VM; signature trust tests; Intune ingestion; clean uninstall; capability report blocks misleading packages. Depends on F02 and F21.
