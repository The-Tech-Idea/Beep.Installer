using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Policy;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Diagnostics")]
public sealed class RuntimeTelemetryTests : IDisposable
{
    private readonly string _tempDir;

    public RuntimeTelemetryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BeepTelemetryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Emit_DisabledPolicy_DoesNotWriteOrSend()
    {
        var project = InstallerProjectFactory.CreateNew("TelemetryApp", "1.0.0", "ACME", "");
        var localPath = Path.Combine(_tempDir, "telemetry.jsonl");
        var policyEvaluation = InstallerPolicyEvaluator.EvaluateProject(new InstallerPolicy
        {
            TelemetryMode = "disabled",
            ForbidLiteralSecrets = false,
            ForbidUnsignedDrivers = false,
        }, project);

        var result = new RuntimeTelemetrySink().Emit(new RuntimeTelemetryOptions
        {
            Project = project,
            PolicyEvaluation = policyEvaluation,
            OutputPath = localPath,
            Sender = _ => throw new InvalidOperationException("sender must not be called")
        });

        result.Emitted.Should().BeFalse();
        File.Exists(localPath).Should().BeFalse();
    }

    [Fact]
    public void Emit_LocalPolicy_WritesRedactedLocalTelemetry()
    {
        var project = InstallerProjectFactory.CreateNew("TelemetryApp", "1.0.0", "ACME", "");
        var installPath = Path.Combine(_tempDir, "install");
        var logPath = Path.Combine(_tempDir, "install.log");
        var localPath = Path.Combine(_tempDir, "telemetry.jsonl");
        var policyEvaluation = InstallerPolicyEvaluator.EvaluateProject(new InstallerPolicy
        {
            TelemetryMode = "local",
            ForbidLiteralSecrets = false,
            ForbidUnsignedDrivers = false,
        }, project);
        Diag.Reset();
        using (Diag.BeginScope("install", "telemetry-correlation"))
            Diag.Warn("UnitTest", "password=hunter2", eventId: "BI2479");

        var result = new RuntimeTelemetrySink().Emit(new RuntimeTelemetryOptions
        {
            Action = "install",
            Project = project,
            InstallPath = installPath,
            Success = false,
            ExitCode = 1603,
            Message = "failed token=abc123",
            LogPath = logPath,
            PolicyEvaluation = policyEvaluation,
            CorrelationId = "telemetry-correlation",
            OutputPath = localPath
        });

        result.Emitted.Should().BeTrue();
        result.LocalWritten.Should().BeTrue();
        result.RemoteSent.Should().BeFalse();
        var json = File.ReadAllText(localPath);
        json.Should().Contain("\"mode\":\"local\"");
        json.Should().Contain("***REDACTED***");
        json.Should().Contain("BI2479");
        json.Should().NotContain("hunter2");
        json.Should().NotContain("abc123");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("productName").GetString().Should().BeEmpty();
        root.GetProperty("installPath").GetString().Should().BeEmpty();
    }

    [Fact]
    public void Emit_AnonymousPolicy_SendsOnlyWhenEndpointIsExplicit()
    {
        var project = InstallerProjectFactory.CreateNew("TelemetryApp", "1.0.0", "ACME", "");
        var policyEvaluation = InstallerPolicyEvaluator.EvaluateProject(new InstallerPolicy
        {
            TelemetryMode = "anonymous",
            TelemetryEndpoint = "https://telemetry.example.test/runtime",
            AllowedTelemetryHosts = { "telemetry.example.test" },
            ForbidLiteralSecrets = false,
            ForbidUnsignedDrivers = false,
        }, project);
        RuntimeTelemetryEnvelope? sent = null;

        var result = new RuntimeTelemetrySink().Emit(new RuntimeTelemetryOptions
        {
            Action = "repair",
            Project = project,
            Success = true,
            PolicyEvaluation = policyEvaluation,
            OutputPath = Path.Combine(_tempDir, "anonymous.jsonl"),
            Sender = envelope =>
            {
                sent = envelope;
                return new RuntimeTelemetrySendResult { Success = true, Message = "accepted" };
            }
        });

        result.RemoteSent.Should().BeTrue();
        sent.Should().NotBeNull();
        sent!.Mode.Should().Be("anonymous");
        sent.ProductName.Should().BeEmpty();
        sent.InstallPath.Should().BeEmpty();
    }
}
