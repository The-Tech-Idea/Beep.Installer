using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;

namespace Beep.Installer.Engine;

public sealed record AuthenticodeSigningRequest(
    string FilePath,
    string CertificatePath,
    string? CertificatePassword,
    string? TimestampUrl,
    string? ExpectedSubject = null,
    bool VerifyAfterSign = true,
    string? StoreName = null,
    string? StoreLocation = null,
    string? StoreThumbprint = null,
    string? StoreSubject = null,
    string? TimestampOutagePolicy = null,
    int TimestampRetryCount = 2,
    string? RemoteProvider = null,
    string? RemoteEndpoint = null,
    string? RemoteKeyId = null,
    string? RemoteCredential = null);

public sealed class CodeSigningResult
{
    public bool Success { get; init; }
    public bool PasswordWasSecretReference { get; init; }
    public string? Error { get; init; }
    public string? VerificationSummary { get; init; }
    public string? ToolPath { get; init; }
    public string? ToolVersion { get; init; }
    public string? CertificateSubject { get; init; }
    public string? CertificateIssuer { get; init; }
    public string? CertificateThumbprint { get; init; }
    public string? CertificateStoreName { get; init; }
    public string? CertificateStoreLocation { get; init; }
    public string? CertificateStoreThumbprint { get; init; }
    public string? CertificateStoreSubject { get; init; }
    public DateTimeOffset? CertificateNotBeforeUtc { get; init; }
    public DateTimeOffset? CertificateNotAfterUtc { get; init; }
    public string? SignatureDigestAlgorithm { get; init; }
    public string? FileDigestSha256 { get; init; }
    public bool? Timestamped { get; init; }
    public string? TimestampDescription { get; init; }
    public string? TimestampCertificateSubject { get; init; }
    public string? TimestampCertificateIssuer { get; init; }
    public string? TimestampCertificateThumbprint { get; init; }
    public string? TimestampOutagePolicy { get; init; }
    public int TimestampRetryCount { get; init; }
    public string? TimestampPolicyWarning { get; init; }
    public string? RemoteProvider { get; init; }
    public string? RemoteEndpoint { get; init; }
    public string? RemoteKeyId { get; init; }
    public bool RemoteCredentialWasSecretReference { get; init; }
    public List<CodeSigningAuditEvent> AuditEvents { get; init; } = new();
}

public sealed class CodeSigningAuditEvent
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string EventType { get; init; } = "";
    public string ArtifactPath { get; init; } = "";
    public string ArtifactSha256 { get; init; } = "";
    public string Provider { get; init; } = "";
    public string Endpoint { get; init; } = "";
    public string KeyId { get; init; } = "";
    public string ToolPath { get; init; } = "";
    public string ToolVersion { get; init; } = "";
    public bool Success { get; init; }
    public string Error { get; init; } = "";
}

public interface IAuthenticodeSigner
{
    CodeSigningResult Sign(AuthenticodeSigningRequest request);
}

public sealed class CodeSigningService
{
    private readonly IAuthenticodeSigner _signer;
    private readonly ISecretProvider _secretProvider;

    public CodeSigningService()
        : this(new SigningProviderRouter(new SignToolAuthenticodeSigner(), new HttpRemoteAuthenticodeSigner()), new CompositeSecretProvider())
    {
    }

    public CodeSigningService(IAuthenticodeSigner signer, ISecretProvider? secretProvider = null)
    {
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _secretProvider = secretProvider ?? new CompositeSecretProvider();
    }

