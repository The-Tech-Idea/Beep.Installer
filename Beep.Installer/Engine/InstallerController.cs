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
    private readonly AuthoringHistory _history = new();
    private int _authoringChangeDepth;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

    /// <summary>Commit a field-edit burst before starting the next authoring action.</summary>
    public void CommitAuthoringChanges(string label = "Edit")
    {
        lock (_autoSaveLock)
        {
            if (_authoringChangeDepth != 0) return;
            if (_history.Record(_project, label)) NotifyHistoryChanged();
        }
    }

    public void RunAuthoringChange(string label, Action action)
    {
        lock (_autoSaveLock)
        {
            if (_authoringChangeDepth == 0) CommitAuthoringChanges();
            _authoringChangeDepth++;
            try { action(); }
            finally
            {
                _authoringChangeDepth--;
                if (_authoringChangeDepth == 0) CommitAuthoringChanges(label);
            }
        }
    }

    public AuthoringHistoryRestore? Undo() => RestoreHistory(false);
    public AuthoringHistoryRestore? Redo() => RestoreHistory(true);

    private AuthoringHistoryRestore? RestoreHistory(bool redo)
    {
        lock (_autoSaveLock)
        {
            CommitAuthoringChanges();
            var restored = redo ? _history.Redo(_project) : _history.Undo(_project);
            if (restored is null) return null;
            _project.PropertyChanged -= OnProjectPropertyChanged;
            _project = restored.Project;
            _project.PropertyChanged += OnProjectPropertyChanged;
            OnPropertyChanged(nameof(Project));
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(SaveState));
            NotifyHistoryChanged();
            ProjectReloaded?.Invoke(this, EventArgs.Empty);
            return restored;
        }
    }

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void ResetHistory()
    {
        _history.Reset(_project);
        NotifyHistoryChanged();
    }
    private string? _filePath;
    private string _untitledRecoveryPath = NewUntitledRecoveryPath();
    private string? _sessionRecoveryPath;
    private string? _recoveredSourcePath;
    private string? _recoveredSourceFingerprint;
    private readonly System.Collections.Generic.Dictionary<string, string?> _pendingRecoveryCleanup = new(StringComparer.OrdinalIgnoreCase);
    private static string NewUntitledRecoveryPath(string? documentPath = null) => Path.Combine(AutoSave.RecoveryDirectory,
        Guid.NewGuid().ToString("N") + (documentPath is null ? "" : ".named") + ".autosave.bsetup");
    private string RecoveryPath => _sessionRecoveryPath ?? (_filePath is null ? _untitledRecoveryPath : AutoSave.AutoSavePath(_filePath));
    private System.Threading.Timer? _autoSaveTimer;

    public InstallProject Project => _project;
    public bool IsDirty => _project.IsDirty;
    public bool HasFilePath => !string.IsNullOrEmpty(_filePath);
    public string? FilePath => _filePath;
    private bool _isRecovered;
    private DateTime? _lastSavedAt, _lastAutoSavedAt;
    private string? _saveError, _autoSaveError, _recoveryError, _cleanupError;
    public AuthoringSaveState SaveState
    {
        get { lock (_autoSaveLock) return new(_filePath, RecoveryPath, IsDirty,
            _isRecovered, _lastSavedAt, _lastAutoSavedAt, _saveError, _autoSaveError, _recoveryError, _cleanupError); }
    }

    private void ResetSaveState()
    {
        _isRecovered = false;
        _sessionRecoveryPath = null;
        _recoveredSourcePath = null;
        _recoveredSourceFingerprint = null;
        _lastSavedAt = _lastAutoSavedAt = null;
        _saveError = _autoSaveError = _recoveryError = _cleanupError = null;
    }

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
            // This is the empty shell placeholder, not an authored New transaction.
            _project.MarkClean();
        }
        _project.PropertyChanged += OnProjectPropertyChanged;
        _history.Reset(_project);
        StartAutoSave();
    }

    public void ReplaceProject(InstallProject project)
    {
        lock (_autoSaveLock)
        {
            CommitAuthoringChanges();
            _history.AdoptReplacement(_project, project);
            if (_project != null)
                _project.PropertyChanged -= OnProjectPropertyChanged;
            _project = project;
            _project.PropertyChanged += OnProjectPropertyChanged;
            OnPropertyChanged(nameof(Project));
            OnPropertyChanged(nameof(IsDirty));
            CommitAuthoringChanges("Replace project");
        }
    }

    // ── Lifecycle ──

    public void New(string name, string version, string publisher, string sourceDir)
        => New(ProjectTemplates.EmptyId, name, version, publisher, sourceDir);

    public void New(string templateId, string name, string version, string publisher, string sourceDir)
        => New(new ProjectTemplate { Id = templateId }, name, version, publisher, sourceDir);

    public void New(ProjectTemplate template, string name, string version, string publisher, string sourceDir)
    {
        var created = ProjectTemplates.Create(template, name, version, publisher, sourceDir);
        lock (_autoSaveLock)
        {
            if (_project != null)
                _project.PropertyChanged -= OnProjectPropertyChanged;
            _project = created;
            _project.PropertyChanged += OnProjectPropertyChanged;
            _filePath = null;
            _untitledRecoveryPath = NewUntitledRecoveryPath();
            ResetSaveState();
            ResetHistory();
            OnPropertyChanged(nameof(Project));
            OnPropertyChanged(nameof(IsDirty));
            ProjectReloaded?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(SaveState));
        }
    }

    public (bool ok, string? error) Open(string path, bool recoverDraft = true)
    {
        lock (_autoSaveLock)
        {
            var (project, error) = InstallerScriptSerializer.Load(path);
            if (project == null)
                return (false, error);
            ResetSaveState();

            // Script-relative paths resolve against the script, not the builder's working
            // directory. In memory only — saving must not bake absolute paths into the script.
            InstallerScriptSerializer.ResolveRelativePaths(project, path);
            project.MarkClean();
            _lastSavedAt = File.GetLastWriteTimeUtc(path);

            if (recoverDraft && AutoSave.IsRecoveryAvailable(path))
            {
                var recovered = AutoSave.AutoSavePath(path);
                if (recovered != null && File.Exists(recovered))
                {
                    var (recoveryProject, recoveryError) = InstallerScriptSerializer.Load(recovered);
                    if (recoveryProject != null)
                    {
                        InstallerScriptSerializer.ResolveRelativePaths(recoveryProject, path);
                        recoveryProject.MarkDirty();
                        project = recoveryProject;
                        _isRecovered = true;
                        _lastAutoSavedAt = File.GetLastWriteTimeUtc(recovered);
                    }
                    else _recoveryError = recoveryError;
                }
            }

            if (_project != null)
                _project.PropertyChanged -= OnProjectPropertyChanged;
            _project = project;
            _project.PropertyChanged += OnProjectPropertyChanged;
            _filePath = path;
            ResetHistory();
            RecentProjects.Record(path);
            OnPropertyChanged(nameof(Project));
            OnPropertyChanged(nameof(IsDirty));
            ProjectReloaded?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(SaveState));
            return (true, null);
        }
    }

    public (bool ok, string? error) RecoverUntitled(string recoveryPath)
        => RecoverDraft(new RecoveryDraftEntry(recoveryPath));

    public (bool ok, string? error) RecoverDraft(RecoveryDraftEntry entry)
    {
        lock (_autoSaveLock)
        {
            if (entry.DiscoveryError is not null) return (false, entry.DiscoveryError);
            var recoveryPath = entry.RecoveryPath;
            var sourceFingerprint = AutoSave.RecoveryFingerprint(recoveryPath);
            var (project, error) = InstallerScriptSerializer.Load(recoveryPath);
            if (project is null) return (false, error);
            try { if (entry.DocumentPath is not null) InstallerScriptSerializer.ResolveRelativePaths(project, entry.DocumentPath); }
            catch (Exception ex) { return (false, ex.Message); }
            var sessionPath = NewUntitledRecoveryPath(entry.DocumentPath);
            try
            {
                Directory.CreateDirectory(AutoSave.RecoveryDirectory);
                AutoSave.RecordDocumentIdentity(sessionPath, entry.DocumentPath);
                AtomicFileWriter.WriteAllText(sessionPath, InstallerScriptSerializer.Write(project));
            }
            catch (Exception ex) { AutoSave.ClearRecoveryFile(sessionPath); return (false, ex.Message); }
            _project.PropertyChanged -= OnProjectPropertyChanged;
            _project = project;
            _project.PropertyChanged += OnProjectPropertyChanged;
            _filePath = entry.DocumentPath;
            _untitledRecoveryPath = sessionPath;
            ResetSaveState();
            _sessionRecoveryPath = sessionPath;
            if (entry.DocumentPath is not null && File.Exists(entry.DocumentPath)) _lastSavedAt = File.GetLastWriteTimeUtc(entry.DocumentPath);
            _recoveredSourcePath = recoveryPath;
            _recoveredSourceFingerprint = sourceFingerprint;
            _isRecovered = true;
            _lastAutoSavedAt = File.GetLastWriteTimeUtc(recoveryPath);
            _project.MarkDirty();
            ResetHistory();
            OnPropertyChanged(nameof(Project));
            OnPropertyChanged(nameof(IsDirty));
            ProjectReloaded?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(SaveState));
            return (true, null);
        }
    }

    public (bool ok, string? error) Save()
    {
        lock (_autoSaveLock)
        {
            if (_filePath == null)
            {
                _saveError = "No file path. Use SaveAs.";
                OnPropertyChanged(nameof(SaveState));
                return (false, _saveError);
            }
            return SaveTo(_filePath);
        }
    }

    private (bool ok, string? error) SaveTo(string path)
    {
        CommitAuthoringChanges();
        var previousRecoveryPath = RecoveryPath;
        var result = InstallerScriptSerializer.Save(_project, path);
        _saveError = result.error;
        if (result.ok)
        {
            _filePath = path;
            _sessionRecoveryPath = null;
            _lastSavedAt = DateTime.UtcNow;
            _isRecovered = false;
            _recoveryError = _autoSaveError = null;
            _project.MarkClean();
            _pendingRecoveryCleanup[previousRecoveryPath] = AutoSave.RecoveryFingerprint(previousRecoveryPath);
            if (_recoveredSourcePath is not null) _pendingRecoveryCleanup[_recoveredSourcePath] = _recoveredSourceFingerprint;
            _recoveredSourcePath = null;
            _recoveredSourceFingerprint = null;
            _cleanupError = null;
            foreach (var pending in new System.Collections.Generic.Dictionary<string, string?>(_pendingRecoveryCleanup))
            {
                var recoveryFile = pending.Key;
                var cleanupError = AutoSave.ClearRecoveryFile(recoveryFile, pending.Value, verifyFingerprint: true);
                if (cleanupError is null) _pendingRecoveryCleanup.Remove(recoveryFile);
                else _cleanupError = (_cleanupError is null ? "" : _cleanupError + Environment.NewLine) + recoveryFile + ": " + cleanupError;
            }
            OnPropertyChanged(nameof(IsDirty));
        }
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(SaveState));
        return result;
    }

    public (bool ok, string? error) SaveAs(string path)
    {
        lock (_autoSaveLock)
        {
            var result = SaveTo(EnsureScriptExtension(path));
            if (result.ok)
                RecentProjects.Record(_filePath!);
            return result;
        }
    }

    // ── Build ──

    /// <param name="cancellationToken">
    /// Threaded through to the pipeline. Without it a build started from the builder could not be
    /// stopped: the progress dialog has had a Cancel button all along, but nothing ever handed it a
    /// source, so pressing it showed "Cancelling…" and the build ran to completion anyway. The CLI
    /// has always passed one.
    /// </param>
    public BuildPipeline.BuildResult Build(bool clean = false, CancellationToken cancellationToken = default)
        => BuildCapturedProject(CaptureBuildProject(), clean, cancellationToken);

    /// <summary>Capture on the UI thread before a build is dispatched. No references to editable resources are shared.</summary>
    internal InstallProject CaptureBuildProject()
    {
        lock (_autoSaveLock)
        {
            var (snapshot, error) = InstallerScriptSerializer.Read(InstallerScriptSerializer.Write(_project), _project.ProjectName);
            return snapshot ?? throw new InvalidOperationException(error ?? "The project could not be captured for building.");
        }
    }

    internal BuildPipeline.BuildResult BuildCapturedProject(InstallProject snapshot, bool clean = false, CancellationToken cancellationToken = default,
        IProgress<BuildPipeline.BuildProgress>? progress = null)
    {
        // SINGLE source of truth: delegate to BuildPipeline
        var pipeline = new BuildPipeline
        {
            Progress = new DirectProgress<BuildPipeline.BuildProgress>(p =>
            {
                progress?.Report(p);
                BuildProgressChanged?.Invoke(this, p);
            }),
            CancellationToken = cancellationToken
        };
        var result = pipeline.Run(snapshot, clean);
        var finalProgress = new BuildPipeline.BuildProgress(
            100,
            result.Success ? "Build complete."
                : result.Canceled ? "Build canceled."
                : "Build failed.", result.Canceled ? BuildPipeline.BuildPhase.Canceled : result.Success ? BuildPipeline.BuildPhase.Completed : BuildPipeline.BuildPhase.Failed);
        progress?.Report(finalProgress);
        BuildProgressChanged?.Invoke(this, finalProgress);
        return result;
    }

    private sealed class DirectProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
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
    public ProjectTemplateUpdatePreview PreviewTemplateUpdate(ProjectTemplate template)
        => ProjectAuthoringWorkspace.PreviewTemplateUpdate(_project, template);

    public ProjectTemplateUpdatePreview ApplyTemplateUpdate(string templateId)
        => ApplyTemplateUpdate(new ProjectTemplate { Id = templateId });

    public ProjectTemplateUpdatePreview ApplyTemplateUpdate(ProjectTemplate template)
    {
        CommitAuthoringChanges();
        var preview = PreviewTemplateUpdate(template);
        var updated = ProjectAuthoringWorkspace.CreateTemplateCandidate(_project, template);
        updated.MarkDirty();
        ReplaceProject(updated);
        ProjectReloaded?.Invoke(this, EventArgs.Empty);
        return preview;
    }

    // ── Scan ──

    internal ResourceRecipePreview ApplyResourceRecipe(ResourceRecipeRequest request)
    {
        lock (_autoSaveLock)
        {
            ResourceRecipePreview preview = null!;
            RunAuthoringChange("Resource recipe", () => preview = ResourceRecipeCatalog.Apply(_project, request));
            if (preview.CanApply) ProjectMutated?.Invoke(this, EventArgs.Empty);
            return preview;
        }
    }

    private long _sourceScanGeneration;

    internal AuthoringSourceScan CaptureSourceScan(string directory)
    {
        lock (_autoSaveLock)
        {
            CommitAuthoringChanges();
            return AuthoringSourceScan.Capture(_project, directory, ++_sourceScanGeneration);
        }
    }

    internal bool IsSourceScanCurrent(AuthoringSourceScan scan)
    {
        lock (_autoSaveLock) return scan.Generation == _sourceScanGeneration && scan.Matches(_project);
    }

    internal bool TryApplySourceScan(AuthoringSourceScan scan)
    {
        lock (_autoSaveLock)
        {
            if (!scan.IsCompleted || !IsSourceScanCurrent(scan)) return false;
            RunAuthoringChange("Scan source", () => scan.Apply(_project));
            ++_sourceScanGeneration; // A completed request can only be applied once.
            ProjectMutated?.Invoke(this, EventArgs.Empty);
            return true;
        }
    }

    public SourceScanner.ScanResult Scan(string? sourceDirectory = null)
    {
        var scan = CaptureSourceScan(sourceDirectory ?? _project.SourceDirectory);
        var result = scan.Run();
        if (!TryApplySourceScan(scan)) throw new InvalidOperationException("The project changed during source scanning. Rescan to update its files.");
        return result;
    }

    public string? FindBestBuildOutput()
    {
        return SourceScanner.FindBestBuildOutput(_project.SourceDirectory);
    }

    // ── Publish ──

    public PublishResult Publish(string outputDir, string? updateUrl = null, bool sign = true)
    {
        var publisher = new ClickOncePublisher
        {
            Progress = new Progress<(int, string)>(p =>
                BuildProgressChanged?.Invoke(this, new BuildPipeline.BuildProgress(p.Item1, p.Item2)))
        };
        return publisher.Publish(_project, outputDir,
            updateUrl ?? _project.AppUpdatesURL, sign);
    }

    // ── Preview ──

    internal BuildPipeline.BuildResult PublishCapturedProject(InstallProject snapshot, string outputDir,
        string updateUrl, bool sign, IProgress<BuildPipeline.BuildProgress> progress)
    {
        var publisher = new ClickOncePublisher
        {
            BuildProgress = progress
        };
        var published = publisher.Publish(snapshot, outputDir, updateUrl, sign);
        var result = new BuildPipeline.BuildResult { Success = published.Success, ArtifactKind = "clickonce",
            OutputFile = published.DeploymentManifest, FileCount = published.DeployFileCount, Elapsed = published.Elapsed };
        result.Warnings.AddRange(published.Warnings);
        result.Errors.AddRange(published.Errors);
        if (result.Success && !File.Exists(result.OutputFile))
        {
            result.Success = false;
            result.Errors.Add("The publisher did not produce a deployment manifest.");
        }
        if (File.Exists(result.OutputFile)) result.OutputSizeBytes = new FileInfo(result.OutputFile).Length;
        return result;
    }

    public void ShowPreview(IWin32Window owner)
    {
        using var f = new WizardPreviewForm(_project);
        f.ShowDialog(owner);
    }

    public string PreviewScript() => InstallerScriptSerializer.Write(_project);

    // ── Helpers ──

    public bool ConfirmDiscardChanges(IWin32Window owner, Func<bool>? saveAction = null, bool hasPendingChanges = false)
    {
        if (!IsDirty && !hasPendingChanges) return true;
        var ans = MessageBox.Show(owner,
            Beep.Installer.Lang.LanguageManager.GetOrDefault("SaveState_SaveFirst", "You have unsaved changes. Save first?"),
            "Beep Installer", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (ans == DialogResult.Cancel) return false;
        if (ans == DialogResult.Yes)
        {
            if (saveAction is not null) return saveAction();
            if (!HasFilePath)
            {
                using var dialog = new SaveFileDialog { Filter = InstallerScriptSerializer.FileFilter,
                    FileName = _project.OutputBaseFilename + ".bsetup" };
                if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
                var result = SaveAs(dialog.FileName);
                if (!result.ok) { MessageBox.Show(owner, result.error, "Save", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
            }
            else
            {
                var result = Save();
                if (!result.ok) { MessageBox.Show(owner, result.error, "Save", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
            }
        }
        return true;
    }

    private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(SaveState));
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
        lock (_autoSaveLock)
        {
            try
            {
                if (!_project.IsDirty) return;
                var text = InstallerScriptSerializer.Write(_project);
                var path = RecoveryPath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                AtomicFileWriter.WriteAllText(path, text);
                _lastAutoSavedAt = DateTime.UtcNow;
                _autoSaveError = null;
                OnPropertyChanged(nameof(SaveState));
            }
            catch (Exception ex)
            {
                Diag.Warn("AutoSave", "periodic autosave failed — crash recovery is unavailable", ex, "BI2640");
                _autoSaveError = ex.Message;
                OnPropertyChanged(nameof(SaveState));
            }
            finally
            {
                Interlocked.Exchange(ref _autoSaveInFlight, 0);
            }
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
