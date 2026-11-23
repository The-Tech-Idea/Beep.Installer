using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Ui;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Forms;

public class PackageBuilderForm : Form
{
    // Delegated to Ui.InstallerTheme — these were one of three uncoordinated palettes and
    // ignored the active Beep theme entirely. Kept as named members so the ~200 call sites
    // below stay readable.
    private static Color ShellBackColor => Ui.InstallerTheme.Shell;
    private static Color PanelBackColor => Ui.InstallerTheme.Panel;
    private static Color BorderColor => Ui.InstallerTheme.Border;
    private static Color TextColor => Ui.InstallerTheme.Text;
    private static Color MutedTextColor => Ui.InstallerTheme.MutedText;
    private static Color AccentColor => Ui.InstallerTheme.Accent;
    private static Font UiFont => Ui.InstallerTheme.Body;
    private static Font UiFontBold => Ui.InstallerTheme.BodyBold;
    private static Font SectionTitleFont => Ui.InstallerTheme.SectionTitle;

    private readonly InstallerController _controller;
    private readonly AuthoringThemeBinding _themeBinding;
    private readonly string[] _runtimeArgs;
    private InstallProject _project;

    private ToolStrip _toolbar = null!;
    private ToolStripButton _undoButton = null!, _redoButton = null!;
    private ToolStripButton? _releaseButton;
    private ToolStripButton? _comparisonButton;
    private ToolStripDropDownButton _recentsBtn = null!;
    private StatusStrip _status = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripStatusLabel _dirtyLabel = null!;
    private ToolStripStatusLabel _buildResultLabel = null!;
    private BuildPipeline.BuildResult? _lastBuildResult;
    private readonly AuthoringBuildWorkspace _buildWorkspace = new();
    private InstallProject? _buildProjectReference;
    private string? _buildProjectScript;
    private string? _lastBuildProjectName;
    private Guid _recordedBuildId;
    private ToolStripProgressBar _progress = null!;

    private LeftNavPanel _nav = null!;
    private TextBox _navSearchBox = null!;
    private readonly SectionViewRegistry _sectionViews = new();
    private Panel _contentHost = null!;
    private Panel? _activeContent;
    private string? _activeSectionId;

    private Panel? _contentIdentity;
    private Panel? _contentLayout;
    private Panel? _contentEula;
    private Panel? _contentSource;
    private Panel? _contentIncludes;
    private Panel? _contentComponents;
    private Panel? _contentPrerequisites;
    private Panel? _contentShortcuts;
    private Panel? _contentRegistry;
    private Panel? _contentBranding;
    private Panel? _contentWizardPages;
    private Panel? _contentBuild;
    private Panel? _contentScript;
    private Panel? _contentOutput;
    private Panel? _contentPayload;
    private Panel? _contentPackage;
    private Panel? _contentCompression;
    private Panel? _contentCodeSign;
    private Panel? _contentMsix;
    private Panel? _contentLog;
    private Panel? _contentResult;
    private Panel? _contentScheduledTasks;
    private Panel? _contentFirewallRules;
    private Panel? _contentCertificates;
    private Panel? _contentComRegistrations;
    private Panel? _contentDriverPackages;
    private Panel? _contentConfigTransforms;
    private Panel? _contentIisAppPools;
    private Panel? _contentIisSites;
    private Panel? _contentWebDeployPackages;
    private Panel? _contentEnvironmentVariables;
    private Panel? _contentFileAssociations;
    private Panel? _contentWindowsServices;
    private Panel? _contentUpdateChannels;
    private Panel? _contentPackages;
    private Panel? _contentPrerequisiteCatalogs;
    private Panel? _contentMsixOptionalPackages;
    private Panel? _contentSupersedence;
    private Panel? _contentCustomPages;
    private Panel? _contentResources;

    private TextBox _sourceDirBox = null!;
    private TreeView _fileTree = null!;
    private Label _fileStatsLabel = null!;
    private ListBox _includeList = null!, _excludeList = null!;
    private TextBox _includeBox = null!, _excludeBox = null!;
    private DataGridView _componentsGrid = null!;
    private PropertyGrid _componentProps = null!;
    private BindingSource _componentsBinding = null!;
    private CheckedListBox _wizardPagesList = null!;
    private bool _loadingWizardPages;

