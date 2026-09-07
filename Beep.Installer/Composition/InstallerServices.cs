using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Beep.Installer.Engine;
using TheTechIdea.Beep.Services;
using TheTechIdea.Beep.Services.Logging;
using TheTechIdea.Beep.Services.Telemetry;
using TheTechIdea.Beep.Services.Telemetry.Redaction;
using TheTechIdea.Beep.Services.Telemetry.Sinks;

namespace Beep.Installer.Composition;

/// <summary>
/// The shell's composition root.
///
/// It registers Beep's structured logging pipeline and hands it to <see cref="Diag"/>, which is
/// what makes this worth doing: the installer's ~125 diagnostic call sites keep their existing
/// shape and start flowing through enrichment, redaction, rotation, retention and a storage budget
/// instead of being appended raw to a file in <c>%TEMP%</c> that nothing ever prunes.
///
/// Redaction is the part that matters most here. This process handles signing passwords, API keys
/// and connection strings; <see cref="DefaultRedactionPresets.LogsBalanced"/> scrubs those on the
/// way to disk, which hand-rolled <c>File.AppendAllText</c> never did.
///
/// Deliberately narrow: no <c>AddBeepServices()</c>. That builds an <c>IBeepService</c> with a
/// DMEEditor, datasource drivers and assembly discovery — the installer authors <c>.bsetup</c>
/// files and copies payloads, and uses none of it. Registering it to satisfy a design document
/// would buy startup cost and an <c>%AppData%</c> footprint on end-user machines for nothing.
/// </summary>
internal static class InstallerServices
{
    /// <summary>Log file prefix; the sink appends its own rolling suffix and <c>.ndjson</c>.</summary>
    private const string LogPrefix = "beep-installer";

    /// <param name="logDirectory">
    /// Where the rolling log is written. Defaults to Beep's standard per-user logs directory;
    /// tests pass a temporary path so composing the shell does not write to the developer's
    /// profile.
    /// </param>
    public static ServiceProvider Build(string[] args, string? logDirectory = null)
    {
        var services = new ServiceCollection();

        services.AddBeepLogging(options =>
        {
            options.Enabled = true;
            options.MinLevel = BeepLogLevel.Debug;
            options.Sinks.Add(new FileRollingSink(
                logDirectory ?? PlatformPaths.LogsDir("BeepInstaller"), prefix: LogPrefix));
            foreach (var redactor in DefaultRedactionPresets.LogsBalanced())
                options.Redactors.Add(redactor);
        });

        // The two engine services the shell used to new up by hand.
        services.AddSingleton<InstallerController>();

        // The raw command line, so a form that needs it can be resolved rather than constructed.
        services.AddSingleton(args);
        services.AddTransient<Forms.PackageBuilderForm>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Points <see cref="Diag"/> at the pipeline for the lifetime of the returned handle. Returns
    /// null when logging could not be composed — diagnostics keep working through the local ring
    /// and file sink, because a logging pipeline is never a reason to fail an install.
    /// </summary>
    public static IDisposable? RouteDiagnostics(IServiceProvider provider)
    {
        try
        {
            var log = provider.GetService<IBeepLog>();
            return log is null ? null : Diag.UseLog(log);
        }
        catch (Exception ex)
        {
            Diag.Debug("Composition", "structured logging unavailable; using the local sink only", ex);
            return null;
        }
    }

    /// <summary>
    /// Drains the pipeline and tears the container down on the way out.
    ///
    /// Two things make this fussier than a <c>using</c>. Sinks batch, so without an explicit flush
    /// the last entries of a short headless run — exactly the ones describing why it failed — never
    /// reach disk. And <c>TelemetryPipeline</c> implements only <see cref="IAsyncDisposable"/>, so
    /// the container's synchronous <c>Dispose</c> throws <c>InvalidOperationException</c> rather
    /// than disposing it; every CLI invocation would end in an unhandled exception. The disposal is
    /// pumped through the thread pool so it cannot deadlock against a UI synchronization context
    /// left behind by <c>Application.Run</c>.
    /// </summary>
    public static void Shutdown(ServiceProvider provider)
    {
        try
        {
            provider.GetService<IBeepLog>()?.FlushAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Diag.Debug("Composition", "log flush on shutdown failed", ex);
        }

        try
        {
            Task.Run(async () => await provider.DisposeAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Diag.Debug("Composition", "service provider disposal failed", ex);
        }
    }
}
