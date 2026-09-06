# F14 — Patch and Delta Updates

## Signed AppId binding — 2026-09-06

DeltaInstalledImage now signs the canonical AppId alongside existing plan/payload metadata. Installed-image authoring requires ExpectedAppId (CLI reads it from the supplied project), rejects mismatched source/target IDs before publication and refuses missing IDs. Apply validates the local journal against the signed ID before staging, validates the staged journal against it and writes that signed identity into target metadata. Existing checkpoint recovery reuses target validation. No alternate identity store or display-name fallback is introduced.

The real CLI lifecycle test now substitutes a different ID while keeping name/publisher unchanged, asserts rejection without modifying the journal or rotating the installation, then restores the fixture and completes normal update/maintenance. Feed-level caller identity pins and registration/discovery adoption remain open.

Verification: 49 delta and installed-image lifecycle checks passed, including all six default/custom/external-journal CLI cases and authoring rejection for wrong, missing and differing target IDs. This is not evidence of authenticated local checkpoint storage or managed-device qualification.

## File ownership changes — 2026-09-06

Project-bound deltas now add/remove owned application files. The signed release contract binds the base file-operation hash and target compiled file operations while requiring non-file resource operations to remain unchanged. Build/apply compare payload additions/removals with source/target ownership; unowned changes, shared-counted files, duplicate destinations, unsafe destinations and cross-resource identity collisions are refused. The maintenance manifest is updated using its existing model and local installation path.

Journal history is retained. Successful `Supersede` entries retire previous file ownership, and target apply/verify entries are materialized only after the signed staged payload hash matches. Shared `ActiveOperations` logic drives replay and delta validation; resume respects the same retirement boundary. Default, in-folder and external journals use the same transaction path. Rollback restores prior file contents and journal history.

Real installer-process scenarios cover adding `new.txt`, removing `old.txt`, rollback, recovery, repairing a deleted new file and uninstall across all three journal locations. The external-journal scenario also chains a subsequent signed release to verify that superseded history does not block another update. Non-file resource changes, shared-resource accounting, authenticated checkpoint state and managed/power-loss qualification remain open. This supersedes older unchanged-file-layout restrictions below.

Verification: all 134 delta/process/provider/recovery checks passed, including the six actual lifecycle variants and chained-release case. This is local execution evidence, not official managed-target or abrupt-power-loss qualification.

## External resource journal transactions — 2026-09-06

External journals now participate in the existing delta transaction. The signed release contract declares external journal ownership without publishing author-machine paths or history. The location pointer is retained locally; the machine-local delta checkpoint contains exact before/after journal snapshots. Stage preparation does not write the external journal. Apply holds its journal lease through promotion/reconciliation, checkpoints before directory rotation, and then atomically replaces the external record through the shared checkpoint writer. Prepared/failed/committed states reuse one checkpoint record rather than duplicate metadata construction.

Rollback/recovery require the current external journal to match a recorded snapshot and bind its path to the installation's verified location/owner. Unknown or missing contents stop without overwriting recovery data. Recovery can finish promoted files with an old journal, or restore a missing installation and its base journal. Preview makes neither moves nor journal writes. The acquisition cache cannot overlap the selected resource journal. This is recoverable multi-location commit, not an atomic transaction across filesystems; keep the delta checkpoint with the installation and external journal.

Actual installer-process coverage now includes default, in-folder and external journals. The external case holds a real Windows file lock across apply, verifies promotion with deferred journal replacement, previews/finishes recovery, refuses altered journal contents before rollback, restores byte-identical base history, and exercises a simulated missing-install recovery before repair/uninstall. Abrupt power loss, cross-session permissions, authenticated journal/checkpoint state and managed-fleet qualification remain open. This supersedes older external-journal implementation gaps below.

Verification: 156 delta/feed/process/checkpoint checks passed; final focused verification passed five real lifecycle/checkpoint cases after the source-snapshot consistency check was added. Snapshot serialization and byte-atomic replacement reuse the journal store and shared checkpoint writer. Recovery snapshots are machine-local checkpoint data, not a second active journal or part of the published package.

## In-folder custom journal transactions — 2026-09-06

The signed installed-image contract now binds the relative resource-journal path. Authoring resolves recorded locations and requires the same in-folder location in both images; external locations are refused until their separate commit transaction exists. Payload hashing excludes the bound journal, not an arbitrary caller exclusion list. The location record remains signed payload and cannot be changed during apply. Stage promotion carries the retained local journal and its location through the existing directory transaction; exact-tree rollback restores both.

