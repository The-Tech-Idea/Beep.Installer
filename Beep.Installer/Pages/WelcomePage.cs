using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Lang;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Pages;

public class WelcomePage : UserControl, IInstallerPage
{
    private Label _title = null!;
    private Label _versionLabel = null!;
    private Label _description = null!;

    public string PageTitle => LanguageManager.GetOrDefault("Wizard_Welcome", "Welcome");
    public string Subtitle => "Setup will install this application on your computer.";
    public bool CanGoNext => true;
    public event EventHandler<bool>? ValidityChanged
    {
        add { /* no-op */ }
        remove { /* no-op */ }
    }

    public WelcomePage()
    {
        _title = new Label { Font = new Font("Segoe UI", 18, FontStyle.Bold), Location = new Point(0, 0), AutoSize = true };
        _versionLabel = new Label { Font = new Font("Segoe UI", 10), Location = new Point(0, 44), ForeColor = Color.Gray, AutoSize = true };
        _description = new Label { Location = new Point(0, 80), Size = new Size(500, 200), AutoSize = false };
        Controls.AddRange(new Control[] { _title, _versionLabel, _description });
    }

    public void OnEnter(InstallContext ctx)
    {
        var product = ctx.Config.ProductName;
        var version = ctx.Config.ProductVersion;

        _title.Text = $"{PageTitle} \u2014 {product}";
        _versionLabel.Text = $"Version {version}";

        var subtitle = LanguageManager.GetOrDefault("Welcome_Subtitle", "Setup will install {0} on your computer.");
        var body = LanguageManager.GetOrDefault("Welcome_Description",
            "This wizard will guide you through the installation.\r\n\r\nClick Next to continue.");

        _description.Text = string.Format(subtitle, product) + "\r\n\r\n" + body;
    }

    public new bool Validate() => true;
}
