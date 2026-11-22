# F32 — CLI vocabulary, usage text and reference page

**Outcome:** every documented switch runs, and the human documentation covers every verb.

**Fixes:**
- `EnterpriseCommandLine` vocabulary: added `/NOSCRIPTCMDS`, `/ALLOWSCRIPTCMDS` (flags) and `/APPINSTALLERHOURS=` (prefix). All three are read by the code and printed in the usage, yet were rejected with `BI7005` before the installer started — the same class of defect recorded for `/PUBLISHFEED` in the 2026-09-09 master-tracker log.
- `PrintUsage`: one line for `/CATALOG`. (Every other verb was already present; the guard confirms it.)
- `Help/cli-reference.html`: the hand-written tables stay; a generated "Complete command catalog" section (markers `catalog:begin/end`) is added from the catalog, regenerated with `scripts/Update-CliReference.ps1`.

**Acceptance:** every catalog option passes the validator; every verb appears in `PrintUsage` and the reference page; the generated region equals `CliVerbCatalog.ToHtml()`.

**Verification:** `CliCatalogTests` (`EveryCatalogOption_…`, `EveryVerb_IsListedInTheUsageText`, `EveryVerb_IsDocumentedInTheCliReference`, `CliReferencePage_GeneratedRegion_…`). A direct `/S /NOSCRIPTCMDS /X` run reported only `/X` as unknown.

**Remaining:** `PrintUsage` is still hand-written prose (it carries explanatory lines the catalog does not); only its coverage is guarded. The older hand-written tables on the page overlap the generated section; they were not removed.

## Update 2026-11-18
`/APPINSTALLERNOPROMPT` and `/APPINSTALLERFORCEUPDATEFROMANYVERSION` (read at `Program.cs`) were also rejected with `BI7005`; registered. `/VER` added to `PrintUsage`. Usage and reference checks now match whole tokens.
