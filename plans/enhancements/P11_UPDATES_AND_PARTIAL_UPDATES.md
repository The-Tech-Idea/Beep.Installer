# Phase 11: Updates, Partial Updates & the NuGet Module Channel — Task List

> 📄 Design: [P11_UPDATES_AND_PARTIAL_UPDATES_DESIGN.md](P11_UPDATES_AND_PARTIAL_UPDATES_DESIGN.md) ·
> Tracker: [MASTER_TRACKER.md](MASTER_TRACKER.md)
> Status: ⬜ not started · Priority: P1 · Depends on: P10.A · **Blocked by decisions D10 (hosting), D11 (feed integrity)** · Placement fixed by **D12**: the update API lives in **BeepDM** so any Beep-based app gets self-update by registering one service; Beep.Installer only publishes the feed and provisions the settings.

Existing BeepDM code this phase builds ON, not around (verified 2026-07-23):

| Exists | Where | Used by |
|---|---|---|
| NuGet update engine: `UpdateAsync(id, version, dir)`, `BulkUpdateAsync`, `CheckForUpdatesAsync`, `GetPackagesWithUpdatesAsync` | `NuGetManagement/Services/UpdateService.cs:39,124,153,164` | 11.C `ModuleUpdater` (thin, feed-pinned wrapper) |
| Package inventory + install/load | `IAssemblyHandler.cs:179,195,238` | 11.C |
| Hash verify | `InstallHelpers.VerifyFileHash:29` | 11.A/11.B |
| Content-addressed payload (`_blobs/<sha256>` + manifest) | `Beep.Installer.Core/Build/PayloadPackager.cs:16` | 11.B deltas |
| Version ledger `RecordAppVersion` | `Services/AppMap/IVersionManagementService.cs:26` | 11.C (record applied updates) |
| Approval tokens (if D10=B ever) | `Studio/Deployment/IDeploymentMetadataService.cs` | future gated rollout |
| Name to avoid | `Pipelines/Models/ReleaseManifest.cs` (unrelated pipeline domain) | POCO naming |

---

## Stage 11.A — Contracts, feed client & publishing

| # | Task | Files | Verify | Status |
|---|------|-------|--------|--------|
| 11.A.0 | Record D10 (hosting) and D11 (feed integrity) in the tracker | `MASTER_TRACKER.md` | rows updated | ✅ (both **A**, 2026-07-24) |
| 11.A.1a | **BeepDM Models**: `Updates/` contracts — `IAppUpdateService`, `UpdateFeed`, `AppReleaseInfo`, `ArtifactRef`, `ModuleRef`, `UpdateCheckResult`, `UpdateSettings` (avoid the name `ReleaseManifest`) | new `BeepDM/DataManagementModelsStandard/Updates/*.cs` | Models builds; round-trip test vs design §1 JSON | ✅ (adds `DeltaRef`; reuses `Installer.UpdateMode`) |
| 11.A.1b | **BeepDM Engine**: `UpdateFeedClient` — async fetch + parse + per-artifact SHA-256 (`VerifyFileHash`); injectable fetcher; named errors, no silent nulls | new `BeepDM/DataManagementEngineStandard/Updates/UpdateFeedClient.cs` (+ `IFeedTransport`/`HttpFeedTransport`, `UpdateFeedException`) | corrupt-hash → named error; malformed-feed test | ✅ |
| 11.A.1c | `AddBeepAppUpdates(settings)` DI registration; default settings source = `update-settings.json` beside the app | new `Updates/UpdateServiceExtensions.cs` (+ `AppUpdateService` with `CheckAsync`; apply/rollback return named "not yet" until 11.B/C) | resolves `IAppUpdateService` in a test host | ✅ |
| 11.A.2a | **Installer publisher**: `/PUBLISHFEED=<dir>` stage — `<version>/_blobs/` + manifest + full Setup.exe; `feed.json` written atomically (temp + rename) | new `Beep.Installer.Core/Build/FeedPublisher.cs` (+ `PayloadPackager.ExpandSolidToLooseStore`); `Program.cs` `/PUBLISHFEED` | v1.0 then v1.1 publish → both folders + feed at v1.1 | ✅ |
| 11.A.2b | Immutability guard: publishing an existing version **fails** without `/REPUBLISH` (the stale-3.1.1 lesson) | `FeedPublisher` | republish-refused test | ✅ |
| 11.A.2c | **Provisioning**: build stamps `update-settings.json` (feed URL/channel/mode) from the currently-decorative `AppUpdatesURL`/`AppUpdateMode` project fields | `BuildPipeline.StampUpdateSettings` (adds it as a payload file so it installs beside the app) | installed app's settings resolve to the authored feed | ✅ (unit-level; full install E2E in the skipped integration bucket) |
| 11.A.G | **Gate:** two-version publish; a test app with `AddBeepAppUpdates()` reads it back verified | 6 `FeedPublisherTests` + 12 `UpdatesTests` | two-version publish + immutability + delta store green; DI resolves + feed round-trips | ✅ (unit); live build→publish→check E2E deferred to the integration bucket |

## Stage 11.B — App delta updates (blob-level, side-by-side)

