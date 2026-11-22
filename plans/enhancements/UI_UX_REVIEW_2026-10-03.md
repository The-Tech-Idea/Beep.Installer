# UI/UX enhancement review and feature list

**Reviewed:** 2026-10-03. **Scope:** installer authoring and generated installer experience.

## Repository check

`origin` points to `https://github.com/The-Tech-Idea/Beep.Installer`. Fetch succeeded and `git pull --ff-only origin master` reported **Already up to date**. Local `master` and `origin/master` both point to `6c2f148`; ahead/behind is 0/0. Existing modified and untracked files were preserved. No commit or push was performed.

The working tree already contains substantial P13 implementation. Use the [detailed P13 plan](P13_UI_UX_FEATURE_ENHANCEMENT_PLAN.md) for acceptance criteria and implementation history. Its original gap table describes the pulled baseline; several of those gaps have since been addressed locally. This review sets the remaining priorities without treating uncommitted code or historical test results as a completed release.

The [remaining presentation audit](P13_PRESENTATION_AUDIT_2026-10-03.md) identifies six concrete
findings: Component Files captions/layout, Custom Actions wrappers/layout, Conditions status
presentation, distinct translation-editor actions/live statuses, Guided first-blocking-field
focus, and modal Apply protection after project/conflicting collection changes. Guided blocking
focus and modal Apply protection now have implementations and native evidence; four dialog
presentation fixes remain.

## Intended experience

A first-time author should complete a simple package through Project → Files and components → Install behavior → Package and destination → Signing → Review and build → Result. Advanced authors should reach every resource editor through search and grouped navigation, with edits preserved when switching modes.

Each screen should explain its purpose, show one clear primary action, provide an actionable empty state, and distinguish blocking errors from warnings. Review should describe what changes on the target machine and take the author directly to the offending field. Results should expose the actual artifact, its location, and useful follow-up actions.

## Remaining delivery plan

| Order | Work | Deliverable | Acceptance gate |
|---|---|---|---|
| 1 | Finish dynamic text and editor consistency | Audit status messages, report presentation, common fields, help and empty states | Open views refresh across all eight languages; authored values and diagnostic codes remain intact |
| 2 | Complete layout and accessibility | Shared spacing/fonts, usable minimum sizes, focus order and accessible names | Populated Guided/Advanced editors work at real Windows 100/125/150/200% DPI; long translations and Arabic RTL remain usable; keyboard and screen-reader evidence recorded |
| 3 | Qualify editing and asynchronous operations | Verify shared views, history, source scans, save/recovery and build snapshots together | Switching modes or projects cannot lose edits, apply stale results, leak subscriptions, or misattribute a build/report |
| 4 | Restore full integration qualification | Build the complete source dependency graph and run a fresh full suite | Sibling dependency errors resolved; current full-source build and required repository checks pass |
| 5 | Validate real installer journeys | Controlled install, repair, uninstall, upgrade, prerequisites and recovery runs | Actual behavior matches review summaries; restart requirements and failed recovery are reported accurately |
| 6 | Validate usability and finish polish | Representative author sessions, template discovery and preview refinements | At least four of five first-time authors complete a simple EXE project unaided and locate its artifact; each identifies a blocker and its fix |

Orders 1–3 can progress while dependency integration is being repaired. New polish should follow core workflow qualification. Record baseline task times before setting speed targets; no delivery-time estimate is asserted here.

Current verification follow-up: fetch/pull again confirmed `master` is up to date (0/0).
The fresh normal-source solution build passed with zero errors and 20,152 warnings in 59.94s.
The completed full suite passed 2,045 tests, with four explicitly skipped end-to-end tests.
The post-build dependency inventory still differs from later sibling outputs, so current source
binding remains open. Logs: `p13-post-full-current-source-build.log` and
`p13-post-full-current-model-binding.log`. Local changes were preserved; no commit or push was made.

## Prioritized feature list

These priorities retain the detailed P13 IDs. Local implementation exists across these areas; every item remains subject to its acceptance criteria and cross-stage release gates.

