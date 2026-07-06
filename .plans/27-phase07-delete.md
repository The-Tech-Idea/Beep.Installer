# Phase 7 — Delete Old Files

## Files to delete

| File | Reason |
|------|--------|
| `Engine/DependencyScanner.cs` | Merged into `SourceScanner.cs` |
| `Engine/ProjectScanApplier.cs` | Merged into `SourceScanner.cs` |
| `Engine/CustomActionLoader.cs` | Merged into `CustomActionManager.cs` |
| `Engine/CustomActionValidator.cs` | Merged into `CustomActionManager.cs` |
| `Engine/CustomPageLoader.cs` | Merged into `CustomPageManager.cs` |
| `Engine/CustomFieldCollector.cs` | Merged into `CustomPageManager.cs` |

## Files that need import updates

After deletion, any file that imported a deleted class must be updated:

### `using` updates needed in:

| File | Old import | New import |
|------|-----------|-----------|
| `PackageBuilderForm.cs` | `DependencyScanner` | `SourceScanner` |
| `InstallerBuilder.cs` | `DependencyScanner` (if used) | None (source dir validation only) |
| `Forms/CustomActionsDialog.cs` | `CustomActionValidator` | `CustomActionManager` |
| `Pages/CustomPage.cs` | `CustomFieldCollector`, `CustomPageLoader` | `CustomPageManager` |
| Any scan-calling code | `DependencyScanner.ScanResult` | `SourceScanner.ScanResult` |

## Tests to delete/rename

| Old test file | Action |
|---------------|--------|
| `ProjectScanApplierTests.cs` | Delete (tests move to `SourceScannerTests.cs`) |
| Any `CustomActionLoaderTests.cs` | Delete or rename to `CustomActionManagerTests.cs` |
| Any `CustomActionValidatorTests.cs` | Delete or rename |
| Any `CustomPageLoaderTests.cs` / `CustomFieldCollectorTests.cs` | Delete or rename |

## Verification
- Build solution — no compile errors from missing files
- `dotnet build Beep.Installer.slnx` — must succeed
- No remaining `using` directives referencing deleted classes
