using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class EnterpriseCliQualificationRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public EnterpriseCliQualificationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepCliQualTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Run_GeneratesEnterpriseCliQualificationEvidence()
    {
        var projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Beep.Installer", "samples", "MyApp.bsetup"));
        var outDir = Path.Combine(_tempDir, "cli");

        var report = new EnterpriseCliQualificationRunner().Run(new EnterpriseCliQualificationOptions
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
            "json-response-expansion",
            "rsp-response-expansion",
            "modern-alias-normalization",
            "unknown-argument-fails",
            "missing-response-fails",
            "property-catalog-contract",
            "no-cli-secret-leak"
        });
        report.Scenarios.Should().OnlyContain(s => s.Success);
        File.Exists(Path.Combine(outDir, EnterpriseCliQualificationRunner.ReportFileName)).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "property-catalog.json")).Should().BeTrue();
        File.ReadAllText(Path.Combine(outDir, EnterpriseCliQualificationRunner.ReportFileName))
            .Should().NotContain("hunter2")
            .And.NotContain("abc123")
            .And.NotContain("super-secret");
    }

    [Fact]
    public void Run_FailsWhenProjectCannotLoad()
    {
        var report = new EnterpriseCliQualificationRunner().Run(new EnterpriseCliQualificationOptions
        {
            ProjectPath = Path.Combine(_tempDir, "missing.bsetup"),
            OutputDirectory = Path.Combine(_tempDir, "cli")
        });

        report.Success.Should().BeFalse();
        report.ExitCode.Should().Be(1);
        report.Scenarios.Should().ContainSingle(s => s.Id == "load-project" && !s.Success);
    }
}
