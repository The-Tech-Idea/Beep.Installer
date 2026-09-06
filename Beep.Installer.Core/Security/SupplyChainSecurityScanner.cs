using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Models;

namespace Beep.Installer.Security;

public sealed class SupplyChainSecurityOptions
{
    public Beep.Installer.Policy.InstallerPolicy? Policy { get; init; }
    public string? InstallerPath { get; init; }
    public string? OutputPath { get; init; }
    public IArtifactSignatureVerifier? SignatureVerifier { get; init; }
    public IWaiverSignatureVerifier? WaiverSignatureVerifier { get; init; }
    public IReadOnlyList<IArtifactSecurityScanner> ArtifactScanners { get; init; } = Array.Empty<IArtifactSecurityScanner>();
    public bool UseBuiltInWindowsDefenderScanner { get; init; } = true;
    public bool UseBuiltInOsvScanner { get; init; } = true;
    public IArtifactSecurityScanner? BuiltInMalwareScanner { get; init; }
    public IArtifactSecurityScanner? BuiltInVulnerabilityScanner { get; init; }
}

public sealed class SupplyChainSecurityReport
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "1.0";
    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("productName")]
    public string ProductName { get; init; } = "";
    [JsonPropertyName("productVersion")]
    public string ProductVersion { get; init; } = "";
    [JsonPropertyName("planHash")]
    public string PlanHash { get; init; } = "";
    [JsonPropertyName("artifacts")]
    public List<SupplyChainArtifact> Artifacts { get; init; } = new();
    [JsonPropertyName("findings")]
    public List<SupplyChainFinding> Findings { get; init; } = new();
    [JsonPropertyName("hasErrors")]
    public bool HasErrors => Findings.Any(f => f.Severity == "error");
}

public sealed class SupplyChainArtifact
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";
    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = "";
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }
    [JsonPropertyName("signature")]
    public ArtifactSignatureStatus? Signature { get; init; }
    [JsonPropertyName("scans")]
    public List<ArtifactSecurityScanResult> Scans { get; init; } = new();
}

public sealed class SupplyChainFinding
{
    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "info";
    [JsonPropertyName("code")]
    public string Code { get; init; } = "";
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";
    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = "";
    [JsonPropertyName("waived")]
    public bool Waived { get; init; }
    [JsonPropertyName("waiverId")]
    public string WaiverId { get; init; } = "";
    [JsonPropertyName("waiverReason")]
    public string WaiverReason { get; init; } = "";
    [JsonPropertyName("waiverApprovedBy")]
    public string WaiverApprovedBy { get; init; } = "";
    [JsonPropertyName("waiverExpiresAtUtc")]
    public DateTimeOffset? WaiverExpiresAtUtc { get; init; }
}

public sealed class ArtifactSignatureStatus
{
    [JsonPropertyName("inspected")]
    public bool Inspected { get; init; }
    [JsonPropertyName("trusted")]
    public bool Trusted { get; init; }
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";
    [JsonPropertyName("subject")]
    public string Subject { get; init; } = "";
    [JsonPropertyName("issuer")]
    public string Issuer { get; init; } = "";
    [JsonPropertyName("thumbprint")]
    public string Thumbprint { get; init; } = "";
    [JsonPropertyName("notBeforeUtc")]
    public string NotBeforeUtc { get; init; } = "";
    [JsonPropertyName("notAfterUtc")]
    public string NotAfterUtc { get; init; } = "";
    [JsonPropertyName("timestampSubject")]
    public string TimestampSubject { get; init; } = "";
    [JsonPropertyName("timestampThumbprint")]
    public string TimestampThumbprint { get; init; } = "";
    [JsonPropertyName("timestampNotBeforeUtc")]
    public string TimestampNotBeforeUtc { get; init; } = "";
    [JsonPropertyName("timestampNotAfterUtc")]
    public string TimestampNotAfterUtc { get; init; } = "";
    [JsonPropertyName("chainStatus")]
    public string ChainStatus { get; init; } = "";
    [JsonPropertyName("chain")]
    public List<ArtifactSignatureCertificateEvidence> Chain { get; init; } = new();
    [JsonPropertyName("timestampChainStatus")]
    public string TimestampChainStatus { get; init; } = "";
    [JsonPropertyName("timestampChain")]
    public List<ArtifactSignatureCertificateEvidence> TimestampChain { get; init; } = new();
    [JsonPropertyName("revocationMode")]
    public string RevocationMode { get; init; } = "";
    [JsonPropertyName("error")]
    public string Error { get; init; } = "";
}

public sealed class ArtifactSignatureCertificateEvidence
{
    [JsonPropertyName("subject")]
    public string Subject { get; init; } = "";
    [JsonPropertyName("issuer")]
    public string Issuer { get; init; } = "";
    [JsonPropertyName("thumbprint")]
    public string Thumbprint { get; init; } = "";
    [JsonPropertyName("notBeforeUtc")]
    public string NotBeforeUtc { get; init; } = "";
    [JsonPropertyName("notAfterUtc")]
    public string NotAfterUtc { get; init; } = "";
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";
}

public interface IArtifactSignatureVerifier
{
    ArtifactSignatureStatus Verify(string path);
}

public sealed class ArtifactSecurityScanResult
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";
    [JsonPropertyName("toolName")]
    public string ToolName { get; init; } = "";
    [JsonPropertyName("toolVersion")]
    public string ToolVersion { get; init; } = "";
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";
    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
    [JsonPropertyName("detections")]
    public List<ArtifactSecurityDetection> Detections { get; init; } = new();
    [JsonPropertyName("error")]
    public string Error { get; init; } = "";
}

public sealed class ArtifactSecurityDetection
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "error";
    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

public interface IArtifactSecurityScanner
{
    string Kind { get; }
    ArtifactSecurityScanResult Scan(string path, SupplyChainArtifact artifact);
}

public sealed class WaiverSignatureVerification
{
    public bool Trusted { get; init; }
    public string Status { get; init; } = "";
    public string Error { get; init; } = "";
}

public interface IWaiverSignatureVerifier
{
    WaiverSignatureVerification Verify(
        Beep.Installer.Policy.SupplyChainWaiver waiver,
        Beep.Installer.Policy.InstallerPolicy policy);
}

