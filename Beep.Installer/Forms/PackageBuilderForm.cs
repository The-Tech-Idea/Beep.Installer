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
    private readonly string[] _runtimeArgs;
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
    private TextBox _navSearchBox = null!;
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

    public PackageBuilderForm(InstallerController controller, string[]? runtimeArgs = null)
    {
        _runtimeArgs = runtimeArgs?.ToArray() ?? Array.Empty<string>();
        _controller = controller;
        _project = controller.Project;
        InitializeUi();
        BindController();
        BindProject();
        UpdateTitle();
        Engine.Accessibility.Attach(this);
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

        // Invalidation drops the panel that is currently on screen along with the rest, so rebuild
        // it against the project that was just opened. Without this the user is left looking at a
        // blank pane -- and before invalidation covered these sections at all, at the previous
        // project's data.
        if (_activeSectionId is { } section) OnSectionSelected(this, section);

        HideWelcome();
    }

    private void OnProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstallProject.SourceDirectory)) RefreshFileTree();
        if (_brandingPreview is { IsDisposed: false }) _brandingPreview.Invalidate();
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
            ValidateFieldsInline();
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

    private void OnBuildProgress(object? sender, BuildPipeline.BuildProgress p)
    {
        if (InvokeRequired) { BeginInvoke((Action)(() => OnBuildProgress(sender, p))); return; }
        _progress.Value = Math.Clamp(p.Percent, 0, 100);
        _activeBuildProgressForm?.UpdateProgress(p.Percent, p.Message);
        _buildLogBox?.AppendText($"[{p.Percent,3}%] {p.Message}{Environment.NewLine}");
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
            QuickStartButton(), PreviewButton(), BuildButton(), PublishButton(), UpdatesButton(), TemplateButton(), ActionsButton(), ConditionsButton(),
            new ToolStripSeparator(), LangButton(), HelpButton(), AboutButton()
        });

        _nav = new LeftNavPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(241, 244, 248) };
        PopulateLeftNav();
        _nav.SectionSelected += OnSectionSelected;
        _navSearchBox = new TextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = L("Builder_SearchSections", "Search sections…"),
            Margin = new Padding(8),
            AccessibleName = "Search authoring sections"
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
        navHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        navHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        navHost.Controls.Add(_navSearchBox, 0, 0);
        navHost.Controls.Add(_nav, 0, 1);

        _contentHost = new Panel { Dock = DockStyle.Fill, BackColor = ShellBackColor, Padding = new Padding(14) };
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, IsSplitterFixed = true, SplitterWidth = 1 };
        split.Panel1.Controls.Add(navHost);
        split.Panel2.Controls.Add(_contentHost);
        split.SplitterDistance = 236;

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

    /// <summary>
    /// Shorthand for a localized string with the English text as the fallback. The builder was
    /// entirely English-only; this covers its high-traffic chrome (navigation sections and the
    /// repeated action buttons). Full coverage of every field label remains outstanding.
    /// </summary>
    private static string L(string key, string english)
        => Lang.LanguageManager.GetOrDefault(key, english);

    private void PopulateLeftNav()
    {
        // Section headers go through the resource manager; the English text stays as the
        // fallback so behaviour is unchanged for en.
        var g = _nav.AddSection("project", L("Nav_Project", "Project"));
        _nav.AddItem(g, "identity", "Identity", "Product name, version, publisher, setup titles");
        _nav.AddItem(g, "layout", "Layout", "Install directory, scope, privileges, install type");
        _nav.AddItem(g, "eula", "EULA", "License agreement and EULA display");

        g = _nav.AddSection("source", L("Nav_Source", "Source"));
        _nav.AddItem(g, "source", "Files", "Source directory, payload files, scan");
        _nav.AddItem(g, "includes", "Includes", "Include and exclude patterns");

        g = _nav.AddSection("features", L("Nav_Features", "Features"));
        _nav.AddItem(g, "components", "Components", "Feature tree, selected components, conditions");
        _nav.AddItem(g, "prerequisites", "Prerequisites", "Runtime packages, prerequisite catalogs, dependencies");
        _nav.AddItem(g, "shortcuts", "Shortcuts", "Desktop, Start Menu and Startup shortcuts");
        _nav.AddItem(g, "registry", "Registry", "Registry keys, values and uninstall metadata");

        g = _nav.AddSection("advanced", "Advanced Resources");
        _nav.AddItem(g, "scheduledtasks", "Scheduled Tasks", "Task Scheduler actions, triggers, elevation and credentials");
        _nav.AddItem(g, "firewallrules", "Firewall Rules", "Windows Firewall ports, programs, services and profiles");
        _nav.AddItem(g, "certificates", "Certificates", "Certificate source, store, thumbprint and rollback");
        _nav.AddItem(g, "comregistrations", "COM", "COM CLSID, ProgID, server type and typelib registration");
        _nav.AddItem(g, "driverpackages", "Drivers", "INF/sys packages, service identity, signing and reboot behavior");
        _nav.AddItem(g, "configtransforms", "Config Transforms", "JSON/XML/INI transforms with rollback backups");
        _nav.AddItem(g, "iisapppools", "IIS App Pools", "Runtime, pipeline, identity and ownership settings");
        _nav.AddItem(g, "iissites", "IIS Sites", "Physical path, application pool, bindings and ownership");
        _nav.AddItem(g, "webdeploy", "Web Deploy", "msdeploy packages, IIS site target and parameters");

        g = _nav.AddSection("customize", L("Nav_Customize", "Customize"));
        _nav.AddItem(g, "branding", "Branding", "Theme, banner, logo and installer branding");
        _nav.AddItem(g, "wizardpages", "Wizard Pages", "Custom wizard pages and unattended properties");

        // Output shape and packaging. Every one of these sections was already written and wired
        // into OnSectionSelected -- and none of them had a nav item, so there was no way to reach
        // code signing, MSIX, compression or the output settings from the UI at all. The features
        // were implemented and simply unreachable.
        g = _nav.AddSection("output", L("Nav_Output", "Output"));
        _nav.AddItem(g, "output", "Output", "Output directory, setup file name and install-type defaults");
        _nav.AddItem(g, "payload", "Payload", "Payload folder, single-file, self-contained and download URL");
        _nav.AddItem(g, "compression", "Compression", "Method, level and solid compression");
        _nav.AddItem(g, "package", "Package Format", "EXE, MSI, MSIX and store readiness");
        _nav.AddItem(g, "codesign", "Code Signing", "Certificate, timestamp URL and signing policy");
        _nav.AddItem(g, "msix", "MSIX", "Identity, publisher and optional packages");

        g = _nav.AddSection("build", L("Nav_Build", "Build"));
        _nav.AddItem(g, "script", "Script", "Canonical .bsetup script preview and editor");
        _nav.AddItem(g, "build", "Build Workflow", "Validation, plan hash, package build and diagnostics");
        _nav.AddItem(g, "log", "Build Log", "Full output from the last build");
        _nav.AddItem(g, "result", "Last Result", "Artifacts, sizes and warnings from the last build");
    }

    private void OnSectionSelected(object? sender, string id)
    {
        HideWelcome();
        _activeSectionId = id;
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
            "scheduledtasks" => GetOrCreate(ref _contentScheduledTasks, BuildScheduledTasksSection),
            "firewallrules" => GetOrCreate(ref _contentFirewallRules, BuildFirewallRulesSection),
            "certificates" => GetOrCreate(ref _contentCertificates, BuildCertificatesSection),
            "comregistrations" => GetOrCreate(ref _contentComRegistrations, BuildComRegistrationsSection),
            "driverpackages" => GetOrCreate(ref _contentDriverPackages, BuildDriverPackagesSection),
            "configtransforms" => GetOrCreate(ref _contentConfigTransforms, BuildConfigTransformsSection),
            "iisapppools" => GetOrCreate(ref _contentIisAppPools, BuildIisAppPoolsSection),
            "iissites" => GetOrCreate(ref _contentIisSites, BuildIisSitesSection),
            "webdeploy" => GetOrCreate(ref _contentWebDeployPackages, BuildWebDeployPackagesSection),
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

        // Mark what is already wrong on arrival, rather than only after the next edit.
        ValidateFieldsInline();
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

    private static readonly System.Reflection.FieldInfo[] CachedSectionPanelFields =
        typeof(PackageBuilderForm)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(Panel) && f.Name.StartsWith("_content", StringComparison.Ordinal))
            .ToArray();

    // ═══════════════════════════════════════════
    //  Section builders — all WinForms DataBinding
    // ═══════════════════════════════════════════

    private Panel BuildIdentitySection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddBoundRow(layout, L("Field_ScriptName", "Script name:"),        proj, nameof(InstallProject.ProjectName));
        AddBoundRow(layout, L("Field_ProductName", "Product name:"),       proj, nameof(InstallProject.AppName));
        AddBoundRow(layout, L("Field_ProductIDAppId", "Product ID (AppId):"),  proj, nameof(InstallProject.AppId));
        var identityHelp = new Label
        {
            AutoSize = true,
            Text = L("Builder_KeepThisGUIDUnchangedFor", "Keep this GUID unchanged for updates and renames. A different GUID identifies a different product."),
            AccessibleName = "Product ID guidance",
            Margin = new Padding(3, 0, 3, 8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(identityHelp, 1, layout.RowCount++);
        AddBoundRow(layout, L("Field_Version", "Version:"),            proj, nameof(InstallProject.AppVersion));
        AddBoundRow(layout, L("Field_Publisher", "Publisher:"),          proj, nameof(InstallProject.AppPublisher));
        AddBoundRow(layout, L("Field_PublisherURL", "Publisher URL:"),      proj, nameof(InstallProject.AppPublisherURL));
        AddBoundRow(layout, L("Field_SupportURL", "Support URL:"),        proj, nameof(InstallProject.AppSupportURL));
        AddBoundRow(layout, L("Field_SupportEmail", "Support email:"),      proj, nameof(InstallProject.AppSupportEmail));
        AddBoundRow(layout, L("Field_UpdateURL", "Update URL:"),         proj, nameof(InstallProject.AppUpdatesURL));
        AddEnumRow<UpdateMode>(layout, "Update mode:",  proj, nameof(InstallProject.AppUpdateMode));
        AddFileRow(layout, L("Field_SetupIconIco", "Setup icon (.ico):"),  proj, nameof(InstallProject.SetupIconFile), "*.ico");
        AddFileRow(layout, L("Field_BannerImage", "Banner image:"),       proj, nameof(InstallProject.WizardImageFile), "*.png;*.jpg;*.bmp");
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildLayoutSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddFolderRow(layout, L("Field_DefaultInstallPath", "Default install path:"), proj, nameof(InstallProject.DefaultDirName));
        AddBoundRow(layout, L("Field_StartMenuFolder", "Start menu folder:"),    proj, nameof(InstallProject.DefaultGroupName));
        AddEnumRow<InstallationType>(layout, "Install type:", proj, nameof(InstallProject.DefaultInstallType));
        AddEnumRow<PrivilegeLevel>(layout, "Privileges:", proj, nameof(InstallProject.PrivilegesRequired));
        AddEnumRow<InstallationScope>(layout, "Scope:", proj, nameof(InstallProject.DefaultScope));
        AddCheckRow(layout, L("Field_AllowScopeSelection", "Allow scope selection"),  proj, nameof(InstallProject.AllowScopeSelection));
        AddCheckRow(layout, L("Field_Prefer64Bit", "Prefer 64-bit"),          proj, nameof(InstallProject.Prefer64Bit));
        AddCheckRow(layout, L("Field_AllowNoIcons", "Allow no icons"),         proj, nameof(InstallProject.AllowNoIcons));
        AddCheckRow(layout, L("Field_ShowDirOnReadyPage", "Show dir on ready page"), proj, nameof(InstallProject.AlwaysShowDirOnReadyPage));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildEulaSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddFileRow(layout, L("Field_LicenseFile", "License file:"), proj, nameof(InstallProject.LicenseFile), "*.rtf;*.txt;*.md");
        AddCheckRow(layout, L("Field_ShowEULAPage", "Show EULA page:"), proj, nameof(InstallProject.ShowEula));
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
        var box = new TextBox
        {
            Name = propertyName,
            AccessibleName = label.TrimEnd(':'),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 4)
        };
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
        var addBtn = new Button { Text = L("Btn_Add", "Add"), Width = 60 };
        var removeBtn = new Button { Text = L("Btn_Remove", "Remove"), Width = 70 };
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
        topBar.Controls.Add(new Label { Text = L("Builder_SourceDir", "Source directory:"), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 4, 4, 0) });
        _sourceDirBox = new TextBox { Width = 400 };
        _sourceDirBox.DataBindings.Add("Text", _project, nameof(InstallProject.SourceDirectory), false, DataSourceUpdateMode.OnPropertyChanged);
        var browseBtn = new Button { Text = "…", Width = 30 };
        browseBtn.Click += (_, _) => { using var dlg = new FolderBrowserDialog(); if (dlg.ShowDialog(this) == DialogResult.OK) _sourceDirBox.Text = dlg.SelectedPath; };
        var rescanBtn = new Button { Text = L("Btn_Rescan", "Rescan"), Width = 70 };
        rescanBtn.Click += (_, _) => RescanSource();
        topBar.Controls.Add(_sourceDirBox);
        topBar.Controls.Add(browseBtn);
        topBar.Controls.Add(rescanBtn);
        _fileStatsLabel = new Label { Text = L("Builder_NoScan", "No scan yet."), AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(4, 6, 0, 0) };
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
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        _componentsGrid.Columns.AddRange(
            TextColumn(nameof(InstallComponent.Id), L("Grid_Id", "Id"), fillWeight: 18),
            TextColumn(nameof(InstallComponent.Name), L("Grid_Name", "Name"), fillWeight: 30),
            TextColumn(nameof(InstallComponent.Description), L("Grid_Description", "Description"), fillWeight: 34),
            CheckColumn(nameof(InstallComponent.Required), L("Grid_Required", "Required"), fillWeight: 9),
            CheckColumn(nameof(InstallComponent.Selected), L("Grid_Selected", "Selected"), fillWeight: 9));

        _componentProps = new PropertyGrid { Dock = DockStyle.Fill, HelpVisible = false };
        _componentProps.DataBindings.Add("SelectedObject", _componentsBinding, "", true, DataSourceUpdateMode.OnPropertyChanged);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, FlowDirection = FlowDirection.LeftToRight };
        var addBtn = new Button { Text = L("Btn_Add", "Add"), Width = 50 };
        var editBtn = new Button { Text = L("Builder_EditFiles", "Edit Files"), Width = 70 };
        var removeBtn = new Button { Text = L("Btn_Remove", "Remove"), Width = 65 };
        var scanBtn = new Button { Text = L("Builder_ScanSource", "Scan Source"), Width = 90 };
        // Glyphs, not prose: these stay literal. Routing them through the resource manager wrote
        // the escape sequence itself into the .resx, so the buttons would have rendered the text
        // "\u25B2" rather than an arrow.
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
        var addBtn = new Button { Text = L("Btn_Add", "Add") }; var remBtn = new Button { Text = L("Btn_Remove", "Remove") };
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
        var addBtn = new Button { Text = L("Btn_Add", "Add") }; var remBtn = new Button { Text = L("Btn_Remove", "Remove") };
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
        var addBtn = new Button { Text = L("Btn_Add", "Add") }; var remBtn = new Button { Text = L("Btn_Remove", "Remove") };
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

    private Panel BuildScheduledTasksSection()
        => BuildAdvancedResourceSection(
            "Scheduled Tasks",
            "Create Task Scheduler entries with trigger, elevation, working-directory, credential and uninstall behavior.",
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
            });

    private Panel BuildFirewallRulesSection()
        => BuildAdvancedResourceSection(
            "Firewall Rules",
            "Author Windows Firewall rules for ports, programs, services and profile scope.",
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
            });

    private Panel BuildCertificatesSection()
        => BuildAdvancedResourceSection(
            "Certificates",
            "Install certificate assets into the Windows certificate store with thumbprint and rollback ownership controls.",
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
            "COM Registrations",
            "Register COM CLSID/ProgID metadata against in-process or local-server binaries.",
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
            "Driver Packages",
            "Stage driver INF/sys packages with service identity, signing requirements and reboot behavior.",
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
            "Config Transforms",
            "Apply JSON/XML/INI configuration changes with install backup and rollback restore controls.",
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
            "IIS App Pools",
            "Define IIS application-pool runtime, pipeline mode, identity, autostart and ownership.",
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
            });

    private Panel BuildIisSitesSection()
        => BuildAdvancedResourceSection(
            "IIS Sites",
            "Define IIS site physical path, application pool, binding collection and uninstall ownership.",
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
            });

    private Panel BuildWebDeployPackagesSection()
        => BuildAdvancedResourceSection(
            "Web Deploy Packages",
            "Attach msdeploy packages to an IIS site target with package parameters and uninstall ownership.",
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
        string title,
        string description,
        System.Collections.ObjectModel.ObservableCollection<T> collection,
        Func<T> createDefault)
        where T : class
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = PanelBackColor,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = PanelBackColor };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = SectionTitleFont, ForeColor = TextColor }, 0, 0);
        header.Controls.Add(new Label { Text = description, Dock = DockStyle.Fill, ForeColor = MutedTextColor }, 0, 1);
        layout.Controls.Add(header, 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 560 };
        var binding = new BindingSource { DataSource = collection };
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            DataSource = binding,
            AutoGenerateColumns = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false
        };
        grid.DataError += (_, e) => e.ThrowException = false;
        grid.CellValueChanged += (_, _) => _project.MarkDirty();
        grid.UserDeletedRow += (_, _) => _project.MarkDirty();
        grid.DataBindingComplete += (_, _) =>
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                var propertyType = typeof(T).GetProperty(column.DataPropertyName)?.PropertyType;
                if (propertyType == null)
                    continue;
                if (propertyType != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType))
                    column.Visible = false;
            }
        };

        var props = new PropertyGrid { Dock = DockStyle.Fill, HelpVisible = true };
        props.DataBindings.Add("SelectedObject", binding, "", true, DataSourceUpdateMode.OnPropertyChanged);
        props.PropertyValueChanged += (_, _) => _project.MarkDirty();

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.LeftToRight };
        var addBtn = new Button { Text = L("Btn_Add", "Add"), AutoSize = true };
        var duplicateBtn = new Button { Text = L("Builder_Duplicate", "Duplicate"), AutoSize = true };
        var removeBtn = new Button { Text = L("Btn_Remove", "Remove"), AutoSize = true };
        addBtn.Click += (_, _) =>
        {
            var item = createDefault();
            collection.Add(item);
            binding.Position = collection.Count - 1;
            _project.MarkDirty();
        };
        duplicateBtn.Click += (_, _) =>
        {
            if (binding.Current is not T current)
                return;
            var item = CloneAdvancedResource(current);
            collection.Add(item);
            binding.Position = collection.Count - 1;
            _project.MarkDirty();
        };
        removeBtn.Click += (_, _) =>
        {
            if (binding.Current is not T current)
                return;
            collection.Remove(current);
            _project.MarkDirty();
        };
        btnPanel.Controls.AddRange(new Control[] { addBtn, duplicateBtn, removeBtn });

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(grid);
        left.Controls.Add(btnPanel);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(props);
        layout.Controls.Add(split, 0, 1);
        p.Controls.Add(layout);
        return p;
    }

    private static T CloneAdvancedResource<T>(T source) where T : class
    {
        var clone = Activator.CreateInstance<T>();
        foreach (var property in typeof(T).GetProperties().Where(p => p.CanRead && p.CanWrite))
        {
            var value = property.GetValue(source);
            if (value is System.Collections.IDictionary sourceDictionary)
            {
                var targetDictionary = property.GetValue(clone) as System.Collections.IDictionary;
                if (targetDictionary != null)
                {
                    foreach (System.Collections.DictionaryEntry entry in sourceDictionary)
                        targetDictionary.Add(entry.Key, entry.Value);
                    continue;
                }
            }
            if (value is System.Collections.IList sourceList)
            {
                var targetList = property.GetValue(clone) as System.Collections.IList;
                if (targetList != null)
                {
                    foreach (var item in sourceList)
                        targetList.Add(item);
                    continue;
                }
            }
            property.SetValue(clone, value);
        }
        return clone;
    }

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
        AddBoundRow(layout, L("Field_WindowTitle", "Window title:"),        proj, nameof(InstallProject.WindowTitle));
        AddBoundRow(layout, L("Field_WelcomeTitle", "Welcome title:"),       proj, nameof(InstallProject.WelcomeTitle));
        AddEnumRow<WizardTheme>(layout, "Theme:", proj, nameof(InstallProject.DefaultTheme));
        AddBoundRow(layout, L("Field_SidebarBackground", "Sidebar background:"),  proj, nameof(InstallProject.SidebarBackgroundColor));
        AddBoundRow(layout, L("Field_SidebarText", "Sidebar text:"),        proj, nameof(InstallProject.SidebarTextColor));
        AddBoundRow(layout, L("Field_AccentColor", "Accent color:"),        proj, nameof(InstallProject.AccentColor));
        AddCheckRow(layout, L("Field_AllowComponentSelection", "Allow component selection:"), proj, nameof(InstallProject.AllowComponentSelection));
        AddCheckRow(layout, L("Field_AllowPathChange", "Allow path change:"),        proj, nameof(InstallProject.AllowPathChange));

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

        _project.EnabledWizardPages.Clear();
        if (enabled.Count != WizardPageChoices.Length)
            foreach (var id in enabled)
                _project.EnabledWizardPages.Add(id);

        _project.MarkDirty();
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
        var applyBtn = new Button { Text = L("Builder_ApplyScript", "Apply Script"), Width = 110, Height = 30 };
        var reloadBtn = new Button { Text = L("Builder_ReloadFromFields", "Reload From Fields"), Width = 140, Height = 30 };
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
                label.Text = L("Builder_Modified", "Modified");
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
            Text = L("Builder_BuildWorkflow", "Build Workflow"),
            AutoSize = true,
            Font = SectionTitleFont,
            ForeColor = TextColor,
            Margin = new Padding(0, 0, 0, 4),
        }, 0, 0);
        var buildNow = new Button { Text = L("Builder_BuildSetupExe", "Build Setup.exe"), AutoSize = true, Height = 34, Padding = new Padding(14, 0, 14, 0), Margin = new Padding(12, 0, 0, 0) };
        StylePrimaryButton(buildNow);
        buildNow.Click += (_, _) => BuildInstaller();
        header.Controls.Add(buildNow, 1, 0);
        layout.Controls.Add(header, 0, layout.RowCount++);

        layout.Controls.Add(BuildWorkflowGroup("Output", workflow =>
        {
            AddFolderRow(workflow, "Output directory:", _project, nameof(InstallProject.OutputDir));
        }), 0, layout.RowCount++);

        var keyboardGroup = BuildWorkflowTextGroup("Keyboard Walkthrough", 150, out _keyboardWalkthroughBox);
        _keyboardWalkthroughBox.ReadOnly = true;
        _keyboardWalkthroughBox.Text = KeyboardWalkthroughText();
        layout.Controls.Add(keyboardGroup, 0, layout.RowCount++);

        var validationGroup = BuildWorkflowTextGroup("Validation Center", 190, out _validationCenterBox);
        _validationCenterBox.ReadOnly = true;
        _validationCenterBox.Text = L("Builder_ValidationCenterIsReadyClick", "Validation Center is ready. Click Validate Project to see grouped schema, semantic and resource diagnostics.");
        layout.Controls.Add(validationGroup, 0, layout.RowCount++);

        var capabilityGroup = BuildWorkflowTextGroup("Format Readiness", 190, out _formatCapabilityBox);
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
        refreshCapability.Click += (_, _) => RefreshFormatCapabilityReport();
        capabilityActions.Controls.Add(refreshCapability);
        capabilityGroup.Controls.Add(capabilityActions);
        capabilityActions.BringToFront();
        layout.Controls.Add(capabilityGroup, 0, layout.RowCount++);

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
        AddFolderRow(layout, L("Field_OutputDirectory", "Output directory:"),      proj, nameof(InstallProject.OutputDir));
        AddBoundRow(layout, L("Field_SetupFileName", "Setup file name:"),       proj, nameof(InstallProject.OutputBaseFilename));
        AddBoundRow(layout, L("Field_MainExecutable", "Main executable:"),       proj, nameof(InstallProject.MainExecutable));
        AddEnumRow<Architecture>(layout, "Target architecture:", proj, nameof(InstallProject.ArchitecturesAllowed));
        AddCheckRow(layout, L("Field_RegisterUninstallEntry", "Register uninstall entry"), proj, nameof(InstallProject.CreateUninstallEntry));
        AddCheckRow(layout, L("Field_CreateSystemRestorePoint", "Create system restore point"), proj, nameof(InstallProject.CreateRestorePoint));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildPayloadSection()
    {
        var p = NewSectionPanel();
        var layout = NewTwoColTable();
        var proj = _project;
        AddBoundRow(layout, L("Field_PayloadFolder", "Payload folder:"),       proj, nameof(InstallProject.PayloadFolderName));
        AddEnumRow<PayloadSourceType>(layout, "Payload source:", proj, nameof(InstallProject.PayloadSource));
        AddBoundRow(layout, L("Field_PayloadURL", "Payload URL:"),          proj, nameof(InstallProject.PayloadUrl));
        AddCheckRow(layout, L("Field_CompressPayload", "Compress payload"),       proj, nameof(InstallProject.CompressPayload));
        AddEnumRow<CompressionFormat>(layout, "Compression format:", proj, nameof(InstallProject.Compression));
        AddEnumRow<CompressionStrength>(layout, "Compression strength:", proj, nameof(InstallProject.CompressionLevel));
        AddCheckRow(layout, L("Field_SolidCompression", "Solid compression"),      proj, nameof(InstallProject.SolidCompression));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildPackageSection()
    {
        var p = NewSectionPanel();
        var layout = NewTwoColTable();
        var proj = _project;
        AddSectionRow(layout, L("Section_CodeSigning", "Code signing"));
        AddFileRow(layout, L("Field_CertificatePfx", "Certificate (.pfx):"), proj, nameof(InstallProject.CodeSignCertificatePath), "*.pfx");
        AddBoundRow(layout, L("Field_CertificatePassword", "Certificate password:"), proj, nameof(InstallProject.CodeSignCertificatePassword));
        AddBoundRow(layout, L("Field_TimestampURL", "Timestamp URL:"), proj, nameof(InstallProject.CodeSignTimestampUrl));
        AddSectionRow(layout, L("Section_OptionalPackageMetadata", "Optional package metadata"));
        AddEnumRow<InstallerOutputFormat>(layout, "Output format:", proj, nameof(InstallProject.OutputFormat));
        AddBoundRow(layout, L("Field_MSIXIdentity", "MSIX identity:"), proj, nameof(InstallProject.MsixIdentity));
        AddBoundRow(layout, L("Field_MSIXPublisher", "MSIX publisher:"), proj, nameof(InstallProject.MsixPublisher));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildCompressionSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddCheckRow(layout, L("Field_CompressPayload", "Compress payload"),       proj, nameof(InstallProject.CompressPayload));
        AddEnumRow<CompressionFormat>(layout, "Compression format:", proj, nameof(InstallProject.Compression));
        AddCheckRow(layout, L("Field_SolidCompression", "Solid compression"),      proj, nameof(InstallProject.SolidCompression));
        AddEnumRow<CompressionStrength>(layout, "Compression strength:", proj, nameof(InstallProject.CompressionLevel));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildCodeSignSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddFileRow(layout, L("Field_CertificatePfx", "Certificate (.pfx):"),    proj, nameof(InstallProject.CodeSignCertificatePath), "*.pfx");
        AddBoundRow(layout, L("Field_CertificatePassword", "Certificate password:"),   proj, nameof(InstallProject.CodeSignCertificatePassword));
        AddBoundRow(layout, L("Field_TimestampURL", "Timestamp URL:"),          proj, nameof(InstallProject.CodeSignTimestampUrl));
        p.Controls.Add(layout);
        return p;
    }

    private Panel BuildMsixSection()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = NewTwoColTable();
        var proj = _project;
        AddBoundRow(layout, L("Field_MSIXIdentity", "MSIX identity:"),    proj, nameof(InstallProject.MsixIdentity));
        AddBoundRow(layout, L("Field_MSIXPublisher", "MSIX publisher:"),   proj, nameof(InstallProject.MsixPublisher));
        AddBoundRow(layout, L("Field_UpdateURL", "Update URL:"),       proj, nameof(InstallProject.AppUpdatesURL));
        AddEnumRow<UpdateMode>(layout, "Update mode:", proj, nameof(InstallProject.AppUpdateMode));
        AddBoundRow(layout, L("Field_UpdateCheckHours", "Update check hours:"), proj, nameof(InstallProject.AppInstallerHoursBetweenUpdateChecks));
        AddCheckRow(layout, L("Field_PromptUsersBeforeUpdate", "Prompt users before update"), proj, nameof(InstallProject.AppInstallerShowPrompt));
        AddCheckRow(layout, L("Field_AllowDowngradeForceUpdate", "Allow downgrade/force update"), proj, nameof(InstallProject.AppInstallerForceUpdateFromAnyVersion));
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
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(dlg.FileName, _buildLogBox.Text); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        var copyBtn = new Button { Text = L("Builder_CopyAll", "Copy All"), Width = 90, Height = 28 };
        copyBtn.Click += (_, _) =>
        {
            if (_buildLogBox == null) return;
            try { Clipboard.SetText(_buildLogBox.Text); copyBtn.Text = L("Builder_Copied", "Copied!"); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Copy failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        var selectAllBtn = new Button { Text = L("Builder_SelectAll", "Select All"), Width = 90, Height = 28 };
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
            Text = L("Builder_LastBuildResult", "Last Build Result"),
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            Location = new Point(0, 0),
            AutoSize = true,
        };
        var statusBanner = new Label
        {
            Name = "StatusBanner",
            Text = L("Builder_NoBuildHasBeenRun", "No build has been run yet. Click Build to produce the Setup.exe."),
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
        var pathLbl = new Label { Text = L("Builder_EXEPath", "EXE path:"), Font = new Font("Segoe UI", 9, FontStyle.Bold), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
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
            statusBanner.Text = L("Builder_NoBuildHasBeenRun", "No build has been run yet. Click Build to produce the Setup.exe.");
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
            status.Text = L("Builder_NoBuildHasBeenRun", "No build has been run yet.");
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
    private ToolStripButton QuickStartButton() => MakeButton(
        L("Quick_Button", "Quick Start"),
        L("Quick_ButtonHint", "Guided setup of the fields a build requires"),
        (_, _) =>
        {
            using var quickStart = new QuickStartWizard(_controller.Project);
            if (quickStart.ShowDialog(this) != DialogResult.OK) return;

            _controller.Project.MarkDirty();
            BindProject();
            InvalidateAllContent();
            if (_activeSectionId is { } section) OnSectionSelected(this, section);
        });

    private ToolStripButton PreviewButton() => MakeButton("Preview", "Preview the install wizard", (_, _) => PreviewWizard());
    private ToolStripButton BuildButton() => MakeButton("Build", "Build the Setup.exe", (_, _) => BuildInstaller());
    private ToolStripButton PublishButton() => MakeButton("Publish", "Publish as ClickOnce", (_, _) => PublishProject());
    private ToolStripButton UpdatesButton() => MakeButton(Lang.LanguageManager.T("Update_Title"), Lang.LanguageManager.T("Update_Instructions"), (_, _) =>
    {
        using var form = global::Beep.Installer.Program.CreateUpdateCenter(_project, _runtimeArgs);
        form.ShowDialog(this);
    });
    private ToolStripButton TemplateButton() => MakeButton("Templates", "Preview and apply a built-in template update", (_, _) => UpdateFromTemplate());
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
        if (!_controller.ConfirmDiscardChanges(this)) return;

        using var dlg = new ProjectNewDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _controller.New(dlg.TemplateId, dlg.ProductName, dlg.Version, dlg.Publisher, dlg.SourceDirectory);
        if (!guided) return;

        // The wizard edits the project the controller just created, so cancelling it leaves a
        // perfectly usable project rather than nothing.
        using var quickStart = new QuickStartWizard(_controller.Project);
        if (quickStart.ShowDialog(this) == DialogResult.OK)
        {
            _controller.Project.MarkDirty();
            BindProject();
            InvalidateAllContent();
            if (_activeSectionId is { } section) OnSectionSelected(this, section);
        }
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
        SetStatus(result.Errors.Count == 0 ? "Validation passed." : $"{result.Errors.Count} error(s).");
    }

    private void PopulateValidationCenter(ProjectValidationCenterReport report)
    {
        if (_validationCenterBox == null || _validationCenterBox.IsDisposed)
            return;

        var writer = new StringWriter();
        writer.WriteLine($"Validation center: {_project.ProjectName}");
        writer.WriteLine($"Schema: {report.SchemaVersion}");
        writer.WriteLine($"Plan hash: {report.PlanHash}");
        writer.WriteLine($"Errors: {report.ErrorCount}, Warnings: {report.WarningCount}");
        writer.WriteLine();

        if (report.Areas.Count == 0)
        {
            writer.WriteLine("No validation findings. The project is clean under strict authoring validation.");
        }
        else
        {
            foreach (var area in report.Areas)
            {
                writer.WriteLine($"{area.Label} — {area.ErrorCount} error(s), {area.WarningCount} warning(s)");
                foreach (var finding in area.Findings)
                {
                    writer.WriteLine($"  [{finding.Severity}] {finding.Code} {finding.Path}");
                    writer.WriteLine($"      {finding.Message}");
                    if (!string.IsNullOrWhiteSpace(finding.Fix))
                        writer.WriteLine($"      Fix: {finding.Fix}");
                }
                writer.WriteLine();
            }
        }

        _validationCenterBox.Text = writer.ToString();
    }

    private void RefreshFormatCapabilityReport()
    {
        if (!ApplyScriptEditor()) return;
        var report = _controller.CreatePackageFormatCapabilityReport();
        var writer = new StringWriter();
        writer.WriteLine($"Format readiness: {_project.ProjectName}");
        writer.WriteLine($"Plan hash: {report.PlanHash}");
        writer.WriteLine($"Release readiness: {report.ReleaseReadinessStatus}");
        writer.WriteLine($"Ready: {report.ReadyFormatCount}, Warnings: {report.WarningFormatCount}, Blocked: {report.BlockedFormatCount}");
        writer.WriteLine(report.ReleaseReadinessSummary);
        writer.WriteLine();

        foreach (var format in report.Formats)
        {
            writer.WriteLine($"{format.Format}: {format.Status}");
            writer.WriteLine($"  {format.Summary}");
            if (format.CanonicalArtifacts.Count > 0)
                writer.WriteLine($"  Artifacts: {string.Join(", ", format.CanonicalArtifacts)}");

            foreach (var finding in format.Findings.Take(12))
            {
                var code = string.IsNullOrWhiteSpace(finding.Code) ? "finding" : finding.Code;
                writer.WriteLine($"  [{finding.Severity}] {code}: {finding.Message}");
            }

            if (format.Findings.Count > 12)
                writer.WriteLine($"  ... {format.Findings.Count - 12} more finding(s)");
            writer.WriteLine();
        }

        _nav.SelectSection("build");
        _formatCapabilityBox.Text = writer.ToString();
        SetStatus("Format readiness refreshed.");
    }

    private void PreviewWizard() { if (!ApplyScriptEditor()) return; using var f = new WizardPreviewForm(_project); f.ShowDialog(this); }
    private void ShowAbout() { MessageBox.Show(this, "Beep Installer — Package Builder\r\nVersion 1.0.0\r\n\r\nBuild self-contained Setup.exe installers for Windows.", "About Beep Installer", MessageBoxButtons.OK, MessageBoxIcon.Information); }

    private void UpdateFromTemplate()
    {
        if (!ApplyScriptEditor()) return;
        using var dlg = new TemplateUpdateDialog(ProjectTemplates.Builtins, id => _controller.PreviewTemplateUpdate(id));
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var preview = _controller.ApplyTemplateUpdate(dlg.SelectedTemplateId);
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
        SetStatus($"Applied template '{dlg.SelectedTemplateId}' — {preview.Diff.Count} change(s).");
        RefreshScriptPreview(force: true);
    }

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

        // The dialog's Cancel button was inert because nothing supplied a cancellation source.
        var buildCts = new CancellationTokenSource();
        progressForm.SetCancellationSource(buildCts);
        progressForm.Show(this);

        Task.Run(() =>
        {
            var result = _controller.Build(clean, buildCts.Token);
            var canceled = buildCts.IsCancellationRequested;
            BeginInvoke((Action)(() =>
            {
                _progress.Visible = false;
                _activeBuildProgressForm = null;
                progressForm.Close();
                buildCts.Dispose();

                // A cancelled build is neither a success nor a failure to show an error dialog for:
                // the user asked for it to stop, and telling them "Build failed" would read as a
                // defect in their project.
                if (canceled)
                {
                    SetStatus("Build canceled.");
                    RecordLastBuild(result);
                    ShowBuildResultInForm(result);
                    return;
                }

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
    private void EditComponentConditions()
    {
        // Carry the grid selection into the dialog so the author does not have to find the same
        // component a second time.
        var selected = _componentsGrid?.SelectedRows.Count > 0
            ? _componentsGrid.SelectedRows[0].DataBoundItem as InstallComponent
            : null;

        using var dlg = new ComponentConditionsDialog(_project.Components, selected);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _project.MarkDirty();
    }

    private void ShowHelp()
    {
        MessageBox.Show(
            this,
            "1. Set source directory (build output of your app).\r\n2. Configure components, prerequisites, shortcuts, registry and advanced resources.\r\n3. Use Ctrl+F to search sections, Ctrl+Tab / Ctrl+Shift+Tab to move through authoring sections, F7 to validate and F5 to build.\r\n4. Save the .bsetup script.\r\n5. Build → produces a self-contained Setup.exe.",
            "Beep Installer",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    internal static string KeyboardWalkthroughText()
        => """
           Keyboard-only authoring path
           1. Ctrl+F focuses section search. Type identity, source, components, firewall, IIS, config, build, etc.; Enter opens the first match and Escape clears search.
           2. Ctrl+Tab moves to the next visible authoring section; Ctrl+Shift+Tab moves to the previous visible section.
           3. Tab and Shift+Tab move through fields, grids, property details and action buttons inside the active section.
           4. Ctrl+T opens template diff/apply, Ctrl+P previews the wizard, F7 validates into the grouped Validation Center, and F5 builds.
           5. Ctrl+S saves, Ctrl+Shift+S saves as, Ctrl+O opens, and Ctrl+N starts a new installer project.
           """;

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
        var generation = ++_fileTreeGeneration;

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
    private async void ScanAndPopulate(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        if (_scanInProgress) return;    // re-entrancy: the button stays clickable while we run

        _scanInProgress = true;
        _project.SourceDirectory = dir;
        SetStatus($"Scanning {dir}…");
        UseWaitCursor = true;

        try
        {
            var result = await Task.Run(() => _controller.Scan(dir));
            SetStatus($"Scan complete: {result.FileCount} files ({result.TotalSizeBytes / 1024.0 / 1024.0:F1} MB), {result.ManagedCount} managed.");

            foreach (var warning in result.Warnings)
                Engine.Diag.Info("Builder", $"scan: {warning}");
        }
        catch (Exception ex)
        {
            // A scan failure must not take the builder down with it.
            SetStatus($"Scan failed: {ex.Message}");
            Engine.Diag.Warn("Builder", $"scan of '{dir}' failed", ex);
            MessageBox.Show(this, $"Could not scan '{dir}':{Environment.NewLine}{ex.Message}",
                "Scan failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
            _scanInProgress = false;
        }
    }

    private bool _scanInProgress;

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
        }
    }
}
