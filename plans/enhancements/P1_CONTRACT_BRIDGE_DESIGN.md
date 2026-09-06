# Phase 1: Contract Bridge — Make the Installer Actually Install — Design Document

**Status:** ⬜ not started · **Priority:** P0 · **Depends on:** P0
**Tracker:** [MASTER_TRACKER.md](MASTER_TRACKER.md) · **Evidence:** [R0_REVIEW_FINDINGS.md](R0_REVIEW_FINDINGS.md) §1.2, §2

> Replaces the rev. 1 doc "Contracts Unification in BeepDM Models", which proposed moving
> `InstallProject` into BeepDM. Code reading showed the real problem is not *where* the model
> lives — it is that the installer never produces the contract BeepDM's engine consumes.

## 0. Problem Statement

`Program.cs:256-260` sets `SetupContext.Properties["InstallProject"]`. Every BeepDM step
reads `context.TryGetProperty<InstallConfig>("InstallConfig")`. Four steps hard-fail
`Validate()` without it — `FileCopyStep.cs:32`, `PrerequisiteCheckStep.cs:32`,
`ShortcutCreateStep.cs:31`, `VerifyInstallStep.cs:30` — and `SetupWizard.Run` aborts the run
on any failed `Validate` (`SetupWizard.cs:186-188`).

The bug is invisible because `SetupContext.TryGetProperty<T>` is `where T : class` and
returns `v as T` (`SetupContext.cs:32`): a missing key and a wrong-typed value produce the
same `null`.

Beyond `InstallConfig`, ~12 steps read context keys with no `InstallConfig` home at all, and
two load-bearing flags (`PerUser`, `IsSelfContained`) are **value types**, so they cannot be
read through `TryGetProperty<T>` and must be boxed under exact key names.

## 1. Goals

1. A single, tested projection `InstallProject → InstallConfig`. Acceptance: unit test
   asserts every load-bearing field maps (per the table in §2.2).
2. One place builds the install `SetupContext`, for all four callers (silent install, UI
   install, uninstall, self-test). Acceptance: `Program.cs` contains no direct
   `context.Properties[...]` assignments.
