using System.Security.Cryptography;
using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Policy;

namespace Beep.Installer.Deployment;

public sealed class ReleaseEvidenceVerificationOptions
{
    public string? EvidenceDirectory { get; init; }
    public string? SbomPath { get; init; }
    public string? ProvenancePath { get; init; }
    public string? SigningEvidencePath { get; init; }
    public string? InstallerPath { get; init; }
    public string? SourceRoot { get; init; }
    public string? ReportPath { get; init; }
    public InstallerPolicyEvaluation? PolicyEvaluation { get; init; }
    public bool RequireAttestations { get; init; }
    public string? TrustedAttestationPublicKeyPath { get; init; }
    public string? TrustedAttestationPublicKey { get; init; }
}

public sealed class ReleaseEvidenceVerificationResult
{
    public string ReportPath { get; init; } = "";
    public bool Success => Diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public List<ReleaseEvidenceVerifiedArtifact> Artifacts { get; init; } = new();
}

public sealed class ReleaseEvidenceVerifiedArtifact
{
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string ExpectedSha256 { get; init; } = "";
    public string ActualSha256 { get; init; } = "";
    public bool Matched { get; init; }
}

public static class ReleaseEvidenceVerifier
{
    public static ReleaseEvidenceVerificationResult Verify(InstallProject project, ReleaseEvidenceVerificationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        options ??= new ReleaseEvidenceVerificationOptions();

        var evidenceDirectory = ResolveEvidenceDirectory(project, options);
        var sbomPath = Path.GetFullPath(string.IsNullOrWhiteSpace(options.SbomPath)
            ? Path.Combine(evidenceDirectory, $"{SafeFile(project.AppName)}-{SafeFile(project.AppVersion)}.spdx.json")
            : options.SbomPath!);
        var provenancePath = Path.GetFullPath(string.IsNullOrWhiteSpace(options.ProvenancePath)
            ? Path.Combine(evidenceDirectory, $"{SafeFile(project.AppName)}-{SafeFile(project.AppVersion)}.provenance.json")
            : options.ProvenancePath!);
        var reportPath = Path.GetFullPath(string.IsNullOrWhiteSpace(options.ReportPath)
            ? Path.Combine(evidenceDirectory, $"{SafeFile(project.AppName)}-{SafeFile(project.AppVersion)}.evidence-verification.json")
            : options.ReportPath!);

        var result = new ReleaseEvidenceVerificationResult { ReportPath = reportPath };
        var expectedArtifacts = ExpectedArtifacts(project, options, result.Diagnostics);
        var planHash = ExpectedPlanHash(project, options, result.Diagnostics);

        VerifySbom(sbomPath, expectedArtifacts, result);
        VerifyProvenance(provenancePath, expectedArtifacts, planHash, result);
        VerifyAttestation(sbomPath, "application/spdx+json", options, result);
        VerifyAttestation(provenancePath, "application/vnd.in-toto+json", options, result);
        VerifyReleasePolicy(project, options, expectedArtifacts, result);
        WriteReport(project, result);
        return result;
    }

