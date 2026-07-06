# Phase 03 — Builder Simplification

**Goal:** `InstallerBuilder`, `Publisher`, `MsixPackager`, and the ClickOnce helpers all read from
the flat `InstallProject`. The triple-write block and the `FirstExistingPath` fallbacks disappear
because there's no longer a triple or a fallback chain to manage.

**Why third:** the model and serializer are the read/write contract. Once those are flat, the
builder is a pure consumer of the flat model.

---

## Scope

### 3.1 `InstallerBuilder.BuildRuntimeProject` simplified

Today (lines 318–402 of `InstallerBuilder.cs`) this method does a triple-write:

```csharp
// today — three writes for the same value
runtime.Build.BannerImagePath = "banner.png";
runtime.Branding.WelcomeBannerPath = "banner.png";
runtime.InstallConfig.BannerImagePath = "banner.png";
// ... and a similar triple for the icon
```

After phase 1+3 it becomes:

```csharp
private InstallProject BuildRuntimeProject(InstallProject project, string payloadDir)
{
    var runtime = project.DeepClone();
    runtime.SchemaVersion = InstallProject.CurrentSchemaVersion;
    runtime.Prefer64Bit = !string.Equals(project.ArchitecturesAllowed, "x86",
                                          StringComparison.OrdinalIgnoreCase);

    // The ONLY rewrite needed: rebase every file's source path
    // relative to the payload dir so the runtime can resolve it.
    var sourceDir = string.IsNullOrWhiteSpace(project.SourceDirectory) ? null : project.SourceDirectory;
    foreach (var comp in runtime.Components)
    {
        if (comp.Files == null) continue;
        foreach (var file in comp.Files)
        {
            if (string.IsNullOrWhiteSpace(file.SourcePath)) continue;
            var normalized = file.SourcePath.Replace('/', '\\').Trim();
            if (!Path.IsPathRooted(normalized))
            {
                file.SourcePath = normalized.Replace('\\', '/');
                continue;
            }
            var abs = Path.GetFullPath(normalized);
            string key = (sourceDir != null && abs.StartsWith(sourceDir, StringComparison.OrdinalIgnoreCase))
                ? Path.GetRelativePath(sourceDir, abs)
                : Path.GetFileName(abs);
            file.SourcePath = key.Replace('\\', '/');
        }
    }

    // Asset paths become the sidecar names we ship alongside the exe.
    if (!string.IsNullOrWhiteSpace(runtime.SetupIconFile))   runtime.SetupIconFile   = "setup.ico";
    if (!string.IsNullOrWhiteSpace(runtime.WizardImageFile)) runtime.WizardImageFile = "banner.png";

    // If the project points at an external EULA file, inline its content for the runtime.
    if (!string.IsNullOrWhiteSpace(project.LicenseFile) && File.Exists(project.LicenseFile))
    {
        try { runtime.LicenseText = File.ReadAllText(project.LicenseFile); }
        catch { /* leave LicenseText as-is */ }
    }

    return runtime;
}
```

### 3.2 `CopyBrandingAssets` no longer needs fallback

Today (line 776–802) it uses `FirstExistingPath(Build.BannerImagePath, Branding.WelcomeBannerPath,
InstallConfig.BannerImagePath)`. With the flat model, it reads `project.WizardImageFile` and
`project.SetupIconFile` once each:

```csharp
private static void CopyBrandingAssets(InstallProject project, string outputDir, BuildResult result)
{
    if (!string.IsNullOrWhiteSpace(project.WizardImageFile) && File.Exists(project.WizardImageFile))
    {
        try
        {
            var dest = Path.Combine(outputDir, "banner.png");
            if (!string.Equals(Path.GetFullPath(project.WizardImageFile),
                               Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                File.Copy(project.WizardImageFile, dest, overwrite: true);
        }
        catch (Exception ex) { result.Warnings.Add($"Could not copy banner: {ex.Message}"); }
    }

    if (!string.IsNullOrWhiteSpace(project.SetupIconFile) && File.Exists(project.SetupIconFile))
    {
        try
        {
            var dest = Path.Combine(outputDir, "setup.ico");
            File.Copy(project.SetupIconFile, dest, overwrite: true);
        }
        catch (Exception ex) { result.Warnings.Add($"Could not copy icon: {ex.Message}"); }
    }
}
```

`FirstExistingPath` is removed entirely.

### 3.3 `GenerateUninstallRegistryEntries` reads flat props

