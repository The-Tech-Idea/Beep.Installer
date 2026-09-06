using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class IisQualificationRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public IisQualificationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepIisQualTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Run_GeneratesIisProviderQualificationEvidence()
    {
        var scriptPath = Path.Combine(_tempDir, "WebApp.bsetup");
        var packagePath = Path.Combine(_tempDir, "web.zip");
        File.WriteAllText(packagePath, "fake web deploy package");
        var project = InstallerProjectFactory.CreateNew("Web App", "2.3.4", "ACME", "");
        project.IisAppPools.Add(new IisAppPoolDefinition
        {
            Name = "WebAppPool",
            RuntimeVersion = "v4.0",
            PipelineMode = IisManagedPipelineMode.Integrated,
            Identity = "ApplicationPoolIdentity",
            AutoStart = true,
            StartAfterInstall = true
        });
        project.IisSites.Add(new IisSiteDefinition
        {
            Name = "WebApp",
            PhysicalPath = @"{app}\wwwroot",
            ApplicationPool = "WebAppPool",
            RemoveOnUninstall = true,
            Bindings =
            {
                new IisBindingDefinition { Protocol = IisBindingProtocol.Http, IpAddress = "*", Port = 8080, Host = "localhost" },
                new IisBindingDefinition
                {
                    Protocol = IisBindingProtocol.Https,
                    IpAddress = "*",
                    Port = 8443,
                    Host = "secure.localhost",
                    CertificateThumbprint = "0123456789ABCDEF0123456789ABCDEF01234567",
                    CertificateStoreName = "My",
                    SslFlags = 1
                }
            }
        });
        project.WebDeployPackages.Add(new WebDeployPackageDefinition
        {
            Name = "WebAppPackage",
            PackagePath = packagePath,
            SiteName = "WebApp",
            Destination = "auto",
            Parameters = { ["Environment"] = "Production" }
        });
        InstallerScriptSerializer.Save(project, scriptPath);
        var outDir = Path.Combine(_tempDir, "evidence");

        var report = new IisQualificationRunner().Run(new IisQualificationOptions
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
            "compiled-iis-operations",
            "app-pool-create",
            "app-pool-secret-boundary",
            "site-create-bindings",
            "shared-site-rollback",
            "https-binding-guard",
            "webdeploy-package",
            "no-iis-secret-leak"
        });
        report.Scenarios.Should().OnlyContain(s => s.Success);
        File.Exists(Path.Combine(outDir, IisQualificationRunner.ReportFileName)).Should().BeTrue();
    }

    [Fact]
    public void Run_FailsWhenProjectHasNoIisResources()
    {
        var scriptPath = Path.Combine(_tempDir, "NoIis.bsetup");
        var project = InstallerProjectFactory.CreateNew("No IIS", "1.0.0", "ACME", "");
        InstallerScriptSerializer.Save(project, scriptPath);

        var report = new IisQualificationRunner().Run(new IisQualificationOptions
        {
            ProjectPath = scriptPath,
            OutputDirectory = Path.Combine(_tempDir, "no-iis-evidence")
        });

        report.Success.Should().BeFalse();
        report.Scenarios.Single(s => s.Id == "compiled-iis-operations").Diagnostics
            .Should().Contain(d => d.Code == "BI0702")
            .And.Contain(d => d.Code == "BI0703");
    }
}
