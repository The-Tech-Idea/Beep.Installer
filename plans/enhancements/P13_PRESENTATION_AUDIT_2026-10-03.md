# P13 remaining presentation audit

**Reviewed:** 2026-10-03. **Scope:** existing UX06–08 and UX11–12 acceptance criteria and UI design rules.
**State (updated 2026-11-23):** A05 and A06 implemented with native baselines and focused passing checks. A03 has live captions/status/palette/layout, grid binding, whole-component save validation and preview localization with selected native evidence. A02 is implemented with an Image Gen target, owned native render and 77 passing focused checks. A04 is now implemented against its generated target with 45 passing focused checks; see evidence below. A01 has staged drop/asynchronous metadata implementation and 41 current focused passing checks; actual Explorer/OLE, representative large-folder latency and broader keyboard/accessibility/actual-DPI/release qualification remain open. Older baseline statements below are historical.


## Prepared file recovery correction — 2026-11-23

2026-11-23 prepared file mutation correction: the ordinary fresh/replacement applied-before-checkpoint defect is addressed through the existing file provider, executor, journal and recovery owners. Durable PrepareApply before-state precedes mutation; interrupted replay restores bytes/modification time/access DACL, and committed uninstall removes owned payload. Private backup creation/inheritance, backup path/hash validation, attempt identity, older Verify invalidation and rollback JSON action have focused guards. Resource journal format 2.0 refuses version 1 without rewriting it; SDK contract documented. Final normal-source selection: 126 passed, zero failed/skipped, 3s. Six negative-control selections failed as intended and were restored before final tests. Self-contained current runtime then passed both controlled Sandbox profiles, five cases each with zero failures/skips: fresh and replacement interruption/rollback/resume/uninstall; replacement rollback verifies all 300 original hashes/times, both resumes verify 300 new hashes, unrelated note retained, JSON identifies rollback. Exact source/runtime hashes, native environment, prepared/completed journals, failed history and repeat configuration: [acceptance package](design-evidence/acceptance-package/README.md). Native kills are timing samples; deterministic persisted-journal fault tests cover the precise missing-completion boundary. Other providers, reboot/power-loss/filesystems, version-pair upgrade, non-admin/per-machine, human DPI/Narrator/author tasks and signed/release gates remain open. No full suite, cloud build, host setup/runtime CLI install, commit, push or publication. Whole P13 remains in progress.

## Runtime checkpoint defect and bounded verification — 2026-11-23

2026-11-23 runtime checkpoint continuation: existing upgrade qualifier executed in the user-selected Sandbox, ten scenarios passed. Process interruption exposed a missing canonical journal during File.Replace; AtomicFileWriter now publishes a flushed same-directory overwrite rename, preserves Windows access DACLs and retains link guards/cleanup reporting. Final normal-source ResourceProviderRuntime/JournalPersistence/RecoveryQualifier selection: 56 passed, zero failed/skipped, 5s. Final separate-process rollback/resume sample passed five cases: partial journal inspection, rollback, resumed 300 matching payload hashes/four Resume entries and uninstall. However a preceding real sample had five copied files versus four durable Apply entries: rollback exited 0 and left one file. This confirmed provider.Apply-before-checkpoint gap remains unfixed and blocks recovery acceptance; the later green timing sample does not close it. The existing matrix runner now records singleton failures correctly and runs independent recovery cases without masking failure. Exact failed/green attempts, native journals/logs, tested runtime/source hashes, API rationale and next owner-level mutation-state work: [acceptance package](design-evidence/acceptance-package/README.md). No full suite, cloud build, host setup/runtime CLI install, commit, push or publication. Complete P13 remains in progress.

## Controlled Sandbox acceptance — 2026-11-23

