using System;
using System.IO;
using System.Linq;
using Beep.Installer.Composition;
using Beep.Installer.Engine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TheTechIdea.Beep.Services.Logging;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// The shell's composition root (5.A.2).
///
/// The point of it is not DI for its own sake — it is that the installer's ~125 existing
/// <c>Diag</c> call sites start flowing through Beep's telemetry pipeline, which redacts, rolls and
/// budgets what a hand-rolled <c>File.AppendAllText</c> into <c>%TEMP%</c> never did.
/// </summary>
[Collection("Diagnostics")]
public sealed class CompositionRootTests : IDisposable
{
    private readonly string _logDirectory;

    public CompositionRootTests()
    {
        _logDirectory = Path.Combine(Path.GetTempPath(), $"BeepComposition_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_logDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_logDirectory, recursive: true); } catch { /* temp dir, best effort */ }
    }

    private string LogText()
        => string.Concat(Directory.GetFiles(_logDirectory, "*", SearchOption.AllDirectories)
            .Select(file =>
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }));

    [Fact]
    public void StructuredLogging_IsRegisteredAndEnabled()
    {
        var provider = InstallerServices.Build(Array.Empty<string>(), _logDirectory);
        try
        {
            var log = provider.GetService<IBeepLog>();

            log.Should().NotBeNull();
            log!.IsEnabled.Should().BeTrue("the shell opts logging in; BeepLoggingOptions.Enabled defaults to false");
        }
        finally { InstallerServices.Shutdown(provider); }
    }

    [Fact]
    public void DiagnosticsFromExistingCallSites_ReachThePipeline()
    {
        var provider = InstallerServices.Build(Array.Empty<string>(), _logDirectory);
        var marker = "composition-" + Guid.NewGuid().ToString("N");
        try
        {
            using (InstallerServices.RouteDiagnostics(provider))
                Diag.Warn("Composition", marker);
        }
        finally { InstallerServices.Shutdown(provider); }

        // Shutdown flushes, so the entry must be on disk by the time it returns.
        LogText().Should().Contain(marker, "an unchanged Diag call site should land in the pipeline's sink");
    }

    [Fact]
    public void SecretsInDiagnostics_AreRedactedOnTheWayToDisk()
    {
        var provider = InstallerServices.Build(Array.Empty<string>(), _logDirectory);
        const string secret = "Password=SuperSecret123!";
        try
        {
            // The installer handles signing passwords, API keys and connection strings. Diag used
            // to append them verbatim to a file in %TEMP%.
            using (InstallerServices.RouteDiagnostics(provider))
                Diag.Warn("Signing", $"connect using {secret}");
        }
        finally { InstallerServices.Shutdown(provider); }

        var written = LogText();
        // Assert the entry landed before asserting what it does not contain, so this cannot pass
        // simply because nothing reached the sink.
        written.Should().Contain("Signing", "the entry itself has to reach the sink");
        written.Should().NotContain("SuperSecret123!", "the balanced redaction preset must scrub credentials");
    }

    [Fact]
    public void TheControllerIsResolvable_RatherThanNewedByHand()
    {
        var provider = InstallerServices.Build(Array.Empty<string>(), _logDirectory);
        try
        {
            provider.GetService<InstallerController>().Should().NotBeNull();
        }
        finally { InstallerServices.Shutdown(provider); }
    }

    [Fact]
    public void Shutdown_DoesNotThrow_EvenThoughThePipelineIsAsyncDisposableOnly()
    {
        // A plain `using` on the provider throws InvalidOperationException here: TelemetryPipeline
        // implements only IAsyncDisposable. That would end every CLI invocation in a crash.
        var provider = InstallerServices.Build(Array.Empty<string>(), _logDirectory);

        var shutdown = () => InstallerServices.Shutdown(provider);

        shutdown.Should().NotThrow();
    }

    [Fact]
    public void RouteDiagnostics_RestoresTheLocalSink_WhenTheHandleIsDisposed()
    {
        var provider = InstallerServices.Build(Array.Empty<string>(), _logDirectory);
        var marker = "after-" + Guid.NewGuid().ToString("N");
        try
        {
            using (InstallerServices.RouteDiagnostics(provider)) { }
            Diag.Reset();
            Diag.Warn("Composition", marker);

            Diag.Recent.Should().Contain(e => e.Message == marker,
                "diagnostics keep working once the pipeline is detached");
        }
        finally { InstallerServices.Shutdown(provider); }
    }
}
