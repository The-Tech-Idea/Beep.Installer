# Beep.Installer command prompt for a project

Copy this into a terminal session or an AI assistant's instructions to work on a `.bsetup` project from the command line.
Every command below is described in full by `Beep.Installer.exe /CATALOG` (JSON, `/MARKDOWN` or `/HTML`) and in `Help/cli-reference.html`.

## The loop

```powershell
$exe = ".\Beep.Installer\bin\Release\net10.0-windows\Beep.Installer.exe"
$p   = "MyApp.bsetup"

& $exe /VALIDATE=$p /STRICT /JSON          # lint, schema, policy; exit 1 means findings
& $exe /PLAN=$p /JSON                      # compile the install plan, installs nothing
& $exe /FORMATREADINESS=$p /JSON           # which of EXE, MSI, MSIX, WinGet are releasable
& $exe /BUILD=$p /OUT=.\out /SBOM /PROVENANCE /REQUIRESIGNED
& $exe /PUBLISH=$p /OUT=.\clickonce /UPDATEURL=https://example.com/app/   # ClickOnce
```

Exit codes: `0` success, `3010` success but reboot required, `1` the operation failed, `2` the command line was rejected (check spelling with `/CATALOG`).

## Rules

1. Find a command with `/CATALOG /MARKDOWN`; never guess switch names.
2. `/S`, `/REPAIR`, `/UNINSTALL`, delta apply, MSI and the qualification runners that install can change the machine. Run them only on a disposable machine or VM, and only when asked. `/S /DRYRUN` and `/REPAIR /DRYRUN` are **not** safe previews: they still create folders and write the registry.
3. Credentials go in as `secret://env/NAME` or `env:NAME`, never as literal values.
4. Add `/JSON` and read the structured result rather than scraping text.
5. Run focused checks only: validate and plan before build, and the narrowest test filter when testing.

## Through MCP

`Beep.Installer.Mcp` offers the same commands to an AI client (`.mcp.json`, `Help/mcp-server.html`). Use the `build_installer` prompt for the loop above; machine-changing commands need an explicit `allowMachineChanges=true`.

## Start it as an MCP server

`Beep.Installer.exe /MCP` runs the installer as an MCP server over stdio. In the app, the toolbar's **AI / MCP** button shows this command and adds it to Claude Desktop, Codex and Claude Code.
