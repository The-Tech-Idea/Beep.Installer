using System;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Beep.Installer.Engine.ClickOnce;

/// <summary>
/// Creates a Windows .lnk shortcut via PowerShell hosting WScript.Shell — no COM reference
/// required, works on every Windows install. Track B2.2.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Shortcut
{
    /// <summary>Returns true on success.</summary>
    public static bool Create(string linkPath, string targetPath, string? arguments = null,
                              string? workDir = null, string? iconPath = null, string? description = null)
    {
        static string Q(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        var ps = string.Join("; ",
            "$ws = New-Object -ComObject WScript.Shell",
            $"$s = $ws.CreateShortcut({Q(linkPath)})",
            $"$s.TargetPath = {Q(targetPath)}",
            arguments != null ? $"$s.Arguments = {Q(arguments)}" : null,
            workDir != null ? $"$s.WorkingDirectory = {Q(workDir)}" : null,
            iconPath != null ? $"$s.IconLocation = {Q(iconPath)}" : null,
            description != null ? $"$s.Description = {Q(description)}" : null,
            "$s.Save()");

        try
        {
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{ps.Replace("\"", "\\\"")}\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(30_000);
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Diag.Debug("Shortcut", "powershell shell failed", ex);
            return false;
        }
    }
}