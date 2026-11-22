# F33 — Invocation builder

**Outcome:** one place turns "run this command with these inputs" into a validated argument vector, or says why not.

**Design:** `CliInvocationBuilder.Build(catalog, request)` in Core. Rules: command must exist and be MCP-exposed; value verbs need their value, flag verbs refuse one; option names are accepted as `OUT`, `/OUT`, `/OUT=`; unknown options are refused listing the known ones; required options are enforced; value options need a value; credentials must match `secret://env/NAME` or `env:NAME` and the refusal never echoes the value; MCP-blocked options are refused; `/PROPERTY:` pairs only where the verb takes them; `/DRYRUN` only where honoured; machine-changing verbs need `dryRun` (if supported) or `allowMachineChanges`; `/JSON` is appended when supported and not declined; finally the same `EnterpriseCommandLine.ExpandAndValidate` the exe uses. Output is a `string[]`, never joined, so a path with spaces or quotes cannot become a second argument.

**Acceptance / Verification:** `CliInvocationBuilderTests`, including `EveryExposedVerb_CanBeBuiltFromItsOwnRequiredInputs`.

**Remaining:** the 3010/exit semantics live in the MCP runner, not here. Secrets are only checked for *shape*; an `env:NAME` naming an unset variable is the exe's to report.

## Update 2026-11-18
Confirmation now follows `CliEffect.NeedsConfirmation()` (`machine-changing` and `publishes`). The refusal says "publishes to an external feed" or "changes this machine", and "has no dry-run" for verbs without `/DRYRUN` (`uninstall`, `rollback-delta`). Tests: `Uninstall_HasNoDryRun…`, `PublishSdk_NeedsDryRunOrAllowance…`.
