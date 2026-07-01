using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Lang;

namespace Beep.Installer.Pages;

public class StartMenuPage : UserControl, IInstallerPage
{
    private Label _prompt = null!;
    private TextBox _folderBox = null!;
    private CheckBox _skipCheck = null!;

    public string PageTitle => LanguageManager.GetOrDefault("Wizard_StartMenu", "Start Menu Folder");
    public string Subtitle => "Choose a Start Menu folder for shortcuts.";
    public bool CanGoNext => true;
    public event EventHandler<bool>? ValidityChanged;
    public bool CreateStartMenu => !_skipCheck.Checked;
    public string FolderName => _folderBox.Text;

    public StartMenuPage()
    {
        _prompt = new Label { Location = new Point(0, 0), Size = new Size(500, 20), AutoSize = true };
        _folderBox = new TextBox { Location = new Point(0, 30), Size = new Size(400, 24), Text = "Application" };
        _skipCheck = new CheckBox
        {
            Text = LanguageManager.GetOrDefault("StartMenu_Skip", "Don't create a Start Menu folder"),
            Location = new Point(0, 70),
            Size = new Size(500, 24)
        };
        _skipCheck.CheckedChanged += (_, _) =>
        {
            _folderBox.Enabled = !_skipCheck.Checked;
            ValidityChanged?.Invoke(this, CanGoNext);
        };
        Controls.AddRange(new Control[] { _prompt, _folderBox, _skipCheck });
    }

    public void OnEnter(InstallContext ctx)
    {
        _prompt.Text = LanguageManager.GetOrDefault("StartMenu_Prompt", "Select the Start Menu folder where shortcuts will be created:");
        if (!string.IsNullOrEmpty(ctx.Config.StartMenuFolder)) _folderBox.Text = ctx.Config.StartMenuFolder;
    }

    public new bool Validate() => true;
}
