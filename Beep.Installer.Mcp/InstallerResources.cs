using System.ComponentModel;
using Beep.Installer.Core.Cli;
using ModelContextProtocol.Server;

namespace Beep.Installer.Mcp;

/// <summary>Read-only reference material a client can attach without spending a tool call.</summary>
[McpServerResourceType]
public sealed class InstallerResources(InstallerCommandRunner runner)
{
    [McpServerResource(UriTemplate = "beep-installer://catalog", Name = "Beep.Installer command catalog", MimeType = "application/json"),
     Description("Every command, option, effect and MCP exposure, as JSON — the same data /CATALOG /JSON prints.")]
    public string CatalogJson() => runner.Catalog.ToJson();

    [McpServerResource(UriTemplate = "beep-installer://catalog.md", Name = "Beep.Installer command reference", MimeType = "text/markdown"),
     Description("The command catalog as Markdown tables, one per category.")]
    public string CatalogMarkdown() => runner.Catalog.ToMarkdown();

    [McpServerResource(UriTemplate = "beep-installer://guide", Name = "Beep.Installer MCP guide", MimeType = "text/plain"),
     Description("How to use the installer tools safely.")]
    public string Guide() =>
        "Beep.Installer builds installers from a .bsetup project. Start with installer_validate, then installer_plan, then installer_build. " +
        "installer_list_commands / installer_describe_command show every command; installer_run reaches all of them. " +
        "Commands that install, uninstall, repair or update this machine are refused unless dryRun=true (preview, where supported) or allowMachineChanges=true. " +
        "Credentials are never accepted as values: pass secret://env/NAME or env:NAME, resolved from the server's environment. " +
        "Exit codes: 0 success, 3010 success but reboot required, 1 the operation failed, 2 the command line was rejected. " +
        "This server is Beep.Installer.exe /MCP: it runs commands through the same executable.";
}
