# Phase 2: Runtime Thinning — Delegate to BeepDM, Fix It Upstream — Design Document

**Status:** ⬜ not started · **Priority:** P1 · **Depends on:** P1
**Tracker:** [MASTER_TRACKER.md](MASTER_TRACKER.md) · **Evidence:** [R0_REVIEW_FINDINGS.md](R0_REVIEW_FINDINGS.md) §2.2, §3.1

> Replaces the rev. 1 doc "Authoring Engine → BeepDM". That phase pushed the `.bsetup`
> serializer and source scanner into BeepDM; BeepDM's scope doc rules that out (R0 §3). This
> phase instead thins the installer of **runtime-install** logic — the part BeepDM genuinely
> owns — and fixes the gaps that stop BeepDM from being able to own it.

## 0. Problem Statement

After P1 the installer produces the right contract, but it still carries runtime-install
logic that duplicates BeepDM, and BeepDM still has inert contract surface that forces the
installer to compensate:

**Installer duplicates BeepDM:**
- `Engine/PrerequisiteDetector.cs` (221 lines) shells detection commands and parses .NET
  versions — overlapping `PrerequisiteCheckStep` (`:86-96,139-157`) and `BootstrapperStep`.
- `Engine/InstallScopeResolver.cs` re-implements default-path logic that exists as
  `InstallHelpers.GetDefaultInstallPath(productName, perUser)` (`InstallHelpers.cs:226`).
- Version comparison is hand-rolled in several places while BeepDM ships `SemVer.Compare`
  and `UpgradeEngine.IsNewer` (`UpgradeEngine.cs:93`).
- Payload hash verification is absent although `InstallHelpers.ComputeFileHash` /
  `VerifyFileHash` exist (`InstallHelpers.cs:17,29`).
- `Engine/ConditionListValidator` + `ComponentSelection` wrap
  `InstallConditionEvaluator`, which **nothing in BeepDM calls** — the installer is the
  de-facto owner of component gating.

**BeepDM gaps that block delegation** (all verified, R0 §2.2):
- `InstallConfig.EnvironmentVariables` has **no step**; `VerifyInstallStep.cs:69` reads
  `"EnvVarsSet"` which nothing writes.
- **No Add/Remove-Programs uninstall-key writer exists anywhere**, despite
  `InstallProject.CreateUninstallEntry` — so uninstall is not discoverable from Windows.
- Duplicate `StepId` `installer.com.register` (`ComAndGacSteps.cs:22` vs
  `AdvancedSteps.cs:127`) — `SetupWizardBuilder.Build()` throws if both are registered.
- `ShortcutCreateStep` and `InstallHelpers.RegisterFileAssociation` are not scope-aware;
  `RollbackManager.RegisterRegistryWrite` hardcodes HKLM (`RollbackManager.cs:66`).
- No step honours `SetupOptions.DryRun`; none sets `SupportsRollback = true`, so
  `AutoRollbackOnFailure` reports "nothing undone".
- `Prerequisite.DownloadUrl`/`SilentInstallArgs` are unused; downloads use the parallel
  `BootstrapperItem` model (`BootstrapperStep.cs:139`).

## 1. Goals

1. The installer holds no runtime-install logic that BeepDM already implements.
   Acceptance: `PrerequisiteDetector` and the duplicate path/version/hash helpers are gone
   from the installer; behavior is unchanged or better.
2. Environment variables actually get applied and uninstalled. Acceptance: an install with
   an `EnvironmentVariableOp` sets it; uninstall removes it; `"EnvVarsSet"` is populated.
3. Uninstall is discoverable in Add/Remove Programs when `CreateUninstallEntry` is true.
   Acceptance: the ARP entry appears with correct name/version/publisher/icon and its
   uninstall command works.
4. Rollback is real. Acceptance: a deliberately failing install reverses files, registry,
   and shortcuts; the rollback report lists actual undone actions, not "not supported".
5. Payload integrity is verified before copy. Acceptance: a corrupted payload aborts the
   install before any file lands.

## 2. Design

### 2.1 Delete installer-side duplicates

| Delete / shrink | Replace with |
|---|---|
| `Engine/PrerequisiteDetector.cs` runtime checks | `PrerequisiteCheckStep` (already in the graph) + `BootstrapperStep` for downloads. **Keep** an authoring-side detector used by the builder to *suggest* prerequisites at scan time — that is an authoring concern (moves to `Beep.Installer.Core` in P3) |
| `InstallScopeResolver` path logic | `InstallHelpers.GetDefaultInstallPath` (`:226`); the installer keeps only the *policy* decision (which scope), not the path formula |
| ad-hoc version compares | `SemVer.Compare` / `UpgradeEngine.IsNewer` |
| no hash check | `InstallHelpers.VerifyFileHash` in `PayloadDownloadStep`/`PayloadPrepareStep` |

