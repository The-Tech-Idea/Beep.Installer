# Phase 3: Authoring Core — Extract to `Beep.Installer.Core` — Design Document

**Status:** ⬜ not started · **Priority:** P1 · **Depends on:** P1 (P2 parallelizable)
**Tracker:** [MASTER_TRACKER.md](MASTER_TRACKER.md) · **Evidence:** [R0_REVIEW_FINDINGS.md](R0_REVIEW_FINDINGS.md) §3.1, §4 (A4–A6, A10)

> Replaces the rev. 1 doc "Build & Payload Engine → BeepDM". BeepDM's scope doc explicitly
> excludes packaging and signing (R0 §3), so the destination changes from BeepDM to a new
> non-UI library in this repo. The *thinning* goal is unchanged: this logic leaves the
> WinForms exe.

## 0. Problem Statement

~5,000 LOC of authoring/build logic lives inside the WinForms executable, none of it
WinForms-dependent:

- `Engine/BuildPipeline.cs` (770) — a god class doing validation, payload staging,
  `dotnet publish` shelling, Win32 icon embedding (P/Invoke `:574-583`), zip, PE append,
  cleanup — plus **no-op stubs** for signing (`:585`) and MSIX (`:592`) while the CLI
  advertises `/FORMAT=msix` and real `SignTool`/`MsixPackager` sit unwired.
- `Engine/InstallerScriptSerializer.cs` (1,248) — the `.bsetup` format, the only authoring
  contract. BeepDM has no `.bsetup` handling at all (zero matches repo-wide).
- `Engine/SourceScanner.cs` (605) — PE metadata classification, `deps.json` resolution,
  prerequisite suggestion.
- `PayloadPackager`, `PePayloadWriter`, `EmbeddedInstallerResources`, `GlobMatcher`,
  `ProjectTemplates`, `InstallerProjectFactory`, `AutoSave`, `RecentProjects`, `Diag`.

Plus the duplication and silent-failure defects from R0 §4: `ResolveOutputDirectory` /
`SafeFileName` in two places (`BuildPipeline.cs:604-621`, `InstallScopeResolver.cs:37-53`),
PE embed reimplemented inline (`BuildPipeline.cs:467` vs `PePayloadWriter.cs:27`), tool
probing in three places, validation in two, version literal in three, and empty `catch {}`
at `BuildPipeline.cs:103,151,289,313,408,487,744,751` — including `:289`, where payload files
are silently dropped from the built installer.

## 1. Goals

1. A new `Beep.Installer.Core` class library (net10.0, **no** `UseWindowsForms`) owns all
   authoring/build logic. Acceptance: the exe project contains only `Forms/`, `Pages/`,
   `Ui/`, `Lang/`, `Hosting/`, and UI helpers.
2. `BuildPipeline` decomposed into ordered, single-purpose stages behind `IInstallerBuilder`.
   Acceptance: no stage file exceeds ~150 lines; a `/BUILD` of `samples/HelloApp` produces a
   working Setup.exe.
3. Real cancellation: `CancellationToken` threaded through every stage. Acceptance:
   cancelling mid-build stops within ~2s and leaves no partial output.
4. No silent failures: every catch logs and records, or rethrows. Acceptance: zero empty
   `catch {}` in moved code; a payload file that fails to stage fails the build.
5. Signing and MSIX are wired or fail loudly — never silent no-ops. Acceptance:
   `/FORMAT=msix` either produces an `.msix` or exits non-zero with the probed tool paths.
6. `.bsetup` round-trips byte-identically. Acceptance: golden-file test on both sample
   scripts.

## 2. Design

### 2.1 Project layout

```
Beep.Installer.Core/                       (net10.0, no WinForms)
├── Beep.Installer.Core.csproj             → refs DataManagementModels + DataManagementEngine
├── Model/InstallProject.cs                (moved from the exe; still authoring-owned)
├── Model/CustomWizardPage.cs
├── Contracts/IInstallerBuilder.cs, IInstallerScriptSerializer.cs,
│             ISourceScanner.cs, InstallerBuildResult.cs, SourceScanResult.cs
├── Authoring/BsetupSerializer.{Read,Write,Mappers}.cs
├── Authoring/SourceScanner.cs, GlobMatcher.cs, ProjectTemplates.cs,
│             InstallerProjectFactory.cs, InstallerProjectValidator.cs
├── Authoring/RecentProjects.cs, AutoSavePolicy.cs
├── Runtime/InstallConfigProjector.cs, InstallContextBuilder.cs   (from P1)
└── Build/InstallerBuilder.cs + Stages/*.cs, PayloadPackager.cs,
          PePayloadWriter.cs, ToolLocator.cs, EmbeddedInstallerResources.cs
```

