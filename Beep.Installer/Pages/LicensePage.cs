using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Lang;

namespace Beep.Installer.Pages;

public class LicensePage : UserControl, IInstallerPage
{
    private RichTextBox _licenseText = null!;
    private RadioButton _accept = null!;
    private RadioButton _decline = null!;
    private Label _prompt = null!;

    public string PageTitle => LanguageManager.GetOrDefault("Wizard_License", "License Agreement");
    public string Subtitle => "Please review the license terms before installing.";
    public bool CanGoNext => _accept.Checked;
    public event EventHandler<bool>? ValidityChanged;
    public bool Accepted => _accept.Checked;

    public LicensePage()
    {
        _licenseText = new RichTextBox
        {
            Location = new Point(0, 0),
            Size = new Size(500, 280),
            ReadOnly = true,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        _prompt = new Label
        {
            Location = new Point(0, 290),
            Size = new Size(500, 20),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _accept = new RadioButton
        {
            Location = new Point(10, 315),
            Size = new Size(400, 24),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _decline = new RadioButton
        {
            Location = new Point(10, 342),
            Size = new Size(400, 24),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Checked = true
        };

        _accept.CheckedChanged += (_, _) => ValidityChanged?.Invoke(this, _accept.Checked);
        _decline.CheckedChanged += (_, _) => ValidityChanged?.Invoke(this, _accept.Checked);

        Controls.AddRange(new Control[] { _licenseText, _prompt, _accept, _decline });
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        _prompt.Text = LanguageManager.GetOrDefault("License_Prompt", "Do you accept the terms of this license agreement?");
        _accept.Text = LanguageManager.GetOrDefault("License_Accept", "I accept the agreement");
        _decline.Text = LanguageManager.GetOrDefault("License_Decline", "I do not accept the agreement");
    }

    public void OnEnter(InstallContext ctx)
    {
        ApplyLanguage();
        _licenseText.Text = !string.IsNullOrEmpty(ctx.Config.LicenseText)
            ? ctx.Config.LicenseText
            : "No license text provided. By proceeding, you accept the default terms.";
    }

    public new bool Validate() => _accept.Checked;
}
