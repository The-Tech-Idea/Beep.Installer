# F25 — Professional Authoring UX and Templates

## Persistent product identity — 2026-09-06

The existing Identity section exposes AppId through the shared bound-field helper, with guidance that updates and renames retain the same GUID. Shared text fields receive explicit accessible names and stable property-based control names. New projects generate an ID once; loading and validation never invent ownership. Shared project validation and the published canonical JSON schema require a nonzero hyphenated GUID before plan compilation. Both checked-in root installer scripts have explicit distinct IDs. Rendered assistive-technology qualification remains separate from automated editor checks.

**Outcome:** complex projects are discoverable and safe to author.

**Design:** searchable authoring navigation, package format readiness, guided advanced-resource editors, grouped validation center, keyboard-only walkthrough, condition-builder polish and reusable signed templates are implemented on the canonical Package Builder paths; keep controller thin.

**Acceptance:** reference enterprise project can be authored without raw file editing; keyboard-only workflow; invalid values cannot build; template update shows diff; GUI and CLI produce the same plan hash. Depends on F01/F02 and provider schemas.

## Implemented slices

- Built-in project templates now fail closed for unknown IDs instead of silently falling back to an empty project.
- The New Project dialog selection now flows through `InstallerController.New(templateId, ...)`, so choosing Console, WinForms, WPF or Windows Service actually shapes the created installer project.
- Added `ProjectAuthoringWorkspace` as the canonical authoring evidence surface:
  - creates strict validation snapshots from the current `InstallProject`;
  - reuses `ProjectSchemaService`, `ProjectCanonicalJsonExporter`, `InstallerScriptSerializer` and `InstallPlanCompiler`;
  - exposes canonical JSON, `.bsetup` script text, compiled plan JSON, plan hash and schema diagnostics without duplicating model or validation logic.
- Added template update preview support through `ProjectAuthoringWorkspace.PreviewTemplateUpdate`, which compares the current project to a selected built-in template using canonical JSON and plan hashes, then returns path-level diff entries.
- The Package Builder validation action now writes schema version, plan hash and canonical JSON size into the build/validation log, giving the GUI the same plan-hash evidence used by CLI plan output.
- Added the Package Builder template update flow:
  - `TemplateUpdateDialog` lets authors choose a built-in template and review the canonical diff before applying it.
  - The dialog shows current/updated plan hashes, change count and updated diagnostics from `ProjectTemplateUpdatePreview`.
  - Applying the template uses `InstallerController.ApplyTemplateUpdate`, replaces the project with the same canonical candidate previewed by the dialog, marks it dirty and refreshes the Package Builder.
  - `Ctrl+T` and the toolbar `Templates` action open the update flow.
- Added searchable Package Builder navigation:
  - `LeftNavPanel` now owns its section/item model and filters it directly instead of using a separate navigation launcher.
  - Section search matches group label, item id, visible label and authoring hints.
  - The Package Builder sidebar has a `Search sections...` box; Enter selects the first visible match and Escape clears the filter.
  - `Ctrl+F` focuses the navigation search box for keyboard-first authoring.
- Added the Package Builder Format Readiness pane:
  - `PackageFormatCapabilityReporter` creates one canonical authoring report for EXE, MSI, MSIX, WinGet and Intune/ConfigMgr.
  - EXE readiness reuses strict `ProjectAuthoringWorkspace` diagnostics and plan hash evidence.
  - MSI readiness invokes the existing `MsiPackageExporter` in report mode and maps its capability findings.
  - MSIX readiness reuses `MsixProjectCapabilityAnalyzer`.
  - Intune/ConfigMgr readiness invokes the existing `EnterpriseDeploymentKitGenerator` and reports generated deployment-kit assets.
  - WinGet readiness is aligned with the existing manifest exporter inputs, surfacing package metadata and release-artifact evidence requirements before export.
- Added guided advanced-resource editors in Package Builder:
  - The searchable left nav now includes an Advanced Resources section.
  - Editors cover scheduled tasks, firewall rules, certificates, COM registrations, driver packages, config transforms, IIS app pools, IIS sites and Web Deploy packages.
  - Each editor binds directly to the existing `InstallProject` collection for that resource family, so `.bsetup`, schema, compiler and exporter flows continue to use the canonical model.
  - The shared editor pattern provides list grid, property-grid details, Add, Duplicate and Remove actions.
  - Duplicate cloning handles list and dictionary properties without sharing mutable collection references.
