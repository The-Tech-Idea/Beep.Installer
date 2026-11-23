# P14 — Command-line catalog and MCP server

**Date:** 2026-11-18
**Status:** Stages S0–S6 implemented and verified by the focused run below. S7 (open items) is listed honestly at the end.
**Request:** "Read the documentation of Beep.Installer. Create/update CLI command prompt to use with project. Also create MCP server for it with all functionalities. Create multi stage plan. Document per feature. Update master tracker."
**Interpretation (recorded, because the phrase is ambiguous):** "CLI command prompt" is read as the `Beep.Installer.exe` command line — its verbs, switches, help and reference — not a new interactive shell. "All functionalities" is read as every CLI verb that can be driven without a person at a window (70 catalog entries; 67 offered over MCP, 3 withheld with a stated reason: `/?` (superseded by the discovery tools) and the two window-opening verbs).
**Per-feature documents:** [F30–F39](../professional-enterprise-roadmap/features/) (`F30_CLI_COMMAND_CATALOG.md` … `F39_MCP_CLIENT_CONFIGURATION.md`), following the existing `Fnn_*.md` convention.

## What the review found (S0)

Documentation read: `CLAUDE.md`, `Help/*` (esp. `cli-reference.html`), `plans/enhancements/MASTER_TRACKER.md`, P12/P13, `plans/professional-enterprise-roadmap/features/F10_ENTERPRISE_CLI.md` and `F26_HEADLESS_SDK.md`, `Program.cs` `PrintUsage`, `ProgramVerbs.cs`, `EnterpriseCommandLine.cs`, and the sibling `Beep.SimpleDraw.Mcp` server.

| Finding | Evidence | Fix |
|---|---|---|
| A verb is described in **four** hand-kept places (dispatch table, `EnterpriseCommandLine` vocabulary, `PrintUsage`, `Help/cli-reference.html`) | Reference page documented **13 of the 69 pre-existing** verbs; 56 had no entry (the 70th is the new `/CATALOG`) | One catalog (F30) + guards (F38) |
| Documented switches rejected by the validator with `BI7005` | `/S /NOSCRIPTCMDS`, `/S /ALLOWSCRIPTCMDS`, `/BUILD /APPINSTALLERHOURS=` are read by the code and printed in the usage text but were not in the vocabulary | Registered in `EnterpriseCommandLine` (same defect class as the 2026-09-09 `/PUBLISHFEED` finding in the master tracker) |
| No machine-readable verb list | Only `/?` prose | `/CATALOG [/JSON\|/MARKDOWN\|/HTML]` (F31) |
| The shell is a `WinExe`; unclear whether a parent can capture its output | Verified: with `RedirectStandardOutput` (`.NET Process`), `/VER` and `/LISTTEMPLATES /JSON` return their output and exit code. A Git-Bash pipe does **not** capture it — a shell-pipe artefact, not an exe defect | MCP spawns with redirected handles (F35) |
| No MCP server | — | `Beep.Installer.Mcp` (F34–F37) |

## Design decisions (and why)

1. **The MCP server is a thin wrapper that spawns the exe.** Almost every handler lives in the shell's `Program.cs`; re-implementing any would create a second owner of the verb. What a client gets is exactly what a person at a prompt gets, including exit codes.
2. **One catalog (`cli-catalog.json`, embedded in Core) is the owner of verb *description*; `Program.Verbs` stays the owner of *dispatch*.** A guard test ties them (F38).
3. **Few typed tools + one generic runner, not 70 tools.** Ten tools: discovery (2), generic `installer_run` (1), everyday typed flow (7). A client with 70 tool definitions spends its context on them.
4. **Safety is decided from catalog data, in one builder** (`CliInvocationBuilder`, F33), so no tool can forget it: machine-changing verbs need `dryRun=true` or `allowMachineChanges=true`; credentials must be `secret://env/NAME` / `env:NAME` references; raw-only secrets are blocked; unknown options are refused; arguments are an argv vector, never a joined string.
5. **Executable path only from an explicit setting** (`--exe` / `BEEP_INSTALLER_EXE`), never guessed.
6. **stdio transport, SDK version copied from the sibling** (`ModelContextProtocol` 1.4.1, same as `Beep.SimpleDraw.Mcp`). No HTTP transport: a local installer driver has no reason to listen on a port.

### Comparable systems (adopt / adapt / avoid)

