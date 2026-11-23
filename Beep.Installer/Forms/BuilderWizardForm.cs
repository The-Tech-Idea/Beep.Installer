using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Ui;
using Beep.Installer.Lang;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>
/// The wizard-first authoring shell: a stepper over the "golden path" sections a build actually
/// needs, in order, with gated forward navigation -- the default, primary way to open the builder.
/// See plans/enhancements/P12_WIZARD_FIRST_IA_DESIGN.md §3.1-3.2.
///
/// Steps 1+ host the exact same section panels <see cref="PackageBuilderForm"/>'s Advanced mode
/// uses. They are built by a <see cref="PackageBuilderForm"/> instance this form owns but never
/// shows -- constructing it runs the same `Build*Section()` factories Advanced mode already has, so
/// no section's own UI is duplicated or rewritten; each step re-parents that section's panel into
/// this form's own step host instead. "Open full editor" simply shows that same instance for real.
/// </summary>
public sealed class BuilderWizardForm : Form
{
    private static readonly GuidedAuthoringStep[] GoldenPath =
    {
        new("project", "Guided_Project", "Project", new[] { "identity" }),
        new("files", "Guided_Files", "Files and components", new[] { "source", "includes", "components" }),
        new("install", "Guided_Install", "Install behavior", new[] { "layout", "prerequisites", "shortcuts", "registry" }),
        new("package", "Guided_Package", "Package and destination", new[] { "package", "output" }),
        new("signing", "Guided_Signing", "Signing", new[] { "codesign" }, Optional: true),
        new("review", "Guided_Review", "Review and build", Array.Empty<string>()),
        new("result", "Guided_Result", "Result", new[] { "result" }),
    };

    private readonly InstallerController _controller;
    private readonly AuthoringThemeBinding _themeBinding;
    private readonly PackageBuilderForm _advanced;
    private readonly bool _ownsAdvanced;
    private int _stepIndex = -1; // -1 = step 0, "new project"; 0..GoldenPath.Length-1 = GoldenPath

    private Panel _stepHost = null!;
    private Label _titleLabel = null!;
    private FlowLayoutPanel _stepperStrip = null!;
    private Button _backBtn = null!;
    private Button _nextBtn = null!;
    private Label _stepValidationLabel = null!;
    private Button _skipBtn = null!;
    private Button _advancedBtn = null!;
    private Button _saveBtn = null!;
    private Button _recoveryBtn = null!;
    private Button _undoBtn = null!, _redoBtn = null!;
    private Label _saveStateLabel = null!;
    private readonly ToolTip _saveStateTip = new();
    private Control? _ownedStepView;
    private readonly System.Collections.Generic.List<Control> _borrowedViews = new();
    private string? _sectionId;
    private string? _preferredSection;
    private bool _allowFilesOnly;

    // Step 0 fields (mirrors ProjectNewDialog's fields -- see P12 §3.2 step 0; not reparented from
    // that dialog because its own OK/Cancel buttons are bound to *its* DialogResult, which would
    // fight this form's non-modal Back/Next).
    private ComboBox _templateBox = null!;
    private ProjectTemplate? _creationTemplate;
    private (string Name, string Version, string Publisher, string Source, string TemplateId, object? Blueprint)? _initialCreationDraft;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<IWin32Window, string, string, DialogResult>? ConfirmClose { get; set; }
    private TextBox _nameBox = null!;
    private TextBox _versionBox = null!;
    private TextBox _publisherBox = null!;
    private TextBox _sourceBox = null!;
    private bool _projectCreated;
    private bool _advancedHandoff;
    internal string? AdvancedDiagnosticPath { get; private set; }

    /// <summary>Raised once the user asks for the full section browser + toolbar instead.</summary>
    public event EventHandler? AdvancedRequested;

    public BuilderWizardForm(InstallerController controller) : this(controller, new PackageBuilderForm(controller))
    {
        _ownsAdvanced = true;
    }

    /// <summary>
    /// Reuses an already-open <see cref="PackageBuilderForm"/> as the hidden section-panel source
    /// instead of constructing a second one against the same controller -- what Advanced mode's own
    /// "Guided" button (P12 12.D.3) needs: switching from Advanced to Guided must not leave two
    /// PackageBuilderForm instances both subscribed to the same InstallerController's events.
    /// </summary>
    public BuilderWizardForm(InstallerController controller, PackageBuilderForm existingAdvanced, bool startNew = false)
    {
        _controller = controller;
        _advanced = existingAdvanced;
        _advanced.BuildResultRecorded += OnBuildResultRecorded;
        _advanced.BuildStarted += OnBuildResultRecorded;
        _projectCreated = !startNew && (controller.HasFilePath || controller.IsDirty || controller.SaveState.IsRecovered || HasRealProject(controller.Project));

        InitializeUi();
        _themeBinding = new AuthoringThemeBinding(this);

        // Coming from Advanced mode's own "Guided" button: land on the matching step instead of
        // always restarting at Identity, so switching views does not lose where the user was.
        _preferredSection = existingAdvanced.ActiveSectionId;
        var startIndex = Array.FindIndex(GoldenPath, s => s.Sections.Contains(_preferredSection));
        GoTo(startNew ? -1 : (startIndex >= 0 ? startIndex : (_projectCreated ? 0 : -1)));
        LanguageManager.LanguageChanged += OnLanguageChanged;
        _controller.PropertyChanged += OnSaveStateChanged;
        UpdateSaveState();
    }

