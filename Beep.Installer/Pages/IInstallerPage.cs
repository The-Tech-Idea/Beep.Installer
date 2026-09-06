using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Pages;

/// <summary>Lifecycle interface for installer wizard pages.</summary>
public interface IInstallerPage
{
    string PageTitle { get; }
    string Subtitle { get; }
    bool CanGoNext { get; }
    void OnEnter(InstallContext ctx);
    bool Validate();
    event EventHandler<bool>? ValidityChanged;
}

/// <summary>Shared context passed between installer pages and the wizard shell.
/// Implements INotifyPropertyChanged for direct WinForms data binding.</summary>
public class InstallContext : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private InstallProject _project = new();
    public InstallProject Project
    {
        get => _project;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Set(ref _project, value);
            PerUser = Beep.Installer.Engine.InstallScopeResolver.IsPerUser(value);
        }
    }

    private string _installPath = "";
    public string InstallPath
    {
        get => _installPath;
        set => Set(ref _installPath, value);
    }

    private bool _acceptLicense;
    public bool AcceptLicense
    {
        get => _acceptLicense;
        set => Set(ref _acceptLicense, value);
    }

    private bool _createDesktopIcon = true;
    public bool CreateDesktopIcon
    {
        get => _createDesktopIcon;
        set => Set(ref _createDesktopIcon, value);
    }

    private bool _createStartMenu = true;
    public bool CreateStartMenu
    {
        get => _createStartMenu;
        set => Set(ref _createStartMenu, value);
    }

    private bool _autoStart;
    public bool AutoStart
    {
        get => _autoStart;
        set => Set(ref _autoStart, value);
    }

    private bool _perUser;
    public bool PerUser
    {
        get => _perUser;
        set => Set(ref _perUser, value);
    }

    private bool _fileAssociations = true;
    public bool FileAssociations
    {
        get => _fileAssociations;
        set => Set(ref _fileAssociations, value);
    }

    private string _startMenuFolder = "";
    public string StartMenuFolder
    {
        get => _startMenuFolder;
        set => Set(ref _startMenuFolder, value);
    }

    private InstallationType _installType = InstallationType.Typical;
    public InstallationType InstallType
    {
        get => _installType;
        set => Set(ref _installType, value);
    }

    public Dictionary<string, object> Bag { get; } = new();

    public long EstimatedSizeBytes =>
        Project.Components.Where(c => c.Selected || c.Required).Sum(c => c.SizeBytes);

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
