# Phase 0: Make It Build, Bind, and Test Correctly — Design Document

**Status:** ⬜ not started · **Priority:** P0 — blocks every other phase
**Tracker:** [MASTER_TRACKER.md](MASTER_TRACKER.md) · **Evidence:** [R0_REVIEW_FINDINGS.md](R0_REVIEW_FINDINGS.md)

> This phase did not exist in the first draft of the plan. It was added after verifying
> the actual runtime and build state: **the installer does not currently work end to end,
> and its test suite does not compile.** No refactoring can be validated until this is fixed.

## 0. Problem Statement

Three independent defects, all P0:

### 0.1 The app binds to a stale `DataManagementModels.dll` and throws at runtime

`Beep.Installer.csproj:18-20` project-references `DataManagementEngine.csproj`,
`TheTechIdea.Beep.Winform.Controls.csproj`, and `TheTechIdea.Beep.Vis.Modules.csproj` —
but **not** `DataManagementModels.csproj` directly. Meanwhile
`TheTechIdea.Beep.Winform.Controls.csproj:4720` and
`TheTechIdea.Beep.Vis.Modules.csproj:42` both `PackageReference`
`TheTechIdea.Beep.DataManagementModels` **3.1.1** (NuGet).

`DataManagementModels.csproj:14-21` declares `<Version>3.1.1</Version>` with no separate
`AssemblyVersion`/`FileVersion`, so the **source build and the NuGet package have byte-identical
assembly identity (3.1.1.0)**. NuGet's graph keys on id+version and picks the nearest-to-root
node; the installer's only project node for Models is *transitive* (depth 2 via
DataManagementEngine) while Winform.Controls contributes a *package* node — so the package wins:

- `Beep.Installer\obj\project.assets.json:354` → `"TheTechIdea.Beep.DataManagementModels/3.1.1": { "type": "package" }`
- Contrast `Beep.Winform.Data.Integrated.Controls\obj\project.assets.json:533` → `"type": "project"`

