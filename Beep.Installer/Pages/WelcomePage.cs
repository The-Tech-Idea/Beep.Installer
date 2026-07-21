using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Lang;
using TheTechIdea.Beep.Winform.Controls;

namespace Beep.Installer.Pages;

public class WelcomePage : UserControl, IInstallerPage
{
    private BeepLabel _title = null!;
    private BeepLabel _versionLabel = null!;
    private BeepLabel _description = null!;
    private PictureBox _icon = null!;
    private InstallContext? _ctx;

    public string PageTitle => LanguageManager.GetOrDefault("Wizard_Welcome", "Welcome");
    public string Subtitle => "Setup will install this application on your computer.";
    public bool CanGoNext => true;
    public event EventHandler<bool>? ValidityChanged
    {
        add { }
        remove { }
    }

    public WelcomePage()
    {
        _icon = new PictureBox
        {
            Location = new Point(0, 0),
            Size = new Size(48, 48),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        _title = new BeepLabel { Font = new Font("Segoe UI", 18, FontStyle.Bold), Location = new Point(60, 0), AutoSize = true };
        _versionLabel = new BeepLabel { Font = new Font("Segoe UI", 10), Location = new Point(60, 36), ForeColor = Color.Gray, AutoSize = true };
        _description = new BeepLabel { Location = new Point(0, 80), Size = new Size(500, 200), AutoSize = false };
        Controls.AddRange(new Control[] { _icon, _title, _versionLabel, _description });
    }

    public void OnEnter(InstallContext ctx)
    {
        _ctx = ctx;
        // Direct WinForms data binding: version label binds to project's AppVersion.
        // Title and description compose with AppName — set once on enter.
        foreach (var b in _versionLabel.DataBindings.Cast<Binding>().ToList()) b.ReadValue();
        _versionLabel.DataBindings.Clear();
        _versionLabel.DataBindings.Add("Text", ctx.Project, nameof(Models.InstallProject.AppVersion),
            formattingEnabled: true, DataSourceUpdateMode.OnPropertyChanged);
        _title.Text = $"{PageTitle} — {ctx.Project.AppName}";

        var subtitle = LanguageManager.GetOrDefault("Welcome_Subtitle", "Setup will install {0} on your computer.");
        var body = LanguageManager.GetOrDefault("Welcome_Description",
            "This wizard will guide you through the installation.\r\n\r\nClick Next to continue.");

        _description.Text = string.Format(subtitle, ctx.Project.AppName) + "\r\n\r\n" + body;

        LoadProductIcon();
    }

    private void LoadProductIcon()
    {
        try
        {
            var asm = GetType().Assembly;
            using var stream = asm.GetManifestResourceStream("Beep.Installer.Resources.Icons.package.svg");
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
            // Decorative product icon — absence must not block the wizard.
            Engine.Diag.Debug("WelcomePage", "product icon could not be rendered", ex);
        }
    }

    public new bool Validate() => true;
}
