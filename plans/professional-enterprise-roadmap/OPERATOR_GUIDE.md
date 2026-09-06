# Installer operations and recovery

Journal discovery uses the persisted AppId: `.beep-installer/<canonical-guid>.resource-journal.json`. A custom journal is rediscovered through the adjacent `.location.json` record. Preserve the project's AppId for all maintenance operations; display names do not select journal files. SDK journal locator methods now take AppId, not product names.

Operational update check/apply requires `/UPDATEAPPID=<persisted-project-guid>`, `/UPDATEAPPNAME=<name>` and `/UPDATEPUBLISHER=<publisher>` from trusted configuration. Response files use `updateAppId`; SDK callers set `ExpectedAppId`. Do not obtain the expected ID from the incoming feed. The update center uses its loaded project's AppId. Missing or malformed pins fail before feed access.

Status: development workflow guide, 2026-09-06. This is not approval to deploy a release candidate. The [master tracker](MASTER_TRACKER.md) lists unproven lifecycle, accessibility and target-environment gates.

## Before deployment

1. Identify the exact product, publisher, installed version, installation scope and target directory. Do not infer the installed version from the project being authored.
2. Use the approved project, policy and public keys obtained through a trusted deployment channel. A key downloaded alongside an untrusted feed does not establish trust.
3. Retain the installer/project for the installed version, recovery journals and backups. Keep user data backed up independently.
4. Close the application being maintained. Service/locked-file scenarios need their qualified shutdown/restart procedure.
5. Use the privileges required by the authored scope. Elevation does not change a user installation into a machine installation.
6. Choose separate installation, staging, download-cache, publication-state and recovery-journal locations. Do not reuse a populated staging folder. Never place a cache above an installation or recovery directory.

Examples below use fictional paths and versions. Replace them before execution. Run from PowerShell with the correct installer executable in the current directory.

## Installation and maintenance

### Author a delta from installation images

Use complete base and target installation images, not just application payload folders. Bind authoring to the target project so the builder checks both canonical installation journals before publishing:

```powershell
.\Beep.Installer.exe /DELTA=C:\Publish\ServiceApp-2 /DELTABASE=C:\Images\ServiceApp-1 /DELTATARGET=C:\Images\ServiceApp-2 /SCRIPT=C:\Projects\ServiceApp.bsetup /DELTABASEVERSION=1.0.0 /DELTASIGNKEY=C:\Deploy\Keys\delta-private.pem
```

The target version defaults to the project version; `/DELTATARGETVERSION` can set it explicitly. SDK callers supply `DeltaUpdateBuildOptions.ExpectedAppId`, `ExpectedProductName` and `ExpectedPublisher`. Project-bound CLI authoring requires `/SCRIPT` containing the persisted AppId; display-name/publisher switches alone cannot establish product identity. An unreadable explicitly supplied project is an error. Both image journals must match that AppId before publication. The signed installed-image contract carries the ID and apply validates both local and staged journals against it. Generic directory deltas without identity pins are not evidence of channel-ready installation images.

Project-bound authoring separates the reusable payload from machine-owned journals and, when present, `install-manifest.json`. The signed manifest carries product/scope, source/target plan hashes, base file ownership and target file operations. It can add/remove owned application files while preserving client history and local paths. Old file ownership is recorded as superseded; repair/uninstall use the active target entries. Non-file operations must remain unchanged. Shared-counted files, redacted operation inputs, unsupported destinations and failed/rolled-back histories require the full installer workflow. Default, in-folder and external journals are supported; authenticated recovery state and managed-fleet qualification remain open.

Apply, rollback and recovery also reconcile existing Windows discovery and Apps & Features versions when scope, installation location and publisher match. Unrelated or missing registrations are not overwritten or created. If registration fails after file promotion/restoration, retain the journal and run explicit recovery; do not assume the operation was wholly undone. Recovery preview performs no registry writes, while actual recovery can repair stale versions even when the file state is already committed.

