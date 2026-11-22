# F36 — MCP tools

**Outcome:** ten tools reach every exposed command without flooding a client with 70 definitions.

| Tool | Purpose | Annotations |
|---|---|---|
| `installer_list_commands` | catalog listing, filter by category/effect | read-only |
| `installer_describe_command` | one command's options, effect, exit codes | read-only |
| `installer_run` | **any** catalog command; the safety rules of F33 apply | destructive, open-world |
| `installer_validate`, `installer_plan`, `installer_preview`, `installer_format_readiness`, `installer_list_templates` | the everyday read-only flow | read-only |
| `installer_build`, `installer_publish_clickonce` | write artifacts to a chosen directory; never accept a credential (signing comes from the project) | non-destructive |

The typed tools are thin: they build a `CliInvocationRequest` and call the same runner; no tool re-implements a verb. Annotations are hints for the client; enforcement is the builder's.

**Verification:** `McpServerTests.Stdio_InitializeListAndCall_…` asserts all ten names and live calls of several; the manual smoke ran `installer_validate` and `installer_plan` on `samples/MyApp.bsetup` (exit 0, structured JSON).

**Remaining:** only `installer_run` reaches the other exposed commands; typed tools for further flows (delta, update channels, evidence) are a possible addition, not a coverage gap.

## Update 2026-11-18
`installer_run` documents `workingDirectory` defaulting to the server's directory. Effect filters accept `publishes`. `/ALLOWSCRIPTCMDS` cannot be set through any tool.