public sealed class SupplyChainSecurityScanner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = SupplyChainSecurityJsonContext.Default
    };

    public SupplyChainSecurityReport Scan(InstallProject project, SupplyChainSecurityOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        options ??= new SupplyChainSecurityOptions();
        var verifier = options.SignatureVerifier ?? new WindowsAuthenticodeSignatureVerifier();
        var waiverVerifier = options.WaiverSignatureVerifier ?? new RsaSha256WaiverSignatureVerifier();
        var artifactScanners = EffectiveArtifactScanners(options);

        var compiler = new Beep.Installer.Engine.InstallPlanCompiler();
        var compile = compiler.Compile(project);
        var report = new SupplyChainSecurityReport
        {
            ProductName = project.AppName ?? "",
            ProductVersion = project.AppVersion ?? "",
            PlanHash = compile.Plan?.PlanHash ?? ""
        };

        foreach (var diagnostic in compile.Diagnostics.Where(d => d.Severity == Beep.Installer.Engine.ProjectSchemaDiagnosticSeverity.Error))
        {
            AddFinding(options.Policy, waiverVerifier, report, new SupplyChainFinding
            {
                Severity = "error",
                Code = "BI9000",
                Path = diagnostic.Path,
                Message = diagnostic.Message
            });
        }

        if (compile.Plan is not null)
        {
            foreach (var candidate in CandidateFiles(compile.Plan, options.InstallerPath))
                InspectCandidate(project, candidate, options.Policy, verifier, waiverVerifier, artifactScanners, report);
        }

        WriteReportIfRequested(report, options.OutputPath);
        return report;
    }

    public static string ToJson(SupplyChainSecurityReport report)
        => JsonSerializer.Serialize(report, JsonOptions);

    private static IEnumerable<FileCandidate> CandidateFiles(Beep.Installer.Engine.CompiledInstallPlan plan, string? installerPath)
    {
        if (!string.IsNullOrWhiteSpace(installerPath))
            yield return new FileCandidate("installer", "Installer", installerPath, Required: true);

        foreach (var operation in plan.Operations)
        {
            switch (operation.Type)
            {
                case "file.copy":
                    yield return new FileCandidate("payload", $"{operation.Id}.source", Input(operation, "source"), IsRequired(operation));
                    break;
                case "custom-action.run":
                    yield return new FileCandidate("custom-action", $"{operation.Id}.path", Input(operation, "path"), IsRequired(operation));
                    break;
                case "certificate.install":
                    yield return new FileCandidate("certificate", $"{operation.Id}.sourcePath", Input(operation, "sourcePath"), Required: true);
                    break;
                case "driver.package":
                    yield return new FileCandidate("driver", $"{operation.Id}.infPath", Input(operation, "infPath"), Required: true);
                    break;
            }
        }
    }

    private static void InspectCandidate(
        InstallProject project,
        FileCandidate candidate,
        Beep.Installer.Policy.InstallerPolicy? policy,
        IArtifactSignatureVerifier verifier,
        IWaiverSignatureVerifier waiverVerifier,
        IReadOnlyList<IArtifactSecurityScanner> artifactScanners,
        SupplyChainSecurityReport report)
    {
        if (string.IsNullOrWhiteSpace(candidate.Path))
            return;

        if (IsRuntimeMacro(candidate.Path) || IsRemote(candidate.Path))
            return;

        var fullPath = Path.GetFullPath(candidate.Path);
        if (!File.Exists(fullPath))
        {
            if (candidate.Required)
            {
                AddFinding(policy, waiverVerifier, report, new SupplyChainFinding
                {
                    Severity = "error",
                    Code = "BI9001",
                    Path = candidate.DisplayPath,
                    Message = $"Required {candidate.Kind} file is missing: {SafePath(project, fullPath)}"
                });
            }
            return;
        }

        var info = new FileInfo(fullPath);
        var sha256 = ComputeSha256(fullPath);
        var signature = ShouldInspectSignature(candidate, fullPath, policy)
            ? verifier.Verify(fullPath)
            : null;
        var artifact = new SupplyChainArtifact
        {
            Kind = candidate.Kind,
            Path = SafePath(project, fullPath),
            Sha256 = sha256,
            SizeBytes = info.Length,
            Signature = signature
        };
        report.Artifacts.Add(artifact);

        RunArtifactScanners(policy, waiverVerifier, artifactScanners, report, candidate.DisplayPath, fullPath, artifact);

        if (policy?.DeniedSha256.Contains(sha256, StringComparer.OrdinalIgnoreCase) == true)
        {
            AddFinding(policy, waiverVerifier, report, new SupplyChainFinding
            {
                Severity = "error",
                Code = "BI9002",
                Path = candidate.DisplayPath,
                Sha256 = sha256,
                Message = $"Artifact hash is denied by policy: {SafePath(project, fullPath)}"
            });
        }

        var expected = ExpectedHash(project, fullPath, policy);
        if (!string.IsNullOrWhiteSpace(expected) && !sha256.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            AddFinding(policy, waiverVerifier, report, new SupplyChainFinding
            {
                Severity = "error",
                Code = "BI9003",
                Path = candidate.DisplayPath,
                Sha256 = sha256,
                Message = $"Artifact hash does not match policy for {SafePath(project, fullPath)}."
            });
        }

        if (policy?.RequireDeclaredPayloadHashes == true
            && candidate.Kind == "payload"
            && string.IsNullOrWhiteSpace(expected))
        {
            AddFinding(policy, waiverVerifier, report, new SupplyChainFinding
            {
                Severity = "error",
                Code = "BI9004",
                Path = candidate.DisplayPath,
                Sha256 = sha256,
                Message = $"Policy requires a declared SHA-256 hash for payload file {SafePath(project, fullPath)}."
            });
        }

        if (policy?.RequireSignedArtifacts == true && IsSignable(fullPath))
        {
            if (signature?.Trusted != true)
            {
                AddFinding(policy, waiverVerifier, report, new SupplyChainFinding
                {
                    Severity = "error",
                    Code = "BI9005",
                    Path = candidate.DisplayPath,
                    Sha256 = sha256,
                    Message = $"Policy requires signed artifacts, but {SafePath(project, fullPath)} is not trusted ({signature?.Status ?? "not inspected"})."
                });
            }
        }

        if (signature?.Trusted == true
            && policy?.AllowedArtifactSignerSubjects.Count > 0
            && !policy.AllowedArtifactSignerSubjects.Any(allowed => signature.Subject.Contains(allowed, StringComparison.OrdinalIgnoreCase)))
        {
            AddFinding(policy, waiverVerifier, report, new SupplyChainFinding
            {
                Severity = "error",
                Code = "BI9006",
                Path = candidate.DisplayPath,
                Sha256 = sha256,
                Message = $"Artifact signer subject is not allowed by policy for {SafePath(project, fullPath)}."
            });
        }
    }

    private static void RunArtifactScanners(
        Beep.Installer.Policy.InstallerPolicy? policy,
        IWaiverSignatureVerifier waiverVerifier,
        IReadOnlyList<IArtifactSecurityScanner> artifactScanners,
        SupplyChainSecurityReport report,
        string displayPath,
        string fullPath,
        SupplyChainArtifact artifact)
    {
        var requireMalware = policy?.RequireMalwareScan == true;
        var requireVulnerability = policy?.RequireVulnerabilityScan == true;
        if (!requireMalware && !requireVulnerability && artifactScanners.Count == 0)
            return;

        if (requireMalware && !artifactScanners.Any(scanner => scanner.Kind.Equals("malware", StringComparison.OrdinalIgnoreCase)))
        {
            AddFinding(policy, waiverVerifier, report, new SupplyChainFinding
            {
                Severity = "error",
                Code = "BI9010",
                Path = displayPath,
                Sha256 = artifact.Sha256,
                Message = $"Policy requires malware scanning, but no malware scanner adapter is configured for {artifact.Path}."
            });
        }

        if (requireVulnerability && !artifactScanners.Any(scanner => scanner.Kind.Equals("vulnerability", StringComparison.OrdinalIgnoreCase)))
        {
            AddFinding(policy, waiverVerifier, report, new SupplyChainFinding
            {
                Severity = "error",
                Code = "BI9011",
                Path = displayPath,
                Sha256 = artifact.Sha256,
                Message = $"Policy requires vulnerability scanning, but no vulnerability scanner adapter is configured for {artifact.Path}."
            });
        }

        foreach (var scanner in artifactScanners)
        {
            ArtifactSecurityScanResult result;
            try
            {
                result = scanner.Scan(fullPath, artifact);
            }
            catch (Exception ex)
            {
                result = new ArtifactSecurityScanResult
                {
                    Kind = scanner.Kind,
                    ToolName = scanner.GetType().Name,
                    Status = "Error",
                    Error = ex.Message
                };
            }

            artifact.Scans.Add(result);
            if (result.Status.Equals("Error", StringComparison.OrdinalIgnoreCase))
            {
                AddFinding(policy, waiverVerifier, report, new SupplyChainFinding
                {
                    Severity = "error",
                    Code = "BI9014",
                    Path = displayPath,
                    Sha256 = artifact.Sha256,
                    Message = $"{ScannerLabel(result)} failed for {artifact.Path}: {result.Error}"
                });
            }

            foreach (var detection in result.Detections)
            {
                AddFinding(policy, waiverVerifier, report, new SupplyChainFinding
                {
                    Severity = string.IsNullOrWhiteSpace(detection.Severity) ? "error" : detection.Severity,
                    Code = result.Kind.Equals("vulnerability", StringComparison.OrdinalIgnoreCase) ? "BI9013" : "BI9012",
                    Path = displayPath,
                    Sha256 = artifact.Sha256,
                    Message = $"{ScannerLabel(result)} detected {detection.Id} in {artifact.Path}: {detection.Message}"
                });
            }
        }
    }

    private static IReadOnlyList<IArtifactSecurityScanner> EffectiveArtifactScanners(SupplyChainSecurityOptions options)
    {
        var configured = options.ArtifactScanners ?? Array.Empty<IArtifactSecurityScanner>();
        if (options.Policy?.RequireMalwareScan == true
            && options.UseBuiltInWindowsDefenderScanner
            && !configured.Any(scanner => scanner.Kind.Equals("malware", StringComparison.OrdinalIgnoreCase)))
        {
            var effective = new List<IArtifactSecurityScanner>(configured.Count + 2);
            effective.AddRange(configured);
            effective.Add(options.BuiltInMalwareScanner ?? new WindowsDefenderArtifactScanner());
            configured = effective;
        }

        if (options.Policy?.RequireVulnerabilityScan == true
            && options.UseBuiltInOsvScanner
            && !configured.Any(scanner => scanner.Kind.Equals("vulnerability", StringComparison.OrdinalIgnoreCase)))
        {
            var effective = new List<IArtifactSecurityScanner>(configured.Count + 1);
            effective.AddRange(configured);
            effective.Add(options.BuiltInVulnerabilityScanner ?? new OsvArtifactVulnerabilityScanner());
            return effective;
        }

        return configured;
    }

    private static string ScannerLabel(ArtifactSecurityScanResult result)
        => string.IsNullOrWhiteSpace(result.ToolVersion)
            ? $"{result.Kind} scanner '{result.ToolName}'"
            : $"{result.Kind} scanner '{result.ToolName}' ({result.ToolVersion})";

    private static void AddFinding(
        Beep.Installer.Policy.InstallerPolicy? policy,
        IWaiverSignatureVerifier waiverVerifier,
        SupplyChainSecurityReport report,
        SupplyChainFinding finding)
    {
        var waiver = MatchingWaiver(policy, finding);
        if (waiver is null)
        {
            report.Findings.Add(finding);
            return;
        }

        if (string.IsNullOrWhiteSpace(waiver.ApprovedBy)
            || string.IsNullOrWhiteSpace(waiver.Reason)
            || string.IsNullOrWhiteSpace(waiver.Signature)
            || !waiver.ExpiresAtUtc.HasValue)
        {
            report.Findings.Add(finding);
            report.Findings.Add(new SupplyChainFinding
            {
                Severity = "error",
                Code = "BI9008",
                Path = finding.Path,
                Sha256 = finding.Sha256,
                Message = $"Supply-chain waiver '{waiver.Id}' is incomplete and cannot suppress finding {finding.Code}."
            });
            return;
        }

        if (waiver.ExpiresAtUtc.Value <= DateTimeOffset.UtcNow)
        {
            report.Findings.Add(finding);
            report.Findings.Add(new SupplyChainFinding
            {
                Severity = "error",
                Code = "BI9007",
                Path = finding.Path,
                Sha256 = finding.Sha256,
                Message = $"Supply-chain waiver '{waiver.Id}' is expired and cannot suppress finding {finding.Code}."
            });
            return;
        }

        var verification = waiverVerifier.Verify(waiver, policy!);
        if (!verification.Trusted)
        {
            report.Findings.Add(finding);
            report.Findings.Add(new SupplyChainFinding
            {
                Severity = "error",
                Code = "BI9009",
                Path = finding.Path,
                Sha256 = finding.Sha256,
                Message = $"Supply-chain waiver '{waiver.Id}' signature is not trusted and cannot suppress finding {finding.Code}: {verification.Error}"
            });
            return;
        }

        report.Findings.Add(new SupplyChainFinding
        {
            Severity = "warning",
            Code = finding.Code,
            Path = finding.Path,
            Sha256 = finding.Sha256,
            Message = finding.Message,
            Waived = true,
            WaiverId = waiver.Id,
            WaiverReason = waiver.Reason,
            WaiverApprovedBy = waiver.ApprovedBy,
            WaiverExpiresAtUtc = waiver.ExpiresAtUtc
        });
    }

    private static Beep.Installer.Policy.SupplyChainWaiver? MatchingWaiver(
        Beep.Installer.Policy.InstallerPolicy? policy,
        SupplyChainFinding finding)
    {
        if (policy is null || policy.SupplyChainWaivers.Count == 0)
            return null;

        return policy.SupplyChainWaivers.FirstOrDefault(waiver =>
            waiver.FindingCode.Equals(finding.Code, StringComparison.OrdinalIgnoreCase)
            && MatchesWaiverTarget(waiver, finding));
    }

    private static bool MatchesWaiverTarget(Beep.Installer.Policy.SupplyChainWaiver waiver, SupplyChainFinding finding)
    {
        var hasSha = !string.IsNullOrWhiteSpace(waiver.Sha256);
        var hasPath = !string.IsNullOrWhiteSpace(waiver.Path);

        if (!hasSha && !hasPath)
            return false;

        if (hasSha && !waiver.Sha256.Equals(finding.Sha256, StringComparison.OrdinalIgnoreCase))
            return false;

        if (hasPath && !NormalizePath(waiver.Path).Equals(NormalizePath(finding.Path), StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static string NormalizePath(string value)
        => value.Replace('\\', '/').Trim();

    private static string? ExpectedHash(
        InstallProject project,
        string fullPath,
        Beep.Installer.Policy.InstallerPolicy? policy)
    {
        if (policy is null || policy.RequiredFileSha256.Count == 0)
            return null;

        foreach (var key in CandidateKeys(project, fullPath))
        {
            if (policy.RequiredFileSha256.TryGetValue(key, out var value))
                return NormalizeHash(value);
        }

        return null;
    }

    private static IEnumerable<string> CandidateKeys(InstallProject project, string fullPath)
    {
        yield return fullPath;
        yield return fullPath.Replace('\\', '/');
        yield return Path.GetFileName(fullPath);

        if (!string.IsNullOrWhiteSpace(project.SourceDirectory))
        {
            var sourceRoot = Path.GetFullPath(project.SourceDirectory);
            if (fullPath.StartsWith(sourceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(sourceRoot, fullPath);
                yield return relative;
                yield return relative.Replace('\\', '/');
            }
        }
    }

    private static bool IsRequired(Beep.Installer.Engine.CompiledInstallOperation operation)
        => !operation.Inputs.TryGetValue("required", out var value)
           || value.Equals("true", StringComparison.OrdinalIgnoreCase)
           || value.Equals("yes", StringComparison.OrdinalIgnoreCase);

    private static string Input(Beep.Installer.Engine.CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string NormalizeHash(string value)
        => value.Replace(" ", "", StringComparison.Ordinal).Trim().ToLowerInvariant();

    private static bool IsRemote(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && uri.Scheme is "http" or "https";

    private static bool IsRuntimeMacro(string value)
        => value.Contains("{app}", StringComparison.OrdinalIgnoreCase)
           || value.Contains("%InstallPath%", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldInspectSignature(FileCandidate candidate, string fullPath, Beep.Installer.Policy.InstallerPolicy? policy)
        => IsSignable(fullPath)
           && (policy?.RequireSignedArtifacts == true
               || policy?.AllowedArtifactSignerSubjects.Count > 0
               || candidate.Kind is "installer" or "custom-action");

    private static bool IsSignable(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".msi", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".msp", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".msix", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".msixbundle", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".appinstaller", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase);
    }

    private static string SafePath(InstallProject project, string fullPath)
    {
        if (!string.IsNullOrWhiteSpace(project.SourceDirectory))
        {
            var sourceRoot = Path.GetFullPath(project.SourceDirectory);
            if (fullPath.StartsWith(sourceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(sourceRoot, fullPath).Replace('\\', '/');
        }

        return Path.GetFileName(fullPath);
    }

    private static void WriteReportIfRequested(SupplyChainSecurityReport report, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, ToJson(report));
    }

    private sealed record FileCandidate(string Kind, string DisplayPath, string Path, bool Required);
}

public sealed record SecurityScannerCommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface ISecurityScannerCommandRunner
{
    string ToolPath { get; }
    string ToolVersion { get; }
    SecurityScannerCommandResult Run(IReadOnlyList<string> arguments);
}

public sealed class WindowsDefenderArtifactScanner : IArtifactSecurityScanner
{
    private readonly ISecurityScannerCommandRunner _commandRunner;

    public WindowsDefenderArtifactScanner()
        : this(new MpCmdRunDefenderCommandRunner())
    {
    }

    public WindowsDefenderArtifactScanner(ISecurityScannerCommandRunner commandRunner)
    {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public string Kind => "malware";

    public ArtifactSecurityScanResult Scan(string path, SupplyChainArtifact artifact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(artifact);

        var result = _commandRunner.Run(new[] { "-Scan", "-ScanType", "3", "-File", path, "-DisableRemediation" });
        var output = SecurityScannerOutput.Compact(result.StandardOutput, result.StandardError);
        if (result.ExitCode == 0 && !LooksLikeDetection(output))
        {
            return Result("Clean", "Microsoft Defender completed a file scan without detections.", output);
        }

        if (result.ExitCode == 2 || (result.ExitCode == 0 && LooksLikeDetection(output)))
        {
            return Result(
                "Detected",
                "Microsoft Defender reported one or more detections.",
                output,
                ExtractDetections(output));
        }

        return Result(
            "Error",
            "Microsoft Defender could not complete the file scan.",
            output,
            error: string.IsNullOrWhiteSpace(output)
                ? $"MpCmdRun exited with code {result.ExitCode}."
                : $"MpCmdRun exited with code {result.ExitCode}: {output}");
    }

    private ArtifactSecurityScanResult Result(
        string status,
        string message,
        string evidence,
        List<ArtifactSecurityDetection>? detections = null,
        string error = "")
        => new()
        {
            Kind = Kind,
            ToolName = "Microsoft Defender Antivirus",
            ToolVersion = string.IsNullOrWhiteSpace(_commandRunner.ToolVersion) ? "unknown" : _commandRunner.ToolVersion,
            Status = status,
            Message = string.IsNullOrWhiteSpace(evidence) ? message : $"{message} Evidence: {evidence}",
            Detections = detections ?? new List<ArtifactSecurityDetection>(),
            Error = error
        };

    private static bool LooksLikeDetection(string evidence)
        => evidence.Contains("threat", StringComparison.OrdinalIgnoreCase)
           || evidence.Contains("detected", StringComparison.OrdinalIgnoreCase)
           || evidence.Contains("found", StringComparison.OrdinalIgnoreCase)
           || evidence.Contains("infected", StringComparison.OrdinalIgnoreCase)
           || evidence.Contains("malware", StringComparison.OrdinalIgnoreCase);

    private static List<ArtifactSecurityDetection> ExtractDetections(string evidence)
    {
        var detections = new List<ArtifactSecurityDetection>();
        foreach (var rawLine in evidence.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || !LooksLikeDetection(line))
                continue;

            detections.Add(new ArtifactSecurityDetection
            {
                Id = ExtractDetectionId(line),
                Severity = "error",
                Message = line
            });
        }

        if (detections.Count == 0)
        {
            detections.Add(new ArtifactSecurityDetection
            {
                Id = "MicrosoftDefenderDetection",
                Severity = "error",
                Message = string.IsNullOrWhiteSpace(evidence) ? "Microsoft Defender reported a detection." : evidence
            });
        }

        return detections;
    }

    private static string ExtractDetectionId(string line)
    {
        foreach (var separator in new[] { ':', '=' })
        {
            var index = line.IndexOf(separator, StringComparison.Ordinal);
            if (index >= 0 && index + 1 < line.Length)
            {
                var candidate = line[(index + 1)..].Trim();
                if (candidate.Length > 0)
                    return candidate.Length > 96 ? candidate[..96] : candidate;
            }
        }

        return line.Length > 96 ? line[..96] : line;
    }

}

public sealed class ClamAvArtifactScanner : IArtifactSecurityScanner
{
    private readonly ISecurityScannerCommandRunner _commandRunner;

    public ClamAvArtifactScanner()
        : this(new ClamAvCommandRunner())
    {
    }

    public ClamAvArtifactScanner(ISecurityScannerCommandRunner commandRunner)
    {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public string Kind => "malware";

    public ArtifactSecurityScanResult Scan(string path, SupplyChainArtifact artifact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(artifact);

        var result = _commandRunner.Run(new[] { "--infected", "--no-summary", "--stdout", path });
        var output = SecurityScannerOutput.Compact(result.StandardOutput, result.StandardError);
        return result.ExitCode switch
        {
            0 => Result("Clean", "ClamAV completed a file scan without detections.", output),
            1 => Result("Detected", "ClamAV reported one or more detections.", output, ExtractDetections(output)),
            _ => Result(
                "Error",
                "ClamAV could not complete the file scan.",
                output,
                error: string.IsNullOrWhiteSpace(output)
                    ? $"clamscan exited with code {result.ExitCode}."
                    : $"clamscan exited with code {result.ExitCode}: {output}")
        };
    }

    private ArtifactSecurityScanResult Result(
        string status,
        string message,
        string evidence,
        List<ArtifactSecurityDetection>? detections = null,
        string error = "")
        => new()
        {
            Kind = Kind,
            ToolName = "ClamAV clamscan",
            ToolVersion = string.IsNullOrWhiteSpace(_commandRunner.ToolVersion) ? "unknown" : _commandRunner.ToolVersion,
            Status = status,
            Message = string.IsNullOrWhiteSpace(evidence) ? message : $"{message} Evidence: {evidence}",
            Detections = detections ?? new List<ArtifactSecurityDetection>(),
            Error = error
        };

    private static List<ArtifactSecurityDetection> ExtractDetections(string evidence)
    {
        var detections = new List<ArtifactSecurityDetection>();
        foreach (var rawLine in evidence.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var id = ExtractDetectionId(line);
            detections.Add(new ArtifactSecurityDetection
            {
                Id = id,
                Severity = "error",
                Message = line
            });
        }

        if (detections.Count == 0)
        {
            detections.Add(new ArtifactSecurityDetection
            {
                Id = "ClamAvDetection",
                Severity = "error",
                Message = "ClamAV reported a detection."
            });
        }

        return detections;
    }

    private static string ExtractDetectionId(string line)
    {
        const string foundMarker = " FOUND";
        var foundIndex = line.LastIndexOf(foundMarker, StringComparison.OrdinalIgnoreCase);
        if (foundIndex > 0)
            line = line[..foundIndex].Trim();

        var separatorIndex = line.LastIndexOf(':');
        if (separatorIndex >= 0 && separatorIndex + 1 < line.Length)
            line = line[(separatorIndex + 1)..].Trim();

        return line.Length > 96 ? line[..96] : line;
    }

}

public sealed class OsvArtifactVulnerabilityScanner : IArtifactSecurityScanner
{
    private readonly ISecurityScannerCommandRunner _commandRunner;

    public OsvArtifactVulnerabilityScanner()
        : this(new OsvScannerCommandRunner())
    {
    }

    public OsvArtifactVulnerabilityScanner(ISecurityScannerCommandRunner commandRunner)
    {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public string Kind => "vulnerability";

    public ArtifactSecurityScanResult Scan(string path, SupplyChainArtifact artifact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(artifact);

        var result = _commandRunner.Run(new[] { "scan", "--format", "json", path });
        var detections = ParseOsvDetections(result.StandardOutput);
        var evidence = SecurityScannerOutput.Compact(result.StandardOutput, result.StandardError);
        if (detections.Count > 0 || result.ExitCode == 1)
        {
            return Result(
                "Detected",
                "OSV-Scanner reported one or more vulnerabilities.",
                evidence,
                detections.Count > 0 ? detections : new List<ArtifactSecurityDetection>
                {
                    new()
                    {
                        Id = "OSV-Vulnerability",
                        Severity = "error",
                        Message = string.IsNullOrWhiteSpace(evidence) ? "OSV-Scanner reported a vulnerability." : evidence
                    }
                });
        }

        if (result.ExitCode == 0)
            return Result("Clean", "OSV-Scanner completed without vulnerabilities.", evidence);

        return Result(
            "Error",
            "OSV-Scanner could not complete the vulnerability scan.",
            evidence,
            error: string.IsNullOrWhiteSpace(evidence)
                ? $"osv-scanner exited with code {result.ExitCode}."
                : $"osv-scanner exited with code {result.ExitCode}: {evidence}");
    }

    private ArtifactSecurityScanResult Result(
        string status,
        string message,
        string evidence,
        List<ArtifactSecurityDetection>? detections = null,
        string error = "")
        => new()
        {
            Kind = Kind,
            ToolName = "OSV-Scanner",
            ToolVersion = string.IsNullOrWhiteSpace(_commandRunner.ToolVersion) ? "unknown" : _commandRunner.ToolVersion,
            Status = status,
            Message = string.IsNullOrWhiteSpace(evidence) ? message : $"{message} Evidence: {evidence}",
            Detections = detections ?? new List<ArtifactSecurityDetection>(),
            Error = error
        };

    private static List<ArtifactSecurityDetection> ParseOsvDetections(string json)
    {
        var detections = new List<ArtifactSecurityDetection>();
        if (string.IsNullOrWhiteSpace(json))
            return detections;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return detections;

            foreach (var result in results.EnumerateArray())
            {
                if (!result.TryGetProperty("packages", out var packages) || packages.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var package in packages.EnumerateArray())
                    AddOsvPackageDetections(package, detections);
            }
        }
        catch (JsonException)
        {
        }

        return detections;
    }

    private static void AddOsvPackageDetections(JsonElement package, List<ArtifactSecurityDetection> detections)
    {
        var packageName = "";
        var packageVersion = "";
        if (package.TryGetProperty("package", out var packageInfo) && packageInfo.ValueKind == JsonValueKind.Object)
        {
            packageName = JsonString(packageInfo, "name");
            packageVersion = JsonString(packageInfo, "version");
        }

        if (!package.TryGetProperty("vulnerabilities", out var vulnerabilities) || vulnerabilities.ValueKind != JsonValueKind.Array)
            return;

        foreach (var vulnerability in vulnerabilities.EnumerateArray())
        {
            var id = JsonString(vulnerability, "id");
            if (string.IsNullOrWhiteSpace(id))
                id = "OSV-Vulnerability";

            detections.Add(new ArtifactSecurityDetection
            {
                Id = id,
                Severity = "error",
                Message = VulnerabilityMessage(id, packageName, packageVersion, JsonString(vulnerability, "summary"))
            });
        }
    }

    private static string VulnerabilityMessage(string id, string packageName, string packageVersion, string summary)
    {
        var component = string.IsNullOrWhiteSpace(packageName)
            ? "component"
            : string.IsNullOrWhiteSpace(packageVersion)
                ? packageName
                : $"{packageName}@{packageVersion}";
        return string.IsNullOrWhiteSpace(summary) ? $"{id} affects {component}." : $"{id} affects {component}: {summary}";
    }

    private static string JsonString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}

public sealed class TrivyFilesystemVulnerabilityScanner : IArtifactSecurityScanner
{
    private readonly ISecurityScannerCommandRunner _commandRunner;

    public TrivyFilesystemVulnerabilityScanner()
        : this(new TrivyCommandRunner())
    {
    }

    public TrivyFilesystemVulnerabilityScanner(ISecurityScannerCommandRunner commandRunner)
    {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public string Kind => "vulnerability";

    public ArtifactSecurityScanResult Scan(string path, SupplyChainArtifact artifact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(artifact);

        var result = _commandRunner.Run(new[] { "fs", "--format", "json", "--exit-code", "1", path });
        var detections = ParseTrivyDetections(result.StandardOutput);
        var evidence = SecurityScannerOutput.Compact(result.StandardOutput, result.StandardError);
        if (detections.Count > 0 || result.ExitCode == 1)
        {
            return Result(
                "Detected",
                "Trivy reported one or more vulnerabilities.",
                evidence,
                detections.Count > 0 ? detections : new List<ArtifactSecurityDetection>
                {
                    new()
                    {
                        Id = "Trivy-Vulnerability",
                        Severity = "error",
                        Message = string.IsNullOrWhiteSpace(evidence) ? "Trivy reported a vulnerability." : evidence
                    }
                });
        }

        if (result.ExitCode == 0)
            return Result("Clean", "Trivy completed without vulnerabilities.", evidence);

        return Result(
            "Error",
            "Trivy could not complete the vulnerability scan.",
            evidence,
            error: string.IsNullOrWhiteSpace(evidence)
                ? $"trivy exited with code {result.ExitCode}."
                : $"trivy exited with code {result.ExitCode}: {evidence}");
    }

    private ArtifactSecurityScanResult Result(
        string status,
        string message,
        string evidence,
        List<ArtifactSecurityDetection>? detections = null,
        string error = "")
        => new()
        {
            Kind = Kind,
            ToolName = "Trivy",
            ToolVersion = string.IsNullOrWhiteSpace(_commandRunner.ToolVersion) ? "unknown" : _commandRunner.ToolVersion,
            Status = status,
            Message = string.IsNullOrWhiteSpace(evidence) ? message : $"{message} Evidence: {evidence}",
            Detections = detections ?? new List<ArtifactSecurityDetection>(),
            Error = error
        };

    private static List<ArtifactSecurityDetection> ParseTrivyDetections(string json)
    {
        var detections = new List<ArtifactSecurityDetection>();
        if (string.IsNullOrWhiteSpace(json))
            return detections;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("Results", out var results) || results.ValueKind != JsonValueKind.Array)
                return detections;

            foreach (var result in results.EnumerateArray())
            {
                if (!result.TryGetProperty("Vulnerabilities", out var vulnerabilities) || vulnerabilities.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var vulnerability in vulnerabilities.EnumerateArray())
                {
                    var id = JsonString(vulnerability, "VulnerabilityID");
                    if (string.IsNullOrWhiteSpace(id))
                        id = "Trivy-Vulnerability";

                    detections.Add(new ArtifactSecurityDetection
                    {
                        Id = id,
                        Severity = TrivySeverity(JsonString(vulnerability, "Severity")),
                        Message = TrivyMessage(id, JsonString(vulnerability, "PkgName"), JsonString(vulnerability, "InstalledVersion"), JsonString(vulnerability, "Title"))
                    });
                }
            }
        }
        catch (JsonException)
        {
        }

        return detections;
    }

    private static string TrivySeverity(string severity)
        => severity.ToUpperInvariant() switch
        {
            "CRITICAL" or "HIGH" => "error",
            "MEDIUM" => "warning",
            _ => "info"
        };

    private static string TrivyMessage(string id, string packageName, string installedVersion, string title)
    {
        var component = string.IsNullOrWhiteSpace(packageName)
            ? "component"
            : string.IsNullOrWhiteSpace(installedVersion)
                ? packageName
                : $"{packageName}@{installedVersion}";
        return string.IsNullOrWhiteSpace(title) ? $"{id} affects {component}." : $"{id} affects {component}: {title}";
    }

    private static string JsonString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}

internal static class SecurityScannerOutput
{
    public static string Compact(string standardOutput, string standardError)
    {
        var joined = string.Join(
            " ",
            new[] { standardOutput, standardError }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim()));
        return joined.Length <= 600 ? joined : $"{joined[..600]}...";
    }
}

public class ProcessSecurityScannerCommandRunner : ISecurityScannerCommandRunner
{
    private readonly string? _toolPath;
    private readonly TimeSpan _timeout;
    private readonly string _missingToolMessage;
    private readonly string _startFailureMessage;
    private readonly string _timeoutMessage;
    private readonly string _debugContext;

    public ProcessSecurityScannerCommandRunner(
        string? toolPath,
        TimeSpan timeout,
        string missingToolMessage,
        string startFailureMessage,
        string timeoutMessage,
        string debugContext)
    {
        _toolPath = toolPath;
        _timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(2) : timeout;
        _missingToolMessage = string.IsNullOrWhiteSpace(missingToolMessage) ? "Security scanner command was not found." : missingToolMessage;
        _startFailureMessage = string.IsNullOrWhiteSpace(startFailureMessage) ? "Security scanner command could not be started." : startFailureMessage;
        _timeoutMessage = string.IsNullOrWhiteSpace(timeoutMessage) ? "Security scanner command timed out." : timeoutMessage;
        _debugContext = string.IsNullOrWhiteSpace(debugContext) ? "SecurityScannerCommandRunner" : debugContext;
    }

    public string ToolPath => _toolPath ?? "";

    public string ToolVersion
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_toolPath) || !File.Exists(_toolPath))
                return "";

            try
            {
                var version = FileVersionInfo.GetVersionInfo(_toolPath).FileVersion;
                if (!string.IsNullOrWhiteSpace(version))
                    return version;

                var directoryName = Path.GetFileName(Path.GetDirectoryName(_toolPath));
                return directoryName ?? "";
            }
            catch (Exception ex)
            {
                Beep.Installer.Engine.Diag.Debug("SupplyChainSecurityScanner", $"Unable to inspect {_debugContext} version", ex);
                return "";
            }
        }
    }

    public SecurityScannerCommandResult Run(IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(_toolPath) || !File.Exists(_toolPath))
            return new SecurityScannerCommandResult(-1, "", _missingToolMessage);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _toolPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            if (!process.Start())
                return new SecurityScannerCommandResult(-1, "", _startFailureMessage);

            var standardOutput = new StringBuilder();
            var standardError = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    standardOutput.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    standardError.AppendLine(e.Data);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit((int)_timeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Beep.Installer.Engine.Diag.Debug("SupplyChainSecurityScanner", $"Unable to stop timed-out {_debugContext}", ex);
                }

                return new SecurityScannerCommandResult(-1, standardOutput.ToString(), _timeoutMessage);
            }

            process.WaitForExit();
            return new SecurityScannerCommandResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
        }
        catch (Exception ex)
        {
            Beep.Installer.Engine.Diag.Debug("SupplyChainSecurityScanner", $"{_debugContext} failed", ex);
            return new SecurityScannerCommandResult(-1, "", ex.Message);
        }
    }
}

