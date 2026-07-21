# R0 — Beep.Installer Review Findings (rev. 2, 2026-07-20)

Consolidated review of `Beep.Installer` (net10.0-windows WinForms, ~13.4k LOC) plus its
relationship to BeepDM. This is the evidence base for phases P0–P9.

> **Revision note.** Rev. 1 assumed the goal was "move installer logic into BeepDM". Code
> reading disproved the premise in two ways: (1) the app does not currently work end-to-end,
> and (2) BeepDM's own design docs put packaging/authoring **out of BeepDM's scope**. This
> revision records what the code actually does and re-derives the target architecture.

---

## 1. Current state: the app does not work end to end

Three independent P0 defects, each verified in code:

### 1.1 Runtime binds to a stale assembly → `TypeLoadException`

`Beep.Installer.csproj:18-20` project-references DataManagementEngine, Winform.Controls and
Vis.Modules — but **not** `DataManagementModels.csproj` directly. Winform.Controls
(`:4720`) and Vis.Modules (`:42`) `PackageReference` the NuGet
`TheTechIdea.Beep.DataManagementModels` **3.1.1**, and `DataManagementModels.csproj:14`
declares `<Version>3.1.1</Version>` with no distinct `AssemblyVersion` — so **source and
package share one identity**. NuGet resolves nearest-to-root; the installer's Models project
node is transitive (depth 2) while the package node is direct, so the package wins:
`Beep.Installer\obj\project.assets.json:354` shows `"type": "package"` where the working
sibling `Beep.Winform.Data.Integrated.Controls` shows `"type": "project"` (`:533`).

Worse, the local feed `C:\Users\f_ald\source\repos\LocalNugetFiles` had **3.1.1 overwritten
in place** (feed nupkg dated Jul 19; extracted global-cache copy dated Jul 14). NuGet treats
versions as immutable and never re-extracts. `ISetupStateStore` was added to BeepDM source
on Jul 16 — after the cached extract — so it is absent from the loaded assembly. Result:
`System.TypeLoadException: Could not load type 'TheTechIdea.Beep.SetUp.State.ISetupStateStore'`
from `SetupWizardBuilder.Build()`. **Every install/uninstall/self-test path dies at startup.**

### 1.2 The installer never produces an `InstallConfig` — so no step can run

The installer has **zero references to `InstallConfig`** anywhere. `Program.cs:256-260`
populates `SetupContext.Properties["InstallProject"]` with an `InstallProject`. But every
BeepDM step reads `context.TryGetProperty<InstallConfig>("InstallConfig")`, and four of them
**hard-fail `Validate()`** without it: `FileCopyStep.cs:32`, `PrerequisiteCheckStep.cs:32`,
`ShortcutCreateStep.cs:31`, `VerifyInstallStep.cs:30`. `SetupWizard.Run` aborts the whole run
on a failed `Validate` (`SetupWizard.cs:186-188`).

The failure is silent by construction: `SetupContext.TryGetProperty<T>` is
`where T : class` and returns `v as T` (`SetupContext.cs:32`) — a wrong-typed or
wrong-keyed value is indistinguishable from a missing one.

**So even with 1.1 fixed, nothing installs.** The wiring only looks connected.

### 1.3 The test project does not compile

`Beep.Installer.Tests` (last touched `b408c11`, 2026-07-06) still references types renamed in
the July 9 commits: `InstallerBuilder` → `BuildPipeline` (`EndToEndTests.cs:237`,
`EdgeCaseTests.cs:163,183,203,212,226`, `InstallerBuilderTests.cs:148,159,176`) and
`BuildProgress` (`InstallerBuilderTests.cs:157,158,163`), plus `List`↔`ObservableCollection`
(`SharedFileCountTests.cs:102`, `InstallerScriptSerializerTests.cs:182`), a non-existent
`ConditionType.ArchitecturesAllowed` (`:200`), `Architecture`↔`string` and
`int`↔`CompressionStrength` conversions (`:200,271,274`, `StoreReadinessTests.cs:90`), and a
shadowed identifier (`:247`). **The "160 tests" have never run against current code.**

CI cannot catch any of this: `.github/workflows/dotnet-desktop.yml` is the unedited GitHub
template with placeholders (`Solution_Name: your-solution-name`).

---

## 2. The contract relationship (the key architectural finding)

**The mismatch between `InstallProject` and `InstallConfig` is scalar-level only.** The
collections are already the *same BeepDM types*:

