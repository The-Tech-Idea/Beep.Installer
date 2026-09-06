using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Policy;
using Beep.Installer.Security;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class PrerequisiteCatalogServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"beep-catalog-{Guid.NewGuid():N}");

    public PrerequisiteCatalogServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void ApplyCatalogs_VerifiesSignedCatalogAndAddsPackageNodes()
    {
        var catalogPath = Path.Combine(_tempDir, "runtime.catalog.json");
        var publicKeyPath = Path.Combine(_tempDir, "catalog.pub.pem");
        using var rsa = RSA.Create(2048);
        var catalogJson = CatalogJson("dotnet-desktop-10", ".NET Desktop Runtime 10");
        File.WriteAllText(catalogPath, catalogJson);
        File.WriteAllText(publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());
        var signature = Sign(rsa, catalogJson);
        var project = new InstallProject();
        project.PrerequisiteCatalogs.Add(new PrerequisiteCatalogReference
        {
            Path = catalogPath,
            Signature = signature,
            TrustedPublicKeyPath = publicKeyPath,
            Required = true
        });

        var result = PrerequisiteCatalogService.ApplyCatalogs(project, _tempDir);

        result.Success.Should().BeTrue();
        result.CatalogsRead.Should().Be(1);
        result.PackagesAdded.Should().Be(1);
        project.Packages.Should().ContainSingle(p =>
            p.Id == "dotnet-desktop-10"
            && p.PackageType == PackageNodeType.Exe
            && p.DownloadUrl == "https://download.example.test/windowsdesktop-runtime-10.exe"
            && p.Sha256 == new string('a', 64)
            && p.InstallArgs == "/install /quiet /norestart"
            && p.IsMandatory);
    }

    [Fact]
    public void ApplyCatalogs_RejectsTamperedCatalogSignature()
    {
        var catalogPath = Path.Combine(_tempDir, "runtime.catalog.json");
        using var rsa = RSA.Create(2048);
        var signedJson = CatalogJson("vc-redist", "Visual C++ Redistributable");
        var tamperedJson = CatalogJson("vc-redist", "Tampered Visual C++ Redistributable");
        File.WriteAllText(catalogPath, tamperedJson);
        var project = new InstallProject();
        project.PrerequisiteCatalogs.Add(new PrerequisiteCatalogReference
        {
            Path = catalogPath,
            Signature = Sign(rsa, signedJson),
            TrustedPublicKey = rsa.ExportSubjectPublicKeyInfoPem(),
            Required = true
        });

        var result = PrerequisiteCatalogService.ApplyCatalogs(project, _tempDir);

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1352");
        project.Packages.Should().BeEmpty();
    }

    [Fact]
    public void ApplyCatalogs_ExpandsBuiltInMicrosoftRuntimeCatalog()
    {
        var project = new InstallProject();
        project.PrerequisiteCatalogs.Add(new PrerequisiteCatalogReference
        {
            Path = "builtin:microsoft-runtimes",
            Required = true
        });

        var result = PrerequisiteCatalogService.ApplyCatalogs(project, _tempDir);

        result.Success.Should().BeTrue();
        result.CatalogsRead.Should().Be(1);
        result.PackagesAdded.Should().Be(2);
        project.Packages.Should().ContainSingle(p =>
            p.Id == "dotnet-desktop-runtime-10"
            && p.DownloadUrl.Contains("windowsdesktop-runtime-10.0.11-win-x64.exe", StringComparison.Ordinal)
            && p.DownloadUrlX86.Contains("windowsdesktop-runtime-10.0.11-win-x86.exe", StringComparison.Ordinal)
            && p.Sha512.Length == 128
            && p.Sha512X86.Length == 128
            && p.InstallArgs == "/install /quiet /norestart");
        project.Packages.Should().ContainSingle(p =>
            p.Id == "vc-redist-v14"
            && p.DownloadUrl == "https://aka.ms/vc14/vc_redist.x64.exe"
            && p.DownloadUrlX86 == "https://aka.ms/vc14/vc_redist.x86.exe"
            && p.DetectionCommand.Contains("\\x64", StringComparison.Ordinal)
            && p.DetectionCommandX86.Contains("\\x86", StringComparison.Ordinal)
            && p.DetectionPatternX86 == "0x1"
            && string.IsNullOrWhiteSpace(p.Sha512));
    }

    [Fact]
    public void Compiler_ExpandsBuiltInCatalogIntoPackageOperations()
    {
        var project = InstallerProjectFactory.CreateNew("CatalogApp", "1.0.0", "ACME", "");
        project.PrerequisiteCatalogs.Add(new PrerequisiteCatalogReference
        {
            Path = "builtin:microsoft-runtimes",
            Required = true
        });

        var result = new InstallPlanCompiler().Compile(project);

        result.Success.Should().BeTrue();
        var dotnet = result.Plan!.Operations.Should().ContainSingle(o => o.Id == "package:dotnet-desktop-runtime-10").Subject;
        dotnet.Inputs["sha512"].Should().HaveLength(128);
        dotnet.Inputs["sha512X86"].Should().HaveLength(128);
        dotnet.Inputs["downloadUrl"].Should().Contain("windowsdesktop-runtime-10.0.11-win-x64.exe");
        var vc = result.Plan.Operations.Should().ContainSingle(o => o.Id == "package:vc-redist-v14").Subject;
        vc.Inputs["downloadUrl"].Should().Be("https://aka.ms/vc14/vc_redist.x64.exe");
        vc.Inputs["detectionCommand"].Should().Contain("\\x64");
        vc.Inputs["detectionCommandX86"].Should().Contain("\\x86");
    }

    [Fact]
    public void ApplyCatalogs_UsesPolicyTrustForApprovedPrivateCatalog()
    {
        var catalogPath = Path.Combine(_tempDir, "runtime.catalog.json");
        using var rsa = RSA.Create(2048);
        var catalogJson = CatalogJson("dotnet-desktop-10", ".NET Desktop Runtime 10");
        File.WriteAllText(catalogPath, catalogJson);
        var project = new InstallProject();
        project.PrerequisiteCatalogs.Add(new PrerequisiteCatalogReference
        {
            Path = catalogPath,
            Signature = Sign(rsa, catalogJson),
            Required = true
        });
        var policy = new InstallerPolicy
        {
            RequireSignedPrerequisiteCatalogs = true,
            AllowedPrerequisiteCatalogs = { "microsoft-runtime@2026.08.31" },
            TrustedPrerequisiteCatalogIssuers = { "The Tech Idea" },
            TrustedPrerequisiteCatalogPublicKeys = { rsa.ExportSubjectPublicKeyInfoPem() }
        };

        var result = PrerequisiteCatalogService.ApplyCatalogs(project, new PrerequisiteCatalogApplyOptions
        {
            BaseDirectory = _tempDir,
            Policy = policy
        });

        result.Success.Should().BeTrue();
        project.Packages.Should().ContainSingle(p => p.Id == "dotnet-desktop-10");
    }

    [Fact]
    public void ApplyCatalogs_RejectsPrivateCatalogWithUntrustedIssuer()
    {
        var catalogPath = Path.Combine(_tempDir, "runtime.catalog.json");
        using var rsa = RSA.Create(2048);
        var catalogJson = CatalogJson("dotnet-desktop-10", ".NET Desktop Runtime 10");
        File.WriteAllText(catalogPath, catalogJson);
        var project = new InstallProject();
        project.PrerequisiteCatalogs.Add(new PrerequisiteCatalogReference
        {
            Path = catalogPath,
            Signature = Sign(rsa, catalogJson),
            Required = true
        });
        var policy = new InstallerPolicy
        {
            RequireSignedPrerequisiteCatalogs = true,
            TrustedPrerequisiteCatalogIssuers = { "ACME Catalog Authority" },
            TrustedPrerequisiteCatalogPublicKeys = { rsa.ExportSubjectPublicKeyInfoPem() }
        };

        var result = PrerequisiteCatalogService.ApplyCatalogs(project, new PrerequisiteCatalogApplyOptions
        {
            BaseDirectory = _tempDir,
            Policy = policy
        });

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1373");
        project.Packages.Should().BeEmpty();
    }

    [Fact]
    public void ApplyCatalogs_RejectsMutableBuiltInCatalogEntryWhenPinnedHashesAreRequired()
    {
        var project = new InstallProject();
        project.PrerequisiteCatalogs.Add(new PrerequisiteCatalogReference
        {
            Path = "builtin:microsoft-runtimes",
            Required = true
        });

        var result = PrerequisiteCatalogService.ApplyCatalogs(project, new PrerequisiteCatalogApplyOptions
        {
            BaseDirectory = _tempDir,
            Policy = new InstallerPolicy { RequirePinnedPrerequisiteCatalogHashes = true }
        });

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1372" && d.Path.Contains("vc-redist-v14", StringComparison.Ordinal));
        project.Packages.Should().ContainSingle(p => p.Id == "dotnet-desktop-runtime-10");
        project.Packages.Should().NotContain(p => p.Id == "vc-redist-v14");
    }

    [Fact]
    public void Compiler_AppliesPrerequisiteCatalogPolicyBeforePackageOperations()
    {
        var project = InstallerProjectFactory.CreateNew("CatalogApp", "1.0.0", "ACME", "");
        project.PrerequisiteCatalogs.Add(new PrerequisiteCatalogReference
        {
            Path = "builtin:microsoft-runtimes",
            Required = true
        });
        var evaluation = InstallerPolicyEvaluator.EvaluateProject(
            new InstallerPolicy { RequirePinnedPrerequisiteCatalogHashes = true },
            project);

        var result = new InstallPlanCompiler().Compile(project, evaluation);

        result.Plan.Should().BeNull();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1372");
    }

    [Fact]
    public void ExportCatalog_WritesSignedBuiltInCatalogAndApprovalMetadata()
    {
        using var rsa = RSA.Create(2048);
        var privateKeyPath = Path.Combine(_tempDir, "catalog.private.pem");
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        File.WriteAllText(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());

        var export = PrerequisiteCatalogService.ExportCatalog(new PrerequisiteCatalogExportOptions
        {
            Catalog = "builtin:microsoft-runtimes",
            OutputDirectory = _tempDir,
            SigningPrivateKeyPath = privateKeyPath,
            KeyId = "catalog-release-key",
            ApprovedBy = "release-admin",
            ApprovalReason = "Approved runtime catalog"
        });

        export.Success.Should().BeTrue();
        File.Exists(export.CatalogPath).Should().BeTrue();
        File.Exists(export.SignaturePath).Should().BeTrue();
        File.Exists(export.ApprovalPath).Should().BeTrue();
        export.Sha256.Should().HaveLength(64);
        export.Sha512.Should().HaveLength(128);

        var project = new InstallProject();
        project.PrerequisiteCatalogs.Add(new PrerequisiteCatalogReference
        {
            Path = export.CatalogPath,
            Signature = File.ReadAllText(export.SignaturePath).Trim(),
            TrustedPublicKey = publicKey,
            Required = true
        });
        PrerequisiteCatalogService.ApplyCatalogs(project, _tempDir).Success.Should().BeTrue();
        project.Packages.Should().Contain(p => p.Id == "dotnet-desktop-runtime-10");

        using var approval = JsonDocument.Parse(File.ReadAllText(export.ApprovalPath));
        approval.RootElement.GetProperty("catalogId").GetString().Should().Be("builtin.microsoft-runtimes");
        approval.RootElement.GetProperty("keyId").GetString().Should().Be("catalog-release-key");
        approval.RootElement.GetProperty("publicKeySha256").GetString().Should().HaveLength(64);
        approval.RootElement.GetProperty("catalogSha256").GetString().Should().Be(export.Sha256);
        approval.RootElement.GetProperty("approvedBy").GetString().Should().Be("release-admin");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static string Sign(RSA rsa, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return RsaSha256DetachedSignatureVerifier.SignaturePrefix + Convert.ToBase64String(signature);
    }

    private static string CatalogJson(string id, string name)
        => $$"""
           {
             "SchemaVersion": "1.0",
             "CatalogId": "microsoft-runtime",
             "Version": "2026.08.31",
             "Issuer": "The Tech Idea",
             "Entries": [
               {
                 "Id": "{{id}}",
                 "Name": "{{name}}",
                 "Version": "10.0.0",
                 "PackageType": "exe",
                 "DownloadUrl": "https://download.example.test/windowsdesktop-runtime-10.exe",
                 "DownloadUrlX86": "https://download.example.test/windowsdesktop-runtime-10-x86.exe",
                 "Sha256": "{{new string('a', 64)}}",
                 "Sha512": "{{new string('b', 128)}}",
                 "DetectionCommand": "dotnet --list-runtimes",
                 "DetectionPattern": "Microsoft.WindowsDesktop.App 10.",
                 "SilentInstallArgs": "/install /quiet /norestart",
                 "Mandatory": true,
                 "SuccessExitCodes": "0,3010,1641",
                 "RebootExitCodes": "3010,1641",
                 "RetryCount": 3,
                 "TimeoutSeconds": 1800,
                 "HelpUrl": "https://learn.example.test/dotnet-runtime",
                 "License": "Microsoft runtime redistribution",
                 "Redistributable": true
               }
             ]
           }
           """;
}
