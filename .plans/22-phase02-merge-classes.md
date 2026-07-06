# Phase 2 — Merge Duplicate Function Classes

## 2a. SourceScanner

**New file:** `Engine/SourceScanner.cs`

**Deletes:** `Engine/DependencyScanner.cs`, `Engine/ProjectScanApplier.cs`

### Merged contents
- `DependencyScanner.ScanResult` class → `SourceScanner.ScanResult`
- `DependencyScanner.ScanDirectory()` → private, called internally
- `DependencyScanner.FindBuildOutput()` → moved
- `DependencyScanner.FindDependencies()` → private
- All private helpers (`ClassifyFile`, `FindDotNetDependencies`, `FindNativeAndSiblingDlls`, `DetectDotNetPrerequisite`, `BuildFileIndex`, `ReadAssemblyReferences`, `ReadDepsJson`, `MakeDotNetPrerequisite`, `TfmToVersion`)
- `ProjectScanApplier.Apply()` → private, called internally
- `ProjectScanApplier.FindOrCreateComponent()` → private
- `ProjectScanApplier.AddScannedFile()` → private
- `ProjectScanApplyOptions` → `SourceScannerOptions`
- `ProjectScanApplyResult` → part of result

### Public API
```csharp
public class SourceScanner
{
    public class ScanResult { ... }
    public class Options { string ComponentId, bool AddSuggestedPrerequisites, bool DetectMainExecutable }

    public ScanResult ScanAndApply(InstallProject project, Options? options = null);
    public ScanResult ScanAndApply(InstallProject project, string sourceDirectory, Options? options = null);

    public static string? FindBuildOutput(string projectDir); // static helper
}
```

`ScanAndApply()`:
1. Runs `ScanDirectory()` internally
2. Applies results to `project.Components`, `project.Prerequisites`, `project.MainExecutable` in-place
3. Returns combined result

---

## 2b. CustomActionManager

**New file:** `Engine/CustomActionManager.cs`

**Deletes:** `Engine/CustomActionLoader.cs`, `Engine/CustomActionValidator.cs`

### Public API
```csharp
public class CustomActionManager
{
    public class Issue { ... }
    public enum IssueSeverity { Warning, Error }

    public List<CustomAction> Load(); // reads RuntimeProjectContext.Current
    public IReadOnlyList<Issue> Validate(IList<CustomAction> actions);
    public bool IsPublishable(IList<CustomAction> actions);
}
```

---

## 2c. CustomPageManager

**New file:** `Engine/CustomPageManager.cs`

**Deletes:** `Engine/CustomPageLoader.cs`, `Engine/CustomFieldCollector.cs`

### Public API
```csharp
public class CustomPageManager
{
    public List<CustomWizardPage> Load(); // reads RuntimeProjectContext.Current
    public (bool ok, string error) Validate(IEnumerable<CustomField> fields, IReadOnlyDictionary<string, string> values);
    public Dictionary<string, string> Collect(IEnumerable<CustomField> fields, IReadOnlyDictionary<string, string> values);
    public string ExpandMacros(string text, IReadOnlyDictionary<string, string> customValues);
}
```

---

## Impact on other files
- `InstallerController.Scan()` creates `SourceScanner` and calls `ScanAndApply()`
- `PackageBuilderForm` calls `controller.Scan()` instead of `DependencyScanner.ScanDirectory()` + `ProjectScanApplier.Apply()`
- `CustomActionsDialog` imports `CustomActionManager` instead of both `CustomActionLoader` and `CustomActionValidator`
- `CustomPage` wizard page imports `CustomPageManager`
- `InstallerBuilder` imports `SourceScanner` instead of `DependencyScanner`
- Tests updated accordingly
