using System.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class RecoveryQualificationRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public RecoveryQualificationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepRecoveryQualTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Run_GeneratesJournalRecoveryQualificationEvidence()
    {
        var scriptPath = Path.Combine(_tempDir, "ServiceApp.bsetup");
        var project = InstallerProjectFactory.CreateNew("Service App", "2.3.4", "ACME", "");
        InstallerScriptSerializer.Save(project, scriptPath);
        var outDir = Path.Combine(_tempDir, "evidence");

        var report = new RecoveryQualificationRunner().Run(new RecoveryQualificationOptions
        {
            ProjectPath = scriptPath,
            OutputDirectory = outDir
        });

        // Name the scenario that failed: a bare "expected True" says nothing about which of the
        // eight recovery scenarios regressed.
        report.Success.Should().BeTrue(string.Join("; ",
            report.Scenarios.Where(s => !s.Success).Select(s => $"{s.Id}: {s.Message}")));
        report.ExitCode.Should().Be(0);
        report.ProductName.Should().Be("Service App");
        report.ProductVersion.Should().Be("2.3.4");
        report.Scenarios.Select(s => s.Id).Should().Contain(new[]
        {
            "load-project",
            "compile-plan",
            "atomic-save-load",
            "load-failure-statuses",
            "metadata-mismatch-warning",
            "pending-rollback-selection",
            "abandon-archives-journal",
            "secret-free-journal"
        });
        report.Scenarios.Should().OnlyContain(s => s.Success);
        File.Exists(Path.Combine(outDir, RecoveryQualificationRunner.ReportFileName)).Should().BeTrue();
    }

    [Fact]
    public void Run_FailsMissingProject()
    {
        var report = new RecoveryQualificationRunner().Run(new RecoveryQualificationOptions
        {
            ProjectPath = Path.Combine(_tempDir, "missing.bsetup"),
            OutputDirectory = Path.Combine(_tempDir, "missing-evidence")
        });

        report.Success.Should().BeFalse();
        report.ExitCode.Should().Be(1);
        report.Scenarios.Single(s => s.Id == "load-project").Diagnostics
            .Should().Contain(d => d.Code == "BI1201");
    }
}
