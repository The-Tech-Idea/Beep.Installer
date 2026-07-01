# Beep Installer — Architecture Overview

## What this app IS

**Beep.Installer is an *installer generator*** — like InnoSetup Compiler, NSIS, or Advanced Installer. The developer uses it to author an installer project (.bpkg) and produce a self-contained `Setup.exe` to ship to end users.

The same `Beep.Installer.exe` is **both** the generator AND the runtime. At startup it detects:
- If `install-config.json` exists next to the exe → run as the shipped installer
- If `/BUILD=`, `/VALIDATE=`, `/S`, `/UNINSTALL`, `/LANGMGR`, etc. → run in the corresponding mode
- Otherwise → open the Package Builder UI

---

## Generator mode (Package Builder)

```
┌───────────────────────────────────────────────────────────────────────┐
│ [New][Open][Save][Save As] | [Recent ▾] | [Preview][Build] | [Lang][Help][About] │
├───────────────────────────────────────────────────────────────────────┤
│ [Project][Files][Components][Prereqs][Shortcuts][Registry]           │
│ [Branding][Wizard Pages][Build][Build Log]                            │
├───────────────────────────────────────────────────────────────────────┤
│                    (active tab content)                               │
├───────────────────────────────────────────────────────────────────────┤
│ status text                    ● unsaved                [▓▓░░] 75%    │
└───────────────────────────────────────────────────────────────────────┘

Keyboard: Ctrl+N (New)  Ctrl+O (Open)  Ctrl+S (Save)  Ctrl+Shift+S (SaveAs)
          Ctrl+P (Preview)  F5 (Build)  F7 (Validate)
```

## Runtime mode (shipped installer)

```
┌───────────────────────────────────────────────────────────────────┐
│ BEEP v1.0.0            |  Setup steps                              │
│ [banner.png]            |    ✓ 1. Welcome                           │
│ ────────────             |    ✓ 2. License                           │
│ ✓ Welcome               |    ► 3. Components                        │
│ ✓ License               |      4. Folder                            │
│ ► Components            |      5. Start Menu                        │
│   Folder                |      6. Tasks                             │
│   Start Menu            |      7. Ready                             │
│   Tasks                 |                                           │
│   Ready                 | Language: [English ▾]                     │
├───────────────────────────────────────────────────────────────────┤
│                 (wizard page content)                              │
├───────────────────────────────────────────────────────────────────┤
│                         [< Back]  [Install]  [Cancel]              │
└───────────────────────────────────────────────────────────────────┘

Two styles: Themed (dark sidebar) + Classic (light sidebar)
Both support: branding colors, banner image, RTL (Arabic/Hebrew/Farsi/Urdu)
Keyboard: Ctrl+B (Back)  Ctrl+N (Next)  Escape (Cancel)
```

## Install step pipeline

```
PrerequisiteCheckStep     Checks OS, .NET, admin, disk space, custom prereqs
       │
       ▼
DirectoryCreateStep       Creates the install directory tree
       │
       ▼
PayloadDownloadStep       (if PayloadSource=Url) Downloads zip from URL, extracts
       │
       ▼
FileCopyStep              Copies all component files with per-file progress
       │
       ▼
ShortcutCreateStep        Creates Desktop / Start Menu / Startup .lnk files
       │
       ▼
RegistryWriteStep         Writes HKLM registry entries (config + Add/Remove Programs)
       │
       ▼
VerifyInstallStep         Verifies all files, writes install-manifest.json for uninstall
```

## Build pipeline (InstallerBuilder)

```
0. Validate project (product name, version, components)
   └─ if errors → abort with error report

1. (optional) Clean output directory if overwrite confirmed

2. Stage source files → payload/
   └─ apply IncludePatterns / ExcludePatterns

3. Generate uninstall registry entries (if RegisterUninstallEntry)

4. Write config files:
   ├── install-config.json   (InstallConfig)
   ├── branding.json         (InstallerBranding)
   ├── payload.json          (PayloadSource + PayloadUrl)
   ├── version.txt           (product + version + build timestamp)
   └── project.bpkg          (full project for reference)

5. Copy branding assets:
   ├── banner.png            (from Branding.WelcomeBannerPath)
   └── setup.ico             (from BuildOptions.IconPath)

6. Copy runtime + dependencies:
   ├── Setup-{Product}-{Version}.exe
   ├── Setup-{Product}-{Version}.deps.json
   └── Setup-{Product}-{Version}.runtimeconfig.json

7. Embed .ico into PE (Win32 UpdateResource — all icon sizes)

8. (optional) Compress payload/ → payload.zip

9. (optional) Code-sign with signtool.exe (SHA256 + RFC 3161 timestamp)
```