2026-11-23 controlled Sandbox acceptance: user selected Windows Sandbox on this PC. Built an unsigned inert text-only fixture and standalone authoring host locally; actual guest final existing-matrix run passed five cases with zero failures and four explicit skips: install/hash, repair/restored hash, uninstall/removed payload, corrupt-payload refusal and blocked-destination refusal. Child exits/logs and artifact hashes are retained. Guided authoring startup was observed in the clean guest. This is not partial-apply rollback, persisted recovery, per-machine/non-admin, upgrade/restart/prerequisite or human accessibility acceptance. Accessibility qualifier obsolete RTL literal was corrected against its current owner; old matcher failed its guard and final focused selection passed eight, zero skips. Real required-manual CLI gate remains exit 1 with six readiness checks passing and six missing manual artifacts. Recovery metadata contract passed at the verified physical path; filesystem-link protection retained. Exact prior failures, redundant diagnostic repeat, final evidence, operator checks and source hashes: [acceptance package](design-evidence/acceptance-package/README.md). Complete P13 remains in progress. No full suite, cloud build, host install, commit, push or publication.

## UX01/11 creation baseline and close — 2026-11-23

The untouched-startup Save-first prompt observed in the prior entry batch is corrected at the controller/view owners. Only the generated shell placeholder starts clean; explicit New and supplied projects remain authored. Initial creation fields/template identity form a stable baseline across UI-language rebuilds. Real edits and reversions update save status correctly; template-only edits are included. Authored blank projects preserve their identity when entering Guided.

The actual entry fixture now uses ordinary Close and asserts zero untouched-startup prompts, without forced MarkClean/Dispose. Edited-draft Cancel/Discard decisions are injected and retain canonical data as appropriate. Old-state reversion failed six checks; removed authored-project recognition failed the identity guard. Final normal-source focused regression: 63 passed, zero skipped, 1m08s. [Exact source/model hashes, logs and limits](design-evidence/creation-draft-baseline/README.md). Physical dialog input, Yes/save-picker, actual OS DPI/Narrator, author tasks and installed runtime/recovery remain open.

## UX01 actual composed startup — 2026-11-23

The current acceptance audit found a composed-entry bypass that direct form tests had not covered. Program default dispatch now runs the existing cold-start selector with the registered controller: fresh/missing/unreadable recent state opens Guided, valid recent documents reopen Advanced through recovery opening. Four actual Main/message-loop baseline cases failed; after correction, all 46 focused startup/Guided/menu/composition checks passed, zero skipped, 33s. Project/recent fixtures are isolated and inert. [Exact source/model hashes and limitations](design-evidence/cold-start-entry/README.md).

Native Windows UI Automation observation is available, but input geometry failed and a key attempt had no confirmed effect. The startup fixture's interactive-close cleanup also exposed the existing Save-first prompt; the final fixture disposes its owned window and does not qualify that close/save journey. Actual physical input, OS DPI/Narrator, author-task and installed-runtime acceptance remain open. The main plan now contains a current requirement-to-evidence map for all twenty UX IDs; historical completion statements are bounded by that map.

## UX06 large Guided Review — 2026-11-23

The existing Review renderer now batches page/table layout while constructing its unchanged summaries and edit links. Native qualification uses real default snapshot/schema/plan/package validation for 100 components and 3,000 authored destinations with an owned text payload. It retains full detached validation and candidate-only normalization; a later unnotified edit in the final file still blocks Build. No payload runs.

Matched original-renderer comparison failed the chosen one-second fixture guard (1,490ms construction / 1,450ms refresh). Final restored-source selected regression: 69 passed, zero skipped, 36s. Local combined-run sample: 25ms construction, 184ms refresh, 196ms maximum message-loop gap. Early cold isolated runs included first-use theme preparation and do not establish pure snapshot latency; startup-matched samples and exact limitations are recorded in [Review evidence](design-evidence/large-guided-review/README.md). Source/model hashes match tested outputs. Broad OS DPI/Narrator, remote-filesystem/other payload-size behavior, wider startup and runtime/release qualification remain open.

## A01 real large-file-list qualification — 2026-11-23

