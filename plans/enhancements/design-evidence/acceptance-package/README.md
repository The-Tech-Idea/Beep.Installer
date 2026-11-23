# P13 controlled acceptance package — 2026-11-23

Status: bounded Sandbox runtime smoke verified; the complete P13 plan remains in progress.

Repository packaging: source/runtime digest summaries and operator notes are checked in. Raw logs,
local executable builds, temporary probes, Sandbox exports and machine-specific launch configurations
are retained locally and ignored by Git. References below identify that local evidence; a fresh clone
does not contain it. Publishing or release acceptance still requires the applicable archived evidence.

## Prepared file mutation correction — 2026-11-23, latest evidence

**The ordinary file-copy applied-before-checkpoint gap is corrected in its existing owners.**
P13 remains in progress; this closes the specific fresh/replacement file-copy defect, not generic
provider, reboot, upgrade, human accessibility or release acceptance.

### Source and contract

FileCopyResourceProvider prepares its durable before-state before changing destination bytes.
ResourcePlanExecutor persists a `PrepareApply` snapshot through the existing checkpoint owner;
failed checkpoint publication escapes without entering the copy phase. Pending replay includes
prepared operations even when the successful Apply record is absent. The recovery service owns
completion selection, including invalidation of an older Verify by a newer prepared repair.
Resume reuses the original before-state and installed attempt identity; caller context is restored.

The existing AtomicFileWriter streams backups, flushes data, preserves modification time and uses
private Windows temporary files before publication. Backup access cannot broaden through its
storage directory's inheritance. Replay validates its owned backup path and content hash, restores
original bytes/modification time/access DACL, and reconciles an already restored file. Ordinary
committed uninstall deletes owned payload; recovery rollback restores before-state. Missing or
mismatched recovery state refuses mutation. CLI JSON now correctly reports `action: rollback`.
No parallel store, directory scan for unrecorded payload or legacy journal fallback was introduced.

Resource journal format is **2.0**. Version 1 journals are rejected as incompatible and are not
rewritten. Source project formats are unchanged. This local unreleased contract change is documented
in `Beep.Installer.Core/Packaging/Sdk/README.md`; no public SDK/release compatibility is certified.
The repository releases endpoint returned an empty collection; this does not prove absence of
NuGet/manual installations. Operator/release review of any existing installations remains required.
No real host installation journal was operated on during these Sandbox checks.

### Verification of the final source

- **126 passed, 0 failed/skipped, 3 seconds**:
  `prepared-recovery-final-source-tests.log`. Focused selection includes PreparedFileRecoveryTests,
  ResourceProviderRuntimeTests, JournalPersistenceTests, RecoveryQualificationRunnerTests,
  UpgradeFlowTests, ResourceProviderSdkTests and DeltaUpdatePackageServiceTests.ApplyAtomically.
- **Fresh Sandbox profile: 5 passed, 0 failed/skipped, exit 0**:
  `guest-output/prepared-fresh/gate-matrix.json`.
- **Replacement Sandbox profile: 5 passed, 0 failed/skipped, exit 0**:
  `guest-output/prepared-replacement/gate-matrix.json`. All 300 original file hashes and original
  modification times are verified after rollback. Both profiles preserve an unrelated note;
  resume verifies all 300 new payload hashes and committed uninstall removes them.
- Each profile terminates two actual installer processes, then uses new processes for inspection,
  rollback and resume. Both rollback JSON responses parsed as action rollback/success true/exit 0.
  Actual child arguments, logs, prepared journals, completed resume journals, source/runtime hashes
  and environment are retained in the two profile directories and `prepared-native-profiles.json`.
- The guest reports Windows NT 10.0.26100.0, 64-bit, host A5EC862C-1660-4. It is an elevated
  WDAGUtilityAccount per-user fixture, not a standard-user or per-machine qualification.
