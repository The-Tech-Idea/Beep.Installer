# P13 — UI, UX and Feature Enhancement Plan

**Date:** 2026-10-02
**Status:** In progress; Stage A verified, B–D authoring/presentation/build workflows expanded, productivity/release/runtime presentation and support features through UX18 added. UX19 signed-gallery, saved-template and favorites workflows and UX20 localized/scaled/page-selection preview workflows are implemented with selected regression evidence, including independent-process preference checks. Cross-stage qualification gates remain open, including wider provider/reboot/runtime and human acceptance beyond qualified ordinary file copies.
**Baseline:** `master`, `6c2f1481e1512a5cfc0f2f02b8cd51d341c61527`.
**Scope:** WinForms authoring experience first; generated installer experience second.
**Relationship:** Continues [P12](P12_WIZARD_FIRST_IA_DESIGN.md), [P6](P6_UIUX_DESIGN.md), [P7](P7_I18N_A11Y_DESIGN.md), and the existing enterprise roadmap. It does not reset their completed work.

**Concise current review:** [UI/UX review and feature list, 2026-10-03](UI_UX_REVIEW_2026-10-03.md) records the latest remote check, remaining delivery priorities and the 20-feature summary. Original source-gap findings below describe the pulled baseline; they are not all outstanding defects in the locally modified implementation.

**Current presentation and entry audit:** [Concrete findings and evidence](P13_PRESENTATION_AUDIT_2026-10-03.md).
A01–A06 implementation slices now have selected native evidence; menu/SVG, large-list and large-Review work is recorded below. The composed cold-start bypass found on 2026-11-23 is corrected with actual Main/Dispatch/message-loop checks. Actual OS DPI/Narrator, representative author tasks, physical input and installed runtime/recovery qualification remain open. Historical statements below are not current acceptance claims.

## Current requirement-to-evidence map — 2026-11-23

This map preserves all UX01–20 requirements. It locates owners and bounded evidence; it does not certify every requirement or the whole dirty tree. Root-level October logs are historical tested-source results. November evidence directories identify later tested source hashes. The current rule permits focused selections, not a full suite. Human acceptance and controlled installed journeys cannot be replaced with source checks, preview scaling or synthetic outcomes.

| ID | Existing owner and located evidence | Acceptance still open |
|---|---|---|
| UX01 | `ProgramVerbs.CreateColdStartForm`, `BuilderWizardForm`; actual entry defect corrected, [63 current startup/creation/save checks](design-evidence/creation-draft-baseline/README.md) | Physical startup/save-picker interaction; representative first-time authors |
| UX02 | `BuilderWizardForm`; jump/revisit cases in the same current selection | Broader keyboard-only author journey |
| UX03 | Guided validity/diagnostic routing; `BuilderWizardFormTests`, [69 Review/field-focus checks](design-evidence/large-guided-review/README.md) | Real author identification/correction of blockers |
| UX04 | Existing optional-step/files-only Guided flow; `FilesOnlyChoiceReachesReviewAndReviewLinksReturnToTheCorrectTab` in current selection | End-to-end authored output/installed behavior |
| UX05 | Existing package step/capability services; MSIX pre-Review blocker in current selection | Each required final format/artifact and installed qualification |
| UX06 | `GuidedReviewPage`, `ProjectReviewSnapshot`; [real 3,000-entry default validation and 69 checks](design-evidence/large-guided-review/README.md) | Other payload/resource sizes and real author edit-link journey |
| UX07 | Shared `AuthoringThemeBinding`, A01–A06 dialogs, [menus/SVG](design-evidence/menu-bar/README.md), [Actions](design-evidence/custom-actions/README.md), [Languages](design-evidence/language-manager/README.md) | Visual owner acceptance, high contrast and actual OS scaling across populated screens |
| UX08 | Existing LanguageManager/RtlHelper/live captions; eight-culture native evidence in the above slices | Physical keyboard, Narrator/Accessibility Insights and actual supported OS DPI |
| UX09 | `AuthoringBuildWorkspace`, `BuildWorkspaceView`; existing build identity/cancellation/stale-result tests and historical workflow results | Representative current build/signing/output/cancellation journeys |
| UX10 | `ResourceRecipeCatalog`, `ResourceRecipeDialog`; historical recipe model/presentation checks | Controlled installed resource/rollback behavior, not just preview |
| UX11 | `AuthoringSaveState`, `SaveStatePresentation`, recovery opening/catalog; historical save/recovery evidence plus current startup opening | Controlled crash/restart/retention and real save/discard/recovery interaction |
| UX12 | `AuthoringHistory`; historical 235-case editor regression/25 guards, later modal/menu checks | Broader native text-undo and cross-mode author journey |
| UX13 | ReleaseOverview model/dialog; historical final 28-case focused result | Current artifact/report association and real deployment evidence |
| UX14 | UpdatePreview/UpdateCenter; historical final 29 guards | Real signed update/acquisition/staging/recovery qualification |
| UX15 | ProjectComparison model/dialog; historical final 19 presentation/accessibility cases | Current representative project comparison review |
| UX16 | `AuthoringHelpCatalog`, help dialog/search; historical 159-case regression | Physical F1/search/edit navigation and discoverability with authors |
| UX17 | RuntimeOperationPresentation/outcome and runtime pages; historical 172-case layout regression | Text-only Sandbox install/repair/uninstall smoke verified ([evidence](design-evidence/acceptance-package/README.md)); ordinary file-copy interruption gap corrected with 126 focused checks and two Sandbox profiles; other providers and representative interactive, per-machine/non-admin, upgrade, prerequisites/restart outcomes remain |
| UX18 | RuntimeSupportBundle generator/export/dialog; historical 196-case support regression | Real failed-operation/redaction/export evidence and consent journey |
| UX19 | TemplateGallery/TemplateLibraryStore; historical 63 independent-process checks | Current signed-template author journey, broader filesystem/session cases and usability |
| UX20 | WizardPreviewForm; historical 147-case populated-preview/isolation regression | Real OS accessibility/scaling; preview never substitutes for installed execution |

Live Windows availability was rechecked: UI Automation can read the owned app tree, but indexed input failed with `coordinate input geometry is unavailable`; an attempted key did not produce a confirmed state change. This is partial observation, not interactive acceptance. No unrelated user windows were operated. The user subsequently selected Windows Sandbox for controlled runtime acceptance; bounded guest results are recorded in the acceptance package. No release, push or cloud build is authorized by this audit. The separate current publication/Actions restrictions remain in force.

## Historical next actions — repository review, 2026-10-03

The remote was fetched again and `git pull --ff-only origin master` reported already up to date; `HEAD...origin/master` remains 0/0. The working tree contains substantial uncommitted implementation and test changes, which were preserved. The feature list below remains the enhancement backlog; appended implementation notes record selected evidence rather than release completion.

1. **Finish hand-built editor and dynamic-message coverage.** Shared bound rows, resource headings/actions, navigation, toolbar/search/title and Recent menus now refresh live. Guided rebuilt-field focus, inline results, source/pattern/component editors, identity/package help, script/log actions, workflow headings/pending hints/keyboard guide and welcome captions have selected evidence. The nine remaining system-resource headings/help now use live resource keys, with populated all-eight-language checks. Generated validation/capability report wrappers now rerender detached data live; save, validation, template, script, history and completed-build statuses use captured localized presentation. Engine explanations and retained logs remain verbatim. Project picker titles/filter labels, field-picker filters and folder descriptions now use localized factories; Guided Project Browse focus and minimum-window help layout have selected checks. Actual owned-modal language refresh now preserves all four Guided Project draft-field selections and keeps the parent disabled across RTL handle recreation, with nested-modal and explicit-disable checks. Audit remaining modal/wizard captions and other dynamic messages. Earlier focus failures are historical evidence, superseded for the tested paths by subsequent runs.
2. **Complete the presentation audit.** Both authoring shells now have live palette bindings with selected light/dark evidence. Inline results use generic artifact labels and wrapping layout; reopening cached sections preserves the current palette and custom fonts. Review remaining shared font usage, populated layouts, exact diagnostic-field routing, keyboard navigation, accessible names and event lifetimes across both modes. Build-workspace artifact guards, root-only Setup paths, indexed include/exclude navigation and queued file-tree completion after empty/invalid source changes have targeted checks. Source scanning now works on a detached full model and checks origin, generation, completion and a full-state hash before applying on the UI thread; the final 109-case regression covers later edits/replacement/disposal, undo/redo, stale failures and both pending/generated script flows. Remaining scan lifecycle/performance scenarios should be assessed against the release gates rather than assuming this selected evidence covers all concurrency. A later 96-case run had a recurring COM-grid focus assertion failure (95 passed); isolated nine-editor and repeated four-window checks passed, so its cause remains under investigation.
3. **Run current integration checks.** After remaining changes settle, run required repository checks and focused integration selections covering the changed requirements. The current owner rule prohibits full-suite runs; older full-suite instructions and results below are historical, not permission to repeat them. The earlier normal-source regression compiled the source dependency graph and passed all 101 selected picker/filter/presentation/navigation checks (`p13-project-picker-final-regression.log`), with five existing test warnings. Earlier sibling BeepDM security/filter compile errors were resolved externally. The earlier 144-case report/status run is historical selected evidence. Later field/focus qualification exposed inactive-window focus loss and then STA worker timeouts; the follow-up records the fixes, failed runs and fresh sibling compile errors. The later 118-case normal-source regression passed after modal-origin refresh fixes and external resolution of the sibling compile errors. A passing selected rerun cannot qualify the complete current working tree.
4. **Qualify real Windows journeys.** Capture actual OS DPI, Narrator/Accessibility Insights, representative author-task results, and controlled install/repair/uninstall/update/recovery evidence. Preview scaling and native automated form checks cover different scenarios.

Latest full-suite qualification: the unfiltered normal-source run passed with 2,045 passed, zero failed and four explicitly skipped end-to-end cases, 2,049 total in 31m24s (`p13-async-review-current-full-suite.log`). The preceding 203-case selected run and whole-solution build (zero errors, seven existing warnings) passed. Their models binding also passed then, but the post-full-suite inventory now finds matching app/test copies and no matching current sibling Debug output (`p13-async-review-full-suite-model-binding.log`). Source outputs changed during the run; later A05/A06 source builds and selected regressions restored matching app/test/source copies, as recorded below. Four dialog source-audit fixes and actual Windows/manual/runtime gates keep P13 in progress. A passing full suite does not qualify its skipped cases or later implementation changes.

These actions retain the P0–P3 feature priorities and stage dependencies below. P13 is still in progress.

Fresh repository/build follow-up: remote fetch and fast-forward pull again report already up to date,
with ahead/behind 0/0. The subsequent normal-source whole-solution build passed with zero errors
and 20,152 warnings in 59.94 seconds (`p13-post-full-current-source-build.log`). A fresh inventory
still finds matching app/test models copies but no matching sibling Debug output
(`p13-post-full-current-model-binding.log`): app/test copies are dated 11:48:52 UTC, while sibling
outputs were rebuilt at 11:51–11:52 UTC. This build does not close the current binding gate or
qualify tests against those later outputs. Preserve the concurrent sibling work; repeat binding
qualification when the source/output state is stable. No production/test changes, commit or push
were made in this repository-review follow-up.

### Current integration and remaining source audit, 2026-10-03

The unfiltered normal-source test run finished with 2,009 passed, ten failed and four skipped,
2,023 total in 31.9007 minutes (`p13-current-full-source-suite.log`). It failed overall; intermediate
passing counts must not be treated as a passing suite. CI's dependency-binding check passed:
the models assembly in the test output is byte-identical to the sibling net10.0 source build
(`p13-current-model-source-binding.log`). The subsequent whole-solution build passed with zero
errors and 11,504 existing dependency/analyzer warnings (`p13-current-whole-solution-build.log`),
covering both extension libraries and UpdateServer as well as the app/core/tests. The later binding
check failed, including after the selected run completed. A fresh hash inventory confirms matching
app/test copies dated 09:58:35 UTC, while all three sibling Debug source outputs were rebuilt at
10:00–10:01 UTC with different hashes (`p13-current-model-source-binding-after-solution.log`).
This establishes that the source outputs changed afterward; it does not establish their provenance
for a new run. That mismatch required a fresh binding check; the later authoring-language and
asynchronous Review checks passed for their current app/test copies, as recorded below.

The two implementation gaps confirmed by source inspection are now implemented and being qualified:

- UX08: the original authoring Languages action opened LanguageManagerForm, which edits
  translation files without selecting UI culture. Both authoring modes now open a shared
  UI-language dialog with native language names, an explicit Apply action and a separate
  translation-editor action. Its new native qualification is recorded below; it does not
  change the project's configured installer languages.
- UX06/responsiveness: Guided Review now evaluates a detached model in the background using
  LatestProjectCheck. Its pending/failure/stale states, exact edit links, full-state/origin guards
  and candidate-only normalization have native checks recorded below. Language changes retain
  the page's captured diagnostics and pending check while refreshing presentation. Full integration
  and large-project responsiveness remain subject to qualification.

### Asynchronous Guided Review — implementation in progress, 2026-10-03

Review now captures independent full-model worker and summary graphs before validation; the
worker uses LatestProjectCheck and Task.Run for snapshot/schema/plan/package checks. UI-side
capture remains synchronous and has not been qualified as a large-project latency bound.
Normalization affects the candidate only. The summary is separate even from that worker graph.
Build is disabled while checking, on current failures, on blocking errors and after detected changes.
Completion and Build dispatch verify original project identity and a full serialized-state hash.
Unnotified nested edits are checked at completion and again before Build; no polling latency claim
is made for such edits after a completed review. Check Again captures the current model and
replaces readiness only when that newer request completes. Project/reload/mutation subscriptions
detach on disposal. Old failures/results cannot publish or expose exception inputs.

Five status/retry keys ship in all eight languages (40 entries). Engine explanations, paths and
codes remain verbatim; severity wrappers use the shared localized presentation. Diagnostic edit
links keep exact paths and now have stable control names. Completion updates diagnostics/status/
Build only, retaining summary controls and focus. An initial native language case exposed complete
control replacement losing the edit target in an inactive Arabic window. The diagnostic rerun
confirmed ownerActive=false and no managed active target. The test now distinguishes retained
managed target from native activation and checks actual focus on reentry; product completion no
longer recreates the summary tree. This does not claim Windows can never deactivate a window.

Initial normal-source installer build passed (0 errors, 1,469 warnings), including an unused local
warning corrected before later builds. All 68 first focused checks passed through the normal source
graph (`p13-async-review-focused.log`, 30s). The expanded native-state run failed one focus case
(16 passed, one failed; `p13-async-review-native-states.log`), reproduced alone in
`p13-async-review-focus-diagnostic.log`. After the update, all 26 async/modal/inactive-focus checks
passed through the normal source graph (`p13-async-review-native-states-retry.log`, 1m17s).
The first expanded normal-source regression passed 202 checks, no skips, in 4m46s
(`p13-async-review-source-regression.log`). A subsequently added actual diagnostic-link culture
case reproduced focus loss after the handler replaced the page with one still checking
(`p13-async-review-diagnostic-focus-baseline.log`). Guided now retains the Review page on culture
changes and refreshes its captured summary, diagnostics, status and action captions. This retains
the diagnostic target and does not cancel/restart the pending engine check. The held-check case
now verifies exactly one evaluator request across all eight languages, unchanged captured/current
model and selected edit target, then readiness on completion; its owned task drains in finally.
All 27 retained-culture/async/modal/inactive-focus checks passed through the normal source graph
(`p13-async-review-retained-culture-focused.log`, 53s). Final normal-source expanded regression
passed all 203 checks, no skips, in 5m19s (`p13-async-review-final-source-regression.log`). It includes
the new diagnostic-link case, all eighteen asynchronous Review cases, existing diagnostic/modal
handoffs, language selectors, both authoring shells, help, release/comparison and runtime-support
actions. Five existing test warnings remain. Whole-solution build passed with zero errors and seven
existing warnings in 17.88s (`p13-async-review-whole-solution-build.log`): five test warnings and two
UpdateServer nullable channel/product warnings. The settled app/test models copies match a current
sibling Debug source output (`p13-async-review-model-binding.log`). The subsequent unfiltered
normal-source suite passed: 2,045 passed, zero failed, four skipped, 2,049 total in 31m24s
(`p13-async-review-current-full-suite.log`). Post-run app/test models copies still match each other,
but no current sibling Debug output matches them (`p13-async-review-full-suite-model-binding.log`).
Source outputs were rebuilt during that run; this later binding mismatch needs a fresh build/check.
These selected results do not close the current full-suite or real Windows gates.

### Authoring UI-language choice — 2026-10-03

Advanced Languages and the new Guided Languages action open the same owned dialog. Its eight
choices use native language names, default to the current UI language and apply only on explicit
Apply (also the Enter action). Closing without Apply leaves culture unchanged. Translation editing
remains a separate button opening LanguageManagerForm; no translation file is edited by choosing
UI culture. Three captions ship in all eight languages (24 entries), and Guided Help now refreshes
its caption/accessibility name live. The dialog follows the authoring palette and RTL direction.

Three new native cases exercise both actual authoring buttons, all eight Apply selections, retained
draft values/text selections and unchanged serialized project, disabled modal owner across RTL
refresh, restored owner on close, initial selection, unapplied choice and language-listener cleanup.
All 22 selected language/resource/modal checks passed in 44 seconds
(`p13-authoring-language-focused.log`). Current installer compilation against existing dependencies
passed with zero warnings/errors (`p13-authoring-language-dependency-build-retry.log`); an initial
local accessibility namespace qualification error was corrected before that build. The normal
source build failed at two sibling FormsManager.ManagedReadPolicy VerifyQueryRegistration
references (`p13-authoring-language-build.log`). The subsequent source-regression attempt also
failed before test execution at sibling QueryExecution.EnterQueryModeCoreAsync
(`p13-authoring-language-source-regression.log`). The initial expanded dependency run passed
102 of 103 checks and failed the toolbar test's expectation of the former translation-editor
tooltip (`p13-authoring-language-dependency-regression.log`). Its expectation now matches the
UI-language action, retaining all live-caption/focus/model/listener assertions. The final expanded
run passed all 103 checks, no skips, in 2m50s
(`p13-authoring-language-dependency-regression-retry.log`). After completion, the app/test models
copies match each other and one current sibling Debug source output
(`p13-authoring-language-model-binding.log`). This supersedes the earlier hash mismatch for these
copies, but does not make the failed normal-source builds pass. Five existing test warnings remain.
This is not full-source qualification. The
translation editor itself, real OS DPI, keyboard/screen-reader and broader release gates remain open.

### Full-suite failure repairs — support snapshot and resource key

Eight failures were the source/resource coverage checks for an undeclared `Builder_SaveTitle`
key in the workflow keyboard guide. That caption now uses the existing translated `Common_Save`
key. The initial focused rerun passed those checks but still failed one support-dialog fixture
cleanup (37 passed, one failed; `p13-full-suite-failure-focused.log`).

