# F15 — Update Channels and Rollout

## Current implementation and closure — 2026-09-06

### Signed AppId contract (partial)

Operational caller pins are now mandatory: Check (and therefore channel ApplyDelta) requires a nonzero ExpectedAppId plus name/publisher before opening local or remote feed content. UpdateCenterForm captures AppId from the supplied project and overrides any feed-default identity. CLI forwards /UPDATEAPPID; the ServiceApp response sample supplies its persisted project ID. The verifier compares the pin to signed metadata, publication verifies attached image identity, runtime forwards the feed ID to atomic delta validation, and replay state is keyed by AppId. Remaining: discovery/registration identity adoption, protected shared state and broader qualification evidence. Older pending caller-pin notes below are superseded.

CLI identity forwarding is implemented: /UPDATEAPPID, --update-app-id and response-file updateAppId map through the existing parser. Shared channel options and standalone feed verification forward ExpectedAppId. Delta authoring accepts the explicit pin when no project is supplied. Required operational enforcement and update-center forwarding remain open.

Feed publication now validates the attached delta's installed-image AppId, name and publisher against the authoring project before replacing output. Missing installed-image metadata and foreign IDs produce BI1576; existing feed/signature files remain unchanged. Generic directory deltas must not be published as channel attachments. Runtime handoff still independently checks signed identity, including externally signed mismatched feeds.

Channel-to-delta runtime handoff now forwards the verified feed AppId into DeltaUpdateAtomicApplyOptions.ExpectedAppId. Atomic application checks that ID against the signed installed-image descriptor before staging and again under its existing transaction boundary. Channel application therefore requires installed-image metadata; generic directory deltas are not channel application artifacts. A different product ID with matching display metadata and valid signatures is rejected. Mandatory caller pins, CLI/UI forwarding and publication-time attachment identity validation remain open.

Publication replay checkpoints now use the canonical signed AppId as their key and recorded identity, replacing display-name/publisher keys in the existing store. Signed display renames cannot reset the high-water mark; distinct AppIds retain independent histories even with matching display metadata. Existing atomic persistence and locking remain authoritative. This does not authenticate/protect local storage or close mandatory caller-pin and feed-to-delta adoption.

Feed export now includes the canonical project AppId in signed content. Shared manifest validation rejects missing, malformed and zero IDs. Verification accepts ExpectedAppId and rejects a different GUID even when the publisher signature is valid; GUID casing does not change identity. Remote verification forwards the same pin. Required operational pins, CLI/UI forwarding, replay-state key adoption and feed-to-delta AppId handoff remain unfinished. This contract change alone does not close operational identity.

Installed-record prerequisite: the existing resource execution journal now persists publisher alongside product name/version. `ResourceJournalRecoveryService.ValidateIdentity` provides one comparison used by recovery metadata checks and maintenance scope resolution. New journals take publisher from the compiled plan; same-name/different-publisher or omitted publisher records cannot match a project with a declared publisher. Runtime/scope/recovery verification: 68 tests passed. This does not yet bind channel application to the installed record, establish a stable product identifier, or authenticate journals; those requirements remain open.

The builder's **Updates** button and `/UPDATEUI /SCRIPT=<trusted-project.bsetup>` now open an update center. The expected app name/publisher comes from the loaded project, not the downloaded feed. Installed version is deliberately not populated from the authored release version. The surface delegates to the existing signed feed check/apply, recovery and rollback services; policy resolution and credential/proxy transport configuration are shared with the existing application host.

Check displays signature status, eligibility reasons and policy diagnostics. Apply is disabled until an allowed check, field edits invalidate that check, and actual application performs a fresh check. Work runs away from the UI thread, progress is displayed, and cancellation uses the engine's precommit boundary. Closing during an operation requests cancellation and keeps the form alive until the worker finishes. Recovery/rollback require explicit confirmation and the existing journal guards. Public-key paths and optional local delta folders are supported; empty local delta selection uses remote acquisition.

Persistent acquisition cache is also implemented: verified blobs are reused, interrupted blob downloads resume across CLI processes, and mutable metadata is fetched and verified again. `/UPDATECACHE` selects the root. A disconnected-transfer fixture proved reconnect in a new process; this is not power-loss qualification.

The maintenance workflow now includes Beep-managed file/folder pickers. Recovery first invokes the existing read-only preview, displays the resulting state, installation path, version and journal, and requires confirmation before invoking recovery again with fresh engine validation. Successful apply/rollback/recovery results expose `InstalledVersion` from the manifest or validated journal/tree decision; the UI uses that result to refresh its installation fields. It clears the restored channel when the journal cannot establish it. Preview returns the expected resulting version without writing it to the installation. Verification: 27 focused UI/delta tests passed, including target/base version reporting through apply, preview and rollback.