`ConditionListValidator` and `ComponentSelection` **stay** in the installer (authoring +
UI gating) because BeepDM never calls `InstallConditionEvaluator` itself — but they call
into it rather than reimplementing evaluation.

### 2.2 Contribute the missing steps to BeepDM

These are runtime-install concerns and belong in
`DataManagementEngineStandard/Installer/Steps/` beside their 22 siblings (namespace
`TheTechIdea.Beep.Installer.Steps`):

**`EnvironmentVariableStep`** — new. StepId `installer.envvars.write`. Applies
`InstallConfig.EnvironmentVariables` honoring `EnvironmentVariableOp.Scope`, writes
`"EnvVarsSet"` (the key `VerifyInstallStep.cs:69` already expects), and calls
`InstallHelpers.BroadcastEnvironmentChange()` (`:200`, currently uncalled). Reversal added to
`UninstallStep`.

**`UninstallEntryStep`** — new. StepId `installer.arp.register`. Writes the ARP key under
`Software\Microsoft\Windows\CurrentVersion\Uninstall\{ProductName}` in the scope-correct hive
via `InstallScope.OpenBaseKey` — `DisplayName`, `DisplayVersion`, `Publisher`,
`InstallLocation`, `DisplayIcon`, `UninstallString`, `EstimatedSize`, `NoModify/NoRepair`.
Removed by `UninstallStep`. This closes the largest functional hole: today nothing writes it.

**Scope-awareness fixes** — `ShortcutCreateStep` chooses
`CommonPrograms`/`CommonDesktopDirectory` when `PerUser == false`;
`InstallHelpers.RegisterFileAssociation` gains a `perUser` parameter;
`RollbackManager.RegisterRegistryWrite` takes the hive instead of hardcoding
`Registry.LocalMachine` (`:66`).

**`SupportsRollback`** — implemented on the steps that mutate system state (`FileCopyStep`,
`RegistryWriteStep`, `ShortcutCreateStep`, `EnvironmentVariableStep`, `UninstallEntryStep`,
`ComServerRegistrationStep`) so `AutoRollbackOnFailure` does real work through
`RollbackOrchestrator` (`Rollback/RollbackOrchestrator.cs:24`).

**Duplicate StepId** — rename `AdvancedSteps.ComRegistrationStep` (the `regsvr32` strategy)
to `installer.com.regsvr32`, leaving `ComAndGacSteps.ComServerRegistrationStep` as
`installer.com.register`. Both strategies remain available; they can now coexist.

**`DryRun`** — the mutating steps check `context.Options.DryRun` and report intended actions
without performing them. Needed for the builder's "preview install" and for safe CI runs.

### 2.3 Open Decision D3 — extend `InstallConfig`?

Five `InstallProject` fields are runtime-relevant but have no `InstallConfig` home and
currently travel as loose context keys: `SelfContained`, scope preference
(`DefaultScope`/`AllowScopeSelection`), `PayloadFolderName`, `CreateRestorePoint`,
`CreateUninstallEntry`.

**Recommended: add them to `InstallConfig`** (additive; schema stays "1.0" since additive
JSON is compatible). It makes a shipped `install-config.json` self-describing and lets
`ResolvePayloadRoot` stop hardcoding `"payload"` (`ConfigManager.cs:119`). If declined, they
remain context keys set by `InstallContextBuilder` — which works, but leaves the JSON
artifact incomplete.

### 2.4 Upstream test coverage

BeepDM's installer domain has **zero tests** (R0 §2.2) despite
`SharedFileCountStep.cs:32` and `UninstallStep.cs:29` exposing test-only ctor overrides
clearly added for tests never written. Every new/modified step here ships with tests in
`BeepDM/tests/` — the first coverage this domain has had. Registry-touching tests use the
existing injectable key-path overrides against a scratch hive.

## 3. API surface

Additive to `TheTechIdea.Beep.DataManagementEngine` (new steps) and, if D3 is approved,
`...DataManagementModels` (five `InstallConfig` fields). Minor-version bump; coordinate with
the P0 version-bump decision so the local feed stays truthful.

## 4. Files to change

