using System;
using System.IO;
using System.Linq;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

public static class InstallScopeResolver
{
    public static string ProgramFilesFolder(bool prefer64Bit)
    {
        if (prefer64Bit)
            return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return string.IsNullOrEmpty(x86)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            : x86;
    }

    /// <summary>
    /// The single decision point for per-user vs per-machine install scope.
    ///
    /// This matters more than it looks: the resulting flag travels as the <c>PerUser</c>
    /// context key and selects the registry hive for the registry, COM, shared-file and
    /// uninstall steps. Getting it wrong silently sends every write to HKLM.
    ///
    /// Note the current mapping treats only <see cref="PrivilegeLevel.User"/> as per-user;
    /// <see cref="PrivilegeLevel.Lowest"/> resolves to per-machine, which is arguably
    /// backwards. Behaviour is preserved here deliberately — changing it would relocate
    /// existing installations. Revisit alongside the scope-awareness work in phase P2.
    /// </summary>
    public static bool IsPerUser(InstallProject project)
    {
        if (project == null) return false;
        return project.PrivilegesRequired != PrivilegeLevel.Admin
            && project.PrivilegesRequired != PrivilegeLevel.Lowest;
    }

    public static string DefaultBase(bool prefer64Bit, bool perUser)
        => perUser
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : ProgramFilesFolder(prefer64Bit);

    public static string ResolveDefaultPath(InstallProject project, bool perUser)
    {
        var raw = project?.DefaultDirName ?? "";
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        var folder = DefaultBase(project?.Prefer64Bit ?? true, perUser);
        var resolved = raw
            .Replace("%ProgramFilesX86%", folder)
            .Replace("%ProgramFiles%", folder);
        return TheTechIdea.Beep.Installer.ConfigManager.ExpandVariables(resolved);
    }

    public static string ResolveOutputDirectory(InstallProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.OutputDir))
            return project.OutputDir;

        var product = SafeFileName(project.AppName);
        var version = SafeFileName(project.AppVersion);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "BeepInstaller", "Builds", $"{product}-{version}");
    }

    private static string SafeFileName(string name, string fallback = "Application")
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? fallback : clean;
    }
}