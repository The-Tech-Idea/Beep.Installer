using System.ComponentModel;
using System.Diagnostics;

namespace Beep.Installer.Cli;

/// <summary>
/// The one owner of running the installer executable from a console: starts it with the given arguments, relays its
/// standard output and error as they arrive, and returns its exit code. It knows no verb; the installer decides what an
/// argument means.
/// </summary>
public static class InstallerForwarder
{
    /// <summary>The environment variable that names the installer executable when it is not beside beepi.</summary>
    public const string ExecutableVariable = "BEEP_INSTALLER_EXE";

    public const int ExitInstallerNotFound = 3;
    public const int ExitCouldNotStart = 4;
    public const string InstallerFileName = "Beep.Installer.exe";

    /// <summary>The installer to run: the variable when set, else the one beside this program; null when neither exists.</summary>
    public static string? Locate(string baseDirectory, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        }

        var beside = Path.Combine(baseDirectory, InstallerFileName);
        return File.Exists(beside) ? beside : null;
    }

    public static async Task<int> RunAsync(string? executable, IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        if (executable is null)
        {
            await error.WriteLineAsync($"beepi: {InstallerFileName} was not found beside beepi, and {ExecutableVariable} does not name an existing file.");
            return ExitInstallerNotFound;
        }

        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            // Reported to the caller on stderr with a distinct exit code: a console tool has no other channel.
            await error.WriteLineAsync($"beepi: could not start {executable}: {ex.Message}");
            return ExitCouldNotStart;
        }

        // The installer reads no input in a headless verb; closing stdin keeps it from waiting on a console that is not there.
        process.StandardInput.Close();
        var copyOut = Pump(process.StandardOutput, output, cancellationToken);
        var copyError = Pump(process.StandardError, error, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(copyOut, copyError);
        return process.ExitCode;
    }

    private static async Task Pump(StreamReader from, TextWriter to, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await from.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            await to.FlushAsync(cancellationToken);
        }
    }
}
