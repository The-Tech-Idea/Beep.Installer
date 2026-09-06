# Phase 11: Updates, Partial Updates & the NuGet Module Channel — Design Document

**Status:** ⬜ not started · **Priority:** P1 · **Depends on:** P10.A (upgrade flow)
**Tracker:** [MASTER_TRACKER.md](MASTER_TRACKER.md) · Blocked by Open Decisions **D10, D11**

## 0. Problem Statement

Three questions this phase answers, asked directly by the product owner:

1. How does an *installed* app get updates like commercial products do?
2. How do we update **just one NuGet-packaged part** of an installed app — a partial update —
   without reinstalling everything?
3. Does this require a service API on a dedicated server?

**Scoping decision (owner, 2026-07-23, recorded as D12):** the update capability is a feature
of the **deployed app**, not of the installer. Beep.Installer only *provisions* it (stamps the
feed location into the install). The check/download/apply API therefore lives in **BeepDM** —
contracts in `DataManagementModelsStandard`, implementation in `DataManagementEngineStandard` —
so any developer building on BeepDM adds self-update to their app by registering one service,
whether or not that app was even deployed by Beep.Installer. This is consistent with BeepDM's
own installer-service plan, which lists the "online-update service" as an in-scope component
(`00-overview-and-scope.md`); only packaging and code-signing are excluded from BeepDM.

### The short answers (details below)

1. A **static JSON feed + signed artifacts over HTTPS**, checked by the installed app —
   the model Squirrel/Velopack and BeepDM's own installer-service plan use. No code runs
   server-side.
2. **Two complementary mechanisms, both mostly built already:**
   - *Module-level:* parts of the app that are NuGet packages (Beep drivers/plugins already
     are) update individually through BeepDM's existing machinery — the `IAssemblyHandler`
     surface (`UpdateNuGetPackageAsync:195`, `GetInstalledNuGetPackagesAsync:238`) backed by
     `NuGetManagement/Services/UpdateService`, which **already implements**
     `UpdateAsync(id, version, installDir)`, `BulkUpdateAsync`, `CheckForUpdatesAsync` and
     `GetPackagesWithUpdatesAsync` (returns current-vs-latest per package,
     `UpdateService.cs:39,124,153,164`). What is missing is only the *governed* layer: pin to
     the versions + SHA-256 hashes the feed names (rather than "latest on the feed"),
     required-module semantics, and policy.
   - *File-level:* the solid payload is already a **content-addressed store** —
     `_blobs/<sha256>` plus `_payload-manifest.json` mapping path → blob
     (`PayloadPackager.cs:16,23,35`). A delta update is therefore: fetch the new manifest,
     diff blob hashes against what is on disk, download **only the missing blobs**. The hard
     part of delta updates (a content-addressed format) already exists; nobody has pointed an
     updater at it.
3. **No dedicated server is required to start.** A static host (GitHub Releases, S3/Azure
   Blob, any HTTPS file server, or the existing `LocalNugetFiles`-style folder feed for LAN)
   serves feed + packages. A service API becomes worthwhile only for: staged/percentage
   rollouts, license-gated downloads, server-side telemetry, or on-demand delta generation.
   Decision **D10** records this; the design keeps the client API-agnostic so an API can be
   added later without touching installed apps.

## 1. The update feed (aligns with BeepDM's installer-service plan)

`feed.json` at a stable HTTPS URL, shape taken from
`BeepDM/.../Plans/FutureWork/installer-service/00-overview-and-scope.md:139-159` and extended
with the two partial-update sections:

```json
{
  "product": "MyApp",
  "channel": "stable",
  "latest": {
    "version": "1.2.3",
    "releasedAt": "2026-07-23T00:00:00Z",
    "minSupportedVersion": "1.1.0",
    "releaseNotes": "https://.../notes/1.2.3",
    "full":  { "url": ".../Setup-MyApp-1.2.3.exe", "sha256": "..." },
    "delta": { "manifestUrl": ".../1.2.3/_payload-manifest.json",
               "blobBaseUrl": ".../1.2.3/_blobs/" }
  },
  "modules": [
    { "id": "TheTechIdea.Beep.PostgresDriver", "version": "2.4.1",
      "feed": "https://.../nuget/v3/index.json", "sha256": "...", "required": false }
  ]
}
```

Rules: every artifact carries a SHA-256 verified before use
(`InstallHelpers.VerifyFileHash`); `minSupportedVersion` forces a **full** install when the
installed version is too old to delta from; the feed itself should eventually be signed
(D11 covers signature vs TLS-only for v1).

## 2. Client design — the BeepDM `Updates` domain (D12)