    /// <summary>A controller from CreateColdStartController's reopen-most-recent path already has a
    /// real project; step 0 is only for a genuinely fresh one.</summary>
    private static bool HasRealProject(InstallProject project)
        => !string.IsNullOrWhiteSpace(project.SourceDirectory) || project.Components.Count > 0
            || project.Resources.Count + project.WindowsServices.Count + project.ScheduledTasks.Count
            + project.FirewallRules.Count + project.IisSites.Count + project.IisAppPools.Count
            + project.ConfigTransforms.Count + project.Certificates.Count + project.ComRegistrations.Count
            + project.DriverPackages.Count + project.EnvironmentVariables.Count + project.FileAssociations.Count
            + project.WebDeployPackages.Count + project.Packages.Count + project.Shortcuts.Count
            + project.RegistryEntries.Count + project.Prerequisites.Count + project.CustomPages.Count
            + project.CustomActions.Count > 0;

    /// <summary>The hidden Advanced-mode form, so the caller can show it after "Open full editor"
    /// without constructing a second one against the same controller.</summary>
    public PackageBuilderForm AdvancedForm => _advanced;

    internal string? CurrentSectionId => _stepIndex < 0 ? null : _sectionId;
    internal string CurrentStepId => _stepIndex < 0 ? "project" : GoldenPath[_stepIndex].Id;

    private void InitializeUi()
    {
        Text = L("Wizard_BeepInstaller", "Beep Installer");
        Size = new Size(1000, 700);
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Font = InstallerTheme.Body;
        Icon = SystemIcons.Application;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Padding = new Padding(14, 10, 14, 6), BackColor = InstallerTheme.Sidebar };
        AuthoringThemeBinding.SetSurface(header, AuthoringSurface.Sidebar);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _titleLabel = new Label { AutoSize = true, Font = InstallerTheme.SectionTitle, Anchor = AnchorStyles.Left };
        _advancedBtn = new Button { Text = L("Wizard_OpenFullEditor", "Open full editor"), AutoSize = true };
        _advancedBtn.Click += (_, _) =>
        {
            if (_stepIndex <= 0 && !TryCreateProject()) return;
            PrepareAdvancedHandoff();
            _advancedHandoff = true;
            AdvancedRequested?.Invoke(this, EventArgs.Empty);
        };
        header.Controls.Add(_titleLabel, 0, 0);
        _saveBtn = new Button { Text = L("Common_Save", "Save"), AutoSize = true };
        _saveBtn.Click += (_, _) => SaveDraft();
        header.Controls.Add(_saveBtn, 1, 0);
        _recoveryBtn = new Button { Name = "GuidedRecovery", Text = L("Recovery_Title", "Recover unsaved project"), AutoSize = true };
        _recoveryBtn.AccessibleName = _recoveryBtn.Text;
        _recoveryBtn.Click += (_, _) => RecoverProject();
        header.Controls.Add(_recoveryBtn, 2, 0);
        header.Controls.Add(_advancedBtn, 3, 0);
        _saveStateLabel = new Label { Name = "AuthoringSaveState", AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(3, 6, 3, 3) };
        header.Controls.Add(_saveStateLabel, 0, 1);
        header.SetColumnSpan(_saveStateLabel, 4);
        var historyButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        var undo = new Button { Name = "GuidedUndo", Text = L("History_Undo", "Undo"), AutoSize = true };
        var redo = new Button { Name = "GuidedRedo", Text = L("History_Redo", "Redo"), AutoSize = true };
        _undoBtn = undo; _redoBtn = redo;
        undo.Click += (_, _) => RestoreAuthoringHistory(false);
        redo.Click += (_, _) => RestoreAuthoringHistory(true);
        historyButtons.Controls.AddRange(new Control[] { undo, redo });
        var help = new Button { Name = "GuidedHelp", Text = LanguageManager.GetString("Help_Title"), AutoSize = true };
        new LiveUiCaption("Help_Title", "Help").Bind(help);
        help.Click += (_, _) => ShowContextHelp(); historyButtons.Controls.Add(help);
        var languages = new Button { Name = "GuidedLanguages", AutoSize = true };
        new LiveUiCaption("Toolbar_Languages", "Languages").Bind(languages);
        languages.Click += (_, _) => { using var dialog = new AuthoringLanguageDialog(); dialog.ShowDialog(this); };
        historyButtons.Controls.Add(languages);
        var mcp = new Button { Name = "GuidedMcp", AutoSize = true };
        new LiveUiCaption("Toolbar_Mcp", "AI / MCP").Bind(mcp);
        mcp.AccessibleDescription = LanguageManager.GetOrDefault("Toolbar_McpHint", "Use this installer from Claude, Codex and other AI assistants (MCP server)");
        mcp.Click += (_, _) => ShowMcpDialog();
        historyButtons.Controls.Add(mcp);
        header.Controls.Add(historyButtons, 0, 2);
        header.SetColumnSpan(historyButtons, 4);

