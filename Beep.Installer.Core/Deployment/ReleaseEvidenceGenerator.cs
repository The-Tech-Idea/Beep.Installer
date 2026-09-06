using System.Security.Cryptography;
using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Models;
using Beep.Installer.Policy;
using Beep.Installer.Security;

namespace Beep.Installer.Deployment;

public sealed class ReleaseEvidenceOptions
{
    public string? OutputDirectory { get; init; }
    public string? InstallerPath { get; init; }
    public string? SourceRoot { get; init; }
    public string? SourceRevision { get; init; }
    public string BuildType { get; init; } = "https://the-tech-idea.example/beep-installer/build/v1";
    public InstallerPolicyEvaluation? PolicyEvaluation { get; init; }
    public SupplyChainSecurityReport? SupplyChainReport { get; init; }
    public IReadOnlyList<InstallerExtensionReference> Extensions { get; init; } = Array.Empty<InstallerExtensionReference>();
    public string? AttestationPrivateKeyPath { get; init; }
    public string? AttestationKeyId { get; init; }
}

public sealed class ReleaseEvidenceResult
{
    public string OutputDirectory { get; init; } = "";
    public string SbomPath { get; init; } = "";
    public string ProvenancePath { get; init; } = "";
    public string SbomAttestationPath { get; set; } = "";
    public string ProvenanceAttestationPath { get; set; } = "";
    public string PlanHash { get; init; } = "";
    public List<string> Files { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public static class ReleaseEvidenceGenerator
{
    public static ReleaseEvidenceResult Generate(InstallProject project, ReleaseEvidenceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        options ??= new ReleaseEvidenceOptions();

        var outputDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(options.OutputDirectory)
                ? Path.Combine(BuildPipeline.ResolveOutputDirectory(project), "evidence")
                : options.OutputDirectory!);
        Directory.CreateDirectory(outputDirectory);

        var planResult = new InstallPlanCompiler().Compile(project, options.PolicyEvaluation);
        if (!planResult.Success || planResult.Plan == null)
            throw new InvalidOperationException(
                "Release evidence requires a compiled install plan: "
                + string.Join("; ", planResult.Diagnostics.Select(d => $"{d.Code} {d.Path}: {d.Message}")));

        var result = new ReleaseEvidenceResult
        {
            OutputDirectory = outputDirectory,
            SbomPath = Path.Combine(outputDirectory, $"{SafeFile(project.AppName)}-{SafeFile(project.AppVersion)}.spdx.json"),
            ProvenancePath = Path.Combine(outputDirectory, $"{SafeFile(project.AppName)}-{SafeFile(project.AppVersion)}.provenance.json"),
            PlanHash = planResult.Plan.PlanHash
        };

        var sourceRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(options.SourceRoot) ? project.SourceDirectory : options.SourceRoot!);
        var files = CollectFiles(project, sourceRoot, result.Warnings);
        var installerSubject = InstallerSubject(options.InstallerPath, result.Warnings);
        var dependencyPackages = CollectDependencyPackages(project, options.Extensions);
        var sbom = CreateSpdx(project, files, dependencyPackages, installerSubject, result.PlanHash);
        var provenance = CreateProvenance(project, files, dependencyPackages, installerSubject, result.PlanHash, options);

        WriteJson(result.SbomPath, sbom);
        result.Files.Add(result.SbomPath);
        WriteJson(result.ProvenancePath, provenance);
        result.Files.Add(result.ProvenancePath);
        if (!string.IsNullOrWhiteSpace(options.AttestationPrivateKeyPath))
        {
            var sbomEnvelopePath = ReleaseEvidenceAttestation.WriteDsseEnvelope(
                result.SbomPath,
                "application/spdx+json",
                options.AttestationPrivateKeyPath!,
                options.AttestationKeyId ?? "");
            result.Files.Add(sbomEnvelopePath);
            var provenanceEnvelopePath = ReleaseEvidenceAttestation.WriteDsseEnvelope(
                result.ProvenancePath,
                "application/vnd.in-toto+json",
                options.AttestationPrivateKeyPath!,
                options.AttestationKeyId ?? "");
            result.Files.Add(provenanceEnvelopePath);
            result.SbomAttestationPath = sbomEnvelopePath;
            result.ProvenanceAttestationPath = provenanceEnvelopePath;
        }
        return result;
    }

