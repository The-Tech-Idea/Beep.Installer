# Phase 4: ClickOnce / MSIX / Signing Consolidation — Design Document

**Status:** ⬜ not started · **Priority:** P2 · **Depends on:** P3
**Tracker:** [MASTER_TRACKER.md](MASTER_TRACKER.md) · **Evidence:** [R0_REVIEW_FINDINGS.md](R0_REVIEW_FINDINGS.md) §3, §4 (A5, A7)

> **Rev. 2 note — the "where does it live" question is now settled.** BeepDM's own scope doc
> states the installer is *"Not a packaging tool"* and *"Not a code-signing service"*
> (`installer-service/00-overview-and-scope.md:55-67`), and BeepDM contains no ClickOnce,
> MSIX, or signtool code at all. So option B (move into BeepDM) is rejected on BeepDM's own
> terms, and option C (delete the features) is rejected because they work and are the only
> distribution path the tool offers. **Decision: this code lives in `Beep.Installer.Core`
> (created in P3) under a `Packaging/` folder** — non-UI, out of the exe, and free to depend
> on Win32 packaging tooling that BeepDM must not. The former Open Decision D3 is closed;
> D3 in the current tracker refers to a different question (extending `InstallConfig`).

## 0. Problem Statement

Nine `Engine/ClickOnce/*` files plus `Publisher`, `MsixPackager`, `StoreReadinessChecker`,
and `SignTool` implement a second distribution stack (ClickOnce-style per-user install +
web update feed, MSIX packaging, Authenticode signing). Two tensions:

1. **BeepDM's own installer-service plan declares this out of scope** — "Not a packaging
   tool", "Not a code-signing service"
   (`BeepDM/DataManagementEngineStandard/Services/Studio/Plans/FutureWork/installer-service/00-overview-and-scope.md:56-67,94-96`).
   BeepDM has no ClickOnce/MSIX/signtool code today, and its update-feed protocol
   (`:139-159`) plus `NuGetPackageManager`/`UpgradeEngine` cover the *update* problem
   differently.
2. The local stack has real defects: `Engine/ClickOnce/RollbackManager` name-collides
   with BeepDM `Steps.RollbackManager` used at `Program.cs:264` (A7); version
   normalizers are triplicated (`ApplicationManifestWriter.cs:74`, `MsixPackager.cs:227`,
   `UpdateChecker.cs:105`); update check/apply is sync-over-async
   (`UpdateChecker.cs:90`, `UpdateApplier.cs:50`); shortcut creation shells PowerShell
   with string concatenation (`Shortcut.cs:18-31`).

## 1. Goals

1. Decision D3 resolved and recorded (see options below). Acceptance: tracker updated,
   this doc's chosen option marked.
2. Whatever survives is behind `IInstallerPublisher` (P1 contract) with a single home —
   no packaging logic left loose in the shell. Acceptance: `Engine/ClickOnce/` folder gone
   from the shell.
3. Name collision, version-normalizer triplication, and sync-over-async removed.
   Acceptance: one `VersionNormalizer`; `UpdateChecker/Applier` async with `ct`;
   `ClickOnce.RollbackManager` renamed `VersionBackupRotator`.
4. Update-version comparison uses BeepDM `SemVer.Compare` / `UpgradeEngine.IsNewer`
   (`UpgradeEngine.cs:93`). Acceptance: local `IsNewer` deleted.

## 2. Resolved placement

`Beep.Installer.Core/Packaging/` — the non-UI library created in P3. Rationale in the
rev. 2 note above: BeepDM's scope doc rules out BeepDM, the features work and are worth
keeping, and Core already exists as the home for authoring/build concerns. Folding into Core
rather than minting a second library avoids ceremony; if packaging ever needs to ship
independently it can be split then (Open Decision D9).

## 3. Design

Inside `Beep.Installer.Core` (net10.0, references `DataManagementModels`/`Engine`, **no**
WinForms):

```
Beep.Installer.Core/
└── Packaging/
    ├── ClickOnce/{ApplicationManifestWriter, DeploymentManifestWriter, PublishStager,
    │               ClickOnceRuntime, UpdateChecker, UpdateApplier, VersionBackupRotator,
    │               TrustChecker, ShortcutWriter}.cs
    ├── Msix/{MsixPackager, StoreReadinessChecker}.cs
    ├── Signing/SignTool.cs                  // uses the shared ToolLocator from P3
    └── ClickOncePublisher.cs                // : IInstallerPublisher
```

