# Phase 06 — Tests

**Goal:** All 32 test files pass against the flat model. Three new tests verify the consolidation:
`Migrate_LegacyFile_LoadsIntoUnifiedProject`, `Save_LegacyFile_CreatesBackupBeforeOverwrite`,
`UnifiedModel_HasNoDuplicateFields`.

**Why sixth:** phases 1–5 change every property path in the codebase. Tests catch regressions and
verify the migration path works.

---

## Scope

### 6.1 Property-path renames

A mechanical rewrite across all 32 test files:

| Old path | New path |
|---|---|
| `p.InstallConfig.ProductName` | `p.AppName` |
| `p.InstallConfig.ProductVersion` | `p.AppVersion` |
| `p.InstallConfig.Publisher` | `p.AppPublisher` |
| `p.InstallConfig.DefaultInstallPath` | `p.DefaultDirName` |
| `p.InstallConfig.StartMenuFolder` | `p.DefaultGroupName` |
| `p.InstallConfig.RequireAdminPrivileges` | `p.PrivilegesRequired` |
| `p.InstallConfig.DefaultInstallType` | `p.DefaultInstallType` |
| `p.InstallConfig.LicenseText` | `p.LicenseText` |
| `p.InstallConfig.LicenseFile` | `p.LicenseFile` (via `LicenseFile` field) |
| `p.InstallConfig.SupportUrl` | `p.AppSupportURL` |
| `p.InstallConfig.UpdateUrl` | `p.AppUpdatesURL` |
| `p.InstallConfig.UpdateMode` | `p.AppUpdateMode` |
| `p.InstallConfig.BannerImagePath` | `p.WizardImageFile` |
| `p.InstallConfig.ProductIconPath` | `p.SetupIconFile` |
| `p.InstallConfig.Components` | `p.Components` |
| `p.InstallConfig.Prerequisites` | `p.Prerequisites` |
| `p.InstallConfig.Shortcuts` | `p.Shortcuts` |
| `p.InstallConfig.RegistryEntries` | `p.RegistryEntries` |
| `p.InstallConfig.EnvironmentVariables` | `p.EnvironmentVariables` |
| `p.InstallConfig.Prefer64Bit` | `p.Prefer64Bit` |
| `p.Branding.ProductName` | `p.AppName` |
| `p.Branding.WindowTitle` | `p.WindowTitle` |
| `p.Branding.WelcomeTitle` | `p.WelcomeTitle` |
| `p.Branding.PublisherName` | `p.AppPublisher` |
| `p.Branding.PublisherUrl` | `p.AppPublisherURL` |
| `p.Branding.SupportEmail` | `p.AppSupportEmail` |
| `p.Branding.ProductIconPath` | `p.SetupIconFile` |
| `p.Branding.WelcomeBannerPath` | `p.WizardImageFile` |
| `p.Branding.LicenseFile` | `p.LicenseFile` |
| `p.Branding.SidebarBackgroundColor` | `p.SidebarBackgroundColor` |
| `p.Branding.SidebarTextColor` | `p.SidebarTextColor` |
| `p.Branding.AccentColor` | `p.AccentColor` |
| `p.Branding.ShowEula` | `p.ShowEula` |
| `p.Branding.AllowComponentSelection` | `p.AllowComponentSelection` |
| `p.Branding.AllowPathChange` | `p.AllowPathChange` |
| `p.Branding.DefaultTheme` | `p.DefaultTheme` |
| `p.Build.OutputFileName` | `p.OutputBaseFilename + ".exe"` (full filename) or `p.OutputBaseFilename` (basename) |
| `p.Build.OutputDirectory` | `p.OutputDir` |
| `p.Build.OutputFormat` | `p.OutputFormat` |
| `p.Build.MainExecutable` | `p.MainExecutable` |
| `p.Build.PayloadFolderName` | `p.PayloadFolderName` |
| `p.Build.PayloadSource` | `p.PayloadSource` |
| `p.Build.PayloadUrl` | `p.PayloadUrl` |
| `p.Build.CompressPayload` | `p.CompressPayload` |
| `p.Build.CompressionMethod` | `p.Compression` |
| `p.Build.SolidCompression` | `p.SolidCompression` |
| `p.Build.CompressionLevel` | `p.CompressionLevel` |
| `p.Build.EmbedPayload` | `p.SingleFile` |
| `p.Build.SelfContained` | `p.SelfContained` |
| `p.Build.Architecture` | `p.ArchitecturesAllowed` |
| `p.Build.RegisterUninstallEntry` | `p.CreateUninstallEntry` |
| `p.Build.CreateSystemRestorePoint` | `p.CreateRestorePoint` |
| `p.Build.AllowScopeSelection` | `p.AllowScopeSelection` |
| `p.Build.DefaultScope` | `p.DefaultScope` |
| `p.Build.CodeSignCertificatePath` | `p.CodeSignCertificatePath` |
| `p.Build.CodeSignCertificatePassword` | `p.CodeSignCertificatePassword` |
| `p.Build.CodeSignTimestampUrl` | `p.CodeSignTimestampUrl` |
| `p.Build.MsixIdentity` | `p.MsixIdentity` |
| `p.Build.MsixPublisher` | `p.MsixPublisher` |
| `p.Build.IconPath` | `p.SetupIconFile` |
| `p.Build.BannerImagePath` | `p.WizardImageFile` |
| `p.Build.EulaFilePath` | `p.LicenseFile` |
| `p.IncludePatterns` | `p.SourceIncludes` |
| `p.ExcludePatterns` | `p.SourceExcludes` |