3. `/SELFTEST` passes end to end. Acceptance: install → manifest → uninstall → cleanup all
   report PASS (this is the phase's headline exit criterion).
4. Silent install and uninstall work against a real built installer. Acceptance:
   `Setup.exe /S /D=<tmp>` installs files and registers uninstall; `/UNINSTALL` reverses it.
5. `RuntimeProjectContext.Current` (global mutable static) is deleted; steps read the typed
   context. Acceptance: the file is gone and grep finds no references.

## 2. Design

### 2.1 Where the mapper lives

`Beep.Installer/Engine/InstallConfigProjector.cs` (moves to `Beep.Installer.Core` in P3).

**Not** in BeepDM: the projection's *source* is the authoring model, which BeepDM's scope doc
places outside BeepDM (R0 §3). The projection's *target* is a BeepDM type, which the
installer already references. So the installer is the correct owner — it depends inward on
BeepDM, never the reverse.

### 2.2 The projection

```csharp
public static class InstallConfigProjector
{
    public static InstallConfig ToInstallConfig(InstallProject p, string payloadRoot)
    {
        var cfg = new InstallConfig
        {
            SchemaVersion       = InstallConfig.CurrentSchemaVersion,
            ConfigDirectory     = payloadRoot,          // drives ResolvePayloadRoot fallbacks
            ProductName         = p.AppName,
            ProductVersion      = p.AppVersion,
            Publisher           = p.AppPublisher,
            DefaultInstallPath  = p.DefaultDirName,
            StartMenuFolder     = p.DefaultGroupName,
            DefaultInstallType  = Map(p.DefaultInstallType),   // InstallationTypeEx -> InstallationType
            RequireAdminPrivileges = p.PrivilegesRequired == PrivilegeLevel.Admin,
            Prefer64Bit         = p.Prefer64Bit,               // LOAD-BEARING: registry view
            LicenseText         = p.LicenseText,
            BannerImagePath     = p.WizardImageFile,
            ProductIconPath     = p.SetupIconFile,
            SupportUrl          = p.AppSupportURL,
            UpdateUrl           = p.AppUpdatesURL,
            UpdateMode          = Map(p.AppUpdateMode),
            Components          = new List<InstallComponent>(p.Components),   // same type — no conversion
            Prerequisites       = new List<Prerequisite>(p.Prerequisites),
            Shortcuts           = new List<ShortcutDefinition>(p.Shortcuts),
            RegistryEntries     = new List<RegistryOperation>(p.RegistryEntries),
            EnvironmentVariables= new List<EnvironmentVariableOp>(p.EnvironmentVariables),
        };
        return cfg;
    }
}
```

Field-mapping table the unit test asserts (load-bearing marked ★ — verified consumers):

| `InstallProject` | `InstallConfig` | Consumer |
|---|---|---|
| `AppName` ★ | `ProductName` | `VerifyInstallStep.cs:61`, `SystemRestoreStep.cs:25`, `CustomActionStep.cs:149` macro, `UpgradeEngine.cs:131` |
| `AppVersion` ★ | `ProductVersion` | `VerifyInstallStep.cs:62`, `CustomActionStep.cs:150`, `UpgradeEngine.cs:133` |
| `AppPublisher` | `Publisher` | `CustomActionStep.cs:151` macro |
| `DefaultDirName` | `DefaultInstallPath` | `PrerequisiteCheckStep.cs:116` fallback only |
| `DefaultGroupName` | `StartMenuFolder` | `ShortcutCreateStep.cs:85` fallback |
| `Prefer64Bit` ★ | `Prefer64Bit` | `InstallScope.Is64Bit:26` → registry view for Registry/COM/SharedFile/Uninstall steps |
| `Components` ★ | `Components` | the core — `FileCopyStep.cs:52` etc. (identical type) |
| `Prerequisites` ★ | `Prerequisites` | `PrerequisiteCheckStep.cs:86-87` |
| `Shortcuts` ★ | `Shortcuts` | `ShortcutCreateStep.cs:43` |
| `RegistryEntries` ★ | `RegistryEntries` | `RegistryWriteStep.cs:27,38` |
| `EnvironmentVariables` | `EnvironmentVariables` | ⚠️ inert — no step applies it (R0 §2.2); mapped for completeness, wired in P2 |
| `LicenseText`, `WizardImageFile`, `SetupIconFile`, `AppSupportURL`, `AppUpdatesURL`, `AppUpdateMode`, `DefaultInstallType`, `PrivilegesRequired` | respective | decorative at runtime; mapped so `install-config.json` is complete |

**Not mapped** (authoring/build-only, deliberately absent from `InstallConfig`):
`OutputFormat`, `Compression*`, `SolidCompression`, `SingleFile`, `OutputDir`,
`OutputBaseFilename`, `Msix*`, `CodeSign*`, `SourceDirectory`, `SourceIncludes/Excludes`,
`ArchitecturesAllowed`, theme colors, `CustomPages`, `EnabledWizardPages`. These are exactly
the fields BeepDM's scope doc excludes.

`SelfContained`, `DefaultScope`/`AllowScopeSelection`, `PayloadFolderName`,
`CreateRestorePoint`, `CreateUninstallEntry` have **no `InstallConfig` home but are
runtime-relevant** — they flow as context keys in §2.3, and are candidates for extending
`InstallConfig` upstream (Open Decision D3).

### 2.3 `InstallContextBuilder` — one place that builds the context

`Beep.Installer/Engine/InstallContextBuilder.cs`. Replaces every ad-hoc
`context.Properties[...]` write in `Program.cs` (`:256-265`, `:308-312`, `:342-344`) and
`BeepModernInstallerForm`.

```csharp
public static SetupContext ForInstall(InstallProject p, string installPath,
                                      bool perUser, string payloadRoot,
                                      RollbackManager rollback)
{
    var ctx = new SetupContext();
    ctx.Properties["InstallConfig"]   = InstallConfigProjector.ToInstallConfig(p, payloadRoot);
    ctx.Properties["InstallPath"]     = installPath;                 // string
    ctx.Properties["PerUser"]         = perUser;                     // boxed bool — InstallScope.cs:22
    ctx.Properties["IsSelfContained"] = p.SelfContained;             // boxed bool — PrerequisiteCheckStep.cs:71
    ctx.Properties["PayloadRoot"]     = payloadRoot;                 // FileCopyStep.cs:48
    ctx.Properties["RollbackManager"] = rollback;                    // FileCopyStep.cs:99
    ctx.Properties["CustomActions"]   = new List<CustomAction>(p.CustomActions);
    ctx.Properties["CustomValues"]    = customValues;                // {Custom:field} macros
    return ctx;
}
```

Key names are exact string literals today; they become `InstallContextKeys` constants
(installer-side) so a typo cannot silently produce the current failure mode. Value-type keys
(`PerUser`, `IsSelfContained`) **must** be boxed plain — steps read them via
`Properties.TryGetValue` + pattern match, not `TryGetProperty<T>`.

`ForUninstall` sets `InstallPath`, `InstallConfig` (for scope), `PerUser`, `CustomActions`.

### 2.4 Payload root and `PerUser` correctness

- `ConfigManager.ResolvePayloadRoot` hardcodes the folder name `"payload"`
  (`ConfigManager.cs:119`) and ignores `InstallProject.PayloadFolderName`. The builder
  therefore always sets `PayloadRoot` explicitly (it wins at `FileCopyStep.cs:48`), computed
  from `PayloadFolderName`. `PayloadPrepareStep` keeps ownership of *locating* the payload
  but writes the result through the builder's key rather than the global static.
- `PerUser` currently derives from an inline expression in `Program.cs:241`. It moves into
  `InstallScopeResolver` as the single decision point (`PrivilegesRequired`, `DefaultScope`,
  `AllowScopeSelection`, plus the wizard's user choice). Getting this wrong silently sends
  every registry write to HKLM (`InstallScope.cs:22`).

### 2.5 Deleting the global static

`RuntimeProjectContext.Current` (`Engine/RuntimeProjectContext.cs:8`) was read by
Core-owned `Steps/PayloadPrepareStep.cs` and `Steps/PayloadDownloadStep.cs` for
`PayloadFolderName`, `SourceDirectory`, and `Compression`. Those three values move into
context keys set by `InstallContextBuilder`; both steps take them from `SetupContext`. The
file is then deleted along with all direct writes.

### 2.6 Enum de-duplication

`InstallationTypeEx`/`UpdateModeEx` (`Models/InstallProject.cs:80-91`) are deleted and
replaced by BeepDM's `InstallationType`/`UpdateMode`; the serializer's hand-written mappers
(`InstallerScriptSerializer.cs:1000-1019`) collapse to strict `Enum.TryParse` handling for
the current `.bsetup` contract.

## 3. API surface

No HTTP endpoints. The runtime artifact contract gains one file: the build stamps
`install-config.json` (camelCase, per `ConfigManager.cs:15-21`) beside the payload, so a
shipped installer can be inspected and so `ConfigManager.Load` becomes a valid alternate
entry point. `.bsetup` remains the authoring format.

## 4. Files to change

| Action | File | Lines | Risk |
|--------|------|-------|------|
| New | `Engine/InstallConfigProjector.cs` | ~120 | medium |
| New | `Engine/InstallContextBuilder.cs` + `InstallContextKeys.cs` | ~140 | medium |
| Modify | `Program.cs` (4 call sites use the builder) | ~-60/+25 | medium |
| Modify | `Forms/BeepModernInstallerForm.cs` (UI install path uses the builder) | ~30 | medium |
| Modify | Core-owned `Steps/PayloadPrepareStep.cs`, `Steps/PayloadDownloadStep.cs` (context, not global) | ~40 | medium |
| Modify | `Models/InstallProject.cs` (drop `...Ex` enums) | ~-15 | low |
| Modify | `Engine/InstallerScriptSerializer.cs` (strict enum mapping) | ~30 | medium |
| Modify | `Engine/InstallScopeResolver.cs` (single `PerUser` decision) | ~30 | low |
| Modify | `Engine/BuildPipeline.cs` (also emit `install-config.json`) | ~25 | low |
| Delete | `Engine/RuntimeProjectContext.cs` | -9 | medium |
| New | tests: projector mapping, context-key completeness, selftest E2E | ~200 | low |

## 5. Error handling matrix

| Operation | Before | After |
|-----------|--------|-------|
| Step needs `InstallConfig` | `Validate` fails → whole run aborts with a generic message; root cause invisible | context always carries it; a missing key is caught by a context-completeness test before shipping |
| Wrong key/type supplied | silent `null` (`TryGetProperty` `as T`) | key constants + builder eliminate the class of bug; builder validates required keys and throws with the exact key name |
| `PerUser` unset | silently writes HKLM | resolved once in `InstallScopeResolver`; asserted in the context test |
| `IsSelfContained` unset | `PrerequisiteCheckStep` hard-fails on a machine without .NET | always set from `InstallProject.SelfContained` |

## 6. Dev-mode contract

`.bsetup` files follow the current schema. Remove discarded enum names and stale runtime
aliases instead of carrying alternate parsing tables.

## 7. Verification

```
Beep.Installer.exe /SELFTEST                 # MUST pass — the exit criterion
Beep.Installer.exe /BUILD=samples\HelloApp\...\*.bsetup /OUT=%TEMP%\p1
%TEMP%\p1\Setup-*.exe /S /D=%TEMP%\p1install # files land; install-manifest.json written
%TEMP%\p1\Setup-*.exe /UNINSTALL /D=%TEMP%\p1install
dotnet test Beep.Installer.Tests             # projector + context tests green; P0 skips unskipped
```

## 8. Risks

| Risk | Mitigation |
|------|------------|
| Silent context-key drift returns later | `InstallContextKeys` constants + a test that asserts every key BeepDM reads is produced by the builder (key list in R0 §2.1) |
| BeepDM steps have inert fields, so a "correct" mapping still no-ops (env vars, conditions) | documented in R0 §2.2; wiring them is P2, not silently assumed here |
| `PerUser` regression sends writes to the wrong hive | single decision point + explicit per-user and per-machine self-test variants |
| Duplicate `StepId` throws at `Build()` if both COM steps are ever added | wizard graph factory (P5) owns step selection; note recorded now |

## 9. Out of scope

- Wiring BeepDM's inert contract surface (env-var step, condition gating, ARP uninstall key) — **P2**.
- Extracting authoring code into a library — **P3**.
- Extending `InstallConfig` upstream — Open Decision D3, executed in P2 if approved.
- Any UI change beyond routing the install path through the builder.

## 10. Sub-task execution order

1. **1.A.1** `InstallConfigProjector` + mapping unit test. Verify: test green.
2. **1.A.2** `InstallContextKeys` + `InstallContextBuilder` (install + uninstall) + completeness test. Verify: test green.
3. **1.A.3** Route `Program.cs` silent-install/uninstall/selftest through the builder. Verify: `/SELFTEST` passes.
4. **1.B.1** Move payload steps off `RuntimeProjectContext` onto context keys. Verify: `/SELFTEST` still passes; payload located from a built exe.
5. **1.B.2** Delete `RuntimeProjectContext`; route the UI install path through the builder. Verify: wizard install works.
6. **1.B.3** Drop `...Ex` enums; strict serializer enum parse. Verify: both sample `.bsetup` files round-trip unchanged.
7. **1.C.1** Single `PerUser` decision in `InstallScopeResolver`; emit `install-config.json` at build. Verify: per-user and per-machine installs both land in the right hive/path.
