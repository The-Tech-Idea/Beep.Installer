# Commercial Installer Feature Gap Analysis

**This is now a generator** (not the installer itself). The gap analysis is split in two:

1. **Generator gap** — what the *developer tool* should be able to do
2. **Generated installer gap** — what the *shipped Setup.exe* should be able to do

The shipped Setup.exe is largely powered by the BeepDM engine (steps, helpers, etc.), so most "installer" features are already there.

---

## 1. Generator (developer tool) — what the Package Builder should do

| Feature | Status | Notes |
|---|---|---|
| ✅ Create / open / save `.bpkg` projects | ✅ | `ProjectSerializer`, `PackageBuilderForm` |
| ✅ Tabbed project editor (10 tabs) | ✅ | Project / Files / Components / Prerequisites / Shortcuts / Registry / Branding / Wizard Pages / Build / Log |
| ✅ Source directory scan with include/exclude patterns | ✅ | Approximate glob (`**/`); full glob TBD |
| ✅ Per-component file list editor | ✅ | `ComponentFilesDialog` |
| ✅ Live preview of wizard (both styles) | ✅ | `WizardPreviewForm` shows Themed + Classic side-by-side, consumes `InstallerBranding` |
| ✅ Live progress during build | ✅ | `Progress<BuildProgress>` in status bar + log tab |
| ✅ Headless build for CI (`/BUILD=`) | ✅ | |
| ✅ Headless preview (`/PREVIEW=`) | ✅ | |
| ✅ Language manager for translations | ✅ | `LanguageManagerForm` |
| ✅ Theming / branding editor | ✅ | colors, banner, theme name |
| ✅ Sample project | ✅ | `samples/MyApp.bpkg` + `samples/HelloApp/` |
| ✅ About dialog | ✅ | `PackageBuilderForm.ShowAbout()` |
| ✅ Unit tests | ✅ | 19 tests in `Beep.Installer.Tests` |
| ⬜ Drag-drop file editor | ⬜ | |
| ⬜ Component condition editor (per-component conditions) | ⬜ | TBD |
| ⬜ Upgrade / repair / modify-mode editors | ⬜ | Engine supports these, but no UI |
| ⬜ Project templates gallery | ⬜ | |
| ⬜ Side-by-side build comparison | ⬜ | |

---

## 2. Generated installer (shipped Setup.exe) — what the end user sees

### 🔴 Critical (expected in any commercial installer)

| Feature | Status | Where |
|---|---|---|
| **Wizard pages (welcome/license/components/folder/startmenu/tasks/ready/complete)** | ✅ | `Pages/*` |
| **File copy with per-file progress** | ✅ | `FileCopyStep` (BeepDM) |
| **Shortcuts (Desktop / StartMenu / Startup)** | ✅ | `ShortcutCreateStep` (BeepDM) |
| **Registry write/cleanup** | ✅ | `RegistryWriteStep` (BeepDM) |
| **Uninstall with manifest** | ✅ | `UninstallStep` (BeepDM) — manifest written by `VerifyInstallStep` |
| **Prerequisite checks (OS, .NET, admin, disk)** | ✅ | `PrerequisiteCheckStep` (BeepDM) |
| **Silent / unattended mode** | ✅ | `/S` flag in `Program.cs` |
| **Hash verification (SHA256 per file)** | ✅ | `InstallHelpers.ComputeFileHash` |
| **System restore point** | ✅ | `SystemRestoreStep` (BeepDM) |
| **Restart Manager (locked files)** | ✅ | `InstallHelpers.ScheduleFileForRestart` |
| **Custom actions (run scripts/exes)** | ✅ | `CustomActionStep` (BeepDM) |
| **Config generation (appsettings, etc.)** | ✅ | `ConfigGenerationStep` (BeepDM) |
| **Multi-language UI** | ✅ | 8 langs shipped; runtime switcher in `ThemedInstallerForm` |
| **Per-user vs per-machine** | ✅ | `FolderPage.PerUser` toggle |
| **File type associations** | ✅ | `FileAssociationStep` (BeepDM) + `InstallHelpers.RegisterFileAssociation` |

### 🟡 High (competitive differentiators)

