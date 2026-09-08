using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Beep.Installer.Engine.ClickOnce;

/// <summary>
/// Creates a Windows .lnk shortcut through the shell's <c>WScript.Shell</c> COM object.
///
/// This used to build a PowerShell script and shell out to <c>powershell.exe</c> to reach the same
/// COM object. That meant spawning an interpreter per shortcut, quoting every value twice —
/// once for PowerShell, once for the command line — and inheriting the process-launch defects the
/// rest of the installer has since been swept clear of: redirected pipes that were never drained,
/// and <c>ExitCode</c> read after a wait that may have timed out, which throws on a live process.
/// It also failed silently on any machine with PowerShell disabled by policy.
///
/// The runtime install path already talked to this COM object directly
/// (<c>WindowsInstallerShortcutStore</c>); this is the same mechanism, so both paths now produce a
/// shortcut the same way.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Shortcut
{
    /// <summary>Returns true on success. Never throws — a failed shortcut is not a failed install.</summary>
    public static bool Create(string linkPath, string targetPath, string? arguments = null,
                              string? workDir = null, string? iconPath = null, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(linkPath) || string.IsNullOrWhiteSpace(targetPath))
        {
            Diag.Warn("Shortcut", "shortcut needs both a link path and a target");
            return false;
        }

        object? shell = null;
        object? shortcut = null;
        try
        {
            var directory = Path.GetDirectoryName(linkPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                Diag.Warn("Shortcut", "WScript.Shell is not registered on this machine");
                return false;
            }

            shell = Activator.CreateInstance(shellType);
            dynamic ws = shell!;
            dynamic link = ws.CreateShortcut(linkPath);
            shortcut = link;

            link.TargetPath = targetPath;
            if (!string.IsNullOrWhiteSpace(arguments)) link.Arguments = arguments;
            if (!string.IsNullOrWhiteSpace(workDir)) link.WorkingDirectory = workDir;
            if (!string.IsNullOrWhiteSpace(iconPath)) link.IconLocation = iconPath;
            if (!string.IsNullOrWhiteSpace(description)) link.Description = description;
            link.Save();

            return File.Exists(linkPath);
        }
        catch (Exception ex)
        {
            Diag.Warn("Shortcut", $"could not create shortcut '{linkPath}'", ex, "BI2630");
            return false;
        }
        finally
        {
            // Released explicitly: an unreleased shell object keeps a COM apartment alive for the
            // rest of the process.
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }
}
