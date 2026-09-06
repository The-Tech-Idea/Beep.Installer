using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Deployment;
using Beep.Installer.Engine;
using Beep.Installer.Policy;
using Beep.Installer.Security;

namespace Beep.Installer.Quality;

public sealed class SupplyChainQualificationOptions
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string InstallerPath { get; init; } = "";
    public string SourceRoot { get; init; } = "";
}

public sealed class SupplyChainQualificationReport
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<SupplyChainQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class SupplyChainQualificationScenario
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class SupplyChainQualificationRunner
{
    public const string ReportFileName = "supply-chain-qualification.json";

    public SupplyChainQualificationReport Run(SupplyChainQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var projectPath = Path.GetFullPath(Required(options.ProjectPath, nameof(options.ProjectPath)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory, "supply-chain-qualification")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<SupplyChainQualificationScenario>();
        var (project, loadError) = InstallerScriptSerializer.Load(projectPath);
        if (project is null)
        {
            scenarios.Add(Scenario("load-project", "Load the installer project used for supply-chain qualification.", new[]
            {
                Error("BI2301", "Project", loadError ?? "Project could not be loaded.")
            }, outputDirectory));
            return Complete(projectPath, outputDirectory, "", "", started, scenarios);
        }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        scenarios.Add(Scenario("load-project", "Load the installer project used for supply-chain qualification.", Array.Empty<ProjectSchemaDiagnostic>(), outputDirectory));

        var sourceRoot = string.IsNullOrWhiteSpace(options.SourceRoot)
            ? Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory
            : options.SourceRoot;
        var installerPath = string.IsNullOrWhiteSpace(options.InstallerPath)
            ? CreateQualificationInstaller(outputDirectory)
            : Path.GetFullPath(options.InstallerPath);

        ValidateCleanRequiredScanners(project, outputDirectory, installerPath, scenarios);
        ValidateMissingScannerFailClosed(project, outputDirectory, installerPath, scenarios);
        ValidateMalwareDetectionFailClosed(project, outputDirectory, installerPath, scenarios);
        ValidateVulnerabilityDetectionFailClosed(project, outputDirectory, installerPath, scenarios);
        ValidateScannerErrorFailClosed(project, outputDirectory, installerPath, scenarios);
        ValidateUnsignedArtifactPolicy(project, outputDirectory, installerPath, scenarios);
        ValidateSignedArtifactPolicy(project, outputDirectory, installerPath, scenarios);
        ValidateReleaseEvidenceCarriesScannerSummary(project, outputDirectory, sourceRoot, installerPath, scenarios);
        ValidateNoLocalPathLeak(outputDirectory, sourceRoot, scenarios);

        return Complete(projectPath, outputDirectory, project.AppName ?? "", project.AppVersion ?? "", started, scenarios);
    }

    public static void WriteReport(SupplyChainQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, SupplyChainQualificationJsonContext.Default.SupplyChainQualificationReport));
    }

    private static void ValidateCleanRequiredScanners(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        string installerPath,
        List<SupplyChainQualificationScenario> scenarios)
    {
        var report = Scan(project, outputDirectory, "clean-required-scanners.report.json", installerPath, new InstallerPolicy
        {
            RequireMalwareScan = true,
            RequireVulnerabilityScan = true
        }, new IArtifactSecurityScanner[]
        {
            new FixedArtifactScanner("malware", "Qualification Malware Scanner", "1.0.0", "Clean"),
            new FixedArtifactScanner("vulnerability", "Qualification Vulnerability Scanner", "1.0.0", "Clean")
        });

        var diagnostics = UnexpectedErrors(report);
        if (!report.Artifacts.Any(a => a.Scans.Any(s => s.Kind == "malware" && s.Status == "Clean")))
            diagnostics.Add(Error("BI2302", "Malware", "Required malware scan did not produce clean scanner evidence."));
        if (!report.Artifacts.Any(a => a.Scans.Any(s => s.Kind == "vulnerability" && s.Status == "Clean")))
            diagnostics.Add(Error("BI2303", "Vulnerability", "Required vulnerability scan did not produce clean scanner evidence."));
        scenarios.Add(Scenario("clean-required-scanners", "Required malware and vulnerability scanner adapters pass clean artifacts with tool evidence.", diagnostics, outputDirectory));
    }

    private static void ValidateMissingScannerFailClosed(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        string installerPath,
        List<SupplyChainQualificationScenario> scenarios)
    {
        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            InstallerPath = installerPath,
            OutputPath = Path.Combine(outputDirectory, "missing-scanners.report.json"),
            Policy = new InstallerPolicy
            {
                RequireMalwareScan = true,
                RequireVulnerabilityScan = true
            },
            UseBuiltInWindowsDefenderScanner = false,
            UseBuiltInOsvScanner = false
        });

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        RequireFinding(report, "BI9010", "Malware", "Missing required malware scanner did not fail closed.", diagnostics);
        RequireFinding(report, "BI9011", "Vulnerability", "Missing required vulnerability scanner did not fail closed.", diagnostics);
        scenarios.Add(Scenario("missing-scanners-fail-closed", "Locked-down hosts without required scanner adapters fail closed.", diagnostics, outputDirectory));
    }

    private static void ValidateMalwareDetectionFailClosed(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        string installerPath,
        List<SupplyChainQualificationScenario> scenarios)
    {
        var report = Scan(project, outputDirectory, "malware-detection.report.json", installerPath, new InstallerPolicy
        {
            RequireMalwareScan = true
        }, new IArtifactSecurityScanner[]
        {
            new FixedArtifactScanner("malware", "Qualification Malware Scanner", "1.0.0", "Detected")
                .WithDetection("EICAR-Test-File", "error", "Seeded malware test signature.")
        });

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        RequireFinding(report, "BI9012", "Malware", "Malware detection did not fail closed.", diagnostics);
        scenarios.Add(Scenario("malware-detection-fail-closed", "Seeded malware detections block release qualification.", diagnostics, outputDirectory));
    }

    private static void ValidateVulnerabilityDetectionFailClosed(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        string installerPath,
        List<SupplyChainQualificationScenario> scenarios)
    {
        var report = Scan(project, outputDirectory, "vulnerability-detection.report.json", installerPath, new InstallerPolicy
        {
            RequireVulnerabilityScan = true
        }, new IArtifactSecurityScanner[]
        {
            new FixedArtifactScanner("vulnerability", "Qualification Vulnerability Scanner", "1.0.0", "Detected")
                .WithDetection("CVE-2026-0001", "error", "Seeded critical vulnerability.")
        });

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        RequireFinding(report, "BI9013", "Vulnerability", "Vulnerability detection did not fail closed.", diagnostics);
        scenarios.Add(Scenario("vulnerability-detection-fail-closed", "Seeded vulnerability detections block release qualification.", diagnostics, outputDirectory));
    }

    private static void ValidateScannerErrorFailClosed(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        string installerPath,
        List<SupplyChainQualificationScenario> scenarios)
    {
        var report = Scan(project, outputDirectory, "scanner-error.report.json", installerPath, new InstallerPolicy
        {
            RequireMalwareScan = true
        }, new IArtifactSecurityScanner[] { new ThrowingArtifactScanner("malware") });

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        RequireFinding(report, "BI9014", "Scanner", "Scanner adapter errors did not fail closed.", diagnostics);
        scenarios.Add(Scenario("scanner-error-fail-closed", "Scanner adapter failures are release-blocking and machine-readable.", diagnostics, outputDirectory));
    }

    private static void ValidateUnsignedArtifactPolicy(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        string installerPath,
        List<SupplyChainQualificationScenario> scenarios)
    {
        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            InstallerPath = installerPath,
            OutputPath = Path.Combine(outputDirectory, "unsigned-artifact.report.json"),
            Policy = new InstallerPolicy { RequireSignedArtifacts = true },
            SignatureVerifier = new FixedSignatureVerifier(false, "NotSigned", "")
        });

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        RequireFinding(report, "BI9005", "Signing", "Unsigned installer artifacts did not fail signed-artifact policy.", diagnostics);
        scenarios.Add(Scenario("unsigned-artifact-policy", "Unsigned signable release artifacts fail closed when policy requires signed artifacts.", diagnostics, outputDirectory));
    }

    private static void ValidateSignedArtifactPolicy(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        string installerPath,
        List<SupplyChainQualificationScenario> scenarios)
    {
        var report = new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            InstallerPath = installerPath,
            OutputPath = Path.Combine(outputDirectory, "signed-artifact.report.json"),
            Policy = new InstallerPolicy
            {
                RequireSignedArtifacts = true,
                AllowedArtifactSignerSubjects = { "CN=The Tech Idea" }
            },
            SignatureVerifier = new FixedSignatureVerifier(true, "Valid", "CN=The Tech Idea Release")
        });

        var diagnostics = UnexpectedErrors(report);
        if (!report.Artifacts.Any(a => a.Kind == "installer" && a.Signature?.Trusted == true))
            diagnostics.Add(Error("BI2304", "Signing", "Trusted installer signature evidence was not recorded."));
        scenarios.Add(Scenario("signed-artifact-policy", "Trusted signed release artifacts satisfy signed-artifact policy and preserve certificate evidence.", diagnostics, outputDirectory));
    }

    private static void ValidateReleaseEvidenceCarriesScannerSummary(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        string sourceRoot,
        string installerPath,
        List<SupplyChainQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        try
        {
            var scanReport = Scan(project, outputDirectory, "release-evidence-source-scan.report.json", installerPath, new InstallerPolicy
            {
                RequireMalwareScan = true,
                RequireVulnerabilityScan = true
            }, new IArtifactSecurityScanner[]
            {
                new FixedArtifactScanner("malware", "Qualification Malware Scanner", "1.0.0", "Clean"),
                new FixedArtifactScanner("vulnerability", "Qualification Vulnerability Scanner", "1.0.0", "Clean")
            });

            var evidence = ReleaseEvidenceGenerator.Generate(project, new ReleaseEvidenceOptions
            {
                OutputDirectory = Path.Combine(outputDirectory, "release-evidence"),
                InstallerPath = installerPath,
                SourceRoot = sourceRoot,
                SupplyChainReport = scanReport
            });

            using var document = JsonDocument.Parse(File.ReadAllText(evidence.ProvenancePath));
            var summary = document.RootElement
                .GetProperty("predicate")
                .GetProperty("buildDefinition")
                .GetProperty("internalParameters")
                .GetProperty("supplyChainSecurity");
            if (summary.GetProperty("included").ValueKind != JsonValueKind.True)
                diagnostics.Add(Error("BI2305", "Provenance.supplyChainSecurity", "Release provenance did not include supply-chain scanner evidence."));
            if (!summary.GetProperty("status").GetString()!.Equals("passed", StringComparison.OrdinalIgnoreCase))
                diagnostics.Add(Error("BI2306", "Provenance.supplyChainSecurity.status", "Release provenance did not record a passing supply-chain status."));
            if (summary.GetProperty("scannerResults").GetArrayLength() == 0)
                diagnostics.Add(Error("BI2307", "Provenance.supplyChainSecurity.scannerResults", "Release provenance did not preserve scanner tool result summaries."));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            diagnostics.Add(Error("BI2308", "ReleaseEvidence", ex.Message));
        }

        scenarios.Add(Scenario("release-evidence-scanner-summary", "Release provenance preserves scanner tool/version/status summaries from the canonical supply-chain report.", diagnostics, outputDirectory));
    }

    private static void ValidateNoLocalPathLeak(
        string outputDirectory,
        string sourceRoot,
        List<SupplyChainQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var normalizedRoot = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var file in Directory.EnumerateFiles(outputDirectory, "*.report.json", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            if (text.Contains(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || text.Contains(normalizedRoot.Replace("\\", "\\\\", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Error("BI2309", Path.GetFileName(file), "Supply-chain qualification evidence leaks a local source root."));
            }
        }

        scenarios.Add(Scenario("no-security-path-leak", "Supply-chain reports use safe artifact paths and do not leak local source roots.", diagnostics, outputDirectory));
    }

    private static SupplyChainSecurityReport Scan(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        string fileName,
        string installerPath,
        InstallerPolicy policy,
        IReadOnlyList<IArtifactSecurityScanner> artifactScanners)
        => new SupplyChainSecurityScanner().Scan(project, new SupplyChainSecurityOptions
        {
            InstallerPath = installerPath,
            OutputPath = Path.Combine(outputDirectory, fileName),
            Policy = policy,
            ArtifactScanners = artifactScanners,
            SignatureVerifier = new FixedSignatureVerifier(true, "Valid", "CN=The Tech Idea Release")
        });

    private static List<ProjectSchemaDiagnostic> UnexpectedErrors(SupplyChainSecurityReport report)
        => report.Findings
            .Where(f => f.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
            .Select(f => Error(f.Code, f.Path, f.Message))
            .ToList();

    private static void RequireFinding(
        SupplyChainSecurityReport report,
        string code,
        string path,
        string message,
        List<ProjectSchemaDiagnostic> diagnostics)
    {
        if (!report.Findings.Any(f => f.Code.Equals(code, StringComparison.OrdinalIgnoreCase)
                                      && f.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(Error(code, path, message));
        }
    }

    private static SupplyChainQualificationReport Complete(
        string projectPath,
        string outputDirectory,
        string productName,
        string productVersion,
        DateTimeOffset started,
        List<SupplyChainQualificationScenario> scenarios)
    {
        var success = scenarios.All(s => s.Success);
        var report = new SupplyChainQualificationReport
        {
            ProjectPath = projectPath,
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            ProductName = productName,
            ProductVersion = productVersion,
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Supply-chain qualification completed." : "Supply-chain qualification failed.",
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    private static SupplyChainQualificationScenario Scenario(string id, string description, IEnumerable<ProjectSchemaDiagnostic> diagnostics, string outputDirectory)
    {
        var items = diagnostics.ToList();
        var success = items.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".diagnostics.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(items, SupplyChainQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return new SupplyChainQualificationScenario
        {
            Id = id,
            Description = description,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Passed." : "Failed.",
            EvidencePath = evidencePath,
            Diagnostics = items
        };
    }

    private static string CreateQualificationInstaller(string outputDirectory)
    {
        var path = Path.Combine(outputDirectory, "qualification-installer.exe");
        File.WriteAllText(path, "synthetic installer artifact for supply-chain qualification");
        return path;
    }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);

    private sealed class FixedSignatureVerifier : IArtifactSignatureVerifier
    {
        private readonly bool _trusted;
        private readonly string _status;
        private readonly string _subject;

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
                Issuer = "CN=Enterprise Issuing CA",
                Thumbprint = "ABCDEF0123456789",
                NotBeforeUtc = "2026-01-01T00:00:00.0000000Z",
                NotAfterUtc = "2027-01-01T00:00:00.0000000Z",
                TimestampSubject = "CN=Trusted Timestamp Authority",
                TimestampThumbprint = "1234567890ABCDEF",
                TimestampNotBeforeUtc = "2025-01-01T00:00:00.0000000Z",
                TimestampNotAfterUtc = "2030-01-01T00:00:00.0000000Z",
                RevocationMode = "Online"
            };
    }

    private sealed class FixedArtifactScanner : IArtifactSecurityScanner
    {
        private readonly List<ArtifactSecurityDetection> _detections = new();
        private readonly string _status;
        private readonly string _toolName;
        private readonly string _toolVersion;

        public FixedArtifactScanner(string kind, string toolName, string toolVersion, string status)
        {
            Kind = kind;
            _toolName = toolName;
            _toolVersion = toolVersion;
            _status = status;
        }

        public string Kind { get; }

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
                ToolName = _toolName,
                ToolVersion = _toolVersion,
                Status = _status,
                Message = "Qualification scan completed.",
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
            => throw new InvalidOperationException("qualification scanner adapter failed");
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SupplyChainQualificationReport))]
[JsonSerializable(typeof(SupplyChainQualificationScenario))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class SupplyChainQualificationJsonContext : JsonSerializerContext;
