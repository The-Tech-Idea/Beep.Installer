# Feature Gap Analysis — vs category leaders

**Last updated:** 2026-07-01 (revision 2)

**Goal:** Beep.Installer should match the capabilities of the leading setup/deployment tools.
Benchmark set: **Inno Setup, ClickOnce, NSIS, WiX/MSI, Advanced Installer, InstallShield,
Squirrel, Velopack.** The matrix below is split by our three target output tracks:

- **Track A** — `Setup.exe` wizard (Inno / NSIS / InstallShield / Advanced Installer)
- **Track B** — ClickOnce-style publish + auto-update (ClickOnce / Squirrel / Velopack)
- **Track C** — MSIX packaging (WiX-MSI / MSIX / Store)

Legend: ✅ done · 🟡 partial · ⬜ missing · 🔴 broken (P0)

---

## 1. Track A — Setup.exe wizard (Inno / NSIS / Advanced Installer / InstallShield)

### Core packaging & install

| Feature | Inno | NSIS | Adv.Installer | InstallShield | **Beep** | Notes |
|---|---|---|---|---|---|---|
| Project file (script/JSON) | ✅ .iss | ✅ .nsi | ✅ .aip | ✅ .ism | ✅ .bpkg | |
| Compile → Setup.exe | ✅ | ✅ | ✅ | ✅ | 🟡 | P0-1: payload broken off build machine |
| Wizard pages (welcome/license/components/folder/tasks/ready/complete) | ✅ | ✅ | ✅ | ✅ | ✅ | 8 pages + ErrorPage |
| Per-file progress | ✅ | ✅ | ✅ | ✅ | ✅ | FileCopyStep |
| Silent install | ✅ /SILENT | ✅ /S | ✅ | ✅ /s | ✅ /S | |
| Multi-language UI | ✅ 90+ | 🟡 | ✅ | ✅ | 🟡 8 langs | RTL ar/he/fa/ur |
| 32/64-bit install modes | ✅ | ✅ | ✅ | ✅ | ⬜ | needs separate install dirs + reg views |
| Per-user vs per-machine | ✅ | ✅ | ✅ | ✅ | 🟡 | UI toggle; full isolation TBD |

### Compression

| Feature | Inno | NSIS | Adv.Installer | InstallShield | **Beep** | Notes |
|---|---|---|---|---|---|---|
| LZMA2 / solid compression | ✅ | ✅ (LZMA/zlib/bzip2) | ✅ | ✅ | ⬜ | only System.IO.Compression zip |
| Selectable compression levels | ✅ | ✅ | ✅ | ✅ | 🟡 | 0-9 mapped to 4 enum levels |
| Single-file self-extractor | ✅ | ✅ | ✅ | ✅ | ⬜ | payload is sidecar folder/zip |
| Disk-spanning | ✅ | 🟡 | ✅ | ✅ | ⬜ | |

### Scripting / customization

| Feature | Inno | NSIS | Adv.Installer | InstallShield | **Beep** | Notes |
|---|---|---|---|---|---|---|
| Custom scripting language | ✅ Pascal Script | ✅ NSIS script | ✅ | ✅ InstallScript | ⬜ | **biggest parity gap** |
| Pre/post install event hooks | ✅ CurStepChanged etc. | ✅ | ✅ | ✅ | 🟡 | CustomActionStep exists; not exposed in UI |
| Custom wizard pages | ✅ | ✅ (nsDialogs) | ✅ | ✅ | ⬜ | only 8 fixed pages |
| Conditional components (OS/arch/reg) | ✅ | ✅ | ✅ | ✅ | 🟡 | InstallConditionEvaluator in BeepDM, not wired to UI |
| Install types (Typical/Custom/Complete) | ✅ | ✅ | ✅ | ✅ | 🟡 | enum exists; UI not enforced |

### System integration

| Feature | Inno | NSIS | Adv.Installer | InstallShield | **Beep** | Notes |
|---|---|---|---|---|---|---|
| Registry write/cleanup | ✅ | ✅ | ✅ | ✅ | ✅ | RegistryWriteStep |
| Shortcuts (Desktop/StartMenu/Startup/QuickLaunch) | ✅ | ✅ | ✅ | ✅ | ✅ | ShortcutCreateStep |
| Environment variables + WM_SETTINGCHANGE | ✅ | ✅ | ✅ | ✅ | ✅ | AdvancedSteps |
| File type associations | ✅ | ✅ | ✅ | ✅ | ✅ | FileAssociationStep |
| Windows Firewall rules | 🟡 | 🟡 | ✅ | ✅ | ✅ | FirewallStep |
| Windows Services | 🟡 | 🟡 | ✅ | ✅ | ✅ | ServiceInstallStep |
| Scheduled tasks | 🟡 | 🟡 | ✅ | ✅ | ✅ | AdvancedSteps |
| Fonts | ✅ | ✅ | ✅ | ✅ | ✅ | FontAndCertSteps |
| Certificates | 🟡 | 🟡 | ✅ | ✅ | ✅ | FontAndCertSteps |
| COM / DCOM registration | ✅ | 🟡 | ✅ | ✅ | ⬜ | |
| GAC assembly install | ✅ | 🟡 | ✅ | ✅ | ⬜ | |
| Shared file ref-counting | ✅ | ✅ | ✅ | ✅ | ⬜ | |
| Driver / device install | 🟡 | 🟡 | ✅ | ✅ | 🟡 | DriverProvisionStep exists (BeepDM) |
| Bootstrapper / prereq chaining | ✅ | ✅ | ✅ | ✅ | ✅ | BootstrapperStep |

