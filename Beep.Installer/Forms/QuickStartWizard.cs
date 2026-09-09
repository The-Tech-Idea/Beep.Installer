using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Models;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>
/// A guided path from "I have an application" to "I have a Setup.exe".
///
/// The Package Builder is a good tool for someone who already knows what a `.bsetup` contains: a
/// left nav with thirty sections, every one of them optional, in no particular order. For a first
/// project that is the wrong shape entirely — nothing indicates which four of those thirty are
/// required, so the common outcome is a build that fails validation on something the author never
/// knew they had to fill in.
///
/// This asks only for what a build genuinely cannot proceed without, in the order the answers
/// depend on each other, and leaves everything else at its authored default. The full builder stays
/// exactly as it was for people who want it; this is the on-ramp, not a replacement.
/// </summary>
public sealed class QuickStartWizard : Form
{
    private readonly InstallProject _project;
    private readonly Panel _content;
    private readonly Label _heading;
    private readonly Label _explain;
    private readonly Label _stepLabel;
    private readonly Button _back;
    private readonly Button _next;
    private readonly Label _validation;

    private readonly Step[] _steps;
    private int _current;

    /// <summary>One question, its explanation, and whether it has been answered.</summary>
    private sealed record Step(
        string Heading,
        string Explain,
        Func<Control> Build,
        Func<string?> Validate);

    /// <summary>The project the author ends up with. Only meaningful when the dialog returns OK.</summary>
    public InstallProject Project => _project;

    /// <summary>
    /// How many questions the on-ramp asks.
    ///
    /// Public because it is a claim worth holding: four questions is the point of the wizard, and a
    /// fifth would make it the builder again. Exposing the count beats a test reaching for the step
    /// array through reflection.
    /// </summary>
    public int StepCount => _steps.Length;

