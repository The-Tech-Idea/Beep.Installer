# Phase 5: Thin Shell — Composition Root, DI, Beep Conventions — Design Document

**Status:** ⬜ not started · **Priority:** P1 · **Depends on:** P1–P3 (P4 optional)

> **Rev. 2 note.** The engine services registered below now live in `Beep.Installer.Core`
> (P3) rather than BeepDM — see [R0](R0_REVIEW_FINDINGS.md) §3.1 for why authoring/build code
> stays out of BeepDM. The DI shape is otherwise unchanged: the shell depends on interfaces,
> and BeepDM's own services come from `AddBeepForDesktop()`/`AddSetupWizard()`.
**Tracker:** [MASTER_TRACKER.md](MASTER_TRACKER.md) · **Evidence:** [R0_REVIEW_FINDINGS.md](R0_REVIEW_FINDINGS.md) §3 (A2, A3)

## 0. Problem Statement

After P1–P4 the logic lives in BeepDM/Packaging, but the shell still wires everything by
hand: no DI (`new InstallerController()` `Program.cs:192`, `new BuildPipeline()` `:445`),
the install wizard step graph is duplicated three times with stringly-typed `dependsOn`
ids (`Program.cs:267-283, 315-320, 346-352`), and the app ignores BeepDM's standard
bootstrap (`BeepService`/`AddBeepForDesktop`, `AddSetupWizard`, `IDMLogger`,
`IErrorsInfo` conventions — none referenced anywhere in the project today).

## 1. Goals

1. Single composition root using `Microsoft.Extensions.Hosting` +
   `AddBeepForDesktop()`/`AddSetupWizard()` (both already exist in
   `BeepDM/DataManagementEngineStandard/Services/BeepServiceExtensions.Desktop.cs` and
   `SetUp/SetupWizardServiceExtensions.cs`). Acceptance: zero `new` of engine services in
   `Forms/`/`Pages/`; all resolved via constructor injection.
2. One canonical install step-graph definition reused by silent install, UI install,
   uninstall, and selftest. Acceptance: the three inline `SetupWizardBuilder` chains in
   `Program.cs` are replaced by calls into a single factory.
3. Logging/progress on Beep conventions: `IDMLogger` replaces `Engine/Diag` call sites;
   long ops report `IProgress<PassedArgs>`; step results are `IErrorsInfo`.
   Acceptance: `Diag` class deleted or reduced to a file-sink `IDMLogger` adapter.
4. CLI parsing isolated in one `CliOptions.Parse(args)` type (still no external parser
   dependency), so `Dispatch` reads options, not raw strings. Acceptance: `Program.Dispatch`
   under 100 lines.

## 2. Design

### 2.1 Composition root

```csharp
// Program.cs (shape)
var services = new ServiceCollection();
services.AddBeepForDesktop();                    // ConfigEditor, AssemblyHandler, DMEEditor, IDMLogger
services.AddSetupWizard();                       // ISetupWizardFactory etc.
// from Beep.Installer.Core (P3/P4)
services.AddSingleton<IInstallerScriptSerializer, BsetupSerializer>();
services.AddSingleton<IInstallerBuilder, InstallerBuilder>();
services.AddSingleton<ISourceScanner, SourceScanner>();
services.AddSingleton<IInstallerPublisher, ClickOncePublisher>();
services.AddTransient<PackageBuilderForm>();
services.AddTransient<BeepModernInstallerForm>();
using var provider = services.BuildServiceProvider();
```

Runtime (installing) mode should use a slim `AddBeepInstallerRuntime()` registration if
measurement shows desktop services add startup cost.

### 2.2 One wizard graph

Core-owned `InstallWizardGraph` with:

- `BuildInstall(...)` for silent and interactive install.
- `BuildRepair(...)` for payload/resource convergence.
- `BuildUninstall(...)` for provider-journal replay.
- `BuildSelfTestInstall(...)` / `BuildSelfTestUninstall(...)` for lifecycle self-test.

