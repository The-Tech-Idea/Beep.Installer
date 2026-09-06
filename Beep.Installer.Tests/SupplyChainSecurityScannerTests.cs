using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Policy;
using Beep.Installer.Security;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class SupplyChainSecurityScannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _payloadFile;

    public SupplyChainSecurityScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepSupplyChain_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(_sourceDir);
        _payloadFile = Path.Combine(_sourceDir, "bin", "app.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(_payloadFile)!);
        File.WriteAllText(_payloadFile, "known-good");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Scan_RecordsArtifactHashesWithoutLeakingAbsoluteSourceRoot()
    {
        var project = Project();
        var reportPath = Path.Combine(_tempDir, "reports", "supply-chain.json");

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            OutputPath = reportPath
        });

        report.HasErrors.Should().BeFalse();
        report.Artifacts.Should().ContainSingle(a => a.Kind == "payload" && a.Path == "bin/app.exe");
        report.Artifacts.Single().Sha256.Should().Be(Sha256(_payloadFile));
        File.Exists(reportPath).Should().BeTrue();
        File.ReadAllText(reportPath).Should().NotContain(_sourceDir.Replace("\\", "\\\\"));
    }

    [Fact]
    public void Scan_FailsWhenDeclaredPayloadHashDoesNotMatch()
    {
        var project = Project();
        var policy = new InstallerPolicy
        {
            RequiredFileSha256 =
            {
                ["bin/app.exe"] = new string('a', 64)
            }
        };

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions { Policy = policy });

        report.HasErrors.Should().BeTrue();
        report.Findings.Should().Contain(f => f.Code == "BI9003");
    }

    [Fact]
    public void Scan_FailsWhenPayloadHashIsDenied()
    {
        var project = Project();
        var policy = new InstallerPolicy();
        policy.DeniedSha256.Add(Sha256(_payloadFile));

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions { Policy = policy });

        report.HasErrors.Should().BeTrue();
        report.Findings.Should().Contain(f => f.Code == "BI9002");
    }

    [Fact]
    public void Scan_DowngradesMatchingFindingWhenSignedWaiverIsActive()
    {
        var project = Project();
        var sha256 = Sha256(_payloadFile);
        var policy = new InstallerPolicy();
        policy.DeniedSha256.Add(sha256);
        using var rsa = RSA.Create(2048);
        policy.TrustedWaiverPublicKeys.Add(rsa.ExportSubjectPublicKeyInfoPem());
        var waiver = Waiver("WAIVE-001", "BI9002", sha256, DateTimeOffset.UtcNow.AddDays(1));
        SignWaiver(rsa, waiver);
        policy.SupplyChainWaivers.Add(waiver);

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions { Policy = policy });

        report.HasErrors.Should().BeFalse();
        report.Findings.Should().ContainSingle(f => f.Code == "BI9002" && f.Waived);
        var finding = report.Findings.Single();
        finding.Severity.Should().Be("warning");
        finding.WaiverId.Should().Be("WAIVE-001");
        finding.WaiverApprovedBy.Should().Be("security@example.test");
    }

    [Fact]
    public void Scan_FailsWhenMatchingWaiverSignatureIsTampered()
    {
        var project = Project();
        var sha256 = Sha256(_payloadFile);
        var policy = new InstallerPolicy();
        policy.DeniedSha256.Add(sha256);
        using var rsa = RSA.Create(2048);
        policy.TrustedWaiverPublicKeys.Add(rsa.ExportSubjectPublicKeyInfoPem());
        var waiver = Waiver("WAIVE-TAMPERED", "BI9002", sha256, DateTimeOffset.UtcNow.AddDays(1));
        SignWaiver(rsa, waiver);
        waiver.Reason = "Changed after approval.";
        policy.SupplyChainWaivers.Add(waiver);

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions { Policy = policy });

        report.HasErrors.Should().BeTrue();
        report.Findings.Should().Contain(f => f.Code == "BI9002" && !f.Waived);
        report.Findings.Should().Contain(f => f.Code == "BI9009");
    }

    [Fact]
    public void Scan_FailsWhenMatchingWaiverIsSignedByUntrustedKey()
    {
        var project = Project();
        var sha256 = Sha256(_payloadFile);
        var policy = new InstallerPolicy();
        policy.DeniedSha256.Add(sha256);
        using var trusted = RSA.Create(2048);
        using var untrusted = RSA.Create(2048);
        policy.TrustedWaiverPublicKeys.Add(trusted.ExportSubjectPublicKeyInfoPem());
        var waiver = Waiver("WAIVE-UNTRUSTED", "BI9002", sha256, DateTimeOffset.UtcNow.AddDays(1));
        SignWaiver(untrusted, waiver);
        policy.SupplyChainWaivers.Add(waiver);

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions { Policy = policy });

        report.HasErrors.Should().BeTrue();
        report.Findings.Should().Contain(f => f.Code == "BI9002" && !f.Waived);
        report.Findings.Should().Contain(f => f.Code == "BI9009");
    }

    [Fact]
    public void Scan_FailsWhenMatchingWaiverIsExpired()
    {
        var project = Project();
        var sha256 = Sha256(_payloadFile);
        var policy = new InstallerPolicy();
        policy.DeniedSha256.Add(sha256);
        policy.SupplyChainWaivers.Add(new SupplyChainWaiver
        {
            Id = "WAIVE-EXPIRED",
            FindingCode = "BI9002",
            Sha256 = sha256,
            ApprovedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            ApprovedBy = "security@example.test",
            Reason = "Expired waiver.",
            Signature = "policy-signature-placeholder"
        });

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions { Policy = policy });

        report.HasErrors.Should().BeTrue();
        report.Findings.Should().Contain(f => f.Code == "BI9002" && !f.Waived);
        report.Findings.Should().Contain(f => f.Code == "BI9007");
    }

    [Fact]
    public void Scan_FailsWhenMatchingWaiverIsUnsignedOrIncomplete()
    {
        var project = Project();
        var sha256 = Sha256(_payloadFile);
        var policy = new InstallerPolicy();
        policy.DeniedSha256.Add(sha256);
        policy.SupplyChainWaivers.Add(new SupplyChainWaiver
        {
            Id = "WAIVE-INCOMPLETE",
            FindingCode = "BI9002",
            Sha256 = sha256,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            ApprovedBy = "security@example.test",
            Reason = "Missing policy signature."
        });

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions { Policy = policy });

        report.HasErrors.Should().BeTrue();
        report.Findings.Should().Contain(f => f.Code == "BI9002" && !f.Waived);
        report.Findings.Should().Contain(f => f.Code == "BI9008");
    }

    [Fact]
    public void Scan_FailsWhenPolicyRequiresDeclaredPayloadHashes()
    {
        var project = Project();
        var policy = new InstallerPolicy { RequireDeclaredPayloadHashes = true };

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions { Policy = policy });

        report.HasErrors.Should().BeTrue();
        report.Findings.Should().Contain(f => f.Code == "BI9004");
    }

    [Fact]
    public void Scan_FailsWhenPolicyRequiresMalwareScannerButNoneIsConfigured()
    {
        var project = Project();
        var policy = new InstallerPolicy { RequireMalwareScan = true };

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            Policy = policy,
            UseBuiltInWindowsDefenderScanner = false
        });

        report.HasErrors.Should().BeTrue();
        report.Findings.Should().Contain(f => f.Code == "BI9010");
    }

    [Fact]
    public void Scan_UsesBuiltInMalwareScannerWhenPolicyRequiresMalwareScan()
    {
        var project = Project();
        var policy = new InstallerPolicy { RequireMalwareScan = true };
        var scanner = new FixedArtifactScanner("malware", "BuiltInDefender", "1.2.3", "Clean");

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            Policy = policy,
            BuiltInMalwareScanner = scanner
        });

        report.HasErrors.Should().BeFalse();
        report.Findings.Should().NotContain(f => f.Code == "BI9010");
        var scan = report.Artifacts.Single(a => a.Kind == "payload").Scans.Should().ContainSingle().Subject;
        scan.ToolName.Should().Be("BuiltInDefender");
    }

    [Fact]
    public void WindowsDefenderArtifactScanner_ReturnsCleanWhenMpCmdRunSucceeds()
    {
        var scanner = new WindowsDefenderArtifactScanner(new FakeDefenderCommandRunner(_payloadFile,
            new SecurityScannerCommandResult(0, "Scan completed successfully.", "")));

        var scan = scanner.Scan(_payloadFile, new SupplyChainArtifact
        {
            Kind = "payload",
            Path = "bin/app.exe",
            Sha256 = Sha256(_payloadFile),
            SizeBytes = new FileInfo(_payloadFile).Length
        });

        scan.Kind.Should().Be("malware");
        scan.ToolName.Should().Be("Microsoft Defender Antivirus");
        scan.ToolVersion.Should().Be("4.18.test");
        scan.Status.Should().Be("Clean");
        scan.Detections.Should().BeEmpty();
        scan.Error.Should().BeEmpty();
    }

    [Fact]
    public void WindowsDefenderArtifactScanner_ReturnsDetectionWhenMpCmdRunReportsThreat()
    {
        var scanner = new WindowsDefenderArtifactScanner(new FakeDefenderCommandRunner(_payloadFile,
            new SecurityScannerCommandResult(2, "Threat detected: EICAR-Test-File", "")));

        var scan = scanner.Scan(_payloadFile, new SupplyChainArtifact
        {
            Kind = "payload",
            Path = "bin/app.exe",
            Sha256 = Sha256(_payloadFile),
            SizeBytes = new FileInfo(_payloadFile).Length
        });

        scan.Status.Should().Be("Detected");
        scan.Detections.Should().ContainSingle(d => d.Id == "EICAR-Test-File");
        scan.Error.Should().BeEmpty();
    }

    [Fact]
    public void WindowsDefenderArtifactScanner_ReturnsErrorWhenMpCmdRunIsUnavailable()
    {
        var scanner = new WindowsDefenderArtifactScanner(new FakeDefenderCommandRunner(_payloadFile,
            new SecurityScannerCommandResult(-1, "", "Microsoft Defender MpCmdRun.exe was not found.")));

        var scan = scanner.Scan(_payloadFile, new SupplyChainArtifact
        {
            Kind = "payload",
            Path = "bin/app.exe",
            Sha256 = Sha256(_payloadFile),
            SizeBytes = new FileInfo(_payloadFile).Length
        });

        scan.Status.Should().Be("Error");
        scan.Error.Should().Contain("MpCmdRun.exe was not found");
        scan.Detections.Should().BeEmpty();
    }

    [Fact]
    public void ClamAvArtifactScanner_ReturnsCleanWhenClamscanSucceeds()
    {
        var scanner = new ClamAvArtifactScanner(new FakeSecurityScannerCommandRunner(
            _payloadFile,
            new[] { "--infected", "--no-summary", "--stdout", _payloadFile },
            "clamscan",
            "1.4.test",
            new SecurityScannerCommandResult(0, "", "")));

        var scan = scanner.Scan(_payloadFile, Artifact());

        scan.Kind.Should().Be("malware");
        scan.ToolName.Should().Be("ClamAV clamscan");
        scan.ToolVersion.Should().Be("1.4.test");
        scan.Status.Should().Be("Clean");
        scan.Detections.Should().BeEmpty();
        scan.Error.Should().BeEmpty();
    }

    [Fact]
    public void ClamAvArtifactScanner_ReturnsDetectionWhenClamscanFindsVirus()
    {
        var scanner = new ClamAvArtifactScanner(new FakeSecurityScannerCommandRunner(
            _payloadFile,
            new[] { "--infected", "--no-summary", "--stdout", _payloadFile },
            "clamscan",
            "1.4.test",
            new SecurityScannerCommandResult(1, $"{_payloadFile}: Eicar-Signature FOUND", "")));

        var scan = scanner.Scan(_payloadFile, Artifact());

        scan.Status.Should().Be("Detected");
        scan.Detections.Should().ContainSingle(d => d.Id == "Eicar-Signature");
        scan.Error.Should().BeEmpty();
    }

    [Fact]
    public void ClamAvArtifactScanner_ReturnsErrorWhenClamscanFails()
    {
        var scanner = new ClamAvArtifactScanner(new FakeSecurityScannerCommandRunner(
            _payloadFile,
            new[] { "--infected", "--no-summary", "--stdout", _payloadFile },
            "clamscan",
            "1.4.test",
            new SecurityScannerCommandResult(2, "", "scanner service unavailable")));

        var scan = scanner.Scan(_payloadFile, Artifact());

        scan.Status.Should().Be("Error");
        scan.Error.Should().Contain("scanner service unavailable");
        scan.Detections.Should().BeEmpty();
    }

    [Fact]
    public void Scan_FailsWhenPolicyRequiresVulnerabilityScannerButBuiltInOsvIsDisabled()
    {
        var project = Project();
        var policy = new InstallerPolicy { RequireVulnerabilityScan = true };

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            Policy = policy,
            UseBuiltInOsvScanner = false
        });

        report.HasErrors.Should().BeTrue();
        report.Findings.Should().Contain(f => f.Code == "BI9011");
    }

    [Fact]
    public void Scan_UsesBuiltInVulnerabilityScannerWhenPolicyRequiresVulnerabilityScan()
    {
        var project = Project();
        var policy = new InstallerPolicy { RequireVulnerabilityScan = true };
        var scanner = new FixedArtifactScanner("vulnerability", "BuiltInOSV", "2.3.4", "Clean");

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            Policy = policy,
            BuiltInVulnerabilityScanner = scanner
        });

        report.HasErrors.Should().BeFalse();
        report.Findings.Should().NotContain(f => f.Code == "BI9011");
        var scan = report.Artifacts.Single(a => a.Kind == "payload").Scans.Should().ContainSingle().Subject;
        scan.ToolName.Should().Be("BuiltInOSV");
    }

    [Fact]
    public void OsvArtifactVulnerabilityScanner_ReturnsCleanWhenOsvReportsNoVulnerabilities()
    {
        var scanner = new OsvArtifactVulnerabilityScanner(new FakeSecurityScannerCommandRunner(
            _payloadFile,
            new[] { "scan", "--format", "json", _payloadFile },
            "osv-scanner",
            "2.0.test",
            new SecurityScannerCommandResult(0, """{"results":[]}""", "")));

        var scan = scanner.Scan(_payloadFile, Artifact());

        scan.Kind.Should().Be("vulnerability");
        scan.ToolName.Should().Be("OSV-Scanner");
        scan.ToolVersion.Should().Be("2.0.test");
        scan.Status.Should().Be("Clean");
        scan.Detections.Should().BeEmpty();
        scan.Error.Should().BeEmpty();
    }

    [Fact]
    public void OsvArtifactVulnerabilityScanner_ReturnsDetectionFromJsonReport()
    {
        var json = """
            {
              "results": [
                {
                  "packages": [
                    {
                      "package": { "name": "Newtonsoft.Json", "version": "12.0.1" },
                      "vulnerabilities": [
                        { "id": "GHSA-5crp-9r3c-p9vr", "summary": "Improper handling of metadata." }
                      ]
                    }
                  ]
                }
              ]
            }
            """;
        var scanner = new OsvArtifactVulnerabilityScanner(new FakeSecurityScannerCommandRunner(
            _payloadFile,
            new[] { "scan", "--format", "json", _payloadFile },
            "osv-scanner",
            "2.0.test",
            new SecurityScannerCommandResult(1, json, "")));

        var scan = scanner.Scan(_payloadFile, Artifact());

        scan.Status.Should().Be("Detected");
        var detection = scan.Detections.Should().ContainSingle().Subject;
        detection.Id.Should().Be("GHSA-5crp-9r3c-p9vr");
        detection.Message.Should().Contain("Newtonsoft.Json@12.0.1");
        scan.Error.Should().BeEmpty();
    }

    [Fact]
    public void OsvArtifactVulnerabilityScanner_ReturnsErrorWhenOsvFails()
    {
        var scanner = new OsvArtifactVulnerabilityScanner(new FakeSecurityScannerCommandRunner(
            _payloadFile,
            new[] { "scan", "--format", "json", _payloadFile },
            "osv-scanner",
            "2.0.test",
            new SecurityScannerCommandResult(2, "", "lockfile parse failed")));

        var scan = scanner.Scan(_payloadFile, Artifact());

        scan.Status.Should().Be("Error");
        scan.Error.Should().Contain("lockfile parse failed");
        scan.Detections.Should().BeEmpty();
    }

    [Fact]
    public void TrivyFilesystemVulnerabilityScanner_ReturnsDetectionFromJsonReport()
    {
        var json = """
            {
              "Results": [
                {
                  "Target": "app",
                  "Vulnerabilities": [
                    {
                      "VulnerabilityID": "CVE-2026-0001",
                      "PkgName": "openssl",
                      "InstalledVersion": "1.0.0",
                      "Severity": "CRITICAL",
                      "Title": "test critical vulnerability"
                    }
                  ]
                }
              ]
            }
            """;
        var scanner = new TrivyFilesystemVulnerabilityScanner(new FakeSecurityScannerCommandRunner(
            _payloadFile,
            new[] { "fs", "--format", "json", "--exit-code", "1", _payloadFile },
            "trivy",
            "0.58.test",
            new SecurityScannerCommandResult(1, json, "")));

        var scan = scanner.Scan(_payloadFile, Artifact());

        scan.Kind.Should().Be("vulnerability");
        scan.ToolName.Should().Be("Trivy");
        scan.Status.Should().Be("Detected");
        var detection = scan.Detections.Should().ContainSingle().Subject;
        detection.Id.Should().Be("CVE-2026-0001");
        detection.Severity.Should().Be("error");
        detection.Message.Should().Contain("openssl@1.0.0");
    }

    [Fact]
    public void Scan_RecordsCleanScannerToolEvidence()
    {
        var project = Project();
        var scanner = new FixedArtifactScanner("malware", "Defender", "1.2.3", "Clean");

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            ArtifactScanners = new[] { scanner }
        });

        report.HasErrors.Should().BeFalse();
        var scan = report.Artifacts.Single(a => a.Kind == "payload").Scans.Should().ContainSingle().Subject;
        scan.Kind.Should().Be("malware");
        scan.ToolName.Should().Be("Defender");
        scan.ToolVersion.Should().Be("1.2.3");
        scan.Status.Should().Be("Clean");

        using var document = JsonDocument.Parse(SupplyChainSecurityScanner.ToJson(report));
        var jsonScan = document.RootElement.GetProperty("artifacts")[0].GetProperty("scans")[0];
        jsonScan.GetProperty("toolName").GetString().Should().Be("Defender");
        jsonScan.GetProperty("toolVersion").GetString().Should().Be("1.2.3");
    }

    [Fact]
    public void Scan_FailsWhenMalwareScannerDetectsThreat()
    {
        var project = Project();
        var scanner = new FixedArtifactScanner("malware", "Defender", "1.2.3", "Detected")
            .WithDetection("EICAR-Test-File", "error", "Test malware signature.");

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            ArtifactScanners = new[] { scanner }
        });

        report.HasErrors.Should().BeTrue();
        report.Artifacts.Single(a => a.Kind == "payload").Scans.Single().Detections.Should().ContainSingle();
        report.Findings.Should().Contain(f => f.Code == "BI9012" && f.Message.Contains("EICAR-Test-File", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_FailsWhenVulnerabilityScannerDetectsVulnerability()
    {
        var project = Project();
        var policy = new InstallerPolicy { RequireVulnerabilityScan = true };
        var scanner = new FixedArtifactScanner("vulnerability", "DependencyAudit", "4.5.6", "Detected")
            .WithDetection("CVE-2026-0001", "error", "Critical vulnerable component.");

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            Policy = policy,
            ArtifactScanners = new[] { scanner }
        });

        report.HasErrors.Should().BeTrue();
        report.Findings.Should().Contain(f => f.Code == "BI9013" && f.Message.Contains("CVE-2026-0001", StringComparison.Ordinal));
        report.Findings.Should().NotContain(f => f.Code == "BI9011");
    }

    [Fact]
    public void Scan_FailsWhenScannerAdapterThrows()
    {
        var project = Project();
        var scanner = new ThrowingArtifactScanner("malware");

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            ArtifactScanners = new IArtifactSecurityScanner[] { scanner }
        });

        report.HasErrors.Should().BeTrue();
        report.Artifacts.Single(a => a.Kind == "payload").Scans.Single().Status.Should().Be("Error");
        report.Findings.Should().Contain(f => f.Code == "BI9014");
    }

    [Fact]
    public void Scan_PassesWhenRequiredPayloadHashMatches()
    {
        var project = Project();
        var policy = new InstallerPolicy
        {
            RequireDeclaredPayloadHashes = true,
            RequiredFileSha256 =
            {
                ["bin/app.exe"] = Sha256(_payloadFile)
            }
        };

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions { Policy = policy });

        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void ToJson_ProducesMachineReadableFindings()
    {
        var project = Project();
        var policy = new InstallerPolicy { RequireDeclaredPayloadHashes = true };
        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions { Policy = policy });

        using var document = JsonDocument.Parse(SupplyChainSecurityScanner.ToJson(report));

        document.RootElement.GetProperty("findings").EnumerateArray()
            .Should().Contain(f => f.GetProperty("code").GetString() == "BI9004");
    }

    [Fact]
    public void Scan_FailsWhenPolicyRequiresSignedArtifactsAndNestedExeIsUnsigned()
    {
        var project = Project();
        var policy = new InstallerPolicy { RequireSignedArtifacts = true };

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            Policy = policy,
            SignatureVerifier = new FixedSignatureVerifier(false, "NotSigned", "")
        });

        report.HasErrors.Should().BeTrue();
        report.Artifacts.Single(a => a.Kind == "payload").Signature.Should().NotBeNull();
        report.Findings.Should().Contain(f => f.Code == "BI9005");
    }

    [Fact]
    public void Scan_PassesSignedArtifactWhenSignerSubjectIsAllowed()
    {
        var project = Project();
        var policy = new InstallerPolicy
        {
            RequireSignedArtifacts = true,
            AllowedArtifactSignerSubjects = { "CN=The Tech Idea" }
        };

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            Policy = policy,
            SignatureVerifier = new FixedSignatureVerifier(true, "Valid", "CN=The Tech Idea")
        });

        report.HasErrors.Should().BeFalse();
        report.Artifacts.Single(a => a.Kind == "payload").Signature!.Trusted.Should().BeTrue();
    }

    [Fact]
    public void Scan_RecordsCertificateAndTimestampEvidenceForSignedArtifacts()
    {
        var project = Project();
        var policy = new InstallerPolicy { RequireSignedArtifacts = true };

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            Policy = policy,
            SignatureVerifier = new FixedSignatureVerifier(true, "Valid", "CN=The Tech Idea")
            {
                Issuer = "CN=Enterprise Issuing CA",
                Thumbprint = "ABCDEF0123456789",
                NotBeforeUtc = "2026-01-01T00:00:00.0000000Z",
                NotAfterUtc = "2027-01-01T00:00:00.0000000Z",
                TimestampSubject = "CN=Trusted Timestamp Authority",
                TimestampThumbprint = "1234567890ABCDEF",
                TimestampNotBeforeUtc = "2025-01-01T00:00:00.0000000Z",
                TimestampNotAfterUtc = "2030-01-01T00:00:00.0000000Z",
                ChainStatus = "",
                RevocationMode = "Online",
                Chain =
                {
                    new ArtifactSignatureCertificateEvidence
                    {
                        Subject = "CN=The Tech Idea",
                        Issuer = "CN=Enterprise Issuing CA",
                        Thumbprint = "ABCDEF0123456789",
                        NotBeforeUtc = "2026-01-01T00:00:00.0000000Z",
                        NotAfterUtc = "2027-01-01T00:00:00.0000000Z",
                        Status = ""
                    },
                    new ArtifactSignatureCertificateEvidence
                    {
                        Subject = "CN=Enterprise Issuing CA",
                        Issuer = "CN=Enterprise Root CA",
                        Thumbprint = "CA00000000000001",
                        NotBeforeUtc = "2025-01-01T00:00:00.0000000Z",
                        NotAfterUtc = "2035-01-01T00:00:00.0000000Z",
                        Status = ""
                    }
                },
                TimestampChainStatus = "RevocationStatusUnknown: unable to check revocation",
                TimestampChain =
                {
                    new ArtifactSignatureCertificateEvidence
                    {
                        Subject = "CN=Trusted Timestamp Authority",
                        Issuer = "CN=Timestamp Issuing CA",
                        Thumbprint = "1234567890ABCDEF",
                        NotBeforeUtc = "2025-01-01T00:00:00.0000000Z",
                        NotAfterUtc = "2030-01-01T00:00:00.0000000Z",
                        Status = "RevocationStatusUnknown: unable to check revocation"
                    }
                }
            }
        });

        var signature = report.Artifacts.Single(a => a.Kind == "payload").Signature!;
        signature.Issuer.Should().Be("CN=Enterprise Issuing CA");
        signature.Thumbprint.Should().Be("ABCDEF0123456789");
        signature.NotAfterUtc.Should().Be("2027-01-01T00:00:00.0000000Z");
        signature.TimestampSubject.Should().Be("CN=Trusted Timestamp Authority");
        signature.TimestampThumbprint.Should().Be("1234567890ABCDEF");
        signature.RevocationMode.Should().Be("Online");
        signature.Chain.Should().HaveCount(2);
        signature.Chain[1].Thumbprint.Should().Be("CA00000000000001");
        signature.TimestampChain.Should().ContainSingle(c => c.Subject == "CN=Trusted Timestamp Authority");
        signature.TimestampChainStatus.Should().Contain("RevocationStatusUnknown");

        using var document = JsonDocument.Parse(SupplyChainSecurityScanner.ToJson(report));
        var jsonSignature = document.RootElement.GetProperty("artifacts")[0].GetProperty("signature");
        jsonSignature.GetProperty("thumbprint").GetString().Should().Be("ABCDEF0123456789");
        jsonSignature.GetProperty("timestampSubject").GetString().Should().Be("CN=Trusted Timestamp Authority");
        jsonSignature.GetProperty("revocationMode").GetString().Should().Be("Online");
        jsonSignature.GetProperty("chain")[1].GetProperty("thumbprint").GetString().Should().Be("CA00000000000001");
        jsonSignature.GetProperty("timestampChain")[0].GetProperty("status").GetString().Should().Contain("RevocationStatusUnknown");
    }

    [Fact]
    public void Scan_FailsSignedArtifactWhenSignerSubjectIsNotAllowed()
    {
        var project = Project();
        var policy = new InstallerPolicy
        {
            AllowedArtifactSignerSubjects = { "CN=The Tech Idea" }
        };

        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            Policy = policy,
            SignatureVerifier = new FixedSignatureVerifier(true, "Valid", "CN=Contoso")
        });

        report.HasErrors.Should().BeTrue();
        report.Findings.Should().Contain(f => f.Code == "BI9006");
    }

    private InstallProject Project()
    {
        var project = InstallerProjectFactory.CreateNew("SupplyChainApp", "1.0.0", "ACME", _sourceDir);
        project.Components.Clear();
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true,
            Files = new List<FileCopyOperation>
            {
                new()
                {
                    SourcePath = _payloadFile,
                    DestinationPath = "bin/app.exe",
                    Description = "App"
                }
            }
        });
        return project;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static SupplyChainWaiver Waiver(string id, string code, string sha256, DateTimeOffset expiresAtUtc)
        => new()
        {
            Id = id,
            FindingCode = code,
            Sha256 = sha256,
            ApprovedAtUtc = DateTimeOffset.Parse("2026-08-30T00:00:00Z"),
            ExpiresAtUtc = expiresAtUtc,
            ApprovedBy = "security@example.test",
            Reason = "Known test artifact allowed for staging."
        };

    private static void SignWaiver(RSA rsa, SupplyChainWaiver waiver)
    {
        var payload = Encoding.UTF8.GetBytes(RsaSha256WaiverSignatureVerifier.CanonicalWaiverPayload(waiver));
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        waiver.Signature = "rsa-sha256:" + Convert.ToBase64String(signature);
    }

    private sealed class FixedSignatureVerifier : IArtifactSignatureVerifier
    {
        private readonly bool _trusted;
        private readonly string _status;
        private readonly string _subject;

        public string Issuer { get; init; } = "";
        public string Thumbprint { get; init; } = "";
        public string NotBeforeUtc { get; init; } = "";
        public string NotAfterUtc { get; init; } = "";
        public string TimestampSubject { get; init; } = "";
        public string TimestampThumbprint { get; init; } = "";
        public string TimestampNotBeforeUtc { get; init; } = "";
        public string TimestampNotAfterUtc { get; init; } = "";
        public string ChainStatus { get; init; } = "";
        public List<ArtifactSignatureCertificateEvidence> Chain { get; init; } = new();
        public string TimestampChainStatus { get; init; } = "";
        public List<ArtifactSignatureCertificateEvidence> TimestampChain { get; init; } = new();
        public string RevocationMode { get; init; } = "";

        public FixedSignatureVerifier(bool trusted, string status, string subject)
        {
            _trusted = trusted;
            _status = status;
            _subject = subject;
        }

        public ArtifactSignatureStatus Verify(string path)
            => new()
            {
                Inspected = true,
                Trusted = _trusted,
                Status = _status,
                Subject = _subject,
                Issuer = Issuer,
                Thumbprint = Thumbprint,
                NotBeforeUtc = NotBeforeUtc,
                NotAfterUtc = NotAfterUtc,
                TimestampSubject = TimestampSubject,
                TimestampThumbprint = TimestampThumbprint,
                TimestampNotBeforeUtc = TimestampNotBeforeUtc,
                TimestampNotAfterUtc = TimestampNotAfterUtc,
                ChainStatus = ChainStatus,
                Chain = Chain.ToList(),
                TimestampChainStatus = TimestampChainStatus,
                TimestampChain = TimestampChain.ToList(),
                RevocationMode = RevocationMode
            };
    }

    private sealed class FixedArtifactScanner : IArtifactSecurityScanner
    {
        private readonly List<ArtifactSecurityDetection> _detections = new();
        private readonly string _status;

        public FixedArtifactScanner(string kind, string toolName, string toolVersion, string status)
        {
            Kind = kind;
            ToolName = toolName;
            ToolVersion = toolVersion;
            _status = status;
        }

        public string Kind { get; }
        private string ToolName { get; }
        private string ToolVersion { get; }

        public FixedArtifactScanner WithDetection(string id, string severity, string message)
        {
            _detections.Add(new ArtifactSecurityDetection
            {
                Id = id,
                Severity = severity,
                Message = message
            });
            return this;
        }

        public ArtifactSecurityScanResult Scan(string path, SupplyChainArtifact artifact)
            => new()
            {
                Kind = Kind,
                ToolName = ToolName,
                ToolVersion = ToolVersion,
                Status = _status,
                Message = "Scan completed.",
                Detections = _detections.ToList()
            };
    }

    private sealed class ThrowingArtifactScanner : IArtifactSecurityScanner
    {
        public ThrowingArtifactScanner(string kind)
        {
            Kind = kind;
        }

        public string Kind { get; }

        public ArtifactSecurityScanResult Scan(string path, SupplyChainArtifact artifact)
            => throw new InvalidOperationException("scanner exploded");
    }

    private SupplyChainArtifact Artifact()
        => new()
        {
            Kind = "payload",
            Path = "bin/app.exe",
            Sha256 = Sha256(_payloadFile),
            SizeBytes = new FileInfo(_payloadFile).Length
        };

    private sealed class FakeDefenderCommandRunner : ISecurityScannerCommandRunner
    {
        private readonly string _expectedPath;
        private readonly SecurityScannerCommandResult _result;

        public FakeDefenderCommandRunner(string expectedPath, SecurityScannerCommandResult result)
        {
            _expectedPath = expectedPath;
            _result = result;
        }

        public string ToolPath => "MpCmdRun.exe";
        public string ToolVersion => "4.18.test";

        public SecurityScannerCommandResult Run(IReadOnlyList<string> arguments)
        {
            arguments.Should().ContainInOrder("-Scan", "-ScanType", "3", "-File", _expectedPath, "-DisableRemediation");
            return _result;
        }
    }

    private sealed class FakeSecurityScannerCommandRunner : ISecurityScannerCommandRunner
    {
        private readonly IReadOnlyList<string> _expectedArguments;
        private readonly SecurityScannerCommandResult _result;

        public FakeSecurityScannerCommandRunner(
            string toolPath,
            IReadOnlyList<string> expectedArguments,
            string toolName,
            string toolVersion,
            SecurityScannerCommandResult result)
        {
            ToolPath = toolPath;
            _expectedArguments = expectedArguments;
            ToolName = toolName;
            ToolVersion = toolVersion;
            _result = result;
        }

        public string ToolPath { get; }
        public string ToolName { get; }
        public string ToolVersion { get; }

        public SecurityScannerCommandResult Run(IReadOnlyList<string> arguments)
        {
            arguments.Should().ContainInOrder(_expectedArguments);
            return _result;
        }
    }
}
