# Beep Installer — Master TODO Tracker

**Last updated:** 2026-07-01 (comprehensive revision pass — 14 bug fixes + plan update)

**Target:** **Installer generator** (like InnoSetup Compiler / NSIS). Authors `.bpkg` projects and builds self-contained `Setup.exe` distributions.

---

## 1. Headline Status

| Item | Status |
|---|---|
| Project model (`.bpkg`) | ✅ |
| Build pipeline (`.bpkg` → `Setup.exe`) | ✅ |
| Project serializer | ✅ |
| Generator UI — Package Builder (tabbed, keyboard shortcuts) | ✅ |
| Generator UI — Welcome page + Recent Projects | ✅ |
| Generator UI — New Project dialog | ✅ |
| Generator UI — Component Files editor + drag-drop | ✅ |
| Generator UI — Language Manager (event leak fixed) | ✅ |
| Wizard preview (Themed + Classic, branded) | ✅ |
| Runtime installer — Themed (modern dark, banner, RTL) | ✅ |
| Runtime installer — Classic (light, banner, RTL) | ✅ |
| Runtime installer — Progress display (localized) | ✅ |
| Runtime installer — Error page with Retry | ✅ |
| WinForms adapter (disposal-safe) | ✅ |
| Wizard pages (8) including ErrorPage | ✅ |
| Multi-language resources (8 langs, RTL support) | ✅ |
| CLI: `/BUILD`, `/VALIDATE`, `/PREVIEW`, `/CONFIG`, `/S`, `/UNINSTALL`, `/SELFTEST`, `/LANGMGR`, `/?` | ✅ |
| Headless dispatch (WinForms init deferred for CLI) | ✅ |
| Self-test (`/SELFTEST`) — no temp dir leaks | ✅ |
| Theming pipeline (branding.json → runtime colors + banner) | ✅ |
| Code signing (signtool.exe — SHA256 + RFC 3161) | ✅ |
| PE icon embedding (Win32 UpdateResource — all icon sizes) | ✅ |
| Banner image in runtime | ✅ |
| Add/Remove Programs registry auto-generation | ✅ |
| Online payload (URL-based download step) | ✅ |
| High-DPI support (AutoScaleMode.Dpi on all forms) | ✅ |
| Keyboard shortcuts (Ctrl+N/O/S/P, F5/F7, Ctrl+B/N/Esc on wizards) | ✅ |
| Build overwrite confirmation | ✅ |
| Drag-drop .bpkg onto builder | ✅ |
| Component auto-populate from source scan | ✅ |
| Locale-aware formatting (file sizes, dates) | ✅ |
| Version stamping (version.txt next to Setup.exe) | ✅ |
| Unit tests — 38 tests, 0 failures | ✅ |
| Integration tests — full build/install/uninstall cycle | ✅ |
| **Bug fixes this session** — 14 critical/logic bugs resolved | ✅ |
| **MSIX packaging** | ⬜ |
| **Delta updates** | ⬜ |
| **Project templates** | ⬜ |
| **Replace silent catch-all blocks with diagnostics** | ⬜ |
| **High-contrast theme support** | ⬜ |
| **UI TabIndex ordering** | ⬜ |
| **AccessibleNames on controls** | ⬜ |

**Completion:** 38 / 44 core items ✅ (86%).

---

## 2. Bug fix log (this session)