Two original failures were in runtime support actions: a click did not create PendingExport in
one native case, and another fixture deleted its source while a dialog's background read still
held the file. The click now has explicit selectable/enabled/operation assertions; its original
intermittent cause is not proven solely by a passing rerun. A controlled regression reproduced
independent reads for concurrent Shown/manual requests (`p13-support-read-baseline.log`). The
initial fixture needed to dispatch Shown before collecting its task; the corrected baseline
failed on the expected task-identity assertion, with all owned reads drained.

Each support-review dialog now owns one observable PendingLoad task. Repeated requests,
including handle recreation, reuse it; changing the source after review cannot replace the
captured body or export. Disposal invalidates UI publication while allowing the bounded read
to finish, and does not start another read. Modal page tests identify their own-thread modal and
await its read before deleting the owned fixture. Tests inject a held reader to verify coalescing,
source mutation, retained export, disposal and handle release. Closing the product dialog does
not wait for that read. Its native helper now uses the shared three-minute matrix deadline and
test-host termination policy rather than leaving a worker running after timeout.

All 40 focused checks passed in 54.9120 seconds (`p13-support-read-resource-focused.log`). After
expanding native review/export to four independent windows across all eight cultures, the final
normal-source selected regression passed all 124 checks with no skips in 5.5534 minutes
(`p13-support-read-source-regression.log`). It includes support generation/actions, operation
summaries, resource coverage, workflow/status captions, modal/inactive focus, page validity and
structural DPI/RTL/accessibility cases. It does not turn the preceding failed full suite into a
passing one. A fresh full suite after remaining implementation changes is still required.

The full suite contains four explicitly skipped end-to-end tests. Even a passing current suite
will not close those scenarios, live installer qualification, OS DPI, assistive technology or
representative author usability. No completion is claimed from intermediate test progress.

### Shared-field layout and focus qualification follow-up, 2026-10-03

Six native field-layout cases reproduced clipping when a larger multiline caption required more height than the fixed row allowed (`p13-shared-field-layout-baseline.log`). Shared field and section rows now use preferred sizing with compact minimum heights; file/folder hosts size to their contents and the license pane can scroll. Checkbox minimum sizing follows the same approach. Seven initial focused checks passed; all eight expanded layout cases passed in the subsequent selected runs. Synthetic enlarged fonts exercise clipping; they do not qualify actual OS DPI.

The COM failure diagnostics showed a recreated editor becoming inactive with no active control. Focus restoration now retains its managed target and text selection while inactive, without activating the window. Two native tests check another window keeps focus and the saved target resumes when the owner is activated. The COM test now explicitly checks retained focus target and user reentry after deactivation; it does not require an inactive window to own global focus. The underlying Windows activation cause and modal-origin language changes remain open.

The 111-case run (`p13-shared-field-focus-final-regression.log`) passed the layout, inactive-focus and COM cases, but failed overall: 93 passed, 18 failed. Two language-matrix workers exceeded their former 60-second deadline and continued changing process-wide culture after the tests failed. Advanced localization and authoring-status helpers now use a three-minute matrix deadline and stop the dedicated test host on timeout so a live worker cannot corrupt subsequent results. This deadline is not a UI performance acceptance target; other STA helpers and the timeout termination branch still need qualification.

The first rerun with that helper did not execute tests: the normal source build failed on five missing members in sibling BeepDM `FormsManager.Lifecycle.cs` (`p13-sta-focus-isolation-focused.log`). Those sibling sources were preserved. Explicit installer compilation against existing dependency binaries passed with zero warnings/errors (`p13-sta-focus-app-dependency-build.log`). A passing dependency-binary build does not qualify the complete source graph. The fresh dependency-binary focused run passed all five checks with no skips in 2.3664 minutes (`p13-sta-focus-isolation-dependency-focused.log`), including both previously timed-out language matrices, deferred navigation guards and both inactive-focus cases. The subsequent fresh-process regression passed all 111 selected checks with no skips in 4.8173 minutes (`p13-shared-field-sta-dependency-regression.log`). It used those current dependency-binary builds with `--no-build`; full-source integration and the timeout termination branch remain unverified.

### Modal-origin language refresh follow-up, 2026-10-03

Actual owned `ShowDialog` tests reproduced lost selection in rebuilt Guided Project fields.
The corrected baseline (`p13-modal-language-baseline.log`) passed Advanced but failed Guided
and direct bookmark capture (one passed, two failed). The initial fixture incorrectly checked
managed `Enabled`; it was corrected to inspect native `IsWindowEnabled`, because ShowDialog
disables the native owner while its managed property remains true.

Bookmarks now capture the saved active leaf when an owned modal is visible, including nested
containers, and retain replacement-field selection without activating the owner. Simply restoring
the active control exposed a second failure: RTL handle recreation left the owner natively enabled
and allowed Guided to take modal focus. Both shells now use a scoped modal refresh guard that
keeps the owner disabled during rebuilding, disables the replacement native handle until modal
closure, and restores the current handle according to the owner's managed enabled state.
Modal-close and owner-disposal handlers detach their subscriptions. A queued initial re-enable
attempt failed the immediate return-state check; re-enabling now happens on modal closure.

Six initial corrected checks passed; the expanded ten-case focused run passed with no skips in
52.5914 seconds (`p13-modal-language-expanded-focused.log`). It covers all four Guided draft
fields across eight languages, Advanced identity, actual modal focus/native owner disabling,
canonical values, selection and return focus, nested modals, explicit owner disable and existing
inactive/deferred-navigation guards. These use a real owned modal with test-driven language
changes; they do not exercise translation-file editing in LanguageManagerForm.

Normal source compilation passed with zero errors and 1,468 dependency/analyzer warnings
(`p13-modal-language-source-build.log`); earlier sibling FormsManager errors were resolved
externally. The final normal-source regression passed all 118 selected checks with no skips in 7.4265 minutes (`p13-modal-language-source-regression.log`), including all new modal cases and existing field/focus/navigation/theme/status/diagnostic/section-rendering cases.
Other modal editors/captions, lifecycle cases, current full suite and real Windows qualification
remain open. P13 and the full implementation goal remain in progress.

## 1. Repository synchronization and review limits

- Remote: `https://github.com/The-Tech-Idea/Beep.Installer`, branch `origin/master`.
- Fetched and pulled with `git pull --ff-only origin master`.
- Advanced from `82ba68d` to `6c2f148`: 58 commits; 439 files changed, 107,696 insertions and 1,713 deletions.
- The existing local modification to `CLAUDE.md` was preserved.
- Rechecked on 2026-10-03: `git fetch origin` found no divergence (`HEAD...origin/master` = 0/0), and `git pull --ff-only origin master` reported already up to date at `6c2f148`. Existing local implementation changes were preserved.
- This plan is based on source and roadmap inspection. No live visual walkthrough, build, or test execution was performed for this documentation change. Existing test coverage and historical roadmap results are not fresh verification of this checkout.

## 2. Product outcome

An author should be able to create a project, choose a suitable package, understand what will happen on the target machine, fix blocking issues, and produce an artifact through one coherent guided experience. Experienced authors retain direct access to all advanced configuration.

Three journeys define priorities:

1. **First package:** desktop app source folder → identity and files → install behavior → output → review → build.
2. **Maintain a release:** open project → understand changes → adjust version/resources → validate → build/publish.
3. **Deploy a system application:** service or IIS template → prerequisites and resources → package compatibility → signing → deployment assets.

## 3. Current feature inventory

These are present in the code; availability does not mean every format supports every resource or that release qualification is complete.

| Area | Existing implementation | Next enhancement |
|---|---|---|
| Guided authoring | `BuilderWizardForm`: new project, identity, source, components, shortcuts, registry, output, signing, build; Advanced/Guided handoff | Consistent entry points, explicit optional steps, package choice and review |
| Advanced editor | `PackageBuilderForm`, grouped searchable navigation, keyboard shortcuts | Reduce toolbar density; consistent headers, empty states and focused field editors |
| Project authoring | `.bsetup`, schema diagnostics, canonical JSON, compiled plans, templates and template diff | Navigable diagnostics, safe editing history, review of changes |
| Files and conditions | Source scanning, includes/excludes, components, files dialog and boolean condition editor | Payload summary and guided destination/condition editing |
| System resources | Provider-backed resources including services, IIS, registry, shortcuts, tasks, environment variables, associations, firewall, certificates, COM, drivers, transforms, packages and Web Deploy | Discoverable recipes and capability guidance using existing provider contracts |
| Packaging | EXE, MSI, MSIX and ClickOnce implementations; packaging and publish wizards | Explain output choices and surface format-specific blockers early |
| Release readiness | `PackageFormatCapabilityReporter`: EXE, MSI, MSIX, WinGet and Intune/ConfigMgr | Present actionable format comparison in the guided workflow |
| Signing | Code-signing wizard, signing services and secret reference support | Guided readiness, certificate status and actionable failure messages |
| Updates | Update center, feeds/channels, rollout and delta services | Preview channel transitions, compatibility and rollback expectations |
| Enterprise release | WinGet, deployment kits, offline layouts, evidence and qualification services | One release checklist and explicit artifact/evidence status |
| Build experience | Pipeline progress, cancellation, logs, result/error dialogs and artifact actions | Unified build workspace with stage status and durable results |
| Runtime installer | Page graph, component/prerequisite selection, progress, completion/errors, localization and RTL | Clear operation/recovery summaries and support actions |
| Accessibility and recovery | Theme tokens, accessible naming helpers, DPI/RTL tests, autosave and recovery | Audit newly added guided shell; explain save/recovery state consistently |

## 4. Source-derived gaps to address first

| ID | Evidence | User impact | Priority |
|---|---|---|---|
| G01 | `PackageBuilderForm.NewProject(bool)` opens `ProjectNewDialog` then `QuickStartWizard`; cold start uses `BuilderWizardForm` | Different creation workflows depending on entry point | P0 |
| G02 | `BuilderWizardForm.MakeStepperButton` checks only `CanAdvancePastCurrentStep` before a forward jump | Intermediate required steps can be bypassed; new-project creation is tied to Next | P0 |
| G03 | `ValidateFieldsInline` writes warnings and errors to `ErrorProvider`; `ActiveSectionHasErrors` treats every nonempty error string as blocking | Field warnings can block Next; navigation depends on presentation state and debounced feedback | P0 |
| G04 | `GoldenPath` omits package format, prerequisites and review; shortcuts/registry/signing appear as ordinary sequential steps | Authors cannot complete several common choices within the default path | P1 |
| G05 | `BuildNewProjectStep` recreates text boxes on revisit; `OnNext` skips `TryCreateProject` once `_projectCreated` is true | Revisiting and editing initial fields needs an explicit update policy | P1 |
| G06 | Empty components are blocked in Guided although the code notes files-only installers can be valid | A legitimate project shape lacks an explained alternative | P1 |
| G07 | Guided step titles are literals; result/progress forms contain hardcoded strings/colors; theme status and muted colors are fixed | Newer surfaces have inconsistent translation/theme coverage | P1 |
| G08 | Guided mode reparents panels produced by a hidden `PackageBuilderForm` | Reuse works today, but section lifetime, subscriptions and shell coupling need a durable boundary | P1 |
| G09 | Build feedback spans progress, error, result, log and workflow surfaces | Authors must reconstruct what failed and what action to take across views | P1 |

G02/G03/G05 are source findings requiring behavioral regression cases during implementation; they are not claimed as reproduced live defects. Reuse existing search, validation, autosave and cancellation instead of listing them as missing features.

## 5. Target information architecture

**Guided steps:** Project → Files and components → Install behavior → Package and destination → Signing → Review and build → Result.

- **Project:** template, identity, source folder and stable application ID. Avoid asking for the same identity twice.
- **Files and components:** scan summary, destinations, includes/excludes, optional features and estimated payload size.
- **Install behavior:** scope, prerequisites and shortcuts; reveal registry and system resources when selected or suggested by the template.
- **Package and destination:** EXE/MSI/MSIX/ClickOnce choice, compatibility findings, output location and deployment intent.
- **Signing:** show configured/not configured/blocked status; allow explicit unsigned development builds where existing policy permits them.
- **Review and build:** consolidated identity, files, system changes, package limitations, signing and diagnostics. Edit links return to the relevant step.
- **Result:** artifacts, warnings, log, open folder, copy path and available next actions appropriate to the artifact type.

Suggested shell:

```text
Project name • version                 Save state     Guided / Advanced
Step list              Main editor                     Context help
  Project              Heading + short explanation     Why this matters
  Files                Fields / resource list          Examples
  Install behavior     Inline issues                   Compatibility
  Package
  Signing
  Review / Result
Back                   Blocking issues / warnings      Next / Build
```

Use a vertical step list when horizontal titles cannot fit. Context help may collapse at narrow widths. Keep one primary action per step and stable footer placement. Advanced mode keeps searchable groups and all current sections, with consistent terminology between both views.

## 6. Prioritized feature list and acceptance criteria

| ID | Feature | Priority / kind | Acceptance criteria |
|---|---|---|---|
| UX01 | One project-creation flow | P0 / improve | Cold start, toolbar New and Ctrl+N use the same guided creation coordinator; cancel/discard behavior preserves existing rules |
| UX02 | Reliable step navigation | P0 / fix | Forward jumps validate required intervening steps; step 0 cannot be bypassed before creation; revisiting does not create a second project |
| UX03 | Severity-aware validation | P0 / fix | Blocking uses typed diagnostics, independent of `ErrorProvider` text; warnings remain visible and allow navigation unless an explicit policy makes them blocking; immediate edit-and-Next uses current values |
| UX04 | Optional steps and files-only path | P1 / improve | Shortcuts, registry and unsigned development configuration have explicit skip states; files-only projects proceed when canonical validation allows them |
| UX05 | Guided package selection | P1 / improve | All implemented output choices are explained; format-specific blockers appear before Build; selection uses existing exporter/analyzer behavior |
| UX06 | Review with edit links | P1 / new UI over existing model | Shows files, scope, system changes, format and signing; each issue/edit action opens the correct step or Advanced section without losing edits |
| UX07 | Consistent form design | P1 / improve | Shared spacing/type/status tokens; labeled fields, help text and clear empty states across Guided, Advanced and resource dialogs |
| UX08 | Guided shell localization/accessibility | P1 / improve | Step titles/statuses use resx; names, focus order and direction update correctly; long translations and Arabic remain usable at supported DPI levels |
| UX09 | Unified build workspace | P1 / improve | Shows preparing/building/signing/completed/failed/cancelled stages and live log; cancellation remains connected to the pipeline; result persists until next build/project change |
| UX10 | Resource recipes | P1 / improve | Service, IIS, task and firewall recipes create existing typed resources; required inputs and rollback/support limits are visible; no duplicate runtime logic |
| UX11 | Save and recovery status | P1 / improve | Saved/unsaved/recovery-available/autosave-failed states are explained; original file and recovered draft are distinguished |
| UX12 | Undo/redo transactions | P2 / new | Add/edit/remove/duplicate and template apply undo coherently; opening a project resets history; script and generated views stay synchronized; secrets are not retained in history |
| UX13 | Release overview | P2 / new UI over existing services | Shows package, signing, deployment assets and qualification evidence separately; generated assets do not imply deployment succeeded |
| UX14 | Update preview | P2 / improve | Explains target channel/version, payload choice and known rollback constraints using current transition/delta services |
| UX15 | Project comparison | P2 / improve | Extends canonical snapshot/template diff into a user-visible change review; highlights resources, version, destinations and package changes |
| UX16 | Contextual search and help | P2 / improve | Existing navigation search supports useful synonyms; results indicate Guided/Advanced destinations; F1 opens relevant local help |
| UX17 | Runtime operation summary | P2 / improve | Ready page describes scope, prerequisites and system changes; completion reports restart requirements; errors distinguish failure and recovery outcome |
| UX18 | Runtime support actions | P2 / improve | Copy error details and export support bundle use existing diagnostics/redaction; failed recovery is never displayed as successful |
| UX19 | Template gallery and favorites | P3 / improve | Search/category/sample metadata makes current templates discoverable; imported signed templates retain trust verification |
| UX20 | Preview refinements | P3 / improve | Preview can exercise language, DPI/layout and page-selection scenarios without running installation |

P0 = correctness before broader UI work; P1 = core workflow; P2 = productivity/release depth; P3 = polish after core journeys pass. This ranking is an implementation recommendation, not an estimate of elapsed delivery time.

## 7. UI design rules

1. Extend `InstallerTheme` with spacing, field/action sizing and semantic foreground/background pairs; audit pairs under active light, dark and high-contrast themes rather than assuming fixed status colors work everywhere.
2. Use layout containers and preferred sizing. Test minimum window size and 100/125/150/200% scaling with populated controls and long translated text.
3. Every editor has a title, purpose, primary action and empty state. Explain the next useful action: “Add a shortcut” or “Choose source files.”
4. Provide visible validation messages with severity and a fix. Color and icons supplement text; focus moves to the first blocking field when navigation fails.
5. Keep resource lists consistent: Add, Edit, Duplicate and Remove; use structured input editors for common properties and retain the property grid as an advanced fallback.
6. Route all new visible strings through the existing language system. Update open windows when language/theme changes.
7. Result actions depend on artifact capabilities: an MSIX or ClickOnce artifact must not be labeled “Setup EXE.” Never enable an action just because an unrelated output exists.

## 8. Implementation architecture

- Keep `.bsetup` and `InstallProject` canonical. Reuse `ProjectSchemaService`, `ProjectAuthoringWorkspace`, `ProjectValidationCenter`, capability reporters and current build/publish services.
- Introduce a small typed step descriptor/state layer: ID, localized title, applicability, validation scope, status, and view factory. Track current/reached steps explicitly. Do not derive validity from control text or button appearance.
- Use a shared project-creation coordinator for New/cold start, with explicit draft creation and revisit semantics. Editing Project after creation updates identity fields without regenerating AppId or discarding resources.
- Extract section factories from `PackageBuilderForm` incrementally into a section/view registry usable by either shell. Define who owns/disposes views and event subscriptions; avoid parallel hidden form instances against the same controller.
- Add a diagnostic path-to-section/field mapping for review navigation. Preserve full paths so repeated row properties cannot accidentally route to another resource.
- Preserve `SectionIds`, `OpenSection`, `ActiveSection` and existing keyboard behavior during extraction. Reuse `BindingSourceRow<T>` and `ValidationCounts`; add abstractions only where multiple consumers need them.
- Keep engine/runtime work in the appropriate existing Core/BeepDM boundary. UI changes must not introduce a second validator, installer executor, signer or update engine.
- Freeze or snapshot the project for a running build so edits do not change its meaning mid-operation. Label readiness as stale when the project changes; run expensive format checks asynchronously with cancellation and discard outdated results.
- For runtime cancellation, the current implementation disables Cancel during installation because the underlying execution path is synchronous. Enable cancellation only after engine support and rollback verification exist; first improve the explanation of this state.

## 9. Delivery sequence

