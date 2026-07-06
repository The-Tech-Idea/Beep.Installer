using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Lang;
using TheTechIdea.Beep.Winform.Controls;

namespace Beep.Installer.Pages;

public class LicensePage : UserControl, IInstallerPage
{
    private BeepLabel _title = null!;
    private BeepLabel _prompt = null!;
    private RichTextBox _licenseText = null!;
    private CheckBox _acceptCheck = null!;
    private InstallContext? _ctx;

    public string PageTitle => LanguageManager.GetOrDefault("Wizard_License", "License Agreement");
    public string Subtitle => "Please review the license terms before installing.";
    public bool CanGoNext => _acceptCheck.Checked;
    public event EventHandler<bool>? ValidityChanged;
    public bool Accepted => _acceptCheck.Checked;

    public LicensePage()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(32, 28, 32, 12);

        _title = new BeepLabel
        {
            Text = "License Agreement",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            Location = new Point(32, 24),
            AutoSize = true
        };

        _licenseText = new RichTextBox
        {
            Location = new Point(32, 64),
            Size = new Size(620, 280),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9)
        };

        _prompt = new BeepLabel
        {
            Text = "Do you accept the terms of this license agreement?",
            Location = new Point(32, 358),
            Size = new Size(620, 20),
            AutoSize = true,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };

        _acceptCheck = new CheckBox
        {
            Text = LanguageManager.GetOrDefault("License_Accept", "I accept the agreement"),
            Location = new Point(32, 388),
            Size = new Size(500, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Checked = false,
            Font = new Font("Segoe UI", 10)
        };

        _acceptCheck.CheckedChanged += (_, _) => ValidityChanged?.Invoke(this, _acceptCheck.Checked);

        Controls.AddRange(new Control[] { _title, _licenseText, _prompt, _acceptCheck });
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        _prompt.Text = LanguageManager.GetOrDefault("License_Prompt", "Do you accept the terms of this license agreement?");
        _acceptCheck.Text = LanguageManager.GetOrDefault("License_Accept", "I accept the agreement");
    }

    public void OnEnter(InstallContext ctx)
    {
        _ctx = ctx;
        ApplyLanguage();
        _acceptCheck.DataBindings.Clear();
        _acceptCheck.DataBindings.Add("Checked", ctx, nameof(InstallContext.AcceptLicense),
            formattingEnabled: true, DataSourceUpdateMode.OnPropertyChanged);
        _licenseText.Text = !string.IsNullOrEmpty(ctx.Project.LicenseText)
            ? ctx.Project.LicenseText
            : "No license text provided. By proceeding, you accept the default terms.";
        ctx.AcceptLicense = false;
    }

    public new bool Validate() => _acceptCheck.Checked;
}
