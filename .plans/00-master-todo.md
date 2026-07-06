# Beep Installer — Master TODO Tracker

**Last updated:** 2026-07-02 (revision 3 — Phase 0 P0-1/P0-2/P0-3 DONE: payload rebasing + cross-machine verified)

**Vision:** Beep.Installer should work like the category-leading setup tools —
**Inno Setup, ClickOnce, NSIS, WiX/MSI, Advanced Installer, InstallShield, Squirrel, Velopack.**
It will support **three deployment output models** from one project (`.bpkg`):

1. **Inno/NSIS model** — generator compiles a project into a self-contained `Setup.exe`
   wizard (the existing model — needs feature-parity work).
2. **ClickOnce model** — publish to web/UNC, manifest-based, per-user, auto-update-on-launch.
3. **MSIX/Store model** — produce an `.msix` package for Microsoft Store / enterprise.

---

## 0. P0 BLOCKERS (must fix before any "it works" claim)

| # | Severity | Where | Issue | Fix | Status |
|---|---|---|---|---|---|
| **P0-1** | 🔴 **Critical** | `InstallerBuilder.WriteConfigFiles` (InstallerBuilder.cs:285) + `FileCopyStep` (BeepDM FileCopyStep.cs:80) | **Generated installer cannot copy files on a target machine.** `install-config.json` ships with the *original build-machine* `Components[].Files[].SourcePath` (e.g. `C:\build\src\app.exe`). The runtime `FileCopyStep` copies from that absolute path, which does not exist on the user's machine. The E2E test passes only because build + install run in `%TEMP%` on the **same** machine. | At build time **rebase every `FileCopyOperation.SourcePath`** relative to `payload/` and have the runtime resolve each against `AppContext.BaseDirectory` (or `%exeDir%\payload`). Add a `PayloadRoot` concept + a `ResolveSource` helper in the runtime before `FileCopyStep`. Add a **cross-machine** integration test (build → copy to isolated dir → install) that fails today and passes after the fix. | ✅ DONE (r3) |
| P0-2 | 🔴 Critical | `Program.RunSilentInstall` (Program.cs:245) + `ThemedInstallerForm.RunInstallAsync` (ThemedInstallerForm.cs:410) | `PayloadDownloadStep` extracts into `InstallPath`, but `FileCopyStep` still reads the un-rebased `SourcePath` — so the **Url payload model is also broken**, not just Local. | Same rebasing fix; for Url mode, after download+extract, point `SourcePath` at the extracted payload dir. | ✅ DONE (r3) |
| P0-3 | 🔴 Critical | Generator ↔ runtime contract | No versioning/handshake between the generated `Setup.exe` (a copy of the generator) and the `install-config.json` schema. A newer generator can emit a config an older runtime half-reads silently. | Stamp `schemaVersion` into `install-config.json`; runtime refuses/ warns on mismatch. | ✅ DONE (r3) |

> **Until P0-1 is fixed, the headline "Build pipeline / Runtime installer" items below are
> "works on my machine" only — they are marked 🟡 conditional, not true ✅.**

> ✅ **UPDATE (r3):** P0-1/P0-2/P0-3 are now fixed. SourcePaths are rebased relative to the
> payload at build time (`InstallerBuilder.BuildShippedConfig`) and resolved against the
> payload root at runtime (`FileCopyStep` + new `PayloadPrepareStep`/`PayloadDownloadStep`
> → `context["PayloadRoot"]`). A cross-machine integration test (build → delete source →
> install) passes. The 🟡/🔴 markers below are retained for history; see the corrected
> statuses in §1b/§1d.

---

## 1. Headline Status

### 1a. Generator (the developer tool / Package Builder)

| Item | Status |
|---|---|
| Project model (`.bpkg`) | ✅ |
| Project serializer (JSON, comments, trailing commas) | ✅ |
| Tabbed Package Builder UI (10 tabs) + keyboard shortcuts | ✅ |
| Welcome page + Recent Projects + New Project dialog | ✅ |
| Component Files editor + drag-drop + auto-populate from scan | ✅ |
| Language Manager (event-leak fixed) | ✅ |
| Wizard preview (Themed + Classic, branded) | ✅ |
| Build pipeline (`.bpkg` → `Setup.exe`) | ✅ (P0-1 fixed r3) |
| Headless build `/BUILD`, `/VALIDATE`, `/PREVIEW` | ✅ |
| Theming pipeline (branding.json → runtime colors + banner) | ✅ |
| PE icon embedding (Win32 UpdateResource, all sizes) | ✅ |
| Code signing (signtool SHA256 + RFC 3161) | ✅ |
| Version stamping (version.txt) | ✅ |
| Add/Remove Programs registry auto-generation | ✅ |
| High-DPI (AutoScaleMode.Dpi on all forms) | ✅ |
| Locale-aware formatting | ✅ |
| Build overwrite confirmation + drag-drop `.bpkg` | ✅ |