        _stepperStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(14, 0, 14, 6), BackColor = InstallerTheme.Sidebar };
        AuthoringThemeBinding.SetSurface(_stepperStrip, AuthoringSurface.Sidebar);

        _stepHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = InstallerTheme.Panel };

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(14, 8, 14, 10) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _backBtn = new Button { Text = L("Wizard_Back", "< Back"), AutoSize = true };
        _backBtn.Click += (_, _) => GoTo(_stepIndex - 1);
        _stepValidationLabel = new Label { AutoSize = true, MaximumSize = new Size(480, 0), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = InstallerTheme.Danger };
        AuthoringThemeBinding.SetTone(_stepValidationLabel, AuthoringTone.Danger);
        _skipBtn = new Button { Text = L("Guided_ContinueUnsigned", "Continue without signing"), AutoSize = true };
        _skipBtn.Click += (_, _) => NavigateTo(_stepIndex + 1);
        _nextBtn = new Button { Text = L("Wizard_Next", "Next >"), AutoSize = true };
        _nextBtn.Click += (_, _) => OnNext();
        footer.Controls.Add(_backBtn, 0, 0);
        footer.Controls.Add(_stepValidationLabel, 1, 0);
        footer.Controls.Add(_skipBtn, 2, 0);
        footer.Controls.Add(_nextBtn, 3, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_stepperStrip, 0, 1);
        root.Controls.Add(_stepHost, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);

        // The menu bar is added after the fill layout so it docks above it.
        var menu = new MenuStrip { Name = "GuidedMenu" };
        var tools = new ToolStripMenuItem { Name = "GuidedMenuTools" };
        new LiveUiCaption("Menu_Tools", "Tools").Bind(tools);
        var mcpItem = new ToolStripMenuItem { Name = "GuidedMenuMcp", ShortcutKeys = Keys.Control | Keys.Shift | Keys.M };
        new LiveUiCaption("Menu_McpServer", "AI / MCP server…").Bind(mcpItem);
        mcpItem.Click += (_, _) => ShowMcpDialog();
        tools.DropDownItems.Add(mcpItem);
        menu.Items.Add(tools);
        MainMenuStrip = menu;
        Controls.Add(menu);

        BuildStepperStrip();
        Engine.Accessibility.Attach(this);
    }

    private void ShowMcpDialog()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("The installer cannot locate its own executable.");
        using var dialog = new McpServerDialog(exe);
        dialog.ShowDialog(this);
    }

    private void BuildStepperStrip()
    {
        foreach (var oldButton in _stepperStrip.Controls.Cast<Control>().ToArray()) oldButton.Dispose();
        _stepperStrip.Controls.Clear();
        for (var i = 0; i < GoldenPath.Length; i++)
            _stepperStrip.Controls.Add(MakeStepperButton(i, L(GoldenPath[i].TitleKey, GoldenPath[i].Title)));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke(() => OnLanguageChanged(sender, e)); return; }

        var focus = UiFocusBookmark.Capture(this);
        using var modalRefresh = ModalOwnerRefresh.Begin(this);

        // The project fields are an uncommitted draft. Rebuilding captions must retain their values.
        var draft = _stepIndex <= 0
            ? new[] { _nameBox.Text, _versionBox.Text, _publisherBox.Text, _sourceBox.Text }
            : null;
        var selectedTemplate = _stepIndex <= 0 ? _templateBox.SelectedItem as ProjectTemplate : null;
        _preferredSection = _sectionId;
        Text = L("Wizard_BeepInstaller", "Beep Installer");
        _advancedBtn.Text = L("Wizard_OpenFullEditor", "Open full editor");
        _saveBtn.Text = L("Common_Save", "Save");
        _saveBtn.AccessibleName = _saveBtn.Text;
        _recoveryBtn.Text = _recoveryBtn.AccessibleName = L("Recovery_Title", "Recover unsaved project");
        _undoBtn.Text = _undoBtn.AccessibleName = L("History_Undo", "Undo");
        _redoBtn.Text = _redoBtn.AccessibleName = L("History_Redo", "Redo");
        UpdateSaveState();
        _backBtn.Text = L("Wizard_Back", "< Back");
        _skipBtn.Text = L("Guided_ContinueUnsigned", "Continue without signing");
        BuildStepperStrip();
        if (_ownedStepView is GuidedReviewPage review)
        {
            _titleLabel.Text = L("Guided_Review", "Review and build");
            review.RefreshCaptions();
        }
        else GoTo(_stepIndex);
        if (draft is not null)
        {
            _nameBox.Text = draft[0]; _versionBox.Text = draft[1];
            _publisherBox.Text = draft[2]; _sourceBox.Text = draft[3];
            if (selectedTemplate?.Blueprint is not null && !_templateBox.Items.Cast<ProjectTemplate>().Any(template => template.Id == selectedTemplate.Id)) _templateBox.Items.Add(selectedTemplate);
            _templateBox.SelectedItem = _templateBox.Items.Cast<ProjectTemplate>().FirstOrDefault(template => template.Id == selectedTemplate?.Id);
        }
        foreach (Button button in _stepperStrip.Controls) button.AccessibleName = button.Text;
        _advancedBtn.AccessibleName = _advancedBtn.Text;
        _backBtn.AccessibleName = _backBtn.Text;
        _skipBtn.AccessibleName = _skipBtn.Text;
        _nextBtn.AccessibleName = _nextBtn.Text;
        if (focus is not null && !string.IsNullOrEmpty(focus.ControlName) &&
            Controls.Find(focus.ControlName, true) is { Length: 1 } matches)
            focus.RestoreReplacement(matches[0]);
        else focus?.Restore();
    }

    private Button MakeStepperButton(int index, string title)
    {
        var button = new Button { Name = "GuidedStep_" + GoldenPath[index].Id, Text = title, AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 6, 0), Tag = index };
        button.Click += (_, _) =>
        {
            if (!_projectCreated && index == 0) return;
            NavigateTo(index);
        };
        return button;
    }

    private void OnNext()
    {
        NavigateTo(_stepIndex < 0 ? 1 : _stepIndex + 1);
    }

    private void NavigateTo(int target)
    {
        target = Math.Clamp(target, -1, GoldenPath.Length - 1);
        if (target == _stepIndex) return;
        if (target == GoldenPath.Length - 1 && (_advanced.IsBuilding || _advanced.BuildWorkspace.Snapshot.Result is not null))
        {
            GoTo(target);
            return;
        }
        if (target <= _stepIndex) { GoTo(target); return; }

        // Evaluate every intervening step, using the same path as Next. A jump must not
        // bypass project creation or a collection error just because the current field is valid.
        while (_stepIndex < target)
        {
            if (_stepIndex == -1)
            {
                if (!TryCreateProject()) return;
            }
            else if (!CanAdvancePastCurrentStep(out var reason))
            {
                _stepValidationLabel.Text = reason;
                return;
            }
            GoTo(_stepIndex + 1);
        }
    }

    /// <summary>
    /// Gates forward movement using typed section diagnostics, including collection errors.
    /// Files-only projects can continue when the author explicitly selects that path.
    /// </summary>
    private bool CanAdvancePastCurrentStep(out string reason)
    {
        try
        {
            if (_stepIndex == 0 && !TryCreateProject()) { reason = _stepValidationLabel.Text; return false; }
            if (_stepIndex > 0)
            {
                var active = _advanced.ActiveSectionDiagnostics();
                var unavailable = active.FirstOrDefault(d => d.Code == "BIUI001");
                if (unavailable is not null) { reason = unavailable.Message; return false; }
            }
            var diagnostics = ProjectSchemaService.Validate(_controller.Project).Diagnostics;
            var sections = GoldenPath[_stepIndex].Sections;
            var packageDiagnostics = CurrentStepId == "package" ? PackageTargetDiagnostics.Validate(_controller.Project) : Array.Empty<ProjectSchemaDiagnostic>();
            diagnostics.AddRange(packageDiagnostics);
            var diagnostic = diagnostics
                .Where(d => sections.Contains(AuthoringDiagnosticNavigation.SectionForPath(d.Path)) ||
                    packageDiagnostics.Contains(d))
                .FirstOrDefault(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error);
            if (diagnostic is not null)
            {
                reason = diagnostic.Message;
                FocusBlockingDiagnostic(diagnostic.Path);
                return false;
            }

            if (CurrentStepId == "files" && _controller.Project.Components.Count == 0 && !_allowFilesOnly)
            {
                reason = L("Wizard_NoComponentsYet", "Add at least one component before continuing.");
                FocusBlockingDiagnostic("Components");
                return false;
            }

            reason = "";
            return true;
        }
        catch (Exception ex)
        {
            Diag.Warn("GuidedValidation", "Step validation unavailable", ex);
            reason = L("Wizard_ValidationUnavailable", "Validation is unavailable. Try again before continuing.");
            return false;
        }
    }

    private void FocusBlockingDiagnostic(string path)
    {
        if (_stepIndex <= 0)
        {
            if (_ownedStepView is not null) DiagnosticEditorNavigator.Focus(_ownedStepView, path);
            return;
        }

        // Select an existing tab instead of rebuilding the step: rebuilding discards drafts
        // and clears the reason for blocking.
        var section = AuthoringDiagnosticNavigation.SectionForPath(path);
        var tabs = _stepHost.Controls.Find("GuidedSections", true).OfType<TabControl>().FirstOrDefault();
        var page = tabs?.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Name == section);
        if (page is null)
        {
            // Package capability checks can refer to an earlier source/output step. Use the
            // existing Guided route, then let NavigateTo retain the reason on that destination.
            if (GoldenPath.Any(step => step.Sections.Contains(section))) EditDiagnostic(path);
            return;
        }
        tabs!.SelectedTab = page;
        if (path == "Components")
            page.Controls.Find("ComponentsAdd", true).FirstOrDefault()?.Focus();
        else
            _advanced.FocusDiagnostic(path);
    }

    private void GoTo(int index)
    {
        index = Math.Clamp(index, -1, GoldenPath.Length - 1);
        _stepIndex = index;
        _stepValidationLabel.Text = "";

        ReleaseStepView();
        _stepHost.Controls.Clear();
        _sectionId = index < 0 ? null : GoldenPath[index].Sections.FirstOrDefault();
        if (index <= 0)
        {
            _titleLabel.Text = L("Guided_Project", "Project");
            _ownedStepView = BuildNewProjectStep();
            _stepHost.Controls.Add(_ownedStepView);
        }
        else if (CurrentStepId == "review")
        {
            _titleLabel.Text = L("Guided_Review", "Review and build");
            _ownedStepView = new GuidedReviewPage(_controller.Project, EditDiagnostic, () => _advanced.StartGuidedBuild(this), OpenResourceRecipes, () => _advanced.ShowReleaseOverview(this), () => _advanced.ShowProjectComparison(this), _controller);
            _stepHost.Controls.Add(_ownedStepView);
        }
        else if (CurrentStepId == "result" && _advanced.BuildWorkspace.Snapshot.Id != Guid.Empty)
        {
            _titleLabel.Text = L("Workspace_Title", "Build workspace");
            _ownedStepView = new BuildWorkspaceView(_advanced.BuildWorkspace, info => _advanced.ArtifactLauncher(info));
            _stepHost.Controls.Add(_ownedStepView);
        }
        else
        {
            var step = GoldenPath[index];
            _titleLabel.Text = L(step.TitleKey, step.Title);
            _ownedStepView = BuildSectionTabs(step);
            _stepHost.Controls.Add(_ownedStepView);
        }

        _backBtn.Enabled = index > 0;
        _nextBtn.Text = index == GoldenPath.Length - 1
            ? L("Wizard_Finish", "Finish")
            : L("Wizard_Next", "Next >");
        _nextBtn.Visible = index < GoldenPath.Length - 2;
        _skipBtn.Visible = index >= 0 && GoldenPath[index].Optional && !_controller.Project.HasCodeSigningCertificate;

        foreach (Button b in _stepperStrip.Controls)
        {
            b.FlatStyle = (int)b.Tag! == Math.Max(0, index) ? FlatStyle.Popup : FlatStyle.Flat;
            AuthoringThemeBinding.SetSurface(b, (int)b.Tag! == Math.Max(0, index) ? AuthoringSurface.Accent : AuthoringSurface.Sidebar);
            b.Enabled = (int)b.Tag != GoldenPath.Length - 1 || _advanced.LastBuildResult is not null || _advanced.IsBuilding || _advanced.BuildWorkspace.Snapshot.Result is not null;
        }
        Engine.Accessibility.ApplyAutoNames(this);
        RtlHelper.ApplyDirection(this, RtlHelper.IsRtl(LanguageManager.CurrentCulture.TwoLetterISOLanguageName));
    }

    private Control BuildSectionTabs(GuidedAuthoringStep step)
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Name = "GuidedSections", AccessibleName = _titleLabel.Text };
        foreach (var id in step.Sections)
            tabs.TabPages.Add(new TabPage(SectionTitle(id)) { Name = id, Tag = id });
        var preferred = tabs.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Name == _preferredSection);
        if (preferred is not null) tabs.SelectedTab = preferred;
        void ShowSelected()
        {
            if (tabs.SelectedTab is not { } page) return;
            _sectionId = page.Name;
            _advanced.OpenSection(page.Name);
            if (_advanced.ActiveSection is not { } panel) return;
            _borrowedViews.Add(panel);
            page.Controls.Add(panel);
        }
        tabs.SelectedIndexChanged += (_, _) => ShowSelected();
        ShowSelected();
        _preferredSection = null;
        var wrap = new Panel { Dock = DockStyle.Fill };
        wrap.Controls.Add(tabs);
        if (step.Id == "files")
        {
            var filesOnly = new CheckBox { Name = "FilesOnly", Text = L("Guided_FilesOnly", "Use source files without defining optional components"),
                AutoSize = true, Dock = DockStyle.Bottom, Checked = _allowFilesOnly };
            filesOnly.CheckedChanged += (_, _) => _allowFilesOnly = filesOnly.Checked;
            wrap.Controls.Add(filesOnly);
        }
        if (step.Id == "install")
        {
            wrap.Controls.Add(new Label { Text = L("Guided_OptionalBehavior", "Prerequisites, shortcuts and registry entries are optional. Leave their lists empty when they are not needed."), AutoSize = true, Dock = DockStyle.Bottom });
            var recipes = new Button { Name = "GuidedResourceRecipes", Text = L("Recipe_Title", "Resource recipes"), AutoSize = true, Dock = DockStyle.Bottom };
            recipes.Click += (_, _) => OpenResourceRecipes();
            wrap.Controls.Add(recipes);
        }
        return wrap;
    }

    private void OpenResourceRecipes()
    {
        if (_advanced.ShowResourceRecipe(owner: this)) GoTo(_stepIndex);
    }

    private static string SectionTitle(string id) => id switch
    {
        "source" => L("Nav_Source", "Source"), "includes" => L("Nav_Includes", "Includes and excludes"),
        "components" => L("Nav_Components", "Components"), "layout" => L("Nav_Layout", "Scope and destination"),
        "prerequisites" => L("Section_Prerequisites", "Prerequisites"), "shortcuts" => L("Section_Shortcuts", "Shortcuts"),
        "registry" => L("Section_Registry", "Registry"), "package" => L("Packaging_Title", "Output format"),
        "output" => L("Nav_Output", "Output"), "codesign" => L("Guided_Signing", "Signing"),
        "result" => L("Guided_Result", "Result"), _ => id
    };

    private void ReleaseStepView()
    {
        foreach (var view in _borrowedViews) view.Parent?.Controls.Remove(view);
        _borrowedViews.Clear();
        _ownedStepView?.Dispose();
        _ownedStepView = null;
    }

    private void EditDiagnostic(string path)
    {
        var section = AuthoringDiagnosticNavigation.SectionForPath(path);
        if (section == "customactions")
        {
            _advanced.EditCustomActionDiagnostic(path, this);
            if (CurrentStepId == "review") GoTo(_stepIndex);
            return;
        }
        var index = Array.FindIndex(GoldenPath, s => s.Sections.Contains(section));
        if (index < 0)
        {
            ReleaseStepView();
            AdvancedDiagnosticPath = path;
            _advancedHandoff = true;
            _advanced.OpenDiagnostic(path);
            AdvancedRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        _preferredSection = section;
        GoTo(index);
        if (index == 0)
        {
            var name = path.Split('.').Last();
            _ownedStepView?.Controls.Find(name, searchAllChildren: true).FirstOrDefault()?.Focus();
        }
        else if (_sectionId is not null) _advanced.FocusDiagnostic(path);
    }

    private void OnBuildResultRecorded(object? sender, EventArgs e)
    {
        if (!IsDisposed) GoTo(GoldenPath.Length - 1);
    }

    private bool SaveDraft()
    {
        if (_stepIndex <= 0 && !TryCreateProject()) return false;
        return _advanced.SaveGuidedProject(this, _stepHost);
    }

    internal bool RecoverProject(Func<string?>? selectDraft = null, Func<bool>? confirmChanges = null)
    {
        if (_advanced.IsBuilding)
        {
            _stepValidationLabel.Text = L("Guided_WaitForBuild", "Wait for the build to finish, or cancel it from the build workspace.");
            return false;
        }
        PackageBuilderForm.CommitBindings(_stepHost);
        var confirmed = confirmChanges ?? (() => _controller.ConfirmDiscardChanges(this, SaveDraft,
            HasProjectDraftChanges() || _advanced.HasPendingScriptChanges));
        if (!_advanced.RecoverProject(this, confirmed, selectDraft)) return false;
        _projectCreated = true;
        _allowFilesOnly = false;
        _preferredSection = "identity";
        GoTo(0);
        UpdateSaveState();
        return true;
    }

    private void OnSaveStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        if (InvokeRequired) { BeginInvoke(UpdateSaveState); return; }
        UpdateSaveState();
    }

    private void UpdateSaveState()
    {
        _undoBtn.Enabled = _projectCreated && (_controller.CanUndo || HasProjectDraftChanges());
        _redoBtn.Enabled = _projectCreated && _controller.CanRedo && !HasProjectDraftChanges();
        var state = _controller.SaveState;
        if (HasProjectDraftChanges()) state = state with { IsDirty = true };
        _saveStateLabel.Text = SaveStatePresentation.Caption(state);
        _saveStateLabel.AccessibleName = _saveStateLabel.Text;
        _saveStateLabel.AccessibleDescription = SaveStatePresentation.Details(state);
        _saveStateTip.SetToolTip(_saveStateLabel, _saveStateLabel.AccessibleDescription);
    }

    private (string Name, string Version, string Publisher, string Source, string TemplateId, object? Blueprint) CaptureCreationDraft()
    {
        var template = _templateBox.SelectedItem as ProjectTemplate;
        return (_nameBox.Text.Trim(), _versionBox.Text.Trim(), _publisherBox.Text.Trim(), _sourceBox.Text.Trim(), template?.Id ?? "", template?.Blueprint);
    }

    private bool HasProjectDraftChanges()
    {
        if (_stepIndex > 0 || _nameBox is null) return false;
        if (!_projectCreated) return _initialCreationDraft is { } initial && CaptureCreationDraft() != initial;
        return _nameBox.Text.Trim() != _controller.Project.AppName ||
            _versionBox.Text.Trim() != _controller.Project.AppVersion ||
            _publisherBox.Text.Trim() != _controller.Project.AppPublisher ||
            _sourceBox.Text.Trim() != _controller.Project.SourceDirectory;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F1) { ShowContextHelp(); return true; }
        if (keyData == (Keys.Control | Keys.F)) { ShowContextHelp(focusSearch: true); return true; }
        if (keyData == (Keys.Control | Keys.Z)) { if (!AuthoringHistoryKeyboard.UndoFocusedText(this)) RestoreAuthoringHistory(false); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { RestoreAuthoringHistory(true); return true; }
        if (keyData == (Keys.Control | Keys.S)) { SaveDraft(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ShowContextHelp(bool focusSearch = false)
    {
        var context = _sectionId ?? (CurrentStepId switch { "review" => "build", "result" => "result", "project" => "identity", _ => "source" });
        using var dialog = new AuthoringHelpDialog(context, focusSearch); dialog.ShowDialog(this);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_advanced.IsBuilding)
        {
            e.Cancel = true;
            _stepValidationLabel.Text = L("Guided_WaitForBuild", "Wait for the build to finish, or cancel it from the build workspace.");
        }
        else if (_ownsAdvanced && !_advancedHandoff && (_controller.IsDirty || HasProjectDraftChanges()))
        {
            var message = L("SaveState_SaveFirst", "You have unsaved changes. Save first?");
            var answer = ConfirmClose?.Invoke(this, message, Text) ?? MessageBox.Show(this, message,
                Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel || (answer == DialogResult.Yes && !SaveDraft())) e.Cancel = true;
        }
        base.OnFormClosing(e);
    }

    private Panel BuildNewProjectStep()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

        int r = 0;
        layout.Controls.Add(FieldLabel(L("NewProject_Template", "Template:")), 0, r);
        _templateBox = new ComboBox { Name = "CreationTemplate", AccessibleName = L("NewProject_Template", "Template:"), Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var t in ProjectTemplates.Builtins) _templateBox.Items.Add(t);
        if (_creationTemplate?.Blueprint is not null) _templateBox.Items.Add(_creationTemplate);
        _templateBox.SelectedItem = _templateBox.Items.Cast<ProjectTemplate>().FirstOrDefault(template => template.Id == _creationTemplate?.Id) ?? _templateBox.Items[0];
        _templateBox.SelectedIndexChanged += (_, _) => { _creationTemplate = _templateBox.SelectedItem as ProjectTemplate; UpdateSaveState(); };
        _templateBox.Enabled = !_projectCreated;
        var templateRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        templateRow.ColumnStyles.Add(new(SizeType.Percent, 100)); templateRow.ColumnStyles.Add(new(SizeType.AutoSize));
        var gallery = Ui.TemplateGallerySelection.CreateButton(_templateBox); gallery.Enabled = !_projectCreated;
        templateRow.Controls.Add(_templateBox, 0, 0); templateRow.Controls.Add(gallery, 1, 0);
        layout.SetColumnSpan(templateRow, 2);
        layout.Controls.Add(templateRow, 1, r++);

        // Prefill from the controller's current project once it has been created, so revisiting this
        // step (Back, or the stepper strip) reflects what New()/Identity actually set instead of
        // showing the original placeholder defaults again.
        var project = _controller.Project;

        layout.Controls.Add(FieldLabel(L("NewProject_ProductName", "Product name:")), 0, r);
        _nameBox = new TextBox { Name = "AppName", AccessibleName = L("NewProject_ProductName", "Product name:"), Dock = DockStyle.Fill, Text = _projectCreated ? project.AppName : "MyApplication" };
        layout.SetColumnSpan(_nameBox, 2);
        layout.Controls.Add(_nameBox, 1, r++);

        layout.Controls.Add(FieldLabel(L("NewProject_Version", "Version:")), 0, r);
        _versionBox = new TextBox { Name = "AppVersion", AccessibleName = L("NewProject_Version", "Version:"), Dock = DockStyle.Fill, Text = _projectCreated ? project.AppVersion : "1.0.0" };
        layout.SetColumnSpan(_versionBox, 2);
        layout.Controls.Add(_versionBox, 1, r++);

        layout.Controls.Add(FieldLabel(L("NewProject_Publisher", "Publisher:")), 0, r);
        _publisherBox = new TextBox { Name = "AppPublisher", AccessibleName = L("NewProject_Publisher", "Publisher:"), Dock = DockStyle.Fill, Text = _projectCreated ? project.AppPublisher : Environment.UserName };
        layout.SetColumnSpan(_publisherBox, 2);
        layout.Controls.Add(_publisherBox, 1, r++);

        layout.Controls.Add(FieldLabel(L("NewProject_SourceDirectory", "Source directory:")), 0, r);
        _sourceBox = new TextBox { Name = "SourceDirectory", AccessibleName = L("NewProject_SourceDirectory", "Source directory:"), Dock = DockStyle.Fill, Text = _projectCreated ? project.SourceDirectory : "" };
        var browseBtn = new Button { Name = "BrowseCreationSource", Text = "…", Dock = DockStyle.Fill,
            AccessibleName = AuthoringFileDialogs.BrowseCaption(L("NewProject_SourceDirectory", "Source directory:")) };
        browseBtn.Click += (_, _) =>
        {
            using var d = AuthoringFileDialogs.Folder(L("NewProject_SourceDirectory", "Source directory:"), _sourceBox.Text);
            if (d.ShowDialog(this) == DialogResult.OK) _sourceBox.Text = d.SelectedPath;
        };
        layout.Controls.Add(_sourceBox, 1, r);
        layout.Controls.Add(browseBtn, 2, r++);
        foreach (var field in new[] { _nameBox, _versionBox, _publisherBox, _sourceBox })
            field.TextChanged += (_, _) => UpdateSaveState();

        var hint = new Label
        {
            Text = L("NewProject_TheSourceDirectoryContains", "The source directory contains the files that will be installed (e.g. your app's bin/Release output)."),
            Dock = DockStyle.Fill,
            Name = "CreationSourceHelp",
            ForeColor = InstallerTheme.MutedText,
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 6),
        };
        AuthoringThemeBinding.SetTone(hint, AuthoringTone.Muted);
        layout.SetColumnSpan(hint, 3);
        layout.Controls.Add(hint, 0, r);

        if (!_projectCreated && _initialCreationDraft is null) _initialCreationDraft = CaptureCreationDraft();
        for (var row = 0; row <= r; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var wrap = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        wrap.Controls.Add(layout);
        return wrap;
    }

    private static Label FieldLabel(string text)
        => new() { Text = text, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, AutoSize = true, MinimumSize = new Size(0, 28) };

    /// <summary>
    /// The step host holds whatever <see cref="_advanced"/> section panel is currently visible --
    /// re-parented out of <see cref="_advanced"/>'s own content host in <see cref="GoTo"/>. Before
    /// handing off to Advanced mode, put it back so <see cref="_advanced"/> shows something instead
    /// of an empty content area (re-parenting is idempotent: <c>OpenSection</c> re-adds the same
    /// cached panel, silently detaching it from wherever it currently lives).
    /// </summary>
    private void PrepareAdvancedHandoff()
    {
        var id = _sectionId ?? "identity";
        ReleaseStepView();
        _advanced.OpenSection(id);
    }

    internal void RestoreAuthoringHistory(bool redo)
    {
        if (!_projectCreated) return;
        PackageBuilderForm.CommitBindings(_stepHost);
        if (_stepIndex <= 0 && !TryCreateProject()) return;
        ReleaseStepView();
        _advanced.RestoreAuthoringHistory(redo, this);
        GoTo(_stepIndex);
        UpdateSaveState();
    }

    private bool TryCreateProject()
    {
        var name = _nameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _stepValidationLabel.Text = L("Builder_ProductNameRequired", "Product name is required.");
            _nameBox.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(_versionBox.Text))
        {
            _stepValidationLabel.Text = L("Builder_VersionRequired", "Version is required.");
            _versionBox.Focus();
            return false;
        }

        var template = _templateBox.SelectedItem as ProjectTemplate ?? new ProjectTemplate { Id = ProjectTemplates.EmptyId };
        if (!_projectCreated)
            _controller.New(template, name, _versionBox.Text.Trim(), _publisherBox.Text.Trim(), _sourceBox.Text.Trim());
        else
        {
            // Revisiting edits the same graph: retain AppId, components, file path and resources.
            _controller.RunAuthoringChange("Project details", () =>
            {
                _controller.Project.AppName = name;
                _controller.Project.AppVersion = _versionBox.Text.Trim();
                _controller.Project.AppPublisher = _publisherBox.Text.Trim();
                _controller.Project.SourceDirectory = _sourceBox.Text.Trim();
            });
        }
        _projectCreated = true;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) LanguageManager.LanguageChanged -= OnLanguageChanged;
        if (disposing)
        {
            _themeBinding.Dispose();
            _controller.PropertyChanged -= OnSaveStateChanged;
            _saveStateTip.Dispose();
        }
        if (disposing && !_advanced.IsDisposed)
        {
            _advanced.BuildResultRecorded -= OnBuildResultRecorded;
            _advanced.BuildStarted -= OnBuildResultRecorded;
            // Section controls belong to the editor cache. Reclaim them before Form.Dispose
            // recursively disposes the wizard's children, even when the caller closes via X.
            var section = CurrentSectionId;
            ReleaseStepView();
            if (section is not null) _advanced.OpenSection(section);
            if (_ownsAdvanced)
            {
                _advanced.Dispose();
                _controller.StopAutoSave();
            }
        }
        base.Dispose(disposing);
    }
}