```csharp
private static void GenerateUninstallRegistryEntries(InstallProject project)
{
    var baseKey = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{project.AppName}";
    var entries = new List<RegistryOperation>
    {
        new() { KeyPath = baseKey, ValueName = "DisplayName",    Value = project.AppName,     ValueKind = RegistryValueKind.String },
        new() { KeyPath = baseKey, ValueName = "DisplayVersion", Value = project.AppVersion,  ValueKind = RegistryValueKind.String },
        new() { KeyPath = baseKey, ValueName = "Publisher",      Value = project.AppPublisher,ValueKind = RegistryValueKind.String },
        new() { KeyPath = baseKey, ValueName = "InstallLocation",Value = "%InstallPath%",     ValueKind = RegistryValueKind.String },
        new() { KeyPath = baseKey, ValueName = "UninstallString",
                Value = $"\"{Path.Combine("%InstallPath%", project.OutputBaseFilename + ".exe")}\" /UNINSTALL",
                ValueKind = RegistryValueKind.ExpandString },
        new() { KeyPath = baseKey, ValueName = "QuietUninstallString",
                Value = $"\"{Path.Combine("%InstallPath%", project.OutputBaseFilename + ".exe")}\" /UNINSTALL /S",
                ValueKind = RegistryValueKind.ExpandString },
    };
    if (!string.IsNullOrWhiteSpace(project.AppSupportURL))
        entries.Add(new() { KeyPath = baseKey, ValueName = "HelpLink", Value = project.AppSupportURL, ValueKind = RegistryValueKind.String });
    foreach (var e in entries) project.RegistryEntries.Add(e);
}
```

### 3.4 `WriteRuntimeScript` no longer references deleted types

```csharp
private void WriteRuntimeScript(InstallProject project, string outputDir, string payloadDir, BuildResult result)
{
    if (project.CreateUninstallEntry)
        GenerateUninstallRegistryEntries(project);

    var runtimeProject = BuildRuntimeProject(project, payloadDir);
    File.WriteAllText(Path.Combine(outputDir, "script.bsetup"),
                      InstallerScriptSerializer.Write(runtimeProject));

    var versionInfo = $@"{project.AppName}
Version {project.AppVersion}
{project.AppPublisher}
Built {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
Beep Installer v{System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0"}
";
    File.WriteAllText(Path.Combine(outputDir, "version.txt"), versionInfo);
}
```

The shipping artifact (`script.bsetup` written into the output) is now an exact representation of
`runtimeProject` — no field-by-field rewrites against a "real" model.

### 3.5 `ValidateProject` reads flat props

```csharp
private static void ValidateProject(InstallProject project, BuildResult result)
{
    if (string.IsNullOrWhiteSpace(project.AppName))
        result.Errors.Add("AppName is required.");
    if (string.IsNullOrWhiteSpace(project.AppVersion))
        result.Errors.Add("AppVersion is required.");
    if (project.Components == null || project.Components.Count == 0)
        result.Warnings.Add("No components defined — the installer will not copy any files.");
    if (string.IsNullOrWhiteSpace(project.SourceDirectory))
        result.Warnings.Add("No source directory set — the payload will be empty.");
    else if (!Directory.Exists(project.SourceDirectory))
        result.Warnings.Add($"Source directory does not exist: {project.SourceDirectory}");
}
```

### 3.6 `ResolveOutputDirectory` reads flat props

```csharp
private static string ResolveOutputDirectory(InstallProject project)
{
    if (!string.IsNullOrWhiteSpace(project.OutputDir)) return project.OutputDir;
    var product = SafeFileName(project.AppName);
    var version = SafeFileName(project.AppVersion);
    return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "BeepInstaller", "Builds", $"{product}-{version}");
}
```

### 3.7 `StagePayload` reads flat props

```csharp
private string StagePayload(InstallProject project, BuildResult result)
{
    var outputDir = ResolveOutputDirectory(project);
    var payloadDir = Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(Path.Combine(outputDir,
                                    project.OutputBaseFilename + ".exe"))) ?? "",
        project.PayloadFolderName);
    // ... same body, with project.SourceDirectory / SourceIncludes / SourceExcludes
}
```

### 3.8 `CopyRuntime` reads flat props

```csharp
private void CopyRuntime(InstallProject project, string outputDir, BuildResult result)
{
    if (project.SelfContained) CopySelfContainedRuntime(project, outputDir, result);
    else CopyFrameworkDependentRuntime(project, outputDir, result);

    result.OutputFile = Path.Combine(outputDir, project.OutputBaseFilename + ".exe");
    if (File.Exists(result.OutputFile))
        result.OutputSizeBytes = new FileInfo(result.OutputFile).Length;
    Report(result, 80, $"Runtime copied: {Path.GetFileName(result.OutputFile)}");
}

private void CopySelfContainedRuntime(InstallProject project, string outputDir, BuildResult result)
{
    var rid = project.ArchitecturesAllowed switch
    {
        "x86" => "win-x86",
        "arm64" => "win-arm64",
        _ => "win-x64"
    };
    // ... uses project.OutputBaseFilename + ".exe"
}
```

