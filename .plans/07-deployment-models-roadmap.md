# Deployment Models Roadmap — Tracks A / B / C (detailed)

**Last updated:** 2026-07-01

One `.bpkg` → three output shapes. See `00-master-todo.md` for status and `06-commercial-feature-gap.md`
for the benchmark matrix. This doc is the **engineering spec per phase** — data model changes,
exact files, API signatures, algorithm/logic, tests, and acceptance criteria.

A new top-level `Deployment` section on `InstallProject` selects which tracks to build:

```jsonc
"deployment": {
  "tracks": [ "Setup", "ClickOnce", "Msix" ],   // any subset
  "publishTarget": "",        // Track B: folder / URL / UNC
  "updateUrl": "",            // Track B: auto-update source
  "msixIdentity": "",         // Track C: PackageIdentity (e.g. "CN=...")
  "msixPublisherDisplayName": "", // Track C: publisher display
  "storeAssociation": ""      // Track C: optional Store association file
}
```

Each phase below uses a consistent template:
**Goal → Data model → Files → Implementation → Tests → Acceptance → Risks.**

---
---

# Phase 0 — Make Track A actually work (BLOCKER)

> Nothing else matters until the shipped installer copies files on a real machine.

## Goal
A `Setup.exe` built on machine X must install correctly on machine Y, with **no access** to X's
file system. Today this fails because `install-config.json` ships build-machine-absolute
`SourcePath`s that the runtime `FileCopyStep` reads verbatim.

## Root cause (confirmed)
- `Engine/InstallerBuilder.cs` `WriteConfigFiles` (line ~285) serializes `project.InstallConfig`
  **as-is**; `Components[].Files[].SourcePath` still holds values like `C:\src\app.exe`.
- `StagePayload` copies files into `<out>\payload\<rel>` but never records/updates the source path.
- BeepDM `FileCopyStep.Execute` (FileCopyStep.cs:80) does `File.Copy(op.SourcePath, destPath)`.
- `EndToEndTests.FullCycle_*` passes only because build + install share the same `%TEMP%` machine.

## Data model changes
- `Models/InstallProject.cs` → `InstallProject`:
  - add `public int ConfigSchemaVersion { get; set; } = 1;`
- The serialized `install-config.json` gains a top-level `"schemaVersion": 1` (write side) and the
  loader validates it (read side). `InstallConfig` itself (in BeepDM) is **not** modified — the
  version is written/read as a sibling JSON property in the same file via a wrapper type.

## New files
1. `Engine/PayloadPathResolver.cs`
   ```csharp
   public static class PayloadPathResolver
   {
       /// <summary>Absolute path to the payload dir next to the exe.</summary>
       public static string LocatePayloadRoot(string exeDir, string payloadFolderName = "payload");

       /// <summary>True if a payload (folder or zip) exists beside the exe.</summary>
       public static bool HasPayload(string exeDir, string payloadFolderName = "payload");

       /// <summary>Returns a CLONE of config with every File SourcePath resolved to an
       /// absolute path under payloadRoot. Handles relative, missing-abs, and already-abs.</summary>
       public static InstallConfig Resolve(InstallConfig config, string payloadRoot);

       /// <summary>If only payload.zip exists, extract it once to <exeDir>\payload and return that.</summary>
       public static string EnsureExtracted(string exeDir, string payloadFolderName = "payload");
   }
   ```
   Resolution rules (per `FileCopyOperation op`), tried in order:
   1. If `Path.IsPathRooted(op.SourcePath)` and `File.Exists(op.SourcePath)` → leave as-is.
   2. Else `Path.Combine(payloadRoot, op.SourcePath)` if exists → use it.
   3. Else `Path.Combine(payloadRoot, op.DestinationPath)` if exists → use it (DestinationPath is
      always payload-relative after rebasing).
   4. Else `Path.Combine(payloadRoot, Path.GetFileName(op.SourcePath))`.
   5. Else mark the file missing → returned via an out-param `List<string> unresolved` so the caller
      can fail loudly instead of silently skipping.

2. `Engine/ConfigFile.cs` — wrapper to carry the schema version alongside InstallConfig at runtime:
   ```csharp
   public class ConfigFile
   {
       public int SchemaVersion { get; set; } = 1;
       public InstallConfig Config { get; set; } = new();
       public const int CurrentSchemaVersion = 1;
   }
   ```

