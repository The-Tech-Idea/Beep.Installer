using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Models;
using Beep.Installer.Security;

namespace Beep.Installer.Engine;

public sealed class ProjectTemplatePackageOptions
{
    public string TemplateId { get; init; } = ProjectTemplates.EmptyId;
    public string ProductName { get; init; } = "TemplateApp";
    public string ProductVersion { get; init; } = "1.0.0";
    public string Publisher { get; init; } = "The Tech Idea";
    public string SourceDirectory { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string Issuer { get; init; } = "";
    public string SigningPrivateKeyPath { get; init; } = "";
}

public sealed class ProjectTemplatePackageResult
{
    public string PackageDirectory { get; init; } = "";
    public string ManifestPath { get; init; } = "";
    public string ProjectPath { get; init; } = "";
    public string SignaturePath { get; init; } = "";
    public string TemplateId { get; init; } = "";
    public string TemplateHash { get; init; } = "";
    public string PublicKeySha256 { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public bool Success => Diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
}

public sealed class ProjectTemplatePackageVerificationOptions
{
    public string PackageDirectory { get; init; } = "";
    public string TrustedPublicKey { get; init; } = "";
    public string TrustedPublicKeyPath { get; init; } = "";
    public bool RequireSignature { get; init; } = true;
}

public sealed class ProjectTemplatePackageVerificationResult
{
    public ProjectTemplatePackageManifest? Manifest { get; init; }
    public string ProjectPath { get; init; } = "";
    public string ActualProjectSha256 { get; init; } = "";
    public bool SignatureTrusted { get; init; }
    public string TrustedKeySha256 { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public bool Success => Diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
}

public sealed class ProjectTemplatePackageManifest
{
    public string SchemaVersion { get; init; } = "1.0";
    public string TemplateId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public string Issuer { get; init; } = "";
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string ProjectFile { get; init; } = ProjectTemplatePackageService.ProjectFileName;
    public string ProjectSha256 { get; init; } = "";
    public string SignatureFile { get; init; } = ProjectTemplatePackageService.SignatureFileName;
    public string SignatureAlgorithm { get; init; } = "rsa-sha256";
    public string PublicKeySha256 { get; init; } = "";
}

public static class ProjectTemplatePackageService
{
    public const string ManifestFileName = "beep-project-template.json";
    public const string ProjectFileName = "template.project.canonical.json";
    public const string SignatureFileName = "beep-project-template.json.sig";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public static string ToJson(ProjectTemplatePackageResult result)
        => JsonSerializer.Serialize(result, ProjectTemplatePackageJsonContext.Default.ProjectTemplatePackageResult);

    public static string ToJson(ProjectTemplatePackageVerificationResult result)
        => JsonSerializer.Serialize(result, ProjectTemplatePackageJsonContext.Default.ProjectTemplatePackageVerificationResult);