| Feature | Status | Where |
|---|---|---|
| **Bootstrapper chaining** (install prereqs in sequence) | ✅ | `BootstrapperStep` (BeepDM) |
| **Conditional components** (OS, arch, registry check) | ✅ | `InstallConditionEvaluator` (BeepDM) |
| **Firewall rules** | ✅ | `FirewallStep` (BeepDM) |
| **Windows Service install** | ✅ | `ServiceInstallStep` (BeepDM) |
| **Scheduled tasks** | ✅ | `AdvancedSteps` (BeepDM) |
| **Environment variables** | ✅ | `AdvancedSteps` (BeepDM) |
| **Network install** (source from UNC/URL) | 🟡 | Engine supports it but no UI in generator |
| **Pause/Resume** | ✅ | `PauseResumeManager` (BeepDM) + `SetupCheckpointStore` |
| **Code signing of the Setup.exe** | 🟡 | Generator stub; runs signtool externally |
| **Recording/playback** (record an install, replay) | ⬜ | TBD |

### 🟢 Nice-to-have (premium products)

| Feature | Status | Notes |
|---|---|---|
| **Theming / skinnable UI** | ✅ | `InstallerBranding` model + `Engine.ThemeLoader` runtime; 3 themes (Modern/Classic/Compact) |
| **Branding (logo, colors, banner)** | 🟡 | Generator side complete; runtime banner image still TBD |
| **Online payload (download during install)** | ⬜ | TBD |
| **MSIX packaging** | ⬜ | TBD |
| **Delta updates** (binary diff upgrades) | ⬜ | `UpgradeEngine` exists in BeepDM but is not yet wired into the generator |
| **MSI wrapper** | ⬜ | TBD |
| **Merge modules** (`.msm`) | ⬜ | TBD |
| **Advertising shortcuts** (install-on-first-use) | ⬜ | TBD |
| **COM registration** | ⬜ | TBD |
| **Environment broadcast** (WM_SETTINGCHANGE) | ✅ | `InstallHelpers.BroadcastEnvironmentChange` |
| **Font installation** | ✅ | `FontAndCertSteps` (BeepDM) |
| **Certificate installation** | ✅ | `FontAndCertSteps` (BeepDM) |
| **Installer password** | ⬜ | TBD |
| **Installation analytics** | ⬜ | TBD |
| **Self-test mode** | ✅ | `/SELFTEST` flag |
| **Upgrade detection + backup/restore** | ✅ | `UpgradeEngine` (BeepDM) |
| **Code signing of the Setup.exe** | ✅ | `InstallerBuilder.CodeSign` invokes `signtool.exe` (SHA256 + RFC 3161 timestamp) |

---

## 3. Implementation Plan (sessions 1 and 2)

### Session 1 — Generator foundation

- ✅ Clarified generator vs runtime architecture
- ✅ Built a complete, tabbed Package Builder
- ✅ Built a full `InstallerBuilder` engine
- ✅ Built two complete production-quality wizard UIs (Themed + Classic)
- ✅ Fixed all major language-loading bugs
- ✅ Fixed page-level language fallbacks
- ✅ CLI for headless build / preview / self-test
- ✅ Headless dispatch (WinForms init deferred for CLI)

### Session 3 — Banner, drag-drop, validation, recent, icon, integration tests

- ✅ **Banner image in runtime** — `Engine.BannerLoader` + branded banner display in both wizards
- ✅ **Drag-drop in ComponentFilesDialog** — Explorer file/folder drops auto-add to component
- ✅ **/VALIDATE command** — `Beep.Installer.exe /VALIDATE=project.bpkg` + Validate button in UI
- ✅ **Recent Projects** — persistent MRU list + toolbar dropdown
- ✅ **PE icon embedding** — Win32 `UpdateResource` replaces icon in generated .exe
- ✅ **Integration tests** — full build/install/uninstall cycle; 25 total tests
- ✅ **Runtime config copy** — deps.json + runtimeconfig.json copied alongside generated exe
- ✅ **/S + /CONFIG= and /UNINSTALL + /CONFIG=** — explicit config path support

## 4. Next Session

1. **Online payload** — `BuildOptions` already has structure; add `DownloadStep`
2. **MSIX packaging** as alternative output format
3. **Integration tests** — generate a real `Setup.exe`, run it in a clean VM
4. **Drag-drop** in `ComponentFilesDialog`
5. **High-DPI / accessibility audit**
6. **PE icon embedding** (currently sidecar; needs PE resource patcher)
7. **Localization polish** — RTL handling, locale-specific formatting
