using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Deployment;
using Beep.Installer.Engine;

namespace Beep.Installer.Quality;

public sealed class ReleaseEvidenceQualificationOptions
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string InstallerPath { get; init; } = "";
    public string SourceRoot { get; init; } = "";
}

public sealed class ReleaseEvidenceQualificationReport
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
    public List<ReleaseEvidenceQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class ReleaseEvidenceQualificationScenario
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class ReleaseEvidenceQualificationRunner
{
    public const string ReportFileName = "release-evidence-qualification.json";

    public ReleaseEvidenceQualificationReport Run(ReleaseEvidenceQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var projectPath = Path.GetFullPath(Required(options.ProjectPath, nameof(options.ProjectPath)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory, "release-evidence-qualification")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<ReleaseEvidenceQualificationScenario>();
        var (project, loadError) = InstallerScriptSerializer.Load(projectPath);
        if (project is null)
        {
            scenarios.Add(Scenario("load-project", "Load the installer project used for release-evidence qualification.", new[]
            {
                Error("BI2201", "Project", loadError ?? "Project could not be loaded.")
            }, outputDirectory));
            return Complete(projectPath, outputDirectory, "", "", started, scenarios);
        }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        scenarios.Add(Scenario("load-project", "Load the installer project used for release-evidence qualification.", Array.Empty<ProjectSchemaDiagnostic>(), outputDirectory));

        var sourceRoot = string.IsNullOrWhiteSpace(options.SourceRoot)
            ? Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory
            : options.SourceRoot;
        var installerPath = string.IsNullOrWhiteSpace(options.InstallerPath)
            ? CreateQualificationInstaller(outputDirectory)
            : Path.GetFullPath(options.InstallerPath);
        var generated = GenerateEvidence(project, outputDirectory, sourceRoot, installerPath, scenarios);
        if (generated != null)
        {
            VerifyGeneratedEvidence(project, outputDirectory, sourceRoot, installerPath, generated, scenarios);
            VerifyDsseAttestations(project, outputDirectory, sourceRoot, installerPath, scenarios);
            VerifyTamperDetection(project, outputDirectory, sourceRoot, installerPath, generated, scenarios);
            ValidateReleaseEvidencePathHygiene(generated, sourceRoot, scenarios, outputDirectory);
        }

        return Complete(projectPath, outputDirectory, project.AppName ?? "", project.AppVersion ?? "", started, scenarios);
    }

    public static void WriteReport(ReleaseEvidenceQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, ReleaseEvidenceQualificationJsonContext.Default.ReleaseEvidenceQualificationReport));
    }

    private static ReleaseEvidenceResult? GenerateEvidence(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        string sourceRoot,
        string installerPath,
        List<ReleaseEvidenceQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        try
        {
            var evidenceDirectory = Path.Combine(outputDirectory, "generated");
            var result = ReleaseEvidenceGenerator.Generate(project, new ReleaseEvidenceOptions
            {
                OutputDirectory = evidenceDirectory,
                InstallerPath = installerPath,
                SourceRoot = sourceRoot,
                SourceRevision = "qualification-synthetic-revision",
                BuildType = "https://the-tech-idea.example/beep-installer/qualification/release-evidence"
            });

            if (!File.Exists(result.SbomPath))
                diagnostics.Add(Error("BI2230", "SBOM", "Release evidence generation did not write an SPDX SBOM."));
            if (!File.Exists(result.ProvenancePath))
                diagnostics.Add(Error("BI2231", "Provenance", "Release evidence generation did not write provenance."));
            if (string.IsNullOrWhiteSpace(result.PlanHash))
                diagnostics.Add(Error("BI2232", "PlanHash", "Release evidence generation did not expose the compiled plan hash."));

            scenarios.Add(Scenario("generate-evidence", "Generate SBOM and provenance with the existing release evidence generator.", diagnostics, outputDirectory));
            return result;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            scenarios.Add(Scenario("generate-evidence", "Generate SBOM and provenance with the existing release evidence generator.", new[]
            {
                Error("BI2233", "ReleaseEvidence", ex.Message)
            }, outputDirectory));
            return null;
        }
    }

    private static void VerifyGeneratedEvidence(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        string sourceRoot,
        string installerPath,
        ReleaseEvidenceResult generated,
        List<ReleaseEvidenceQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        try
        {
            var verification = ReleaseEvidenceVerifier.Verify(project, new ReleaseEvidenceVerificationOptions
            {
                EvidenceDirectory = generated.OutputDirectory,
                InstallerPath = installerPath,
                SourceRoot = sourceRoot,
                ReportPath = Path.Combine(outputDirectory, "generated", "qualification-verification.json")
            });
            diagnostics.AddRange(verification.Diagnostics.Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error));
            if (!verification.Success)
                diagnostics.Add(Error("BI2234", "ReleaseEvidence", "Generated release evidence did not verify."));
            if (!verification.Artifacts.Any(a => a.Kind == "sbom" && a.Matched))
                diagnostics.Add(Error("BI2235", "SBOM", "Verification did not match any SBOM artifact hash."));
            if (!verification.Artifacts.Any(a => a.Kind == "provenance" && a.Name == "compiled-install-plan" && a.Matched))
                diagnostics.Add(Error("BI2236", "Provenance", "Verification did not match the compiled-plan provenance subject."));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            diagnostics.Add(Error("BI2237", "ReleaseEvidence", ex.Message));
        }

        scenarios.Add(Scenario("verify-generated-evidence", "Verify generated SBOM, installer and compiled-plan hashes through the existing verifier.", diagnostics, outputDirectory));
    }

    private static void VerifyDsseAttestations(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        string sourceRoot,
        string installerPath,
        List<ReleaseEvidenceQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        try
        {
            var keyDirectory = Path.Combine(outputDirectory, "attestation-keys");
            Directory.CreateDirectory(keyDirectory);
            var privateKeyPath = Path.Combine(keyDirectory, "qualification-attestation.private.pem");
            var publicKeyPath = Path.Combine(keyDirectory, "qualification-attestation.public.pem");
            using (var rsa = RSA.Create(2048))
            {
                File.WriteAllText(privateKeyPath, rsa.ExportRSAPrivateKeyPem());
                File.WriteAllText(publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());
            }

            var signedDirectory = Path.Combine(outputDirectory, "signed");
            var signed = ReleaseEvidenceGenerator.Generate(project, new ReleaseEvidenceOptions
            {
                OutputDirectory = signedDirectory,
                InstallerPath = installerPath,
                SourceRoot = sourceRoot,
                SourceRevision = "qualification-synthetic-revision",
                BuildType = "https://the-tech-idea.example/beep-installer/qualification/release-evidence",
                AttestationPrivateKeyPath = privateKeyPath,
                AttestationKeyId = "qualification-attestation"
            });

            if (!File.Exists(signed.SbomAttestationPath))
                diagnostics.Add(Error("BI2238", "SBOM.DSSE", "SBOM DSSE envelope was not generated."));
            if (!File.Exists(signed.ProvenanceAttestationPath))
                diagnostics.Add(Error("BI2239", "Provenance.DSSE", "Provenance DSSE envelope was not generated."));

            var verification = ReleaseEvidenceVerifier.Verify(project, new ReleaseEvidenceVerificationOptions
            {
                EvidenceDirectory = signed.OutputDirectory,
                InstallerPath = installerPath,
                SourceRoot = sourceRoot,
                ReportPath = Path.Combine(signedDirectory, "qualification-dsse-verification.json"),
                RequireAttestations = true,
                TrustedAttestationPublicKeyPath = publicKeyPath
            });
            diagnostics.AddRange(verification.Diagnostics.Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error));
            if (!verification.Success)
                diagnostics.Add(Error("BI2240", "DSSE", "Signed release evidence did not verify with the trusted attestation key."));
            if (verification.Artifacts.Count(a => a.Kind == "dsse" && a.Matched) < 2)
                diagnostics.Add(Error("BI2241", "DSSE", "Both SBOM and provenance DSSE envelopes must verify."));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException or CryptographicException or InvalidOperationException)
        {
            diagnostics.Add(Error("BI2242", "DSSE", ex.Message));
        }

        scenarios.Add(Scenario("verify-dsse-attestations", "Generate and verify DSSE-signed SBOM/provenance attestations with an isolated qualification key.", diagnostics, outputDirectory));
    }

    private static void VerifyTamperDetection(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        string sourceRoot,
        string installerPath,
        ReleaseEvidenceResult generated,
        List<ReleaseEvidenceQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        try
        {
            var tamperDirectory = Path.Combine(outputDirectory, "tampered");
            CopyDirectory(generated.OutputDirectory, tamperDirectory);
            var sbomPath = Path.Combine(tamperDirectory, Path.GetFileName(generated.SbomPath));
            var tamperedText = File.ReadAllText(sbomPath).Replace("\"algorithm\": \"SHA256\"", "\"algorithm\": \"SHA256-TAMPERED\"", StringComparison.Ordinal);
            File.WriteAllText(sbomPath, tamperedText);

            var verification = ReleaseEvidenceVerifier.Verify(project, new ReleaseEvidenceVerificationOptions
            {
                EvidenceDirectory = tamperDirectory,
                InstallerPath = installerPath,
                SourceRoot = sourceRoot,
                ReportPath = Path.Combine(tamperDirectory, "qualification-tamper-verification.json")
            });
            if (verification.Success)
                diagnostics.Add(Error("BI2243", "TamperDetection", "Tampered SBOM evidence verified successfully; verifier must fail closed."));
            if (!verification.Diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                diagnostics.Add(Error("BI2244", "TamperDetection", "Tampered evidence did not produce an error diagnostic."));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            diagnostics.Add(Error("BI2245", "TamperDetection", ex.Message));
        }

        scenarios.Add(Scenario("tamper-detection", "Prove evidence verification fails closed when release evidence is changed after generation.", diagnostics, outputDirectory));
    }

    private static void ValidateReleaseEvidencePathHygiene(
        ReleaseEvidenceResult generated,
        string sourceRoot,
        List<ReleaseEvidenceQualificationScenario> scenarios,
        string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var normalizedRoot = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var path in new[] { generated.SbomPath, generated.ProvenancePath, generated.SbomAttestationPath, generated.ProvenanceAttestationPath })
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            var text = File.ReadAllText(path);
            if (text.Contains(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || text.Contains(normalizedRoot.Replace("\\", "\\\\", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase)
                || text.Contains("qualification-attestation.private", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Error("BI2246", Path.GetFileName(path), "Release evidence sidecar leaks a local source path or private-key file name."));
            }
        }

        scenarios.Add(Scenario("no-local-path-leak", "SBOM/provenance sidecars contain safe artifact names and do not leak local source roots.", diagnostics, outputDirectory));
    }

    private static ReleaseEvidenceQualificationReport Complete(
        string projectPath,
        string outputDirectory,
        string productName,
        string productVersion,
        DateTimeOffset started,
        List<ReleaseEvidenceQualificationScenario> scenarios)
    {
        var success = scenarios.All(s => s.Success);
        var report = new ReleaseEvidenceQualificationReport
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
            Message = success ? "Release-evidence qualification completed." : "Release-evidence qualification failed.",
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    private static ReleaseEvidenceQualificationScenario Scenario(string id, string description, IEnumerable<ProjectSchemaDiagnostic> diagnostics, string outputDirectory)
    {
        var items = diagnostics.ToList();
        var success = items.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".diagnostics.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(items, ReleaseEvidenceQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return new ReleaseEvidenceQualificationScenario
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
        File.WriteAllText(path, "synthetic installer artifact for release evidence qualification");
        return path;
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
            File.Copy(file, Path.Combine(targetDirectory, Path.GetFileName(file)), overwrite: true);
    }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ReleaseEvidenceQualificationReport))]
[JsonSerializable(typeof(ReleaseEvidenceQualificationScenario))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class ReleaseEvidenceQualificationJsonContext : JsonSerializerContext;