| # | Severity | File | Issue | Fix |
|---|---|---|---|---|
| 1 | 🔴 Critical | Program.cs:179 | Null ref if install-config.json is malformed | Added null check + error message |
| 2 | 🔴 Critical | LanguageManagerForm:169 | Event handler leak on CellValueChanged/SelectionChanged | Unsubscribe before re-subscribing |
| 3 | 🔴 Critical | PeIconEmbedder | Only first .ico entry embedded; group references missing IDs | Loop through all icons, embed each as RT_ICON |
| 4 | 🔴 Critical | ComponentFilesDialog:276 | UpdateFromEditor selected wrong item after RemoveAt+Add | InsertAt original index instead of appending |
| 5 | 🔴 Critical | ThemedInstallerForm:413 | Progress Invoke without disposal guard | Added IsDisposed check + try/catch |
| 6 | 🟡 High | Both wizards | Retry navigated to `_pages.Count-3` (magic index) | FindIndex by page type (ReadyPage) |
| 7 | 🟡 High | Both wizards | CapturePageState called BEFORE Validate | Swapped: validate first, then capture |
| 8 | 🟡 High | RtlHelper | Only Arabic supported for RTL | Added Hebrew, Farsi, Urdu |
| 9 | 🟡 High | InstallerBuilder+PackageBuilder | Duplicate SafeFileName methods | Shared utility, removed duplicate |
| 10 | 🟡 High | InstallContext | FileAssociations checkbox was dead UI | Added property + wired in both CapturePageState methods |
| 11 | 🟢 Medium | PayloadDownloadStep | HttpClient created per download call | Made static readonly |
| 12 | 🟢 Medium | Program.cs silent install | Missing PrerequisiteCheckStep | Added to step pipeline |
| 13 | 🟢 Medium | Program.cs self-test | Source temp dir never cleaned | Track and clean up in finally block |
| 14 | 🟢 Medium | InstallerMainForm | All UI strings hardcoded | Localized key strings via LanguageManager |

---

## 3. Architecture

```
              ┌──────────────────────────────────┐
              │  Beep.Installer.exe  (DEVELOPER) │
              │  Package Builder GUI             │
              │   ├── Welcome page (New/Open/Recent) │
              │   ├── Project tab (product info) │
              │   ├── Files & Payload tab        │
              │   ├── Components tab (add/edit/scan) │
              │   ├── Prerequisites tab          │
              │   ├── Shortcuts tab              │
              │   ├── Registry tab               │
              │   ├── Branding tab (colors, banner) │
              │   ├── Wizard Pages tab (enable/disable) │
              │   ├── Build tab (output, signing, compression) │
              │   ├── Build Log tab              │
              │   └── Language Manager tool      │
              └─────────────────┬────────────────┘
                                │ Save .bpkg → Build button → /BUILD=
                                ▼
            ┌──────────────────────────────────────┐
            │  InstallerBuilder engine            │
            │   0. Validate / clean output        │
            │   1. Stage source files → payload/  │
            │   2. Generate uninstall registry keys │
            │   3. Write install-config.json      │
            │   4. Write branding.json / payload.json │
            │   5. Write version.txt              │
            │   6. Copy branding assets (banner, icon) │
            │   7. Copy this exe as Setup-…exe    │
            │      + deps.json + runtimeconfig.json │
            │   8. Embed .ico into PE (UpdateResource) │
            │   9. (Optional) compress payload → .zip │
            │  10. (Optional) code-sign via signtool │
            └─────────────────┬────────────────────┘
                              │
                              ▼
            ┌──────────────────────────────────────┐
            │  Setup-{Product}-{Version}.exe      │
            │  install-config.json                │
            │  branding.json                      │
            │  payload.json  (source=Local|Url)   │
            │  version.txt                        │
            │  payload/ or payload.zip            │
            └─────────────────┬────────────────────┘
                              │ User runs Setup.exe
                              ▼
            ┌──────────────────────────────────────┐
            │  ThemedInstallerForm  OR            │
            │  InstallerWizardForm                │
            │  Steps:                             │
            │   PrerequisiteCheck                 │
            │   DirectoryCreate                   │
            │   PayloadDownload (if URL)           │
            │   FileCopy                           │
            │   ShortcutCreate                    │
            │   RegistryWrite (incl. Add/Remove)   │
            │   VerifyInstall                      │
            │  Pass/fail → CompletePage / ErrorPage │
            └──────────────────────────────────────┘
```

---

## 4. Next Steps (priority order)

1. **MSIX packaging** — alternative output format for Microsoft Store / enterprise (2 days)
2. **Delta updates** — wire BeepDM UpgradeEngine into the generator (1 day)
3. **High-contrast theme** — detect and apply Windows high-contrast colors (0.5 day)
4. **AccessibleNames** — screen reader support on key controls (0.5 day)
5. **UI TabIndex** — proper tab order on all forms (0.5 day)
6. **Replace silent catch blocks** — log diagnostics instead of swallowing (0.5 day)
7. **Project templates** — save/load .bpkg as template for reuse (0.5 day)
8. **Auto-save** — periodic save of dirty project (0.5 day)