Update-center UI strings now use the existing language manager and resource files for English, Arabic, Spanish, French, German, Chinese, Japanese and Portuguese. The 39 keys include accessible labels, confirmations, status messages and picker filters; existing Browse/Cancel resources are reused. RTL follows the selected installer language, independently of the OS language. Shared resource lookup now loads its selected default language lazily so direct form construction does not display raw keys. A compiled-resource test checks key presence and format argument counts in every shipped language. Seven focused UI/localization tests passed. Engine diagnostic details remain technical output and translations still require native-language review.

Operational `Check` and `ApplyDelta` now enforce a seven-day maximum age for signed feed publication metadata, with up to five minutes of forward clock skew. Export writes `CreatedUtc` into the signed manifest; deserialization no longer invents a current timestamp if the field is absent. Expired, future-dated and missing timestamps produce BI1579 with no eligibility decision and no delta acquisition/application. The same captured evaluation time is used for scheduling. `Verify` remains an artifact inspection operation and does not reject historical signed evidence solely because it is old. Publishers must refresh and sign metadata at least weekly; device clocks must be correct. This bounds staleness but does not replace persisted replay tracking within the valid window. Verification: 76 feed/qualification/apply tests passed, including exact age/skew boundaries.

Persistent replay checks now record accepted signed publication timestamps and feed hashes, keyed by expected product name and publisher independently of URL or signing key. After signature, identity, freshness and channel policy verification, a shared nonblocking operation lease serializes checkpoint comparison/publication through the existing atomic writer. Older timestamps and changed content at the same timestamp yield BI1580 without eligibility. Identical repeated metadata does not rewrite the checkpoint. Invalid or unreadable state blocks checks rather than silently resetting it.

`/UPDATESTATE=<directory>`, `--update-state=` and response-file `updateState` configure the checkpoint root; the update-center launch forwards the same option. Default state is per-user LocalApplicationData/BeepInstaller/UpdateState, separate from the disposable download cache. Checks and eligibility previews write accepted trust metadata but do not install payloads. Hold/revocation decisions still retain newer accepted publication metadata. Preserve this directory: deleting or replacing writable local checkpoints resets or changes the remembered publication boundary. Protected shared/multi-user state deployment and concurrent check-to-apply qualification remain open. Verification: 77 feed/qualification/process tests and four focused older/same-time-change/repeat/corrupt-state cases passed.

Channel application now requires the canonical resource journal in the selected installation, with matching product name/publisher and numerically equivalent base version. The shared journal identity validator is reused. An early read prevents payload acquisition for mismatched installations; atomic apply repeats the check under the existing installation lease. Before commit, the staged journal must match the signed target version and expected identity. Missing or mismatched target records preserve the current installation and do not create the delta commit journal or rotate backups. Journal files are not excluded from signed base/target tree hashes or synthesized during apply.

Release-image requirement: build channel deltas from complete matching base/target installation images containing their canonical resource journals. Raw payload-only targets are insufficient for channel application. The target journal must describe the actual target installation; production authoring must not use the simplified test records. Custom out-of-tree installed journals are not yet handled. Verification: 106 feed/delta/process tests plus six current/target publisher/version/missing-record cases passed. Actual installed-image authoring/lifecycle qualification remains open.

Cache quota and retention are implemented on the existing acquisition path. Defaults are 4 GiB and 30 days; SDK `MaximumCacheBytes`/`CacheRetention`, CLI `/UPDATECACHEMAXBYTES`/`/UPDATECACHERETENTIONDAYS`, modern aliases and response keys configure them. CLI and UI now share one feed-options factory. A cache-root lease serializes reservations; the existing per-entry lease stays held through download/application. Reservations include signed delta bytes and 17 MiB metadata allowance, and downloaded manifest blob totals must fit the signed reservation.

Each acquired entry has a versioned ownership/reservation marker. Preparation evicts expired entries and then older entries as necessary for quota, but only when the entry lease is available and its hash-directory contents match the known metadata/blob layout. Unknown/unmarked entries count toward usage but are preserved. Cleanup deletes inspected files and empty directories without recursive tree deletion. Insufficient reclaimable space fails rather than deleting active or unowned data. Cache roots cannot overlap installation, stage, backup, commit-journal or replay-state paths. Deleting cache entries is permanent but payloads can be downloaded again; publication replay checkpoints are never cache eviction targets. Verification: 22 remote apply/reconnect checks and 52 cache/UI/parser/process checks passed; focused tests cover quota eviction, active/unowned preservation and expiration with spare capacity. Managed-host concurrent capacity and retention qualification remain open.

