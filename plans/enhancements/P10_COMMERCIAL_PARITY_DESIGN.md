# Phase 10: Commercial-Grade Installer Parity — Design Document

**Status:** ⬜ not started · **Priority:** P1 · **Depends on:** P1–P3 (all landed)
**Tracker:** [MASTER_TRACKER.md](MASTER_TRACKER.md)

## 0. Problem Statement

The installer now works end to end (build → install → uninstall verified 2026-07-20), but a
user coming from Inno Setup, WiX/MSI, Squirrel or an MSIX-distributed app will notice missing
behaviours that commercial installers treat as table stakes. This phase closes those gaps.
The benchmark set: **Inno Setup** (authoring UX, script model — which `.bsetup` already
imitates), **MSI** (repair, upgrade codes, restart handling, logging), **Squirrel/Velopack**
(silent background updates, side-by-side versions), **MSIX** (clean uninstall guarantees).

What we already have (verified in code, not assumed):

| Capability | Where |
|---|---|
| Detect existing install + version compare | `UpgradeEngine.DetectExisting:22`, `IsNewer:93` — **but nothing calls it**; a re-run install today blindly overwrites |
| Backup / restore | `UpgradeEngine.Backup:46`, `RestoreFromBackup:71` — uncalled |
| User-config migration across versions | `UpgradeEngine.MigrateUserConfig:106` — uncalled |
| Transactional rollback on failed install | `RollbackManager` + wired steps (verified live: failed registry write rolled back copied files) |
| ARP entry | synthesized by `BuildUninstallRegistryEntries` |
| Shared-file refcounting (SharedDLLs) | `SharedFileCountStep` + tests |
| Restore point, locked-file/restart-manager, firewall, file assoc | `InstallHelpers` (restore point wired; RestartManager helpers **uncalled**) |
| Silent install/uninstall, exit codes | `/S`, `/UNINSTALL`, 0/1/2/99 |

## 1. Goals (each = one commercial behaviour, testable)

1. **Upgrade-in-place.** Running a newer installer over an existing install detects it,
   backs up, migrates user config, installs, and removes the backup on success — instead of
   blind overwrite. Downgrade prompts (UI) / fails with exit code (silent) unless `/FORCE`.
2. **Repair mode.** `Setup.exe /REPAIR` (and an ARP "Repair" verb via `ModifyPath`) re-copies
   files whose hash differs from the manifest, restores shortcuts/registry, touches nothing else.
3. **Locked-file handling.** Files in use are scheduled via the existing
   `InstallHelpers.ScheduleFileForRestart` and the install exits **3010** (the MSI
   "reboot required" convention) instead of failing mid-copy.
4. **Install log.** `/LOG=<path>` writes the existing `InstallLogger` output (today it goes to
   an unannounced `%TEMP%` file); ARP entry records the log location; the wizard's
   "View installation log" button points at it.
5. **Standard silent grammar.** `/VERYSILENT`, `/SUPPRESSMSGBOXES`, `/NORESTART`,
   `/RESTARTEXITCODE=n` accepted (Inno-compatible aliases), so existing deployment tooling
   (Intune/SCCM scripts written for Inno) works unchanged.
6. **Signed-by-default posture.** Unsigned builds emit a prominent result-form warning with a
   SmartScreen explanation; `/BUILD` gains `/REQUIRESIGNED` for CI gates. (Real signing already
   works since the P3 stub removal.)

## 2. Design

### 2.1 Upgrade flow (goal 1)

New `UpgradeStep` (BeepDM `Installer/Steps/`, id `installer.upgrade.detect`), first in the
graph after prerequisites:

- `UpgradeEngine.DetectExisting(config.ProductName)` — note it reads HKLM only
  (`UpgradeEngine.cs:22`); extend with the scope-aware `InstallScope.OpenBaseKey` first,
  same pattern as the P2 fixes.
- Same version → switch context into **repair** semantics (goal 2). Older installer than
  installed → fail `Errors.Failed` with a clear message; `/FORCE` overrides.