Split per BeepDM's standard Models/Engine pattern (same as ETL and SetUp):

**Contracts — `DataManagementModelsStandard/Updates/`** (namespace `TheTechIdea.Beep.Updates`):

```csharp
public interface IAppUpdateService
{
    /// <summary>Fetch + verify the feed; compare against the installed state.</summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default);

    /// <summary>Apply an app update (delta when possible, full otherwise), side-by-side.</summary>
    Task<IErrorsInfo> ApplyAppUpdateAsync(UpdateCheckResult check, IProgress<PassedArgs>? progress = null, CancellationToken ct = default);

    /// <summary>Update only the stale NuGet modules listed in the check result.</summary>
    Task<IErrorsInfo> ApplyModuleUpdatesAsync(UpdateCheckResult check, IProgress<PassedArgs>? progress = null, CancellationToken ct = default);

    /// <summary>Flip back to the previous side-by-side version.</summary>
    Task<IErrorsInfo> RollbackAsync(CancellationToken ct = default);

    UpdateSettings Settings { get; }        // feed URL, channel, mode, opt-out
    event EventHandler<UpdateCheckResult>? UpdateAvailable;
}
```

plus the feed POCOs (`UpdateFeed`, `AppReleaseInfo`, `ArtifactRef`, `ModuleRef`,
`UpdateCheckResult`, `UpdateSettings`). Naming note: BeepDM already has a
`Pipelines.Models.ReleaseManifest` (a data-pipeline release-evidence bundle,
`ReleaseManifest.cs:11`) — an unrelated domain, so the update POCOs deliberately avoid
"ReleaseManifest"/"Release" as bare names. Conventions: `IErrorsInfo` results,
`IProgress<PassedArgs>` progress, async-with-`CancellationToken` throughout — this domain is
born cancellable rather than inheriting the sync-over-async debt.

**Implementation — `DataManagementEngineStandard/Updates/`**: `AppUpdateService` composed of
`UpdateFeedClient`, `DeltaPlanner`, `SideBySideApplier`, `ModuleUpdater` (over the existing
`IAssemblyHandler`), `UpdatePolicy`. Registered via `AddBeepAppUpdates(settings)` alongside
`AddBeepForDesktop()`; `AppUpdateService` records applied versions through the existing
`IVersionManagementService.RecordAppVersion` (`Services/AppMap/IVersionManagementService.cs:26`)
so update history lands in the same ledger as deployments.

**What Beep.Installer contributes** (the only installer-side pieces):
- the publisher (`/PUBLISHFEED`, §"Publisher side" below), and
- provisioning: the build stamps `update-settings.json` (feed URL, channel, `UpdateMode`,
  from the existing `AppUpdatesURL`/`AppUpdateMode` project fields — currently decorative)
  next to the app, which `AddBeepAppUpdates()` reads by default. An optional tiny
  `Updater.exe` companion handles the case where the app itself cannot flip its own junction.

Component notes:

- **`UpdateFeedClient`** — fetch + parse + hash-verify feed; async from day one (this is also
  what retires the `UpdateChecker`/`UpdateApplier` sync-over-async exemptions in the
  silent-failure guard; the ClickOnce pair then delegates here or is deleted).
- **`DeltaPlanner`** — input: remote `_payload-manifest.json` + local install manifest;
  output: blobs to download, files to rewrite/delete, estimated bytes. Pure and unit-testable.
- **`UpdateApplier`** — download blobs to a staging dir, verify each hash, then apply using
  the **side-by-side layout** from the installer-service plan (`01-phases.md:206-212`):
  materialize `app-1.2.3/` next to `app-1.2.2/`, flip the `current` junction, delete the old
  version after N successful launches. Rollback = flip the junction back — this supersedes
  the in-place `Packaging/ClickOnce/UpdateApplier` swap. Locked-file problem disappears
  because the running version is never written to.
- **`ModuleUpdater`** — thin orchestration over the existing NuGet `UpdateService`:
  `GetPackagesWithUpdatesAsync` for the installed inventory, intersect with `feed.modules`
  (feed-pinned versions win over "latest"), `UpdateAsync(id, pinnedVersion)` per stale module,
  then sha256-verify the fetched package. Live reload of an updated module is **not** promised in v1: default is
  "applies on next launch". (BeepDM's `SharedContextAssemblyHandler` uses
  `AssemblyLoadContext`, so hot reload is *possible* later, but unloading collectible ALCs
  reliably is its own project — recorded, not scoped.)
- **`UpdatePolicy`** — check-on-launch + every-6h (plan default), `UpdateMode.Required`
  blocks the app until applied; `Optional` notifies. Honour a `BEEP_NO_UPDATE` env/setting
  for managed environments.