```powershell
.\Beep.Installer.exe '/SCRIPT=C:\Deploy\App.bsetup' /S '/D=C:\Apps\App' /JSON
.\Beep.Installer.exe '/SCRIPT=C:\Deploy\App.bsetup' /REPAIR '/D=C:\Apps\App' /JSON
.\Beep.Installer.exe '/SCRIPT=C:\Deploy\App.bsetup' /UNINSTALL '/D=C:\Apps\App' /JSON
```

Use the matching project for maintenance. An installation journal belonging to another product/publisher, plan or scope must not be edited to force acceptance. When multiple registered installations exist, explicitly select the target with `/D`.

The CLI/UI retain the operation lease through their transaction. SDK callers using the canonical graph factories receive coordinated Run/Resume/RunAsync entry points. Callers doing additional commit/rollback work outside those calls must hold `InstallationOperationLock` across the whole transaction and use synchronous execution on the lease-owning thread. A busy lease is a competing operation, not permission to remove state or start a second maintenance process.

## Update center

```powershell
.\Beep.Installer.exe /UPDATEUI '/SCRIPT=C:\Deploy\App.bsetup' '/UPDATESTATE=C:\DeployState\App' '/UPDATECACHE=C:\DeployCache\App'
```

The builder also exposes **Update center**. Select the signed feed, trusted feed and delta public keys, installation directory, installed version and channel. Check displays signature, policy and eligibility results. Editing inputs invalidates the previous check; Apply checks again. Recover presents a read-only preview before confirmation. Closing during work requests cancellation and waits for a safe stopping point; commit is not forcibly interrupted.

## Signed update check and apply

Start with the [response-file example](../../Beep.Installer/samples/ServiceApp.update-check.response.json). It checks eligibility only and uses fictional release URLs and key paths. Customize identity, version, paths, channels and policy, then run:

```powershell
.\Beep.Installer.exe '/RESPONSE=C:\Deploy\App.update-check.response.json'
```

Operational checks require expected product name and publisher, a trusted feed public key and an installed version. Successful signature verification alone does not mean an update is eligible. Inspect decision and policy diagnostics, including hold/revocation reasons.

For application, replace `checkUpdateChannel` with `applyUpdateChannel` in a copy of the response file and add `deltaCurrent`, `deltaStage`, `deltaJournal` and `deltaTrustKey`. Add `delta` for an approved local delta directory; omit it to acquire the package declared by the signed feed. First use `/DRYRUN` to preview eligibility; then omit it only for the authorized change. Preview may download metadata and persist accepted publication state, but it does not download/apply delta payloads or validate the complete installation image.

Channel deltas require complete matching base/target installation images. Author them using the project-bound command above to retain client-owned maintenance records. The installed record must match product, publisher and base version; the staged record must match target version. Generic directory deltas still hash every file. Raw payload-only images and custom out-of-tree installed journals are not supported by the channel workflow. Managed-fleet qualification remains open; do not manufacture fixture-like journals or relax integrity checks to make a package apply.

## Freshness, credentials and storage

- Feeds at least seven days old or over five minutes ahead of the device clock are rejected. Publishers must refresh and sign metadata; operators must maintain correct clocks.
- Accepted publication timestamps and hashes persist by product/publisher. Older feeds and changed content at the same timestamp are rejected. Do not delete publication state to bypass a rejection. Preserve corrupt state and investigate the publisher/device history.
- `/UPDATESTATE` is persistent trust history, not a payload cache. The default is the current user's LocalApplicationData/BeepInstaller/UpdateState. Protected shared/multi-user deployment remains to be qualified.
- `/UPDATECACHEMAXBYTES` and `/UPDATECACHERETENTIONDAYS` default to 4 GiB and 30 days. Cleanup only evicts inactive recognized installer cache entries; unknown or active files can prevent a reservation. Evicted downloads can be reacquired, but deletion itself is permanent.
- `/UPDATEAUTHORIGIN` plus `/UPDATEBEARERREF` uses an existing secret reference, scoped to an explicit HTTPS origin. Do not put bearer values in scripts, URLs or response files.
- Explicit proxy settings use `/UPDATEPROXY`, `/UPDATEPROXYUSER` and `/UPDATEPROXYPASSWORDREF`. Authenticated proxy configuration requires HTTPS and a secret reference. Production authenticated TLS/proxy evidence is still required.