- Newer → `Backup()` to `<installPath>.backup`, record path in context; `MigrateUserConfig`
  after file copy; delete backup in a new terminal `CommitUpgradeStep`; on failure the
  existing rollback plus `RestoreFromBackup` reinstates the old version.

`RegisterInstall` (`UpgradeEngine.cs:127`) becomes scope-aware and is called by
`VerifyInstallStep` so DetectExisting has something to find — today nothing writes it.

### 2.2 Repair (goal 2)

`install-manifest.json` already lists every installed file; the solid-payload manifest maps
path → SHA-256. Repair = for each manifest entry, compare on-disk hash
(`InstallHelpers.ComputeFileHash`) against payload hash, re-extract mismatches/missing from
the embedded payload, re-run `ShortcutCreateStep` + `RegistryWriteStep` idempotently.
Surface as `/REPAIR` in `Program.Dispatch` and as `ModifyPath` in the ARP entry.

### 2.3 Locked files (goal 3)

`FileCopyStep`: on `IOException` where `InstallHelpers.IsFileLocked` confirms a lock, call
`ScheduleFileForRestart(src, dest)` (`InstallHelpers.cs:43`, currently uncalled), record
`RebootRequired = true` in context, continue. End of run: exit 3010 (configurable via
`/RESTARTEXITCODE`), suppressed message under `/NORESTART` per convention.

### 2.4 Log (goal 4)

`InstallLogger` already exists with `StepStart/StepComplete/CopyTo` — wire it into the wizard
graph runner (it is currently consumed by nobody in the graph), honour `/LOG=`, store the
path in the ARP entry (`InstallLocation` sibling value `LogFile`).

## 3. Files to change

| Action | File | Risk |
|--------|------|------|
| New | `BeepDM/.../Installer/Steps/UpgradeStep.cs` + `CommitUpgradeStep.cs` | medium |
| Modify | `BeepDM/.../Installer/UpgradeEngine.cs` (scope-aware registry) | medium |
| Modify | `BeepDM/.../Installer/Steps/{FileCopyStep,VerifyInstallStep}.cs` (locked files; RegisterInstall) | medium |
| New | `Beep.Installer.Core/Runtime/RepairPlanner.cs` | medium |
| Modify | `Beep.Installer/Program.cs` (+`/REPAIR /FORCE /LOG /VERYSILENT /NORESTART /RESTARTEXITCODE`) | medium |
| Modify | `Hosting/InstallWizardGraph.cs` (upgrade/repair variants) | low |
| Tests | upgrade/downgrade/repair/locked-file suites | — |

## 4. Verification

```
build v1.0 → install → build v1.1 → install over it   # upgrade path, config migrated, backup gone
run v1.0 installer again over v1.1                    # downgrade refused; /FORCE overrides
corrupt an installed file → Setup.exe /REPAIR         # only that file restored
hold a file open → /S install                         # exit 3010, file scheduled for reboot
/S /LOG=%TEMP%\i.log                                  # log exists, referenced in ARP
```

## 5. Out of scope

MSI/WiX interop, winget manifests, Store submission — separate decisions. Update delivery is
**P11**.

## 6. Sub-task execution order

1. **10.A.1** Scope-aware `UpgradeEngine` + `RegisterInstall` wired into verify. Gate: DetectExisting finds a fresh install.
2. **10.A.2** `UpgradeStep`/`CommitUpgradeStep` + downgrade guard + `/FORCE`. Gate: v1.0→v1.1→refused-downgrade sequence.
3. **10.B.1** Repair planner + `/REPAIR` + ARP `ModifyPath`. Gate: corrupted-file test.
4. **10.B.2** Locked-file → 3010 path. Gate: held-file test.
5. **10.C.1** `/LOG` + logger wired to graph + ARP reference. Gate: log content matches steps run.
6. **10.C.2** Inno-compatible silent aliases + unsigned-build warning + `/REQUIRESIGNED`. Gate: alias matrix test.