### 6.2 `TestHelpers.UseTestDefaults`

```csharp
public static InstallProject UseTestDefaults(this InstallProject project)
{
    project.SelfContained = false;
    project.SingleFile    = false;
    return project;
}
```

Every test that calls `project.Build.UseTestDefaults()` is rewritten to call
`project.UseTestDefaults()`.

### 6.3 New tests

`Beep.Installer.Tests/UnifiedModelTests.cs`:

```csharp
public class UnifiedModelTests
{
    [Fact]
    public void UnifiedModel_HasNoDuplicateStringPropertyNames()
    {
        // Walk InstallProject and all nested types; collect every string property
        // name; assert no name appears twice.
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in WalkStringProps(typeof(InstallProject)))
        {
            if (seen.TryGetValue(prop.Name, out var existing))
                throw new Exception($"Property '{prop.Name}' appears on both {existing} and {prop.DeclaringType}");
            seen[prop.Name] = prop.DeclaringType.Name;
        }
    }

    [Fact]
    public void InstallProject_HasNoNestedViewObjects()
    {
        // No property on InstallProject should be InstallConfig, InstallerBranding,
        // BuildOptions, or any wrapper that holds these.
        foreach (var prop in typeof(InstallProject).GetProperties())
        {
            Assert.False(prop.Name.Equals("InstallConfig"));
            Assert.False(prop.Name.Equals("Branding"));
            Assert.False(prop.Name.Equals("Build"));
            Assert.False(prop.Name.Equals("BuildOptions"));
        }
    }
}
```

`Beep.Installer.Tests/InstallerScriptSerializerTests.cs` (additions):

