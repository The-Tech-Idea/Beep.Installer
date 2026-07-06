using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Lang;
using TheTechIdea.Beep.Winform.Controls;

namespace Beep.Installer.Pages;

public class StartMenuPage : UserControl, IInstallerPage
{
    private BeepLabel _title = null!;
    private BeepLabel _prompt = null!;
    private TextBox _folderBox = null!;
    private CheckBox _skipCheck = null!;
    private InstallContext? _ctx;

    public string PageTitle => LanguageManager.GetOrDefault("Wizard_StartMenu", "Start Menu Folder");
    public string Subtitle => "Choose a Start Menu folder for shortcuts.";
    public bool CanGoNext => true;
    public event EventHandler<bool>? ValidityChanged;
    public bool CreateStartMenu => !_skipCheck.Checked;
    public string FolderName => _folderBox.Text;

    public StartMenuPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(32, 28, 32, 12);

        _title = new BeepLabel
        {
            Text = "Start Menu Folder",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            Location = new Point(32, 24),
            AutoSize = true
        };

        _prompt = new BeepLabel
        {
            Text = "Select the Start Menu folder where shortcuts will be created:",
            Location = new Point(32, 64),
            Size = new Size(620, 40),
            Font = new Font("Segoe UI", 10)
        };

        _folderBox = new TextBox
        {
            Location = new Point(32, 116),
            Size = new Size(620, 30),
            Font = new Font("Segoe UI", 10),
            Text = "Application",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _skipCheck = new CheckBox
        {
            Text = LanguageManager.GetOrDefault("StartMenu_Skip", "Don't create a Start Menu folder"),
            Location = new Point(32, 158),
            Size = new Size(620, 28),
            Font = new Font("Segoe UI", 10)
        };
        _skipCheck.CheckedChanged += (_, _) =>
        {
            if (_ctx != null) _ctx.CreateStartMenu = !_skipCheck.Checked;
            if (_ctx != null) _ctx.StartMenuFolder = _folderBox.Text;
            _folderBox.Enabled = !_skipCheck.Checked;
            ValidityChanged?.Invoke(this, CanGoNext);
        };
        _folderBox.TextChanged += (_, _) =>
        {
            if (_ctx != null) _ctx.StartMenuFolder = _folderBox.Text;
            ValidityChanged?.Invoke(this, CanGoNext);
        };
        Controls.AddRange(new Control[] { _title, _prompt, _folderBox, _skipCheck });
    }

    public void OnEnter(InstallContext ctx)
    {
        _ctx = ctx;
        _skipCheck.Checked = !ctx.CreateStartMenu;
        _prompt.Text = LanguageManager.GetOrDefault("StartMenu_Prompt", "Select the Start Menu folder where shortcuts will be created:");
        var project = ctx.Project;
        if (!string.IsNullOrEmpty(project.DefaultGroupName)) _folderBox.Text = project.DefaultGroupName;
        ctx.StartMenuFolder = _folderBox.Text;
        _folderBox.Enabled = !_skipCheck.Checked;
    }

    public new bool Validate() => true;
}
