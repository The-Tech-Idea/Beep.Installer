# F38 — Drift guards

**Outcome:** the places a verb is written down cannot disagree silently.

`CliCatalogTests` asserts: dispatch table ⇔ catalog (tokens and aliases, headless flag, flag/value kind); every catalog option and verb token passes `EnterpriseCommandLine`; interactive verbs are never exposed and non-headless means interactive; withheld verbs state a reason; ids and tokens are unique; `/DRYRUN` and `/JSON` claims match declared options; `PrintUsage` mentions every verb; `Help/cli-reference.html` mentions every verb and its generated region equals `CliVerbCatalog.ToHtml()`. The existing `CliVerbTableTests` (offset/shadowing) and `EnterpriseCommandLineTests` stay.

**Seen failing:** before the vocabulary fix the option guard reported three switches; before regeneration the reference guards listed 57 undocumented verbs.

**Limits (what these cannot see):** they compare *names*, not behaviour. A catalog option the exe silently ignores would still pass (the "accepted-then-ignored" defect class needs a per-verb behavioural test). The usage-text guard reads `Program.cs` source text rather than running `/?`.

## Update 2026-11-18
Added: token-boundary matching for usage/reference guards (limit: a token embedded in prose still counts if bounded by non-alphanumerics); reverse vocabulary guard via reflection on `EnterpriseCommandLine.KnownFlags/KnownPrefixes` with a documented global allowlist (limit: it depends on those private member names; it fails loudly if renamed).
