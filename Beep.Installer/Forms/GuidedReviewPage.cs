using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Ui;
using System.Collections;
using System.ComponentModel;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

internal sealed class GuidedReviewPage : UserControl
{
    private enum CheckState { Pending, Ready, Blocked, Stale, Failed }
    private readonly LatestProjectCheck _checks = new();
    private readonly InstallerController? _controller;
    private readonly Func<InstallProject> _current;
    private readonly Action<string> _edit;
    private readonly Action _build;
    private readonly Action? _recipe, _release, _compare;
    private InstallProject _observed;
    private ProjectReviewSnapshot? _snapshot;
    private CheckState _state = CheckState.Pending;
    private int _generation;
    private bool _started;
    private Button _start = null!;
    private Label _status = null!;
    private TableLayoutPanel _diagnosticRows = null!;
    internal IReadOnlyList<ProjectSchemaDiagnostic> Diagnostics { get; private set; } = Array.Empty<ProjectSchemaDiagnostic>();
    internal Task? PendingCheck { get; private set; }
    internal Func<InstallProject, CancellationToken, Task<IReadOnlyList<ProjectSchemaDiagnostic>>> Evaluate = EvaluateAsync;

    internal GuidedReviewPage(InstallProject project, Action<string> edit, Action build, Action? recipe = null, Action? release = null, Action? compare = null, InstallerController? controller = null)
    {
        _controller = controller; _current = () => controller?.Project ?? project;
        _observed = project; _edit = edit; _build = build; _recipe = recipe; _release = release; _compare = compare;
        Dock = DockStyle.Fill;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScroll = true;
        CaptureAndRender();
        _observed.PropertyChanged += ProjectChanged;
        if (_controller is not null)
        {
            _controller.ProjectReloaded += ProjectChanged;
            _controller.ProjectMutated += ProjectChanged;
        }
    }

