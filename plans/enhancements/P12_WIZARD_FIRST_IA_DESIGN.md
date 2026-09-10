# Phase 12: Wizard-First IA + Shared Editing ViewModel — Design Document

**Status:** 🟡 12.A shipped (foundation fixes) · 12.B–12.E planned, not started · **Priority:** P1
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
   `Ui.BindingSourceRow<T>` and a new `SectionViewModel<T>` (§3.3), not a fifth copy.
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

### 3.1 The wizard shell — reuse the pattern, not the class

`BeepModernInstallerForm` already proves the pattern this needs: a stepper generated from a page
list, `Next` blocked until the current page's `Validate()` passes, `Back` always free. The new
`Forms/BuilderWizardForm` (working name) is a *sibling* of that pattern for the authoring side, not
a shared base class with it — the runtime wizard installs an app; this one edits a `.bsetup` project,
and their page contracts differ enough (runtime pages read `SetupContext`; builder steps read
`InstallProject` directly through a ViewModel) that forcing a shared type would be the kind of
premature abstraction that costs more than it saves. What *is* shared is the shape: an ordered step
list, a stepper sidebar/header showing them, `Next`/`Back`, and per-step validation gating.

```
Forms/BuilderWizardForm.cs        # the stepper shell: Next/Back, step list, hosts one step at a time
ViewModels/BuilderStep.cs         # { Id, Title, Func<Panel> Build, Func<ValidationResult> Validate }
```

Each `BuilderStep.Build` returns **the exact same `Panel` the existing `Build*Section()` methods on
`PackageBuilderForm` already produce** — no section's own UI is rewritten by this phase. The wizard
shell is a new way to sequence and gate existing panels, exactly as the discarded banner prototype
already established was the low-risk way to do this (§0); what changes from that prototype is that
the shell *is* the primary window instead of a strip glued onto the old one, and forward movement is
actually gated on validity instead of freely clickable.

### 3.2 Step order (the "golden path")

The wizard shell's default steps, in order, replacing the three-window
`ProjectNewDialog → QuickStartWizard → PackageBuilderForm` chain the discarded patch used:

| # | Step | Backing section(s) | Required to reach Build? |
|---|------|---------------------|---------------------------|
| 0 | New project | `ProjectNewDialog`'s fields (template, product name, version, publisher, source dir) — absorbed, not a separate window | Yes |
| 1 | Identity | `identity` | Yes |
| 2 | Source | `source`, `includes` | Yes |
| 3 | Components | `components` | Yes (at least one) |
| 4 | Shortcuts | `shortcuts` | No — skippable |
| 5 | Registry | `registry` | No — skippable |
| 6 | Output | `output`, `compression`, `package` | Yes |
| 7 | Code signing | `codesign` | No — skippable, matches today's optional signing |
| 8 | Build | `build` (runs the existing build pipeline) | — terminal step |

Every other section (Prerequisites, Branding, WizardPages, CustomPages, the ten "Advanced
Resources" sections, MSIX\*, UpdateChannels, PrerequisiteCatalogs, Supersedence, Packages, Script,
Log, Result) stays exactly where it is today: inside Advanced mode's `LeftNavPanel`, unchanged,
unremoved. This is a curated subset by design — the wizard is a *path through the common case*, not
a re-hosting of all 40 sections; forcing everything into a linear order would recreate the "40 rows"
problem P6 already fixed once, in stepper form this time.

### 3.3 The ViewModel layer

```csharp
namespace Beep.Installer.ViewModels;

// Builds on Ui.BindingSourceRow<T> (12.A.1) rather than replacing it.
public sealed class SectionViewModel<T> where T : class
{
    public BindingSource Binding { get; }
    public BindingSourceRow<T> Row { get; }          // safe current-row access (already shipped)
    public IReadOnlyList<string> FieldNames { get; }  // for per-step validation filtering, see below

    public void Add(T item);
    public void Duplicate();                          // no-op, not an exception, with nothing selected
    public void Remove();                              // same
    public event EventHandler? Changed;                // list OR position changed — one event to redraw from

    // Filters ProjectSchemaService.Validate(project) down to this section's own field names —
    // the same validator PackageBuilderForm.ValidateFieldsInline already calls today, just scoped.
    public ValidationResult Validate(InstallProject project);
}
```