### 3.9 MSIX packaging reads flat props

```csharp
private void PackageMsix(InstallProject project, string outputDir, BuildResult result)
{
    var fmt = (project.OutputFormat ?? "exe").Trim().ToLowerInvariant();
    if (fmt is not ("msix" or "msixbundle")) return;

    var identity = string.IsNullOrWhiteSpace(project.MsixIdentity) ? project.AppName : project.MsixIdentity;
    var publisher = string.IsNullOrWhiteSpace(project.MsixPublisher)
        ? ("CN=" + (project.AppPublisher ?? "Publisher"))
        : project.MsixPublisher;
    var msix = MsixPackager.Package(project.SourceDirectory, outputDir, identity, publisher,
                                    project.AppName, project.AppVersion,
                                    project.MainExecutable,
                                    architecture: project.ArchitecturesAllowed);
    foreach (var w in msix.Warnings) result.Warnings.Add(w);
    if (!msix.Success) result.Errors.Add("MSIX: " + (msix.Error ?? "unknown"));
    result.MsixPackagePath = msix.MsixPackagePath;
}
```

### 3.10 `CompressPayload` reads flat props

```csharp
private void CompressPayload(InstallProject project, string stagedDir, string outputDir, BuildResult result)
{
    var numeric = Math.Clamp(project.CompressionLevel, 0, 9);
    var level = numeric switch
    {
        0 => CompressionLevel.NoCompression,
        <= 3 => CompressionLevel.Fastest,
        <= 6 => CompressionLevel.Optimal,
        _ => CompressionLevel.SmallestSize
    };
    var method = (project.Compression ?? "zip").Trim();
    // ... uses project.PayloadFolderName
}
```

### 3.11 `CodeSign` reads flat props

```csharp
private void CodeSign(InstallProject project, BuildResult result)
{
    // ... uses project.CodeSignCertificatePath, project.CodeSignCertificatePassword,
    //     project.CodeSignTimestampUrl
}
```

### 3.12 `Publisher.cs` reads flat props

`Beep.Installer/Engine/Publisher.cs` has scattered references to `project.InstallConfig.ProductName`,
`project.InstallConfig.ProductVersion`, `project.InstallConfig.Publisher`, `project.InstallConfig.UpdateUrl`.
Replace with `project.AppName`, `project.AppVersion`, `project.AppPublisher`, `project.AppUpdatesURL`.

### 3.13 ClickOnce helpers read flat props

The `Engine/ClickOnce/` helpers (`ClickOnceRuntime`, `PublishStager`, `UpdateChecker`,
`UpdateApplier`, `TrustChecker`, `RollbackManager`, `Shortcut`, `ApplicationManifestWriter`,
`DeploymentManifestWriter`) take parameters by name today (`string productName`, `string version`,
`string? publisher`). The caller sites (in `Publisher.cs` and elsewhere) change to pass
`project.AppName`, `project.AppVersion`, `project.AppPublisher`. The helpers' signatures stay
unchanged — they already take string inputs.

`TestHelpers.UseTestDefaults()` previously operated on `BuildOptions`; it operates on
`project.PayloadFolderName` etc. via flat property setters. The helpers' shape changes:

```csharp
// before
public static BuildOptions UseTestDefaults(this BuildOptions options)
{
    options.SelfContained = false;
    options.SingleFile = false;
    return options;
}

// after
public static InstallProject UseTestDefaults(this InstallProject project)
{
    project.SelfContained = false;
    project.SingleFile = false;
    return project;
}
```

### 3.14 `DependencyScanner.cs` and `ProjectScanApplier.cs` read flat props

`DependencyScanner.ScanDirectory(dir)` and `ProjectScanApplier.Apply(...)` populate the project
based on the source directory. With the flat model, they write directly to `project.Components`,
`project.MainExecutable`, `project.Prerequisites`, etc. No reference to `InstallConfig`, `Branding`,
or `BuildOptions`.

### 3.15 `InstallerProjectFactory.CreateNew()` flat factory

