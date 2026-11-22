# P13 remaining presentation audit

**Reviewed:** 2026-10-03. **Scope:** existing UX06–08 and UX11–12 acceptance criteria and UI design rules.
**State:** A05 and A06 implemented with native baselines and focused passing checks. A03 now has live captions/status/palette/layout, grid binding, whole-component save validation and preview localization with selected native evidence; broader keyboard/accessibility/actual-DPI qualification remains. A01 has partial live presentation/layout/picker implementation; staged drop and asynchronous metadata work remain. A02 and A04 still require implementation/native qualification.

The fresh full suite finished successfully against its compiled implementation: 2,045 passed,
zero failed, four explicitly skipped end-to-end cases, 2,049 total in 31m24s
(`p13-async-review-current-full-suite.log`). This audit did not modify production or test source
during the run. The preceding 203 selected checks, whole-solution build and models binding passed.
The post-run inventory finds matching app/test models copies but no matching current sibling Debug
output, following source-output rebuilds during the run. A subsequent whole-solution source build
passed with zero errors and 20,152 warnings in 59.94 seconds
(`p13-post-full-current-source-build.log`). Its follow-up inventory still has zero source matches:
app/test copies are dated 11:48:52 UTC, with sibling outputs rebuilt at 11:51–11:52 UTC
(`p13-post-full-current-model-binding.log`). Current binding requires stable outputs and a new check.
Neither the suite nor this source audit closes the skipped cases or real Windows qualification.

## A05 implementation evidence

Actual Next/jump baseline checks failed all four cases: missing-name focus stayed on the action,
and component errors stayed on Source (`p13-guided-blocking-focus-baseline.log`). The first fixes
passed four checks. Expanded checks then failed three cases (34 passed): blank creation version
was defaulted and an earlier-source ClickOnce blocker stayed on Package
(`p13-guided-blocking-focus-expanded.log`). Both are corrected.

Guided now focuses missing name/version before canonical creation/mutation, selects the existing
tab and exact diagnostic row/property, or routes to an earlier Guided field when a package check
requires it. It retains the blocking message and authored data. Empty required components focus
Add; files-only remains explicit. The required-version caption ships in eight cultures.
All 39 focused cases passed (20s); the normal-source expanded regression passed all 143 checks,
no skips, in 2m57s (`p13-guided-blocking-focus-source-regression.log`), including warning navigation,
language refresh, modal focus and asynchronous Review. Final app/test models hashes match the
current sibling net10.0 Debug output (`p13-guided-blocking-focus-model-binding.log`). This resolves
the earlier mismatch for this selected batch. The table below retains original baseline findings;
A05 is implemented, while real OS DPI/Narrator and broader release gates remain open.

## A06 implementation evidence

Six actual modal cases accepted OK after project replacement or edited-target changes, failing
the pre-close rejection baseline (`p13-modal-draft-conflict-native-baseline.log`). The baseline
fixture cancels after assertions, so this is not a claim of observed persisted data loss. New
callback properties required WinForms serialization annotations before the initial source build
could pass. All six rejection cases then passed.

Actions, Conditions and Files now capture project origin and serialized edited-target state before
opening. Conditions also protect component references/order/IDs; Files protect membership and
source/size context. Draft bindings commit before checking and applying. Compatible Apply remains
one undo transaction and preserves unrelated changes. A conflict keeps the draft open with
DialogResult.None and an accessible warning explaining how to retain values and reopen safely.
The warning wraps, refreshes in all eight cultures/RTL and light/dark palettes, and detaches its
subscriptions. Whole-dialog presentation remains under A01–A03. File-size reads precede canonical
collection replacement. No fixture payload runs.

All 16 focused checks passed (25s), including current/replacement/target-change cases, Apply/undo/
redo, unrelated edits, feedback/listener lifetime and an actual completed background source scan
while a file draft is open. An earlier expanded run had three incorrect Panel-color assertions
(13 passed), corrected to the existing shared Shell role for labels directly on a Form. Final
normal-source regression passed 209 checks, no skips, in 2m12s
(`p13-modal-draft-conflict-source-regression.log`). Whole-solution build passed with zero errors,
11,592 warnings, in 38.10s; app/test models match current sibling net10.0 Debug output
(`p13-modal-draft-conflict-whole-solution-build.log`, `p13-modal-draft-conflict-model-binding.log`).
The earlier full suite predates A05/A06. Large-target latency and actual Windows/manual/runtime
gates remain open; A01–A04 are the remaining source-audit implementation items.