Precommit validation checks the staged journal's product/version, target plan, scope, unchanged resource operations and recorded owner root. The real CLI lifecycle scenario now runs with both default and in-folder custom journals, including independent installation, signed package authoring, apply, registration reconciliation, rollback, recovery, repair and uninstall. External-journal commit/rollback and managed-target qualification remain unfinished.

## Installed version registration — 2026-09-06

Project-bound delta apply, rollback and recovery now reconcile `Version` in the existing UpgradeEngine registration and `DisplayVersion` in Apps & Features. A shared registration helper uses the existing discovery key contract; build, log annotation and ClickOnce reuse one uninstall-key path helper. It searches both Windows views in the recorded user/machine scope, updates only registrations whose absolute install location and publisher match, preserves maintenance commands and never creates missing keys. Conflicting publisher identity fails precommit; unreadable/unwritable registration fails explicitly.

The delta journal retains the signed installed-image contract for recovery. Post-promotion registration failure leaves the prepared journal recoverable; recovery chooses the version from verified machine trees and retries reconciliation even for an already-terminal journal. Preview is read-only. Writes across views/keys are not atomic, so interrupted writes require recovery. The real user-scope process lifecycle checks update/rollback versions and repair of a deliberately stale registration. Machine-scope ACL failures and cross-session qualification remain open. This supersedes older registration-gap notes below.

## Reusable installed-image payloads — 2026-09-06

Project-bound deltas now carry a signed `InstalledImage` release contract rather than publishing the author machine's journal. Payload hashes exclude only the canonical product journal and the declared maintenance manifest. Apply validates local identity, plan/scope and resource-operation hash, retains local entries/attempt/history and maintenance paths, and advances target plan/version metadata. Commit revalidates signed payload hashes; rollback and recovery use separate complete machine-tree hashes, including retained records. The existing delta engine, journal store and maintenance model remain authoritative.

A real CLI lifecycle test installs separate author-base, author-target and client directories, publishes a signed delta, updates the independent client, verifies retained local history, rolls back byte-for-byte, reapplies, repairs and uninstalls. Resource-operation or file-layout changes require the full installer; sensitive/redacted snapshots and failed/rolled-back histories are refused. Registration-version synchronization, changed-resource execution, custom journals, journal trust and managed-fleet qualification remain open. This section supersedes the whole-tree limitation in older notes below.

Verification: 148 delta/feed/process checks passed before additional negative cases; the expanded run exposed an uncaught resource-change rejection, which was corrected to return the existing failure result. Final focused verification passed all 41 delta/lifecycle checks. The independent-install case runs actual installer processes, not fabricated journal fixtures; it does not qualify remote delivery, services or managed devices.

## Installed-image authoring — 2026-09-06

The existing delta builder now accepts expected product/publisher pins and validates both source and target canonical installation journals against the requested versions before writing output. It reuses the apply-time identity validator. CLI `/DELTA` accepts `/SCRIPT` to derive product, publisher and the default target version; an unreadable explicit project fails. Without a project, existing `/UPDATEAPPNAME` and `/UPDATEPUBLISHER` switches supply the pins. See the [operator guide](../OPERATOR_GUIDE.md).

This catches invalid images before publication but does not close real installed-image lifecycle qualification: machine-specific journals still participate in exact tree hashes. Reusable fleet image authoring, custom journal integration and journal trust remain open.

Verification: 38 delta service tests passed, including six authoring identity cases that check rejection preserves an existing publication. These fixture-based checks do not represent a real installed-image or fleet deployment run.

## Implementation — 2026-09-06 runtime host coordination

Silent install, repair and uninstall acquire the existing installation-directory lease before context journal reads and step execution, retaining it through commit or failure rollback. Contention uses the existing runtime preflight diagnostics and JSON result envelope (exit 2). The interactive installer acquires and releases the same lease on its synchronous worker thread; its commit and rollback now run inside that worker's lease. No separate locking engine was introduced. SDK hosts can use the public `InstallationOperationLock` for their own orchestration; acquisition and disposal must occur on the same thread.

Verification: 38 focused process, delta and graph tests passed, including actual child-process refusal for install, repair and uninstall while the parent holds the shared lease, preserved installation contents and subsequent lease reacquisition. This is local cross-process evidence, not multi-user/cross-session qualification. Direct graph/SDK consumers must explicitly hold the lease; standalone staging, alternate path aliases, custom-journal discovery and abrupt termination qualification remain open.

