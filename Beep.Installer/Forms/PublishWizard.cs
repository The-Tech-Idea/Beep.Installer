using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Beep.Installer.Models;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Forms;

/// <summary>
/// Guided ClickOnce publish.
///
/// Publishing used to be a single folder picker. That skipped the two settings that decide whether
/// the deployment actually works, both of which <see cref="Engine.InstallerController.Publish"/> has
/// always accepted:
///
/// <list type="bullet">
/// <item><b>The update URL.</b> This is baked into the deployment manifest as the address installed
/// clients check for new versions. It is <i>not</i> the folder being written now — publishing to
/// <c>D:\drop</c> and serving from <c>https://…</c> is the normal case, and getting this wrong is
/// the classic ClickOnce failure: the install works, and it never updates again.</item>
/// <item><b>Signing.</b> An unsigned deployment triggers SmartScreen's "unrecognized app"
/// interstitial, which most users read as "this is malware".</item>
/// </list>
///
/// Neither was asked, so both silently took a default. This asks, explains what the answer means,
/// and shows what will happen before it happens.
/// </summary>
public sealed class PublishWizard : Form
{
    private readonly InstallProject _project;

    private TextBox _folderBox = null!;
    private TextBox _urlBox = null!;
    private CheckBox _signBox = null!;
    private Label _signalLabel = null!;
    private Label _validation = null!;

    /// <summary>Folder the publish output is written to.</summary>
    public string PublishFolder => _folderBox.Text.Trim();

    /// <summary>Address baked into the manifest for clients to check. Empty means "no update URL".</summary>
    public string UpdateUrl => _urlBox.Text.Trim();

    /// <summary>Whether to sign the manifests when a certificate is configured.</summary>
    public bool Sign => _signBox.Checked;

    /// <summary>Everything the dialog decides, with no control attached.</summary>
    public readonly record struct Choice(string Folder, string UpdateUrl, bool Sign);

    /// <summary>What the controls say right now.</summary>
    public Choice CurrentChoice => new(PublishFolder, UpdateUrl, Sign);

    public PublishWizard(InstallProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));

        Text = L("Publish_Title", "Publish");
        Size = new Size(700, 460);
        MinimumSize = new Size(620, 420);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimizeBox = false;
        MaximizeBox = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(20) };

        root.Controls.Add(Heading(L("Publish_Heading", "Publish a ClickOnce deployment")));
        root.Controls.Add(Explain(L("Publish_Explain",
            "Two of these decide whether your users ever receive an update, so they are asked rather than "
            + "assumed.")));

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _folderBox = AddRow(fields, L("Publish_Folder", "Publish to folder:"), "", browse: true);
        fields.Controls.Add(Hint(L("Publish_FolderHint",
            "Where the files are written now — a local folder, a share, or a staging directory.")), 1, NextRow(fields));

        _urlBox = AddRow(fields, L("Publish_UpdateUrl", "Clients check:"), _project.AppUpdatesURL, browse: false);
        fields.Controls.Add(Hint(L("Publish_UpdateUrlHint",
            "The address installed clients will check for updates — usually an https:// URL, not the folder "
            + "above. Leave empty for a deployment that never updates itself.")), 1, NextRow(fields));

        _signBox = new CheckBox
        {
            Text = L("Publish_Sign", "Sign the manifests"),
            AutoSize = true,
            Checked = _project.HasCodeSigningCertificate,
            Margin = new Padding(0, 8, 0, 0),
        };
        var signRow = NextRow(fields);
        fields.Controls.Add(_signBox, 1, signRow);

        _signalLabel = Hint("");
        fields.Controls.Add(_signalLabel, 1, NextRow(fields));

        _signBox.CheckedChanged += (_, _) => DescribeSigning();
        DescribeSigning();

        root.Controls.Add(fields);

        _validation = new Label { AutoSize = true, ForeColor = Color.FromArgb(168, 32, 32), Margin = new Padding(0, 12, 0, 0) };
        root.Controls.Add(_validation);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(20, 8, 20, 16),
        };
        var cancel = new Button { Text = L("Common_Cancel", "Cancel"), Width = 92, Height = 30, DialogResult = DialogResult.Cancel };
        var publish = new Button { Text = L("Publish_Go", "Publish"), Width = 92, Height = 30 };
        buttons.Controls.AddRange(new Control[] { cancel, publish });

        publish.Click += (_, _) =>
        {
            var problem = CurrentProblem();
            if (problem != null) { _validation.Text = problem; return; }
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(root);
        Controls.Add(buttons);
        CancelButton = cancel;
        AcceptButton = publish;

        Engine.Accessibility.Attach(this);
    }

    /// <summary>
    /// States the consequence rather than the setting. "Unsigned" means nothing to most authors;
    /// "your users will see a SmartScreen warning" is the thing they are actually deciding.
    /// </summary>
    private void DescribeSigning()
    {
        if (!_signBox.Checked)
        {
            _signalLabel.Text = L("Publish_UnsignedWarning",
                "Unsigned: Windows SmartScreen will warn users before this runs.");
            _signalLabel.ForeColor = Color.FromArgb(150, 90, 0);
            return;
        }

        if (!_project.HasCodeSigningCertificate)
        {
            _signalLabel.Text = L("Publish_NoCertificate",
                "No certificate is configured yet — set one in Code Signing, or this publishes unsigned anyway.");
            _signalLabel.ForeColor = Color.FromArgb(150, 90, 0);
            return;
        }

        _signalLabel.Text = L("Publish_WillSign", "Manifests will be signed with the configured certificate.");
        _signalLabel.ForeColor = Ui.InstallerTheme.MutedText;
    }

    private string? CurrentProblem() => Validate(CurrentChoice);

    /// <summary>Why this publish target cannot be used, or <c>null</c> if it can.</summary>
    public static string? Validate(Choice choice)
    {
        if (string.IsNullOrWhiteSpace(choice.Folder))
            return L("Publish_NeedFolder", "Choose a folder to publish into.");

        var folder = choice.Folder.Trim();
        var parent = Path.GetDirectoryName(Path.GetFullPath(folder));
        if (parent != null && !Directory.Exists(parent) && !Directory.Exists(folder))
            return L("Publish_FolderUnreachable", "That folder's parent does not exist.");

        var url = choice.UpdateUrl.Trim();
        if (!string.IsNullOrEmpty(url) && !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            // A relative update URL produces a manifest clients cannot resolve, and the failure only
            // shows up on someone else's machine at update time.
            return L("Publish_UrlNotAbsolute",
                "The update address must be absolute, such as https://example.com/app/ — clients resolve it "
                + "from their own machine, not from yours.");
        }

        return null;
    }

    // ── layout helpers ──────────────────────────────────────────────────────

    private Label Heading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
        Margin = new Padding(0, 0, 0, 4),
    };

    private static Label Explain(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(620, 0),
        ForeColor = Ui.InstallerTheme.MutedText,
    };

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(430, 0),
        ForeColor = Ui.InstallerTheme.MutedText,
        Margin = new Padding(0, 0, 0, 10),
    };

    private static int NextRow(TableLayoutPanel table)
    {
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return table.RowCount - 1;
    }

    private TextBox AddRow(TableLayoutPanel table, string caption, string? value, bool browse)
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

        if (browse)
        {
            var button = new Button { Text = L("Btn_Browse", "Browse…"), Width = 86, Height = 24, Margin = new Padding(6, 4, 0, 4) };
            button.Click += (_, _) =>
            {
                using var dialog = new FolderBrowserDialog { Description = caption.TrimEnd(':') };
                if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath;
            };
            table.Controls.Add(button, 2, row);
        }

        return box;
    }
}