public sealed class MpCmdRunDefenderCommandRunner : ProcessSecurityScannerCommandRunner
{
    public MpCmdRunDefenderCommandRunner()
        : this(null, TimeSpan.FromMinutes(2))
    {
    }

    public MpCmdRunDefenderCommandRunner(string? toolPath, TimeSpan timeout)
        : base(
            string.IsNullOrWhiteSpace(toolPath) ? FindMpCmdRun() : toolPath,
            timeout,
            "Microsoft Defender MpCmdRun.exe was not found.",
            "Microsoft Defender MpCmdRun.exe could not be started.",
            "Microsoft Defender scan timed out.",
            "Microsoft Defender scan")
    {
    }

    private static string? FindMpCmdRun()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var platformRoot = string.IsNullOrWhiteSpace(programData)
            ? ""
            : Path.Combine(programData, "Microsoft", "Windows Defender", "Platform");
        if (Directory.Exists(platformRoot))
        {
            try
            {
                var newest = Directory.EnumerateDirectories(platformRoot)
                    .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .Select(directory => Path.Combine(directory, "MpCmdRun.exe"))
                    .FirstOrDefault(File.Exists);
                if (!string.IsNullOrWhiteSpace(newest))
                    return newest;
            }
            catch (Exception ex)
            {
                Beep.Installer.Engine.Diag.Debug("SupplyChainSecurityScanner", "Microsoft Defender platform directory skipped", ex);
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var defenderTool = string.IsNullOrWhiteSpace(programFiles)
            ? ""
            : Path.Combine(programFiles, "Windows Defender", "MpCmdRun.exe");
        return File.Exists(defenderTool) ? defenderTool : null;
    }
}

public sealed class ClamAvCommandRunner : ProcessSecurityScannerCommandRunner
{
    public ClamAvCommandRunner()
        : this(null, TimeSpan.FromMinutes(2))
    {
    }

