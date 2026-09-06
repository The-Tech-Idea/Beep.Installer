using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class ReleaseEvidenceQualificationRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public ReleaseEvidenceQualificationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepEvidenceQualTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Run_GeneratesVerificationAttestationAndTamperQualificationEvidence()
    {
        var projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Beep.Installer", "samples", "ServiceApp.bsetup"));
        var outDir = Path.Combine(_tempDir, "evidence");
        var installerPath = Path.Combine(_tempDir, "Setup-ServiceApp.exe");
        File.WriteAllText(installerPath, "synthetic installer artifact");

        var report = new ReleaseEvidenceQualificationRunner().Run(new ReleaseEvidenceQualificationOptions
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
            "generate-evidence",
            "verify-generated-evidence",
            "verify-dsse-attestations",
            "tamper-detection",
            "no-local-path-leak"
        });
        report.Scenarios.Should().OnlyContain(s => s.Success);
        File.Exists(Path.Combine(outDir, ReleaseEvidenceQualificationRunner.ReportFileName)).Should().BeTrue();
        Directory.EnumerateFiles(Path.Combine(outDir, "signed"), "*.dsse.json").Should().HaveCount(2);
    }

    [Fact]
    public void Run_FailsWhenProjectCannotLoad()
    {
        var report = new ReleaseEvidenceQualificationRunner().Run(new ReleaseEvidenceQualificationOptions
        {
            ProjectPath = Path.Combine(_tempDir, "missing.bsetup"),
            OutputDirectory = Path.Combine(_tempDir, "evidence")
        });

        report.Success.Should().BeFalse();
        report.ExitCode.Should().Be(1);
        report.Scenarios.Should().ContainSingle(s => s.Id == "load-project" && !s.Success);
    }
}
