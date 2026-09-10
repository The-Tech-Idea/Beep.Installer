# Phase 12: Wizard-First IA + Shared Editing ViewModel — Design Document

**Status:** ✅ 12.A–12.D.4 shipped · 12.E opportunistic, ongoing · **Priority:** P1
**Depends on:** P6 (theme tokens, dead-UI cleanup)
**Tracker:** [MASTER_TRACKER.md](MASTER_TRACKER.md)

## 0. Problem statement

Two things the product owner reported directly (2026-09-10), the second one explicitly asking for a
rewrite, not another patch on top of the current shape:

1. **"Use ViewModel patterns to avoid duplicating work."** The builder hand-rolls the same
   `BindingSource` + Add/Duplicate/Remove + validation wiring in roughly twenty places instead of
   once. `BindingSource.Current` throwing `IndexOutOfRangeException` on an empty list — instead of
   returning null — was found and fixed in one place (`PackageBuilderForm.BuildAdvancedResourceSection<T>`,
   commit `33bf94c`) and was still live, unfixed, in three more (`ComponentConditionsDialog`,
   `CustomActionsDialog`, `ComponentFilesDialog`) until `263753d` fixed all three the same way. That
   is the shape of the whole problem: one class of bug, fixed by hand, four times, three of them
   wrong. There is no shared layer that a fifth, sixth, twentieth list editor is built on top of —
   every dialog and every builder section starts from a blank `BindingSource` and re-derives the same
   behavior.
2. **"System should be more user-friendly and wizard-by-default and primary, and not scattered
   forms — separate forms are always for advanced."** The builder's authoring surface is
   `PackageBuilderForm` (3,231 lines): a `LeftNavPanel` sidebar with 8 groups / ~40 sections navigated
   freely, no order, no "what do I need to fill in first," plus 17 independent popup forms reached
   from toolbar or row buttons (`CodeSigningWizard`, `PackagingWizard`, `PublishWizard`,
   `ResourceWizard`, `TemplateUpdateDialog`, `LanguageManagerForm`, `UpdateCenterForm`,
   `CustomActionsDialog`, `ComponentConditionsDialog`, `ComponentFilesDialog`, `QuickStartWizard`,
   `ProjectNewDialog`, `BuildProgressForm`, `BuildResultForm`, `BuildErrorForm`, `WizardPreviewForm`,
   `InputBox` — file sizes and roles in §1.2). None of this is arranged as a guided path; it is
   arranged as a control panel. `BeepModernInstallerForm` — the *runtime*, end-user install wizard,
   a completely different system that ships inside the built installer — already solved this problem
   for its own audience: a real stepper, generated from the page list, gated forward navigation via
   each page's own `Validate()` (P6.A.1). The builder never got the same treatment.

A same-day first attempt at 2 patched this without rearchitecting: a cold-start change
(`Program.CreateColdStartController`) that ran the existing `QuickStartWizard` before
`PackageBuilderForm` appeared, and a dismissible banner glued above the section content area
listing six "golden path" steps. Both worked and were fully tested, but they are patches on the
existing scattered shape, not a rewrite of it — a banner bolted onto a control panel is still a
control panel. Built, verified green, and then **discarded** at the product owner's explicit
instruction ("i dont want patching") in favor of the plan below. `Ui/BindingSourceRow<T>` and the
section-menu fix were kept — they are correctness fixes and genuine shared infrastructure, not
patches on the IA.

## 1. Current-state inventory (2026-09-10, `wc -l`, this is the rewrite's starting material)

### 1.1 The authoring shell

| File | Lines | Role |
|------|------:|------|
| `Forms/PackageBuilderForm.cs` | 3,231 | The whole authoring surface: toolbar, `LeftNavPanel`, ~40 `Build*Section()` panel factories dispatched from one `switch` in `OnSectionSelected`, inline validation, build/publish orchestration |
| `Ui/LeftNavPanel.cs` | 249 | The section sidebar (8 collapsible groups, ~40 leaf items) |
| `Ui/InstallerTheme.cs` | 86 | Shared color/theme tokens (P6.B) |
| `Ui/BindingSourceRow.cs` | 47 | Safe "current row" + button-enablement helper (12.A.1) — the one piece of shared editing infrastructure that exists today |
| `Engine/InstallerController.cs` | 344 | Wraps `InstallProject`: open/save/new/dirty-tracking/autosave. The closest thing to a ViewModel today, but it is one controller for the *whole project*, not one per section |

