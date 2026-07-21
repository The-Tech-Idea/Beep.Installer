using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Beep.Installer.Forms;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

public class InstallerController : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ProjectReloaded;
    public event EventHandler<BuildPipeline.BuildProgress>? BuildProgressChanged;
    public event EventHandler? ProjectMutated;

    private InstallProject _project;
    private string? _filePath;
    private System.Threading.Timer? _autoSaveTimer;

    public InstallProject Project => _project;
    public bool IsDirty => _project.IsDirty;
    public bool HasFilePath => !string.IsNullOrEmpty(_filePath);
    public string? FilePath => _filePath;

    public string ProductName
    {
        get
        {
            var name = _project.ProjectName ?? _project.AppName;
            return string.IsNullOrEmpty(_currentFile) ? name : Path.GetFileNameWithoutExtension(_filePath ?? _project.OutputBaseFilename);
        }
    }

    public InstallerController() : this(null) { }

    public InstallerController(InstallProject? project)
    {
        if (project != null)
        {
            _project = project;
        }
        else
        {
            _project = InstallerProjectFactory.CreateNew("MyApplication", "1.0.0", "Publisher", "");
        }
        _project.PropertyChanged += OnProjectPropertyChanged;
        StartAutoSave();
    }

    public void ReplaceProject(InstallProject project)
    {
        if (_project != null)
            _project.PropertyChanged -= OnProjectPropertyChanged;
        _project = project;
        _project.PropertyChanged += OnProjectPropertyChanged;
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(IsDirty));
    }

    // ── Lifecycle ──

    public void New(string name, string version, string publisher, string sourceDir)
    {
        if (_project != null)
            _project.PropertyChanged -= OnProjectPropertyChanged;
        _project = InstallerProjectFactory.CreateNew(name, version, publisher, sourceDir);
        _project.PropertyChanged += OnProjectPropertyChanged;
        _filePath = null;
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(IsDirty));
        ProjectReloaded?.Invoke(this, EventArgs.Empty);
    }

    public (bool ok, string? error) Open(string path)
    {
        var (project, error) = InstallerScriptSerializer.Load(path);
        if (project == null)
            return (false, error);

        // Script-relative paths resolve against the script, not the builder's working
        // directory. In memory only — saving must not bake absolute paths into the script.
        InstallerScriptSerializer.ResolveRelativePaths(project, path);

        if (AutoSave.IsRecoveryAvailable(path))
        {
            var recovered = AutoSave.AutoSavePath(path);
            if (recovered != null && File.Exists(recovered))
            {
                var (recoveryProject, _) = InstallerScriptSerializer.Load(recovered);
                if (recoveryProject != null)
                    project = recoveryProject;
            }
        }

        if (_project != null)
            _project.PropertyChanged -= OnProjectPropertyChanged;
        _project = project;
        _project.PropertyChanged += OnProjectPropertyChanged;
        _filePath = path;
        RecentProjects.Record(path);
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(IsDirty));
        ProjectReloaded?.Invoke(this, EventArgs.Empty);
        return (true, null);
    }

    public (bool ok, string? error) Save()
    {
        if (_filePath == null)
            return (false, "No file path. Use SaveAs.");
        var result = InstallerScriptSerializer.Save(_project, _filePath);
        if (result.ok)
        {
            _project.MarkClean();
            AutoSave.Clear(_filePath);
            OnPropertyChanged(nameof(IsDirty));
        }
        return result;
    }

    public (bool ok, string? error) SaveAs(string path)
    {
        _filePath = EnsureScriptExtension(path);
        var result = Save();
        if (result.ok)
            RecentProjects.Record(_filePath!);
        return result;
    }

    // ── Build ──

    public BuildPipeline.BuildResult Build(bool clean = false)
    {
        // SINGLE source of truth: delegate to BuildPipeline
        var pipeline = new BuildPipeline
        {
            Progress = new Progress<BuildPipeline.BuildProgress>(p => BuildProgressChanged?.Invoke(this, p))
        };
        var result = pipeline.Run(_project, clean);
        BuildProgressChanged?.Invoke(this, new BuildPipeline.BuildProgress(100, result.Success ? "Build complete." : "Build failed."));
        return result;
    }

    public BuildPipeline.BuildResult Validate()
    {
        var result = new BuildPipeline.BuildResult();
        if (string.IsNullOrWhiteSpace(_project.AppName))
            result.Errors.Add("Product name is required.");
        if (string.IsNullOrWhiteSpace(_project.AppVersion))
            result.Errors.Add("Product version is required.");
        if (_project.Components == null || _project.Components.Count == 0)
            result.Warnings.Add("No components defined.");
        return result;
    }

    // ── Scan ──

    public SourceScanner.ScanResult Scan(string? sourceDirectory = null)
    {
        var scanner = new SourceScanner();
        var dir = sourceDirectory ?? _project.SourceDirectory;
        var result = scanner.ScanAndApply(_project, dir);
        ProjectMutated?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public string? FindBestBuildOutput()
    {
        return SourceScanner.FindBestBuildOutput(_project.SourceDirectory);
    }

    // ── Publish ──

    public PublishResult Publish(string outputDir, string? updateUrl = null, bool sign = true)
    {
        var publisher = new Publisher
        {
            Progress = new Progress<(int, string)>(p =>
                BuildProgressChanged?.Invoke(this, new BuildPipeline.BuildProgress(p.Item1, p.Item2)))
        };
        return publisher.Publish(_project, outputDir,
            updateUrl ?? _project.AppUpdatesURL, sign);
    }

    // ── Preview ──

    public void ShowPreview(IWin32Window owner)
    {
        using var f = new WizardPreviewForm(_project);
        f.ShowDialog(owner);
    }

    public string PreviewScript() => InstallerScriptSerializer.Write(_project);

    // ── Helpers ──

    public bool ConfirmDiscardChanges(IWin32Window owner)
    {
        if (!IsDirty) return true;
        var ans = MessageBox.Show(owner,
            "You have unsaved changes. Save first?",
            "Beep Installer", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (ans == DialogResult.Cancel) return false;
        if (ans == DialogResult.Yes) Save();
        return true;
    }

    private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Project));
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── Auto-save ──

    private void StartAutoSave()
    {
        _autoSaveTimer = new System.Threading.Timer(_ =>
        {
            if (!_project.IsDirty) return;
            try
            {
                var path = AutoSave.AutoSavePath(_filePath);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                InstallerScriptSerializer.Save(_project, path);
            }
            catch (Exception ex)
            {
                // Autosave exists so a crash does not lose the user's work. Failing silently
                // means they believe they are covered when they are not — record it so the
                // failure is at least discoverable.
                Diag.Warn("AutoSave", "periodic autosave failed — crash recovery is unavailable", ex);
            }
        }, null, (int)AutoSave.Interval.TotalMilliseconds, (int)AutoSave.Interval.TotalMilliseconds);
    }

    public void StopAutoSave()
    {
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;
    }

    private static string EnsureScriptExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(ext)
            ? Path.ChangeExtension(path, InstallerScriptSerializer.FileExtension)
            : path;
    }

    private string _currentFile => _filePath ?? "";
}