This is deliberately small. It does not become a full MVVM framework with `ICommand`/relay-command
machinery — WinForms data-binding via `BindingSource` already does most of the work, and the
project's own convention (`Ui/BindingSourceRow.cs`) is "wrap the one unsafe operation, wire the one
repeated behavior," not "replace WinForms binding." `SectionViewModel<T>.Validate` is the new piece:
it is what lets the wizard shell (§3.1) ask "is step N allowed to advance" without the shell knowing
anything about what step N contains — the same separation `IInstallerPage.Validate()` already gives
the runtime wizard.

Sections that are single-value editors, not lists (Identity, Layout, Output naming) do not need
`SectionViewModel<T>` — they already just bind `PropertyGrid`/named `TextBox`es directly to
`InstallProject` properties, which is fine as-is. The ViewModel layer's job is specifically the
~20 places that hand-roll list-editing (`BuildAdvancedResourceSection<T>`'s 17 call sites,
`ComponentConditionsDialog`, `CustomActionsDialog`, `ComponentFilesDialog`, and any future one),
where the duplication actually lives.

### 3.4 Mode switch: Guided (default) vs Advanced

- **Guided** = `BuilderWizardForm`. This is what cold start and `File > New` open.
- **Advanced** = today's `PackageBuilderForm`, byte-for-byte unchanged in its own behavior. Reached
  from one explicit action inside `BuilderWizardForm` ("Open full editor" / similar), and from the
  wizard's own "Build" terminal step ("continue editing" after a successful or failed build).
  Once in Advanced, the user can return to Guided from the toolbar (a "Guided" button, the same idea
  the discarded prototype's toggle had, kept because the destination — Advanced mode's toolbar — is
  exactly where "separate forms are for advanced" tools already live: Actions, Conditions, Signing,
  Packaging, Publish, Templates, Languages).
- **Both modes share the same `InstallerController`/`InstallProject`.** Switching modes never
  reloads or duplicates state — `BuilderWizardForm` and `PackageBuilderForm` are two views over the
  same controller, exactly as the discarded cold-start patch already established for the
  `ProjectNewDialog`/`QuickStartWizard` hand-off, just generalized to the whole session instead of
  only the first launch.

## 4. What stays from 12.A, what the discarded patch does not carry forward

| 12.A piece | Status |
|-------------|--------|
| `Ui.BindingSourceRow<T>` (12.A.1) | **Kept, extended.** `SectionViewModel<T>` (§3.3) is built on it. |
| Fix for the 3 unguarded `.Current` sites (12.A.2) | **Kept.** Already correct; nothing to redo. |
| Section-menu groups start expanded (`5b829a8`) | **Kept.** Unrelated to IA, a straightforward UX correctness fix. |
| Cold-start runs `QuickStartWizard` before `PackageBuilderForm` (12.A.3, `d59eda6`) | **Superseded** by §3.1/§3.4 — cold start opens `BuilderWizardForm` directly; the `ProjectNewDialog` → `QuickStartWizard` → `PackageBuilderForm` chain goes away in favor of the wizard shell owning steps 0–8 itself. |
| `startOnFirstSection` constructor flag, `GuidedStepBanner` (prototyped, uncommitted) | **Discarded.** Built, tested green, removed at the product owner's instruction against patching. Recorded here so it is not reinvented: a banner glued above the existing content host was the lower-risk, lower-value version of §3.1; §3.1 is the version actually being built. |

## 5. Sub-task execution order

Each sub-task lands as its own reviewed, tested commit — "no patching" means *no more ad hoc drips
that don't add up to this architecture*, not "one unreviewable commit." A rewrite this size still
needs staged, verifiable delivery; what changes from the discarded first attempt is that every step
below is aimed at the target in §3, not at a local symptom.

