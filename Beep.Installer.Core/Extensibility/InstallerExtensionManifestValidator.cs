using System.Security.Cryptography;
using Beep.Installer.Engine;

namespace Beep.Installer.Extensibility;

public sealed class InstallerExtensionManifestValidator
{
    public string EngineVersion { get; }
    public bool RequireSignature { get; init; }
    public IReadOnlyList<string> TrustedPublicKeys { get; init; } = Array.Empty<string>();

    public InstallerExtensionManifestValidator(string engineVersion = "1.0.0")
    {
        EngineVersion = string.IsNullOrWhiteSpace(engineVersion) ? "1.0.0" : engineVersion;
    }

    public InstallerExtensionValidationResult Validate(InstallerExtensionReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var result = new InstallerExtensionValidationResult();
        var manifest = reference.Manifest;

        ValidateRequiredFields(manifest, result);
        ValidateVersions(manifest, result);
        ValidateCapabilities(manifest, result);
        ValidateEntryAssembly(reference, result);
        ValidateSignature(manifest, result);
        ValidateInventory(reference, result);

        return result;
    }

    private static void ValidateInventory(InstallerExtensionReference reference, InstallerExtensionValidationResult result)
    {
        var files = reference.Manifest.Files;
        if (files is null || files.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(reference.Manifest.Signature))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4042", "Extension.Files", "Signed extensions require a complete deployment file inventory.");
            return;
        }
        try
        {
            var actual = InstallerExtensionPackageInventory.Capture(reference.DirectoryPath, reference.Manifest.EntryAssembly);
            if (actual.Count != files.Count || actual.Any(file => !files.TryGetValue(file.Key, out var expected)
                || !file.Value.Equals(expected, StringComparison.OrdinalIgnoreCase)))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4043", "Extension.Files", "Extension deployment files do not match the manifest inventory.");
        }
        catch (Exception ex)
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4043", "Extension.Files", "Extension inventory verification failed: " + ex.Message);
        }
    }

    private void ValidateRequiredFields(InstallerExtensionManifest manifest, InstallerExtensionValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4001", "Extension.Id", "Extension Id is required.");
        if (string.IsNullOrWhiteSpace(manifest.Publisher))
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4002", "Extension.Publisher", "Extension Publisher is required.");
        if (string.IsNullOrWhiteSpace(manifest.Version))
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4003", "Extension.Version", "Extension Version is required.");
        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4004", "Extension.EntryAssembly", "EntryAssembly is required.");
    }

    private void ValidateVersions(InstallerExtensionManifest manifest, InstallerExtensionValidationResult result)
    {
        if (!TryParseVersion(manifest.Version, out _))
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4010", "Extension.Version", $"Extension Version '{manifest.Version}' is not a valid semantic version.");

        if (!TryParseVersion(EngineVersion, out var engine))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4011", "Engine.Version", $"Engine version '{EngineVersion}' is not valid.");
            return;
        }

        var minimumEngineVersion = manifest.MinimumEngineVersion ?? "1.0.0";
        if (!string.IsNullOrWhiteSpace(minimumEngineVersion))
        {
            if (!TryParseVersion(minimumEngineVersion, out var min))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4012", "Extension.MinimumEngineVersion", $"MinimumEngineVersion '{manifest.MinimumEngineVersion}' is not valid.");
            else if (engine < min)
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4013", "Extension.MinimumEngineVersion", $"Extension requires engine {manifest.MinimumEngineVersion} or newer.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.MaximumEngineVersion))
        {
            if (!TryParseVersion(manifest.MaximumEngineVersion, out var max))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4014", "Extension.MaximumEngineVersion", $"MaximumEngineVersion '{manifest.MaximumEngineVersion}' is not valid.");
            else if (engine > max)
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4015", "Extension.MaximumEngineVersion", $"Extension supports engine {manifest.MaximumEngineVersion} or older.");
        }
    }

    private static void ValidateCapabilities(InstallerExtensionManifest manifest, InstallerExtensionValidationResult result)
    {
        if (manifest.ResourceTypes.Count == 0 && manifest.ValidatorTypes.Count == 0 && manifest.ExporterFormats.Count == 0)
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4020", "Extension.Capabilities", "At least one resource type, validator type or exporter format is required.");
            return;
        }

        ValidateUniqueCapabilityList(manifest.ResourceTypes, "ResourceTypes", "resource type", "BI4021", "BI4022", result);
        ValidateUniqueCapabilityList(manifest.ValidatorTypes, "ValidatorTypes", "validator type", "BI4023", "BI4024", result);
        ValidateUniqueCapabilityList(manifest.ExporterFormats, "ExporterFormats", "exporter format", "BI4025", "BI4026", result);
    }

    private static void ValidateUniqueCapabilityList(
        IReadOnlyList<string> values,
        string property,
        string label,
        string emptyCode,
        string duplicateCode,
        InstallerExtensionValidationResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (string.IsNullOrWhiteSpace(value))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, emptyCode, $"Extension.{property}[{i}]", $"{label} is empty.");
                continue;
            }

            if (!seen.Add(value))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, duplicateCode, $"Extension.{property}[{i}]", $"Duplicate {label} '{value}'.");
        }
    }

    private void ValidateEntryAssembly(InstallerExtensionReference reference, InstallerExtensionValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(reference.Manifest.EntryAssembly))
            return;

        if (string.IsNullOrWhiteSpace(reference.EntryAssemblyPath) || !File.Exists(reference.EntryAssemblyPath))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4030", "Extension.EntryAssembly", $"Entry assembly was not found: {reference.Manifest.EntryAssembly}");
            return;
        }

        if (string.IsNullOrWhiteSpace(reference.Manifest.Sha256))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4031", "Extension.Sha256", "Extension Sha256 is missing.", "Publish the SHA-256 hash of EntryAssembly.");
            return;
        }

        var actual = ComputeSha256(reference.EntryAssemblyPath);
        if (!actual.Equals(reference.Manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            result.Add(
                ProjectSchemaDiagnosticSeverity.Error,
                "BI4032",
                "Extension.Sha256",
                "Extension entry assembly hash does not match the manifest.");
        }
    }

    private void ValidateSignature(InstallerExtensionManifest manifest, InstallerExtensionValidationResult result)
    {
        if (RequireSignature && string.IsNullOrWhiteSpace(manifest.Signature))
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4040", "Extension.Signature", "Extension signature is required by policy.");
        if (!string.IsNullOrWhiteSpace(manifest.Signature))
        {
            var verification = Beep.Installer.Security.RsaSha256DetachedSignatureVerifier.VerifyUtf8Payload(
                manifest.CanonicalSigningPayload(), manifest.Signature, TrustedPublicKeys, "extension manifest");
            if (!verification.Trusted)
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI4041", "Extension.Signature", verification.Error);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Split('-', '+')[0];
        return Version.TryParse(normalized, out version!);
    }
}