    public ClamAvCommandRunner(string? toolPath, TimeSpan timeout)
        : base(
            string.IsNullOrWhiteSpace(toolPath)
                ? SecurityScannerToolDiscovery.FindOnPath(OperatingSystem.IsWindows()
                    ? new[] { "clamscan.exe", "clamscan" }
                    : new[] { "clamscan" })
                : toolPath,
            timeout,
            "ClamAV clamscan was not found.",
            "ClamAV clamscan could not be started.",
            "ClamAV scan timed out.",
            "ClamAV scan")
    {
    }
}

public sealed class OsvScannerCommandRunner : ProcessSecurityScannerCommandRunner
{
    public OsvScannerCommandRunner()
        : this(null, TimeSpan.FromMinutes(2))
    {
    }

    public OsvScannerCommandRunner(string? toolPath, TimeSpan timeout)
        : base(
            string.IsNullOrWhiteSpace(toolPath)
                ? SecurityScannerToolDiscovery.FindOnPath(OperatingSystem.IsWindows()
                    ? new[] { "osv-scanner.exe", "osv-scanner" }
                    : new[] { "osv-scanner" })
                : toolPath,
            timeout,
            "OSV-Scanner was not found.",
            "OSV-Scanner could not be started.",
            "OSV-Scanner scan timed out.",
            "OSV-Scanner scan")
    {
    }
}

public sealed class TrivyCommandRunner : ProcessSecurityScannerCommandRunner
{
    public TrivyCommandRunner()
        : this(null, TimeSpan.FromMinutes(2))
    {
    }

