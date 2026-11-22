# F30 — CLI command catalog

**Outcome:** one machine-readable description of every `Beep.Installer.exe` verb, read by `/CATALOG`, the help-drift guards, the reference page and the MCP server.

**Design:** `Beep.Installer.Core/Cli/cli-catalog.json` (embedded resource) is the owner of verb *description*: id, token, aliases, kind (flag/value), category, effect (`read-only`, `writes-files`, `network`, `machine-changing`, `interactive`), summary, headless, JSON and dry-run support, the value a verb takes, every option (required / secret / MCP-blocked), and MCP exposure with a reason when withheld. `CliVerbCatalog` loads it once and offers `Find`, `ToJson`, `ToMarkdown`, `ToHtml`. `Program.Verbs` remains the owner of *dispatch*; F38 ties the two.

**Acceptance:** every dispatched verb (including the positional `qualify` matrix runner) is in the catalog and the reverse; every option is accepted by the argument validator; withheld verbs state why; value verbs name their value.

**Implementation (2026-11-18):** 70 verbs. Option lists were taken from the verbs' own `ArgValue`/`Has` reads and from `PrintUsage`, not invented. Secret options: `/SIGNPASSWORD=`, `/SIGNREMOTECREDENTIAL=`, `/SDKAPIKEY=`, `/LAYOUTBEARERTOKEN=`; raw-only secrets (`/LAYOUTPROXYPASSWORD=`, `/LAYOUTHEADERVALUE=`) are blocked over MCP.

**Verification:** `CliCatalogTests` (parity, headless, kind, options, exposure, uniqueness, dry-run/JSON claims) — in `p14-cli-mcp-focused.log`.

**Remaining:** the catalog says *which* switches exist, not what values are legal; value rules stay with the exe. Adding a verb means editing `cli-catalog.json` by hand; the guards fail until it and the verb table agree.

## Update 2026-11-18
Effects now include `publishes` (`publish-sdk`); `qualify-update-channel-feed/-delta/-upgrade` are `machine-changing`; `uninstall` no longer claims `/DRYRUN`. Effects serialize as `read-only | writes-files | network | publishes | machine-changing | interactive`. Previously unlisted switches the code reads were added to their verbs' options. Guards: `Uninstall_DoesNotClaimADryRun…`, `EffectsThatNeedConfirmation…`, `EveryTokenTheValidatorKnows_IsCataloguedOrAGlobalSwitch`, `BlockedOptions_AlwaysSayWhy`.
Also 2026-11-18: `silent-install` and `repair` no longer declare `/DRYRUN` — measured, the shell's dry run still creates directories and writes registry values. Guard: `SilentInstallAndRepair_HaveNoPreview…`.
