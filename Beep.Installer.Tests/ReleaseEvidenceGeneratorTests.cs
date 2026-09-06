using System.Security.Cryptography;
using System.Text.Json;
using Beep.Installer.Deployment;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Models;
using Beep.Installer.Policy;
using Beep.Installer.Security;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class ReleaseEvidenceGeneratorTests : IDisposable
{
    private readonly string _tempDir;

    public ReleaseEvidenceGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepEvidence_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Generate_WritesSpdxAndProvenanceWithoutLeakingAbsoluteSourcePaths()
    {
        var sourceRoot = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "bin"));
        var payloadFile = Path.Combine(sourceRoot, "bin", "app.exe");
        File.WriteAllBytes(payloadFile, [0x01, 0x02, 0x03, 0x04]);
        var installerFile = Path.Combine(_tempDir, "Setup.exe");
        File.WriteAllBytes(installerFile, [0x42, 0x45, 0x45, 0x50]);
        var payloadHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(payloadFile)));
        var installerHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installerFile)));

        var project = InstallerProjectFactory.CreateNew("EvidenceApp", "1.2.3", "ACME", sourceRoot);
        project.LicenseText = "Enterprise license terms";
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = payloadFile,
                    DestinationPath = "bin/app.exe",
                    IsRequired = true
                }
            }
        });

        var result = ReleaseEvidenceGenerator.Generate(project, new ReleaseEvidenceOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "evidence"),
            InstallerPath = installerFile,
            SourceRoot = sourceRoot,
            SourceRevision = "abcdef123456"
        });

        File.Exists(result.SbomPath).Should().BeTrue();
        File.Exists(result.ProvenancePath).Should().BeTrue();
        result.Files.Should().BeEquivalentTo(new[] { result.SbomPath, result.ProvenancePath });

        var sbomText = File.ReadAllText(result.SbomPath);
        sbomText.Should().Contain("\"spdxVersion\": \"SPDX-2.3\"");
        sbomText.Should().Contain("\"fileName\": \"bin/app.exe\"");
        sbomText.Should().Contain("\"licenseDeclared\": \"LicenseRef-ProjectLicense\"");
        sbomText.Should().Contain(payloadHash.ToLowerInvariant());
        sbomText.Should().Contain(installerHash.ToLowerInvariant());
        sbomText.Should().NotContain(sourceRoot.Replace("\\", "\\\\", StringComparison.Ordinal));
        sbomText.Should().NotContain(payloadFile.Replace("\\", "\\\\", StringComparison.Ordinal));

        var provenance = JsonDocument.Parse(File.ReadAllText(result.ProvenancePath));
        provenance.RootElement.GetProperty("_type").GetString().Should().Be("https://in-toto.io/Statement/v1");
        provenance.RootElement.GetProperty("predicateType").GetString().Should().Be("https://slsa.dev/provenance/v1");
        provenance.RootElement.GetProperty("predicate").GetProperty("sourceRevision").GetString().Should().Be("abcdef123456");
        File.ReadAllText(result.ProvenancePath).Should().Contain(installerHash.ToLowerInvariant());
    }

    [Fact]
    public void PolicyRequirement_NoLongerFailsProjectEvaluationBeforeEvidenceGeneration()
    {
        var project = InstallerProjectFactory.CreateNew("EvidenceApp", "1.0.0", "ACME", "");
        var policy = new Beep.Installer.Policy.InstallerPolicy
        {
            RequireSbom = true,
            RequireProvenance = true
        };

        var result = Beep.Installer.Policy.InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Select(d => d.Code).Should().NotContain(new[] { "BI8008", "BI8009" });
    }

    [Fact]
    public void Generate_IncludesSupplyChainGateSummaryInProvenance()
    {
        var sourceRoot = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "bin"));
        var payloadFile = Path.Combine(sourceRoot, "bin", "app.exe");
        File.WriteAllText(payloadFile, "payload");
        var project = InstallerProjectFactory.CreateNew("EvidenceApp", "1.2.3", "ACME", sourceRoot);
        project.LicenseText = "Enterprise license terms";
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = payloadFile,
                    DestinationPath = "bin/app.exe",
                    IsRequired = true
                }
            }
        });
        var supplyChainReport = new SupplyChainSecurityReport
        {
            SchemaVersion = "1.0",
            ProductName = "EvidenceApp",
            ProductVersion = "1.2.3",
            PlanHash = "ABCDEF",
            Artifacts =
            {
                new SupplyChainArtifact
                {
                    Kind = "payload",
                    Path = "bin/app.exe",
                    Sha256 = new string('a', 64),
                    SizeBytes = 7,
                    Scans =
                    {
                        new ArtifactSecurityScanResult
                        {
                            Kind = "vulnerability",
                            ToolName = "OSV-Scanner",
                            ToolVersion = "2.0.test",
                            Status = "Detected",
                            Detections =
                            {
                                new ArtifactSecurityDetection
                                {
                                    Id = "GHSA-test",
                                    Severity = "error",
                                    Message = "Test vulnerability."
                                }
                            }
                        }
                    }
                }
            },
            Findings =
            {
                new SupplyChainFinding
                {
                    Severity = "error",
                    Code = "BI9013",
                    Path = "bin/app.exe",
                    Sha256 = new string('a', 64),
                    Message = "OSV-Scanner detected GHSA-test."
                }
            }
        };

        var result = ReleaseEvidenceGenerator.Generate(project, new ReleaseEvidenceOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "evidence-with-supply-chain"),
            SourceRoot = sourceRoot,
            SupplyChainReport = supplyChainReport
        });

        using var provenance = JsonDocument.Parse(File.ReadAllText(result.ProvenancePath));
        var supplyChain = provenance.RootElement
            .GetProperty("predicate")
            .GetProperty("buildDefinition")
            .GetProperty("internalParameters")
            .GetProperty("supplyChainSecurity");

        supplyChain.GetProperty("included").GetBoolean().Should().BeTrue();
        supplyChain.GetProperty("status").GetString().Should().Be("failed");
        supplyChain.GetProperty("artifactCount").GetInt32().Should().Be(1);
        supplyChain.GetProperty("scannedArtifactCount").GetInt32().Should().Be(1);
        supplyChain.GetProperty("findingCount").GetInt32().Should().Be(1);
        supplyChain.GetProperty("findingsByCode")[0].GetProperty("code").GetString().Should().Be("BI9013");
        var scanner = supplyChain.GetProperty("scannerResults")[0];
        scanner.GetProperty("artifact").GetString().Should().Be("bin/app.exe");
        scanner.GetProperty("toolName").GetString().Should().Be("OSV-Scanner");
        scanner.GetProperty("toolVersion").GetString().Should().Be("2.0.test");
        scanner.GetProperty("status").GetString().Should().Be("Detected");
        scanner.GetProperty("detectionCount").GetInt32().Should().Be(1);
        File.ReadAllText(result.ProvenancePath).Should().NotContain(sourceRoot.Replace("\\", "\\\\", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_IncludesPolicyDecisionEvidenceInProvenance()
    {
        var sourceRoot = Path.Combine(_tempDir, "src-policy");
        Directory.CreateDirectory(sourceRoot);
        var project = InstallerProjectFactory.CreateNew("EvidenceApp", "1.2.3", "ACME", sourceRoot);
        var publicKeyMarker = "-----BEGIN PUBLIC KEY-----";
        var policy = new InstallerPolicy
        {
            RequireSbom = true,
            RequireProvenance = true,
            RequireSupplyChainScan = true,
            AllowedPublishers = { "ACME" },
            PolicyIssuer = "CN=The Tech Idea Policy Authority",
            TrustedPolicyIssuerSubjects = { "CN=The Tech Idea Policy Authority" }
        };
        var evaluation = InstallerPolicyEvaluator.EvaluateProject(policy, project);
        evaluation.Sources.Add(new InstallerPolicySource
        {
            Kind = "machine",
            Name = "machine-policy.json",
            Sha256 = new string('b', 64)
        });

        var result = ReleaseEvidenceGenerator.Generate(project, new ReleaseEvidenceOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "evidence-with-policy"),
            SourceRoot = sourceRoot,
            PolicyEvaluation = evaluation
        });

        using var provenance = JsonDocument.Parse(File.ReadAllText(result.ProvenancePath));
        var policyEvidence = provenance.RootElement
            .GetProperty("predicate")
            .GetProperty("buildDefinition")
            .GetProperty("internalParameters")
            .GetProperty("policy");

        policyEvidence.GetProperty("included").GetBoolean().Should().BeTrue();
        policyEvidence.GetProperty("status").GetString().Should().Be("passed");
        policyEvidence.GetProperty("effectivePolicySha256").GetString().Should().NotBeNullOrWhiteSpace();
        policyEvidence.GetProperty("sources")[0].GetProperty("kind").GetString().Should().Be("machine");
        policyEvidence.GetProperty("sources")[0].GetProperty("name").GetString().Should().Be("machine-policy.json");
        policyEvidence.GetProperty("requiredControls").EnumerateArray().Select(e => e.GetString()).Should().Contain("supply-chain-scan");
        policyEvidence.GetProperty("constraintCounts").GetProperty("allowedPublishers").GetInt32().Should().Be(1);
        File.ReadAllText(result.ProvenancePath).Should().NotContain(publicKeyMarker);
    }

    [Fact]
    public void Generate_IncludesPrerequisitesPackagesAndExtensionsInSbomAndProvenance()
    {
        var sourceRoot = Path.Combine(_tempDir, "src-dependencies");
        Directory.CreateDirectory(sourceRoot);
        var extensionDir = Path.Combine(_tempDir, "extensions", "audit");
        Directory.CreateDirectory(extensionDir);
        var extensionAssembly = Path.Combine(extensionDir, "Beep.Audit.Extension.dll");
        File.WriteAllText(extensionAssembly, "extension-binary");
        var extensionHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(extensionAssembly)));

        var project = InstallerProjectFactory.CreateNew("EvidenceApp", "1.2.3", "ACME", sourceRoot);
        project.Prerequisites.Add(new Prerequisite
        {
            Id = "dotnet-runtime",
            Name = ".NET Desktop Runtime",
            VersionRequired = "10.0.1",
            DownloadUrl = "https://download.example.test/windowsdesktop-runtime-10.0.1-win-x64.exe",
            DownloadUrlX86 = "https://download.example.test/windowsdesktop-runtime-10.0.1-win-x86.exe",
            IsMandatory = true,
            HelpUrl = "https://learn.example.test/runtime"
        });
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "vc-redist",
            Name = "Microsoft Visual C++ Redistributable",
            PackageType = PackageNodeType.Exe,
            DownloadUrl = "https://download.example.test/vc_redist.x64.exe",
            Sha256 = new string('a', 64),
            IsMandatory = true
        });
        var extensions = new[]
        {
            new InstallerExtensionReference
            {
                DirectoryPath = extensionDir,
                EntryAssemblyPath = extensionAssembly,
                ManifestPath = Path.Combine(extensionDir, "beep.installer.extension.json"),
                Manifest = new InstallerExtensionManifest
                {
                    Id = "beep.audit.extension",
                    Publisher = "ACME",
                    Version = "2.1.0",
                    MinimumEngineVersion = "1.0.0",
                    EntryAssembly = "Beep.Audit.Extension.dll",
                    Sha256 = extensionHash,
                    Permissions = InstallerExtensionPermission.FileSystem | InstallerExtensionPermission.Network,
                    ResourceTypes = { "audit.log", "audit.export" }
                }
            }
        };

        var result = ReleaseEvidenceGenerator.Generate(project, new ReleaseEvidenceOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "dependency-evidence"),
            SourceRoot = sourceRoot,
            Extensions = extensions
        });

        var sbomText = File.ReadAllText(result.SbomPath);
        sbomText.Should().Contain("\"SPDXID\": \"SPDXRef-Prerequisite-dotnet-runtime\"");
        sbomText.Should().Contain("\"SPDXID\": \"SPDXRef-PackageNode-vc-redist\"");
        sbomText.Should().Contain("\"SPDXID\": \"SPDXRef-Extension-beep-audit-extension\"");
        sbomText.Should().Contain("\"relationshipType\": \"DEPENDS_ON\"");
        sbomText.Should().Contain(extensionHash.ToLowerInvariant());
        sbomText.Should().NotContain(extensionDir.Replace("\\", "\\\\", StringComparison.Ordinal));
        sbomText.Should().NotContain(extensionAssembly.Replace("\\", "\\\\", StringComparison.Ordinal));

        using var provenance = JsonDocument.Parse(File.ReadAllText(result.ProvenancePath));
        var dependencies = provenance.RootElement
            .GetProperty("predicate")
            .GetProperty("buildDefinition")
            .GetProperty("resolvedDependencies")
            .EnumerateArray()
            .ToList();
        dependencies.Should().Contain(d =>
            d.GetProperty("type").GetString() == "prerequisite"
            && d.GetProperty("name").GetString() == ".NET Desktop Runtime"
            && d.GetProperty("version").GetString() == "10.0.1");
        dependencies.Should().Contain(d =>
            d.GetProperty("type").GetString() == "package-node"
            && d.GetProperty("name").GetString() == "Microsoft Visual C++ Redistributable"
            && d.GetProperty("digest").GetProperty("sha256").GetString() == new string('a', 64));
        dependencies.Should().Contain(d =>
            d.GetProperty("type").GetString() == "installer-extension"
            && d.GetProperty("name").GetString() == "beep.audit.extension"
            && d.GetProperty("fileName").GetString() == "Beep.Audit.Extension.dll");
        File.ReadAllText(result.ProvenancePath).Should().NotContain(extensionDir.Replace("\\", "\\\\", StringComparison.Ordinal));
    }

    [Fact]
    public void Verify_RegeneratesHashesAndPassesForMatchingEvidence()
    {
        var sourceRoot = Path.Combine(_tempDir, "src-verify");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "bin"));
        var payloadFile = Path.Combine(sourceRoot, "bin", "app.exe");
        var installerFile = Path.Combine(_tempDir, "Setup-verify.exe");
        File.WriteAllText(payloadFile, "payload");
        File.WriteAllText(installerFile, "installer");

        var project = InstallerProjectFactory.CreateNew("EvidenceApp", "1.2.3", "ACME", sourceRoot);
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = payloadFile,
                    DestinationPath = "bin/app.exe",
                    IsRequired = true
                }
            }
        });
        var generated = ReleaseEvidenceGenerator.Generate(project, new ReleaseEvidenceOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "verify-evidence"),
            InstallerPath = installerFile,
            SourceRoot = sourceRoot
        });

        var verification = ReleaseEvidenceVerifier.Verify(project, new ReleaseEvidenceVerificationOptions
        {
            EvidenceDirectory = generated.OutputDirectory,
            InstallerPath = installerFile,
            SourceRoot = sourceRoot
        });

        verification.Success.Should().BeTrue();
        verification.Diagnostics.Should().BeEmpty();
        verification.Artifacts.Should().Contain(a => a.Kind == "sbom" && a.Name == "bin/app.exe" && a.Matched);
        verification.Artifacts.Should().Contain(a => a.Kind == "provenance" && a.Name == "Setup-verify.exe" && a.Matched);
        verification.Artifacts.Should().Contain(a => a.Kind == "provenance" && a.Name == "compiled-install-plan" && a.Matched);
        File.Exists(verification.ReportPath).Should().BeTrue();
        using var report = JsonDocument.Parse(File.ReadAllText(verification.ReportPath));
        report.RootElement.GetProperty("succeeded").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Generate_WithAttestationKey_WritesDsseEnvelopesAndVerifierTrustsThem()
    {
        var sourceRoot = Path.Combine(_tempDir, "src-attested");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "bin"));
        var payloadFile = Path.Combine(sourceRoot, "bin", "app.exe");
        var installerFile = Path.Combine(_tempDir, "Setup-attested.exe");
        File.WriteAllText(payloadFile, "payload");
        File.WriteAllText(installerFile, "installer");

        using var rsa = RSA.Create(2048);
        var privateKeyPath = Path.Combine(_tempDir, "attest-private.pem");
        var publicKeyPath = Path.Combine(_tempDir, "attest-public.pem");
        File.WriteAllText(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());

        var project = InstallerProjectFactory.CreateNew("EvidenceApp", "1.2.3", "ACME", sourceRoot);
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = payloadFile,
                    DestinationPath = "bin/app.exe",
                    IsRequired = true
                }
            }
        });

        var generated = ReleaseEvidenceGenerator.Generate(project, new ReleaseEvidenceOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "attested-evidence"),
            InstallerPath = installerFile,
            SourceRoot = sourceRoot,
            AttestationPrivateKeyPath = privateKeyPath,
            AttestationKeyId = "release-attestation-key"
        });

        generated.SbomAttestationPath.Should().EndWith(".spdx.json.dsse.json");
        generated.ProvenanceAttestationPath.Should().EndWith(".provenance.json.dsse.json");
        File.Exists(generated.SbomAttestationPath).Should().BeTrue();
        File.Exists(generated.ProvenanceAttestationPath).Should().BeTrue();
        generated.Files.Should().Contain(new[] { generated.SbomAttestationPath, generated.ProvenanceAttestationPath });

        var verification = ReleaseEvidenceVerifier.Verify(project, new ReleaseEvidenceVerificationOptions
        {
            EvidenceDirectory = generated.OutputDirectory,
            InstallerPath = installerFile,
            SourceRoot = sourceRoot,
            RequireAttestations = true,
            TrustedAttestationPublicKeyPath = publicKeyPath
        });

        verification.Success.Should().BeTrue();
        verification.Diagnostics.Should().BeEmpty();
        verification.Artifacts.Should().Contain(a => a.Kind == "dsse" && a.Name.EndsWith(".spdx.json.dsse.json") && a.Matched);
        verification.Artifacts.Should().Contain(a => a.Kind == "dsse" && a.Name.EndsWith(".provenance.json.dsse.json") && a.Matched);
    }

    [Fact]
    public void Verify_FailsWhenRequiredDsseEnvelopeIsMissing()
    {
        var sourceRoot = Path.Combine(_tempDir, "src-missing-attestation");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "bin"));
        var payloadFile = Path.Combine(sourceRoot, "bin", "app.exe");
        File.WriteAllText(payloadFile, "payload");
        using var rsa = RSA.Create(2048);
        var publicKeyPath = Path.Combine(_tempDir, "missing-attest-public.pem");
        File.WriteAllText(publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());

        var project = InstallerProjectFactory.CreateNew("EvidenceApp", "1.2.3", "ACME", sourceRoot);
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = payloadFile,
                    DestinationPath = "bin/app.exe",
                    IsRequired = true
                }
            }
        });
        var generated = ReleaseEvidenceGenerator.Generate(project, new ReleaseEvidenceOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "unsigned-evidence"),
            SourceRoot = sourceRoot
        });

        var verification = ReleaseEvidenceVerifier.Verify(project, new ReleaseEvidenceVerificationOptions
        {
            EvidenceDirectory = generated.OutputDirectory,
            SourceRoot = sourceRoot,
            RequireAttestations = true,
            TrustedAttestationPublicKeyPath = publicKeyPath
        });

        verification.Success.Should().BeFalse();
        verification.Diagnostics.Should().Contain(d => d.Code == "BI2222");
    }

    [Fact]
    public void Verify_FailsWhenPolicyRequiresSignedArtifactWithoutSigningEvidence()
    {
        var sourceRoot = Path.Combine(_tempDir, "src-unsigned-release");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "bin"));
        var payloadFile = Path.Combine(sourceRoot, "bin", "app.exe");
        var installerFile = Path.Combine(_tempDir, "Setup-unsigned.exe");
        File.WriteAllText(payloadFile, "payload");
        File.WriteAllText(installerFile, "installer");

        var project = InstallerProjectFactory.CreateNew("EvidenceApp", "1.2.3", "ACME", sourceRoot);
        project.LicenseText = "Enterprise license terms";
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = payloadFile,
                    DestinationPath = "bin/app.exe",
                    IsRequired = true
                }
            }
        });
        var policy = new InstallerPolicy { RequireSignedArtifacts = true, RequireLicenseMetadata = true };
        var evaluation = InstallerPolicyEvaluator.EvaluateProject(policy, project);
        var generated = ReleaseEvidenceGenerator.Generate(project, new ReleaseEvidenceOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "unsigned-release-evidence"),
            InstallerPath = installerFile,
            SourceRoot = sourceRoot,
            PolicyEvaluation = evaluation
        });

        var verification = ReleaseEvidenceVerifier.Verify(project, new ReleaseEvidenceVerificationOptions
        {
            EvidenceDirectory = generated.OutputDirectory,
            InstallerPath = installerFile,
            SourceRoot = sourceRoot,
            PolicyEvaluation = evaluation
        });

        verification.Success.Should().BeFalse();
        verification.Diagnostics.Should().Contain(d => d.Code == "BI2227");
    }

    [Fact]
    public void Verify_PassesSignedArtifactPolicyWithSuccessfulSigningEvidence()
    {
        var sourceRoot = Path.Combine(_tempDir, "src-signed-release");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "bin"));
        var payloadFile = Path.Combine(sourceRoot, "bin", "app.exe");
        var installerFile = Path.Combine(_tempDir, "Setup-signed.exe");
        File.WriteAllText(payloadFile, "payload");
        File.WriteAllText(installerFile, "installer");

        var project = InstallerProjectFactory.CreateNew("EvidenceApp", "1.2.3", "ACME", sourceRoot);
        project.LicenseText = "Enterprise license terms";
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = payloadFile,
                    DestinationPath = "bin/app.exe",
                    IsRequired = true
                }
            }
        });
        var policy = new InstallerPolicy { RequireSignedArtifacts = true, RequireLicenseMetadata = true };
        var evaluation = InstallerPolicyEvaluator.EvaluateProject(policy, project);
        var generated = ReleaseEvidenceGenerator.Generate(project, new ReleaseEvidenceOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "signed-release-evidence"),
            InstallerPath = installerFile,
            SourceRoot = sourceRoot,
            PolicyEvaluation = evaluation
        });
        var signingEvidencePath = Path.Combine(_tempDir, "signing-evidence.json");
        File.WriteAllText(signingEvidencePath, JsonSerializer.Serialize(new
        {
            signingEvidence = new[]
            {
                new
                {
                    artifactKind = "exe",
                    artifactPath = installerFile,
                    success = true,
                    fileDigestSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installerFile))).ToLowerInvariant()
                }
            }
        }));

        var verification = ReleaseEvidenceVerifier.Verify(project, new ReleaseEvidenceVerificationOptions
        {
            EvidenceDirectory = generated.OutputDirectory,
            InstallerPath = installerFile,
            SourceRoot = sourceRoot,
            SigningEvidencePath = signingEvidencePath,
            PolicyEvaluation = evaluation
        });

        verification.Success.Should().BeTrue();
        verification.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Verify_FailsClosedAndWritesReport_WhenEvidenceJsonIsMalformed()
    {
        var sourceRoot = Path.Combine(_tempDir, "src-malformed-evidence");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "bin"));
        var payloadFile = Path.Combine(sourceRoot, "bin", "app.exe");
        File.WriteAllText(payloadFile, "payload");

        var project = InstallerProjectFactory.CreateNew("EvidenceApp", "1.2.3", "ACME", sourceRoot);
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = payloadFile,
                    DestinationPath = "bin/app.exe",
                    IsRequired = true
                }
            }
        });
        var evidenceDir = Path.Combine(_tempDir, "malformed-evidence");
        Directory.CreateDirectory(evidenceDir);
        File.WriteAllText(Path.Combine(evidenceDir, "EvidenceApp-1.2.3.spdx.json"), "{ not-json");
        File.WriteAllText(Path.Combine(evidenceDir, "EvidenceApp-1.2.3.provenance.json"), "{ also-not-json");

        var verification = ReleaseEvidenceVerifier.Verify(project, new ReleaseEvidenceVerificationOptions
        {
            EvidenceDirectory = evidenceDir,
            SourceRoot = sourceRoot
        });

        verification.Success.Should().BeFalse();
        verification.Diagnostics.Should().Contain(d => d.Code == "BI2240" && d.Path == "SBOM");
        verification.Diagnostics.Should().Contain(d => d.Code == "BI2241" && d.Path == "Provenance");
        File.Exists(verification.ReportPath).Should().BeTrue();
        using var report = JsonDocument.Parse(File.ReadAllText(verification.ReportPath));
        report.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Verify_FailsClosed_WhenSigningEvidenceJsonIsMalformed()
    {
        var sourceRoot = Path.Combine(_tempDir, "src-malformed-signing");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "bin"));
        var payloadFile = Path.Combine(sourceRoot, "bin", "app.exe");
        var installerFile = Path.Combine(_tempDir, "Setup-malformed-signing.exe");
        File.WriteAllText(payloadFile, "payload");
        File.WriteAllText(installerFile, "installer");

        var project = InstallerProjectFactory.CreateNew("EvidenceApp", "1.2.3", "ACME", sourceRoot);
        project.LicenseText = "Enterprise license terms";
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = payloadFile,
                    DestinationPath = "bin/app.exe",
                    IsRequired = true
                }
            }
        });
        var policy = new InstallerPolicy { RequireSignedArtifacts = true, RequireLicenseMetadata = true };
        var evaluation = InstallerPolicyEvaluator.EvaluateProject(policy, project);
        var generated = ReleaseEvidenceGenerator.Generate(project, new ReleaseEvidenceOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "malformed-signing-evidence"),
            InstallerPath = installerFile,
            SourceRoot = sourceRoot,
            PolicyEvaluation = evaluation
        });
        var signingEvidencePath = Path.Combine(_tempDir, "malformed-signing-evidence.json");
        File.WriteAllText(signingEvidencePath, "{ nope");

        var verification = ReleaseEvidenceVerifier.Verify(project, new ReleaseEvidenceVerificationOptions
        {
            EvidenceDirectory = generated.OutputDirectory,
            InstallerPath = installerFile,
            SourceRoot = sourceRoot,
            SigningEvidencePath = signingEvidencePath,
            PolicyEvaluation = evaluation
        });

        verification.Success.Should().BeFalse();
        verification.Diagnostics.Should().Contain(d => d.Code == "BI2242" && d.Path == "SigningEvidence");
    }

    [Fact]
    public void Verify_FailsWhenPayloadHashDriftsAfterEvidenceGeneration()
    {
        var sourceRoot = Path.Combine(_tempDir, "src-drift");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "bin"));
        var payloadFile = Path.Combine(sourceRoot, "bin", "app.exe");
        File.WriteAllText(payloadFile, "payload-v1");

        var project = InstallerProjectFactory.CreateNew("EvidenceApp", "1.2.3", "ACME", sourceRoot);
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = payloadFile,
                    DestinationPath = "bin/app.exe",
                    IsRequired = true
                }
            }
        });
        var generated = ReleaseEvidenceGenerator.Generate(project, new ReleaseEvidenceOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "drift-evidence"),
            SourceRoot = sourceRoot
        });
        File.WriteAllText(payloadFile, "payload-v2");

        var verification = ReleaseEvidenceVerifier.Verify(project, new ReleaseEvidenceVerificationOptions
        {
            EvidenceDirectory = generated.OutputDirectory,
            SourceRoot = sourceRoot
        });

        verification.Success.Should().BeFalse();
        verification.Diagnostics.Should().Contain(d => d.Code == "BI2221" && d.Path == "bin/app.exe");
        verification.Artifacts.Should().Contain(a => a.Kind == "sbom" && a.Name == "bin/app.exe" && !a.Matched);
        using var report = JsonDocument.Parse(File.ReadAllText(verification.ReportPath));
        report.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
    }
}
