# Component Files scan failure reporting — 2026-11-23

P13 A01 continuation. The source already contains staged Explorer-file/folder drop handlers, asynchronous metadata reads, cancellation, generation/stale-draft guards, scrollable scan details and guarded Apply. Earlier audit statements describing these as absent are historical. The rejected side-by-side Edit Files redesign remains reverted; this change does not alter the screen layout or introduce another design.

## Confirmed gap and correction

Injected size, drop and Apply exceptions previously produced no diagnostic record; some exposed raw exception text in status. All three new guards failed against that source (`baseline.log`, three failed, zero passed, 4s).

The existing ComponentFilesDialog owner now records these exceptions through Diag, with a scoped correlation reference carried into the existing localized status/accessibility text. Full details remain in diagnostics. Expected cancellation and obsolete/disposed read failures are also reported rather than silently discarded, while the existing cancellation/stale-result policy still prevents draft mutation. ComponentFileScan reports expected filesystem errors; unavailable dropped paths carry a reference instead of exception text.

No parallel logger or exception store was added. Diag remains the shared diagnostic owner, with the existing ring/JSONL/file sinks and IBeepLog composition bridge. This slice does not certify the application's wider durable audit-store/admin-notification requirements or all unrelated catches. No blanket success is inferred from a log record.

## Verification

- `baseline.log`: all three new failure-observability cases failed as expected before the production correction.
- `focused.log`: normal source build and all **23** selected file scan/presentation cases passed, zero skipped, **58s**. Covers actual native drag event staging and guarded Builder modal Apply/undo/redo, detached canonical state, duplicate/conflict/missing feedback, source-relative size reads, slow/cancelled/superseded/disposed work, source-path edits, translated/accessibility feedback, minimum layout and existing lifetime/palette behavior.
- Added size/drop late-failure-after-disposal cases check that diagnostic reporting survives while canonical and detached data remain unchanged. **Final normal-source regression (`final-regression.log`): 41 passed, zero failed/skipped, 1m28s**, including modal draft-conflict checks and both late-failure cases.
- `source-hashes.log`: exact affected production/test source SHA256 inventory.
- `model-binding.log`: app/test Models match current sibling source net10.0 Debug output (`7EC8A6AB182D13E2D699C7FEBB0C5D85E4996BB02D0F5516E53A2D475E95DB23`). Direct source project binding is retained.

Tests inject inert failures or read owned fixture files; no installer payload executes. Persistent diagnostic verification uses an isolated owned directory. Existing dependency/test warnings remain. No full suite, cloud build, commit, push or publication. Actual Explorer/OLE interaction, actual OS DPI/Narrator, representative large-folder latency and wider runtime/release qualification remain open. The current plan's remaining gates are not closed by these focused checks.
