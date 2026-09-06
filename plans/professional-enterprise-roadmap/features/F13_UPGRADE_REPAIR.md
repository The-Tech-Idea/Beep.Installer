# F13 — Upgrade and Repair

## Current display-rename maintenance contract — 2026-09-06

The application's existing Publisher now forwards AppId into PublishStager. One canonical `Beep.{AppId}` identity drives both manifest names, their assembly identity values, dependency codebase, trust-check paths and the landing-page link. Display names remain landing-page content, not publication paths. Missing/malformed/zero AppId fails before staging and does not overwrite a previous publication. Native ClickOnce manifest/schema, signing and install/update acceptance remain unqualified; this identity change does not certify those existing implementations.

ClickOnceRuntime's SDK installation directory now uses canonical AppId/version, not display name. Its existing uninstall registration retains the shortcut path; a rename creates the new shortcut and removes only the previously recorded .lnk inside that AppId's shortcut folder. Copying no longer recursively deletes the destination, preserving unrelated user files. The runtime acquires the existing product-directory lease, rejects publisher changes for a registered AppId, validates numeric version and executable containment before copies, and records AppId in its existing marker. Nine focused runtime tests pass, including real shortcut/registry rename and user-file preservation. ClickOnce publishing manifests still require identity continuity work; this SDK runtime has no production caller in the current application, so these checks do not establish full-host lifecycle completion.

Typed-resource maintenance selects ownership by AppId and preserves publisher/version/scope checks. The existing compiler hash routine can project the installed display name for comparison without changing the current plan. Only this metadata field is normalized: changed operations, inputs, conditions or other plan fields still fail. Repair and rollback contexts expand ProductName using the installed journal name so name-based resource paths stay anchored to the installed resources. No old-name discovery or compatibility store is introduced.

Verification covers install, display rename, removal of an installed file, repair and uninstall with a ProductName-expanded destination, plus rejection of changed operation plans. Full-host renamed-product lifecycle and the broader release matrix remain open. This section supersedes the incremental name-based discovery statements below.

## AppId runtime bridge — 2026-09-06

Windows uninstall-list entries now use canonical AppId keys through the existing InstallationRegistration helper. BuildPipeline authors those keys with AppId metadata, Program annotations use the same key, and delta reconciliation updates it. ClickOnceRuntime.Install requires AppId and validates the key before file copies; its displayed product name remains metadata. Removal continues through the recorded registry operations, not a second cleanup store. Resource journal discovery and additional name-based lifecycle metadata constraints remain unfinished.

Discovery registration now uses SOFTWARE/TheTechIdea/Installations/{canonical-AppId}, with required nonzero GUID validation and no display-name lookup. UpgradeEngine stores ProductName as metadata, and runtime UpgradeStep, UninstallStep, maintenance scope discovery and delta version reconciliation pass AppId consistently. The upgrade qualification runner now authors an explicit per-run identity. A rename can retain its discovery registration; Windows uninstall-list keys and resource journal discovery remain name-based and are not yet rename-safe.

The runtime InstallConfig contract in the referenced BeepDM project now retains AppId from InstallConfigProjector. UpgradeEngine writes it into the existing registration record. A real HKCU registration check verifies projected/serialized identity persistence and cleans up its unique test key. Existing name-based key selection remains unchanged in this step; discovery, ARP keys, upgrade/uninstall calls and version reconciliation must move together before rename-safe discovery can be claimed. Dependency changes are in BeepDM/DataManagementModelsStandard/Installer/InstallConfig.cs and BeepDM/DataManagementEngineStandard/Installer/UpgradeEngine.cs.

## Operation coordination — 2026-09-06

All canonical graph factories return a coordination decorator for Run/Resume/RunAsync while forwarding steps, state, options and reports to the existing setup wizard. Direct callers acquire the shared installation lease before steps execute; CLI/UI synchronous graph calls reuse their owning thread's outer lease so host commit/rollback remains protected. Recursive same-target graphs and concurrent reuse of a wizard are rejected. Direct callers performing additional mutations after Run must hold their own outer lease and use synchronous execution on its owner thread. Graph/process verification: 51 tests passed.

Windows lease identities now use a read-only directory/file handle and the normalized NT path returned by [GetFinalPathNameByHandleW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew). Missing target components are appended after resolving the nearest existing ancestor. Resolution errors fail closed. This keeps an existing junction alias and its real target on the same lease, including not-yet-created installations below the junction. Verification: 51 graph/cache/delta/process checks and two real Windows junction cases passed. Network/mapped-drive deployment, hard-link file aliases, changing links during operations and overlapping parent/child installations are not proven by those tests.

