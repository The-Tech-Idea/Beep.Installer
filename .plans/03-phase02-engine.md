# Phase 02 — Build Engine

**Target:** `Beep.Installer/Engine/`

---

## `InstallerBuilder` — produces a `Setup.exe`

The build pipeline that takes an `InstallProject` and emits a self-contained `Setup.exe`.

```csharp
public class InstallerBuilder
{
    public IProgress<BuildProgress>? Progress { get; set; }

    public BuildResult Build(InstallProject project) { … }
}
```

### Build result

```csharp
public class BuildResult
{
    public bool Success { get; set; }
    public string OutputFile { get; set; } = "";
    public string PayloadPath { get; set; } = "";
    public long OutputSizeBytes { get; set; }
    public int FileCount { get; set; }
    public long PayloadSizeBytes { get; set; }
    public List<string> Steps { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();
    public TimeSpan Elapsed { get; set; }
    public string Summary { get; }      // pre-formatted multi-line summary
}
```

### Progress event

```csharp
public readonly record struct BuildProgress(int Percent, string Message);
```

---

## Build pipeline

```
ValidateProject
   │  check product name, version, components, source directory
   ▼
ResolveOutputDirectory
   │  if OutputDirectory empty → %USERPROFILE%\Documents\BeepInstaller\Builds\<product>-<version>
   ▼
StagePayload
   │  copy source tree → <output>\<payloadFolderName>\
   │  applying IncludePatterns and ExcludePatterns
   ▼
WriteConfigFiles
   │  install-config.json   (the runtime reads this)
   │  branding.json         (theming)
   │  project.bpkg          (reference; allows re-build)
   ▼
CopyRuntime
   │  copy this exe to <output>\<OutputFileName>
   │  (if IconPath set: copy icon for reference; PE-embed requires signtool)
   ▼
CompressPayload  (optional)
   │  zip <output>\<payloadFolderName>\ → <output>\<payloadFolderName>.zip
   │  delete uncompressed folder
   ▼
CodeSign  (optional, currently a stub)
   │  emit warning to run signtool.exe manually
   ▼
Return BuildResult
```

---

## Project serializer

`ProjectSerializer` loads and saves `.bpkg` files.

```csharp
public static class ProjectSerializer
{
    public const string FileExtension = ".bpkg";
    public const string FileFilter = "Beep Installer Project (*.bpkg)|*.bpkg|All files (*.*)|*.*";

    public static InstallProject CreateNew(string productName, string version, string publisher, string sourceDirectory);
    public static (InstallProject? project, string? error) Load(string path);
    public static (bool ok, string? error) Save(InstallProject project, string path);
    public static string? ReadScalar(string path, string propertyPath);
}
```

JSON options:
- WriteIndented = true
- PropertyNamingPolicy = camelCase
- ReadCommentHandling = Skip
- AllowTrailingCommas = true

---

## Output files

```
Beep.Installer/Engine/
├── InstallerBuilder.cs
├── ProjectSerializer.cs
└── ThemeLoader.cs              (branding.json loader + #RRGGBB color parser)

Beep.Installer.Tests/           (xUnit)
├── ProjectSerializerTests.cs
├── InstallerBuilderTests.cs
└── ThemeLoaderTests.cs
```

**Status:** ✅ Complete (with 19 passing unit tests).

---

## CLI usage

```bash
# Build with default output dir
Beep.Installer.exe /BUILD=project.bpkg

# Build with custom output dir
Beep.Installer.exe /BUILD=project.bpkg /OUT=C:\Releases

# Preview project summary (no build)
Beep.Installer.exe /PREVIEW=project.bpkg
```

The Package Builder's Build button calls `InstallerBuilder` in-process, with `Progress<BuildProgress>` updating a status bar + log tab.
