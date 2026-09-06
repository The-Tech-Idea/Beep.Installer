using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beep.Installer.Engine;

namespace Beep.Installer.Deployment;

public sealed class ReleaseEvidenceAttestationResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = "";
    public string PayloadSha256 { get; init; } = "";
    public string KeyId { get; init; } = "";
}

public static class ReleaseEvidenceAttestation
{
    public const string EnvelopeVersion = "DSSEv1";

    public static string WriteDsseEnvelope(
        string payloadPath,
        string payloadType,
        string privateKeyPath,
        string keyId,
        string? envelopePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadType);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPath);

        var fullPayloadPath = Path.GetFullPath(payloadPath);
        var fullPrivateKeyPath = Path.GetFullPath(privateKeyPath);
        if (!File.Exists(fullPayloadPath))
            throw new FileNotFoundException("Attestation payload was not found.", fullPayloadPath);
        if (!File.Exists(fullPrivateKeyPath))
            throw new FileNotFoundException("Attestation private key was not found.", fullPrivateKeyPath);

        var payload = File.ReadAllBytes(fullPayloadPath);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(fullPrivateKeyPath));
        var signature = rsa.SignData(Pae(payloadType, payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var envelope = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["payloadType"] = payloadType,
            ["payload"] = Convert.ToBase64String(payload),
            ["signatures"] = new[]
            {
                new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["keyid"] = keyId ?? "",
                    ["sig"] = Convert.ToBase64String(signature)
                }
            }
        };

        var outputPath = Path.GetFullPath(string.IsNullOrWhiteSpace(envelopePath) ? fullPayloadPath + ".dsse.json" : envelopePath!);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
        return outputPath;
    }

    public static ReleaseEvidenceAttestationResult VerifyDsseEnvelope(
        string payloadPath,
        string payloadType,
        string envelopePath,
        IEnumerable<string> trustedPublicKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadType);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelopePath);
        ArgumentNullException.ThrowIfNull(trustedPublicKeys);

        var fullPayloadPath = Path.GetFullPath(payloadPath);
        var fullEnvelopePath = Path.GetFullPath(envelopePath);
        if (!File.Exists(fullPayloadPath))
            return Failure($"Attestation payload was not found: {fullPayloadPath}");
        if (!File.Exists(fullEnvelopePath))
            return Failure($"DSSE envelope was not found: {fullEnvelopePath}");

        var keys = trustedPublicKeys.Where(key => !string.IsNullOrWhiteSpace(key)).ToList();
        if (keys.Count == 0)
            return Failure("No trusted DSSE public keys are configured.");

        try
        {
            var payload = File.ReadAllBytes(fullPayloadPath);
            using var document = JsonDocument.Parse(File.ReadAllText(fullEnvelopePath));
            var root = document.RootElement;
            if (!root.TryGetProperty("payloadType", out var payloadTypeElement)
                || !string.Equals(payloadTypeElement.GetString(), payloadType, StringComparison.Ordinal))
            {
                return Failure($"DSSE payloadType mismatch. Expected '{payloadType}'.");
            }

            if (!root.TryGetProperty("payload", out var payloadElement) || payloadElement.ValueKind != JsonValueKind.String)
                return Failure("DSSE envelope has no base64 payload.");

            var envelopePayload = Convert.FromBase64String(payloadElement.GetString() ?? "");
            if (!payload.SequenceEqual(envelopePayload))
                return Failure("DSSE envelope payload does not match the evidence file.");

            if (!root.TryGetProperty("signatures", out var signatures) || signatures.ValueKind != JsonValueKind.Array)
                return Failure("DSSE envelope has no signatures array.");

            var pae = Pae(payloadType, envelopePayload);
            foreach (var signatureElement in signatures.EnumerateArray())
            {
                if (!signatureElement.TryGetProperty("sig", out var sigElement) || sigElement.ValueKind != JsonValueKind.String)
                    continue;

                var signature = Convert.FromBase64String(sigElement.GetString() ?? "");
                foreach (var key in keys)
                {
                    try
                    {
                        using var rsa = RSA.Create();
                        rsa.ImportFromPem(key);
                        if (rsa.VerifyData(pae, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                        {
                            return new ReleaseEvidenceAttestationResult
                            {
                                Success = true,
                                PayloadSha256 = Sha256Bytes(envelopePayload).ToLowerInvariant(),
                                KeyId = signatureElement.TryGetProperty("keyid", out var keyId) ? keyId.GetString() ?? "" : ""
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        Diag.Debug("ReleaseEvidenceAttestation", "DSSE public key skipped during verification", ex);
                    }
                }
            }

            return Failure("DSSE signature did not verify against any trusted public key.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            return Failure($"DSSE verification failed: {ex.Message}");
        }
    }

    private static byte[] Pae(string payloadType, byte[] payload)
    {
        using var ms = new MemoryStream();
        WriteAscii(ms, EnvelopeVersion);
        ms.WriteByte((byte)' ');
        var payloadTypeBytes = Encoding.UTF8.GetBytes(payloadType);
        WriteAscii(ms, payloadTypeBytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        ms.WriteByte((byte)' ');
        ms.Write(payloadTypeBytes);
        ms.WriteByte((byte)' ');
        WriteAscii(ms, payload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        ms.WriteByte((byte)' ');
        ms.Write(payload);
        return ms.ToArray();
    }

    private static void WriteAscii(Stream stream, string value)
        => stream.Write(Encoding.ASCII.GetBytes(value));

    private static string Sha256Bytes(byte[] value)
        => Convert.ToHexString(SHA256.HashData(value));

    private static ReleaseEvidenceAttestationResult Failure(string error)
        => new()
        {
            Success = false,
            Error = error
        };
}
