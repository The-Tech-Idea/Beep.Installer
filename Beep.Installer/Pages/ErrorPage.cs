using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Beep.Installer.Pages;

/// <summary>
/// Dedicated error page shown when installation fails.
/// Shows the error message, a "View Log" button, and a "Retry" option.
/// </summary>
public class ErrorPage : UserControl, IInstallerPage
{
    private Label _icon = null!;
    private Label _title = null!;
    private TextBox _details = null!;
    private Button _viewLogBtn = null!;
    private Button _retryBtn = null!;
    private Label _hint = null!;

    private string? _logPath;
    private bool _retryRequested;

    public string PageTitle => "Installation Failed";
    public string Subtitle => "An error occurred during installation.";
    public bool CanGoNext => false;
    public event EventHandler<bool>? ValidityChanged;
    public bool RetryRequested => _retryRequested;
    public string? LogPath => _logPath;

    public ErrorPage()
    {
        _icon = new Label
        {
            Location = new Point(0, 0),
            Size = new Size(48, 48),
            Font = new Font("Segoe UI", 36),
            ForeColor = Color.Red,
            Text = "\u2717"
        };
        _title = new Label
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
        _viewLogBtn = new Button
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
        _retryBtn = new Button
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
            ForeColor = Color.Gray,
            Text = "Check the log for details, or click Retry to attempt the installation again."
        };

        Controls.AddRange(new Control[] { _icon, _title, _details, _viewLogBtn, _retryBtn, _hint });
    }

    public void OnEnter(InstallContext ctx) { }

    public void SetError(string errorMessage, string? logPath = null)
    {
        _details.Text = string.IsNullOrWhiteSpace(errorMessage)
            ? "An unknown error occurred during installation. Please check the log for details."
            : errorMessage;
        _logPath = logPath;
        _viewLogBtn.Visible = !string.IsNullOrEmpty(logPath) && File.Exists(logPath);
        _retryRequested = false;
    }

    public new bool Validate() => true;
}
