# Phase 12: Wizard-First IA + Shared Editing ViewModel — Design Document

**Status:** 🟡 started (12.A.1, 12.A.2, 12.A.3 shipped) · **Priority:** P1 · **Depends on:** P6 (theme tokens, dead-UI cleanup)
**Tracker:** [MASTER_TRACKER.md](MASTER_TRACKER.md)

## 0. Problem statement

Two things the product owner reported directly (2026-09-10):

1. **"Use ViewModel patterns to avoid duplicating work."** The same unsafe pattern —
   `BindingSource.Current` read directly in a button handler — was hand-written four times
   across the builder (`PackageBuilderForm.BuildAdvancedResourceSection<T>`,
   `ComponentConditionsDialog.OnRemove`, `CustomActionsDialog.OnRemove`/`BindEditorToCurrent`,
   `ComponentFilesDialog`'s remove handler). `BindingSource.Current` throws
   `IndexOutOfRangeException("Index -1 does not have a value")` on an empty list rather than
   returning null — this exact defect was found and fixed once, in
   `BuildAdvancedResourceSection`, by commit `33bf94c` ("Fix the crash behind 'many screens have
   errors'"). The other three sites were never touched, so the same crash is still live in three
   dialogs today: open Custom Actions, Conditions, or a component's file list on a project that
   has zero rows yet (the common case — a freshly added component has no conditions or files),
   click Remove, and it throws. Nothing caught it because `EveryDialogRendersTests` presses
   every safe button in control-tree order, which is Add-before-Remove for all three dialogs, so
   Remove was only ever exercised against a list Add had just populated.
2. **"System should be more user-friendly and wizard-by-default and primary, and not
   scattered forms — separate forms are always for advanced."** The builder's authoring surface
   is a `LeftNavPanel` with 8 groups / ~40 sections navigated freely (no guided order), plus
   roughly a dozen independent popup dialogs reached from toolbar buttons
   (`CodeSigningWizard`, `PackagingWizard`, `PublishWizard`, `ResourceWizard`,
   `TemplateUpdateDialog`, `LanguageManagerForm`, `UpdateCenterForm`, `CustomActionsDialog`,
   `ComponentConditionsDialog`, …). A guided on-ramp already exists —
   `QuickStartWizard` — but its own doc comment frames it as strictly optional: *"The full
   builder stays exactly as it was for people who want it; this is the on-ramp, not a
   replacement."* It runs today only when the user clicks "Quick Start" on the toolbar, or
   implicitly after `File > New` (`PackageBuilderForm.NewProject(guided: true)`). Cold start
   (`Beep.Installer.exe` with no args) skipped both and landed straight on an unsaved blank
   project inside the full flat builder — the literal "scattered forms are what I see first"
   the report is about.

Also raised in the same report and already fixed same-day, tracked here for continuity:
**the section menu forced browsing one group at a time** — the P6 collapse-by-default change
(`33bf94c`) made every group but the current one start collapsed, which traded "scroll past 40
rows" for "click through 7 headers one at a time." Fixed by starting every group expanded
(`5b829a8`); collapsing a header is still available, just not forced.

## 1. Goals

1. One safe, shared "current row" accessor for every `BindingSource`-backed list editor —
   no dialog reimplements (or forgets) the empty-list fix.
   Acceptance: `Ui.BindingSourceRow<T>` used at all four sites; Remove/Duplicate on an empty
   list is a no-op (button disabled), not a crash, proven by a test that presses Remove
   *before* Add.
2. A cold start with nothing to resume shows the guided on-ramp first, not the flat builder.
   Acceptance: `Beep.Installer.exe` with no args and no recent project runs
   `ProjectNewDialog` → `QuickStartWizard` before the main window; a returning user with a
   valid recent project is not interrupted (silently reopens it, same as today).
3. (Remaining) Extend the guided path past project creation: the common ongoing edits
   (components, source, output naming, a build) get a wizard-paced surface as the default view;
   the full flat nav + every advanced dialog stay reachable behind an explicit "Advanced"
   affordance, never removed.

## 2. Design

### 2.1 `Ui.BindingSourceRow<T>` (shipped)

```csharp
public sealed class BindingSourceRow<T> where T : class
{
    public T? Current => /* read binding[binding.Position], guarded — never binding.Current */;
    public bool HasRow => Current != null;
    public void WireRowButtons(params Control[] rowButtons); // disable when !HasRow
}
```

Wraps the fix `33bf94c` proved (`Ui/LeftNavPanel.cs` sibling — lives in `Beep.Installer/Ui/`
alongside `InstallerTheme.cs`). `BuildAdvancedResourceSection<T>` (17 sections),
`ComponentConditionsDialog`, `CustomActionsDialog`, `ComponentFilesDialog` all construct one
over their `BindingSource` and call `WireRowButtons` once instead of hand-rolling
`CurrentItem()`/`RefreshRowButtons()` locals or, worse, reading `.Current` directly.

This is the first shared primitive, not the last. If a future section needs more than "safe
current row + button enablement" (e.g. shared validation-summary wiring, shared
add/duplicate/remove commands as `ICommand`-style objects), extend `BindingSourceRow<T>` or add
a sibling type in `Ui/` rather than growing a fifth inline copy.

### 2.2 Cold-start guided flow (shipped)

`Program.CreateColdStartController()` (`Cli/ProgramVerbs.cs`), called from `RunDefaultMode`
only in the windowed, non-runtime, no-DI-container branch:

1. `RecentProjects.Load().FirstOrDefault()` — if a path is recorded and the file still exists,
   open it and return. Returning users see no change.
2. Otherwise: `ProjectNewDialog` → on OK, `controller.New(...)` then `QuickStartWizard`
   (mirrors `PackageBuilderForm.NewProject(guided: true)` exactly). Cancelling either dialog
   falls back to a blank `InstallerController()` — the pre-existing behavior — rather than
   leaving no window at all.

Deliberately *not* touched: `PackageBuilderForm`'s constructor. Dozens of tests
(`EverySectionRendersTests`, `EveryDialogRendersTests`, …) construct
`new PackageBuilderForm(controller, args)` directly and interact with it immediately; a modal
dialog in the constructor would hang every one of them. Gating at the `Program`/`ProgramVerbs`
level, before the form is ever constructed, keeps the guided flow out of every code path that
builds a `PackageBuilderForm` directly.

**Not covered by an automated test.** `RecentProjects` persists to the real
`%APPDATA%\BeepInstaller\recent.json` with no seam to redirect it, and nothing in the suite
touches that file today — doing so from a test would mutate the developer's actual recent-files
list, which is worse than the gap. `RunDefaultMode`'s windowed branch was already outside test
coverage (`Application.Run` blocks). Verify manually: delete `%APPDATA%\BeepInstaller\recent.json`,
launch `Beep.Installer.exe` with no args, confirm "New Installer Script" appears before the
builder; relaunch and confirm it reopens the project silently.

### 2.3 Extending the guided path (not yet started)

Open question this phase has not answered: how far past project creation does "wizard by
default" go? Options, roughly increasing in scope/risk:

- **A.** Stop here — guided project *creation*, flat builder for everything after. Lowest risk,
  smallest change from today, but likely does not fully satisfy "wizard... primary," which read
  as more than a one-time on-ramp.
- **B.** A persistent "Guided" / "Advanced" mode toggle on `PackageBuilderForm` itself: Guided
  mode re-sequences the existing sections into a Next/Back flow (reusing the section content
  panels that already exist — no rewrite of each section's UI, just an outer stepper replacing
  free nav); Advanced mode is today's `LeftNavPanel` + toolbar, unchanged. Default to Guided.
- **C.** Split into two windows: a new lightweight wizard shell as the primary experience,
  `PackageBuilderForm` demoted to an explicitly-launched "Advanced" editor. Highest
  risk — `PackageBuilderForm` is 3,200+ lines and the composition root
  (`Services?.GetService(typeof(PackageBuilderForm))`) assumes it is *the* shell.

Recommend **B**: it reuses every existing section panel (no content rewrite, no risk to the 40
sections' own logic/tests), and "Advanced" stays one click away rather than a separate
executable path — closest to "separate forms are always for advanced" without a full rewrite.
Needs its own design pass (stepper ordering across 40 sections, which are skippable vs.
required, how validation gates forward navigation — precedent: `BeepModernInstallerForm`'s
stepper already does gated forward navigation, P6.A.1) before implementation. Not attempted in
this pass — `PackageBuilderForm` is large enough, and used by enough tests, that reshaping its
navigation deserves its own reviewed change rather than being folded into the same commit as
the bug fixes above.

## 3. API surface

`Beep.Installer.Ui.BindingSourceRow<T>` — see 2.1. No other new public surface yet.

## 4. Files changed (12.A.1–12.A.3)

| Action | File |
|--------|------|
| New | `Ui/BindingSourceRow.cs` |
| Modify | `Forms/PackageBuilderForm.cs` (`BuildAdvancedResourceSection<T>` uses it; `Cli/ProgramVerbs.cs` cold-start flow) |
| Modify | `Forms/ComponentConditionsDialog.cs`, `Forms/CustomActionsDialog.cs`, `Forms/ComponentFilesDialog.cs` (fix + `WireRowButtons`) |
| Modify | `Ui/LeftNavPanel.cs` (groups start expanded, not collapsed-except-current) |
| Modify | `Beep.Installer.Tests/EveryDialogRendersTests.cs` (`RemovingWithNothingSelectedDoesNotThrow`) |
| Modify | `Beep.Installer.Tests/LeftNavGroupingTests.cs` (updated expectations) |

## 5. Verification

- Full suite green (see tracker entry for count).
- `RemovingWithNothingSelectedDoesNotThrow` reproduces the pre-fix crash against the old code
  (Remove pressed before Add, on each dialog's naturally-empty list) and passes against the fix.
- Cold-start flow: manual verification only (2.2) — no automated seam for `%APPDATA%`.

## 6. Risks

| Risk | Mitigation |
|------|------------|
| `CreateColdStartController` silently changes first-run UX for every fresh install | Scoped to *only* the no-recent-project case; a returning user's launch is byte-for-byte the same as before (open their last project, no dialog) |
| Section B (2.3) reshapes `PackageBuilderForm` navigation, used by ~90 tests across `EverySectionRendersTests`/`EveryDialogRendersTests`/keyboard-walkthrough tests | Not started; when it is, land the stepper as an outer shell around the existing section panels so per-section tests keep constructing/asserting on panels directly, unaffected by the navigation wrapper |

## 7. Sub-task execution order

1. **12.A.1** `Ui.BindingSourceRow<T>` + apply to all four duplicate sites. ✅
2. **12.A.2** Regression test: Remove/Duplicate pressed before Add, per dialog. ✅
3. **12.A.3** Cold start runs the guided on-ramp when there is nothing to resume. ✅
4. **12.B.1** Design pass for 2.3 option B: stepper ordering, required-vs-skippable sections,
   validation-gated forward navigation, how "Advanced" is reached and what it shows. ⬜
5. **12.B.2** Implement the Guided/Advanced toggle per 12.B.1. ⬜