```csharp
[Fact]
public void Save_NewProject_WritesOneSetupSection()
{
    var project = InstallerProjectFactory.CreateNew("MyApp", "1.0.0", "ACME", @"C:\src");
    var path = Path.Combine(_tempDir, "new.bsetup");
    InstallerScriptSerializer.Save(project, path);
    var lines = File.ReadAllText(path);
    var setupCount = System.Text.RegularExpressions.Regex.Matches(lines, @"\[Setup\]").Count;
    setupCount.Should().Be(1, "the new format has exactly one [Setup] section");
}

[Fact]
public void Save_LegacyFile_CreatesBackupBeforeOverwrite()
{
    var legacyContent = """
        [Setup]
        AppName=LegacyApp
        [Branding]
        ProductName=LegacyApp
        WelcomeTitle=Welcome to LegacyApp
        [Build]
        OutputFileName=Setup-LegacyApp-1.0.0.exe
        """;
    var path = Path.Combine(_tempDir, "legacy.bsetup");
    File.WriteAllText(path, legacyContent);

    var (loaded, _) = InstallerScriptSerializer.Load(path);
    InstallerScriptSerializer.Save(loaded!, path);

    File.Exists(path + ".bak").Should().BeTrue();
    File.ReadAllText(path + ".bak").Should().Contain("[Branding]");
}

[Fact]
public void Migrate_LegacyFile_LoadsIntoUnifiedProject()
{
    var legacyContent = """
        [Setup]
        AppName=MyApp
        AppVersion=2.0.0
        Publisher=ACME
        [Branding]
        ProductName=MyApp
        WindowTitle=MyApp Setup
        WelcomeTitle=Welcome to MyApp Setup
        WelcomeBannerPath=banner.png
        ProductIconPath=setup.ico
        [Build]
        OutputFileName=Setup-MyApp-2.0.0.exe
        OutputFormat=msix
        CompressionMethod=lzma2
        EmbedPayload=no
        """;
    var path = Path.Combine(_tempDir, "legacy.bsetup");
    File.WriteAllText(path, legacyContent);

    var (loaded, err) = InstallerScriptSerializer.Load(path);
    loaded.Should().NotBeNull();
    loaded!.AppName.Should().Be("MyApp");
    loaded.AppVersion.Should().Be("2.0.0");
    loaded.AppPublisher.Should().Be("ACME");
    loaded.WindowTitle.Should().Be("MyApp Setup");
    loaded.WelcomeTitle.Should().Be("Welcome to MyApp Setup");
    loaded.WizardImageFile.Should().Be("banner.png");
    loaded.SetupIconFile.Should().Be("setup.ico");
    loaded.OutputBaseFilename.Should().Be("Setup-MyApp-2.0.0");
    loaded.OutputFormat.Should().Be("msix");
    loaded.Compression.Should().Be("lzma2");
    loaded.SingleFile.Should().BeFalse();
}
```

`Beep.Installer.Tests/InstallerBuilderTests.cs` (update + additions):

Existing tests are updated to read flat props. New:

```csharp
[Fact]
public void Build_OneSetupSectionInScript()
{
    var project = InstallerProjectFactory.CreateNew("Flat", "1.0.0", "Pub", "")
        .UseTestDefaults();
    project.SourceDirectory = Path.Combine(_tempDir, "src");
    Directory.CreateDirectory(project.SourceDirectory);
    File.WriteAllText(Path.Combine(project.SourceDirectory, "x.txt"), "x");

    var builder = new InstallerBuilder();
    builder.Build(project);

    var script = File.ReadAllText(Path.Combine(project.OutputDir, "script.bsetup"));
    var setupCount = System.Text.RegularExpressions.Regex.Matches(script, @"\[Setup\]").Count;
    setupCount.Should().Be(1);
}
```

### 6.4 Test-by-test plan

Each test file is updated individually. Listed in order of expected breakage:

| Test file | What changes |
|---|---|
| `InstallerScriptSerializerTests.cs` | Property path renames; add `Migrate_LegacyFile_LoadsIntoUnifiedProject` + `Save_LegacyFile_CreatesBackupBeforeOverwrite` + `Save_NewProject_WritesOneSetupSection`. |
| `InstallerBuilderTests.cs` | Property path renames; replace `Build.BannerImagePath`/`Build.IconPath` assertions with flat-prop equivalents. Add `Build_OneSetupSectionInScript`. |
| `EndToEndTests.cs` | Property path renames; `UseTestDefaults()` call updated. |
| `EdgeCaseTests.cs` | Property path renames; `UseTestDefaults()` call updated. |
| `CompressionTests.cs` | Property path renames (`CompressPayload`, `SolidCompression`, `CompressionMethod` → `Compression`). |
| `CustomPageTests.cs` | Property path renames. |
| `GlobMatcherTests.cs` | Property path renames. |
| `InstallScopeTests.cs` | Property path renames. |
| `MsixPackagerTests.cs` | Property path renames. |
| `ProjectTemplatesTests.cs` | Property path renames. |
| `PublishIntegrationTests.cs` | Property path renames. |
| `PublishTests.cs` | Property path renames. |
| `ScriptingAndRollbackTests.cs` | Property path renames. |
| `TrustCheckerTests.cs` | Property path renames. |
| `ClickOnceRuntimeTests.cs` | Caller-side property path renames. |
| `ClickOnceTests.cs` | Caller-side property path renames. |
| `RollbackTests.cs` | Property path renames. |
| `UpdateCheckerTests.cs` | Property path renames. |
| `UpdateApplierTests.cs` | Property path renames. |
| `ComponentSelectionTests.cs` | Property path renames (`InstallConfig.Components` → `Project.Components`). |
| `ConditionListValidatorTests.cs` | Property path renames. |
| `CustomActionValidatorTests.cs` | Property path renames. |
| `SharedFileCountTests.cs` | Property path renames. |
| `ComRegistrationTests.cs` | Property path renames. |
| `EmbeddedPayloadTests.cs` | Property path renames; verify the runtime now reads `install-config.json` (JSON dump of `InstallProject`). |
| `AccessibilityTests.cs` | Unaffected unless it constructs an `InstallProject`. |
| `AutoSaveTests.cs` | Property path renames. |
| `DiagTests.cs` | Likely unaffected. |
| `ProjectScanApplierTests.cs` | Property path renames. |
| `StoreReadinessTests.cs` | Property path renames. |
| `ThemeLoaderTests.cs` | Unaffected (operates on color strings). |
| `TestHelpers.cs` | `UseTestDefaults` operates on `InstallProject` directly. |
| `Beep.Installer.Tests.csproj` | Add `UnifiedModelTests.cs` if not picked up automatically. |

