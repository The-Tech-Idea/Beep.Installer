using System.Security.Cryptography;
using System.Text.Json;
using Beep.Installer.Deployment;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class WinGetManifestExporterTests : IDisposable
{
    private readonly string _tempDir;

    public WinGetManifestExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepWinGet_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Generate_WritesMultiFileManifestWithComputedInstallerHash()
    {
        var project = InstallerProjectFactory.CreateNew("Service App", "2.3.4", "The Tech Idea", "");
        project.DefaultScope = InstallationScope.Machine;
        project.ArchitecturesAllowed = Architecture.X64Compatible;
        project.LicenseText = "MIT License";
        project.WelcomeTitle = "Install Service App for enterprise teams";
        var installerPath = Path.Combine(_tempDir, "Setup-ServiceApp.exe");
        File.WriteAllBytes(installerPath, [0x42, 0x45, 0x45, 0x50]);
        var expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installerPath)));

        var result = WinGetManifestExporter.Generate(project, new WinGetManifestOptions
        {
            OutputDirectory = _tempDir,
            InstallerPath = installerPath,
            InstallerUrl = "https://downloads.example.test/Setup-ServiceApp.exe",
            Moniker = "serviceapp"
        });

        result.PackageIdentifier.Should().Be("TheTechIdea.ServiceApp");
        result.InstallerSha256.Should().Be(expectedHash);
        result.Files.Should().HaveCount(5);
        result.ManifestDirectory.Should().EndWith(Path.Combine("manifests", "t", "TheTechIdea", "ServiceApp", "2.3.4"));

        var version = File.ReadAllText(Path.Combine(result.ManifestDirectory, "TheTechIdea.ServiceApp.yaml"));
        version.Should().Contain("ManifestType: version");
        version.Should().Contain("DefaultLocale: en-US");

        var locale = File.ReadAllText(Path.Combine(result.ManifestDirectory, "TheTechIdea.ServiceApp.locale.en-US.yaml"));
        locale.Should().Contain("ManifestType: defaultLocale");
        locale.Should().Contain("Publisher: \"The Tech Idea\"");
        locale.Should().Contain("PackageName: \"Service App\"");
        locale.Should().Contain("License: \"MIT License\"");
        locale.Should().Contain("Moniker: serviceapp");

        var installer = File.ReadAllText(Path.Combine(result.ManifestDirectory, "TheTechIdea.ServiceApp.installer.yaml"));
        installer.Should().Contain("ManifestType: installer");
        installer.Should().Contain("InstallerType: exe");
        installer.Should().Contain("Architecture: x64");
        installer.Should().Contain("Scope: machine");
        installer.Should().Contain("InstallerUrl: https://downloads.example.test/Setup-ServiceApp.exe");
        installer.Should().Contain($"InstallerSha256: {expectedHash}");
        installer.Should().Contain("Silent: \"/S /JSON /NORESTART\"");
        installer.Should().Contain("InstallLocation: /D=<INSTALLPATH>");
        installer.Should().Contain("Repair: \"/REPAIR /JSON\"");

        var qualificationPath = Path.Combine(_tempDir, WinGetManifestExporter.QualificationFileName);
        File.Exists(qualificationPath).Should().BeTrue();
        using var qualification = JsonDocument.Parse(File.ReadAllText(qualificationPath));
        var qualificationRoot = qualification.RootElement;
        qualificationRoot.GetProperty("target").GetString().Should().Be("wingetLocalManifest");
        qualificationRoot.GetProperty("packageIdentifier").GetString().Should().Be("TheTechIdea.ServiceApp");
        qualificationRoot.GetProperty("validationScript").GetString().Should().Be("Qualification/Test-WinGetLocalManifest.ps1");
        qualificationRoot.GetProperty("installerType").GetString().Should().Be("exe");
        qualificationRoot.GetProperty("scope").GetString().Should().Be("machine");
        qualificationRoot.GetProperty("installers").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("sha256").GetString().Should().Be(expectedHash);
        qualificationRoot.GetProperty("commands").GetProperty("validate").GetString().Should().Contain("winget validate");
        qualificationRoot.GetProperty("commands").GetProperty("install").GetString().Should().Contain("winget install --manifest");
        qualificationRoot.GetProperty("expectedExitCodes").GetProperty("install").EnumerateArray()
            .Select(e => e.GetInt32())
            .Should().BeEquivalentTo(new[] { 0, 3010 });
        File.ReadAllText(qualificationPath).Should().NotMatchRegex("(?i)(password|secret|token)");

        var qualificationScript = File.ReadAllText(Path.Combine(_tempDir, "Qualification", "Test-WinGetLocalManifest.ps1"));
        qualificationScript.Should().Contain("winget-localmanifest-evidence.json");
        qualificationScript.Should().Contain("winget.exe");
        qualificationScript.Should().Contain("validate");
        qualificationScript.Should().Contain("RunInstall");
    }

    [Fact]
    public void Generate_RequiresShaWhenOnlyInstallerUrlIsProvided()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");

        var act = () => WinGetManifestExporter.Generate(project, new WinGetManifestOptions
        {
            OutputDirectory = _tempDir,
            InstallerUrl = "https://downloads.example.test/Setup.exe"
        });

        act.Should().Throw<ArgumentException>().WithMessage("*SHA256*");
    }

    [Fact]
    public void Generate_AcceptsUrlAndExplicitShaForReleaseMetadata()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");

        var result = WinGetManifestExporter.Generate(project, new WinGetManifestOptions
        {
            OutputDirectory = _tempDir,
            InstallerUrl = "https://downloads.example.test/Setup.exe",
            InstallerSha256 = new string('a', 64),
            PackageIdentifier = "Acme.PolicyApp"
        });

        result.InstallerSha256.Should().Be(new string('A', 64));
        File.ReadAllText(Path.Combine(result.ManifestDirectory, "Acme.PolicyApp.installer.yaml"))
            .Should().Contain("InstallerSha256: " + new string('A', 64));
        File.Exists(Path.Combine(_tempDir, "Qualification", "Test-WinGetLocalManifest.ps1")).Should().BeTrue();
    }

    [Fact]
    public void Generate_MapsMandatoryPackageNodesToWinGetPackageDependencies()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "Microsoft.VCRedist.2015+.x64",
            Name = "Microsoft Visual C++ Redistributable",
            IsMandatory = true
        });
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "InternalBootstrapper",
            Name = "Internal bootstrapper",
            IsMandatory = true
        });
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "Microsoft.DotNet.DesktopRuntime.10",
            Name = ".NET Desktop Runtime",
            IsMandatory = false
        });

        WinGetManifestExporter.Generate(project, new WinGetManifestOptions
        {
            OutputDirectory = _tempDir,
            InstallerUrl = "https://downloads.example.test/Setup.exe",
            InstallerSha256 = new string('b', 64),
            PackageIdentifier = "Acme.PolicyApp"
        });

        var installer = File.ReadAllText(Path.Combine(_tempDir, "manifests", "a", "Acme", "PolicyApp", "1.0.0", "Acme.PolicyApp.installer.yaml"));
        installer.Should().Contain("  Dependencies:");
        installer.Should().Contain("    PackageDependencies:");
        installer.Should().Contain("    - PackageIdentifier: \"Microsoft.VCRedist.2015+.x64\"");
        installer.Should().NotContain("InternalBootstrapper");
        installer.Should().NotContain("Microsoft.DotNet.DesktopRuntime.10");

        using var qualification = JsonDocument.Parse(File.ReadAllText(Path.Combine(_tempDir, WinGetManifestExporter.QualificationFileName)));
        qualification.RootElement.GetProperty("packageDependencies").EnumerateArray()
            .Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "Microsoft.VCRedist.2015+.x64" });
    }

    [Fact]
    public void Generate_WritesMultiArchitectureInstallers()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        var x64Path = Path.Combine(_tempDir, "Setup-x64.exe");
        var x86Path = Path.Combine(_tempDir, "Setup-x86.exe");
        File.WriteAllBytes(x64Path, [0x64, 0x64]);
        File.WriteAllBytes(x86Path, [0x86, 0x86]);
        var x64Hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x64Path)));
        var x86Hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x86Path)));
        var arm64Hash = new string('c', 64).ToUpperInvariant();

        var result = WinGetManifestExporter.Generate(project, new WinGetManifestOptions
        {
            OutputDirectory = _tempDir,
            PackageIdentifier = "Acme.PolicyApp",
            Installers =
            {
                new WinGetInstallerArtifact
                {
                    Architecture = "x64",
                    InstallerPath = x64Path,
                    InstallerUrl = "https://downloads.example.test/Setup-x64.exe"
                },
                new WinGetInstallerArtifact
                {
                    Architecture = "x86",
                    InstallerPath = x86Path,
                    InstallerUrl = "https://downloads.example.test/Setup-x86.exe"
                },
                new WinGetInstallerArtifact
                {
                    Architecture = "arm64",
                    InstallerUrl = "https://downloads.example.test/Setup-arm64.exe",
                    InstallerSha256 = arm64Hash
                }
            }
        });

        result.Installers.Select(i => i.Architecture).Should().Equal("x86", "x64", "arm64");
        result.InstallerSha256.Should().Be(x86Hash);

        var installer = File.ReadAllText(Path.Combine(_tempDir, "manifests", "a", "Acme", "PolicyApp", "1.0.0", "Acme.PolicyApp.installer.yaml"));
        installer.Should().Contain("- Architecture: x86");
        installer.Should().Contain("  InstallerUrl: https://downloads.example.test/Setup-x86.exe");
        installer.Should().Contain($"  InstallerSha256: {x86Hash}");
        installer.Should().Contain("- Architecture: x64");
        installer.Should().Contain("  InstallerUrl: https://downloads.example.test/Setup-x64.exe");
        installer.Should().Contain($"  InstallerSha256: {x64Hash}");
        installer.Should().Contain("- Architecture: arm64");
        installer.Should().Contain("  InstallerUrl: https://downloads.example.test/Setup-arm64.exe");
        installer.Should().Contain($"  InstallerSha256: {arm64Hash}");

        using var qualification = JsonDocument.Parse(File.ReadAllText(Path.Combine(_tempDir, WinGetManifestExporter.QualificationFileName)));
        qualification.RootElement.GetProperty("installers").EnumerateArray()
            .Select(e => e.GetProperty("architecture").GetString())
            .Should().Equal("x86", "x64", "arm64");
    }

    [Fact]
    public void Generate_LinksReleaseEvidenceArtifactsInQualificationMetadata()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        var sbomPath = Path.Combine(_tempDir, "PolicyApp.spdx.json");
        var provenancePath = Path.Combine(_tempDir, "PolicyApp.provenance.json");
        var signingEvidencePath = Path.Combine(_tempDir, "signing-evidence.json");
        File.WriteAllText(sbomPath, """{"spdxVersion":"SPDX-2.3"}""");
        File.WriteAllText(provenancePath, """{"predicateType":"https://slsa.dev/provenance/v1"}""");
        File.WriteAllText(signingEvidencePath, """{"signed":true}""");
        var expectedSbomHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sbomPath))).ToLowerInvariant();

        var result = WinGetManifestExporter.Generate(project, new WinGetManifestOptions
        {
            OutputDirectory = _tempDir,
            InstallerUrl = "https://downloads.example.test/Setup.exe",
            InstallerSha256 = new string('d', 64),
            PackageIdentifier = "Acme.PolicyApp",
            SbomPath = sbomPath,
            ProvenancePath = provenancePath,
            SigningEvidencePath = signingEvidencePath
        });

        result.Warnings.Should().BeEmpty();
        using var qualification = JsonDocument.Parse(File.ReadAllText(Path.Combine(_tempDir, WinGetManifestExporter.QualificationFileName)));
        var evidence = qualification.RootElement.GetProperty("releaseEvidence").EnumerateArray().ToArray();
        evidence.Select(e => e.GetProperty("kind").GetString()).Should().Equal("sbom", "provenance", "signingEvidence");
        var sbom = evidence.Single(e => e.GetProperty("kind").GetString() == "sbom");
        sbom.GetProperty("exists").GetBoolean().Should().BeTrue();
        sbom.GetProperty("fileName").GetString().Should().Be("PolicyApp.spdx.json");
        sbom.GetProperty("sha256").GetString().Should().Be(expectedSbomHash);
        sbom.GetProperty("sizeBytes").GetInt64().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Generate_WarnsWhenReferencedReleaseEvidenceArtifactIsMissing()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        var missingPath = Path.Combine(_tempDir, "missing.provenance.json");

        var result = WinGetManifestExporter.Generate(project, new WinGetManifestOptions
        {
            OutputDirectory = _tempDir,
            InstallerUrl = "https://downloads.example.test/Setup.exe",
            InstallerSha256 = new string('e', 64),
            PackageIdentifier = "Acme.PolicyApp",
            ProvenancePath = missingPath
        });

        result.Warnings.Should().ContainSingle(w => w.Contains("provenance", StringComparison.OrdinalIgnoreCase)
                                                    && w.Contains("not found", StringComparison.OrdinalIgnoreCase));
        using var qualification = JsonDocument.Parse(File.ReadAllText(Path.Combine(_tempDir, WinGetManifestExporter.QualificationFileName)));
        var evidence = qualification.RootElement.GetProperty("releaseEvidence").EnumerateArray().Should().ContainSingle().Subject;
        evidence.GetProperty("exists").GetBoolean().Should().BeFalse();
        evidence.TryGetProperty("sha256", out _).Should().BeFalse();
    }

    [Fact]
    public void Generate_EmitsMsixSignatureSha256WhenProvided()
    {
        var project = InstallerProjectFactory.CreateNew("StoreApp", "1.0.0", "ACME", "");
        project.OutputFormat = InstallerOutputFormat.Msix;
        var signatureHash = new string('f', 64).ToUpperInvariant();

        var result = WinGetManifestExporter.Generate(project, new WinGetManifestOptions
        {
            OutputDirectory = _tempDir,
            InstallerUrl = "https://downloads.example.test/StoreApp.msix",
            InstallerSha256 = new string('a', 64),
            SignatureSha256 = signatureHash,
            PackageIdentifier = "Acme.StoreApp"
        });

        result.Warnings.Should().BeEmpty();
        var installer = File.ReadAllText(Path.Combine(_tempDir, "manifests", "a", "Acme", "StoreApp", "1.0.0", "Acme.StoreApp.installer.yaml"));
        installer.Should().Contain("InstallerType: msix");
        installer.Should().Contain($"  SignatureSha256: {signatureHash}");

        using var qualification = JsonDocument.Parse(File.ReadAllText(Path.Combine(_tempDir, WinGetManifestExporter.QualificationFileName)));
        qualification.RootElement.GetProperty("installers")[0].GetProperty("signatureSha256").GetString().Should().Be(signatureHash);
    }

    [Fact]
    public void Generate_WarnsWhenMsixSignatureSha256IsMissing()
    {
        var project = InstallerProjectFactory.CreateNew("StoreApp", "1.0.0", "ACME", "");
        project.OutputFormat = InstallerOutputFormat.Msix;

        var result = WinGetManifestExporter.Generate(project, new WinGetManifestOptions
        {
            OutputDirectory = _tempDir,
            InstallerUrl = "https://downloads.example.test/StoreApp.msix",
            InstallerSha256 = new string('a', 64),
            PackageIdentifier = "Acme.StoreApp"
        });

        result.Warnings.Should().ContainSingle(w => w.Contains("SignatureSha256", StringComparison.OrdinalIgnoreCase));
        File.ReadAllText(Path.Combine(_tempDir, "manifests", "a", "Acme", "StoreApp", "1.0.0", "Acme.StoreApp.installer.yaml"))
            .Should().NotContain("SignatureSha256:");
    }
}