**Outcome:** formalize and qualify the already implemented upgrade/repair foundation.

**Design:** product/channel identity, semantic version policy, backup/restore, owned-file hash repair, downgrade guard and registration commit only after verification.

**Acceptance:** fresh, same-version, minor/major upgrade, refused/forced downgrade, corrupt/missing file, locked file, failure rollback and uninstall matrices pass; user-created data remains untouched. Depends on F12.

## Implementation slice — 2026-09-06 effective installation scope

`DefaultScope` is the ownership authority, independent of elevation requirements. The shared context builder applies the selected scope to the runtime project before compilation and policy evaluation, so the plan hash and journal represent the actual installation.

`InstallContextBuilder.ForRepair` and `ForUninstall` read the installed scope from the resource journal, including an explicit journal path. A recorded scope takes precedence over the packaged default and the new-install scope-selection lock. Wrong-product or invalid-scope journals fail before resource execution. CLI repair delegates to `ForRepair`; it no longer duplicates this logic.

Verified: 86 tests across `InstallScopeTests`, `InstallContextBridgeTests`, `ResourceProviderRuntimeTests`, `RepairAndExitCodeTests` and `EnterprisePropertyCatalogTests` passed. Coverage includes both scopes, custom journal paths, policy/plan agreement, invalid journal identity and scope, and later uninstall using the installed plan hash.

`InstallScopeResolver.ResolveMaintenancePath` now searches both user and machine registrations in the project's architecture view, using the existing registration reader. An explicit `/D` bypasses discovery. Identical normalized paths are one target; distinct matches fail with instructions to select `/D`. Both CLI maintenance commands share this resolver. Discovery and journal-scope errors use the existing runtime JSON envelope, exit code 2, stderr diagnostics and support-bundle/telemetry path; missing-repair handling reuses that same failure writer.

Additional verification: 25 scope and CLI process tests passed, including both lookup scopes, ambiguity refusal, explicit-path precedence, path deduplication, invalid-journal repair/uninstall JSON, missing repair target and missing uninstall journal.

Remaining: clean-machine lifecycle qualification, including real user/machine registrations and elevation boundaries.

## Implementation slice — 2026-09-02 upgrade qualification gate

Added a first-party upgrade/downgrade qualification gate that exercises the existing `UpgradeStep`, `CommitUpgradeStep` and `UpgradeEngine` contracts instead of creating a duplicate lifecycle path:

```powershell
Beep.Installer.exe /QUALIFYUPGRADE=<script.bsetup> [/OUT=<dir>]
```

The runner writes `upgrade-qualification.json` plus per-scenario diagnostics for:

- project load;
- fresh install proceeds without backup;
- same-version maintenance proceeds without backup;
- older-to-newer upgrade creates a restorable backup and records previous-version evidence;
- downgrade is blocked unless forced and reports the installed version plus `/FORCE` override;
- forced downgrade still creates a restorable backup;
- successful commit preserves user-created files and removes the backup;
- failed-upgrade restore reinstates the previous payload and consumes the backup.

Response files and modern aliases support:

- `qualifyUpgrade`
- `--qualify-upgrade=...`

## Implementation slice — 2026-09-02 owned-file repair qualification

Extended the same `/QUALIFYUPGRADE` gate to prove repair behavior for installer-owned files without adding a second repair engine. The runner now builds a real component payload, damages the installed `app.exe` in two ways, and executes `InstallWizardGraph.BuildRepair()` so qualification covers the canonical payload-prepare plus typed-resource apply path.

New scenarios:

- `missing-owned-file-repair` restores an absent owned file from the payload.
- `corrupt-owned-file-repair` replaces altered owned-file content with the payload version.

The shared payload and typed-resource setup steps plus `InstallWizardGraph` now live in Core, so the WinForms shell and headless qualification runner use the same repair graph.

## Verification

- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "UpgradeQualificationRunnerTests" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "UpgradeQualificationRunnerTests|EnterpriseCommandLineTests.UpgradeQualificationArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- CLI smoke: `/QUALIFYUPGRADE="Beep.Installer\samples\ServiceApp.bsetup" /OUT="artifacts\f13-upgrade-qualification-dev"` passed all ten scenarios.

## Remaining work

- Attach official clean-VM evidence for locked-file repair and uninstall matrix runs.
