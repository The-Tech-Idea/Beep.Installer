# F12 — Resume, Journal and Recovery

## AppId journal discovery — 2026-09-06

The existing journal store now resolves .beep-installer/{canonical-AppId}.resource-journal.json and its location record. Empty/malformed IDs fail; no display-name lookup or generic filename fallback remains. Custom journal selection validates AppId and owner root, retaining existing link/conflict protections. Install/resume/maintenance, delta images, external transactions and feed cache protection use the same ID-based locator. External-journal resolution failures in atomic application return failure results without file rotation. Name/version/publisher metadata comparisons remain separate constraints; complete renamed-product maintenance is not yet claimed.

### AppId enforcement — 2026-09-06

ResourcePlanExecutor persists compiled AppId in the existing resource journal metadata. Shared metadata validation requires a nonzero matching GUID; maintenance scope resolution applies the same check before using recorded scope. Identity casing is immaterial, while missing/malformed/different identities fail. No empty-ID or name-derived fallback exists. Delta target journal rewriting retains the local identity. Tests that reconstruct an installation must retain its persisted AppId rather than call new-product creation without carrying that identity.

Still open: signed delta/feed AppId binding, registration/discovery identity adoption and full lifecycle qualification after enforcement. Existing product-name/publisher checks remain additional constraints; display rename support is not yet complete.

## Custom journal discovery — 2026-09-06

Silent installation now forwards `/JOURNAL` into the shared context builder. The journal store records a location-only pointer after the first durable checkpoint, before resource mutation. In-root locations are relative; external locations are absolute. There is still one resource journal. The journal metadata records its owning installation root, and custom-path reuse refuses another installation's journal. The normal resource workflows hold the existing operation lease for the selected journal as well as their host installation lease.

Repair, uninstall, scope lookup and `/RECOVERY` share discovery. An explicit path conflicting with the recorded path, a missing recorded journal, conflicting canonical state, unreadable records or filesystem links fail rather than creating another journal. Successful uninstall removes the selected journal and location record, preserving neighboring operator files. A failed first location publication can retain an initial checkpoint; retry with the originally selected path.

Verification: 71 focused runtime/scope/checkpoint and process checks passed. Real CLI cases cover both in-root and external journals, repair without `/JOURNAL`, recovery inspection, missing-journal refusal, automatic uninstall cleanup and neighboring-file preservation. Fixture journals were updated to carry the required owner root; fake provider tests now use temporary installation roots.

Remaining: authenticated location/journal/checkpoint state, cross-session qualification and abrupt interruption. F14 now supports in-folder and external journals through the existing delta transaction, including exact external snapshots and explicit interrupted-commit recovery. This section supersedes older custom-discovery gaps, not the release exit criteria.

**Outcome:** crashes, power loss and reboots do not leave an unknowable state.

**Design:** append-only journal keyed by product, plan hash and attempt; operation states include planned/applied/verified/rolled-back; atomic persistence; restart markers; safe resume rules and explicit abandon/rollback flow.

**Acceptance:** fault injection after every transition converges correctly; corrupted journal is detected; changed plan cannot resume; journal contains no secrets; cleanup retention follows policy. Depends on F02.

## Implementation notes — 2026-08-30