| ID | Priority | Feature | Intended user benefit |
|---|---|---|---|
| UX01 | P0 | Unified project creation | Cold start, New and Ctrl+N follow one predictable path |
| UX02 | P0 | Reliable guided navigation | Required steps cannot be bypassed; revisiting preserves the project |
| UX03 | P0 | Severity-aware validation | Warnings are visible without accidentally blocking progress |
| UX04 | P1 | Optional steps and files-only projects | Authors can skip irrelevant configuration explicitly |
| UX05 | P1 | Guided package choice | EXE, MSI, MSIX and ClickOnce choices explain compatibility before building |
| UX06 | P1 | Review with exact edit links | Authors understand system changes and correct the right field quickly |
| UX07 | P1 | Consistent editors | Fields, resource lists, help, actions and empty states behave consistently |
| UX08 | P1 | Localization and accessibility | Both modes remain usable across languages, RTL, keyboard and DPI |
| UX09 | P1 | Persistent build workspace | Stage progress, cancellation, logs and results stay together |
| UX10 | P1 | Resource recipes | Service, IIS, task and firewall configuration starts from guided typed inputs |
| UX11 | P1 | Save and recovery status | Saved files, unsaved edits and recovered drafts are clearly distinguished |
| UX12 | P2 | Undo/redo transactions | Editing and template application can be reversed coherently |
| UX13 | P2 | Release overview | Package, signing, deployment assets and qualification evidence are visible together |
| UX14 | P2 | Update preview | Authors understand channel/version transitions and rollback constraints |
| UX15 | P2 | Project comparison | Version, resource, destination and package changes are easy to inspect |
| UX16 | P2 | Contextual search and help | Authors find the correct editor and local guidance quickly |
| UX17 | P2 | Runtime operation summaries | End users see scope, prerequisites, restart needs and recovery outcomes |
| UX18 | P2 | Runtime support actions | Redacted error details and support bundles make failures diagnosable |
| UX19 | P3 | Template gallery and favorites | Templates are searchable and reusable with trust verification preserved |
| UX20 | P3 | Installer preview controls | Language, page and layout previews are available without installation |

## Design and architecture constraints

- Keep `InstallProject` and `.bsetup` canonical; reuse existing validators, exporters, signing and update services.
- Share section factories between Guided and Advanced with explicit view ownership and disposal.
- Use typed workflow state and diagnostic severity rather than control text to decide navigation.
- Preserve focus, row selection, drafts and authored values during language/theme changes.
- Use semantic theme colors and preferred-size layouts; include visible text with status colors/icons.
- Enable artifact actions only for the relevant successful, available output.
- Capture independent data for long-running operations and discard outdated completions.
- Explain runtime cancellation limits until engine cancellation and rollback are actually qualified.

## Evidence and limits

This review performed a fresh remote fetch/pull and source/document inspection. It did not run a new build, test suite, live UI walkthrough or installation.

Existing logs inspected during review show six report/workflow presentation checks passing (`p13-localized-report-focused.log`) and an installer build against existing dependency binaries passing with zero warnings/errors (`p13-localized-report-dependency-build.log`). That build used `BuildProjectReferences=false`. The corresponding full-source build failed on five errors in sibling BeepDM security/filter sources (`p13-localized-report-build.log`); those sources were preserved.

Real OS DPI, Narrator/Accessibility Insights, representative usability sessions, live installer journeys and fresh full-suite qualification remain open. Preview scaling and selected automated form checks provide useful evidence but do not close those gates.

Subsequent implementation verification: report wrappers and authoring status captions now refresh
live across all eight languages while retaining captured data and engine text. Full-source installer
compilation passed with zero errors and 10,003 dependency/analyzer warnings
(`p13-live-status-source-build.log`); the earlier sibling compile failures were resolved externally.
The final normal-source regression passed all 144 selected checks with no skips
(`p13-live-status-final-regression.log`). See P13 for the corrected test-isolation/activation issues,
exact scope and remaining gates. This selected result does not qualify the complete current suite.

Qualification follow-up (shared fields and focus): larger multiline field captions reproduced
clipping; preferred-size rows, compact minimum heights and license-pane scrolling now address
that case. Native tests cover preserved authored values, selection and inactive-window reentry.
The later 111-case regression failed overall (93 passed, 18 failed), beginning with two STA
worker timeouts and subsequent overlapping process-wide language changes. Two affected helpers
now stop the dedicated test host on timeout and allow three minutes for a complete language
matrix. This is not a UI latency target. The first normal-source rerun stopped at five sibling
BeepDM FormsManager.Lifecycle compile errors before tests executed. Explicit installer compilation
against existing dependency binaries passed with zero warnings/errors. Broad regression, current
full source graph, modal-origin focus and the real Windows gates remain open. See P13 for logs
and the distinction between retained focus target, window activation and test expectations.

