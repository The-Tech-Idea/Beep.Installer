# Phase 01 — Project Model & Build Options

**Target:** `Beep.Installer/Models/`

**Status:** ✅ Complete (revision pass: 2026-07-01)

## `InstallProject`

The `.bpkg` artifact. Contains:
- `SchemaVersion`, `ProjectName`, `CreatedAt`, `ModifiedAt`
- `SourceDirectory` — root of the file tree to install
- `IncludePatterns` / `ExcludePatterns` — file glob filters
- `InstallConfig` — the runtime configuration (from BeepDM)
- `Branding` — UI branding (from BeepDM)
- `Build` — build pipeline options

## `BuildOptions`

Build-time settings:

| Field | Default | Notes |
|---|---|---|
| `OutputFileName` | `"Setup.exe"` | Generated exe name |
| `OutputDirectory` | `""` | Defaults to Documents\BeepInstaller\Builds\... |
| `PayloadFolderName` | `"payload"` | Subfolder / zip name |
| `IconPath` | `""` | .ico to embed in PE |
| `BannerImagePath` | `""` | Banner for wizard sidebar |
| `EulaFilePath` | `""` | Override license text |
| `CompressPayload` | `true` | Zip the payload folder |
| `CompressionLevel` | `6` | 0=store, 1-3=fast, 4-6=optimal, 7-9=smallest |
| `CodeSignCertificatePath` | `""` | .pfx for signtool |
| `CodeSignCertificatePassword` | `""` | Certificate password |
| `CodeSignTimestampUrl` | `"http://timestamp.digicert.com"` | RFC 3161 timestamp |
| `RegisterUninstallEntry` | `true` | Windows Add/Remove Programs |
| `CreateSystemRestorePoint` | `true` | System restore before install |
| `SelfContained` | `false` | Bundle .NET runtime |
| `Architecture` | `"x64"` | x64 / x86 / arm64 / AnyCPU |
| `AllowScopeSelection` | `true` | Per-user vs per-machine |
| `DefaultScope` | `"Machine"` | Machine or User |
| `PayloadSource` | `"Local"` | "Local" (bundled) or "Url" (downloaded) |
| `PayloadUrl` | `""` | URL for online payload |

## Key design note

The model is the single source of truth for both the generator and the generated installer. The same object is serialized into the `.bpkg` project file AND into the `install-config.json` shipped with the Setup.exe (via `BuildOptions` fields are written to `payload.json`).
