# Phase 7: Localization, RTL, Accessibility — Design Document

**Status:** ⬜ not started · **Priority:** P2 · **Depends on:** P6 (layouts must be container-based first)
**Tracker:** [MASTER_TRACKER.md](MASTER_TRACKER.md) · **Evidence:** [R0_REVIEW_FINDINGS.md](R0_REVIEW_FINDINGS.md) §4 (U4–U6)

## 0. Problem Statement

- `RtlHelper` (`Engine/RtlHelper.cs`) is never called; Arabic/Hebrew/Farsi/Urdu users get
  LTR layouts (U5).
- `LanguageManager.SetLanguage` is only invoked from `Initialize()` (system culture);
  end users cannot switch language in the wizard (U5).
- resx drift: en = 38 keys, all 7 other languages = 30 — the `Error_*` block exists only
  in English (U5).
- Hardcoded user-facing English literals throughout pages, shell dialogs, and the entire
  builder (U6; e.g. `WelcomePage.cs:17`, `ReadyPage.cs:55-82`, `PrerequisitePage.cs:32-258`,
  `BeepModernInstallerForm.cs:301-303,421`).
- `Accessibility.EnsureAccessibility` runs only on the wizard (`BeepModernInstallerForm.cs:85`)
  and its `IsInteractive` filter (`Engine/Accessibility.cs:80-82`) excludes Beep controls —
  so Next/Back/Cancel have no `AccessibleName`; builder and dialogs get no a11y pass at
  all; tab order = child-add order on absolutely-positioned pages (U4).
- BeepDM already ships localization POCOs (`DataManagementModelsStandard/Installer/LanguageModels.cs`
  — `AppLanguage`, `TranslationString`, `TranslationCategory`) that the local manager ignores.

## 1. Goals

1. End user can switch wizard language on the Welcome page; choice applies immediately
   and persists into the install log. Acceptance: switching to Arabic re-renders RTL.
2. RTL wired: `RtlHelper.ApplyRtl` invoked for RTL cultures across wizard + pages.
   Acceptance: mirrored layout verified for `ar`.
3. Key parity: every resx has the same key set; CI-able check. Acceptance: new test
   `LanguageResxParityTests` green (38/38 × 8 cultures).
4. No hardcoded user-visible literals in runtime wizard pages/shell; builder strings
   routed too (best effort, tracked list). Acceptance: string-literal audit script
   reports 0 for `Pages/` + `BeepModernInstallerForm`.
5. Accessibility pass on every form: names for Beep controls, logical tab order, focus
   on navigation, status not conveyed by color alone. Acceptance: Windows Narrator
   walkthrough completes the wizard; Accessibility Insights fast-pass clean on builder.

## 2. Design

### 2.1 Language switching + RTL

- Welcome page gains a `BeepComboBox` listing `LanguageManager.SupportedCultures`
  (the 8 in `LanguageManager.cs:31`); on change: `SetLanguage(culture)`, raise
  `LanguageChanged`, shell re-binds all page texts (pages expose `ReloadStrings()`
  added to `IInstallerPage` as default-interface or via base class), then
  `RtlHelper.ApplyRtl(form)` when `LanguageManager.IsRtl(culture)`.
- Persist choice in `SetupContext` (typed accessor) so install log + silent mode record it.
- `LocaleFormatter` keeps handling sizes/dates (already wired).
- Alignment with BeepDM: `LanguageManager` internally maps resx entries onto
  `TranslationString`/`AppLanguage` models so authored translations can round-trip with
  the `LanguageManagerForm` editor and any future BeepDM-side tooling. The manager class
  itself stays in the shell (BeepDM has models only, no manager — confirmed by survey).

### 2.2 String routing + parity

- Sweep `Pages/*`, `BeepModernInstallerForm`, `Steps` progress messages: literals →
  `LanguageManager.T("key")` with keys added to `Strings_en.resx` and translated stubs
  (English fallback) in the other 7.
- Fill the 8 missing `Error_*` keys in all non-en resx.
- New test: load all 8 resx via `ResXResourceReader`, assert identical key sets.
- Builder: route toolbar/nav/section captions + MessageBox texts (largest win first);
  full builder coverage is a stretch goal tracked in the task table.

### 2.3 Accessibility

- `Accessibility.IsInteractive` extended: `or c is BeepButton or BeepLabel or BeepComboBox ...`
  (or duck-typed on `TheTechIdea.Beep.Winform.Controls.Base` if available) so Beep
  controls receive `AccessibleName` (`Engine/Accessibility.cs:80-82`).
- `EnsureAccessibility` called in: `PackageBuilderForm`, every dialog ctor, every wizard
  page after build (currently only the shell `:85`).
- Tab order: after P6's container layouts, `NormalizeTabOrder` follows visual order;
  verify per page.
- Focus: shell sets focus to first control on page enter (done in P6.A.1, verified here).
- Color-alone status: PrerequisitePage badge (`:136-142`) and BuildResult get ✓/✕ glyph +
  text alongside color.
- High-contrast: `ApplyHighContrastColors` covers Beep-painted controls via
  `InstallerTheme` (P6) high-contrast token set.

## 3. API surface

N/A.

## 4. Files to change

| Action | File | Lines | Risk |
|--------|------|-------|------|
| Modify | `Pages/*.cs` (string routing + ReloadStrings) | ~300 | medium |
| Modify | `Forms/BeepModernInstallerForm.cs` (language combo, RTL, rebind) | ~90 | medium |
| Modify | `Lang/LanguageManager.cs` (IsRtl, SupportedCultures, model mapping) | ~60 | low |
| Modify | `Lang/Strings_*.resx` ×8 (key parity + new keys) | ~200 | low |
| Modify | `Engine/Accessibility.cs` (Beep-control support) + call sites in builder/dialogs | ~80 | low |
| New | `Beep.Installer.Tests/LanguageResxParityTests.cs`, string-literal audit script | ~120 | low |

## 5. Error handling matrix

| Operation | Before | After |
|-----------|--------|-------|
| Missing key in culture | silent English fallback | unchanged at runtime; **build-time parity test fails** |
| Language switch mid-wizard | impossible | re-render; failures fall back to previous culture with logged warning |

## 6. Backward compatibility

Default behavior (system culture, LTR) unchanged for non-RTL users who never touch the
combo.

## 7. Verification

```
dotnet test --filter LanguageResxParity
# audit script: grep Pages/ + wizard shell for quoted sentence-case literals → 0
# Manual: Narrator wizard walkthrough; Accessibility Insights fast-pass on builder;
# ar culture: full wizard at 100%/150% DPI, screenshot mirror check
```

## 8. Risks

| Risk | Mitigation |
|------|------------|
| RTL mirroring breaks Beep custom-painted controls (stepper) | test early; if BeepStepperBar can't mirror, dock it right and reverse item order manually |
| Machine-stub translations read poorly | mark stubbed keys with a tracked TODO list per language |

## 9. Out of scope

New languages beyond the existing 8; translation of sample projects; builder full-string
coverage beyond the tracked list.

## 10. Sub-task execution order

1. **7.A.1** Resx parity + parity test + missing `Error_*` keys. Verify: test green.
2. **7.A.2** String routing in wizard pages/shell + audit script. Verify: audit = 0.
3. **7.B.1** Language combo + ReloadStrings + persistence. Verify: live switch works.
4. **7.B.2** RTL wiring + ar visual pass. Verify: mirrored screenshots.
5. **7.C.1** Accessibility: Beep-control names, call-site coverage, tab order, glyphs. Verify: Narrator + Insights pass.