Documented behaviour only; not an exhaustive survey.

- **Inno Setup** — `/VERYSILENT`, `/SUPPRESSMSGBOXES`, `/DIR`, `/LOG` grammar. *Adopted already* (the exe's runtime verbs); the catalog keeps those aliases as first-class.
- **MSI (`msiexec`)** — exit code 3010 = success, reboot required. *Adopted*: the MCP result treats 3010 as success, not an error.
- **WiX / `dotnet` CLIs** — `--help`-only discoverability. *Avoided*: prose help is not machine-readable; `/CATALOG /JSON` and MCP discovery tools are.
- **MCP tool annotations** (`readOnlyHint`, `destructiveHint`, `openWorldHint`) — *adopted* on every tool so a client can gate confirmation itself; the server additionally enforces it, because annotations are hints.

Sources (not re-fetched in this session; the server's `initialize` reply was observed negotiating protocol `2025-06-18`):
- MCP specification, transports and tools — https://modelcontextprotocol.io/specification/2025-06-18
- MCP C# SDK — https://github.com/modelcontextprotocol/csharp-sdk
- Inno Setup command-line parameters — https://jrsoftware.org/ishelp/ (topic "Setup Command Line Parameters")
- Windows Installer error codes (3010) — https://learn.microsoft.com/windows/win32/msi/error-codes

## Stages

| Stage | Outcome | Feature doc | Status |
|---|---|---|---|
| **S0** Discovery | Docs read; drift measured; stdout capture verified | this document | ✅ |
| **S1** Catalog | `cli-catalog.json` (70 verbs, options, effect, JSON/dry-run support, MCP exposure) + loader | [F30](../professional-enterprise-roadmap/features/F30_CLI_COMMAND_CATALOG.md) | ✅ |
| **S2** CLI | `/CATALOG` verb; vocabulary fixes; usage line; regenerated reference page | [F31](../professional-enterprise-roadmap/features/F31_CATALOG_VERB.md), [F32](../professional-enterprise-roadmap/features/F32_CLI_VOCABULARY_AND_REFERENCE.md) | ✅ |
| **S3** Invocation builder | One place that turns a request into a validated argv | [F33](../professional-enterprise-roadmap/features/F33_INVOCATION_BUILDER.md) | ✅ |
| **S4** MCP host and runner | `Beep.Installer.Mcp` project, stdio host, process runner | [F34](../professional-enterprise-roadmap/features/F34_MCP_SERVER_HOST.md), [F35](../professional-enterprise-roadmap/features/F35_MCP_PROCESS_RUNNER.md) | ✅ |
| **S5** MCP tools and resources | Discovery, generic run, typed flow, resources | [F36](../professional-enterprise-roadmap/features/F36_MCP_TOOLS.md), [F37](../professional-enterprise-roadmap/features/F37_MCP_RESOURCES.md) | ✅ |
| **S6** Guards, docs, client config | Drift guards; Help page; `.mcp.json`; tracker | [F38](../professional-enterprise-roadmap/features/F38_DRIFT_GUARDS.md), [F39](../professional-enterprise-roadmap/features/F39_MCP_CLIENT_CONFIGURATION.md) | ✅ |
| **S7** Open | See below | — | ⬜ |

## Verification (focused only, per standing instruction)

`dotnet test … --filter "CliCatalogTests|CliInvocationBuilderTests|McpServerTests|CliVerbTableTests|EnterpriseCommandLineTests"` — **108 passed, 0 failed, 0 skipped** (`p14-cli-mcp-focused.log`; filter widened with `HelpCli*` and `EnterpriseCliQualificationRunnerTests` after review). Includes the two real stdio tests (initialize → tools/list → tools/call against the real `Beep.Installer.exe`).

Guards were seen failing before the fixes: the catalog/vocabulary guard failed on the three rejected switches, the reference-page guards failed on the 57 undocumented verbs, and the stdio tests failed while the test host resolved the wrong dependency set. Manual stdio smoke (`initialize`, `tools/list`, `installer_run version`, a refused `silent-install`, `installer_list_templates`, `installer_validate` and `installer_plan` on `samples/MyApp.bsetup`, `resources/list`) all behaved as designed.

## User-facing command prompt

`Help/cli-prompt.md` is the copy-paste guide for working on a project from the command line (the loop, exit codes, safety rules, MCP pointer). `CliPromptGuide_OnlyUsesSwitchesTheCatalogKnows` fails if it names a switch the catalog lacks.

## Review corrections (2026-11-18, after an independent review)

- **Effect gate.** A new effect `publishes` (`publish-sdk`) is confirmed like a machine change. `qualify-update-channel-feed` (really installs/updates/uninstalls under `/UPDATECHANNELLIFECYCLE`), `qualify-delta` and `qualify-upgrade` are re-rated `machine-changing`. Effects are serialized as kebab-case wire names (`read-only`…) in the catalog, `/CATALOG` and the MCP tools.
- **`/UNINSTALL` ignores `/DRYRUN`.** The shell's `RunUninstall` never reads it, so the catalog no longer claims a preview; MCP requires `allowMachineChanges=true`. The shell itself was deliberately **not** changed.
- **`/ALLOWSCRIPTCMDS`** overrides the machine's script-command policy. It now works on shipped installers (validator vocabulary) but is **withheld over MCP**: a person must pass it.
- **Switches the code reads but no verb listed** are now catalogued (`/APPINSTALLER*`, `/BUILDTYPE=`, `/SOURCEREVISION=`, `/EVIDENCE`, `/SUPPORTBUNDLE*`, `/MST*`, `/SIGNATURESHA256*`, `/LAYOUTNORESUME` …). `/APPINSTALLERNOPROMPT` and `/APPINSTALLERFORCEUPDATEFROMANYVERSION` were also being rejected by the validator with `BI7005`; registered.
- **Runner** starts the child in the server's own working directory (not the exe's folder) and reports its kill/parse catches through `ILogger` (stderr).
- **Guards tightened:** usage/reference matching is token-boundary (`/VER` no longer satisfied by `/VERYSILENT`; `/VER` was missing from the usage text and was added); a reverse guard fails when the validator knows a switch no verb lists (documented global allowlist: `/RESPONSE= /CONFIG= /NOPROMPT /DIR= /INSTALLDIR= /TARGETDIR=` and generated `--os --arch --plan-hash`).

## S7 — not done, stated plainly

- **Finding (shell/engine, not changed):** `/S /DRYRUN` (and by the same path `/REPAIR /DRYRUN`) is not a preview. Run through MCP against `samples/MyApp.bsetup` with a temp `/D=`, it created the install directory and attempted `HKLM` registry writes, failing only because the process was not elevated (exit 1). Elevated, it would really install. The catalog therefore declares **no** dry-run for `silent-install` and `repair`; MCP requires `allowMachineChanges=true`. The usage line "/S /DRYRUN Compile/run checks without applying changes" in `PrintUsage` is wrong and is left for the owner of that engine. The temp directory it created was removed. Root cause found and half fixed (2026-11-18): `SetupContext.Options` defaults to a non-null `new SetupOptions()` and the engine lets the context's options win, so the wizard's `DryRun` was ignored. `CoordinatedInstallerWizard.Execute` now sets `context.Options = inner.Options` (one owner, every caller). Reproduced and guarded by `SilentInstallDryRunTests`; registry and file copy now honour `DryRun`. **Still open, in BeepDM (not edited):** `DirectoryCreateStep` and `VerifyInstallStep` do not check `DryRun`, so the folder is still created and Verify then fails on files that were not copied. The catalog therefore keeps declaring no dry-run for `silent-install` and `repair`.
- **Global switches are intentionally uncatalogued** (`/RESPONSE=`, `/CONFIG=`, `/NOPROMPT`, `/DIR=` aliases); they are not offered as options.
- **One machine-changing verb has run for real through MCP:** `selftest` (install, verify, uninstall per-user inside `%TEMP%`, exit 0, `allowMachineChanges=true`), in the stdio test. Anything touching Program Files, HKLM or other real installs has not been run; the one attempt, `/S /DRYRUN`, is the finding above. `silent-install`, `uninstall`, `repair`, `apply-delta`, `msi` … are covered by the builder's refusal rules and by the exe's own suites.
- **Run end to end through MCP on the sample project (exit 0 or 1, never rejected, no timeout):** `qualify-plan`, `qualify-config`, `qualify-diagnostics`, `qualify-security`, `qualify-sdk`, `qualify-cli` (`Stdio_ScriptQualificationCommands_RunEndToEndOnTheSampleProject`), plus `validate`, `plan`, `preview`, `format-readiness`, `list-templates`, `version`, `selftest`.
- **Not every verb has been executed end-to-end through MCP.** The builder is proven able to compose a valid argv for every exposed verb (`EveryExposedVerb_CanBeBuiltFromItsOwnRequiredInputs`), and the exe validates its own arguments, but most qualification verbs were not run.
- **Value-level argument checks are the exe's.** The catalog says *which* switches exist, not what each value may contain; the exe answers exit 2 for a bad value.
- **Windows only.** The server drives `Beep.Installer.exe`; the server itself is `net10.0` but no cross-platform run was attempted.
- **Not in a release/package.** The MCP project is in the solution and has a `.mcp.json`; it is not yet packed (e.g. as a `dotnet tool`) or documented in a release checklist.
- **MCP prompts** (guided "build and sign" workflows) are not provided; the `beep-installer://guide` resource is the only prose.
- **Existing tracker/doc statements** in `F10`/`F26` predate the catalog and are left as history.
- Nothing was committed or pushed; the working tree already held a large uncommitted body of P13 work, and these changes are additive to it.

## Local install — 2026-11-18

- `scripts/Publish-Installer.ps1` publishes the self-contained exe to `dist/Beep.Installer` (git-ignored) and the MCP server to `%LOCALAPPDATA%\BeepInstallerMcp`. Shortcuts `Beep Installer.lnk` sit on the desktop and in the repo root.
- Registered as `beep-installer` in Claude Desktop (`claude_desktop_config.json`), Codex (`~/.codex/config.toml`) and Claude Code (user scope). Backups of both files were taken as `*.bak-<timestamp>`.
- Evidence: `claude mcp list` reports the server **Connected** (a live Claude Code client). Claude Desktop and Codex need a restart; they were not opened to confirm.
- The repo's `.mcp.json` still points at Debug paths, so inside this repo Claude Code sees two definitions of `beep-installer` and warns; the project one wins.
- Live client call (2026-11-18): a headless Claude Code run (`claude -p`, run from `%TEMP%` so the user-scope entry applied) called `installer_run version` and `installer_list_commands` on the installed server and got `Beep Installer v1.0.0` and 67 commands.

## Architecture change — the server is part of the installer (2026-11-18)

Requirement restated by Fahad: the MCP server must be part of the app, runnable from the command line or from the app menu.

- **Command line:** `Beep.Installer.exe /MCP` (catalog verb `mcp`, 71 verbs, 67 offered over MCP; `help`, `update-ui`, `lang-manager` and `mcp` itself are withheld).
- **App menu:** **Project → AI / MCP** in the advanced builder, or **Tools → AI / MCP server…** in Guided, opens `McpServerDialog`, which shows the command and registers the installer with Claude Desktop, Codex and Claude Code through `McpClientRegistrar`.
- **Code:** `Beep.Installer.Mcp` is a library hosted by the shell (`InstallerMcpHost`); the separate server exe, `--exe` and `BEEP_INSTALLER_EXE` are no longer how it is started. `.mcp.json`, the three local clients, `Help/mcp-server.html`, F34 and F39 were updated.
- **Evidence:** the stdio tests start `Beep.Installer.exe /MCP`; `claude mcp list` shows the published `dist` exe Connected and a headless Claude Code call returned `Beep Installer v1.0.0`. `McpClientRegistrarTests` cover the Claude Desktop JSON and Codex TOML writes on temp files. The dialog was built and shown by the existing dialog/builder render tests only; it was not clicked through by a person.

## Menu placement and icon (2026-11-18)

- Current placement (2026-11-23): the advanced Package Builder offers **Project → AI / MCP**, keeping its compact toolbar for validation/build/preview/package/signing/publish. This supersedes the earlier crowded-toolbar placement. The guided wizard still has **Tools → AI / MCP server…** (Ctrl+Shift+M) and its header button. Guard: `McpEntryPointTests`; normal-source menu-route regression passed 92 checks (`design-evidence/menu-bar/menu-route-regression.log`).
- `Beep.Installer.exe` carries an icon (`Beep.Installer/Resources/beep-installer.ico`, an abstract package box with a download arrow; no people). `dist/Beep.Installer` was republished with it and with the `/MCP` server.

- 2026-11-18: MCP gained `installer_get_project_fields` / `installer_set_project_fields` (`ProjectFieldEditor`, tests in ProjectFieldEditorTests). Collections (files, components, shortcuts, registry) remain builder-only.
