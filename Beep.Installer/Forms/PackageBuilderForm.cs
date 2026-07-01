using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Forms;

/// <summary>
/// Package Builder — the main generator UI.
/// Authors an <see cref="InstallProject"/> (.bpkg) and produces a self-contained
/// Setup.exe via <see cref="InstallerBuilder"/>.
/// </summary>
public class PackageBuilderForm : Form
{
    private TabControl _tabs = null!;
    private ToolStrip _toolbar = null!;
    private ToolStripDropDownButton _recentsBtn = null!;
    private StatusStrip _status = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripStatusLabel _dirtyLabel = null!;
    private ToolStripProgressBar _progress = null!;

    // ── Tab pages ──
    private TabPage _tabProject = null!;
    private TabPage _tabFiles = null!;
    private TabPage _tabComponents = null!;
    private TabPage _tabPrerequisites = null!;
    private TabPage _tabShortcuts = null!;
    private TabPage _tabRegistry = null!;
    private TabPage _tabBranding = null!;
    private TabPage _tabWizardPages = null!;
    private TabPage _tabBuild = null!;
    private TabPage _tabLog = null!;

    // Project tab
    private TextBox _projectNameBox = null!;
    private TextBox _productNameBox = null!;
    private TextBox _versionBox = null!;
    private TextBox _publisherBox = null!;
    private TextBox _supportUrlBox = null!;
    private TextBox _defaultPathBox = null!;
    private TextBox _startMenuBox = null!;
    private CheckBox _requireAdminCheck = null!;
    private ComboBox _installTypeCombo = null!;
    private TextBox _licenseTextBox = null!;

    // Files tab
    private TextBox _sourceDirBox = null!;
    private Button _browseSourceBtn = null!;
    private Button _rescanBtn = null!;
    private CheckBox _recursiveCheck = null!;
    private TreeView _fileTree = null!;
    private Label _fileStatsLabel = null!;
    private ListBox _excludeList = null!;
    private TextBox _excludeBox = null!;
    private Button _addExcludeBtn = null!;
    private Button _removeExcludeBtn = null!;

    // Components tab
    private ListView _componentsList = null!;
    private Button _addComponentBtn = null!;
    private Button _editComponentBtn = null!;
    private Button _removeComponentBtn = null!;
    private Button _moveUpBtn = null!;
    private Button _moveDownBtn = null!;
    private Button _autoPopulateBtn = null!;
    private PropertyGrid _componentProps = null!;

    // Prerequisites tab
    private ListView _prereqList = null!;
    private Button _addPrereqBtn = null!;
    private Button _removePrereqBtn = null!;

    // Shortcuts tab
    private ListView _shortcutsList = null!;
    private Button _addShortcutBtn = null!;
    private Button _removeShortcutBtn = null!;

    // Registry tab
    private ListView _registryList = null!;
    private Button _addRegistryBtn = null!;
    private Button _removeRegistryBtn = null!;

    // Branding tab
    private TextBox _brandingWindowTitleBox = null!;
    private TextBox _brandingWelcomeBox = null!;
    private TextBox _brandingPubNameBox = null!;
    private TextBox _brandingPubUrlBox = null!;
    private TextBox _brandingSupportEmailBox = null!;
    private TextBox _brandingIconPathBox = null!;
    private TextBox _brandingBannerPathBox = null!;
    private Button _brandingIconBrowseBtn = null!;
    private Button _brandingBannerBrowseBtn = null!;
    private CheckBox _brandingShowEulaCheck = null!;
    private CheckBox _brandingAllowComponentsCheck = null!;
    private CheckBox _brandingAllowPathCheck = null!;
    private ComboBox _brandingThemeCombo = null!;

    // Wizard pages tab
    private CheckedListBox _wizardPagesList = null!;

    // Build tab
    private TextBox _buildOutputDirBox = null!;
    private Button _buildOutputDirBrowseBtn = null!;
    private TextBox _buildFileNameBox = null!;
    private TextBox _buildIconPathBox = null!;
    private Button _buildIconBrowseBtn = null!;
    private CheckBox _buildCompressCheck = null!;
    private TrackBar _buildCompressionTrack = null!;
    private Label _buildCompressionLabel = null!;
    private CheckBox _buildUninstallEntryCheck = null!;
    private CheckBox _buildRestorePointCheck = null!;
    private CheckBox _buildSelfContainedCheck = null!;
    private CheckBox _buildAllowScopeCheck = null!;
    private ComboBox _buildScopeCombo = null!;
    private ComboBox _buildArchCombo = null!;
    private TextBox _buildCertPathBox = null!;
    private TextBox _buildCertPasswordBox = null!;
    private Button _buildCertBrowseBtn = null!;
    private Button _buildNowBtn = null!;
    private Button _previewBtn = null!;

    // Log tab
    private TextBox _buildLogBox = null!;
    private Button _clearLogBtn = null!;

    private InstallProject _project = null!;
    private string? _currentFile;
    private bool _dirty;
    private bool _suppressDirty;

    /// <summary>Welcome overlay shown when no project is loaded.</summary>
    private Panel? _welcomePanel;
    private Button? _welcomeNewBtn;
    private Button? _welcomeOpenBtn;

    public InstallProject Project => _project;
    public string? CurrentFile => _currentFile;

    public PackageBuilderForm()
    {
        _project = ProjectSerializer.CreateNew("MyApplication", "1.0.0", "Publisher", "");
        InitializeUi();
        BindFromProject();
        SetDirty(false);
        UpdateTitle();
    }

    // ════════════════════════════════════════════════════════════════════
    //  UI construction
    // ════════════════════════════════════════════════════════════════════

