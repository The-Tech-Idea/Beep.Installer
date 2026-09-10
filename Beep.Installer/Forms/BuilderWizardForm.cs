using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Ui;
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
    private static readonly (string Id, string Title)[] GoldenPath =
    {
        ("identity", "Identity"),
        ("source", "Source"),
        ("components", "Components"),
        ("shortcuts", "Shortcuts"),
        ("registry", "Registry"),
        ("output", "Output"),
        ("codesign", "Code signing"),
        ("build", "Build"),
    };

    private readonly InstallerController _controller;
    private readonly PackageBuilderForm _advanced;
    private int _stepIndex = -1; // -1 = step 0, "new project"; 0..GoldenPath.Length-1 = GoldenPath

    private Panel _stepHost = null!;
    private Label _titleLabel = null!;
    private FlowLayoutPanel _stepperStrip = null!;
    private Button _backBtn = null!;
    private Button _nextBtn = null!;
    private Label _stepValidationLabel = null!;

    // Step 0 fields (mirrors ProjectNewDialog's fields -- see P12 §3.2 step 0; not reparented from
    // that dialog because its own OK/Cancel buttons are bound to *its* DialogResult, which would
    // fight this form's non-modal Back/Next).
    private ComboBox _templateBox = null!;
    private TextBox _nameBox = null!;
    private TextBox _versionBox = null!;
    private TextBox _publisherBox = null!;
    private TextBox _sourceBox = null!;
    private bool _projectCreated;

    /// <summary>Raised once the user asks for the full section browser + toolbar instead.</summary>
    public event EventHandler? AdvancedRequested;

    public BuilderWizardForm(InstallerController controller)
    {
        _controller = controller;
        _advanced = new PackageBuilderForm(controller);
        _projectCreated = HasRealProject(controller.Project);

        InitializeUi();
        GoTo(_projectCreated ? 0 : -1);
    }

    /// <summary>A controller from CreateColdStartController's reopen-most-recent path already has a
    /// real project; step 0 is only for a genuinely fresh one.</summary>
    private static bool HasRealProject(InstallProject project)
        => !string.IsNullOrWhiteSpace(project.SourceDirectory) || project.Components.Count > 0;

    /// <summary>The hidden Advanced-mode form, so the caller can show it after "Open full editor"
    /// without constructing a second one against the same controller.</summary>
    public PackageBuilderForm AdvancedForm => _advanced;

    private void InitializeUi()
    {
        Text = L("Wizard_BeepInstaller", "Beep Installer");
        Size = new Size(1000, 700);
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Icon = SystemIcons.Application;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(14, 10, 14, 6), BackColor = InstallerTheme.Sidebar };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _titleLabel = new Label { AutoSize = true, Font = new Font("Segoe UI", 13, FontStyle.Bold), Anchor = AnchorStyles.Left };
        var advancedBtn = new Button { Text = L("Wizard_OpenFullEditor", "Open full editor"), AutoSize = true };
        advancedBtn.Click += (_, _) =>
        {
            PrepareAdvancedHandoff();
            AdvancedRequested?.Invoke(this, EventArgs.Empty);
        };
        header.Controls.Add(_titleLabel, 0, 0);
        header.Controls.Add(advancedBtn, 1, 0);

        _stepperStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(14, 0, 14, 6), BackColor = InstallerTheme.Sidebar };

        _stepHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(14, 8, 14, 10) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _backBtn = new Button { Text = L("Wizard_Back", "< Back"), AutoSize = true };
        _backBtn.Click += (_, _) => GoTo(_stepIndex - 1);
        _stepValidationLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DarkRed };
        _nextBtn = new Button { Text = L("Wizard_Next", "Next >"), AutoSize = true };
        _nextBtn.Click += (_, _) => OnNext();
        footer.Controls.Add(_backBtn, 0, 0);
        footer.Controls.Add(_stepValidationLabel, 1, 0);
        footer.Controls.Add(_nextBtn, 2, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_stepperStrip, 0, 1);
        root.Controls.Add(_stepHost, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);

        BuildStepperStrip();
        Engine.Accessibility.Attach(this);
    }

    private void BuildStepperStrip()
    {
        _stepperStrip.Controls.Clear();
        var newProjectBtn = MakeStepperButton(-1, L("Wizard_StepNewProject", "New project"));
        _stepperStrip.Controls.Add(newProjectBtn);
        for (var i = 0; i < GoldenPath.Length; i++)
            _stepperStrip.Controls.Add(MakeStepperButton(i, GoldenPath[i].Title));
    }

    private Button MakeStepperButton(int index, string title)
    {
        var button = new Button { Text = title, AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 6, 0), Tag = index };
        button.Click += (_, _) =>
        {
            // Jumping directly (not just Next/Back) is always allowed to an already-reached step, or
            // backward; jumping forward past an invalid step is blocked the same way Next is.
            if (index > _stepIndex && _advanced.ActiveSectionHasErrors()) return;
            GoTo(index);
        };
        return button;
    }

    private void OnNext()
    {
        if (_stepIndex == -1)
        {
            // Only creates the project the first time. Revisiting this step later (Back, or the
            // stepper strip) must not re-run New() -- that would silently discard whatever the user
            // already edited on Identity/Source/etc. against the project New() created originally.
            if (!_projectCreated && !TryCreateProject()) return;
            GoTo(0);
            return;
        }

        if (_advanced.ActiveSectionHasErrors())
        {
            _stepValidationLabel.Text = L("Wizard_FixErrorsBeforeContinuing", "Fix the errors on this step before continuing.");
            return;
        }

        GoTo(_stepIndex + 1);
    }

    private void GoTo(int index)
    {
        index = Math.Clamp(index, -1, GoldenPath.Length - 1);
        _stepIndex = index;
        _stepValidationLabel.Text = "";

        _stepHost.Controls.Clear();
        if (index == -1)
        {
            _titleLabel.Text = L("Wizard_StepNewProject", "New project");
            _stepHost.Controls.Add(BuildNewProjectStep());
        }
        else
        {
            var (id, title) = GoldenPath[index];
            _titleLabel.Text = title;
            _advanced.OpenSection(id);
            if (_advanced.ActiveSection is { } section)
            {
                section.Dock = DockStyle.Fill;
                _stepHost.Controls.Add(section);
            }
        }

        _backBtn.Enabled = index > -1;
        _nextBtn.Text = index == GoldenPath.Length - 1
            ? L("Wizard_Finish", "Finish")
            : L("Wizard_Next", "Next >");
        _nextBtn.Visible = index != GoldenPath.Length - 1; // Build step has its own Build button

        foreach (Button b in _stepperStrip.Controls)
            b.FlatStyle = (int)b.Tag! == index ? FlatStyle.Popup : FlatStyle.Flat;
    }

    private Panel BuildNewProjectStep()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));

        int r = 0;
        layout.Controls.Add(FieldLabel(L("NewProject_Template", "Template:")), 0, r);
        _templateBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var t in ProjectTemplates.Builtins) _templateBox.Items.Add(t);
        _templateBox.SelectedIndex = 0;
        layout.SetColumnSpan(_templateBox, 2);
        layout.Controls.Add(_templateBox, 1, r++);

        // Prefill from the controller's current project once it has been created, so revisiting this
        // step (Back, or the stepper strip) reflects what New()/Identity actually set instead of
        // showing the original placeholder defaults again.
        var project = _controller.Project;

        layout.Controls.Add(FieldLabel(L("NewProject_ProductName", "Product name:")), 0, r);
        _nameBox = new TextBox { Dock = DockStyle.Fill, Text = _projectCreated ? project.AppName : "MyApplication" };
        layout.SetColumnSpan(_nameBox, 2);
        layout.Controls.Add(_nameBox, 1, r++);

        layout.Controls.Add(FieldLabel(L("NewProject_Version", "Version:")), 0, r);
        _versionBox = new TextBox { Dock = DockStyle.Fill, Text = _projectCreated ? project.AppVersion : "1.0.0" };
        layout.SetColumnSpan(_versionBox, 2);
        layout.Controls.Add(_versionBox, 1, r++);

        layout.Controls.Add(FieldLabel(L("NewProject_Publisher", "Publisher:")), 0, r);
        _publisherBox = new TextBox { Dock = DockStyle.Fill, Text = _projectCreated ? project.AppPublisher : Environment.UserName };
        layout.SetColumnSpan(_publisherBox, 2);
        layout.Controls.Add(_publisherBox, 1, r++);

        layout.Controls.Add(FieldLabel(L("NewProject_SourceDirectory", "Source directory:")), 0, r);
        _sourceBox = new TextBox { Dock = DockStyle.Fill, Text = _projectCreated ? project.SourceDirectory : "" };
        var browseBtn = new Button { Text = "…", Dock = DockStyle.Fill };
        browseBtn.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog();
            if (d.ShowDialog(this) == DialogResult.OK) _sourceBox.Text = d.SelectedPath;
        };
        layout.Controls.Add(_sourceBox, 1, r);
        layout.Controls.Add(browseBtn, 2, r++);

        var hint = new Label
        {
            Text = L("NewProject_TheSourceDirectoryContains", "The source directory contains the files that will be installed (e.g. your app's bin/Release output)."),
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            AutoSize = false,
            Height = 40,
        };
        layout.SetColumnSpan(hint, 3);
        layout.Controls.Add(hint, 0, r);

        var wrap = new Panel { Dock = DockStyle.Fill };
        wrap.Controls.Add(layout);
        return wrap;
    }

    private static Label FieldLabel(string text)
        => new() { Text = text, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };

    /// <summary>
    /// The step host holds whatever <see cref="_advanced"/> section panel is currently visible --
    /// re-parented out of <see cref="_advanced"/>'s own content host in <see cref="GoTo"/>. Before
    /// handing off to Advanced mode, put it back so <see cref="_advanced"/> shows something instead
    /// of an empty content area (re-parenting is idempotent: <c>OpenSection</c> re-adds the same
    /// cached panel, silently detaching it from wherever it currently lives).
    /// </summary>
    private void PrepareAdvancedHandoff()
    {
        var id = _stepIndex == -1 ? "identity" : GoldenPath[_stepIndex].Id;
        _advanced.OpenSection(id);
    }

    private bool TryCreateProject()
    {
        var name = _nameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _stepValidationLabel.Text = L("Builder_ProductNameRequired", "Product name is required.");
            return false;
        }

        var templateId = _templateBox.SelectedItem is ProjectTemplate t ? t.Id : ProjectTemplates.EmptyId;
        _controller.New(templateId, name, _versionBox.Text.Trim(), _publisherBox.Text.Trim(), _sourceBox.Text.Trim());
        _projectCreated = true;
        return true;
    }
}