    public QuickStartWizard(InstallProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));

        Text = L("Quick_Title", "New installer — quick start");
        Size = new Size(720, 520);
        MinimumSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        // Absolute sizes below need DPI auto-scaling or the dialog clips at 125%+.
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);

        _steps = new[]
        {
            new Step(
                L("Quick_ProductHeading", "What are you installing?"),
                L("Quick_ProductExplain",
                    "The name and version your users will see, in Add/Remove Programs and in the installer itself."),
                BuildProductStep,
                ValidateProduct),

            new Step(
                L("Quick_SourceHeading", "Where are the files?"),
                L("Quick_SourceExplain",
                    "The folder holding the application to install — usually your build output, such as bin\\Release. "
                    + "Everything in it is packaged unless you exclude it later."),
                BuildSourceStep,
                ValidateSource),

            new Step(
                L("Quick_TargetHeading", "Where should it install?"),
                L("Quick_TargetExplain",
                    "Per-machine installs go under Program Files and need administrator rights. Per-user installs "
                    + "go under the user's own folder and do not."),
                BuildTargetStep,
                ValidateTarget),

            new Step(
                L("Quick_ReviewHeading", "Ready to build"),
                L("Quick_ReviewExplain",
                    "These are the answers that will be written to your .bsetup. Everything else keeps its default, "
                    + "and the full builder can change any of it afterwards."),
                BuildReviewStep,
                () => null),
        };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
        _heading = new Label { AutoSize = true, Font = new Font(Font.FontFamily, 14F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
        _explain = new Label { Dock = DockStyle.Fill, AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = Ui.InstallerTheme.MutedText };
        header.Controls.Add(_heading);
        header.Controls.Add(_explain);

        _content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 16, 0, 8) };
        _validation = new Label { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.FromArgb(168, 32, 32), Margin = new Padding(0, 4, 0, 4) };

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _stepLabel = new Label { AutoSize = true, ForeColor = Ui.InstallerTheme.MutedText, Anchor = AnchorStyles.Left };

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        _back = new Button { Text = L("Btn_Back", "< Back"), Width = 90, Height = 30 };
        _next = new Button { Text = L("Btn_Next", "Next >"), Width = 90, Height = 30 };
        var cancel = new Button { Text = L("Common_Cancel", "Cancel"), Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange(new Control[] { _back, _next, cancel });

        footer.Controls.Add(_stepLabel, 0, 0);
        footer.Controls.Add(buttons, 1, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_content, 0, 1);
        root.Controls.Add(_validation, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);

        CancelButton = cancel;
        _back.Click += (_, _) => Go(_current - 1);
        _next.Click += (_, _) => Advance();

        Engine.Accessibility.Attach(this);
        Go(0);
    }

    private void Go(int index)
    {
        if (index < 0 || index >= _steps.Length) return;

        _current = index;
        var step = _steps[index];

        _heading.Text = step.Heading;
        _explain.Text = step.Explain;
        _stepLabel.Text = string.Format(L("Quick_StepOf", "Step {0} of {1}"), index + 1, _steps.Length);
        _validation.Text = "";

        _content.Controls.Clear();
        var body = step.Build();
        body.Dock = DockStyle.Fill;
        _content.Controls.Add(body);

        _back.Enabled = index > 0;
        _next.Text = index == _steps.Length - 1
            ? L("Quick_CreateAndBuild", "Create")
            : L("Btn_Next", "Next >");

        Engine.Accessibility.EnsureAccessibility(this);
    }

    /// <summary>
    /// Validates before moving on, so a problem is reported next to the question that caused it
    /// rather than as a build failure three steps later.
    /// </summary>
    private void Advance()
    {
        var problem = _steps[_current].Validate();
        if (problem != null)
        {
            _validation.Text = problem;
            return;
        }

        if (_current == _steps.Length - 1)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        Go(_current + 1);
    }

    // ── step 1 · product ────────────────────────────────────────────────────

    private TextBox _nameBox = null!;
    private TextBox _versionBox = null!;
    private TextBox _publisherBox = null!;

    private Control BuildProductStep()
    {
        var table = TwoColumn();
        _nameBox = AddField(table, L("Field_ProductName", "Product name:"), _project.AppName);
        _versionBox = AddField(table, L("Field_Version", "Version:"), string.IsNullOrWhiteSpace(_project.AppVersion) ? "1.0.0" : _project.AppVersion);
        _publisherBox = AddField(table, L("Field_Publisher", "Publisher:"), _project.AppPublisher);
        return table;
    }

    private string? ValidateProduct()
    {
        var problem = CheckProduct(_nameBox.Text, _versionBox.Text);
        if (problem != null) return problem;

        ApplyProduct(_project, _nameBox.Text, _versionBox.Text, _publisherBox.Text);
        return null;
    }

    /// <summary>Commits a product identity that <see cref="CheckProduct"/> has already accepted.</summary>
    public static void ApplyProduct(InstallProject project, string name, string version, string publisher)
    {
        project.AppName = name.Trim();
        project.AppVersion = version.Trim();
        project.AppPublisher = publisher.Trim();
    }

    /// <summary>Why this product identity is not usable, or <c>null</c> if it is.</summary>
    public static string? CheckProduct(string name, string version)
    {
        if (string.IsNullOrWhiteSpace(name))
            return L("Quick_NeedName", "A product name is required — it is what users see when they install and uninstall.");

        // The one parser the schema validator and the runtime version gate both use, so the
        // wizard cannot accept something the build will later reject.
        if (!Engine.SemanticVersion.TryParse(version, out _))
            return L("Quick_NeedVersion", "The version must look like 1.0 or 1.0.0 — upgrades are decided by comparing it.");

        return null;
    }

    // ── step 2 · source ─────────────────────────────────────────────────────

    private TextBox _sourceBox = null!;
    private Label _sourceSummary = null!;

    private Control BuildSourceStep()
    {
        var table = TwoColumn();
        _sourceBox = AddField(table, L("Builder_SourceDir", "Source folder:"), _project.SourceDirectory);

        var browse = new Button { Text = L("Btn_Browse", "Browse…"), Width = 90, Height = 26, Margin = new Padding(0, 4, 0, 4) };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = L("Quick_PickSource", "Select the folder to install") };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _sourceBox.Text = dialog.SelectedPath;
                DescribeSource();
            }
        };
        var row = AddTableRow(table);
        table.Controls.Add(new Label(), 0, row);
        table.Controls.Add(browse, 1, row);

        _sourceSummary = new Label { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Ui.InstallerTheme.MutedText };
        row = AddTableRow(table);
        table.SetColumnSpan(_sourceSummary, 2);
        table.Controls.Add(_sourceSummary, 0, row);

        _sourceBox.TextChanged += (_, _) => DescribeSource();
        DescribeSource();
        return table;
    }

    /// <summary>
    /// Says what is actually in the folder. "142 files, 38.4 MB" is the difference between trusting
    /// the choice and finding out at build time that it pointed at an empty directory.
    /// </summary>
    private void DescribeSource()
    {
        var path = _sourceBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            _sourceSummary.Text = "";
            return;
        }

        try
        {
            var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToList();
            var bytes = files.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
            _sourceSummary.Text = string.Format(
                L("Quick_SourceSummary", "{0} files, {1:F1} MB will be packaged."),
                files.Count, bytes / 1024.0 / 1024.0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _sourceSummary.Text = L("Quick_SourceUnreadable", "That folder could not be read.");
        }
    }

    private string? ValidateSource()
    {
        var path = _sourceBox.Text.Trim();
        var problem = CheckSource(path);
        if (problem != null) return problem;

        _project.SourceDirectory = path;
        return null;
    }

    /// <summary>Why this source folder cannot be packaged, or <c>null</c> if it can.</summary>
    public static string? CheckSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return L("Quick_NeedSource", "Choose the folder holding the application to install.");
        if (!Directory.Exists(path))
            return L("Quick_SourceMissing", "That folder does not exist.");
        if (!Directory.EnumerateFileSystemEntries(path).Any())
            return L("Quick_SourceEmpty", "That folder is empty — the installer would contain nothing.");

        return null;
    }

    // ── step 3 · target ─────────────────────────────────────────────────────

    private RadioButton _perMachine = null!;
    private RadioButton _perUser = null!;
    private TextBox _folderBox = null!;

    private Control BuildTargetStep()
    {
        var table = TwoColumn();

        _perMachine = new RadioButton
        {
            Text = L("Quick_PerMachine", "All users (Program Files, needs administrator)"),
            AutoSize = true,
            Checked = _project.DefaultScope == InstallationScope.Machine,
        };
        _perUser = new RadioButton
        {
            Text = L("Quick_PerUser", "Just me (no administrator needed)"),
            AutoSize = true,
            Checked = _project.DefaultScope == InstallationScope.User,
        };

        var row = AddTableRow(table);
        table.SetColumnSpan(_perMachine, 2);
        table.Controls.Add(_perMachine, 0, row);
        row = AddTableRow(table);
        table.SetColumnSpan(_perUser, 2);
        table.Controls.Add(_perUser, 0, row);

        _folderBox = AddField(table, L("Field_DefaultInstallPath", "Install folder:"),
            string.IsNullOrWhiteSpace(_project.DefaultDirName) ? DefaultFolderFor(_perUser.Checked) : _project.DefaultDirName);

        // Keep the suggested folder honest when the scope changes; a per-user install defaulting to
        // Program Files is the sort of thing that only fails on someone else's machine.
        void Sync(object? sender, EventArgs e) => _folderBox.Text = DefaultFolderFor(_perUser.Checked);
        _perUser.CheckedChanged += Sync;
        _perMachine.CheckedChanged += Sync;

        return table;
    }

    private string DefaultFolderFor(bool perUser) => DefaultFolderFor(_project.AppName, perUser);

    /// <summary>The install location suggested for a scope, in the constant form the runtime expands.</summary>
    public static string DefaultFolderFor(string? appName, bool perUser)
    {
        var name = string.IsNullOrWhiteSpace(appName) ? "MyApp" : appName;
        return perUser ? $@"{{localappdata}}\{name}" : $@"{{pf}}\{name}";
    }

    private string? ValidateTarget()
    {
        var problem = CheckTarget(_folderBox.Text);
        if (problem != null) return problem;

        _project.DefaultDirName = _folderBox.Text.Trim();
        ApplyScope(_project, _perUser.Checked);
        return null;
    }

    /// <summary>Why this install location is not usable, or <c>null</c> if it is.</summary>
    public static string? CheckTarget(string folder)
        => string.IsNullOrWhiteSpace(folder)
            ? L("Quick_NeedFolder", "Choose where the application should be installed.")
            : null;

    /// <summary>
    /// Sets scope and privilege together.
    ///
    /// They are separate in the model but not independent in practice: a per-user install that
    /// demands elevation prompts for something it does not need, and a machine-wide install that
    /// does not ask will fail on the first write outside the user profile.
    /// </summary>
    public static void ApplyScope(InstallProject project, bool perUser)
    {
        project.DefaultScope = perUser ? InstallationScope.User : InstallationScope.Machine;
        project.PrivilegesRequired = perUser ? PrivilegeLevel.Lowest : PrivilegeLevel.Admin;
    }

    // ── step 4 · review ─────────────────────────────────────────────────────

    private Control BuildReviewStep()
    {
        var summary = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9F),
            AccessibleName = L("Quick_ReviewHeading", "Ready to build"),
        };

        summary.Lines = new[]
        {
            $"{L("Field_ProductName", "Product name:"),-22} {_project.AppName}",
            $"{L("Field_Version", "Version:"),-22} {_project.AppVersion}",
            $"{L("Field_Publisher", "Publisher:"),-22} {_project.AppPublisher}",
            "",
            $"{L("Builder_SourceDir", "Source folder:"),-22} {_project.SourceDirectory}",
            $"{L("Field_DefaultInstallPath", "Install folder:"),-22} {_project.DefaultDirName}",
            $"{L("Quick_Scope", "Scope:"),-22} {(_project.DefaultScope == InstallationScope.User ? L("Quick_PerUserShort", "Just me") : L("Quick_PerMachineShort", "All users"))}",
            "",
            L("Quick_NextSteps",
                "The full builder opens next. Components, shortcuts, registry entries, signing and "
                + "packaging format are all optional and can be set there."),
        };

        return summary;
    }

    // ── small layout helpers ────────────────────────────────────────────────

    private static TableLayoutPanel TwoColumn()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        return table;
    }

    private static int AddTableRow(TableLayoutPanel table)
    {
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return table.RowCount - 1;
    }

    private static TextBox AddField(TableLayoutPanel table, string caption, string? value)
    {
        var row = AddTableRow(table);
        var label = new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 4) };
        var box = new TextBox
        {
            Text = value ?? "",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 4),
            AccessibleName = caption.TrimEnd(':'),
        };
        table.Controls.Add(label, 0, row);
        table.Controls.Add(box, 1, row);
        return box;
    }
}