| Stage | Work | Depends on | Exit gate |
|---|---|---|---|
| A — Workflow correctness | UX01–03; regression cases for step jumps, warning severity, creation/revisit and immediate edits | Current P12 baseline | No invalid required-step bypass; warnings do not accidentally block; one creation path |
| B — Guided completeness | UX04–06, typed step descriptors, package selection and review routing | A | Desktop and files-only projects reach a correct build configuration without Advanced detours |
| C — Shared presentation | Section registry extraction, UX07–08, theme/strings audit | A; coordinate with B | Both modes retain edits, focus and lifetime correctness; populated DPI/RTL/accessibility checks pass |
| D — Build and resources | UX09–11, resource recipes and build snapshot handling | B–C | Build/cancel/failure/recovery states and resource authoring remain coherent |
| E — Productivity and release | UX12–16 | Stable editor transactions and canonical snapshots | Undo and release/update previews are consistent with canonical engine results |
| F — Runtime and polish | UX17–20 | Existing runtime services; cancellation needs separate engine work | Runtime journeys and template/preview polish pass scenario qualification |

Start Stage A with UX03 and UX02, then unify New. Avoid adding more guided steps until validity and creation state have a stable contract.

## 10. Verification and release gates

For implementation, run targeted suites appropriate to each change, followed by required repository checks. Do not treat documentation statuses as passing test evidence.

- **Navigation:** jump from New directly to Build; jump over invalid Source; edit a previously valid step; revisit Project; switch modes repeatedly; verify saved and unsaved state survives.
- **Validation:** warning-only step; nested resource error; immediate typing followed by Next; diagnostic navigation to the exact row/field; validator failure reports an unavailable state rather than silently claiming readiness.
- **Creation/data:** cold start vs Ctrl+N; cancelled creation; files-only project; duplicate/remove resource; template apply; round-trip save/load preserves unknown/advanced configuration as supported by the serializer.
- **Build:** success, failure, cancellation, signing failure and output absent; edits during a build; stale readiness result; artifact action availability for each format.
- **Presentation:** populated Guided and Advanced views at minimum size and supported DPI; long translations; Arabic RTL; keyboard-only navigation; Narrator and Accessibility Insights evidence captured separately from structural automated checks.
- **Runtime:** representative install/repair/uninstall/upgrade, prerequisite failure, recovery failure and restart-required scenarios in controlled Windows qualification environments.

Existing regression anchors include `BuilderWizardFormTests`, `EverySectionRendersTests`, `EveryDialogRendersTests`, `PageValidityTests`, `InlineValidationTests`, `BuildCancellationTests`, `DpiLayoutTests`, `RtlAndAccessibilityAuditTests`, `LanguageSwitchTests` and authoring/capability tests. Expand them where the new behavior has a meaningful contract.

Usability targets to validate with representative authors: at least four of five first-time participants complete a simple EXE project without assistance; each can identify a blocker and its fix; all can locate the resulting artifact. Record baseline task times before adopting a speed-improvement target. Do not report these proposed targets as measured outcomes.

## 11. Completion checklist

- [x] Stage A navigation and severity behavior verified.
- [x] One default creation flow and stable revisit semantics.
- [ ] Guided package choice, optional steps and review shipped.
- [ ] Shared section ownership and mode handoff verified.
- [ ] Theme, localization, populated DPI and assistive-technology evidence recorded.
- [ ] Build, resource recipes and save/recovery journeys verified.
- [x] Productivity/release enhancements tracked individually against UX IDs (current map above; acceptance remains bounded).
- [ ] Runtime summary/support improvements qualified.
- [ ] Master tracker updated with actual outcomes and test evidence per stage.

### Implementation log — 2026-10-02

- UX01: Advanced New/Ctrl+N now opens `BuilderWizardForm` against the same controller/editor; cancelling before creation retains the existing project. The older quick-start chain is removed from the default New path.
- UX02: Forward step jumps traverse every intervening step through the Next validation path, including project creation. Revisiting Project applies edited values without replacing AppId, components or resources.
- UX03: Navigation uses fresh schema diagnostics filtered by severity and section scope, not `ErrorProvider` messages or the debounce timer. Nested component/registry diagnostics participate; unavailable validation blocks with a translated explanation.
- Shared panel lifetime: Guided disposal returns borrowed panels to the existing editor. Owned editor instances and autosave are disposed together. Editor subscriptions now detach on disposal and project rebinding avoids duplicate subscriptions.
- Final Stage A verification: 88 tests passed across `BuilderWizardFormTests`, `InlineValidationTests`, `EverySectionRendersTests`, `EveryDialogRendersTests`, `BuilderSectionCacheTests` and `LocalizationTests` (2026-10-02). No full-suite or live assistive-technology qualification is claimed.
- Stages B–F and usability/manual qualification targets have not been implemented or verified by this batch.

### Stage B progress — 2026-10-02

- Guided now uses typed descriptors for seven grouped steps: Project, Files and components, Install behavior, Package and destination, Signing, Review and build, Result. Initial creation advances directly to Files; revisiting Project updates the same graph.
- An explicit files-only choice permits an empty component list. Optional prerequisites/shortcuts/registry are explained, and unsigned signing configuration has a continue action.
- MSI and ClickOnce are additional persisted `OutputFormat` values. Script serialization and canonical/compiled representations retain the choice. The build pipeline routes them through the existing WiX exporter and ClickOnce publisher; it does not substitute an EXE build.
- MSIX and ClickOnce compatibility blockers and strict MSI export findings are checked before Review. Package selection includes explanatory help and the update URL.
- Review summarizes identity, files, scope/system changes, package/output and signing with canonical diagnostics and edit links. Full diagnostic paths survive routing; borrowed tab controls are detached before disposing their containers.
- Result actions use a generic artifact label; ClickOnce offers manifest opening. The selected services currently lack mid-operation cancellation, so their progress windows explain and disable that action instead of claiming to cancel an operation they cannot interrupt.
- **Verification:** Core/UI compile using existing sibling dependency binaries (`BuildProjectReferences=false`). The expanded Stage B run passed 174 tests, including guided/editor behavior, dialog/section rendering, serialization and MSI service contracts (`p13-stage-b-latest-test.log`). The original broad run was stopped after 124 passes because an existing file-dialog test pressed Add with empty input and opened a modal validation message; it now seeds valid editor input and passes. The MSI packaging test injects a WiX tool runner; ClickOnce publication uses the real publisher. This does not prove a real WiX package, Windows deployment, or a clean source build.
- Cached result actions now bind once and read the current result, with a regression proving a reopened panel launches only the newest artifact once.
- MSI source scanning follows the explicitly selected folder, without build-output auto-discovery. MSI/ClickOnce reject unsupported clean-output requests before changing the destination. MSI WinGet manifests use the MSI type without EXE command switches; unsupported ClickOnce WinGet export reports an error before creating output. The 19 selected package/deployment tests passed (`p13-target-deployment-test.log`).
- **Source-build rechecks:** The earlier BeepDM `SchemaPersistenceHelper` interface mismatch cleared through concurrent dependency work. A normal `dotnet build Beep.Installer/Beep.Installer.csproj --no-restore` passed with zero errors and 9,744 warnings across the dependency tree (`p13-source-recheck.log`). The following source-based test run passed 223 of 224 tests; the Arabic-switching test raced with a runtime-dialog test that reset the global language. The Language test collection now disables parallel execution. A subsequent source rerun failed during new concurrent BeepDM work with CS1061 in `SyncCheckpointFailureResult.cs` (`List<IErrorsInfo>.Failed`, `p13-source-regression-isolated.log`). Its files were not edited by this task. Final isolated regression verification uses the last successful dependency binaries; the current source-build gate remains open.
- **Remaining in B/C:** qualify all output journeys, move the remaining Review-page and format-comparison checks off the UI thread, continue metadata/title extraction and live Advanced retranslation, and complete populated Guided/Advanced DPI/RTL and assistive-technology coverage. Stage B is not marked complete.

Stage C presentation work (partial):

- Added 29 translated keys across English, Arabic, German, French, Spanish, Portuguese, Japanese and Chinese for the new guided/package/result surfaces.
- Guided shell language changes rebuild captions while preserving uncommitted project fields and the selected template, keep the selected section, and update accessible button names. New step controls receive the current RTL direction; switching back to English removes mirroring. Rebuilt stepper buttons are disposed, and the language subscription is removed on disposal.
- **Verification:** 68 selected guided, package, localization and live-switching tests passed (`p13-stage-c-test.log`). UI compilation had no warnings/errors against existing dependency binaries. Core compilation had three existing platform warnings and no errors. These checks do not establish populated DPI coverage, visual usability, Narrator behavior, or live retranslation of every cached Advanced field.
- The two source-binding checks passed against the rebuilt dependencies (`p13-source-binding.log`). This establishes assembly provenance for that run; it does not resolve a later dependency compilation failure.
- **Final isolated regression run:** 226 of 226 selected tests passed after the test-input and Language collection fixes (`p13-isolated-regression.log`). The test harness was rebuilt with `BuildProjectReferences=false` against the last successful sibling build. This run includes guided/editor behavior, all selected dialog/section rendering, package/WinGet contracts, serialization, localization/live switching and source-binding checks. The current full-source build remains affected by concurrent BeepDM compilation changes.
- Stages D–F and all unverified completion gates above remain open. No commits or remote push have been made by this task.

### Shared editors, review detail and build snapshots — next implementation batch

- Extracted the resource-list/PropertyGrid factory into `Ui/ResourceSectionFactory.cs` and registered all 40 section factories through `SectionViewRegistry`. Cached views remain owned by the editor and are borrowed by Guided; existing section IDs, cache invalidation, navigation and mode handoff are preserved. Full removal of the hidden editor dependency is not claimed.
- Canonical paths select the owning model by collection identity, then resolve nested objects such as `IisSites[1].Bindings[1].Port` and `Components[0].Files[1].DestinationPath`. The PropertyGrid selects the exact property and edits the nested model directly through one-way selection; it cannot replace the owning resource via SelectedObject writeback.
- Review now previews individual authored resource categories and component files with exact item edit links. Each category preview is bounded to five items, with access to the full editor. Names are shown without command arguments, credential values or environment-variable values. Custom actions open their existing editor from Review.
- Custom-action editing now uses draft copies. OK applies additions, removals and edited values to the project; Cancel preserves the original actions. The earlier list-copy behavior discarded additions/removals while leaking field edits into the original graph.
- Resource headers/help and action rows wrap, empty lists explain Add, grids/properties have explicit accessible names, and the returned panel disposes its BindingSource. Project reload no longer reuses disposed cached bindings, and clears the previous build result. Semantic text/status tokens choose readable foregrounds on light/dark/colored backgrounds and use system colors in high contrast.
- Added 15 more translated keys across all eight languages, bringing the new guided/resource/readiness key count to 44 (in addition to the Stage A unavailable-validation key).
- Builds capture an independent model through the existing serializer before dispatch. The in-memory parser shares the saved-script parser. Captured version, output folder, component files and provider inputs stay stable while the live project is edited. MSI/ClickOnce no longer offer the unsupported clean-output prompt in the UI.
- Status readiness uses a captured model and runs schema/package checks on a worker. Project changes invalidate the result immediately; cancellation and generation checks discard late results and obsolete failures, including queued UI publication. Resource changes invalidate readiness even when the project was already dirty. Cancellation occurs between validation phases, not inside the existing validators.
- **Verification:** 148 shared-presentation tests passed (`p13-shared-presentation-test.log`); 39 snapshot/cancellation/parser tests passed (`p13-build-snapshot-test.log`); 191 selected snapshot/readiness regressions passed (`p13-snapshot-readiness-regression.log`). After the final lifetime/readiness edits, 50 targeted workflow tests and 15 readiness-contract tests passed. These counts describe separate runs and must not be added together.
- **Latest source build:** `dotnet build Beep.Installer/Beep.Installer.csproj --no-restore` passed with zero errors and 9,750 dependency-tree warnings (`p13-final-source-build.log`). The earlier concurrent BeepDM compile errors are historical, not the current build result. The later UI-only rebuild passed with zero warnings/errors against these dependency outputs. Final selected regression verification passed 233 of 233 tests (`p13-final-regression.log`), including source-binding checks. This is selected regression coverage, not a full-suite or runtime deployment qualification.
- Resource layout checks use populated controls, Arabic/German text and programmatic 100/125/150/200% scaling. They are structural checks, not OS DPI changes, a visual review, Narrator evidence or full Guided/Advanced qualification. Dictionary/provider-input navigation, recipes, save/recovery status, complete build workspace/result attribution during project changes, productivity/release and runtime features remain open.

### Stage D save and recovery progress — 2026-10-02

- UX11 now exposes immutable save state: document path, separate recovery path, saved/autosaved timestamps, recovered status and save/autosave/recovery errors. Advanced shows the status with accessible details; Guided shows the same status, a Save action and Ctrl+S. Pending Project-field edits appear unsaved before they are committed, and Guided Save commits those fields and the shared editor bindings.
- Recovered drafts remain dirty, resolve relative paths against the original script and retain the original document identity until saved. Invalid recovery drafts are preserved and their load errors are disclosed while the original project opens. Resolving an original script's relative paths does not create unsaved changes.
- Save As updates the document path only after a successful write. The controller's save-before-discard action now handles untitled projects and refuses to discard after a canceled or failed save. Advanced's discard check includes pending script edits and routes saving through the editor's apply/commit action; its visible status also reflects that draft. Build/publish stop when saving fails. An owned Guided window prompts before discarding its draft; switching to the full editor preserves the project without this prompt. Advanced cannot close during a build.
- Script saves now use the existing atomic writer. Autosave captures the graph, target path and atomic write under the same lock as New/Open/Save/Save As, preventing stale draft writes after saving or document replacement. Autosave errors and successful retries update visible state without marking a saved document dirty.
- Added nine save/recovery strings in all eight supported languages. New regressions exercise failed Save As, valid/invalid recovery, autosave failure/retry, concurrent save/autosave, relative-path clean state and the real Guided and Advanced script Save actions. The preliminary source build passed with zero errors and 9,776 dependency/test warnings. **Final verification:** the normal source build passed with zero errors and 1,470 dependency/test warnings (`p13-save-state-final-build.log`); 254 of 254 selected regressions passed, including source-binding checks (`p13-save-state-final-regression.log`). `git diff --check` reports no whitespace errors. These counts supersede the preliminary 84/252/253 runs for this batch, rather than adding to them.
- UX11 remains partial: untitled recovery discovery, explicit recovery-choice/history UI, backup-retention failure reporting and controlled crash/restart qualification are still open. UX09's complete build workspace/result attribution, UX10 recipes, UX12 undo/redo and Stages E/F remain open. Selected tests do not qualify actual Windows deployment, OS DPI, visual usability or assistive technology.

### Stage D build workspace progress — 2026-10-02

- UX09 now has one job model and a shared workspace control used by Advanced's Build Workflow and Guided's Result step. Guided enters it as soon as a job starts. Build and Publish retain their live progress messages, complete logs, artifact path and result inline; they no longer open competing progress/success/error dialogs on completion. The older Build Log page reads the same retained job log.
- The workspace records the captured product name, version and package format; reports Preparing, Building, Signing, Cancelling, Completed, Failed and Cancelled; and exposes cancellation, save-log, open-artifact and open-folder actions. EXE/MSIX, MSI and ClickOnce send typed phases instead of making the UI infer phases from English text. Pipeline cancellation has an explicit result flag. Successful builds are not retroactively described as cancelled merely because a late cancellation request arrived.
- A job remains active until its worker returns, even if it reports 100% prematurely. Concurrent jobs are rejected. Progress is scoped to its job identifier, so late reports cannot overwrite the next job or a terminal result. Disposed views stop observing the job; reopening a view reconstructs its current state. Language changes retranslate status/actions and switch workspace RTL.
- Editing or replacing the project during a build retains the original job identity, labels it as an earlier revision and prevents assigning its result to the current project's Last Result. Completion also compares the live serialized project with the captured revision to catch unnotified nested edits. Completed current results survive section navigation and clear on the next job/project change. The existing MSI/ClickOnce services still cannot interrupt their current operations; their cancel action stays disabled with an explanation.
- Publish uses an independent project capture, participates in the same job exclusivity/lifetime rules and returns the actual `.application` manifest rather than treating its output directory as an executable. A real publisher regression verifies the captured identity, produced file and success log. A pre-existing success message ordering error in ClickOnce progress was corrected; failed/early-return publication durations are now recorded.
- Twelve workspace strings and revised wait/cancel guidance cover all eight languages. New model/UI regressions cover live phases, logs, cancellation/token propagation, unsupported interruption, observer disposal/reopening, current result persistence, project edits/replacement, unnotified nested edits, late progress, worker exceptions and current artifact actions.
- **Final verification:** the normal source build passed with zero errors and five existing test warnings (`p13-build-workspace-final-build.log`); all 270 selected regressions passed (`p13-build-workspace-final-regression.log`), including source-binding, live language/RTL switching, cancellation button, view lifetime/reopening and result attribution checks. `git diff --check` has no whitespace errors. The preliminary 55/268 runs are superseded by this run and must not be added to it. These selected tests do not establish full-suite, real WiX/toolchain builds, controlled Windows deployment, OS DPI or assistive-technology qualification. Live phase messages are streamed; detailed tool output is appended when the existing packaging service returns its buffered log.
- Stage D remains in progress: UX10 resource recipes and the UX11 recovery discovery/history/retention gates are open. The remaining B/C qualification and Stages E/F requirements remain open.

### Stage D resource recipes progress — 2026-10-02

- UX10 adds Windows service, IIS site/application pool, scheduled task and inbound firewall recipes. Guided Install/Review, the Advanced toolbar and the corresponding resource lists open the same dialog. Each recipe creates the existing typed definitions and uses the existing schema, package capability and compiler contracts.
- Preview evaluates an independent project capture and lists resource/settings changes without changing the project. Apply revalidates the current graph, preserves existing object/collection identity, rejects duplicate names and authored IIS binding collisions, and rolls back a failed collection commit. Cancel preserves the project and current list selection.
- Machine/admin installation and switching to EXE are explicit choices shown in the preview. Machine recipes disable scope/privilege overrides; unsupported package targets and strict MSI capability errors block Apply. Service recipes offer built-in accounts; credentials, TLS and advanced task settings remain available through the full editors.
- The dialog explains tool prerequisites, account behavior and the existing providers' removal/rollback limits. It does not probe or overwrite-check target-machine resources; authors must use appropriate names. Twenty-eight strings cover all eight languages; language switching preserves drafts and updates RTL/accessibility captions. Fields and guidance wrap within the scrolling form.
- Automated coverage exercises all four recipes through serialization and compilation, preview/cancel preservation, invalid input, stale-preview collisions, explicit settings, failed IIS pair commits, dialog Apply, language switching and editor entry points. Controlled Windows install/repair/uninstall, populated OS DPI and assistive-technology qualification remain open.
- **Final verification:** the normal source build passed with zero errors and five existing test warnings (`p13-resource-recipe-final-build.log`). All 327 selected regressions passed (`p13-resource-recipe-final-regression.log`), including the earlier build/save/navigation/package checks, source binding, canonical schema/compiler contracts and new recipe coverage. This supersedes the preliminary 84-test recipe run; counts are not additive. No full-suite or live target-machine qualification is claimed.
- Stage D remains in progress: recovery discovery/history/retention work and qualification gates remain open. UX12 and Stages E/F remain open.

### Stage D recovery discovery progress — 2026-10-02

