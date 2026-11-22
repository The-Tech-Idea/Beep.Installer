using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Engine;
using Beep.Installer.Ui;
using TheTechIdea.Beep.Winform.Controls;

namespace Beep.Installer.Pages;

public class CompletePage : UserControl, IInstallerPage
{
    private PictureBox _icon = null!;
    private Label _title = null!;
    private TextBox _message = null!;
    private CheckBox _launchCheck = null!;
    private CheckBox _openLogCheck = null!;
    private BeepButton _viewSupportBundleBtn = null!;
    private bool _success;
    private string _resultMessage = "";
    private string? _messageKey;
    private string[] _messageArguments = Array.Empty<string>();
    private RuntimeOperationOutcome? _outcome;
    private readonly RuntimePageLayout _layout;

    public string PageTitle => _success
        ? LanguageManager.GetOrDefault("Wizard_Complete", "Installation Complete")
        : LanguageManager.GetOrDefault("Runtime_Failed", "Installation Failed");
    public string Subtitle => _success
        ? LanguageManager.GetOrDefault("Complete_Success", "The application has been installed successfully.")
        : LanguageManager.GetOrDefault("Complete_Failure", "There was a problem during installation.");
    public bool CanGoNext => false;
    public event EventHandler<bool>? ValidityChanged
    {
        add { }
        remove { }
    }

    public bool LaunchApplication => _launchCheck.Checked;
    public bool OpenLog => _openLogCheck.Checked;
    public string? LogPath { get; private set; }
    public string? SupportBundlePath { get; private set; }

    public CompletePage()
    {
        _icon = new PictureBox
        {
            Location = new Point(0, 0),
            Size = new Size(48, 48),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        _title = new Label
        {
            Location = new Point(60, 5),
            Size = new Size(440, 30),
            AutoSize = true
        };
        _message = new TextBox
        {
            Name = "RuntimeCompletionSummary",
            AccessibleName = "Installation outcome",
            Location = new Point(0, 65),
            Size = new Size(500, 100),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = InstallerTheme.Panel, ForeColor = InstallerTheme.Text
        };
        _launchCheck = new CheckBox
        {
            Text = LanguageManager.GetOrDefault("Complete_Launch", "Launch the application"),
            Location = new Point(0, 180),
            Size = new Size(500, 24),
            Checked = true
        };
        _openLogCheck = new CheckBox
        {
            Text = LanguageManager.GetOrDefault("Complete_ViewLog", "View installation log"),
            Location = new Point(0, 210),
            Size = new Size(500, 24),
            Checked = false
        };
        _viewSupportBundleBtn = new BeepButton
        {
            Text = "View Support Bundle",
            Location = new Point(0, 248),
            Size = new Size(170, 32),
            Visible = false
        };
        _viewSupportBundleBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(SupportBundlePath) && File.Exists(SupportBundlePath))
            {
                using var dialog = new Forms.RuntimeSupportBundleDialog(SupportBundlePath); dialog.ShowDialog(this);
            }
            else
                MessageBox.Show(this, LanguageManager.GetOrDefault("Complete_NoBundle", "No support bundle was generated."), LanguageManager.GetOrDefault("Complete_BundleTitle", "Support Bundle"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        Controls.AddRange(new Control[] { _icon, _title, _message, _launchCheck, _openLogCheck, _viewSupportBundleBtn });
        _layout = new RuntimePageLayout(this, _title, _message, _icon, true, _launchCheck, _openLogCheck, _viewSupportBundleBtn);
        ReloadStrings();
    }

    public void OnEnter(InstallContext ctx) { }

    public void SetResult(bool success, string message, string? logPath = null, string? supportBundlePath = null,
        RuntimeOperationOutcome? outcome = null)
    {
        _success = success;
        LogPath = logPath;
        SupportBundlePath = supportBundlePath;
        _resultMessage = message;
        _messageKey = null;
        _messageArguments = Array.Empty<string>();
        _outcome = outcome;
        ReloadStrings();

        var oldIcon = _icon.Image;
        _icon.Image = LoadStatusIcon(success);
        oldIcon?.Dispose();

        _launchCheck.Visible = success;
        _openLogCheck.Visible = !string.IsNullOrEmpty(logPath) && File.Exists(logPath);
        if (_openLogCheck.Visible) _openLogCheck.Tag = logPath;
        _viewSupportBundleBtn.Visible = !string.IsNullOrWhiteSpace(supportBundlePath) && File.Exists(supportBundlePath);
        _layout.Refresh();
    }

    public void ReloadStrings()
    {
        _title.Text = PageTitle;
        var message = _messageKey is null ? _resultMessage : string.Format(LanguageManager.GetString(_messageKey), _messageArguments.Cast<object>().ToArray());
        _message.Text = message + (_outcome is null ? "" : Environment.NewLine + Environment.NewLine + RuntimeOperationPresentation.Describe(_outcome));
        _message.AccessibleName = PageTitle;
        _launchCheck.Text = LanguageManager.GetOrDefault("Complete_Launch", "Launch the application");
        _openLogCheck.Text = LanguageManager.GetOrDefault("Complete_ViewLog", "View installation log");
        _viewSupportBundleBtn.Text = LanguageManager.GetOrDefault("Support_Review", "Review support bundle");
        _launchCheck.AccessibleName = _launchCheck.Text; _openLogCheck.AccessibleName = _openLogCheck.Text;
        _viewSupportBundleBtn.AccessibleName = _viewSupportBundleBtn.Text;
        _layout?.Refresh();
    }

    internal void SetLocalizedMessage(string key, params string[] arguments)
    {
        _messageKey = key;
        _messageArguments = arguments.ToArray();
        ReloadStrings();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _layout?.Dispose(); _icon.Image?.Dispose(); _icon.Image = null; }
        base.Dispose(disposing);
    }

    private static Image? LoadStatusIcon(bool success)
    {
        try
        {
            var asm = typeof(CompletePage).Assembly;
            var name = success ? "circle-check" : "circle-x";
            using var stream = asm.GetManifestResourceStream($"Beep.Installer.Resources.Icons.{name}.svg");
            if (stream == null) return null;
            var svg = Svg.SvgDocument.Open<Svg.SvgDocument>(stream);
            var bmp = new Bitmap(48, 48);
            svg.Draw(bmp);
            return bmp;
        }
        catch { return null; }
    }

    public new bool Validate() => true;
}
