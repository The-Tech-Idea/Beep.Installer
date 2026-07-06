using Beep.Installer.Models;

namespace Beep.Installer.Engine;

/// <summary>Holds the installer project loaded by a shipped Setup.exe.</summary>
public static class RuntimeProjectContext
{
    public static InstallProject? Current { get; set; }
}