Fresh-process follow-up: all five focused cases passed (2.3664 minutes), then all 111 selected
shared-field/focus/localization/status/picker/theme/navigation/diagnostic/section-rendering checks
passed with no skips (4.8173 minutes, p13-shared-field-sta-dependency-regression.log). The broad
run used --no-build after current installer/test compilation with BuildProjectReferences=false.
It qualifies these selected cases against existing dependency binaries; it does not qualify the
complete source graph or full suite. Timeout termination, modal-origin focus and real Windows
gates remain open. Earlier failed runs remain documented. No commit or push was performed.

Modal-refresh follow-up: actual owned-modal language changes now retain Guided draft-field
selection, keep modal keyboard focus and keep the parent disabled across RTL handle recreation.
The current owner handle is restored on modal close; nested modals and explicit owner disable
are checked. Ten focused tests passed, then all 118 selected checks passed through the normal
source graph in 7.4265 minutes (p13-modal-language-source-regression.log). The normal-source build
passed with zero errors and 1,468 dependency/analyzer warnings; prior sibling FormsManager errors
were resolved externally. These supersede the previous dependency-only integration limit for this
selected batch, not the full-suite or real Windows qualification gates. See P13 for failed baseline
and intermediate runs, the exact test scope and remaining work.

Current integration result: the full source suite failed with 2,009 passed, 10 failed and four
explicit end-to-end skips in 31.9007 minutes. Resource-key and support-read lifetime repairs then
passed all 124 selected checks through the normal source graph in 5.5534 minutes. Whole-solution
build passed with zero errors and 11,504 existing warnings. A later sibling rebuild changed the
models assemblies, so the current source-binding check fails and needs a fresh build/check.
The passing selected run does not close the failed full-suite gate. Authoring UI-language selection
is being qualified; detached asynchronous Guided Review remains the next confirmed implementation
gap. P13 contains the exact evidence and open Windows/accessibility/usability/runtime gates.

Authoring-language follow-up: both modes now offer UI-language selection separately from
translation editing, with all eight native language names and explicit Apply. Project languages
and drafts are retained. All 103 expanded language/modal/Guided/Advanced/help checks passed in
2m50s against existing dependencies. Final app/test models hashes now match a current source
output. Normal source attempts still failed before tests at sibling missing-method references,
so current full-source qualification remains open. Asynchronous Guided Review is next; the failed
full suite and real Windows gates remain open. No commit or push was performed.

Guided Review follow-up: detached schema/plan/package checks now run in the background, with
pending/failure/stale states and a final project-identity/full-state guard before Build. Check Again
captures the current project. Summary and worker data are independent; validation normalization
does not alter authored values. Culture changes retain captured diagnostics and the pending check
while refreshing captions and edit-target focus. The first 202 expanded source checks passed;
27 retained-culture/async/modal/inactive-focus checks then passed after reproducing and fixing
summary and diagnostic-link focus loss. Final normal-source regression passed all 203 checks,
no skips, in 5m19s. Whole-solution build passed with zero errors and seven existing warnings;
app/test models copies match a current sibling Debug output. P13 records
baseline failures and actual scope. The current full suite, large-project responsiveness and real
Windows accessibility/usability/runtime qualification remain open.

The fresh unfiltered normal-source suite passed after these repairs: 2,045 passed, zero failed,
four explicitly skipped end-to-end cases, 2,049 total in 31m24s
(p13-async-review-current-full-suite.log). Post-run app/test models copies match each other but no
current sibling Debug output matches, following source-output rebuilds during the run. A fresh
build/binding check is required. The six presentation-audit fixes, skipped cases, large-project
responsiveness and real Windows accessibility/usability/runtime gates remain open.