- Final published host: `authoring-host-prepared-final/Beep.Installer.exe`, self-contained Release
  win-x64, single-file, ReadyToRun disabled; `authoring-prepared-final-publish.log` succeeded.
  `prepared-native-verification.json` ties both reports to its exact SHA256 and verifies logs.
  `prepared-tested-source.json` confirms tested source hashes still match after both runs.

The random native kills observed ordinary partial copies with prepared records. They do not by
themselves force every post-copy/pre-completion instant. Deterministic persisted-journal fault
checks cover that precise boundary: after actual file copy, completion saving fails; a fresh
provider recovers the earlier state. They also cover failed preparation saving, a second interrupted
resume, earlier-verification invalidation, restricted/inherited DACLs, modification times,
opaque attempt-path confinement and refusing unprepared recovery. This is stronger and different
evidence than the earlier unchanged-source lucky green sample.

### Negative controls retained

| Removed/broken behavior | Actual selected result | Log |
|---|---|---|
| Before-mutation checkpoint | 7 failed / 3 passed | `prepared-recovery-no-writeahead-negative.log` |
| Backup source access rules | 1 failed | `prepared-recovery-backup-acl-negative.log` |
| New preparation invalidates older verification | 1 failed | `prepared-recovery-stale-verification-negative.log` |
| Protect backup from broader storage inheritance | 1 failed | `prepared-recovery-inheritance-negative.log` |
| Encode opaque attempt path / refuse missing before-state | 2 failed | `prepared-recovery-identity-refusal-negative.log` |
| Preserve original modification time | 1 failed | `prepared-recovery-write-time-negative.log` |

Sources were restored with fresh timestamps before the final normal-source run. Initial focused
execution had one old schema-version assertion (55 passed/1 failed); it now asserts format 2.0.
The first stale-verification negative-control launcher did not find its CRLF target and ran no test;
the normalized retry failed the intended guard. These development outcomes remain distinct from
final qualification. No full suite or cloud build ran.

### Data and remaining acceptance

Before-state backup content stays under the owned installation's `.beep-installer/resource-backups`
path. Journals contain its path/hash and original Windows access-rule metadata. Backups are temporary
local recovery material; no external transmission or new analytics/service was added. Successful
replay/committed uninstall removes the replayed backup files. Whole-product retention/failed-run
cleanup remains a separate acceptance gate; do not infer it from these selected outcomes.

Other built-in/third-party resource providers must implement and qualify their own preparation
and replay. Deferred reboot paths, complete version-pair upgrade, generic resource rollback,
non-admin/per-machine cases, power loss, other filesystems, physical input, actual OS DPI,
Narrator/Accessibility Insights, five first-time authors and signed/final formats remain open.
`manual-evidence` is still empty. No publication, push, commit or host setup/runtime CLI install.

For a deliberate disposable repeat, `P13-Prepared-Recovery.wsb` launches the existing matrix owner
against the current runtime and replacement profile using read-only inputs plus one results mapping.
It has not been separately launched; the equivalent mode was actually executed through WSB CLI.
Use a new output directory when repeating. The original WSB file remains the historical smoke setup.