1. **12.C.1** — `ViewModels/SectionViewModel<T>`. Convert the four sites `Ui.BindingSourceRow<T>`
   already touched (`BuildAdvancedResourceSection<T>`, `ComponentConditionsDialog`,
   `CustomActionsDialog`, `ComponentFilesDialog`) to use it for validation too, not just safe-row
   access. Verify: existing tests for these four unchanged; new tests for `Validate()` scoping.
2. **12.C.2** — Convert the golden-path sections' list editors (Components, Shortcuts, Registry) to
   `SectionViewModel<T>`. Identity/Source/Output stay direct-bound (§3.3, no list to wrap).
3. **12.D.1** — `Forms/BuilderWizardForm` + `ViewModels/BuilderStep`: stepper shell hosting the
   9 steps in §3.2, `Next` gated on `SectionViewModel.Validate`/existing field validation, `Back`
   always free. No new section UI — steps host the existing `Build*Section()` panels via a small
   adapter that constructs them against the shared `InstallerController`.
4. **12.D.2** — Step 0 (new project) absorbs `ProjectNewDialog`'s fields directly into the shell
   instead of launching it as a separate window. `Program.RunDefaultMode`/`CreateColdStartController`
   changed to open `BuilderWizardForm` on cold start (recent project still reopens silently and
   skips straight to Advanced mode — a returning user is not walked through steps 0–8 again for a
   project that already has all of it filled in).
5. **12.D.3** — "Open full editor" action wires Guided → Advanced; a toolbar "Guided" button wires
   Advanced → Guided. Verify: full existing suite green with zero test changes required in
   `EverySectionRendersTests`/`EveryDialogRendersTests` (they construct `PackageBuilderForm`
   directly and never touch `BuilderWizardForm`, so this is the regression gate).
6. **12.E** — Opportunistic: convert remaining list-editing sections (Prerequisites, the ten
   Advanced Resources sections, Packages, MSIX Optional Packages) to `SectionViewModel<T>` as they
   are next touched for any other reason, rather than as one big sweep. Not a blocker for 12.D
   shipping — Advanced mode already works today for all of them.

## 6. Risks

| Risk | Mitigation |
|------|------------|
| `BuilderWizardForm` duplicates validation logic already in `PackageBuilderForm.ValidateFieldsInline` | `SectionViewModel.Validate` calls the same `ProjectSchemaService.Validate(_project)` the builder already uses (§3.3) — one validator, two presentations of its output, not two validators |
| Absorbing `ProjectNewDialog`/`QuickStartWizard`'s fields into step 0/steps 1–3 changes their tested behavior | Their *field logic* (template application, default naming) moves, their *UI* does not need to — extract the field-handling into a method both the old dialog (kept for `File > New` inside Advanced mode, if wanted) and the new step call, rather than rewriting it twice |
| `BuildAdvancedResourceSection<T>` (17 call sites) is genuinely load-bearing; changing its validation path wrong breaks Typed Resources broadly | 12.C.1 lands it alone, verified against all 17 resource kinds, before anything in 12.D depends on it |
| Scope creep back into "convert all 40 sections to ViewModels before shipping the wizard" | 12.E is explicitly opportunistic and unblocked from 12.D; the wizard's 9 steps in §3.2 do not require it |

## 7. Verification

- Full suite green after every sub-task, not just at the end.
- `EverySectionRendersTests`/`EveryDialogRendersTests` require zero changes through 12.D — that is
  the proof Advanced mode is untouched.
- New: `BuilderWizardFormTests` — each of the 9 steps opens; `Next` is blocked on an empty required
  step and unblocked once filled; `Back` is always enabled except on step 0; "Open full editor"
  hands off to a `PackageBuilderForm` on the same `InstallerController` with the same data.
- Manual: cold start with no recent project → wizard shell, not Advanced; cold start with a valid
  recent project → Advanced directly, no steps re-walked (§5.4).
