# Phase 8: Security & Reliability Hardening — Design Document

**Status:** ⬜ not started · **Priority:** P1 (items 8.A are P0-urgent and may be pulled forward)
**Depends on:** P1 (contract for secret ref), coordinates with P3/P4 moves
**Tracker:** [MASTER_TRACKER.md](MASTER_TRACKER.md) · **Evidence:** [R0_REVIEW_FINDINGS.md](R0_REVIEW_FINDINGS.md) §3 (A6, A8, A9)

## 0. Problem Statement

Defensive hardening of the authoring + install paths for software the user builds and
ships with this tool:

1. **Plaintext signing secret**: `CodeSignCertificatePassword`
   (`Models/InstallProject.cs:534`) round-trips verbatim into `.bsetup`
   (`InstallerScriptSerializer.cs:267`) — a cleartext secret in a file users commit to git.
2. **Arbitrary command execution from scripts**: `PrerequisiteDetector.RunDetectionCommand`
   (`PrerequisiteDetector.cs:192-207`) splits and executes any `.bsetup`-supplied
   detection command; a malicious/tampered script executes code at *validate/check* time,
   before the user consented to install.
3. **Shell string-building**: `ClickOnce/Shortcut.Create` builds a PowerShell command via
   concatenation with only quote-doubling (`Shortcut.cs:18-31`).
4. **Silent failure**: pervasive empty `catch {}` (list in R0 A6) — including payload
   files silently missing from a built installer (`BuildPipeline.cs:289`).
5. **Data race**: autosave timer serializes `InstallProject` on a pool thread while the
   UI mutates it (`InstallerController.cs:220-230`).
6. **Sync-over-async**: `.GetAwaiter().GetResult()` in `UpdateChecker.cs:90`,
   `UpdateApplier.cs:50`, `PayloadDownloadStep.cs:59`.
7. **Payload integrity**: downloaded payloads (`PayloadDownloadStep`) are not
   hash-verified even though BeepDM ships `InstallHelpers.ComputeFileHash/VerifyFileHash`
   (`BeepDM/.../Installer/InstallHelpers.cs:17+`).

Items 3/6 are structurally fixed by P3/P4 if those phases land first — this phase owns
verifying them and closing whatever remains.

## 1. Goals

1. No secret is ever written to `.bsetup`. Acceptance: serializer writes
   `CodeSignCertificatePasswordRef` only; loading an old script with a plaintext
   password migrates it to DPAPI storage and blanks the field, with a user-visible notice.
2. Script-supplied commands never auto-execute. Acceptance: detection commands and custom
   actions run only after explicit consent (interactive) or an explicit CLI flag
   (`/ALLOWSCRIPTCMDS`) in headless mode; default silent behavior = skip + warn.
3. Downloaded payloads verified. Acceptance: `PayloadUrl` flows require a `PayloadSha256`
   in the project; mismatch aborts install before file copy.
4. Zero empty catch blocks in shipped code. Acceptance: analyzer/grep gate in CI.
5. Autosave race eliminated. Acceptance: stress test (mutate model while autosave fires
   ×1000) produces valid scripts every time.

## 2. Design

### 2.1 Secrets (goal 1)

- `InstallerSecretStore` (Engine, Windows DPAPI `ProtectedData` — package already in the
  dependency tree): `Store(ref, secret)` / `Retrieve(ref)`; refs look like
  `dpapi:<guid>` or `env:VAR_NAME`.
- Serializer: never emits the obsolete plaintext key; on load of legacy key → store via
  DPAPI, rewrite ref, add load-warning. Build `SignStage` resolves the ref at build time
  only, in memory.
- CI usage documented: `env:` refs for build agents.

### 2.2 Command execution consent (goal 2)

- `ScriptCommandPolicy { Allow, Prompt, Skip }` on `InstallerBuildOptions` / runtime
  context. Interactive default = `Prompt` (one consolidated consent dialog listing the
  commands, shown once per script); silent default = `Skip` unless `/ALLOWSCRIPTCMDS`.
- Applies to: prerequisite detection commands, custom actions with
  `CustomActionTiming.*` (already surfaced through BeepDM `CustomActionStep`), and
  prereq installer launches (`PrerequisitePage.cs:190-240`).
- Detection commands additionally run with `UseShellExecute=false`, explicit
  file+args split (no shell interpretation).

