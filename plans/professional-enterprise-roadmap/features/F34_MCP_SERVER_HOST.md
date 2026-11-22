# F34 — MCP server host

**Outcome:** `Beep.Installer.Mcp`, a stdio MCP server, in the solution.

**Design:** `net10.0` console project; `ModelContextProtocol` 1.4.1 (same as `Beep.SimpleDraw.Mcp`) plus `Microsoft.Extensions.Hosting`; references Core only (not the WinForms shell). `Host.CreateApplicationBuilder`, console logging forced to stderr so stdout carries only protocol, `AddMcpServer().WithStdioServerTransport().WithTools<…>().WithResources<…>()`. Executable path: `--exe <path>` or `BEEP_INSTALLER_EXE` (`InstallerExecutable`), resolved per call so discovery tools work without it and the error names both settings.

**Acceptance:** the server starts without the exe; `initialize` returns `serverInfo.name = Beep.Installer.Mcp`; nothing but JSON-RPC on stdout.

**Verification:** `McpServerTests.Stdio_*` and the manual smoke (`initialize` → `tools/list` → `tools/call`). The test project's package graph does not carry the server's hosting dependency, so the stdio tests run the server from its own build output, as it ships.

**Remaining:** no HTTP transport (deliberate); no packaging as a `dotnet tool`.

## Update 2026-11-18 — hosted by the installer
`Beep.Installer.Mcp` is now a library: `InstallerMcpHost.RunAsync(installerExe, ct)` is called by `Beep.Installer.exe /MCP` (catalog verb `mcp`, not offered over MCP itself). The shell references the library, so the self-contained installer carries the server. The standalone `Beep.Installer.Mcp.exe` was removed. Stdio tests start `Beep.Installer.exe /MCP`.
