# F37 — MCP resources

**Outcome:** reference material a client can attach without spending a tool call.

`beep-installer://catalog` (JSON), `beep-installer://catalog.md` (Markdown tables), `beep-installer://guide` (workflow, safety and exit-code summary). All read the one catalog.

**Verification:** `resources/list` observed in the smoke run.

**Remaining:** the Help site pages and plan documents are not exposed as resources (the server does not know where the repo is); no MCP prompts.

## Update 2026-11-18 — prompts
`InstallerPrompts` adds three MCP prompts: `build_installer` (validate → plan → format readiness → build), `preview_machine_change` (dry-run first, ask before `allowMachineChanges`) and `explore_commands`. They only instruct the model which existing tools to call. Test: the stdio test lists and fetches them.
