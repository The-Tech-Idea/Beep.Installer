# F24 — Structured Diagnostics and Support Bundle

**Outcome:** users and support can diagnose failures without parsing localized prose.

**Design:** stable event IDs and JSON log schema with correlation/operation IDs; central redaction; configurable retention; support bundle includes logs, redacted plan, system facts and signature checks only after preview/consent; optional privacy-controlled telemetry.

**Acceptance:** failure cases link to operation/provider; localization does not alter event IDs; seeded secrets/PII are redacted; bundle content is deterministic and reviewable; logging failure never hides primary result. Depends on F02 and F21.

## Implemented runtime support bundle slice

- Added `RuntimeSupportBundleGenerator` as the canonical runtime support artifact writer for silent install, repair and uninstall.
- Added `/SUPPORTBUNDLE` and `/SUPPORTBUNDLE=<path>` with `--support-bundle`, JSON response-file `supportBundle`, and JSON response-file `supportBundlePath` aliases.
- Runtime JSON results now include `supportBundlePath` when a support bundle is requested.
- Bundles include action/result metadata, product facts, redacted install/log/journal paths, system facts, recent diagnostics, bounded redacted logs, journal hash metadata, redacted compiled plan JSON and F11 policy decision evidence.
- Enterprise policy evaluation now runs before silent install, repair and uninstall; policy failures block the operation with exit code `2` and can still emit support evidence.
- Bundle generation is best-effort and never hides the primary install/repair/uninstall result.

## Implemented structured diagnostic event slice

- Promoted `Diag` into the canonical structured diagnostic sink while preserving existing call sites.
- `DiagEntry` now carries stable machine fields: `eventId`, `level`, `operation`, `correlationId`, `context`, `message`, `errorType`, `error` and timestamp.
- Added scoped correlation through `Diag.BeginScope(operation, correlationId)` so runtime install, repair and uninstall events are tied to one operation.
- Added daily newline-delimited JSON diagnostics at `%TEMP%\Beep_Diagnostics_yyyyMMdd.jsonl` in addition to the existing human-readable text log.
- Runtime lifecycle events now emit explicit IDs:
  - `BI2401` / `BI2402` / `BI2404` / `BI2405` for install start/success/policy-block/failure
  - `BI2411` / `BI2412` / `BI2413` / `BI2414` / `BI2415` for repair start/success/not-found/policy-block/failure
  - `BI2421` / `BI2422` / `BI2424` / `BI2425` for uninstall start/success/policy-block/failure
- Support bundles now include the JSON diagnostics log path and structured diagnostic fields, so support can filter by event ID and correlation ID without parsing localized prose.

## Implemented retention and review metadata slice

- Added configurable support-bundle retention through `/SUPPORTBUNDLERETENTIONDAYS=<days>`, `--support-bundle-retention-days=<days>` and JSON response-file `supportBundleRetentionDays`.
- Added `/SUPPORTBUNDLEPREVIEW`, `--support-bundle-preview` and JSON response-file `supportBundlePreview` for review-only bundle generation.
- The canonical bundle payload now includes a `privacy` section declaring preview-only state, consent state, redaction, review requirement and sensitive-value policy.
- The canonical bundle payload now includes a `retention` section declaring whether retention is enabled, effective retention days, cutoff timestamp, output directory, match pattern and expired bundle deletion count.
- Retention cleanup is scoped to the bundle output directory, non-recursive, and only removes expired files matching the existing `*-support-bundle.json` naming contract.
- Invalid negative retention values fall back to the 30-day default; values above 3650 days are capped.

## Implemented wizard review surface slice

- Interactive installs now create the same canonical support bundle after success, step failure or unexpected wizard crash.
- Wizard-generated bundles are marked `previewOnly=true` and `consentGranted=false`; the artifact is local and reviewable before manual sharing.
- The Complete page now exposes a `View Support Bundle` action alongside the existing log option when a bundle exists.
- The Error page now exposes a `View Support Bundle` action next to `View Installation Log` and `Retry Installation`, so failed interactive installs have the same support evidence as silent installs.
- Interactive wizard lifecycle diagnostics now emit scoped event IDs (`BI2431`, `BI2432`, `BI2435`, `BI2436`) and share the same correlation ID used in the generated support bundle.
- Diagnostics tests now use scoped log directories so the JSONL verification is deterministic under full-suite parallel execution.

## Implemented privacy-controlled runtime telemetry slice

