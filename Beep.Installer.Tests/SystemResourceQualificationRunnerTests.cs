using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class SystemResourceQualificationRunnerTests : IDisposable
{
    private const string Thumbprint = "00112233445566778899AABBCCDDEEFF00112233";
    private readonly string _tempDir;

    public SystemResourceQualificationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepSystemQualTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Run_GeneratesSystemResourceProviderQualificationEvidence()
    {
        var scriptPath = Path.Combine(_tempDir, "SystemApp.bsetup");
        var project = InstallerProjectFactory.CreateNew("System App", "2.3.4", "ACME", "");
        project.ScheduledTasks.Add(new ScheduledTaskDefinition
        {
            Name = "ACME\\SystemApp Maintenance",
            ExecutablePath = @"{app}\SystemApp.exe",
            Arguments = "--maintain",
            WorkingDirectory = @"{app}",
            Trigger = ScheduledTaskTrigger.Daily,
            StartTime = "02:15",
            Enabled = true,
            RunElevated = true,
            Username = "SYSTEM",
            StopOnUninstall = true
        });
        project.FirewallRules.Add(new FirewallRuleDefinition
        {
            Name = "ACME SystemApp API",
            Direction = FirewallRuleDirection.In,
            Action = FirewallRuleAction.Allow,
            Protocol = FirewallRuleProtocol.Tcp,
            LocalPort = "443",
            Program = @"{app}\SystemApp.exe",
            Profile = "domain,private",
            Enabled = true,
            RemoveOnUninstall = true
        });
        project.FileAssociations.Add(new FileAssociationDefinition
        {
            Extension = ".systemapp",
            ProgId = "ACME.SystemApp.Document",
            Description = "SystemApp document",
            ExecutablePath = @"{app}\SystemApp.exe",
            Arguments = "open \"%1\"",
            IconPath = @"{app}\SystemApp.exe,0",
            ContentType = "application/x-systemapp",
            PerceivedType = "document",
            Verb = "open",
            VerbDisplayName = "Open with SystemApp"
        });
        project.Certificates.Add(new CertificateDefinition
        {
            SourcePath = @"certs\root.cer",
            StoreName = "Root",
            StoreLocation = InstallationScope.User,
            Thumbprint = Thumbprint,
            FriendlyName = "ACME Test Root",
            RemoveOnUninstall = true
        });
        project.ComRegistrations.Add(new ComRegistrationDefinition
        {
            Clsid = "{00112233-4455-6677-8899-AABBCCDDEEFF}",
            ProgId = "ACME.SystemApp.Automation.1",
            VersionIndependentProgId = "ACME.SystemApp.Automation",
            Description = "SystemApp automation object",
            ServerPath = @"{app}\SystemApp.Automation.dll",
            ServerType = ComServerType.InProc,
            ThreadingModel = "Both",
            Version = "1.0"
        });
        project.DriverPackages.Add(new DriverPackageDefinition
        {
            Name = "ACME Virtual Device",
            Kind = DriverPackageKind.Pnp,
            InfPath = @"{app}\Drivers\acmevirt.inf",
            HardwareId = @"ROOT\ACMEVIRT",
            ClassName = "System",
            InstallDevices = true,
            RequireSigned = true,
            RemoveOnUninstall = true,
            RebootBehavior = DriverPackageRebootBehavior.Possible
        });
        InstallerScriptSerializer.Save(project, scriptPath);
        var outDir = Path.Combine(_tempDir, "evidence");

        var report = new SystemResourceQualificationRunner().Run(new SystemResourceQualificationOptions
        {
            ProjectPath = scriptPath,
            OutputDirectory = outDir
        });

        report.Success.Should().BeTrue(string.Join(Environment.NewLine,
            report.Scenarios.SelectMany(s => s.Diagnostics.Select(d => $"{s.Id}: {d.Code} {d.Path} {d.Message}"))));
        report.ExitCode.Should().Be(0);
        report.Scenarios.Select(s => s.Id).Should().Contain(new[]
        {
            "load-project",
            "compiled-system-resources",
            "scheduled-task-provider",
            "firewall-provider",
            "file-association-provider",
            "certificate-provider",
            "com-provider",
            "driver-provider",
            "no-system-secret-leak"
        });
        report.Scenarios.Should().OnlyContain(s => s.Success);
        File.Exists(Path.Combine(outDir, SystemResourceQualificationRunner.ReportFileName)).Should().BeTrue();
    }

    [Fact]
    public void Run_FailsWhenProjectHasNoSystemResources()
    {
        var scriptPath = Path.Combine(_tempDir, "NoSystem.bsetup");
        var project = InstallerProjectFactory.CreateNew("No System", "1.0.0", "ACME", "");
        InstallerScriptSerializer.Save(project, scriptPath);

        var report = new SystemResourceQualificationRunner().Run(new SystemResourceQualificationOptions
        {
            ProjectPath = scriptPath,
            OutputDirectory = Path.Combine(_tempDir, "no-system-evidence")
        });

        report.Success.Should().BeFalse();
        report.Scenarios.Single(s => s.Id == "compiled-system-resources").Diagnostics
            .Should().Contain(d => d.Code == "BI0902");
    }
}
