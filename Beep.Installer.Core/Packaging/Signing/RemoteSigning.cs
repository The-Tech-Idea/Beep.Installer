using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beep.Installer.Engine;

public sealed class SigningProviderRouter : IAuthenticodeSigner
{
    private readonly IAuthenticodeSigner _localSigner;
    private readonly IAuthenticodeSigner _remoteSigner;

    public SigningProviderRouter(IAuthenticodeSigner localSigner, IAuthenticodeSigner remoteSigner)
    {
        _localSigner = localSigner ?? throw new ArgumentNullException(nameof(localSigner));
        _remoteSigner = remoteSigner ?? throw new ArgumentNullException(nameof(remoteSigner));
    }

    public CodeSigningResult Sign(AuthenticodeSigningRequest request)
        => string.IsNullOrWhiteSpace(request.RemoteEndpoint)
            ? _localSigner.Sign(request)
            : _remoteSigner.Sign(request);
}

public sealed class HttpRemoteAuthenticodeSigner : IAuthenticodeSigner
{
    private readonly HttpClient _httpClient;

    public HttpRemoteAuthenticodeSigner()
        : this(new HttpClient())
    {
    }

    public HttpRemoteAuthenticodeSigner(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public CodeSigningResult Sign(AuthenticodeSigningRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RemoteEndpoint))
            return new CodeSigningResult { Error = "Remote signing endpoint is required." };
        if (!File.Exists(request.FilePath))
            return new CodeSigningResult { Error = $"Remote signing artifact not found: {request.FilePath}" };

        var artifactSha256 = Sha256File(request.FilePath);
        var auditStart = Audit("remote-sign.requested", request, artifactSha256, success: true);

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.RemoteEndpoint);
            if (!string.IsNullOrWhiteSpace(request.RemoteCredential))
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.RemoteCredential);

            var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = "1.0",
                ["artifactName"] = Path.GetFileName(request.FilePath),
                ["artifactSha256"] = artifactSha256,
                ["keyId"] = request.RemoteKeyId,
                ["timestampUrl"] = request.TimestampUrl,
                ["timestampOutagePolicy"] = request.TimestampOutagePolicy,
                ["expectedSubject"] = request.ExpectedSubject,
                ["signatureDigestAlgorithm"] = "SHA256"
            };
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = _httpClient.Send(httpRequest);
            using var responseStream = response.Content.ReadAsStream();
            using var responseReader = new StreamReader(responseStream, Encoding.UTF8);
            var responseText = responseReader.ReadToEnd();
            if (!response.IsSuccessStatusCode)
            {
                return Failure(
                    request,
                    artifactSha256,
                    $"Remote signer returned {(int)response.StatusCode}: {Trim(responseText)}",
                    auditStart);
            }

            using var json = JsonDocument.Parse(responseText);
            var root = json.RootElement;
            if (root.TryGetProperty("success", out var successElement)
                && successElement.ValueKind == JsonValueKind.False)
            {
                return Failure(request, artifactSha256, Value(root, "error", "Remote signer rejected the signing request."), auditStart);
            }

            var signedArtifact = Value(root, "signedArtifactBase64");
            if (string.IsNullOrWhiteSpace(signedArtifact))
                signedArtifact = Value(root, "signedContentBase64");
            if (string.IsNullOrWhiteSpace(signedArtifact))
                return Failure(request, artifactSha256, "Remote signer response did not include signedArtifactBase64.", auditStart);

            File.WriteAllBytes(request.FilePath, Convert.FromBase64String(signedArtifact));
            var signedSha256 = Sha256File(request.FilePath);
            var timestamped = Bool(root, "timestamped");
            var result = new CodeSigningResult
            {
                Success = true,
                ToolPath = request.RemoteEndpoint,
                ToolVersion = Value(root, "providerVersion"),
                CertificateSubject = Value(root, "certificateSubject"),
                CertificateIssuer = Value(root, "certificateIssuer"),
                CertificateThumbprint = NormalizeThumbprint(Value(root, "certificateThumbprint")),
                SignatureDigestAlgorithm = Value(root, "signatureDigestAlgorithm", "SHA256").ToUpperInvariant(),
                FileDigestSha256 = signedSha256,
                Timestamped = timestamped,
                TimestampDescription = Value(root, "timestampDescription"),
                TimestampCertificateSubject = Value(root, "timestampCertificateSubject"),
                TimestampCertificateIssuer = Value(root, "timestampCertificateIssuer"),
                TimestampCertificateThumbprint = NormalizeThumbprint(Value(root, "timestampCertificateThumbprint")),
                TimestampOutagePolicy = request.TimestampOutagePolicy,
                RemoteProvider = string.IsNullOrWhiteSpace(request.RemoteProvider) ? "http" : request.RemoteProvider,
                RemoteEndpoint = request.RemoteEndpoint,
                RemoteKeyId = request.RemoteKeyId,
                VerificationSummary = Value(root, "verificationSummary", "remote signer returned a signed artifact"),
                AuditEvents =
                {
                    auditStart,
                    Audit("remote-sign.completed", request, signedSha256, success: true)
                }
            };

            if (!string.IsNullOrWhiteSpace(request.ExpectedSubject)
                && (string.IsNullOrWhiteSpace(result.CertificateSubject)
                    || !result.CertificateSubject.Contains(request.ExpectedSubject, StringComparison.OrdinalIgnoreCase)))
            {
                return Failure(
                    request,
                    signedSha256,
                    $"Remote signer certificate subject '{result.CertificateSubject}' did not match expected subject '{request.ExpectedSubject}'.",
                    auditStart);
            }

            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException or FormatException)
        {
            return Failure(request, artifactSha256, $"Remote signing failed: {ex.Message}", auditStart);
        }
    }

    private static CodeSigningResult Failure(
        AuthenticodeSigningRequest request,
        string artifactSha256,
        string error,
        CodeSigningAuditEvent auditStart)
        => new()
        {
            Error = error,
            ToolPath = request.RemoteEndpoint,
            TimestampOutagePolicy = request.TimestampOutagePolicy,
            RemoteProvider = string.IsNullOrWhiteSpace(request.RemoteProvider) ? "http" : request.RemoteProvider,
            RemoteEndpoint = request.RemoteEndpoint,
            RemoteKeyId = request.RemoteKeyId,
            AuditEvents =
            {
                auditStart,
                Audit("remote-sign.failed", request, artifactSha256, success: false, error)
            }
        };

    private static CodeSigningAuditEvent Audit(
        string eventType,
        AuthenticodeSigningRequest request,
        string artifactSha256,
        bool success,
        string error = "")
        => new()
        {
            EventType = eventType,
            ArtifactPath = request.FilePath,
            ArtifactSha256 = artifactSha256,
            Provider = string.IsNullOrWhiteSpace(request.RemoteProvider) ? "http" : request.RemoteProvider,
            Endpoint = request.RemoteEndpoint ?? "",
            KeyId = request.RemoteKeyId ?? "",
            ToolPath = request.RemoteEndpoint ?? "",
            Success = success,
            Error = error
        };

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToUpperInvariant();
    }

    private static string Value(JsonElement root, string name, string fallback = "")
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static bool? Bool(JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;

    private static string NormalizeThumbprint(string value)
        => string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

    private static string Trim(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var singleLine = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return singleLine.Length <= 400 ? singleLine : singleLine[..400] + "…";
    }
}