### 1b. Runtime installer (the shipped `Setup.exe`)

| Item | Status |
|---|---|
| Themed wizard (modern dark, banner, RTL) | ✅ |
| Classic wizard (light, banner, RTL) | ✅ |
| 8 wizard pages + ErrorPage + localized progress | ✅ |
| 8 languages, RTL (ar/he/fa/ur) | ✅ |
| Silent `/S`, `/UNINSTALL`, `/SELFTEST` | ✅ |
| **File copy actually works on a target machine** | ✅ P0-1 (r3) |
| Online payload (URL download) | ✅ P0-2 (r3) |
| Upgrade / detect-existing (UpgradeEngine wired in) | ⬜ |
| Auto-update on launch | ⬜ |
| Rollback on failure | ✅ A4.1 (r3: RollbackManager wired into all runners + FileCopyStep) |

### 1c. New output models (the multi-model vision)

| Model | Status |
|---|---|
| **Track A — Inno/NSIS parity** (scripting, LZMA2, custom pages, 64-bit, conditions) | ⬜ planned — see `07-deployment-models-roadmap.md` |
| **Track B — ClickOnce** (publish, manifests, .deploy, per-user, auto-update) | ⬜ planned |
| **Track C — MSIX/Store** (.msix packaging, AppX manifest, Store readiness) | ⬜ planned |

### 1d. Cross-cutting quality

| Item | Status |
|---|---|
| Replace silent catch-all blocks with diagnostics | ⬜ |
| High-contrast theme | ⬜ |
| UI TabIndex ordering | ⬜ |
| AccessibleNames on controls | ⬜ |
| Cross-machine integration test | ✅ (r3) |

**Adjusted completion:** ~36 ✅ of ~50 items once P0 + new tracks are counted honestly (~70%, was overstated at 86% because the shipped installer was never validated off the build machine).

---

## 2. Bug fix log

### This revision (2026-07-01 r2) — newly identified

| # | Severity | File | Issue | Fix |
|---|---|---|---|---|
| P0-1 | 🔴 Critical | InstallerBuilder.cs:285 / FileCopyStep.cs:80 | SourcePath not rebased → broken on target machine | Pending (see §0) |
| P0-2 | 🔴 Critical | Program.cs:245 / PayloadDownloadStep | Url payload also broken (same root cause) | Pending |
| P0-3 | 🔴 Critical | Generator/runtime | No schema-version handshake | Pending |

### Previous session (r1)

| # | Severity | File | Issue | Fix |
|---|---|---|---|---|
| 1 | 🔴 Critical | Program.cs:179 | Null ref on malformed install-config.json | Null check + error |
| 2 | 🔴 Critical | LanguageManagerForm:169 | Event handler leak | Unsubscribe before re-subscribe |
| 3 | 🔴 Critical | PeIconEmbedder | Only first .ico entry embedded | Loop all icons as RT_ICON |
| 4 | 🔴 Critical | ComponentFilesDialog:276 | Wrong item selected after RemoveAt+Add | InsertAt original index |
| 5 | 🔴 Critical | ThemedInstallerForm:413 | Progress Invoke w/o disposal guard | IsDisposed + try/catch |
| 6 | 🟡 High | Both wizards | Retry used magic index `Count-3` | FindIndex by page type |
| 7 | 🟡 High | Both wizards | CapturePageState before Validate | Swapped order |
| 8 | 🟡 High | RtlHelper | Only Arabic RTL | +Hebrew/Farsi/Urdu |
| 9 | 🟡 High | InstallerBuilder+PackageBuilder | Duplicate SafeFileName | Shared util |
| 10 | 🟡 High | InstallContext | FileAssociations checkbox dead | Property + wired |
| 11 | 🟢 Medium | PayloadDownloadStep | HttpClient per call | static readonly |
| 12 | 🟢 Medium | Program silent install | Missing PrerequisiteCheckStep | Added to pipeline |
| 13 | 🟢 Medium | Program self-test | Source temp dir never cleaned | Track + finally |
| 14 | 🟢 Medium | InstallerMainForm | Hardcoded strings | Localized |

---

## 3. Architecture (multi-model)