## Baseline findings and required work

The table records the original audit. A05 and A06 implementation evidence is recorded separately;
the four remaining dialog presentation items are A01–A04.

| ID | Finding and source | Required change | Evidence required |
|---|---|---|---|
| A01 | [ComponentFilesDialog](../../Beep.Installer/Forms/ComponentFilesDialog.cs) has an English `Edit files — {name}` title and `Total: … MB across … file(s)` summary. Its captions are construction-time lookups; no live language binding exists. The editor uses fixed caption columns, fixed action heights and an unlocalized field picker. Its tip advertises file/folder drag-and-drop, but the source has no AllowDrop setup or drag/drop handlers. | Localize the title, captured size/count summary and picker labels. Bind live captions and accessibility names without resetting the selected draft file, typed values or text selection. Use wrapping/preferred-size action/field layouts and shared palette/type roles. Implement the advertised file/folder drop as a staged draft operation with existing scanner/path rules and responsive cancellation. | Actual populated dialog across all eight cultures, minimum size and enlarged translated captions; retained draft/canonical file objects, selection/focus, picker arguments, captured component attribution, palette and listener disposal. Owned file/folder drop fixtures must verify path mapping, duplicate/missing/cancelled requests and Apply-only-on-OK without executing payloads. Actual Explorer/OLE, OS DPI and large-folder latency remain separate. |
| A02 | [CustomActionsDialog](../../Beep.Installer/Forms/CustomActionsDialog.cs) retains English Save-error/warning prompt wrappers and an English action-count status prefix. Its editor rows are fixed at 28 pixels and its validation/footer surfaces use fixed heights/colors. It has no live language or authoring palette binding. | Localize UI wrappers/counts while retaining engine issue text and keys. Give each field a stable accessible caption, prefer automatic row/action sizing and apply semantic theme roles. Preserve the selected detached action, field drafts and existing error/warning Save policy. | Native populated actions, real Add/Remove/editor bindings, blocking errors and warning confirmation through injected prompt results, all eight cultures, selected row/caret/value retention, minimum/enlarged layouts, palette and event disposal. Do not launch an authored action. |
| A03 | [ComponentConditionsDialog](../../Beep.Installer/Forms/ComponentConditionsDialog.cs) passes English component/count wrappers into [ValidationCounts](../../Beep.Installer/Ui/ValidationCounts.cs). The helper emits English error/warning suffixes and fixed DarkRed/DarkOrange/DarkGreen colors. Conditions fields and validation surfaces need the same live caption/layout audit. | Use localized status presentation and semantic palette roles. Preserve component selection, conditions, expression and preview during caption refresh. Keep validator messages and typed blocking policy intact; UI language must not execute condition evaluation. | Populated multi-component/native condition cases, exact retained selections/expressions, error/warning/count captions in all cultures, palette/contrast structure, minimum/enlarged layouts and detached subscriptions. Actual machine-condition evaluation is a separate action. |
| A04 | [LanguageManagerForm](../../Beep.Installer/Forms/LanguageManagerForm.cs) uses the same `LangMgr_Language` key for Add and Remove, and the same `LangMgr_Key` key for Add/Remove Key. English resources define those keys as `- Language` and `- Key`, so the Add captions resolve to the removal captions. It also has fixed-position toolbar buttons, private fonts/colors and English dynamic statuses. | Give distinct actions distinct keys. Refresh UI captions/status wrappers independently of the language being edited, preserving unsaved cells, grid selection and preview. Use preferred/wrapping layout and shared appearance. Keep file Save/Import/Export behavior distinct from UI-language selection. | Native Add/Remove captions in all eight cultures; repeated culture changes while a translation cell is being edited; unchanged selected editor language, dictionaries, caret/selection and draft content; minimum/enlarged layouts and subscription cleanup. Any file-write test must use owned temporary destinations rather than shipped translation files. |
| A05 | [BuilderWizardForm.NavigateTo / CanAdvancePastCurrentStep / TryCreateProject](../../Beep.Installer/Forms/BuilderWizardForm.cs) reports a blocking message and returns, without explicitly routing focus to the blocking diagnostic field. `ActiveSectionDiagnostics` collects typed errors but does not focus them. UI design rule 4 explicitly requires focus to the first blocking field. | Reproduce the missing-name and typed section/collection cases, then route the blocking path to its existing exact editor navigation. Retain the explanatory message and authored values; do not turn warnings into blockers or perform an unnecessary Advanced handoff. | Actual Next/forward-jump controls with focus initially on the action, verified current step/tab/exact field or row after blocking, unchanged project identity/data, translated wrapper/accessibility and warning navigation. Source alone is not a native reproduction. |
| A06 | [PackageBuilderForm modal callers and Apply helpers](../../Beep.Installer/Forms/PackageBuilderForm.cs) do not uniformly guard the originating project and edited collection state. Custom Actions applies its detached results to the current `_project`; Conditions compares current component count/IDs; Files checks component membership. Neither count/IDs nor membership verifies an unchanged edited collection. | Reproduce project replacement with matching component IDs and concurrent changes to an edited collection while a modal holds its draft. Guard Apply against the originating project and conflicting target changes. Explain stale/conflicting state while retaining the staged values for inspection/recovery, rather than applying them to another project or silently overwriting newer data. Compatible Apply must remain one undo transaction. | Actual owned modal entry points, controlled project replacement and target changes before OK, unchanged new/current model and history on rejection, retained draft values, truthful feedback and successful current Apply/undo. Include completed source scans while a file draft is open; no payload execution. These races are source concerns requiring native reproduction, not claimed observed data loss. |

