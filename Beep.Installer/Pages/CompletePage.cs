using System;
using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Lang;
using TheTechIdea.Beep.Winform.Controls;

namespace Beep.Installer.Pages;

public class CompletePage : UserControl, IInstallerPage
{
    private PictureBox _icon = null!;
    private BeepLabel _title = null!;
    private BeepLabel _message = null!;
    private CheckBox _launchCheck = null!;
    private CheckBox _openLogCheck = null!;
    private bool _success;

    public string PageTitle => _success
        ? LanguageManager.GetOrDefault("Wizard_Complete", "Installation Complete")
        : "Installation Failed";
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

    public CompletePage()
    {
        _icon = new PictureBox
        {
            Location = new Point(0, 0),
            Size = new Size(48, 48),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        _title = new BeepLabel
        {
            Location = new Point(60, 5),
            Size = new Size(440, 30),
            Font = new Font("Segoe UI", 14, FontStyle.Bold)
        };
        _message = new BeepLabel
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
        _title.Text = success ? "Installation Complete" : "Installation Failed";
        _message.Text = message;

        _icon.Image = LoadStatusIcon(success);

        _launchCheck.Visible = success;
        _openLogCheck.Visible = !string.IsNullOrEmpty(logPath);
        if (_openLogCheck.Visible) _openLogCheck.Tag = logPath;
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