## Modified files
- `Engine/InstallerBuilder.cs`
  - `StagePayload`: while enumerating, build `Dictionary<string,string> originalToRelative`
    (`srcFile` → `rel`). Return it (out param) so `WriteConfigFiles` can rebase.
  - `WriteConfigFiles`: **clone** `project.InstallConfig` (deep, per-component/per-file); for each
    `FileCopyOperation` set `SourcePath = originalToRelative[oldAbs]` (payload-relative). Serialize
    the **clone** (not the original project object) into `install-config.json` with `schemaVersion`.
  - Keep the *project* `.bpkg` copy (line ~307) serializing the **original** absolute paths — that's
    the developer's source of truth and must round-trip for re-builds.
- `Engine/ProjectSerializer.cs`
  - `Load`/`Save`: round-trip `ConfigSchemaVersion`; warn if newer than `ConfigFile.CurrentSchemaVersion`.
- `Engine/ConfigManager` is in BeepDM — to avoid touching it, the Beep.Installer side reads
  `install-config.json` via a new `Engine/RuntimeConfigLoader.cs` that returns `(InstallConfig, int schema, string? err)`
  and performs the version handshake.
- Runtime callers (3 places — must all resolve before building the wizard):
  - `Program.RunSilentInstall`
  - `Program.RunUninstall`
  - `Program.RunSelfTest` (unchanged — uses synthetic abs paths)
  - `Forms/ThemedInstallerForm.RunInstallAsync` (line ~396)
  - `Forms/InstallerWizardForm.RunInstall` (line ~387)
  - At each: `var root = PayloadPathResolver.EnsureExtracted(exeDir); var cfg = PayloadPathResolver.Resolve(_ctx.Config, root);`
    then put `cfg` (not `_ctx.Config`) into the `SetupContext`.
- `Steps/PayloadDownloadStep.cs`
  - After `ZipFile.ExtractToDirectory(tempZip, installPath)`, set
    `context.Properties["PayloadExtractedRoot"] = installPath`. The resolver, when it sees this flag,
    uses `installPath` as the payload root instead of `<exeDir>\payload`.

## Tests
- New `Beep.Installer.Tests/PayloadPathResolverTests.cs`:
  - `Resolve_RelativeSourcePath_CombinesWithPayloadRoot`
  - `Resolve_AbsoluteMissingSource_FallsBackToDestinationPath`
  - `Resolve_ReturnsUnresolvedList_WhenNothingMatches`
  - `EnsureExtracted_ExtractsZip_WhenFolderMissing` (writes payload.zip, expects extraction)
  - `EnsureExtracted_IsIdempotent` (second call does not re-extract)
- New `Beep.Installer.Tests/ConfigSchemaTests.cs`:
  - `Loader_Rejects_UnknownFutureSchema` (writes schemaVersion=999 → returns error)
  - `Loader_Accepts_CurrentSchema`
- **Harden** `EndToEndTests.FullCycle_BuildThenSilentInstallUninstall`:
  - After `/BUILD`, **copy** the build output dir to `_tempRoot\isolated`, then **delete** the
    original source tree (`srcDir`) and the original build dir. Then run `/S /D=…` against the
    isolated copy. This is the cross-machine regression guard — it must fail before Phase 0 and
    pass after. Add `FullCycle_CrossMachineInstall` as the canonical name.

## Acceptance criteria
1. `install-config.json` in build output contains **no rooted** `SourcePath` (grep test).
2. `/BUILD` then `/S` against a relocated copy of the output succeeds; installed files match.
3. `PayloadPathResolver` resolves 100% of component files; missing files produce a clear error page,
   not a silent skip.
4. A future/unknown `schemaVersion` is rejected with a clear message, exit code 2.
5. All existing 38 tests still pass; new tests add ≥7 green.

## Risks / notes
- **Idempotent extraction**: if `payload.zip` changes between runs (debug loops), detect via a
  `.extracted` marker file storing the zip's last-write-time/length; re-extract on mismatch.
- **Path separator portability**: store rebased paths with `/` or `Path.DirectorySeparatorChar`
  consistently; resolver must normalize both.
- Do **not** mutate the in-memory `InstallProject` during build (would corrupt the open editor);
  always clone before rebasing.

---
---

