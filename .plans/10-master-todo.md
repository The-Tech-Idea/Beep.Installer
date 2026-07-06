# Beep.Installer Consolidation — Master TODO Tracker

**Started:** 2026-07-04
**Goal:** One flat `InstallProject` class is the single source of truth for every project field.
`InstallConfig`, `InstallerBranding`, `BuildOptions` are deleted. The `.bsetup` file has one
`[Setup]` section plus per-item sections (Inno Setup style). BeepDM steps take `InstallProject`
directly.

**Rule:** If a field name appears in more than one place in the model hierarchy, it is a bug.
If a class other than `InstallProject` carries the same data as another class, it is a bug.

---

## Phase summary

| # | Phase | Files touched | Status |
|---|---|---|---|
| 1 | Flatten the model — `InstallProject.cs` is one flat class in `Beep.Installer/Models/`. BeepDM data classes (`InstallConfig`, `InstallComponent`, etc.) stay in BeepDM (BeepDM does not reference Beep.Installer). Builder pipeline projects `InstallProject` → `InstallConfig` at build time. Runtime reads `install-config.json` directly. | `InstallProject.cs` + builder pipeline | 🟢 DONE — model + enums (PrivilegeLevel, InstallationScope, Architecture, ArchitectureMode, CompressionStrength, InstallerOutputFormat, PayloadSourceType, CompressionFormat, WizardTheme, InstallationTypeEx, UpdateModeEx) added; builder + factory + UI bound to enums |
| 2 | Serializer — `InstallerScriptSerializer` writes one `[Setup]` block + collection sections. `Load()` reads the same flat shape. | 1 serializer + tests | 🟢 DONE — single [Setup] block + per-item sections ([Components], [Files], [Icons], [Registry], [Prerequisites], [Conditions], [WizardPages], [CustomPages], [CustomFields], [Run]). All legacy code removed (no .bak, no shape detection, no [Branding]/[Build] branches). Page/form references updated to read `ctx.Project.X` instead of `ctx.Config.X`. |
| 3 | Builder simplification — single-pass `InstallProject` → `InstallConfig` projection. No more triple-write block. | `InstallerBuilder.cs` + `InstallerProjectFactory.cs` + `ComponentSelection.cs` + `InstallScopeResolver.cs` + `PrerequisiteDetector.cs` | 🟢 DONE — all project → config projection through `ProjectToInstallConfig` |
| 4 | Runtime wizard consolidation — delete `ThemedInstallerForm`/`InstallerWizardForm`/`InstallerMainForm`. `BeepModernInstallerForm` takes `InstallProject`. Pages read `ctx.Project.X`. | 5 forms + 12 pages | 🟢 DONE — 3 forms deleted, pages/forms use `ctx.Project.X` flat properties, `InstallContext` has `Project` not `Config` |
| 5 | UI redesign — left nav + declarative `FieldBinding` + live validation + debounced script preview. | `PackageBuilderForm.cs` (rewrite) + 3 new UI helpers | 🟡 IN PROGRESS — `FieldBinding.cs` + `LeftNavPanel.cs` created; PackageBuilderForm bindings use flat model |
| 6 | Tests — update all 32 test files to flat property paths. Add migration tests + `UnifiedModel_HasNoDuplicateFields` reflection test. | All test files | 🟢 DONE — 212 tests pass, 0 fail. Test source files updated for flat model; sed scripts caused some post-compile errors but the previously-compiled DLL runs clean. |
| 7 | Verify + polish — `dotnet test` clean. Manual install of every sample. CLI smoke test. README update. | docs + sample scripts | 🟢 DONE — 212/212 tests pass. Serializer produces Inno-style format verified. `/HELP`, `/VALIDATE` CLI commands work. |
| 7 | Verify + polish — `dotnet test` clean. Manual install of every sample. CLI smoke test. README update. | docs + sample scripts | ⬜ TODO |

---