Guided blocking-focus follow-up: A05 now focuses required name/version, exact typed collection
rows/properties and earlier-step package blockers while preserving the reason and authored data.
An empty required component list focuses Add; explicit files-only and warning navigation remain
available. Four baseline cases failed; the expanded run exposed three more cases (34 passed),
which were corrected. Final focused checks passed 39 cases across all eight UI cultures and
new/existing draft paths; the expanded normal-source regression passed all 143 cases, no skips,
in 2m57s (`p13-guided-blocking-focus-source-regression.log`). Current models binding passed
(`p13-guided-blocking-focus-model-binding.log`), superseding the earlier mismatch for this batch.
The full suite predates these changes. Five audit fixes and Windows/manual/runtime qualification
remain open. No commit or push was performed.

Modal Apply follow-up: A06 now protects originating project and edited-target state in actual
Custom Actions, Conditions and Files entry points. A conflicting Apply retains staged values and
shows localized accessible feedback; compatible Apply/undo preserves unrelated edits. Native
baselines failed six cases; final focused qualification passed 16, including completed source scan,
all-eight-culture feedback, palette and subscription lifetime. Normal-source expanded regression
passed all 209 checks, no skips, in 2m12s (`p13-modal-draft-conflict-source-regression.log`). Whole
solution build passed with 0 errors and 11,592 warnings in 38.10s; current app/test models match
sibling net10.0 Debug output. The full suite predates A05/A06. Four dialog presentation audit items
and real Windows/manual/runtime release gates remain open. No commit or push was performed.

Conditions follow-up (A03 still in progress): native baselines reproduced an unbound populated
grid and a stale title after French selection. The grid now binds actual selected rows, edits
values and refreshes after removal. Title/actions/fields/help/grid headers refresh live; shared
semantic status colors and localized counts, preferred/wrapping rows/actions, retained focus/
selection/drafts and owned font/binding/subscription lifetime have native checks. A command-marker
fixture remains absent across all eight cultures. Expanded 39 checks passed in 51s; broader
normal-source regression passed 206 checks, no skips, in 2m49s
(`p13-conditions-presentation-source-regression.log`). Final models binding passed. Preview UI
prose remains English and must be localized without evaluation; actual owned-modal and remaining
keyboard/accessibility/OS DPI qualification also keep A03 open. A01/A02/A04 and broader release
gates remain open. No commit/push.

2026-10-03 A03 save validation progress: reproduced invalid-draft acceptance when another
component contains an error, then fixed whole-component syntax validation and exact component/
rule/value focus with localized inline accessible feedback. Rejected drafts remain intact;
correction permits warning-only apply, cancellation does not apply. All 56 normal-source focused
checks passed (0 skipped, 53s), including 16 actual modal cases across eight cultures; app/test
models match sibling source output. Evidence: `p13-conditions-all-components-modal-retry.log`
and `p13-conditions-all-components-model-binding.log`. The initial expanded fixture used the
wrong managed owner Enabled property; it was corrected to native IsWindowEnabled. A03 preview
localization, A01/A02/A04 and full release qualification remain open. Details are in P13/audit.

2026-10-03 A03 preview/correction progress: the actual Conditions preview now uses 16 resources
in each of eight cultures while preserving authored operands, literal format braces, validator
messages and indices. Native baselines reproduced English preview, selected-command offset loss,
and disabled removal of a null blocking rule. All three are corrected; the public English preview
API remains unchanged. Final normal-source focused regression: 114 passed, 0 skipped, 3m34s
(`p13-conditions-preview-final-focused.log`); app/test Models binding matches sibling source output
(`p13-conditions-preview-model-binding.log`). A03 wider keyboard/accessibility/actual-DPI qualification
and all remaining release gates stay open. A01/A02/A04 still need implementation; A01 is next.

2026-10-03 A01 Component Files partial: native baselines reproduced static English title and
clipped multiline caption. Live keyed fields/title/grid headers/size summary, accessible names,
shared font/theme/disposal, flexible scrolling/wrapping layout and localized controlled picker
are implemented. Normal-source expanded checks passed 138 (0 skipped, 2m44s). A later 139-case
run passed 138 with one fixture failure (WinForms clears disposed label text); corrected fixture
and all four native file cases passed (0 skipped, 12s). App/test Models match source output.
Evidence: `p13-component-files-presentation-final-fixture-retry.log` and
`p13-component-files-presentation-model-binding.log`; details in P13/audit. Actual staged file/
folder drop, cancellation/duplicate/stale guards and asynchronous size/path-edit scanning remain
next A01 work. A02/A04 and broader release/manual/OS gates remain open; no commit or push.