These items implement existing plan requirements; they do not replace the twenty-feature scope.
The order above groups shared modal work. A05 and A06 correctness changes now have native evidence.
Keep each failing baseline and final regression
separate so a passing narrow check does not qualify unrelated dialogs.

## Current evidence limits

- `EveryDialogRendersTests` uses an explicit eighteen-dialog factory list and checks construction,
  in-place buttons, empty removal and accessible names. It does not establish all-eight-language
  live refresh, populated long-caption layout, palette lifetime or all new dialog coverage.
  AuthoringLanguageDialog currently has its own native action/lifetime cases; decide whether to add
  it to the shared rendering inventory when that inventory is extended.
- Preserve the existing detached file/action drafts and Apply-only-on-OK behavior. Source inspection
  confirms ComponentFilesDialog clones file objects, and its caller applies ResultFiles only on OK;
  this audit does not claim a Cancel mutation defect.
- Shared appearance tokens do not prove resource lifetime. InstallerTheme's font accessors construct
  Font objects; any lifetime claim needs repeated-window/theme evidence and explicit ownership review.
  This is an unqualified ownership question, not a claimed measured GDI leak.
- Real Windows 100/125/150/200% DPI, Narrator/Accessibility Insights, representative author sessions,
  controlled installer journeys, the four explicitly skipped end-to-end cases and large-project
  responsiveness remain the original separate gates. Native font/layout checks cannot replace them.

See [P13](P13_UI_UX_FEATURE_ENHANCEMENT_PLAN.md) for complete acceptance criteria and implementation
history, and the [concise feature review](UI_UX_REVIEW_2026-10-03.md) for the prioritized feature list.

## A03 current implementation evidence — partial

Native baselines reproduced an empty populated grid (missing DataSource) and a stale English
window title after French selection. Current editor binds/selects/edits real condition rows and
refreshes after removal; CommandReturns exposes Value2. Explicit keyed captions, accessible field
names, help and grid headers refresh without replacing controls/drafts. Status counts use localized
resources and shared semantic colors. The form owns/disposes its shared-derived body font, binding
and language/theme subscriptions. Selector/field/status/action rows prefer content size; actions
and help wrap, with a minimum window and scrollable editor. Four populated native cases cover all
eight cultures, caret/focus/row/component/expression and model preservation, clean/error/warning
palette states, lifetime and enlarged multiline captions. An inert command-marker fixture proves
these caption changes do not evaluate that condition. All 39 expanded source checks passed in 51s
(`p13-conditions-presentation-expanded.log`). Broad normal-source regression passed all 206 checks,
no skips, in 2m49s (`p13-conditions-presentation-source-regression.log`); final app/test models
copies match current sibling net10.0 Debug output (`p13-conditions-presentation-model-binding.log`).

