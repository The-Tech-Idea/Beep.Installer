using Beep.Installer.Engine;
using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class UpgradeQualificationRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public UpgradeQualificationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepUpgradeQualTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Run_GeneratesUpgradeDowngradeAndRestoreEvidence()
    {
        var scriptPath = Path.Combine(_tempDir, "ServiceApp.bsetup");
        var project = InstallerProjectFactory.CreateNew("Service App", "2.3.4", "ACME", "");
        InstallerScriptSerializer.Save(project, scriptPath);
        var outDir = Path.Combine(_tempDir, "evidence");

        var report = new UpgradeQualificationRunner().Run(new UpgradeQualificationOptions
        {
            ProjectPath = scriptPath,
            OutputDirectory = outDir
        });

        report.Success.Should().BeTrue();
        report.ExitCode.Should().Be(0);
        report.ProductName.Should().Be("Service App");
        report.ProductVersion.Should().Be("2.3.4");
        report.Scenarios.Select(s => s.Id).Should().Contain(new[]
        {
            "fresh-install",
            "same-version-maintenance",
            "upgrade-backup",
            "downgrade-blocked",
            "forced-downgrade-backup",
            "missing-owned-file-repair",
            "corrupt-owned-file-repair",
            "commit-preserves-user-files",
            "failed-upgrade-restore"
        });
        report.Scenarios.Should().OnlyContain(s => s.Success);
        File.Exists(Path.Combine(outDir, UpgradeQualificationRunner.ReportFileName)).Should().BeTrue();
    }

    [Fact]
    public void Run_FailsMissingProject()
    {
        var report = new UpgradeQualificationRunner().Run(new UpgradeQualificationOptions
        {
            ProjectPath = Path.Combine(_tempDir, "missing.bsetup"),
            OutputDirectory = Path.Combine(_tempDir, "missing-evidence")
        });

        report.Success.Should().BeFalse();
        report.ExitCode.Should().Be(1);
        report.Scenarios.Single(s => s.Id == "load-project").Diagnostics
            .Should().Contain(d => d.Code == "BI1301");
    }
}