    private void InitializeUi()
    {
        Text = "Beep Installer — Package Builder";
        Size = new Size(1100, 720);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Icon = SystemIcons.Application;

        _recentsBtn = new ToolStripDropDownButton("Recent") { ToolTipText = "Recently opened projects" };

        _toolbar = new ToolStrip { ImageScalingSize = new Size(16, 16) };
        _toolbar.Items.AddRange(new ToolStripItem[]
        {
            NewButton(),
            OpenButton(),
            SaveButton(),
            SaveAsButton(),
            new ToolStripSeparator(),
            RecentButton(),
            new ToolStripSeparator(),
            PreviewButton(),
            BuildButton(),
            new ToolStripSeparator(),
            LangButton(),
            HelpButton(),
            AboutButton()
        });

        _tabs = new TabControl { Dock = DockStyle.Fill };

        _tabProject       = new TabPage("Project");
        _tabFiles         = new TabPage("Files & Payload");
        _tabComponents    = new TabPage("Components");
        _tabPrerequisites = new TabPage("Prerequisites");
        _tabShortcuts     = new TabPage("Shortcuts");
        _tabRegistry      = new TabPage("Registry");
        _tabBranding      = new TabPage("Branding");
        _tabWizardPages   = new TabPage("Wizard Pages");
        _tabBuild         = new TabPage("Build");
        _tabLog           = new TabPage("Build Log");

        BuildProjectTab();
        BuildFilesTab();
        BuildComponentsTab();
        BuildPrereqTab();
        BuildShortcutsTab();
        BuildRegistryTab();
        BuildBrandingTab();
        BuildWizardPagesTab();
        BuildBuildTab();
        BuildLogTab();

        _tabs.TabPages.AddRange(new[]
        {
            _tabProject, _tabFiles, _tabComponents, _tabPrerequisites,
            _tabShortcuts, _tabRegistry, _tabBranding, _tabWizardPages,
            _tabBuild, _tabLog
        });

        _status = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("Ready.") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _dirtyLabel = new ToolStripStatusLabel { Text = "" };
        _progress = new ToolStripProgressBar { Size = new Size(180, 16), Visible = false };
        _status.Items.AddRange(new ToolStripItem[] { _statusLabel, _dirtyLabel, _progress });

        Controls.Add(_tabs);
        Controls.Add(_toolbar);
        Controls.Add(_status);

        BuildWelcomePanel();
        ShowWelcome();

        // Keyboard shortcuts
        KeyPreview = true;
        KeyDown += OnKeyDown;

        AllowDrop = true;
        DragEnter += (_, e) => { e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None; };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            {
                var file = paths[0];
                if (file.EndsWith(".bpkg", StringComparison.OrdinalIgnoreCase))
                    LoadProject(file);
            }
        };
    }

    private ToolStripButton NewButton() => MakeButton("New", "New project", (_, _) => NewProject());
    private ToolStripButton OpenButton() => MakeButton("Open", "Open .bpkg", (_, _) => OpenProject());
    private ToolStripButton SaveButton() => MakeButton("Save", "Save project", (_, _) => SaveProject());
    private ToolStripButton SaveAsButton() => MakeButton("Save As", "Save project as", (_, _) => SaveProjectAs());
    private ToolStripDropDownButton RecentButton() { RefreshRecents(); return _recentsBtn; }
    private ToolStripButton PreviewButton() => MakeButton("Preview", "Preview the install wizard", (_, _) => PreviewWizard());
    private ToolStripButton BuildButton() => MakeButton("Build", "Build the Setup.exe", (_, _) => BuildInstaller());
    private ToolStripButton LangButton() => MakeButton("Languages", "Open Language Manager", (_, _) =>
    {
        using var f = new LanguageManagerForm();
        f.ShowDialog(this);
    });
    private ToolStripButton AboutButton() => MakeButton("About", "About Beep Installer", (_, _) => ShowAbout());
    private new ToolStripButton HelpButton() => MakeButton("Help", "Show help", (_, _) => ShowHelp());

    private static ToolStripButton MakeButton(string text, string tip, EventHandler handler)
    {
        var b = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
        b.Click += handler;
        return b;
    }

    // ── Project tab ──
    private void BuildProjectTab()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        AddField(layout, "Project name:", out _projectNameBox, ref row);
        AddField(layout, "Product name:", out _productNameBox, ref row);
        AddField(layout, "Version:", out _versionBox, ref row);
        AddField(layout, "Publisher:", out _publisherBox, ref row);
        AddField(layout, "Support URL:", out _supportUrlBox, ref row);
        AddField(layout, "Default install path:", out _defaultPathBox, ref row,
            "%ProgramFiles%\\MyApp — variables are expanded at install time.");
        AddField(layout, "Start Menu folder:", out _startMenuBox, ref row);

        layout.Controls.Add(new Label { Text = "Install type:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _installTypeCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _installTypeCombo.Items.AddRange(new object[] { "Typical", "Custom", "Complete" });
        layout.Controls.Add(_installTypeCombo, 1, row);
        _installTypeCombo.SelectedIndexChanged += (_, _) => MarkDirty();
        row++;

        layout.Controls.Add(new Label { Text = "Admin required:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        _requireAdminCheck = new CheckBox { Text = "Require administrator privileges", Dock = DockStyle.Fill };
        _requireAdminCheck.CheckedChanged += (_, _) => MarkDirty();
        layout.Controls.Add(_requireAdminCheck, 1, row);
        row++;

        layout.Controls.Add(new Label { Text = "License text:", TextAlign = ContentAlignment.TopLeft, Dock = DockStyle.Fill }, 0, row);
        _licenseTextBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 200 };
        _licenseTextBox.TextChanged += (_, _) => MarkDirty();
        layout.SetRowSpan(_licenseTextBox, 1);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        layout.Controls.Add(_licenseTextBox, 1, row);
        row++;

        _tabProject.Controls.Add(layout);

        HookDirty(_projectNameBox, _productNameBox, _versionBox, _publisherBox, _supportUrlBox,
                  _defaultPathBox, _startMenuBox, _licenseTextBox);
    }

    private static void AddField(TableLayoutPanel layout, string label, out TextBox box, ref int row, string? tooltip = null)
    {
        var lbl = new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
        layout.Controls.Add(lbl, 0, row);
        box = new TextBox { Dock = DockStyle.Fill };
        layout.Controls.Add(box, 1, row);
        if (tooltip != null) box.Tag = tooltip;
        row++;
    }

    // ── Files tab ──
    private void BuildFilesTab()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 380 };

        // Left: source dir + filters
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(8) };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

        int r = 0;
        left.Controls.Add(new Label { Text = "Source directory:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, r);
        _sourceDirBox = new TextBox { Dock = DockStyle.Fill };
        _browseSourceBtn = new Button { Text = "…", Dock = DockStyle.Fill };
        _browseSourceBtn.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog();
            if (d.ShowDialog(this) == DialogResult.OK) _sourceDirBox.Text = d.SelectedPath;
        };
        left.Controls.Add(_sourceDirBox, 1, r);
        left.Controls.Add(_browseSourceBtn, 2, r);
        r++;

        _recursiveCheck = new CheckBox { Text = "Include subdirectories", Checked = true, Dock = DockStyle.Fill };
        left.Controls.Add(_recursiveCheck, 1, r);
        r++;

        _rescanBtn = new Button { Text = "Rescan", Dock = DockStyle.Fill, Height = 28 };
        _rescanBtn.Click += (_, _) => RescanSource();
        left.SetColumnSpan(_rescanBtn, 3);
        left.Controls.Add(_rescanBtn, 0, r); r++;

        _fileStatsLabel = new Label { Text = "No scan yet.", Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText };
        left.SetColumnSpan(_fileStatsLabel, 3);
        left.Controls.Add(_fileStatsLabel, 0, r); r++;

        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _fileTree = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true };
        left.SetColumnSpan(_fileTree, 3);
        left.Controls.Add(_fileTree, 0, r);

        // Right: exclude patterns
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8) };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        right.Controls.Add(new Label { Text = "Exclude patterns (one per line):", Dock = DockStyle.Fill }, 0, 0);
        right.SetColumnSpan(right.GetControlFromPosition(0, 0)!, 2);

        _excludeList = new ListBox { Dock = DockStyle.Fill };
        right.SetColumnSpan(_excludeList, 2);
        right.Controls.Add(_excludeList, 0, 1);

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        _excludeBox = new TextBox { Width = 180 };
        _addExcludeBtn = new Button { Text = "Add", Width = 60 };
        _addExcludeBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_excludeBox.Text))
            {
                _project.ExcludePatterns.Add(_excludeBox.Text.Trim());
                _excludeBox.Clear();
                RefreshExcludeList();
                MarkDirty();
            }
        };
        _removeExcludeBtn = new Button { Text = "Remove", Width = 70 };
        _removeExcludeBtn.Click += (_, _) =>
        {
            if (_excludeList.SelectedItem is string s)
            {
                _project.ExcludePatterns.Remove(s);
                RefreshExcludeList();
                MarkDirty();
            }
        };
        btnRow.Controls.AddRange(new Control[] { _excludeBox, _addExcludeBtn, _removeExcludeBtn });
        right.SetColumnSpan(btnRow, 2);
        right.Controls.Add(btnRow, 0, 2);

        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        _tabFiles.Controls.Add(split);

        _sourceDirBox.TextChanged += (_, _) => MarkDirty();
        _recursiveCheck.CheckedChanged += (_, _) => MarkDirty();
    }

    private void RefreshExcludeList()
    {
        _excludeList.Items.Clear();
        foreach (var p in _project.ExcludePatterns) _excludeList.Items.Add(p);
    }

    private void RescanSource()
    {
        _fileTree.Nodes.Clear();
        var dir = _sourceDirBox.Text.Trim();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            _fileStatsLabel.Text = "Source directory not found.";
            _fileTree.Nodes.Clear();
            return;
        }

        long totalSize = 0;
        int fileCount = 0;
        var rootNode = new TreeNode(Path.GetFileName(dir.Length == 0 ? dir : dir.TrimEnd(Path.DirectorySeparatorChar))) { Tag = dir };
        _fileTree.Nodes.Add(rootNode);

        var opt = _recursiveCheck.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var file in Directory.EnumerateFiles(dir, "*.*", opt))
        {
            var rel = Path.GetRelativePath(dir, file);
            if (IsExcluded(rel, _project.ExcludePatterns)) continue;
            var size = new FileInfo(file).Length;
            totalSize += size;
            fileCount++;
            AddFileToTree(rootNode, dir, file);
        }

        rootNode.Expand();
        _fileStatsLabel.Text = $"{fileCount:N0} files | {totalSize / 1024.0 / 1024.0:F1} MB | root: {dir}";

        // Auto-populate the Core component with discovered files
        AutoPopulateCoreComponent(dir);
        RefreshComponentsList();
    }

    private static void AddFileToTree(TreeNode root, string baseDir, string filePath)
    {
        var rel = Path.GetRelativePath(baseDir, filePath);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var found = current.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Text == parts[i]);
            if (found == null) { found = new TreeNode(parts[i]) { Tag = Path.Combine(baseDir, string.Join(Path.DirectorySeparatorChar.ToString(), parts.Take(i + 1))) }; current.Nodes.Add(found); }
            current = found;
        }
        var leaf = parts.Last();
        current.Nodes.Add(new TreeNode(leaf) { Tag = filePath, Checked = true });
    }

    private static bool IsExcluded(string relativePath, List<string> excludePatterns)
    {
        var normalized = relativePath.Replace('\\', '/');
        foreach (var pattern in excludePatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            var p = pattern.Trim().Replace('\\', '/');
            if (p.StartsWith("**/")) p = p[3..];
            if (p.EndsWith("/**")) p = p[..^3];
            if (normalized.Contains(p.TrimStart('/'), StringComparison.OrdinalIgnoreCase)) return true;
            if (normalized.EndsWith(p.TrimStart('*'), StringComparison.OrdinalIgnoreCase) && p.StartsWith("*.")) return true;
        }
        return false;
    }

    private void AutoPopulateCoreComponent(string sourceDir)
    {
        var dir = sourceDir.Trim();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        var opt = _recursiveCheck.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = new List<TheTechIdea.Beep.Installer.FileCopyOperation>();
        long totalSize = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.*", opt))
        {
            var rel = Path.GetRelativePath(dir, file);
            if (IsExcluded(rel, _project.ExcludePatterns)) continue;
            var sz = new FileInfo(file).Length;
            totalSize += sz;
            files.Add(new TheTechIdea.Beep.Installer.FileCopyOperation
            {
                SourcePath = file,
                DestinationPath = rel,
                Description = Path.GetFileName(file),
                Overwrite = true,
                IsRequired = true
            });
        }

        // Find or create a "core" component
        var core = _project.InstallConfig.Components.FirstOrDefault(c =>
            string.Equals(c.Id, "core", StringComparison.OrdinalIgnoreCase));

        if (core == null)
        {
            core = new TheTechIdea.Beep.Installer.InstallComponent
            {
                Id = "core", Name = "Core Application", Required = true, Selected = true,
                IncludedIn = TheTechIdea.Beep.Installer.InstallationType.Typical,
                Description = "Main application files (auto-populated from source directory)."
            };
            _project.InstallConfig.Components.Insert(0, core);
        }

        core.Files = files;
        core.SizeBytes = totalSize;
        MarkDirty();
    }

    // ── Components tab ──
    private void BuildComponentsTab()
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 320 };

        _componentsList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false
        };
        _componentsList.Columns.Add("Id", 90);
        _componentsList.Columns.Add("Name", 150);
        _componentsList.Columns.Add("Required", 70);
        _componentsList.Columns.Add("Default", 70);
        _componentsList.Columns.Add("Size (MB)", 80);
        _componentsList.SelectedIndexChanged += (_, _) => BindComponentProps();

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, FlowDirection = FlowDirection.LeftToRight };
        _addComponentBtn = new Button { Text = "Add" };
        _editComponentBtn = new Button { Text = "Edit Files…" };
        _removeComponentBtn = new Button { Text = "Remove" };
        _moveUpBtn = new Button { Text = "▲" };
        _moveDownBtn = new Button { Text = "▼" };
        _autoPopulateBtn = new Button { Text = "Scan Source" };
        _addComponentBtn.Click += (_, _) => AddComponent();
        _editComponentBtn.Click += (_, _) => EditComponentFiles();
        _removeComponentBtn.Click += (_, _) => RemoveComponent();
        _moveUpBtn.Click += (_, _) => MoveComponent(-1);
        _moveDownBtn.Click += (_, _) => MoveComponent(+1);
        _autoPopulateBtn.Click += (_, _) => { AutoPopulateCoreComponent(_project.SourceDirectory); RefreshComponentsList(); };
        btnPanel.Controls.AddRange(new Control[] { _addComponentBtn, _editComponentBtn, _removeComponentBtn, _autoPopulateBtn, _moveUpBtn, _moveDownBtn });

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_componentsList);
        left.Controls.Add(btnPanel);

        _componentProps = new PropertyGrid { Dock = DockStyle.Fill, HelpVisible = false };
        _componentProps.PropertyValueChanged += (_, _) => { RefreshComponentsList(); MarkDirty(); };

        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_componentProps);
        _tabComponents.Controls.Add(split);
    }

    private void RefreshComponentsList()
    {
        _componentsList.Items.Clear();
        foreach (var c in _project.InstallConfig.Components)
        {
            var lvi = new ListViewItem(c.Id);
            lvi.SubItems.Add(c.Name);
            lvi.SubItems.Add(c.Required ? "yes" : "no");
            lvi.SubItems.Add(c.Selected ? "yes" : "no");
            lvi.SubItems.Add((c.SizeBytes / 1024.0 / 1024.0).ToString("F1"));
            lvi.Tag = c;
            _componentsList.Items.Add(lvi);
        }
    }

    private void BindComponentProps()
    {
        if (_componentsList.SelectedItems.Count == 0) { _componentProps.SelectedObject = null; return; }
        _componentProps.SelectedObject = _componentsList.SelectedItems[0].Tag;
    }

    private void AddComponent()
    {
        var c = new InstallComponent
        {
            Id = $"comp{_project.InstallConfig.Components.Count + 1}",
            Name = "New Component",
            Required = false,
            Selected = true,
            IncludedIn = InstallationType.Typical,
            Description = ""
        };
        _project.InstallConfig.Components.Add(c);
        RefreshComponentsList();
        MarkDirty();
    }

    private void RemoveComponent()
    {
        if (_componentsList.SelectedItems.Count == 0) return;
        if (_componentsList.SelectedItems[0].Tag is InstallComponent c)
        {
            if (c.Required)
            {
                MessageBox.Show(this, "Cannot remove a required component.", "Beep Installer",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _project.InstallConfig.Components.Remove(c);
            RefreshComponentsList();
            MarkDirty();
        }
    }

    private void EditComponentFiles()
    {
        if (_componentsList.SelectedItems.Count == 0) return;
        if (_componentsList.SelectedItems[0].Tag is not InstallComponent c) return;

        using var dlg = new ComponentFilesDialog(c, _project.SourceDirectory);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            c.Files = dlg.ResultFiles;
            c.SizeBytes = dlg.ResultFiles.Sum(f => File.Exists(f.SourcePath) ? new FileInfo(f.SourcePath).Length : 0);
            RefreshComponentsList();
            MarkDirty();
        }
    }

    private void MoveComponent(int delta)
    {
        if (_componentsList.SelectedItems.Count == 0) return;
        var idx = _componentsList.SelectedIndices[0];
        var newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= _project.InstallConfig.Components.Count) return;
        var list = _project.InstallConfig.Components;
        var item = list[idx];
        list.RemoveAt(idx);
        list.Insert(newIdx, item);
        RefreshComponentsList();
        _componentsList.Items[newIdx].Selected = true;
        MarkDirty();
    }

    // ── Prerequisites tab ──
    private void BuildPrereqTab()
    {
        _prereqList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
        _prereqList.Columns.Add("Id", 90);
        _prereqList.Columns.Add("Name", 150);
        _prereqList.Columns.Add("Required version", 110);
        _prereqList.Columns.Add("Mandatory", 80);
        _prereqList.Columns.Add("Download URL", 250);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32 };
        _addPrereqBtn = new Button { Text = "Add" };
        _removePrereqBtn = new Button { Text = "Remove" };
        _addPrereqBtn.Click += (_, _) =>
        {
            var p = new Prerequisite { Id = $"prereq{_project.InstallConfig.Prerequisites.Count + 1}", Name = "New prerequisite", VersionRequired = "1.0", IsMandatory = true };
            _project.InstallConfig.Prerequisites.Add(p);
            RefreshPrereqList();
            MarkDirty();
        };
        _removePrereqBtn.Click += (_, _) =>
        {
            if (_prereqList.SelectedItems.Count == 0) return;
            if (_prereqList.SelectedItems[0].Tag is Prerequisite p)
            {
                _project.InstallConfig.Prerequisites.Remove(p);
                RefreshPrereqList();
                MarkDirty();
            }
        };
        btnPanel.Controls.AddRange(new Control[] { _addPrereqBtn, _removePrereqBtn });

        var p = new Panel { Dock = DockStyle.Fill };
        p.Controls.Add(_prereqList);
        p.Controls.Add(btnPanel);
        _tabPrerequisites.Controls.Add(p);
    }

    private void RefreshPrereqList()
    {
        _prereqList.Items.Clear();
        foreach (var p in _project.InstallConfig.Prerequisites)
        {
            var lvi = new ListViewItem(p.Id);
            lvi.SubItems.Add(p.Name);
            lvi.SubItems.Add(p.VersionRequired);
            lvi.SubItems.Add(p.IsMandatory ? "yes" : "no");
            lvi.SubItems.Add(p.DownloadUrl);
            lvi.Tag = p;
            _prereqList.Items.Add(lvi);
        }
    }

    // ── Shortcuts tab ──
    private void BuildShortcutsTab()
    {
        _shortcutsList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
        _shortcutsList.Columns.Add("Name", 150);
        _shortcutsList.Columns.Add("Target", 150);
        _shortcutsList.Columns.Add("Location", 100);
        _shortcutsList.Columns.Add("Subfolder", 150);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32 };
        _addShortcutBtn = new Button { Text = "Add" };
        _removeShortcutBtn = new Button { Text = "Remove" };
        _addShortcutBtn.Click += (_, _) =>
        {
            var s = new ShortcutDefinition { Name = _project.InstallConfig.ProductName, TargetPath = "", Location = ShortcutLocation.StartMenu, StartMenuSubfolder = _project.InstallConfig.StartMenuFolder };
            _project.InstallConfig.Shortcuts.Add(s);
            RefreshShortcutsList();
            MarkDirty();
        };
        _removeShortcutBtn.Click += (_, _) =>
        {
            if (_shortcutsList.SelectedItems.Count == 0) return;
            if (_shortcutsList.SelectedItems[0].Tag is ShortcutDefinition s)
            {
                _project.InstallConfig.Shortcuts.Remove(s);
                RefreshShortcutsList();
                MarkDirty();
            }
        };
        btnPanel.Controls.AddRange(new Control[] { _addShortcutBtn, _removeShortcutBtn });

        var p = new Panel { Dock = DockStyle.Fill };
        p.Controls.Add(_shortcutsList);
        p.Controls.Add(btnPanel);
        _tabShortcuts.Controls.Add(p);
    }

    private void RefreshShortcutsList()
    {
        _shortcutsList.Items.Clear();
        foreach (var s in _project.InstallConfig.Shortcuts)
        {
            var lvi = new ListViewItem(s.Name);
            lvi.SubItems.Add(s.TargetPath);
            lvi.SubItems.Add(s.Location.ToString());
            lvi.SubItems.Add(s.StartMenuSubfolder);
            lvi.Tag = s;
            _shortcutsList.Items.Add(lvi);
        }
    }

    // ── Registry tab ──
    private void BuildRegistryTab()
    {
        _registryList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
        _registryList.Columns.Add("Key path", 220);
        _registryList.Columns.Add("Value name", 120);
        _registryList.Columns.Add("Value", 150);
        _registryList.Columns.Add("Kind", 80);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32 };
        _addRegistryBtn = new Button { Text = "Add" };
        _removeRegistryBtn = new Button { Text = "Remove" };
        _addRegistryBtn.Click += (_, _) =>
        {
            var r = new RegistryOperation { KeyPath = $@"SOFTWARE\{_project.InstallConfig.Publisher}\{_project.InstallConfig.ProductName}", ValueName = "Version", Value = _project.InstallConfig.ProductVersion, ValueKind = Microsoft.Win32.RegistryValueKind.String };
            _project.InstallConfig.RegistryEntries.Add(r);
            RefreshRegistryList();
            MarkDirty();
        };
        _removeRegistryBtn.Click += (_, _) =>
        {
            if (_registryList.SelectedItems.Count == 0) return;
            if (_registryList.SelectedItems[0].Tag is RegistryOperation r)
            {
                _project.InstallConfig.RegistryEntries.Remove(r);
                RefreshRegistryList();
                MarkDirty();
            }
        };
        btnPanel.Controls.AddRange(new Control[] { _addRegistryBtn, _removeRegistryBtn });

        var p = new Panel { Dock = DockStyle.Fill };
        p.Controls.Add(_registryList);
        p.Controls.Add(btnPanel);
        _tabRegistry.Controls.Add(p);
    }

    private void RefreshRegistryList()
    {
        _registryList.Items.Clear();
        foreach (var r in _project.InstallConfig.RegistryEntries)
        {
            var lvi = new ListViewItem(r.KeyPath);
            lvi.SubItems.Add(r.ValueName);
            lvi.SubItems.Add(r.Value);
            lvi.SubItems.Add(r.ValueKind.ToString());
            lvi.Tag = r;
            _registryList.Items.Add(lvi);
        }
    }

    // ── Branding tab ──
    private void BuildBrandingTab()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(12), AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

        AddFileField(layout, "Window title:", out _brandingWindowTitleBox, out _, 0, true);
        AddFileField(layout, "Welcome title:", out _brandingWelcomeBox, out _, 1, true);
        AddFileField(layout, "Publisher name:", out _brandingPubNameBox, out _, 2, true);
        AddFileField(layout, "Publisher URL:", out _brandingPubUrlBox, out _, 3, true);
        AddFileField(layout, "Support email:", out _brandingSupportEmailBox, out _, 4, true);
        AddFileField(layout, "Product icon:", out _brandingIconPathBox, out _brandingIconBrowseBtn, 5, true);
        AddFileField(layout, "Banner image:", out _brandingBannerPathBox, out _brandingBannerBrowseBtn, 6, true);

        layout.Controls.Add(new Label { Text = "Theme:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 7);
        _brandingThemeCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _brandingThemeCombo.Items.AddRange(new object[] { "Modern", "Classic", "Compact" });
        layout.Controls.Add(_brandingThemeCombo, 1, 7);
        _brandingThemeCombo.SelectedIndexChanged += (_, _) => MarkDirty();

        _brandingShowEulaCheck = new CheckBox { Text = "Show EULA page", Dock = DockStyle.Fill };
        _brandingAllowComponentsCheck = new CheckBox { Text = "Allow component selection", Dock = DockStyle.Fill };
        _brandingAllowPathCheck = new CheckBox { Text = "Allow install path change", Dock = DockStyle.Fill };
        layout.Controls.Add(_brandingShowEulaCheck, 1, 8);
        layout.Controls.Add(_brandingAllowComponentsCheck, 1, 9);
        layout.Controls.Add(_brandingAllowPathCheck, 1, 10);

        foreach (var c in new[] { _brandingShowEulaCheck, _brandingAllowComponentsCheck, _brandingAllowPathCheck })
            c.CheckedChanged += (_, _) => MarkDirty();

        HookDirty(_brandingWindowTitleBox, _brandingWelcomeBox, _brandingPubNameBox, _brandingPubUrlBox,
                  _brandingSupportEmailBox, _brandingIconPathBox, _brandingBannerPathBox);

        _tabBranding.Controls.Add(layout);
    }

    private void AddFileField(TableLayoutPanel layout, string label, out TextBox box, out Button? browse, int row, bool isFile)
    {
        layout.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        box = new TextBox { Dock = DockStyle.Fill };
        layout.Controls.Add(box, 1, row);
        browse = new Button { Text = "…", Dock = DockStyle.Fill };
        if (isFile)
        {
            var capturedBox = box;
            browse.Click += (_, _) =>
            {
                using var d = new OpenFileDialog { Filter = "Icon files (*.ico)|*.ico|Image files (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp|All files|*.*" };
                if (d.ShowDialog(this) == DialogResult.OK) capturedBox.Text = d.FileName;
            };
        }
        layout.Controls.Add(browse, 2, row);
    }

    // ── Wizard pages tab ──
    private void BuildWizardPagesTab()
    {
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        _wizardPagesList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false
        };
        _wizardPagesList.Items.AddRange(new object[]
        {
            "Welcome",
            "License (EULA)",
            "Install Type (Typical / Custom / Complete)",
            "Component Selection",
            "Destination Folder",
            "Start Menu Folder",
            "Additional Tasks",
            "Per-User vs Per-Machine",
            "Prerequisites",
            "Ready (review)",
            "Progress (installing)",
            "Complete (launch / log)"
        });
        for (int i = 0; i < _wizardPagesList.Items.Count; i++) _wizardPagesList.SetItemChecked(i, true);
        _wizardPagesList.ItemCheck += (_, _) => BeginInvoke((Action)MarkDirty);
        p.Controls.Add(_wizardPagesList);

        var hint = new Label
        {
            Text = "Tick the wizard pages the end user will see. Unticked pages are skipped.",
            Dock = DockStyle.Bottom, Height = 24, ForeColor = SystemColors.GrayText
        };
        p.Controls.Add(hint);
        _tabWizardPages.Controls.Add(p);
    }

    // ── Build tab ──
    private void BuildBuildTab()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(12), AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

        // Output
        int r = 0;
        layout.Controls.Add(new Label { Text = "Output directory:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, r);
        _buildOutputDirBox = new TextBox { Dock = DockStyle.Fill };
        _buildOutputDirBrowseBtn = new Button { Text = "…", Dock = DockStyle.Fill };
        _buildOutputDirBrowseBtn.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog();
            if (d.ShowDialog(this) == DialogResult.OK) _buildOutputDirBox.Text = d.SelectedPath;
        };
        layout.Controls.Add(_buildOutputDirBox, 1, r);
        layout.Controls.Add(_buildOutputDirBrowseBtn, 2, r);
        r++;

        AddBuildField(layout, "Output filename:", out _buildFileNameBox, ref r);
        AddBuildFileField(layout, "Setup icon (.ico):", out _buildIconPathBox, out _buildIconBrowseBtn, ref r);

        layout.Controls.Add(new Label { Text = "Architecture:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, r);
        _buildArchCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _buildArchCombo.Items.AddRange(new object[] { "x64", "x86", "arm64", "AnyCPU" });
        layout.Controls.Add(_buildArchCombo, 1, r);
        _buildArchCombo.SelectedIndexChanged += (_, _) => MarkDirty();
        r++;

        // Compression
        _buildCompressCheck = new CheckBox { Text = "Compress payload into a single zip", Dock = DockStyle.Fill, Checked = true };
        _buildCompressCheck.CheckedChanged += (_, _) => MarkDirty();
        layout.SetColumnSpan(_buildCompressCheck, 2);
        layout.Controls.Add(_buildCompressCheck, 0, r); r++;

        var compressionPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Height = 36 };
        _buildCompressionTrack = new TrackBar { Minimum = 0, Maximum = 9, Value = 6, TickFrequency = 1, Width = 240, Height = 28 };
        _buildCompressionLabel = new Label { Text = "Level 6 (default)", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 8, 0, 0) };
        _buildCompressionTrack.ValueChanged += (_, _) =>
        {
            _buildCompressionLabel.Text = $"Level {_buildCompressionTrack.Value} ({(CompressionLevelName(_buildCompressionTrack.Value))})";
            MarkDirty();
        };
        compressionPanel.Controls.Add(_buildCompressionTrack);
        compressionPanel.Controls.Add(_buildCompressionLabel);
        layout.SetColumnSpan(compressionPanel, 2);
        layout.Controls.Add(compressionPanel, 0, r); r++;

        // Toggles
        _buildUninstallEntryCheck = new CheckBox { Text = "Register uninstall entry (Add/Remove Programs)", Dock = DockStyle.Fill, Checked = true };
        _buildRestorePointCheck = new CheckBox { Text = "Create Windows system restore point before install", Dock = DockStyle.Fill, Checked = true };
        _buildSelfContainedCheck = new CheckBox { Text = "Self-contained (.NET runtime bundled)", Dock = DockStyle.Fill };
        _buildAllowScopeCheck = new CheckBox { Text = "Allow per-user / per-machine scope choice", Dock = DockStyle.Fill, Checked = true };
        foreach (var c in new[] { _buildUninstallEntryCheck, _buildRestorePointCheck, _buildSelfContainedCheck, _buildAllowScopeCheck })
            c.CheckedChanged += (_, _) => MarkDirty();
        layout.SetColumnSpan(_buildUninstallEntryCheck, 2); layout.Controls.Add(_buildUninstallEntryCheck, 0, r); r++;
        layout.SetColumnSpan(_buildRestorePointCheck, 2); layout.Controls.Add(_buildRestorePointCheck, 0, r); r++;
        layout.SetColumnSpan(_buildSelfContainedCheck, 2); layout.Controls.Add(_buildSelfContainedCheck, 0, r); r++;
        layout.SetColumnSpan(_buildAllowScopeCheck, 2); layout.Controls.Add(_buildAllowScopeCheck, 0, r); r++;

        layout.Controls.Add(new Label { Text = "Default scope:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, r);
        _buildScopeCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _buildScopeCombo.Items.AddRange(new object[] { "Machine", "User" });
        _buildScopeCombo.SelectedIndexChanged += (_, _) => MarkDirty();
        layout.Controls.Add(_buildScopeCombo, 1, r);
        r++;

        // Code signing
        AddBuildFileField(layout, "Code-sign cert (.pfx):", out _buildCertPathBox, out _buildCertBrowseBtn, ref r);
        AddBuildField(layout, "Cert password (optional):", out _buildCertPasswordBox, ref r, usePasswordMask: true);
        _buildCertPasswordBox.UseSystemPasswordChar = true;

        // Actions
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 40, FlowDirection = FlowDirection.LeftToRight };
        var validateBtn = new Button { Text = "Validate Project", Width = 120, Height = 32 };
        _previewBtn = new Button { Text = "Preview Wizard", Width = 130, Height = 32 };
        _buildNowBtn = new Button { Text = "Build Setup.exe", Width = 140, Height = 32, BackColor = Color.FromArgb(41, 98, 255), ForeColor = Color.White };
        validateBtn.Click += (_, _) => ValidateProject();
        _previewBtn.Click += (_, _) => PreviewWizard();
        _buildNowBtn.Click += (_, _) => BuildInstaller();
        actions.Controls.AddRange(new Control[] { validateBtn, _previewBtn, _buildNowBtn });
        layout.SetColumnSpan(actions, 3);
        layout.Controls.Add(actions, 0, r);

        HookDirty(_buildOutputDirBox, _buildFileNameBox, _buildIconPathBox, _buildCertPathBox, _buildCertPasswordBox);
        _tabBuild.Controls.Add(layout);
    }

    private void AddBuildField(TableLayoutPanel layout, string label, out TextBox box, ref int row, bool usePasswordMask = false)
    {
        layout.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        box = new TextBox { Dock = DockStyle.Fill };
        if (usePasswordMask) box.UseSystemPasswordChar = true;
        layout.SetColumnSpan(box, 2);
        layout.Controls.Add(box, 1, row);
        row++;
    }

    private void AddBuildFileField(TableLayoutPanel layout, string label, out TextBox box, out Button browse, ref int row)
    {
        layout.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        box = new TextBox { Dock = DockStyle.Fill };
        layout.Controls.Add(box, 1, row);
        browse = new Button { Text = "…", Dock = DockStyle.Fill };
        var capturedBox = box;
        browse.Click += (_, _) =>
        {
            using var d = new OpenFileDialog();
            if (label.Contains(".ico")) d.Filter = "Icon files (*.ico)|*.ico";
            else if (label.Contains(".pfx")) d.Filter = "Personal Information Exchange (*.pfx)|*.pfx|All files|*.*";
            else d.Filter = "All files (*.*)|*.*";
            if (d.ShowDialog(this) == DialogResult.OK) capturedBox.Text = d.FileName;
        };
        layout.Controls.Add(browse, 2, row);
        row++;
    }

    private static string CompressionLevelName(int v) => v switch
    {
        0 => "store",
        <= 3 => "fast",
        <= 6 => "default",
        <= 8 => "high",
        _ => "maximum"
    };

    // ── Log tab ──
    private void BuildLogTab()
    {
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        _buildLogBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.LightGreen
        };
        _clearLogBtn = new Button { Text = "Clear log", Dock = DockStyle.Bottom, Height = 28 };
        _clearLogBtn.Click += (_, _) => _buildLogBox.Clear();
        p.Controls.Add(_buildLogBox);
        p.Controls.Add(_clearLogBtn);
        _tabLog.Controls.Add(p);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Bind / sync helpers
    // ════════════════════════════════════════════════════════════════════

    private void BindFromProject()
    {
        _suppressDirty = true;
        try
        {
            _projectNameBox.Text = _project.ProjectName;
            _productNameBox.Text = _project.InstallConfig.ProductName;
            _versionBox.Text = _project.InstallConfig.ProductVersion;
            _publisherBox.Text = _project.InstallConfig.Publisher;
            _supportUrlBox.Text = _project.InstallConfig.SupportUrl ?? "";
            _defaultPathBox.Text = _project.InstallConfig.DefaultInstallPath;
            _startMenuBox.Text = _project.InstallConfig.StartMenuFolder;
            _requireAdminCheck.Checked = _project.InstallConfig.RequireAdminPrivileges;
            _installTypeCombo.SelectedItem = _project.InstallConfig.DefaultInstallType.ToString();
            _licenseTextBox.Text = _project.InstallConfig.LicenseText ?? "";

            _sourceDirBox.Text = _project.SourceDirectory;
            RefreshExcludeList();

            RefreshComponentsList();
            RefreshPrereqList();
            RefreshShortcutsList();
            RefreshRegistryList();

            _brandingWindowTitleBox.Text = _project.Branding.WindowTitle;
            _brandingWelcomeBox.Text = _project.Branding.WelcomeTitle;
            _brandingPubNameBox.Text = _project.Branding.PublisherName;
            _brandingPubUrlBox.Text = _project.Branding.PublisherUrl;
            _brandingSupportEmailBox.Text = _project.Branding.SupportEmail;
            _brandingIconPathBox.Text = _project.Branding.ProductIconPath;
            _brandingBannerPathBox.Text = _project.Branding.WelcomeBannerPath;
            _brandingShowEulaCheck.Checked = _project.Branding.ShowEula;
            _brandingAllowComponentsCheck.Checked = _project.Branding.AllowComponentSelection;
            _brandingAllowPathCheck.Checked = _project.Branding.AllowPathChange;
            _brandingThemeCombo.SelectedItem = string.IsNullOrEmpty(_project.Branding.DefaultTheme) ? "Modern" : _project.Branding.DefaultTheme;

            _buildOutputDirBox.Text = _project.Build.OutputDirectory;
            _buildFileNameBox.Text = _project.Build.OutputFileName;
            _buildIconPathBox.Text = _project.Build.IconPath;
            _buildCompressCheck.Checked = _project.Build.CompressPayload;
            _buildCompressionTrack.Value = Math.Clamp(_project.Build.CompressionLevel, 0, 9);
            _buildUninstallEntryCheck.Checked = _project.Build.RegisterUninstallEntry;
            _buildRestorePointCheck.Checked = _project.Build.CreateSystemRestorePoint;
            _buildSelfContainedCheck.Checked = _project.Build.SelfContained;
            _buildAllowScopeCheck.Checked = _project.Build.AllowScopeSelection;
            _buildScopeCombo.SelectedItem = _project.Build.DefaultScope;
            _buildArchCombo.SelectedItem = _project.Build.Architecture;
            _buildCertPathBox.Text = _project.Build.CodeSignCertificatePath;
            _buildCertPasswordBox.Text = _project.Build.CodeSignCertificatePassword;
        }
        finally { _suppressDirty = false; }
    }

    private void SyncToProject()
    {
        if (_suppressDirty) return;

        _project.ProjectName = _projectNameBox.Text.Trim();
        _project.InstallConfig.ProductName = _productNameBox.Text.Trim();
        _project.InstallConfig.ProductVersion = _versionBox.Text.Trim();
        _project.InstallConfig.Publisher = _publisherBox.Text.Trim();
        _project.InstallConfig.SupportUrl = _supportUrlBox.Text.Trim();
        _project.InstallConfig.DefaultInstallPath = _defaultPathBox.Text.Trim();
        _project.InstallConfig.StartMenuFolder = _startMenuBox.Text.Trim();
        _project.InstallConfig.RequireAdminPrivileges = _requireAdminCheck.Checked;
        if (Enum.TryParse<InstallationType>(_installTypeCombo.SelectedItem?.ToString(), out var t)) _project.InstallConfig.DefaultInstallType = t;
        _project.InstallConfig.LicenseText = _licenseTextBox.Text;

        _project.SourceDirectory = _sourceDirBox.Text.Trim();

        _project.Branding.WindowTitle = _brandingWindowTitleBox.Text.Trim();
        _project.Branding.WelcomeTitle = _brandingWelcomeBox.Text.Trim();
        _project.Branding.PublisherName = _brandingPubNameBox.Text.Trim();
        _project.Branding.PublisherUrl = _brandingPubUrlBox.Text.Trim();
        _project.Branding.SupportEmail = _brandingSupportEmailBox.Text.Trim();
        _project.Branding.ProductIconPath = _brandingIconPathBox.Text.Trim();
        _project.Branding.WelcomeBannerPath = _brandingBannerPathBox.Text.Trim();
        _project.Branding.ShowEula = _brandingShowEulaCheck.Checked;
        _project.Branding.AllowComponentSelection = _brandingAllowComponentsCheck.Checked;
        _project.Branding.AllowPathChange = _brandingAllowPathCheck.Checked;
        _project.Branding.DefaultTheme = _brandingThemeCombo.SelectedItem?.ToString() ?? "Modern";

        _project.Build.OutputDirectory = _buildOutputDirBox.Text.Trim();
        _project.Build.OutputFileName = _buildFileNameBox.Text.Trim();
        _project.Build.IconPath = _buildIconPathBox.Text.Trim();
        _project.Build.CompressPayload = _buildCompressCheck.Checked;
        _project.Build.CompressionLevel = _buildCompressionTrack.Value;
        _project.Build.RegisterUninstallEntry = _buildUninstallEntryCheck.Checked;
        _project.Build.CreateSystemRestorePoint = _buildRestorePointCheck.Checked;
        _project.Build.SelfContained = _buildSelfContainedCheck.Checked;
        _project.Build.AllowScopeSelection = _buildAllowScopeCheck.Checked;
        _project.Build.DefaultScope = _buildScopeCombo.SelectedItem?.ToString() ?? "Machine";
        _project.Build.Architecture = _buildArchCombo.SelectedItem?.ToString() ?? "x64";
        _project.Build.CodeSignCertificatePath = _buildCertPathBox.Text.Trim();
        _project.Build.CodeSignCertificatePassword = _buildCertPasswordBox.Text;
    }

    private void MarkDirty()
    {
        if (_suppressDirty) return;
        SyncToProject();
        SetDirty(true);
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        _dirtyLabel.Text = dirty ? "● unsaved" : "";
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        var name = string.IsNullOrEmpty(_currentFile) ? "Untitled" : Path.GetFileName(_currentFile);
        Text = $"Beep Installer — Package Builder — {name}{(_dirty ? "*" : "")}";
    }

    private void HookDirty(params Control[] controls)
    {
        foreach (var c in controls)
        {
            switch (c)
            {
                case TextBox tb: tb.TextChanged += (_, _) => MarkDirty(); break;
                case CheckBox cb: cb.CheckedChanged += (_, _) => MarkDirty(); break;
                case ComboBox combo: combo.SelectedIndexChanged += (_, _) => MarkDirty(); break;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Commands
    // ════════════════════════════════════════════════════════════════════

    private void NewProject()
    {
        if (!ConfirmDiscardChanges()) return;
        using var dlg = new ProjectNewDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _project = ProjectSerializer.CreateNew(dlg.ProductName, dlg.Version, dlg.Publisher, dlg.SourceDirectory);
        _currentFile = null;
        BindFromProject();
        SetDirty(false);
        UpdateTitle();
        HideWelcome();
    }

    private void OpenProject()
    {
        if (!ConfirmDiscardChanges()) return;
        using var dlg = new OpenFileDialog { Filter = ProjectSerializer.FileFilter, Title = "Open Beep Installer Project" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        LoadProject(dlg.FileName);
    }

    private void LoadProject(string path)
    {
        var (project, err) = ProjectSerializer.Load(path);
        if (project == null)
        {
            MessageBox.Show(this, err, "Open Project", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _project = project;
        _currentFile = path;
        BindFromProject();
        SetDirty(false);
        UpdateTitle();
        SetStatus($"Opened {Path.GetFileName(path)}");
        Engine.RecentProjects.Record(path);
        RefreshRecents();
        HideWelcome();
    }

    private void SaveProject()
    {
        SyncToProject();
        if (string.IsNullOrEmpty(_currentFile)) { SaveProjectAs(); return; }
        var (ok, err) = ProjectSerializer.Save(_project, _currentFile);
        if (!ok) { MessageBox.Show(this, err, "Save", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        SetDirty(false);
        SetStatus($"Saved {Path.GetFileName(_currentFile)}");
    }

    private void SaveProjectAs()
    {
        SyncToProject();
        using var dlg = new SaveFileDialog
        {
            Filter = ProjectSerializer.FileFilter,
            FileName = string.IsNullOrEmpty(_currentFile)
                ? InstallerBuilder.SafeFileName($"{_project.InstallConfig.ProductName}.bpkg", "project.bpkg")
                : Path.GetFileName(_currentFile)
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _currentFile = dlg.FileName;
        SaveProject();
        Engine.RecentProjects.Record(dlg.FileName);
        RefreshRecents();
    }

    private void ValidateProject()
    {
        SyncToProject();
        var result = new Beep.Installer.Engine.InstallerBuilder().Validate(_project);
        _tabs.SelectedTab = _tabLog;
        _buildLogBox.Clear();

        _buildLogBox.AppendText($"=== Project Validation: {_project.ProjectName} ===\r\n");
        foreach (var s in result.Steps) _buildLogBox.AppendText($"  {s}\r\n");
        _buildLogBox.AppendText($"\r\nErrors: {result.Errors.Count}, Warnings: {result.Warnings.Count}\r\n");

        if (result.Errors.Count > 0)
        {
            _buildLogBox.AppendText("--- ERRORS ---\r\n");
            foreach (var e in result.Errors) _buildLogBox.AppendText($"  🔴 {e}\r\n");
        }
        if (result.Warnings.Count > 0)
        {
            _buildLogBox.AppendText("--- WARNINGS ---\r\n");
            foreach (var w in result.Warnings) _buildLogBox.AppendText($"  🟡 {w}\r\n");
        }
        if (result.Errors.Count == 0 && result.Warnings.Count == 0)
            _buildLogBox.AppendText("  ✅ All checks passed.\r\n");

        SetStatus(result.Errors.Count == 0 ? "Validation passed." : $"{result.Errors.Count} validation error(s).");
    }

    private void PreviewWizard()
    {
        SyncToProject();
        using var f = new WizardPreviewForm(_project);
        f.ShowDialog(this);
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            $"Beep Installer — Package Builder\r\nVersion 1.0.0\r\n\r\n" +
            "Build self-contained Setup.exe installers for Windows.\r\n\r\n" +
            "The Tech Idea\r\nhttps://github.com/The-Tech-Idea",
            "About Beep Installer", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void BuildInstaller()
    {
        SyncToProject();

        if (string.IsNullOrEmpty(_project.InstallConfig.ProductName))
        {
            MessageBox.Show(this, "Product name is required.", "Build", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tabs.SelectedTab = _tabProject;
            return;
        }

        if (string.IsNullOrEmpty(_project.Build.OutputFileName))
        {
            MessageBox.Show(this, "Output filename is required.", "Build", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tabs.SelectedTab = _tabBuild;
            return;
        }

        if (string.IsNullOrEmpty(_project.SourceDirectory) || !Directory.Exists(_project.SourceDirectory))
        {
            var ans = MessageBox.Show(this,
                "Source directory is empty or missing. The payload will be empty. Continue?",
                "Build", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ans != DialogResult.Yes) return;
        }

        if (string.IsNullOrEmpty(_currentFile))
        {
            var ans = MessageBox.Show(this,
                "Project has not been saved. Save before building?",
                "Build", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (ans == DialogResult.Cancel) return;
            if (ans == DialogResult.Yes) SaveProjectAs();
            if (string.IsNullOrEmpty(_currentFile)) return;
        }

        // If still dirty, save before building
        if (_dirty) ProjectSerializer.Save(_project, _currentFile!);

        // If output dir already has a previous build, offer to clean it
        var outputDir = string.IsNullOrEmpty(_project.Build.OutputDirectory) ? "" : _project.Build.OutputDirectory;
        var clean = false;
        if (!string.IsNullOrEmpty(outputDir) && Directory.Exists(outputDir) && Directory.EnumerateFileSystemEntries(outputDir).Any())
        {
            var ans = MessageBox.Show(this,
                $"Output directory already contains files:\r\n{outputDir}\r\n\r\nClean before building?",
                "Build", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (ans == DialogResult.Cancel) return;
            clean = ans == DialogResult.Yes;
        }

        _tabs.SelectedTab = _tabLog;
        _buildLogBox.Clear();
        _progress.Visible = true;
        _buildNowBtn.Enabled = false;
        _previewBtn.Enabled = false;

        var progress = new Progress<BuildProgress>(p =>
        {
            _progress.Value = Math.Clamp(p.Percent, 0, 100);
            _buildLogBox.AppendText($"[{p.Percent,3}%] {p.Message}{Environment.NewLine}");
            _buildLogBox.SelectionStart = _buildLogBox.Text.Length;
            _buildLogBox.ScrollToCaret();
        });

        var builder = new InstallerBuilder { Progress = progress };
        var cleanFlag = clean;

        Task.Run(() =>
        {
            var result = builder.Build(_project, cleanFlag);
            BeginInvoke((Action)(() =>
            {
                _progress.Visible = false;
                _buildNowBtn.Enabled = true;
                _previewBtn.Enabled = true;

                if (result.Success)
                {
                    SetStatus($"Built {Path.GetFileName(result.OutputFile)} in {result.Elapsed.TotalSeconds:F1}s");
                    _buildLogBox.AppendText(Environment.NewLine + result.Summary);
                    if (MessageBox.Show(this,
                        $"Build succeeded.\r\n\r\n{result.Summary}\r\n\r\nOpen the output folder?",
                        "Build complete", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        var dir = Path.GetDirectoryName(result.OutputFile);
                        if (!string.IsNullOrEmpty(dir)) Process.Start("explorer.exe", dir);
                    }
                }
                else
                {
                    SetStatus("Build failed.");
                    var msg = "Build failed:\r\n" + string.Join("\r\n", result.Errors);
                    MessageBox.Show(this, msg, "Build failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }));
        });
    }

    private void ShowHelp()
    {
        MessageBox.Show(this,
            "Beep Installer — Package Builder\r\n\r\n" +
            "1. Set source directory (the build output of your app).\r\n" +
            "2. Configure components, prerequisites, shortcuts, registry.\r\n" +
            "3. Customize branding and wizard pages.\r\n" +
            "4. Save the .bpkg project.\r\n" +
            "5. Build → produces a self-contained Setup.exe to ship.\r\n\r\n" +
            "CLI: Beep.Installer.exe /BUILD=project.bpkg",
            "Beep Installer", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_dirty) return true;
        var ans = MessageBox.Show(this, "You have unsaved changes. Save first?",
            "Beep Installer", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (ans == DialogResult.Cancel) return false;
        if (ans == DialogResult.Yes) SaveProject();
        return true;
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private void RefreshRecents()
    {
        _recentsBtn.DropDownItems.Clear();
        var entries = Engine.RecentProjects.Load();
        if (entries.Count == 0)
        {
            _recentsBtn.DropDownItems.Add("(no recent projects)").Enabled = false;
            return;
        }
        foreach (var e in entries)
        {
            var item = new ToolStripMenuItem
            {
                Text = Path.GetFileName(e.Path),
                ToolTipText = e.Path
            };
            item.Click += (_, _) => OpenRecent(e.Path);
            _recentsBtn.DropDownItems.Add(item);
        }
        _recentsBtn.DropDownItems.Add(new ToolStripSeparator());
        var clearItem = new ToolStripMenuItem("Clear Recent");
        clearItem.Click += (_, _) => { Engine.RecentProjects.Clear(); RefreshRecents(); };
        _recentsBtn.DropDownItems.Add(clearItem);
    }

    private void OpenRecent(string path)
    {
        if (!ConfirmDiscardChanges()) return;
        if (!File.Exists(path))
        {
            MessageBox.Show(this, $"File not found: {path}", "Recent Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        LoadProject(path);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!ConfirmDiscardChanges()) { e.Cancel = true; return; }
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

    private void BuildWelcomePanel()
    {
        _welcomePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Visible = false
        };

        var center = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.None,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var title = new Label
        {
            Text = "Beep Installer — Package Builder",
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 30, 40),
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.None
        };
        var sub = new Label
        {
            Text = "Build self-contained Windows installers for your applications.",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(120, 120, 130),
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.None
        };

        _welcomeNewBtn = new Button
        {
            Text = "New Project",
            Width = 220, Height = 32,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            BackColor = Color.FromArgb(41, 98, 255),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.None
        };
        _welcomeNewBtn.FlatAppearance.BorderSize = 0;
        _welcomeNewBtn.Click += (_, _) => NewProject();

        _welcomeOpenBtn = new Button
        {
            Text = "Open Project",
            Width = 220, Height = 32,
            Font = new Font("Segoe UI", 10),
            Anchor = AnchorStyles.None
        };
        _welcomeOpenBtn.Click += (_, _) => OpenProject();

        var recentItems = RecentProjects.Load();
        center.Controls.Add(title, 0, 0);
        center.Controls.Add(sub, 0, 1);
        center.Controls.Add(_welcomeNewBtn, 0, 2);
        center.Controls.Add(_welcomeOpenBtn, 0, 3);

        if (recentItems.Count > 0)
        {
            center.Controls.Add(new Panel { Height = 20, Width = 220, Anchor = AnchorStyles.None }, 0, 5);
            var recentLabel = new Label
            {
                Text = "Recent projects:",
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 120, 130),
                AutoSize = true,
                Anchor = AnchorStyles.None
            };
            center.Controls.Add(recentLabel, 0, 6);
            int r = 7;
            foreach (var entry in recentItems.Take(5))
            {
                var link = new LinkLabel
                {
                    Text = Path.GetFileName(entry.Path),
                    AutoSize = true,
                    Anchor = AnchorStyles.None,
                    Tag = entry.Path
                };
                link.Click += (s, _) => OpenRecent(((LinkLabel)s!).Tag!.ToString()!);
                center.Controls.Add(link, 0, r);
                r++;
            }
        }

        // Center the panel
        _welcomePanel.Controls.Add(center);
        _welcomePanel.Resize += (_, _) =>
        {
            center.Location = new Point(
                (_welcomePanel.ClientSize.Width - center.Width) / 2,
                (_welcomePanel.ClientSize.Height - center.Height) / 2);
        };

        Controls.Add(_welcomePanel);
    }

    private void ShowWelcome()
    {
        _tabs.Visible = false;
        if (_welcomePanel != null)
        {
            _welcomePanel.Visible = true;
            _welcomePanel.BringToFront();
        }
    }

    private void HideWelcome()
    {
        _tabs.Visible = true;
        if (_welcomePanel != null) _welcomePanel.Visible = false;
    }
}