## Implementation — 2026-09-06 operation coordination

Atomic delta apply, journal rollback and interrupted-commit recovery share an installation-path-keyed nonblocking operation lock. Windows uses a global named mutex; same-thread reentrant operations are also rejected. Recovery re-reads its journal after acquiring the lock and refuses a changed snapshot. Before staging, apply refuses an unfinished/unsupported selected journal or a terminal journal belonging to another installation. It does not silently overwrite recovery evidence.

Verification: 56 delta/checkpoint/channel tests passed. Concurrent-thread and reentrant apply attempts fail before staging; the owning operation completes and subsequent rollback proves lease release. An unfinished selected journal stays byte-identical. Named-lock behavior across sessions and discovery of unfinished journals stored at other custom paths remain to qualify; standalone staging and non-delta installer operations are not covered by this lock yet.

## Implementation — 2026-09-06 interrupted-commit recovery

`RecoverAtomicApply` and `/RECOVERDELTA=<journal.json>` inspect supported journal states and verified current/backup trees. A promoted target plus matching base backup is finalized as committed; an intact base is recorded as recovered; a missing installation with a verified base backup in a prepared/failed run is restored. Ambiguous or changed trees fail without mutation. Shared path validation is reused from rollback. Journal records use record-copy updates, removing repeated metadata copying.

`/DRYRUN` inspects without moves or journal writes; `/JSON`, `--recover-delta=` and response key `recoverDelta` are supported. Successful recovery can be repeated without changing a verified terminal state. Prepared stage and retired backup remnants are retained, not automatically deleted.

Verification: 28 targeted delta/channel/checkpoint tests passed; the three recovery scenarios subsequently passed using real CLI processes. Simulated states cover completed promotion without journal commit, missing current installation with a valid backup, modified-tree refusal, read-only preview and repeat recovery. Actual abrupt process/power-loss qualification, journal authentication and concurrent-operation coordination remain open.

## Implementation — 2026-09-06 shared checkpoint persistence

Resource execution journals and delta journals now use one `AtomicFileWriter`: serialize before publication, write a uniquely named same-directory temporary file, flush its contents to disk, then replace the destination. Existing target/ancestor links are rejected and failed temporary files are cleaned up. The resource store's separate temporary/backup writer was removed; delta journals no longer overwrite their checkpoints in place.

Verification: 67 checkpoint/resource-runtime/delta/channel-process tests passed. Real Windows locked-file tests preserve the previous checkpoint bytes and prevent delta rotation when the prepared journal cannot be published. This verifies replacement failure behavior, not a complete power-loss recovery protocol across directory moves and journal updates.

## Implementation — 2026-09-06 rollback preflight integrity

Journal-driven rollback now requires the supported schema, committed state, absolute installation/backup paths, canonical `<install>.bak1`, and a journal outside both trees. It reuses the delta directory/link checks and hashes both trees before swapping: the backup must match the recorded base and the current installation must match the target. Changed or user-added files cause a no-move failure, preserving both versions for explicit recovery. Expected journal read/parse failures return the existing failure result.

Verification: 64 delta/feed/channel-process/rollback tests passed. New cases alter backup contents, add user data, redirect the backup path, corrupt JSON and null the journal state; each preserves current files, backups and journal bytes without creating a swap. These are consistency checks, not cryptographic journal authentication or power-loss recovery.

## Implementation — 2026-09-06 reversible rollback swaps

Rollback and rotation now share `ReversibleDirectoryMoves` inside the existing rollback manager. A failed swap reverses completed moves, restoring the current version and backup when recovery moves succeed. A preexisting `.swap` path is rejected and preserved rather than deleted, because it may contain interrupted-run recovery data.

Verification: 34 rollback/delta/channel-process tests passed. New cases inject failure at all three swap moves and verify both versions remain unchanged; a preexisting swap prevents all moves and retains its contents. Durable crash recovery remains separate from this caught-exception recovery.

## Implementation — 2026-09-06 recoverable rotation failures

The shared `RollbackManager.Rotate` now retains the oldest backup in a uniquely named retirement directory until stage promotion succeeds, records completed moves and reverses them after a caught failure. Successful promotion removes the retired backup; cleanup failure leaves it intact with a diagnostic. If reversal also fails, the returned error identifies retained recovery data. The optional move adapter supports deterministic fault injection; normal callers continue to use filesystem moves.

