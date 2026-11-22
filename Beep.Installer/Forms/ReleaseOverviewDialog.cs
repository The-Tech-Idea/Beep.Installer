using Beep.Installer.Engine;
using Beep.Installer.Lang;
using Beep.Installer.Quality;
using Beep.Installer.Ui;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

internal sealed class ReleaseOverviewDialog : Form
{
    private readonly Func<ReleaseQualificationPortfolioReport?, IEnumerable<string>, ReleaseOverviewSnapshot> _capture;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Label _notice = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label _error = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = InstallerTheme.Danger };
    private readonly Button _assets = new() { AutoSize = true, Name = "ReleaseSelectAssets" };
    private readonly Button _report = new() { AutoSize = true, Name = "ReleaseSelectReport" };
    private readonly Button _refresh = new() { AutoSize = true };
    private readonly Button _close = new() { AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly string[] _keys = { "Release_Package", "Guided_Signing", "Release_Assets", "Release_Qualifications" };
    private readonly Font _body = InstallerTheme.Body;
    private readonly TextBox[] _views;
    private ReleaseQualificationPortfolioReport? _portfolio;
    private string? _portfolioPath;
    private string[] _selectedAssets = Array.Empty<string>();
    private ReleasePortfolioLoadError _loadError;
    private long _loadGeneration;

    internal ReleaseOverviewDialog(Func<ReleaseQualificationPortfolioReport?, IEnumerable<string>, ReleaseOverviewSnapshot> capture)
    {
        _capture = capture;
        Size = new(900, 660); MinimumSize = new(700, 480); StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize));
        _views = Enumerable.Range(0, 4).Select(index => new TextBox { Name = "ReleaseView" + index, ReadOnly = true,
            Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Font = _body }).ToArray();
        for (var index = 0; index < _views.Length; index++)
        { var page = new TabPage(); page.Controls.Add(_views[index]); _tabs.TabPages.Add(page); }
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        actions.Controls.AddRange(new Control[] { _assets, _report, _refresh, _close });
        foreach (Button button in actions.Controls) button.MinimumSize = new(0, InstallerTheme.ActionMinHeight);
        layout.Controls.Add(_notice, 0, 0); layout.Controls.Add(_error, 0, 1); layout.Controls.Add(_tabs, 0, 2); layout.Controls.Add(actions, 0, 3);
        Controls.Add(layout); CancelButton = _close;
        _assets.Click += (_, _) => { using var picker = new OpenFileDialog { Multiselect = true, Title = _assets.Text }; if (picker.ShowDialog(this) == DialogResult.OK) SelectDeploymentFiles(picker.FileNames); };
        _report.Click += async (_, _) =>
        {
            using var picker = new OpenFileDialog { Filter = "JSON (*.json)|*.json", Title = _report.Text };
            if (picker.ShowDialog(this) == DialogResult.OK) await LoadPortfolioAsync(picker.FileName);
        };
        _refresh.Click += (_, _) => RefreshOverview();
        LanguageManager.LanguageChanged += OnLanguageChanged;
        SizeChanged += (_, _) => _notice.MaximumSize = new(Math.Max(1, ClientSize.Width - 40), 0);
        RefreshOverview(); Engine.Accessibility.ApplyAutoNames(this);
    }

    internal void SelectDeploymentFiles(IEnumerable<string> paths)
    {
        _selectedAssets = paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        RefreshOverview();
    }

    internal async Task<bool> LoadPortfolioAsync(string path)
    {
        var generation = ++_loadGeneration; _report.Enabled = false;
        var loaded = await Task.Run(() => ReleasePortfolioReader.Read(path));
        if (IsDisposed || Disposing || generation != _loadGeneration) return false;
        _report.Enabled = true; _loadError = loaded.Error;
        if (loaded.Report is not null) { _portfolio = loaded.Report; _portfolioPath = Path.GetFullPath(path); }
        RefreshOverview(); return loaded.Report is not null;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => OnLanguageChanged(sender, e)); return; }
        RefreshOverview();
    }

    internal void RefreshOverview()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(RefreshOverview); }
            catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated) { }
            return;
        }
        var snapshot = _capture(_portfolio, _selectedAssets);
        Text = L("Release_Title", "Release overview");
        _notice.Text = L("Release_Notice", "Generated assets do not prove deployment succeeded.")
            + (snapshot.IsCurrentProject ? "" : "\r\n" + L("Workspace_OlderRevision", "This build uses an earlier project revision. Its result is not attached to the current project."));
        _error.Text = _loadError == ReleasePortfolioLoadError.None ? "" : L("Release_ReportInvalid", "The qualification report could not be loaded. Choose a readable portfolio JSON file under 4 MB.");
        for (var index = 0; index < _keys.Length; index++) _tabs.TabPages[index].Text = _views[index].AccessibleName = L(_keys[index], _keys[index]);
        string Available(bool exists) => exists ? L("Release_FilePresent", "File present") : L("Release_Unavailable", "File unavailable");
        string Result(bool success) => success ? L("Release_ReportedPassed", "Reported passed") : L("Release_ReportedFailed", "Reported failed");
        string Completed(DateTimeOffset? value) => value is { } date && date != default ? date.ToString("u", System.Globalization.CultureInfo.InvariantCulture) : "";
        string Files(IEnumerable<ReleaseFile> files) => string.Join("\r\n\r\n", files.Select(file => Available(file.Exists) + "\r\n" + file.Path));
        var empty = L("Release_NoEvidence", "No evidence selected or recorded.");
        var state = snapshot.BuildState switch
        {
            ReleaseBuildState.Building => L("Workspace_Building", "Building"),
            ReleaseBuildState.Completed => L("Workspace_Completed", "Build completed"),
            ReleaseBuildState.Failed => L("Workspace_Failed", "Build failed"),
            ReleaseBuildState.Cancelled => L("Workspace_Cancelled", "Build cancelled"),
            _ => L("Workspace_Idle", "Ready to build")
        };
        _views[0].Text = $"{snapshot.ProjectName} • {snapshot.Version} • {snapshot.Format}\r\n{state}\r\n\r\n" + (snapshot.PackageFiles.Count == 0 ? empty : Files(snapshot.PackageFiles));
        _views[1].Text = snapshot.Signing.Count == 0 ? empty : string.Join("\r\n\r\n", snapshot.Signing.Select(item =>
            $"{item.Kind} • {Result(item.ReportedSuccess)} • {Available(item.ArtifactExists)}\r\n{item.ArtifactPath}\r\n{item.CertificateSubject}\r\nSHA-256: {item.FileDigestSha256}"));
        if (snapshot.SigningConfigured.HasValue) _views[1].Text = (snapshot.SigningConfigured.Value
            ? L("Guided_Configured", "Configured") : L("Guided_Unsigned", "Unsigned development build")) + "\r\n\r\n" + _views[1].Text;
        _views[2].Text = snapshot.SelectedDeploymentFiles.Count == 0 ? empty : Files(snapshot.SelectedDeploymentFiles);
        _views[3].Text = L("Release_Unbound", "Selected qualification reports are not associated with this build.") + "\r\n" + _portfolioPath + "\r\n\r\n"
            + (snapshot.QualificationReportedSuccess.HasValue ? Result(snapshot.QualificationReportedSuccess.Value) + " • " + Completed(snapshot.QualificationCompletedUtc) + "\r\n\r\n" : "")
            + (snapshot.Qualifications.Count == 0 ? empty : string.Join("\r\n\r\n", snapshot.Qualifications.Select(item =>
                $"{item.Id} • {(item.State == ReleaseQualificationState.Missing ? Available(false) : Result(item.State == ReleaseQualificationState.ReportedPassed))}\r\n{Completed(item.CompletedUtc)}\r\n{Available(item.ReportExists)} • {item.ReportPath}")));
        _assets.Text = _assets.AccessibleName = L("Release_LoadAssets", "Select deployment assets");
        _report.Text = _report.AccessibleName = L("Release_LoadReport", "Load qualification portfolio");
        _refresh.Text = L("Release_Refresh", "Refresh"); _close.Text = L("Common_Close", "Close");
        RtlHelper.ApplyDirection(this, RtlHelper.IsRtl(LanguageManager.CurrentCulture.TwoLetterISOLanguageName));
        _tabs.RightToLeftLayout = RightToLeft == RightToLeft.Yes;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { ++_loadGeneration; LanguageManager.LanguageChanged -= OnLanguageChanged; }
        base.Dispose(disposing);
        if (disposing) _body.Dispose();
    }
}
