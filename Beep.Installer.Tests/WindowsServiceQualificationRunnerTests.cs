using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class WindowsServiceQualificationRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public WindowsServiceQualificationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepSvcQualTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Run_GeneratesWindowsServiceProviderQualificationEvidence()
    {
        var scriptPath = Path.Combine(_tempDir, "ServiceApp.bsetup");
        var project = InstallerProjectFactory.CreateNew("Service App", "2.3.4", "ACME", "");
        project.WindowsServices.Add(new WindowsServiceDefinition
        {
            Name = "ServiceApp",
            DisplayName = "Service App Worker",
            Description = "Runs background work.",
            ExecutablePath = @"{app}\ServiceApp.exe",
            Arguments = "--service",
            StartMode = WindowsServiceStartMode.DelayedAuto,
            StartAfterInstall = true,
            StopOnUninstall = true,
            Account = WindowsServiceAccount.LocalSystem,
            DependsOn = new List<string> { "EventLog" },
            FailureRestartDelaySeconds = 60
        });
        InstallerScriptSerializer.Save(project, scriptPath);
        var outDir = Path.Combine(_tempDir, "evidence");

        var report = new WindowsServiceQualificationRunner().Run(new WindowsServiceQualificationOptions
        {
            ProjectPath = scriptPath,
            OutputDirectory = outDir
        });

        report.Success.Should().BeTrue();
        report.ExitCode.Should().Be(0);
        report.Scenarios.Select(s => s.Id).Should().Contain(new[]
        {
            "load-project",
            "compiled-service-operations",
            "create-command",
            "update-rollback",
            "secret-boundary",
            "no-secret-leak"
        });
        report.Scenarios.Should().OnlyContain(s => s.Success);
        File.Exists(Path.Combine(outDir, WindowsServiceQualificationRunner.ReportFileName)).Should().BeTrue();
    }

    [Fact]
    public void Run_FailsWhenProjectHasNoServices()
    {
        var scriptPath = Path.Combine(_tempDir, "NoServices.bsetup");
        var project = InstallerProjectFactory.CreateNew("No Services", "1.0.0", "ACME", "");
        InstallerScriptSerializer.Save(project, scriptPath);

        var report = new WindowsServiceQualificationRunner().Run(new WindowsServiceQualificationOptions
        {
            ProjectPath = scriptPath,
            OutputDirectory = Path.Combine(_tempDir, "no-services-evidence")
        });

        report.Success.Should().BeFalse();
        report.Scenarios.Single(s => s.Id == "compiled-service-operations").Diagnostics
            .Should().Contain(d => d.Code == "BI0602");
    }
}