    public TrivyCommandRunner(string? toolPath, TimeSpan timeout)
        : base(
            string.IsNullOrWhiteSpace(toolPath)
                ? SecurityScannerToolDiscovery.FindOnPath(OperatingSystem.IsWindows()
                    ? new[] { "trivy.exe", "trivy" }
                    : new[] { "trivy" })
                : toolPath,
            timeout,
            "Trivy was not found.",
            "Trivy could not be started.",
            "Trivy scan timed out.",
            "Trivy scan")
    {
    }
}

internal static class SecurityScannerToolDiscovery
{
    public static string? FindOnPath(IReadOnlyList<string> executableNames)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var executableName in executableNames)
            {
                try
                {
                    var candidate = Path.Combine(directory, executableName);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (Exception ex)
                {
                    Beep.Installer.Engine.Diag.Debug("SupplyChainSecurityScanner", "PATH entry skipped during scanner discovery", ex);
                }
            }
        }

        return null;
    }
}

public sealed class RsaSha256WaiverSignatureVerifier : IWaiverSignatureVerifier
{
    public WaiverSignatureVerification Verify(
        Beep.Installer.Policy.SupplyChainWaiver waiver,
        Beep.Installer.Policy.InstallerPolicy policy)
    {
        var verification = RsaSha256DetachedSignatureVerifier.VerifyUtf8Payload(
            CanonicalWaiverPayload(waiver),
            waiver.Signature,
            policy.TrustedWaiverPublicKeys,
            "waiver");

        return new WaiverSignatureVerification
        {
            Trusted = verification.Trusted,
            Status = verification.Status,
            Error = verification.Error
        };
    }

