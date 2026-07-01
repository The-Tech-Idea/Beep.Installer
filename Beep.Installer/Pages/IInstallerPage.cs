using System;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>Shared context passed between installer pages and the wizard shell.</summary>
public class InstallContext
{
    public InstallConfig Config { get; set; } = new();
    public string InstallPath { get; set; } = "";
    public bool AcceptLicense { get; set; }
    public bool CreateDesktopIcon { get; set; } = true;
    public bool CreateStartMenu { get; set; } = true;
    public bool AutoStart { get; set; }
    public bool PerUser { get; set; }
    public bool FileAssociations { get; set; } = true;
    public string StartMenuFolder { get; set; } = "";
    public InstallationType InstallType { get; set; } = InstallationType.Typical;

    /// <summary>Bag for misc key/value passing between pages.</summary>
    public Dictionary<string, object> Bag { get; } = new();

    public long EstimatedSizeBytes =>
        Config.Components.Where(c => c.Selected || c.Required).Sum(c => c.SizeBytes);
}
