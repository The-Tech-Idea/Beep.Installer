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
        => New(ProjectTemplates.EmptyId, name, version, publisher, sourceDir);

    public void New(string templateId, string name, string version, string publisher, string sourceDir)
    {
        if (_project != null)
            _project.PropertyChanged -= OnProjectPropertyChanged;
        _project = ProjectTemplates.Create(templateId, name, version, publisher, sourceDir);
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
        var snapshot = ProjectAuthoringWorkspace.CreateSnapshot(
            _project,
            new ProjectSchemaValidationOptions { Strict = true });
        var result = new BuildPipeline.BuildResult();
        foreach (var diagnostic in snapshot.Diagnostics)
        {
            var message = $"{diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}";
            if (diagnostic.Severity == ProjectSchemaDiagnosticSeverity.Error)
                result.Errors.Add(message);
            else
                result.Warnings.Add(message);
        }
        return result;
    }

    public ProjectAuthoringSnapshot CreateAuthoringSnapshot()
        => ProjectAuthoringWorkspace.CreateSnapshot(_project, new ProjectSchemaValidationOptions { Strict = true });

    public ProjectValidationCenterReport CreateValidationCenterReport()
        => ProjectValidationCenter.Create(_project, new ProjectSchemaValidationOptions { Strict = true });

    public PackageFormatCapabilityReport CreatePackageFormatCapabilityReport()
        => PackageFormatCapabilityReporter.Create(_project);

    public ProjectTemplateUpdatePreview PreviewTemplateUpdate(string templateId)
        => ProjectAuthoringWorkspace.PreviewTemplateUpdate(_project, templateId);

    public ProjectTemplateUpdatePreview ApplyTemplateUpdate(string templateId)
    {
        var preview = PreviewTemplateUpdate(templateId);
        var updated = ProjectAuthoringWorkspace.CreateTemplateCandidate(_project, templateId);
        updated.MarkDirty();
        ReplaceProject(updated);
        ProjectReloaded?.Invoke(this, EventArgs.Empty);
        return preview;
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

    /// <summary>Serialises one autosave at a time; a slow write must not overlap the next tick.</summary>
    private int _autoSaveInFlight;

    private void StartAutoSave()
    {
        _autoSaveTimer = new System.Threading.Timer(_ => WriteAutoSaveSnapshot(),
            null, (int)AutoSave.Interval.TotalMilliseconds, (int)AutoSave.Interval.TotalMilliseconds);
    }

    /// <summary>
    /// Writes the crash-recovery snapshot. Internal so the race can actually be tested.
    ///
    /// Three things this has to get right, none of which it used to:
    ///
    /// The timer runs on a thread-pool thread while the user edits on the UI thread, and the
    /// serializer walks the project's ObservableCollections. Doing that against the live object
    /// throws "collection was modified" mid-write, or worse writes a torn snapshot that looks
    /// valid. The text is now produced under the same lock the editing path takes.
    ///
    /// It called <c>Save</c>, which stamps <c>ModifiedAt</c> on the project — through
    /// <c>SetProperty</c>, which sets <c>IsDirty</c>. Autosaving therefore dirtied the very project
    /// it was snapshotting, so a saved document reported unsaved changes 30 seconds later and
    /// prompted on close. It now serialises without touching the project at all.
    ///
    /// The write went straight to the autosave path, so a crash during it left a truncated file
    /// that <c>IsRecoveryAvailable</c> would happily offer as recovered work. It is now atomic.
    /// </summary>
    internal void WriteAutoSaveSnapshot()
    {
        if (!_project.IsDirty) return;

        // A tick that arrives while the previous one is still writing is dropped, not queued: the
        // next tick captures newer state anyway.
        if (Interlocked.Exchange(ref _autoSaveInFlight, 1) == 1) return;
        try
        {
            string text;
            lock (_autoSaveLock)
                text = InstallerScriptSerializer.Write(_project);

            var path = AutoSave.AutoSavePath(_filePath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            AtomicFileWriter.WriteAllText(path, text);
        }
        catch (Exception ex)
        {
            // Autosave exists so a crash does not lose the user's work. Failing silently
            // means they believe they are covered when they are not — record it so the
            // failure is at least discoverable.
            Diag.Warn("AutoSave", "periodic autosave failed — crash recovery is unavailable", ex, "BI2640");
        }
        finally
        {
            Interlocked.Exchange(ref _autoSaveInFlight, 0);
        }
    }

    /// <summary>
    /// Taken while the snapshot is serialised and by any edit that restructures the project, so a
    /// snapshot never walks a collection mid-mutation.
    /// </summary>
    internal object AutoSaveLock => _autoSaveLock;

    private readonly object _autoSaveLock = new();

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