### 6.5 New file: `UnifiedModelTests.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Beep.Installer.Models;
using Xunit;

namespace Beep.Installer.Tests;

public class UnifiedModelTests
{
    [Fact]
    public void UnifiedModel_HasNoDuplicateStringPropertyNames()
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in WalkStringProps(typeof(InstallProject)))
        {
            if (seen.TryGetValue(prop.Name, out var existing))
                throw new Exception($"Property '{prop.Name}' is declared on both {existing} and {prop.DeclaringType?.Name}.");
            seen[prop.Name] = prop.DeclaringType?.Name ?? "?";
        }
    }

    [Fact]
    public void InstallProject_HasNoPeerConfigObjects()
    {
        var forbidden = new[] { "InstallConfig", "InstallerBranding", "BuildOptions",
                                "Build", "Branding", "InstallConfig" };
        foreach (var prop in typeof(InstallProject).GetProperties())
            Assert.False(forbidden.Contains(prop.Name),
                $"InstallProject must not have a nested view-object property '{prop.Name}'.");
    }

    private static IEnumerable<PropertyInfo> WalkStringProps(Type t)
    {
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.PropertyType == typeof(string)) yield return p;
            else if (p.PropertyType.IsClass && p.PropertyType != typeof(string) &&
                     !p.PropertyType.IsPrimitive && !p.PropertyType.IsEnum &&
                     p.PropertyType.Namespace?.StartsWith("Beep.Installer.Models") == true)
                foreach (var nested in WalkStringProps(p.PropertyType))
                    yield return nested;
        }
    }
}
```

---

## Files

### NEW

- `Beep.Installer.Tests/UnifiedModelTests.cs`

### UPDATED (all 32 test files)

- Property path renames + flat reads.
- `TestHelpers.UseTestDefaults` operates on `InstallProject`.
- New tests added to `InstallerScriptSerializerTests.cs` and `InstallerBuilderTests.cs`.

### UNCHANGED

- `Beep.Installer.Tests.csproj` (unless adding new files requires manual `<Compile>` entries — they
  don't because the project uses `<Compile Include="**\*.cs" />`).

---

## Order of execution

1. Rewrite `TestHelpers.cs` — `UseTestDefaults(this InstallProject)`.
2. Run `dotnet build` to see which test files fail first. Use the compiler errors to drive the
   renames.
3. Update each test file mechanically (search/replace using the table in §6.1).
4. Add the three new tests.
5. Run `dotnet test` until all pass.

---

## Acceptance

| # | Check |
|---|---|
| 1 | `dotnet test Beep.Installer.Tests` — 0 failures. |
| 2 | `UnifiedModel_HasNoDuplicateStringPropertyNames` passes. |
| 3 | `InstallProject_HasNoPeerConfigObjects` passes. |
| 4 | `Migrate_LegacyFile_LoadsIntoUnifiedProject` passes. |
| 5 | `Save_LegacyFile_CreatesBackupBeforeOverwrite` passes. |
| 6 | `Save_NewProject_WritesOneSetupSection` passes. |
| 7 | `Build_OneSetupSectionInScript` passes. |