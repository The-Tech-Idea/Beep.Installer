using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Beep.Installer.Mcp;

/// <summary>
/// Guided workflows a client can offer as ready-made prompts. Each one only tells the model which of the
/// existing tools to call and in what order; no prompt runs anything itself.
/// </summary>
[McpServerPromptType]
public sealed class InstallerPrompts
{
    [McpServerPrompt(Name = "build_installer"), Description("Validate, plan and build an installer from a .bsetup project, then report readiness per package format.")]
    public string BuildInstaller(
        [Description("Path to the .bsetup project.")] string script,
        [Description("Output directory for the build.")] string? outputDir = null)
        => $"Build an installer from '{script}'. Use the Beep.Installer tools in this order and stop at the first failure, reporting its diagnostics: " +
           "1) installer_validate (strict=true); 2) installer_plan; 3) installer_format_readiness; " +
           $"4) installer_build{(string.IsNullOrWhiteSpace(outputDir) ? string.Empty : $" with outputDir '{outputDir}'")}. " +
           "Summarise the result and which formats (EXE, MSI, MSIX, WinGet) are ready. Never pass a credential as a value; signing uses the project's own configuration.";

    [McpServerPrompt(Name = "preview_machine_change"), Description("Preview an install, uninstall or repair with /DRYRUN before anything changes this machine.")]
    public string PreviewMachineChange(
        [Description("Command id: silent-install, repair, uninstall, apply-delta, msi ...")] string command,
        [Description("The .bsetup project or the command's value.")] string value)
        => $"Use installer_describe_command for '{command}' to see whether it supports /DRYRUN. If it does, call installer_run with dryRun=true for '{value}' and explain what it would do. " +
           "If it has no dry-run, say so and ask me before calling installer_run with allowMachineChanges=true. Never set allowMachineChanges without my explicit yes.";

    [McpServerPrompt(Name = "explore_commands"), Description("Find the right installer command for a goal and explain its options.")]
    public string ExploreCommands([Description("What you want to achieve, e.g. 'sign an MSI' or 'verify an update feed'.")] string goal)
        => $"I want to: {goal}. Call installer_list_commands, pick the matching commands, then installer_describe_command for each and explain the required options, effect, and whether /DRYRUN and /JSON are supported.";
}
