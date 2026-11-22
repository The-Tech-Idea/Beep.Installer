# F39 — MCP client configuration

**Outcome:** the server is usable with this project from Claude Code and any stdio MCP client.

Repo-root `.mcp.json` (project scope):

```json
{ "mcpServers": { "beep-installer": {
    "command": "Beep.Installer/bin/Debug/net10.0-windows/Beep.Installer.exe",
    "args": ["/MCP"] } } }
```

Paths are relative to the project root; build first (`dotnet build Beep.Installer.slnx`, or the two projects). To point at another build set `BEEP_INSTALLER_EXE` instead of `--exe`. Signing credentials are passed as `secret://env/NAME` and read from the server's environment.

Documented for people in `Help/mcp-server.html`.

**Remaining:** Debug-output paths only; a Release/published layout needs its own entry. Not verified inside a live Claude Code session in this work (the stdio protocol was verified with scripted clients).

## Update 2026-11-18 — the server is the installer
`Beep.Installer.exe /MCP` is the server; there is no separate server exe, `--exe` or `BEEP_INSTALLER_EXE` to set. `McpClientRegistrar` writes the same entry into Claude Desktop (`claude_desktop_config.json`), Codex (`config.toml`) and Claude Code (`claude mcp add --scope user`), backing up files first. The builder's **AI / MCP** toolbar button (`McpServerDialog`) shows the command and runs the registrations. Tests: `McpClientRegistrarTests`.
