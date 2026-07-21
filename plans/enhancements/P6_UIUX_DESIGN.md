# Phase 6: UI/UX Overhaul — Wizard Correctness, Branding, Builder — Design Document

**Status:** ⬜ not started · **Priority:** P1 · **Depends on:** P1 (contracts); parallelizable with P2–P5
**Tracker:** [MASTER_TRACKER.md](MASTER_TRACKER.md) · **Evidence:** [R0_REVIEW_FINDINGS.md](R0_REVIEW_FINDINGS.md) §4 (U1–U3, U7–U12)

## 0. Problem Statement

The end-user wizard has correctness bugs (stepper mismatch + validation bypass, U2), shows
no real progress (U3), and silently ignores the branding the author configured (U1). The
builder freezes on large sources (U9), has cancel buttons that don't cancel (U10),
developer-grade editors (U11), dead sections (U12), and three uncoordinated color systems
across wizard/builder/dialogs with widespread absolute-pixel layouts that break above
100% DPI (U7, U8). House style is Beep controls + theme tokens (`beep-winform-design`);
only the wizard shell follows it today.

## 1. Goals

1. Wizard stepper reflects the real page list and stepper clicks cannot skip validation.
   Acceptance: AdditionalTasks appears as a step; clicking a forward step runs each
   intermediate page's `Validate()`.
2. Install shows a determinate progress bar + current-step text bound to
   `IProgress<PassedArgs>`. Acceptance: visible bar advances during `/SELFTEST`-style run.
3. Authored branding applies at runtime: sidebar colors, accent, wizard image, icon,
   theme. Acceptance: building a `.bsetup` with custom `AccentColor`+`WizardImageFile`
   visibly changes the shipped wizard.
4. No blocking filesystem work on the builder UI thread; every long op cancellable.
   Acceptance: scanning a 10k-file tree keeps the UI responsive; Cancel actually stops
   build/scan/publish.
5. One shared theme-token layer; all forms/dialogs DPI-safe (`AutoScaleMode.Dpi` +
   layout containers instead of absolute coordinates). Acceptance: visual pass at 150%
   scaling shows no clipping on any form.
6. Dead UI removed or wired. Acceptance: every nav section reachable; `WizardPreviewForm`
   tab shell simplified; WizardPages checklist actually toggles pages.

## 2. Design

### 2.1 Wizard correctness (U2)

- `BeepModernInstallerForm`: generate stepper items **from `_pages`**
  (`BuildPages`, `:307-335`) instead of the hardcoded 7-item `Steps[]` (`:52-61`);
  hide post-Ready pages (Complete/Error) from the stepper.
- Stepper click handler (`:289-293`): navigating forward runs `Validate()` on every page
  between current and target; on first failure, land on the failing page. Backward jumps
  stay free.
- On page swap, set focus to the page's first focusable control (fixes focus loss after
  `_content.Controls.Clear()`).

### 2.2 Install progress (U3)

Replace the single label (`:437-444`) with a `BeepProgressBar` + step label + optional
details expander fed by the existing `Progress<PassedArgs>` (`:486-498`). `PassedArgs`
already carries messages; percent derives from completed-step count over
`InstallWizardGraphFactory.StepIds` total (P5). Cancel button requests the token from P3.

### 2.3 Branding pipeline (U1)

- Runtime: `BeepModernInstallerForm.InitializeUi` consumes `project.Branding`
  (P1 contract): `ThemeLoader.ParseColor` for sidebar/accent, `BannerLoader.Load` for
  wizard image, window icon from `SetupIconFile`, `DefaultTheme` →
  `BeepThemesManager.SetCurrentTheme`. Both helpers finally get called
  (today: defined, never referenced).
- Builder: live preview panel in the Branding section re-renders on change (reuses
  `WizardPreviewForm` host).

### 2.4 Builder responsiveness + cancellation (U9, U10)

- `ScanAndPopulate` (`PackageBuilderForm.cs:1631-1638`), `RefreshFileTree` (`:1588-1600`),
  and per-file `FileInfo.Length` sizing (`:1664`, `ComponentFilesDialog.cs:163-169`) move
  to `Task.Run` with progress + `ct`; tree populated from a prebuilt model, UI marshalled
  once.
- `BuildProgressForm`'s CTS (`:88-97`) flows into `IInstallerBuilder.Run(..., ct)` (P3).
  Publish gains the same token path.

### 2.5 Shared theme tokens + DPI (U7, U8)

- New `Ui/InstallerTheme.cs`: single token source (surface, text, muted, accent, danger,
  success) resolved from `BeepThemesManager.CurrentTheme` with fallbacks — replaces the
  three palettes (`PackageBuilderForm.cs:20-28,1255-1343`, per-dialog inline colors,
  wizard hardcodes `BeepModernInstallerForm.cs:92-135`). Dark theme follows automatically
  when the Beep theme is dark.