A03 stays open: UI-owned preview descriptions remain English. A localized UI renderer must retain
rule facts and verbatim validator messages/indices, preview selection and draft state, without
machine evaluation. Actual owned-modal focus and full keyboard/accessibility/OS DPI scope need
qualification. Selected checks do not close those gates or the other three dialog implementation
items. The original table remains the baseline audit, not a claim that repaired source is unchanged.

Additional A03 source qualification concern: OnOk currently validates CurrentComponent only,
although Apply writes every draft component's Conditions/expression. Reproduce a blocking rule
in a different component while the selected component is valid, then qualify the intended whole
edited-list error/warning policy and exact correction focus. This source observation is not yet
claimed as a native reproduction. Avoid machine evaluation and real warning-message prompts in
that qualification; use controlled presentation seams where needed.

### A03 whole-component save validation — 2026-10-03

Native baseline `p13-conditions-all-components-baseline.log` failed its single check:
OK called ApplyDraft with an error in another component while the selected component was valid.
This reproduces acceptance of an invalid draft; the fixture callback does not write a project.
OnOk now commits pending bindings, validates every edited component, and selects the first
blocking component and rule before focusing its missing Value. A null rule stays selected for
removal. Inline localized CannotSave feedback retains the component ID, rule index and verbatim
validator message, with matching accessible text and existing semantic error color. It refreshes
through the existing language handler. No rule evaluation or authored command launch is added.
Warnings retain the existing non-blocking policy. ApplyDraft runs only after all errors are fixed.

`p13-conditions-all-components-focused.log`: 40 passed, zero failures/skips, 58s.
The first expanded modal run (`p13-conditions-all-components-modal.log`) had 16 fixture failures
because Form.Enabled does not reflect WinForms' native modal-owner disabling; its 40 existing
checks passed. The fixture now uses IsWindowEnabled, as the existing modal tests do.
`p13-conditions-all-components-modal-retry.log`: 56 passed, zero failures/skips, 53s,
using the normal source-reference graph. Sixteen actual ShowDialog cases cover all eight cultures
with cancellation and correction/apply paths, native owner disabling/restoration, exact component/
row/value focus, unchanged rejected draft, localized inline accessible feedback and warning-only
successful apply exactly once. Current app/test model DLL hashes match each other and one sibling
Debug source output (`p13-conditions-all-components-model-binding.log`). Diff whitespace check passed.

A03 remains partial: UI-owned preview prose still needs localization. The tests cover current
Windows native modal behavior, not the full keyboard, screen-reader, high-contrast or actual OS DPI
matrix. A01/A02/A04 and the outstanding full-suite/runtime/human release gates remain open; earlier
whole-solution and unfiltered test results predate this change. No commit or push was made.

### A03 preview localization and null-rule correction — 2026-10-03

Native French baseline failed (`p13-conditions-preview-localization-baseline.log`, 1 failed):
the actual preview retained English group explanation after a language change. The UI now uses
ConditionPreviewPresentation, with 16 explicit resource keys in each of the eight cultures for
empty gates, All/Any/Not meanings, issue heading, missing rules and all current rule descriptions.
Authored values/operators/expected values remain format arguments and validator messages/indices
remain intact; severity wrappers use the shared localized presentation. This renderer inspects
syntax only. Existing inert command-marker and draft-preservation tests continue to pass. The
public deterministic English BuildConditionPreview API remains unchanged for existing callers.

The first focused run passed both translation/fact cases but failed selected-command retention
(`p13-conditions-preview-first-focused.log`, 2 passed/1 failed): the focus bookmark restored an
old character offset after translated prose moved the command. The language handler now bookmarks
the preview's updated selection before RTL focus restoration. Native cases retain the selected
command, focused preview and unchanged draft across all eight cultures. A separate populated
matrix covers every rule type, all three expression modes, warning/error wrappers, authored
strings containing literal format braces, verbatim issue facts, and the empty-component state.
Expanded regression passed 113 checks, no skips, 3m15s (`p13-conditions-preview-expanded.log`).