- UX11 gives each untitled controller/project a distinct recovery file instead of sharing one global draft. The Advanced recovery picker discovers those drafts and the legacy shared file, shows project identity/time/path, and prevents selecting unreadable drafts while preserving their files.
- Recovery opens an unsaved project in a separate session copy. The original draft remains available until a successful real save; failed saves preserve both. Starting another new project allocates a fresh recovery identity. Saving as a named project clears the preceding untitled recovery rather than leaving it undiscoverably orphaned.
- Opening a saved project in Advanced now offers Recover, open the saved version while retaining the draft, or Cancel. The controller still supports its existing automatic-recovery contract for programmatic callers. A skipped newer draft is not deleted by opening the saved version.
- Successful save and recovery-file cleanup are separate outcomes. Cleanup failures retain their paths, appear in save status/details and retry on a later successful save. A failed cleanup does not report the already-saved project as unsaved.
- Cleanup retains a content fingerprint. A draft changed after recovery or failed deletion is preserved instead of deleting another session's newer work. Tests cover both changed source drafts and changed cleanup-retry files.
- Six new strings cover all eight languages. The picker re-translates its actions/headers and RTL without losing selection, supports Enter/Escape and rejects unreadable entries. Regressions exercise separate sessions, discovery, recovery through a fresh controller, failed/ successful Save As, saved-version choice, invalid recovery preservation and locked-file cleanup/retry.
- Remaining UX11 work includes direct Guided recovery entry/discovery, a unified saved-project recovery/history browser and explicit retention/discard controls, backup-retention reporting, and controlled crash/restart qualification. Stage D and the complete P13 plan remain in progress.
- **Final verification:** the normal source build passed with zero errors and zero warnings on the final incremental rebuild (`p13-recovery-fingerprint-build.log`); the preceding rebuild had zero errors and 1,477 dependency/test warnings. All 358 selected regressions passed (`p13-recovery-fingerprint-regression.log`), including source binding, saved/untitled recovery, changed-draft preservation, populated picker selection/RTL and the earlier authoring/package/schema/compiler checks. All six recovery keys occur exactly once in each of eight resource files; `git diff --check` reports no whitespace errors. These counts supersede the preliminary 92/356 runs and do not establish full-suite or live crash/OS DPI/assistive-technology qualification.

### Guided recovery entry — 2026-10-02

- Guided's header now opens the same recovery picker and controller operation as Advanced. Its save/discard check includes uncommitted Project fields and Advanced script changes, using the actual Guided save action. Cancelling the save/discard check or picker retains the current graph, draft fields and step.
- Successful recovery returns to Project, rebinds fields to the recovered graph and marks the project as already created. Next edits that graph rather than replacing it, preserving AppId/resources and recovery identity. A controller already holding an empty recovered project is also recognized as an existing project at Guided startup. Active builds explain why recovery must wait.
- The recovery button uses the existing eight-language strings and updates its accessible caption on live language switches. The shared picker uses the visible Guided owner. Tests cover rejected confirmation, picker cancellation, pending fields, Arabic switching, recovery from a borrowed Files panel, and Next after empty/populated recovery.
- **Focused verification:** 40 Guided/recovery/picker/source-binding tests passed (`p13-guided-recovery-test.log`) using a normal source compilation with five existing test warnings and no compilation errors. Direct Guided recovery access is implemented; unified saved-project history, retention controls and crash/restart qualification remain open. P13 is still in progress.
- Full-suite qualification is running separately (`p13-guided-recovery-full-suite.log`); no full-suite pass is claimed yet. A testhost stack dump identifies existing `DryRunTests.Dispose` and `EnvironmentVariableStepTests.Dispose` waiting in Windows user-environment-variable cleanup. The test process remains active; it has not been cancelled or replaced.

### Cold-start recovery and qualification — 2026-10-02

- Cold start and Advanced now share the interactive saved-project opening helper. A newer draft requires Yes/No/Cancel; No keeps the recovery file, Cancel leaves the controller's graph/save state unchanged, and no newer draft opens without prompting. A cancelled/failed cold-start reopen stops the unused controller's autosave timer before falling back to Guided.
- New contract tests cover all choices, original/recovered identity and dirty state, cancellation without mutation and opening without a newer draft. These use the same helper called by both interactive entry points; the controller's existing programmatic `Open` contract remains intact.
- The earlier full-suite run was deliberately interrupted after a stack dump identified existing test-fixture cleanup waiting in `Environment.SetEnvironmentVariable(User)` notification broadcasts. Its 1,447 completed tests and four skips are partial evidence, not a full-suite pass. `DryRunTests` and `EnvironmentVariableStepTests` now clean only their uniquely named HKCU Environment values directly, leaving actual step tests/assertions intact. No production environment-variable provider was changed.
- Normal source compilation passed with zero errors and five existing test warnings (`p13-coldstart-recovery-build.log`). A full-suite rerun is tracked in `p13-coldstart-recovery-full-suite.log`; its completion remains unproven until a terminal result is recorded.
- The eight cold-start/shared-opening and Guided recovery contract tests passed separately against that source build (`p13-coldstart-recovery-focused.log`). The wider suite remains a separate, still-running qualification gate; its blocked notification test is not substituted with these focused checks.
- **Full-suite terminal result:** the cold-start build completed all 1,632 tests: 1,628 passed, four existing integration tests skipped and zero failed (`p13-coldstart-recovery-full-suite.log`, 25m56s). This replaces the earlier pending status for that build. It predates the saved-project catalog batch below and does not qualify that later code or the skipped real publish/install/feed scenarios.

### Saved-project recovery catalog — 2026-10-02

- Guided and Advanced's picker now combines independent session drafts with newer autosaves for the current/recent saved projects, deduplicates repeated paths and shows original project identity separately from the draft path. Older saved-project autosaves are omitted. Missing originals remain recoverable when their known path has an autosave.
- Named recovery opens a separate unsaved session copy while retaining the original document path and resolving relative paths against that document. Further autosaves update the session copy, preserving the selected source. The same Save action can recreate a missing original; Save As still commits identity only after success.
- A small origin sidecar preserves named session identity across restart/discovery. Missing/invalid origin metadata is not silently interpreted as a different project: malformed, null and empty metadata are disclosed and blocked. Successful cleanup removes the session script and its sidecar. Original-project captions are translated in all eight languages, and untitled row captions retranslate without losing selection.
- **Verification:** compilation against the usual test output reached locked-copy errors because the ongoing testhost held its assemblies (`p13-saved-recovery-build.log`); no compiler errors were reported. A separate `bin/P13SavedRecovery/` source build with all project references enabled passed with zero errors and 9,811 dependency/test warnings (`p13-saved-recovery-final-build.log`). All 93 selected saved/untitled recovery, save/race, populated-picker, Guided, dialog/cache and source-binding tests passed against those outputs (`p13-saved-recovery-test.log`). This does not replace the running full-suite gate or prove live crash/restart qualification.
- Remaining recovery work includes browsing projects outside the current/recent catalog, explicit history/retention controls and controlled crash/restart qualification. The full P13 goal remains in progress.
- Missing-sidecar detection now uses a distinct `.named.autosave.bsetup` session marker, so a missing sidecar cannot silently turn a named project into an untitled one. The separate output rebuild passed with zero errors and 1,477 warnings (`p13-saved-recovery-metadata-build.log`); 94 selected checks passed (`p13-saved-recovery-metadata-test.log`). After the older full suite finished, the usual source output rebuilt successfully with zero errors and 8,129 dependency/test warnings (`p13-saved-recovery-normal-build.log`), resolving the earlier file-lock failure. The 94-check result supersedes the preliminary 93-check run.
- **Final current-build regression:** all 373 selected authoring/recovery/build-workspace/navigation/package/schema/compiler/localization/source-binding checks passed from the usual outputs (`p13-saved-recovery-normal-regression.log`). This is broader than the focused 94-check run but is not a full-suite result for the saved-catalog build. The 1,628-pass full-suite result above belongs to the preceding cold-start build. `git diff --check` reports no whitespace errors; all eight original-project captions exist exactly once.

### UX12 implementation batch — 2026-10-03

- Added bounded, in-memory authoring history (100 transactions). History serializes configuration through masking getters: typed passwords, declared provider inputs, password custom fields and recognized sensitive key/value inputs are excluded as plaintext. Changing sensitivity metadata scrubs older snapshots as well. Current compatible credentials stay in the live graph; restored items with missing credentials remain empty and produce a re-entry warning. Account/certificate changes cannot reuse credentials belonging to another identity. Secret references remain references.
- Controller transactions group nested actions, resource recipes and source scans. Project/script replacement adopts matching resource identities; New, Open and recovery reset history. Save retains history and document identity, while save timestamps and credential-only changes do not create entries.
- Advanced and Guided expose Undo/Redo controls and Ctrl+Z/Ctrl+Y. Shared resource Add, Duplicate, Remove and guided replacements have individual transaction boundaries; field-edit bursts commit after the existing pause. Guided project details commit together. Graph restoration rebuilds cached section bindings and invalidates readiness/build attribution. Added 24 captions across eight languages with live Guided translation.
- Tests exposed and fixed two existing presentation defects: a resource-only project could be recreated when entering Guided mode, and project reload could clear its newly rebuilt section while hiding the welcome panel. Populated UI tests now exercise consecutive resource-button changes, each undo/redo and restored control availability.
- **Verification:** 13 core/controller history tests initially passed; focused Guided history checks passed after the project-detection fix. The final broader source-build regression passed all 174 selected history, Guided, recovery/save, resource, diagnostic, build-workspace and package-target checks (`p13-authoring-history-rebind-regression.log`). All 24 history resource entries parse and occur exactly once, and `git diff --check` reports no whitespace errors. These checks do not replace the full-suite or live deployment/accessibility gates.
- **Status: UX12 partial.** Remaining work includes explicit transaction coverage for every specialized component/custom-page editor and template/script presentation, native text-editor undo behavior, enabled-state/action feedback, Advanced live-language captions and manual qualification. Other Stage E/F features and the full P13 goal remain open.
- A final sensitivity guard also scrubs history when metadata is declared before the next transaction commit. All 19 focused model/controller/Guided/button tests passed after that change (`p13-authoring-history-final-focused.log`), including an explicit uncommitted-sensitivity regression. The broader 174-check result precedes this final guard; it is not a full-suite result for the history build.

### UX12 editor transaction coverage — 2026-10-03

- Added separate transactions for component Add, Duplicate, Remove, Move, file-list Apply, condition Apply, custom-action Apply, source-pattern changes and wizard-page selection. Component duplication allocates an unused ID; shared resource duplication now deep-copies nested models. Component actions wrap at narrow widths, and direct grid creation/deletion cannot bypass the explicit actions.
- Component file dialogs now edit cloned file operations. Condition editing in Advanced also uses detached component drafts and explicitly applies only condition lists/expressions. Cancel leaves the original graph unchanged, and successful Apply can be undone. Apply checks component identity before writing drafts to a potentially changed graph.
- History controls follow available actions and pending scripts; Advanced history captions update live with language changes. Ctrl+Z uses native undo when an editable text control has its own undo buffer. The toolbar retains explicit project-level undo. Restore actions update status and rebuild the appropriate script/generated editor.
- Added populated UI coverage for template apply/undo/redo, script apply/undo/redo and generated identity fields; component duplication and independent nested files; file draft/apply restoration; conditions and custom actions as separate transactions; button states, Arabic captions and focused text undo. The first keyboard test used a programmatic assignment that does not create native undo; it now uses the control's undoable Paste operation and passes.
- **Verification:** normal source build passed with zero errors and 9,908 dependency warnings (`p13-history-editor-build.log`). All five new populated presentation checks passed (`p13-history-editor-keyboard-retest.log`), and all 235 selected current-source history/authoring/recovery/build/package/feature-coverage/wizard checks passed (`p13-history-editor-final-regression.log`). The latter supersedes the preliminary run with one keyboard-test failure. No full-suite result is claimed for this batch.
- UX12 functional coverage now includes its template/script synchronization acceptance paths. Manual keyboard, language, DPI and accessibility qualification remains open, along with the broader P13 delivery gates. Stage E's release overview, update preview, comparison and contextual help are the next feature work.
- The final source rebuild and focused guard run passed all 25 model/controller/presentation/Guided history checks (`p13-history-editor-final-guards.log`). The new-project draft leaves project history disabled until creation, so an Undo action cannot create or replace the project. This final run follows the component-identity guards and draft safeguard; the 235-check broader run predates those final guards.

### UX13 release overview model — 2026-10-03

- Added a detached, read-only release presentation snapshot over existing `BuildPipeline.BuildResult`, signing evidence and `ReleaseQualificationPortfolioReport` contracts. Package artifacts, signing results, explicitly selected deployment files and qualification rows are separate collections. Reading an overview does not generate files, sign an artifact, run qualification or deploy anything.
- File availability is checked independently of reported build/signing success. Cancellation and errors cannot appear as a completed build. Qualification rows honor each existing report item's result and error diagnostics rather than trusting an aggregate success flag; report-file availability is separate from its reported result.
- Portfolio reports have no project/build identity contract, so their evidence remains unassociated with the current build. Deployment outcome is explicitly unknown, including when all selected assets exist. The snapshot omits signing credentials, remote endpoints, certificate file paths and raw error/audit details and stays unchanged when source reports/results are subsequently edited.
- **Verification:** seven release overview cases plus 20 authoring-history checks passed from the current source outputs (`p13-release-overview-model-retest.log`). The preliminary compile caught a test's use of the wrong diagnostic constructor; that test was corrected before the successful run.
- **Status: UX13 in progress.** The user-visible overview, selected asset/report loading, live localization and populated presentation checks still need implementation. UX14–20 and manual qualification gates remain open; no full P13 completion is claimed.

### UX13 release overview presentation — 2026-10-03

- Added the shared Release Overview dialog through the Advanced toolbar and Guided Review. Its separate tabs show package output/file availability, recorded signing results, explicitly selected deployment files and qualification portfolio evidence. It uses the retained build job identity and signals older project revisions; running jobs show Building and open dialogs refresh on build/reload events.
- Qualification loading is asynchronous, bounded to 4 MB and uses the existing portfolio contract. It rejects unreadable, malformed, ambiguous or incomplete report data, checks known diagnostic severities and resolves relative evidence paths against the selected portfolio. Failed loading preserves the prior report and selected assets. The screen identifies the selected portfolio path and UTC completion times, reports root/item failures separately, and keeps qualification evidence unbound to the current build because the contract does not establish that association.
- Reading or viewing files does not generate deployment assets, run qualification or deploy anything. Configured signing remains distinct from recorded signing success; unavailable package/report files remain visible. Raw qualification messages/diagnostics and signing credential/error metadata are excluded from the retained presentation data. Reported signing/qualification outcomes are historical claims, not a fresh verification of changed on-disk files.
- Added 15 captions to all eight languages (120 verified entries), live translation/RTL with retained selected tab/assets/report, minimum-size wrapping actions, accessible tab/text names and explicit event/font disposal. The dialog is included in the every-dialog render/interaction checks.
- **Verification:** the initial UI source build passed with zero errors and three platform warnings (`p13-release-overview-ui-build.log`). All 265 selected release, history, authoring, recovery, build-workspace, package, feature/wizard and dialog checks passed (`p13-release-overview-ui-regression.log`). These include the production portfolio writer contract, invalid/oversized data, error precedence, relative paths and populated German/Arabic layouts at simulated 100/125/150/200% scales. All 120 release entries parse and occur once; whitespace checks pass.
- UX13 now has its user-visible workflow. Final focused checks cover the subsequent live toolbar caption, both entry points, lifecycle guards and selected-report identity display. Live Windows deployment, OS DPI and assistive-technology qualification remain open. UX14 update preview is the next feature; the full P13 goal remains active.
- **Final current-source verification:** all 28 focused release model/reader/presentation checks passed after the subsequent changes (`p13-release-overview-final-focused.log`). This includes both actual modal entry points and successful/failed asynchronous loading. The 265-check result predates those final focused additions; neither run is a full-suite deployment or manual qualification result.

### UX14 update preview — 2026-10-03

- Added a read-only update review in the existing Update Center. It shows current/target channel, installed version, selected delta target and feed version separately, local/remote payload source and advertised delta/target-image sizes. Sizes are explicitly not required free disk space. The existing Apply path has no full-package fallback, and the preview states that limitation.
- Preview and Apply now share signed-feed numeric base-version selection. Missing or ambiguous deltas, denied transitions, untrusted feeds, unusable keys, absent sources, policy-blocked remote acquisition, untrusted local manifests and local/feed contract mismatches prevent Apply. Local preview checks the signed manifest and advertised versions/tree hashes; remote signatures and payload file hashes remain Apply-time checks. Install-directory availability is checked separately, and installed identity, staging, backup, resource/journal recovery and actual disk capacity are not claimed as verified by preview.
- Apply carries the reviewed feed digest into a fresh engine Check. A changed trusted publication requires a fresh preview before installation acquisition/staging/journal writes. Existing caller behavior remains available when no expected digest is supplied. Check retains the engine's existing feed acquisition/replay-ledger behavior; the preview does not apply an update, rotate backups or edit installation journals.
- The review explains that rollback needs a committed journal, an unchanged retained backup and resource recovery; a feed rollback version alone does not establish availability. External resource recovery can fail even when files are restored. Existing failure/recovery outcomes remain authoritative.
- Added 22 preview captions in all eight languages (176 entries), live dialog/preview translation and RTL without changing inputs, a copyable native read-only view, asynchronous bounded key reading and stale-input guards. Actions stay in a wrapping footer while inputs/review scroll. Layout testing exposed a content-fitting helper overwriting the dialog's minimum size; the dialog now retains its own metric-based minimum/default sizes.
- **Verification:** initial normal source build passed with zero errors (`p13-update-preview-build.log`). All 93 selected preview/Update Center/feed-engine checks passed (`p13-update-preview-focused.log`); after correcting the footer, all 27 focused checks passed (`p13-update-preview-ui-retest.log`). All 265 selected preview/feed/transition/delta/release/dialog checks passed after live translation and stale-result protection (`p13-update-preview-final-regression.log`). These cover real RSA-signed local packages, changed trusted publications, no stage/journal writes on digest mismatch, successful matching-digest application, blocked states and populated German/Arabic simulated 100/125/150/200% layouts. Final disposal/cancellation guards have a separate focused run.
- UX14 has its user-visible preview workflow. Live update acquisition/application, OS DPI, keyboard and assistive-technology qualification remain open. UX15 project comparison is next; UX15–20 and the full P13 delivery gates remain open.
- **Final current-source verification:** all 29 focused preview/Update Center checks passed after disposal/cancellation guards (`p13-update-preview-final-guards.log`). A disposed dialog cannot publish worker results or receive live language callbacks. The 265-check broader result predates those final lifetime guards; neither result is a full-suite or live deployment qualification claim.

### UX15 project comparison — 2026-10-03

