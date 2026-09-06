using System.Diagnostics;

namespace Beep.Installer.Tests;

internal static class InstallerCliRunner
{
    public static InstallerCliResult Run(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{InstallerDll()}\" {arguments}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Installer process did not exit within 60 seconds. Arguments: {arguments}");
        }

        return new InstallerCliResult(
            process.ExitCode,
            stdoutTask.GetAwaiter().GetResult().Trim(),
            stderrTask.GetAwaiter().GetResult().Trim());
    }

    private static string InstallerDll()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Beep.Installer.dll");
        if (File.Exists(path))
            return path;

        throw new FileNotFoundException("Cannot locate Beep.Installer.dll next to the test assembly.", path);
    }
}

internal sealed record InstallerCliResult(int ExitCode, string StandardOutput, string StandardError);