    /// <summary>
    /// Per-field authoring errors, shown beside the offending box (6.C.2).
    ///
    /// Until now the only way to discover that a project was invalid was to build it and read a
    /// list of messages naming properties -- so the author fixed things in a dialog they had to
    /// leave to find out. The diagnostics already carry a path like <c>Setup.AppVersion</c> and the
    /// bound text boxes are already named after the property they edit, so the two can simply be
    /// matched up.
    /// </summary>
    private readonly ErrorProvider _fieldErrors = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };

    /// <summary>
    /// Coalesces validation while the user is typing. Every keystroke raises PropertyChanged, and
    /// the schema validator walks the whole project; revalidating per character would be wasted
    /// work and would flag a field as invalid halfway through being filled in.
    /// </summary>
    private System.Windows.Forms.Timer? _inlineValidationTimer;
    private TextBox _scriptPreviewBox = null!;
    private TextBox _keyboardWalkthroughBox = null!;
    private TextBox _validationCenterBox = null!;
    private TextBox _buildLogBox = null!;
    private TextBox _formatCapabilityBox = null!;
    private TextBox _licenseTextBox = null!;

    private System.Windows.Forms.Timer? _scriptDebounce;
    private bool _scriptEditorDirty;
    private bool _updatingScriptEditor;
    private Panel? _welcomePanel;

    public InstallProject Project => _project;
    private Form? _guidedOwner;
    private Form ActionOwner => _guidedOwner is { IsDisposed: false } ? _guidedOwner : this;
    internal event EventHandler? BuildResultRecorded;
    internal event EventHandler? BuildStarted;
    internal bool IsBuilding => _buildWorkspace.Snapshot.IsRunning;
    internal AuthoringBuildWorkspace BuildWorkspace => _buildWorkspace;
    internal BuildPipeline.BuildResult? LastBuildResult => _lastBuildResult;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<ProcessStartInfo> ArtifactLauncher { get; set; } = info => Process.Start(info);

    internal void StartGuidedBuild(Form owner)
    {
        _guidedOwner = owner;
        _ = Handle;
        BuildInstaller();
    }

    internal void OpenDiagnostic(string path)
    {
        if (AuthoringDiagnosticNavigation.SectionForPath(path) == "customactions")
        {
            EditCustomActionDiagnostic(path, ActionOwner);
            return;
        }
        OpenSection(AuthoringDiagnosticNavigation.SectionForPath(path));
        FocusDiagnostic(path);
    }

    private string? _pendingDiagnosticFocus;
    internal void FocusDiagnostic(string path)
    {
        if (_activeContent is null) return;
        if (!_activeContent.Visible) { _pendingDiagnosticFocus = path; return; }
        _pendingDiagnosticFocus = null;
        DiagnosticEditorNavigator.Focus(_activeContent, path);
    }
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible || !IsHandleCreated || _pendingDiagnosticFocus is null) return;
        BeginInvoke(() =>
        {
            if (IsDisposed || Disposing || !Visible || _pendingDiagnosticFocus is not { } path) return;
            FocusDiagnostic(path);
        });
    }
    public PackageBuilderForm(InstallerController controller, string[]? runtimeArgs = null)
    {
        _runtimeArgs = runtimeArgs?.ToArray() ?? Array.Empty<string>();
        _controller = controller;
        _project = controller.Project;
        RegisterSectionViews();
        InitializeUi();
        _themeBinding = new AuthoringThemeBinding(this);
        BindController();
        BindProject();
        UpdateHistoryActions();
        OnHistoryLanguageChanged(this, EventArgs.Empty);
        UpdateTitle();
        Engine.Accessibility.Attach(this);
    }

    private void BindController()
    {
        Lang.LanguageManager.LanguageChanged += OnHistoryLanguageChanged;
        _controller.PropertyChanged += OnControllerPropertyChanged;
        _controller.ProjectReloaded += OnControllerProjectReloaded;
        _controller.ProjectMutated += OnControllerProjectMutated;
        _buildWorkspace.Changed += OnBuildWorkspaceChanged;
    }

    private InstallProject? _boundProject;

    private void BindProject()
    {
        if (_boundProject is not null) _boundProject.PropertyChanged -= OnProjectPropertyChanged;
        _boundProject = _project;
        _boundProject.PropertyChanged += OnProjectPropertyChanged;
    }

    private void OnControllerProjectReloaded(object? sender, EventArgs e) => OnProjectReloaded();
    private void OnControllerProjectMutated(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => OnControllerProjectMutated(sender, e)); return; }
        RefreshFileTree();
        MarkReadinessStale();
        ScheduleInlineValidation();
        if (_activeSectionId == "script") RefreshScriptPreview();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _themeBinding.Dispose();
            Lang.LanguageManager.LanguageChanged -= OnHistoryLanguageChanged;
            _controller.PropertyChanged -= OnControllerPropertyChanged;
            _controller.ProjectReloaded -= OnControllerProjectReloaded;
            _controller.ProjectMutated -= OnControllerProjectMutated;
            _buildWorkspace.Changed -= OnBuildWorkspaceChanged;
            _buildWorkspace.Dispose();
            if (_boundProject is not null) _boundProject.PropertyChanged -= OnProjectPropertyChanged;
            _inlineValidationTimer?.Dispose();
            _scriptDebounce?.Dispose();
            _fieldErrors.Dispose();
            _readinessChecks.Dispose();
            InvalidateAllContent();
            _welcomePanel?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (!IsHandleCreated) return;
        if (InvokeRequired) { BeginInvoke(() => OnControllerPropertyChanged(sender, e)); return; }
        UpdateHistoryActions();
        if (e.PropertyName == nameof(InstallerController.IsDirty) ||
            e.PropertyName == nameof(InstallerController.Project) ||
            e.PropertyName == nameof(InstallerController.SaveState))
        {
            if (_dirtyLabel is null) return;
            var state = _controller.SaveState;
            if (_scriptEditorDirty) state = state with { IsDirty = true };
            _dirtyLabel.Text = SaveStatePresentation.Caption(state);
            _dirtyLabel.ToolTipText = SaveStatePresentation.Details(state);
            _dirtyLabel.AccessibleName = _dirtyLabel.Text;
            _dirtyLabel.AccessibleDescription = _dirtyLabel.ToolTipText;
            UpdateTitle();
        }
    }

    private void OnProjectReloaded()
    {
        _pendingDiagnosticFocus = null;
        SetStatus("");
        ResetSourceScanCaption();
        InvalidateBuildForProjectChange();
        _project = _controller.Project;
        BindProject();
        _lastBuildResult = null;
        _validationCenterHasReport = _formatCapabilityHasReport = false;
        _displayedValidationReport = null; _displayedCapabilityReport = null;
        _buildResultLabel.Text = "";
        _buildResultLabel.IsLink = false;
        InvalidateAllContent();
        HideWelcome();

        // Invalidation drops the panel that is currently on screen along with the rest, so rebuild
        // it against the project that was just opened. Without this the user is left looking at a
        // blank pane -- and before invalidation covered these sections at all, at the previous
        // project's data.
        if (_activeSectionId is { } section) OnSectionSelected(this, section);

        RefreshReadiness();
    }

    private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => OnProjectPropertyChanged(sender, e)); return; }
        if (e.PropertyName != nameof(InstallProject.IsDirty) || _project.IsDirty) InvalidateBuildForProjectChange();
        MarkReadinessStale();
        if (e.PropertyName == nameof(InstallProject.SourceDirectory)) { ResetSourceScanCaption(); RefreshFileTree(); }
        if (_brandingPreview is { IsDisposed: false }) _brandingPreview.Invalidate();
        ScheduleInlineValidation();
    }

    private void MarkReadinessStale()
    {
        _readinessChecks.Invalidate();
        if (_readinessLabel is null || ApplyNoProjectState()) return;
        _readinessLabel.Text = L("Readiness_Stale", "Project changed — readiness needs checking.");
        AuthoringThemeBinding.SetTone(_readinessLabel, AuthoringTone.Muted);
        _readinessLabel.ToolTipText = "";
    }

    private void OnAuthoringResourceChanged()
    {
        if (IsDisposed || Disposing) return;
        _project.MarkDirty();
        _controller.CommitAuthoringChanges("Edit resource");
        InvalidateBuildForProjectChange();
        MarkReadinessStale();
        ScheduleInlineValidation();
    }

    /// <summary>Restarts the debounce; validation runs once the user pauses.</summary>
    private void ScheduleInlineValidation()
    {
        if (IsDisposed || Disposing) return;

        _inlineValidationTimer ??= CreateInlineValidationTimer();
        _inlineValidationTimer.Stop();
        _inlineValidationTimer.Start();
    }

    private System.Windows.Forms.Timer CreateInlineValidationTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 400 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _controller.CommitAuthoringChanges();
            ValidateFieldsInline();
            RefreshReadiness();
        };
        // No designer container on this form, so the timer is disposed with the form explicitly.
        Disposed += (_, _) => timer.Dispose();
        return timer;
    }

    /// <summary>
    /// Marks the bound fields the schema validator objects to.
    ///
    /// Deliberately the same validator the build runs, so the builder cannot tell the author a
    /// project is fine and then have `/BUILD` reject it -- which is exactly the split that let a
    /// project fail `/VALIDATE` and build anyway (3.C.1). Warnings are shown too, with their
    /// severity in the text, because an ErrorProvider icon alone does not distinguish them.
    /// </summary>
    private void ValidateFieldsInline()
    {
        if (IsDisposed || Disposing || _activeContent is null || _activeContent.IsDisposed) return;

        var byName = new Dictionary<string, Control>(StringComparer.Ordinal);
        CollectNamedFields(_activeContent, byName);
        if (byName.Count == 0) return;

        foreach (var control in byName.Values)
            _fieldErrors.SetError(control, "");

        ProjectSchemaValidationResult validation;
        try
        {
            validation = ProjectSchemaService.Validate(_project);
        }
        catch
        {
            // Inline feedback is a convenience; a validator fault must not take the builder down.
            return;
        }

        var messages = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var diagnostic in validation.Diagnostics)
        {
            if (string.IsNullOrWhiteSpace(diagnostic.Path)) continue;

            // "Setup.AppVersion" -> "AppVersion", which is what the bound control is named.
            var property = diagnostic.Path[(diagnostic.Path.LastIndexOf('.') + 1)..];
            if (!byName.ContainsKey(property)) continue;

            var prefix = diagnostic.Severity == ProjectSchemaDiagnosticSeverity.Error ? "" : "Warning: ";
            if (!messages.TryGetValue(property, out var list))
                messages[property] = list = new List<string>();
            list.Add(prefix + diagnostic.Message);
        }

        foreach (var (property, list) in messages)
            _fieldErrors.SetError(byName[property], string.Join(Environment.NewLine, list));
    }

    /// <summary>Bound editors, which are named after the property they edit.</summary>
    private static void CollectNamedFields(Control root, Dictionary<string, Control> into)
    {
        foreach (Control child in root.Controls)
        {
            if (!string.IsNullOrEmpty(child.Name) && child is TextBox or ComboBox or NumericUpDown)
                into[child.Name] = child;
            CollectNamedFields(child, into);
        }
    }

    private void InvalidateBuildForProjectChange()
    {
        if (_buildWorkspace.Snapshot.Id == Guid.Empty && _lastBuildResult is null) return;
        _lastBuildResult = null;
        _lastBuildProjectName = null;
        if (_buildResultLabel is not null) { _buildResultLabel.Text = ""; _buildResultLabel.IsLink = false; }
        _buildWorkspace.ProjectChanged();
        PopulateResultSection();
        PopulateWorkflowResult();
    }

    private void OnBuildWorkspaceChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => OnBuildWorkspaceChanged(sender, e)); }
            catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated) { }
            return;
        }
        var state = _buildWorkspace.Snapshot;
        _progress.Visible = state.IsRunning;
        _progress.Value = state.Percent;
        SyncBuildLogViews(state.Log);
        if (state.Result is null || state.Id == _recordedBuildId) return;
        if (state.IsCurrentProject && (!ReferenceEquals(_buildProjectReference, _controller.Project) ||
            _buildProjectScript != _controller.PreviewScript()))
        {
            _buildWorkspace.DetachCurrentBuild();
            return;
        }
        _recordedBuildId = state.Id;
        var completedStage = state.Stage;
        SetStatusPresentation(() => BuildWorkspaceView.StageCaption(completedStage));
        if (state.IsCurrentProject)
        {
            RecordLastBuild(state.Result, state.ProjectName);
            ShowBuildResultInForm(state.Result);
        }
    }

    private void InitializeUi()
    {
        Text = L("Builder_BeepInstallerPackageBuilder", "Beep Installer — Package Builder");
        Size = new Size(1180, 760);
        MinimumSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Icon = SystemIcons.Application;
        Font = UiFont;
        BackColor = ShellBackColor;

        _recentsBtn = new ToolStripDropDownButton(L("Builder_Recent", "Recent")) { ToolTipText = L("Builder_RecentlyOpenedInstallerScripts", "Recently opened installer scripts") };
        _recentsBtn.Name = "Builder_Recent";
        C("Builder_Recent", "Recent").Bind(_recentsBtn, C("Builder_RecentlyOpenedInstallerScripts", "Recently opened installer scripts"));
        _toolbar = new ToolStrip
        {
            ImageScalingSize = new Size(16, 16),
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = PanelBackColor,
            Padding = new Padding(8, 6, 8, 6),
            RenderMode = ToolStripRenderMode.System,
        };
        _toolbar.Items.AddRange(new ToolStripItem[]
        {
            NewButton(), OpenButton(), SaveButton(), SaveAsButton(),
            _undoButton = MakeButton(C("History_Undo", "Undo"), "Ctrl+Z", (_, _) => RestoreAuthoringHistory(false)),
            _redoButton = MakeButton(C("History_Redo", "Redo"), "Ctrl+Y", (_, _) => RestoreAuthoringHistory(true)),
            new ToolStripSeparator(), RecentButton(), new ToolStripSeparator(),
            // Grouped by intent: how to start, then the build-and-release path, then the occasional tools
            // folded into one menu so the bar fits and nothing falls into the overflow arrow.
            GuideButton(), QuickStartButton(), new ToolStripSeparator(),
            PreviewButton(), BuildButton(), PackagingButton(), SigningButton(), PublishButton(), new ToolStripSeparator(),
            McpButton(), MoreToolsMenu()
        });
        ApplyToolbarIcons();
        TheTechIdea.Beep.Winform.Controls.ThemeManagement.BeepThemesManager.ThemeChanged += (_, _) => { if (!IsDisposed && IsHandleCreated) BeginInvoke(ApplyToolbarIcons); };

        _nav = new LeftNavPanel { Dock = DockStyle.Fill, BackColor = InstallerTheme.Sidebar };
        PopulateLeftNav();
        _nav.SectionSelected += OnSectionSelected;
        _navSearchBox = new TextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = L("Builder_SearchSections", "Search sections…"),
            Margin = new Padding(8),
            Name = "AuthoringSectionSearch",
            AccessibleName = L("Builder_SearchSections", "Search sections…")
        };
        _navSearchBox.TextChanged += (_, _) => _nav.FilterText = _navSearchBox.Text;
        _navSearchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (_nav.SelectFirstMatch())
                    e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _navSearchBox.Clear();
                e.Handled = true;
            }
        };
        var navHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = Color.FromArgb(241, 244, 248),
            Padding = new Padding(8, 8, 8, 8)
        };
        AuthoringThemeBinding.SetSurface(navHost, AuthoringSurface.Sidebar);
        navHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        navHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        navHost.Controls.Add(_navSearchBox, 0, 0);
        navHost.Controls.Add(_nav, 0, 1);

        _contentHost = new Panel { Dock = DockStyle.Fill, BackColor = ShellBackColor, Padding = new Padding(14) };
        AuthoringThemeBinding.SetSurface(_contentHost, AuthoringSurface.Shell);
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, IsSplitterFixed = false, SplitterWidth = 3, Panel1MinSize = 200 };
        split.Panel1.Controls.Add(navHost);
        split.Panel2.Controls.Add(_contentHost);
        // Set once the form has its real width: a SplitContainer clamps the distance against its width at
        // the moment of assignment, and before layout that is the 150 px default, which is what clipped the
        // longer section names ("Advanced Resour…").
        split.SplitterDistance = 272;
        Shown += (_, _) => split.SplitterDistance = Math.Clamp(272, split.Panel1MinSize, Math.Max(split.Panel1MinSize, split.Width - split.Panel2MinSize - split.SplitterWidth));

        _status = new StatusStrip { BackColor = PanelBackColor, SizingGrip = false };
        _statusLabel = new ToolStripStatusLabel("Ready.") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _dirtyLabel = new ToolStripStatusLabel { Text = "" };
        _buildResultLabel = new ToolStripStatusLabel { Text = "", IsLink = true, ForeColor = AccentColor };
        _buildResultLabel.Click += (_, _) => OpenLastBuild();
        _progress = new ToolStripProgressBar { Size = new Size(180, 16), Visible = false };
        // Whether the project can build, visible at all times.
        //
        // Answering "can I build this yet?" previously meant pressing Build and reading a dialog --
        // the answer existed the whole time and was only offered as the result of an action that
        // takes a minute. Inline validation marks the field that is wrong, but only on the section
        // being looked at, so a problem three sections away stayed invisible.
        _readinessLabel = new ToolStripStatusLabel
        {
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 12, 0),
        };

        _status.Items.AddRange(new ToolStripItem[] { _statusLabel, _readinessLabel, _dirtyLabel, _buildResultLabel, _progress });

        Controls.Add(split);
        Controls.Add(_toolbar);
        Controls.Add(_status);

        BuildWelcomePanel();
        ShowWelcome();

        KeyPreview = true;
        KeyDown += OnKeyDown;
        AllowDrop = true;
        DragEnter += (_, e) => { e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None; };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            {
                var file = paths[0];
                if (file.EndsWith(".bsetup", StringComparison.OrdinalIgnoreCase))
                    LoadProject(file);
            }
        };

        _scriptDebounce = new System.Windows.Forms.Timer { Interval = 400 };
        _scriptDebounce.Tick += (_, _) => { _scriptDebounce.Stop(); RefreshScriptPreview(); };
    }

    /// <summary>
    /// Shorthand for a localized string with the English text as the fallback. The builder was
    /// entirely English-only; this covers its high-traffic chrome (navigation sections and the
    /// repeated action buttons). Full coverage of every field label remains outstanding.
    /// </summary>
    private static string L(string key, string english)
        => Lang.LanguageManager.GetOrDefault(key, english);
    private static LiveUiCaption C(string key, string english) => new(key, english);

    private void RegisterSectionViews()
    {
        _sectionViews.Register("identity", () => GetOrCreate(ref _contentIdentity, BuildIdentitySection));
        _sectionViews.Register("layout", () => GetOrCreate(ref _contentLayout, BuildLayoutSection));
        _sectionViews.Register("eula", () => GetOrCreate(ref _contentEula, BuildEulaSection));
        _sectionViews.Register("source", () => GetOrCreate(ref _contentSource, BuildSourceSection));
        _sectionViews.Register("includes", () => GetOrCreate(ref _contentIncludes, BuildIncludesSection));
        _sectionViews.Register("components", () => GetOrCreate(ref _contentComponents, BuildComponentsSection));
        _sectionViews.Register("prerequisites", () => GetOrCreate(ref _contentPrerequisites, BuildPrerequisitesSection));
        _sectionViews.Register("shortcuts", () => GetOrCreate(ref _contentShortcuts, BuildShortcutsSection));
        _sectionViews.Register("registry", () => GetOrCreate(ref _contentRegistry, BuildRegistrySection));
        _sectionViews.Register("branding", () => GetOrCreate(ref _contentBranding, BuildBrandingSection));
        _sectionViews.Register("wizardpages", () => GetOrCreate(ref _contentWizardPages, BuildWizardPagesSection));
        _sectionViews.Register("build", () => GetOrCreate(ref _contentBuild, BuildWorkflowSection));
        _sectionViews.Register("script", () => GetOrCreate(ref _contentScript, BuildScriptSection));
        _sectionViews.Register("output", () => GetOrCreate(ref _contentOutput, BuildOutputSection));
        _sectionViews.Register("payload", () => GetOrCreate(ref _contentPayload, BuildPayloadSection));
        _sectionViews.Register("package", () => GetOrCreate(ref _contentPackage, BuildPackageSection));
        _sectionViews.Register("compression", () => GetOrCreate(ref _contentCompression, BuildCompressionSection));
        _sectionViews.Register("codesign", () => GetOrCreate(ref _contentCodeSign, BuildCodeSignSection));
        _sectionViews.Register("msix", () => GetOrCreate(ref _contentMsix, BuildMsixSection));
        _sectionViews.Register("log", () => GetOrCreate(ref _contentLog, BuildLogSection));
        _sectionViews.Register("result", () => GetOrCreate(ref _contentResult, BuildResultSection));
        _sectionViews.Register("scheduledtasks", () => GetOrCreate(ref _contentScheduledTasks, BuildScheduledTasksSection));
        _sectionViews.Register("firewallrules", () => GetOrCreate(ref _contentFirewallRules, BuildFirewallRulesSection));
        _sectionViews.Register("certificates", () => GetOrCreate(ref _contentCertificates, BuildCertificatesSection));
        _sectionViews.Register("comregistrations", () => GetOrCreate(ref _contentComRegistrations, BuildComRegistrationsSection));
        _sectionViews.Register("driverpackages", () => GetOrCreate(ref _contentDriverPackages, BuildDriverPackagesSection));
        _sectionViews.Register("configtransforms", () => GetOrCreate(ref _contentConfigTransforms, BuildConfigTransformsSection));
        _sectionViews.Register("iisapppools", () => GetOrCreate(ref _contentIisAppPools, BuildIisAppPoolsSection));
        _sectionViews.Register("iissites", () => GetOrCreate(ref _contentIisSites, BuildIisSitesSection));
        _sectionViews.Register("webdeploy", () => GetOrCreate(ref _contentWebDeployPackages, BuildWebDeployPackagesSection));
        _sectionViews.Register("environment", () => GetOrCreate(ref _contentEnvironmentVariables, BuildEnvironmentVariablesSection));
        _sectionViews.Register("fileassociations", () => GetOrCreate(ref _contentFileAssociations, BuildFileAssociationsSection));
        _sectionViews.Register("windowsservices", () => GetOrCreate(ref _contentWindowsServices, BuildWindowsServicesSection));
        _sectionViews.Register("updatechannels", () => GetOrCreate(ref _contentUpdateChannels, BuildUpdateChannelsSection));
        _sectionViews.Register("packages", () => GetOrCreate(ref _contentPackages, BuildPackagesSection));
        _sectionViews.Register("prereqcatalogs", () => GetOrCreate(ref _contentPrerequisiteCatalogs, BuildPrerequisiteCatalogsSection));
        _sectionViews.Register("msixoptional", () => GetOrCreate(ref _contentMsixOptionalPackages, BuildMsixOptionalPackagesSection));
        _sectionViews.Register("supersedence", () => GetOrCreate(ref _contentSupersedence, BuildDeploymentSupersedenceSection));
        _sectionViews.Register("custompages", () => GetOrCreate(ref _contentCustomPages, BuildCustomPagesSection));
        _sectionViews.Register("resources", () => GetOrCreate(ref _contentResources, BuildResourcesSection));
    }

    private void PopulateLeftNav()
    {
        // Section headers go through the resource manager; the English text stays as the
        // fallback so behaviour is unchanged for en.
        var g = _nav.AddSection("project", "Project", "Nav_Project");
        _nav.AddItem(g, "identity", "Identity", "Product name, version, publisher, setup titles");
        _nav.AddItem(g, "layout", "Layout", "Install directory, scope, privileges, install type");
        _nav.AddItem(g, "eula", "EULA", "License agreement and EULA display");

        g = _nav.AddSection("source", "Source", "Nav_Source");
        _nav.AddItem(g, "source", "Files", "Source directory, payload files, scan");
        _nav.AddItem(g, "includes", "Includes", "Include and exclude patterns");

        g = _nav.AddSection("features", "Features", "Nav_Features");
        _nav.AddItem(g, "components", "Components", "Feature tree, selected components, conditions");
        _nav.AddItem(g, "prerequisites", "Prerequisites", "Runtime packages, prerequisite catalogs, dependencies");
        _nav.AddItem(g, "shortcuts", "Shortcuts", "Desktop, Start Menu and Startup shortcuts");
        _nav.AddItem(g, "registry", "Registry", "Registry keys, values and uninstall metadata");

        g = _nav.AddSection("advanced", "Advanced Resources", "Nav_AdvancedResources");
        _nav.AddItem(g, "scheduledtasks", "Scheduled Tasks", "Task Scheduler actions, triggers, elevation and credentials");
        _nav.AddItem(g, "firewallrules", "Firewall Rules", "Windows Firewall ports, programs, services and profiles");
        _nav.AddItem(g, "certificates", "Certificates", "Certificate source, store, thumbprint and rollback");
        _nav.AddItem(g, "comregistrations", "COM", "COM CLSID, ProgID, server type and typelib registration");
        _nav.AddItem(g, "driverpackages", "Drivers", "INF/sys packages, service identity, signing and reboot behavior");
        _nav.AddItem(g, "configtransforms", "Config Transforms", "JSON/XML/INI transforms with rollback backups");
        _nav.AddItem(g, "iisapppools", "IIS App Pools", "Runtime, pipeline, identity and ownership settings");
        _nav.AddItem(g, "iissites", "IIS Sites", "Physical path, application pool, bindings and ownership");
        _nav.AddItem(g, "webdeploy", "Web Deploy", "msdeploy packages, IIS site target and parameters");
        _nav.AddItem(g, "environment", "Environment", "Variables written at install and removed at uninstall");
        _nav.AddItem(g, "fileassociations", "File Associations", "Extensions, ProgIds and the verbs that open them");
        _nav.AddItem(g, "windowsservices", "Windows Services", "Service identity, start mode and install behaviour");
        _nav.AddItem(g, "resources", "Typed Resources", "Operations run by the extension resource providers");

        g = _nav.AddSection("customize", "Customize", "Nav_Customize");
        _nav.AddItem(g, "branding", "Branding", "Theme, banner, logo and installer branding");
        _nav.AddItem(g, "wizardpages", "Wizard Pages", "Which built-in wizard pages the installer shows");
        _nav.AddItem(g, "custompages", "Custom Pages", "Extra pages and fields shown during install");

        // Output shape and packaging. Every one of these sections was already written and wired
        // into OnSectionSelected -- and none of them had a nav item, so there was no way to reach
        // code signing, MSIX, compression or the output settings from the UI at all. The features
        // were implemented and simply unreachable.
        g = _nav.AddSection("output", "Output", "Nav_Output");
        _nav.AddItem(g, "output", "Output", "Output directory, setup file name and install-type defaults");
        _nav.AddItem(g, "payload", "Payload", "Payload folder, single-file, self-contained and download URL");
        _nav.AddItem(g, "compression", "Compression", "Method, level and solid compression");
        _nav.AddItem(g, "package", "Package Format", "EXE, MSI, MSIX and store readiness");
        _nav.AddItem(g, "codesign", "Code Signing", "Certificate, timestamp URL and signing policy");
        _nav.AddItem(g, "msix", "MSIX", "Identity, publisher and optional packages");
        _nav.AddItem(g, "msixoptional", "MSIX Optional", "Optional packages in this application's MSIX family");
        _nav.AddItem(g, "packages", "Package Nodes", "Sub-packages composed into this installer");

        g = _nav.AddSection("deployment", "Deployment", "Nav_Deployment");
        _nav.AddItem(g, "updatechannels", "Update Channels", "Release rings, feeds and rollout percentage");
        _nav.AddItem(g, "prereqcatalogs", "Prerequisite Catalogs", "Signed prerequisite catalogs and trusted keys");
        _nav.AddItem(g, "supersedence", "Supersedence", "Earlier products this installer replaces (Intune/ConfigMgr)");

        g = _nav.AddSection("build", "Build", "Nav_Build");
        _nav.AddItem(g, "script", "Script", "Canonical .bsetup script preview and editor");
        _nav.AddItem(g, "build", "Build Workflow", "Validation, plan hash, package build and diagnostics");
        _nav.AddItem(g, "log", "Build Log", "Full output from the last build");
        _nav.AddItem(g, "result", "Last Result", "Artifacts, sizes and warnings from the last build");
    }

    private void OnSectionSelected(object? sender, string id)
    {
        if (_pendingDiagnosticFocus is { } path && AuthoringDiagnosticNavigation.SectionForPath(path) != id)
            _pendingDiagnosticFocus = null;
        HideWelcome();
        _activeSectionId = id;
        _contentHost.Controls.Clear();
        _activeContent = _sectionViews.GetView(id);
        if (_activeContent != null)
        {
            _activeContent.Dock = DockStyle.Fill;
            _activeContent.Font = Font;
            StyleControlTree(_activeContent);
            AuthoringThemeBinding.Refresh(_activeContent);
            _contentHost.Controls.Add(_activeContent);
        }
        if (id == "script") RefreshScriptPreview();
        if (id == "build") PopulateWorkflowResult();
        if (id == "result") PopulateResultSection();
        if (id == "build" && _activeContent is not null)
            _buildLogBox = FindNamed<TextBox>(_activeContent, "WorkflowBuildLog")!;
        if (id == "log" && _activeContent is not null)
            _buildLogBox = FindNamed<TextBox>(_activeContent, "LegacyBuildLog")!;
        if (id == "log" || (id == "build" && _buildWorkspace.Snapshot.Id != Guid.Empty))
            SyncBuildLogViews(_buildWorkspace.Snapshot.Log);

        // Mark what is already wrong on arrival, rather than only after the next edit.
        ValidateFieldsInline();
    }

    private void SyncBuildLogViews(string log)
    {
        foreach (var entry in new[] { (_contentBuild, "WorkflowBuildLog"), (_contentLog, "LegacyBuildLog") })
            if (entry.Item1 is { IsDisposed: false } && FindNamed<TextBox>(entry.Item1, entry.Item2) is { IsDisposed: false } box)
                box.Text = log;
    }

    private Panel? GetOrCreate(ref Panel? field, Func<Panel> builder)
    {
        if (field == null || field.IsDisposed) field = builder();
        return field;
    }

    /// <summary>
    /// Drops every cached section panel so the next view is rebuilt against the current project.
    ///
    /// This was a hand-written assignment chain and it had fallen nine sections behind the fields it
    /// was meant to cover -- certificates, COM registrations, config transforms, driver packages,
    /// firewall rules, the two IIS sections, scheduled tasks and web-deploy packages were never
    /// cleared. Those panels bind to the collections of the project that was open when they were
    /// first built, so after opening a second project they kept showing the first one's data and
    /// edits made in them were written back to the project the user had closed.
    ///
    /// Enumerating the fields instead of listing them means a new section cannot be forgotten.
    /// </summary>
    private void InvalidateAllContent()
    {
        foreach (var field in CachedSectionPanelFields)
        {
            if (field.GetValue(this) is Panel panel && !panel.IsDisposed)
                panel.Dispose();

            field.SetValue(this, null);
        }
    }

    /// <summary>
    /// Every cached section panel field (<c>_contentIdentity</c>, <c>_contentLayout</c>, ...) --
    /// found by name convention rather than listed, per <see cref="InvalidateAllContent"/>'s own
    /// reasoning. That convention also matches <c>_contentHost</c>, the live container those panels
    /// get docked into, not a cached one of them: nulling and disposing it here left the builder with
    /// no content area at all, crashing the next line in <c>OnProjectReloaded</c>
    /// (<c>HideWelcome</c>'s <c>_contentHost.Controls.Clear()</c>) the moment a second project loaded
    /// into an already-open builder -- e.g. File &gt; Open while a project is already open. Excluded
    /// by exact name rather than tightening the prefix, since every real cached-panel field already
    /// names its section (<c>_contentIdentity</c>, never bare <c>_content...</c>).
    /// </summary>
    private static readonly System.Reflection.FieldInfo[] CachedSectionPanelFields =
        typeof(PackageBuilderForm)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(Panel)
                        && f.Name.StartsWith("_content", StringComparison.Ordinal)
                        && f.Name != "_contentHost")
            .ToArray();

    // ═══════════════════════════════════════════
    //  Section builders — all WinForms DataBinding
    // ═══════════════════════════════════════════

    private Panel BuildIdentitySection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddBoundRow(layout, C("Field_ScriptName", "Script name:"),        proj, nameof(InstallProject.ProjectName));
        AddBoundRow(layout, C("Field_ProductName", "Product name:"),       proj, nameof(InstallProject.AppName));
        AddBoundRow(layout, C("Field_ProductIDAppId", "Product ID (AppId):"),  proj, nameof(InstallProject.AppId));
        var identityHelp = new Label
        {
            Name = "IdentityGuidance",
            AutoSize = true,
            Text = L("Builder_KeepThisGUIDUnchangedFor", "Keep this GUID unchanged for updates and renames. A different GUID identifies a different product."),
            AccessibleName = "Product ID guidance",
            Margin = new Padding(3, 0, 3, 8)
        };
        C("Builder_KeepThisGUIDUnchangedFor", "Keep this GUID unchanged for updates and renames. A different GUID identifies a different product.").Bind(identityHelp);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(identityHelp, 1, layout.RowCount++);
        AddBoundRow(layout, C("Field_Version", "Version:"),            proj, nameof(InstallProject.AppVersion));
        AddBoundRow(layout, C("Field_Publisher", "Publisher:"),          proj, nameof(InstallProject.AppPublisher));
        AddBoundRow(layout, C("Field_PublisherURL", "Publisher URL:"),      proj, nameof(InstallProject.AppPublisherURL));
        AddBoundRow(layout, C("Field_SupportURL", "Support URL:"),        proj, nameof(InstallProject.AppSupportURL));
        AddBoundRow(layout, C("Field_SupportEmail", "Support email:"),      proj, nameof(InstallProject.AppSupportEmail));
        AddBoundRow(layout, C("Field_UpdateURL", "Update URL:"),         proj, nameof(InstallProject.AppUpdatesURL));
        AddEnumRow<UpdateMode>(layout, C("Field_UpdateMode", "Update mode:"),  proj, nameof(InstallProject.AppUpdateMode));
        AddFileRow(layout, C("Field_SetupIconIco", "Setup icon (.ico):"),  proj, nameof(InstallProject.SetupIconFile), "*.ico");
        AddFileRow(layout, C("Field_BannerImage", "Banner image:"),       proj, nameof(InstallProject.WizardImageFile), "*.png;*.jpg;*.bmp");
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildLayoutSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddFolderRow(layout, C("Field_DefaultInstallPath", "Default install path:"), proj, nameof(InstallProject.DefaultDirName));
        AddBoundRow(layout, C("Field_StartMenuFolder", "Start menu folder:"),    proj, nameof(InstallProject.DefaultGroupName));
        AddEnumRow<InstallationType>(layout, C("Field_InstallType", "Install type:"), proj, nameof(InstallProject.DefaultInstallType));
        AddEnumRow<PrivilegeLevel>(layout, C("Field_Privileges", "Privileges:"), proj, nameof(InstallProject.PrivilegesRequired));
        AddEnumRow<InstallationScope>(layout, C("Field_Scope", "Scope:"), proj, nameof(InstallProject.DefaultScope));
        AddCheckRow(layout, C("Field_AllowScopeSelection", "Allow scope selection"),  proj, nameof(InstallProject.AllowScopeSelection));
        AddCheckRow(layout, C("Field_Prefer64Bit", "Prefer 64-bit"),          proj, nameof(InstallProject.Prefer64Bit));
        AddCheckRow(layout, C("Field_AllowNoIcons", "Allow no icons"),         proj, nameof(InstallProject.AllowNoIcons));
        AddCheckRow(layout, C("Field_ShowDirOnReadyPage", "Show dir on ready page"), proj, nameof(InstallProject.AlwaysShowDirOnReadyPage));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildEulaSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddFileRow(layout, C("Field_LicenseFile", "License file:"), proj, nameof(InstallProject.LicenseFile), "*.rtf;*.txt;*.md");
        AddCheckRow(layout, C("Field_ShowEULAPage", "Show EULA page:"), proj, nameof(InstallProject.ShowEula));
        p.Controls.Add(layout);

        _licenseTextBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9) };
        _licenseTextBox.DataBindings.Add("Text", proj, nameof(InstallProject.LicenseText), false, DataSourceUpdateMode.OnPropertyChanged);
        var licPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        licPanel.Controls.Add(_licenseTextBox);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 120, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.AutoScroll = true;
        split.Panel1.Controls.Add(layout);
        split.Panel2.Controls.Add(licPanel);
        p.Controls.Add(split);
        return p;
    }

    // ═══════════════════════════════════════════
    //  Direct WinForms DataBindings.Add helpers
    // ═══════════════════════════════════════════

    /// <summary>A bound, editable text column. Declared width rather than reflected order.</summary>
    private static DataGridViewTextBoxColumn TextColumn(string property, string header, int fillWeight)
        => new()
        {
            DataPropertyName = property,
            HeaderText = header,
            Name = property,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        };

    /// <summary>A bound checkbox column, for the booleans a text cell would render as "True".</summary>
    private static DataGridViewCheckBoxColumn CheckColumn(string property, string header, int fillWeight)
        => new()
        {
            DataPropertyName = property,
            HeaderText = header,
            Name = property,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        };

    private static TableLayoutPanel NewTwoColTable()
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(20),
            BackColor = PanelBackColor,
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    private static Panel NewSectionPanel()
        => new()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = PanelBackColor,
        };

    private static void AddSectionRow(TableLayoutPanel layout, LiveUiCaption title)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label
        {
            Text = title.Value,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            AutoSize = true,
            MinimumSize = new Size(0, 26),
            Font = UiFontBold,
            ForeColor = TextColor,
            Margin = new Padding(0, 10, 0, 6),
        };
        layout.SetColumnSpan(label, 2);
        title.Bind(label);
        layout.Controls.Add(label, 0, row);
    }

    private static void AddBoundRow(TableLayoutPanel layout, LiveUiCaption label, InstallProject proj, string propertyName)
    {
        var lbl = CreateRowLabel(label.Value); label.Bind(lbl);
        var box = new TextBox
        {
            Name = propertyName,
            AccessibleName = label.Value.TrimEnd(':'),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 4)
        };
        box.DataBindings.Add("Text", proj, propertyName, true, DataSourceUpdateMode.OnPropertyChanged);
        label.Bind(box, text: false, trimColon: true);
        int row = AddTableRow(layout);
        layout.Controls.Add(lbl, 0, row);
        layout.Controls.Add(box, 1, row);
    }

    private static void AddCheckRow(TableLayoutPanel layout, LiveUiCaption label, InstallProject proj, string propertyName)
    {
        var cb = new CheckBox { Name = propertyName, Text = label.Value.TrimEnd(':'), Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 6), AutoSize = true, MinimumSize = new Size(0, 26) };
        label.Bind(cb, trimColon: true);
        cb.DataBindings.Add("Checked", proj, propertyName, true, DataSourceUpdateMode.OnPropertyChanged);
        int row = AddTableRow(layout);
        layout.SetColumnSpan(cb, 2);
        layout.Controls.Add(cb, 0, row);
    }

    private static void AddEnumRow<TEnum>(TableLayoutPanel layout, LiveUiCaption label, InstallProject proj, string propertyName)
        where TEnum : struct, Enum
    {
        var lbl = CreateRowLabel(label.Value); label.Bind(lbl);
        var combo = new ComboBox { Name = propertyName, Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 0, 4) };
        label.Bind(combo, text: false, trimColon: true);
        foreach (var v in Enum.GetValues<TEnum>()) combo.Items.Add(v);
        combo.DataBindings.Add("SelectedItem", proj, propertyName, true, DataSourceUpdateMode.OnPropertyChanged);
        int row = AddTableRow(layout);
        layout.Controls.Add(lbl, 0, row);
        layout.Controls.Add(combo, 1, row);
    }

    private static void AddFileRow(TableLayoutPanel layout, LiveUiCaption label, InstallProject proj, string propertyName, string filter)
    {
        var lbl = CreateRowLabel(label.Value); label.Bind(lbl);
        var box = new TextBox { Name = propertyName, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 8, 4) };
        var btn = new Button { Text = "...", Dock = DockStyle.Right, Width = 36, Margin = new Padding(0, 3, 0, 3) };
        label.Bind(box, text: false, trimColon: true);
        label.Bind(btn, text: false, trimColon: true, prefix: C("Btn_Browse", "Browse..."));
        box.DataBindings.Add("Text", proj, propertyName, true, DataSourceUpdateMode.OnPropertyChanged);
        btn.Click += (_, _) =>
        {
            using var dlg = AuthoringFileDialogs.File(label.Value, filter);
            if (dlg.ShowDialog(btn) == DialogResult.OK) box.Text = dlg.FileName;
        };
        var host = new Panel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 30) };
        host.Controls.Add(box);
        host.Controls.Add(btn);
        int row = AddTableRow(layout);
        layout.Controls.Add(lbl, 0, row);
        layout.Controls.Add(host, 1, row);
    }

    private static void AddFolderRow(TableLayoutPanel layout, LiveUiCaption label, InstallProject proj, string propertyName)
    {
        var lbl = CreateRowLabel(label.Value); label.Bind(lbl);
        var box = new TextBox { Name = propertyName, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 8, 4) };
        var btn = new Button { Text = "...", Dock = DockStyle.Right, Width = 36, Margin = new Padding(0, 3, 0, 3) };
        label.Bind(box, text: false, trimColon: true);
        label.Bind(btn, text: false, trimColon: true, prefix: C("Btn_Browse", "Browse..."));
        box.DataBindings.Add("Text", proj, propertyName, true, DataSourceUpdateMode.OnPropertyChanged);
        btn.Click += (_, _) =>
        {
            using var dlg = AuthoringFileDialogs.Folder(label.Value, box.Text);
            if (dlg.ShowDialog(btn) == DialogResult.OK) box.Text = dlg.SelectedPath;
        };
        var host = new Panel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 30) };
        host.Controls.Add(box);
        host.Controls.Add(btn);
        int row = AddTableRow(layout);
        layout.Controls.Add(lbl, 0, row);
        layout.Controls.Add(host, 1, row);
    }

    private static int AddTableRow(TableLayoutPanel layout)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return row;
    }

    private static Label CreateRowLabel(string text)
    {
        var label = new Label
        {
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            MinimumSize = new Size(0, 30),
            Dock = DockStyle.Fill,
            ForeColor = MutedTextColor,
            Font = UiFontBold,
            Margin = new Padding(0, 4, 12, 4),
        };
        AuthoringThemeBinding.SetTone(label, AuthoringTone.Muted);
        return label;
    }

    private Panel BuildIncludesSection()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8) };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        right.Controls.Add(CreatePatternPanel(C("Builder_IncludePatterns", "Include patterns:"), nameof(InstallProject.SourceIncludes), out _includeList, out _includeBox, _project.SourceIncludes), 0, 0);
        right.Controls.Add(CreatePatternPanel(C("Builder_ExcludePatterns", "Exclude patterns:"), nameof(InstallProject.SourceExcludes), out _excludeList, out _excludeBox, _project.SourceExcludes), 1, 0);
        p.Controls.Add(right);
        return p;
    }

    private Panel CreatePatternPanel(LiveUiCaption label, string name, out ListBox list, out TextBox box,
        System.Collections.ObjectModel.ObservableCollection<string> collection)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 0, 8, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading = new Label { Name = name + "Caption", Dock = DockStyle.Top, AutoSize = true }; label.Bind(heading);
        panel.Controls.Add(heading, 0, 0);

        list = new ListBox { Name = name, Dock = DockStyle.Fill };
        label.Bind(list, text: false, trimColon: true);
        var patterns = new BindingSource { DataSource = collection };
        list.DataSource = patterns;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) => patterns.ResetBindings(false);
        collection.CollectionChanged += changed;
        panel.Disposed += (_, _) => { collection.CollectionChanged -= changed; patterns.Dispose(); };
        panel.Controls.Add(list, 0, 1);

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
        box = new TextBox { Name = name + "Draft", Width = 180 };
        label.Bind(box, text: false, trimColon: true);
        var listRef = list;
        var boxRef = box;
        var addBtn = new Button { Name = name + "Add", AutoSize = true };
        var removeBtn = new Button { Name = name + "Remove", AutoSize = true };
        C("Btn_Add", "Add").Bind(addBtn); C("Btn_Remove", "Remove").Bind(removeBtn);
        addBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(boxRef.Text))
            {
                _controller.RunAuthoringChange("Add source pattern", () =>
                {
                    collection.Add(boxRef.Text.Trim());
                    boxRef.Clear();
                    OnAuthoringResourceChanged();
                });
            }
        };
        removeBtn.Click += (_, _) =>
        {
            if (listRef.SelectedItem is string s)
            {
                _controller.RunAuthoringChange("Remove source pattern", () =>
                {
                    collection.Remove(s);
                    OnAuthoringResourceChanged();
                });
            }
        };
        btnRow.Controls.AddRange(new Control[] { box, addBtn, removeBtn });
        panel.Controls.Add(btnRow, 0, 2);
        return panel;
    }

    private Panel BuildSourceSection()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var topBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 4, 8, 0) };
        topBar.AutoSize = true; topBar.WrapContents = true;
        var heading = new Label { Name = "SourceDirectoryCaption", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 4, 4, 0) };
        C("Builder_SourceDir", "Source directory:").Bind(heading); topBar.Controls.Add(heading);
        _sourceDirBox = new TextBox { Name = nameof(InstallProject.SourceDirectory), AccessibleName = L("Builder_SourceDir", "Source directory:"), Width = 400 };
        _sourceDirBox.DataBindings.Add("Text", _project, nameof(InstallProject.SourceDirectory), false, DataSourceUpdateMode.OnPropertyChanged);
        C("Builder_SourceDir", "Source directory:").Bind(_sourceDirBox, text: false, trimColon: true);
        var browseBtn = new Button { Name = "BrowseSource", Text = "…", Width = 30 };
        C("Builder_SourceDir", "Source directory:").Bind(browseBtn, text: false, trimColon: true, prefix: C("Btn_Browse", "Browse..."));
        browseBtn.Click += (_, _) => { using var dlg = AuthoringFileDialogs.Folder(L("Builder_SourceDir", "Source directory:"), _sourceDirBox.Text); if (dlg.ShowDialog(ActionOwner) == DialogResult.OK) _sourceDirBox.Text = dlg.SelectedPath; };
        var rescanBtn = new Button { Name = "RescanSource", AutoSize = true };
        C("Btn_Rescan", "Rescan").Bind(rescanBtn);
        rescanBtn.Click += (_, _) => RescanSource();
        topBar.Controls.Add(_sourceDirBox);
        topBar.Controls.Add(browseBtn);
        topBar.Controls.Add(rescanBtn);
        _fileStatsLabel = new Label { Name = "SourceScanStatus", AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(4, 6, 0, 0) };
        RefreshSourceScanCaptions();
        topBar.Controls.Add(_fileStatsLabel);

        _fileTree = new TreeView { Name = "SourceFiles", Dock = DockStyle.Fill, CheckBoxes = true };
        C("Nav_Source", "Source").Bind(_fileTree, text: false);
        _fileTree.AfterCheck += OnFileTreeCheck;
        p.Controls.Add(_fileTree);
        p.Controls.Add(topBar);
        return p;
    }

    private Panel BuildComponentsSection()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 340 };

        _componentsBinding = new BindingSource { DataSource = _project.Components };
        // Columns are declared, not reflected. AutoGenerateColumns produced one column per public
        // property of InstallComponent -- including nine List<T> members that render as
        // "ObservableCollection`1" and cannot be edited in a cell -- which were then hidden again
        // from a ListChanged handler. That meant the junk columns were visible until the list next
        // changed (on a freshly opened project, indefinitely), and every property added to the model
        // silently appeared in the grid until someone remembered to extend the hide list. It is the
        // same hand-maintained-list drift that left nine builder sections uninvalidated.
        _componentsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = _componentsBinding,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        _componentsGrid.Columns.AddRange(
            TextColumn(nameof(InstallComponent.Id), L("Grid_Id", "Id"), fillWeight: 18),
            TextColumn(nameof(InstallComponent.Name), L("Grid_Name", "Name"), fillWeight: 30),
            TextColumn(nameof(InstallComponent.Description), L("Grid_Description", "Description"), fillWeight: 34),
            CheckColumn(nameof(InstallComponent.Required), L("Grid_Required", "Required"), fillWeight: 9),
            CheckColumn(nameof(InstallComponent.Selected), L("Grid_Selected", "Selected"), fillWeight: 9));
        C("Nav_Components", "Components").Bind(_componentsGrid, text: false);
        RefreshComponentCaptions();

        _componentProps = new PropertyGrid { Dock = DockStyle.Fill, HelpVisible = false };
        C("Nav_Components", "Components").Bind(_componentProps, text: false);
        var componentRow = new BindingSourceRow<InstallComponent>(_componentsBinding);
        _componentsBinding.CurrentChanged += (_, _) => _componentProps.SelectedObject = componentRow.Current;
        _componentProps.SelectedObject = componentRow.Current;
        _componentProps.PropertyValueChanged += (_, _) => OnAuthoringResourceChanged();
        _componentsGrid.CellValueChanged += (_, _) => OnAuthoringResourceChanged();
        var componentBinding = _componentsBinding;
        p.Disposed += (_, _) => componentBinding.Dispose();

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
        var addBtn = new Button { Name = "ComponentsAdd", Text = L("Btn_Add", "Add"), Width = 50 };
        var editBtn = new Button { Text = L("Builder_EditFiles", "Edit Files"), Width = 70 };
        var removeBtn = new Button { Text = L("Btn_Remove", "Remove"), Width = 65 };
        var duplicateBtn = new Button { Text = L("Builder_Duplicate", "Duplicate"), AutoSize = true };
        var scanBtn = new Button { Text = L("Builder_ScanSource", "Scan Source"), Width = 90 };
        // Glyphs, not prose: these stay literal. Routing them through the resource manager wrote
        // the escape sequence itself into the .resx, so the buttons would have rendered the text
        // "\u25B2" rather than an arrow.
        var upBtn = new Button { Text = "\u25B2", Width = 30 };
        var downBtn = new Button { Text = "\u25BC", Width = 30 };
        C("Btn_Add", "Add").Bind(addBtn); C("Builder_EditFiles", "Edit Files").Bind(editBtn);
        C("Btn_Remove", "Remove").Bind(removeBtn); C("Builder_Duplicate", "Duplicate").Bind(duplicateBtn);
        C("Builder_ScanSource", "Scan Source").Bind(scanBtn);
        C("Builder_MoveUp", "Move up").Bind(upBtn, text: false); C("Builder_MoveDown", "Move down").Bind(downBtn, text: false);
        foreach (var button in new[] { addBtn, editBtn, removeBtn, duplicateBtn, scanBtn }) button.AutoSize = true;
        addBtn.Click += (_, _) => AddComponent();
        editBtn.Click += (_, _) => EditComponentFiles();
        removeBtn.Click += (_, _) => RemoveComponent();
        duplicateBtn.Click += (_, _) => DuplicateComponent();
        scanBtn.Click += (_, _) => ScanAndPopulate(_project.SourceDirectory);
        upBtn.Click += (_, _) => MoveComponent(-1);
        downBtn.Click += (_, _) => MoveComponent(+1);
        btnPanel.Controls.AddRange(new Control[] { addBtn, duplicateBtn, editBtn, removeBtn, scanBtn, upBtn, downBtn });

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_componentsGrid);
        left.Controls.Add(btnPanel);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_componentProps);
        p.Controls.Add(split);
        return p;
    }

    // 12.C.2: Prerequisites/Shortcuts/Registry used to hand-roll the same BindingSource + grid +
    // PropertyGrid + Add/Remove wiring BuildAdvancedResourceSection<T> already provides for the 17
    // typed-resource sections -- same collection type (ObservableCollection<T>), same reflection-based
    // clone already generic over T. Reusing it here removes ~90 lines of duplicate wiring and fixes
    // two latent gaps those hand-rolled copies had: AutoGenerateColumns with no list-property hiding
    // (Prerequisite/ShortcutDefinition/RegistryOperation could have rendered a raw
    // "ObservableCollection`1" column exactly like the Components section's own doc comment warns
    // about) and no DataError handler (a bad cell edit could throw instead of being ignored).
    private Panel BuildPrerequisitesSection()
        => BuildAdvancedResourceSection(
            C("Nav_Prerequisites", "Prerequisites"),
            C("Prereq_Description", "Runtime packages and dependencies to check or install before this app."),
            _project.Prerequisites,
            () => new Prerequisite { Id = $"prereq{_project.Prerequisites.Count + 1}", Name = "New prerequisite" });

    private Panel BuildShortcutsSection()
        => BuildAdvancedResourceSection(
            C("Nav_Shortcuts", "Shortcuts"),
            C("Shortcuts_Description", "Desktop, Start Menu and Startup shortcuts created at install."),
            _project.Shortcuts,
            () => new ShortcutDefinition { Name = _project.AppName });

    private Panel BuildRegistrySection()
        => BuildAdvancedResourceSection(
            C("Nav_Registry", "Registry"),
            C("Registry_Description", "Registry keys and values written at install."),
            _project.RegistryEntries,
            () => new RegistryOperation { KeyPath = $@"SOFTWARE\{_project.AppPublisher}\{_project.AppName}", ValueName = "Version", Value = _project.AppVersion });

    private Panel BuildScheduledTasksSection()
        => BuildAdvancedResourceSection(
            C("Section_ScheduledTasks", "Scheduled Tasks"),
            C("Section_ScheduledTasksHelp", "Create Task Scheduler entries with trigger, elevation, working-directory, credential and uninstall behavior."),
            _project.ScheduledTasks,
            () => new ScheduledTaskDefinition
            {
                Name = $"{SafeIdentifier(_project.AppName)} Task",
                Description = $"Runs {_project.AppName}.",
                ExecutablePath = DefaultMainExecutableReference(),
                WorkingDirectory = "{app}",
                Trigger = ScheduledTaskTrigger.OnLogon,
                Enabled = true,
                StopOnUninstall = true
            }, openRecipe: () => ShowResourceRecipe(ResourceRecipeKind.ScheduledTask));

    // ── Sections for collections that had no UI at all ──────────────────────
    //
    // Every one of these round-trips through the serializer and is honoured at install time; none
    // of them had anywhere to be authored, so the only way to use them was to hand-edit the
    // .bsetup. A model capability with no way to reach it is indistinguishable from a missing
    // feature for anyone using the tool.
    //
    // Defaults are seeded from the project rather than left blank: an empty row tells the author
    // nothing about what the field wants, and the shape of a plausible value does.

    private Panel BuildEnvironmentVariablesSection()
        => BuildAdvancedResourceSection(
            C("Section_EnvironmentVariables", "Environment Variables"),
            C("Section_EnvironmentVariablesHelp",
                "Variables written at install and removed at uninstall. Use {app} to refer to the install folder."),
            _project.EnvironmentVariables,
            () => new EnvironmentVariableOp
            {
                Name = SafeIdentifier(_project.AppName).ToUpperInvariant() + "_HOME",
                Value = "{app}"
            });

    private Panel BuildFileAssociationsSection()
        => BuildAdvancedResourceSection(
            C("Section_FileAssociations", "File Associations"),
            C("Section_FileAssociationsHelp",
                "File types this application opens. The ProgId must be unique to the product, or another "
                + "installer's uninstall can take the association with it."),
            _project.FileAssociations,
            () => new FileAssociationDefinition
            {
                Extension = ".myapp",
                ProgId = $"{SafeIdentifier(_project.AppName)}.Document",
                Description = $"{_project.AppName} document",
                ExecutablePath = DefaultMainExecutableReference(),
                Arguments = "\"%1\""
            });

    private Panel BuildWindowsServicesSection()
        => BuildAdvancedResourceSection(
            C("Section_WindowsServices", "Windows Services"),
            C("Section_WindowsServicesHelp",
                "Services registered at install. A service needs a per-machine install: it cannot be "
                + "registered from a per-user one."),
            _project.WindowsServices,
            () => new WindowsServiceDefinition
            {
                Name = SafeIdentifier(_project.AppName) + "Svc",
                DisplayName = $"{_project.AppName} Service",
                Description = $"Background service for {_project.AppName}.",
                ExecutablePath = DefaultMainExecutableReference(),
                StartAfterInstall = true
            }, openRecipe: () => ShowResourceRecipe(ResourceRecipeKind.WindowsService));

    private Panel BuildUpdateChannelsSection()
        => BuildAdvancedResourceSection(
            C("Section_UpdateChannels", "Update Channels"),
            C("Section_UpdateChannelsHelp",
                "Named release rings and their feeds. Rollout percentage staggers who is offered a build; "
                + "the feed URL is what installed clients actually check."),
            _project.UpdateChannels,
            () => new UpdateChannelDefinition
            {
                Id = "stable",
                Name = "Stable",
                Ring = "stable",
                FeedUrl = _project.AppUpdatesURL,
                RolloutPercentage = 100
            });

    private Panel BuildPackagesSection()
        => BuildAdvancedResourceSection(
            C("Section_Packages", "Package Nodes"),
            C("Section_PackagesHelp",
                "Sub-packages composed into this installer, from a local path or a download URL."),
            _project.Packages,
            () => new PackageNodeDefinition
            {
                Id = SafeIdentifier(_project.AppName).ToLowerInvariant() + "-core",
                Name = $"{_project.AppName} core"
            });

    private Panel BuildPrerequisiteCatalogsSection()
        => BuildAdvancedResourceSection(
            C("Section_PrerequisiteCatalogs", "Prerequisite Catalogs"),
            C("Section_PrerequisiteCatalogsHelp",
                "Signed catalogs of prerequisite definitions. A catalog without a trusted key is read but "
                + "not trusted, so pin the publisher key you expect."),
            _project.PrerequisiteCatalogs,
            () => new PrerequisiteCatalogReference { Required = false });

    private Panel BuildMsixOptionalPackagesSection()
        => BuildAdvancedResourceSection(
            C("Section_MsixOptionalPackages", "MSIX Optional Packages"),
            C("Section_MsixOptionalPackagesHelp",
                "Optional packages that join this application's MSIX family. Publisher must match the main "
                + "package exactly or Windows refuses the relationship."),
            _project.MsixOptionalPackages,
            () => new MsixOptionalPackageDefinition
            {
                Name = SafeIdentifier(_project.AppName) + ".Optional",
                Publisher = _project.MsixPublisher,
                Version = _project.AppVersion
            });

    private Panel BuildDeploymentSupersedenceSection()
        => BuildAdvancedResourceSection(
            C("Section_Supersedence", "Supersedence"),
            C("Section_SupersedenceHelp",
                "Earlier products this installer replaces, for Intune and ConfigMgr. The version range decides "
                + "which installs are superseded rather than left alongside."),
            _project.DeploymentSupersedence,
            () => new DeploymentSupersedenceRule
            {
                PackageId = _project.AppId,
                DisplayName = _project.AppName,
                MinimumVersion = "0.0.0",
                MaximumVersion = _project.AppVersion
            });

    private Panel BuildResourcesSection()
        => BuildAdvancedResourceSection(
            C("Section_Resources", "Typed Resources"),
            C("Section_ResourcesHelp",
                "Operations executed by the typed resource providers at install time. These need explicit "
                + "extension directories and the EXE host — a build refuses otherwise, because MSIX cannot "
                + "execute providers."),
            _project.Resources,
            () => new Beep.Installer.Engine.CompiledInstallOperation
            {
                Id = $"file.copy:{_project.Resources.Count + 1}",
                Type = "file.copy",
                DisplayName = L("Section_ResourcesDefaultName", "Copy a file"),
                RollbackSupported = true
            },
            createGuided: () =>
            {
                using var wizard = new ResourceWizard(_project);
                return wizard.ShowDialog(ActionOwner) == DialogResult.OK ? wizard.Result : null;
            },
            editGuided: current =>
            {
                if (!Beep.Installer.Extensibility.ResourceInputCatalog.TryGet(current.Type, out _))
                {
                    MessageBox.Show(ActionOwner,
                        string.Format(L("Builder_NoDescriptor",
                            "'{0}' has no described inputs, so it can only be edited in the grid."), current.Type),
                        L("Section_Resources", "Typed Resources"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return null;
                }

                using var wizard = new ResourceWizard(_project, current);
                return wizard.ShowDialog(ActionOwner) == DialogResult.OK ? wizard.Result : null;
            });

    private Panel BuildCustomPagesSection()
        => BuildAdvancedResourceSection(
            C("Section_CustomPages", "Custom Wizard Pages"),
            C("Section_CustomPagesHelp",
                "Extra pages shown during install, with fields whose answers reach the install context. "
                + "Order decides where a page appears in the wizard."),
            _project.CustomPages,
            () => new CustomWizardPage
            {
                Id = "page" + (_project.CustomPages.Count + 1),
                Title = L("Section_CustomPageTitle", "Additional options"),
                Order = _project.CustomPages.Count + 1
            });

    private Panel BuildFirewallRulesSection()
        => BuildAdvancedResourceSection(
            C("Section_FirewallRules", "Firewall Rules"),
            C("Section_FirewallRulesHelp", "Author Windows Firewall rules for ports, programs, services and profile scope."),
            _project.FirewallRules,
            () => new FirewallRuleDefinition
            {
                Name = $"{_project.AppName} inbound",
                Description = $"Allows inbound traffic for {_project.AppName}.",
                Direction = FirewallRuleDirection.In,
                Action = FirewallRuleAction.Allow,
                Protocol = FirewallRuleProtocol.Tcp,
                LocalPort = "443",
                Program = DefaultMainExecutableReference(),
                Profile = "domain,private",
                Enabled = true,
                RemoveOnUninstall = true
            }, openRecipe: () => ShowResourceRecipe(ResourceRecipeKind.FirewallRule));

    private Panel BuildCertificatesSection()
        => BuildAdvancedResourceSection(
            C("Section_Certificates", "Certificates"),
            C("Section_CertificatesHelp", "Install certificate assets into the Windows certificate store with thumbprint and rollback ownership controls."),
            _project.Certificates,
            () => new CertificateDefinition
            {
                SourcePath = "{app}\\certificates\\certificate.cer",
                StoreName = "My",
                StoreLocation = InstallationScope.Machine,
                RemoveOnUninstall = true
            });

    private Panel BuildComRegistrationsSection()
        => BuildAdvancedResourceSection(
            C("Section_ComRegistrations", "COM Registrations"),
            C("Section_ComRegistrationsHelp", "Register COM CLSID/ProgID metadata against in-process or local-server binaries."),
            _project.ComRegistrations,
            () => new ComRegistrationDefinition
            {
                Description = $"{_project.AppName} COM server",
                ServerPath = DefaultMainExecutableReference(),
                ServerType = ComServerType.LocalServer,
                ThreadingModel = "Both",
                RemoveOnUninstall = true
            });

    private Panel BuildDriverPackagesSection()
        => BuildAdvancedResourceSection(
            C("Section_DriverPackages", "Driver Packages"),
            C("Section_DriverPackagesHelp", "Stage driver INF/sys packages with service identity, signing requirements and reboot behavior."),
            _project.DriverPackages,
            () => new DriverPackageDefinition
            {
                Name = $"{SafeIdentifier(_project.AppName)}Driver",
                Kind = DriverPackageKind.Pnp,
                InfPath = "{app}\\drivers\\driver.inf",
                RequireSigned = true,
                RemoveOnUninstall = true,
                RebootBehavior = DriverPackageRebootBehavior.Possible
            });

    private Panel BuildConfigTransformsSection()
        => BuildAdvancedResourceSection(
            C("Section_ConfigTransforms", "Config Transforms"),
            C("Section_ConfigTransformsHelp", "Apply JSON/XML/INI configuration changes with install backup and rollback restore controls."),
            _project.ConfigTransforms,
            () => new ConfigTransformDefinition
            {
                Name = "Set app setting",
                TargetPath = "{app}\\appsettings.json",
                Format = ConfigTransformFormat.Json,
                Operation = ConfigTransformOperation.Set,
                KeyPath = "Logging:LogLevel:Default",
                Value = "Information",
                BackupOnInstall = true,
                RestoreOnRollback = true
            });

    private Panel BuildIisAppPoolsSection()
        => BuildAdvancedResourceSection(
            C("Section_IisAppPools", "IIS App Pools"),
            C("Section_IisAppPoolsHelp", "Define IIS application-pool runtime, pipeline mode, identity, autostart and ownership."),
            _project.IisAppPools,
            () => new IisAppPoolDefinition
            {
                Name = SafeIdentifier(_project.AppName),
                RuntimeVersion = "v4.0",
                PipelineMode = IisManagedPipelineMode.Integrated,
                Identity = "ApplicationPoolIdentity",
                AutoStart = true,
                StartAfterInstall = true,
                RemoveOnUninstall = true
            }, openRecipe: () => ShowResourceRecipe(ResourceRecipeKind.IisSite));

    private Panel BuildIisSitesSection()
        => BuildAdvancedResourceSection(
            C("Section_IisSites", "IIS Sites"),
            C("Section_IisSitesHelp", "Define IIS site physical path, application pool, binding collection and uninstall ownership."),
            _project.IisSites,
            () => new IisSiteDefinition
            {
                Name = _project.AppName,
                PhysicalPath = "{app}\\wwwroot",
                ApplicationPool = SafeIdentifier(_project.AppName),
                StartAfterInstall = true,
                RemoveOnUninstall = true,
                Bindings =
                {
                    new IisBindingDefinition { Protocol = IisBindingProtocol.Http, IpAddress = "*", Port = 80 }
                }
            }, openRecipe: () => ShowResourceRecipe(ResourceRecipeKind.IisSite));

    private Panel BuildWebDeployPackagesSection()
        => BuildAdvancedResourceSection(
            C("Section_WebDeploy", "Web Deploy Packages"),
            C("Section_WebDeployHelp", "Attach msdeploy packages to an IIS site target with package parameters and uninstall ownership."),
            _project.WebDeployPackages,
            () => new WebDeployPackageDefinition
            {
                Name = $"{SafeIdentifier(_project.AppName)}WebDeploy",
                PackagePath = "{app}\\deploy\\package.zip",
                SiteName = _project.AppName,
                Destination = "auto",
                RemoveOnUninstall = true
            });

    private Panel BuildAdvancedResourceSection<T>(
        LiveUiCaption title,
        LiveUiCaption description,
        System.Collections.ObjectModel.ObservableCollection<T> collection,
        Func<T> createDefault,
        Func<T?>? createGuided = null,
        Func<T, T?>? editGuided = null,
        Func<bool>? openRecipe = null)
        where T : class
        => ResourceSectionFactory.Create(title, description, collection, createDefault,
            OnAuthoringResourceChanged, CloneAdvancedResource, createGuided, editGuided, openRecipe, _controller.RunAuthoringChange);
    private static T CloneAdvancedResource<T>(T source) where T : class
        => System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(source))
            ?? throw new InvalidOperationException("The resource could not be copied.");

    private string DefaultMainExecutableReference()
    {
        var exe = string.IsNullOrWhiteSpace(_project.MainExecutable)
            ? $"{SafeIdentifier(_project.AppName)}.exe"
            : Path.GetFileName(_project.MainExecutable);
        return "{app}\\" + exe;
    }

    private static string SafeIdentifier(string value)
    {
        var chars = (string.IsNullOrWhiteSpace(value) ? "App" : value)
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return chars.Length == 0 ? "App" : new string(chars);
    }

    private Panel BuildBrandingSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddBoundRow(layout, C("Field_WindowTitle", "Window title:"),        proj, nameof(InstallProject.WindowTitle));
        AddBoundRow(layout, C("Field_WelcomeTitle", "Welcome title:"),       proj, nameof(InstallProject.WelcomeTitle));
        AddEnumRow<WizardTheme>(layout, C("Field_Theme", "Theme:"), proj, nameof(InstallProject.DefaultTheme));
        AddBoundRow(layout, C("Field_SidebarBackground", "Sidebar background:"),  proj, nameof(InstallProject.SidebarBackgroundColor));
        AddBoundRow(layout, C("Field_SidebarText", "Sidebar text:"),        proj, nameof(InstallProject.SidebarTextColor));
        AddBoundRow(layout, C("Field_AccentColor", "Accent color:"),        proj, nameof(InstallProject.AccentColor));
        AddCheckRow(layout, C("Field_AllowComponentSelection", "Allow component selection:"), proj, nameof(InstallProject.AllowComponentSelection));
        AddCheckRow(layout, C("Field_AllowPathChange", "Allow path change:"),        proj, nameof(InstallProject.AllowPathChange));

        // Live swatch. Ctrl+P has always opened the full wizard preview, but colours are the one
        // thing you tune by iterating -- typing a hex value, looking, adjusting -- and a modal you
        // have to open and close for each attempt is the wrong shape for that.
        _brandingPreview = new Panel { Dock = DockStyle.Bottom, Height = 132, Padding = new Padding(0, 8, 0, 0) };
        _brandingPreview.Paint += (_, e) => PaintBrandingPreview(e.Graphics, _brandingPreview!.ClientRectangle);
        p.Controls.Add(layout);
        p.Controls.Add(_brandingPreview);
        return p;
    }

    /// <summary>
    /// The wizard-page ids this checklist offers, in wizard order, with their display labels.
    /// The label is what the author reads; the id is what goes into <c>[WizardPages]</c>.
    /// </summary>
    private static readonly (string Id, string Label)[] WizardPageChoices =
    {
        (WizardPageIds.Welcome,         "Welcome"),
        (WizardPageIds.License,         "License (EULA)"),
        (WizardPageIds.Prerequisites,   "Prerequisites"),
        (WizardPageIds.Components,      "Component Selection"),
        (WizardPageIds.Folder,          "Destination Folder"),
        (WizardPageIds.StartMenu,       "Start Menu Folder"),
        (WizardPageIds.AdditionalTasks, "Additional Tasks"),
        (WizardPageIds.Ready,           "Ready (review)"),
        (WizardPageIds.Progress,        "Progress (installing)"),
        (WizardPageIds.Complete,        "Complete (launch / log)")
    };

    private Panel BuildWizardPagesSection()
    {
        // This list used to hard-check every box, ignore the project entirely, and answer a click by
        // marking the document dirty -- so it recorded nothing and drove nothing. It is now the
        // editor for InstallProject.EnabledWizardPages.
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        _wizardPagesList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        _wizardPagesList.Items.AddRange(WizardPageChoices.Select(c => (object)c.Label).ToArray());

        LoadWizardPageChecks();

        _wizardPagesList.ItemCheck += (_, e) =>
        {
            if (_loadingWizardPages) return;

            // ItemCheck fires before the control updates, so read the new value from the event and
            // let the control settle before writing the model back.
            var index = e.Index;
            var isChecked = e.NewValue == CheckState.Checked;
            if (WizardPageIds.Structural.Contains(WizardPageChoices[index].Id) && !isChecked)
            {
                // The wizard builds these regardless; letting the box clear would claim otherwise.
                e.NewValue = CheckState.Checked;
                return;
            }

            BeginInvoke((Action)(() => SaveWizardPageChecks()));
        };

        p.Controls.Add(_wizardPagesList);
        return p;
    }

    /// <summary>Reflects the project's enabled-page list into the checklist.</summary>
    private void LoadWizardPageChecks()
    {
        if (_wizardPagesList is null || _wizardPagesList.IsDisposed) return;

        _loadingWizardPages = true;
        try
        {
            for (var i = 0; i < WizardPageChoices.Length; i++)
                _wizardPagesList.SetItemChecked(i, WizardPageIds.IsEnabled(_project, WizardPageChoices[i].Id));
        }
        finally
        {
            _loadingWizardPages = false;
        }
    }

    /// <summary>
    /// Writes the checklist back to the project. An all-checked list is stored as an empty
    /// collection so the serializer omits the <c>[WizardPages]</c> section entirely and a default
    /// project keeps producing the same script it always did.
    /// </summary>
    private void SaveWizardPageChecks()
    {
        if (_wizardPagesList is null || _wizardPagesList.IsDisposed) return;

        var enabled = new List<string>();
        for (var i = 0; i < WizardPageChoices.Length; i++)
            if (_wizardPagesList.GetItemChecked(i))
                enabled.Add(WizardPageChoices[i].Id);

        _controller.RunAuthoringChange("Wizard pages", () =>
        {
            _project.EnabledWizardPages.Clear();
            if (enabled.Count != WizardPageChoices.Length)
                foreach (var id in enabled)
                    _project.EnabledWizardPages.Add(id);

            OnAuthoringResourceChanged();
        });
    }

    private Panel BuildScriptSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = PanelBackColor };
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            BackColor = PanelBackColor,
            Padding = new Padding(0, 0, 0, 8),
        };
        var applyBtn = new Button { Text = L("Builder_ApplyScript", "Apply Script"), Width = 110, Height = 30 };
        var reloadBtn = new Button { Text = L("Builder_ReloadFromFields", "Reload From Fields"), Width = 140, Height = 30 };
        applyBtn.Name = "ApplyScript"; reloadBtn.Name = "ReloadScript";
        applyBtn.AutoSize = reloadBtn.AutoSize = true;
        C("Builder_ApplyScript", "Apply Script").Bind(applyBtn); C("Builder_ReloadFromFields", "Reload From Fields").Bind(reloadBtn);
        var status = new Label
        {
            Name = "ScriptEditorStatus",
            AutoSize = true,
            Text = "",
            ForeColor = MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 7, 0, 0),
        };
        applyBtn.Click += (_, _) => ApplyScriptEditor(showSuccess: true);
        reloadBtn.Click += (_, _) => RefreshScriptPreview(force: true);
        top.Controls.AddRange(new Control[] { applyBtn, reloadBtn, status });

        _scriptPreviewBox = new TextBox
        {
            Name = "ScriptEditor",
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = false,
            AcceptsTab = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(250, 250, 250)
        };
        _scriptPreviewBox.TextChanged += (_, _) =>
        {
            if (_updatingScriptEditor) return;
            _scriptEditorDirty = true;
            OnControllerPropertyChanged(_controller, new PropertyChangedEventArgs(nameof(InstallerController.SaveState)));
            if (FindNamed<Label>(p, "ScriptEditorStatus") is { } label)
                label.Text = L("Builder_Modified", "Modified");
        };
        C("Help_Script", "Script").Bind(_scriptPreviewBox, text: false);
        p.Controls.Add(_scriptPreviewBox);
        p.Controls.Add(top);
        return p;
    }

    private Panel BuildWorkflowSection()
    {
        var p = NewSectionPanel();
        p.Padding = new Padding(18);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = PanelBackColor,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = PanelBackColor,
            Margin = new Padding(0, 0, 0, 12),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label
        {
            Name = "BuildWorkflowTitle",
            Text = L("Builder_BuildWorkflow", "Build Workflow"),
            AutoSize = true,
            Font = SectionTitleFont,
            ForeColor = TextColor,
            Margin = new Padding(0, 0, 0, 4),
        }, 0, 0);
        C("Builder_BuildWorkflow", "Build Workflow").Bind(header.Controls[0]);
        var buildNow = new Button { Text = L("Builder_BuildSetupExe", "Build Setup.exe"), AutoSize = true, Height = 34, Padding = new Padding(14, 0, 14, 0), Margin = new Padding(12, 0, 0, 0) };
        buildNow.Name = "BuildWorkflowAction"; C("Builder_BuildTitle", "Build").Bind(buildNow);
        StylePrimaryButton(buildNow);
        buildNow.Click += (_, _) => BuildInstaller();
        header.Controls.Add(buildNow, 1, 0);
        layout.Controls.Add(header, 0, layout.RowCount++);

        var workspaceGroup = new GroupBox { Text = L("Workspace_Title", "Build workspace"), Dock = DockStyle.Top,
            Height = 420, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(8) };
        workspaceGroup.Controls.Add(new BuildWorkspaceView(_buildWorkspace, info => ArtifactLauncher(info)));
        C("Workspace_Title", "Build workspace").Bind(workspaceGroup);
        layout.Controls.Add(workspaceGroup, 0, layout.RowCount++);

        layout.Controls.Add(BuildWorkflowGroup(C("Nav_Output", "Output"), workflow =>
        {
            AddFolderRow(workflow, C("Field_OutputDirectory", "Output directory:"), _project, nameof(InstallProject.OutputDir));
        }), 0, layout.RowCount++);

        var keyboardGroup = BuildWorkflowTextGroup(C("Builder_KeyboardTitle", "Keyboard walkthrough"), 150, out _keyboardWalkthroughBox);
        _keyboardWalkthroughBox.ReadOnly = true;
        _keyboardWalkthroughBox.Text = KeyboardWalkthroughText();
        layout.Controls.Add(keyboardGroup, 0, layout.RowCount++);

        var validationGroup = BuildWorkflowTextGroup(C("Builder_ValidationTitle", "Validation center"), 190, out _validationCenterBox);
        _validationCenterHasReport = false;
        _validationCenterBox.ReadOnly = true;
        _validationCenterBox.Text = L("Builder_ValidationCenterIsReadyClick", "Validation Center is ready. Click Validate Project to see grouped schema, semantic and resource diagnostics.");
        layout.Controls.Add(validationGroup, 0, layout.RowCount++);

        var capabilityGroup = BuildWorkflowTextGroup(C("Builder_FormatReadinessTitle", "Format readiness"), 190, out _formatCapabilityBox);
        _formatCapabilityHasReport = false;
        _formatCapabilityBox.ReadOnly = true;
        _formatCapabilityBox.Text = L("Builder_ReleaseReadinessPendingClickRefresh", "Release readiness: pending. Click Refresh Format Readiness to see EXE, MSI, MSIX, WinGet and Intune/ConfigMgr capability status.");
        var capabilityActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 0, 0, 6)
        };
        var refreshCapability = new Button { Name = "RefreshFormatReadinessButton", Text = L("Builder_RefreshFormatReadiness", "Refresh Format Readiness"), AutoSize = true, Height = 28, Padding = new Padding(12, 0, 12, 0) };
        C("Builder_RefreshFormatReadiness", "Refresh Format Readiness").Bind(refreshCapability);
        capabilityActions.AutoSize = true; capabilityActions.WrapContents = true;
        refreshCapability.Click += (_, _) => RefreshFormatCapabilityReport();
        capabilityActions.Controls.Add(refreshCapability);
        capabilityGroup.Controls.Add(capabilityActions);
        capabilityActions.BringToFront();
        layout.Controls.Add(capabilityGroup, 0, layout.RowCount++);

        var logGroup = BuildWorkflowTextGroup(C("Help_Log", "Build Log"), 180, out _buildLogBox);
        _buildLogBox.Name = "WorkflowBuildLog";
        _buildLogBox.ReadOnly = true;
        layout.Controls.Add(logGroup, 0, layout.RowCount++);

        layout.Controls.Add(BuildWorkflowResultGroup(), 0, layout.RowCount++);

        p.Controls.Add(layout);
        return p;
    }

    private static GroupBox BuildWorkflowGroup(LiveUiCaption title, Action<TableLayoutPanel> build)
    {
        var group = new GroupBox
        {
            Name = title.Key ?? "WorkflowGroup",
            Text = title.Value,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Font = UiFontBold,
            ForeColor = TextColor,
            Padding = new Padding(10, 8, 10, 10),
            Margin = new Padding(0, 0, 0, 12),
        };
        title.Bind(group);
        var table = NewTwoColTable();
        table.Padding = new Padding(8, 10, 8, 8);
        build(table);
        group.Controls.Add(table);
        return group;
    }

    private static GroupBox BuildWorkflowTextGroup(LiveUiCaption title, int height, out TextBox textBox)
    {
        var group = new GroupBox
        {
            Name = title.Key ?? "WorkflowTextGroup",
            Text = title.Value,
            Dock = DockStyle.Top,
            Height = height,
            Font = UiFontBold,
            ForeColor = TextColor,
            Padding = new Padding(10, 8, 10, 10),
            Margin = new Padding(0, 0, 0, 12),
        };
        textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(248, 250, 252),
        };
        group.Controls.Add(textBox);
        title.Bind(group); title.Bind(textBox, text: false);
        return group;
    }

    private GroupBox BuildWorkflowResultGroup()
    {
        var group = new GroupBox
        {
            Name = "WorkflowResultGroup",
            Text = L("Builder_Result", "Result"),
            Dock = DockStyle.Top,
            Height = 150,
            Font = UiFontBold,
            ForeColor = TextColor,
            Padding = new Padding(10, 10, 10, 10),
            Margin = new Padding(0, 0, 0, 12),
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var status = new Label { Name = "WorkflowResultStatus", Dock = DockStyle.Fill, Text = L("Builder_NoBuildHasBeenRun", "No build has been run yet."), Font = UiFontBold, ForeColor = MutedTextColor, TextAlign = ContentAlignment.MiddleLeft };
        var path = new TextBox { Name = "WorkflowResultPath", Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(248, 250, 252), Font = new Font("Consolas", 9) };
        var copy = new Button { Name = "WorkflowCopyPathBtn", Text = L("Builder_Copy", "Copy"), Width = 70, Enabled = false };
        C("Builder_Result", "Result").Bind(group); C("Builder_Copy", "Copy").Bind(copy);
        C("BuildResult_Artifact", "Output artifact:").Bind(path, text: false);
        copy.AutoSize = true;
        copy.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(path.Text)) Clipboard.SetText(path.Text); };
        var details = new Label { Name = "WorkflowResultDetails", Dock = DockStyle.Fill, ForeColor = MutedTextColor, TextAlign = ContentAlignment.TopLeft };

        layout.Controls.Add(status, 0, 0);
        layout.SetColumnSpan(status, 2);
        layout.Controls.Add(path, 0, 1);
        layout.Controls.Add(copy, 1, 1);
        layout.Controls.Add(details, 0, 2);
        layout.SetColumnSpan(details, 2);
        group.Controls.Add(layout);
        return group;
    }

    private Panel BuildOutputSection()
    {
        var p = NewSectionPanel();
        var layout = NewTwoColTable();
        var proj = _project;
        AddFolderRow(layout, C("Field_OutputDirectory", "Output directory:"),      proj, nameof(InstallProject.OutputDir));
        AddBoundRow(layout, C("Field_SetupFileName", "Setup file name:"),       proj, nameof(InstallProject.OutputBaseFilename));
        AddBoundRow(layout, C("Field_MainExecutable", "Main executable:"),       proj, nameof(InstallProject.MainExecutable));
        AddEnumRow<Architecture>(layout, C("Field_TargetArchitecture", "Target architecture:"), proj, nameof(InstallProject.ArchitecturesAllowed));
        AddCheckRow(layout, C("Field_RegisterUninstallEntry", "Register uninstall entry"), proj, nameof(InstallProject.CreateUninstallEntry));
        AddCheckRow(layout, C("Field_CreateSystemRestorePoint", "Create system restore point"), proj, nameof(InstallProject.CreateRestorePoint));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildPayloadSection()
    {
        var p = NewSectionPanel();
        var layout = NewTwoColTable();
        var proj = _project;
        AddBoundRow(layout, C("Field_PayloadFolder", "Payload folder:"),       proj, nameof(InstallProject.PayloadFolderName));
        AddEnumRow<PayloadSourceType>(layout, C("Field_PayloadSource", "Payload source:"), proj, nameof(InstallProject.PayloadSource));
        AddBoundRow(layout, C("Field_PayloadURL", "Payload URL:"),          proj, nameof(InstallProject.PayloadUrl));
        AddCheckRow(layout, C("Field_CompressPayload", "Compress payload"),       proj, nameof(InstallProject.CompressPayload));
        AddEnumRow<CompressionFormat>(layout, C("Field_CompressionFormat", "Compression format:"), proj, nameof(InstallProject.Compression));
        AddEnumRow<CompressionStrength>(layout, C("Field_CompressionStrength", "Compression strength:"), proj, nameof(InstallProject.CompressionLevel));
        AddCheckRow(layout, C("Field_SolidCompression", "Solid compression"),      proj, nameof(InstallProject.SolidCompression));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildPackageSection()
    {
        var p = NewSectionPanel();
        var layout = NewTwoColTable();
        var proj = _project;
        AddSectionRow(layout, C("Section_CodeSigning", "Code signing"));
        AddFileRow(layout, C("Field_CertificatePfx", "Certificate (.pfx):"), proj, nameof(InstallProject.CodeSignCertificatePath), "*.pfx");
        AddBoundRow(layout, C("Field_CertificatePassword", "Certificate password:"), proj, nameof(InstallProject.CodeSignCertificatePassword));
        AddBoundRow(layout, C("Field_TimestampURL", "Timestamp URL:"), proj, nameof(InstallProject.CodeSignTimestampUrl));
        AddSectionRow(layout, C("Section_OptionalPackageMetadata", "Optional package metadata"));
        AddEnumRow<InstallerOutputFormat>(layout, C("Field_OutputFormat", "Output format:"), proj, nameof(InstallProject.OutputFormat));
        var helpRow = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var packageHelp = new Label
        {
            Name = "PackageHelp",
            Text = L("Guided_PackageHelp", "EXE provides a full installer wizard. MSI uses Windows Installer and requires WiX to build. MSIX uses package identity and cannot carry classic system changes. ClickOnce publishes the source folder and update manifests."),
            AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = MutedTextColor
        };
        C("Guided_PackageHelp", "EXE provides a full installer wizard. MSI uses Windows Installer and requires WiX to build. MSIX uses package identity and cannot carry classic system changes. ClickOnce publishes the source folder and update manifests.").Bind(packageHelp);
        layout.Controls.Add(packageHelp, 1, helpRow);
        AddBoundRow(layout, C("Field_UpdateURL", "Update URL:"), proj, nameof(InstallProject.AppUpdatesURL));
        AddBoundRow(layout, C("Field_MSIXIdentity", "MSIX identity:"), proj, nameof(InstallProject.MsixIdentity));
        AddBoundRow(layout, C("Field_MSIXPublisher", "MSIX publisher:"), proj, nameof(InstallProject.MsixPublisher));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildCompressionSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddCheckRow(layout, C("Field_CompressPayload", "Compress payload"),       proj, nameof(InstallProject.CompressPayload));
        AddEnumRow<CompressionFormat>(layout, C("Field_CompressionFormat", "Compression format:"), proj, nameof(InstallProject.Compression));
        AddCheckRow(layout, C("Field_SolidCompression", "Solid compression"),      proj, nameof(InstallProject.SolidCompression));
        AddEnumRow<CompressionStrength>(layout, C("Field_CompressionStrength", "Compression strength:"), proj, nameof(InstallProject.CompressionLevel));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildCodeSignSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddFileRow(layout, C("Field_CertificatePfx", "Certificate (.pfx):"),    proj, nameof(InstallProject.CodeSignCertificatePath), "*.pfx");
        AddBoundRow(layout, C("Field_CertificatePassword", "Certificate password:"),   proj, nameof(InstallProject.CodeSignCertificatePassword));
        AddBoundRow(layout, C("Field_TimestampURL", "Timestamp URL:"),          proj, nameof(InstallProject.CodeSignTimestampUrl));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildMsixSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddBoundRow(layout, C("Field_MSIXIdentity", "MSIX identity:"),    proj, nameof(InstallProject.MsixIdentity));
        AddBoundRow(layout, C("Field_MSIXPublisher", "MSIX publisher:"),   proj, nameof(InstallProject.MsixPublisher));
        AddBoundRow(layout, C("Field_UpdateURL", "Update URL:"),       proj, nameof(InstallProject.AppUpdatesURL));
        AddEnumRow<UpdateMode>(layout, C("Field_UpdateMode", "Update mode:"), proj, nameof(InstallProject.AppUpdateMode));
        AddBoundRow(layout, C("Field_UpdateCheckHours", "Update check hours:"), proj, nameof(InstallProject.AppInstallerHoursBetweenUpdateChecks));
        AddCheckRow(layout, C("Field_PromptUsersBeforeUpdate", "Prompt users before update"), proj, nameof(InstallProject.AppInstallerShowPrompt));
        AddCheckRow(layout, C("Field_AllowDowngradeForceUpdate", "Allow downgrade/force update"), proj, nameof(InstallProject.AppInstallerForceUpdateFromAnyVersion));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildLogSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        var topBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            FlowDirection = FlowDirection.RightToLeft,
        };
        var clearBtn = new Button { Text = L("Builder_Clear", "Clear"), Width = 70, Height = 28 };
        clearBtn.Click += (_, _) => _buildLogBox?.Clear();
        var saveBtn = new Button { Text = L("Builder_SaveAs", "Save As…"), Width = 90, Height = 28 };
        saveBtn.Click += (_, _) =>
        {
            if (_buildLogBox == null) return;
            using var dlg = new SaveFileDialog { Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*", FileName = $"build-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
            if (dlg.ShowDialog(ActionOwner) != DialogResult.OK) return;
            try { File.WriteAllText(dlg.FileName, _buildLogBox.Text); }
            catch (Exception ex) { MessageBox.Show(ActionOwner, ex.Message, L("Common_SaveFailed", "Save failed"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        var copyBtn = new Button { Text = L("Builder_CopyAll", "Copy All"), Width = 90, Height = 28 };
        copyBtn.Click += (_, _) =>
        {
            if (_buildLogBox == null) return;
            try { Clipboard.SetText(_buildLogBox.Text); copyBtn.Text = L("Builder_Copied", "Copied!"); }
            catch (Exception ex) { MessageBox.Show(ActionOwner, ex.Message, L("Common_CopyFailed", "Copy failed"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        var selectAllBtn = new Button { Text = L("Builder_SelectAll", "Select All"), Width = 90, Height = 28 };
        topBar.AutoSize = true; topBar.WrapContents = true;
        foreach (var (button, key, fallback) in new[] { (clearBtn, "Builder_Clear", "Clear"), (saveBtn, "Builder_SaveAs", "Save As…"),
            (copyBtn, "Builder_CopyAll", "Copy All"), (selectAllBtn, "Builder_SelectAll", "Select All") })
        { button.Name = key; button.AutoSize = true; C(key, fallback).Bind(button); }
        selectAllBtn.Click += (_, _) => { _buildLogBox?.Focus(); _buildLogBox?.SelectAll(); };
        topBar.Controls.AddRange(new Control[] { clearBtn, saveBtn, copyBtn, selectAllBtn });

        _buildLogBox = new TextBox
        {
            Name = "LegacyBuildLog",
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.LightGreen,
        };
        C("Help_Log", "Build Log").Bind(_buildLogBox, text: false);

        p.Controls.Add(_buildLogBox);
        p.Controls.Add(topBar);
        return p;
    }

    private Panel BuildResultSection()
    {
        // Inline result view that always shows the last build outcome, with actions.
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), AutoScroll = true, BackColor = Color.White };
        var titleLabel = new Label
        {
            Name = "ResultTitle",
            Text = L("Builder_LastBuildResult", "Last Build Result"),
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            Location = new Point(0, 0),
            AutoSize = true,
        };
        var statusBanner = new Label
        {
            Name = "StatusBanner",
            Text = L("Builder_NoBuildHasBeenRun", "No build has been run yet."),
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Location = new Point(0, 32),
            AutoSize = true,
        };
        var pathLabel = new Label { Name = "PathLabel", Text = "", Location = new Point(0, 64), AutoSize = true, ForeColor = Color.FromArgb(60, 60, 60) };
        var sizeLabel = new Label { Name = "SizeLabel", Text = "", Location = new Point(0, 84), AutoSize = true, ForeColor = Color.FromArgb(60, 60, 60) };
        var issueLabel = new Label { Name = "IssueLabel", Text = "", Location = new Point(0, 104), AutoSize = true, ForeColor = Color.FromArgb(60, 60, 60) };

        var pathRow = new TableLayoutPanel
        {
            Location = new Point(0, 130),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var pathLbl = new Label { Name = "ArtifactPathCaption", Text = L("BuildResult_Artifact", "Output artifact:"), Font = UiFontBold, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
        var pathBox = new TextBox { Name = "PathBox", Width = 400, ReadOnly = true, BackColor = Color.WhiteSmoke, Font = new Font("Consolas", 9) };
        var copyBtn = new Button { Text = L("Builder_Copy", "Copy"), Width = 70, Height = 26 };
        copyBtn.Name = "CopyPathBtn";
        copyBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(pathBox.Text))
            {
                try { Clipboard.SetText(pathBox.Text); copyBtn.Text = L("Builder_Copied", "Copied!"); }
                catch (Exception ex)
                {
                    // Do not claim success when the clipboard was locked by another process.
                    copyBtn.Text = L("Builder_CopyFailed", "Copy failed");
                    Engine.Diag.Warn("PackageBuilderForm", "clipboard copy failed", ex);
                }
            }
        };
        pathRow.Controls.Add(pathLbl, 0, 0);
        pathRow.Controls.Add(pathBox, 1, 0);
        pathRow.Controls.Add(copyBtn, 2, 0);

        var actionPanel = new FlowLayoutPanel
        {
            Location = new Point(0, 180),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
        };
        var openFolderBtn = new Button { Text = L("Builder_OpenFolder", "Open Folder"), Width = 130, Height = 36, Name = "OpenFolderBtn" };
        var runBtn = new Button { Text = L("Builder_RunInstaller", "Run Installer"), Width = 140, Height = 36, Name = "RunInstallerBtn" };
        var buildAnotherBtn = new Button { Text = L("Builder_BuildAgain", "Build Again"), Width = 130, Height = 36, Name = "BuildAgainBtn" };
        openFolderBtn.Click += (_, _) => LaunchCurrentArtifact(openFolder: true);
        runBtn.Click += (_, _) => LaunchCurrentArtifact(openFolder: false);
        buildAnotherBtn.Click += (_, _) => BuildInstaller();
        actionPanel.Controls.AddRange(new Control[] { openFolderBtn, runBtn, buildAnotherBtn });

        C("Builder_LastBuildResult", "Last Build Result").Bind(titleLabel);
        C("BuildResult_Artifact", "Output artifact:").Bind(pathLbl);
        C("BuildResult_Artifact", "Output artifact:").Bind(pathBox, text: false);
        C("Builder_Copy", "Copy").Bind(copyBtn);
        C("Builder_OpenFolder", "Open Folder").Bind(openFolderBtn);
        C("Builder_BuildAgain", "Build Again").Bind(buildAnotherBtn);
        var layout = new TableLayoutPanel { Name = "ResultLayout", Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var captions = new[] { titleLabel, statusBanner, pathLabel, sizeLabel, issueLabel };
        for (var i = 0; i < captions.Length; i++)
        {
            captions[i].Dock = DockStyle.Top; captions[i].AutoSize = true; captions[i].Margin = new Padding(0, 0, 0, InstallerTheme.FieldGap);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.Controls.Add(captions[i], 0, i);
        }
        pathRow.Dock = actionPanel.Dock = DockStyle.Top; actionPanel.WrapContents = true; pathBox.Dock = DockStyle.Fill;
        copyBtn.AutoSize = openFolderBtn.AutoSize = runBtn.AutoSize = buildAnotherBtn.AutoSize = true;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(pathRow, 0, 5); layout.Controls.Add(actionPanel, 0, 6);
        layout.SizeChanged += (_, _) => { foreach (var caption in captions) caption.MaximumSize = new Size(Math.Max(1, layout.ClientSize.Width - caption.Margin.Horizontal), 0); };
        p.Controls.Add(layout);
        PopulateResultSection();
        return p;
    }

    private void PopulateResultSection()
    {
        var panel = _contentResult;
        if (panel == null) return;
        var statusBanner = FindNamed<Label>(panel, "StatusBanner");
        var pathLabel = FindNamed<Label>(panel, "PathLabel");
        var sizeLabel = FindNamed<Label>(panel, "SizeLabel");
        var issueLabel = FindNamed<Label>(panel, "IssueLabel");
        var pathBox = FindNamed<TextBox>(panel, "PathBox");
        var copyBtn = FindNamed<Button>(panel, "CopyPathBtn");
        var openBtn = FindNamed<Button>(panel, "OpenFolderBtn");
        var runBtn = FindNamed<Button>(panel, "RunInstallerBtn");
        if (statusBanner == null || pathBox == null) return;

        if (_lastBuildResult == null)
        {
            statusBanner.Text = L("Builder_NoBuildHasBeenRun", "No build has been run yet.");
            AuthoringThemeBinding.SetTone(statusBanner, AuthoringTone.Muted);
            pathBox.Text = ""; pathLabel!.Text = ""; sizeLabel!.Text = ""; issueLabel!.Text = "";
            copyBtn!.Enabled = openBtn!.Enabled = runBtn!.Enabled = false;
            return;
        }

        var r = _lastBuildResult;
        statusBanner.Text = ResultCaption(r);
        AuthoringThemeBinding.SetTone(statusBanner, r.Canceled ? AuthoringTone.Warning : r.Success ? AuthoringTone.Success : AuthoringTone.Danger);
        pathBox.Text = r.OutputFile;
        pathLabel!.Text = L("BuildResult_Artifact", "Output artifact:") + " " + r.OutputFile;
        if (!string.IsNullOrWhiteSpace(r.SetupScriptPath))
            pathLabel.Text += Environment.NewLine + L("BuildResult_SetupScript", "Setup script:") + " " + r.SetupScriptPath;
        sizeLabel!.Text = ResultMetrics(r);
        issueLabel!.Text = string.Format(Lang.LanguageManager.CurrentCulture, L("Builder_ResultIssues", "Errors: {0} • Warnings: {1}"), r.Errors.Count, r.Warnings.Count);
        bool hasArtifact = !string.IsNullOrEmpty(r.OutputFile) && File.Exists(r.OutputFile);
        copyBtn!.Enabled = hasArtifact;
        openBtn!.Enabled = hasArtifact;
        runBtn!.Enabled = hasArtifact && r.Success && !r.Canceled;
        runBtn.Text = r.ArtifactKind == "clickonce" ? L("BuildResult_OpenManifest", "Open manifest") : L("BuildResult_RunInstaller", "Run Installer");
        runBtn.AccessibleName = runBtn.Text;
    }

    private void LaunchCurrentArtifact(bool openFolder)
    {
        var result = _lastBuildResult;
        if (result is null || !File.Exists(result.OutputFile)) return;
        if (!openFolder && (!result.Success || result.Canceled)) return;
        try
        {
            var start = openFolder
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{result.OutputFile}\"") { UseShellExecute = true }
                : new ProcessStartInfo(result.OutputFile) { UseShellExecute = true };
            ArtifactLauncher(start);
        }
        catch (Exception ex) { MessageBox.Show(ActionOwner, ex.Message, L("Common_Error", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void PopulateWorkflowResult()
    {
        var panel = _contentBuild;
        if (panel == null) return;

        var status = FindNamed<Label>(panel, "WorkflowResultStatus");
        var path = FindNamed<TextBox>(panel, "WorkflowResultPath");
        var details = FindNamed<Label>(panel, "WorkflowResultDetails");
        var copy = FindNamed<Button>(panel, "WorkflowCopyPathBtn");
        if (status == null || path == null || details == null || copy == null) return;

        if (_lastBuildResult == null)
        {
            status.Text = L("Builder_NoBuildHasBeenRun", "No build has been run yet.");
            AuthoringThemeBinding.SetTone(status, AuthoringTone.Muted);
            path.Text = "";
            details.Text = "";
            copy.Enabled = false;
            return;
        }

        var result = _lastBuildResult;
        status.Text = ResultCaption(result);
        AuthoringThemeBinding.SetTone(status, result.Canceled ? AuthoringTone.Warning : result.Success ? AuthoringTone.Success : AuthoringTone.Danger);
        path.Text = result.OutputFile;
        details.Text = result.Success
            ? L("BuildResult_SetupScript", "Setup script:") + " " + result.SetupScriptPath + Environment.NewLine + ResultMetrics(result)
            : string.Join(Environment.NewLine, result.Errors.Take(3));
        copy.Enabled = !string.IsNullOrWhiteSpace(result.OutputFile) && File.Exists(result.OutputFile);
    }

    private static string ResultCaption(BuildPipeline.BuildResult result) => result.Canceled ? L("Workspace_Cancelled", "Build cancelled")
        : result.Success ? L("Workspace_Completed", "Build completed") : L("Workspace_Failed", "Build failed");
    private static string ResultMetrics(BuildPipeline.BuildResult result) => result.OutputSizeBytes > 0
        ? string.Format(Lang.LanguageManager.CurrentCulture, L("Builder_ResultMetrics", "Size: {0:N1} MB • Time: {1:N1} s"), result.OutputSizeBytes / 1024d / 1024d, result.Elapsed.TotalSeconds)
        : string.Format(Lang.LanguageManager.CurrentCulture, L("Builder_ResultTime", "Time: {0:N1} s"), result.Elapsed.TotalSeconds);

    private void ShowBuildResultInForm(BuildPipeline.BuildResult result)
    {
        PopulateResultSection();
        PopulateWorkflowResult();
        if (_guidedOwner is null || _guidedOwner.IsDisposed) _nav.SelectSection("build");
    }

    private static T? FindNamed<T>(Control root, string name) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T typed && child.Name == name)
                return typed;
            var found = FindNamed<T>(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    private static void StyleControlTree(Control root)
    {
        if (root is Panel or TableLayoutPanel or FlowLayoutPanel or SplitContainer)
            root.BackColor = PanelBackColor;

        switch (root)
        {
            case Button button:
                StyleButton(button);
                break;
            case TextBox box:
                box.BorderStyle = BorderStyle.FixedSingle;
                box.BackColor = box.ReadOnly ? Color.FromArgb(248, 250, 252) : Color.White;
                box.ForeColor = TextColor;
                break;
            case ComboBox combo:
                combo.FlatStyle = FlatStyle.Standard;
                combo.ForeColor = TextColor;
                break;
            case Label label:
                label.ForeColor = label.ForeColor == SystemColors.ControlText ? TextColor : label.ForeColor;
                break;
            case DataGridView grid:
                StyleGrid(grid);
                break;
            case PropertyGrid propertyGrid:
                propertyGrid.BackColor = PanelBackColor;
                propertyGrid.ViewBackColor = Color.White;
                propertyGrid.ViewForeColor = TextColor;
                propertyGrid.LineColor = BorderColor;
                break;
            case CheckedListBox checkedList:
                checkedList.BorderStyle = BorderStyle.FixedSingle;
                checkedList.BackColor = Color.White;
                break;
            case ListBox list:
                list.BorderStyle = BorderStyle.FixedSingle;
                list.BackColor = Color.White;
                break;
            case TreeView tree:
                tree.BorderStyle = BorderStyle.FixedSingle;
                tree.BackColor = Color.White;
                tree.ForeColor = TextColor;
                break;
        }

        if (root is PropertyGrid or DataGridView) return;
        foreach (Control child in root.Controls)
            StyleControlTree(child);
    }

    private static void StyleButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = BorderColor;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 246, 255);
        button.BackColor = Color.White;
        button.ForeColor = TextColor;
        button.Height = Math.Max(button.Height, 30);
        button.Margin = new Padding(4, 3, 4, 3);
        button.UseVisualStyleBackColor = false;
    }

    private static void StylePrimaryButton(Button button)
    {
        StyleButton(button);
        button.BackColor = AccentColor;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = AccentColor;
        AuthoringThemeBinding.SetSurface(button, AuthoringSurface.Accent);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(29, 78, 216);
    }

    private static void StyleGrid(DataGridView grid)
    {
        grid.BorderStyle = BorderStyle.None;
        grid.BackgroundColor = Color.White;
        grid.GridColor = BorderColor;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextColor;
        grid.ColumnHeadersDefaultCellStyle.Font = UiFontBold;
        grid.DefaultCellStyle.BackColor = Color.White;
        grid.DefaultCellStyle.ForeColor = TextColor;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
        grid.DefaultCellStyle.SelectionForeColor = TextColor;
        grid.RowHeadersVisible = false;
        grid.AllowUserToResizeRows = false;
    }

    // ═══════════════════════════════════════════
    //  Toolbar
    // ═══════════════════════════════════════════

    /// <summary>The less frequent authoring tools, in one menu. The buttons keep their live captions and handlers.</summary>
    private ToolStripDropDownButton MoreToolsMenu()
    {
        var more = new ToolStripDropDownButton(L("Toolbar_More", "More"))
        {
            Name = "Toolbar_More",
            ToolTipText = L("Toolbar_MoreHint", "Updates, templates, actions, conditions, recipes, release overview and project comparison"),
            Font = UiFont,
        };
        foreach (var tool in new ToolStripItem[] { RecoveryButton(), UpdatesButton(), TemplateButton(), ActionsButton(), ConditionsButton(), RecipeButton(), ReleaseButton(), ComparisonButton(), new ToolStripSeparator(), LangButton(), HelpButton(), AboutButton() })
        {
            if (tool is ToolStripButton) tool.AutoSize = true;
            more.DropDownItems.Add(tool);
        }
        C("Toolbar_More", "More").Bind(more, C("Toolbar_MoreHint", "Updates, templates, actions, conditions, recipes, release overview and project comparison"));
        return more;
    }

    private static readonly (string Key, string[] Icon)[] ToolbarIconMap =
    {
        ("Toolbar_New", new[] { "plus", "file-plus" }), ("Toolbar_Open", new[] { "fi-tr-folder-open", "folder-open" }),
        ("Common_Save", new[] { "fi-tr-floppy-disk-pen", "floppy-disk-fill" }), ("Builder_SaveAsTitle", new[] { "fi-tr-copy", "copy" }),
        ("History_Undo", new[] { "fi-tr-undo-alt", "fi-tr-undo" }), ("History_Redo", new[] { "fi-tr-redo-alt" }),
        ("Builder_Recent", new[] { "fi-tr-time-past", "clock" }), ("Toolbar_Guided", new[] { "fi-tr-sparkles", "sparkles" }),
        ("Quick_Button", new[] { "fi-tr-bolt", "bolt" }), ("Toolbar_Preview", new[] { "eye" }),
        ("Builder_BuildTitle", new[] { "fi-tr-hammer", "fi-tr-tools", "settings" }), ("Packaging_Button", new[] { "box-multiple", "stack-2" }),
        ("Signing_Button", new[] { "fi-tr-certificate", "shield-check" }), ("Builder_PublishTitle", new[] { "fi-tr-rocket", "fi-tr-cloud-upload" }),
        ("Update_Title", new[] { "refresh" }), ("Toolbar_Mcp", new[] { "fi-tr-robot", "sparkles" }),
        ("Toolbar_More", new[] { "dots", "menu-2", "menu" }),
    };

    /// <summary>Gives each top-level toolbar item its Beep icon above its caption (the Fluent command-bar layout).</summary>
    private void ApplyToolbarIcons()
    {
        if (_toolbar is null) return;
        _toolbar.ImageScalingSize = new Size(22, 22);
        foreach (var (key, icon) in ToolbarIconMap)
        {
            var item = _toolbar.Items.Cast<ToolStripItem>().FirstOrDefault(i => i.Name == key);
            var image = item is null ? null : BeepIcons.Render(22, icon);
            if (item is null || image is null) continue;
            item.Image = image;
            item.ImageScaling = ToolStripItemImageScaling.None;
            item.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            item.TextImageRelation = TextImageRelation.ImageAboveText;
        }
    }

    private ToolStripButton NewButton() => MakeButton(C("Toolbar_New", "New"), C("Toolbar_NewHint", "New installer script"), (_, _) => NewProject());
    private ToolStripButton OpenButton() => MakeButton(C("Toolbar_Open", "Open"), C("Toolbar_OpenHint", "Open installer script"), (_, _) => OpenProject());
    private ToolStripButton SaveButton() => MakeButton(C("Common_Save", "Save"), C("Toolbar_SaveHint", "Save installer script"), (_, _) => SaveProject());
    private ToolStripButton SaveAsButton() => MakeButton(C("Builder_SaveAsTitle", "Save As"), C("Toolbar_SaveAsHint", "Save installer script as"), (_, _) => SaveProjectAs());
    private ToolStripDropDownButton RecentButton() { RefreshRecents(); return _recentsBtn; }
    private ToolStripButton PackagingButton() => MakeButton(
        C("Packaging_Button", "Format"),
        C("Packaging_ButtonHint", "Choose the output format and see what it cannot carry"),
        (_, _) =>
        {
            using var wizard = new PackagingWizard(_controller.Project);
            if (wizard.ShowDialog(ActionOwner) != DialogResult.OK) return;

            _controller.Project.MarkDirty();
            BindProject();
            InvalidateAllContent();
            if (_activeSectionId is { } section) OnSectionSelected(this, section);
        });

    private ToolStripButton SigningButton() => MakeButton(
        C("Signing_Button", "Signing"),
        C("Signing_ButtonHint", "Choose how this installer is signed"),
        (_, _) =>
        {
            using var wizard = new CodeSigningWizard(_controller.Project);
            if (wizard.ShowDialog(ActionOwner) != DialogResult.OK) return;

            _controller.Project.MarkDirty();
            BindProject();
            InvalidateAllContent();
            if (_activeSectionId is { } section) OnSectionSelected(this, section);
        });

    /// <summary>
    /// Advanced -> Guided (P12 12.D.3). Reuses this same instance as the wizard's hidden section
    /// source (<see cref="BuilderWizardForm(InstallerController, PackageBuilderForm)"/>) rather than
    /// constructing a second PackageBuilderForm against the same controller. Hiding rather than
    /// closing this form keeps the main application alive. A modal guided shell returns to this
    /// same editor through either "Open full editor" or the window close action.
    /// </summary>
    private ToolStripButton GuideButton() => MakeButton(
        C("Toolbar_Guided", "Guided"),
        C("Toolbar_GuidedHint", "Switch to the guided step-by-step view"),
        (_, _) => ShowGuided());

    private void ShowGuided(bool startNew = false)
    {
        using var wizard = new BuilderWizardForm(_controller, this, startNew);
        wizard.AdvancedRequested += (_, _) => wizard.Close();
        Hide();
        try { wizard.ShowDialog(); }
        finally
        {
            // Reclaim the shared panel before disposing the modal shell. Closing Guided
            // returns to the editor without losing the project or leaving a hidden main window.
            var section = wizard.AdvancedDiagnosticPath is { } diagnosticPath
                ? AuthoringDiagnosticNavigation.SectionForPath(diagnosticPath)
                : wizard.CurrentSectionId ?? "identity";
            OpenSection(section);
            Show();
            Activate();
            if (wizard.AdvancedDiagnosticPath is { } path) FocusDiagnostic(path);
        }
    }

    private ToolStripButton QuickStartButton() => MakeButton(
        C("Quick_Button", "Quick Start"),
        C("Quick_ButtonHint", "Guided setup of the fields a build requires"),
        (_, _) =>
        {
            using var quickStart = new QuickStartWizard(_controller.Project);
            if (quickStart.ShowDialog(ActionOwner) != DialogResult.OK) return;

            _controller.Project.MarkDirty();
            BindProject();
            InvalidateAllContent();
            if (_activeSectionId is { } section) OnSectionSelected(this, section);
        });

    private ToolStripButton PreviewButton() => MakeButton(C("Toolbar_Preview", "Preview"), C("Toolbar_PreviewHint", "Preview the install wizard"), (_, _) => PreviewWizard());
    private ToolStripButton BuildButton()
    {
        var b = MakeButton(C("Builder_BuildTitle", "Build"), C("Toolbar_BuildHint", "Build the Setup.exe"), (_, _) => BuildInstaller());
        b.Font = new Font(UiFont, FontStyle.Bold); // the primary action reads as one
        return b;
    }
    private ToolStripButton PublishButton() => MakeButton(C("Builder_PublishTitle", "Publish"), C("Toolbar_PublishHint", "Publish as ClickOnce"), (_, _) => PublishProject());
    private ToolStripButton UpdatesButton() => MakeButton(C("Update_Title", "Update center"), C("Update_Instructions", "Enter the installed version and trusted public keys. Close the application being updated before applying."), (_, _) =>
    {
        using var form = global::Beep.Installer.Program.CreateUpdateCenter(_project, _runtimeArgs);
        form.ShowDialog(ActionOwner);
    });
    private ToolStripButton TemplateButton() => MakeButton(C("Toolbar_Templates", "Templates"), C("Toolbar_TemplatesHint", "Preview and apply a built-in template update"), (_, _) => UpdateFromTemplate());
    private ToolStripButton ActionsButton() => MakeButton(C("Toolbar_Actions", "Actions"), C("Toolbar_ActionsHint", "Edit custom actions"), (_, _) => EditCustomActions());
    private ToolStripButton RecipeButton() => MakeButton(C("Recipe_Title", "Resource recipes"), C("Recipe_Preview", "Preview changes"), (_, _) => ShowResourceRecipe());
    private ToolStripButton ReleaseButton() => _releaseButton = MakeButton(C("Release_Title", "Release overview"), C("Release_Notice", "Generated assets do not prove deployment succeeded."), (_, _) => ShowReleaseOverview());
    private ToolStripButton ComparisonButton() => _comparisonButton = MakeButton(C("Comparison_Title", "Compare projects"), C("Comparison_Select", "Choose a baseline project"), (_, _) => ShowProjectComparison());

    internal void ShowProjectComparison(Form? owner = null)
    {
        if (_activeContent is not null) CommitBindings(_activeContent);
        using var dialog = new ProjectComparisonDialog(() => _controller.Project);
        dialog.ShowDialog(owner ?? ActionOwner);
    }

    internal ReleaseOverviewSnapshot CaptureReleaseOverview(Beep.Installer.Quality.ReleaseQualificationPortfolioReport? portfolio, IEnumerable<string> assets)
    {
        var build = _buildWorkspace.Snapshot;
        var hasBuild = build.Id != Guid.Empty;
        bool? signingConfigured = !hasBuild || build.IsCurrentProject ? _project.HasCodeSigningCertificate : null;
        return ReleaseOverview.Create(hasBuild ? build.ProjectName : _project.AppName,
            hasBuild ? build.Version : _project.AppVersion, hasBuild ? build.Format : _project.OutputFormat.ToString(),
            !hasBuild || build.IsCurrentProject, signingConfigured,
            build.Result, assets, portfolio, build.IsRunning);
    }

    internal void ShowReleaseOverview(Form? owner = null)
    {
        using var dialog = new ReleaseOverviewDialog(CaptureReleaseOverview);
        EventHandler updated = (_, _) => { if (dialog.IsHandleCreated && !dialog.IsDisposed) dialog.RefreshOverview(); };
        _buildWorkspace.Changed += updated;
        _controller.ProjectReloaded += updated;
        try { dialog.ShowDialog(owner ?? ActionOwner); }
        finally { _buildWorkspace.Changed -= updated; _controller.ProjectReloaded -= updated; }
    }

    internal bool ShowResourceRecipe(ResourceRecipeKind kind = ResourceRecipeKind.WindowsService, Form? owner = null)
    {
        if (_activeContent is not null) CommitBindings(_activeContent);
        using var dialog = new ResourceRecipeDialog(() => _controller.Project, _controller.ApplyResourceRecipe, kind, DefaultMainExecutableReference());
        return dialog.ShowDialog(owner ?? ActionOwner) == DialogResult.OK;
    }
    private ToolStripButton ConditionsButton() => MakeButton(C("Toolbar_Conditions", "Conditions"), C("Toolbar_ConditionsHint", "Edit component conditions"), (_, _) => EditComponentConditions());
    private ToolStripButton LangButton() => MakeButton(C("Toolbar_Languages", "Languages"), C("AuthoringLanguage_Title", "UI language"), (_, _) => { using var f = new AuthoringLanguageDialog(); f.ShowDialog(ActionOwner); });
    private ToolStripButton AboutButton() => MakeButton(C("Toolbar_About", "About"), C("Builder_AboutTitle", "About Beep Installer"), (_, _) => ShowAbout());
    private ToolStripButton McpButton() => MakeButton(C("Toolbar_Mcp", "AI / MCP"), C("Toolbar_McpHint", "Use this installer from Claude, Codex and other AI assistants (MCP server)"), (_, _) =>
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("The installer cannot locate its own executable.");
        using var dialog = new McpServerDialog(exe);
        dialog.ShowDialog(ActionOwner);
    });
    private new ToolStripButton HelpButton() => MakeButton(C("Toolbar_Help", "Help"), C("Toolbar_HelpHint", "Show help"), (_, _) => ShowHelp());
    private static ToolStripButton MakeButton(LiveUiCaption text, LiveUiCaption tip, EventHandler handler)
    {
        var b = new ToolStripButton(text.Value)
        {
            Name = text.Key ?? "",
            ToolTipText = tip.Value,
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Font = UiFont,
            Padding = new Padding(6, 2, 6, 2),
            Margin = new Padding(2, 0, 2, 0),
        };
        text.Bind(b, tip);
        b.Click += handler;
        return b;
    }

    // ═══════════════════════════════════════════
    //  Commands
    // ═══════════════════════════════════════════

    private void NewProject() => NewProject(guided: true);

    /// <summary>
    /// Starts a new project, guided by default.
    /// </summary>
    /// <param name="guided">
    /// Run the quick-start wizard first. The builder presents thirty optional sections in no
    /// particular order and nothing marks the four a build actually requires, so a first project
    /// usually ends in a validation failure on a field the author did not know existed. The wizard
    /// asks for exactly those, in dependency order, and hands the rest to the builder unchanged.
    /// </param>
    private void NewProject(bool guided)
    {
        if (!ConfirmDiscardEditorChanges()) return;

        if (guided)
        {
            ShowGuided(startNew: true);
            return;
        }

        using var dlg = new ProjectNewDialog();
        if (dlg.ShowDialog(ActionOwner) != DialogResult.OK) return;

        _controller.New(dlg.SelectedTemplate, dlg.ProductName, dlg.Version, dlg.Publisher, dlg.SourceDirectory);
    }

    private void OpenProject()
    {
        if (!ConfirmDiscardEditorChanges()) return;
        using var dlg = AuthoringFileDialogs.OpenProject();
        if (dlg.ShowDialog(ActionOwner) != DialogResult.OK) return;
        LoadProject(dlg.FileName);
    }

    internal bool SaveGuidedProject(Form owner, Control view)
    {
        CommitBindings(view);
        var previousOwner = _guidedOwner;
        _guidedOwner = owner;
        try { return SaveProject(); }
        finally { _guidedOwner = previousOwner; }
    }

    private bool SaveProject()
    {
        if (!ApplyScriptEditor()) return false;
        if (_activeContent is not null) CommitBindings(_activeContent);
        if (!_controller.HasFilePath) return SaveProjectAs();
        var (ok, err) = _controller.Save();
        if (!ok) { MessageBox.Show(ActionOwner, err, L("Common_Save", "Save"), MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
        SetStatusCaption("Status_SavedFile", "Saved {0}", Path.GetFileName(_controller.FilePath!));
        return true;
    }

    private bool ConfirmDiscardEditorChanges()
    {
        if (_activeContent is not null) CommitBindings(_activeContent);
        return _controller.ConfirmDiscardChanges(ActionOwner, SaveProject, _scriptEditorDirty);
    }

    private bool SaveProjectAs()
    {
        if (!ApplyScriptEditor()) return false;
        if (_activeContent is not null) CommitBindings(_activeContent);
        using var dlg = AuthoringFileDialogs.SaveProject(_controller.HasFilePath
                ? Path.GetFileName(_controller.FilePath!)
                : _project.OutputBaseFilename + ".bsetup");
        if (dlg.ShowDialog(ActionOwner) != DialogResult.OK) return false;
        var savePath = dlg.FileName;
        if (string.IsNullOrWhiteSpace(savePath)) return false;
        var (ok, err) = _controller.SaveAs(savePath);
        if (!ok) { MessageBox.Show(ActionOwner, err, L("Builder_SaveAsTitle", "Save As"), MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
        RefreshRecents();
        SetStatusCaption("Status_SavedFile", "Saved {0}", Path.GetFileName(savePath));
        return true;
    }

    private void LoadProject(string path)
    {
        var (ok, err, canceled) = RecoveryProjectOpening.Open(_controller, path, ActionOwner);
        if (canceled) return;
        if (!ok) { MessageBox.Show(ActionOwner, err, L("Builder_OpenScriptTitle", "Open Installer Script"), MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        RefreshRecents();
    }

    private ToolStripButton RecoveryButton() => MakeButton(C("Recovery_Title", "Recover unsaved project"), C("Recovery_Help", "Choose an unsaved draft"), (_, _) => RecoverProject());

    internal bool HasPendingScriptChanges => _scriptEditorDirty;

    internal bool RecoverProject(Form? owner = null, Func<bool>? confirmChanges = null, Func<string?>? selectDraft = null)
    {
        var previousOwner = _guidedOwner;
        if (owner is not null) _guidedOwner = owner;
        try
        {
            if (!(confirmChanges?.Invoke() ?? ConfirmDiscardEditorChanges())) return false;
            RecoveryDraftEntry? entry;
            if (selectDraft is not null) entry = selectDraft() is string selectedPath ? new RecoveryDraftEntry(selectedPath) : null;
            else
            {
                var savedPaths = RecentProjects.Load().Select(recent => recent.Path).ToList();
                if (_controller.FilePath is string currentPath) savedPaths.Add(currentPath);
                using var dialog = new RecoveryDraftDialog(AutoSave.DiscoverDrafts(savedPaths));
                entry = dialog.ShowDialog(ActionOwner) == DialogResult.OK ? dialog.SelectedEntry : null;
            }
            if (entry is null) return false;
            var (ok, error) = _controller.RecoverDraft(entry);
            if (!ok) MessageBox.Show(ActionOwner, error, L("Recovery_Title", "Recover unsaved project"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return ok;
        }
        catch (Exception ex) { MessageBox.Show(ActionOwner, ex.Message, L("Recovery_Title", "Recover unsaved project"), MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
        finally { _guidedOwner = previousOwner; }
    }

    // ═══════════════════════════════════════════
    //  Build / publish / validate
    // ═══════════════════════════════════════════

    private void ValidateProject()
    {
        if (!ApplyScriptEditor()) return;
        var result = _controller.Validate();
        var snapshot = _controller.CreateAuthoringSnapshot();
        _nav.SelectSection("build");
        if (_buildLogBox != null)
        {
            _buildLogBox.Clear();
            _buildLogBox.AppendText($"Validation: {_project.ProjectName}\r\nErrors: {result.Errors.Count}, Warnings: {result.Warnings.Count}\r\n");
            _buildLogBox.AppendText($"Schema: {snapshot.SchemaVersion}\r\nPlan hash: {snapshot.PlanHash}\r\nCanonical JSON bytes: {snapshot.CanonicalJson.Length}\r\n\r\n");
            foreach (var e in result.Errors) _buildLogBox.AppendText($"ERR: {e}\r\n");
            foreach (var w in result.Warnings) _buildLogBox.AppendText($"WARN: {w}\r\n");
        }
        PopulateValidationCenter(_controller.CreateValidationCenterReport());
        if (result.Errors.Count == 0) SetStatusCaption("Status_ValidationPassed", "Validation passed.");
        else SetStatusCaption("Status_ValidationErrors", "Validation errors: {0:N0}.", result.Errors.Count);
    }

    private void PopulateValidationCenter(ProjectValidationCenterReport report)
    {
        if (_validationCenterBox == null || _validationCenterBox.IsDisposed) return;
        _displayedValidationReport = AuthoringReportPresentation.Capture(report);
        _validationReportProjectName = _project.ProjectName;
        _validationCenterHasReport = true;
        RefreshWorkflowCaptions();
    }

    private void RefreshFormatCapabilityReport()
    {
        if (!ApplyScriptEditor()) return;
        var report = _controller.CreatePackageFormatCapabilityReport();
        PopulateFormatCapabilityReport(report);
        SetStatusCaption("Report_Refreshed", "Format readiness refreshed.");
    }

    private void PopulateFormatCapabilityReport(PackageFormatCapabilityReport report)
    {
        _nav.SelectSection("build");
        _displayedCapabilityReport = AuthoringReportPresentation.Capture(report);
        _capabilityReportProjectName = _project.ProjectName;
        _formatCapabilityHasReport = true;
        RefreshWorkflowCaptions();
    }
    private void PreviewWizard() { if (!ApplyScriptEditor()) return; using var f = new WizardPreviewForm(_project); f.ShowDialog(ActionOwner); }
    private void ShowAbout() { MessageBox.Show(ActionOwner, L("Builder_AboutBody", "Beep Installer — Package Builder\r\nVersion 1.0.0\r\n\r\nBuild self-contained Setup.exe installers for Windows."), L("Builder_AboutTitle", "About Beep Installer"), MessageBoxButtons.OK, MessageBoxIcon.Information); }

    private void UpdateFromTemplate()
    {
        if (!ApplyScriptEditor()) return;
        using var dlg = new TemplateUpdateDialog(ProjectTemplates.Builtins, id => _controller.PreviewTemplateUpdate(id), template => _controller.PreviewTemplateUpdate(template));
        if (dlg.ShowDialog(ActionOwner) != DialogResult.OK) return;

        var preview = _controller.ApplyTemplateUpdate(dlg.SelectedTemplate);
        _project = _controller.Project;
        _nav.SelectSection("build");
        if (_buildLogBox != null)
        {
            _buildLogBox.Clear();
            _buildLogBox.AppendText($"Template update: {dlg.SelectedTemplateId}\r\n");
            _buildLogBox.AppendText($"Previous plan hash: {preview.Current.PlanHash}\r\n");
            _buildLogBox.AppendText($"Updated plan hash: {preview.Updated.PlanHash}\r\n");
            _buildLogBox.AppendText($"Diff entries: {preview.Diff.Count}\r\n\r\n");
            foreach (var diff in preview.Diff.Take(200))
                _buildLogBox.AppendText($"{diff.Path}\r\n  - {diff.CurrentValue}\r\n  + {diff.UpdatedValue}\r\n");
            if (preview.Diff.Count > 200)
                _buildLogBox.AppendText($"... {preview.Diff.Count - 200} more diff entries omitted from the UI log.\r\n");
        }
        SetStatusCaption("Status_TemplateApplied", "Applied template '{0}' — changes: {1:N0}.", dlg.SelectedTemplateId, preview.Diff.Count);
        RefreshScriptPreview(force: true);
    }

    internal void RecordLastBuild(BuildPipeline.BuildResult result, string? projectName = null)
    {
        _lastBuildResult = result;
        _lastBuildProjectName = projectName ?? _project.AppName;
        RefreshLastBuildCaption();
        AuthoringThemeBinding.SetTone(_buildResultLabel, result.Canceled ? AuthoringTone.Warning : result.Success ? AuthoringTone.Success : AuthoringTone.Danger);
        _buildResultLabel.IsLink = true;
        _buildResultLabel.ToolTipText = $"{_lastBuildProjectName}{Environment.NewLine}{result.OutputFile}";
        _buildResultLabel.Font = new Font(_buildResultLabel.Font, FontStyle.Bold);
        BuildResultRecorded?.Invoke(this, EventArgs.Empty);
    }

    private void OpenLastBuild()
    {
        if (_lastBuildResult == null)
        {
            MessageBox.Show(ActionOwner, L("Builder_NoBuildYet", "No build has been run yet."), "Beep Installer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_guidedOwner is null || _guidedOwner.IsDisposed) _nav.SelectSection("build");
        else BuildResultRecorded?.Invoke(this, EventArgs.Empty);
    }

    private void BuildInstaller()
    {
        if (IsBuilding) return;
        if (_activeContent is not null) CommitBindings(_activeContent);
        if (!ApplyScriptEditor()) return;
        if (string.IsNullOrEmpty(_project.AppName)) { MessageBox.Show(ActionOwner, L("Builder_ProductNameRequired", "Product name is required."), L("Builder_BuildTitle", "Build"), MessageBoxButtons.OK, MessageBoxIcon.Warning); _nav.SelectSection("identity"); return; }
        if (!_controller.HasFilePath) { var ans = MessageBox.Show(ActionOwner, L("Builder_ScriptNotSaved", "Installer script has not been saved. Save before building?"), L("Builder_BuildTitle", "Build"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question); if (ans == DialogResult.Cancel) return; if (ans == DialogResult.Yes) SaveProjectAs(); if (!_controller.HasFilePath) return; }
        if (_controller.IsDirty && !SaveProject()) return;
        var outputDir = _project.OutputDir;
        var clean = false;
        if (_project.OutputFormat is not (InstallerOutputFormat.Msi or InstallerOutputFormat.ClickOnce) &&
            !string.IsNullOrEmpty(outputDir) && Directory.Exists(outputDir) && Directory.EnumerateFileSystemEntries(outputDir).Any())
        {
            var ans = MessageBox.Show(ActionOwner, string.Format(L("Builder_OutputNotEmpty", "Output directory already contains files:\r\n{0}\r\n\r\nClean before building?"), outputDir), L("Builder_BuildTitle", "Build"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (ans == DialogResult.Cancel) return;
            clean = ans == DialogResult.Yes;
        }
        InstallProject buildProject;
        try { buildProject = _controller.CaptureBuildProject(); }
        catch (Exception ex)
        {
            MessageBox.Show(ActionOwner, ex.Message, L("Builder_BuildTitle", "Build"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        StartBuildWorkspace(buildProject, buildProject.OutputFormat is not (InstallerOutputFormat.Msi or InstallerOutputFormat.ClickOnce),
            (progress, token) => _controller.BuildCapturedProject(buildProject, clean, token, progress));
    }

    internal Task<AuthoringBuildSnapshot> StartBuildWorkspace(InstallProject capturedProject, bool canCancel,
        Func<IProgress<BuildPipeline.BuildProgress>, CancellationToken, BuildPipeline.BuildResult> build)
    {
        if (IsBuilding) throw new InvalidOperationException("A build is already running.");
        _ = Handle;
        _buildProjectReference = _controller.Project;
        _buildProjectScript = _controller.PreviewScript();
        _lastBuildResult = null;
        _lastBuildProjectName = null;
        _buildResultLabel.Text = "";
        _buildResultLabel.IsLink = false;
        _nav.SelectSection("build");
        var task = _buildWorkspace.StartAsync(capturedProject, canCancel, build);
        PopulateResultSection();
        PopulateWorkflowResult();
        BuildStarted?.Invoke(this, EventArgs.Empty);
        return task;
    }

    private void PublishProject()
    {
        if (IsBuilding) return;
        if (!ApplyScriptEditor()) return;
        if (string.IsNullOrEmpty(_project.AppName)) { MessageBox.Show(ActionOwner, L("Builder_ProductNameRequired", "Product name is required."), L("Builder_PublishTitle", "Publish"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        // A bare folder picker skipped the update URL and signing entirely, so both silently took a
        // default -- and a wrong update URL is the classic ClickOnce failure: the install works and
        // then never updates again.
        using var dlg = new PublishWizard(_project);
        if (dlg.ShowDialog(ActionOwner) != DialogResult.OK) return;
        if (_controller.IsDirty && !SaveProject()) return;
        var capturedProject = _controller.CaptureBuildProject();
        capturedProject.OutputFormat = InstallerOutputFormat.ClickOnce;
        var publishFolder = dlg.PublishFolder;
        var updateUrl = dlg.UpdateUrl;
        var sign = dlg.Sign;
        StartBuildWorkspace(capturedProject, false,
            (progress, _) => _controller.PublishCapturedProject(capturedProject, publishFolder, updateUrl, sign, progress));
    }

    private void EditCustomActions() => EditCustomActionDiagnostic(null, ActionOwner);

    internal void EditCustomActionDiagnostic(string? path, Form owner)
    {
        var origin = _project;
        var baseline = ModalDraftApply.Capture(origin.CustomActions);
        using var dialog = new CustomActionsDialog(origin.CustomActions);
        using var commit = new ModalDraftApply(dialog,
            () => ReferenceEquals(origin, _project) && ReferenceEquals(origin, _controller.Project) &&
                baseline == ModalDraftApply.Capture(origin.CustomActions),
            () => ApplyCustomActionResult(dialog.ResultActions));
        dialog.ApplyDraft = commit.TryApply;
        if (path is not null) dialog.Shown += (_, _) => DiagnosticEditorNavigator.Focus(dialog, path);
        dialog.ShowDialog(owner);
    }

    internal void ApplyCustomActionResult(IEnumerable<TheTechIdea.Beep.Installer.Steps.CustomAction> actions)
    {
        var result = actions.ToArray();
        _controller.RunAuthoringChange("Custom actions", () =>
        {
            _project.CustomActions.Clear();
            foreach (var action in result) _project.CustomActions.Add(action);
            OnAuthoringResourceChanged();
        });
    }
    private void EditComponentConditions()
    {
        // Carry the grid selection into the dialog so the author does not have to find the same
        // component a second time.
        var selected = _componentsGrid is { IsDisposed: false } && _componentsGrid.SelectedRows.Count > 0
            ? _componentsGrid.SelectedRows[0].DataBoundItem as InstallComponent
            : null;

        var origin = _project;
        var components = origin.Components.ToArray();
        string TargetState() => ModalDraftApply.Capture(origin.Components.Select(component =>
            new object[] { component.Id, component.ConditionExpression, component.Conditions }).ToArray());
        var baseline = TargetState();
        var drafts = components.Select(CloneAdvancedResource).ToList();
        var selectedIndex = selected is null ? -1 : _project.Components.IndexOf(selected);
        using var dlg = new ComponentConditionsDialog(drafts, selectedIndex < 0 ? null : drafts[selectedIndex]);
        using var commit = new ModalDraftApply(dlg,
            () => ReferenceEquals(origin, _project) && ReferenceEquals(origin, _controller.Project) &&
                components.Length == origin.Components.Count &&
                !components.Where((component, index) => !ReferenceEquals(component, origin.Components[index])).Any() &&
                baseline == TargetState(),
            () => ApplyComponentConditionResult(drafts));
        dlg.ApplyDraft = commit.TryApply;
        dlg.ShowDialog(ActionOwner);
    }

    internal void ApplyComponentConditionResult(IReadOnlyList<InstallComponent> drafts)
    {
        if (drafts.Count != _project.Components.Count || drafts.Where((draft, index) => draft.Id != _project.Components[index].Id).Any())
            throw new InvalidOperationException("The component list changed while conditions were being edited.");
        _controller.RunAuthoringChange("Component conditions", () =>
        {
            for (var index = 0; index < drafts.Count; index++)
            {
                var component = _project.Components[index];
                component.Conditions.Clear();
                foreach (var condition in drafts[index].Conditions) component.Conditions.Add(condition);
                component.ConditionExpression = drafts[index].ConditionExpression;
            }
            OnAuthoringResourceChanged();
        });
    }

    private void ShowHelp()
    {
        using var help = new AuthoringHelpDialog(_activeSectionId);
        help.ShowDialog(ActionOwner);
    }

    /// <summary>Every section the nav currently offers, in display order.</summary>
    internal IReadOnlyList<string> SectionIds => _nav.VisibleSectionIds;

    /// <summary>Opens a section as clicking its nav entry would.</summary>
    internal void OpenSection(string id) => _nav.SelectSection(id);

    /// <summary>The panel currently on screen for the selected section, if any.</summary>
    internal Control? ActiveSection => _activeContent;

    /// <summary>The id of the section currently on screen, if any (e.g. "identity").</summary>
    internal string? ActiveSectionId => _activeSectionId;

    /// <summary>
    /// Whether current canonical diagnostics contain a blocking issue for this section.
    /// Warnings remain visible through inline feedback without blocking guided navigation.
    /// </summary>
    internal bool ActiveSectionHasErrors()
    {
        return ActiveSectionDiagnostics().Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error);
    }

    /// <summary>Validate current model values synchronously. ErrorProvider is presentation only:
    /// its warnings and its debounce timer must never decide whether navigation is allowed.</summary>
    internal IReadOnlyList<ProjectSchemaDiagnostic> ActiveSectionDiagnostics()
    {
        if (_activeContent is null) return Array.Empty<ProjectSchemaDiagnostic>();
        try
        {
            CommitBindings(_activeContent);
            var fields = new Dictionary<string, Control>(StringComparer.Ordinal);
            CollectNamedFields(_activeContent, fields);
            var diagnostics = ProjectSchemaService.Validate(_project).Diagnostics;
            var collection = _activeSectionId switch
            {
                "components" => "Components",
                "prerequisites" => "Prerequisites",
                "shortcuts" => "Shortcuts",
                "registry" => "RegistryEntries",
                _ => null
            };
            return diagnostics.Where(d =>
                d.Path == "Setup.SchemaVersion" ||
                (d.Path.StartsWith("Setup.", StringComparison.Ordinal) && fields.ContainsKey(d.Path[6..])) ||
                (collection is not null && (d.Path == collection || d.Path.StartsWith(collection + "[", StringComparison.Ordinal))))
                .ToArray();
        }
        catch (Exception ex)
        {
            Diag.Warn("AuthoringValidation", "Step validation unavailable", ex);
            return new[] { new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error,
                "BIUI001", "", L("Wizard_ValidationUnavailable", "Validation is unavailable. Try again before continuing.")) };
        }
    }

    internal static void CommitBindings(Control root)
    {
        foreach (Binding binding in root.DataBindings) binding.WriteValue();
        foreach (Control child in root.Controls) CommitBindings(child);
        if (root is DataGridView grid) grid.EndEdit();
    }

    internal static string KeyboardWalkthroughText()
        => string.Join(Environment.NewLine, new[]
        {
            L("Builder_KeyboardTitle", "Keyboard walkthrough"),
            "Ctrl+F — " + L("Builder_SearchSections", "Search sections…") + "; " + L("Builder_KeyboardSearchHelp", "Enter opens the first match; Escape clears search."),
            "Ctrl+Tab / Ctrl+Shift+Tab — " + L("Builder_KeyboardSections", "Next / previous visible section"),
            "Tab / Shift+Tab — " + L("Builder_KeyboardFields", "Next / previous field, grid, property or action"),
            "Ctrl+T — " + L("Toolbar_Templates", "Templates") + "; Ctrl+P — " + L("Toolbar_Preview", "Preview") + "; F7 — " + L("Common_Validation", "Validation") + "; F5 — " + L("Builder_BuildTitle", "Build"),
            "Ctrl+S — " + L("Common_Save", "Save") + "; Ctrl+Shift+S — " + L("Builder_SaveAsTitle", "Save As") + "; Ctrl+O — " + L("Toolbar_Open", "Open") + "; Ctrl+N — " + L("Toolbar_New", "New")
        });

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (IsBuilding) { e.Cancel = true; return; }
        if (!ConfirmDiscardEditorChanges()) { e.Cancel = true; return; }
        _controller.StopAutoSave();
        _scriptDebounce?.Stop();
        base.OnFormClosing(e);
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Z) { if (!AuthoringHistoryKeyboard.UndoFocusedText(this)) RestoreAuthoringHistory(false); e.SuppressKeyPress = true; return; }
        if (e.Control && e.KeyCode == Keys.Y) { RestoreAuthoringHistory(true); e.SuppressKeyPress = true; return; }
        if (e.Control && e.Shift && e.KeyCode == Keys.S) { SaveProjectAs(); e.Handled = true; return; }
        if (e.Control && e.Shift && e.KeyCode == Keys.Tab) { _nav.SelectAdjacentSection(-1); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.F) { _navSearchBox.Focus(); _navSearchBox.SelectAll(); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.Tab) { _nav.SelectAdjacentSection(+1); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.N) { NewProject(); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.O) { OpenProject(); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.S) { SaveProject(); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.P) { PreviewWizard(); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.T) { UpdateFromTemplate(); e.Handled = true; return; }
        if (e.KeyCode == Keys.F5) { BuildInstaller(); e.Handled = true; return; }
        if (e.KeyCode == Keys.F1) { ShowHelp(); e.Handled = true; return; }
        if (e.KeyCode == Keys.F7) { ValidateProject(); e.Handled = true; return; }
    }

    // ═══════════════════════════════════════════
    //  File tree / scanning
    // ═══════════════════════════════════════════

    /// <summary>
    /// Rebuilds the source file tree.
    ///
    /// The filesystem work happens off the UI thread. Every declared file cost up to two
    /// <see cref="File.Exists"/> calls — one to choose the path and one inside the tree walk — and
    /// this runs whenever the source directory changes, so on a project declaring thousands of
    /// files the builder froze for the duration. 6.C.1 moved the *scan* off the UI thread but left
    /// this behind, which is the same freeze on a smaller budget.
    ///
    /// Reads of <c>Components</c> are snapshotted first: the collections are observable and the
    /// user can edit them while this is in flight.
    /// </summary>
    private void RefreshFileTree()
    {
        // An empty or invalid source still supersedes every earlier request.
        var generation = ++_fileTreeGeneration;
        if (_fileTree == null) return;

        _fileTree.Nodes.Clear();
        var sourceDir = _project.SourceDirectory;
        if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir)) return;

        var declared = _project.Components
            .SelectMany(c => (IEnumerable<FileCopyOperation>?)c.Files ?? Array.Empty<FileCopyOperation>())
            .Select(f => (f.SourcePath, f.DestinationPath))
            .ToList();

        // Track the request so a stale result cannot overwrite a newer tree: the source directory
        // can change again while this one is still resolving.
        Task.Run(() => declared
                .Select(d => File.Exists(d.SourcePath) ? d.SourcePath : Path.Combine(sourceDir, d.DestinationPath))
                .Where(File.Exists)
                .ToList())
            .ContinueWith(t =>
            {
                if (IsDisposed || Disposing || _fileTree is null || _fileTree.IsDisposed) return;
                if (generation != _fileTreeGeneration) return;
                if (t.IsFaulted) return;

                _fileTree.BeginUpdate();
                try
                {
                    _fileTree.Nodes.Clear();
                    var rootNode = new TreeNode(sourceDir) { Tag = sourceDir };
                    _fileTree.Nodes.Add(rootNode);
                    foreach (var path in t.Result)
                        AddFileToTree(rootNode, sourceDir, path);
                    rootNode.Expand();
                }
                finally
                {
                    _fileTree.EndUpdate();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Guards against an older refresh landing after a newer one.</summary>
    private int _fileTreeGeneration;

    private Panel? _brandingPreview;
    private ToolStripStatusLabel? _readinessLabel;
    private readonly LatestProjectCheck _readinessChecks = new();

    /// <summary>
    /// Recomputes the "can this build?" indicator.
    ///
    /// Deliberately the same <see cref="ProjectSchemaService"/> the build runs, so the status bar
    /// cannot disagree with what pressing Build would do -- the split that once let `/VALIDATE`
    /// reject a project `/BUILD` accepted is exactly the shape to avoid repeating in the UI.
    /// </summary>
    private async void RefreshReadiness()
    {
        if (_readinessLabel is null || IsDisposed || Disposing || !IsHandleCreated) return;
        if (ApplyNoProjectState()) return;
        var generation = -1;

        try
        {
            var snapshot = _controller.CaptureBuildProject();
            _readinessLabel.Text = L("Readiness_Checking", "Checking readiness…");
            AuthoringThemeBinding.SetTone(_readinessLabel, AuthoringTone.Muted);
            var pending = _readinessChecks.RunAsync(snapshot, (project, token) => Task.Run<IReadOnlyList<ProjectSchemaDiagnostic>>(() =>
            {
                token.ThrowIfCancellationRequested();
                var validation = ProjectSchemaService.Validate(project).Diagnostics;
                token.ThrowIfCancellationRequested();
                validation.AddRange(PackageTargetDiagnostics.Validate(project));
                token.ThrowIfCancellationRequested();
                return validation;
            }, token));
            generation = _readinessChecks.Generation;
            var diagnostics = await pending;
            if (diagnostics is null || IsDisposed || Disposing || generation != _readinessChecks.Generation) return;
            if (InvokeRequired)
            {
                if (IsHandleCreated) BeginInvoke(() =>
                {
                    if (generation == _readinessChecks.Generation) ApplyReadiness(diagnostics);
                });
                return;
            }
            ApplyReadiness(diagnostics);
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing || (generation >= 0 && generation != _readinessChecks.Generation)) return;
            _readinessLabel.Text = L("Readiness_Unavailable", "Readiness check unavailable. Validate before building.");
            AuthoringThemeBinding.SetTone(_readinessLabel, AuthoringTone.Warning);
            _readinessLabel.ToolTipText = "";
            Diag.Warn("AuthoringReadiness", "Readiness check failed", ex);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        OnControllerPropertyChanged(_controller, new PropertyChangedEventArgs(nameof(InstallerController.SaveState)));
        RefreshReadiness();
    }

    private void ApplyReadiness(IReadOnlyList<ProjectSchemaDiagnostic> diagnostics)
    {
            if (_readinessLabel is null || IsDisposed || Disposing || ApplyNoProjectState()) return;
            var errors = diagnostics.Count(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error);
            var warnings = diagnostics.Count(d => d.Severity == ProjectSchemaDiagnosticSeverity.Warning);

            if (errors > 0)
            {
                _readinessLabel.Text = string.Format(L("Builder_ReadinessBlocked", "⛔ {0} blocking problem(s)"), errors);
                AuthoringThemeBinding.SetTone(_readinessLabel, AuthoringTone.Danger);
                _readinessLabel.ToolTipText = string.Join(Environment.NewLine,
                    diagnostics
                        .Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error)
                        .Take(8)
                        .Select(d => $"{d.Code} {d.Path}: {d.Message}"));
                return;
            }

            _readinessLabel.Text = warnings > 0
                ? string.Format(L("Builder_ReadinessWarn", "✓ Ready — {0} warning(s)"), warnings)
                : L("Builder_ReadinessOk", "✓ Ready to build");
            AuthoringThemeBinding.SetTone(_readinessLabel, warnings > 0 ? AuthoringTone.Warning : AuthoringTone.Success);
            _readinessLabel.ToolTipText = warnings > 0
                ? string.Join(Environment.NewLine,
                    diagnostics
                        .Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Warning)
                        .Take(8)
                        .Select(d => $"{d.Code} {d.Path}: {d.Message}"))
                : "";
    }

    /// <summary>
    /// Draws the wizard's sidebar, title and accent using the authored colours, so the effect of a
    /// change is visible while it is being made.
    /// </summary>
    private void PaintBrandingPreview(Graphics g, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var area = Rectangle.Inflate(bounds, -1, -1);
        area.Y += 8;
        area.Height -= 8;
        if (area.Height <= 0) return;

        var sidebarBack = ParseColor(_project.SidebarBackgroundColor, Color.FromArgb(31, 41, 55));
        var sidebarText = ParseColor(_project.SidebarTextColor, Color.White);
        var accent = ParseColor(_project.AccentColor, Color.FromArgb(37, 99, 235));

        using var page = new SolidBrush(Color.White);
        g.FillRectangle(page, area);
        using (var border = new Pen(BorderColor)) g.DrawRectangle(border, area);

        var sidebar = new Rectangle(area.X + 1, area.Y + 1, Math.Max(1, area.Width / 3), area.Height - 2);
        using (var back = new SolidBrush(sidebarBack)) g.FillRectangle(back, sidebar);

        using var text = new SolidBrush(sidebarText);
        var title = string.IsNullOrWhiteSpace(_project.WelcomeTitle) ? _project.AppName : _project.WelcomeTitle;
        g.DrawString(title, UiFontBold, text, new RectangleF(sidebar.X + 8, sidebar.Y + 10, sidebar.Width - 16, 40));
        using (var muted = new SolidBrush(Color.FromArgb(160, sidebarText)))
            g.DrawString(L("Builder_BrandingPreviewStep", "Setup"), UiFont, muted, sidebar.X + 8, sidebar.Y + 56);

        // The accent shows where it actually lands: the primary button.
        var button = new Rectangle(area.Right - 104, area.Bottom - 40, 88, 26);
        using (var fill = new SolidBrush(accent)) g.FillRectangle(fill, button);
        using var buttonText = new SolidBrush(Contrasting(accent));
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(L("Btn_Install", "Install"), UiFont, buttonText, button, centered);
    }

    /// <summary>Authored colours are free text, so an unparseable value falls back rather than throwing.</summary>
    private static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try
        {
            var parsed = ColorTranslator.FromHtml(value.Trim());
            return parsed.A == 0 ? fallback : parsed;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>Black or white, whichever stays readable on the accent.</summary>
    private static Color Contrasting(Color background)
        => (background.R * 0.299 + background.G * 0.587 + background.B * 0.114) > 150 ? Color.Black : Color.White;

    /// <summary>
    /// Adds one already-verified path. The existence check moved to the caller's background pass —
    /// doing it here meant a filesystem stat per file on the UI thread.
    /// </summary>
    private static void AddFileToTree(TreeNode root, string baseDir, string filePath)
    {
        var rel = Path.GetRelativePath(baseDir, filePath);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var f = current.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Text == parts[i]);
            if (f == null)
            {
                f = new TreeNode(parts[i]) { Tag = Path.Combine(baseDir, string.Join(Path.DirectorySeparatorChar.ToString(), parts.Take(i + 1))) };
                current.Nodes.Add(f);
            }
            current = f;
        }
        current.Nodes.Add(new TreeNode(parts.Last()) { Tag = filePath, Checked = true });
    }

    private void OnFileTreeCheck(object? sender, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is not string filePath) return;
        foreach (var comp in _project.Components)
            foreach (var f in comp.Files ?? new List<FileCopyOperation>())
                if (string.Equals(f.SourcePath, filePath, StringComparison.OrdinalIgnoreCase)) { f.IsRequired = e.Node.Checked; _project.MarkDirty(); return; }
    }

    private void RescanSource() { ScanAndPopulate(_sourceDirBox.Text.Trim()); }

    /// <summary>
    /// Scans the source tree off the UI thread.
    ///
    /// This used to walk the whole directory synchronously — reading PE metadata and
    /// deps.json for every file — so the builder froze solid on a large source tree with no
    /// indication it was still alive.
    /// </summary>
    private async void ScanAndPopulate(string dir) => await ScanAndPopulateAsync(dir);

    internal async Task ScanAndPopulateAsync(string dir, Func<InstallProject, string, SourceScanner.ScanResult>? scanner = null)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        if (_scanInProgress) return;    // re-entrancy: the button stays clickable while we run
        var section = _activeSectionId;
        if (!ApplyScriptEditor()) return;
        if (section is not null && _activeSectionId != section) OpenSection(section);

        _scanInProgress = true;
        var scannedProject = _project;
        SetSourceScanCaption("Builder_ScanningSource", dir);
        UseWaitCursor = true;
        AuthoringSourceScan? scan = null;

        try
        {
            scan = _controller.CaptureSourceScan(dir);
            var result = await Task.Run(() => scan.Run(scanner));
            if (IsDisposed || Disposing || !ReferenceEquals(_project, scannedProject)) return;
            if (_scriptEditorDirty || !_controller.TryApplySourceScan(scan))
            {
                SetSourceScanCaption("Builder_ScanDiscarded");
                return;
            }
            SetSourceScanCaption("Builder_ScanComplete", result.FileCount, result.TotalSizeBytes / 1024d / 1024d, result.ManagedCount);

            foreach (var warning in result.Warnings)
                Engine.Diag.Info("Builder", $"scan: {warning}");
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing || !ReferenceEquals(_project, scannedProject)) return;
            if (_scriptEditorDirty || (scan is not null && !_controller.IsSourceScanCurrent(scan)))
            {
                SetSourceScanCaption("Builder_ScanDiscarded");
                return;
            }
            // A scan failure must not take the builder down with it.
            SetSourceScanCaption("Builder_ScanFailedStatus", ex.Message);
            Engine.Diag.Warn("Builder", $"scan of '{dir}' failed", ex);
            MessageBox.Show(ActionOwner,
                string.Format(L("Builder_ScanFailedBody", "Could not scan '{0}':{1}{2}"), dir, Environment.NewLine, ex.Message),
                L("Builder_ScanFailedTitle", "Scan failed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed && !Disposing) UseWaitCursor = false;
            _scanInProgress = false;
        }
    }

    private bool _scanInProgress;

    private void AddComponent()
    {
        _controller.RunAuthoringChange("Add component", () =>
        {
            _project.Components.Add(new InstallComponent { Id = NextComponentId(), Name = "New Component", Selected = true, IncludedIn = InstallationType.Typical });
            OnAuthoringResourceChanged();
        });
    }

    private string NextComponentId() => Enumerable.Range(1, _project.Components.Count + 1)
        .Select(index => $"comp{index}").First(id => !_project.Components.Any(component => string.Equals(component.Id, id, StringComparison.OrdinalIgnoreCase)));

    private void DuplicateComponent()
    {
        if (_componentsGrid?.SelectedRows.Count > 0 && _componentsGrid.SelectedRows[0].DataBoundItem is InstallComponent component)
            _controller.RunAuthoringChange("Duplicate component", () =>
            {
                var copy = CloneAdvancedResource(component);
                copy.Id = NextComponentId();
                _project.Components.Add(copy);
                OnAuthoringResourceChanged();
            });
    }

    private void RemoveComponent()
    {
        if (_componentsGrid?.SelectedRows.Count > 0 && _componentsGrid.SelectedRows[0].DataBoundItem is InstallComponent c && !c.Required)
        {
            _controller.RunAuthoringChange("Remove component", () =>
            {
                _project.Components.Remove(c);
                OnAuthoringResourceChanged();
            });
        }
    }

    private void EditComponentFiles()
    {
        if (_componentsGrid?.SelectedRows.Count > 0 && _componentsGrid.SelectedRows[0].DataBoundItem is InstallComponent c)
        {
            var origin = _project;
            string TargetState() => ModalDraftApply.Capture(new object[] { origin.SourceDirectory, c.Files, c.SizeBytes });
            var baseline = TargetState();
            using var dlg = new ComponentFilesDialog(c, origin.SourceDirectory);
            using var commit = new ModalDraftApply(dlg,
                () => ReferenceEquals(origin, _project) && ReferenceEquals(origin, _controller.Project) &&
                    origin.Components.Contains(c) && baseline == TargetState(),
                () => ApplyComponentFileResult(c, dlg.ResultFiles, dlg.ReadySizeBytes ??
                    throw new InvalidOperationException("The file-size result is not current.")));
            dlg.ApplyDraft = commit.TryApply;
            dlg.ShowDialog(ActionOwner);
        }
    }

    internal void ApplyComponentFileResult(InstallComponent component, IEnumerable<FileCopyOperation> files, long? capturedSizeBytes = null)
    {
        if (!_project.Components.Contains(component)) throw new InvalidOperationException("The component changed while files were being edited.");
        var result = files.ToArray();
        var sizeBytes = capturedSizeBytes ?? ComponentFileScan.ReadSizes(result.Select(file => file.SourcePath).ToArray(),
            _project.SourceDirectory, CancellationToken.None).Bytes;
        _controller.RunAuthoringChange("Component files", () =>
        {
            component.Files.Clear();
            foreach (var file in result) component.Files.Add(file);
            component.SizeBytes = sizeBytes;
            OnAuthoringResourceChanged();
        });
    }

    private void MoveComponent(int delta)
    {
        if (_componentsGrid?.SelectedRows.Count > 0)
        {
            var idx = _componentsGrid.SelectedRows[0].Index;
            var newIdx = idx + delta;
            if (newIdx >= 0 && newIdx < _project.Components.Count)
            {
                _controller.RunAuthoringChange("Move component", () =>
                {
                    var item = _project.Components[idx];
                    _project.Components.RemoveAt(idx);
                    _project.Components.Insert(newIdx, item);
                    OnAuthoringResourceChanged();
                });
            }
        }
    }

    // ═══════════════════════════════════════════
    //  Welcome panel
    // ═══════════════════════════════════════════

    private void BuildWelcomePanel()
    {
        _welcomePanel = new Panel { Dock = DockStyle.Fill, BackColor = PanelBackColor, Visible = false };
        var center = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.None,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(24),
        };
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.Controls.Add(new Label { Text = L("Builder_BeepInstallerPackageBuilder", "Beep Installer Package Builder"), Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = TextColor, AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 0, 0, 6) }, 0, 0);
        center.Controls.Add(new Label { Text = L("Builder_AuthorSetupScriptsAndBuild", "Author setup scripts and build a self-contained Windows Setup.exe."), Font = new Font("Segoe UI", 10), ForeColor = MutedTextColor, AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 0, 0, 0) }, 0, 1);
        var newBtn = new Button { Text = L("Builder_NewInstallerScript", "New Installer Script"), Size = new Size(220, 36), Margin = new Padding(0, 8, 0, 6) };
        StylePrimaryButton(newBtn);
        newBtn.Click += (_, _) => NewProject();
        var openBtn = new Button { Text = L("Builder_OpenExistingScript", "Open Existing Script"), Size = new Size(220, 36), Margin = new Padding(0, 0, 0, 10) };
        StyleButton(openBtn);
        openBtn.Click += (_, _) => OpenProject();
        center.Controls.Add(new Panel { Height = 12 }, 0, 2);
        center.Controls.Add(newBtn, 0, 3);
        center.Controls.Add(openBtn, 0, 4);
        var sub = new Label { Text = L("Builder_DragABsetupFileOnto", "Drag a .bsetup file onto this window to open it."), Font = UiFont, ForeColor = MutedTextColor, AutoSize = true, TextAlign = ContentAlignment.MiddleCenter };
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.Controls.Add(BuildDropZoneAndRecents(sub), 0, 5);
        C("Builder_BeepInstallerPackageBuilder", "Beep Installer Package Builder").Bind(center.GetControlFromPosition(0, 0)!);
        C("Builder_AuthorSetupScriptsAndBuild", "Author setup scripts and build a self-contained Windows Setup.exe.").Bind(center.GetControlFromPosition(0, 1)!);
        C("Builder_NewInstallerScript", "New Installer Script").Bind(newBtn); C("Builder_OpenExistingScript", "Open Existing Script").Bind(openBtn);
        C("Builder_DragABsetupFileOnto", "Drag a .bsetup file onto this window to open it.").Bind(sub);
        newBtn.AutoSize = openBtn.AutoSize = true;
        _welcomePanel.Controls.Add(center);
        center.Location = new Point((_welcomePanel.Width - center.Width) / 2, (_welcomePanel.Height - center.Height) / 2);
        _welcomePanel.Resize += (_, _) => { if (center != null) center.Location = new Point((_welcomePanel.Width - center.Width) / 2, (_welcomePanel.Height - center.Height) / 2); };
    }

    /// <summary>
    /// With no project loaded there is nothing to build: the project actions are disabled and the status says
    /// so, instead of "Ready to build" beside "Unsaved changes" on a blank canvas. Returns true while in that state.
    /// </summary>
    private bool ApplyNoProjectState()
    {
        var noProject = _welcomePanel is { Visible: true };
        if (_toolbar is not null)
            foreach (var item in _toolbar.Items.OfType<ToolStripButton>())
                if (item.Name is "Builder_BuildTitle" or "Toolbar_Preview" or "Builder_PublishTitle" or "Signing_Button")
                    item.Enabled = !noProject;
        // The section links and their search edit a project; with none open they would lead to editors with nothing to edit,
        // so the whole left panel stays hidden until a project is created or opened.
        if (_nav?.Parent?.Parent?.Parent is SplitContainer navSplit) navSplit.Panel1Collapsed = noProject;
        if (noProject && _readinessLabel is not null)
        {
            _readinessLabel.Text = L("Builder_NoProjectLoaded", "No project loaded");
            AuthoringThemeBinding.SetTone(_readinessLabel, AuthoringTone.Muted);
            _readinessLabel.ToolTipText = "";
        }
        return noProject;
    }

    /// <summary>The launchpad's lower half: a dashed drop zone carrying the drag hint, then the recent projects.</summary>
    private Control BuildDropZoneAndRecents(Label dragHint)
    {
        var host = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
        var zone = new Panel { Size = new Size(420, 64), Margin = new Padding(0, 8, 0, 12), BackColor = PanelBackColor, Padding = new Padding(2), AllowDrop = true, AccessibleName = dragHint.Text };
        zone.Paint += (_, e) =>
        {
            using var pen = new Pen(MutedTextColor) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, 0, 0, zone.Width - 1, zone.Height - 1);
        };
        dragHint.AutoSize = false; dragHint.Dock = DockStyle.Fill; dragHint.TextAlign = ContentAlignment.MiddleCenter;
        zone.Controls.Add(dragHint);
        host.Controls.Add(zone);

        var entries = RecentProjects.Load().Take(5).ToList();
        if (entries.Count > 0)
        {
            var title = new Label { Text = L("Builder_RecentProjects", "Recent projects"), Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = TextColor, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            host.Controls.Add(title);
            foreach (var entry in entries)
            {
                var link = new LinkLabel { Text = Path.GetFileName(entry.Path), AutoSize = true, Font = UiFont, Margin = new Padding(0, 0, 0, 2) };
                var path = entry.Path;
                new ToolTip().SetToolTip(link, path);
                link.LinkClicked += (_, _) => OpenRecent(path);
                host.Controls.Add(link);
            }
        }
        return host;
    }

    private void ShowWelcome()
    {
        if (_welcomePanel == null) return;
        _welcomePanel.Visible = true;
        ApplyNoProjectState();
        _contentHost.Controls.Clear();
        _contentHost.Controls.Add(_welcomePanel);
        _welcomePanel.Dock = DockStyle.Fill;
        _welcomePanel.BringToFront();
    }

    private void HideWelcome()
    {
        if (_welcomePanel == null) return;
        _contentHost.Controls.Clear();
        _welcomePanel.Visible = false;
        ApplyNoProjectState();
    }

    // ═══════════════════════════════════════════
    //  Status / title / recents / script
    // ═══════════════════════════════════════════

    private void UpdateTitle()
    {
        var name = string.IsNullOrEmpty(_controller.FilePath)
            ? L("SaveState_Untitled", "Untitled project")
            : Path.GetFileName(_controller.FilePath);
        Text = $"{L("Builder_BeepInstallerPackageBuilder", "Beep Installer — Package Builder")} — {name}{(_controller.IsDirty ? "*" : "")}";
    }

    private void RefreshLastBuildCaption()
    {
        if (_lastBuildResult is not { } result) return;
        _buildResultLabel.Text = result.Canceled ? L("Workspace_Cancelled", "Build cancelled") : result.Success
            ? string.Format(Lang.LanguageManager.CurrentCulture, L("Builder_LastBuildArtifact", "Last build: {0} (click to view)"), Path.GetFileName(result.OutputFile))
            : L("Workspace_Failed", "Build failed");
        _buildResultLabel.AccessibleName = _buildResultLabel.Text;
    }

    private string _sourceScanCaptionKey = "Builder_NoScan";
    private object[] _sourceScanCaptionArguments = Array.Empty<object>();
    private bool _sourceScanOwnsStatus;

    private void SetSourceScanCaption(string key, params object[] arguments)
    {
        _statusCaptionRenderer = null;
        _sourceScanCaptionKey = key; _sourceScanCaptionArguments = arguments; _sourceScanOwnsStatus = true;
        RefreshSourceScanCaptions();
    }

    private void ResetSourceScanCaption()
    {
        _sourceScanCaptionKey = "Builder_NoScan"; _sourceScanCaptionArguments = Array.Empty<object>(); _sourceScanOwnsStatus = false;
        RefreshSourceScanCaptions();
    }

    private void RefreshSourceScanCaptions()
    {
        var caption = string.Format(Lang.LanguageManager.CurrentCulture, L(_sourceScanCaptionKey, "No scan yet."), _sourceScanCaptionArguments);
        if (_fileStatsLabel is { IsDisposed: false }) _fileStatsLabel.Text = _fileStatsLabel.AccessibleName = caption;
        if (_sourceScanOwnsStatus && _statusLabel is { IsDisposed: false }) _statusLabel.Text = _statusLabel.AccessibleName = caption;
    }

    private void RefreshComponentCaptions()
    {
        if (_componentsGrid is not { IsDisposed: false }) return;
        foreach (var (property, key, fallback) in new[] { ("Id", "Grid_Id", "Id"), ("Name", "Grid_Name", "Name"),
            ("Description", "Grid_Description", "Description"), ("Required", "Grid_Required", "Required"), ("Selected", "Grid_Selected", "Selected") })
            if (_componentsGrid.Columns[property] is { } column) column.HeaderText = L(key, fallback);
    }

    private bool _validationCenterHasReport, _formatCapabilityHasReport;
    private ProjectValidationCenterReport? _displayedValidationReport;
    private PackageFormatCapabilityReport? _displayedCapabilityReport;
    private string _validationReportProjectName = "", _capabilityReportProjectName = "";
    private void RefreshWorkflowCaptions()
    {
        if (_keyboardWalkthroughBox is { IsDisposed: false }) _keyboardWalkthroughBox.Text = KeyboardWalkthroughText();
        if (!_validationCenterHasReport && _validationCenterBox is { IsDisposed: false })
            _validationCenterBox.Text = L("Builder_ValidationCenterIsReadyClick", "Validation Center is ready. Click Validate Project to see grouped schema, semantic and resource diagnostics.");
        if (!_formatCapabilityHasReport && _formatCapabilityBox is { IsDisposed: false })
            _formatCapabilityBox.Text = L("Builder_ReleaseReadinessPendingClickRefresh", "Release readiness: pending. Click Refresh Format Readiness to see EXE, MSI, MSIX, WinGet and Intune/ConfigMgr capability status.");
        if (_validationCenterHasReport && _displayedValidationReport is { } validation && _validationCenterBox is { IsDisposed: false })
            AuthoringReportPresentation.UpdateText(_validationCenterBox, AuthoringReportPresentation.Validation(_validationReportProjectName, validation));
        if (_formatCapabilityHasReport && _displayedCapabilityReport is { } capability && _formatCapabilityBox is { IsDisposed: false })
            AuthoringReportPresentation.UpdateText(_formatCapabilityBox, AuthoringReportPresentation.Capabilities(_capabilityReportProjectName, capability));
        if (_contentScript is { IsDisposed: false } && FindNamed<Label>(_contentScript, "ScriptEditorStatus") is { } status)
            status.Text = status.AccessibleName = _scriptEditorDirty ? L("Builder_Modified", "Modified") : "";
    }

    private Func<string>? _statusCaptionRenderer;
    private void SetStatusCaption(string key, string fallback, params object[] arguments)
    {
        var capturedArguments = arguments.ToArray();
        SetStatusPresentation(() => string.Format(Lang.LanguageManager.CurrentCulture, L(key, fallback), capturedArguments));
    }
    private void SetStatusPresentation(Func<string> renderer)
    {
        _sourceScanOwnsStatus = false; _statusCaptionRenderer = renderer; RefreshStatusCaption();
    }
    private void RefreshStatusCaption()
    {
        if (_statusCaptionRenderer is { } renderer && _statusLabel is { IsDisposed: false })
            _statusLabel.Text = _statusLabel.AccessibleName = renderer();
    }
    private void SetStatus(string text) { _statusCaptionRenderer = null; _sourceScanOwnsStatus = false; _statusLabel.Text = _statusLabel.AccessibleName = text; }

    private void RefreshRecents()
    {
        foreach (ToolStripItem item in _recentsBtn.DropDownItems.Cast<ToolStripItem>().ToArray()) item.Dispose();
        _recentsBtn.DropDownItems.Clear();
        var entries = RecentProjects.Load();
        if (entries.Count == 0)
        {
            var empty = new ToolStripMenuItem { Name = "Builder_NoRecentScripts", Enabled = false };
            C("Builder_NoRecentScripts", "No recent scripts").Bind(empty);
            _recentsBtn.DropDownItems.Add(empty); return;
        }
        foreach (var e in entries)
        {
            var item = new ToolStripMenuItem { Text = Path.GetFileName(e.Path), ToolTipText = e.Path };
            item.Click += (_, _) => OpenRecent(e.Path);
            _recentsBtn.DropDownItems.Add(item);
        }
        _recentsBtn.DropDownItems.Add(new ToolStripSeparator());
        var clearItem = new ToolStripMenuItem { Name = "Builder_ClearRecent" };
        C("Builder_ClearRecent", "Clear recent scripts").Bind(clearItem);
        clearItem.Click += (_, _) => { RecentProjects.Clear(); RefreshRecents(); };
        _recentsBtn.DropDownItems.Add(clearItem);
    }

    private void OpenRecent(string path)
    {
        if (!ConfirmDiscardEditorChanges()) return;
        if (!File.Exists(path)) { MessageBox.Show(ActionOwner, string.Format(L("Builder_FileNotFound", "File not found: {0}"), path), L("Builder_RecentScriptTitle", "Recent Script"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        LoadProject(path);
    }

    private void OnHistoryLanguageChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => OnHistoryLanguageChanged(sender, e)); return; }
        var focus = UiFocusBookmark.Capture(this);
        using var modalRefresh = ModalOwnerRefresh.Begin(this);
        var rtl = Lang.LanguageManager.CurrentCulture.TextInfo.IsRightToLeft;
        RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = rtl;
        focus?.Restore();
        _navSearchBox.PlaceholderText = _navSearchBox.AccessibleName = L("Builder_SearchSections", "Search sections…");
        UpdateTitle();
        PopulateResultSection(); PopulateWorkflowResult();
        RefreshLastBuildCaption();
        RefreshSourceScanCaptions(); RefreshComponentCaptions();
        RefreshWorkflowCaptions();
        _undoButton.Text = _undoButton.AccessibleName = L("History_Undo", "Undo");
        RefreshStatusCaption();
        _redoButton.Text = _redoButton.AccessibleName = L("History_Redo", "Redo");
        if (_comparisonButton is not null)
        {
            _comparisonButton.Text = _comparisonButton.AccessibleName = L("Comparison_Title", "Compare projects");
            _comparisonButton.ToolTipText = L("Comparison_Select", "Choose a baseline project");
        }
        if (_releaseButton is not null)
        {
            _releaseButton.Text = _releaseButton.AccessibleName = L("Release_Title", "Release overview");
            _releaseButton.ToolTipText = L("Release_Notice", "Generated assets do not prove deployment succeeded.");
        }
    }

    private void UpdateHistoryActions()
    {
        if (_undoButton is null) return;
        _undoButton.Enabled = _controller.CanUndo || _scriptEditorDirty;
        _redoButton.Enabled = _controller.CanRedo && !_scriptEditorDirty;
    }

    internal AuthoringHistoryRestore? RestoreAuthoringHistory(bool redo, Form? owner = null)
    {
        if (_activeContent is { IsDisposed: false }) CommitBindings(_activeContent);
        if (!ApplyScriptEditor()) return null;
        var restored = redo ? _controller.Redo() : _controller.Undo();
        if (restored is not null) SetStatusCaption(redo ? "History_Redo" : "History_Undo", redo ? "Redo" : "Undo");
        if (restored?.MissingCredentials > 0)
            MessageBox.Show(owner ?? ActionOwner,
                string.Format(L("History_Credentials", "{0} credential values require re-entry. Review the restored items before building."), restored.MissingCredentials),
                L("History_Undo", "Undo"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return restored;
    }

    internal bool ApplyScriptEditor(bool showSuccess = false)
    {
        if (_scriptPreviewBox == null || _scriptPreviewBox.IsDisposed || !_scriptEditorDirty)
            return true;

        var tempPath = Path.Combine(Path.GetTempPath(), $"beep-script-{Guid.NewGuid():N}.bsetup");
        try
        {
            File.WriteAllText(tempPath, _scriptPreviewBox.Text);
            var (project, error) = InstallerScriptSerializer.Load(tempPath);
            if (project == null)
            {
                MessageBox.Show(ActionOwner, error ?? L("Builder_ScriptParseFailed", "Script could not be parsed."), L("Builder_ScriptTitle", "Script"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            project.MarkDirty();
            _controller.ReplaceProject(project);
            _project = _controller.Project;
            OnProjectReloaded();
            _nav.SelectSection("script");
            _scriptEditorDirty = false;
            RefreshScriptPreview(force: true);
            SetStatusCaption("Builder_ScriptApplied", "Script applied.");
            if (showSuccess)
                MessageBox.Show(ActionOwner, L("Builder_ScriptApplied", "Script applied."), L("Builder_ScriptTitle", "Script"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (Exception ex) { Engine.Diag.Debug("PackageBuilderForm", "temp script cleanup failed", ex); }
        }
    }

    private void RefreshScriptPreview(bool force = false)
    {
        if (_scriptPreviewBox == null || _scriptPreviewBox.IsDisposed) return;
        if (_scriptEditorDirty && !force) return;
        _updatingScriptEditor = true;
        try
        {
            _scriptPreviewBox.Text = InstallerScriptSerializer.Write(_project);
            _scriptEditorDirty = false;
            var root = _contentScript ?? _scriptPreviewBox.Parent;
            if (root != null && FindNamed<Label>(root, "ScriptEditorStatus") is { } label)
                label.Text = "";
        }
        finally
        {
            _updatingScriptEditor = false;
            UpdateHistoryActions();
        }
    }
}