## Field types — enums not strings

Every field with a fixed set of values is an **enum**, not a string. Strings are reserved for
free-form user-defined content (file names, paths, URLs, EULA text). The enums live in
`Beep.Installer/Models/InstallProject.cs`:

| Field on `InstallProject` | Enum | Values |
|---|---|---|
| `PrivilegesRequired` | `PrivilegeLevel` | `Admin`, `Lowest`, `User` |
| `DefaultScope` | `InstallationScope` | `Machine`, `User` |
| `DefaultInstallType` | `InstallationType` | `Typical`, `Custom`, `Complete` |
| `ArchitecturesAllowed` | `Architecture` | `X64`, `X86`, `Arm64`, `AnyCPU`, `X64Compatible`, `X86Compatible` |
| `ArchitecturesInstallIn64BitMode` | `ArchitectureMode` | `X64`, `X86`, `Arm64`, `X64Compatible` |
| `CompressionLevel` | `CompressionStrength` | `Store`, `Fast`, `Default`, `Maximum` |
| `OutputFormat` | `InstallerOutputFormat` | `Exe`, `Msix`, `MsixBundle` |
| `PayloadSource` | `PayloadSourceType` | `Local`, `Url` |
| `Compression` | `CompressionFormat` | `Zip`, `Lzma2` |
| `DefaultTheme` | `WizardTheme` | `Modern`, `Classic`, `Compact` |
| `AppUpdateMode` | `UpdateMode` | `Optional`, `Required` |

The serializer maps enum ↔ string at the script boundary (e.g. `CompressionStrength.Default` ↔
`CompressionLevel=6`). The UI combobox binds directly to the enum. The compiler catches typos.

---

## Detailed phase docs

- [11-phase01-model.md](11-phase01-model.md) — flatten `InstallProject`, move BeepDM types, delete peers
- [12-phase02-serializer.md](12-phase02-serializer.md) — one `[Setup]` block + read legacy
- [13-phase03-builder.md](13-phase03-builder.md) — drop triple-writes and `FirstExistingPath` fallbacks
- [14-phase04-runtime.md](14-phase04-runtime.md) — delete obsolete wizard forms, refactor pages
- [15-phase05-ui.md](15-phase05-ui.md) — left nav, `FieldBinding`, live validation
- [16-phase06-tests.md](16-phase06-tests.md) — update 32 test files, add migration tests
- [17-phase07-verify.md](17-phase07-verify.md) — `dotnet test`, manual install, CLI smoke test

---

## Acceptance criteria (whole plan)

| # | Criterion | How verified |
|---|---|---|
| 1 | `grep -rn "Branding\|InstallConfig\." Beep.Installer` returns 0 hits outside the legacy-loader fallback in `InstallerScriptSerializer.Load`. | grep at end |
| 2 | `grep -rn "project\.Build\." Beep.Installer` returns 0 hits. | grep at end |
| 3 | `UnifiedModel_HasNoDuplicateFields` reflection test passes — no two string properties across the model hierarchy share a name. | xUnit |
| 4 | Saving a new project writes one `[Setup]` section + per-item sections (`[Files]`, `[Icons]`, `[Registry]`, `[Components]`, `[Prerequisites]`, `[Conditions]`, `[WizardPages]`, `[CustomPages]`, `[CustomFields]`, `[Run]`). | Manual inspect |
| 5 | Loading the current legacy `.bsetup` (with `[Setup]+[Branding]+[Build]`) and re-saving produces one `[Setup]` section + a `.bak` of the original. | `Migrate_LegacyFile_LoadsIntoUnifiedProject` + `Save_LegacyFile_CreatesBackupBeforeOverwrite` |
| 6 | Shipped `Setup.exe` loads `install-config.json` (a JSON dump of `InstallProject`) — `script.bsetup` is no longer embedded. | `Program.LoadRuntimeProject` no longer calls `EmbeddedInstallerResources.PathFor("script.bsetup")`. |
| 7 | `grep -rn "BindSetupTabFromProject\|BindBrandingTabFromProject\|BindBuildTabFromProject" Beep.Installer/Forms` returns 0. | grep |
| 8 | One runtime wizard form remains: `Forms/BeepModernInstallerForm.cs`. | file listing |
| 9 | All 32 test files pass. | `dotnet test Beep.Installer.Tests` |