**Compounding root cause — a republished immutable version.** The local folder feed
`C:\Users\f_ald\source\repos\LocalNugetFiles` (registered in
`%AppData%\NuGet\NuGet.Config`) holds
`TheTechIdea.Beep.DataManagementModels.3.1.1.nupkg` dated **2026-07-19** (2,572,161 bytes),
while the extracted global-cache copy at
`%USERPROFILE%\.nuget\packages\thetechidea.beep.datamanagementmodels\3.1.1\` is dated
**2026-07-14** (`.nupkg.metadata` records `"source": "...LocalNugetFiles"`). The version was
**overwritten in place with new content**. NuGet treats a version as immutable: because 3.1.1
is already extracted, restore never re-extracts it. `ISetupStateStore` was added to BeepDM
source on **2026-07-16**, i.e. after the cached extract — so it is absent from the loaded
assembly.

Consequence, reproduced: running the shipped entry point fails with
`System.TypeLoadException: Could not load type 'TheTechIdea.Beep.SetUp.State.ISetupStateStore'
from assembly 'DataManagementModels, Version=3.1.1.0'` thrown from
`SetupWizardBuilder.Build()`. **Every install, uninstall, and self-test path is dead.**

### 0.2 The test project does not compile

`Beep.Installer.Tests` still references types renamed in the July 9 commits and never updated
(last test commit is `b408c11`, 2026-07-06):

| Error | Location |
|---|---|
| `InstallerBuilder` not found (renamed → `BuildPipeline`) | `EndToEndTests.cs:237`, `EdgeCaseTests.cs:163,183,203,212,226`, `InstallerBuilderTests.cs:148,159,176` |
| `BuildProgress` not found (now nested in `BuildPipeline`) | `InstallerBuilderTests.cs:157,158,163` |
| `List<InstallComponent>` → `ObservableCollection<InstallComponent>` | `SharedFileCountTests.cs:102` |
| `ObservableCollection<string>` has no `AddRange` | `InstallerScriptSerializerTests.cs:182` |
| `ConditionType.ArchitecturesAllowed` does not exist | `InstallerScriptSerializerTests.cs:200` |
| `Architecture` enum ↔ `string` mismatches | `InstallerScriptSerializerTests.cs:200,271`; `StoreReadinessTests.cs:90` |
| `int` → `CompressionStrength` | `InstallerScriptSerializerTests.cs:274` |
| `'original' is a variable but used like a type` | `InstallerScriptSerializerTests.cs:247` |

So the "160 test methods" are **not a safety net** — zero of them have run since the rename.

### 0.3 CI cannot catch either problem

`.github/workflows/dotnet-desktop.yml` is the unmodified GitHub template with placeholder
values (`Solution_Name: your-solution-name`, `Test_Project_Path: your-test-project-path`).
It never builds this solution or runs these tests. Also, `Beep.Installer.slnx` contains only
the installer + tests — no BeepDM or Beep.Winform projects — so nothing forces a coherent
rebuild of the dependency tree.

## 1. Goals

1. The installer binds to **source** BeepDM, deterministically. Acceptance:
   `Beep.Installer\obj\project.assets.json` shows
   `"TheTechIdea.Beep.DataManagementModels/...": { "type": "project" }`, and the output
   `DataManagementModels.dll` matches the freshly built source DLL byte-for-byte.
2. Version identity is honest again. Acceptance: BeepDM source version no longer collides
   with a differently-contented published 3.1.1; the local feed carries the new version.
3. `Beep.Installer.Tests` compiles and runs. Acceptance: `dotnet test` executes; every
   test either passes or fails on a *real assertion*, not a compile error.
4. CI actually builds this solution and runs these tests. Acceptance: workflow references
   real paths and fails when 0.1/0.2 regress.

**Explicit non-goal for this phase:** making the install *succeed*. Even after 0.1 is fixed,
installs still fail because the installer never populates `InstallConfig` — that is P1.
This phase gets us to a state where that failure is *observable and testable* rather than
masked by a `TypeLoadException`.

## 2. Design

### 2.1 Fix assembly binding (0.1)

Three coordinated changes, in order:

**(a) Purge the poisoned cache entry.** The extracted 3.1.1 in the global packages folder
contains Jul-14 bits that no longer match the feed. Delete
`%USERPROFILE%\.nuget\packages\thetechidea.beep.datamanagementmodels\3.1.1\` (and the
matching `thetechidea.beep.datamanagementengine\3.1.1\`) before any restore. Without this,
every package-based resolution keeps serving stale bits.

**(b) Add the direct project reference** — the precedent used by five sibling projects that
successfully mix BeepDM source with Winform.Controls
(`Beep.Winform.Data.Integrated...csproj:73-76`, `Beep.Desktop.Common.csproj:63-66`,
`Beep.Desktop.IDE.Extensions.csproj:183-186`, `TheTechIdea.Beep.TreeNodes.csproj:56-58`,
`TheTechIdea.Beep.MVVM.csproj:39-41`). In `Beep.Installer.csproj`:

```xml
<ProjectReference Include="..\..\BeepDM\DataManagementModelsStandard\DataManagementModels.csproj" />
```

A direct depth-1 project node outranks the transitive package node, flipping resolution to
`"type": "project"`. Blast radius: this project only. Expect NU1605 downgrade warnings;
suppress with a repo-local `Directory.Build.props` carrying `<NoWarn>$(NoWarn);NU1605</NoWarn>`
— the same remedy already used at `Beep.OilandGas\Directory.Build.props` and
`Beep.Desktop\TheTechIdea.Beep.Desktop.IDE.Extensions\Directory.Build.props`.

**(c) Bump BeepDM source version** to `3.1.2` in `DataManagementModels.csproj:14` and
`DataManagementEngine.csproj:9`, and repack so the local feed is truthful. This prevents
recurrence: never overwrite a published version again. (b) is what unblocks today; (c) is
what stops the trap resetting.

**Deferred to a follow-up (recorded, not done here):** fixing the real design defect in
`TheTechIdea.Beep.Vis.Modules.csproj:45-48`, whose conditional ItemGroup adds *packages* when
BeepDM source is **absent** but never adds *project references* when it is **present** — the
inverse of the intent. The correct shape is the guarded pattern at `BeepShell.csproj:41-43`.
Changing Beep.Winform affects many downstream consumers (Beep.WPF, BeepWeb, Beep.Desktop,
Beep.OilandGas, Beep.AI.*), so it needs its own change window — see Open Decision D6.

### 2.2 Repair the test project (0.2)

Mechanical, no behavior change: rename `InstallerBuilder` → `BuildPipeline`,
`BuildProgress` → `BuildPipeline.BuildProgress`, wrap list literals in
`ObservableCollection<T>`, replace `AddRange` with a loop or collection initializer, fix the
`Architecture`/`string` and `int`/`CompressionStrength` conversions, correct the
`ConditionType.ArchitecturesAllowed` reference to the real member, and fix the shadowed
`original` identifier at `InstallerScriptSerializerTests.cs:247`.

Tests that then fail for *real* reasons (notably anything exercising install steps, which
will fail on the missing `InstallConfig`) get `[Fact(Skip="P1: InstallConfig bridge")]` with
the skip reason naming the phase that fixes them — so the count is honest and the debt is
visible rather than deleted.

### 2.3 Make CI real (0.3)

Replace the template placeholders with the actual solution and test paths, and check out the
sibling repos the build needs (BeepDM, Beep.Winform) since the references are relative paths.
Add a step asserting the resolved Models DLL came from source, so 0.1 cannot silently return.
The full multi-repo CI strategy remains Open Decision D5.

## 3. API surface

None. No product code behavior changes in this phase.

## 4. Files to change

| Action | File | Lines | Risk |
|--------|------|-------|------|
| Modify | `Beep.Installer\Beep.Installer.csproj` (add Models ProjectReference) | +1 | low |
| New | `Beep.Installer\Directory.Build.props` (NoWarn NU1605) | ~5 | low |
| Modify | `BeepDM\DataManagementModelsStandard\DataManagementModels.csproj` (version 3.1.2) | ~1 | medium |
| Modify | `BeepDM\DataManagementEngineStandard\DataManagementEngine.csproj` (version 3.1.2) | ~1 | medium |
| Modify | `Beep.Installer.Tests\*.cs` (9 files — compile fixes + honest skips) | ~60 | low |
| Modify | `.github\workflows\dotnet-desktop.yml` | ~40 | medium |
| Env | purge poisoned global-cache entries for 3.1.1 | n/a | low |

## 5. Error handling matrix

| Operation | Before | After |
|-----------|--------|-------|
| App startup in runtime mode | `TypeLoadException` from `SetupWizardBuilder.Build()`, crash log | loads; proceeds to the (still-failing, P1) step validation with a clear message |
| `dotnet test` | build error, 0 tests run | tests run; step-dependent ones explicitly skipped with a phase reference |
| Stale package silently used | invisible | CI assertion fails the build |

## 6. Backward compatibility

The version bump to 3.1.2 means consumers pinned to `3.1.1` keep resolving the old package
until they bump — which is correct and intended, because 3.1.1's published content is
ambiguous. Nothing in the installer's own file formats or CLI changes.

## 7. Verification

```
# after cache purge + restore
dotnet build Beep.Installer\Beep.Installer.csproj
#   assert: obj\project.assets.json shows "type": "project" for DataManagementModels
#   assert: bin output DataManagementModels.dll == BeepDM source build output
Beep.Installer.exe /VER          # starts without TypeLoadException
Beep.Installer.exe /SELFTEST     # expected: reaches step validation and fails on InstallConfig (P1), NOT TypeLoadException
dotnet test Beep.Installer.Tests # compiles; runs; skips are explicit
```

Note the self-test is expected to still fail here — but for the *right* reason. That
distinction is the phase's exit criterion.

## 8. Risks

| Risk | Mitigation |
|------|------------|
| Version bump ripples to other BeepDM consumers | bump is additive; consumers pinned to 3.1.1 are unaffected until they choose to move |
| NU1605 downgrade warnings become errors under `TreatWarningsAsErrors` | repo-local `Directory.Build.props` NoWarn, matching existing precedent |
| Purging the cache breaks another project mid-build | purge only the two poisoned ids; restore repopulates from the feed |
| Skipped tests become permanent debt | each skip must name the phase that unskips it; P9 asserts zero unexplained skips |

## 9. Out of scope

- The `InstallConfig` contract bridge (P1) — the reason installs still fail after this phase.
- Fixing `Vis.Modules`/`Winform.Controls` reference design (Open Decision D6).
- Central Package Management across the tree (rejected as a first move: zero projects use
  CPM today, so enabling it would break every csproj carrying inline `Version=`).

## 10. Sub-task execution order

1. **0.A.1** Purge poisoned 3.1.1 cache entries. Verify: folders gone.
2. **0.A.2** Add direct `DataManagementModels` ProjectReference + `Directory.Build.props`. Verify: assets.json shows `"type": "project"`.
3. **0.A.3** Bump BeepDM source to 3.1.2 and repack to the local feed. Verify: feed + cache agree; installer output DLL matches source build.
4. **0.A.4** Confirm startup no longer throws `TypeLoadException`. Verify: `/VER` and `/SELFTEST` reach step validation.
5. **0.B.1** Fix test-project compile errors. Verify: `dotnet test` builds.
6. **0.B.2** Add phase-referenced skips for step-dependent tests. Verify: run is green with explicit skip list.
7. **0.C.1** Point CI at the real solution/test paths + sibling checkouts + source-binding assertion. Verify: workflow fails if 0.1 regresses.