- Added Compare Projects in the Advanced toolbar and Guided Review. Authors select a baseline `.bsetup` and review its configuration against current edits without opening/replacing the authoring document. The dialog highlights identity/version, paths/destinations, package/signing, files/resources and other canonical fields; Added/Removed/Changed are textual statuses. Category filters, field/value search and a copyable, scrollable selected-change detail view retain their state through live localization.
- Comparison extends the existing canonical exporter/template diff contract. A new internal value projection masks known protected members and dictionary keys before canonical JSON is created. Existing authoring-history sensitivity discovery is reused, with declarations from both models: a field newly marked sensitive cannot expose the earlier side's value. Plaintext credentials and secret references are excluded. Sensitive roles are masked conservatively across matching member types/keys. Returned snapshots contain detached read-only change records, not either source graph, script or executable plan. Source model/file contents and authoring history remain unchanged.
- Review has the existing canonical exporter's coverage/order semantics. It explicitly excludes protected values, custom actions, custom wizard pages and payload file contents; it does not claim full script-text comparison, fresh payload verification or build/install qualification. Package configuration changes are separate from resource changes, and advertised paths are configuration values rather than evidence that files exist.
- Baseline reading is asynchronous and bounded to 16 MiB. It uses the existing serializer and script-relative path resolution on the detached parsed model, so comparing an opened document to its own saved file does not report rebasing as a change. Raw parser errors can quote values, so failures use a generic localized message and preserve the previous successful baseline/review. Refresh rereads the selected file and captures current edits. UTC capture time makes the snapshot boundary explicit.
- Added virtual grid rows for large projects, a fixed-height row summary and full-value details so long resource values do not expand beyond the viewport. Minimum/default layouts use shared font/action/status tokens, follow live theme colors and preserve filters/selections during language/RTL changes. Selection/loading generations and cancellation prevent older or disposed requests from publishing results; event/font ownership is explicit. The dialog joins the existing every-dialog checks.
- Added 25 comparison captions in all eight languages (200 entries), including formatted baseline/capture/field-count metadata. Accessible names for dynamic labels, filters, actions, grid and details update with the displayed language; Choose Baseline and Close provide default/cancel keyboard actions.
- **Verification:** the first normal build hit a concurrent sibling BeepDM `reverseConfig` compile error; that checkout was not edited. Installer-only builds against existing dependency outputs passed, followed by a successful normal source rebuild with zero errors and 1,475 warnings (`p13-project-comparison-normal-rebuild.log`). All 47 preliminary comparison/history/template checks passed (`p13-project-comparison-focused.log`). Adding details exposed six populated scaled-layout failures; the corrected container passed all 14 presentation checks (`p13-project-comparison-layout-retest.log`). All 225 selected comparison/history/template/serializer/golden/dialog/Guided/update-preview/release checks then passed (`p13-project-comparison-final-regression.log`). These cover actual modal entry points, unchanged source models/files, credentials and newly declared sensitivity, relative paths, previous-review preservation, latest-load/disposal behavior, 500 resources with 4,000-character values, and populated German/Arabic simulated 100/125/150/200% layouts.
- **Final current-source verification:** all 19 focused model/reader/presentation checks passed after dynamic accessible-label/default-action changes (`p13-project-comparison-final-accessibility.log`), including a real theme event that preserves the comparison/search and restores current theme colors. The 225-check run predates those last label/default-action changes. Neither run is a full suite or manual Narrator/OS-DPI qualification result.
- UX15 has its user-visible canonical comparison workflow. UX16 contextual search/help is next. UX16–20, remaining cross-stage gaps and the P13 manual/live/usability release gates stay open; the complete P13 goal is not achieved yet.

### UX16 contextual search and local help — 2026-10-03

- Added a packaged offline help catalog covering every current Advanced navigation section (40 topics). F1 and the Advanced Help action open the active section's topic instead of the previous one-size message that described every artifact as Setup EXE. Guided F1 opens the current step/section topic, a visible Guided Help action is available, and Guided Ctrl+F opens help with search focused. Help reads static guidance only; it does not read credentials, create an uncreated project, validate, build or navigate around required-step gates.
- Extended the existing Advanced navigation filter with useful aliases, localized task phrases and token matching across topic titles, hints and help. Examples include open port, scheduled job, default program, digital signature, web server, background process and installation location. The two task examples in the localized empty state have explicit aliases across all eight languages, including languages without whitespace word boundaries.
- Filtered navigation rows show available views; full localized tooltips identify the corresponding Guided step or Advanced-only destination. Selecting a navigation result retains its existing Advanced section behavior. The help browser offers topic/available-view columns and full topic-specific guidance, live search and an explicit no-match state. It is a read-only guidance browser, not an alternate project editor or execution surface.
- Navigation filtering/language rebuilds restore selection quietly rather than reopening the active editor; clearing a filter also restores its prior selection when possible. Labels/tooltips/column names update with language, while the active editor and uncommitted text remain intact. The navigation and help browser use shared typography and theme colors with owned font/event lifetimes. Help preserves the selected topic/query through live language/RTL changes, remains scrollable at minimum sizes and is included in every-dialog checks.
- Added 43 help title/body/alias captions across eight languages (344 embedded entries). Guidance distinguishes install scope/privileges, files/components, prerequisite trust, resource/provider/package limits, firewall and task settings, IIS/services, packaging/signing, updates and build results. It explains policy/signature/evidence limits using existing authoring behavior and does not claim a project has been validated by opening help.
- **Verification:** a normal source build passed with zero errors and 1,475 warnings (`p13-context-help-build.log`). The preliminary focused run passed 26/28 checks; a missing installation synonym and a test invoking the wrong keyboard-handler signature were corrected. All 28 focused navigation/help/editor checks then passed (`p13-context-help-focused-retest.log`). The final source rebuild passed all 159 selected help/navigation/editor/every-section/every-dialog/Guided/history/comparison checks (`p13-context-help-regression.log`). These include actual modal keyboard-handler entry points, an unchanged uncreated Guided draft, localized port/task aliases in every shipped language, all 40 topic/destination mappings, format-argument checks for all new resources, quiet filter/language restoration, view indicators/empty states, real theme events and populated German/Arabic simulated 100/125/150/200% layouts.
- UX16 has its functional search/help workflow. General Advanced group/editor translation gaps and manual keyboard, Narrator, OS DPI and user-task qualification remain open with the cross-stage P13 gates. UX17 runtime operation summary is next; UX17–20 and the full P13 goal remain in progress. No full-suite or manual qualification result is claimed for this batch.

### UX17 runtime evidence — first implementation batch

- Ready now explicitly describes current-user/all-user scope, the selected install destination/type/Start Menu folder, selected components and estimated payload size. Counts cover configured prerequisites/catalogs/packages, registry/environment/service/task/firewall/association/certificate/COM/driver/provider/configuration/IIS/Web Deploy/shortcut/custom-action collections. Counts are explicitly configured intent, subject to conditions and detection, not an executed-operation report. Provider inputs and conditions are not copied into the summary. The summary scrolls and re-renders through the existing page language lifecycle.
- A detached `RuntimeOperationOutcome` captures the existing runtime's restart flag, attempted rollback action counts and previous-version file-backup recovery state. Completion presents the actual restart evidence. Failure retains the original failed-step message and log path, separates file backup success/failure/unknown/not-attempted from rollback attempts, and never claims complete recovery from action counts (the engine catches some errors inside actions). Backup restore exceptions no longer replace the original installation failure. The existing rollback manager shares the installation logger, so revert failures have log evidence. No new executor or cancellation capability was introduced.
- The wizard uses its actual Complete page as the terminal page, keeps Finish localized after live language changes, retains runtime context/log evidence on exceptions, and reports the existing reboot-aware success exit code in support metadata. Preview completion text survives rendering and does not claim an installation happened.
- Normal-source build passed with zero errors (`p13-runtime-summary-build.log`). The selected runtime summary/layout/RTL/exit-code/page-selection/context regression passed 82 checks (`p13-runtime-summary-final-regression.log`). New native checks cover both scopes, retained restart evidence, all backup outcomes, log/terminal-page wiring, no provider input exposure, and four simulated Ready-page scales. This does not qualify a real restart, locked-file replacement or system-resource recovery.
- UX17 remains open for eight-language resources for the new explanatory captions (currently English fallbacks), broader terminal-page layout/theme/accessibility work and controlled install/failure/recovery qualification. UX18–20 and cross-stage manual/live/usability gates also remain open. No full-suite result is claimed.

### UX17 localization and responsive pages — follow-up batch

- Added all 26 new runtime captions to the eight packaged languages (208 resources), including scope, configured-change limits, restart evidence, rollback counts, each backup outcome and preview/installed messages. Resource checks read the embedded language resources directly and verify matching keys and formatting argument requirements, without relying on English fallback.
- Ready, Complete and Error share a responsive runtime layout. Headings wrap, the readonly summary fills the remaining space and scrolls, and actions wrap or stack instead of using fixed coordinates. The summary's preferred text height cannot push actions off the page. Theme colors update through the existing theme event, fonts are owned/disposed, and native summary controls/actions expose localized accessible names and RTL reading direction. Language reload preserves operation evidence and launch/log choices.
- Installed/preview messages now retain a detached localized format and identity arguments, so the existing wizard language switch updates these messages live. A new result clears the previous format/evidence. Engine failure detail text is retained as engine evidence; the UI's failure and recovery explanations are localized.
- Normal-source responsive-page build passed with zero errors and 1,467 dependency warnings (`p13-runtime-layout-build.log`). Final normal-source selected regression passed 172 checks (`p13-runtime-layout-final-regression.log`), including 30 runtime-summary checks, existing runtime/context/exit-code/page/layout/RTL checks, all dialog rendering and theme/DPI checks. Native checks verify the global page bounds and lack of overlaps for visible actions and long readonly summaries, all three pages at German/Arabic 100/125/150/200% simulated scales, actual theme events, safe disposal, retained user choices and live localized identity formats.
- The prior UX17 caption/terminal-page presentation gaps are addressed by this batch. Real install/failure/recovery/restart qualification, actual OS DPI/Narrator/manual usability evidence, UX18–20 and cross-stage gates remain open. Programmatic scaling and readonly outcome presentation do not prove system changes or full recovery succeeded. No full-suite or live deployment result is claimed. UX18 support actions are next, using the existing `RuntimeRedactor` and support-bundle contracts.

### UX18 runtime support actions

- Error provides an explicit Copy error details action. The readonly error presentation and copied details use the existing `RuntimeRedactor`; copied text includes the failure/restart/recovery summary without changing installation or retry state. Clipboard failures are shown as support-action failures, and the original operation evidence remains visible. Tests exercise the actual button with a supplied clipboard sink rather than overwriting the user's clipboard. The shared redactor now handles quoted assignment values containing spaces.
- Both Complete and Error open a readonly support-bundle review dialog from the existing support action. The dialog loads captured diagnostics asynchronously, offers a native Save As action, performs export on a worker and prevents duplicate dispatch/late publication after disposal. It has live eight-language/RTL captions, the shared responsive/theme layout, localized accessible names, preserved review text and a clear saved/failed status. Cancelling the destination picker writes nothing.
- `RuntimeSupportBundleExport` validates a bounded 4 MiB captured JSON artifact, rejects duplicate/unsupported/invalid data and inconsistent recovery outcomes, and reuses the existing generator's JSON redaction. The central redactor now honors provider-declared `SensitiveInputs`, including nonstandard key names. Invalid/unavailable/oversized input has no exportable preview and never exposes parser errors or raw invalid content.
- Export writes exactly the detached, reviewed redacted snapshot to an atomically replaced destination. Later source edits/deletion do not change what was reviewed; the original captured artifact cannot be selected as the output path. Failed writes preserve previous exports and clean the owned temporary file. Export does not regenerate the live project/diagnostics or change original privacy/consent/result fields. The existing preview/consent metadata remains truthful, and the dialog asks users to review residual sensitive detail before sharing.
- Existing support generation now records the wizard's detached operation evidence: restart requirement, attempted rollback counts, backup recovery and explicit `fullRecoveryVerified: false`. Failed backup recovery is preserved as failure; reviewing, copying or exporting cannot turn it into a successful install/recovery. No recovery executor, upload or installer cancellation capability was added.
- Ten support captions ship in all eight languages (80 resources). The latest normal source build passed with zero warnings/errors (`p13-runtime-support-final-build.log`). Final selected source regression passed 196 checks (`p13-runtime-support-final-regression.log`), covering new core/native support scenarios plus existing runtime bundle/summary, every dialog, layout/RTL, exit-code, page and context checks. New checks cover redaction/declared inputs, captured privacy/failure metadata, invalid inputs, locked-output preservation, both real modal entrypoints, asynchronous disposal, native picker cancellation, embedded resources and German/Arabic minimum-size simulated scales.
- UX18 has its functional copy/review/export workflow. Real clipboard/Save As interaction, installed-machine failure/recovery scenarios, actual OS DPI/Narrator/manual usability and full-suite/cross-stage qualification remain open. UX19 template discovery/favorites and UX20 preview refinements are next. No live install/recovery or full-suite result is claimed.

### UX19 template gallery — built-in discovery and favorites batch

- Added a gallery for the five existing built-ins: text search across names/descriptions/categories and actual sample shortcut targets, category filtering, favorites filtering, readonly sample metadata, selection and a no-match state. Samples are derived from the existing creation factory and detached from its temporary models. Unknown entries have no applicable built-in sample and cannot be selected through the gallery. Browsing does not create or modify the current installer project.
- New Project, Guided creation and the existing template-update selector open the same gallery and select by ID into their existing combo boxes. Creation/apply still goes through the existing controller/template factories and diff/history paths. Cancelling gallery selection does not replace a project. A Guided live-language rebuild now restores the selected template by ID, fixing the earlier object-identity mismatch when its combo was recreated, while retaining uncommitted creation fields.
- Favorites persist only identifiers in the user profile's BeepInstaller configuration. Reads are bounded and schema checked; duplicate/unsupported/extra-field/invalid/oversized/unreadable data is preserved and cannot be silently overwritten. Writes use an owned temporary file and atomic replacement, preserve previous data on failure, and serialize independent store instances within the process. Favorite status is a UI preference, never template trust or authorization.
- Corrected the built-in service description to match its actual Startup shortcut. The factory does not create a Windows service registration; the gallery makes that sample limitation visible rather than presenting the template as a configured Windows service. Project/template execution behavior is unchanged.
- The latest normal source build passed with zero warnings/errors (`p13-template-gallery-final-build.log`). The normal source regression passed 149 selected checks (`p13-template-gallery-final-regression.log`), including new metadata/persistence/filter/modal/draft checks, existing project/signed-template verification checks, all dialog rendering and Guided/history/template regressions. New persistence checks cover corrupt/future/duplicate/oversized data, locked-output preservation/temporary cleanup, fresh-store round trips and concurrent store instances in the same process. These results do not qualify cross-process favorite concurrency or signed-package gallery import.
- **UX19 remains in progress.** Signed-package discovery/import in the gallery must retain the existing package service's signature/key/hash verification and preserve its actual package content; this batch does not introduce an importer or substitute a built-in for a signed package. UX20 and the cross-stage manual/live/full-suite gates remain open.

### UX19 gallery localization and layout qualification batch

- All 30 gallery captions now ship in eight languages (240 resources), including built-in descriptions, categories and actual sample shortcut locations. Shortcut targets remain literal. Search accepts localized metadata and retains English metadata for discoverability. Language changes preserve the query, category, selected template and uncommitted creation fields.
- Gallery launch buttons update their captions and accessible names on live language changes and unsubscribe when disposed. Gallery actions now explicitly receive the current theme colors alongside the search, list and sample detail controls.
- New embedded-resource checks verify matching keys and format arguments across all eight languages. Native checks cover localized search, sample descriptions/locations, launcher captions, draft preservation, theme updates and safe disposal. Populated German/Arabic layouts at minimum size pass simulated 100/125/150/200% scales, including long invalid-favorites status text, visible labels/actions, containment and overlap checks. These programmatic scales do not qualify actual OS DPI or assistive technology behavior.
- The verified installer build passed with zero warnings/errors against existing dependency binaries (`p13-template-gallery-localized-isolated-build.log`). After a transient missing `RequiredDataSourceRule` compile error in the sibling BeepDM project, the normal source-dependency build retry passed with 1470 warnings and zero errors (`p13-template-gallery-localized-build-retry.log`). The final regression after rebuilding the installer passed 167 selected checks using `BuildProjectReferences=false` (`p13-template-gallery-localized-verified-regression.log`); this is selected regression evidence, not a full-suite result.
- Signed-package gallery discovery/import, cross-process favorites concurrency, UX20 preview refinements and final live/manual/full-suite gates remain open.

### UX19 verified signed-template import foundation

- Added `VerifiedTemplateImporter` and an immutable captured `VerifiedTemplateBlueprint`. The importer reads bounded manifest/project/signature/key files, rejects duplicate JSON members and unsafe or colliding manifest filenames, and requires the existing package verifier's signature, project hash and trusted-key checks against a private snapshot of those exact bytes. Source edits after verification do not alter the captured project. The current exported package format uses flat files; nested manifest file paths are rejected. No signature bypass or built-in substitution is provided.
- Canonical JSON is reconstructed into the actual project model. Exact structural comparisons against both the reconstructed canonical JSON and a `.bsetup` save/reload reject unsupported or lossy content. Blueprint creation returns a separate graph each time. Typed failures do not expose parser errors, key material or invalid file contents. This core API does not execute installation, assign new product identity, apply a template to a live project or persist trust.
- Round-trip checks exposed and fixed serializer losses: component display names now persist, an explicit empty `[Components]` section stays empty (legacy scripts without that section retain their default Core component), authored output filenames retain version dots/extensions through `OutputFilename`, and Startup shortcuts retain their location. The legacy `OutputBaseFilename` read behavior remains supported.
- Normal source import/serializer/template regression passed 52 checks (`p13-template-import-roundtrip-final-tests.log`). Checks exercise all five genuinely signed exports, custom signed component content, detached creation after source changes, wrong-key/tampered packages, unsafe paths, unsupported fields, duplicate/future manifests, bounded input and legacy serializer behavior. The broader normal source authoring/recovery/gallery regression passed 249 selected checks (`p13-template-import-authoring-regression.log`). The final normal source build passed with zero warnings/errors (`p13-template-import-final-build.log`). No full-suite result is claimed.
- **UX19 remains in progress.** The gallery still needs signed-package discovery, public-key selection/review, verified sample display and controller integration for creation/template updates using the actual captured graph. Core verification evidence does not qualify those UI flows. UX20 and the cross-stage live/manual/full-suite gates remain open.

### UX19 signed-gallery selection and authoring integration

