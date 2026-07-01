using System;
using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Lang;

namespace Beep.Installer.Pages;

public class CompletePage : UserControl, IInstallerPage
{
    private Label _icon = null!;
    private Label _title = null!;
    private Label _message = null!;
    private CheckBox _launchCheck = null!;
    private CheckBox _openLogCheck = null!;
    private bool _success;

    public string PageTitle => _success
        ? LanguageManager.GetOrDefault("Wizard_Complete", "Installation Complete")
        : "Installation Failed";
    public string Subtitle => _success
        ? LanguageManager.GetOrDefault("Complete_Success", "The application has been installed successfully.")
        : LanguageManager.GetOrDefault("Complete_Failure", "There was a problem during installation.");
    public bool CanGoNext => false; // Last page
    public event EventHandler<bool>? ValidityChanged
    {
        add { }
        remove { }
    }

    public bool LaunchApplication => _launchCheck.Checked;
    public bool OpenLog => _openLogCheck.Checked;
    public string? LogPath { get; private set; }

    public CompletePage()
    {
        _icon = new Label
        {
            Location = new Point(0, 0),
            Size = new Size(48, 48),
            Font = new Font("Segoe UI", 36),
            Text = "\u2713"
        };
        _title = new Label
        {
            Location = new Point(60, 5),
            Size = new Size(440, 30),
            Font = new Font("Segoe UI", 14, FontStyle.Bold)
        };
        _message = new Label
        {
            Location = new Point(0, 65),
            Size = new Size(500, 100),
            AutoSize = false
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

        Controls.AddRange(new Control[] { _icon, _title, _message, _launchCheck, _openLogCheck });
    }

    public void OnEnter(InstallContext ctx) { }

    public void SetResult(bool success, string message, string? logPath = null)
    {
        _success = success;
        LogPath = logPath;
        _icon.Text = success ? "\u2713" : "\u2717";
        _icon.ForeColor = success ? Color.Green : Color.Red;
        _title.Text = success ? "Installation Complete" : "Installation Failed";
        _message.Text = message;

        _launchCheck.Visible = success;
        _openLogCheck.Visible = !string.IsNullOrEmpty(logPath);
        if (_openLogCheck.Visible) _openLogCheck.Tag = logPath;
    }

    public new bool Validate() => true;
}
