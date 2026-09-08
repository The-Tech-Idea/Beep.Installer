using System.Diagnostics;

namespace Beep.Installer.Engine.ClickOnce;

/// <summary>Native ClickOnce manifest operations; never substitutes a structural XML check.</summary>
internal static class MageManifestTool
{
    public static string? Find()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var sdk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft SDKs", "Windows", "v10.0A", "bin");
        if (Directory.Exists(sdk))
        {
            var candidate = Directory.EnumerateDirectories(sdk, "NETFX * Tools")
                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p => Path.Combine(p, "mage.exe")).FirstOrDefault(File.Exists);
            if (candidate is not null) return candidate;
        }
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathRooted(p))
            .Select(p => Path.Combine(p, "mage.exe")).FirstOrDefault(File.Exists);
    }

    public static (bool success, string? error) Run(params string[] arguments)
    {
        var tool = Find();
        if (tool is null) return (false, "Mage.exe is required for native ClickOnce manifest validation. Install the .NET Framework SDK tools.");
        try
        {
            var start = new ProcessStartInfo(tool)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Mage could not be started.");
            // Drain both pipes concurrently; do not expose output that may contain signing secrets.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return (false, "Native ClickOnce manifest operation timed out.");
            }
            // The wait above is bounded but this drain was not. A child that exits while something
            // it spawned still holds the write end of the pipe leaves this blocking for good --
            // the same shape of hang that wedged the qualification runner.
            if (!Task.WaitAll(new Task[] { stdout, stderr }, TimeSpan.FromSeconds(30)))
                return (false, "Native ClickOnce manifest tool exited but its output pipes stayed open.");
            // Mage 4.8 can return exit code zero for malformed/invalid signatures.
            // Require its affirmative operation result; unknown/localized output fails closed.
            var output = stdout.Result.Trim();
            var confirmed = arguments.FirstOrDefault() switch
            {
                "-Verify" => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Any(line => line.Trim() == "Manifest has a valid signature."),
                "-Sign" => output == Path.GetFileName(arguments[1]) + " successfully signed",
                _ => false
            };
            return process.ExitCode == 0 && confirmed && string.IsNullOrWhiteSpace(stderr.Result)
                ? (true, null) : (false, $"Native ClickOnce manifest validation failed (Mage exit {process.ExitCode}).");
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { return (false, "Native ClickOnce manifest tool could not complete."); }
    }
}