### 1.2 The 17 scattered popup forms

| File | Lines | Launched from | Keep as "Advanced," or absorb into the wizard? |
|------|------:|----------------|------------------------------------------------|
| `ResourceWizard.cs` | 523 | Typed-resources section row | Advanced (already its own multi-step wizard for one row) |
| `QuickStartWizard.cs` | 472 | Toolbar "Quick Start," `File > New` | **Absorb** — becomes the new wizard shell's first steps (§3.2) |
| `CodeSigningWizard.cs` | 429 | Toolbar "Signing" | Advanced |
| `LanguageManagerForm.cs` | 387 | Toolbar "Languages" | Advanced |
| `ComponentConditionsDialog.cs` | 385 | Component row "Conditions" | Advanced (row-scoped editor) |
| `PackagingWizard.cs` | 348 | Toolbar "Packaging" | Advanced |
| `BuildResultForm.cs` | 273 | After a build | Neither — a result view, unaffected |
| `CustomActionsDialog.cs` | 260 | Toolbar "Actions" | Advanced |
| `PublishWizard.cs` | 249 | Toolbar "Publish" | Advanced |
| `UpdateCenterForm.cs` | 246 | Toolbar "Updates" | Advanced |
| `BuildErrorForm.cs` | 201 | Build failure | Neither — an error view, unaffected |
| `ComponentFilesDialog.cs` | 178 | Component row "Files" | Advanced (row-scoped editor) |
| `TemplateUpdateDialog.cs` | 142 | Toolbar "Templates" | Advanced |
| `ProjectNewDialog.cs` | 110 | `File > New`, cold start | **Absorb** — becomes the wizard shell's step 0 |
| `BuildProgressForm.cs` | 104 | During a build | Neither — a progress view, unaffected |
| `WizardPreviewForm.cs` | 66 | Toolbar "Preview" | Neither — literally previews the *runtime* wizard, unaffected |
| `InputBox.cs` | 38 | Various prompts | Neither — a primitive, unaffected |

Ten of the seventeen stay exactly as they are: single-purpose editors for one row or one advanced
concern, already reached by one explicit click. That is the correct shape for "advanced" and this
plan does not touch them. Two (`QuickStartWizard`, `ProjectNewDialog`) get absorbed because they are
themselves already wizard-shaped on-ramps that a real wizard shell should own directly instead of
chaining three separate windows together (`ProjectNewDialog` → `QuickStartWizard` →
`PackageBuilderForm`, which is what the discarded cold-start patch did).

### 1.3 The runtime wizard (reference architecture, not in scope to change)

| File | Lines |
|------|------:|
| `Forms/BeepModernInstallerForm.cs` | 1,033 |
| `Pages/*.cs` (12 files: Welcome, License, Folder, ComponentSelection, Prerequisite, AdditionalTasks, StartMenu, Ready, Complete, Error, Custom, `IInstallerPage`) | 1,694 |

This is the shell that ships *inside a built installer* and runs on the end user's machine — a
different audience, a different binary path (`IsRuntimeMode()` in `ProgramVerbs.cs`), already a
proper stepper with gated `Validate()` per page (P6.A.1). It is the precedent the builder's new
wizard shell should follow structurally (§3.1), not a thing this phase modifies.

### 1.4 Test surface that must keep passing, unmodified in behavior

~1,480 tests total. The load-bearing seam: `PackageBuilderForm.SectionIds` / `OpenSection(id)` /
`ActiveSection` (internal, used by `EverySectionRendersTests`, `EveryDialogRendersTests`), which
routes through `_nav.SelectSection(id)`. Anything this phase does to `PackageBuilderForm` must keep
that seam working exactly as today, because ~90 test cases across those two files exercise every
section and every dialog through it. `LeftNavGroupingTests`, `DpiLayoutTests`,
`RtlAndAccessibilityAuditTests`, `WizardPageSelectionTests` cover adjacent surfaces and are
unaffected by anything below (they do not touch the wizard shell being added).

