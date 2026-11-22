# F35 — MCP process runner

**Outcome:** a command runs exactly as at a prompt, and its outcome is returned faithfully.

**Design:** `InstallerCommandRunner` builds the invocation (F33), resolves the exe, spawns it with `ProcessStartInfo.ArgumentList`, closes stdin immediately (the child can never wait on a prompt), and captures stdout/stderr bounded at 1,000,000 characters each. Default timeout 300 s, clamped 1–3600 s; a timeout or client cancel kills the whole process tree. The exit code is returned as-is with its meaning: 0 success, 3010 success+reboot (not an error), 1 failed, 2 rejected. If `/JSON` was requested and stdout parses, it is returned as `json` (and not duplicated as text); otherwise the text is kept. A refused request returns `ran=false` with reasons and spawns nothing.

**Verification:** `McpServerTests` — exit 3010/2, JSON vs text, a 60 s command killed at its 2 s timeout, stdin never waited on, missing/wrong exe message, a refused request spawns nothing. Verified separately that a .NET parent captures this WinExe's stdout (`/VER`, `/LISTTEMPLATES /JSON`) while a Git-Bash pipe does not.

**Remaining:** the environment is inherited as-is (that is how `secret://env/NAME` resolves); no per-call environment override. Output beyond the cap is dropped and flagged `outputTruncated`.

## Update 2026-11-18
The child's default working directory is the server's `Environment.CurrentDirectory` (was the exe's folder, which broke relative paths from clients). Kill/parse catches log through `ILogger<InstallerCommandRunner>` to stderr. Test: `Runner_RunsInTheServersDirectory_NotTheExecutablesFolder`.