# Track A — Inno / NSIS parity (`Setup.exe` wizard)

Goal: close the gaps in `06-commercial-feature-gap.md` §1 so the wizard matches Inno/NSIS for
real-world packaging.

## A1 — Scripting & customization (biggest perceived gap)

### A1.1 Expose CustomActionStep in the generator
- **Goal:** let a developer attach "run this exe/script at step X" without hand-editing JSON.
- **Data model:** extend `InstallConfig` usage (no model change — the step already exists in BeepDM).
  Add a UI-side DTO in `Models/CustomActionDefinition.cs`:
  ```csharp
  public class CustomActionDefinition
  {
      public string Id { get; set; } = "";
      public string Name { get; set; } = "";
      public string Target { get; set; } = "";          // exe/bat/ps1 path (may be {InstallPath}\x.exe)
      public string Arguments { get; set; } = "";
      public string WorkingDirectory { get; set; } = ""; // {InstallPath} macro
      public CustomActionStage Stage { get; set; }       // PreInstall, PostInstall, PreUninstall
      public bool WaitForExit { get; set; } = true;
      public bool HideWindow { get; set; } = true;
      public string Condition { get; set; } = "";        // expression or empty
      public int TimeoutMs { get; set; } = 0;            // 0 = infinite
  }
  public enum CustomActionStage { PreInstall, PostInstall, PreUninstall, PostUninstall }
  ```
- **Files:**
  - New `Forms/CustomActionsDialog.cs` (list editor + add/edit, mirroring `ComponentFilesDialog`).
  - `Forms/PackageBuilderForm.cs`: new "Actions" tab; persist into a new
    `InstallProject.CustomActions` list and translate to `CustomActionStep` config at build time.
  - `Engine/InstallerBuilder.cs`: when writing `install-config.json`, emit the custom actions into
    the BeepDM config shape that `CustomActionStep` consumes.
  - Runtime (`Program.RunSilentInstall`, both wizard forms): insert a `CustomActionStep(PreInstall)`
    before `FileCopyStep` and a `CustomActionStep(PostInstall)` before `VerifyInstallStep`.
- **Macros:** support `{InstallPath}`, `{ProductName}`, `{Version}` substituted before launch.
- **Tests:** `CustomActionsTests` — PreInstall blocks install on non-zero exit; PostInstall runs
  after FileCopy; macro substitution; condition skip.
- **Acceptance:** a project with a post-install "register service" action runs it after files copy.

### A1.2 Event model
- **Goal:** canonical hooks instead of ad-hoc stage flags.
- **Data model:** `InstallProject.Events` of type `InstallEvents { List<CustomActionDefinition> BeforeInstall, AfterInstall, BeforeUninstall }`.
- **Files:** thin mapping in `InstallerBuilder` from `Events` → the right `CustomActionStage`.
- **Acceptance:** A1.1 and A1.2 share the same execution path; docs clarify both.

### A1.3 Custom wizard pages
- **Goal:** user-defined pages beyond the 8 built-in (parity with Inno `CreateInputFilePage` etc.).
- **Data model:** `Models/CustomWizardPage.cs`:
  ```csharp
  public class CustomWizardPage
  {
      public string Id { get; set; } = "";
      public string Title { get; set; } = "";
      public string Subtitle { get; set; } = "";
      public int Order { get; set; }                // insert position among built-ins
      public List<CustomField> Fields { get; set; } = new();
  }
  public class CustomField
  {
      public string Id { get; set; } = "";
      public string Label { get; set; } = "";
      public CustomFieldType Type { get; set; }      // Text, Check, Radio, Path, Password
      public string DefaultValue { get; set; } = "";
      public List<string> Options { get; set; } = new(); // for Radio
      public bool Required { get; set; }
      public string DestinationMacro { get; set; } = ""; // e.g. {Custom:myField}
  }
  public enum CustomFieldType { Text, Check, Radio, Path, Password }
  ```
- **Files:**
  - New `Pages/CustomPage.cs` implementing `IInstallerPage` — renders Fields dynamically, validates
    `Required`, stores answers into `InstallContext.Bag["Custom:<id>"]`.
  - `Forms/ThemedInstallerForm.BuildPages` / `InstallerWizardForm`: interleave custom pages by `Order`.
  - New `Forms/CustomPagesDialog.cs` editor + new generator tab.
  - Macro expansion in `InstallerBuilder` so `{Custom:myField}` resolves in Custom Actions / config files.
