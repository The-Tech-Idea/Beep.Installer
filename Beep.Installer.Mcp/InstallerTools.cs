using System.ComponentModel;
using Beep.Installer.Engine;
using Beep.Installer.Core.Cli;
using ModelContextProtocol.Server;

namespace Beep.Installer.Mcp;

/// <summary>
/// The MCP tool surface. A few typed tools cover the everyday authoring flow; <c>installer_run</c> reaches
/// every other command in the catalog, so nothing the CLI can do is out of reach.
/// All of them go through <see cref="InstallerCommandRunner"/> — no tool re-implements a verb.
/// </summary>
[McpServerToolType]
public sealed class InstallerTools(InstallerCommandRunner runner)
{
    // ── Discovery ────────────────────────────────────────────────────────────

    [McpServerTool(Name = "installer_list_commands", Title = "List installer commands", ReadOnly = true, Destructive = false, OpenWorld = false),
     Description("List the Beep.Installer commands (about 70) with category, effect and summary. Filter by category (builder, sdk, updates, qualification, formats, security, runtime) or effect (read-only, writes-files, network, machine-changing, interactive). Use installer_describe_command for one command's options.")]
    public object ListCommands(
        [Description("Optional category filter.")] string? category = null,
        [Description("Optional effect filter.")] string? effect = null,
        [Description("Include commands that are deliberately not offered over MCP, with the reason.")] bool includeUnavailable = false)
    {
        var verbs = runner.Catalog.Verbs.AsEnumerable();
        if (!includeUnavailable) verbs = verbs.Where(v => v.Mcp.Exposed);
        if (!string.IsNullOrWhiteSpace(category)) verbs = verbs.Where(v => string.Equals(v.Category, category, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(effect)) verbs = verbs.Where(v => v.Effect.ToWireName().Equals(effect, StringComparison.OrdinalIgnoreCase));

        return new
        {
            categories = runner.Catalog.Categories,
            commands = verbs.Select(v => new
            {
                id = v.Id,
                token = v.Token,
                category = v.Category,
                effect = v.Effect.ToWireName(),
                summary = v.Summary,
                supportsJson = v.SupportsJson,
                supportsDryRun = v.SupportsDryRun,
                available = v.Mcp.Exposed,
                unavailableReason = v.Mcp.Reason,
            }).ToList(),
        };
    }

    [McpServerTool(Name = "installer_describe_command", Title = "Describe an installer command", ReadOnly = true, Destructive = false, OpenWorld = false),
     Description("Describe one command: the value it takes, every option (required/secret flags), effect, dry-run and JSON support, and the exit-code contract.")]
    public object DescribeCommand([Description("Command id (e.g. 'build') or token (e.g. '/BUILD=').")] string command)
    {
        var verb = runner.Catalog.Find(command);
        return verb is null
            ? new { found = false, message = $"No command '{command}'. Use installer_list_commands." }
            : (object)new { found = true, command = verb, exitCodes = runner.Catalog.ExitCodes };
    }

    // ── Generic runner ───────────────────────────────────────────────────────

    [McpServerTool(Name = "installer_run", Title = "Run any installer command", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true),
     Description("Run any catalog command. 'value' is the command's own value (e.g. the .bsetup path for 'build'); 'options' maps option names to values ({\"/OUT=\":\"C:/out\", \"/SBOM\":\"true\"}). Commands that change this machine (silent-install, uninstall, repair, apply-delta, msi, ...) are refused unless dryRun=true (where supported) or allowMachineChanges=true. Credential options accept only secret://env/NAME or env:NAME references. Returns the exit code as-is: 0 success, 3010 success+reboot, 1 failed, 2 rejected arguments.")]
    public Task<InstallerRunResult> Run(
        [Description("Command id or token, e.g. 'validate' or '/VALIDATE='.")] string command,
        [Description("The command's value, e.g. a .bsetup path.")] string? value = null,
        [Description("Option name -> value. Flag options take \"true\".")] Dictionary<string, string>? options = null,
        [Description("Unattended /PROPERTY:Name=Value pairs (silent-install and repair).")] Dictionary<string, string>? properties = null,
        [Description("Preview only, where the command supports /DRYRUN.")] bool dryRun = false,
        [Description("Required to run a machine-changing command for real.")] bool allowMachineChanges = false,
        [Description("Kill the command after this many seconds (default 300, max 3600).")] int? timeoutSeconds = null,
        [Description("Directory to run in. Relative paths in options resolve against it.")] string? workingDirectory = null,
        CancellationToken cancellationToken = default)
        => runner.RunAsync(
            new CliInvocationRequest
            {
                Command = command,
                Value = value,
                Options = options,
                Properties = properties,
                DryRun = dryRun,
                AllowMachineChanges = allowMachineChanges,
            },
            timeoutSeconds, workingDirectory, cancellationToken);

    // ── Typed everyday tools ─────────────────────────────────────────────────

    [McpServerTool(Name = "installer_validate", Title = "Validate a .bsetup project", ReadOnly = true, Destructive = false, OpenWorld = false),
     Description("Validate an installer project without building it (lint, schema, policy, extension validators). Returns the structured diagnostics report.")]
    public Task<InstallerRunResult> Validate(
        [Description("Path to the .bsetup project.")] string script,
        [Description("Fail strict warnings such as literal secrets.")] bool strict = false,
        [Description("Extension directories, ';' separated.")] string? extensions = null,
        CancellationToken cancellationToken = default)
        => Simple("validate", script, Flags(("/STRICT", strict)), Values(("/EXTENSIONS=", extensions)), cancellationToken);

    [McpServerTool(Name = "installer_plan", Title = "Compile the install plan", ReadOnly = true, Destructive = false, OpenWorld = false),
     Description("Compile a project and return its install plan without building or installing anything.")]
    public Task<InstallerRunResult> Plan(
        [Description("Path to the .bsetup project.")] string script,
        [Description("Fail strict warnings.")] bool strict = false,
        CancellationToken cancellationToken = default)
        => Simple("plan", script, Flags(("/STRICT", strict)), null, cancellationToken);

    [McpServerTool(Name = "installer_preview", Title = "Summarize a project", ReadOnly = true, Destructive = false, OpenWorld = false),
     Description("Print a short summary of a project: product, version, components, shortcuts.")]
    public Task<InstallerRunResult> Preview(
        [Description("Path to the .bsetup project.")] string script,
        CancellationToken cancellationToken = default)
        => Simple("preview", script, null, null, cancellationToken);

    [McpServerTool(Name = "installer_format_readiness", Title = "Package format readiness", ReadOnly = true, Destructive = false, OpenWorld = false),
     Description("Report whether EXE, MSI, MSIX, WinGet and the management kit are each ready to release for this project. The exit code is 1 when any format is blocked.")]
    public Task<InstallerRunResult> FormatReadiness(
        [Description("Path to the .bsetup project.")] string script,
        CancellationToken cancellationToken = default)
        => Simple("format-readiness", script, null, null, cancellationToken);

    [McpServerTool(Name = "installer_list_templates", Title = "List project templates", ReadOnly = true, Destructive = false, OpenWorld = false),
     Description("List the built-in project template ids (console, service, ...) usable for new projects and template packages.")]
    public Task<InstallerRunResult> ListTemplates(CancellationToken cancellationToken = default)
        => Simple("list-templates", null, null, null, cancellationToken);

    // ── Project fields (not a CLI verb: the builder's own scalar fields, validated before anything is written) ──

    [McpServerTool(Name = "installer_get_project_fields", Title = "Read project fields", ReadOnly = true, Destructive = false, OpenWorld = false),
     Description("List the editable top-level scalar fields of a .bsetup project (name, version, publisher, install directory, flags...) with their current values.")]
    public ProjectFieldResult GetProjectFields([Description("Path to the .bsetup project.")] string script)
        => ProjectFieldEditor.List(script);

    [McpServerTool(Name = "installer_set_project_fields", Title = "Edit project fields", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Change scalar fields of a .bsetup project, e.g. {\"AppVersion\":\"2.0.0\"}. The project is validated first; an edit that would make it invalid is refused and nothing is written. A .bak copy is kept. Credential-like fields and collections (files, components, shortcuts, registry) are not editable here.")]
    public ProjectFieldResult SetProjectFields(
        [Description("Path to the .bsetup project.")] string script,
        [Description("Field name -> new value.")] Dictionary<string, string> fields)
        => ProjectFieldEditor.Set(script, fields);

    [McpServerTool(Name = "installer_build", Title = "Build an installer", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Build the installer from a project (Track A Setup.exe; format 'msix' for Track C). Writes into outputDir. Signing uses the project's own configuration; this tool never accepts a credential.")]
    public Task<InstallerRunResult> Build(
        [Description("Path to the .bsetup project.")] string script,
        [Description("Output directory.")] string? outputDir = null,
        [Description("Package format: exe, msix or msixbundle.")] string? format = null,
        [Description("Fail strict warnings.")] bool strict = false,
        [Description("Export an SPDX SBOM beside the output.")] bool sbom = false,
        [Description("Export provenance beside the output.")] bool provenance = false,
        [Description("Refuse to build unless the result is signed.")] bool requireSigned = false,
        [Description("Kill the build after this many seconds.")] int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => Simple("build", script,
            Flags(("/STRICT", strict), ("/SBOM", sbom), ("/PROVENANCE", provenance), ("/REQUIRESIGNED", requireSigned)),
            Values(("/OUT=", outputDir), ("/FORMAT=", format)), cancellationToken, timeoutSeconds);

    [McpServerTool(Name = "installer_publish_clickonce", Title = "Publish as ClickOnce", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Publish the project as a ClickOnce deployment (Track B): publish folder, manifests and .deploy files.")]
    public Task<InstallerRunResult> PublishClickOnce(
        [Description("Path to the .bsetup project.")] string script,
        [Description("Output directory.")] string? outputDir = null,
        [Description("Update URL baked into the deployment manifest.")] string? updateUrl = null,
        [Description("Skip signing.")] bool noSign = false,
        [Description("Kill the publish after this many seconds.")] int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
        => Simple("publish", script, Flags(("/NOSIGN", noSign)),
            Values(("/OUT=", outputDir), ("/UPDATEURL=", updateUrl)), cancellationToken, timeoutSeconds);

    [McpServerTool(Name = "installer_export_winget", Title = "Export WinGet manifests", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Export WinGet manifests for a project beside its output. Pass the built installer's path or its public download URL; the manifest records its hash. MSI, MSIX, signing and update-server commands are machine-changing or publish, so they are reached through installer_run with an explicit confirmation, not here.")]
    public Task<InstallerRunResult> ExportWinget(
        [Description("Path to the .bsetup project.")] string script,
        [Description("Output directory.")] string? outputDir = null,
        [Description("Path to the built installer file.")] string? installer = null,
        [Description("Public URL the installer will be downloaded from.")] string? installerUrl = null,
        CancellationToken cancellationToken = default)
        => Simple("winget", script, null,
            Values(("/OUT=", outputDir), ("/INSTALLER=", installer), ("/INSTALLERURL=", installerUrl)), cancellationToken);

    // ── helpers ──────────────────────────────────────────────────────────────

    private Task<InstallerRunResult> Simple(
        string command,
        string? value,
        Dictionary<string, string>? flags,
        Dictionary<string, string>? values,
        CancellationToken cancellationToken,
        int? timeoutSeconds = null)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (flags is not null) foreach (var (k, v) in flags) options[k] = v;
        if (values is not null) foreach (var (k, v) in values) options[k] = v;

        return runner.RunAsync(
            new CliInvocationRequest { Command = command, Value = value, Options = options },
            timeoutSeconds, workingDirectory: null, cancellationToken);
    }

    private static Dictionary<string, string> Flags(params (string Name, bool On)[] flags)
        => flags.Where(f => f.On).ToDictionary(f => f.Name, _ => "true", StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> Values(params (string Name, string? Value)[] values)
        => values.Where(v => !string.IsNullOrWhiteSpace(v.Value)).ToDictionary(v => v.Name, v => v.Value!, StringComparer.OrdinalIgnoreCase);

}
