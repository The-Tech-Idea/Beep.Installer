using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TheTechIdea.Beep.Winform.Controls;

namespace Beep.Installer.Pages;

public class ErrorPage : UserControl, IInstallerPage
{
    private PictureBox _icon = null!;
    private BeepLabel _title = null!;
    private TextBox _details = null!;
    private BeepButton _viewLogBtn = null!;
    private BeepButton _viewSupportBundleBtn = null!;
    private BeepButton _retryBtn = null!;
    private BeepLabel _hint = null!;

    private string? _logPath;
    private string? _supportBundlePath;
    private bool _retryRequested;

    public string PageTitle => "Installation Failed";
    public string Subtitle => "An error occurred during installation.";
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
        _title = new BeepLabel
        {
            Location = new Point(60, 8),
            Size = new Size(440, 30),
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            Text = "Installation Failed"
        };
        _details = new TextBox
        {
            Location = new Point(0, 65),
            Size = new Size(500, 184),
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(255, 245, 245),
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
                MessageBox.Show(this, "No log file was generated.", "View Log",
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
                Process.Start("notepad.exe", _supportBundlePath);
            else
                MessageBox.Show(this, "No support bundle was generated.", "Support Bundle",
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
        _hint = new BeepLabel
        {
            Location = new Point(0, 296),
            Size = new Size(500, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.Gray,
            Text = "Check the log for details, or click Retry to attempt the installation again."
        };

        Controls.AddRange(new Control[] { _icon, _title, _details, _viewLogBtn, _viewSupportBundleBtn, _retryBtn, _hint });

        LoadErrorIcon();
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

    public void SetError(string errorMessage, string? logPath = null, string? supportBundlePath = null)
    {
        _details.Text = string.IsNullOrWhiteSpace(errorMessage)
            ? "An unknown error occurred during installation. Please check the log for details."
            : errorMessage;
        _logPath = logPath;
        _supportBundlePath = supportBundlePath;
        _viewLogBtn.Visible = !string.IsNullOrEmpty(logPath) && File.Exists(logPath);
        _viewSupportBundleBtn.Visible = !string.IsNullOrEmpty(supportBundlePath) && File.Exists(supportBundlePath);
        _retryRequested = false;
    }

    public new bool Validate() => true;
}
