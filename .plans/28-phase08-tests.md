# Phase 8 — Test Updates

## Test project: `Beep.Installer.Tests`

### Tests needing updates

| Test File | Change |
|-----------|--------|
| `InstallerBuilderTests.cs` | Remove tests for Clone methods, ProjectToInstallConfig, MapDefaultInstallType, MapUpdateMode. Add test: `Build_does_not_clone_project()` |
| `ProjectScanApplierTests.cs` → rename to `SourceScannerTests.cs` | All tests use `SourceScanner.ScanAndApply()` instead of `DependencyScanner.ScanDirectory()` + `ProjectScanApplier.Apply()` |
| CustomAction tests (if exist) → `CustomActionManagerTests.cs` | Use `CustomActionManager.Load()` + `.Validate()` |
| CustomPage/FieldCollector tests (if exist) → `CustomPageManagerTests.cs` | Use `CustomPageManager` methods |
| `ProjectTemplatesTests.cs` | Should be fine — `InstallerProjectFactory` is unchanged |
| `InstallerScriptSerializerTests.cs` | Should be fine — serializer is unchanged |
| `GlobMatcherTests.cs` | Unchanged |
| `CompressionTests.cs` | Unchanged |
| `ComponentSelectionTests.cs` | Unchanged (uses BeepDM types) |
| `ConditionListValidatorTests.cs` | Unchanged |
| `EndToEndTests.cs` | May need updates if references old InstallConfig/Clone paths |
| `PublishTests.cs` / `PublishIntegrationTests.cs` | May need updates for controller-based workflow |

### New test: InstallerControllerTests.cs
```csharp
[Fact]
public void New_creates_fresh_project()
{
    var ctl = new InstallerController();
    ctl.New("TestApp", "1.0", "Pub", "");
    Assert.Equal("TestApp", ctl.Project.AppName);
}

[Fact]
public void PropertyChanged_fires_on_edit()
{
    var ctl = new InstallerController();
    var fired = false;
    ctl.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ctl.IsDirty)) fired = true; };
    ctl.Project.AppName = "Changed";
    Assert.True(fired);
    Assert.True(ctl.IsDirty);
}

[Fact]
public void MarkClean_resets_dirty()
{
    var ctl = new InstallerController();
    ctl.Project.AppName = "Changed";
    ctl.Project.MarkClean();
    Assert.False(ctl.IsDirty);
}
```

### New test: SourceScannerTests.cs
```csharp
[Fact]
public void ScanAndApply_populates_components()
{
    var project = InstallerProjectFactory.CreateNew("Test", "1.0", "Pub", testSourceDir);
    var scanner = new SourceScanner();
    var result = scanner.ScanAndApply(project);
    Assert.NotEmpty(project.Components);
    Assert.True(project.Components[0].Files.Count > 0);
}
```

### Run tests
```powershell
dotnet test Beep.Installer.Tests/Beep.Installer.Tests.csproj
```

### Expected failures (known, acceptable)
- Tests that created `InstallConfig` — rewritten to use `InstallProject`
- Tests that called `CloneProject()` or `CloneComponent()` — rewritten or deleted
- Tests that called `DependencyScanner` directly — rewritten to use `SourceScanner`

### Clean lint
```powershell
dotnet format Beep.Installer/Beep.Installer.csproj
dotnet format Beep.Installer.Tests/Beep.Installer.Tests.csproj
```