| # | Task | Files | Verify | Status |
|---|------|-------|--------|--------|
| 11.B.1 | `DeltaPlanner` (pure): remote vs local manifest → { blobs to fetch, files to write/delete, bytes vs full }; full install forced below `minSupportedVersion` | new `BeepDM/.../Updates/DeltaPlanner.cs` (+ `PayloadManifest`/`PayloadEntry`/`DeltaPlan`/`BlobFetch` models) | unit matrix: unchanged / one-file / rename (same blob → zero download) / below-min | ✅ (7 tests; full via null local; version-gate is the caller's `RequiresFullInstall`) |
| 11.B.2a | `SideBySideApplier`: stage → verify → materialize `app-<ver>\` → flip `current` junction; never writes into the running version | new `Updates/SideBySideApplier.cs` (+ `IDirectoryLink`/`JunctionLink`) | junction-flip + running-exe-untouched tests | ✅ |
| 11.B.2b | Crash safety: interrupted apply leaves junction on old; relaunch cleans staging; corrupt blob aborts pre-flip | same | kill-mid-update; corrupt-blob | ✅ (stale-staging swept next apply; corrupt blob aborts pre-flip, `PointCalls==0`) |
| 11.B.2c | Retirement (delete old after N clean launches) + `RollbackAsync` = flip back; applied versions recorded via `IVersionManagementService.RecordAppVersion` | same + `AppUpdateService` | rollback test; ledger entry present | 🟡 `Rollback` + `RetireOldVersions` done (8 tests); `OnApplied` hook exposed — ledger wiring lands with the `AppUpdateService` composition in 11.C |
| 11.B.G | **Gate:** v1.0→v1.1 delta downloads only changed blobs (assert bytes), survives kill, rolls back | — | — | ⬜ |

## Stage 11.C — Module channel (feed-pinned NuGet updates) & policy

| # | Task | Files | Verify | Status |
|---|------|-------|--------|--------|
| 11.C.1a | `ModuleUpdater` as a thin governed layer over the **existing** `UpdateService`: inventory via `GetPackagesWithUpdatesAsync`, intersect with `feed.modules` (feed-pinned version wins over "latest"), `UpdateAsync(id, pinnedVersion)`, then sha256-verify | new `Updates/ModuleUpdater.cs` + `IModulePackageService` seam + `NuGetModulePackageService` adapter | module-only bump updates one package; app files byte-identical | ✅ (8 tests; pin-wins-downgrade, sha reject/accept) |
| 11.C.1b | v1 semantics: module updates apply **on next launch** (no ALC hot-unload); `required: true` module blocks app start until updated | same (`HasBlockingRequiredModule`) | required-module test | ✅ (required-missing is stale + blocking; hot reload out of scope by design §6) |
| 11.C.2a | `UpdatePolicy` + `AppUpdateService` façade implementing `IAppUpdateService` (check on launch + 6h; `Required` blocks, `Optional` notifies via `UpdateAvailable`; `BEEP_NO_UPDATE` opt-out) | `Updates/AppUpdateService.cs` (composes feed client + `DeltaPlanner` + `SideBySideApplier` + `ModuleUpdater`) | policy matrix | ✅ policy inputs (`Mode`, `Disabled`/`BEEP_NO_UPDATE`, `UpdateAvailable`, required-blocking) live on the settings+service rather than a separate `UpdatePolicy` class; the 6-hourly *scheduler* is trivial host glue, not a background timer here |
| 11.C.2b | Installer exe verbs `/CHECKUPDATE` `/UPDATE` as thin wrappers over `IAppUpdateService` | `Beep.Installer/Program.cs` | CLI test vs local feed dir | ✅ (shell-side; sync-over-async is fine — the guard scans Core only) |
| 11.C.2c | Retire `Beep.Installer.Core/Packaging/ClickOnce/{UpdateChecker,UpdateApplier}` → delegate to BeepDM Updates or delete; shrink `SilentFailureGuardTests` exemption list to `IInstallerHostBuilder` only | Core Packaging; guard tests | guard green with smaller exemption list | ✅ deleted (zero production callers) + their tests; exemption now `{IInstallerHostBuilder.cs}` |
| 11.C.2d | BeepDM tests for the whole Updates domain + developer-facing README (`DataManagementEngineStandard/Updates/README.md`) showing `AddBeepAppUpdates` + `CheckAsync`/`ApplyModuleUpdatesAsync` usage | BeepDM tests + README | suite green; README compiles as doc-test snippet | ✅ 38 Updates tests + README |
| 11.C.G | **Gate:** design §5 matrix end-to-end + SOLID review row | 38 BeepDM Updates tests + 6 `FeedPublisherTests` | full matrix green at unit level | 🟡 unit matrix green (delta / corrupt-blob abort / module-only / rollback / crash-safety); the live build→publish→check→update→kill-mid-update E2E is in the deferred integration bucket |

---

## Definition of done

A developer adds `AddBeepAppUpdates()` to **any** Beep-based app and gets: feed check,
hash-verified delta app updates via side-by-side junction flip, rollback, and feed-pinned
single-module NuGet updates — with Beep.Installer's only roles being `/PUBLISHFEED` and
stamping `update-settings.json` at build time. ClickOnce updater pair retired; guard
exemptions shrunk; module updates demonstrably leave app files untouched.
