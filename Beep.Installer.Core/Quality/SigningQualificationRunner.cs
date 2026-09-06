using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Engine;

namespace Beep.Installer.Quality;

public sealed class SigningQualificationOptions
{
    public string OutputDirectory { get; init; } = "";
}

public sealed class SigningQualificationReport
{
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<SigningQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class SigningQualificationScenario
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class SigningQualificationRunner
{
    public const string ReportFileName = "signing-qualification.json";

    public SigningQualificationReport Run(SigningQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Environment.CurrentDirectory, "signing-qualification")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var artifactPath = Path.Combine(outputDirectory, "unsigned-setup.exe");
        File.WriteAllText(artifactPath, "unsigned artifact for signing qualification");

        var scenarios = new List<SigningQualificationScenario>
        {
            ValidatePfxSecretBoundary(artifactPath, outputDirectory),
            ValidateStoreSelector(artifactPath, outputDirectory),
            ValidateRemoteCredentialBoundary(artifactPath, outputDirectory),
            ValidateTimestampPolicy(artifactPath, outputDirectory),
            ValidateMixedSourceRejection(artifactPath, outputDirectory),
            ValidateNoResolvedSecretLeak(outputDirectory)
        };

        return Complete(outputDirectory, started, scenarios);
    }

    public static void WriteReport(SigningQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, SigningQualificationJsonContext.Default.SigningQualificationReport));
    }

    private static SigningQualificationScenario ValidatePfxSecretBoundary(string artifactPath, string outputDirectory)
    {
        const string resolvedSecret = "resolved-pfx-password!";
        var signer = new CapturingSigner();
        var service = new CodeSigningService(signer, new FixedSecretProvider("env", resolvedSecret));
        var result = service.SignInstaller(
            artifactPath,
            Path.Combine(outputDirectory, "release.pfx"),
            "secret://env/SIGNING_PFX_PASSWORD",
            "https://timestamp.example.test",
            expectedSubject: "CN=ACME Release");

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (!result.Success)
            diagnostics.Add(Error("BI2101", "Signing.Pfx", result.Error ?? "PFX signing did not succeed."));
        if (!result.PasswordWasSecretReference)
            diagnostics.Add(Error("BI2102", "Signing.Pfx.Password", "PFX signing password was not identified as an opaque secret reference."));
        if (signer.LastRequest?.CertificatePassword != resolvedSecret)
            diagnostics.Add(Error("BI2103", "Signing.Pfx.Password", "PFX signing password did not resolve at the signing boundary."));
        if (JsonSerializer.Serialize(result, SigningQualificationJsonContext.Default.CodeSigningResult).Contains(resolvedSecret, StringComparison.Ordinal))
            diagnostics.Add(Error("BI2104", "Signing.Pfx.Password", "Resolved PFX password leaked into signing result evidence."));

        return Scenario("pfx-secret-boundary", "PFX passwords resolve only at the signing boundary and do not appear in evidence.", diagnostics, outputDirectory);
    }

    private static SigningQualificationScenario ValidateStoreSelector(string artifactPath, string outputDirectory)
    {
        var signer = new CapturingSigner();
        var service = new CodeSigningService(signer, new FixedSecretProvider("env", "unused"));
        var result = service.SignInstaller(
            artifactPath,
            certificatePath: "",
            certificatePassword: "secret://env/SHOULD_NOT_RESOLVE_FOR_STORE",
            timestampUrl: "https://timestamp.example.test",
            expectedSubject: "CN=ACME Release",
            storeName: "My",
            storeLocation: "LocalMachine",
            storeThumbprint: "aa bb cc",
            storeSubject: "");

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (!result.Success)
            diagnostics.Add(Error("BI2105", "Signing.Store", result.Error ?? "Certificate-store signing did not succeed."));
        if (result.PasswordWasSecretReference)
            diagnostics.Add(Error("BI2106", "Signing.Store.Password", "Store signing should not resolve a PFX password."));
        if (result.CertificateStoreThumbprint != "AABBCC")
            diagnostics.Add(Error("BI2107", "Signing.Store.Thumbprint", "Certificate-store thumbprint was not normalized."));
        if (signer.LastRequest?.StoreLocation != "LocalMachine")
            diagnostics.Add(Error("BI2108", "Signing.Store.Location", "Store selector was not passed to the signer."));

        return Scenario("certificate-store-selector", "Windows certificate-store signing uses selectors without resolving unrelated PFX passwords.", diagnostics, outputDirectory);
    }

    private static SigningQualificationScenario ValidateRemoteCredentialBoundary(string artifactPath, string outputDirectory)
    {
        const string resolvedCredential = "remote-token!";
        var signer = new CapturingSigner();
        var service = new CodeSigningService(signer, new FixedSecretProvider("env", resolvedCredential));
        var result = service.SignInstaller(
            artifactPath,
            certificatePath: "",
            certificatePassword: null,
            timestampUrl: "https://timestamp.example.test",
            remoteProvider: "enterprise-hsm",
            remoteEndpoint: "https://signing.example.test/api/sign",
            remoteKeyId: "release-key",
            remoteCredential: "secret://env/REMOTE_SIGNING_TOKEN");

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (!result.Success)
            diagnostics.Add(Error("BI2109", "Signing.Remote", result.Error ?? "Remote signing did not succeed."));
        if (!result.RemoteCredentialWasSecretReference)
            diagnostics.Add(Error("BI2110", "Signing.Remote.Credential", "Remote signing credential was not identified as an opaque secret reference."));
        if (signer.LastRequest?.RemoteCredential != resolvedCredential)
            diagnostics.Add(Error("BI2111", "Signing.Remote.Credential", "Remote credential did not resolve at the signing boundary."));
        if (!result.AuditEvents.Any(e => e.EventType == "remote-sign.completed" && e.Success && e.KeyId == "release-key"))
            diagnostics.Add(Error("BI2112", "Signing.Remote.Audit", "Remote signing did not emit completion audit evidence."));
        if (JsonSerializer.Serialize(result, SigningQualificationJsonContext.Default.CodeSigningResult).Contains(resolvedCredential, StringComparison.Ordinal))
            diagnostics.Add(Error("BI2113", "Signing.Remote.Credential", "Resolved remote credential leaked into signing result evidence."));

        return Scenario("remote-credential-audit", "Remote signing credentials resolve at the boundary and remote audit evidence is emitted without secret leakage.", diagnostics, outputDirectory);
    }

    private static SigningQualificationScenario ValidateTimestampPolicy(string artifactPath, string outputDirectory)
    {
        var signer = new CapturingSigner();
        var service = new CodeSigningService(signer, new FixedSecretProvider("env", "resolved-password"));
        var result = service.SignInstaller(
            artifactPath,
            Path.Combine(outputDirectory, "release.pfx"),
            "secret://env/SIGNING_PFX_PASSWORD",
            "https://timestamp.example.test",
            timestampOutagePolicy: "retry",
            timestampRetryCount: 4);

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (!result.Success)
            diagnostics.Add(Error("BI2114", "Signing.Timestamp", result.Error ?? "Timestamp policy signing did not succeed."));
        if (result.TimestampOutagePolicy != "retry" || signer.LastRequest?.TimestampOutagePolicy != "retry")
            diagnostics.Add(Error("BI2115", "Signing.Timestamp.Policy", "Timestamp outage policy was not normalized and passed through."));
        if (signer.LastRequest?.TimestampRetryCount != 4)
            diagnostics.Add(Error("BI2116", "Signing.Timestamp.Retry", "Timestamp retry count was not passed to the signer."));

        return Scenario("timestamp-policy", "Timestamp outage policy and retry budget are normalized and passed to the signer.", diagnostics, outputDirectory);
    }

    private static SigningQualificationScenario ValidateMixedSourceRejection(string artifactPath, string outputDirectory)
    {
        var service = new CodeSigningService(new CapturingSigner(), new FixedSecretProvider("env", "unused"));
        var result = service.SignInstaller(
            artifactPath,
            certificatePath: Path.Combine(outputDirectory, "release.pfx"),
            certificatePassword: null,
            timestampUrl: "https://timestamp.example.test",
            storeThumbprint: "AABBCC");

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (result.Success)
            diagnostics.Add(Error("BI2117", "Signing.Source", "Mixed PFX/store signing selectors should fail closed."));
        if (string.IsNullOrWhiteSpace(result.Error) || !result.Error.Contains("exactly one signing source", StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(Error("BI2118", "Signing.Source", "Mixed signing source error should be explicit and actionable."));

        return Scenario("mixed-source-rejection", "PFX, store and remote signing selectors are mutually exclusive.", diagnostics, outputDirectory);
    }

    private static SigningQualificationScenario ValidateNoResolvedSecretLeak(string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        foreach (var file in Directory.EnumerateFiles(outputDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            if (text.Contains("resolved-pfx-password!", StringComparison.Ordinal)
                || text.Contains("remote-token!", StringComparison.Ordinal))
            {
                diagnostics.Add(Error("BI2119", file, "Signing qualification evidence contains a resolved secret."));
            }
        }

        return Scenario("no-signing-secret-leak", "Signing qualification evidence contains no resolved signing secrets.", diagnostics, outputDirectory);
    }

    private static SigningQualificationReport Complete(
        string outputDirectory,
        DateTimeOffset started,
        List<SigningQualificationScenario> scenarios)
    {
        var success = scenarios.All(s => s.Success);
        var report = new SigningQualificationReport
        {
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Signing qualification completed." : "Signing qualification failed.",
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    private static SigningQualificationScenario Scenario(string id, string description, IEnumerable<ProjectSchemaDiagnostic> diagnostics, string outputDirectory)
    {
        var items = diagnostics.ToList();
        var success = items.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".diagnostics.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(items, SigningQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return new SigningQualificationScenario
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

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);

    private sealed class CapturingSigner : IAuthenticodeSigner
    {
        public AuthenticodeSigningRequest? LastRequest { get; private set; }

        public CodeSigningResult Sign(AuthenticodeSigningRequest request)
        {
            LastRequest = request;
            return new CodeSigningResult
            {
                Success = true,
                VerificationSummary = "verified",
                ToolPath = "fake-signtool",
                ToolVersion = "10.0.26100.1",
                CertificateSubject = "CN=ACME Release",
                CertificateIssuer = "CN=ACME Root",
                CertificateThumbprint = "00112233445566778899AABBCCDDEEFF00112233",
                CertificateStoreName = request.StoreName,
                CertificateStoreLocation = request.StoreLocation,
                CertificateStoreThumbprint = request.StoreThumbprint,
                CertificateStoreSubject = request.StoreSubject,
                SignatureDigestAlgorithm = "SHA256",
                FileDigestSha256 = "AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899",
                Timestamped = true,
                TimestampDescription = "2026-09-02T00:00:00Z",
                TimestampCertificateSubject = "CN=Trusted Timestamp Authority",
                TimestampOutagePolicy = request.TimestampOutagePolicy,
                TimestampRetryCount = request.TimestampRetryCount,
                RemoteProvider = request.RemoteProvider,
                RemoteEndpoint = request.RemoteEndpoint,
                RemoteKeyId = request.RemoteKeyId,
                AuditEvents =
                {
                    new CodeSigningAuditEvent
                    {
                        EventType = string.IsNullOrWhiteSpace(request.RemoteEndpoint) ? "local-sign.completed" : "remote-sign.completed",
                        ArtifactPath = request.FilePath,
                        ArtifactSha256 = "AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899",
                        Provider = request.RemoteProvider ?? "",
                        Endpoint = request.RemoteEndpoint ?? "",
                        KeyId = request.RemoteKeyId ?? "",
                        ToolPath = "fake-signtool",
                        ToolVersion = "10.0.26100.1",
                        Success = true
                    }
                }
            };
        }
    }

    private sealed class FixedSecretProvider : ISecretProvider
    {
        private readonly string _scheme;
        private readonly string? _value;

        public FixedSecretProvider(string scheme, string? value)
        {
            _scheme = scheme;
            _value = value;
        }

        public bool Supports(string scheme)
            => scheme.Equals(_scheme, StringComparison.OrdinalIgnoreCase);

        public SecretResolutionResult Resolve(SecretReference reference)
            => _value is null
                ? SecretResolutionResult.Failed($"Secret '{reference.Name}' is not available.")
                : SecretResolutionResult.Found(_value);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SigningQualificationReport))]
[JsonSerializable(typeof(SigningQualificationScenario))]
[JsonSerializable(typeof(CodeSigningResult))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class SigningQualificationJsonContext : JsonSerializerContext;