- Added grouped Validation Center output in Package Builder:
  - `ProjectValidationCenter` reuses strict `ProjectAuthoringWorkspace` snapshots and existing `ProjectSchemaService` diagnostics.
  - Findings are grouped by authoring area, including advanced resources such as firewall rules and IIS sites.
  - Each finding surfaces severity, diagnostic code, path, message and fix text in the Build Workflow validation pane.
  - Programmatic left-nav selection now fires the same section-selection path as user navigation, making validation/build navigation deterministic.
- Added keyboard-only Package Builder walkthrough:
  - Build Workflow now includes a Keyboard Walkthrough pane documenting the full authoring path.
  - `Ctrl+F` focuses nav search; Enter opens the first visible match; Escape clears search.
  - `Ctrl+Tab` and `Ctrl+Shift+Tab` move through visible left-nav sections with deterministic `LeftNavPanel.SelectAdjacentSection` state.
  - `F1` help now includes the keyboard path plus template, preview, validate and build shortcuts.
  - Focused UI tests cover walkthrough text and adjacent left-nav selection.
- Polished the canonical component condition builder:
  - The existing `ComponentConditionsDialog` now exposes `All`, `Any` and `Not` group expression modes directly instead of hiding expression authoring behind raw script edits.
  - Guided rule buttons add common architecture, admin and file-existence gates to the component's existing `InstallCondition` list.
  - The dialog now shows a plain-language preview explaining how grouped boolean expressions evaluate and lists validation issues inline.
  - Binding is initialized once and refreshed deterministically when authors switch components or add rules.
  - Focused tests cover grouped-expression preview text and validation issue surfacing.
- Added reusable signed project-template packages:
  - `ProjectTemplatePackageService` exports built-in templates as a canonical bundle with `template.project.canonical.json`, `beep-project-template.json` metadata and a detached RSA-SHA256 signature.
  - Template metadata records template id, name, category, description, issuer, creation time, canonical project hash, signature algorithm and signing key fingerprint.
  - Verification requires a trusted public key by default, validates the detached signature and fails closed when canonical template JSON is tampered.
  - The service reuses `ProjectTemplates`, `ProjectCanonicalJsonExporter` and `RsaSha256DetachedSignatureVerifier`; it does not introduce a second template model.
  - Focused tests cover signed export/trust verification and tamper detection.
- Added Build Workflow release-readiness summary:
  - `PackageFormatCapabilityReport` now carries one derived release-readiness status plus ready/warning/blocked format counts.
  - The Package Builder Format Readiness pane surfaces that summary before listing EXE, MSI, MSIX, WinGet and Intune/ConfigMgr details.
  - The summary is derived from existing package-format capability findings, so it does not introduce a duplicate readiness model.
- Added headless format-readiness parity:
  - `/FORMATREADINESS=<script.bsetup> [/JSON] [/OUT=<json>]` emits the same `PackageFormatCapabilityReport` used by the Package Builder.
  - The command exits non-zero when any format is blocked, making the authoring readiness surface usable in CI and release checklists.
  - Response files and modern aliases support `formatReadiness` and `--format-readiness=...`.
- Added headless signed-template package parity:
  - `/LISTTEMPLATES [/JSON] [/OUT=<json>]` lists built-in template ids, names, categories and descriptions from `ProjectTemplates.Builtins`, making signed template export discoverable and archiveable without adding a separate catalog model.
  - `/EXPORTTEMPLATEPACKAGE=<template-id> /TEMPLATESIGNKEY=<pem> [/OUT=<dir>] [/JSON]` exports the same signed reusable template package created by `ProjectTemplatePackageService`.
  - `/VERIFYTEMPLATEPACKAGE=<dir> /TEMPLATETRUSTKEY=<pem> [/JSON]` verifies the package manifest, canonical template hash and detached signature.
  - Response files and modern aliases support template listing, export/verify commands plus template product/version/publisher/source/issuer/signing/trust-key fields.

## Remaining hardening

- No F25 implementation gaps remain in the current roadmap slice.