Official API basis: [ACL-aware file creation](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemaclextensions.create?view=net-10.0)
and [Windows file security](https://learn.microsoft.com/en-us/dotnet/api/system.security.accesscontrol.filesecurity?view=net-10.0),
read 2026-11-23. These APIs plus scoped evidence do not establish whole-product compliance.

## Historical checkpoint continuation — before prepared mutation correction

**Historical finding: the applied-before-checkpoint window was confirmed here; the latest section records its subsequent correction and bounded evidence.**
The current checkpoint file-publication fix is implemented and bounded verification passed.
Do not treat the later passing interruption run as resolving the earlier unjournaled-file failure.

- Existing upgrade qualifier executed in the requested Sandbox: **10 scenarios passed**, CLI exit 0.
  `guest-output/upgrade-qualification/upgrade-qualification.json` and its `execution.json` record
  actual isolated HKCU/upgrade-engine/repair-graph checks. This is not a packaged version-pair test.
- Final normal-source selection: **56 passed, zero failed/skipped, 5 seconds**;
  `atomic-checkpoint-final-runtime-tests.log`. Selection: ResourceProviderRuntimeTests,
  JournalPersistenceTests and RecoveryQualificationRunnerTests. New restricted-DACL guard failed
  during development and now passes. Locked checkpoint tests still verify failure, unchanged old
  bytes, cleanup and subsequent successful replacement; they accept the native rename API's
  IOException or UnauthorizedAccessException failure categories.
- The old publisher used File.Replace. The shared-read interruption left its canonical journal
  missing, with Windows replacement temporary files in the guest. The saved failure is
  `guest-output/interrupted-recovery-shared-read/`; its wrapper returned exit 1.
  AtomicFileWriter now flushes the same-directory temporary file, preserves an existing Windows
  access DACL, and publishes with overwrite rename. Link traversal guards remain in place;
  temporary cleanup errors report through Diag. No alternate journal store or fallback reader was added.
- Final controlled **independent interruption run: 5 passed, zero failed/skipped, wrapper exit 0**:
  `guest-output/interrupted-recovery-independent/gate-matrix.json`. A new process inspected each
  partial journal. Rollback of the first sample removed all owned payload. The second sample
  resumed in a new CLI process, recorded four Resume entries, verified all **300 expected file
  hashes**, and uninstalled successfully. The full completed resume journal is retained under
  `checkpoint-evidence/resume-completed.resource-journal.json` before uninstall cleanup.
- This final run uses `authoring-host-recovery/Beep.Installer.exe`, self-contained Release win-x64,
  single file with ReadyToRun **disabled**. Publish succeeded (`authoring-recovery-publish.log`).
  It exercises the existing `/S /SCRIPT=`, `/RECOVERY= /ROLLBACK` and `/UNINSTALL /SCRIPT=` paths,
  not an embedded-payload package or a power-loss test. The original and intermediate published
  hosts remain separate; their earlier reports refer to those hosts' own hashes.

### Historical confirmed recovery defect

`guest-output/interrupted-recovery-complete/checkpoint-evidence/rollback-interruption.json`
records **5 files present versus 4 durable successful file Apply entries**. The saved before-journal
contains those four file copies. `/RECOVERY=... /ROLLBACK` returned **exit 0 but left one payload
file**. Its report is **1 passed / 1 failed** with wrapper exit 1. The later clean sample is timing-
dependent; the later prepared-mutation correction above is required to address this failure.

Source inspection locates the gap: ResourcePlanExecutor invokes `provider.Apply` before persisting
its successful Apply snapshot; ResourceJournalRecoveryService replays only recorded successful
Apply snapshots. FileCopyResourceProvider holds replacement backup state in memory, so robust
interrupted mutation also needs durable provider-owned before-state. Existing checkpoint-fault
contract tests and a successful later sample cannot establish crash safety across that gap.
Next implementation: prepare and durably record recoverable mutation state through the existing
provider/executor/journal contracts **before changing the resource**, then reconcile incomplete
operations and verify fresh-create and replacement rollback/resume with deterministic guards and
real process interruption. Do not scan/delete arbitrary unjournaled files or claim generic provider
recovery from this text-file fixture. The full requirement remains open.

### Investigation failures retained

The first observer used Get-Content and interfered with replacement. A shared-delete reader then
exposed the missing-journal kill case. An intermediate corrected writer was also blocked by the
active observer; the final test observes directory entries only while installation runs and opens
the journal after termination. `interrupted-recovery-final/` passed inspection/rollback but the
harness incorrectly required the removed journal after successful uninstall. The corrected harness
records cleanup absence normally and runs rollback/resume independently without hiding failures.
All attempts have distinct output directories; none are relabelled as passing.

The earliest failure also exposed PowerShell singleton counting: a lone FAIL produced a null
count and wrapper exit 0. Write-Evidence now uses array counts. The actual later failing guest
reports return exit 1, and `count-probe/execution.json` confirms 0 passed / 1 failed / exit 1.
The probe's first relative path and Windows PowerShell module-loading attempts failed before
producing a valid result; only the later pwsh result is claimed as a passing negative-control check.
During writer development, lock exception-category differences and DACL-copy errors produced
failed selections; logs remain. A temporary write-access probe was removed from the final owner.

API basis: [.NET File.Move overwrite](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move)
and the [.NET 10 Windows file implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Windows.cs),
read 2026-11-23. The same-directory flush/rename choice and this process-kill evidence do not
establish power-loss durability or behavior on every supported filesystem.

No full suite, cloud build, commit, push, release or host setup/runtime CLI install was run.
Focused tests did exercise their owned temporary resource cases on the host. Sandbox remains open;
no unrelated user window/VM was operated. Human/DPI/Narrator/signing and wider release gates stay open.
`interruption-source-inventory.json` identifies current source and the actually exercised runtime.
The previous artifact inventory is retained as historical; current files are in `artifact-inventory.json`.

## Observed results

| Check | Result | Evidence |
|---|---|---|
| Inert files-only project validation | Exit 0 | `validation-execution.json`, `fixture-validation.log` |
| Local EXE build with SBOM/provenance | Exit 0 | `local-build-execution.json`, `local-build.stdout.log`, `local-build/evidence/` |
| Standalone authoring publish | Succeeded after required R2R restore | `authoring-runtime-restore.log`, `authoring-publish-restored.log` |
| Accessibility readiness focused selection | 8 passed, 0 failed/skipped, 794 ms | `a11y-final-focused.log` |
| Old accessibility matcher negative control | 1 failed / 1 passed as expected | `a11y-old-matcher-negative.log` |
| Actual required-manual accessibility CLI gate | Exit 1; six source readiness scenarios pass; six manual artifacts absent | `a11y-final-execution.json`, `a11y/a11y-localization-qualification.json` |
| Recovery metadata/journal contract | Exit 0 at physical output path | `recovery-physical-execution.json`, `recovery-contract-physical/` |
| Actual guest lifecycle fixture | 5 passed, 0 failed, 4 skipped; CLI wrapper exit 0 | `guest-output/final/gate-matrix.json`, `wsb-final-run-result.json` |
| Standalone authoring guest startup | Guided Project screen observed, process alive with title Beep Installer | `guest-output/authoring-startup.json`, `guest-output/authoring-observed.json`, native Computer Use observation in chat |

No full test suite, remote build, commit, push, publication or host installer execution was performed.
The test installer is unsigned, contains one inert 69-byte text file and has no executable payload,
custom actions or uninstall entry. The exact fixture AppId and defaults are in `input/acceptance.bsetup`.

## Actual Sandbox runtime coverage

Windows Sandbox guest `A5EC862C-1660-4`, WDAGUtilityAccount, elevated session; this remains a
**per-user fixture**, not a standard-user or per-machine qualification. Sandbox ID:
`496d633d-1077-4ead-a034-b79e4b85c06b`. Final run completed 2026-11-23T14:31:30Z.

1. `/S /D=<owned guest temp>/install-user /NORESTART`: exit 0; expected payload SHA256 verified.
2. Deliberately change that owned payload, then `/REPAIR /D=... /NORESTART`: exit 0; original hash restored.
3. `/UNINSTALL /D=...`: exit 0; all expected payload files absent.
4. Corrupt the appended payload of an owned installer copy: exit 2; no payload left installed.
5. Put a directory at the first payload destination: exit 1; no expected payload files remain.
   This is blocked-destination refusal. It does **not** establish rollback after partial application,
   persisted recovery, interrupted process recovery or upgrade restoration.

The four skips are authoring build/canonicalization in the guest, per-machine install and uninstall,
and authoring CLI checks. SDK/source authoring build occurred separately on the host; this packet
alone does not supply the SDK/source checkout needed to build a new EXE in a clean guest.
All five final child process arguments, exit codes, timestamps and stdout/stderr filenames are recorded
under `operations` in the final report; the corresponding ten logs are in `guest-output/final/runtime-logs/`.

The first logon run completed while startup investigation was underway. An unnecessary diagnostic
repeat was started before the first report was noticed; it finished separately with the same 5/0/4
outcome. Original report: `sandbox-first-report.json`; diagnostic repeat: `guest-output/gate-matrix.json`.
They are repeats of the same fixture, not additional coverage. A final run was justified by changes
that record per-process evidence, mark machine-scope cases honestly skipped and narrow the blocked-
destination case label. Its report is separate under `guest-output/final/`.

## Existing owners and changes

`AccessibilityLocalizationQualificationRunner` falsely required an obsolete literal RTL assignment.
Its bounded source wiring check now recognizes the existing RtlHelper bidirectional owner and still
rejects missing form chrome wiring. The focused native case checks both direction changes. These
checks do not replace Narrator, physical input or visual inspection; required manual evidence still fails.
Restored source SHA256 matches `A11yRunner-fixed.cs.txt`; see `source-inventory.json`.

`scripts/run-gate-matrix.ps1` remains the runtime qualification owner. It now supports a prebuilt
fixture with expected payload hashes, refuses prebuilt mode outside the requested Sandbox, awaits
actual WinExe exits, verifies repair, preserves runtime logs and avoids recursive cleanup of shared
Program Files locations. Machine-scope verification needs its own correctly scoped fixture.
The host SandboxOnly guard was exercised and refused before resolving nonexistent input files.

## Sandbox configuration and execution

`P13-Acceptance.wsb` maps setup, input, runner and standalone authoring directories read-only;
only `guest-output` is writable. Networking, clipboard, microphone, camera, printer and vGPU are
configured disabled; ProtectedClient is enabled. The observed guest diagnostic confirms expected
inputs exist; no write-probe of read-only mappings was performed. Do not claim a full isolation audit.
The startup runner is the evidence copy of the existing script, not a second application owner.
These settings use Microsoft's [WSB configuration](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file).
The documented [WSB CLI](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-cli)
was used to inspect the guest and execute the final runner as ExistingLogin; no System escalation.
Only the Sandbox window was read using Computer Use; no unrelated user window was operated.
The screenshot capture includes part of the host adjacent to the narrow remote window and is not a
clean full-screen visual acceptance artifact. The untouched authoring process was terminated by
its verified guest PID for cleanup; this is not ordinary close/save acceptance. Sandbox remains open.

To repeat deliberately, the final existing owner invocation inside the guest is:

```powershell
C:\P13Runner\run-gate-matrix.ps1 -SandboxOnly -PrebuiltSetup C:\P13Setup\Setup-P13-Acceptance.exe -PayloadDirectory C:\P13Input\payload -EvidencePath C:\P13Results\<new-run>\gate-matrix.json
```

Use a new output directory; do not rerun merely because output has not appeared yet.
The original WSB logon command writes the earlier root report; do not confuse it with the final report.

## Prior failures and limitations retained

- Connected build tool could not find Beep.Installer.csproj; `connected-build-result.json`. Local CLI build succeeded.
- Initial standalone publish with --no-restore lacked R2R runtime pack; `authoring-publish.log`. Explicit matching restore then publish succeeded.
- Checkpoint output through C:/Users/f_ald/source/repos was rejected because that ancestor is a junction. Physical H:/Source/Repos output succeeded; the guard was retained.
- Initial `a11y-cli.log` WinExe launcher value was not awaited and is not a valid exit record. Use `a11y-final-execution.json`.
- The recovery qualifier covers metadata/journal contracts only. It did not interrupt, resume or roll back an installed operation.
- Actual 100/150/200% display scaling, Arabic screenshots, Narrator, Accessibility Insights,
  keyboard-only paths, five first-time authors (at least four unaided successes), upgrade/prerequisites/
  restart/persisted recovery, signed packages and required final formats remain open.
  `manual-evidence` is deliberately empty; no fabricated passing artifact was supplied.

See `OPERATOR-CHECKS.md` for the remaining participant and assistive-technology procedure.