| `InstallProject` member | Type | Same as `InstallConfig`? |
|---|---|---|
| `Components` (`:565`) | `ObservableCollection<InstallComponent>` | ✅ (`InstallConfig.cs:57`) |
| `Prerequisites` (`:572`) | `ObservableCollection<Prerequisite>` | ✅ (`:153`) |
| `Shortcuts` (`:579`) | `ObservableCollection<ShortcutDefinition>` | ✅ (`:112`) |
| `RegistryEntries` (`:586`) | `ObservableCollection<RegistryOperation>` | ✅ (`:102`) |
| `EnvironmentVariables` (`:593`) | `ObservableCollection<EnvironmentVariableOp>` | ✅ (`:124`) |
| `CustomActions` (`:600`) | `ObservableCollection<CustomAction>` | ✅ (`CustomActionStep.cs:168`) |

Only names differ on scalars (`AppName`→`ProductName`, `AppVersion`→`ProductVersion`,
`DefaultDirName`→`DefaultInstallPath`, `DefaultGroupName`→`StartMenuFolder`) plus the
duplicate enums `InstallationTypeEx`/`UpdateModeEx` shadowing `InstallationType`/`UpdateMode`
(`Models/InstallProject.cs:80-91`).

**Therefore a projection is cheap** — list payloads need no conversion at all. This is why
P1 is a mapper, not a migration.

### 2.1 …but the mapper alone is not sufficient

~12 of BeepDM's 22 steps read context keys that have **no home in `InstallConfig`**:
`Services`, `FileAssociations`, `FirewallRules`, `Certificates`, `FontFiles`,
`ScheduledTasks`, `Downloads`, `Bootstrapper`, `ConfigTemplates`, `ComComponents`,
`CustomValues`, `ComponentDirs`. The host must populate these directly.

Load-bearing context keys the host **must** set (else silent no-op or hard fail):

| Key | Type | Why |
|---|---|---|
| `InstallConfig` | `InstallConfig` | 4 steps hard-fail Validate |
| `InstallPath` | `string` | nothing derives it from `DefaultInstallPath` |
| `PerUser` | boxed `bool` | `InstallScope.cs:22`; otherwise everything writes **HKLM** |
| `IsSelfContained` | boxed `bool` | `PrerequisiteCheckStep.cs:71`; else hard-fails on a machine without .NET |
| `PayloadRoot` | `string` | `ResolvePayloadRoot` hardcodes the folder name `"payload"` (`ConfigManager.cs:119`), ignoring `InstallProject.PayloadFolderName` |
| `CustomActions` | `List<CustomAction>` | `CustomActionStep.cs:139` |
| `RollbackManager` | `RollbackManager` | `FileCopyStep.cs:99` — its only consumer |

Note `PerUser`/`IsSelfContained` are value types and so **cannot** be read by
`TryGetProperty<T>` (class-constrained); steps read them via `Properties.TryGetValue` +
pattern match. The host must box them under exactly these keys.

### 2.2 Large parts of BeepDM's install contract are inert

Not defects in the installer — defects/gaps in BeepDM worth knowing before designing against it:

- `InstallConfig.EnvironmentVariables` — **no step applies it**. `VerifyInstallStep.cs:69`
  reads `"EnvVarsSet"`, which nothing ever writes.
- `InstallComponent.Conditions` / `DependsOn` / `ConflictsWith` / `IncludedIn` and
  `InstallConfig.DefaultInstallType` — read by nobody. `InstallConditionEvaluator.EvaluateAll`
  exists (`InstallConditionEvaluator.cs:37`) with **zero call sites**; the UI must gate components.
- `InstallConfig.RequireAdminPrivileges` — inert; admin is only ever a warning
  (`PrerequisiteCheckStep.cs:62-67`).
- `Prerequisite.DownloadUrl` / `SilentInstallArgs` / `Id` / `HelpUrl` — unused; downloading
  lives in a **parallel model** `BootstrapperItem` (`BootstrapperStep.cs:139`).
- Duplicate `StepId` `installer.com.register` (`ComAndGacSteps.cs:22` vs
  `AdvancedSteps.cs:127`) — `SetupWizardBuilder.Build()` throws if both are added
  (`SetupWizardBuilder.cs:334-338`).
- `ShortcutCreateStep` and `InstallHelpers.RegisterFileAssociation` are **not scope-aware**
  (always per-user folders / HKCU); `RollbackManager.RegisterRegistryWrite` hardcodes HKLM
  (`RollbackManager.cs:66`).
- **No Add/Remove-Programs uninstall-key writer exists anywhere** — despite
  `InstallProject.CreateUninstallEntry`.
- No installer step honours `SetupOptions.DryRun`; none sets `SupportsRollback = true`, so
  `AutoRollbackOnFailure` produces a report of "nothing undone".
- `SetupContext.Properties` is **not persisted** in `SetupState` — a resumed wizard must
  have its whole context rebuilt by the host.
- **BeepDM's installer domain has zero test coverage.** `tests/SetupWizardTests` (~158 tests)
  covers the SetUp framework only; nothing tests `ConfigManager`, the 22 steps,
  `InstallScope`, `RollbackManager`, `UpgradeEngine`, or `InstallConditionEvaluator`.