| Action | File | Lines | Risk |
|--------|------|-------|------|
| New | `BeepDM/.../Installer/Steps/EnvironmentVariableStep.cs` | ~120 | medium |
| New | `BeepDM/.../Installer/Steps/UninstallEntryStep.cs` | ~150 | medium |
| Modify | `BeepDM/.../Installer/Steps/UninstallStep.cs` (reverse env vars + ARP) | ~80 | medium |
| Modify | `BeepDM/.../Installer/Steps/ShortcutCreateStep.cs` (scope-aware) | ~30 | medium |
| Modify | `BeepDM/.../Installer/Steps/AdvancedSteps.cs` (rename StepId) | ~5 | low |
| Modify | `BeepDM/.../Installer/{RollbackManager,InstallHelpers}.cs` (hive param, perUser assoc) | ~40 | medium |
| Modify | 6 steps: `SupportsRollback` + `RollbackAsync` + `DryRun` honoring | ~250 | high |
| Modify | `BeepDM/.../Installer/InstallConfig.cs` (D3, if approved) | ~15 | low |
| New | `BeepDM/tests/InstallerTests/*` (first coverage) | ~600 | low |
| Delete | `Beep.Installer/Engine/PrerequisiteDetector.cs` (runtime half) | ~-150 | medium |
| Modify | `Beep.Installer/Engine/InstallScopeResolver.cs` (policy only) | ~-30 | low |
| Modify | Core-owned `Steps/Payload*.cs` (hash verify) | ~40 | low |
| Modify | Core-owned wizard graph (register new steps) | ~15 | low |

## 5. Error handling matrix

| Operation | Before | After |
|-----------|--------|-------|
| Env var in config | silently ignored (no step) | applied + recorded + broadcast; reversed on uninstall |
| Uninstall discoverability | absent from Add/Remove Programs | ARP entry written; removed on uninstall |
| Install fails midway | files stay; rollback report says "not supported" | real reversal of files/registry/shortcuts/env/ARP |
| Corrupt downloaded payload | extracted and copied | hash mismatch aborts before any file lands |
| Both COM steps registered | `Build()` throws `InvalidOperationException` | distinct ids; both usable |

## 6. Dev-mode contract

The installer is still in dev mode. Replace runtime signatures directly when a cleaner
contract exists, and update all in-repo callers/tests in the same slice. Do not add retired
overloads just to preserve a discarded installer surface.

## 7. Verification

```
dotnet test BeepDM/tests/InstallerTests          # new upstream coverage
Beep.Installer.exe /SELFTEST
# per-machine install: assert ARP entry + HKLM writes + env var set
# per-user install:    assert HKCU writes + per-user shortcut folders
# failure injection:   force a required file missing -> assert full reversal
# corrupt payload:     flip a byte -> assert abort before copy
```

## 8. Risks

| Risk | Mitigation |
|------|------------|
| Editing BeepDM affects other consumers | update the owning callers/tests in the same slice and run the relevant BeepDM/installer build before merge |
| `SupportsRollback` across 6 steps is the largest behavioral change here | one step per commit, each with a failure-injection test |
| ARP registration on per-user installs writes the wrong hive | scope-aware via `InstallScope.OpenBaseKey`; both scopes covered by tests |
| D3 undecided blocks the phase | if declined, keep the five values as context keys — no other design change |

## 9. Out of scope

Authoring/build logic (P3), packaging (P4), DI (P5). Side-by-side install layout and the
update feed from BeepDM's installer-service plan (`01-phases.md:206-212`) — recorded as a
backlog item, not attempted here.

## 10. Sub-task execution order

1. **2.A.1** Rename the duplicate COM StepId + regression. Verify: both steps registrable.
2. **2.A.2** `EnvironmentVariableStep` + uninstall reversal + tests. Verify: env var round-trip.
3. **2.A.3** `UninstallEntryStep` (ARP) + uninstall reversal + tests. Verify: entry appears/disappears, both scopes.
4. **2.B.1** Scope-awareness fixes (shortcuts, file assoc, rollback hive). Verify: per-user vs per-machine tests.
5. **2.B.2** `SupportsRollback`/`RollbackAsync` on the 6 mutating steps. Verify: failure-injection reversal test.
6. **2.B.3** `DryRun` honoring. Verify: dry run mutates nothing.
7. **2.C.1** Delete installer-side runtime duplicates; adopt `InstallHelpers`/`SemVer`. Verify: `/SELFTEST` + install E2E unchanged.
8. **2.C.2** Payload hash verification. Verify: corrupt-payload abort test.
9. **2.C.3** (if D3 approved) Extend `InstallConfig` + move the five values off loose keys. Verify: `install-config.json` self-describing; round-trip test.
