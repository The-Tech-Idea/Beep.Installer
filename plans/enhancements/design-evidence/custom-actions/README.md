# Custom Actions presentation — 2026-11-23

Scope: P13 A02, in the existing CustomActionsDialog and shared custom-action validator. The rejected Edit Files design remains reverted.

## Visual target and comparison

The Image Gen [target](target-2026-11-23.png) and [prompt](prompt-2026-11-23.txt) were generated and shown before implementation. The [implemented render](implemented.png) is an owned WinForms DrawToBitmap fixture, not a capture of the user's running app. It preserves the target's command/timing list, aligned selected-action fields, compact add/remove icons, and single OK/Cancel footer. Existing native body fonts and semantic theme colors make the actual screen denser than the illustrative target. Native window chrome, contrast-selected OK text and the existing circle-X cancel glyph differ. This is structural comparison, not pixel-perfect or accessibility acceptance.

New add/remove resources are actual currentColor SVG vectors. Existing BeepIcons renders them; no raster-in-SVG wrappers, human imagery or new asset service were introduced.

## Behavior

All nine action properties remain editable in detached drafts. Only accepted OK applies. Add starts with an empty required path. Invalid timing and negative timeout remain visible for correction. Shared diagnostics now carry row/property metadata, including missing rows and unknown timing; blocking errors select and focus the exact field with inline feedback. Warning-only drafts retain Yes/No confirmation. Wrappers, timing captions, counts, tooltips and accessible captions refresh across eight languages/RTL without changing authored data. Theme and language subscriptions detach on disposal.

The last-row removal defect was fixed at its owner: focus moves to the stable Add control before the binding list removes the focused editor's row. No exception is swallowed. The native fixture records and rethrows unexpected WinForms exceptions rather than waiting behind an interactive exception popup.

## Verification

- `build.log`: source app build, zero errors, six warnings, 26.6s.
- `focused-tests.log`: 11 passed, six failed; exposed null-description normalization, stale translated selected timing and clipped minimum-size footer.
- `focused-corrections.log`: 14 passed before an aborted run; not counted as a successful batch.
- `empty-and-minimum-diagnostics.log`: one passed, one failed; captured the actual CurrencyManager/DataGridView index -1 exception on removing the final row.
- `last-row-focus-fix.log`: final-row add/remove/cancel and translated timing case passed, 7s.
- `expanded-regression.log`: final normal-source focused regression, **77 passed, zero failed/skipped, 1m32s**. Includes actual dialog correction paths, warning decisions, all cultures, minimum/enlarged layout, theme/listener lifetime, modal conflict, language choice, diagnostic editing, validation counts and resource coverage.
- `model-binding.log`: app/test Models SHA256 matches sibling source Debug output (`7EC8A6AB182D13E2D699C7FEBB0C5D85E4996BB02D0F5516E53A2D475E95DB23`). Direct source project binding is retained.

The named timing switch is exhaustive over named enum members, with CS8509 treated as an app build error. Unknown numeric values are handled before that switch; compiler warning CS8524 remains, together with existing dependency/test warnings. No warning suppression was added for this work.

No authored command was launched. A focused marker fixture verifies editing/applying command metadata does not execute it. This local metadata editor adds no accounts, network service, payments, telemetry, tracking or external imagery. Whole-product release/privacy/legal acceptance is not inferred from this slice. Actual OS DPI, Narrator, full keyboard qualification, A01/A04 and remaining P13/release gates remain open. No full suite, cloud build, commit, push or publication.

## Design references checked

- [Advanced Installer custom actions](https://docs.advancedinstaller.com/custom-actions.html)
- [Inno Setup Run section](https://jrsoftware.org/inno-setup/documentation/topic_runsection.htm)
- [Microsoft dialog guidance](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs)

Checked 2026-11-23. These informed command metadata/timing presentation, inline correction and clear acceptance/cancellation. They do not authorize executing authored commands in the editor.