Remaining implementation: custom installed-journal integration; stable product identifiers remain an open identity design item. Remaining UI validation: rendered layout, high-DPI, keyboard, screen reader, themes and linguistic review. Remaining deployment evidence: real installed-image lifecycle, concurrent cache capacity, protected/shared replay state, authenticated TLS/proxy and official target lifecycle runs. Earlier dated remaining-work paragraphs below are historical, not the current backlog.

Verification for the UI baseline: app build succeeded; 38 UI construction/eligibility invalidation and CLI parser tests passed. No visual walkthrough or complete interactive lifecycle is claimed yet.

**Outcome:** controlled stable/beta/internal releases with enterprise scheduling.

**Design:** signed feed metadata for channel, ring, rollout percentage, deadline, minimum version, critical/block-activation, maintenance window, proxy and rollback target; device cohort selection is deterministic and privacy preserving.

**Acceptance:** channel transitions and policy precedence tested; staged rollout is reproducible; revoked release stops; critical update behavior is explicit; offline devices recover on reconnect. Depends on F14 and F11.

## Implemented — 2026-09-06 caller-pinned feed identity

Operational `Check`/`ApplyDelta` now require `ExpectedAppName` and `ExpectedAppPublisher` in feed options. CLI check/apply require `/UPDATEAPPNAME=<name>` and `/UPDATEPUBLISHER=<publisher>` (also `--update-app-name=`, `--update-publisher=` and response keys `updateAppName`, `updatePublisher`). Callers must obtain these from trusted deployment configuration, not the incoming feed. Missing identity returns `BI1578` before acquisition. The shared feed verifier compares the signed name/publisher exactly; mismatch yields no eligibility decision or delta application. This prevents accepting another product's feed merely because it uses the same signing key.

Artifact-only `Verify`/`VERIFYUPDATECHANNELFEED` still supports signature inspection without an expected product; when either identity field is supplied, both are required and enforced by the same verifier. There is no second identity checker. Existing SDK and process fixtures now provide their independently known product identity.

Verification: 104 feed/CLI/parser tests passed; final 17 direct-verifier, apply and required-identity tests passed after consolidating the comparison. Same-key wrong-name/wrong-publisher feeds leave installed files unchanged, create no stage/journal and make no delta requests. Missing identity makes no feed requests. Identity pinning is currently the name/publisher pair, not a stable product ID or authenticated binding to an installed journal; deployment hosts must provide the correct expected identity. Replay/freshness protection, cross-run cache/reconnect, UI integration and official deployment evidence remain.

## Design — update transport credentials and proxy

Extend `PackageAcquisitionStore` with runtime-only transport options, reused by feed and delta retrieval. Resolve bearer tokens and proxy passwords through the existing `ISecretProvider`/`SecretReference` contract; no raw secrets in CLI arguments, feed artifacts or result objects. Bearer credentials require an explicitly configured HTTPS origin and are never forwarded to other origins or redirects. Explicit proxy URI/user/password-reference settings configure the existing HTTP handler; authenticated proxies require HTTPS. Verify request headers and proxy settings without live credentials, plus fail-closed secret resolution and cross-origin isolation. This is endpoint consumption, not a new login/token-issuance system. Existing F15 tracker remains authoritative; no parallel auth roadmap.

### Implemented and verified

`PackageDownloadTransportOptions` configures the existing downloader, and `UpdateChannelFeedVerificationOptions.Transport` passes those settings through feed and delta acquisition. Credential fields/providers are excluded from JSON serialization. Resolution reuses `SecretReference` and `ISecretProvider`; provider failure details and literal credential values are not included in configuration errors. Bearer secrets are resolved only for the configured HTTPS scheme/host/port, and automatic redirects are disabled. Other origins receive no bearer header. Proxy credentials belong to the HTTP handler's proxy object, not origin request headers. Unauthenticated explicit HTTP proxies are supported; authenticated proxies require HTTPS. Leaving proxy settings empty retains the handler's system proxy behavior.

CLI/response/modern-alias controls: `/UPDATEAUTHORIGIN`, `/UPDATEBEARERREF`, `/UPDATEPROXY`, `/UPDATEPROXYUSER`, `/UPDATEPROXYPASSWORDREF` (all value options). Example: `/UPDATEAUTHORIGIN=https://updates.example.test /UPDATEBEARERREF=env:UPDATE_TOKEN`. CLI uses the existing default environment-secret provider; SDK hosts can inject another existing provider implementation. References and credentials are never added to signed feed metadata.