- Added `RuntimeTelemetrySink` as the canonical privacy-controlled telemetry transport for runtime install, repair and uninstall.
- Telemetry follows F11 policy exactly: `disabled` emits nothing, `local` writes redacted JSONL only, and `anonymous`/`full` send only when policy provides an HTTPS telemetry endpoint.
- The telemetry envelope includes schema version, emitted timestamp, action, correlation ID, result, exit code, message, policy status/hash and bounded structured diagnostics.
- Anonymous mode omits product identity and local paths; full mode includes product identity and redacted install/log/journal paths.
- Runtime messages and diagnostics use the shared runtime redactor so tokens, passwords, API keys, connection strings and user/temp paths are not leaked.
- Added `/TELEMETRYOUT=<path>`, `--telemetry-out=<path>` and response-file `telemetryOut` for deterministic local telemetry evidence.
- Logging failures remain non-blocking and cannot hide the primary install, repair or uninstall result.

## Implemented slice — 2026-09-02 Core sync-boundary cleanup

Removed hidden sync-over-async waits from Core-owned network/package boundaries that run inside installer setup flows.

- Runtime telemetry remote send now uses `HttpClient.Send` and preserves the same redacted envelope/result contract.
- Remote signing now posts through the synchronous HTTP boundary and reads the response stream directly, preserving audit events and signed-artifact handling.
- Payload download now streams with synchronous `HttpClient.Send` plus normal stream reads inside the existing synchronous setup-step contract.
- Package acquisition now reads remote package streams without blocking on an async stream task.
- `SilentFailureGuardTests` no longer exempts these files from the sync-over-async guard.
- Remote-signing tests now support synchronous fake HTTP handlers, so the production signer path is verified without a test-only async dependency.

## Implemented slice — 2026-09-02 ClickOnce sync-boundary cleanup

Removed the remaining ClickOnce update-path sync-over-async waits from Core.

- `UpdateChecker` now fetches deployment manifests through `HttpClient.Send` and reads the response stream directly.
- `UpdateApplier` now downloads deployment manifests, application manifests and payload files through `HttpClient.Send` plus synchronous stream copy.
- ClickOnce update code is covered by the same Core deadlock guard as the rest of the installer engine.

## Implemented slice — 2026-09-02 host-output sync-boundary cleanup

Removed the final Core sync-over-async wait from installer host publishing.

- `IInstallerHostBuilder` now drains `dotnet publish` stdout/stderr through event-based capture instead of joining asynchronous read tasks.
- `SilentFailureGuardTests` now has no Core sync-over-async exemptions; any future `.GetAwaiter().GetResult()` in Core fails the guard directly.

## Implemented slice — 2026-09-02 shell silent-failure cleanup

Removed the last shell-level empty crash-path handlers that were still hidden behind a filename exemption.

- Fatal startup/dispatch crash-log write failures now record diagnostics and emit a warning to stderr.
- Runtime fatal-message crash-log write failures now record diagnostics and emit a warning to stderr.
- `SilentFailureGuardTests` no longer exempts `Program.cs`; app-shell empty catches must either be explicit teardown races or record why they fired.

## Implemented slice — 2026-09-02 diagnostics qualification gate

Added a first-party diagnostics/support qualification gate over the existing runtime support bundle, structured diagnostics and telemetry paths.

- Added `DiagnosticsQualificationRunner` in Core under `Quality`.
- Added `/QUALIFYDIAGNOSTICS=<script.bsetup>` CLI command.
- Added command-line and JSON response-file support:
  - `qualifyDiagnostics`
  - `--qualify-diagnostics=`
  - existing `/OUT=` is reused for the generated evidence directory.
- Qualification now proves:
  - the project loads and resolves script-relative paths
  - support bundles preserve schema version, stable event IDs, correlation ID, policy evidence and redacted plan evidence
  - seeded passwords, tokens, connection-string values and journal secrets are absent from generated support evidence
  - support bundle local user/temp paths are redacted
  - preview bundles force review-before-sharing privacy metadata
  - retention cleanup deletes only expired `*-support-bundle.json` artifacts
  - disabled telemetry emits nothing and does not call a sender
  - local telemetry writes redacted JSONL without remote send or identity/path fields
  - anonymous telemetry sends only through an explicit endpoint sender and omits identity/path fields
  - full telemetry can include product identity while local paths remain redacted
  - generated diagnostics qualification evidence contains no seeded secrets.
- The gate writes `diagnostics-qualification.json` plus per-scenario diagnostics for CI and release archives.

## Remaining hardening

- Run `/QUALIFYDIAGNOSTICS` against official clean VM and managed-device images and archive `diagnostics-qualification.json`, including real endpoint acceptance/rejection evidence where an approved telemetry endpoint is available.
