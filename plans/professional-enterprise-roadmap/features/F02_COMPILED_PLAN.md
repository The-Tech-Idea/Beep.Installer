# F02 — Compiled Plan and Condition Engine

**Outcome:** all front ends and formats consume one immutable dependency graph.

**Design:** normalize defaults, paths, identities, conditions, hashes and resource dependencies into canonical JSON; separate environment facts from desired state; support typed all/any/not comparisons without arbitrary code.

**Acceptance:** identical inputs/facts yield the same plan hash; cycles and missing references fail preflight; `plan --json` is redacted; current interactive and silent graphs are equivalent. Depends on F01.

## Implementation notes — 2026-08-30

- Added `ResourcePlanExecutor` to apply supported compiled-plan operations through registered providers.
- Added execution journal entries for validate/detect/plan/apply/verify/rollback/skip.
- Added immediate reverse-order rollback when a later provider operation fails.
- Added setup context keys for the authoring project, compiled plan, provider registry, and resource execution journal.
- Added `ResourceProviderStep` to the common install graph so compiled-plan providers run consistently for UI and silent installs.
- Converted `file.copy` into a provider-native operation with install-root safety validation, copy/overwrite behavior, optional-file skips, verification, and backup-aware rollback.
- Converted `registry.write` into a provider-native operation with scope-selected HKCU/HKLM writes, hive-relative validation, value-kind conversion, verification, and rollback restore/delete behavior.
- Added atomic JSON persistence for resource execution journals and wired the default journal path into install/uninstall contexts.
- Updated `ResourceProviderStep` to persist the execution journal on both successful and failed provider runs.
- Converted `environment.set` into a provider-native operation with scope selection, per-user machine-scope downgrade, install-path expansion, verification, rollback and fakeable tests.
- Converted `shortcut.create` into a provider-native operation with deterministic link-path resolution, target existence checks, verification, rollback restore/delete and fakeable tests.
- Rewired full install and repair graphs so common typed resources run through `ResourceProviderStep` instead of duplicate direct file/shortcut/registry/environment steps.
- Added manifest-compatible output publishing from the compiled plan so `VerifyInstallStep` continues to receive installed files, registry entries, environment variables, and shortcuts while the runtime moves to provider-owned lifecycle state.
- Added `component.select` execution and dependency-skip propagation so resources for unselected optional components do not apply.
- Filtered manifest-compatible provider outputs to operations that actually applied, preventing skipped component resources from entering uninstall manifests.
- Emitted machine-readable component condition inputs into the compiled plan (`conditionCount`, `condition.N.type`, `condition.N.operator`, `condition.N.value`, `condition.N.value2`) while retaining the display condition string for diagnostics.
- Added provider-safe condition evaluation through injectable environment facts, covering `AlwaysTrue`, `AlwaysFalse`, OS version, architecture, file/directory existence, registry key/value, command result, and administrator checks.
- Wired resource condition facts through `ResourceProviderStep`, so tests and future hosts can evaluate plans deterministically without touching the real machine.
- Added redacted operation snapshots to successful provider apply journal entries, enabling later replay without re-compiling or depending on direct uninstall manifests.
- Made the real uninstall and self-test uninstall graphs use `ResourceProviderUninstallStep`, which replays successful provider applications in reverse order and removes product-owned runtime artifacts after successful replay.
- Bound provider journals to product name, product version, compiled plan hash, install scope, attempt id and timestamps, so replay can verify it is acting on the exact plan that created the journal.

## Implementation notes — 2026-08-31 grouped condition expressions

- Added `ConditionExpressionMode` to the shared installer component model.
- Components now support `ConditionExpression = All | Any | Not`, with `All` remaining the default deterministic mode.
- The `.bsetup` serializer round-trips `ConditionExpression` on `[Components]` and also accepts `Expression`/`Mode` on `[Conditions]` entries for authoring convenience.
- The compiled plan emits `conditionExpression` beside the existing `condition.N.*` inputs, keeping one `component.select` condition path.
- `ComponentSelectResourceProvider` evaluates grouped conditions as all/any/not against the existing injectable environment facts.
- `ComponentSelection` authoring preview uses the same expression mode, so UI visibility and provider execution agree.
- `ServiceApp.bsetup` includes a harmless `ConditionExpression: any` sample that compiles to machine-readable plan metadata.

## Implementation notes — 2026-08-31 unsupported-operation policy

- Added `unsupportedMsiOperationSeverity` to the organization policy contract.
- `error` keeps unsupported MSI export capabilities as release-blocking findings.
- `warning` allows MSI export to continue while preserving `BI1601` findings in `msi-capabilities.json`.
- Policy severity controls MSI unsupported-operation behavior; without policy, unsupported MSI nodes remain release-blocking errors.
- The sample enterprise policy pins the default to `error`.

## Implementation notes — 2026-09-01 fact-aware condition evaluator consolidation

- Added `InstallerConditionFactsEvaluator` as the canonical installer-side evaluator for typed component conditions.
- `ComponentSelectResourceProvider` now delegates grouped all/any/not condition checks to the shared evaluator instead of carrying a private copy.
- `ComponentSelection` authoring/runtime preview now accepts injectable `IInstallerConditionFacts`, install root and variable values, so UI availability and provider execution can be driven by the same deterministic facts.
- The component-conditions dialog test action now uses `ComponentSelection.IsAvailable`, keeping the visible authoring result aligned with runtime gating.
- `SelectedSize` now excludes components whose conditions fail, including required components that are not actually available on the target facts.
- Focused coverage proves authoring preview, install-type selection, selected size, compiled condition metadata and resource-provider execution all agree under injected environment facts.

## Implementation notes — 2026-09-02 compiled-plan qualification gate

- Added shared `BuiltInInstallerResourceProviders.CreateDefaultRegistry()` in Core so runtime install execution and qualification checks consume one provider list.
- Added `/QUALIFYPLAN=<script.bsetup>` and `--qualify-plan=<script.bsetup>`.
- Added JSON response-file key `qualifyPlan`.
- Added `CompiledPlanQualificationRunner`, which:
  - loads a `.bsetup` project;
  - compiles the plan twice and verifies deterministic `PlanHash`;
  - verifies every compiled operation type has a provider in the shared default registry;
  - verifies dependency references resolve and do not self-reference;
  - writes `compiled-plan.json` and checks seeded secrets are not leaked;
  - verifies the default provider registry covers professional installer resource types.

## Verification

- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "CompiledPlanQualificationRunnerTests|EnterpriseCommandLineTests.CompiledPlanQualificationArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `/QUALIFYPLAN="Beep.Installer\samples\ServiceApp.bsetup" /OUT="artifacts\f02-compiled-plan-qualification-dev"` passed all 7 qualification scenarios.

Remaining hardening: run VM-level replay/fault-injection coverage on official Windows targets and archive the resulting evidence.