### 2.3 Integrity (goal 3)

- `FileCopyOperation`/component payload gains optional `Sha256` (BeepDM Models —
  additive); `PayloadDownloadStep` verifies via `InstallHelpers.VerifyFileHash` before
  extraction; `BuildPipeline`'s `CompressPayloadStage` records hashes at build time so
  URL payloads produced by the same tool are always verifiable.

### 2.4 Reliability (goals 4–5)

- Empty-catch sweep: each becomes (a) log + result-warning, (b) rethrow, or (c) a
  justified `// intentional: <reason>` suppression — target list from R0 A6.
- Autosave: UI thread produces the serialized string snapshot
  (`InstallerScriptSerializer.Write` on the UI thread is fast — measured; if >50 ms on
  large projects, deep-clone the project instead), pool thread only does file I/O.
- Confirm P3/P4 removed sync-over-async; add analyzer rule
  (`CA2007`-adjacent / custom grep for `GetAwaiter().GetResult()` in non-Main code).

## 3. API surface

Additive Models changes: `Sha256` on file/payload contracts, `ScriptCommandPolicy` enum.

## 4. Files to change

| Action | File | Lines | Risk |
|--------|------|-------|------|
| New | `Beep.Installer/Hosting/InstallerSecretStore.cs` (or Packaging project) | ~90 | medium |
| Modify | serializer (secret ref read/write/migrate) — BeepDM Authoring | ~60 | medium |
| Modify | `BeepDM Models` — `Sha256`, `ScriptCommandPolicy`, `PasswordRef` finalization | ~40 | low |
| Modify | `PrerequisiteDetector`, `PayloadDownloadStep`, `CustomActionStep` call sites (policy + verify) | ~120 | medium |
| Modify | empty-catch sites per R0 A6 list | ~80 | medium |
| Modify | autosave snapshot logic (`InstallerController`/shell controller) | ~30 | low |
| New | CI grep gates (empty catch, GetResult) + stress test | ~60 | low |

## 5. Error handling matrix

| Operation | Before | After |
|-----------|--------|-------|
| Legacy script w/ plaintext password | silently kept forever | migrated to DPAPI + blanked + warning |
| Detection command in silent mode | executed blindly | skipped + warning (exit note), unless `/ALLOWSCRIPTCMDS` |
| Downloaded payload corrupted | extracted anyway | hash mismatch → install aborts pre-copy, rollback clean |
| Autosave during edit | possible corrupt/partial script | snapshot-consistent write |

## 6. Backward compatibility

Old `.bsetup` files load with automatic secret migration. Scripts relying on silent
detection-command execution need `/ALLOWSCRIPTCMDS` — **breaking for silent CI installs
using detection commands**; called out in release notes (this is the point of the change).

## 7. Verification

```
dotnet test --filter "Security|Rollback|EmbeddedPayload|ScriptingAndRollback"
# legacy-secret migration test; hash-mismatch abort test; policy matrix test (3 modes × interactive/silent)
# stress: autosave race harness 1000 iterations
# CI gates: grep 'catch { }' and 'GetAwaiter().GetResult()' → 0 (allowlist file)
```

## 8. Risks

| Risk | Mitigation |
|------|------------|
| DPAPI ties secrets to user/machine — CI agents differ | `env:` ref scheme is first-class; docs |
| Consent prompt annoys legitimate authors | one prompt per script hash, remembered |
| Breaking silent installs (policy default) | major release notes + `/ALLOWSCRIPTCMDS` escape hatch |

## 9. Out of scope

Authenticode timestamp-server fallback logic; malware scanning of payloads; sandboxing
custom actions (OS-level job objects) — backlog.

## 10. Sub-task execution order

1. **8.A.1** Secret store + serializer migration + SignStage resolution. Verify: migration test; no plaintext in any written script.
2. **8.A.2** Command-execution policy (detector, custom actions, prereq installs). Verify: policy matrix test.
3. **8.A.3** Payload hash record + verify. Verify: mismatch-abort test.
4. **8.B.1** Empty-catch sweep + CI gates. Verify: grep gate green, suite green.
5. **8.B.2** Autosave snapshot fix + stress test. Verify: 1000-iteration harness.
6. **8.B.3** Sync-over-async confirmation sweep. Verify: gate green.
