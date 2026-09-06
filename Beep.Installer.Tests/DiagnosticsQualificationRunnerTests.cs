using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Diagnostics")]
public sealed class DiagnosticsQualificationRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public DiagnosticsQualificationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepDiagnosticsQualTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Run_GeneratesSupportBundleDiagnosticAndTelemetryQualification()
    {
        var projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Beep.Installer", "samples", "MyApp.bsetup"));
        var outDir = Path.Combine(_tempDir, "diagnostics");

        var report = new DiagnosticsQualificationRunner().Run(new DiagnosticsQualificationOptions
        {
            ProjectPath = projectPath,
            OutputDirectory = outDir
        });

        report.Success.Should().BeTrue(string.Join(Environment.NewLine,
            report.Scenarios.SelectMany(s => s.Diagnostics.Select(d => $"{s.Id}: {d.Code} {d.Path} {d.Message}"))));
        report.ExitCode.Should().Be(0);
        report.Scenarios.Select(s => s.Id).Should().Equal(new[]
        {
            "load-project",
            "support-bundle-redaction",
            "preview-retention",
            "telemetry-disabled",
            "telemetry-local",
            "telemetry-anonymous-remote",
            "telemetry-full-remote",
            "no-diagnostics-secret-leak"
        });
        report.Scenarios.Should().OnlyContain(s => s.Success);
        File.Exists(Path.Combine(outDir, DiagnosticsQualificationRunner.ReportFileName)).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "support-redaction-support-bundle.json")).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "telemetry-local.jsonl")).Should().BeTrue();
        File.ReadAllText(Path.Combine(outDir, DiagnosticsQualificationRunner.ReportFileName))
            .Should().NotContain("hunter2")
            .And.NotContain("abc123")
            .And.NotContain("super-secret")
            .And.NotContain("journal-secret");
    }

    [Fact]
    public void Run_FailsWhenProjectCannotLoad()
    {
        var report = new DiagnosticsQualificationRunner().Run(new DiagnosticsQualificationOptions
        {
            ProjectPath = Path.Combine(_tempDir, "missing.bsetup"),
            OutputDirectory = Path.Combine(_tempDir, "diagnostics")
        });

        report.Success.Should().BeFalse();
        report.ExitCode.Should().Be(1);
        report.Scenarios.Should().ContainSingle(s => s.Id == "load-project" && !s.Success);
    }
}