Verification: 62 transport/feed/remote-CLI/parser checks passed; final 14 transport/parser tests passed with additional insecure-proxy rejection and alias/response coverage. Tests inspect scoped bearer headers, same-host different-port/scheme isolation, provider-error redaction, proxy credential separation and serialization exclusions. A real loopback proxy serves a shared-downloader request addressed to an otherwise unresolved host. Live authenticated TLS endpoints/proxies, enterprise trust stores and token renewal remain to qualify; header configuration tests are not claimed as live authenticated deployment evidence.

## Implemented — 2026-09-06 remote signed feed retrieval

SDK `Check` and CLI `/CHECKUPDATECHANNEL` / `/APPLYUPDATECHANNEL` accept an HTTP(S) URL ending in `/beep-update-channels.json`. The sibling canonical signature is fetched through the existing size-limited, cancellation-aware downloader with redirects disabled. Credentials, queries and fragments in the feed URL are rejected. A trusted key must be supplied before acquisition begins; cryptographic verification remains mandatory before channel evaluation or delta retrieval.

The existing policy evaluator now exposes `EvaluateUpdateSource`, sharing update URL/host/transport rules with channel validation. It gates the actual feed source before network access, without requiring an as-yet-unverified selected channel. Channel constraints are checked after signature verification. Policy provenance and diagnostics carry through feed, channel and delta checks using one evidence helper. CLI cancellation now covers eligibility checks and previews as well as application.

Remote eligibility previews download the two metadata files, but never acquire delta payloads or change the installation. Feed downloads and partials are cleaned up using only their known generated filenames, without recursive deletion; cleanup failures do not replace the operation result. Delta packages retain the prior recovery behavior.

Verification: 89 feed/policy/CLI tests passed after fixing a no-request shutdown race in the loopback test server. Real CLI processes cover remote feed plus remote delta apply/rollback, hold and preview; the latter two fetch only metadata. SDK checks cover allowed channel selection after download, denied source hosts, forbidden HTTP, missing keys, valid-JSON signature tampering and redirect refusal. Remaining: authenticated endpoints and explicit proxy controls, cross-run cache/reconnect recovery, feed identity/replay protection, UI integration and official TLS/managed-device qualification.

## Implemented — 2026-09-06 remote delta delivery

Feed attachments can carry a signed `PackageBaseUrl`. Export accepts `/UPDATECHANNELDELTABASEURL=https://host/release/` alongside `/DELTA=<dir>`; the SDK uses `DeltaPackageBaseUrl`. Modern alias `--update-channel-delta-base-url=` and response-file key `updateChannelDeltaBaseUrl` are recognized. A URL without an attachment is rejected. Package locations must be HTTP(S) directories without credentials, queries or fragments.

`/APPLYUPDATECHANNEL` now acquires the signed remote package when `/DELTA` is omitted. Local package application is unchanged. Eligibility and enterprise update-host/insecure-source policy run before network access, preserving policy provenance and diagnostics in the existing result. The shared `PackageAcquisitionStore` now supports cancellation, byte limits and redirect rejection; its existing retry/partial-download implementation is reused. Resume responses must match the requested byte offset.

Acquisition downloads bounded manifest/signature files, verifies the existing delta signature, binds its versions/tree hashes to the signed feed, then downloads only canonical content-addressed blobs. Blob sizes and hashes are checked before invoking the existing atomic apply engine. Redirects are refused rather than following a host not evaluated by policy. The signed package is retained in a unique temporary directory, recorded in the commit journal on success. Hold and eligibility-only dry run perform no acquisition.

Verification: 115 feed/provider/parser/local-CLI tests passed; 18 focused remote-CLI and malformed-URL tests passed; final 19 acquisition/apply tests passed after policy-evidence and download-limit work. Real loopback HTTP and CLI processes cover remote apply/rollback, no-network hold/preview, denied hosts, same-key package substitution, corrupt blobs, redirect refusal, existing range resume, byte limits and pre-cancelled acquisition. Corrected the existing range test server to emit the required Content-Range header.

Remaining: remote retrieval of the feed itself, authenticated endpoints/explicit proxy configuration, stable cross-run cache and reconnect recovery, bounded cache cleanup, feed identity/replay protection, UI integration and official TLS/managed-device release evidence. Per-call retries and retained temporary packages do not prove complete offline/reconnect behavior. HTTP remains governed by `forbidInsecureRemoteSources`; production policies should enable it.