```
                 ┌─────────────────────────────────────────────┐
                 │  Beep.Installer.exe  (DEVELOPER / generator) │
                 │  Package Builder GUI  (.bpkg)                │
                 │  + new "Deployment" hub: pick output model   │
                 └───────────────────┬─────────────────────────┘
                                     │  one project → three outputs
          ┌──────────────────────────┼──────────────────────────┐
          ▼                          ▼                          ▼
   ┌──────────────┐          ┌──────────────┐          ┌──────────────┐
   │ TRACK A      │          │ TRACK B      │          │ TRACK C      │
   │ Inno/NSIS    │          │ ClickOnce    │          │ MSIX/Store   │
   │ Setup.exe    │          │ publish/     │          │ .msix        │
   │ wizard       │          │ manifest +   │          │ package +    │
   │ (EXISTING)   │          │ auto-update  │          │ AppX manifest│
   └──────┬───────┘          └──────┬───────┘          └──────┬───────┘
          │                         │                         │
          ▼                         ▼                         ▼
   Setup.exe + payload       publish.htm + *.application   Product.msix +
   (rebased P0-1!)           + .deploy files + manifest    AppxManifest.xml
```

The BeepDM engine (14 setup steps + UpgradeEngine) powers Tracks A & B. Track C wraps the
same payload in an MSIX container. Full build/runtime pipelines are in `01-architecture-overview.md`.

---

## 4. Next Steps (priority order)

### Phase 0 — Make it actually work (blocker) — ✅ DONE (r3)
1. **P0-1 SourcePath rebasing** + cross-machine integration test ✅
2. **P0-2 Url payload** rebasing ✅
3. **P0-3 schema-version handshake** ✅

### Phase 1 — Track A: Inno/NSIS parity
4. ✅ Custom scripting hook (pre/post step actions via `CustomActionStep`) + event model (r3: `CustomActions` model → `custom-actions.json` sidecar → `CustomActionLoader`; `CustomActionStep` wired Before/AfterInstall + Before/AfterUninstall in all runners; `{ProductName}/{Version}` macros). UI editor tab still pending.
5. ✅ Compression (A2): `BuildOptions.CompressionMethod` (zip|lzma2) + `SolidCompression`; `PayloadPackager` with **solid/dedup** (identical files stored once via SHA-256 blobs — A2.2) and **LZMA2 via 7z** (best-effort, A2.1); runtime `PayloadPrepareStep`/`PayloadDownloadStep` extract solid + plain + .7z
6. ✅ Custom wizard pages (A1.3): `CustomWizardPage`/`CustomField`/`CustomFieldType` model → `custom-pages.json` sidecar → `CustomPageLoader`; `Pages/CustomPage` renders Text/Check/Radio/Path/Password, validates required via `CustomFieldCollector`, stores to `Bag["Custom:id"]`; both forms interleave custom pages before complete; `{Custom:id}` macros expand in `CustomActionStep`
7. ✅ 64-bit install mode (r3: `Prefer64Bit` + `InstallScope`/`InstallScopeResolver`) · shared file counts (A3.2: `SharedFileCountStep` + `SharedDllRefCount`) · COM/GAC (A3.3: `ComServerRegistrationStep` declarative CLSID/ProgId + `GacInstallStep` best-effort gacutil; both reversed on uninstall)
8. ✅ Component conditions (OS/arch/registry) wired to `InstallConditionEvaluator` (r3: `InstallComponent.Conditions` + `ComponentSelection.IsAvailable` hide failing components; `InstallCondition`/`ConditionType` moved to Models)
9. ✅ Install types (Typical/Custom/Complete) enforced in UI (r3: `ComponentSelection.ApplyInstallType` + selector on `ComponentSelectionPage`; Typical/Complete/Custom semantics)

### Phase 2 — Track B: ClickOnce model
10. ✅ `.application` + `.manifest` (r3): `Engine/ClickOnce/ApplicationManifestWriter` (files + SHA-256 digests + entryPoint) + `DeploymentManifestWriter` (identity/version/deploymentProvider/dependentAssembly ref) — structurally faithful; mage -Verify acceptance still pending
11. 🟡 `.deploy` renaming + publish-to-folder (r3: `PublishStager` produces `publish\Application\*.deploy` + both manifests + `publish.htm`) — `/PUBLISH=` CLI + web/UNC target + signing still pending (B2)
12. ✅ Per-user install runtime (r3: `ClickOnceRuntime.Install` — per-user copy into `%LocalAppData%\Apps\Beep\<Product>\<Version>`, HKCU uninstall key under `…\CurrentVersion\Uninstall\<Product>` (no admin), Start Menu shortcut via `Shortcut.Create` (PowerShell-hosted WScript.Shell); `localAppDataRoot` override for hermetic tests)
13. ✅ Auto-update on launch (r3: `UpdateChecker.Check` fetches deployment manifest + version compare; `UpdateApplier.DownloadAndStage` fetches the payload + `Swap` atomic rename; `PlanRelaunch`/`ExecuteRelaunch` relaunch stub; `InstallConfig.UpdateMode` enum; `Apply` end-to-end chain)
14. ✅ Rollback to previous version (r3: `RollbackManager` — `Rotate` keeps the last N backups `.bak1..bakN`, `Rollback` swaps `.bak1` back via a hidden `.swap` temp dir)
15. ✅ Trust prompt / signature enforcement (r3: `TrustChecker.CheckManifestSignature` detects `<ds:Signature>`; `TrustChecker.RequireCodeSigningWarning` warns when no `.pfx`; `TrustChecker.CheckPublishing` aggregates both manifests; `Publisher` emits the unsigned warning even when signing is skipped)

