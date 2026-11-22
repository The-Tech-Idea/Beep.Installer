namespace Beep.Installer.Mcp;

/// <summary>
/// Where <c>Beep.Installer.exe</c> is. Taken only from an explicit setting — <c>--exe &lt;path&gt;</c> or the
/// <c>BEEP_INSTALLER_EXE</c> environment variable — never guessed from the working directory, so the
/// server cannot run a different build than the one the user pointed it at.
/// </summary>
public sealed class InstallerExecutable
{
    public const string EnvironmentVariable = "BEEP_INSTALLER_EXE";
    public const string CommandLineSwitch = "--exe";

    private readonly string? _configured;

    public InstallerExecutable(string? configuredPath) => _configured = string.IsNullOrWhiteSpace(configuredPath) ? null : configuredPath;

    public static InstallerExecutable FromEnvironment(string? commandLinePath)
        => new(commandLinePath ?? Environment.GetEnvironmentVariable(EnvironmentVariable));

    /// <summary>The executable to run, or the reason there isn't one. Checked per call so the catalog tools work without it.</summary>
    public bool TryResolve(out string path, out string problem)
    {
        path = "";
        if (_configured is null)
        {
            problem = $"Beep.Installer.exe is not configured. Start this server with {CommandLineSwitch} <path to Beep.Installer.exe> " +
                      $"or set the {EnvironmentVariable} environment variable.";
            return false;
        }

        var full = Path.GetFullPath(_configured);
        if (!File.Exists(full))
        {
            problem = $"Beep.Installer.exe was not found at '{full}' (from {CommandLineSwitch} / {EnvironmentVariable}). Correct that setting.";
            return false;
        }

        path = full;
        problem = "";
        return true;
    }
}
