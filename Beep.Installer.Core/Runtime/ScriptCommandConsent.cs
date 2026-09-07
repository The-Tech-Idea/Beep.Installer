using Beep.Installer.Policy;

namespace Beep.Installer.Engine;

/// <summary>
/// Decides whether an installation may execute the custom actions its author declared.
///
/// Custom actions launch arbitrary executables during install. They ship inside the same installer
/// the operator chose to run, so this is not about untrusted code — it is about the deployer being
/// able to say no. Someone pushing a third-party package through Intune or SCCM has, until now, had
/// no way to take the files without also taking the scripts, and <c>ForbidCustomActions</c> existed
/// in the policy model but only ever affected MSI export.
///
/// Silence is not refusal: with no flag and no policy the decision is "none", and actions run as
/// they always have. Making refusal the default would break every deployment that relies on them.
/// </summary>
public static class ScriptCommandConsent
{
    /// <summary>
    /// The effective decision, or null when nobody expressed one.
    /// </summary>
    /// <param name="explicitlyAllowed">The deployer passed <c>/ALLOWSCRIPTCMDS</c>.</param>
    /// <param name="explicitlyRefused">The deployer passed <c>/NOSCRIPTCMDS</c>.</param>
    /// <param name="policy">The effective enterprise policy, when one applies.</param>
    public static bool? Decide(bool explicitlyAllowed, bool explicitlyRefused, InstallerPolicy? policy)
    {
        // The operator at the keyboard outranks the policy file: /ALLOWSCRIPTCMDS is how a managed
        // machine runs a package whose actions are genuinely needed, without editing policy.
        if (explicitlyAllowed) return true;
        if (explicitlyRefused) return false;
        if (policy?.ForbidCustomActions == true) return false;
        return null;
    }
}
