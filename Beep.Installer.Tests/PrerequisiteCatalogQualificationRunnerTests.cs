using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Quality;
using Beep.Installer.Security;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class PrerequisiteCatalogQualificationRunnerTests : IDisposable
{
    private readonly string _root;

    public PrerequisiteCatalogQualificationRunnerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "BeepCatalogQual_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Run_ProvesSignedCatalogProjectionAndAirGappedOfflineLayout()
    {
        var script = CreateCatalogProject(out _);

        var report = new PrerequisiteCatalogQualificationRunner().Run(new PrerequisiteCatalogQualificationOptions
        {
            ProjectPath = script,
            OutputDirectory = Path.Combine(_root, "qualification")
        });

        report.Success.Should().BeTrue();
        report.ExitCode.Should().Be(0);
        report.Scenarios.Select(s => s.Id).Should().Equal(
            "load-project",
            "catalog-resolution",
            "compiled-package-operations",
            "offline-layout-build",
            "air-gapped-layout-verification",
            "catalog-package-offline-coverage");
        report.Packages.Should().ContainSingle(p =>
            p.PackageId == "local-runtime"
            && p.AvailableOffline
            && p.ContentAddress.StartsWith("sha512:", StringComparison.Ordinal)
            && p.BlobPath.Contains("local-runtime.exe", StringComparison.Ordinal));
        File.Exists(report.ReportPath).Should().BeTrue();
    }

    [Fact]
    public void Run_FailsClosed_WhenCatalogPackageIsNotAvailableOffline()
    {
        var script = CreateCatalogProject(out var payloadPath);
        File.Delete(payloadPath);

        var report = new PrerequisiteCatalogQualificationRunner().Run(new PrerequisiteCatalogQualificationOptions
        {
            ProjectPath = script,
            OutputDirectory = Path.Combine(_root, "qualification-missing")
        });

        report.Success.Should().BeFalse();
        report.Scenarios.Single(s => s.Id == "offline-layout-build").Diagnostics
            .Should().Contain(d => d.Code == "BI2001");
        report.Scenarios.Single(s => s.Id == "catalog-package-offline-coverage").Diagnostics
            .Should().Contain(d => d.Code == "BI0508");
    }

    private string CreateCatalogProject(out string payloadPath)
    {
        var payloadDir = Path.Combine(_root, "redist");
        Directory.CreateDirectory(payloadDir);
        payloadPath = Path.Combine(payloadDir, "local-runtime.exe");
        File.WriteAllText(payloadPath, "runtime payload");
        var payloadSha512 = Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(payloadPath))).ToLowerInvariant();

        using var rsa = RSA.Create(2048);
        var catalogPath = Path.Combine(_root, "runtime.catalog.json");
        var catalogJson = $$"""
            {
              "SchemaVersion": "1.0",
              "CatalogId": "local-runtime-catalog",
              "Version": "2026.09.02",
              "Issuer": "The Tech Idea Test",
              "Entries": [
                {
                  "Id": "local-runtime",
                  "Name": "Local Runtime",
                  "Version": "1.0.0",
                  "PackageType": "exe",
                  "DownloadUrl": "{{payloadPath.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                  "Sha512": "{{payloadSha512}}",
                  "DetectionCommand": "local-runtime --version",
                  "DetectionPattern": "1.0.0",
                  "SilentInstallArgs": "/quiet",
                  "Mandatory": true,
                  "SuccessExitCodes": "0",
                  "RebootExitCodes": "",
                  "RetryCount": 1,
                  "TimeoutSeconds": 60
                }
              ]
            }
            """;
        File.WriteAllText(catalogPath, catalogJson);
        var publicKeyPath = Path.Combine(_root, "runtime.catalog.pub.pem");
        File.WriteAllText(publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());

        var project = InstallerProjectFactory.CreateNew("CatalogQualifiedApp", "1.0.0", "ACME", "");
        project.PrerequisiteCatalogs.Add(new PrerequisiteCatalogReference
        {
            Path = catalogPath,
            Signature = Sign(rsa, catalogJson),
            TrustedPublicKeyPath = publicKeyPath,
            Required = true
        });

        var script = Path.Combine(_root, "CatalogQualifiedApp.bsetup");
        InstallerScriptSerializer.Save(project, script).ok.Should().BeTrue();
        return script;
    }

    private static string Sign(RSA rsa, string payload)
        => RsaSha256DetachedSignatureVerifier.SignaturePrefix
           + Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
}
