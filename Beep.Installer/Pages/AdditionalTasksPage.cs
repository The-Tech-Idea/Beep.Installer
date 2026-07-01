using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Lang;

namespace Beep.Installer.Pages;

public class AdditionalTasksPage : UserControl, IInstallerPage
{
    private CheckBox _desktopIcon = null!;
    private CheckBox _autoStart = null!;
    private CheckBox _fileAssoc = null!;

    public string PageTitle => LanguageManager.GetOrDefault("Wizard_Tasks", "Additional Tasks");
    public string Subtitle => "Select additional setup tasks.";
    public bool CanGoNext => true;
    public event EventHandler<bool>? ValidityChanged;

    public bool CreateDesktopIcon => _desktopIcon.Checked;
    public bool AutoStart => _autoStart.Checked;
    public bool FileAssociations => _fileAssoc.Checked;

    public AdditionalTasksPage()
    {
        _desktopIcon = new CheckBox
        {
            Text = LanguageManager.GetOrDefault("Task_DesktopIcon", "Create a desktop icon"),
            Location = new Point(0, 0), Size = new Size(500, 28), Checked = true
        };
        _autoStart = new CheckBox
        {
            Text = LanguageManager.GetOrDefault("Task_AutoStart", "Start the application when Windows starts"),
            Location = new Point(0, 35), Size = new Size(500, 28), Checked = false
        };
        _fileAssoc = new CheckBox
        {
            Text = LanguageManager.GetOrDefault("Task_FileAssoc", "Associate project files with the application"),
            Location = new Point(0, 70), Size = new Size(500, 28), Checked = true
        };

        foreach (var cb in new[] { _desktopIcon, _autoStart, _fileAssoc })
            cb.CheckedChanged += (_, _) => ValidityChanged?.Invoke(this, true);

        Controls.AddRange(new Control[] { _desktopIcon, _autoStart, _fileAssoc });
    }

    public void OnEnter(InstallContext ctx)
    {
        _desktopIcon.Checked = ctx.CreateDesktopIcon;
        _autoStart.Checked = ctx.AutoStart;
    }

    public new bool Validate() => true;
}