## Implemented — 2026-09-06 signed delta attachment contract

Feed export and runtime verification now share attachment validation: non-null collections/entries, valid numeric versions, strictly increasing targets, one delta per normalized base version, canonical manifest/signature filenames, SHA-256 tree hashes and non-negative size metadata. Invalid attachments produce `BI1576`, even if the feed has a valid publisher signature. Ordinary channel application cannot silently downgrade or reinstall an equal version; explicit journal rollback remains the restoration path. Export also rejects an undeclared attachment target instead of silently omitting the delta.

Application selects the signed base using the shared numeric version grammar (`2.0` equals `2.0.0`), then passes the signed spelling to the existing delta engine. Removed the weaker duplicate attachment hash check from application; verified metadata is the single contract.

Verification: 43 feed/CLI/qualification tests passed, followed by all 3 invalid-export preservation cases after adding unknown-channel rejection. Signed malformed-feed cases cover downgrade, equal versions, ambiguous normalized bases, nulls, paths, hashes and sizes. Existing release artifacts remain unchanged on invalid export; signed application/rollback also succeeds with an equivalent installed-version spelling. Remote acquisition, UI integration, feed identity/replay protection and official clean-target evidence remain open.

## Implemented slice — 2026-09-01 canonical update channel model

Update channels are now first-class project metadata instead of being implied by one update URL:

- Added selected `AppUpdateChannel` to the canonical setup fields.
- Added `[UpdateChannels]` authoring with:
  - `Id`
  - `Name`
  - `Ring`
  - `FeedUrl`
  - `RolloutPercentage`
  - `MinimumVersion`
  - `DeadlineUtc`
  - `Critical`
  - `MaintenanceWindow`
  - `RollbackVersion`
  - `Revoked`
- Added `InstallProject.UpdateChannels` and `UpdateChannelDefinition`.
- Added `.bsetup` read/write support and canonical JSON/golden-fixture coverage.
- Added `bsetup-1.0.schema.json` validation for update channels.
- Added strict schema diagnostics:
  - `BI1151` missing channel id.
  - `BI1152` duplicate channel id.
  - `BI1153` rollout percentage outside 0–100.
  - `BI1154` non-absolute feed URL.
  - `BI1155` invalid minimum version.
  - `BI1156` invalid rollback version.
  - `BI1157` revoked channel selected/authored as active project metadata.
  - `BI1158` selected `AppUpdateChannel` is not declared.
- MSIX/AppInstaller builds now use the selected channel `FeedUrl` when present.
- Critical channels set AppInstaller `UpdateBlocksActivation=true`.
- `msix-capabilities.json` records selected channel metadata and the full channel list.
- F11 policy can now allow-list update channels with `allowedUpdateChannels`.

## Implemented slice — 2026-09-01 deterministic rollout cohort evidence

Staged rollout assignment is now deterministic and privacy preserving:

- Added a canonical `UpdateRolloutEvaluator` for selected update channels.
- Cohort material is derived from app name, publisher, channel id, channel ring and a local cohort seed.
- The persisted evidence stores only a SHA-256 cohort hash and bucket `0–99`; the raw cohort seed is not written to `msix-capabilities.json`.
- Partial rollout includes a device when `bucket < RolloutPercentage`.
- Revoked channels always block inclusion.
- `RolloutPercentage=0` always excludes; `RolloutPercentage=100` always includes.
- `msix-capabilities.json` now records the selected channel rollout decision beside AppInstaller feed/channel metadata.

## Implemented slice — 2026-09-01 signed update channel feed metadata

Update channel metadata can now be exported as a signed release artifact:

- Added `UpdateChannelFeedPackageService` in the canonical update packaging area.
- Exports `beep-update-channels.json` containing app identity, selected channel id, issuer and declared channel/ring/feed/rollout/deadline/critical/rollback/revocation metadata.
- Writes detached `beep-update-channels.json.sig` signatures using the shared RSA-SHA256 verifier format already used by project templates, prerequisite catalogs and offline layouts.
- Verification requires a trusted public key by default.
- Verification fails closed when the feed is tampered, the signature is missing, or no trusted key/key path is configured.
- Feed metadata is ordered by channel id for stable review output.
- Enterprise command parsing now recognizes signed-feed export/verify vocabulary:
  - `/UPDATECHANNELFEED=<script.bsetup>`
  - `/VERIFYUPDATECHANNELFEED=<beep-update-channels.json>`
  - `/UPDATECHANNELFEEDSIGNKEY=<private.pem>`
  - `/UPDATECHANNELFEEDTRUSTKEY=<public.pem>`
  - `/UPDATECHANNELFEEDISSUER=<name>`
  - modern aliases such as `--update-channel-feed=` and JSON response-file keys such as `updateChannelFeed`.