`InstallProject` stays the authoring model and moves here, **not** into BeepDM — it carries
build-pipeline concerns (`OutputFormat`, `Compression*`, `Msix*`, `CodeSign*`,
`SourceIncludes/Excludes`) that BeepDM's scope doc excludes. The P1 projector converts it to
BeepDM's `InstallConfig` at the runtime boundary.

### 2.2 Pipeline decomposition

`InstallerBuilder : IInstallerBuilder` runs ordered stages, each
`IBuildStage { IErrorsInfo Execute(BuildContext ctx, CancellationToken ct); }`:

1. `ValidateStage` — the single `InstallerProjectValidator` (replaces both
   `BuildPipeline.ValidateProject:249` and `InstallerController.Validate:143`, plus the
   validators extracted from the model: `ValidateCustomActions:624`,
   `ValidateCustomFields:645`, `ExpandCustomMacros:665`).
2. `StagePayloadStage` — per-file errors recorded; **no silent drops** (fixes `:289`).
3. `WriteRuntimeArtifactsStage` — `script.bsetup` + `install-config.json` (from P1).
4. `PublishHostStage` — `dotnet publish`; async process wait honoring `ct`, replacing the
   `WaitForExit(300_000)` + `stdoutTask.Wait(2000)` truncation race (`:406-412`).
5. `EmbedIconStage` — the Win32 `UpdateResource` P/Invoke, isolated and optional.
6. `CompressPayloadStage` — `PayloadPackager` (solid/zip/LZMA); reuses BeepDM
   `FileHelper.CompressFile/ExtractZipFile` where equivalent.
7. `EmbedPayloadStage` — calls `PePayloadWriter.Embed`; deletes the inline duplicate (`:467`).
8. `SignStage` — real `SignTool.Sign` when configured; cert secret resolved via the P8
   reference scheme (interim: current field with a loud warning).
9. `PackageFormatStage` — `exe` passthrough, or `MsixPackager.Package` +
   `StoreReadinessChecker` warnings.
10. `CleanupStage` — explicit, driven by result state (removes the stale
    "non single-file mode" branch at `:204-206`).

`BuildContext` carries project, options, temp dirs, and accumulated warnings/errors.
Progress standardizes on BeepDM's `IProgress<PassedArgs>`, replacing the ad-hoc
`BuildPipeline.BuildProgress` record struct (`:71`) and `Publisher`'s `(int,string)` tuples.

### 2.3 Shared helpers (kills the triplication)

`ToolLocator` — one PATH + Windows-Kits probe serving `SignTool.Find` (`:10`),
`MsixPackager.FindMakeAppx` (`:120`) and `PayloadPackager.FindSevenZip` (`:130`), returning
the probed paths for error messages.
`VersionNormalizer` — one 4-part normalizer replacing
`ApplicationManifestWriter.cs:74`, `MsixPackager.cs:227`, `UpdateChecker.cs:105`; comparison
delegates to BeepDM `SemVer.Compare`.
`SafeFileName`/`ResolveOutputDirectory` — single copy.
Version string — read from assembly metadata, never a literal (removes the `AppInfo`,
csproj, `BuildPipeline.cs:329` triplication).

### 2.4 Logging

`Engine/Diag` becomes a `IDMLogger` adapter (BeepDM convention) rather than a bespoke sink;
the moved code takes an injected logger, so the swallowed-exception sites become
`LogWarning` + a recorded build warning.

### 2.5 The exe after this phase

`InstallerController` splits: engine calls go through the P3 interfaces (resolved by DI in
P5); the WinForms parts (`MessageBox`, `ConfirmDiscardChanges:196`, `WizardPreviewForm`
launch `:186-190`) stay in a slim shell controller.

## 3. API surface

New library, versioned with the installer. Not published to NuGet initially (Open Decision
D9 if it should be).

## 4. Files to change

