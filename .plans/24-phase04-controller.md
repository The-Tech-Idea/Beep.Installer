# Phase 4 — InstallerController (the one main class)

**New file:** `Engine/InstallerController.cs`

## Design

```
InstallerController : INotifyPropertyChanged
│
├── _project : InstallProject        ← THE single data instance
│
├── _serializer  : InstallerScriptSerializer
├── _scanner     : SourceScanner
├── _builder     : InstallerBuilder
├── _publisher   : Publisher
├── _autoSave    : AutoSave
├── _recents     : RecentProjects
│
├── Methods (all main functionality)
│   ├── New(name, version, publisher, sourceDir)
│   ├── Open(path) → (bool ok, string? error)
│   ├── Save() → (bool ok, string? error)
│   ├── SaveAs(path) → (bool ok, string? error)
│   ├── Build() → BuildResult
│   ├── Build(clean) → BuildResult
│   ├── Validate() → BuildResult
│   ├── Scan() → SourceScanner.ScanResult
│   ├── Scan(sourceDirectory) → SourceScanner.ScanResult
│   ├── Publish(outputDir, updateUrl) → PublishResult
│   ├── Preview() → string (script text)
│   └── ShowPreview(owner) → show WizardPreviewForm
│
├── Properties
│   ├── Project → InstallProject
│   ├── IsDirty → bool (reads Project.IsDirty)
│   ├── HasFilePath → bool
│   └── FilePath → string?
│
├── Events
│   ├── PropertyChanged (INotifyPropertyChanged)
│   ├── ProjectReloaded (EventArgs)
│   └── BuildProgressChanged (BuildProgress)
│
└── Internal
    ├── _dirtyLabel → string (for UI display)
    ├── _autoSaveTimer → Timer
    └── MarkDirty(), MarkClean()
```

## Implementation outline

```csharp
public class InstallerController : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private InstallProject _project;
    private string? _filePath;
    private readonly Timer _autoSaveTimer;

    public InstallProject Project => _project;
    public bool IsDirty => _project.IsDirty;
    public bool HasFilePath => !string.IsNullOrEmpty(_filePath);
    public string? FilePath => _filePath;

    public InstallerController()
    {
        _project = InstallerProjectFactory.CreateNew("MyApplication", "1.0.0", "Publisher", "");
        _project.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Project));
        _autoSaveTimer = new Timer(_ => AutoSaveIfDirty(), null, AutoSave.Interval, AutoSave.Interval);
    }

    public void New(string name, string version, string publisher, string sourceDir)
    {
        _project = InstallerProjectFactory.CreateNew(name, version, publisher, sourceDir);
        _filePath = null;
        OnPropertyChanged(nameof(Project));
        ProjectReloaded?.Invoke(this, EventArgs.Empty);
    }

    public (bool ok, string? error) Open(string path)
    {
        var (project, error) = InstallerScriptSerializer.Load(path);
        if (project == null) return (false, error);
        _project = project;
        _filePath = path;
        _project.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Project));
        RecentProjects.Record(path);
        OnPropertyChanged(nameof(Project));
        ProjectReloaded?.Invoke(this, EventArgs.Empty);
        return (true, null);
    }

    public (bool ok, string? error) Save()
    {
        if (_filePath == null) return (false, "No file path. Use SaveAs.");
        var result = InstallerScriptSerializer.Save(_project, _filePath);
        if (result.ok) { _project.MarkClean(); AutoSave.Clear(_filePath); OnPropertyChanged(nameof(IsDirty)); }
        return result;
    }

    public (bool ok, string? error) SaveAs(string path)
    {
        _filePath = path;
        var result = Save();
        if (result.ok) RecentProjects.Record(_filePath!);
        return result;
    }

    public BuildResult Build(bool clean = false)
    {
        var builder = new InstallerBuilder { Progress = new Progress<BuildProgress>(p => BuildProgressChanged?.Invoke(this, p)) };
        return builder.Build(_project, clean);
    }

    public BuildResult Validate()
    {
        return new InstallerBuilder().Validate(_project);
    }

    public SourceScanner.ScanResult Scan(string? sourceDirectory = null)
    {
        var scanner = new SourceScanner();
        return scanner.ScanAndApply(_project, sourceDirectory ?? _project.SourceDirectory);
    }

    public void ShowPreview(IWin32Window owner)
    {
        using var f = new WizardPreviewForm(_project);
        f.ShowDialog(owner);
    }

    public string Preview() => InstallerScriptSerializer.Write(_project);

    public event EventHandler? ProjectReloaded;
    public event EventHandler<BuildProgress>? BuildProgressChanged;

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void AutoSaveIfDirty()
    {
        if (!_project.IsDirty) return;
        try
        {
            var path = AutoSave.AutoSavePath(_filePath);
            if (!string.IsNullOrWhiteSpace(Path.GetDirectoryName(path)))
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            InstallerScriptSerializer.Save(_project, path);
        }
        catch { }
    }
}
```

## Impact
- `Program.cs` creates one `InstallerController` and uses it for everything
- `PackageBuilderForm` constructor takes `InstallerController`
- No more direct `new InstallerBuilder()`, `new DependencyScanner()`, etc. in forms