- The actual CLI dispatch now executes signed-feed export and verification through the same canonical `UpdateChannelFeedPackageService`.

## Implemented slice — 2026-09-01 channel transition and revocation decisions

Channel transition behavior is now testable before VM qualification:

- Added `UpdateChannelTransitionEvaluator` in the canonical update packaging area.
- The evaluator consumes signed update-channel feed metadata and reuses `UpdateRolloutEvaluator` for staged rollout decisions.
- It allows same-channel update or cross-channel transition only when the target channel exists, is not revoked, passes minimum-version checks and includes the cohort.
- It returns a `hold` decision when staged rollout excludes the cohort.
- It returns a `rollback` decision when the target channel is revoked and declares a rollback version.
- It blocks missing feed, undeclared target channel and installed-version-below-minimum cases.

## Implemented slice — 2026-09-02 maintenance-window and deadline enforcement

Enterprise scheduling is now enforced by the canonical transition evaluator instead of being feed-only metadata:

- `UpdateChannelTransitionEvaluator` now accepts an explicit UTC evaluation time for deterministic CI and release evidence.
- Transition decisions now preserve `DeadlineUtc`, `DeadlineReached`, `MaintenanceWindow` and `MaintenanceWindowMatched` evidence.
- Non-critical updates outside the declared UTC maintenance window return `hold`.
- Non-critical updates inside the declared UTC maintenance window proceed.
- Reached deadlines allow non-critical updates to proceed even outside the declared maintenance window.
- Critical updates can proceed outside the declared maintenance window and record the bypass reason.
- The implementation extends the existing transition evaluator and signed-feed model; no duplicate scheduler or channel engine was added.

## Implemented slice — 2026-09-01 update channel qualification evidence runner

Release teams can now generate repeatable local evidence before running clean VM qualification:

- Added `UpdateChannelQualificationRunner`.
- Writes `update-channel-qualification.json`.
- Records host machine, OS, architecture, feed path, output path and scenario outcomes.
- Includes signed feed verification evidence.
- Includes tampered-feed negative preflight evidence.
- Includes offline/reconnect evidence: unavailable feed fails closed, restored trusted feed verifies.
- Includes transition, staged-rollout hold and revoked-rollback decision evidence.
- Reuses `UpdateChannelFeedPackageService` and `UpdateChannelTransitionEvaluator`; no duplicate feed parser or transition engine was added.

## Implemented slice — 2026-09-01 update channel qualification CLI

The evidence runner is now available through the installer executable, not only through code-level tests:

- Added `/QUALIFYUPDATECHANNELFEED=<beep-update-channels.json>`.
- Added enterprise automation options:
  - `/UPDATECHANNELCURRENT=<id>`
  - `/UPDATECHANNELTARGET=<id>`
  - `/UPDATECHANNELINSTALLEDVERSION=<version>`
  - `/UPDATECHANNELCOHORT=<seed>`
  - `/UPDATECHANNELLIFECYCLE`
  - `/UPDATECHANNELSCRIPT=<current.bsetup>`
  - `/UPDATECHANNELUPDATEDSCRIPT=<updated.bsetup>`
  - `/UPDATECHANNELDOWNGRADESCRIPT=<older.bsetup>`
  - `/UPDATECHANNELINSTALLDIR=<dir>`
  - `/UPDATECHANNELINSTALLER=<exe>`
  - `/UPDATECHANNELFEEDTRUSTKEY=<public.pem>`
  - `/OUT=<dir>`
- Added modern aliases such as `--qualify-update-channel-feed=`, `--update-channel-current=`, `--update-channel-target=`, `--update-channel-installed-version=`, `--update-channel-cohort=`, `--update-channel-lifecycle`, `--update-channel-script=`, `--update-channel-updated-script=`, `--update-channel-downgrade-script=`, `--update-channel-install-dir=` and `--update-channel-installer=`.
- Added JSON response-file keys for the same options.
- The command invokes the canonical `UpdateChannelQualificationRunner`, which itself reuses signed-feed verification and channel-transition evaluation.
- CLI output summarizes scenario pass/fail state and writes `update-channel-qualification.json` for release evidence archives.
- When `/UPDATECHANNELLIFECYCLE` is supplied, the same report now adds command evidence for install-current, update-target, downgrade-blocked and uninstall-current.
- Lifecycle command evidence captures command line, executable, arguments, working directory, exit code, stdout, stderr and per-action evidence files.
- `/DRYRUN` records planned lifecycle evidence without mutating the current machine; official VM/managed-device qualification can run the same command without `/DRYRUN`.
- Process-level coverage now proves real executable export, qualification and report creation.