A subsequent native null-rule baseline failed (`p13-conditions-null-rule-baseline.log`, 1 failed):
validation selected the null row but Remove was disabled. Remove enablement and mutation now use
valid row position, including null rows; empty lists still safely disable/no-op removal. The native
case proves localized missing-rule preview, rejected Apply, exact offending row, successful removal
and subsequent single Apply. Final normal-source focused regression passed all 114 checks, zero
failures/skips, 3m34s (`p13-conditions-preview-final-focused.log`). This includes Conditions,
validator/counts, draft-conflict, resource coverage/localization and modal-language focus checks.
Current app/test model DLLs match each other and one sibling Debug source output
(`p13-conditions-preview-model-binding.log`); direct Models ProjectReference is retained.
Whitespace check passed. No commit or push was made.

A03 presentation/validation implementation now includes localized preview prose; broader keyboard,
assistive-technology, high-contrast and actual OS DPI qualification remains open. A01/A02/A04 still
need implementation and native qualification. The full unfiltered suite and whole-solution evidence
predate this batch; runtime/install/human release gates are not closed by these selected checks.
Next implementation batch: A01 Component Files live title/captions/size summary, flexible layout,
localized picker and actual staged files/folders drop behavior with draft/cancel/conflict integrity.

### A01 Component Files presentation — partial progress, 2026-10-03

Native baseline (`p13-component-files-presentation-baseline.log`) failed both actual-form cases:
French left the English title and a large multiline source caption did not fit its row. Component
Files now owns/disposes a shared-derived body font, explicit keyed live captions/accessibility,
shared theme binding, translated dynamic title and known-size/unavailable-size summary. Culture
refresh retains authored fields/caret/draft and uses captured size totals without rereading files.
All current file grid headers include localized shared-file reference counting; full file-operation
cloning is retained. Table field rows use preferred sizes, editor scrolling and flexible caption/
browse columns; help/actions/footer wrap and the minimum window is 700x480. OK commits bindings
before the existing draft-conflict guard; native default OK/Cancel buttons are assigned.

The source picker uses AuthoringFileDialogs with localized title/filter, existing initial directory
and CheckFileExists. A controlled ShowFilePicker seam verifies settings and cancellation without
opening an OS picker or changing the draft. Unavailable/missing metadata is counted explicitly;
expected file metadata exceptions no longer escape UpdateSize. Language/theme and external draft
ListChanged subscriptions are removed on disposal; the owned binding and font are disposed.

First focused build found two missing RtlHelper namespace errors (no tests executed;
`p13-component-files-presentation-focused.log`), corrected with the Engine import.
Expanded normal-source regression passed all 138 checks, zero skips, 2m44s
(`p13-component-files-presentation-expanded.log`): actual dialogs, conflicts, resources and
localization plus the three populated file presentation cases. After adding shared-count caption
and lifetime coverage, `p13-component-files-presentation-final.log` passed 138 checks and failed
one new fixture assertion, no skips, 3m45s: WinForms clears Label.Text during disposal, so comparing
to its pre-disposal text was invalid. The fixture now captures state after disposal before mutating
the retained draft list, and reliably disposes its form on assertion failure. No production change
was needed for this fixture correction. Final normal-source rerun of all four file presentation
checks passed, zero skips, 12s (`p13-component-files-presentation-final-fixture-retry.log`).
Cases cover eight cultures, authored draft/caret/focus/RTL, unchanged canonical component, captured
1 MiB totals after an owned fixture is deleted, picker settings/cancel, minimum multiline caption,
light/dark semantic colors and listener cleanup/closed-summary integrity. Current app/test model
DLLs match each other and one sibling source Debug output
(`p13-component-files-presentation-model-binding.log`). Whitespace check passed. No commit/push.

A01 remains partial. The Explorer files/folders drop hint still advertises unimplemented handlers;
implement actual staged scans, cancellation/disposal/stale-result guards, duplicate/collision and
missing-path feedback, and Apply only through the existing guarded OK transaction. UpdateSize still
reads metadata synchronously on list changes; replace it with captured asynchronous work including
source-path edit refresh and qualify large lists/slow paths. Real Explorer OLE, full modal keyboard/
assistive technology/OS DPI, A02/A04 and all outstanding release qualification remain open. Earlier
whole-solution/unfiltered suite evidence predates this batch and is not a fresh qualification.