## Choose the correct recovery path

| Situation | Action | Preserved evidence |
|---|---|---|
| Interrupted typed-resource installation | Inspect `/RECOVERY=<matching-script>` | Resource journal, project and installed files |
| Interrupted delta promotion | Preview `/RECOVERDELTA=<delta-journal> /DRYRUN /JSON` | Current/stage/backup trees and delta journal |
| Committed delta must be undone | Explicit `/ROLLBACKDELTA=<delta-journal>` | Verified current and backup trees |
| Integrity mismatch or ambiguous state | Stop; investigate retained artifacts | Do not rewrite journals or manually swap trees |

Typed-resource inspection and rollback:

```powershell
.\Beep.Installer.exe '/RECOVERY=C:\Deploy\App.bsetup' '/D=C:\Apps\App'
.\Beep.Installer.exe '/RECOVERY=C:\Deploy\App.bsetup' '/D=C:\Apps\App' /ROLLBACK
```

Select a custom resource journal at installation with `/S /JOURNAL=<path>`. The first durable checkpoint records its location, so repair, uninstall and recovery inspection subsequently need only the matching project and `/D`. The journal can be inside or outside the installation; neighboring files remain untouched. Do not move it or edit its location record casually: missing, conflicting or foreign-owned records stop maintenance, and a conflicting `/JOURNAL` does not override the recorded selection. Project-bound deltas support in-folder journals at the same relative location, or external journals using each installation's recorded local path. The package never distributes the author machine's external journal path/history.

For external journals, retain the delta checkpoint: it contains the exact before/after journal snapshots needed to complete or reverse a partially committed update. Staging does not change the live external journal. A failure after file promotion can leave the application at the target version while the external journal is still at the base version. Use `/RECOVERDELTA` preview and explicit recovery before ordinary maintenance; do not replace or edit the journal to force acceptance. Unknown/missing external contents are preserved rather than recreated automatically. Recovery does not promise atomic writes across two filesystems.

`/ABANDON` archives a resource journal; it is not rollback and does not prove resources were removed. Use it only after an operator has decided how remaining resources will be handled. A recorded custom location will then refer to a missing active journal, so ordinary maintenance remains blocked until the recovery state is explicitly resolved.

Delta recovery and explicit rollback:

```powershell
.\Beep.Installer.exe '/RECOVERDELTA=C:\Recovery\App.delta-journal.json' /DRYRUN /JSON
.\Beep.Installer.exe '/RECOVERDELTA=C:\Recovery\App.delta-journal.json' /JSON
.\Beep.Installer.exe '/ROLLBACKDELTA=C:\Recovery\App.delta-journal.json'
```

The second and third commands are different choices, not a sequence to run automatically. Recovery revalidates actual tree state; rollback restores a verified committed backup. `/ROLLBACKDELTA` currently emits text, not the check/apply JSON envelope, and has no preview mode. Do not assume every command supports every switch.

## Disaster-recovery drill and release sign-off

Execute on a disposable qualified target, never by intentionally interrupting an important installation:

1. Record installer/package hashes, trusted-key fingerprints, policy identity, platform, scope and starting version.
2. Complete install, check, update, repair and uninstall scenarios, verifying application behavior and resource cleanup—not just exit codes.
3. Exercise a controlled interruption at the required phase boundary using the existing qualification infrastructure. Preserve all artifacts.
4. Run the applicable read-only recovery inspection; record its decision and unchanged artifacts.
5. Execute the approved recovery or rollback action. Verify application version, files, services/registrations and user-data preservation.
6. Repeat inspection/recovery to demonstrate convergence. Archive logs and journals in the release evidence set.
7. Review support bundles for sensitive information before sharing them. Runtime `/SUPPORTBUNDLEPREVIEW` produces the shared review artifact; it is not consent to transmit data.
8. Run `/QUALIFYRELEASE` against the archived evidence root using the intended preset, and resolve missing or failed evidence. Do not equate a generated checklist or a local unit-test run with an executed recovery drill.

No supported target cell or recovery drill is marked complete by this document. Sign-off belongs in the release evidence and [Phase 08](phases/PHASE_08_QUALIFICATION_RELEASE.md).