    private static string ResolveEvidenceDirectory(InstallProject project, ReleaseEvidenceVerificationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.EvidenceDirectory))
            return Path.GetFullPath(options.EvidenceDirectory!);
        return Path.GetFullPath(Path.Combine(BuildPipeline.ResolveOutputDirectory(project), "evidence"));
    }

    private static List<ExpectedArtifact> ExpectedArtifacts(
        InstallProject project,
        ReleaseEvidenceVerificationOptions options,
        List<ProjectSchemaDiagnostic> diagnostics)
    {
        var sourceRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(options.SourceRoot) ? project.SourceDirectory : options.SourceRoot!);
        var artifacts = new List<ExpectedArtifact>();

        if (!string.IsNullOrWhiteSpace(options.InstallerPath))
        {
            var installerPath = Path.GetFullPath(options.InstallerPath!);
            if (File.Exists(installerPath))
            {
                artifacts.Add(new ExpectedArtifact("installer", Path.GetFileName(installerPath), installerPath, Sha256File(installerPath).ToLowerInvariant()));
            }
            else
            {
                diagnostics.Add(Error("BI2210", "Installer", $"Installer artifact was not found: {installerPath}"));
            }
        }

        foreach (var component in project.Components.OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var file in component.Files.OrderBy(f => f.DestinationPath, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(file.SourcePath))
                    continue;

                var sourcePath = Path.GetFullPath(file.SourcePath);
                var artifactName = NormalizeArtifactPath(file.DestinationPath);
                if (string.IsNullOrWhiteSpace(artifactName))
                    artifactName = Path.GetFileName(sourcePath);

                if (!File.Exists(sourcePath))
                {
                    diagnostics.Add(Error("BI2211", artifactName, $"SBOM source artifact was not found: {SafePathForMessage(sourcePath, sourceRoot)}"));
                    continue;
                }

                artifacts.Add(new ExpectedArtifact("payload", artifactName, sourcePath, Sha256File(sourcePath).ToLowerInvariant()));
            }
        }

        return artifacts;
    }

    private static string ExpectedPlanHash(
        InstallProject project,
        ReleaseEvidenceVerificationOptions options,
        List<ProjectSchemaDiagnostic> diagnostics)
    {
        var planResult = new InstallPlanCompiler().Compile(project, options.PolicyEvaluation);
        if (planResult.Success && planResult.Plan != null)
            return planResult.Plan.PlanHash.ToLowerInvariant();

        diagnostics.Add(Error("BI2212", "Plan", "Release evidence verification could not compile the install plan: "
            + string.Join("; ", planResult.Diagnostics.Select(d => $"{d.Code} {d.Path}: {d.Message}"))));
        return "";
    }

    private static void VerifySbom(string sbomPath, IReadOnlyList<ExpectedArtifact> expectedArtifacts, ReleaseEvidenceVerificationResult result)
    {
        if (!File.Exists(sbomPath))
        {
            result.Diagnostics.Add(Error("BI2213", "SBOM", $"SBOM file was not found: {sbomPath}"));
            return;
        }

        using var document = TryParseEvidenceJson(sbomPath, "SBOM", "BI2240", result);
        if (document is null)
            return;

        if (!document.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            result.Diagnostics.Add(Error("BI2214", "SBOM.files", "SBOM has no files array."));
            return;
        }

        var sbomHashes = files.EnumerateArray()
            .Where(f => f.TryGetProperty("fileName", out _))
            .ToDictionary(
                f => f.GetProperty("fileName").GetString() ?? "",
                f => FirstSha256(f),
                StringComparer.OrdinalIgnoreCase);

        foreach (var artifact in expectedArtifacts)
        {
            if (!sbomHashes.TryGetValue(artifact.Name, out var expectedSha256) || string.IsNullOrWhiteSpace(expectedSha256))
            {
                result.Diagnostics.Add(Error("BI2215", artifact.Name, $"SBOM is missing artifact '{artifact.Name}'."));
                continue;
            }

            AddArtifactResult("sbom", artifact, expectedSha256, result);
        }
    }

    private static void VerifyProvenance(
        string provenancePath,
        IReadOnlyList<ExpectedArtifact> expectedArtifacts,
        string planHash,
        ReleaseEvidenceVerificationResult result)
    {
        if (!File.Exists(provenancePath))
        {
            result.Diagnostics.Add(Error("BI2216", "Provenance", $"Provenance file was not found: {provenancePath}"));
            return;
        }

        using var document = TryParseEvidenceJson(provenancePath, "Provenance", "BI2241", result);
        if (document is null)
            return;

        if (!document.RootElement.TryGetProperty("subject", out var subject) || subject.ValueKind != JsonValueKind.Array)
        {
            result.Diagnostics.Add(Error("BI2217", "Provenance.subject", "Provenance has no subject array."));
            return;
        }

        var subjects = subject.EnumerateArray()
            .Where(s => s.TryGetProperty("name", out _) && s.TryGetProperty("digest", out _))
            .ToDictionary(
                s => s.GetProperty("name").GetString() ?? "",
                s => s.GetProperty("digest").TryGetProperty("sha256", out var sha) ? sha.GetString() ?? "" : "",
                StringComparer.OrdinalIgnoreCase);

        foreach (var artifact in expectedArtifacts.Where(a => a.Kind == "installer"))
        {
            if (!subjects.TryGetValue(artifact.Name, out var expectedSha256) || string.IsNullOrWhiteSpace(expectedSha256))
            {
                result.Diagnostics.Add(Error("BI2218", artifact.Name, $"Provenance is missing installer subject '{artifact.Name}'."));
                continue;
            }

            AddArtifactResult("provenance", artifact, expectedSha256, result);
        }

        if (!string.IsNullOrWhiteSpace(planHash))
        {
            if (!subjects.TryGetValue("compiled-install-plan", out var provenancePlanHash) || string.IsNullOrWhiteSpace(provenancePlanHash))
            {
                result.Diagnostics.Add(Error("BI2219", "compiled-install-plan", "Provenance is missing compiled-install-plan subject."));
            }
            else if (!string.Equals(planHash, provenancePlanHash, StringComparison.OrdinalIgnoreCase))
            {
                result.Diagnostics.Add(Error("BI2220", "compiled-install-plan", $"Provenance plan hash mismatch. Expected {provenancePlanHash}, actual {planHash}."));
            }
            else
            {
                result.Artifacts.Add(new ReleaseEvidenceVerifiedArtifact
                {
                    Kind = "provenance",
                    Name = "compiled-install-plan",
                    ExpectedSha256 = provenancePlanHash.ToLowerInvariant(),
                    ActualSha256 = planHash,
                    Matched = true
                });
            }
        }
    }

    private static void AddArtifactResult(
        string kind,
        ExpectedArtifact artifact,
        string expectedSha256,
        ReleaseEvidenceVerificationResult result)
    {
        var normalizedExpected = expectedSha256.ToLowerInvariant();
        var matched = string.Equals(artifact.Sha256, normalizedExpected, StringComparison.OrdinalIgnoreCase);
        result.Artifacts.Add(new ReleaseEvidenceVerifiedArtifact
        {
            Kind = kind,
            Name = artifact.Name,
            Path = artifact.Path,
            ExpectedSha256 = normalizedExpected,
            ActualSha256 = artifact.Sha256,
            Matched = matched
        });

        if (!matched)
            result.Diagnostics.Add(Error("BI2221", artifact.Name, $"{kind} hash mismatch for '{artifact.Name}'. Expected {normalizedExpected}, actual {artifact.Sha256}."));
    }

    private static void VerifyAttestation(
        string payloadPath,
        string payloadType,
        ReleaseEvidenceVerificationOptions options,
        ReleaseEvidenceVerificationResult result)
    {
        var envelopePath = payloadPath + ".dsse.json";
        if (!File.Exists(envelopePath))
        {
            if (options.RequireAttestations)
                result.Diagnostics.Add(Error("BI2222", Path.GetFileName(envelopePath), $"Required DSSE attestation was not found: {envelopePath}"));
            return;
        }

        var trustedKeys = TrustedAttestationKeys(options);
        if (trustedKeys.Count == 0)
        {
            result.Diagnostics.Add(new ProjectSchemaDiagnostic(
                ProjectSchemaDiagnosticSeverity.Warning,
                "BI2223",
                Path.GetFileName(envelopePath),
                "DSSE attestation is present but no trusted attestation public key was configured; signature was not verified."));
            return;
        }

        var verification = ReleaseEvidenceAttestation.VerifyDsseEnvelope(payloadPath, payloadType, envelopePath, trustedKeys);
        result.Artifacts.Add(new ReleaseEvidenceVerifiedArtifact
        {
            Kind = "dsse",
            Name = Path.GetFileName(envelopePath),
            Path = envelopePath,
            ExpectedSha256 = verification.PayloadSha256,
            ActualSha256 = File.Exists(payloadPath) ? Sha256File(payloadPath).ToLowerInvariant() : "",
            Matched = verification.Success
        });
        if (!verification.Success)
            result.Diagnostics.Add(Error("BI2224", Path.GetFileName(envelopePath), verification.Error));
    }

    private static void VerifyReleasePolicy(
        InstallProject project,
        ReleaseEvidenceVerificationOptions options,
        IReadOnlyList<ExpectedArtifact> expectedArtifacts,
        ReleaseEvidenceVerificationResult result)
    {
        var policy = options.PolicyEvaluation?.Policy;
        if (policy == null)
            return;

        if (policy.RequireLicenseMetadata
            && string.IsNullOrWhiteSpace(project.LicenseFile)
            && string.IsNullOrWhiteSpace(project.LicenseText))
        {
            result.Diagnostics.Add(Error("BI2225", "Setup.License",
                "Release policy requires license metadata, but neither LicenseFile nor LicenseText is configured."));
        }

        if (!policy.RequireSignedInstaller && !policy.RequireSignedArtifacts)
            return;

        var installer = expectedArtifacts.FirstOrDefault(a => a.Kind == "installer");
        if (installer == null)
        {
            result.Diagnostics.Add(Error("BI2226", "Installer",
                "Release policy requires signed release artifacts, but no installer artifact was supplied for verification."));
            return;
        }

        if (string.IsNullOrWhiteSpace(options.SigningEvidencePath))
        {
            result.Diagnostics.Add(Error("BI2227", "SigningEvidence",
                "Release policy requires signed release artifacts, but no signing evidence report was supplied."));
            return;
        }

        var signingEvidencePath = Path.GetFullPath(options.SigningEvidencePath!);
        if (!File.Exists(signingEvidencePath))
        {
            result.Diagnostics.Add(Error("BI2228", "SigningEvidence",
                $"Signing evidence report was not found: {signingEvidencePath}"));
            return;
        }

        using var document = TryParseEvidenceJson(signingEvidencePath, "SigningEvidence", "BI2242", result);
        if (document is null)
            return;

        if (!HasSuccessfulSigningEvidence(document.RootElement, installer.Name))
        {
            result.Diagnostics.Add(Error("BI2229", "SigningEvidence",
                $"Signing evidence report does not contain successful signing evidence for installer artifact '{installer.Name}'."));
        }
    }

    private static bool HasSuccessfulSigningEvidence(JsonElement root, string installerFileName)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return HasSuccessfulSigningEvidence(root.EnumerateArray(), installerFileName);

        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (root.TryGetProperty("signingEvidence", out var signingEvidence)
            && signingEvidence.ValueKind == JsonValueKind.Array
            && HasSuccessfulSigningEvidence(signingEvidence.EnumerateArray(), installerFileName))
        {
            return true;
        }

        if (root.TryGetProperty("artifactKind", out _) || root.TryGetProperty("success", out _))
            return IsSuccessfulSigningEvidence(root, installerFileName);

        return false;
    }

    private static bool HasSuccessfulSigningEvidence(JsonElement.ArrayEnumerator items, string installerFileName)
    {
        foreach (var item in items)
        {
            if (IsSuccessfulSigningEvidence(item, installerFileName))
                return true;
        }

        return false;
    }

    private static bool IsSuccessfulSigningEvidence(JsonElement item, string installerFileName)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return false;

        var success = item.TryGetProperty("success", out var successElement)
            && successElement.ValueKind == JsonValueKind.True;
        if (!success)
            return false;

        var artifactKind = item.TryGetProperty("artifactKind", out var kindElement)
            ? kindElement.GetString() ?? ""
            : "";
        var artifactPath = item.TryGetProperty("artifactPath", out var pathElement)
            ? pathElement.GetString() ?? ""
            : "";
        var artifactFileName = string.IsNullOrWhiteSpace(artifactPath) ? "" : Path.GetFileName(artifactPath);
        var kindMatches = artifactKind.Equals("exe", StringComparison.OrdinalIgnoreCase)
            || artifactKind.Equals("msi", StringComparison.OrdinalIgnoreCase)
            || artifactKind.Equals("msix", StringComparison.OrdinalIgnoreCase)
            || artifactKind.Equals("msixbundle", StringComparison.OrdinalIgnoreCase)
            || artifactKind.Equals("installer", StringComparison.OrdinalIgnoreCase);
        var fileMatches = string.IsNullOrWhiteSpace(artifactFileName)
            || artifactFileName.Equals(installerFileName, StringComparison.OrdinalIgnoreCase);
        return kindMatches && fileMatches;
    }

    private static List<string> TrustedAttestationKeys(ReleaseEvidenceVerificationOptions options)
    {
        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.TrustedAttestationPublicKey))
            keys.Add(options.TrustedAttestationPublicKey!);
        if (!string.IsNullOrWhiteSpace(options.TrustedAttestationPublicKeyPath))
        {
            var keyPath = Path.GetFullPath(options.TrustedAttestationPublicKeyPath!);
            if (File.Exists(keyPath))
                keys.Add(File.ReadAllText(keyPath));
        }
        return keys;
    }

    private static JsonDocument? TryParseEvidenceJson(
        string path,
        string diagnosticPath,
        string code,
        ReleaseEvidenceVerificationResult result)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            result.Diagnostics.Add(Error(code, diagnosticPath, $"Release evidence JSON could not be read: {ex.Message}"));
            return null;
        }
    }

    private static string FirstSha256(JsonElement file)
    {
        if (!file.TryGetProperty("checksums", out var checksums) || checksums.ValueKind != JsonValueKind.Array)
            return "";

        foreach (var checksum in checksums.EnumerateArray())
        {
            if (checksum.TryGetProperty("algorithm", out var algorithm)
                && string.Equals(algorithm.GetString(), "SHA256", StringComparison.OrdinalIgnoreCase)
                && checksum.TryGetProperty("checksumValue", out var value))
            {
                return value.GetString() ?? "";
            }
        }

        return "";
    }

    private static void WriteReport(InstallProject project, ReleaseEvidenceVerificationResult result)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "1.0",
            ["target"] = "releaseEvidence",
            ["productName"] = project.AppName,
            ["productVersion"] = project.AppVersion,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("o"),
            ["succeeded"] = result.Success,
            ["artifacts"] = result.Artifacts.Select(a => new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kind"] = a.Kind,
                ["name"] = a.Name,
                ["path"] = a.Path,
                ["expectedSha256"] = a.ExpectedSha256,
                ["actualSha256"] = a.ActualSha256,
                ["matched"] = a.Matched
            }).ToList(),
            ["diagnostics"] = result.Diagnostics.Select(d => new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["severity"] = d.Severity.ToString().ToLowerInvariant(),
                ["code"] = d.Code,
                ["path"] = d.Path,
                ["message"] = d.Message
            }).ToList()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(result.ReportPath)!);
        File.WriteAllText(result.ReportPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);

    private static string NormalizeArtifactPath(string path)
        => path.Replace('\\', '/').TrimStart('/');

    private static string SafePathForMessage(string fullPath, string root)
    {
        try
        {
            var relative = Path.GetRelativePath(root, fullPath);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                return NormalizeArtifactPath(relative);
        }
        catch
        {
            // Keep diagnostics useful without exposing broad local paths when possible.
        }

        return Path.GetFileName(fullPath);
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string SafeFile(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (value ?? "release").Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "release" : result;
    }

    private sealed record ExpectedArtifact(string Kind, string Name, string Path, string Sha256);
}