    private static IReadOnlyList<EvidenceFile> CollectFiles(InstallProject project, string sourceRoot, List<string> warnings)
    {
        var files = new List<EvidenceFile>();
        foreach (var component in project.Components.OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var file in component.Files.OrderBy(f => f.DestinationPath, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(file.SourcePath))
                    continue;

                var sourcePath = Path.GetFullPath(file.SourcePath);
                if (!File.Exists(sourcePath))
                {
                    warnings.Add($"SBOM skipped missing file '{SafePathForMessage(sourcePath, sourceRoot)}'.");
                    continue;
                }

                var destination = NormalizeArtifactPath(file.DestinationPath);
                var relationshipTarget = string.IsNullOrWhiteSpace(destination)
                    ? Path.GetFileName(sourcePath)
                    : destination;
                files.Add(new EvidenceFile(
                    ComponentId: component.Id,
                    SourceRelativePath: SafePathForMessage(sourcePath, sourceRoot),
                    ArtifactPath: relationshipTarget,
                    Sha256: Sha256File(sourcePath),
                    SizeBytes: new FileInfo(sourcePath).Length));
            }
        }

        return files;
    }

    private static EvidenceSubject? InstallerSubject(string? installerPath, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(installerPath))
        {
            warnings.Add("No installer artifact was supplied; provenance subject contains the project plan only.");
            return null;
        }

        var fullPath = Path.GetFullPath(installerPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Installer artifact was not found.", fullPath);

        return new EvidenceSubject(Path.GetFileName(fullPath), Sha256File(fullPath), new FileInfo(fullPath).Length);
    }

    private static SortedDictionary<string, object?> CreateSpdx(
        InstallProject project,
        IReadOnlyList<EvidenceFile> files,
        IReadOnlyList<EvidencePackage> dependencyPackages,
        EvidenceSubject? installerSubject,
        string planHash)
    {
        var documentNamespace = $"https://the-tech-idea.example/spdx/{Slug(project.AppPublisher)}/{Slug(project.AppName)}/{project.AppVersion}/{planHash}";
        var spdxFiles = files.Select((file, index) => new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["SPDXID"] = $"SPDXRef-File-{index + 1}",
            ["checksums"] = new[] { new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["algorithm"] = "SHA256",
                ["checksumValue"] = file.Sha256.ToLowerInvariant()
            }},
            ["copyrightText"] = "NOASSERTION",
            ["fileName"] = file.ArtifactPath,
            ["fileTypes"] = new[] { "BINARY" },
            ["licenseConcluded"] = "NOASSERTION",
            ["licenseInfoInFiles"] = new[] { "NOASSERTION" }
        }).ToList();

        if (installerSubject != null)
        {
            spdxFiles.Insert(0, new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["SPDXID"] = "SPDXRef-Installer",
                ["checksums"] = new[] { new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["algorithm"] = "SHA256",
                    ["checksumValue"] = installerSubject.Sha256.ToLowerInvariant()
                }},
                ["copyrightText"] = "NOASSERTION",
                ["fileName"] = installerSubject.Name,
                ["fileTypes"] = new[] { "BINARY" },
                ["licenseConcluded"] = "NOASSERTION",
                ["licenseInfoInFiles"] = new[] { "NOASSERTION" }
            });
        }

        var packageVerificationCode = Sha256Text(string.Join('\n', files.Select(f => f.Sha256).Order(StringComparer.Ordinal)));
        var packages = new List<SortedDictionary<string, object?>>
        {
            new(StringComparer.Ordinal)
            {
                ["SPDXID"] = "SPDXRef-Package",
                ["downloadLocation"] = "NOASSERTION",
                ["filesAnalyzed"] = true,
                ["licenseConcluded"] = "NOASSERTION",
                ["licenseDeclared"] = LicenseFromProject(project),
                ["name"] = project.AppName,
                ["packageFileName"] = installerSubject?.Name ?? "NOASSERTION",
                ["packageVerificationCode"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["packageVerificationCodeValue"] = packageVerificationCode.ToLowerInvariant()
                },
                ["versionInfo"] = project.AppVersion
            }
        };
        packages.AddRange(dependencyPackages.Select(CreateSpdxPackage));

        return new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["SPDXID"] = "SPDXRef-DOCUMENT",
            ["creationInfo"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["created"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["creators"] = new[] { "Tool: Beep.Installer" }
            },
            ["dataLicense"] = "CC0-1.0",
            ["documentNamespace"] = documentNamespace,
            ["files"] = spdxFiles,
            ["name"] = $"{project.AppName} {project.AppVersion} SBOM",
            ["packages"] = packages,
            ["relationships"] = BuildRelationships(files, dependencyPackages, installerSubject),
            ["spdxVersion"] = "SPDX-2.3"
        };
    }

    private static SortedDictionary<string, object?> CreateProvenance(
        InstallProject project,
        IReadOnlyList<EvidenceFile> files,
        IReadOnlyList<EvidencePackage> dependencyPackages,
        EvidenceSubject? installerSubject,
        string planHash,
        ReleaseEvidenceOptions options)
    {
        var subject = new List<SortedDictionary<string, object?>>();
        subject.Add(new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = "compiled-install-plan",
            ["digest"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sha256"] = planHash.ToLowerInvariant()
            }
        });
        if (installerSubject != null)
        {
            subject.Insert(0, new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = installerSubject.Name,
                ["digest"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["sha256"] = installerSubject.Sha256.ToLowerInvariant()
                }
            });
        }

        return new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["_type"] = "https://in-toto.io/Statement/v1",
            ["predicateType"] = "https://slsa.dev/provenance/v1",
            ["subject"] = subject,
            ["predicate"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["buildDefinition"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["buildType"] = string.IsNullOrWhiteSpace(options.BuildType)
                        ? "https://the-tech-idea.example/beep-installer/build/v1"
                        : options.BuildType,
                    ["externalParameters"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["packageName"] = project.AppName,
                        ["packageVersion"] = project.AppVersion,
                        ["publisher"] = project.AppPublisher,
                        ["scope"] = project.DefaultScope == InstallationScope.User ? "user" : "machine",
                        ["outputFormat"] = project.OutputFormat.ToString().ToLowerInvariant()
                    },
                    ["internalParameters"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["compiledPlanSha256"] = planHash.ToLowerInvariant(),
                        ["payloadFileCount"] = files.Count,
                        ["policy"] = CreatePolicySummary(options.PolicyEvaluation),
                        ["supplyChainSecurity"] = CreateSupplyChainSummary(options.SupplyChainReport)
                    },
                    ["resolvedDependencies"] = BuildResolvedDependencies(files, dependencyPackages)
                },
                ["runDetails"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["builder"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["id"] = "https://the-tech-idea.example/beep-installer"
                    },
                    ["metadata"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["invocationId"] = Guid.NewGuid().ToString("N"),
                        ["startedOn"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        ["finishedOn"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    }
                },
                ["sourceRevision"] = string.IsNullOrWhiteSpace(options.SourceRevision) ? "NOASSERTION" : options.SourceRevision
            }
        };
    }

    private static SortedDictionary<string, object?> CreateSupplyChainSummary(SupplyChainSecurityReport? report)
    {
        if (report == null)
        {
            return new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["included"] = false,
                ["status"] = "not-run"
            };
        }

        var findingsByCode = report.Findings
            .GroupBy(finding => finding.Code, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = group.Key,
                ["count"] = group.Count(),
                ["errors"] = group.Count(f => f.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)),
                ["warnings"] = group.Count(f => f.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase)),
                ["waived"] = group.Count(f => f.Waived)
            })
            .ToList();

        var scannerResults = report.Artifacts
            .SelectMany(artifact => artifact.Scans.Select(scan => new { Artifact = artifact, Scan = scan }))
            .OrderBy(item => item.Artifact.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Scan.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Scan.ToolName, StringComparer.OrdinalIgnoreCase)
            .Select(item => new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["artifact"] = item.Artifact.Path,
                ["kind"] = item.Scan.Kind,
                ["toolName"] = item.Scan.ToolName,
                ["toolVersion"] = item.Scan.ToolVersion,
                ["status"] = item.Scan.Status,
                ["detectionCount"] = item.Scan.Detections.Count
            })
            .ToList();

        return new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["included"] = true,
            ["status"] = report.HasErrors ? "failed" : "passed",
            ["schemaVersion"] = report.SchemaVersion,
            ["planHash"] = report.PlanHash.ToLowerInvariant(),
            ["artifactCount"] = report.Artifacts.Count,
            ["scannedArtifactCount"] = report.Artifacts.Count(artifact => artifact.Scans.Count > 0),
            ["findingCount"] = report.Findings.Count,
            ["errorCount"] = report.Findings.Count(finding => finding.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)),
            ["warningCount"] = report.Findings.Count(finding => finding.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase)),
            ["waivedFindingCount"] = report.Findings.Count(finding => finding.Waived),
            ["findingsByCode"] = findingsByCode,
            ["scannerResults"] = scannerResults
        };
    }

    private static SortedDictionary<string, object?> CreatePolicySummary(InstallerPolicyEvaluation? evaluation)
    {
        var evidence = evaluation is null
            ? new InstallerPolicyDecisionEvidence { Included = false, Status = "not-configured" }
            : InstallerPolicyEvaluator.CreateEvidence(evaluation);

        return new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["included"] = evidence.Included,
            ["status"] = evidence.Status,
            ["effectivePolicySha256"] = evidence.EffectivePolicySha256,
            ["sources"] = evidence.Sources.Select(source => new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kind"] = source.Kind,
                ["name"] = source.Name,
                ["sha256"] = source.Sha256
            }).ToList(),
            ["policyIssuer"] = evidence.PolicyIssuer,
            ["requireSignedPolicy"] = evidence.RequireSignedPolicy,
            ["policySignaturePresent"] = evidence.PolicySignaturePresent,
            ["trustedPolicyIssuerCount"] = evidence.TrustedPolicyIssuerCount,
            ["trustedPolicySigningKeyCount"] = evidence.TrustedPolicySigningKeyCount,
            ["requiredControls"] = evidence.RequiredControls,
            ["constraintCounts"] = evidence.ConstraintCounts,
            ["diagnostics"] = evidence.Diagnostics.Select(diagnostic => new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["severity"] = diagnostic.Severity.ToString().ToLowerInvariant(),
                ["code"] = diagnostic.Code,
                ["path"] = diagnostic.Path,
                ["message"] = diagnostic.Message
            }).ToList()
        };
    }

    private static List<SortedDictionary<string, object?>> BuildRelationships(
        IReadOnlyList<EvidenceFile> files,
        IReadOnlyList<EvidencePackage> dependencyPackages,
        EvidenceSubject? installerSubject)
    {
        var relationships = new List<SortedDictionary<string, object?>>
        {
            new(StringComparer.Ordinal)
            {
                ["spdxElementId"] = "SPDXRef-DOCUMENT",
                ["relationshipType"] = "DESCRIBES",
                ["relatedSpdxElement"] = "SPDXRef-Package"
            }
        };

        if (installerSubject != null)
        {
            relationships.Add(new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["spdxElementId"] = "SPDXRef-Package",
                ["relationshipType"] = "CONTAINS",
                ["relatedSpdxElement"] = "SPDXRef-Installer"
            });
        }

        relationships.AddRange(files.Select((_, index) => new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["spdxElementId"] = "SPDXRef-Package",
            ["relationshipType"] = "CONTAINS",
            ["relatedSpdxElement"] = $"SPDXRef-File-{index + 1}"
        }));
        relationships.AddRange(dependencyPackages.Select(package => new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["spdxElementId"] = "SPDXRef-Package",
            ["relationshipType"] = "DEPENDS_ON",
            ["relatedSpdxElement"] = package.SpdxId
        }));
        return relationships;
    }

    private static IReadOnlyList<EvidencePackage> CollectDependencyPackages(
        InstallProject project,
        IReadOnlyList<InstallerExtensionReference> extensions)
    {
        var packages = new List<EvidencePackage>();
        packages.AddRange(project.Prerequisites
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(p => new EvidencePackage(
                SpdxId: $"SPDXRef-Prerequisite-{SpdxToken(p.Id)}",
                Kind: "prerequisite",
                Name: FirstNonEmpty(p.Name, p.Id),
                Version: p.VersionRequired,
                DownloadLocation: FirstNonEmpty(p.DownloadUrl, p.DownloadUrlX86, "NOASSERTION"),
                Sha256: "",
                PackageFileName: FileNameFromLocation(FirstNonEmpty(p.DownloadUrl, p.DownloadUrlX86)),
                Supplier: "NOASSERTION",
                Metadata: new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["id"] = p.Id,
                    ["mandatory"] = p.IsMandatory,
                    ["helpUrl"] = string.IsNullOrWhiteSpace(p.HelpUrl) ? null : p.HelpUrl
                })));

        packages.AddRange(project.Packages
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(p => new EvidencePackage(
                SpdxId: $"SPDXRef-PackageNode-{SpdxToken(p.Id)}",
                Kind: "package-node",
                Name: FirstNonEmpty(p.Name, p.Id),
                Version: "",
                DownloadLocation: FirstNonEmpty(p.DownloadUrl, p.DownloadUrlX86, "NOASSERTION"),
                Sha256: p.Sha256,
                PackageFileName: FileNameFromLocation(FirstNonEmpty(p.SourcePath, p.DownloadUrl, p.DownloadUrlX86)),
                Supplier: "NOASSERTION",
                Metadata: new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["id"] = p.Id,
                    ["packageType"] = p.PackageType.ToString(),
                    ["mandatory"] = p.IsMandatory,
                    ["sha512Present"] = !string.IsNullOrWhiteSpace(p.Sha512) || !string.IsNullOrWhiteSpace(p.Sha512X86)
                })));

        packages.AddRange(extensions
            .OrderBy(e => e.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .Select(e => new EvidencePackage(
                SpdxId: $"SPDXRef-Extension-{SpdxToken(e.Manifest.Id)}",
                Kind: "installer-extension",
                Name: e.Manifest.Id,
                Version: e.Manifest.Version,
                DownloadLocation: "NOASSERTION",
                Sha256: e.Manifest.Sha256,
                PackageFileName: Path.GetFileName(e.Manifest.EntryAssembly),
                Supplier: string.IsNullOrWhiteSpace(e.Manifest.Publisher) ? "NOASSERTION" : $"Organization: {e.Manifest.Publisher}",
                Metadata: new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["id"] = e.Manifest.Id,
                    ["minimumEngineVersion"] = e.Manifest.MinimumEngineVersion,
                    ["maximumEngineVersion"] = string.IsNullOrWhiteSpace(e.Manifest.MaximumEngineVersion) ? null : e.Manifest.MaximumEngineVersion,
                    ["permissions"] = e.Manifest.Permissions.ToString(),
                    ["resourceTypes"] = e.Manifest.ResourceTypes.Order(StringComparer.OrdinalIgnoreCase).ToList(),
                    ["signaturePresent"] = !string.IsNullOrWhiteSpace(e.Manifest.Signature)
                })));

        return packages
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.SpdxId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static SortedDictionary<string, object?> CreateSpdxPackage(EvidencePackage package)
    {
        var spdxPackage = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["SPDXID"] = package.SpdxId,
            ["downloadLocation"] = package.DownloadLocation,
            ["filesAnalyzed"] = false,
            ["licenseConcluded"] = "NOASSERTION",
            ["licenseDeclared"] = "NOASSERTION",
            ["name"] = package.Name,
            ["supplier"] = package.Supplier
        };
        if (!string.IsNullOrWhiteSpace(package.Version))
            spdxPackage["versionInfo"] = package.Version;
        if (!string.IsNullOrWhiteSpace(package.PackageFileName))
            spdxPackage["packageFileName"] = package.PackageFileName;
        if (!string.IsNullOrWhiteSpace(package.Sha256))
        {
            spdxPackage["checksums"] = new[] { new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["algorithm"] = "SHA256",
                ["checksumValue"] = package.Sha256.ToLowerInvariant()
            }};
        }
        return spdxPackage;
    }

    private static List<SortedDictionary<string, object?>> BuildResolvedDependencies(
        IReadOnlyList<EvidenceFile> files,
        IReadOnlyList<EvidencePackage> dependencyPackages)
    {
        var dependencies = files.Select(file => new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "payload-file",
            ["name"] = file.ArtifactPath,
            ["digest"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sha256"] = file.Sha256.ToLowerInvariant()
            }
        }).ToList();

        dependencies.AddRange(dependencyPackages.Select(package =>
        {
            var dependency = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = package.Kind,
                ["name"] = package.Name,
                ["metadata"] = package.Metadata
            };
            if (!string.IsNullOrWhiteSpace(package.Version))
                dependency["version"] = package.Version;
            if (!string.IsNullOrWhiteSpace(package.DownloadLocation) && package.DownloadLocation != "NOASSERTION")
                dependency["uri"] = package.DownloadLocation;
            if (!string.IsNullOrWhiteSpace(package.PackageFileName))
                dependency["fileName"] = package.PackageFileName;
            if (!string.IsNullOrWhiteSpace(package.Sha256))
            {
                dependency["digest"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["sha256"] = package.Sha256.ToLowerInvariant()
                };
            }
            return dependency;
        }));

        return dependencies;
    }

    private static string LicenseFromProject(InstallProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.LicenseText) || !string.IsNullOrWhiteSpace(project.LicenseFile))
            return "LicenseRef-ProjectLicense";
        return "NOASSERTION";
    }

    private static string NormalizeArtifactPath(string path)
        => path.Replace('\\', '/').TrimStart('/');

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string FileNameFromLocation(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return "";
        if (Uri.TryCreate(location, UriKind.Absolute, out var uri))
            return Path.GetFileName(uri.LocalPath);
        return Path.GetFileName(location);
    }

    private static string SpdxToken(string? value)
    {
        var chars = (value ?? "dependency")
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var token = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(token) ? "dependency" : token;
    }

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
            // Ignore and fall through to file name only; evidence must not leak local paths.
        }

        return Path.GetFileName(fullPath);
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Sha256Text(string value)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private static string SafeFile(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (value ?? "release").Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "release" : result;
    }

    private static string Slug(string? value)
    {
        var chars = (value ?? "unknown").Where(char.IsLetterOrDigit).ToArray();
        return chars.Length == 0 ? "unknown" : new string(chars);
    }

    private static void WriteJson(string path, SortedDictionary<string, object?> payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private sealed record EvidenceFile(string ComponentId, string SourceRelativePath, string ArtifactPath, string Sha256, long SizeBytes);
    private sealed record EvidencePackage(
        string SpdxId,
        string Kind,
        string Name,
        string Version,
        string DownloadLocation,
        string Sha256,
        string PackageFileName,
        string Supplier,
        SortedDictionary<string, object?> Metadata);
    private sealed record EvidenceSubject(string Name, string Sha256, long SizeBytes);
}