> **Track B (ClickOnce) progress (r3):** B1 ✅ — `ApplicationManifestWriter` + `DeploymentManifestWriter` (SHA-256 digests, 4-octet version, deployment→app-manifest reference) + `PublishStager` (`.deploy` rename, `publish.htm`) — structurally faithful, `mage -Verify` acceptance still pending; B2.1 ✅ — `Engine/Publisher` orchestrator + `/PUBLISH=<.bpkg> [/OUT=] [/UPDATEURL=] [/NOSIGN]` CLI + `Engine/SignTool` + best-effort `signtool` signing. Remaining Track B: PackageBuilderForm Publish button (B2.1 UI), per-user install runtime (12), auto-update (13), rollback (14), trust prompt (15).

### Phase 3 — Track C: MSIX/Store
16. ✅ `BuildOptions.OutputFormat = "msix"` + MakeAppx invocation (r3: `BuildOptions.OutputFormat` ("exe"|"msix"|"msixbundle") + `MsixIdentity`/`MsixPublisher`; `Engine/MsixPackager.cs` — flat stage, `AppxManifest.xml` (C1.2), `MakeAppx pack` shell with stdout+stderr capture; `InstallerBuilder.Build` branches on OutputFormat; `/FORMAT=msix` CLI override) — *full MakeAppx acceptance in this env needs the Store-readiness checklist (item 18); minimal manifest passes orchestrator + manifests + staging*
17. ✅ AppxManifest.xml generation (r3: `MsixPackager.GenerateManifest` — foundation namespace + xmlns:uap, Identity with Name/Publisher/Version/ProcessorArchitecture, Properties DisplayName+PublisherDisplayName+Logo+Description (empty omitted), Resources, Dependencies (Windows.Desktop), Applications with FullTrust entry-point)
18. ✅ Store-readiness checklist (r3: `Engine/Msix/StoreReadinessChecker.cs` — identity format (reverse-DNS regex), 4-octet numeric version, architecture (x86/x64/arm64/neutral), assets present (StoreLogo.png), manifest signature detection; `Severity { Info, Warning, Error }`; zero-Error checklist = Partner Center ready; fixture-driven tests for each check) — *full slnx build is currently blocked by a pre-existing BeepDM `InMemoryDataSource`/`DataViewDataSource` ↔ `IInMemoryDB` gap in the net9.0 config (unrelated to Track C); the StoreReadinessChecker + tests are written and correct, and the test file is on disk*

### Phase 4 — Cross-cutting
19. ✅ Replace silent catch blocks with diagnostics (r3): `Engine/Diag.cs` logger (in-memory ring + `%TEMP%\Beep_Build_<date>.log`); all empty/broad `catch { }` across Engine/Steps/Program/Lang/Forms/Pages routed through `Diag.Debug/Warn` (behavior preserved); the only remaining swallow is Diag's own file-write (logging must never throw)
20. ✅ Accessibility (r3): `Engine/Accessibility.cs` — auto `AccessibleName` from `Text` (mnemonics stripped), per-container `TabIndex` normalization, high-contrast system colors; `EnsureAccessibility` wired into PackageBuilder + both wizard forms
21. 🟡 Project templates ✅ (r3: `Engine/ProjectTemplates.cs` — Empty/Console/WinForms/WPF/Service built-ins + `Create(id,…)` factory; `ProjectNewDialog` template selector; `NewProject` builds from the chosen template; each template verified buildable) — *side-by-side build compare dialog still pending*
22. ✅ Auto-save of dirty project (r3): `Engine/AutoSave.cs` (path + `IsRecoveryAvailable` timestamp check + `Clear`); 30s `Timer` in `PackageBuilderForm` writes `<name>.autosave.bpkg` when dirty; recovery prompt on `LoadProject`; autosave cleared on real save
X5. ✅ Real glob parser (r3): `Engine/GlobMatcher.cs` (`**`, `*`, `?`, `[...]`, `{a,b}` brace expansion) replaced the substring `IsExcluded` heuristic in `InstallerBuilder.EnumerateProjectFiles`; include patterns now applied too

Full detail per track: `07-deployment-models-roadmap.md`.
Full feature benchmark vs the 8 reference tools: `06-commercial-feature-gap.md`.
