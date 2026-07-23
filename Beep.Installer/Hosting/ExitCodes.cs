using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Hosting;

/// <summary>
/// Installer process exit codes, following the MSI conventions deployment tooling
/// (Intune/SCCM/winget) already understands.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int BadScript = 2;

    /// <summary>MSI convention: succeeded, but a reboot is required to complete.</summary>
    public const int RebootRequired = 3010;

    /// <summary>
    /// Resolves the exit code for a finished install run.
    /// </summary>
    /// <param name="succeeded">Whether the wizard run succeeded.</param>
    /// <param name="context">Run context; <c>RebootRequired</c> is set by FileCopyStep when a
    /// locked file was scheduled for replacement at reboot.</param>
    /// <param name="noRestart">CLI <c>/NORESTART</c>: report plain success even when a reboot
    /// is pending (for tooling that treats any non-zero code as failure).</param>
    /// <param name="restartExitCode">CLI <c>/RESTARTEXITCODE=n</c> override for the 3010 default.</param>
    public static int ForInstallResult(bool succeeded, SetupContext context, bool noRestart, int? restartExitCode = null)
    {
        if (!succeeded) return Failure;

        var rebootPending = context.Properties.TryGetValue("RebootRequired", out var flag) && flag is true;
        if (!rebootPending || noRestart) return Success;

        return restartExitCode ?? RebootRequired;
    }
}