## 2. Goals

1. **One shared editing layer.** No section or dialog hand-rolls `BindingSource` wiring,
   add/duplicate/remove, or validation again. A new list editor is built on
   `Ui.BindingSourceRow<T>` (row access) and `Ui.ValidationCounts` (error/warning summary), not a
   fifth or sixth hand-rolled copy (§3.3).
2. **A real wizard is the default, primary experience**, not a banner or a pre-dialog in front of
   the old shape. Cold start and `File > New` open a stepper shell with gated forward navigation
   (mirroring `BeepModernInstallerForm`'s existing, proven pattern), walking the sections a build
   actually needs, in order.
3. **The current 40-section browser + 17 popups stay reachable, unremoved, as "Advanced."** One
   explicit action opens them. Nothing that exists today is deleted; it changes what is *default*.
4. **No regression.** Every existing test keeps passing without its own behavior changing;
   `EverySectionRendersTests`/`EveryDialogRendersTests` continue to exercise Advanced mode exactly as
   they do today, since Advanced mode *is* today's `PackageBuilderForm`, untouched.

## 3. Target architecture

### 3.1 The wizard shell — reuse the pattern, not the class (shipped, 12.D.1)

`BeepModernInstallerForm` already proves the pattern this needs: a stepper generated from a page
list, `Next` blocked until the current page's `Validate()` passes, `Back` always free.
`Forms/BuilderWizardForm.cs` is a *sibling* of that pattern for the authoring side, not a shared
base class with it — the runtime wizard installs an app; this one edits a `.bsetup` project, and
their page contracts differ enough (runtime pages read `SetupContext`; builder steps read
`InstallProject` directly) that forcing a shared type would be the kind of premature abstraction
that costs more than it saves. What is shared is the shape: an ordered step list, a stepper strip,
`Next`/`Back`, and per-step validation gating.

Each step 1+ hosts **the exact same `Panel` `PackageBuilderForm`'s own `Build*Section()` methods
already produce** — no section's own UI was rewritten. Concretely: `BuilderWizardForm` constructs a
`PackageBuilderForm` instance it owns but never shows (`_advanced`); each step calls
`_advanced.OpenSection(id)` (the same internal seam `EverySectionRendersTests` already uses) and
re-parents `_advanced.ActiveSection` out of `_advanced`'s own content host into the wizard's step
host. Re-parenting a `Control` is idempotent — adding it to a new parent silently detaches it from
whichever one it was in — so "Open full editor" simply re-opens the current step's id on `_advanced`
(pulling its panel back) and shows that same instance for real. `Next`/step-strip forward movement
is gated by a new `PackageBuilderForm.ActiveSectionHasErrors()`, reading the same `_fieldErrors`
`ValidateFieldsInline()` already populates on every section switch — no second validation path.

**Real bug found and fixed while building this (`7b47bba`):** `InvalidateAllContent()`'s reflection
filter (every private `Panel` field named `_content*`) also matched `_contentHost` itself, the live
container the cached section panels dock into. `OnProjectReloaded` disposed and nulled it along
with the real cache on every project reload, then crashed the very next line
(`HideWelcome`'s `_contentHost.Controls.Clear()`). Reachable in the shipped app already — `File >
Open` a second project into an already-open builder hits the same path — not something this phase
introduced, just the first thing to exercise it. Fixed by excluding `_contentHost` by exact name.

### 3.2 Step order (the "golden path", shipped 12.D.1/12.D.2)

The wizard shell's default steps, in order, replacing the three-window
`ProjectNewDialog → QuickStartWizard → PackageBuilderForm` chain the discarded patch used:

| # | Step | Backing section id |
|---|------|---------------------|
| 0 | New project | absorbed inline (template, product name, version, publisher, source dir — mirrors `ProjectNewDialog`'s fields; not a separate window) |
| 1 | Identity | `identity` |
| 2 | Source | `source` |
| 3 | Components | `components` |
| 4 | Shortcuts | `shortcuts` |
| 5 | Registry | `registry` |
| 6 | Output | `output` |
| 7 | Code signing | `codesign` |
| 8 | Build | `build` — terminal step, no Next; hosts the existing Build button/pipeline |

Every step's Next is gated uniformly by `ActiveSectionHasErrors()` (§3.1) — shipped simpler than the
original "Required to reach Build?" column above sketched (per-step required-vs-skippable rules,
e.g. forcing at least one component). Uniform gating was the lower-risk first cut; differentiating
which steps are truly required is deferred, not designed away — see §5, 12.D.4 (new). `includes`,
`compression`, and `package` are not separate or bundled steps; they stay reachable only from
Advanced mode, same as everything below.

Every other section (Prerequisites, Includes, Compression, Package Format, Branding, WizardPages,
CustomPages, the ten "Advanced Resources" sections, MSIX\*, UpdateChannels, PrerequisiteCatalogs,
Supersedence, Packages, Script, Log, Result) stays exactly where it is today: inside Advanced mode's
`LeftNavPanel`, unchanged, unremoved. This is a curated subset by design — the wizard is a *path
through the common case*, not a re-hosting of all 40 sections; forcing everything into a linear
order would recreate the "40 rows" problem P6 already fixed once, in stepper form this time.

### 3.3 The shared editing layer (shipped 12.C.1, revised from the original sketch)

The original design for this section sketched a single `SectionViewModel<T>` combining row access,
CRUD, and validation. Building 12.C.1 against the real code (not the sketch) found the actual
duplication was narrower and a different shape, so what shipped is two small, independent pieces
instead of one speculative one — consistent with the project's own convention
(`Ui/BindingSourceRow.cs`) of "wrap the one unsafe operation, wire the one repeated behavior," not
"replace WinForms binding" with a framework:

- **`Ui.BindingSourceRow<T>`** (12.A.1, already shipped before this phase) — safe `Current`/`HasRow`
  + `WireRowButtons`, used at all four `BindingSource`-backed list-editing sites
  (`BuildAdvancedResourceSection<T>`'s 17 call sites, `ComponentConditionsDialog`,
  `CustomActionsDialog`, `ComponentFilesDialog`).
- **`Ui.ValidationCounts`** (12.C.1) — the *other* real duplicate, found by reading each of those
  four sites rather than assumed: `ComponentConditionsDialog` and `CustomActionsDialog` each
  hand-rolled "count errors, treat the rest as warnings, set a label's text and color" against two
  differently-shaped issue types (`ConditionListValidator.Issue`'s `Severity` enum vs
  `CustomActionIssue`'s bool `IsError`). `ValidationCounts.From(issues, isError)` takes a predicate
  instead of assuming a shared issue interface. `BuildAdvancedResourceSection<T>` and
  `ComponentFilesDialog` have no equivalent validation-summary label — confirmed by reading each,
  not assumed — so nothing to extract there.

Per-step "is this step allowed to advance" (§3.1) is **not** provided by either of these — it reuses
`PackageBuilderForm.ValidateFieldsInline()`'s existing per-field `ErrorProvider` state via
`ActiveSectionHasErrors()`, since that already runs the same `ProjectSchemaService.Validate(project)`
the whole builder uses. A `SectionViewModel<T>.Validate()` scoped to list-editing sections
specifically remains a reasonable idea for 12.C.2/12.E if a concrete duplicate need shows up there
too, but is not invented ahead of one.

### 3.4 Mode switch: Guided (default) vs Advanced

- **Guided** = `BuilderWizardForm`. Shipped (12.D.2): cold start with nothing to resume opens it
  directly, as the primary window — not a pre-dialog in front of `PackageBuilderForm`.
- **Advanced** = today's `PackageBuilderForm`, byte-for-byte unchanged in its own behavior. Shipped:
  reached from one explicit action inside `BuilderWizardForm` ("Open full editor"), which re-parents
  the current step's panel back into it before showing it for real (§3.1). A returning user with a
  valid recent project still reopens straight into Advanced, unchanged since 12.A.3 — no wizard walk
  for a project that already has everything filled in.
- **Advanced → Guided, shipped (12.D.3):** a toolbar "Guided" button on `PackageBuilderForm`
  constructs a `BuilderWizardForm` over a new two-arg constructor
  (`BuilderWizardForm(controller, existingAdvanced)`) that reuses *this same instance* as the
  wizard's hidden section source instead of constructing a second `PackageBuilderForm` against the
  same controller (two instances both subscribed to the same `InstallerController` events was the
  risk flagged in the original design). Hides this form, shows the wizard; the wizard's "Open full
  editor" reverses it, same handoff as cold start. The wizard opens on whichever golden-path step
  matches `ActiveSectionId` (new), not always Identity — switching views does not lose place.
  `File > New`'s existing `NewProject(guided: true)` (`ProjectNewDialog` → `QuickStartWizard`, both
  still intact, unabsorbed) remains a separate, pre-existing on-ramp, not the same code path.
- **Both modes share the same `InstallerController`/`InstallProject`.** Switching modes never
  reloads or duplicates state — `BuilderWizardForm` owns a `PackageBuilderForm` instance
  (`_advanced`) constructed against the same controller, shown for real only on handoff.

## 4. What stays from 12.A, what the discarded patch does not carry forward

| 12.A piece | Status |
|-------------|--------|
| `Ui.BindingSourceRow<T>` (12.A.1) | **Kept, extended.** `SectionViewModel<T>` (§3.3) is built on it. |
| Fix for the 3 unguarded `.Current` sites (12.A.2) | **Kept.** Already correct; nothing to redo. |
| Section-menu groups start expanded (`5b829a8`) | **Kept.** Unrelated to IA, a straightforward UX correctness fix. |
| Cold-start runs `QuickStartWizard` before `PackageBuilderForm` (12.A.3, `d59eda6`) | **Superseded, code replaced** (`c6ada65`, 12.D.2) — cold start now opens `BuilderWizardForm` directly; the `ProjectNewDialog` → `QuickStartWizard` → `PackageBuilderForm` chain no longer runs from cold start (both dialogs are untouched and still reachable from `File > New` inside Advanced mode). |
| `startOnFirstSection` constructor flag, `GuidedStepBanner` (prototyped, uncommitted) | **Discarded.** Built, tested green, removed at the product owner's instruction against patching. Recorded here so it is not reinvented: a banner glued above the existing content host was the lower-risk, lower-value version of §3.1; §3.1 is the version actually being built. |

## 5. Sub-task execution order

Each sub-task lands as its own reviewed, tested commit — "no patching" means *no more ad hoc drips
that don't add up to this architecture*, not "one unreviewable commit." A rewrite this size still
needs staged, verifiable delivery; what changes from the discarded first attempt is that every step
below is aimed at the target in §3, not at a local symptom.

1. **12.C.1** ✅ — `Ui.ValidationCounts`, applied to `ComponentConditionsDialog` and
   `CustomActionsDialog` (the two sites that actually had the duplicate — see §3.3 for why this
   differs from the original sketch). `BuilderSectionCacheTests`-style regression coverage via new
   `ValidationCountsTests`. Commit `49ce0c4`.
2. **12.C.2** ✅ — The concrete-duplication check (§3.3's method) found Prerequisites/Shortcuts/
   Registry hand-rolled the same `BindingSource` + grid + `PropertyGrid` + Add/Remove wiring
   `BuildAdvancedResourceSection<T>` already provides — same collection type
   (`ObservableCollection<T>`), same generic clone — so they were routed through it directly rather
   than through a new `SectionViewModel<T>`. Removed ~90 lines and 9 dead fields; fixed two latent
   gaps (unhidden list-typed columns, unhandled `DataError`) as a side effect. Commit `1e17c64`.
   Components itself stays hand-rolled — its extra typed columns, Scan Source, and Move Up/Down
   buttons are genuinely section-specific, not a copy of this pattern.
3. **12.D.1** ✅ — `Forms/BuilderWizardForm`: stepper shell hosting the 9 steps in §3.2, re-parenting
   panels from a hidden `PackageBuilderForm` instance, `Next` gated by the new
   `PackageBuilderForm.ActiveSectionHasErrors()`. `BuilderWizardFormTests` covers gating, step
   revisits, and the Advanced handoff. Commit `85f4f26`. Found and fixed a real, previously
   unreachable-by-tests production bug along the way (`_contentHost` disposal, `7b47bba` — see §3.1).
4. **12.D.2** ✅ — `Program.RunColdStart` opens `BuilderWizardForm` on a genuinely fresh cold start;
   a valid recent project still reopens straight into Advanced mode, unchanged. Commit `c6ada65`.
5. **12.D.3** ✅ — `PackageBuilderForm.GuideButton()` + `BuilderWizardForm`'s new two-arg
   constructor, reusing the current instance rather than constructing a second one. Hide/Show, not
   `Application.Run` juggling — both forms are simply shown/hidden non-modally, never closed until
   the user actually closes one, so whichever form `Application.Run` originally started with stays
   alive throughout. Commit `c488eb4`, which also fixed a real gap: `BuilderWizardForm.cs`'s own
   twelve `L()` strings (New Project step fields + all `Wizard_*` chrome) had no resx entry in any
   of the 8 languages — `StringResourceCoverageTests` caught it on the first full run after 12.D.1.
6. **12.D.4** (new, split out of the original "Required to reach Build?" column in §3.2) ✅ — One
   concrete rule shipped rather than a generic per-step rule engine: Next/the stepper strip refuse
   to leave the Components step with zero components (that check cannot come from
   `ActiveSectionHasErrors()`, since a `DataGridView`'s row count isn't a named field). No other
   golden-path step got a section-specific rule — field-level validation already covers Identity/
   Source/Output, and Shortcuts/Registry/Code signing are genuinely optional. Commit `4b767bb`.
7. **12.E** ⬜ — Opportunistic: apply `Ui.ValidationCounts`/`Ui.BindingSourceRow<T>` to any further
   hand-rolled duplicate found in the remaining ~28 advanced-only sections, as they are next touched
   for any other reason. Not a blocker for anything above — Advanced mode already works today for
   all of them.

## 6. Risks

| Risk | Mitigation | Status |
|------|------------|--------|
| `BuilderWizardForm` duplicates validation logic already in `PackageBuilderForm.ValidateFieldsInline` | `ActiveSectionHasErrors()` reads the same `_fieldErrors` provider `ValidateFieldsInline` already populates (§3.1) — one validator, one state, two ways to ask about it | Shipped as designed |
| Re-parenting section panels between the hidden `_advanced` and the wizard's step host corrupts state or throws | `BuilderWizardFormTests.EveryGoldenPathStepOpensWithoutThrowing` walks all 9 steps; `.NET` re-parenting is inherently idempotent (add-to-new-parent detaches from old) | Verified in tests |
| Revisiting step 0 after project creation re-runs `InstallerController.New()`, silently discarding edits | Found in review before shipping (not by a test) — fixed by only creating on first visit; regression test added | Fixed, tested |
| `PackageBuilderForm.InvalidateAllContent()`'s reflection filter matches unintended fields by name convention | Found by this phase's new code path, not by existing coverage — fixed (`_contentHost` excluded) plus a named regression test; the filter is inherently fragile to a future field named `_content*` for a non-cached purpose, worth remembering if one is ever added | Fixed, tested |
| Scope creep back into "convert all 40 sections before shipping anything" | 12.E is explicitly opportunistic; 12.D shipped without it | Holding |

## 7. Verification

- Targeted test runs after each change during development (full suite before each commit gates the
  actual merge) — `dotnet test --filter` on the touched classes; full suite for changes touching
  `PackageBuilderForm.cs` given its ~90-test blast radius (§1.4).
- `EverySectionRendersTests`/`EveryDialogRendersTests` required zero changes through 12.D.2 — that
  is the proof Advanced mode is untouched. Confirmed, not just planned.
- `BuilderWizardFormTests` (5 tests): step-0 gating on an empty name, revisiting step 0 does not
  reset an already-created project (named regression), every golden-path step opens without
  throwing, "Open full editor" hands the current section back to Advanced mode with the same
  `InstallProject` reference.
- `ValidationCountsTests` (5 tests) and a new `BuilderSectionCacheTests.ContentHostItselfIsNeverInvalidated`
  regression test.
- Manual (not yet performed): cold start with no recent project → wizard shell, not Advanced; cold
  start with a valid recent project → Advanced directly, no steps re-walked.