- All forms/dialogs: `AutoScaleMode.Dpi` (currently only `PackageBuilderForm:161-162` and
  `ComponentFilesDialog:39-40`); wizard pages re-laid on `TableLayoutPanel`/`FlowLayoutPanel`
  (pattern: `CustomPage.cs:32-39`, the one page already flow-based). Fix double-padding:
  pages stop self-padding, the shell's 24px content padding (`:277`) is the single source.
- `LanguageManagerForm` toolbar (`:45-71`): fixed x-pixels → `FlowLayoutPanel`; emoji
  glyphs → embedded SVG icons (`Resources/Icons`, U12/#14).
- Builder plain controls migrate to Beep equivalents opportunistically per section
  (Open Decision D4 sets the ambition level: token-layer restyle only vs full Beep
  control adoption).

### 2.6 Builder editor & dead-UI cleanup (U11, U12)

- Components grid: explicit column definitions (delete post-hoc hiding `:594-605`);
  contextual row buttons for Files/Conditions/Actions (dialogs pre-select the row's
  component — fixes `ComponentConditionsDialog.cs:52-55` re-selection friction).
- Inline validation: field-level error providers on Identity/Layout (product name,
  version format, source dir exists) instead of MessageBox-on-build only (`:1478`).
- Remove or wire the 8 unreachable sections (`OnSectionSelected` handlers with no nav
  entry, `:272-279`); wire WizardPages checkboxes to `EnabledWizardPages` so they
  actually govern `BuildPages`; drop `WizardPreviewForm`'s single-tab `TabControl`
  (`:33-34`).

## 3. API surface

N/A (desktop UI).

## 4. Files to change

| Action | File | Lines | Risk |
|--------|------|-------|------|
| Modify | `Forms/BeepModernInstallerForm.cs` (stepper, progress, branding, focus) | ~180 | medium |
| New | `Ui/InstallerTheme.cs` | ~80 | low |
| Modify | `Forms/PackageBuilderForm.cs` (async scan, tokens, grid, validation, nav cleanup) | ~350 | high |
| Modify | all `Pages/*.cs` (layout containers, padding, tokens) | ~400 | medium |
| Modify | all dialogs (`Forms/*.cs`) + `Ui/LeftNavPanel.cs` (tokens, DPI) | ~250 | medium |
| Delete | dead sections/vestigial code in builder + `WizardPreviewForm` tab shell | -150 | low |

## 5. Error handling matrix

| Operation | Before | After |
|-----------|--------|-------|
| Scan fails mid-tree | freeze or silent partial | toast/status error + partial results labeled |
| Branding asset missing (image/icon path) | n/a (never loaded) | warning badge in builder; runtime falls back to defaults silently |
| Stepper jump past failing page | allowed (skips validation) | blocked; failing page shown with its message |

## 6. Backward compatibility

`.bsetup` unchanged. Old scripts without `Branding` values render exactly like today
(token defaults match current hardcoded palette).

## 7. Verification

- Manual matrix: 100%/150%/200% DPI on builder + wizard + every dialog.
- Scripted: build HelloApp with custom branding → run wizard → screenshot check.
- `/SELFTEST` + full test suite; `CustomPageTests`, `ComponentSelectionTests` green.
- Keyboard-only wizard walkthrough (ties into P7 a11y gate).

## 8. Risks

| Risk | Mitigation |
|------|------------|
| PackageBuilderForm churn (1,832 lines) destabilizes builder | section-by-section commits; keep behavior parity per section |
| Beep control swaps change perceived look | D4 sets scope; token-layer-first keeps visuals stable |
| Page re-layout regressions | per-page before/after screenshots at 3 DPI levels |

## 9. Out of scope

Localization/RTL/accessibility passes (P7) — though layouts built here must not assume
LTR or fixed string widths. Undo/redo for the builder (backlog).

## 10. Sub-task execution order

1. **6.A.1** Wizard: stepper from pages + gated jumps + focus. Verify: manual walkthrough; can't skip license.
2. **6.A.2** Install progress bar + cancel. Verify: visible progress on selftest-style install.
3. **6.A.3** Branding pipeline runtime + builder preview. Verify: custom-branding build shows colors/banner.
4. **6.B.1** `InstallerTheme` tokens; migrate wizard + dialogs + builder palettes. Verify: 150% DPI pass, dark Beep theme sanity.
5. **6.B.2** Page/dialog re-layout to containers + DPI mode everywhere. Verify: DPI matrix.
6. **6.C.1** Async scan/tree/sizing + real cancellation. Verify: 10k-file scan responsive; cancel works.
7. **6.C.2** Grid/editor improvements + contextual dialogs + inline validation. Verify: component edit flow without re-selection.
8. **6.C.3** Dead-UI removal + WizardPages wiring. Verify: every nav section reachable; disabled page really skipped in preview.