    private void Render(InstallProject project)
    {
        var focus = FindForm() is { } owner ? UiFocusBookmark.Capture(owner) : null;
        foreach (Control old in Controls.Cast<Control>().ToArray()) { Controls.Remove(old); old.Dispose(); }
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new(SizeType.AutoSize));
        void Row(string summary, string path)
        {
            var row = layout.RowCount++;
            layout.RowStyles.Add(new(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = summary, AutoSize = true, MaximumSize = new Size(580, 0), Margin = new Padding(0, 8, 12, 8) }, 0, row);
            var link = new Button { Name = "ReviewEdit_" + path, Text = L("Btn_Edit", "Edit"), AutoSize = true, AccessibleName = L("Btn_Edit", "Edit") + ": " + summary, Tag = path, Enabled = _state is not (CheckState.Stale or CheckState.Failed) };
            link.Click += (_, _) => { if (IsCurrent()) _edit(path); else MarkStale(); };
            layout.Controls.Add(link, 1, row);
        }
        Row($"{project.AppName} • {project.AppVersion} • {project.AppPublisher}", "Setup.AppName");
        Row(L("Guided_Files", "Files and components") + $": {project.Components.Count} / {project.Components.Sum(c => c.Files.Count)}", "Components");
        Row(L("Guided_Install", "Install behavior") + $": {project.DefaultScope} • {project.DefaultDirName}", "Setup.DefaultScope");
        Row(L("Guided_SystemChanges", "System changes") + $": {project.Prerequisites.Count} {L("Section_Prerequisites", "prerequisites")}, {project.Shortcuts.Count} {L("Section_Shortcuts", "shortcuts")}, {project.RegistryEntries.Count} {L("Section_Registry", "registry entries")}, {project.Resources.Count + project.WindowsServices.Count + project.ScheduledTasks.Count + project.FirewallRules.Count + project.IisSites.Count + project.IisAppPools.Count + project.ConfigTransforms.Count + project.Certificates.Count + project.ComRegistrations.Count + project.DriverPackages.Count + project.EnvironmentVariables.Count + project.FileAssociations.Count + project.WebDeployPackages.Count + project.Packages.Count} {L("Guided_Resources", "resources")}", "Prerequisites");
        void Group(IList collection, string path, string key, string caption)
        {
            if (collection.Count == 0) return;
            var category = L(key, caption);
            Row(category + ": " + collection.Count, path);
            for (var index = 0; index < Math.Min(collection.Count, 5); index++)
            {
                var item = collection[index];
                if (item is null) continue;
                var properties = TypeDescriptor.GetProperties(item);
                // Review names the operation; credentials, arguments and variable values stay in
                // the editor. Bound the preview so a large payload does not create thousands of rows.
                var name = new[] { "Name", "DisplayName", "Id", "KeyPath", "Path", "Extension", "DestinationPath" }
                    .Select(property => properties[property]?.GetValue(item) as string)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                Row("  " + (name ?? category + " " + (index + 1)), path + "[" + index + "]");
            }
            if (collection.Count > 5)
                Row(string.Format(L("Review_MoreItems", "{0} additional items. Edit to view the full list."), collection.Count - 5), path);
        }
        for (var index = 0; index < Math.Min(project.Components.Count, 5); index++)
            Group(project.Components[index].Files, $"Components[{index}].Files", "Guided_Files", "Files and components");
        Group(project.Prerequisites, "Prerequisites", "Section_Prerequisites", "Prerequisites");
        Group(project.Shortcuts, "Shortcuts", "Section_Shortcuts", "Shortcuts");
        Group(project.RegistryEntries, "RegistryEntries", "Section_Registry", "Registry entries");
        Group(project.WindowsServices, "WindowsServices", "Section_WindowsServices", "Windows Services");
        Group(project.ScheduledTasks, "ScheduledTasks", "Section_ScheduledTasks", "Scheduled Tasks");
        Group(project.FirewallRules, "FirewallRules", "Section_FirewallRules", "Firewall Rules");
        Group(project.Certificates, "Certificates", "Section_Certificates", "Certificates");
        Group(project.ComRegistrations, "ComRegistrations", "Section_ComRegistrations", "COM registrations");
        Group(project.DriverPackages, "DriverPackages", "Section_DriverPackages", "Driver packages");
        Group(project.ConfigTransforms, "ConfigTransforms", "Section_ConfigTransforms", "Configuration transforms");
        Group(project.IisAppPools, "IisAppPools", "Section_IisAppPools", "IIS Application Pools");
        Group(project.IisSites, "IisSites", "Section_IisSites", "IIS Sites");
        Group(project.WebDeployPackages, "WebDeployPackages", "Section_WebDeploy", "Web Deploy packages");
        Group(project.EnvironmentVariables, "EnvironmentVariables", "Section_EnvironmentVariables", "Environment Variables");
        Group(project.FileAssociations, "FileAssociations", "Section_FileAssociations", "File Associations");
        Group(project.Packages, "Packages", "Section_Packages", "Package Nodes");
        Group(project.PrerequisiteCatalogs, "PrerequisiteCatalogs", "Section_PrerequisiteCatalogs", "Prerequisite Catalogs");
        Group(project.Resources, "Resources", "Section_Resources", "Typed Resources");
        Group(project.CustomActions, "CustomActions", "Toolbar_Actions", "Custom actions");
        Row(project.OutputFormat + " • " + BuildPipeline.ResolveOutputDirectory(project), "Setup.OutputFormat");
        Row(L("Guided_Signing", "Signing") + ": " + (project.HasCodeSigningCertificate ? L("Guided_Configured", "Configured") : L("Guided_Unsigned", "Unsigned development build")), "Setup.CodeSignCertificatePath");
        _diagnosticRows = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        _diagnosticRows.ColumnStyles.Add(new(SizeType.Percent, 100));
        _diagnosticRows.ColumnStyles.Add(new(SizeType.AutoSize));
        var diagnosticsRow = layout.RowCount++; layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(_diagnosticRows, 0, diagnosticsRow); layout.SetColumnSpan(_diagnosticRows, 2);
        PopulateDiagnostics();
        var statusKey = _state switch { CheckState.Pending => "Review_Checking", CheckState.Failed => "Review_CheckFailed", CheckState.Stale => "Review_CheckChanged", _ => "Review_CheckComplete" };
        var status = new Label { Name = "GuidedReviewStatus", Text = L(statusKey, "Review check"), AutoSize = true, MaximumSize = new(580, 0), Margin = new(0, 8, 12, 8) };
        _status = status;
        status.AccessibleName = status.Text;
        var statusRow = layout.RowCount++; layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(status, 0, statusRow); layout.SetColumnSpan(status, 2);
        var start = new Button { Name = "GuidedBuild", Text = L("Builder_BuildTitle", "Build"), AutoSize = true,
            Enabled = _state == CheckState.Ready, BackColor = InstallerTheme.Accent, ForeColor = InstallerTheme.AccentText };
        _start = start;
        AuthoringThemeBinding.SetSurface(start, AuthoringSurface.Accent);
        start.Click += (_, _) => { if (_state == CheckState.Ready && IsCurrent()) _build(); else MarkStale(); };
        var last = layout.RowCount++;
        layout.Controls.Add(start, 0, last);
        if (_recipe is not null)
        {
            var recipes = new Button { Name = "GuidedResourceRecipes", Text = L("Recipe_Title", "Resource recipes"), AutoSize = true };
            recipes.Click += (_, _) => _recipe();
            layout.Controls.Add(recipes, 1, last);
        }
        Controls.Add(layout);
        var retry = new Button { Name = "GuidedReviewCheckAgain", Text = L("Review_CheckAgain", "Check again"), AutoSize = true };
        retry.Click += async (_, _) => await RefreshAsync();
        layout.Controls.Add(retry, 0, layout.RowCount++);
        if (_release is not null)
        {
            var overview = new Button { Name = "GuidedReleaseOverview", Text = L("Release_Title", "Release overview"), AutoSize = true };
            overview.Click += (_, _) => _release();
            layout.Controls.Add(overview, 0, layout.RowCount++);
        }
        if (_compare is not null)
        {
            var comparison = new Button { Name = "GuidedProjectComparison", Text = L("Comparison_Title", "Compare projects"), AutoSize = true };
            comparison.Click += (_, _) => _compare(); layout.Controls.Add(comparison, 0, layout.RowCount++);
        }
        Engine.Accessibility.ApplyAutoNames(this);
        if (focus is not null && !string.IsNullOrEmpty(focus.ControlName) && Controls.Find(focus.ControlName, true) is { Length: 1 } matches)
            focus.RestoreReplacement(matches[0]);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_started) return;
        _started = true;
        PendingCheck = CheckAsync(++_generation);
    }

    internal Task RefreshAsync()
    {
        if (IsDisposed || Disposing) return Task.CompletedTask;
        _checks.Invalidate(); var generation = ++_generation;
        _observed.PropertyChanged -= ProjectChanged; _observed = _current(); _observed.PropertyChanged += ProjectChanged;
        _state = CheckState.Pending; Diagnostics = Array.Empty<ProjectSchemaDiagnostic>();
        CaptureAndRender();
        return PendingCheck = CheckAsync(generation);
    }

    internal void RefreshCaptions()
    {
        if (IsDisposed || Disposing) return;
        // UI culture does not change the captured project or its engine diagnostics.
        // Retain readiness and any pending check while refreshing their presentation.
        Render(_snapshot?.Summary ?? new InstallProject());
    }

    private void CaptureAndRender()
    {
        try { _snapshot = ProjectReviewSnapshot.Capture(_current()); Render(_snapshot.Summary); }
        catch (Exception)
        {
            Diag.Warn("GuidedReview", "Could not capture the project for review.", eventId: "BI2440");
            _snapshot = null; _state = CheckState.Failed;
            // No exception text or secret-bearing inputs are rendered in a capture failure.
            Render(new InstallProject());
        }
    }

    private async Task CheckAsync(int generation)
    {
        var snapshot = _snapshot;
        if (snapshot is null || _state != CheckState.Pending) return;
        try
        {
            var diagnostics = await _checks.RunAsync(snapshot.Candidate, Evaluate);
            if (IsDisposed || Disposing || generation != _generation || diagnostics is null) return;
            if (!IsCurrent()) { MarkStale(); return; }
            Diagnostics = diagnostics.ToArray();
            _state = Diagnostics.Any(issue => issue.Severity == ProjectSchemaDiagnosticSeverity.Error) ? CheckState.Blocked : CheckState.Ready;
        }
        catch (Exception)
        {
            if (IsDisposed || Disposing || generation != _generation) return;
            if (!IsCurrent()) { MarkStale(); return; }
            _state = CheckState.Failed; Diagnostics = Array.Empty<ProjectSchemaDiagnostic>();
            Diag.Warn("GuidedReview", "Could not check the captured project.", eventId: "BI2441");
        }
        PopulateDiagnostics(); UpdateState();
    }

    private static Task<IReadOnlyList<ProjectSchemaDiagnostic>> EvaluateAsync(InstallProject project, CancellationToken token)
        => Task.Run<IReadOnlyList<ProjectSchemaDiagnostic>>(() =>
        {
            token.ThrowIfCancellationRequested();
            var snapshot = ProjectAuthoringWorkspace.CreateSnapshot(project);
            token.ThrowIfCancellationRequested();
            return snapshot.Diagnostics.Concat(PackageTargetDiagnostics.Validate(project)).ToArray();
        }, token);

    private bool IsCurrent()
    {
        if (IsDisposed || Disposing || _snapshot is null) return false;
        try { return _snapshot.Matches(_current()); }
        catch (Exception) { return false; }
    }

    private void ProjectChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => ProjectChanged(sender, e)); return; }
        if (!IsCurrent()) MarkStale();
    }

    private void MarkStale()
    {
        if (IsDisposed || Disposing) return;
        _checks.Invalidate(); ++_generation; _state = CheckState.Stale;
        Diagnostics = Array.Empty<ProjectSchemaDiagnostic>();
        PopulateDiagnostics(); UpdateState();
    }

    private void UpdateState()
    {
        var key = _state switch { CheckState.Pending => "Review_Checking", CheckState.Failed => "Review_CheckFailed", CheckState.Stale => "Review_CheckChanged", _ => "Review_CheckComplete" };
        _status.Text = _status.AccessibleName = L(key, "Review check");
        _start.Enabled = _state == CheckState.Ready;
        foreach (var layout in Controls.OfType<TableLayoutPanel>())
            foreach (var link in layout.Controls.OfType<Button>().Where(button => button.Tag is string))
                link.Enabled = _state is not (CheckState.Stale or CheckState.Failed);
    }

    private void PopulateDiagnostics()
    {
        foreach (Control old in _diagnosticRows.Controls.Cast<Control>().ToArray()) { _diagnosticRows.Controls.Remove(old); old.Dispose(); }
        _diagnosticRows.RowStyles.Clear(); _diagnosticRows.RowCount = 0;
        foreach (var issue in Diagnostics)
        {
            var summary = $"{AuthoringReportPresentation.Status(issue.Severity.ToString())} • {issue.Code} • {issue.Path}\n{issue.Message}\n{issue.Fix}";
            var row = _diagnosticRows.RowCount++; _diagnosticRows.RowStyles.Add(new(SizeType.AutoSize));
            _diagnosticRows.Controls.Add(new Label { Text = summary, AutoSize = true, MaximumSize = new(580, 0), Margin = new(0, 8, 12, 8) }, 0, row);
            var link = new Button { Name = "ReviewDiagnostic_" + issue.Code + "_" + issue.Path, Text = L("Btn_Edit", "Edit"), AutoSize = true, AccessibleName = L("Btn_Edit", "Edit") + ": " + summary, Tag = issue.Path };
            link.Click += (_, _) => { if (IsCurrent()) _edit(issue.Path); else MarkStale(); };
            _diagnosticRows.Controls.Add(link, 1, row);
        }
        Engine.Accessibility.ApplyAutoNames(_diagnosticRows);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ++_generation; _checks.Dispose(); _observed.PropertyChanged -= ProjectChanged;
            if (_controller is not null)
            {
                _controller.ProjectReloaded -= ProjectChanged;
                _controller.ProjectMutated -= ProjectChanged;
            }
        }
        base.Dispose(disposing);
    }
}