Verification: 30 rollback/delta/channel-process tests passed. Fault injection at each of five moves against real temporary directories proves the current installation, stage and all three existing backups are restored. This covers caught move failures, not abrupt process termination or power loss. Rollback-swap failure recovery remains a separate gap.

## Implementation — 2026-09-06 cooperative cancellation

Delta staging/atomic options accept `CancellationToken` and phase progress. Preparation observes cancellation before work, during file enumeration/copy/resource application, and at `ready-to-commit`. No cancellation checks occur after journal preparation begins: rotation and final journal recording are a critical section. Cancellation raises `OperationCanceledException` and retains any partial/complete stage for inspection rather than deleting it. Per-file synchronous I/O finishes before the next cancellation check.

The trusted channel SDK forwards cancellation/progress. Channel-apply CLI handles Ctrl+C cooperatively, removes its handler afterward, reports `cancelled=true` in JSON and returns 1602 when cancellation is observed. Terminal-signal delivery itself still needs interactive qualification.

Verification: 43 delta/feed/channel-process tests passed. Deterministic cancellation tests at entry, staging and ready-to-commit confirm original files are preserved, no backup/journal is created and only the prepared stage remains at the final precommit boundary. Process termination/power-loss recovery is separate and remains unproven.

## Implementation — 2026-09-06 staging path safety

The canonical delta apply engine rejects equal/nested installation, stage and package paths; filesystem roots; link-bearing inputs/path ancestors; nonempty staging directories; and journal or staging/package collisions with reserved backups. It no longer recursively deletes a preexisting stage directory. An interrupted/failed stage is retained and requires an explicit fresh empty stage or operator cleanup, rather than silently deleting unknown contents.

Verification: 40 delta, channel-feed and channel-apply process tests passed. Negative cases confirm same/parent/child/package/occupied stage selections and journal/backup collisions leave existing application, package and user files unchanged. Cancellation handling remains pending; this safety issue was found while inspecting that boundary.

**Outcome:** deliver smaller signed updates without sacrificing recoverability.

**Design:** publisher compares manifests and emits content-addressed blobs/deltas; client verifies signed manifest and final hashes; side-by-side staging and atomic switch; base-version/tree mismatch refuses the delta. MSI output may produce MSP separately.

**Acceptance:** delta benefit evidence prevents inefficient patches; interrupted/corrupt delta never applies; rollback restores exact prior manifest; base-version mismatch never applies. Depends on F13, F21 and F23.

## Implemented slice — 2026-09-01 strict content-addressed delta package

- Added `DeltaUpdatePackageService` as the canonical Core service for file-tree delta release artifacts.
- Delta builds compare a base directory and updated directory, then write `beep-delta-manifest.json`.
- The manifest records base version, target version, base tree SHA-256, target tree SHA-256, target bytes, delta bytes, delta ratio and every file action.
- Only `add` and `update` actions store payload content under `blobs/sha256/<hash>/<filename>`.
- `remove` and `unchanged` actions are metadata-only, so unchanged content is not duplicated in the delta package.
- Optional detached RSA-SHA256 manifest signatures are emitted as `beep-delta-manifest.json.sig` and verified through the shared detached-signature verifier used by policy, supply-chain waivers and update-channel feeds.
- `ApplyToStage` requires the current install version and full current tree hash to match the declared base before any stage is produced.
- Blob hashes are verified before copy, and the finished stage tree must match the manifest target tree hash.
- Unsafe absolute or parent-traversal paths are rejected before staging.

## Implemented slice — 2026-09-01 delta release CLI contract

- Wired the delta package service into the canonical enterprise command-line front door.
- Added `/DELTA=<outdir>` with required `/DELTABASE=<dir>` and `/DELTATARGET=<dir>` for release delta creation.
- Added `/DELTABASEVERSION=<version>` and `/DELTATARGETVERSION=<version>` for strict base/target version evidence.
- Added `/DELTASIGNKEY=<pem>` for signed delta manifest output.
- Added `/VERIFYDELTA=<dir>` with `/DELTATRUSTKEY=<pem>` and `/REQUIRESIGNED` for fail-closed signature verification.
- Added `/APPLYDELTA=<dir>` with `/DELTACURRENT=<dir>`, `/DELTASTAGE=<dir>`, `/DELTAJOURNAL=<json>` and `/DELTACURRENTVERSION=<version>` for verified atomic install-root promotion.
- Added `/ROLLBACKDELTA=<journal.json>` for exact prior-tree restoration from the delta rollback journal.
- Added modern aliases such as `--delta=`, `--verify-delta=`, `--apply-delta=`, `--rollback-delta=`, `--delta-base=`, `--delta-target=`, `--delta-trust-key=`, `--delta-journal=` and `--delta-current-version=`.
- Added response-file fields for the same canonical arguments, keeping F14 automation inside the F10 parser instead of a separate command model.
- Added process-level coverage that builds, verifies, atomically applies and rolls back a signed delta through the actual installer executable.