---

## 3. BeepDM's own scope doc contradicts "move packaging into BeepDM"

`BeepDM/DataManagementEngineStandard/Services/Studio/Plans/FutureWork/installer-service/00-overview-and-scope.md`
(status: *"planning only, no code yet"*) explicitly excludes what Beep.Installer's build
pipeline does:

- `:55-61` — **"Not a packaging tool.** The installer is the *runtime* that installs/updates
  an already-packaged app. The packaging (MSI, EXE, MSIX) is done by the project's own build
  pipeline."
- `:62-64` — **"Not a code-signing service"** (verify only).
- `:93-104` non-goals: building, signing, packaging; "prerequisite checks are a small win, not a goal".
- `:242-245` — "will **not** ship as an MSI or use WiX".
- Rationale `:38-53`: the engine is in-process and must not gain a UI shell or Win32
  packaging dependencies; installer tech must stay swappable.

This directly contradicts `InstallProject`'s `OutputFormat`, `Compression`, `SingleFile`,
`SelfContained`, `Msix*` and `CodeSign*` fields — Beep.Installer is doing precisely what
BeepDM's plan calls out of scope. **Rev. 1's P2/P3 (move BuildPipeline + serializer + MSIX +
signing into BeepDM) would violate BeepDM's stated architecture.**

Note also: BeepDM has **no `.bsetup` handling anywhere** (zero matches repo-wide). Its only
programmatic `InstallConfig` builders are `ConfigManager.Load` (JSON) and
`ConfigManager.GenerateFromDirectory` (`ConfigManager.cs:155-205`).

### 3.1 Resulting target architecture

The correct split is **by lifecycle stage**, not by "push everything down":

| Concern | Owner | Rationale |
|---|---|---|
| Runtime install/uninstall/upgrade execution | **BeepDM** (`Installer/Steps`, `SetUp`) | already implemented there; installer should delegate, not duplicate |
| Runtime install contract (`InstallConfig`, branding, language models) | **BeepDM** | serialization contract with schema handshake |
| Authoring model (`InstallProject`), `.bsetup` format, build pipeline, payload packaging, MSIX/ClickOnce/signing | **Beep.Installer** (extracted to a non-UI class library) | explicitly out of BeepDM's scope; keeps BeepDM free of Win32 packaging deps |
| WinForms shell (Forms/Pages/Ui/Lang) | **Beep.Installer** | UI |

"Thin" therefore means: **thin on runtime-install logic** (delegate to BeepDM) while
legitimately owning authoring/packaging — with that authoring logic pulled out of the WinForms
exe into its own library so the exe really is just a shell.

---

## 4. Installer-side defects (unchanged from rev. 1, still valid)

| # | Defect | Evidence |
|---|---|---|
| A1 | Global mutable static | `Engine/RuntimeProjectContext.cs:8`, written `Program.cs:229,237,301,341`, read `Steps/PayloadPrepareStep.cs:132,137,152` |
| A2 | Wizard graph duplicated 3× with magic-string deps | `Program.cs:267-283, 315-320, 346-352` |
| A3 | No DI; ~30 statics; no `IDMEEditor`/`BeepService` usage at all | `new BuildPipeline()` `Program.cs:445`, `InstallerController.cs:134` |
| A4 | God classes | `InstallerScriptSerializer` 1,248; `BuildPipeline` 770; `SourceScanner` 605 |
| A5 | Duplicated logic | `ResolveOutputDirectory`/`SafeFileName` in `BuildPipeline.cs:604-621` **and** `InstallScopeResolver.cs:37-53`; `BuildPipeline.EmbedPayloadIntoExe:467` reimplements `PePayloadWriter.Embed:27`; version normalizers ×3; tool probing ×3; validation ×2; version literal ×3 |
| A6 | Swallowed exceptions | `BuildPipeline.cs:103,151,289,313,408,487,744,751` (`:289` silently drops payload files), `SourceScanner.cs:241,378,381,478,548,604` |
| A7 | Name collision | local `Engine/ClickOnce/RollbackManager` vs BeepDM `Steps.RollbackManager` (`Program.cs:264`) |
| A8 | Threading | autosave timer serializes while UI mutates (`InstallerController.cs:220`); sync-over-async (`UpdateChecker.cs:90`, `UpdateApplier.cs:50`, `PayloadDownloadStep.cs:59`) |
| A9 | Security | plaintext signing password persisted (`Models/InstallProject.cs:534` → `InstallerScriptSerializer.cs:267`); `.bsetup` detection commands executed verbatim (`PrerequisiteDetector.cs:192-207`); PowerShell string-concat shortcuts (`ClickOnce/Shortcut.cs:18-31`) |
| A10 | Stubs advertised as features | `BuildPipeline.SignExe:585` / `PackageMsix:592` are no-ops though `/FORMAT=msix` is advertised; real `SignTool`/`MsixPackager` exist unwired |
| A11 | Dead schema | `install-config.sample.json` read by no code; describes services/firewall/scheduledTasks the `.bsetup` format doesn't support |
| A12 | Duplicate enums | `InstallationTypeEx`/`UpdateModeEx` (`Models/InstallProject.cs:80-91`) with mappers (`InstallerScriptSerializer.cs:1000-1019`) |