- The shared gallery now offers signed-package folder and trusted public-key selection. Verification runs asynchronously against the captured importer; selection is disabled while it runs, duplicate import attempts are guarded, and disposal prevents late selection publication. Picker cancellation and verification failure preserve the existing selection. Failed imports expose localized typed status, not parser errors or invalid package contents.
- A verified entry shows its actual component/shortcut sample, issuer, public-key/project SHA-256 values and a readonly protected canonical review. The review reuses comparison sensitivity rules to mask known protected members and provider-declared inputs. It distinguishes captured signed content from placeholder built-in samples, and explicitly states that verification neither validates payload files nor approves installation. The gallery does not execute templates.
- The selected descriptor carries its immutable verified blueprint through the existing combo boxes. New Project and Guided creation materialize that captured graph, generate a new AppId and apply the author's explicit name/version/publisher/source fields. Guided language and view rebuilds retain the signed descriptor. Template-update preview/apply use the actual blueprint, preserve existing product identity, output/destination/scope and signing fields, and retain the existing history/undo flow. Preview differences now use protected comparison values.
- Signed favorites persist only `signed:` content-hash identifiers. Favorite status remains a preference and cannot create a verified blueprint. Imported descriptors remain available in the current selector/gallery session; this batch does not persist a package library or silently trust a favorite on a future session.
- Eleven new signed-import captions ship in eight languages (88 new resources; 41 gallery captions / 328 total gallery resources). The native key picker reuses the existing localized public-key filter. Normal source regression passed 173 selected import/gallery/template/comparison/Guided/history/dialog/serializer checks (`p13-signed-gallery-final-regression.log`). After the final review wording change, all 31 gallery checks passed (`p13-signed-gallery-layout-final-tests.log`), including both real modal New/Update entrypoints, detached graph creation, preserved update identity/signing/undo, failed/cancelled import, safe disposal, resource formats and signed German/Arabic minimum-size 200% simulated layouts with unreadable favorites. The final normal source build passed with zero warnings/errors (`p13-signed-gallery-reviewed-final-build.log`).
- **UX19 remains in progress.** Saved signed-package discovery across sessions and its stale-source/key/hash handling remain open, along with cross-process favorites concurrency. Real OS picker interaction, OS DPI, assistive technology, user-task and live/full-suite qualification remain unverified. UX20 preview refinements and the remaining cross-stage P13 gates are still open.

### UX19 saved signed-template discovery

- Added a per-user signed-template library containing only package/public-key source references and reviewed manifest, project and public-key SHA-256 pins. It does not copy package graphs, signing/private-key contents or protected project values. Reads are bounded to 256 KiB / 32 records, reject duplicate/unsupported/malformed entries, and preserve damaged or unavailable files. Saves use owned temporary files and atomic replacement, retaining prior data on failure.
- Every discovery reruns the existing captured signature/hash/key importer against the source files and matches all three reviewed pins. Even validly signed changes to project content or manifest metadata require explicit reimport. Changed, missing, oversized or unverifiable sources do not become selectable library entries; their saved records remain intact. Explicit successful reimport replaces the source reference/pins rather than duplicating that package.
- Gallery loading starts asynchronously during form loading. Verified saved samples join the existing filters/favorites without replacing the current selection. Successful explicit imports remember their reviewed references; a failed catalog save still allows the newly reviewed session template and displays a warning. Disposal guards both discovery and import publication. New-session native checks prove that unchanged pinned favorites reappear, while changed sources stay absent. Two library captions ship in all eight languages (43 gallery captions / 344 resources).
- Favorites and library mutations now take a path-specific named mutex in addition to atomic replacement; this establishes coordination across processes. The current test evidence covers existing concurrent store instances within one process, not independent-process qualification. That remaining gate is not claimed as passed.
- The normal source build passed with zero warnings/errors (`p13-template-library-final-build.log`). Normal source regression passed 164 selected library/import/gallery/template/comparison/Guided/dialog checks (`p13-template-library-final-regression.log`). After adding catalog-size, native failed-save and disposal checks, all 58 focused library/import/gallery checks passed (`p13-template-library-actions-final-tests.log`). No full-suite or manual/live result is claimed.
- UX19 has its discovery/import/favorites/creation/update workflow. Separate-process preference qualification, real picker interaction, actual OS DPI/accessibility, user-task and live/full-suite gates remain open. UX20 preview refinements and the remaining cross-stage P13 requirements are next; the full implementation goal is not complete.

### UX20 preview controls — first implementation batch

- Wizard preview now captures an independent `.bsetup` project snapshot rather than sharing the editable authoring graph. Reset recreates the wizard from that original capture. Preview component/context changes leave the authoring graph and its dirty state untouched.
- A page selector uses the actual configured wizard page list, including terminal pages. Its internal navigation API rejects real installer forms and bypasses installation dispatch. Completion displays the existing truthful preview result; failure uses an explicitly simulated message. Finishing completion or closing the embedded wizard closes the preview owner, while resetting disposes/replaces the old child without closing the owner.
- Added 100/125/150/200% layout dimension simulation in a scrollable viewport. Scale changes retain the page/context; reset retains the chosen scale and returns to the first page. This is deliberately described as layout simulation, not actual OS DPI or font/accessibility qualification. Native scaling checks exposed and fixed inherited form minimum-size/double-scaling behavior.
- Existing wizard language controls remain available. The wrapper follows live language/RTL changes and refreshes its page captions after the child reloads, including after reset changes subscription order. **Six new preview captions currently use English fallbacks; eight-language resources, broader populated page scenarios, theme/layout and terminal-message localization checks remain open.**
- Inspection found prerequisite download/install actions could still launch processes in the old preview. A dedicated preview context flag now disables the buttons and guards both browser launch and the prerequisite installation method. No installer executor was added to preview. Dedicated prerequisite-action and complete no-side-effect qualification remain to be added; source guards alone do not qualify that full gate.
- The initial normal source build passed with zero warnings/errors (`p13-preview-controls-build.log`). A later source dependency rebuild hit a transient missing `RequiredDateTimeRule` error in sibling BeepDM. Installer builds against existing dependencies passed with zero warnings/errors; 75 selected preview/dialog checks and the final four native control checks passed using `BuildProjectReferences=false` (`p13-preview-isolated-regression.log`, `p13-preview-final-controls-tests.log`). The normal source build retry then passed with 1470 warnings and zero errors (`p13-preview-source-build-retry.log`). The final normal source regression passed 76 selected preview/dialog checks (`p13-preview-final-source-regression.log`). No full-suite, actual OS DPI or manual/live qualification is claimed.
- UX20 and the full P13 goal remain in progress. Preview localization/scenarios, separate-process preference checks, earlier cross-stage authoring/accessibility gaps and final live/manual/full-suite gates remain open.

### UX20 localized preview and prerequisite action isolation

- All seven new preview captions now ship in eight languages (56 resources), including the simulated failure and the explicit not-checked prerequisite state. Embedded-resource checks verify all keys, nonempty translations and matching format arguments. Native checks exercise all eight languages after reset recreates the wizard, confirming wrapper captions, accessible names, page choices, selected page/scale and simulated error text remain synchronized.
- A selected simulated failure now refreshes its body on live language changes. Preview-only prerequisite status rows also refresh their localized not-checked message. Runtime prerequisite behavior is unchanged by these preview branches.
- Further inspection found prerequisite detection can execute authored commands even before an install. Preview now creates not-checked display results instead of calling the detector, permits preview navigation without claiming requirements are met, and retains disabled/guarded download/browser/install actions. A native marker-command fixture proves its authored detection command remains unexecuted. The same check invokes the guarded install method directly and verifies immediate completion, unchanged result/status and disabled download/install controls, including a live Arabic status update.
- Final normal source regression passed 79 selected preview/dialog checks (`p13-preview-localized-safe-regression.log`). The final normal source build passed with zero warnings/errors (`p13-preview-localized-final-build.log`). This evidence covers the configured prerequisite-command/action path and preview translations, not every possible operation or full end-to-end installation suppression scenario.
- UX20 still requires broader populated page-selection/theme/scale scenarios and complete install-action/no-side-effect qualification. Separate-process preferences, actual OS DPI/accessibility, real user-task and live/full-suite gates and earlier cross-stage P13 requirements remain open. The full implementation goal is not complete.

### UX20 populated preview, theme and install-action isolation — 2026-10-03

- The preview wrapper and page/scale/reset controls now use shared application theme tokens and an owned body font. Live theme changes update the controls; disposal removes subscriptions and releases the font.
- Native German/Arabic checks populate components and custom text/password/radio fields, restrict enabled built-in pages, and verify the captured page graph. At minimum window size, controls remain contained and non-overlapping; all pages remain selectable at simulated 100/125/150/200% scales and oversized content exposes both scrollbars. Custom validation and password masking work, and the author's graph stays unchanged. These checks do not qualify actual OS DPI or every populated page layout.
- A native preview install-action fixture accepts the license and invokes the actual asynchronous install method from Ready with a real payload and authored command sentinel. Completion shows the localized preview-only message; no payload copy, authored command, install directory, installation log or runtime support bundle is produced. The initial test's asynchronous continuation stalled under the test synchronization context; supplying a WinForms synchronization context and pumping events resolved the fixture without changing production execution.
- Final normal source regression passed 147 selected preview/dialog/runtime-summary/support-action/theme checks (`p13-preview-populated-final-regression.log`). Final normal source build passed with zero warnings/errors (`p13-preview-populated-final-build.log`). These are selected checks, not a full-suite result.
- UX20 has functional preview controls and evidence for these configured page and install paths. Separate-process preferences, broader Guided/Advanced presentation, exact diagnostic-field focus, actual OS DPI/accessibility, representative author tasks, live runtime qualification and current full-suite gates remain open. P13 remains in progress.

### UX06 dictionary diagnostic editing and Guided handoff — 2026-10-03

- Canonical dictionary-input paths now select the actual key in a property-grid view over the existing dictionary. Keys containing dots, hyphens or brackets are preserved as literal input names. A diagnostic for a missing input exposes an empty editable field without adding the key until an edit is committed. The resource and neighboring values are preserved.
- Sensitive-input validation (`BI1703`) now identifies `Resources[index].Inputs.key` so its Review edit link can reach the value needing correction. The property view masks provider-declared sensitive keys and values recognized by the existing authoring sensitivity rules. Input edits use the resource editor's existing dirty/readiness/history notification.
- Native checks exposed and fixed two handoff/navigation defects. Returning from the actual Advanced → Guided modal flow now retains the Review diagnostic's Advanced section instead of reopening Identity. Focus is deferred while the target panel is hidden and applied when the editor becomes visible; changing the project or destination discards pending focus. Programmatic navigation explicitly clears other cached selections before selecting a row, preventing hidden/pre-handle selections from reverting when the native ListView is created.
- Native checks exercise the actual Review diagnostic button and modal handoff, hidden/show-again focus, missing/existing sensitive values, literal punctuation in keys, sibling/resource preservation, undo/redo, owning-row restoration after invalid nested navigation, and stale-focus cancellation. The final normal source regression passed 111 selected diagnostic/Guided/section/resource/history/schema/wizard/help checks (`p13-dictionary-final-regression.log`). The final normal source build passed with zero warnings/errors (`p13-dictionary-final-build.log`). This selected evidence does not prove every diagnostic field or manual keyboard/accessibility journey.
- This closes the demonstrated typed-resource dictionary-input focus gap. Broader presentation and field-navigation audits, separate-process preferences, actual OS DPI/accessibility, representative author tasks, live runtime and current full-suite qualification remain open. P13 remains in progress.

### UX19 independent-process preferences — 2026-10-03

- Added a checked-in console test worker built into an owned temporary output directory. It loads the currently built Core implementation and runs as independent hidden `dotnet` processes against isolated preference and genuinely signed-package fixtures. A readiness/start gate places writers and readers in contention. Worker output contains typed results and bounded exception type/HRESULT/method metadata, not file contents, credentials or parser messages; no installer CLI or user-profile preference files are changed by these tests.
- Process checks exposed two persistence defects. Adding a 257th favorite could previously create a file rejected by the next read; writes now enforce the existing 256-entry bound and preserve the original bytes when full. Concurrent readers could also leave handles open during a subsequent destination replacement, producing a Windows access-denied failure in `MoveFile`. Both stores now coordinate reads and writes through the same path-specific named mutex. Favorites acquire that mutex before the in-process gate, preserving consistent lock order; library writers call the internal read while already holding the lease. The existing five-second bounded lock wait and typed unavailable results remain in effect.
- Eight independent favorite writers plus a reader retain all IDs, including differently cased paths to the same Windows file. Four contenders at the 255-entry boundary permit exactly one new favorite; subsequent overflow is refused without changing the file. Five library writers plus a reader retain every separately signed source. A fresh discovery process verifies all five packages; after a source is changed, another process rejects that entry while preserving the catalog and other four entries.
- Further process checks prove damaged favorites/library files cannot be replaced, owned temporary files are cleaned after writes/refusals, and terminating a worker while it holds the actual production mutex permits a later worker to acquire the abandoned lock and preserve/add favorite IDs. This is evidence for abandoned-lock recovery, not interruption during every disk-write phase or cross-session/network-filesystem qualification.
- Final normal source regression passed all 63 selected process/gallery/library/import checks (`p13-template-process-fixed-tests.log`), including five new independent-process scenarios. Final normal source build passed with zero warnings/errors (`p13-template-process-final-build.log`). The earlier concurrent failures are retained in the initial/contention logs; no current full-suite result is claimed.
- UX19 now has independent-process evidence for the tested local Windows preference workflows. Broader presentation/field audits, actual OS DPI/accessibility, representative author tasks, live runtime and current full-suite qualification remain open. The full P13 goal is still in progress.

### UX07–08 Advanced live captions and RTL focus — 2026-10-03

- Shared bound-field, checkbox, enum, file/folder and section-heading helpers now retain resource keys and refresh captions and accessible names when language changes. They never replace authored input values. Resource sections refresh headings, help, actions, empty-state text and grid/property-grid accessible names without replacing the model or selected row. Subscriptions end with their controls. Eleven navigation/field keys ship in all eight languages (88 resources).
- Navigation group titles refresh without reopening the editor, expanding collapsed groups or firing a section-change event. Rebuilding the native list suppresses selection notifications across the entire update and preserves nested notification guards. English group names remain searchable alongside translated names.
- Native checks reproduced Arabic focus loss: the form's active control was a surrounding container rather than the focused text field. A focus bookmark now captures the actual focused leaf and text selection before RTL handle changes, then restores them with visibility, lifetime and form-ownership guards. Queued restoration respects later navigation, another focused window and a newer caret selection. Hidden forms do not capture stale native focus.
- Six new checks cover all eight languages, collapsed groups, unchanged authored fields and canonical scripts, enum/checkbox values, selected resources, subscription disposal, focus/selection and moved/disposed controls. The final normal source regression passed all 75 selected localization/help/diagnostic/Guided/resource/section checks (`p13-advanced-live-verified-regression.log`). The final normal source installer build passed with zero warnings/errors (`p13-advanced-live-verified-build.log`). Earlier failed focus and lifecycle runs remain in their logs; fallback dependency builds were used only during diagnosis, not for this final verification.
- This qualifies the tested shared editor paths. Toolbar/search captions, hand-built editors, Guided rebuilt-control focus and broader populated layouts remain audit work. Actual OS DPI, assistive technology, representative author tasks, live runtime journeys and a current full-suite run are still open. P13 remains in progress.

### UX07–08 live authoring chrome and Guided rebuilt-control focus — 2026-10-03

- Toolbar buttons now retain resource captions and tooltips and update their accessible names on live language changes. Search placeholder/name and the editor title refresh without replacing the search query, text selection, current panel or project. Recent menu captions and empty/clear actions refresh while authored filenames and paths remain unchanged. Replacing menu items disposes their language subscriptions; two new menu keys ship in eight languages (16 resources).
- Guided language changes capture focus before rebuilding owned controls. Stable field/template/step identifiers permit restoring the matching replacement control with its text selection, guarded by type and form ownership. This retains the pending Project draft, non-default template choice, template-picker focus and stepper-button focus. Existing lifetime/visibility/navigation guards remain in effect.
- Native checks cover the toolbar captions and hints, search/title, Recent refresh/disposal and the Guided draft fields across all eight languages. The initial Guided fixture incorrectly required built-in template object identity even though each catalog call creates new objects; it now verifies the selected non-default template ID. The final normal source regression passed all 77 selected localization/help/diagnostic/Guided/resource/section checks (`p13-chrome-guided-final-regression.log`). Final normal source installer build passed with zero warnings/errors (`p13-chrome-guided-final-build.log`). Nullable annotations in the new assertions were cleaned up afterward without changing their behavior.
- Source inspection finds no form-wide live theme subscription in either authoring shell; individual navigation/build controls already have subscriptions. Shared shell/editor live-theme coverage is the next presentation task, alongside remaining hand-built caption/dynamic-message coverage, populated layout and broader diagnostic-field audits. Actual OS DPI, assistive technology, representative author tasks, live runtime and current full-suite qualification remain open. P13 is still in progress.

### UX07 shared authoring live palettes — 2026-10-03

- Both authoring shells now own a shared live theme binding for existing, cached, borrowed and newly added controls. Explicit surface/tone roles preserve sidebar, primary-action, muted, success, warning and error semantics. Readiness and build outcomes set roles from their typed state, so later theme changes do not erase their meaning. Fonts, text, tags, binding sources and selected models are not replaced by palette refreshes.
- Resource grids update body/header/selection colors; property grids update view, help, category and focused-item colors through public APIs. Native composite internals retain their own styling: the initial check found recursively painting PropertyGrid children overwrote its public help color, and the binding now excludes native private child trees. Toolstrip/menu rendering resolves readable text against the selected surface, and tabs use a themed caption/focus renderer. Existing navigation/build-workspace bindings keep their own ownership.
- Three native checks switch between shipped light and dark palettes. They verify cached fields/resources/results, selected rows and property objects, canonical scripts, keyboard focus/text selection, Guided borrowed-panel ownership and return, selected tab rendering, and controls added while dark. An isolated lifecycle check repeatedly adds/disposes control trees, verifies watch records are removed, and proves the global subscription ends on disposal. The first build-result fixture edited the project after recording its result; that correctly invalidated the result. The fixture now starts with authored values before recording the failed build, qualifying a current outcome.
- Final normal source regression passed all 88 selected theme/localization/help/diagnostic/Guided/resource/section checks (`p13-authoring-theme-verified-regression.log`). Final normal source installer build passed with zero warnings/errors (`p13-authoring-theme-verified-build.log`). This evidence covers these palettes and configured native paths, not all themes, OS high contrast, actual DPI or assistive technology.
- Remaining work includes hand-built/dynamic captions, artifact-appropriate inline result labels, shared font audit, populated layout/field-navigation coverage, actual OS DPI/accessibility, representative author tasks, live runtime journeys and a current full-suite run. P13 remains in progress.

### UX07–09 inline artifact results, cached palettes and fonts — 2026-10-03