    public static string CanonicalWaiverPayload(Beep.Installer.Policy.SupplyChainWaiver waiver)
        => string.Join('\n',
            "beep-installer:supply-chain-waiver:v1",
            waiver.Id.Trim(),
            waiver.FindingCode.Trim().ToUpperInvariant(),
            waiver.Path.Replace('\\', '/').Trim(),
            waiver.Sha256.Replace(" ", "", StringComparison.Ordinal).Trim().ToLowerInvariant(),
            waiver.ApprovedAtUtc?.ToUniversalTime().ToString("O") ?? "",
            waiver.ExpiresAtUtc?.ToUniversalTime().ToString("O") ?? "",
            waiver.ApprovedBy.Trim(),
            waiver.Reason.Trim());

}

public sealed class WindowsAuthenticodeSignatureVerifier : IArtifactSignatureVerifier
{
    public ArtifactSignatureStatus Verify(string path)
    {
        try
        {
            var shell = FindPowerShell();
            if (shell is null)
            {
                return new ArtifactSignatureStatus
                {
                    Inspected = true,
                    Trusted = false,
                    Status = "VerifierUnavailable",
                    Error = "PowerShell was not found for Authenticode verification."
                };
            }

            var script = "& { param($targetPath) " +
                         "function Build-Chain($cert) { " +
                         "if(-not $cert){ return [pscustomobject]@{ Status=''; Elements=@() } } " +
                         "$chain=New-Object System.Security.Cryptography.X509Certificates.X509Chain; " +
                         "$chain.ChainPolicy.RevocationMode=[System.Security.Cryptography.X509Certificates.X509RevocationMode]::Online; " +
                         "$chain.ChainPolicy.RevocationFlag=[System.Security.Cryptography.X509Certificates.X509RevocationFlag]::ExcludeRoot; " +
                         "$chain.ChainPolicy.VerificationFlags=[System.Security.Cryptography.X509Certificates.X509VerificationFlags]::NoFlag; " +
                         "[void]$chain.Build($cert); " +
                         "$overall=($chain.ChainStatus | ForEach-Object { ([string]$_.Status).Trim() + ': ' + $_.StatusInformation.Trim() }) -join '; '; " +
                         "$elements=@($chain.ChainElements | ForEach-Object { " +
                         "$element=$_.Certificate; " +
                         "$status=($_.ChainElementStatus | ForEach-Object { ([string]$_.Status).Trim() + ': ' + $_.StatusInformation.Trim() }) -join '; '; " +
                         "[pscustomobject]@{ Subject=$element.Subject; Issuer=$element.Issuer; Thumbprint=$element.Thumbprint; NotBeforeUtc=$element.NotBefore.ToUniversalTime().ToString('O'); NotAfterUtc=$element.NotAfter.ToUniversalTime().ToString('O'); Status=$status } " +
                         "}); " +
                         "[pscustomobject]@{ Status=$overall; Elements=$elements } " +
                         "} " +
                         "$s=Get-AuthenticodeSignature -LiteralPath $targetPath; " +
                         "$signer=$s.SignerCertificate; " +
                         "$timestamp=$s.TimeStamperCertificate; " +
                         "$signerChain=Build-Chain $signer; " +
                         "$timestampChain=Build-Chain $timestamp; " +
                         "[pscustomobject]@{" +
                         "Status=[string]$s.Status;" +
                         "Subject=if($signer){$signer.Subject}else{''};" +
                         "Issuer=if($signer){$signer.Issuer}else{''};" +
                         "Thumbprint=if($signer){$signer.Thumbprint}else{''};" +
                         "NotBeforeUtc=if($signer){$signer.NotBefore.ToUniversalTime().ToString('O')}else{''};" +
                         "NotAfterUtc=if($signer){$signer.NotAfter.ToUniversalTime().ToString('O')}else{''};" +
                         "TimestampSubject=if($timestamp){$timestamp.Subject}else{''};" +
                         "TimestampThumbprint=if($timestamp){$timestamp.Thumbprint}else{''};" +
                         "TimestampNotBeforeUtc=if($timestamp){$timestamp.NotBefore.ToUniversalTime().ToString('O')}else{''};" +
                         "TimestampNotAfterUtc=if($timestamp){$timestamp.NotAfter.ToUniversalTime().ToString('O')}else{''};" +
                         "ChainStatus=$signerChain.Status;" +
                         "Chain=$signerChain.Elements;" +
                         "TimestampChainStatus=$timestampChain.Status;" +
                         "TimestampChain=$timestampChain.Elements;" +
                         "RevocationMode='Online'" +
                         "} | ConvertTo-Json -Compress -Depth 8 }";
            var psi = new ProcessStartInfo
            {
                FileName = shell,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);
            psi.ArgumentList.Add(path);

            using var process = Process.Start(psi);
            if (process is null)
                return Error("Could not start PowerShell for Authenticode verification.");

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30_000))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) { Beep.Installer.Engine.Diag.Debug("SupplyChainSecurityScanner", "signature verifier timeout kill failed", ex); }
                return Error("Authenticode verification timed out.");
            }

            if (process.ExitCode != 0)
                return Error($"Authenticode verification failed: {stderr.Trim()}");

            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            var status = StringProperty(root, "Status");
            return new ArtifactSignatureStatus
            {
                Inspected = true,
                Trusted = status.Equals("Valid", StringComparison.OrdinalIgnoreCase),
                Status = status,
                Subject = StringProperty(root, "Subject"),
                Issuer = StringProperty(root, "Issuer"),
                Thumbprint = StringProperty(root, "Thumbprint"),
                NotBeforeUtc = StringProperty(root, "NotBeforeUtc"),
                NotAfterUtc = StringProperty(root, "NotAfterUtc"),
                TimestampSubject = StringProperty(root, "TimestampSubject"),
                TimestampThumbprint = StringProperty(root, "TimestampThumbprint"),
                TimestampNotBeforeUtc = StringProperty(root, "TimestampNotBeforeUtc"),
                TimestampNotAfterUtc = StringProperty(root, "TimestampNotAfterUtc"),
                ChainStatus = StringProperty(root, "ChainStatus"),
                Chain = CertificateEvidence(root, "Chain"),
                TimestampChainStatus = StringProperty(root, "TimestampChainStatus"),
                TimestampChain = CertificateEvidence(root, "TimestampChain"),
                RevocationMode = StringProperty(root, "RevocationMode")
            };
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static string? FindPowerShell()
    {
        foreach (var name in new[] { "pwsh.exe", "powershell.exe" })
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                         .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (Exception ex)
                {
                    Beep.Installer.Engine.Diag.Debug("SupplyChainSecurityScanner", "PATH entry skipped during PowerShell discovery", ex);
                }
            }
        }

        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windowsPowerShell = string.IsNullOrWhiteSpace(system)
            ? ""
            : Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(windowsPowerShell) ? windowsPowerShell : null;
    }

    private static ArtifactSignatureStatus Error(string error)
        => new()
        {
            Inspected = true,
            Trusted = false,
            Status = "Error",
            Error = error
        };

    private static string StringProperty(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static List<ArtifactSignatureCertificateEvidence> CertificateEvidence(JsonElement root, string name)
    {
        var certificates = new List<ArtifactSignatureCertificateEvidence>();
        if (!root.TryGetProperty(name, out var values))
            return certificates;

        if (values.ValueKind == JsonValueKind.Object)
        {
            certificates.Add(CertificateEvidence(values));
            return certificates;
        }

        if (values.ValueKind != JsonValueKind.Array)
            return certificates;

        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.Object)
                certificates.Add(CertificateEvidence(value));
        }

        return certificates;
    }

    private static ArtifactSignatureCertificateEvidence CertificateEvidence(JsonElement value)
        => new()
        {
            Subject = StringProperty(value, "Subject"),
            Issuer = StringProperty(value, "Issuer"),
            Thumbprint = StringProperty(value, "Thumbprint"),
            NotBeforeUtc = StringProperty(value, "NotBeforeUtc"),
            NotAfterUtc = StringProperty(value, "NotAfterUtc"),
            Status = StringProperty(value, "Status")
        };
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(SupplyChainSecurityReport))]
[JsonSerializable(typeof(ArtifactSecurityScanResult))]
[JsonSerializable(typeof(ArtifactSecurityDetection))]
[JsonSerializable(typeof(ArtifactSignatureCertificateEvidence))]
internal sealed partial class SupplyChainSecurityJsonContext : JsonSerializerContext
{
}