## Verification

- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "InstallerScriptSerializerTests|ProjectSchemaServiceTests|InstallerPolicyTests|MsixPackagerTests" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "UpdateRolloutEvaluatorTests|MsixPackagerTests" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "UpdateChannelFeedPackageServiceTests|UpdateRolloutEvaluatorTests|MsixPackagerTests" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "UpdateChannelFeedPackageServiceTests|UpdateChannelFeedAliasesAndJsonResponse" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "UpdateChannelFeedCli|UpdateChannelFeedPackageServiceTests|UpdateChannelFeedAliasesAndJsonResponse" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "UpdateChannelTransitionEvaluatorTests|UpdateChannelFeedCli|UpdateChannelFeedPackageServiceTests|UpdateChannelFeedAliasesAndJsonResponse" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "UpdateChannelQualificationRunnerTests|UpdateChannelTransitionEvaluatorTests|UpdateChannelFeedCli|UpdateChannelFeedPackageServiceTests" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "UpdateChannelQualificationRunnerTests|UpdateChannelTransitionEvaluatorTests|UpdateChannelFeedCli|UpdateChannelQualificationCli|UpdateChannelFeedAliasesAndJsonResponse" --no-restore --nologo --verbosity quiet`
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "UpdateChannelTransitionEvaluatorTests" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`

## Remaining hardening

### Implemented — 2026-09-06 operational eligibility command

`/CHECKUPDATECHANNEL=<feed.json> /UPDATECHANNELFEEDTRUSTKEY=<public.pem> /UPDATECHANNELINSTALLEDVERSION=<version>` checks a device against a trusted signed feed without running qualification scenarios, downloading payloads or applying updates. Optional existing switches select current/target channels and the cohort seed. `--check-update-channel=` and response-file `checkUpdateChannel` map through the canonical enterprise parser; `/JSON` returns signature status, decision and diagnostics without the raw cohort seed.

The command delegates to `UpdateChannelFeedPackageService.Check`, which requires signature trust even if an SDK caller supplies verification options with signatures disabled. It evaluates only the verified manifest using the existing transition evaluator. Exit 1 means verification failed; exit 2 means a blocked decision; exit 0 means evaluation completed with update/transition/hold/rollback. Automation must inspect `decision.allowed` and `decision.action` before taking any action; successful checking is not authorization to install.

Verification: 21 feed-package, CLI process and command-parser tests passed. The process test exports a real signed feed, checks it through the modern alias, verifies JSON/privacy behavior, tampers the feed and confirms no decision is returned.

Operational policy integration is now implemented: the CLI resolves project/profile/machine policy through `InstallerPolicyResolver`, passes load failures into the SDK check, and evaluates the selected verified channel through shared `InstallerPolicyEvaluator` rules. Channel and update-host allowlists, required update URL, policy validation, signed emergency overrides and insecure-HTTP restrictions apply before any decision is exposed. Update hosts use their dedicated allowlist; package-download hosts remain separately scoped. Policy failures return exit 2 with no eligibility decision. JSON includes safe policy evidence, not raw policy/key material.

Verification: 50 policy, feed-package and CLI process tests passed, covering denied channels, denied hosts, insecure update URLs and a real `/MACHINEPOLICY` denial against a signed feed.

The SDK now exposes `UpdateChannelFeedPackageService.ApplyDelta`: it runs the trusted policy-aware check, requires an allowed decision, selects exactly one declared delta for the installed version and requires a trusted delta signature. The existing delta engine receives expected base/target hashes and target version, checks them before staging and again before its journal/rotation step, and performs the existing apply/rollback lifecycle. Caller-supplied expectation fields cannot replace the signed feed's values.

Verification: 30 feed/delta tests passed. Real temporary-file scenarios cover signed apply plus rollback, rollout hold without staging/journal creation, and rejection of a substituted delta signed by the same key but carrying different target content. No second delta engine was introduced.

The local-package handoff is now exposed through `/APPLYUPDATECHANNEL=<feed.json>` with `/DELTA=<package-dir>`, `/DELTACURRENT=<install-dir>`, `/DELTASTAGE=<stage-dir>`, `/UPDATECHANNELINSTALLEDVERSION`, `/UPDATECHANNELFEEDTRUSTKEY` and `/DELTATRUSTKEY`. Existing channel selection, cohort, policy and journal switches apply. Modern alias `--apply-update-channel=` and response key `applyUpdateChannel` use the canonical parser. Check/apply commands share their CLI handler; neither introduces another execution engine.

