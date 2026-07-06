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
    private static readonly Color ShellBackColor = Color.FromArgb(246, 248, 251);
    private static readonly Color PanelBackColor = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(220, 225, 232);
    private static readonly Color TextColor = Color.FromArgb(32, 38, 46);
    private static readonly Color MutedTextColor = Color.FromArgb(98, 107, 119);
    private static readonly Color AccentColor = Color.FromArgb(37, 99, 235);
    private static readonly Font UiFont = new("Segoe UI", 9F);
    private static readonly Font UiFontBold = new("Segoe UI", 9F, FontStyle.Bold);
    private static readonly Font SectionTitleFont = new("Segoe UI", 13F, FontStyle.Bold);

    private readonly InstallerController _controller;
    private InstallProject _project;

    private ToolStrip _toolbar = null!;
    private ToolStripDropDownButton _recentsBtn = null!;
    private StatusStrip _status = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripStatusLabel _dirtyLabel = null!;
    private ToolStripStatusLabel _buildResultLabel = null!;
    private BuildPipeline.BuildResult? _lastBuildResult;
    private BuildProgressForm? _activeBuildProgressForm;
    private ToolStripProgressBar _progress = null!;

    private LeftNavPanel _nav = null!;
    private Panel _contentHost = null!;
    private Panel? _activeContent;

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

    private TextBox _sourceDirBox = null!;
    private TreeView _fileTree = null!;
    private Label _fileStatsLabel = null!;
    private ListBox _includeList = null!, _excludeList = null!;
    private TextBox _includeBox = null!, _excludeBox = null!;
    private DataGridView _componentsGrid = null!;
    private PropertyGrid _componentProps = null!;
    private BindingSource _componentsBinding = null!;
    private DataGridView _prereqGrid = null!;
    private PropertyGrid _prereqProps = null!;
    private BindingSource _prereqsBinding = null!;
    private DataGridView _shortcutsGrid = null!;
    private PropertyGrid _shortcutProps = null!;
    private BindingSource _shortcutsBinding = null!;
    private DataGridView _registryGrid = null!;
    private PropertyGrid _registryProps = null!;
    private BindingSource _registryBinding = null!;
    private CheckedListBox _wizardPagesList = null!;
    private TextBox _scriptPreviewBox = null!;
    private TextBox _buildLogBox = null!;
    private TextBox _licenseTextBox = null!;

    private System.Windows.Forms.Timer? _scriptDebounce;
    private bool _scriptEditorDirty;
    private bool _updatingScriptEditor;
    private Panel? _welcomePanel;

    public InstallProject Project => _project;

    public PackageBuilderForm(InstallerController controller)
    {
        _controller = controller;
        _project = controller.Project;
        InitializeUi();
        BindController();
        BindProject();
        UpdateTitle();
    }

    private void BindController()
    {
        _controller.PropertyChanged += OnControllerPropertyChanged;
        _controller.ProjectReloaded += (_, _) => OnProjectReloaded();
        _controller.ProjectMutated += (_, _) => RefreshFileTree();
        _controller.BuildProgressChanged += OnBuildProgress;
    }

    private void BindProject() => _project.PropertyChanged += OnProjectPropertyChanged;

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstallerController.IsDirty) ||
            e.PropertyName == nameof(InstallerController.Project))
        {
            _dirtyLabel.Text = _controller.IsDirty ? "● unsaved" : "";
            UpdateTitle();
        }
    }

    private void OnProjectReloaded()
    {
        _project = _controller.Project;
        BindProject();
        if (_componentsBinding != null) _componentsBinding.DataSource = _project.Components;
        if (_prereqsBinding != null) _prereqsBinding.DataSource = _project.Prerequisites;
        if (_shortcutsBinding != null) _shortcutsBinding.DataSource = _project.Shortcuts;
        if (_registryBinding != null) _registryBinding.DataSource = _project.RegistryEntries;
        if (_includeList != null) _includeList.DataSource = _project.SourceIncludes;
        if (_excludeList != null) _excludeList.DataSource = _project.SourceExcludes;
        InvalidateAllContent();
        HideWelcome();
    }

    private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstallProject.SourceDirectory)) RefreshFileTree();
    }

    private void OnBuildProgress(object? sender, BuildPipeline.BuildProgress p)
    {
        if (InvokeRequired) { BeginInvoke((Action)(() => OnBuildProgress(sender, p))); return; }
        _progress.Value = Math.Clamp(p.Percent, 0, 100);
        _activeBuildProgressForm?.UpdateProgress(p.Percent, p.Message);
        _buildLogBox?.AppendText($"[{p.Percent,3}%] {p.Message}{Environment.NewLine}");
    }

    private void InitializeUi()
    {
        Text = "Beep Installer — Package Builder";
        Size = new Size(1180, 760);
        MinimumSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Icon = SystemIcons.Application;
        Font = UiFont;
        BackColor = ShellBackColor;

        _recentsBtn = new ToolStripDropDownButton("Recent") { ToolTipText = "Recently opened installer scripts" };
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
            new ToolStripSeparator(), RecentButton(), new ToolStripSeparator(),
            PreviewButton(), BuildButton(), PublishButton(), ActionsButton(), ConditionsButton(),
            new ToolStripSeparator(), LangButton(), HelpButton(), AboutButton()
        });

        _nav = new LeftNavPanel { Width = 220, Dock = DockStyle.Left, BackColor = Color.FromArgb(241, 244, 248) };
        PopulateLeftNav();
        _nav.SectionSelected += OnSectionSelected;

        _contentHost = new Panel { Dock = DockStyle.Fill, BackColor = ShellBackColor, Padding = new Padding(14) };
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, IsSplitterFixed = true, SplitterWidth = 1 };
        split.Panel1.Controls.Add(_nav);
        split.Panel2.Controls.Add(_contentHost);
        split.SplitterDistance = _nav.Width;

        _status = new StatusStrip { BackColor = PanelBackColor, SizingGrip = false };
        _statusLabel = new ToolStripStatusLabel("Ready.") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _dirtyLabel = new ToolStripStatusLabel { Text = "" };
        _buildResultLabel = new ToolStripStatusLabel { Text = "", IsLink = true, ForeColor = AccentColor };
        _buildResultLabel.Click += (_, _) => OpenLastBuild();
        _progress = new ToolStripProgressBar { Size = new Size(180, 16), Visible = false };
        _status.Items.AddRange(new ToolStripItem[] { _statusLabel, _dirtyLabel, _buildResultLabel, _progress });

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

    private void PopulateLeftNav()
    {
        var g = _nav.AddSection("project", "Project");
        _nav.AddItem(g, "identity", "Identity");
        _nav.AddItem(g, "layout", "Layout");
        _nav.AddItem(g, "eula", "EULA");

        g = _nav.AddSection("source", "Source");
        _nav.AddItem(g, "source", "Files");
        _nav.AddItem(g, "includes", "Includes");

        g = _nav.AddSection("features", "Features");
        _nav.AddItem(g, "components", "Components");
        _nav.AddItem(g, "prerequisites", "Prerequisites");
        _nav.AddItem(g, "shortcuts", "Shortcuts");
        _nav.AddItem(g, "registry", "Registry");

        g = _nav.AddSection("customize", "Customize");
        _nav.AddItem(g, "branding", "Branding");
        _nav.AddItem(g, "wizardpages", "Wizard Pages");

        g = _nav.AddSection("build", "Build");
        _nav.AddItem(g, "script", "Script");
        _nav.AddItem(g, "build", "Build Workflow");
    }

    private void OnSectionSelected(object? sender, string id)
    {
        HideWelcome();
        _contentHost.Controls.Clear();
        _activeContent = id switch
        {
            "identity"     => GetOrCreate(ref _contentIdentity, BuildIdentitySection),
            "layout"       => GetOrCreate(ref _contentLayout, BuildLayoutSection),
            "eula"         => GetOrCreate(ref _contentEula, BuildEulaSection),
            "source"       => GetOrCreate(ref _contentSource, BuildSourceSection),
            "includes"     => GetOrCreate(ref _contentIncludes, BuildIncludesSection),
            "components"   => GetOrCreate(ref _contentComponents, BuildComponentsSection),
            "prerequisites"=> GetOrCreate(ref _contentPrerequisites, BuildPrerequisitesSection),
            "shortcuts"    => GetOrCreate(ref _contentShortcuts, BuildShortcutsSection),
            "registry"     => GetOrCreate(ref _contentRegistry, BuildRegistrySection),
            "branding"     => GetOrCreate(ref _contentBranding, BuildBrandingSection),
            "wizardpages"  => GetOrCreate(ref _contentWizardPages, BuildWizardPagesSection),
            "build"        => GetOrCreate(ref _contentBuild, BuildWorkflowSection),
            "script"       => GetOrCreate(ref _contentScript, BuildScriptSection),
            "output"       => GetOrCreate(ref _contentOutput, BuildOutputSection),
            "payload"      => GetOrCreate(ref _contentPayload, BuildPayloadSection),
            "package"      => GetOrCreate(ref _contentPackage, BuildPackageSection),
            "compression"  => GetOrCreate(ref _contentCompression, BuildCompressionSection),
            "codesign"     => GetOrCreate(ref _contentCodeSign, BuildCodeSignSection),
            "msix"         => GetOrCreate(ref _contentMsix, BuildMsixSection),
            "log"          => GetOrCreate(ref _contentLog, BuildLogSection),
            "result"       => GetOrCreate(ref _contentResult, BuildResultSection),
            _ => null
        };
        if (_activeContent != null)
        {
            _activeContent.Dock = DockStyle.Fill;
            StyleControlTree(_activeContent);
            _contentHost.Controls.Add(_activeContent);
        }
        if (id == "script") RefreshScriptPreview();
        if (id == "build") PopulateWorkflowResult();
    }

    private Panel? GetOrCreate(ref Panel? field, Func<Panel> builder)
    {
        if (field == null || field.IsDisposed) field = builder();
        return field;
    }

    private void InvalidateAllContent()
    {
        _contentIdentity = _contentLayout = _contentEula = _contentSource = _contentIncludes =
        _contentComponents = _contentPrerequisites = _contentShortcuts = _contentRegistry =
        _contentBranding = _contentWizardPages = _contentScript = _contentOutput =
        _contentBuild = _contentPayload = _contentPackage = _contentCompression = _contentCodeSign = _contentMsix = _contentLog = _contentResult = null;
    }

    // ═══════════════════════════════════════════
    //  Section builders — all WinForms DataBinding
    // ═══════════════════════════════════════════

    private Panel BuildIdentitySection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddBoundRow(layout, "Script name:",        proj, nameof(InstallProject.ProjectName));
        AddBoundRow(layout, "Product name:",       proj, nameof(InstallProject.AppName));
        AddBoundRow(layout, "Version:",            proj, nameof(InstallProject.AppVersion));
        AddBoundRow(layout, "Publisher:",          proj, nameof(InstallProject.AppPublisher));
        AddBoundRow(layout, "Publisher URL:",      proj, nameof(InstallProject.AppPublisherURL));
        AddBoundRow(layout, "Support URL:",        proj, nameof(InstallProject.AppSupportURL));
        AddBoundRow(layout, "Support email:",      proj, nameof(InstallProject.AppSupportEmail));
        AddBoundRow(layout, "Update URL:",         proj, nameof(InstallProject.AppUpdatesURL));
        AddEnumRow<UpdateModeEx>(layout, "Update mode:",  proj, nameof(InstallProject.AppUpdateMode));
        AddFileRow(layout, "Setup icon (.ico):",  proj, nameof(InstallProject.SetupIconFile), "*.ico");
        AddFileRow(layout, "Banner image:",       proj, nameof(InstallProject.WizardImageFile), "*.png;*.jpg;*.bmp");
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildLayoutSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddFolderRow(layout, "Default install path:", proj, nameof(InstallProject.DefaultDirName));
        AddBoundRow(layout, "Start menu folder:",    proj, nameof(InstallProject.DefaultGroupName));
        AddEnumRow<InstallationTypeEx>(layout, "Install type:", proj, nameof(InstallProject.DefaultInstallType));
        AddEnumRow<PrivilegeLevel>(layout, "Privileges:", proj, nameof(InstallProject.PrivilegesRequired));
        AddEnumRow<InstallationScope>(layout, "Scope:", proj, nameof(InstallProject.DefaultScope));
        AddCheckRow(layout, "Allow scope selection",  proj, nameof(InstallProject.AllowScopeSelection));
        AddCheckRow(layout, "Prefer 64-bit",          proj, nameof(InstallProject.Prefer64Bit));
        AddCheckRow(layout, "Allow no icons",         proj, nameof(InstallProject.AllowNoIcons));
        AddCheckRow(layout, "Show dir on ready page", proj, nameof(InstallProject.AlwaysShowDirOnReadyPage));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildEulaSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddFileRow(layout, "License file:", proj, nameof(InstallProject.LicenseFile), "*.rtf;*.txt;*.md");
        AddCheckRow(layout, "Show EULA page:", proj, nameof(InstallProject.ShowEula));
        p.Controls.Add(layout);

        _licenseTextBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9) };
        _licenseTextBox.DataBindings.Add("Text", proj, nameof(InstallProject.LicenseText), false, DataSourceUpdateMode.OnPropertyChanged);
        var licPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        licPanel.Controls.Add(_licenseTextBox);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 120, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(layout);
        split.Panel2.Controls.Add(licPanel);
        p.Controls.Add(split);
        return p;
    }

    // ═══════════════════════════════════════════
    //  Direct WinForms DataBindings.Add helpers
    // ═══════════════════════════════════════════

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

    private static void AddSectionRow(TableLayoutPanel layout, string title)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = UiFontBold,
            ForeColor = TextColor,
            Margin = new Padding(0, 10, 0, 6),
        };
        layout.SetColumnSpan(label, 2);
        layout.Controls.Add(label, 0, row);
    }

    private static void AddBoundRow(TableLayoutPanel layout, string label, InstallProject proj, string propertyName)
    {
        var lbl = CreateRowLabel(label);
        var box = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
        box.DataBindings.Add("Text", proj, propertyName, true, DataSourceUpdateMode.OnPropertyChanged);
        int row = AddTableRow(layout);
        layout.Controls.Add(lbl, 0, row);
        layout.Controls.Add(box, 1, row);
    }

    private static void AddCheckRow(TableLayoutPanel layout, string label, InstallProject proj, string propertyName)
    {
        var cb = new CheckBox { Text = label.TrimEnd(':'), Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 6), AutoSize = true };
        cb.DataBindings.Add("Checked", proj, propertyName, true, DataSourceUpdateMode.OnPropertyChanged);
        int row = AddTableRow(layout);
        layout.SetColumnSpan(cb, 2);
        layout.Controls.Add(cb, 0, row);
    }

    private static void AddEnumRow<TEnum>(TableLayoutPanel layout, string label, InstallProject proj, string propertyName)
        where TEnum : struct, Enum
    {
        var lbl = CreateRowLabel(label);
        var combo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 0, 4) };
        foreach (var v in Enum.GetValues<TEnum>()) combo.Items.Add(v);
        combo.DataBindings.Add("SelectedItem", proj, propertyName, true, DataSourceUpdateMode.OnPropertyChanged);
        int row = AddTableRow(layout);
        layout.Controls.Add(lbl, 0, row);
        layout.Controls.Add(combo, 1, row);
    }

    private static void AddFileRow(TableLayoutPanel layout, string label, InstallProject proj, string propertyName, string filter)
    {
        var lbl = CreateRowLabel(label);
        var box = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 8, 4) };
        var btn = new Button { Text = "...", Dock = DockStyle.Right, Width = 36, Margin = new Padding(0, 3, 0, 3) };
        box.DataBindings.Add("Text", proj, propertyName, true, DataSourceUpdateMode.OnPropertyChanged);
        btn.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = filter };
            if (dlg.ShowDialog(btn) == DialogResult.OK) box.Text = dlg.FileName;
        };
        var host = new Panel { Dock = DockStyle.Fill };
        host.Controls.Add(box);
        host.Controls.Add(btn);
        int row = AddTableRow(layout);
        layout.Controls.Add(lbl, 0, row);
        layout.Controls.Add(host, 1, row);
    }

    private static void AddFolderRow(TableLayoutPanel layout, string label, InstallProject proj, string propertyName)
    {
        var lbl = CreateRowLabel(label);
        var box = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 8, 4) };
        var btn = new Button { Text = "...", Dock = DockStyle.Right, Width = 36, Margin = new Padding(0, 3, 0, 3) };
        box.DataBindings.Add("Text", proj, propertyName, true, DataSourceUpdateMode.OnPropertyChanged);
        btn.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog(btn) == DialogResult.OK) box.Text = dlg.SelectedPath;
        };
        var host = new Panel { Dock = DockStyle.Fill };
        host.Controls.Add(box);
        host.Controls.Add(btn);
        int row = AddTableRow(layout);
        layout.Controls.Add(lbl, 0, row);
        layout.Controls.Add(host, 1, row);
    }

    private static int AddTableRow(TableLayoutPanel layout)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        return row;
    }

    private static Label CreateRowLabel(string text)
        => new()
        {
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            ForeColor = MutedTextColor,
            Font = UiFontBold,
            Margin = new Padding(0, 4, 12, 4),
        };

    private Panel BuildIncludesSection()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8) };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        right.Controls.Add(CreatePatternPanel("Include patterns:", out _includeList, out _includeBox, _project.SourceIncludes), 0, 0);
        right.Controls.Add(CreatePatternPanel("Exclude patterns:", out _excludeList, out _excludeBox, _project.SourceExcludes), 1, 0);
        p.Controls.Add(right);
        return p;
    }

    private Panel CreatePatternPanel(string label, out ListBox list, out TextBox box,
        System.Collections.ObjectModel.ObservableCollection<string> collection)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 0, 8, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill }, 0, 0);

        list = new ListBox { Dock = DockStyle.Fill };
        list.DataSource = collection;
        panel.Controls.Add(list, 0, 1);

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        box = new TextBox { Width = 180 };
        var listRef = list;
        var boxRef = box;
        var addBtn = new Button { Text = "Add", Width = 60 };
        var removeBtn = new Button { Text = "Remove", Width = 70 };
        addBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(boxRef.Text))
            {
                collection.Add(boxRef.Text.Trim());
                boxRef.Clear();
                _project.MarkDirty();
            }
        };
        removeBtn.Click += (_, _) =>
        {
            if (listRef.SelectedItem is string s)
            {
                collection.Remove(s);
                _project.MarkDirty();
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
        topBar.Controls.Add(new Label { Text = "Source directory:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 4, 4, 0) });
        _sourceDirBox = new TextBox { Width = 400 };
        _sourceDirBox.DataBindings.Add("Text", _project, nameof(InstallProject.SourceDirectory), false, DataSourceUpdateMode.OnPropertyChanged);
        var browseBtn = new Button { Text = "…", Width = 30 };
        browseBtn.Click += (_, _) => { using var dlg = new FolderBrowserDialog(); if (dlg.ShowDialog(this) == DialogResult.OK) _sourceDirBox.Text = dlg.SelectedPath; };
        var rescanBtn = new Button { Text = "Rescan", Width = 70 };
        rescanBtn.Click += (_, _) => RescanSource();
        topBar.Controls.Add(_sourceDirBox);
        topBar.Controls.Add(browseBtn);
        topBar.Controls.Add(rescanBtn);
        _fileStatsLabel = new Label { Text = "No scan yet.", AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(4, 6, 0, 0) };
        topBar.Controls.Add(_fileStatsLabel);

        _fileTree = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true };
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
        _componentsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            DataSource = _componentsBinding,
            AutoGenerateColumns = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        // After auto-generation, hide the raw Files collection column (it shows
        // "ObservableCollection`1" text and can't be edited usefully in a grid cell).
        // Also rename the count column to be clear.
        _componentsBinding.ListChanged += (_, _) =>
        {
            HideComponentColumn("Files");
            HideComponentColumn("Conditions");
            HideComponentColumn("Includes");
            HideComponentColumn("ConflictsWith");
            HideComponentColumn("DependsOn");
            HideComponentColumn("Registry");
            HideComponentColumn("Shortcuts");
            HideComponentColumn("ComRegistrations");
            HideComponentColumn("GacAssemblies");
        };

        _componentProps = new PropertyGrid { Dock = DockStyle.Fill, HelpVisible = false };
        _componentProps.DataBindings.Add("SelectedObject", _componentsBinding, "", true, DataSourceUpdateMode.OnPropertyChanged);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, FlowDirection = FlowDirection.LeftToRight };
        var addBtn = new Button { Text = "Add", Width = 50 };
        var editBtn = new Button { Text = "Edit Files", Width = 70 };
        var removeBtn = new Button { Text = "Remove", Width = 65 };
        var scanBtn = new Button { Text = "Scan Source", Width = 90 };
        var upBtn = new Button { Text = "\u25B2", Width = 30 };
        var downBtn = new Button { Text = "\u25BC", Width = 30 };
        addBtn.Click += (_, _) => AddComponent();
        editBtn.Click += (_, _) => EditComponentFiles();
        removeBtn.Click += (_, _) => RemoveComponent();
        scanBtn.Click += (_, _) => ScanAndPopulate(_project.SourceDirectory);
        upBtn.Click += (_, _) => MoveComponent(-1);
        downBtn.Click += (_, _) => MoveComponent(+1);
        btnPanel.Controls.AddRange(new Control[] { addBtn, editBtn, removeBtn, scanBtn, upBtn, downBtn });

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_componentsGrid);
        left.Controls.Add(btnPanel);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_componentProps);
        p.Controls.Add(split);
        return p;
    }

    private void HideComponentColumn(string name)
    {
        var column = _componentsGrid.Columns[name];
        if (column != null)
            column.Visible = false;
    }

    private Panel BuildPrerequisitesSection()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 480 };

        _prereqsBinding = new BindingSource { DataSource = _project.Prerequisites };
        _prereqGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            DataSource = _prereqsBinding,
            AutoGenerateColumns = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        _prereqProps = new PropertyGrid { Dock = DockStyle.Fill, HelpVisible = false };
        _prereqProps.DataBindings.Add("SelectedObject", _prereqsBinding, "", true, DataSourceUpdateMode.OnPropertyChanged);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32 };
        var addBtn = new Button { Text = "Add" }; var remBtn = new Button { Text = "Remove" };
        addBtn.Click += (_, _) => _project.Prerequisites.Add(new Prerequisite { Id = $"prereq{_project.Prerequisites.Count + 1}", Name = "New prerequisite" });
        remBtn.Click += (_, _) => { if (_prereqGrid.SelectedRows.Count > 0 && _prereqGrid.SelectedRows[0].DataBoundItem is Prerequisite pr) _project.Prerequisites.Remove(pr); };
        btnPanel.Controls.AddRange(new Control[] { addBtn, remBtn });

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_prereqGrid);
        left.Controls.Add(btnPanel);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_prereqProps);
        p.Controls.Add(split);
        return p;
    }

    private Panel BuildShortcutsSection()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 470 };

        _shortcutsBinding = new BindingSource { DataSource = _project.Shortcuts };
        _shortcutsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            DataSource = _shortcutsBinding,
            AutoGenerateColumns = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        _shortcutProps = new PropertyGrid { Dock = DockStyle.Fill, HelpVisible = false };
        _shortcutProps.DataBindings.Add("SelectedObject", _shortcutsBinding, "", true, DataSourceUpdateMode.OnPropertyChanged);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32 };
        var addBtn = new Button { Text = "Add" }; var remBtn = new Button { Text = "Remove" };
        addBtn.Click += (_, _) => _project.Shortcuts.Add(new ShortcutDefinition { Name = _project.AppName });
        remBtn.Click += (_, _) => { if (_shortcutsGrid.SelectedRows.Count > 0 && _shortcutsGrid.SelectedRows[0].DataBoundItem is ShortcutDefinition s) _project.Shortcuts.Remove(s); };
        btnPanel.Controls.AddRange(new Control[] { addBtn, remBtn });

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_shortcutsGrid);
        left.Controls.Add(btnPanel);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_shortcutProps);
        p.Controls.Add(split);
        return p;
    }

    private Panel BuildRegistrySection()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 570 };

        _registryBinding = new BindingSource { DataSource = _project.RegistryEntries };
        _registryGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            DataSource = _registryBinding,
            AutoGenerateColumns = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        _registryProps = new PropertyGrid { Dock = DockStyle.Fill, HelpVisible = false };
        _registryProps.DataBindings.Add("SelectedObject", _registryBinding, "", true, DataSourceUpdateMode.OnPropertyChanged);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32 };
        var addBtn = new Button { Text = "Add" }; var remBtn = new Button { Text = "Remove" };
        addBtn.Click += (_, _) => _project.RegistryEntries.Add(new RegistryOperation { KeyPath = $@"SOFTWARE\{_project.AppPublisher}\{_project.AppName}", ValueName = "Version", Value = _project.AppVersion });
        remBtn.Click += (_, _) => { if (_registryGrid.SelectedRows.Count > 0 && _registryGrid.SelectedRows[0].DataBoundItem is RegistryOperation r) _project.RegistryEntries.Remove(r); };
        btnPanel.Controls.AddRange(new Control[] { addBtn, remBtn });

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_registryGrid);
        left.Controls.Add(btnPanel);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_registryProps);
        p.Controls.Add(split);
        return p;
    }

    private Panel BuildBrandingSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddBoundRow(layout, "Window title:",        proj, nameof(InstallProject.WindowTitle));
        AddBoundRow(layout, "Welcome title:",       proj, nameof(InstallProject.WelcomeTitle));
        AddEnumRow<WizardTheme>(layout, "Theme:", proj, nameof(InstallProject.DefaultTheme));
        AddBoundRow(layout, "Sidebar background:",  proj, nameof(InstallProject.SidebarBackgroundColor));
        AddBoundRow(layout, "Sidebar text:",        proj, nameof(InstallProject.SidebarTextColor));
        AddBoundRow(layout, "Accent color:",        proj, nameof(InstallProject.AccentColor));
        AddCheckRow(layout, "Allow component selection:", proj, nameof(InstallProject.AllowComponentSelection));
        AddCheckRow(layout, "Allow path change:",        proj, nameof(InstallProject.AllowPathChange));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildWizardPagesSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        _wizardPagesList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        _wizardPagesList.Items.AddRange(new object[] { "Welcome", "License (EULA)", "Component Selection", "Destination Folder", "Start Menu Folder", "Additional Tasks", "Prerequisites", "Ready (review)", "Progress (installing)", "Complete (launch / log)" });
        for (int i = 0; i < _wizardPagesList.Items.Count; i++) _wizardPagesList.SetItemChecked(i, true);
        _wizardPagesList.ItemCheck += (_, _) => BeginInvoke((Action)(() => _project.MarkDirty()));
        p.Controls.Add(_wizardPagesList);
        return p;
    }

    private Panel BuildScriptSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = PanelBackColor };
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = PanelBackColor,
            Padding = new Padding(0, 0, 0, 8),
        };
        var applyBtn = new Button { Text = "Apply Script", Width = 110, Height = 30 };
        var reloadBtn = new Button { Text = "Reload From Fields", Width = 140, Height = 30 };
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
            if (FindNamed<Label>(p, "ScriptEditorStatus") is { } label)
                label.Text = "Modified";
        };
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
            Text = "Build Workflow",
            AutoSize = true,
            Font = SectionTitleFont,
            ForeColor = TextColor,
            Margin = new Padding(0, 0, 0, 4),
        }, 0, 0);
        var buildNow = new Button { Text = "Build Setup.exe", AutoSize = true, Height = 34, Padding = new Padding(14, 0, 14, 0), Margin = new Padding(12, 0, 0, 0) };
        StylePrimaryButton(buildNow);
        buildNow.Click += (_, _) => BuildInstaller();
        header.Controls.Add(buildNow, 1, 0);
        layout.Controls.Add(header, 0, layout.RowCount++);

        layout.Controls.Add(BuildWorkflowGroup("Output", workflow =>
        {
            AddFolderRow(workflow, "Output directory:", _project, nameof(InstallProject.OutputDir));
        }), 0, layout.RowCount++);

        var logGroup = BuildWorkflowTextGroup("Build Log", 180, out _buildLogBox);
        _buildLogBox.ReadOnly = true;
        layout.Controls.Add(logGroup, 0, layout.RowCount++);

        layout.Controls.Add(BuildWorkflowResultGroup(), 0, layout.RowCount++);

        p.Controls.Add(layout);
        return p;
    }

    private static GroupBox BuildWorkflowGroup(string title, Action<TableLayoutPanel> build)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Font = UiFontBold,
            ForeColor = TextColor,
            Padding = new Padding(10, 8, 10, 10),
            Margin = new Padding(0, 0, 0, 12),
        };
        var table = NewTwoColTable();
        table.Padding = new Padding(8, 10, 8, 8);
        build(table);
        group.Controls.Add(table);
        return group;
    }

    private static GroupBox BuildWorkflowTextGroup(string title, int height, out TextBox textBox)
    {
        var group = new GroupBox
        {
            Text = title,
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
        return group;
    }

    private GroupBox BuildWorkflowResultGroup()
    {
        var group = new GroupBox
        {
            Text = "Result",
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

        var status = new Label { Name = "WorkflowResultStatus", Dock = DockStyle.Fill, Text = "No build has been run yet.", Font = UiFontBold, ForeColor = MutedTextColor, TextAlign = ContentAlignment.MiddleLeft };
        var path = new TextBox { Name = "WorkflowResultPath", Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(248, 250, 252), Font = new Font("Consolas", 9) };
        var copy = new Button { Name = "WorkflowCopyPathBtn", Text = "Copy", Width = 70, Enabled = false };
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
        AddFolderRow(layout, "Output directory:",      proj, nameof(InstallProject.OutputDir));
        AddBoundRow(layout, "Setup file name:",       proj, nameof(InstallProject.OutputBaseFilename));
        AddBoundRow(layout, "Main executable:",       proj, nameof(InstallProject.MainExecutable));
        AddEnumRow<Architecture>(layout, "Target architecture:", proj, nameof(InstallProject.ArchitecturesAllowed));
        AddCheckRow(layout, "Register uninstall entry", proj, nameof(InstallProject.CreateUninstallEntry));
        AddCheckRow(layout, "Create system restore point", proj, nameof(InstallProject.CreateRestorePoint));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildPayloadSection()
    {
        var p = NewSectionPanel();
        var layout = NewTwoColTable();
        var proj = _project;
        AddBoundRow(layout, "Payload folder:",       proj, nameof(InstallProject.PayloadFolderName));
        AddEnumRow<PayloadSourceType>(layout, "Payload source:", proj, nameof(InstallProject.PayloadSource));
        AddBoundRow(layout, "Payload URL:",          proj, nameof(InstallProject.PayloadUrl));
        AddCheckRow(layout, "Compress payload",       proj, nameof(InstallProject.CompressPayload));
        AddEnumRow<CompressionFormat>(layout, "Compression format:", proj, nameof(InstallProject.Compression));
        AddEnumRow<CompressionStrength>(layout, "Compression strength:", proj, nameof(InstallProject.CompressionLevel));
        AddCheckRow(layout, "Solid compression",      proj, nameof(InstallProject.SolidCompression));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildPackageSection()
    {
        var p = NewSectionPanel();
        var layout = NewTwoColTable();
        var proj = _project;
        AddSectionRow(layout, "Code signing");
        AddFileRow(layout, "Certificate (.pfx):", proj, nameof(InstallProject.CodeSignCertificatePath), "*.pfx");
        AddBoundRow(layout, "Certificate password:", proj, nameof(InstallProject.CodeSignCertificatePassword));
        AddBoundRow(layout, "Timestamp URL:", proj, nameof(InstallProject.CodeSignTimestampUrl));
        AddSectionRow(layout, "Optional package metadata");
        AddEnumRow<InstallerOutputFormat>(layout, "Output format:", proj, nameof(InstallProject.OutputFormat));
        AddBoundRow(layout, "MSIX identity:", proj, nameof(InstallProject.MsixIdentity));
        AddBoundRow(layout, "MSIX publisher:", proj, nameof(InstallProject.MsixPublisher));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildCompressionSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddCheckRow(layout, "Compress payload",       proj, nameof(InstallProject.CompressPayload));
        AddEnumRow<CompressionFormat>(layout, "Compression format:", proj, nameof(InstallProject.Compression));
        AddCheckRow(layout, "Solid compression",      proj, nameof(InstallProject.SolidCompression));
        AddEnumRow<CompressionStrength>(layout, "Compression strength:", proj, nameof(InstallProject.CompressionLevel));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildCodeSignSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddFileRow(layout, "Certificate (.pfx):",    proj, nameof(InstallProject.CodeSignCertificatePath), "*.pfx");
        AddBoundRow(layout, "Certificate password:",   proj, nameof(InstallProject.CodeSignCertificatePassword));
        AddBoundRow(layout, "Timestamp URL:",          proj, nameof(InstallProject.CodeSignTimestampUrl));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildMsixSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddBoundRow(layout, "MSIX identity:",    proj, nameof(InstallProject.MsixIdentity));
        AddBoundRow(layout, "MSIX publisher:",   proj, nameof(InstallProject.MsixPublisher));
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
        var clearBtn = new Button { Text = "Clear", Width = 70, Height = 28 };
        clearBtn.Click += (_, _) => _buildLogBox?.Clear();
        var saveBtn = new Button { Text = "Save As…", Width = 90, Height = 28 };
        saveBtn.Click += (_, _) =>
        {
            if (_buildLogBox == null) return;
            using var dlg = new SaveFileDialog { Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*", FileName = $"build-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(dlg.FileName, _buildLogBox.Text); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        var copyBtn = new Button { Text = "Copy All", Width = 90, Height = 28 };
        copyBtn.Click += (_, _) =>
        {
            if (_buildLogBox == null) return;
            try { Clipboard.SetText(_buildLogBox.Text); copyBtn.Text = "Copied!"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Copy failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        var selectAllBtn = new Button { Text = "Select All", Width = 90, Height = 28 };
        selectAllBtn.Click += (_, _) => { _buildLogBox?.Focus(); _buildLogBox?.SelectAll(); };
        topBar.Controls.AddRange(new Control[] { clearBtn, saveBtn, copyBtn, selectAllBtn });

        _buildLogBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.LightGreen,
        };

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
            Text = "Last Build Result",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            Location = new Point(0, 0),
            AutoSize = true,
        };
        var statusBanner = new Label
        {
            Name = "StatusBanner",
            Text = "No build has been run yet. Click Build to produce the Setup.exe.",
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
        var pathLbl = new Label { Text = "EXE path:", Font = new Font("Segoe UI", 9, FontStyle.Bold), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
        var pathBox = new TextBox { Name = "PathBox", Width = 400, ReadOnly = true, BackColor = Color.WhiteSmoke, Font = new Font("Consolas", 9) };
        var copyBtn = new Button { Text = "Copy", Width = 70, Height = 26 };
        copyBtn.Name = "CopyPathBtn";
        copyBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(pathBox.Text))
            {
                try { Clipboard.SetText(pathBox.Text); copyBtn.Text = "Copied!"; } catch { }
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
        var openFolderBtn = new Button { Text = "Open Folder", Width = 130, Height = 36, Name = "OpenFolderBtn" };
        var runBtn = new Button { Text = "Run Installer", Width = 140, Height = 36, Name = "RunInstallerBtn" };
        var buildAnotherBtn = new Button { Text = "Build Again", Width = 130, Height = 36, Name = "BuildAgainBtn" };
        buildAnotherBtn.Click += (_, _) => BuildInstaller();
        actionPanel.Controls.AddRange(new Control[] { openFolderBtn, runBtn, buildAnotherBtn });

        p.Controls.AddRange(new Control[] { titleLabel, statusBanner, pathLabel, sizeLabel, issueLabel, pathRow, actionPanel });
        PopulateResultSection();
        return p;
    }

    private void PopulateResultSection()
    {
        var panel = _contentResult;
        if (panel == null) return;
        var statusBanner = panel.Controls["StatusBanner"] as Label;
        var pathLabel = panel.Controls["PathLabel"] as Label;
        var sizeLabel = panel.Controls["SizeLabel"] as Label;
        var issueLabel = panel.Controls["IssueLabel"] as Label;
        var pathBox = FindNamed<TextBox>(panel, "PathBox");
        var copyBtn = FindNamed<Button>(panel, "CopyPathBtn");
        var openBtn = FindNamed<Button>(panel, "OpenFolderBtn");
        var runBtn = FindNamed<Button>(panel, "RunInstallerBtn");
        if (statusBanner == null || pathBox == null) return;

        if (_lastBuildResult == null)
        {
            statusBanner.Text = "No build has been run yet. Click Build to produce the Setup.exe.";
            statusBanner.ForeColor = Color.FromArgb(60, 60, 60);
            pathBox.Text = ""; pathLabel!.Text = ""; sizeLabel!.Text = ""; issueLabel!.Text = "";
            copyBtn!.Enabled = openBtn!.Enabled = runBtn!.Enabled = false;
            return;
        }

        var r = _lastBuildResult;
        statusBanner.Text = r.Success ? "✓ Build succeeded" : "✕ Build failed";
        statusBanner.ForeColor = r.Success ? Color.FromArgb(0, 130, 0) : Color.FromArgb(200, 50, 50);
        pathBox.Text = r.OutputFile;
        pathLabel!.Text = $"Setup EXE: {r.OutputFile}";
        if (!string.IsNullOrWhiteSpace(r.SetupScriptPath))
            pathLabel.Text += $"{Environment.NewLine}Setup script: {r.SetupScriptPath}";
        sizeLabel!.Text = r.OutputSizeBytes > 0
            ? $"Size: {r.OutputSizeBytes / 1024.0 / 1024.0:F1} MB  •  Time: {r.Elapsed.TotalSeconds:F1}s"
            : $"Time: {r.Elapsed.TotalSeconds:F1}s";
        issueLabel!.Text = $"Errors: {r.Errors?.Count ?? 0}    Warnings: {r.Warnings?.Count ?? 0}";
        bool hasExe = !string.IsNullOrEmpty(r.OutputFile) && File.Exists(r.OutputFile);
        copyBtn!.Enabled = hasExe;
        openBtn!.Enabled = hasExe;
        runBtn!.Enabled = hasExe;
        openBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{r.OutputFile}\"") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        runBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(r.OutputFile) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
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
            status.Text = "No build has been run yet.";
            status.ForeColor = MutedTextColor;
            path.Text = "";
            details.Text = "";
            copy.Enabled = false;
            return;
        }

        var result = _lastBuildResult;
        status.Text = result.Success ? "Build succeeded" : "Build failed";
        status.ForeColor = result.Success ? Color.FromArgb(0, 130, 0) : Color.FromArgb(200, 50, 50);
        path.Text = result.OutputFile;
        details.Text = result.Success
            ? $"Setup script: {result.SetupScriptPath}{Environment.NewLine}Size: {result.OutputSizeBytes / 1024.0 / 1024.0:F1} MB    Time: {result.Elapsed.TotalSeconds:F1}s"
            : string.Join(Environment.NewLine, result.Errors.Take(3));
        copy.Enabled = !string.IsNullOrWhiteSpace(result.OutputFile);
    }

    private void ShowBuildResultInForm(BuildPipeline.BuildResult result)
    {
        PopulateResultSection();
        PopulateWorkflowResult();
        _nav.SelectSection("build");
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
        root.Font = UiFont;
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
                propertyGrid.HelpVisible = false;
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

    private ToolStripButton NewButton() => MakeButton("New", "New installer script", (_, _) => NewProject());
    private ToolStripButton OpenButton() => MakeButton("Open", "Open installer script", (_, _) => OpenProject());
    private ToolStripButton SaveButton() => MakeButton("Save", "Save installer script", (_, _) => SaveProject());
    private ToolStripButton SaveAsButton() => MakeButton("Save As", "Save installer script as", (_, _) => SaveProjectAs());
    private ToolStripDropDownButton RecentButton() { RefreshRecents(); return _recentsBtn; }
    private ToolStripButton PreviewButton() => MakeButton("Preview", "Preview the install wizard", (_, _) => PreviewWizard());
    private ToolStripButton BuildButton() => MakeButton("Build", "Build the Setup.exe", (_, _) => BuildInstaller());
    private ToolStripButton PublishButton() => MakeButton("Publish", "Publish as ClickOnce", (_, _) => PublishProject());
    private ToolStripButton ActionsButton() => MakeButton("Actions", "Edit custom actions", (_, _) => EditCustomActions());
    private ToolStripButton ConditionsButton() => MakeButton("Conditions", "Edit component conditions", (_, _) => EditComponentConditions());
    private ToolStripButton LangButton() => MakeButton("Languages", "Open Language Manager", (_, _) => { using var f = new LanguageManagerForm(); f.ShowDialog(this); });
    private ToolStripButton AboutButton() => MakeButton("About", "About Beep Installer", (_, _) => ShowAbout());
    private new ToolStripButton HelpButton() => MakeButton("Help", "Show help", (_, _) => ShowHelp());
    private static ToolStripButton MakeButton(string text, string tip, EventHandler handler)
    {
        var b = new ToolStripButton(text)
        {
            ToolTipText = tip,
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Font = UiFont,
            Padding = new Padding(6, 2, 6, 2),
            Margin = new Padding(2, 0, 2, 0),
        };
        b.Click += handler;
        return b;
    }

    // ═══════════════════════════════════════════
    //  Commands
    // ═══════════════════════════════════════════

    private void NewProject()
    {
        if (!_controller.ConfirmDiscardChanges(this)) return;
        using var dlg = new ProjectNewDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _controller.New(dlg.ProductName, dlg.Version, dlg.Publisher, dlg.SourceDirectory);
    }

    private void OpenProject()
    {
        if (!_controller.ConfirmDiscardChanges(this)) return;
        using var dlg = new OpenFileDialog { Filter = InstallerScriptSerializer.FileFilter, Title = "Open Beep Installer Script" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        LoadProject(dlg.FileName);
    }

    private void SaveProject()
    {
        if (!ApplyScriptEditor()) return;
        if (!_controller.HasFilePath) { SaveProjectAs(); return; }
        var (ok, err) = _controller.Save();
        if (!ok) { MessageBox.Show(this, err, "Save", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        SetStatus($"Saved {Path.GetFileName(_controller.FilePath!)}");
    }

    private void SaveProjectAs()
    {
        if (!ApplyScriptEditor()) return;
        using var dlg = new SaveFileDialog
        {
            Filter = InstallerScriptSerializer.FileFilter,
            FileName = _controller.HasFilePath
                ? Path.GetFileName(_controller.FilePath)
                : _project.OutputBaseFilename + ".bsetup"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var savePath = dlg.FileName;
        if (string.IsNullOrWhiteSpace(savePath)) return;
        var (ok, err) = _controller.SaveAs(savePath);
        if (!ok) { MessageBox.Show(this, err, "Save As", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        RefreshRecents();
        SetStatus($"Saved {Path.GetFileName(savePath)}");
    }

    private void LoadProject(string path)
    {
        var (ok, err) = _controller.Open(path);
        if (!ok) { MessageBox.Show(this, err, "Open Installer Script", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        RefreshRecents();
    }

    // ═══════════════════════════════════════════
    //  Build / publish / validate
    // ═══════════════════════════════════════════

    private void ValidateProject()
    {
        if (!ApplyScriptEditor()) return;
        var result = _controller.Validate();
        _nav.SelectSection("build");
        if (_buildLogBox != null)
        {
            _buildLogBox.Clear();
            _buildLogBox.AppendText($"Validation: {_project.ProjectName}\r\nErrors: {result.Errors.Count}, Warnings: {result.Warnings.Count}\r\n");
            foreach (var e in result.Errors) _buildLogBox.AppendText($"ERR: {e}\r\n");
            foreach (var w in result.Warnings) _buildLogBox.AppendText($"WARN: {w}\r\n");
        }
        SetStatus(result.Errors.Count == 0 ? "Validation passed." : $"{result.Errors.Count} error(s).");
    }

    private void PreviewWizard() { if (!ApplyScriptEditor()) return; using var f = new WizardPreviewForm(_project); f.ShowDialog(this); }
    private void ShowAbout() { MessageBox.Show(this, "Beep Installer — Package Builder\r\nVersion 1.0.0\r\n\r\nBuild self-contained Setup.exe installers for Windows.", "About Beep Installer", MessageBoxButtons.OK, MessageBoxIcon.Information); }

    private void RecordLastBuild(BuildPipeline.BuildResult result)
    {
        _lastBuildResult = result;
        _buildResultLabel.Text = result.Success
            ? $"▶ Last build: {Path.GetFileName(result.OutputFile)}  (click to view)"
            : $"✕ Last build failed  (click to view)";
        _buildResultLabel.ForeColor = result.Success ? Color.FromArgb(0, 130, 0) : Color.FromArgb(200, 50, 50);
        _buildResultLabel.IsLink = true;
        _buildResultLabel.Font = new Font(_buildResultLabel.Font, FontStyle.Bold);
    }

    private void OpenLastBuild()
    {
        if (_lastBuildResult == null)
        {
            MessageBox.Show(this, "No build has been run yet.", "Beep Installer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var f = new BuildResultForm(_lastBuildResult, _project.AppName);
        f.ShowDialog(this);
    }

    private void BuildInstaller()
    {
        if (!ApplyScriptEditor()) return;
        if (string.IsNullOrEmpty(_project.AppName)) { MessageBox.Show(this, "Product name is required.", "Build", MessageBoxButtons.OK, MessageBoxIcon.Warning); _nav.SelectSection("identity"); return; }
        if (!_controller.HasFilePath) { var ans = MessageBox.Show(this, "Installer script has not been saved. Save before building?", "Build", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question); if (ans == DialogResult.Cancel) return; if (ans == DialogResult.Yes) SaveProjectAs(); if (!_controller.HasFilePath) return; }
        if (_controller.IsDirty) SaveProject();
        var outputDir = _project.OutputDir;
        var clean = false;
        if (!string.IsNullOrEmpty(outputDir) && Directory.Exists(outputDir) && Directory.EnumerateFileSystemEntries(outputDir).Any())
        {
            var ans = MessageBox.Show(this, $"Output directory already contains files:\r\n{outputDir}\r\n\r\nClean before building?", "Build", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (ans == DialogResult.Cancel) return;
            clean = ans == DialogResult.Yes;
        }
        _nav.SelectSection("build");
        _buildLogBox?.Clear();
        _progress.Visible = true;
        var projectName = _project.AppName;
        var progressForm = new BuildProgressForm($"Building {projectName}", "Initializing build…") { Owner = this };
        _activeBuildProgressForm = progressForm;
        progressForm.Show(this);
        Task.Run(() =>
        {
            var result = _controller.Build(clean);
            BeginInvoke((Action)(() =>
            {
                _progress.Visible = false;
                _activeBuildProgressForm = null;
                progressForm.Close();
                SetStatus(result.Success ? $"Built {Path.GetFileName(result.OutputFile)} in {result.Elapsed.TotalSeconds:F1}s" : "Build failed.");
                RecordLastBuild(result);
                ShowBuildResultInForm(result);
                if (result.Success)
                {
                    using var resultForm = new BuildResultForm(result, projectName);
                    resultForm.ShowDialog(this);
                }
                else
                {
                    var summary = result.Errors.Count > 0
                        ? result.Errors[0]
                        : "Build failed. See log for details.";
                    using var errorForm = new BuildErrorForm(
                        "Build failed",
                        summary,
                        result.Errors.Count > 0 ? string.Join("\n", result.Errors) : "",
                        result.Steps);
                    errorForm.ShowDialog(this);
                }
            }));
        });
    }

    private void PublishProject()
    {
        if (!ApplyScriptEditor()) return;
        if (string.IsNullOrEmpty(_project.AppName)) { MessageBox.Show(this, "Product name is required.", "Publish", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        using var dlg = new FolderBrowserDialog { Description = "ClickOnce publish folder" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (_controller.IsDirty) SaveProject();
        var projectName = _project.AppName;
        _nav.SelectSection("build");
        _buildLogBox?.Clear();
        _progress.Visible = true;
        Task.Run(() =>
        {
            var result = _controller.Publish(dlg.SelectedPath);
            BeginInvoke((Action)(() =>
            {
                _progress.Visible = false;
                SetStatus(result.Success ? "Publish complete." : "Publish failed.");
                var buildResult = new BuildPipeline.BuildResult
                {
                    Success = result.Success,
                    OutputFile = result.PublishDir,
                    MsixPackagePath = result.PublishDir,
                    Elapsed = result.Elapsed,
                };
                RecordLastBuild(buildResult);
                using var resultForm = new BuildResultForm(buildResult, projectName);
                resultForm.Text = result.Success ? "Publish Complete" : "Publish Failed";
                resultForm.ShowDialog(this);
            }));
        });
    }

    private void EditCustomActions() { using var dlg = new CustomActionsDialog(_project.CustomActions); if (dlg.ShowDialog(this) != DialogResult.OK) return; _project.MarkDirty(); }
    private void EditComponentConditions() { using var dlg = new ComponentConditionsDialog(_project.Components); if (dlg.ShowDialog(this) != DialogResult.OK) return; _project.MarkDirty(); }

    private void ShowHelp() { MessageBox.Show(this, "1. Set source directory (build output of your app).\r\n2. Configure components, prerequisites, shortcuts, registry.\r\n3. Customize branding and wizard pages.\r\n4. Save the .bsetup script.\r\n5. Build → produces a self-contained Setup.exe.", "Beep Installer", MessageBoxButtons.OK, MessageBoxIcon.Information); }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_controller.ConfirmDiscardChanges(this)) { e.Cancel = true; return; }
        _controller.StopAutoSave();
        _scriptDebounce?.Stop();
        base.OnFormClosing(e);
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.Shift && e.KeyCode == Keys.S) { SaveProjectAs(); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.N) { NewProject(); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.O) { OpenProject(); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.S) { SaveProject(); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.P) { PreviewWizard(); e.Handled = true; return; }
        if (e.KeyCode == Keys.F5) { BuildInstaller(); e.Handled = true; return; }
        if (e.KeyCode == Keys.F7) { ValidateProject(); e.Handled = true; return; }
    }

    // ═══════════════════════════════════════════
    //  File tree / scanning
    // ═══════════════════════════════════════════

    private void RefreshFileTree()
    {
        if (_fileTree == null) return;
        _fileTree.Nodes.Clear();
        var sourceDir = _project.SourceDirectory;
        if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir)) return;
        var rootNode = new TreeNode(sourceDir) { Tag = sourceDir };
        _fileTree.Nodes.Add(rootNode);
        foreach (var comp in _project.Components)
            foreach (var f in comp.Files ?? new List<FileCopyOperation>())
                AddFileToTree(rootNode, sourceDir, File.Exists(f.SourcePath) ? f.SourcePath : Path.Combine(sourceDir, f.DestinationPath));
        rootNode.Expand();
    }

    private static void AddFileToTree(TreeNode root, string baseDir, string filePath)
    {
        if (!File.Exists(filePath)) return;
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

    private void ScanAndPopulate(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        _project.SourceDirectory = dir;
        SetStatus($"Scanning {dir}…");
        var result = _controller.Scan(dir);
        SetStatus($"Scan complete: {result.FileCount} files ({result.TotalSizeBytes / 1024.0 / 1024.0:F1} MB), {result.ManagedCount} managed.");
    }

    private void AddComponent()
    {
        _project.Components.Add(new InstallComponent { Id = $"comp{_project.Components.Count + 1}", Name = "New Component", Selected = true, IncludedIn = InstallationType.Typical });
        _project.MarkDirty();
    }

    private void RemoveComponent()
    {
        if (_componentsGrid?.SelectedRows.Count > 0 && _componentsGrid.SelectedRows[0].DataBoundItem is InstallComponent c && !c.Required)
        {
            _project.Components.Remove(c);
            _project.MarkDirty();
        }
    }

    private void EditComponentFiles()
    {
        if (_componentsGrid?.SelectedRows.Count > 0 && _componentsGrid.SelectedRows[0].DataBoundItem is InstallComponent c)
        {
            using var dlg = new ComponentFilesDialog(c, _project.SourceDirectory);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                c.Files.Clear();
                foreach (var f in dlg.ResultFiles) c.Files.Add(f);
                c.SizeBytes = dlg.ResultFiles.Sum(f => File.Exists(f.SourcePath) ? new FileInfo(f.SourcePath).Length : 0);
                _project.MarkDirty();
            }
        }
    }

    private void MoveComponent(int delta)
    {
        if (_componentsGrid?.SelectedRows.Count > 0)
        {
            var idx = _componentsGrid.SelectedRows[0].Index;
            var newIdx = idx + delta;
            if (newIdx >= 0 && newIdx < _project.Components.Count)
            {
                var item = _project.Components[idx];
                _project.Components.RemoveAt(idx);
                _project.Components.Insert(newIdx, item);
                _project.MarkDirty();
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
        center.Controls.Add(new Label { Text = "Beep Installer Package Builder", Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = TextColor, AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 0, 0, 6) }, 0, 0);
        center.Controls.Add(new Label { Text = "Author setup scripts and build a self-contained Windows Setup.exe.", Font = new Font("Segoe UI", 10), ForeColor = MutedTextColor, AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 0, 0, 0) }, 0, 1);
        var newBtn = new Button { Text = "New Installer Script", Size = new Size(220, 36), Margin = new Padding(0, 8, 0, 6) };
        StylePrimaryButton(newBtn);
        newBtn.Click += (_, _) => NewProject();
        var openBtn = new Button { Text = "Open Existing Script", Size = new Size(220, 36), Margin = new Padding(0, 0, 0, 10) };
        StyleButton(openBtn);
        openBtn.Click += (_, _) => OpenProject();
        center.Controls.Add(new Panel { Height = 12 }, 0, 2);
        center.Controls.Add(newBtn, 0, 3);
        center.Controls.Add(openBtn, 0, 4);
        var sub = new Label { Text = "Drag a .bsetup file onto this window to open it.", Font = UiFont, ForeColor = MutedTextColor, AutoSize = true, TextAlign = ContentAlignment.MiddleCenter };
        center.Controls.Add(sub, 0, 5);
        _welcomePanel.Controls.Add(center);
        center.Location = new Point((_welcomePanel.Width - center.Width) / 2, (_welcomePanel.Height - center.Height) / 2);
        _welcomePanel.Resize += (_, _) => { if (center != null) center.Location = new Point((_welcomePanel.Width - center.Width) / 2, (_welcomePanel.Height - center.Height) / 2); };
    }

    private void ShowWelcome()
    {
        if (_welcomePanel == null) return;
        _welcomePanel.Visible = true;
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
    }

    // ═══════════════════════════════════════════
    //  Status / title / recents / script
    // ═══════════════════════════════════════════

    private void UpdateTitle()
    {
        var name = string.IsNullOrEmpty(_controller.FilePath)
            ? "Unsaved script"
            : Path.GetFileName(_controller.FilePath);
        Text = $"Beep Installer — Package Builder — {name}{(_controller.IsDirty ? "*" : "")}";
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private void RefreshRecents()
    {
        _recentsBtn.DropDownItems.Clear();
        var entries = RecentProjects.Load();
        if (entries.Count == 0) { _recentsBtn.DropDownItems.Add("(no recent scripts)").Enabled = false; return; }
        foreach (var e in entries)
        {
            var item = new ToolStripMenuItem { Text = Path.GetFileName(e.Path), ToolTipText = e.Path };
            item.Click += (_, _) => OpenRecent(e.Path);
            _recentsBtn.DropDownItems.Add(item);
        }
        _recentsBtn.DropDownItems.Add(new ToolStripSeparator());
        var clearItem = new ToolStripMenuItem("Clear Recent");
        clearItem.Click += (_, _) => { RecentProjects.Clear(); RefreshRecents(); };
        _recentsBtn.DropDownItems.Add(clearItem);
    }

    private void OpenRecent(string path)
    {
        if (!_controller.ConfirmDiscardChanges(this)) return;
        if (!File.Exists(path)) { MessageBox.Show(this, $"File not found: {path}", "Recent Script", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        LoadProject(path);
    }

    private bool ApplyScriptEditor(bool showSuccess = false)
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
                MessageBox.Show(this, error ?? "Script could not be parsed.", "Script", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            project.MarkDirty();
            _controller.ReplaceProject(project);
            _project = _controller.Project;
            OnProjectReloaded();
            _nav.SelectSection("script");
            _scriptEditorDirty = false;
            RefreshScriptPreview(force: true);
            SetStatus("Script applied.");
            if (showSuccess)
                MessageBox.Show(this, "Script applied.", "Script", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
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
        }
    }
}
