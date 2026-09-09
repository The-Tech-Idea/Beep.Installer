using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Beep.Installer.Models;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>
/// Guided output-format selection.
///
/// Choosing MSIX is not a packaging preference — it silently invalidates authored content. MSIX
/// cannot chain prerequisites, cannot expand prerequisite catalogs, cannot represent package nodes,
/// and cannot execute typed resource providers at all (a build with resources and MSIX output is
/// refused outright). None of that was visible at the point the format was chosen: it surfaced later
/// as a capability report, or as a build that stopped.
///
/// So the format picker lists, from <i>this</i> project, exactly what the choice would leave behind.
/// It also asks for the two identity fields MSIX genuinely requires, including the one that is
/// reliably got wrong: <c>MsixPublisher</c> is the certificate <b>subject</b>, not the display
/// publisher, and a package whose publisher does not match its signing certificate is rejected on
/// install rather than at build time.
/// </summary>
public sealed class PackagingWizard : Form
{
    /// <summary>3–50 ASCII letters, digits, periods or hyphens — the rule StoreReadinessChecker applies.</summary>
    private static readonly Regex IdentityName = new(@"^[A-Za-z0-9.\-]{3,50}$", RegexOptions.Compiled);

    private readonly InstallProject _project;
    private readonly ComboBox _format;
    private readonly Panel _formatFields;
    private readonly Label _impact;
    private readonly Label _validation;

    private TextBox? _identity;
    private TextBox? _publisher;
    private CheckBox? _singleFile;
    private CheckBox? _selfContained;
    private CheckBox? _solid;

    private sealed record FormatChoice(InstallerOutputFormat Value, string Label)
    {
        public override string ToString() => Label;
    }

    public InstallerOutputFormat Format => ((FormatChoice)_format.SelectedItem!).Value;

    /// <summary>
    /// Everything the dialog decides, with no control attached.
    ///
    /// The rules below (what MSIX cannot carry, what a legal package identity looks like) are worth
    /// testing on their own, and a test that has to construct a <see cref="Form"/> and reach past
    /// <c>private</c> to reach them is testing the layout by accident. Splitting the answer from the
    /// widgets that collect it means the decision is exercised directly and the wiring is exercised
    /// once, deliberately, through <see cref="CurrentChoice"/>.
    /// </summary>
    public readonly record struct Choice(
        InstallerOutputFormat Format,
        string Identity,
        string Publisher,
        bool SingleFile,
        bool SelfContained,
        bool SolidCompression);

    /// <summary>What the controls say right now — the bridge between the widgets and <see cref="Choice"/>.</summary>
    public Choice CurrentChoice => new(
        Format,
        _identity?.Text.Trim() ?? "",
        _publisher?.Text.Trim() ?? "",
        _singleFile?.Checked ?? _project.SingleFile,
        _selfContained?.Checked ?? _project.SelfContained,
        _solid?.Checked ?? _project.SolidCompression);

