# F31 — `/CATALOG` verb

**Outcome:** the command line can describe itself as data.

**Design:** `Beep.Installer.exe /CATALOG [/JSON | /MARKDOWN | /HTML] [/OUT=<file>]`. No format prints a compact id/category/summary list; `/OUT=` writes the text to a file instead of stdout (which also lets the WinExe shell be driven without a console). Implemented as one `CliVerb.Flag` in `ProgramVerbs.cs` (`RunCatalog`) over `CliVerbCatalog`; no logic of its own.

**Acceptance:** output equals the catalog's own rendering; `/OUT=` works under `Start-Process`; the new switches are in the validator vocabulary (`/CATALOG`, `/MARKDOWN`, `/HTML`).

**Verification:** `scripts/Update-CliReference.ps1` ran `/CATALOG /HTML /OUT=` against the built shell to regenerate the page; `CliCatalogTests.CliReferencePage_GeneratedRegion_MatchesTheCatalog` then passed.

**Remaining:** no `--catalog` modern alias (the `--` aliases are a closed list in `EnterpriseCommandLine.NormalizeAliases`); no response-file key.