### Publisher side (build output)

`/BUILD` already produces the solid payload. Add `/PUBLISHFEED=<dir>`: writes the versioned
folder (`1.2.3/_blobs/`, `_payload-manifest.json`, full Setup.exe) and updates `feed.json`
atomically. Publishing is then "copy a folder to your host" — nothing more. Module packages
are pushed to whatever NuGet feed D10 selects (folder feed, GitHub Packages, Azure
Artifacts, or a private BaGet container — all speak the same v3 protocol
`NuGetPackageManager` already consumes).

**Hard rule learned this project:** never republish a version in place. The 3.1.1 stale-cache
incident (P0) is exactly what feed immutability prevents — new content ⇒ new version, always.

## 3. Server question, decided as D10

| Option | What it is | Enough for | Not enough for |
|---|---|---|---|
| **A (recommended start)** | Static HTTPS host / GitHub Releases / S3 / LAN folder | feeds, full + delta payloads, module NuGet feed, `Required` updates | staged rollout %, auth-gated downloads, telemetry |
| B | A, plus a tiny read-only API (single container) | + staged rollout, per-license channels, download stats | — |
| C | Full update service (accounts, delta-on-demand) | everything | overkill until there is a fleet to justify it |

The client only ever sees URLs + hashes, so A→B→C is a hosting evolution, not a client change.

If B is ever chosen, BeepDM already holds the governance primitive for it:
`IDeploymentMetadataService` and its approval-token records
(`Studio/Deployment/IDeploymentMetadataService.cs:22,61-79`) — the same HMAC approval-token
flow the installer-service plan prescribes for Staging/Live promotions — can gate a
`Required` update behind an issued token rather than inventing a new auth mechanism.

## 4. Files to change

| Action | File | Risk |
|--------|------|------|
| New | `BeepDM/DataManagementModelsStandard/Updates/{IAppUpdateService,FeedModels,UpdateSettings,UpdateCheckResult}.cs` | low |
| New | `BeepDM/DataManagementEngineStandard/Updates/{AppUpdateService,UpdateFeedClient,DeltaPlanner,SideBySideApplier,ModuleUpdater,UpdatePolicy}.cs` + `AddBeepAppUpdates` registration | medium |
| New | BeepDM tests for the Updates domain (extends the P2 precedent of first installer-domain coverage) | — |
| Modify | `Beep.Installer.Core/Build/BuildPipeline.cs` (+`/PUBLISHFEED` stage; stamp `update-settings.json` from `AppUpdatesURL`/`AppUpdateMode`) | medium |
| Modify | `Beep.Installer/Program.cs` (`/CHECKUPDATE`, `/UPDATE` verbs — thin wrappers over `IAppUpdateService`) | low |
| Retire | `Beep.Installer.Core/Packaging/ClickOnce/{UpdateChecker,UpdateApplier}` → delegate to BeepDM Updates or delete (removes the two guard exemptions) | medium |
| Tests | DeltaPlanner (pure), feed round-trip, junction flip, module compare | — |

## 5. Verification

```
/BUILD v1.0 /PUBLISHFEED=feed → install → /BUILD v1.1 /PUBLISHFEED=feed
installed app /CHECKUPDATE          # sees 1.1, plans delta, reports bytes saved vs full
/UPDATE                             # side-by-side dir appears, junction flips, old kept
corrupt one blob on the "server"    # hash mismatch → abort, junction untouched
module bump only (feed.modules)     # ModuleUpdater updates one package; app files untouched
kill mid-update                     # relaunch → old version still runs (junction never flipped)
```

## 6. Out of scope

Hot reload of updated modules; binary diffs *within* a file (bsdiff/zstd patches — blob-level
dedup first, measure, then decide); auto-update of the updater itself; Store/winget channels.

## 7. Sub-task execution order

1. **11.A.1** Feed contract POCOs + `UpdateFeedClient` (async, hash-verified). Gate: feed round-trip test.
2. **11.A.2** `/PUBLISHFEED` publisher stage. Gate: two published versions produce a valid feed + blob store.
3. **11.B.1** `DeltaPlanner`. Gate: plan for v1.0→v1.1 lists exactly the changed blobs.
4. **11.B.2** Side-by-side `UpdateApplier` + junction flip + crash-safety. Gate: kill-mid-update test.
5. **11.C.1** `ModuleUpdater` over `IAssemblyHandler`. Gate: single-module update leaves app files untouched.
6. **11.C.2** `UpdatePolicy` + `/CHECKUPDATE` `/UPDATE` verbs; retire ClickOnce updater pair. Gate: guard exemptions removed.
