# Phase 9: Test Migration & Full Regression — Design Document

**Status:** ⬜ not started · **Priority:** P0 gate (runs partly inside every phase, closes at the end)
**Depends on:** all phases
**Tracker:** [MASTER_TRACKER.md](MASTER_TRACKER.md)

## 0. Problem Statement

`Beep.Installer.Tests` has 160 test methods across 32 files (serializer, build pipeline,
ClickOnce, rollback, scanner, packaging, a11y, autosave, E2E). P1–P4 move the code under
test into BeepDM (and possibly `Beep.Installer.Packaging`), which breaks test project
references and namespaces. The moved logic must keep its coverage — in the right place —
and the whole program needs one final gate proving the thin-shell refactor changed
structure, not behavior. CI today is a plain .NET Desktop workflow
(`.github/workflows/`, commit `23ceb23`) that must learn about the BeepDM project
dependency.

## 1. Goals

1. Every moved class keeps (or increases) its test coverage, with engine tests living
   beside the engine: new `BeepDM` test target (or existing BeepDM test project if one
   exists — verify) for Authoring/Build/Installer suites; Packaging tests follow their
   project. Acceptance: per-suite counts before/after moves are equal or higher.
2. Shell keeps a slim test set: CLI parsing, wizard graph factory, page validation logic,
   theme tokens. Acceptance: `Beep.Installer.Tests` still runs headless.
3. Golden-file / parity artifacts from P2 (serializer round-trip) and P3 (build output
   parity) are permanent regression tests, not one-off checks.
4. CI builds the full dependency chain (BeepDM → Winform.Controls → Installer →
   Packaging → tests) and runs `/SELFTEST` as a smoke step. Acceptance: green pipeline
   on a clean runner.
5. SOLID checklist review recorded per phase (tracker `N.M` rows).

## 2. Design

### 2.1 Test redistribution map

| Current suite | Destination |
|---|---|
| `InstallerScriptSerializerTests`, `SourceScannerTests`, `GlobMatcherTests`, `ConditionListValidatorTests`, `CustomActionValidatorTests`, `ProjectTemplatesTests`, `AutoSaveTests`, `ComponentSelectionTests`, `InstallScopeTests` | `Beep.Installer.Core.Tests` (Authoring) |
| `InstallerBuilderTests`, `CompressionTests`, `EmbeddedPayloadTests`, `EdgeCaseTests`, `EndToEndTests` | `Beep.Installer.Core.Tests` (Build) — E2E also stays runnable against the shell exe |
| `ClickOnceTests`, `ClickOnceRuntimeTests`, `PublishTests`, `PublishIntegrationTests`, `UpdateApplierTests`, `UpdateCheckerTests`, `TrustCheckerTests`, `MsixPackagerTests`, `StoreReadinessTests` | `Beep.Installer.Core.Tests` (Packaging) |
| `SharedFileCountTests`, `RollbackTests`, `ScriptingAndRollbackTests`, `ComRegistrationTests` | **BeepDM** `tests/InstallerTests` — these exercise BeepDM steps, which had **zero** coverage before P2 (R0 §2.2) |
| `AccessibilityTests`, `ThemeLoaderTests`, `CustomPageTests`, `DiagTests` | stay in `Beep.Installer.Tests` (shell) |
| **new** — projector mapping + context-key completeness (P1) | `Beep.Installer.Core.Tests` |

Rule: tests move in the **same commit** as the class they cover; nothing is deleted
without its replacement running green.

### 2.2 New permanent suites

- `BsetupGoldenRoundTripTests` — both repo sample `.bsetup` files + a maximal synthetic
  script: load → write → byte diff (from P2.A.1).
- `BuildOutputParityTests` — HelloApp build: manifest of staged files + footer offsets
  (from P3.B.1).
- `CliParityTests` — capture `/?`, `/VER`, `/VALIDATE`, `/PREVIEW` outputs (from P5.A.1).
- `LanguageResxParityTests` (from P7.A.1); security gates (from P8.B.1).

### 2.3 CI workflow

Extend the existing GitHub Actions .NET Desktop workflow:

1. checkout with sibling repos (BeepDM, Beep.Winform) — actions checkout paths or a
   composite/submodule strategy (spike: today the csproj uses relative
   `..\..\BeepDM\...` references, `Beep.Installer.csproj:18-20`; CI must reproduce that
   tree or switch to NuGet package references — record as D5 follow-up if tree checkout
   is too heavy).
2. `dotnet build` solution, `dotnet test` all test projects.
3. Smoke: `Beep.Installer.exe /SELFTEST` + `/BUILD` HelloApp + run built Setup `/S` +
   `/UNINSTALL` in the runner temp.

## 3. API surface

N/A.

## 4. Files to change

| Action | File | Lines | Risk |
|--------|------|-------|------|
| New/Move | BeepDM engine test project + moved suites | ~large, mechanical | medium |
| New | `Beep.Installer.Packaging.Tests` (D3=A) | ~mechanical | low |
| Modify | `Beep.Installer.Tests` (slim to shell scope) | -many | low |
| Modify | `.github/workflows/*.yml` (dependency tree + smoke) | ~60 | medium |
| New | golden/parity/CLI suites listed in §2.2 | ~400 | low |

## 5. Error handling matrix

N/A (test phase).

## 6. Backward compatibility

N/A.

## 7. Verification (the phase IS verification)

Final gate, run on clean machine + CI:

```
dotnet build <solution incl. BeepDM>
dotnet test  (all projects)                       # ≥160 tests, 0 fail
Beep.Installer.exe /SELFTEST                       # PASS
Beep.Installer.exe /BUILD=samples HelloApp → /S → /UNINSTALL   # clean E2E
150% DPI + Narrator + ar-culture manual checklist (from P6/P7)
SOLID checklist (dev-lifecycle §5) recorded per phase in tracker
```

## 8. Risks

| Risk | Mitigation |
|------|------------|
| CI can't see sibling repos | D5 spike early (during P1), not at the end |
| Test moves conflated with logic changes | move-only commits, rename-detection-friendly |
| E2E tests flaky on runners (elevation, MSIX tooling) | tag `RequiresWindowsTooling`, run conditionally |

## 9. Out of scope

New feature tests beyond parity; performance benchmarking (backlog).

## 10. Sub-task execution order

1. **9.A.1** D5 spike: CI dependency-tree strategy. Verify: green build on runner with BeepDM reference.
2. **9.A.2** (continuous) per-phase test moves + parity suites as scheduled in P2/P3/P5/P7/P8.
3. **9.B.1** Redistribute remaining suites per §2.1 map. Verify: total ≥160, all green.
4. **9.B.2** CI smoke steps (`/SELFTEST`, E2E build+install+uninstall). Verify: green pipeline.
5. **9.B.3** Final manual matrix (DPI/Narrator/RTL) + SOLID review rows in tracker. Verify: tracker all ✅.
