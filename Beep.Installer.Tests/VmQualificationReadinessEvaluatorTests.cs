using System.Text.Json;
using Beep.Installer.Engine.Msi;
using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class VmQualificationReadinessEvaluatorTests : IDisposable
{
    private readonly string _root;

    public VmQualificationReadinessEvaluatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "BeepVmReadiness_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Evaluate_Passes_WhenRequiredTargetsScenariosFreshGreenAndNegativeEvidenceExist()
    {
        var evidence = Path.Combine(_root, "evidence");
        WriteMatrixReport(evidence, "win11-x64", success: true, completedUtc: DateTimeOffset.UtcNow.AddHours(-1),
            scenarios: new[] { "msi-lifecycle" },
            actions: new[] { "msi-install", "msi-repair", "msi-downgrade-blocked", "msi-uninstall" });

        var report = new VmQualificationReadinessEvaluator().Evaluate(new VmQualificationReadinessOptions
        {
            EvidenceRoot = evidence,
            OutputDirectory = Path.Combine(_root, "out"),
            RequiredTargets = new[] { new VmQualificationTarget("win11-x64", "Windows 11 24H2", "x64", "hyperv") },
            RequiredScenarios = new[] { "msi-lifecycle" },
            MaxEvidenceAgeDays = 7,
            MaxFlakyFailuresPerTarget = 0,
            MaxDurationSeconds = 600,
            RequireNegativeSecurityEvidence = true
        });

        report.Success.Should().BeTrue();
        report.ExitCode.Should().Be(0);
        report.Cells.Should().ContainSingle(c => c.EnvironmentId == "win11-x64" && c.Success);
        File.Exists(report.ReportPath).Should().BeTrue();
        File.Exists(report.EvidenceRunbookPath).Should().BeTrue();
        File.Exists(report.EvidenceManifestPath).Should().BeTrue();
        File.ReadAllText(report.EvidenceRunbookPath).Should().Contain("win11-x64");
        File.ReadAllText(report.EvidenceRunbookPath).Should().Contain("msi-lifecycle");
        using var manifest = JsonDocument.Parse(File.ReadAllText(report.EvidenceManifestPath));
        manifest.RootElement.GetProperty("RequiredTargets").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("EnvironmentId").GetString().Should().Be("win11-x64");
    }

    [Fact]
    public void Evaluate_FailsClosed_ForMissingTargetStaleEvidenceAndFlakyFailures()
    {
        var evidence = Path.Combine(_root, "evidence-fail");
        WriteMatrixReport(Path.Combine(evidence, "old"), "win10-x64", success: false, completedUtc: DateTimeOffset.UtcNow.AddDays(-3),
            scenarios: new[] { "msi-lifecycle" },
            actions: new[] { "msi-install" });
        WriteMatrixReport(Path.Combine(evidence, "latest"), "win10-x64", success: true, completedUtc: DateTimeOffset.UtcNow.AddDays(-2),
            scenarios: new[] { "msi-lifecycle" },
            actions: new[] { "msi-install" });

        var report = new VmQualificationReadinessEvaluator().Evaluate(new VmQualificationReadinessOptions
        {
            EvidenceRoot = evidence,
            OutputDirectory = Path.Combine(_root, "out-fail"),
            RequiredTargets = new[]
            {
                new VmQualificationTarget("win10-x64", "Windows 10 22H2", "x64", "hyperv"),
                new VmQualificationTarget("server2025", "Windows Server 2025", "x64", "azure")
            },
            RequiredScenarios = new[] { "msi-lifecycle", "tampered-package-negative" },
            MaxEvidenceAgeDays = 1,
            MaxFlakyFailuresPerTarget = 0,
            RequireNegativeSecurityEvidence = true
        });

        report.Success.Should().BeFalse();
        report.Diagnostics.Should().Contain(d => d.Code == "BI2802" && d.Path.Contains("server2025", StringComparison.Ordinal));
        report.Diagnostics.Should().Contain(d => d.Code == "BI2811" && d.Path == "win10-x64");
        report.Diagnostics.Should().Contain(d => d.Code == "BI2812" && d.Path == "win10-x64");
        report.Diagnostics.Should().Contain(d => d.Code == "BI2814" && d.Path.Contains("tampered-package-negative", StringComparison.Ordinal));
        report.Diagnostics.Should().Contain(d => d.Code == "BI2815" && d.Path == "win10-x64");
        File.Exists(report.EvidenceRunbookPath).Should().BeTrue();
        File.ReadAllText(report.EvidenceRunbookPath).Should().Contain("server2025");
    }

    private static void WriteMatrixReport(string directory, string environmentId, bool success, DateTimeOffset completedUtc, IReadOnlyList<string> scenarios, IReadOnlyList<string> actions)
    {
        Directory.CreateDirectory(directory);
        var report = new MsiLifecycleMatrixRunnerReport
        {
            EnvironmentId = environmentId,
            OperatingSystem = environmentId.StartsWith("win10", StringComparison.OrdinalIgnoreCase) ? "Windows 10 22H2" : "Windows 11 24H2",
            Architecture = "x64",
            Channel = "hyperv",
            LogDirectory = directory,
            StartedUtc = completedUtc.AddSeconds(-120),
            CompletedUtc = completedUtc,
            Success = success,
            ExitCode = success ? 0 : 1603,
            Message = success ? "Matrix qualification completed." : "Matrix qualification failed.",
            Scenarios = scenarios.Select(id => new MsiLifecycleMatrixScenarioEvidence
            {
                Id = id,
                Category = "lifecycle",
                Description = id,
                Actions = actions.ToList(),
                Success = success
            }).ToList(),
            Actions = actions.Select(name => new MsiLifecycleMatrixRunnerAction
            {
                Name = name,
                ScenarioId = scenarios[0],
                ScenarioCategory = "lifecycle",
                ArtifactPath = "app.msi",
                LogPath = Path.Combine(directory, name + ".log"),
                CommandLine = "msiexec " + name,
                ToolVersion = "test",
                ExitCode = success ? 0 : 1603,
                Success = success
            }).ToList()
        };

        File.WriteAllText(
            Path.Combine(directory, MsiLifecycleMatrixRunner.ReportFileName),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
