using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class ConfigTransformQualificationRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigTransformQualificationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepConfigQualTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Run_GeneratesConfigTransformQualificationEvidence()
    {
        var projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Beep.Installer", "samples", "ServiceApp.bsetup"));
        var outDir = Path.Combine(_tempDir, "config");

        var report = new ConfigTransformQualificationRunner().Run(new ConfigTransformQualificationOptions
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
            "project-transform-compilation",
            "json-apply-verify-rollback",
            "xml-apply-verify-rollback",
            "ini-apply-verify-rollback",
            "conflict-diagnostics",
            "secret-diagnostics",
            "unresolved-secret-fails-before-mutation",
            "no-config-secret-leak"
        });
        report.Scenarios.Should().OnlyContain(s => s.Success);
        File.Exists(Path.Combine(outDir, ConfigTransformQualificationRunner.ReportFileName)).Should().BeTrue();
        File.ReadAllText(Path.Combine(outDir, ConfigTransformQualificationRunner.ReportFileName))
            .Should().NotContain("hunter2")
            .And.NotContain("abc123")
            .And.NotContain("super-secret")
            .And.NotContain("open-sesame");
    }

    [Fact]
    public void Run_FailsWhenProjectCannotLoad()
    {
        var report = new ConfigTransformQualificationRunner().Run(new ConfigTransformQualificationOptions
        {
            ProjectPath = Path.Combine(_tempDir, "missing.bsetup"),
            OutputDirectory = Path.Combine(_tempDir, "config")
        });

        report.Success.Should().BeFalse();
        report.ExitCode.Should().Be(1);
        report.Scenarios.Should().ContainSingle(s => s.Id == "load-project" && !s.Success);
    }
}