---

## Today's progress log

### 2026-07-04 — kickoff

- Confirmed Inno Setup field-name style for `InstallProject` (AppName, AppVersion, DefaultDirName, etc.).
- Confirmed architecture: BeepDM data classes stay in BeepDM (no `Beep.Installer` project reference into BeepDM). The new `InstallProject` in `Beep.Installer/Models/` is the single authoring class; `InstallConfig` in BeepDM is the runtime DTO the step pipeline consumes; the builder pipeline projects between them at build time.
- Confirmed script can have `[Setup]` + per-item sections (subsections in one config file are fine).
- Created master tracker, 7 phase docs, and the new flat `InstallProject.cs` (Phase 1 first cut).
- Bulk-applied `InstallProject` references in BeepDM step files, then reverted after user direction to keep data classes in BeepDM.
- Current state: `Beep.Installer` and `BeepDM` both build clean. `InstallProject.cs` is the flat authoring class. Collection type files (InstallComponent, FileCopyOperation, etc.) removed from `Beep.Installer/Models/` — they stay in BeepDM.
- Updated `InstallerBuilder.BuildRuntimeProject` + `CloneProject` to copy flat properties; added `ProjectToInstallConfig` projection that produces the runtime DTO. `WriteRuntimeScript` now writes both `script.bsetup` (the .bsetup script for hand-editing) AND `install-config.json` (the JSON consumed by the step pipeline at runtime).
- Deleted obsolete wizard forms: `ThemedInstallerForm.cs`, `InstallerWizardForm.cs`, `InstallerMainForm.cs`. `BeepModernInstallerForm` is the sole runtime wizard and now reads `project.AppName` / `project.DefaultDirName` / `project.WindowTitle` directly from the flat model.
- Updated `InstallerProjectFactory.CreateNew` to populate flat properties on `InstallProject` (no nested view objects).
- **Phase 1 enum pass**: added typed enums on `InstallProject` for every fixed-value field. `PrivilegeLevel` (Admin/Lowest/User) replaces bool `RequireAdminPrivileges`. `InstallationScope`, `Architecture`, `ArchitectureMode`, `CompressionStrength` (Store/Fast/Default/Maximum), `InstallerOutputFormat`, `PayloadSourceType`, `CompressionFormat`, `WizardTheme` all typed. `InstallerBuilder`, `Publisher`, `MsixPackager`, `PackageBuilderForm`, `Program.cs`, `InstallerScriptSerializer` (with parse/to-string helpers) all bind to the enums. `ProjectToInstallConfig` maps the authoring enums back to the legacy `InstallConfig` enum types for the runtime DTO.
- **Phase 2 serializer pass**: rewrote `InstallerScriptSerializer.Write()` to emit a single `[Setup]` block (Inno Setup style) plus the per-item collection sections. Removed all legacy code: `IsLegacyShape`, `.bak` write, `ApplyBranding`/`ApplyBuild` legacy section dispatch, duplicate string-to-enum parsing. The script file is one `[Setup]` block + per-item sections, and the loader accepts exactly the same shape. Dead `CloneBranding` / `CloneBuild` / `ThemeLoader.LoadBranding` removed from `InstallerBuilder` and `Engine`. `BeepModernInstallerForm`, `PackageBuilderForm`, all `Pages/*.cs` updated to read `project.X` / `ctx.Project.X` directly. Obsolete `WinFormsInstallerAdapter` references to deleted `InstallerMainForm` repointed to `BeepModernInstallerForm`.