`VersionNormalizer` is **not** duplicated here — P3 §2.3 already creates the single copy in
Core, and this phase deletes the three local variants
(`ApplicationManifestWriter.cs:74`, `MsixPackager.cs:227`, `UpdateChecker.cs:105`).

- `ClickOncePublisher` wraps today's `Publisher.Publish` (`Engine/Publisher.cs:20`) with
  `IProgress<PassedArgs>` + `CancellationToken`.
- `UpdateChecker.Check` / `UpdateApplier.DownloadAndStage/Apply` become `async Task`-based
  (`HttpClient` injectable fetcher already exists, `UpdateChecker.cs:31`).
- `ShortcutWriter` replaces PowerShell shelling with COM `IShellLink` via
  `Type.GetTypeFromProgID("WScript.Shell")` late binding or CsWin32 — no string-built
  command line (closes the injection surface, coordinates with P8).
- `SignTool` uses `ToolLocator` from P3 (shared probing).
- Shell keeps only the `/PUBLISH=` CLI branch and Publish button, both calling
  `IInstallerPublisher`.

## 4. Files to change

| Action | File | Lines | Risk |
|--------|------|-------|------|
| New | `Beep.Installer.Core/Packaging/**` (13 files, moved/adapted) | ~1,300 | medium |
| Delete | `Beep.Installer/Engine/ClickOnce/*` (9 files), `Engine/{Publisher,MsixPackager,SignTool}.cs`, `Engine/Msix/*` | -1,300 | medium |
| Modify | `Beep.Installer/Program.cs` `/PUBLISH` branch; `InstallerController.Publish` | ~30 | low |

## 5. Error handling matrix

| Operation | Before | After |
|-----------|--------|-------|
| Update manifest fetch fails | sync `.GetAwaiter().GetResult()` exception path | async, `IErrorsInfo` Failed + retry-able message |
| Unsigned publish | warning text only (`TrustChecker.cs:75`) | warning surfaced on `InstallerPublishResult.Warnings` AND builder UI banner (P6) |
| MakeAppx/signtool missing | mixed (stub vs real) | uniform `ToolLocator` failure with probe list |

## 6. Dev-mode contract

Published ClickOnce layouts and `.bsetup` fields follow the current contract. Remove duplicate
packaging paths as Core ownership expands.

## 7. Verification

```
dotnet test Beep.Installer.Tests --filter "ClickOnce|Publish|Msix|StoreReadiness|UpdateApplier|UpdateChecker|TrustChecker"
Beep.Installer.exe /PUBLISH=samples\... /OUT=%TEMP%\p4pub /NOSIGN
# serve %TEMP%\p4pub over local http, run UpdateChecker against it (existing integration test)
```

## 8. Risks

| Risk | Mitigation |
|------|------------|
| COM shortcut writer behaves differently on server SKUs | replace it with the tested canonical shortcut writer |
| ~40 tests reference old namespaces | mechanical namespace update in same commit (P9 owns final sweep) |
| ClickOnce update path overlaps BeepDM's planned update feed (`installer-service/00-overview-and-scope.md:139-165`) | this phase preserves today's behavior only; convergence is a backlog decision, not a silent rewrite |

## 9. Out of scope

Store submission automation; delta/differential updates; APPX bundle signing flows;
adopting BeepDM's planned `feed.json` update protocol.

## 10. Sub-task execution order

1. **4.A.2** Add `Packaging/` to Core; move Msix + Signing onto the shared `ToolLocator`. Verify: Msix/StoreReadiness tests green.
3. **4.A.3** Move ClickOnce files; rename `RollbackManager`→`VersionBackupRotator`; async Update*. Verify: ClickOnce/Update tests green.
4. **4.A.4** `ClickOncePublisher : IInstallerPublisher`; rewire shell `/PUBLISH`; delete old Engine files. Verify: publish integration test green.
5. **4.B.1** Replace shortcut PowerShell shelling. Verify: shortcut created on clean VM through the canonical writer.