## Project layout

```
Beep.Installer/                     Generator + runtime
├── Models/InstallProject.cs        .bpkg model + BuildOptions
├── Engine/
│   ├── InstallerBuilder.cs         Build pipeline
│   ├── ProjectSerializer.cs        .bpkg IO
│   ├── ThemeLoader.cs              Branding.json + color parsing
│   ├── BannerLoader.cs             Image loading + resizing
│   ├── PeIconEmbedder.cs           Win32 UpdateResource for .ico
│   ├── RtlHelper.cs                Arabic/Hebrew/Farsi/Urdu RTL
│   ├── LocaleFormatter.cs          Culture-aware size/date formats
│   └── RecentProjects.cs           MRU list in %APPDATA%
├── Forms/
│   ├── PackageBuilderForm.cs       Tabbed generator UI
│   ├── ProjectNewDialog.cs         New project dialog
│   ├── ComponentFilesDialog.cs     File list editor + drag-drop
│   ├── WizardPreviewForm.cs        Preview both wizard styles
│   ├── ThemedInstallerForm.cs      Runtime — modern dark sidebar
│   ├── InstallerWizardForm.cs      Runtime — classic light sidebar
│   ├── InstallerMainForm.cs        Runtime — progress display
│   ├── LanguageManagerForm.cs      Translation editor
│   └── InputBox.cs                 Modal text prompt
├── Pages/
│   ├── IInstallerPage.cs           Interface + InstallContext
│   ├── WelcomePage.cs
│   ├── LicensePage.cs
│   ├── ComponentSelectionPage.cs
│   ├── FolderPage.cs
│   ├── StartMenuPage.cs
│   ├── AdditionalTasksPage.cs
│   ├── ReadyPage.cs
│   ├── CompletePage.cs
│   └── ErrorPage.cs               Dedicated error UI with Retry
├── Steps/PayloadDownloadStep.cs    ISetupStep — URL payload download
├── Lang/
│   ├── LanguageManager.cs          i18n with 3-tier fallback
│   └── Strings_*.resx              en, ar, de, es, fr, ja, pt, zh
├── Program.cs                      Mode dispatch + CLI
├── WinFormsInstallerAdapter.cs     ISetupWizardAdapter bridge
└── samples/
    ├── HelloApp/                   Sample source tree
    └── MyApp.bpkg                  Sample project

Beep.Installer.Tests/              xUnit — 38 tests, 0 failures
├── ProjectSerializerTests.cs      7 tests
├── InstallerBuilderTests.cs       8 tests
├── ThemeLoaderTests.cs            4 tests
├── EndToEndTests.cs               6 tests
└── EdgeCaseTests.cs              13 tests
```

## CLI commands

```
Beep.Installer.exe                        Open Package Builder (or run as installer)
Beep.Installer.exe /BUILD=<project.bpkg>  Build installer (headless, CI-friendly)
Beep.Installer.exe /VALIDATE=<project.bpkg> Validate project without building
Beep.Installer.exe /PREVIEW=<project.bpkg> Show project summary
Beep.Installer.exe /CONFIG=<config.json>  Run as installer with explicit config
Beep.Installer.exe /S [/D=<path>] [/CONFIG=<path>] Silent install
Beep.Installer.exe /UNINSTALL [/D=<path>] [/CONFIG=<path>] Silent uninstall
Beep.Installer.exe /SELFTEST              Install + verify + uninstall in %TEMP%
Beep.Installer.exe /LANGMGR               Open Language Manager
Beep.Installer.exe /OUT=<dir>             Override output dir (with /BUILD)
Beep.Installer.exe /?                     Show this help
```

## Key design decisions

1. **Generator and runtime are the SAME exe** — no extra build pipeline. The generator copies itself (renamed) to the output folder.
2. **Self-contained payload** — files are a folder or zip next to the Setup.exe. No MSI, no Wix, no NSIS — pure .NET 9.
3. **Project file is human-editable JSON** — `.bpkg` uses camelCase, supports comments and trailing commas.
4. **Two wizard styles, one config** — Themed and Classic are interchangeable; both consume InstallConfig + InstallerBranding.
5. **Reuse the BeepDM engine** — install steps are in BeepDM (PrerequisiteCheck, FileCopy, Registry, etc.). The generator authors configs; the runtime invokes them.
6. **Headless build** — `/BUILD=project.bpkg /OUT=dir` for CI without UI.
7. **No external dependencies** — signtool is found on PATH or Windows Kits; everything else is built-in Win32 APIs.
