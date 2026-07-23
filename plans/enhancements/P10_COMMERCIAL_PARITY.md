# Phase 10: Commercial-Grade Installer Parity — Task List

> 📄 Design: [P10_COMMERCIAL_PARITY_DESIGN.md](P10_COMMERCIAL_PARITY_DESIGN.md) ·
> Tracker: [MASTER_TRACKER.md](MASTER_TRACKER.md)
> Status: ⬜ not started · Priority: P1 · Depends on: P1–P3 (landed)

Execution rule: one stage at a time, its gate green before the next; mark ✅ here **and** in
the tracker in the same commit as the code.

---

## Stage 10.A — Upgrade-in-place

The core gap: `UpgradeEngine` exists (`BeepDM/.../Installer/UpgradeEngine.cs`) but has no
callers, and `RegisterInstall` is never invoked — so `DetectExisting` has nothing to find and
a re-run installer blindly overwrites.

| # | Task | Files | Verify | Status |
|---|------|-------|--------|--------|
| 10.A.1a | Make `UpgradeEngine` scope-aware: hive overloads for `DetectExisting`/`RegisterInstall` (+ new `UnregisterInstall`, `RegistrationKeyPath`); old HKLM signatures kept `[Obsolete]` | `BeepDM/.../Installer/UpgradeEngine.cs` | `RegisterAndDetect_RoundTrip_InTheGivenHive` (HKCU) | ✅ |
| 10.A.1b | `VerifyInstallStep` registers the install (scope-correct, best-effort) after the manifest; `UninstallStep` unregisters so no ghost remains | `VerifyInstallStep.cs`; `UninstallStep.cs` | `/SELFTEST` + live registry check: registration present after install, gone after uninstall | ✅ |
| 10.A.2a | `UpgradeStep` (id `installer.upgrade.detect`), after prerequisites, before any disk write: fresh → proceed; same version → reinstall note; older → fail with actionable message naming installed version + `/FORCE`; newer or forced → `Backup()` + context keys (`UpgradeBackupPath`, `UpgradePreviousVersion`); **backup failure aborts** rather than upgrading without a restore point | `UpgradeStep.cs`; `InstallWizardGraph.cs`; `InstallContextBuilder` (`force` param); `Program.cs` (`/FORCE`) | 5 `UpgradeFlowTests` decision tests | ✅ |
| 10.A.2b | `CommitUpgradeStep` terminal (after verify — backup discarded only once install proven): `MigrateUserConfig` from backup then delete it; failure path: both hosts (silent + wizard UI) call `RestoreFromBackup` when a backup was recorded | `UpgradeStep.cs` (both steps); `Program.cs`; `BeepModernInstallerForm.cs` | commit/migrate/restore tests | ✅ |
| 10.A.G | **Gate:** scripted sequence — install v1.0, write user config, install v1.1 (config kept, backup gone), attempt v1.0 over v1.1 (refused), retry with `/FORCE` (succeeds) | — | **E2E green 2026-07-23:** fresh exit 0 reg 1.0.0 · upgrade exit 0 reg 1.1.0, payload v2, `settingsKept=True`, `backupGone=True` · downgrade exit 1, reg stays 1.1.0, message names 1.1.0 + `/FORCE` · forced exit 0 reg 1.0.0 · suite 296/0 | ✅ |

## Stage 10.B — Repair & locked files

| # | Task | Files | Verify | Status |
|---|------|-------|--------|--------|
| 10.B.1a | Repair planner as pure `RepairFilesStep.ComputePlan` (placed in **BeepDM** beside the other runtime steps, not Core — runtime-install logic per the architecture split). Compares payload vs installed SHA-256; payload-absent entries skipped — repair never deletes or invents files | new `BeepDM/.../Installer/Steps/RepairFilesStep.cs` | 4 planner tests (intact / missing / modified / never-staged) | ✅ |
| 10.B.1b | `/REPAIR` verb (install path from `/D=`, else the P10.A registration, else script default) + `BuildRepair` graph (payload → repair → shortcuts → registry → env; deliberately **no** upgrade detection or custom actions) + ARP `ModifyPath`; honours `DryRun` | `Program.cs`; `InstallWizardGraph.cs`; `BuildPipeline.cs` ARP synthesis | **E2E green 2026-07-23:** corrupted `app.txt` and deleted `docs\help.txt` both restored, `user-data.txt` untouched, exit 0 | ✅ |
| 10.B.2a | `FileCopyStep` locked destination: stage to `<dest>.pending` (same volume) + `ScheduleFileForRestart` + `RebootRequired` context flag; when scheduling is impossible (unelevated — it writes HKLM PendingFileRenameOperations) fail with an actionable "in use" message instead of the old unhandled `IOException` | `FileCopyStep.cs` | held-file test accepting either honest outcome by elevation | ✅ |
| 10.B.2b | `Hosting/ExitCodes.ForInstallResult`: 3010 on success-with-pending-reboot, `/NORESTART` → 0, `/RESTARTEXITCODE=n` override; failure always 1 | `ExitCodes.cs`; `Program.cs` | 5 exit-code matrix tests | ✅ |
| 10.B.G | **Gate:** repair E2E green (above) + suite 309/0 + `/SELFTEST` 0. *Not live-verified:* an elevated locked-file → actual 3010 process exit (needs an elevated console; unit-covered via `ExitCodes` + step tests) | — | — | ✅ (elevated 3010 run outstanding) |

