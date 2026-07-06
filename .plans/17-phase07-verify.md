# Phase 07 — Verify + Polish

**Goal:** `dotnet test Beep.Installer.Tests` is green. Manual install of every sample (Local,
URL, MSIX) succeeds. CLI smoke test (`/BUILD`, `/VALIDATE`, `/PREVIEW`, `/PUBLISH`, `/S`,
`/UNINSTALL`, `/SELFTEST`) succeeds. README is updated. Sample `.bsetup` scripts are migrated to
the new single-section shape.

**Why seventh:** the final gate. Catches regressions and confirms end-to-end behavior.

---

## Scope

### 7.1 Automated tests

```powershell
dotnet test Beep.Installer.Tests --nologo --logger "console;verbosity=normal"
```

All 32 test files pass. Required by acceptance criterion #9.

### 7.2 Manual install scenarios

For each scenario, use the sample apps in `Beep.Installer/samples/`:

| # | Scenario | What to verify |
|---|---|---|
| 1 | Local payload, self-contained, embedded | `Setup.exe` is a single file. Run it on a clean machine. Files are copied, registry is written, shortcuts created. |
| 2 | Local payload, framework-dependent | Build with `SelfContained = false`. Run on a machine with .NET 10 runtime installed. Files are copied. |
| 3 | URL payload | Set `PayloadSource = Url`. Run. Runtime downloads the payload before file copy. Files are copied. |
| 4 | MSIX output | Set `OutputFormat = Msix`. Build produces `*.msix`. Install via `Add-AppxPackage`. |
| 5 | MSIX bundle | Set `OutputFormat = MsixBundle`. Build produces `*.msixbundle`. |
| 6 | Silent install | Run `Setup.exe /S`. No UI. Files are copied. |
| 7 | Silent uninstall | Run `Setup.exe /UNINSTALL`. Files removed, registry cleaned, shortcuts gone. |
| 8 | Self-test | Run `Setup.exe /SELFTEST`. Install + verify + uninstall in `%TEMP%`. Exit code 0. |
| 9 | Code-signed build | Set `CodeSignCertificatePath = ...pfx`. Build. EXE has valid signature. |

### 7.3 CLI smoke test

```powershell
# Build
Beep.Installer.exe /BUILD=samples\HelloApp\Setup.bsetup /OUT=...\build
# Validate
Beep.Installer.exe /VALIDATE=samples\HelloApp\Setup.bsetup
# Preview
Beep.Installer.exe /PREVIEW=samples\HelloApp\Setup.bsetup
# Publish (ClickOnce)
Beep.Installer.exe /PUBLISH=samples\HelloApp\Setup.bsetup /OUT=...\publish
```

### 7.4 Cross-machine integration test

The P0-1 cross-machine test still passes: build on machine A, copy artifacts to machine B,
install on machine B, files are copied, no source-machine paths leak through.

### 7.5 Sample `.bsetup` migration

`samples/HelloApp/Setup.bsetup` (and any other sample scripts) are updated to the new
single-section shape:

```
[Setup]
SchemaVersion=1.0
ScriptName=HelloApp
AppName=HelloApp
AppVersion=1.0.0
AppPublisher=The Tech Idea
...
```

### 7.6 README updates

The `Beep.Installer/README.md` (if it exists) and the main `.plans/01-architecture-overview.md`
are updated:

- Document the flat `InstallProject` model.
- Document the new `[Setup]`-only script format.
- Document the enums and what they map to.
- Document the migration path (legacy files load, save produces `.bak` + new shape).
- Update the wizard status table — `BeepModernInstallerForm` is the only runtime wizard.

### 7.7 `.plans/00-master-todo.md` update

The original `00-master-todo.md` (Tracks A/B/C plan) is preserved (it's the multi-model roadmap
doc). The new `10-master-todo.md` is the consolidation tracker. Both coexist.

### 7.8 Final acceptance sweep

```bash
grep -rn "Branding"          Beep.Installer BeepDM  → 0 hits outside legacy-loader fallback
grep -rn "InstallConfig\."   Beep.Installer BeepDM  → 0 hits outside legacy-loader fallback
grep -rn "project\.Build\."  Beep.Installer         → 0 hits
grep -rn "BindSetupTabFromProject\|BindBrandingTabFromProject\|BindBuildTabFromProject"
                              Beep.Installer/Forms   → 0 hits
ls Beep.Installer/Forms/*InstallerForm* → exactly BeepModernInstallerForm.cs
dotnet test Beep.Installer.Tests        → 0 failures
```

Every entry in the master tracker `## Acceptance criteria` table is checked.

---

## Files

### UPDATED

- `Beep.Installer/README.md` (if it exists)
- `.plans/01-architecture-overview.md` — wizard table + script format notes
- `Beep.Installer/samples/HelloApp/Setup.bsetup` (and any other sample scripts) — migrated to new
  single-section shape.

### UNCHANGED

- All source/test code — those are owned by phases 1–6.

---

## Order of execution

1. `dotnet test Beep.Installer.Tests` — fix any remaining failures.
2. Manual install of all 9 scenarios from §7.2.
3. CLI smoke test from §7.3.
4. Cross-machine integration test.
5. Migrate sample `.bsetup` files.
6. Update README + `.plans/01-architecture-overview.md`.
7. Final acceptance sweep from §7.8.
8. Mark the master tracker "done" — every row in the acceptance criteria table is green.

---

## Acceptance

| # | Check |
|---|---|
| 1 | `dotnet test Beep.Installer.Tests` — 0 failures. |
| 2 | All 9 manual install scenarios from §7.2 succeed. |
| 3 | All 4 CLI commands from §7.3 succeed. |
| 4 | Cross-machine integration test passes. |
| 5 | Sample `.bsetup` files are in the new single-section shape. |
| 6 | README + `.plans/01-architecture-overview.md` updated. |
| 7 | All 9 master-tracker acceptance criteria from `10-master-todo.md` are green. |
| 8 | The shipped `Setup.exe` is self-contained and embeds the payload when `SingleFile = true`. |