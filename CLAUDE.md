<!-- smarterasp-pem-rule:2026-10-02 -->
> **Fahad's SmarterASP hosting rule (2026-10-02):** Use IdentityServer's PEM RSA key method,
> parsed in memory. No PFX/PKCS#12 loading, Windows certificate store or user-profile key container;
> do not suggest import-flag retries or changing `Load User Profile`. Follow the hosting rule in
> `The-Tech-Idea/CLAUDE.md` and IdentityServer's `CredentialFiles.RsaKey`/`PemKeyRingCertificate` owners.
> Family Needs uses `DataProtection:Key`, never `CertificatePath`/`CertificatePassword`.
> Preserve each installation's existing keys and database key ring; no new key on startup, disabled
> encryption, key-ring deletion or copying IdentityServer's private key into another application.
> This supersedes older PFX deployment examples. Authentication migration still requires each app
> to protect its own cookies and stored secrets. Check deployed builds and log timestamps.
<!-- /smarterasp-pem-rule -->

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> **Payments**: if this app ever needs to charge/checkout a customer (e.g. a paid license
> activation flow), integrate with the Beep.DoitAll Payments API over HTTP — see
> `../Beep.DoitAll/docs/payments/EXTERNAL-API-INTEGRATION-GUIDE.md` for the endpoints, auth, and
> setup steps. Never embed Beep.DoitAll in-process (it's a separate SaaS) and never hand-roll a
> vendor SDK integration directly.

## What this is

Beep.Installer is a **multi-model installer generator** written in .NET 10 (WinForms) that
targets parity with Inno Setup, ClickOnce, NSIS, WiX/MSI, etc. One `.bsetup` project file can
be built into three output shapes ("tracks"):

- **Track A** — `Setup.exe` + payload (Inno Setup / NSIS-style wizard)
- **Track B** — ClickOnce publish folder + manifests + `.deploy` files
- **Track C** — `.msix` package + AppxManifest

The **same `Beep.Installer.exe` is both the generator and the Track-A runtime.** At startup
`Program.Dispatch` picks a mode from CLI args and whether an installer script exists beside/
inside the exe (`IsRuntimeMode`): a shipped installer carries `script.bsetup` (on disk next to
the exe, or embedded as a resource), otherwise the Package Builder UI opens.

## Build, test, run

```bash
dotnet build Beep.Installer.slnx                                  # build all three projects
dotnet test Beep.Installer.Tests/Beep.Installer.Tests.csproj      # run the xUnit suite
dotnet test Beep.Installer.Tests/... --filter "FullyQualifiedName~SourceScanner"  # one class
dotnet test ... --filter "DisplayName~rebases payload"            # one test by name
dotnet run --project Beep.Installer                               # launch the generator UI
```

CLI verbs (all headless except the last two) dispatched in `Program.cs`:

```
/BUILD=<script.bsetup> [/OUT=dir] [/FORMAT=msix|msixbundle|exe] [/REQUIRESIGNED]  Build (Track A/C)
/PUBLISH=<script.bsetup> [/OUT=dir] [/UPDATEURL=url] [/NOSIGN]                    Publish (Track B / ClickOnce)
/VALIDATE=<script.bsetup>            Validate without building
/PREVIEW=<script.bsetup>            Print project summary
/S | /SILENT | /VERYSILENT [/D=path] [/COMPONENTS=a,b] [/FORCE] [/NORESTART]     Silent install (runtime)
/UNINSTALL [/D=path]   /REPAIR [/D=path]                                          Runtime maintenance
/SELFTEST                            Install→verify→uninstall in %TEMP%, exit 0 on success
/LANGMGR                             Open the standalone translation editor
```

`/CATALOG [/JSON|/MARKDOWN|/HTML]` prints the machine-readable command catalog. The catalog
(`Beep.Installer.Core/Cli/cli-catalog.json`) is the one description of every verb — options, effect,
JSON/dry-run support, MCP exposure — and is guarded against the dispatch table (`Program.Verbs`), the
argument validator (`EnterpriseCommandLine`), `PrintUsage` and `Help/cli-reference.html`
(`CliCatalogTests`). **Adding a verb means updating all of them**; after editing the catalog run
`scripts/Update-CliReference.ps1`. See `plans/enhancements/P14_CLI_AND_MCP_PLAN.md`.

`Help/cli-prompt.md` is the copy-paste command-line workflow for a project (guarded against the catalog).

**The MCP server is part of the installer:** `Beep.Installer.exe /MCP` serves MCP over stdio, and the builder's
**AI / MCP** toolbar button shows the command and registers it with Claude Desktop, Codex and Claude Code
(`McpServerDialog`, `McpClientRegistrar`). The server code is the library project `Beep.Installer.Mcp`
(`InstallerMcpHost`); it spawns the same exe per command and re-implements no verb. The rules for machine-changing
commands, credential references and dry-run live in `CliInvocationBuilder` (Core). Config for this repo: `.mcp.json`.

`/VERYSILENT`, `/SUPPRESSMSGBOXES`, `/NORESTART`, exit code `3010` are Inno Setup's grammar
on purpose — deployment scripts written for Inno (Intune/SCCM/winget) work unchanged.

## ⚠️ Critical: cross-repo project references

This repo does **not** build standalone. `Beep.Installer.csproj` and
`Beep.Installer.Core.csproj` reference sibling source repos by relative path:
`..\..\BeepDM\`, `..\..\Beep.Winform\`. Those repos must be checked out next to this one.

`DataManagementModels.csproj` is referenced **directly** (not just transitively via
`DataManagementEngine`), and `Directory.Build.props` sets `NoWarn NU1605`. This is load-bearing,
not incidental: `Beep.Winform.Controls`/`Vis.Modules` carry a `PackageReference` to
`TheTechIdea.Beep.DataManagementModels` at the *same* assembly version as the source build.
NuGet resolves nearest-to-root, so without the direct `ProjectReference` the stale package wins
and the app fails at runtime with `TypeLoadException` on newer BeepDM types. Do not "clean up"
that reference or re-enable NU1605.

## Architecture

Three projects, split by a deliberate rule — **runtime-install logic lives in BeepDM;
authoring/build/packaging lives here** (BeepDM's own scope doc excludes packaging and signing):

- **`Beep.Installer.Core`** (`net10.0`, no UI, no WinForms) — the authoring/build/packaging
  engine. `Authoring/` (`.bsetup` serialize/deserialize, source scanning, project templates,
  custom pages), `Build/` (`BuildPipeline`, payload packaging, PE payload writer), `Packaging/`
  (`ClickOnce/`, `Msix/`, `Signing/SignTool`), `Runtime/` (projects the authoring model into
  the BeepDM install context), and `Model/InstallProject.cs` (the `.bsetup` model + build
  options, an `INotifyPropertyChanged` graph).
- **`Beep.Installer`** (`net10.0-windows`, WinForms) — a **thin shell**: `Forms/` (Package
  Builder UI + runtime wizard `BeepModernInstallerForm`), `Pages/` (wizard pages implementing
  `IInstallerPage`), `Hosting/InstallWizardGraph.cs` (wires the step graph), `Engine/`
  (theme/banner/locale/rtl/accessibility helpers + `InstallerController`), `Lang/` (i18n +
  `Strings_*.resx`), `Program.cs` (mode dispatch).
- **`Beep.Installer.Tests`** — xUnit + FluentAssertions.

### The install engine is not in this repo

The actual install **steps** — `PrerequisiteCheckStep`, `FileCopyStep`, `ShortcutCreateStep`,
`RegistryWriteStep`, `VerifyInstallStep`, `UpgradeStep`, `UninstallStep`, etc. — live in
**BeepDM** under the `TheTechIdea.Beep.Installer.Steps` / `TheTechIdea.Beep.SetUp` namespaces.
This repo only *composes and drives* them. `Hosting/InstallWizardGraph.cs` is the single place
those steps are ordered into a `SetupWizardBuilder` graph — install, repair, uninstall, and
self-test each build a variant there. It used to be inline in four call sites; keep it in one
place (the shortcut create/remove paths drifted apart precisely because it wasn't).

### The two-model bridge

The authoring model (`Beep.Installer.Models.InstallProject`) is **not** what the steps consume —
they read BeepDM's `InstallConfig` from a `SetupContext`. `Core/Runtime/InstallContextBuilder`
+ `InstallConfigProjector` + `InstallContextKeys` perform that projection and populate every
context key the step graph reads. When adding a step that needs new config, wire it through the
projector/builder, not by reaching into the model from the step.

### Build pipeline seam

`BuildPipeline` does **not** hardcode `dotnet publish`; host production is behind
`Build/IInstallerHostBuilder` (`DotnetPublishHostBuilder` is the real impl; tests use a stub).
This is what makes the build path testable without spawning the SDK.

## Conventions and gotchas

- The project file extension is **`.bsetup`** (human-editable JSON: camelCase, comments and
  trailing commas allowed). Older `.plans` docs call it `.bpkg` — that name is dead.
- `.plans/` documents the *original* design and has drifted (it references old file names like
  `InstallerBuilder.cs`/`ProjectSerializer.cs` that were since renamed/split). `plans/enhancements/`
  (esp. `MASTER_TRACKER.md`) is the current, code-derived roadmap and progress log — trust it
  over `.plans/` when they disagree.
- Payload `SourcePath`s are **rebased** to `payload/` at build time and resolved against
  `AppContext.BaseDirectory` at runtime — a build-machine absolute path in a shipped config is a
  bug (this was the original P0 defect).
- Install/uninstall/upgrade run under a `RollbackManager`: `FileCopyStep` registers each file,
  and on any failure the host rolls back and restores the pre-upgrade backup. `CommitUpgradeStep`
  runs **last** so the backup is only discarded after verification succeeds.
- Headless (console) paths use the `SyncProgress` shim, not `Progress<T>` — a console host has no
  `SynchronizationContext`, so `Progress<T>` callbacks would land on the thread pool and arrive
  after `Run()` returned, dropping log lines.
- Runtime supports RTL (Arabic/Hebrew/Farsi/Urdu) and 8 UI languages via `Strings_*.resx` with a
  3-tier fallback; keep new user-facing strings in the resx, not inline.