### Robustness

| Feature | Inno | NSIS | Adv.Installer | InstallShield | **Beep** | Notes |
|---|---|---|---|---|---|---|
| SHA-256 file verification | ✅ | 🟡 | ✅ | ✅ | ✅ | InstallHelpers.ComputeFileHash |
| Restart Manager (locked files) | ✅ | 🟡 | ✅ | ✅ | ✅ | ScheduleFileForRestart |
| System restore point | ✅ | 🟡 | ✅ | ✅ | ✅ | SystemRestoreStep |
| Transactional rollback | ✅ | 🟡 | ✅ | ✅ | 🟡 | RollbackManager exists; not wired |
| Resume / checkpoint after reboot | ✅ | 🟡 | ✅ | ✅ | 🟡 | SetupCheckpointStore exists |
| Full uninstall with manifest | ✅ | ✅ | ✅ | ✅ | ✅ | UninstallStep |
| Code signing (signtool) | ✅ | ✅ | ✅ | ✅ | ✅ | SHA256 + RFC3161 |

---

## 2. Track B — ClickOnce-style publish + auto-update (ClickOnce / Squirrel / Velopack)

| Feature | ClickOnce | Squirrel | Velopack | **Beep** | Notes |
|---|---|---|---|---|---|
| Publish to web/UNC/folder | ✅ | ✅ (GitHub/Release) | ✅ | ⬜ | /PUBLISH= planned |
| `.application` + `.manifest` | ✅ | n/a (Release) | n/a | ⬜ | |
| `.deploy` file renaming | ✅ | n/a | n/a | ⬜ | |
| Per-user install (no admin) | ✅ | ✅ | ✅ | 🟡 | UI toggle; isolation TBD |
| Auto-update on launch | ✅ | ✅ | ✅ | ⬜ | UpgradeEngine exists, not wired |
| Background delta/patch updates | 🟡 | 🟡 | ✅ (binary diff) | ⬜ | Velopack-style delta |
| Rollback to previous version | ✅ | 🟡 | ✅ | 🟡 | UpgradeEngine.Backup/Restore exists |
| Update subscription (required vs optional) | ✅ | n/a | ✅ | ⬜ | |
| Trust prompt / signature | ✅ | ✅ | ✅ | ✅ | signtool; manifest signing TBD |
| First-run experience / shortcuts | ✅ | ✅ | ✅ | 🟡 | ShortcutCreateStep |
| Uninstall via Add/Remove Programs | ✅ | ✅ | ✅ | ✅ | |

---

## 3. Track C — MSIX / Store (MSIX / WiX-MSI / Store)

| Feature | MSIX | WiX (MSI) | Store | **Beep** | Notes |
|---|---|---|---|---|---|
| `.msix` / `.msixbundle` output | ✅ | n/a (.msi) | ✅ | ⬜ | MakeAppx; BuildOptions.OutputFormat |
| `.msi` output (WiX) | n/a | ✅ | n/a | ⬜ | lower priority |
| AppxManifest.xml generation | ✅ | n/a (WiX XML) | ✅ | ⬜ | identity/capabilities/visuals |
| Visual assets (tiles/splash) | ✅ | n/a | ✅ | ⬜ | from icon/banner |
| Capabilities declaration | ✅ | n/a | ✅ | ⬜ | |
| MSIX signing (trusted cert) | ✅ | n/a | ✅ (Store) | ⬜ | |
| App Installer (.appinstaller) auto-update | ✅ | n/a | ✅ | ⬜ | overlaps Track B |
| Store packaging/validation | n/a | n/a | ✅ | ⬜ | |
| Containerized clean uninstall | ✅ | 🟡 | ✅ | ⬜ | MSIX gives this free |

---

## 4. Generator (developer tool) — cross-track

| Feature | Adv.Installer | InstallShield | **Beep** | Notes |
|---|---|---|---|---|
| GUI project editor | ✅ | ✅ | ✅ | 10 tabs |
| Project templates gallery | ✅ | ✅ | ⬜ | |
| Side-by-side build comparison | 🟡 | 🟡 | ⬜ | |
| Upgrade/repair/modify-mode editors | ✅ | ✅ | ⬜ | engine supports, no UI |
| Component condition editor | ✅ | ✅ | ⬜ | |
| Repackaging (exe→msi/msix) | ✅ | ✅ | ⬜ | |
| CI/headless build | ✅ | ✅ | ✅ | /BUILD |
| Live wizard preview | ✅ | ✅ | ✅ | Themed+Classic |
| Language translation editor | ✅ | ✅ | ✅ | LanguageManagerForm |

---

## 5. Priority summary (what to build, in order)

1. **P0 fixes** (§0 of master todo) — without these nothing ships correctly.
2. **Track A scripting + compression parity** — the single biggest perceived gap vs Inno/NSIS.
3. **Track A system integration** (64-bit, COM, GAC, shared counts, conditions) — enterprise parity.
4. **Track B publish + auto-update** — unlocks the ClickOnce/Squirrel/Velopack use-case.
5. **Track C MSIX** — Store + enterprise modern packaging.
6. **Cross-cutting** (rollback wiring, templates, accessibility, diagnostics).

Full per-phase breakdown: `07-deployment-models-roadmap.md`.
