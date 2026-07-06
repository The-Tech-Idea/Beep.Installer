# Master TODO — Beep.Installer Architecture Refactor

**Goal:** One main class (`InstallerController`) that holds one `InstallProject` (INotifyPropertyChanged). All classes get the same reference — no copies, no cloning, no legacy `InstallConfig` bridge.

---

## Phase 1 — InstallProject: INotifyPropertyChanged + Single Source of Truth
- [ ] Add `INotifyPropertyChanged` with `SetProperty<T>()` helper
- [ ] All properties raise `PropertyChanged` via helper
- [ ] `IsDirty` auto-tracks — flips `true` on any change
- [ ] Add `MarkClean()` method (called after Save)
- [ ] Remove InstallConfig comment block
- **File:** `Models/InstallProject.cs`

## Phase 2 — Merge Duplicate Function Classes (one class per function)
- [ ] Create `Engine/SourceScanner.cs` (merge `DependencyScanner` + `ProjectScanApplier`)
- [ ] Create `Engine/CustomActionManager.cs` (merge `CustomActionLoader` + `CustomActionValidator`)
- [ ] Create `Engine/CustomPageManager.cs` (merge `CustomPageLoader` + `CustomFieldCollector` + `CustomMacro`)

## Phase 3 — Remove InstallConfig + Clone Bridge
- [ ] `InstallerBuilder.cs`: delete `ProjectToInstallConfig()`
- [ ] `InstallerBuilder.cs`: delete `ConfigManager.Save()` call
- [ ] `InstallerBuilder.cs`: delete `CloneProject()` + all 18 `Clone*()` methods
- [ ] `InstallerBuilder.cs`: delete `MapDefaultInstallType()`, `MapUpdateMode()`
- [ ] `InstallerBuilder.cs`: rewrite `BuildRuntimeProject()` — work on shared ref, no copy
- [ ] `PayloadPrepareStep.cs`: remove dead `InstallConfig` parameter from 3 helpers
- [ ] `Program.cs`: `MakeSelfTestConfig()` uses `InstallProject`, not `InstallConfig`
- [ ] `Program.cs`: remove `context.Properties["InstallConfig"]` assignment

## Phase 4 — InstallerController (the one main class)
- [ ] Create `Engine/InstallerController.cs`
- [ ] Holds `InstallProject Project` — the ONLY instance
- [ ] Methods: `New()`, `Open()`, `Save()`, `SaveAs()`, `Build()`, `Validate()`, `Scan()`, `Publish()`, `Preview()`
- [ ] Events: `PropertyChanged`, `ProjectReloaded`, `BuildProgressChanged`
- [ ] Dirtiness tracking via `Project.IsDirty`
- [ ] Auto-save and recent projects managed here

## Phase 5 — FieldBinding + PackageBuilderForm Refactor
- [ ] `FieldBinding.cs`: add INotifyPropertyChanged auto-wire overloads (no manual setter delegates)
- [ ] `PackageBuilderForm.cs`: takes `InstallerController`, subscribes to events
- [ ] Remove all manual `Dirty()` call sites
- [ ] Section builders use new FieldBinding auto-wire

## Phase 6 — Program.cs Simplification
- [ ] Create `InstallerController` once
- [ ] Dispatch CLI commands through controller
- [ ] Pass controller to `PackageBuilderForm`

## Phase 7 — Delete Old Files
- [ ] Delete `Engine/DependencyScanner.cs`
- [ ] Delete `Engine/ProjectScanApplier.cs`
- [ ] Delete `Engine/CustomActionLoader.cs`
- [ ] Delete `Engine/CustomActionValidator.cs`
- [ ] Delete `Engine/CustomPageLoader.cs`
- [ ] Delete `Engine/CustomFieldCollector.cs`

## Phase 8 — Test Updates
- [ ] Fix `InstallerBuilderTests.cs` — remove clone method tests
- [ ] Fix `ProjectScanApplierTests.cs` → rename/adapt for `SourceScannerTests`
- [ ] Fix CustomAction tests → `CustomActionManagerTests`
- [ ] Fix CustomPage/FieldCollector tests → `CustomPageManagerTests`
- [ ] Fix any test referencing `InstallConfig`
- [ ] Run full test suite

---

## Architecture Rule (enforced throughout)

```
InstallerController  ←  the ONLY owner of InstallProject
  └── InstallProject ←  ONE instance, shared by reference
       ├── SourceScanner.ScanAndApply(ref)   ──┐
       ├── InstallerBuilder.Build(ref)       ──┤
       ├── Serializer.Write(ref)             ──┤ all get same ref
       ├── CustomActionManager.Validate(ref) ──┤
       ├── CustomPageManager.Load(ref)       ──┤
       ├── PackageBuilderForm                ──┤
       └── RuntimeProjectContext.Current     ──┘
```

**Rule:** No class creates its own `InstallProject`. No class clones `InstallProject`. One object, everywhere.
