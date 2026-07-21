using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Beep.Installer.Lang;
using TheTechIdea.Beep.Winform.Controls;

namespace Beep.Installer.Pages;

public class AdditionalTasksPage : UserControl, IInstallerPage
{
    private BeepLabel _title = null!;
    private BeepLabel _prompt = null!;
    private CheckBox _desktopIcon = null!;
    private CheckBox _autoStart = null!;
    private CheckBox _fileAssoc = null!;
    private InstallContext? _ctx;

    public string PageTitle => LanguageManager.GetOrDefault("Wizard_Tasks", "Additional Tasks");
    public string Subtitle => "Select additional setup tasks.";
    public bool CanGoNext => true;
    public event EventHandler<bool>? ValidityChanged;

    public bool CreateDesktopIcon => _desktopIcon.Checked;
    public bool AutoStart => _autoStart.Checked;
    public bool FileAssociations => _fileAssoc.Checked;

    public AdditionalTasksPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(32, 28, 32, 12);

        _title = new BeepLabel
        {
            Text = LanguageManager.GetOrDefault("Wizard_Tasks", "Additional Tasks"),
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            Location = new Point(32, 24),
            AutoSize = true
        };

        _prompt = new BeepLabel
        {
            Text = "Select the additional tasks you would like Setup to perform:",
            Location = new Point(32, 64),
            Size = new Size(620, 40),
            Font = new Font("Segoe UI", 10)
        };

        _desktopIcon = new CheckBox
        {
            Text = LanguageManager.GetOrDefault("Task_DesktopIcon", "Create a desktop icon"),
            Location = new Point(32, 124), Size = new Size(620, 32), Checked = true,
            Font = new Font("Segoe UI", 10)
        };

        _autoStart = new CheckBox
        {
            Text = LanguageManager.GetOrDefault("Task_AutoStart", "Start the application when Windows starts"),
            Location = new Point(32, 162), Size = new Size(620, 32), Checked = false,
            Font = new Font("Segoe UI", 10)
        };

        _fileAssoc = new CheckBox
        {
            Text = LanguageManager.GetOrDefault("Task_FileAssoc", "Associate project files with the application"),
            Location = new Point(32, 200), Size = new Size(620, 32), Checked = true,
            Font = new Font("Segoe UI", 10)
        };

        foreach (var cb in new[] { _desktopIcon, _autoStart, _fileAssoc })
            cb.CheckedChanged += (_, _) => ValidityChanged?.Invoke(this, true);

        Controls.AddRange(new Control[] { _title, _prompt, _desktopIcon, _autoStart, _fileAssoc });
    }

    public void OnEnter(InstallContext ctx)
    {
        _ctx = ctx;
        BindCheckBox(_desktopIcon, ctx, nameof(InstallContext.CreateDesktopIcon));
        BindCheckBox(_autoStart, ctx, nameof(InstallContext.AutoStart));
        BindCheckBox(_fileAssoc, ctx, nameof(InstallContext.FileAssociations));
    }

    public new bool Validate() => true;

    private static void BindCheckBox(CheckBox checkBox, InstallContext ctx, string propertyName)
    {
        checkBox.DataBindings.Clear();
        checkBox.DataBindings.Add("Checked", ctx, propertyName, true, DataSourceUpdateMode.OnPropertyChanged);
    }
}
