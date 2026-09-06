using System.Security.Cryptography;
using System.Text;

namespace Beep.Installer.Security;

public sealed class DetachedSignatureVerification
{
    public bool Trusted { get; init; }
    public string Status { get; init; } = "";
    public string Error { get; init; } = "";
}

public static class RsaSha256DetachedSignatureVerifier
{
    public const string SignaturePrefix = "rsa-sha256:";

    public static DetachedSignatureVerification VerifyUtf8Payload(
        string payload,
        string signature,
        IEnumerable<string> trustedPublicKeys,
        string trustPurpose)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(trustedPublicKeys);

        var trustedKeys = trustedPublicKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToList();
        if (trustedKeys.Count == 0)
            return Untrusted($"No trusted {trustPurpose} public keys are configured.");

        var normalizedSignature = signature.StartsWith(SignaturePrefix, StringComparison.OrdinalIgnoreCase)
            ? signature[SignaturePrefix.Length..]
            : signature;

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(normalizedSignature);
        }
        catch (FormatException ex)
        {
            return Untrusted($"{trustPurpose} signature is not valid base64: {ex.Message}");
        }

        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        foreach (var key in trustedKeys)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(key);
                if (rsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    return new DetachedSignatureVerification
                    {
                        Trusted = true,
                        Status = "Valid"
                    };
                }
            }
            catch (Exception ex)
            {
                Beep.Installer.Engine.Diag.Debug("RsaSha256DetachedSignatureVerifier", $"{trustPurpose} public key skipped during verification", ex);
            }
        }

        return Untrusted($"Signature did not verify against any trusted {trustPurpose} public key.");
    }

    private static DetachedSignatureVerification Untrusted(string error)
        => new()
        {
            Trusted = false,
            Status = "Untrusted",
            Error = error
        };
}