    public PackagingWizard(InstallProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));

        Text = L("Packaging_Title", "Output format");
        Size = new Size(740, 560);
        MinimumSize = new Size(660, 500);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(20) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = L("Packaging_Heading", "What should the build produce?"),
            AutoSize = true,
            Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        });

        root.Controls.Add(new Label
        {
            Text = L("Packaging_Explain",
                "The format decides what the installer can do. Anything the chosen format cannot carry is "
                + "listed below, taken from this project."),
            AutoSize = true,
            MaximumSize = new Size(660, 0),
            ForeColor = Ui.InstallerTheme.MutedText,
            Margin = new Padding(0, 0, 0, 12),
        });

        _format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360, AccessibleName = L("Packaging_Format", "Output format") };
        _format.Items.AddRange(new object[]
        {
            new FormatChoice(InstallerOutputFormat.Exe, L("Packaging_Exe", "Setup.exe — full capability, works everywhere")),
            new FormatChoice(InstallerOutputFormat.Msix, L("Packaging_Msix", "MSIX package — Store and managed deployment")),
            new FormatChoice(InstallerOutputFormat.MsixBundle, L("Packaging_MsixBundle", "MSIX bundle — several architectures in one package")),
        });
        _format.SelectedIndex = (int)_project.OutputFormat;
        _format.SelectedIndexChanged += (_, _) => Refresh_();
        root.Controls.Add(_format);

        _formatFields = new Panel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        root.Controls.Add(_formatFields);

        _impact = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(660, 0),
            Margin = new Padding(0, 12, 0, 0),
        };
        root.Controls.Add(_impact);

        _validation = new Label { AutoSize = true, ForeColor = Color.FromArgb(168, 32, 32), Margin = new Padding(0, 8, 0, 0) };
        root.Controls.Add(_validation);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(20, 8, 20, 16) };
        var cancel = new Button { Text = L("Common_Cancel", "Cancel"), Width = 92, Height = 30, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = L("Common_OK", "OK"), Width = 92, Height = 30 };
        buttons.Controls.AddRange(new Control[] { cancel, ok });

        ok.Click += (_, _) =>
        {
            var problem = CurrentProblem();
            if (problem != null) { _validation.Text = problem; return; }
            Apply();
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(root);
        Controls.Add(buttons);
        CancelButton = cancel;

        Refresh_();
        Engine.Accessibility.Attach(this);
    }

    /// <summary>
    /// What this project would lose by choosing the selected format.
    ///
    /// Computed from the project rather than described in general terms: "3 prerequisites will not
    /// be installed" is actionable in a way that "MSIX does not support prerequisite chaining" is
    /// not, when you cannot remember whether you authored any.
    /// </summary>
    public static IReadOnlyList<string> IncompatibilitiesFor(InstallProject project, InstallerOutputFormat format)
    {
        if (format == InstallerOutputFormat.Exe) return Array.Empty<string>();

        var lost = new List<string>();

        if (project.Resources.Count > 0)
            lost.Add(string.Format(L("Packaging_LostResources",
                "{0} typed resource operation(s) — MSIX cannot execute extension providers, and the build will refuse."),
                project.Resources.Count));

        if (project.Prerequisites.Count > 0)
            lost.Add(string.Format(L("Packaging_LostPrereqs",
                "{0} prerequisite(s) — MSIX cannot chain installers."), project.Prerequisites.Count));

        if (project.PrerequisiteCatalogs.Count > 0)
            lost.Add(string.Format(L("Packaging_LostCatalogs",
                "{0} prerequisite catalog(s) — catalog expansion produces resources MSIX cannot run."),
                project.PrerequisiteCatalogs.Count));

        if (project.Packages.Count > 0)
            lost.Add(string.Format(L("Packaging_LostPackages",
                "{0} package node(s) — these are suite/chainer resources."), project.Packages.Count));

        if (project.CustomActions.Count > 0)
            lost.Add(string.Format(L("Packaging_LostActions",
                "{0} custom action(s) — MSIX runs no install-time executables."), project.CustomActions.Count));

        if (project.WindowsServices.Count > 0)
            lost.Add(string.Format(L("Packaging_LostServices",
                "{0} Windows service(s) — service registration is not represented in MSIX output."),
                project.WindowsServices.Count));

        return lost;
    }

    private void Refresh_()
    {
        _validation.Text = "";
        _formatFields.Controls.Clear();
        _identity = _publisher = null;
        _singleFile = _selfContained = _solid = null;

        var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        if (Format == InstallerOutputFormat.Exe)
        {
            _singleFile = Check(table, L("Packaging_SingleFile", "Single self-contained file"), _project.SingleFile);
            _selfContained = Check(table, L("Packaging_SelfContained", "Include the .NET runtime"), _project.SelfContained);
            _solid = Check(table, L("Field_SolidCompression", "Solid compression"), _project.SolidCompression);
            Hint(table, L("Packaging_SolidHint",
                "Solid compression deduplicates identical files before compressing, which helps most when the "
                + "payload has many copies of the same assembly."));
        }
        else
        {
            _identity = Field(table, L("Field_MSIXIdentity", "MSIX identity:"), _project.MsixIdentity);
            Hint(table, L("Packaging_IdentityHint",
                "3–50 ASCII letters, digits, periods or hyphens. This is package identity, not a display name."));

            _publisher = Field(table, L("Field_MSIXPublisher", "MSIX publisher:"), _project.MsixPublisher);
            Hint(table, L("Packaging_PublisherHint",
                "The certificate subject, exactly — for example CN=Contoso Ltd, O=Contoso Ltd, C=GB. A package "
                + "whose publisher does not match its signing certificate is rejected when it is installed, "
                + "not when it is built."));
        }

        _formatFields.Controls.Add(table);
        DescribeImpact();
    }

    private void DescribeImpact()
    {
        var lost = IncompatibilitiesFor(_project, Format);
        if (lost.Count == 0)
        {
            _impact.Text = Format == InstallerOutputFormat.Exe
                ? L("Packaging_ExeFine", "Everything authored in this project is supported by this format.")
                : L("Packaging_MsixFine", "Nothing in this project depends on features MSIX cannot represent.");
            _impact.ForeColor = Ui.InstallerTheme.MutedText;
            return;
        }

        _impact.Text = L("Packaging_LostHeading", "This format cannot carry the following, which you have authored:")
                       + Environment.NewLine
                       + string.Join(Environment.NewLine, lost.Select(l => "  • " + l));
        _impact.ForeColor = Color.FromArgb(150, 90, 0);
    }

    private string? CurrentProblem() => Validate(CurrentChoice, _project);

    /// <summary>Why this choice cannot be applied, or <c>null</c> if it can.</summary>
    public static string? Validate(Choice choice, InstallProject project)
    {
        if (choice.Format == InstallerOutputFormat.Exe) return null;

        if (!IdentityName.IsMatch(choice.Identity))
            return L("Packaging_BadIdentity",
                "MSIX identity must be 3–50 ASCII letters, digits, periods or hyphens.");

        if (string.IsNullOrWhiteSpace(choice.Publisher))
            return L("Packaging_NeedPublisher",
                "MSIX needs the certificate subject as its publisher.");

        // Refusing outright would be wrong -- the author may intend to remove the resources. Saying
        // so plainly is not the same as blocking.
        if (project.Resources.Count > 0)
            return L("Packaging_ResourcesBlock",
                "This project has typed resources, and a build with MSIX output refuses them. Remove them, or "
                + "choose Setup.exe.");

        return null;
    }

    internal void Apply() => ApplyTo(CurrentChoice, _project);

    /// <summary>Writes the choice onto the project.</summary>
    public static void ApplyTo(Choice choice, InstallProject project)
    {
        project.OutputFormat = choice.Format;

        if (choice.Format == InstallerOutputFormat.Exe)
        {
            project.SingleFile = choice.SingleFile;
            project.SelfContained = choice.SelfContained;
            project.SolidCompression = choice.SolidCompression;
            return;
        }

        project.MsixIdentity = choice.Identity;
        project.MsixPublisher = choice.Publisher;
    }

    // ── layout helpers ──────────────────────────────────────────────────────

    private static int NextRow(TableLayoutPanel table)
    {
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return table.RowCount - 1;
    }

    private static void Hint(TableLayoutPanel table, string text)
        => table.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            ForeColor = Ui.InstallerTheme.MutedText,
            Margin = new Padding(0, 0, 0, 10),
        }, 1, NextRow(table));

    private static TextBox Field(TableLayoutPanel table, string caption, string? value)
    {
        var row = NextRow(table);
        table.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 4) }, 0, row);
        var box = new TextBox
        {
            Text = value ?? "",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 4),
            AccessibleName = caption.TrimEnd(':'),
        };
        table.Controls.Add(box, 1, row);
        return box;
    }

    private static CheckBox Check(TableLayoutPanel table, string caption, bool value)
    {
        var row = NextRow(table);
        var box = new CheckBox { Text = caption, Checked = value, AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        table.SetColumnSpan(box, 2);
        table.Controls.Add(box, 0, row);
        return box;
    }
}