```csharp
public static class InstallerProjectFactory
{
    public static InstallProject CreateNew(
        string appName = "MyApplication",
        string appVersion = "1.0.0",
        string appPublisher = "Publisher",
        string sourceDirectory = "")
    {
        var name = string.IsNullOrWhiteSpace(appName) ? "MyApplication" : appName.Trim();
        var ver  = string.IsNullOrWhiteSpace(appVersion) ? "1.0.0" : appVersion.Trim();
        var pub  = string.IsNullOrWhiteSpace(appPublisher) ? "Publisher" : appPublisher.Trim();

        return new InstallProject
        {
            ProjectName = name,
            AppName = name,
            AppVersion = ver,
            AppPublisher = pub,
            SourceDirectory = sourceDirectory ?? "",
            DefaultDirName = $"%ProgramFiles%\\{name}",
            DefaultGroupName = name,
            DefaultInstallType = InstallationType.Typical,
            PrivilegesRequired = true,
            WindowTitle = $"{name} Setup",
            WelcomeTitle = $"Welcome to {name} Setup",
            DefaultTheme = "Modern",
            ShowEula = true,
            AllowComponentSelection = true,
            AllowPathChange = true,
            OutputBaseFilename = "Setup",
            PayloadFolderName = "payload",
            CompressionLevel = 6,
            ArchitecturesAllowed = "x64compatible",
            ArchitecturesInstallIn64BitMode = "x64compatible",
            DefaultScope = "Machine",
            AllowScopeSelection = true,
            CreateUninstallEntry = true,
            CreateRestorePoint = true,
        };
    }
}
```

### 3.16 `ProjectTemplates.Create` flat

```csharp
public static InstallProject Create(string templateId, string productName, string version,
                                    string publisher, string sourceDirectory)
{
    var p = InstallerProjectFactory.CreateNew(productName, version, publisher, sourceDirectory);
    switch ((templateId ?? EmptyId).ToLowerInvariant())
    {
        case ConsoleId:
            AddCoreComponent(p, "Console Application");
            AddStartMenuShortcut(p, "consoleapp.exe");
            break;
        case WinFormsId:
        case WpfId:
            AddCoreComponent(p, "Main Application");
            var exe = templateId == WpfId ? "app.dll" : "app.exe";
            AddDesktopShortcut(p, exe);
            AddStartMenuShortcut(p, exe);
            break;
        case ServiceId:
            AddCoreComponent(p, "Service Host");
            AddStartupShortcut(p, "servicehost.exe");
            break;
    }
    return p;
}
```

---

## Files

### REWRITTEN

- `Beep.Installer/Engine/InstallerBuilder.cs` — flat reads, no triple-writes.
- `Beep.Installer/Engine/InstallerProjectFactory.cs` — flat factory.
- `Beep.Installer/Engine/ProjectTemplates.cs` — flat templates.
- `Beep.Installer/Engine/Publisher.cs` — flat inputs.
- `Beep.Installer/Engine/MsixPackager.cs` — flat callers (no signature change).
- `Beep.Installer/Engine/DependencyScanner.cs` — flat writes.
- `Beep.Installer/Engine/ProjectScanApplier.cs` — flat writes.
- `Beep.Installer.Tests/TestHelpers.cs` — `UseTestDefaults` operates on `InstallProject`.

### UPDATED (small)

- `Engine/ClickOnce/*.cs` — caller sites pass `project.AppName` etc.
- `Engine/RollbackManager.cs` — same.

---

## Order of execution

1. Update `InstallerProjectFactory.CreateNew` signature + body to flat shape.
2. Update `ProjectTemplates.Create` to call the flat factory.
3. Update every reference in `InstallerBuilder.cs` (`BuildRuntimeProject`, `WriteRuntimeScript`,
   `CopyBrandingAssets`, `GenerateUninstallRegistryEntries`, `ValidateProject`,
   `ResolveOutputDirectory`, `StagePayload`, `CopyRuntime`, `CopySelfContainedRuntime`,
   `CopyFrameworkDependentRuntime`, `CompressPayload`, `CodeSign`, the MSIX branch) to flat props.
4. Update `Publisher.cs`, `MsixPackager.cs`, `ClickOnce/*` callers.
5. Update `DependencyScanner.cs` and `ProjectScanApplier.cs` to write flat props.
6. Update `TestHelpers.UseTestDefaults` to operate on `InstallProject`.
7. `dotnet build` — expect failures in `PackageBuilderForm` (the UI still reads the old nested
   shape). Those are fixed in phase 5.

---

## Acceptance

| # | Check |
|---|---|
| 1 | `grep -rn "project\.Build\.\|project\.Branding\.\|project\.InstallConfig\." Beep.Installer/Engine` returns 0. |
| 2 | `FirstExistingPath` is deleted (its callers no longer have multi-source fields). |
| 3 | `InstallerBuilder.BuildRuntimeProject` is ≤ 50 lines (was ~80). |
| 4 | `dotnet test Beep.Installer.Tests` — all non-UI tests pass. |