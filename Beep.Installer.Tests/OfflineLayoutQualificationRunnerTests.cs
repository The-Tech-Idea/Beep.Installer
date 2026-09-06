using System.Security.Cryptography;
using System.Text.Json;
using Beep.Installer.Deployment;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class OfflineLayoutQualificationRunnerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"beep-layout-qualification-{Guid.NewGuid():N}");

    public OfflineLayoutQualificationRunnerTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Run_WritesEvidenceForValidMissingTamperedAndDryRunLifecycleScenarios()
    {
        var packagePath = Path.Combine(_tempDir, "runtime.exe");
        File.WriteAllText(packagePath, "qualified runtime payload");
        var scriptPath = Path.Combine(_tempDir, "app.bsetup");
        File.WriteAllText(scriptPath, "[Setup]\nAppName=QualifiedApp\n");
        var project = InstallerProjectFactory.CreateNew("QualifiedApp", "1.0.0", "ACME", _tempDir);
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "runtime",
            SourcePath = "runtime.exe",
            Sha512 = Sha512(packagePath),
            InstallArgs = "/quiet"
        });
        var layout = new OfflineLayoutBuilder().Build(project, Path.Combine(_tempDir, "layout"), new OfflineLayoutOptions
        {
            BaseDirectory = _tempDir
        });

        var report = new OfflineLayoutQualificationRunner().Run(new OfflineLayoutQualificationOptions
        {
            LayoutDirectory = layout.LayoutDirectory,
            ScriptPath = scriptPath,
            InstallDirectory = Path.Combine(_tempDir, "install"),
            OutputDirectory = Path.Combine(_tempDir, "qualification"),
            DryRun = true,
            VerificationOptions = new OfflineLayoutVerificationOptions { RequireSignature = false },
            ExtraInstallArguments = new[] { "/PROPERTY:PerUser=true" }
        });

        report.Success.Should().BeTrue();
        File.Exists(report.ReportPath).Should().BeTrue();
        report.Scenarios.Select(s => s.Id).Should().Contain(new[]
        {
            "verify-layout",
            "suite-package-inventory",
            "missing-blob-failure",
            "tampered-blob-failure",
            "install-offline",
            "repair-offline",
            "uninstall-offline"
        });
        report.Scenarios.Should().OnlyContain(s => s.Success);
        report.Scenarios.Single(s => s.Id == "missing-blob-failure").Diagnostics.Should().Contain(d => d.Code == "BI2008");
        report.Scenarios.Single(s => s.Id == "tampered-blob-failure").Diagnostics
            .Any(d => d.Code is "BI2009" or "BI2010")
            .Should().BeTrue();
        report.Scenarios.Single(s => s.Id == "install-offline").CommandLine.Should().Contain("/OFFLINELAYOUT=");
        report.Scenarios.Single(s => s.Id == "install-offline").CommandLine.Should().Contain("/PROPERTY:PerUser=true");

        using var document = JsonDocument.Parse(File.ReadAllText(report.ReportPath));
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("scenarios").GetArrayLength().Should().Be(7);
    }

    [Fact]
    public void Run_RequiresMixedPackageTypeSuiteInventoryWhenRequested()
    {
        var project = InstallerProjectFactory.CreateNew("MixedSuiteApp", "1.0.0", "ACME", _tempDir);
        AddPackage(project, "helper-exe", PackageNodeType.Exe, "helper.exe");
        AddPackage(project, "main-msi", PackageNodeType.Msi, "main.msi");
        AddPackage(project, "patch-msp", PackageNodeType.Msp, "patch.msp");
        AddPackage(project, "windows-msu", PackageNodeType.Msu, "windows.msu");
        var layout = new OfflineLayoutBuilder().Build(project, Path.Combine(_tempDir, "mixed-layout"), new OfflineLayoutOptions
        {
            BaseDirectory = _tempDir
        });

        var report = new OfflineLayoutQualificationRunner().Run(new OfflineLayoutQualificationOptions
        {
            LayoutDirectory = layout.LayoutDirectory,
            OutputDirectory = Path.Combine(_tempDir, "mixed-qualification"),
            DryRun = true,
            RequireMixedPackageTypes = true,
            VerificationOptions = new OfflineLayoutVerificationOptions { RequireSignature = false }
        });

        report.Success.Should().BeTrue();
        var suite = report.Scenarios.Single(s => s.Id == "suite-package-inventory");
        suite.Packages.Select(p => p.PackageType).Should().Contain(new[] { "exe", "msi", "msp", "msu" });
        suite.Packages.Should().OnlyContain(p => p.ContentAddress.StartsWith("sha512:", StringComparison.OrdinalIgnoreCase));
        File.Exists(suite.EvidencePath).Should().BeTrue();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static string Sha512(string path)
        => Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private void AddPackage(InstallProject project, string id, PackageNodeType packageType, string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, $"{id} payload");
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = id,
            PackageType = packageType,
            SourcePath = fileName,
            Sha512 = Sha512(path),
            InstallArgs = "/quiet",
            RepairArgs = "/repair /quiet",
            UninstallCommand = fileName,
            UninstallArgs = "/uninstall /quiet"
        });
    }
}