Step ids come from a `StepIds` constants class (kills the magic strings
`"installer.prerequisites.check"` etc.). Longer term this can serialize to BeepDM's
`SetupDefinition` JSON (wizard-as-JSON, `DataManagementModelsStandard/SetUp/Definition/`)
— recorded as a follow-up, not required for this phase.

### 2.3 Logging & progress

- `Diag.Warn/Info/Debug` call sites → injected `IDMLogger` (`LogWarning/LogInfo/LogDebug`).
  A `FileDmLoggerSink` preserves today's `%TEMP%` per-day file behavior for the runtime
  installer where no config folder exists yet.
- `Publisher`-style `(int,string)` progress tuples are gone after P3/P4; the wizard UI
  binds `IProgress<PassedArgs>` end to end.

### 2.4 CLI

`CliOptions` record: `Mode` (Build/Validate/Preview/Publish/Silent/Uninstall/SelfTest/
LangMgr/GeneratorUi/RuntimeUi/Help/Version), plus `ScriptPath`, `OutDir`, `Format`,
`InstallDir`, `Components`, `UpdateUrl`, `NoSign`, `Silent`. Parsing keeps the existing
`/FLAG=` syntax and case-insensitivity; `PrintUsage` text unchanged.

## 3. API surface

N/A. Shell-internal.

## 4. Files to change

| Action | File | Lines | Risk |
|--------|------|-------|------|
| Modify | `Beep.Installer/Program.cs` (host + CliOptions + slim Dispatch) | ~-250/+180 | medium |
| Modify | Core-owned `Hosting/InstallWizardGraph.cs` + shell composition/CLI seams | ~260 | low |
| Modify | `Forms/PackageBuilderForm.cs`, `Forms/BeepModernInstallerForm.cs` (ctor injection) | ~60 | medium |
| Delete/Modify | `Engine/Diag.cs` → `Hosting/FileDmLoggerSink.cs` | ~40 | low |

## 5. Error handling matrix

| Operation | Before | After |
|-----------|--------|-------|
| Unhandled UI exception | MessageBox + crash log (`Program.cs:548-567`) | unchanged behavior, but logged through `IDMLogger` too |
| Unknown CLI flag | silently ignored | usage printed + exit 64 |
| Step failure | `Errors.Ok` flag check per call site | uniform: graph runner logs step id + `IErrorsInfo.Message`, rollback path unchanged |

## 6. Dev-mode contract

Keep the CLI surface intentionally modern and strict; remove stale aliases or duplicate graph
paths when they conflict with the current installer contract.

## 7. Verification

```
Beep.Installer.exe /?  /VER  /VALIDATE=... /PREVIEW=...   # parity vs pre-phase captures
Beep.Installer.exe /SELFTEST
Beep.Installer.exe /BUILD=... && <built Setup.exe> /S && <it> /UNINSTALL
dotnet test Beep.Installer.Tests
```

## 8. Risks

| Risk | Mitigation |
|------|------------|
| `AddBeepForDesktop` startup cost in runtime-installer mode | measure; use slim runtime registration |
| DI changes touch every form ctor | forms resolved from provider only in Program.cs; child dialogs may take services via ctor progressively |

## 9. Out of scope

UI redesign (P6), SetupDefinition JSON consolidation (follow-up), localization (P7).

## 10. Sub-task execution order

1. **5.A.1** `CliOptions` + slim `Dispatch`. Verify: CLI parity captures.
2. **5.A.2** Composition root + registrations; forms resolved via provider. Verify: builder + wizard open.
3. **5.B.1** Core-owned `InstallWizardGraph` + `StepIds`; delete inline chains. Verify: `/S`, `/UNINSTALL`, `/SELFTEST` green.
4. **5.B.2** `IDMLogger` adoption; retire `Diag`. Verify: log file appears in both modes; grep Diag = 0.