- Inline result views now describe EXE, MSI, MSIX, MSIX bundle and ClickOnce outputs with a generic artifact label. ClickOnce uses Open manifest. Completed/failed/cancelled status, source-script labels, size/time, issue counts and the last-build status link refresh in all eight languages. Four new metric/status resources ship in every language (32 resources); formatting follows the selected culture.
- The result page uses wrapping captions, automatic rows and wrapping actions instead of fixed vertical positions. Native minimum-size checks preserve the cached result/workflow panels, authored output paths, focused path field and text selection during every supported language switch, including Arabic. Artifact actions require an existing output; launch also checks successful, noncancelled state inside the handler. Five inert artifact fixtures exercise the actual button and injected launch boundary without executing packages.
- The legacy section styling pass previously overwrote the current palette when cached editors reopened and replaced explicit fonts on descendants. Sections now inherit the shell body font while preserving custom fonts; palette refresh follows structural styling and respects native composite ownership. Primary-action roles and property-grid help settings remain intact. Guided uses the same body font. Native checks retain monospace editor fonts and dark colors across section changes.
- Seven focused checks passed (`p13-result-captions-tests.log`). After additional status-link/resource assertions, the normal source regression passed all 124 selected result/theme/localization/help/diagnostic/Guided/resource/build-workspace checks (`p13-inline-results-regression.log`). These checks qualify the tested presentation and action boundaries, not live installation or actual OS DPI/accessibility. Remaining hand-built source-pattern/source-scan/component captions, broader fields/layouts, representative author tasks, runtime journeys and current full-suite qualification remain open. P13 is still in progress.
- Final normal source installer build passed with zero warnings/errors (`p13-inline-results-final-build.log`). The initial source build reported existing dependency/analyzer warnings; its zero-error result is recorded separately in `p13-result-captions-build.log`.

### UX07–08 source, pattern and component live captions — 2026-10-03

- Pattern headings, input/list accessible names and Add/Remove captions now retain resource keys. Action rows wrap rather than keeping a fixed height. BindingSource receives explicit observable-collection notifications, fixing the displayed list remaining unchanged after Add/Remove; the panel removes the collection subscription and disposes the binding when closed.
- Source directory heading/input/browse name, Rescan and tree accessible name refresh without replacing authored paths or the tree. The source header wraps. Scan summaries now retain format arguments instead of a preformatted English status; scanning/completed/failed captions refresh with the current culture. The source summary resets on source-directory/project changes. A later unrelated status message keeps ownership of the status bar during language changes. Async scan UI completion checks disposal and project identity; this is not qualification of every scan concurrency scenario.
- Component action captions, accessible grid/property names, five declared column headers and move-arrow accessible names refresh without replacing the selected resource or canonical script. Arrow glyphs stay unchanged. Seven new keys ship in every supported language (56 resources).
- Three native checks passed (`p13-source-caption-focused.log`): all-eight-language draft/focus/text/list-selection preservation, actual pattern Add/Remove/list updates, selected component/header/action preservation and a real scan of an owned temporary source fixture. Scan captions use the actual file count/size, tree paths and project remain unchanged during language switches, and later status/source changes are respected. The initial test build used `using` on the nondisposable controller and was corrected before these tests ran.
- Final normal source regression passed all 127 selected source/result/theme/localization/help/diagnostic/Guided/resource/build-workspace checks (`p13-source-caption-regression.log`). Remaining caption work is identified in Current next actions, alongside broader field/layout/concurrency audits, actual OS DPI/accessibility, author tasks, live runtime journeys and current full-suite qualification. P13 remains in progress.
- Initial normal source installer build passed with zero warnings/errors (`p13-source-caption-build.log`). After the successful regression, the final source rebuild failed with five compiler errors in sibling BeepDM `MigrationManager.ExecutionOrchestration.cs` and `MigrationManager.RolloutGovernance.cs` (`p13-source-caption-final-build.log`). Those external files were preserved. The installer builds with zero warnings/errors using `/p:BuildProjectReferences=false` and the existing dependency binaries (`p13-source-caption-dependency-build.log`); this is not a passing final full-source build. Complete source-graph qualification must be repeated after the sibling errors are resolved.

### UX07–09 script/workflow/log/help/welcome captions and native RTL lifetime — 2026-10-03

- Identity guidance, package help, welcome captions/actions, script Apply/Reload and modified status, legacy log actions, workflow headings/actions/output labels/results and pending report hints now refresh live. Script/log action rows wrap and their buttons size to translated text. The build action uses a generic Build caption for every target. Six new keys provide 48 resources in eight languages; the keyboard guide composes translated action labels while retaining the actual shortcut keys and search/section/field instructions.
- Pending report hints refresh only until a report is populated. Existing validation/capability report text is preserved during language switches; translating that report presentation remains an audit item. Script drafts, canonical project values, log text and authored output paths are not replaced. The detached welcome panel is explicitly disposed with its owning editor, fixing five language subscriptions left behind after it was removed from the content host.
- Native checks exposed an RTL error on the populated build workflow: `BuildWorkspaceView.OnHandleCreated` refreshed state and changed RightToLeft during an ancestor's handle recreation. Handle creation now refreshes display state without starting a direction transition; the normal language event updates direction afterward. This removes the reentrant transition. Caption refresh must complete without a swallowed LanguageManager diagnostic in the tested workflow. Host-reparenting/recreation experiments were removed after locating this cause; their failed evidence remains in the diagnostic logs.
- Final focused checks passed all 21 source/result/Advanced/workflow localization cases (`p13-workspace-direction-checks.log`). Three new checks cover all eight languages, script drafts and dirty state, focus/text selection, monospace fonts, log preservation, workflow headings/pending hints/shortcut guide, authored output, retained generated reports and identity/package/welcome captions. The existing lifecycle check verifies editor disposal returns the language-listener count to baseline.
- Expanded regression passed all 144 selected localization/theme/help/diagnostic/Guided/resource/build-workspace/authoring/DPI-token/section-rendering checks (`p13-workflow-caption-regression.log`), using `/p:BuildProjectReferences=false` because the sibling BeepDM source graph has compiler errors. Installer compilation against existing dependencies passed with zero warnings/errors (`p13-workspace-direction-build.log`). This is selected evidence against those dependency binaries, not full-source/full-suite or actual OS DPI/assistive-technology qualification. Broader report/status/resource-caption/field/layout audits and Windows journeys remain open; P13 remains in progress.
- The subsequent complete source rebuild passed with 1,469 dependency/analyzer warnings and zero errors (`p13-workflow-caption-final-source-build.log`). The five sibling migration errors were resolved externally; those files were not edited for this installer task. All 144 selected checks then passed against the rebuilt source graph (`p13-workflow-caption-source-regression.log`), with no skipped checks. Full-suite and remaining qualification gates stay open.

### UX07–08 system-resource headings/help — 2026-10-03

- Scheduled tasks, firewall rules, certificates, COM registrations, driver packages, configuration transforms, IIS application pools, IIS sites and Web Deploy editors now bind their existing localized title keys and new help keys. Nine help keys ship across eight languages (72 resources); all 18 heading/help keys are present and nonempty in each language, with valid XML and no duplicate keys.
- Nine native populated-editor cases add actual rows through the UI and retain the second authored resource across all eight language switches. They verify title/help text and accessible names, grid/property names, selected binding row and property object, keyboard focus, unchanged canonical project values, detached cached-panel translation and reopening, and disposal returning language-listener counts to baseline. These checks do not execute the authored system resources.
- Normal source installer build passed with zero warnings/errors (`p13-system-resource-captions-build.log`). All 99 selected Advanced/Guided/localization/theme/resource/workflow/source/feature checks passed with no skips in 4m49s (`p13-system-resource-captions-regression.log`); the test compilation reported five existing nullable/analyzer warnings. `git diff --check` found no whitespace errors. Remote fetch/pull again confirmed `6c2f148`, no divergence and already up to date; existing local changes were preserved.
- Generated report/status captions, remaining field/layout/concurrency audits, current full-suite checks and real Windows/accessibility/usability/runtime qualification remain open. P13 remains in progress.

### UX06/09 source-tree, diagnostic routing and artifact guards — 2026-10-03

- Build-workspace artifact actions require a successful, noncancelled result and an existing artifact. The handler rechecks current result flags independently of the button state. Failed/cancelled results can still open their existing output directory for inspection. Native cases cover failure, cancellation, conflicting success/cancel flags, direct handler invocation and a result invalidated before a button refresh; launch delegates record inert fixture paths rather than executing artifacts.
- Root-only Setup diagnostics open Identity without substring errors. Setup.SourceIncludes and Setup.SourceExcludes route to Includes, including indexed paths. The navigator selects the exact list row; absent/out-of-range indices do not change another selection or mutate the model. Native cases remove the selected pattern through its actual button while preserving the other list, and root-path cases retain canonical values.
- File-tree generation advances before the empty/invalid-source return, so such a change supersedes a previously completed but queued refresh. Two native race cases hold the actual worker continuation, change the source to empty or nonexistent, then release the old continuation and verify that the tree stays empty and the project stays unchanged. This does not qualify the separate mutating source-scan worker.
- All 32 selected workspace/diagnostic checks passed (`p13-workspace-pattern-guard-focused-retry.log`); both queued-tree cases passed (`p13-file-tree-revision-focused-retry.log`). The initial compiler attempt had a local-variable shadowing error, corrected before tests ran; the initial tree cases used the wrong control name, corrected to the actual SourceFiles control before they passed. The normal source graph compiled during these runs; existing dependency/test warnings remain. Final expanded regression passed all 117 selected workspace/result/diagnostic/source/Guided/localization/history/cache/section checks, with no skips in 5m44s (`p13-workspace-pattern-tree-regression.log`). This does not qualify the full suite or live Windows journeys.
- Final normal source installer build passed with zero errors and 9,995 dependency/analyzer warnings (`p13-workspace-pattern-tree-final-build.log`). `git diff --check` found no whitespace errors. Report/status localization, broader field/layout/concurrency audits, current full-suite and Windows/accessibility/usability/runtime qualification remain open. P13 remains in progress.

### UX07/11/12 detached source scanning — 2026-10-03

- AuthoringSourceScan captures a detached full JSON model, retaining file metadata omitted by the script representation. A SHA-256 state hash, original project reference and request generation identify whether the result is still current; unrun/failed/already-applied requests cannot apply. The worker calls the existing SourceScanner only against the candidate. The synchronous controller Scan API uses the same capture/apply boundary.
- Apply preserves the original project, existing core component, previously authored file objects and existing prerequisites. It updates the scanned files/size, discovered source directory and detected executable, creates a core component only when needed, and appends only new suggested prerequisites. Changes mark the project dirty and record one scan transaction for undo/redo; pre-existing field edits commit separately. A pending valid script applies before capture and returns to the originating section, while a later unapplied script edit rejects the scan. If the author opens the generated script during scanning without editing it, the completed scan refreshes that view; pending authored script text remains protected.
- The UI checks disposal and project replacement before any application. Same-project edits, including unnotified nested fields, discard the result; stale failures are discarded without showing an error for the new source. One translated discard/rescan caption ships in all eight languages. Native cases hold the real scanner worker, exercise later edits/replacement/disposal and retain cached component selection. Discard captions refresh across all eight languages while preserving values and focused fields.
- Initial checks exposed a script-state comparison that missed file-description edits (dictionary input edits were already detected), and a fixture using an unsupported singular runtimeconfig framework shape. Full-model hashing and the supported frameworks-array fixture corrected those failures. A native invalid-source focus case was corrected to settle initial UI messages and establish focus before culture changes. The initial normal source build passed with 1,469 dependency/analyzer warnings and zero errors (`p13-detached-scan-build.log`); selected native focus checks then passed all seven cases (`p13-detached-scan-ui-focus.log`). The first expanded run passed 107 cases (`p13-detached-scan-regression.log`). After the two script-flow additions, final normal source regression passed all 109 selected scan/history/Guided/localization/source/workspace/snapshot/workflow checks, with no skips in 5m12s (`p13-detached-scan-final-regression.log`). These runs are not additive. Five existing nullable/analyzer test warnings remain.
- After the successful regression, the final installer source rebuild failed with five new errors in sibling BeepDM SecurityManager.cs (missing LINQ extension resolution/type inference) and ManagedFilterCompiler.cs (missing ParseCollectionFilterValues and an unreachable pattern), with 9,830 warnings (`p13-detached-scan-final-build.log`). These external files were preserved. Installer compilation using BuildProjectReferences=false and the last successful dependencies passed with zero warnings/errors (`p13-detached-scan-dependency-build.log`); this is not a passing final full-source build. Full-source qualification must be repeated after the sibling errors resolve. `git diff --check` found no whitespace errors.
- The worker does not launch payloads, prerequisite detection commands, downloads or installers. Controlled runtime, actual Windows DPI/accessibility/usability, remaining report/status/field/layout audits and current full-suite qualification remain open. P13 remains in progress.

### Report presentation and live authoring status captions — 2026-10-03

- Validation and capability reports retain detached report data and the captured project name. Language changes rerender headings, area names, severity/status labels and culture-formatted counts without changing engine explanations, paths, codes, hashes or artifact names. A uniquely selected diagnostic code remains selected. Unknown engine tokens remain verbatim; the capability view retains its existing twelve-finding display limit and translates the omitted-count caption.
- Save, validation, template application, script application, undo/redo and completed-build status captions now retain a presentation function and captured arguments, so they refresh live with language changes. A new/replaced project clears the previous status. Source-scan captions and later literal engine messages supersede the previous presentation. Four additional keys ship in all eight languages (32 entries).
- Native status tests exercise actual Save, script application, validation failure/success, build success/failure/cancellation, project replacement and undo/redo. Captured template arguments are checked for caller mutation, and localized status text matches its accessible name. Report checks exercise detached DTO ownership, captured attribution, caching, selected-code preservation and unknown tokens.
- The initial expanded report run exposed native test interference: the modal diagnostic test selected the first process-wide wizard while other UI classes could run alongside it. BuilderWizardFormTests, DiagnosticEditingTests and BuilderSectionCacheTests now share the existing nonparallel Language collection; the modal test additionally identifies its own editor. The failed run was stopped and recorded as aborted (`p13-localized-report-regression.log`), not passing evidence.
- The first status run used a stale installer binary because BuildProjectReferences=false also skips building the application (`p13-live-status-focused.log`). After explicitly rebuilding the installer, all 14 focused checks passed with no skips (`p13-live-status-focused-retry.log`). The explicit dependency-only installer build had zero warnings/errors (`p13-live-status-app-build.log`).
- Full-source installer compilation now passes with zero errors and 10,003 dependency/analyzer warnings (`p13-live-status-source-build.log`); the earlier sibling security/filter errors were resolved externally. Those sibling files were preserved.
- The first normal-source expanded run passed 142 of 143 checks, with one populated COM-editor focus failure (`p13-live-status-source-regression.log`). Its fixture now settles initial activation and binding work before establishing keyboard focus, while retaining the same focus assertion. The final normal-source rerun passed all 144 selected checks with no skips in 6 m 55 s (`p13-live-status-final-regression.log`), including all nine populated resource editors and the added undo/redo status case. Five existing test nullable/analyzer warnings remain. This supersedes the COM focus failure for the tested paths.
- This batch does not close actual OS DPI, screen-reader/accessibility, usability, live runtime or current full-suite gates. Engine diagnostic prose and retained logs are not claimed as fully translated. P13 remains in progress.

### Project and field pickers; Guided Project layout — 2026-10-03

- Authoring project Open/Save pickers now use localized titles and filter descriptions, keeping the `.bsetup`/all-file patterns, authored filename, extension and overwrite checks intact. Shared folder pickers describe the selected field and start at an existing authored directory; an absent directory does not become the picker start location. Three new picker keys ship in all eight languages (24 entries).
- The field picker audit found raw patterns (`*.ico`, `*.png;*.jpg;*.bmp`, `*.rtf;*.txt;*.md`, `*.pfx`) passed directly to OpenFileDialog.Filter. That setter requires description/pattern pairs. The shared factory now supplies valid localized pairs and an all-file option without changing the patterns. Tests confirm the old raw assignments throw ArgumentException and each corrected filter constructs successfully in all eight languages. Actual pickers are not opened by these tests.
- Guided Project's source Browse action now has a stable control name and a descriptive localized accessible name. Focus follows its replacement when language rebuilding occurs. The Project layout uses automatic row sizes, wrapping labels/help and scrolling; source help uses the shared muted theme role and the title uses the shared section-title font. Drafts remain separate from the canonical model during language changes.
- Initial normal-source installer compilation passed (`p13-project-picker-build.log`), with a new nullable filename warning corrected before subsequent normal-source test builds. All 37 picker/Guided/navigation checks passed with no skips (`p13-project-picker-focused.log`, 51 s). These include populated draft/focus/help-size checks at the supported minimum window size in all eight languages; they do not establish actual OS DPI qualification.
- The expanded normal-source run passed 95 of 96 checks, with another intermittent populated COM-grid language-switch focus failure (`p13-project-picker-regression.log`, 8 m 15 s). The prior activation settling change is therefore not claimed as a complete fix. Focus assertions now include active owner/control details for future failures. EverySectionRendersTests shares the serial UI/language collection.
- All nine isolated populated resource-editor checks passed (`p13-resource-focus-diagnostic.log`, 1 m 30 s). All eight picker/filter and repeated COM-window/language-cycle checks passed (`p13-picker-filter-focus-diagnostic.log`, 45 s); the repeated case opens four independent COM editors and retains the same focus, model and listener-lifetime assertions. This selected passing evidence does not explain the intermittent broader-run focus failure.
- A final normal-source expanded run includes all updated picker cases, repeated focus coverage, both shells, cached views, status/workflow captions, diagnostic editing and all forty sections. All 101 selected checks passed with no skips through the normal source graph (`p13-project-picker-final-regression.log`, 4.9483 min), with five existing test nullable/analyzer warnings. The intermittent COM failure is not claimed as diagnosed or fixed by this passing run. Remaining modal/field/font/layout audits, intermittent-focus diagnosis, full-suite and actual Windows qualification stay open. P13 remains in progress.

## 12. Related plans

- [Master tracker](MASTER_TRACKER.md)
- [P12 wizard-first architecture](P12_WIZARD_FIRST_IA_DESIGN.md)
- [F25 professional authoring UX](../professional-enterprise-roadmap/features/F25_AUTHORING_UX.md)
- [F27 localization and accessibility](../professional-enterprise-roadmap/features/F27_I18N_A11Y.md)

The new work primarily improves access to existing capabilities. Existing feature-specific roadmap documents remain authoritative for packaging, enterprise deployment, security and qualification requirements.

### Guided blocking-field focus — implementation and native baseline, 2026-10-03

Audit A05 is now implemented. Four actual-button baseline cases failed (0 passed) because blank
name kept action focus and component errors kept the Source tab selected
(`p13-guided-blocking-focus-baseline.log`). First fixes passed those four cases
(`p13-guided-blocking-focus-focused.log`). Expanded checks then failed three cases (34 passed):
a blank version was defaulted during creation, and a ClickOnce source blocker kept the Package
step selected (`p13-guided-blocking-focus-expanded.log`).

Blank name/version now focuses its existing draft field before project creation/mutation; the
required-version wrapper ships in all eight cultures. Typed errors use existing tab selection and
DiagnosticEditorNavigator for exact row/property focus, retaining the reason and model. An empty
required component collection focuses its Add action; explicit files-only selection still permits
progress. Package checks referring to an earlier Guided section use existing diagnostic navigation
and retain the blocking message there, without an Advanced handoff. Current-tab routing does not
rebuild draft controls. All 39 focused native cases passed through the normal source graph,
no skips, in 20s (`p13-guided-blocking-focus-final.log`), including eight-language Next/jump name
and component checks, new/existing version correction, empty collection/files-only and an earlier
source package blocker. Resource XML inspection confirms one nonempty new key in every culture.
The broader normal-source regression passed all 143 checks, no skips, in 2m57s
(`p13-guided-blocking-focus-source-regression.log`). It includes existing warning navigation,
Guided/Advanced localization, exact diagnostic editing, owned-modal/inactive focus and asynchronous
Review checks. Final models binding passed: app/test copies match the sibling net10.0 Debug output
(`p13-guided-blocking-focus-model-binding.log`). This supersedes the earlier binding mismatch for
this selected batch, not the earlier full suite's scope or remaining release gates.
The earlier full suite predates these changes; its scope is unchanged. Other audit items and the
Windows/manual/runtime release gates remain open. No commit or push was performed.

