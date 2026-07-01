using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Beep.Installer.Lang;

namespace Beep.Installer.Pages;

public class FolderPage : UserControl, IInstallerPage
{
    private Label _prompt = null!;
    private TextBox _pathBox = null!;
    private Button _browseBtn = null!;
    private Label _spaceLabel = null!;
    private CheckBox _perUserCheck = null!;

    public string PageTitle => LanguageManager.GetOrDefault("Wizard_Folder", "Destination Folder");
    public string Subtitle => "Choose where to install.";
    public bool CanGoNext => !string.IsNullOrWhiteSpace(_pathBox.Text) && IsValidPath(_pathBox.Text);
    public event EventHandler<bool>? ValidityChanged;
    public string SelectedPath => _pathBox.Text;
    public bool PerUser => _perUserCheck.Checked;

    public FolderPage()
    {
        _prompt = new Label { Location = new Point(0, 0), Size = new Size(500, 20), AutoSize = true };
        _pathBox = new TextBox
        {
            Location = new Point(0, 30),
            Size = new Size(400, 24),
            Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Application")
        };
        _browseBtn = new Button
        {
            Text = LanguageManager.GetOrDefault("Btn_Browse", "Browse..."),
            Location = new Point(410, 29),
            Size = new Size(90, 26)
        };
        _browseBtn.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = SafeRoot(_pathBox.Text), ShowNewFolderButton = true };
            if (d.ShowDialog() == DialogResult.OK) _pathBox.Text = d.SelectedPath;
        };
        _perUserCheck = new CheckBox
        {
            Text = "Install for current user only (no admin required)",
            Location = new Point(0, 60),
            Size = new Size(500, 24)
        };
        _spaceLabel = new Label
        {
            Location = new Point(0, 90),
            Size = new Size(500, 40),
            ForeColor = Color.Gray
        };

        _pathBox.TextChanged += (_, _) => { UpdateSpaceInfo(); ValidityChanged?.Invoke(this, CanGoNext); };
        Controls.AddRange(new Control[] { _prompt, _pathBox, _browseBtn, _perUserCheck, _spaceLabel });
    }

    public void OnEnter(InstallContext ctx)
    {
        _prompt.Text = LanguageManager.GetOrDefault("Folder_Prompt", "Select the folder where the application will be installed:");
        if (!string.IsNullOrEmpty(ctx.Config.DefaultInstallPath)) _pathBox.Text = ctx.Config.DefaultInstallPath;
        _perUserCheck.Checked = !ctx.Config.RequireAdminPrivileges;
        _perUserCheck.Visible = ctx.Config.RequireAdminPrivileges;
        ctx.InstallPath = _pathBox.Text;
        UpdateSpaceInfo();
    }

    private void UpdateSpaceInfo()
    {
        try
        {
            var root = Path.GetPathRoot(_pathBox.Text);
            if (!string.IsNullOrEmpty(root))
            {
                var free = new DriveInfo(root).AvailableFreeSpace;
                _spaceLabel.Text = LanguageManager.GetOrDefault("Folder_AvailableSpace", "Available space: {0}")
                    .Replace("{0}", Engine.LocaleFormatter.FormatSize(free));
                _spaceLabel.ForeColor = Color.Gray;
            }
        }
        catch
        {
            _spaceLabel.Text = LanguageManager.GetOrDefault("Folder_SpaceUnknown", "Unable to check disk space.");
            _spaceLabel.ForeColor = Color.Gray;
        }
    }

    private static bool IsValidPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return !string.IsNullOrWhiteSpace(full);
        }
        catch { return false; }
    }

    private static string SafeRoot(string path)
    {
        try { return Path.GetDirectoryName(path) ?? path; } catch { return path; }
    }

    public new bool Validate() => CanGoNext;
}