    public static ProjectTemplatePackageResult Export(ProjectTemplatePackageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var diagnostics = new List<ProjectSchemaDiagnostic>();

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            diagnostics.Add(Error("BI2501", "Template.OutputDirectory", "Project template package export requires an output directory."));
        if (string.IsNullOrWhiteSpace(options.SigningPrivateKeyPath))
            diagnostics.Add(Error("BI2502", "Template.SigningPrivateKeyPath", "Project template package export requires a signing private key."));
        if (!string.IsNullOrWhiteSpace(options.SigningPrivateKeyPath) && !File.Exists(options.SigningPrivateKeyPath))
            diagnostics.Add(Error("BI2503", options.SigningPrivateKeyPath, "Project template package signing private key was not found."));
        if (diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
            return new ProjectTemplatePackageResult { Diagnostics = diagnostics };

        var template = ProjectTemplates.Builtins.FirstOrDefault(t => string.Equals(t.Id, options.TemplateId, StringComparison.OrdinalIgnoreCase));
        if (template == null)
        {
            diagnostics.Add(Error("BI2505", "Template.TemplateId", $"Unknown project template '{options.TemplateId}'."));
            return new ProjectTemplatePackageResult { Diagnostics = diagnostics };
        }
        var project = ProjectTemplates.Create(template.Id, options.ProductName, options.ProductVersion, options.Publisher, options.SourceDirectory);
        var packageDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(packageDirectory);

        var projectPath = Path.Combine(packageDirectory, ProjectFileName);
        var projectJson = ProjectCanonicalJsonExporter.ToJson(project);
        var projectBytes = Utf8NoBom.GetBytes(projectJson);
        File.WriteAllBytes(projectPath, projectBytes);
        var projectSha256 = Sha256Hex(projectBytes);

        var signaturePath = Path.Combine(packageDirectory, SignatureFileName);
        string publicKeySha256;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(Path.GetFullPath(options.SigningPrivateKeyPath)));
            publicKeySha256 = Sha256Hex(rsa.ExportSubjectPublicKeyInfo());

            var manifest = new ProjectTemplatePackageManifest
            {
                TemplateId = template.Id,
                Name = template.Name,
                Category = template.Category,
                Description = template.Description,
                Issuer = string.IsNullOrWhiteSpace(options.Issuer) ? options.Publisher : options.Issuer,
                ProjectSha256 = projectSha256,
                PublicKeySha256 = publicKeySha256
            };
            var manifestPath = Path.Combine(packageDirectory, ManifestFileName);
            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine;
            var manifestBytes = Utf8NoBom.GetBytes(manifestJson);
            File.WriteAllBytes(manifestPath, manifestBytes);

            var signature = RsaSha256DetachedSignatureVerifier.SignaturePrefix
                            + Convert.ToBase64String(rsa.SignData(manifestBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            File.WriteAllText(signaturePath, signature, Utf8NoBom);

            return new ProjectTemplatePackageResult
            {
                PackageDirectory = packageDirectory,
                ManifestPath = manifestPath,
                ProjectPath = projectPath,
                SignaturePath = signaturePath,
                TemplateId = template.Id,
                TemplateHash = projectSha256,
                PublicKeySha256 = publicKeySha256,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("BI2504", "Template.Signature", $"Project template package could not be signed: {ex.Message}"));
            return new ProjectTemplatePackageResult
            {
                PackageDirectory = packageDirectory,
                ProjectPath = projectPath,
                SignaturePath = signaturePath,
                TemplateId = template.Id,
                TemplateHash = projectSha256,
                Diagnostics = diagnostics
            };
        }
    }

    public static ProjectTemplatePackageVerificationResult Verify(ProjectTemplatePackageVerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var packageDirectory = Path.GetFullPath(options.PackageDirectory);
        var manifestPath = Path.Combine(packageDirectory, ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            diagnostics.Add(Error("BI2510", manifestPath, "Project template package manifest was not found."));
            return new ProjectTemplatePackageVerificationResult { Diagnostics = diagnostics };
        }

        ProjectTemplatePackageManifest? manifest;
        var manifestJson = File.ReadAllText(manifestPath, Utf8NoBom);
        try
        {
            manifest = JsonSerializer.Deserialize<ProjectTemplatePackageManifest>(manifestJson, JsonOptions);
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("BI2511", manifestPath, $"Project template package manifest could not be read: {ex.Message}"));
            return new ProjectTemplatePackageVerificationResult { Diagnostics = diagnostics };
        }

        if (manifest == null)
        {
            diagnostics.Add(Error("BI2512", manifestPath, "Project template package manifest is empty."));
            return new ProjectTemplatePackageVerificationResult { Diagnostics = diagnostics };
        }

        var projectPath = Path.Combine(packageDirectory, string.IsNullOrWhiteSpace(manifest.ProjectFile) ? ProjectFileName : manifest.ProjectFile);
        if (!File.Exists(projectPath))
        {
            diagnostics.Add(Error("BI2513", projectPath, "Project template canonical JSON was not found."));
        }

        var signaturePath = Path.Combine(packageDirectory, string.IsNullOrWhiteSpace(manifest.SignatureFile) ? SignatureFileName : manifest.SignatureFile);
        var trustedKey = TrustedPublicKey(options);
        var signatureTrusted = false;
        var trustedKeySha256 = string.IsNullOrWhiteSpace(trustedKey) ? "" : Sha256Hex(PublicKeyBytes(trustedKey));

        if (options.RequireSignature)
        {
            if (!File.Exists(signaturePath))
            {
                diagnostics.Add(Error("BI2514", signaturePath, "Project template package signature was not found."));
            }
            else if (string.IsNullOrWhiteSpace(trustedKey))
            {
                diagnostics.Add(Error("BI2515", "Template.TrustedPublicKey", "Project template package verification requires TrustedPublicKey or TrustedPublicKeyPath."));
            }
            else
            {
                var verification = RsaSha256DetachedSignatureVerifier.VerifyUtf8Payload(
                    manifestJson,
                    File.ReadAllText(signaturePath, Utf8NoBom),
                    new[] { trustedKey },
                    "project template package");
                signatureTrusted = verification.Trusted;
                if (!verification.Trusted)
                    diagnostics.Add(Error("BI2516", signaturePath, verification.Error));
            }
        }

        var actualProjectSha256 = "";
        if (File.Exists(projectPath))
        {
            actualProjectSha256 = Sha256Hex(File.ReadAllBytes(projectPath));
            if (!string.Equals(actualProjectSha256, manifest.ProjectSha256, StringComparison.OrdinalIgnoreCase))
                diagnostics.Add(Error("BI2517", projectPath, "Project template canonical JSON hash does not match the signed manifest."));
        }

        if (!string.IsNullOrWhiteSpace(manifest.PublicKeySha256)
            && !string.IsNullOrWhiteSpace(trustedKeySha256)
            && !string.Equals(manifest.PublicKeySha256, trustedKeySha256, StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(Error("BI2518", "Template.PublicKeySha256", "Project template package was signed by a different key than the trusted public key."));

        return new ProjectTemplatePackageVerificationResult
        {
            Manifest = manifest,
            ProjectPath = projectPath,
            ActualProjectSha256 = actualProjectSha256,
            SignatureTrusted = signatureTrusted,
            TrustedKeySha256 = trustedKeySha256,
            Diagnostics = diagnostics
        };
    }

    private static string TrustedPublicKey(ProjectTemplatePackageVerificationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.TrustedPublicKey))
            return options.TrustedPublicKey;
        return !string.IsNullOrWhiteSpace(options.TrustedPublicKeyPath) && File.Exists(options.TrustedPublicKeyPath)
            ? File.ReadAllText(Path.GetFullPath(options.TrustedPublicKeyPath))
            : "";
    }

    private static byte[] PublicKeyBytes(string pem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa.ExportSubjectPublicKeyInfo();
    }

    private static string Sha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ProjectTemplatePackageResult))]
[JsonSerializable(typeof(ProjectTemplatePackageVerificationResult))]
[JsonSerializable(typeof(ProjectTemplatePackageManifest))]
[JsonSerializable(typeof(ProjectSchemaDiagnostic))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class ProjectTemplatePackageJsonContext : JsonSerializerContext;