### Modal draft Apply protection — A06 implementation, 2026-10-03

Six actual Custom Actions, Component Conditions and Component Files modal baseline cases accepted
OK after project replacement or target changes, rather than retaining a rejected draft
(`p13-modal-draft-conflict-native-baseline.log`, six failed). The fixture cancels after assertions;
this is evidence of missing pre-close rejection, not a claim of observed persistent data loss.
An initial test compilation used a string instead of ConditionExpressionMode; corrected before the
native baseline. The first implementation build required DesignerSerializationVisibility.Hidden
on three new WinForms callback properties (`p13-modal-draft-conflict-focused.log`); corrected.

Each real modal caller captures its originating project and serialized edited-target state before
opening. Actions guard the action list; Conditions guard component references/order/IDs plus
conditions/expression; Files guard component membership, files, size and source-directory context.
Unrelated compatible project edits remain applicable. ModalDraftApply commits draft bindings,
checks origin/target and applies once before allowing OK. A conflict leaves DialogResult.None,
retains the staged values and shows an accessible, wrapping, localized warning with guidance to
copy needed values, cancel and reopen. The conflict surface refreshes across eight cultures and
light/dark palettes; its language/theme/size subscriptions detach with the modal. It does not
qualify the other dialog captions or whole-dialog minimum/DPI layout. File sizes are calculated
before changing the canonical collection, so a size-read failure does not leave that replacement
half-applied. Existing validation/warning policy remains in the dialog's OK path.

The six focused rejection checks passed (`p13-modal-draft-conflict-focused-retry.log`, 6s).
Expanded checks passed 13 and failed three incorrect palette assertions: labels directly on Forms
use the shared Shell background, not Panel (`p13-modal-draft-conflict-expanded.log`). Correcting
that expected shared role yielded all 16 passing cases, no skips, in 25s
(`p13-modal-draft-conflict-final-focused.log`). They include matching-ID replacement, target edits,
retained model/history/draft, compatible one-undo Apply/redo with unrelated changes, all-culture
feedback/palette/listener cleanup and an actual completed background source scan while a file
draft remains open. No fixture payload is executed. XML inspection confirms one nonempty conflict
caption in each shipped culture. Expanded normal-source regression passed all 209 checks, no skips,
in 2m12s (`p13-modal-draft-conflict-source-regression.log`). It includes dialog construction/in-place
actions, exact diagnostic editing, history/controller presentation, source-scan transactions and
presentation, modal focus, Guided blocking focus, async Review and UI-language selection.
Whole-solution source build passed with zero errors and 11,592 warnings in 38.10s
(`p13-modal-draft-conflict-whole-solution-build.log`). Final app/test models copies match the
sibling net10.0 Debug output (`p13-modal-draft-conflict-model-binding.log`). The earlier full suite
predates A05/A06 and does not qualify these later changes or the four skipped end-to-end cases.
Large-target snapshot latency, real Windows DPI,
Narrator, manual author tasks and actual runtime journeys remain unqualified. No commit/push.

### Conditions editor presentation and grid binding — A03 in progress, 2026-10-03

Two actual populated-window native baselines failed: the Conditions grid displayed zero rows
because its DataSource was never assigned, and the title stayed English after French UI selection
(`p13-conditions-presentation-native-baseline.log`). Initial test compilation needed DataGridView
Rows.Count assertions instead of collection assertions; fixed before the native baseline.

The grid now binds the existing typed BindingSource, follows the selected condition, edits its
actual values and refreshes rows after removal. CommandReturns now exposes its second operand.
Title, actions, field/accessibility captions, help and grid headers refresh in place through explicit
resource-key bindings; authored field values are never caption targets. Caption refresh retains
component/expression/row state and focus/caret using UiFocusBookmark and shared RTL direction.
ValidationCounts now uses localized count wrappers, accessible status text and semantic shared
colors. Validator issue messages remain verbatim; severity/count policy is unchanged. Its earlier
fixed English/color expectations were updated to verify the shared presentation contract.
Conditions editor-owned syntax refresh does not evaluate a machine condition. An inert owned
command-marker fixture remains absent across all eight cultures. Preview selection is retained
through UpdateText. The body font is shared-theme derived and explicitly owned/disposed; theme,
language and BindingSource lifetimes detach/dispose with the form. Preferred-size selector/field/
status/footer rows, wrapping actions and notes, minimum window and editor scrolling replace fixed
caption/action heights. Full OS DPI/Narrator qualification is separate.

Initial normal-source focused checks passed 21 cases (4s, p13-conditions-presentation-focused.log).
Expanded checks passed all 39 cases, no skips, in 51s
(`p13-conditions-presentation-expanded.log`): populated row selection/edit/remove, all-eight-culture
model/selection/focus/expression preservation without command execution, clean/error/warning
status and light/dark palettes, subscription disposal and enlarged multiline caption/minimum-window
layout, validators/count helper and existing modal conflict protection. Broader normal-source
regression passed all 206 checks, no skips, in 2m49s
(`p13-conditions-presentation-source-regression.log`). It covers actual dialog/in-place actions,
exact diagnostic editing, history presentation, owned-modal focus, language selection, Guided
blocking focus and asynchronous Review alongside Conditions/count/validator cases. Final
app/test models copies match current sibling net10.0 Debug output
(`p13-conditions-presentation-model-binding.log`). Earlier whole-solution/full-suite evidence
predates this Conditions batch and does not qualify its later changes.

A03 remains open: BuildConditionPreview still supplies English UI-owned explanatory prose. Add a
localized UI renderer that retains captured rule values, issue indices/messages and selection,
without running condition evaluation. Qualify that preview, actual owned-modal focus and remaining
keyboard/accessibility/layout scope before closing the audit item. A01/A02/A04 and all larger
Windows/manual/runtime release gates remain open. No commit/push was performed.

A03 follow-up source concern: OnOk validates only CurrentComponent while Apply commits all draft
components. Native qualification must cover a blocker in a different component, selected valid
component, warning policy and exact correction focus before declaring multi-component editing
complete. No reproduced behavior is claimed from this source-only observation.

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

### 2026-10-03 Edit Files redesign rejected and reverted

The user rejected the attempted side-by-side Edit Files layout as worse. Its toolbar, new-entry
flow, curated two-column grid, added resources, and diagnostic-view redesign were reverted.
The preceding horizontal list/editor layout and action styling are restored. Existing staged
metadata scans, async sizes/OK, modal conflict guards and source-relative size fixes remain.
Redesign-specific tests were removed; diagnostic traces were removed from the retained scan tests.
No commit/push or unrelated/sibling source edits were made.

The redesign tests exposed report/layout/RTL failures and do not qualify the retained screen.
The final virtual-report run was deliberately stopped after the user's rejection.
The source dependency build failed on seven missing Newtonsoft references in the concurrently
edited sibling BeepDM Models project (`p13-edit-files-layout-diagnostics.log`). The local app
rollback compiles using existing dependency binaries (`p13-edit-files-layout-rollback-build.log`);
this is not a current full-source graph build. A01 presentation and report stress qualification
remain open. Further visual redesign requires a reviewed design direction before implementation.

### 2026-11-23 Main menu and SVG toolbar — implemented, focused verification

Fahad requested a menu bar for New/Open/Save/Save As and a toolbar reserved for selected functions.
The Image Gen [main-window target](design-evidence/menu-bar/menu-bar-target-2026-11-23.png) and
[icon concepts](design-evidence/menu-bar/toolbar-icon-concepts-2026-11-23.png) were generated and
shown before implementation; prompts, owned renders and comparison are retained in the
[design evidence](design-evidence/menu-bar/README.md).

PackageBuilderForm now owns File/Edit/View/Project/Help menus, native shortcuts, translated
captions and a compact six-icon toolbar. Existing handlers/controller paths still perform the work.
Six actual currentColor SVGs match the icon concepts; BeepIcons loads them through its existing
cache and recolors them for the toolbar's panel. The previous More dropdown and file toolbar commands
are removed; MCP, recovery, release/comparison and language selection remain reachable in menus.
The earlier rejected Edit Files layout remains reverted. This slice does not replace the identity
form or navigation with the mockup's illustrative body.

Normal-source verification: 92 menu-route checks passed (0 skipped, 2m46s), followed by 15 focused
menu/palette checks passed after the final foreground change (0 skipped, 11s). A controlled missing
MainMenuStrip registration made the new guard fail before it was restored. Actual SVG ink, all eight
languages/Arabic RTL, unchanged editor/project, owned-file Save, welcome availability and listener
cleanup are covered. App/test Models match sibling source output. No full suite or cloud builds.
Existing dependency warnings remain; broader P13/native accessibility/DPI/release gates remain open.
No commit, push or publication. This continuation is progress; the full plan is not complete.


2026-11-23 P13 A02 Custom Actions: implemented against the generated target in the existing dialog/validator. Command/timing list, aligned fields, SVG add/remove, one OK/Cancel footer, live eight-culture/RTL captions and semantic themes preserve detached drafts. Blocking errors focus the exact row/field; warnings still require confirmation. Native last-row removal crash, stale timing caption, untouched-null normalization and minimum-size footer regressions were reproduced and corrected. Final normal-source focused regression: 77 passed, zero failed/skipped, 1m32s. App/test Models match sibling source Debug output. Target, native render, red/final logs and honest visual comparison: `design-evidence/custom-actions/README.md`. A02 source slice is implemented; A01/A04, broader OS DPI/accessibility and remaining plan/release gates stay open. No full suite, cloud build, commit, push or publication.


2026-11-23 P13 A04 Language Manager: implemented the generated menu-based target in the existing form. File owns Save all/Ctrl+S and CSV import/export; Language and Keys have distinct Add/Remove commands. UI-language refresh retains an active translation edit/caret, selected editing language, dictionary and preview across all eight cultures/RTL. Native edit-ending baseline failed and was corrected at its owner. Save commits the active cell; platform CSV parsing round-trips quotes/multiline text and rejects malformed imports before mutation. Final normal-source focused regression: 45 passed, zero failed/skipped, 55s, including menu routes, Custom Actions, resource coverage and subscriber cleanup. App/test Models match sibling source output. Target/prompt/native render, source hashes and exact red/final evidence: `design-evidence/language-manager/README.md`. A04 implementation slice is addressed; A01 and broader P13/actual OS DPI/Narrator/release acceptance remain open. No full suite, cloud build, commit, push or publication.


2026-11-23 P13 A01 scan/reporting continuation: source and native regression establish that staged grid drops, asynchronous source-size reads, cancellation/stale/disposal guards and guarded Apply/undo/redo are already implemented, superseding older absent-handler/synchronous-read notes. Three new failure guards failed before correction; size/drop/Apply and obsolete-work catches now report through existing Diag, while accessible localized failure status carries correlation references instead of raw exception detail. Final normal-source selected regression: 41 passed, zero failed/skipped, 1m28s. App/test Models match sibling source Debug output. Exact baseline/final logs and affected source hashes: `design-evidence/file-scan-reporting/README.md`. The rejected Edit Files layout remains reverted. A01 actual Explorer/OLE, representative large-folder latency, actual OS DPI/Narrator, wider audit/notification qualification and remaining P13/runtime/release gates remain open. Next: verify the remaining qualification requirements against current evidence, starting with real owned large-file-list behavior. No full suite, cloud build, commit, push or publication.


2026-11-23 P13 A01 real large-list qualification: 3,000 owned text files across ten directories staged through the actual grid FileDrop event. The normal-source native fixture verifies paths/destinations, 3,000 rows/bytes, unchanged canonical component and one OK callback. Final sample: dispatch 48ms, maximum message-loop gap 310ms, 72 heartbeat ticks. Deliberate empty-result mutation failed the guard. An intermediate restore reused the stale mutation assembly because Copy-Item retained its timestamp; that batch was stopped and is explicitly unqualified. Restored bytes were verified, source recompiled, and final selected regression passed 42, zero failed/skipped, 4m15s. Source/model hashes and exact evidence: `design-evidence/large-file-list/README.md`. The production layout remains unchanged. Actual Explorer/OLE, remote/slow filesystem behavior, OS DPI/Narrator, large Guided Review capture and remaining P13/runtime/release gates remain open. No full suite, cloud build, commit, push or publication.


2026-11-23 UX06 large Guided Review: qualified the real default validation path with 100 components/3,000 file entries. Existing page/table layout is batched without changing content, fields, snapshot owner or validation policy. Matched old-renderer sample: 1,490ms construction / 1,450ms refresh; it fails the explicit one-second fixture guard. The restored batched renderer passed the final 69-case normal-source Review/Guided-presentation/blocking-focus selection, zero failed/skipped, 36s. Final local sample: 25ms construction, 184ms refresh, 196ms maximum message-loop gap. Full detached validation, candidate-only normalization, unchanged canonical state and last-file unnotified edits blocking Build are checked. Early isolated runs included one-time theme setup; these are not pure Review latency. Exact profiles, old/new logs, source/model hashes and limits: `design-evidence/large-guided-review/README.md`. App/test Models match sibling source output. Wider startup/device/OS DPI/Narrator/runtime/release gates remain open. No full suite, cloud build, commit, push or publication.


2026-11-23 UX01 composed startup correction from acceptance audit: default Main previously resolved Advanced directly and bypassed cold-start selection. Existing startup owner now selects Guided for fresh/missing/unreadable recent state or reopens a valid recent project in Advanced using the registered controller. Actual Main/Dispatch/Application.Run baseline failed four cases; final normal-source startup/Guided/menu/composition selection passed 46, zero failed/skipped, 33s. The recent-store owner has a scoped owned-file configuration for host/tests; user recents are isolated. Source/model hashes and exact failed/aborted/final evidence: `design-evidence/cold-start-entry/README.md`. The first corrected test batch stopped at its cleanup Save-first confirmation and is not counted as passing; disposal cleanup excludes that separate user journey. Current UX01–20 acceptance map replaces stale top-level backlog claims. Physical input/visual/Narrator/actual DPI, representative authors and installed runtime/recovery gates remain open. No full suite, commit, push or publication.


2026-11-23 UX01/11 creation baseline and close behavior: the generated shell placeholder is clean; Guided compares fresh fields/template identity with their displayed initial baseline rather than a different canonical placeholder. Language refresh preserves that baseline. Actual field/template edits remain dirty and revert cleanly; Cancel retains text, Discard closes without canonical creation. Already-authored blank projects retain identity on entering Guided. The actual composed-entry fixture now uses ordinary Close without forced MarkClean/Dispose and asserts no untouched-startup confirmation. Restoring old dirtiness/comparison failed six guards; removing authored-project recognition failed its identity guard. Final normal-source startup/creation/navigation/recovery/save/history/presentation selection: 63 passed, zero failed/skipped, 1m08s. Exact logs, source/model hashes and limits: `design-evidence/creation-draft-baseline/README.md`. Native dialog decisions are injected for edited drafts; physical input, Yes/save-picker, OS DPI/Narrator, author tasks and installed runtime/recovery remain open. No full suite, cloud build, commit, push or publication.


2026-11-23 controlled Sandbox acceptance: user selected Windows Sandbox on this PC. Built an unsigned inert text-only fixture and standalone authoring host locally; actual guest final existing-matrix run passed five cases with zero failures and four explicit skips: install/hash, repair/restored hash, uninstall/removed payload, corrupt-payload refusal and blocked-destination refusal. Child exits/logs and artifact hashes are retained. Guided authoring startup was observed in the clean guest. This is not partial-apply rollback, persisted recovery, per-machine/non-admin, upgrade/restart/prerequisite or human accessibility acceptance. Accessibility qualifier obsolete RTL literal was corrected against its current owner; old matcher failed its guard and final focused selection passed eight, zero skips. Real required-manual CLI gate remains exit 1 with six readiness checks passing and six missing manual artifacts. Recovery metadata contract passed at the verified physical path; filesystem-link protection retained. Exact prior failures, redundant diagnostic repeat, final evidence, operator checks and source hashes: [acceptance package](design-evidence/acceptance-package/README.md). Complete P13 remains in progress. No full suite, cloud build, host install, commit, push or publication.


2026-11-23 runtime checkpoint continuation: existing upgrade qualifier executed in the user-selected Sandbox, ten scenarios passed. Process interruption exposed a missing canonical journal during File.Replace; AtomicFileWriter now publishes a flushed same-directory overwrite rename, preserves Windows access DACLs and retains link guards/cleanup reporting. Final normal-source ResourceProviderRuntime/JournalPersistence/RecoveryQualifier selection: 56 passed, zero failed/skipped, 5s. Final separate-process rollback/resume sample passed five cases: partial journal inspection, rollback, resumed 300 matching payload hashes/four Resume entries and uninstall. However a preceding real sample had five copied files versus four durable Apply entries: rollback exited 0 and left one file. This confirmed provider.Apply-before-checkpoint gap remains unfixed and blocks recovery acceptance; the later green timing sample does not close it. The existing matrix runner now records singleton failures correctly and runs independent recovery cases without masking failure. Exact failed/green attempts, native journals/logs, tested runtime/source hashes, API rationale and next owner-level mutation-state work: [acceptance package](design-evidence/acceptance-package/README.md). No full suite, cloud build, host setup/runtime CLI install, commit, push or publication. Complete P13 remains in progress.


2026-11-23 prepared file mutation correction: the ordinary fresh/replacement applied-before-checkpoint defect is addressed through the existing file provider, executor, journal and recovery owners. Durable PrepareApply before-state precedes mutation; interrupted replay restores bytes/modification time/access DACL, and committed uninstall removes owned payload. Private backup creation/inheritance, backup path/hash validation, attempt identity, older Verify invalidation and rollback JSON action have focused guards. Resource journal format 2.0 refuses version 1 without rewriting it; SDK contract documented. Final normal-source selection: 126 passed, zero failed/skipped, 3s. Six negative-control selections failed as intended and were restored before final tests. Self-contained current runtime then passed both controlled Sandbox profiles, five cases each with zero failures/skips: fresh and replacement interruption/rollback/resume/uninstall; replacement rollback verifies all 300 original hashes/times, both resumes verify 300 new hashes, unrelated note retained, JSON identifies rollback. Exact source/runtime hashes, native environment, prepared/completed journals, failed history and repeat configuration: [acceptance package](design-evidence/acceptance-package/README.md). Native kills are timing samples; deterministic persisted-journal fault tests cover the precise missing-completion boundary. Other providers, reboot/power-loss/filesystems, version-pair upgrade, non-admin/per-machine, human DPI/Narrator/author tasks and signed/release gates remain open. No full suite, cloud build, host setup/runtime CLI install, commit, push or publication. Whole P13 remains in progress.
