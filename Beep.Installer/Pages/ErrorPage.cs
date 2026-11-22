using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TheTechIdea.Beep.Winform.Controls;
using Beep.Installer.Engine;
using Beep.Installer.Ui;
using static Beep.Installer.Lang.UiStrings;

namespace Beep.Installer.Pages;

public class ErrorPage : UserControl, IInstallerPage
{
    private PictureBox _icon = null!;
    private Label _title = null!;
    private TextBox _details = null!;
    private BeepButton _viewLogBtn = null!;
    private BeepButton _viewSupportBundleBtn = null!;
    private BeepButton _retryBtn = null!;
    private Label _hint = null!;
    private readonly RuntimePageLayout _layout;
    private readonly BeepButton _copyDetails = new() { Name = "RuntimeCopyErrorDetails" };
    private bool _copyFailed;
    private bool _copied;
    internal Action<string> SupportDetailsCopy = Clipboard.SetText;

    private string? _logPath;
    private string? _supportBundlePath;
    private bool _retryRequested;
    private string _errorMessage = "";
    private RuntimeOperationOutcome? _outcome;

    public string PageTitle => L("Runtime_Failed", "Installation Failed");
    public string Subtitle => L("Complete_Failure", "There was a problem during installation.");
    public bool CanGoNext => false;
    public event EventHandler<bool>? ValidityChanged;
    public bool RetryRequested => _retryRequested;
    public string? LogPath => _logPath;

    public ErrorPage()
    {
        _icon = new PictureBox
        {
            Location = new Point(0, 0),
            Size = new Size(48, 48),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        _title = new Label
        {
            Location = new Point(60, 8),
            Size = new Size(440, 30),
            AutoSize = true,
            Text = "Installation Failed"
        };
        _details = new TextBox
        {
            Name = "RuntimeFailureSummary",
            Location = new Point(0, 65),
            Size = new Size(500, 184),
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = InstallerTheme.Panel, ForeColor = InstallerTheme.Text,
            ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        _viewLogBtn = new BeepButton
        {
            Text = "View Installation Log",
            Location = new Point(0, 260),
            Size = new Size(160, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Visible = false
        };
        _viewLogBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_logPath) && File.Exists(_logPath))
                Process.Start("notepad.exe", _logPath);
            else
                MessageBox.Show(this, L("Error_NoLog", "No log file was generated."), L("Error_ViewLogTitle", "View Log"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        _viewSupportBundleBtn = new BeepButton
        {
            Text = "View Support Bundle",
            Location = new Point(170, 260),
            Size = new Size(160, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Visible = false
        };
        _viewSupportBundleBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_supportBundlePath) && File.Exists(_supportBundlePath))
            {
                using var dialog = new Forms.RuntimeSupportBundleDialog(_supportBundlePath); dialog.ShowDialog(this);
            }
            else
                MessageBox.Show(this, L("Complete_NoBundle", "No support bundle was generated."), L("Complete_BundleTitle", "Support Bundle"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        _retryBtn = new BeepButton
        {
            Text = "Retry Installation",
            Location = new Point(340, 260),
            Size = new Size(160, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _retryBtn.Click += (_, _) =>
        {
            _retryRequested = true;
            ValidityChanged?.Invoke(this, true);
        };
        _hint = new Label
        {
            Location = new Point(0, 296),
            Size = new Size(500, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Ui.InstallerTheme.MutedText,
            Text = "Check the log for details, or click Retry to attempt the installation again."
        };

        Controls.AddRange(new Control[] { _icon, _title, _details, _viewLogBtn, _viewSupportBundleBtn, _retryBtn, _hint });
        _layout = new RuntimePageLayout(this, _title, _details, _icon, false, _copyDetails, _viewLogBtn, _viewSupportBundleBtn, _retryBtn, _hint);
        _copyDetails.Click += (_, _) => CopySupportDetails(SupportDetailsCopy);

        LoadErrorIcon();
        ReloadStrings();
    }

    private void LoadErrorIcon()
    {
        try
        {
            var asm = GetType().Assembly;
            using var stream = asm.GetManifestResourceStream("Beep.Installer.Resources.Icons.alert-triangle.svg");
            if (stream != null)
            {
                var svg = Svg.SvgDocument.Open<Svg.SvgDocument>(stream);
                var bmp = new Bitmap(48, 48);
                svg.Draw(bmp);
                _icon.Image = bmp;
            }
        }
        catch (Exception ex)
        {
            // Decorative status icon — absence must not block the error page.
            Engine.Diag.Debug("ErrorPage", "status icon could not be rendered", ex);
        }
    }

    public void OnEnter(InstallContext ctx) { }

    public void SetError(string errorMessage, string? logPath = null, string? supportBundlePath = null,
        RuntimeOperationOutcome? outcome = null)
    {
        _errorMessage = RuntimeRedactor.Redact(errorMessage);
        _outcome = outcome;
        ReloadStrings();
        _logPath = logPath;
        _supportBundlePath = supportBundlePath;
        _viewLogBtn.Visible = !string.IsNullOrEmpty(logPath) && File.Exists(logPath);
        _viewSupportBundleBtn.Visible = !string.IsNullOrEmpty(supportBundlePath) && File.Exists(supportBundlePath);
        _retryRequested = false;
        _copied = _copyFailed = false;
        ReloadStrings();
        _layout.Refresh();
    }

    public void ReloadStrings()
    {
        _title.Text = PageTitle;
        _details.AccessibleName = PageTitle;
        _details.Text = (string.IsNullOrWhiteSpace(_errorMessage) ? Subtitle : _errorMessage)
            + Environment.NewLine + Environment.NewLine
            + RuntimeOperationPresentation.Describe(_outcome ?? new RuntimeOperationOutcome(false, false));
        _viewLogBtn.Text = L("Complete_ViewLog", "View installation log");
        _viewSupportBundleBtn.Text = L("Support_Review", "Review support bundle");
        _copyDetails.Text = _copyDetails.AccessibleName = L("Support_CopyDetails", "Copy error details");
        _retryBtn.Text = L("Runtime_Retry", "Retry Installation");
        _hint.Text = _copyFailed ? L("Support_CopyFailed", "Error details could not be copied. Retry or select the summary text.")
            : _copied ? L("Support_Copied", "Redacted error details copied. Review them before sharing.")
            : L("Runtime_RecoveryUnknown", "Recovery has not been verified. Review the log and installation state before retrying.");
        foreach (var action in new Control[] { _viewLogBtn, _viewSupportBundleBtn, _retryBtn }) action.AccessibleName = action.Text;
        _layout?.Refresh();
    }

    internal string GetSupportDetails() => RuntimeRedactor.Redact(PageTitle + Environment.NewLine + Environment.NewLine + _details.Text);

    internal bool CopySupportDetails(Action<string> copy)
    {
        try { copy(GetSupportDetails()); _copied = true; _copyFailed = false; ReloadStrings(); return true; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or System.Threading.ThreadStateException)
        { _copyFailed = true; _copied = false; ReloadStrings(); return false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _layout?.Dispose(); _icon.Image?.Dispose(); _icon.Image = null; }
        base.Dispose(disposing);
    }

    public new bool Validate() => true;
}
