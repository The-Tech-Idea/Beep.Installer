using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Lang;
using Beep.Installer.Models;
using TheTechIdea.Beep.Winform.Controls;

namespace Beep.Installer.Pages;

public class FolderPage : UserControl, IInstallerPage
{
    private BeepLabel _title = null!;
    private BeepLabel _prompt = null!;
    private TextBox _pathBox = null!;
    private BeepButton _browseBtn = null!;
    private BeepLabel _spaceLabel = null!;
    private CheckBox _perUserCheck = null!;
    private InstallContext _ctx = null!;
    private bool _userEdited;

    public string PageTitle => LanguageManager.GetOrDefault("Wizard_Folder", "Destination Folder");
    public string Subtitle => "Choose where to install.";
    public bool CanGoNext => !string.IsNullOrWhiteSpace(_pathBox.Text) && IsValidPath(_pathBox.Text);
    public event EventHandler<bool>? ValidityChanged;
    public string SelectedPath => _pathBox.Text;
    public bool PerUser => _perUserCheck.Checked;

    public FolderPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(32, 28, 32, 12);

        _title = new BeepLabel
        {
            Text = "Destination Folder",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            Location = new Point(32, 24),
            AutoSize = true
        };

        _prompt = new BeepLabel
        {
            Text = "Select the folder where the application will be installed:",
            Location = new Point(32, 64),
            Size = new Size(620, 40),
            Font = new Font("Segoe UI", 10)
        };

        _pathBox = new TextBox
        {
            Location = new Point(32, 116),
            Size = new Size(500, 30),
            Font = new Font("Segoe UI", 10),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _browseBtn = new BeepButton
        {
            Text = "Browse...",
            ImagePath = "Beep.Installer.Resources.Icons.folder-open.svg",
            Location = new Point(540, 114),
            Size = new Size(112, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Font = new Font("Segoe UI", 9)
        };
        _browseBtn.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = SafeRoot(_pathBox.Text), ShowNewFolderButton = true };
            if (d.ShowDialog() == DialogResult.OK) _pathBox.Text = d.SelectedPath;
        };

        _perUserCheck = new CheckBox
        {
            Text = "Install for current user only (no admin required)",
            Location = new Point(32, 158),
            Size = new Size(620, 24),
            Font = new Font("Segoe UI", 10)
        };
        _spaceLabel = new BeepLabel
        {
            Location = new Point(32, 196),
            Size = new Size(620, 30),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 9)
        };

        _pathBox.TextChanged += (_, _) =>
        {
            _userEdited = true;
            UpdateSpaceInfo();
            ValidityChanged?.Invoke(this, CanGoNext);
        };
        _perUserCheck.CheckedChanged += (_, _) =>
        {
            if (_ctx is null) return;
            if (!_userEdited)
                _pathBox.Text = InstallScopeResolver.ResolveDefaultPath(_ctx.Project, _perUserCheck.Checked);
        };
        Controls.AddRange(new Control[] { _title, _prompt, _pathBox, _browseBtn, _perUserCheck, _spaceLabel });
    }

    public void OnEnter(InstallContext ctx)
    {
        _ctx = ctx;
        _userEdited = false;

        _pathBox.DataBindings.Clear();
        _pathBox.DataBindings.Add("Text", ctx, nameof(InstallContext.InstallPath), true, DataSourceUpdateMode.OnPropertyChanged);
        _perUserCheck.DataBindings.Clear();
        _perUserCheck.DataBindings.Add("Checked", ctx, nameof(InstallContext.PerUser), true, DataSourceUpdateMode.OnPropertyChanged);

        _prompt.Text = LanguageManager.GetOrDefault("Folder_Prompt", "Select the folder where the application will be installed:");
        _perUserCheck.Visible = ctx.Project.PrivilegesRequired == PrivilegeLevel.Admin;
        if (!_userEdited) _pathBox.Text = InstallScopeResolver.ResolveDefaultPath(ctx.Project, _perUserCheck.Checked);
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
        catch (Exception ex) { Engine.Diag.Debug("FolderPage", $"invalid path '{path}'", ex); return false; }
    }

    private static string SafeRoot(string path)
    {
        try { return Path.GetDirectoryName(path) ?? path; }
        catch (Exception ex) { Engine.Diag.Debug("FolderPage", "SafeRoot fallback", ex); return path; }
    }

    public new bool Validate() => CanGoNext;
}