## 5. UI/UX defects (unchanged from rev. 1)

| # | Defect | Evidence |
|---|---|---|
| U1 | Authored branding never applied at runtime | `ThemeLoader`/`BannerLoader` never called; wizard hardcodes palette (`BeepModernInstallerForm.cs:92-135`); builder collects colors/banner (`PackageBuilderForm.cs:748-750`) that go nowhere |
| U2 | Stepper omits a page; step clicks bypass validation | 7 hardcoded steps (`:52-61`) vs 8+ pages (`:307-335`); clickable jump (`:289-293`) skips `Validate()` — license/prereqs skippable |
| U3 | No real install progress | single centered label (`:437-444`) |
| U4 | Accessibility gaps | `EnsureAccessibility` only in wizard (`:85`); `IsInteractive` excludes Beep controls (`Accessibility.cs:80-82`) so Next/Back/Cancel are unnamed; builder+dialogs uncovered; focus lost on page swap |
| U5 | RTL + language switching unwired | `RtlHelper` never called; `SetLanguage` only from `Initialize()`; no end-user switcher; resx drift (en=38, others=30) |
| U6 | Hardcoded English throughout | `WelcomePage.cs:17`, `ComponentSelectionPage.cs:22-23,98,215`, `ReadyPage.cs:55-82`, `PrerequisitePage.cs:32,153,169,196,258`, `BeepModernInstallerForm.cs:301-303,421`; entire builder |
| U7 | DPI fragility | wizard pages use absolute coords; only `PackageBuilderForm:161-162` and `ComponentFilesDialog:39-40` set `AutoScaleMode.Dpi`; `LanguageManagerForm:45-71` fixed x-pixels |
| U8 | Three uncoordinated palettes, no dark mode | wizard hardcodes; `PackageBuilderForm.cs:20-28,1255-1343` statics; per-dialog inline colors |
| U9 | UI-thread blocking | `ScanAndPopulate:1631-1638`, `RefreshFileTree:1588-1600`, per-file `FileInfo.Length` (`:1664`, `ComponentFilesDialog.cs:163-169`) |
| U10 | Cancel buttons don't cancel | `BuildProgressForm.cs:88-97` CTS never reaches the pipeline; wizard/prereq/publish have no token; prereq download unbounded (`PrerequisitePage.cs:202`) |
| U11 | Developer-grade builder editors | `PropertyGrid` + autogen grid with post-hoc column hiding (`:594-605`); no undo; validation only via F7 dump (`:1435-1448`); decontextualized modals (`ComponentConditionsDialog.cs:52-55`) |
| U12 | Dead/vestigial UI | ~8 unreachable builder sections (`:272-279`); single-tab TabControl (`WizardPreviewForm:33-34`); WizardPages checkboxes only MarkDirty (`:757-766`); emoji toolbar glyphs (`LanguageManagerForm:45-71`) |

## 6. Strengths to preserve

- Clean dual-mode entry with headless CLI and deferred WinForms init (`Program.cs:34-103`).
- Self-extracting PE + solid/dedup payload design (`PePayloadWriter`, `PayloadPackager`).
- `BeepModernInstallerForm` shell layout (docking, stepper, shortcuts).
- Background `Task.Run` + `IProgress` pattern already used for build/publish.
- The `.bsetup` authoring format itself — expressive and human-readable; BeepDM has no equivalent.

## 7. Phase map

See [MASTER_TRACKER.md](MASTER_TRACKER.md). Ordering is driven by the fact that **nothing can
be validated until P0 and P1 land**.

- **P0** Build, binding, and test compilation — make it runnable and testable
- **P1** Contract bridge (`InstallProject` → `InstallConfig` + full context) — make it *install*
- **P2** Runtime thinning: delegate to BeepDM; contribute missing pieces upstream
- **P3** Authoring core: extract non-UI authoring/build logic into `Beep.Installer.Core`
- **P4** Packaging consolidation (ClickOnce/MSIX/signing) into `Beep.Installer.Packaging`
- **P5** Thin shell: DI composition root, one wizard graph, Beep logging/progress conventions
- **P6** UI/UX overhaul
- **P7** Localization, RTL, accessibility
- **P8** Security & reliability hardening
- **P9** Test migration & regression (including first-ever BeepDM installer-domain tests)
