using System.Text.Json;
using Beep.Installer.Deployment;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Quality;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class DeploymentKitQualificationRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public DeploymentKitQualificationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepDeployKitQual_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Run_PassesGeneratedKitAndWarnsWhenManagedEvidenceIsNotRequired()
    {
        var project = InstallerProjectFactory.CreateNew("Service App", "2.3.4", "ACME", "");
        project.DefaultScope = InstallationScope.Machine;
        project.ArchitecturesAllowed = Architecture.X64Compatible;
        EnterpriseDeploymentKitGenerator.Generate(project, _tempDir);

        var report = new DeploymentKitQualificationRunner().Run(new DeploymentKitQualificationOptions
        {
            KitRoot = _tempDir
        });

        report.Success.Should().BeTrue();
        report.ExitCode.Should().Be(0);
        File.Exists(Path.Combine(_tempDir, DeploymentKitQualificationRunner.ReportFileName)).Should().BeTrue();
        report.Scenarios.Should().Contain(s => s.Id == "managed-device-evidence"
                                               && s.Success
                                               && s.Diagnostics.Any(d => d.Code == "BI1918"));
    }

    [Fact]
    public void Run_FailsWhenManagedEvidenceIsRequiredButMissing()
    {
        var project = InstallerProjectFactory.CreateNew("Service App", "2.3.4", "ACME", "");
        EnterpriseDeploymentKitGenerator.Generate(project, _tempDir);

        var report = new DeploymentKitQualificationRunner().Run(new DeploymentKitQualificationOptions
        {
            KitRoot = _tempDir,
            RequireManagedDeviceEvidence = true
        });

        report.Success.Should().BeFalse();
        report.ExitCode.Should().Be(1);
        report.Scenarios.Single(s => s.Id == "managed-device-evidence").Diagnostics
            .Should().Contain(d => d.Code == "BI1917");
    }

    [Fact]
    public void Run_PassesRequiredFreshManagedEvidence()
    {
        var project = InstallerProjectFactory.CreateNew("Service App", "2.3.4", "ACME", "");
        EnterpriseDeploymentKitGenerator.Generate(project, _tempDir);
        WriteEvidence(succeeded: true, DateTimeOffset.UtcNow);

        var report = new DeploymentKitQualificationRunner().Run(new DeploymentKitQualificationOptions
        {
            KitRoot = _tempDir,
            RequireManagedDeviceEvidence = true,
            MaxEvidenceAgeDays = 30
        });

        report.Success.Should().BeTrue();
        report.Scenarios.Single(s => s.Id == "managed-device-evidence").Success.Should().BeTrue();
    }

    [Fact]
    public void Run_FailsManagedEvidence_WhenProductIdentityDoesNotMatchKit()
    {
        var project = InstallerProjectFactory.CreateNew("Service App", "2.3.4", "ACME", "");
        EnterpriseDeploymentKitGenerator.Generate(project, _tempDir);
        WriteEvidence(succeeded: true, DateTimeOffset.UtcNow, productName: "Other App", productVersion: "9.9.9");

        var report = new DeploymentKitQualificationRunner().Run(new DeploymentKitQualificationOptions
        {
            KitRoot = _tempDir,
            RequireManagedDeviceEvidence = true
        });

        report.Success.Should().BeFalse();
        var diagnostics = report.Scenarios.Single(s => s.Id == "managed-device-evidence").Diagnostics;
        diagnostics.Should().Contain(d => d.Code == "BI1926");
        diagnostics.Should().Contain(d => d.Code == "BI1927");
    }

    [Fact]
    public void Run_FailsManagedEvidence_WhenActionExitCodeIsOutsideExpectedCodes()
    {
        var project = InstallerProjectFactory.CreateNew("Service App", "2.3.4", "ACME", "");
        EnterpriseDeploymentKitGenerator.Generate(project, _tempDir);
        WriteEvidence(succeeded: true, DateTimeOffset.UtcNow, repairExitCode: 1603, repairExpectedExitCode: 0);

        var report = new DeploymentKitQualificationRunner().Run(new DeploymentKitQualificationOptions
        {
            KitRoot = _tempDir,
            RequireManagedDeviceEvidence = true
        });

        report.Success.Should().BeFalse();
        report.Scenarios.Single(s => s.Id == "managed-device-evidence").Diagnostics
            .Should().Contain(d => d.Code == "BI1931" && d.Path.Contains("repair", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Run_FailsInlineSecretAssignment()
    {
        var project = InstallerProjectFactory.CreateNew("Service App", "2.3.4", "ACME", "");
        EnterpriseDeploymentKitGenerator.Generate(project, _tempDir);
        File.AppendAllText(Path.Combine(_tempDir, "Intune", "README.md"), Environment.NewLine + "token: leaked-value");

        var report = new DeploymentKitQualificationRunner().Run(new DeploymentKitQualificationOptions
        {
            KitRoot = _tempDir
        });

        report.Success.Should().BeFalse();
        report.Scenarios.Single(s => s.Id == "secret-scan").Diagnostics
            .Should().Contain(d => d.Code == "BI1924" && d.Path.EndsWith("README.md", StringComparison.OrdinalIgnoreCase));
    }

    private void WriteEvidence(
        bool succeeded,
        DateTimeOffset finishedAt,
        string productName = "Service App",
        string productVersion = "2.3.4",
        int repairExitCode = 0,
        int repairExpectedExitCode = 0)
    {
        var report = new
        {
            schemaVersion = "1.0",
            productName,
            productVersion,
            machineName = "TEST-CLIENT",
            userName = "TEST\\Admin",
            startedAt = finishedAt.AddMinutes(-5).ToString("O"),
            finishedAt = finishedAt.ToString("O"),
            succeeded,
            actions = new[]
            {
                Action("install", 0, 0),
                Action("detectAfterInstall", 0, 0),
                Action("repair", repairExitCode, repairExpectedExitCode),
                Action("uninstall", 0, 0),
                Action("detectAfterUninstall", 1, 1)
            }
        };
        var path = Path.Combine(_tempDir, "ManagedDeviceEvidence", "deployment-evidence.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object Action(string name, int exitCode, int expectedExitCode)
        => new
        {
            name,
            script = name + ".ps1",
            exitCode,
            expectedExitCodes = new[] { expectedExitCode },
            succeeded = true,
            startedAt = DateTimeOffset.UtcNow.AddSeconds(-5).ToString("O"),
            finishedAt = DateTimeOffset.UtcNow.ToString("O"),
            error = (string?)null
        };
}
