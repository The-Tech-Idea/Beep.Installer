using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class SupplyChainQualificationRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public SupplyChainQualificationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepSupplyChainQualTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Run_GeneratesScannerSignatureAndReleaseEvidenceQualification()
    {
        var projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Beep.Installer", "samples", "MyApp.bsetup"));
        var outDir = Path.Combine(_tempDir, "security");
        var installerPath = Path.Combine(_tempDir, "Setup-MyApp.exe");
        File.WriteAllText(installerPath, "synthetic installer artifact");

        var report = new SupplyChainQualificationRunner().Run(new SupplyChainQualificationOptions
        {
            ProjectPath = projectPath,
            OutputDirectory = outDir,
            InstallerPath = installerPath
        });

        report.Success.Should().BeTrue(string.Join(Environment.NewLine,
            report.Scenarios.SelectMany(s => s.Diagnostics.Select(d => $"{s.Id}: {d.Code} {d.Path} {d.Message}"))));
        report.ExitCode.Should().Be(0);
        report.Scenarios.Select(s => s.Id).Should().Equal(new[]
        {
            "load-project",
            "clean-required-scanners",
            "missing-scanners-fail-closed",
            "malware-detection-fail-closed",
            "vulnerability-detection-fail-closed",
            "scanner-error-fail-closed",
            "unsigned-artifact-policy",
            "signed-artifact-policy",
            "release-evidence-scanner-summary",
            "no-security-path-leak"
        });
        report.Scenarios.Should().OnlyContain(s => s.Success);
        File.Exists(Path.Combine(outDir, SupplyChainQualificationRunner.ReportFileName)).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "release-evidence-source-scan.report.json")).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "release-evidence", "MyApp-1.0.0.provenance.json")).Should().BeTrue();
    }

    [Fact]
    public void Run_FailsWhenProjectCannotLoad()
    {
        var report = new SupplyChainQualificationRunner().Run(new SupplyChainQualificationOptions
        {
            ProjectPath = Path.Combine(_tempDir, "missing.bsetup"),
            OutputDirectory = Path.Combine(_tempDir, "security")
        });

        report.Success.Should().BeFalse();
        report.ExitCode.Should().Be(1);
        report.Scenarios.Should().ContainSingle(s => s.Id == "load-project" && !s.Success);
    }
}
