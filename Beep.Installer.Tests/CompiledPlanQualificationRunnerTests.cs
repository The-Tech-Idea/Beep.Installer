using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class CompiledPlanQualificationRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public CompiledPlanQualificationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepPlanQualTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Run_GeneratesCompiledPlanProviderCoverageEvidence()
    {
        var projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Beep.Installer", "samples", "ServiceApp.bsetup"));
        var outDir = Path.Combine(_tempDir, "plan");

        var report = new CompiledPlanQualificationRunner().Run(new CompiledPlanQualificationOptions
        {
            ProjectPath = projectPath,
            OutputDirectory = outDir
        });

        report.Success.Should().BeTrue(string.Join(Environment.NewLine,
            report.Scenarios.SelectMany(s => s.Diagnostics.Select(d => $"{s.Id}: {d.Code} {d.Path} {d.Message}"))));
        report.ExitCode.Should().Be(0);
        report.PlanHash.Should().NotBeNullOrWhiteSpace();
        report.OperationTypes.Should().Contain(new[]
        {
            "component.select",
            "file.copy",
            "service.install",
            "scheduled-task.create",
            "firewall.rule",
            "file-association.register",
            "com.register",
            "driver.package",
            "config.transform",
            "iis.appPool",
            "iis.site",
            "registry.write"
        });
        report.Scenarios.Select(s => s.Id).Should().Equal(new[]
        {
            "load-project",
            "compile-plan",
            "deterministic-plan-hash",
            "provider-coverage",
            "dependency-integrity",
            "plan-json-redaction",
            "default-provider-registry"
        });
        File.Exists(Path.Combine(outDir, CompiledPlanQualificationRunner.ReportFileName)).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "compiled-plan.json")).Should().BeTrue();
    }

    [Fact]
    public void Run_FailsWhenProjectCannotLoad()
    {
        var report = new CompiledPlanQualificationRunner().Run(new CompiledPlanQualificationOptions
        {
            ProjectPath = Path.Combine(_tempDir, "missing.bsetup"),
            OutputDirectory = Path.Combine(_tempDir, "plan")
        });

        report.Success.Should().BeFalse();
        report.ExitCode.Should().Be(1);
        report.Scenarios.Should().ContainSingle(s => s.Id == "load-project" && !s.Success);
    }
}
