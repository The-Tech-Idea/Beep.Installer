using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Beep.Installer.Engine;

public static class SignTool
{
    public static string? Find()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { var candidate = Path.Combine(dir, "signtool.exe"); if (File.Exists(candidate)) return candidate; }
            catch (Exception ex) { Diag.Debug("SignTool", "PATH entry skipped", ex); }
        }

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            var sdkBase = Path.Combine(root, "Windows Kits", "10", "bin");
            if (!Directory.Exists(sdkBase)) continue;
            foreach (var verDir in Directory.EnumerateDirectories(sdkBase))
            {
                var candidate = Path.Combine(verDir, "x64", "signtool.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    public static (bool ok, string? error) Sign(string exePath, string certPath,
        string? certPassword, string? timestampUrl)
    {
        var signtool = Find();
        if (signtool == null)
            return (false, "signtool.exe not found. Install the Windows SDK.");

        try
        {
            var args = new StringBuilder();
            args.Append("sign /fd SHA256 ");
            if (!string.IsNullOrEmpty(timestampUrl))
                args.Append($"/tr \"{timestampUrl}\" /td SHA256 ");
            if (!string.IsNullOrEmpty(certPassword))
                args.Append($"/f \"{certPath}\" /p \"{certPassword}\" ");
            else
                args.Append($"/f \"{certPath}\" ");
            args.Append($"\"{exePath}\"");

            var psi = new ProcessStartInfo
            {
                FileName = signtool,
                Arguments = args.ToString(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(60_000);

            if (p.ExitCode != 0)
                return (false, $"signtool exited {p.ExitCode}: {stdout?.Trim()} {stderr?.Trim()}");
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