    public CodeSigningResult SignInstaller(
        string installerPath,
        string certificatePath,
        string? certificatePassword,
        string? timestampUrl,
        string? expectedSubject = null,
        bool verifyAfterSign = true,
        string? storeName = null,
        string? storeLocation = null,
        string? storeThumbprint = null,
        string? storeSubject = null,
        string? timestampOutagePolicy = null,
        int timestampRetryCount = 2,
        string? remoteProvider = null,
        string? remoteEndpoint = null,
        string? remoteKeyId = null,
        string? remoteCredential = null)
    {
        if (string.IsNullOrWhiteSpace(installerPath))
            return new CodeSigningResult { Error = "Installer path is required for code signing." };

        var hasPfx = !string.IsNullOrWhiteSpace(certificatePath);
        var hasStoreSelector = !string.IsNullOrWhiteSpace(storeThumbprint) || !string.IsNullOrWhiteSpace(storeSubject);
        var hasRemoteSelector = !string.IsNullOrWhiteSpace(remoteEndpoint);
        var selectorCount = (hasPfx ? 1 : 0) + (hasStoreSelector ? 1 : 0) + (hasRemoteSelector ? 1 : 0);
        if (selectorCount == 0)
            return new CodeSigningResult { Error = "A PFX certificate path, Windows certificate-store selector or remote signing endpoint is required for code signing." };
        if (selectorCount > 1)
            return new CodeSigningResult { Error = "Configure exactly one signing source: PFX certificate path, Windows certificate-store selector or remote signing endpoint." };

        var normalizedTimestampPolicy = NormalizeTimestampOutagePolicy(timestampOutagePolicy);
        if (normalizedTimestampPolicy is null)
            return new CodeSigningResult { Error = "Timestamp outage policy must be 'fail', 'warn' or 'retry'." };

        var resolvedPassword = certificatePassword;
        var wasSecretReference = false;
        if (hasPfx && SecretReference.IsReference(certificatePassword))
        {
            wasSecretReference = true;
            if (!SecretReference.TryParse(certificatePassword, out var reference, out var parseError))
                return new CodeSigningResult { PasswordWasSecretReference = true, Error = parseError };

            var resolved = _secretProvider.Resolve(reference);
            if (!resolved.Success)
                return new CodeSigningResult { PasswordWasSecretReference = true, Error = resolved.Error };
            resolvedPassword = resolved.Value;
        }

        var resolvedRemoteCredential = remoteCredential;
        var remoteCredentialWasSecretReference = false;
        if (hasRemoteSelector && SecretReference.IsReference(remoteCredential))
        {
            remoteCredentialWasSecretReference = true;
            if (!SecretReference.TryParse(remoteCredential, out var reference, out var parseError))
                return new CodeSigningResult { RemoteCredentialWasSecretReference = true, Error = parseError };

            var resolved = _secretProvider.Resolve(reference);
            if (!resolved.Success)
                return new CodeSigningResult { RemoteCredentialWasSecretReference = true, Error = resolved.Error };
            resolvedRemoteCredential = resolved.Value;
        }

        var certificateMetadata = hasPfx ? TryReadCertificateMetadata(certificatePath, resolvedPassword) : null;
        var result = _signer.Sign(new AuthenticodeSigningRequest(
            installerPath,
            certificatePath,
            resolvedPassword,
            timestampUrl,
            expectedSubject,
            verifyAfterSign,
            NormalizeStoreName(storeName),
            NormalizeStoreLocation(storeLocation),
            NormalizeThumbprint(storeThumbprint),
            storeSubject,
            normalizedTimestampPolicy,
            Math.Max(0, timestampRetryCount),
            NormalizeRemoteProvider(remoteProvider),
            remoteEndpoint,
            remoteKeyId,
            resolvedRemoteCredential));

        return new CodeSigningResult
        {
            Success = result.Success,
            PasswordWasSecretReference = wasSecretReference,
            Error = result.Error,
            VerificationSummary = result.VerificationSummary,
            ToolPath = result.ToolPath,
            ToolVersion = result.ToolVersion,
            CertificateSubject = FirstNonEmpty(result.CertificateSubject, certificateMetadata?.Subject),
            CertificateIssuer = FirstNonEmpty(result.CertificateIssuer, certificateMetadata?.Issuer),
            CertificateThumbprint = FirstNonEmpty(result.CertificateThumbprint, certificateMetadata?.Thumbprint),
            CertificateStoreName = result.CertificateStoreName ?? NormalizeStoreName(storeName),
            CertificateStoreLocation = result.CertificateStoreLocation ?? NormalizeStoreLocation(storeLocation),
            CertificateStoreThumbprint = result.CertificateStoreThumbprint ?? NormalizeThumbprint(storeThumbprint),
            CertificateStoreSubject = result.CertificateStoreSubject ?? storeSubject,
            CertificateNotBeforeUtc = result.CertificateNotBeforeUtc ?? certificateMetadata?.NotBeforeUtc,
            CertificateNotAfterUtc = result.CertificateNotAfterUtc ?? certificateMetadata?.NotAfterUtc,
            SignatureDigestAlgorithm = result.SignatureDigestAlgorithm,
            FileDigestSha256 = result.FileDigestSha256,
            Timestamped = result.Timestamped,
            TimestampDescription = result.TimestampDescription,
            TimestampCertificateSubject = result.TimestampCertificateSubject,
            TimestampCertificateIssuer = result.TimestampCertificateIssuer,
            TimestampCertificateThumbprint = result.TimestampCertificateThumbprint,
            TimestampOutagePolicy = result.TimestampOutagePolicy ?? normalizedTimestampPolicy,
            TimestampRetryCount = result.TimestampRetryCount,
            TimestampPolicyWarning = result.TimestampPolicyWarning,
            RemoteProvider = result.RemoteProvider ?? NormalizeRemoteProvider(remoteProvider),
            RemoteEndpoint = result.RemoteEndpoint ?? remoteEndpoint,
            RemoteKeyId = result.RemoteKeyId ?? remoteKeyId,
            RemoteCredentialWasSecretReference = remoteCredentialWasSecretReference,
            AuditEvents = result.AuditEvents
        };
    }

    private static CertificateMetadata? TryReadCertificateMetadata(string certificatePath, string? password)
    {
        try
        {
            var cert = string.IsNullOrEmpty(password)
                ? X509CertificateLoader.LoadPkcs12FromFile(certificatePath, null)
                : X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password);
            using (cert)
            {
                return new CertificateMetadata(
                    cert.Subject,
                    cert.Issuer,
                    cert.Thumbprint ?? "",
                    new DateTimeOffset(cert.NotBefore.ToUniversalTime(), TimeSpan.Zero),
                    new DateTimeOffset(cert.NotAfter.ToUniversalTime(), TimeSpan.Zero));
            }
        }
        catch (Exception ex)
        {
            Diag.Debug("CodeSigningService", "certificate metadata inspection failed", ex);
            return null;
        }
    }

    private static string? FirstNonEmpty(string? first, string? second)
        => string.IsNullOrWhiteSpace(first) ? second : first;

    private static string NormalizeStoreName(string? value)
        => string.IsNullOrWhiteSpace(value) ? "My" : value.Trim();

    private static string NormalizeStoreLocation(string? value)
        => string.IsNullOrWhiteSpace(value) ? "CurrentUser" : value.Trim();

    private static string NormalizeThumbprint(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

    private static string? NormalizeTimestampOutagePolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "fail";
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "fail" or "warn" or "retry" ? normalized : null;
    }

    private static string NormalizeRemoteProvider(string? value)
        => string.IsNullOrWhiteSpace(value) ? "http" : value.Trim();

    private sealed record CertificateMetadata(
        string Subject,
        string Issuer,
        string Thumbprint,
        DateTimeOffset NotBeforeUtc,
        DateTimeOffset NotAfterUtc);
}