`/JSON` reports action, decision, policy evidence, applied status and the delta result/journal. `/DRYRUN` is explicitly an eligibility-only preview: `previewOnly=true`, `applied=false`, no delta staging or journal writes. It does not claim package-content validation. Hold/denied apply decisions exit 2; verification or application failures exit 1; applied updates or successful eligible previews exit 0. Existing `/ROLLBACKDELTA` handles rollback.

Verification: five targeted process/parser tests passed, including real signed CLI apply plus CLI rollback, rollout hold without writes, read-only preview, signed eligibility checking and tamper rejection.

Remaining runtime integration: UI handoff, authenticated remote acquisition, interruption handling and official clean-target lifecycle evidence. Local-package application does not prove automatic network update delivery.

### Implemented — 2026-09-06 publication failure recovery

The existing feed exporter stages and flushes both payloads before replacement, serializes publishers through an exclusive package-local lock and restores the prior feed if signature replacement fails. A failed first publication removes the newly placed feed; unsuccessful restoration retains the backup for recovery. Output file links are rejected. Expected temporary files are cleaned up, and a later export can reacquire the lock.

Verification: 20 feed-package/qualification tests passed. Real Windows file locks forced signature replacement failures for existing and first-time feeds; assertions prove restoration/removal, unchanged signature, staging cleanup and a successful verified retry. This is recoverable publication for caught I/O failures, not crash-atomic two-file commit; readers still reject mismatched signatures during replacement.

### Implemented — 2026-09-06 feed verification boundary

The existing feed verifier rejects null/empty channel collections, null entries, duplicate or empty identities, undeclared selected channels, unsupported schema/signature algorithms and invalid rollout/version/window settings before trust processing. Eligibility grammar reuses the transition evaluator. Signature filenames must match the canonical package-local sidecar; signature filesystem links are rejected. Expected I/O, key-import and cryptographic failures become structured diagnostics rather than escaping the verification API.

Verification: 47 feed-package, transition and update-channel qualification tests passed, including malformed manifests, signature traversal and malformed trust keys.

Export now calls the same extracted manifest validator as verification. Invalid rollout percentages are rejected rather than clamped. Manifest validation and signature computation finish before output creation or file writes, preserving existing release artifacts when authoring or signing fails. An additional five negative export cases cover windows, versions, rollout, duplicate identities and undeclared selection; all 52 feed/transition/qualification tests passed. This does not claim two-file publication is atomic under an I/O failure. Clean-target lifecycle evidence remains open.

### Implemented — 2026-09-06 fail-closed version eligibility

The transition evaluator no longer treats a missing installed version as satisfying a configured minimum. Channel minimum, rollback and installed versions use one numeric grammar: one to four dot-separated non-negative integer components, with omitted components compared as zero. Empty components, extra components, signs, overflow and prerelease suffixes are rejected rather than stripped or truncated. Authoring minimum/rollback checks use the same parser. Malformed rollback targets are blocked before a revoked channel can recommend rollback. Valid lower versions retain the existing below-minimum diagnostic.

This is the current development contract, not a compatibility conversion. Version parsing for unrelated installer features is unchanged.

Verification: 51 transition, feed-package, update-channel qualification and project-schema tests passed, including malformed/missing version rejection, equivalent numeric versions and invalid revoked-channel rollback targets.

### Implemented — 2026-09-06 strict maintenance-window contract

Authoring and runtime share `UpdateChannelTransitionEvaluator.IsValidMaintenanceWindow`. Nonempty windows require an English day name (full or three-letter), two distinct invariant `HH:mm` times and the `UTC` suffix, for example `Sat 23:00-01:00 UTC`. Invalid windows produce authoring diagnostic `BI1159` and a blocked transition; critical flags and reached deadlines cannot bypass invalid configuration. Valid overnight windows wrap into the next day, including Saturday to Sunday. Intervals include the start and exclude the end.

Verified: 34 transition, signed-feed package and project-schema tests passed. Added negative cases for invalid hours, negative times, equal endpoints, malformed clock syntax and missing UTC, plus overnight/week-boundary and exact-end behavior. This replaces permissive culture-dependent parsing in the existing evaluator rather than adding a scheduler.

- Execute and archive CLI-generated `update-channel-qualification.json` from official clean VM/managed-device targets using the implemented lifecycle command set.
