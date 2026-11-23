# Clean creation baseline and authored identity — 2026-11-23

UX01/UX11 follow-up to the observed Save-first prompt on an untouched fresh launch. The controller's generated placeholder was dirty, while the Guided creation view compared its displayed defaults against a different placeholder publisher. Template-only edits were also absent from the comparison.

## Owner changes

- InstallerController marks only its generated empty-shell placeholder clean. Supplied projects and explicit New transactions retain their authored dirty state.
- BuilderWizardForm captures the initial displayed creation fields/template identity once. Fresh draft comparisons use that baseline; language refresh cannot reset it. Existing-project comparisons continue against their canonical fields. Template selection refreshes save status, including template-only edits and reversions.
- Already-authored dirty projects are recognized when entering Guided even if they have no files/resources yet, preserving their name/version/AppId instead of treating them as a new placeholder.
- The existing close-confirmation policy remains Yes/No/Cancel. An internal prompt seam supports explicit owned Cancel/Discard checks; the normal path still shows the existing native message box. There is no new dialog/layout/theme or second draft/creation engine.

## Verification

- `focused.log`: first 13 cases passed, zero skipped, 56s. Covers fresh controller/view state, all four field edits and template selection/reversion across eight cultures, canonical-data retention, Cancel/Discard, supplied/explicit project dirtiness, plus actual composed startup with normal Close.
- `reverted-behavior-negative.log`: restoring the former placeholder dirtiness and canonical-field comparison made six guards fail; three invariant cases still passed, zero skipped, 6s.
- `authored-identity-negative.log`: removing authored-blank-project recognition failed the new identity guard, one failed, 7s.
- `restoration.log`: both fixed owners restored byte-for-byte with fresh write timestamps before verification.
- `final-regression.log`: **63 passed, zero failed/skipped, 1m08s** against the restored source. Includes actual Main/Dispatch/Application.Run and ordinary Close (no forced MarkClean/Dispose cleanup), Guided creation/navigation/recovery/presentation, save/recovery state and authoring history.
- `source-hashes.log` identifies the tested dirty source; `model-binding.log` confirms app/test Models match sibling source net10.0 Debug output. Direct source project binding remains.

The composed-entry fixture now counts any unexpected fresh close confirmation, safely discards only its owned fixture if a regression occurs, and fails the guard if any prompt was requested. Separate Cancel/Discard tests preserve uncommitted text and canonical JSON. They inject the decision; they do not prove real mouse/keyboard selection of native dialog buttons or the Yes/save-file-picker journey.

No authored payload or installer runs. Existing dependency/test warnings remain. Actual physical input, file-picker/save interaction, OS DPI/Narrator, representative authors and installed runtime/recovery acceptance remain open. No full suite, cloud build, commit, push or publication.