| Action | File | Lines | Risk |
|--------|------|-------|------|
| New | `Beep.Installer.Core.csproj` + folder scaffold | ~40 | low |
| Move | serializer → `Authoring/BsetupSerializer.*` (3 partials) | ~1,100 | medium |
| Move | `SourceScanner`, `GlobMatcher`, factory, templates, MRU, autosave | ~1,000 | medium |
| New | `Build/InstallerBuilder.cs` + 10 stage files | ~900 | high |
| Move | `PayloadPackager`, `PePayloadWriter`, `EmbeddedInstallerResources` + new `ToolLocator`, `VersionNormalizer` | ~500 | medium |
| Move | `InstallProject`, `CustomWizardPage` + P1 projector/builder | ~800 | medium |
| Delete | corresponding `Beep.Installer/Engine/*`, `Models/*` | ~-4,200 | high |
| Modify | `Program.cs`, `InstallerController`, `BuildProgressForm` (interfaces + ct) | ~120 | medium |
| Modify | `Beep.Installer.Tests` + new `Beep.Installer.Core.Tests` | ~mechanical | medium |

## 5. Error handling matrix

| Operation | Before | After |
|-----------|--------|-------|
| Payload file fails to stage | `catch {}` — silently missing from the installer (`:289`) | per-file error; build fails unless the file is optional |
| `dotnet publish` timeout/overrun | fixed 300s wait + output truncation race | async wait honoring `ct`; full output captured |
| Sign/MSIX requested, tool missing | silent no-op with a buried warning | build **fails** listing every probed path |
| Build cancelled | not possible | `OperationCanceledException` → `CleanupStage` removes partial output |

## 6. Dev-mode contract

Keep one current CLI, `.bsetup`, and Setup.exe output layout. If a cleaner current contract is
needed, update the serializer, builder, samples and tests together instead of carrying aliases.

## 7. Verification

```
dotnet test Beep.Installer.Core.Tests        # golden .bsetup round-trip, scanner parity, stage parity
Beep.Installer.exe /BUILD=samples\HelloApp\... /OUT=%TEMP%\p3
%TEMP%\p3\Setup-*.exe /S /D=%TEMP%\p3i && %TEMP%\p3\Setup-*.exe /UNINSTALL /D=%TEMP%\p3i
Beep.Installer.exe /BUILD=... /FORMAT=msix   # produces .msix or fails loudly
Beep.Installer.exe /SELFTEST
# cancel test: large source, cancel mid-build, assert clean temp
```

## 8. Risks

| Risk | Mitigation |
|------|------------|
| Highest-churn phase (~5k LOC moved) | golden + parity tests written **before** each move; old class kept until parity proven |
| Serializer regressions | byte-identical round-trip gate on both sample scripts, from P0's repaired test project |
| PE footer drift breaks shipped updaters | byte-level footer/offset tests |
| Icon-embed P/Invoke fails on CI | stage is optional + fixture-based test |

## 9. Out of scope

ClickOnce/MSIX/signing **implementations** move in P4 (this phase only wires the calls and
shares `ToolLocator`). DI composition (P5). Secret storage (P8).

## 10. Sub-task execution order

1. **3.A.1** Create `Beep.Installer.Core` + move leaf pure helpers (`GlobMatcher`, `PePayloadWriter`, `PayloadPackager`, `ToolLocator`, `VersionNormalizer`). Verify: their tests green.
2. **3.A.2** Move `InstallProject`/`CustomWizardPage` + P1 projector/context builder. Verify: `/SELFTEST` passes.
3. **3.A.3** Move serializer as `BsetupSerializer` partials behind the interface. Verify: golden round-trip green.
4. **3.A.4** Move scanner, factory, templates, MRU, autosave; inject logger; fix swallowed catches. Verify: scan parity on HelloApp.
5. **3.B.1** Build `InstallerBuilder` + pure stages (1,2,3,6,7,10); parity vs old pipeline. Verify: staged-payload diff empty.
6. **3.B.2** Port publish + icon stages with async/ct. Verify: `/BUILD` produces a working Setup.exe.
7. **3.B.3** Wire sign + MSIX stages for real. Verify: loud failure without tools; success with them.
8. **3.C.1** Single `InstallerProjectValidator`; delete both old validation paths. Verify: `/VALIDATE` parity.
9. **3.C.2** Delete old `BuildPipeline`; thread CTS from UI/CLI. Verify: cancel test + full suite.