## Stage 10.C — Logging & deployment ergonomics

| # | Task | Files | Verify | Status |
|---|------|-------|--------|--------|
| 10.C.0 | **Bug found & fixed during this stage:** nothing expanded `%InstallPath%` in registry values (`ExpandString` only resolves real env vars), so every synthesized ARP `UninstallString`/`ModifyPath`/`InstallLocation` was a literal unrunnable `%InstallPath%\Setup.exe` — **uninstall from Add/Remove Programs was broken**. `RegistryWriteStep` now expands `%InstallPath%`/`{InstallPath}` in key paths and values, and records the *expanded* operations in the manifest so uninstall deletes the real keys | `RegistryWriteStep.cs` | unit test + live ARP check in the gate | ✅ |
| 10.C.1a | `InstallLogger` wired at the hosts (synchronous `SyncProgress` — `Progress<T>` on a console host posts to the thread pool and loses late lines) + per-step results from `GetReport()` via `StepComplete`; wizard UI logs through its existing progress handler | `Program.cs`; `BeepModernInstallerForm.cs` | log lists steps + results | ✅ |
| 10.C.1b | `/LOG=<path>` honoured (default announced instead of silently written); ARP gains `LogFile` post-success; wizard "View installation log" now points at the real log (previously the manifest) | `Program.cs`; `BeepModernInstallerForm.cs` | gate: `/LOG=x` produces x; ARP `LogFile` set | ✅ |
| 10.C.2a | Inno aliases: `/VERYSILENT` = silent, `/SUPPRESSMSGBOXES` accepted, `/NORESTART` + `/RESTARTEXITCODE` (landed in 10.B.2b) | `Program.cs` | gate: install via `/VERYSILENT` | ✅ |
| 10.C.2b | Unsigned build → prominent SmartScreen warning in result + build log; `/REQUIRESIGNED` refuses an unsigned `/BUILD` (exit 1) before publishing | `BuildPipeline.cs`; `Program.cs` | gate: warning present in build output | ✅ |
| 10.C.0b | **Second ARP bug found by the gate:** the `.bsetup` loader baked `HKEY_LOCAL_MACHINE\` into every registry `KeyPath` (the writer stripped it again, so round-trips looked symmetric) — `CreateSubKey` then created a literal `HKEY_LOCAL_MACHINE` subkey under the scope hive, landing the whole ARP set at `HKCU\HKEY_LOCAL_MACHINE\…` where Windows never looks. Loader no longer prepends; new `InstallScope.NormalizeKeyPath` defensively strips hive tokens in write and uninstall paths (older manifests still carry them) | serializer; `InstallScope.cs`; `RegistryWriteStep.cs`; `UninstallStep.cs` | hive-token unit test + gate | ✅ |
| 10.C.0c | **Third ARP bug:** uninstall deleted registry *values* but left the key shells, so the ARP key survived uninstall (the host-recorded `LogFile` kept it non-empty). ARP keys are wholly product-owned → deleted outright; other touched keys removed only when empty, mirroring the directory sweep | `UninstallStep.cs` | gate: `arpKeyRemoved=True` | ✅ |
| 10.C.G | **Gate green 2026-07-23:** build (unsigned warning present) → `/VERYSILENT /LOG=` install exit 0, ARP present with fully-expanded `UninstallString`/`ModifyPath` + `LogFile`, no literal HKLM key → uninstall exit 0, **ARP key and product registration both removed**. Suite 311/0 | — | — | ✅ |

---

## Definition of done

Upgrade, downgrade-refusal, repair, locked-file/3010, `/LOG`, and the Inno alias set all
demonstrated by scripted E2E (not just unit tests), tracker rows ✅, and no new silent-failure
guard offenders.
