using System;
using System.Collections.Generic;
using System.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Policy;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Engine;

/// <summary>
/// Binds the effective installer policy, and the deployer's decision about authored custom actions,
/// onto a run context.
///
/// This existed only inside <c>Program</c>'s headless verbs. The interactive wizard built its
/// context through the same <c>InstallContextBuilder</c> — with a comment saying both paths produce
/// an identical context — and then never set <c>ResourcePolicy</c> and never applied the consent
/// decision. So <c>Setup.exe /POLICY=corp.json</c> was honoured, and <c>Setup.exe /POLICY=corp.json</c>
/// without <c>/S</c> silently ignored the same file.
///
/// The practical exposure is narrow: policy is only ever loaded from a path given on the command
/// line — there is no ambient machine location the resolver probes — so a plain double-click had no
/// policy to ignore in the first place. It is the divergence between two paths that claim to be
/// identical that makes this worth having in one place.
/// </summary>
internal static class RuntimePolicyBinder
{
    /// <summary>What binding the policy decided, so a caller can surface it in its own idiom.</summary>
    internal sealed class Binding
    {
        public InstallerPolicy? Policy { get; init; }
        public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
        public bool? ScriptCommandsAllowed { get; init; }

        public bool HasErrors => Diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error);
    }

    /// <summary>
    /// Resolves policy from <paramref name="args"/>, evaluates it against the project, and writes
    /// both the policy and the custom-action decision onto the context.
    /// </summary>
    /// <param name="args">
    /// The process command line. Interactive runs carry the same switches as silent ones; the
    /// wizard simply never looked at them.
    /// </param>
    public static Binding Apply(SetupContext context, InstallProject project, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(project);
        args ??= Array.Empty<string>();

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var resolution = InstallerPolicyResolver.Resolve(new InstallerPolicyResolutionOptions
        {
            ProjectPolicyPath = ArgValue(args, "/PROJECTPOLICY=") ?? "",
            ProfilePolicyPath = ArgValue(args, "/PROFILEPOLICY=") ?? ArgValue(args, "/POLICY=") ?? "",
            MachinePolicyPath = ArgValue(args, "/MACHINEPOLICY=") ?? ""
        });
        diagnostics.AddRange(resolution.Diagnostics);

        var policy = resolution.Policy;
        if (policy != null)
        {
            diagnostics.AddRange(InstallerPolicyEvaluator.EvaluateProject(policy, project).Diagnostics);
            context.Properties[InstallContextKeys.ResourcePolicy] = policy;
        }

        // Silence stays silence: with no flag and no policy nothing is written, and the custom
        // action step behaves exactly as it always has.
        var decision = ScriptCommandConsent.Decide(
            Has(args, "/ALLOWSCRIPTCMDS"), Has(args, "/NOSCRIPTCMDS"), policy);

        if (decision is not null)
        {
            context.Properties[InstallContextKeys.AllowScriptCommands] = decision.Value;
            if (!decision.Value)
                Diag.Warn("Policy", "Authored custom actions are refused for this run.", eventId: "BI2620");
        }

        return new Binding { Policy = policy, Diagnostics = diagnostics, ScriptCommandsAllowed = decision };
    }

    private static string? ArgValue(string[] args, string prefix)
        => args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private static bool Has(string[] args, string flag)
        => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
}