- **Tests:** `CustomPageTests` — Required validation; answers persisted to context; macro resolves.
- **Acceptance:** a page asking for a license key injects it into an `appsettings.json` at install.

### A1.4 (Stretch) Condition expression evaluator
- **Goal:** `Condition = "OS.Version >= 10.0.19041 AND NOT Exists(HKLM\...\Installed)"`.
- **Files:** new `Engine/ConditionEvaluator.cs` — small recursive-descent parser (AND/OR/NOT,
  comparison, `Exists(reg)`, `OsIs`, `ArchIs`). Reuse BeepDM `InstallConditionEvaluator` where possible.
- **Tests:** parser unit tests + truth table for each operator.
- **Acceptance:** component with `Condition` is auto-(de)selected on the components page.

---

## A2 — Compression

### A2.1 LZMA2 option
- **Data model:** `BuildOptions.CompressionMethod { get; set; } = "zip";` (`"zip" | "lzma2"`).
- **Files:**
  - `Engine/LzmaCompressor.cs` — bundle the [LZMA SDK](https://www.7-zip.org/sdk.html) (public
    domain) `Compress`/`Decompress` classes into the project (no NuGet needed); or shell to `7z.exe`
    if detected on PATH as a fallback.
  - `InstallerBuilder.CompressPayload`: branch on `CompressionMethod`.
  - Runtime: `PayloadPathResolver.EnsureExtracted` must handle `.7z`/LZMA payloads — add LZMA
    decompress path (the SDK decoder).
- **Tests:** round-trip compress→decompress equals original; payload smaller than zip at level 9.
- **Acceptance:** LZMA2 payload ≥30% smaller than zip for typical app payloads.

### A2.2 Solid compression
- **Data model:** `BuildOptions.SolidCompression { get; set; } = true;`
- **Logic:** LZMA SDK `Encode` over a concatenated stream with a small directory header
  (`<nameLen><name><size><data>…`). Decoder reads the directory then extracts per-file.
- **Files:** extend `LzmaCompressor` with `CreateSolid(IEnumerable<(string name,string file)> entries, Stream out)`.
- **Acceptance:** duplicate DLLs across components are stored once.

### A2.3 Single-file self-extracting payload
- **Goal:** embed the compressed payload as a PE resource in `Setup.exe` (no sidecar).
- **Data model:** `BuildOptions.EmbedPayload { get; set; } = false;`
- **Files:** `Engine/PeResourceWriter.cs` (Win32 `UpdateResource` for `RT_RCDATA`); runtime reads the
  embedded resource when no `payload/` folder exists.
- **Acceptance:** a single `Setup.exe` with no sibling files installs the app.

---

## A3 — System integration parity

### A3.1 64-bit install mode
- **Data model:** `InstallConfig.InstallScope { get; set; } = InstallScope.Machine;` and
  `bool Prefer64Bit { get; set; } = true;` (default path resolution +
  `RegistryView.Registry64`/`Registry32`).
- **Files:** FolderPage resolves `ProgramFiles` vs `ProgramFiles(x86)`; RegistryWriteStep/UninstallStep
  pick `RegistryView` accordingly (this logic lives in BeepDM — add an `InstallScope`/`Prefer64Bit`
  read in the step, or pass via context).
- **Tests:** 64-bit build writes to `Program Files` + `HKLM` (non-WOW6432); 32-bit to WOW6432.

### A3.2 Shared file reference counting
- **Goal:** don't remove a DLL still used by another product (MSI `SharedDLLs` parity).
- **Data model:** `FileCopyOperation.SharedCount { get; set; }` (bool/flag).
- **Files:** new step `SharedFileCountStep` (in BeepDM) — increments/decrements
  `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs`; uninstall removes file only when count hits 0.
- **Tests:** two installs of different products sharing a DLL; uninstall first leaves the DLL.

### A3.3 COM / GAC registration
- **Files:** new BeepDM steps `ComRegistrationStep` (write `HKCR\CLSID\{…}`) and `GacInstallStep`
  (use `System.EnterpriseServices.Internal.Publish` to install/uninstall from GAC).
- **Data model:** `ComRegistration { Clsid, ProgId, ThreadingModel, DllPath }`,
  `GacAssembly { Path, StrongName }`.
