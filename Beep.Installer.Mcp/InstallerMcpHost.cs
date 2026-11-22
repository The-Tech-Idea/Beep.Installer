using Beep.Installer.Core.Cli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Beep.Installer.Mcp;

/// <summary>
/// The one owner of "run the installer as an MCP server". <c>Beep.Installer.exe /MCP</c> calls it, so the installer
/// itself is the server: stdout carries only the MCP protocol and every log line goes to stderr.
/// </summary>
public static class InstallerMcpHost
{
    /// <summary>The switch that starts the server from the installer's command line.</summary>
    public const string CommandLineSwitch = "/MCP";

    /// <summary>Serves MCP over stdio until the client closes it. <paramref name="installerExecutable"/> is the exe the tools run commands through.</summary>
    public static async Task RunAsync(string installerExecutable, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerExecutable);

        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddSingleton(new InstallerExecutable(installerExecutable));
        builder.Services.AddSingleton(CliVerbCatalog.Default);
        builder.Services.AddSingleton<InstallerCommandRunner>();
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<InstallerTools>()
            .WithResources<InstallerResources>()
            .WithPrompts<InstallerPrompts>();

        await builder.Build().RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