A real owned 3,000-file/ten-directory grid drop now has a native message-loop qualification check. It verifies all paths, relative destinations, grid rows, known bytes, detached canonical state and one OK callback. The final local sample returned from drop dispatch in 48ms; largest observed message-loop gap was 310ms, with 72 heartbeat ticks. All 42 selected normal-source file/presentation/modal-conflict cases passed, zero skipped, 4m15s.

A deliberate empty scan result made the new guard fail. The first post-mutation batch reused its old assembly because backup restoration preserved an old source timestamp; that aborted batch is not normal-source evidence. Source bytes were verified, its timestamp advanced and the app recompiled before the successful final batch. [Exact logs, source/model hashes, metrics and limitations](design-evidence/large-file-list/README.md). This qualifies the specific local small-text-file fixture, not network shares, arbitrary payload sizes, real Explorer OLE, actual OS DPI/Narrator or whole-product acceptance. Production layout remains unchanged.

## A01 staged scan implementation and reporting — 2026-11-23

Current source inspection supersedes the older unimplemented-drop/synchronous-size statements below: the actual grid handles file drops, stages metadata in the background, guards cancellation/supersession/disposal/draft edits, reports duplicate/conflicting/unavailable paths and applies once through guarded OK. Source-path edits refresh captured sizes asynchronously; the rejected alternative layout remains reverted.

Three new native guards reproduced silent size/drop/Apply failures. The existing owner now reports all affected catches through Diag, with full exception details in diagnostics and correlation references in localized accessible failure status. Obsolete/disposed failures remain observable without mutating drafts. Final normal-source selected regression: 41 passed, zero failed/skipped, 1m28s, including actual drag-event staging, Builder Apply/undo/redo, source-relative size, cancellation, stale drafts, listener/palette/layout checks, modal conflicts and late failures after disposal. App/test Models match sibling source output. Exact red/final evidence and source hashes: [file scan reporting](design-evidence/file-scan-reporting/README.md).

A01's implementation is present with these selected checks. Actual Explorer/OLE, actual OS DPI/Narrator, representative large-folder latency, wider logging/audit/notification service qualification and the remaining P13/runtime/release gates remain open. No full suite, cloud build, commit, push or publication.

## A04 implementation evidence — 2026-11-23

Language Manager now uses File/Language/Keys menus, distinct action resources, preferred layouts and shared theme/fonts. UI-language refresh preserves the active translation-cell draft/caret, selected editing language, dictionaries and preview, including Arabic direction changes. Save/CSV commands commit the active cell; platform CSV parsing preserves quotes/multiline text and validates imports before mutation. All writes/removal tests use isolated owned directories.

The edit-retention baseline failed (six passed, one failed); the native handle transition was corrected and all seven cases passed. Final normal-source regression: 45 passed, zero failed/skipped, 55s, including main menu and Custom Actions regressions, eight cultures, minimum/enlarged bounds and listener disposal. Target/prompt, native comparison, logs, source hashes and limitations are in [A04 evidence](design-evidence/language-manager/README.md). A01 and broader native/manual/release gates remain open. No full suite, cloud build, commit, push or publication.

## A02 implementation evidence — 2026-11-23

Custom Actions now has a command/timing list, aligned selected-action editor, real SVG add/remove icons and one automatically sized OK/Cancel footer. Live wrappers/counts/timing captions, accessible field captions and semantic theme roles preserve detached drafts in all eight cultures. Shared validator row/property metadata routes blocking errors to the exact field; warning confirmation and Apply-only-on-OK remain. Focus-before-removal fixes a reproduced native final-row crash. No authored command executes in the editor.

Final normal-source focused regression: 77 passed, zero failed/skipped, 1m32s. App/test Models match sibling source output. Red regressions, generated target/prompt, owned native render, comparison and limitations are retained in [A02 evidence](design-evidence/custom-actions/README.md). This closes the listed A02 implementation slice, not broader DPI/Narrator or whole-product acceptance. No full suite, cloud build, commit or push.

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