## Implemented slice — 2026-09-01 atomic delta apply and rollback journal

- `/APPLYDELTA` now performs the professional update path directly: verify the signed delta, materialize a verified stage, atomically promote it into the install root and write a rollback journal.
- Atomic promotion reuses the existing rollback rotation manager instead of creating a second backup system.
- The rollback journal records delta directory, install directory, stage directory, backup directory, manifest path, base/target versions and base/target tree hashes.
- The committed install root is hashed after promotion and must match the delta target tree hash.
- `/ROLLBACKDELTA=<journal.json>` restores the prior install tree from the journal-backed backup and verifies that the restored tree matches the delta base tree hash.
- `/APPLYDELTA` is intentionally no longer a stage-only command in dev mode; the canonical command now means verified commit.

## Implemented slice — 2026-09-01 update-channel delta attachment

- Update-channel feed export now accepts `/DELTA=<dir>` as publish metadata when used with `/UPDATECHANNELFEED=<script.bsetup>`.
- Delta metadata is attached to the selected or explicitly targeted channel entry, using `/UPDATECHANNELTARGET=<channelId>` when supplied.
- Feed entries now include `DeltaPackages[]` with base version, target version, manifest file, signature file, base tree hash, target tree hash, target bytes, delta bytes and delta ratio.
- The feed continues to use the existing RSA-SHA256 detached signature path; delta metadata is covered by the signed feed payload.
- Delta feed attachment stores portable metadata and file names, not local absolute paths.
- Process-level coverage now builds a delta, exports a signed feed with attached delta metadata, and verifies the emitted feed JSON through the installer executable.

## Implemented slice — 2026-09-01 delta qualification evidence runner

- Added `DeltaUpdateQualificationRunner` for deterministic F14 lifecycle evidence without a second delta apply engine.
- Added `/QUALIFYDELTA=<dir>` with `/DELTACURRENT=<dir>`, `/OUT=<dir>`, `/DELTATRUSTKEY=<pem>`, `/REQUIRESIGNED` and `/DELTACURRENTVERSION=<version>`.
- The qualification runner writes `delta-update-qualification.json` plus per-scenario evidence files.
- Positive evidence covers signed manifest verification and verified atomic apply followed by journal rollback.
- Negative evidence covers tampered manifest rejection, missing/interrupted blob rejection and wrong-base-tree rejection.
- The runner clones the install tree into qualification work directories, so it can be run on clean VM release targets without mutating the original baseline install.
- Response-file and modern alias support includes `qualifyDelta` and `--qualify-delta=`.

## Verification

- `dotnet test Beep.Installer.Tests/Beep.Installer.Tests.csproj --filter "DeltaUpdatePackageServiceTests" --no-restore --nologo --verbosity quiet`
- `dotnet test Beep.Installer.Tests/Beep.Installer.Tests.csproj --filter "DeltaUpdatePackageServiceTests|EnterpriseProcessContractTests.DeltaCli_BuildsVerifiesAppliesAndRollsBackSignedDelta|EnterpriseCommandLineTests.UpdateChannelFeedAliasesAndJsonResponse" --no-restore --nologo --verbosity quiet`
- `dotnet test Beep.Installer.Tests/Beep.Installer.Tests.csproj --filter "UpdateChannelFeedPackageServiceTests|EnterpriseProcessContractTests.DeltaCli_BuildsVerifiesAppliesAndRollsBackSignedDelta|EnterpriseCommandLineTests.UpdateChannelFeedAliasesAndJsonResponse" --no-restore --nologo --verbosity quiet`
- `dotnet test Beep.Installer.Tests/Beep.Installer.Tests.csproj --filter "DeltaUpdatePackageServiceTests|EnterpriseProcessContractTests.DeltaCli_BuildsVerifiesAppliesAndRollsBackSignedDelta|EnterpriseCommandLineTests.UpdateChannelFeedAliasesAndJsonResponse" --no-restore --nologo --verbosity quiet`

## Remaining hardening

- Execute the completed `/QUALIFYDELTA` evidence contract on official clean VM release targets and archive the produced `delta-update-qualification.json` reports.