- **Tests:** regedit entry present after install; `gacutil -l` lists the assembly.

### A3.4 Component conditions in UI
- **Files:** new `Forms/ConditionEditorDialog.cs`; depends on A1.4 evaluator. Per-component
  `InstallComponent.Condition` rendered with a small builder UI.
- **Acceptance:** a component set to `ArchIs = arm64` is hidden on x64 installs.

### A3.5 Enforce InstallationType
- **Files:** `ComponentSelectionPage` honors `IncludedIn` — Typical hides non-Typical; Complete
  checks all; Custom shows all. Add a `Typical/Complete/Custom` selector at the top of the page.
- **Tests:** Typical total size < Complete total size; switching updates selection.

---

## A4 — Robustness wiring

### A4.1 Wire RollbackManager
- **Files:** in both wizard forms' install runner + `Program.RunSilentInstall`, wrap each step:
  before `Execute`, register an `IRollbackAction` (FileCopy registers delete; Registry registers
  delete-value; Shortcut registers delete-link). On any step `Fail`, run the stack in reverse.
- **Tests:** inject a failing step after FileCopy; assert installed file removed + reg key cleared.

### A4.2 Wire UpgradeEngine
- **Files:** new `Steps/UpgradeStep` (PreInstall) that calls
  `UpgradeEngine.DetectExisting → Backup → (on fail) RestoreFromBackup`; runs before FileCopy. After
  install, `UpgradeEngine.RegisterInstall`. Add `BuildOptions.AutoUpgrade { get; set; } = true;`.
- **Tests:** install v1; install v2 over it; assert `.backup` created and user-config migrated;
  failing v2 restores v1.

---
---

# Track B — ClickOnce-style publish + auto-update

Goal: match ClickOnce / Squirrel / Velopack publish-and-update flow (`06-…gap.md` §2).

## B1 — Manifests

### B1.1 Deployment manifest (`.application`)
- **Data model:** `Engine/ClickOnce/DeploymentManifestModel.cs` (Identity name, version, publisher,
  `deploymentProvider` codebase/update URL, `subscription` for required/optional updates).
- **Files:** `Engine/ClickOnce/DeploymentManifestWriter.cs` — emits XML conforming to the
  ClickOnce `assembly`/`assemblyIdentity`/`deployment`/`deploymentProvider` schema.
- **Logic:**
  - Version = `InstallConfig.ProductVersion`.
  - `deploymentProvider codebase` = `Deployment.UpdateUrl` (required for auto-update).
  - Reference to the application manifest by relative path + SHA-256 digest.
- **Tests:** generated XML round-trips through `mage -Verify` (if mage present) or schema-XSD check.

### B1.2 Application manifest (`.manifest`)
- **Files:** `Engine/ClickOnce/ApplicationManifestWriter.cs` — `assemblyIdentity`, `TrustInfo`
  (permission set: FullTrust or partial), `dependency` entries (.NET runtime), file entries with
  `writeableType="applicationData"` and the entry-point flag.
- **Logic:** enumerate the payload; mark the configured main exe as `entryPoint`.
- **Tests:** manifest lists every payload file with correct hash.