- Added `ResourceExecutionJournalStore` for durable provider execution journal persistence.
- Journal writes are atomic within the destination directory: serialize to a temporary file, then replace or move into the final path.
- Default resource journal path is now part of install/uninstall context setup: `<install-root>/.beep-installer/<product>.resource-journal.json`.
- `ResourceProviderStep` persists the journal after provider execution even when a provider fails, preserving validate/detect/plan/apply/rollback evidence for recovery.
- Added tests for journal save/load, successful resource-step persistence, and failure-path persistence.
- Rewired install/repair graphs so resource execution is no longer duplicated by separate file/shortcut/registry/environment steps; provider journal is now the primary audit trail for common resources.
- `ResourceProviderStep` publishes manifest-compatible outputs for verification while typed-resource lifecycle state is owned by the provider journal.
- Executor skip propagation now records dependent resources as skipped when their component gate is skipped, keeping recovery and uninstall state aligned with actual execution.
- Component gates now include condition-driven skips from deterministic condition facts, so inapplicable resources remain out of applied journals and uninstall manifests.
- Successful provider apply entries now include redacted operation snapshots for later replay.
- Real uninstall and self-test uninstall now use `ResourceProviderUninstallStep` to replay successful provider applications in reverse order from the durable journal.
- Provider replay rollback removes file, registry, environment and shortcut resources from journal snapshots without requiring install-time in-memory rollback state.
- Provider journals now carry product name, product version, plan hash, install scope, attempt id, started-at and updated-at metadata.
- Replay uninstall recompiles the current project and refuses to run when journal product/version/plan hash/scope metadata does not match.
- Journal loading now returns structured statuses for loaded, missing, corrupt, incompatible and unreadable journal files.
- Provider uninstall validation/execution uses structured journal diagnostics, so corrupt or incompatible journals fail with actionable messages before any resource rollback begins.
- Added `ResourceJournalRecoveryService` as the single owner for pending-rollback inspection, metadata validation and active-journal abandonment.
- Added `/RECOVERY=<script.bsetup>` CLI status output with product/path/journal metadata, entry counts, failed counts, pending undo operations and plan-mismatch warnings.
- Added `/RECOVERY=<script.bsetup> /ROLLBACK` to run the same provider journal replay path used by uninstall, including optional `/D=<path>` and `/JOURNAL=<path>` overrides.
- Added `/RECOVERY=<script.bsetup> /ABANDON` to remove the active journal from the runtime path while preserving it under an `.abandoned.<timestamp>` audit filename.
- Resource execution now checkpoints the journal after every provider transition instead of only after the resource step returns.
- Install resource execution can resume from a compatible existing journal: operations with a prior successful apply and verify entry are marked `Resume` and not applied again.
- Resume is plan-bound through the same product/version/plan-hash/scope metadata gate used by rollback, and the original attempt id is retained when a journal is resumed.
- Provider journal entries now include explicit lifecycle execution mode: `install`, `repair` or `update`.
- Runtime install hosts stamp `ResourceExecutionMode=install`; `/REPAIR` stamps `repair`; the shared `UpgradeStep` stamps `update` whenever an existing product install is detected.
- Resume policy is now mode-aware: clean interrupted installs may skip prior verified operations, while repair/update re-detect, re-plan and replay provider operations against the current machine state.
- Added deterministic checkpoint fault-injection coverage for every provider transition in a two-operation dependency plan; each simulated crash persists the journal first, then a retry resumes/converges to verified state.

## Implementation slice — 2026-09-02 recovery qualification gate

Added a first-party recovery qualification gate that packages the existing journal/recovery behavior into release evidence:

```powershell
Beep.Installer.exe /QUALIFYRECOVERY=<script.bsetup> [/OUT=<dir>]
```

The runner writes `recovery-qualification.json` plus per-scenario diagnostics for:

- project load and canonical plan compilation;
- atomic journal save/load and metadata round-trip;
- distinct missing/corrupt/incompatible journal statuses;
- plan-hash mismatch warning for changed-plan recovery attempts;
- pending rollback selection that ignores operations already rolled back;
- abandon flow that archives the active journal instead of deleting evidence;
- secret-free journal snapshots that preserve opaque secret handles without resolved values.

Response files and modern aliases support:

- `qualifyRecovery`
- `--qualify-recovery=...`

## Verification

- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "RecoveryQualificationRunnerTests|EnterpriseCommandLineTests.RecoveryQualificationArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- CLI smoke: `/QUALIFYRECOVERY="Beep.Installer\samples\ServiceApp.bsetup" /OUT="artifacts\f12-recovery-qualification-dev"` passed all eight scenarios.

## Remaining work

- Run the completed fault-injection/lifecycle scenarios on real VM targets and attach captured evidence.