### B1.3 `.deploy` renaming + publish.htm
- **Files:** `Engine/ClickOnce/PublishStager.cs`:
  - Copy payload → `publish\Application\`.
  - Rename every file to `*.deploy` (ClickOnce convention; undone on install).
  - Generate `publish.htm` (landing page with install button + prerequisites list).
- **Tests:** every file under `Application\` ends in `.deploy` except the manifests.

## B2 — Publish pipeline

### B2.1 `/PUBLISH=` + generator button
- **CLI:** `Beep.Installer.exe /PUBLISH=<project.bpkg> /TARGET=<dir|url|unc>`.
- **Files:** `Program.RunPublish` dispatch; `Engine/ClickOnce/Publisher.cs` orchestrating
  B1.1→B1.3 + signing (call `Engine/SignTool`/`mage.exe`).
- **UI:** PackageBuilderForm "Publish" button + a new "Publish" tab (target, update URL, trust).
- **Tests:** `/PUBLISH=` produces a complete publish folder; manifests signed.

### B2.2 Per-user install runtime
- **Files:** new `Engine/ClickOnce/ClickOnceRuntime.cs` (bootstrap entry invoked when the app was
  launched from `.\AppRef.exe`): install root = `%LocalAppData%\Apps\Beep\<ProductName>\<hash>\`.
- **Logic:** no elevation; write shortcut under `%AppData%\Microsoft\Windows\Start Menu`; register a
  lightweight uninstall entry under HKCU.
- **Tests:** install path is under `%LocalAppData%`; no UAC prompt (verified by no admin token).

### B2.3 Trust prompt / signature
- **Files:** enforce that manifests are signed; if `BuildOptions.CodeSignCertificatePath` empty,
  emit a **warning** (unsigned ClickOnce triggers the scary red dialog). Document trusted-cert placement.
- **Acceptance:** signed publish installs without the unknown-publisher warning on a cert-installed machine.

## B3 — Auto-update

### B3.1 On-launch update check
- **Files:** `Engine/ClickOnce/UpdateChecker.cs`:
  ```csharp
  public class UpdateInfo { public bool Available; public string RemoteVersion; public string Notes; public Uri UpdateUrl; }
  public static UpdateInfo Check(string updateUrl, string currentVersion); // uses UpgradeEngine.IsNewer
  ```
- **Logic:** fetch `<updateUrl>/MyApp.application`, parse `assemblyIdentity version`, compare.
- **Tests:** mock HTTP server returns newer manifest → `Available=true`.

### B3.2 Download + swap + relaunch
- **Files:** `Engine/ClickOnce/UpdateApplier.cs` — download new payload, stage to
  `<installRoot>\.<version>\`, atomic directory swap, relaunch via a small `updater.exe` stub that
  waits for the main process to exit.
- **Data model:** `Deployment.UpdateMode { Required, Optional }` — Required prompts "must update",
  Optional offers "skip this version".
- **Tests:** simulate v1 running, publish v2 → on next launch the running version becomes v2.

### B3.3 Rollback
- **Files:** keep the last N (default 3) version dirs; `updater.exe` exposes `--rollback` that
  swaps the previous dir back. Wire an "Roll back" entry to the Start Menu group or a `/ROLLBACK` CLI.
- **Acceptance:** after a bad update, `/ROLLBACK` restores the prior version.

### B3.4 (Stretch) Binary-diff delta updates
- **Files:** `Engine/DeltaPatcher.cs` usingbsdiff-style deltas; publish step emits
  `<v1_to_v2>.delta` alongside the full payload; UpdateApplier prefers the delta when the running
  version matches its source.
- **Acceptance:** delta is <30% of full payload for minor updates; applies cleanly.

---
---

# Track C — MSIX / Store

Goal: produce a valid, signed `.msix` for Microsoft Store / enterprise (`06-…gap.md` §3).

## C1 — Packaging

### C1.1 Output format switch
- **Data model:** `BuildOptions.OutputFormat { get; set; } = "exe";` (`"exe" | "msix" | "msixbundle"`).
- **Files:** `Engine/MsixPackager.cs` orchestrator; `InstallerBuilder.Build` branches on `OutputFormat`.
- **CLI:** `/BUILD=project.bpkg /OUT=dir` honors `OutputFormat`; add `/FORMAT=msix` override.

### C1.2 AppxManifest.xml generation
- **Files:** `Engine/Msix/AppxManifestWriter.cs`:
  ```xml
  <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" ...>
    <Identity Name="..." Publisher="CN=..." Version="x.y.z.0" ProcessorArchitecture="x64"/>
    <Properties><DisplayName>..</DisplayName><PublisherDisplayName>..</PublisherDisplayName><Logo>..</Logo></Properties>
    <Resources><Resource Language="en-us"/></Resources>
    <Dependencies><TargetDeviceFamily Name="Windows.Desktop" .../></Dependencies>
    <Applications><Application Id="App" Executable="app.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName=".." Description=".." BackgroundColor=".." Square150x150Logo=".."/>
    </Application></Applications>
    <Capabilities>...</Capabilities>
  </Package>
  ```
- **Logic:** version must be 4-octet (append `.0`); Identity/Publisher from `Deployment.MsixIdentity`.

### C1.3 Visual assets
- **Files:** `Engine/Msix/AssetGenerator.cs` — from `setup.ico` + banner, emit the required tile/scale
  PNGs (`Square150x150`, `Square44x44`, `StoreLogo`, `Wide310x150`, etc.) using `System.Drawing`.
  Place under `Assets\` in the staging dir.
- **Tests:** manifest-referenced assets all exist on disk.

### C1.4 MakeAppx + sign
- **Files:** `Engine/Msix/MsixPackager.cs` shells to `MakeAppx.exe pack /d <stage> /p <out.msix>`
  (found via Windows Kits, like `FindSignTool`). Sign with `signtool sign /fd SHA256 /f <pfx>` — MSIX
  **requires** a trusted cert (document `Import-Pfx` + trust).
- **Tests:** package opens in "App packages" / `Get-AppxPackage` after install.

## C2 — Store readiness

### C2.1 `.appinstaller` for auto-update
- **Files:** `Engine/Msix/AppInstallerWriter.cs` — emits `MyApp.appinstaller` (Version, MainBundle/Package
  uri, UpdateSettings: OnLaunch, HoursBetweenChecks). Overlaps Track B but MSIX-native.
- **Acceptance:** side-loaded MSIX updates automatically from a published `.appinstaller` URL.

### C2.2 Store-readiness checklist
- **Files:** `Engine/Msix/StoreReadinessChecker.cs` returning `List<CheckResult>` — identity format,
  4-octet version, all assets present, arch correct, capabilities justified, signed. UI shows it in
  the new "MSIX" tab; `/BUILD` prints it.
- **Acceptance:** zero-fail checklist → acceptable for Partner Center upload.

### C2.3 Store association (optional)
- **Files:** read optional `Deployment.StoreAssociation` file (from VS Store association) to pin the
  Store identity; pass through to manifest.
- **Acceptance:** produced `.msixupload` matches the reserved Store identity.

---
---

# Cross-cutting (all tracks)

### X1 Replace silent catch-all blocks with diagnostics
- **Files:** new `Engine/Diagnostic.cs` (static `Diag.Warn/Info/Error` → writes to
  `%TEMP%\Beep_Build_<ts>.log` and surfaces in Build Log tab). Sweep all `catch { }` /
  `catch (Exception) { }` in InstallerBuilder, CopyRuntime, BannerLoader, RtlHelper,
  PayloadDownloadStep, PeIconEmbedder — replace with `Diag.Warn(ex)`.
- **Tests:** an exception in staged copy now appears in the build log.
- **Acceptance:** no empty catch blocks remain (`rg "catch\s*(\{|\(Exception\)\s*\{)"` returns 0 in product code).

### X2 Accessibility
- **Files:** every Form gets a `SetupAccessibility()` pass: `TabIndex` order, `AccessibleName` on
  interactive controls, a `HighContrastTheme` path in `ThemeLoader` reading
  `SystemInformation.HighContrast`. Verify with Narrator.
- **Acceptance:** all buttons/inputs reachable & labeled via screen reader; high-contrast legible.

### X3 Project templates + build comparison
- **Files:** `Engine/Templates/` with `.bpkg` templates (Console, WinForms, WPF, Service);
  `Forms/TemplatesDialog.cs`. `Forms/CompareDialog.cs` diffs two build summaries side-by-side.
- **Acceptance:** "New from Template" creates a working project in one click.

### X4 Auto-save
- **Files:** `Forms/PackageBuilderForm` 30s `Timer` that saves to `<project>.autosave.bpkg` when dirty;
  on open, offer recovery if autosave is newer.
- **Acceptance:** killing the process mid-edit loses <30s of work.

### X5 Real glob parser
- **Files:** `Engine/GlobMatcher.cs` (brace expansion `{dll,exe}`, `**`, `?`, char classes) replacing
  the substring `IsExcluded` heuristic in InstallerBuilder.cs:266. Use in `EnumerateProjectFiles`.
- **Tests:** `*.{dll,exe}` matches only those ext; `**/bin/**` matches nested `bin`; `!` negation.
- **Acceptance:** include/exclude behave identically to gitignore-style expectations.

---

## Suggested session order (unchanged)
1. **Phase 0** — P0 fixes (one focused session).
2. **Track A4 + A1.1–A1.2** — robustness + basic scripting.
3. **Track A2 + A3** — compression & integration parity.
4. **Track B1–B2** — ClickOnce publish.
5. **Track B3** — auto-update.
6. **Track C** — MSIX.
7. **Cross-cutting X1–X5** throughout.
